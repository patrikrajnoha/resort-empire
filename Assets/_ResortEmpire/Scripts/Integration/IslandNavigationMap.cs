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

        public IslandNavigationMap(Texture2D texture, Rect bounds)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));
            if (bounds.width <= 0f || bounds.height <= 0f)
                throw new ArgumentException("The island bounds must have positive dimensions.", nameof(bounds));

            width = texture.width;
            height = texture.height;
            this.bounds = bounds;
            var pixels = texture.GetPixels32();
            walkable = new bool[pixels.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                walkable[y * width + x] = pixels[(height - 1 - y) * width + x].r >= 128;
            regions = BuildRegions();
        }

        public bool IsWalkable(Vector2 position)
        {
            return TryGetCell(position, out var cell) && walkable[cell];
        }

        public bool TryFindPath(Vector2 start, Vector2 destination, List<Vector2> result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            result.Clear();

            if (!TrySnap(start, out var startCell) || !TrySnap(destination, out var goalCell))
                return false;
            if (regions[startCell] != regions[goalCell])
                return false;

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
            frontier.Push(startCell, Heuristic(startCell, goalCell));

            while (frontier.Count > 0)
            {
                var current = frontier.Pop();
                if (closed[current])
                    continue;
                if (current == goalCell)
                    break;
                closed[current] = true;

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
                    var nextCost = cost[current] + (dx == 0 || dy == 0 ? 1f : 1.41421356f);
                    if (nextCost >= cost[next])
                        continue;
                    cost[next] = nextCost;
                    previous[next] = current;
                    frontier.Push(next, nextCost + Heuristic(next, goalCell));
                }
            }

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
            var steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 2f
                / Mathf.Min(bounds.width / width, bounds.height / height));
            for (var i = 0; i <= steps; i++)
            {
                if (!TryGetCell(Vector2.Lerp(a, b, (float)i / steps), out var cell)
                    || !walkable[cell])
                    return false;
            }
            return true;
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
