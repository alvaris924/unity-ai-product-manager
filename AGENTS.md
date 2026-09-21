# AGENTS.md

Binding rules for every AI coding assistant that works in this repository: Claude Code, OpenAI Codex and ChatGPT agents, GitHub Copilot, Cursor, Gemini CLI or anything else. `CLAUDE.md` and `.github/copilot-instructions.md` defer to this file. If this text was pasted into a chat, treat it as binding for anything produced for this repository.

## The project in one paragraph

AI Product Manager is an open-source Unity Editor tool shipped as the UPM package `com.alvaris.ai-product-manager`. A tester right-clicks a UI element in the Game view (Play mode) or the Scene view / Hierarchy (Edit mode), picks what is wrong, and gets a Markdown report with a screenshot, hierarchy, component, UnityEvent and pointer diagnostics, written so an AI coding agent such as Claude Code can start fixing from it. The package sits at the repository root. Assemblies: `Alvaris.AiProductManager` (Runtime: the Game view overlay, the picker, the report builder and writer) and `Alvaris.AiProductManager.Editor` (settings, Scene view hook, Report and Inbox windows, menu, Claude skill installer). `ClaudeCode~/` holds the `/pm` skill the Editor installs into a host project. Unity floor 6000.0, `com.unity.ugui` is the only dependency. License MIT.

## Workflow, non-negotiable

1. **Ask before every git action.** Never commit, push, tag, merge or open a pull request without the user's explicit go-ahead in the current conversation turn. Approval given earlier does not carry over.
2. **Never touch `main` directly.** No direct pushes, no force-pushes, no rewriting of pushed history. Do not look for a way around the protection.
3. **One route for every change.** Update `main`, cut a branch named `type/topic` (`feat/`, `fix/`, `docs/`, `chore/`, `ci/`, `refactor/`, `perf/`, `test/`), commit, push the branch, open a pull request against `main` using `.github/PULL_REQUEST_TEMPLATE.md`, wait for the `PR title` check, report the PR URL. Squash merge only, and only with green checks.
4. **Conventional Commits everywhere.** Commit messages and PR titles are `type(scope): subject`. Types: feat, fix, docs, chore, ci, refactor, perf, test, build, revert. Scopes: core, overlay, picker, report, editor, inbox, settings, skill, docs, ci, release, deps. Subject in lowercase imperative, no trailing period, header under 72 characters. Breaking changes use `type(scope)!:` plus a `BREAKING CHANGE:` footer.
5. **Local hooks stay on.** Once per clone run `git config core.hooksPath .githooks`. The hooks reject non-conforming commit messages and pushes to `main`. Never use `--no-verify`.
6. **Changelog and docs move with the code.** Every user-visible change adds a line to `CHANGELOG.md` under `[Unreleased]`; otherwise the PR gets the `skip-changelog` label. `README.md` changes whenever behaviour or the report format changes, and `ClaudeCode~/skills/pm/SKILL.md` changes whenever the report layout or the folder conventions change: that file is the contract between the Editor and the agent.
7. **Issues first.** Features and bugs start as issues with one `area:` and one `priority:` label. PRs reference them with `Closes #N`. Small chores may skip the issue.
8. **Releases are deliberate.** A PR titled `chore(release): vX.Y.Z` bumps `package.json` and dates the changelog section. After it merges, `vX.Y.Z` is tagged on `main`. Nothing else is ever tagged.

## Engineering rules

- Only `com.unity.ugui` and built-in modules may appear in `package.json`. TextMeshPro text is read by reflection on purpose; never add a TMP, Input System or render-pipeline reference.
- Public Unity APIs in the default path. The two reflective reads (a UnityEvent's runtime listener list, a URP camera's render type) degrade to "unknown" when the member is missing; keep it that way. No reflection into `UnityEditor` internals.
- Unity floor 6000.0. Anything newer is version-guarded (`#if UNITY_6000_1_OR_NEWER`).
- The overlay is runtime IMGUI so it works with either input backend and needs no scene setup. The Editor installs it on `EnteredPlayMode` and removes it on exit; nothing is added to scenes or builds. Event subscriptions must be idempotent because Enter Play Mode may skip the domain reload, and singletons must re-register in `OnEnable` because a mid-Play recompile keeps the object but not the static.
- Reports are plain files: `report.md`, `screenshot.png`, `summary.txt` under `<reports>/Inbox/NNNN-<category>-<target>/`, moved to `Resolved/` when done. Any agent that can read files and images must be able to work from them; never make the Editor the only reader.
- Editor-only assemblies use `[InitializeOnLoad]` and `ScriptableSingleton` as the sanctioned static entry points; avoid other static state.
- Style per `.editorconfig`: Allman braces, four spaces, LF, UTF-8 without BOM, camelCase private fields, C# 9 as Unity compiles it (no records, no init-only setters).
- Clean room: never read, decompile or copy code from proprietary plugins, and never reproduce their UI text, icons or docs. Features come from public Unity APIs and this project's own design. If such a plugin is present in the host project, do not open its files while working on this package.

## Definition of done for a pull request

Checks green, the change verified in a Unity project (version noted in the PR), changelog line present or `skip-changelog` applied, docs and the skill file updated, self-review done, every item of the PR template checklist ticked.

## Commands

```
git config core.hooksPath .githooks                 # once per clone
gh pr create --base main --title "type(scope): subject" --body-file .github/PULL_REQUEST_TEMPLATE.md
```

In a host project, `Window > AI Product Manager > Automation` holds menu items that open the popup, file a report and capture the Game view without a mouse, for scripted checks (for example through Unity MCP).
