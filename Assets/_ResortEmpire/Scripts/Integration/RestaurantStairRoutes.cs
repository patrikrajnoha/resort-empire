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
        private struct DiningTable
        {
            [Tooltip("Bottom edge of the seated guest, normalized within the sprite.")]
            public Vector2 Seat;
            public float Scale;
        }

        [Serializable]
        private struct StairRoute
        {
            public Sprite Sprite;
            [Tooltip("Foot positions from the bottom of the exterior stairs to the landing, normalized within the sprite.")]
            public Vector2[] Waypoints;
            [Tooltip("Visible dining tables in the order that guests fill them.")]
            public DiningTable[] DiningTables;
        }

        [SerializeField] private StairRoute[] routes;
        [SerializeField] private RestaurantDiningVisual diningSeatTemplate;

        private readonly List<RestaurantDiningVisual> diningSeats = new List<RestaurantDiningVisual>();

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

        internal void ShowDiner(GameObject tourist)
        {
            if (diningSeatTemplate == null)
                return;

            foreach (var seat in diningSeats)
                if (seat.Guest == tourist)
                    return;

            if (!TryGetLayout(out var route, out var rect) || route.DiningTables == null)
                return;

            for (var index = 0; index < route.DiningTables.Length; index++)
            {
                if (index == diningSeats.Count)
                    diningSeats.Add(Instantiate(diningSeatTemplate, diningSeatTemplate.transform.parent, false));

                var seat = diningSeats[index];
                if (seat.IsOccupied)
                    continue;

                var table = route.DiningTables[index];
                var position = transform.TransformPoint(Rect.NormalizedToPoint(rect, table.Seat));
                seat.Show(tourist, position, table.Scale);
                return;
            }
        }

        internal void AdvanceDining(float deltaTime)
        {
            foreach (var seat in diningSeats)
                seat.Advance(deltaTime);
        }

        internal void ReleaseDiner(GameObject tourist)
        {
            foreach (var seat in diningSeats)
                seat.Release(tourist);
        }

        internal void ClearDining()
        {
            foreach (var seat in diningSeats)
                if (seat != null)
                    seat.Clear();
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
