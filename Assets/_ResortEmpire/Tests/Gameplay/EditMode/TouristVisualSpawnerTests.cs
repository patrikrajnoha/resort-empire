using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Waves;
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
        private GameObject touristPrefab;
        private GameObject boatSignalObject;
        private BoatArrivalSignal boatArrivalSignal;

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

            spawnerObject = new GameObject("TouristVisualSpawnerTest");
            spawnerObject.SetActive(false);
            spawner = spawnerObject.AddComponent<TouristVisualSpawner>();
            SetPrivateField(spawner, "gameplayBootstrap", bootstrap);
            SetPrivateField(spawner, "boatArrivalSignal", boatArrivalSignal);
            SetPrivateField(spawner, "touristContainer", containerObject.GetComponent<RectTransform>());
            SetPrivateField(spawner, "spawnPoint", spawnPoint);
            SetPrivateField(spawner, "touristPrefab", touristPrefab);
            SetPrivateField(spawner, "touristCount", 5);
            spawnerObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(spawnerObject);
            Object.DestroyImmediate(touristPrefab);
            Object.DestroyImmediate(containerObject);
            Object.DestroyImmediate(boatSignalObject);
            Object.DestroyImmediate(bootstrapObject);
        }

        [Test]
        public void WaveLifecycleShowsTouristsDuringActiveAndPausedPhasesThenHidesThem()
        {
            Assert.That(spawner.ActiveTouristCount, Is.Zero);

            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            Assert.That(spawner.ActiveTouristCount, Is.Zero, "Tourists wait for the boat to reach the dock.");
            boatArrivalSignal.NotifyArrived();
            Assert.That(spawner.ActiveTouristCount, Is.Zero, "The boat cannot spawn tourists before the incoming phase ends.");

            bootstrap.AdvanceSimulation(1d);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_ACTIVE));
            Assert.That(spawner.ActiveTouristCount, Is.EqualTo(5));
            Assert.That(containerObject.transform.childCount, Is.EqualTo(5));
            Assert.That(bootstrap.TryPauseWave().Succeeded, Is.True);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_PAUSED));
            Assert.That(spawner.ActiveTouristCount, Is.EqualTo(5));

            Assert.That(bootstrap.TryResumeWave().Succeeded, Is.True);
            bootstrap.AdvanceSimulation(100d);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            Assert.That(spawner.ActiveTouristCount, Is.Zero);
            Assert.That(containerObject.transform.childCount, Is.EqualTo(5), "The pool is reused instead of recreated.");
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
            Assert.That(containerObject.transform.childCount, Is.EqualTo(5));
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
                spawnPoint.anchoredPosition + new Vector2(-48f, 0f),
                spawnPoint.anchoredPosition,
                spawnPoint.anchoredPosition + new Vector2(48f, 0f),
                spawnPoint.anchoredPosition + new Vector2(-24f, -36f),
                spawnPoint.anchoredPosition + new Vector2(24f, -36f)
            };

            for (var index = 0; index < tourists.Length; index++)
                Assert.That(tourists[index].rectTransform.anchoredPosition, Is.EqualTo(expectedPositions[index]));
        }

        [Test]
        public void TouristStaysAtDestinationInsteadOfReturningToSpawn()
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

            var tourist = containerObject.GetComponentsInChildren<Image>(true)[1];
            var destinationPosition = tourist.rectTransform.anchoredPosition;
            var animate = typeof(TouristVisualSpawner).GetMethod(
                "AnimateTourists", BindingFlags.Instance | BindingFlags.NonPublic);
            animate.Invoke(spawner, null);

            Assert.That(GetPrivateField(barRoutine, "Completed"), Is.EqualTo(true));
            Assert.That(tourist.gameObject.activeSelf, Is.True);
            Assert.That(tourist.rectTransform.anchoredPosition, Is.EqualTo(destinationPosition));

            animate.Invoke(spawner, null);
            Assert.That(tourist.rectTransform.anchoredPosition, Is.EqualTo(destinationPosition));
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
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
