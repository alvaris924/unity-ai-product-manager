using System;
using UnityEditor;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>Builds, screenshots and files reports started from the Editor (Scene view, Hierarchy, Report window).</summary>
    public static class EditorReportFlow
    {
        /// <summary>One-click report: build, take the screenshot the current mode allows, file.</summary>
        public static void QuickReport(GameObject target, GameObject hit, string category)
        {
            ProductManagerEditor.SyncConfig();
            var report = ReportBuilder.Build(target, hit, category, "", null, null, null);
            FileWithScreenshot(report, AiProductManagerSettings.instance.runtime.captureScreenshot, null);
        }

        /// <summary>
        /// Files a built report. In Play mode the overlay captures the Game view at the end of the frame (asynchronous);
        /// in Edit mode the scene cameras are rendered right away.
        /// </summary>
        public static void FileWithScreenshot(IssueReport report, bool wantScreenshot, Action<IssueReport> done)
        {
            if (wantScreenshot && report.ScreenshotPng == null)
            {
                if (Application.isPlaying && ProductManagerOverlay.Instance != null)
                {
                    ProductManagerOverlay.Instance.CaptureScreenshotAsync(report.Target, png =>
                    {
                        report.ScreenshotPng = png;
                        Finish(report, done);
                    });
                    return;
                }
                report.ScreenshotPng = EditorScreenshot.Capture(report.Target, out var note);
                report.ScreenshotNote = note;
            }
            Finish(report, done);
        }

        static void Finish(IssueReport report, Action<IssueReport> done)
        {
            ProductManager.FileReport(report);
            done?.Invoke(report);
        }
    }

    /// <summary>Edit-mode screenshots: the scene cameras rendered at the Game view's size, target outlined.</summary>
    public static class EditorScreenshot
    {
        public static byte[] Capture(GameObject target, out string note)
        {
            note = "";
            var size = Handles.GetMainGameViewSize();
            int width = Mathf.Max(64, (int)size.x);
            int height = Mathf.Max(64, (int)size.y);
            Texture2D tex = null;
            try
            {
                tex = ScreenshotUtil.RenderCamerasToTexture(width, height, target, out var highlight);
                if (tex == null)
                {
                    note = "no screenshot: no camera could be rendered in Edit mode";
                    return null;
                }
                if (highlight.HasValue) ScreenshotUtil.DrawOutline(tex, highlight.Value);
                note = "rendered from the scene cameras in Edit mode; Screen Space - Overlay canvases are not included";
                return tex.EncodeToPNG();
            }
            catch (Exception e)
            {
                note = "no screenshot: " + e.Message;
                return null;
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
