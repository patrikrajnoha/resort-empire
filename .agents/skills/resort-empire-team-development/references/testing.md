# Execution playbooks, tests and Definition of Done

> **Reference status:** This file is an operational extraction and clarification of the original team skill, verified against the bundled project specifications where noted. The authoritative exact wording and values remain in `../sources/`. This file does not assert that the codebase has any particular classes or folder paths.

## 14. Per-task execution playbooks

### A. Universal implementation loop

1. **Understand request:** desired behavior, Jira issue and AC, Prototype/MVP boundary, current code and any conflicts.
2. **Inspect:** status/diff, relevant scene, prefab, script, ScriptableObject/config, event connections, existing service APIs and reference sections. Identify teammate-owned surfaces.
3. **Plan minimal change:** affected files/contracts, inputs/outputs, expected failure cases, compatibility and tests. Explain consequential decisions; keep routine changes lightweight.
4. **Implement:** reuse model/prefabs, avoid duplicate systems, ensure transaction/state consistency, do not modify unrelated resources.
5. **Verify:** compilation when Unity tooling exists; unit/Play Mode/scene checks appropriate to change; validate screenshots against available Figma; Android build/device when supported.
6. **Review/report:** diff, modified paths, implemented AC, tests *actually run* and outcomes, unmet dependencies and contradictions. No automatic commits or Jira edits.

### B. UI feature

Check the screen ID and exact Prototype/MVP surface. Read UI Handoff and current Figma node if accessible; inspect Canvas hierarchy, anchors, TMP assets, existing common panels and source of gameplay state. Build a reusable prefab (not just a scene instance), bind to authoritative data, handle enabled/hidden actions for current phase, verify dynamic content (3–5 needs, long labels, reduced viewport). Compare in Editor and Android device if available; otherwise report unchecked visual/integration items.

### C. Wave/service feature

Read Build IV §4.3–4.8; inspect one canonical wave lifecycle and need data path. Ensure queued-vs-in-service distinction, gradual release, nonzero empty utilization, service-progress reaction to Worker/boost moves, rush event once, deterministic end state and pause/resume; test WR1–WR9 and energy-free best-case. **Never fake 90-second hard wave expiry.** If simulator exists (`wave_sim.py`), run it with the build's current parameters and document results; if unavailable, do not claim WR7/WR8 verified.

### D. Buildings/staff/economy

Trace model → command/transaction → visual update → persistence. Validate locked versus eligible versus owned, Worker bundled exactly once, level and slot/phase restrictions, affordable/unaffordable states and double taps. Verify that reassignment is free and that UI doesn't mutate wallet. Separate Prototype anonymous Workers from MVP Manager/Specialist hires.

### E. VIP feature

Check MVP permission and approved content; inspect readiness and request configuration; distinguish consumable `MISSING` from roster quality cap; do not use ordinary wave Worker Allocation. Test Accept/Maybe Later/Decline, complete despite imperfect roster, allowed Fix/Speed Up transitions, outcomes and one-time rewards, plus Spa request content.

### F. Save, sync or infrastructure

Trace canonical state and ownership, Android lifecycle, local save atomicity, replay-safe reward IDs, simulation freeze on pause, version migration, offline use and Firebase unavailability. For cloud work, inspect actual Firebase package, project settings, auth and security rules; never create credentials or promise backend behavior without access. For Android changes, use project-supported Unity version/build tools and verify artifacts/device targets.

## 15. Testing and Definition of Done

**Core DoD:** implementation satisfies the live Jira AC (or explicitly documented draft AC); Unity project compiles where tooling is available; correct scene/Prefab asset and references exist; normal and edge cases work; game-state ownership remains single-source; no unrelated regressions or colleague edits; platform checks run to feasible extent; changes/tests/limits reported accurately.

**Minimum scenario matrix (adapt to task scope):**

| Area | Representative positive + negative cases |
|---|---|
| Wave | Start manually; gradual demand; drain after arrival; every seat completes; unavailable need absent; pause/resume; wave 2 rush once; no Energy required. |
| Workers | Pre-wave and mid-wave reassignment; effect on **in-progress seats**; source slowdown; reject full slot; assignments restored; 2/3 → 3/4 → 4/5. |
| Satisfaction | Queued-only wait; seated unit wait=0; floor invariants; 1★/5★/Perfect; Ocean View weight; no live numeric score. |
| Economy | Exact-affordability boundary; insufficient Coins/Energy/Materials; double tap; failure/rollback; claim repeated on reopen; no negative balance. |
| Building | Gate after Wave 3/6; defer purchase; return via plot; bundled Worker; level3 Prototype limit; phase-appropriate upgrade actions. |
| VIP | Optional decline; prepared/unprepared cap; missing Materials Fix; missing role doesn't hard-block; Spa not wave need; reward once. |
| Save/Sync | Resume while arrival, drain, boost or rush pending; offline play; corrupted or old save; cloud failure/conflict without silent rollback. |
| UI | Landscape touch size, safe areas, no obstruction, 3–5 stable need layout, popup closure/listener cleanup, **all Canvas text is Text (TMP)/TextMeshProUGUI (no legacy UI Text)**, **Canvas sprites use UI → Image with Source Image**, Rubik font and saved prefabs. World-space sprites may continue using SpriteRenderer. |
| Android | Build installs/launches; 30 FPS target on agreed device, orientation and package size checked if test device/build tool available. |

For **Prototype playtest**, seek ≥5 non-team participants; assess comprehension of forecast, moving a Worker without hint, capacity-vs-staff distinction, interpreting results and both unlocks. Proposed requirements plan expects ≥4/5 users to perform key tasks unaided and understand tutorial in ~2 min; these are **test targets, not results already obtained**. Eight waves may be tested with seeded save states around Waves 3/6 to keep a session manageable. Capture issues and revise UI or mechanics according to evidence.

**Verification honesty:** distinguish static review, code compilation, Unity Editor Play Mode, actual Android device, simulator, and human playtest. A file merely saved or syntactically plausible is **not** “tested in Unity”.

## 16. Frequently wrong assumptions — prohibition list

- Do not call the game an idle game, accumulate offline wave earnings, or name the ready state `IDLE`.
- Do not spawn all demand at t=0, end at 90 s, remove guests waiting too long or treat a full seat as queue.
- Do not calculate Prototype satisfaction from the last-frame empty queue or silently include occupied service duration (F44 conflict).
- Do not require Energy to move Workers, finish waves/VIP, achieve 5★/Perfect with a prepared resort; no forced ads.
- Do not treat Workers as named Managers or VIP Specialists; no named staff characters in Prototype.
- Do not make Spa a sixth regular wave need or add a Spa Worker slot.
- Do not treat unlock eligibility as purchase, or spawn a need from an unlocked **but unowned** building.
- Do not confuse quality upgrades with Lv1–3 capacity upgrades, or allow purchases/hiring during a wave.
- Do not share S3's Worker logic with VIP Request Board; visual prefab reuse does not imply gameplay coupling.
- Do not show numerical satisfaction live, or trust outdated Figma worker cards/ratings over Build IV.
- Do not decrease Reputation or Resort Value, use Gems for speed/Energy, or award anything twice.
- Do not assume the project has a given Unity version, specific folder, classes, tests, Firebase integration or branch convention without inspection.
- Do not infer live Jira status or developer ownership from the dated consolidated backlog.
