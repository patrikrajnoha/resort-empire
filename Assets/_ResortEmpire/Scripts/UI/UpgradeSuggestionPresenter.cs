using System;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>
    /// S5 binding after a claimed wave: suggests the Lv1–Lv3 capacity upgrade for the building
    /// with the longest queue. Both buttons finish the results step and return to AWAITING_WAVE.
    /// </summary>
    public sealed class UpgradeSuggestionPresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class NeedIcon
        {
            public NeedType Need;
            public Sprite Icon;
            [Tooltip("Glyph icons keep the card's tint; full-color building art is shown untinted.")]
            public bool Tint = true;
        }

        [SerializeField] private ResortGameplayBootstrap gameplay;
        [SerializeField] private Image buildingIcon;
        [SerializeField] private NeedIcon[] needIcons;
        [SerializeField] private TextMeshProUGUI buildingName;
        [SerializeField] private TextMeshProUGUI description;
        [SerializeField] private TextMeshProUGUI capacityLabel;
        [SerializeField] private TextMeshProUGUI capacityValue;
        [SerializeField] private TextMeshProUGUI costValue;
        [Tooltip("Quality and Resort Value rows describe MVP upgrades; Prototype upgrades add capacity only.")]
        [SerializeField] private GameObject[] mvpOnlyRows;
        [SerializeField] private Button upgradeButton;
        [SerializeField] private Button laterButton;
        [SerializeField] private Color unaffordableCost = new Color(0.86f, 0.24f, 0.2f);
        public UnityEvent<string> CommandFailed = new UnityEvent<string>();

        private BuildingId? suggestion;
        private Color affordableCost = Color.white;
        private Color iconTint = Color.white;
        private bool continuePending;

        private void Awake()
        {
            if (costValue != null) affordableCost = costValue.color;
            if (buildingIcon != null) iconTint = buildingIcon.color;
            foreach (var row in mvpOnlyRows)
                if (row != null) row.SetActive(false);
        }

        private void OnEnable()
        {
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (gameplay == null) return;
            gameplay.ResourcesChanged += HandleResourcesChanged;
            if (upgradeButton != null) upgradeButton.onClick.AddListener(Upgrade);
            if (laterButton != null) laterButton.onClick.AddListener(Continue);
            Render();
        }

        private void OnDisable()
        {
            continuePending = false;
            if (gameplay == null) return;
            gameplay.ResourcesChanged -= HandleResourcesChanged;
            if (upgradeButton != null) upgradeButton.onClick.RemoveListener(Upgrade);
            if (laterButton != null) laterButton.onClick.RemoveListener(Continue);
        }

        // This window opens inside the claim transaction's notification, where a nested
        // command would be rejected, so an empty suggestion continues on the next frame.
        private void Update()
        {
            if (!continuePending) return;
            continuePending = false;
            Continue();
        }

        private void HandleResourcesChanged(ResourceSnapshot resources) => Render();

        private void Render()
        {
            // Outside UPGRADE (for example an F1 preview) the static layout stays untouched.
            if (gameplay.CurrentPhase != GamePhase.UPGRADE) return;
            suggestion = ChooseSuggestion();
            if (!suggestion.HasValue)
            {
                continuePending = true;
                return;
            }

            var building = Find(suggestion.Value).Value;
            var cost = building.UpgradeCost.Value;
            var affordable = gameplay.Resources.Coins >= cost;
            SetText(buildingName, ResortUiText.BuildingName(building.BuildingId));
            foreach (var icon in needIcons)
            {
                if (icon.Need != building.NeedType || buildingIcon == null) continue;
                buildingIcon.sprite = icon.Icon;
                buildingIcon.preserveAspect = true;
                buildingIcon.color = icon.Tint ? iconTint : Color.white;
            }
            SetText(description, affordable ? "More seats shorten the queue!" : "Earn more Coins to upgrade.");
            SetText(capacityLabel, ResortUiText.NeedName(building.NeedType) + " Capacity");
            SetText(capacityValue, building.Capacity + " -> <color=#00C896>" + building.NextCapacity.Value + "</color>");
            SetText(costValue, ResortUiText.Number(cost));
            if (costValue != null) costValue.color = affordable ? affordableCost : unaffordableCost;
            if (upgradeButton != null) upgradeButton.interactable = affordable;
        }

        // Longest total queue wait first; otherwise the lowest-level upgradable building.
        private BuildingId? ChooseSuggestion()
        {
            BuildingId? choice = null;
            var longestWait = 0d;
            var result = gameplay.CurrentWaveResult;
            if (result != null)
            {
                foreach (var wait in result.Waiting)
                {
                    var building = Find(wait.BuildingId);
                    if (wait.TotalWait > longestWait && building.HasValue && building.Value.UpgradeCost.HasValue)
                    {
                        longestWait = wait.TotalWait;
                        choice = wait.BuildingId;
                    }
                }
            }
            if (choice.HasValue) return choice;

            var lowestLevel = int.MaxValue;
            foreach (var building in gameplay.Buildings)
            {
                if (!building.UpgradeCost.HasValue || building.Level >= lowestLevel) continue;
                lowestLevel = building.Level;
                choice = building.BuildingId;
            }
            return choice;
        }

        private BuildingStateSnapshot? Find(BuildingId id)
        {
            foreach (var building in gameplay.Buildings)
                if (building.BuildingId == id) return building;
            return null;
        }

        private void Upgrade()
        {
            if (!suggestion.HasValue) return;
            var command = gameplay.TryUpgradeBuilding(suggestion.Value);
            if (!command.Succeeded)
            {
                Fail(command.Message);
                return;
            }
            Continue();
        }

        private void Continue()
        {
            if (gameplay.CurrentPhase != GamePhase.UPGRADE) return;
            var command = gameplay.TryContinueAfterResults();
            if (!command.Succeeded) Fail(command.Message);
        }

        private void Fail(string message)
        {
            Debug.LogWarning(message);
            SetText(description, message);
            CommandFailed.Invoke(message);
        }

        private static void SetText(TextMeshProUGUI label, string value)
        {
            if (label != null) label.text = value;
        }
    }
}
