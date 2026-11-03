using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>Reusable S1 building detail window backed by the canonical gameplay facade.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingInfoPanelPresenter : MonoBehaviour
    {
        [SerializeField] private ResortGameplayBootstrap gameplay;
        [SerializeField] private TextMeshProUGUI buildingName;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI workersText;
        [SerializeField] private TextMeshProUGUI capacityText;
        [SerializeField] private TextMeshProUGUI nextUpgradeText;
        [SerializeField] private TextMeshProUGUI upgradeCostText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button upgradeButton;
        [SerializeField] private Button closeButton;

        private BuildingId? selectedBuilding;
        private bool upgradePending;
        private bool buttonsWired;

        public void Configure(
            ResortGameplayBootstrap runtime,
            TextMeshProUGUI nameLabel,
            TextMeshProUGUI levelLabel,
            TextMeshProUGUI workerLabel,
            TextMeshProUGUI nextLabel,
            TextMeshProUGUI costLabel,
            TextMeshProUGUI statusLabel,
            Button upgrade,
            Button close)
        {
            gameplay = runtime;
            buildingName = nameLabel;
            levelText = levelLabel;
            workersText = workerLabel;
            nextUpgradeText = nextLabel;
            upgradeCostText = costLabel;
            messageText = statusLabel;
            upgradeButton = upgrade;
            closeButton = close;
            WireButtons();
        }

        private void Awake() => WireButtons();

        private void WireButtons()
        {
            if (buttonsWired) return;
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (upgradeButton != null) upgradeButton.onClick.AddListener(Upgrade);
            buttonsWired = closeButton != null || upgradeButton != null;
        }

        private void OnEnable()
        {
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (gameplay != null)
            {
                gameplay.ResourcesChanged += HandleResourcesChanged;
                gameplay.BuildingsChanged += HandleBuildingsChanged;
                gameplay.StateChanged += HandleStateChanged;
            }
            Refresh();
        }

        private void OnDisable()
        {
            upgradePending = false;
            if (gameplay == null) return;
            gameplay.ResourcesChanged -= HandleResourcesChanged;
            gameplay.BuildingsChanged -= HandleBuildingsChanged;
            gameplay.StateChanged -= HandleStateChanged;
        }

        public void Open(BuildingId building)
        {
            selectedBuilding = building;
            gameObject.SetActive(true);
            Debug.Log("BuildingInfoPanel: opened for " + building + ".", this);
            Refresh();
        }

        public void Close()
        {
            selectedBuilding = null;
            gameObject.SetActive(false);
            Debug.Log("BuildingInfoPanel: closed.", this);
        }

        private void HandleResourcesChanged(ResourceSnapshot _) => Refresh();
        private void HandleBuildingsChanged(System.Collections.Generic.IReadOnlyList<BuildingStateSnapshot> _) => Refresh();
        private void HandleStateChanged(GamePhase _, GamePhase __) => Refresh();

        private void Refresh()
        {
            if (!selectedBuilding.HasValue || gameplay == null) return;
            var building = FindBuilding(selectedBuilding.Value);
            if (!building.HasValue) return;

            var state = building.Value;
            SetText(buildingName, ResortUiText.BuildingName(state.BuildingId));
            SetText(levelText, "Level: " + state.Level);
            var workers = AssignedWorkers(state.BuildingId);
            SetText(workersText, "Workers: " + workers + " / " + WorkerCapacity(state));

            var hasNextLevel = state.UpgradeCost.HasValue;
            var betweenWaves = gameplay.CurrentPhase == GamePhase.AWAITING_WAVE || gameplay.CurrentPhase == GamePhase.UPGRADE;
            var upgradeAvailable = hasNextLevel && betweenWaves;
            SetText(nextUpgradeText, hasNextLevel ? "Next Upgrade: Level " + (state.Level + 1) : "Maximum Level Reached");
            SetText(capacityText, hasNextLevel
                ? "Capacity: " + state.Capacity + " -> " + state.NextCapacity.Value
                : "Capacity: " + state.Capacity);
            if (upgradeAvailable)
            {
                var cost = state.UpgradeCost.Value;
                var missing = Mathf.Max(0, cost - gameplay.Resources.Coins);
                SetText(upgradeCostText, "Upgrade Cost: " + ResortUiText.Number(cost) + " Coins");
                SetText(messageText, missing > 0 ? "Missing: " + ResortUiText.Number(missing) + " Coins" : string.Empty);
                if (upgradeButton != null) upgradeButton.interactable = missing == 0 && !upgradePending;
            }
            else
            {
                SetText(upgradeCostText, "Upgrade is not available");
                SetText(messageText, string.Empty);
                if (upgradeButton != null) upgradeButton.interactable = false;
            }
        }

        private void Upgrade()
        {
            if (upgradePending || !selectedBuilding.HasValue || gameplay == null) return;
            upgradePending = true;
            if (upgradeButton != null) upgradeButton.interactable = false;
            var result = gameplay.TryUpgradeBuilding(selectedBuilding.Value);
            upgradePending = false;
            if (!result.Succeeded) SetText(messageText, result.Message);
            Refresh();
        }

        private BuildingStateSnapshot? FindBuilding(BuildingId id)
        {
            foreach (var building in gameplay.Buildings)
                if (building.BuildingId == id) return building;
            return null;
        }

        private int AssignedWorkers(BuildingId id)
        {
            var count = 0;
            foreach (var assignment in gameplay.CurrentWorkerAssignments)
                if (assignment.BuildingId == id) count++;
            return count;
        }

        private static int WorkerCapacity(BuildingStateSnapshot state) => state.IsOwned ? 1 : 0;
        private static void SetText(TextMeshProUGUI label, string value) { if (label != null) label.text = value; }
    }
}
