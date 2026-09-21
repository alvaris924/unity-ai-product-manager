using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>
    /// Wires the runtime side to the Editor: installs the Game view overlay when Play mode starts, adds Editor-only
    /// facts to every report (prefab, script paths, serialized fields, git branch) and shows where the report went.
    /// Subscriptions are idempotent because Enter Play Mode may skip the domain reload.
    /// </summary>
    [InitializeOnLoad]
    public static class ProductManagerEditor
    {
        static ProductManagerEditor()
        {
            LogBuffer.Install();
            ProductManager.Enriching -= Enrich;
            ProductManager.Enriching += Enrich;
            ProductManager.Filed -= OnFiled;
            ProductManager.Filed += OnFiled;
            ProductManager.DetailedReportHandler = OpenReportWindow;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SceneViewReporter.Hook();
            // Settings are loaded lazily: the asset database is not ready inside a static constructor.
            EditorApplication.delayCall += () =>
            {
                SyncConfig();
                if (EditorApplication.isPlaying) InstallOverlayIfEnabled();
            };
        }

        /// <summary>Pushes the current settings into ProductManager.Config (used by the overlay and the report builder).</summary>
        public static void SyncConfig()
        {
            ProductManager.Config = AiProductManagerSettings.instance.BuildRuntimeConfig();
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    InstallOverlayIfEnabled();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    ProductManagerOverlay.Uninstall();
                    break;
            }
        }

        public static void InstallOverlayIfEnabled()
        {
            var settings = AiProductManagerSettings.instance;
            if (!settings.enableInPlayMode || !Application.isPlaying) return;
            ProductManagerOverlay.Install(settings.BuildRuntimeConfig());
        }

        static bool OpenReportWindow(IssueReport report)
        {
            ReportWindow.OpenWith(report);
            return true;
        }

        static void OnFiled(IssueReport report)
        {
            var settings = AiProductManagerSettings.instance;
            if (!Application.isPlaying || ProductManagerOverlay.Instance == null)
            {
                string message = "Report #" + report.Number.ToString("0000") + " saved" + (settings.clipboard == ClipboardContent.Nothing ? "" : " — summary copied, paste it into Claude Code");
                var window = EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow : EditorWindow.mouseOverWindow;
                if (window == null) window = SceneView.lastActiveSceneView;
                if (window != null) window.ShowNotification(new GUIContent(message), 4);
            }
            InboxWindow.RefreshIfOpen();
            if (settings.openInboxAfterReport) InboxWindow.Open();
        }

        // ------------------------------------------------------------------ enrichment

        static void Enrich(IssueReport report)
        {
            var settings = AiProductManagerSettings.instance;
            report.ProjectName = Path.GetFileName(AiProductManagerSettings.ProjectRoot);
            report.GitBranch = ReadGitBranch(AiProductManagerSettings.ProjectRoot);
            if (!Application.isPlaying)
            {
                var size = Handles.GetMainGameViewSize();
                if (size.x > 0 && size.y > 0) report.ScreenSize = new Vector2Int((int)size.x, (int)size.y);
            }

            var section = report.Section("Editor details");
            var go = report.Target;
            if (go == null)
            {
                section.Line("_no target_");
                return;
            }

            var stage = PrefabStageUtility.GetPrefabStage(go);
            if (stage != null)
            {
                section.Line("- Editing prefab asset `" + stage.assetPath + "` (Prefab mode)");
                AddAsset(report, stage.assetPath);
            }
            else if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                bool overrides = root != null && PrefabUtility.HasPrefabInstanceAnyOverrides(root, false);
                section.Line("- Prefab instance of `" + assetPath + "` (instance root `" + (root != null ? root.name : "?") + "`" + (overrides ? ", has overrides" : "") + ")");
                AddAsset(report, assetPath);
            }
            else
            {
                section.Line("- Plain scene object (not a prefab instance)");
            }
            if (!string.IsNullOrEmpty(report.ScenePath)) AddAsset(report, report.ScenePath);

            section.Line("- Scripts on the target and its ancestors" + (settings.includeSerializedFields ? " (serialized fields as currently set):" : ":"));
            int scripts = 0;
            for (var t = go.transform; t != null; t = t.parent)
            {
                foreach (var behaviour in t.GetComponents<MonoBehaviour>())
                {
                    if (behaviour == null) continue;
                    var type = behaviour.GetType();
                    if (ReportBuilder.IsUnityType(type)) continue;
                    scripts++;
                    var script = MonoScript.FromMonoBehaviour(behaviour);
                    var path = script != null ? AssetDatabase.GetAssetPath(script) : null;
                    string where = t == go.transform ? "on the target" : "on ancestor `" + t.name + "`";
                    section.Line("  - **" + type.Name + "** (" + where + ") — " + (string.IsNullOrEmpty(path) ? "script path unknown" : "`" + path + "`"));
                    if (!string.IsNullOrEmpty(path) && !report.ScriptPaths.Contains(path)) report.ScriptPaths.Add(path);
                    if (settings.includeSerializedFields) AppendSerializedFields(section, behaviour);
                }
            }
            if (scripts == 0) section.Line("  - none (only engine components)");
        }

        static void AddAsset(IssueReport report, string path)
        {
            if (!string.IsNullOrEmpty(path) && !report.AssetPaths.Contains(path)) report.AssetPaths.Add(path);
        }

        static void AppendSerializedFields(ReportSection section, MonoBehaviour behaviour)
        {
            SerializedObject so;
            try { so = new SerializedObject(behaviour); }
            catch { return; }
            var prop = so.GetIterator();
            bool enterChildren = true;
            int count = 0;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.name == "m_Script") continue;
                if (++count > 40)
                {
                    section.Line("      … more fields omitted");
                    break;
                }
                section.Line("      " + prop.name + ": " + Describe(prop));
            }
        }

        static string Describe(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                {
                    var o = p.objectReferenceValue;
                    if (o == null) return "None ⚠";
                    return o is Component c ? c.gameObject.name + " (" + o.GetType().Name + ")" : o.name + " (" + o.GetType().Name + ")";
                }
                case SerializedPropertyType.Integer: return p.longValue.ToString();
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float: return p.doubleValue.ToString("0.###");
                case SerializedPropertyType.String: return "\"" + ReportBuilder.Trunc(p.stringValue, 80) + "\"";
                case SerializedPropertyType.Enum:
                {
                    var names = p.enumDisplayNames;
                    return p.enumValueIndex >= 0 && p.enumValueIndex < names.Length ? names[p.enumValueIndex] : p.intValue.ToString();
                }
                case SerializedPropertyType.Color: return "#" + ColorUtility.ToHtmlStringRGBA(p.colorValue);
                case SerializedPropertyType.Vector2: return p.vector2Value.ToString("0.##");
                case SerializedPropertyType.Vector3: return p.vector3Value.ToString("0.##");
                case SerializedPropertyType.Vector4: return p.vector4Value.ToString("0.##");
                case SerializedPropertyType.Vector2Int: return p.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int: return p.vector3IntValue.ToString();
                case SerializedPropertyType.Rect: return p.rectValue.ToString("0.##");
                case SerializedPropertyType.Bounds: return p.boundsValue.ToString("0.##");
                case SerializedPropertyType.Quaternion: return p.quaternionValue.eulerAngles.ToString("0.##") + " (euler)";
                case SerializedPropertyType.LayerMask: return "mask " + p.intValue;
                case SerializedPropertyType.AnimationCurve: return "curve";
                case SerializedPropertyType.Gradient: return "gradient";
                case SerializedPropertyType.ManagedReference: return string.IsNullOrEmpty(p.managedReferenceFullTypename) ? "null" : "{" + p.managedReferenceFullTypename + "}";
                case SerializedPropertyType.Generic: return p.isArray ? "[" + p.arraySize + " items]" : "{…}";
                default: return p.propertyType.ToString();
            }
        }

        static string ReadGitBranch(string projectRoot)
        {
            try
            {
                for (var dir = new DirectoryInfo(projectRoot); dir != null; dir = dir.Parent)
                {
                    var head = Path.Combine(dir.FullName, ".git", "HEAD");
                    if (!File.Exists(head)) continue;
                    var text = File.ReadAllText(head).Trim();
                    const string prefix = "ref: refs/heads/";
                    return text.StartsWith(prefix) ? text.Substring(prefix.Length) : text.Substring(0, Mathf.Min(8, text.Length));
                }
            }
            catch { }
            return "";
        }
    }
}
