using TMPro;
using UnityEngine;
namespace ResortEmpire.UI {
    public sealed class RewardItemView : MonoBehaviour {
        public TextMeshProUGUI Amount;
        public UnityEngine.UI.Image Icon;
        public bool TintIcon = true;
        public void SetReward(string label, Sprite icon, Color color) { Amount.text=label; Amount.color=color; Icon.sprite=icon; Icon.color=TintIcon?color:Color.white; }
    }
}
