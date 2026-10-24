using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace ResortEmpire
{
    /// <summary>
    /// Touch-first camera navigation for the resort island.
    /// The island sprites stay fixed in world space; only the camera moves and zooms.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ResortTouchCameraController : MonoBehaviour
    {
        [Header("World bounds")]
        [SerializeField] private RectTransform boundsSource;
        [SerializeField] private bool calculateBoundsFromGraphics = true;
        [SerializeField] private Bounds fallbackWorldBounds = new Bounds(Vector3.zero, new Vector3(14.48f, 10.86f, 0.1f));

        [Header("Orthographic zoom")]
        [SerializeField, Min(0.01f)] private float minOrthographicSize = 2.5f;
        [SerializeField, Min(0.01f)] private float maxOrthographicSize = 6.5f;
        [SerializeField, Min(0.001f)] private float zoomSensitivity = 1.35f;
        [SerializeField, Min(0f)] private float zoomSmoothTime = 0.18f;

        [Header("Pan")]
        [SerializeField, Min(0f)] private float panSensitivity = 1.45f;
        [SerializeField, Min(0f)] private float panSmoothTime = 0.14f;
        [SerializeField, Min(0f)] private float panInertiaDamping = 8f;
        [SerializeField, Min(0f)] private float maxPanInertiaSpeed = 10f;
        [SerializeField, Min(0.001f)] private float panVelocitySmoothingTime = 0.05f;
        [SerializeField, Min(0f)] private float touchDragDeadzonePixels = 6f;
        [SerializeField, Min(0f)] private float pinchZoomDeadzonePixels = 3f;

        [Header("Edge resistance")]
        [SerializeField, Min(0f)] private float edgeRubberBandDistance = 0.4f;
        [SerializeField, Range(0.01f, 1f)] private float edgeRubberBandStrength = 0.35f;
        [SerializeField, Min(0.01f)] private float edgeReturnSmoothTime = 0.12f;
        [SerializeField, Min(0f)] private float viewportSafetyPixels = 4f;

        [Header("Focus gestures")]
        [SerializeField, Range(0.1f, 0.95f)] private float doubleTapZoomScale = 0.65f;
        [SerializeField, Min(0.05f)] private float doubleTapMaxDelay = 0.3f;
        [SerializeField, Min(1f)] private float doubleTapMaxDistancePixels = 48f;

        [Header("Perspective fallback")]
        [SerializeField, Min(0.01f)] private float minPerspectiveDistance = 5f;
        [SerializeField, Min(0.01f)] private float maxPerspectiveDistance = 30f;

        [Header("Editor fallback")]
        [SerializeField] private bool enableMouseFallback = true;
        [SerializeField, Min(0.001f)] private float mouseScrollSensitivity = 1.25f;

        private readonly List<RaycastResult> raycastResults = new List<RaycastResult>(8);
        private Camera targetCamera;
        private Bounds worldBounds;
        private bool hasWorldBounds;
        private Transform worldCanvasTransform;
        private float configuredMaxOrthographicSize;
        private Vector3 targetCameraPosition;
        private Vector3 cameraPositionVelocity;
        private Vector3 edgeReturnVelocity;
        private Vector3 panVelocity;
        private float targetZoomValue;
        private float zoomValueVelocity;
        private bool gestureBlocked;
        private bool panInputThisFrame;
        private int previousTouchCount;
        private bool singleTouchDragging;
        private Vector2 singleTouchStartPosition;
        private Vector2 previousSingleTouchPosition;
        private Vector2 previousPinchCenter;
        private float previousPinchDistance;
        private bool mouseDragging;
        private bool mouseDragStarted;
        private bool mouseTapEligible;
        private Vector2 mouseDragStartPosition;
        private Vector2 previousMousePosition;
        private float lastTapTime = -1f;
        private Vector2 lastTapPosition;
        private float zoomBeforeDoubleTap = -1f;
        private float lastCameraAspect = -1f;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            if (targetCamera == null || boundsSource == null)
            {
                enabled = false;
                Debug.LogError("ResortTouchCameraController needs a Camera and a world-space bounds source.", this);
                return;
            }

            configuredMaxOrthographicSize = maxOrthographicSize;
            Canvas worldCanvas = boundsSource.GetComponentInParent<Canvas>();
            worldCanvasTransform = worldCanvas != null ? worldCanvas.transform : boundsSource.transform;

            targetCameraPosition = targetCamera.transform.position;
            targetZoomValue = targetCamera.orthographic
                ? Mathf.Clamp(targetCamera.orthographicSize, minOrthographicSize, maxOrthographicSize)
                : Mathf.Clamp(GetPerspectiveDistance(), minPerspectiveDistance, maxPerspectiveDistance);

            RefreshWorldBounds();
            if (targetCamera.orthographic)
            {
                // Landscape editor/Game views can be narrower than the phone
                // stream. Fit the complete background to the current aspect
                // before applying the camera bounds, otherwise its sides are
                // cropped on startup.
                UpdateOrthographicZoomLimit();
                targetCameraPosition = new Vector3(
                    worldBounds.center.x,
                    worldBounds.center.y,
                    targetCameraPosition.z);
                lastCameraAspect = targetCamera.aspect;
            }

            targetCameraPosition = ClampCameraPosition(targetCameraPosition, targetZoomValue);
            targetCamera.transform.position = targetCameraPosition;
            if (targetCamera.orthographic)
                targetCamera.orthographicSize = targetZoomValue;
        }

        private void OnEnable()
        {
            ResetGestureState();
            mouseDragging = false;
            mouseDragStarted = false;
            mouseTapEligible = false;
            panVelocity = Vector3.zero;
            cameraPositionVelocity = Vector3.zero;
            edgeReturnVelocity = Vector3.zero;
            zoomValueVelocity = 0f;
        }

        private void Update()
        {
            if (targetCamera == null || boundsSource == null)
                return;

            panInputThisFrame = false;

            if (!hasWorldBounds)
                RefreshWorldBounds();

            if (targetCamera.orthographic && !Mathf.Approximately(lastCameraAspect, targetCamera.aspect))
            {
                // Re-fit only when the viewport aspect changes. This keeps the
                // full background visible after a Game View/remote resize,
                // while preserving normal manual zooming during gameplay.
                UpdateOrthographicZoomLimit();
                lastCameraAspect = targetCamera.aspect;
            }

            bool touchWasHandled = UpdateTouchInput();
            if (!touchWasHandled && enableMouseFallback)
                UpdateMouseInput();

            ApplyPanInertia();
            if (!panInputThisFrame)
                ApplyEdgeReturn();

            targetCamera.transform.position = Vector3.SmoothDamp(
                targetCamera.transform.position,
                targetCameraPosition,
                ref cameraPositionVelocity,
                panSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            if (targetCamera.orthographic)
            {
                targetCamera.orthographicSize = Mathf.SmoothDamp(
                    targetCamera.orthographicSize,
                    targetZoomValue,
                    ref zoomValueVelocity,
                    zoomSmoothTime,
                    Mathf.Infinity,
                    Time.unscaledDeltaTime);

                // During a smoothed zoom the target position can reach an edge
                // before the rendered camera reaches the new zoom value. Keep
                // the actually rendered camera inside the background as well,
                // otherwise a short black strip can appear at the edge.
                bool targetIsOutsideBounds = IsOutsideCameraBounds(
                    targetCameraPosition,
                    targetCamera.orthographicSize);
                if (!targetIsOutsideBounds)
                {
                    Vector3 safeCameraPosition = ClampCameraPosition(
                        targetCamera.transform.position,
                        targetCamera.orthographicSize);
                    if ((safeCameraPosition - targetCamera.transform.position).sqrMagnitude <= 0.000001f)
                        return;

                    targetCamera.transform.position = safeCameraPosition;
                    targetCameraPosition = safeCameraPosition;
                    cameraPositionVelocity = Vector3.zero;
                    panVelocity = Vector3.zero;
                }
            }
            else
            {
                ApplyPerspectiveZoom();
            }
        }

        public void RefreshWorldBounds()
        {
            if (!calculateBoundsFromGraphics || boundsSource == null)
            {
                worldBounds = fallbackWorldBounds;
                hasWorldBounds = worldBounds.size.x > 0f && worldBounds.size.y > 0f;
                return;
            }

            Canvas.ForceUpdateCanvases();
            var graphics = boundsSource.GetComponentsInChildren<Graphic>(true);
            bool hasBounds = false;
            var calculatedBounds = new Bounds();

            foreach (var graphic in graphics)
            {
                if (graphic == null || !graphic.gameObject.activeInHierarchy)
                    continue;

                var corners = new Vector3[4];
                graphic.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    if (!hasBounds)
                    {
                        calculatedBounds = new Bounds(corner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        calculatedBounds.Encapsulate(corner);
                    }
                }
            }

            worldBounds = hasBounds && calculatedBounds.size.x > 0f && calculatedBounds.size.y > 0f
                ? calculatedBounds
                : fallbackWorldBounds;
            hasWorldBounds = worldBounds.size.x > 0f && worldBounds.size.y > 0f;
        }

        private float GetMinimumOrthographicSizeToFitBounds()
        {
            if (!hasWorldBounds || targetCamera == null)
                return minOrthographicSize;

            float safeAspect = Mathf.Max(targetCamera.aspect, 0.01f);
            float requiredHalfHeight = worldBounds.extents.y;
            float requiredHalfWidth = worldBounds.extents.x / safeAspect;
            float fitZoom = Mathf.Max(minOrthographicSize, requiredHalfHeight, requiredHalfWidth);

            // Leave a small coverage margin so raster rounding, aspect changes
            // and edge panning can never expose the camera's black clear color.
            float viewportHeight = Mathf.Max(targetCamera.pixelHeight, 1f);
            float coverageScale = 1f + (2f * viewportSafetyPixels / viewportHeight);
            return fitZoom / coverageScale;
        }

        private void UpdateOrthographicZoomLimit()
        {
            if (!targetCamera.orthographic || !hasWorldBounds)
                return;

            float fitZoom = GetMinimumOrthographicSizeToFitBounds();

            // A larger orthographic size zooms farther out. Never allow the
            // camera to zoom beyond the size where the complete background
            // still fills the viewport; that would reveal black outside the
            // finite island bounds.
            maxOrthographicSize = Mathf.Max(
                minOrthographicSize,
                Mathf.Min(configuredMaxOrthographicSize, fitZoom));
            targetZoomValue = Mathf.Clamp(
                targetZoomValue,
                minOrthographicSize,
                maxOrthographicSize);
        }

        private bool UpdateTouchInput()
        {
            int touchCount = 0;
            Vector2 firstPosition = Vector2.zero;
            Vector2 secondPosition = Vector2.zero;
            int firstPointerId = -1;
            int secondPointerId = -1;

            // Unity Remote can expose phone touches through the Input System
            // or through the legacy remote touch API, depending on the editor
            // and Remote app version. Read both so mobile gestures are stable.
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                for (int i = 0; i < touchscreen.touches.Count; i++)
                {
                    var touch = touchscreen.touches[i];
                    if (!touch.press.isPressed)
                        continue;

                    if (touchCount == 0)
                    {
                        firstPosition = touch.position.ReadValue();
                        firstPointerId = touch.touchId.ReadValue();
                    }
                    else if (touchCount == 1)
                    {
                        secondPosition = touch.position.ReadValue();
                        secondPointerId = touch.touchId.ReadValue();
                    }

                    touchCount++;
                }
            }

            // Unity Remote 5 may populate the legacy touch API even when the
            // new Input System has a Touchscreen device but no active touches.
            if (touchCount == 0 && Input.touchCount > 0)
            {
                int legacyTouchCount = Mathf.Min(Input.touchCount, 2);
                for (int i = 0; i < legacyTouchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (i == 0)
                    {
                        firstPosition = touch.position;
                        firstPointerId = touch.fingerId;
                    }
                    else
                    {
                        secondPosition = touch.position;
                        secondPointerId = touch.fingerId;
                    }
                }

                touchCount = legacyTouchCount;
            }

            if (touchCount == 0)
            {
                if (previousTouchCount == 1 && !gestureBlocked && !singleTouchDragging)
                    RegisterTap(previousSingleTouchPosition);

                ResetGestureState();
                return false;
            }

            if (touchCount != previousTouchCount)
            {
                gestureBlocked = IsPointerBlocked(firstPosition, firstPointerId)
                    || (touchCount > 1 && IsPointerBlocked(secondPosition, secondPointerId));
                previousTouchCount = touchCount;

                if (!gestureBlocked)
                {
                    if (touchCount == 1)
                    {
                        singleTouchStartPosition = firstPosition;
                        singleTouchDragging = false;
                        previousSingleTouchPosition = firstPosition;
                    }
                    else
                    {
                        previousPinchCenter = GetTouchCenter(firstPosition, secondPosition);
                        previousPinchDistance = GetTouchDistance(firstPosition, secondPosition);
                    }

                    panVelocity = Vector3.zero;
                }

                return true;
            }

            if (gestureBlocked)
                return true;

            if (touchCount == 1)
            {
                Vector2 position = firstPosition;
                if (!singleTouchDragging)
                {
                    if (Vector2.Distance(position, singleTouchStartPosition) < touchDragDeadzonePixels)
                    {
                        previousSingleTouchPosition = position;
                        return true;
                    }

                    singleTouchDragging = true;
                }

                Vector2 delta = position - previousSingleTouchPosition;
                previousSingleTouchPosition = position;
                PanByScreenDelta(delta);
            }
            else
            {
                Vector2 center = GetTouchCenter(firstPosition, secondPosition);
                float distance = GetTouchDistance(firstPosition, secondPosition);
                float previousDistance = Mathf.Max(previousPinchDistance, 1f);

                if (Mathf.Abs(distance - previousDistance) >= pinchZoomDeadzonePixels)
                    ZoomAroundScreenPoint(distance / previousDistance, center);
                PanByScreenDelta(center - previousPinchCenter);

                previousPinchCenter = center;
                previousPinchDistance = distance;
            }

            return true;
        }

        private void UpdateMouseInput()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            Vector2 position = mouse.position.ReadValue();
            bool leftPressed = mouse.leftButton.isPressed;
            bool middlePressed = mouse.middleButton.isPressed;
            bool pressed = leftPressed || middlePressed;

            if (pressed && !mouseDragging)
            {
                mouseDragging = !IsPointerBlocked(position, -1);
                mouseDragStarted = false;
                mouseTapEligible = leftPressed;
                mouseDragStartPosition = position;
                previousMousePosition = position;
                panVelocity = Vector3.zero;
            }
            else if (!pressed)
            {
                if (mouseDragging && mouseTapEligible && !mouseDragStarted)
                    RegisterTap(position);

                mouseDragging = false;
                mouseDragStarted = false;
                mouseTapEligible = false;
            }

            if (mouseDragging && pressed)
            {
                if (!mouseDragStarted)
                {
                    if (Vector2.Distance(position, mouseDragStartPosition) < touchDragDeadzonePixels)
                    {
                        previousMousePosition = position;
                        return;
                    }

                    mouseDragStarted = true;
                }

                PanByScreenDelta(position - previousMousePosition);
                previousMousePosition = position;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.001f && !IsPointerBlocked(position, -1))
            {
                float scrollSteps = Mathf.Abs(scroll) > 10f ? scroll / 120f : scroll;
                float ratio = Mathf.Pow(1.15f, scrollSteps * mouseScrollSensitivity);
                ZoomAroundScreenPoint(ratio, position);
            }
        }

        public void PanByScreenDelta(Vector2 screenDelta)
        {
            if (targetCamera == null || screenDelta.sqrMagnitude <= 0.000001f)
                return;

            Vector2 screenCenter = new Vector2(
                targetCamera.pixelRect.x + targetCamera.pixelRect.width * 0.5f,
                targetCamera.pixelRect.y + targetCamera.pixelRect.height * 0.5f);
            Vector3 before = GetWorldPointOnContentPlane(screenCenter);
            Vector3 after = GetWorldPointOnContentPlane(screenCenter + screenDelta);
            Vector3 previousPosition = targetCameraPosition;
            targetCameraPosition = ApplyRubberBandPosition(
                targetCameraPosition - (after - before) * panSensitivity,
                targetZoomValue);

            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime > 0.000001f)
            {
                Vector3 appliedDelta = targetCameraPosition - previousPosition;
                Vector3 measuredVelocity = appliedDelta / deltaTime;
                float blend = 1f - Mathf.Exp(-deltaTime / Mathf.Max(panVelocitySmoothingTime, 0.001f));
                panVelocity = Vector3.Lerp(panVelocity, measuredVelocity, blend);
                if (appliedDelta.sqrMagnitude <= 0.000001f)
                    panVelocity = Vector3.zero;
            }

            panInputThisFrame = true;
        }

        private void ApplyPanInertia()
        {
            if (panInputThisFrame || panVelocity.sqrMagnitude <= 0.000001f)
                return;

            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0.000001f)
                return;

            Vector3 previousPosition = targetCameraPosition;
            Vector3 velocity = Vector3.ClampMagnitude(panVelocity, maxPanInertiaSpeed);
            targetCameraPosition = ApplyRubberBandPosition(
                targetCameraPosition + velocity * deltaTime,
                targetZoomValue);

            if ((targetCameraPosition - previousPosition).sqrMagnitude <= 0.000001f)
            {
                panVelocity = Vector3.zero;
                return;
            }

            panVelocity *= Mathf.Exp(-panInertiaDamping * deltaTime);
        }

        public void ZoomAroundScreenPoint(float ratio, Vector2 screenPoint)
        {
            if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio))
                return;

            if (targetCamera.orthographic)
            {
                float oldZoom = targetZoomValue;
                float newZoom = Mathf.Clamp(
                    oldZoom / Mathf.Pow(ratio, zoomSensitivity),
                    minOrthographicSize,
                    maxOrthographicSize);

                if (Mathf.Approximately(oldZoom, newZoom))
                    return;

                Vector3 worldBefore = GetWorldPointAtOrthographicSize(
                    screenPoint,
                    targetCamera.orthographicSize,
                    targetCamera.transform.position);
                Vector3 worldAfter = GetWorldPointAtOrthographicSize(
                    screenPoint,
                    newZoom,
                    targetCamera.transform.position);

                targetCameraPosition += worldBefore - worldAfter;
                targetZoomValue = newZoom;
                targetCameraPosition = ClampCameraPosition(targetCameraPosition, targetZoomValue);
            }
            else
            {
                float oldDistance = targetZoomValue;
                float newDistance = Mathf.Clamp(
                    oldDistance / Mathf.Pow(ratio, zoomSensitivity),
                    minPerspectiveDistance,
                    maxPerspectiveDistance);
                targetZoomValue = newDistance;
            }
        }

        /// <summary>
        /// Smoothly centers the camera on a world point. This is useful for
        /// double-tap focus and for future bottleneck/building notifications.
        /// </summary>
        public void FocusOnWorldPoint(Vector3 worldPoint, float zoomValue = -1f)
        {
            if (targetCamera == null)
                return;

            Vector3 right = targetCamera.transform.right;
            Vector3 up = targetCamera.transform.up;
            float currentCameraX = Vector3.Dot(targetCameraPosition, right);
            float currentCameraY = Vector3.Dot(targetCameraPosition, up);
            float targetWorldX = Vector3.Dot(worldPoint, right);
            float targetWorldY = Vector3.Dot(worldPoint, up);

            Vector3 focusedPosition = targetCameraPosition
                + right * (targetWorldX - currentCameraX)
                + up * (targetWorldY - currentCameraY);

            if (zoomValue > 0f)
            {
                targetZoomValue = targetCamera.orthographic
                    ? Mathf.Clamp(zoomValue, minOrthographicSize, maxOrthographicSize)
                    : Mathf.Clamp(zoomValue, minPerspectiveDistance, maxPerspectiveDistance);
            }

            targetCameraPosition = ClampCameraPosition(focusedPosition, targetZoomValue);
            panVelocity = Vector3.zero;
            edgeReturnVelocity = Vector3.zero;
            cameraPositionVelocity = Vector3.zero;
            zoomValueVelocity = 0f;
        }

        private void FocusOnScreenPoint(Vector2 screenPoint)
        {
            Vector3 worldPoint = GetWorldPointOnContentPlane(screenPoint);
            float focusedZoom = targetZoomValue;

            if (targetCamera.orthographic)
            {
                if (targetZoomValue > minOrthographicSize + 0.01f)
                {
                    zoomBeforeDoubleTap = targetZoomValue;
                    focusedZoom = targetZoomValue * doubleTapZoomScale;
                }
                else if (zoomBeforeDoubleTap > 0f)
                {
                    focusedZoom = zoomBeforeDoubleTap;
                    zoomBeforeDoubleTap = -1f;
                }
            }

            FocusOnWorldPoint(worldPoint, focusedZoom);
        }

        private void RegisterTap(Vector2 screenPosition)
        {
            float now = Time.unscaledTime;
            bool isDoubleTap = lastTapTime >= 0f
                && now - lastTapTime <= doubleTapMaxDelay
                && Vector2.Distance(screenPosition, lastTapPosition) <= doubleTapMaxDistancePixels;

            if (isDoubleTap)
            {
                FocusOnScreenPoint(screenPosition);
                lastTapTime = -1f;
                return;
            }

            lastTapTime = now;
            lastTapPosition = screenPosition;
        }

        private void ApplyEdgeReturn()
        {
            if (!hasWorldBounds)
                return;

            Vector3 constrainedPosition = ClampCameraPosition(targetCameraPosition, targetZoomValue);
            if ((constrainedPosition - targetCameraPosition).sqrMagnitude <= 0.000001f)
            {
                targetCameraPosition = constrainedPosition;
                edgeReturnVelocity = Vector3.zero;
                return;
            }

            targetCameraPosition = Vector3.SmoothDamp(
                targetCameraPosition,
                constrainedPosition,
                ref edgeReturnVelocity,
                edgeReturnSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
        }

        private Vector3 ApplyRubberBandPosition(Vector3 position, float zoomValue)
        {
            if (!hasWorldBounds || edgeRubberBandDistance <= 0f)
                return ClampCameraPosition(position, zoomValue);

            GetCameraPositionLimits(position, zoomValue, out Vector3 right, out Vector3 up,
                out float minX, out float maxX, out float minY, out float maxY);

            float cameraX = Vector3.Dot(position, right);
            float cameraY = Vector3.Dot(position, up);
            float rubberX = ApplyRubberBandAxis(cameraX, minX, maxX);
            float rubberY = ApplyRubberBandAxis(cameraY, minY, maxY);

            return position
                + right * (rubberX - cameraX)
                + up * (rubberY - cameraY);
        }

        private float ApplyRubberBandAxis(float value, float min, float max)
        {
            if (min > max)
                return (min + max) * 0.5f;

            // Keep the camera centre inside the valid limits at all times.
            // The resistance zone provides the elastic feel without ever
            // exposing pixels outside the background.
            if (value <= min)
                return min;

            if (value >= max)
                return max;

            float edgeZone = Mathf.Min(edgeRubberBandDistance, (max - min) * 0.25f);
            if (edgeZone <= 0.0001f)
                return value;

            if (value < min + edgeZone)
            {
                float normalized = (value - min) / edgeZone;
                float eased = Mathf.SmoothStep(0f, 1f, normalized);
                float resisted = Mathf.Lerp(normalized, eased, edgeRubberBandStrength);
                return min + resisted * edgeZone;
            }

            if (value > max - edgeZone)
            {
                float normalized = (max - value) / edgeZone;
                float eased = Mathf.SmoothStep(0f, 1f, normalized);
                float resisted = Mathf.Lerp(normalized, eased, edgeRubberBandStrength);
                return max - resisted * edgeZone;
            }

            return Mathf.Clamp(value, min, max);
        }

        private bool IsOutsideCameraBounds(Vector3 position, float zoomValue)
        {
            return (ClampCameraPosition(position, zoomValue) - position).sqrMagnitude > 0.000001f;
        }

        private Vector3 ClampCameraPosition(Vector3 position, float zoomValue)
        {
            if (!hasWorldBounds)
                return position;

            GetCameraPositionLimits(position, zoomValue, out Vector3 right, out Vector3 up,
                out float minX, out float maxX, out float minY, out float maxY);
            float cameraX = Vector3.Dot(position, right);
            float cameraY = Vector3.Dot(position, up);
            float clampedX = ClampAxis(cameraX, minX, maxX);
            float clampedY = ClampAxis(cameraY, minY, maxY);

            return position + right * (clampedX - cameraX) + up * (clampedY - cameraY);
        }

        private void GetCameraPositionLimits(
            Vector3 position,
            float zoomValue,
            out Vector3 right,
            out Vector3 up,
            out float minX,
            out float maxX,
            out float minY,
            out float maxY)
        {
            right = targetCamera.transform.right;
            up = targetCamera.transform.up;
            float halfHeight;
            float halfWidth;

            if (targetCamera.orthographic)
            {
                halfHeight = zoomValue;
                halfWidth = zoomValue * targetCamera.aspect;
            }
            else
            {
                float depth = Mathf.Max(Vector3.Dot(worldBounds.center - position, targetCamera.transform.forward), 0.01f);
                halfHeight = depth * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                halfWidth = halfHeight * targetCamera.aspect;
            }

            GetProjectedWorldBounds(right, up, out float worldMinX, out float worldMaxX,
                out float worldMinY, out float worldMaxY);
            minX = worldMinX + halfWidth;
            maxX = worldMaxX - halfWidth;
            minY = worldMinY + halfHeight;
            maxY = worldMaxY - halfHeight;
        }

        private void GetProjectedWorldBounds(Vector3 right, Vector3 up, out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = float.PositiveInfinity;
            maxX = float.NegativeInfinity;
            minY = float.PositiveInfinity;
            maxY = float.NegativeInfinity;

            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        Vector3 corner = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        float projectedX = Vector3.Dot(corner, right);
                        float projectedY = Vector3.Dot(corner, up);
                        minX = Mathf.Min(minX, projectedX);
                        maxX = Mathf.Max(maxX, projectedX);
                        minY = Mathf.Min(minY, projectedY);
                        maxY = Mathf.Max(maxY, projectedY);
                    }
                }
            }
        }

        private static float ClampAxis(float value, float min, float max)
        {
            return min > max ? (min + max) * 0.5f : Mathf.Clamp(value, min, max);
        }

        private Vector3 GetWorldPointOnContentPlane(Vector2 screenPoint)
        {
            float depth = Vector3.Dot(worldBounds.center - targetCamera.transform.position, targetCamera.transform.forward);
            depth = Mathf.Max(depth, 0.01f);
            return targetCamera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, depth));
        }

        private Vector3 GetWorldPointAtOrthographicSize(Vector2 screenPoint, float orthographicSize, Vector3 cameraPosition)
        {
            Rect pixelRect = targetCamera.pixelRect;
            float normalizedX = ((screenPoint.x - pixelRect.xMin) / Mathf.Max(pixelRect.width, 1f) - 0.5f) * 2f;
            float normalizedY = ((screenPoint.y - pixelRect.yMin) / Mathf.Max(pixelRect.height, 1f) - 0.5f) * 2f;
            float depth = Vector3.Dot(worldBounds.center - cameraPosition, targetCamera.transform.forward);
            depth = Mathf.Max(depth, 0.01f);

            return cameraPosition
                + targetCamera.transform.right * (normalizedX * orthographicSize * targetCamera.aspect)
                + targetCamera.transform.up * (normalizedY * orthographicSize)
                + targetCamera.transform.forward * depth;
        }

        private float GetPerspectiveDistance()
        {
            return Mathf.Max(Vector3.Distance(targetCamera.transform.position, worldBounds.center), 0.01f);
        }

        private void ApplyPerspectiveZoom()
        {
            Vector3 focus = hasWorldBounds ? worldBounds.center : targetCamera.transform.position + targetCamera.transform.forward * targetZoomValue;
            Vector3 direction = (targetCamera.transform.position - focus).normalized;
            if (direction.sqrMagnitude < 0.0001f)
                direction = -targetCamera.transform.forward;

            float currentDistance = Mathf.SmoothDamp(
                Vector3.Distance(targetCamera.transform.position, focus),
                targetZoomValue,
                ref zoomValueVelocity,
                zoomSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            targetCamera.transform.position = focus + direction * currentDistance;
        }

        private static Vector2 GetTouchCenter(Vector2 first, Vector2 second)
        {
            return (first + second) * 0.5f;
        }

        private static float GetTouchDistance(Vector2 first, Vector2 second)
        {
            return Vector2.Distance(first, second);
        }

        private void ResetGestureState()
        {
            previousTouchCount = 0;
            gestureBlocked = false;
            singleTouchDragging = false;
            edgeReturnVelocity = Vector3.zero;
        }

        private bool IsPointerBlocked(Vector2 screenPosition, int pointerId)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            var pointerData = new PointerEventData(eventSystem)
            {
                position = screenPosition,
                pointerId = pointerId
            };
            raycastResults.Clear();
            eventSystem.RaycastAll(pointerData, raycastResults);

            foreach (var raycast in raycastResults)
            {
                if (raycast.gameObject == null)
                    continue;

                Transform raycastTransform = raycast.gameObject.transform;
                bool isWorldCanvasGraphic = worldCanvasTransform != null
                    && (raycastTransform == worldCanvasTransform || raycastTransform.IsChildOf(worldCanvasTransform));

                if (isWorldCanvasGraphic)
                    continue;

                return true;
            }

            return false;
        }
    }
}
