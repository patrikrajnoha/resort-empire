using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;
using ResortEmpire.Gameplay.Staff;
using ResortEmpire.Gameplay.Waves;
using UnityEngine;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class SuddenRushTests
    {
        private static WaveDefinition RushWave() => new WaveDefinition(2, 8,
            new[] { new NeedDemandDefinition(NeedType.Rooms, 1),
                new NeedDemandDefinition(NeedType.Food, 1), new NeedDemandDefinition(NeedType.Drinks, 1) },
            new[] { new ScheduledDemandEvent("rush", ScheduledDemandEventKind.SuddenRush, 4, 2, 4) });

        [Test]
        public void NoticeAppearsOnlyAtTriggerAndKeepsItsOriginalTargetAfterWorkerMoves()
        {
            var game = PhaseTwoTestData.CreateScenario();
            game.Waves.StartWave(RushWave());
            game.Waves.Advance(3);
            Assert.That(game.Waves.CaptureSuddenRush(), Is.Null);
            Assert.That(game.Waves.CaptureSnapshot().DemandTotal, Is.EqualTo(3));
            Assert.That(game.Waves.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.True);
            game.Waves.Advance(1);
            Assert.That(game.Waves.CaptureSuddenRush().BuildingId, Is.EqualTo(BuildingId.Reception));
            Assert.That(game.Waves.CaptureSnapshot().DemandTotal, Is.EqualTo(7));
            Assert.That(game.Waves.TryReassignWorker(BuildingId.Restaurant, BuildingId.Reception), Is.True);
            game.Waves.Advance(0);
            Assert.That(game.Waves.CaptureSuddenRush().BuildingId, Is.EqualTo(BuildingId.Reception));
            Assert.That(game.State.CurrentState, Is.EqualTo(GamePhase.WAVE_ACTIVE));
        }

        [Test]
        public void RushArrivesOverItsDurationAndPauseDoesNotDuplicateOrLoseDemand()
        {
            var game = PhaseTwoTestData.CreateScenario(serviceDuration: 100);
            game.Waves.StartWave(RushWave());
            game.Waves.Advance(4);
            Assert.That(game.Waves.CaptureSnapshot().DemandArrived, Is.Zero);
            game.Waves.Advance(0.5);
            Assert.That(game.Waves.CaptureSnapshot().DemandArrived, Is.EqualTo(1));
            game.Waves.TryPauseWave();
            game.Waves.Advance(100);
            Assert.That(game.Waves.CaptureSnapshot().DemandArrived, Is.EqualTo(1));
            game.Waves.TryResumeWave();
            game.Waves.Advance(1.5);
            Assert.That(game.Waves.CaptureSnapshot().DemandArrived, Is.EqualTo(4));
            Assert.That(game.Waves.CaptureSnapshot().WaitingQueueLength, Is.EqualTo(3));
            Assert.That(game.Waves.CaptureSnapshot().DemandTotal, Is.EqualTo(7));
            game.Waves.Advance(10000);
            Assert.That(game.Waves.CaptureSnapshot().CompletionRecords.Count, Is.EqualTo(7));
            Assert.That(game.State.CurrentState, Is.EqualTo(GamePhase.WAVE_COMPLETE));
        }

        [Test]
        public void IgnoreIsIdempotentAndHasNoEffectOnServiceOrRewards()
        {
            var ignored = PhaseTwoTestData.CreateScenario();
            var visible = PhaseTwoTestData.CreateScenario();
            ignored.Waves.StartWave(RushWave());
            visible.Waves.StartWave(RushWave());
            ignored.Waves.Advance(4);
            visible.Waves.Advance(4);
            Assert.That(ignored.Waves.TryDismissSuddenRush("other"), Is.False);
            Assert.That(ignored.Waves.TryDismissSuddenRush("rush"), Is.True);
            Assert.That(ignored.Waves.TryDismissSuddenRush("rush"), Is.False);
            ignored.Waves.TryPauseWave();
            ignored.Waves.Advance(500);
            ignored.Waves.TryResumeWave();
            Assert.That(ignored.Waves.CaptureSuddenRush().NotificationDismissed, Is.True);
            ignored.Waves.Advance(1000);
            visible.Waves.Advance(1000);
            var a = ignored.Waves.CaptureSnapshot();
            var b = visible.Waves.CaptureSnapshot();
            Assert.That(a.DemandTotal, Is.EqualTo(b.DemandTotal));
            CollectionAssert.AreEqual(b.CompletionRecords.Select(r => r.WaitingDuration),
                a.CompletionRecords.Select(r => r.WaitingDuration));
            var calculator = new WaveRewardCalculator(EconomyTestFixtures.Config());
            var rewardA = calculator.Calculate("a", 2, a.CompletionRecords);
            var rewardB = calculator.Calculate("b", 2, b.CompletionRecords);
            Assert.That(rewardA.ServedUnits, Is.EqualTo(7));
            Assert.That(rewardA.Satisfaction, Is.EqualTo(rewardB.Satisfaction));
            Assert.That(rewardA.Rewards.Coins, Is.EqualTo(rewardB.Rewards.Coins));
        }

        [Test]
        public void FullyStaffedOwnedBuildingsSkipRushInsteadOfTargetingLockedPlots()
        {
            var buildings = new TestBuildingProvider();
            var state = new GameState();
            // The production WorkerManager guarantees one vacancy; exercise the wave's
            // defensive no-candidate path through its provider boundary.
            var workers = new FullyStaffedProvider();
            var waves = new WaveManager(state, new SimulationClock(), buildings, workers, PhaseTwoTestData.Config());
            waves.StartWave(RushWave());
            waves.Advance(4);
            Assert.That(waves.CaptureSuddenRush(), Is.Null);
            Assert.That(waves.CaptureSnapshot().DemandTotal, Is.EqualTo(3));
            waves.Advance(100);
            Assert.That(state.CurrentState, Is.EqualTo(GamePhase.WAVE_COMPLETE));
        }

        private sealed class FullyStaffedProvider : IWorkerAssignmentProvider
        {
            public System.Collections.Generic.IReadOnlyList<WorkerAssignmentSnapshot> Assignments { get; } = new[]
            {
                new WorkerAssignmentSnapshot(1, BuildingId.Reception),
                new WorkerAssignmentSnapshot(2, BuildingId.Restaurant),
                new WorkerAssignmentSnapshot(3, BuildingId.BeachBar)
            };
            public int WorkerCount => 3;
            public int GetAssignedWorkerCount(BuildingId id) => (int)id < 3 ? 1 : 0;
            public bool TryReassignWorker(BuildingId source, BuildingId destination) => false;
        }

        [Test]
        public void CheckpointRestartCreatesOneFreshRushAndPreservesRewardDemand()
        {
            var disk = new TestProgressionPersistence();
            var config = EconomyTestFixtures.Config();
            using (var game = new ResortProgressionRuntime(config, disk))
            {
                EconomyTestFixtures.FinishWaves(game, 1);
                game.TryStartWave();
                game.Waves.Advance(config.IncomingDuration + config.RushStart + 1);
                game.Waves.TryDismissSuddenRush("sudden-rush");
            }
            using (var restored = new ResortProgressionRuntime(config, disk))
            {
                Assert.That(restored.GetNextWave().WaveId, Is.EqualTo(2));
                Assert.That(restored.Waves.CaptureSuddenRush(), Is.Null);
                EconomyTestFixtures.Complete(restored);
                Assert.That(restored.Waves.CaptureSnapshot().DemandTotal, Is.EqualTo(51));
                Assert.That(restored.CurrentWaveResult.ServedUnits, Is.EqualTo(51));
                Assert.That(restored.CurrentWaveResult.Waiting.Single(w => w.BuildingId == BuildingId.BeachBar).ServedUnits,
                    Is.EqualTo(21));
            }
        }
    }

    public sealed class SuddenRushIntegrationTests
    {
        private GameObject root;
        private ResortGameplayBootstrap gameplay;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("SuddenRushTest");
            gameplay = root.AddComponent<ResortGameplayBootstrap>();
            var config = EconomyTestFixtures.Config();
            config.EnergyRewards = new int[5];
            Assert.That(gameplay.TryConfigureProgression(config, new TestProgressionPersistence()), Is.True);
            gameplay.TryStartWave();
            gameplay.AdvanceSimulation(10000);
            gameplay.TryClaimWaveReward(gameplay.CurrentWaveResult.RewardId);
            gameplay.TryContinueAfterResults();
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

        private void StartRush()
        {
            Assert.That(gameplay.TryStartWave().Succeeded, Is.True);
            // Step into the rush window; accumulated double time can be one ULP below 45 at 47.
            gameplay.AdvanceSimulation(48);
            Assert.That(gameplay.CurrentSuddenRush.IsVisible, Is.True);
        }

        [Test]
        public void ForecastHidesRushAndS2TotalsIncludeItWhenItStarts()
        {
            Assert.That(gameplay.NextWaveForecast.WaveId, Is.EqualTo(2));
            Assert.That(gameplay.NextWaveForecast.DemandTotal, Is.EqualTo(45));
            Assert.That(gameplay.CurrentSuddenRush.IsVisible, Is.False);
            StartRush();
            var view = new WaveNeedStatusTracker().Capture(gameplay);
            Assert.That(view.Total, Is.EqualTo(51));
            Assert.That(view.Needs.Single(n => n.BuildingId == BuildingId.BeachBar).Total, Is.EqualTo(21));
        }

        [Test]
        public void ApplicationBackgroundResumeKeepsNoticeDismissedAndDemandExactlyOnce()
        {
            StartRush();
            gameplay.TryDismissSuddenRush(gameplay.CurrentSuddenRush.EventId);
            var pause = typeof(ResortGameplayBootstrap).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic);
            pause.Invoke(gameplay, new object[] { true });
            var time = gameplay.SimulationTime;
            gameplay.AdvanceSimulation(100);
            Assert.That(gameplay.SimulationTime, Is.EqualTo(time));
            pause.Invoke(gameplay, new object[] { false });
            Assert.That(gameplay.CurrentSuddenRush.IsVisible, Is.False);
            gameplay.AdvanceSimulation(10000);
            Assert.That(gameplay.CurrentWaveResult.ServedUnits, Is.EqualTo(51));
            Assert.That(gameplay.Resources.Energy, Is.Zero);
        }

        [Test]
        public void WorkerNavigationKeepsAffectedBuildingContextWithoutPausing()
        {
            StartRush();
            var target = gameplay.CurrentSuddenRush.BuildingId;
            var coordinator = root.AddComponent<ResortUiStateCoordinator>();
            var window = new GameObject("Workers");
            window.transform.SetParent(root.transform);
            window.SetActive(false);
            SetField(coordinator, "gameplayBootstrap", gameplay);
            SetField(coordinator, "workerAllocationWindow", window);
            typeof(ResortUiStateCoordinator).GetMethod("BindToGameplay", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(coordinator, null);
            Assert.That(coordinator.OpenWorkerAllocation(target), Is.True);
            Assert.That(coordinator.WorkerAllocationTarget, Is.EqualTo(target));
            Assert.That(window.activeSelf, Is.True);
            Assert.That(gameplay.CurrentPhase, Is.EqualTo(GamePhase.WAVE_ACTIVE));
            Assert.That(gameplay.TryReassignWorker(BuildingId.Reception, target).Succeeded, Is.True);
            Assert.That(coordinator.WorkerAllocationTarget, Is.EqualTo(target));
            Assert.That(coordinator.OpenWorkerAllocation(BuildingId.Pool), Is.False);
            coordinator.OpenWorkerAllocation();
            Assert.That(coordinator.WorkerAllocationTarget, Is.Null);
        }

        [Test]
        public void InsufficientEnergyLeavesRushVisibleAndWaveCompletes()
        {
            StartRush();
            var target = gameplay.CurrentSuddenRush.BuildingId;
            Assert.That(gameplay.TryUseEnergyBoost(target).Status, Is.EqualTo(ResortGameplayCommandStatus.Rejected));
            Assert.That(gameplay.Resources.Energy, Is.Zero);
            Assert.That(gameplay.CurrentSuddenRush.IsVisible, Is.True);
            gameplay.AdvanceSimulation(10000);
            Assert.That(gameplay.CurrentPhase, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            Assert.That(gameplay.CurrentWaveResult.ServedUnits, Is.EqualTo(51));
        }

        [Test]
        public void BoostAdapterHonorsProviderAvailabilityAndClickTimeRejection()
        {
            StartRush();
            var boost = root.AddComponent<RushTestBoostService>();
            SetField(gameplay, "energyBoostBehaviour", boost);
            var target = gameplay.CurrentSuddenRush.BuildingId;
            boost.Availability = ResortGameplayCommandResult.Rejected("Not enough Energy.");
            Assert.That(gameplay.TryUseEnergyBoost(target).Message, Is.EqualTo("Not enough Energy."));
            Assert.That(boost.Calls, Is.Zero);
            boost.Availability = ResortGameplayCommandResult.Success();
            boost.UseResult = ResortGameplayCommandResult.Rejected("Boost already active.");
            Assert.That(gameplay.TryUseEnergyBoost(target).Message, Is.EqualTo("Boost already active."));
            Assert.That(gameplay.CurrentSuddenRush.IsVisible, Is.True);
            boost.UseResult = ResortGameplayCommandResult.Success();
            Assert.That(gameplay.TryUseEnergyBoost(target).Succeeded, Is.True);
            Assert.That(boost.LastTarget, Is.EqualTo(target));
            gameplay.TryPauseWave();
            Assert.That(gameplay.TryUseEnergyBoost(target).Succeeded, Is.False);
            Assert.That(boost.Calls, Is.EqualTo(2));
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    // Test double for the adapter contract, not an Energy boost implementation.
    public sealed class RushTestBoostService : MonoBehaviour, IEnergyBoostService
    {
        public ResortGameplayCommandResult Availability;
        public ResortGameplayCommandResult UseResult;
        public int Calls;
        public BuildingId LastTarget;
        public ResortGameplayCommandResult GetAvailability(BuildingId buildingId) => Availability;
        public ResortGameplayCommandResult TryUseBoost(BuildingId buildingId)
        {
            Calls++;
            LastTarget = buildingId;
            return UseResult;
        }
    }
}
