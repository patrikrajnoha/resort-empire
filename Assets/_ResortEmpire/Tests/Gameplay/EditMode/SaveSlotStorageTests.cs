using System;
using System.IO;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class SaveSlotStorageTests
    {
        private string root;
        private ProgressionConfig config;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "resort-save-slots-" + Guid.NewGuid().ToString("N"));
            config = EconomyTestFixtures.Config(1000);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void SlotsRemainIndependentWhenSavingAndLoading()
        {
            SaveSlotStorage.SetActiveSlot(root, 1);
            using (var slotOne = new ResortProgressionRuntime(config, SaveSlotStorage.CreatePersistence(root, 1)))
                Assert.That(slotOne.TryUpgradeBuilding(BuildingId.Reception), Is.True);

            SaveSlotStorage.SetActiveSlot(root, 2);
            using (var slotTwo = new ResortProgressionRuntime(config, SaveSlotStorage.CreatePersistence(root, 2)))
                Assert.That(slotTwo.Resources.Coins, Is.EqualTo(1000));

            using (var restoredOne = new ResortProgressionRuntime(config, SaveSlotStorage.CreatePersistence(root, 1)))
                Assert.That(restoredOne.Resources.Coins, Is.EqualTo(800));
            using (var restoredTwo = new ResortProgressionRuntime(config, SaveSlotStorage.CreatePersistence(root, 2)))
                Assert.That(restoredTwo.Resources.Coins, Is.EqualTo(1000));

            Assert.That(SaveSlotStorage.GetMetadata(root, 1, config).Status, Is.EqualTo(SaveSlotStatus.Occupied));
            Assert.That(SaveSlotStorage.GetMetadata(root, 2, config).Status, Is.EqualTo(SaveSlotStatus.Occupied));
        }

        [Test]
        public void CorruptionIsReportedWithoutChangingOtherSlots()
        {
            using (new ResortProgressionRuntime(config, SaveSlotStorage.CreatePersistence(root, 1))) { }
            File.WriteAllText(SaveSlotStorage.GetSlotPath(root, 2), "not json");

            Assert.That(SaveSlotStorage.GetMetadata(root, 1, config).Status, Is.EqualTo(SaveSlotStatus.Occupied));
            Assert.That(SaveSlotStorage.GetMetadata(root, 2, config).Status, Is.EqualTo(SaveSlotStatus.Corrupted));
            Assert.That(File.Exists(SaveSlotStorage.GetSlotPath(root, 2)), Is.True);
        }

        [Test]
        public void ActiveSlotCannotBeDeletedByTheStorageLayer()
        {
            using (new ResortProgressionRuntime(config, SaveSlotStorage.CreatePersistence(root, 1))) { }
            SaveSlotStorage.SetActiveSlot(root, 1);

            Assert.That(SaveSlotStorage.TryDeleteInactiveSlot(root, 1), Is.False);
            Assert.That(SaveSlotStorage.GetMetadata(root, 1, config).Status, Is.EqualTo(SaveSlotStatus.Occupied));
        }
    }
}
