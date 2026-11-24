using System;
using System.Collections.Generic;
using ResortEmpire.Gameplay;
using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>Icon shown in a guest feedback bubble.</summary>
    public enum GuestBubbleKind
    {
        /// <summary>A guest was served within the patience grace period (full satisfaction).</summary>
        Happy = 0,
        /// <summary>Guests are waiting, but the front guest is still within the patience grace period.</summary>
        Waiting = 1,
        /// <summary>The front guest has waited past the patience grace period, so satisfaction is dropping.</summary>
        Unhappy = 2,
        /// <summary>More guests are waiting than the queue can show.</summary>
        Crowded = 3
    }

    /// <summary>Where a bubble should be drawn.</summary>
    public enum GuestBubbleAnchor
    {
        /// <summary>Above the building's entrance.</summary>
        Building = 0,
        /// <summary>Above the first visible waiting guest.</summary>
        FrontGuest = 1,
        /// <summary>Above the last visible waiting guest.</summary>
        BackGuest = 2
    }

    public readonly struct GuestBubbleRequest
    {
        public GuestBubbleRequest(BuildingId buildingId, GuestBubbleKind kind, GuestBubbleAnchor anchor)
        {
            BuildingId = buildingId;
            Kind = kind;
            Anchor = anchor;
        }

        public BuildingId BuildingId { get; }
        public GuestBubbleKind Kind { get; }
        public GuestBubbleAnchor Anchor { get; }
    }

    /// <summary>Inspector-tunable pacing for queue guests and satisfaction bubbles.</summary>
    [Serializable]
    public sealed class GuestQueueFeedbackSettings
    {
        [Tooltip("Most waiting guests drawn at one building. Longer queues show a crowd bubble instead.")]
        [Range(1, 6)] public int MaxVisibleGuests = 4;

        [Tooltip("Seconds a queue must exist before its first bubble, so queues that clear at once stay quiet.")]
        [Min(0f)] public float FirstNegativeDelay = 1.5f;

        [Tooltip("Seconds between repeated bubbles over the same waiting queue.")]
        [Min(0.5f)] public float NegativeInterval = 5f;

        [Tooltip("Minimum seconds between two happy bubbles at the same building.")]
        [Min(0f)] public float HappyCooldown = 4f;

        [Tooltip("Seconds a served guest's happy bubble may wait for a free slot before it is dropped.")]
        [Min(0f)] public float HappyExpiry = 1.5f;

        [Tooltip("How long one bubble stays on screen. A building never shows two bubbles at once.")]
        [Min(0.5f)] public float BubbleLifetime = 2.4f;

        [Tooltip("Most bubbles on screen across the island.")]
        [Range(1, 8)] public int MaxConcurrentBubbles = 3;

        [Tooltip("Used only when the economy configuration is unavailable. The scene's economy asset normally supplies it.")]
        [Min(0f)] public float FallbackPatienceGrace = 10f;

        public GuestQueueFeedbackSettings Copy()
        {
            return (GuestQueueFeedbackSettings)MemberwiseClone();
        }
    }

    /// <summary>
    /// Presentation-only model for waiting guests and satisfaction bubbles.
    /// It reads queue lengths and completion records produced by the wave simulation and never
    /// writes back: it cannot add demand, start service or change any satisfaction score.
    /// Guest wait times are estimated from a visual FIFO of observed queue growth, mirroring
    /// the simulation's arrival-ordered queue, and compared against the economy's patience grace
    /// so bubbles turn negative exactly when the existing satisfaction formula starts to drop.
    /// </summary>
    public sealed class GuestQueueFeedbackModel
    {
        private static readonly BuildingId[] AllBuildings =
        {
            BuildingId.Reception,
            BuildingId.Restaurant,
            BuildingId.BeachBar,
            BuildingId.Pool,
            BuildingId.Bungalows
        };

        private readonly GuestQueueFeedbackSettings settings;
        private readonly Dictionary<BuildingId, QueueState> queues = new Dictionary<BuildingId, QueueState>();
        private int waveId;
        private int fairnessOffset;

        public GuestQueueFeedbackModel(GuestQueueFeedbackSettings settings, double patienceGrace)
        {
            this.settings = (settings ?? new GuestQueueFeedbackSettings()).Copy();
            if (double.IsNaN(patienceGrace) || double.IsInfinity(patienceGrace) || patienceGrace < 0d)
                throw new ArgumentOutOfRangeException(nameof(patienceGrace), patienceGrace, "Patience grace must be finite and non-negative.");

            PatienceGrace = patienceGrace;
            foreach (var building in AllBuildings)
                queues.Add(building, new QueueState());
        }

        public double PatienceGrace { get; }
        public int MaxVisibleGuests => settings.MaxVisibleGuests;

        /// <summary>Forgets all queues, timers and pending bubbles.</summary>
        public void Reset()
        {
            waveId = 0;
            foreach (var queue in queues.Values)
                queue.Clear();
        }

        /// <summary>Records the current waiting length of one building. Occupied service seats are never included.</summary>
        public void ObserveQueue(int observedWaveId, BuildingId buildingId, int waiting, double simulationTime)
        {
            if (waiting < 0)
                throw new ArgumentOutOfRangeException(nameof(waiting), waiting, "The waiting queue cannot be negative.");

            BeginWave(observedWaveId);
            var queue = queues[buildingId];
            while (queue.Arrivals.Count < waiting)
                queue.Arrivals.Enqueue(simulationTime);
            while (queue.Arrivals.Count > waiting)
                queue.Arrivals.Dequeue();
        }

        /// <summary>Queues a happy bubble when the served guest's wait kept full satisfaction.</summary>
        public void ObserveCompletion(NeedCompletionRecord record)
        {
            BeginWave(record.WaveId);
            if (record.WaitingDuration > PatienceGrace)
                return;

            var queue = queues[record.BuildingId];
            queue.PendingHappy = true;
            queue.PendingHappyAge = 0f;
        }

        public int GetWaiting(BuildingId buildingId)
        {
            return queues[buildingId].Arrivals.Count;
        }

        public int GetVisibleGuests(BuildingId buildingId)
        {
            return Math.Min(GetWaiting(buildingId), settings.MaxVisibleGuests);
        }

        /// <summary>Estimated simulation seconds the front waiting guest has waited, or 0 with no queue.</summary>
        public double GetFrontWait(BuildingId buildingId, double simulationTime)
        {
            var arrivals = queues[buildingId].Arrivals;
            return arrivals.Count == 0 ? 0d : Math.Max(0d, simulationTime - arrivals.Peek());
        }

        /// <summary>
        /// Advances bubble pacing by unpaused presentation time and appends the bubbles to show now.
        /// </summary>
        public void Advance(float deltaSeconds, double simulationTime, int activeBubbleCount, List<GuestBubbleRequest> output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            if (deltaSeconds < 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "Delta time must be finite and non-negative.");

            foreach (var queue in queues.Values)
                queue.Tick(deltaSeconds, settings);

            // Rotate the starting building so one busy building cannot starve the others.
            for (var step = 0; step < AllBuildings.Length; step++)
            {
                var buildingId = AllBuildings[(step + fairnessOffset) % AllBuildings.Length];
                var queue = queues[buildingId];
                if (activeBubbleCount >= settings.MaxConcurrentBubbles || queue.BubbleCooldown > 0f)
                    continue;

                if (TryEmitNegative(buildingId, queue, simulationTime, output)
                    || TryEmitHappy(buildingId, queue, simulationTime, output))
                {
                    queue.BubbleCooldown = settings.BubbleLifetime;
                    activeBubbleCount++;
                }
            }

            fairnessOffset = (fairnessOffset + 1) % AllBuildings.Length;
        }

        private bool TryEmitNegative(BuildingId buildingId, QueueState queue, double simulationTime, List<GuestBubbleRequest> output)
        {
            if (queue.Arrivals.Count == 0 || queue.NegativeTimer > 0f)
                return false;

            var unhappy = GetFrontWait(buildingId, simulationTime) > PatienceGrace;
            var crowded = queue.Arrivals.Count > settings.MaxVisibleGuests;
            GuestBubbleRequest request;
            if (unhappy && crowded)
            {
                // Alternate so both problems stay visible on a long, slow queue.
                request = queue.ShowCrowdNext
                    ? new GuestBubbleRequest(buildingId, GuestBubbleKind.Crowded, GuestBubbleAnchor.BackGuest)
                    : new GuestBubbleRequest(buildingId, GuestBubbleKind.Unhappy, GuestBubbleAnchor.FrontGuest);
                queue.ShowCrowdNext = !queue.ShowCrowdNext;
            }
            else if (unhappy)
            {
                request = new GuestBubbleRequest(buildingId, GuestBubbleKind.Unhappy, GuestBubbleAnchor.FrontGuest);
            }
            else if (crowded)
            {
                request = new GuestBubbleRequest(buildingId, GuestBubbleKind.Crowded, GuestBubbleAnchor.BackGuest);
            }
            else
            {
                request = new GuestBubbleRequest(buildingId, GuestBubbleKind.Waiting, GuestBubbleAnchor.FrontGuest);
            }

            output.Add(request);
            queue.NegativeTimer = settings.NegativeInterval;
            return true;
        }

        private bool TryEmitHappy(BuildingId buildingId, QueueState queue, double simulationTime, List<GuestBubbleRequest> output)
        {
            if (!queue.PendingHappy || queue.HappyCooldown > 0f)
                return false;

            queue.PendingHappy = false;
            // A delighted guest next to a queue that is already losing satisfaction sends mixed signals.
            if (GetFrontWait(buildingId, simulationTime) > PatienceGrace)
                return false;

            output.Add(new GuestBubbleRequest(buildingId, GuestBubbleKind.Happy, GuestBubbleAnchor.Building));
            queue.HappyCooldown = settings.HappyCooldown;
            return true;
        }

        private void BeginWave(int observedWaveId)
        {
            if (observedWaveId == waveId)
                return;

            Reset();
            waveId = observedWaveId;
        }

        private sealed class QueueState
        {
            public readonly Queue<double> Arrivals = new Queue<double>();
            public float NegativeTimer = -1f;
            public float BubbleCooldown;
            public float HappyCooldown;
            public bool PendingHappy;
            public float PendingHappyAge;
            public bool ShowCrowdNext;

            public void Tick(float deltaSeconds, GuestQueueFeedbackSettings settings)
            {
                BubbleCooldown = Mathf.Max(0f, BubbleCooldown - deltaSeconds);
                HappyCooldown = Mathf.Max(0f, HappyCooldown - deltaSeconds);

                if (PendingHappy)
                {
                    PendingHappyAge += deltaSeconds;
                    if (PendingHappyAge > settings.HappyExpiry)
                        PendingHappy = false;
                }

                if (Arrivals.Count == 0)
                {
                    // The next queue earns a fresh first-bubble delay.
                    NegativeTimer = -1f;
                    ShowCrowdNext = false;
                    return;
                }

                // The queue was observed before this tick, so this tick's time already counts.
                var remaining = NegativeTimer < 0f ? settings.FirstNegativeDelay : NegativeTimer;
                NegativeTimer = Mathf.Max(0f, remaining - deltaSeconds);
            }

            public void Clear()
            {
                Arrivals.Clear();
                NegativeTimer = -1f;
                BubbleCooldown = 0f;
                HappyCooldown = 0f;
                PendingHappy = false;
                PendingHappyAge = 0f;
                ShowCrowdNext = false;
            }
        }
    }
}
