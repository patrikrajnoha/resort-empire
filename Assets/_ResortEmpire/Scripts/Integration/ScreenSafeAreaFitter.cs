using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>Keeps a full-screen RectTransform inside <see cref="Screen.safeArea"/> (camera cutouts, rounded corners).</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ScreenSafeAreaFitter : MonoBehaviour
    {
        private Rect appliedArea;
        private Vector2Int appliedScreen;

        private void OnEnable() => Fit(true);
        private void Update() => Fit(false);

        private void Fit(bool force)
        {
            var area = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (!force && area == appliedArea && screen == appliedScreen) return;
            appliedArea = area;
            appliedScreen = screen;
            if (screen.x <= 0 || screen.y <= 0) return;

            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(area.xMin / screen.x, area.yMin / screen.y);
            rect.anchorMax = new Vector2(area.xMax / screen.x, area.yMax / screen.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
