# AI Product Manager for Unity

Point at a UI element in the Unity Editor, say what is wrong, and hand the result to an AI coding agent.

In **Play mode** you right-click the element in the Game view and pick a category ("Button doesn't work", "Wrong
text or value", ...). In **Edit mode** you Ctrl+right-click it in the Scene view, or right-click it in the Hierarchy
and choose *AI Product Manager → Report issue...*. Either way you get a folder with:

- `report.md` — the target's hierarchy path, RectTransform, every component with the properties that matter
  (interactable, `onClick` persistent + runtime listener counts, raycastTarget, CanvasGroup alpha/blocksRaycasts,
  Canvas + raycaster setup, layout groups...), its ancestors, a **Findings** list of likely causes, the EventSystem
  raycast stack at the click ("the pointer is blocked by ..."), the graphics under the pointer in visual order,
  recent console output with stack traces, the prefab, the project scripts involved with their serialized field
  values, and the files to look at;
- `screenshot.png` — the Game view with the target outlined in red;
- `summary.txt` — a few lines with the report path, copied to the clipboard so you can paste it into
  [Claude Code](https://claude.com/claude-code) (or any agent that can read files).

Reports go to `<project>/AIProductManager/Inbox/NNNN-<category>-<target>/` and move to `Resolved/` when fixed.

> **Status: pre-release.** Everything below works on Unity 6000.6 in a real project; `v0.1.0` is the first tag once the last rough edges are filed as issues.

## Requirements

Unity 6000.0 or newer with uGUI 2.x (`com.unity.ugui`). No other dependencies: TextMeshPro text is read by reflection, and the popup is runtime IMGUI, so it works with the Input Manager and the Input System package alike.

## Install

Add the package through **Window → Package Manager → + → Add package from git URL**:

```
https://github.com/alvaris924/unity-ai-product-manager.git
```

or in `Packages/manifest.json` (pin a tag once one exists, for example `#v0.1.0`):

```json
"com.alvaris.ai-product-manager": "https://github.com/alvaris924/unity-ai-product-manager.git"
```

To hack on it, clone the repo next to your project and use a local reference instead:
`"com.alvaris.ai-product-manager": "file:../../unity-ai-product-manager"`.

## Use

**Play mode** — right-click any UI element in the Game view. A popup shows the element it picked (the control the
EventSystem would deliver the click to), with *Parent* / *Next under* / *Reset* to adjust, and one button per
category. Click a category: the screenshot is taken, the report is written and the summary is copied. *Add notes...*
opens an Editor window instead, where you can type what you expected. While the popup is open the EventSystem is
paused so the click does not also reach the game.

**Edit mode** — Ctrl+right-click in the Scene view (the button and modifiers are configurable) shows the same
categories as a menu, for the picked object, the exact graphic under the pointer, or any of its parents. The
Hierarchy context menu and *Window → AI Product Manager → Report selected object...* open the notes window. The
Edit-mode screenshot is rendered from the scene cameras, so Screen Space - Overlay canvases are not in it.

**Inbox** — *Window → AI Product Manager → Inbox* lists pending and resolved reports: copy the summary again, open
the report or the screenshot, resolve, reopen or delete. *Copy all pending for Claude* puts every pending summary on
the clipboard at once.

**Settings** — *Edit → Project Settings → AI Product Manager*: categories, trigger buttons, reports folder,
what goes on the clipboard, console window, popup scale, screenshot on/off.

## Working with Claude Code

1. *Window → AI Product Manager → Install Claude Code skill (.claude/skills/pm)* writes a `/pm` skill into the
   project. It tells Claude where the reports are, how to read them, how to verify a fix and to move the folder to
   `Resolved/` when done.
2. File a report, then paste the clipboard into Claude Code — or type `/pm` to take the oldest pending one,
   `/pm 12` for a specific number, `/pm list` to see what is waiting.
3. `/pm watch` makes Claude poll the inbox so every report you file in Unity is picked up without pasting anything.

The report format is plain Markdown, so it works with any agent that can read files and images.

## What the report is good at

The Findings section is written for "the button does nothing" and its relatives. It checks, among other things:
inactive objects, disabled or non-interactable Selectables, CanvasGroups that block or hide a subtree, missing
raycast targets, missing GraphicRaycaster / EventSystem / canvas camera, zero scales, off-screen targets, events
with no listeners, another graphic sitting on top of the target, and recent exceptions.

## Notes and limits

- Editor-only by design: the overlay is installed when Play mode starts and removed when it ends. Nothing is added
  to scenes or builds.
- The Game view popup is runtime IMGUI, so it works with both the Input Manager and the Input System package.
- Screen Space - Overlay canvases are missing from Edit-mode screenshots (Play-mode screenshots include everything).
- TextMeshPro text is read through reflection, so the package does not depend on TMP.
- Runtime listener counts of UnityEvents come from a private field; if a Unity release renames it the report says
  `? runtime` instead of a number.

## Contributing

Issues and pull requests are welcome. The workflow (branches, Conventional Commits, PR template, changelog, releases) is in [CONTRIBUTING.md](CONTRIBUTING.md); AI assistants follow [AGENTS.md](AGENTS.md).

## License

MIT — see [LICENSE](LICENSE).
