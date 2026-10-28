using System;
using System.Collections.Generic;
using System.Linq;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ResortEmpire.DebugUi
{
    /// <summary>
    /// Development-only presentation inspector for the existing Resort UI screens.
    /// F1 cycles through deterministic visual previews without blocking gameplay input.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResortDebugUiController : MonoBehaviour
    {
        private const int DebugCanvasSortingOrder = 1000;
        private static ResortDebugUiController activeInstance;

        private readonly List<DebugPreviewEntry> previewEntries = new List<DebugPreviewEntry>();
        private ResortDebugUiPreviewManager previewManager;
        private ResortDebugUiCycleState cycleState;
        private ResortGameplayBootstrap gameplayBootstrap;
        private ResortUiStateCoordinator uiCoordinator;
        private Canvas debugCanvas;
        private TextMeshProUGUI indicatorText;
        private bool overrideOwned;
        private bool f1WasPressed;

        public bool PreviewVisible => previewManager != null && previewManager.HasActivePreview;
        public int CurrentPreviewIndex => cycleState == null ? -1 : cycleState.CurrentIndex;
        public int PreviewCount => previewEntries.Count;

        public static bool IsDevelopmentUiAvailable
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            get { return true; }
#else
            get { return false; }
#endif
        }

        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!ClaimControllerOwnership())
                return;

            debugCanvas = GetComponent<Canvas>();
            if (debugCanvas == null)
                debugCanvas = gameObject.AddComponent<Canvas>();
            debugCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            debugCanvas.overrideSorting = true;
            debugCanvas.sortingOrder = DebugCanvasSortingOrder;

            gameplayBootstrap = FindAnyObjectByType<ResortGameplayBootstrap>();
            uiCoordinator = FindAnyObjectByType<ResortUiStateCoordinator>();
            DiscoverPreviewTargets();
            cycleState = new ResortDebugUiCycleState(previewEntries.Count);
            previewManager = new ResortDebugUiPreviewManager(
                previewEntries.Select(entry => entry.Target).ToList());
            BuildIndicator();
#else
            enabled = false;
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!IsDevelopmentUiAvailable || activeInstance != this)
                return;

            var f1Pressed = Keyboard.current != null && Keyboard.current.f1Key.isPressed;
            if (f1Pressed && !f1WasPressed)
                AdvancePreviewCycle();
            f1WasPressed = f1Pressed;
#endif
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RestoreNormalUi();
            if (ReferenceEquals(activeInstance, this))
                activeInstance = null;
#endif
        }

        /// <summary>
        /// Advances Home Island, Wave Needs, Worker Allocation, Wave Complete, Upgrade,
        /// Area Unlock, VIP Request Board, VIP Preview, VIP Complete, then normal UI.
        /// </summary>
        public int AdvancePreviewCycle()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (previewManager == null || previewEntries.Count == 0)
                return -1;

            var nextIndex = cycleState.Advance();
            if (nextIndex < 0)
            {
                RestoreNormalUi();
                return -1;
            }

            if (!OpenPreview(nextIndex))
            {
                cycleState.Reset();
                return -1;
            }

            return nextIndex;
#else
            return -1;
#endif
        }

        public void RestoreNormalUi()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (previewManager != null)
                previewManager.RestoreNormalUi();

            if (cycleState != null)
                cycleState.Reset();
            if (overrideOwned)
            {
                overrideOwned = false;
                if (uiCoordinator != null)
                    uiCoordinator.EndPreviewOverride();
            }

            SetIndicatorVisible(false);
#endif
        }

        public bool OpenPreview(int index)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (previewManager == null || index < 0 || index >= previewEntries.Count)
                return false;

            if (!previewEntries[index].Target.IsAvailable)
                return false;

            if (!overrideOwned)
            {
                overrideOwned = true;
                if (uiCoordinator != null)
                    uiCoordinator.BeginPreviewOverride();
            }

            if (!previewManager.OpenPreview(index))
                return false;

            cycleState.Select(index);
            ApplyMockPresentation(previewEntries[index]);
            SetIndicatorVisible(true);
            return true;
#else
            return false;
#endif
        }

        private bool ClaimControllerOwnership()
        {
            if (activeInstance != null && !ReferenceEquals(activeInstance, this))
            {
                enabled = false;
                return false;
            }

            activeInstance = this;
            return true;
        }

        private void DiscoverPreviewTargets()
        {
            previewEntries.Clear();
            var definitions = new[]
            {
                new PreviewDefinition("Home Island", "HomeIslandView"),
                new PreviewDefinition("Wave Needs", "WaveNeedsPanel"),
                new PreviewDefinition("Worker Allocation", "AssignStaffRestaurant"),
                new PreviewDefinition("Wave Complete", "WaveComplete"),
                new PreviewDefinition("Upgrade Suggestion", "UpgradeSuggestionPanel"),
                new PreviewDefinition("Area Unlock", "AreaUnlockPanel"),
                new PreviewDefinition("VIP Request Board", "VipRequestBoard"),
                new PreviewDefinition("VIP Preview", "VipPreview"),
                new PreviewDefinition("VIP Complete", "VipComplete")
            };

            foreach (var definition in definitions)
            {
                var target = FindSceneObject(definition.ObjectName);
                previewEntries.Add(new DebugPreviewEntry(
                    definition.Label,
                    new ResortDebugUiPreviewTarget(target)));
            }
        }

        private void BuildIndicator()
        {
            var indicatorObject = new GameObject("DebugCycleIndicator", typeof(RectTransform));
            indicatorObject.transform.SetParent(transform, false);
            var rect = indicatorObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(28f, -28f);
            rect.sizeDelta = new Vector2(620f, 42f);

            indicatorText = indicatorObject.AddComponent<TextMeshProUGUI>();
            indicatorText.font = TMP_Settings.defaultFontAsset;
            indicatorText.fontSize = 20f;
            indicatorText.fontStyle = FontStyles.Bold;
            indicatorText.color = Color.white;
            indicatorText.alignment = TextAlignmentOptions.TopLeft;
            indicatorText.raycastTarget = false;
            SetIndicatorVisible(false);
        }

        private void SetIndicatorVisible(bool visible)
        {
            if (indicatorText == null)
                return;

            indicatorText.text = visible
                ? "DEBUG " + (CurrentPreviewIndex + 1) + "/" + previewEntries.Count
                    + " — " + previewEntries[CurrentPreviewIndex].Label + "  [F1: next]"
                : string.Empty;
            indicatorText.gameObject.SetActive(visible);
        }

        private static void ApplyMockPresentation(DebugPreviewEntry entry)
        {
            var root = entry.Target.GameObject;
            if (root == null)
                return;

            var resultView = root.GetComponentInChildren<ResultsView>(true);
            if (resultView != null)
            {
                try
                {
                    // Keep values within the existing compact result-field widths.
                    resultView.SetResults("12/12", "92%", "Rooms & drinks went great!", 4, false);
                }
                catch (NullReferenceException)
                {
                    // Missing serialized presentation references leave this visual-only preview intact.
                }
            }

            var vipPreview = root.GetComponentInChildren<VipPreviewView>(true);
            if (vipPreview != null)
            {
                try
                {
                    vipPreview.SetGuest(
                        "Maya Sterling",
                        "A returning guest seeking a calm island escape.",
                        "A memorable stay from arrival to departure.",
                        null,
                        4);
                    vipPreview.SetArrival(125f, 180f);
                }
                catch (NullReferenceException)
                {
                    // Missing serialized presentation references leave this visual-only preview intact.
                }
            }
        }

        private static GameObject FindSceneObject(string objectName)
        {
            var all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            var exact = all.FirstOrDefault(transform => transform.name == objectName);
            return exact == null ? null : exact.gameObject;
        }

        private readonly struct PreviewDefinition
        {
            public PreviewDefinition(string label, string objectName)
            {
                Label = label;
                ObjectName = objectName;
            }

            public string Label { get; }
            public string ObjectName { get; }
        }

        private sealed class DebugPreviewEntry
        {
            public DebugPreviewEntry(string label, ResortDebugUiPreviewTarget target)
            {
                Label = label;
                Target = target;
            }

            public string Label { get; }
            public ResortDebugUiPreviewTarget Target { get; }
        }
    }

    public sealed class ResortDebugUiPreviewTarget : IResortDebugUiPreviewTarget
    {
        public ResortDebugUiPreviewTarget(GameObject gameObject)
        {
            GameObject = gameObject;
        }

        public GameObject GameObject { get; }
        public bool IsAvailable => GameObject != null;
        public bool IsActive
        {
            get { return GameObject != null && GameObject.activeSelf; }
            set
            {
                if (GameObject != null)
                    GameObject.SetActive(value);
            }
        }
    }
}
