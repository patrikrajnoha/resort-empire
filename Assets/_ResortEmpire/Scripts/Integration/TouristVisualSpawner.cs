using System.Collections.Generic;
using ResortEmpire.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// Presentation-only tourist visuals for active wave phases.
    /// Tourists are pooled UI images and never participate in gameplay simulation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TouristVisualSpawner : MonoBehaviour
    {
        private static readonly Vector2[] DefaultSpawnPositions =
        {
            new Vector2(-420f, -170f),
            new Vector2(-190f, 80f),
            new Vector2(60f, -40f),
            new Vector2(270f, 150f),
            new Vector2(420f, -120f),
            new Vector2(120f, 230f)
        };

        [SerializeField]
        private ResortGameplayBootstrap gameplayBootstrap;

        [SerializeField]
        private RectTransform touristContainer;

        [SerializeField]
        private GameObject touristPrefab;

        [SerializeField, Range(3, 6)]
        private int touristCount = 5;

        [SerializeField]
        private Vector2[] spawnPositions =
        {
            new Vector2(-420f, -170f),
            new Vector2(-190f, 80f),
            new Vector2(60f, -40f),
            new Vector2(270f, 150f),
            new Vector2(420f, -120f),
            new Vector2(120f, 230f)
        };

        [SerializeField, Min(0f)]
        private float waypointSpeed = 0.2f;

        [SerializeField, Min(0f)]
        private float idleBobHeight = 7f;

        [SerializeField, Min(0f)]
        private float idleBobSpeed = 1.5f;

        private readonly List<GameObject> pooledTourists = new List<GameObject>();
        private IResortGameplayUiFacade gameplayFacade;
        private bool subscribed;

        /// <summary>Number of tourist images currently visible in the active wave.</summary>
        public int ActiveTouristCount { get; private set; }

        private void Awake()
        {
            ResolveSceneReferences();
            NormalizeConfiguration();
        }

        private void OnEnable()
        {
            BindToGameplay();
            SynchronizeImmediately();
        }

        private void Update()
        {
            if (ActiveTouristCount == 0)
                return;

            AnimateTourists();
        }

        private void OnDisable()
        {
            UnbindFromGameplay();
            DeactivateTourists();
        }

        private void OnDestroy()
        {
            UnbindFromGameplay();
        }

        private void ResolveSceneReferences()
        {
            if (gameplayBootstrap == null)
                gameplayBootstrap = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (touristContainer == null)
                touristContainer = transform as RectTransform;
        }

        private void NormalizeConfiguration()
        {
            touristCount = Mathf.Clamp(touristCount, 3, 6);
            if (spawnPositions == null || spawnPositions.Length == 0)
                spawnPositions = DefaultSpawnPositions;
        }

        private void BindToGameplay()
        {
            if (subscribed)
                return;

            ResolveSceneReferences();
            if (gameplayBootstrap == null)
                return;

            gameplayFacade = gameplayBootstrap;
            gameplayFacade.StateChanged += HandleStateChanged;
            subscribed = true;
        }

        private void UnbindFromGameplay()
        {
            if (!subscribed || gameplayFacade == null)
                return;

            gameplayFacade.StateChanged -= HandleStateChanged;
            gameplayFacade = null;
            subscribed = false;
        }

        private void SynchronizeImmediately()
        {
            SetWaveVisibility(gameplayFacade != null && IsWavePhase(gameplayFacade.CurrentPhase));
        }

        private void HandleStateChanged(GamePhase previousState, GamePhase nextState)
        {
            SetWaveVisibility(IsWavePhase(nextState));
        }

        private void SetWaveVisibility(bool visible)
        {
            if (visible)
            {
                SpawnTouristsIfNeeded();
                return;
            }

            DeactivateTourists();
        }

        private void SpawnTouristsIfNeeded()
        {
            if (ActiveTouristCount > 0)
                return;

            if (touristContainer == null || touristPrefab == null)
            {
                Debug.LogWarning("Tourist visuals cannot spawn because the container or prefab is missing.", this);
                return;
            }

            EnsurePool(touristCount);
            for (var index = 0; index < touristCount; index++)
            {
                var tourist = pooledTourists[index];
                var rectTransform = tourist.GetComponent<RectTransform>();
                rectTransform.anchoredPosition = GetSpawnPosition(index);
                rectTransform.localScale = Vector3.one;
                rectTransform.localRotation = Quaternion.identity;
                tourist.transform.SetAsLastSibling();
                tourist.SetActive(true);
            }

            ActiveTouristCount = touristCount;
        }

        private void EnsurePool(int requiredCount)
        {
            while (pooledTourists.Count < requiredCount)
            {
                var tourist = Instantiate(touristPrefab, touristContainer, false);
                tourist.name = "TouristVisual_" + pooledTourists.Count;
                tourist.SetActive(false);

                var image = tourist.GetComponent<Image>();
                if (image != null)
                    image.raycastTarget = false;

                pooledTourists.Add(tourist);
            }
        }

        private void DeactivateTourists()
        {
            for (var index = 0; index < pooledTourists.Count; index++)
            {
                if (pooledTourists[index] != null)
                    pooledTourists[index].SetActive(false);
            }

            ActiveTouristCount = 0;
        }

        private void AnimateTourists()
        {
            var positionCount = spawnPositions.Length;
            var now = Time.unscaledTime;
            for (var index = 0; index < ActiveTouristCount; index++)
            {
                var tourist = pooledTourists[index];
                if (tourist == null)
                    continue;

                var rectTransform = tourist.GetComponent<RectTransform>();
                var start = GetSpawnPosition(index);
                var end = GetSpawnPosition((index + 1) % positionCount);
                var waypoint = Mathf.PingPong((now + index * 0.75f) * waypointSpeed, 1f);
                var position = Vector2.Lerp(start, end, waypoint);
                position.y += Mathf.Sin((now + index * 0.5f) * idleBobSpeed) * idleBobHeight;
                rectTransform.anchoredPosition = position;
            }
        }

        private Vector2 GetSpawnPosition(int index)
        {
            if (spawnPositions == null || spawnPositions.Length == 0)
                return DefaultSpawnPositions[index % DefaultSpawnPositions.Length];

            return spawnPositions[index % spawnPositions.Length];
        }

        private static bool IsWavePhase(GamePhase phase)
        {
            return phase == GamePhase.WAVE_INCOMING
                || phase == GamePhase.WAVE_ACTIVE
                || phase == GamePhase.WAVE_PAUSED
                || phase == GamePhase.WAVE_RESOLVING;
        }
    }
}
