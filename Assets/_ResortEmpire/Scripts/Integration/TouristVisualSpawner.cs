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
            public bool HideFromStartUntilPortalExit;
            public int BarSeatIndex = -1;
            public bool WaitingForBarSeat;
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

            if (navigationMap == null)
            {
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
                        Debug.LogWarning("Tourist visuals cannot spawn because no walkable path layer or navigation map is assigned.", this);
                        return;
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
                        navigationMap = new IslandNavigationMap(navigationMapTexture, islandBase.rect);
                        restaurantRouteMap = null;
                    }
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
                if (!PrepareRoutine(routine, routineSpawnPosition, routine.StaggerDelay))
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
            return obstacles;
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
            var preserveProtectedArea = !hardObstacle;
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
            for (var index = 0; index < touristRoutines.Count; index++)
            {
                var routine = touristRoutines[index];
                if (routine == null || routine.Tourist == null)
                    continue;

                if (routine.Completed)
                    continue;

                if (routine.AtDestination)
                {
                    if (routine.Activity == TouristActivity.SitAtDestination
                        && routine.WaitingForBarSeat)
                    {
                        if (TryReserveBarSeat(routine))
                        {
                            routine.WaitingForBarSeat = false;
                            routine.DwellRemaining = seatedDwellSeconds;
                            routine.TalkingAnimationTime = 0f;
                            PlaceRoutineAtBarSeat(routine);
                        }

                        continue;
                    }

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
            Vector2 navigationStartPosition)
        {
            routine.Route.Clear();
            routine.PortalEntryRouteIndex = -1;
            routine.PortalExitRouteIndex = -1;
            routine.HideFromStartUntilPortalExit = false;
            int destinationIndex = routine.RoutineIndex;
            routine.Activity = GetActivity(destinationIndex);
            var hasBarSeat = destinationIndex != 1 || TryReserveBarSeat(routine);
            routine.WaitingForBarSeat = destinationIndex == 1 && !hasBarSeat;
            var entryPoint = GetEntryPoint(destinationIndex);
            var destination = entryPoint == null
                ? GetDestinationPosition(destinationIndex, routine.BarSeatIndex)
                : GetPointPosition(entryPoint);
            var baseStart = islandBase.InverseTransformPoint(
                touristContainer.TransformPoint(navigationStartPosition));
            var baseDestination = islandBase.InverseTransformPoint(touristContainer.TransformPoint(destination));
            var baseRoute = new List<Vector2>();
            // Every Restaurant guest joins the authored approach behind
            // Reception. This prevents direct arrivals from choosing the
            // lower-beach shortcut around the palm and boulders.
            var foundRoute = destinationIndex == 2
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
                if (!TryBuildPortalRoute(baseStart, baseDestination, baseRoute, routine))
                {
                    ReleaseBarSeat(routine);
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
            // entrance marker as the final point so the building guest reaches
            // the authored door position before hiding.
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
            // The ordinary walking layer stays behind buildings. Only the
            // exact final segment into a building entry is raised temporarily.
            if (routine.Tourist.transform.parent != touristContainer)
                routine.Tourist.transform.SetParent(touristContainer, false);
            routine.Tourist.transform.SetAsLastSibling();
            SetWalkingTouristScale(routine.Tourist);
            SetTouristPosition(routine);
            SetTouristActive(routine.Tourist, !routine.HideFromStartUntilPortalExit);
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

        private bool TryFindBarSeatRoute(Vector2 start, Vector2 destination, List<Vector2> result)
        {
            return navigationMap.TryFindPathToNearestReachableInStartRegion(start, destination, result);
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
            return GetEntryPoint(destinationIndex) != null
                ? TryFindWalkableRoute(start, destination, result)
                : destinationIndex == 1
                    ? TryFindBarSeatRoute(start, destination, result)
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
                routine.PathPosition = routine.Route[routine.PortalExitRouteIndex];
                SetTouristPosition(routine);
                SetTouristActive(routine.Tourist, false);
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
                var entryPoint = GetEntryPoint(routine.RoutineIndex);
                if (entryPoint != null)
                {
                    routine.PathPosition = GetPointPosition(entryPoint);
                    SetTouristPosition(routine);
                }

                SetTouristActive(routine.Tourist, false);
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

            if (routine.RoutineIndex == 1)
                ReleaseBarSeat(routine);
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
            {
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
            routine.Tourist.transform.SetParent(barSeatOverlay, false);
            routine.PathPosition = barSeatOverlay.InverseTransformPoint(seatWorldPosition);
            SetTouristPosition(routine);
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

        private RectTransform GetEntryPoint(int destinationIndex)
        {
            return destinationIndex == 0
                ? GetReceptionEntryPoint()
                : destinationIndex == 2
                    ? restaurantEntryPoint
                    : null;
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

        private Vector2 GetDestinationPosition(int destinationIndex, int barSeatIndex)
        {
            RectTransform point = destinationIndex == 0
                ? receptionPoint
                : destinationIndex == 1
                    ? barSeatPoint
                    : restaurantPoint;

            var destination = GetPointPosition(point);

            if (destinationIndex == 1)
                return barSeatIndex >= 0
                    ? GetBarSeatPosition(barSeatIndex)
                    : destination;

            return destination;
        }

        private Vector2 GetBarSeatPosition(int barSeatIndex)
        {
            var entryIndex = GetBarSeatEntryIndex(barSeatIndex);
            // Preserve the authored level-1 seating that was already aligned
            // with the artwork. Later levels use their explicit scene markers.
            if (GetBarLevel() > 1 && entryIndex >= 0)
                return GetPointPosition(barSeatEntryPoints[entryIndex]);

            return GetPointPosition(barSeatPoint)
                + BarSeatVisualOffsets[barSeatIndex % BarSeatVisualOffsets.Length];
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
                    || routine.RoutineIndex == 1)
                {
                    continue;
                }

                if (routine.RouteIndex >= routine.Route.Count - 1)
                    return true;
            }

            return false;
        }

        private void SetTouristEntryRenderLayer(bool foreground)
        {
            if (touristContainer == null || receptionNavigationObstacle == null)
                return;

            var receptionVisual = receptionNavigationObstacle.parent as RectTransform;
            if (receptionVisual == null || receptionVisual.parent != touristContainer.parent)
                return;

            var targetIndex = foreground
                ? touristContainer.parent.childCount - 1
                : Mathf.Max(0, receptionVisual.GetSiblingIndex() - 1);
            if (touristContainer.GetSiblingIndex() != targetIndex)
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
