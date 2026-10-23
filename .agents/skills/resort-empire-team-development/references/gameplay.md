# Game systems: Prototype, waves, staff and upgrades

> **Reference status:** This file is an operational extraction and clarification of the original team skill, verified against the bundled project specifications where noted. The authoritative exact wording and values remain in `../sources/`. This file does not assert that the codebase has any particular classes or folder paths.

## 4. Phases and scope boundary

| | **Prototype** — priority 1 | **MVP** — after Prototype validation |
|---|---|---|
| Objective | Test whether watching bottlenecks, allocating Workers and upgrading feels meaningful | Add VIP/content/staff progression without breaking the core |
| Guest demand | One Casual Tourist wave-demand type; **8 scripted waves** | Same wave model + four optional VIP profiles |
| Wave buildings | Start Reception, Restaurant, Beach Bar; unlock Pool then Bungalows | Same five ordinary wave buildings; add **VIP-only Spa** |
| Active needs | Rooms, Food, Drinks → Relax → Ocean View | Five ordinary wave needs; no new Spa need |
| Workers | 2 for 3 buildings → 3 for 4 → 4 for 5; anonymous, 1 slot/building | Hired additional Workers, diminishing returns, 80%-of-slots cap (tuning) |
| Staff roles | No named staff whatsoever, including visuals | Managers Receptionist/Chef/Bartender/Lifeguard/Bellhop; Specialists Butler/Captain |
| Building levels | Lv1–Lv3: capacity upgrades only | Lv4 adds slot + quality, Lv5 adds capacity + slot + quality |
| Resources | Coins, Energy, non-spendable Resort Value | Add Reputation, Materials, Gems |
| Energy | Mode A temporary wave service-speed boost | VIP speed/quality features where approved; regular-wave Mode B pending |
| UI | **S1–S5 and S9** = 6 distinct interfaces | + **S6–S8** = 9 distinct interfaces |
| Explicitly out | VIP, Request Board, Managers, Specialists, rarity, gem/IAP store, reputation, materials, social/club, daily loop | Rarity/collection, live-ops, additional islands, generic event hub unless approved |

The prototype's eight-wave playthrough can take ~12–20 minutes; this does not contradict the *ordinary session* target of 3–7 minutes / roughly 1–2 waves. Preserve short-session save/resume. Do not use `IDLE` as a core wave state name: the specification calls it `AWAITING_WAVE` and explicitly rejects “idle” framing.

## 5. Core gameplay: buildings, waves, demand

### 5.1 Buildings and service mapping

| Building | Need | Availability | Interpretation |
|---|---|---|---|
| Reception | Rooms | Owned from new game | Check-in **and basic hotel accommodation** |
| Restaurant | Food | Owned from new game | Meals and dining |
| Beach Bar | Drinks | Owned from new game | Drinks and open beach counter |
| Pool | Relax | Eligibility after completing Wave 3; purchase with Coins | Pool relaxation service |
| Bungalows | Ocean View | Eligibility after completing Wave 6; purchase with Coins | Premium ocean-view lodging need; lower demand but higher satisfaction weight |
| Spa | **No ordinary wave need** | MVP; Reputation gate and Coins purchase | VIP request readiness only; **no Worker slot** |

An eligible unlock is **not** an owned building. Construction-plot lifecycle: locked → purchase-eligible → owned; insufficient Coins remains eligible but not affordable. When purchasing Pool or Bungalows, atomically add the building **and one Worker assigned to it**. An unpurchased building's need is **absent from forecast and waves** (WR9), even after its eligibility gate opens. S9 can reopen when the player taps an eligible plot between waves. Debut of a newly owned need should be gentle (~half existing need, tuning); existing demands do not simultaneously grow in its debut wave. Delay of purchases must not break demand tables.

### 5.2 Wave lifecycle — source: Build IV §4.3–4.4

`AWAITING_WAVE` → `WAVE_INCOMING` (brief ship/shuttle arrival) → `WAVE_ACTIVE` (arrival, then draining) → `WAVE_RESOLVING` → `WAVE_COMPLETE` → claim rewards / `UPGRADE` suggestion and optional S9 → `AWAITING_WAVE` next wave. An active wave transitions to `WAVE_PAUSED` on app suspension; resume exactly the frozen wave state.

- Player starts waves **manually**, without spending Energy, payment, or watching ads; at most one regular wave active.
- Forecast active owned services and wave demand. Before starting, allow Worker reassignment.
- During arrival, each scripted need's demand is released **gradually and uniformly**, `arrival_rate = demand_total / wave_timer` (subject to one Sudden Rush exception). Approximate `wave_timer = 90 s` is **TUNING**, not a hard-coded promise.
- After the arrival timer expires, **stop new demand** and continue to serve the queue. **Wave completion requires all arrived demand to finish processing**, not simply an empty waiting line if customers are still in service; see §4.3 resolving condition / service seats. No guest disappears during a wave, and draining may lengthen the total wave.
- Worker moves and applicable Energy boost stay available during **both arrival and draining**. Upgrades, area purchases and staff hiring are between-wave only; do **not** show their actions during active wave states.
- App suspension freezes simulation timers, need arrivals, queue positions, service progress, rush triggers and boost duration. Offline time does **not** complete a regular wave or accumulate idle Coins.
- Wave 2 scripted **Sudden Rush** targets the building **currently lacking a Worker**, not a fixed Beach Bar target. Unexpected extra demand enters the selected need (example +6 spread across ~10 s around 45 s, TUNING). It does not pause the wave. Choices: `Move Worker here`, optional `Use Energy to serve faster`, `Ignore`. Ignore dismisses UI, not extra demand; the event occurs once and must not retrigger on save/load. It remains solvable without Energy.
- Wave reward Coins are derived from **total need units including rush** × `coins_per_unit` × rating multiplier, not elapsed drain duration. Apply only once when claimed.

### 5.3 Simulation model — *seats*, not fake throughput counters

- A building's **capacity** is the number of simultaneous service seats. Arrived service units enter available seats; excess units enter a **waiting queue**. Waiting ends upon seating. An occupied seat is not a waiting-queue bottleneck.
- Each service unit has arrival, queue-wait, occupancy and normalized service progress. A service's **work progress** advances over time, so worker moves, Manager multipliers or Energy boost affect **already-seated units immediately**, not only the next visitor. No lost progress on reassignment.
- For estimation only, `fill_rate ≈ capacity × utilization × manager_multiplier × boost_multiplier / service_time`. Actual simulation must respect discrete seat occupancy and completion events; do not implement the estimate as a competing second simulation.
- Prototype utilization: staffed = 1.0; empty **> 0** (example 0.33, TUNING), so a Worker-free building is slow, never stopped. Example `service_time = 12 s`; 6 s was explicitly rejected as too easy; neither is an immutable balancing constant.
- Need UI warnings derive from **waiting queue growing over threshold/hysteresis**, not occupied seats. Need states: `OK`, `BOTTLENECK`, `RECOVERING`, `SATISFIED`; a non-purchased need is absent / locked, not simulated. After an effective Worker move, communicate improvement immediately (`RECOVERING`), while retaining an honest warning until the queue actually recovers.
- Buildings determine **concurrent capacity**. Workers determine **utilization**; Managers (MVP) multiply service speed; Energy temporarily speeds service. A speed boost **never increases capacity**. Avoid conflating a capacity upgrade with faster base service time.

**Simulation acceptance invariants (Build IV WR1–WR9):**

1. `unit_floor < wave_floor < 3-star threshold`; `utilization_empty > 0`.
2. Arrival timer only closes arrival; all service finishes before wave closes. No unit abandons a queue mid-wave.
3. Worker reassignment is immediate and **free**; upgrades are never purchasable mid-wave.
4. From Wave 3, except gentle debut Waves **4 and 7**, best static placements without upgrades/Energy/worker moves should **not** yield 5★ (WR7). Fully upgraded resort at current-phase maximum must be capable of 5★ in each wave **without Energy** (WR8). Validate using actual balancing simulator if present; if not, say unverified.
5. A not-yet-purchased service never spawns need demand (WR9).
6. Results below 5★ are soft feedback, **not failure** or a destructive penalty for subsequent waves.

### 5.4 Satisfaction and star ratings

- **Prototype: each need unit is the unit of evaluation**, not the visual guest character. Measure **queue waiting from need arrival to taking a seat**. Do not include occupied-seat service time in the Prototype calculation without explicit approval (requirements F44 conflicts; see §3).
- Each unit scores 100% through `patience_grace`, decreases toward `unit_floor` over the later threshold; compute weighted wave average, apply `wave_floor` (all TUNING). Ocean View contributes greater weight than ordinary needs. Default examples: grace 10 s, decay to unit floor 25% at 30 s, wave floor 40%.
- Star examples for tuning: 1★ ≥40%, 2★ ≥55%, 3★ ≥70%, 4★ ≥80%, 5★ ≥90%, Perfect 100%. Never accidentally require Energy for 5★ / Perfect Stay. Prevent an inactive player from getting an undeserved 3★ merely through a high score floor.
- Numeric **Wave Satisfaction is hidden during live waves**. Display it on S4 after completion, with positive / negative review breakdown explaining cause; recommendation should link to the longest-lived bottleneck, not random building. Unsatisfied guests may visually leave **after** wave results; this does not reduce subsequent demand or add a second penalty.
- Avoid conflating satisfaction, star rating, resource reward, Resource Value, and Reputation; each has its own calculation and owner.

## 6. Workers, Managers, Specialists, and upgrades

### Workers — Prototype

- Anonymous, interchangeable pool: **2 assigned Workers / 3 owned buildings at start**. Every wave building has exactly **one slot** through Lv3. Every Worker is on a building; no unassigned bench; exactly one owned building has no Worker. With Pool: 3/4. With Bungalows: 4/5.
- Rearrange **free, immediately** before waves and in active arrival/drain states. Preserve positions between waves. A move chooses a **source staffed building** and the **one vacant destination**; do not offer meaningless staff-for-staff swaps. Source slows and destination speeds **immediately**, including in-flight service.
- A building without a Worker still operates slowly; ensure the visual conveys “unoccupied” rather than “broken”. Do **not** display named Chef/Bartender/etc. figures or named rated staff cards in Prototype.
- A Worker move is a *zero-sum decision*: feedback shows both the helped building and the now-vacant source, letting the player distinguish insufficient capacity (fix through upgrade between waves) from missing worker (fix through reassignment now).

### MVP staffing

- Workers can be hired between waves for Coins into a free Worker slot. One Worker is still bundled with each newly purchased **ordinary wave building**; no Worker is added by Spa. Additional workers have diminishing utilization return in buildings with multiple slots; **never** allow the pool to fill all available slots. `floor(worker_cap_ratio × total_wave_building_slots)` with example ratio 0.80 is TUNING.
- Managers: hired for Coins, one relevant manager for each ordinary wave building (Receptionist, Chef, Bartender, Lifeguard, Bellhop); add a building speed multiplier and serve as a VIP roster prerequisite, **not** a basic service on/off switch. No rarity/collectible manager system in MVP.
- Specialists: **Butler, Captain**; hired for Coins; **no ordinary-wave building assignment** and no wave-speed effect; they only satisfy VIP roster prerequisites. “Staff” is the umbrella term for Workers/Managers/Specialists, **not** the old name for the manager layer.
- Lv1–Lv3 prototype example capacities 3/5/7, one Worker slot, capacity only. MVP Lv4: adds second Worker slot and base quality tier (capacity example remains 7). Lv5: third Worker slot, additional quality tier and capacity example 10. **Exact prices and capacity values are TUNING**, not fixed constants. Lv4–5 require Coins + Materials. Never let a generic regular-building upgrade rule automatically modify Spa.
