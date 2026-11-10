using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ResortEmpire.Gameplay;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// The one rule S3 uses to decide where MOVE sends a Worker. A fixed target never redirects to a
    /// different vacancy, even after it becomes staffed. Without a target, the Prototype's single
    /// vacant building receives the Worker. The focus is the building S3 was opened for.
    /// </summary>
    public sealed class WorkerAllocationContext
    {
        private readonly HashSet<BuildingId> staffed;

        private WorkerAllocationContext(IReadOnlyList<BuildingId> owned, HashSet<BuildingId> staffed,
            BuildingId? target, BuildingId? focus, bool movesAllowed)
        {
            OwnedBuildings = owned;
            this.staffed = staffed;
            Target = target;
            Focus = focus.HasValue && Contains(owned, focus.Value) ? focus : null;
            MovesAllowed = movesAllowed;

            if (target.HasValue)
            {
                Destination = Contains(owned, target.Value) && !staffed.Contains(target.Value) ? target : null;
                return;
            }
            foreach (var building in owned)
            {
                if (!staffed.Contains(building))
                    Destination = building;
            }
        }

        /// <summary>Owned buildings in BuildingId order, one S3 card each.</summary>
        public IReadOnlyList<BuildingId> OwnedBuildings { get; }
        public BuildingId? Target { get; }
        /// <summary>The service S3 was opened for, or null. Never an unowned building.</summary>
        public BuildingId? Focus { get; }
        /// <summary>Where MOVE sends a Worker now, or null when no move is possible.</summary>
        public BuildingId? Destination { get; }
        /// <summary>Moves are free between waves and during the active wave, never while incoming or paused.</summary>
        public bool MovesAllowed { get; }

        public bool IsStaffed(BuildingId building) => staffed.Contains(building);

        public bool CanMoveFrom(BuildingId source)
        {
            return MovesAllowed && Destination.HasValue && source != Destination.Value
                && staffed.Contains(source) && Contains(OwnedBuildings, source);
        }

        public static WorkerAllocationContext Capture(IResortGameplayUiFacade gameplay, BuildingId? target, BuildingId? focus)
        {
            if (gameplay == null)
                throw new ArgumentNullException(nameof(gameplay));

            var owned = new List<BuildingId>();
            foreach (var building in gameplay.Buildings)
            {
                if (building.IsOwned)
                    owned.Add(building.BuildingId);
            }
            var staffed = new List<BuildingId>();
            foreach (var worker in gameplay.CurrentWorkerAssignments)
                staffed.Add(worker.BuildingId);
            return Resolve(owned, staffed, target, focus, gameplay.CurrentPhase);
        }

        public static WorkerAllocationContext Resolve(IEnumerable<BuildingId> ownedBuildings, IEnumerable<BuildingId> staffedBuildings,
            BuildingId? target, BuildingId? focus, GamePhase phase)
        {
            if (ownedBuildings == null)
                throw new ArgumentNullException(nameof(ownedBuildings));
            if (staffedBuildings == null)
                throw new ArgumentNullException(nameof(staffedBuildings));

            var owned = new List<BuildingId>(new HashSet<BuildingId>(ownedBuildings));
            owned.Sort((left, right) => ((int)left).CompareTo((int)right));
            var movesAllowed = phase == GamePhase.AWAITING_WAVE || phase == GamePhase.WAVE_ACTIVE;
            return new WorkerAllocationContext(new ReadOnlyCollection<BuildingId>(owned),
                new HashSet<BuildingId>(staffedBuildings), target, focus, movesAllowed);
        }

        private static bool Contains(IReadOnlyList<BuildingId> buildings, BuildingId building)
        {
            foreach (var candidate in buildings)
            {
                if (candidate == building)
                    return true;
            }
            return false;
        }
    }
}
