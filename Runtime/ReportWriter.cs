using System.IO;
using System.Text;
using UnityEngine;

namespace Alvaris.AiProductManager
{
    /// <summary>
    /// Writes a report to <c>&lt;root&gt;/Inbox/NNNN-category-target/</c> (report.md, screenshot.png, summary.txt)
    /// and puts the summary or the whole report on the clipboard.
    /// </summary>
    public static class ReportWriter
    {
        public const string InboxFolder = "Inbox";
        public const string ResolvedFolder = "Resolved";
        public const string ReportFile = "report.md";
        public const string ScreenshotFile = "screenshot.png";
        public const string SummaryFile = "summary.txt";

        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void Write(IssueReport report, string root, ClipboardContent clipboard)
        {
            var inbox = Path.Combine(root, InboxFolder);
            Directory.CreateDirectory(inbox);
            report.Number = NextNumber(root);
            report.ReportFolder = Path.GetFullPath(Path.Combine(inbox, report.FolderName));
            Directory.CreateDirectory(report.ReportFolder);

            if (report.ScreenshotPng != null && report.ScreenshotPng.Length > 0)
            {
                report.ScreenshotFilePath = Path.Combine(report.ReportFolder, ScreenshotFile);
                File.WriteAllBytes(report.ScreenshotFilePath, report.ScreenshotPng);
            }
            report.ReportFilePath = Path.Combine(report.ReportFolder, ReportFile);
            File.WriteAllText(report.ReportFilePath, report.ToMarkdown(), Utf8NoBom);
            report.SummaryFilePath = Path.Combine(report.ReportFolder, SummaryFile);
            File.WriteAllText(report.SummaryFilePath, report.ToSummary(), Utf8NoBom);

            CopyToClipboard(report, clipboard);
        }

        public static void CopyToClipboard(IssueReport report, ClipboardContent mode)
        {
            if (mode == ClipboardContent.Nothing) return;
            GUIUtility.systemCopyBuffer = mode == ClipboardContent.FullReport ? report.ToMarkdown() : report.ToSummary();
        }

        /// <summary>One more than the highest number used in Inbox/ and Resolved/.</summary>
        public static int NextNumber(string root)
        {
            int max = 0;
            foreach (var sub in new[] { InboxFolder, ResolvedFolder })
            {
                var dir = Path.Combine(root, sub);
                if (!Directory.Exists(dir)) continue;
                foreach (var d in Directory.GetDirectories(dir))
                {
                    int n = LeadingNumber(Path.GetFileName(d));
                    if (n > max) max = n;
                }
            }
            return max + 1;
        }

        public static int LeadingNumber(string folderName)
        {
            int i = 0;
            while (i < folderName.Length && char.IsDigit(folderName[i])) i++;
            return i > 0 && int.TryParse(folderName.Substring(0, i), out var n) ? n : 0;
        }
    }
}
