using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>One line of a playtest: what was done, what came of it, and the console errors logged since the line before.</summary>
    [Serializable]
    public sealed class PlaytestStep
    {
        public double seconds;
        public string action;
        public string detail;
        /// <summary>A screenshot or video in the session folder, by file name; empty when the step made none.</summary>
        public string file;
        public List<string> errors = new List<string>();
    }

    /// <summary>
    /// The playtest in progress: its folder under <c>&lt;reports&gt;/Playtests/NNNN-title/</c>, its steps and its video.
    /// Kept in a ScriptableSingleton so a session started in Edit mode survives the domain reload of entering Play mode.
    /// <see cref="Finish"/> writes <c>playtest.md</c> and <c>summary.txt</c> next to the screenshots.
    /// </summary>
    public sealed class PlaytestSession : ScriptableSingleton<PlaytestSession>
    {
        public const string PlaytestsFolder = "Playtests";
        public const string ReportFile = "playtest.md";
        public const string SummaryFile = "summary.txt";

        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        [SerializeField] bool active;
        [SerializeField] string title;
        [SerializeField] string folder;
        [SerializeField] long startedTicks;
        [SerializeField] long errorsCheckedTicks;
        [SerializeField] int shotCount;
        [SerializeField] int videoCount;
        [SerializeField] string gameViewSize;
        [SerializeField] List<PlaytestStep> steps = new List<PlaytestStep>();

        [NonSerialized] PlaytestRecorder recorder;

        public bool IsActive => active;
        public string Title => title;
        public string Folder => folder;
        public IReadOnlyList<PlaytestStep> Steps => steps;
        public double Elapsed => active ? (DateTime.Now - new DateTime(startedTicks)).TotalSeconds : 0;
        internal PlaytestRecorder Recorder => recorder;

        /// <summary>Starts a new session (finishing the current one first) and returns its folder.</summary>
        public string Begin(string sessionTitle)
        {
            if (active) Finish();
            title = string.IsNullOrWhiteSpace(sessionTitle) ? "playtest" : sessionTitle.Trim();
            var root = Path.Combine(AiProductManagerSettings.instance.ReportsRootAbsolute, PlaytestsFolder);
            Directory.CreateDirectory(root);
            folder = Path.Combine(root, NextNumber(root).ToString("0000") + "-" + Slug(title));
            Directory.CreateDirectory(folder);
            startedTicks = DateTime.Now.Ticks;
            errorsCheckedTicks = startedTicks;
            shotCount = 0;
            videoCount = 0;
            gameViewSize = "";
            steps.Clear();
            active = true;
            return folder;
        }

        /// <summary>The active session's folder, starting an untitled session when none is running.</summary>
        public string EnsureActive()
        {
            if (!active) Begin("playtest");
            return folder;
        }

        public PlaytestStep AddStep(string action, string detail, string file = "")
        {
            EnsureActive();
            if (Application.isPlaying && string.IsNullOrEmpty(gameViewSize)) gameViewSize = Screen.width + "×" + Screen.height;
            var step = new PlaytestStep
            {
                seconds = Elapsed,
                action = action,
                detail = detail ?? "",
                file = file ?? "",
            };
            var checkedAt = new DateTime(errorsCheckedTicks);
            foreach (var entry in LogBuffer.ErrorsSince(checkedAt, 20))
                step.errors.Add(entry.Type + ": " + ReportBuilder.Trunc(entry.Message, 300));
            errorsCheckedTicks = DateTime.Now.Ticks;
            steps.Add(step);
            return step;
        }

        /// <summary>Absolute path for the next screenshot, named <c>NN-label.png</c>.</summary>
        public string NextShotPath(string label)
        {
            EnsureActive();
            shotCount++;
            return Path.Combine(folder, shotCount.ToString("00") + "-" + Slug(string.IsNullOrWhiteSpace(label) ? "shot" : label) + ".png");
        }

        internal PlaytestRecorder BeginRecorder(int fps, float maxSeconds)
        {
            EnsureActive();
            EndRecorder();
            videoCount++;
            recorder = new PlaytestRecorder(Path.Combine(folder, videoCount == 1 ? "video.mp4" : "video-" + videoCount + ".mp4"), fps, maxSeconds);
            return recorder;
        }

        /// <summary>Closes the video file, if one is being written, and returns the finished recorder (null when none was).</summary>
        internal PlaytestRecorder EndRecorder()
        {
            var finished = recorder;
            recorder = null;
            finished?.Dispose();
            return finished;
        }

        /// <summary>Writes playtest.md and summary.txt, ends the session and returns the report's path (empty when no session ran).</summary>
        public string Finish()
        {
            if (!active) return "";
            var video = EndRecorder();
            if (video != null) AddStep("record", "stopped: " + Path.GetFileName(video.FilePath) + ", " + video.Seconds.ToString("0.0") + " s", Path.GetFileName(video.FilePath));
            var report = Path.Combine(folder, ReportFile);
            File.WriteAllText(report, ToMarkdown(), Utf8NoBom);
            File.WriteAllText(Path.Combine(folder, SummaryFile), ToSummary(report), Utf8NoBom);
            active = false;
            return report;
        }

        string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.Append("# Playtest ").Append(ReportWriter.LeadingNumber(Path.GetFileName(folder)).ToString("0000")).Append(" · ").AppendLine(title);
            sb.AppendLine();
            sb.Append("- Project: `").Append(Path.GetFileName(AiProductManagerSettings.ProjectRoot)).Append("` · Unity ").AppendLine(Application.unityVersion);
            sb.Append("- Started ").Append(new DateTime(startedTicks).ToString("yyyy-MM-dd HH:mm:ss"))
              .Append(" · ").Append(Duration(Elapsed))
              .Append(string.IsNullOrEmpty(gameViewSize) ? "" : " · Game view " + gameViewSize).AppendLine();
            int errorCount = 0;
            foreach (var s in steps) errorCount += s.errors.Count;
            sb.Append("- ").Append(steps.Count).Append(steps.Count == 1 ? " step, " : " steps, ").Append(errorCount).AppendLine(errorCount == 1 ? " console error" : " console errors");
            foreach (var s in steps)
                if (s.action == "record" && s.file.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    sb.Append("- Video: [").Append(s.file).Append("](").Append(s.file).AppendLine(")");
            sb.AppendLine();
            sb.AppendLine("## Steps");
            sb.AppendLine();
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                sb.Append(i + 1).Append(". `").Append(Clock(s.seconds)).Append("` **").Append(s.action).Append("** ").AppendLine(s.detail);
                if (!string.IsNullOrEmpty(s.file) && s.file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    sb.Append("   ![").Append(s.detail.Replace("]", ")")).Append("](").Append(s.file).AppendLine(")");
                if (s.errors.Count > 0)
                {
                    sb.Append("   ⚠ ").Append(s.errors.Count).AppendLine(s.errors.Count == 1 ? " console error before this step:" : " console errors before this step:");
                    foreach (var e in s.errors) sb.Append("   - `").Append(e.Replace("`", "'")).AppendLine("`");
                }
            }
            sb.AppendLine();
            sb.AppendLine("_Gestures were delivered through the uGUI EventSystem: controls that read an input device directly did not see them._");
            return sb.ToString();
        }

        string ToSummary(string reportPath)
        {
            int errorCount = 0, shots = 0;
            foreach (var s in steps)
            {
                errorCount += s.errors.Count;
                if (s.file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) shots++;
            }
            return "Playtest \"" + title + "\": " + steps.Count + " steps, " + shots + " screenshots, " + errorCount + " console errors, "
                   + Duration(Elapsed) + ". Report: " + reportPath;
        }

        static int NextNumber(string root)
        {
            int max = 0;
            foreach (var d in Directory.GetDirectories(root))
                max = Mathf.Max(max, ReportWriter.LeadingNumber(Path.GetFileName(d)));
            return max + 1;
        }

        internal static string Slug(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
                if (sb.Length >= 40) break;
            }
            var slug = sb.ToString().Trim('-');
            return slug.Length > 0 ? slug : "shot";
        }

        static string Clock(double seconds) => ((int)(seconds / 60)).ToString("00") + ":" + (seconds % 60).ToString("00.0");

        static string Duration(double seconds) => seconds < 60 ? seconds.ToString("0") + " s" : ((int)(seconds / 60)) + " min " + ((int)(seconds % 60)) + " s";
    }
}
