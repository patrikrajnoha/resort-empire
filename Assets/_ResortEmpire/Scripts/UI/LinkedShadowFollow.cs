using UnityEngine;

namespace ResortEmpire.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class LinkedShadowFollow : MonoBehaviour
    {
        [SerializeField] private RectTransform target;
        [SerializeField] private Vector2 offset = new Vector2(26f, -34f);

        private RectTransform shadowRect;

        private void Awake()
        {
            CacheReferences();
        }

        private void OnEnable()
        {
            CacheReferences();
            ApplyShadowPosition();
        }

        private void LateUpdate()
        {
            ApplyShadowPosition();
        }

        private void OnValidate()
        {
            CacheReferences();
            ApplyShadowPosition();
        }

        private void CacheReferences()
        {
            if (shadowRect == null)
                shadowRect = GetComponent<RectTransform>();
        }

        private void ApplyShadowPosition()
        {
            if (shadowRect == null || target == null)
                return;

            Transform parent = shadowRect.parent;
            if (parent == null)
                return;

            Vector3 targetPosition = parent.InverseTransformPoint(target.position);
            Vector3 shadowPosition = shadowRect.localPosition;
            shadowPosition.x = targetPosition.x + offset.x;
            shadowPosition.y = targetPosition.y + offset.y;
            shadowRect.localPosition = shadowPosition;
        }
    }
}
