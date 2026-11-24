using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Staff;
using ResortEmpire.Gameplay.Waves;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class WaveManagerTests
    {
        [Test]
        public void WaveStartsInIncomingThenBecomesActiveDeterministically()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            var wave = PhaseTwoTestData.ThreeNeedWave(arrivalDuration: 4d, unitsPerNeed: 1);

            Assert.That(scenario.Waves.StartWave(wave), Is.True);
            Assert.That(scenario.State.CurrentState, Is.EqualTo(GamePhase.WAVE_INCOMING));
            scenario.Waves.Advance(0d);
            Assert.That(scenario.State.CurrentState, Is.EqualTo(GamePhase.WAVE_ACTIVE));
        }

        [Test]
        public void DemandArrivesProgressivelyAtScheduledTimestamps()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 10d);
            var wave = new WaveDefinition(1, 4d, new[] { new NeedDemandDefinition(NeedType.Rooms, 4) });
            scenario.Waves.StartWave(wave);
            scenario.Waves.Advance(0d);
            scenario.Waves.Advance(0.9d);
            Assert.That(scenario.Waves.CaptureSnapshot().DemandArrived, Is.Zero);
            scenario.Waves.Advance(0.1d);
            Assert.That(scenario.Waves.CaptureSnapshot().DemandArrived, Is.EqualTo(1));
            Assert.That(scenario.Waves.CaptureSnapshot().Needs[0].DemandArrived, Is.EqualTo(1));
        }

        [Test]
        public void LargeAdvanceEmitsExactDemandAndDoesNotLoseUnits()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 2, serviceDuration: 1d);
            var wave = new WaveDefinition(
                1,
                4d,
                new[]
                {
                    new NeedDemandDefinition(NeedType.Rooms, 4),
                    new NeedDemandDefinition(NeedType.Food, 3)
                });
            scenario.Waves.StartWave(wave);
            scenario.Waves.Advance(100d);

            var snapshot = scenario.Waves.CaptureSnapshot();
            Assert.That(snapshot.Lifecycle, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            Assert.That(snapshot.DemandTotal, Is.EqualTo(7));
            Assert.That(snapshot.DemandArrived, Is.EqualTo(7));
            Assert.That(snapshot.CompletedUnits, Is.EqualTo(7));
            Assert.That(snapshot.Backlog, Is.Zero);
        }

        [Test]
        public void ArrivalWindowClosingDoesNotCompleteBusyWave()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 10d);
            var wave = new WaveDefinition(1, 2d, new[] { new NeedDemandDefinition(NeedType.Rooms, 3) });
            scenario.Waves.StartWave(wave);
            scenario.Waves.Advance(2d);

            var snapshot = scenario.Waves.CaptureSnapshot();
            Assert.That(snapshot.ArrivalsClosed, Is.True);
            Assert.That(snapshot.Lifecycle, Is.EqualTo(GamePhase.WAVE_ACTIVE));
            Assert.That(snapshot.Backlog, Is.GreaterThan(0));
        }

        [Test]
        public void WaveCompletionEventFiresExactlyOnceAfterQueuesDrain()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 1d);
            var wave = new WaveDefinition(1, 2d, new[] { new NeedDemandDefinition(NeedType.Rooms, 2) });
            var completions = new List<WaveSnapshot>();
            scenario.Waves.WaveCompleted += completions.Add;
            Assert.That(scenario.Waves.StartWave(wave), Is.True);
            scenario.Waves.Advance(2d);
            Assert.That(scenario.Waves.ArrivalsClosed, Is.True);
            Assert.That(scenario.Waves.CaptureSnapshot().Backlog, Is.EqualTo(1));
            Assert.That(completions, Is.Empty);
            scenario.Waves.Advance(100d);
            scenario.Waves.Advance(100d);

            Assert.That(completions, Has.Count.EqualTo(1));
            Assert.That(completions[0].CompletedUnits, Is.EqualTo(2));
            Assert.That(completions[0].Backlog, Is.Zero);
            Assert.That(completions[0].Lifecycle, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            Assert.That(scenario.State.CurrentState, Is.EqualTo(GamePhase.WAVE_COMPLETE));
        }

        [Test]
        public void PauseFreezesClockArrivalsAndService()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 4d);
            var wave = new WaveDefinition(1, 4d, new[] { new NeedDemandDefinition(NeedType.Rooms, 4) });
            Assert.That(scenario.Waves.StartWave(wave), Is.True);
            scenario.Waves.Advance(1.5d);
            var beforePause = scenario.Waves.CaptureSnapshot();
            var timeBeforePause = scenario.Clock.CurrentTime;
            var occupied = scenario.Waves.CaptureActiveWave().Units.Single(unit => unit.State == NeedUnitState.IN_SERVICE);
            Assert.That(occupied.Progress, Is.EqualTo(0.125d).Within(1e-9));

            Assert.That(scenario.Waves.TryPauseWave(), Is.True);
            scenario.Waves.Advance(100d);
            var duringPause = scenario.Waves.CaptureSnapshot();
            Assert.That(scenario.Clock.CurrentTime, Is.EqualTo(timeBeforePause));
            Assert.That(duringPause.DemandArrived, Is.EqualTo(beforePause.DemandArrived));
            Assert.That(duringPause.ActiveServiceCount, Is.EqualTo(beforePause.ActiveServiceCount));
            var paused = scenario.Waves.CaptureActiveWave().Units.Single(unit => unit.State == NeedUnitState.IN_SERVICE);
            Assert.That(paused.Id, Is.EqualTo(occupied.Id));
            Assert.That(paused.Progress, Is.EqualTo(occupied.Progress));

            Assert.That(scenario.Waves.TryResumeWave(), Is.True);
            scenario.Waves.Advance(100d);
            Assert.That(scenario.State.CurrentState, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            var completion = scenario.Waves.CaptureSnapshot().CompletionRecords.Single(record => record.NeedUnitId == occupied.Id);
            Assert.That(completion.ServiceCompletionTime, Is.EqualTo(5d).Within(1e-9));
        }

        [Test]
        public void UnownedNeedDoesNotSpawn()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            var wave = new WaveDefinition(
                1,
                2d,
                new[]
                {
                    new NeedDemandDefinition(NeedType.Rooms, 1),
                    new NeedDemandDefinition(NeedType.Relax, 8)
                });
            scenario.Waves.StartWave(wave);
            scenario.Waves.Advance(100d);

            var snapshot = scenario.Waves.CaptureSnapshot();
            Assert.That(snapshot.DemandTotal, Is.EqualTo(1));
            Assert.That(snapshot.Needs, Has.Count.EqualTo(1));
            Assert.That(snapshot.Needs[0].NeedType, Is.EqualTo(NeedType.Rooms));
        }

        [Test]
        public void SuddenRushTargetsCurrentUnstaffedBuildingAndUsesNormalFlow()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 1d);
            var wave = new WaveDefinition(
                2,
                8d,
                new[]
                {
                    new NeedDemandDefinition(NeedType.Rooms, 1),
                    new NeedDemandDefinition(NeedType.Drinks, 1)
                },
                new[] { new ScheduledDemandEvent("rush", ScheduledDemandEventKind.SuddenRush, 4d, 2d, 3) });
            scenario.Waves.StartWave(wave);
            scenario.Waves.Advance(2d);
            Assert.That(scenario.Waves.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.True);
            scenario.Waves.Advance(2d);

            var snapshot = scenario.Waves.CaptureSnapshot();
            Assert.That(snapshot.Needs, Has.Some.Matches<NeedFlowSnapshot>(need => need.NeedType == NeedType.Rooms && need.DemandTotal == 4));
            Assert.That(snapshot.DemandTotal, Is.EqualTo(5));
            scenario.Waves.Advance(100d);
            Assert.That(scenario.Waves.CaptureSnapshot().CompletedUnits, Is.EqualTo(5));
        }

        [Test]
        public void SuddenRushDoesNotTriggerDuringPause()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            var wave = new WaveDefinition(
                2,
                8d,
                new[] { new NeedDemandDefinition(NeedType.Drinks, 1) },
                new[] { new ScheduledDemandEvent("rush", ScheduledDemandEventKind.SuddenRush, 4d, 2d, 3) });
            scenario.Waves.StartWave(wave);
            scenario.Waves.Advance(2d);
            scenario.Waves.TryPauseWave();
            scenario.Waves.Advance(100d);
            Assert.That(scenario.Waves.CaptureSnapshot().DemandTotal, Is.EqualTo(1));
            scenario.Waves.TryResumeWave();
            scenario.Waves.Advance(2d);
            Assert.That(scenario.Waves.CaptureSnapshot().DemandTotal, Is.EqualTo(4));
        }

        [Test]
        public void CompletionRecordsPreserveScheduledArrivalAndStartTimes()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 1d);
            var wave = new WaveDefinition(1, 4d, new[] { new NeedDemandDefinition(NeedType.Rooms, 2) });
            scenario.Waves.StartWave(wave);
            scenario.Waves.Advance(100d);

            var records = scenario.Waves.CaptureSnapshot().CompletionRecords;
            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(records[0].ArrivalTime, Is.EqualTo(2d).Within(1e-9));
            Assert.That(records[0].ServiceStartTime, Is.EqualTo(2d).Within(1e-9));
            Assert.That(records[0].WaitingDuration, Is.EqualTo(0d).Within(1e-9));
        }

        [Test]
        public void IdenticalScenarioIsDeterministicAcrossStepSizes()
        {
            var small = RunScenario(new[] { 0.25d, 0.5d, 0.75d, 1.25d, 2d, 5d });
            var large = RunScenario(new[] { 10d });

            Assert.That(large.DemandArrived, Is.EqualTo(small.DemandArrived));
            Assert.That(large.CompletedUnits, Is.EqualTo(small.CompletedUnits));
            Assert.That(large.Lifecycle, Is.EqualTo(small.Lifecycle));
            Assert.That(large.CompletionRecords, Has.Count.EqualTo(small.CompletionRecords.Count));
            for (var index = 0; index < small.CompletionRecords.Count; index++)
            {
                Assert.That(large.CompletionRecords[index].ArrivalTime, Is.EqualTo(small.CompletionRecords[index].ArrivalTime).Within(1e-9));
                Assert.That(large.CompletionRecords[index].ServiceStartTime, Is.EqualTo(small.CompletionRecords[index].ServiceStartTime).Within(1e-9));
                Assert.That(large.CompletionRecords[index].ServiceCompletionTime, Is.EqualTo(small.CompletionRecords[index].ServiceCompletionTime).Within(1e-9));
            }
        }

        [Test]
        public void ActiveWorkerReassignmentRemainsAvailableThroughQueueDrain()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 8d);
            var wave = new WaveDefinition(1, 4d, new[]
            {
                new NeedDemandDefinition(NeedType.Rooms, 3),
                new NeedDemandDefinition(NeedType.Drinks, 3)
            });
            Assert.That(scenario.Waves.StartWave(wave), Is.True);
            scenario.Waves.Advance(4d);
            var draining = scenario.Waves.CaptureSnapshot();
            Assert.That(draining.ArrivalsClosed, Is.True);
            Assert.That(draining.Lifecycle, Is.EqualTo(GamePhase.WAVE_ACTIVE));
            Assert.That(draining.WaitingQueueLength, Is.EqualTo(4));
            var before = scenario.Waves.CaptureActiveWave().Units.Where(unit => unit.State == NeedUnitState.IN_SERVICE).ToArray();
            var rooms = before.Single(unit => unit.Need == NeedType.Rooms);
            var drinks = before.Single(unit => unit.Need == NeedType.Drinks);

            Assert.That(scenario.Waves.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.True);
            Assert.That(scenario.Workers.GetAssignedWorkerCount(BuildingId.Reception), Is.Zero);
            Assert.That(scenario.Workers.GetAssignedWorkerCount(BuildingId.BeachBar), Is.EqualTo(1));
            scenario.Waves.Advance(2d);
            var after = scenario.Waves.CaptureActiveWave().Units;
            Assert.That(after.Single(unit => unit.Id == rooms.Id).Progress - rooms.Progress, Is.EqualTo(0.125d).Within(1e-9));
            Assert.That(after.Single(unit => unit.Id == drinks.Id).Progress - drinks.Progress, Is.EqualTo(0.25d).Within(1e-9));
            Assert.That(scenario.Waves.CaptureSnapshot().DemandArrived, Is.EqualTo(6));

            scenario.Waves.Advance(100d);
            var complete = scenario.Waves.CaptureSnapshot();
            Assert.That(complete.Lifecycle, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            Assert.That(complete.CompletedUnits, Is.EqualTo(6));
            Assert.That(complete.Backlog, Is.Zero);
            foreach (var need in new[] { NeedType.Rooms, NeedType.Drinks })
            {
                var served = complete.CompletionRecords.Where(record => record.NeedType == need).ToArray();
                Assert.That(served.Select(record => record.ArrivalTime), Is.EqualTo(new[] { 4d / 3d, 8d / 3d, 4d }).Within(1e-9));
                Assert.That(served[1].ServiceStartTime, Is.EqualTo(served[0].ServiceCompletionTime).Within(1e-9));
                Assert.That(served[2].ServiceStartTime, Is.EqualTo(served[1].ServiceCompletionTime).Within(1e-9));
            }
        }

        private static WaveSnapshot RunScenario(IReadOnlyList<double> steps)
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 1d);
            scenario.Waves.StartWave(new WaveDefinition(
                1,
                4d,
                new[]
                {
                    new NeedDemandDefinition(NeedType.Rooms, 4),
                    new NeedDemandDefinition(NeedType.Drinks, 3)
                }));
            foreach (var step in steps)
                scenario.Waves.Advance(step);
            return scenario.Waves.CaptureSnapshot();
        }
    }
}
