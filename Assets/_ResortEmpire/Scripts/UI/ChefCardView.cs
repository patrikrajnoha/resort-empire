using TMPro;
using UnityEngine;
using UnityEngine.Events;
namespace ResortEmpire.UI {
    public sealed class ChefCardView : MonoBehaviour {
        public TextMeshProUGUI ChefName;
        public TextMeshProUGUI Status;
        public UnityEngine.UI.Image Portrait;
        public GameObject MissingPortrait;
        public UnityEngine.UI.Image StatusBackground;
        public Sprite AvailableBadge;
        public Sprite BusyBadge;
        public UnityEngine.UI.Button SelectButton;
        public CanvasGroup Appearance;
        public StarRatingView Stars;
        public UnityEvent Selected = new UnityEvent();
        public void SetData(string chefName, Sprite portrait, int rating, bool available) {
            ChefName.text=chefName; Portrait.sprite=portrait; Portrait.enabled=portrait;
            MissingPortrait.SetActive(!portrait); Stars.SetRating(rating); SetAvailable(available);
        }
        public void SetAvailable(bool available) {
            Status.text=available?"AVAILABLE":"BUSY";
            var badge=available?AvailableBadge:BusyBadge;
            if(badge) { StatusBackground.sprite=badge; StatusBackground.color=Color.white; }
            else StatusBackground.color=available?new Color(0,0.75f,0.55f):new Color(1,0.71f,0.2f);
            SelectButton.gameObject.SetActive(available); SelectButton.interactable=available;
            Appearance.alpha=available?1:0.5f;
        }
        public void Select() { if(SelectButton.interactable) Selected.Invoke(); }
    }
}
