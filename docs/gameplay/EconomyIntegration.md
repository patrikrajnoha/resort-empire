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
| `NextWaveForecast` / `CompletedWaves` / `TotalWaves` | S1 forecast and wave counter; reading the forecast never consumes a debut |

`WaveNeedStatusTracker.Capture(facade)` turns the live wave into an S2 view model (served/total,
waiting queue, occupied seats, staffing, `OK`/`BOTTLENECK`/`RECOVERING`/`SATISFIED` and one-shot
feedback per owned need) without exposing simulation types. Its rules and settings are described in
`docs/ui/ResortScreens.md`. UI scripts compile into Assembly-CSharp, which does not reference the Waves or Staff
assemblies, so presentation code must use these Integration types instead of `WaveSnapshot`.

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

Before starting a wave the current Worker placement is saved. Waves that use no boosts
resume from that pre-wave checkpoint after relaunch. A Speed Up purchase saves the
deduction and the active wave together, including queue order, work already done in
occupied seats, scheduled demand and each building's boost allowance. Backgrounding,
quitting normally or explicitly saving updates that checkpoint. Relaunch resumes it
at its saved simulation time. Backgrounding a live process freezes the clock.

Unclaimed results reopen on S4 and claimed results reopen in `UPGRADE`. A completion
write failure retains the result in memory and allows Claim to retry the complete
transaction. The previous checkpoint remains available until a write succeeds.

The S2 service cards and Sudden Rush use Energy Mode A. `DemoEconomy.asset` exposes
`EnergyBoostCost`, `EnergyBoostTimeReduction` and `EnergyBoostDuration`. The defaults
are 3 Energy, a 50% reduction and 15 seconds. The service rate is multiplied by
`1 / (1 - reduction)` on top of Worker utilization. A building has one purchase
per wave and keeps its existing number of seats.

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
presenter above. The scene's buttons now drive these commands; the bootstrap component's
**Gameplay/Start Next Wave**, **Gameplay/Claim Wave Reward**, and **Gameplay/Continue After Results**
Inspector context menus remain for debugging. They are Editor-only and call the same facade commands.
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

Resolved on 2026-11-01 by the gameplay/UI integration task:

- **Accelerated-step stall.** A service seat could round its progress to 1 inside a segment cut
  short by an arrival boundary and stay occupied, so the loop stopped on a zero-length boundary
  (`Wave simulation could not make deterministic progress`). It also happened with 0.3333 s
  steps, Unity's default maximum frame delta, so a single frame hitch could end a wave in Play
  Mode. `ServiceSlotRuntime.Advance` now completes every seat that reaches full progress.
  Covered by `AcceleratedStepsCompleteWaveWhenArrivalAndServiceBoundariesCoincide`.
- **Second launch failed to load.** `JsonUtility` cannot store a null class field, so a saved
  `Result = null` came back as an empty result and validation threw "Awaiting wave cannot
  contain an unconsumed result." The JSON adapter now treats `WaveId == 0` as no result.
  Covered by `JsonCheckpointWithoutResultReloadsOnNextLaunch`.
- **Invalid saves crashed `Awake`.** `InvalidDataException` is not an `IOException`, so it
  escaped the bootstrap's filters. It now leaves the runtime unconfigured with a logged
  diagnostic, as documented above.
- The facade exposes the next-wave forecast, and S2 bottleneck/recovering states come from
  `WaveNeedStatusTracker`. Screen navigation and UI wiring are done; see `docs/ui/ResortScreens.md`.

Still open:

- After its debut wave, Ocean View currently receives the same base demand as other needs
  (27 units each in Wave 8). The gameplay reference describes lower Ocean View demand with
  a higher satisfaction weight. A separate demand factor/table remains to be implemented.
- S4 and S5 rank buildings by total guest waiting time, not by how long each bottleneck lasted.

Pre-commit verification on 2026-10-29 used isolated snapshots of the Git index, so later
unstaged files could not accidentally satisfy earlier commits' dependencies. All 135 domain
tests passed again. The integration sources compiled against the temporary Unity API stubs;
scene IDs, configuration references and new asset metadata passed static checks. A repeat of
the adapter smoke test was blocked by Windows Application Control when loading `UnityStubs.dll`.
That blocked run is not a successful test and does not replace the outstanding Unity checks.

Validation on 2026-11-01 in the Unity 6000.4.0f1 Editor: all 167 `ResortEmpire.Gameplay.EditMode.Tests`
cases passed, including the Unity integration tests that had not run before and the new
regression, need-status and navigation tests. Play Mode in `Resort.unity` (1920×1080 Game view)
completed the full loop through the scene's own buttons: Start Wave → arrivals → Beach Bar
bottleneck on S2 → Worker moved mid-wave on S3 → completion after the queues drained → 4★ on S4 →
claim (a second click and a replayed claim were both rejected) → S5 upgrade → S1 for Wave 2.
Waves 2–3 were then advanced through the facade to check the Sudden Rush target, the Pool
unlock on S9 with its bundled Worker, and four needs on S2. All nine F1 previews still open and
restore the canonical UI. No Android device validation was performed.
