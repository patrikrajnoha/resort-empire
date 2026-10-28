using System;
using System.Collections.Generic;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Staff;
using ResortEmpire.Gameplay.Waves;
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

        public bool IsConfigured => waveManager != null && workerManager != null;
        public string ConfigurationDiagnostic => configurationDiagnostic;
        public GamePhase CurrentPhase => gameState == null ? GamePhase.AWAITING_WAVE : gameState.CurrentState;
        public double SimulationTime => simulationClock == null ? 0d : simulationClock.CurrentTime;
        public double ArrivalWindowElapsed => waveManager == null ? 0d : waveManager.ArrivalWindowElapsed;
        public WaveSnapshot CurrentWaveSnapshot => waveManager == null ? null : waveManager.CaptureSnapshot();
        public IReadOnlyList<WorkerAssignmentSnapshot> CurrentWorkerAssignments =>
            workerManager == null ? EmptyWorkerAssignments : workerManager.Assignments;

        public event Action<GamePhase, GamePhase> StateChanged;
        public event Action<NeedCompletionRecord> NeedCompleted;
        public event Action<WaveSnapshot> WaveCompleted;
        public event Action<WorkerAssignmentChange> WorkerAssignmentChanged;

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
            if (!IsConfigured || !advanceSimulation)
                return;

            if (CurrentPhase != GamePhase.WAVE_INCOMING && CurrentPhase != GamePhase.WAVE_ACTIVE)
                return;

            var deltaSeconds = Time.deltaTime;
            if (deltaSeconds <= 0f)
                return;

            waveManager.Advance(deltaSeconds);
        }

        private void OnDestroy()
        {
            DisposeRuntimeSubscriptions();
            if (ReferenceEquals(activeInstance, this))
                activeInstance = null;
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
            if (!IsConfigured)
                return ResortGameplayCommandResult.Unavailable(GetUnavailableMessage());

            return waveManager.TryReassignWorker(sourceBuildingId, destinationBuildingId)
                ? ResortGameplayCommandResult.Success()
                : ResortGameplayCommandResult.Rejected("The Worker reassignment was rejected by gameplay rules.");
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
            StateChanged?.Invoke(previousState, nextState);
        }

        private void HandleNeedCompleted(NeedCompletionRecord record)
        {
            NeedCompleted?.Invoke(record);
        }

        private void HandleWaveCompleted(WaveSnapshot snapshot)
        {
            WaveCompleted?.Invoke(snapshot);
        }

        private void HandleWorkerAssignmentChanged(WorkerAssignmentChange change)
        {
            WorkerAssignmentChanged?.Invoke(change);
        }
    }
}
