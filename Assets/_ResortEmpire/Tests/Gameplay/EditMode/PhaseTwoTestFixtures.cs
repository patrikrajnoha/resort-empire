using System.Collections.Generic;
using System.Collections.ObjectModel;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Staff;
using ResortEmpire.Gameplay.Waves;

namespace ResortEmpire.Gameplay.Tests
{
    internal sealed class TestBuildingProvider : IBuildingSnapshotProvider
    {
        private readonly List<BuildingSnapshot> buildings;

        public TestBuildingProvider(bool poolOwned = false, bool bungalowsOwned = false, int capacity = 1, double serviceDuration = 1d, int workerSlotCount = 1)
        {
            buildings = new List<BuildingSnapshot>
            {
                new BuildingSnapshot(BuildingId.Reception, true, capacity, serviceDuration, workerSlotCount),
                new BuildingSnapshot(BuildingId.Restaurant, true, capacity, serviceDuration, workerSlotCount),
                new BuildingSnapshot(BuildingId.BeachBar, true, capacity, serviceDuration, workerSlotCount),
                new BuildingSnapshot(BuildingId.Pool, poolOwned, capacity, serviceDuration, workerSlotCount),
                new BuildingSnapshot(BuildingId.Bungalows, bungalowsOwned, capacity, serviceDuration, workerSlotCount)
            };
        }

        public IReadOnlyList<BuildingSnapshot> Buildings => new ReadOnlyCollection<BuildingSnapshot>(buildings);

        public void SetOwned(BuildingId buildingId, bool isOwned)
        {
            for (var index = 0; index < buildings.Count; index++)
            {
                if (buildings[index].BuildingId != buildingId)
                    continue;

                var previous = buildings[index];
                buildings[index] = new BuildingSnapshot(
                    previous.BuildingId,
                    isOwned,
                    previous.Capacity,
                    previous.ServiceDuration,
                    previous.WorkerSlotCount);
                return;
            }
        }

        public void SetCapacity(BuildingId buildingId, int capacity)
        {
            Replace(buildingId, capacity, null, null);
        }

        public void SetServiceDuration(BuildingId buildingId, double serviceDuration)
        {
            Replace(buildingId, null, serviceDuration, null);
        }

        public void SetWorkerSlotCount(BuildingId buildingId, int workerSlotCount)
        {
            Replace(buildingId, null, null, workerSlotCount);
        }

        private void Replace(BuildingId buildingId, int? capacity, double? serviceDuration, int? workerSlotCount)
        {
            for (var index = 0; index < buildings.Count; index++)
            {
                if (buildings[index].BuildingId != buildingId)
                    continue;

                var previous = buildings[index];
                buildings[index] = new BuildingSnapshot(
                    previous.BuildingId,
                    previous.IsOwned,
                    capacity ?? previous.Capacity,
                    serviceDuration ?? previous.ServiceDuration,
                    workerSlotCount ?? previous.WorkerSlotCount);
                return;
            }
        }
    }

    internal static class PhaseTwoTestData
    {
        public static WaveSimulationConfig Config(double incomingDuration = 0d, double staffedUtilization = 1d, double emptyUtilization = 0.5d)
        {
            return new WaveSimulationConfig(incomingDuration, staffedUtilization, emptyUtilization);
        }

        public static WaveDefinition ThreeNeedWave(int waveId = 1, double arrivalDuration = 4d, int unitsPerNeed = 3)
        {
            return new WaveDefinition(
                waveId,
                arrivalDuration,
                new[]
                {
                    new NeedDemandDefinition(NeedType.Rooms, unitsPerNeed),
                    new NeedDemandDefinition(NeedType.Food, unitsPerNeed),
                    new NeedDemandDefinition(NeedType.Drinks, unitsPerNeed)
                });
        }

        public static (GameState State, SimulationClock Clock, TestBuildingProvider Buildings, WorkerManager Workers, WaveManager Waves) CreateScenario(
            bool poolOwned = false,
            bool bungalowsOwned = false,
            int capacity = 1,
            double serviceDuration = 1d,
            int workerSlotCount = 1)
        {
            var state = new GameState();
            var clock = new SimulationClock();
            var provider = new TestBuildingProvider(poolOwned, bungalowsOwned, capacity, serviceDuration, workerSlotCount);
            var initialAssignments = new List<BuildingId> { BuildingId.Reception, BuildingId.Restaurant };
            if (poolOwned)
                initialAssignments.Add(BuildingId.Pool);
            if (bungalowsOwned)
                initialAssignments.Add(BuildingId.Bungalows);
            var workers = new WorkerManager(provider, state, initialAssignments);
            var waves = new WaveManager(state, clock, provider, workers, Config());
            return (state, clock, provider, workers, waves);
        }
    }

}
