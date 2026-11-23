using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class GameSessionNavigatorTests
    {
        private const string MainMenuPath = "Assets/_ResortEmpire/Scenes/MainMenu.unity";
        private FakeHost host;
        private IDisposable hostScope;
        private string root;

        [SetUp]
        public void SetUp()
        {
            host = new FakeHost();
            hostScope = GameSessionNavigator.UseHost(host);
            root = Path.Combine(Path.GetTempPath(), "resort-navigation-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            hostScope.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void RepeatedRequestsLoadGameplayOnlyOnce()
        {
            Assert.That(GameSessionNavigator.TryEnterGameplay(), Is.True);
            Assert.That(GameSessionNavigator.TryEnterGameplay(), Is.False);
            Assert.That(GameSessionNavigator.TryContinue(root, 2), Is.False);
            Assert.That(GameSessionNavigator.TryReturnToMainMenu(), Is.False);
            Assert.That(GameSessionNavigator.IsTransitioning, Is.True);

            host.CompleteLoad();

            Assert.That(host.Loads, Is.EqualTo(new[] { GameSessionNavigator.GameplayScene }));
            Assert.That(GameSessionNavigator.IsTransitioning, Is.False);
            Assert.That(SaveSlotStorage.GetActiveSlot(root), Is.EqualTo(1), "A rejected Continue must not switch slots.");
        }

        [Test]
        public void ContinueMakesTheChosenSlotActiveBeforeLoading()
        {
            Assert.That(GameSessionNavigator.TryContinue(root, 2), Is.True);

            Assert.That(SaveSlotStorage.GetActiveSlot(root), Is.EqualTo(2));
            Assert.That(host.Loads.Single(), Is.EqualTo(GameSessionNavigator.GameplayScene));
        }

        [Test]
        public void GameplayThatCannotLoadItsSaveReturnsToTheMainMenuWithAMessage()
        {
            host.GameplayFailure = "The local checkpoint is invalid; the file has been preserved.";

            Assert.That(GameSessionNavigator.TryEnterGameplay(), Is.True);
            host.CompleteLoad();

            Assert.That(host.Loads, Is.EqualTo(new[] { GameSessionNavigator.GameplayScene, GameSessionNavigator.MainMenuScene }));
            Assert.That(GameSessionNavigator.IsTransitioning, Is.True, "Still locked until the menu is active.");
            host.CompleteLoad();
            Assert.That(GameSessionNavigator.IsTransitioning, Is.False);
            Assert.That(GameSessionNavigator.ConsumeMenuMessage(), Is.EqualTo(GameSessionNavigator.LoadFailedMessage));
            Assert.That(GameSessionNavigator.ConsumeMenuMessage(), Is.Null, "The message is shown once.");
        }

        [Test]
        public void AMissingSceneIsRejectedWithoutLockingNavigation()
        {
            host.Loadable.Remove(GameSessionNavigator.GameplayScene);

            Assert.That(GameSessionNavigator.TryContinue(root, 3), Is.False);
            Assert.That(GameSessionNavigator.IsTransitioning, Is.False);
            Assert.That(Directory.Exists(root), Is.False, "Continue must not touch the saves when gameplay cannot load.");

            host.Loadable.Remove(GameSessionNavigator.MainMenuScene);
            Assert.That(GameSessionNavigator.CanReturnToMainMenu, Is.False);
            Assert.That(GameSessionNavigator.TryReturnToMainMenu(), Is.False);
            Assert.That(host.Loads, Is.Empty);
        }

        [Test]
        public void ReturningToTheMainMenuUnlocksOnceItHasLoaded()
        {
            Assert.That(GameSessionNavigator.TryReturnToMainMenu(), Is.True);
            Assert.That(GameSessionNavigator.CanReturnToMainMenu, Is.False);
            host.CompleteLoad();
            Assert.That(GameSessionNavigator.CanReturnToMainMenu, Is.True);
            Assert.That(GameSessionNavigator.ConsumeMenuMessage(), Is.Null);
        }

        [Test]
        public void TheGameStartsOnTheMainMenu()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.That(scenes[0].path, Is.EqualTo(MainMenuPath));
            Assert.That(scenes[0].enabled, Is.True);
            Assert.That(scenes.Any(s => s.enabled && s.path == "Assets/_ResortEmpire/Scenes/Resort.unity"), Is.True);
            Assert.That(Path.GetFileNameWithoutExtension(scenes[0].path), Is.EqualTo(GameSessionNavigator.MainMenuScene));
        }

        [Test]
        public void MainMenuSceneIsWiredForMouseAndTouch()
        {
            var scene = EditorSceneManager.OpenPreviewScene(MainMenuPath);
            try
            {
                var roots = scene.GetRootGameObjects();
                var module = roots.SelectMany(r => r.GetComponentsInChildren<InputSystemUIInputModule>(true)).Single();
                Assert.That(module.actionsAsset, Is.Not.Null);
                Assert.That(module.point.action, Is.Not.Null);
                var menu = roots.SelectMany(r => r.GetComponentsInChildren<MainMenuController>(true)).Single();
                var serialized = new SerializedObject(menu);
                foreach (var field in new[] { "economy", "continueButton", "continueDetail", "newGameButton", "messageRow", "messageText",
                             "loadingIndicator", "confirmationDialog", "confirmationText", "confirmButton", "cancelButton" })
                    Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);

                foreach (var button in new[] { menu.ContinueButton, menu.NewGameButton, menu.ConfirmButton, menu.CancelButton })
                {
                    Assert.That(button.targetGraphic, Is.Not.Null, button.name);
                    AssertReceivesTaps(button);
                    Assert.That(button.GetComponentInParent<Canvas>(true).GetComponent<GraphicRaycaster>(), Is.Not.Null);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void HomeIslandOffersAMenuButtonThatReceivesTaps()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/_ResortEmpire/Prefabs/UI/HomeIslandView.prefab");
            try
            {
                var button = root.transform.Find("Top/MainMenuButton");
                Assert.That(button, Is.Not.Null);
                Assert.That(button.GetComponent<ReturnToMainMenuButton>(), Is.Not.Null);
                AssertReceivesTaps(button.GetComponent<Button>());
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void AssertReceivesTaps(Button button)
        {
            // The button's own Image is its tap area; a transparent one must not be culled, or raycasts skip it.
            var area = button.GetComponent<Image>();
            Assert.That(area, Is.Not.Null, button.name);
            Assert.That(area.raycastTarget, Is.True, button.name + " must receive taps.");
            if (area.color.a <= 0f)
                Assert.That(area.canvasRenderer.cullTransparentMesh, Is.False, button.name + " has a transparent tap area that is culled.");
        }

        private sealed class FakeHost : IGameSessionHost
        {
            private readonly Queue<Action> pending = new Queue<Action>();
            public readonly HashSet<string> Loadable = new HashSet<string> { GameSessionNavigator.MainMenuScene, GameSessionNavigator.GameplayScene };
            public readonly List<string> Loads = new List<string>();
            public string GameplayFailure;

            public bool CanLoad(string sceneName) => Loadable.Contains(sceneName);

            public bool TryLoad(string sceneName, Action loaded)
            {
                Loads.Add(sceneName);
                pending.Enqueue(loaded);
                return true;
            }

            public string FindGameplayFailure() => GameplayFailure;

            public void CompleteLoad() => pending.Dequeue()();
        }
    }
}
