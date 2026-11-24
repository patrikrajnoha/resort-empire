using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>Loads scenes for <see cref="GameSessionNavigator"/>. Replaced in tests.</summary>
    public interface IGameSessionHost
    {
        bool CanLoad(string sceneName);
        /// <summary>Starts loading <paramref name="sceneName"/> as the only scene and calls <paramref name="loaded"/> once it is active.</summary>
        bool TryLoad(string sceneName, Action loaded);
        /// <summary>Returns why the loaded gameplay scene is unusable, or null when its runtime is configured.</summary>
        string FindGameplayFailure();
    }

    /// <summary>
    /// The only path between the Main Menu and gameplay. One transition runs at a time, so repeated taps
    /// cannot load the gameplay scene twice. A gameplay scene whose save cannot be loaded returns to the
    /// Main Menu with a message instead of leaving the player in an unconfigured resort.
    /// </summary>
    public static class GameSessionNavigator
    {
        public const string MainMenuScene = "MainMenu";
        public const string GameplayScene = "Resort";
        public const string LoadFailedMessage =
            "Your saved game could not be loaded. The save file was kept unchanged.";

        private static IGameSessionHost host = new UnitySceneHost();
        private static string pendingMenuMessage;

        public static bool IsTransitioning { get; private set; }
        public static bool CanReturnToMainMenu => !IsTransitioning && host.CanLoad(MainMenuScene);

        /// <summary>Sets <paramref name="slotNumber"/> active, then loads gameplay, which opens the active slot.</summary>
        public static bool TryContinue(string root, int slotNumber)
        {
            if (IsTransitioning || !host.CanLoad(GameplayScene)) return false;
            SaveSlotStorage.SetActiveSlot(root, slotNumber);
            return TryEnterGameplay();
        }

        /// <summary>Loads gameplay for the active slot.</summary>
        public static bool TryEnterGameplay()
        {
            if (!Begin(GameplayScene)) return false;
            if (host.TryLoad(GameplayScene, VerifyGameplay)) return true;
            IsTransitioning = false;
            return false;
        }

        public static bool TryReturnToMainMenu(string message = null)
        {
            if (!Begin(MainMenuScene)) return false;
            if (message != null) pendingMenuMessage = message;
            if (host.TryLoad(MainMenuScene, Finish)) return true;
            IsTransitioning = false;
            return false;
        }

        /// <summary>Returns and clears the message the Main Menu should show after a failed session, or null.</summary>
        public static string ConsumeMenuMessage()
        {
            var message = pendingMenuMessage;
            pendingMenuMessage = null;
            return message;
        }

        /// <summary>Replaces the scene host until the returned scope is disposed. Intended for tests.</summary>
        public static IDisposable UseHost(IGameSessionHost replacement)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            var previous = host;
            host = replacement;
            IsTransitioning = false;
            pendingMenuMessage = null;
            return new HostScope(previous);
        }

        private static bool Begin(string sceneName)
        {
            if (IsTransitioning || !host.CanLoad(sceneName)) return false;
            IsTransitioning = true;
            // Debug speed controls change timeScale; a new session always starts at normal speed.
            Time.timeScale = 1f;
            return true;
        }

        private static void VerifyGameplay()
        {
            var failure = host.FindGameplayFailure();
            IsTransitioning = false;
            if (failure == null) return;
            Debug.LogWarning("Returning to the Main Menu: " + failure);
            if (!TryReturnToMainMenu(LoadFailedMessage))
                Debug.LogError("The Main Menu scene cannot be loaded; gameplay stays unconfigured.");
        }

        private static void Finish() => IsTransitioning = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            // Static state survives entering Play Mode when domain reload is disabled.
            host = new UnitySceneHost();
            IsTransitioning = false;
            pendingMenuMessage = null;
        }

        private sealed class HostScope : IDisposable
        {
            private IGameSessionHost previous;
            public HostScope(IGameSessionHost previous) { this.previous = previous; }

            public void Dispose()
            {
                if (previous == null) return;
                host = previous;
                previous = null;
                IsTransitioning = false;
                pendingMenuMessage = null;
            }
        }

        private sealed class UnitySceneHost : IGameSessionHost
        {
            public bool CanLoad(string sceneName) => Application.CanStreamedLevelBeLoaded(sceneName);

            public bool TryLoad(string sceneName, Action loaded)
            {
                var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                if (operation == null) return false;
                operation.completed += _ => loaded();
                return true;
            }

            public string FindGameplayFailure()
            {
                var gameplay = UnityEngine.Object.FindAnyObjectByType<ResortGameplayBootstrap>();
                if (gameplay == null) return "The gameplay scene has no ResortGameplayBootstrap.";
                return gameplay.IsConfigured ? null : gameplay.ConfigurationDiagnostic ?? "Gameplay is not configured.";
            }
        }
    }
}
