using UnityEngine;

namespace ResortEmpire.UI
{
    /// <summary>
    /// Keeps a screen-space UI element aligned with a world-space target as the camera moves.
    /// The marker remains a normal UI control, so its Button and visual hierarchy are preserved.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class WorldAnchoredUiMarker : MonoBehaviour
    {
        [SerializeField] public Transform worldTarget;
        [SerializeField] public Camera worldCamera;
        [SerializeField] public Canvas targetCanvas;
        [SerializeField] public Vector3 worldOffset;
        [SerializeField] public bool hideWhenBehindCamera = true;

        private RectTransform markerRect;
        private RectTransform parentRect;
        private CanvasGroup canvasGroup;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            RefreshPosition();
        }

        private void LateUpdate()
        {
            RefreshPosition();
        }

        public void RefreshNow()
        {
            RefreshPosition();
        }

        public void Configure(Transform target, Camera camera, Canvas canvas, Vector3 offset)
        {
            worldTarget = target;
            worldCamera = camera;
            targetCanvas = canvas;
            worldOffset = offset;
            ResolveReferences();
            RefreshPosition();
        }

        private void ResolveReferences()
        {
            if (markerRect == null)
                markerRect = GetComponent<RectTransform>();
            if (parentRect == null)
                parentRect = markerRect == null ? null : markerRect.parent as RectTransform;
            if (targetCanvas == null)
                targetCanvas = GetComponentInParent<Canvas>(true);
            if (worldCamera == null)
                worldCamera = targetCanvas == null ? Camera.main : targetCanvas.worldCamera;
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void RefreshPosition()
        {
            ResolveReferences();
            if (worldTarget == null || worldCamera == null || targetCanvas == null || parentRect == null)
            {
                SetVisible(false);
                return;
            }

            var screenPoint = worldCamera.WorldToScreenPoint(worldTarget.position + worldOffset);
            if (hideWhenBehindCamera && screenPoint.z <= 0f)
            {
                SetVisible(false);
                return;
            }

            var eventCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : targetCanvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect,
                    screenPoint,
                    eventCamera,
                    out var localPoint))
            {
                SetVisible(false);
                return;
            }

            markerRect.anchoredPosition = localPoint;
            SetVisible(true);
        }

        private void SetVisible(bool visible)
        {
            if (canvasGroup == null)
                return;

            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }
    }
}
