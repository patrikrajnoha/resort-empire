using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class WaveNeedsPanelSceneTests
    {
        [Test]
        public void ResortSceneS2BindsOrderedCardsHidesLiveSatisfactionAndKeepsTheIslandVisible()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_ResortEmpire/Scenes/Resort.unity");
            try
            {
                var gameUi = scene.GetRootGameObjects().Single(x => x.name == "GameUI");
                var s2 = gameUi.transform.Find("SafeArea/HomeIslandViewCanvas/WaveNeedsPanel");
                Assert.That(s2, Is.Not.Null);
                var presenter = new SerializedObject(s2.GetComponent("WaveNeedsPresenter"));

                var cards = presenter.FindProperty("cards");
                Assert.That(cards.arraySize, Is.EqualTo(5), "Three base needs plus Pool and Bungalows.");
                for (var i = 0; i < cards.arraySize; i++)
                {
                    var card = cards.GetArrayElementAtIndex(i);
                    Assert.That(card.FindPropertyRelative("Need").intValue, Is.EqualTo(i), "Cards keep NeedType order.");
                    foreach (var field in new[] { "Root", "Name", "Count", "Detail", "Button", "AlertIcon" })
                        Assert.That(card.FindPropertyRelative(field).objectReferenceValue, Is.Not.Null, "Card " + i + " " + field);

                    // Tapping a card must open S3 for its own need: the Button lives on that card, with no stale scene listeners.
                    var cardRoot = (RectTransform)card.FindPropertyRelative("Root").objectReferenceValue;
                    var button = (UnityEngine.UI.Button)card.FindPropertyRelative("Button").objectReferenceValue;
                    Assert.That(cardRoot.name, Is.EqualTo(((NeedType)i) + "Card"));
                    Assert.That(button.gameObject, Is.SameAs(cardRoot.gameObject));
                    Assert.That(button.onClick.GetPersistentEventCount(), Is.Zero, cardRoot.name);
                }

                // Build IV: no live numeric Wave Satisfaction during a wave.
                foreach (var name in new[] { "RatingStars", "RatingValue", "SatisfactionBar", "SatisfactionThresholds" })
                    Assert.That(s2.Find("Panel/" + name).gameObject.activeSelf, Is.False, name);

                var toast = (CanvasGroup)presenter.FindProperty("feedbackToast").objectReferenceValue;
                Assert.That(toast, Is.Not.Null);
                Assert.That(toast.gameObject.activeSelf, Is.False);
                Assert.That(toast.blocksRaycasts, Is.False, "Feedback never interrupts play.");
                Assert.That(presenter.FindProperty("feedbackText").objectReferenceValue, Is.Not.Null);

                var panel = (RectTransform)s2.Find("Panel");
                Assert.That(panel.anchorMin.x, Is.EqualTo(1f), "S2 docks to the right edge.");
                Assert.That(panel.sizeDelta.x / 2340f, Is.LessThan(0.4f), "Most of the island stays visible.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }

    public sealed class GameplayUiNavigationTests
    {
        private GameObject root;
        private ResortGameplayBootstrap bootstrap;
        private ResortUiStateCoordinator coordinator;
        private GameObject needs;
        private GameObject results;
        private GameObject upgrade;
        private GameObject workers;
        private GameObject unlock;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("NavigationTest");
            bootstrap = root.AddComponent<ResortGameplayBootstrap>();
            Assert.That(bootstrap.TryConfigureProgression(EconomyTestFixtures.Config(coins: 1000), new TestProgressionPersistence()), Is.True);
            coordinator = root.AddComponent<ResortUiStateCoordinator>();
            needs = Window("WaveNeeds");
            results = Window("WaveComplete");
            upgrade = Window("UpgradeSuggestion");
            workers = Window("WorkerAllocation");
            unlock = Window("AreaUnlock");
            Set("gameplayBootstrap", bootstrap);
            Set("homeIslandView", Window("HomeIsland"));
            Set("waveNeedsPanel", needs);
            Set("waveCompleteWindow", results);
            Set("upgradeSuggestionWindow", upgrade);
            Set("workerAllocationWindow", workers);
            Set("areaUnlockWindow", unlock);
            // Edit Mode does not run OnEnable; bind exactly as the component does at runtime.
            typeof(ResortUiStateCoordinator).GetMethod("BindToGameplay", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(coordinator, null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        [Test]
        public void ForecastDescribesTheNextScriptedWaveWithoutStartingIt()
        {
            var forecast = bootstrap.NextWaveForecast;
            Assert.That(forecast.WaveId, Is.EqualTo(1));
            Assert.That(forecast.DemandTotal, Is.EqualTo(36));
            Assert.That(bootstrap.TotalWaves, Is.EqualTo(8));
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.AWAITING_WAVE));
        }

        [Test]
        public void OneLoopMovesThroughWaveNeedsResultsUpgradeAndBack()
        {
            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            Assert.That(needs.activeSelf, Is.True);
            Assert.That(coordinator.OpenWorkerAllocation(), Is.True);

            bootstrap.AdvanceSimulation(10000);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            Assert.That(needs.activeSelf, Is.False);
            Assert.That(workers.activeSelf, Is.False, "S3 closes once the wave is over.");
            Assert.That(results.activeSelf, Is.True);

            Assert.That(bootstrap.TryClaimWaveReward(bootstrap.CurrentWaveResult.RewardId).Succeeded, Is.True);
            Assert.That(results.activeSelf, Is.False);
            Assert.That(upgrade.activeSelf, Is.True);
            Assert.That(coordinator.OpenWorkerAllocation(), Is.False);

            Assert.That(bootstrap.TryContinueAfterResults().Succeeded, Is.True);
            Assert.That(upgrade.activeSelf, Is.False);
            Assert.That(bootstrap.CompletedWaves, Is.EqualTo(1));
            Assert.That(bootstrap.NextWaveForecast.WaveId, Is.EqualTo(2));
        }

        [Test]
        public void NeedCardOpensWorkerAllocationForItsServiceAndClosingReturnsToS2()
        {
            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            Assert.That(needs.activeSelf, Is.True, "S2 opens automatically when the wave starts.");

            Assert.That(coordinator.OpenWorkerAllocationForService(BuildingId.BeachBar), Is.True);
            Assert.That(workers.activeSelf, Is.True);
            Assert.That(coordinator.IsWorkerAllocationOpen, Is.True);
            Assert.That(needs.activeSelf, Is.True, "S2 stays active underneath S3.");
            Assert.That(coordinator.WorkerAllocationFocus, Is.EqualTo(BuildingId.BeachBar));
            Assert.That(coordinator.WorkerAllocationTarget, Is.EqualTo(BuildingId.BeachBar), "A vacant service is the move destination.");

            Assert.That(coordinator.OpenWorkerAllocationForService(BuildingId.Reception), Is.True);
            Assert.That(coordinator.WorkerAllocationFocus, Is.EqualTo(BuildingId.Reception));
            Assert.That(coordinator.WorkerAllocationTarget, Is.Null, "A staffed service can still give its Worker away.");

            workers.SetActive(false);
            Assert.That(coordinator.IsWorkerAllocationOpen, Is.False);
            Assert.That(needs.activeSelf, Is.True, "Closing S3 returns to S2.");

            bootstrap.AdvanceSimulation(5);
            Assert.That(needs.activeSelf, Is.True, "S2 remains visible while the wave runs.");
        }

        [Test]
        public void NeedCardForAnUnpurchasedServiceDoesNotOpenWorkerAllocation()
        {
            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            Assert.That(coordinator.OpenWorkerAllocationForService(BuildingId.Pool), Is.False);
            Assert.That(workers.activeSelf, Is.False);
        }

        [Test]
        public void NewlyEligibleAreaIsAnnouncedOnceAndCanBeReopened()
        {
            for (var wave = 1; wave <= 3; wave++)
            {
                Assert.That(unlock.activeSelf, Is.False);
                Assert.That(coordinator.OpenAreaUnlock(), Is.False);
                Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
                bootstrap.AdvanceSimulation(10000);
                Assert.That(bootstrap.TryClaimWaveReward(bootstrap.CurrentWaveResult.RewardId).Succeeded, Is.True);
                Assert.That(bootstrap.TryContinueAfterResults().Succeeded, Is.True);
            }

            Assert.That(unlock.activeSelf, Is.True, "Pool becomes eligible after Wave 3.");
            unlock.SetActive(false);
            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            Assert.That(coordinator.OpenAreaUnlock(), Is.False, "No purchases during a wave.");
            bootstrap.AdvanceSimulation(10000);
            Assert.That(bootstrap.TryClaimWaveReward(bootstrap.CurrentWaveResult.RewardId).Succeeded, Is.True);
            Assert.That(bootstrap.TryContinueAfterResults().Succeeded, Is.True);
            Assert.That(unlock.activeSelf, Is.False, "Later is respected; the plot marker reopens S9.");
            Assert.That(coordinator.OpenAreaUnlock(), Is.True);
        }

        private GameObject Window(string name)
        {
            var window = new GameObject(name);
            window.transform.SetParent(root.transform);
            window.SetActive(false);
            return window;
        }

        private void Set(string field, object value)
        {
            var info = typeof(ResortUiStateCoordinator).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, field);
            info.SetValue(coordinator, value);
        }
    }
}
