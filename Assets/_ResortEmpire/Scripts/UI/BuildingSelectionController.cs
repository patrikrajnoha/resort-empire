using System;
using System.Collections.Generic;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>Single-pointer S1 selection for world-space building Images.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingSelectionController : MonoBehaviour
    {
        [Serializable]
        public sealed class BuildingTarget
        {
            public BuildingId Building;
            public Transform Root;
            public Image[] OutlineImages;
        }

        [SerializeField] private ResortGameplayBootstrap gameplay;
        [SerializeField] private BuildingInfoPanelPresenter informationPanel;
        [SerializeField] private BuildingTarget[] targets;

        private readonly List<RaycastResult> raycasts = new List<RaycastResult>();
        private BuildingTarget highlighted;
        private bool pointerWasPressed;
        private bool pointerGestureBlockedByUi;

        public void Configure(ResortGameplayBootstrap runtime, BuildingInfoPanelPresenter panel, BuildingTarget[] configuredTargets)
        {
            gameplay = runtime;
            informationPanel = panel;
            targets = configuredTargets;
            PrepareOutlines();
        }

        private void Awake() => PrepareOutlines();

        private void OnEnable()
        {
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (gameplay != null) gameplay.StateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (gameplay != null) gameplay.StateChanged -= HandleStateChanged;
            pointerWasPressed = false;
            pointerGestureBlockedByUi = false;
            SetHighlighted(null);
        }

        private void HandleStateChanged(GamePhase _, GamePhase next)
        {
            if (AllowsBuildingSelection(next)) return;
            SetHighlighted(null);
            informationPanel?.Close();
        }

        private void Update()
        {
            if (!TryReadPointer(out var position, out var pressed)) return;

            if (!AllowsBuildingSelection(gameplay == null ? GamePhase.WAVE_ACTIVE : gameplay.CurrentPhase))
            {
                // Never carry a gesture that started during a wave into the next
                // between-wave state, and never interfere with Worker UI controls.
                pointerWasPressed = pressed;
                pointerGestureBlockedByUi = pressed;
                SetHighlighted(null);
                return;
            }

            if (informationPanel != null && informationPanel.gameObject.activeInHierarchy)
            {
                pointerWasPressed = false;
                pointerGestureBlockedByUi = true;
                SetHighlighted(null);
                return;
            }

            // The island uses a world-space uGUI Canvas. Its GraphicRaycaster gives us
            // the actual alpha-shaped building Images, so no rectangular physics hitboxes
            // are needed for these multi-sprite UI visuals.
            var target = TargetAt(position, out var pointerOverBlockingUi);

            if (pressed && !pointerWasPressed)
            {
                pointerGestureBlockedByUi = pointerOverBlockingUi;
                if (pointerGestureBlockedByUi)
                    Debug.Log("BuildingSelection: gesture started on UI and will be ignored.", this);
            }

            if (pointerGestureBlockedByUi)
                target = null;
            SetHighlighted(pointerOverBlockingUi ? null : target);

            if (pressed)
            {
                if (!pointerWasPressed)
                    Debug.Log("BuildingSelection: pointer pressed.", this);
                pointerWasPressed = true;
                return;
            }

            if (!pointerWasPressed) return;
            SetHighlighted(null);
            pointerWasPressed = false;
            if (pointerGestureBlockedByUi)
            {
                pointerGestureBlockedByUi = false;
                return;
            }
            if (target == null || !IsOwned(target.Building))
            {
                Debug.Log("BuildingSelection: pointer released outside a selectable building.", this);
                return;
            }

            Debug.Log("BuildingSelection: selected " + target.Building + ".", this);
            informationPanel?.Open(target.Building);
        }

        private bool TryReadPointer(out Vector2 position, out bool pressed)
        {
            var touch = Touchscreen.current;
            // Keep reading the touch for its release frame; otherwise Android would
            // never deliver the selection that happens when the finger is lifted.
            if (touch != null && (touch.primaryTouch.press.isPressed || pointerWasPressed))
            {
                position = touch.primaryTouch.position.ReadValue();
                pressed = touch.primaryTouch.press.isPressed;
                return true;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                position = mouse.position.ReadValue();
                pressed = mouse.leftButton.isPressed;
                return true;
            }

            position = default;
            pressed = false;
            return false;
        }

        private BuildingTarget TargetAt(Vector2 screenPosition, out bool pointerOverBlockingUi)
        {
            pointerOverBlockingUi = false;
            if (EventSystem.current == null) return null;
            raycasts.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPosition }, raycasts);
            foreach (var raycast in raycasts)
            {
                foreach (var target in targets)
                    if (target != null && target.Root != null && raycast.gameObject.transform.IsChildOf(target.Root))
                        return target;

                // Screen-space Buttons (H/R/B and other HUD actions) must win over
                // island selection. Background graphics on the world island Canvas are
                // deliberately ignored so they do not mask their own building Images.
                if (IsBlockingUi(raycast.gameObject))
                {
                    pointerOverBlockingUi = true;
                    return null;
                }
            }
            return null;
        }

        private bool IsBlockingUi(GameObject gameObject)
        {
            if (gameObject == null) return false;
            if (gameObject.transform.IsChildOf(transform)) return false;
            return gameObject.GetComponentInParent<Selectable>() != null;
        }

        private bool IsOwned(BuildingId id)
        {
            if (gameplay == null) return false;
            foreach (var building in gameplay.Buildings)
                if (building.BuildingId == id) return building.IsOwned;
            return false;
        }

        private static bool AllowsBuildingSelection(GamePhase phase) =>
            phase == GamePhase.AWAITING_WAVE || phase == GamePhase.UPGRADE;

        private void PrepareOutlines()
        {
            if (targets == null) return;
            foreach (var target in targets)
            {
                if (target == null || target.OutlineImages == null) continue;
                foreach (var image in target.OutlineImages)
                {
                    if (image == null) continue;
                    // A disabled raycast target was the original scene wiring issue.
                    // Keep selection limited to the visible building silhouette Image.
                    image.raycastTarget = true;
                    var outline = image.GetComponent<Outline>();
                    if (outline == null) outline = image.gameObject.AddComponent<Outline>();
                    outline.effectColor = Color.white;
                    outline.effectDistance = new Vector2(2f, -2f);
                    outline.useGraphicAlpha = true;
                    outline.enabled = false;
                }
            }
        }

        private void SetHighlighted(BuildingTarget target)
        {
            if (ReferenceEquals(highlighted, target)) return;
            SetOutline(highlighted, false);
            highlighted = target;
            SetOutline(highlighted, true);
            Debug.Log("BuildingSelection: highlight " + (highlighted == null ? "cleared" : highlighted.Building.ToString()) + ".", this);
        }

        private static void SetOutline(BuildingTarget target, bool enabled)
        {
            if (target == null || target.OutlineImages == null) return;
            foreach (var image in target.OutlineImages)
            {
                if (image == null) continue;
                var outline = image.GetComponent<Outline>();
                if (outline != null) outline.enabled = enabled;
            }
        }
    }
}
