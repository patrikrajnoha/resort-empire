using System.Collections.Generic;
using NUnit.Framework;
using ResortEmpire.Gameplay;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class GameStateTests
    {
        [Test]
        public void InitialStateIsAwaitingWave()
        {
            Assert.That(new GameState().CurrentState, Is.EqualTo(GamePhase.AWAITING_WAVE));
        }

        [Test]
        public void DocumentedForwardLifecycleIsAccepted()
        {
            var state = new GameState();

            Assert.That(state.TryTransition(GamePhase.WAVE_INCOMING), Is.True);
            Assert.That(state.TryTransition(GamePhase.WAVE_ACTIVE), Is.True);
            Assert.That(state.TryTransition(GamePhase.WAVE_RESOLVING), Is.True);
            Assert.That(state.TryTransition(GamePhase.WAVE_COMPLETE), Is.True);
            Assert.That(state.TryTransition(GamePhase.UPGRADE), Is.True);
            Assert.That(state.TryTransition(GamePhase.AWAITING_WAVE), Is.True);
        }

        [Test]
        public void InvalidTransitionsAreRejectedWithoutMutation()
        {
            var state = new GameState();

            Assert.That(state.TryTransition(GamePhase.WAVE_ACTIVE), Is.False);
            Assert.That(state.TryTransition(GamePhase.WAVE_PAUSED), Is.False);
            Assert.That(state.CurrentState, Is.EqualTo(GamePhase.AWAITING_WAVE));
        }

        [Test]
        public void SuccessfulTransitionsRaiseExactlyOneEvent()
        {
            var state = new GameState();
            var transitions = new List<(GamePhase Previous, GamePhase Current)>();
            state.StateChanged += (previous, current) => transitions.Add((previous, current));

            Assert.That(state.TryTransition(GamePhase.WAVE_INCOMING), Is.True);
            Assert.That(state.TryTransition(GamePhase.WAVE_INCOMING), Is.False);

            Assert.That(transitions, Has.Count.EqualTo(1));
            Assert.That(transitions[0].Previous, Is.EqualTo(GamePhase.AWAITING_WAVE));
            Assert.That(transitions[0].Current, Is.EqualTo(GamePhase.WAVE_INCOMING));
        }

        [Test]
        public void InvalidTransitionsRaiseNoEvent()
        {
            var state = new GameState();
            var eventCount = 0;
            state.StateChanged += (_, __) => eventCount++;

            Assert.That(state.TryTransition(GamePhase.WAVE_ACTIVE), Is.False);
            Assert.That(eventCount, Is.Zero);
        }

        [Test]
        public void PauseAndResumeRestoreActiveState()
        {
            var state = new GameState();
            state.TryTransition(GamePhase.WAVE_INCOMING);
            state.TryTransition(GamePhase.WAVE_ACTIVE);

            Assert.That(state.TryTransition(GamePhase.WAVE_PAUSED), Is.True);
            Assert.That(state.CurrentState, Is.EqualTo(GamePhase.WAVE_PAUSED));
            Assert.That(state.TryTransition(GamePhase.WAVE_ACTIVE), Is.True);
            Assert.That(state.CurrentState, Is.EqualTo(GamePhase.WAVE_ACTIVE));
        }

        [Test]
        public void PauseIsOnlyAvailableFromActiveWave()
        {
            var state = new GameState();

            Assert.That(state.TryTransition(GamePhase.WAVE_PAUSED), Is.False);
            state.TryTransition(GamePhase.WAVE_INCOMING);
            Assert.That(state.TryTransition(GamePhase.WAVE_PAUSED), Is.False);
        }

        [Test]
        public void PausedStateCannotSkipResumeToAnotherState()
        {
            var state = new GameState();
            state.TryTransition(GamePhase.WAVE_INCOMING);
            state.TryTransition(GamePhase.WAVE_ACTIVE);
            state.TryTransition(GamePhase.WAVE_PAUSED);

            Assert.That(state.TryTransition(GamePhase.WAVE_RESOLVING), Is.False);
            Assert.That(state.CurrentState, Is.EqualTo(GamePhase.WAVE_PAUSED));
        }
    }
}
