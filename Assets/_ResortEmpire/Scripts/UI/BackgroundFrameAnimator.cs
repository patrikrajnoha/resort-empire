using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    [RequireComponent(typeof(Image))]
    public sealed class BackgroundFrameAnimator : MonoBehaviour
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField, Min(0.1f)] private float framesPerSecond = 5f;
        [SerializeField] private bool pingPong = true;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool smoothTransitions = true;
        [SerializeField, Min(0f)] private float transitionDuration = 0.18f;

        private Image targetImage;
        private Image transitionImage;
        private float frameTimer;
        private float transitionTimer;
        private float cycleTime;
        private int currentFrame;
        private int direction = 1;
        private bool isPlaying;
        private bool isTransitioning;

        public bool IsPlaying => isPlaying;
        public float NormalizedCyclePhase
        {
            get
            {
                float cycleDuration = GetCycleDuration();
                return cycleDuration > 0f ? cycleTime / cycleDuration : 0f;
            }
        }

        private void Awake()
        {
            targetImage = GetComponent<Image>();
            CreateTransitionImage();
            KeepAtBackOfCanvas();
            ApplyCurrentFrame();
        }

        private void OnEnable()
        {
            isPlaying = playOnEnable;
            frameTimer = 0f;
            cycleTime = 0f;
            ResetTransition();
            KeepAtBackOfCanvas();
        }

        private void LateUpdate()
        {
            KeepAtBackOfCanvas();
        }

        private void Update()
        {
            if (!isPlaying || frames == null || frames.Length < 2)
                return;

            UpdateTransition();

            frameTimer += Time.deltaTime;
            float cycleDuration = GetCycleDuration();
            if (cycleDuration > 0f)
                cycleTime = Mathf.Repeat(cycleTime + Time.deltaTime, cycleDuration);

            float frameDuration = 1f / Mathf.Max(0.1f, framesPerSecond);

            while (frameTimer >= frameDuration)
            {
                frameTimer -= frameDuration;
                AdvanceFrame();
            }
        }

        private void KeepAtBackOfCanvas()
        {
            if (transform.parent != null && transform.GetSiblingIndex() != 0)
                transform.SetAsFirstSibling();
        }

        public void Play()
        {
            isPlaying = true;
        }

        public void Stop()
        {
            isPlaying = false;
        }

        private void AdvanceFrame()
        {
            int nextFrame = currentFrame + direction;

            if (pingPong)
            {
                if (nextFrame >= frames.Length || nextFrame < 0)
                {
                    direction *= -1;
                    nextFrame = currentFrame + direction;
                }
            }
            else
            {
                nextFrame = (nextFrame + frames.Length) % frames.Length;
            }

            if (!smoothTransitions || transitionImage == null || transitionDuration <= 0f)
            {
                currentFrame = nextFrame;
                ApplyCurrentFrame();
                return;
            }

            currentFrame = nextFrame;
            transitionImage.sprite = frames[currentFrame];
            transitionImage.color = WithAlpha(targetImage.color, 0f);
            transitionImage.enabled = true;
            transitionTimer = 0f;
            isTransitioning = true;
        }

        private void ApplyCurrentFrame()
        {
            if (targetImage == null || frames == null || frames.Length == 0)
                return;

            currentFrame = Mathf.Clamp(currentFrame, 0, frames.Length - 1);
            if (frames[currentFrame] != null)
                targetImage.sprite = frames[currentFrame];
        }

        private float GetCycleDuration()
        {
            if (frames == null || frames.Length < 2)
                return 0f;

            int cycleSteps = pingPong ? (frames.Length - 1) * 2 : frames.Length;
            return cycleSteps / Mathf.Max(0.1f, framesPerSecond);
        }

        private void CreateTransitionImage()
        {
            if (targetImage == null || transitionImage != null)
                return;

            GameObject overlayObject = new GameObject("FrameTransition", typeof(RectTransform), typeof(Image));
            RectTransform overlayTransform = overlayObject.GetComponent<RectTransform>();
            overlayTransform.SetParent(transform, false);
            overlayTransform.anchorMin = Vector2.zero;
            overlayTransform.anchorMax = Vector2.one;
            overlayTransform.anchoredPosition = Vector2.zero;
            overlayTransform.sizeDelta = Vector2.zero;
            overlayTransform.localRotation = Quaternion.identity;
            overlayTransform.localScale = Vector3.one;

            transitionImage = overlayObject.GetComponent<Image>();
            transitionImage.type = targetImage.type;
            transitionImage.preserveAspect = targetImage.preserveAspect;
            transitionImage.fillCenter = targetImage.fillCenter;
            transitionImage.fillMethod = targetImage.fillMethod;
            transitionImage.fillAmount = targetImage.fillAmount;
            transitionImage.fillClockwise = targetImage.fillClockwise;
            transitionImage.fillOrigin = targetImage.fillOrigin;
            transitionImage.useSpriteMesh = targetImage.useSpriteMesh;
            transitionImage.pixelsPerUnitMultiplier = targetImage.pixelsPerUnitMultiplier;
            transitionImage.material = targetImage.material;
            transitionImage.maskable = targetImage.maskable;
            transitionImage.raycastTarget = false;
            transitionImage.enabled = false;
            overlayTransform.SetAsLastSibling();
        }

        private void UpdateTransition()
        {
            if (!isTransitioning || transitionImage == null)
                return;

            float frameDuration = 1f / Mathf.Max(0.1f, framesPerSecond);
            float duration = Mathf.Min(transitionDuration, frameDuration * 0.9f);
            transitionTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(transitionTimer / Mathf.Max(0.01f, duration));
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            transitionImage.color = WithAlpha(targetImage.color, easedProgress);

            if (progress < 1f)
                return;

            targetImage.sprite = frames[currentFrame];
            transitionImage.enabled = false;
            isTransitioning = false;
        }

        private void ResetTransition()
        {
            isTransitioning = false;
            transitionTimer = 0f;

            if (transitionImage == null)
                return;

            transitionImage.enabled = false;
            transitionImage.color = WithAlpha(targetImage != null ? targetImage.color : Color.white, 0f);
            ApplyCurrentFrame();
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private void OnValidate()
        {
            framesPerSecond = Mathf.Max(0.1f, framesPerSecond);
            transitionDuration = Mathf.Max(0f, transitionDuration);
        }
    }
}
