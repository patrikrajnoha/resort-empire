# Git collaboration and Jira workflow

> **Reference status:** This file is an operational extraction and clarification of the original team skill, verified against the bundled project specifications where noted. The authoritative exact wording and values remain in `../sources/`. This file does not assert that the codebase has any particular classes or folder paths.

## 12. Git and shared-team collaboration — safety-critical

**Before edits:** identify worktree/branch and the current uncommitted changes; run `git status --short` (read-only) and inspect relevant diffs. Never discard or stash a teammate's changes silently. Determine the team's actual branch strategy and whether a merge/rebase/pull is safe. The old UI instruction says “always pull latest”; in a dirty worktree, blind `git pull` may damage work. **Update safely only when conditions allow**, with explicit confirmation for a risky merge/rebase or potential conflict.

**While editing:** stay within task ownership and Jira AC; no drive-by formatting, reimporting asset trees, changing unrelated project settings, mass renames, or unexpected plugin/package installs. Treat Unity `.meta` GUID stability as critical; do not regenerate GUIDs, move/delete referenced assets casually or modify another person's prefab/scene behind their back. Coordinate shared-scene/prefab changes and keep change sets narrow. Avoid hardcoded paths and avoid committing build artifacts, caches or secrets.

**Finishing:** inspect `git diff` for all touched files and look for accidental Unity serialization changes. **Do not `commit`, `push`, force-push, reset, clean, checkout/restore destructive changes, rewrite history, or delete branches without explicit instruction.** Requested commit/push should use agreed branch flow and a clear message only after validation. Never claim merged/committed/synced changes without tool evidence.

## 13. Jira, Scrum, roles and changing project state

The dated consolidated backlog (**14 Oct 2026**) covers **eight historically drafted Epics, 56 Issues**. This is a snapshot of drafted issues, not a live Jira export. **On 23 Oct 2026 the team discussed removing `SCRUM-21` as an Epic while keeping its six underlying Tasks; actual deletion is NOT verified.** The group subsequently began planning UI subtasks under `SCRUM-24` and discussed `SCRUM-29` as the building-data task. **Never infer current issue topology or Sprint assignments from this archived table**:

| Epic | Responsibility |
|---|---|
| `SCRUM-14` | Build and expand the resort |
| `SCRUM-15` | Run guest waves and manage services |
| `SCRUM-16` | Manage resort staff |
| `SCRUM-17` | Manage economy and resources |
| `SCRUM-18` | Evaluate guest satisfaction and ratings |
| `SCRUM-19` | Manage VIP guest visits |
| `SCRUM-20` | Save and restore game progress |
| `SCRUM-21` | Set up and validate the game / infrastructure and testing |

An **Epic** groups a major objective, a **Story** states player-facing value, a **Task** is deliverable implementation work, a **Subtask** splits an issue without creating duplicate scope, a **Sprint** is the timebox. **Acceptance Criteria (AC)** are what must be proven; **Definition of Done** includes integration, testing and reporting. Some infrastructure issues may not have an Epic; check live Jira.

**When given a Jira key:** read the **current** Description, AC, Phase, Dependencies, linked tasks and status from accessible Jira or supplied text. If Jira is inaccessible, say so and operate on the provided snapshot; never claim current status. Before adding subtasks, confirm that the issue's scope is divisible and ask/request creation only when authorized. Trace each implemented behavior to AC and affected subsystem, record blocked/unmet AC. Do not mark an issue done merely because code compiles.

**Historical UI allocation, reference only:** Dominik: S1/S2/S5/S7/S9; Natalia: S3/S4/S6/S8. Ownership, branch state, Sprint membership and deadline may have changed; verify before editing shared views. Respect whoever currently owns a feature, irrespective of this snapshot.
