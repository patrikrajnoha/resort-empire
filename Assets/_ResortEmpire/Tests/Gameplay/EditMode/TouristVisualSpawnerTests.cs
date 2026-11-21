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
        private RectTransform receptionEntryPoint;
        private RectTransform restaurantEntryPoint;
        private RectTransform barSeatOverlay;
        private GameObject touristPrefab;
        private GameObject boatSignalObject;
        private BoatArrivalSignal boatArrivalSignal;
        private GameObject islandBaseObject;
        private Texture2D navigationTexture;

        [SetUp]
        public void SetUp()
        {
            ResortDebugLevelOverride.Clear();
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
            receptionEntryPoint = CreateDestination("ReceptionEntrancePoint", new Vector2(190f, -45f));
            restaurantEntryPoint = CreateDestination("RestaurantEntrancePoint", new Vector2(480f, -100f));
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
            SetPrivateField(spawner, "receptionEntryPoint", receptionEntryPoint);
            SetPrivateField(spawner, "restaurantEntryPoint", restaurantEntryPoint);
            SetPrivateField(spawner, "touristPrefab", touristPrefab);
            SetPrivateField(spawner, "touristCount", 5);
            spawnerObject.SetActive(true);
            typeof(TouristVisualSpawner).GetMethod(
                "BindToGameplay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(spawner, null);
        }

        [TearDown]
        public void TearDown()
        {
            ResortDebugLevelOverride.Clear();
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
            Assert.That(containerObject.transform.childCount, Is.EqualTo(13));
            Assert.That(bootstrap.TryPauseWave().Succeeded, Is.True, "Pausing the visual test wave failed.");
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_PAUSED));
            Assert.That(spawner.ActiveTouristCount, Is.EqualTo(5), "Tourists disappeared when the wave was paused.");

            Assert.That(bootstrap.TryResumeWave().Succeeded, Is.True, "Resuming the visual test wave failed.");
            bootstrap.AdvanceSimulation(100d);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            Assert.That(spawner.ActiveTouristCount, Is.Zero);
            Assert.That(containerObject.transform.childCount, Is.EqualTo(13), "The pool is reused instead of recreated.");
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
            Assert.That(containerObject.transform.childCount, Is.EqualTo(13));
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

        [TestCase("PalmObstacle")]
        [TestCase("RestaurantSupportPostsNavigationObstacle")]
        public void HardNavigationObstaclesKeepTouristSpriteClear(string obstacleName)
        {
            var obstacleObject = new GameObject(obstacleName);
            try
            {
                var obstacleTransform = obstacleObject.AddComponent<RectTransform>();
                obstacleTransform.SetParent(islandBaseObject.transform, false);
                obstacleTransform.sizeDelta = new Vector2(100f, 40f);

                var buildObstacle = typeof(TouristVisualSpawner).GetMethod(
                    "BuildNavigationObstacle", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(buildObstacle, Is.Not.Null);
                var obstacle = (IslandNavigationMap.NavigationObstacle)buildObstacle.Invoke(
                    spawner,
                    new object[] { obstacleTransform });

                Assert.That(
                    obstacle.IntersectsSegment(new Vector2(-120f, 25f), new Vector2(120f, 25f)),
                    Is.True,
                    "The visible tourist footprint must not overlap a hard scene blocker.");
            }
            finally
            {
                Object.DestroyImmediate(obstacleObject);
            }
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
        public void RestaurantGuestUsesReceptionExitBeforeRestaurantApproach()
        {
            boatArrivalSignal.NotifyArrived();
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            var restaurantRoutine = routines[2];
            var route = (IList)GetPrivateField(restaurantRoutine, "Route");
            var passesReceptionExit = false;
            foreach (Vector2 point in route)
                passesReceptionExit |= Vector2.Distance(point, new Vector2(100f, 20f)) < 0.01f;

            Assert.That(
                passesReceptionExit,
                Is.True,
                "Restaurant guests must use the authored route behind Reception.");
        }

        [Test]
        public void LevelOneBarReservesAtMostItsFourAuthoredStools()
        {
            boatArrivalSignal.NotifyArrived();
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            var prepare = FindPrivateMethod("PrepareRoutine", 3);
            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            foreach (var index in new[] { 0, 2, 3 })
            {
                var routine = routines[index];
                SetPrivateField(routine, "RoutineIndex", 1);
                Assert.That(
                    (bool)prepare.Invoke(spawner, new object[] { routine, Vector2.zero, 0f }),
                    Is.True);
            }

            var reservedSeats = new HashSet<int>();
            var waitingGuests = 0;
            foreach (var routine in routines)
            {
                if ((int)GetPrivateField(routine, "RoutineIndex") != 1)
                    continue;

                var seatIndex = (int)GetPrivateField(routine, "BarSeatIndex");
                if (seatIndex >= 0)
                    reservedSeats.Add(seatIndex);
                else
                    waitingGuests++;
            }

            Assert.That(reservedSeats.Count, Is.EqualTo(4));
            Assert.That(waitingGuests, Is.EqualTo(1));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void BarGuestsReachTheAisleBelowTheirOwnStoolBeforeSitting(int level)
        {
            spawnPoint.anchoredPosition = new Vector2(240f, 260f);
            var bar = new GameObject("BeachBarSprite").AddComponent<RectTransform>();
            bar.SetParent(islandBaseObject.transform, false);
            bar.anchoredPosition = new Vector2(240f, 120f);
            bar.sizeDelta = new Vector2(200f, 160f);
            SetPrivateField(spawner, "barNavigationObstacle", bar);

            if (level > 1)
            {
                var stool = CreateDestination($"BarSeatEntry_Lvl{level}_01", new Vector2(240f, 86f));
                ((IList)GetPrivateField(spawner, "barSeatEntryPoints")).Add(stool);
            }
            ResortDebugLevelOverride.SetLevel(level);

            boatArrivalSignal.NotifyArrived();
            FindPrivateMethod("HandleStateChanged", 2).Invoke(
                spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            var barRoutine = routines[1];
            var route = (IList)GetPrivateField(barRoutine, "Route");
            var aisle = (Vector2)route[route.Count - 2];
            var stoolPosition = (Vector2)route[route.Count - 1];
            var barEntrance = (RectTransform)GetPrivateField(spawner, "barSeatPoint");

            Assert.That(aisle.x, Is.EqualTo(stoolPosition.x).Within(0.01f));
            Assert.That(aisle.y, Is.EqualTo(barEntrance.anchoredPosition.y).Within(0.01f));
            Assert.That(stoolPosition.y, Is.GreaterThan(aisle.y));

            var obstacle = (IslandNavigationMap.NavigationObstacle)FindPrivateMethod(
                "BuildNavigationObstacle", 1).Invoke(spawner, new object[] { bar });
            for (var index = 0; index < route.Count - 2; index++)
            {
                Assert.That(obstacle.IntersectsSegment((Vector2)route[index], (Vector2)route[index + 1]),
                    Is.False, "The walk to the aisle must go around the bar.");
            }
        }

        [Test]
        public void BarGuestLeavesVisualSeatThroughWalkableAnchorBeforeRestaurantRoute()
        {
            var realMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/_ResortEmpire/Art/Shared/WorldAndUI/Island/Navigation/island-walkable.png");
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
                new Vector2(480f, -100f);

            boatArrivalSignal.NotifyArrived();
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            var barRoutine = routines[1];
            var enter = typeof(TouristVisualSpawner).GetMethod(
                "EnterDestination", BindingFlags.Instance | BindingFlags.NonPublic);
            enter.Invoke(spawner, new[] { barRoutine });
            SetPrivateField(barRoutine, "DwellRemaining", 0f);

            var advance = typeof(TouristVisualSpawner).GetMethod(
                "AdvanceRoutine", BindingFlags.Instance | BindingFlags.NonPublic);
            advance.Invoke(spawner, new[] { barRoutine });

            Assert.That(GetPrivateField(barRoutine, "Completed"), Is.EqualTo(false),
                "A bar guest should not be dropped when its visual stool is inside blocked art.");
            Assert.That(GetPrivateField(barRoutine, "RoutineIndex"), Is.EqualTo(2));
            Assert.That(GetPrivateField(barRoutine, "AtDestination"), Is.EqualTo(false));

            var route = (IList)GetPrivateField(barRoutine, "Route");
            Assert.That(route.Count, Is.GreaterThan(2));
            Assert.That(Vector2.Distance((Vector2)route[0], new Vector2(4.63f, 95.99f)), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance((Vector2)route[1], new Vector2(4.63f, -25f)), Is.LessThan(0.01f));
            var passedReceptionExit = false;
            foreach (Vector2 point in route)
                passedReceptionExit |= Vector2.Distance(point, new Vector2(-74f, -37f)) < 16f;
            Assert.That(
                passedReceptionExit,
                Is.True,
                "A bar guest must leave through the ReceptionExitPoint before continuing to the Restaurant.");
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
            Assert.That(firstBarGuest.GetComponent<RectTransform>().localScale.y, Is.EqualTo(1.2f).Within(0.001f));
            Assert.That(secondBarGuest.GetComponent<RectTransform>().localScale.y, Is.EqualTo(1.2f).Within(0.001f));
            Assert.That(firstBarGuest.GetComponent<RectTransform>().localScale.x, Is.LessThan(0f));
            Assert.That(secondBarGuest.GetComponent<RectTransform>().localScale.x, Is.GreaterThan(0f));
            Assert.That(secondBarGuest.transform.GetSiblingIndex(), Is.LessThan(firstBarGuest.transform.GetSiblingIndex()),
                "The left stool must draw behind its right neighbor even if the left guest arrives later.");
        }

        [Test]
        public void BuildingGuestsHideAtTheirConfiguredEntranceMarkers()
        {
            boatArrivalSignal.NotifyArrived();
            var stateChanged = typeof(TouristVisualSpawner).GetMethod(
                "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });

            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            var enter = typeof(TouristVisualSpawner).GetMethod(
                "EnterDestination", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var index in new[] { 0, 2 })
            {
                var routine = routines[index];
                enter.Invoke(spawner, new[] { routine });
                var tourist = (GameObject)GetPrivateField(routine, "Tourist");
                var expectedEntry = index == 0 ? receptionEntryPoint : restaurantEntryPoint;
                Assert.That(tourist.activeSelf, Is.False);
                Assert.That(tourist.GetComponent<RectTransform>().anchoredPosition,
                    Is.EqualTo(expectedEntry.anchoredPosition));
            }
        }

        [Test]
        public void BuildingEntrySmokeStartsAtEachEntranceAndReusesFinishedPuffs()
        {
            var smoke = ConfigureEntranceSmoke();
            var setWaveVisibility = FindPrivateMethod("SetWaveVisibility", 1);
            setWaveVisibility.Invoke(spawner, new object[] { true });
            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            var enter = FindPrivateMethod("EnterDestination", 1);

            enter.Invoke(spawner, new[] { routines[0] });
            enter.Invoke(spawner, new[] { routines[1] });
            enter.Invoke(spawner, new[] { routines[2] });
            enter.Invoke(spawner, new[] { routines[2] });

            var images = smoke.GetComponentsInChildren<Image>();
            Assert.That(images, Has.Length.EqualTo(6));
            Assert.That(images[1].rectTransform.anchoredPosition.x, Is.EqualTo(receptionEntryPoint.anchoredPosition.x));
            Assert.That(images[4].rectTransform.anchoredPosition.x, Is.EqualTo(restaurantEntryPoint.anchoredPosition.x));
            foreach (var image in images)
                Assert.That(image.raycastTarget, Is.False);

            var tick = typeof(TouristEntranceSmoke).GetMethod("Advance", BindingFlags.Instance | BindingFlags.NonPublic);
            var initialOpacity = images[1].color.a;
            var initialSize = images[1].rectTransform.sizeDelta;
            tick.Invoke(smoke, new object[] { 0.2f });
            Assert.That(images[1].color.a, Is.LessThan(initialOpacity));
            Assert.That(images[1].rectTransform.sizeDelta.x, Is.GreaterThan(initialSize.x));
            tick.Invoke(smoke, new object[] { 0.5f });
            Assert.That(smoke.GetComponentsInChildren<Image>(), Is.Empty);

            enter.Invoke(spawner, new[] { routines[3] });
            Assert.That(smoke.GetComponentsInChildren<Image>(), Has.Length.EqualTo(3));
            Assert.That(smoke.transform.childCount, Is.EqualTo(6));
            setWaveVisibility.Invoke(spawner, new object[] { false });
            Assert.That(smoke.GetComponentsInChildren<Image>(), Is.Empty);
        }

        [TestCase(1, 766)]
        [TestCase(2, 820)]
        [TestCase(3, 881)]
        public void RestaurantGuestsClimbAndDescendTheStairsForTheCurrentSprite(int level, int landingPixelY)
        {
            const string root = "Assets/_ResortEmpire/";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(root + "Prefabs/Buildings/Restaurant.prefab");
            var restaurant = Object.Instantiate(prefab, islandBaseObject.transform, false);
            var stairs = restaurant.GetComponentInChildren<RestaurantStairRoutes>();
            var restaurantImage = stairs.GetComponent<Image>();
            var restaurantApproach = (RectTransform)GetPrivateField(spawner, "restaurantPoint");
            restaurantApproach.anchoredPosition = new Vector2(480f, -100f);
            touristPrefab.GetComponent<RectTransform>().sizeDelta = new Vector2(40f, 48f);
            SetPrivateField(spawner, "restaurantStairRoutes", stairs);
            SetPrivateField(spawner, "touristCount", 6);

            var setWaveVisibility = FindPrivateMethod("SetWaveVisibility", 1);
            var move = FindPrivateMethod("MoveAlongRoutine", 2);
            var advance = FindPrivateMethod("AdvanceRoutine", 1);

            // Start and clean up a wave at the prefab's initial level. The next
            // wave must use the sprite now shown by the same restaurant instance.
            setWaveVisibility.Invoke(spawner, new object[] { true });
            var routines = (IList)GetPrivateField(spawner, "touristRoutines");
            Assert.That(routines.Count, Is.EqualTo(6));
            var pooledGuest = (GameObject)GetPrivateField(routines[2], "Tourist");
            setWaveVisibility.Invoke(spawner, new object[] { false });
            Assert.That(pooledGuest.activeSelf, Is.False);
            if (level > 1)
            {
                restaurantImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    root + $"Art/UI/HomeIsland/Buildings/Starting/restaurant-level-0{level}.png");
                Assert.That(restaurantImage.sprite, Is.Not.Null);
            }
            setWaveVisibility.Invoke(spawner, new object[] { true });
            Assert.That(routines.Count, Is.EqualTo(6));
            Assert.That(GetPrivateField(routines[2], "Tourist"), Is.SameAs(pooledGuest));

            foreach (var index in new[] { 2, 5 })
            {
                var routine = routines[index];
                var tourist = (GameObject)GetPrivateField(routine, "Tourist");
                var rect = tourist.GetComponent<RectTransform>();
                var route = (List<Vector2>)GetPrivateField(routine, "Route");
                var stairStart = (int)GetPrivateField(routine, "StairStartIndex");
                Assert.That(stairStart, Is.GreaterThan(0));
                Assert.That(route[stairStart - 1], Is.EqualTo(restaurantApproach.anchoredPosition));
                var ascent = route.GetRange(stairStart, route.Count - stairStart);
                Assert.That(ascent.Count, Is.GreaterThan(2));
                for (var step = 1; step < ascent.Count; step++)
                {
                    Assert.That(ascent[step].y, Is.GreaterThan(ascent[step - 1].y));
                    Assert.That(ascent[step].x, Is.LessThan(ascent[step - 1].x));
                }

                SetPrivateField(routine, "StartDelayRemaining", 0f);
                for (var frame = 0; frame < 400 && tourist.activeSelf; frame++)
                {
                    var before = (Vector2)GetPrivateField(routine, "PathPosition");
                    var routeIndex = (int)GetPrivateField(routine, "RouteIndex");
                    move.Invoke(spawner, new[] { routine, (object)0.05f });
                    if (routeIndex < stairStart)
                        continue;

                    var after = (Vector2)GetPrivateField(routine, "PathPosition");
                    Assert.That(Vector2.Distance(before, after), Is.LessThanOrEqualTo(5.25f));
                    Assert.That(tourist.transform.parent, Is.SameAs(stairs.transform));
                    if (routeIndex < route.Count - 1)
                        Assert.That(tourist.activeSelf, Is.True);
                }

                Assert.That(GetPrivateField(routine, "AtDestination"), Is.EqualTo(true));
                Assert.That(GetPrivateField(routine, "DwellRemaining"),
                    Is.EqualTo(GetPrivateField(spawner, "insideDwellSeconds")));
                Assert.That(tourist.activeSelf, Is.False);
                var top = ascent[ascent.Count - 1];
                var foot = rect.TransformPoint(new Vector3(0f, rect.rect.yMin, 0f));
                var localFoot = restaurantImage.rectTransform.InverseTransformPoint(foot);
                var spriteRect = restaurantImage.rectTransform.rect;
                var footPixelY = (spriteRect.yMax - localFoot.y) / spriteRect.height * restaurantImage.sprite.rect.height;
                Assert.That(footPixelY, Is.EqualTo(landingPixelY).Within(0.01f));

                advance.Invoke(spawner, new[] { routine });
                Assert.That(tourist.activeSelf, Is.True);
                Assert.That(rect.localScale.x, Is.GreaterThan(0f));
                var departure = (Vector2)containerObject.transform.InverseTransformPoint(rect.position);
                Assert.That(Vector2.Distance(departure, top), Is.LessThan(0.001f));
                Assert.That(GetPrivateField(routine, "RoutineIndex"), Is.EqualTo(0));
                for (var step = 0; step < ascent.Count - 1; step++)
                    Assert.That(route[step], Is.EqualTo(ascent[ascent.Count - 2 - step]));
                Assert.That(route[ascent.Count - 1], Is.EqualTo(restaurantApproach.anchoredPosition));
                Assert.That(route.Count, Is.GreaterThan(ascent.Count));

                for (var frame = 0; frame < 150 && rect.parent == stairs.transform; frame++)
                {
                    var before = (Vector2)GetPrivateField(routine, "PathPosition");
                    move.Invoke(spawner, new[] { routine, (object)0.05f });
                    var after = (Vector2)GetPrivateField(routine, "PathPosition");
                    Assert.That(Vector2.Distance(before, after), Is.LessThanOrEqualTo(5.25f));
                    Assert.That(tourist.activeSelf, Is.True);
                }
                Assert.That(rect.parent, Is.SameAs(containerObject.transform));
            }

            setWaveVisibility.Invoke(spawner, new object[] { false });
            Assert.That(spawner.ActiveTouristCount, Is.Zero);
            Assert.That(routines, Is.Empty);
            foreach (var tourist in (IList)GetPrivateField(spawner, "pooledTourists"))
                Assert.That(((GameObject)tourist).activeSelf, Is.False);
        }

        [Test]
        public void BarGuestDisappearsAtReceptionAndReappearsOnInnerPath()
        {
            var smoke = ConfigureEntranceSmoke();
            var realMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/_ResortEmpire/Art/Shared/WorldAndUI/Island/Navigation/island-walkable.png");
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
                new Vector2(480f, -100f);

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
            var portalSmoke = smoke.GetComponentsInChildren<Image>();
            Assert.That(portalSmoke, Has.Length.EqualTo(3));
            Assert.That(portalSmoke[1].rectTransform.anchoredPosition.x,
                Is.EqualTo(((Vector2)route[portalEntry]).x).Within(0.01f),
                "Smoke belongs at the visible entrance before the guest is moved to the portal exit.");
            Assert.That(tourist.GetComponent<RectTransform>().anchoredPosition,
                Is.EqualTo((Vector2)route[portalExit]),
                "A hidden tourist must be positioned at the portal exit before reactivation.");

            move.Invoke(spawner, new[] { barRoutine, (object)1f });
            Assert.That(tourist.activeSelf, Is.True);
            Assert.That(smoke.GetComponentsInChildren<Image>(), Has.Length.EqualTo(3));
            Assert.That(tourist.GetComponent<RectTransform>().anchoredPosition,
                Is.EqualTo((Vector2)route[portalExit]));
        }

        [Test]
        public void RestaurantGuestUsesReceptionPortalAndAllConfiguredTouristsRemainActive()
        {
            var realMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/_ResortEmpire/Art/Shared/WorldAndUI/Island/Navigation/island-walkable.png");
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
                new Vector2(480f, -100f);

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

        private TouristEntranceSmoke ConfigureEntranceSmoke()
        {
            var overlay = CreateDestination("TouristEntranceEffects", Vector2.zero);
            var smoke = overlay.gameObject.AddComponent<TouristEntranceSmoke>();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/_ResortEmpire/Art/Shared/UI/VFX/tourist-entrance-smoke.png");
            Assert.That(sprite, Is.Not.Null);
            SetPrivateField(smoke, "puffSprite", sprite);
            SetPrivateField(spawner, "entranceSmoke", smoke);
            return smoke;
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, fieldName);
            return field.GetValue(target);
        }

        private static MethodInfo FindPrivateMethod(string methodName, int parameterCount)
        {
            var methods = typeof(TouristVisualSpawner).GetMethods(
                BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                if (method.Name == methodName
                    && method.GetParameters().Length == parameterCount)
                {
                    return method;
                }
            }

            Assert.Fail($"Missing {methodName} overload with {parameterCount} parameters.");
            return null;
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
