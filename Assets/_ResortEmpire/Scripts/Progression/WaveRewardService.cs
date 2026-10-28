using System;

namespace ResortEmpire.Gameplay.Progression
{
    /// <summary>Owns the immutable result view and validates a claim against the durable reward ledger.</summary>
    public sealed class WaveRewardService
    {
        public WaveResult CurrentResult { get; private set; }
        internal void Apply(WaveResultData data) { CurrentResult = data == null ? null : new WaveResult(data); }

        internal bool TryPrepareClaim(ProgressionSaveData next, string rewardId, ResourceSnapshot resources, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(rewardId) || next.Result == null || next.Result.RewardId != rewardId)
                error = "No matching reward is available.";
            else if (next.Result.Claimed || next.ClaimedRewardIds.Contains(rewardId))
                error = "This reward has already been claimed.";
            else if (next.Phase != GamePhase.WAVE_COMPLETE)
                error = "Rewards can only be claimed after a completed wave.";
            if (error != null) return false;
            ResourceSnapshot updated;
            if (!ResourceManager.TryCalculate(resources, next.Result.Coins, next.Result.Energy, next.Result.ResortValue, out updated))
            {
                error = "The reward would exceed the resource balance limit.";
                return false;
            }
            next.Coins = updated.Coins;
            next.Energy = updated.Energy;
            next.ResortValue = updated.ResortValue;
            next.Result.Claimed = true;
            next.ClaimedRewardIds.Add(rewardId);
            next.ClaimedWaves = next.Result.WaveId;
            next.Phase = GamePhase.UPGRADE;
            return true;
        }
    }
}
