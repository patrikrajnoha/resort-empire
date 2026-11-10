using System;
using System.Collections.Generic;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>
    /// S2 binding: one card per owned need, stacked in a stable 3 -> 4 -> 5 order. Each card shows
    /// its service, served/total, the waiting queue and the Worker or seat limit behind it. Status is
    /// spelled out, never colour-only. Numeric satisfaction is never shown during a wave. Tapping a
    /// card opens S3 for that service; S2 stays active underneath, so closing S3 returns here.
    /// </summary>
    public sealed class WaveNeedsPresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class NeedCard
        {
            public NeedType Need;
            public RectTransform Root;
            [Tooltip("Need name label; the service name is appended in a smaller grey run.")]
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Count;
            public Image ProgressFill;
            [Tooltip("Always-visible row: status word, waiting queue and the Worker or seat limit.")]
            [FormerlySerializedAs("Warning")]
            public TextMeshProUGUI Detail;
            public GameObject AlertIcon;
            public Image Icon;
            [Tooltip("Glyph icons take the status color; full-color building art keeps its own.")]
            public bool TintIcon = true;
            public Button Button;
            public UnityEngine.UI.Button SpeedUpButton;
            public TextMeshProUGUI SpeedUpLabel;
            public TextMeshProUGUI BoostTimer;
            [NonSerialized] public UnityAction SpeedUp;
            [NonSerialized] public UnityAction Open;
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
        [SerializeField] private float cardHeight = 176f;
        [SerializeField] private float refreshInterval = 0.1f;
        [SerializeField] private Color okColor = new Color(0.06f, 0.72f, 0.51f);
        [SerializeField] private Color recoveringColor = new Color(1f, 0.6f, 0f);
        [SerializeField] private Color bottleneckColor = new Color(0.96f, 0.25f, 0.27f);
        [SerializeField] private Color serviceNameColor = new Color(0.45f, 0.5f, 0.58f);

        [Header("Bottleneck detection")]
        [SerializeField] private WaveNeedStatusSettings statusSettings = new WaveNeedStatusSettings();

        [Header("Feedback toast")]
        [Tooltip("Short contextual messages. Never blocks taps; holds while S3 covers the panel.")]
        [SerializeField] private CanvasGroup feedbackToast;
        [SerializeField] private TextMeshProUGUI feedbackText;
        [SerializeField] private float feedbackDuration = 3.5f;
        [SerializeField] private float feedbackFade = 0.25f;
        [Tooltip("A lower-priority message may replace a higher one only after this many seconds.")]
        [SerializeField] private float feedbackMinimumTime = 1.5f;

        [Header("Sudden Rush")]
        [SerializeField] private RectTransform rushNotification;
        [SerializeField] private TextMeshProUGUI rushTitle;
        [SerializeField] private TextMeshProUGUI rushDescription;
        [SerializeField] private TextMeshProUGUI rushFeedback;
        [SerializeField] private UnityEngine.UI.Button moveWorkerButton;
        [SerializeField] private UnityEngine.UI.Button energyBoostButton;
        [SerializeField] private UnityEngine.UI.Button ignoreRushButton;

        private WaveNeedStatusTracker tracker;
        private float nextRefresh;
        private string rushActionMessage;
        private bool scrollToTop;
        private float feedbackRemaining;
        private float feedbackShown;
        private NeedFeedbackKind feedbackKind;

        private WaveNeedStatusTracker Tracker
        {
            get
            {
                if (tracker != null) return tracker;
                try { tracker = new WaveNeedStatusTracker(statusSettings); }
                catch (ArgumentOutOfRangeException exception)
                {
                    Debug.LogError("Invalid S2 bottleneck settings; using defaults. " + exception.Message, this);
                    tracker = new WaveNeedStatusTracker();
                }
                return tracker;
            }
        }

        private void OnEnable()
        {
            scrollToTop = true;
            HideFeedback();
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (navigation == null) navigation = FindAnyObjectByType<ResortUiStateCoordinator>();
            foreach (var card in cards)
            {
                if (card.Button != null)
                {
                    card.Open = () => OpenWorkerAllocation(card);
                    card.Button.onClick.AddListener(card.Open);
                }
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
                if (card.Button != null && card.Open != null) card.Button.onClick.RemoveListener(card.Open);
                if (card.SpeedUpButton != null && card.SpeedUp != null) card.SpeedUpButton.onClick.RemoveListener(card.SpeedUp);
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
            UpdateFeedback();
            if (Time.unscaledTime < nextRefresh) return;
            Refresh();
        }

        private void HandleStateChanged(GamePhase previous, GamePhase next) => Refresh();
        private void HandleWorkerAssignmentChanged(WorkerAssignmentChange change) => Refresh();

        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + refreshInterval;
            // Before the first wave (for example an F1 preview) the static layout stays untouched.
            var wave = Tracker.Capture(gameplay);
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
                var color = ColorFor(need.Status);
                SetText(card.Name, ResortUiText.NeedName(need.NeedType) + "<size=72%><color=#"
                    + ColorUtility.ToHtmlStringRGB(serviceNameColor) + ">  " + ResortUiText.BuildingName(need.BuildingId) + "</color></size>");
                SetText(card.Count, need.Served + "/" + need.Total);
                if (card.Count != null) card.Count.color = color;
                if (card.Icon != null && card.TintIcon) card.Icon.color = color;
                if (card.ProgressFill != null)
                {
                    card.ProgressFill.fillAmount = need.Total > 0 ? (float)need.Served / need.Total : 0f;
                    card.ProgressFill.color = color;
                }
                if (card.Detail != null)
                {
                    if (!card.Detail.gameObject.activeSelf) card.Detail.gameObject.SetActive(true);
                    card.Detail.text = DetailFor(need);
                }
                if (card.AlertIcon != null) card.AlertIcon.SetActive(need.Status == NeedServiceStatus.BOTTLENECK);
                RefreshBoost(card, need);

                if (!Mathf.Approximately(card.Root.rect.height, cardHeight))
                    card.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cardHeight);
                if (needsScroll == null)
                {
                    card.Root.anchoredPosition = new Vector2(card.Root.anchoredPosition.x, -(top + cardHeight * 0.5f));
                    top += cardHeight + cardSpacing;
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
            ShowFeedback(wave.Feedback);
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

        // The status word always leads, so the state never depends on colour alone. The cause then
        // separates a missing Worker (move one now) from full seats (upgrade between waves).
        private string DetailFor(NeedStatusSnapshot need)
        {
            var waiting = need.Waiting + " waiting";
            switch (need.Status)
            {
                case NeedServiceStatus.SATISFIED:
                    return Colored("SATISFIED", okColor) + " · all " + need.Total + " served";
                case NeedServiceStatus.BOTTLENECK:
                    return Colored("BOTTLENECK", bottleneckColor) + " · " + waiting + " · "
                        + Colored(CauseFor(need), bottleneckColor);
                case NeedServiceStatus.RECOVERING:
                    return Colored("RECOVERING", recoveringColor) + " · " + waiting + ", shrinking";
                default:
                    return Colored("OK", okColor) + " · " + waiting + " · "
                        + (need.IsStaffed ? "Worker on duty" : Colored("No Worker", recoveringColor));
            }
        }

        private static string CauseFor(NeedStatusSnapshot need)
        {
            return need.Cause == NeedQueueCause.ServiceCapacity
                ? "SEATS FULL " + need.InService + "/" + need.Capacity
                : "NO WORKER";
        }

        private static string Colored(string text, Color color) =>
            "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";

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

        private void ShowFeedback(IReadOnlyList<NeedFeedback> feedback)
        {
            if (feedback.Count == 0 || feedbackToast == null || feedbackText == null) return;
            var lead = feedback[0];
            // A routine message must not immediately hide a fresh warning.
            if (feedbackRemaining > 0f && lead.Kind < feedbackKind && feedbackShown < feedbackMinimumTime) return;

            var message = MessageFor(lead);
            // A Worker move helps one building and slows another: show both halves together.
            if (feedback.Count > 1) message += "\n" + MessageFor(feedback[1]);
            feedbackText.text = message;
            feedbackKind = lead.Kind;
            feedbackRemaining = feedbackDuration;
            feedbackShown = 0f;
            if (!feedbackToast.gameObject.activeSelf) feedbackToast.gameObject.SetActive(true);
        }

        private void UpdateFeedback()
        {
            if (feedbackToast == null || feedbackRemaining <= 0f) return;
            // S3 covers the panel; hold the message so it is still there when the player returns.
            if (navigation == null || !navigation.IsWorkerAllocationOpen)
            {
                feedbackRemaining -= Time.unscaledDeltaTime;
                feedbackShown += Time.unscaledDeltaTime;
            }
            if (feedbackRemaining <= 0f)
            {
                HideFeedback();
                return;
            }
            var fade = Mathf.Max(0.01f, feedbackFade);
            feedbackToast.alpha = Mathf.Clamp01(Mathf.Min(feedbackShown, feedbackRemaining) / fade);
        }

        private void HideFeedback()
        {
            feedbackRemaining = 0f;
            if (feedbackToast == null) return;
            feedbackToast.alpha = 0f;
            feedbackToast.blocksRaycasts = false;
            feedbackToast.interactable = false;
            if (feedbackToast.gameObject.activeSelf) feedbackToast.gameObject.SetActive(false);
        }

        private static string MessageFor(NeedFeedback feedback)
        {
            var building = ResortUiText.BuildingName(feedback.BuildingId);
            var need = ResortUiText.NeedName(feedback.NeedType);
            switch (feedback.Kind)
            {
                case NeedFeedbackKind.BottleneckMissingWorker:
                    return building + " queue keeps growing: no Worker. Tap " + need + " to send one.";
                case NeedFeedbackKind.BottleneckCapacity:
                    return building + " seats are full. Use Energy now or upgrade after the wave.";
                case NeedFeedbackKind.RecoveringAfterWorker:
                    return "Worker arrived! " + building + " is recovering.";
                case NeedFeedbackKind.WorkerRemoved:
                    return building + " has no Worker now and serves slower.";
                case NeedFeedbackKind.Recovering:
                    return building + " queue is shrinking.";
                case NeedFeedbackKind.BackToNormal:
                    return building + " is back to normal.";
                case NeedFeedbackKind.WorkerAssigned:
                    return building + " has a Worker: full speed.";
                case NeedFeedbackKind.ArrivalsEnded:
                    return "All guests have arrived. Clear the remaining queues.";
                default:
                    return need + " fully served at " + building + ".";
            }
        }

        private void OpenWorkerAllocation(NeedCard card)
        {
            if (navigation != null) navigation.OpenWorkerAllocationForNeed(card.Need);
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
