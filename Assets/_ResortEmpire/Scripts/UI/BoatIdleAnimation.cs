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
        [SerializeField, Range(0f, 1f)] private float phaseOffset;
        [SerializeField, Min(0f)] private float bobAmplitude = 3f;
        [SerializeField, Min(0f)] private float tiltAmplitude = 0.8f;

        private Vector2 restPosition;
        private float restRotationZ;

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
                float phase = Mathf.Repeat(OceanVideoBackground.NormalizedCyclePhase + phaseOffset, 1f);
                float angle = phase * Mathf.PI * 2f;
                float bob = Mathf.Sin(angle) * bobAmplitude;
                float tilt = Mathf.Sin(angle) * tiltAmplitude;

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
            tiltAmplitude = Mathf.Max(0f, tiltAmplitude);

            CacheReferences();
            ApplyWaveVisibility();
        }
    }
}
