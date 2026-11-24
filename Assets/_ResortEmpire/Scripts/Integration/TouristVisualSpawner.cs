using System;
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
        private const int PoolDestination = 3;
        private const int BungalowsDestination = 4;
        private const int DestinationCount = 5;

        private enum TouristActivity
        {
            HideInside,
            SitAtDestination,
            RelaxInPool
        }

        private sealed class TouristRoutineState
        {
            public GameObject Tourist;
            public readonly List<Vector2> Route = new List<Vector2>(8);
            public readonly List<Vector2> RestaurantStairs = new List<Vector2>();
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
            public bool HideFromStartUntilPortalExit;
            public int BarSeatIndex = -1;
            public bool WaitingForBarSeat;
            public RectTransform PoolSeat;
            public int StairStartIndex;
            public int StairEndIndex;

            public bool IsOnRestaurantStairRoute =>
                RouteIndex >= StairStartIndex && RouteIndex < StairEndIndex;
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

        // Fallback positions for the authored level-1 bar stools. Scene entry
        // markers take precedence, so later bar levels can expose their own
        // number of stools without another code change.
        private static readonly Vector2[] BarSeatVisualOffsets =
        {
            new Vector2(-52.46f, 136.11f),
            new Vector2(-21.37f, 120.99f),
            new Vector2(44.22f, 112.02f),
            new Vector2(99.20f, 123.70f)
        };

        // Level 2 was visually tuned with the authored static guests. Keep
        // those exact overlay-local positions for live guests as well; the
        // marker points are entry/contact points and are not the sprite
        // centres for this bar art.
        private static readonly Vector2[] LevelTwoVisualSeatPositions =
        {
            new Vector2(-33.5f, 102.8f),
            new Vector2(-13.8f, 89.9f),
            new Vector2(13.7f, 76.6f),
            new Vector2(47.2f, 70.4f),
            new Vector2(84.2f, 70.4f),
            new Vector2(119.4f, 80.0f)
        };

        // Guests face inward so adjacent guests can visually interact.
        private static readonly float[] BarSeatFacingDirections =
        {
            1f,
            -1f,
            1f,
            -1f
        };

        private const float WalkingTouristScale = 1.2f;
        private const float SeatedTouristScale = WalkingTouristScale;
        private const float SeatedTouristVerticalOffset = 0f;
        private const float LevelTwoBarSeatFallbackOffset = 32f;
        // Level 3 markers sit on the stool tops. Raise the centered sprite
        // by roughly half its rendered height to meet the seat surface.
        private const float LevelThreeBarSeatOffset = 27f;
        // The authored Restaurant route passes through the narrow, visible
        // gap between the palm and the support posts. These are intentionally
        // separate: a uniform large margin closes the gap and sends tourists
        // across the lower beach.
        private const float PalmObstacleSafetyMargin = 8f;
        private const float RestaurantSupportPostsSafetyMargin = 20f;

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
        private RectTransform walkablePathLayer;

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
        private RectTransform poolEntrancePoint;

        [SerializeField, Tooltip("Sun lounger markers in Pool level order. Each marker's local Y points toward the headrest and its rect defines the resting pose size.")]
        private RectTransform[] poolSeatLayouts;

        [SerializeField, Min(0f)]
        private float poolDwellSeconds = 14f;

        [SerializeField]
        private RectTransform bungalowsEntrancePoint;

        [SerializeField]
        private RectTransform poolNavigationObstacle;

        [SerializeField]
        private RectTransform bungalowsNavigationObstacle;

        [SerializeField]
        private RestaurantStairRoutes restaurantStairRoutes;

        [SerializeField]
        private TouristEntranceSmoke entranceSmoke;

        private RectTransform receptionEntryPoint;
        private readonly List<RectTransform> receptionLevelEntryPoints = new List<RectTransform>();

        private RectTransform restaurantEntryPoint;

        private RectTransform receptionNavigationObstacle;

        private readonly List<RectTransform> navigationObstacles = new List<RectTransform>();
        private readonly List<RectTransform> navigationWalkableAreas = new List<RectTransform>();
        private readonly List<RectTransform> barSeatEntryPoints = new List<RectTransform>();

        private RectTransform barNavigationObstacle;

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
        private IslandNavigationMap restaurantRouteMap;

        /// <summary>Number of tourist visuals assigned to the current wave.</summary>
        public int ActiveTouristCount { get; private set; }

        private void Awake()
        {
            ResolveSceneReferences();
            NormalizeConfiguration();
        }

        private void OnEnable()
        {
            ResortDebugLevelOverride.Changed += HandleDebugLevelChanged;
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
            ResortDebugLevelOverride.Changed -= HandleDebugLevelChanged;
            UnbindFromGameplay();
            DeactivateTourists();
            navigationMap = null;
            restaurantRouteMap = null;
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
            if (walkablePathLayer == null && touristContainer != null && touristContainer.parent != null)
                walkablePathLayer = touristContainer.parent.Find("WalkablePathLayer") as RectTransform;
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
            if (receptionEntryPoint == null)
                receptionEntryPoint = FindSceneMarker("ReceptionEntrancePoint", "ReceptionLvl1Entry");
            FindReceptionLevelEntryPoints();
            if (restaurantEntryPoint == null)
                restaurantEntryPoint = FindSceneMarker("RestaurantEntrancePoint", "RestaurantLvl1Entry");
            if (receptionNavigationObstacle == null)
                receptionNavigationObstacle = FindSceneMarker("Reception", "ReceptionVisual");
            if (navigationObstacles.Count == 0)
                FindSceneMarkers(
                    "RestaurantNavigationObstacle",
                    "RestaurantVisual",
                    navigationObstacles);
            AddSceneMarkerIfMissing(
                "RestaurantSupportPostsNavigationObstacle",
                "RestaurantSupportPostsNavigationBlocker",
                navigationObstacles);
            AddSceneMarkerIfMissing("PalmObstacle", "PalmNavigationObstacle", navigationObstacles);
            AddSceneMarkerIfMissing(
                "RestaurantApproachWalkableArea",
                "RestaurantWalkableCorridor",
                navigationWalkableAreas);
            if (barNavigationObstacle == null)
                barNavigationObstacle = FindSceneMarker("BeachBarSprite", "BeachBarVisual");
            FindBarSeatEntryPoints();
        }

        private void FindReceptionLevelEntryPoints()
        {
            receptionLevelEntryPoints.Clear();
            var markers = FindObjectsByType<RectTransform>(FindObjectsInactive.Include);
            for (var index = 0; index < markers.Length; index++)
            {
                var marker = markers[index];
                if (marker == null || !marker.name.StartsWith("ReceptionEntry_Lvl", StringComparison.Ordinal))
                    continue;

                receptionLevelEntryPoints.Add(marker);
            }

            receptionLevelEntryPoints.Sort((left, right) =>
                string.CompareOrdinal(left.name, right.name));
        }

        private void FindBarSeatEntryPoints()
        {
            barSeatEntryPoints.Clear();
            var markers = FindObjectsByType<RectTransform>(FindObjectsInactive.Include);
            for (var index = 0; index < markers.Length; index++)
            {
                var marker = markers[index];
                if (marker == null || !marker.name.StartsWith("BarSeatEntry_Lvl", StringComparison.Ordinal))
                    continue;

                barSeatEntryPoints.Add(marker);
            }

            barSeatEntryPoints.Sort((left, right) =>
                string.CompareOrdinal(left.name, right.name));
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

        private static RectTransform FindSceneMarker(string preferredName, string legacyName)
        {
            var marker = GameObject.Find(preferredName);
            if (marker == null)
                marker = GameObject.Find(legacyName);

            return marker == null ? null : marker.transform as RectTransform;
        }

        private static void FindSceneMarkers(
            string preferredName,
            string legacyName,
            List<RectTransform> results)
        {
            results.Clear();
            var markers = FindObjectsByType<RectTransform>(
                FindObjectsInactive.Include);
            var numberedPrefix = preferredName + " (";
            for (var index = 0; index < markers.Length; index++)
            {
                var marker = markers[index];
                if (marker.name.Equals(preferredName, StringComparison.Ordinal)
                    || marker.name.StartsWith(numberedPrefix, StringComparison.Ordinal))
                {
                    results.Add(marker);
                }
            }

            if (results.Count == 0)
            {
                var fallback = FindSceneMarker(preferredName, legacyName);
                if (fallback != null)
                    results.Add(fallback);
            }
        }

        private static void AddSceneMarkerIfMissing(
            string preferredName,
            string legacyName,
            List<RectTransform> results)
        {
            var marker = FindSceneMarker(preferredName, legacyName);
            if (marker == null)
            {
                var markers = FindObjectsByType<RectTransform>(FindObjectsInactive.Include);
                for (var index = 0; index < markers.Length; index++)
                {
                    var candidate = markers[index];
                    if (candidate.name.Equals(preferredName, StringComparison.Ordinal)
                        || candidate.name.Equals(legacyName, StringComparison.Ordinal)
                        || candidate.name.StartsWith(preferredName + " (", StringComparison.Ordinal)
                        || candidate.name.StartsWith(legacyName + " (", StringComparison.Ordinal))
                    {
                        marker = candidate;
                        break;
                    }
                }
            }

            if (marker != null && !results.Contains(marker))
                results.Add(marker);
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

        private void HandleDebugLevelChanged(int? _)
        {
            RefreshBarSeatAssignments();
        }

        private void RefreshBarSeatAssignments()
        {
            if (touristRoutines.Count == 0)
                return;

            for (var index = 0; index < touristRoutines.Count; index++)
            {
                var routine = touristRoutines[index];
                if (routine == null || routine.Activity != TouristActivity.SitAtDestination)
                    continue;

                ReleaseBarSeat(routine);
            }

            for (var index = 0; index < touristRoutines.Count; index++)
            {
                var routine = touristRoutines[index];
                if (routine == null || routine.Completed || !routine.AtDestination
                    || routine.Activity != TouristActivity.SitAtDestination
                    || routine.Tourist.transform.parent != barSeatOverlay)
                {
                    continue;
                }

                if (TryReserveBarSeat(routine))
                    PlaceRoutineAtBarSeat(routine);
            }

            // Routes are tied to the old stool layout. If a guest is on the last
            // leg, return them along that same line before changing seats.
            for (var index = 0; index < touristRoutines.Count; index++)
            {
                var routine = touristRoutines[index];
                if (routine == null || routine.Completed
                    || routine.Activity != TouristActivity.SitAtDestination || routine.BarSeatIndex >= 0)
                {
                    continue;
                }

                ResumeBarApproach(routine);
            }
        }

        private void ResumeBarApproach(TouristRoutineState routine)
        {
            Vector2 position = touristContainer.InverseTransformPoint(routine.Tourist.transform.position);
            var approach = GetBarApproachPosition(position);
            var fromStool = routine.Tourist.transform.parent == barSeatOverlay
                || (!routine.AtDestination && routine.RouteIndex == routine.Route.Count - 1);
            var navigationStart = fromStool && position.y > approach.y ? approach : position;
            if (!PrepareRoutine(routine, position, routine.StartDelayRemaining, navigationStart))
            {
                CompleteRoutine(routine);
                SetTouristActive(routine.Tourist, false);
            }
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
                || islandBase == null)
            {
                Debug.LogWarning("Tourist visuals cannot spawn because island navigation references are missing.", this);
                return;
            }

            if (!EnsureNavigationMap("Tourist visuals cannot spawn"))
                return;

            EnsurePool(touristCount);
            var spawnPosition = GetSpawnPointPosition();
            touristRoutines.Clear();
            for (var index = 0; index < touristCount; index++)
            {
                var tourist = pooledTourists[index];
                tourist.transform.SetParent(touristContainer, false);
                var rectTransform = tourist.GetComponent<RectTransform>();
                rectTransform.localScale = Vector3.one * WalkingTouristScale;
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
                // Keep the original three arrivals and give the extra tourists
                // direct routes to purchased buildings when those paths exist.
                var hasRoute = false;
                if (index == PoolDestination || index == BungalowsDestination)
                {
                    var defaultDestination = routine.RoutineIndex;
                    routine.RoutineIndex = index;
                    hasRoute = PrepareRoutine(routine, routineSpawnPosition, routine.StaggerDelay);
                    if (!hasRoute)
                        routine.RoutineIndex = defaultDestination;
                }
                if (!hasRoute && !PrepareRoutine(routine, routineSpawnPosition, routine.StaggerDelay))
                {
                    SetTouristActive(tourist, false);
                    continue;
                }
                touristRoutines.Add(routine);
            }

            ActiveTouristCount = touristRoutines.Count;
            if (touristSprites != null && touristSprites.Length > 0)
                spriteVariantOffset = (spriteVariantOffset + touristCount) % touristSprites.Length;
        }

        private bool EnsureNavigationMap(string failurePrefix)
        {
            if (navigationMap != null)
                return true;

            try
            {
                var pathLayerImage = walkablePathLayer == null
                    ? null
                    : walkablePathLayer.GetComponent<Image>();
                var pathLayerTexture = pathLayerImage == null || pathLayerImage.sprite == null
                    ? null
                    : pathLayerImage.sprite.texture;
                if (pathLayerTexture == null && navigationMapTexture == null)
                {
                    Debug.LogWarning(failurePrefix + " because no walkable path layer or navigation map is assigned.", this);
                    return false;
                }
                if (pathLayerTexture != null)
                {
                    var sampleWidth = navigationMapTexture != null ? navigationMapTexture.width : 181;
                    var sampleHeight = navigationMapTexture != null ? navigationMapTexture.height : 136;
                    navigationMap = new IslandNavigationMap(
                        pathLayerTexture,
                        GetNavigationBounds(walkablePathLayer),
                        true,
                        sampleWidth,
                        sampleHeight,
                        GetNavigationObstacles(),
                        navigationMapTexture,
                        GetNavigationWalkableAreas());
                    restaurantRouteMap = new IslandNavigationMap(
                        pathLayerTexture,
                        GetNavigationBounds(walkablePathLayer),
                        true,
                        sampleWidth,
                        sampleHeight,
                        GetNavigationObstacles(false),
                        navigationMapTexture,
                        GetNavigationWalkableAreas());
                }
                else
                {
                    var barObstacles = new List<IslandNavigationMap.NavigationObstacle>(1);
                    AddNavigationObstacle(barObstacles, barNavigationObstacle);
                    navigationMap = new IslandNavigationMap(
                        navigationMapTexture,
                        islandBase.rect,
                        false,
                        navigationMapTexture.width,
                        navigationMapTexture.height,
                        barObstacles,
                        null);
                    restaurantRouteMap = null;
                }
            }
            catch (UnityException exception)
            {
                Debug.LogError("The island navigation texture must have Read/Write enabled: " + exception.Message, this);
                navigationMap = null;
                restaurantRouteMap = null;
                return false;
            }

            return true;
        }

        /// <summary>Container that owns tourist visuals. Queue visuals share its coordinate space and render layer.</summary>
        public RectTransform TouristContainer => touristContainer;

        /// <summary>The tourist character used for roaming guests, reused for waiting guests.</summary>
        public GameObject TouristPrefab => touristPrefab;

        /// <summary>Tourist sprite variants, reused for waiting guests.</summary>
        public IReadOnlyList<Sprite> TouristSprites => touristSprites;

        /// <summary>
        /// Presentation-only queue line for <paramref name="buildingId"/> in tourist-container space.
        /// The first point is where guests enter the building; the rest follows the walkable route
        /// back toward the pier, so evenly spaced positions along it never leave the island paths.
        /// This reads navigation data only and does not reserve seats or affect roaming tourists.
        /// </summary>
        public bool TryGetQueueRoute(BuildingId buildingId, List<Vector2> route)
        {
            if (route == null)
                throw new ArgumentNullException(nameof(route));

            route.Clear();
            // Scene lookups scan every RectTransform; Awake has normally resolved them already.
            if (touristContainer == null || spawnPoint == null || islandBase == null)
                ResolveSceneReferences();
            if (touristContainer == null || spawnPoint == null || islandBase == null)
                return false;

            var anchor = GetQueueAnchor(buildingId);
            if (anchor == null || !EnsureNavigationMap("Guest queue visuals cannot be placed"))
                return false;

            var anchorPosition = buildingId == BuildingId.BeachBar
                ? GetPointPosition(barSeatPoint)
                : GetPointPosition(anchor);
            var baseAnchor = islandBase.InverseTransformPoint(touristContainer.TransformPoint(anchorPosition));
            var baseSpawn = islandBase.InverseTransformPoint(touristContainer.TransformPoint(GetSpawnPointPosition()));
            var baseRoute = new List<Vector2>();
            if (!navigationMap.TryFindPathToNearestReachable(baseAnchor, baseSpawn, baseRoute) || baseRoute.Count == 0)
                return false;

            route.Add(anchorPosition);
            for (var index = 0; index < baseRoute.Count; index++)
            {
                Vector2 point = touristContainer.InverseTransformPoint(islandBase.TransformPoint(baseRoute[index]));
                if (Vector2.Distance(route[route.Count - 1], point) > 0.5f)
                    route.Add(point);
            }

            return route.Count > 1;
        }

        private RectTransform GetQueueAnchor(BuildingId buildingId)
        {
            switch (buildingId)
            {
                case BuildingId.Reception:
                    var entry = GetReceptionEntryPoint();
                    return entry != null ? entry : receptionPoint;
                case BuildingId.BeachBar: return barSeatPoint;
                case BuildingId.Restaurant: return restaurantPoint != null ? restaurantPoint : restaurantEntryPoint;
                case BuildingId.Pool: return poolEntrancePoint;
                case BuildingId.Bungalows: return bungalowsEntrancePoint;
                default: return null;
            }
        }

        private Rect GetNavigationBounds(RectTransform source)
        {
            if (source == null)
                return islandBase.rect;

            var corners = new Vector3[4];
            source.GetWorldCorners(corners);
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var index = 0; index < corners.Length; index++)
            {
                var local = islandBase.InverseTransformPoint(corners[index]);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private IReadOnlyList<IslandNavigationMap.NavigationObstacle> GetNavigationObstacles(
            bool includeReception = true)
        {
            var obstacles = new List<IslandNavigationMap.NavigationObstacle>(
                3 + navigationObstacles.Count);
            if (includeReception)
                AddNavigationObstacle(obstacles, receptionNavigationObstacle);
            for (var index = 0; index < navigationObstacles.Count; index++)
                AddNavigationObstacle(obstacles, navigationObstacles[index]);
            AddNavigationObstacle(obstacles, barNavigationObstacle);
            AddDestinationObstacle(obstacles, poolNavigationObstacle);
            AddDestinationObstacle(obstacles, bungalowsNavigationObstacle);
            return obstacles;
        }

        private void AddDestinationObstacle(
            List<IslandNavigationMap.NavigationObstacle> obstacles,
            RectTransform footprint)
        {
            if (footprint != null)
                obstacles.Add(new IslandNavigationMap.NavigationObstacle(GetNavigationBounds(footprint), false));
        }

        private IReadOnlyList<Rect> GetNavigationWalkableAreas()
        {
            if (navigationWalkableAreas.Count == 0)
                return null;

            var areas = new List<Rect>(navigationWalkableAreas.Count);
            for (var index = 0; index < navigationWalkableAreas.Count; index++)
            {
                var source = navigationWalkableAreas[index];
                if (source == null)
                    continue;

                var corners = new Vector3[4];
                source.GetWorldCorners(corners);
                var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                for (var cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
                {
                    var point = islandBase.InverseTransformPoint(corners[cornerIndex]);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }

                areas.Add(Rect.MinMaxRect(min.x, min.y, max.x, max.y));
            }

            return areas.Count == 0 ? null : areas;
        }

        private void AddNavigationObstacle(
            List<IslandNavigationMap.NavigationObstacle> obstacles,
            RectTransform source)
        {
            if (source == null)
                return;

            obstacles.Add(BuildNavigationObstacle(source));
        }

        private IslandNavigationMap.NavigationObstacle BuildNavigationObstacle(RectTransform source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var corners = new Vector3[4];
            source.GetWorldCorners(corners);
            var localCorners = new Vector2[corners.Length];
            for (var index = 0; index < corners.Length; index++)
                localCorners[index] = islandBase.InverseTransformPoint(corners[index]);

            // Keep a safety margin around building artwork while preserving
            // the authored rotation of narrow blockers such as palm leaves
            // and restaurant support posts. Reception already sits against
            // the authored route and its exact footprint is the only opening
            // for the lower-island approach, so do not expand that marker.
            var palmObstacle = source.name.Equals("PalmObstacle", StringComparison.Ordinal)
                || source.name.Equals("PalmNavigationObstacle", StringComparison.Ordinal);
            var restaurantSupportPosts = source.name.Equals(
                "RestaurantSupportPostsNavigationObstacle",
                StringComparison.Ordinal)
                || source.name.Equals(
                    "RestaurantSupportPostsNavigationBlocker",
                    StringComparison.Ordinal);
            var hardObstacle = palmObstacle || restaurantSupportPosts;
            // The tourist sprite is scaled to 1.2x at runtime, so the route
            // centre must keep roughly half a sprite clear of hard blockers.
            // The authored restaurant approach area provides the wider detour
            // needed for this clearance without allowing the NPC through the
            // support posts or palm.
            var isReception = source == receptionNavigationObstacle
                || source.name.Equals("Reception", StringComparison.Ordinal)
                || source.name.Equals("ReceptionVisual", StringComparison.Ordinal);
            // Keep the authored palm-to-post corridor open. The marker bounds
            // already cover the visible art, so this small buffer prevents a
            // centre-point collision without sealing the route to Restaurant.
            var safetyMargin = restaurantSupportPosts
                ? RestaurantSupportPostsSafetyMargin
                : palmObstacle
                    ? PalmObstacleSafetyMargin
                : isReception ? 0f : 8f;
            var center = (localCorners[0] + localCorners[1]
                + localCorners[2] + localCorners[3]) * 0.25f;
            var horizontal = (localCorners[1] - localCorners[0]).normalized;
            var vertical = (localCorners[3] - localCorners[0]).normalized;
            var halfWidth = Vector2.Distance(localCorners[0], localCorners[1]) * 0.5f
                + safetyMargin;
            var halfHeight = Vector2.Distance(localCorners[0], localCorners[3]) * 0.5f
                + safetyMargin;
            var expandedCorners = new[]
            {
                center - horizontal * halfWidth - vertical * halfHeight,
                center + horizontal * halfWidth - vertical * halfHeight,
                center + horizontal * halfWidth + vertical * halfHeight,
                center - horizontal * halfWidth + vertical * halfHeight
            };
            // The bar's aisle is outside the footprint. The only path into
            // the footprint should be the explicit final leg to a stool.
            var preserveProtectedArea = !hardObstacle && source != barNavigationObstacle;
            return new IslandNavigationMap.NavigationObstacle(
                expandedCorners,
                preserveProtectedArea);
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
            if (entranceSmoke != null)
                entranceSmoke.Clear();
            if (restaurantStairRoutes != null)
                restaurantStairRoutes.ClearDining();
            for (var index = 0; index < pooledTourists.Count; index++)
            {
                if (pooledTourists[index] != null)
                    SetTouristActive(pooledTourists[index], false);
            }

            ActiveTouristCount = 0;
            touristRoutines.Clear();
        }

        private void AnimateTourists()
        {
            if (gameplayFacade != null && gameplayFacade.CurrentPhase == GamePhase.WAVE_PAUSED)
                return;

            var deltaTime = Time.unscaledDeltaTime;
            if (entranceSmoke != null)
                entranceSmoke.Advance(deltaTime);
            if (restaurantStairRoutes != null)
                restaurantStairRoutes.AdvanceDining(deltaTime);
            for (var index = 0; index < touristRoutines.Count; index++)
            {
                var routine = touristRoutines[index];
                if (routine == null || routine.Tourist == null)
                    continue;

                if (routine.Completed)
                    continue;

                if (routine.RoutineIndex >= PoolDestination && !IsDestinationAvailable(routine.RoutineIndex))
                {
                    CompleteRoutine(routine);
                    SetTouristActive(routine.Tourist, false);
                    continue;
                }

                if (routine.AtDestination)
                {
                    if (routine.Activity == TouristActivity.SitAtDestination
                        && routine.WaitingForBarSeat)
                    {
                        if (TryReserveBarSeat(routine))
                            ResumeBarApproach(routine);

                        continue;
                    }

                    if (routine.DwellRemaining > 0f
                        && routine.Activity == TouristActivity.SitAtDestination)
                        AnimateBarConversation(routine, deltaTime);

                    routine.DwellRemaining -= deltaTime;
                    if (routine.DwellRemaining <= 0f)
                        AdvanceRoutine(routine);
                    else if (routine.RoutineIndex == 2 && routine.DwellRemaining >= 0.6f && restaurantStairRoutes != null)
                        restaurantStairRoutes.ShowDiner(routine.Tourist);
                    continue;
                }

                MoveAlongRoutine(routine, deltaTime);
            }

            SetTouristEntryRenderLayer(IsAnyTouristEnteringBuilding());
        }

        private bool PrepareRoutine(TouristRoutineState routine, Vector2 startPosition, float startDelay)
        {
            return PrepareRoutine(routine, startPosition, startDelay, startPosition);
        }

        private bool PrepareRoutine(
            TouristRoutineState routine,
            Vector2 startPosition,
            float startDelay,
            Vector2 navigationStartPosition,
            bool leavingRestaurantStairs = false)
        {
            routine.Route.Clear();
            routine.StairStartIndex = 0;
            routine.StairEndIndex = 0;
            routine.PortalEntryRouteIndex = -1;
            routine.PortalExitRouteIndex = -1;
            routine.HideFromStartUntilPortalExit = false;
            int destinationIndex = routine.RoutineIndex;
            var isPurchasedDestination = destinationIndex >= PoolDestination;
            routine.Activity = GetActivity(destinationIndex);
            var hasBarSeat = destinationIndex != 1 || TryReserveBarSeat(routine);
            routine.WaitingForBarSeat = destinationIndex == 1 && !hasBarSeat;
            var entryPoint = GetEntryPoint(destinationIndex);
            if (isPurchasedDestination && (entryPoint == null || !IsDestinationAvailable(destinationIndex)))
                return false;
            var barSeat = destinationIndex == 1 && hasBarSeat
                ? (Vector2?)GetBarSeatPosition(routine.BarSeatIndex)
                : null;
            var hasRestaurantStairs = destinationIndex == 2 && LoadRestaurantStairs(routine);
            var destination = hasRestaurantStairs
                ? GetPointPosition(restaurantPoint)
                : barSeat.HasValue
                    ? GetBarApproachPosition(barSeat.Value)
                    : entryPoint == null
                        ? GetDestinationPosition(destinationIndex)
                        : GetPointPosition(entryPoint);
            var baseStart = islandBase.InverseTransformPoint(
                touristContainer.TransformPoint(navigationStartPosition));
            var baseDestination = islandBase.InverseTransformPoint(touristContainer.TransformPoint(destination));
            var baseRoute = new List<Vector2>();
            // Every Restaurant guest joins the authored approach behind
            // Reception. This prevents direct arrivals from choosing the
            // lower-beach shortcut around the palm and boulders.
            var foundRoute = isPurchasedDestination
                ? navigationMap.IsWalkable(baseDestination)
                    && navigationMap.TryFindPath(baseStart, baseDestination, baseRoute)
                : destinationIndex == 2
                ? TryBuildRestaurantRouteViaReceptionExit(
                    baseStart,
                    baseDestination,
                    baseRoute)
                : entryPoint != null || destinationIndex == 1
                ? TryFindWalkableRoute(baseStart, baseDestination, baseRoute)
                : navigationMap.TryFindPath(baseStart, baseDestination, baseRoute);
            if (!foundRoute && destinationIndex == 2)
                foundRoute = TryBuildRestaurantPortalRoute(
                    baseStart,
                    baseDestination,
                    baseRoute,
                    routine);
            if (!foundRoute)
            {
                if (isPurchasedDestination)
                    return false;

                if (!TryBuildPortalRoute(baseStart, baseDestination, baseRoute, routine))
                {
                    ReleaseBarSeat(routine);
                    Debug.LogWarning("No walkable island route to destination " + destinationIndex, this);
                    return false;
                }
            }
            if (leavingRestaurantStairs)
            {
                // The same pooled tourist starts at the landing where it was hidden.
                for (var index = routine.RestaurantStairs.Count - 2; index >= 0; index--)
                    routine.Route.Add(routine.RestaurantStairs[index]);
                routine.Route.Add(navigationStartPosition);
                routine.StairEndIndex = routine.Route.Count;
            }
            else if (Vector2.Distance(startPosition, navigationStartPosition) > 1f)
            {
                // Leave the stool straight down before joining the walkable aisle.
                routine.Route.Add(startPosition);
                routine.Route.Add(navigationStartPosition);
            }

            if (routine.PortalEntryRouteIndex >= 0)
                routine.PortalEntryRouteIndex += routine.Route.Count;
            if (routine.PortalExitRouteIndex >= 0)
                routine.PortalExitRouteIndex += routine.Route.Count;

            for (var index = 0; index < baseRoute.Count; index++)
                routine.Route.Add(touristContainer.InverseTransformPoint(islandBase.TransformPoint(baseRoute[index])));

            // Use the exact entrance or aisle point after the snapped navigation path.
            if (hasRestaurantStairs || barSeat.HasValue
                || Vector2.Distance(routine.Route[routine.Route.Count - 1], destination) > 1f)
                routine.Route.Add(destination);
            if (barSeat.HasValue)
                routine.Route.Add(barSeat.Value);
            if (hasRestaurantStairs)
            {
                routine.StairStartIndex = routine.Route.Count;
                routine.Route.AddRange(routine.RestaurantStairs);
                routine.StairEndIndex = routine.Route.Count;
            }

            routine.RouteIndex = 0;
            routine.AtDestination = false;
            routine.Completed = false;
            routine.DwellRemaining = 0f;
            routine.TalkingAnimationTime = 0f;
            routine.PathPosition = startPosition;
            routine.StartDelayRemaining = startDelay;
            routine.PortalDelayRemaining = 0f;
            // The ordinary walking layer stays behind buildings. Only the
            // exact final segment into a building entry is raised temporarily.
            if (routine.Tourist.transform.parent != touristContainer)
                routine.Tourist.transform.SetParent(touristContainer, false);
            routine.Tourist.transform.SetAsLastSibling();
            SetWalkingTouristScale(routine.Tourist);
            SetTouristPosition(routine);
            if (leavingRestaurantStairs)
            {
                var touristRect = routine.Tourist.GetComponent<RectTransform>();
                SetHorizontalFacing(touristRect, routine.Route[0].x - startPosition.x);
                SetRestaurantStairLayer(touristRect, true);
                SetWalkingPosition(touristRect, startPosition, true);
            }
            SetTouristActive(routine.Tourist, !routine.HideFromStartUntilPortalExit);
            return true;
        }

        private bool LoadRestaurantStairs(TouristRoutineState routine)
        {
            routine.RestaurantStairs.Clear();
            if (restaurantStairRoutes == null
                || !restaurantStairRoutes.TryGetWaypoints(touristContainer, routine.RestaurantStairs))
                return false;

            // Navigation positions use the tourist image's pivot. The stair markers
            // describe where the tourist's feet touch the artwork.
            var touristRect = routine.Tourist.GetComponent<RectTransform>();
            var footOffset = Vector2.up * (-touristRect.rect.yMin * WalkingTouristScale);
            for (var index = 0; index < routine.RestaurantStairs.Count; index++)
                routine.RestaurantStairs[index] += footOffset;
            return true;
        }

        private bool TryFindWalkableEntryRoute(Vector2 start, Vector2 entry, List<Vector2> result)
        {
            return navigationMap.TryFindPathToNearestReachable(start, entry, result);
        }

        private bool TryFindWalkableRoute(Vector2 start, Vector2 destination, List<Vector2> result)
        {
            return navigationMap.TryFindPathToNearestReachable(start, destination, result);
        }

        private bool TryBuildRestaurantRouteViaReceptionExit(
            Vector2 start,
            Vector2 destination,
            List<Vector2> result)
        {
            if (receptionExitPoint == null)
                return false;

            var receptionExit = GetPointPosition(receptionExitPoint);
            var baseReceptionExit = islandBase.InverseTransformPoint(
                touristContainer.TransformPoint(receptionExit));
            var barToReceptionExit = new List<Vector2>();
            var receptionExitToRestaurant = new List<Vector2>();
            var restaurantMap = restaurantRouteMap ?? navigationMap;
            if (!TryFindWalkableRoute(start, baseReceptionExit, barToReceptionExit)
                || barToReceptionExit.Count == 0
                || restaurantMap == null
                || !restaurantMap.TryFindPathToNearestReachable(
                    barToReceptionExit[barToReceptionExit.Count - 1],
                    destination,
                    receptionExitToRestaurant))
            {
                return false;
            }

            result.AddRange(barToReceptionExit);
            if (result.Count == 0
                || Vector2.Distance(result[result.Count - 1], baseReceptionExit) > 1f)
            {
                result.Add(baseReceptionExit);
            }

            for (var index = 0; index < receptionExitToRestaurant.Count; index++)
            {
                if (index == 0 && result.Count > 0
                    && Vector2.Distance(result[result.Count - 1], receptionExitToRestaurant[index]) <= 1f)
                {
                    continue;
                }

                result.Add(receptionExitToRestaurant[index]);
            }

            return result.Count > 1;
        }

        private bool TryBuildRestaurantPortalRoute(
            Vector2 start,
            Vector2 destination,
            List<Vector2> result,
            TouristRoutineState routine)
        {
            if (restaurantRouteMap == null
                || !restaurantRouteMap.TryFindPathToNearestReachable(start, destination, result))
                return false;

            if (receptionNavigationObstacle == null || result.Count < 2)
                return true;

            var receptionObstacle = BuildNavigationObstacle(receptionNavigationObstacle);
            var firstClearIndex = -1;
            for (var index = 0; index < result.Count; index++)
            {
                if (receptionObstacle.Contains(result[index]))
                    continue;

                firstClearIndex = index;
                break;
            }

            if (firstClearIndex > 0)
            {
                routine.PortalExitRouteIndex = firstClearIndex;
                routine.HideFromStartUntilPortalExit = true;
            }

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
                // Portal markers can intentionally sit inside the building art.
                // Route to the nearest connected walkable cells before hiding
                // or reappearing; requiring marker cells themselves would
                // reject a valid portal.
                var entryFound = TryFindWalkableRoute(baseStart, entry, entryRoute);
                var destinationFound = entryFound
                    && TryFindDestinationRoute(exit, baseDestination, destinationRoute, routine.RoutineIndex);
                if (!entryFound || !destinationFound)
                    continue;

                route.AddRange(entryRoute);
                routine.PortalEntryRouteIndex = route.Count - 1;
                routine.PortalExitRouteIndex = route.Count;
                route.AddRange(destinationRoute);
                return true;
            }

            return false;
        }

        private bool TryFindDestinationRoute(
            Vector2 start,
            Vector2 destination,
            List<Vector2> result,
            int destinationIndex)
        {
            return GetEntryPoint(destinationIndex) != null || destinationIndex == 1
                ? TryFindWalkableRoute(start, destination, result)
                : navigationMap.TryFindPath(start, destination, result);
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
                SetTouristPosition(routine);
                if (routine.PortalDelayRemaining <= 0f)
                    SetTouristActive(routine.Tourist, true);
                return;
            }

            var rectTransform = routine.Tourist.GetComponent<RectTransform>();
            if (rectTransform == null)
                return;

            var onRestaurantStairs = routine.IsOnRestaurantStairRoute;
            SetRestaurantStairLayer(rectTransform, onRestaurantStairs);
            var target = routine.Route[Mathf.Clamp(routine.RouteIndex, 0, routine.Route.Count - 1)];
            var current = routine.PathPosition;
            var next = Vector2.MoveTowards(current, target, walkSpeed * deltaTime);
            var movement = next - current;
            routine.PathPosition = next;
            SetHorizontalFacing(rectTransform, movement.x);
            bool reachedTarget = Vector2.Distance(next, target) <= 0.75f;

            var renderedPosition = next;
            var bob = Mathf.Sin((Time.unscaledTime + routine.RoutineIndex) * idleBobSpeed) * idleBobHeight;
            renderedPosition.y += onRestaurantStairs ? Mathf.Abs(bob) * 0.25f : bob;
            SetWalkingPosition(rectTransform, renderedPosition, onRestaurantStairs);
            if (!reachedTarget)
                return;

            routine.PathPosition = target;
            SetWalkingPosition(rectTransform, target, onRestaurantStairs);
            routine.RouteIndex++;
            if (routine.RouteIndex == routine.PortalExitRouteIndex)
            {
                HideAtBuildingEntrance(routine);
                routine.PathPosition = routine.Route[routine.PortalExitRouteIndex];
                SetTouristPosition(routine);
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
                var entryPoint = routine.RoutineIndex == 2 && routine.RestaurantStairs.Count > 0
                    ? null
                    : GetEntryPoint(routine.RoutineIndex);
                if (entryPoint != null)
                {
                    routine.PathPosition = GetPointPosition(entryPoint);
                    SetTouristPosition(routine);
                }

                HideAtBuildingEntrance(routine);
                if (routine.RoutineIndex == 2 && restaurantStairRoutes != null)
                    restaurantStairRoutes.ShowDiner(routine.Tourist);
                return;
            }

            if (routine.Activity == TouristActivity.SitAtDestination)
            {
                if (routine.BarSeatIndex < 0)
                {
                    routine.WaitingForBarSeat = true;
                    routine.DwellRemaining = 0f;
                    return;
                }

                PlaceRoutineAtBarSeat(routine);
            }
            else if (routine.Activity == TouristActivity.RelaxInPool)
            {
                routine.DwellRemaining = TryPlaceAtPoolSeat(routine) ? poolDwellSeconds : 0f;
            }
        }

        private bool TryPlaceAtPoolSeat(TouristRoutineState routine)
        {
            var levelIndex = GetBuildingLevel(BuildingId.Pool) - 1;
            if (poolSeatLayouts == null || levelIndex < 0 || levelIndex >= poolSeatLayouts.Length)
                return false;

            var layout = poolSeatLayouts[levelIndex];
            if (layout == null || !layout.gameObject.activeInHierarchy)
                return false;

            for (var index = 0; index < layout.childCount; index++)
            {
                var seat = layout.GetChild(index) as RectTransform;
                if (seat == null || !seat.gameObject.activeInHierarchy || IsPoolSeatOccupied(seat))
                    continue;

                routine.PoolSeat = seat;
                var tourist = (RectTransform)routine.Tourist.transform;
                tourist.SetParent(seat, false);
                // The marker is aligned to the lounger. Fit the existing guest
                // sprite inside its cushion without changing the walking pose.
                tourist.localRotation = Quaternion.identity;
                var scale = Mathf.Min(seat.rect.width / tourist.rect.width, seat.rect.height / tourist.rect.height);
                tourist.localScale = Vector3.one * scale;
                routine.PathPosition = Vector2.zero;
                SetTouristPosition(routine);
                return true;
            }

            return false;
        }

        private bool IsPoolSeatOccupied(RectTransform seat)
        {
            foreach (var routine in touristRoutines)
                if (routine.PoolSeat == seat && routine.Tourist != null)
                    return true;

            return false;
        }

        private void HideAtBuildingEntrance(TouristRoutineState routine)
        {
            var tourist = routine.Tourist;
            if (entranceSmoke != null && tourist.activeInHierarchy)
            {
                var rect = tourist.GetComponent<RectTransform>();
                var origin = new Vector3(rect.rect.center.x, rect.rect.yMin + rect.rect.height * 0.23f, 0f);
                entranceSmoke.PlayAt(rect.TransformPoint(origin));
            }

            SetTouristActive(tourist, false);
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
            var leavingPool = routine.RoutineIndex == PoolDestination;
            var departurePosition = GetDeparturePosition(routine);
            var navigationDeparturePosition = departurePosition;
            var leavingRestaurantStairs = routine.RoutineIndex == 2 && routine.RestaurantStairs.Count > 0;
            if (leavingRestaurantStairs)
                navigationDeparturePosition = GetPointPosition(restaurantPoint);
            if (routine.RoutineIndex == 1 && barSeatPoint != null)
                navigationDeparturePosition = GetBarApproachPosition(departurePosition);

            if (routine.RoutineIndex == 1)
                ReleaseBarSeat(routine);
            if (routine.RoutineIndex == 2 && restaurantStairRoutes != null)
                restaurantStairRoutes.ReleaseDiner(routine.Tourist);
            routine.PoolSeat = null;
            routine.Tourist.transform.SetParent(touristContainer, false);
            do
            {
                routine.RoutineIndex = (routine.RoutineIndex + 1) % DestinationCount;
                if (PrepareRoutine(routine, departurePosition, 0f, navigationDeparturePosition, leavingRestaurantStairs))
                    return;
            }
            while (routine.RoutineIndex >= PoolDestination);

            CompleteRoutine(routine);
            if (leavingPool)
                SetTouristActive(routine.Tourist, false);
        }

        private Vector2 GetDeparturePosition(TouristRoutineState routine)
        {
            if (routine.RoutineIndex == 0)
                return GetPointPosition(receptionExitPoint);

            if (routine.RoutineIndex == PoolDestination)
                return GetPointPosition(poolEntrancePoint);

            if (routine.RoutineIndex == 2)
            {
                if (routine.RestaurantStairs.Count > 0)
                    return routine.RestaurantStairs[routine.RestaurantStairs.Count - 1];

                // Restaurant guests are hidden at the entrance while inside.
                // Reappearing at the visual restaurant point would place the
                // NPC inside the building and make its exit cross the artwork
                // and support-post blockers before navigation even starts.
                var restaurantExit = GetEntryPoint(2);
                return restaurantExit != null
                    ? GetPointPosition(restaurantExit)
                    : GetPointPosition(restaurantPoint);
            }

            return touristContainer.InverseTransformPoint(routine.Tourist.transform.position);
        }

        private void PlaceAtBarSeat(TouristRoutineState routine, Vector2 seatPosition)
        {
            seatPosition += Vector2.up * SeatedTouristVerticalOffset;
            SetSeatedTouristScale(routine.Tourist);
            if (barSeatOverlay == null || touristContainer == null)
            {
                routine.PathPosition = seatPosition;
                SetTouristPosition(routine);
                return;
            }

            var seatWorldPosition = touristContainer.TransformPoint(seatPosition);
            var tourist = routine.Tourist.transform;
            tourist.SetParent(barSeatOverlay, false);
            routine.PathPosition = barSeatOverlay.InverseTransformPoint(seatWorldPosition);
            SetTouristPosition(routine);

            // Canvas siblings drawn later appear in front. Keep stools ordered
            // from left to right regardless of which guest sat down first.
            tourist.SetAsLastSibling();
            for (var index = 0; index < barSeatOverlay.childCount - 1; index++)
            {
                if (barSeatOverlay.GetChild(index).localPosition.x <= routine.PathPosition.x)
                    continue;

                tourist.SetSiblingIndex(index);
                break;
            }
        }

        private void PlaceRoutineAtBarSeat(TouristRoutineState routine)
        {
            var entryIndex = GetBarSeatEntryIndex(routine.BarSeatIndex);
            SetHorizontalFacing(
                routine.Tourist.GetComponent<RectTransform>(),
                GetBarSeatFacingDirection(entryIndex, routine.BarSeatIndex));
            PlaceAtBarSeat(routine, GetBarSeatPosition(routine.BarSeatIndex));
        }

        private void CompleteRoutine(TouristRoutineState routine)
        {
            ReleaseBarSeat(routine);
            routine.PoolSeat = null;
            routine.AtDestination = false;
            routine.Completed = true;
            routine.DwellRemaining = 0f;
        }

        private TouristActivity GetActivity(int destinationIndex)
        {
            switch (destinationIndex)
            {
                case 1: return TouristActivity.SitAtDestination;
                case PoolDestination: return TouristActivity.RelaxInPool;
                default: return TouristActivity.HideInside;
            }
        }

        private RectTransform GetEntryPoint(int destinationIndex)
        {
            switch (destinationIndex)
            {
                case 0: return GetReceptionEntryPoint();
                case 2: return restaurantEntryPoint;
                case PoolDestination: return poolEntrancePoint;
                case BungalowsDestination: return bungalowsEntrancePoint;
                default: return null;
            }
        }

        private bool IsDestinationAvailable(int destinationIndex)
        {
            var buildings = gameplayFacade?.Buildings;
            if (buildings == null)
                return false;

            var buildingId = destinationIndex == PoolDestination ? BuildingId.Pool : BuildingId.Bungalows;
            for (var index = 0; index < buildings.Count; index++)
            {
                if (buildings[index].BuildingId == buildingId)
                    return buildings[index].IsOwned;
            }

            return false;
        }

        private RectTransform GetReceptionEntryPoint()
        {
            var level = GetBuildingLevel(BuildingId.Reception);
            for (var index = 0; index < receptionLevelEntryPoints.Count; index++)
            {
                if (GetLevelFromMarkerName(receptionLevelEntryPoints[index].name, "ReceptionEntry_Lvl") == level)
                    return receptionLevelEntryPoints[index];
            }

            return receptionEntryPoint;
        }

        private Vector2 GetDestinationPosition(int destinationIndex)
        {
            var point = destinationIndex == 0
                ? receptionPoint
                : destinationIndex == 1
                    ? barSeatPoint
                    : restaurantPoint;
            return GetPointPosition(point);
        }

        private Vector2 GetBarApproachPosition(Vector2 seatPosition)
        {
            return new Vector2(seatPosition.x, GetPointPosition(barSeatPoint).y);
        }

        private Vector2 GetBarSeatPosition(int barSeatIndex)
        {
            var level = GetBarLevel();
            var entryIndex = GetBarSeatEntryIndex(barSeatIndex);
            // Preserve the authored level-1 seating that was already aligned
            // with the artwork. Later levels use their explicit scene markers.
            if (level == 2 && entryIndex >= 0 && barSeatOverlay != null)
            {
                var overlaySeatPosition = LevelTwoVisualSeatPositions[
                    barSeatIndex % LevelTwoVisualSeatPositions.Length];
                return touristContainer.InverseTransformPoint(
                    barSeatOverlay.TransformPoint(overlaySeatPosition));
            }

            if (level > 1 && entryIndex >= 0)
                return GetUpgradedBarSeatPosition(barSeatEntryPoints[entryIndex], level);

            return GetPointPosition(barSeatPoint)
                + BarSeatVisualOffsets[barSeatIndex % BarSeatVisualOffsets.Length];
        }

        private Vector2 GetUpgradedBarSeatPosition(RectTransform marker, int level)
        {
            var offset = level == 2 ? LevelTwoBarSeatFallbackOffset : LevelThreeBarSeatOffset;
            return GetPointPosition(marker) + Vector2.up * offset;
        }

        private int GetBarSeatEntryIndex(int barSeatIndex)
        {
            var level = GetBarLevel();
            var firstMatch = -1;
            var matchCount = 0;
            for (var index = 0; index < barSeatEntryPoints.Count; index++)
            {
                if (GetBarSeatEntryLevel(barSeatEntryPoints[index]) != level)
                    continue;

                if (firstMatch < 0)
                    firstMatch = index;
                matchCount++;
            }

            if (matchCount == 0)
                return -1;

            var targetMatch = barSeatIndex % matchCount;
            for (var index = firstMatch; index < barSeatEntryPoints.Count; index++)
            {
                if (GetBarSeatEntryLevel(barSeatEntryPoints[index]) != level)
                    continue;

                if (targetMatch-- == 0)
                    return index;
            }

            return -1;
        }

        private int GetBarSeatCapacity()
        {
            var level = GetBarLevel();
            var capacity = 0;
            for (var index = 0; index < barSeatEntryPoints.Count; index++)
            {
                if (GetBarSeatEntryLevel(barSeatEntryPoints[index]) == level)
                    capacity++;
            }

            // Level 1 uses the four existing authored offsets rather than
            // scene markers. Higher levels must declare their own stools.
            return level == 1
                ? BarSeatVisualOffsets.Length
                : capacity;
        }

        private bool TryReserveBarSeat(TouristRoutineState routine)
        {
            if (routine.BarSeatIndex >= 0)
                return true;

            var capacity = GetBarSeatCapacity();
            if (capacity <= 0)
                return false;

            // Keep the established Level-1 visual ordering where possible,
            // then choose the next unreserved stool.
            var preferredSeat = routine.SpawnSlot % capacity;
            for (var offset = 0; offset < capacity; offset++)
            {
                var candidate = (preferredSeat + offset) % capacity;
                var occupied = false;
                for (var index = 0; index < touristRoutines.Count; index++)
                {
                    var other = touristRoutines[index];
                    if (other != null && other != routine
                        && other.BarSeatIndex == candidate)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (occupied)
                    continue;

                routine.BarSeatIndex = candidate;
                return true;
            }

            return false;
        }

        private static void ReleaseBarSeat(TouristRoutineState routine)
        {
            if (routine == null)
                return;

            routine.BarSeatIndex = -1;
            routine.WaitingForBarSeat = false;
        }

        private int GetBarSeatEntryLevel(RectTransform entry)
        {
            var prefix = "BarSeatEntry_Lvl";
            var levelStart = prefix.Length;
            var levelEnd = entry.name.IndexOf('_', levelStart);
            if (levelEnd <= levelStart
                || !int.TryParse(entry.name.Substring(levelStart, levelEnd - levelStart), out var level))
                return 1;

            return Mathf.Max(1, level);
        }

        private float GetBarSeatFacingDirection(int entryIndex, int barSeatIndex)
        {
            if (entryIndex >= 0)
            {
                var markerScale = barSeatEntryPoints[entryIndex].localScale.x;
                if (Mathf.Abs(markerScale) > 0.01f)
                    return Mathf.Sign(markerScale);
            }

            return BarSeatFacingDirections[barSeatIndex % BarSeatFacingDirections.Length];
        }

        private int GetBarLevel()
        {
            return GetBuildingLevel(BuildingId.BeachBar);
        }

        private int GetBuildingLevel(BuildingId buildingId)
        {
            if (ResortDebugLevelOverride.CurrentLevel.HasValue)
                return ResortDebugLevelOverride.CurrentLevel.Value;

            if (gameplayFacade != null && gameplayFacade.Buildings != null)
            {
                for (var index = 0; index < gameplayFacade.Buildings.Count; index++)
                {
                    var building = gameplayFacade.Buildings[index];
                    if (building.BuildingId == buildingId)
                        return Mathf.Max(1, building.Level);
                }
            }

            return 1;
        }

        private static int GetLevelFromMarkerName(string markerName, string prefix)
        {
            var levelStart = prefix.Length;
            var levelEnd = markerName.IndexOf('_', levelStart);
            if (levelEnd <= levelStart
                || !int.TryParse(markerName.Substring(levelStart, levelEnd - levelStart), out var level))
                return 1;

            return Mathf.Max(1, level);
        }

        private void SetTouristPosition(TouristRoutineState routine)
        {
            var rectTransform = routine.Tourist.GetComponent<RectTransform>();
            if (rectTransform != null)
                rectTransform.anchoredPosition = routine.PathPosition;
        }

        private void SetWalkingTouristScale(GameObject tourist)
        {
            var rectTransform = tourist == null ? null : tourist.GetComponent<RectTransform>();
            if (rectTransform == null)
                return;

            var direction = rectTransform.localScale.x < 0f ? -1f : 1f;
            rectTransform.localScale = new Vector3(
                direction * WalkingTouristScale,
                WalkingTouristScale,
                1f);
        }

        private void SetSeatedTouristScale(GameObject tourist)
        {
            var rectTransform = tourist == null ? null : tourist.GetComponent<RectTransform>();
            if (rectTransform == null)
                return;

            var direction = rectTransform.localScale.x < 0f ? -1f : 1f;
            rectTransform.localScale = new Vector3(
                direction * SeatedTouristScale,
                SeatedTouristScale,
                1f);
        }

        private bool IsAnyTouristEnteringBuilding()
        {
            for (var index = 0; index < touristRoutines.Count; index++)
            {
                var routine = touristRoutines[index];
                if (routine == null || routine.Completed || routine.AtDestination
                    || routine.RoutineIndex == 1 || routine.IsOnRestaurantStairRoute)
                {
                    continue;
                }

                if (routine.RouteIndex >= routine.Route.Count - 1)
                    return true;
            }

            return false;
        }

        private void SetRestaurantStairLayer(RectTransform tourist, bool foreground)
        {
            if (restaurantStairRoutes == null)
                return;

            var stairLayer = restaurantStairRoutes.transform;
            if (foreground && tourist.parent != stairLayer)
                tourist.SetParent(stairLayer, true);
            else if (!foreground && tourist.parent == stairLayer)
                tourist.SetParent(touristContainer, true);
        }

        private void SetWalkingPosition(RectTransform tourist, Vector2 position, bool onRestaurantStairs)
        {
            if (!onRestaurantStairs)
            {
                tourist.anchoredPosition = position;
                return;
            }

            tourist.position = touristContainer.TransformPoint(position);

            // Draw the guest farther down the stairs in front when two guests pass.
            var layer = tourist.parent;
            var current = tourist.GetSiblingIndex();
            var index = current;
            var height = tourist.localPosition.y;
            while (index > 0 && layer.GetChild(index - 1).localPosition.y < height)
                index--;
            while (index < layer.childCount - 1 && layer.GetChild(index + 1).localPosition.y > height)
                index++;
            if (index != current)
                tourist.SetSiblingIndex(index);
        }

        private void SetTouristEntryRenderLayer(bool foreground)
        {
            if (touristContainer == null || receptionNavigationObstacle == null)
                return;

            var receptionVisual = receptionNavigationObstacle.parent as RectTransform;
            if (receptionVisual == null || receptionVisual.parent != touristContainer.parent)
                return;

            var currentIndex = touristContainer.GetSiblingIndex();
            var targetIndex = foreground
                ? touristContainer.parent.childCount - 1
                : Mathf.Max(0, receptionVisual.GetSiblingIndex() - 1);
            if (foreground && barSeatOverlay != null && barSeatOverlay.parent == touristContainer.parent)
            {
                var seatIndex = barSeatOverlay.GetSiblingIndex();
                targetIndex = currentIndex < seatIndex ? seatIndex - 1 : seatIndex;
            }
            if (currentIndex != targetIndex)
                touristContainer.SetSiblingIndex(targetIndex);
        }

        private static void SetTouristActive(GameObject tourist, bool active)
        {
            if (tourist != null && tourist.activeSelf != active)
                tourist.SetActive(active);
        }

        private static Vector2 GetSpawnSpreadOffset(int spawnSlot)
        {
            return SpawnSpreadOffsets[spawnSlot % SpawnSpreadOffsets.Length];
        }

        private Vector2 GetPointPosition(RectTransform point)
        {
            if (point == null || touristContainer == null)
                return Vector2.zero;

            if (touristContainer.parent != null && !point.transform.IsChildOf(touristContainer.parent))
            {
                var camera = Camera.main;
                if (camera != null && islandBase != null)
                {
                    var screenPoint = RectTransformUtility.WorldToScreenPoint(null, point.position);
                    var depth = Mathf.Abs(camera.transform.position.z - islandBase.position.z);
                    var islandWorldPoint = camera.ScreenToWorldPoint(
                        new Vector3(screenPoint.x, screenPoint.y, depth));
                    return touristContainer.InverseTransformPoint(islandWorldPoint);
                }
            }

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
