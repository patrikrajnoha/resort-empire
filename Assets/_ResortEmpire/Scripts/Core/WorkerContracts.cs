using System;
using System.Collections.Generic;

namespace ResortEmpire.Gameplay
{
    /// <summary>Immutable view of one anonymous Prototype Worker assignment.</summary>
    public readonly struct WorkerAssignmentSnapshot : IEquatable<WorkerAssignmentSnapshot>
    {
        public WorkerAssignmentSnapshot(int workerId, BuildingId buildingId)
        {
            if (workerId <= 0)
                throw new ArgumentOutOfRangeException(nameof(workerId), workerId, "Worker identifier must be positive.");
            if (!Enum.IsDefined(typeof(BuildingId), buildingId))
                throw new ArgumentOutOfRangeException(nameof(buildingId), buildingId, "Unknown building identifier.");

            WorkerId = workerId;
            BuildingId = buildingId;
        }

        public int WorkerId { get; }
        public BuildingId BuildingId { get; }

        public bool Equals(WorkerAssignmentSnapshot other)
        {
            return WorkerId == other.WorkerId && BuildingId == other.BuildingId;
        }

        public override bool Equals(object obj)
        {
            return obj is WorkerAssignmentSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (WorkerId * 397) ^ (int)BuildingId;
        }
    }

    /// <summary>Details of a completed zero-cost Worker move.</summary>
    public readonly struct WorkerAssignmentChange : IEquatable<WorkerAssignmentChange>
    {
        public WorkerAssignmentChange(int workerId, BuildingId sourceBuildingId, BuildingId destinationBuildingId)
        {
            if (workerId <= 0)
                throw new ArgumentOutOfRangeException(nameof(workerId), workerId, "Worker identifier must be positive.");
            if (!Enum.IsDefined(typeof(BuildingId), sourceBuildingId))
                throw new ArgumentOutOfRangeException(nameof(sourceBuildingId), sourceBuildingId, "Unknown source building identifier.");
            if (!Enum.IsDefined(typeof(BuildingId), destinationBuildingId))
                throw new ArgumentOutOfRangeException(nameof(destinationBuildingId), destinationBuildingId, "Unknown destination building identifier.");
            if (sourceBuildingId == destinationBuildingId)
                throw new ArgumentException("Source and destination buildings must differ.");

            WorkerId = workerId;
            SourceBuildingId = sourceBuildingId;
            DestinationBuildingId = destinationBuildingId;
        }

        public int WorkerId { get; }
        public BuildingId SourceBuildingId { get; }
        public BuildingId DestinationBuildingId { get; }

        public bool Equals(WorkerAssignmentChange other)
        {
            return WorkerId == other.WorkerId
                && SourceBuildingId == other.SourceBuildingId
                && DestinationBuildingId == other.DestinationBuildingId;
        }

        public override bool Equals(object obj)
        {
            return obj is WorkerAssignmentChange other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = WorkerId;
                hash = (hash * 397) ^ (int)SourceBuildingId;
                return (hash * 397) ^ (int)DestinationBuildingId;
            }
        }
    }

    /// <summary>
    /// Narrow read-only/runtime command boundary consumed by WaveManager.
    /// Building ownership and Worker creation remain outside the wave system.
    /// </summary>
    public interface IWorkerAssignmentProvider
    {
        IReadOnlyList<WorkerAssignmentSnapshot> Assignments { get; }
        int WorkerCount { get; }
        int GetAssignedWorkerCount(BuildingId buildingId);
        bool TryReassignWorker(BuildingId sourceBuildingId, BuildingId destinationBuildingId);
    }

    /// <summary>
    /// Read-only active-wave configuration policy shared by WorkerManager and WaveManager.
    /// A missing guard means the WorkerManager is operating independently between waves.
    /// </summary>
    public interface IActiveWaveConfigurationGuard
    {
        bool IsConfigurationStable { get; }
        string ConfigurationDiagnostic { get; }
    }

    /// <summary>One-time binding point for the active-wave guard used by a WorkerManager.</summary>
    public interface IWorkerAssignmentConfigurationHost
    {
        void AttachConfigurationGuard(IActiveWaveConfigurationGuard guard);
    }
}
