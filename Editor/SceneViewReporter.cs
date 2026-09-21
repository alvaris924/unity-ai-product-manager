using UnityEditor;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>
    /// Edit-mode entry point: Ctrl+right-click (configurable) on an object in the Scene view shows the category menu
    /// for it, its parents, or the exact graphic under the pointer.
    /// </summary>
    static class SceneViewReporter
    {
        static bool hooked;
        static bool swallowContextClick;

        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
        }

        static void OnSceneGui(SceneView sceneView)
        {
            var e = Event.current;
            if (swallowContextClick && (e.type == EventType.ContextClick || e.type == EventType.MouseUp))
            {
                swallowContextClick = e.type != EventType.ContextClick;
                e.Use();
                return;
            }
            if (e.type != EventType.MouseDown) return;
            swallowContextClick = false;
            var settings = AiProductManagerSettings.instance;
            if (e.button != (int)settings.sceneViewButton || e.control != settings.sceneViewNeedsCtrl || e.alt != settings.sceneViewNeedsAlt || e.shift != settings.sceneViewNeedsShift) return;

            var picked = HandleUtility.PickGameObject(e.mousePosition, false);
            swallowContextClick = true;
            e.Use();
            ShowMenu(picked);
        }

        static void ShowMenu(GameObject picked)
        {
            var interactive = picked != null ? UiPicker.InteractiveNear(picked) : null;
            var target = interactive != null ? interactive : picked;
            var menu = new GenericMenu();
            menu.AddDisabledItem(new GUIContent(target != null ? "Report: " + target.name : "Report: nothing under the pointer"));
            menu.AddSeparator("");
            AddCategories(menu, "", target, picked);
            if (target != null)
            {
                menu.AddSeparator("");
                if (picked != null && picked != target) AddCategories(menu, "Pointed graphic '" + Escape(picked.name) + "'/", picked, picked);
                int n = 0;
                for (var t = target.transform.parent; t != null && n < 6; t = t.parent, n++)
                    AddCategories(menu, "Parent '" + Escape(t.name) + "'/", t.gameObject, picked);
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Add notes... (open window)"), false, () => ReportWindow.Open(target, picked));
            menu.ShowAsContext();
        }

        static void AddCategories(GenericMenu menu, string prefix, GameObject target, GameObject hit)
        {
            var categories = AiProductManagerSettings.instance.runtime.categories ?? ProductManagerConfig.DefaultCategories();
            foreach (var category in categories)
            {
                var chosen = category;
                menu.AddItem(new GUIContent(prefix + Escape(category)), false, () => EditorReportFlow.QuickReport(target, hit, chosen));
            }
        }

        // A slash would create a submenu.
        static string Escape(string s) => s.Replace("/", "∕");
    }
}
