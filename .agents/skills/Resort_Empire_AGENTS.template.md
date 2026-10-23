# Resort Empire — optional repository AGENTS.md addition

> Template, **not an instruction to overwrite an existing AGENTS.md**. Merge with your team's current repository rules after review.

When modifying the Resort Empire Unity/C# project, use the repository skill `resort-empire-team-development` at `.agents/skills/resort-empire-team-development/SKILL.md` (or the team-agreed agent-specific installation location). Read the relevant `references/` and exact approved `sources/` sections for the task. The Build Document Version IV governs Prototype mechanics unless a newer explicit team decision supersedes it.

Check the current Jira issue and acceptance criteria, the actual repository state, `git status`, relevant Unity scenes, scripts, assets and Prefabs **before editing**. Protect uncommitted teammate changes and Unity `.meta` GUIDs; do not commit/push, reset/clean, alter unrelated project files or amend approved gameplay rules without explicit user authorization. Do not silently implement MVP mechanics in Prototype. Use one authoritative gameplay/economy state; UI must not duplicate underlying logic.

All new Canvas UI texts must be `Text (TMP)` (`TextMeshProUGUI` via `UI → Text - TextMeshPro`) and all individual Canvas UI sprites must be added via `UI → Image` and its `Source Image` field. Do not confuse these with world-space `SpriteRenderer` objects.

Report which files changed, what was actually tested and which acceptance criteria are incomplete. If Unity Editor, Android, Figma, Firebase, or Jira is inaccessible, state that honestly.
