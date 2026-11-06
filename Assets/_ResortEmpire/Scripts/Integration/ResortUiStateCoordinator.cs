using System.Collections.Generic;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// Phase-driven window navigation for the Prototype loop: S2 during a wave, S4 for an
    /// unclaimed result, S5 after claiming. S3 and S9 are player-opened modals that close
    /// when the phase no longer allows them. It never creates result data or runs commands.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResortUiStateCoordinator : MonoBehaviour
    {
        [SerializeField]
        private ResortGameplayBootstrap gameplayBootstrap;

        [SerializeField]
        private GameObject homeIslandView;

        [SerializeField]
        private GameObject waveNeedsPanel;

        [SerializeField]
        private GameObject waveCompleteWindow;

        [SerializeField]
        private GameObject upgradeSuggestionWindow;

        [SerializeField]
        private GameObject workerAllocationWindow;

        [SerializeField]
        private GameObject areaUnlockWindow;

        private readonly HashSet<BuildingId> announcedAreas = new HashSet<BuildingId>();
        private IResortGameplayUiFacade gameplayFacade;
        private bool subscribed;
        private int previewOverrideDepth;

        public bool IsPreviewOverrideActive => previewOverrideDepth > 0;
        public BuildingId? WorkerAllocationTarget { get; private set; }
        public event System.Action WorkerAllocationContextChanged;

        /// <summary>
        /// Temporarily suppresses normal state-driven visibility while a presentation-only
        /// debug preview owns the visible windows.
        /// </summary>
        public void BeginPreviewOverride()
        {
            previewOverrideDepth++;
        }

        /// <summary>
        /// Releases the debug preview ownership and reapplies the canonical gameplay state.
        /// </summary>
        public void EndPreviewOverride()
        {
            if (previewOverrideDepth == 0)
                return;

            previewOverrideDepth--;
            if (previewOverrideDepth == 0)
                SynchronizeImmediately();
        }

        /// <summary>Opens S3 when Workers may be viewed: between waves or while a wave runs.</summary>
        public bool OpenWorkerAllocation()
        {
            return OpenWorkerAllocationFor(null);
        }

        public bool OpenWorkerAllocation(BuildingId buildingId)
        {
            if (gameplayFacade == null) return false;
            foreach (var building in gameplayFacade.Buildings)
                if (building.BuildingId == buildingId && building.IsOwned)
                    return OpenWorkerAllocationFor(buildingId);
            return false;
        }

        private bool OpenWorkerAllocationFor(BuildingId? buildingId)
        {
            if (IsPreviewOverrideActive || workerAllocationWindow == null || !AllowsWorkerAllocation(CurrentPhase))
                return false;

            WorkerAllocationTarget = buildingId;
            workerAllocationWindow.SetActive(true);
            WorkerAllocationContextChanged?.Invoke();
            return true;
        }

        /// <summary>Opens S9 between waves when an eligible building is not yet owned.</summary>
        public bool OpenAreaUnlock()
        {
            if (IsPreviewOverrideActive || areaUnlockWindow == null || CurrentPhase != GamePhase.AWAITING_WAVE
                || !FindEligibleArea().HasValue)
                return false;

            areaUnlockWindow.SetActive(true);
            return true;
        }

        private GamePhase CurrentPhase => gameplayFacade == null ? GamePhase.AWAITING_WAVE : gameplayFacade.CurrentPhase;

        private void Awake()
        {
            ResolveSceneReferences();
        }

        private void OnEnable()
        {
            BindToGameplay();
            SynchronizeImmediately();
        }

        private void Start()
        {
            BindToGameplay();
            SynchronizeImmediately();
        }

        private void OnDisable()
        {
            UnbindFromGameplay();
        }

        private void OnDestroy()
        {
            UnbindFromGameplay();
        }

        private void ResolveSceneReferences()
        {
            if (gameplayBootstrap == null)
                gameplayBootstrap = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (homeIslandView == null)
                homeIslandView = FindSceneObject("GameUI/SafeArea/HomeIslandViewCanvas/HomeIslandView");
            if (waveNeedsPanel == null)
                waveNeedsPanel = FindSceneObject("GameUI/SafeArea/HomeIslandViewCanvas/WaveNeedsPanel");
            if (upgradeSuggestionWindow == null)
                upgradeSuggestionWindow = FindSceneObject("GameUI/SafeArea/HomeIslandViewCanvas/UpgradeSuggestionPanel");
            if (areaUnlockWindow == null)
                areaUnlockWindow = FindSceneObject("GameUI/SafeArea/HomeIslandViewCanvas/AreaUnlockPanel");
            if (waveCompleteWindow == null)
                waveCompleteWindow = FindSceneObject("WaveCompleteCanvas/WaveComplete");
            if (workerAllocationWindow == null)
                workerAllocationWindow = FindSceneObject("AssignStaffRestaurantCanvas/AssignStaffRestaurant");
        }

        private void BindToGameplay()
        {
            if (subscribed)
                return;
            if (gameplayBootstrap == null)
                gameplayBootstrap = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (gameplayBootstrap == null)
                return;

            gameplayFacade = gameplayBootstrap;
            gameplayFacade.StateChanged += HandleStateChanged;
            gameplayFacade.WaveResultReady += HandleWaveResultReady;
            subscribed = true;
        }

        private void UnbindFromGameplay()
        {
            if (!subscribed || gameplayFacade == null)
                return;

            gameplayFacade.StateChanged -= HandleStateChanged;
            gameplayFacade.WaveResultReady -= HandleWaveResultReady;
            gameplayFacade = null;
            subscribed = false;
        }

        private void SynchronizeImmediately()
        {
            ResolveSceneReferences();
            ApplyPhase(CurrentPhase);
        }

        private void HandleStateChanged(GamePhase previousState, GamePhase nextState)
        {
            ApplyPhase(nextState);
        }

        // The raw WAVE_COMPLETE transition happens before the result is calculated.
        private void HandleWaveResultReady(WaveResult result)
        {
            ApplyPhase(CurrentPhase);
        }

        private void ApplyPhase(GamePhase phase)
        {
            if (IsPreviewOverrideActive)
                return;

            if (homeIslandView != null)
                homeIslandView.SetActive(true);

            SetActive(waveNeedsPanel, phase == GamePhase.WAVE_INCOMING
                || phase == GamePhase.WAVE_ACTIVE
                || phase == GamePhase.WAVE_PAUSED
                || phase == GamePhase.WAVE_RESOLVING);
            SetActive(waveCompleteWindow, phase == GamePhase.WAVE_COMPLETE
                && gameplayFacade != null && gameplayFacade.CurrentWaveResult != null);
            SetActive(upgradeSuggestionWindow, phase == GamePhase.UPGRADE);
            if (!AllowsWorkerAllocation(phase))
                SetActive(workerAllocationWindow, false);

            if (phase != GamePhase.AWAITING_WAVE)
            {
                SetActive(areaUnlockWindow, false);
                return;
            }

            // A newly eligible area is announced once per session; Later keeps the plot purchasable.
            var area = FindEligibleArea();
            if (area.HasValue && announcedAreas.Add(area.Value))
                SetActive(areaUnlockWindow, true);
        }

        private BuildingId? FindEligibleArea()
        {
            if (gameplayFacade == null)
                return null;

            foreach (var building in gameplayFacade.Buildings)
            {
                if (building.IsEligible && !building.IsOwned)
                    return building.BuildingId;
            }
            return null;
        }

        private static bool AllowsWorkerAllocation(GamePhase phase)
        {
            return phase == GamePhase.AWAITING_WAVE
                || phase == GamePhase.WAVE_INCOMING
                || phase == GamePhase.WAVE_ACTIVE
                || phase == GamePhase.WAVE_PAUSED;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }

        private static GameObject FindSceneObject(string path)
        {
            var parts = path.Split('/');
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0])
                    continue;

                var current = root.transform;
                for (var index = 1; index < parts.Length && current != null; index++)
                    current = current.Find(parts[index]);
                return current == null ? null : current.gameObject;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == path)
                    return root;
            }

            return null;
        }
    }
}
