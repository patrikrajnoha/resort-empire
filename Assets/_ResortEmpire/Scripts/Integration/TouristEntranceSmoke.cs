using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.Gameplay.Integration
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class TouristEntranceSmoke : MonoBehaviour
    {
        private sealed class Puff
        {
            public Image Image;
            public Vector2 Origin;
            public Vector2 Drift;
            public float Size;
            public float Duration;
            public float Age;
        }

        [SerializeField] private Sprite puffSprite;

        private readonly List<Puff> puffs = new List<Puff>();

        internal void PlayAt(Vector3 worldPosition)
        {
            if (puffSprite == null || !isActiveAndEnabled)
                return;

            Vector2 origin = transform.InverseTransformPoint(worldPosition);
            Emit(origin + new Vector2(-4f, 0f), new Vector2(-14f, 11f), 24f, 0.36f);
            Emit(origin + new Vector2(0f, 3f), new Vector2(1f, 18f), 32f, 0.44f);
            Emit(origin + new Vector2(5f, -1f), new Vector2(13f, 8f), 22f, 0.32f);
        }

        internal void Advance(float deltaTime)
        {
            foreach (var puff in puffs)
            {
                if (!puff.Image.gameObject.activeSelf)
                    continue;

                puff.Age += deltaTime;
                if (puff.Age >= puff.Duration)
                    puff.Image.gameObject.SetActive(false);
                else
                    Render(puff);
            }
        }

        internal void Clear()
        {
            foreach (var puff in puffs)
                if (puff.Image != null)
                    puff.Image.gameObject.SetActive(false);
        }

        private void Emit(Vector2 origin, Vector2 drift, float size, float duration)
        {
            var puff = GetPuff();
            puff.Origin = origin;
            puff.Drift = drift;
            puff.Size = size;
            puff.Duration = duration;
            puff.Age = 0f;
            puff.Image.transform.SetAsLastSibling();
            Render(puff);
            puff.Image.gameObject.SetActive(true);
        }

        private Puff GetPuff()
        {
            foreach (var puff in puffs)
                if (!puff.Image.gameObject.activeSelf)
                    return puff;

            var child = new GameObject("Smoke Puff", typeof(RectTransform), typeof(Image));
            child.layer = gameObject.layer;
            child.SetActive(false);
            child.transform.SetParent(transform, false);
            var image = child.GetComponent<Image>();
            image.sprite = puffSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var created = new Puff { Image = image };
            puffs.Add(created);
            return created;
        }

        private static void Render(Puff puff)
        {
            var progress = puff.Age / puff.Duration;
            var remaining = 1f - progress;
            var expansion = 1f - remaining * remaining;
            puff.Image.rectTransform.anchoredPosition = puff.Origin + puff.Drift * expansion;
            puff.Image.rectTransform.sizeDelta = Vector2.one * (puff.Size * Mathf.Lerp(0.46f, 1f, expansion));
            puff.Image.color = new Color(1f, 1f, 1f, 0.86f * remaining);
        }
    }
}
