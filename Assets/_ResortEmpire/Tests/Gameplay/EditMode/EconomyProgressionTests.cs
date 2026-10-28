using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ResortEmpire.Gameplay.Progression;

namespace ResortEmpire.Gameplay.Tests
{
    internal static class EconomyTestFixtures
    {
        public static ProgressionConfig Config(int coins = 0) => new ProgressionConfig
        {
            InitialCoins = coins, Capacities = new[] { 3, 5, 7 }, UpgradeCosts = new[] { 200, 400 },
            PurchaseCosts = new[] { 0, 0, 0, 400, 700 }, UnlockWaves = new[] { 0, 0, 0, 3, 6 },
            ServiceDurations = new[] { 12d, 12d, 12d, 12d, 12d },
            InitialWorkers = new[] { BuildingId.Reception, BuildingId.Restaurant },
            BaseDemand = new[] { 12, 15, 18, 18, 21, 24, 24, 27 },
            IncomingDuration = 2, ArrivalDuration = 90, UtilizationStaffed = 1, UtilizationEmpty = 0.33,
            PatienceGrace = 10, PatienceLimit = 30, UnitFloor = 25, WaveFloor = 40, OceanViewWeight = 2,
            StarThresholds = new[] { 40d, 55d, 70d, 80d, 90d }, CoinsPerUnit = 5,
            CoinMultipliers = new[] { 0.6, 0.8, 1, 1.2, 1.5 }, EnergyRewards = new[] { 0, 0, 1, 2, 3 },
            ResortValueRewards = new[] { 1, 2, 3, 4, 5 }, RushStart = 45, RushDuration = 10, RushUnits = 6, DebutDemandFactor = 0.5
        };

        public static void Complete(ResortProgressionRuntime game)
        {
            Assert.That(game.TryStartWave(), Is.True, game.LastCommandError);
            game.Waves.Advance(10000);
            Assert.That(game.State.CurrentState, Is.EqualTo(GamePhase.WAVE_COMPLETE));
        }
        public static void ClaimAndContinue(ResortProgressionRuntime game)
        {
            Assert.That(game.TryClaimWaveReward(game.CurrentWaveResult.RewardId), Is.True, game.LastCommandError);
            Assert.That(game.TryContinueAfterResults(), Is.True, game.LastCommandError);
        }
        public static void FinishWaves(ResortProgressionRuntime game, int count)
        {
            for (var i = 0; i < count; i++) { Complete(game); ClaimAndContinue(game); }
        }
    }

    internal sealed class TestProgressionPersistence : IProgressionPersistence
    {
        public ProgressionSaveData Data;
        public bool FailWrites;
        public ProgressionSaveData Load() => Data?.Copy();
        public void Save(ProgressionSaveData data)
        {
            if (FailWrites) throw new IOException("Injected disk failure");
            Data = data.Copy();
        }
    }

    public sealed class ResourceManagerTests
    {
        [Test]
        public void SpendingExactBalanceAndRejectingInvalidAmountsNeverCreatesDebt()
        {
            var wallet = new ResourceManager(new ResourceSnapshot(10, 3, 2));
            var changes = 0;
            wallet.ResourcesChanged += _ => changes++;
            Assert.That(wallet.CanAffordCoins(10), Is.True);
            Assert.That(wallet.TrySpendCoins(11), Is.False);
            Assert.That(wallet.TrySpendCoins(-1), Is.False);
            Assert.That(wallet.TrySpendCoins(int.MinValue), Is.False);
            Assert.That(wallet.TryAddCoins(-1), Is.False);
            Assert.That(wallet.TryIncreaseResortValue(-1), Is.False);
            Assert.That(changes, Is.Zero);
            Assert.That(wallet.TrySpendCoins(10), Is.True);
            Assert.That(wallet.Resources.Coins, Is.Zero);
            Assert.That(wallet.TrySpendCoins(1), Is.False);
            Assert.That(wallet.TrySpendEnergy(3), Is.True);
            Assert.That(wallet.TrySpendEnergy(1), Is.False);
            Assert.That(wallet.TryAddEnergy(2), Is.True);
            Assert.That(wallet.TryIncreaseResortValue(4), Is.True);
            Assert.That(wallet.Resources.ResortValue, Is.EqualTo(6));
        }

        [Test]
        public void OverflowIsRejectedForEveryResource()
        {
            var wallet = new ResourceManager(new ResourceSnapshot(int.MaxValue, int.MaxValue, int.MaxValue));
            Assert.That(wallet.TryAddCoins(1), Is.False);
            Assert.That(wallet.TryAddEnergy(1), Is.False);
            Assert.That(wallet.TryIncreaseResortValue(1), Is.False);
            Assert.That(wallet.Resources.Coins, Is.EqualTo(int.MaxValue));
            Assert.That(wallet.Resources.Energy, Is.EqualTo(int.MaxValue));
            Assert.That(wallet.Resources.ResortValue, Is.EqualTo(int.MaxValue));
        }
    }

    public sealed class WaveRewardCalculatorTests
    {
        [Test]
        public void PerfectScoreRemainsExactlyOneHundredWithFractionalWeights()
        {
            var config = EconomyTestFixtures.Config();
            config.OceanViewWeight = 1.1;
            var records = Enumerable.Range(1, 27).Select(i =>
                new NeedCompletionRecord(i, 1, NeedType.OceanView, BuildingId.Bungalows, 0, 0, 1)).ToArray();
            var result = new WaveRewardCalculator(config).Calculate("r", 1, records);
            Assert.That(result.Perfect, Is.True);
            Assert.That(result.Satisfaction, Is.EqualTo(100));
        }

        [TestCase(0, 100)] [TestCase(10, 100)] [TestCase(20, 62.5)] [TestCase(30, 25)] [TestCase(100, 25)]
        public void UnitScoreUsesQueueWait(double wait, double expected)
        {
            Assert.That(new WaveRewardCalculator(EconomyTestFixtures.Config()).CalculateUnitScore(wait), Is.EqualTo(expected));
        }

        [TestCase(40, 1)] [TestCase(54.99, 1)] [TestCase(55, 2)] [TestCase(70, 3)]
        [TestCase(80, 4)] [TestCase(90, 5)] [TestCase(100, 5)]
        public void StarBoundariesAreInclusive(double satisfaction, int stars)
        {
            Assert.That(new WaveRewardCalculator(EconomyTestFixtures.Config()).CalculateStars(satisfaction), Is.EqualTo(stars));
        }

        [Test]
        public void OceanViewIsWeightedAndServiceDurationIsExcluded()
        {
            var calculator = new WaveRewardCalculator(EconomyTestFixtures.Config());
            var result = calculator.Calculate("r", 1, new[]
            {
                new NeedCompletionRecord(1, 1, NeedType.Rooms, BuildingId.Reception, 0, 0, 1000),
                new NeedCompletionRecord(2, 1, NeedType.OceanView, BuildingId.Bungalows, 0, 30, 31)
            });
            Assert.That(result.Satisfaction, Is.EqualTo(50));
            Assert.That(result.Rewards.Coins, Is.EqualTo(6));
            Assert.That(result.Perfect, Is.False);
            Assert.That(result.Waiting[0].AverageWait, Is.Zero);
            Assert.That(result.Waiting[1].AverageWait, Is.EqualTo(30));
        }

        [Test]
        public void FloorDoesNotGrantThreeStarsAndPerfectRequiresEveryUnitWithinGrace()
        {
            var calculator = new WaveRewardCalculator(EconomyTestFixtures.Config());
            var bad = calculator.Calculate("r", 1, new[] { new NeedCompletionRecord(1, 1, NeedType.Rooms, BuildingId.Reception, 0, 100, 110) });
            Assert.That(bad.Satisfaction, Is.EqualTo(40));
            Assert.That(bad.Stars, Is.EqualTo(1));
            var perfect = calculator.Calculate("r", 1, new[] { new NeedCompletionRecord(1, 1, NeedType.Rooms, BuildingId.Reception, 0, 10, 1000) });
            Assert.That(perfect.Perfect, Is.True);
            Assert.That(perfect.Rewards.Coins, Is.EqualTo(7));
        }

        [Test]
        public void RejectsDuplicatedOrForeignCompletionRecords()
        {
            var calculator = new WaveRewardCalculator(EconomyTestFixtures.Config());
            var record = new NeedCompletionRecord(1, 1, NeedType.Rooms, BuildingId.Reception, 0, 0, 1);
            Assert.Throws<ArgumentException>(() => calculator.Calculate("r", 1, new[] { record, record }));
            Assert.Throws<ArgumentException>(() => calculator.Calculate("r", 2, new[] { record }));
        }
    }

    public sealed class EconomyProgressionTests
    {
        [Test]
        public void WalletOverflowRejectsClaimWithoutChangingLedgerOrResources()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(int.MaxValue), disk))
            {
                EconomyTestFixtures.Complete(game);
                Assert.That(game.TryClaimWaveReward(game.CurrentWaveResult.RewardId), Is.False);
                Assert.That(game.Resources.Coins, Is.EqualTo(int.MaxValue));
                Assert.That(game.CurrentWaveResult.Claimed, Is.False);
                Assert.That(disk.Data.ClaimedRewardIds, Is.Empty);
            }
        }

        [Test]
        public void CompletionCheckpointRetryAndUpgradePhasePurchaseRestrictions()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(10000), disk))
            {
                EconomyTestFixtures.FinishWaves(game, 2);
                Assert.That(game.TryStartWave(), Is.True);
                disk.FailWrites = true;
                game.Waves.Advance(10000);
                disk.FailWrites = false;
                Assert.That(game.TrySaveProgress(), Is.True);
                Assert.That(disk.Data.Phase, Is.EqualTo(GamePhase.WAVE_COMPLETE));
                Assert.That(game.TryUpgradeBuilding(BuildingId.Reception), Is.False);
                Assert.That(game.TryClaimWaveReward(game.CurrentWaveResult.RewardId), Is.True);
                Assert.That(game.TryPurchaseBuilding(BuildingId.Pool), Is.False);
                Assert.That(game.TryUpgradeBuilding(BuildingId.Reception), Is.True);
                Assert.That(game.TryContinueAfterResults(), Is.True);
                Assert.That(game.TryPurchaseBuilding(BuildingId.Pool), Is.True);
            }
        }

        [Test]
        public void FailedContinueRetainsClaimedResultAndDoesNotAdvancePhase()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                EconomyTestFixtures.Complete(game);
                Assert.That(game.TryClaimWaveReward(game.CurrentWaveResult.RewardId), Is.True);
                var coins = game.Resources.Coins;
                disk.FailWrites = true;
                Assert.That(game.TryContinueAfterResults(), Is.False);
                Assert.That(game.State.CurrentState, Is.EqualTo(GamePhase.UPGRADE));
                Assert.That(game.CurrentWaveResult.Claimed, Is.True);
                Assert.That(game.Resources.Coins, Is.EqualTo(coins));
                Assert.That(game.TryClaimWaveReward(game.CurrentWaveResult.RewardId), Is.False);
            }
        }

        [Test]
        public void FailedPreWaveSaveDoesNotStartSimulationOrCommitWorkerMove()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                disk.FailWrites = true;
                Assert.That(game.TryStartWave(), Is.False);
                Assert.That(game.State.CurrentState, Is.EqualTo(GamePhase.AWAITING_WAVE));
                Assert.That(game.TryReassignWorker(BuildingId.Restaurant, BuildingId.BeachBar), Is.False);
                Assert.That(game.Workers.GetAssignedWorkerCount(BuildingId.Restaurant), Is.EqualTo(1));
                Assert.That(game.Workers.GetAssignedWorkerCount(BuildingId.BeachBar), Is.Zero);
            }
        }

        [Test]
        public void ConfigurationIsCopiedAndCannotMutateAnActiveWave()
        {
            var config = EconomyTestFixtures.Config();
            using (var game = new ResortProgressionRuntime(config, new TestProgressionPersistence()))
            {
                Assert.That(game.TryStartWave(), Is.True);
                config.Capacities[0] = 999;
                config.CoinMultipliers[0] = 999;
                config.BaseDemand[1] = 999;
                game.Waves.Advance(10000);
                Assert.That(game.Waves.HasConfigurationMismatch, Is.False);
                Assert.That(game.Buildings.Buildings[0].Capacity, Is.EqualTo(3));
                EconomyTestFixtures.ClaimAndContinue(game);
                Assert.That(game.GetNextWave().Demand[0].DemandTotal, Is.EqualTo(15));
            }
        }

        [Test]
        public void PurchaseAtExactBalanceSucceedsAndUnaffordablePurchaseDoesNotChangeOwnership()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
                EconomyTestFixtures.FinishWaves(game, 3);
            disk.Data.Coins = 399; // Seed a boundary fixture, not an in-game mutation API.
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                Assert.That(game.TryPurchaseBuilding(BuildingId.Pool), Is.False);
                Assert.That(game.Resources.Coins, Is.EqualTo(399));
                Assert.That(game.Workers.WorkerCount, Is.EqualTo(2));
            }
            disk.Data.Coins = 400;
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                Assert.That(game.TryPurchaseBuilding(BuildingId.Pool), Is.True);
                Assert.That(game.Resources.Coins, Is.Zero);
                Assert.That(game.Workers.WorkerCount, Is.EqualTo(3));
            }
        }

        [Test]
        public void ClaimIsDurableOnceOnlyAndBlocksNextWaveUntilContinue()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                EconomyTestFixtures.Complete(game);
                var result = game.CurrentWaveResult;
                Assert.That(result.ServedUnits, Is.EqualTo(36));
                Assert.That(game.Resources.Coins, Is.Zero);
                Assert.That(game.TryStartWave(), Is.False);
                Assert.That(game.TryContinueAfterResults(), Is.False);
                Assert.That(game.TryClaimWaveReward("wrong"), Is.False);
                Assert.That(game.TryClaimWaveReward(result.RewardId), Is.True);
                Assert.That(game.Resources.Coins, Is.EqualTo(result.Rewards.Coins));
                Assert.That(game.TryClaimWaveReward(result.RewardId), Is.False);
                Assert.That(game.TryStartWave(), Is.False);
            }
            using (var restored = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                var coins = restored.Resources.Coins;
                Assert.That(restored.State.CurrentState, Is.EqualTo(GamePhase.UPGRADE));
                Assert.That(restored.TryClaimWaveReward(restored.CurrentWaveResult.RewardId), Is.False);
                Assert.That(restored.Resources.Coins, Is.EqualTo(coins));
                Assert.That(restored.TryContinueAfterResults(), Is.True);
                Assert.That(restored.GetNextWave().WaveId, Is.EqualTo(2));
            }
        }

        [Test]
        public void UnclaimedResultSurvivesRestartAndFailedClaimIsRetryable()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk)) EconomyTestFixtures.Complete(game);
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                var id = game.CurrentWaveResult.RewardId;
                disk.FailWrites = true;
                Assert.That(game.TryClaimWaveReward(id), Is.False);
                Assert.That(game.LastFailureWasPersistence, Is.True);
                Assert.That(game.Resources.Coins, Is.Zero);
                Assert.That(game.CurrentWaveResult.Claimed, Is.False);
                Assert.That(game.State.CurrentState, Is.EqualTo(GamePhase.WAVE_COMPLETE));
                Assert.That(disk.Data.ClaimedRewardIds, Is.Empty);
                disk.FailWrites = false;
                Assert.That(game.TryClaimWaveReward(id), Is.True);
                Assert.That(disk.Data.ClaimedRewardIds.Count, Is.EqualTo(1));
                Assert.That(game.PersistenceDiagnostic, Is.Null);
            }
        }

        [Test]
        public void FailedCompletionSaveCanBeRetriedByClaimWithoutLosingResult()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                Assert.That(game.TryStartWave(), Is.True);
                disk.FailWrites = true;
                game.Waves.Advance(10000);
                Assert.That(game.CurrentWaveResult, Is.Not.Null);
                Assert.That(game.PersistenceDiagnostic, Is.Not.Null);
                Assert.That(disk.Data.Phase, Is.EqualTo(GamePhase.AWAITING_WAVE));
                disk.FailWrites = false;
                Assert.That(game.TryClaimWaveReward(game.CurrentWaveResult.RewardId), Is.True);
                Assert.That(game.ClaimedWaves, Is.EqualTo(1));
                Assert.That(game.TryClaimWaveReward(game.CurrentWaveResult.RewardId), Is.False);
            }
        }

        [Test]
        public void ActiveWaveRestartsFromPreWaveCheckpointWithSavedWorkers()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                Assert.That(game.TryReassignWorker(BuildingId.Restaurant, BuildingId.BeachBar), Is.True);
                Assert.That(game.TryStartWave(), Is.True);
                game.Waves.Advance(50);
                Assert.That(game.TryReassignWorker(BuildingId.BeachBar, BuildingId.Restaurant), Is.True);
            }
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                Assert.That(game.State.CurrentState, Is.EqualTo(GamePhase.AWAITING_WAVE));
                Assert.That(game.GetNextWave().WaveId, Is.EqualTo(1));
                Assert.That(game.Workers.GetAssignedWorkerCount(BuildingId.BeachBar), Is.EqualTo(1));
                Assert.That(game.Resources.Coins, Is.Zero);
            }
        }

        [Test]
        public void UpgradeUsesRealCapacityAndStopsAtLevelThreeWithoutChangingSpeedOrSlots()
        {
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(600), new TestProgressionPersistence()))
            {
                Assert.That(game.TryUpgradeBuilding(BuildingId.Reception), Is.True);
                Assert.That(game.Resources.Coins, Is.EqualTo(400));
                Assert.That(game.Buildings.Buildings[0].Capacity, Is.EqualTo(5));
                Assert.That(game.TryUpgradeBuilding(BuildingId.Reception), Is.True);
                Assert.That(game.Resources.Coins, Is.Zero);
                Assert.That(game.Buildings.Buildings[0].Capacity, Is.EqualTo(7));
                Assert.That(game.Buildings.Buildings[0].ServiceDuration, Is.EqualTo(12));
                Assert.That(game.Buildings.Buildings[0].WorkerSlotCount, Is.EqualTo(1));
                Assert.That(game.TryUpgradeBuilding(BuildingId.Reception), Is.False);
                Assert.That(game.TryUpgradeBuilding(BuildingId.Restaurant), Is.False);
                Assert.That(game.TryUpgradeBuilding(BuildingId.Pool), Is.False);
            }
        }

        [Test]
        public void FailedUpgradeDoesNotPublishOrChangeAnything()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(200), disk))
            {
                var changes = 0;
                game.ResourcesChanged += _ => changes++;
                game.BuildingsChanged += _ => changes++;
                disk.FailWrites = true;
                Assert.That(game.TryUpgradeBuilding(BuildingId.Reception), Is.False);
                Assert.That(game.Resources.Coins, Is.EqualTo(200));
                Assert.That(game.Buildings.States[0].Level, Is.EqualTo(1));
                Assert.That(disk.Data.Coins, Is.EqualTo(200));
                Assert.That(changes, Is.Zero);
            }
        }

        [Test]
        public void PurchasesRequireWaveGatesAndBundleOneWorkerAtomically()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(10000), disk))
            {
                Assert.That(game.TryPurchaseBuilding(BuildingId.Pool), Is.False);
                EconomyTestFixtures.FinishWaves(game, 3);
                Assert.That(game.Buildings.States[3].IsEligible, Is.True);
                Assert.That(game.GetNextWave().Demand.Any(x => x.NeedType == NeedType.Relax), Is.False);
                var coins = game.Resources.Coins;
                disk.FailWrites = true;
                Assert.That(game.TryPurchaseBuilding(BuildingId.Pool), Is.False);
                Assert.That(game.Resources.Coins, Is.EqualTo(coins));
                Assert.That(game.Workers.WorkerCount, Is.EqualTo(2));
                Assert.That(game.Buildings.States[3].IsOwned, Is.False);
                disk.FailWrites = false;
                Assert.That(game.TryPurchaseBuilding(BuildingId.Pool), Is.True);
                Assert.That(game.Resources.Coins, Is.EqualTo(coins - 400));
                Assert.That(game.Workers.WorkerCount, Is.EqualTo(3));
                Assert.That(game.Workers.GetAssignedWorkerCount(BuildingId.Pool), Is.EqualTo(1));
                Assert.That(game.TryPurchaseBuilding(BuildingId.Pool), Is.False);
                Assert.That(game.TryPurchaseBuilding(BuildingId.Bungalows), Is.False);
                EconomyTestFixtures.FinishWaves(game, 3);
                Assert.That(game.TryPurchaseBuilding(BuildingId.Bungalows), Is.True);
                Assert.That(game.Workers.WorkerCount, Is.EqualTo(4));
                Assert.That(game.Workers.EmptyOwnedBuildings.Count, Is.EqualTo(1));
            }
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                Assert.That(game.Workers.WorkerCount, Is.EqualTo(4));
                Assert.That(game.Buildings.States[4].IsOwned, Is.True);
            }
        }

        [Test]
        public void DelayedPurchaseHasGentleDebutAndFreezesExistingDemand()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(10000), disk))
            {
                EconomyTestFixtures.FinishWaves(game, 5);
                Assert.That(game.TryPurchaseBuilding(BuildingId.Pool), Is.True);
            }
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk))
            {
                var wave = game.GetNextWave();
                Assert.That(wave.WaveId, Is.EqualTo(6));
                Assert.That(wave.Demand.Single(x => x.NeedType == NeedType.Rooms).DemandTotal, Is.EqualTo(21));
                Assert.That(wave.Demand.Single(x => x.NeedType == NeedType.Relax).DemandTotal, Is.EqualTo(12));
                Assert.That(game.GetNextWave().Demand.Single(x => x.NeedType == NeedType.Relax).DemandTotal, Is.EqualTo(12));
                EconomyTestFixtures.FinishWaves(game, 1);
                Assert.That(game.GetNextWave().Demand.Single(x => x.NeedType == NeedType.Relax).DemandTotal, Is.EqualTo(24));
            }
        }

        [Test]
        public void FullEightWaveLoopIncludesRushAndStopsAfterEight()
        {
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), new TestProgressionPersistence()))
            {
                for (var wave = 1; wave <= 8; wave++)
                {
                    EconomyTestFixtures.Complete(game);
                    Assert.That(game.CurrentWaveResult.WaveId, Is.EqualTo(wave));
                    if (wave == 2) Assert.That(game.CurrentWaveResult.ServedUnits, Is.EqualTo(51));
                    EconomyTestFixtures.ClaimAndContinue(game);
                }
                Assert.That(game.ClaimedWaves, Is.EqualTo(8));
                Assert.That(game.GetNextWave(), Is.Null);
                Assert.That(game.TryStartWave(), Is.False);
            }
        }

        [Test]
        public void ActiveAndPausedWaveRejectEconomicChanges()
        {
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(10000), new TestProgressionPersistence()))
            {
                EconomyTestFixtures.FinishWaves(game, 3);
                Assert.That(game.TryStartWave(), Is.True);
                Assert.That(game.TryUpgradeBuilding(BuildingId.Reception), Is.False);
                Assert.That(game.TryPurchaseBuilding(BuildingId.Pool), Is.False);
                game.Waves.Advance(10);
                Assert.That(game.TryUpgradeBuilding(BuildingId.Reception), Is.False);
                Assert.That(game.Waves.TryPauseWave(), Is.True);
                Assert.That(game.TryPurchaseBuilding(BuildingId.Pool), Is.False);
                Assert.That(game.TryUpgradeBuilding(BuildingId.Reception), Is.False);
            }
        }

        [Test]
        public void CorruptAndUnsupportedSaveNeverResetPlayerProgress()
        {
            var disk = new TestProgressionPersistence();
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk)) { }
            disk.Data.Version = 9;
            Assert.Throws<InvalidDataException>(() => new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk));
            Assert.That(disk.Data.Version, Is.EqualTo(9));
            disk.Data.Version = 1;
            disk.Data.Coins = -1;
            Assert.Throws<InvalidDataException>(() => new ResortProgressionRuntime(EconomyTestFixtures.Config(), disk));
            Assert.That(disk.Data.Coins, Is.EqualTo(-1));
        }

        [Test]
        public void ReentrantClaimAndThrowingUiListenerCannotDuplicateOrUndoCommit()
        {
            using (var game = new ResortProgressionRuntime(EconomyTestFixtures.Config(), new TestProgressionPersistence()))
            {
                EconomyTestFixtures.Complete(game);
                var reward = game.CurrentWaveResult;
                game.ResourcesChanged += _ => Assert.That(game.TryClaimWaveReward(reward.RewardId), Is.False);
                game.ResourcesChanged += _ => throw new InvalidOperationException("UI error");
                Assert.That(game.TryClaimWaveReward(reward.RewardId), Is.True);
                Assert.That(game.Resources.Coins, Is.EqualTo(reward.Rewards.Coins));
                Assert.That(game.CurrentWaveResult.Claimed, Is.True);
            }
        }
    }
}
