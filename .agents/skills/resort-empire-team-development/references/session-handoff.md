# Session handoff and final report

> **Reference status:** This file is an operational extraction and clarification of the original team skill, verified against the bundled project specifications where noted. The authoritative exact wording and values remain in `../sources/`. This file does not assert that the codebase has any particular classes or folder paths.

## 17. Explicit session handoff (optional)

**Trigger only** when developer asks for “session handoff”, “handoff summary”, “wrap up session”, “summarize before clearing context”, or a clear equivalent. This reuses the supplied session-handoff convention, but **normal development answers remain in the developer's language**. Output handoff **in English only**, **in chat**, do not write a file or change memory unless separately authorized. Review the entire current session and known touched files; do not invent or broad-scan the filesystem just to populate a template. If a section has no information, write `none`.

Use this exact handoff outline:

```markdown
# Session Handoff — <brief feature or work title>

## Conversation language
<developer's language>. Reply to user in this language; this handoff is in English for cross-agent precision.

## Where it started
<original task, constraints, key context in 2–3 sentences>

## Decisions locked + what shipped
- <decision/change, reason, absolute path when known>

## Key files for next session
- `<absolute path>` — <purpose>; include plan/memory files touched if applicable

## Running state
- Background processes: <IDs and stop commands, or none>
- Dev servers / ports: <details or none>
- Open worktrees / branches: <details or none>

## Verification — how to confirm things still work
- `<command or manual check>` — <actual result or expected outcome, distinguished>

## Deferred + open questions
- Deferred: <work and reason, or none>
- Open: <decision/blocker and context, or none>

## Pick up here
<one or two sentences stating the single most useful next action>
```

For handoff files, use absolute paths known in the running environment; if unknown, say `path not confirmed`. Keep operational state (exact tests, errors, shell IDs and open decisions), not a congratulatory retrospective or speculation. The user may use the handoff to start a fresh agent.

## 18. Standard completion report

Every substantial task should end with a compact, evidence-based report (in the developer's language):

```text
Task / Jira key: ...  |  Phase: Prototype / MVP
Implemented: ...
Changed files: ... (exact paths if known)
Important decisions: ... (or none)
Verification actually performed: ... (pass/fail + command/scene/device)
Unverified: ... (Unity Editor / Android / Figma / Jira if inaccessible)
Known limitations / unmet acceptance criteria: ...
Integration with other owners: ...
Documentation conflicts or approvals needed: ...
Git: no commit/push unless explicitly authorized
```

**Success means:** the team understands what changed, why it fits Resort Empire's mechanics and phase, what remains for integration, and whether it actually works. Never turn guessed status into a fact.
