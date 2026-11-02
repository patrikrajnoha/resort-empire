using System;
using System.Collections.Generic;
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
    /// S9 binding for one data-driven area: an eligible plot is purchased with Coins and arrives with
    /// its need and one assigned Worker. LATER keeps the plot purchasable from its island marker.
    /// </summary>
    public sealed class AreaUnlockPresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class AreaContent
        {
            public BuildingId Building;
            public string Title;
            public Sprite Illustration;
        }

        [SerializeField] private ResortGameplayBootstrap gameplay;
        [SerializeField] private TextMeshProUGUI title;
        [SerializeField] private TextMeshProUGUI demandLine;
        [SerializeField] private TextMeshProUGUI workerLine;
        [SerializeField] private TextMeshProUGUI cost;
        [SerializeField] private Image illustration;
        [SerializeField] private Button unlockButton;
        [SerializeField] private Button laterButton;
        [SerializeField] private AreaContent[] areas;
        [SerializeField] private Color unaffordableCost = new Color(0.86f, 0.24f, 0.2f);
        public UnityEvent<string> CommandFailed = new UnityEvent<string>();

        private BuildingId? offered;
        private Color affordableCost = Color.white;
        private bool closePending;

        private void Awake()
        {
            if (cost != null) affordableCost = cost.color;
        }

        // Render can run during this object's own activation, when SetActive(false) is not allowed.
        private void Update()
        {
            if (closePending) Close();
        }

        private void OnEnable()
        {
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (unlockButton != null) unlockButton.onClick.AddListener(Unlock);
            if (laterButton != null) laterButton.onClick.AddListener(Close);
            if (gameplay == null) return;
            gameplay.ResourcesChanged += HandleResourcesChanged;
            gameplay.BuildingsChanged += HandleBuildingsChanged;
            Render();
        }

        private void OnDisable()
        {
            if (unlockButton != null) unlockButton.onClick.RemoveListener(Unlock);
            if (laterButton != null) laterButton.onClick.RemoveListener(Close);
            if (gameplay == null) return;
            gameplay.ResourcesChanged -= HandleResourcesChanged;
            gameplay.BuildingsChanged -= HandleBuildingsChanged;
        }

        private void HandleResourcesChanged(ResourceSnapshot resources) => Render();
        private void HandleBuildingsChanged(IReadOnlyList<BuildingStateSnapshot> buildings) => Render();

        private void Render()
        {
            offered = null;
            foreach (var building in gameplay.Buildings)
            {
                if (!building.IsEligible || building.IsOwned) continue;
                var content = ContentFor(building.BuildingId);
                if (content == null) continue;
                offered = building.BuildingId;
                var affordable = gameplay.Resources.Coins >= building.PurchaseCost;
                SetText(title, content.Title);
                SetText(demandLine, "Adds: " + ResortUiText.NeedName(building.NeedType) + " Demand");
                SetText(workerLine, "Includes 1 extra Worker");
                SetText(cost, ResortUiText.Number(building.PurchaseCost) + " Coins");
                if (cost != null) cost.color = affordable ? affordableCost : unaffordableCost;
                if (illustration != null && content.Illustration != null)
                {
                    illustration.sprite = content.Illustration;
                    illustration.preserveAspect = true;
                }
                if (unlockButton != null) unlockButton.interactable = affordable;
                return;
            }
            // Nothing left to unlock: close in play, but leave an F1 preview's static layout alone.
            closePending = gameplay.IsConfigured && gameplay.CurrentPhase == GamePhase.AWAITING_WAVE;
        }

        private AreaContent ContentFor(BuildingId building)
        {
            foreach (var area in areas)
                if (area.Building == building) return area;
            return null;
        }

        private void Unlock()
        {
            if (!offered.HasValue) return;
            var command = gameplay.TryPurchaseBuilding(offered.Value);
            if (command.Succeeded)
            {
                Close();
                return;
            }
            Debug.LogWarning(command.Message);
            SetText(cost, command.Message);
            CommandFailed.Invoke(command.Message);
        }

        private void Close()
        {
            closePending = false;
            gameObject.SetActive(false);
        }

        private static void SetText(TextMeshProUGUI label, string value)
        {
            if (label != null) label.text = value;
        }
    }
}
