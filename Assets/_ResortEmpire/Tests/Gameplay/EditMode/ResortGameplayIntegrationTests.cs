using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Waves;
using UnityEngine;
using ResortEmpire.Gameplay;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class ResortGameplayIntegrationTests
    {
        private GameObject bootstrapObject;
        private ResortGameplayBootstrap bootstrap;

        [SetUp]
        public void SetUp()
        {
            bootstrapObject = new GameObject("TestResortGameplayBootstrap");
            bootstrap = bootstrapObject.AddComponent<ResortGameplayBootstrap>();
        }

        [TearDown]
        public void TearDown()
        {
            if (bootstrapObject != null)
                Object.DestroyImmediate(bootstrapObject);
        }

        [Test]
        public void MissingDependenciesRemainSafeAndAwaitingWave()
        {
            Assert.That(bootstrap.IsConfigured, Is.False);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.AWAITING_WAVE));
            Assert.That(bootstrap.SimulationTime, Is.EqualTo(0d));

            var result = bootstrap.TryStartWave();

            Assert.That(result.Status, Is.EqualTo(ResortGameplayCommandStatus.Unavailable));
            Assert.That(result.Message, Does.Contain("not configured"));
        }

        [Test]
        public void ValidDependenciesCreateOneCanonicalRuntimeAndForwardState()
        {
            var dependencies = CreateDependencies();
            var transitions = 0;
            bootstrap.StateChanged += (_, __) => transitions++;

            Assert.That(bootstrap.TryConfigure(dependencies), Is.True);
            Assert.That(bootstrap.IsConfigured, Is.True);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.AWAITING_WAVE));

            var result = bootstrap.TryStartWave();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_INCOMING));
            Assert.That(transitions, Is.EqualTo(1));
            Assert.That(bootstrap.CurrentWaveSnapshot, Is.Not.Null);
        }

        [Test]
        public void InvalidDependenciesDoNotCreatePartialRuntime()
        {
            var result = bootstrap.TryConfigure(new TestDependencies
            {
                BuildingProvider = null,
                WaveDefinitionProvider = new TestWaveDefinitionProvider(),
                SimulationConfig = new WaveSimulationConfig(0d, 1d, 0.5d),
                InitialWorkerAssignments = new[] { BuildingId.Reception }
            });

            Assert.That(result, Is.False);
            Assert.That(bootstrap.IsConfigured, Is.False);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.AWAITING_WAVE));
            Assert.That(bootstrap.TryPauseWave().Status, Is.EqualTo(ResortGameplayCommandStatus.Unavailable));
        }

        [Test]
        public void CoordinatorSynchronizesInitialAndIncomingVisibility()
        {
            var home = new GameObject("TestHomeIslandView");
            var needs = new GameObject("TestWaveNeedsPanel");
            needs.SetActive(false);
            var coordinatorObject = new GameObject("TestResortUiCoordinator");
            coordinatorObject.SetActive(false);
            var coordinator = coordinatorObject.AddComponent<ResortUiStateCoordinator>();
            SetPrivateField(coordinator, "gameplayBootstrap", bootstrap);
            SetPrivateField(coordinator, "homeIslandView", home);
            SetPrivateField(coordinator, "waveNeedsPanel", needs);
            coordinatorObject.SetActive(true);

            var applyPhase = typeof(ResortUiStateCoordinator).GetMethod(
                "ApplyPhase",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(applyPhase, Is.Not.Null);

            applyPhase.Invoke(coordinator, new object[] { GamePhase.AWAITING_WAVE });
            Assert.That(home.activeSelf, Is.True);
            Assert.That(needs.activeSelf, Is.False);

            applyPhase.Invoke(coordinator, new object[] { GamePhase.WAVE_INCOMING });
            Assert.That(needs.activeSelf, Is.True);

            Object.DestroyImmediate(coordinatorObject);
            Object.DestroyImmediate(home);
            Object.DestroyImmediate(needs);
        }

        [Test]
        public void RebindingConfiguredRuntimeIsRejected()
        {
            var dependencies = CreateDependencies();

            Assert.That(bootstrap.TryConfigure(dependencies), Is.True);
            Assert.That(bootstrap.TryConfigure(dependencies), Is.False);
            Assert.That(bootstrap.IsConfigured, Is.True);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.AWAITING_WAVE));
        }

        [Test]
        public void ArrivalWindowElapsedUsesIncomingPhaseProgress()
        {
            var state = new GameState();
            var clock = new SimulationClock();
            var buildings = new TestBuildingProvider();
            var workers = new ResortEmpire.Gameplay.Staff.WorkerManager(
                buildings,
                state,
                new[] { BuildingId.Reception, BuildingId.Restaurant });
            var waves = new WaveManager(
                state,
                clock,
                buildings,
                workers,
                new WaveSimulationConfig(2d, 1d, 0.5d));

            waves.StartWave(new WaveDefinition(
                1,
                4d,
                new[] { new NeedDemandDefinition(NeedType.Rooms, 1) }));
            waves.Advance(1d);

            Assert.That(waves.CurrentPhase, Is.EqualTo(GamePhase.WAVE_INCOMING));
            Assert.That(waves.ArrivalWindowElapsed, Is.EqualTo(1d).Within(1e-9));
            Assert.That(waves.ArrivalElapsed, Is.EqualTo(0d).Within(1e-9));

            waves.Advance(1d);

            Assert.That(waves.CurrentPhase, Is.EqualTo(GamePhase.WAVE_ACTIVE));
            Assert.That(waves.ArrivalWindowElapsed, Is.EqualTo(2d).Within(1e-9));
        }

        private static TestDependencies CreateDependencies()
        {
            return new TestDependencies
            {
                BuildingProvider = new TestBuildingProvider(),
                WaveDefinitionProvider = new TestWaveDefinitionProvider(),
                SimulationConfig = new WaveSimulationConfig(0d, 1d, 0.5d),
                InitialWorkerAssignments = new[] { BuildingId.Reception, BuildingId.Restaurant }
            };
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Expected private field was not found: " + fieldName);
            field.SetValue(target, value);
        }

        private sealed class TestDependencies : IResortGameplayDependencySource
        {
            public IBuildingSnapshotProvider BuildingProvider { get; set; }
            public IWaveDefinitionProvider WaveDefinitionProvider { get; set; }
            public WaveSimulationConfig SimulationConfig { get; set; }
            public IReadOnlyList<BuildingId> InitialWorkerAssignments { get; set; }
        }

        private sealed class TestWaveDefinitionProvider : IWaveDefinitionProvider
        {
            public WaveDefinition GetNextWave()
            {
                return new WaveDefinition(
                    1,
                    1d,
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
