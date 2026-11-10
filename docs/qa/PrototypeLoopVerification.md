# Prototype gameplay loop verification

Verification of the full Prototype loop in Unity Play Mode, played only through the game's own buttons:

Start Wave → Guest Arrival → Service Needs → Worker Reassignment → Wave Complete → Rating → Claim Rewards → Next Wave

**Result:** the loop works end to end. Two waves were played and claimed, and Wave 3 was offered. Two defects
were found; neither blocks the loop (see [Defects found](#defects-found)).

## Test setup

| Item | Value |
| --- | --- |
| Date | 2026-11-10 |
| Git revision | `d9847a9` on `develop` (only unrelated, uncommitted Unity AI Assistant settings files in the tree) |
| Unity | 6000.4.0f1, Linux Editor |
| Scene | `Assets/_ResortEmpire/Scenes/Resort.unity`, Game view 1026×436 |
| Profile | Fresh: no save file, wallet 0/0/0 |
| Speed | `Time.timeScale = 1`, real time; no `AdvanceSimulation`, no debug context menus or hotkeys |

**Input.** Every action was a pointer tap from a virtual Input System mouse, at a point on the button where the
scene's `EventSystem` raycast resolves to that button. Taps went through `InputSystemUIInputModule`, the
`GraphicRaycaster`s and the `Button` components, as a player's would. A covered or non-interactable button
cannot be clicked this way. Gameplay state was only read, never changed, apart from one deliberate replayed
claim (see Wave 1).

Because the Editor window did not have OS focus, two Input System settings were changed in memory for the run
only, and restored afterwards:

- `editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView`
- `backgroundBehavior = IgnoreFocus`

There is no Input System settings asset, so no file changed. The real mouse was disabled during the run so
stray clicks could not reach the game.

## Results

| # | Check | Result |
| --- | --- | --- |
| 1–2 | Resort scene starts; START WAVE 1 tap starts the wave | Pass. WAVE_INCOMING → WAVE_ACTIVE after 2 s; S2 opened |
| 3 | Correct wave configuration | Pass. Rooms/Food/Drinks 12 units each, 3 seats, 12 s service. Reception and Restaurant staffed, Beach Bar vacant |
| 4 | Demand generates | Pass. Arrivals every 7.5 s from 7.5 to 90.0 s per need, 36/36 arrived before completion |
| 5 | Queues and service | Pass. Staffed services kept a wait of 2.0 s per unit. The unstaffed Beach Bar took 36.36 s per unit and turned BOTTLENECK at 77.0 s (4 waiting, NO WORKER) |
| 6 | Reassign a Worker through the UI | Pass via the island's Beach Bar marker, then MOVE on Reception's S3 card at 79.07 s. **The S2 card route failed** (defect 1) |
| 7 | Reassignment affects service | Pass. Beach Bar went from 36.36 s to 12.00 s per unit; Reception from 12.00 s to 36.36 s. A unit already in service slowed when its Worker left (17.0 s) |
| 8 | Capacity respected | Pass. No service ever had more than 3 occupied seats, checked every frame and from the completion records |
| 9 | Wave finishes naturally, only when all service is done | Pass. Wave 1 completed at 128.36 s, in the frame its last unit finished: 36/36 served, no queue, no occupied seat |
| 10 | Results show correct rating and rewards | Pass. S4: WAVE 1 COMPLETE!, 36/36, 93.3%, 5 stars, no Perfect, "Beach Bar had the longest queue", +270 Coins, +3 Energy, +5 Resort Value |
| 11–12 | Claim through the UI; wallet updates | Pass. CLAIM REWARDS took the wallet from 0/0/0 to 270/3/5. S4 closed and S5 opened; the S1 HUD showed the new values |
| 13 | Repeated claims rejected | Pass. CLAIM was disabled and S4 closed. Replaying the claim through the facade returned `Rejected` ("No unclaimed wave result is available.") and changed nothing. The save held 270 Coins and the reward id |
| 14 | Back to the post-wave screen | Pass. S5 offered a Beach Bar upgrade (3 → 5 seats, 200 Coins). LATER returned to S1 with START WAVE 2 and "NEXT: 45 GUESTS" |
| 15 | Start the next wave | Pass. START WAVE 2 tap; 15 units per need. The Sudden Rush added 6 Rooms units to the unstaffed Reception at 45 s into the wave |
| 16 | State and resources persist | Pass. Wave 2 started with 270/3/5 and the moved Worker still at Beach Bar. After leaving and re-entering Play Mode the game reloaded: 2/8 waves, 525/4/8, same Workers, START WAVE 3 |
| 17 | Unity integration tests | Pass. 251/251 EditMode tests. The project has no Play Mode test assembly |
| 18 | Console | No errors or warnings during either wave. One error at every Play Mode start (defect 2). One Unity AI Assistant package warning (editor tooling) |

**How the rating was checked.** Satisfaction, stars and rewards were recomputed from the per-unit completion
records (arrival and service-start times) using the rules in
[EconomyIntegration.md](../gameplay/EconomyIntegration.md#demo-waves-and-ratings). The recomputation was
independent of `WaveRewardCalculator`. Both waves matched the canonical result and the S4 text exactly:

| Wave | Units | Satisfaction | Stars | Coins | Energy | Resort Value | Wallet after claim |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 36 | 93.279% | 5 | 36 × 5 × 1.5 = 270 | 3 | 5 | 270 / 3 / 5 |
| 2 | 51 | 75.655% | 3 | 51 × 5 × 1.0 = 255 | 1 | 3 | 525 / 4 / 8 |

**Wave 2.** No Worker was moved in Wave 2. This deliberately left the rush on the unstaffed Reception: every
Reception unit took 36.4 s, and the wave ran until 402.91 s, 51/51 served. S4 showed 75.7%, 3 stars,
"Reception had the longest queue" and +255 Coins, +1 Energy, +3 Resort Value. CLAIM, the rejected replay,
S5 (Reception upgrade) and LATER behaved as in Wave 1.

## Defects found

Both defects should be filed as separate bugs.

1. **S2 need cards do not receive taps.** Every card's background `Image` (the `Button`'s target graphic) has
   Raycast Target off, both in `WaveNeedsPanel.prefab` (Rooms, Food, Drinks) and on the Relax and Ocean View
   copies in the scene. Only the Speed Up strip inside each card is hit. A tap anywhere else falls through to
   the scroll `Viewport`, so the card never opens S3. The bottleneck toast still tells the player to
   "Tap Drinks to send one." The island building markers are a working alternative, so the loop is not blocked.
   The SCRUM-88 Play Mode check missed this because it invoked the card `Button` directly, and no automated test
   checks pointer raycasts on the cards.
2. **Ocean background video does not import in the Linux Editor.** `OceanBackground.mp4` (H.264, present and
   pulled from LFS) imports as a `DefaultAsset` with no frames, so the prefab's `VideoClip` reference is null.
   `OceanVideoBackground` then shows the static fallback and logs "Ocean background video could not be prepared
   or played: Ocean video clip or playback texture is missing." as an error on every Play Mode start.
   It is cosmetic. Not checked on Windows, macOS or Android.

## Not covered

- Android device or other resolutions; only the 1026×436 Game view was used.
- Waves 3–8, UPGRADE NOW, area purchases, Energy boosts, Sudden Rush buttons and pausing. These were not tapped
  in this run; the EditMode tests cover them.
- NPC visuals and balancing (out of scope).
