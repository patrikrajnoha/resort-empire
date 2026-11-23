using System;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// The Main Menu: Continue opens the most recent valid save, New Game starts a fresh resort in the active
    /// slot (after confirmation when that replaces a save). Every button locks while a scene is loading.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private ResortEconomyConfig economy;
        [SerializeField] private Button continueButton;
        [SerializeField] private TextMeshProUGUI continueDetail;
        [SerializeField] private Button newGameButton;
        [SerializeField] private GameObject messageRow;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private GameObject loadingIndicator;
        [SerializeField] private GameObject confirmationDialog;
        [SerializeField] private TextMeshProUGUI confirmationText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;

        private MainMenuSaveSummary summary;
        private string sessionMessage;
        private string actionMessage;
        private bool wasBusy;

        public Button ContinueButton => continueButton;
        public Button NewGameButton => newGameButton;
        public Button ConfirmButton => confirmButton;
        public Button CancelButton => cancelButton;
        public bool IsConfirmationOpen => confirmationDialog != null && confirmationDialog.activeSelf;
        public bool IsBusy => GameSessionNavigator.IsTransitioning;
        public string ContinueDetail => continueDetail == null ? null : continueDetail.text;
        public string Message => messageRow != null && messageRow.activeSelf && messageText != null ? messageText.text : null;
        public MainMenuSaveSummary Summary => summary;

        private void Awake()
        {
            if (continueButton != null) continueButton.onClick.AddListener(Continue);
            if (newGameButton != null) newGameButton.onClick.AddListener(RequestNewGame);
            if (confirmButton != null) confirmButton.onClick.AddListener(ConfirmNewGame);
            if (cancelButton != null) cancelButton.onClick.AddListener(CancelNewGame);
        }

        private void OnDestroy()
        {
            if (continueButton != null) continueButton.onClick.RemoveListener(Continue);
            if (newGameButton != null) newGameButton.onClick.RemoveListener(RequestNewGame);
            if (confirmButton != null) confirmButton.onClick.RemoveListener(ConfirmNewGame);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(CancelNewGame);
        }

        private void OnEnable()
        {
            Time.timeScale = 1f;
            sessionMessage = GameSessionNavigator.ConsumeMenuMessage();
            SetConfirmationOpen(false);
            wasBusy = IsBusy;
            Refresh();
        }

        private void Update()
        {
            // Android back closes the confirmation, like its CANCEL button.
            if (IsConfirmationOpen && !IsBusy && Input.GetKeyDown(KeyCode.Escape)) CancelNewGame();
            // The menu can appear while the transition that brought it here is still finishing
            // (a failed gameplay load); show its message once that transition ends.
            if (wasBusy != IsBusy)
            {
                wasBusy = IsBusy;
                UpdateMessage();
            }
            ApplyInteractivity();
        }

        /// <summary>Re-reads the saves on disk and updates both buttons and the message.</summary>
        public void Refresh()
        {
            actionMessage = null;
            if (economy == null)
            {
                summary = null;
                ShowContinue("Game data is missing");
                ShowMessage("The game configuration is missing. Reinstall the game.", true);
                ApplyInteractivity();
                return;
            }

            try { summary = MainMenuSaves.Read(ResortSaveLocation.Root, economy.CreateRuntimeConfig()); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is ArgumentException || exception is InvalidOperationException)
            {
                summary = null;
                ShowContinue("Saves could not be read");
                ShowMessage("Your saves could not be read: " + exception.Message, true);
                ApplyInteractivity();
                return;
            }

            switch (summary.ContinueState)
            {
                case MainMenuContinueState.Available:
                    ShowContinue(DescribeSave(summary));
                    break;
                case MainMenuContinueState.Unreadable:
                    ShowContinue("Save could not be loaded");
                    break;
                default:
                    ShowContinue("No saved resort yet");
                    break;
            }
            UpdateMessage();
            ApplyInteractivity();
        }

        private void Continue()
        {
            if (IsBusy || IsConfirmationOpen || summary == null || !summary.CanContinue) return;
            try
            {
                if (!GameSessionNavigator.TryContinue(ResortSaveLocation.Root, summary.ContinueSlot.SlotNumber))
                    Fail("The resort could not be opened. Please try again.");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Fail("Your save could not be selected: " + exception.Message);
            }
            ApplyInteractivity();
        }

        private void RequestNewGame()
        {
            if (IsBusy || IsConfirmationOpen || summary == null) return;
            if (!summary.NewGameReplacesSave) { StartNewGame(); return; }

            confirmationText.text = summary.NewGameSlot.Status == SaveSlotStatus.Corrupted
                ? "Start a new game?\nYour unreadable save will be replaced."
                : "Start a new game?\nYour resort (" + WaveLabel(summary.NewGameSlot.CurrentWave, summary.TotalWaves)
                  + ") will be replaced. This cannot be undone.";
            SetConfirmationOpen(true);
            ApplyInteractivity();
        }

        private void ConfirmNewGame()
        {
            if (IsBusy || !IsConfirmationOpen) return;
            SetConfirmationOpen(false);
            StartNewGame();
        }

        private void CancelNewGame()
        {
            if (IsBusy) return;
            SetConfirmationOpen(false);
            ApplyInteractivity();
        }

        private void StartNewGame()
        {
            if (!MainMenuSaves.TryCreateNewGame(ResortSaveLocation.Root, summary.NewGameSlot.SlotNumber, economy.CreateRuntimeConfig(), out var error))
            {
                Refresh();
                Fail(error);
                return;
            }
            if (!GameSessionNavigator.TryEnterGameplay())
            {
                Refresh();
                Fail("The resort could not be opened. Please try again.");
                return;
            }
            UpdateMessage();
            ApplyInteractivity();
        }

        private void Fail(string message)
        {
            actionMessage = message;
            UpdateMessage();
            ApplyInteractivity();
        }

        private void ApplyInteractivity()
        {
            var idle = !IsBusy && !IsConfirmationOpen;
            if (continueButton != null) continueButton.interactable = idle && summary != null && summary.CanContinue;
            if (newGameButton != null) newGameButton.interactable = idle && summary != null;
            if (confirmButton != null) confirmButton.interactable = !IsBusy;
            if (cancelButton != null) cancelButton.interactable = !IsBusy;
            if (loadingIndicator != null && loadingIndicator.activeSelf != IsBusy) loadingIndicator.SetActive(IsBusy);
        }

        private void UpdateMessage()
        {
            if (IsBusy) { ShowMessage(null, false); return; }
            if (actionMessage != null) { ShowMessage(actionMessage, true); return; }
            if (sessionMessage != null) { ShowMessage(sessionMessage, true); return; }
            ShowMessage(summary?.Notice, true);
        }

        private void ShowMessage(string text, bool visible)
        {
            var show = visible && !string.IsNullOrEmpty(text);
            if (messageRow != null) messageRow.SetActive(show);
            if (messageText != null) messageText.text = show ? text : string.Empty;
        }

        private void ShowContinue(string detail)
        {
            if (continueDetail != null) continueDetail.text = detail;
        }

        private void SetConfirmationOpen(bool open)
        {
            if (confirmationDialog != null) confirmationDialog.SetActive(open);
        }

        private static string DescribeSave(MainMenuSaveSummary save)
        {
            var saved = save.ContinueSlot.LastSavedUtc.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);
            return WaveLabel(save.ContinueSlot.CurrentWave, save.TotalWaves) + "  ·  saved " + saved;
        }

        private static string WaveLabel(int wave, int totalWaves) =>
            wave > totalWaves ? "All " + totalWaves + " waves" : "Wave " + wave;
    }
}
