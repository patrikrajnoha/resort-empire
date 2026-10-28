using System;

namespace ResortEmpire.Gameplay.Waves
{
    public enum NeedUnitState
    {
        WAITING = 0,
        IN_SERVICE = 1,
        COMPLETED = 2
    }

    /// <summary>Mutable runtime state for one anonymous need unit, never a guest entity.</summary>
    public sealed class NeedRuntime
    {
        public NeedRuntime(long needUnitId, int waveId, NeedType needType, double arrivalTime)
        {
            if (needUnitId <= 0)
                throw new ArgumentOutOfRangeException(nameof(needUnitId), needUnitId, "Need-unit identifier must be positive.");
            if (waveId <= 0)
                throw new ArgumentOutOfRangeException(nameof(waveId), waveId, "Wave identifier must be positive.");
            if (!Enum.IsDefined(typeof(NeedType), needType))
                throw new ArgumentOutOfRangeException(nameof(needType), needType, "Unknown need identifier.");
            if (!IsFiniteNonNegative(arrivalTime))
                throw new ArgumentOutOfRangeException(nameof(arrivalTime), arrivalTime, "Arrival time must be finite and non-negative.");

            NeedUnitId = needUnitId;
            WaveId = waveId;
            NeedType = needType;
            BuildingId = BuildingNeedMapping.GetBuildingId(needType);
            ArrivalTime = arrivalTime;
            State = NeedUnitState.WAITING;
        }

        public long NeedUnitId { get; }
        public int WaveId { get; }
        public NeedType NeedType { get; }
        public BuildingId BuildingId { get; }
        public double ArrivalTime { get; }
        public double? ServiceStartTime { get; private set; }
        public double? ServiceCompletionTime { get; private set; }
        public NeedUnitState State { get; private set; }
        public double WaitingDuration => ServiceStartTime.HasValue ? ServiceStartTime.Value - ArrivalTime : 0d;

        public bool TryStartService(double serviceStartTime)
        {
            if (State != NeedUnitState.WAITING || !IsFiniteNonNegative(serviceStartTime) || serviceStartTime < ArrivalTime)
                return false;

            ServiceStartTime = serviceStartTime;
            State = NeedUnitState.IN_SERVICE;
            return true;
        }

        public bool TryComplete(double serviceCompletionTime)
        {
            if (State != NeedUnitState.IN_SERVICE
                || !ServiceStartTime.HasValue
                || !IsFiniteNonNegative(serviceCompletionTime)
                || serviceCompletionTime < ServiceStartTime.Value)
                return false;

            ServiceCompletionTime = serviceCompletionTime;
            State = NeedUnitState.COMPLETED;
            return true;
        }

        public NeedCompletionRecord GetCompletionRecord()
        {
            if (State != NeedUnitState.COMPLETED || !ServiceStartTime.HasValue || !ServiceCompletionTime.HasValue)
                throw new InvalidOperationException("A completion record is available only after service completion.");

            return new NeedCompletionRecord(
                NeedUnitId,
                WaveId,
                NeedType,
                BuildingId,
                ArrivalTime,
                ServiceStartTime.Value,
                ServiceCompletionTime.Value);
        }

        public NeedRuntimeSnapshot CaptureSnapshot()
        {
            return new NeedRuntimeSnapshot(
                NeedUnitId,
                WaveId,
                NeedType,
                BuildingId,
                ArrivalTime,
                ServiceStartTime,
                ServiceCompletionTime,
                State);
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>Immutable UI/save-friendly view of a need unit.</summary>
    public readonly struct NeedRuntimeSnapshot : IEquatable<NeedRuntimeSnapshot>
    {
        public NeedRuntimeSnapshot(
            long needUnitId,
            int waveId,
            NeedType needType,
            BuildingId buildingId,
            double arrivalTime,
            double? serviceStartTime,
            double? serviceCompletionTime,
            NeedUnitState state)
        {
            NeedUnitId = needUnitId;
            WaveId = waveId;
            NeedType = needType;
            BuildingId = buildingId;
            ArrivalTime = arrivalTime;
            ServiceStartTime = serviceStartTime;
            ServiceCompletionTime = serviceCompletionTime;
            State = state;
        }

        public long NeedUnitId { get; }
        public int WaveId { get; }
        public NeedType NeedType { get; }
        public BuildingId BuildingId { get; }
        public double ArrivalTime { get; }
        public double? ServiceStartTime { get; }
        public double? ServiceCompletionTime { get; }
        public NeedUnitState State { get; }

        public bool Equals(NeedRuntimeSnapshot other)
        {
            return NeedUnitId == other.NeedUnitId
                && WaveId == other.WaveId
                && NeedType == other.NeedType
                && BuildingId == other.BuildingId
                && ArrivalTime.Equals(other.ArrivalTime)
                && Nullable.Equals(ServiceStartTime, other.ServiceStartTime)
                && Nullable.Equals(ServiceCompletionTime, other.ServiceCompletionTime)
                && State == other.State;
        }

        public override bool Equals(object obj)
        {
            return obj is NeedRuntimeSnapshot other && Equals(other);
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
                hash = (hash * 397) ^ (ServiceStartTime.HasValue ? ServiceStartTime.Value.GetHashCode() : 0);
                hash = (hash * 397) ^ (ServiceCompletionTime.HasValue ? ServiceCompletionTime.Value.GetHashCode() : 0);
                return (hash * 397) ^ (int)State;
            }
        }
    }
}
