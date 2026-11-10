using System;
using System.Linq;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Waves;
using UnityEngine;

namespace ResortEmpire.Gameplay.Tests
{
    /// <summary>S2 need states. Synthetic observations pin exact queue sequences; scenario tests use the real simulation.</summary>
    public sealed class WaveNeedStatusTrackerTests
    {
        // Defaults: more than 2 waiting, still growing after 4 s; RECOVERING after a 2-guest drop; OK at 1 or fewer.
        private WaveNeedStatusTracker tracker;

        [SetUp]
        public void SetUp()
        {
            tracker = new WaveNeedStatusTracker();
        }

        [Test]
        public void OccupiedSeatsAloneNeverRaiseABottleneck()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 5, serviceDuration: 100d);
            scenario.Waves.StartWave(new WaveDefinition(1, 5d, new[] { new NeedDemandDefinition(NeedType.Drinks, 5) }));
            for (var step = 0; step < 40; step++)
            {
                scenario.Waves.Advance(0.5d);
                var drinks = tracker.Update(scenario.Waves.CaptureSnapshot(), scenario.Clock.CurrentTime).Needs.Single();
                Assert.That(drinks.Waiting, Is.Zero);
                Assert.That(drinks.Status, Is.EqualTo(NeedServiceStatus.OK));
            }
            var final = tracker.Update(scenario.Waves.CaptureSnapshot(), scenario.Clock.CurrentTime).Needs.Single();
            Assert.That(final.InService, Is.EqualTo(5), "Every seat is busy, yet nobody is waiting.");
            Assert.That(final.Capacity, Is.EqualTo(5));
        }

        [Test]
        public void QueueOverThresholdMustKeepGrowingForTheConfiguredDuration()
        {
            Assert.That(Step(0d, Drinks(3)).Status, Is.EqualTo(NeedServiceStatus.OK), "Exceeding the threshold only starts the clock.");
            Assert.That(Step(2d, Drinks(4)).Status, Is.EqualTo(NeedServiceStatus.OK));
            Assert.That(Step(3.9d, Drinks(5)).Status, Is.EqualTo(NeedServiceStatus.OK));

            var wave = tracker.Update(1, false, new[] { Drinks(5) }, 4d);
            Assert.That(wave.Needs.Single().Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK));
            Assert.That(wave.Feedback.Select(f => f.Kind), Is.EqualTo(new[] { NeedFeedbackKind.BottleneckMissingWorker }));
        }

        [Test]
        public void QueueThatOnlyHoldsAboveThresholdIsNotABottleneck()
        {
            for (var time = 0d; time <= 30d; time += 0.5d)
                Assert.That(Step(time, Drinks(3)).Status, Is.EqualTo(NeedServiceStatus.OK), "t=" + time);
        }

        [Test]
        public void QueueFluctuatingAroundThresholdNeverWarnsOrFlickers()
        {
            var pattern = new[] { 1, 2, 3, 4, 3, 2, 3, 2, 3, 4, 2, 1 };
            for (var step = 0; step < 120; step++)
            {
                var wave = tracker.Update(1, false, new[] { Drinks(pattern[step % pattern.Length]) }, step);
                Assert.That(wave.Needs.Single().Status, Is.EqualTo(NeedServiceStatus.OK), "step " + step);
                Assert.That(wave.Feedback, Is.Empty, "step " + step);
            }
        }

        [Test]
        public void ShrinkingRestartsTheGrowthWindow()
        {
            Step(0d, Drinks(5));
            Step(2d, Drinks(4));
            Assert.That(Step(5d, Drinks(5)).Status, Is.EqualTo(NeedServiceStatus.OK), "Only 3 s of growth since it shrank.");
            Assert.That(Step(6d, Drinks(5)).Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK));
        }

        [Test]
        public void PausedWaveNeverConfirmsABottleneck()
        {
            for (var queue = 3; queue < 12; queue++)
                Assert.That(Step(10d, Drinks(queue)).Status, Is.EqualTo(NeedServiceStatus.OK), "Simulation time is frozen.");
        }

        [Test]
        public void WorkerArrivalShowsRecoveringAtOnceAndTheNextArrivalDoesNotFlipItBack()
        {
            ReachBottleneck(staffed: false);

            var moved = tracker.Update(1, false, new[] { Drinks(5, staffed: true) }, 10d);
            Assert.That(moved.Needs.Single().Status, Is.EqualTo(NeedServiceStatus.RECOVERING), "The queue has not drained yet.");
            Assert.That(moved.Feedback.Single().Kind, Is.EqualTo(NeedFeedbackKind.RecoveringAfterWorker));

            Assert.That(Step(10.5d, Drinks(6, staffed: true)).Status, Is.EqualTo(NeedServiceStatus.RECOVERING));
            Assert.That(Step(13d, Drinks(6, staffed: true)).Status, Is.EqualTo(NeedServiceStatus.RECOVERING));
            Assert.That(Step(16d, Drinks(3, staffed: true)).Status, Is.EqualTo(NeedServiceStatus.RECOVERING));

            var cleared = tracker.Update(1, false, new[] { Drinks(1, staffed: true) }, 20d);
            Assert.That(cleared.Needs.Single().Status, Is.EqualTo(NeedServiceStatus.OK));
            Assert.That(cleared.Feedback.Single().Kind, Is.EqualTo(NeedFeedbackKind.BackToNormal));
        }

        [Test]
        public void RecoveringQueueThatKeepsGrowingAgainReturnsToBottleneck()
        {
            ReachBottleneck(staffed: false);
            Step(10d, Drinks(4, staffed: true));

            Assert.That(Step(11d, Drinks(6, staffed: true)).Status, Is.EqualTo(NeedServiceStatus.RECOVERING), "Growth just started.");
            var worse = tracker.Update(1, false, new[] { Drinks(8, staffed: true) }, 15d);
            Assert.That(worse.Needs.Single().Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK));
            Assert.That(worse.Needs.Single().Cause, Is.EqualTo(NeedQueueCause.ServiceCapacity));
            Assert.That(worse.Feedback.Single().Kind, Is.EqualTo(NeedFeedbackKind.BottleneckCapacity));
        }

        [Test]
        public void RemovingTheWorkerFromARecoveringNeedWarnsImmediately()
        {
            ReachBottleneck(staffed: false);
            Step(10d, Drinks(5, staffed: true));

            var removed = tracker.Update(1, false, new[] { Drinks(5, staffed: false) }, 10.1d);
            Assert.That(removed.Needs.Single().Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK));
            Assert.That(removed.Feedback.Single().Kind, Is.EqualTo(NeedFeedbackKind.BottleneckMissingWorker));
        }

        [Test]
        public void QueueDrainingFromItsPeakShowsRecoveringBeforeOk()
        {
            ReachBottleneck(staffed: true);
            Assert.That(Step(9d, Drinks(6, staffed: true)).Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK));
            Assert.That(Step(10d, Drinks(5, staffed: true)).Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK), "One guest is noise.");

            var draining = tracker.Update(1, false, new[] { Drinks(4, staffed: true) }, 11d);
            Assert.That(draining.Needs.Single().Status, Is.EqualTo(NeedServiceStatus.RECOVERING));
            Assert.That(draining.Feedback.Single().Kind, Is.EqualTo(NeedFeedbackKind.Recovering));
            Assert.That(Step(12d, Drinks(2, staffed: true)).Status, Is.EqualTo(NeedServiceStatus.RECOVERING));
            Assert.That(Step(13d, Drinks(1, staffed: true)).Status, Is.EqualTo(NeedServiceStatus.OK));
        }

        [Test]
        public void CauseSeparatesMissingWorkerFromFullSeats()
        {
            Assert.That(Step(0d, Drinks(3, staffed: false)).Cause, Is.EqualTo(NeedQueueCause.MissingWorker));
            Assert.That(Step(1d, Drinks(3, staffed: true, inService: 3, capacity: 3)).Cause, Is.EqualTo(NeedQueueCause.ServiceCapacity));
            Assert.That(Step(2d, Drinks(0, staffed: true, inService: 3, capacity: 3)).Cause, Is.EqualTo(NeedQueueCause.None));
        }

        [Test]
        public void SatisfiedOnlyOnceArrivalsCloseAndEveryUnitIsServed()
        {
            Assert.That(Step(0d, Drinks(0, served: 20, total: 20)).Status, Is.EqualTo(NeedServiceStatus.OK), "More guests can still arrive.");

            var closed = tracker.Update(1, true, new[] { Drinks(0, served: 20, total: 20) }, 1d);
            Assert.That(closed.Needs.Single().Status, Is.EqualTo(NeedServiceStatus.SATISFIED));
            Assert.That(closed.Feedback.Select(f => f.Kind),
                Is.EqualTo(new[] { NeedFeedbackKind.ArrivalsEnded, NeedFeedbackKind.Satisfied }));
            Assert.That(closed.Feedback.First().IsWaveWide, Is.True);

            var later = tracker.Update(1, true, new[] { Drinks(0, served: 20, total: 20) }, 2d);
            Assert.That(later.Feedback, Is.Empty, "Every change is announced once.");
        }

        [Test]
        public void WorkerMoveBetweenCalmBuildingsReportsBothHalves()
        {
            tracker.Update(1, false, new[] { Need(NeedType.Rooms, 0, true), Need(NeedType.Drinks, 0, false) }, 0d);
            var moved = tracker.Update(1, false, new[] { Need(NeedType.Rooms, 0, false), Need(NeedType.Drinks, 0, true) }, 1d);

            Assert.That(moved.Needs.All(n => n.Status == NeedServiceStatus.OK), Is.True);
            Assert.That(moved.Feedback.Select(f => f.Kind), Is.EqualTo(new[] { NeedFeedbackKind.WorkerRemoved, NeedFeedbackKind.WorkerAssigned }));
            Assert.That(moved.Feedback.Select(f => f.BuildingId), Is.EqualTo(new[] { BuildingId.Reception, BuildingId.BeachBar }));
        }

        [Test]
        public void NeedsAreReportedInStableNeedOrder()
        {
            var wave = tracker.Update(1, false, new[]
            {
                Need(NeedType.OceanView, 0, true), Need(NeedType.Food, 0, true), Need(NeedType.Rooms, 0, true),
                Need(NeedType.Relax, 0, true), Need(NeedType.Drinks, 0, false)
            }, 0d);
            Assert.That(wave.Needs.Select(n => n.NeedType), Is.EqualTo(new[]
                { NeedType.Rooms, NeedType.Drinks, NeedType.Food, NeedType.Relax, NeedType.OceanView }));
        }

        [Test]
        public void NewWaveStartsFromAFreshState()
        {
            ReachBottleneck(staffed: false);
            var next = tracker.Update(2, false, new[] { Drinks(5) }, 100d);
            Assert.That(next.Needs.Single().Status, Is.EqualTo(NeedServiceStatus.OK));
            Assert.That(next.Feedback, Is.Empty);
        }

        [Test]
        public void ConfiguredThresholdsAreUsedAndCopiedAtCreation()
        {
            var settings = new WaveNeedStatusSettings { BottleneckQueueThreshold = 5, GrowthDuration = 1f, RecoveredQueue = 2 };
            tracker = new WaveNeedStatusTracker(settings);
            settings.BottleneckQueueThreshold = 1;

            Step(0d, Drinks(4));
            Assert.That(Step(5d, Drinks(5)).Status, Is.EqualTo(NeedServiceStatus.OK), "5 does not exceed the threshold of 5.");
            Step(6d, Drinks(6));
            Assert.That(Step(7d, Drinks(7)).Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK));
            Assert.That(Step(8d, Drinks(2)).Status, Is.EqualTo(NeedServiceStatus.OK), "2 is the configured recovered queue.");
        }

        [Test]
        public void ZeroGrowthDurationWarnsAsSoonAsTheThresholdIsExceeded()
        {
            tracker = new WaveNeedStatusTracker(new WaveNeedStatusSettings { GrowthDuration = 0f });
            Assert.That(Step(0d, Drinks(3)).Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK));
        }

        [Test]
        public void SettingsMustLeaveRoomForHysteresis()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WaveNeedStatusTracker(new WaveNeedStatusSettings { BottleneckQueueThreshold = 2, RecoveredQueue = 2 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WaveNeedStatusTracker(new WaveNeedStatusSettings { RecoveredQueue = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WaveNeedStatusTracker(new WaveNeedStatusSettings { GrowthDuration = -1f }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WaveNeedStatusTracker(new WaveNeedStatusSettings { GrowthDuration = float.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WaveNeedStatusTracker(new WaveNeedStatusSettings { RecoveryDrop = 0 }));
        }

        [Test]
        public void ScenarioGrowingQueueWarnsAfterTheDurationAndWorkerMoveRecoversAtOnce()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 4d);
            scenario.Waves.StartWave(new WaveDefinition(1, 8d, new[] { new NeedDemandDefinition(NeedType.Drinks, 8) }));
            scenario.Waves.Advance(4.5d);
            var early = tracker.Update(scenario.Waves.CaptureSnapshot(), scenario.Clock.CurrentTime).Needs.Single();
            Assert.That(early.Waiting, Is.EqualTo(3));
            Assert.That(early.IsStaffed, Is.False);
            Assert.That(early.Status, Is.EqualTo(NeedServiceStatus.OK), "Over the threshold, but not growing for long enough yet.");

            var drinks = early;
            for (var step = 0; step < 20 && drinks.Status != NeedServiceStatus.BOTTLENECK; step++)
            {
                scenario.Waves.Advance(0.25d);
                drinks = tracker.Update(scenario.Waves.CaptureSnapshot(), scenario.Clock.CurrentTime).Needs.Single();
            }
            Assert.That(drinks.Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK));
            Assert.That(drinks.Cause, Is.EqualTo(NeedQueueCause.MissingWorker));

            Assert.That(scenario.Waves.TryReassignWorker(BuildingId.Restaurant, BuildingId.BeachBar), Is.True);
            var after = tracker.Update(scenario.Waves.CaptureSnapshot(), scenario.Clock.CurrentTime);
            Assert.That(after.Needs.Single().Waiting, Is.EqualTo(drinks.Waiting), "The queue has not drained yet.");
            Assert.That(after.Needs.Single().Status, Is.EqualTo(NeedServiceStatus.RECOVERING));
            Assert.That(after.Feedback.Single().Kind, Is.EqualTo(NeedFeedbackKind.RecoveringAfterWorker));
        }

        [Test]
        public void ScenarioOnlyOwnedNeedsAppearAndKeepTheirOrderAsPoolIsAdded()
        {
            var demand = new[]
            {
                new NeedDemandDefinition(NeedType.OceanView, 2), new NeedDemandDefinition(NeedType.Relax, 2),
                new NeedDemandDefinition(NeedType.Food, 2), new NeedDemandDefinition(NeedType.Drinks, 2),
                new NeedDemandDefinition(NeedType.Rooms, 2)
            };
            var three = PhaseTwoTestData.CreateScenario();
            three.Waves.StartWave(new WaveDefinition(1, 4d, demand));
            Assert.That(tracker.Update(three.Waves.CaptureSnapshot(), 0d).Needs.Select(n => n.NeedType),
                Is.EqualTo(new[] { NeedType.Rooms, NeedType.Drinks, NeedType.Food }));

            var four = PhaseTwoTestData.CreateScenario(poolOwned: true);
            four.Waves.StartWave(new WaveDefinition(1, 4d, demand));
            Assert.That(tracker.Update(four.Waves.CaptureSnapshot(), 0d).Needs.Select(n => n.NeedType),
                Is.EqualTo(new[] { NeedType.Rooms, NeedType.Drinks, NeedType.Food, NeedType.Relax }));

            var five = PhaseTwoTestData.CreateScenario(poolOwned: true, bungalowsOwned: true);
            five.Waves.StartWave(new WaveDefinition(1, 4d, demand));
            Assert.That(tracker.Update(five.Waves.CaptureSnapshot(), 0d).Needs.Select(n => n.NeedType),
                Is.EqualTo(new[] { NeedType.Rooms, NeedType.Drinks, NeedType.Food, NeedType.Relax, NeedType.OceanView }));
        }

        [Test]
        public void ScenarioEveryNeedIsSatisfiedOnceArrivalsCloseAndServiceFinishes()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 1d);
            scenario.Waves.StartWave(PhaseTwoTestData.ThreeNeedWave());
            for (var step = 0; step < 200 && scenario.State.CurrentState != GamePhase.WAVE_COMPLETE; step++)
            {
                scenario.Waves.Advance(0.25d);
                tracker.Update(scenario.Waves.CaptureSnapshot(), scenario.Clock.CurrentTime);
            }

            var final = tracker.Update(scenario.Waves.CaptureSnapshot(), scenario.Clock.CurrentTime);
            Assert.That(final.Needs, Has.Count.EqualTo(3));
            Assert.That(final.Needs.All(need => need.Status == NeedServiceStatus.SATISFIED), Is.True);
        }

        [Test]
        public void ProductionTuningFlagsTheUnstaffedBeachBarButNeverTheStaffedBuildings()
        {
            var root = new GameObject("TrackerProductionTuning");
            try
            {
                var gameplay = root.AddComponent<ResortGameplayBootstrap>();
                Assert.That(gameplay.TryConfigureProgression(EconomyTestFixtures.Config(), new TestProgressionPersistence()), Is.True);
                Assert.That(gameplay.TryStartWave().Succeeded, Is.True);

                NeedStatusSnapshot? beachBar = null;
                for (var step = 0; step < 400 && gameplay.CurrentPhase != GamePhase.WAVE_COMPLETE; step++)
                {
                    gameplay.AdvanceSimulation(0.5d);
                    var wave = tracker.Capture(gameplay);
                    foreach (var need in wave.Needs.Where(n => n.IsStaffed))
                        Assert.That(need.Status, Is.Not.EqualTo(NeedServiceStatus.BOTTLENECK).And.Not.EqualTo(NeedServiceStatus.RECOVERING),
                            need.BuildingId + " is staffed and its queue never grows.");
                    var drinks = wave.Needs.Single(n => n.BuildingId == BuildingId.BeachBar);
                    if (drinks.Status == NeedServiceStatus.BOTTLENECK) { beachBar = drinks; break; }
                }

                Assert.That(beachBar.HasValue, Is.True, "Wave 1 teaches the missing Worker on Beach Bar.");
                Assert.That(beachBar.Value.Cause, Is.EqualTo(NeedQueueCause.MissingWorker));
                Assert.That(gameplay.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar).Succeeded, Is.True);
                Assert.That(tracker.Capture(gameplay).Needs.Single(n => n.BuildingId == BuildingId.BeachBar).Status,
                    Is.EqualTo(NeedServiceStatus.RECOVERING));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private void ReachBottleneck(bool staffed)
        {
            Step(0d, Drinks(3, staffed));
            Assert.That(Step(5d, Drinks(5, staffed)).Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK));
        }

        private NeedStatusSnapshot Step(double time, NeedQueueObservation need)
        {
            return tracker.Update(1, false, new[] { need }, time).Needs.Single();
        }

        private static NeedQueueObservation Drinks(int waiting, bool staffed = false, int served = 0, int total = 20,
            int inService = 3, int capacity = 3)
        {
            return new NeedQueueObservation(NeedType.Drinks, served, total, waiting, inService, capacity, staffed);
        }

        private static NeedQueueObservation Need(NeedType need, int waiting, bool staffed)
        {
            return new NeedQueueObservation(need, 0, 10, waiting, 0, 3, staffed);
        }
    }
}
