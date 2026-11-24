using System;
using System.Collections.Generic;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Progression;
using ResortEmpire.Gameplay.Waves;
using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// Presentation-only queue feedback for active waves. Waiting guests are drawn in a line at
    /// each building that has a service queue, and occasional bubbles show how satisfied guests
    /// are. Everything is derived from read-only wave snapshots and completion events, so this
    /// component never creates demand, starts service or changes satisfaction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GuestQueueVisualizer : MonoBehaviour
    {
        private const float GuestScale = 1.2f; // Matches the roaming tourists.
        private const float BubblePopSeconds = 0.18f;
        private const float BubbleFadeSeconds = 0.35f;
        private const float BubbleRiseDistance = 6f;
        private const float GuestFadeSpeed = 4f;
        private const float LeaveFadeDelay = 0.25f;

        private static readonly BuildingId[] LayoutOrder =
        {
            BuildingId.Reception,
            BuildingId.Restaurant,
            BuildingId.BeachBar,
            BuildingId.Pool,
            BuildingId.Bungalows
        };

        private static readonly string[] DefaultBuildingArtNames =
        {
            "Reception",
            "RestaurantSprite",
            "BeachBarSprite",
            "PoolLockedSprite",
            "BungalowsLockedSprite"
        };

        [SerializeField]
        private ResortGameplayBootstrap gameplayBootstrap;

        [SerializeField]
        private TouristVisualSpawner touristSpawner;

        [SerializeField, Tooltip("Building artwork waiting guests must never overlap. Found by name when empty.")]
        private Image[] buildingArt;

        [SerializeField, Tooltip("Authored queue lines. Each line's active children are points from the front of the queue to the back. "
            + "Buildings without a line follow the walkable route from their entrance toward the pier.")]
        private QueueLine[] queueLines;

        [Header("Bubbles")]
        [SerializeField] private Sprite happyBubble;
        [SerializeField] private Sprite waitingBubble;
        [SerializeField] private Sprite unhappyBubble;
        [SerializeField] private Sprite crowdedBubble;
        [SerializeField] private Vector2 bubbleSize = new Vector2(58f, 54f);
        [SerializeField, Tooltip("Height above a waiting guest's centre where a bubble's tail points.")]
        private float guestBubbleHeight = 24f;
        [SerializeField, Tooltip("Height above a building's entrance where its bubble's tail points.")]
        private float buildingBubbleHeight = 44f;

        [Header("Queue")]
        [SerializeField] private GuestQueueFeedbackSettings settings = new GuestQueueFeedbackSettings();
        [SerializeField, Min(8f)] private float guestSpacing = 30f;
        [SerializeField, Min(0f), Tooltip("Distance from the entrance marker to the first waiting guest on an automatic route.")]
        private float frontGuestOffset = 12f;
        [SerializeField, Min(0f), Tooltip("A line whose first clear position is farther than this from its start shows no guests, "
            + "so a blocked entrance never sends its queue somewhere unrelated on the island.")]
        private float maxFrontGap = 45f;
        [SerializeField, Min(1f)] private float walkSpeed = 70f;
        [SerializeField, Min(0f)] private float idleBobHeight = 1.2f;
        [SerializeField, Min(0f)] private float idleBobSpeed = 2.2f;
        [SerializeField, Min(0.05f), Tooltip("Seconds between reads of the wave's queue lengths.")]
        private float refreshInterval = 0.25f;

        private readonly Dictionary<BuildingId, QueueView> views = new Dictionary<BuildingId, QueueView>();
        private readonly List<QueueGuest> leavingGuests = new List<QueueGuest>();
        private readonly Stack<QueueGuest> guestPool = new Stack<QueueGuest>();
        private readonly List<Bubble> bubbles = new List<Bubble>();
        private readonly Stack<Bubble> bubblePool = new Stack<Bubble>();
        private readonly List<GuestBubbleRequest> bubbleRequests = new List<GuestBubbleRequest>();
        private readonly List<Vector2> routeBuffer = new List<Vector2>();
        private readonly List<Vector2> reservedSlots = new List<Vector2>();
        private readonly List<ArtShape> artShapes = new List<ArtShape>();

        private IResortGameplayUiFacade gameplay;
        private GuestQueueFeedbackModel model;
        private RectTransform guestLayer;
        private RectTransform bubbleLayer;
        private bool subscribed;
        private bool layoutsDirty = true;
        private bool showing;
        private float refreshRemaining;
        private int spriteCursor;

        /// <summary>Waiting guests currently standing in line at <paramref name="buildingId"/>.</summary>
        public int GetVisibleGuestCount(BuildingId buildingId)
        {
            return views.TryGetValue(buildingId, out var view) ? view.Line.Count : 0;
        }

        /// <summary>Queue positions available at <paramref name="buildingId"/> for the current layout.</summary>
        public IReadOnlyList<Vector2> GetQueueSlots(BuildingId buildingId)
        {
            return views.TryGetValue(buildingId, out var view) ? view.Slots : (IReadOnlyList<Vector2>)Array.Empty<Vector2>();
        }

        /// <summary>Kinds of the satisfaction bubbles currently on screen.</summary>
        public void GetActiveBubbleKinds(List<GuestBubbleKind> results)
        {
            results.Clear();
            foreach (var bubble in bubbles)
                results.Add(bubble.Kind);
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResortDebugLevelOverride.Changed += HandleDebugLevelChanged;
            Bind();
        }

        private void OnDisable()
        {
            ResortDebugLevelOverride.Changed -= HandleDebugLevelChanged;
            Unbind();
            HideAll();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Update()
        {
            Step(Time.unscaledDeltaTime);
        }

        private void Step(float deltaTime)
        {
            if (!subscribed)
                Bind();

            if (!ShouldShow())
            {
                if (showing)
                    HideAll();
                return;
            }

            if (!showing)
            {
                showing = true;
                refreshRemaining = 0f;
            }

            // A paused wave freezes the island, matching the roaming tourists.
            if (gameplay.CurrentPhase == GamePhase.WAVE_PAUSED)
                return;

            refreshRemaining -= deltaTime;
            if (refreshRemaining <= 0f)
            {
                refreshRemaining = refreshInterval;
                RefreshQueues();
            }

            if (model != null)
            {
                bubbleRequests.Clear();
                model.Advance(deltaTime, gameplay.SimulationTime, bubbles.Count, bubbleRequests);
                foreach (var request in bubbleRequests)
                    ShowBubble(request);
            }

            AnimateGuests(deltaTime);
            AnimateBubbles(deltaTime);
        }

        private void ResolveReferences()
        {
            if (gameplayBootstrap == null)
                gameplayBootstrap = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (touristSpawner == null)
                touristSpawner = GetComponent<TouristVisualSpawner>();
            if (touristSpawner == null)
                touristSpawner = FindAnyObjectByType<TouristVisualSpawner>();
        }

        private void Bind()
        {
            if (subscribed)
                return;

            ResolveReferences();
            if (gameplayBootstrap == null)
                return;

            gameplay = gameplayBootstrap;
            gameplay.NeedCompleted += HandleNeedCompleted;
            gameplay.BuildingsChanged += HandleBuildingsChanged;
            subscribed = true;
        }

        private void Unbind()
        {
            if (!subscribed || gameplay == null)
                return;

            gameplay.NeedCompleted -= HandleNeedCompleted;
            gameplay.BuildingsChanged -= HandleBuildingsChanged;
            gameplay = null;
            subscribed = false;
        }

        private bool ShouldShow()
        {
            return gameplay != null
                && touristSpawner != null
                && touristSpawner.TouristContainer != null
                && touristSpawner.ActiveTouristCount > 0
                && IsWavePhase(gameplay.CurrentPhase);
        }

        private void HandleNeedCompleted(NeedCompletionRecord record)
        {
            if (showing && model != null)
                model.ObserveCompletion(record);
        }

        private void HandleBuildingsChanged(IReadOnlyList<BuildingStateSnapshot> _)
        {
            layoutsDirty = true;
        }

        private void HandleDebugLevelChanged(int? _)
        {
            layoutsDirty = true;
        }

        private void RefreshQueues()
        {
            var snapshot = gameplay.CurrentWaveSnapshot;
            if (snapshot == null || snapshot.WaveId <= 0)
                return;

            EnsureModel();
            EnsureLayers();
            if (layoutsDirty)
                RebuildLayouts(snapshot);

            var simulationTime = gameplay.SimulationTime;
            foreach (var buildingId in LayoutOrder)
            {
                var waiting = 0;
                foreach (var need in snapshot.Needs)
                {
                    if (need.BuildingId == buildingId)
                    {
                        waiting = need.WaitingQueueLength;
                        break;
                    }
                }

                model.ObserveQueue(snapshot.WaveId, buildingId, waiting, simulationTime);
                if (views.TryGetValue(buildingId, out var view))
                    SyncLine(view, Mathf.Min(model.GetVisibleGuests(buildingId), view.Slots.Count));
            }

            SortGuestDepth();
        }

        private void EnsureModel()
        {
            if (model != null)
                return;

            model = new GuestQueueFeedbackModel(settings, ReadPatienceGrace());
        }

        private double ReadPatienceGrace()
        {
            try
            {
                var config = gameplayBootstrap == null ? null : gameplayBootstrap.ProgressionConfiguration;
                if (config != null)
                    return config.PatienceGrace;
            }
            catch (ArgumentException)
            {
                // An invalid economy asset is reported by the bootstrap; bubbles fall back below.
            }
            catch (InvalidOperationException)
            {
            }

            return settings.FallbackPatienceGrace;
        }

        private void EnsureLayers()
        {
            var container = touristSpawner.TouristContainer;
            if (guestLayer == null)
            {
                // Inside the tourist container so waiting guests share its depth relative to buildings.
                guestLayer = CreateLayer("GuestQueueLayer", container);
                guestLayer.SetAsFirstSibling();
            }

            if (bubbleLayer == null)
            {
                var parent = container.parent as RectTransform ?? container;
                bubbleLayer = CreateLayer("GuestFeedbackBubbles", parent);
                bubbleLayer.SetAsLastSibling();
                // A nested canvas keeps bubbles above building art whatever the sibling order,
                // and isolates their per-frame animation from the island canvas batch.
                var parentCanvas = parent.GetComponentInParent<Canvas>();
                var canvas = bubbleLayer.gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                if (parentCanvas != null)
                {
                    canvas.sortingLayerID = parentCanvas.sortingLayerID;
                    canvas.sortingOrder = parentCanvas.sortingOrder + 5;
                }
            }
        }

        private static RectTransform CreateLayer(string layerName, Transform parent)
        {
            var layerObject = new GameObject(layerName, typeof(RectTransform));
            var layer = (RectTransform)layerObject.transform;
            layer.SetParent(parent, false);
            layer.anchorMin = layer.anchorMax = new Vector2(0.5f, 0.5f);
            layer.pivot = new Vector2(0.5f, 0.5f);
            layer.sizeDelta = Vector2.zero;
            layer.anchoredPosition = Vector2.zero;
            return layer;
        }

        private void RebuildLayouts(WaveSnapshot snapshot)
        {
            layoutsDirty = false;
            CollectArtShapes();
            reservedSlots.Clear();
            foreach (var buildingId in LayoutOrder)
            {
                if (!views.TryGetValue(buildingId, out var view))
                {
                    view = new QueueView(buildingId);
                    views.Add(buildingId, view);
                }

                view.Slots.Clear();
                view.SpawnPoints.Clear();
                view.Route.Clear();
                var authored = TryGetAuthoredLine(buildingId, routeBuffer);
                if (HasNeed(snapshot, buildingId) && (authored || touristSpawner.TryGetQueueRoute(buildingId, routeBuffer)))
                {
                    view.Route.AddRange(routeBuffer);
                    var distances = new List<float>(settings.MaxVisibleGuests);
                    GuestQueueLayout.PlaceSlots(
                        view.Route,
                        settings.MaxVisibleGuests,
                        authored ? 0f : frontGuestOffset,
                        guestSpacing,
                        guestSpacing * 0.75f,
                        IsClearOfBuildings,
                        reservedSlots,
                        view.Slots,
                        distances);
                    if (distances.Count > 0 && distances[0] - (authored ? 0f : frontGuestOffset) > maxFrontGap)
                    {
                        view.Slots.Clear();
                        distances.Clear();
                    }

                    var length = GuestQueueLayout.GetLength(view.Route);
                    foreach (var distance in distances)
                        view.SpawnPoints.Add(GuestQueueLayout.Sample(view.Route, Mathf.Min(length, distance + guestSpacing)));
                    reservedSlots.AddRange(view.Slots);
                }

                // Guests beyond the new layout leave; the rest walk to their new places.
                SyncLine(view, Mathf.Min(view.Line.Count, view.Slots.Count));
            }
        }

        /// <summary>Copies an authored line's active points, front first, into tourist-container space.</summary>
        private bool TryGetAuthoredLine(BuildingId buildingId, List<Vector2> route)
        {
            route.Clear();
            if (queueLines == null)
                return false;

            var container = touristSpawner.TouristContainer;
            foreach (var line in queueLines)
            {
                if (line.Building != buildingId || line.Points == null)
                    continue;

                foreach (Transform point in line.Points)
                {
                    if (point.gameObject.activeSelf)
                        route.Add(container.InverseTransformPoint(point.position));
                }

                break;
            }

            return route.Count > 0;
        }

        /// <summary>The wave only has need flows for owned buildings, so only those get a queue line.</summary>
        private static bool HasNeed(WaveSnapshot snapshot, BuildingId buildingId)
        {
            foreach (var need in snapshot.Needs)
            {
                if (need.BuildingId == buildingId)
                    return true;
            }

            return false;
        }

        private void CollectArtShapes()
        {
            artShapes.Clear();
            var container = touristSpawner.TouristContainer;
            if (buildingArt == null || buildingArt.Length == 0)
                buildingArt = FindDefaultBuildingArt(container);

            foreach (var art in buildingArt)
            {
                if (art != null && art.sprite != null && art.isActiveAndEnabled)
                    artShapes.Add(ArtShape.Create(art, container));
            }
        }

        private static Image[] FindDefaultBuildingArt(Transform container)
        {
            var root = container.parent != null ? container.parent : container;
            var results = new List<Image>();
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (Array.IndexOf(DefaultBuildingArtNames, image.name) >= 0)
                    results.Add(image);
            }

            return results.ToArray();
        }

        /// <summary>True when a waiting guest centred at <paramref name="position"/> keeps its whole body off building art.</summary>
        private bool IsClearOfBuildings(Vector2 position)
        {
            if (artShapes.Count == 0)
                return true;

            var half = GetGuestHalfSize();
            // The tourist art has transparent margins, so test an inset outline of the body.
            var probes = new[]
            {
                position,
                position + new Vector2(0f, half.y * 0.75f),
                position + new Vector2(-half.x * 0.6f, half.y * 0.4f),
                position + new Vector2(half.x * 0.6f, half.y * 0.4f),
                position + new Vector2(-half.x * 0.6f, -half.y * 0.2f),
                position + new Vector2(half.x * 0.6f, -half.y * 0.2f),
                position + new Vector2(-half.x * 0.45f, -half.y * 0.85f),
                position + new Vector2(half.x * 0.45f, -half.y * 0.85f)
            };

            foreach (var shape in artShapes)
            {
                foreach (var probe in probes)
                {
                    if (shape.Contains(probe))
                        return false;
                }
            }

            return true;
        }

        private Vector2 GetGuestHalfSize()
        {
            var prefab = touristSpawner.TouristPrefab;
            var rect = prefab == null ? null : prefab.GetComponent<RectTransform>();
            var size = rect == null ? new Vector2(40f, 48f) : rect.sizeDelta;
            return size * (GuestScale * 0.5f);
        }

        private void SyncLine(QueueView view, int visible)
        {
            var line = view.Line;
            while (line.Count > visible)
            {
                // The front guest has started service: they step into the building and fade out.
                var guest = line[0];
                line.RemoveAt(0);
                guest.Leaving = true;
                guest.LeaveDelay = LeaveFadeDelay;
                guest.Target = view.Route.Count > 0 ? view.Route[0] : guest.Position;
                leavingGuests.Add(guest);
            }

            while (line.Count < visible)
            {
                var guest = AcquireGuest();
                var index = line.Count;
                guest.Position = index < view.SpawnPoints.Count ? view.SpawnPoints[index] : view.Slots[index];
                guest.Alpha = 0f;
                guest.Phase = UnityEngine.Random.value * Mathf.PI * 2f;
                line.Add(guest);
            }

            for (var index = 0; index < line.Count; index++)
            {
                var guest = line[index];
                guest.Target = view.Slots[index];
                guest.FacingTarget = index == 0
                    ? (view.Route.Count > 0 ? view.Route[0] : view.Slots[0])
                    : view.Slots[index - 1];
            }
        }

        private QueueGuest AcquireGuest()
        {
            QueueGuest guest;
            if (guestPool.Count > 0)
            {
                guest = guestPool.Pop();
            }
            else
            {
                var prefab = touristSpawner.TouristPrefab;
                var instance = prefab != null
                    ? Instantiate(prefab, guestLayer, false)
                    : new GameObject("QueueGuest", typeof(RectTransform), typeof(Image));
                instance.name = "QueueGuest";
                guest = new QueueGuest
                {
                    Rect = (RectTransform)instance.transform,
                    Image = instance.GetComponent<Image>()
                };
                if (guest.Image != null)
                    guest.Image.raycastTarget = false;
            }

            guest.Rect.SetParent(guestLayer, false);
            guest.Rect.localRotation = Quaternion.identity;
            guest.Rect.localScale = Vector3.one * GuestScale;
            guest.Leaving = false;
            var sprites = touristSpawner.TouristSprites;
            if (guest.Image != null && sprites != null && sprites.Count > 0)
            {
                guest.Image.sprite = sprites[spriteCursor % sprites.Count];
                spriteCursor++;
            }

            guest.Rect.gameObject.SetActive(true);
            return guest;
        }

        private void ReleaseGuest(QueueGuest guest)
        {
            foreach (var bubble in bubbles)
            {
                if (bubble.Speaker == guest)
                    bubble.DetachSpeaker();
            }

            guest.Rect.gameObject.SetActive(false);
            guestPool.Push(guest);
        }

        private void AnimateGuests(float deltaTime)
        {
            var time = Time.unscaledTime;
            foreach (var view in views.Values)
            {
                foreach (var guest in view.Line)
                {
                    guest.Alpha = Mathf.MoveTowards(guest.Alpha, 1f, deltaTime * GuestFadeSpeed);
                    MoveGuest(guest, deltaTime, time);
                }
            }

            for (var index = leavingGuests.Count - 1; index >= 0; index--)
            {
                var guest = leavingGuests[index];
                guest.LeaveDelay -= deltaTime;
                if (guest.LeaveDelay <= 0f)
                    guest.Alpha = Mathf.MoveTowards(guest.Alpha, 0f, deltaTime * GuestFadeSpeed);
                MoveGuest(guest, deltaTime, time);
                if (guest.Alpha > 0f)
                    continue;

                leavingGuests.RemoveAt(index);
                ReleaseGuest(guest);
            }
        }

        private void MoveGuest(QueueGuest guest, float deltaTime, float time)
        {
            var previous = guest.Position;
            guest.Position = Vector2.MoveTowards(guest.Position, guest.Target, walkSpeed * deltaTime);
            var moving = (guest.Position - previous).sqrMagnitude > 0.0001f;
            var facing = moving
                ? guest.Position.x - previous.x
                : guest.FacingTarget.x - guest.Position.x;
            if (Mathf.Abs(facing) > 0.01f)
            {
                var scale = guest.Rect.localScale;
                scale.x = Mathf.Abs(scale.x) * Mathf.Sign(facing);
                guest.Rect.localScale = scale;
            }

            var bob = moving
                ? Mathf.Abs(Mathf.Sin((time + guest.Phase) * 9f)) * 2f
                : Mathf.Sin(time * idleBobSpeed + guest.Phase) * idleBobHeight;
            guest.Rect.anchoredPosition = guest.Position + new Vector2(0f, bob);
            if (guest.Image != null)
            {
                var color = guest.Image.color;
                if (!Mathf.Approximately(color.a, guest.Alpha))
                {
                    color.a = guest.Alpha;
                    guest.Image.color = color;
                }
            }
        }

        private void SortGuestDepth()
        {
            if (guestLayer == null || guestLayer.childCount < 2)
                return;

            // Guests lower on the island stand in front of guests behind them.
            var ordered = new List<RectTransform>(guestLayer.childCount);
            foreach (RectTransform child in guestLayer)
                ordered.Add(child);
            ordered.Sort((left, right) => right.anchoredPosition.y.CompareTo(left.anchoredPosition.y));
            for (var index = 0; index < ordered.Count; index++)
            {
                if (ordered[index].GetSiblingIndex() != index)
                    ordered[index].SetSiblingIndex(index);
            }
        }

        private void ShowBubble(GuestBubbleRequest request)
        {
            var sprite = GetBubbleSprite(request.Kind);
            if (sprite == null || bubbleLayer == null || !views.TryGetValue(request.BuildingId, out var view))
                return;

            QueueGuest speaker = null;
            if (request.Anchor == GuestBubbleAnchor.FrontGuest && view.Line.Count > 0)
                speaker = view.Line[0];
            else if (request.Anchor == GuestBubbleAnchor.BackGuest && view.Line.Count > 0)
                speaker = view.Line[view.Line.Count - 1];

            Vector2 fixedPosition;
            if (speaker != null)
                fixedPosition = speaker.Position + new Vector2(0f, guestBubbleHeight);
            else if (view.Route.Count > 0)
                fixedPosition = view.Route[0] + new Vector2(0f, buildingBubbleHeight);
            else
                return; // No placed queue line means no known position for this building.

            var bubble = bubblePool.Count > 0 ? bubblePool.Pop() : CreateBubble();
            bubble.Kind = request.Kind;
            bubble.Speaker = speaker;
            bubble.FixedPosition = fixedPosition;
            bubble.Age = 0f;
            bubble.Image.sprite = sprite;
            var pivot = sprite.rect.size.x > 0f && sprite.rect.size.y > 0f
                ? new Vector2(sprite.pivot.x / sprite.rect.size.x, sprite.pivot.y / sprite.rect.size.y)
                : new Vector2(0.5f, 0f);
            bubble.Rect.pivot = pivot;
            bubble.Rect.sizeDelta = bubbleSize;
            bubble.Rect.localScale = Vector3.zero;
            bubble.Rect.SetAsLastSibling();
            bubble.Rect.gameObject.SetActive(true);
            bubbles.Add(bubble);
            PositionBubble(bubble);
        }

        private Bubble CreateBubble()
        {
            var bubbleObject = new GameObject("GuestBubble", typeof(RectTransform), typeof(Image));
            var bubble = new Bubble
            {
                Rect = (RectTransform)bubbleObject.transform,
                Image = bubbleObject.GetComponent<Image>()
            };
            bubble.Rect.SetParent(bubbleLayer, false);
            bubble.Rect.anchorMin = bubble.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            bubble.Image.raycastTarget = false;
            bubble.Image.preserveAspect = true;
            return bubble;
        }

        private Sprite GetBubbleSprite(GuestBubbleKind kind)
        {
            switch (kind)
            {
                case GuestBubbleKind.Happy: return happyBubble;
                case GuestBubbleKind.Waiting: return waitingBubble;
                case GuestBubbleKind.Unhappy: return unhappyBubble;
                case GuestBubbleKind.Crowded: return crowdedBubble;
                default: return null;
            }
        }

        private void AnimateBubbles(float deltaTime)
        {
            var lifetime = Mathf.Max(BubblePopSeconds + BubbleFadeSeconds, settings.BubbleLifetime);
            for (var index = bubbles.Count - 1; index >= 0; index--)
            {
                var bubble = bubbles[index];
                bubble.Age += deltaTime;
                if (bubble.Age >= lifetime)
                {
                    bubbles.RemoveAt(index);
                    bubble.Rect.gameObject.SetActive(false);
                    bubble.Speaker = null;
                    bubblePool.Push(bubble);
                    continue;
                }

                var pop = Mathf.Clamp01(bubble.Age / BubblePopSeconds);
                bubble.Rect.localScale = Vector3.one * EaseOutBack(pop);
                var fade = Mathf.Clamp01((lifetime - bubble.Age) / BubbleFadeSeconds);
                var color = bubble.Image.color;
                color.a = fade;
                bubble.Image.color = color;
                PositionBubble(bubble);
            }
        }

        private void PositionBubble(Bubble bubble)
        {
            if (bubble.Speaker != null)
                bubble.FixedPosition = bubble.Speaker.Position + new Vector2(0f, guestBubbleHeight);

            var rise = BubbleRiseDistance * Mathf.Clamp01(bubble.Age / Mathf.Max(0.01f, settings.BubbleLifetime));
            var local = bubble.FixedPosition + new Vector2(0f, rise);
            var world = guestLayer.TransformPoint(local);
            bubble.Rect.anchoredPosition = bubbleLayer.InverseTransformPoint(world);
        }

        private static float EaseOutBack(float t)
        {
            const float overshoot = 1.70158f;
            var shifted = t - 1f;
            return 1f + (overshoot + 1f) * shifted * shifted * shifted + overshoot * shifted * shifted;
        }

        private void HideAll()
        {
            showing = false;
            model = null;
            layoutsDirty = true;
            foreach (var view in views.Values)
            {
                foreach (var guest in view.Line)
                    ReleaseGuest(guest);
                view.Line.Clear();
            }

            foreach (var guest in leavingGuests)
                ReleaseGuest(guest);
            leavingGuests.Clear();

            foreach (var bubble in bubbles)
            {
                bubble.Rect.gameObject.SetActive(false);
                bubble.Speaker = null;
                bubblePool.Push(bubble);
            }

            bubbles.Clear();
        }

        private static bool IsWavePhase(GamePhase phase)
        {
            return phase == GamePhase.WAVE_ACTIVE
                || phase == GamePhase.WAVE_PAUSED
                || phase == GamePhase.WAVE_RESOLVING;
        }

        [Serializable]
        private struct QueueLine
        {
            public BuildingId Building;
            [Tooltip("Parent of the line's points, ordered from the front of the queue to the back.")]
            public RectTransform Points;
        }

        private sealed class QueueView
        {
            public QueueView(BuildingId buildingId)
            {
                BuildingId = buildingId;
            }

            public BuildingId BuildingId { get; }
            public readonly List<Vector2> Route = new List<Vector2>();
            public readonly List<Vector2> Slots = new List<Vector2>();
            public readonly List<Vector2> SpawnPoints = new List<Vector2>();
            public readonly List<QueueGuest> Line = new List<QueueGuest>();
        }

        private sealed class QueueGuest
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Position;
            public Vector2 Target;
            public Vector2 FacingTarget;
            public float Alpha;
            public float Phase;
            public bool Leaving;
            public float LeaveDelay;
        }

        private sealed class Bubble
        {
            public RectTransform Rect;
            public Image Image;
            public GuestBubbleKind Kind;
            public QueueGuest Speaker;
            public Vector2 FixedPosition;
            public float Age;

            /// <summary>Keeps the bubble where its guest last stood (FixedPosition tracks the speaker each frame).</summary>
            public void DetachSpeaker()
            {
                Speaker = null;
            }
        }

        /// <summary>Opaque outline of a building sprite (its tight mesh) in tourist-container space.</summary>
        private sealed class ArtShape
        {
            private Vector2[] vertices;
            private ushort[] triangles;
            private Rect bounds;

            public static ArtShape Create(Image image, RectTransform space)
            {
                var sprite = image.sprite;
                var rect = (RectTransform)image.transform;
                var drawRect = GetDrawRect(image, rect);
                var spriteVertices = sprite.vertices;
                var shape = new ArtShape
                {
                    vertices = new Vector2[spriteVertices.Length],
                    triangles = sprite.triangles
                };

                var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                var spriteSize = sprite.rect.size;
                for (var index = 0; index < spriteVertices.Length; index++)
                {
                    // Sprite vertices are in units around the pivot; map them onto the drawn rect.
                    var pixel = spriteVertices[index] * sprite.pixelsPerUnit + sprite.pivot;
                    var normalized = new Vector2(pixel.x / spriteSize.x, pixel.y / spriteSize.y);
                    var local = new Vector2(
                        drawRect.xMin + normalized.x * drawRect.width,
                        drawRect.yMin + normalized.y * drawRect.height);
                    Vector2 point = space.InverseTransformPoint(rect.TransformPoint(local));
                    shape.vertices[index] = point;
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }

                shape.bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                return shape;
            }

            public bool Contains(Vector2 point)
            {
                if (!bounds.Contains(point))
                    return false;

                for (var index = 0; index + 2 < triangles.Length; index += 3)
                {
                    if (InTriangle(point, vertices[triangles[index]], vertices[triangles[index + 1]], vertices[triangles[index + 2]]))
                        return true;
                }

                return false;
            }

            private static Rect GetDrawRect(Image image, RectTransform rect)
            {
                var drawRect = rect.rect;
                if (!image.preserveAspect || image.sprite.rect.height <= 0f || drawRect.height <= 0f)
                    return drawRect;

                var spriteRatio = image.sprite.rect.width / image.sprite.rect.height;
                var rectRatio = drawRect.width / drawRect.height;
                if (spriteRatio > rectRatio)
                {
                    var oldHeight = drawRect.height;
                    drawRect.height = drawRect.width / spriteRatio;
                    drawRect.y += (oldHeight - drawRect.height) * rect.pivot.y;
                }
                else
                {
                    var oldWidth = drawRect.width;
                    drawRect.width = drawRect.height * spriteRatio;
                    drawRect.x += (oldWidth - drawRect.width) * rect.pivot.x;
                }

                return drawRect;
            }

            private static bool InTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
            {
                var d1 = Cross(point, a, b);
                var d2 = Cross(point, b, c);
                var d3 = Cross(point, c, a);
                var hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
                var hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
                return !(hasNegative && hasPositive);
            }

            private static float Cross(Vector2 p, Vector2 a, Vector2 b)
            {
                return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            }
        }
    }
}
