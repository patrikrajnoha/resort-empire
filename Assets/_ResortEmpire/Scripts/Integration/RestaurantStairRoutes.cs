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
        }

        [SerializeField] private StairRoute[] routes;

        internal bool TryGetWaypoints(Transform coordinateSpace, List<Vector2> waypoints)
        {
            waypoints.Clear();
            var image = GetComponent<Image>();
            var sprite = image.overrideSprite;
            if (sprite == null || routes == null)
                return false;

            foreach (var route in routes)
            {
                if (route.Sprite != sprite || route.Waypoints == null || route.Waypoints.Length < 3)
                    continue;

                var rect = image.GetPixelAdjustedRect();
                if (image.preserveAspect)
                {
                    var size = sprite.rect.size;
                    var scale = Mathf.Min(rect.width / size.x, rect.height / size.y);
                    var fittedSize = size * scale;
                    rect.position += Vector2.Scale(rect.size - fittedSize, image.rectTransform.pivot);
                    rect.size = fittedSize;
                }

                foreach (var waypoint in route.Waypoints)
                {
                    var localPoint = Rect.NormalizedToPoint(rect, waypoint);
                    waypoints.Add(coordinateSpace.InverseTransformPoint(transform.TransformPoint(localPoint)));
                }
                return true;
            }

            return false;
        }
    }
}
