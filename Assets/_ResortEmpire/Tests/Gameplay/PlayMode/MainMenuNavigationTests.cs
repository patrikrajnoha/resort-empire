using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ResortEmpire.Gameplay.Tests
{
    /// <summary>
    /// Loads the real Main Menu and Resort scenes against a temporary save folder and taps buttons through the
    /// scene's EventSystem raycast, so a covered or non-raycastable button fails the test like a real tap would.
    /// </summary>
    public sealed class MainMenuNavigationTests
    {
        // Known Linux-Editor-only error: the ocean video does not import there (the static fallback shows).
        private const string AllowedError = "Ocean background video could not be prepared or played";
        private const float LoadTimeout = 30f;

        private string root;
        private IDisposable saveScope;
        private readonly List<string> errors = new List<string>();
        private int gameplayLoads;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "resort-menu-playmode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            saveScope = ResortSaveLocation.Redirect(root);
            errors.Clear();
            gameplayLoads = 0;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += CollectErrors;
            SceneManager.sceneLoaded += CountGameplayLoads;
            yield return LoadMainMenu();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Leave gameplay before the save folder disappears, so nothing saves into a deleted folder.
            if (SceneManager.GetActiveScene().name != GameSessionNavigator.MainMenuScene)
                yield return LoadMainMenu();
            SceneManager.sceneLoaded -= CountGameplayLoads;
            Application.logMessageReceived -= CollectErrors;
            LogAssert.ignoreFailingMessages = false;
            saveScope.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Assert.That(errors, Is.Empty, "Unexpected errors were logged.");
        }

        [UnityTest]
        public IEnumerator NewGameFromAFreshProfileStartsWaveOneAndMenuReturnsToTheMainMenu()
        {
            IgnoreKnownErrors();
            var menu = Menu();
            Assert.That(menu.ContinueButton.interactable, Is.False);
            Assert.That(menu.ContinueDetail, Is.EqualTo("No saved resort yet"));
            Assert.That(menu.Message, Is.Null);
            Assert.That(menu.NewGameButton.interactable, Is.True);

            Tap(menu.NewGameButton);
            Assert.That(menu.IsConfirmationOpen, Is.False, "Without a save there is nothing to confirm.");
            yield return WaitForGameplay();

            var game = Gameplay();
            Assert.That(game.IsConfigured, Is.True);
            Assert.That(game.CurrentPhase, Is.EqualTo(GamePhase.AWAITING_WAVE));
            Assert.That(game.CompletedWaves, Is.EqualTo(0));
            Assert.That(game.NextWaveForecast.WaveId, Is.EqualTo(1));
            Assert.That(game.Resources.Coins, Is.EqualTo(0));
            Assert.That(File.Exists(SaveSlotStorage.GetSlotPath(root, 1)), Is.True, "The new game is saved straight away.");

            Tap(MenuButton());
            yield return WaitForMainMenu();

            menu = Menu();
            Assert.That(menu.ContinueButton.interactable, Is.True);
            Assert.That(menu.ContinueDetail, Does.StartWith("Wave 1"));
            Assert.That(gameplayLoads, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ContinueReopensTheMostRecentSavedResort()
        {
            IgnoreKnownErrors();
            Tap(Menu().NewGameButton);
            yield return WaitForGameplay();
            var game = Gameplay();
            var profile = Profile(1);
            Assert.That(game.TryDebugAddResources(300, 0).Succeeded, Is.True);
            Assert.That(game.TryUpgradeBuilding(BuildingId.Reception).Succeeded, Is.True, "Upgrades save their checkpoint.");

            Tap(MenuButton());
            yield return WaitForMainMenu();
            Tap(Menu().ContinueButton);
            yield return WaitForGameplay();

            game = Gameplay();
            Assert.That(game.IsConfigured, Is.True);
            Assert.That(Profile(1), Is.EqualTo(profile));
            Assert.That(game.Resources.Coins, Is.EqualTo(100));
            Assert.That(game.Buildings[(int)BuildingId.Reception].Level, Is.EqualTo(2));
            Assert.That(UnityEngine.Object.FindObjectsByType<ResortGameplayBootstrap>(FindObjectsInactive.Include).Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RepeatedTapsStartOnlyOneGameplaySession()
        {
            IgnoreKnownErrors();
            var menu = Menu();
            Tap(menu.NewGameButton);
            Assert.That(menu.IsBusy, Is.True);
            Assert.That(menu.NewGameButton.interactable, Is.False);
            Assert.That(menu.ContinueButton.interactable, Is.False);
            TapIgnoringResult(menu.NewGameButton);
            TapIgnoringResult(menu.ContinueButton);
            Assert.That(GameSessionNavigator.TryEnterGameplay(), Is.False);

            yield return WaitForGameplay();
            for (var frame = 0; frame < 10; frame++) yield return null;

            Assert.That(gameplayLoads, Is.EqualTo(1));
            Assert.That(UnityEngine.Object.FindObjectsByType<ResortGameplayBootstrap>(FindObjectsInactive.Include).Length, Is.EqualTo(1));
            Assert.That(Gameplay().IsConfigured, Is.True);

            var menuButton = MenuButton();
            Tap(menuButton);
            TapIgnoringResult(menuButton);
            yield return WaitForMainMenu();
            Assert.That(SceneManager.sceneCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator NewGameOverAnExistingResortAsksFirstAndCancelKeepsIt()
        {
            IgnoreKnownErrors();
            Assert.That(MainMenuSaves.TryCreateNewGame(root, 1, Economy(), out var error), Is.True, error);
            var original = File.ReadAllText(SaveSlotStorage.GetSlotPath(root, 1));
            yield return LoadMainMenu();
            var menu = Menu();

            Tap(menu.NewGameButton);
            Assert.That(menu.IsConfirmationOpen, Is.True);
            Assert.That(menu.ContinueButton.interactable, Is.False, "The menu behind the dialog is locked.");
            Assert.That(menu.NewGameButton.interactable, Is.False);
            yield return null;
            Assert.That(TopHandler(Centre(menu.ContinueButton)), Is.Not.EqualTo(menu.ContinueButton.gameObject), "The dialog covers the menu.");
            Tap(menu.CancelButton);
            Assert.That(menu.IsConfirmationOpen, Is.False);
            Assert.That(File.ReadAllText(SaveSlotStorage.GetSlotPath(root, 1)), Is.EqualTo(original));
            Assert.That(menu.ContinueButton.interactable, Is.True);

            Tap(menu.NewGameButton);
            yield return null;
            Tap(menu.ConfirmButton);
            yield return WaitForGameplay();

            Assert.That(Gameplay().IsConfigured, Is.True);
            Assert.That(Profile(1), Is.Not.EqualTo(ProfileOf(original)), "Confirming replaces the resort with a new one.");
        }

        [UnityTest]
        public IEnumerator AnUnreadableSaveDisablesContinueAndNewGameReplacesItAfterConfirmation()
        {
            IgnoreKnownErrors();
            WriteGarbage(1);
            yield return LoadMainMenu();
            var menu = Menu();

            Assert.That(menu.ContinueButton.interactable, Is.False);
            Assert.That(menu.Message, Is.EqualTo(MainMenuSaves.UnreadableNotice));
            Assert.That(menu.NewGameButton.interactable, Is.True);

            Tap(menu.NewGameButton);
            Assert.That(menu.IsConfirmationOpen, Is.True);
            yield return null;
            Tap(menu.ConfirmButton);
            yield return WaitForGameplay();
            Assert.That(Gameplay().IsConfigured, Is.True);
        }

        [UnityTest]
        public IEnumerator ASaveThatCannotBeLoadedReturnsToAUsableMainMenu()
        {
            IgnoreKnownErrors();
            Assert.That(MainMenuSaves.TryCreateNewGame(root, 1, Economy(), out var error), Is.True, error);
            yield return LoadMainMenu();
            var menu = Menu();
            Assert.That(menu.ContinueButton.interactable, Is.True);

            // The file breaks after the menu checked it, e.g. a failed write by another process.
            WriteGarbage(1);
            Tap(menu.ContinueButton);
            yield return WaitUntil(() => gameplayLoads == 1, "the gameplay scene to load");
            yield return WaitForMainMenu();

            menu = Menu();
            Assert.That(menu.Message, Is.EqualTo(GameSessionNavigator.LoadFailedMessage));
            Assert.That(menu.ContinueButton.interactable, Is.False);
            Assert.That(menu.NewGameButton.interactable, Is.True);
            Assert.That(File.ReadAllText(SaveSlotStorage.GetSlotPath(root, 1)), Is.EqualTo("not json"), "The broken save is kept.");

            Tap(menu.NewGameButton);
            yield return null;
            Tap(menu.ConfirmButton);
            yield return WaitForGameplay();
            Assert.That(Gameplay().IsConfigured, Is.True);
        }

        [UnityTest]
        public IEnumerator TheMenuButtonIsOnlyOfferedBetweenWaves()
        {
            IgnoreKnownErrors();
            Tap(Menu().NewGameButton);
            yield return WaitForGameplay();
            var button = MenuButton();
            Assert.That(button.GetComponent<ReturnToMainMenuButton>().IsAvailable, Is.True);

            Assert.That(Gameplay().TryStartWave().Succeeded, Is.True);
            yield return null;

            var group = button.GetComponent<CanvasGroup>();
            Assert.That(button.GetComponent<ReturnToMainMenuButton>().IsAvailable, Is.False);
            Assert.That(group.alpha, Is.EqualTo(0f));
            Assert.That(group.blocksRaycasts, Is.False);
            Assert.That(group.interactable, Is.False);
        }

        [UnityTest]
        public IEnumerator ResortWaveOneShowsServiceNeedsAndClaimsRewardsOnceThroughTheUi()
        {
            Tap(Menu().NewGameButton);
            yield return WaitForGameplay();
            var game = Gameplay();
            Assert.That(game.IsConfigured, Is.True);
            var initialResources = game.Resources;

            Tap(SceneComponent<Button>("StartWaveButton"));
            game.AdvanceSimulation(2d);
            yield return null;

            Assert.That(game.CurrentPhase, Is.EqualTo(GamePhase.WAVE_ACTIVE));
            var needs = SceneComponent<RectTransform>("WaveNeedsPanel");
            Assert.That(needs.gameObject.activeInHierarchy, Is.True);
            Assert.That(needs.GetComponentsInChildren<TextMeshProUGUI>().Select(label => label.text),
                Does.Contain("SERVED 0/36"));
            var rooms = needs.GetComponentsInChildren<Button>().Single(button => button.name == "RoomsCard");
            Assert.That(rooms.GetComponentsInChildren<TextMeshProUGUI>().Select(label => label.text),
                Does.Contain("0/12"));

            // Run the same scene simulation to completion without a real-time wait for service.
            game.AdvanceSimulation(10000d);
            yield return null;
            Assert.That(game.CurrentPhase, Is.EqualTo(GamePhase.WAVE_COMPLETE));
            Assert.That(needs.gameObject.activeInHierarchy, Is.False);
            var result = game.CurrentWaveResult;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.WaveId, Is.EqualTo(1));
            Assert.That(result.ServedUnits, Is.EqualTo(36));
            Assert.That(result.Claimed, Is.False);
            Assert.That(game.Resources, Is.EqualTo(initialResources));
            var results = SceneComponent<RectTransform>("WaveComplete");
            var claim = results.GetComponentsInChildren<Button>().Single(button => button.name == "ClaimRewardsButton");
            Assert.That(results.GetComponentsInChildren<TextMeshProUGUI>().Select(label => label.text),
                Does.Contain("36/36"));
            var claimPosition = Centre(claim);
            var walletUpdates = new List<ResourceSnapshot>();
            game.ResourcesChanged += walletUpdates.Add;

            Tap(claim);
            Click(claimPosition);
            yield return null;

            Assert.That(game.CurrentPhase, Is.EqualTo(GamePhase.UPGRADE));
            Assert.That(SceneComponent<RectTransform>("UpgradeSuggestionPanel").gameObject.activeInHierarchy, Is.True);
            Assert.That(claim.gameObject.activeInHierarchy, Is.False);
            Assert.That(game.CompletedWaves, Is.EqualTo(1));
            var expected = new ResourceSnapshot(initialResources.Coins + result.Rewards.Coins,
                initialResources.Energy + result.Rewards.Energy, initialResources.ResortValue + result.Rewards.ResortValue);
            Assert.That(walletUpdates, Is.EqualTo(new[] { expected }));
            Assert.That(game.Resources, Is.EqualTo(expected));
            Assert.That(SceneComponent<TextMeshProUGUI>("CoinsText").text, Is.EqualTo(expected.Coins.ToString()));
            Assert.That(SceneComponent<TextMeshProUGUI>("EnergyText").text, Is.EqualTo(expected.Energy.ToString()));
            Assert.That(SceneComponent<TextMeshProUGUI>("ResortValueText").text, Is.EqualTo("Resort Value: " + expected.ResortValue));
            var save = SaveSlotStorage.CreateActivePersistence(root).Load();
            Assert.That(save.ClaimedRewardIds, Is.EqualTo(new[] { result.RewardId }));
            Assert.That(new ResourceSnapshot(save.Coins, save.Energy, save.ResortValue), Is.EqualTo(expected));
        }

        [UnityTest]
        public IEnumerator LayoutIsReadableAndTappableOnAndroidLandscapeScreens()
        {
            IgnoreKnownErrors();
            var menu = Menu();
            var canvas = menu.GetComponent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>();
            var dialog = menu.transform.Find("SafeArea/NewGameConfirmation").gameObject;
            var messageRow = menu.transform.Find("SafeArea/MenuPanel/MessageRow").gameObject;
            messageRow.SetActive(true);
            messageRow.GetComponentInChildren<TextMeshProUGUI>().text = MainMenuSaves.UnreadableNotice;
            var sizes = new[]
            {
                new Vector2Int(1280, 720), new Vector2Int(1600, 720), new Vector2Int(1920, 1080), new Vector2Int(2340, 1080),
                new Vector2Int(2400, 1080), new Vector2Int(3200, 1440), new Vector2Int(2048, 1536), new Vector2Int(2560, 1600)
            };
            var camera = new GameObject("LayoutCamera").AddComponent<Camera>();
            try
            {
                foreach (var size in sizes)
                foreach (var withDialog in new[] { false, true })
                {
                    dialog.SetActive(withDialog);
                    var texture = new RenderTexture(size.x, size.y, 24);
                    camera.targetTexture = texture;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1;
                    scaler.enabled = false;
                    // The same factor CanvasScaler computes for this screen.
                    var scale = Mathf.Pow(2, Mathf.Lerp(Mathf.Log(size.x / scaler.referenceResolution.x, 2),
                        Mathf.Log(size.y / scaler.referenceResolution.y, 2), scaler.matchWidthOrHeight));
                    canvas.scaleFactor = scale;
                    yield return null;
                    Canvas.ForceUpdateCanvases();

                    var label = size.x + "x" + size.y + (withDialog ? " with dialog" : string.Empty);
                    var buttons = withDialog ? new[] { menu.CancelButton, menu.ConfirmButton } : new[] { menu.ContinueButton, menu.NewGameButton };
                    var rects = new List<Rect>();
                    foreach (var button in buttons)
                    {
                        var rect = ScreenRect((RectTransform)button.transform, camera);
                        Assert.That(new Rect(0, 0, size.x, size.y).Contains(rect.min) && new Rect(0, 0, size.x, size.y).Contains(rect.max),
                            Is.True, button.name + " is on screen at " + label);
                        // Android asks for 48 dp touch targets. Assume the smallest common density for the shape:
                        // a phone is about 360 dp tall in landscape, a 4:3 or 16:10 tablet about 600 dp.
                        var minimum = 48f * size.y / ((float)size.x / size.y >= 1.7f ? 360f : 600f);
                        Assert.That(rect.height, Is.GreaterThanOrEqualTo(minimum), button.name + " is 48 dp tall at " + label);
                        Assert.That(rect.width, Is.GreaterThanOrEqualTo(minimum), button.name + " is 48 dp wide at " + label);
                        Assert.That(rects.Any(r => r.Overlaps(rect)), Is.False, button.name + " overlaps another button at " + label);
                        rects.Add(rect);
                        var hit = TopHandler(rect.center);
                        Assert.That(hit, Is.EqualTo(button.gameObject), button.name + " receives a tap at its centre at " + label);
                    }

                    foreach (var text in menu.GetComponentsInChildren<TextMeshProUGUI>(false))
                    {
                        if (!text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text)) continue;
                        text.ForceMeshUpdate();
                        Assert.That(text.isTextOverflowing, Is.False, text.name + " fits at " + label);
                        Assert.That(text.fontSize * scale, Is.GreaterThanOrEqualTo(size.y * 0.018f), text.name + " is readable at " + label);
                    }
                    camera.targetTexture = null;
                    texture.Release();
                    UnityEngine.Object.Destroy(texture);
                }
            }
            finally
            {
                dialog.SetActive(false);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                scaler.enabled = true;
                UnityEngine.Object.Destroy(camera.gameObject);
            }
        }

        /// <summary>
        /// The test framework resets this flag per test; <see cref="CollectErrors"/> still fails on any unexpected error.
        /// </summary>
        private static void IgnoreKnownErrors() => LogAssert.ignoreFailingMessages = true;

        private static MainMenuController Menu()
        {
            var menu = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
            Assert.That(menu, Is.Not.Null, "The Main Menu is not loaded.");
            return menu;
        }

        private static ResortGameplayBootstrap Gameplay()
        {
            var game = UnityEngine.Object.FindAnyObjectByType<ResortGameplayBootstrap>();
            Assert.That(game, Is.Not.Null, "The gameplay scene is not loaded.");
            return game;
        }

        private static T SceneComponent<T>(string objectName) where T : Component
        {
            return SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(sceneRoot => sceneRoot.GetComponentsInChildren<T>())
                .Single(component => component.gameObject.activeInHierarchy && component.name == objectName);
        }

        private static Button MenuButton()
        {
            var menu = UnityEngine.Object.FindAnyObjectByType<ReturnToMainMenuButton>();
            Assert.That(menu, Is.Not.Null, "S1 has no MENU button.");
            return menu.GetComponent<Button>();
        }

        private static ProgressionConfig Economy() =>
            UnityEngine.Object.FindAnyObjectByType<MainMenuController>() != null
                ? UnityEngine.Resources.FindObjectsOfTypeAll<ResortEconomyConfig>().First().CreateRuntimeConfig()
                : throw new InvalidOperationException("Load the Main Menu first.");

        private string Profile(int slot) => new JsonFileProgressionPersistence(SaveSlotStorage.GetSlotPath(root, slot)).Load().ProfileId;
        private static string ProfileOf(string json) => JsonUtility.FromJson<ProgressionSaveData>(json).ProfileId;

        private void WriteGarbage(int slot)
        {
            var path = SaveSlotStorage.GetSlotPath(root, slot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "not json");
        }

        private static void Tap(Button button)
        {
            Assert.That(button.gameObject.activeInHierarchy, Is.True, button.name + " is not visible.");
            Assert.That(button.IsInteractable(), Is.True, button.name + " is not interactable.");
            var target = Centre(button);
            Assert.That(TopHandler(target), Is.EqualTo(button.gameObject), button.name + " does not receive a tap at its centre.");
            Click(target);
        }

        private static void TapIgnoringResult(Button button) => Click(Centre(button));

        private static void Click(Vector2 position)
        {
            var data = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left };
            var handler = TopHandler(position);
            if (handler == null) return;
            data.pointerPress = handler;
            data.pointerClick = handler;
            ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerClickHandler);
        }

        private static GameObject TopHandler(Vector2 position)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            return hits.Count == 0 ? null : ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
        }

        private static Vector2 Centre(Button button)
        {
            var canvas = button.GetComponentInParent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return ScreenRect((RectTransform)button.transform, camera).center;
        }

        private static Rect ScreenRect(RectTransform rect, Camera camera)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private IEnumerator LoadMainMenu()
        {
            SceneManager.LoadScene(GameSessionNavigator.MainMenuScene);
            yield return WaitForMainMenu();
        }

        private static IEnumerator WaitForMainMenu()
        {
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == GameSessionNavigator.MainMenuScene
                && UnityEngine.Object.FindAnyObjectByType<MainMenuController>() != null && !GameSessionNavigator.IsTransitioning,
                "the Main Menu");
            yield return null;
        }

        private static IEnumerator WaitForGameplay()
        {
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == GameSessionNavigator.GameplayScene
                && !GameSessionNavigator.IsTransitioning, "the gameplay scene");
            yield return null;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartup + LoadTimeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("Timed out waiting for " + what + ".");
                yield return null;
            }
        }

        private void CountGameplayLoads(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == GameSessionNavigator.GameplayScene) gameplayLoads++;
        }

        private void CollectErrors(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (message.StartsWith(AllowedError, StringComparison.Ordinal)) return;
            errors.Add(type + ": " + message + "\n" + stackTrace);
        }
    }
}
