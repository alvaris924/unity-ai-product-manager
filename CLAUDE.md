# CLAUDE.md

The binding rules for this repository live in `AGENTS.md`, imported below. They apply to every AI assistant; this file only adds Claude Code specifics.

@AGENTS.md

## Claude Code specifics

- Use `gh` for issues and pull requests, and open PRs with the template as the body.
- Before any git action, state the branch name, the Conventional Commit title and the files touched, then wait for the go-ahead in that same turn.
- If a request would touch `main` directly, decline and offer the branch-and-PR route.
- When working on this repository from another project directory (for example a game project that references it as a local package), read `AGENTS.md` here first; the rules do not depend on where the session started.
- The `/pm` skill under `ClaudeCode~/skills/pm/` is what a host project's Claude Code sees after `Window > AI Product Manager > Install Claude Code skill`; keep it in step with the report format.
