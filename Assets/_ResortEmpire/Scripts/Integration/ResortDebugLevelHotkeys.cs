using System;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using TMPro;
using UnityEngine.EventSystems;
#endif

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// Temporary visual level override used to inspect all three building arts
    /// without changing the saved progression or spending upgrade currency.
    /// </summary>
    public static class ResortDebugLevelOverride
    {
        public static int? CurrentLevel { get; private set; }

        public static event Action<int?> Changed;

        public static void SetLevel(int level)
        {
            level = Mathf.Clamp(level, 1, 3);
            if (CurrentLevel == level)
                return;

            CurrentLevel = level;
            Changed?.Invoke(CurrentLevel);
        }

        public static void Clear()
        {
            if (!CurrentLevel.HasValue)
                return;

            CurrentLevel = null;
            Changed?.Invoke(null);
        }
    }

    [DisallowMultipleComponent]
    public sealed class ResortDebugLevelHotkeys : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            if (DebugModalState.BlocksInput) return;
            var level = Input.GetKeyDown(KeyCode.F1) ? 1
                : Input.GetKeyDown(KeyCode.F2) ? 2
                : Input.GetKeyDown(KeyCode.F3) ? 3 : 0;
            if (level == 0) return;

            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && (selected.GetComponentInParent<TMP_InputField>()?.isFocused == true
                || selected.GetComponentInParent<UnityEngine.UI.InputField>()?.isFocused == true)) return;
            if (FindAnyObjectByType<ResortUiStateCoordinator>()?.IsDebugHotkeyBlocked == true) return;

            var game = FindAnyObjectByType<ResortGameplayBootstrap>();
            Debug.Log(new DebugCommandManager(game).Execute($"set_buildings_level {level}"), this);
        }
#endif
    }
}
