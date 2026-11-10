using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ResortEmpire.Gameplay.Integration;
using UnityEngine;

namespace ResortEmpire.Gameplay.Tests
{
    /// <summary>Selecting an S2 need opens S3 for exactly the building that serves it.</summary>
    public sealed class WorkerAllocationFromWaveNeedsTests
    {
        private GameObject root;
        private ResortGameplayBootstrap bootstrap;
        private ResortUiStateCoordinator coordinator;
        private GameObject needs;
        private GameObject workers;

        [TearDown]
        public void TearDown()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }

        [TestCase(NeedType.Rooms, BuildingId.Reception)]
        [TestCase(NeedType.Food, BuildingId.Restaurant)]
        [TestCase(NeedType.Drinks, BuildingId.BeachBar)]
        [TestCase(NeedType.Relax, BuildingId.Pool)]
        [TestCase(NeedType.OceanView, BuildingId.Bungalows)]
        public void SelectingANeedOpensWorkerAllocationForItsBuilding(NeedType need, BuildingId building)
        {
            CreateActiveWave(ownedNeeds: 5);

            Assert.That(coordinator.OpenWorkerAllocationForNeed(need), Is.True);
            Assert.That(workers.activeSelf, Is.True);
            Assert.That(needs.activeSelf, Is.True, "S2 stays active underneath S3.");
            Assert.That(coordinator.WorkerAllocationFocus, Is.EqualTo(building));

            var context = coordinator.CaptureWorkerAllocationContext();
            Assert.That(context.Focus, Is.EqualTo(building));
            Assert.That(context.Destination, Is.EqualTo(BuildingId.BeachBar), "Beach Bar is the one vacant building.");
            if (building == BuildingId.BeachBar)
            {
                Assert.That(context.Target, Is.EqualTo(BuildingId.BeachBar), "A vacant service is the fixed destination.");
                Assert.That(context.CanMoveFrom(BuildingId.Reception), Is.True);
            }
            else
            {
                Assert.That(context.Target, Is.Null, "A staffed service can still give its Worker away.");
                Assert.That(context.CanMoveFrom(building), Is.True);
            }

            workers.SetActive(false);
            Assert.That(coordinator.IsWorkerAllocationOpen, Is.False);
            Assert.That(needs.activeSelf, Is.True, "Returning from S3 restores S2.");
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void EveryVisibleNeedOpensItsBuildingAndLockedNeedsAreRejected(int ownedNeeds)
        {
            CreateActiveWave(ownedNeeds);
            var visible = bootstrap.CurrentWaveSnapshot.Needs.Select(n => n.NeedType).ToList();
            Assert.That(visible, Has.Count.EqualTo(ownedNeeds));

            foreach (NeedType need in Enum.GetValues(typeof(NeedType)))
            {
                workers.SetActive(false);
                var opened = coordinator.OpenWorkerAllocationForNeed(need);
                Assert.That(opened, Is.EqualTo(visible.Contains(need)), need + " with " + ownedNeeds + " needs");
                Assert.That(workers.activeSelf, Is.EqualTo(opened), need.ToString());
                if (opened)
                    Assert.That(coordinator.WorkerAllocationFocus, Is.EqualTo(BuildingNeedMapping.GetBuildingId(need)));
                Assert.That(needs.activeSelf, Is.True);
            }
        }

        [Test]
        public void TargetedContextSurvivesTheMoveAndNeverRedirectsToTheNewVacancy()
        {
            CreateActiveWave(ownedNeeds: 3);
            Assert.That(coordinator.OpenWorkerAllocationForNeed(NeedType.Drinks), Is.True);
            var before = coordinator.CaptureWorkerAllocationContext();
            Assert.That(before.Destination, Is.EqualTo(BuildingId.BeachBar));
            Assert.That(before.CanMoveFrom(BuildingId.BeachBar), Is.False, "The destination has no Worker to give.");

            Assert.That(bootstrap.TryReassignWorker(BuildingId.Reception, before.Destination.Value).Succeeded, Is.True);
            var after = coordinator.CaptureWorkerAllocationContext();
            Assert.That(after.Focus, Is.EqualTo(BuildingId.BeachBar));
            Assert.That(after.IsStaffed(BuildingId.BeachBar), Is.True);
            Assert.That(after.Destination, Is.Null, "S3 keeps Beach Bar; the next tap cannot pull a Worker to Reception.");
            Assert.That(after.CanMoveFrom(BuildingId.Restaurant), Is.False);

            Assert.That(coordinator.OpenWorkerAllocationForNeed(NeedType.Rooms), Is.True);
            var rooms = coordinator.CaptureWorkerAllocationContext();
            Assert.That(rooms.Target, Is.EqualTo(BuildingId.Reception), "Reception is now the vacant service.");
            Assert.That(rooms.Destination, Is.EqualTo(BuildingId.Reception));
        }

        [Test]
        public void StaffedNeedKeepsItsFocusWhileItsWorkerIsMovedAway()
        {
            CreateActiveWave(ownedNeeds: 4);
            Assert.That(coordinator.OpenWorkerAllocationForNeed(NeedType.Relax), Is.True);
            var before = coordinator.CaptureWorkerAllocationContext();
            Assert.That(before.Focus, Is.EqualTo(BuildingId.Pool));
            Assert.That(before.IsStaffed(BuildingId.Pool), Is.True, "Buying the Pool assigns its Worker.");
            Assert.That(before.CanMoveFrom(BuildingId.Pool), Is.True);

            Assert.That(bootstrap.TryReassignWorker(BuildingId.Pool, before.Destination.Value).Succeeded, Is.True);
            var after = coordinator.CaptureWorkerAllocationContext();
            Assert.That(after.Focus, Is.EqualTo(BuildingId.Pool));
            Assert.That(after.Destination, Is.EqualTo(BuildingId.Pool), "The selected Pool is now where a Worker goes.");
        }

        [Test]
        public void ContextRejectsUnownedBuildingsAndBlocksMovesOutsideActivePlay()
        {
            var owned = new[] { BuildingId.Reception, BuildingId.Restaurant, BuildingId.BeachBar };
            var staffed = new[] { BuildingId.Reception, BuildingId.Restaurant };

            var pool = WorkerAllocationContext.Resolve(owned, staffed, BuildingId.Pool, BuildingId.Pool, GamePhase.WAVE_ACTIVE);
            Assert.That(pool.Focus, Is.Null, "An unpurchased service is never selected.");
            Assert.That(pool.Destination, Is.Null);
            Assert.That(pool.OwnedBuildings, Is.EqualTo(owned));

            foreach (var phase in new[] { GamePhase.WAVE_INCOMING, GamePhase.WAVE_PAUSED })
            {
                var waiting = WorkerAllocationContext.Resolve(owned, staffed, null, BuildingId.BeachBar, phase);
                Assert.That(waiting.Destination, Is.EqualTo(BuildingId.BeachBar));
                Assert.That(waiting.CanMoveFrom(BuildingId.Reception), Is.False, phase.ToString());
            }

            var full = WorkerAllocationContext.Resolve(owned, owned, null, null, GamePhase.AWAITING_WAVE);
            Assert.That(full.Destination, Is.Null, "No vacancy, no move.");
        }

        private void CreateActiveWave(int ownedNeeds)
        {
            root = new GameObject("WorkerAllocationFromWaveNeeds");
            bootstrap = root.AddComponent<ResortGameplayBootstrap>();
            var config = EconomyTestFixtures.Config(coins: 5000);
            config.UnlockWaves = new[] { 0, 0, 0, 0, 0 };
            Assert.That(bootstrap.TryConfigureProgression(config, new TestProgressionPersistence()), Is.True);
            if (ownedNeeds >= 4) Assert.That(bootstrap.TryPurchaseBuilding(BuildingId.Pool).Succeeded, Is.True);
            if (ownedNeeds >= 5) Assert.That(bootstrap.TryPurchaseBuilding(BuildingId.Bungalows).Succeeded, Is.True);

            coordinator = root.AddComponent<ResortUiStateCoordinator>();
            needs = Window("WaveNeeds");
            workers = Window("WorkerAllocation");
            Set("gameplayBootstrap", bootstrap);
            Set("homeIslandView", Window("HomeIsland"));
            Set("waveNeedsPanel", needs);
            Set("waveCompleteWindow", Window("WaveComplete"));
            Set("upgradeSuggestionWindow", Window("UpgradeSuggestion"));
            Set("workerAllocationWindow", workers);
            Set("areaUnlockWindow", Window("AreaUnlock"));
            // Edit Mode does not run OnEnable; bind exactly as the component does at runtime.
            typeof(ResortUiStateCoordinator).GetMethod("BindToGameplay", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(coordinator, null);

            Assert.That(bootstrap.TryStartWave().Succeeded, Is.True);
            bootstrap.AdvanceSimulation(5);
            Assert.That(bootstrap.CurrentPhase, Is.EqualTo(GamePhase.WAVE_ACTIVE));
            Assert.That(needs.activeSelf, Is.True);
        }

        private GameObject Window(string name)
        {
            var window = new GameObject(name);
            window.transform.SetParent(root.transform);
            window.SetActive(false);
            return window;
        }

        private void Set(string field, object value)
        {
            var info = typeof(ResortUiStateCoordinator).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, field);
            info.SetValue(coordinator, value);
        }
    }
}
