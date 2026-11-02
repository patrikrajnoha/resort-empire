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
        [SerializeField] private TextMeshProUGUI header;
        [SerializeField] private RectTransform cardContainer;
        [SerializeField] private ChefCardView cardTemplate;
        [Tooltip("Decorative composition for static previews; the live island shows through at runtime.")]
        [SerializeField] private GameObject staticBackdrop;
        [SerializeField] private Sprite staffedIcon;
        [SerializeField] private Sprite vacantIcon;
        [SerializeField] private float cardSpacing = 12f;
        [SerializeField] private float sideMargin = 24f;
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
            moveFeedback = null;
            if (gameplay == null) return;
            gameplay.StateChanged += HandleStateChanged;
            gameplay.WorkerAssignmentChanged += HandleWorkerAssignmentChanged;
            gameplay.BuildingsChanged += HandleBuildingsChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (gameplay == null) return;
            gameplay.StateChanged -= HandleStateChanged;
            gameplay.WorkerAssignmentChanged -= HandleWorkerAssignmentChanged;
            gameplay.BuildingsChanged -= HandleBuildingsChanged;
        }

        private void HandleStateChanged(GamePhase previous, GamePhase next) => Refresh();
        private void HandleWorkerAssignmentChanged(WorkerAssignmentChange change) => Refresh();
        private void HandleBuildingsChanged(IReadOnlyList<BuildingStateSnapshot> buildings) => Refresh();

        private void Refresh()
        {
            var staffed = new HashSet<BuildingId>();
            foreach (var worker in gameplay.CurrentWorkerAssignments) staffed.Add(worker.BuildingId);
            cardBuildings.Clear();
            BuildingId? vacant = null;
            foreach (var building in gameplay.Buildings)
            {
                if (!building.IsOwned) continue;
                cardBuildings.Add(building.BuildingId);
                if (!staffed.Contains(building.BuildingId)) vacant = building.BuildingId;
            }

            var canMove = vacant.HasValue
                && (gameplay.CurrentPhase == GamePhase.AWAITING_WAVE || gameplay.CurrentPhase == GamePhase.WAVE_ACTIVE);
            while (cards.Count < cardBuildings.Count) AddCard(Instantiate(cardTemplate, cardContainer));
            for (var i = 0; i < cards.Count; i++)
            {
                cards[i].gameObject.SetActive(i < cardBuildings.Count);
                if (i < cardBuildings.Count) Bind(cards[i], cardBuildings[i], staffed.Contains(cardBuildings[i]), canMove);
            }
            Layout(cardBuildings.Count);

            if (header == null) return;
            if (moveFeedback != null) header.text = moveFeedback;
            else if (!vacant.HasValue) header.text = "ALL BUILDINGS STAFFED";
            else header.text = "MOVE A WORKER TO " + ResortUiText.BuildingName(vacant.Value).ToUpperInvariant();
        }

        private void Bind(ChefCardView card, BuildingId building, bool isStaffed, bool canMove)
        {
            card.SetAvailable(isStaffed);
            card.ChefName.text = ResortUiText.BuildingName(building);
            card.Status.text = isStaffed ? "STAFFED" : "VACANT";
            card.Appearance.alpha = 1f;
            card.Portrait.sprite = isStaffed ? staffedIcon : vacantIcon;
            card.Portrait.enabled = card.Portrait.sprite != null;
            card.MissingPortrait.SetActive(false);
            card.Stars.gameObject.SetActive(false);
            card.SelectButton.interactable = isStaffed && canMove;
            var label = card.SelectButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = "MOVE";
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
            BuildingId? destination = null;
            var staffed = new HashSet<BuildingId>();
            foreach (var worker in gameplay.CurrentWorkerAssignments) staffed.Add(worker.BuildingId);
            foreach (var building in cardBuildings)
                if (!staffed.Contains(building)) destination = building;
            if (!destination.HasValue) return;

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
