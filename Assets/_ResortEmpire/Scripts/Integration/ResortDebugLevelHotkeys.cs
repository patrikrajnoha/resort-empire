using System;
using UnityEngine;

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
        private void Awake()
        {
            ResortDebugLevelOverride.Clear();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
                SetLevel(1);
            else if (Input.GetKeyDown(KeyCode.F2))
                SetLevel(2);
            else if (Input.GetKeyDown(KeyCode.F3))
                SetLevel(3);
        }

        private void OnDestroy()
        {
            ResortDebugLevelOverride.Clear();
        }

        private static void SetLevel(int level)
        {
            ResortDebugLevelOverride.SetLevel(level);
            Debug.Log("Debug building level preview: Level " + level + ".");
        }
    }
}
