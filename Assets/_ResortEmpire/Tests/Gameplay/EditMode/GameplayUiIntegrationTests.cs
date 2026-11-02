using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Waves;
using UnityEngine;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class WaveNeedStatusTrackerTests
    {
        [Test]
        public void OccupiedSeatsAloneNeverRaiseABottleneck()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 5, serviceDuration: 100d);
            scenario.Waves.StartWave(new WaveDefinition(1, 5d, new[] { new NeedDemandDefinition(NeedType.Drinks, 5) }));
            scenario.Waves.Advance(5d);
            var tracker = new WaveNeedStatusTracker();

            var drinks = tracker.Update(scenario.Waves.CaptureSnapshot()).Single();

            Assert.That(drinks.Waiting, Is.Zero);
            Assert.That(drinks.Status, Is.EqualTo(NeedServiceStatus.OK));
        }

        [Test]
        public void GrowingQueueIsBottleneckAndWorkerMoveShowsRecoveringImmediately()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 4d);
            scenario.Waves.StartWave(new WaveDefinition(1, 8d, new[] { new NeedDemandDefinition(NeedType.Drinks, 8) }));
            var tracker = new WaveNeedStatusTracker();
            scenario.Waves.Advance(4.5d);

            var before = tracker.Update(scenario.Waves.CaptureSnapshot()).Single();
            Assert.That(before.Waiting, Is.EqualTo(3));
            Assert.That(before.IsStaffed, Is.False);
            Assert.That(before.Status, Is.EqualTo(NeedServiceStatus.BOTTLENECK));

            Assert.That(scenario.Waves.TryReassignWorker(BuildingId.Restaurant, BuildingId.BeachBar), Is.True);
            var after = tracker.Update(scenario.Waves.CaptureSnapshot()).Single();
            Assert.That(after.Waiting, Is.EqualTo(3), "The queue has not drained yet.");
            Assert.That(after.Status, Is.EqualTo(NeedServiceStatus.RECOVERING));
        }

        [Test]
        public void EveryNeedIsSatisfiedOnceArrivalsCloseAndServiceFinishes()
        {
            var scenario = PhaseTwoTestData.CreateScenario(capacity: 1, serviceDuration: 1d);
            scenario.Waves.StartWave(PhaseTwoTestData.ThreeNeedWave());
            var tracker = new WaveNeedStatusTracker();
            for (var step = 0; step < 200 && scenario.State.CurrentState != GamePhase.WAVE_COMPLETE; step++)
            {
                scenario.Waves.Advance(0.25d);
                tracker.Update(scenario.Waves.CaptureSnapshot());
            }

            var final = tracker.Update(scenario.Waves.CaptureSnapshot());
            Assert.That(final, Has.Count.EqualTo(3));
            Assert.That(final.All(need => need.Status == NeedServiceStatus.SATISFIED), Is.True);
        }

        [Test]
        public void ThresholdsMustLeaveRoomForHysteresis()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WaveNeedStatusTracker(2, 2));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WaveNeedStatusTracker(3, -1));
        }
    }

    public sealed class GameplayUiNavigationTests
    {
        private GameObject root;
        private ResortGameplayBootstrap bootstrap;
        private ResortUiStateCoordinator coordinator;
        private GameObject needs;
        private GameObject results;
        private GameObject upgrade;
        private GameObject workers;
        private GameObject unlock;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("NavigationTest");
            bootstrap = root.AddComponent<ResortGameplayBootstrap>();
            Assert.That(bootstrap.TryConfigureProgression(EconomyTestFixtures.Config(coins: 1000), new TestProgressionPersistence()), Is.True);
            coordinator = root.AddComponent<ResortUiStateCoordinator>();
            needs = Window("WaveNeeds");
            results = Window("WaveComplete");
            upgrade = Window("UpgradeSuggestion");
            workers = Window("WorkerAllocation");
            unlock = Window("AreaUnlock");
            Set("gameplayBootstrap", bootstrap);
            Set("homeIslandView", Window("HomeIsland"));
            Set("waveNeedsPanel", needs);
            Set("waveCompleteWindow", results);
            Set("upgradeSuggestionWindow", upgrade);
            Set("workerAllocationWindow", workers);
            Set("areaUnlockWindow", unlock);
            // Edit Mode does not run OnEnable; bind exactly as the component does at runtime.
            typeof(ResortUiStateCoordinator).GetMethod("BindToGameplay", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(coordinator, null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        [Test]
        public void ForecastDescribesTheNextScriptedWaveWithoutStartingIt()
        {
            var forecast = bootstrap.NextWaveForecast;
            Assert.That(forecast.WaveId, Is.EqualTo(1));
            Assert.That(forecast.DemandTotal, Is.EqualTo(36));
            Assert.That(bootstrap.TotalWaves, Is.EqualTo(8));
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.AWAITING_WAVE));
        }

        [Test]
        public void OneLoopMovesThroughWaveNeedsResultsUpgradeAndBack()
        {
            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            Assert.That(needs.activeSelf, Is.True);
            Assert.That(coordinator.OpenWorkerAllocation(), Is.True);

            bootstrap.AdvanceSimulation(10000);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            Assert.That(needs.activeSelf, Is.False);
            Assert.That(workers.activeSelf, Is.False, "S3 closes once the wave is over.");
            Assert.That(results.activeSelf, Is.True);

            Assert.That(bootstrap.TryClaimWaveReward(bootstrap.CurrentWaveResult.RewardId).Succeeded, Is.True);
            Assert.That(results.activeSelf, Is.False);
            Assert.That(upgrade.activeSelf, Is.True);
            Assert.That(coordinator.OpenWorkerAllocation(), Is.False);

            Assert.That(bootstrap.TryContinueAfterResults().Succeeded, Is.True);
            Assert.That(upgrade.activeSelf, Is.False);
            Assert.That(bootstrap.CompletedWaves, Is.EqualTo(1));
            Assert.That(bootstrap.NextWaveForecast.WaveId, Is.EqualTo(2));
        }

        [Test]
        public void NewlyEligibleAreaIsAnnouncedOnceAndCanBeReopened()
        {
            for (var wave = 1; wave <= 3; wave++)
            {
                Assert.That(unlock.activeSelf, Is.False);
                Assert.That(coordinator.OpenAreaUnlock(), Is.False);
                Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
                bootstrap.AdvanceSimulation(10000);
                Assert.That(bootstrap.TryClaimWaveReward(bootstrap.CurrentWaveResult.RewardId).Succeeded, Is.True);
                Assert.That(bootstrap.TryContinueAfterResults().Succeeded, Is.True);
            }

            Assert.That(unlock.activeSelf, Is.True, "Pool becomes eligible after Wave 3.");
            unlock.SetActive(false);
            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            Assert.That(coordinator.OpenAreaUnlock(), Is.False, "No purchases during a wave.");
            bootstrap.AdvanceSimulation(10000);
            Assert.That(bootstrap.TryClaimWaveReward(bootstrap.CurrentWaveResult.RewardId).Succeeded, Is.True);
            Assert.That(bootstrap.TryContinueAfterResults().Succeeded, Is.True);
            Assert.That(unlock.activeSelf, Is.False, "Later is respected; the plot marker reopens S9.");
            Assert.That(coordinator.OpenAreaUnlock(), Is.True);
        }

        private GameObject Window(string name)
        {
            var window = new GameObject(name);
            window.transform.SetParent(root.transform);
            window.SetActive(false);
            return window;
        }

        private void Set(string field, object value)
        {
            var info = typeof(ResortUiStateCoordinator).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, field);
            info.SetValue(coordinator, value);
        }
    }
}
