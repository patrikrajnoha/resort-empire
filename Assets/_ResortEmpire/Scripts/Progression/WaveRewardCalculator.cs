using System;
using System.Collections.Generic;

namespace ResortEmpire.Gameplay.Progression
{
    public readonly struct BuildingWaitSnapshot
    {
        internal BuildingWaitSnapshot(BuildingWaitData data)
        {
            BuildingId = data.BuildingId;
            ServedUnits = data.ServedUnits;
            TotalWait = data.TotalWait;
            MaxWait = data.MaxWait;
        }
        public BuildingId BuildingId { get; }
        public int ServedUnits { get; }
        public double TotalWait { get; }
        public double MaxWait { get; }
        public double AverageWait => ServedUnits == 0 ? 0 : TotalWait / ServedUnits;
    }

    public sealed class WaveResult
    {
        internal WaveResult(WaveResultData data)
        {
            RewardId = data.RewardId;
            WaveId = data.WaveId;
            ServedUnits = data.ServedUnits;
            Satisfaction = data.Satisfaction;
            Stars = data.Stars;
            Perfect = data.Perfect;
            Claimed = data.Claimed;
            Rewards = new ResourceSnapshot(data.Coins, data.Energy, data.ResortValue);
            var waits = new List<BuildingWaitSnapshot>();
            foreach (var wait in data.Waiting) waits.Add(new BuildingWaitSnapshot(wait));
            Waiting = waits.AsReadOnly();
        }
        public string RewardId { get; }
        public int WaveId { get; }
        public int ServedUnits { get; }
        public double Satisfaction { get; }
        public int Stars { get; }
        public bool Perfect { get; }
        public bool Claimed { get; }
        public ResourceSnapshot Rewards { get; }
        public IReadOnlyList<BuildingWaitSnapshot> Waiting { get; }
    }

    public sealed class WaveRewardCalculator
    {
        private readonly ProgressionConfig config;
        public WaveRewardCalculator(ProgressionConfig config) { this.config = config.Copy(); }

        public double CalculateUnitScore(double waitingDuration)
        {
            if (!ProgressionConfig.Finite(waitingDuration) || waitingDuration < 0)
                throw new ArgumentOutOfRangeException(nameof(waitingDuration));
            if (waitingDuration <= config.PatienceGrace) return 100;
            var fraction = Math.Min(1, (waitingDuration - config.PatienceGrace) / (config.PatienceLimit - config.PatienceGrace));
            return 100 - (100 - config.UnitFloor) * fraction;
        }

        public int CalculateStars(double satisfaction)
        {
            if (!ProgressionConfig.Finite(satisfaction) || satisfaction < config.WaveFloor || satisfaction > 100)
                throw new ArgumentOutOfRangeException(nameof(satisfaction));
            var stars = 1;
            for (var i = 1; i < config.StarThresholds.Length; i++)
                if (satisfaction >= config.StarThresholds[i]) stars = i + 1;
            return stars;
        }

        public WaveResult Calculate(string rewardId, int waveId, IReadOnlyList<NeedCompletionRecord> records) =>
            new WaveResult(CalculateData(rewardId, waveId, records));

        internal WaveResultData CalculateData(string rewardId, int waveId, IReadOnlyList<NeedCompletionRecord> records)
        {
            if (string.IsNullOrWhiteSpace(rewardId) || waveId <= 0 || records == null || records.Count == 0)
                throw new ArgumentException("A result requires an identifier and completed demand.");
            var weightedScore = 0d;
            var weightTotal = 0d;
            var perfect = true;
            var ids = new HashSet<long>();
            var waits = new SortedDictionary<BuildingId, BuildingWaitData>();
            foreach (var record in records)
            {
                if (record.NeedUnitId <= 0 || record.WaveId != waveId || !ids.Add(record.NeedUnitId))
                    throw new ArgumentException("Completion records must be unique and belong to this wave.");
                var score = CalculateUnitScore(record.WaitingDuration);
                var weight = record.NeedType == NeedType.OceanView ? config.OceanViewWeight : 1;
                weightedScore += score * weight;
                weightTotal += weight;
                perfect &= record.WaitingDuration <= config.PatienceGrace;
                BuildingWaitData wait;
                if (!waits.TryGetValue(record.BuildingId, out wait))
                {
                    wait = new BuildingWaitData { BuildingId = record.BuildingId };
                    waits.Add(record.BuildingId, wait);
                }
                wait.ServedUnits++;
                wait.TotalWait += record.WaitingDuration;
                wait.MaxWait = Math.Max(wait.MaxWait, record.WaitingDuration);
            }
            var satisfaction = perfect ? 100 : Math.Max(config.WaveFloor, Math.Min(100, weightedScore / weightTotal));
            var stars = CalculateStars(satisfaction);
            // Decimal avoids a binary floating-point artifact reducing an exact integer coin award by one.
            var coins = decimal.Floor((decimal)records.Count * config.CoinsPerUnit * (decimal)config.CoinMultipliers[stars - 1]);
            if (coins > int.MaxValue) throw new OverflowException("Wave reward exceeds the wallet range.");
            return new WaveResultData
            {
                RewardId = rewardId, WaveId = waveId, ServedUnits = records.Count,
                Satisfaction = satisfaction, Stars = stars, Perfect = perfect,
                Coins = (int)coins, Energy = config.EnergyRewards[stars - 1], ResortValue = config.ResortValueRewards[stars - 1],
                Waiting = new List<BuildingWaitData>(waits.Values).ToArray()
            };
        }
    }
}
