using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>
    /// Scripted playtests in the Editor, for an agent or a test: name a control ("Play", "Shop/BuyButton") or a point,
    /// tap, hold, drag or push a joystick through <see cref="PlaytestDriver"/>, take screenshots and record an MP4.
    /// Every action becomes a step of the active <see cref="PlaytestSession"/>, with the console errors logged since
    /// the step before; <see cref="Finish"/> writes the report. Gestures, shots and recording need Play mode.
    /// </summary>
    [InitializeOnLoad]
    public static class Playtest
    {
        /// <summary>Where a gesture starts and what is there right now.</summary>
        public sealed class Target
        {
            public string Label;
            public Vector2 ScreenPoint;
            public UiTargets.Element Element;
            public int Matches;
            public GameObject TopAtPoint;
        }

        static Playtest()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting -= OnQuitting;
            EditorApplication.quitting += OnQuitting;
        }

        public static PlaytestSession Session => PlaytestSession.instance;

        public static string Begin(string title) => Session.Begin(title);

        /// <summary>Stops a recording, writes playtest.md and summary.txt and returns the report path.</summary>
        public static string Finish()
        {
            StopRecording();
            return Session.Finish();
        }

        /// <summary>Adds a free-text step, e.g. what was expected and what happened instead.</summary>
        public static void Note(string text) => Session.AddStep("note", text);

        public static List<UiTargets.Element> Ui(string filter)
        {
            RequirePlayMode();
            return UiTargets.List(filter);
        }

        /// <summary>The centre of the control a query names. Throws when nothing on screen matches.</summary>
        public static Target Resolve(string query)
        {
            RequirePlayMode();
            if (!UiTargets.TryResolve(query, out var element, out var matches))
                throw new ArgumentException("Nothing on screen matches \"" + query + "\". List the controls with Ui() / pm_ui.");
            bool queryIsPath = element.Path.EndsWith(query.Trim(), StringComparison.OrdinalIgnoreCase) && query.Contains("/");
            return new Target
            {
                Label = queryIsPath ? "`" + element.Path + "`" : "\"" + query.Trim() + "\" (`" + element.Path + "`)",
                ScreenPoint = element.Centre,
                Element = element,
                Matches = matches.Count,
                TopAtPoint = element.TopAtCentre,
            };
        }

        /// <summary>A screen point (pixels, origin bottom-left) and what the EventSystem would hit there.</summary>
        public static Target At(Vector2 screenPoint)
        {
            RequirePlayMode();
            var hits = UiPicker.EventSystemRaycast(screenPoint);
            return new Target
            {
                Label = "(" + Mathf.RoundToInt(screenPoint.x) + ", " + Mathf.RoundToInt(Screen.height - screenPoint.y) + ")",
                ScreenPoint = screenPoint,
                TopAtPoint = hits.Count > 0 ? hits[0].gameObject : null,
            };
        }

        /// <summary>Taps the target; with <paramref name="holdSeconds"/> above a tap's length it is a long press.</summary>
        public static int Tap(Target target, float holdSeconds = 0f)
        {
            var driver = Driver();
            Session.EnsureActive();
            return holdSeconds > PlaytestDriver.TapSeconds
                ? driver.Hold(target.ScreenPoint, holdSeconds, r => Log("hold", target.Label + " for " + holdSeconds.ToString("0.##") + " s", r, target))
                : driver.Tap(target.ScreenPoint, r => Log("tap", target.Label, r, target));
        }

        /// <summary>Presses on the target and slides to <paramref name="to"/> (pixels, origin bottom-left) over the given time.</summary>
        public static int Drag(Target from, Vector2 to, float seconds)
        {
            var driver = Driver();
            Session.EnsureActive();
            string label = from.Label + " to (" + Mathf.RoundToInt(to.x) + ", " + Mathf.RoundToInt(Screen.height - to.y) + ") in " + seconds.ToString("0.##") + " s";
            return driver.Drag(from.ScreenPoint, to, seconds, r => Log("drag", label, r, from));
        }

        /// <summary>Joystick push: presses on the target and keeps the finger moving one way for the given time.</summary>
        public static int Push(Target from, Vector2 direction, float seconds, float pixelsPerSecond)
        {
            var driver = Driver();
            Session.EnsureActive();
            string label = from.Label + " toward " + Compass(direction) + " for " + seconds.ToString("0.##") + " s";
            return driver.Push(from.ScreenPoint, direction, pixelsPerSecond, seconds, r => Log("push", label, r, from));
        }

        /// <summary>Saves the Game view at the end of this frame as the session's next <c>NN-label.png</c> and returns its path.</summary>
        public static string Screenshot(string label)
        {
            var driver = Driver();
            var path = Session.NextShotPath(label);
            var text = string.IsNullOrWhiteSpace(label) ? "Game view" : label.Trim();
            driver.CaptureEndOfFrame(tex =>
            {
                if (tex == null)
                {
                    Session.AddStep("shot", text + " (capture failed)");
                    return;
                }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Session.AddStep("shot", text, Path.GetFileName(path));
            });
            return path;
        }

        /// <summary>Starts writing the Game view to the session's video file and returns its path.</summary>
        public static string StartRecording(int fps, float maxSeconds)
        {
            var driver = Driver();
            var recorder = Session.BeginRecorder(fps, maxSeconds);
            driver.StartFrameStream(recorder.FramesPerSecond, tex =>
            {
                var active = Session.Recorder;
                if (active == null) return;
                active.Add(tex);
                if (active.IsFull) StopRecording();
            });
            Session.AddStep("record", "started at " + recorder.FramesPerSecond + " fps, at most " + Mathf.RoundToInt(maxSeconds) + " s");
            return recorder.FilePath;
        }

        /// <summary>Closes the video file. Returns null when nothing was recording.</summary>
        public static PlaytestRecorderInfo StopRecording()
        {
            if (PlaytestDriver.Instance != null) PlaytestDriver.Instance.StopFrameStream();
            var recorder = Session.EndRecorder();
            if (recorder == null) return null;
            var info = new PlaytestRecorderInfo
            {
                Path = recorder.FilePath,
                Seconds = recorder.Seconds,
                Frames = recorder.FramesWritten,
                FramesPerSecond = recorder.FramesPerSecond,
                Size = recorder.Size,
                SkippedFrames = recorder.FramesSkipped,
            };
            Session.AddStep("record", "stopped: " + Path.GetFileName(info.Path) + ", " + info.Seconds.ToString("0.0") + " s"
                                      + (info.SkippedFrames > 0 ? " (" + info.SkippedFrames + " frames skipped: the Game view was resized)" : ""),
                Path.GetFileName(info.Path));
            return info;
        }

        public static bool IsRecording => Session.Recorder != null;

        /// <summary>The video being written, or empty when nothing is recording.</summary>
        public static string RecordingFile => Session.Recorder != null ? Session.Recorder.FilePath : "";

        /// <summary>Seconds of video written so far.</summary>
        public static float RecordingSeconds => Session.Recorder != null ? Session.Recorder.Seconds : 0f;

        // ------------------------------------------------------------------ internals

        static PlaytestDriver Driver()
        {
            RequirePlayMode();
            var driver = PlaytestDriver.Ensure();
            if (driver == null) throw new InvalidOperationException("The playtest driver could not be installed.");
            return driver;
        }

        static void RequirePlayMode()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first: gestures, screenshots and recording act on the running game.");
        }

        static void Log(string action, string label, PlaytestDriver.GestureResult r, Target target)
        {
            Session.AddStep(action, label + " → " + Outcome(action, r, target.Element != null ? target.Element.GameObject : null));
        }

        /// <summary>What the gesture reached, checked against the control it aimed at: a different control taking it is the finding.</summary>
        static string Outcome(string action, PlaytestDriver.GestureResult r, GameObject aimed)
        {
            string result;
            if (r.Clicked != null) result = Reached("clicked", r.Clicked, aimed);
            else if (r.Dragged != null) result = Reached("dragged", r.Dragged, aimed);
            else if (r.PressHandler != null) result = Reached("pressed", r.PressHandler, aimed) + (action == "tap" ? ", no click on release" : "");
            else if (r.PressHit != null) result = "landed on `" + ReportBuilder.HierarchyPath(r.PressHit.transform) + "`, which takes no pointer events" + (aimed != null ? " ⚠ instead of the target" : "");
            else result = "landed on nothing" + (aimed != null ? " ⚠" : "");
            return r.Cancelled ? result + " (cancelled)" : result;
        }

        static string Reached(string verb, GameObject reached, GameObject aimed)
        {
            if (aimed != null && reached == aimed) return verb + " ✔";
            return verb + " `" + ReportBuilder.HierarchyPath(reached.transform) + "`" + (aimed != null ? " ⚠ instead of the target" : "");
        }

        static string Compass(Vector2 direction)
        {
            // Screen space: y grows upward.
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            string[] names = { "right", "up-right", "up", "up-left", "left", "down-left", "down", "down-right" };
            int index = Mathf.RoundToInt(((angle % 360f) + 360f) % 360f / 45f) % 8;
            return names[index];
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingPlayMode) return;
            StopRecording();
            if (PlaytestDriver.Instance != null) PlaytestDriver.Instance.CancelAll();
            PlaytestDriver.Uninstall();
        }

        static void OnBeforeAssemblyReload() => StopRecording();

        static void OnQuitting() => Finish();
    }

    /// <summary>A finished recording.</summary>
    public sealed class PlaytestRecorderInfo
    {
        public string Path;
        public float Seconds;
        public int Frames;
        public int FramesPerSecond;
        public Vector2Int Size;
        public int SkippedFrames;
    }
}
