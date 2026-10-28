using System;
using System.Collections.Generic;

namespace ResortEmpire.Gameplay.Progression
{
    /// <summary>Persistence DTO, never exposed as live UI state. Only between-wave checkpoints are stored.</summary>
    [Serializable]
    public sealed class ProgressionSaveData
    {
        public int Version = 1;
        public string ProfileId;
        public int Coins;
        public int Energy;
        public int ResortValue;
        public int ClaimedWaves;
        public GamePhase Phase;
        public BuildingSaveData[] Buildings;
        public WorkerSaveData[] Workers;
        public List<string> ClaimedRewardIds = new List<string>();
        public WaveResultData Result;

        public ProgressionSaveData Copy()
        {
            var copy = (ProgressionSaveData)MemberwiseClone();
            copy.Buildings = Array.ConvertAll(Buildings, x => x.Copy());
            copy.Workers = Array.ConvertAll(Workers, x => x.Copy());
            copy.ClaimedRewardIds = new List<string>(ClaimedRewardIds);
            copy.Result = Result?.Copy();
            return copy;
        }
    }

    [Serializable]
    public sealed class BuildingSaveData
    {
        public BuildingId BuildingId;
        public int Level; // 0 = not owned
        public bool HasDebuted;
        public int LastBaseDemand;
        public BuildingSaveData Copy() => (BuildingSaveData)MemberwiseClone();
    }

    [Serializable]
    public sealed class WorkerSaveData
    {
        public int WorkerId;
        public BuildingId BuildingId;
        public WorkerSaveData Copy() => (WorkerSaveData)MemberwiseClone();
    }

    [Serializable]
    public sealed class WaveResultData
    {
        public string RewardId;
        public int WaveId;
        public int ServedUnits;
        public double Satisfaction;
        public int Stars;
        public bool Perfect;
        public bool Claimed;
        public int Coins;
        public int Energy;
        public int ResortValue;
        public BuildingWaitData[] Waiting;
        public WaveResultData Copy()
        {
            var copy = (WaveResultData)MemberwiseClone();
            copy.Waiting = Array.ConvertAll(Waiting, x => x.Copy());
            return copy;
        }
    }

    [Serializable]
    public sealed class BuildingWaitData
    {
        public BuildingId BuildingId;
        public int ServedUnits;
        public double TotalWait;
        public double MaxWait;
        public BuildingWaitData Copy() => (BuildingWaitData)MemberwiseClone();
    }

    public interface IProgressionPersistence
    {
        /// <returns>Null only when no save exists. Invalid/unreadable saves must throw.</returns>
        ProgressionSaveData Load();
        /// <summary>Atomically replaces the checkpoint or throws without replacing it.</summary>
        void Save(ProgressionSaveData data);
    }
}
