using System;
using System.Collections.Generic;

namespace ResortEmpire.DebugUi
{
    public sealed class ResortDebugUiCycleState
    {
        private readonly int previewCount;
        private int currentIndex = -1;

        public ResortDebugUiCycleState(int previewCount)
        {
            if (previewCount < 0)
                throw new ArgumentOutOfRangeException(nameof(previewCount));

            this.previewCount = previewCount;
        }

        public int CurrentIndex => currentIndex;
        public bool IsNormal => currentIndex < 0;

        public int Advance()
        {
            if (previewCount == 0)
                return -1;

            currentIndex++;
            if (currentIndex >= previewCount)
                currentIndex = -1;

            return currentIndex;
        }

        public bool Select(int index)
        {
            if (index < -1 || index >= previewCount)
                return false;

            currentIndex = index;
            return true;
        }

        public void Reset()
        {
            currentIndex = -1;
        }
    }

    public interface IResortDebugUiPreviewTarget
    {
        bool IsAvailable { get; }
        bool IsActive { get; set; }
    }

    public sealed class ResortDebugUiPreviewManager
    {
        private readonly IReadOnlyList<IResortDebugUiPreviewTarget> targets;
        private bool[] originalStates;
        private int activeIndex = -1;

        public ResortDebugUiPreviewManager(IReadOnlyList<IResortDebugUiPreviewTarget> targets)
        {
            this.targets = targets ?? Array.Empty<IResortDebugUiPreviewTarget>();
        }

        public bool HasActivePreview => activeIndex >= 0;
        public int ActiveIndex => activeIndex;
        public bool HasCapturedState => originalStates != null;

        public bool OpenPreview(int index)
        {
            if (index < 0 || index >= targets.Count || !targets[index].IsAvailable)
                return false;

            if (originalStates == null)
            {
                originalStates = new bool[targets.Count];
                for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                    originalStates[targetIndex] = targets[targetIndex].IsActive;
            }

            for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                targets[targetIndex].IsActive = targetIndex == index;

            activeIndex = index;
            return true;
        }

        public void RestoreNormalUi()
        {
            if (originalStates != null)
            {
                for (var index = 0; index < targets.Count; index++)
                    targets[index].IsActive = originalStates[index];
            }

            originalStates = null;
            activeIndex = -1;
        }
    }
}
