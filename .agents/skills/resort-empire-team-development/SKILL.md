---
name: resort-empire-team-development
description: >-
  Resort Empire Unity/C# Android development guidance. Use for work on resort
  buildings, guest waves, Worker management, economy, satisfaction, VIP visits,
  Figma-to-Unity UI, prefabs, Git collaboration, Jira tasks, save/Firebase,
  debugging, testing, architecture, integration, or an explicitly requested
  session handoff. Read the relevant references before editing project files.
---

# Resort Empire — shared team development skill

**Edition:** 2.1 · **Reviewed:** 2026-10-23 · **Applies to:** Resort Empire: Paradise Tycoon only.

## Purpose and task contract

Help a team of ~4–5 students deliver a coherent, testable **Unity/C# 2D-isometric landscape Android resort-management game** in a roughly 2–3 month university-project window. The project is a **calm active-management tycoon, not an idle-income game**.

**Loop:** prepare buildings and Workers → manually start a guest wave → watch service demand and queues → reassign Workers or optionally boost → finish all demand → receive a satisfaction/rating result → claim rewards → improve the resort → repeat. **Prototype** validates this wave loop; **MVP** expands to VIP requests, advanced staffing, and other approved systems.

**Final deliverable:** a playable, installable Android build with the scope actually approved for the delivery phase, consistent gameplay/UI/persistence, documented requirements and measured or honestly reported tests. A planned feature is not proof it has been implemented. Do not claim the entire prospective MVP will fit a deadline without schedule evidence.

**Response language:** speak to each developer in their language. Keep source identifiers, game UI strings, and Jira descriptions in English where appropriate. Handoffs are English-only by explicit request.

## Before any coding or Jira change

1. Identify the **specific task/Jira key**, relevant **Prototype/MVP** phase, its acceptance criteria and owner. If live Jira is inaccessible, say so: the bundled backlog is a **dated draft, not live Jira**.
2. Inspect the **actual repository**: applicable `AGENTS.md`, Unity version/project settings, relevant existing scripts/scenes/prefabs/configs, branch status and changes. Do not invent paths, classes, features, packages or tool access.
3. Read **only the references relevant to the task**, then the authoritative bundled sources when behavior, scope or numbers matter. Treat Figma as visual guidance, not the source of game mechanics.
4. Identify dependencies and existing owners. Coordinate shared scenes/prefabs/interfaces instead of implementing parallel systems.
5. Implement the smallest maintainable, testable change. Never modify unrelated work or perform destructive Git actions. Do not commit/push unless explicitly requested.
6. Verify to the extent tooling permits. Report changes, tests **actually run**, unmet acceptance criteria and blocked decisions. Never claim Unity/Android/Figma/Jira validation without performing it.

## Read the relevant reference on demand

| When the task concerns... | Read first |
|---|---|
| Waves, buildings, queues, satisfaction, Workers, upgrades | [`references/gameplay.md`](references/gameplay.md) |
| Coins, Energy, VIP, Reputation, Materials, Gems, Spa | [`references/economy-and-vip.md`](references/economy-and-vip.md) |
| S1–S9, Figma implementation, canvas hierarchy, TMP, prefabs | [`references/unity-ui.md`](references/unity-ui.md) |
| C# code organization, cross-system integration, save/Firebase | [`references/architecture-and-persistence.md`](references/architecture-and-persistence.md) |
| Git safety, teammates, Jira issue boundaries, sprint work | [`references/git-and-jira.md`](references/git-and-jira.md) |
| Test plans, Definition of Done, investigation, reporting | [`references/testing.md`](references/testing.md) |
| Conflicts between documents, unresolved design decisions | [`references/decisions-and-conflicts.md`](references/decisions-and-conflicts.md) |
| Explicit handoff or context transfer before `/clear` | [`references/session-handoff.md`](references/session-handoff.md) |

**Authoritative sources packaged offline:** `sources/build-document-prototype-mvp-v4.md` (main mechanic/phase authority), `sources/functional-nonfunctional-requirements.pdf` (F01–F55; N01–N26; may conflict), `sources/ui-screens-handoff.md` (UX contract), `sources/game-design-document.md` (pillars), `sources/jira-backlog-snapshot-2026-10-14.md` (editorial backlog, not live), plus historical UI/handoff guidance. Read the relevant source sections when references are insufficient; do **not** read all source documents for a trivial edit. Additional actual repo docs or approved decisions may supersede this October snapshot.

## Key invariants (never silently change)

- Wave lifecycle: `AWAITING_WAVE` → `WAVE_INCOMING` → `WAVE_ACTIVE` (arrival, then queue drain) → `WAVE_RESOLVING` → `WAVE_COMPLETE` → rewards/optional upgrade → `AWAITING_WAVE`. A normal wave does **not** finish merely because its ~90 s arrival window ended; all arrivals already generated must finish service.
- Prototype starts with **Reception/Rooms, Restaurant/Food, Beach Bar/Drinks** and **2 Workers for 3 buildings**; Pool/Relax becomes eligible after Wave 3, Bungalows/Ocean View after Wave 6. Each purchase includes one assigned Worker. **Eligible is not owned**; unowned services generate no wave demand. Eight scripted waves.
- Anonymous Workers in Prototype: free, instant reassignment, only one slot per regular building, one unstaffed building at any time. Unstaffed service remains slow, not stopped; moving Workers affects services already in progress. No named staff roster in Prototype.
- Services use **discrete seats with queues**; incoming demand progresses uniformly except Wave 2's one-time **Sudden Rush**. The active wave continues while arrivals stop and queues drain; UI warns about bottlenecks without showing numeric satisfaction live.
- Under **Build IV**, Prototype satisfaction measures each *need unit's waiting until seating*; requirements PDF F44 separately includes service duration. This is a **known conflict**. Do not quietly merge formulas. Wave results use weighted satisfaction (Ocean View heavier), low-but-nonzero minimum rating, and allow 5★/Perfect without Energy in a sufficiently prepared resort.
- Energy is optional (never a gate or worker-move cost); no passive/offline wave progress, passive Coins or Energy regeneration. Waves pause when app backgrounds; active queues, timers, boosts and Sudden Rush status resume from the frozen save.
- Coins/Materials/Gems use canonical atomic transaction handling, claim rewards exactly once. Resort Value and Reputation are cumulative, not spendable. **Never** give Gems a temporary speed/Energy effect.
- Prototype upgrades Lv1–Lv3 affect **capacity**; Lv4/Lv5 Manager/extra Worker slot/quality systems are MVP. No new purchases, upgrades or hiring during active waves.
- VIP is optional **MVP only**: S6 preview → S7 request board → S8 result; roster gaps lower quality caps while missing consumables may require `Fix`; **do not** send VIP into S3 Worker Allocation. Spa is VIP-only with no wave need or Worker slot.
- UI screen IDs: S1 island, S2 wave needs, S3 Worker Allocation, S4 wave results, S5 upgrade suggestion, S6 VIP preview, S7 VIP request board, S8 VIP results, S9 area unlock. Reuse S4/S8 result template; S2/S7 may share visuals but **not mechanics**.
- **Mandatory Unity Canvas UI component types:** create every UI text element using **`UI → Text - TextMeshPro`** (`TextMeshProUGUI` / `Text (TMP)`), never the legacy `UI → Text`. Add individual sprite images in a Canvas using **`UI → Image`** (an `Image` component with the sprite assigned to `Source Image`), not `Raw Image` or a scene `SpriteRenderer` in place of a UI image. This does not prohibit `SpriteRenderer` for sprites in the actual 2D-isometric game world. See `references/unity-ui.md`.

## Source precedence and unresolved decisions

**For game behavior/phase:** current explicitly approved team decisions → Build Document **Version IV** → approved requirements with conflicts flagged → UI Handoff → GDD → visual mockups. **For issue deliverables:** live Jira AC and current task instruction govern what work is requested, but do not silently overturn approved game rules. **For implementation details:** existing repo code is evidence of current implementation, not proof the gameplay is correct. If sources conflict: record both with exact section IDs; follow documented precedence only if unambiguous; otherwise isolate the change and request a decision. See `references/decisions-and-conflicts.md`.

## Scope and collaboration guardrails

- **Single owner for gameplay state.** UI displays snapshots and issues commands; it does not run its own simulation or mutate a duplicate wallet. Saving serializes the existing canonical state, not a parallel model.
- **No speculative scope:** do not add new islands, online multiplayer, full stores/ads, extra waves or uncontrolled staff rarity just because a mockup includes them.
- **No claims of live access:** Figma nodes, Jira status/assignees, repository path/Unity version, Firebase service, actual target device and `wave_sim.py` presence all require inspection.
- **Prototype and MVP are not interchangeable.** A Figma mockup may be historical. Implement visuals compatible with the active phase; report mismatched mechanics before extending them.
- For major work, finish with the evidence-based report defined in [`references/testing.md`](references/testing.md). For explicit context handoff use [`references/session-handoff.md`](references/session-handoff.md), not automatically.
