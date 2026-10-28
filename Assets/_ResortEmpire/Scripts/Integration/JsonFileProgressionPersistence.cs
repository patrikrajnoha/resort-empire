using System;
using System.IO;
using System.Text;
using ResortEmpire.Gameplay.Progression;
using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>One local checkpoint. A malformed save is reported, never silently reset.</summary>
    public sealed class JsonFileProgressionPersistence : IProgressionPersistence
    {
        private readonly string path;
        public JsonFileProgressionPersistence(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A save path is required.", nameof(path));
            this.path = Path.GetFullPath(path);
        }

        public ProgressionSaveData Load()
        {
            string json;
            try { json = File.ReadAllText(path, Encoding.UTF8); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            try
            {
                if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Empty checkpoint.");
                var data = JsonUtility.FromJson<ProgressionSaveData>(json);
                if (data == null) throw new ArgumentException("Empty checkpoint.");
                return data;
            }
            catch (ArgumentException exception) { throw new InvalidDataException("The local checkpoint is invalid; the file has been preserved.", exception); }
        }

        public void Save(ProgressionSaveData data)
        {
            var json = JsonUtility.ToJson(data, true);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(json);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (NotSupportedException exception)
            {
                throw new IOException("This filesystem does not support atomic checkpoint replacement.", exception);
            }
            finally
            {
                // Cleanup errors must not turn a successful atomic replacement into a reported failure.
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
