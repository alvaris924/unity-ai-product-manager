using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>
    /// Copies the package's Claude Code skill into the project as .claude/skills/pm/SKILL.md, so "/pm" in Claude Code
    /// knows where the reports are and how to work through them.
    /// </summary>
    public static class ClaudeSkillInstaller
    {
        public static string PackageRoot
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ClaudeSkillInstaller).Assembly);
                return info != null ? info.resolvedPath : null;
            }
        }

        public static string SkillTargetPath => Path.Combine(AiProductManagerSettings.ProjectRoot, ".claude", "skills", "pm", "SKILL.md");

        public static void Install()
        {
            var root = PackageRoot;
            if (string.IsNullOrEmpty(root))
            {
                Debug.LogError("[AI PM] Cannot locate the AI Product Manager package folder.");
                return;
            }
            var source = Path.Combine(root, "ClaudeCode~", "skills", "pm", "SKILL.md");
            if (!File.Exists(source))
            {
                Debug.LogError("[AI PM] Skill template not found: " + source);
                return;
            }
            var settings = AiProductManagerSettings.instance;
            var text = File.ReadAllText(source).Replace("{{REPORTS_FOLDER}}", settings.ReportsFolderForDocs);
            var target = SkillTargetPath;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, text, new UTF8Encoding(false));
            Debug.Log("[AI PM] Installed the Claude Code skill at " + target + ". In Claude Code (opened in the project folder) type /pm, /pm watch, or just paste a report summary.");
            var window = EditorWindow.focusedWindow;
            if (window != null) window.ShowNotification(new GUIContent("Claude Code skill installed: /pm"), 3);
        }
    }
}
