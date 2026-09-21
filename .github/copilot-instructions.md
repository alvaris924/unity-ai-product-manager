# Copilot instructions

Follow `AGENTS.md` at the repository root. It is the binding workflow and engineering contract for every AI assistant.

The essentials, repeated because Copilot does not import files:

- No commits, pushes, tags, merges or pull requests without the user's explicit go-ahead in the current turn. Never push to `main`; never force-push.
- Branch `type/topic` from an up-to-date `main`. Commit messages and PR titles are Conventional Commits, `type(scope): subject`. Squash merge only, after the `PR title` check passes.
- `com.unity.ugui` is the only dependency, public Unity APIs only, Unity floor 6000.0.
- A `CHANGELOG.md` line under `[Unreleased]` for every user-visible change; `ClaudeCode~/skills/pm/SKILL.md` changes with the report format.
- Clean room: never read, decompile or copy code from proprietary plugins; build features from public Unity APIs only.
