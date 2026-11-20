using System.Collections.Generic;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>
    /// S3 binding for anonymous Prototype Workers. Each owned building keeps its place in the row
    /// while the destination and available sources update after a move.
    /// </summary>
    public sealed class WorkerAllocationPresenter : MonoBehaviour
    {
        [SerializeField] private ResortGameplayBootstrap gameplay;
        [SerializeField] private ResortUiStateCoordinator navigation;
        [SerializeField] private TextMeshProUGUI header;
        [SerializeField] private RectTransform cardContainer;
        [SerializeField] private ChefCardView cardTemplate;
        [Tooltip("Decorative composition for static previews; the live island shows through at runtime.")]
        [SerializeField] private GameObject staticBackdrop;
        [SerializeField] private Sprite roundedSurface;
        [SerializeField] private TMP_FontAsset bodyFont;
        [Tooltip("Building thumbnails in BuildingId order.")]
        [SerializeField] private Sprite[] buildingIcons;
        [SerializeField] private float cardSpacing = 12f;
        [SerializeField] private float sideMargin = 24f;
        public UnityEvent<string> CommandFailed = new UnityEvent<string>();

        private const float CardTop = 66f;
        private const float CardHeight = 122f;
        private const float CardMaxWidth = 168f;
        private const float BottomPadding = 14f;
        private static readonly Color Ink = new Color32(26, 55, 53, 255);
        private static readonly Color MutedInk = new Color32(68, 96, 92, 255);
        private static readonly Color DestinationInk = new Color32(110, 66, 19, 255);
        private static readonly Color DestinationCard = new Color32(255, 244, 219, 255);

        private readonly List<ChefCardView> cards = new List<ChefCardView>();
        private readonly List<BuildingId> cardBuildings = new List<BuildingId>();
        private RectTransform sheet;
        private RectTransform content;
        private TextMeshProUGUI subtitle;
        private string moveFeedback;
        private SwipeDownToClose swipeDismiss;

        private void Awake()
        {
            if (staticBackdrop != null) staticBackdrop.SetActive(false);
            // Only the template is reused; the other mock chef cards stay hidden.
            foreach (Transform child in cardContainer)
                child.gameObject.SetActive(child == cardTemplate.transform);
            ConfigureSheet();
            ConfigureCardTemplate();
            AddCard(cardTemplate);
            EnsureSwipeDismissHandle();
        }

        private void OnEnable()
        {
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (navigation == null) navigation = FindAnyObjectByType<ResortUiStateCoordinator>();
            if (navigation != null) navigation.WorkerAllocationContextChanged += HandleContextChanged;
            if (swipeDismiss != null) swipeDismiss.ResetPosition();
            moveFeedback = null;
            if (gameplay == null) return;
            gameplay.StateChanged += HandleStateChanged;
            gameplay.WorkerAssignmentChanged += HandleWorkerAssignmentChanged;
            gameplay.BuildingsChanged += HandleBuildingsChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (navigation != null) navigation.WorkerAllocationContextChanged -= HandleContextChanged;
            if (gameplay == null) return;
            gameplay.StateChanged -= HandleStateChanged;
            gameplay.WorkerAssignmentChanged -= HandleWorkerAssignmentChanged;
            gameplay.BuildingsChanged -= HandleBuildingsChanged;
        }

        private void HandleStateChanged(GamePhase previous, GamePhase next) => Refresh();
        private void HandleWorkerAssignmentChanged(WorkerAssignmentChange change) => Refresh();
        private void HandleBuildingsChanged(IReadOnlyList<BuildingStateSnapshot> buildings) => Refresh();
        private void HandleContextChanged()
        {
            moveFeedback = null;
            Refresh();
        }

        // Target, focus and the move destination all come from one shared rule, so the buttons,
        // the header and Move() can never disagree about which building S3 is for.
        private WorkerAllocationContext CaptureContext() => navigation != null
            ? navigation.CaptureWorkerAllocationContext()
            : WorkerAllocationContext.Capture(gameplay, null, null);

        private void Refresh()
        {
            if (gameplay == null) return;
            var context = CaptureContext();
            cardBuildings.Clear();
            cardBuildings.AddRange(context.OwnedBuildings);

            while (cards.Count < cardBuildings.Count) AddCard(Instantiate(cardTemplate, cardContainer));
            for (var i = 0; i < cards.Count; i++)
            {
                cards[i].gameObject.SetActive(i < cardBuildings.Count);
                if (i < cardBuildings.Count) Bind(cards[i], cardBuildings[i], context);
            }
            Layout(cardBuildings.Count);

            var destination = context.Destination;
            if (destination.HasValue)
                header.text = "Move worker to " + ResortUiText.BuildingName(destination.Value);
            else
                header.text = context.Target.HasValue
                    ? ResortUiText.BuildingName(context.Target.Value) + " is staffed"
                    : "All buildings staffed";

            var focus = context.Focus;
            if (moveFeedback != null) subtitle.text = moveFeedback;
            else if (!destination.HasValue) subtitle.text = string.Empty;
            else if (!context.MovesAllowed) subtitle.text = "Worker moves are currently unavailable.";
            else if (focus.HasValue && context.IsStaffed(focus.Value))
                subtitle.text = ResortUiText.BuildingName(focus.Value) + " is already staffed. The building you choose will slow down.";
            else subtitle.text = "Moving a worker slows service at their current building.";
        }

        private void Bind(ChefCardView card, BuildingId building, WorkerAllocationContext context)
        {
            var isStaffed = context.IsStaffed(building);
            var isDestination = context.Destination == building;
            card.ChefName.text = ResortUiText.BuildingName(building);
            card.Status.text = isStaffed ? "Staffed" : "Vacant";
            card.Status.color = isStaffed ? MutedInk : DestinationInk;
            var iconIndex = (int)building;
            card.Portrait.sprite = buildingIcons != null && iconIndex >= 0 && iconIndex < buildingIcons.Length
                ? buildingIcons[iconIndex]
                : null;
            card.Portrait.enabled = card.Portrait.sprite != null;

            card.GetComponent<Image>().color = isDestination ? DestinationCard : Color.white;
            var button = card.SelectButton;
            button.interactable = context.CanMoveFrom(building);
            button.image.enabled = !isDestination;
            button.gameObject.SetActive(context.Destination.HasValue && (isDestination || isStaffed));
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            label.text = isDestination ? "Destination" : "Move from here";
            label.color = isDestination ? DestinationInk : Ink;
            SetFocused(card, context.Focus == building && !isDestination);
        }

        private void SetFocused(ChefCardView card, bool focused)
        {
            var outline = card.GetComponent<Outline>();
            if (outline == null)
            {
                if (!focused) return;
                outline = card.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color32(87, 151, 140, 255);
                outline.effectDistance = new Vector2(1f, -1f);
            }
            outline.enabled = focused;
        }

        private void AddCard(ChefCardView card)
        {
            var index = cards.Count;
            cards.Add(card);
            card.Selected.AddListener(() => Move(index));
        }

        private void OnRectTransformDimensionsChange()
        {
            if (sheet != null && isActiveAndEnabled) Layout(cardBuildings.Count);
        }

        private void Layout(int count)
        {
            var root = (RectTransform)transform;
            var viewportWidth = root.rect.width / Mathf.Abs(content.localScale.x);
            if (viewportWidth <= 0f) return;

            var gaps = Mathf.Max(0, count - 1) * cardSpacing;
            var desiredWidth = Mathf.Min(content.rect.width, Mathf.Max(320f, count * CardMaxWidth + gaps + 2f * sideMargin));
            var sheetWidth = Mathf.Min(desiredWidth, Mathf.Max(1f, viewportWidth - 16f));
            sheet.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sheetWidth);
            if (count == 0) return;

            var cardWidth = Mathf.Max(1f, (sheetWidth - 2f * sideMargin - gaps) / count);
            var rowWidth = count * cardWidth + gaps;
            PlaceAtTop(cardContainer, 0.5f, new Vector2(0f, -CardTop), new Vector2(rowWidth, CardHeight));
            for (var i = 0; i < count; i++)
            {
                var rect = (RectTransform)cards[i].transform;
                PlaceAtTop(rect, 0f, new Vector2(cardWidth * 0.5f + i * (cardWidth + cardSpacing), 0f),
                    new Vector2(cardWidth, CardHeight));
            }
        }

        private void EnsureSwipeDismissHandle()
        {
            var area = sheet.Find("SwipeDismissArea") as RectTransform;
            if (area == null)
            {
                var areaObject = new GameObject("SwipeDismissArea", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                areaObject.layer = sheet.gameObject.layer;
                areaObject.transform.SetParent(sheet, false);
                area = areaObject.transform as RectTransform;
                var image = areaObject.GetComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0.001f);
                image.raycastTarget = true;
            }

            PlaceAtTop(area, 0.5f, new Vector2(0f, 5f), new Vector2(190f, 25f));

            swipeDismiss = area.GetComponent<SwipeDownToClose>();
            if (swipeDismiss == null) swipeDismiss = area.gameObject.AddComponent<SwipeDownToClose>();
            swipeDismiss.Configure(sheet, GetComponent<ResortWindowView>());
            area.SetAsLastSibling();

            var handle = sheet.Find("Handle") as RectTransform;
            if (handle != null)
            {
                PlaceAtTop(handle, 0.5f, new Vector2(0f, -8f), new Vector2(46f, 4f));
                var handleImage = handle.GetComponent<Image>();
                if (handleImage != null)
                {
                    SetSurface(handleImage, new Color32(119, 154, 149, 255), 4f);
                    handleImage.raycastTarget = false;
                }
            }
        }

        private void ConfigureSheet()
        {
            sheet = (RectTransform)cardContainer.parent;
            content = (RectTransform)sheet.parent;
            sheet.anchorMin = sheet.anchorMax = sheet.pivot = new Vector2(0.5f, 0f);
            sheet.anchoredPosition = new Vector2(0f, 8f);
            sheet.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, CardTop + CardHeight + BottomPadding);
            SetSurface(sheet.GetComponent<Image>(), new Color32(231, 244, 239, 255), 1f);
            AddShadow(sheet.gameObject, 4f, 45);

            var scrim = transform.Find("Background").GetComponent<Image>();
            scrim.color = new Color32(10, 32, 32, 94);

            StyleText(header, 22f, Ink, TextAlignmentOptions.MidlineLeft);
            StretchAtTop(header.rectTransform, sideMargin, 65f, 17f, 29f);
            subtitle = new GameObject("Subtitle", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            subtitle.gameObject.layer = sheet.gameObject.layer;
            subtitle.transform.SetParent(sheet, false);
            subtitle.font = bodyFont;
            StyleText(subtitle, 11f, MutedInk, TextAlignmentOptions.MidlineLeft);
            StretchAtTop(subtitle.rectTransform, sideMargin, 65f, 45f, 16f);

            var close = sheet.Find("CloseButton").GetComponent<Button>();
            PlaceAtTop((RectTransform)close.transform, 1f, new Vector2(-36f, -12f), new Vector2(40f, 40f));
            SetSurface(close.image, new Color32(211, 234, 229, 255), 2f);
            var closeLabel = close.GetComponentInChildren<TextMeshProUGUI>(true);
            closeLabel.gameObject.SetActive(true);
            closeLabel.text = "×";
            StyleText(closeLabel, 20f, Ink, TextAlignmentOptions.Midline);
            StretchAtTop(closeLabel.rectTransform, 0f, 0f, 0f, 40f);
        }

        private void ConfigureCardTemplate()
        {
            var card = cardTemplate;
            SetSurface(card.GetComponent<Image>(), Color.white, 1.6f);
            AddShadow(card.gameObject, 2f, 23);
            var portraitFrame = card.Portrait.transform.parent;
            card.Portrait.transform.SetParent(card.transform, false);
            portraitFrame.gameObject.SetActive(false);
            card.Portrait.type = Image.Type.Simple;
            card.Portrait.color = Color.white;
            card.Portrait.preserveAspect = true;
            PlaceAtTop(card.Portrait.rectTransform, 0.5f, new Vector2(0f, -5f), new Vector2(74f, 48f));
            card.Stars.gameObject.SetActive(false);

            StyleText(card.ChefName, 15.5f, Ink, TextAlignmentOptions.Midline);
            StretchAtTop(card.ChefName.rectTransform, 4f, 4f, 53f, 20f);
            card.StatusBackground.enabled = false;
            StretchAtTop(card.StatusBackground.rectTransform, 4f, 4f, 72f, 14f);
            card.Status.font = bodyFont;
            StyleText(card.Status, 10.5f, MutedInk, TextAlignmentOptions.Midline);
            StretchAtTop(card.Status.rectTransform, 0f, 0f, 0f, 14f);

            var button = card.SelectButton;
            SetSurface(button.image, Color.white, 2.6f);
            StretchAtTop((RectTransform)button.transform, 9f, 9f, 88f, 27f);
            var colors = button.colors;
            colors.normalColor = new Color32(215, 237, 232, 255);
            colors.highlightedColor = new Color32(194, 229, 221, 255);
            colors.pressedColor = new Color32(161, 208, 197, 255);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color32(235, 240, 238, 255);
            button.colors = colors;
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            StyleText(label, 10.5f, Ink, TextAlignmentOptions.Midline);
            StretchAtTop(label.rectTransform, 2f, 2f, 0f, 27f);
        }

        private void SetSurface(Image image, Color color, float cornerScale)
        {
            image.sprite = roundedSurface;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = cornerScale;
            image.color = color;
        }

        private static void AddShadow(GameObject surface, float offset, byte opacity)
        {
            var shadow = surface.AddComponent<Shadow>();
            shadow.effectColor = new Color32(18, 59, 54, opacity);
            shadow.effectDistance = new Vector2(0f, -offset);
        }

        private static void StyleText(TextMeshProUGUI text, float size, Color color, TextAlignmentOptions alignment)
        {
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
        }

        private static void PlaceAtTop(RectTransform rect, float anchorX, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(anchorX, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void StretchAtTop(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private void Move(int index)
        {
            if (index >= cardBuildings.Count) return;
            var source = cardBuildings[index];
            var context = CaptureContext();
            if (!context.CanMoveFrom(source)) return;
            var destination = context.Destination;

            var command = gameplay.TryReassignWorker(source, destination.Value);
            if (command.Succeeded)
            {
                // Zero-sum feedback: name both the helped and the now-vacant building.
                moveFeedback = ResortUiText.BuildingName(destination.Value) + " speeds up. "
                    + ResortUiText.BuildingName(source) + " slows down.";
                Refresh();
                return;
            }
            Debug.LogWarning(command.Message);
            CommandFailed.Invoke(command.Message);
            subtitle.text = command.Message;
        }
    }
}
