# Economy, buildings and wave rewards

This implements the approved Prototype task (priority 1 and priority 2). The scene uses
`GameSystems/ResortGameplayBootstrap` and its assigned `ResortGameplayDependencySource`.
The dependency source references `Assets/_ResortEmpire/Data/Progression/DemoEconomy.asset`.
These are **demo balancing values**, not approved release tuning.

## UI integration for Jakub

Use `IResortGameplayUiFacade` from the scene bootstrap. Do not instantiate another gameplay
runtime or change save DTOs from UI. All commands return `ResortGameplayCommandResult`:
`Succeeded`, `Rejected` (game rule), or `Unavailable` (configuration/persistence failure).

| Read/event | UI use |
| --- | --- |
| `Resources` / `ResourcesChanged` | Coins, Energy and cumulative Resort Value |
| `Buildings` / `BuildingsChanged` | Owned/eligible, level, capacity, base duration, purchase and next upgrade cost |
| `CurrentWorkerAssignments` / `WorkerAssignmentChanged` | Worker movement; also refresh on `BuildingsChanged` after a purchase |
| `CurrentWaveResult` / `WaveResultReady` | S4 satisfaction, stars, Perfect, reward amounts, per-building queue wait and claim status |
| `CurrentPhase` / `StateChanged` | Window navigation |
| `PersistenceDiagnostic` | Failed checkpoint write; allow retry through `TrySaveProgress()` or Claim |

Subscribe in `OnEnable`, unsubscribe in `OnDisable`, and immediately read the current
snapshot after subscribing. A reopened result must use its existing `RewardId`, not generate
a new one. Use `WaveResultReady` to populate S4: the raw `WAVE_COMPLETE` phase transition
occurs before reward calculation. The runtime can already contain a restored result at startup.

```csharp
var result = gameplay.CurrentWaveResult;
var response = gameplay.TryClaimWaveReward(result.RewardId);
if (!response.Succeeded)
{
    // Display response.Message and allow retry if the canonical result is still unclaimed.
}
```

`WaveResultsPresenter` is an optional ready-made binding for the existing `ResultsView`.
Place it on the S4 window; assign the bootstrap, view and the three reward item references
in the Inspector. Its `CommandFailed` string event can drive an error label. It does not
open/close windows or change their layout; S4/S5 navigation remains UI-owned. In particular,
`ResultsView.SetResults` resets its local click guard, so the presenter reapplies the canonical
claimed state after every render. Duplicate claims are rejected by gameplay regardless of UI.

Commands:

- `TryStartWave()` starts the next of eight scripted waves. Pending rewards block it.
- `TryClaimWaveReward(rewardId)` credits all rewards once and enters `UPGRADE`.
- `TryContinueAfterResults()` clears the displayed result and enters `AWAITING_WAVE`.
- `TryUpgradeBuilding(id)` is valid in `AWAITING_WAVE` or `UPGRADE`, up to Lv3.
- `TryPurchaseBuilding(id)` is valid in `AWAITING_WAVE`, after the Wave 3/6 gates.
- `TryReassignWorker(source, destination)` remains free before/during waves. The destination must be vacant.

Disable upgrade controls when `UpgradeCost` is null or the phase is wrong. Eligible does not
mean owned or affordable. A purchase costs Coins and adds one assigned Worker in the same
saved operation; refresh Worker displays on `BuildingsChanged`. Capacity increases with level;
the base service duration and single Worker slot stay unchanged. Numerical satisfaction is
only available in the final result. `ServedUnits` counts need units, not distinct visual guests.

## Architecture and persistence

The Unity-free `ResortEmpire.Gameplay.Progression` assembly composes the existing Core,
Staff and Waves assemblies. It contains the resource/building managers, rating calculator,
reward service and `ResortProgressionRuntime`. The bootstrap exposes snapshots and commands
from that runtime. Legacy simulation-only dependency sources remain supported for existing
tests; economy commands report unavailable on those sources.

Transactions validate a detached candidate checkpoint, save it, then update the wallet,
building provider, Worker assignments and phase before publishing UI events. Failed writes
leave the live economy unchanged. Reentrant economy commands during notification are rejected.
UI event exceptions are isolated from economic commit; bootstrap logs those exceptions.
Config arrays are copied at runtime creation, so Inspector edits cannot alter an active wave.

Save: `Application.persistentDataPath/resort-progress-v1.json`, schema version 1. A uniquely
named temporary file is flushed before atomic replacement. Resources, buildings, stable Worker
IDs, progress, current result and the claim ledger are in that same file. Reward IDs combine
the persisted profile ID and wave number. Malformed/unknown versions disable configuration
with a diagnostic and preserve the original file; there is no silent reset or guessed migration.

The agreed scope is **between-wave checkpoints**. Before starting a wave the current Worker
placement is saved. Relaunch during a wave restarts that wave from its pre-wave checkpoint,
without offline progress. Backgrounding a live process suspends simulation; returning resumes
it. Unclaimed results reopen on S4; claimed results reopen in `UPGRADE`. A completion write
failure retains the result in memory and allows Claim to retry the complete transaction. If the
process is killed before a failed write can be retried, the prior checkpoint is retained.

Energy has balance and reward support only: boosts, regeneration, Firebase and active-queue
serialization are outside this task. Resort Value cannot be spent or decreased.

## Demo waves and ratings

Each of the three initial services receives 12, 15, 18, 18, 21, 24, 24, 27 base need units
over 90 seconds. Wave 2 adds six rush units at 45–55 seconds to the currently unstaffed building.
Pool/Bungalows create no demand before purchase. Their first owned wave uses
`ceil(baseDemand * 0.5)`, while existing services repeat their last base demand (rush excluded).
This also works for delayed or simultaneous purchases. Subsequent waves use the current base
demand. Forecast reads do not consume the debut.

Waiting ends at seating; service time is excluded (Build IV behavior; the older F44 conflict is
not treated as resolved). Scores are 100 through 10 seconds, decay linearly to 25 at 30 seconds,
then remain 25. Wave satisfaction is weighted (Ocean View weight 2, other needs 1) and floored
at 40. Star thresholds are 40/55/70/80/90. Perfect requires every need unit to be within grace.
Coin awards are floored after multiplying all served need units, including rush, by five and
the rating multiplier. Stored result amounts do not get recalculated when reopening the UI.

## Validation

Run the `ResortEmpire.Gameplay.EditMode.Tests` assembly in Unity Test Runner, including
`EconomyProgressionTests` and `EconomyUnityIntegrationTests`. The latter checks the real
configuration asset, scene dependency references, JSON persistence and facade integration.

For Play Mode: open Resort, verify bootstrap `IsConfigured`, start/finish a wave, inspect
`CurrentWaveResult`, claim, verify resources, continue and start wave 2. Connect S4 using the
presenter above. Before UI buttons are wired, use the bootstrap component's **Gameplay/Start
Next Wave**, **Gameplay/Claim Wave Reward**, and **Gameplay/Continue After Results** Inspector
context menus during Play Mode. They are Editor-only and call the same facade commands.
Check error retry and reopen behavior. Use a test save/config to reach gates
3/6 and test purchases/upgrades; never edit a real player's save to balance the game.

The provided defaults still need a gameplay balance pass, including achievable 5-star play
without Energy. Android file replacement and full Play Mode presentation require validation
with the Unity Editor/device; a standalone .NET test pass does not establish those results.

Implementation validation on 2026-10-28: the four Unity-free gameplay assemblies compiled
with Roslyn and their 135 NUnit cases passed under .NET Framework (including existing wave,
Worker and lifecycle regressions). Integration/result-presenter source was additionally compiled
against temporary Unity API stubs. A Windows file-lock test exercised actual atomic replacement
failure and retry; adapter tests used a substitute JSON serializer, not Unity's `JsonUtility`.
Scene IDs, script/asset GUID references and serialized demo values were checked statically.
The Unity Editor was not available in this environment: Unity import, the seven new Unity
integration tests, Play Mode and Android validation remain unrun.

## Review findings and remaining work

The implementation covers the agreed economy task, not the entire Prototype definition of
done. Keep the following review findings visible before merging or handing off a release:

- The existing wave engine has a reproducible accelerated-step failure. With a fresh clock,
  Wave 2, capacity 7 in all three starting buildings, the demo service/arrival/rush settings
  and the initial Worker placement, `Advance(10000)` throws
  `Wave simulation could not make deterministic progress` at simulation time 80. Advancing
  the same scenario in `1 / 60` second steps completes it. Add a regression test and repair
  the service boundary handling; the 135-test suite does not cover this case.
- After its debut wave, Ocean View currently receives the same base demand as other needs
  (27 units each in Wave 8). The gameplay reference describes lower Ocean View demand with
  a higher satisfaction weight. A separate demand factor/table remains to be implemented.
- The facade does not yet expose the next-wave forecast or ready-made bottleneck/recovering
  states. The optional result presenter ranks buildings by total guest waiting time, not by
  bottleneck duration, and provides generic feedback rather than the complete S5 recommendation.
  Screen navigation and UI wiring are still owned by the UI integration task.

Pre-commit verification on 2026-10-29 used isolated snapshots of the Git index, so later
unstaged files could not accidentally satisfy earlier commits' dependencies. All 135 domain
tests passed again. The integration sources compiled against the temporary Unity API stubs;
scene IDs, configuration references and new asset metadata passed static checks. A repeat of
the adapter smoke test was blocked by Windows Application Control when loading `UnityStubs.dll`.
That blocked run is not a successful test and does not replace the outstanding Unity checks.
