# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Game view popup: in Play mode, right-click any UI element and pick what is wrong from a list of categories. The popup names the control the EventSystem would deliver the click to, outlines it, and offers Parent / Next under / Reset to adjust the target. The EventSystem is paused while the popup is open so the click never reaches the game. The trigger button and modifiers, the categories and the popup scale are settings.
- Edit mode entry points: Ctrl+right-click in the Scene view shows the same categories as a menu for the picked object, the exact graphic under the pointer or any of its parents; the Hierarchy context menu and Window > AI Product Manager > Report selected object... open a window with a notes field.
- Reports: each one is a folder `Inbox/NNNN-<category>-<target>/` with `report.md`, `screenshot.png` (Game view with the target outlined in red; rendered from the scene cameras in Edit mode) and `summary.txt`. The report carries the target's hierarchy path, RectTransform, every component with the properties that matter for UI bugs (Selectable state, UnityEvent persistent and runtime listener counts, raycastTarget, CanvasGroup, Canvas and raycaster setup, layout groups), the ancestors, a Findings list of likely causes, the EventSystem hit stack and the graphics under the pointer, recent console output with stack traces, the prefab, the project scripts involved with their serialized field values and the files to look at.
- Inbox window (Window > AI Product Manager > Inbox): pending and resolved reports, copy the summary again or every pending summary at once, open the report or the screenshot, resolve, reopen or delete.
- Project Settings page (AI Product Manager): categories, trigger buttons, reports folder, clipboard content, console window, screenshot and serialized-field switches.
- Claude Code integration: the summary is copied to the clipboard when a report is filed, and Window > AI Product Manager > Install Claude Code skill writes a `/pm` skill into the host project that reads reports, fixes the cause, moves them to `Resolved/`, and can watch the inbox.
- Automation menu (Window > AI Product Manager > Automation) for scripted checks without a mouse: open the popup on the selected object, file a report, capture the Game view with the popup visible.
- Scripted playtests: taps, long presses, drags and joystick pushes delivered as synthetic fingers through the uGUI EventSystem (several at once, on unscaled time); controls resolved by name, hierarchy path or visible text, with a ⚠ when a gesture reaches another control than the one it aimed at; Game view screenshots and MP4 recording through the Editor's built-in MediaEncoder; a `Playtests/NNNN-<title>/` folder with `playtest.md` (every step and the console errors between steps), the screenshots and the video. C# API `Playtest`, and Unity CLI commands `pm_session`, `pm_ui`, `pm_tap`, `pm_drag`, `pm_push`, `pm_shot`, `pm_record` and `pm_note` when `com.unity.pipeline` is installed. The `/pm playtest` skill routine drives it from Claude Code.
- Playtests window (Window > AI Product Manager > Playtests): every session with its steps and outcomes, the console errors between them, screenshot thumbnails and the video beside the steps (a step's time seeks to that moment), the session in progress updating live, and Copy for Claude / Open report / Reveal / Delete. The video plays through the built-in Video module when it is enabled.
- Repository workflow: contribution guide, `AGENTS.md` rules for AI assistants, local git hooks, pull request and issue templates, Conventional Commit title check, Dependabot for GitHub Actions.

[Unreleased]: https://github.com/alvaris924/unity-ai-product-manager/commits/main
