# Sudden Rush (Wave 2)

The existing scheduled-demand simulation drives the event. `DemoEconomy.asset` exposes
`tuning.RushStart`, `RushDuration`, and `RushUnits` (defaults: 45 seconds, 10 seconds,
6 units). Timing starts at the active wave, after the incoming phase. Rush is omitted
from the S1 forecast. At the trigger, the simulation picks the first owned, unstaffed
building in BuildingId order using live Worker assignments. The production Worker rules
leave exactly one such building. If a custom provider supplies no candidate, the event
is skipped; it never falls back to a locked or unpurchased plot or blocks completion.

All rush units immediately count toward the service total, then arrive evenly over the
configured duration into the normal queue. Service, waiting time, satisfaction and
rewards use the existing simulation. Worker moves after activation do not retarget it.

## Notification and navigation

`WaveManager.CaptureSuddenRush()` exposes the started event; dismissal is held in its
runtime state. `ISuddenRushUiFacade` provides presentation data through the bootstrap.
S2's `WaveNeedsPresenter` shows the contextual notice above the need cards and connects:

- **Move Worker here:** opens S3 with `ResortUiStateCoordinator.WorkerAllocationTarget`
  set to the affected building, then dismisses the notice. S3 keeps that destination
  fixed and uses the existing free Worker command. Once staffed, it disables the move
  buttons instead of redirecting the next click to a newly vacant building.
- **Use Energy to serve faster** activates the same Speed Up command as the service
  cards. The notice displays the configured Energy cost. A successful boost dismisses
  the notice, and a rejected attempt displays the reason.
- **Ignore:** only dismisses the notice. It cannot remove demand or apply a penalty.

Opening S3, displaying the notice, and dismissing it never pause the wave.
Disabling/reopening S2 or pausing/resuming does not recreate the notice or demand.

## Save behavior

Backgrounding pauses the live wave and preserves the event target, emitted-unit count
and dismissal. Spending Energy creates a saved wave snapshot that includes the rush
state and queues. When Speed Up is used from the notice, its dismissal is saved with
the Energy deduction. Waves that use no boosts resume from their pre-wave checkpoint
after relaunch.

## Validation (2026-11-06, Unity 6000.4.0f1)

- Unity compilation: no errors or warnings.
- `ResortEmpire.Gameplay.EditMode.Tests`: **187 passed, 0 failed**, including ten new
  rush tests for target timing, progressive arrivals, dismissal, pause/background resume,
  locked plots, no-candidate behavior, forecast/S2 totals, reward demand, checkpoint
  restart, S3 context and Energy adapter rejection/forwarding. Adapter tests use a fake
  provider and do not establish real boost behavior.
- Live Resort scene: the notice showed Beach Bar, Drinks changed from 15 to 21, total
  demand was 51, and frames plus simulation time advanced while the notice was visible.
- The actual Move button passed the UI raycast check and opened S3 for Beach Bar.
  Moving Reception's Worker changed the live allocation and displayed the existing
  speed-up/slow-down feedback. Wave completion included all 51 units with zero Energy.
- A separate live run moved Workers before the trigger and targeted Reception. Pause
  before/during the event, Ignore, and reopening S2 preserved the 51-unit total and
  completed without Energy. No Play Mode errors were reported.

Play Mode checks used isolated test saves; the production dependency source was restored
and the Editor returned to Edit Mode. Android/device validation was not performed.
