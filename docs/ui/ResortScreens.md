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
| S2 `WaveNeedsPanel` | `WaveNeedsPresenter` | Served/total and queue state per owned need, stacked 3 → 4 → 5. Tapping a card opens S3. |
| S3 `AssignStaffRestaurant` | `WorkerAllocationPresenter` | One card per owned building. MOVE sends that Worker to the single vacant building and names both the helped and the slowed building. |
| S4 `WaveComplete` | `WaveResultsPresenter` | Result, longest-queue warning, `+N` rewards, claim once. |
| S5 `UpgradeSuggestionPanel` | `UpgradeSuggestionPresenter` | Capacity upgrade for the longest queue. LATER or UPGRADE NOW returns to S1. |
| S9 `AreaUnlockPanel` | `AreaUnlockPresenter` | Pool after Wave 3, Bungalows after Wave 6; announced once per session. |

Deliberate deviations from the mockups, for the screen owners to review:

- **S2:** the live satisfaction stars, value, bar and thresholds are deactivated, because Build IV
  forbids numeric satisfaction during a wave. Relax and Ocean View cards were added as copies of the
  Food card, and every card gained a warning row and a Button. These are scene overrides on the
  prefab instance; the prefab asset is unchanged.
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

## Git LFS

Clone using Git rather than downloading GitHub's source ZIP, then run:

```sh
git lfs install
git lfs pull
```

The source ZIP may contain LFS pointer text instead of PNG/TTF data. The existing artwork was recovered and hash-verified locally; those unchanged objects are already stored in this repository's LFS storage.
