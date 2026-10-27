using UnityEngine;
namespace ResortEmpire.UI {
    public sealed class StarRatingView : MonoBehaviour {
        public UnityEngine.UI.Image[] Stars;
        public Sprite Filled;
        public Sprite Empty;
        [Range(0,5)] public int Rating = 4;
        public void SetRating(int value) { Rating=Mathf.Clamp(value,0,5); Apply(); }
        void OnEnable() { Apply(); }
        void OnValidate() { Apply(); }
        void Apply() { if(Stars==null)return; for(int i=0;i<Stars.Length;i++) if(Stars[i]) { Stars[i].sprite=i<Rating?Filled:Empty; Stars[i].color=Color.white; } }
    }
}
