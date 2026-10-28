using System;
using NUnit.Framework;
using ResortEmpire.Gameplay.Waves;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class WaveDefinitionTests
    {
        [Test]
        public void ValidDefinitionCopiesExplicitDemandAndEvents()
        {
            var definition = new WaveDefinition(
                2,
                90d,
                new[] { new NeedDemandDefinition(NeedType.Drinks, 16) },
                new[] { new ScheduledDemandEvent("rush", ScheduledDemandEventKind.SuddenRush, 45d, 10d, 6) });

            Assert.That(definition.WaveId, Is.EqualTo(2));
            Assert.That(definition.Demand[0].DemandTotal, Is.EqualTo(16));
            Assert.That(definition.ScheduledEvents[0].DemandUnits, Is.EqualTo(6));
        }

        [Test]
        public void NegativeDemandIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NeedDemandDefinition(NeedType.Rooms, -1));
        }

        [Test]
        public void NonPositiveArrivalDurationIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WaveDefinition(1, 0d, Array.Empty<NeedDemandDefinition>()));
        }

        [Test]
        public void DuplicateNeedDefinitionsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new WaveDefinition(
                1,
                10d,
                new[]
                {
                    new NeedDemandDefinition(NeedType.Rooms, 1),
                    new NeedDemandDefinition(NeedType.Rooms, 2)
                }));
        }

        [Test]
        public void EventOutsideArrivalWindowIsRejected()
        {
            var scheduledEvent = new ScheduledDemandEvent(
                "rush",
                ScheduledDemandEventKind.AdditionalDemand,
                8d,
                5d,
                1,
                NeedType.Rooms);

            Assert.Throws<ArgumentException>(() => new WaveDefinition(
                1,
                10d,
                new[] { new NeedDemandDefinition(NeedType.Rooms, 1) },
                new[] { scheduledEvent }));
        }

        [Test]
        public void SuddenRushIsOnlyValidForWaveTwoAndHasDynamicTarget()
        {
            var rush = new ScheduledDemandEvent("rush", ScheduledDemandEventKind.SuddenRush, 4d, 2d, 2);
            Assert.That(rush.TargetNeed, Is.Null);
            Assert.Throws<ArgumentException>(() => new WaveDefinition(1, 10d, Array.Empty<NeedDemandDefinition>(), new[] { rush }));
        }
    }
}
