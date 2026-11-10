using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Waves;
using UnityEngine;

namespace ResortEmpire.Gameplay.Tests
{
    /// <summary>
    /// Bottleneck timing on the real simulation, sampled through the S2 facade the way the
    /// presenter samples it: spikes, sustained growth, pause, Worker moves and wave resets.
    /// </summary>
    public sealed class BottleneckDetectionTimingTests
    {
        private const double SampleStep = 0.5d;
        private readonly List<GameObject> roots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var root in roots)
                if (root != null) Object.DestroyImmediate(root);
            roots.Clear();
        }

        [Test]
        public void ShortArrivalBurstThatStaffedSeatsClearNeverWarns()
        {
            // Ten guests in 1.5 s at a staffed 3-seat Reception: the queue spikes to 7 and drains in two service rounds.
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 3, serviceDuration: 3d);
            scenario.Waves.StartWave(new WaveDefinition(1, 1.5d, new[] { new NeedDemandDefinition(NeedType.Rooms, 10) }));
            var tracker = new WaveNeedStatusTracker();
            var peak = 0;
            var overThresholdFor = 0d;
            for (var step = 0; step < 80 && scenario.State.CurrentState != GamePhase.WAVE_COMPLETE; step++)
            {
                scenario.Waves.Advance(0.25d);
                var wave = tracker.Update(scenario.Waves.CaptureSnapshot(), scenario.Clock.CurrentTime);
                var rooms = wave.Needs.Single();
                peak = Mathf.Max(peak, rooms.Waiting);
                if (rooms.Waiting > 2) overThresholdFor += 0.25d;
                Assert.That(rooms.Status, Is.Not.EqualTo(NeedServiceStatus.BOTTLENECK).And.Not.EqualTo(NeedServiceStatus.RECOVERING),
                    "t=" + scenario.Clock.CurrentTime + " waiting " + rooms.Waiting);
                Assert.That(wave.Feedback.Select(f => f.Kind), Has.None.EqualTo(NeedFeedbackKind.BottleneckCapacity));
            }
            Assert.That(peak, Is.GreaterThanOrEqualTo(6), "The burst really spiked.");
            Assert.That(overThresholdFor, Is.GreaterThan(4d), "It stayed over the threshold longer than GrowthDuration, but was draining.");
        }

        [Test]
        public void StaffedQueueThatKeepsGrowingWarnsAfterTheDurationAsFullSeats()
        {
            // One seat serving every 2 s against one arrival per second: steady growth.
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 2d);
            scenario.Waves.StartWave(new WaveDefinition(1, 30d, new[] { new NeedDemandDefinition(NeedType.Rooms, 30) }));
            var tracker = new WaveNeedStatusTracker();
            double? firstOver = null;
            NeedStatusSnapshot rooms = default;
            for (var step = 0; step < 120 && rooms.Status != NeedServiceStatus.BOTTLENECK; step++)
            {
                scenario.Waves.Advance(0.25d);
                rooms = tracker.Update(scenario.Waves.CaptureSnapshot(), scenario.Clock.CurrentTime).Needs.Single();
                if (!firstOver.HasValue && rooms.Waiting > 2) firstOver = scenario.Clock.CurrentTime;
            }

            Assert.That(rooms.Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK));
            Assert.That(rooms.Cause, Is.EqualTo(NeedQueueCause.ServiceCapacity));
            Assert.That(rooms.InService, Is.EqualTo(1), "The guest in the seat is not part of the queue.");
            Assert.That(scenario.Clock.CurrentTime - firstOver.Value, Is.InRange(4d, 5d), "Warns once 4 s of growth are confirmed.");
        }

        [TestCase(2f)]
        [TestCase(4f)]
        [TestCase(8f)]
        public void ProductionBeachBarWarnsOnlyAfterTheConfiguredGrowthDuration(float duration)
        {
            var settings = new WaveNeedStatusSettings { GrowthDuration = duration };
            var game = StartFirstWave();
            var run = RunUntilBeachBarWarns(game, new WaveNeedStatusTracker(settings));

            Assert.That(run.WarnedAt.HasValue, Is.True, "Wave 1 teaches the missing Worker on Beach Bar.");
            Assert.That(run.WarnedAt.Value - run.FirstOverThreshold.Value, Is.GreaterThanOrEqualTo(duration));
            Assert.That(run.WarnedAt.Value - run.FirstOverThreshold.Value, Is.LessThan(duration + 8d), "And not much later.");
        }

        [Test]
        public void PauseFreezesTheGrowthWindowAndResumeContinuesIt()
        {
            var reference = RunUntilBeachBarWarns(StartFirstWave(), new WaveNeedStatusTracker());
            Assert.That(reference.WarnedAt.HasValue, Is.True);
            var pauseAt = reference.FirstOverThreshold.Value + 1d;
            Assert.That(pauseAt, Is.LessThan(reference.WarnedAt.Value), "Pause while the window is open.");
            TearDown(); // Only one bootstrap may own the gameplay runtime at a time.

            var game = StartFirstWave();
            var tracker = new WaveNeedStatusTracker();
            while (game.SimulationTime < pauseAt)
            {
                game.AdvanceSimulation(SampleStep);
                tracker.Capture(game);
            }
            Assert.That(game.TryPauseWave().Succeeded, Is.True);
            var frozenAt = game.SimulationTime;
            var frozen = BeachBar(tracker.Capture(game));
            Assert.That(frozen.Status, Is.EqualTo(NeedServiceStatus.OK));
            Assert.That(frozen.Waiting, Is.GreaterThan(2), "The growth window is open.");

            // A long pause: S2 keeps sampling and every Advance call is ignored.
            for (var i = 0; i < 200; i++)
            {
                game.AdvanceSimulation(SampleStep);
                var paused = tracker.Capture(game);
                Assert.That(game.SimulationTime, Is.EqualTo(frozenAt));
                Assert.That(BeachBar(paused).Status, Is.EqualTo(NeedServiceStatus.OK), "No warning while paused.");
                Assert.That(BeachBar(paused).Waiting, Is.EqualTo(frozen.Waiting));
                Assert.That(paused.Feedback, Is.Empty);
            }

            Assert.That(game.TryResumeWave().Succeeded, Is.True);
            var resumed = RunUntilBeachBarWarns(game, tracker);
            Assert.That(resumed.WarnedAt.Value, Is.EqualTo(reference.WarnedAt.Value).Within(1e-9),
                "The pause neither advanced nor restarted the timer.");
        }

        [Test]
        public void MovingAWorkerToTheBottleneckRecoversItWithoutFlicker()
        {
            var game = StartFirstWave();
            var tracker = new WaveNeedStatusTracker();
            Assert.That(RunUntilBeachBarWarns(game, tracker).WarnedAt.HasValue, Is.True);

            Assert.That(game.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar).Succeeded, Is.True);
            var moved = tracker.Capture(game);
            Assert.That(BeachBar(moved).Status, Is.EqualTo(NeedServiceStatus.RECOVERING), "Feedback is immediate.");
            Assert.That(moved.Feedback.Select(f => (f.Kind, f.BuildingId)), Is.EqualTo(new[]
            {
                (NeedFeedbackKind.RecoveringAfterWorker, BuildingId.BeachBar),
                (NeedFeedbackKind.WorkerRemoved, BuildingId.Reception)
            }));

            var beachBar = new List<NeedServiceStatus> { NeedServiceStatus.RECOVERING };
            var minimumAfterMove = BeachBar(moved).Waiting;
            while (game.CurrentPhase != GamePhase.WAVE_COMPLETE)
            {
                game.AdvanceSimulation(SampleStep);
                var wave = tracker.Capture(game);
                var drinks = BeachBar(wave);
                if (drinks.Status != beachBar[beachBar.Count - 1]) beachBar.Add(drinks.Status);
                minimumAfterMove = Mathf.Min(minimumAfterMove, drinks.Waiting);
                var reception = wave.Needs.Single(n => n.BuildingId == BuildingId.Reception);
                if (reception.Status == NeedServiceStatus.BOTTLENECK)
                    Assert.That(reception.Cause, Is.EqualTo(NeedQueueCause.MissingWorker), "Reception gave its Worker away.");
            }

            Assert.That(beachBar, Is.EqualTo(new[] { NeedServiceStatus.RECOVERING, NeedServiceStatus.OK, NeedServiceStatus.SATISFIED }),
                "The staffed Beach Bar drains and never warns again.");
            Assert.That(minimumAfterMove, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void EachWaveStartsDetectionFromScratch()
        {
            var game = StartFirstWave();
            var tracker = new WaveNeedStatusTracker();
            Assert.That(RunUntilBeachBarWarns(game, tracker).WarnedAt.HasValue, Is.True);
            while (game.CurrentPhase != GamePhase.WAVE_COMPLETE)
            {
                game.AdvanceSimulation(SampleStep);
                tracker.Capture(game);
            }
            Assert.That(tracker.Capture(game).Needs.Select(n => n.Status), Is.All.EqualTo(NeedServiceStatus.SATISFIED));

            ClaimAndContinue(game);
            Assert.That(game.TryStartWave().Succeeded, Is.True);
            var first = tracker.Capture(game);
            Assert.That(first.WaveId, Is.EqualTo(2));
            Assert.That(first.Needs.Select(n => n.Status), Is.All.EqualTo(NeedServiceStatus.OK), "Nothing carries over from wave 1.");
            Assert.That(first.Feedback, Is.Empty);

            var wave2 = RunUntilBeachBarWarns(game, tracker);
            Assert.That(wave2.WarnedAt.HasValue, Is.True, "Beach Bar is still unstaffed in wave 2.");
            Assert.That(wave2.WarnedAt.Value - wave2.FirstOverThreshold.Value, Is.GreaterThanOrEqualTo(4d),
                "Wave 2 gets its own full growth window.");
        }

        [Test]
        public void AllEightProductionWavesWarnOnlyForRealQueuesAndNeverFlicker()
        {
            var settings = new WaveNeedStatusSettings();
            var game = CreateGame();
            var tracker = new WaveNeedStatusTracker(settings);
            var warnings = 0;
            for (var waveId = 1; waveId <= 8; waveId++)
            {
                foreach (var building in game.Buildings.Where(b => b.IsEligible && !b.IsOwned).ToList())
                    game.TryPurchaseBuilding(building.BuildingId);
                Assert.That(game.TryStartWave().Succeeded, Is.True, "wave " + waveId);

                var history = new Dictionary<NeedType, List<(double Time, NeedServiceStatus Status, int Waiting)>>();
                while (game.CurrentPhase != GamePhase.WAVE_COMPLETE)
                {
                    game.AdvanceSimulation(SampleStep);
                    foreach (var need in tracker.Capture(game).Needs)
                    {
                        if (!history.TryGetValue(need.NeedType, out var changes))
                            history.Add(need.NeedType, changes = new List<(double, NeedServiceStatus, int)> { (game.SimulationTime, need.Status, need.Waiting) });
                        else if (changes[changes.Count - 1].Status != need.Status)
                            changes.Add((game.SimulationTime, need.Status, need.Waiting));
                    }
                }

                foreach (var pair in history)
                {
                    var changes = pair.Value;
                    var trace = "wave " + waveId + " " + pair.Key + ": "
                        + string.Join(", ", changes.Select(c => c.Status + "@" + c.Time.ToString("0.0") + "(" + c.Waiting + ")"));
                    Assert.That(changes[changes.Count - 1].Status, Is.EqualTo(NeedServiceStatus.SATISFIED), trace);

                    double? lastWarning = null;
                    for (var i = 1; i < changes.Count; i++)
                    {
                        if (changes[i].Status != NeedServiceStatus.BOTTLENECK) continue;
                        warnings++;
                        Assert.That(changes[i].Waiting, Is.GreaterThan(settings.BottleneckQueueThreshold), trace);
                        if (lastWarning.HasValue)
                            Assert.That(changes[i].Time - lastWarning.Value, Is.GreaterThanOrEqualTo(settings.GrowthDuration), trace);
                        lastWarning = changes[i].Time;
                    }
                    Assert.That(changes.Count, Is.LessThanOrEqualTo(6), "No rapid switching. " + trace);
                }
                ClaimAndContinue(game);
            }
            Assert.That(warnings, Is.GreaterThan(0), "The unstaffed building is flagged in the scripted waves.");
        }

        private ResortGameplayBootstrap CreateGame()
        {
            var root = new GameObject("BottleneckDetectionTiming");
            roots.Add(root);
            var game = root.AddComponent<ResortGameplayBootstrap>();
            Assert.That(game.TryConfigureProgression(EconomyTestFixtures.Config(), new TestProgressionPersistence()), Is.True);
            return game;
        }

        private ResortGameplayBootstrap StartFirstWave()
        {
            var game = CreateGame();
            Assert.That(game.TryStartWave().Succeeded, Is.True);
            return game;
        }

        private static void ClaimAndContinue(ResortGameplayBootstrap game)
        {
            Assert.That(game.TryClaimWaveReward(game.CurrentWaveResult.RewardId).Succeeded, Is.True);
            Assert.That(game.TryContinueAfterResults().Succeeded, Is.True);
        }

        private static (double? FirstOverThreshold, double? WarnedAt) RunUntilBeachBarWarns(
            ResortGameplayBootstrap game, WaveNeedStatusTracker tracker)
        {
            double? firstOver = null;
            while (game.CurrentPhase != GamePhase.WAVE_COMPLETE)
            {
                game.AdvanceSimulation(SampleStep);
                var drinks = BeachBar(tracker.Capture(game));
                if (!firstOver.HasValue && drinks.Waiting > 2) firstOver = game.SimulationTime;
                if (drinks.Status == NeedServiceStatus.BOTTLENECK) return (firstOver, game.SimulationTime);
            }
            return (firstOver, null);
        }

        private static NeedStatusSnapshot BeachBar(WaveProgressSnapshot wave)
        {
            return wave.Needs.Single(n => n.BuildingId == BuildingId.BeachBar);
        }
    }
}
