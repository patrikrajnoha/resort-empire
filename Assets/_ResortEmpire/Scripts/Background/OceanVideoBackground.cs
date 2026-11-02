using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ResortEmpire.UI
{
    /// <summary>
    /// Plays the resort ocean loop into a RawImage while keeping a static Image
    /// visible until the first decoded frame is ready.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class OceanVideoBackground : MonoBehaviour
    {
        [SerializeField] private VideoClip videoClip;
        [SerializeField] private Image fallbackImage;
        [SerializeField] private RawImage videoImage;
        // The cropped extended video is displayed. The OceanVideo child is
        // sized so the measured original artwork region remains exactly
        // aligned with the original OceanBackground rect.
        [SerializeField] private Rect videoUvRect = new Rect(0f, 0f, 1f, 1f);
        [SerializeField, Min(16)] private int renderTextureWidth = 3072;
        [SerializeField, Min(16)] private int renderTextureHeight = 2048;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool pauseWhenApplicationIsPaused = true;

        private VideoPlayer videoPlayer;
        private RenderTexture playbackTexture;
        private bool hasPreparedVideo;
        private bool wasPlayingBeforePause;

        public bool IsPlaying => videoPlayer != null && videoPlayer.isPlaying;
        public static float NormalizedCyclePhase { get; private set; }

        private void Awake()
        {
            CacheReferences();
            CreatePlaybackTexture();
            ConfigureVideoPlayer();
            ShowFallback();
            KeepAtBackOfCanvas();
        }

        private void OnEnable()
        {
            CacheReferences();
            KeepAtBackOfCanvas();

            if (playOnEnable)
                PrepareVideo();
        }

        private void Update()
        {
            UpdateSynchronizedCycle();
        }

        private void OnDisable()
        {
            if (videoPlayer != null && videoPlayer.isPlaying)
            {
                wasPlayingBeforePause = true;
                videoPlayer.Pause();
            }
        }

        private void LateUpdate()
        {
            KeepAtBackOfCanvas();
        }

        private void UpdateSynchronizedCycle()
        {
            float duration = videoClip != null ? Mathf.Max((float)videoClip.length, 0.001f) : 6f;
            float playbackTime = videoPlayer != null && videoPlayer.isPrepared && videoPlayer.isPlaying
                ? (float)videoPlayer.time
                : Time.unscaledTime;

            NormalizedCyclePhase = Mathf.Repeat(playbackTime / duration, 1f);
            Shader.SetGlobalFloat("_OceanCyclePhase", NormalizedCyclePhase);
        }

        private void OnApplicationPause(bool paused)
        {
            if (!pauseWhenApplicationIsPaused || videoPlayer == null)
                return;

            if (paused)
            {
                wasPlayingBeforePause = videoPlayer.isPlaying;
                if (wasPlayingBeforePause)
                    videoPlayer.Pause();
                return;
            }

            if (wasPlayingBeforePause && hasPreparedVideo)
                videoPlayer.Play();
        }

        private void OnDestroy()
        {
            if (videoPlayer != null)
            {
                videoPlayer.prepareCompleted -= HandlePrepareCompleted;
                videoPlayer.errorReceived -= HandleVideoError;
                videoPlayer.started -= HandlePlaybackStarted;
                videoPlayer.Stop();
                videoPlayer.targetTexture = null;
            }

            if (playbackTexture == null)
                return;

            playbackTexture.Release();
            Destroy(playbackTexture);
            playbackTexture = null;
        }

        private void CacheReferences()
        {
            if (fallbackImage == null)
                fallbackImage = GetComponent<Image>();

            if (videoImage == null)
                videoImage = GetComponentInChildren<RawImage>(true);

            if (videoPlayer == null)
                videoPlayer = GetComponent<VideoPlayer>();

            if (videoPlayer == null)
                videoPlayer = gameObject.AddComponent<VideoPlayer>();
        }

        private void CreatePlaybackTexture()
        {
            if (playbackTexture != null)
                return;

            int width = Mathf.Max(16, renderTextureWidth);
            int height = Mathf.Max(16, renderTextureHeight);
            playbackTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "OceanBackgroundPlayback",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            playbackTexture.Create();
        }

        private void ConfigureVideoPlayer()
        {
            videoPlayer.playOnAwake = false;
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = videoClip;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = playbackTexture;
            videoPlayer.isLooping = true;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.skipOnDrop = true;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            videoPlayer.sendFrameReadyEvents = false;

            if (videoImage != null)
                videoImage.uvRect = videoUvRect;

            videoPlayer.prepareCompleted -= HandlePrepareCompleted;
            videoPlayer.errorReceived -= HandleVideoError;
            videoPlayer.started -= HandlePlaybackStarted;
            videoPlayer.prepareCompleted += HandlePrepareCompleted;
            videoPlayer.errorReceived += HandleVideoError;
            videoPlayer.started += HandlePlaybackStarted;
        }

        private void PrepareVideo()
        {
            if (videoPlayer == null || videoClip == null || playbackTexture == null)
            {
                HandleVideoError(videoPlayer, "Ocean video clip or playback texture is missing.");
                return;
            }

            hasPreparedVideo = false;
            ShowFallback();
            videoPlayer.Prepare();
        }

        private void HandlePrepareCompleted(VideoPlayer source)
        {
            hasPreparedVideo = true;
            source.isLooping = true;
            source.Play();
        }

        private void HandlePlaybackStarted(VideoPlayer source)
        {
            if (videoImage != null)
            {
                videoImage.texture = playbackTexture;
                videoImage.enabled = true;
            }

            if (fallbackImage != null)
                fallbackImage.enabled = false;
        }

        private void HandleVideoError(VideoPlayer source, string message)
        {
            hasPreparedVideo = false;
            if (source != null)
                source.Stop();

            ShowFallback();
            Debug.LogError($"Ocean background video could not be prepared or played: {message}", this);
        }

        private void ShowFallback()
        {
            if (fallbackImage != null)
                fallbackImage.enabled = true;

            if (videoImage != null)
                videoImage.enabled = false;
        }

        private void KeepAtBackOfCanvas()
        {
            if (transform.parent != null && transform.GetSiblingIndex() != 0)
                transform.SetAsFirstSibling();
        }

        private void OnValidate()
        {
            renderTextureWidth = Mathf.Max(16, renderTextureWidth);
            renderTextureHeight = Mathf.Max(16, renderTextureHeight);
        }
    }
}
