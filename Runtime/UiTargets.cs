using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Alvaris.AiProductManager
{
    /// <summary>
    /// What a finger could press right now: every active Selectable and pointer handler under an active canvas, with
    /// its screen rect, visible text and whether something else would take a tap at its centre. Lets a playtest name
    /// its targets ("Play", "Shop/BuyButton") instead of guessing coordinates.
    /// </summary>
    public static class UiTargets
    {
        public sealed class Element
        {
            public GameObject GameObject;
            public string Name;
            public string Path;
            public string Text;
            /// <summary>Selectable type (Button, Toggle…) or the first pointer handler's type.</summary>
            public string Kind;
            /// <summary>Screen pixels, origin bottom-left.</summary>
            public Rect ScreenRect;
            public bool Interactable;
            /// <summary>What the EventSystem would hit at the centre; null when nothing is there.</summary>
            public GameObject TopAtCentre;
            /// <summary>A tap at the centre would reach another control (or nothing) instead of this one.</summary>
            public bool Blocked;

            public Vector2 Centre => ScreenRect.center;
        }

        /// <summary>
        /// Every pressable element on screen, top to bottom then left to right. <paramref name="filter"/> keeps those
        /// whose name, path or text contains it (case-insensitive).
        /// </summary>
        public static List<Element> List(string filter = null)
        {
            var found = new List<Element>();
            var seen = new HashSet<GameObject>();
            var screen = new Rect(0, 0, Screen.width, Screen.height);

            foreach (var selectable in Selectable.allSelectablesArray)
            {
                if (selectable == null || !selectable.isActiveAndEnabled) continue;
                Consider(selectable.gameObject, selectable.GetType().Name, selectable.IsInteractable(), screen, seen, found);
            }

            var roots = new HashSet<Canvas>();
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (canvas != null && canvas.isActiveAndEnabled) roots.Add(canvas.rootCanvas != null ? canvas.rootCanvas : canvas);
            foreach (var root in roots)
            {
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(false))
                {
                    if (behaviour == null || !behaviour.isActiveAndEnabled || behaviour is Selectable) continue;
                    if (!(behaviour is IPointerClickHandler) && !(behaviour is IPointerDownHandler) && !(behaviour is IDragHandler)) continue;
                    Consider(behaviour.gameObject, behaviour.GetType().Name, true, screen, seen, found);
                }
            }

            if (!string.IsNullOrEmpty(filter))
                found.RemoveAll(e => !Contains(e.Name, filter) && !Contains(e.Path, filter) && !Contains(e.Text, filter));
            found.Sort((a, b) =>
            {
                int byRow = b.ScreenRect.yMax.CompareTo(a.ScreenRect.yMax);
                return Mathf.Abs(a.ScreenRect.yMax - b.ScreenRect.yMax) > 8f ? byRow : a.ScreenRect.xMin.CompareTo(b.ScreenRect.xMin);
            });
            return found;
        }

        /// <summary>
        /// Finds the element a query names: an exact hierarchy path, then an exact name, then exact visible text, then
        /// a name or text containing it. Among equals the first unblocked, interactable one wins. False when nothing matches.
        /// </summary>
        public static bool TryResolve(string query, out Element element, out List<Element> matches)
        {
            element = null;
            matches = new List<Element>();
            if (string.IsNullOrWhiteSpace(query)) return false;
            query = query.Trim();
            var all = List();
            var rules = new Func<Element, bool>[]
            {
                e => string.Equals(e.Path, query, StringComparison.OrdinalIgnoreCase) || (query.Contains("/") && e.Path.EndsWith("/" + query, StringComparison.OrdinalIgnoreCase)),
                e => string.Equals(e.Name, query, StringComparison.OrdinalIgnoreCase),
                e => string.Equals(e.Text, query, StringComparison.OrdinalIgnoreCase),
                e => Contains(e.Name, query) || Contains(e.Text, query),
            };
            foreach (var rule in rules)
            {
                matches = all.FindAll(e => rule(e));
                if (matches.Count == 0) continue;
                element = matches.Find(e => e.Interactable && !e.Blocked) ?? matches.Find(e => e.Interactable) ?? matches[0];
                return true;
            }
            return false;
        }

        /// <summary>The first non-empty text on the object or below it: a uGUI Text, or TextMeshPro read by reflection.</summary>
        public static string ReadText(GameObject go)
        {
            if (go == null) return "";
            foreach (var component in go.GetComponentsInChildren<Component>(false))
            {
                if (component == null) continue;
                string text = null;
                if (component is Text legacy) text = legacy.text;
                else if (component.GetType().FullName != null && component.GetType().FullName.StartsWith("TMPro.", StringComparison.Ordinal))
                {
                    var prop = component.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                    try { text = prop?.GetValue(component) as string; }
                    catch { text = null; }
                }
                if (!string.IsNullOrWhiteSpace(text)) return ReportBuilder.Trunc(text.Trim(), 60);
            }
            return "";
        }

        static void Consider(GameObject go, string kind, bool interactable, Rect screen, HashSet<GameObject> seen, List<Element> found)
        {
            if (go == null || !seen.Add(go)) return;
            if (!UiPicker.TryGetScreenRect(go, out var rect)) return;
            if (rect.width < 1f || rect.height < 1f || !rect.Overlaps(screen)) return;
            var visible = Rect.MinMaxRect(Mathf.Max(rect.xMin, 0), Mathf.Max(rect.yMin, 0), Mathf.Min(rect.xMax, screen.width), Mathf.Min(rect.yMax, screen.height));
            var hits = UiPicker.EventSystemRaycast(visible.center);
            var top = hits.Count > 0 ? hits[0].gameObject : null;
            bool reaches = top != null && (top == go || top.transform.IsChildOf(go.transform) || UiPicker.InteractiveAncestor(top) == go);
            found.Add(new Element
            {
                GameObject = go,
                Name = go.name,
                Path = ReportBuilder.HierarchyPath(go.transform),
                Text = ReadText(go),
                Kind = kind,
                ScreenRect = visible,
                Interactable = interactable,
                TopAtCentre = top,
                Blocked = !reaches,
            });
        }

        static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
