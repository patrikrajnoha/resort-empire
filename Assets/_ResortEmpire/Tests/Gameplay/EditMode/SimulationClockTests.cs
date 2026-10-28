using System;
using NUnit.Framework;
using ResortEmpire.Gameplay;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class SimulationClockTests
    {
        [Test]
        public void InitialTimeIsZeroAndRunning()
        {
            var clock = new SimulationClock();

            Assert.That(clock.CurrentTime, Is.EqualTo(0d));
            Assert.That(clock.IsPaused, Is.False);
        }

        [Test]
        public void PositiveDeltasAccumulateDeterministically()
        {
            var clock = new SimulationClock();

            clock.Advance(1.25d);
            clock.Advance(2.75d);

            Assert.That(clock.CurrentTime, Is.EqualTo(4d).Within(0.0000001d));
        }

        [Test]
        public void EquivalentAdvancementSequencesProduceEquivalentTime()
        {
            var first = new SimulationClock();
            var second = new SimulationClock();

            first.Advance(0.1d);
            first.Advance(0.2d);
            first.Advance(0.7d);
            second.Advance(1.0d);

            Assert.That(first.CurrentTime, Is.EqualTo(second.CurrentTime).Within(0.0000001d));
        }

        [Test]
        public void ZeroDeltaDoesNotChangeTime()
        {
            var clock = new SimulationClock(3.5d);

            clock.Advance(0d);

            Assert.That(clock.CurrentTime, Is.EqualTo(3.5d));
        }

        [Test]
        public void NegativeNaNAndInfinityDeltasAreRejected()
        {
            var clock = new SimulationClock();

            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-0.1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.PositiveInfinity));
            Assert.That(clock.CurrentTime, Is.EqualTo(0d));
        }

        [Test]
        public void PauseFreezesTimeAndResumeContinuesFromTheSameValue()
        {
            var clock = new SimulationClock();
            clock.Advance(2d);
            clock.Pause();
            clock.Advance(10d);

            Assert.That(clock.CurrentTime, Is.EqualTo(2d));
            Assert.That(clock.IsPaused, Is.True);

            clock.Resume();
            clock.Advance(3d);

            Assert.That(clock.CurrentTime, Is.EqualTo(5d));
            Assert.That(clock.IsPaused, Is.False);
        }

        [Test]
        public void RepeatedPauseAndResumeAreSafe()
        {
            var clock = new SimulationClock();

            clock.Pause();
            clock.Pause();
            clock.Resume();
            clock.Resume();
            clock.Advance(2d);

            Assert.That(clock.CurrentTime, Is.EqualTo(2d));
        }

        [Test]
        public void SnapshotCanRestoreTimeAndPauseState()
        {
            var source = new SimulationClock();
            source.Advance(8.25d);
            source.Pause();

            var restored = new SimulationClock(source.CaptureSnapshot());

            Assert.That(restored.CurrentTime, Is.EqualTo(8.25d));
            Assert.That(restored.IsPaused, Is.True);
            restored.Advance(1d);
            Assert.That(restored.CurrentTime, Is.EqualTo(8.25d));
        }

        [Test]
        public void InvalidInitialTimeIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClock(-1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClock(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClock(double.PositiveInfinity));
        }
    }
}
