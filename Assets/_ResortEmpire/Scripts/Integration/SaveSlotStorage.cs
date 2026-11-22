using System;
using System.IO;
using System.Text;
using ResortEmpire.Gameplay.Progression;
using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    public enum SaveSlotStatus
    {
        Empty,
        Occupied,
        Corrupted
    }

    /// <summary>Read-only summary for a single persisted progression checkpoint.</summary>
    public readonly struct SaveSlotMetadata
    {
        public SaveSlotMetadata(int slotNumber, SaveSlotStatus status, int currentWave, DateTime lastSavedUtc, bool isActive)
        {
            SlotNumber = slotNumber;
            Status = status;
            CurrentWave = currentWave;
            LastSavedUtc = lastSavedUtc;
            IsActive = isActive;
        }

        public int SlotNumber { get; }
        public SaveSlotStatus Status { get; }
        public int CurrentWave { get; }
        public DateTime LastSavedUtc { get; }
        public bool IsActive { get; }
    }

    /// <summary>
    /// Owns the slot-to-file mapping only. The canonical progression serializer remains
    /// <see cref="JsonFileProgressionPersistence"/> so each slot retains the existing schema,
    /// validation and atomic replacement behavior.
    /// </summary>
    public static class SaveSlotStorage
    {
        public const int SlotCount = 3;
        private const string FolderName = "save-slots";
        private const string ActiveSlotFileName = "active-slot.json";
        private const string LegacyFileName = "resort-progress-v1.json";

        [Serializable]
        private sealed class ActiveSlotRecord
        {
            public int SlotNumber = 1;
        }

        public static IProgressionPersistence CreateActivePersistence(string persistentDataPath)
        {
            var slot = GetActiveSlot(persistentDataPath);
            EnsureLegacyMigration(persistentDataPath, slot);
            return new JsonFileProgressionPersistence(GetSlotPath(persistentDataPath, slot));
        }

        public static IProgressionPersistence CreatePersistence(string persistentDataPath, int slotNumber)
        {
            ValidateSlotNumber(slotNumber);
            EnsureLegacyMigration(persistentDataPath, slotNumber);
            return new JsonFileProgressionPersistence(GetSlotPath(persistentDataPath, slotNumber));
        }

        public static int GetActiveSlot(string persistentDataPath)
        {
            var activePath = Path.Combine(GetFolderPath(persistentDataPath), ActiveSlotFileName);
            try
            {
                if (!File.Exists(activePath)) return 1;
                var record = JsonUtility.FromJson<ActiveSlotRecord>(File.ReadAllText(activePath, Encoding.UTF8));
                return record != null && IsValidSlotNumber(record.SlotNumber) ? record.SlotNumber : 1;
            }
            catch (IOException) { return 1; }
            catch (UnauthorizedAccessException) { return 1; }
            catch (ArgumentException) { return 1; }
        }

        public static void SetActiveSlot(string persistentDataPath, int slotNumber)
        {
            ValidateSlotNumber(slotNumber);
            var folder = GetFolderPath(persistentDataPath);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, ActiveSlotFileName);
            WriteAtomically(path, JsonUtility.ToJson(new ActiveSlotRecord { SlotNumber = slotNumber }, true));
        }

        public static SaveSlotMetadata GetMetadata(string persistentDataPath, int slotNumber, ProgressionConfig configuration)
        {
            ValidateSlotNumber(slotNumber);
            EnsureLegacyMigration(persistentDataPath, slotNumber);
            var path = GetSlotPath(persistentDataPath, slotNumber);
            var active = GetActiveSlot(persistentDataPath) == slotNumber;
            if (!File.Exists(path)) return new SaveSlotMetadata(slotNumber, SaveSlotStatus.Empty, 0, default, active);

            try
            {
                var data = new JsonFileProgressionPersistence(path).Load();
                if (data == null) return new SaveSlotMetadata(slotNumber, SaveSlotStatus.Empty, 0, default, active);
                ResortProgressionRuntime.ValidateSave(data, configuration);
                var wave = data.ActiveWave != null ? data.ActiveWave.WaveId
                    : data.Result != null ? data.Result.WaveId : data.ClaimedWaves + 1;
                return new SaveSlotMetadata(slotNumber, SaveSlotStatus.Occupied, wave, File.GetLastWriteTimeUtc(path), active);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is ArgumentException || exception is InvalidDataException)
            {
                return new SaveSlotMetadata(slotNumber, SaveSlotStatus.Corrupted, 0, File.GetLastWriteTimeUtc(path), active);
            }
        }

        /// <summary>Deletes only a non-active slot after the UI has obtained explicit confirmation.</summary>
        public static bool TryDeleteInactiveSlot(string persistentDataPath, int slotNumber)
        {
            ValidateSlotNumber(slotNumber);
            if (GetActiveSlot(persistentDataPath) == slotNumber) return false;
            var path = GetSlotPath(persistentDataPath, slotNumber);
            if (!File.Exists(path)) return true;
            File.Delete(path);
            return true;
        }

        /// <summary>Clears a slot only when the caller has already confirmed an overwrite.</summary>
        public static void ClearSlotForConfirmedOverwrite(string persistentDataPath, int slotNumber)
        {
            ValidateSlotNumber(slotNumber);
            var path = GetSlotPath(persistentDataPath, slotNumber);
            if (File.Exists(path)) File.Delete(path);
            SetActiveSlot(persistentDataPath, slotNumber);
        }

        public static string GetSlotPath(string persistentDataPath, int slotNumber)
        {
            ValidateSlotNumber(slotNumber);
            return Path.Combine(GetFolderPath(persistentDataPath), "slot-" + slotNumber + "-progress-v1.json");
        }

        private static void EnsureLegacyMigration(string persistentDataPath, int slotNumber)
        {
            if (slotNumber != 1) return;
            var destination = GetSlotPath(persistentDataPath, 1);
            var legacy = Path.Combine(persistentDataPath, LegacyFileName);
            if (File.Exists(destination) || !File.Exists(legacy)) return;

            Directory.CreateDirectory(GetFolderPath(persistentDataPath));
            var temporary = destination + ".migration.tmp";
            try
            {
                File.Copy(legacy, temporary, true);
                if (File.Exists(destination)) return;
                File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string GetFolderPath(string persistentDataPath)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath)) throw new ArgumentException("A persistent data path is required.", nameof(persistentDataPath));
            return Path.Combine(persistentDataPath, FolderName);
        }

        private static void WriteAtomically(string path, string text)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static bool IsValidSlotNumber(int slotNumber) => slotNumber >= 1 && slotNumber <= SlotCount;
        private static void ValidateSlotNumber(int slotNumber)
        {
            if (!IsValidSlotNumber(slotNumber)) throw new ArgumentOutOfRangeException(nameof(slotNumber));
        }
    }
}
