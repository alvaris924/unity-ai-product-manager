using System.IO;
using UnityEditor;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>Project-wide settings, stored in ProjectSettings/AiProductManagerSettings.asset (Project Settings > AI Product Manager).</summary>
    [FilePath("ProjectSettings/AiProductManagerSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class AiProductManagerSettings : ScriptableSingleton<AiProductManagerSettings>
    {
        [Tooltip("Install the in-game popup when Play mode starts.")]
        public bool enableInPlayMode = true;

        [Tooltip("Folder that receives Inbox/ and Resolved/. Relative to the project root, or absolute.")]
        public string reportsFolder = "AIProductManager";

        [Tooltip("What to copy when a report is filed: a short summary with the report path (paste it into Claude Code), the whole report, or nothing.")]
        public ClipboardContent clipboard = ClipboardContent.Summary;

        [Tooltip("List each script's serialized field values in the report.")]
        public bool includeSerializedFields = true;

        [Tooltip("Open the Inbox window after a report is filed.")]
        public bool openInboxAfterReport;

        [Header("Scene view trigger (Edit mode)")]
        [Tooltip("Mouse button that opens the report menu over the object under the pointer in the Scene view.")]
        public PointerButton sceneViewButton = PointerButton.Right;
        public bool sceneViewNeedsCtrl = true;
        public bool sceneViewNeedsAlt;
        public bool sceneViewNeedsShift;

        [Header("Game view popup (Play mode)")]
        public ProductManagerConfig runtime = new ProductManagerConfig();

        public static string ProjectRoot => Path.GetDirectoryName(Path.GetFullPath(Application.dataPath));

        public string ReportsRootAbsolute
        {
            get
            {
                var folder = string.IsNullOrWhiteSpace(reportsFolder) ? "AIProductManager" : reportsFolder.Trim();
                return Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(ProjectRoot, folder));
            }
        }

        /// <summary>The reports folder as the project sees it (relative when it lives inside the project).</summary>
        public string ReportsFolderForDocs
        {
            get
            {
                var root = ReportsRootAbsolute;
                var project = ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return root.StartsWith(project, System.StringComparison.OrdinalIgnoreCase) ? root.Substring(project.Length).Replace('\\', '/') : root;
            }
        }

        public ProductManagerConfig BuildRuntimeConfig()
        {
            var config = (runtime ?? new ProductManagerConfig()).Clone();
            config.reportsRoot = ReportsRootAbsolute;
            config.clipboard = clipboard;
            return config;
        }

        public void SaveNow() => Save(true);
    }
}
