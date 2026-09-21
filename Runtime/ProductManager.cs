using System;
using System.IO;
using UnityEngine;

namespace Alvaris.AiProductManager
{
    /// <summary>
    /// Entry point shared by the in-game overlay and the Editor: holds the active configuration, lets the Editor
    /// enrich reports before they are written, and files them.
    /// </summary>
    public static class ProductManager
    {
        static ProductManagerConfig config = new ProductManagerConfig();

        public static ProductManagerConfig Config
        {
            get => config;
            set => config = value ?? new ProductManagerConfig();
        }

        /// <summary>Raised before a report is written; subscribers add sections (the Editor adds script paths, prefab info, serialized fields).</summary>
        public static event Action<IssueReport> Enriching;

        /// <summary>Raised after the report has been written to disk and the clipboard.</summary>
        public static event Action<IssueReport> Filed;

        /// <summary>Set by the Editor: opens a window to add notes before filing. Returns false when it could not.</summary>
        public static Func<IssueReport, bool> DetailedReportHandler;

        public static bool CanRequestDetailed => DetailedReportHandler != null;

        /// <summary>Folder that receives Inbox/ and Resolved/.</summary>
        public static string ReportsRoot =>
            string.IsNullOrEmpty(config.reportsRoot) ? Path.Combine(Application.persistentDataPath, "AIProductManager") : config.reportsRoot;

        /// <summary>Enriches, writes and copies the report. The report's Number and paths are set afterwards.</summary>
        public static IssueReport FileReport(IssueReport report)
        {
            try { Enriching?.Invoke(report); }
            catch (Exception e) { Debug.LogException(e); }

            ReportWriter.Write(report, ReportsRoot, config.clipboard);
            Debug.Log("[AI PM] Report #" + report.Number.ToString("0000") + " \"" + report.Category + "\""
                      + (string.IsNullOrEmpty(report.TargetPath) ? "" : " on " + report.TargetPath) + " → " + report.ReportFilePath
                      + (config.clipboard == ClipboardContent.Nothing ? "" : " (" + (config.clipboard == ClipboardContent.Summary ? "summary" : "full report") + " copied to the clipboard)"));

            try { Filed?.Invoke(report); }
            catch (Exception e) { Debug.LogException(e); }
            return report;
        }

        public static bool RequestDetailed(IssueReport report)
        {
            var handler = DetailedReportHandler;
            if (handler == null) return false;
            try { return handler(report); }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }
    }
}
