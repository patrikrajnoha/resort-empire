# Resort Empire Team Skill — installation and usage

**Package version:** 2.1, 23 October 2026. This is a documentation/agent-instruction package; **it does not change your Unity project automatically**.

## Folder contents

- `resort-empire-team-development/SKILL.md`: main, relatively compact skill with triggers, game identity and non-negotiable rules.
- `resort-empire-team-development/references/`: eight topic-specific guides; AI opens these **only for relevant tasks**.
- `resort-empire-team-development/sources/`: original supplied source documents as offline snapshots (including the Requirements PDF and dated Jira backlog), renamed for stable paths.

There is **one and only one `SKILL.md`** in the distributable skill directory. The legacy UI instructions were preserved as a differently named source document to avoid confusion.

**Unity UI creation rule (v2.1):** all Canvas text must be `Text (TMP)` (`TextMeshProUGUI`, **UI → Text - TextMeshPro**); each Canvas sprite graphic must be **UI → Image**, with the sprite in `Source Image`. SpriteRenderers remain appropriate for in-world island/building/guest art. The authoritative details are in `references/unity-ui.md`.

## Install as a repository skill — Codex

1. Unzip the archive.
2. Copy the `resort-empire-team-development` directory into your Unity repository at `.agents/skills/resort-empire-team-development/` (do not copy only `SKILL.md`: the references and sources are necessary).
3. In a Codex session, use the skill by name when appropriate, e.g. `$resort-empire-team-development`, or ask to use the Resort Empire development skill.
4. Add the optional root `AGENTS.md` contents from the separate `AGENTS.template.md` file **after checking your current AGENTS.md**; never overwrite existing team instructions.
5. Check that the agent can open `references/unity-ui.md` and `sources/build-document-prototype-mvp-v4.md` from the skill directory.

## Install in Claude Code

1. Copy the skill directory to `.claude/skills/resort-empire-team-development/` in the Unity repository (or `~/.claude/skills/` for personal use).
2. Confirm the skill appears/discovers in your Claude Code environment; invoke by name where available.
3. Keep the source documents next to `SKILL.md`; do not silently assume a chat attachment exists on another developer's machine.

## Other agents / ChatGPT

Install paths and automatic skill invocation vary by platform. Uploading only the Markdown text into an ordinary chat does not make it an always-active repository skill. If the tool does not support skills, attach the package/docs or explicitly ask the agent to read the main skill and relevant references before work.

## Updating the skill

- Treat `sources/` as an **October 2026 reference snapshot**, not a live Jira mirror.
- After decisions change, update the authoritative project documents and then affected topic reference + main invariant; note review date. Avoid synchronizing mutable sprint assignments into hard-coded architecture.
- Keep sensitive secrets, Firebase credentials and proprietary/unlicensed assets out of public repositories.
- Do not put multiple uncoordinated copies of this skill under several discovery paths in the same tool if that would cause duplicate skill registration.

## Distribution and checks

This package was generated from the files provided in the conversation. The actual Unity repository, current Jira state, and live Figma nodes **have not been inspected or modified**. Developers should verify real paths, current owners and acceptance criteria before any code change.

**Official installation references:**
- Codex: https://learn.chatgpt.com/docs/build-skills
- Codex customization: https://learn.chatgpt.com/docs/customization/overview
- Claude: https://platform.claude.com/docs/en/agents-and-tools/agent-skills/overview
