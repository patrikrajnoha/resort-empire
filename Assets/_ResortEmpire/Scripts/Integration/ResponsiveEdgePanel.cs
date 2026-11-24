using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>
    /// Responsive edge panel interaction for S2. The panel is sized from its parent and
    /// safe area. Holding the tablet frame lets the player place it anywhere within that area.
    /// A small runtime-created handle remains reachable when the panel is closed.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class ResponsiveEdgePanel : MonoBehaviour
    {
        private enum DockEdge { Right, Left, Bottom }

        [Header("References")]
        [SerializeField] private RectTransform panel;
        [SerializeField] private RectTransform handle;

        [Header("Responsive sizing")]
        [SerializeField, Range(0.2f, 0.55f)] private float widthRatio = 0.32f;
        [SerializeField] private float minimumWidth = 420f;
        [SerializeField] private float maximumWidth = 620f;
        [SerializeField, Range(0.55f, 1f)] private float heightRatio = 0.78f;
        [SerializeField] private float minimumHeight = 460f;
        [SerializeField] private float maximumHeight = 860f;
        [SerializeField] private float panelAspect = 0.75f;
        [SerializeField] private float verticalOffset = -108f;
        [SerializeField] private float edgeHandleWidth = 96f;
        [SerializeField] private float edgeHandleHeight = 120f;
        [SerializeField] private float horizontalPadding = 16f;
        [SerializeField] private float verticalPadding = 16f;

        [Header("Gesture and animation")]
        [SerializeField] private float gestureThreshold = 22f;
        [SerializeField, Min(0.1f)] private float moveHoldDuration = 0.45f;
        [SerializeField, Range(0.1f, 0.8f)] private float snapDistance = 0.32f;
        [SerializeField] private float flickVelocity = 900f;
        [SerializeField] private float snapDuration = 0.24f;
        [SerializeField] private bool startClosed = true;
        [Tooltip("Use this for an accessibility setting when the project exposes reduced motion preferences.")]
        [SerializeField] private bool reducedMotion;
        [Header("Reliable touch swipe")]
        [SerializeField] private float swipeAxisRatio = 1.1f;
        [SerializeField] private float swipeCloseDistanceRatio = 0.16f;

        private RectTransform rootRect;
        private Canvas rootCanvas;
        private RectTransform display;
        private GameObject phoneFrame;
        private GestureTarget panelGesture;
        private GestureTarget handleGesture;
        private ContentGestureRelay contentGesture;
        private Button closeButton;
        private Vector2 dockedOpenPosition;
        private Vector2 openPosition;
        private DockEdge dockEdge;
        private Rect openPositionBounds;
        private Vector2? placedPosition;
        private Vector2 lastRootSize;
        private Rect lastSafeArea;
        private float lastScaleFactor = -1f;
        private float panelWidth;
        private bool layoutReady;
        private bool isOpen;
        private bool pointerDown;
        private bool dragging;
        private bool cancelled;
        private bool pointerStartedOnHandle;
        private bool pointerStartedOnInteractiveControl;
        private bool pressedLeftFrame;
        private bool pressedTopFrame;
        private int activePointerId;
        private Vector2 pointerStartPosition;
        private Vector2 lastPointerPosition;
        private float lastPointerTime;
        private float pointerVelocity;
        private float? moveReadyAt;
        private Coroutine snapCoroutine;
        private bool globalSwipeTracking;
        private Vector2 globalSwipeStart;
        private int globalSwipePointerId;
        private Quaternion restRotation;
        private float motionTilt;
        private float targetMotionTilt;
        private float alertElapsed = -1f;
        private float alertDuration;
        private float alertAngle;

        public bool IsOpen => isOpen;
        public event Action<bool> OpenStateChanged;

        private bool IsMovingPanel => pointerDown && moveReadyAt.HasValue && Time.unscaledTime >= moveReadyAt.Value;
        private Vector2 DockedPosition => dockEdge switch
        {
            DockEdge.Left => new Vector2(openPositionBounds.xMin, openPosition.y),
            DockEdge.Bottom => new Vector2(openPosition.x, openPositionBounds.yMin),
            _ => dockedOpenPosition
        };

        private Vector2 ClosedPosition => DockedPosition + GetDockDirection(dockEdge) * SlideTravel(dockEdge);

        private void Awake()
        {
            rootRect = transform as RectTransform;
            rootCanvas = GetComponentInParent<Canvas>();
            ResolveReferences();
            if (panel != null) restRotation = panel.localRotation;
            if (Application.isPlaying)
                EnsureGestureTargets();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (Application.isPlaying)
                EnsureGestureTargets();
            RefreshLayout(true);
            SetOpen(Application.isPlaying ? !startClosed : true, true);
        }

        private void OnDisable()
        {
            StopSnap();
            pointerDown = false;
            dragging = false;
            globalSwipeTracking = false;
            motionTilt = targetMotionTilt = 0f;
            alertElapsed = -1f;
            if (panel != null) panel.localRotation = restRotation;
        }

        private void LateUpdate()
        {
            RefreshLayout(false);
            if (!Application.isPlaying || panel == null) return;
            var dt = Time.unscaledDeltaTime;
            motionTilt = Mathf.Lerp(motionTilt, reducedMotion ? 0f : targetMotionTilt, 1f - Mathf.Exp(-18f * dt));
            targetMotionTilt = 0f;
            var alertTilt = 0f;
            if (alertElapsed >= 0f)
            {
                alertElapsed += dt;
                var t = Mathf.Clamp01(alertElapsed / alertDuration);
                if (!reducedMotion) alertTilt = Mathf.Sin(t * Mathf.PI * 6f) * alertAngle * (1f - t);
                if (t >= 1f) alertElapsed = -1f;
            }
            panel.localRotation = restRotation * Quaternion.Euler(0f, 0f, motionTilt + alertTilt);
        }

        public void PlayBottleneckWobble(float duration, float angle)
        {
            if (reducedMotion || !isActiveAndEnabled) return;
            alertElapsed = 0f;
            alertDuration = Mathf.Max(0.1f, duration);
            alertAngle = Mathf.Clamp(angle, 0f, 5f);
        }

        private void Update()
        {
            if (ResortEmpire.Gameplay.Integration.DebugModalState.BlocksInput) { globalSwipeTracking = false; return; }
            PollReliableCloseSwipe();

            if (!Application.isPlaying || !Input.GetKeyDown(KeyCode.Escape))
                return;

            HandleBackNavigation();
        }

        private void PollReliableCloseSwipe()
        {
            if (!Application.isPlaying || !isOpen || panel == null || IsMovingPanel)
            {
                globalSwipeTracking = false;
                return;
            }

            // Keep this outside the EventSystem drag chain. ScrollRect and card buttons can
            // claim a drag, but a clear horizontal swipe should still close the edge panel.
            if (Input.touchCount > 0)
            {
                for (var index = 0; index < Input.touchCount; index++)
                {
                    var touch = Input.GetTouch(index);
                    if (!globalSwipeTracking && touch.phase == TouchPhase.Began)
                    {
                        TryBeginReliableSwipe(touch.position, touch.fingerId);
                        continue;
                    }

                    if (globalSwipeTracking && touch.fingerId == globalSwipePointerId)
                    {
                        if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                            TryCompleteReliableSwipe(touch.position);
                        else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                            globalSwipeTracking = false;
                        return;
                    }
                }

                return;
            }

            if (!globalSwipeTracking && Input.GetMouseButtonDown(0))
                TryBeginReliableSwipe(Input.mousePosition, -1);

            if (globalSwipeTracking && Input.GetMouseButton(0))
                TryCompleteReliableSwipe(Input.mousePosition);

            if (globalSwipeTracking && Input.GetMouseButtonUp(0))
                globalSwipeTracking = false;
        }

        private void TryBeginReliableSwipe(Vector2 screenPosition, int pointerId)
        {
            var camera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? rootCanvas.worldCamera
                : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(panel, screenPosition, camera))
                return;

            globalSwipeTracking = true;
            globalSwipeStart = screenPosition;
            globalSwipePointerId = pointerId;
        }

        private void TryCompleteReliableSwipe(Vector2 screenPosition)
        {
            var delta = screenPosition - globalSwipeStart;
            var threshold = Mathf.Clamp(panel.rect.width * GetCanvasScale() * swipeCloseDistanceRatio, 56f, 140f);
            if (delta.x < threshold || delta.x <= Mathf.Abs(delta.y) * swipeAxisRatio)
                return;

            globalSwipeTracking = false;
            SetOpen(false, false);
        }

        /// <summary>
        /// Handles Android/system Back or Escape with the same animated close used by a rightward
        /// swipe on the open panel. When already closed, the game remains responsible for Back.
        /// </summary>
        public bool HandleBackNavigation()
        {
            if (!isOpen)
                return false;

            SetOpen(false, false);
            return true;
        }

        public void Toggle()
        {
            SetOpen(!isOpen, false);
        }

        public void OpenImmediate()
        {
            SetOpen(true, true);
        }

        public void CloseImmediate()
        {
            SetOpen(false, true);
        }

        public void SetOpen(bool open, bool immediate)
            => SetOpen(open, immediate, open ? dockEdge : DockEdge.Right);

        private void SetOpen(bool open, bool immediate, DockEdge edge)
        {
            if (!layoutReady)
                RefreshLayout(true);

            isOpen = open;
            StopSnap();
            dockEdge = edge;
            LayoutHandle();
            pointerDown = false;
            moveReadyAt = null;
            globalSwipeTracking = false;

            var target = open ? openPosition : ClosedPosition;
            if (immediate || reducedMotion || !isActiveAndEnabled)
            {
                ApplyPosition(target);
                UpdateHandleVisibility();
                OpenStateChanged?.Invoke(isOpen);
                return;
            }

            snapCoroutine = StartCoroutine(SnapTo(target, open));
        }

        private System.Collections.IEnumerator SnapTo(Vector2 target, bool open)
        {
            var start = panel.anchoredPosition;
            var elapsed = 0f;
            var duration = Mathf.Max(0.01f, snapDuration) * (open ? 1.35f : 1.1f);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                // Ease out gently, so the tablet responds immediately and settles softly.
                t = 1f - Mathf.Pow(1f - t, 4f);
                ApplyPosition(Vector2.LerpUnclamped(start, target, t));
                yield return null;
            }

            ApplyPosition(target);
            isOpen = open;
            snapCoroutine = null;
            UpdateHandleVisibility();
            OpenStateChanged?.Invoke(isOpen);
        }

        private void ApplyPosition(Vector2 position)
        {
            if (panel != null)
            {
                if (Application.isPlaying && !reducedMotion)
                {
                    var velocity = (position.x - panel.anchoredPosition.x) / Mathf.Max(0.001f, Time.unscaledDeltaTime);
                    targetMotionTilt = Mathf.Clamp(-velocity * 0.0015f, -1.5f, 1.5f);
                }
                panel.anchoredPosition = position;
            }
        }

        private void StopSnap()
        {
            if (snapCoroutine == null) return;
            StopCoroutine(snapCoroutine);
            snapCoroutine = null;
        }

        private void ResolveReferences()
        {
            rootRect ??= transform as RectTransform;
            rootCanvas ??= GetComponentInParent<Canvas>();
            if (panel == null)
                panel = transform.Find("Panel") as RectTransform;
            if (panel != null)
            {
                display ??= panel.Find("Display") as RectTransform;
                phoneFrame ??= panel.Find("PhoneFrame")?.gameObject;
            }
        }

        private void EnsureGestureTargets()
        {
            if (panel != null)
            {
                panelGesture = panel.GetComponent<GestureTarget>();
                if (panelGesture == null) panelGesture = panel.gameObject.AddComponent<GestureTarget>();
                panelGesture.Configure(this, false);
                EnsureCloseButton();
            }

            if (handle == null)
            {
                var existing = transform.Find("EdgeHandle");
                handle = existing as RectTransform;
            }

            if (handle == null)
            {
                var handleObject = new GameObject("EdgeHandle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                handleObject.layer = gameObject.layer;
                handle = handleObject.transform as RectTransform;
                handle.SetParent(transform, false);

                var image = handleObject.GetComponent<Image>();
                // Keep the edge hit area for swipe/tap interaction, but do not draw
                // the old dark pill over the tablet UI.
                image.color = new Color(0.06f, 0.09f, 0.12f, 0f);
                image.raycastTarget = true;

                var gripObject = new GameObject("Grip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                gripObject.layer = gameObject.layer;
                var grip = gripObject.transform as RectTransform;
                grip.SetParent(handle, false);
                grip.anchorMin = new Vector2(0.5f, 0.5f);
                grip.anchorMax = new Vector2(0.5f, 0.5f);
                grip.pivot = new Vector2(0.5f, 0.5f);
                grip.sizeDelta = new Vector2(5f, 54f);
                var gripImage = gripObject.GetComponent<Image>();
                gripImage.color = new Color(1f, 1f, 1f, 0f);
                gripImage.raycastTarget = false;
            }

            handleGesture = handle.GetComponent<GestureTarget>();
            if (handleGesture == null) handleGesture = handle.gameObject.AddComponent<GestureTarget>();
            handleGesture.Configure(this, true);

            var scrollRect = panel.GetComponentInChildren<ScrollRect>(true);
            if (scrollRect != null && scrollRect.content != null)
            {
                contentGesture = scrollRect.content.GetComponent<ContentGestureRelay>();
                if (contentGesture == null)
                    contentGesture = scrollRect.content.gameObject.AddComponent<ContentGestureRelay>();
                contentGesture.Configure(this, scrollRect);
            }
        }

        private void UpdateHandleVisibility()
        {
            if (handle != null)
                handle.gameObject.SetActive(!isOpen);
        }

        private void RefreshLayout(bool force)
        {
            ResolveReferences();
            if (rootRect == null || panel == null) return;

            var safeArea = Screen.safeArea;
            if (safeArea.width <= 0f || safeArea.height <= 0f)
                safeArea = new Rect(0f, 0f, Screen.width, Screen.height);

            var scaleFactor = rootCanvas != null ? rootCanvas.scaleFactor : 1f;
            var rootSize = rootRect.rect.size;
            if (!force && layoutReady && rootSize == lastRootSize && safeArea == lastSafeArea
                && Mathf.Approximately(scaleFactor, lastScaleFactor))
                return;

            lastRootSize = rootSize;
            lastSafeArea = safeArea;
            lastScaleFactor = scaleFactor;

            var camera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? rootCanvas.worldCamera
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, safeArea.min, camera, out var safeMin)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, safeArea.max, camera, out var safeMax))
            {
                safeMin = rootRect.rect.min;
                safeMax = rootRect.rect.max;
            }

            safeMin = Vector2.Max(safeMin, rootRect.rect.min);
            safeMax = Vector2.Min(safeMax, rootRect.rect.max);
            var safeSize = safeMax - safeMin;
            if (safeSize.x <= 0f || safeSize.y <= 0f) return;

            var padding = Vector2.Min(new Vector2(horizontalPadding, verticalPadding), safeSize * 0.5f);
            var availableWidth = Mathf.Max(1f, safeSize.x - padding.x * 2f);
            var availableHeight = Mathf.Max(1f, safeSize.y - padding.y * 2f);
            var preferredWidthRatio = availableWidth < availableHeight ? Mathf.Max(widthRatio, 0.64f) : widthRatio;
            var preferredWidth = Mathf.Clamp(rootSize.x * preferredWidthRatio, minimumWidth, maximumWidth);
            var preferredHeight = Mathf.Min(availableHeight,
                Mathf.Clamp(availableHeight * heightRatio, minimumHeight, maximumHeight));
            var aspect = Mathf.Max(0.01f, panelAspect);
            panelWidth = Mathf.Min(Mathf.Min(preferredWidth, availableWidth), preferredHeight * aspect);
            var panelHeight = panelWidth / aspect;

            panel.anchorMin = new Vector2(1f, 0.5f);
            panel.anchorMax = new Vector2(1f, 0.5f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.sizeDelta = new Vector2(panelWidth, panelHeight);

            var anchor = new Vector2(rootRect.rect.xMax, rootRect.rect.center.y);
            var min = safeMin + padding - anchor + new Vector2(panelWidth, panelHeight * 0.5f);
            var max = safeMax - padding - anchor - new Vector2(0f, panelHeight * 0.5f);
            max = Vector2.Max(max, min);
            openPositionBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            var centerY = (safeMin.y + safeMax.y) * 0.5f - anchor.y;
            dockedOpenPosition = ClampOpenPosition(new Vector2(max.x, centerY + verticalOffset));
            openPosition = ClampOpenPosition(placedPosition ?? dockedOpenPosition);

            layoutReady = true;
            ApplyPosition(isOpen ? openPosition : ClosedPosition);
            LayoutHandle();
            LayoutCloseButton();
            UpdateHandleVisibility();
        }

        private void LayoutHandle()
        {
            if (!handle || !panel) return;

            handle.anchorMin = handle.anchorMax = handle.pivot = new Vector2(1f, 0.5f);
            switch (dockEdge)
            {
                case DockEdge.Left:
                    handle.sizeDelta = new Vector2(Mathf.Min(edgeHandleWidth, panelWidth), panel.rect.height);
                    handle.anchoredPosition = ClosedPosition;
                    break;
                case DockEdge.Bottom:
                    handle.sizeDelta = new Vector2(panelWidth, Mathf.Min(edgeHandleWidth, panel.rect.height));
                    handle.anchoredPosition = ClosedPosition + Vector2.up * (SlideTravel(dockEdge) * 0.5f);
                    break;
                default:
                    handle.sizeDelta = new Vector2(Mathf.Min(edgeHandleWidth, panelWidth), panel.rect.height);
                    handle.anchoredPosition = dockedOpenPosition;
                    break;
            }
        }

        private static Vector2 GetDockDirection(DockEdge edge) => edge switch
        {
            DockEdge.Left => Vector2.left,
            DockEdge.Bottom => Vector2.down,
            _ => Vector2.right
        };

        private float SlideTravel(DockEdge edge) => Mathf.Max(0f,
            (edge == DockEdge.Bottom ? panel.rect.height : panelWidth) - edgeHandleWidth);

        private void EnsureCloseButton()
        {
            if (panel == null) return;

            var existing = panel.Find("CloseButton");
            if (existing != null)
                existing.gameObject.SetActive(false);

            // The tablet uses its frame and swipe gestures for docking.
            closeButton = null;
        }

        private void LayoutCloseButton()
        {
            if (closeButton == null) return;
            var rect = closeButton.transform as RectTransform;
            if (rect == null) return;

            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(56f, 56f);
            rect.anchoredPosition = new Vector2(-16f, panelWidth <= 500f ? -16f : -94f);
        }

        private void CloseFromButton()
        {
            SetOpen(false, false);
        }

        private void BeginPointer(PointerEventData eventData, bool fromHandle)
        {
            if (!isActiveAndEnabled || eventData.button != PointerEventData.InputButton.Left)
                return;
            if (!fromHandle && !isOpen)
                return;
            if (pointerDown && eventData.pointerId != activePointerId)
                return;

            StopSnap();
            pointerDown = true;
            dragging = false;
            cancelled = false;
            pointerStartedOnHandle = fromHandle;
            pointerStartedOnInteractiveControl = !fromHandle
                && IsInteractiveControl(eventData.pointerPressRaycast.gameObject);
            moveReadyAt = !fromHandle && !pointerStartedOnInteractiveControl
                ? Time.unscaledTime + moveHoldDuration
                : (float?)null;
            RecordFramePress(eventData);
            activePointerId = eventData.pointerId;
            pointerStartPosition = eventData.position;
            lastPointerPosition = eventData.position;
            lastPointerTime = Time.unscaledTime;
            pointerVelocity = 0f;
        }

        private void RecordFramePress(PointerEventData eventData)
        {
            pressedLeftFrame = pressedTopFrame = false;
            if (!moveReadyAt.HasValue || !display || !phoneFrame || eventData.pointerPressRaycast.gameObject != phoneFrame
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, eventData.position, eventData.pressEventCamera, out var point)
                || !panel.rect.Contains(point))
                return;

            pressedLeftFrame = point.x <= Mathf.Lerp(panel.rect.xMin, panel.rect.xMax, display.anchorMin.x);
            pressedTopFrame = point.y >= Mathf.Lerp(panel.rect.yMin, panel.rect.yMax, display.anchorMax.y);
        }

        private void DragPointer(PointerEventData eventData, bool fromHandle)
        {
            if (!pointerDown || cancelled || eventData.pointerId != activePointerId || fromHandle != pointerStartedOnHandle)
                return;

            if (IsMovingPanel)
            {
                MovePanel(eventData);
                return;
            }

            var totalDelta = eventData.position - pointerStartPosition;
            if (totalDelta.sqrMagnitude > gestureThreshold * gestureThreshold * 0.25f)
                moveReadyAt = null;

            var edge = fromHandle ? dockEdge : DockEdge.Right;
            var direction = GetDockDirection(edge);
            if (!dragging)
            {
                var along = Vector2.Dot(totalDelta, direction);
                var across = Mathf.Abs(Vector2.Dot(totalDelta, Vector2.Perpendicular(direction)));
                if (across > Mathf.Abs(along) * 1.15f && across > gestureThreshold * 0.5f)
                {
                    cancelled = true;
                    return;
                }
                if (Mathf.Abs(along) < gestureThreshold)
                    return;

                if (fromHandle ? along >= 0f : along <= 0f)
                {
                    cancelled = true;
                    return;
                }

                dragging = true;
                // Once the gesture is clearly horizontal, the edge panel owns it even
                // when it started on a button/card inside the ScrollRect. A vertical
                // gesture was already cancelled above, so the ScrollRect keeps scrolling.
                if (pointerStartedOnInteractiveControl)
                    eventData.Use();
            }

            var delta = Vector2.Dot(eventData.position - lastPointerPosition, direction);
            var slideStart = fromHandle ? DockedPosition : openPosition;
            var offset = Vector2.Dot(panel.anchoredPosition - slideStart, direction);
            ApplyPosition(slideStart + direction * Mathf.Clamp(offset + delta / GetCanvasScale(), 0f, SlideTravel(edge)));

            var now = Time.unscaledTime;
            var deltaTime = now - lastPointerTime;
            if (deltaTime > 0.0001f)
                pointerVelocity = delta / deltaTime;
            lastPointerPosition = eventData.position;
            lastPointerTime = now;
        }

        private void EndPointer(PointerEventData eventData, bool fromHandle)
        {
            if (!pointerDown || eventData.pointerId != activePointerId || fromHandle != pointerStartedOnHandle)
                return;

            var wasMoving = IsMovingPanel;
            var displacement = eventData.position - pointerStartPosition;
            var tappedLeft = !wasMoving && moveReadyAt.HasValue && pressedLeftFrame && !eventData.dragging
                && displacement.sqrMagnitude < gestureThreshold * gestureThreshold * 0.25f
                && openPosition.x < openPositionBounds.center.x;
            pointerDown = false;
            moveReadyAt = null;
            if (wasMoving)
            {
                MovePanel(eventData);
                var bottomThreshold = Mathf.Clamp(panel.rect.height * GetCanvasScale() * swipeCloseDistanceRatio, 56f, 140f);
                if (pressedTopFrame && -displacement.y >= bottomThreshold && -displacement.y > Mathf.Abs(displacement.x) * swipeAxisRatio)
                    SetOpen(false, false, DockEdge.Bottom);
                return;
            }
            if (cancelled)
                return;

            if (!dragging)
            {
                if (fromHandle) Toggle();
                else if (tappedLeft) SetOpen(false, false, DockEdge.Left);
                return;
            }

            var edge = fromHandle ? dockEdge : DockEdge.Right;
            var slideStart = fromHandle ? DockedPosition : openPosition;
            var offset = Vector2.Dot(panel.anchoredPosition - slideStart, GetDockDirection(edge));
            var progress = Mathf.InverseLerp(0f, SlideTravel(edge), offset);
            var quickClose = !fromHandle && pointerVelocity >= flickVelocity;
            var quickOpen = fromHandle && pointerVelocity <= -flickVelocity;
            var shouldClose = progress >= snapDistance || quickClose;
            var shouldOpen = progress <= 1f - snapDistance || quickOpen;

            if (fromHandle)
                SetOpen(shouldOpen, false, dockEdge);
            else
                SetOpen(!shouldClose, false);
        }

        private void PrepareRelayedPointer(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || !isOpen || eventData.button != PointerEventData.InputButton.Left)
                return;
            if (pointerDown && eventData.pointerId != activePointerId)
                return;

            StopSnap();
            pointerDown = false;
            moveReadyAt = null;
            dragging = false;
            cancelled = false;
            pointerStartedOnHandle = false;
            pointerStartedOnInteractiveControl = true;
            activePointerId = eventData.pointerId;
            pointerStartPosition = eventData.pressPosition;
            lastPointerPosition = eventData.position;
            lastPointerTime = Time.unscaledTime;
            pointerVelocity = 0f;
        }

        private bool BeginRelayedDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != activePointerId || !isOpen)
                return false;

            var totalDelta = eventData.position - pointerStartPosition;
            var vertical = Mathf.Abs(totalDelta.y) > Mathf.Abs(totalDelta.x) * 1.15f
                && Mathf.Abs(totalDelta.y) > gestureThreshold * 0.5f;
            if (vertical)
            {
                CancelRelayedPointer();
                return true;
            }

            pointerDown = true;
            DragPointer(eventData, false);
            return false;
        }

        private void CancelRelayedPointer()
        {
            pointerDown = false;
            dragging = false;
            cancelled = true;
        }

        private void MovePanel(PointerEventData eventData)
        {
            var camera = eventData.pressEventCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, lastPointerPosition, camera, out var previous)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, eventData.position, camera, out var current))
            {
                openPosition = ClampOpenPosition(panel.anchoredPosition + current - previous);
                placedPosition = openPosition;
                ApplyPosition(openPosition);
            }
            lastPointerPosition = eventData.position;
        }

        private Vector2 ClampOpenPosition(Vector2 position) => new Vector2(
            Mathf.Clamp(position.x, openPositionBounds.xMin, openPositionBounds.xMax),
            Mathf.Clamp(position.y, openPositionBounds.yMin, openPositionBounds.yMax));

        private bool IsInteractiveControl(GameObject target)
        {
            if (target == null) return false;
            if (target.GetComponentInParent<Selectable>() != null) return true;
            if (target.GetComponentInParent<ScrollRect>() != null) return true;
            return false;
        }

        private float GetCanvasScale()
        {
            return rootCanvas != null ? Mathf.Max(0.01f, rootCanvas.scaleFactor) : 1f;
        }

        private sealed class GestureTarget : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, ICancelHandler
        {
            private ResponsiveEdgePanel owner;
            private bool fromHandle;

            public void Configure(ResponsiveEdgePanel panelOwner, bool handleTarget)
            {
                owner = panelOwner;
                fromHandle = handleTarget;
            }

            public void OnPointerDown(PointerEventData eventData) => owner?.BeginPointer(eventData, fromHandle);
            public void OnDrag(PointerEventData eventData) => owner?.DragPointer(eventData, fromHandle);
            public void OnPointerUp(PointerEventData eventData) => owner?.EndPointer(eventData, fromHandle);
            public void OnCancel(BaseEventData eventData)
            {
                if (owner == null) return;
                owner.pointerDown = false;
                owner.cancelled = true;
            }
        }

        /// <summary>
        /// Receives drag ownership from content without intercepting Button pointer-down/up events.
        /// Vertical movement is returned to ScrollRect; horizontal movement belongs to the panel.
        /// </summary>
        private sealed class ContentGestureRelay : MonoBehaviour, IInitializePotentialDragHandler,
            IBeginDragHandler, IDragHandler, IEndDragHandler, ICancelHandler
        {
            private ResponsiveEdgePanel owner;
            private ScrollRect scrollRect;
            private bool scrolling;

            public void Configure(ResponsiveEdgePanel panelOwner, ScrollRect targetScrollRect)
            {
                owner = panelOwner;
                scrollRect = targetScrollRect;
            }

            public void OnInitializePotentialDrag(PointerEventData eventData)
            {
                owner?.PrepareRelayedPointer(eventData);
                scrollRect?.OnInitializePotentialDrag(eventData);
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                scrolling = owner == null || owner.BeginRelayedDrag(eventData);
                if (!scrolling || scrollRect == null) return;
                scrollRect.OnBeginDrag(eventData);
                scrollRect.OnDrag(eventData);
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (scrolling) scrollRect?.OnDrag(eventData);
                else owner?.DragPointer(eventData, false);
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (scrolling)
                {
                    scrollRect?.OnEndDrag(eventData);
                    scrolling = false;
                    return;
                }

                owner?.EndPointer(eventData, false);
            }

            public void OnCancel(BaseEventData eventData)
            {
                scrolling = false;
                owner?.CancelRelayedPointer();
                if (scrollRect != null) scrollRect.StopMovement();
            }
        }
    }
}
