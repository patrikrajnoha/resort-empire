using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ResortEmpire.Gameplay.Waves
{
    public readonly struct NeedFlowSnapshot
    {
        internal NeedFlowSnapshot(
            NeedType needType,
            BuildingId buildingId,
            int demandTotal,
            int demandArrived,
            int completedUnits,
            int waitingQueueLength,
            int activeServiceCount)
        {
            NeedType = needType;
            BuildingId = buildingId;
            DemandTotal = demandTotal;
            DemandArrived = demandArrived;
            CompletedUnits = completedUnits;
            WaitingQueueLength = waitingQueueLength;
            ActiveServiceCount = activeServiceCount;
            Backlog = demandArrived - completedUnits;
        }

        public NeedType NeedType { get; }
        public BuildingId BuildingId { get; }
        public int DemandTotal { get; }
        public int DemandArrived { get; }
        public int CompletedUnits { get; }
        public int WaitingQueueLength { get; }
        public int ActiveServiceCount { get; }
        public int Backlog { get; }
    }

    /// <summary>Immutable wave output for future UI, ratings and persistence adapters.</summary>
    public sealed class WaveSnapshot
    {
        internal WaveSnapshot(
            int waveId,
            GamePhase lifecycle,
            bool isArrivalPhase,
            bool arrivalsClosed,
            int demandTotal,
            int demandArrived,
            int completedUnits,
            int waitingQueueLength,
            int activeServiceCount,
            IReadOnlyList<NeedFlowSnapshot> needs,
            IReadOnlyList<WorkerAssignmentSnapshot> workerAssignments,
            IReadOnlyList<NeedCompletionRecord> completionRecords)
        {
            WaveId = waveId;
            Lifecycle = lifecycle;
            IsArrivalPhase = isArrivalPhase;
            ArrivalsClosed = arrivalsClosed;
            DemandTotal = demandTotal;
            DemandArrived = demandArrived;
            CompletedUnits = completedUnits;
            WaitingQueueLength = waitingQueueLength;
            ActiveServiceCount = activeServiceCount;
            Backlog = demandArrived - completedUnits;
            Needs = needs;
            WorkerAssignments = workerAssignments;
            CompletionRecords = completionRecords;
        }

        public int WaveId { get; }
        public GamePhase Lifecycle { get; }
        public bool IsArrivalPhase { get; }
        public bool ArrivalsClosed { get; }
        public int DemandTotal { get; }
        public int DemandArrived { get; }
        public int CompletedUnits { get; }
        public int WaitingQueueLength { get; }
        public int ActiveServiceCount { get; }
        public int Backlog { get; }
        public IReadOnlyList<NeedFlowSnapshot> Needs { get; }
        public IReadOnlyList<WorkerAssignmentSnapshot> WorkerAssignments { get; }
        public IReadOnlyList<NeedCompletionRecord> CompletionRecords { get; }
    }

    /// <summary>Single authority for one deterministic scripted wave.</summary>
    public sealed class WaveManager : IActiveWaveConfigurationGuard
    {
        private readonly GameState gameState;
        private readonly SimulationClock clock;
        private readonly IBuildingSnapshotProvider buildingProvider;
        private readonly IWorkerAssignmentProvider workerProvider;
        private readonly WaveSimulationConfig config;
        private readonly List<BuildingRuntime> buildings = new List<BuildingRuntime>();
        private readonly List<NeedFlowRuntime> flows = new List<NeedFlowRuntime>();
        private readonly Dictionary<BuildingId, BuildingRuntime> buildingsById = new Dictionary<BuildingId, BuildingRuntime>();
        private readonly Dictionary<NeedType, NeedFlowRuntime> flowsByNeed = new Dictionary<NeedType, NeedFlowRuntime>();
        private readonly List<ScheduledEventRuntime> scheduledEvents = new List<ScheduledEventRuntime>();
        private readonly List<NeedCompletionRecord> completionRecords = new List<NeedCompletionRecord>();
        private List<BuildingSnapshot> activeConfigurationSnapshot;
        private string configurationDiagnostic;

        private WaveDefinition definition;
        private double incomingElapsed;
        private double waveElapsed;
        private double waveStartTime;
        private long nextNeedUnitId = 1;
        private bool arrivalsClosed;
        private bool completionEventRaised;

        public WaveManager(
            GameState gameState,
            SimulationClock clock,
            IBuildingSnapshotProvider buildingProvider,
            IWorkerAssignmentProvider workerProvider,
            WaveSimulationConfig config)
        {
            this.gameState = gameState ?? throw new ArgumentNullException(nameof(gameState));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.buildingProvider = buildingProvider ?? throw new ArgumentNullException(nameof(buildingProvider));
            this.workerProvider = workerProvider ?? throw new ArgumentNullException(nameof(workerProvider));
            this.config = config ?? throw new ArgumentNullException(nameof(config));

            var configurationHost = workerProvider as IWorkerAssignmentConfigurationHost;
            if (configurationHost != null)
                configurationHost.AttachConfigurationGuard(this);
        }

        public int CurrentWaveId => definition == null ? 0 : definition.WaveId;
        public GamePhase CurrentPhase => gameState.CurrentState;
        public double ArrivalWindowElapsed => incomingElapsed;
        public double ArrivalElapsed => waveElapsed;
        public bool ArrivalsClosed => arrivalsClosed;
        public bool HasConfigurationMismatch => configurationDiagnostic != null;
        public string ConfigurationDiagnostic => configurationDiagnostic;
        public bool IsConfigurationStable => EnsureActiveConfigurationStable();
        public event Action<NeedCompletionRecord> NeedCompleted;
        public event Action<WaveSnapshot> WaveCompleted;

        public bool StartWave(WaveDefinition waveDefinition)
        {
            if (waveDefinition == null)
                throw new ArgumentNullException(nameof(waveDefinition));
            if (gameState.CurrentState != GamePhase.AWAITING_WAVE)
                return false;

            ResetRuntime(waveDefinition);
            if (!gameState.TryTransition(GamePhase.WAVE_INCOMING))
                return false;

            return true;
        }

        public bool TryPauseWave()
        {
            if (gameState.CurrentState != GamePhase.WAVE_ACTIVE)
                return false;

            if (!gameState.TryTransition(GamePhase.WAVE_PAUSED))
                return false;
            clock.Pause();
            return true;
        }

        public bool TryResumeWave()
        {
            if (gameState.CurrentState != GamePhase.WAVE_PAUSED)
                return false;
            if (!EnsureActiveConfigurationStable())
                return false;

            clock.Resume();
            return gameState.TryTransition(GamePhase.WAVE_ACTIVE);
        }

        public bool TryReassignWorker(BuildingId sourceBuildingId, BuildingId destinationBuildingId)
        {
            if (gameState.CurrentState != GamePhase.AWAITING_WAVE
                && gameState.CurrentState != GamePhase.WAVE_ACTIVE)
                return false;
            if (gameState.CurrentState == GamePhase.WAVE_ACTIVE && !EnsureActiveConfigurationStable())
                return false;

            var reassigned = workerProvider.TryReassignWorker(sourceBuildingId, destinationBuildingId);
            if (reassigned)
                SynchronizeWorkerUtilization();
            return reassigned;
        }

        public void Advance(double deltaSeconds)
        {
            ValidateDelta(deltaSeconds);
            if (clock.IsPaused || gameState.CurrentState == GamePhase.WAVE_PAUSED)
                return;
            if (IsWaveConfigurationState(gameState.CurrentState) && !EnsureActiveConfigurationStable())
                return;

            var targetTime = clock.CurrentTime + deltaSeconds;
            if (double.IsNaN(targetTime) || double.IsInfinity(targetTime))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "The resulting simulation time is outside the supported range.");
            // SimulationClock stores one double and has no residual accumulator. If
            // targetTime equals the current time at a very large magnitude, the
            // positive delta is below the representable ULP and cannot be encoded
            // without changing the Phase 1 clock contract.

            while (true)
            {
                if (gameState.CurrentState == GamePhase.WAVE_INCOMING)
                {
                    if (incomingElapsed >= config.IncomingDuration)
                    {
                        gameState.TryTransition(GamePhase.WAVE_ACTIVE);
                        continue;
                    }

                    var incomingRemaining = targetTime - clock.CurrentTime;
                    if (incomingRemaining <= 0d)
                        return;

                    var incomingSegment = Math.Min(incomingRemaining, config.IncomingDuration - incomingElapsed);
                    AdvanceClock(incomingSegment);
                    incomingElapsed += incomingSegment;
                    continue;
                }

                if (gameState.CurrentState != GamePhase.WAVE_ACTIVE)
                    return;

                ProcessCurrentTime();
                if (TryCompleteWave())
                    return;

                var remaining = targetTime - clock.CurrentTime;
                if (remaining <= 0d)
                    return;

                var nextBoundary = FindNextBoundary();
                var segment = Math.Min(remaining, nextBoundary);
                if (double.IsPositiveInfinity(segment))
                    segment = remaining;

                if (segment <= 0d)
                {
                    ProcessCurrentTime();
                    if (TryCompleteWave())
                        return;
                    throw new InvalidOperationException("Wave simulation could not make deterministic progress.");
                }

                // Boundary ordering is deterministic: complete services in the
                // segment first, advance the clock to that boundary, then the
                // next loop processes scheduled demand/events and queue refill.
                AdvanceServices(segment);
                AdvanceClock(segment);
                waveElapsed += segment;
            }
        }

        public WaveSnapshot CaptureSnapshot()
        {
            var needSnapshots = new List<NeedFlowSnapshot>();
            var demandTotal = 0;
            var demandArrived = 0;
            var completedUnits = 0;
            var waitingQueueLength = 0;
            var activeServiceCount = 0;

            var sortedFlows = new List<NeedFlowRuntime>(flows);
            sortedFlows.Sort((left, right) => ((int)left.NeedType).CompareTo((int)right.NeedType));
            foreach (var flow in sortedFlows)
            {
                demandTotal += flow.DemandTotal;
                demandArrived += flow.DemandArrived;
                completedUnits += flow.CompletedUnits;
                waitingQueueLength += flow.Building.Queue.Count;
                activeServiceCount += flow.Building.ServiceSlots.ActiveCount;
                needSnapshots.Add(new NeedFlowSnapshot(
                    flow.NeedType,
                    flow.Building.BuildingId,
                    flow.DemandTotal,
                    flow.DemandArrived,
                    flow.CompletedUnits,
                    flow.Building.Queue.Count,
                    flow.Building.ServiceSlots.ActiveCount));
            }

            var assignments = new List<WorkerAssignmentSnapshot>(workerProvider.Assignments ?? Array.Empty<WorkerAssignmentSnapshot>());
            var records = new List<NeedCompletionRecord>(completionRecords);
            return new WaveSnapshot(
                CurrentWaveId,
                gameState.CurrentState,
                gameState.CurrentState == GamePhase.WAVE_INCOMING
                    || gameState.CurrentState == GamePhase.WAVE_ACTIVE && !arrivalsClosed,
                arrivalsClosed,
                demandTotal,
                demandArrived,
                completedUnits,
                waitingQueueLength,
                activeServiceCount,
                new ReadOnlyCollection<NeedFlowSnapshot>(needSnapshots),
                new ReadOnlyCollection<WorkerAssignmentSnapshot>(assignments),
                new ReadOnlyCollection<NeedCompletionRecord>(records));
        }

        private void ResetRuntime(WaveDefinition waveDefinition)
        {
            definition = waveDefinition;
            incomingElapsed = 0d;
            waveElapsed = 0d;
            waveStartTime = clock.CurrentTime;
            arrivalsClosed = false;
            completionEventRaised = false;
            buildings.Clear();
            buildingsById.Clear();
            flows.Clear();
            flowsByNeed.Clear();
            scheduledEvents.Clear();
            completionRecords.Clear();
            configurationDiagnostic = null;

            var snapshots = CaptureBuildingConfiguration();
            // Prototype building ownership/capacity/service duration/slot data is
            // immutable for this wave. Worker assignments remain live and are
            // synchronized separately during active play.
            activeConfigurationSnapshot = snapshots;
            foreach (var snapshot in snapshots)
            {
                if (!snapshot.IsOwned)
                    continue;

                var runtime = new BuildingRuntime(snapshot);
                buildings.Add(runtime);
                buildingsById.Add(snapshot.BuildingId, runtime);
            }
            buildings.Sort((left, right) => ((int)left.BuildingId).CompareTo((int)right.BuildingId));

            foreach (var demand in waveDefinition.Demand)
            {
                BuildingRuntime building;
                if (buildingsById.TryGetValue(demand.BuildingId, out building))
                    EnsureFlow(demand.NeedType, building).BaseDemandTotal = demand.DemandTotal;
            }

            foreach (var scheduledEvent in waveDefinition.ScheduledEvents)
            {
                var eventRuntime = new ScheduledEventRuntime(scheduledEvent);
                if (scheduledEvent.Kind == ScheduledDemandEventKind.AdditionalDemand)
                {
                    var targetBuilding = buildingsById.ContainsKey(BuildingNeedMapping.GetBuildingId(scheduledEvent.TargetNeed.Value));
                    if (!targetBuilding)
                    {
                        eventRuntime.IsFiltered = true;
                    }
                    else
                    {
                        var building = buildingsById[BuildingNeedMapping.GetBuildingId(scheduledEvent.TargetNeed.Value)];
                        eventRuntime.TargetNeed = scheduledEvent.TargetNeed.Value;
                        eventRuntime.Flow = EnsureFlow(eventRuntime.TargetNeed.Value, building);
                        eventRuntime.Flow.ScheduledDemandTotal += scheduledEvent.DemandUnits;
                    }
                }
                scheduledEvents.Add(eventRuntime);
            }
        }

        private NeedFlowRuntime EnsureFlow(NeedType needType, BuildingRuntime building)
        {
            NeedFlowRuntime flow;
            if (flowsByNeed.TryGetValue(needType, out flow))
                return flow;

            flow = new NeedFlowRuntime(needType, building);
            flowsByNeed.Add(needType, flow);
            flows.Add(flow);
            return flow;
        }

        private void ProcessCurrentTime()
        {
            if (waveElapsed >= definition.ArrivalDuration)
            {
                waveElapsed = definition.ArrivalDuration;
                arrivalsClosed = true;
            }

            ActivateDueEvents();
            EmitDueBaseDemand();
            EmitDueScheduledDemand();
            SynchronizeWorkerUtilization();
            AdmitWaitingUnits();
        }

        private void ActivateDueEvents()
        {
            foreach (var eventRuntime in scheduledEvents)
            {
                if (eventRuntime.IsFiltered || eventRuntime.IsStarted || waveElapsed < eventRuntime.Definition.StartTime)
                    continue;

                if (eventRuntime.Definition.Kind == ScheduledDemandEventKind.SuddenRush)
                {
                    var targetBuilding = FindCurrentlyUnstaffedBuilding();
                    if (targetBuilding == null)
                    {
                        eventRuntime.IsFiltered = true;
                        continue;
                    }

                    eventRuntime.TargetNeed = targetBuilding.Snapshot.NeedType;
                    eventRuntime.Flow = EnsureFlow(eventRuntime.TargetNeed.Value, targetBuilding);
                    eventRuntime.Flow.ScheduledDemandTotal += eventRuntime.Definition.DemandUnits;
                }

                eventRuntime.IsStarted = true;
            }
        }

        private void EmitDueBaseDemand()
        {
            foreach (var flow in flows)
            {
                while (flow.BaseEmitted < flow.BaseDemandTotal
                    && BaseArrivalTime(flow) <= waveElapsed)
                {
                    var arrivalOffset = BaseArrivalTime(flow);
                    flow.BaseEmitted++;
                    EmitUnit(flow, waveStartTime + arrivalOffset);
                }
            }
        }

        private void EmitDueScheduledDemand()
        {
            foreach (var eventRuntime in scheduledEvents)
            {
                if (eventRuntime.IsFiltered || !eventRuntime.IsStarted || eventRuntime.Flow == null)
                    continue;

                while (eventRuntime.Emitted < eventRuntime.Definition.DemandUnits
                    && EventDemandTime(eventRuntime) <= waveElapsed)
                {
                    var arrivalOffset = EventDemandTime(eventRuntime);
                    eventRuntime.Emitted++;
                    EmitUnit(eventRuntime.Flow, waveStartTime + arrivalOffset);
                }
            }
        }

        private void EmitUnit(NeedFlowRuntime flow, double arrivalTime)
        {
            var unit = new NeedRuntime(nextNeedUnitId++, definition.WaveId, flow.NeedType, arrivalTime);
            flow.DemandArrived++;
            flow.Building.Queue.Enqueue(unit);
        }

        private void AdmitWaitingUnits()
        {
            foreach (var building in buildings)
            {
                while (building.ServiceSlots.HasCapacity && building.Queue.TryDequeue(out var unit))
                {
                    if (!building.ServiceSlots.TryStartService(unit, clock.CurrentTime))
                        throw new InvalidOperationException("A waiting need unit could not enter an available service seat.");
                }
            }
        }

        private void AdvanceServices(double seconds)
        {
            foreach (var building in buildings)
            {
                var result = building.ServiceSlots.Advance(seconds, clock.CurrentTime);
                foreach (var completedUnit in result.CompletedUnits)
                {
                    var flow = flowsByNeed[completedUnit.NeedType];
                    flow.CompletedUnits++;
                    var record = completedUnit.GetCompletionRecord();
                    completionRecords.Add(record);
                    NeedCompleted?.Invoke(record);
                }
            }
        }

        private bool TryCompleteWave()
        {
            if (!arrivalsClosed || !AllDemandEmitted() || !AllScheduledEventsResolved())
                return false;
            foreach (var building in buildings)
            {
                if (building.Queue.Count > 0 || building.ServiceSlots.ActiveCount > 0)
                    return false;
            }

            if (!gameState.TryTransition(GamePhase.WAVE_RESOLVING))
                return false;
            if (!gameState.TryTransition(GamePhase.WAVE_COMPLETE))
                throw new InvalidOperationException("Wave resolving state could not transition to completion.");
            if (!completionEventRaised)
            {
                completionEventRaised = true;
                WaveCompleted?.Invoke(CaptureSnapshot());
            }
            return true;
        }

        private bool AllDemandEmitted()
        {
            foreach (var flow in flows)
            {
                if (flow.DemandArrived < flow.DemandTotal || flow.CompletedUnits < flow.DemandArrived)
                    return false;
            }
            return true;
        }

        private bool AllScheduledEventsResolved()
        {
            foreach (var eventRuntime in scheduledEvents)
            {
                if (!eventRuntime.IsFiltered
                    && (!eventRuntime.IsStarted || eventRuntime.Emitted < eventRuntime.Definition.DemandUnits))
                    return false;
            }
            return true;
        }

        private double FindNextBoundary()
        {
            var next = double.PositiveInfinity;
            if (!arrivalsClosed)
                next = Math.Min(next, definition.ArrivalDuration - waveElapsed);

            foreach (var flow in flows)
            {
                if (flow.BaseEmitted < flow.BaseDemandTotal)
                    next = Math.Min(next, BaseArrivalTime(flow) - waveElapsed);
            }

            foreach (var eventRuntime in scheduledEvents)
            {
                if (eventRuntime.IsFiltered)
                    continue;
                if (!eventRuntime.IsStarted)
                    next = Math.Min(next, eventRuntime.Definition.StartTime - waveElapsed);
                else if (eventRuntime.Emitted < eventRuntime.Definition.DemandUnits)
                    next = Math.Min(next, EventDemandTime(eventRuntime) - waveElapsed);
            }

            foreach (var building in buildings)
                next = Math.Min(next, building.ServiceSlots.TimeUntilNextCompletion());

            return Math.Max(0d, next);
        }

        private double BaseArrivalTime(NeedFlowRuntime flow)
        {
            return definition.ArrivalDuration * (flow.BaseEmitted + 1d) / flow.BaseDemandTotal;
        }

        private double EventDemandTime(ScheduledEventRuntime eventRuntime)
        {
            return eventRuntime.Definition.StartTime
                + eventRuntime.Definition.Duration * (eventRuntime.Emitted + 1d) / eventRuntime.Definition.DemandUnits;
        }

        private BuildingRuntime FindCurrentlyUnstaffedBuilding()
        {
            foreach (var building in buildings)
            {
                if (workerProvider.GetAssignedWorkerCount(building.BuildingId) == 0)
                    return building;
            }
            return null;
        }

        private void SynchronizeWorkerUtilization()
        {
            foreach (var building in buildings)
            {
                var utilization = workerProvider.GetAssignedWorkerCount(building.BuildingId) > 0
                    ? config.UtilizationStaffed
                    : config.UtilizationEmpty;
                building.ServiceSlots.SetUtilization(utilization);
            }
        }

        private void AdvanceClock(double seconds)
        {
            if (seconds == 0d)
                return;
            clock.Advance(seconds);
        }

        private List<BuildingSnapshot> CaptureBuildingConfiguration()
        {
            var snapshots = buildingProvider.Buildings;
            if (snapshots == null)
                throw new InvalidOperationException("Building provider returned no snapshot list.");

            var result = new List<BuildingSnapshot>(snapshots.Count);
            var seenBuildings = new HashSet<BuildingId>();
            foreach (var snapshot in snapshots)
            {
                if (!seenBuildings.Add(snapshot.BuildingId))
                    throw new InvalidOperationException("Building provider returned duplicate building identifiers.");
                result.Add(snapshot);
            }

            result.Sort((left, right) => ((int)left.BuildingId).CompareTo((int)right.BuildingId));
            return result;
        }

        private bool EnsureActiveConfigurationStable()
        {
            if (activeConfigurationSnapshot == null || !IsWaveConfigurationState(gameState.CurrentState))
                return true;

            List<BuildingSnapshot> currentConfiguration;
            try
            {
                currentConfiguration = CaptureBuildingConfiguration();
            }
            catch (InvalidOperationException exception)
            {
                configurationDiagnostic = "Active wave configuration is invalid: " + exception.Message;
                return false;
            }

            if (currentConfiguration.Count != activeConfigurationSnapshot.Count)
            {
                configurationDiagnostic = "Active wave configuration changed: building set/count no longer matches the wave-start snapshot.";
                return false;
            }

            for (var index = 0; index < activeConfigurationSnapshot.Count; index++)
            {
                if (currentConfiguration[index].Equals(activeConfigurationSnapshot[index]))
                    continue;

                var expected = activeConfigurationSnapshot[index];
                var actual = currentConfiguration[index];
                configurationDiagnostic = string.Format(
                    "Active wave configuration changed for {0}: expected owned={1}, capacity={2}, duration={3}, workerSlots={4}; received owned={5}, capacity={6}, duration={7}, workerSlots={8}.",
                    expected.BuildingId,
                    expected.IsOwned,
                    expected.Capacity,
                    expected.ServiceDuration,
                    expected.WorkerSlotCount,
                    actual.IsOwned,
                    actual.Capacity,
                    actual.ServiceDuration,
                    actual.WorkerSlotCount);
                return false;
            }

            configurationDiagnostic = null;
            return true;
        }

        private static bool IsWaveConfigurationState(GamePhase phase)
        {
            return phase == GamePhase.WAVE_INCOMING
                || phase == GamePhase.WAVE_ACTIVE
                || phase == GamePhase.WAVE_PAUSED
                || phase == GamePhase.WAVE_RESOLVING;
        }

        private static void ValidateDelta(double deltaSeconds)
        {
            if (deltaSeconds < 0d || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "Delta time must be finite and non-negative.");
        }

        private sealed class BuildingRuntime
        {
            public BuildingRuntime(BuildingSnapshot snapshot)
            {
                Snapshot = snapshot;
                Queue = new BuildingQueueRuntime(snapshot.BuildingId);
                ServiceSlots = new ServiceSlotRuntime(snapshot.Capacity, snapshot.ServiceDuration, 1d);
            }

            public BuildingSnapshot Snapshot { get; }
            public BuildingId BuildingId => Snapshot.BuildingId;
            public BuildingQueueRuntime Queue { get; }
            public ServiceSlotRuntime ServiceSlots { get; }
        }

        private sealed class NeedFlowRuntime
        {
            public NeedFlowRuntime(NeedType needType, BuildingRuntime building)
            {
                NeedType = needType;
                Building = building;
            }

            public NeedType NeedType { get; }
            public BuildingRuntime Building { get; }
            public int BaseDemandTotal { get; set; }
            public int ScheduledDemandTotal { get; set; }
            public int BaseEmitted { get; set; }
            public int DemandArrived { get; set; }
            public int CompletedUnits { get; set; }
            public int DemandTotal => BaseDemandTotal + ScheduledDemandTotal;
        }

        private sealed class ScheduledEventRuntime
        {
            public ScheduledEventRuntime(ScheduledDemandEvent definition)
            {
                Definition = definition;
            }

            public ScheduledDemandEvent Definition { get; }
            public bool IsStarted { get; set; }
            public bool IsFiltered { get; set; }
            public int Emitted { get; set; }
            public NeedType? TargetNeed { get; set; }
            public NeedFlowRuntime Flow { get; set; }
        }
    }
}
