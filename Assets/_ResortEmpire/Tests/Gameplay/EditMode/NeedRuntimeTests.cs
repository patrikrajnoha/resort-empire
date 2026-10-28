using NUnit.Framework;
using ResortEmpire.Gameplay.Waves;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class NeedRuntimeTests
    {
        [Test]
        public void NeedStartsWaitingWithArrivalTimestamp()
        {
            var need = new NeedRuntime(1, 2, NeedType.Rooms, 3.5d);

            Assert.That(need.State, Is.EqualTo(NeedUnitState.WAITING));
            Assert.That(need.ArrivalTime, Is.EqualTo(3.5d));
            Assert.That(need.ServiceStartTime, Is.Null);
        }

        [Test]
        public void ImmediateServiceHasZeroWaitingTime()
        {
            var need = new NeedRuntime(1, 1, NeedType.Rooms, 4d);

            Assert.That(need.TryStartService(4d), Is.True);
            Assert.That(need.WaitingDuration, Is.EqualTo(0d));
        }

        [Test]
        public void CompletionRecordExcludesServiceDurationFromWaiting()
        {
            var need = new NeedRuntime(1, 1, NeedType.Rooms, 4d);
            need.TryStartService(9d);
            need.TryComplete(20d);

            var record = need.GetCompletionRecord();
            Assert.That(record.WaitingDuration, Is.EqualTo(5d));
            Assert.That(record.ServiceCompletionTime - record.ServiceStartTime, Is.EqualTo(11d));
        }

        [Test]
        public void ServiceCannotStartBeforeArrival()
        {
            var need = new NeedRuntime(1, 1, NeedType.Rooms, 4d);

            Assert.That(need.TryStartService(3.9d), Is.False);
            Assert.That(need.State, Is.EqualTo(NeedUnitState.WAITING));
        }

        [Test]
        public void InvalidLifecycleTransitionsAreRejected()
        {
            var need = new NeedRuntime(1, 1, NeedType.Rooms, 0d);

            Assert.That(need.TryComplete(1d), Is.False);
            Assert.That(need.TryStartService(0d), Is.True);
            Assert.That(need.TryStartService(0d), Is.False);
            Assert.That(need.TryComplete(1d), Is.True);
            Assert.That(need.TryComplete(2d), Is.False);
        }

        [Test]
        public void CompletionRecordIsUnavailableBeforeCompletion()
        {
            var need = new NeedRuntime(1, 1, NeedType.Rooms, 0d);

            Assert.Throws<System.InvalidOperationException>(() => need.GetCompletionRecord());
        }
    }
}
