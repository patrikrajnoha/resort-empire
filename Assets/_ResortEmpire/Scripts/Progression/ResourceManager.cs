using System;

namespace ResortEmpire.Gameplay.Progression
{
    public readonly struct ResourceSnapshot
    {
        public ResourceSnapshot(int coins, int energy, int resortValue)
        {
            if (coins < 0 || energy < 0 || resortValue < 0) throw new ArgumentOutOfRangeException(nameof(coins));
            Coins = coins;
            Energy = energy;
            ResortValue = resortValue;
        }
        public int Coins { get; }
        public int Energy { get; }
        public int ResortValue { get; }
    }

    /// <summary>Authoritative wallet. UI receives snapshots, never a mutable manager.</summary>
    public sealed class ResourceManager
    {
        private ResourceSnapshot resources;
        public ResourceManager(ResourceSnapshot initial) { resources = initial; }
        public ResourceSnapshot Resources => resources;
        public event Action<ResourceSnapshot> ResourcesChanged;
        public bool CanAffordCoins(int amount) => amount >= 0 && resources.Coins >= amount;
        public bool TrySpendCoins(int amount) => amount >= 0 && TryChange(-amount, 0, 0);
        public bool TryAddCoins(int amount) => amount >= 0 && TryChange(amount, 0, 0);
        public bool TrySpendEnergy(int amount) => amount >= 0 && TryChange(0, -amount, 0);
        public bool TryAddEnergy(int amount) => amount >= 0 && TryChange(0, amount, 0);
        public bool TryIncreaseResortValue(int amount) => amount >= 0 && TryChange(0, 0, amount);

        private bool TryChange(int coins, int energy, int value)
        {
            ResourceSnapshot next;
            if (!TryCalculate(resources, coins, energy, value, out next)) return false;
            Apply(next);
            ResourcesChanged?.Invoke(resources);
            return true;
        }

        internal void Apply(ResourceSnapshot next) { resources = next; }
        internal static bool TryCalculate(ResourceSnapshot current, int coins, int energy, int value, out ResourceSnapshot next)
        {
            var c = (long)current.Coins + coins;
            var e = (long)current.Energy + energy;
            var v = (long)current.ResortValue + value;
            next = current;
            if (value < 0 || c < 0 || c > int.MaxValue || e < 0 || e > int.MaxValue || v > int.MaxValue) return false;
            next = new ResourceSnapshot((int)c, (int)e, (int)v);
            return true;
        }
    }
}
