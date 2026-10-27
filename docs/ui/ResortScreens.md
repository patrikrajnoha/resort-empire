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

## Git LFS

Clone using Git rather than downloading GitHub's source ZIP, then run:

```sh
git lfs install
git lfs pull
```

The source ZIP may contain LFS pointer text instead of PNG/TTF data. The existing artwork was recovered and hash-verified locally; those unchanged objects are already stored in this repository's LFS storage.
