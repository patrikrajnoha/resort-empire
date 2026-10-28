using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ResortEmpire.Gameplay;

namespace ResortEmpire.Gameplay.Staff
{
    /// <summary>Anonymous Prototype Worker pool and zero-cost assignment commands.</summary>
    public sealed class WorkerManager : IWorkerAssignmentProvider, IWorkerAssignmentConfigurationHost
    {
        private readonly IBuildingSnapshotProvider buildingProvider;
        private readonly GameState gameState;
        private readonly List<WorkerAssignmentSnapshot> assignments = new List<WorkerAssignmentSnapshot>();
        private readonly HashSet<BuildingId> knownOwnedBuildings = new HashSet<BuildingId>();
        private IActiveWaveConfigurationGuard configurationGuard;
        private int nextWorkerId = 1;
        private IReadOnlyList<WorkerAssignmentSnapshot> assignmentSnapshot = new ReadOnlyCollection<WorkerAssignmentSnapshot>(new List<WorkerAssignmentSnapshot>());
        private IReadOnlyList<BuildingId> emptyOwnedBuildingSnapshot = new ReadOnlyCollection<BuildingId>(new List<BuildingId>());

        public WorkerManager(
            IBuildingSnapshotProvider buildingProvider,
            GameState gameState,
            IEnumerable<BuildingId> initialAssignments)
        {
            this.buildingProvider = buildingProvider ?? throw new ArgumentNullException(nameof(buildingProvider));
            this.gameState = gameState ?? throw new ArgumentNullException(nameof(gameState));
            if (initialAssignments == null)
                throw new ArgumentNullException(nameof(initialAssignments));

            var buildingSnapshots = ReadBuildingSnapshots(requirePrototypeSlots: true);
            foreach (var building in buildingSnapshots.Values)
            {
                if (building.IsOwned)
                    knownOwnedBuildings.Add(building.BuildingId);
            }

            foreach (var buildingId in initialAssignments)
            {
                BuildingSnapshot building;
                if (!buildingSnapshots.TryGetValue(buildingId, out building))
                    throw new ArgumentException("Initial assignment references an unknown building.", nameof(initialAssignments));
                if (!building.IsOwned)
                    throw new ArgumentException("Initial assignment references an unowned building.", nameof(initialAssignments));
                if (GetAssignedWorkerCount(buildingId) >= building.WorkerSlotCount)
                    throw new ArgumentException("Initial assignments exceed a building's Worker-slot count.", nameof(initialAssignments));

                assignments.Add(new WorkerAssignmentSnapshot(nextWorkerId++, buildingId));
            }

            var ownedBuildingCount = 0;
            foreach (var building in buildingSnapshots.Values)
            {
                if (building.IsOwned)
                    ownedBuildingCount++;
            }
            if (assignments.Count != Math.Max(0, ownedBuildingCount - 1))
                throw new ArgumentException("Prototype Worker assignments must leave exactly one owned building unstaffed.", nameof(initialAssignments));

            RefreshSnapshots(buildingSnapshots);
        }

        public event Action<WorkerAssignmentChange> AssignmentChanged;

        public IReadOnlyList<WorkerAssignmentSnapshot> Assignments => assignmentSnapshot;
        public IReadOnlyList<BuildingId> EmptyOwnedBuildings => emptyOwnedBuildingSnapshot;
        public int WorkerCount => assignments.Count;

        public void AttachConfigurationGuard(IActiveWaveConfigurationGuard guard)
        {
            if (guard == null)
                throw new ArgumentNullException(nameof(guard));
            if (configurationGuard != null && !ReferenceEquals(configurationGuard, guard))
                throw new InvalidOperationException("A WorkerManager cannot be rebound to a different active-wave configuration guard.");

            configurationGuard = guard;
        }

        public int GetAssignedWorkerCount(BuildingId buildingId)
        {
            var count = 0;
            foreach (var assignment in assignments)
            {
                if (assignment.BuildingId == buildingId)
                    count++;
            }
            return count;
        }

        public bool TryReassignWorker(BuildingId sourceBuildingId, BuildingId destinationBuildingId)
        {
            if (gameState.CurrentState != GamePhase.AWAITING_WAVE
                && gameState.CurrentState != GamePhase.WAVE_ACTIVE)
                return false;
            if (sourceBuildingId == destinationBuildingId)
                return false;
            if (gameState.CurrentState == GamePhase.WAVE_ACTIVE
                && configurationGuard != null
                && !configurationGuard.IsConfigurationStable)
                return false;

            var buildingSnapshots = ReadBuildingSnapshots(requirePrototypeSlots: false);
            if (buildingSnapshots == null)
                return false;
            BuildingSnapshot source;
            BuildingSnapshot destination;
            if (!buildingSnapshots.TryGetValue(sourceBuildingId, out source)
                || !buildingSnapshots.TryGetValue(destinationBuildingId, out destination))
                return false;
            if (!source.IsOwned || !destination.IsOwned)
                return false;
            if (GetAssignedWorkerCount(sourceBuildingId) <= 0)
                return false;
            if (GetAssignedWorkerCount(destinationBuildingId) > 0)
                return false;

            var assignmentIndex = assignments.FindIndex(assignment => assignment.BuildingId == sourceBuildingId);
            if (assignmentIndex < 0)
                return false;

            var previous = assignments[assignmentIndex];
            assignments[assignmentIndex] = new WorkerAssignmentSnapshot(previous.WorkerId, destinationBuildingId);
            RefreshSnapshots(buildingSnapshots);
            AssignmentChanged?.Invoke(new WorkerAssignmentChange(previous.WorkerId, sourceBuildingId, destinationBuildingId));
            return true;
        }

        /// <summary>
        /// Applies an externally confirmed ownership change. It is intentionally not a purchase operation.
        /// A duplicate notification is a safe no-op and cannot create another Worker.
        /// </summary>
        public bool TryApplyOwnershipChange(BuildingId buildingId)
        {
            if (gameState.CurrentState != GamePhase.AWAITING_WAVE)
                return false;
            if (knownOwnedBuildings.Contains(buildingId))
                return false;

            var buildingSnapshots = ReadBuildingSnapshots(requirePrototypeSlots: false);
            if (buildingSnapshots == null)
                return false;
            BuildingSnapshot building;
            if (!buildingSnapshots.TryGetValue(buildingId, out building) || !building.IsOwned)
                return false;
            if (building.WorkerSlotCount <= 0 || GetAssignedWorkerCount(buildingId) >= building.WorkerSlotCount)
                return false;

            knownOwnedBuildings.Add(buildingId);
            assignments.Add(new WorkerAssignmentSnapshot(nextWorkerId++, buildingId));
            RefreshSnapshots(buildingSnapshots);
            return true;
        }

        private Dictionary<BuildingId, BuildingSnapshot> ReadBuildingSnapshots(bool requirePrototypeSlots)
        {
            var snapshots = buildingProvider.Buildings;
            if (snapshots == null)
                throw new InvalidOperationException("Building provider returned no snapshot list.");

            var result = new Dictionary<BuildingId, BuildingSnapshot>();
            foreach (var snapshot in snapshots)
            {
                if (result.ContainsKey(snapshot.BuildingId))
                    throw new InvalidOperationException("Building provider returned duplicate building identifiers.");
                if (snapshot.WorkerSlotCount != 1)
                {
                    if (requirePrototypeSlots)
                        throw new InvalidOperationException("Prototype Worker configuration requires exactly one slot per building.");
                    return null;
                }
                result.Add(snapshot.BuildingId, snapshot);
            }
            return result;
        }

        private void RefreshSnapshots(Dictionary<BuildingId, BuildingSnapshot> buildingSnapshots)
        {
            assignments.Sort((left, right) => left.WorkerId.CompareTo(right.WorkerId));
            assignmentSnapshot = new ReadOnlyCollection<WorkerAssignmentSnapshot>(new List<WorkerAssignmentSnapshot>(assignments));

            var emptyBuildings = new List<BuildingId>();
            foreach (var pair in buildingSnapshots)
            {
                if (pair.Value.IsOwned && GetAssignedWorkerCount(pair.Key) == 0)
                    emptyBuildings.Add(pair.Key);
            }
            emptyBuildings.Sort();
            emptyOwnedBuildingSnapshot = new ReadOnlyCollection<BuildingId>(emptyBuildings);
        }
    }
}
