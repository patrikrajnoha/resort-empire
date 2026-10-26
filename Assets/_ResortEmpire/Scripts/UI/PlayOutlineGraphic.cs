using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>Scalable white outlined play icon for primary action buttons.</summary>
    [AddComponentMenu("UI/Play Outline Graphic")]
    public sealed class PlayOutlineGraphic : MaskableGraphic
    {
        [SerializeField] private Color iconColor = Color.white;

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            var rect = rectTransform.rect;
            var center = rect.center;
            var scale = Mathf.Min(rect.width / 48f, rect.height / 52f);

            var outer = new[]
            {
                center + new Vector2(-17f, -21f) * scale,
                center + new Vector2(-17f, 21f) * scale,
                center + new Vector2(20f, 0f) * scale
            };
            var inner = new[]
            {
                center + new Vector2(-12f, -15f) * scale,
                center + new Vector2(-12f, 15f) * scale,
                center + new Vector2(13f, 0f) * scale
            };

            for (var i = 0; i < 3; i++) vertexHelper.AddVert(outer[i], iconColor, Vector2.zero);
            for (var i = 0; i < 3; i++) vertexHelper.AddVert(inner[i], iconColor, Vector2.zero);

            for (var i = 0; i < 3; i++)
            {
                var next = (i + 1) % 3;
                vertexHelper.AddTriangle(i, next, 3 + next);
                vertexHelper.AddTriangle(i, 3 + next, 3 + i);
            }
        }
    }
}
