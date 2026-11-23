using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;

namespace ResortEmpire.Gameplay.UI
{
    public class PauseMenuController : MonoBehaviour
    {
        [Header("Gameplay")]
        [SerializeField] private ResortGameplayBootstrap gameplay;

        [Header("Pause Menu")]
        [SerializeField] private Button pauseButton;
        [SerializeField] private GameObject pauseOverlay;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button mainMenuButton;

        [Header("Settings")]
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private Button backButton;
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private TMP_Dropdown languageDropdown;
        [SerializeField] private Button developerModeButton;

        [Header("Audio - Optional")]
        [SerializeField] private AudioMixer audioMixer;
        [SerializeField] private string musicParameter = "MusicVolume";
        [SerializeField] private string sfxParameter = "SfxVolume";

        [Header("Text")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI warningText;

        private const string MusicKey = "MusicVolume";
        private const string SfxKey = "SfxVolume";
        private const string LanguageKey = "GameLanguage";

        private TextMeshProUGUI resumeLabel;
        private TextMeshProUGUI menuLabel;

        private bool isPaused;
        private bool pausedWaveByMenu;
        private bool confirmingExit;

        private float previousTimeScale = 1f;

        private void Awake()
        {
            if (gameplay == null)
                gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();

            resumeLabel = resumeButton.GetComponentInChildren<TextMeshProUGUI>(true);
            menuLabel = mainMenuButton.GetComponentInChildren<TextMeshProUGUI>(true);

            // Pause menu buttons
            pauseButton.onClick.AddListener(OpenPauseMenu);
            resumeButton.onClick.AddListener(OnResumePressed);
            settingsButton.onClick.AddListener(OpenSettings);
            mainMenuButton.onClick.AddListener(OnMainMenuPressed);

            // Settings buttons
            backButton.onClick.AddListener(CloseSettings);

            // Volume sliders
            musicSlider.onValueChanged.AddListener(SetMusicVolume);
            sfxSlider.onValueChanged.AddListener(SetSfxVolume);

            // Language selection
            languageDropdown.onValueChanged.AddListener(SetLanguage);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            developerModeButton.onClick.AddListener(OpenDeveloperMode);
#else
            developerModeButton.gameObject.SetActive(false);
#endif

            LoadSettings();

            settingsPanel.SetActive(false);
            pausePanel.SetActive(true);
            pauseOverlay.SetActive(false);

            ResetMenu();
        }

        // =====================================
        // PAUSE
        // =====================================

        public void OpenPauseMenu()
        {
            if (isPaused || GameSessionNavigator.IsTransitioning)
                return;

            if (gameplay == null)
                gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();

            pausedWaveByMenu = false;

            if (gameplay != null &&
                gameplay.CurrentPhase == GamePhase.WAVE_ACTIVE)
            {
                var result = gameplay.TryPauseWave();

                if (!result.Succeeded)
                {
                    Debug.LogWarning(result.Message);
                    return;
                }

                pausedWaveByMenu = true;
            }

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            isPaused = true;

            ResetMenu();

            settingsPanel.SetActive(false);
            pausePanel.SetActive(true);
            pauseOverlay.SetActive(true);
        }

        private void OnResumePressed()
        {
            if (confirmingExit)
            {
                ResetMenu();
                return;
            }

            ResumeGame();
        }

        public void ResumeGame()
        {
            if (!isPaused)
                return;

            if (pausedWaveByMenu)
            {
                var result = gameplay.TryResumeWave();

                if (!result.Succeeded)
                {
                    ShowWarning(result.Message);
                    return;
                }
            }

            pausedWaveByMenu = false;
            isPaused = false;

            Time.timeScale = previousTimeScale;

            settingsPanel.SetActive(false);
            pausePanel.SetActive(true);
            pauseOverlay.SetActive(false);
        }

        // =====================================
        // SETTINGS
        // =====================================

        public void OpenSettings()
        {
            if (!isPaused)
                return;

            pausePanel.SetActive(false);
            settingsPanel.SetActive(true);
        }

        public void CloseSettings()
        {
            settingsPanel.SetActive(false);
            pausePanel.SetActive(true);
        }

        private void LoadSettings()
        {
            float music = PlayerPrefs.GetFloat(MusicKey, 1f);
            float sfx = PlayerPrefs.GetFloat(SfxKey, 1f);

            musicSlider.SetValueWithoutNotify(music);
            sfxSlider.SetValueWithoutNotify(sfx);

            ApplyVolume(musicParameter, music);
            ApplyVolume(sfxParameter, sfx);

            // Language selection is prepared for future localization.
            languageDropdown.ClearOptions();

            languageDropdown.AddOptions(new List<string>
            {
                "English",
                "Slovenčina"
            });

            int language = PlayerPrefs.GetInt(LanguageKey, 0);

            languageDropdown.SetValueWithoutNotify(
                Mathf.Clamp(language, 0, 1)
            );

            languageDropdown.RefreshShownValue();
        }

        public void SetMusicVolume(float value)
        {
            PlayerPrefs.SetFloat(MusicKey, value);
            PlayerPrefs.Save();

            ApplyVolume(musicParameter, value);
        }

        public void SetSfxVolume(float value)
        {
            PlayerPrefs.SetFloat(SfxKey, value);
            PlayerPrefs.Save();

            ApplyVolume(sfxParameter, value);
        }

        private void ApplyVolume(string parameter, float value)
        {
            // Audio Mixer can be assigned when audio is implemented.
            if (audioMixer == null)
                return;

            float decibels = value <= 0.0001f
                ? -80f
                : Mathf.Log10(value) * 20f;

            audioMixer.SetFloat(parameter, decibels);
        }

        public void SetLanguage(int index)
        {
            PlayerPrefs.SetInt(LanguageKey, index);
            PlayerPrefs.Save();

            // Actual translations will be connected later.
        }

        // =====================================
        // DEVELOPER MODE
        // =====================================

        public void OpenDeveloperMode()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var debugPanel = FindFirstObjectByType<DebugModePanel>();

            if (debugPanel == null)
            {
                Debug.LogWarning(
                    "Developer Mode panel was not found."
                );
                return;
            }

            debugPanel.OpenFromSettings();
#endif
        }

        // =====================================
        // MAIN MENU
        // =====================================

        private void OnMainMenuPressed()
        {
            if (!isPaused || GameSessionNavigator.IsTransitioning)
                return;

            if (confirmingExit)
            {
                ReturnToMainMenu();
                return;
            }

            if (gameplay != null && gameplay.IsConfigured)
            {
                var result = gameplay.TrySaveProgress();

                if (!result.Succeeded)
                {
                    ShowExitConfirmation(result.Message);
                    return;
                }
            }

            ReturnToMainMenu();
        }

        private void ShowExitConfirmation(string reason)
        {
            confirmingExit = true;

            titleText.text = "UNSAVED PROGRESS";

            warningText.text =
                "Leaving may discard unsaved progress.\n" +
                reason;

            warningText.gameObject.SetActive(true);

            if (resumeLabel != null)
                resumeLabel.text = "CANCEL";

            if (menuLabel != null)
                menuLabel.text = "LEAVE";
        }

        private void ReturnToMainMenu()
        {
            if (!GameSessionNavigator.TryReturnToMainMenu())
            {
                // Keep the simulation frozen if navigation fails.
                Time.timeScale = 0f;

                titleText.text = "NAVIGATION FAILED";

                ShowWarning("Could not open the Main Menu.");
            }
        }

        private void ShowWarning(string message)
        {
            if (warningText == null)
                return;

            warningText.text = message;
            warningText.gameObject.SetActive(true);
        }

        private void ResetMenu()
        {
            confirmingExit = false;

            if (titleText != null)
                titleText.text = "GAME PAUSED";

            if (warningText != null)
                warningText.gameObject.SetActive(false);

            if (resumeLabel != null)
                resumeLabel.text = "RESUME";

            if (menuLabel != null)
                menuLabel.text = "MAIN MENU";
        }

        private void OnDestroy()
        {
            if (pauseButton != null)
                pauseButton.onClick.RemoveListener(OpenPauseMenu);

            if (resumeButton != null)
                resumeButton.onClick.RemoveListener(OnResumePressed);

            if (settingsButton != null)
                settingsButton.onClick.RemoveListener(OpenSettings);

            if (mainMenuButton != null)
                mainMenuButton.onClick.RemoveListener(OnMainMenuPressed);

            if (backButton != null)
                backButton.onClick.RemoveListener(CloseSettings);

            if (musicSlider != null)
                musicSlider.onValueChanged.RemoveListener(SetMusicVolume);

            if (sfxSlider != null)
                sfxSlider.onValueChanged.RemoveListener(SetSfxVolume);

            if (languageDropdown != null)
                languageDropdown.onValueChanged.RemoveListener(SetLanguage);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (developerModeButton != null)
                developerModeButton.onClick.RemoveListener(OpenDeveloperMode);
#endif

            if (isPaused && !GameSessionNavigator.IsTransitioning)
                Time.timeScale = previousTimeScale;
        }
    }
}