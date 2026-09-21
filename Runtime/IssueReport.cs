using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Alvaris.AiProductManager
{
    /// <summary>A titled block of Markdown in a report.</summary>
    public sealed class ReportSection
    {
        public string Title;
        public readonly StringBuilder Body = new StringBuilder();

        public ReportSection(string title) { Title = title; }

        public ReportSection Line(string text)
        {
            Body.Append(text).Append('\n');
            return this;
        }
    }

    /// <summary>One filed issue: what the reporter chose plus everything the tool found out about the target.</summary>
    public sealed class IssueReport
    {
        /// <summary>Assigned when the report is written to disk; 0 until then.</summary>
        public int Number;
        public string Category = "";
        public string Notes = "";
        public DateTime CreatedAt = DateTime.Now;
        public bool IsPlayMode;
        /// <summary>Seconds since Play started (Play mode only).</summary>
        public float PlayTime;
        public string SceneName = "";
        public string ScenePath = "";
        public string ProjectName = "";
        public string GitBranch = "";
        public string UnityVersion = Application.unityVersion;

        /// <summary>The object the report is about. Kept for enrichers; may be gone later, so the strings are what get written.</summary>
        public GameObject Target;
        /// <summary>The graphic that was actually under the pointer (often a child of the target).</summary>
        public GameObject Hit;
        public string TargetName = "";
        public string TargetPath = "";
        public string HitPath = "";
        public string HitDescription = "";
        public string HitRelation = "";

        /// <summary>Where the reporter pointed (pixels, origin bottom-left), if the report came from a click.</summary>
        public Vector2? PointerScreenPos;
        /// <summary>Where the diagnostics probed when there was no click (the target's centre).</summary>
        public Vector2? ProbePosition;
        public Vector2Int ScreenSize;
        public Rect? TargetScreenRect;

        public byte[] ScreenshotPng;
        public string ScreenshotNote = "";

        /// <summary>Lines starting with ⚠ (something is wrong) or ✔ (checked, fine).</summary>
        public readonly List<string> Findings = new List<string>();
        public readonly List<ReportSection> Sections = new List<ReportSection>();
        /// <summary>Project scripts worth opening, as asset paths.</summary>
        public readonly List<string> ScriptPaths = new List<string>();
        /// <summary>Other assets involved (prefabs, scenes), as asset paths.</summary>
        public readonly List<string> AssetPaths = new List<string>();

        // Set by ReportWriter.
        public string ReportFolder;
        public string ReportFilePath;
        public string ScreenshotFilePath;
        public string SummaryFilePath;

        public ReportSection Section(string title)
        {
            var section = new ReportSection(title);
            Sections.Add(section);
            return section;
        }

        public string Title => "AI Product Manager report" + (Number > 0 ? " #" + Number.ToString("0000") : "") + " — " + Category;

        public string FolderName =>
            Number.ToString("0000") + "-" + Slug(Category, true) + "-" + Slug(string.IsNullOrEmpty(TargetName) ? "screen" : TargetName, false);

        /// <summary>File-system friendly form of a label: letters, digits and single dashes, at most 40 characters.</summary>
        public static string Slug(string text, bool lowercase)
        {
            if (string.IsNullOrEmpty(text)) return "x";
            var sb = new StringBuilder();
            bool dash = false;
            foreach (var ch in text.Replace("'", "").Replace("’", ""))
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(lowercase ? char.ToLowerInvariant(ch) : ch);
                    dash = false;
                }
                else if (!dash && sb.Length > 0)
                {
                    sb.Append('-');
                    dash = true;
                }
                if (sb.Length >= 40) break;
            }
            var slug = sb.ToString().TrimEnd('-');
            return slug.Length > 0 ? slug : "x";
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(Title).Append("\n\n");
            sb.Append("| | |\n|---|---|\n");
            Row(sb, "Target", string.IsNullOrEmpty(TargetPath) ? "_nothing under the pointer_" : "`" + TargetPath + "`");
            if (!string.IsNullOrEmpty(HitPath) && HitPath != TargetPath)
                Row(sb, "Pointed at", "`" + HitPath + "`" + (string.IsNullOrEmpty(HitDescription) ? "" : " — " + HitDescription) + (string.IsNullOrEmpty(HitRelation) ? "" : " (" + HitRelation + ")"));
            Row(sb, "Mode", IsPlayMode ? "Play mode, " + PlayTime.ToString("0.0") + " s after Play" : "Edit mode");
            Row(sb, "Scene", string.IsNullOrEmpty(ScenePath) ? SceneName : ScenePath);
            if (PointerScreenPos.HasValue)
                Row(sb, "Pointer", "(" + PointerScreenPos.Value.x.ToString("0") + ", " + PointerScreenPos.Value.y.ToString("0") + ") on a " + ScreenSize.x + "×" + ScreenSize.y + " screen (pixels, origin bottom-left)");
            else if (ProbePosition.HasValue)
                Row(sb, "Probed at", "the target's centre (" + ProbePosition.Value.x.ToString("0") + ", " + ProbePosition.Value.y.ToString("0") + ") on a " + ScreenSize.x + "×" + ScreenSize.y + " screen");
            else
                Row(sb, "Screen", ScreenSize.x + "×" + ScreenSize.y);
            Row(sb, "Filed", CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            Row(sb, "Project", (string.IsNullOrEmpty(ProjectName) ? "" : ProjectName + " · ") + "Unity " + UnityVersion + (string.IsNullOrEmpty(GitBranch) ? "" : " · branch " + GitBranch));
            bool hasShot = ScreenshotPng != null && ScreenshotPng.Length > 0 || !string.IsNullOrEmpty(ScreenshotFilePath);
            Row(sb, "Screenshot", (hasShot ? "screenshot.png — the target is outlined in red" : "none") + (string.IsNullOrEmpty(ScreenshotNote) ? "" : " (" + ScreenshotNote + ")"));
            sb.Append('\n');
            sb.Append("**Reporter's notes:** ").Append(string.IsNullOrWhiteSpace(Notes) ? "_none_" : Notes.Trim()).Append("\n\n");

            sb.Append("## Findings\n");
            if (Findings.Count == 0) sb.Append("- _nothing flagged_\n");
            foreach (var f in Findings) sb.Append("- ").Append(f).Append('\n');

            foreach (var section in Sections)
            {
                sb.Append("\n## ").Append(section.Title).Append('\n');
                sb.Append(section.Body);
            }

            if (ScriptPaths.Count > 0 || AssetPaths.Count > 0)
            {
                sb.Append("\n## Files to look at\n");
                foreach (var p in ScriptPaths) sb.Append("- `").Append(p).Append("`\n");
                foreach (var p in AssetPaths) sb.Append("- `").Append(p).Append("`\n");
            }
            return sb.ToString();
        }

        /// <summary>The short text that goes on the clipboard: enough for a chat message, with the file paths.</summary>
        public string ToSummary()
        {
            var sb = new StringBuilder();
            sb.Append("AI Product Manager report").Append(Number > 0 ? " #" + Number.ToString("0000") : "").Append(": \"").Append(Category).Append('"');
            sb.Append(string.IsNullOrEmpty(TargetPath) ? " about the screen" : " on `" + TargetPath + "`");
            sb.Append(IsPlayMode ? " (Play mode" : " (Edit mode").Append(string.IsNullOrEmpty(SceneName) ? ")" : ", scene " + SceneName + ")").Append('\n');
            if (!string.IsNullOrWhiteSpace(Notes)) sb.Append("Notes: ").Append(Notes.Trim()).Append('\n');
            var flagged = Findings.Where(f => f.StartsWith("⚠")).Take(4).Select(f => f.Substring(1).Trim()).ToList();
            if (flagged.Count > 0) sb.Append("Findings: ").Append(string.Join("; ", flagged)).Append('\n');
            if (!string.IsNullOrEmpty(ReportFilePath))
            {
                sb.Append("Please read the full report and the screenshot first, then find the cause and fix it:\n");
                sb.Append("- ").Append(ReportFilePath).Append('\n');
                if (!string.IsNullOrEmpty(ScreenshotFilePath)) sb.Append("- ").Append(ScreenshotFilePath).Append('\n');
            }
            return sb.ToString();
        }

        static void Row(StringBuilder sb, string key, string value)
        {
            sb.Append("| ").Append(key).Append(" | ").Append((value ?? "").Replace("|", "\\|").Replace("\n", " ")).Append(" |\n");
        }
    }
}
