using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>Project Settings > AI Product Manager.</summary>
    static class AiProductManagerSettingsProvider
    {
        public const string Path = "Project/AI Product Manager";

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(Path, SettingsScope.Project)
            {
                label = "AI Product Manager",
                keywords = new HashSet<string>(new[] { "AI", "Product Manager", "Claude", "issue", "report", "bug", "QA" }),
                guiHandler = _ => Draw(),
            };
        }

        static void Draw()
        {
            var settings = AiProductManagerSettings.instance;
            // ScriptableSingleton instances are NotEditable by default, which greys out SerializedObject fields.
            settings.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.NotEditable;
            var so = new SerializedObject(settings);
            so.Update();

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.HelpBox(
                        "Play mode: right-click a UI element in the Game view and pick what is wrong.\n" +
                        "Edit mode: Ctrl+right-click in the Scene view, or right-click in the Hierarchy > AI Product Manager > Report issue...\n" +
                        "Each report is saved under the reports folder and its summary is copied for Claude Code.",
                        MessageType.Info);

                    EditorGUI.BeginChangeCheck();
                    var prop = so.GetIterator();
                    bool enterChildren = true;
                    while (prop.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (prop.name == "m_Script") continue;
                        EditorGUILayout.PropertyField(prop, true);
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        so.ApplyModifiedProperties();
                        settings.SaveNow();
                        ProductManagerEditor.SyncConfig();
                    }

                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("Reports go to", settings.ReportsRootAbsolute, EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Open Inbox window")) InboxWindow.Open();
                        if (GUILayout.Button("Install Claude Code skill (/pm)")) ClaudeSkillInstaller.Install();
                        if (GUILayout.Button("Reveal reports folder")) ProductManagerMenu.RevealReportsFolder();
                    }
                }
                GUILayout.Space(10);
            }
        }
    }
}
