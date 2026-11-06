using System;
using System.Collections.Generic;
using System.IO;

namespace ResortEmpire.Gameplay.Waves
{
    public sealed partial class WaveManager
    {
        public bool HasUsedServiceBoosts => buildings.Exists(building => building.Boost.SpeedMultiplier > 0d);

        public bool HasUsedServiceBoost(BuildingId id) =>
            buildingsById.TryGetValue(id, out var building) && building.Boost.SpeedMultiplier > 0d;

        public double ServiceBoostRemaining(BuildingId id) =>
            buildingsById.TryGetValue(id, out var building)
                ? Math.Max(0d, building.Boost.ExpiresAt - clock.CurrentTime)
                : 0d;

        public string ServiceBoostRestriction(BuildingId id)
        {
            if (CurrentPhase != GamePhase.WAVE_ACTIVE)
                return "Speed Up is available while the wave is running.";
            if (!buildingsById.TryGetValue(id, out var building)
                || !flowsByNeed.TryGetValue(building.Snapshot.NeedType, out var flow))
                return "Choose an owned service in the current wave.";
            if (HasUsedServiceBoost(id))
                return "This building has already used Speed Up this wave.";
            if (arrivalsClosed && flow.CompletedUnits == flow.DemandTotal)
                return "This building has finished serving guests.";
            return null;
        }

        // The progression transaction validates and persists this exact boost before applying it.
        public void ApplyServiceBoost(ServiceBoostCheckpoint boost)
        {
            if (ServiceBoostRestriction(boost.Building) != null || !IsValidBoost(boost)
                || boost.ExpiresAt <= clock.CurrentTime)
                throw new InvalidOperationException("The service boost is no longer eligible.");
            buildingsById[boost.Building].Boost = boost;
            SynchronizeWorkerUtilization();
        }

        public ActiveWaveCheckpoint CaptureActiveWave(ServiceBoostCheckpoint? pendingBoost = null, string dismissRushId = null)
        {
            if (CurrentPhase != GamePhase.WAVE_ACTIVE && CurrentPhase != GamePhase.WAVE_PAUSED)
                throw new InvalidOperationException("Only a running wave can be captured.");

            var baseArrivals = new List<BaseArrivalCheckpoint>();
            foreach (var flow in flows)
                if (flow.BaseDemandTotal > 0)
                    baseArrivals.Add(new BaseArrivalCheckpoint { Need = flow.NeedType, Emitted = flow.BaseEmitted });
            var events = new List<ScheduledArrivalCheckpoint>();
            foreach (var scheduled in scheduledEvents)
                events.Add(new ScheduledArrivalCheckpoint
                {
                    EventId = scheduled.Definition.EventId, Started = scheduled.IsStarted,
                    Filtered = scheduled.IsFiltered,
                    NotificationDismissed = scheduled.NotificationDismissed || scheduled.Definition.EventId == dismissRushId,
                    TargetNeed = scheduled.TargetNeed.GetValueOrDefault(), Emitted = scheduled.Emitted
                });

            var units = new List<WaveUnitCheckpoint>();
            foreach (var record in completionRecords)
                units.Add(WaveUnitCheckpoint.Completed(record));
            var boosts = new List<ServiceBoostCheckpoint>();
            foreach (var building in buildings)
            {
                foreach (var unit in building.Queue.WaitingUnits)
                    units.Add(WaveUnitCheckpoint.Waiting(unit));
                building.ServiceSlots.CaptureServices(units);
                var boost = pendingBoost.HasValue && pendingBoost.Value.Building == building.BuildingId
                    ? pendingBoost.Value : building.Boost;
                if (boost.SpeedMultiplier > 0d) boosts.Add(boost);
            }

            return new ActiveWaveCheckpoint
            {
                WaveId = CurrentWaveId, ClockTime = clock.CurrentTime, WaveStartTime = waveStartTime,
                ArrivalElapsed = waveElapsed, NextUnitId = nextNeedUnitId, BaseArrivals = baseArrivals.ToArray(),
                ScheduledArrivals = events.ToArray(), Units = units.ToArray(), Boosts = boosts.ToArray()
            };
        }

        public void RestoreActiveWave(WaveDefinition wave, ActiveWaveCheckpoint save)
        {
            Check(CurrentPhase == GamePhase.AWAITING_WAVE && wave != null && save.WaveId == wave.WaveId
                && save.ClockTime == clock.CurrentTime && save.WaveStartTime >= 0d
                && save.WaveStartTime <= save.ClockTime && save.ArrivalElapsed >= 0d
                && save.ArrivalElapsed <= wave.ArrivalDuration && save.NextUnitId > 0
                && save.BaseArrivals != null && save.ScheduledArrivals != null && save.Units != null
                && save.Boosts != null && save.Boosts.Length > 0);

            ResetRuntime(wave);
            waveStartTime = save.WaveStartTime;
            waveElapsed = save.ArrivalElapsed;
            incomingElapsed = config.IncomingDuration;
            arrivalsClosed = waveElapsed >= wave.ArrivalDuration;
            nextNeedUnitId = save.NextUnitId;
            var restoredNeeds = new HashSet<NeedType>();
            foreach (var arrival in save.BaseArrivals)
            {
                Check(restoredNeeds.Add(arrival.Need) && flowsByNeed.ContainsKey(arrival.Need));
                var flow = flowsByNeed[arrival.Need];
                Check(flow.BaseDemandTotal > 0 && arrival.Emitted >= 0 && arrival.Emitted <= flow.BaseDemandTotal);
                flow.BaseEmitted = arrival.Emitted;
                flow.DemandArrived = arrival.Emitted;
            }
            foreach (var flow in flows)
                Check(flow.BaseDemandTotal == 0 || restoredNeeds.Contains(flow.NeedType));

            RestoreScheduledArrivals(save.ScheduledArrivals);
            RestoreUnits(save.Units);
            foreach (var boost in save.Boosts)
            {
                Check(IsValidBoost(boost) && !HasUsedServiceBoost(boost.Building));
                buildingsById[boost.Building].Boost = boost;
            }
            SynchronizeWorkerUtilization();
            gameState.TryTransition(GamePhase.WAVE_INCOMING);
            gameState.TryTransition(GamePhase.WAVE_ACTIVE);
        }

        private void RestoreScheduledArrivals(ScheduledArrivalCheckpoint[] savedEvents)
        {
            Check(savedEvents.Length == scheduledEvents.Count);
            for (var index = 0; index < savedEvents.Length; index++)
            {
                var saved = savedEvents[index];
                var scheduled = scheduledEvents[index];
                Check(saved.EventId == scheduled.Definition.EventId && saved.Emitted >= 0
                    && saved.Emitted <= scheduled.Definition.DemandUnits
                    && (saved.Started || saved.Emitted == 0)
                    && (!saved.Started || !saved.Filtered));
                if (scheduled.Definition.Kind == ScheduledDemandEventKind.SuddenRush && saved.Started)
                {
                    Check(Enum.IsDefined(typeof(NeedType), saved.TargetNeed));
                    Check(buildingsById.ContainsKey(BuildingNeedMapping.GetBuildingId(saved.TargetNeed)));
                    var building = buildingsById[BuildingNeedMapping.GetBuildingId(saved.TargetNeed)];
                    scheduled.TargetNeed = saved.TargetNeed;
                    scheduled.Flow = EnsureFlow(saved.TargetNeed, building);
                    scheduled.Flow.ScheduledDemandTotal += scheduled.Definition.DemandUnits;
                }
                scheduled.IsStarted = saved.Started;
                scheduled.IsFiltered = saved.Filtered;
                scheduled.Emitted = saved.Emitted;
                scheduled.NotificationDismissed = saved.NotificationDismissed;
                if (saved.Started)
                {
                    Check(scheduled.Flow != null);
                    scheduled.Flow.DemandArrived += saved.Emitted;
                }
            }
        }

        private void RestoreUnits(WaveUnitCheckpoint[] units)
        {
            var ids = new HashSet<long>();
            var counts = new Dictionary<NeedType, int>();
            foreach (var saved in units)
            {
                Check(saved.Id > 0 && saved.Id < nextNeedUnitId && ids.Add(saved.Id)
                    && flowsByNeed.ContainsKey(saved.Need) && saved.Arrival <= clock.CurrentTime);
                var flow = flowsByNeed[saved.Need];
                var unit = new NeedRuntime(saved.Id, CurrentWaveId, saved.Need, saved.Arrival);
                counts.TryGetValue(saved.Need, out var count);
                counts[saved.Need] = count + 1;
                switch (saved.State)
                {
                    case NeedUnitState.WAITING:
                        Check(flow.Building.Queue.Enqueue(unit));
                        break;
                    case NeedUnitState.IN_SERVICE:
                        Check(saved.ServiceStart <= clock.CurrentTime
                            && flow.Building.ServiceSlots.TryRestoreService(unit, saved.ServiceStart, saved.Progress));
                        break;
                    case NeedUnitState.COMPLETED:
                        Check(saved.ServiceEnd <= clock.CurrentTime && unit.TryStartService(saved.ServiceStart)
                            && unit.TryComplete(saved.ServiceEnd));
                        completionRecords.Add(unit.GetCompletionRecord());
                        flow.CompletedUnits++;
                        break;
                    default:
                        Check(false);
                        break;
                }
            }
            foreach (var flow in flows)
            {
                counts.TryGetValue(flow.NeedType, out var count);
                Check(count == flow.DemandArrived);
            }
        }

        private bool IsValidBoost(ServiceBoostCheckpoint boost) =>
            buildingsById.ContainsKey(boost.Building) && boost.ExpiresAt > waveStartTime
            && !double.IsInfinity(boost.ExpiresAt) && boost.SpeedMultiplier > 1d
            && !double.IsInfinity(boost.SpeedMultiplier);

        private static void Check(bool condition)
        {
            if (!condition)
                throw new InvalidDataException("The active wave checkpoint is invalid or incompatible with the wave configuration.");
        }
    }
}
