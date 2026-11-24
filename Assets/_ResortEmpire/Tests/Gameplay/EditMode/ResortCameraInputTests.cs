using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class ResortCameraInputTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject cameraObject;
        private Camera camera;
        private Component controller;
        private Type controllerType;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("Camera input test");
            cameraObject.SetActive(false);
            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.aspect = 1f;
            controllerType = Type.GetType("ResortEmpire.ResortTouchCameraController, Assembly-CSharp", true);
            controller = cameraObject.AddComponent(controllerType);
            controllerType.GetField("targetCamera", PrivateInstance).SetValue(controller, camera);
            controllerType.GetField("targetZoomValue", PrivateInstance).SetValue(controller, 4f);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void ZoomWaitsForTheSimulatorViewport()
        {
            camera.pixelRect = new Rect(0f, 0f, 0f, 0f);
            Assert.That(controllerType.GetMethod("IsViewportReady", PrivateInstance).Invoke(controller, null), Is.False);
            controllerType.GetMethod("ZoomAroundScreenPoint").Invoke(controller, new object[] { 2f, Vector2.zero });
            Assert.That(TargetZoom(), Is.EqualTo(4f));

            camera.pixelRect = new Rect(0f, 0f, 200f, 200f);
            Assert.That(controllerType.GetMethod("IsViewportReady", PrivateInstance).Invoke(controller, null), Is.True);
            controllerType.GetMethod("ZoomAroundScreenPoint").Invoke(controller, new object[] { 2f, new Vector2(100f, 100f) });
            Assert.That(TargetZoom(), Is.EqualTo(2.5f));
        }

        [Test]
        public void KeyboardZoomStaysWithinConfiguredLimits()
        {
            camera.pixelRect = new Rect(0f, 0f, 200f, 200f);
            var previousMode = InputSystem.settings.updateMode;
            Keyboard keyboard = null;
            try
            {
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();
                InputSystem.ResetDevice(keyboard);
                Assert.That(keyboard.numpadPlusKey.wasPressedThisFrame, Is.False);
                Assert.That(keyboard.numpadMinusKey.wasPressedThisFrame, Is.False);
                PressZoomKey(keyboard, Key.NumpadPlus);
                var firstZoom = TargetZoom();
                Assert.That(firstZoom, Is.LessThan(4f));
                InputSystem.Update();
                controllerType.GetMethod("UpdateKeyboardInput", PrivateInstance).Invoke(controller, null);
                Assert.That(TargetZoom(), Is.EqualTo(firstZoom), "Holding the key must not continuously zoom.");

                for (var press = 0; press < 7; press++)
                    PressZoomKey(keyboard, Key.NumpadPlus);
                Assert.That(TargetZoom(), Is.EqualTo(2.5f));

                for (var press = 0; press < 12; press++)
                    PressZoomKey(keyboard, Key.NumpadMinus);
                Assert.That(TargetZoom(), Is.EqualTo(6.5f));
            }
            finally
            {
                if (keyboard != null)
                    InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.updateMode = previousMode;
            }
        }

        private void PressZoomKey(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            controllerType.GetMethod("UpdateKeyboardInput", PrivateInstance).Invoke(controller, null);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            InputSystem.Update();
            Assert.That(Keyboard.current, Is.SameAs(keyboard));
            Assert.That(keyboard[key].isPressed, Is.True);
            controllerType.GetMethod("UpdateKeyboardInput", PrivateInstance).Invoke(controller, null);
        }

        private float TargetZoom() => (float)controllerType.GetField("targetZoomValue", PrivateInstance).GetValue(controller);
    }
}
