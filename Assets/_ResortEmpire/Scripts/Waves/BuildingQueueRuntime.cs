using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ResortEmpire.Gameplay.Waves
{
    /// <summary>Deterministic FIFO queue for waiting need units at one building.</summary>
    public sealed class BuildingQueueRuntime
    {
        private readonly List<NeedRuntime> waitingUnits = new List<NeedRuntime>();
        private readonly HashSet<long> waitingUnitIds = new HashSet<long>();

        public BuildingQueueRuntime(BuildingId buildingId)
        {
            if (!Enum.IsDefined(typeof(BuildingId), buildingId))
                throw new ArgumentOutOfRangeException(nameof(buildingId), buildingId, "Unknown building identifier.");

            BuildingId = buildingId;
        }

        public BuildingId BuildingId { get; }
        public int Count => waitingUnits.Count;
        public IReadOnlyList<long> WaitingUnitIds
        {
            get
            {
                var ids = new List<long>(waitingUnits.Count);
                foreach (var unit in waitingUnits)
                    ids.Add(unit.NeedUnitId);
                return new ReadOnlyCollection<long>(ids);
            }
        }

        public bool Contains(long needUnitId)
        {
            return waitingUnitIds.Contains(needUnitId);
        }

        public bool Enqueue(NeedRuntime needUnit)
        {
            if (needUnit == null)
                throw new ArgumentNullException(nameof(needUnit));
            if (needUnit.BuildingId != BuildingId || needUnit.State != NeedUnitState.WAITING)
                return false;
            if (!waitingUnitIds.Add(needUnit.NeedUnitId))
                return false;

            var insertionIndex = waitingUnits.FindIndex(existing => existing.ArrivalTime > needUnit.ArrivalTime);
            if (insertionIndex < 0)
                waitingUnits.Add(needUnit);
            else
                waitingUnits.Insert(insertionIndex, needUnit);
            return true;
        }

        public bool TryDequeue(out NeedRuntime needUnit)
        {
            if (waitingUnits.Count == 0)
            {
                needUnit = null;
                return false;
            }

            needUnit = waitingUnits[0];
            waitingUnits.RemoveAt(0);
            waitingUnitIds.Remove(needUnit.NeedUnitId);
            return true;
        }

        public void Clear()
        {
            waitingUnits.Clear();
            waitingUnitIds.Clear();
        }
    }
}
