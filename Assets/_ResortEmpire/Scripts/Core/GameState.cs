using System;

namespace ResortEmpire.Gameplay
{
    /// <summary>Prototype lifecycle states. A waiting state is named AWAITING_WAVE, never IDLE.</summary>
    public enum GamePhase
    {
        AWAITING_WAVE = 0,
        WAVE_INCOMING = 1,
        WAVE_ACTIVE = 2,
        WAVE_PAUSED = 3,
        WAVE_RESOLVING = 4,
        WAVE_COMPLETE = 5,
        UPGRADE = 6
    }

    /// <summary>
    /// Single authority for legal Prototype lifecycle transitions.
    /// Wave completion conditions remain the responsibility of the future WaveManager.
    /// </summary>
    public sealed class GameState
    {
        private GamePhase stateBeforePause;

        public GameState()
        {
            CurrentState = GamePhase.AWAITING_WAVE;
            stateBeforePause = GamePhase.WAVE_ACTIVE;
        }

        public GamePhase CurrentState { get; private set; }

        /// <summary>Raised once after a successful state change.</summary>
        public event Action<GamePhase, GamePhase> StateChanged;

        public bool TryTransition(GamePhase nextState)
        {
            if (nextState == CurrentState || !CanTransition(CurrentState, nextState))
                return false;

            var previousState = CurrentState;
            if (nextState == GamePhase.WAVE_PAUSED)
                stateBeforePause = previousState;

            CurrentState = nextState;
            StateChanged?.Invoke(previousState, nextState);
            return true;
        }

        private bool CanTransition(GamePhase currentState, GamePhase nextState)
        {
            if (currentState == GamePhase.WAVE_PAUSED)
                return nextState == stateBeforePause;

            switch (currentState)
            {
                case GamePhase.AWAITING_WAVE:
                    return nextState == GamePhase.WAVE_INCOMING;
                case GamePhase.WAVE_INCOMING:
                    return nextState == GamePhase.WAVE_ACTIVE;
                case GamePhase.WAVE_ACTIVE:
                    return nextState == GamePhase.WAVE_RESOLVING
                        || nextState == GamePhase.WAVE_PAUSED;
                case GamePhase.WAVE_RESOLVING:
                    return nextState == GamePhase.WAVE_COMPLETE;
                case GamePhase.WAVE_COMPLETE:
                    return nextState == GamePhase.UPGRADE;
                case GamePhase.UPGRADE:
                    return nextState == GamePhase.AWAITING_WAVE;
                default:
                    return false;
            }
        }
    }
}
