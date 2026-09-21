using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>Report with notes: pick the target and category, type what you expected, save and copy.</summary>
    public sealed class ReportWindow : EditorWindow
    {
        GameObject target;
        GameObject hit;
        int categoryIndex;
        string customCategory = "";
        string notes = "";
        bool includeScreenshot = true;
        bool filing;
        IssueReport prebuilt;   // came from the Game view popup: sections and screenshot already captured
        IssueReport filed;
        Vector2 scroll;

        public static void Open(GameObject target, GameObject hit = null)
        {
            var window = GetWindow<ReportWindow>(true, "Report an issue", true);
            window.target = target;
            window.hit = hit != null ? hit : target;
            window.prebuilt = null;
            window.filed = null;
            window.filing = false;
            window.minSize = new Vector2(440, 380);
            window.Show();
        }

        public static void OpenWith(IssueReport report)
        {
            var window = GetWindow<ReportWindow>(true, "Report an issue", true);
            window.prebuilt = report;
            window.target = report.Target;
            window.hit = report.Hit;
            window.filed = null;
            window.filing = false;
            window.includeScreenshot = report.ScreenshotPng != null;
            window.minSize = new Vector2(440, 380);
            window.Show();
            window.Focus();
        }

        string[] Categories => AiProductManagerSettings.instance.runtime.categories ?? ProductManagerConfig.DefaultCategories();

        string Category
        {
            get
            {
                var cats = Categories;
                if (categoryIndex >= 0 && categoryIndex < cats.Length) return cats[categoryIndex];
                return string.IsNullOrWhiteSpace(customCategory) ? "Something else" : customCategory.Trim();
            }
        }

        void OnGUI()
        {
            var settings = AiProductManagerSettings.instance;
            if (filed != null)
            {
                DrawResult(settings);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(prebuilt != null || filing))
            {
                target = (GameObject)EditorGUILayout.ObjectField("Target", target, typeof(GameObject), true);
            }
            if (target != null)
            {
                EditorGUILayout.LabelField(" ", ReportBuilder.HierarchyPath(target.transform), EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(prebuilt != null || target.transform.parent == null))
                        if (GUILayout.Button("Parent", GUILayout.Width(70))) target = target.transform.parent.gameObject;
                    if (GUILayout.Button("Ping", GUILayout.Width(50))) EditorGUIUtility.PingObject(target);
                }
            }
            else if (prebuilt == null)
            {
                EditorGUILayout.HelpBox("Drop the GameObject the issue is about, or leave empty to report the screen in general.", MessageType.None);
            }

            var options = Categories.Concat(new[] { "Custom..." }).ToArray();
            categoryIndex = EditorGUILayout.Popup("What's wrong", Mathf.Clamp(categoryIndex, 0, options.Length - 1), options);
            if (categoryIndex == options.Length - 1) customCategory = EditorGUILayout.TextField("Custom category", customCategory);

            EditorGUILayout.LabelField("Notes: what you expected and what happened");
            notes = EditorGUILayout.TextArea(notes, GUILayout.MinHeight(80));

            string screenshotLabel = prebuilt != null && prebuilt.ScreenshotPng != null ? "Include the screenshot taken when you clicked"
                : Application.isPlaying ? "Include a Game view screenshot"
                : "Include a screenshot rendered from the scene cameras";
            includeScreenshot = EditorGUILayout.ToggleLeft(screenshotLabel, includeScreenshot);

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("Saved to " + settings.ReportsRootAbsolute + "\\Inbox" +
                                    (settings.clipboard == ClipboardContent.Nothing ? "." : "; the " + (settings.clipboard == ClipboardContent.Summary ? "summary" : "full report") + " is copied to the clipboard for Claude Code."),
                MessageType.None);

            using (new EditorGUI.DisabledScope(filing))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(filing ? "Saving..." : "Save report & copy for Claude", GUILayout.Height(30))) File();
                if (GUILayout.Button("Copy full report only", GUILayout.Height(30), GUILayout.Width(160))) CopyOnly();
            }
            EditorGUILayout.EndScrollView();
        }

        void File()
        {
            ProductManagerEditor.SyncConfig();
            var report = prebuilt ?? ReportBuilder.Build(target, hit, Category, notes, null, null, null);
            report.Category = Category;
            report.Notes = notes;
            if (!includeScreenshot) report.ScreenshotPng = null;
            filing = true;
            EditorReportFlow.FileWithScreenshot(report, includeScreenshot, r =>
            {
                filed = r;
                filing = false;
                Repaint();
            });
        }

        void CopyOnly()
        {
            ProductManagerEditor.SyncConfig();
            var report = prebuilt ?? ReportBuilder.Build(target, hit, Category, notes, null, null, null);
            report.Category = Category;
            report.Notes = notes;
            EditorGUIUtility.systemCopyBuffer = report.ToMarkdown();
            ShowNotification(new GUIContent("Full report copied (not saved)"), 3);
        }

        void DrawResult(AiProductManagerSettings settings)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Report #" + filed.Number.ToString("0000") + " saved", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(filed.Category + (string.IsNullOrEmpty(filed.TargetPath) ? "" : " — " + filed.TargetPath), EditorStyles.wordWrappedLabel);
            EditorGUILayout.SelectableLabel(filed.ReportFilePath, EditorStyles.miniLabel, GUILayout.Height(16));
            if (settings.clipboard != ClipboardContent.Nothing)
                EditorGUILayout.HelpBox("The " + (settings.clipboard == ClipboardContent.Summary ? "summary" : "full report") + " is on the clipboard: paste it into Claude Code.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy summary")) EditorGUIUtility.systemCopyBuffer = filed.ToSummary();
                if (GUILayout.Button("Copy full report")) EditorGUIUtility.systemCopyBuffer = filed.ToMarkdown();
                if (GUILayout.Button("Open report")) EditorUtility.OpenWithDefaultApp(filed.ReportFilePath);
                if (GUILayout.Button("Reveal")) EditorUtility.RevealInFinder(filed.ReportFilePath);
            }
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Inbox")) InboxWindow.Open();
                if (GUILayout.Button("New report"))
                {
                    filed = null;
                    prebuilt = null;
                    notes = "";
                }
                if (GUILayout.Button("Close")) Close();
            }
        }
    }
}
