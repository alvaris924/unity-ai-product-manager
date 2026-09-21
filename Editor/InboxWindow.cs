using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>Window > AI Product Manager > Inbox: the filed reports, what is pending and what was resolved.</summary>
    public sealed class InboxWindow : EditorWindow
    {
        sealed class Item
        {
            public string Folder;
            public string Name;
            public string Title;
            public string Summary;
            public int Number;
            public DateTime Time;
            public bool HasScreenshot;
        }

        static InboxWindow openWindow;

        List<Item> inbox = new List<Item>();
        List<Item> resolved = new List<Item>();
        int tab;
        Vector2 scroll;

        [MenuItem("Window/AI Product Manager/Inbox", false, 10)]
        public static void Open()
        {
            var window = GetWindow<InboxWindow>("AI PM Inbox");
            window.minSize = new Vector2(520, 240);
            window.Show();
            window.Scan();
        }

        public static void RefreshIfOpen()
        {
            if (openWindow == null) return;
            openWindow.Scan();
            openWindow.Repaint();
        }

        void OnEnable()
        {
            openWindow = this;
            Scan();
        }

        void OnDisable()
        {
            if (openWindow == this) openWindow = null;
        }

        void OnFocus() => Scan();

        void Scan()
        {
            var root = AiProductManagerSettings.instance.ReportsRootAbsolute;
            inbox = Load(Path.Combine(root, ReportWriter.InboxFolder));
            inbox.Sort((a, b) => a.Number.CompareTo(b.Number));
            resolved = Load(Path.Combine(root, ReportWriter.ResolvedFolder));
            resolved.Sort((a, b) => b.Number.CompareTo(a.Number));
        }

        static List<Item> Load(string dir)
        {
            var items = new List<Item>();
            if (!Directory.Exists(dir)) return items;
            foreach (var folder in Directory.GetDirectories(dir))
            {
                var item = new Item { Folder = folder, Name = Path.GetFileName(folder), Time = Directory.GetCreationTime(folder) };
                item.Number = ReportWriter.LeadingNumber(item.Name);
                var reportFile = Path.Combine(folder, ReportWriter.ReportFile);
                item.Title = item.Name;
                try
                {
                    if (File.Exists(reportFile))
                    {
                        using (var reader = new StreamReader(reportFile))
                        {
                            var first = reader.ReadLine() ?? "";
                            if (first.StartsWith("# ")) item.Title = first.Substring(2).Replace("AI Product Manager report ", "");
                        }
                    }
                    var summaryFile = Path.Combine(folder, ReportWriter.SummaryFile);
                    item.Summary = File.Exists(summaryFile) ? File.ReadAllText(summaryFile) : "";
                }
                catch { item.Summary = ""; }
                item.HasScreenshot = File.Exists(Path.Combine(folder, ReportWriter.ScreenshotFile));
                items.Add(item);
            }
            return items;
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                tab = GUILayout.Toolbar(tab, new[] { "Inbox (" + inbox.Count + ")", "Resolved (" + resolved.Count + ")" }, EditorStyles.toolbarButton, GUILayout.Width(220));
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(inbox.Count == 0))
                    if (GUILayout.Button("Copy all pending for Claude", EditorStyles.toolbarButton)) CopyAllPending();
                if (GUILayout.Button("Report selected...", EditorStyles.toolbarButton)) ReportWindow.Open(Selection.activeGameObject);
                if (GUILayout.Button("Folder", EditorStyles.toolbarButton)) ProductManagerMenu.RevealReportsFolder();
                if (GUILayout.Button("Settings", EditorStyles.toolbarButton)) SettingsService.OpenProjectSettings(AiProductManagerSettingsProvider.Path);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) Scan();
            }

            var items = tab == 0 ? inbox : resolved;
            if (items.Count == 0)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.HelpBox(tab == 0
                        ? "No pending reports. In Play mode, right-click a UI element in the Game view; in Edit mode, Ctrl+right-click in the Scene view or use the Hierarchy context menu."
                        : "Nothing resolved yet. Claude Code moves a report here (or use Resolve) once the issue is fixed.",
                    MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var item in items)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.LabelField(item.Title, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(item.Name + "  ·  " + item.Time.ToString("yyyy-MM-dd HH:mm") + (item.HasScreenshot ? "  ·  screenshot" : ""), EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(item.Summary)))
                            if (GUILayout.Button("Copy for Claude", GUILayout.Width(110))) CopySummary(item);
                        if (GUILayout.Button("Open report", GUILayout.Width(90))) EditorUtility.OpenWithDefaultApp(Path.Combine(item.Folder, ReportWriter.ReportFile));
                        using (new EditorGUI.DisabledScope(!item.HasScreenshot))
                            if (GUILayout.Button("Screenshot", GUILayout.Width(80))) EditorUtility.OpenWithDefaultApp(Path.Combine(item.Folder, ReportWriter.ScreenshotFile));
                        if (GUILayout.Button("Reveal", GUILayout.Width(60))) EditorUtility.RevealInFinder(Path.Combine(item.Folder, ReportWriter.ReportFile));
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(tab == 0 ? "Resolve" : "Reopen", GUILayout.Width(70))) Move(item, tab == 0 ? ReportWriter.ResolvedFolder : ReportWriter.InboxFolder);
                        if (GUILayout.Button("Delete", GUILayout.Width(60))) Delete(item);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void CopySummary(Item item)
        {
            EditorGUIUtility.systemCopyBuffer = item.Summary;
            ShowNotification(new GUIContent("Copied — paste it into Claude Code"), 2);
        }

        void CopyAllPending()
        {
            var sb = new StringBuilder();
            sb.AppendLine("There are " + inbox.Count + " pending AI Product Manager reports. Work through them oldest first and move each folder to Resolved/ when it is fixed.");
            foreach (var item in inbox)
            {
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrEmpty(item.Summary) ? item.Folder : item.Summary.Trim());
            }
            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            ShowNotification(new GUIContent("Copied " + inbox.Count + " summaries — paste them into Claude Code"), 3);
        }

        void Move(Item item, string toSubfolder)
        {
            var root = AiProductManagerSettings.instance.ReportsRootAbsolute;
            var destinationDir = Path.Combine(root, toSubfolder);
            Directory.CreateDirectory(destinationDir);
            var destination = Path.Combine(destinationDir, item.Name);
            try
            {
                if (Directory.Exists(destination)) Directory.Delete(destination, true);
                Directory.Move(item.Folder, destination);
            }
            catch (Exception e)
            {
                Debug.LogError("[AI PM] Could not move " + item.Name + ": " + e.Message);
            }
            Scan();
        }

        void Delete(Item item)
        {
            if (!EditorUtility.DisplayDialog("Delete report", "Delete " + item.Name + " and its screenshot?", "Delete", "Cancel")) return;
            try { Directory.Delete(item.Folder, true); }
            catch (Exception e) { Debug.LogError("[AI PM] Could not delete " + item.Name + ": " + e.Message); }
            Scan();
        }
    }
}
