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
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Service;
            public TextMeshProUGUI Status;
            public TextMeshProUGUI Count;
            public Image ProgressFill;
            [Tooltip("Waiting queue and the Worker or seat limit.")]
            [FormerlySerializedAs("Warning")]
            public TextMeshProUGUI Detail;
            public Image StatusIndicator;
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
        [SerializeField] private ScrollRect needsScroll;
        [SerializeField] private TextMeshProUGUI heading;
        [SerializeField] private TextMeshProUGUI waveStatus;
        [SerializeField] private TextMeshProUGUI guests;
        [SerializeField] private Image guestProgressFill;
        [SerializeField] private RectTransform guestQuote;
        [SerializeField] private TextMeshProUGUI guestQuoteText;
        [SerializeField] private NeedCard[] cards;
        [SerializeField] private float refreshInterval = 0.1f;

        [Header("Progress colors")]
        [SerializeField] private Color completeColor = new Color32(17, 116, 89, 255);
        [SerializeField] private Color waitingColor = new Color32(151, 79, 16, 255);
        [SerializeField] private Color bottleneckColor = new Color32(165, 48, 56, 255);
        [SerializeField] private Color activeColor = new Color32(19, 101, 132, 255);

        [Header("Bottleneck detection")]
        [SerializeField] private WaveNeedStatusSettings statusSettings = new WaveNeedStatusSettings();

        [Header("Bottleneck alert")]
        private ResponsiveEdgePanel tabletMotion;
        [SerializeField, Min(0.1f)] private float wobbleDuration = 0.55f;
        [SerializeField, Range(0f, 5f)] private float wobbleAngle = 2f;
        [SerializeField, Min(0f)] private float alertCooldown = 5f;
        [SerializeField] private bool vibrateOnBottleneck = true;

        private readonly HashSet<NeedType> bottleneckNeeds = new HashSet<NeedType>();
        private int alertWaveId = -1;
        private float nextAlertTime;

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
            if (tabletMotion == null) tabletMotion = GetComponent<ResponsiveEdgePanel>();
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
            var wave = Tracker.Capture(gameplay);
            if (wave == null)
            {
                bottleneckNeeds.Clear();
                alertWaveId = -1;
                ResetScrollIfRequested();
                return;
            }

            SetText(heading, "WAVE " + wave.WaveId);
            SetText(waveStatus, StatusFor(gameplay.CurrentPhase, wave.ArrivalsClosed));
            SetText(guests, "SERVED " + wave.Served + "/" + wave.Total);
            SetProgress(guestProgressFill, wave.Served, wave.Total);

            NeedStatusSnapshot? worst = null;
            RefreshRushNotification();
            foreach (var card in cards)
            {
                var status = Find(wave, card.Need);
                card.Root.gameObject.SetActive(status.HasValue);
                if (!status.HasValue) continue;

                var need = status.Value;
                var color = ColorFor(need);
                SetText(card.Name, ResortUiText.NeedName(need.NeedType));
                SetText(card.Service, ResortUiText.BuildingName(need.BuildingId));
                SetText(card.Count, need.Served + "/" + need.Total);
                SetText(card.Status, CardStatusFor(need));
                SetText(card.Detail, DetailFor(need));
                if (card.Status != null) card.Status.color = color;
                if (card.StatusIndicator != null) card.StatusIndicator.color = color;
                if (card.Count != null) card.Count.color = color;
                if (card.ProgressFill != null) card.ProgressFill.color = color;
                SetProgress(card.ProgressFill, need.Served, need.Total);
                RefreshBoost(card, need);
                if (IsWorse(need, worst)) worst = need;
            }

            RefreshBottleneckAlert(wave);
            SetText(guestQuoteText, QuoteFor(worst));
            ShowFeedback(wave.Feedback);
            ResetScrollIfRequested();
        }

        private void RefreshBottleneckAlert(WaveProgressSnapshot wave)
        {
            if (alertWaveId != wave.WaveId)
            {
                alertWaveId = wave.WaveId;
                bottleneckNeeds.Clear();
                nextAlertTime = 0f;
            }
            if (gameplay.CurrentPhase != GamePhase.WAVE_ACTIVE)
            {
                return;
            }

            var enteredBottleneck = false;
            foreach (var need in wave.Needs)
            {
                if (need.Status == NeedServiceStatus.BOTTLENECK)
                    enteredBottleneck |= bottleneckNeeds.Add(need.NeedType);
                else
                    bottleneckNeeds.Remove(need.NeedType);
            }
            if (!enteredBottleneck || Time.unscaledTime < nextAlertTime) return;
            nextAlertTime = Time.unscaledTime + alertCooldown;
            tabletMotion?.PlayBottleneckWobble(wobbleDuration, wobbleAngle);
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            if (vibrateOnBottleneck) Handheld.Vibrate();
#endif
        }

        private static void SetProgress(Image fill, int served, int total)
        {
            if (fill == null) return;

            var amount = total > 0 ? Mathf.Clamp01((float)served / total) : 0f;
            var rect = fill.rectTransform;
            if (rect.anchorMax.x != amount)
                rect.anchorMax = new Vector2(amount, 1f);
            fill.enabled = amount > 0f;
        }

        private void ResetScrollIfRequested()
        {
            if (!scrollToTop || needsScroll == null || needsScroll.content == null) return;
            scrollToTop = false;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(needsScroll.content);
            needsScroll.StopMovement();
            needsScroll.verticalNormalizedPosition = 1f;
        }

        private void RefreshBoost(NeedCard card, NeedStatusSnapshot need)
        {
            if (card.SpeedUpButton == null || card.BoostTimer == null) return;
            var remaining = gameplay.EnergyBoostRemaining(need.BuildingId);
            var canOffer = remaining <= 0d && gameplay.EnergyBoostCost > 0 && gameplay.CurrentPhase == GamePhase.WAVE_ACTIVE
                && need.Served < need.Total && !gameplay.HasUsedEnergyBoost(need.BuildingId);
            card.BoostTimer.transform.parent.gameObject.SetActive(remaining > 0d);
            if (remaining > 0d) SetText(card.BoostTimer, "Boost active\n" + Math.Ceiling(remaining) + "s left");
            card.SpeedUpButton.gameObject.SetActive(canOffer);
            if (!canOffer) return;

            var cost = gameplay.EnergyBoostCost;
            var availability = gameplay.GetEnergyBoostAvailability(need.BuildingId);
            card.SpeedUpButton.interactable = availability.Succeeded;
            SetText(card.SpeedUpLabel, availability.Succeeded
                ? card.BoostFailure ?? ("Speed Up\n" + cost + " Energy")
                : "Need\n" + cost + " Energy");
        }

        private void UseEnergyForCard(NeedCard card)
        {
            if (gameplay == null) return;
            var building = BuildingNeedMapping.GetBuildingId(card.Need);
            if (!gameplay.GetEnergyBoostAvailability(building).Succeeded) return;

            var result = gameplay.TryUseEnergyBoost(building);
            card.BoostFailure = result.Status == ResortGameplayCommandStatus.Unavailable ? "Save failed\nRetry" : null;
            Refresh();
        }

        private static NeedStatusSnapshot? Find(WaveProgressSnapshot wave, NeedType need)
        {
            foreach (var status in wave.Needs)
                if (status.NeedType == need) return status;
            return null;
        }

        private static string StatusFor(GamePhase phase, bool arrivalsClosed)
        {
            switch (phase)
            {
                case GamePhase.WAVE_INCOMING: return "ARRIVING";
                case GamePhase.WAVE_PAUSED: return "PAUSED";
                case GamePhase.WAVE_COMPLETE: return "COMPLETE";
                default: return arrivalsClosed ? "FINAL SERVICE" : "IN PROGRESS";
            }
        }

        private Color ColorFor(NeedStatusSnapshot need)
        {
            if (need.Total <= 0) return activeColor;
            if (need.Status == NeedServiceStatus.BOTTLENECK) return bottleneckColor;
            if (need.Status == NeedServiceStatus.SATISFIED) return completeColor;
            return need.Status == NeedServiceStatus.RECOVERING || need.Waiting > 0 ? waitingColor : activeColor;
        }

        private static string CardStatusFor(NeedStatusSnapshot need)
        {
            if (need.Total <= 0) return "NO DEMAND";
            switch (need.Status)
            {
                case NeedServiceStatus.SATISFIED: return "COMPLETE";
                case NeedServiceStatus.BOTTLENECK: return "BOTTLENECK";
                case NeedServiceStatus.RECOVERING: return "RECOVERING";
                default: return need.Waiting > 0 ? "WAITING" : "ON TRACK";
            }
        }

        private static string DetailFor(NeedStatusSnapshot need)
        {
            if (need.Total <= 0) return "No service required";
            if (need.Status == NeedServiceStatus.SATISFIED) return "All served";

            var staffing = need.IsStaffed ? "Worker on duty" : "No worker";
            if (need.Status == NeedServiceStatus.BOTTLENECK && need.Cause == NeedQueueCause.ServiceCapacity)
                staffing = "Seats full " + need.InService + "/" + need.Capacity;
            return need.Waiting + " waiting · " + staffing;
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
            if (worst.HasValue && worst.Value.Waiting > 0)
                return "\"Still waiting for " + ResortUiText.NeedName(worst.Value.NeedType).ToLowerInvariant() + ".\"";
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
            if (guestQuote != null) guestQuote.gameObject.SetActive(false);
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
            if (guestQuote != null) guestQuote.gameObject.SetActive(true);
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

        private void RefreshRushNotification()
        {
            if (rushNotification == null) return;
            var rush = gameplay == null ? default : gameplay.CurrentSuddenRush;
            if (rush.IsVisible && !rushNotification.gameObject.activeSelf)
                scrollToTop = true;
            rushNotification.gameObject.SetActive(rush.IsVisible);
            if (!rush.IsVisible)
            {
                rushActionMessage = null;
                return;
            }

            SetText(rushTitle, "SUDDEN RUSH · " + ResortUiText.BuildingName(rush.BuildingId).ToUpperInvariant());
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
                    "Boost · " + gameplay.EnergyBoostCost + " Energy");
            }
            if (moveWorkerButton != null) moveWorkerButton.interactable = navigation != null;
            SetText(rushFeedback, rushActionMessage ?? (availability.Succeeded
                ? "The wave keeps running. Energy is optional." : availability.Message));
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
            if (label != null && label.text != value) label.text = value;
        }
    }
}
