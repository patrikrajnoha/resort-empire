using System;
using System.IO;
using ResortEmpire.Gameplay.Progression;

namespace ResortEmpire.Gameplay.Integration
{
    public enum MainMenuContinueState
    {
        /// <summary>No save exists yet; Continue is disabled.</summary>
        NoSave,
        /// <summary>A valid save exists; Continue opens it.</summary>
        Available,
        /// <summary>Only unreadable saves exist; Continue is disabled and the files are kept.</summary>
        Unreadable
    }

    /// <summary>What the Main Menu offers for the saves currently on disk.</summary>
    public sealed class MainMenuSaveSummary
    {
        internal MainMenuSaveSummary(MainMenuContinueState state, SaveSlotMetadata continueSlot, SaveSlotMetadata newGameSlot,
            int totalWaves, string notice)
        {
            ContinueState = state;
            ContinueSlot = continueSlot;
            NewGameSlot = newGameSlot;
            TotalWaves = totalWaves;
            Notice = notice;
        }

        public MainMenuContinueState ContinueState { get; }
        public bool CanContinue => ContinueState == MainMenuContinueState.Available;
        /// <summary>The most recently saved valid slot. Only meaningful when <see cref="CanContinue"/> is true.</summary>
        public SaveSlotMetadata ContinueSlot { get; }
        /// <summary>The slot New Game writes to: the active slot.</summary>
        public SaveSlotMetadata NewGameSlot { get; }
        public bool NewGameReplacesSave => NewGameSlot.Status != SaveSlotStatus.Empty;
        public int TotalWaves { get; }
        /// <summary>A warning to show the player, or null.</summary>
        public string Notice { get; }
        public bool AllWavesComplete => CanContinue && ContinueSlot.CurrentWave > TotalWaves;
    }

    /// <summary>
    /// Main Menu rules on top of <see cref="SaveSlotStorage"/>. Continue opens the most recently saved valid
    /// slot; New Game replaces the active slot. Unreadable saves are reported, never deleted silently.
    /// </summary>
    public static class MainMenuSaves
    {
        public const string UnreadableNotice =
            "Your saved game could not be loaded. The file was kept; New Game starts a fresh resort.";

        public static MainMenuSaveSummary Read(string root, ProgressionConfig configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            var activeSlot = SaveSlotStorage.GetActiveSlot(root);
            var slots = new SaveSlotMetadata[SaveSlotStorage.SlotCount];
            for (var index = 0; index < slots.Length; index++)
                slots[index] = ReadSlot(root, index + 1, activeSlot, configuration);

            SaveSlotMetadata? newest = null;
            SaveSlotMetadata? newestUnreadable = null;
            foreach (var slot in slots)
            {
                if (slot.Status == SaveSlotStatus.Occupied && (newest == null || IsNewer(slot, newest.Value)))
                    newest = slot;
                if (slot.Status == SaveSlotStatus.Corrupted && (newestUnreadable == null || IsNewer(slot, newestUnreadable.Value)))
                    newestUnreadable = slot;
            }

            var newGameSlot = slots[activeSlot - 1];
            if (newest == null)
                return new MainMenuSaveSummary(newestUnreadable == null ? MainMenuContinueState.NoSave : MainMenuContinueState.Unreadable,
                    default, newGameSlot, configuration.BaseDemand.Length, newestUnreadable == null ? null : UnreadableNotice);

            string notice = null;
            if (newestUnreadable != null && newestUnreadable.Value.LastSavedUtc > newest.Value.LastSavedUtc)
                notice = "Your latest save could not be loaded. Continue opens your previous resort.";
            return new MainMenuSaveSummary(MainMenuContinueState.Available, newest.Value, newGameSlot,
                configuration.BaseDemand.Length, notice);
        }

        /// <summary>
        /// Writes a fresh checkpoint into <paramref name="slotNumber"/> through the canonical runtime and makes it
        /// the active slot. Any previous content of that slot is replaced; callers confirm that with the player first.
        /// </summary>
        public static bool TryCreateNewGame(string root, int slotNumber, ProgressionConfig configuration, out string error)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            try
            {
                var slotFile = new JsonFileProgressionPersistence(SaveSlotStorage.GetSlotPath(root, slotNumber));
                // The runtime saves the new game it creates, so the checkpoint has the normal schema and profile ID.
                using (new ResortProgressionRuntime(configuration, new FreshGamePersistence(slotFile))) { }
                SaveSlotStorage.SetActiveSlot(root, slotNumber);
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is ArgumentException || exception is InvalidOperationException)
            {
                error = "A new game could not be created: " + exception.Message;
                return false;
            }
        }

        private static SaveSlotMetadata ReadSlot(string root, int slot, int activeSlot, ProgressionConfig configuration)
        {
            try { return SaveSlotStorage.GetMetadata(root, slot, configuration); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // The legacy-save migration can fail before the slot is read; treat the slot as unreadable.
                return new SaveSlotMetadata(slot, SaveSlotStatus.Corrupted, 0, DateTime.MinValue, slot == activeSlot);
            }
        }

        private static bool IsNewer(SaveSlotMetadata candidate, SaveSlotMetadata current)
        {
            if (candidate.LastSavedUtc != current.LastSavedUtc) return candidate.LastSavedUtc > current.LastSavedUtc;
            if (candidate.IsActive != current.IsActive) return candidate.IsActive;
            return candidate.SlotNumber < current.SlotNumber;
        }

        /// <summary>Ignores whatever the slot holds so the runtime starts a new game, then saves it there.</summary>
        private sealed class FreshGamePersistence : IProgressionPersistence
        {
            private readonly IProgressionPersistence slot;
            public FreshGamePersistence(IProgressionPersistence slot) { this.slot = slot; }
            public ProgressionSaveData Load() => null;
            public void Save(ProgressionSaveData data) => slot.Save(data);
        }
    }
}
