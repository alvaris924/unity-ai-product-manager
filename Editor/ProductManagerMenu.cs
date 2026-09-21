using System.IO;
using UnityEditor;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>Menu entries: Window > AI Product Manager and the Hierarchy context menu.</summary>
    public static class ProductManagerMenu
    {
        const string Root = "Window/AI Product Manager/";

        [MenuItem(Root + "Report selected object...", false, 1)]
        static void ReportSelected() => ReportWindow.Open(Selection.activeGameObject);

        [MenuItem(Root + "Settings", false, 20)]
        static void OpenSettings() => SettingsService.OpenProjectSettings(AiProductManagerSettingsProvider.Path);

        [MenuItem(Root + "Reveal reports folder", false, 21)]
        public static void RevealReportsFolder()
        {
            var inbox = Path.Combine(AiProductManagerSettings.instance.ReportsRootAbsolute, ReportWriter.InboxFolder);
            Directory.CreateDirectory(inbox);
            EditorUtility.RevealInFinder(inbox);
        }

        [MenuItem(Root + "Install Claude Code skill (.claude/skills/pm)", false, 40)]
        static void InstallSkill() => ClaudeSkillInstaller.Install();

        [MenuItem("GameObject/AI Product Manager/Report issue...", false, 49)]
        static void HierarchyReport(MenuCommand command)
        {
            // Invoked once per selected object from the Hierarchy; open a single window.
            if (command.context != null && command.context != Selection.activeGameObject) return;
            ReportWindow.Open(command.context as GameObject ?? Selection.activeGameObject);
        }

        [MenuItem("GameObject/AI Product Manager/Report issue...", true)]
        static bool HierarchyReportValidate() => Selection.activeGameObject != null;

        // ------------------------------------------------------------------ automation (scripted testing, e.g. through MCP)

        [MenuItem(Root + "Automation/Open popup on selected object (Play mode)", false, 100)]
        static void AutomationOpenPopup()
        {
            var overlay = ProductManagerOverlay.Instance;
            var go = Selection.activeGameObject;
            if (overlay == null || go == null)
            {
                Debug.LogWarning("[AI PM] Automation: enter Play mode with the overlay enabled and select a scene object first.");
                return;
            }
            if (!UiPicker.TryGetScreenRect(go, out var rect))
            {
                Debug.LogWarning("[AI PM] Automation: the selected object has no screen rect.");
                return;
            }
            var centre = rect.center;
            var gui = new Vector2(centre.x, Screen.height - centre.y) / overlay.OverlayScale;
            overlay.OpenPopup(centre, gui);
            Debug.Log("[AI PM] Automation: popup opened at " + centre + " for " + (overlay.CurrentTarget != null ? ReportBuilder.HierarchyPath(overlay.CurrentTarget.transform) : "nothing"));
        }

        [MenuItem(Root + "Automation/Submit first category in open popup", false, 101)]
        static void AutomationSubmitFirst()
        {
            var overlay = ProductManagerOverlay.Instance;
            if (overlay == null || !overlay.IsPopupOpen)
            {
                Debug.LogWarning("[AI PM] Automation: no popup is open.");
                return;
            }
            overlay.SubmitCategory(0);
        }

        [MenuItem(Root + "Automation/Quick report on selected object (first category)", false, 102)]
        static void AutomationQuickReport()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.LogWarning("[AI PM] Automation: select a scene object first.");
                return;
            }
            var categories = AiProductManagerSettings.instance.runtime.categories ?? ProductManagerConfig.DefaultCategories();
            EditorReportFlow.QuickReport(go, go, categories[0]);
        }

        [MenuItem(Root + "Automation/Capture Game view with overlay to Temp", false, 103)]
        static void AutomationCapture()
        {
            var overlay = ProductManagerOverlay.Instance;
            if (overlay == null)
            {
                Debug.LogWarning("[AI PM] Automation: the overlay is not installed (Play mode only).");
                return;
            }
            overlay.StartCoroutine(CaptureWithOverlay());
        }

        static System.Collections.IEnumerator CaptureWithOverlay()
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var file = Path.Combine(AiProductManagerSettings.ProjectRoot, "Temp", "aipm-gameview.png");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.Destroy(tex);
            Debug.Log("[AI PM] Automation: wrote " + file);
        }
    }
}
