using System;
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
            var changes = 0;
            facade.WaveResultReady += result => { if (!result.Claimed) results++; };
            facade.ResourcesChanged += resources => { Assert.That(facade.Resources.Coins, Is.EqualTo(resources.Coins)); changes++; };
            for (var wave = 1; wave <= 8; wave++)
            {
                Assert.That(facade.TryStartWave().Succeeded, Is.True);
                bootstrap.AdvanceSimulation(10000);
                Assert.That(facade.CurrentWaveResult.WaveId, Is.EqualTo(wave));
                Assert.That(facade.TryClaimWaveReward(facade.CurrentWaveResult.RewardId).Succeeded, Is.True);
                Assert.That(facade.TryClaimWaveReward(facade.CurrentWaveResult.RewardId).Succeeded, Is.False);
                Assert.That(facade.TryContinueAfterResults().Succeeded, Is.True);
            }
            Assert.That(results, Is.EqualTo(8));
            Assert.That(changes, Is.EqualTo(8));
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
