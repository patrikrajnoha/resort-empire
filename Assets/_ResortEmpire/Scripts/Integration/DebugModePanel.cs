#if UNITY_EDITOR || DEVELOPMENT_BUILD
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace ResortEmpire.Gameplay.Integration
{
    [DefaultExecutionOrder(-1000)]
    public sealed class DebugModePanel : MonoBehaviour
    {
        public GameObject panel;
        public GameObject modalOverlay;
        public TMP_InputField input;
        public TextMeshProUGUI output;
        public Button executeButton, coinsButton, energyButton, statusButton, closeButton;
        private DebugCommandManager commands;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterInstaller()
        {
            SceneManager.sceneLoaded -= Install;
            SceneManager.sceneLoaded += Install;
        }

        private static void Install(Scene scene, LoadSceneMode mode)
        {
            var canvas = GameObject.Find("GameUI");
            if (canvas == null || canvas.scene != scene || canvas.GetComponentInChildren<DebugModePanel>(true) != null) return;
            var prefab = Resources.Load<GameObject>("DebugModePanel");
            if (canvas != null && prefab != null)
            {
                var instance = Instantiate(prefab, canvas.transform, false);
                var root = instance.GetComponent<RectTransform>();
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
                root.localScale = Vector3.one;
                var modalCanvas = instance.GetComponent<Canvas>();
                modalCanvas.overrideSorting = true;
                modalCanvas.sortingOrder = short.MaxValue - 1;
            }
        }
        private void Awake()
        {
            commands = new DebugCommandManager(FindAnyObjectByType<ResortGameplayBootstrap>());
            output.enableAutoSizing = true;
            output.fontSizeMin = 12;
            output.fontSizeMax = output.fontSize;
            executeButton.onClick.AddListener(() => Run(input.text));
            coinsButton.onClick.AddListener(() => Run("add_coins 100"));
            energyButton.onClick.AddListener(() => Run("add_energy 50"));
            statusButton.onClick.AddListener(() => Run("wave_status"));
            closeButton.onClick.AddListener(() => SetOpen(false));
            SetOpen(false);
        }
        private void Update()
        {
            if (DebugModalState.SaveSlotsOpen) return;
            if (Input.GetKeyDown(KeyCode.F9)) SetOpen(!panel.activeSelf);
            else if (panel.activeSelf && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
        }
        internal void Close() => SetOpen(false);

        public void OpenFromSettings()
        {
            if (!DebugModalState.SaveSlotsOpen)
                SetOpen(true);
        }

        private void SetOpen(bool open)
        {
            DebugModalState.SetOpen(open);
            modalOverlay.SetActive(open);
            panel.SetActive(open);
            if (!open && input != null) input.DeactivateInputField();
        }
        private void OnDisable() { DebugModalState.SetOpen(false); }
        private void Run(string command) { output.text = commands.Execute(command); }
    }
}
#endif
