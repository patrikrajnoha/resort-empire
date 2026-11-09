using System.Collections;
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
    public sealed class TouristVisualSpawnerTests
    {
        private GameObject bootstrapObject;
        private ResortGameplayBootstrap bootstrap;
        private GameObject spawnerObject;
        private TouristVisualSpawner spawner;
        private GameObject containerObject;
        private RectTransform spawnPoint;
        private RectTransform barSeatOverlay;
        private GameObject touristPrefab;
        private GameObject boatSignalObject;
        private BoatArrivalSignal boatArrivalSignal;
        private GameObject islandBaseObject;
        private Texture2D navigationTexture;

        [SetUp]
        public void SetUp()
        {
            bootstrapObject = new GameObject("TouristVisualBootstrapTest");
            bootstrap = bootstrapObject.AddComponent<ResortGameplayBootstrap>();
            Assert.That(bootstrap.TryConfigure(new TestDependencies()), Is.True);

            containerObject = new GameObject("TouristContainer");
            containerObject.AddComponent<RectTransform>();
            var spawnPointObject = new GameObject("TouristSpawnPoint");
            spawnPoint = spawnPointObject.AddComponent<RectTransform>();
            spawnPoint.SetParent(containerObject.transform, false);
            spawnPoint.anchoredPosition = new Vector2(123f, -45f);

            touristPrefab = new GameObject("TouristCharacter");
            touristPrefab.AddComponent<RectTransform>();
            var image = touristPrefab.AddComponent<Image>();
            image.raycastTarget = true;
            touristPrefab.SetActive(false);

            boatSignalObject = new GameObject("BoatArrivalSignal");
            boatArrivalSignal = boatSignalObject.AddComponent<BoatArrivalSignal>();

            islandBaseObject = new GameObject("IslandBase");
            var islandBase = islandBaseObject.AddComponent<RectTransform>();
            islandBase.sizeDelta = new Vector2(1448f, 1086f);
            navigationTexture = new Texture2D(181, 136, TextureFormat.RGBA32, false);
            var pixels = new Color32[181 * 136];
            for (var index = 0; index < pixels.Length; index++)
                pixels[index] = new Color32(255, 255, 255, 255);
            navigationTexture.SetPixels32(pixels);
            navigationTexture.Apply();

            var reception = CreateDestination("TouristReceptionPoint", new Vector2(200f, -50f));
            var receptionExit = CreateDestination("TouristReceptionExitPoint", new Vector2(100f, 20f));
            var bar = CreateDestination("TouristBarSeatPoint", new Vector2(250f, 10f));
            var restaurant = CreateDestination("TouristRestaurantPoint", new Vector2(300f, -20f));
            var barSeatOverlayObject = new GameObject("TouristBarSeatOverlay");
            barSeatOverlay = barSeatOverlayObject.AddComponent<RectTransform>();
            barSeatOverlay.SetParent(containerObject.transform, false);

            spawnerObject = new GameObject("TouristVisualSpawnerTest");
            spawnerObject.SetActive(false);
            spawner = spawnerObject.AddComponent<TouristVisualSpawner>();
            SetPrivateField(spawner, "gameplayBootstrap", bootstrap);
            SetPrivateField(spawner, "boatArrivalSignal", boatArrivalSignal);
            SetPrivateField(spawner, "touristContainer", containerObject.GetComponent<RectTransform>());
            SetPrivateField(spawner, "spawnPoint", spawnPoint);
            SetPrivateField(spawner, "islandBase", islandBase);
            SetPrivateField(spawner, "navigationMapTexture", navigationTexture);
            SetPrivateField(spawner, "receptionPoint", reception);
            SetPrivateField(spawner, "receptionExitPoint", receptionExit);
            SetPrivateField(spawner, "barSeatPoint", bar);
            SetPrivateField(spawner, "barSeatOverlay", barSeatOverlay);
            SetPrivateField(spawner, "restaurantPoint", restaurant);
            SetPrivateField(spawner, "touristPrefab", touristPrefab);
            SetPrivateField(spawner, "touristCount", 5);
            spawnerObject.SetActive(true);
            typeof(TouristVisualSpawner).GetMethod(
                "BindToGameplay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(spawner, null);
        }

        [TearDown]
        public void TearDown()
        {
            typeof(TouristVisualSpawner).GetMethod(
                "UnbindFromGameplay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(spawner, null);
            Object.DestroyImmediate(spawnerObject);
            Object.DestroyImmediate(touristPrefab);
            Object.DestroyImmediate(containerObject);
            Object.DestroyImmediate(boatSignalObject);
            Object.DestroyImmediate(islandBaseObject);
            Object.DestroyImmediate(navigationTexture);
            Object.DestroyImmediate(bootstrapObject);
        }

        [Test]
        public void WaveLifecycleShowsTouristsDuringActiveAndPausedPhasesThenHidesThem()
        {
            Assert.That(spawner.ActiveTouristCount, Is.Zero);

            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True, "Starting the visual test wave failed.");
            Assert.That(spawner.ActiveTouristCount, Is.Zero, "Tourists wait for the boat to reach the dock.");
            bootstrap.AdvanceSimulation(1d);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_ACTIVE));
            Assert.That(spawner.ActiveTouristCount, Is.Zero, "Tourists wait for the arrival signal.");
            boatArrivalSignal.NotifyArrived();
            Assert.That(spawner.ActiveTouristCount, Is.EqualTo(5), "No tourists appeared after boat arrival.");
            Assert.That(containerObject.transform.childCount, Is.EqualTo(11));
            Assert.That(bootstrap.TryPauseWave().Succeeded, Is.True, "Pausing the visual test wave failed.");
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_PAUSED));
            Assert.That(spawner.ActiveTouristCount, Is.EqualTo(5), "Tourists disappeared when the wave was paused.");

            Assert.That(bootstrap.TryResumeWave().Succeeded, Is.True, "Resuming the visual test wave failed.");
            bootstrap.AdvanceSimulation(100d);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            Assert.That(spawner.ActiveTouristCount, Is.Zero);
            Assert.That(containerObject.transform.childCount, Is.EqualTo(11), "The pool is reused instead of recreated.");
        }

        [Test]
        public void RepeatedActivePhaseNotificationsDoNotDuplicateTouristsAndImagesDoNotInterceptRaycasts()
        {
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(stateChanged, Is.Not.Null);

            boatArrivalSignal.NotifyArrived();
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });
            stateChanged.Invoke(spawner, new object[] { GamePhase.WAVE_INCOMING, GamePhase.WAVE_ACTIVE });

            Assert.That(spawner.ActiveTouristCount, Is.EqualTo(5));
            Assert.That(containerObject.transform.childCount, Is.EqualTo(11));
            foreach (var image in containerObject.GetComponentsInChildren<Image>(true))
                Assert.That(image.raycastTarget, Is.False);
        }

        [Test]
        public void TouristsStartSpacedAroundConfiguredSpawnPoint()
        {
            boatArrivalSignal.NotifyArrived();
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            var tourists = containerObject.GetComponentsInChildren<Image>(true);
            Assert.That(tourists, Has.Length.EqualTo(5));
            var expectedPositions = new[]
            {
                spawnPoint.anchoredPosition + new Vector2(-18f, 0f),
                spawnPoint.anchoredPosition,
                spawnPoint.anchoredPosition + new Vector2(18f, 0f),
                spawnPoint.anchoredPosition + new Vector2(-9f, -12f),
                spawnPoint.anchoredPosition + new Vector2(9f, -12f)
            };

            for (var index = 0; index < tourists.Length; index++)
                Assert.That(Vector2.Distance(tourists[index].rectTransform.anchoredPosition,
                    expectedPositions[index]), Is.LessThan(7f));
        }

        [Test]
        public void TouristsHaveMovementAwayFromSpawnAfterArrival()
        {
            boatArrivalSignal.NotifyArrived();
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            var routine = routines[0];
            var move = typeof(TouristVisualSpawner).GetMethod(
                "MoveAlongRoutine", BindingFlags.Instance | BindingFlags.NonPublic);
            var tourist = (GameObject)GetPrivateField(routine, "Tourist");
            var startPosition = tourist.GetComponent<RectTransform>().anchoredPosition;

            SetPrivateField(routine, "StartDelayRemaining", 0f);
            move.Invoke(spawner, new[] { routine, (object)1f });

            Assert.That(Vector2.Distance(tourist.GetComponent<RectTransform>().anchoredPosition,
                startPosition), Is.GreaterThan(1f), "Tourist remained at its spawn point instead of following its route.");
        }

        [Test]
        public void TouristAdvancesToNextDestinationInsteadOfCompleting()
        {
            boatArrivalSignal.NotifyArrived();
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            var routinesField = typeof(TouristVisualSpawner).GetField(
                "touristRoutines", BindingFlags.Instance | BindingFlags.NonPublic);
            var routines = (IList)routinesField.GetValue(spawner);
            var barRoutine = routines[1];
            SetPrivateField(barRoutine, "AtDestination", true);
            SetPrivateField(barRoutine, "DwellRemaining", 0f);

            var tourist = (GameObject)GetPrivateField(barRoutine, "Tourist");
            var animate = typeof(TouristVisualSpawner).GetMethod(
                "AnimateTourists", BindingFlags.Instance | BindingFlags.NonPublic);
            animate.Invoke(spawner, null);

            Assert.That(GetPrivateField(barRoutine, "Completed"), Is.EqualTo(false));
            Assert.That(GetPrivateField(barRoutine, "RoutineIndex"), Is.EqualTo(2));
            Assert.That(GetPrivateField(barRoutine, "AtDestination"), Is.EqualTo(false));
            Assert.That(tourist.gameObject.activeSelf, Is.True);
            Assert.That(((IList)GetPrivateField(barRoutine, "Route")).Count, Is.GreaterThan(1));
        }

        [Test]
        public void BarGuestsAnimateWhileTalking()
        {
            boatArrivalSignal.NotifyArrived();
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            var barRoutine = routines[1];
            var enter = typeof(TouristVisualSpawner).GetMethod(
                "EnterDestination", BindingFlags.Instance | BindingFlags.NonPublic);
            var animateConversation = typeof(TouristVisualSpawner).GetMethod(
                "AnimateBarConversation", BindingFlags.Instance | BindingFlags.NonPublic);
            var tourist = (GameObject)GetPrivateField(barRoutine, "Tourist");

            enter.Invoke(spawner, new[] { barRoutine });
            var before = tourist.GetComponent<RectTransform>().anchoredPosition;
            animateConversation.Invoke(spawner, new[] { barRoutine, (object)0.25f });
            var afterFirstStep = tourist.GetComponent<RectTransform>().anchoredPosition;
            animateConversation.Invoke(spawner, new[] { barRoutine, (object)0.25f });
            var afterSecondStep = tourist.GetComponent<RectTransform>().anchoredPosition;

            Assert.That(Vector2.Distance(afterFirstStep, before), Is.GreaterThan(0.1f));
            Assert.That(Vector2.Distance(afterSecondStep, afterFirstStep), Is.GreaterThan(0.1f));
        }

        [Test]
        public void BuildingGuestsEnterBuildingsAndBarGuestsOccupySeparateStools()
        {
            boatArrivalSignal.NotifyArrived();
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            var enter = typeof(TouristVisualSpawner).GetMethod(
                "EnterDestination", BindingFlags.Instance | BindingFlags.NonPublic);
            var complete = typeof(TouristVisualSpawner).GetMethod(
                "CompleteRoutine", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var routine in routines)
            {
                enter.Invoke(spawner, new[] { routine });
                var tourist = (GameObject)GetPrivateField(routine, "Tourist");
                var routineIndex = (int)GetPrivateField(routine, "RoutineIndex");
                Assert.That(tourist.activeSelf, Is.EqualTo(routineIndex == 1),
                    "Only Bar guests should remain visible after reaching their destination.");
                complete.Invoke(spawner, new[] { routine });
                Assert.That(tourist.activeSelf, Is.EqualTo(routineIndex == 1),
                    "Reception and Restaurant guests should stay hidden inside the building.");
            }

            var firstBarGuest = (GameObject)GetPrivateField(routines[1], "Tourist");
            var secondBarGuest = (GameObject)GetPrivateField(routines[4], "Tourist");
            Assert.That(Vector2.Distance(firstBarGuest.GetComponent<RectTransform>().anchoredPosition,
                new Vector2(228.63f, 130.99f)), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(secondBarGuest.GetComponent<RectTransform>().anchoredPosition,
                new Vector2(197.54f, 146.11f)), Is.LessThan(0.01f));
            Assert.That(firstBarGuest.GetComponent<RectTransform>().localScale.x, Is.LessThan(0f));
            Assert.That(secondBarGuest.GetComponent<RectTransform>().localScale.x, Is.GreaterThan(0f));
        }

        [Test]
        public void BarGuestDisappearsAtReceptionAndReappearsOnInnerPath()
        {
            var realMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/_ResortEmpire/Art/Island/Navigation/island-walkable.png");
            Assert.That(realMap, Is.Not.Null);
            SetPrivateField(spawner, "navigationMapTexture", realMap);
            spawnPoint.anchoredPosition = new Vector2(102f, -311f);
            ((RectTransform)GetPrivateField(spawner, "receptionPoint")).anchoredPosition =
                new Vector2(76f, -265f);
            ((RectTransform)GetPrivateField(spawner, "receptionExitPoint")).anchoredPosition =
                new Vector2(-74f, -37f);
            ((RectTransform)GetPrivateField(spawner, "barSeatPoint")).anchoredPosition =
                new Vector2(26f, -25f);
            ((RectTransform)GetPrivateField(spawner, "restaurantPoint")).anchoredPosition =
                new Vector2(356f, -77f);

            boatArrivalSignal.NotifyArrived();
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            Assert.That(routines.Count, Is.EqualTo(5));
            var barRoutine = routines[1];
            var portalEntry = (int)GetPrivateField(barRoutine, "PortalEntryRouteIndex");
            var portalExit = (int)GetPrivateField(barRoutine, "PortalExitRouteIndex");
            Assert.That(portalEntry, Is.GreaterThan(0));
            Assert.That(portalExit, Is.EqualTo(portalEntry + 1));

            var route = (IList)GetPrivateField(barRoutine, "Route");
            var tourist = (GameObject)GetPrivateField(barRoutine, "Tourist");
            SetPrivateField(barRoutine, "StartDelayRemaining", 0f);
            SetPrivateField(barRoutine, "RouteIndex", portalEntry);
            SetPrivateField(barRoutine, "PathPosition", (Vector2)route[portalEntry]);
            var move = typeof(TouristVisualSpawner).GetMethod(
                "MoveAlongRoutine", BindingFlags.Instance | BindingFlags.NonPublic);
            move.Invoke(spawner, new[] { barRoutine, (object)0.1f });
            Assert.That(tourist.activeSelf, Is.False);

            move.Invoke(spawner, new[] { barRoutine, (object)1f });
            Assert.That(tourist.activeSelf, Is.True);
            Assert.That(tourist.GetComponent<RectTransform>().anchoredPosition,
                Is.EqualTo((Vector2)route[portalExit]));
        }

        [Test]
        public void RestaurantGuestUsesReceptionPortalAndAllConfiguredTouristsRemainActive()
        {
            var realMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/_ResortEmpire/Art/Island/Navigation/island-walkable.png");
            Assert.That(realMap, Is.Not.Null);
            SetPrivateField(spawner, "navigationMapTexture", realMap);
            spawnPoint.anchoredPosition = new Vector2(102f, -311f);
            ((RectTransform)GetPrivateField(spawner, "receptionPoint")).anchoredPosition =
                new Vector2(76f, -265f);
            ((RectTransform)GetPrivateField(spawner, "receptionExitPoint")).anchoredPosition =
                new Vector2(-74f, -37f);
            ((RectTransform)GetPrivateField(spawner, "barSeatPoint")).anchoredPosition =
                new Vector2(26f, -25f);
            ((RectTransform)GetPrivateField(spawner, "restaurantPoint")).anchoredPosition =
                new Vector2(356f, -77f);

            boatArrivalSignal.NotifyArrived();
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            Assert.That(spawner.ActiveTouristCount, Is.EqualTo(5),
                "A disconnected restaurant region must not skip a configured tourist when the reception portal is valid.");

            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            var restaurantRoutine = routines[2];
            var portalEntry = (int)GetPrivateField(restaurantRoutine, "PortalEntryRouteIndex");
            var portalExit = (int)GetPrivateField(restaurantRoutine, "PortalExitRouteIndex");
            Assert.That(portalEntry, Is.GreaterThan(0));
            Assert.That(portalExit, Is.EqualTo(portalEntry + 1));
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private RectTransform CreateDestination(string name, Vector2 position)
        {
            var gameObject = new GameObject(name);
            var rect = gameObject.AddComponent<RectTransform>();
            rect.SetParent(containerObject.transform, false);
            rect.anchoredPosition = position;
            return rect;
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, fieldName);
            return field.GetValue(target);
        }

        private sealed class TestDependencies : IResortGameplayDependencySource
        {
            public IBuildingSnapshotProvider BuildingProvider { get; } = new TestBuildingProvider();
            public IWaveDefinitionProvider WaveDefinitionProvider { get; } = new TestWaveDefinitionProvider();
            public WaveSimulationConfig SimulationConfig { get; } = new WaveSimulationConfig(0d, 1d, 0.5d);
            public IReadOnlyList<BuildingId> InitialWorkerAssignments { get; } =
                new[] { BuildingId.Reception, BuildingId.Restaurant };
        }

        private sealed class TestWaveDefinitionProvider : IWaveDefinitionProvider
        {
            public WaveDefinition GetNextWave()
            {
                return new WaveDefinition(
                    1,
                    10d,
                    new[]
                    {
                        new NeedDemandDefinition(NeedType.Rooms, 1),
                        new NeedDemandDefinition(NeedType.Food, 1),
                        new NeedDemandDefinition(NeedType.Drinks, 1)
                    });
            }
        }
    }
}
