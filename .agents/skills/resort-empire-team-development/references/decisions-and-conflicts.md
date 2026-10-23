# Source precedence, discrepancies, open decisions

> **Reference status:** This file is an operational extraction and clarification of the original team skill, verified against the bundled project specifications where noted. The authoritative exact wording and values remain in `../sources/`. This file does not assert that the codebase has any particular classes or folder paths.

## 3. Sources, precedence, and uncertainty

Before modifying mechanics or UI, open the **actual repository copies** of the following sources if available. These are reference names, not evidence that files exist at these paths:

| Source | Purpose / authority |
|---|---|
| `build-document-prototype-mvp.md` — **Version IV** | Primary phase split, game-mechanics source of truth; §4 Prototype and §5 MVP. Versions II/III are historical only. |
| `Resort_Empire_Funkcne_a_Nefunkcne_Poziadavky (1)(1).pdf` or latest approved requirements PDF | F01–F55 / N01–N26 compliance; a provisional phase-agnostic proposal, some conflicts exist. |
| `ui-screens-handoff.md` | S1–S9 behavior, surfaces, state-dependent interactions, visual intent. |
| `game-design-document.md` | Game identity, pillars, non-goals, terminology. |
| `Resort_Empire_Complete_Jira_Backlog.md` / DOCX | Consolidated issue descriptions, dependencies and AC; **14 Oct 2026 working synthesis, not a Jira export**. Query Jira for live issue details/status. |
| Figma: `https://www.figma.com/design/whlebVoyEKoW4emX7kdR2B/Resort-Empire?node-id=0-1&p=f` | Visual layouts and sprites only, when accessible; never override current gameplay rules. |
| Team UI skill / source prefabs | Existing hierarchy, fonts, naming and UI delivery constraints. |
| Decision Log, Energy System Spec, Project rules, First Session Walkthrough, `wave_sim.py` | Authoritative *when actually available* for decisions, boost details, playthrough, balancing verification. Do not pretend to have read them. |

**Precedence:** (1) current explicit task instruction *within approved scope* → (2) current formally approved team decisions → (3) Build Document IV for phase/gameplay → (4) approved requirements with conflicts called out → (5) UI handoff for UX flows → (6) GDD → (7) live Jira AC for issue scope → (8) Figma for visual design → (9) historical UI skill and generic Unity references. Repository reality is authoritative about what code currently exists, not about whether implemented behavior is correct.

**Conflict procedure:** quote the competing sections/IDs; classify as `BLOCKING IMPLEMENTATION`, `MVP DECISION`, `TUNING`, or `EDITORIAL`; adopt a safe current reference only when explicit precedence resolves it; otherwise isolate the parameter/feature behind configuration and request a team decision. Never silently change a gameplay rule because a mockup, Jira draft, or old code says something different. Record final decisions in the team's Decision Log if instructed and allowed.

**Known issues to flag (not to silently “fix”):**
1. **Satisfaction measurement:** Build IV §4.4 counts **queue waiting time until a service seat is assigned**, *not service duration*; requirements **F44** proposes arrival-to-completion time including service. This changes the simulation/rating formula. For Prototype follow Build IV, mark F44 unresolved pending explicit alignment.
2. **Phase scope:** requirements PDF covers Lv5, VIP, Managers, Firebase and Gems without a phase split; Build IV defers these to MVP. Do not implement them in Prototype solely to satisfy a phase-agnostic F-ID.
3. **VIP rating thresholds:** old UI handoff S7 thresholds `3★ 50 / 4★ 70 / 5★ 90 / Perfect 100` differ from newer wave thresholds `1★ 40 / 2★ 55 / 3★ 70 / 4★ 80 / 5★ 90 / Perfect 100` (TUNING). MVP thresholds must be resolved; do not hardcode old Figma values.
4. **Energy Mode B:** VIP `READY` preparation/quality compensation is described, but *Mode B for regular waves* remains an MVP open decision; do not infer it from old named-staff UI. The prototype has only Mode A speed boost.
5. **Spa progression:** VIP-only Spa has no Worker slot or ordinary need; its upgrade levels/effects remain an MVP decision. Do not copy the regular building Lv1–5 ladder onto Spa without approval.
6. **UI handoff editorial wording:** its closing §G calls S3 shared, while its detailed S3/S7 descriptions and Build IV unequivocally reserve **S3 for regular-wave Worker Allocation, not VIP**. Reuse shared styling where useful, never share the gameplay action.
7. **Figma drift:** older S3 named staff, older S2 live numerical satisfaction, older S5 quality upgrades, energy cap displays, and outdated VIP stars are **not** implementation authority.
8. **Not established by inspected sources:** repository structure/Unity version, current Jira assignments, live Figma nodes, approved Firebase service choices, exact Android target devices, and actual `wave_sim.py` availability. Verify before making concrete claims.


## October 23, 2026 packaging corrections and status clarifications

- `SCRUM-21` was included in an **October 14 historical backlog draft**. The team discussed deleting that Epic while retaining its Tasks. **Do not say the deletion happened** without inspecting live Jira.
- Jira issue titles and assignments have changed during Sprint 1. The snapshot uses drafted labels, not necessarily the actual current Jira descriptions.
- Current team UI work discussed two subtasks beneath `SCRUM-24` (Dominik S1/S2/S5/S7/S9, Natalia S3/S4/S6/S8), but actual Jira creation and status are unverified; use current Jira and repository to establish ownership.
- `sources/` contains **local October 2026 copies** of supporting documents. They are included so that the skill is usable when live links are unavailable, but a newer approved repo revision takes precedence under the explicit source rules. If the user requests exact historical text, use those copies without silently harmonizing content.
- The source PDF's **F44 conflicts with Build IV**; do not change the PDF or present the discrepancy as resolved. The formal decision remains with the team.
- MVP open items in Build IV include VIP rating thresholds, Spa upgrade progression, and Mode B behavior during ordinary waves. Firebase service choice, profile recovery and cloud-save conflict policy are also unresolved or implementation dependent; record before building.
- The advertised build/quality targets (30 FPS, start ≈10 s, package ≈150 MB, response ≤200 ms, eight-wave stability) are **acceptance targets rather than measured achievements**; reference-device conditions must be agreed.
