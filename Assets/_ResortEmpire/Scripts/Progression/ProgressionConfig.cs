using System;
using ResortEmpire.Gameplay.Waves;

namespace ResortEmpire.Gameplay.Progression
{
    /// <summary>Serialized tuning input. The scene's demo asset supplies all balancing numbers.</summary>
    [Serializable]
    public sealed class ProgressionConfig
    {
        public int InitialCoins;
        public int InitialEnergy;
        public int InitialResortValue;
        public int[] Capacities;
        public int[] UpgradeCosts;
        public int[] PurchaseCosts;
        public int[] UnlockWaves;
        public double[] ServiceDurations;
        public BuildingId[] InitialWorkers;
        public int[] BaseDemand;
        public double IncomingDuration;
        public double ArrivalDuration;
        public double UtilizationStaffed;
        public double UtilizationEmpty;
        public double PatienceGrace;
        public double PatienceLimit;
        public double UnitFloor;
        public double WaveFloor;
        public double OceanViewWeight;
        public double[] StarThresholds;
        public int CoinsPerUnit;
        public double[] CoinMultipliers;
        public int[] EnergyRewards;
        public int[] ResortValueRewards;
        public double RushStart;
        public double RushDuration;
        public int RushUnits;
        public double DebutDemandFactor;

        public WaveSimulationConfig SimulationConfig =>
            new WaveSimulationConfig(IncomingDuration, UtilizationStaffed, UtilizationEmpty);

        public ProgressionConfig Copy()
        {
            Validate();
            var copy = (ProgressionConfig)MemberwiseClone();
            copy.Capacities = (int[])Capacities.Clone();
            copy.UpgradeCosts = (int[])UpgradeCosts.Clone();
            copy.PurchaseCosts = (int[])PurchaseCosts.Clone();
            copy.UnlockWaves = (int[])UnlockWaves.Clone();
            copy.ServiceDurations = (double[])ServiceDurations.Clone();
            copy.InitialWorkers = (BuildingId[])InitialWorkers.Clone();
            copy.BaseDemand = (int[])BaseDemand.Clone();
            copy.StarThresholds = (double[])StarThresholds.Clone();
            copy.CoinMultipliers = (double[])CoinMultipliers.Clone();
            copy.EnergyRewards = (int[])EnergyRewards.Clone();
            copy.ResortValueRewards = (int[])ResortValueRewards.Clone();
            return copy;
        }

        public void Validate()
        {
            Require(InitialCoins >= 0 && InitialEnergy >= 0 && InitialResortValue >= 0, "Negative initial resources.");
            Require(Capacities != null && Capacities.Length == 3, "Three capacity levels are required.");
            Require(UpgradeCosts != null && UpgradeCosts.Length == 2, "Two upgrade prices are required.");
            Require(PurchaseCosts != null && PurchaseCosts.Length == 5 && UnlockWaves != null && UnlockWaves.Length == 5
                && ServiceDurations != null && ServiceDurations.Length == 5, "Five building definitions are required.");
            Require(InitialWorkers != null && InitialWorkers.Length == 2 && InitialWorkers[0] != InitialWorkers[1]
                && (int)InitialWorkers[0] >= 0 && (int)InitialWorkers[0] < 3
                && (int)InitialWorkers[1] >= 0 && (int)InitialWorkers[1] < 3, "Two distinct starting building assignments are required.");
            Require(BaseDemand != null && BaseDemand.Length == 8, "The Prototype requires eight waves.");
            Require(StarThresholds != null && StarThresholds.Length == 5 && CoinMultipliers != null && CoinMultipliers.Length == 5
                && EnergyRewards != null && EnergyRewards.Length == 5 && ResortValueRewards != null && ResortValueRewards.Length == 5,
                "Five rating reward tiers are required.");
            for (var i = 0; i < 3; i++)
                Require(Capacities[i] > 0 && (i == 0 || Capacities[i] > Capacities[i - 1]), "Capacity must increase with level.");
            foreach (var price in UpgradeCosts) Require(price >= 0, "Negative upgrade price.");
            for (var i = 0; i < 5; i++)
            {
                Require(PurchaseCosts[i] >= 0 && UnlockWaves[i] >= 0 && UnlockWaves[i] <= BaseDemand.Length, "Invalid building gate/price.");
                Require(Finite(ServiceDurations[i]) && ServiceDurations[i] > 0, "Invalid service duration.");
                Require(Finite(StarThresholds[i]) && StarThresholds[i] >= 0 && StarThresholds[i] <= 100
                    && (i == 0 || StarThresholds[i] > StarThresholds[i - 1]), "Invalid rating thresholds.");
                Require(Finite(CoinMultipliers[i]) && CoinMultipliers[i] >= 0 && EnergyRewards[i] >= 0 && ResortValueRewards[i] >= 0,
                    "Invalid rating rewards.");
            }
            var largestDemand = 0;
            foreach (var demand in BaseDemand)
            {
                Require(demand > 0, "Wave demand must be positive.");
                largestDemand = Math.Max(largestDemand, demand);
            }
            Require(Finite(ArrivalDuration) && ArrivalDuration > 0, "Invalid arrival duration.");
            Require(Finite(PatienceGrace) && PatienceGrace >= 0 && Finite(PatienceLimit) && PatienceLimit > PatienceGrace, "Invalid patience range.");
            Require(Finite(UnitFloor) && Finite(WaveFloor) && UnitFloor >= 0 && UnitFloor < WaveFloor
                && WaveFloor >= StarThresholds[0] && WaveFloor < StarThresholds[2], "Invalid satisfaction floors.");
            Require(Finite(OceanViewWeight) && OceanViewWeight > 1 && CoinsPerUnit >= 0, "Invalid weight/reward.");
            Require(Finite(DebutDemandFactor) && DebutDemandFactor > 0 && DebutDemandFactor <= 1, "Invalid debut factor.");
            var simulation = SimulationConfig;
            new ScheduledDemandEvent("sudden-rush", ScheduledDemandEventKind.SuddenRush, RushStart, RushDuration, RushUnits);
            Require(RushStart + RushDuration <= ArrivalDuration, "Rush must fit inside arrivals.");
            var maximumUnits = (long)largestDemand * 5 + RushUnits;
            Require(maximumUnits <= int.MaxValue, "Wave demand exceeds the supported count.");
            foreach (var multiplier in CoinMultipliers)
                Require(maximumUnits * (double)CoinsPerUnit * multiplier <= int.MaxValue, "A configured wave reward exceeds the wallet range.");
            Require(Finite(maximumUnits * OceanViewWeight * 100), "The configured weight exceeds the rating range.");
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static void Require(bool condition, string message)
        {
            if (!condition) throw new ArgumentException(message);
        }
    }
}
