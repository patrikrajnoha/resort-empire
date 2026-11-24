using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class EconomyUnityIntegrationTests
    {
        private GameObject bootstrapObject;
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "resort-economy-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (bootstrapObject != null) UnityEngine.Object.DestroyImmediate(bootstrapObject);
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }

        [Test]
        public void DemoAssetImportsAndContainsApprovedTrialValues()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ResortEconomyConfig>("Assets/_ResortEmpire/Data/Progression/DemoEconomy.asset");
            Assert.That(asset, Is.Not.Null);
            var config = asset.CreateRuntimeConfig();
            Assert.DoesNotThrow(config.Validate);
            Assert.That(config.Capacities, Is.EqualTo(new[] { 3, 5, 7 }));
            Assert.That(config.BaseDemand, Is.EqualTo(new[] { 12, 15, 18, 18, 21, 24, 24, 27 }));
            Assert.That(config.UnlockWaves, Is.EqualTo(new[] { 0, 0, 0, 3, 6 }));
            Assert.That(config.CoinMultipliers, Is.EqualTo(new[] { 0.6, 0.8, 1, 1.2, 1.5 }));
        }

        [Test]
        public void ResortSceneHasAssignedProductionDependencies()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_ResortEmpire/Scenes/Resort.unity");
            try
            {
                var systems = scene.GetRootGameObjects().Single(x => x.name == "GameSystems");
                var bootstrap = systems.GetComponent<ResortGameplayBootstrap>();
                var source = systems.GetComponent<ResortGameplayDependencySource>();
                Assert.That(bootstrap, Is.Not.Null);
                Assert.That(source, Is.Not.Null);
                var serialized = new SerializedObject(bootstrap);
                Assert.That(serialized.FindProperty("dependencySourceBehaviour").objectReferenceValue, Is.EqualTo(source));
                Assert.DoesNotThrow(() => source.ProgressionConfig.Validate());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void FacadeCompletesAllWavesAndPublishesCanonicalResults()
        {
            bootstrapObject = new GameObject("EconomyBootstrapTest");
            var bootstrap = bootstrapObject.AddComponent<ResortGameplayBootstrap>();
            Assert.That(bootstrap.TryConfigureProgression(EconomyTestFixtures.Config(), new TestProgressionPersistence()), Is.True);
            IResortGameplayUiFacade facade = bootstrap;
            var results = 0;
            var walletUpdates = new List<ResourceSnapshot>();
            facade.WaveResultReady += result => { if (!result.Claimed) results++; };
            facade.ResourcesChanged += walletUpdates.Add;
            for (var wave = 1; wave <= 8; wave++)
            {
                Assert.That(facade.TryStartWave().Succeeded, Is.True);
                bootstrap.AdvanceSimulation(10000);
                Assert.That(facade.CurrentWaveResult.WaveId, Is.EqualTo(wave));
                Assert.That(facade.TryClaimWaveReward(facade.CurrentWaveResult.RewardId).Succeeded, Is.True);
                Assert.That(facade.TryClaimWaveReward(facade.CurrentWaveResult.RewardId).Succeeded, Is.False);
                Assert.That(walletUpdates, Has.Count.EqualTo(wave));
                Assert.That(walletUpdates[wave - 1], Is.EqualTo(facade.Resources));
                Assert.That(facade.TryContinueAfterResults().Succeeded, Is.True);
            }
            Assert.That(results, Is.EqualTo(8));
            Assert.That(walletUpdates, Has.Count.EqualTo(8));
            Assert.That(facade.TryStartWave().Succeeded, Is.False);
        }

        [Test]
        public void FacadeReportsFailedSaveAsUnavailableAndRetainsUnclaimedResult()
        {
            bootstrapObject = new GameObject("EconomyBootstrapTest");
            var bootstrap = bootstrapObject.AddComponent<ResortGameplayBootstrap>();
            var disk = new TestProgressionPersistence();
            Assert.That(bootstrap.TryConfigureProgression(EconomyTestFixtures.Config(), disk), Is.True);
            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            bootstrap.AdvanceSimulation(10000);
            disk.FailWrites = true;
            Assert.That(bootstrap.TryClaimWaveReward(bootstrap.CurrentWaveResult.RewardId).Status, Is.EqualTo(ResortGameplayCommandStatus.Unavailable));
            Assert.That(bootstrap.CurrentWaveResult.Claimed, Is.False);
            Assert.That(bootstrap.PersistenceDiagnostic, Is.Not.Empty);
        }

        [Test]
        public void RepeatedBackgroundNotificationDoesNotLoseAutomaticResume()
        {
            bootstrapObject = new GameObject("EconomyBootstrapTest");
            var bootstrap = bootstrapObject.AddComponent<ResortGameplayBootstrap>();
            Assert.That(bootstrap.TryConfigureProgression(EconomyTestFixtures.Config(), new TestProgressionPersistence()), Is.True);
            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            bootstrap.AdvanceSimulation(10);
            var pause = typeof(ResortGameplayBootstrap).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic);
            pause.Invoke(bootstrap, new object[] { true });
            pause.Invoke(bootstrap, new object[] { true });
            var time = bootstrap.SimulationTime;
            bootstrap.AdvanceSimulation(1000);
            Assert.That(bootstrap.SimulationTime, Is.EqualTo(time));
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_PAUSED));
            pause.Invoke(bootstrap, new object[] { false });
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_ACTIVE));
        }

        [Test]
        public void JsonRoundTripPersistsUnclaimedAndClaimedResultsWithoutDuplicateCredit()
        {
            var path = Path.Combine(directory, "progress.json");
            var store = new JsonFileProgressionPersistence(path);
            Assert.That(store.Load(), Is.Null);
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), store)) EconomyTestFixtures.Complete(game);
            var before = store.Load();
            Assert.That(before.Result.Claimed, Is.False);
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), store))
                Assert.That(game.TryClaimWaveReward(game.CurrentWaveResult.RewardId), Is.True);
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), store))
            {
                Assert.That(game.Resources.Coins, Is.EqualTo(before.Result.Coins));
                Assert.That(game.TryClaimWaveReward(game.CurrentWaveResult.RewardId), Is.False);
                Assert.That(game.CurrentWaveResult.Waiting.Sum(x => x.ServedUnits), Is.EqualTo(before.Result.ServedUnits));
            }
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }

        // Regression: JsonUtility writes a null Result as a default object, which used to make the
        // second launch fail with "Awaiting wave cannot contain an unconsumed result."
        [Test]
        public void JsonCheckpointWithoutResultReloadsOnNextLaunch()
        {
            var path = Path.Combine(directory, "progress.json");
            var store = new JsonFileProgressionPersistence(path);
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), store))
                Assert.That(game.CurrentWaveResult, Is.Null);
            Assert.That(store.Load().Result, Is.Null);
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), store))
            {
                EconomyTestFixtures.FinishWaves(game, 1);
                Assert.That(game.State.CurrentState, Is.EqualTo(GamePhase.AWAITING_WAVE));
            }
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), store))
            {
                Assert.That(game.ClaimedWaves, Is.EqualTo(1));
                Assert.That(game.CurrentWaveResult, Is.Null);
            }
        }

        [Test]
        public void PausedUnboostedWaveRejectsSaveAndReloadsThePreWaveJsonCheckpoint()
        {
            var path = Path.Combine(directory, "unboosted-wave.json");
            var config = EconomyTestFixtures.Config(coins: 1000);
            var store = new JsonFileProgressionPersistence(path);
            using (var game = new ResortProgressionRuntime(config, store))
            {
                Assert.That(game.TryStartWave(), Is.True);
                var preWaveBytes = File.ReadAllBytes(path);
                game.Waves.Advance(45d);
                Assert.That(game.State.CurrentState, Is.EqualTo(GamePhase.WAVE_ACTIVE));
                Assert.That(game.Waves.CaptureSnapshot().DemandArrived, Is.GreaterThan(0));
                Assert.That(game.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.True);
                Assert.That(game.Waves.TryPauseWave(), Is.True);
                Assert.That(game.CanSaveActiveWave, Is.False);
                Assert.That(game.TrySaveProgress(), Is.False);
                Assert.That(game.LastFailureWasPersistence, Is.False);
                Assert.That(game.LastCommandError, Does.Contain("pre-wave checkpoint"));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(preWaveBytes));
                Assert.That(store.Load().ActiveWave, Is.Null);
            }

            using var restored = new ResortProgressionRuntime(config, new JsonFileProgressionPersistence(path));
            Assert.That(restored.State.CurrentState, Is.EqualTo(GamePhase.AWAITING_WAVE));
            Assert.That(restored.Clock.CurrentTime, Is.Zero);
            Assert.That(restored.GetNextWave().WaveId, Is.EqualTo(1));
            Assert.That(restored.Workers.GetAssignedWorkerCount(BuildingId.Reception), Is.EqualTo(1));
            Assert.That(restored.Workers.GetAssignedWorkerCount(BuildingId.BeachBar), Is.Zero);
            Assert.That(restored.Resources, Is.EqualTo(new ResourceSnapshot(1000, 0, 0)));
        }

        [Test]
        public void InvalidJsonIsPreservedAndBlocksConfiguration()
        {
            var path = Path.Combine(directory, "progress.json");
            File.WriteAllText(path, "{ broken checkpoint");
            bootstrapObject = new GameObject("EconomyBootstrapTest");
            var bootstrap = bootstrapObject.AddComponent<ResortGameplayBootstrap>();
            Assert.That(bootstrap.TryConfigureProgression(EconomyTestFixtures.Config(), new JsonFileProgressionPersistence(path)), Is.False);
            Assert.That(bootstrap.IsConfigured, Is.False);
            Assert.That(bootstrap.ConfigurationDiagnostic, Is.Not.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo("{ broken checkpoint"));
        }
    }
}
