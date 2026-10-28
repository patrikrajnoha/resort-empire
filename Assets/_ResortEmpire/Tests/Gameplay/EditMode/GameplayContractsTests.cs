using System;
using NUnit.Framework;
using ResortEmpire.Gameplay;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class GameplayContractsTests
    {
        [Test]
        public void BuildingIdentifiersAreDistinct()
        {
            var values = (BuildingId[])Enum.GetValues(typeof(BuildingId));
            Assert.That(values, Has.Length.EqualTo(5));
            Assert.That(new System.Collections.Generic.HashSet<BuildingId>(values), Has.Count.EqualTo(5));
        }

        [Test]
        public void NeedIdentifiersAreDistinct()
        {
            var values = (NeedType[])Enum.GetValues(typeof(NeedType));
            Assert.That(values, Has.Length.EqualTo(5));
            Assert.That(new System.Collections.Generic.HashSet<NeedType>(values), Has.Count.EqualTo(5));
        }

        [Test]
        public void BuildingNeedMappingMatchesVersionFour()
        {
            Assert.That(BuildingNeedMapping.GetNeedType(BuildingId.Reception), Is.EqualTo(NeedType.Rooms));
            Assert.That(BuildingNeedMapping.GetNeedType(BuildingId.Restaurant), Is.EqualTo(NeedType.Food));
            Assert.That(BuildingNeedMapping.GetNeedType(BuildingId.BeachBar), Is.EqualTo(NeedType.Drinks));
            Assert.That(BuildingNeedMapping.GetNeedType(BuildingId.Pool), Is.EqualTo(NeedType.Relax));
            Assert.That(BuildingNeedMapping.GetNeedType(BuildingId.Bungalows), Is.EqualTo(NeedType.OceanView));
        }

        [Test]
        public void BuildingSnapshotUsesImmutableApprovedMapping()
        {
            var snapshot = new BuildingSnapshot(BuildingId.Restaurant, true, 3, 12d, 1);

            Assert.That(snapshot.BuildingId, Is.EqualTo(BuildingId.Restaurant));
            Assert.That(snapshot.NeedType, Is.EqualTo(NeedType.Food));
            Assert.That(snapshot.IsOwned, Is.True);
            Assert.That(snapshot.Capacity, Is.EqualTo(3));
            Assert.That(snapshot.ServiceDuration, Is.EqualTo(12d));
            Assert.That(snapshot.WorkerSlotCount, Is.EqualTo(1));
            Assert.That(typeof(BuildingSnapshot).GetProperty(nameof(BuildingSnapshot.Capacity)).CanWrite, Is.False);
        }

        [Test]
        public void NeedCompletionWaitingDurationExcludesServiceDuration()
        {
            var record = new NeedCompletionRecord(
                needUnitId: 1,
                waveId: 2,
                needType: NeedType.Food,
                buildingId: BuildingId.Restaurant,
                arrivalTime: 4.5d,
                serviceStartTime: 11.25d,
                serviceCompletionTime: 23.25d);

            Assert.That(record.WaitingDuration, Is.EqualTo(6.75d).Within(0.0000001d));
        }

        [Test]
        public void NeedCompletionRejectsInvalidTimestampOrdering()
        {
            Assert.Throws<ArgumentException>(() => new NeedCompletionRecord(
                1, 1, NeedType.Rooms, BuildingId.Reception, 5d, 4d, 6d));
            Assert.Throws<ArgumentException>(() => new NeedCompletionRecord(
                1, 1, NeedType.Rooms, BuildingId.Reception, 5d, 6d, 4d));
        }

        [Test]
        public void NeedCompletionRejectsMismatchedBuildingAndNeed()
        {
            Assert.Throws<ArgumentException>(() => new NeedCompletionRecord(
                1, 1, NeedType.Food, BuildingId.Reception, 0d, 0d, 1d));
        }
    }
}
