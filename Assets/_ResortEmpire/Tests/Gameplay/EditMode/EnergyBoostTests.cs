using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;
using ResortEmpire.Gameplay.Waves;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class EnergyBoostTests
    {
        [Test]
        public void BoostAcceleratesOccupiedSeatAndExpiresWithoutResettingProgress()
        {
            var config = EconomyTestFixtures.Config();
            config.InitialEnergy = 6;
            config.EnergyBoostDuration = 3d;
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(config, disk))
            {
                Assert.That(game.TryStartWave(), Is.True);
                game.Waves.Advance(12.5d); // First Rooms arrival at 9.5 clock seconds, with 3 seconds of work done.
                var before = game.Waves.CaptureActiveWave().Units.Single(u => u.Need == NeedType.Rooms);
                Assert.That(before.Progress, Is.EqualTo(0.25d).Within(1e-10));
                Assert.That(game.TryUseEnergyBoost(BuildingId.Reception), Is.True, game.LastCommandError);
                Assert.That(game.Buildings.States.Single(b => b.BuildingId == BuildingId.Reception).Capacity,
                    Is.EqualTo(config.Capacities[0]));
                using var singleStep = new ResortProgressionRuntime(config, new TestProgressionPersistence { Data = disk.Load() });
                singleStep.Waves.Advance(7d); // One advance crosses boost expiry and seat completion.
                var singleStepRecord = singleStep.Waves.CaptureSnapshot().CompletionRecords.Single(record => record.NeedUnitId == before.Id);
                Assert.That(singleStepRecord.ServiceCompletionTime, Is.EqualTo(18.5d).Within(1e-9));

                game.Waves.Advance(2.5d);
                Assert.That(game.Waves.ServiceBoostRemaining(BuildingId.Reception), Is.EqualTo(0.5d).Within(1e-9));
                game.Waves.Advance(0.5d);
                Assert.That(game.Waves.ServiceBoostRemaining(BuildingId.Reception), Is.Zero);
                var atExpiry = game.Waves.CaptureActiveWave().Units.Single(unit => unit.Id == before.Id);
                Assert.That(atExpiry.State, Is.EqualTo(NeedUnitState.IN_SERVICE));
                Assert.That(atExpiry.Progress, Is.EqualTo(0.75d).Within(1e-9));
                game.Waves.Advance(1d);
                var afterExpiry = game.Waves.CaptureActiveWave().Units.Single(unit => unit.Id == before.Id);
                Assert.That(afterExpiry.Progress, Is.EqualTo(0.75d + 1d / 12d).Within(1e-9));
                game.Waves.Advance(3d);
                var completed = game.Waves.CaptureSnapshot().CompletionRecords.First(r => r.NeedUnitId == before.Id);
                Assert.That(completed.ServiceCompletionTime - completed.ServiceStartTime, Is.EqualTo(9d).Within(1e-9));
                Assert.That(game.Waves.HasUsedServiceBoost(BuildingId.Reception), Is.True);
                Assert.That(game.TryUseEnergyBoost(BuildingId.Reception), Is.False);
                Assert.That(game.Resources.Energy, Is.EqualTo(3));
            }
        }

        [Test]
        public void DrainBoostCombinesWithWorkerAndOtherBuildingsHaveIndependentLimits()
        {
            var config = EconomyTestFixtures.Config();
            config.InitialEnergy = 7;
            config.EnergyBoostCost = 2;
            using (var game = new ResortProgressionRuntime(config, new TestProgressionPersistence()))
            {
                Assert.That(game.TryUseEnergyBoost(BuildingId.Reception), Is.False);
                Assert.That(game.TryStartWave(), Is.True);
                game.Waves.Advance(92.1d);
                Assert.That(game.Waves.ArrivalsClosed, Is.True);
                Assert.That(game.TryUseEnergyBoost(BuildingId.Pool), Is.False);
                Assert.That(game.TryUseEnergyBoost(BuildingId.BeachBar), Is.True, game.LastCommandError);
                Assert.That(game.TryUseEnergyBoost(BuildingId.Restaurant), Is.True, game.LastCommandError);
                Assert.That(game.Resources.Energy, Is.EqualTo(3));
                var before = game.Waves.CaptureActiveWave().Units
                    .First(u => u.Need == NeedType.Drinks && u.State == NeedUnitState.IN_SERVICE && u.Progress < 0.75d);
                Assert.That(game.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.True);
                game.Waves.Advance(1d);
                var after = game.Waves.CaptureActiveWave().Units.Single(u => u.Id == before.Id);
                Assert.That(after.Progress - before.Progress, Is.EqualTo(2d / 12d).Within(1e-10));
                game.Waves.Advance(1000d);
                EconomyTestFixtures.ClaimAndContinue(game);
                Assert.That(game.TryStartWave(), Is.True);
                game.Waves.Advance(3d);
                Assert.That(game.Waves.HasUsedServiceBoost(BuildingId.BeachBar), Is.False);
                Assert.That(game.Waves.HasUsedServiceBoost(BuildingId.Restaurant), Is.False);
                Assert.That(game.Waves.ServiceBoostRemaining(BuildingId.BeachBar), Is.Zero);
                var energy = game.Resources.Energy;
                Assert.That(game.TryUseEnergyBoost(BuildingId.BeachBar), Is.True, game.LastCommandError);
                Assert.That(game.Resources.Energy, Is.EqualTo(energy - 2));
                Assert.That(game.Waves.ServiceBoostRemaining(BuildingId.BeachBar), Is.EqualTo(config.EnergyBoostDuration));
            }
        }

        [Test]
        public void FailedCheckpointAndRepeatedInputDoNotSpendTwice()
        {
            var config = EconomyTestFixtures.Config();
            config.InitialEnergy = 3;
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(config, disk))
            {
                game.TryStartWave();
                game.Waves.Advance(3d);
                disk.FailWrites = true;
                Assert.That(game.TryUseEnergyBoost(BuildingId.Reception), Is.False);
                Assert.That(game.LastFailureWasPersistence, Is.True);
                Assert.That(game.Resources.Energy, Is.EqualTo(3));
                Assert.That(game.Waves.HasUsedServiceBoost(BuildingId.Reception), Is.False);
                Assert.That(disk.Data.ActiveWave, Is.Null);
                disk.FailWrites = false;
                Assert.That(game.TryUseEnergyBoost(BuildingId.Reception), Is.True);
                Assert.That(game.TryUseEnergyBoost(BuildingId.Reception), Is.False);
                Assert.That(game.TryUseEnergyBoost(BuildingId.Restaurant), Is.False);
                Assert.That(game.Resources.Energy, Is.Zero);
                Assert.That(disk.Data.Energy, Is.Zero);
                Assert.That(disk.Data.ActiveWave.Boosts, Has.Length.EqualTo(1));
            }
        }

        [Test]
        public void GameplayBootstrapWaitsForExplicitResumeOfSavedWave()
        {
            var config = EconomyTestFixtures.Config();
            config.InitialEnergy = 6;
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(config, disk))
            {
                Assert.That(game.TryStartWave(), Is.True);
                game.Waves.Advance(12.5d);
                Assert.That(game.TryUseEnergyBoost(BuildingId.Reception), Is.True);
            }

            var root = new UnityEngine.GameObject("CheckpointResumeTest");
            try
            {
                var bootstrap = root.AddComponent<ResortGameplayBootstrap>();
                Assert.That(bootstrap.TryConfigureProgression(config, disk), Is.True);
                Assert.That(bootstrap.IsAwaitingWaveResume, Is.True);
                Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_PAUSED));
                var savedTime = bootstrap.SimulationTime;
                bootstrap.AdvanceSimulation(2d);
                Assert.That(bootstrap.SimulationTime, Is.EqualTo(savedTime));
                Assert.That(bootstrap.TrySaveProgress().Succeeded, Is.True);
                Assert.That(disk.Data.ActiveWave.WaveId, Is.EqualTo(1));

                Assert.That(bootstrap.TryResumeWave().Succeeded, Is.True);
                Assert.That(bootstrap.IsAwaitingWaveResume, Is.False);
                Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_ACTIVE));
                bootstrap.AdvanceSimulation(2d);
                Assert.That(bootstrap.SimulationTime, Is.EqualTo(savedTime + 2d));
                Assert.That(bootstrap.TryPauseWave().Succeeded, Is.True);
                Assert.That(bootstrap.IsAwaitingWaveResume, Is.False);

                Assert.That(bootstrap.TryReloadProgression(new TestProgressionPersistence()), Is.True);
                Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.AWAITING_WAVE));
                Assert.That(bootstrap.IsAwaitingWaveResume, Is.False);
                Assert.That(bootstrap.NextWaveForecast.HasWave, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void JsonReloadRestoresRemainingTimeSeatsRushAndSpentAllowance()
        {
            var path = Path.Combine(Path.GetTempPath(), "resort-boost-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var disk = new JsonFileProgressionPersistence(path);
                var config = EconomyTestFixtures.Config();
                config.InitialEnergy = 8;
                config.EnergyRewards = new int[5];
                ActiveWaveCheckpoint saved;
                using (var game = new ResortProgressionRuntime(config, disk))
                {
                    StartWaveTwoWithRushBoost(game);
                    Assert.That(disk.Load().ActiveWave.ScheduledArrivals.Single().NotificationDismissed, Is.True);
                    game.Waves.Advance(4.25d);
                    Assert.That(game.Waves.TryPauseWave(), Is.True);
                    game.Waves.Advance(100d);
                    Assert.That(game.Waves.ServiceBoostRemaining(BuildingId.BeachBar), Is.EqualTo(10.75d).Within(1e-9));
                    Assert.That(game.TrySaveProgress(), Is.True);
                    saved = game.Waves.CaptureActiveWave();
                    Assert.That(saved.ScheduledArrivals.Single().Emitted, Is.EqualTo(3));
                }
                using (var restored = new ResortProgressionRuntime(config, disk))
                {
                    Assert.That(restored.State.CurrentState, Is.EqualTo(GamePhase.WAVE_ACTIVE));
                    Assert.That(restored.Resources.Energy, Is.EqualTo(5));
                    Assert.That(restored.Waves.ServiceBoostRemaining(BuildingId.BeachBar), Is.EqualTo(10.75d).Within(1e-9));
                    Assert.That(restored.Waves.CaptureSuddenRush().NotificationDismissed, Is.True);
                    var resumed = restored.Waves.CaptureActiveWave();
                    Assert.That(resumed.ClockTime, Is.EqualTo(saved.ClockTime).Within(1e-9));
                    Assert.That(resumed.ScheduledArrivals.Single().Emitted, Is.EqualTo(3));
                    var units = resumed.Units;
                    Assert.That(units.Select(unit => unit.Id), Is.EqualTo(saved.Units.Select(unit => unit.Id)));
                    for (var i = 0; i < units.Length; i++)
                    {
                        Assert.That(units[i].Need, Is.EqualTo(saved.Units[i].Need));
                        Assert.That(units[i].State, Is.EqualTo(saved.Units[i].State));
                        Assert.That(units[i].Arrival, Is.EqualTo(saved.Units[i].Arrival).Within(1e-10));
                        Assert.That(units[i].ServiceStart, Is.EqualTo(saved.Units[i].ServiceStart).Within(1e-10));
                        Assert.That(units[i].ServiceEnd, Is.EqualTo(saved.Units[i].ServiceEnd).Within(1e-10));
                        Assert.That(units[i].Progress, Is.EqualTo(saved.Units[i].Progress).Within(1e-10));
                    }
                    Assert.That(restored.TryUseEnergyBoost(BuildingId.BeachBar), Is.False);
                    using var uninterrupted = new ResortProgressionRuntime(config, new TestProgressionPersistence());
                    StartWaveTwoWithRushBoost(uninterrupted);
                    uninterrupted.Waves.Advance(4.25d);
                    restored.Waves.Advance(10.75d);
                    uninterrupted.Waves.Advance(10.75d);
                    Assert.That(restored.Waves.ServiceBoostRemaining(BuildingId.BeachBar), Is.Zero);
                    Assert.That(restored.Waves.CaptureActiveWave().ScheduledArrivals.Single().Emitted, Is.EqualTo(6));
                    Assert.That(restored.Waves.CaptureSnapshot().DemandArrived,
                        Is.EqualTo(uninterrupted.Waves.CaptureSnapshot().DemandArrived));

                    restored.Waves.Advance(1000d);
                    uninterrupted.Waves.Advance(1000d);
                    var actual = restored.Waves.CaptureSnapshot().CompletionRecords;
                    var expected = uninterrupted.Waves.CaptureSnapshot().CompletionRecords;
                    Assert.That(actual, Has.Count.EqualTo(51));
                    Assert.That(actual.Select(record => (record.NeedUnitId, record.NeedType)),
                        Is.EqualTo(expected.Select(record => (record.NeedUnitId, record.NeedType))));
                    for (var index = 0; index < actual.Count; index++)
                    {
                        Assert.That(actual[index].ArrivalTime, Is.EqualTo(expected[index].ArrivalTime).Within(1e-9));
                        Assert.That(actual[index].ServiceStartTime, Is.EqualTo(expected[index].ServiceStartTime).Within(1e-9));
                        Assert.That(actual[index].ServiceCompletionTime, Is.EqualTo(expected[index].ServiceCompletionTime).Within(1e-9));
                    }
                    Assert.That(restored.CurrentWaveResult.ServedUnits, Is.EqualTo(51));
                    Assert.That(restored.CurrentWaveResult.Satisfaction, Is.EqualTo(uninterrupted.CurrentWaveResult.Satisfaction).Within(1e-9));
                    Assert.That(restored.CurrentWaveResult.Rewards, Is.EqualTo(uninterrupted.CurrentWaveResult.Rewards));
                    Assert.That(restored.CurrentWaveResult.Stars, Is.EqualTo(uninterrupted.CurrentWaveResult.Stars));
                    Assert.That(disk.Load().ActiveWave, Is.Null);
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void StartWaveTwoWithRushBoost(ResortProgressionRuntime game)
        {
            EconomyTestFixtures.FinishWaves(game, 1);
            Assert.That(game.TryStartWave(), Is.True);
            game.Waves.Advance(48d);
            Assert.That(game.TryUseEnergyBoost(BuildingId.BeachBar, "sudden-rush"), Is.True, game.LastCommandError);
        }
    }
}
