using UnityEngine;

namespace ResortEmpire.UI
{
    /// <summary>Keeps the island's authored panel position inside the safe area on narrower screens.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class BuildingInfoPanelPlacement : MonoBehaviour
    {
        private const float EdgePadding = 36f;

        private RectTransform panel;
        private RectTransform parent;
        private Canvas canvas;
        private Vector2 preferredPosition;
        private Rect lastSafeArea;
        private Rect lastParentRect;
        private float lastCanvasScale;

        private void Awake()
        {
            panel = (RectTransform)transform;
            parent = panel.parent as RectTransform;
            canvas = GetComponentInParent<Canvas>()?.rootCanvas;
            preferredPosition = panel.anchoredPosition;
        }

        private void OnEnable() => UpdatePlacement(force: true);

        private void LateUpdate() => UpdatePlacement(force: false);

        private void UpdatePlacement(bool force)
        {
            if (parent == null || canvas == null) return;

            var safeArea = Screen.safeArea;
            var parentRect = parent.rect;
            var canvasScale = canvas.scaleFactor;
            if (!force && safeArea == lastSafeArea && parentRect == lastParentRect &&
                Mathf.Approximately(canvasScale, lastCanvasScale)) return;
            lastSafeArea = safeArea;
            lastParentRect = parentRect;
            lastCanvasScale = canvasScale;

            var safeMin = parentRect.min;
            var safeMax = parentRect.max;
            if (safeArea.width > 0f && safeArea.height > 0f &&
                safeArea != new Rect(0, 0, Screen.width, Screen.height))
            {
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safeArea.min, camera, out var screenMin) &&
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safeArea.max, camera, out var screenMax))
                {
                    safeMin = Vector2.Max(safeMin, screenMin);
                    safeMax = Vector2.Min(safeMax, screenMax);
                }
            }

            var anchor = (Vector2)panel.localPosition - panel.anchoredPosition;
            var min = safeMin - panel.rect.min - anchor + Vector2.one * EdgePadding;
            var max = safeMax - panel.rect.max - anchor - Vector2.one * EdgePadding;
            panel.anchoredPosition = new Vector2(
                FitAxis(preferredPosition.x, min.x, max.x),
                FitAxis(preferredPosition.y, min.y, max.y));
        }

        private static float FitAxis(float preferred, float min, float max) =>
            min <= max ? Mathf.Clamp(preferred, min, max) : (min + max) * 0.5f;
    }
}
