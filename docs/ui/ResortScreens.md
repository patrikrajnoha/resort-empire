# Resort result and staff screens

Adds four editable uGUI windows to `Assets/_ResortEmpire/Scenes/Resort.unity`:

- `AssignStaffRestaurantCanvas` → `AssignStaffRestaurant`
- `WaveCompleteCanvas` → `WaveComplete`
- `VipPreviewCanvas` → `VipPreview`
- `VipCompleteCanvas` → `VipComplete`

Each canvas is active; its window prefab instance is inactive until opened. Prefabs live in `Assets/_ResortEmpire/Prefabs/UI/`. Activate one window to preview it. `VipComplete` is a variant of `WaveComplete`; chef cards share `ChefCard.prefab`. `ResortBackdrop.prefab` is an editable decorative composition of the existing ocean, island, buildings, and boat assets. It does not reflect live building upgrades.

The presentation components expose chef selection, request-opening and reward-claim events for gameplay integration. They do not implement wallet transactions. `ResultsView.SetResults` resets the local claim-event guard for a new result session; the canonical gameplay transaction must enforce actual reward idempotency. VIP arrival progress is updated through `VipPreviewView.SetArrival`.

All text uses Rubik TextMeshPro assets with Midline alignment. The restored original UI skins, portraits, icons, badges, stars, and confetti are reused. Sliced skin assets reference existing textures without changing their importer settings or GUIDs. The resort artwork differs from the screenshot reference, so the background is an approximation using the actual project assets.

Verified in Unity 6000.4.0f1 Play Mode: all four rendered windows, busy-card selection protection, guarded/resettable claim events, countdown/progress values, and scene EventSystem setup. Edit Mode checks covered prefab links, the result variant, font/style/alignment settings, button event bindings, and sliced sprite borders. RectTransform stage-fit checks passed at 2340×1080, 1920×1080, 1280×720, and 2560×1080. No Android-device validation was performed. Final isolated rendering produced no new Console warnings or errors.

## Runtime binding (Prototype loop)

Each window is driven by a thin presenter in `Scripts/UI` that reads the
`ResortGameplayBootstrap` facade and sends its commands; none of them holds wallet or simulation
state. `ResortUiStateCoordinator` (on `GameUI`) owns phase-driven navigation: S2 during a wave,
S4 once a result is ready, S5 after claiming. It also opens S3 and S9 on request and closes them
when the phase no longer allows them.

| Screen | Presenter | Behavior |
| --- | --- | --- |
| S1 `HomeIslandView` | `HomeIslandPresenter` | Coins, Energy, Resort Value, next-wave forecast, `START WAVE n`. H/R/B markers open S3; the `+` marker follows the eligible plot and opens S9. The VIP button is hidden (MVP only). |
| Island art | `ResortIslandPresenter` | Level 1–3 art per building; construction site once eligible; hidden while locked. |
| S2 `WaveNeedsPanel` | `WaveNeedsPresenter` | Need + service name, served/total, waiting queue and Worker/seat cause per owned need, stacked 3 → 4 → 5. Tapping a card opens S3 for that service. See [S2 Wave Needs Panel](#s2-wave-needs-panel). |
| S3 `AssignStaffRestaurant` | `WorkerAllocationPresenter` | One card per owned building. MOVE sends that Worker to the single vacant building and names both the helped and the slowed building. Opened from an S2 need, the serving building's card is outlined. |
| S4 `WaveComplete` | `WaveResultsPresenter` | Result, longest-queue warning, `+N` rewards, claim once. |
| S5 `UpgradeSuggestionPanel` | `UpgradeSuggestionPresenter` | Capacity upgrade for the longest queue. LATER or UPGRADE NOW returns to S1. |
| S9 `AreaUnlockPanel` | `AreaUnlockPresenter` | Pool after Wave 3, Bungalows after Wave 6; announced once per session. |

Deliberate deviations from the mockups, for the screen owners to review:

- **S2:** the live satisfaction stars, value, bar and thresholds are deactivated, because Build IV
  forbids numeric satisfaction during a wave. Relax and Ocean View cards were added as copies of the
  Food card, and every card gained an always-visible detail row and a Button. Cards are 176 px tall so
  three needs fit without scrolling, and a `FeedbackToast` sits above the panel. These are scene
  overrides on the prefab instance; the prefab asset is unchanged.
- **S3:** the named-chef cards are reused as card art only. At runtime the names, portraits and star
  ratings are replaced with building names and the anonymous Worker icons, because the Prototype
  has no named staff. The prefab still shows the chef mockup in edit mode. A dedicated anonymous
  Worker card would be cleaner.
- **S3 and S4** hide their decorative `ResortBackdrop` at runtime so the live island shows behind them.
- **S5:** the quality and Resort Value rows are hidden, because Prototype upgrades add capacity only.
- **S9:** a LATER button was added (a copy of S5's). The S5 and S9 dims now block taps on S1 underneath.
- **Art import:** `bungalows-level-01.png` and `bungalows-level-03.png` import as tiny slices
  (58×25 and 89×51 px), so level 2 Bungalows art stands in for every level. The importer settings
  were not changed.

## S2 Wave Needs Panel

S2 opens with the wave and stays docked right (about a third of the width) until the result is
ready. Each card shows the need and its service (`Drinks  Beach Bar`), served/total, and a detail
row that leads with the state in words, so nothing depends on colour alone:

| State | Detail row | Meaning |
| --- | --- | --- |
| OK | `OK · 0 waiting · Worker on duty` / `No Worker` | Normal service |
| BOTTLENECK | `BOTTLENECK · 4 waiting · NO WORKER` or `SEATS FULL 3/3` | Queue over the threshold and still growing |
| RECOVERING | `RECOVERING · 3 waiting · Worker on duty` / `No Worker` | Overloaded queue draining, not yet normal |
| SATISFIED | `SATISFIED · all 12 served` | Arrivals closed and every unit served |

`NO WORKER` means moving a Worker helps now; `SEATS FULL` (Worker present, every seat busy) means only
Energy or a capacity upgrade helps. Occupied seats never count as the waiting queue.

`WaveNeedStatusTracker` derives the states from the waiting queue, timed in simulation seconds. A paused
wave (`WAVE_PAUSED`, or the app in the background) does not advance the simulation clock, so a pause
neither confirms a warning nor restarts one; the timer continues where it stopped. Tune it under
**Bottleneck detection** on the `WaveNeedsPresenter`:

| Setting | Default | Rule |
| --- | --- | --- |
| `BottleneckQueueThreshold` | 2 | BOTTLENECK needs more than this many waiting |
| `GrowthDuration` | 4 s | …and the queue must stay above it and be longer than when it crossed, after this long |
| `RecoveryDrop` | 2 | RECOVERING once the queue is this far below its bottleneck peak; before a warning, a drain this size restarts the timer |
| `RecoveredQueue` | 1 | Warnings clear to OK at or below this; must be below the threshold |

Short spikes never warn. The growth timer restarts when the queue falls below where the timer started,
or drains `RecoveryDrop` guests from its high point. So a burst such as 3 → 6 → 4 that is already
clearing never warns, even if it stays over the threshold for longer than `GrowthDuration`. A one-guest
dip is service noise and does not restart the timer. A queue swinging between 3 and 5 without net
growth never warns either.

Moving a Worker into a bottleneck shows RECOVERING immediately. Removing it shows BOTTLENECK
immediately while more than the threshold are waiting; with a shorter queue the need returns to OK with
`<Building> has no Worker now and serves slower.`, and the normal growth timer decides from there.
Re-warning from RECOVERING needs a full `RecoveryDrop` band of renewed, sustained growth, so the arrival
that lands just after a Worker move cannot flip it back. Because every new warning needs its own growth
timer, two BOTTLENECK warnings for one need are always at least `GrowthDuration` apart. A new wave id
discards every timer, peak and status, so nothing carries over from the previous wave. With the demo tuning, Wave 1's
unstaffed Beach Bar saw-tooths between 1 and 2 waiting without a warning, then turns BOTTLENECK at
4 waiting (about 77 s).

The toast shows one or two short messages for 3.5 s, for example `Worker arrived! Beach Bar is
recovering.` plus `Reception has no Worker now and serves slower.` It never takes taps. While S3 covers
the panel the toast is held, so it is still there when the player returns.

Tapping a card calls `ResortUiStateCoordinator.OpenWorkerAllocationForNeed(card.Need)`, which opens S3
for the serving building: Rooms → Reception, Food → Restaurant, Drinks → Beach Bar, Relax → Pool,
Ocean View → Bungalows. Unpurchased services have no card and are rejected anyway, so S3 stays closed.
That building's S3 card is outlined. A vacant service becomes S3's fixed destination (`MOVE A WORKER TO
BEACH BAR`) and stays fixed after the move, so the next tap cannot pull a Worker to the newly vacant
building. A staffed service is only the focus, so its Worker can still be moved away (`RECEPTION ALREADY
STAFFED · BEACH BAR NEEDS A WORKER`). Cards deliberately do not use `OpenWorkerAllocation(BuildingId)`:
that overload always fixes the destination, so for a staffed building it would disable every MOVE.
`WorkerAllocationContext` holds the target/focus/destination rule that S3's buttons, header and moves
share. S2 stays active under S3, so closing S3 returns to it.

Validated 2026-11-10 in Unity 6000.4.0f1 Play Mode, rendered at 2340×1080 on a fresh profile: the Wave 1
bottleneck, tapping Drinks into S3, moving Reception's Worker, and returning to S2 with RECOVERING and
the toast. Android device readability has not been checked.
Need-card navigation was also checked in Play Mode with 3, 4 and 5 needs, using saves produced by playing
the demo economy (fresh profile, Pool after Wave 3, Bungalows after Wave 6). Every visible card opened
S3 for its building with that card outlined, locked needs left S3 closed, and S2 returned after each close.
Moves from the Drinks and Relax cards behaved as described above.
Bottleneck timing was checked in Play Mode on a fresh profile at 3× speed, reading the S2 cards. Wave 1's
Beach Bar queue first exceeded the threshold at 69.7 s. The wave was paused 1.8 s later; during 24 s of
pause the simulation clock stayed at 71.45 s and the card stayed OK. BOTTLENECK appeared at 77.2 s, when
the queue first grew from 3 to 4. Tapping Drinks and pressing MOVE on Reception showed RECOVERING at
once, and Beach Bar drained to OK (`Beach Bar is back to normal.`) without warning again. Wave 2 opened
with every card at OK and no toast.

## Git LFS

Clone using Git rather than downloading GitHub's source ZIP, then run:

```sh
git lfs install
git lfs pull
```

The source ZIP may contain LFS pointer text instead of PNG/TTF data. The existing artwork was recovered and hash-verified locally; those unchanged objects are already stored in this repository's LFS storage.
