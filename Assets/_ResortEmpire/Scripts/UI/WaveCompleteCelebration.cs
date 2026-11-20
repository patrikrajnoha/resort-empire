using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>Presentation only: pooled Canvas confetti and a staggered rating reveal.</summary>
    public sealed class WaveCompleteCelebration : MonoBehaviour
    {
        public RectTransform Content;
        public RectTransform ConfettiLayer;
        public StarRatingView Rating;
        public Sprite ConfettiSprite;
        public int ParticleCount = 64;
        private Image[] pieces;
        private Vector2[] starts, velocities;
        private float[] spins, delays;
        private bool played;

        private void OnEnable()
        {
            // Presenters may populate results before the window is shown. Also celebrate
            // when opening a preview or reopening an already populated result.
            if (Application.isPlaying) StartCoroutine(PlayAfterLayout());
        }

        private IEnumerator PlayAfterLayout()
        {
            yield return null;
            Play();
        }

        public void Play()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || played || Rating == null) return;
            played = true;
            StartCoroutine(Celebrate());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            played = false;
            if (pieces != null) foreach (var piece in pieces) if (piece) piece.gameObject.SetActive(false);
            if (Rating != null && Rating.Stars != null)
            {
                Rating.SetRating(Rating.Rating);
                foreach (var star in Rating.Stars) if (star) star.rectTransform.localScale = Vector3.one;
            }
        }

        private void BuildPool()
        {
            if (pieces != null || !ConfettiLayer) return;
            pieces = new Image[ParticleCount];
            starts = new Vector2[ParticleCount]; velocities = new Vector2[ParticleCount];
            spins = new float[ParticleCount]; delays = new float[ParticleCount];
            for (int i = 0; i < pieces.Length; i++)
            {
                var go = new GameObject("Confetti" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(ConfettiLayer, false);
                pieces[i] = go.GetComponent<Image>();
                pieces[i].sprite = ConfettiSprite; pieces[i].raycastTarget = false;
                pieces[i].preserveAspect = true;
                pieces[i].rectTransform.anchorMin = pieces[i].rectTransform.anchorMax = new Vector2(.5f, .5f);
                pieces[i].rectTransform.sizeDelta = new Vector2(24 + i % 10, 32 + i % 12);
            }
        }

        private IEnumerator Celebrate()
        {
            BuildPool();
            int earned = Rating.Rating;
            foreach (var star in Rating.Stars) if (star) { star.sprite = Rating.Empty; star.color = Color.white; }
            var random = new System.Random(421);
            if (pieces != null) for (int i = 0; i < pieces.Length; i++)
            {
                float side = i % 2 == 0 ? -1 : 1;
                starts[i] = new Vector2(side * Content.rect.width * .46f, -Content.rect.height * .28f);
                velocities[i] = new Vector2(-side * (100 + (float)random.NextDouble() * 210), 240 + (float)random.NextDouble() * 190);
                spins[i] = (float)random.NextDouble() * 480 - 240;
                delays[i] = (float)random.NextDouble() * .22f;
                pieces[i].color = i % 3 == 0 ? new Color(.2f,.85f,.78f) : i % 3 == 1 ? new Color(1,.68f,.35f) : Color.white;
            }
            float elapsed = 0;
            while (elapsed < 4.2f)
            {
                // Compilation/resume can produce a long first frame; keep the reveal readable.
                elapsed += Mathf.Min(Time.unscaledDeltaTime, .05f);
                for (int i = 0; i < Rating.Stars.Length; i++)
                {
                    var star = Rating.Stars[i]; if (!star) continue;
                    float t = elapsed - (.3f + i * .28f);
                    star.sprite = i < earned && t >= 0 ? Rating.Filled : Rating.Empty;
                    float scale = i < earned && t >= 0 && t < .35f ? 1 + Mathf.Sin(t / .35f * Mathf.PI) * .32f : 1;
                    star.rectTransform.localScale = Vector3.one * scale;
                }
                if (pieces != null) for (int i = 0; i < pieces.Length; i++)
                {
                    float t = elapsed - delays[i];
                    pieces[i].gameObject.SetActive(t >= 0 && t < 3.8f);
                    if (t < 0) continue;
                    pieces[i].rectTransform.anchoredPosition = starts[i] + velocities[i] * t + new Vector2(Mathf.Sin(t * 5 + i) * t * 8, -150 * t * t);
                    pieces[i].rectTransform.localRotation = Quaternion.Euler(0, 0, spins[i] * t);
                    pieces[i].rectTransform.localScale = new Vector3(.35f + Mathf.Abs(Mathf.Cos(t * 7 + i)) * .65f, 1, 1);
                    var color = pieces[i].color; color.a = 1 - Mathf.InverseLerp(2.6f, 3.8f, t); pieces[i].color = color;
                }
                yield return null;
            }
            if (pieces != null) foreach (var piece in pieces) piece.gameObject.SetActive(false);
        }
    }
}
