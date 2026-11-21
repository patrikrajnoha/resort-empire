using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>Shows one guest at a visible table during an existing restaurant visit.</summary>
    [DisallowMultipleComponent]
    public sealed class RestaurantDiningVisual : MonoBehaviour
    {
        private const float MealCueDuration = 0.9f;
        private const float BiteInterval = 1.4f;
        private const float BiteDuration = 0.42f;

        [SerializeField] private Image portrait;
        [SerializeField] private Image mealCue;

        private GameObject guest;
        private Vector2 portraitOrigin;
        private float elapsed;

        internal bool IsOccupied => guest != null;
        internal GameObject Guest => guest;

        internal void Show(GameObject tourist, Vector3 worldPosition, float scale)
        {
            var touristImage = tourist.GetComponent<Image>();
            if (IsOccupied || portrait == null || touristImage == null || touristImage.sprite == null)
                return;

            guest = tourist;
            portraitOrigin = portrait.rectTransform.anchoredPosition;
            portrait.sprite = touristImage.sprite;
            transform.position = worldPosition;
            transform.localScale = Vector3.one * scale;
            elapsed = 0f;
            gameObject.SetActive(true);
            if (mealCue != null)
            {
                mealCue.color = Color.white;
                mealCue.gameObject.SetActive(true);
            }
        }

        internal void Advance(float deltaTime)
        {
            if (!IsOccupied)
                return;

            elapsed += deltaTime;
            var bite = Mathf.Clamp01(Mathf.Repeat(elapsed + 0.85f, BiteInterval) / BiteDuration);
            var nod = Mathf.Sin(bite * Mathf.PI);
            portrait.rectTransform.anchoredPosition = portraitOrigin + new Vector2(nod * 0.5f, -nod * 1.15f);
            if (mealCue != null && mealCue.gameObject.activeSelf)
            {
                var opacity = Mathf.Clamp01((MealCueDuration - elapsed) / 0.28f);
                mealCue.color = new Color(1f, 1f, 1f, opacity);
                if (opacity <= 0f)
                    mealCue.gameObject.SetActive(false);
            }
        }

        internal void Release(GameObject tourist)
        {
            if (guest == tourist)
                Clear();
        }

        internal void Clear()
        {
            if (portrait != null && IsOccupied)
            {
                portrait.rectTransform.anchoredPosition = portraitOrigin;
                portrait.sprite = null;
            }
            guest = null;
            gameObject.SetActive(false);
        }
    }
}
