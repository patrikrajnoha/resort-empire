using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class BoatIdleAnimation : MonoBehaviour
    {
        [SerializeField] private RectTransform boat;
        [SerializeField] private Image boatWave;
        [SerializeField] private RectTransform boatShadow;
        [SerializeField] private Vector2 shadowOffset = new Vector2(14f, -14f);
        [SerializeField] private bool isMoving;
        [SerializeField] private BackgroundFrameAnimator oceanAnimator;
        [SerializeField, Range(0f, 1f)] private float oceanPhaseOffset;
        [SerializeField, Min(0f)] private float bobAmplitude = 3f;
        [SerializeField, Min(0.01f)] private float bobFrequency = 0.28f;
        [SerializeField, Min(0f)] private float tiltAmplitude = 0.8f;
        [SerializeField, Min(0.01f)] private float tiltFrequency = 0.28f;

        private Vector2 restPosition;
        private float restRotationZ;
        private float animationTime;

        public bool IsMoving => isMoving;

        private void Awake()
        {
            CacheReferences();
            CaptureRestPose();
            ApplyWaveVisibility();
        }

        private void OnEnable()
        {
            CacheReferences();
            ApplyWaveVisibility();
            ApplyShadowPose();
        }

        private void LateUpdate()
        {
            if (!isMoving && boat != null)
            {
                float bob;
                float tilt;

                if (oceanAnimator != null)
                {
                    float phase = Mathf.Repeat(oceanAnimator.NormalizedCyclePhase + oceanPhaseOffset, 1f);
                    float angle = phase * Mathf.PI * 2f;
                    bob = Mathf.Sin(angle) * bobAmplitude;
                    tilt = Mathf.Sin(angle) * tiltAmplitude;
                }
                else
                {
                    animationTime += Time.deltaTime;
                    bob = Mathf.Sin(animationTime * Mathf.PI * 2f * bobFrequency) * bobAmplitude;
                    tilt = Mathf.Sin(animationTime * Mathf.PI * 2f * tiltFrequency) * tiltAmplitude;
                }

                boat.anchoredPosition = restPosition + Vector2.up * bob;
                boat.localRotation = Quaternion.Euler(0f, 0f, restRotationZ + tilt);
            }

            ApplyShadowPose();
        }

        public void SetMoving(bool moving)
        {
            CacheReferences();

            if (isMoving == moving)
            {
                ApplyWaveVisibility();
                return;
            }

            if (moving)
                RestoreRestPose();
            else
                CaptureRestPose();

            isMoving = moving;
            animationTime = 0f;
            ApplyWaveVisibility();
            ApplyShadowPose();
        }

        private void CacheReferences()
        {
            if (boat == null)
                boat = GetComponent<RectTransform>();
        }

        private void CaptureRestPose()
        {
            if (boat == null)
                return;

            restPosition = boat.anchoredPosition;
            restRotationZ = Mathf.DeltaAngle(0f, boat.localEulerAngles.z);
        }

        private void RestoreRestPose()
        {
            if (boat == null)
                return;

            boat.anchoredPosition = restPosition;
            boat.localRotation = Quaternion.Euler(0f, 0f, restRotationZ);
        }

        private void ApplyWaveVisibility()
        {
            if (boatWave != null)
                boatWave.enabled = isMoving;
        }

        private void ApplyShadowPose()
        {
            if (boat == null || boatShadow == null)
                return;

            boatShadow.anchoredPosition = boat.anchoredPosition + shadowOffset;
            boatShadow.localRotation = boat.localRotation;
        }

        private void OnValidate()
        {
            bobAmplitude = Mathf.Max(0f, bobAmplitude);
            bobFrequency = Mathf.Max(0.01f, bobFrequency);
            tiltAmplitude = Mathf.Max(0f, tiltAmplitude);
            tiltFrequency = Mathf.Max(0.01f, tiltFrequency);

            CacheReferences();
            ApplyWaveVisibility();
        }
    }
}
