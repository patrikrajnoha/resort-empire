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
            public float TalkingAnimationTime;
            public int PortalEntryRouteIndex = -1;
            public int PortalExitRouteIndex = -1;
            public float PortalDelayRemaining;
        }

        private static readonly Vector2[] SpawnSpreadOffsets =
        {
            new Vector2(-18f, 0f),
            new Vector2(0f, 0f),
            new Vector2(18f, 0f),
            new Vector2(-9f, -12f),
            new Vector2(9f, -12f),
            new Vector2(0f, -20f)
        };

        // The route ends on the sand in front of the bar. These offsets place
        // seated guests on the four visible level-1 bar stools without walking
        // through the art. The inner pair sits slightly higher, matching the
        // perspective of the stools in the bar illustration.
        private static readonly Vector2[] BarSeatVisualOffsets =
        {
            new Vector2(-52.46f, 136.11f),
            new Vector2(-21.37f, 120.99f),
            new Vector2(44.22f, 112.02f),
            new Vector2(99.20f, 123.70f)
        };

        // Guests face inward so adjacent guests can visually interact.
        private static readonly float[] BarSeatFacingDirections =
        {
            1f,
            -1f,
            1f,
            -1f
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
        private RectTransform islandBase;

        [SerializeField]
        private Texture2D navigationMapTexture;

        [SerializeField]
        private RectTransform receptionPoint;

        [SerializeField]
        private RectTransform receptionExitPoint;

        [SerializeField]
        private RectTransform barSeatPoint;

        [SerializeField]
        private RectTransform barSeatOverlay;

        [SerializeField]
        private RectTransform restaurantPoint;

        [SerializeField]
        private GameObject touristPrefab;

        [SerializeField]
        private Sprite[] touristSprites;

        [SerializeField, Range(3, 6)]
        private int touristCount = 5;

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

        [SerializeField, Min(0f)]
        private float barTalkingSwayWidth = 0.7f;

        [SerializeField, Min(0f)]
        private float barTalkingBounceHeight = 0.45f;

        [SerializeField, Min(0f)]
        private float barTalkingSpeed = 1.6f;

        private readonly List<GameObject> pooledTourists = new List<GameObject>();
        private readonly List<TouristRoutineState> touristRoutines = new List<TouristRoutineState>();
        private IResortGameplayUiFacade gameplayFacade;
        private bool subscribed;
        private int spriteVariantOffset;
        private IslandNavigationMap navigationMap;

        /// <summary>Number of tourist visuals assigned to the current wave.</summary>
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
            if (islandBase == null && touristContainer != null && touristContainer.parent != null)
                islandBase = touristContainer.parent.Find("IslandBase") as RectTransform;
            if (spawnPoint == null && touristContainer != null)
            {
                spawnPoint = touristContainer.Find("TouristSpawnPoint") as RectTransform;
                if (spawnPoint == null && touristContainer.parent != null)
                    spawnPoint = touristContainer.parent.Find("TouristSpawnPoint") as RectTransform;
            }
            if (barSeatOverlay == null && touristContainer != null && touristContainer.parent != null)
                barSeatOverlay = touristContainer.parent.Find("TouristBarSeatOverlay") as RectTransform;
            if (receptionPoint == null)
                receptionPoint = FindScenePoint("TouristReceptionPoint");
            if (receptionExitPoint == null)
                receptionExitPoint = FindScenePoint("TouristReceptionExitPoint");
            if (barSeatPoint == null)
                barSeatPoint = FindScenePoint("TouristBarSeatPoint");
            if (restaurantPoint == null)
                restaurantPoint = FindScenePoint("TouristRestaurantPoint");
        }

        private RectTransform FindScenePoint(string pointName)
        {
            if (touristContainer == null)
                return null;

            var point = touristContainer.Find(pointName) as RectTransform;
            if (point != null)
                return point;

            return touristContainer.parent == null
                ? null
                : touristContainer.parent.Find(pointName) as RectTransform;
        }

        private void NormalizeConfiguration()
        {
            touristCount = Mathf.Clamp(touristCount, 3, 6);
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
                && (ActiveTouristCount > 0
                    || boatArrivalSignal != null && boatArrivalSignal.HasArrived));
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

            if (touristContainer == null || touristPrefab == null || spawnPoint == null
                || receptionPoint == null || receptionExitPoint == null
                || barSeatPoint == null || restaurantPoint == null
                || islandBase == null || navigationMapTexture == null)
            {
                Debug.LogWarning("Tourist visuals cannot spawn because island navigation references are missing.", this);
                return;
            }

            if (navigationMap == null)
            {
                try
                {
                    navigationMap = new IslandNavigationMap(navigationMapTexture, islandBase.rect);
                }
                catch (UnityException exception)
                {
                    Debug.LogError("The island navigation texture must have Read/Write enabled: " + exception.Message, this);
                    return;
                }
            }

            EnsurePool(touristCount);
            var spawnPosition = GetSpawnPointPosition();
            touristRoutines.Clear();
            for (var index = 0; index < touristCount; index++)
            {
                var tourist = pooledTourists[index];
                tourist.transform.SetParent(touristContainer, false);
                var rectTransform = tourist.GetComponent<RectTransform>();
                rectTransform.localScale = Vector3.one * 1.2f;
                rectTransform.localRotation = Quaternion.identity;
                var image = tourist.GetComponent<Image>();
                if (image != null && touristSprites != null && touristSprites.Length > 0)
                    image.sprite = touristSprites[(index + spriteVariantOffset) % touristSprites.Length];
                tourist.transform.SetAsLastSibling();
                var routine = new TouristRoutineState
                {
                    Tourist = tourist,
                    RoutineIndex = index % 3,
                    SpawnSlot = index,
                    StaggerDelay = index * 0.4f
                };
                var routineSpawnPosition = spawnPosition + GetSpawnSpreadOffset(routine.SpawnSlot);
                if (!PrepareRoutine(routine, routineSpawnPosition, routine.StaggerDelay))
                {
                    tourist.SetActive(false);
                    continue;
                }
                touristRoutines.Add(routine);
            }

            ActiveTouristCount = touristRoutines.Count;
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
                    if (routine.DwellRemaining > 0f
                        && routine.Activity == TouristActivity.SitAtDestination)
                        AnimateBarConversation(routine, deltaTime);

                    routine.DwellRemaining -= deltaTime;
                    if (routine.DwellRemaining <= 0f)
                        AdvanceRoutine(routine);
                    continue;
                }

                MoveAlongRoutine(routine, deltaTime);
            }
        }

        private bool PrepareRoutine(TouristRoutineState routine, Vector2 startPosition, float startDelay)
        {
            return PrepareRoutine(routine, startPosition, startDelay, startPosition);
        }

        private bool PrepareRoutine(
            TouristRoutineState routine,
            Vector2 startPosition,
            float startDelay,
            Vector2 navigationStartPosition)
        {
            routine.Route.Clear();
            routine.PortalEntryRouteIndex = -1;
            routine.PortalExitRouteIndex = -1;
            int destinationIndex = routine.RoutineIndex;
            routine.Activity = GetActivity(destinationIndex);
            var destination = GetDestinationPosition(destinationIndex, routine.SpawnSlot);
            var baseStart = islandBase.InverseTransformPoint(
                touristContainer.TransformPoint(navigationStartPosition));
            var baseDestination = islandBase.InverseTransformPoint(touristContainer.TransformPoint(destination));
            var baseRoute = new List<Vector2>();
            if (!navigationMap.TryFindPath(baseStart, baseDestination, baseRoute))
            {
                if (!TryBuildPortalRoute(baseStart, baseDestination, baseRoute, routine))
                {
                    Debug.LogWarning("No walkable island route to destination " + destinationIndex, this);
                    return false;
                }
            }
            for (var index = 0; index < baseRoute.Count; index++)
                routine.Route.Add(touristContainer.InverseTransformPoint(islandBase.TransformPoint(baseRoute[index])));

            // A seated guest is rendered on a bar stool inside the building art.
            // Leave from the configured walkable bar anchor, but preserve the
            // visible seat position as a short, explicit exit segment.
            if (Vector2.Distance(startPosition, navigationStartPosition) > 1f)
            {
                routine.Route.Insert(0, startPosition);
                if (routine.PortalEntryRouteIndex >= 0)
                    routine.PortalEntryRouteIndex++;
                if (routine.PortalExitRouteIndex >= 0)
                    routine.PortalExitRouteIndex++;
            }

            // The navigation texture snaps to cell centres. Keep the configured
            // destination as the final point so a guest cannot visually stop at
            // the spawn-side cell when the route is short or both points share a
            // navigation cell.
            if (routine.Route.Count > 0
                && Vector2.Distance(routine.Route[routine.Route.Count - 1], destination) > 1f)
            {
                routine.Route.Add(destination);
            }

            routine.RouteIndex = 0;
            routine.AtDestination = false;
            routine.Completed = false;
            routine.DwellRemaining = 0f;
            routine.TalkingAnimationTime = 0f;
            routine.PathPosition = startPosition;
            routine.StartDelayRemaining = startDelay;
            routine.PortalDelayRemaining = 0f;
            routine.Tourist.SetActive(true);
            SetTouristPosition(routine);
            return true;
        }

        private bool TryBuildPortalRoute(
            Vector2 baseStart,
            Vector2 baseDestination,
            List<Vector2> route,
            TouristRoutineState routine)
        {
            var portalEntries = new[] { receptionPoint, receptionExitPoint };
            var portalExits = new[] { receptionExitPoint, receptionPoint };
            for (var portalIndex = 0; portalIndex < portalEntries.Length; portalIndex++)
            {
                var entry = islandBase.InverseTransformPoint(portalEntries[portalIndex].position);
                var exit = islandBase.InverseTransformPoint(portalExits[portalIndex].position);
                var entryRoute = new List<Vector2>();
                var destinationRoute = new List<Vector2>();
                if (!navigationMap.TryFindPath(baseStart, entry, entryRoute)
                    || !navigationMap.TryFindPath(exit, baseDestination, destinationRoute))
                    continue;

                route.AddRange(entryRoute);
                routine.PortalEntryRouteIndex = route.Count - 1;
                routine.PortalExitRouteIndex = route.Count;
                route.AddRange(destinationRoute);
                return true;
            }

            return false;
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

            if (routine.PortalDelayRemaining > 0f)
            {
                routine.PortalDelayRemaining -= deltaTime;
                if (routine.PortalDelayRemaining <= 0f)
                    routine.Tourist.SetActive(true);
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
            if (routine.RouteIndex == routine.PortalExitRouteIndex)
            {
                routine.Tourist.SetActive(false);
                routine.PathPosition = routine.Route[routine.PortalExitRouteIndex];
                routine.RouteIndex++;
                routine.PortalDelayRemaining = 0.8f;
                return;
            }
            if (routine.RouteIndex < routine.Route.Count)
                return;

            EnterDestination(routine);
        }

        private void EnterDestination(TouristRoutineState routine)
        {
            routine.AtDestination = true;
            routine.TalkingAnimationTime = 0f;
            routine.DwellRemaining = routine.Activity == TouristActivity.SitAtDestination
                ? seatedDwellSeconds
                : insideDwellSeconds;

            if (routine.Activity == TouristActivity.HideInside)
            {
                routine.Tourist.SetActive(false);
                return;
            }

            if (routine.Activity == TouristActivity.SitAtDestination)
            {
                var seatIndex = routine.SpawnSlot % BarSeatVisualOffsets.Length;
                var seatPosition = GetPointPosition(barSeatPoint) + BarSeatVisualOffsets[seatIndex];
                SetHorizontalFacing(
                    routine.Tourist.GetComponent<RectTransform>(),
                    BarSeatFacingDirections[seatIndex]);
                PlaceAtBarSeat(routine, seatPosition);
            }
        }

        private void AnimateBarConversation(TouristRoutineState routine, float deltaTime)
        {
            var rectTransform = routine.Tourist.GetComponent<RectTransform>();
            if (rectTransform == null)
                return;

            routine.TalkingAnimationTime += deltaTime;
            var phase = routine.TalkingAnimationTime * barTalkingSpeed
                + routine.RoutineIndex * 0.9f;
            var sway = Mathf.Sin(phase) * barTalkingSwayWidth;
            var bounce = Mathf.Sin(phase * 1.35f) * barTalkingBounceHeight;
            rectTransform.anchoredPosition = routine.PathPosition + new Vector2(sway, bounce);
        }

        private void AdvanceRoutine(TouristRoutineState routine)
        {
            var departurePosition = GetDeparturePosition(routine);
            var navigationDeparturePosition = departurePosition;
            if (routine.RoutineIndex == 1 && barSeatPoint != null)
                navigationDeparturePosition = GetPointPosition(barSeatPoint);

            routine.Tourist.transform.SetParent(touristContainer, false);
            routine.RoutineIndex = (routine.RoutineIndex + 1) % 3;
            if (!PrepareRoutine(routine, departurePosition, 0f, navigationDeparturePosition))
            {
                routine.AtDestination = false;
                routine.Completed = true;
                routine.DwellRemaining = 0f;
            }
        }

        private Vector2 GetDeparturePosition(TouristRoutineState routine)
        {
            if (routine.RoutineIndex == 0)
                return GetPointPosition(receptionExitPoint);

            if (routine.RoutineIndex == 2)
                return GetPointPosition(restaurantPoint);

            return touristContainer.InverseTransformPoint(routine.Tourist.transform.position);
        }

        private void PlaceAtBarSeat(TouristRoutineState routine, Vector2 seatPosition)
        {
            if (barSeatOverlay == null || touristContainer == null)
            {
                routine.PathPosition = seatPosition;
                SetTouristPosition(routine);
                return;
            }

            var seatWorldPosition = touristContainer.TransformPoint(seatPosition);
            routine.Tourist.transform.SetParent(barSeatOverlay, false);
            routine.PathPosition = barSeatOverlay.InverseTransformPoint(seatWorldPosition);
            SetTouristPosition(routine);
        }

        private void CompleteRoutine(TouristRoutineState routine)
        {
            routine.AtDestination = false;
            routine.Completed = true;
            routine.DwellRemaining = 0f;
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

            var destination = GetPointPosition(point);

            if (destinationIndex == 1)
                destination += new Vector2((spawnSlot % 3 - 1) * 12f, 0f);

            return destination;
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

        private Vector2 GetPointPosition(RectTransform point)
        {
            if (point == null || touristContainer == null)
                return Vector2.zero;

            return touristContainer.InverseTransformPoint(point.position);
        }

        private Vector2 GetSpawnPointPosition()
        {
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

        private static bool IsWavePhase(GamePhase phase)
        {
            return phase == GamePhase.WAVE_INCOMING
                || phase == GamePhase.WAVE_ACTIVE
                || phase == GamePhase.WAVE_PAUSED
                || phase == GamePhase.WAVE_RESOLVING;
        }
    }
}
