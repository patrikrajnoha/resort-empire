using NUnit.Framework;
using ResortEmpire.Gameplay.Progression;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class DebugResetTests
    {
        [Test]
        public void DebugLevelsUpdateCapacityForOwnedBuildingsWithoutSpendingOrSaving()
        {
            var disk = new TestProgressionPersistence();
            var config = EconomyTestFixtures.Config(1000);
            using var game = new ResortProgressionRuntime(config, disk);
            Assert.That(game.TryDebugSetBuildingLevel(null, 3), Is.True);
            Assert.That(game.Buildings.States[0].Capacity, Is.EqualTo(config.Capacities[2]));
            Assert.That(game.Buildings.States[(int)BuildingId.Pool].IsOwned, Is.False);
            Assert.That(game.Resources.Coins, Is.EqualTo(1000));
            Assert.That(disk.Data.Buildings[0].Level, Is.EqualTo(1));
            Assert.That(game.TryDebugSetBuildingLevel(BuildingId.Pool, 2), Is.False);
            Assert.That(game.TryDebugSetBuildingLevel(null, 4), Is.False);
            Assert.That(game.TryDebugSetBuildingLevel(BuildingId.Reception, 1), Is.True);
            Assert.That(game.TryDebugSetBuildingLevel(null, 2), Is.True);
            Assert.That(game.TryStartWave(), Is.True);
            Assert.That(disk.Data.Buildings[0].Level, Is.EqualTo(2));
            Assert.That(game.TryDebugSetBuildingLevel(null, 1), Is.False);
        }

        [Test]
        public void DebugUnlockAndWorkerResetRespectProgressionAndOwnership()
        {
            var disk = new TestProgressionPersistence();
            using var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk);
            Assert.That(game.TryDebugUnlockAvailableBuildings(), Is.False);
            EconomyTestFixtures.FinishWaves(game, 3);
            var coins = game.Resources.Coins;
            Assert.That(game.TryDebugUnlockAvailableBuildings(), Is.True);
            Assert.That(game.TryDebugUnlockAvailableBuildings(), Is.False);
            Assert.That(game.Buildings.States[(int)BuildingId.Pool].IsOwned, Is.True);
            Assert.That(game.Buildings.States[(int)BuildingId.Bungalows].IsOwned, Is.False);
            Assert.That(disk.Data.Buildings[(int)BuildingId.Pool].Level, Is.Zero);
            Assert.That(game.Resources.Coins, Is.EqualTo(coins));
            Assert.That(game.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.True);
            Assert.That(game.TryDebugResetWorkers(), Is.True);
            Assert.That(game.Workers.Assignments.Count, Is.EqualTo(3));
            Assert.That(game.Workers.Assignments[0].BuildingId, Is.EqualTo(BuildingId.Reception));
            Assert.That(game.Workers.GetAssignedWorkerCount(BuildingId.Pool), Is.EqualTo(1));
        }

        [Test]
        public void ResetWritesFreshProgressOnlyToItsOwnPersistence()
        {
            var first = new TestProgressionPersistence();
            var second = new TestProgressionPersistence();
            var config = EconomyTestFixtures.Config(1000);
            using (var game = new ResortProgressionRuntime(config, first))
            using (var other = new ResortProgressionRuntime(config, second))
            {
                Assert.That(game.TryUpgradeBuilding(BuildingId.Reception), Is.True);
                var otherId = second.Data.ProfileId;
                var previousId = first.Data.ProfileId;
                Assert.That(game.TryPrepareNewGame(), Is.True, game.LastCommandError);
                Assert.That(first.Data.ProfileId, Is.Not.EqualTo(previousId));
                Assert.That(first.Data.Coins, Is.EqualTo(1000));
                Assert.That(first.Data.Buildings[0].Level, Is.EqualTo(1));
                Assert.That(second.Data.ProfileId, Is.EqualTo(otherId));
            }
        }

        [Test]
        public void FailedResetPreservesExistingCheckpoint()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(1000), disk))
            {
                Assert.That(game.TryUpgradeBuilding(BuildingId.Reception), Is.True);
                var id = disk.Data.ProfileId;
                disk.FailWrites = true;
                Assert.That(game.TryPrepareNewGame(), Is.False);
                Assert.That(disk.Data.ProfileId, Is.EqualTo(id));
                Assert.That(disk.Data.Buildings[0].Level, Is.EqualTo(2));
            }
        }
    }
}
