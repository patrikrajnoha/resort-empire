using System;
using System.Collections.Generic;
using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// Routes presentation-only NPCs across the walkable sand in island-base.
    /// The source texture is a small, baked map: white cells are walkable.
    /// Coordinates are in the centered IslandBase RectTransform's local space.
    /// </summary>
    public sealed class IslandNavigationMap
    {
        private readonly bool[] walkable;
        private readonly int[] regions;
        private readonly int width;
        private readonly int height;
        private readonly Rect bounds;
        private readonly IReadOnlyList<NavigationObstacle> blockedAreas;
        private readonly IReadOnlyList<Rect> walkableOverrides;

        public readonly struct NavigationObstacle
        {
            private readonly Vector2[] corners;
            private readonly Rect bounds;
            private readonly bool preserveProtectedArea;

            public NavigationObstacle(Rect bounds)
                : this(bounds, true)
            {
            }

            public NavigationObstacle(Rect bounds, bool preserveProtectedArea)
            {
                this.bounds = bounds;
                this.preserveProtectedArea = preserveProtectedArea;
                corners = new[]
                {
                    new Vector2(bounds.xMin, bounds.yMin),
                    new Vector2(bounds.xMax, bounds.yMin),
                    new Vector2(bounds.xMax, bounds.yMax),
                    new Vector2(bounds.xMin, bounds.yMax)
                };
            }

            public NavigationObstacle(IReadOnlyList<Vector2> corners)
                : this(corners, true)
            {
            }

            public NavigationObstacle(
                IReadOnlyList<Vector2> corners,
                bool preserveProtectedArea)
            {
                if (corners == null || corners.Count < 3)
                    throw new ArgumentException("A navigation obstacle needs at least three corners.", nameof(corners));

                this.preserveProtectedArea = preserveProtectedArea;
                this.corners = new Vector2[corners.Count];
                var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                for (var index = 0; index < corners.Count; index++)
                {
                    this.corners[index] = corners[index];
                    min = Vector2.Min(min, corners[index]);
                    max = Vector2.Max(max, corners[index]);
                }

                bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }

            public bool PreserveProtectedArea => preserveProtectedArea;

            public bool Contains(Vector2 point)
            {
                if (!bounds.Contains(point))
                    return false;

                var winding = 0f;
                for (var index = 0; index < corners.Length; index++)
                {
                    var current = corners[index];
                    var next = corners[(index + 1) % corners.Length];
                    var edge = next - current;
                    var offset = point - current;
                    var cross = edge.x * offset.y - edge.y * offset.x;
                    if (Mathf.Abs(cross) <= 0.001f)
                        continue;

                    if (Mathf.Approximately(winding, 0f))
                        winding = Mathf.Sign(cross);
                    else if (winding * cross < 0f)
                        return false;
                }

                return true;
            }

            public bool IntersectsSegment(Vector2 start, Vector2 end)
            {
                if (Contains(start) || Contains(end))
                    return true;

                for (var index = 0; index < corners.Length; index++)
                {
                    var edgeStart = corners[index];
                    var edgeEnd = corners[(index + 1) % corners.Length];
                    if (SegmentsIntersect(start, end, edgeStart, edgeEnd))
                        return true;
                }

                return false;
            }

            private static bool SegmentsIntersect(
                Vector2 firstStart,
                Vector2 firstEnd,
                Vector2 secondStart,
                Vector2 secondEnd)
            {
                var firstDirection = Cross(firstEnd - firstStart, secondStart - firstStart);
                var secondDirection = Cross(firstEnd - firstStart, secondEnd - firstStart);
                var thirdDirection = Cross(secondEnd - secondStart, firstStart - secondStart);
                var fourthDirection = Cross(secondEnd - secondStart, firstEnd - secondStart);

                if (((firstDirection > 0f && secondDirection < 0f)
                    || (firstDirection < 0f && secondDirection > 0f))
                    && ((thirdDirection > 0f && fourthDirection < 0f)
                        || (thirdDirection < 0f && fourthDirection > 0f)))
                {
                    return true;
                }

                return Mathf.Abs(firstDirection) <= 0.001f
                    && OnSegment(firstStart, firstEnd, secondStart)
                    || Mathf.Abs(secondDirection) <= 0.001f
                        && OnSegment(firstStart, firstEnd, secondEnd)
                    || Mathf.Abs(thirdDirection) <= 0.001f
                        && OnSegment(secondStart, secondEnd, firstStart)
                    || Mathf.Abs(fourthDirection) <= 0.001f
                        && OnSegment(secondStart, secondEnd, firstEnd);
            }

            private static float Cross(Vector2 first, Vector2 second)
            {
                return first.x * second.y - first.y * second.x;
            }

            private static bool OnSegment(Vector2 start, Vector2 end, Vector2 point)
            {
                return point.x >= Mathf.Min(start.x, end.x) - 0.001f
                    && point.x <= Mathf.Max(start.x, end.x) + 0.001f
                    && point.y >= Mathf.Min(start.y, end.y) - 0.001f
                    && point.y <= Mathf.Max(start.y, end.y) + 0.001f;
            }
        }

        public IslandNavigationMap(Texture2D texture, Rect bounds)
            : this(
                texture,
                bounds,
                false,
                texture == null ? 0 : texture.width,
                texture == null ? 0 : texture.height,
                (IReadOnlyList<Rect>)null)
        {
        }

        /// <summary>
        /// Builds a navigation map from an authoring texture. The source can use
        /// either the red channel (the baked white mask) or alpha (an invisible
        /// walkable-path layer). Sampling it onto a smaller grid keeps large
        /// source artwork cheap at runtime.
        /// </summary>
        public IslandNavigationMap(
            Texture2D texture,
            Rect bounds,
            bool useAlphaChannel,
            int sampleWidth,
            int sampleHeight)
            : this(
                texture,
                bounds,
                useAlphaChannel,
                sampleWidth,
                sampleHeight,
                (IReadOnlyList<Rect>)null,
                null)
        {
        }

        public IslandNavigationMap(
            Texture2D texture,
            Rect bounds,
            bool useAlphaChannel,
            int sampleWidth,
            int sampleHeight,
            IReadOnlyList<Rect> blockedAreas)
            : this(texture, bounds, useAlphaChannel, sampleWidth, sampleHeight, blockedAreas, null)
        {
        }

        public IslandNavigationMap(
            Texture2D texture,
            Rect bounds,
            bool useAlphaChannel,
            int sampleWidth,
            int sampleHeight,
            IReadOnlyList<Rect> blockedAreas,
            Texture2D protectedAreaTexture)
            : this(
                texture,
                bounds,
                useAlphaChannel,
                sampleWidth,
                sampleHeight,
                ConvertBlockedAreas(blockedAreas),
                protectedAreaTexture)
        {
        }

        public IslandNavigationMap(
            Texture2D texture,
            Rect bounds,
            bool useAlphaChannel,
            int sampleWidth,
            int sampleHeight,
            IReadOnlyList<NavigationObstacle> blockedAreas,
            Texture2D protectedAreaTexture)
            : this(
                texture,
                bounds,
                useAlphaChannel,
                sampleWidth,
                sampleHeight,
                blockedAreas,
                protectedAreaTexture,
                null)
        {
        }

        public IslandNavigationMap(
            Texture2D texture,
            Rect bounds,
            bool useAlphaChannel,
            int sampleWidth,
            int sampleHeight,
            IReadOnlyList<NavigationObstacle> blockedAreas,
            Texture2D protectedAreaTexture,
            IReadOnlyList<Rect> walkableOverrides)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));
            if (bounds.width <= 0f || bounds.height <= 0f)
                throw new ArgumentException("The island bounds must have positive dimensions.", nameof(bounds));
            if (sampleWidth <= 0 || sampleHeight <= 0)
                throw new ArgumentException("Navigation sample dimensions must be positive.");

            width = sampleWidth;
            height = sampleHeight;
            this.bounds = bounds;
            this.blockedAreas = blockedAreas;
            this.walkableOverrides = walkableOverrides;
            walkable = new bool[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                // Navigation coordinates use a top-down island image, while
                // Unity texture coordinates use a bottom-up V axis.
                var u = (x + 0.5f) / width;
                var v = 1f - (y + 0.5f) / height;
                var sample = texture.GetPixelBilinear(u, v);
                var center = new Vector2(
                    this.bounds.xMin + (x + 0.5f) * this.bounds.width / width,
                    this.bounds.yMax - (y + 0.5f) * this.bounds.height / height);
                var isWalkable = IsWalkableOverride(center)
                    || (useAlphaChannel
                        ? sample.a >= 0.5f
                        : sample.r >= 0.5f);
                if (isWalkable && blockedAreas != null)
                {
                    for (var areaIndex = 0; areaIndex < blockedAreas.Count; areaIndex++)
                    {
                        if (!blockedAreas[areaIndex].Contains(center))
                            continue;

                        // The authored layer is intentionally broad, so use
                        // the precise legacy mask inside building footprints.
                        // It preserves the stair approach while preventing a
                        // straight shortcut through the building artwork.
                        isWalkable = blockedAreas[areaIndex].PreserveProtectedArea
                            && protectedAreaTexture != null
                            && SampleWalkable(protectedAreaTexture, u, v, false);
                        break;
                    }
                }

                walkable[y * width + x] = isWalkable;
            }
            regions = BuildRegions();
        }

        private bool IsWalkableOverride(Vector2 position)
        {
            if (walkableOverrides == null)
                return false;

            for (var index = 0; index < walkableOverrides.Count; index++)
            {
                if (walkableOverrides[index].Contains(position))
                    return true;
            }

            return false;
        }

        private static IReadOnlyList<NavigationObstacle> ConvertBlockedAreas(
            IReadOnlyList<Rect> blockedAreas)
        {
            if (blockedAreas == null)
                return null;

            var converted = new List<NavigationObstacle>(blockedAreas.Count);
            for (var index = 0; index < blockedAreas.Count; index++)
                converted.Add(new NavigationObstacle(blockedAreas[index]));

            return converted;
        }

        private static bool SampleWalkable(Texture2D texture, float u, float v, bool useAlphaChannel)
        {
            var sample = texture.GetPixelBilinear(u, v);
            return useAlphaChannel ? sample.a >= 0.5f : sample.r >= 0.5f;
        }

        public bool IsWalkable(Vector2 position)
        {
            return TryGetCell(position, out var cell) && walkable[cell];
        }

        public bool TryFindPath(Vector2 start, Vector2 destination, List<Vector2> result)
        {
            return TryFindPathInternal(start, destination, result, false);
        }

        /// <summary>
        /// Finds a route through the walkable region containing <paramref name="start"/>
        /// and ends at the closest reachable walkable cell to the requested destination.
        /// This is used for building entrance markers which are intentionally placed on
        /// top of building art and therefore are not themselves walkable cells.
        /// </summary>
        public bool TryFindPathToNearestReachable(Vector2 start, Vector2 destination, List<Vector2> result)
        {
            return TryFindPathInternal(start, destination, result, true);
        }

        /// <summary>
        /// Finds a route through the walkable region containing <paramref name="start"/>
        /// and ends at the closest walkable cell in that same region. This is used
        /// for visual destination points such as bar stools which are inside the
        /// destination artwork but still need a short final approach from the
        /// current walkable area.
        /// </summary>
        public bool TryFindPathToNearestReachableInStartRegion(
            Vector2 start,
            Vector2 destination,
            List<Vector2> result)
        {
            if (!TrySnap(start, out var startCell))
                return false;

            return TryFindPathInternal(
                start,
                destination,
                result,
                true,
                regions[startCell]);
        }

        private bool TryFindPathInternal(
            Vector2 start,
            Vector2 destination,
            List<Vector2> result,
            bool allowNearestReachable,
            int nearestRegion = -1)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            result.Clear();

            if (!TrySnap(start, out var startCell))
                return false;

            var destinationCell = -1;
            if (!allowNearestReachable)
            {
                if (!TrySnap(destination, out destinationCell)
                    || regions[startCell] != regions[destinationCell])
                    return false;
            }
            else
            {
                if (!TryGetCell(destination, out _))
                    return false;

                // A building marker may sit on non-walkable art. Resolve it to
                // the nearest authored walkable cell, but do not silently use
                // an arbitrary cell from the start region. If the entrance is
                // in another region, the caller must use its configured portal.
                destinationCell = FindNearestWalkableCell(destination, nearestRegion);
                if (destinationCell < 0 || regions[startCell] != regions[destinationCell])
                    return false;

                allowNearestReachable = false;
            }

            var count = width * height;
            var cost = new float[count];
            var previous = new int[count];
            var closed = new bool[count];
            for (var i = 0; i < count; i++)
            {
                cost[i] = float.PositiveInfinity;
                previous[i] = -1;
            }

            var frontier = new MinHeap();
            cost[startCell] = 0f;
            frontier.Push(startCell, destinationCell >= 0
                ? Heuristic(startCell, destinationCell)
                : 0f);

            var bestCell = startCell;
            var bestDistance = (CellCenter(startCell) - destination).sqrMagnitude;
            while (frontier.Count > 0)
            {
                var current = frontier.Pop();
                if (closed[current])
                    continue;
                closed[current] = true;

                if (allowNearestReachable)
                {
                    var distance = (CellCenter(current) - destination).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestCell = current;
                    }
                }
                else if (current == destinationCell)
                {
                    break;
                }

                var x = current % width;
                var y = current / width;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    var nx = x + dx;
                    var ny = y + dy;
                    if (!InBounds(nx, ny) || !walkable[ny * width + nx])
                        continue;
                    // A diagonal must not squeeze between two blocked cells.
                    if (dx != 0 && dy != 0
                        && (!walkable[y * width + nx] || !walkable[ny * width + x]))
                        continue;

                    var next = ny * width + nx;
                    if (SegmentIntersectsBlockedArea(
                        CellCenter(current),
                        CellCenter(next)))
                    {
                        continue;
                    }

                    var nextCost = cost[current] + (dx == 0 || dy == 0 ? 1f : 1.41421356f);
                    if (nextCost >= cost[next])
                        continue;
                    cost[next] = nextCost;
                    previous[next] = current;
                    frontier.Push(next, nextCost + (destinationCell >= 0
                        ? Heuristic(next, destinationCell)
                        : 0f));
                }
            }

            var goalCell = allowNearestReachable ? bestCell : destinationCell;
            if (startCell != goalCell && previous[goalCell] < 0)
                return false;

            var cells = new List<int>();
            for (var cell = goalCell; cell >= 0; cell = previous[cell])
            {
                cells.Add(cell);
                if (cell == startCell)
                    break;
            }
            cells.Reverse();

            // Greedy line of sight removes the grid's stair steps without crossing blocked cells.
            result.Add(CellCenter(startCell));
            for (var from = 0; from < cells.Count - 1;)
            {
                var to = cells.Count - 1;
                while (to > from + 1 && !HasLineOfSight(cells[from], cells[to]))
                    to--;
                result.Add(CellCenter(cells[to]));
                from = to;
            }
            return true;
        }

        private int FindNearestWalkableCell(Vector2 position, int requiredRegion = -1)
        {
            var bestCell = -1;
            var bestDistance = float.PositiveInfinity;
            for (var cell = 0; cell < walkable.Length; cell++)
            {
                if (!walkable[cell] || requiredRegion >= 0 && regions[cell] != requiredRegion)
                    continue;

                var distance = (CellCenter(cell) - position).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestCell = cell;
            }

            return bestCell;
        }

        private bool TrySnap(Vector2 position, out int cell)
        {
            cell = -1;
            if (!TryGetCell(position, out var candidate))
                return false;
            if (walkable[candidate])
            {
                cell = candidate;
                return true;
            }

            var x = candidate % width;
            var y = candidate / width;
            var bestDistance = int.MaxValue;
            // At most 64 island pixels: a misconfigured water/forest target is rejected.
            for (var dy = -8; dy <= 8; dy++)
            for (var dx = -8; dx <= 8; dx++)
            {
                var nx = x + dx;
                var ny = y + dy;
                var distance = dx * dx + dy * dy;
                if (distance >= bestDistance || distance > 64 || !InBounds(nx, ny))
                    continue;
                var next = ny * width + nx;
                if (!walkable[next])
                    continue;
                bestDistance = distance;
                cell = next;
            }
            return cell >= 0;
        }

        private int[] BuildRegions()
        {
            var labels = new int[walkable.Length];
            var queue = new Queue<int>();
            var region = 0;
            for (var seed = 0; seed < walkable.Length; seed++)
            {
                if (!walkable[seed] || labels[seed] != 0)
                    continue;
                labels[seed] = ++region;
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var x = current % width;
                    var y = current / width;
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        var nx = x + dx;
                        var ny = y + dy;
                        if (!InBounds(nx, ny) || !walkable[ny * width + nx])
                            continue;
                        if (dx != 0 && dy != 0
                            && (!walkable[y * width + nx] || !walkable[ny * width + x]))
                            continue;
                        var next = ny * width + nx;
                        if (labels[next] != 0)
                            continue;
                        labels[next] = region;
                        queue.Enqueue(next);
                    }
                }
            }
            return labels;
        }

        private bool TryGetCell(Vector2 position, out int cell)
        {
            cell = -1;
            var u = (position.x - bounds.xMin) / bounds.width;
            var v = (bounds.yMax - position.y) / bounds.height;
            if (u < 0f || u >= 1f || v < 0f || v >= 1f)
                return false;
            var x = Mathf.FloorToInt(u * width);
            var y = Mathf.FloorToInt(v * height);
            cell = y * width + x;
            return true;
        }

        private Vector2 CellCenter(int cell)
        {
            var x = cell % width;
            var y = cell / width;
            return new Vector2(
                bounds.xMin + (x + 0.5f) * bounds.width / width,
                bounds.yMax - (y + 0.5f) * bounds.height / height);
        }

        private bool HasLineOfSight(int from, int to)
        {
            var a = CellCenter(from);
            var b = CellCenter(to);
            if (SegmentIntersectsBlockedArea(a, b))
                return false;

            // Sample well inside each raster cell. A sparse check can jump
            // over a narrow blocked cell and turn a valid grid route into a
            // visual shortcut through a building.
            var steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)
                / (Mathf.Min(bounds.width / width, bounds.height / height) * 0.25f)));
            for (var i = 0; i <= steps; i++)
            {
                if (!TryGetCell(Vector2.Lerp(a, b, (float)i / steps), out var cell)
                    || !walkable[cell])
                    return false;
            }
            return true;
        }

        private bool SegmentIntersectsBlockedArea(Vector2 start, Vector2 end)
        {
            if (blockedAreas == null)
                return false;

            for (var areaIndex = 0; areaIndex < blockedAreas.Count; areaIndex++)
            {
                if (!blockedAreas[areaIndex].PreserveProtectedArea
                    && blockedAreas[areaIndex].IntersectsSegment(start, end))
                    return true;
            }

            return false;
        }

        private bool InBounds(int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        private float Heuristic(int from, int to)
        {
            var dx = Mathf.Abs(from % width - to % width);
            var dy = Mathf.Abs(from / width - to / width);
            return Mathf.Max(dx, dy) + 0.41421356f * Mathf.Min(dx, dy);
        }

        private sealed class MinHeap
        {
            private readonly List<(int Cell, float Priority)> items = new List<(int, float)>();
            public int Count => items.Count;

            public void Push(int cell, float priority)
            {
                items.Add((cell, priority));
                var index = items.Count - 1;
                while (index > 0)
                {
                    var parent = (index - 1) / 2;
                    if (items[parent].Priority <= priority)
                        break;
                    items[index] = items[parent];
                    index = parent;
                }
                items[index] = (cell, priority);
            }

            public int Pop()
            {
                var root = items[0].Cell;
                var last = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count == 0)
                    return root;

                var index = 0;
                while (index * 2 + 1 < items.Count)
                {
                    var child = index * 2 + 1;
                    if (child + 1 < items.Count && items[child + 1].Priority < items[child].Priority)
                        child++;
                    if (last.Priority <= items[child].Priority)
                        break;
                    items[index] = items[child];
                    index = child;
                }
                items[index] = last;
                return root;
            }
        }
    }
}
