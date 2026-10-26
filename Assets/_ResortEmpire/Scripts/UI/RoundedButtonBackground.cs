using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>Draws a scalable rounded button fill with a separate border.</summary>
    [AddComponentMenu("UI/Rounded Button Background")]
    public sealed class RoundedButtonBackground : MaskableGraphic
    {
        [SerializeField, Min(0f)] private float borderWidth = 7f;
        [SerializeField, Min(0f)] private float cornerRadius = 24f;
        [SerializeField] private Color fillColor = new(0.46f, 0.20f, 0.94f, 1f);
        [SerializeField] private Color borderColor = Color.white;
        [SerializeField, Range(2, 16)] private int cornerSegments = 8;

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();

            var rect = rectTransform.rect;
            var outerRadius = Mathf.Min(cornerRadius, Mathf.Min(rect.width, rect.height) * 0.5f);
            var innerRect = new Rect(
                rect.xMin + borderWidth,
                rect.yMin + borderWidth,
                Mathf.Max(0f, rect.width - borderWidth * 2f),
                Mathf.Max(0f, rect.height - borderWidth * 2f));
            var innerRadius = Mathf.Max(0f, outerRadius - borderWidth);

            AddRoundedRect(vertexHelper, rect, outerRadius, borderColor);
            AddRoundedRect(vertexHelper, innerRect, innerRadius, fillColor);
        }

        private void AddRoundedRect(VertexHelper vertexHelper, Rect rect, float radius, Color color)
        {
            var centerIndex = vertexHelper.currentVertCount;
            vertexHelper.AddVert(rect.center, color, Vector2.zero);

            var count = Mathf.Max(2, cornerSegments);
            for (var corner = 0; corner < 4; corner++)
            {
                var cornerCenter = corner switch
                {
                    0 => new Vector2(rect.xMax - radius, rect.yMax - radius),
                    1 => new Vector2(rect.xMax - radius, rect.yMin + radius),
                    2 => new Vector2(rect.xMin + radius, rect.yMin + radius),
                    _ => new Vector2(rect.xMin + radius, rect.yMax - radius)
                };
                var startAngle = 90f - corner * 90f;
                for (var i = 0; i <= count; i++)
                {
                    var angle = (startAngle - i * 90f / count) * Mathf.Deg2Rad;
                    var point = cornerCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    vertexHelper.AddVert(point, color, Vector2.zero);
                }
            }

            var ringStart = centerIndex + 1;
            var ringCount = vertexHelper.currentVertCount - ringStart;
            for (var i = 0; i < ringCount; i++)
                vertexHelper.AddTriangle(centerIndex, ringStart + i, ringStart + (i + 1) % ringCount);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetVerticesDirty();
        }
#endif
    }
}
