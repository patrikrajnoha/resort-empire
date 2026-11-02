using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using UnityEngine;

namespace ResortEmpire.UI
{
    /// <summary>
    /// Presentation-only arrival movement for the guest boat.
    /// The boat returns to its dock between waves and uses BoatIdleAnimation for water effects.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class BoatArrivalAnimation : MonoBehaviour
    {
        [SerializeField]
        private RectTransform boatRoot;

        [SerializeField]
        private BoatIdleAnimation boatIdleAnimation;

        [SerializeField]
        private BoatArrivalSignal arrivalSignal;

        [SerializeField, Min(0.1f)]
        private float arrivalDuration = 2.5f;

        [SerializeField]
        private Vector2 startMargin = new Vector2(220f, 180f);

        private IResortGameplayUiFacade gameplayFacade;
        private bool subscribed;
        private bool arrivalStarted;
        private bool moving;
        private bool paused;
        private float elapsed;
        private Vector2 dockPosition;
        private Quaternion dockRotation;
        private Vector2 startPosition;

        public bool IsMoving => moving;

        private void Awake()
        {
            CacheReferences();
            CaptureDockPose();
        }

        private void OnEnable()
        {
            CacheReferences();
            BindToGameplay();
            SynchronizeImmediately();
        }

        private void OnDisable()
        {
            UnbindFromGameplay();
            StopAndDock();
        }

        private void OnDestroy()
        {
            UnbindFromGameplay();
        }

        private void Update()
        {
            if (!moving || paused || boatRoot == null)
                return;

            elapsed += Time.deltaTime;
            var normalizedTime = Mathf.Clamp01(elapsed / Mathf.Max(0.1f, arrivalDuration));
            var easedTime = Mathf.SmoothStep(0f, 1f, normalizedTime);
            boatRoot.anchoredPosition = Vector2.LerpUnclamped(startPosition, dockPosition, easedTime);

            if (normalizedTime >= 1f)
            {
                StopAndDock(false);
                arrivalSignal?.NotifyArrived();
            }
        }

        private void CacheReferences()
        {
            if (boatRoot == null)
                boatRoot = GetComponent<RectTransform>();
            if (boatIdleAnimation == null)
                boatIdleAnimation = GetComponentInChildren<BoatIdleAnimation>();
            if (arrivalSignal == null)
                arrivalSignal = GetComponent<BoatArrivalSignal>();
        }

        private void CaptureDockPose()
        {
            if (boatRoot == null)
                return;

            dockPosition = boatRoot.anchoredPosition;
            dockRotation = boatRoot.localRotation;
        }

        private void BindToGameplay()
        {
            if (subscribed)
                return;

            gameplayFacade = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (gameplayFacade == null)
                return;

            gameplayFacade.StateChanged += HandleStateChanged;
            subscribed = true;
        }

        private void UnbindFromGameplay()
        {
            if (!subscribed || gameplayFacade == null)
                return;

            gameplayFacade.StateChanged -= HandleStateChanged;
            gameplayFacade = null;
            subscribed = false;
        }

        private void SynchronizeImmediately()
        {
            if (gameplayFacade == null)
            {
                StopAndDock();
                return;
            }

            switch (gameplayFacade.CurrentPhase)
            {
                case GamePhase.WAVE_INCOMING:
                    StartArrivalIfNeeded();
                    break;
                case GamePhase.WAVE_PAUSED:
                    paused = true;
                    break;
                case GamePhase.WAVE_ACTIVE:
                    paused = false;
                    break;
                default:
                    StopAndDock();
                    break;
            }
        }

        private void HandleStateChanged(GamePhase previousState, GamePhase nextState)
        {
            switch (nextState)
            {
                case GamePhase.WAVE_INCOMING:
                    StartArrivalIfNeeded();
                    break;
                case GamePhase.WAVE_PAUSED:
                    paused = true;
                    break;
                case GamePhase.WAVE_ACTIVE:
                    paused = false;
                    break;
                case GamePhase.WAVE_RESOLVING:
                case GamePhase.WAVE_COMPLETE:
                case GamePhase.UPGRADE:
                case GamePhase.AWAITING_WAVE:
                    StopAndDock();
                    break;
            }
        }

        private void StartArrivalIfNeeded()
        {
            if (arrivalStarted || boatRoot == null)
                return;

            CaptureDockPose();
            startPosition = CalculateStartPosition();
            arrivalSignal?.BeginArrival();
            boatRoot.anchoredPosition = startPosition;
            boatRoot.localRotation = dockRotation;
            elapsed = 0f;
            paused = false;
            moving = true;
            arrivalStarted = true;
            boatIdleAnimation?.SetMoving(true);
        }

        private void StopAndDock(bool resetArrival = true)
        {
            if (boatRoot != null)
            {
                boatRoot.anchoredPosition = dockPosition;
                boatRoot.localRotation = dockRotation;
            }

            moving = false;
            paused = false;
            elapsed = 0f;
            arrivalStarted = false;
            boatIdleAnimation?.SetMoving(false);
            if (resetArrival)
                arrivalSignal?.ResetArrival();
        }

        private Vector2 CalculateStartPosition()
        {
            var parent = boatRoot == null ? null : boatRoot.parent as RectTransform;
            if (parent == null)
                return dockPosition + new Vector2(900f, -700f);

            var bounds = parent.rect;
            var halfBoatSize = boatRoot.rect.size * 0.5f;
            return new Vector2(
                bounds.xMax + startMargin.x + halfBoatSize.x,
                bounds.yMin - startMargin.y - halfBoatSize.y);
        }
    }
}
