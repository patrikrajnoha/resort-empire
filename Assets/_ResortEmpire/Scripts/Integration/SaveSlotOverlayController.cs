using System;
using System.IO;
using ResortEmpire.Gameplay.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>Isolated uGUI overlay for selecting, creating, loading and deleting local save slots.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1100)]
    public sealed class SaveSlotOverlayController : MonoBehaviour
    {
        [SerializeField] private Sprite panelSprite;
        [SerializeField] private Sprite buttonSprite;
        [SerializeField] private TMP_FontAsset textFont;

        private GameObject overlay;
        private GameObject backdrop;
        private GameObject previousSelection;
        private bool isOpen;
        private TextMeshProUGUI heading;
        private TextMeshProUGUI message;
        private GameObject confirmButton;
        private GameObject cancelConfirmationButton;
        private readonly TextMeshProUGUI[] slotLabels = new TextMeshProUGUI[SaveSlotStorage.SlotCount];
        private ResortGameplayBootstrap bootstrap;
        private int selectedSlot = 1;
        private PendingAction pendingAction;

        private enum PendingAction { None, Overwrite, Delete }

        public void Configure(Sprite panel, Sprite button, TMP_FontAsset font)
        {
            panelSprite = panel;
            buttonSprite = button;
            textFont = font;
        }

        private void Awake()
        {
            bootstrap = FindAnyObjectByType<ResortGameplayBootstrap>();
            PrepareOverlay();
            BindOverlay();
            Close();
        }

        /// <summary>Editor-only prefab authoring hook. Runtime callers should use Open and Close.</summary>
        public void BuildPrefabContents()
        {
            PrepareOverlay();
            overlay.SetActive(false);
            backdrop.SetActive(false);
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F5)) return;
            if (isOpen) Close();
            else Open();
        }

        private void OnDisable() => Close();

        /// <summary>Entry point for a future Main Menu New Game button.</summary>
        public void OpenNewGameSlotSelection()
        {
            Open("Choose a slot for a new game");
        }

        /// <summary>Entry point for a future Main Menu Continue button.</summary>
        public void OpenContinueSlotSelection()
        {
            Open("Choose a saved game to continue");
        }

        /// <summary>Entry point for a future Pause Menu; the Save button writes only the active slot.</summary>
        public void OpenPauseSaveSlotSelection()
        {
            Open("Active save slot: " + SaveSlotStorage.GetActiveSlot(ResortSaveLocation.Root));
        }

        /// <summary>Default future-menu entry point; opens the popup without changing game state.</summary>
        public void Open()
        {
            OpenContinueSlotSelection();
        }

        /// <summary>Refreshes visible slot metadata after a save or an external slot operation.</summary>
        public void RefreshSlots()
        {
            if (overlay != null)
                Refresh();
        }

        public void Close()
        {
            pendingAction = PendingAction.None;
            RefreshConfirmationButtons();
            if (overlay != null) overlay.SetActive(false);
            if (backdrop != null) backdrop.SetActive(false);
            if (!isOpen) return;

            isOpen = false;
            DebugModalState.SetSaveSlotsOpen(false);
            var events = EventSystem.current;
            if (events != null)
                events.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy
                    ? previousSelection : null);
            previousSelection = null;
        }

        private void Open(string title)
        {
            if (overlay == null || !isActiveAndEnabled) return;
            if (!isOpen)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                FindAnyObjectByType<DebugModePanel>()?.Close();
#endif
                var events = EventSystem.current;
                previousSelection = events != null ? events.currentSelectedGameObject : null;
                isOpen = true;
                DebugModalState.SetSaveSlotsOpen(true);
                backdrop.SetActive(true);
                overlay.SetActive(true);
                if (events != null)
                    events.SetSelectedGameObject(overlay.transform.Find("Slot" + selectedSlot).gameObject);
            }
            heading.text = "SAVE MANAGER";
            message.text = title;
            pendingAction = PendingAction.None;
            Refresh();
        }

        private void PrepareOverlay()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;
            if (!TryGetComponent<GraphicRaycaster>(out _)) gameObject.AddComponent<GraphicRaycaster>();

            var existingPanel = transform.Find("SaveSlotsPanel");
            if (existingPanel == null) BuildOverlay();
            else overlay = existingPanel.gameObject;

            var existingBackdrop = transform.Find("SaveSlotsBackdrop");
            backdrop = existingBackdrop == null
                ? CreateElement("SaveSlotsBackdrop", transform, Vector2.zero, Vector2.one, Vector2.one * 0.5f, Vector2.zero)
                : existingBackdrop.gameObject;
            if (!backdrop.TryGetComponent<Image>(out var blocker)) blocker = backdrop.AddComponent<Image>();
            blocker.color = Color.clear;
            blocker.raycastTarget = true;
            blocker.canvasRenderer.cullTransparentMesh = false;
            backdrop.transform.SetAsFirstSibling();
        }

        private void BindOverlay()
        {
            heading = overlay.transform.Find("Title").GetComponent<TextMeshProUGUI>();
            message = overlay.transform.Find("Message").GetComponent<TextMeshProUGUI>();
            for (var index = 0; index < slotLabels.Length; index++)
            {
                var slot = index + 1;
                var button = BindButton("Slot" + slot, () => SelectSlot(slot));
                // Older prefabs include a button caption before the metadata label.
                var labels = button.GetComponentsInChildren<TextMeshProUGUI>(true);
                slotLabels[index] = labels[labels.Length - 1];
                for (var label = 0; label < labels.Length - 1; label++)
                    labels[label].gameObject.SetActive(false);
            }

            BindButton("CloseX", Close);
            BindButton("NewGame", CreateNewGame);
            BindButton("Load", LoadSelected);
            BindButton("SaveActive", SaveActive);
            BindButton("Delete", RequestDelete);
            BindButton("Back", Close);
            confirmButton = BindButton("Confirm", ConfirmPendingAction).gameObject;
            cancelConfirmationButton = BindButton("Cancel", CancelPendingAction).gameObject;
        }

        private Button BindButton(string name, UnityAction action)
        {
            var button = overlay.transform.Find(name).GetComponent<Button>();
            // Automatic navigation can select buttons on a canvas behind this modal.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(action);
            return button;
        }

        private void BuildOverlay()
        {
            overlay = CreateElement("SaveSlotsPanel", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(960f, 700f));
            var image = overlay.AddComponent<UnityEngine.UI.Image>();
            image.sprite = panelSprite;
            image.type = panelSprite == null ? UnityEngine.UI.Image.Type.Simple : UnityEngine.UI.Image.Type.Sliced;
            image.color = new Color(0.12f, 0.24f, 0.25f, 0.98f);

            heading = CreateText("Title", overlay.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(860f, 54f), new Vector2(0f, -46f), 30, FontStyles.Bold);
            heading.text = "SAVE MANAGER";
            CreateButton("CloseX", overlay.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(52f, 52f), new Vector2(-42f, -42f));
            for (var index = 0; index < SaveSlotStorage.SlotCount; index++)
            {
                var slot = index + 1;
                var button = CreateButton("Slot" + slot, overlay.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(840f, 112f), new Vector2(0f, -140f - index * 126));
                slotLabels[index] = CreateText("Label", button.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-28f, 0f), Vector2.zero, 20, FontStyles.Normal);
                slotLabels[index].alignment = TextAlignmentOptions.Left;
            }

            CreateButton("NewGame", overlay.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(190f, 58f), new Vector2(145f, 106f));
            CreateButton("Load", overlay.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(190f, 58f), new Vector2(350f, 106f));
            CreateButton("SaveActive", overlay.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(190f, 58f), new Vector2(-350f, 106f));
            CreateButton("Delete", overlay.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(190f, 58f), new Vector2(-145f, 106f));
            CreateButton("Back", overlay.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(190f, 58f), new Vector2(0f, 30f));
            confirmButton = CreateButton("Confirm", overlay.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(160f, 50f), new Vector2(-90f, 182f)).gameObject;
            cancelConfirmationButton = CreateButton("Cancel", overlay.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(160f, 50f), new Vector2(90f, 182f)).gameObject;
            message = CreateText("Message", overlay.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(820f, 58f), new Vector2(0f, 242f), 17, FontStyles.Normal);
            message.alignment = TextAlignmentOptions.Center;
            RefreshConfirmationButtons();
        }

        private void SelectSlot(int slot)
        {
            selectedSlot = slot;
            pendingAction = PendingAction.None;
            message.text = "Selected Slot " + slot + ".";
            Refresh();
        }

        private void CreateNewGame()
        {
            var metadata = GetMetadata(selectedSlot);
            if (metadata.Status != SaveSlotStatus.Empty)
            {
                pendingAction = PendingAction.Overwrite;
                message.text = "Overwrite Slot " + selectedSlot + "?";
                RefreshConfirmationButtons();
                return;
            }
            ActivateNewSlot();
        }

        private void LoadSelected()
        {
            var metadata = GetMetadata(selectedSlot);
            if (metadata.Status != SaveSlotStatus.Occupied)
            {
                message.text = metadata.Status == SaveSlotStatus.Corrupted ? "This slot is corrupted and cannot be loaded." : "Choose a saved game to load.";
                return;
            }
            try
            {
                var activeSave = bootstrap == null ? default : bootstrap.TrySaveProgress();
                if (!activeSave.Succeeded && bootstrap != null)
                    throw new InvalidOperationException(activeSave.Message);
                if (bootstrap == null || !bootstrap.TryReloadProgression(SaveSlotStorage.CreatePersistence(ResortSaveLocation.Root, selectedSlot)))
                    throw new InvalidOperationException("Gameplay runtime could not be rebound to this save slot.");
                SaveSlotStorage.SetActiveSlot(ResortSaveLocation.Root, selectedSlot);
                Close();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                message.text = exception.Message;
            }
        }

        private void SaveActive()
        {
            if (bootstrap == null) { message.text = "Gameplay runtime is unavailable."; return; }
            var result = bootstrap.TrySaveProgress();
            message.text = result.Succeeded ? "Saved Slot " + SaveSlotStorage.GetActiveSlot(ResortSaveLocation.Root) + "." : result.Message;
            Refresh();
        }

        private void RequestDelete()
        {
            var metadata = GetMetadata(selectedSlot);
            if (metadata.Status == SaveSlotStatus.Empty) { message.text = "That slot is already empty."; return; }
            if (metadata.IsActive) { message.text = "Load another slot before deleting the active save."; return; }
            pendingAction = PendingAction.Delete;
            message.text = "Are you sure you want to delete this save?";
            RefreshConfirmationButtons();
        }

        private void ConfirmPendingAction()
        {
            if (pendingAction == PendingAction.Overwrite) { ActivateNewSlot(); return; }
            if (pendingAction != PendingAction.Delete) return;
            SaveSlotStorage.TryDeleteInactiveSlot(ResortSaveLocation.Root, selectedSlot);
            pendingAction = PendingAction.None;
            message.text = "Slot " + selectedSlot + " deleted.";
            Refresh();
        }

        private void CancelPendingAction()
        {
            pendingAction = PendingAction.None;
            message.text = "Cancelled.";
            RefreshConfirmationButtons();
        }

        private void ActivateNewSlot()
        {
            try
            {
                if (pendingAction == PendingAction.Overwrite)
                    SaveSlotStorage.ClearSlotForConfirmedOverwrite(ResortSaveLocation.Root, selectedSlot);
                else
                    SaveSlotStorage.SetActiveSlot(ResortSaveLocation.Root, selectedSlot);
                if (bootstrap == null || !bootstrap.TryReloadProgression(SaveSlotStorage.CreatePersistence(ResortSaveLocation.Root, selectedSlot)))
                    throw new InvalidOperationException("Gameplay runtime could not create the new save slot.");
                Close();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                message.text = exception.Message;
            }
        }

        private SaveSlotMetadata GetMetadata(int slot)
        {
            var configuration = bootstrap == null ? null : bootstrap.ProgressionConfiguration;
            return configuration == null
                ? new SaveSlotMetadata(slot, SaveSlotStatus.Corrupted, 0, default, false)
                : SaveSlotStorage.GetMetadata(ResortSaveLocation.Root, slot, configuration);
        }

        private void Refresh()
        {
            for (var index = 0; index < slotLabels.Length; index++)
            {
                var metadata = GetMetadata(index + 1);
                var status = metadata.Status == SaveSlotStatus.Empty ? "Empty Slot" : metadata.Status == SaveSlotStatus.Corrupted ? "Corrupted Save" : "Saved Game";
                var saved = metadata.Status == SaveSlotStatus.Occupied ? "Wave " + metadata.CurrentWave + "  |  " + metadata.LastSavedUtc.ToLocalTime().ToString("g") : string.Empty;
                slotLabels[index].text = "Slot " + metadata.SlotNumber + (metadata.IsActive ? "  • ACTIVE\n" : "\n") + status + (string.IsNullOrEmpty(saved) ? string.Empty : "\n" + saved);
            }
            RefreshConfirmationButtons();
        }

        private void RefreshConfirmationButtons()
        {
            var visible = pendingAction != PendingAction.None;
            if (confirmButton != null) confirmButton.SetActive(visible);
            if (cancelConfirmationButton != null) cancelConfirmationButton.SetActive(visible);
        }

        private UnityEngine.UI.Button CreateButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 position)
        {
            var element = CreateElement(name, parent, anchorMin, anchorMax, pivot, size, position);
            var image = element.AddComponent<UnityEngine.UI.Image>();
            image.sprite = buttonSprite;
            image.type = buttonSprite == null ? UnityEngine.UI.Image.Type.Simple : UnityEngine.UI.Image.Type.Sliced;
            image.color = new Color(0.18f, 0.56f, 0.49f, 1f);
            var button = element.AddComponent<UnityEngine.UI.Button>();
            var label = CreateText("Label", element.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 18, FontStyles.Bold);
            label.text = name == "NewGame" ? "New Game" : name == "SaveActive" ? "Save Active" : name == "CloseX" ? "X" : name;
            label.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 position, float fontSize, FontStyles style)
        {
            var element = CreateElement(name, parent, anchorMin, anchorMax, pivot, size, position);
            var text = element.AddComponent<TextMeshProUGUI>();
            text.font = textFont == null ? TMP_Settings.defaultFontAsset : textFont;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = Color.white;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateElement(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 position = default)
        {
            var element = new GameObject(name, typeof(RectTransform));
            var rect = element.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return element;
        }
    }
}
