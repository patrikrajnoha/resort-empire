using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    public static class DebugModalState
    {
        private static bool open;
        private static int closedFrame = -1;
        internal static bool SaveSlotsOpen { get; private set; }
        public static bool BlocksInput => open || SaveSlotsOpen || closedFrame == Time.frameCount;
        public static void SetOpen(bool value)
        {
            if (open && !value) closedFrame = Time.frameCount;
            open = value;
        }

        internal static void SetSaveSlotsOpen(bool value)
        {
            if (SaveSlotsOpen && !value) closedFrame = Time.frameCount;
            SaveSlotsOpen = value;
        }
    }
}
