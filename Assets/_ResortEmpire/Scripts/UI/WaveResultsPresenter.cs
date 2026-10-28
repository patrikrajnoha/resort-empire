using System.Globalization;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;
using UnityEngine;
using UnityEngine.Events;

namespace ResortEmpire.UI
{
    /// <summary>Optional thin S4 binding. Assign references in the Inspector; no layout or wallet logic lives here.</summary>
    public sealed class WaveResultsPresenter : MonoBehaviour
    {
        [SerializeField] private ResortGameplayBootstrap gameplay;
        [SerializeField] private ResultsView view;
        [SerializeField] private RewardItemView coinsReward;
        [SerializeField] private RewardItemView energyReward;
        [SerializeField] private RewardItemView resortValueReward;
        public UnityEvent<string> CommandFailed = new UnityEvent<string>();

        private void OnEnable()
        {
            if (gameplay == null || view == null) return;
            gameplay.WaveResultReady += Render;
            view.ClaimRequested.AddListener(Claim);
            Render(gameplay.CurrentWaveResult);
        }

        private void OnDisable()
        {
            if (gameplay != null) gameplay.WaveResultReady -= Render;
            if (view != null) view.ClaimRequested.RemoveListener(Claim);
        }

        private void Render(WaveResult result)
        {
            if (result == null || view == null) return;
            BuildingWaitSnapshot? bottleneck = null;
            foreach (var wait in result.Waiting)
                if (wait.TotalWait > 0 && (!bottleneck.HasValue || wait.TotalWait > bottleneck.Value.TotalWait)) bottleneck = wait;
            var feedback = bottleneck.HasValue
                ? "Longest total queue wait: " + bottleneck.Value.BuildingId + ". Consider assigning a Worker or upgrading capacity."
                : "All needs were served without queue delays.";
            view.SetResults(result.ServedUnits.ToString(CultureInfo.InvariantCulture),
                result.Satisfaction.ToString("0.#", CultureInfo.InvariantCulture) + "%", feedback, result.Stars, result.Perfect);
            SetAmount(coinsReward, result.Rewards.Coins);
            SetAmount(energyReward, result.Rewards.Energy);
            SetAmount(resortValueReward, result.Rewards.ResortValue);
            view.ClaimButton.interactable = !result.Claimed;
        }

        private void Claim()
        {
            var result = gameplay.CurrentWaveResult;
            if (result == null)
            {
                view.ResetClaim();
                CommandFailed.Invoke("No completed wave reward is available.");
                return;
            }
            var command = gameplay.TryClaimWaveReward(result.RewardId);
            Render(gameplay.CurrentWaveResult); // Re-enable on failure; keep disabled if already claimed.
            if (!command.Succeeded) CommandFailed.Invoke(command.Message);
        }

        private static void SetAmount(RewardItemView reward, int amount)
        {
            if (reward != null && reward.Amount != null) reward.Amount.text = amount.ToString(CultureInfo.InvariantCulture);
        }
    }
}
