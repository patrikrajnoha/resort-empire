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
        [SerializeField] private TextMeshProUGUI headerLevel;
        [SerializeField] private TextMeshProUGUI capacityPreview;
        [SerializeField] private TextMeshProUGUI upgradeButtonLabel;

        private BuildingId? selectedBuilding;
        private bool upgradePending;

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
            if (isActiveAndEnabled) UnbindEvents();
            gameplay = runtime;
            buildingName = nameLabel;
            levelText = levelLabel;
            workersText = workerLabel;
            nextUpgradeText = nextLabel;
            upgradeCostText = costLabel;
            messageText = statusLabel;
            upgradeButton = upgrade;
            closeButton = close;
            AutoBindUi();
            if (isActiveAndEnabled)
            {
                BindEvents();
                Refresh();
            }
        }

        private void OnEnable()
        {
            if (!selectedBuilding.HasValue)
            {
                gameObject.SetActive(false);
                return;
            }

            AutoBindUi();
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            BindEvents();
            Refresh();
        }

        private void BindEvents()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (upgradeButton != null) upgradeButton.onClick.AddListener(Upgrade);
            if (gameplay != null)
            {
                gameplay.ResourcesChanged += HandleResourcesChanged;
                gameplay.BuildingsChanged += HandleBuildingsChanged;
                gameplay.StateChanged += HandleStateChanged;
                gameplay.WorkerAssignmentChanged += HandleWorkerAssignmentChanged;
            }
        }

        private void OnDisable()
        {
            upgradePending = false;
            UnbindEvents();
        }

        private void UnbindEvents()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
            if (upgradeButton != null) upgradeButton.onClick.RemoveListener(Upgrade);
            if (gameplay == null) return;
            gameplay.ResourcesChanged -= HandleResourcesChanged;
            gameplay.BuildingsChanged -= HandleBuildingsChanged;
            gameplay.StateChanged -= HandleStateChanged;
            gameplay.WorkerAssignmentChanged -= HandleWorkerAssignmentChanged;
        }

        public void Open(BuildingId building)
        {
            selectedBuilding = building;
            if (gameObject.activeInHierarchy) Refresh();
            else gameObject.SetActive(true);
        }

        public void Close()
        {
            selectedBuilding = null;
            gameObject.SetActive(false);
        }

        private void HandleResourcesChanged(ResourceSnapshot _) => Refresh();
        private void HandleBuildingsChanged(System.Collections.Generic.IReadOnlyList<BuildingStateSnapshot> _) => Refresh();
        private void HandleStateChanged(GamePhase _, GamePhase __) => Refresh();
        private void HandleWorkerAssignmentChanged(WorkerAssignmentChange _) => Refresh();

        private void Refresh()
        {
            var building = selectedBuilding.HasValue && gameplay != null ? FindBuilding(selectedBuilding.Value) : null;
            if (!building.HasValue || !building.Value.IsOwned)
            {
                SetText(buildingName, "Building");
                SetText(headerLevel, string.Empty);
                SetText(levelText, "—");
                SetText(workersText, "—");
                SetText(capacityText, "—");
                SetText(nextUpgradeText, "—");
                SetText(upgradeCostText, "—");
                SetCapacityPreview(string.Empty);
                SetStatus(selectedBuilding.HasValue ? "Building data unavailable" : string.Empty);
                SetUpgradeAction("UPGRADE", false);
                return;
            }

            var state = building.Value;
            var hasSeparateLabels = headerLevel != null;
            SetText(buildingName, ResortUiText.BuildingName(state.BuildingId));
            SetText(headerLevel, "LEVEL " + state.Level);
            SetText(levelText, (hasSeparateLabels ? "" : "Level: ") + state.Level);
            SetText(workersText, (hasSeparateLabels ? "" : "Workers: ") +
                AssignedWorkers(state.BuildingId) + " / " + state.WorkerSlotCount);

            var hasNextLevel = state.UpgradeCost.HasValue;
            var betweenWaves = gameplay.CurrentPhase == GamePhase.AWAITING_WAVE || gameplay.CurrentPhase == GamePhase.UPGRADE;
            var capacity = ResortUiText.Number(state.Capacity);
            var nextCapacity = state.NextCapacity.HasValue ? ResortUiText.Number(state.NextCapacity.Value) : null;
            var hasCapacityGain = nextCapacity != null && state.NextCapacity != state.Capacity;
            var cost = state.UpgradeCost.GetValueOrDefault();
            var missingCoins = Mathf.Max(0, cost - gameplay.Resources.Coins);

            SetText(nextUpgradeText, hasNextLevel
                ? (hasSeparateLabels ? "Level " : "Next Upgrade: Level ") + (state.Level + 1)
                : hasSeparateLabels ? "Max level" : "Maximum Level Reached");
            SetText(capacityText, hasSeparateLabels ? capacity :
                "Capacity: " + capacity + (hasCapacityGain ? " to " + nextCapacity : string.Empty));
            SetCapacityPreview(hasCapacityGain ? "Capacity " + capacity + " to " + nextCapacity : string.Empty);
            SetText(upgradeCostText, hasNextLevel
                ? (hasSeparateLabels ? "" : "Upgrade Cost: ") + ResortUiText.Number(cost) + " coins"
                : hasSeparateLabels ? "—" : "Upgrade is not available");

            var status = string.Empty;
            if (hasNextLevel && !betweenWaves)
                status = "Available between waves";
            else if (hasNextLevel && missingCoins > 0)
                status = "Need " + ResortUiText.Number(missingCoins) + " more coins";
            SetStatus(status);
            SetUpgradeAction(hasNextLevel ? "UPGRADE" : "MAX LEVEL",
                hasNextLevel && betweenWaves && missingCoins == 0 && !upgradePending);
        }

        // Older instances and Configure callers may still rely on the original hierarchy.
        private void AutoBindUi()
        {
            if (buildingName == null) buildingName = FindText("BuildingNameText");
            if (levelText == null) levelText = FindTextUnder("Icon");
            if (workersText == null) workersText = FindTextUnder("Icon (1)");
            if (nextUpgradeText == null) nextUpgradeText = FindTextUnder("Icon (2)");
            if (capacityText == null) capacityText = FindTextUnder("Icon (3)");
            if (upgradeCostText == null) upgradeCostText = FindTextUnder("Icon (4)");

            if (upgradeButton == null)
                upgradeButton = FindComponent<Button>("Buttons/UpgradeButton");

            if (closeButton == null)
            {
                closeButton = FindComponent<Button>("CancelButton");
                if (closeButton == null)
                {
                    var cancelObject = transform.Find("CancelButton");
                    if (cancelObject != null)
                    {
                        closeButton = cancelObject.gameObject.AddComponent<Button>();
                        closeButton.targetGraphic = cancelObject.GetComponent<Graphic>();
                    }
                }
            }
        }

        private TextMeshProUGUI FindText(string childPath)
        {
            var child = transform.Find(childPath);
            return child == null ? null : child.GetComponent<TextMeshProUGUI>();
        }

        private TextMeshProUGUI FindTextUnder(string childPath)
        {
            var child = transform.Find(childPath);
            return child == null ? null : child.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        private T FindComponent<T>(string childPath) where T : Component
        {
            var child = transform.Find(childPath);
            return child == null ? null : child.GetComponent<T>();
        }

        private void Upgrade()
        {
            if (upgradePending || !selectedBuilding.HasValue || gameplay == null ||
                (upgradeButton != null && !upgradeButton.IsInteractable())) return;
            upgradePending = true;
            if (upgradeButton != null) upgradeButton.interactable = false;
            var result = gameplay.TryUpgradeBuilding(selectedBuilding.Value);
            upgradePending = false;
            Refresh();
            if (!result.Succeeded)
            {
                SetStatus(result.Message);
                if (messageText == null)
                    Debug.LogWarning("BuildingInfoPanel: upgrade failed - " + result.Message, this);
            }
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

        private void SetCapacityPreview(string value)
        {
            if (capacityPreview == null) return;
            capacityPreview.text = value;
            capacityPreview.gameObject.SetActive(value.Length > 0);
        }

        private void SetStatus(string value)
        {
            if (messageText == null) return;
            messageText.text = value;
            if (headerLevel != null)
                messageText.gameObject.SetActive(!string.IsNullOrEmpty(value));
        }

        private void SetUpgradeAction(string label, bool available)
        {
            if (upgradeButton != null) upgradeButton.interactable = available;
            if (upgradeButtonLabel == null) return;
            upgradeButtonLabel.text = label;
            upgradeButtonLabel.color = available ? Color.white : new Color32(64, 52, 36, 255);
        }

        private static void SetText(TextMeshProUGUI label, string value) { if (label != null) label.text = value; }
    }
}
