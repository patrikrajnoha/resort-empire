using System;
using ResortEmpire.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// Minimal presentation coordinator for established state-driven windows.
    /// It does not create result data or open upgrade/VIP flows.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResortUiStateCoordinator : MonoBehaviour
    {
        [SerializeField]
        private ResortGameplayBootstrap gameplayBootstrap;

        [SerializeField]
        private GameObject homeIslandView;

        [SerializeField]
        private GameObject waveNeedsPanel;

        private IResortGameplayUiFacade gameplayFacade;
        private bool subscribed;
        private int previewOverrideDepth;

        public bool IsPreviewOverrideActive => previewOverrideDepth > 0;

        /// <summary>
        /// Temporarily suppresses normal state-driven visibility while a presentation-only
        /// debug preview owns the visible windows.
        /// </summary>
        public void BeginPreviewOverride()
        {
            previewOverrideDepth++;
        }

        /// <summary>
        /// Releases the debug preview ownership and reapplies the canonical gameplay state.
        /// </summary>
        public void EndPreviewOverride()
        {
            if (previewOverrideDepth == 0)
                return;

            previewOverrideDepth--;
            if (previewOverrideDepth == 0)
                SynchronizeImmediately();
        }

        private void Awake()
        {
            ResolveSceneReferences();
        }

        private void OnEnable()
        {
            BindToGameplay();
            SynchronizeImmediately();
        }

        private void Start()
        {
            BindToGameplay();
            SynchronizeImmediately();
        }

        private void OnDisable()
        {
            UnbindFromGameplay();
        }

        private void OnDestroy()
        {
            UnbindFromGameplay();
        }

        private void ResolveSceneReferences()
        {
            if (gameplayBootstrap == null)
                gameplayBootstrap = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (homeIslandView == null)
                homeIslandView = FindSceneObject("GameUI/SafeArea/HomeIslandViewCanvas/HomeIslandView");
            if (waveNeedsPanel == null)
                waveNeedsPanel = FindSceneObject("WaveNeedsPanel");
        }

        private void BindToGameplay()
        {
            if (subscribed)
                return;
            if (gameplayBootstrap == null)
                gameplayBootstrap = FindAnyObjectByType<ResortGameplayBootstrap>();
            if (gameplayBootstrap == null)
                return;

            gameplayFacade = gameplayBootstrap;
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
            ResolveSceneReferences();
            var phase = gameplayFacade == null ? GamePhase.AWAITING_WAVE : gameplayFacade.CurrentPhase;
            ApplyPhase(phase);
        }

        private void HandleStateChanged(GamePhase previousState, GamePhase nextState)
        {
            ApplyPhase(nextState);
        }

        private void ApplyPhase(GamePhase phase)
        {
            if (IsPreviewOverrideActive)
                return;

            if (homeIslandView != null)
                homeIslandView.SetActive(true);

            if (waveNeedsPanel != null)
            {
                var visible = phase == GamePhase.WAVE_INCOMING
                    || phase == GamePhase.WAVE_ACTIVE
                    || phase == GamePhase.WAVE_PAUSED
                    || phase == GamePhase.WAVE_RESOLVING;
                waveNeedsPanel.SetActive(visible);
            }
        }

        private static GameObject FindSceneObject(string path)
        {
            var parts = path.Split('/');
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0])
                    continue;

                var current = root.transform;
                for (var index = 1; index < parts.Length && current != null; index++)
                    current = current.Find(parts[index]);
                return current == null ? null : current.gameObject;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == path)
                    return root;
            }

            return null;
        }
    }
}
