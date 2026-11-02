using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Waves;

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

    public readonly struct NeedStatusSnapshot
    {
        internal NeedStatusSnapshot(NeedFlowSnapshot flow, bool isStaffed, NeedServiceStatus status)
        {
            NeedType = flow.NeedType;
            BuildingId = flow.BuildingId;
            Served = flow.CompletedUnits;
            Total = flow.DemandTotal;
            Waiting = flow.WaitingQueueLength;
            IsStaffed = isStaffed;
            Status = status;
        }

        public NeedType NeedType { get; }
        public BuildingId BuildingId { get; }
        public int Served { get; }
        public int Total { get; }
        public int Waiting { get; }
        public bool IsStaffed { get; }
        public NeedServiceStatus Status { get; }
    }

    /// <summary>S2 view of the running wave, free of simulation-assembly types.</summary>
    public sealed class WaveProgressSnapshot
    {
        internal WaveProgressSnapshot(WaveSnapshot snapshot, IReadOnlyList<NeedStatusSnapshot> needs)
        {
            WaveId = snapshot.WaveId;
            ArrivalsClosed = snapshot.ArrivalsClosed;
            Served = snapshot.CompletedUnits;
            Total = snapshot.DemandTotal;
            Needs = needs;
        }

        public int WaveId { get; }
        public bool ArrivalsClosed { get; }
        public int Served { get; }
        public int Total { get; }
        public IReadOnlyList<NeedStatusSnapshot> Needs { get; }
    }

    /// <summary>
    /// Derives need warnings from waiting-queue length with hysteresis so labels do not flicker.
    /// Occupied seats never count as a bottleneck. A Worker arriving at a bottleneck shows
    /// RECOVERING at once, while the warning stays until the queue actually drains.
    /// </summary>
    public sealed class WaveNeedStatusTracker
    {
        private const int RecoveryDrop = 2;
        private readonly int bottleneckQueue;
        private readonly int recoveredQueue;
        private readonly Dictionary<NeedType, NeedMemory> memory = new Dictionary<NeedType, NeedMemory>();
        private int waveId;

        public WaveNeedStatusTracker(int bottleneckQueue = 3, int recoveredQueue = 1)
        {
            if (recoveredQueue < 0 || bottleneckQueue <= recoveredQueue)
                throw new ArgumentOutOfRangeException(nameof(bottleneckQueue), "The bottleneck threshold must exceed the recovery threshold.");
            this.bottleneckQueue = bottleneckQueue;
            this.recoveredQueue = recoveredQueue;
        }

        /// <summary>UI entry point. Returns null before the first wave starts.</summary>
        public WaveProgressSnapshot Capture(IResortGameplayUiFacade gameplay)
        {
            var snapshot = gameplay?.CurrentWaveSnapshot;
            if (snapshot == null || snapshot.WaveId == 0)
                return null;
            return new WaveProgressSnapshot(snapshot, Update(snapshot));
        }

        public IReadOnlyList<NeedStatusSnapshot> Update(WaveSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.WaveId != waveId)
            {
                memory.Clear();
                waveId = snapshot.WaveId;
            }

            var staffed = new HashSet<BuildingId>();
            foreach (var worker in snapshot.WorkerAssignments)
                staffed.Add(worker.BuildingId);

            var result = new List<NeedStatusSnapshot>(snapshot.Needs.Count);
            foreach (var need in snapshot.Needs)
            {
                NeedMemory state;
                if (!memory.TryGetValue(need.NeedType, out state))
                {
                    state = new NeedMemory();
                    memory.Add(need.NeedType, state);
                }

                var isStaffed = staffed.Contains(need.BuildingId);
                state.Status = Next(state, need, isStaffed, snapshot.ArrivalsClosed);
                state.WasStaffed = isStaffed;
                result.Add(new NeedStatusSnapshot(need, isStaffed, state.Status));
            }
            return new ReadOnlyCollection<NeedStatusSnapshot>(result);
        }

        private NeedServiceStatus Next(NeedMemory state, NeedFlowSnapshot need, bool isStaffed, bool arrivalsClosed)
        {
            var queue = need.WaitingQueueLength;
            if (arrivalsClosed && need.CompletedUnits >= need.DemandTotal)
                return NeedServiceStatus.SATISFIED;

            switch (state.Status)
            {
                case NeedServiceStatus.BOTTLENECK:
                    state.PeakQueue = Math.Max(state.PeakQueue, queue);
                    if (queue <= recoveredQueue)
                        return NeedServiceStatus.OK;
                    if ((isStaffed && !state.StaffedAtBottleneck) || queue <= state.PeakQueue - RecoveryDrop)
                        return NeedServiceStatus.RECOVERING;
                    return NeedServiceStatus.BOTTLENECK;
                case NeedServiceStatus.RECOVERING:
                    if (queue <= recoveredQueue)
                        return NeedServiceStatus.OK;
                    if (queue > state.PeakQueue || (state.WasStaffed && !isStaffed))
                        return EnterBottleneck(state, queue, isStaffed);
                    return NeedServiceStatus.RECOVERING;
                default:
                    return queue >= bottleneckQueue ? EnterBottleneck(state, queue, isStaffed) : NeedServiceStatus.OK;
            }
        }

        private static NeedServiceStatus EnterBottleneck(NeedMemory state, int queue, bool isStaffed)
        {
            state.PeakQueue = queue;
            state.StaffedAtBottleneck = isStaffed;
            return NeedServiceStatus.BOTTLENECK;
        }

        private sealed class NeedMemory
        {
            public NeedServiceStatus Status;
            public int PeakQueue;
            public bool StaffedAtBottleneck;
            public bool WasStaffed;
        }
    }
}
