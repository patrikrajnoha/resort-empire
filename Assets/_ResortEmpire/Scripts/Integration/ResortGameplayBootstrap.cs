using System;
using System.Collections.Generic;
using System.IO;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Staff;
using ResortEmpire.Gameplay.Waves;
using ResortEmpire.Gameplay.Progression;
using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// Scene-level owner for the one canonical Resort gameplay runtime.
    /// Without an injected dependency source it remains safely unconfigured.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResortGameplayBootstrap : MonoBehaviour, IResortGameplayUiFacade
    {
        private static ResortGameplayBootstrap activeInstance;

        [SerializeField]
        private MonoBehaviour dependencySourceBehaviour;

        [SerializeField]
        private bool advanceSimulation = true;

        private GameState gameState;
        private SimulationClock simulationClock;
        private WorkerManager workerManager;
        private WaveManager waveManager;
        private IWaveDefinitionProvider waveDefinitionProvider;
        private string configurationDiagnostic;
        private bool runtimeInitialized;
        private ResortProgressionRuntime progression;
        private bool applicationSuspended;
        private bool pausedByApplication;

        public bool IsConfigured => waveManager != null && workerManager != null;
        public string ConfigurationDiagnostic => configurationDiagnostic;
        public GamePhase CurrentPhase => gameState == null ? GamePhase.AWAITING_WAVE : gameState.CurrentState;
        public double SimulationTime => simulationClock == null ? 0d : simulationClock.CurrentTime;
        public double ArrivalWindowElapsed => waveManager == null ? 0d : waveManager.ArrivalWindowElapsed;
        public WaveSnapshot CurrentWaveSnapshot => waveManager == null ? null : waveManager.CaptureSnapshot();
        public IReadOnlyList<WorkerAssignmentSnapshot> CurrentWorkerAssignments =>
            workerManager == null ? EmptyWorkerAssignments : workerManager.Assignments;
        public ResourceSnapshot Resources => progression == null ? default : progression.Resources;
        public IReadOnlyList<BuildingStateSnapshot> Buildings => progression == null
            ? Array.Empty<BuildingStateSnapshot>() : progression.Buildings.States;
        public WaveResult CurrentWaveResult => progression?.CurrentWaveResult;
        public string PersistenceDiagnostic => progression?.PersistenceDiagnostic;

        public event Action<GamePhase, GamePhase> StateChanged;
        public event Action<NeedCompletionRecord> NeedCompleted;
        public event Action<WaveSnapshot> WaveCompleted;
        public event Action<WorkerAssignmentChange> WorkerAssignmentChanged;
        public event Action<ResourceSnapshot> ResourcesChanged;
        public event Action<IReadOnlyList<BuildingStateSnapshot>> BuildingsChanged;
        public event Action<WaveResult> WaveResultReady;

        private static IReadOnlyList<WorkerAssignmentSnapshot> EmptyWorkerAssignments { get; } =
            new WorkerAssignmentSnapshot[0];

        private void Awake()
        {
            if (!ClaimRuntimeOwnership())
                return;

            EnsureBaseRuntime();
            if (dependencySourceBehaviour == null)
            {
                SetUnconfigured("No IResortGameplayDependencySource is assigned; gameplay runtime is not configured.");
                return;
            }

            var productionSource = dependencySourceBehaviour as IResortProgressionDependencySource;
            if (productionSource != null)
            {
                try { TryConfigureProgression(productionSource.ProgressionConfig, productionSource.CreatePersistence()); }
                catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException
                    || exception is IOException || exception is UnauthorizedAccessException)
                { SetUnconfigured("Gameplay configuration failed: " + exception.Message); }
                return;
            }
            var dependencySource = dependencySourceBehaviour as IResortGameplayDependencySource;
            if (dependencySource == null)
            {
                SetUnconfigured("Assigned dependency source does not implement IResortGameplayDependencySource.");
                return;
            }

            TryConfigure(dependencySource);
        }

        private void Update()
        {
            if (!IsConfigured || !advanceSimulation || applicationSuspended)
                return;

            if (CurrentPhase != GamePhase.WAVE_INCOMING && CurrentPhase != GamePhase.WAVE_ACTIVE)
                return;

            var deltaSeconds = Time.deltaTime;
            if (deltaSeconds <= 0f)
                return;

            AdvanceSimulation(deltaSeconds);
        }

        /// <summary>Also supports deterministic manual stepping in integration tests.</summary>
        public void AdvanceSimulation(double deltaSeconds)
        {
            if (IsConfigured && !applicationSuspended) waveManager.Advance(deltaSeconds);
        }

        private void OnApplicationPause(bool paused)
        {
            if (applicationSuspended == paused) return;
            applicationSuspended = paused;
            if (!IsConfigured) return;
            if (paused)
            {
                pausedByApplication = waveManager.TryPauseWave();
                if (CurrentPhase == GamePhase.AWAITING_WAVE || CurrentPhase == GamePhase.WAVE_COMPLETE || CurrentPhase == GamePhase.UPGRADE)
                    progression?.TrySaveProgress();
            }
            else if (pausedByApplication)
            {
                waveManager.TryResumeWave();
                pausedByApplication = false;
            }
        }

        private void OnDestroy()
        {
            DisposeRuntimeSubscriptions();
            if (ReferenceEquals(activeInstance, this))
                activeInstance = null;
        }

        public bool TryConfigureProgression(ProgressionConfig config, IProgressionPersistence persistence)
        {
            if (!ClaimRuntimeOwnership() || IsConfigured) return false;
            try
            {
                var next = new ResortProgressionRuntime(config, persistence);
                progression = next;
                gameState = next.State;
                simulationClock = next.Clock;
                workerManager = next.Workers;
                waveManager = next.Waves;
                runtimeInitialized = true;
                configurationDiagnostic = null;
                gameState.StateChanged += HandleStateChanged;
                workerManager.AssignmentChanged += HandleWorkerAssignmentChanged;
                waveManager.NeedCompleted += HandleNeedCompleted;
                waveManager.WaveCompleted += HandleWaveCompleted;
                progression.ResourcesChanged += HandleResourcesChanged;
                progression.BuildingsChanged += HandleBuildingsChanged;
                progression.WaveResultReady += HandleWaveResultReady;
                progression.WorkerAssignmentChanged += HandleWorkerAssignmentChanged;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException
                || exception is IOException || exception is UnauthorizedAccessException)
            {
                SetUnconfigured("Gameplay configuration failed: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Injects the real composition dependencies. No production defaults are fabricated.
        /// </summary>
        public bool TryConfigure(IResortGameplayDependencySource dependencies)
        {
            if (!ClaimRuntimeOwnership())
                return false;
            if (IsConfigured)
            {
                configurationDiagnostic = "Gameplay runtime has already been configured and cannot be rebound.";
                return false;
            }

            EnsureBaseRuntime();
            if (dependencies == null)
            {
                SetUnconfigured("Gameplay dependency source is null.");
                return false;
            }

            if (dependencies.BuildingProvider == null
                || dependencies.WaveDefinitionProvider == null
                || dependencies.SimulationConfig == null
                || dependencies.InitialWorkerAssignments == null)
            {
                SetUnconfigured("Gameplay dependencies are incomplete; no partial runtime was created.");
                return false;
            }

            try
            {
                var nextState = new GameState();
                var nextClock = new SimulationClock();
                var nextWorkers = new WorkerManager(
                    dependencies.BuildingProvider,
                    nextState,
                    dependencies.InitialWorkerAssignments);
                var nextWaves = new WaveManager(
                    nextState,
                    nextClock,
                    dependencies.BuildingProvider,
                    nextWorkers,
                    dependencies.SimulationConfig);

                gameState = nextState;
                simulationClock = nextClock;
                workerManager = nextWorkers;
                waveManager = nextWaves;
                waveDefinitionProvider = dependencies.WaveDefinitionProvider;
                configurationDiagnostic = null;
                runtimeInitialized = true;

                gameState.StateChanged += HandleStateChanged;
                workerManager.AssignmentChanged += HandleWorkerAssignmentChanged;
                waveManager.NeedCompleted += HandleNeedCompleted;
                waveManager.WaveCompleted += HandleWaveCompleted;
                return true;
            }
            catch (ArgumentException exception)
            {
                SetUnconfigured("Gameplay dependencies are invalid: " + exception.Message);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                SetUnconfigured("Gameplay dependencies are invalid: " + exception.Message);
                return false;
            }
        }

        public ResortGameplayCommandResult TryStartWave()
        {
            if (progression != null) return ProgressionCommand(progression.TryStartWave);
            if (!IsConfigured)
                return ResortGameplayCommandResult.Unavailable(GetUnavailableMessage());

            var definition = waveDefinitionProvider.GetNextWave();
            if (definition == null)
                return ResortGameplayCommandResult.Unavailable("No wave definition is available.");

            return waveManager.StartWave(definition)
                ? ResortGameplayCommandResult.Success()
                : ResortGameplayCommandResult.Rejected("The current gameplay phase does not allow starting a wave.");
        }

        public ResortGameplayCommandResult TryPauseWave()
        {
            if (!IsConfigured)
                return ResortGameplayCommandResult.Unavailable(GetUnavailableMessage());

            return waveManager.TryPauseWave()
                ? ResortGameplayCommandResult.Success()
                : ResortGameplayCommandResult.Rejected("The current gameplay phase does not allow pausing the wave.");
        }

        public ResortGameplayCommandResult TryResumeWave()
        {
            if (!IsConfigured)
                return ResortGameplayCommandResult.Unavailable(GetUnavailableMessage());

            return waveManager.TryResumeWave()
                ? ResortGameplayCommandResult.Success()
                : ResortGameplayCommandResult.Rejected(
                    waveManager.ConfigurationDiagnostic ?? "The current gameplay phase does not allow resuming the wave.");
        }

        public ResortGameplayCommandResult TryReassignWorker(
            BuildingId sourceBuildingId,
            BuildingId destinationBuildingId)
        {
            if (progression != null) return ProgressionCommand(() => progression.TryReassignWorker(sourceBuildingId, destinationBuildingId));
            if (!IsConfigured)
                return ResortGameplayCommandResult.Unavailable(GetUnavailableMessage());

            return waveManager.TryReassignWorker(sourceBuildingId, destinationBuildingId)
                ? ResortGameplayCommandResult.Success()
                : ResortGameplayCommandResult.Rejected("The Worker reassignment was rejected by gameplay rules.");
        }

        public ResortGameplayCommandResult TryClaimWaveReward(string rewardId) =>
            ProgressionCommand(() => progression.TryClaimWaveReward(rewardId));
        public ResortGameplayCommandResult TryContinueAfterResults() => ProgressionCommand(() => progression.TryContinueAfterResults());
        public ResortGameplayCommandResult TryPurchaseBuilding(BuildingId buildingId) => ProgressionCommand(() => progression.TryPurchaseBuilding(buildingId));
        public ResortGameplayCommandResult TryUpgradeBuilding(BuildingId buildingId) => ProgressionCommand(() => progression.TryUpgradeBuilding(buildingId));
        public ResortGameplayCommandResult TrySaveProgress() => ProgressionCommand(() => progression.TrySaveProgress());

#if UNITY_EDITOR
        // Inspector actions exercise the real runtime while the presentation team wires buttons.
        [ContextMenu("Gameplay/Start Next Wave")]
        private void StartWaveFromInspector() => LogInspectorCommand(TryStartWave());
        [ContextMenu("Gameplay/Claim Wave Reward")]
        private void ClaimRewardFromInspector() => LogInspectorCommand(TryClaimWaveReward(CurrentWaveResult?.RewardId));
        [ContextMenu("Gameplay/Continue After Results")]
        private void ContinueFromInspector() => LogInspectorCommand(TryContinueAfterResults());
        private static void LogInspectorCommand(ResortGameplayCommandResult result)
        {
            if (result.Succeeded) Debug.Log("Gameplay command succeeded.");
            else Debug.LogWarning(result.Message);
        }
#endif

        private ResortGameplayCommandResult ProgressionCommand(Func<bool> command)
        {
            if (progression == null) return ResortGameplayCommandResult.Unavailable("The economy runtime is not configured.");
            if (command()) return ResortGameplayCommandResult.Success();
            return progression.LastFailureWasPersistence
                ? ResortGameplayCommandResult.Unavailable(progression.LastCommandError)
                : ResortGameplayCommandResult.Rejected(progression.LastCommandError);
        }

        private bool ClaimRuntimeOwnership()
        {
            if (activeInstance != null && !ReferenceEquals(activeInstance, this))
            {
                configurationDiagnostic = "Another ResortGameplayBootstrap already owns the active scene runtime.";
                enabled = false;
                return false;
            }

            activeInstance = this;
            return true;
        }

        private void EnsureBaseRuntime()
        {
            if (runtimeInitialized)
                return;

            gameState = new GameState();
            simulationClock = new SimulationClock();
            runtimeInitialized = true;
        }

        private void SetUnconfigured(string diagnostic)
        {
            DisposeRuntimeSubscriptions();
            workerManager = null;
            waveManager = null;
            waveDefinitionProvider = null;
            configurationDiagnostic = diagnostic;
            EnsureBaseRuntime();
        }

        private string GetUnavailableMessage()
        {
            return string.IsNullOrEmpty(configurationDiagnostic)
                ? "Gameplay runtime is not configured."
                : configurationDiagnostic;
        }

        private void DisposeRuntimeSubscriptions()
        {
            if (progression != null)
            {
                progression.ResourcesChanged -= HandleResourcesChanged;
                progression.BuildingsChanged -= HandleBuildingsChanged;
                progression.WaveResultReady -= HandleWaveResultReady;
                progression.WorkerAssignmentChanged -= HandleWorkerAssignmentChanged;
                progression.Dispose();
                progression = null;
            }
            if (gameState != null)
                gameState.StateChanged -= HandleStateChanged;
            if (workerManager != null)
                workerManager.AssignmentChanged -= HandleWorkerAssignmentChanged;
            if (waveManager != null)
            {
                waveManager.NeedCompleted -= HandleNeedCompleted;
                waveManager.WaveCompleted -= HandleWaveCompleted;
            }
        }

        private void HandleStateChanged(GamePhase previousState, GamePhase nextState)
        {
            if (StateChanged == null) return;
            foreach (Action<GamePhase, GamePhase> handler in StateChanged.GetInvocationList())
                try { handler(previousState, nextState); }
                catch (Exception exception) { Debug.LogException(exception); }
        }

        private void HandleNeedCompleted(NeedCompletionRecord record)
        {
            Notify(NeedCompleted, record);
        }

        private void HandleWaveCompleted(WaveSnapshot snapshot)
        {
            Notify(WaveCompleted, snapshot);
        }

        private void HandleWorkerAssignmentChanged(WorkerAssignmentChange change)
        {
            Notify(WorkerAssignmentChanged, change);
        }

        private void HandleResourcesChanged(ResourceSnapshot snapshot) => Notify(ResourcesChanged, snapshot);
        private void HandleBuildingsChanged(IReadOnlyList<BuildingStateSnapshot> snapshot) => Notify(BuildingsChanged, snapshot);
        private void HandleWaveResultReady(WaveResult result) => Notify(WaveResultReady, result);
        private static void Notify<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
                try { handler(value); }
                catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
