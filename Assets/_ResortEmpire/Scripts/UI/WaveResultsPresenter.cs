using System.Globalization;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;
using TMPro;
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
        [SerializeField] private TextMeshProUGUI warning;
        [SerializeField] private GameObject warningIcon;
        [Tooltip("Decorative composition for static previews; the live island shows through at runtime.")]
        [SerializeField] private GameObject staticBackdrop;
        public UnityEvent<string> CommandFailed = new UnityEvent<string>();

        private void OnEnable()
        {
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (gameplay == null || view == null) return;
            if (staticBackdrop != null) staticBackdrop.SetActive(false);
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
            BuildingWaitSnapshot? best = null;
            foreach (var wait in result.Waiting)
            {
                if (wait.TotalWait > 0 && (!bottleneck.HasValue || wait.TotalWait > bottleneck.Value.TotalWait)) bottleneck = wait;
                if (!best.HasValue || wait.AverageWait < best.Value.AverageWait) best = wait;
            }
            var feedback = !bottleneck.HasValue
                ? "No guest had to wait!"
                : best.HasValue && best.Value.BuildingId != bottleneck.Value.BuildingId
                    ? ResortUiText.NeedName(BuildingNeedMapping.GetNeedType(best.Value.BuildingId)) + " went great!"
                    : "Guests enjoyed their stay!";
            view.SetResults(result.ServedUnits.ToString(CultureInfo.InvariantCulture) + "/" + result.ServedUnits.ToString(CultureInfo.InvariantCulture),
                result.Satisfaction.ToString("0.#", CultureInfo.InvariantCulture) + "%", feedback, result.Stars, result.Perfect);
            if (view.Heading != null) view.Heading.text = "WAVE " + result.WaveId + " COMPLETE!";
            // The recommendation names the longest total queue wait, the wave's clearest bottleneck.
            if (warning != null)
            {
                warning.gameObject.SetActive(bottleneck.HasValue);
                if (bottleneck.HasValue) warning.text = ResortUiText.BuildingName(bottleneck.Value.BuildingId) + " had the longest queue";
            }
            if (warningIcon != null) warningIcon.SetActive(bottleneck.HasValue);
            SetAmount(coinsReward, result.Rewards.Coins, "Coins");
            SetAmount(energyReward, result.Rewards.Energy, "Energy");
            SetAmount(resortValueReward, result.Rewards.ResortValue, "Resort Value");
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

        private static void SetAmount(RewardItemView reward, int amount, string label)
        {
            if (reward != null && reward.Amount != null)
                reward.Amount.text = "+" + amount.ToString(CultureInfo.InvariantCulture) + " " + label;
        }
    }
}
