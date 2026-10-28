using System;
using System.Collections.Generic;
using System.IO;
using ResortEmpire.Gameplay.Staff;
using ResortEmpire.Gameplay.Waves;

namespace ResortEmpire.Gameplay.Progression
{
    /// <summary>
    /// Composes the real simulation and between-wave transactions. Candidate save data is detached;
    /// the wallet, building provider, Workers and phase are published only after durable replacement.
    /// </summary>
    public sealed class ResortProgressionRuntime : IDisposable
    {
        private readonly ProgressionConfig config;
        private readonly IProgressionPersistence persistence;
        private readonly ResourceManager resources;
        private readonly WaveRewardCalculator calculator;
        private readonly WaveRewardService rewards = new WaveRewardService();
        private ProgressionSaveData checkpoint;
        private ProgressionSaveData pendingCompletion;
        private WaveDefinition activeDefinition;
        private bool executing;

        public ResortProgressionRuntime(ProgressionConfig config, IProgressionPersistence persistence)
        {
            this.config = (config ?? throw new ArgumentNullException(nameof(config))).Copy();
            this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            checkpoint = persistence.Load() ?? CreateNewGame(this.config);
            ValidateSave(checkpoint, this.config);
            // Persist the profile ID before a wave can produce a stable reward ID.
            persistence.Save(checkpoint.Copy());
            State = new GameState(checkpoint.Phase);
            Clock = new SimulationClock();
            Buildings = new BuildingManager(this.config, checkpoint);
            resources = new ResourceManager(new ResourceSnapshot(checkpoint.Coins, checkpoint.Energy, checkpoint.ResortValue));
            calculator = new WaveRewardCalculator(this.config);
            var assignments = ReadWorkers(checkpoint);
            Workers = new WorkerManager(Buildings, State, Array.ConvertAll(checkpoint.Workers, w => w.BuildingId));
            Workers.RestoreAssignments(assignments);
            Waves = new WaveManager(State, Clock, Buildings, Workers, this.config.SimulationConfig);
            rewards.Apply(checkpoint.Result);
            Waves.WaveCompleted += HandleWaveCompleted;
        }

        public GameState State { get; }
        public SimulationClock Clock { get; }
        public WorkerManager Workers { get; }
        public WaveManager Waves { get; }
        public BuildingManager Buildings { get; }
        public ResourceSnapshot Resources => resources.Resources;
        public WaveResult CurrentWaveResult => rewards.CurrentResult;
        public int ClaimedWaves => checkpoint.ClaimedWaves;
        public string LastCommandError { get; private set; }
        public string PersistenceDiagnostic { get; private set; }
        public string NotificationDiagnostic { get; private set; }
        public bool LastFailureWasPersistence { get; private set; }
        public event Action<ResourceSnapshot> ResourcesChanged;
        public event Action<IReadOnlyList<BuildingStateSnapshot>> BuildingsChanged;
        public event Action<WaveResult> WaveResultReady;

        public WaveDefinition GetNextWave()
        {
            if (checkpoint.ClaimedWaves >= config.BaseDemand.Length) return null;
            var waveId = checkpoint.ClaimedWaves + 1;
            var baseline = config.BaseDemand[waveId - 1];
            var debut = false;
            foreach (var building in checkpoint.Buildings)
                debut |= building.Level > 0 && !building.HasDebuted;
            var demands = new List<NeedDemandDefinition>();
            foreach (var building in checkpoint.Buildings)
            {
                if (building.Level == 0) continue;
                var demand = !building.HasDebuted ? (int)Math.Ceiling(baseline * config.DebutDemandFactor)
                    : debut && building.LastBaseDemand > 0 ? building.LastBaseDemand : baseline;
                demands.Add(new NeedDemandDefinition(BuildingNeedMapping.GetNeedType(building.BuildingId), demand));
            }
            var events = new List<ScheduledDemandEvent>();
            if (waveId == 2)
                events.Add(new ScheduledDemandEvent("sudden-rush", ScheduledDemandEventKind.SuddenRush,
                    config.RushStart, config.RushDuration, config.RushUnits));
            return new WaveDefinition(waveId, config.ArrivalDuration, demands, events);
        }

        public bool TryStartWave() => Execute(() =>
        {
            if (State.CurrentState != GamePhase.AWAITING_WAVE || CurrentWaveResult != null)
                return Reject("Claim the previous reward and continue before starting a wave.");
            var definition = GetNextWave();
            if (definition == null) return Reject("All eight Prototype waves are complete.");
            if (!Commit(CaptureCheckpoint())) return false;
            activeDefinition = definition;
            return Waves.StartWave(definition);
        });

        public bool TryClaimWaveReward(string rewardId) => Execute(() =>
        {
            if (State.CurrentState != GamePhase.WAVE_COMPLETE) return Reject("No unclaimed wave result is available.");
            var next = CaptureCheckpoint();
            string error;
            if (!rewards.TryPrepareClaim(next, rewardId, Resources, out error)) return Reject(error);
            if (!Commit(next)) return false;
            Publish(WaveResultReady, CurrentWaveResult);
            return true;
        });

        public bool TryContinueAfterResults() => Execute(() =>
        {
            if (State.CurrentState != GamePhase.UPGRADE || CurrentWaveResult == null || !CurrentWaveResult.Claimed)
                return Reject("Claim the wave reward first.");
            var next = CaptureCheckpoint();
            next.Result = null;
            next.Phase = GamePhase.AWAITING_WAVE;
            return Commit(next);
        });

        public bool TrySaveProgress() => Execute(() =>
        {
            if (!IsCheckpointPhase(State.CurrentState)) return Reject("Active waves restart from their pre-wave checkpoint.");
            return Commit(CaptureCheckpoint());
        });

        public bool TryPurchaseBuilding(BuildingId id) => Execute(() =>
        {
            if (!ValidId(id) || State.CurrentState != GamePhase.AWAITING_WAVE)
                return Reject("Buildings can only be purchased while awaiting a wave.");
            var next = CaptureCheckpoint();
            var building = next.Buildings[(int)id];
            if (building.Level > 0) return Reject("This building is already owned.");
            if (next.ClaimedWaves < config.UnlockWaves[(int)id]) return Reject("This building is still locked.");
            var cost = config.PurchaseCosts[(int)id];
            if (!resources.CanAffordCoins(cost)) return Reject("Not enough Coins.");
            next.Coins -= cost;
            building.Level = 1;
            var workers = new List<WorkerSaveData>(next.Workers);
            var nextId = 1;
            foreach (var worker in workers) nextId = Math.Max(nextId, worker.WorkerId + 1);
            workers.Add(new WorkerSaveData { WorkerId = nextId, BuildingId = id });
            next.Workers = workers.ToArray();
            return Commit(next);
        });

        public bool TryUpgradeBuilding(BuildingId id) => Execute(() =>
        {
            if (!ValidId(id) || (State.CurrentState != GamePhase.AWAITING_WAVE && State.CurrentState != GamePhase.UPGRADE))
                return Reject("Upgrades are only available between waves after claiming rewards.");
            var next = CaptureCheckpoint();
            var building = next.Buildings[(int)id];
            if (building.Level == 0) return Reject("Purchase this building first.");
            if (building.Level >= config.Capacities.Length) return Reject("This building is already at level 3.");
            var cost = config.UpgradeCosts[building.Level - 1];
            if (!resources.CanAffordCoins(cost)) return Reject("Not enough Coins.");
            next.Coins -= cost;
            building.Level++;
            return Commit(next);
        });

        public bool TryReassignWorker(BuildingId source, BuildingId destination) => Execute(() =>
        {
            if (State.CurrentState == GamePhase.WAVE_ACTIVE)
                return Waves.TryReassignWorker(source, destination) || Reject("Worker reassignment is not allowed.");
            if (State.CurrentState != GamePhase.AWAITING_WAVE || source == destination)
                return Reject("Worker reassignment is not allowed.");
            var next = CaptureCheckpoint();
            WorkerSaveData moved = null;
            foreach (var worker in next.Workers)
            {
                if (worker.BuildingId == destination) return Reject("The destination already has a Worker.");
                if (worker.BuildingId == source) moved = worker;
            }
            if (moved == null || !ValidId(destination) || next.Buildings[(int)destination].Level == 0)
                return Reject("Choose a staffed source and an owned vacant destination.");
            moved.BuildingId = destination;
            return Commit(next);
        });

        private void HandleWaveCompleted(WaveSnapshot snapshot)
        {
            executing = true;
            try
            {
                var next = CaptureCheckpoint();
                next.Phase = GamePhase.WAVE_COMPLETE;
                next.Result = calculator.CalculateData(RewardId(next.ProfileId, snapshot.WaveId), snapshot.WaveId, snapshot.CompletionRecords);
                foreach (var demand in activeDefinition.Demand)
                {
                    var building = next.Buildings[(int)demand.BuildingId];
                    building.HasDebuted = true;
                    building.LastBaseDemand = demand.DemandTotal;
                }
                if (!Commit(next))
                {
                    // Keep a claimable result in memory. A later Claim/Save retries the entire checkpoint.
                    pendingCompletion = next;
                    rewards.Apply(next.Result);
                }
                Publish(WaveResultReady, CurrentWaveResult);
            }
            finally { executing = false; }
        }

        private ProgressionSaveData CaptureCheckpoint()
        {
            var next = (pendingCompletion ?? checkpoint).Copy();
            next.Coins = Resources.Coins;
            next.Energy = Resources.Energy;
            next.ResortValue = Resources.ResortValue;
            next.Workers = new WorkerSaveData[Workers.Assignments.Count];
            for (var i = 0; i < next.Workers.Length; i++)
                next.Workers[i] = new WorkerSaveData { WorkerId = Workers.Assignments[i].WorkerId, BuildingId = Workers.Assignments[i].BuildingId };
            return next;
        }

        private bool Commit(ProgressionSaveData next)
        {
            ValidateSave(next, config);
            // Validate the exact post-commit Worker state before touching disk or the live provider.
            var nextBuildings = new BuildingManager(config, next);
            var nextWorkers = new WorkerManager(nextBuildings, new GameState(next.Phase), Array.ConvertAll(next.Workers, w => w.BuildingId));
            var assignments = ReadWorkers(next);
            nextWorkers.RestoreAssignments(assignments);
            try { persistence.Save(next.Copy()); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                PersistenceDiagnostic = "Progress could not be saved: " + exception.Message;
                LastFailureWasPersistence = true;
                return Reject(PersistenceDiagnostic);
            }
            var previousResources = Resources;
            var previousWorkers = Workers.Assignments;
            checkpoint = next;
            pendingCompletion = null;
            PersistenceDiagnostic = null;
            resources.Apply(new ResourceSnapshot(next.Coins, next.Energy, next.ResortValue));
            Buildings.Apply(next);
            rewards.Apply(next.Result);
            Workers.RestoreAssignments(assignments);
            if (State.CurrentState != next.Phase && !State.TryTransition(next.Phase))
                throw new InvalidOperationException("Committed an illegal lifecycle transition.");
            if (previousResources.Coins != Resources.Coins || previousResources.Energy != Resources.Energy
                || previousResources.ResortValue != Resources.ResortValue)
                Publish(ResourcesChanged, Resources);
            Publish(BuildingsChanged, Buildings.States);
            for (var i = 0; i < previousWorkers.Count; i++)
                foreach (var worker in Workers.Assignments)
                    if (worker.WorkerId == previousWorkers[i].WorkerId && worker.BuildingId != previousWorkers[i].BuildingId)
                        Publish(WorkerAssignmentChanged, new WorkerAssignmentChange(worker.WorkerId, previousWorkers[i].BuildingId, worker.BuildingId));
            return true;
        }

        public event Action<WorkerAssignmentChange> WorkerAssignmentChanged;

        private bool Execute(Func<bool> command)
        {
            if (executing) return Reject("Another gameplay command is being processed.");
            executing = true;
            LastCommandError = null;
            LastFailureWasPersistence = false;
            try
            {
                var succeeded = command();
                if (succeeded) LastCommandError = null;
                return succeeded;
            }
            finally { executing = false; }
        }

        private bool Reject(string message) { LastCommandError = message; return false; }
        private void Publish<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
                try { handler(value); }
                catch (Exception exception) { NotificationDiagnostic = exception.Message; }
        }

        private static WorkerAssignmentSnapshot[] ReadWorkers(ProgressionSaveData state) =>
            Array.ConvertAll(state.Workers, w => new WorkerAssignmentSnapshot(w.WorkerId, w.BuildingId));
        internal static string RewardId(string profileId, int waveId) => profileId + ":wave:" + waveId;
        private static bool ValidId(BuildingId id) => (int)id >= 0 && (int)id < 5;
        private static bool IsCheckpointPhase(GamePhase phase) => phase == GamePhase.AWAITING_WAVE || phase == GamePhase.WAVE_COMPLETE || phase == GamePhase.UPGRADE;

        private static ProgressionSaveData CreateNewGame(ProgressionConfig config)
        {
            var data = new ProgressionSaveData
            {
                ProfileId = Guid.NewGuid().ToString("N"), Coins = config.InitialCoins, Energy = config.InitialEnergy,
                ResortValue = config.InitialResortValue, Phase = GamePhase.AWAITING_WAVE,
                Buildings = new BuildingSaveData[5], Workers = new WorkerSaveData[config.InitialWorkers.Length]
            };
            for (var i = 0; i < 5; i++)
                data.Buildings[i] = new BuildingSaveData { BuildingId = (BuildingId)i, Level = i < 3 ? 1 : 0, HasDebuted = i < 3 };
            for (var i = 0; i < data.Workers.Length; i++)
                data.Workers[i] = new WorkerSaveData { WorkerId = i + 1, BuildingId = config.InitialWorkers[i] };
            return data;
        }

        public static void ValidateSave(ProgressionSaveData data, ProgressionConfig config)
        {
            if (data == null || data.Version != 1) throw new InvalidDataException("Unsupported or missing save version.");
            Guid profile;
            Check(Guid.TryParseExact(data.ProfileId, "N", out profile), "Invalid profile ID.");
            Check(data.Coins >= 0 && data.Energy >= 0 && data.ResortValue >= 0, "Negative saved resources.");
            Check(data.ClaimedWaves >= 0 && data.ClaimedWaves <= config.BaseDemand.Length && IsCheckpointPhase(data.Phase), "Invalid saved progress.");
            Check(data.Buildings != null && data.Buildings.Length == 5 && data.Workers != null, "Missing buildings or Workers.");
            Check(data.ClaimedRewardIds != null && data.ClaimedRewardIds.Count == data.ClaimedWaves, "Invalid reward ledger.");
            for (var i = 0; i < data.ClaimedWaves; i++)
                Check(data.ClaimedRewardIds[i] == RewardId(data.ProfileId, i + 1), "Reward ledger does not match progression.");
            var completed = data.ClaimedWaves + (data.Result != null && !data.Result.Claimed ? 1 : 0);
            var owned = 0;
            for (var i = 0; i < 5; i++)
            {
                var b = data.Buildings[i];
                Check(b != null && b.BuildingId == (BuildingId)i && b.Level >= 0 && b.Level <= 3 && b.LastBaseDemand >= 0, "Invalid saved building.");
                Check(i >= 3 || b.Level > 0, "A starting building is missing.");
                Check(b.Level > 0 || (!b.HasDebuted && b.LastBaseDemand == 0), "Unowned building has wave history.");
                Check(i < 3 || b.Level == 0 || completed >= config.UnlockWaves[i], "A building was purchased before its unlock.");
                if (b.Level > 0) owned++;
            }
            Check(data.Workers.Length == owned - 1, "Worker count must leave one owned building empty.");
            var ids = new HashSet<int>();
            var occupied = new HashSet<BuildingId>();
            foreach (var worker in data.Workers)
                Check(worker != null && worker.WorkerId > 0 && worker.WorkerId < int.MaxValue && ids.Add(worker.WorkerId)
                    && ValidId(worker.BuildingId) && data.Buildings[(int)worker.BuildingId].Level > 0 && occupied.Add(worker.BuildingId), "Invalid saved Worker.");
            if (data.Phase == GamePhase.AWAITING_WAVE) Check(data.Result == null, "Awaiting wave cannot contain an unconsumed result.");
            else
            {
                var r = data.Result;
                Check(r != null && r.Claimed == (data.Phase == GamePhase.UPGRADE), "Saved result and phase disagree.");
                Check(r.WaveId == (r.Claimed ? data.ClaimedWaves : data.ClaimedWaves + 1) && r.WaveId > 0
                    && r.WaveId <= config.BaseDemand.Length && r.RewardId == RewardId(data.ProfileId, r.WaveId), "Invalid result ID.");
                Check(r.ServedUnits > 0 && r.Coins >= 0 && r.Energy >= 0 && r.ResortValue >= 0
                    && ProgressionConfig.Finite(r.Satisfaction) && r.Satisfaction >= 0 && r.Satisfaction <= 100
                    && r.Stars >= 1 && r.Stars <= 5 && (!r.Perfect || r.Satisfaction == 100), "Invalid saved result.");
                Check(r.Waiting != null && r.Waiting.Length > 0 && r.Waiting.Length <= 5, "Missing waiting statistics.");
                var buildings = new HashSet<BuildingId>();
                long served = 0;
                foreach (var wait in r.Waiting)
                {
                    Check(wait != null && ValidId(wait.BuildingId) && buildings.Add(wait.BuildingId)
                        && data.Buildings[(int)wait.BuildingId].Level > 0 && wait.ServedUnits > 0
                        && ProgressionConfig.Finite(wait.TotalWait) && ProgressionConfig.Finite(wait.MaxWait)
                        && wait.MaxWait >= 0 && wait.TotalWait >= wait.MaxWait, "Invalid waiting statistics.");
                    served += wait.ServedUnits;
                }
                Check(served == r.ServedUnits, "Result totals disagree.");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }

        public void Dispose() { Waves.WaveCompleted -= HandleWaveCompleted; }
    }
}
