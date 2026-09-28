---
name: pm
description: Work through UI issue reports filed from the Unity Editor by AI Product Manager (the user right-clicked a UI element and picked "Button doesn't work", "Wrong text", ...), and run scripted playtests in the Editor that tap, drag and push joysticks by name and report back with screenshots and a video. Use when the user pastes an "AI Product Manager report" summary, mentions the PM inbox, asks to fix, list or watch reported UI issues, or asks you to play or check something in the game and show them. Arguments - a report number, "list", "watch", or "playtest <what to check>".
---

# AI Product Manager reports

Reports are folders under `{{REPORTS_FOLDER}}/Inbox/` named `NNNN-<category>-<target>/`. Each one holds:

- `report.md` — what was reported and what the tool found: the target's hierarchy path, RectTransform, every
  component with the properties that matter (Button interactable, `onClick` persistent + runtime listener counts,
  Image raycastTarget, CanvasGroup alpha/interactable/blocksRaycasts, Canvas + GraphicRaycaster setup, layout
  groups...), the ancestors, a **Findings** list, the pointer / EventSystem raycast stack at the click, the graphics
  under the pointer in visual order, recent console output (errors first, with stack traces), the prefab, the
  project scripts on the target and its ancestors with their serialized field values, and "Files to look at".
- `screenshot.png` — the Game view when the report was filed; the target is outlined in red.
- `summary.txt` — the short text the user pastes into the chat (it contains the absolute report path).

## Workflow

1. Pick the report: the number the user gave (`/pm 7` → the `0007-*` folder), the one whose summary they pasted, or
   the oldest folder in `Inbox/`. `/pm list` → list the pending reports (number, category, target, age) and stop.
2. Read `report.md` completely and look at `screenshot.png` with the Read tool (it renders images).
3. Start from **Findings**: lines marked ⚠ are the usual causes (pointer blocked by another graphic, `interactable`
   false, a CanvasGroup that blocks or hides the subtree, no `onClick` listeners, an exception in the console, a
   serialized reference that is `None`). Lines marked ✔ were checked and are fine. Then open the scripts under
   **Files to look at** and follow the wiring: who subscribes to the event, what the handler does, what it needs.
4. Fix the cause. Code fixes go into the listed scripts. Scene or prefab wiring changes go through the Unity MCP
   tools when they are connected (`Unity_ManageGameObject`, or `Unity_RunCommand` with `SerializedObject`);
   otherwise describe the exact Inspector change.
5. Verify: recompile and read the console through Unity MCP (`AssetDatabase.Refresh()` then `Unity_ReadConsole`),
   and reproduce in Play mode when you can (there are Editor menu items under Window > AI Product Manager >
   Automation for scripted checks).
6. Close it out: move the folder to `{{REPORTS_FOLDER}}/Resolved/` (keep every file), append a `## Resolution`
   section to its `report.md` (what was wrong, what changed, files touched), and tell the user in 2–3 lines.
   If you could not fix it, leave it in `Inbox/` and say what is missing.

## Watch mode (`/pm watch`)

Arm the Monitor tool with a script that prints one line per new report folder, and re-arm it when it expires
(each run lasts at most 30 minutes):

```bash
prev=$(ls "{{REPORTS_FOLDER}}/Inbox" 2>/dev/null | sort)
while true; do
  cur=$(ls "{{REPORTS_FOLDER}}/Inbox" 2>/dev/null | sort)
  comm -13 <(echo "$prev") <(echo "$cur")
  prev=$cur
  sleep 2
done
```

When a line arrives, run the workflow above for that folder, then keep watching until the user says stop.
While watching, tell the user once that reports filed in Unity will now be picked up automatically.

## Playtests (`/pm playtest <what to check>`)

Play the game in the Editor yourself and report back with screenshots and a video. This needs the Unity CLI
(`unity command …` from the `com.unity.pipeline` package); check it answers with `unity command editor_status`.
Gestures go through the uGUI EventSystem, so on-screen UI reacts to them; code that reads a device directly does not.

1. Start: `unity command pm_session --action start --title "<what you check>"`, then `unity command editor_play`.
   Poll `editor_status` until `playMode` is `playing`, give the game time to load, and take
   `unity command pm_shot --label "start"` to see where you are (read the PNG it returns).
2. Look, act, look. `pm_ui` (optionally `--filter`) lists what a finger could press: prefer a target's name or
   visible text over coordinates, and skip entries with `blocked: true` unless the covering is what you check.
   Act with `pm_tap --target "…"`, `pm_drag --target "…" --dx … --dy …` or
   `pm_push --target "<joystick>" --direction up --seconds 3`. Gestures return at once: wait their duration plus half
   a second before the next command, then `pm_shot --label "…"` and look at the result.
3. Read the outcomes with `pm_session --action status`. A gesture step ends in `clicked ✔` or `dragged ✔`, or in a ⚠
   naming the control that took it instead (a popup, a level-up panel, an invisible overlay). ⚠ steps and the console
   errors listed under a step are findings. Popups often cover the controls: deal with them first.
4. Record what is worth watching: `pm_record --action start --fps 15 --max_seconds 60` before, `pm_record --action stop`
   after. Recording also stops when the session ends or Play mode exits.
5. Write expectations down as you go: `pm_note --text "Expected the shop to open; nothing happened."`
6. Finish: `pm_session --action end` writes `playtest.md` and `summary.txt`; then `unity command editor_stop`. Answer
   with what you found, the ⚠ steps and the console errors, and the key screenshots and the video (send the files
   when your tools can).

The session folder is `{{REPORTS_FOLDER}}/Playtests/NNNN-<title>/`. Coordinates are Game view pixels from the
top-left, as in the screenshots. Do not recompile or refresh assets while in Play mode.

## Notes

- The paths in `summary.txt` are absolute; inside the repo they are `{{REPORTS_FOLDER}}/Inbox/...`.
- Never delete report folders; only move them to `Resolved/`.
- A report with no target ("about the screen") describes a position on screen; use the screenshot and the notes.
- The Edit-mode screenshot is rendered from the scene cameras and does not include Screen Space - Overlay canvases.
