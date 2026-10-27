using UnityEngine;
namespace ResortEmpire.UI {
    [ExecuteAlways] public sealed class DesignStage : MonoBehaviour {
        public Vector2 ReferenceSize = new Vector2(780,360);
        void OnEnable() { Fit(); }
        void LateUpdate() { Fit(); }
        void OnRectTransformDimensionsChange() { Fit(); }
        void Fit() {
            var rect = transform.parent as RectTransform;
            if (!rect || ReferenceSize.x <= 0 || ReferenceSize.y <= 0) return;
            float scale = Mathf.Min(rect.rect.width / ReferenceSize.x, rect.rect.height / ReferenceSize.y);
            if (scale > 0 && !Mathf.Approximately(transform.localScale.x, scale))
                transform.localScale = Vector3.one * scale;
        }
    }
}
