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
        private enum TouristActivity
        {
            HideInside,
            SitAtDestination
        }

        private sealed class TouristRoutineState
        {
            public GameObject Tourist;
            public readonly List<Vector2> Route = new List<Vector2>(8);
            public int RoutineIndex;
            public int RouteIndex;
            public bool AtDestination;
            public bool Completed;
            public float DwellRemaining;
            public TouristActivity Activity;
            public Vector2 PathPosition;
            public int SpawnSlot;
            public float StartDelayRemaining;
            public float StaggerDelay;
        }

        private static readonly Vector2[] DefaultSpawnPositions =
        {
            new Vector2(-420f, -170f),
            new Vector2(-190f, 80f),
            new Vector2(60f, -40f),
            new Vector2(270f, 150f),
            new Vector2(420f, -120f),
            new Vector2(120f, 230f)
        };

        private static readonly Vector2[] SpawnSpreadOffsets =
        {
            new Vector2(-48f, 0f),
            new Vector2(0f, 0f),
            new Vector2(48f, 0f),
            new Vector2(-24f, -36f),
            new Vector2(24f, -36f),
            new Vector2(0f, -72f)
        };

        private static readonly int[][] PathNodeIndicesByDestination =
        {
            new[] { 0, 1, 2 }, // Reception branch
            new[] { 0, 1, 3 }, // Bar branch
            new[] { 0, 1, 4 }  // Restaurant branch
        };

        [SerializeField]
        private ResortGameplayBootstrap gameplayBootstrap;

        [SerializeField]
        private BoatArrivalSignal boatArrivalSignal;

        [SerializeField]
        private RectTransform touristContainer;

        [SerializeField]
        private RectTransform spawnPoint;

        [SerializeField]
        private RectTransform[] pathNodes;

        [SerializeField]
        private RectTransform receptionPoint;

        [SerializeField]
        private RectTransform barSeatPoint;

        [SerializeField]
        private RectTransform restaurantPoint;

        [SerializeField]
        private GameObject touristPrefab;

        [SerializeField]
        private Sprite[] touristSprites;

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

        [SerializeField, Min(1f)]
        private float walkSpeed = 90f;

        [SerializeField, Min(0f)]
        private float insideDwellSeconds = 4f;

        [SerializeField, Min(0f)]
        private float seatedDwellSeconds = 5f;

        [SerializeField, Min(0f)]
        private float idleBobHeight = 7f;

        [SerializeField, Min(0f)]
        private float idleBobSpeed = 1.5f;

        private readonly List<GameObject> pooledTourists = new List<GameObject>();
        private readonly List<TouristRoutineState> touristRoutines = new List<TouristRoutineState>();
        private IResortGameplayUiFacade gameplayFacade;
        private bool subscribed;
        private int spriteVariantOffset;

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
            if (boatArrivalSignal == null)
                boatArrivalSignal = FindAnyObjectByType<BoatArrivalSignal>();
            if (touristContainer == null)
                touristContainer = transform as RectTransform;
            if (spawnPoint == null && touristContainer != null)
            {
                spawnPoint = touristContainer.Find("TouristSpawnPoint") as RectTransform;
                if (spawnPoint == null && touristContainer.parent != null)
                    spawnPoint = touristContainer.parent.Find("TouristSpawnPoint") as RectTransform;
            }
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
            if (boatArrivalSignal != null)
                boatArrivalSignal.Arrived += HandleBoatArrived;
            subscribed = true;
        }

        private void UnbindFromGameplay()
        {
            if (!subscribed || gameplayFacade == null)
                return;

            gameplayFacade.StateChanged -= HandleStateChanged;
            if (boatArrivalSignal != null)
                boatArrivalSignal.Arrived -= HandleBoatArrived;
            gameplayFacade = null;
            subscribed = false;
        }

        private void SynchronizeImmediately()
        {
            SetWaveVisibility(gameplayFacade != null
                && IsWavePhase(gameplayFacade.CurrentPhase)
                && boatArrivalSignal != null
                && boatArrivalSignal.HasArrived);
        }

        private void HandleStateChanged(GamePhase previousState, GamePhase nextState)
        {
            SetWaveVisibility(IsWavePhase(nextState)
                && boatArrivalSignal != null
                && boatArrivalSignal.HasArrived);
        }

        private void HandleBoatArrived()
        {
            if (gameplayFacade != null && IsWavePhase(gameplayFacade.CurrentPhase))
                SetWaveVisibility(true);
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
            var spawnPosition = GetSpawnPointPosition();
            touristRoutines.Clear();
            for (var index = 0; index < touristCount; index++)
            {
                var tourist = pooledTourists[index];
                var rectTransform = tourist.GetComponent<RectTransform>();
                rectTransform.anchoredPosition = spawnPosition;
                rectTransform.localScale = Vector3.one;
                rectTransform.localRotation = Quaternion.identity;
                var image = tourist.GetComponent<Image>();
                if (image != null && touristSprites != null && touristSprites.Length > 0)
                    image.sprite = touristSprites[(index + spriteVariantOffset) % touristSprites.Length];
                tourist.transform.SetAsLastSibling();
                tourist.SetActive(true);

                var routine = new TouristRoutineState
                {
                    Tourist = tourist,
                    RoutineIndex = index % 3,
                    SpawnSlot = index,
                    StaggerDelay = index * 0.4f
                };
                PrepareRoutine(routine, spawnPosition);
                touristRoutines.Add(routine);
            }

            ActiveTouristCount = touristCount;
            if (touristSprites != null && touristSprites.Length > 0)
                spriteVariantOffset = (spriteVariantOffset + touristCount) % touristSprites.Length;
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
            touristRoutines.Clear();
        }

        private void AnimateTourists()
        {
            if (gameplayFacade != null && gameplayFacade.CurrentPhase == GamePhase.WAVE_PAUSED)
                return;

            var deltaTime = Time.unscaledDeltaTime;
            for (var index = 0; index < touristRoutines.Count; index++)
            {
                var routine = touristRoutines[index];
                if (routine == null || routine.Tourist == null)
                    continue;

                if (routine.Completed)
                    continue;

                if (routine.AtDestination)
                {
                    routine.DwellRemaining -= deltaTime;
                    if (routine.DwellRemaining <= 0f)
                        CompleteRoutine(routine);
                    continue;
                }

                MoveAlongRoutine(routine, deltaTime);
            }
        }

        private void PrepareRoutine(TouristRoutineState routine, Vector2 spawnPosition)
        {
            var routineSpawnPosition = spawnPosition + GetSpawnSpreadOffset(routine.SpawnSlot);
            routine.Route.Clear();
            routine.Route.Add(routineSpawnPosition);

            int destinationIndex = routine.RoutineIndex % 3;
            var pathNodeIndices = PathNodeIndicesByDestination[destinationIndex];
            for (var index = 0; index < pathNodeIndices.Length; index++)
            {
                int pathNodeIndex = pathNodeIndices[index];
                if (pathNodes != null && pathNodeIndex < pathNodes.Length)
                    AddRoutePoint(routine.Route, GetPointPosition(pathNodes[pathNodeIndex]));
            }

            routine.Activity = GetActivity(destinationIndex);
            AddRoutePoint(routine.Route, GetDestinationPosition(destinationIndex, routine.SpawnSlot));
            routine.RouteIndex = routine.Route.Count > 1 ? 1 : 0;
            routine.AtDestination = false;
            routine.Completed = false;
            routine.DwellRemaining = 0f;
            routine.PathPosition = routineSpawnPosition;
            routine.StartDelayRemaining = routine.StaggerDelay;
            routine.Tourist.SetActive(true);
            SetTouristPosition(routine);
        }

        private void MoveAlongRoutine(TouristRoutineState routine, float deltaTime)
        {
            if (routine.Route.Count == 0)
                return;

            if (routine.StartDelayRemaining > 0f)
            {
                routine.StartDelayRemaining -= deltaTime;
                SetTouristPosition(routine);
                return;
            }

            var rectTransform = routine.Tourist.GetComponent<RectTransform>();
            if (rectTransform == null)
                return;

            var target = routine.Route[Mathf.Clamp(routine.RouteIndex, 0, routine.Route.Count - 1)];
            var current = routine.PathPosition;
            var next = Vector2.MoveTowards(current, target, walkSpeed * deltaTime);
            var movement = next - current;
            routine.PathPosition = next;
            SetHorizontalFacing(rectTransform, movement.x);
            bool reachedTarget = Vector2.Distance(next, target) <= 0.75f;

            var renderedPosition = next;
            renderedPosition.y += Mathf.Sin((Time.unscaledTime + routine.RoutineIndex) * idleBobSpeed) * idleBobHeight;

            rectTransform.anchoredPosition = renderedPosition;
            if (!reachedTarget)
                return;

            routine.PathPosition = target;
            rectTransform.anchoredPosition = target;
            routine.RouteIndex++;
            if (routine.RouteIndex < routine.Route.Count)
                return;

            EnterDestination(routine);
        }

        private void EnterDestination(TouristRoutineState routine)
        {
            routine.AtDestination = true;
            routine.DwellRemaining = routine.Activity == TouristActivity.SitAtDestination
                ? seatedDwellSeconds
                : insideDwellSeconds;

            if (routine.Activity == TouristActivity.HideInside)
                routine.Tourist.SetActive(false);
        }

        private void CompleteRoutine(TouristRoutineState routine)
        {
            routine.AtDestination = false;
            routine.Completed = true;
            routine.DwellRemaining = 0f;

            if (routine.Activity == TouristActivity.HideInside)
                routine.Tourist.SetActive(false);
        }

        private TouristActivity GetActivity(int destinationIndex)
        {
            return destinationIndex == 1
                ? TouristActivity.SitAtDestination
                : TouristActivity.HideInside;
        }

        private Vector2 GetDestinationPosition(int destinationIndex, int spawnSlot)
        {
            RectTransform point = destinationIndex == 0
                ? receptionPoint
                : destinationIndex == 1
                    ? barSeatPoint
                    : restaurantPoint;

            var destination = point != null
                ? GetPointPosition(point)
                : GetSpawnPosition(destinationIndex + 1);

            if (destinationIndex == 1)
            {
                destination += GetBarSeatOffset(spawnSlot);
            }

            return destination;
        }

        private static Vector2 GetBarSeatOffset(int spawnSlot)
        {
            switch (spawnSlot % 6)
            {
                case 0:
                    return new Vector2(0f, -60f);
                case 1:
                    return Vector2.zero;
                case 2:
                    return new Vector2(0f, 60f);
                case 3:
                    return new Vector2(0f, 120f);
                case 4:
                    return new Vector2(0f, -120f);
                default:
                    return new Vector2(0f, 180f);
            }
        }

        private void SetTouristPosition(TouristRoutineState routine)
        {
            var rectTransform = routine.Tourist.GetComponent<RectTransform>();
            if (rectTransform != null)
                rectTransform.anchoredPosition = routine.PathPosition;
        }

        private static Vector2 GetSpawnSpreadOffset(int spawnSlot)
        {
            return SpawnSpreadOffsets[spawnSlot % SpawnSpreadOffsets.Length];
        }

        private void AddRoutePoint(List<Vector2> route, Vector2 point)
        {
            if (route.Count == 0 || Vector2.Distance(route[route.Count - 1], point) > 1f)
                route.Add(point);
        }

        private Vector2 GetPointPosition(RectTransform point)
        {
            if (point == null || touristContainer == null)
                return Vector2.zero;

            return touristContainer.InverseTransformPoint(point.position);
        }

        private Vector2 GetSpawnPointPosition()
        {
            if (spawnPoint == null || touristContainer == null)
                return GetSpawnPosition(0);

            return touristContainer.InverseTransformPoint(spawnPoint.position);
        }

        private static void SetHorizontalFacing(RectTransform rectTransform, float horizontalDirection)
        {
            if (rectTransform == null || Mathf.Abs(horizontalDirection) <= 0.001f)
                return;

            var scale = rectTransform.localScale;
            scale.x = Mathf.Abs(scale.x) * (horizontalDirection < 0f ? -1f : 1f);
            rectTransform.localScale = scale;
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
