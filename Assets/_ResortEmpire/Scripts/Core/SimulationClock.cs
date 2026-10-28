using System;

namespace ResortEmpire.Gameplay
{
    /// <summary>Minimal persistence-ready value of the deterministic simulation clock.</summary>
    public readonly struct SimulationClockSnapshot : IEquatable<SimulationClockSnapshot>
    {
        public SimulationClockSnapshot(double currentTime, bool isPaused)
        {
            if (!IsFiniteNonNegative(currentTime))
                throw new ArgumentOutOfRangeException(nameof(currentTime), currentTime, "Clock time must be finite and non-negative.");

            CurrentTime = currentTime;
            IsPaused = isPaused;
        }

        public double CurrentTime { get; }
        public bool IsPaused { get; }

        public bool Equals(SimulationClockSnapshot other)
        {
            return CurrentTime.Equals(other.CurrentTime) && IsPaused == other.IsPaused;
        }

        public override bool Equals(object obj)
        {
            return obj is SimulationClockSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (CurrentTime.GetHashCode() * 397) ^ IsPaused.GetHashCode();
            }
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>
    /// Explicit, deterministic simulation time. It never reads wall-clock or Unity frame time.
    /// </summary>
    public sealed class SimulationClock
    {
        public SimulationClock()
            : this(new SimulationClockSnapshot(0d, false))
        {
        }

        public SimulationClock(double initialTime, bool startPaused = false)
            : this(new SimulationClockSnapshot(initialTime, startPaused))
        {
        }

        public SimulationClock(SimulationClockSnapshot snapshot)
        {
            CurrentTime = snapshot.CurrentTime;
            IsPaused = snapshot.IsPaused;
        }

        public double CurrentTime { get; private set; }
        public bool IsPaused { get; private set; }

        public void Advance(double deltaSeconds)
        {
            if (deltaSeconds < 0d || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "Delta time must be finite and non-negative.");

            if (IsPaused || deltaSeconds == 0d)
                return;

            var nextTime = CurrentTime + deltaSeconds;
            if (double.IsInfinity(nextTime) || double.IsNaN(nextTime))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "The resulting clock time is outside the supported range.");

            CurrentTime = nextTime;
        }

        public void Pause()
        {
            IsPaused = true;
        }

        public void Resume()
        {
            IsPaused = false;
        }

        public SimulationClockSnapshot CaptureSnapshot()
        {
            return new SimulationClockSnapshot(CurrentTime, IsPaused);
        }
    }
}
