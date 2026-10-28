using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ResortEmpire.Gameplay.Waves
{
    /// <summary>One explicit need total in a scripted wave.</summary>
    public sealed class NeedDemandDefinition
    {
        public NeedDemandDefinition(NeedType needType, int demandTotal)
        {
            if (!Enum.IsDefined(typeof(NeedType), needType))
                throw new ArgumentOutOfRangeException(nameof(needType), needType, "Unknown need identifier.");
            if (demandTotal < 0)
                throw new ArgumentOutOfRangeException(nameof(demandTotal), demandTotal, "Demand cannot be negative.");

            NeedType = needType;
            DemandTotal = demandTotal;
        }

        public NeedType NeedType { get; }
        public int DemandTotal { get; }
        public BuildingId BuildingId => BuildingNeedMapping.GetBuildingId(NeedType);
    }

    public enum ScheduledDemandEventKind
    {
        AdditionalDemand = 0,
        SuddenRush = 1
    }

    /// <summary>Explicit, validated demand event scheduled within a wave arrival window.</summary>
    public sealed class ScheduledDemandEvent
    {
        public ScheduledDemandEvent(
            string eventId,
            ScheduledDemandEventKind kind,
            double startTime,
            double duration,
            int demandUnits,
            NeedType? targetNeed = null)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("An event identifier is required.", nameof(eventId));
            if (!Enum.IsDefined(typeof(ScheduledDemandEventKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown scheduled-demand event kind.");
            if (!IsFiniteNonNegative(startTime))
                throw new ArgumentOutOfRangeException(nameof(startTime), startTime, "Event start time must be finite and non-negative.");
            if (!IsFinitePositive(duration))
                throw new ArgumentOutOfRangeException(nameof(duration), duration, "Event duration must be finite and positive.");
            if (demandUnits <= 0)
                throw new ArgumentOutOfRangeException(nameof(demandUnits), demandUnits, "Event demand must be positive.");
            if (targetNeed.HasValue && !Enum.IsDefined(typeof(NeedType), targetNeed.Value))
                throw new ArgumentOutOfRangeException(nameof(targetNeed), targetNeed, "Unknown event need identifier.");
            if (kind == ScheduledDemandEventKind.SuddenRush && targetNeed.HasValue)
                throw new ArgumentException("Sudden Rush resolves its target from the currently unstaffed owned building.", nameof(targetNeed));
            if (kind == ScheduledDemandEventKind.AdditionalDemand && !targetNeed.HasValue)
                throw new ArgumentException("Additional demand events require an explicit target need.", nameof(targetNeed));

            EventId = eventId.Trim();
            Kind = kind;
            StartTime = startTime;
            Duration = duration;
            DemandUnits = demandUnits;
            TargetNeed = targetNeed;
        }

        public string EventId { get; }
        public ScheduledDemandEventKind Kind { get; }
        public double StartTime { get; }
        public double Duration { get; }
        public int DemandUnits { get; }
        public NeedType? TargetNeed { get; }

        internal void ValidateAgainstWave(int waveId, double arrivalDuration)
        {
            if (Kind == ScheduledDemandEventKind.SuddenRush && waveId != 2)
                throw new ArgumentException("Sudden Rush is a Wave 2 event.", nameof(waveId));
            if (StartTime + Duration > arrivalDuration)
                throw new ArgumentException("Scheduled demand event must finish within the arrival window.", nameof(Duration));
        }

        private static bool IsFinitePositive(double value)
        {
            return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>Immutable scripted input for one deterministic wave.</summary>
    public sealed class WaveDefinition
    {
        public WaveDefinition(
            int waveId,
            double arrivalDuration,
            IEnumerable<NeedDemandDefinition> demand,
            IEnumerable<ScheduledDemandEvent> scheduledEvents = null)
        {
            if (waveId <= 0)
                throw new ArgumentOutOfRangeException(nameof(waveId), waveId, "Wave identifier must be positive.");
            if (!IsFinitePositive(arrivalDuration))
                throw new ArgumentOutOfRangeException(nameof(arrivalDuration), arrivalDuration, "Arrival duration must be finite and positive.");
            if (demand == null)
                throw new ArgumentNullException(nameof(demand));

            var demandList = new List<NeedDemandDefinition>();
            var demandTypes = new HashSet<NeedType>();
            foreach (var definition in demand)
            {
                if (definition == null)
                    throw new ArgumentException("Demand definitions cannot contain null entries.", nameof(demand));
                if (!demandTypes.Add(definition.NeedType))
                    throw new ArgumentException("A wave cannot define the same need more than once.", nameof(demand));
                demandList.Add(definition);
            }

            var eventList = new List<ScheduledDemandEvent>();
            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            var suddenRushCount = 0;
            if (scheduledEvents != null)
            {
                foreach (var scheduledEvent in scheduledEvents)
                {
                    if (scheduledEvent == null)
                        throw new ArgumentException("Scheduled events cannot contain null entries.", nameof(scheduledEvents));
                    if (!eventIds.Add(scheduledEvent.EventId))
                        throw new ArgumentException("Scheduled event identifiers must be unique.", nameof(scheduledEvents));
                    scheduledEvent.ValidateAgainstWave(waveId, arrivalDuration);
                    if (scheduledEvent.Kind == ScheduledDemandEventKind.SuddenRush && ++suddenRushCount > 1)
                        throw new ArgumentException("A wave can contain at most one Sudden Rush event.", nameof(scheduledEvents));
                    eventList.Add(scheduledEvent);
                }
            }

            WaveId = waveId;
            ArrivalDuration = arrivalDuration;
            Demand = new ReadOnlyCollection<NeedDemandDefinition>(demandList);
            ScheduledEvents = new ReadOnlyCollection<ScheduledDemandEvent>(eventList);
        }

        public int WaveId { get; }
        public double ArrivalDuration { get; }
        public IReadOnlyList<NeedDemandDefinition> Demand { get; }
        public IReadOnlyList<ScheduledDemandEvent> ScheduledEvents { get; }

        private static bool IsFinitePositive(double value)
        {
            return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>Explicit runtime tuning/configuration input; no balancing values are hidden in the engine.</summary>
    public sealed class WaveSimulationConfig
    {
        public WaveSimulationConfig(double incomingDuration, double utilizationStaffed, double utilizationEmpty)
        {
            if (!IsFiniteNonNegative(incomingDuration))
                throw new ArgumentOutOfRangeException(nameof(incomingDuration), incomingDuration, "Incoming duration must be finite and non-negative.");
            if (!IsFinitePositive(utilizationStaffed))
                throw new ArgumentOutOfRangeException(nameof(utilizationStaffed), utilizationStaffed, "Staffed utilization must be finite and positive.");
            if (!IsFinitePositive(utilizationEmpty))
                throw new ArgumentOutOfRangeException(nameof(utilizationEmpty), utilizationEmpty, "Empty-building utilization must be finite and positive.");

            IncomingDuration = incomingDuration;
            UtilizationStaffed = utilizationStaffed;
            UtilizationEmpty = utilizationEmpty;
        }

        public double IncomingDuration { get; }
        public double UtilizationStaffed { get; }
        public double UtilizationEmpty { get; }

        private static bool IsFinitePositive(double value)
        {
            return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
