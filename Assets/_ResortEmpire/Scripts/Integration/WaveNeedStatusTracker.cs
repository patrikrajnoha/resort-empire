using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Waves;
using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>S2 warning state for one owned need. Numeric satisfaction is intentionally absent.</summary>
    public enum NeedServiceStatus
    {
        OK = 0,
        BOTTLENECK = 1,
        RECOVERING = 2,
        SATISFIED = 3
    }

    /// <summary>Why guests are waiting, so S2 can tell a missing Worker apart from full seats.</summary>
    public enum NeedQueueCause
    {
        /// <summary>Nobody is waiting.</summary>
        None = 0,
        /// <summary>The building has no Worker and serves slowly. Moving a Worker helps right away.</summary>
        MissingWorker = 1,
        /// <summary>A Worker is assigned and every seat is busy. Only more capacity (or Energy) helps.</summary>
        ServiceCapacity = 2
    }

    /// <summary>Short, non-blocking S2 messages, declared from least to most important.</summary>
    public enum NeedFeedbackKind
    {
        Satisfied = 0,
        /// <summary>Wave-wide: arrivals have ended and only the remaining queues are being served.</summary>
        ArrivalsEnded = 1,
        WorkerAssigned = 2,
        BackToNormal = 3,
        Recovering = 4,
        WorkerRemoved = 5,
        RecoveringAfterWorker = 6,
        BottleneckCapacity = 7,
        BottleneckMissingWorker = 8
    }

    /// <summary>
    /// Inspector-tunable bottleneck detection. Values are copied when a tracker is created,
    /// so editing them never changes the rules of a wave that is already being observed.
    /// </summary>
    [Serializable]
    public sealed class WaveNeedStatusSettings
    {
        [Tooltip("BOTTLENECK needs more than this many guests waiting. Occupied service seats never count.")]
        [Min(0)] public int BottleneckQueueThreshold = 2;

        [Tooltip("Simulation seconds the queue must stay above the threshold and keep growing before BOTTLENECK shows.")]
        [Min(0f)] public float GrowthDuration = 4f;

        [Tooltip("RECOVERING shows once the queue has shrunk this many guests below its bottleneck peak. Before a warning, "
            + "a queue that drains this much from its high point is a passing spike and restarts the growth window.")]
        [Min(1)] public int RecoveryDrop = 2;

        [Tooltip("At or below this many waiting guests, a warning clears back to OK. Must be below the threshold.")]
        [Min(0)] public int RecoveredQueue = 1;

        public WaveNeedStatusSettings Copy()
        {
            return (WaveNeedStatusSettings)MemberwiseClone();
        }

        public void Validate()
        {
            if (RecoveredQueue < 0)
                throw new ArgumentOutOfRangeException(nameof(RecoveredQueue), RecoveredQueue, "The recovered queue cannot be negative.");
            if (BottleneckQueueThreshold <= RecoveredQueue)
                throw new ArgumentOutOfRangeException(nameof(BottleneckQueueThreshold), BottleneckQueueThreshold,
                    "The bottleneck threshold must exceed the recovered queue so warnings have hysteresis.");
            if (float.IsNaN(GrowthDuration) || float.IsInfinity(GrowthDuration) || GrowthDuration < 0f)
                throw new ArgumentOutOfRangeException(nameof(GrowthDuration), GrowthDuration, "The growth duration must be finite and non-negative.");
            if (RecoveryDrop < 1)
                throw new ArgumentOutOfRangeException(nameof(RecoveryDrop), RecoveryDrop, "The recovery drop must be at least one guest.");
        }
    }

    /// <summary>One live reading of a need. Waiting never includes guests already in a service seat.</summary>
    public readonly struct NeedQueueObservation
    {
        public NeedQueueObservation(NeedType needType, int served, int total, int waiting, int inService, int capacity, bool isStaffed)
        {
            if (served < 0) throw new ArgumentOutOfRangeException(nameof(served), served, "Served units cannot be negative.");
            if (total < 0) throw new ArgumentOutOfRangeException(nameof(total), total, "Total demand cannot be negative.");
            if (waiting < 0) throw new ArgumentOutOfRangeException(nameof(waiting), waiting, "The waiting queue cannot be negative.");
            if (inService < 0) throw new ArgumentOutOfRangeException(nameof(inService), inService, "Occupied seats cannot be negative.");
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity cannot be negative.");

            NeedType = needType;
            BuildingId = BuildingNeedMapping.GetBuildingId(needType);
            Served = served;
            Total = total;
            Waiting = waiting;
            InService = inService;
            Capacity = capacity;
            IsStaffed = isStaffed;
        }

        public NeedType NeedType { get; }
        public BuildingId BuildingId { get; }
        public int Served { get; }
        public int Total { get; }
        public int Waiting { get; }
        public int InService { get; }
        public int Capacity { get; }
        public bool IsStaffed { get; }
    }

    public readonly struct NeedStatusSnapshot
    {
        internal NeedStatusSnapshot(NeedQueueObservation need, NeedServiceStatus status, NeedServiceStatus previousStatus)
        {
            NeedType = need.NeedType;
            BuildingId = need.BuildingId;
            Served = need.Served;
            Total = need.Total;
            Waiting = need.Waiting;
            InService = need.InService;
            Capacity = need.Capacity;
            IsStaffed = need.IsStaffed;
            Status = status;
            PreviousStatus = previousStatus;
        }

        public NeedType NeedType { get; }
        public BuildingId BuildingId { get; }
        public int Served { get; }
        public int Total { get; }
        public int Remaining => Math.Max(0, Total - Served);
        /// <summary>Guests waiting for a seat. Occupied seats are reported separately as InService.</summary>
        public int Waiting { get; }
        public int InService { get; }
        public int Capacity { get; }
        public bool IsStaffed { get; }
        public NeedServiceStatus Status { get; }
        public NeedServiceStatus PreviousStatus { get; }

        public NeedQueueCause Cause => Waiting == 0
            ? NeedQueueCause.None
            : IsStaffed ? NeedQueueCause.ServiceCapacity : NeedQueueCause.MissingWorker;
    }

    /// <summary>A short S2 message. NeedType and BuildingId are unused for wave-wide kinds.</summary>
    public readonly struct NeedFeedback
    {
        internal NeedFeedback(NeedFeedbackKind kind, NeedType needType)
        {
            Kind = kind;
            NeedType = needType;
            BuildingId = BuildingNeedMapping.GetBuildingId(needType);
        }

        public NeedFeedbackKind Kind { get; }
        public NeedType NeedType { get; }
        public BuildingId BuildingId { get; }
        public bool IsWaveWide => Kind == NeedFeedbackKind.ArrivalsEnded;
    }

    /// <summary>S2 view of the running wave, free of simulation-assembly types.</summary>
    public sealed class WaveProgressSnapshot
    {
        internal WaveProgressSnapshot(int waveId, bool arrivalsClosed, IReadOnlyList<NeedStatusSnapshot> needs,
            IReadOnlyList<NeedFeedback> feedback)
        {
            WaveId = waveId;
            ArrivalsClosed = arrivalsClosed;
            Needs = needs;
            Feedback = feedback;
            foreach (var need in needs)
            {
                Served += need.Served;
                Total += need.Total;
            }
        }

        public int WaveId { get; }
        public bool ArrivalsClosed { get; }
        public int Served { get; }
        public int Total { get; }
        /// <summary>Owned needs in NeedType order, so cards keep their relative order as needs unlock.</summary>
        public IReadOnlyList<NeedStatusSnapshot> Needs { get; }
        /// <summary>What changed since the previous update, most important first. Each change is reported once.</summary>
        public IReadOnlyList<NeedFeedback> Feedback { get; }
    }

    /// <summary>
    /// Derives S2 need states from the waiting queue only; occupied seats never count.
    /// BOTTLENECK needs a queue above the threshold that keeps growing for GrowthDuration
    /// simulation seconds, so a pause never raises one and a burst that is already draining
    /// never confirms. Assigning a Worker to a bottleneck shows RECOVERING at once; losing it
    /// while the queue is still over the threshold shows BOTTLENECK at once. All other changes
    /// use hysteresis bands so queues fluctuating by a guest or two never make warnings flicker.
    /// A new wave id discards every timer, peak and status from the previous wave.
    /// </summary>
    public sealed class WaveNeedStatusTracker
    {
        private static readonly IReadOnlyList<NeedFeedback> NoFeedback = new ReadOnlyCollection<NeedFeedback>(new NeedFeedback[0]);

        private readonly WaveNeedStatusSettings settings;
        private readonly Dictionary<NeedType, NeedMemory> memory = new Dictionary<NeedType, NeedMemory>();
        private int waveId;
        private bool arrivalsClosed;

        public WaveNeedStatusTracker()
            : this(null)
        {
        }

        public WaveNeedStatusTracker(WaveNeedStatusSettings settings)
        {
            this.settings = (settings ?? new WaveNeedStatusSettings()).Copy();
            this.settings.Validate();
        }

        /// <summary>UI entry point. Returns null before the first wave starts.</summary>
        public WaveProgressSnapshot Capture(IResortGameplayUiFacade gameplay)
        {
            var snapshot = gameplay?.CurrentWaveSnapshot;
            if (snapshot == null || snapshot.WaveId == 0)
                return null;
            return Update(snapshot, gameplay.SimulationTime);
        }

        public WaveProgressSnapshot Update(WaveSnapshot snapshot, double simulationTime)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var staffed = new HashSet<BuildingId>();
            foreach (var worker in snapshot.WorkerAssignments)
                staffed.Add(worker.BuildingId);

            var needs = new List<NeedQueueObservation>(snapshot.Needs.Count);
            foreach (var need in snapshot.Needs)
            {
                needs.Add(new NeedQueueObservation(need.NeedType, need.CompletedUnits, need.DemandTotal,
                    need.WaitingQueueLength, need.ActiveServiceCount, need.Capacity, staffed.Contains(need.BuildingId)));
            }
            return Update(snapshot.WaveId, snapshot.ArrivalsClosed, needs, simulationTime);
        }

        public WaveProgressSnapshot Update(int waveId, bool arrivalsClosed, IReadOnlyList<NeedQueueObservation> needs, double simulationTime)
        {
            if (waveId <= 0)
                throw new ArgumentOutOfRangeException(nameof(waveId), waveId, "Wave identifier must be positive.");
            if (needs == null)
                throw new ArgumentNullException(nameof(needs));
            if (double.IsNaN(simulationTime) || double.IsInfinity(simulationTime))
                throw new ArgumentOutOfRangeException(nameof(simulationTime), simulationTime, "Simulation time must be finite.");

            // A newly observed wave starts fresh and never announces what it was already doing.
            if (waveId != this.waveId)
            {
                memory.Clear();
                this.waveId = waveId;
                this.arrivalsClosed = arrivalsClosed;
            }

            var ordered = new List<NeedQueueObservation>(needs);
            ordered.Sort((left, right) => ((int)left.NeedType).CompareTo((int)right.NeedType));

            var statuses = new List<NeedStatusSnapshot>(ordered.Count);
            List<NeedFeedback> feedback = null;
            foreach (var need in ordered)
            {
                NeedMemory state;
                if (!memory.TryGetValue(need.NeedType, out state))
                {
                    state = new NeedMemory();
                    memory.Add(need.NeedType, state);
                }

                var gained = state.HasSample && need.IsStaffed && !state.WasStaffed;
                var lost = state.HasSample && !need.IsStaffed && state.WasStaffed;
                var previous = state.Status;
                state.Status = Next(state, need, arrivalsClosed, simulationTime, gained, lost);
                state.WasStaffed = need.IsStaffed;
                state.HasSample = true;
                statuses.Add(new NeedStatusSnapshot(need, state.Status, previous));

                var kind = FeedbackFor(previous, state.Status, need, gained, lost);
                if (kind.HasValue)
                    (feedback ?? (feedback = new List<NeedFeedback>())).Add(new NeedFeedback(kind.Value, need.NeedType));
            }

            if (arrivalsClosed && !this.arrivalsClosed)
                (feedback ?? (feedback = new List<NeedFeedback>())).Add(new NeedFeedback(NeedFeedbackKind.ArrivalsEnded, default(NeedType)));
            this.arrivalsClosed = arrivalsClosed;

            // Most important first; ties keep the stable NeedType order.
            if (feedback != null)
                feedback = StableSortByPriority(feedback);
            return new WaveProgressSnapshot(waveId, arrivalsClosed,
                new ReadOnlyCollection<NeedStatusSnapshot>(statuses),
                feedback == null ? NoFeedback : new ReadOnlyCollection<NeedFeedback>(feedback));
        }

        private NeedServiceStatus Next(NeedMemory state, NeedQueueObservation need, bool closed, double time, bool gained, bool lost)
        {
            if (state.Status == NeedServiceStatus.SATISFIED || (closed && need.Served >= need.Total))
                return NeedServiceStatus.SATISFIED;

            var queue = need.Waiting;
            switch (state.Status)
            {
                case NeedServiceStatus.BOTTLENECK:
                    state.PeakQueue = Math.Max(state.PeakQueue, queue);
                    if (queue <= settings.RecoveredQueue)
                        return EnterOk(state);
                    if (gained || queue <= state.PeakQueue - settings.RecoveryDrop)
                        return EnterRecovering(state, queue);
                    return NeedServiceStatus.BOTTLENECK;

                case NeedServiceStatus.RECOVERING:
                    if (queue <= settings.RecoveredQueue)
                        return EnterOk(state);
                    // Losing the Worker warns at once only if the queue is still over the threshold.
                    // A short queue goes back to OK and the normal growth window decides from there.
                    if (lost)
                        return queue > settings.BottleneckQueueThreshold ? EnterBottleneck(state, queue) : EnterOk(state);
                    // Re-warn only after the queue climbs a full recovery band above its low point and
                    // keeps growing, so the arrival that lands just after a Worker move cannot flip it back.
                    state.LowQueue = Math.Min(state.LowQueue, queue);
                    var floor = Math.Max(settings.BottleneckQueueThreshold, state.LowQueue + settings.RecoveryDrop - 1);
                    return KeepsGrowing(state, queue, floor, time) ? EnterBottleneck(state, queue) : NeedServiceStatus.RECOVERING;

                default:
                    // A newly assigned Worker gets a fresh observation window before any warning.
                    if (gained)
                        state.WindowOpen = false;
                    return KeepsGrowing(state, queue, settings.BottleneckQueueThreshold, time)
                        ? EnterBottleneck(state, queue)
                        : NeedServiceStatus.OK;
            }
        }

        /// <summary>
        /// The window opens when the queue first exceeds <paramref name="floor"/> and closes once it
        /// falls back to it. Growth is confirmed after GrowthDuration if the queue is now longer than
        /// when the window opened. The window restarts if the queue shrinks below that starting
        /// length, or drains RecoveryDrop guests from its high point: a burst that is already
        /// clearing is a spike, not sustained growth. A one-guest dip is noise and is ignored.
        /// </summary>
        private bool KeepsGrowing(NeedMemory state, int queue, int floor, double time)
        {
            if (queue <= floor)
            {
                state.WindowOpen = false;
                return false;
            }

            if (!state.WindowOpen || queue < state.WindowStartQueue || queue <= state.WindowPeakQueue - settings.RecoveryDrop)
            {
                state.WindowOpen = true;
                state.WindowStart = time;
                state.WindowStartQueue = queue;
                state.WindowPeakQueue = queue;
            }
            else
            {
                state.WindowPeakQueue = Math.Max(state.WindowPeakQueue, queue);
            }

            if (settings.GrowthDuration <= 0f)
                return true;
            return time - state.WindowStart >= settings.GrowthDuration && queue > state.WindowStartQueue;
        }

        private static NeedServiceStatus EnterBottleneck(NeedMemory state, int queue)
        {
            state.PeakQueue = queue;
            state.WindowOpen = false;
            return NeedServiceStatus.BOTTLENECK;
        }

        private static NeedServiceStatus EnterRecovering(NeedMemory state, int queue)
        {
            state.LowQueue = queue;
            state.WindowOpen = false;
            return NeedServiceStatus.RECOVERING;
        }

        private static NeedServiceStatus EnterOk(NeedMemory state)
        {
            state.WindowOpen = false;
            return NeedServiceStatus.OK;
        }

        private static NeedFeedbackKind? FeedbackFor(NeedServiceStatus previous, NeedServiceStatus next,
            NeedQueueObservation need, bool gained, bool lost)
        {
            if (previous != next)
            {
                switch (next)
                {
                    case NeedServiceStatus.BOTTLENECK:
                        return need.IsStaffed ? NeedFeedbackKind.BottleneckCapacity : NeedFeedbackKind.BottleneckMissingWorker;
                    case NeedServiceStatus.RECOVERING:
                        return gained ? NeedFeedbackKind.RecoveringAfterWorker : NeedFeedbackKind.Recovering;
                    case NeedServiceStatus.SATISFIED:
                        return NeedFeedbackKind.Satisfied;
                    default:
                        // A warning that clears because its Worker left is not "back to normal".
                        return lost ? NeedFeedbackKind.WorkerRemoved : NeedFeedbackKind.BackToNormal;
                }
            }

            // Moving Workers around a fully served building is not worth a message.
            if (next == NeedServiceStatus.SATISFIED)
                return null;
            if (gained)
                return NeedFeedbackKind.WorkerAssigned;
            if (lost)
                return NeedFeedbackKind.WorkerRemoved;
            return null;
        }

        private static List<NeedFeedback> StableSortByPriority(List<NeedFeedback> feedback)
        {
            var sorted = new List<NeedFeedback>(feedback.Count);
            for (var kind = (int)NeedFeedbackKind.BottleneckMissingWorker; kind >= 0; kind--)
            {
                foreach (var item in feedback)
                {
                    if ((int)item.Kind == kind)
                        sorted.Add(item);
                }
            }
            return sorted;
        }

        private sealed class NeedMemory
        {
            public NeedServiceStatus Status;
            public bool HasSample;
            public bool WasStaffed;
            public int PeakQueue;
            public int LowQueue;
            public bool WindowOpen;
            public double WindowStart;
            public int WindowStartQueue;
            public int WindowPeakQueue;
        }
    }
}
