using System;
using System.Collections.Generic;
using System.IO;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Staff;
using ResortEmpire.Gameplay.Waves;
using ResortEmpire.Gameplay.Progression;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.SceneManagement;
#endif

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// Scene-level owner for the one canonical Resort gameplay runtime.
    /// Without an injected dependency source it remains safely unconfigured.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResortGameplayBootstrap : MonoBehaviour, IResortGameplayUiFacade, ISuddenRushUiFacade
    {
        private static ResortGameplayBootstrap activeInstance;

        [SerializeField]
        private MonoBehaviour dependencySourceBehaviour;

        [SerializeField]
        private bool advanceSimulation = true;

        [Tooltip("Optional override implementing IEnergyBoostService. The progression runtime provides the default.")]
        [SerializeField] private MonoBehaviour energyBoostBehaviour;

        private GameState gameState;
        private SimulationClock simulationClock;
        private WorkerManager workerManager;
        private WaveManager waveManager;
        private IWaveDefinitionProvider waveDefinitionProvider;
        private ProgressionConfig progressionConfig;
        private string configurationDiagnostic;
        private bool runtimeInitialized;
        private ResortProgressionRuntime progression;
        private IProgressionPersistence progressionPersistence;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool restarting;
        private int debugSimulationSpeed = 1;
#endif
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
        public int CompletedWaves => progression?.ClaimedWaves ?? 0;
        public int TotalWaves => progression?.TotalWaves ?? 0;
        public ProgressionConfig ProgressionConfiguration => progressionConfig?.Copy();
        public int EnergyBoostCost => progression?.EnergyBoostCost ?? 0;
        public bool HasUsedEnergyBoost(BuildingId buildingId) => waveManager?.HasUsedServiceBoost(buildingId) ?? false;
        public double EnergyBoostRemaining(BuildingId buildingId) => waveManager?.ServiceBoostRemaining(buildingId) ?? 0d;

        public SuddenRushNotice CurrentSuddenRush
        {
            get
            {
                if (CurrentPhase != GamePhase.WAVE_ACTIVE && CurrentPhase != GamePhase.WAVE_PAUSED)
                    return default;
                var rush = waveManager?.CaptureSuddenRush();
                return rush == null || rush.NotificationDismissed ? default
                    : new SuddenRushNotice(rush.EventId, rush.BuildingId, rush.DemandUnits, rush.Duration);
            }
        }

        public ResortGameplayCommandResult TryDismissSuddenRush(string eventId) =>
            waveManager != null && waveManager.TryDismissSuddenRush(eventId)
                ? ResortGameplayCommandResult.Success()
                : ResortGameplayCommandResult.Rejected("This Sudden Rush notification is no longer available.");

        public ResortGameplayCommandResult GetEnergyBoostAvailability(BuildingId buildingId)
        {
            if (!IsConfigured || CurrentPhase != GamePhase.WAVE_ACTIVE)
                return ResortGameplayCommandResult.Rejected("Energy boosts are available while the wave is running.");
            var restriction = waveManager.ServiceBoostRestriction(buildingId);
            if (restriction != null) return ResortGameplayCommandResult.Rejected(restriction);
            if (energyBoostBehaviour is IEnergyBoostService boost) return boost.GetAvailability(buildingId);
            if (progression == null) return ResortGameplayCommandResult.Unavailable("Energy boost unavailable.");
            restriction = progression.EnergyBoostRestriction(buildingId);
            return restriction == null ? ResortGameplayCommandResult.Success() : ResortGameplayCommandResult.Rejected(restriction);
        }

        public ResortGameplayCommandResult TryUseEnergyBoost(BuildingId buildingId) =>
            TryUseEnergyBoost(buildingId, null);

        public ResortGameplayCommandResult TryUseEnergyBoost(BuildingId buildingId, string dismissRushId)
        {
            var availability = GetEnergyBoostAvailability(buildingId);
            if (!availability.Succeeded) return availability;
            if (!(energyBoostBehaviour is IEnergyBoostService boost))
                return ProgressionCommand(() => progression.TryUseEnergyBoost(buildingId, dismissRushId));
            var result = boost.TryUseBoost(buildingId);
            if (result.Succeeded && dismissRushId != null) TryDismissSuddenRush(dismissRushId);
            return result;
        }

        public WaveForecast NextWaveForecast
        {
            get
            {
                var next = progression?.GetNextWave();
                if (next == null) return default;
                var demand = 0;
                foreach (var need in next.Demand) demand += need.DemandTotal;
                return new WaveForecast(next.WaveId, demand);
            }
        }

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
                catch (Exception exception) when (IsConfigurationFailure(exception))
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (restarting) return;
            if (!DebugModalState.BlocksInput && Input.GetKeyDown(KeyCode.R))
            {
                RestartGame();
                return;
            }
#endif
            if (!IsConfigured || !advanceSimulation || applicationSuspended)
                return;

            if (CurrentPhase != GamePhase.WAVE_INCOMING && CurrentPhase != GamePhase.WAVE_ACTIVE)
                return;

            var deltaSeconds = Time.deltaTime;
            if (deltaSeconds <= 0f)
                return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AdvanceSimulation((double)deltaSeconds * debugSimulationSpeed);
#else
            AdvanceSimulation(deltaSeconds);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void RestartGame()
        {
            var scene = gameObject.scene;
            if (!IsConfigured || progression == null || applicationSuspended || !scene.IsValid()
                || scene.buildIndex < 0 || !Application.CanStreamedLevelBeLoaded(scene.buildIndex))
            {
                Debug.LogError("Cannot reset: gameplay is unavailable or the scene is not in Build Settings.", this);
                return;
            }

            if (!(progressionPersistence is JsonFileProgressionPersistence slotPersistence))
            {
                Debug.LogError("Cannot reset: the active save slot cannot be verified.", this);
                return;
            }

            var activeSlot = SaveSlotStorage.GetActiveSlot(Application.persistentDataPath);
            var activePath = Path.GetFullPath(SaveSlotStorage.GetSlotPath(Application.persistentDataPath, activeSlot));
            if (!string.Equals(slotPersistence.FilePath, activePath, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("Cannot reset: the loaded save does not match the active slot.", this);
                return;
            }

            restarting = true;
            if (!progression.TryPrepareNewGame())
            {
                restarting = false;
                Debug.LogError(progression.LastCommandError ?? "Cannot save a fresh game.", this);
                return;
            }
            advanceSimulation = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(scene.buildIndex);
        }
#endif

        /// <summary>Also supports deterministic manual stepping in integration tests.</summary>
        public void AdvanceSimulation(double deltaSeconds)
        {
            if (IsConfigured && !applicationSuspended) waveManager.Advance(deltaSeconds);
        }

        private void OnApplicationPause(bool paused)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (restarting) return;
#endif
            if (applicationSuspended == paused) return;
            applicationSuspended = paused;
            if (!IsConfigured) return;
            if (paused)
            {
                pausedByApplication = waveManager.TryPauseWave();
                if (CurrentPhase == GamePhase.AWAITING_WAVE || CurrentPhase == GamePhase.WAVE_COMPLETE
                    || CurrentPhase == GamePhase.UPGRADE || progression?.CanSaveActiveWave == true)
                    progression?.TrySaveProgress();
            }
            else if (pausedByApplication)
            {
                waveManager.TryResumeWave();
                pausedByApplication = false;
            }
        }

        private void OnApplicationQuit()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (restarting) return;
#endif
            if (progression?.CanSaveActiveWave == true)
                progression.TrySaveProgress();
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
                progressionPersistence = persistence;
                progressionConfig = config.Copy();
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
            catch (Exception exception) when (IsConfigurationFailure(exception))
            {
                SetUnconfigured("Gameplay configuration failed: " + exception.Message);
                return false;
            }
        }

        /// <summary>Rebinds the canonical runtime to a selected persistence slot between gameplay sessions.</summary>
        public bool TryReloadProgression(IProgressionPersistence persistence)
        {
            if (persistence == null || progressionConfig == null)
                return false;

            DisposeRuntimeSubscriptions();
            workerManager = null;
            waveManager = null;
            waveDefinitionProvider = null;
            gameState = null;
            simulationClock = null;
            runtimeInitialized = false;
            return TryConfigureProgression(progressionConfig, persistence);
        }

        // InvalidDataException (malformed or inconsistent save) is not an IOException.
        private static bool IsConfigurationFailure(Exception exception) =>
            exception is ArgumentException || exception is InvalidOperationException || exception is IOException
            || exception is InvalidDataException || exception is UnauthorizedAccessException;

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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public ResortGameplayCommandResult TryDebugAddResources(int coins, int energy) =>
            ProgressionCommand(() => progression.TryDebugAddResources(coins, energy));

        internal ResortGameplayCommandResult TryDebugSetBuildingLevel(BuildingId? building, int level) =>
            ProgressionCommand(() => progression.TryDebugSetBuildingLevel(building, level));
        internal ResortGameplayCommandResult TryDebugUnlockAvailableBuildings() =>
            ProgressionCommand(progression == null ? null : progression.TryDebugUnlockAvailableBuildings);
        internal ResortGameplayCommandResult TryDebugResetWorkers() =>
            ProgressionCommand(progression == null ? null : progression.TryDebugResetWorkers);
        internal string DebugCheckpointStatus => progression?.DebugCheckpointStatus ?? "Unavailable";
        internal int? DebugActiveSlot
        {
            get
            {
                if (!(progressionPersistence is JsonFileProgressionPersistence file)) return null;
                var root = Application.persistentDataPath;
                var slot = SaveSlotStorage.GetActiveSlot(root);
                var path = Path.GetFullPath(SaveSlotStorage.GetSlotPath(root, slot));
                return string.Equals(file.FilePath, path, StringComparison.Ordinal) ? slot : (int?)null;
            }
        }

        internal ResortGameplayCommandResult TryDebugSetSimulationSpeed(int multiplier)
        {
            if (multiplier < 1 || multiplier > 3)
                return ResortGameplayCommandResult.Rejected("Speed must be 1, 2 or 3.");
            debugSimulationSpeed = multiplier;
            return ResortGameplayCommandResult.Success();
        }

        internal ResortGameplayCommandResult TryDebugCompleteWave()
        {
            if (progression == null || applicationSuspended || restarting
                || (CurrentPhase != GamePhase.WAVE_INCOMING && CurrentPhase != GamePhase.WAVE_ACTIVE))
                return ResortGameplayCommandResult.Rejected("Start or resume a wave first.");
            // Use the same deterministic arrival, service and completion path as normal play.
            AdvanceSimulation(10000d);
            if (CurrentPhase != GamePhase.WAVE_COMPLETE)
                return ResortGameplayCommandResult.Rejected(waveManager.ConfigurationDiagnostic
                    ?? "The wave did not finish within the simulation step.");
            return progression.PersistenceDiagnostic == null ? ResortGameplayCommandResult.Success()
                : ResortGameplayCommandResult.Unavailable(progression.PersistenceDiagnostic);
        }
#endif

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
            Debug.LogWarning(diagnostic, this);
        }

        private string GetUnavailableMessage()
        {
            return string.IsNullOrEmpty(configurationDiagnostic)
                ? "Gameplay runtime is not configured."
                : configurationDiagnostic;
        }

        private void DisposeRuntimeSubscriptions()
        {
            progressionPersistence = null;
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
