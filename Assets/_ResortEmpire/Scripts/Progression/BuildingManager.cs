using System;
using System.Collections.Generic;

namespace ResortEmpire.Gameplay.Progression
{
    public readonly struct BuildingStateSnapshot
    {
        internal BuildingStateSnapshot(BuildingSaveData state, ProgressionConfig config, int completedWaves)
        {
            BuildingId = state.BuildingId;
            Level = state.Level;
            IsEligible = completedWaves >= config.UnlockWaves[(int)BuildingId];
            Capacity = config.Capacities[Math.Max(0, Level - 1)];
            ServiceDuration = config.ServiceDurations[(int)BuildingId];
            PurchaseCost = config.PurchaseCosts[(int)BuildingId];
            UpgradeCost = Level > 0 && Level < config.Capacities.Length ? (int?)config.UpgradeCosts[Level - 1] : null;
        }
        public BuildingId BuildingId { get; }
        public NeedType NeedType => BuildingNeedMapping.GetNeedType(BuildingId);
        public int Level { get; }
        public bool IsOwned => Level > 0;
        public bool IsEligible { get; }
        public int Capacity { get; }
        public double ServiceDuration { get; }
        public int PurchaseCost { get; }
        public int? UpgradeCost { get; }
    }

    public sealed class BuildingManager : IBuildingSnapshotProvider
    {
        private readonly ProgressionConfig config;
        public BuildingManager(ProgressionConfig config, ProgressionSaveData initial)
        {
            this.config = config.Copy();
            Apply(initial);
        }
        public IReadOnlyList<BuildingSnapshot> Buildings { get; private set; }
        public IReadOnlyList<BuildingStateSnapshot> States { get; private set; }

        internal void Apply(ProgressionSaveData state)
        {
            var buildings = new List<BuildingSnapshot>();
            var states = new List<BuildingStateSnapshot>();
            var completed = state.ClaimedWaves + (state.Result != null && !state.Result.Claimed ? 1 : 0);
            foreach (var data in state.Buildings)
            {
                var item = new BuildingStateSnapshot(data, config, completed);
                states.Add(item);
                buildings.Add(new BuildingSnapshot(item.BuildingId, item.IsOwned, item.Capacity, item.ServiceDuration, 1));
            }
            Buildings = buildings.AsReadOnly();
            States = states.AsReadOnly();
        }
    }
}
