using System.Collections.Generic;
using NUnit.Framework;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Integration;
using UnityEngine;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class GuestQueueFeedbackModelTests
    {
        private const double Grace = 10d;
        private readonly List<GuestBubbleRequest> requests = new List<GuestBubbleRequest>();

        [Test]
        public void VisibleGuestsFollowTheWaitingQueueAndAreCapped()
        {
            var model = CreateModel();

            model.ObserveQueue(1, BuildingId.Reception, 2, 0d);
            Assert.That(model.GetVisibleGuests(BuildingId.Reception), Is.EqualTo(2));

            model.ObserveQueue(1, BuildingId.Reception, 9, 1d);
            Assert.That(model.GetWaiting(BuildingId.Reception), Is.EqualTo(9));
            Assert.That(model.GetVisibleGuests(BuildingId.Reception), Is.EqualTo(4));

            model.ObserveQueue(1, BuildingId.Reception, 0, 2d);
            Assert.That(model.GetVisibleGuests(BuildingId.Reception), Is.Zero);
            Assert.That(model.GetVisibleGuests(BuildingId.BeachBar), Is.Zero);
        }

        [Test]
        public void FrontWaitTracksTheOldestWaitingGuestInArrivalOrder()
        {
            var model = CreateModel();

            model.ObserveQueue(1, BuildingId.Restaurant, 1, 0d);
            model.ObserveQueue(1, BuildingId.Restaurant, 3, 5d);
            Assert.That(model.GetFrontWait(BuildingId.Restaurant, 12d), Is.EqualTo(12d));

            // The first guest started service; the next oldest arrived at 5.
            model.ObserveQueue(1, BuildingId.Restaurant, 2, 12d);
            Assert.That(model.GetFrontWait(BuildingId.Restaurant, 12d), Is.EqualTo(7d));

            model.ObserveQueue(1, BuildingId.Restaurant, 0, 13d);
            Assert.That(model.GetFrontWait(BuildingId.Restaurant, 13d), Is.Zero);
        }

        [Test]
        public void ShortQueuesStayQuietThenShowWaitingFeedbackOnTheFrontGuest()
        {
            var model = CreateModel();
            model.ObserveQueue(1, BuildingId.BeachBar, 1, 0d);

            Advance(model, 1f, 1d);
            Assert.That(requests, Is.Empty, "A queue that clears quickly must not raise a bubble.");

            Advance(model, 1f, 2d);
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0].BuildingId, Is.EqualTo(BuildingId.BeachBar));
            Assert.That(requests[0].Kind, Is.EqualTo(GuestBubbleKind.Waiting));
            Assert.That(requests[0].Anchor, Is.EqualTo(GuestBubbleAnchor.FrontGuest));

            Advance(model, 1f, 3d);
            Assert.That(requests, Is.Empty, "Feedback over one queue is occasional, not continuous.");
        }

        [Test]
        public void GuestsWaitingPastPatienceGraceShowUnhappyFeedback()
        {
            var model = CreateModel();
            model.ObserveQueue(1, BuildingId.Reception, 1, 0d);
            Advance(model, 0.1f, 0.1d);
            Advance(model, 2f, Grace + 1d);

            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0].Kind, Is.EqualTo(GuestBubbleKind.Unhappy));
        }

        [Test]
        public void QueuesLongerThanTheVisibleLineShowCrowdFeedbackOnTheLastGuest()
        {
            var model = CreateModel();
            model.ObserveQueue(1, BuildingId.Pool, 6, 0d);
            Advance(model, 0.1f, 0.1d);
            Advance(model, 2f, 2d);

            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0].Kind, Is.EqualTo(GuestBubbleKind.Crowded));
            Assert.That(requests[0].Anchor, Is.EqualTo(GuestBubbleAnchor.BackGuest));
        }

        [Test]
        public void LongSlowQueuesAlternateUnhappyAndCrowdFeedback()
        {
            var model = CreateModel();
            model.ObserveQueue(1, BuildingId.Pool, 6, 0d);
            Advance(model, 0.1f, Grace + 1d);

            var kinds = new List<GuestBubbleKind>();
            for (var step = 0; step < 4; step++)
            {
                Advance(model, 5f, Grace + 1d + step * 5d);
                foreach (var request in requests)
                    kinds.Add(request.Kind);
            }

            Assert.That(kinds, Is.EqualTo(new[]
            {
                GuestBubbleKind.Unhappy,
                GuestBubbleKind.Crowded,
                GuestBubbleKind.Unhappy,
                GuestBubbleKind.Crowded
            }));
        }

        [Test]
        public void GuestsServedWithinPatienceGraceShowHappyFeedbackAtTheBuilding()
        {
            var model = CreateModel();
            model.ObserveCompletion(Record(1, BuildingId.Restaurant, arrival: 0d, start: Grace));

            Advance(model, 0.1f, Grace);

            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0].Kind, Is.EqualTo(GuestBubbleKind.Happy));
            Assert.That(requests[0].Anchor, Is.EqualTo(GuestBubbleAnchor.Building));
        }

        [Test]
        public void GuestsServedAfterPatienceGraceNeverShowHappyFeedback()
        {
            var model = CreateModel();
            model.ObserveCompletion(Record(1, BuildingId.Restaurant, arrival: 0d, start: Grace + 0.5d));

            Advance(model, 0.1f, Grace + 1d);

            Assert.That(requests, Is.Empty);
        }

        [Test]
        public void HappyFeedbackIsOccasionalAndStaleCompletionsExpire()
        {
            var model = CreateModel();
            model.ObserveCompletion(Record(1, BuildingId.BeachBar, 0d, 1d));
            Advance(model, 0.1f, 1d);
            Assert.That(requests, Has.Count.EqualTo(1));

            model.ObserveCompletion(Record(2, BuildingId.BeachBar, 0d, 2d));
            Advance(model, 1f, 2d);
            Advance(model, 1f, 3d);
            Assert.That(requests, Is.Empty, "The building is still cooling down.");

            Advance(model, 3f, 6d);
            Assert.That(requests, Is.Empty, "A completion that could not show in time is dropped, not replayed late.");
        }

        [Test]
        public void ConcurrentBubblesAreCappedAcrossTheIsland()
        {
            var model = CreateModel();
            foreach (var building in new[] { BuildingId.Reception, BuildingId.Restaurant, BuildingId.BeachBar, BuildingId.Pool, BuildingId.Bungalows })
                model.ObserveQueue(1, building, 1, 0d);
            Advance(model, 0.1f, 0.1d);

            requests.Clear();
            model.Advance(2f, 2d, 0, requests);
            Assert.That(requests, Has.Count.EqualTo(3));

            requests.Clear();
            model.Advance(0.1f, 2.1d, 3, requests);
            Assert.That(requests, Is.Empty, "No new bubble while three are already on screen.");
        }

        [Test]
        public void ANewWaveForgetsThePreviousWavesQueuesAndPendingFeedback()
        {
            var model = CreateModel();
            model.ObserveQueue(1, BuildingId.Reception, 3, 0d);
            model.ObserveCompletion(Record(1, BuildingId.Restaurant, 0d, 1d));

            model.ObserveQueue(2, BuildingId.Reception, 1, 50d);

            Assert.That(model.GetWaiting(BuildingId.Reception), Is.EqualTo(1));
            Assert.That(model.GetFrontWait(BuildingId.Reception, 51d), Is.EqualTo(1d));
            Advance(model, 0.1f, 51d);
            Assert.That(requests, Is.Empty, "The previous wave's happy guest must not appear in the new wave.");
        }

        private static GuestQueueFeedbackModel CreateModel()
        {
            var settings = new GuestQueueFeedbackSettings
            {
                MaxVisibleGuests = 4,
                FirstNegativeDelay = 1.5f,
                NegativeInterval = 5f,
                HappyCooldown = 4f,
                HappyExpiry = 1.5f,
                BubbleLifetime = 2.4f,
                MaxConcurrentBubbles = 3
            };
            return new GuestQueueFeedbackModel(settings, Grace);
        }

        private void Advance(GuestQueueFeedbackModel model, float deltaSeconds, double simulationTime)
        {
            requests.Clear();
            model.Advance(deltaSeconds, simulationTime, 0, requests);
        }

        private static NeedCompletionRecord Record(long id, BuildingId building, double arrival, double start)
        {
            return new NeedCompletionRecord(id, 1, BuildingNeedMapping.GetNeedType(building), building, arrival, start, start + 1d);
        }
    }

    public sealed class GuestQueueLayoutTests
    {
        private static readonly Vector2[] StraightRoute = { Vector2.zero, new Vector2(0f, -200f) };

        [Test]
        public void SlotsAreEvenlySpacedAlongTheRouteFromTheEntrance()
        {
            var slots = new List<Vector2>();
            var placed = GuestQueueLayout.PlaceSlots(StraightRoute, 4, 10f, 30f, 20f, null, null, slots);

            Assert.That(placed, Is.EqualTo(4));
            AssertNear(slots[0], new Vector2(0f, -10f));
            AssertNear(slots[1], new Vector2(0f, -40f));
            AssertNear(slots[3], new Vector2(0f, -100f));
        }

        private static void AssertNear(Vector2 actual, Vector2 expected)
        {
            Assert.That(Vector2.Distance(actual, expected), Is.LessThan(0.01f), actual + " != " + expected);
        }

        [Test]
        public void SlotsSkipPositionsThatWouldOverlapBuildings()
        {
            var building = new Rect(-20f, -70f, 40f, 30f); // Covers y -70..-40.
            var slots = new List<Vector2>();
            GuestQueueLayout.PlaceSlots(StraightRoute, 3, 10f, 30f, 20f, p => !building.Contains(p), null, slots);

            Assert.That(slots, Has.Count.EqualTo(3));
            foreach (var slot in slots)
                Assert.That(building.Contains(slot), Is.False, slot.ToString());
        }

        [Test]
        public void SlotsKeepClearOfOtherQueuesAndNeverLeaveTheRoute()
        {
            var reserved = new List<Vector2> { new Vector2(0f, -40f) };
            var slots = new List<Vector2>();
            var placed = GuestQueueLayout.PlaceSlots(StraightRoute, 10, 10f, 30f, 25f, null, reserved, slots);

            Assert.That(placed, Is.InRange(1, 9), "This route has room for several guests but cannot fit all ten.");
            Assert.That(slots, Has.Count.EqualTo(placed));
            foreach (var slot in slots)
            {
                Assert.That(Vector2.Distance(slot, reserved[0]), Is.GreaterThanOrEqualTo(24.99f), slot.ToString());
                Assert.That(slot.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(slot.y, Is.InRange(-200f, 0f));
            }
        }

        [Test]
        public void SamplingFollowsBendsAndClampsToTheEnds()
        {
            var route = new[] { Vector2.zero, new Vector2(10f, 0f), new Vector2(10f, 10f) };

            AssertNear(GuestQueueLayout.Sample(route, 15f), new Vector2(10f, 5f));
            AssertNear(GuestQueueLayout.Sample(route, -5f), Vector2.zero);
            AssertNear(GuestQueueLayout.Sample(route, 100f), new Vector2(10f, 10f));
            Assert.That(GuestQueueLayout.GetLength(route), Is.EqualTo(20f).Within(0.001f));
        }
    }
}
