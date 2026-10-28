using System;
using System.Collections.Generic;
using NUnit.Framework;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Staff;
using ResortEmpire.Gameplay.Waves;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class PhaseTwoHardeningTests
    {
        [Test]
        public void TinyWaveDeltaIsAccountedFor()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(new WaveDefinition(1, 100d, new[] { new NeedDemandDefinition(NeedType.Rooms, 1) }));
            scenario.Waves.Advance(0d);

            scenario.Waves.Advance(1e-10d);

            Assert.That(scenario.Clock.CurrentTime, Is.EqualTo(1e-10d).Within(1e-20d));
        }

        [Test]
        public void RepeatedTinyWaveDeltasAccumulateWithoutLoss()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(new WaveDefinition(1, 100d, new[] { new NeedDemandDefinition(NeedType.Rooms, 1) }));
            scenario.Waves.Advance(0d);

            for (var index = 0; index < 10000; index++)
                scenario.Waves.Advance(1e-10d);

            Assert.That(scenario.Clock.CurrentTime, Is.EqualTo(1e-6d).Within(1e-15d));
        }

        [Test]
        public void ServiceProgressAccumulatesAcrossTinySteps()
        {
            var need = new NeedRuntime(1, 1, NeedType.Rooms, 0d);
            var slots = new ServiceSlotRuntime(1, 1d, 1d);
            Assert.That(slots.TryStartService(need, 0d), Is.True);

            for (var index = 0; index < 10000; index++)
                slots.Advance(1e-10d, 0d);

            Assert.That(slots.ActiveServices[0].NormalizedProgress, Is.EqualTo(1e-6d).Within(1e-15d));
        }

        [Test]
        public void ServiceProgressIsEquivalentForTinyAndLargeSteps()
        {
            var steppedNeed = new NeedRuntime(1, 1, NeedType.Rooms, 0d);
            var steppedSlots = new ServiceSlotRuntime(1, 1d, 1d);
            steppedSlots.TryStartService(steppedNeed, 0d);
            for (var index = 0; index < 10000; index++)
                steppedSlots.Advance(1e-10d, 0d);

            var singleNeed = new NeedRuntime(1, 1, NeedType.Rooms, 0d);
            var singleSlots = new ServiceSlotRuntime(1, 1d, 1d);
            singleSlots.TryStartService(singleNeed, 0d);
            singleSlots.Advance(1e-6d, 0d);

            Assert.That(steppedSlots.ActiveServices[0].NormalizedProgress,
                Is.EqualTo(singleSlots.ActiveServices[0].NormalizedProgress).Within(1e-15d));
        }

        [Test]
        public void PausedWaveIgnoresTinyAndLargeDeltasThenResumes()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(new WaveDefinition(1, 100d, new[] { new NeedDemandDefinition(NeedType.Rooms, 1) }));
            scenario.Waves.Advance(0d);
            scenario.Waves.Advance(1d);
            Assert.That(scenario.Waves.TryPauseWave(), Is.True);
            var frozenTime = scenario.Clock.CurrentTime;

            scenario.Waves.Advance(1e-10d);
            scenario.Waves.Advance(100d);

            Assert.That(scenario.Clock.CurrentTime, Is.EqualTo(frozenTime));
            Assert.That(scenario.Waves.TryResumeWave(), Is.True);
            scenario.Waves.Advance(1e-6d);
            Assert.That(scenario.Clock.CurrentTime, Is.EqualTo(frozenTime + 1e-6d).Within(1e-15d));
        }

        [Test]
        public void InvalidWaveDeltasRemainRejected()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(new WaveDefinition(1, 100d, new[] { new NeedDemandDefinition(NeedType.Rooms, 1) }));

            Assert.Throws<ArgumentOutOfRangeException>(() => scenario.Waves.Advance(-1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => scenario.Waves.Advance(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => scenario.Waves.Advance(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => scenario.Waves.Advance(double.NegativeInfinity));
        }

        [Test]
        public void BoundaryArrivalAndCompletionAreProcessedOnceInOrder()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 1d);
            scenario.Waves.StartWave(new WaveDefinition(1, 2d, new[] { new NeedDemandDefinition(NeedType.Rooms, 2) }));
            scenario.Waves.Advance(2d);

            var snapshot = scenario.Waves.CaptureSnapshot();
            Assert.That(snapshot.CompletionRecords, Has.Count.EqualTo(1));
            Assert.That(snapshot.Needs[0].DemandArrived, Is.EqualTo(2));
            Assert.That(snapshot.CompletionRecords[0].ServiceCompletionTime, Is.EqualTo(2d).Within(1e-12d));
            Assert.That(snapshot.CompletionRecords[0].NeedUnitId, Is.EqualTo(1));

            scenario.Waves.Advance(1d);
            Assert.That(scenario.Waves.CaptureSnapshot().CompletionRecords, Has.Count.EqualTo(2));
        }

        [Test]
        public void OccupiedDestinationIsRejectedEvenWhenProviderDeclaresTwoSlots()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();
            var workers = new WorkerManager(provider, state, new[] { BuildingId.Reception, BuildingId.Restaurant });
            provider.SetWorkerSlotCount(BuildingId.Restaurant, 2);
            var before = new List<WorkerAssignmentSnapshot>(workers.Assignments);
            var changeCount = 0;
            workers.AssignmentChanged += _ => changeCount++;

            Assert.That(workers.TryReassignWorker(BuildingId.Reception, BuildingId.Restaurant), Is.False);
            Assert.That(workers.Assignments, Is.EqualTo(before));
            Assert.That(changeCount, Is.Zero);
        }

        [Test]
        public void InvalidPrototypeSlotConfigurationIsRejectedDuringInitialization()
        {
            var provider = new TestBuildingProvider(workerSlotCount: 2);
            Assert.Throws<InvalidOperationException>(() => new WorkerManager(
                provider,
                new GameState(),
                new[] { BuildingId.Reception, BuildingId.Restaurant }));
        }

        [Test]
        public void ValidWorkerTransferRemainsAtomicAndRaisesOneNotification()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();
            var workers = new WorkerManager(provider, state, new[] { BuildingId.Reception, BuildingId.Restaurant });
            var changeCount = 0;
            workers.AssignmentChanged += _ => changeCount++;

            Assert.That(workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.True);
            Assert.That(workers.GetAssignedWorkerCount(BuildingId.Reception), Is.Zero);
            Assert.That(workers.GetAssignedWorkerCount(BuildingId.BeachBar), Is.EqualTo(1));
            Assert.That(workers.WorkerCount, Is.EqualTo(2));
            Assert.That(changeCount, Is.EqualTo(1));
        }

        [Test]
        public void ActiveOwnershipChangeIsRejectedWithoutChangingRuntime()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(PhaseTwoTestData.ThreeNeedWave());
            scenario.Waves.Advance(0d);
            scenario.Buildings.SetOwned(BuildingId.Pool, true);
            var workerCount = scenario.Workers.WorkerCount;

            Assert.That(scenario.Workers.TryApplyOwnershipChange(BuildingId.Pool), Is.False);
            Assert.That(scenario.Workers.WorkerCount, Is.EqualTo(workerCount));
            Assert.That(scenario.Waves.CaptureSnapshot().Needs, Has.None.Matches<NeedFlowSnapshot>(n => n.NeedType == NeedType.Relax));
        }

        [Test]
        public void ActiveCapacityMutationBlocksDependentOperationAndPreservesService()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 10d);
            scenario.Waves.StartWave(new WaveDefinition(1, 1d, new[] { new NeedDemandDefinition(NeedType.Rooms, 1) }));
            scenario.Waves.Advance(1d);
            var before = scenario.Waves.CaptureSnapshot();
            scenario.Buildings.SetCapacity(BuildingId.Reception, 2);

            Assert.That(scenario.Waves.TryReassignWorker(BuildingId.Restaurant, BuildingId.BeachBar), Is.False);
            Assert.That(scenario.Waves.HasConfigurationMismatch, Is.True);
            Assert.That(scenario.Waves.ConfigurationDiagnostic, Does.Contain("capacity"));
            Assert.That(scenario.Waves.CaptureSnapshot().ActiveServiceCount, Is.EqualTo(before.ActiveServiceCount));
        }

        [Test]
        public void DirectWorkerReassignmentRejectsActiveCapacityMismatchAtomically()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(PhaseTwoTestData.ThreeNeedWave());
            scenario.Waves.Advance(0d);
            scenario.Buildings.SetCapacity(BuildingId.Reception, 2);
            var before = new List<WorkerAssignmentSnapshot>(scenario.Workers.Assignments);
            var eventCount = 0;
            scenario.Workers.AssignmentChanged += _ => eventCount++;

            Assert.That(scenario.Workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.False);
            Assert.That(scenario.Workers.Assignments, Is.EqualTo(before));
            Assert.That(scenario.Workers.WorkerCount, Is.EqualTo(2));
            Assert.That(eventCount, Is.Zero);
            Assert.That(scenario.Waves.HasConfigurationMismatch, Is.True);
        }

        [Test]
        public void DirectWorkerReassignmentRejectsActiveOwnershipMismatchAtomically()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(PhaseTwoTestData.ThreeNeedWave());
            scenario.Waves.Advance(0d);
            scenario.Buildings.SetOwned(BuildingId.Pool, true);
            var before = new List<WorkerAssignmentSnapshot>(scenario.Workers.Assignments);
            var eventCount = 0;
            scenario.Workers.AssignmentChanged += _ => eventCount++;

            Assert.That(scenario.Workers.TryReassignWorker(BuildingId.Reception, BuildingId.Pool), Is.False);
            Assert.That(scenario.Workers.Assignments, Is.EqualTo(before));
            Assert.That(eventCount, Is.Zero);
        }

        [Test]
        public void DirectWorkerReassignmentRejectsActiveServiceDurationMismatchAtomically()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(PhaseTwoTestData.ThreeNeedWave());
            scenario.Waves.Advance(0d);
            scenario.Buildings.SetServiceDuration(BuildingId.Reception, 2d);
            var before = new List<WorkerAssignmentSnapshot>(scenario.Workers.Assignments);
            var eventCount = 0;
            scenario.Workers.AssignmentChanged += _ => eventCount++;

            Assert.That(scenario.Workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.False);
            Assert.That(scenario.Workers.Assignments, Is.EqualTo(before));
            Assert.That(eventCount, Is.Zero);
        }

        [Test]
        public void DirectWorkerReassignmentRejectsActiveWorkerSlotMismatchAtomically()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(PhaseTwoTestData.ThreeNeedWave());
            scenario.Waves.Advance(0d);
            scenario.Buildings.SetWorkerSlotCount(BuildingId.Reception, 2);
            var before = new List<WorkerAssignmentSnapshot>(scenario.Workers.Assignments);
            var eventCount = 0;
            scenario.Workers.AssignmentChanged += _ => eventCount++;

            Assert.That(scenario.Workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.False);
            Assert.That(scenario.Workers.Assignments, Is.EqualTo(before));
            Assert.That(eventCount, Is.Zero);
        }

        [Test]
        public void DirectStableActiveTransferRemainsValidAndEmitsOnce()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 10d);
            scenario.Waves.StartWave(new WaveDefinition(1, 1d, new[] { new NeedDemandDefinition(NeedType.Rooms, 1) }));
            scenario.Waves.Advance(1d);
            var eventCount = 0;
            scenario.Workers.AssignmentChanged += _ => eventCount++;

            Assert.That(scenario.Workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.True);
            Assert.That(scenario.Workers.GetAssignedWorkerCount(BuildingId.Reception), Is.Zero);
            Assert.That(scenario.Workers.GetAssignedWorkerCount(BuildingId.BeachBar), Is.EqualTo(1));
            Assert.That(eventCount, Is.EqualTo(1));
            scenario.Waves.Advance(9d);
            Assert.That(scenario.Waves.CaptureSnapshot().CompletedUnits, Is.Zero);
        }

        [Test]
        public void DirectWorkerReassignmentRemainsProtectedWhilePausedAndDraining()
        {
            var paused = PhaseTwoTestData.CreateScenario();
            paused.Waves.StartWave(PhaseTwoTestData.ThreeNeedWave());
            paused.Waves.Advance(0d);
            Assert.That(paused.Waves.TryPauseWave(), Is.True);
            paused.Buildings.SetCapacity(BuildingId.Reception, 2);
            Assert.That(paused.Workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.False);

            var draining = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 10d);
            draining.Waves.StartWave(new WaveDefinition(1, 1d, new[] { new NeedDemandDefinition(NeedType.Rooms, 1) }));
            draining.Waves.Advance(1d);
            draining.Buildings.SetCapacity(BuildingId.Reception, 2);
            Assert.That(draining.Workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.False);
        }

        [Test]
        public void DirectWorkerReassignmentRecoversAfterProviderRestoration()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(PhaseTwoTestData.ThreeNeedWave());
            scenario.Waves.Advance(0d);
            scenario.Buildings.SetCapacity(BuildingId.Reception, 2);

            Assert.That(scenario.Workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.False);
            scenario.Buildings.SetCapacity(BuildingId.Reception, 1);

            Assert.That(scenario.Workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.True);
            scenario.Waves.Advance(1d);
            Assert.That(scenario.Waves.HasConfigurationMismatch, Is.False);
            Assert.That(scenario.Waves.CaptureSnapshot().WorkerAssignments, Has.Some.Matches<WorkerAssignmentSnapshot>(a => a.BuildingId == BuildingId.BeachBar));
        }

        [Test]
        public void ActiveServiceDurationMutationStopsAdvancementWithoutResettingProgress()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 10d);
            scenario.Waves.StartWave(new WaveDefinition(1, 1d, new[] { new NeedDemandDefinition(NeedType.Rooms, 1) }));
            scenario.Waves.Advance(1d);
            scenario.Waves.Advance(2d);
            var before = scenario.Waves.CaptureSnapshot();
            var timeBefore = scenario.Clock.CurrentTime;
            scenario.Buildings.SetServiceDuration(BuildingId.Reception, 5d);

            Assert.DoesNotThrow(() => scenario.Waves.Advance(100d));
            Assert.That(scenario.Clock.CurrentTime, Is.EqualTo(timeBefore));
            Assert.That(scenario.Waves.CaptureSnapshot().ActiveServiceCount, Is.EqualTo(before.ActiveServiceCount));
            Assert.That(scenario.Waves.HasConfigurationMismatch, Is.True);
        }

        [Test]
        public void PausedOwnershipChangeCannotBypassWaveConfigurationBoundary()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(PhaseTwoTestData.ThreeNeedWave());
            scenario.Waves.Advance(0d);
            Assert.That(scenario.Waves.TryPauseWave(), Is.True);
            scenario.Buildings.SetOwned(BuildingId.Pool, true);

            Assert.That(scenario.Workers.TryApplyOwnershipChange(BuildingId.Pool), Is.False);
            Assert.That(scenario.Waves.TryResumeWave(), Is.False);
            Assert.That(scenario.Workers.WorkerCount, Is.EqualTo(2));
        }

        [Test]
        public void BetweenWaveOwnershipNotificationAddsOneWorkerAndNextWaveSeesBuilding()
        {
            var scenario = PhaseTwoTestData.CreateScenario();
            scenario.Waves.StartWave(new WaveDefinition(1, 1d, new[] { new NeedDemandDefinition(NeedType.Rooms, 0) }));
            scenario.Waves.Advance(2d);
            Assert.That(scenario.State.CurrentState, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            scenario.State.TryTransition(GamePhase.UPGRADE);
            scenario.State.TryTransition(GamePhase.AWAITING_WAVE);
            scenario.Buildings.SetOwned(BuildingId.Pool, true);

            Assert.That(scenario.Workers.TryApplyOwnershipChange(BuildingId.Pool), Is.True);
            Assert.That(scenario.Workers.TryApplyOwnershipChange(BuildingId.Pool), Is.False);
            Assert.That(scenario.Workers.WorkerCount, Is.EqualTo(3));
            Assert.That(scenario.Waves.StartWave(new WaveDefinition(2, 1d, new[] { new NeedDemandDefinition(NeedType.Relax, 1) })), Is.True);
            scenario.Waves.Advance(2d);
            Assert.That(scenario.Waves.CaptureSnapshot().Needs, Has.Some.Matches<NeedFlowSnapshot>(n => n.NeedType == NeedType.Relax));
        }

        [Test]
        public void ScheduledDemandAtArrivalClosureIsNotSkipped()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 1d);
            var wave = new WaveDefinition(
                1,
                1d,
                new[] { new NeedDemandDefinition(NeedType.Rooms, 1) },
                new[] { new ScheduledDemandEvent("closure", ScheduledDemandEventKind.AdditionalDemand, 0d, 1d, 1, NeedType.Rooms) });
            scenario.Waves.StartWave(wave);
            scenario.Waves.Advance(1d);

            Assert.That(scenario.Waves.CaptureSnapshot().DemandTotal, Is.EqualTo(2));
            scenario.Waves.Advance(5d);
            Assert.That(scenario.Waves.CaptureSnapshot().CompletedUnits, Is.EqualTo(2));
        }

        [Test]
        public void PauseAtEventTimestampDoesNotDuplicateScheduledDemand()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 1d);
            var wave = new WaveDefinition(
                2,
                3d,
                Array.Empty<NeedDemandDefinition>(),
                new[] { new ScheduledDemandEvent("rush", ScheduledDemandEventKind.SuddenRush, 1d, 1d, 2) });
            scenario.Waves.StartWave(wave);
            scenario.Waves.Advance(1d);
            Assert.That(scenario.Waves.TryPauseWave(), Is.True);
            var pausedDemand = scenario.Waves.CaptureSnapshot().DemandTotal;
            scenario.Waves.Advance(100d);
            Assert.That(scenario.Waves.CaptureSnapshot().DemandTotal, Is.EqualTo(pausedDemand));
            Assert.That(scenario.Waves.TryResumeWave(), Is.True);
            scenario.Waves.Advance(100d);

            var snapshot = scenario.Waves.CaptureSnapshot();
            Assert.That(snapshot.DemandTotal, Is.EqualTo(2));
            Assert.That(snapshot.CompletedUnits, Is.EqualTo(2));
        }

        [Test]
        public void EqualTimestampScheduledArrivalsRemainFifo()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 1d);
            var wave = new WaveDefinition(
                1,
                2d,
                Array.Empty<NeedDemandDefinition>(),
                new[]
                {
                    new ScheduledDemandEvent("first", ScheduledDemandEventKind.AdditionalDemand, 0d, 1d, 1, NeedType.Rooms),
                    new ScheduledDemandEvent("second", ScheduledDemandEventKind.AdditionalDemand, 0d, 1d, 1, NeedType.Rooms)
                });
            scenario.Waves.StartWave(wave);
            scenario.Waves.Advance(10d);

            var records = scenario.Waves.CaptureSnapshot().CompletionRecords;
            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(records[0].NeedUnitId, Is.EqualTo(1));
            Assert.That(records[1].NeedUnitId, Is.EqualTo(2));
            Assert.That(records[0].ServiceStartTime, Is.LessThan(records[1].ServiceStartTime));
        }

        [Test]
        public void DemandConservationAndCompletionRecordsRemainConsistent()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 2d);
            scenario.Waves.StartWave(PhaseTwoTestData.ThreeNeedWave(arrivalDuration: 2d, unitsPerNeed: 4));
            scenario.Waves.Advance(1d);
            AssertDemandConservation(scenario.Waves.CaptureSnapshot(), completedWave: false);
            scenario.Waves.Advance(100d);
            var finalSnapshot = scenario.Waves.CaptureSnapshot();
            AssertDemandConservation(finalSnapshot, completedWave: true);

            var ids = new HashSet<long>();
            foreach (var record in finalSnapshot.CompletionRecords)
                Assert.That(ids.Add(record.NeedUnitId), Is.True);
        }

        [Test]
        public void IrregularAndLargeStepsProduceEquivalentWaveResults()
        {
            var irregular = RunStepScenario(new[] { 0.13d, 0.27d, 0.6d, 1.4d, 2.6d, 5d });
            var large = RunStepScenario(new[] { 10d });

            Assert.That(irregular.DemandArrived, Is.EqualTo(large.DemandArrived));
            Assert.That(irregular.CompletedUnits, Is.EqualTo(large.CompletedUnits));
            Assert.That(irregular.Lifecycle, Is.EqualTo(large.Lifecycle));
            Assert.That(irregular.CompletionRecords, Has.Count.EqualTo(large.CompletionRecords.Count));
            for (var index = 0; index < irregular.CompletionRecords.Count; index++)
            {
                Assert.That(irregular.CompletionRecords[index].NeedUnitId, Is.EqualTo(large.CompletionRecords[index].NeedUnitId));
                Assert.That(irregular.CompletionRecords[index].ArrivalTime, Is.EqualTo(large.CompletionRecords[index].ArrivalTime).Within(1e-9d));
                Assert.That(irregular.CompletionRecords[index].ServiceStartTime, Is.EqualTo(large.CompletionRecords[index].ServiceStartTime).Within(1e-9d));
                Assert.That(irregular.CompletionRecords[index].ServiceCompletionTime, Is.EqualTo(large.CompletionRecords[index].ServiceCompletionTime).Within(1e-9d));
            }
        }

        private static void AssertDemandConservation(WaveSnapshot snapshot, bool completedWave)
        {
            foreach (var need in snapshot.Needs)
            {
                Assert.That(need.DemandArrived, Is.EqualTo(need.WaitingQueueLength + need.ActiveServiceCount + need.CompletedUnits));
                Assert.That(need.Backlog, Is.EqualTo(need.WaitingQueueLength + need.ActiveServiceCount));
                if (completedWave)
                {
                    Assert.That(need.DemandArrived, Is.EqualTo(need.CompletedUnits));
                    Assert.That(need.WaitingQueueLength, Is.Zero);
                    Assert.That(need.ActiveServiceCount, Is.Zero);
                }
            }
        }

        private static WaveSnapshot RunStepScenario(IReadOnlyList<double> steps)
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
