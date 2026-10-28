using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ResortEmpire.Gameplay.Waves
{
    /// <summary>Read-only progress view for one occupied discrete service seat.</summary>
    public readonly struct ServiceSlotSnapshot : IEquatable<ServiceSlotSnapshot>
    {
        public ServiceSlotSnapshot(long needUnitId, double normalizedProgress)
        {
            if (needUnitId <= 0)
                throw new ArgumentOutOfRangeException(nameof(needUnitId), needUnitId, "Need-unit identifier must be positive.");
            if (normalizedProgress < 0d || normalizedProgress > 1d || double.IsNaN(normalizedProgress) || double.IsInfinity(normalizedProgress))
                throw new ArgumentOutOfRangeException(nameof(normalizedProgress), normalizedProgress, "Progress must be finite and within [0, 1].");

            NeedUnitId = needUnitId;
            NormalizedProgress = normalizedProgress;
        }

        public long NeedUnitId { get; }
        public double NormalizedProgress { get; }

        public bool Equals(ServiceSlotSnapshot other)
        {
            return NeedUnitId == other.NeedUnitId && NormalizedProgress.Equals(other.NormalizedProgress);
        }

        public override bool Equals(object obj)
        {
            return obj is ServiceSlotSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (NeedUnitId.GetHashCode() * 397) ^ NormalizedProgress.GetHashCode();
        }
    }

    /// <summary>Result of a deterministic service advancement segment.</summary>
    public sealed class ServiceAdvanceResult
    {
        internal ServiceAdvanceResult(double consumedSeconds, IReadOnlyList<NeedRuntime> completedUnits)
        {
            ConsumedSeconds = consumedSeconds;
            CompletedUnits = new ReadOnlyCollection<NeedRuntime>(new List<NeedRuntime>(completedUnits));
        }

        public double ConsumedSeconds { get; }
        public IReadOnlyList<NeedRuntime> CompletedUnits { get; }
    }

    /// <summary>
    /// Discrete service seats with normalized progress. Utilization changes affect occupied seats immediately.
    /// </summary>
    public sealed class ServiceSlotRuntime
    {
        private readonly List<ActiveService> activeServices = new List<ActiveService>();

        public ServiceSlotRuntime(int capacity, double serviceDuration, double utilization)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            if (!IsFinitePositive(serviceDuration))
                throw new ArgumentOutOfRangeException(nameof(serviceDuration), serviceDuration, "Service duration must be finite and positive.");

            Capacity = capacity;
            ServiceDuration = serviceDuration;
            SetUtilization(utilization);
        }

        public int Capacity { get; }
        public double ServiceDuration { get; }
        public double Utilization { get; private set; }
        public double ProgressRate => Utilization / ServiceDuration;
        public int ActiveCount => activeServices.Count;
        public bool HasCapacity => activeServices.Count < Capacity;

        public IReadOnlyList<ServiceSlotSnapshot> ActiveServices
        {
            get
            {
                var snapshots = new List<ServiceSlotSnapshot>(activeServices.Count);
                foreach (var service in activeServices)
                    snapshots.Add(new ServiceSlotSnapshot(service.Need.NeedUnitId, service.Progress));
                return new ReadOnlyCollection<ServiceSlotSnapshot>(snapshots);
            }
        }

        public void SetUtilization(double utilization)
        {
            if (!IsFinitePositive(utilization))
                throw new ArgumentOutOfRangeException(nameof(utilization), utilization, "Utilization must be finite and positive.");
            Utilization = utilization;
        }

        public bool TryStartService(NeedRuntime needUnit, double serviceStartTime)
        {
            if (needUnit == null)
                throw new ArgumentNullException(nameof(needUnit));
            if (!HasCapacity || !needUnit.TryStartService(serviceStartTime))
                return false;

            activeServices.Add(new ActiveService(needUnit));
            return true;
        }

        public double TimeUntilNextCompletion()
        {
            if (activeServices.Count == 0)
                return double.PositiveInfinity;

            var shortest = double.PositiveInfinity;
            foreach (var service in activeServices)
            {
                var remainingWork = Math.Max(0d, 1d - service.Progress);
                shortest = Math.Min(shortest, remainingWork / ProgressRate);
            }
            return shortest;
        }

        public ServiceAdvanceResult Advance(double maxSeconds, double startTime)
        {
            ValidateTime(maxSeconds, nameof(maxSeconds), allowZero: true);
            ValidateTime(startTime, nameof(startTime), allowZero: true);

            if (maxSeconds == 0d || activeServices.Count == 0)
                return new ServiceAdvanceResult(maxSeconds, Array.Empty<NeedRuntime>());

            var remaining = maxSeconds;
            var consumed = 0d;
            var completedUnits = new List<NeedRuntime>();

            while (remaining > 0d && activeServices.Count > 0)
            {
                var timeToCompletion = TimeUntilNextCompletion();
                if (timeToCompletion <= 0d)
                {
                    CompleteReadyServices(startTime + consumed, completedUnits);
                    continue;
                }

                var segment = Math.Min(remaining, timeToCompletion);
                var work = ProgressRate * segment;
                foreach (var service in activeServices)
                {
                    var remainingWork = Math.Max(0d, 1d - service.Progress);
                    service.Progress = Math.Min(1d, service.Progress + work);
                    if (remainingWork <= work)
                        service.Progress = 1d;
                }

                consumed += segment;
                var nextRemaining = remaining - segment;
                // A positive segment must be consumed even when subtraction has
                // reached the precision limit of the current double value.
                remaining = nextRemaining < remaining ? nextRemaining : 0d;

                if (segment >= timeToCompletion)
                    CompleteReadyServices(startTime + consumed, completedUnits);
            }

            return new ServiceAdvanceResult(consumed, completedUnits);
        }

        private void CompleteReadyServices(double completionTime, List<NeedRuntime> completedUnits)
        {
            for (var index = activeServices.Count - 1; index >= 0; index--)
            {
                var service = activeServices[index];
                if (service.Progress < 1d)
                    continue;

                service.Progress = 1d;
                if (!service.Need.TryComplete(completionTime))
                    throw new InvalidOperationException("A service seat completed a need unit in an invalid lifecycle state.");

                completedUnits.Insert(0, service.Need);
                activeServices.RemoveAt(index);
            }
        }

        private static void ValidateTime(double value, string parameterName, bool allowZero)
        {
            if ((!allowZero && value <= 0d) || (allowZero && value < 0d) || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName, value, "Time values must be finite and non-negative.");
        }

        private static bool IsFinitePositive(double value)
        {
            return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class ActiveService
        {
            public ActiveService(NeedRuntime need)
            {
                Need = need;
            }

            public NeedRuntime Need { get; }
            public double Progress { get; set; }
        }
    }
}
