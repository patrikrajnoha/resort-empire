# Unity UI: screens, Figma, TMP and Prefabs

> **Reference status:** This file is an operational extraction and clarification of the original team skill, verified against the bundled project specifications where noted. The authoritative exact wording and values remain in `../sources/`. This file does not assert that the codebase has any particular classes or folder paths.

## 9. UI and screen contracts

**Figma is for composition; Build IV / UI Handoff own behavior.** The nine screen IDs describe roles, **not implementation order**. Follow established uGUI/prefab patterns; separate view-only presentation from commands and event subscriptions; make components reused across visually similar surfaces without coupling unrelated mechanics.

| Screen | Phase / surface | Behavioral contract | Reuse notes |
|---|---|---|---|
| **S1 Home / Island View** | Prototype; full-screen persistent island | Show owned buildings, resort status, Coins/Energy/Resort Value and next-wave forecast; select owned building; `Start Wave`; enable pre-wave Worker changes. During wave show Worker action but **hide purchase/upgrade**, not grey-only. Show unlocked unpurchased plots. MVP adds conditional secondary VIP prompt. | Stable island base and context overlays. |
| **S2 Wave Needs Panel** | Prototype; **side panel next to visible island** | Show served/total per **owned** need, queue state and active Worker; 3→4→5 stable ordering; actionable bottleneck / positive recovering; after arrival closes show draining; optional Energy speed control; **no live numerical satisfaction**. Tap need → S3. | Similar visual card language to S7, **not** same data model/action semantics. |
| **S3 Worker Allocation** | Prototype; modal/bottom sheet on S1 or S2 | Show worker distribution, source/vacancy and destination; free instant transfer, return to invoking context and visibly show both speed gain and source loss. **Wave-only**, anonymous Workers in Prototype; MVP extends to multi-slots. | One worker UI, **not** VIP request assignment. |
| **S4 Wave Complete** | Prototype; full-screen/large overlay | First reveal numeric satisfaction + rating, reasons/reviews, rewards and Resort Value. `Claim Rewards` → S5, once only. | **Shared result template** with S8. |
| **S5 Upgrade Suggestion** | Prototype; modal after S4 | Suggest upgrade grounded in bottleneck, before/after capacity and cost; `Upgrade Now` or `Later`. Available only between waves; MVP can suggest eligible Manager/worker purchases. | Reusable investment components; Prototype does **not** advertise quality upgrade. |
| **S6 VIP Preview / Accept** | MVP; card/modal | Show visitor, requests, required roster/area/material, readiness, risk and possible reward before Accept / Maybe Later / Decline. | Secondary conditional island feature, not always-on homepage. |
| **S7 Guest Request Board** | MVP; dedicated screen | 3 + 1 bonus requests, `MISSING`/`READY`/`IN_PROGRESS`/`COMPLETED`, `Fix`, optional `Speed Up`, `Serve All`, quality caps. **No Workers/S3**. Rating thresholds pending. | Reuse cards/styles from S2, not queue logic. |
| **S8 VIP Complete** | MVP; result surface | VIP rating, request result, Rewards (Reputation primary), optional Perfect Stay reward, `Claim Rewards`. | **Use S4 result prefab/template with VIP content/style variant**, not duplicate layout. |
| **S9 Area Unlock** | Prototype; special full-screen moment | Wave 3 Pool and Wave 6 Bungalows eligibility; Coins transaction adds building/need/assigned Worker; `Later` keeps eligible plot purchasable. MVP reuses for Spa, with **Reputation gate**, no Worker or wave need. | One data-driven reusable screen for multiple areas. |

**Mandatory UI implementation standards inherited from the attached UI skill (`../sources/legacy-ui-instructions.md`):**

**Required Unity UI object creation (applies to all new Canvas UI elements):**

1. **Every UI text element must be `Text (TMP)` / TextMeshProUGUI**, created via Unity's **`GameObject → UI → Text - TextMeshPro`** (also available from the Hierarchy context menu under `UI`). **Do not use the legacy `UI → Text` / `UnityEngine.UI.Text` component.** For text displayed inside a Canvas, use the **UI variant** (`TextMeshProUGUI`), rather than a 3D/world-space `TextMeshPro` component.
2. **Every individual sprite used as part of the UI must be a Canvas `UI → Image` object** (`UnityEngine.UI.Image`), created via **`GameObject → UI → Image`**, with the sprite assigned to the component's **`Source Image`** field. Ensure the imported asset is compatible with **`Sprite (2D and UI)`**. Do **not** substitute `Raw Image` or a `SpriteRenderer` for regular Canvas sprite elements.
3. These component rules apply to UI elements only. **World sprites** used to render the isometric island, buildings, guests, and ship may still use the project's existing **`SpriteRenderer`** pipeline outside the Canvas. Do not convert game-world sprite rendering into Canvas UI just to satisfy this rule.
4. When checking a finished UI window, verify that **every Canvas text is TMP and every ordinary Canvas sprite is an `Image`**, and that there are no accidental legacy UI Text objects. Keep the original Rubik font and prefab rules below.


**Current work allocation is not an architectural requirement.** The team discussed giving Dominik S1/S2/S5/S7/S9 and Natalia S3/S4/S6/S8, with two proposed subtasks under `SCRUM-24`. Do not assert that assignments or completed UI prefabs are live until checked. Confirm who owns a shared prefab before editing it.


- Canvas/uGUI window pattern: scene-level `WindowNameCanvas` (active) and `WindowName` window prefab (inactive by default in hierarchy until opened), **where compatible with existing project structure**. Contents grouped simply by actual function (e.g., `Top`, `Middle`, `Bottom`, `Content`, labels, buttons). Do not create deep, pointless nested GameObjects.
- **PascalCase** for GameObjects (`WaveCompleteCanvas`, `WaveComplete`, `ClaimRewardsButton`). Follow existing prefab/folder placement rather than inventing a path; **every finished UI window is saved in the designated Prefabs directory**, not scene-only. Verify prefab references and scene wiring.
- Use **TextMeshPro** and approved assets `Rubik-Regular`, `Rubik-Semibold`, `Rubik-Bold`; do not emulate weights with TMP Bold/Italic/Underline flags. For uppercase UI use actual uppercase string. Horizontal alignment **Left / Center / Right**; vertical alignment **Midline**. Use Auto Size judiciously; fix overly small or unstable text with explicit sizes.
- Begin with actual Figma dimensions and sprites where available; refine anchored layout for Android landscape and the real Unity hierarchy when literal dimensions look wrong. Programmer integration requirements may affect implementation; explain any material visual deviation. Respect art licenses.
- Main actions within thumb reach, one clear primary action per state, icon+text critical warnings (not just color), no warning flicker; ensure S2 doesn't cover island; validate responsive layouts and safe areas on device. Bind the presentation to state updates; reset event listeners when views close to avoid duplicate commands.
- Do not copy historical Figma mechanics: named staff S3, real-time satisfaction on S2, quality upgrade on Prototype S5, Prototype Energy cap, VIP threshold labels. A visually accurate *but mechanically wrong* UI is incomplete.
- Do not copy generic archive Unity UI skills wholesale: tool-specific assumptions, prefabs/paths or legacy API details may not match this repository. Consult them only for relevant Unity mechanics after project conventions.
