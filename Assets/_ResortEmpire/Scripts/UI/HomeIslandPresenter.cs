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
    /// S1 binding: resources, wave status and forecast, Start Wave, and the building and plot
    /// markers that open S3 and S9. Purchase entry points are hidden outside AWAITING_WAVE.
    /// </summary>
    public sealed class HomeIslandPresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class PlotMarkerTarget
        {
            public BuildingId Building;
            public Transform Plot;
        }

        [SerializeField] private ResortGameplayBootstrap gameplay;
        [SerializeField] private ResortUiStateCoordinator navigation;
        [SerializeField] private TextMeshProUGUI coinsText;
        [SerializeField] private TextMeshProUGUI energyText;
        [SerializeField] private TextMeshProUGUI resortValueText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button startWaveButton;
        [SerializeField] private TextMeshProUGUI startWaveText;
        [Tooltip("VIP visits are MVP-only and stay hidden in the Prototype.")]
        [SerializeField] private GameObject vipEntry;
        [SerializeField] private Button[] buildingMarkers;
        [SerializeField] private Button unlockAreaButton;
        [SerializeField] private WorldAnchoredUiMarker unlockAreaMarker;
        [SerializeField] private PlotMarkerTarget[] plots;
        public UnityEvent<string> CommandFailed = new UnityEvent<string>();

        private void OnEnable()
        {
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (navigation == null) navigation = FindAnyObjectByType<ResortUiStateCoordinator>();
            if (vipEntry != null) vipEntry.SetActive(false);
            if (gameplay == null) return;
            gameplay.StateChanged += HandleStateChanged;
            gameplay.ResourcesChanged += HandleResourcesChanged;
            gameplay.BuildingsChanged += HandleBuildingsChanged;
            gameplay.WaveResultReady += HandleWaveResultReady;
            if (startWaveButton != null) startWaveButton.onClick.AddListener(StartWave);
            if (unlockAreaButton != null) unlockAreaButton.onClick.AddListener(OpenAreaUnlock);
            foreach (var marker in buildingMarkers)
                if (marker != null) marker.onClick.AddListener(OpenWorkerAllocation);
            Refresh();
        }

        // Bootstrap Awake may run after this OnEnable; Start sees the configured runtime.
        private void Start() => Refresh();

        private void OnDisable()
        {
            if (gameplay == null) return;
            gameplay.StateChanged -= HandleStateChanged;
            gameplay.ResourcesChanged -= HandleResourcesChanged;
            gameplay.BuildingsChanged -= HandleBuildingsChanged;
            gameplay.WaveResultReady -= HandleWaveResultReady;
            if (startWaveButton != null) startWaveButton.onClick.RemoveListener(StartWave);
            if (unlockAreaButton != null) unlockAreaButton.onClick.RemoveListener(OpenAreaUnlock);
            foreach (var marker in buildingMarkers)
                if (marker != null) marker.onClick.RemoveListener(OpenWorkerAllocation);
        }

        private void HandleStateChanged(GamePhase previous, GamePhase next) => Refresh();
        private void HandleResourcesChanged(ResourceSnapshot resources) => Refresh();
        private void HandleBuildingsChanged(IReadOnlyList<BuildingStateSnapshot> buildings) => Refresh();
        private void HandleWaveResultReady(WaveResult result) => Refresh();

        private void Refresh()
        {
            if (gameplay == null) return;
            var resources = gameplay.Resources;
            SetText(coinsText, ResortUiText.Number(resources.Coins));
            SetText(energyText, ResortUiText.Number(resources.Energy));
            SetText(resortValueText, "Resort Value: " + ResortUiText.Number(resources.ResortValue));

            var phase = gameplay.CurrentPhase;
            var forecast = gameplay.NextWaveForecast;
            SetText(statusText, StatusFor(phase, forecast));
            if (startWaveButton != null)
                startWaveButton.gameObject.SetActive(gameplay.IsConfigured && phase == GamePhase.AWAITING_WAVE && forecast.HasWave);
            if (forecast.HasWave) SetText(startWaveText, "START WAVE " + forecast.WaveId);

            var plot = phase == GamePhase.AWAITING_WAVE ? FindEligiblePlot() : null;
            if (unlockAreaButton != null) unlockAreaButton.gameObject.SetActive(plot != null);
            if (plot != null && unlockAreaMarker != null && unlockAreaMarker.worldTarget != plot)
                unlockAreaMarker.Configure(plot, unlockAreaMarker.worldCamera, unlockAreaMarker.targetCanvas, unlockAreaMarker.worldOffset);
        }

        private string StatusFor(GamePhase phase, WaveForecast forecast)
        {
            if (!gameplay.IsConfigured) return "NOT READY";
            switch (phase)
            {
                case GamePhase.AWAITING_WAVE:
                    // The wave number lives on the Start button; the pill only fits the forecast.
                    return forecast.HasWave ? "NEXT: " + forecast.DemandTotal + " GUESTS" : "ALL " + gameplay.TotalWaves + " WAVES DONE";
                case GamePhase.WAVE_INCOMING: return "GUESTS ARRIVING";
                case GamePhase.WAVE_COMPLETE: return "WAVE COMPLETE";
                case GamePhase.UPGRADE: return "TIME TO UPGRADE";
                default: return "WAVE IN PROGRESS";
            }
        }

        private Transform FindEligiblePlot()
        {
            foreach (var building in gameplay.Buildings)
            {
                if (!building.IsEligible || building.IsOwned) continue;
                foreach (var plot in plots)
                    if (plot != null && plot.Building == building.BuildingId) return plot.Plot;
            }
            return null;
        }

        private void StartWave()
        {
            var command = gameplay.TryStartWave();
            if (command.Succeeded) return;
            Debug.LogWarning(command.Message);
            CommandFailed.Invoke(command.Message);
        }

        private void OpenWorkerAllocation()
        {
            if (navigation != null) navigation.OpenWorkerAllocation();
        }

        private void OpenAreaUnlock()
        {
            if (navigation != null) navigation.OpenAreaUnlock();
        }

        private static void SetText(TextMeshProUGUI label, string value)
        {
            if (label != null) label.text = value;
        }
    }
}
