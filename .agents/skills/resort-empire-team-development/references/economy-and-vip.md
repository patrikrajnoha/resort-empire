# Economy and VIP behavior

> **Reference status:** This file is an operational extraction and clarification of the original team skill, verified against the bundled project specifications where noted. The authoritative exact wording and values remain in `../sources/`. This file does not assert that the codebase has any particular classes or folder paths.

## 7. Economy and transactions

| Concept | Scope | Semantics |
|---|---|---|
| **Coins** | Prototype + MVP | Spendable for unlocked-area purchases, upgrades; MVP also hiring. Main income from waves / VIP, **not passive offline income**. |
| **Energy** | Prototype + MVP | Optional acceleration, **never a gate**. Prototype Mode A: speed boost one building for a temporary duration, at most once per building per wave; examples 3 Energy, half service time for 15 s. Never raises capacity. Source: rated wave/VIP drops; optionally **voluntary** rewarded ad. No time-based regeneration. Prototype has **no Energy cap** and boost is not refunded. |
| **Resort Value** | Prototype + MVP | Cumulative prestige/status, **never spent or decreased**; separate from currencies and rating. |
| **Reputation** | MVP | Cumulative **threshold** for unlocking eligible content, mainly earned by VIP and some wave rewards; never spent/decreased. Eligibility ≠ owning. |
| **Materials** | MVP | Spendable for some VIP `Fix` requests and Lv4–5 building upgrades; mainly from VIP, smaller rewards on waves ≥4★. Flavour labels such as Mangoes must not create extra currency systems. |
| **Gems** | MVP | Permanent premium purchases only, **never** service speedups, direct Energy conversion or pay-to-play wave gates. Actual IAP integration is conditional on approval. |

**Transaction requirements:** one authoritative resource model (no duplicate UI wallet); validate affordability and conditions; apply deduction and benefit as a single logical operation; never negative balances, double purchases, duplicate unlock workers or double-claimed rewards. Use stable action/claim IDs and persist claim state for idempotency across double-taps, app relaunch and sync. UI sends commands and subscribes to authoritative state; UI never independently grants resources or computes award balances. On failure, avoid partial deduction or partial upgrade. Apply Config/Remote Config changes at safe boundaries, **not halfway through a wave**.

**No paywall for core:** `Start Wave` and VIP completion must not require Energy, advertising, or real-money purchase; 5★/Perfect Stay must be achievable without Energy for a sufficiently prepared resort. Returning after inactivity never causes decay or an automatic negative event.

## 8. VIP (MVP only) — a deliberately different gameplay paradigm

- **Four VIP types:** Travel Blogger, Wedding Couple, Investor, Influencer. Optional secondary content: preview → Accept / Maybe later / Decline → dedicated Guest Request Board → results/rewards. VIP must not interrupt or block a regular wave as an exclusive mandatory activity.
- The normal wave is **system management** (aggregate need queues). VIP is **individual request management** (three main requests and one bonus). Never send ordinary tourists to the VIP Request Board. **Never use S3 Worker Allocation to solve VIP requests.**
- VIP quality tests **preparation**: already-owned buildings, hired Managers and Specialists, decor and consumable Materials. Missing consumable/decor may create request `MISSING` and offer `Fix`; missing building/roster is a **soft quality cap**, usually not a blocker to completion. Do not offer `Fix` to hire a Manager mid-visit.
- Per-request lifecycle: `MISSING` → `READY` → `IN_PROGRESS` → `COMPLETED`; quality cap is an **attribute**, not another mutually exclusive request state. `Serve All` is a board-level action; `Speed Up` may accelerate an in-progress VIP service, optional Energy Mode B in `READY` may compensate for imperfect preparation under approved rules.
- VIP result includes satisfaction/rating, completed and incomplete request feedback, and eligible Reputation (primary), Coins, Energy, Materials, Resort Value and sometimes permanent decor. Reward claims must be idempotent.
- Spa is VIP-only, provides *no* ordinary wave need or Worker slot; missing Spa reduces relevant VIP request quality. At least two VIP types should require Spa services. Its levels and exact VIP rating thresholds are unresolved as described in §3.
- Do not invent new VIP request details or timings from screenshots. Use actual approved content tables when available; placeholder data must be explicitly marked and must not be confused with acceptance-complete game content.
