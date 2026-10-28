using NUnit.Framework;
using ResortEmpire.Gameplay;
using ResortEmpire.Gameplay.Staff;

namespace ResortEmpire.Gameplay.Tests
{
    public sealed class WorkerManagerTests
    {
        [Test]
        public void PrototypeStartsWithTwoWorkersAndOneEmptyOwnedBuilding()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();
            var workers = new WorkerManager(provider, state, new[] { BuildingId.Reception, BuildingId.Restaurant });

            Assert.That(workers.WorkerCount, Is.EqualTo(2));
            Assert.That(workers.GetAssignedWorkerCount(BuildingId.Reception), Is.EqualTo(1));
            Assert.That(workers.GetAssignedWorkerCount(BuildingId.Restaurant), Is.EqualTo(1));
            Assert.That(workers.EmptyOwnedBuildings, Is.EqualTo(new[] { BuildingId.BeachBar }));
        }

        [Test]
        public void InitialDistributionMustLeaveExactlyOneOwnedBuildingEmpty()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();

            Assert.Throws<System.ArgumentException>(() => new WorkerManager(
                provider,
                state,
                new[] { BuildingId.Reception, BuildingId.Restaurant, BuildingId.BeachBar }));
        }

        [Test]
        public void ReassignmentIsZeroSumAndRaisesNotification()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();
            var workers = new WorkerManager(provider, state, new[] { BuildingId.Reception, BuildingId.Restaurant });
            WorkerAssignmentChange change = default;
            workers.AssignmentChanged += value => change = value;

            Assert.That(workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.True);
            Assert.That(workers.WorkerCount, Is.EqualTo(2));
            Assert.That(workers.GetAssignedWorkerCount(BuildingId.Reception), Is.Zero);
            Assert.That(workers.GetAssignedWorkerCount(BuildingId.BeachBar), Is.EqualTo(1));
            Assert.That(change.SourceBuildingId, Is.EqualTo(BuildingId.Reception));
            Assert.That(change.DestinationBuildingId, Is.EqualTo(BuildingId.BeachBar));
        }

        [Test]
        public void MissingSourceOccupiedDestinationAndSameBuildingAreRejected()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();
            var workers = new WorkerManager(provider, state, new[] { BuildingId.Reception, BuildingId.Restaurant });

            Assert.That(workers.TryReassignWorker(BuildingId.BeachBar, BuildingId.Reception), Is.False);
            Assert.That(workers.TryReassignWorker(BuildingId.Reception, BuildingId.Restaurant), Is.False);
            Assert.That(workers.TryReassignWorker(BuildingId.Reception, BuildingId.Reception), Is.False);
        }

        [Test]
        public void UnownedDestinationIsRejected()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();
            var workers = new WorkerManager(provider, state, new[] { BuildingId.Reception, BuildingId.Restaurant });

            Assert.That(workers.TryReassignWorker(BuildingId.Reception, BuildingId.Pool), Is.False);
        }

        [Test]
        public void ReassignmentIsAvailableDuringActiveWaveButNotPaused()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();
            var workers = new WorkerManager(provider, state, new[] { BuildingId.Reception, BuildingId.Restaurant });
            state.TryTransition(GamePhase.WAVE_INCOMING);
            state.TryTransition(GamePhase.WAVE_ACTIVE);

            Assert.That(workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar), Is.True);
            state.TryTransition(GamePhase.WAVE_PAUSED);
            Assert.That(workers.TryReassignWorker(BuildingId.Restaurant, BuildingId.Reception), Is.False);
        }

        [Test]
        public void AssignmentPersistsBetweenWaves()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();
            var workers = new WorkerManager(provider, state, new[] { BuildingId.Reception, BuildingId.Restaurant });
            workers.TryReassignWorker(BuildingId.Reception, BuildingId.BeachBar);
            state.TryTransition(GamePhase.WAVE_INCOMING);
            state.TryTransition(GamePhase.WAVE_ACTIVE);
            state.TryTransition(GamePhase.WAVE_RESOLVING);
            state.TryTransition(GamePhase.WAVE_COMPLETE);
            state.TryTransition(GamePhase.UPGRADE);
            state.TryTransition(GamePhase.AWAITING_WAVE);

            Assert.That(workers.GetAssignedWorkerCount(BuildingId.BeachBar), Is.EqualTo(1));
            Assert.That(workers.GetAssignedWorkerCount(BuildingId.Reception), Is.Zero);
        }

        [Test]
        public void PoolOwnershipChangeAddsExactlyOneWorkerAndDuplicateIsSafe()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();
            var workers = new WorkerManager(provider, state, new[] { BuildingId.Reception, BuildingId.Restaurant });
            provider.SetOwned(BuildingId.Pool, true);

            Assert.That(workers.TryApplyOwnershipChange(BuildingId.Pool), Is.True);
            Assert.That(workers.WorkerCount, Is.EqualTo(3));
            Assert.That(workers.GetAssignedWorkerCount(BuildingId.Pool), Is.EqualTo(1));
            Assert.That(workers.TryApplyOwnershipChange(BuildingId.Pool), Is.False);
            Assert.That(workers.WorkerCount, Is.EqualTo(3));
        }

        [Test]
        public void OwnershipChangeDuringActiveWaveIsRejected()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();
            var workers = new WorkerManager(provider, state, new[] { BuildingId.Reception, BuildingId.Restaurant });
            provider.SetOwned(BuildingId.Pool, true);
            state.TryTransition(GamePhase.WAVE_INCOMING);
            state.TryTransition(GamePhase.WAVE_ACTIVE);

            Assert.That(workers.TryApplyOwnershipChange(BuildingId.Pool), Is.False);
            Assert.That(workers.WorkerCount, Is.EqualTo(2));
        }

        [Test]
        public void BungalowsOwnershipChangeAddsOneWorkerAfterPool()
        {
            var state = new GameState();
            var provider = new TestBuildingProvider();
            var workers = new WorkerManager(provider, state, new[] { BuildingId.Reception, BuildingId.Restaurant });
            provider.SetOwned(BuildingId.Pool, true);
            workers.TryApplyOwnershipChange(BuildingId.Pool);
            provider.SetOwned(BuildingId.Bungalows, true);

            Assert.That(workers.TryApplyOwnershipChange(BuildingId.Bungalows), Is.True);
            Assert.That(workers.WorkerCount, Is.EqualTo(4));
            Assert.That(workers.GetAssignedWorkerCount(BuildingId.Bungalows), Is.EqualTo(1));
        }
    }
}
