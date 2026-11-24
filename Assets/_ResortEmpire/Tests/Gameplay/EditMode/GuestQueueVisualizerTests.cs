using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Waves;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class GuestQueueVisualizerTests
    {
        private const string BubbleFolder = "Assets/_ResortEmpire/Art/World/Island/SatisfactionBubbles/";

        private GameObject bootstrapObject;
        private ResortGameplayBootstrap bootstrap;
        private GameObject islandObject;
        private GameObject containerObject;
        private GameObject touristPrefab;
        private GameObject boatSignalObject;
        private BoatArrivalSignal boatArrivalSignal;
        private Texture2D navigationTexture;
        private TouristVisualSpawner spawner;
        private GuestQueueVisualizer visualizer;
        private MethodInfo step;

        [SetUp]
        public void SetUp()
        {
            ResortDebugLevelOverride.Clear();
            bootstrapObject = new GameObject("GuestQueueBootstrapTest");
            bootstrap = bootstrapObject.AddComponent<ResortGameplayBootstrap>();
            Assert.That(bootstrap.TryConfigure(new QueueDependencies()), Is.True);

            islandObject = new GameObject("IslandContent", typeof(RectTransform));
            var islandBase = new GameObject("IslandBase", typeof(RectTransform)).GetComponent<RectTransform>();
            islandBase.SetParent(islandObject.transform, false);
            islandBase.sizeDelta = new Vector2(1448f, 1086f);
            containerObject = new GameObject("TouristVisuals", typeof(RectTransform));
            containerObject.SetActive(false);
            var container = containerObject.GetComponent<RectTransform>();
            container.SetParent(islandObject.transform, false);

            navigationTexture = new Texture2D(181, 136, TextureFormat.RGBA32, false);
            var pixels = new Color32[181 * 136];
            for (var index = 0; index < pixels.Length; index++)
                pixels[index] = new Color32(255, 255, 255, 255);
            navigationTexture.SetPixels32(pixels);
            navigationTexture.Apply();

            touristPrefab = new GameObject("TouristCharacter", typeof(RectTransform), typeof(Image));
            touristPrefab.GetComponent<RectTransform>().sizeDelta = new Vector2(40f, 48f);
            touristPrefab.SetActive(false);
            boatSignalObject = new GameObject("BoatArrivalSignal");
            boatArrivalSignal = boatSignalObject.AddComponent<BoatArrivalSignal>();

            spawner = containerObject.AddComponent<TouristVisualSpawner>();
            Set(spawner, "gameplayBootstrap", bootstrap);
            Set(spawner, "boatArrivalSignal", boatArrivalSignal);
            Set(spawner, "touristContainer", container);
            Set(spawner, "spawnPoint", CreatePoint(container, "TouristSpawnPoint", new Vector2(100f, -300f)));
            Set(spawner, "islandBase", islandBase);
            Set(spawner, "navigationMapTexture", navigationTexture);
            Set(spawner, "receptionPoint", CreatePoint(container, "TouristReceptionPoint", new Vector2(100f, -200f)));
            Set(spawner, "receptionExitPoint", CreatePoint(container, "TouristReceptionExitPoint", new Vector2(0f, -50f)));
            Set(spawner, "barSeatPoint", CreatePoint(container, "TouristBarSeatPoint", new Vector2(-150f, 0f)));
            Set(spawner, "barSeatOverlay", CreatePoint(container, "TouristBarSeatOverlay", Vector2.zero));
            Set(spawner, "restaurantPoint", CreatePoint(container, "TouristRestaurantPoint", new Vector2(300f, -20f)));
            Set(spawner, "receptionEntryPoint", CreatePoint(container, "ReceptionEntrancePoint", new Vector2(100f, -100f)));
            Set(spawner, "restaurantEntryPoint", CreatePoint(container, "RestaurantEntrancePoint", new Vector2(300f, 0f)));
            Set(spawner, "touristPrefab", touristPrefab);
            Set(spawner, "touristCount", 3);

            visualizer = containerObject.AddComponent<GuestQueueVisualizer>();
            Set(visualizer, "gameplayBootstrap", bootstrap);
            Set(visualizer, "touristSpawner", spawner);
            Set(visualizer, "happyBubble", LoadBubble("guest-bubble-happy.png"));
            Set(visualizer, "waitingBubble", LoadBubble("guest-bubble-waiting.png"));
            Set(visualizer, "unhappyBubble", LoadBubble("guest-bubble-unhappy.png"));
            Set(visualizer, "crowdedBubble", LoadBubble("guest-bubble-crowded.png"));
            containerObject.SetActive(true);
            typeof(TouristVisualSpawner).GetMethod("BindToGameplay", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(spawner, null);
            step = typeof(GuestQueueVisualizer).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(step, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            ResortDebugLevelOverride.Clear();
            // SetUp may have failed part way; release whatever exists so later fixtures start clean.
            if (spawner != null)
                typeof(TouristVisualSpawner).GetMethod("UnbindFromGameplay", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(spawner, null);
            if (visualizer != null)
                typeof(GuestQueueVisualizer).GetMethod("Unbind", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(visualizer, null);
            foreach (var created in new Object[] { islandObject, touristPrefab, boatSignalObject, navigationTexture, bootstrapObject })
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
        }

        [Test]
        public void WaitingGuestsFollowTheServiceQueueWithoutChangingTheSimulation()
        {
            StartWaveWithArrivedBoat();
            bootstrap.AdvanceSimulation(2d);
            var before = bootstrap.CurrentWaveSnapshot;
            var timeBefore = bootstrap.SimulationTime;
            var waiting = GetWaiting(before, BuildingId.Reception);
            Assert.That(waiting, Is.GreaterThan(1), "The test wave must build a reception queue.");

            for (var frame = 0; frame < 15; frame++)
                Step(0.2f);

            Assert.That(visualizer.GetVisibleGuestCount(BuildingId.Reception), Is.EqualTo(Mathf.Min(waiting, 4)));
            Assert.That(visualizer.GetVisibleGuestCount(BuildingId.BeachBar), Is.Zero, "Buildings without a queue show no waiting guests.");
            var after = bootstrap.CurrentWaveSnapshot;
            Assert.That(after.DemandTotal, Is.EqualTo(before.DemandTotal), "Visuals must not add demand.");
            Assert.That(after.DemandArrived, Is.EqualTo(before.DemandArrived));
            Assert.That(after.CompletedUnits, Is.EqualTo(before.CompletedUnits));
            Assert.That(after.WaitingQueueLength, Is.EqualTo(before.WaitingQueueLength));
            Assert.That(bootstrap.SimulationTime, Is.EqualTo(timeBefore), "Visuals must not advance the simulation.");

            // Serve guests: the line shortens to match the simulation.
            bootstrap.AdvanceSimulation(9d);
            var drained = GetWaiting(bootstrap.CurrentWaveSnapshot, BuildingId.Reception);
            Assert.That(drained, Is.LessThan(waiting));
            Step(0.3f);
            Assert.That(visualizer.GetVisibleGuestCount(BuildingId.Reception), Is.EqualTo(Mathf.Min(drained, 4)));
        }

        [Test]
        public void QueueSlotsAreDistinctAndShowNegativeFeedbackForWaitingGuests()
        {
            StartWaveWithArrivedBoat();
            bootstrap.AdvanceSimulation(2d);

            var kinds = new List<GuestBubbleKind>();
            var sawNegative = false;
            for (var frame = 0; frame < 20 && !sawNegative; frame++)
            {
                Step(0.2f);
                visualizer.GetActiveBubbleKinds(kinds);
                sawNegative = kinds.Contains(GuestBubbleKind.Waiting)
                    || kinds.Contains(GuestBubbleKind.Unhappy)
                    || kinds.Contains(GuestBubbleKind.Crowded);
            }

            Assert.That(sawNegative, Is.True, "Waiting guests must show negative feedback.");
            var slots = visualizer.GetQueueSlots(BuildingId.Reception);
            Assert.That(slots.Count, Is.GreaterThanOrEqualTo(visualizer.GetVisibleGuestCount(BuildingId.Reception)));
            for (var first = 0; first < slots.Count; first++)
                for (var second = first + 1; second < slots.Count; second++)
                    Assert.That(Vector2.Distance(slots[first], slots[second]), Is.GreaterThan(20f), "Waiting guests must not stand on each other.");
        }

        [Test]
        public void WaitingGuestsAndBubblesDisappearWhenTheWaveEnds()
        {
            StartWaveWithArrivedBoat();
            bootstrap.AdvanceSimulation(2d);
            Step(0.3f);
            Assert.That(visualizer.GetVisibleGuestCount(BuildingId.Reception), Is.GreaterThan(0));

            bootstrap.AdvanceSimulation(100d);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            Step(0.3f);

            var kinds = new List<GuestBubbleKind>();
            visualizer.GetActiveBubbleKinds(kinds);
            Assert.That(visualizer.GetVisibleGuestCount(BuildingId.Reception), Is.Zero);
            Assert.That(kinds, Is.Empty);
        }

        [Test]
        public void AuthoredQueueLinesPlaceGuestsFrontFirstAndSkipBuildingArt()
        {
            var container = containerObject.GetComponent<RectTransform>();
            SetQueueLine(BuildingId.Reception, CreateLine(container, new Vector2(-200f, 100f), new Vector2(-200f, -60f)));
            // Building art over y 85..125: the guest's head reaches about 22 units above its centre,
            // so the first clear position is roughly 40 units down the line (within the front gap).
            SetBuildingArt(CreateArt(container, new Vector2(-200f, 105f), new Vector2(60f, 40f)));

            StartWaveWithArrivedBoat();
            bootstrap.AdvanceSimulation(2d);
            Step(0.3f);

            var slots = visualizer.GetQueueSlots(BuildingId.Reception);
            Assert.That(slots.Count, Is.EqualTo(4));
            Assert.That(slots[0].x, Is.EqualTo(-200f).Within(0.01f), "Guests stand on the authored line, not the automatic route.");
            Assert.That(slots[0].y, Is.InRange(50f, 64f), "The front guest steps back just far enough to clear the building art.");
            for (var index = 1; index < slots.Count; index++)
                Assert.That(slots[index].y, Is.LessThan(slots[index - 1].y), "The line fills from its first point backwards.");
        }

        [Test]
        public void ALineWhoseStartIsBlockedShowsNoGuestsInsteadOfMovingTheQueueAway()
        {
            var container = containerObject.GetComponent<RectTransform>();
            SetQueueLine(BuildingId.Reception, CreateLine(container, new Vector2(-200f, 100f), new Vector2(-200f, -200f)));
            SetBuildingArt(CreateArt(container, new Vector2(-200f, 40f), new Vector2(60f, 160f)));

            StartWaveWithArrivedBoat();
            bootstrap.AdvanceSimulation(2d);
            Step(0.3f);

            Assert.That(GetWaiting(bootstrap.CurrentWaveSnapshot, BuildingId.Reception), Is.GreaterThan(0));
            Assert.That(visualizer.GetQueueSlots(BuildingId.Reception), Is.Empty);
            Assert.That(visualizer.GetVisibleGuestCount(BuildingId.Reception), Is.Zero);
        }

        [Test]
        public void ResortSceneWiresQueueFeedbackToTheTouristVisualsAndBuildingArt()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/_ResortEmpire/Scenes/Resort.unity");
            try
            {
                GuestQueueVisualizer sceneVisualizer = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    sceneVisualizer = root.GetComponentInChildren<GuestQueueVisualizer>(true);
                    if (sceneVisualizer != null)
                        break;
                }

                Assert.That(sceneVisualizer, Is.Not.Null);
                Assert.That(sceneVisualizer.GetComponent<TouristVisualSpawner>(), Is.Not.Null,
                    "Queue guests share the roaming tourists' container and render layer.");
                var serialized = new SerializedObject(sceneVisualizer);
                foreach (var field in new[] { "gameplayBootstrap", "touristSpawner", "happyBubble", "waitingBubble", "unhappyBubble", "crowdedBubble" })
                    Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);

                var art = serialized.FindProperty("buildingArt");
                var names = new List<string>();
                for (var index = 0; index < art.arraySize; index++)
                    names.Add(art.GetArrayElementAtIndex(index).objectReferenceValue.name);
                Assert.That(names, Is.EquivalentTo(new[] { "Reception", "RestaurantSprite", "BeachBarSprite", "PoolLockedSprite", "BungalowsLockedSprite" }));

                var lines = serialized.FindProperty("queueLines");
                var authored = new List<BuildingId>();
                for (var index = 0; index < lines.arraySize; index++)
                {
                    var line = lines.GetArrayElementAtIndex(index);
                    var building = (BuildingId)line.FindPropertyRelative("Building").enumValueIndex;
                    var points = (RectTransform)line.FindPropertyRelative("Points").objectReferenceValue;
                    Assert.That(points, Is.Not.Null, building.ToString());
                    var route = new List<Vector2>();
                    foreach (RectTransform point in points)
                        route.Add(point.anchoredPosition);
                    Assert.That(GuestQueueLayout.GetLength(route), Is.GreaterThanOrEqualTo(90f), building + " must fit four guests 30 units apart.");
                    authored.Add(building);
                }

                Assert.That(authored, Is.EquivalentTo(new[] { BuildingId.Reception, BuildingId.Restaurant, BuildingId.BeachBar, BuildingId.Pool, BuildingId.Bungalows }));
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private void StartWaveWithArrivedBoat()
        {
            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            bootstrap.AdvanceSimulation(0.01d);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_ACTIVE));
            boatArrivalSignal.NotifyArrived();
            Assert.That(spawner.ActiveTouristCount, Is.GreaterThan(0));
        }

        private void Step(float deltaTime)
        {
            step.Invoke(visualizer, new object[] { deltaTime });
        }

        private static int GetWaiting(WaveSnapshot snapshot, BuildingId buildingId)
        {
            foreach (var need in snapshot.Needs)
            {
                if (need.BuildingId == buildingId)
                    return need.WaitingQueueLength;
            }

            return 0;
        }

        private static RectTransform CreateLine(RectTransform parent, Vector2 front, Vector2 back)
        {
            var line = CreatePoint(parent, "ReceptionQueue", Vector2.zero);
            CreatePoint(line, "01", front);
            CreatePoint(line, "02", back);
            return line;
        }

        private static Image CreateArt(RectTransform parent, Vector2 centre, Vector2 size)
        {
            // Bubble sprites import as full-rect meshes, so this art blocks exactly its rect.
            var art = new GameObject("BuildingArt", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            var rect = art.rectTransform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = centre;
            rect.sizeDelta = size;
            art.sprite = LoadBubble("guest-bubble-happy.png");
            return art;
        }

        private void SetBuildingArt(Image art)
        {
            Set(visualizer, "buildingArt", new[] { art });
        }

        private void SetQueueLine(BuildingId buildingId, RectTransform points)
        {
            var lineType = typeof(GuestQueueVisualizer).GetNestedType("QueueLine", BindingFlags.NonPublic);
            var line = System.Activator.CreateInstance(lineType);
            lineType.GetField("Building").SetValue(line, buildingId);
            lineType.GetField("Points").SetValue(line, points);
            var lines = System.Array.CreateInstance(lineType, 1);
            lines.SetValue(line, 0);
            Set(visualizer, "queueLines", lines);
        }

        private static Sprite LoadBubble(string fileName)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BubbleFolder + fileName);
            Assert.That(sprite, Is.Not.Null, fileName);
            return sprite;
        }

        private static RectTransform CreatePoint(RectTransform parent, string name, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            return rect;
        }

        private static void Set(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private sealed class QueueDependencies : IResortGameplayDependencySource
        {
            // One reception seat with a slow service makes arrivals queue up.
            public IBuildingSnapshotProvider BuildingProvider { get; } = new TestBuildingProvider(capacity: 1, serviceDuration: 4d);
            public IWaveDefinitionProvider WaveDefinitionProvider { get; } = new QueueWaveProvider();
            public WaveSimulationConfig SimulationConfig { get; } = new WaveSimulationConfig(0d, 1d, 0.5d);
            // The Prototype leaves exactly one owned building (Beach Bar) unstaffed.
            public IReadOnlyList<BuildingId> InitialWorkerAssignments { get; } = new[] { BuildingId.Reception, BuildingId.Restaurant };
        }

        private sealed class QueueWaveProvider : IWaveDefinitionProvider
        {
            public WaveDefinition GetNextWave()
            {
                return new WaveDefinition(1, 2d, new[] { new NeedDemandDefinition(NeedType.Rooms, 5) });
            }
        }
    }
}
