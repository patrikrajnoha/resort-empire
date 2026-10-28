using NUnit.Framework;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Waves;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class ServiceSlotRuntimeTests
    {
        [Test]
        public void CapacityLimitsConcurrentService()
        {
            var slots = new ServiceSlotRuntime(2, 10d, 1d);
            Assert.That(slots.TryStartService(new NeedRuntime(1, 1, NeedType.Rooms, 0d), 0d), Is.True);
            Assert.That(slots.TryStartService(new NeedRuntime(2, 1, NeedType.Rooms, 0d), 0d), Is.True);
            Assert.That(slots.TryStartService(new NeedRuntime(3, 1, NeedType.Rooms, 0d), 0d), Is.False);
            Assert.That(slots.ActiveCount, Is.EqualTo(2));
        }

        [Test]
        public void StaffedBuildingCompletesFasterThanEmptyBuilding()
        {
            var staffed = new ServiceSlotRuntime(1, 4d, 1d);
            var empty = new ServiceSlotRuntime(1, 4d, 0.5d);
            staffed.TryStartService(new NeedRuntime(1, 1, NeedType.Rooms, 0d), 0d);
            empty.TryStartService(new NeedRuntime(2, 1, NeedType.Rooms, 0d), 0d);

            Assert.That(staffed.TimeUntilNextCompletion(), Is.LessThan(empty.TimeUntilNextCompletion()));
        }

        [Test]
        public void EmptyBuildingStillMakesProgress()
        {
            var slots = new ServiceSlotRuntime(1, 4d, 0.5d);
            slots.TryStartService(new NeedRuntime(1, 1, NeedType.Rooms, 0d), 0d);

            var result = slots.Advance(2d, 0d);
            Assert.That(result.CompletedUnits, Is.Empty);
            Assert.That(slots.ActiveServices[0].NormalizedProgress, Is.EqualTo(0.25d).Within(1e-9));
        }

        [Test]
        public void WorkerArrivalAcceleratesAlreadyOccupiedService()
        {
            var slots = new ServiceSlotRuntime(1, 4d, 0.5d);
            slots.TryStartService(new NeedRuntime(1, 1, NeedType.Rooms, 0d), 0d);
            slots.Advance(1d, 0d);
            var before = slots.ActiveServices[0].NormalizedProgress;
            slots.SetUtilization(1d);
            slots.Advance(1d, 1d);

            Assert.That(slots.ActiveServices[0].NormalizedProgress, Is.EqualTo(before + 0.25d).Within(1e-9));
        }

        [Test]
        public void WorkerDepartureSlowsAlreadyOccupiedService()
        {
            var slots = new ServiceSlotRuntime(1, 4d, 1d);
            slots.TryStartService(new NeedRuntime(1, 1, NeedType.Rooms, 0d), 0d);
            slots.Advance(1d, 0d);
            var before = slots.ActiveServices[0].NormalizedProgress;
            slots.SetUtilization(0.5d);
            slots.Advance(1d, 1d);

            Assert.That(slots.ActiveServices[0].NormalizedProgress, Is.EqualTo(before + 0.125d).Within(1e-9));
        }

        [Test]
        public void UtilizationChangeDoesNotResetProgress()
        {
            var slots = new ServiceSlotRuntime(1, 4d, 0.5d);
            slots.TryStartService(new NeedRuntime(1, 1, NeedType.Rooms, 0d), 0d);
            slots.Advance(1d, 0d);
            var progress = slots.ActiveServices[0].NormalizedProgress;
            slots.SetUtilization(1d);

            Assert.That(slots.ActiveServices[0].NormalizedProgress, Is.EqualTo(progress));
        }

        [Test]
        public void LargeStepCompletesMultipleSeatsWithoutLosingProgress()
        {
            var slots = new ServiceSlotRuntime(2, 1d, 1d);
            slots.TryStartService(new NeedRuntime(1, 1, NeedType.Rooms, 0d), 0d);
            slots.TryStartService(new NeedRuntime(2, 1, NeedType.Rooms, 0d), 0d);

            var result = slots.Advance(3d, 0d);
            Assert.That(result.ConsumedSeconds, Is.EqualTo(1d).Within(1e-9));
            Assert.That(result.CompletedUnits, Has.Count.EqualTo(2));
            Assert.That(slots.ActiveCount, Is.Zero);
        }

        [Test]
        public void ServiceCompletionTimestampIsTheActualBoundary()
        {
            var need = new NeedRuntime(1, 1, NeedType.Rooms, 2d);
            var slots = new ServiceSlotRuntime(1, 4d, 1d);
            slots.TryStartService(need, 2d);

            var result = slots.Advance(5d, 2d);
            Assert.That(result.CompletedUnits, Has.Count.EqualTo(1));
            Assert.That(need.GetCompletionRecord().ServiceCompletionTime, Is.EqualTo(6d).Within(1e-9));
        }

        [Test]
        public void ServiceStartsImmediatelyAndWaitingIsZero()
        {
            var need = new NeedRuntime(1, 1, NeedType.Rooms, 3d);
            var slots = new ServiceSlotRuntime(1, 2d, 1d);
            slots.TryStartService(need, 3d);

            Assert.That(need.WaitingDuration, Is.EqualTo(0d));
        }
    }
}
