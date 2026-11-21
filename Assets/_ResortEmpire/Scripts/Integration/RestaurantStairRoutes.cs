using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.Gameplay.Integration
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class RestaurantStairRoutes : MonoBehaviour
    {
        [Serializable]
        private struct StairRoute
        {
            public Sprite Sprite;
            [Tooltip("Foot positions from the bottom of the exterior stairs to the landing, normalized within the sprite.")]
            public Vector2[] Waypoints;
            [Tooltip("Bottom edge of the seated guest at the right table, normalized within the sprite.")]
            public Vector2 DiningSeat;
        }

        [SerializeField] private StairRoute[] routes;
        [SerializeField] private RestaurantDiningVisual diningVisual;

        internal RestaurantDiningVisual DiningVisual => diningVisual;

        internal bool TryGetWaypoints(Transform coordinateSpace, List<Vector2> waypoints)
        {
            waypoints.Clear();
            if (!TryGetLayout(out var route, out var rect) || route.Waypoints == null || route.Waypoints.Length < 3)
                return false;

            foreach (var waypoint in route.Waypoints)
            {
                var localPoint = Rect.NormalizedToPoint(rect, waypoint);
                waypoints.Add(coordinateSpace.InverseTransformPoint(transform.TransformPoint(localPoint)));
            }
            return true;
        }

        internal bool TryGetDiningSeat(out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!TryGetLayout(out var route, out var rect) || route.DiningSeat == Vector2.zero)
                return false;

            worldPosition = transform.TransformPoint(Rect.NormalizedToPoint(rect, route.DiningSeat));
            return true;
        }

        private bool TryGetLayout(out StairRoute activeRoute, out Rect rect)
        {
            activeRoute = default;
            rect = default;
            var image = GetComponent<Image>();
            var sprite = image.overrideSprite;
            if (sprite == null || routes == null)
                return false;

            foreach (var route in routes)
            {
                if (route.Sprite != sprite)
                    continue;

                rect = image.GetPixelAdjustedRect();
                if (image.preserveAspect)
                {
                    var size = sprite.rect.size;
                    var scale = Mathf.Min(rect.width / size.x, rect.height / size.y);
                    var fittedSize = size * scale;
                    rect.position += Vector2.Scale(rect.size - fittedSize, image.rectTransform.pivot);
                    rect.size = fittedSize;
                }

                activeRoute = route;
                return true;
            }

            return false;
        }
    }
}
