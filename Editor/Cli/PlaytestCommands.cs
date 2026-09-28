using System;
using System.Collections.Generic;
using System.IO;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>
    /// Unity CLI commands (com.unity.pipeline) for scripted playtests, so an agent can drive the Game view from a
    /// terminal: list the controls, tap, drag, push a joystick, take screenshots, record an MP4 and write the report.
    /// This assembly compiles only when the pipeline package is installed. Coordinates are Game view pixels from the
    /// top-left corner, as read off a screenshot. Gestures return at once and are logged in the session when they end.
    /// </summary>
    public static class PlaytestCommands
    {
        const string Tag = "playtest";

        [CliCommand("pm_session",
            "AI Product Manager playtest session. start: a new report folder under <reports>/Playtests/NNNN-title/. end: writes playtest.md and summary.txt there and returns the path. status: the session, its last steps (gesture outcomes, shots, console errors) and the recording.",
            Tags = new[] { Tag })]
        public static object Session(
            [CliArg("action", "start | end | status")] string action = "status",
            [CliArg("title", "Title of a new session (start)")] string title = "")
        {
            switch ((action ?? "status").Trim().ToLowerInvariant())
            {
                case "start":
                    return new { folder = Playtest.Begin(title) };
                case "end":
                {
                    var report = Playtest.Finish();
                    return new { report, folder = string.IsNullOrEmpty(report) ? "" : Path.GetDirectoryName(report) };
                }
                case "status":
                    return Status();
                default:
                    throw new ArgumentException("action must be start, end or status");
            }
        }

        [CliCommand("pm_ui",
            "Every control a finger could press in the Game view right now (Play mode): name, hierarchy path, visible text, kind, centre and size in pixels from the top-left, interactable, and whether something else would take a tap at its centre (blocked).",
            Tags = new[] { Tag })]
        public static object Ui(
            [CliArg("filter", "Keep controls whose name, path or text contains this")] string filter = "")
        {
            var list = Playtest.Ui(filter);
            var elements = new List<object>();
            foreach (var e in list)
            {
                elements.Add(new
                {
                    name = e.Name,
                    text = e.Text,
                    kind = e.Kind,
                    path = e.Path,
                    x = Mathf.RoundToInt(e.Centre.x),
                    y = Mathf.RoundToInt(Screen.height - e.Centre.y),
                    w = Mathf.RoundToInt(e.ScreenRect.width),
                    h = Mathf.RoundToInt(e.ScreenRect.height),
                    interactable = e.Interactable,
                    blocked = e.Blocked,
                    top = e.Blocked && e.TopAtCentre != null ? ReportBuilder.HierarchyPath(e.TopAtCentre.transform) : null,
                });
            }
            return new { screen = new { width = Screen.width, height = Screen.height }, count = elements.Count, elements };
        }

        [CliCommand("pm_tap",
            "Tap a control (--target: name, path or visible text, see pm_ui) or a point (--x --y, pixels from the top-left). --seconds above 0.08 makes it a long press. The outcome (clicked, pressed, landed on nothing) is logged in the session.",
            Tags = new[] { Tag })]
        public static object Tap(
            [CliArg("target", "Control name, hierarchy path or visible text")] string target = "",
            [CliArg("x", "Pixels from the left edge of the Game view")] float x = -1f,
            [CliArg("y", "Pixels from the top edge of the Game view")] float y = -1f,
            [CliArg("seconds", "How long the finger stays down (0 = a tap)")] float seconds = 0f)
        {
            var where = Where(target, x, y);
            int id = Playtest.Tap(where, seconds);
            return new { gesture = id, target = Describe(where) };
        }

        [CliCommand("pm_drag",
            "Press on a control or point and slide to (--to_x, --to_y) or by (--dx, --dy) pixels over --seconds, then lift: sliders, swipes, scroll views, drag and drop.",
            Tags = new[] { Tag })]
        public static object Drag(
            [CliArg("target", "Control to start on (name, path or visible text)")] string target = "",
            [CliArg("x", "Start, pixels from the left")] float x = -1f,
            [CliArg("y", "Start, pixels from the top")] float y = -1f,
            [CliArg("to_x", "End, pixels from the left")] float toX = -1f,
            [CliArg("to_y", "End, pixels from the top")] float toY = -1f,
            [CliArg("dx", "Move right by this many pixels (negative = left)")] float dx = 0f,
            [CliArg("dy", "Move down by this many pixels (negative = up)")] float dy = 0f,
            [CliArg("seconds", "Duration of the slide")] float seconds = 0.4f)
        {
            var where = Where(target, x, y);
            Vector2 end = toX >= 0f && toY >= 0f ? ToScreen(toX, toY) : where.ScreenPoint + new Vector2(dx, -dy);
            int id = Playtest.Drag(where, end, seconds);
            return new { gesture = id, from = Describe(where), to = new { x = Mathf.RoundToInt(end.x), y = Mathf.RoundToInt(Screen.height - end.y) } };
        }

        [CliCommand("pm_push",
            "Push a joystick: press on the stick (--target or --x --y) and keep the finger moving --direction (up, down, left, right, up-left…) or along (--dx, --dy) for --seconds. Works for fixed sticks and for floating sticks that re-centre on the finger.",
            Tags = new[] { Tag })]
        public static object Push(
            [CliArg("target", "The joystick or touch area (name, path or visible text)")] string target = "",
            [CliArg("x", "Press point, pixels from the left")] float x = -1f,
            [CliArg("y", "Press point, pixels from the top")] float y = -1f,
            [CliArg("direction", "up, down, left, right, up-left, up-right, down-left or down-right")] string direction = "up",
            [CliArg("dx", "Custom direction: rightward component")] float dx = 0f,
            [CliArg("dy", "Custom direction: downward component")] float dy = 0f,
            [CliArg("seconds", "How long to push")] float seconds = 2f,
            [CliArg("speed", "Finger speed in pixels per second")] float speed = 600f)
        {
            var where = Where(target, x, y);
            Vector2 dir = dx != 0f || dy != 0f ? new Vector2(dx, -dy) : Direction(direction);
            int id = Playtest.Push(where, dir, seconds, speed);
            return new { gesture = id, target = Describe(where), seconds };
        }

        [CliCommand("pm_shot",
            "Screenshot of the Game view, overlay canvases included, saved as the session's next NN-label.png at the end of this frame (Play mode).",
            Tags = new[] { Tag })]
        public static object Shot(
            [CliArg("label", "What the screenshot shows; also names the file")] string label = "")
        {
            return new { file = Playtest.Screenshot(label) };
        }

        [CliCommand("pm_record",
            "Record the Game view to an H.264 MP4 in the session folder with the Editor's MediaEncoder. start (--fps, --max_seconds) or stop. Ending the session or leaving Play mode also stops it.",
            Tags = new[] { Tag })]
        public static object Record(
            [CliArg("action", "start | stop")] string action = "start",
            [CliArg("fps", "Frames per second of real time")] int fps = 15,
            [CliArg("max_seconds", "Stop on its own after this long")] float maxSeconds = 60f)
        {
            switch ((action ?? "start").Trim().ToLowerInvariant())
            {
                case "start":
                    return new { file = Playtest.StartRecording(fps, maxSeconds) };
                case "stop":
                {
                    var info = Playtest.StopRecording();
                    if (info == null) return new { stopped = false, message = "nothing was recording" };
                    return new { stopped = true, file = info.Path, seconds = info.Seconds, frames = info.Frames, fps = info.FramesPerSecond, width = info.Size.x, height = info.Size.y, skipped = info.SkippedFrames };
                }
                default:
                    throw new ArgumentException("action must be start or stop");
            }
        }

        [CliCommand("pm_note",
            "Add a free-text step to the playtest report: what was expected, what happened instead, a finding.",
            Tags = new[] { Tag })]
        public static object Note(
            [CliArg("text", "The note", Required = true)] string text)
        {
            Playtest.Note(text);
            return new { noted = true };
        }

        // ------------------------------------------------------------------ helpers

        static object Status()
        {
            var session = Playtest.Session;
            var last = new List<object>();
            var steps = session.Steps;
            for (int i = Math.Max(0, steps.Count - 8); i < steps.Count; i++)
            {
                var s = steps[i];
                last.Add(new { n = i + 1, seconds = Math.Round(s.seconds, 1), s.action, s.detail, s.file, s.errors });
            }
            return new
            {
                active = session.IsActive,
                title = session.Title,
                folder = session.Folder,
                elapsed = Math.Round(session.Elapsed, 1),
                steps = steps.Count,
                last,
                recording = Playtest.IsRecording ? new { file = Playtest.RecordingFile, seconds = Playtest.RecordingSeconds } : null,
                playing = EditorApplication.isPlaying,
                gestures = PlaytestDriver.Instance != null ? PlaytestDriver.Instance.ActiveGestures : 0,
            };
        }

        static Playtest.Target Where(string target, float x, float y)
        {
            if (!string.IsNullOrWhiteSpace(target)) return Playtest.Resolve(target);
            if (x < 0f || y < 0f) throw new ArgumentException("Give --target (a name, path or visible text; see pm_ui) or --x and --y in Game view pixels from the top-left.");
            return Playtest.At(ToScreen(x, y));
        }

        static Vector2 ToScreen(float x, float y) => new Vector2(x, Screen.height - y);

        static object Describe(Playtest.Target t) => new
        {
            label = t.Label,
            x = Mathf.RoundToInt(t.ScreenPoint.x),
            y = Mathf.RoundToInt(Screen.height - t.ScreenPoint.y),
            matches = t.Matches,
            blocked = t.Element != null && t.Element.Blocked,
            under_finger = t.TopAtPoint != null ? ReportBuilder.HierarchyPath(t.TopAtPoint.transform) : null,
        };

        static Vector2 Direction(string name)
        {
            switch ((name ?? "up").Trim().ToLowerInvariant())
            {
                case "up": return Vector2.up;
                case "down": return Vector2.down;
                case "left": return Vector2.left;
                case "right": return Vector2.right;
                case "up-left": return new Vector2(-1f, 1f);
                case "up-right": return new Vector2(1f, 1f);
                case "down-left": return new Vector2(-1f, -1f);
                case "down-right": return new Vector2(1f, -1f);
                default: throw new ArgumentException("direction must be up, down, left, right, up-left, up-right, down-left or down-right");
            }
        }
    }
}
