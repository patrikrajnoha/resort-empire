# Architecture, integration, persistence and Firebase

> **Reference status:** This file is an operational extraction and clarification of the original team skill, verified against the bundled project specifications where noted. The authoritative exact wording and values remain in `../sources/`. This file does not assert that the codebase has any particular classes or folder paths.

## 10. Suggested subsystem boundaries (logical, **not invented class names**)

Inspect the repository before choosing scripts, namespaces, folders, GameObject layouts or patterns. If greenfield, use a small number of straightforward, testable components or services; prefer serialized configuration to repeated magic numbers.

| Owner / concern | Owns | Does **not** own |
|---|---|---|
| **Game state & lifecycle** | Active phase, wave entry/exit/pause, turn progression | Economy UI or frame rendering |
| **Buildings** | IDs, need mapping, availability/ownership, levels, seats and Worker slots, building configuration | Wallet deduction logic or UI modals |
| **Wave service simulation** | Scripted arrival, seat occupancy, waiting queues, service progress, rush, need-status data | Prefab text and resource spending |
| **Workers/staff** | Pool, assignments, slot validation, Manager/Specialist ownership; exposes utilization and roster | Need spawn, rating formula or independent wallets |
| **Economy** | Balances, purchases, award claims, boost transaction / idempotency | Manually repainting all resource labels |
| **Ratings** | Waiting-history aggregation, need weighting, star calculation, reviews/recommendation inputs | Converting guest prefabs into individual economic agents |
| **VIP** | VIP schedule/offer, readiness, request lifecycle, quality cap, result/rewards | Wave Worker Allocation UI |
| **UI/view adapters** | Present read-only state snapshots, gather player intent, re-usable prefabs and view states | Secret simulation, direct resource mutation, duplicate business rules |
| **Persistence** | Serialize canonical progression and active-wave snapshot, versioning/migration, save/resume | Independent parallel game-state representation |
| **Firebase adapter (if implemented)** | When implemented: authenticated/anonymous identity, sync, approved Remote Config and conflict handling around local authoritative play | Blocking regular wave actions on network roundtrips |

Recommended directional relationships: data definitions/config → gameplay services → immutable/read-only UI state snapshots; commands from UI → a single owning service; save captures consistent cross-system state. Use ScriptableObjects for static building/wave/VIP configuration **only when it fits existing code**; runtime progress must not accidentally modify shared ScriptableObject assets. Events/C# callbacks can announce updates, but unsubscribe cleanly and avoid an event bus for trivial one-to-one interactions. Avoid adding singletons, dependency injection frameworks, reflection or architectural layers without real needs. Keep APIs/names consistent across owners; if an interface is not yet available, agree on a minimal contract rather than create a second simulator, wallet, or staff pool.

**Integration checks:**
- Buildings ↔ Waves: only owned building needs spawn; actual capacity/slots used.
- Staff ↔ Wave: assignments immediately change service progress; Manager in MVP modifies speed; source building slows.
- Wave ↔ Ratings: need-unit waiting history flows into final result once; no live score display.
- Results ↔ Economy: claim once and update wallet, Resort Value and MVP Reputation/materials consistently.
- Economy ↔ Buildings: purchase/unlock/upgrade atomic, correct phase restrictions, worker bundled once.
- VIP ↔ Building/staff/economy: roster caps, consumable Fix and reward claims consistent; no wave Worker assignment reused.
- Game state ↔ Save: snapshot ongoing service, pending demand, timers, rush fired flag, boosts and placement; consistent resume.
- Local save ↔ cloud: offline works, stale cloud snapshot must not silently overwrite more advanced progress.

## 11. Persistence and Firebase safety

- Maintain **local authoritative playable state** regardless of network. Save after progression and economic changes and when app pauses/backgrounds. Use a versioned save schema, validate data and handle corruption/recovery with visible diagnostics rather than silent resets.
- A wave snapshot must preserve: wave index/phase; arrival elapsed and remaining scripted demand (including extra rush); per-need arrived/queued/in-service/completed units and wait history; per-seat service progress; currently assigned Workers; active boost timers and once-per-building use; `SuddenRushAlreadyTriggered`; reward state/claim IDs. Implement only what the actual simulation requires, but preserve observable continuity.
- Background time pauses normal waves. Different rules for longer **MVP VIP services** must be approved separately; do not reuse elapsed real-world time to advance wave service.
- Stable IDs for economy transactions and rewards prevent duplicate application from double-tap, relaunch, save replay or cloud sync. Test failure between debit/credit and after disk write. Schema migrations should retain existing user progress.
- Firebase is **planned**, not proof it exists; verify actual packages, credentials and permission rules. Never commit secrets or place elevated credentials in client code. Restrict each player's remote saves to their authenticated/anonymous user ID, document sync rules, decide authoritative conflict resolution before turning sync on. Do not implement naïve last-write-wins if it silently loses purchases or progress.
- Remote Config must have cached/default values, predictable validation and a snapshot applied between waves (not alter economics/service rates during a running wave). Offline game remains playable without Firebase.
