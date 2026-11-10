using System.Collections.Generic;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using ResortEmpire.Gameplay.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace ResortEmpire.UI
{
    /// <summary>
    /// S3 binding for anonymous Prototype Workers. One card per owned building; MOVE sends that
    /// building's Worker to the single vacant building for free. The scene's named-chef cards are
    /// reused only as card art: names, portraits and ratings are replaced, never shown.
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
        [SerializeField] private Sprite staffedIcon;
        [SerializeField] private Sprite vacantIcon;
        [SerializeField] private float cardSpacing = 12f;
        [SerializeField] private float sideMargin = 24f;
        [Tooltip("Outline on the card of the building S3 was opened for (for example from an S2 need).")]
        [SerializeField] private Color focusColor = new Color(1f, 0.48f, 0.21f);
        [SerializeField] private Vector2 focusOutline = new Vector2(3f, -3f);
        public UnityEvent<string> CommandFailed = new UnityEvent<string>();

        private readonly List<ChefCardView> cards = new List<ChefCardView>();
        private readonly List<BuildingId> cardBuildings = new List<BuildingId>();
        private string moveFeedback;

        private void Awake()
        {
            if (staticBackdrop != null) staticBackdrop.SetActive(false);
            // Only the template is reused; the other mock chef cards stay hidden.
            foreach (Transform child in cardContainer)
                child.gameObject.SetActive(child == cardTemplate.transform);
            AddCard(cardTemplate);
        }

        private void OnEnable()
        {
            if (gameplay == null) gameplay = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (navigation == null) navigation = FindAnyObjectByType<ResortUiStateCoordinator>();
            if (navigation != null) navigation.WorkerAllocationContextChanged += HandleContextChanged;
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

            if (header == null) return;
            var destination = context.Destination;
            var focus = context.Focus;
            if (moveFeedback != null) header.text = moveFeedback;
            else if (!destination.HasValue) header.text = context.Target.HasValue
                ? ResortUiText.BuildingName(context.Target.Value).ToUpperInvariant() + " ALREADY STAFFED"
                : "ALL BUILDINGS STAFFED";
            // Opened from a staffed S2 need: say so, because its queue is a capacity limit, not a missing Worker.
            else if (focus.HasValue && focus.Value != destination.Value && context.IsStaffed(focus.Value))
                header.text = ResortUiText.BuildingName(focus.Value).ToUpperInvariant() + " ALREADY STAFFED · "
                    + ResortUiText.BuildingName(destination.Value).ToUpperInvariant() + " NEEDS A WORKER";
            else header.text = "MOVE A WORKER TO " + ResortUiText.BuildingName(destination.Value).ToUpperInvariant();
        }

        private void Bind(ChefCardView card, BuildingId building, WorkerAllocationContext context)
        {
            var isStaffed = context.IsStaffed(building);
            card.SetAvailable(isStaffed);
            card.ChefName.text = ResortUiText.BuildingName(building);
            card.Status.text = isStaffed ? "STAFFED" : "VACANT";
            card.Appearance.alpha = 1f;
            card.Portrait.sprite = isStaffed ? staffedIcon : vacantIcon;
            card.Portrait.enabled = card.Portrait.sprite != null;
            card.MissingPortrait.SetActive(false);
            card.Stars.gameObject.SetActive(false);
            card.SelectButton.interactable = context.CanMoveFrom(building);
            var label = card.SelectButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = "MOVE";
            SetFocused(card, context.Focus == building);
        }

        private void SetFocused(ChefCardView card, bool focused)
        {
            var outline = card.GetComponent<UnityEngine.UI.Outline>();
            if (outline == null)
            {
                if (!focused) return;
                outline = card.gameObject.AddComponent<UnityEngine.UI.Outline>();
            }
            outline.effectColor = focusColor;
            outline.effectDistance = focusOutline;
            outline.enabled = focused;
        }

        private void AddCard(ChefCardView card)
        {
            var index = cards.Count;
            cards.Add(card);
            card.Selected.AddListener(() => Move(index));
        }

        // Up to five buildings share the sheet width; cards scale down instead of overflowing.
        private void Layout(int count)
        {
            if (count == 0) return;
            var cardRect = cardTemplate.GetComponent<RectTransform>().rect;
            var rowWidth = count * cardRect.width + (count - 1) * cardSpacing;
            var sheet = (RectTransform)cardContainer.parent;
            var scale = Mathf.Min(1f, (sheet.rect.width - 2f * sideMargin) / rowWidth);
            cardContainer.anchoredPosition = new Vector2((sheet.rect.width - rowWidth * scale) * 0.5f, cardContainer.anchoredPosition.y);
            cardContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rowWidth * scale);
            for (var i = 0; i < count; i++)
            {
                var rect = (RectTransform)cards[i].transform;
                rect.localScale = Vector3.one * scale;
                rect.anchoredPosition = new Vector2(i * (cardRect.width + cardSpacing) * scale, 0f);
            }
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
                moveFeedback = ResortUiText.BuildingName(destination.Value).ToUpperInvariant() + " SPEEDS UP, "
                    + ResortUiText.BuildingName(source).ToUpperInvariant() + " SLOWS DOWN";
                Refresh();
                return;
            }
            Debug.LogWarning(command.Message);
            CommandFailed.Invoke(command.Message);
            if (header != null) header.text = command.Message;
        }
    }
}
