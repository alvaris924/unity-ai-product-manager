# Contributing to AI Product Manager

This document is the workflow contract for every change, whether written by hand or with an AI assistant.

## First-time setup

```
git config core.hooksPath .githooks
```

The hooks reject commit messages that are not Conventional Commits and refuse direct pushes to `main`. They are a courtesy check; the same rules are enforced on GitHub.

## Branching model

- `main` is always releasable and is protected: pull requests only, linear history, required checks, every review conversation resolved.
- Every change lives on a short-lived branch cut from an up-to-date `main`:
  `feat/<topic>`, `fix/<topic>`, `docs/<topic>`, `chore/<topic>`, `ci/<topic>`, `refactor/<topic>`, `perf/<topic>`, `test/<topic>`.
- One branch per issue. Branches are deleted automatically on merge.

## Issues first

- Features and bugs start as an issue using the templates. Small chores may skip the issue.
- Labels: one `area: *` (`overlay`, `report`, `editor`, `inbox`, `skill`, `docs`, `ci`), one `priority: *`, plus the kind (`bug`, `enhancement`, `documentation`, `chore`).

## Commits

- [Conventional Commits](https://www.conventionalcommits.org/): `type(scope): subject`.
  - Types: `feat`, `fix`, `docs`, `chore`, `ci`, `refactor`, `perf`, `test`, `build`, `revert`.
  - Scopes: `core`, `overlay`, `picker`, `report`, `editor`, `inbox`, `settings`, `skill`, `docs`, `ci`, `release`, `deps`.
- Subject in the imperative mood, lowercase first letter, no trailing period, at most 72 characters. The body explains why, not what.
- Breaking changes: `feat(report)!: ...` plus a `BREAKING CHANGE:` footer. Changing the report layout or folder conventions in a way that breaks the `/pm` skill counts.

## Pull requests

- Target `main` and fill in the template. Keep it a draft while work is in progress.
- The PR title must itself be a valid Conventional Commit; the `PR title` check enforces it and the squash merge uses it as the commit subject with the PR body as the message.
- Link the issue with `Closes #123`.
- Before requesting review: self-review the diff, verify the change in a Unity project and say which version, add a `CHANGELOG.md` line under `[Unreleased]` (or apply `skip-changelog`), and update `README.md` and `ClaudeCode~/skills/pm/SKILL.md` if behaviour or the report format changed.
- Squash merge only. Never merge with a failing check.

## Changelog

[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Every user-visible change adds a line under `[Unreleased]` in one of Added, Changed, Deprecated, Removed, Fixed, Security.

## Releases

- [Semantic Versioning](https://semver.org/). `MAJOR` for breaking changes to the report format, the folder conventions or the settings file, `MINOR` for features, `PATCH` for fixes.
- A release PR titled `chore(release): vX.Y.Z` bumps `package.json`, turns `[Unreleased]` into a dated section and updates the comparison links.
- After it merges, tag `vX.Y.Z` on `main`. The release workflow checks that the tag matches `package.json` and publishes a GitHub Release with that changelog section.

## Code standards

- Unity floor is the `unity` field in `package.json` (`6000.0`). Newer APIs go behind version defines.
- `com.unity.ugui` is the only dependency. TextMeshPro is read by reflection; the Input System and render pipelines are never referenced.
- Public Unity APIs only; the two reflective reads (UnityEvent runtime listeners, URP camera render type) must degrade gracefully.
- Formatting follows `.editorconfig`: Allman braces, four spaces, LF, UTF-8 without BOM.

## Clean-room policy

Contributors must not read, decompile or copy code from proprietary or otherwise closed-source plugins, and must not reproduce their UI text, icons or documentation. Every feature is designed from public Unity APIs, Unity documentation and this project's own design notes. Feature parity with other tools is fine; copied expression is not.

## Working with AI assistants

The same rules apply. Assistants never push to `main`, never force-push, and open pull requests like any other contributor. The binding instructions for every assistant are in `AGENTS.md`; `CLAUDE.md` and `.github/copilot-instructions.md` defer to it.
