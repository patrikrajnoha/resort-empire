using System;
using System.Collections.Generic;

namespace ResortEmpire.Gameplay
{
    /// <summary>Stable identifiers for ordinary wave buildings in the Prototype.</summary>
    public enum BuildingId
    {
        Reception = 0,
        Restaurant = 1,
        BeachBar = 2,
        Pool = 3,
        Bungalows = 4
    }

    /// <summary>Stable identifiers for ordinary wave needs in the Prototype.</summary>
    public enum NeedType
    {
        Rooms = 0,
        Drinks = 1,
        Food = 2,
        Relax = 3,
        OceanView = 4
    }

    /// <summary>The approved Prototype mapping from a wave building to its need.</summary>
    public static class BuildingNeedMapping
    {
        public static NeedType GetNeedType(BuildingId buildingId)
        {
            switch (buildingId)
            {
                case BuildingId.Reception:
                    return NeedType.Rooms;
                case BuildingId.Restaurant:
                    return NeedType.Food;
                case BuildingId.BeachBar:
                    return NeedType.Drinks;
                case BuildingId.Pool:
                    return NeedType.Relax;
                case BuildingId.Bungalows:
                    return NeedType.OceanView;
                default:
                    throw new ArgumentOutOfRangeException(nameof(buildingId), buildingId, "Unknown building identifier.");
            }
        }

        public static BuildingId GetBuildingId(NeedType needType)
        {
            switch (needType)
            {
                case NeedType.Rooms:
                    return BuildingId.Reception;
                case NeedType.Drinks:
                    return BuildingId.BeachBar;
                case NeedType.Food:
                    return BuildingId.Restaurant;
                case NeedType.Relax:
                    return BuildingId.Pool;
                case NeedType.OceanView:
                    return BuildingId.Bungalows;
                default:
                    throw new ArgumentOutOfRangeException(nameof(needType), needType, "Unknown need identifier.");
            }
        }
    }

    /// <summary>
    /// Immutable building data supplied by the future BuildingManager.
    /// This type contains configuration and ownership state only; it performs no transactions.
    /// </summary>
    public readonly struct BuildingSnapshot : IEquatable<BuildingSnapshot>
    {
        public BuildingSnapshot(
            BuildingId buildingId,
            bool isOwned,
            int capacity,
            double serviceDuration,
            int workerSlotCount)
        {
            if (!Enum.IsDefined(typeof(BuildingId), buildingId))
                throw new ArgumentOutOfRangeException(nameof(buildingId), buildingId, "Unknown building identifier.");
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            if (!IsFinitePositive(serviceDuration))
                throw new ArgumentOutOfRangeException(nameof(serviceDuration), serviceDuration, "Service duration must be finite and positive.");
            if (workerSlotCount < 0)
                throw new ArgumentOutOfRangeException(nameof(workerSlotCount), workerSlotCount, "Worker slot count cannot be negative.");

            BuildingId = buildingId;
            NeedType = BuildingNeedMapping.GetNeedType(buildingId);
            IsOwned = isOwned;
            Capacity = capacity;
            ServiceDuration = serviceDuration;
            WorkerSlotCount = workerSlotCount;
        }

        public BuildingId BuildingId { get; }
        public NeedType NeedType { get; }
        public bool IsOwned { get; }
        public int Capacity { get; }
        public double ServiceDuration { get; }
        public int WorkerSlotCount { get; }

        public bool Equals(BuildingSnapshot other)
        {
            return BuildingId == other.BuildingId
                && NeedType == other.NeedType
                && IsOwned == other.IsOwned
                && Capacity == other.Capacity
                && ServiceDuration.Equals(other.ServiceDuration)
                && WorkerSlotCount == other.WorkerSlotCount;
        }

        public override bool Equals(object obj)
        {
            return obj is BuildingSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)BuildingId;
                hash = (hash * 397) ^ (int)NeedType;
                hash = (hash * 397) ^ IsOwned.GetHashCode();
                hash = (hash * 397) ^ Capacity;
                hash = (hash * 397) ^ ServiceDuration.GetHashCode();
                return (hash * 397) ^ WorkerSlotCount;
            }
        }

        private static bool IsFinitePositive(double value)
        {
            return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>Read-only source of building data for future gameplay systems.</summary>
    public interface IBuildingSnapshotProvider
    {
        IReadOnlyList<BuildingSnapshot> Buildings { get; }
    }

    /// <summary>
    /// Immutable result for one completed need unit. Waiting ends when service starts;
    /// service duration is intentionally excluded from WaitingDuration.
    /// </summary>
    public readonly struct NeedCompletionRecord : IEquatable<NeedCompletionRecord>
    {
        public NeedCompletionRecord(
            long needUnitId,
            int waveId,
            NeedType needType,
            BuildingId buildingId,
            double arrivalTime,
            double serviceStartTime,
            double serviceCompletionTime)
        {
            if (needUnitId <= 0)
                throw new ArgumentOutOfRangeException(nameof(needUnitId), needUnitId, "Need-unit identifier must be positive.");
            if (waveId <= 0)
                throw new ArgumentOutOfRangeException(nameof(waveId), waveId, "Wave identifier must be positive.");
            if (!Enum.IsDefined(typeof(NeedType), needType))
                throw new ArgumentOutOfRangeException(nameof(needType), needType, "Unknown need identifier.");
            if (!Enum.IsDefined(typeof(BuildingId), buildingId))
                throw new ArgumentOutOfRangeException(nameof(buildingId), buildingId, "Unknown building identifier.");
            if (BuildingNeedMapping.GetBuildingId(needType) != buildingId)
                throw new ArgumentException("The building and need identifiers do not match the approved mapping.");
            if (!IsFiniteNonNegative(arrivalTime))
                throw new ArgumentOutOfRangeException(nameof(arrivalTime), arrivalTime, "Arrival time must be finite and non-negative.");
            if (!IsFiniteNonNegative(serviceStartTime))
                throw new ArgumentOutOfRangeException(nameof(serviceStartTime), serviceStartTime, "Service-start time must be finite and non-negative.");
            if (!IsFiniteNonNegative(serviceCompletionTime))
                throw new ArgumentOutOfRangeException(nameof(serviceCompletionTime), serviceCompletionTime, "Service-completion time must be finite and non-negative.");
            if (serviceStartTime < arrivalTime)
                throw new ArgumentException("Service cannot start before arrival.", nameof(serviceStartTime));
            if (serviceCompletionTime < serviceStartTime)
                throw new ArgumentException("Service cannot complete before it starts.", nameof(serviceCompletionTime));

            NeedUnitId = needUnitId;
            WaveId = waveId;
            NeedType = needType;
            BuildingId = buildingId;
            ArrivalTime = arrivalTime;
            ServiceStartTime = serviceStartTime;
            ServiceCompletionTime = serviceCompletionTime;
            WaitingDuration = serviceStartTime - arrivalTime;
        }

        public long NeedUnitId { get; }
        public int WaveId { get; }
        public NeedType NeedType { get; }
        public BuildingId BuildingId { get; }
        public double ArrivalTime { get; }
        public double ServiceStartTime { get; }
        public double ServiceCompletionTime { get; }
        public double WaitingDuration { get; }

        public bool Equals(NeedCompletionRecord other)
        {
            return NeedUnitId == other.NeedUnitId
                && WaveId == other.WaveId
                && NeedType == other.NeedType
                && BuildingId == other.BuildingId
                && ArrivalTime.Equals(other.ArrivalTime)
                && ServiceStartTime.Equals(other.ServiceStartTime)
                && ServiceCompletionTime.Equals(other.ServiceCompletionTime)
                && WaitingDuration.Equals(other.WaitingDuration);
        }

        public override bool Equals(object obj)
        {
            return obj is NeedCompletionRecord other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = NeedUnitId.GetHashCode();
                hash = (hash * 397) ^ WaveId;
                hash = (hash * 397) ^ (int)NeedType;
                hash = (hash * 397) ^ (int)BuildingId;
                hash = (hash * 397) ^ ArrivalTime.GetHashCode();
                hash = (hash * 397) ^ ServiceStartTime.GetHashCode();
                return (hash * 397) ^ ServiceCompletionTime.GetHashCode();
            }
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
