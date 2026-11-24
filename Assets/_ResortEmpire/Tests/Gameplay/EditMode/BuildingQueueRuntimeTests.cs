using System;
using System.Collections.Generic;
using NUnit.Framework;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Waves;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class BuildingQueueRuntimeTests
    {
        [Test]
        public void QueueDequeuesOldestArrivalFirst()
        {
            var queue = new BuildingQueueRuntime(BuildingId.Reception);
            var later = new NeedRuntime(1, 1, NeedType.Rooms, 2d);
            var earlier = new NeedRuntime(2, 1, NeedType.Rooms, 1d);
            queue.Enqueue(later);
            queue.Enqueue(earlier);

            Assert.That(queue.TryDequeue(out var first), Is.True);
            Assert.That(first.NeedUnitId, Is.EqualTo(2));
            Assert.That(queue.TryDequeue(out var second), Is.True);
            Assert.That(second.NeedUnitId, Is.EqualTo(1));
        }

        [Test]
        public void EqualArrivalTimesRemainStable()
        {
            var queue = new BuildingQueueRuntime(BuildingId.Reception);
            Assert.That(queue.Enqueue(new NeedRuntime(10, 1, NeedType.Rooms, 1d)), Is.True);
            Assert.That(queue.Enqueue(new NeedRuntime(2, 1, NeedType.Rooms, 1d)), Is.True);
            Assert.That(queue.Enqueue(new NeedRuntime(1, 1, NeedType.Rooms, 0d)), Is.True);

            Assert.That(queue.TryDequeue(out var first), Is.True);
            Assert.That(first.NeedUnitId, Is.EqualTo(1));
            Assert.That(queue.TryDequeue(out var second), Is.True);
            Assert.That(second.NeedUnitId, Is.EqualTo(10));
            Assert.That(queue.TryDequeue(out var third), Is.True);
            Assert.That(third.NeedUnitId, Is.EqualTo(2));
            Assert.That(queue.TryDequeue(out var none), Is.False);
            Assert.That(none, Is.Null);
        }

        [Test]
        public void DuplicateAdmissionIsRejected()
        {
            var queue = new BuildingQueueRuntime(BuildingId.Reception);
            var need = new NeedRuntime(1, 1, NeedType.Rooms, 1d);

            Assert.That(queue.Enqueue(need), Is.True);
            Assert.That(queue.Enqueue(need), Is.False);
            Assert.That(queue.Count, Is.EqualTo(1));
        }

        [Test]
        public void WrongBuildingAndActiveNeedAreNotQueued()
        {
            var queue = new BuildingQueueRuntime(BuildingId.Reception);
            var wrongBuilding = new NeedRuntime(1, 1, NeedType.Food, 0d);
            var active = new NeedRuntime(2, 1, NeedType.Rooms, 0d);
            active.TryStartService(0d);

            Assert.That(queue.Enqueue(wrongBuilding), Is.False);
            Assert.That(queue.Enqueue(active), Is.False);
            Assert.That(queue.Count, Is.Zero);
        }

        [Test]
        public void EmptyQueueIsReadOnlyAndDequeueFails()
        {
            var queue = new BuildingQueueRuntime(BuildingId.Reception);

            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.WaitingUnitIds, Is.Empty);
            Assert.That(queue.TryDequeue(out var empty), Is.False);
            Assert.That(empty, Is.Null);

            Assert.That(queue.Enqueue(new NeedRuntime(7, 1, NeedType.Rooms, 0d)), Is.True);
            var occupied = queue.WaitingUnitIds;
            Assert.That(occupied, Is.EqualTo(new long[] { 7 }));
            if (occupied is IList<long> list)
                Assert.That(() => list[0] = 99, Throws.TypeOf<NotSupportedException>());
            Assert.That(queue.TryDequeue(out var unit), Is.True);
            Assert.That(unit.NeedUnitId, Is.EqualTo(7));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.Contains(7), Is.False);
            Assert.That(occupied, Is.EqualTo(new long[] { 7 }));
        }
    }
}
