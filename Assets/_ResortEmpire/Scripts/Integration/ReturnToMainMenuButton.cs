using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// S1 button back to the Main Menu. It is offered only between waves: every economy command saves its
    /// checkpoint before it commits, so leaving then cannot lose progress. It also shows when gameplay is
    /// unconfigured, so a resort opened without a loadable save is never a dead end.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button), typeof(CanvasGroup))]
    public sealed class ReturnToMainMenuButton : MonoBehaviour
    {
        [SerializeField] private ResortGameplayBootstrap gameplay;

        private Button button;
        private CanvasGroup group;

        public bool IsAvailable
        {
            get
            {
                if (!GameSessionNavigator.CanReturnToMainMenu || DebugModalState.BlocksInput) return false;
                if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
                return gameplay == null || !gameplay.IsConfigured || gameplay.CurrentPhase == GamePhase.AWAITING_WAVE;
            }
        }

        private void Awake()
        {
            button = GetComponent<Button>();
            group = GetComponent<CanvasGroup>();
            button.onClick.AddListener(ReturnToMainMenu);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(ReturnToMainMenu);
        }

        private void OnEnable() => Apply();
        private void Update() => Apply();

        private void Apply()
        {
            var available = IsAvailable;
            // A hidden button stays enabled so it notices when it becomes available again.
            group.alpha = available ? 1f : 0f;
            group.interactable = available;
            group.blocksRaycasts = available;
        }

        private void ReturnToMainMenu()
        {
            if (IsAvailable) GameSessionNavigator.TryReturnToMainMenu();
            Apply();
        }
    }
}
