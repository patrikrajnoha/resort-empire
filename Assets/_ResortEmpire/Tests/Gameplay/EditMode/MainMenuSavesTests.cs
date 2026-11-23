using System;
using System.IO;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class MainMenuSavesTests
    {
        private string root;
        private ProgressionConfig config;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "resort-main-menu-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            config = EconomyTestFixtures.Config(1000);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void WithoutSavesContinueIsUnavailableAndNewGameNeedsNoConfirmation()
        {
            var summary = MainMenuSaves.Read(root, config);

            Assert.That(summary.ContinueState, Is.EqualTo(MainMenuContinueState.NoSave));
            Assert.That(summary.CanContinue, Is.False);
            Assert.That(summary.Notice, Is.Null);
            Assert.That(summary.NewGameReplacesSave, Is.False);
            Assert.That(summary.NewGameSlot.SlotNumber, Is.EqualTo(1));
        }

        [Test]
        public void ContinueOpensTheMostRecentlySavedValidSlot()
        {
            SaveProgress(1, upgrade: false);
            SaveProgress(2, upgrade: true);
            SaveSlotStorage.SetActiveSlot(root, 1);
            File.SetLastWriteTimeUtc(SaveSlotStorage.GetSlotPath(root, 1), new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(SaveSlotStorage.GetSlotPath(root, 2), new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc));

            var summary = MainMenuSaves.Read(root, config);

            Assert.That(summary.CanContinue, Is.True);
            Assert.That(summary.ContinueSlot.SlotNumber, Is.EqualTo(2));
            Assert.That(summary.ContinueSlot.CurrentWave, Is.EqualTo(1));
            Assert.That(summary.Notice, Is.Null);
            Assert.That(summary.NewGameSlot.SlotNumber, Is.EqualTo(1), "New Game replaces the active slot, not the newest one.");
        }

        [Test]
        public void ANewerUnreadableSaveFallsBackToTheLastValidOneWithANotice()
        {
            SaveProgress(1, upgrade: true);
            File.WriteAllText(SaveSlotStorage.GetSlotPath(root, 2), "not json");
            File.SetLastWriteTimeUtc(SaveSlotStorage.GetSlotPath(root, 1), new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(SaveSlotStorage.GetSlotPath(root, 2), new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc));

            var summary = MainMenuSaves.Read(root, config);

            Assert.That(summary.CanContinue, Is.True);
            Assert.That(summary.ContinueSlot.SlotNumber, Is.EqualTo(1));
            Assert.That(summary.Notice, Does.Contain("latest save could not be loaded"));
        }

        [Test]
        public void OnlyUnreadableSavesDisableContinueAndKeepTheFile()
        {
            var slot = SaveSlotStorage.GetSlotPath(root, 1);
            Directory.CreateDirectory(Path.GetDirectoryName(slot));
            File.WriteAllText(slot, "not json");

            var summary = MainMenuSaves.Read(root, config);

            Assert.That(summary.ContinueState, Is.EqualTo(MainMenuContinueState.Unreadable));
            Assert.That(summary.CanContinue, Is.False);
            Assert.That(summary.Notice, Is.EqualTo(MainMenuSaves.UnreadableNotice));
            Assert.That(summary.NewGameReplacesSave, Is.True, "Replacing the unreadable save must be confirmed.");
            Assert.That(File.ReadAllText(slot), Is.EqualTo("not json"));
        }

        [Test]
        public void NewGameReplacesOnlyTheActiveSlotWithAFreshValidCheckpoint()
        {
            var oldProfile = SaveProgress(1, upgrade: true);
            SaveProgress(2, upgrade: true);
            var otherSlot = File.ReadAllText(SaveSlotStorage.GetSlotPath(root, 2));
            SaveSlotStorage.SetActiveSlot(root, 1);
            Assert.That(MainMenuSaves.Read(root, config).NewGameReplacesSave, Is.True);

            Assert.That(MainMenuSaves.TryCreateNewGame(root, 1, config, out var error), Is.True, error);

            var data = new JsonFileProgressionPersistence(SaveSlotStorage.GetSlotPath(root, 1)).Load();
            Assert.DoesNotThrow(() => ResortProgressionRuntime.ValidateSave(data, config));
            Assert.That(data.ProfileId, Is.Not.EqualTo(oldProfile));
            Assert.That(data.Coins, Is.EqualTo(1000));
            Assert.That(data.ClaimedWaves, Is.EqualTo(0));
            Assert.That(SaveSlotStorage.GetActiveSlot(root), Is.EqualTo(1));
            Assert.That(File.ReadAllText(SaveSlotStorage.GetSlotPath(root, 2)), Is.EqualTo(otherSlot));
            using (var loaded = new ResortProgressionRuntime(config, SaveSlotStorage.CreateActivePersistence(root)))
            {
                Assert.That(loaded.Resources.Coins, Is.EqualTo(1000));
                Assert.That(loaded.Buildings.States[(int)BuildingId.Reception].Level, Is.EqualTo(1));
            }
        }

        [Test]
        public void NewGameDoesNotBringBackAMigratedLegacySave()
        {
            using (var legacy = new ResortProgressionRuntime(config, new JsonFileProgressionPersistence(Path.Combine(root, "resort-progress-v1.json"))))
                Assert.That(legacy.TryUpgradeBuilding(BuildingId.Reception), Is.True);
            Assert.That(MainMenuSaves.Read(root, config).CanContinue, Is.True, "The legacy save migrates into slot 1.");

            Assert.That(MainMenuSaves.TryCreateNewGame(root, 1, config, out var error), Is.True, error);

            using (var loaded = new ResortProgressionRuntime(config, SaveSlotStorage.CreateActivePersistence(root)))
                Assert.That(loaded.Resources.Coins, Is.EqualTo(1000));
        }

        [Test]
        public void NewGameReplacesAnUnreadableSave()
        {
            var slot = SaveSlotStorage.GetSlotPath(root, 1);
            Directory.CreateDirectory(Path.GetDirectoryName(slot));
            File.WriteAllText(slot, "not json");

            Assert.That(MainMenuSaves.TryCreateNewGame(root, 1, config, out var error), Is.True, error);

            Assert.That(MainMenuSaves.Read(root, config).CanContinue, Is.True);
        }

        [Test]
        public void NewGameReportsAFailedWriteInsteadOfThrowing()
        {
            // A file where the slot folder should be makes every slot write fail.
            File.WriteAllText(Path.Combine(root, "save-slots"), "blocked");

            Assert.That(MainMenuSaves.TryCreateNewGame(root, 1, config, out var error), Is.False);
            Assert.That(error, Does.StartWith("A new game could not be created"));
        }

        [Test]
        public void ContinueShowsTheWaveThePlayerWillPlayNext()
        {
            var path = SaveSlotStorage.GetSlotPath(root, 1);
            using (var runtime = new ResortProgressionRuntime(config, new JsonFileProgressionPersistence(path)))
            {
                Assert.That(runtime.TryStartWave(), Is.True);
                runtime.Waves.Advance(10000);
                Assert.That(runtime.TryClaimWaveReward(runtime.CurrentWaveResult.RewardId), Is.True);
                Assert.That(runtime.TryContinueAfterResults(), Is.True);
            }

            var summary = MainMenuSaves.Read(root, config);

            Assert.That(summary.ContinueSlot.CurrentWave, Is.EqualTo(2));
            Assert.That(summary.AllWavesComplete, Is.False);
        }

        private string SaveProgress(int slot, bool upgrade)
        {
            using (var runtime = new ResortProgressionRuntime(config, SaveSlotStorage.CreatePersistence(root, slot)))
            {
                if (upgrade) Assert.That(runtime.TryUpgradeBuilding(BuildingId.Reception), Is.True);
            }
            return new JsonFileProgressionPersistence(SaveSlotStorage.GetSlotPath(root, slot)).Load().ProfileId;
        }
    }
}
