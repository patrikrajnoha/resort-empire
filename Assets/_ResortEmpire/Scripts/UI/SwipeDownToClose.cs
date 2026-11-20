using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace ResortEmpire.UI
{
    /// <summary>
    /// Bottom-sheet gesture used by the worker allocation window. The handle owns only the
    /// vertical close gesture, so buttons and cards below it keep their normal pointer behavior.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SwipeDownToClose : MonoBehaviour, IPointerDownHandler, IDragHandler,
        IPointerUpHandler, ICancelHandler
    {
        [SerializeField] private float gestureThreshold = 18f;
        [SerializeField] private float closeDistance = 72f;
        [SerializeField] private float flickVelocity = 850f;
        [SerializeField] private float snapDuration = 0.22f;

        private RectTransform sheet;
        private ResortWindowView window;
        private UnityAction closeAction;
        private Canvas canvas;
        private Vector2 openPosition;
        private Vector2 pointerStart;
        private Vector2 lastPointer;
        private float lastPointerTime;
        private float pointerVelocity;
        private int pointerId;
        private bool pointerDown;
        private bool dragging;
        private bool cancelled;
        private Coroutine snapCoroutine;

        public void Configure(RectTransform targetSheet, ResortWindowView targetWindow)
        {
            sheet = targetSheet;
            window = targetWindow;
            closeAction = window != null ? window.Close : null;
            canvas = GetComponentInParent<Canvas>();
            if (sheet != null) openPosition = sheet.anchoredPosition;
            ResetPosition();
        }

        public void Configure(RectTransform targetSheet, UnityAction closeCallback)
        {
            sheet = targetSheet;
            window = null;
            closeAction = closeCallback;
            canvas = GetComponentInParent<Canvas>();
            if (sheet != null) openPosition = sheet.anchoredPosition;
            ResetPosition();
        }

        public void ResetPosition()
        {
            StopSnap();
            pointerDown = false;
            dragging = false;
            cancelled = false;
            if (sheet != null) sheet.anchoredPosition = openPosition;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (sheet == null || eventData.button != PointerEventData.InputButton.Left) return;
            StopSnap();
            pointerDown = true;
            dragging = false;
            cancelled = false;
            pointerId = eventData.pointerId;
            pointerStart = eventData.position;
            lastPointer = eventData.position;
            lastPointerTime = Time.unscaledTime;
            pointerVelocity = 0f;
            eventData.Use();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!pointerDown || eventData.pointerId != pointerId || sheet == null) return;

            var total = eventData.position - pointerStart;
            if (!dragging)
            {
                var horizontal = Mathf.Abs(total.x);
                var vertical = Mathf.Abs(total.y);
                if (horizontal > vertical * 1.15f && horizontal > gestureThreshold)
                {
                    cancelled = true;
                    return;
                }
                if (total.y > -gestureThreshold) return;
                dragging = true;
            }

            var scale = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
            var offset = Mathf.Min(0f, total.y / scale);
            var maximum = -Mathf.Max(closeDistance * 2f, sheet.rect.height + 48f);
            sheet.anchoredPosition = new Vector2(openPosition.x, openPosition.y + Mathf.Max(maximum, offset));

            var now = Time.unscaledTime;
            var deltaTime = now - lastPointerTime;
            if (deltaTime > 0.0001f)
                pointerVelocity = (eventData.position.y - lastPointer.y) / deltaTime;
            lastPointer = eventData.position;
            lastPointerTime = now;
            eventData.Use();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pointerDown || eventData.pointerId != pointerId) return;
            pointerDown = false;
            if (cancelled || !dragging)
            {
                SnapBack();
                return;
            }

            var distance = openPosition.y - sheet.anchoredPosition.y;
            var quickClose = pointerVelocity <= -flickVelocity;
            if (distance >= closeDistance || quickClose)
                SnapClosed();
            else
                SnapBack();
            eventData.Use();
        }

        public void OnCancel(BaseEventData eventData)
        {
            pointerDown = false;
            cancelled = true;
            SnapBack();
        }

        private void SnapBack()
        {
            if (sheet == null) return;
            StartSnap(openPosition, false);
        }

        private void SnapClosed()
        {
            if (sheet == null) return;
            var target = openPosition + new Vector2(0f, -Mathf.Max(closeDistance * 2f, sheet.rect.height + 48f));
            StartSnap(target, true);
        }

        private void StartSnap(Vector2 target, bool closeAfter)
        {
            StopSnap();
            snapCoroutine = StartCoroutine(SnapTo(target, closeAfter));
        }

        private System.Collections.IEnumerator SnapTo(Vector2 target, bool closeAfter)
        {
            var start = sheet.anchoredPosition;
            var elapsed = 0f;
            while (elapsed < snapDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, snapDuration));
                t = t * t * (3f - 2f * t);
                sheet.anchoredPosition = Vector2.LerpUnclamped(start, target, t);
                yield return null;
            }

            sheet.anchoredPosition = target;
            snapCoroutine = null;
            if (closeAfter)
            {
                if (closeAction != null) closeAction.Invoke();
                else transform.root.gameObject.SetActive(false);
            }
        }

        private void StopSnap()
        {
            if (snapCoroutine == null) return;
            StopCoroutine(snapCoroutine);
            snapCoroutine = null;
        }
    }
}
