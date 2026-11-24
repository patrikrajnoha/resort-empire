using System;
using System.Collections.Generic;
using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>Places waiting-guest positions along a walkable route that leads away from a building.</summary>
    public static class GuestQueueLayout
    {
        /// <summary>
        /// Walks <paramref name="route"/> from its first point and appends up to <paramref name="count"/>
        /// evenly spaced positions. A position is skipped (and the walk nudged forward) when
        /// <paramref name="isValid"/> rejects it, for example because the guest would overlap
        /// building art, or when it is closer than <paramref name="minSeparation"/> to a reserved
        /// position belonging to another queue. Returns the number of positions placed; a short
        /// route yields fewer positions rather than positions off the walkable path.
        /// When <paramref name="distances"/> is supplied it receives each placed position's
        /// distance along the route, in the same order.
        /// </summary>
        public static int PlaceSlots(
            IReadOnlyList<Vector2> route,
            int count,
            float firstOffset,
            float spacing,
            float minSeparation,
            Func<Vector2, bool> isValid,
            IReadOnlyList<Vector2> reserved,
            List<Vector2> result,
            List<float> distances = null)
        {
            if (route == null)
                throw new ArgumentNullException(nameof(route));
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (spacing <= 0f)
                throw new ArgumentOutOfRangeException(nameof(spacing), spacing, "Queue spacing must be positive.");
            if (count <= 0 || route.Count == 0)
                return 0;

            var length = GetLength(route);
            var nudge = Mathf.Max(1f, spacing * 0.2f);
            var distance = Mathf.Max(0f, firstOffset);
            var placed = 0;
            while (placed < count && distance <= length)
            {
                var candidate = Sample(route, distance);
                if ((isValid == null || isValid(candidate))
                    && IsSeparated(candidate, reserved, minSeparation)
                    && IsSeparated(candidate, result, minSeparation, result.Count - placed))
                {
                    result.Add(candidate);
                    distances?.Add(distance);
                    placed++;
                    distance += spacing;
                }
                else
                {
                    distance += nudge;
                }
            }

            return placed;
        }

        /// <summary>Position at <paramref name="distance"/> along the polyline, clamped to its ends.</summary>
        public static Vector2 Sample(IReadOnlyList<Vector2> route, float distance)
        {
            if (route == null || route.Count == 0)
                throw new ArgumentException("A route needs at least one point.", nameof(route));
            if (route.Count == 1 || distance <= 0f)
                return route[0];

            var remaining = distance;
            for (var index = 1; index < route.Count; index++)
            {
                var segment = Vector2.Distance(route[index - 1], route[index]);
                if (remaining <= segment)
                    return segment <= 0f
                        ? route[index]
                        : Vector2.Lerp(route[index - 1], route[index], remaining / segment);
                remaining -= segment;
            }

            return route[route.Count - 1];
        }

        public static float GetLength(IReadOnlyList<Vector2> route)
        {
            var length = 0f;
            for (var index = 1; index < route.Count; index++)
                length += Vector2.Distance(route[index - 1], route[index]);
            return length;
        }

        private static bool IsSeparated(Vector2 candidate, IReadOnlyList<Vector2> others, float minSeparation, int start = 0)
        {
            if (others == null || minSeparation <= 0f)
                return true;

            var minSquared = minSeparation * minSeparation;
            for (var index = Mathf.Max(0, start); index < others.Count; index++)
            {
                if ((others[index] - candidate).sqrMagnitude < minSquared)
                    return false;
            }

            return true;
        }
    }
}
