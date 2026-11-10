using System.Collections.Generic;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using UnityEditor;
using UnityEngine;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class IslandNavigationMapTests
    {
        private const string MapPath =
            "Assets/_ResortEmpire/Art/Island/Navigation/island-walkable.png";
        private const string WalkablePathLayerPath =
            "Assets/_ResortEmpire/Art/Island/Navigation/island-walkable-path-layer.png";
        private static readonly Rect IslandBounds = new Rect(-724f, -543f, 1448f, 1086f);

        [Test]
        public void DockAndReceptionExitReachTheirServicesWithoutCrossingObstacles()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);
            Assert.That(texture, Is.Not.Null);
            var map = new IslandNavigationMap(texture, IslandBounds);
            var dock = PixelToLocal(826f, 854f);
            var receptionExit = PixelToLocal(650f, 580f);
            var routes = new[]
            {
                (dock, PixelToLocal(800f, 808f)),          // Reception entrance
                (receptionExit, PixelToLocal(750f, 568f)), // Beach Bar entrance
                (receptionExit, PixelToLocal(1204f, 643f)) // Restaurant stair foot
            };

            foreach (var (start, destination) in routes)
            {
                var path = new List<Vector2>();
                Assert.That(map.TryFindPath(start, destination, path), Is.True);
                Assert.That(path.Count, Is.GreaterThan(1));
                Assert.That(Vector2.Distance(path[0], start), Is.LessThan(12f));
                Assert.That(Vector2.Distance(path[path.Count - 1], destination), Is.LessThan(16f));

                for (var segment = 1; segment < path.Count; segment++)
                {
                    var a = path[segment - 1];
                    var b = path[segment];
                    var samples = Mathf.CeilToInt(Vector2.Distance(a, b) / 2f);
                    for (var sample = 0; sample <= samples; sample++)
                        Assert.That(map.IsWalkable(Vector2.Lerp(a, b, (float)sample / samples)),
                            Is.True, "A route segment crossed water, a building plot, or an obstacle.");
                }
            }

            var restaurantStairFoot = PixelToLocal(1204f, 643f);
            Assert.That(map.IsWalkable(PixelToLocal(1224f, 573f)), Is.False,
                "The Restaurant stair artwork must not remain a walkable navigation surface.");
            Assert.That(map.IsWalkable(restaurantStairFoot), Is.True,
                "The walkable Restaurant stair-foot approach must remain connected.");

            var restaurantRoute = new List<Vector2>();
            Assert.That(map.TryFindPath(receptionExit, restaurantStairFoot, restaurantRoute), Is.True);
            Assert.That(restaurantRoute.Exists(point => point.y <= -75f), Is.True,
                "The Restaurant route must descend into the front approach corridor before the stairs.");
            var stairObstacle = new Rect(456f, -80f, 88f, 80f);
            for (var segment = 1; segment < restaurantRoute.Count; segment++)
            {
                var a = restaurantRoute[segment - 1];
                var b = restaurantRoute[segment];
                var samples = Mathf.CeilToInt(Vector2.Distance(a, b) / 2f);
                for (var sample = 0; sample <= samples; sample++)
                    Assert.That(stairObstacle.Contains(Vector2.Lerp(a, b, (float)sample / samples)), Is.False,
                        "The Restaurant route entered the lower-right staircase footprint.");
            }

            var impossibleShortcut = new List<Vector2>();
            Assert.That(map.TryFindPath(dock, PixelToLocal(750f, 568f), impossibleShortcut),
                Is.False, "The shore rocks separate the dock from the inner paths.");
        }

        [Test]
        public void WaterAndBuildingPlotsAreNotWalkable()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);
            Assert.That(texture, Is.Not.Null);
            var map = new IslandNavigationMap(texture, IslandBounds);

            Assert.That(map.IsWalkable(PixelToLocal(100f, 300f)), Is.False);  // Water
            Assert.That(map.IsWalkable(PixelToLocal(800f, 660f)), Is.False);  // Reception plot
            Assert.That(map.IsWalkable(PixelToLocal(760f, 440f)), Is.False);  // Bar plot
            Assert.That(map.IsWalkable(PixelToLocal(1050f, 350f)), Is.False); // Open build plot
            Assert.That(map.IsWalkable(PixelToLocal(1135f, 794f)), Is.False); // Shore boulder
        }

        [Test]
        public void AuthoringWalkablePathLayerConnectsBoatToBothBuildingEntries()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(WalkablePathLayerPath);
            Assert.That(texture, Is.Not.Null);
            var map = new IslandNavigationMap(texture, IslandBounds, true, 181, 136);
            var spawn = new Vector2(102f, -311f);
            var receptionEntry = new Vector2(55f, -169f);
            var restaurantEntry = new Vector2(481f, -32f);

            foreach (var destination in new[] { receptionEntry, restaurantEntry })
            {
                var path = new List<Vector2>();
                Assert.That(map.TryFindPath(spawn, destination, path), Is.True);
                Assert.That(path.Count, Is.GreaterThan(1));
                for (var segment = 1; segment < path.Count; segment++)
                {
                    var a = path[segment - 1];
                    var b = path[segment];
                    var samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 2f));
                    for (var sample = 0; sample <= samples; sample++)
                    {
                        var point = Vector2.Lerp(a, b, (float)sample / samples);
                        Assert.That(
                            map.IsWalkable(point),
                            Is.True,
                            $"Route to {destination} crossed a non-walkable cell at segment {segment}, sample {sample}: {point} from {a} to {b}.");
                    }
                }
            }
        }

        [Test]
        public void AuthoringWalkablePathLayerCanExcludeBuildingFootprints()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(WalkablePathLayerPath);
            Assert.That(texture, Is.Not.Null);
            var blockedBuilding = new Rect(300f, -100f, 260f, 280f);
            var map = new IslandNavigationMap(texture, IslandBounds, true, 181, 136,
                new[] { blockedBuilding });

            Assert.That(map.IsWalkable(new Vector2(400f, 0f)), Is.False);
            Assert.That(map.IsWalkable(new Vector2(250f, 0f)), Is.True);
        }

        private static Vector2 PixelToLocal(float x, float y)
        {
            return new Vector2(x - 724f, 543f - y);
        }
    }
}
