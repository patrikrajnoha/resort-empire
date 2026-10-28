using System;
using System.Collections.Generic;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Staff;
using ResortEmpire.Gameplay.Waves;
using ResortEmpire.Gameplay.Progression;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// Simulation-only injection boundary retained for custom providers and existing tests.
    /// The production scene uses IResortProgressionDependencySource for economy and persistence.
    /// </summary>
    public interface IResortGameplayDependencySource
    {
        IBuildingSnapshotProvider BuildingProvider { get; }
        IWaveDefinitionProvider WaveDefinitionProvider { get; }
        WaveSimulationConfig SimulationConfig { get; }
        IReadOnlyList<BuildingId> InitialWorkerAssignments { get; }
    }

    /// <summary>Production composition input. Legacy simulation-only dependency sources remain supported.</summary>
    public interface IResortProgressionDependencySource
    {
        ProgressionConfig ProgressionConfig { get; }
        IProgressionPersistence CreatePersistence();
    }

    /// <summary>Supplies scripted waves without making the UI own wave data.</summary>
    public interface IWaveDefinitionProvider
    {
        WaveDefinition GetNextWave();
    }

    public enum ResortGameplayCommandStatus
    {
        Succeeded = 0,
        Rejected = 1,
        Unavailable = 2
    }

    public readonly struct ResortGameplayCommandResult
    {
        private ResortGameplayCommandResult(ResortGameplayCommandStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        public ResortGameplayCommandStatus Status { get; }
        public string Message { get; }
        public bool Succeeded => Status == ResortGameplayCommandStatus.Succeeded;

        public static ResortGameplayCommandResult Success(string message = null)
        {
            return new ResortGameplayCommandResult(ResortGameplayCommandStatus.Succeeded, message);
        }

        public static ResortGameplayCommandResult Rejected(string message)
        {
            return new ResortGameplayCommandResult(ResortGameplayCommandStatus.Rejected, message);
        }

        public static ResortGameplayCommandResult Unavailable(string message)
        {
            return new ResortGameplayCommandResult(ResortGameplayCommandStatus.Unavailable, message);
        }
    }

    /// <summary>
    /// Presentation-facing boundary over the canonical gameplay runtime.
    /// It exposes immutable observations and validated commands only.
    /// </summary>
    public interface IResortGameplayUiFacade
    {
        bool IsConfigured { get; }
        string ConfigurationDiagnostic { get; }
        GamePhase CurrentPhase { get; }
        double SimulationTime { get; }
        double ArrivalWindowElapsed { get; }
        WaveSnapshot CurrentWaveSnapshot { get; }
        IReadOnlyList<WorkerAssignmentSnapshot> CurrentWorkerAssignments { get; }
        ResourceSnapshot Resources { get; }
        IReadOnlyList<BuildingStateSnapshot> Buildings { get; }
        WaveResult CurrentWaveResult { get; }
        string PersistenceDiagnostic { get; }

        event Action<GamePhase, GamePhase> StateChanged;
        event Action<NeedCompletionRecord> NeedCompleted;
        event Action<WaveSnapshot> WaveCompleted;
        event Action<WorkerAssignmentChange> WorkerAssignmentChanged;
        event Action<ResourceSnapshot> ResourcesChanged;
        event Action<IReadOnlyList<BuildingStateSnapshot>> BuildingsChanged;
        event Action<WaveResult> WaveResultReady;

        ResortGameplayCommandResult TryStartWave();
        ResortGameplayCommandResult TryPauseWave();
        ResortGameplayCommandResult TryResumeWave();
        ResortGameplayCommandResult TryReassignWorker(BuildingId sourceBuildingId, BuildingId destinationBuildingId);
        ResortGameplayCommandResult TryClaimWaveReward(string rewardId);
        ResortGameplayCommandResult TryContinueAfterResults();
        ResortGameplayCommandResult TryPurchaseBuilding(BuildingId buildingId);
        ResortGameplayCommandResult TryUpgradeBuilding(BuildingId buildingId);
        ResortGameplayCommandResult TrySaveProgress();
    }
}
