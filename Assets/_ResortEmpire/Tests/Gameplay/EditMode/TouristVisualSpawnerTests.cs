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
        private GameObject touristPrefab;

        [SetUp]
        public void SetUp()
        {
            bootstrapObject = new GameObject("TouristVisualBootstrapTest");
            bootstrap = bootstrapObject.AddComponent<ResortGameplayBootstrap>();
            Assert.That(bootstrap.TryConfigure(new TestDependencies()), Is.True);

            containerObject = new GameObject("TouristContainer");
            containerObject.AddComponent<RectTransform>();

            touristPrefab = new GameObject("TouristCharacter");
            touristPrefab.AddComponent<RectTransform>();
            var image = touristPrefab.AddComponent<Image>();
            image.raycastTarget = true;
            touristPrefab.SetActive(false);

            spawnerObject = new GameObject("TouristVisualSpawnerTest");
            spawnerObject.SetActive(false);
            spawner = spawnerObject.AddComponent<TouristVisualSpawner>();
            SetPrivateField(spawner, "gameplayBootstrap", bootstrap);
            SetPrivateField(spawner, "touristContainer", containerObject.GetComponent<RectTransform>());
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
            Object.DestroyImmediate(bootstrapObject);
        }

        [Test]
        public void WaveLifecycleShowsTouristsDuringActiveAndPausedPhasesThenHidesThem()
        {
            Assert.That(spawner.ActiveTouristCount, Is.Zero);

            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            Assert.That(spawner.ActiveTouristCount, Is.EqualTo(5));
            Assert.That(containerObject.transform.childCount, Is.EqualTo(5));

            bootstrap.AdvanceSimulation(1d);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_ACTIVE));
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

            stateChanged.Invoke(spawner, new object[] { GamePhase.AWAITING_WAVE, GamePhase.WAVE_ACTIVE });
            stateChanged.Invoke(spawner, new object[] { GamePhase.WAVE_INCOMING, GamePhase.WAVE_ACTIVE });

            Assert.That(spawner.ActiveTouristCount, Is.EqualTo(5));
            Assert.That(containerObject.transform.childCount, Is.EqualTo(5));
            foreach (var image in containerObject.GetComponentsInChildren<Image>(true))
                Assert.That(image.raycastTarget, Is.False);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
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
