using System;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>
    /// S2 binding: served/total and queue warnings for each owned need, stacked in a stable
    /// 3 -> 4 -> 5 order. Numeric satisfaction is never shown during a wave; tapping a need opens S3.
    /// </summary>
    public sealed class WaveNeedsPresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class NeedCard
        {
            public NeedType Need;
            public RectTransform Root;
            public TextMeshProUGUI Count;
            public Image ProgressFill;
            public TextMeshProUGUI Warning;
            public GameObject AlertIcon;
            public Image Icon;
            [Tooltip("Glyph icons take the status color; full-color building art keeps its own.")]
            public bool TintIcon = true;
            public Button Button;
            public UnityEngine.UI.Button SpeedUpButton;
            public TextMeshProUGUI SpeedUpLabel;
            public TextMeshProUGUI BoostTimer;
            [NonSerialized] public UnityAction SpeedUp;
            [NonSerialized] public string BoostFailure;
        }

        [SerializeField] private ResortGameplayBootstrap gameplay;
        [SerializeField] private ResortUiStateCoordinator navigation;
        [SerializeField] private RectTransform panel;
        [SerializeField] private UnityEngine.UI.ScrollRect needsScroll;
        [SerializeField] private TextMeshProUGUI heading;
        [SerializeField] private TextMeshProUGUI guests;
        [SerializeField] private RectTransform guestQuote;
        [SerializeField] private TextMeshProUGUI guestQuoteText;
        [SerializeField] private NeedCard[] cards;
        [SerializeField] private float firstCardTop = 150f;
        [SerializeField] private float cardSpacing = 24f;
        [SerializeField] private float cardHeight = 150f;
        [SerializeField] private float warningCardHeight = 209f;
        [SerializeField] private float refreshInterval = 0.1f;
        [SerializeField] private Color okColor = new Color(0.06f, 0.72f, 0.51f);
        [SerializeField] private Color recoveringColor = new Color(1f, 0.6f, 0f);
        [SerializeField] private Color bottleneckColor = new Color(0.96f, 0.25f, 0.27f);

        [Header("Sudden Rush")]
        [SerializeField] private RectTransform rushNotification;
        [SerializeField] private TextMeshProUGUI rushTitle;
        [SerializeField] private TextMeshProUGUI rushDescription;
        [SerializeField] private TextMeshProUGUI rushFeedback;
        [SerializeField] private UnityEngine.UI.Button moveWorkerButton;
        [SerializeField] private UnityEngine.UI.Button energyBoostButton;
        [SerializeField] private UnityEngine.UI.Button ignoreRushButton;

        private readonly WaveNeedStatusTracker tracker = new WaveNeedStatusTracker();
        private float nextRefresh;
        private string rushActionMessage;
        private bool scrollToTop;

        private void OnEnable()
        {
            scrollToTop = true;
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (navigation == null) navigation = FindAnyObjectByType<ResortUiStateCoordinator>();
            foreach (var card in cards)
            {
                if (card.Button != null) card.Button.onClick.AddListener(OpenWorkerAllocation);
                if (card.SpeedUpButton == null) continue;
                card.BoostFailure = null;
                card.SpeedUp = () => UseEnergyForCard(card);
                card.SpeedUpButton.onClick.AddListener(card.SpeedUp);
            }
            if (moveWorkerButton != null) moveWorkerButton.onClick.AddListener(MoveWorkerToRush);
            if (energyBoostButton != null) energyBoostButton.onClick.AddListener(UseEnergyForRush);
            if (ignoreRushButton != null) ignoreRushButton.onClick.AddListener(IgnoreRush);
            if (gameplay == null) return;
            gameplay.StateChanged += HandleStateChanged;
            gameplay.WorkerAssignmentChanged += HandleWorkerAssignmentChanged;
            Refresh();
        }

        private void OnDisable()
        {
            foreach (var card in cards)
            {
                if (card.Button != null) card.Button.onClick.RemoveListener(OpenWorkerAllocation);
                if (card.SpeedUpButton != null) card.SpeedUpButton.onClick.RemoveListener(card.SpeedUp);
            }
            if (moveWorkerButton != null) moveWorkerButton.onClick.RemoveListener(MoveWorkerToRush);
            if (energyBoostButton != null) energyBoostButton.onClick.RemoveListener(UseEnergyForRush);
            if (ignoreRushButton != null) ignoreRushButton.onClick.RemoveListener(IgnoreRush);
            if (gameplay == null) return;
            gameplay.StateChanged -= HandleStateChanged;
            gameplay.WorkerAssignmentChanged -= HandleWorkerAssignmentChanged;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            Refresh();
        }

        private void HandleStateChanged(GamePhase previous, GamePhase next) => Refresh();
        private void HandleWorkerAssignmentChanged(WorkerAssignmentChange change) => Refresh();

        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + refreshInterval;
            // Before the first wave (for example an F1 preview) the static layout stays untouched.
            var wave = tracker.Capture(gameplay);
            if (wave == null)
            {
                ResetScrollIfRequested();
                return;
            }

            SetText(heading, HeadingFor(gameplay.CurrentPhase, wave));
            SetText(guests, "GUESTS: " + wave.Served + "/" + wave.Total);

            NeedStatusSnapshot? worst = null;
            var top = RefreshRushNotification();
            foreach (var card in cards)
            {
                var status = Find(wave, card.Need);
                card.Root.gameObject.SetActive(status.HasValue);
                if (!status.HasValue) continue;
                var need = status.Value;
                var warning = WarningFor(need);
                var color = ColorFor(need.Status);
                SetText(card.Count, need.Served + "/" + need.Total + SuffixFor(need.Status));
                if (card.Count != null) card.Count.color = color;
                if (card.Icon != null && card.TintIcon) card.Icon.color = color;
                if (card.ProgressFill != null)
                {
                    card.ProgressFill.fillAmount = need.Total > 0 ? (float)need.Served / need.Total : 0f;
                    card.ProgressFill.color = color;
                }
                if (card.Warning != null)
                {
                    card.Warning.gameObject.SetActive(warning != null);
                    card.Warning.text = warning ?? string.Empty;
                }
                if (card.AlertIcon != null) card.AlertIcon.SetActive(need.Status == NeedServiceStatus.BOTTLENECK);
                RefreshBoost(card, need);

                var height = warning == null ? cardHeight : warningCardHeight;
                if (!Mathf.Approximately(card.Root.rect.height, height))
                    card.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                if (needsScroll == null)
                {
                    card.Root.anchoredPosition = new Vector2(card.Root.anchoredPosition.x, -(top + height * 0.5f));
                    top += height + cardSpacing;
                }
                if (IsWorse(need, worst)) worst = need;
            }

            if (guestQuote != null)
            {
                var fits = needsScroll != null || panel == null || top + guestQuote.rect.height <= panel.rect.height;
                guestQuote.gameObject.SetActive(fits);
                if (needsScroll == null)
                    guestQuote.anchoredPosition = new Vector2(guestQuote.anchoredPosition.x, -(top + guestQuote.rect.height * 0.5f));
                SetText(guestQuoteText, QuoteFor(worst));
            }
            ResetScrollIfRequested();
        }

        private void ResetScrollIfRequested()
        {
            if (!scrollToTop || needsScroll == null) return;
            scrollToTop = false;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(needsScroll.content);
            needsScroll.StopMovement();
            needsScroll.verticalNormalizedPosition = 1f;
        }

        private void RefreshBoost(NeedCard card, NeedStatusSnapshot need)
        {
            if (card.SpeedUpButton == null || card.BoostTimer == null) return;
            var remaining = gameplay.EnergyBoostRemaining(need.BuildingId);
            var canOffer = gameplay.EnergyBoostCost > 0 && gameplay.CurrentPhase == GamePhase.WAVE_ACTIVE
                && need.Served < need.Total && !gameplay.HasUsedEnergyBoost(need.BuildingId);
            card.BoostTimer.gameObject.SetActive(remaining > 0d);
            if (remaining > 0d) SetText(card.BoostTimer, "Boost active · " + Math.Ceiling(remaining) + "s");
            card.SpeedUpButton.gameObject.SetActive(canOffer);
            if (!canOffer) return;

            var cost = gameplay.EnergyBoostCost;
            var availability = gameplay.GetEnergyBoostAvailability(need.BuildingId);
            card.SpeedUpButton.interactable = availability.Succeeded;
            SetText(card.SpeedUpLabel, availability.Succeeded
                ? card.BoostFailure ?? ("Speed Up · " + cost + " Energy")
                : "Need " + cost + " Energy");
        }

        private void UseEnergyForCard(NeedCard card)
        {
            var result = gameplay.TryUseEnergyBoost(BuildingNeedMapping.GetBuildingId(card.Need));
            card.BoostFailure = result.Status == ResortGameplayCommandStatus.Unavailable ? "Save failed · Retry" : null;
            Refresh();
        }

        private static NeedStatusSnapshot? Find(WaveProgressSnapshot wave, NeedType need)
        {
            foreach (var status in wave.Needs)
                if (status.NeedType == need) return status;
            return null;
        }

        private static string HeadingFor(GamePhase phase, WaveProgressSnapshot wave)
        {
            if (phase == GamePhase.WAVE_INCOMING) return "GUESTS ARRIVING";
            if (phase == GamePhase.WAVE_PAUSED) return "WAVE PAUSED";
            return wave.ArrivalsClosed ? "SERVING LAST GUESTS" : "WAVE " + wave.WaveId + " IN PROGRESS";
        }

        private Color ColorFor(NeedServiceStatus status)
        {
            if (status == NeedServiceStatus.BOTTLENECK) return bottleneckColor;
            return status == NeedServiceStatus.RECOVERING ? recoveringColor : okColor;
        }

        private static string SuffixFor(NeedServiceStatus status)
        {
            switch (status)
            {
                case NeedServiceStatus.BOTTLENECK: return " BOTTLENECK";
                case NeedServiceStatus.RECOVERING: return " RECOVERING";
                case NeedServiceStatus.SATISFIED: return " DONE";
                default: return string.Empty;
            }
        }

        // Distinguishes a missing Worker (move one now) from full seats (upgrade between waves).
        private static string WarningFor(NeedStatusSnapshot need)
        {
            var building = ResortUiText.BuildingName(need.BuildingId);
            if (need.Status == NeedServiceStatus.RECOVERING) return building + " queue recovering";
            if (need.Status != NeedServiceStatus.BOTTLENECK) return null;
            return need.IsStaffed ? building + " queue growing: seats full" : building + " queue growing: no Worker";
        }

        private static bool IsWorse(NeedStatusSnapshot candidate, NeedStatusSnapshot? current)
        {
            if (!current.HasValue) return true;
            return Rank(candidate.Status) > Rank(current.Value.Status)
                || (Rank(candidate.Status) == Rank(current.Value.Status) && candidate.Waiting > current.Value.Waiting);
        }

        private static int Rank(NeedServiceStatus status) =>
            status == NeedServiceStatus.BOTTLENECK ? 2 : status == NeedServiceStatus.RECOVERING ? 1 : 0;

        private static string QuoteFor(NeedStatusSnapshot? worst)
        {
            if (worst.HasValue && worst.Value.Status == NeedServiceStatus.BOTTLENECK)
                return "\"" + ResortUiText.NeedName(worst.Value.NeedType) + " takes a bit too long.\"";
            if (worst.HasValue && worst.Value.Status == NeedServiceStatus.RECOVERING)
                return "\"" + ResortUiText.NeedName(worst.Value.NeedType) + " is getting better!\"";
            return "\"Everything is running smoothly!\"";
        }

        private void OpenWorkerAllocation()
        {
            if (navigation != null) navigation.OpenWorkerAllocation();
        }

        private float RefreshRushNotification()
        {
            if (rushNotification == null) return firstCardTop;
            var rush = gameplay == null ? default : gameplay.CurrentSuddenRush;
            if (rush.IsVisible && !rushNotification.gameObject.activeSelf)
                scrollToTop = true;
            rushNotification.gameObject.SetActive(rush.IsVisible);
            if (!rush.IsVisible)
            {
                rushActionMessage = null;
                return firstCardTop;
            }

            if (needsScroll == null)
                rushNotification.anchoredPosition = new Vector2(0f, -firstCardTop);
            SetText(rushTitle, "SUDDEN RUSH: " + ResortUiText.BuildingName(rush.BuildingId).ToUpperInvariant());
            SetText(rushDescription, "+" + rush.DemandUnits + " "
                + ResortUiText.NeedName(BuildingNeedMapping.GetNeedType(rush.BuildingId))
                + " demand over " + rush.Duration.ToString("0.#") + "s. Guests are waiting!");
            var availability = gameplay.GetEnergyBoostAvailability(rush.BuildingId);
            var used = gameplay.HasUsedEnergyBoost(rush.BuildingId);
            if (energyBoostButton != null)
            {
                energyBoostButton.gameObject.SetActive(!used);
                energyBoostButton.interactable = availability.Succeeded;
                SetText(energyBoostButton.GetComponentInChildren<TextMeshProUGUI>(true),
                    "Use Energy to serve faster\n" + gameplay.EnergyBoostCost + " Energy");
            }
            if (moveWorkerButton != null) moveWorkerButton.interactable = navigation != null;
            SetText(rushFeedback, rushActionMessage ?? (availability.Succeeded
                ? "The wave keeps running. Energy is optional." : availability.Message));
            return firstCardTop + rushNotification.rect.height + cardSpacing;
        }

        private void MoveWorkerToRush()
        {
            var rush = gameplay == null ? default : gameplay.CurrentSuddenRush;
            if (!rush.IsVisible || navigation == null) return;
            if (navigation.OpenWorkerAllocation(rush.BuildingId))
                gameplay.TryDismissSuddenRush(rush.EventId);
            Refresh();
        }

        private void UseEnergyForRush()
        {
            var rush = gameplay == null ? default : gameplay.CurrentSuddenRush;
            if (!rush.IsVisible) return;
            var result = gameplay.TryUseEnergyBoost(rush.BuildingId, rush.EventId);
            if (!result.Succeeded) rushActionMessage = result.Message;
            Refresh();
        }

        private void IgnoreRush()
        {
            var rush = gameplay == null ? default : gameplay.CurrentSuddenRush;
            if (rush.IsVisible) gameplay.TryDismissSuddenRush(rush.EventId);
            Refresh();
        }

        private static void SetText(TextMeshProUGUI label, string value)
        {
            if (label != null) label.text = value;
        }
    }
}
