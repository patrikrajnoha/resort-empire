using System;

namespace ResortEmpire.Gameplay.Waves
{
    [Serializable]
    public sealed class ActiveWaveCheckpoint
    {
        public int WaveId;
        public double ClockTime;
        public double WaveStartTime;
        public double ArrivalElapsed;
        public long NextUnitId;
        public BaseArrivalCheckpoint[] BaseArrivals;
        public ScheduledArrivalCheckpoint[] ScheduledArrivals;
        public WaveUnitCheckpoint[] Units;
        public ServiceBoostCheckpoint[] Boosts;

        public ActiveWaveCheckpoint Copy() => new ActiveWaveCheckpoint
        {
            WaveId = WaveId,
            ClockTime = ClockTime,
            WaveStartTime = WaveStartTime,
            ArrivalElapsed = ArrivalElapsed,
            NextUnitId = NextUnitId,
            BaseArrivals = (BaseArrivalCheckpoint[])BaseArrivals.Clone(),
            ScheduledArrivals = (ScheduledArrivalCheckpoint[])ScheduledArrivals.Clone(),
            Units = (WaveUnitCheckpoint[])Units.Clone(),
            Boosts = (ServiceBoostCheckpoint[])Boosts.Clone()
        };
    }

    [Serializable]
    public struct BaseArrivalCheckpoint
    {
        public NeedType Need;
        public int Emitted;
    }

    [Serializable]
    public struct ScheduledArrivalCheckpoint
    {
        public string EventId;
        public bool Started;
        public bool Filtered;
        public bool NotificationDismissed;
        public NeedType TargetNeed;
        public int Emitted;
    }

    [Serializable]
    public struct ServiceBoostCheckpoint
    {
        public BuildingId Building;
        public double ExpiresAt;
        public double SpeedMultiplier;
    }

    [Serializable]
    public struct WaveUnitCheckpoint
    {
        public long Id;
        public NeedType Need;
        public NeedUnitState State;
        public double Arrival;
        public double ServiceStart;
        public double ServiceEnd;
        public double Progress;

        internal static WaveUnitCheckpoint Waiting(NeedRuntime unit) => new WaveUnitCheckpoint
        {
            Id = unit.NeedUnitId, Need = unit.NeedType, Arrival = unit.ArrivalTime
        };

        internal static WaveUnitCheckpoint InService(NeedRuntime unit, double progress)
        {
            var result = Waiting(unit);
            result.State = NeedUnitState.IN_SERVICE;
            result.ServiceStart = unit.ServiceStartTime.Value;
            result.Progress = progress;
            return result;
        }

        internal static WaveUnitCheckpoint Completed(NeedCompletionRecord record) => new WaveUnitCheckpoint
        {
            Id = record.NeedUnitId, Need = record.NeedType, State = NeedUnitState.COMPLETED,
            Arrival = record.ArrivalTime, ServiceStart = record.ServiceStartTime, ServiceEnd = record.ServiceCompletionTime
        };
    }
}
