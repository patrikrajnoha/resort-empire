# SCRUM-93 Unity test improvements

**Date** 2026-11-24 (Europe/Bratislava)

**Base revision** `cb6303dbeae72663398d9070f6f81bd451dac475` on `main`

The Step 1 audit matches the base revision. Work began with the three Step 1 QA artifacts as the only workspace additions. No applicable `AGENTS.md` was present in the checkout or parent directories. The repository's team skill, Prototype references, save documentation and live Jira acceptance criteria informed the changes. The executable changes are confined to the test assemblies. This step strengthened 21 existing methods (23 cases), replaced two debug cases one for one and added two cases. [Step 1 audit](TEST_AUDIT_REPORT.md) · [Step 1 baseline](TEST_BASELINE.md) · [SCRUM-93](https://kamaratisbenefitmi.atlassian.net/browse/SCRUM-93)

## Tests strengthened or replaced

| Area | Tests | Evidence and protection |
| --- | --- | --- |
| Debug previews | Replaced `PreviewManagerDoesNotIssueGameplayCommandsOrMutateGameState` and `UnconfiguredBootstrapIsCompatibleWithVisualPreviewState` with `ResultPreviewRestoresLiveWaveUiWithoutChangingGameplay` and `ResultPreviewOpensAndRestoresUiWithAnUnconfiguredBootstrap`. | Both exercise the production debug controller, coordinator and actual bootstrap. They verify which objects the controller discovered, preview override ownership, S4 visibility and restoration. The active-wave case first proves guest demand has arrived. Both check clock, phase, wallet, Workers and absence of resource or phase events. Synthetic windows give the controller concrete targets and the test restores its static instance. [Tests](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/ResortDebugUiTests.cs) |
| Reward claims | `ReentrantClaimAndThrowingUiListenerCannotDuplicateOrUndoCommit`, `FacadeCompletesAllWavesAndPublishesCanonicalResults` | `ResortProgressionRuntime.Publish` catches subscriber exceptions, so the subscribers now record values and the assertions run after the commands. The re-entrancy case checks the nested refusal, one following wallet event despite a throwing subscriber, the three balances and one persisted claim ID. The façade test checks event count and all balances after each claim and rejected replay. [Publisher](../../Assets/_ResortEmpire/Scripts/Progression/ResortProgressionRuntime.cs) · [Runtime test](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/EconomyProgressionTests.cs) · [Façade test](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/EconomyUnityIntegrationTests.cs) |
| Drain, pause and completion | `ActiveWorkerReassignmentRemainsAvailableThroughQueueDrain`, `PauseFreezesClockArrivalsAndService`, `WaveCompletionEventFiresExactlyOnceAfterQueuesDrain` | The transfer now starts after arrivals close with two occupied seats and four waiting units. Two seconds of service adds 0.125 progress at the source and 0.25 at the destination. All six units finish in their per-need FIFO order. Pause compares a specific occupied seat's progress and final service timestamp. Completion checks no event at arrival closure and one terminal payload after backlog reaches zero. [Tests](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/WaveManagerTests.cs) |
| FIFO and non-vacuous assertions | `EqualArrivalTimesRemainStable`, `EmptyQueueIsReadOnlyAndDequeueFails`, `DemandConservationAndCompletionRecordsRemainConsistent`, `SlotsKeepClearOfOtherQueuesAndNeverLeaveTheRoute` | Equal-time IDs arrive in reverse numeric order, which detects sorting ties by ID. The empty queue check also verifies a positive waiting-ID snapshot remains unmodified after dequeue. Conservation requires three needs, six mid-wave arrivals, three occupied seats, three waiting units and twelve unique terminal records. Visual queue placement requires a positive count before validating separation. [Queue runtime tests](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/BuildingQueueRuntimeTests.cs) · [Conservation test](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/PhaseTwoHardeningTests.cs) · [Layout test](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/GuestQueueFeedbackModelTests.cs) |
| Boost timing and restoration | `BoostAcceleratesOccupiedSeatAndExpiresWithoutResettingProgress`, `DrainBoostCombinesWithWorkerAndOtherBuildingsHaveIndependentLimits`, `JsonReloadRestoresRemainingTimeSeatsRushAndSpentAllowance` | Asserts the exact expiry boundary, base-rate progress after expiry and the original single step spanning expiry and completion. Tests reset of used allowances and a fresh debit in Wave 2. The JSON test reopens a partially emitted Rush with three of six extra units already spawned. It compares emitted demand, Boost expiry, all 51 completion records and final rating and rewards with an uninterrupted control. [Tests](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/EnergyBoostTests.cs) |
| Sudden Rush | `RushArrivesOverItsDurationAndPauseDoesNotDuplicateOrLoseDemand`, `CheckpointRestartCreatesOneFreshRushAndPreservesRewardDemand` | The pause case verifies the first extra unit reached Drinks, then checks the frozen emitted counter, unique completions and exact Drinks arrival times. The restart case proves the original rush emitted two units and was dismissed before the unsaved wave falls back and starts one fresh rush. [Tests](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/SuddenRushTests.cs) |
| Economy boundaries | `PurchaseAtExactBalanceSucceedsAndUnaffordablePurchaseDoesNotChangeOwnership`, `FailedUpgradeDoesNotPublishOrChangeAnything` | Pool purchase at 399/400 Coins checks persisted level, ownership and the Relax forecast on both branches. An injected upgrade write failure preserves live capacity, persisted level and Coins with zero events. Retrying commits the exact debit and publishes one wallet and one building event. [Tests](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/EconomyProgressionTests.cs) |
| Bottlenecks and production tuning | `ProductionBeachBarWarnsOnlyAfterTheConfiguredGrowthDuration` (three cases), `PauseFreezesTheGrowthWindowAndResumeContinuesIt`, `MovingAWorkerToTheBottleneckRecoversItWithoutFlicker`, `EachWaveStartsDetectionFromScratch`, `AllEightProductionWavesWarnOnlyForRealQueuesAndNeverFlicker` | Loads the actual `DemoEconomy.asset`. Polling loops have a 4,000-step budget of 0.5 simulated seconds per step and report phase, arrivals and backlog on failed completion. The eight-wave fixture requires both purchases, three/four/five active need identities, one Worker vacancy, the served totals 36/51/54/63/84/96/108/135 and no next forecast. Existing timing and warning assertions remain part of the run. [Tests](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/BottleneckDetectionTimingTests.cs) · [Asset](../../Assets/_ResortEmpire/Data/Progression/DemoEconomy.asset) |

## Tests added

| Test | Scope and oracle |
| --- | --- |
| `PausedUnboostedWaveRejectsSaveAndReloadsThePreWaveJsonCheckpoint` | EditMode characterization using production JSON. Starts Wave 1 at 1,000 Coins, advances until guests have arrived and moves a Worker. Pauses and attempts a save. Asserts policy rejection, byte-identical JSON and no active snapshot, then reloads a new runtime and checks clock zero, Wave 1, pre-wave Workers and the original wallet. [Test](../../Assets/_ResortEmpire/Tests/Gameplay/EditMode/EconomyUnityIntegrationTests.cs) |
| `ResortWaveOneShowsServiceNeedsAndClaimsRewardsOnceThroughTheUi` | Graphics-enabled PlayMode using real `MainMenu.unity` and `Resort.unity` and a temporary save root. Raycasts and dispatches a pointer click to New Game, Start Wave and Claim Rewards. Asserts live S2 text `SERVED 0/36` and Rooms `0/12`, S4 text `36/36`, exactly one wallet publication across two clicks, all three HUD balances, S5 visibility and one persisted reward ID. Manual stepping drives the production simulation between UI actions. S2 card hit testing has separate diagnostic evidence below. [Test](../../Assets/_ResortEmpire/Tests/Gameplay/PlayMode/MainMenuNavigationTests.cs) |

## Production findings and Jira drafts

### Unboosted active waves fall back on relaunch

**Classification** Confirmed behavior and confirmed gap against SCRUM-20. The limitation is intentional in the current implementation and repository docs. `CanSaveActiveWave` explicitly requires at least one used Boost. The current Save Manager acceptance criteria preserve existing checkpoint behavior. SCRUM-20 remains To Do and expressly calls for players to be able to resume unfinished Prototype waves after closing the game. Treat this as an open delivery gap rather than a regression in a previously supported flow. [SCRUM-20](https://kamaratisbenefitmi.atlassian.net/browse/SCRUM-20) · [SCRUM-98](https://kamaratisbenefitmi.atlassian.net/browse/SCRUM-98) · [Current save policy](../../Assets/_ResortEmpire/Scripts/Progression/ResortProgressionRuntime.cs) · [Documentation](../gameplay/SuddenRush.md#save-behavior)

**Proposed Jira Bug** `[Save/Load] Continue replays an unfinished wave unless a Speed Up was used`

1. Start a new game in the Resort scene and start Wave 1 with zero Energy use.
2. Wait until guests arrive and move a Worker. Pause the wave and request a save.
3. Close and launch the game again, then choose Continue on the same save.
4. Observe that the game offers Start Wave 1 with the pre-wave Worker assignment and no active arrivals or occupied seats.

**Actual evidence** The automated runtime/File IO case proves the attempted write is rejected, the old JSON stays byte-identical, and a new runtime restores the prior checkpoint. The scene-level relaunch steps above are the proposed manual reproduction on Android.

**Expected under SCRUM-20** Continue offers Resume Wave. The saved clock, arrived units, waiting queues, occupied seat progress, Worker assignments and any fired Rush survive an app close even with zero Energy spent. The restored run completes and claims once. The characterization test can be updated with the intended acceptance oracle when this requirement is implemented.

### Wave Needs card centre can be outside the Editor viewport

**Classification** Reproduced in Windows Unity Editor batch PlayMode at its observed 640×480 viewport. Impact at supported device sizes is unverified.

**Proposed Jira Bug** `[S2] Wave Needs card centre can sit outside the 4:3 viewport`

1. Open the production Resort through the Main Menu at a 640×480 Game view and start Wave 1.
2. Open or observe S2, then inspect the screen rect of the Rooms card and raycast its centre. For manual reproduction, also check a supported 4:3 tablet resolution.
3. In the batch reproduction, the card occupied x=629.34–771.43, putting its centre at x≈700.39 beyond the 640-pixel right edge. The graphic was active and raycast enabled. The real EventSystem returned no hit there even after twelve frames and a canvas update.

**Expected** S2 places the card's usable hit area on-screen at supported aspect ratios and a pointer click opens S3 for Reception. The exploratory invocation with `-screen-width 1920 -screen-height 1080` still reported 640×480 in Editor. Device verification or an explicitly sized interactive Game view is required to establish reach. The retained smoke checks rendered service data and the full Start/Claim input path. The two Jira Bug drafts are included for triage.

## Validation

The final validation reuses the isolated project extracted from the audited commit and copies only the changed tests into it. Windows 11 x64, Unity 6000.4.0f1, Unity Test Framework 1.6.0 and graphics-enabled Editor batch mode match Step 1. PlayMode uses real scene raycasts with synthetic pointer-click dispatch. Physical touch, app termination, Android safe areas and frame-rate behavior require device tests.

```powershell
unity test "$project" --mode EditMode --editor-version 6000.4.0f1 --timeout 300 `
  --output "$run/EditMode.xml" --format json -- -logFile "$run/EditMode.log"
unity test "$project" --mode PlayMode --editor-version 6000.4.0f1 --timeout 300 `
  --output "$run/PlayMode.xml" --format json -- -logFile "$run/PlayMode.log"
```

| Run | EditMode passed / discovered | PlayMode passed / discovered | Failed | Skipped | Inconclusive | Blocked |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Step 1 baseline | 318 / 318 | 8 / 8 | 0 | 0 | 0 | 0 |
| Step 2 final | 319 / 319 | 9 / 9 | 0 | 0 | 0 | 0 |

Both final unfiltered commands exited 0. Recorded NUnit runner durations were 2.558 s for EditMode and 20.239 s for PlayMode excluding startup. All 328 leaf identifiers and verdicts reconcile with the updated inventory. It contains 272 `KEEP`, 40 `IMPROVE` and 16 `MERGE_CANDIDATE` cases. [Updated inventory](TEST_INVENTORY.csv)

The first unfiltered attempt reported one failure in each mode. The EditMode fixture sampled the Rush at its exact third-emission boundary and observed two units. Sampling a quarter second inside the intended interval now makes the partial-arrival precondition unambiguous. The PlayMode fixture's global name match found multiple `ClaimRewardsButton` objects. The final fixture scopes the button to the active S4 panel. Three earlier one-case PlayMode probes reproduced the Rooms card hit failure described above. Their XML evidence is separate from the final core smoke.

| Evidence | File | SHA-256 |
| --- | --- | --- |
| Final EditMode | `EditMode.xml` | `60edbcf9b75ac6578faee22bc1b0675783147d1da4caddbe3184c27ca2abc233` |
| Final PlayMode | `PlayMode.xml` | `630798381bfe06377eaa5c460fa99d72991bf7f99e950c51bac0166ed3046db3` |
| S2 viewport probe | `PlayModeProbe.xml` | |

The local run directory is `%TEMP%\resort-qa-cb6303-fc18c6c3\step2`. It also contains the initial suite XML reports and redacted copies of the final Editor logs.

## Remaining coverage and Step 3

- Design an eight-wave production-scene checklist that traces both gates, paid upgrades, staffing, result presentation, exact reward arithmetic and wallet/ledger persistence against independent expectations. Include a prepared no-Energy Perfect trajectory.
- Validate S2 hit targets, Rush Move/Boost/Ignore, S3 Move and swipe dismissal, S5/S9 actions, screen safe areas, camera gestures and NPC gameplay on supported Android hardware. Check the S2 viewport issue at phone and tablet resolutions.
- Validate Pause Menu and Save Manager against real scenes and disk. Cover background/resume, app termination without a boost, partial boosted Rush restoration, slot switching and atomic JSON replacement failure. Keep the unboosted resume acceptance scenario traceable to SCRUM-20.

## Proposed consolidation awaiting approval

No tests are proposed for deletion. The Step 1 overlap table remains the list of 16 consolidation candidates. Highest-value candidates are the equivalent WaveManager step-partition checks, the two pre-wave Worker move checks that separately assert event payload and count, and the two locked S3 affordance cases. Preserve their unique inputs and oracles before combining them. The two debug replacements above keep two separately discovered cases. [Confirmed overlap](TEST_AUDIT_REPORT.md#confirmed-overlap) · [Updated inventory](TEST_INVENTORY.csv)
