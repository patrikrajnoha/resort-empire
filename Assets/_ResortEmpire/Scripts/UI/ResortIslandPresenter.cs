using System;
using System.Collections.Generic;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>
    /// Island art for building state: owned buildings show their level art, eligible plots show
    /// a construction site, and locked plots stay hidden.
    /// </summary>
    public sealed class ResortIslandPresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class BuildingVisual
        {
            public BuildingId Building;
            public GameObject Root;
            [Tooltip("The building image and any shadow that copies its silhouette.")]
            public Image[] Images;
            [Tooltip("Level 1 to level 3 art.")]
            public Sprite[] LevelSprites;
            public Sprite PlotSprite;
        }

        [SerializeField] private ResortGameplayBootstrap gameplay;
        [SerializeField] private BuildingVisual[] buildings;

        private void OnEnable()
        {
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (gameplay == null) return;
            gameplay.BuildingsChanged += Render;
            Render(gameplay.Buildings);
        }

        // Bootstrap Awake may run after this OnEnable; Start sees the configured runtime.
        private void Start()
        {
            if (gameplay != null) Render(gameplay.Buildings);
        }

        private void OnDisable()
        {
            if (gameplay != null) gameplay.BuildingsChanged -= Render;
        }

        private void Render(IReadOnlyList<BuildingStateSnapshot> states)
        {
            // An unconfigured runtime reports no buildings; the authored scene stays as it is.
            foreach (var state in states)
            {
                var visual = Find(state.BuildingId);
                if (visual == null) continue;
                var sprite = state.IsOwned && visual.LevelSprites.Length > 0
                    ? visual.LevelSprites[Mathf.Clamp(state.Level, 1, visual.LevelSprites.Length) - 1]
                    : state.IsEligible ? visual.PlotSprite : null;
                if (visual.Root != null) visual.Root.SetActive(sprite != null);
                if (sprite == null) continue;
                foreach (var image in visual.Images)
                    if (image != null) image.sprite = sprite;
            }
        }

        private BuildingVisual Find(BuildingId building)
        {
            foreach (var visual in buildings)
                if (visual.Building == building) return visual;
            return null;
        }
    }
}
