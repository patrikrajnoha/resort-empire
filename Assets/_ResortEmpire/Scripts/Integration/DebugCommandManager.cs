#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;

namespace ResortEmpire.Gameplay.Integration
{
    public interface ITestingPresetAdapter { bool TryLoad(string name, out string message); }

    public sealed class DebugCommandManager
    {
        private readonly ResortGameplayBootstrap game;
        private readonly ITestingPresetAdapter presets;
        private readonly Dictionary<string, (string Syntax, string Description, Func<string[], string> Run)> commands =
            new Dictionary<string, (string, string, Func<string[], string>)>(StringComparer.OrdinalIgnoreCase);

        public DebugCommandManager(ResortGameplayBootstrap game, ITestingPresetAdapter presets = null)
        {
            this.game = game;
            this.presets = presets;
            Register("add_coins", "<amount>", "Add Coins.", a => Add(a, true));
            Register("add_energy", "<amount>", "Add Energy.", a => Add(a, false));
            Register("set_buildings_level", "<1-3>", "Set all owned building levels.", a => SetLevel(a, false));
            Register("set_building_level", "<building_id> <1-3>", "Set one owned building level.", a => SetLevel(a, true));
            Register("unlock_all", "", "Construct currently eligible buildings.", a => InSession(() =>
                Report(game.TryDebugUnlockAvailableBuildings(), "Eligible buildings constructed.")), 0);
            Register("wave_status", "", "Show wave and checkpoint state.", _ => InSession(WaveStatus), 0);
            Register("next_wave", "", "Start the next wave after claiming any prior reward.", _ => InSession(NextWave), 0);
            Register("set_wave", "<number>", "Unavailable until validated wave presets exist.", _ =>
                "set_wave is unavailable: no validated wave presets are configured.", 1);
            Register("complete_wave", "", "Simulate the current wave to its result.", _ => InSession(() =>
                Report(game.TryDebugCompleteWave(), "Wave complete. Claim its reward normally.")), 0);
            Register("set_workers_max", "", "Check the current staffing limit.", _ => InSession(() =>
                $"All {game.CurrentWorkerAssignments.Count} available workers are assigned. One owned building remains vacant."), 0);
            Register("reset_workers", "", "Restore default worker assignments.", _ => InSession(() =>
                Report(game.TryDebugResetWorkers(), "Default worker assignments restored.")), 0);
            Register("game_speed", "<1-3>", "Set the wave simulation speed.", Speed);
            Register("game_status", "", "Show the resort and active save slot.", _ => InSession(GameStatus), 0);
            Register("load_preset", "<name>", "Load a configured testing preset.", Preset);
            Register("help", "[page]", "List command syntax and descriptions.", Help);
        }

        private void Register(string name, string parameters, string description, Func<string[], string> run, int? argumentCount = null)
        {
            var syntax = string.IsNullOrEmpty(parameters) ? name : name + " " + parameters;
            commands.Add(name, (syntax, description, args =>
                argumentCount.HasValue && args.Length != argumentCount.Value + 1 ? "Usage: " + syntax : run(args)));
        }
        public string Execute(string line)
        {
            try
            {
                var args = (line ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (args.Length == 0 || !commands.TryGetValue(args[0], out var command)) return "Unknown command.";
                return command.Run(args);
            }
            catch (Exception e) { return "Command failed: " + e.Message; }
        }
        private string Add(string[] args, bool coins)
        {
            if (args.Length != 2 || !int.TryParse(args[1], out var amount) || amount <= 0) return "Enter a positive integer amount.";
            if (game == null || !game.IsConfigured || game.ProgressionConfiguration == null) return "No active gameplay session.";
            var result = game.TryDebugAddResources(coins ? amount : 0, coins ? 0 : amount);
            return result.Succeeded ? "Successfully added " + amount + (coins ? " Coins." : " Energy.") : result.Message;
        }
        private string InSession(Func<string> run)
        {
            return game == null || !game.IsConfigured || game.TotalWaves == 0 ? "No active gameplay session." : run();
        }

        private static string Report(ResortGameplayCommandResult result, string success) =>
            result.Succeeded ? success : result.Message;

        private string SetLevel(string[] args, bool single)
        {
            if (args.Length != (single ? 3 : 2) || !int.TryParse(args[args.Length - 1], out var level)
                || level < 1 || level > 3)
                return single ? "Usage: set_building_level <building_id> <1-3>" : "Usage: set_buildings_level <1-3>";
            BuildingId? target = null;
            if (single)
            {
                var name = Enum.GetNames(typeof(BuildingId)).FirstOrDefault(id =>
                    string.Equals(id, args[1], StringComparison.OrdinalIgnoreCase));
                if (name == null) return "Unknown building. Use Reception, Restaurant, BeachBar, Pool or Bungalows.";
                target = (BuildingId)Enum.Parse(typeof(BuildingId), name);
            }
            return InSession(() => Report(game.TryDebugSetBuildingLevel(target, level),
                $"{(target.HasValue ? target.ToString() : "Owned buildings")} set to level {level}."));
        }

        private string WaveStatus()
        {
            var wave = game.CurrentWaveResult?.WaveId ?? (game.CurrentPhase == GamePhase.AWAITING_WAVE
                ? game.NextWaveForecast.WaveId : game.CurrentWaveSnapshot?.WaveId ?? 0);
            var label = wave > 0 ? wave.ToString() : "None remaining";
            return $"Wave: {label} ({game.CurrentPhase})\nClaimed: {game.CompletedWaves}/{game.TotalWaves}\nCheckpoint: {game.DebugCheckpointStatus}";
        }

        private string NextWave()
        {
            if (game.CurrentPhase == GamePhase.UPGRADE)
            {
                var advance = game.TryContinueAfterResults();
                if (!advance.Succeeded) return advance.Message;
            }
            return Report(game.TryStartWave(), "Next wave started.");
        }

        private string Speed(string[] args)
        {
            if (args.Length != 2 || !int.TryParse(args[1], out var speed) || speed < 1 || speed > 3)
                return "Usage: game_speed <1-3>";
            return InSession(() => Report(game.TryDebugSetSimulationSpeed(speed), $"Wave simulation: {speed}x."));
        }

        private string GameStatus()
        {
            var owned = string.Join(", ", game.Buildings.Where(b => b.IsOwned).Select(b => $"{b.BuildingId} Lv{b.Level}"));
            var workers = string.Join(", ", game.CurrentWorkerAssignments.Select(w => $"#{w.WorkerId}: {w.BuildingId}"));
            return $"Coins: {game.Resources.Coins} | Energy: {game.Resources.Energy} | Slot: {game.DebugActiveSlot?.ToString() ?? "Unavailable"}"
                + $"\n{WaveStatus()}\nBuildings: {owned}\nWorkers: {workers}";
        }

        private string Help(string[] args)
        {
            const int perPage = 3;
            var pages = (commands.Count + perPage - 1) / perPage;
            var page = 1;
            if (args.Length > 2 || (args.Length == 2 && !int.TryParse(args[1], out page)) || page < 1 || page > pages)
                return $"Usage: help <1-{pages}>";
            var entries = commands.Values.Skip((page - 1) * perPage).Take(perPage);
            return $"Help {page}/{pages} (type help <page>)\n"
                + string.Join("\n", entries.Select(entry => entry.Syntax + " — " + entry.Description));
        }
        private string Preset(string[] args)
        {
            if (args.Length != 2) return "Usage: load_preset <name>";
            if (presets == null) return "Testing preset system unavailable.";
            presets.TryLoad(args[1], out var message); return message;
        }
    }
}
#endif
