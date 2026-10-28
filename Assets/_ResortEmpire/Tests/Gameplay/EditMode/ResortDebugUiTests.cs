using System.Collections.Generic;
using NUnit.Framework;
using ResortEmpire.DebugUi;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class ResortDebugUiTests
    {
        [Test]
        public void F1CycleAdvancesNinePreviewsThenReturnsToNormal()
        {
            var state = new ResortDebugUiCycleState(9);

            for (var index = 0; index < 9; index++)
            {
                Assert.That(state.Advance(), Is.EqualTo(index));
                Assert.That(state.CurrentIndex, Is.EqualTo(index));
                Assert.That(state.IsNormal, Is.False);
            }

            Assert.That(state.Advance(), Is.EqualTo(-1));
            Assert.That(state.IsNormal, Is.True);
            Assert.That(state.Advance(), Is.EqualTo(0));
        }

        [Test]
        public void F1CycleWithNoPreviewsStaysNormal()
        {
            var state = new ResortDebugUiCycleState(0);

            Assert.That(state.Advance(), Is.EqualTo(-1));
            Assert.That(state.IsNormal, Is.True);
        }

        [Test]
        public void PreviewManagerKeepsExactlyOnePreviewActive()
        {
            var targets = new[] { new FakeTarget(true, true), new FakeTarget(true, false), new FakeTarget(true, true) };
            var manager = new ResortDebugUiPreviewManager(targets);

            Assert.That(manager.OpenPreview(0), Is.True);
            Assert.That(ActiveCount(targets), Is.EqualTo(1));
            Assert.That(manager.ActiveIndex, Is.EqualTo(0));

            Assert.That(manager.OpenPreview(2), Is.True);
            Assert.That(ActiveCount(targets), Is.EqualTo(1));
            Assert.That(targets[2].IsActive, Is.True);
            Assert.That(targets[0].IsActive, Is.False);
        }

        [Test]
        public void RepeatedOpenAndRestoreOperationsReturnOriginalState()
        {
            var targets = new[] { new FakeTarget(true, true), new FakeTarget(true, true), new FakeTarget(true, false) };
            var manager = new ResortDebugUiPreviewManager(targets);

            manager.OpenPreview(1);
            manager.OpenPreview(0);
            manager.RestoreNormalUi();
            manager.RestoreNormalUi();

            Assert.That(targets[0].IsActive, Is.True);
            Assert.That(targets[1].IsActive, Is.True);
            Assert.That(targets[2].IsActive, Is.False);
            Assert.That(manager.HasActivePreview, Is.False);
            Assert.That(manager.HasCapturedState, Is.False);
        }

        [Test]
        public void MissingPreviewTargetsAreSafeAndDoNotBecomeActive()
        {
            var targets = new[] { new FakeTarget(false, false), new FakeTarget(true, true) };
            var manager = new ResortDebugUiPreviewManager(targets);

            Assert.That(manager.OpenPreview(0), Is.False);
            Assert.That(manager.HasActivePreview, Is.False);
            Assert.That(manager.OpenPreview(1), Is.True);
        }

        [Test]
        public void PreviewManagerDoesNotIssueGameplayCommandsOrMutateGameState()
        {
            var targets = new[] { new FakeTarget(true, true), new FakeTarget(true, false) };
            var manager = new ResortDebugUiPreviewManager(targets);
            var gameplay = new FakeGameplayFacade();

            manager.OpenPreview(1);
            manager.RestoreNormalUi();

            Assert.That(gameplay.CommandCount, Is.EqualTo(0));
            Assert.That(gameplay.Phase, Is.EqualTo(ResortEmpire.Gameplay.GamePhase.AWAITING_WAVE));
        }

        [Test]
        public void PreviewSwitchingWorksWhileTargetsAreInitiallyInactive()
        {
            var targets = new[] { new FakeTarget(true, false), new FakeTarget(true, false), new FakeTarget(true, false) };
            var manager = new ResortDebugUiPreviewManager(targets);

            Assert.That(manager.OpenPreview(1), Is.True);
            Assert.That(manager.OpenPreview(2), Is.True);
            manager.RestoreNormalUi();

            Assert.That(ActiveCount(targets), Is.EqualTo(0));
        }

        [Test]
        public void UnconfiguredBootstrapIsCompatibleWithVisualPreviewState()
        {
            var targets = new[] { new FakeTarget(true, true) };
            var manager = new ResortDebugUiPreviewManager(targets);

            Assert.That(manager.OpenPreview(0), Is.True);
            Assert.That(manager.HasActivePreview, Is.True);
        }

        [Test]
        public void F1KeyboardInputAdvancesOncePerPressAndNotWhileHeld()
        {
            var previewNames = new[]
            {
                "HomeIslandView",
                "WaveNeedsPanel",
                "AssignStaffRestaurant",
                "WaveComplete",
                "UpgradeSuggestionPanel",
                "AreaUnlockPanel",
                "VipRequestBoard",
                "VipPreview",
                "VipComplete"
            };
            var previewObjects = new List<GameObject>();
            var controllerObject = new GameObject("ResortDebugUiControllerTest");
            MonoBehaviour controller = null;
            Keyboard keyboard = null;
            var previousInputUpdateMode = InputSystem.settings.updateMode;
            System.Type controllerType = null;
            System.Reflection.FieldInfo activeInstanceField = null;
            object previousActiveInstance = null;

            try
            {
                foreach (var previewName in previewNames)
                    previewObjects.Add(new GameObject(previewName));

                controllerType = System.Type.GetType("ResortEmpire.DebugUi.ResortDebugUiController, Assembly-CSharp");
                Assert.That(controllerType, Is.Not.Null);
                activeInstanceField = controllerType.GetField("activeInstance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                previousActiveInstance = activeInstanceField.GetValue(null);
                activeInstanceField.SetValue(null, null);
                controller = controllerObject.AddComponent(controllerType) as MonoBehaviour;
                InvokeControllerMethod(controller, "Awake");
                Assert.That(PreviewCount(controller), Is.EqualTo(previewNames.Length));
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();
                InputSystem.ResetDevice(keyboard);

                Assert.That(keyboard.f1Key.wasPressedThisFrame, Is.False);
                InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1));
                InputSystem.Update();
                Assert.That(Keyboard.current, Is.SameAs(keyboard));
                Assert.That(keyboard.f1Key.isPressed, Is.True);
                InvokeControllerMethod(controller, "Update");
                Assert.That(CurrentPreviewIndex(controller), Is.EqualTo(0));

                // Calling Update again in the same input frame must not double-advance.
                InvokeControllerMethod(controller, "Update");
                Assert.That(CurrentPreviewIndex(controller), Is.EqualTo(0));

                // Holding F1 across a later input update must not advance again.
                InputSystem.Update();
                InvokeControllerMethod(controller, "Update");
                Assert.That(CurrentPreviewIndex(controller), Is.EqualTo(0));

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                InvokeControllerMethod(controller, "Update");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1));
                InputSystem.Update();
                InvokeControllerMethod(controller, "Update");
                Assert.That(CurrentPreviewIndex(controller), Is.EqualTo(1));
            }
            finally
            {
                if (controller != null)
                    InvokeControllerMethod(controller, "RestoreNormalUi");
                Object.DestroyImmediate(controllerObject);
                foreach (var previewObject in previewObjects)
                    Object.DestroyImmediate(previewObject);
                if (keyboard != null)
                    InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.updateMode = previousInputUpdateMode;
                if (activeInstanceField != null)
                    activeInstanceField.SetValue(null, previousActiveInstance);
            }
        }

        private static int CurrentPreviewIndex(MonoBehaviour controller)
        {
            return (int)controller.GetType().GetProperty("CurrentPreviewIndex").GetValue(controller);
        }

        private static int PreviewCount(MonoBehaviour controller)
        {
            return (int)controller.GetType().GetProperty("PreviewCount").GetValue(controller);
        }

        private static void InvokeControllerMethod(MonoBehaviour controller, string methodName)
        {
            controller.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Invoke(controller, null);
        }

        private static int ActiveCount(IReadOnlyList<FakeTarget> targets)
        {
            var count = 0;
            for (var index = 0; index < targets.Count; index++)
            {
                if (targets[index].IsActive)
                    count++;
            }

            return count;
        }

        private sealed class FakeTarget : IResortDebugUiPreviewTarget
        {
            public FakeTarget(bool available, bool active)
            {
                IsAvailable = available;
                IsActive = active;
            }

            public bool IsAvailable { get; }
            public bool IsActive { get; set; }
        }

        private sealed class FakeGameplayFacade
        {
            public int CommandCount { get; private set; }
            public ResortEmpire.Gameplay.GamePhase Phase => ResortEmpire.Gameplay.GamePhase.AWAITING_WAVE;
        }
    }
}
