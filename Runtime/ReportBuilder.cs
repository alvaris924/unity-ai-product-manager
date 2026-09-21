using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Alvaris.AiProductManager
{
    /// <summary>
    /// Turns "this object, this complaint" into a report with everything a developer or an AI agent needs to start
    /// fixing: the target, its ancestors, pointer diagnostics, findings and recent console output.
    /// Editor-only facts (script paths, prefab, serialized fields) are added by the Editor through ProductManager.Enriching.
    /// </summary>
    public static class ReportBuilder
    {
        const int MaxAncestors = 25;
        const int MaxStack = 12;

        /// <param name="target">The object the report is about (may be null: "the screen here").</param>
        /// <param name="hit">The graphic under the pointer, when it differs from the target.</param>
        /// <param name="pointerScreenPos">Pointer position in pixels (origin bottom-left), or null when filed from the Hierarchy / Scene view.</param>
        /// <param name="graphicHits">Visual hit stack at the pointer; computed at the target's centre when null.</param>
        /// <param name="eventSystemHits">EventSystem hit stack at the pointer; computed at the target's centre when null.</param>
        public static IssueReport Build(GameObject target, GameObject hit, string category, string notes,
            Vector2? pointerScreenPos, List<UiPicker.Hit> graphicHits, List<RaycastResult> eventSystemHits)
        {
            var config = ProductManager.Config;
            var report = new IssueReport
            {
                Category = category ?? "",
                Notes = notes ?? "",
                Target = target,
                Hit = hit != null ? hit : target,
                IsPlayMode = Application.isPlaying,
                PointerScreenPos = pointerScreenPos,
                ScreenSize = new Vector2Int(Screen.width, Screen.height),
            };
            if (report.IsPlayMode) report.PlayTime = Time.unscaledTime;

            var scene = target != null && target.scene.IsValid() ? target.scene : SceneManager.GetActiveScene();
            report.SceneName = scene.name;
            report.ScenePath = scene.path;

            if (target != null)
            {
                report.TargetName = target.name;
                report.TargetPath = HierarchyPath(target.transform);
                if (UiPicker.TryGetScreenRect(target, out var rect)) report.TargetScreenRect = rect;
            }
            if (report.Hit != null && report.Hit != target)
            {
                report.HitPath = HierarchyPath(report.Hit.transform);
                report.HitDescription = BriefGraphic(report.Hit);
                report.HitRelation = HitRelation(report.Hit, target);
            }

            bool probed = false;
            if (pointerScreenPos == null && target != null && report.TargetScreenRect.HasValue)
            {
                var centre = report.TargetScreenRect.Value.center;
                report.ProbePosition = centre;
                probed = true;
                if (graphicHits == null) graphicHits = UiPicker.PickGraphics(centre);
                if (eventSystemHits == null) eventSystemHits = UiPicker.EventSystemRaycast(centre);
            }

            AppendTarget(report, target);
            AppendAncestors(report, target);
            AppendFindings(report, target, eventSystemHits, config);
            AppendPointer(report, target, graphicHits, eventSystemHits, pointerScreenPos ?? report.ProbePosition, probed);
            AppendConsole(report, config);
            return report;
        }

        // ------------------------------------------------------------------ sections

        static void AppendTarget(IssueReport r, GameObject go)
        {
            var s = r.Section("Target");
            if (go == null)
            {
                s.Line("_Nothing was under the pointer; the report is about the screen at that position._");
                return;
            }
            s.Line("`" + r.TargetPath + "`");
            string active = go.activeInHierarchy ? "yes" : go.activeSelf ? "no (a parent is inactive)" : "no (inactive itself)";
            s.Line("- Active: " + active + " · Layer: " + LayerMask.LayerToName(go.layer) + " · Tag: " + go.tag);
            if (go.transform is RectTransform rt) s.Line("- " + DescribeRectTransform(rt));
            else s.Line("- Transform: position " + V(go.transform.position) + " · localScale " + S(go.transform.localScale));
            if (r.TargetScreenRect.HasValue)
            {
                var sr = r.TargetScreenRect.Value;
                bool onScreen = sr.xMax > 0 && sr.yMax > 0 && sr.xMin < Screen.width && sr.yMin < Screen.height;
                s.Line("- Screen rect: x " + F(sr.xMin) + "–" + F(sr.xMax) + ", y " + F(sr.yMin) + "–" + F(sr.yMax)
                       + " (pixels, origin bottom-left, screen " + Screen.width + "×" + Screen.height + ")" + (onScreen ? "" : " — ⚠ off-screen"));
                if (!onScreen) r.Findings.Add("⚠ The target is completely off-screen.");
            }
            s.Line("- Components:");
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null)
                {
                    s.Line("  - ⚠ Missing script (null component)");
                    r.Findings.Add("⚠ The target has a missing script component.");
                    continue;
                }
                if (c is Transform || c is CanvasRenderer) continue;
                s.Line("  - " + DescribeComponent(c));
            }
        }

        static void AppendAncestors(IssueReport r, GameObject go)
        {
            if (go == null) return;
            var s = r.Section("Ancestors (nearest first)");
            var t = go.transform.parent;
            if (t == null)
            {
                s.Line("_none: the target is a root object_");
                return;
            }
            int n = 0;
            while (t != null && n++ < MaxAncestors)
            {
                var parts = new List<string>();
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) { parts.Add("⚠ Missing script"); continue; }
                    if (c is Transform || c is CanvasRenderer) continue;
                    parts.Add(DescribeComponent(c));
                }
                string flags = (t.gameObject.activeSelf ? "" : " (inactive)") + (t.localScale == Vector3.one ? "" : " scale " + S(t.localScale));
                s.Line("- `" + t.name + "`" + flags + (parts.Count > 0 ? " — " + string.Join("; ", parts) : ""));
                t = t.parent;
            }
            if (t != null) s.Line("- …");
        }

        static void AppendFindings(IssueReport r, GameObject target, List<RaycastResult> eventSystemHits, ProductManagerConfig config)
        {
            var findings = r.Findings;
            if (target != null)
            {
                if (!target.activeInHierarchy)
                    findings.Add(target.activeSelf ? "⚠ The target is inactive because a parent is inactive." : "⚠ The target GameObject is inactive.");

                var selectable = target.GetComponent<Selectable>();
                if (selectable != null)
                {
                    string kind = selectable.GetType().Name;
                    if (!selectable.enabled) findings.Add("⚠ The " + kind + " component is disabled.");
                    if (!selectable.interactable) findings.Add("⚠ " + kind + ".interactable is false.");
                    else if (!selectable.IsInteractable()) findings.Add("⚠ A CanvasGroup above the target has interactable = false, so the " + kind + " ignores input.");
                    var ev = PrimaryEvent(selectable, out var eventName);
                    if (ev != null && ListenerCount(ev) == 0)
                    {
                        if (Application.isPlaying)
                            findings.Add("⚠ " + kind + "." + eventName + " has no listeners (no persistent calls and nothing added at runtime).");
                        else
                            findings.Add("ℹ " + kind + "." + eventName + " has no persistent listeners; listeners added from code only show up in a Play-mode report.");
                    }

                    var graphics = target.GetComponentsInChildren<Graphic>(false);
                    bool anyRaycast = false;
                    foreach (var g in graphics) if (g.raycastTarget && g.isActiveAndEnabled) { anyRaycast = true; break; }
                    if (!anyRaycast) findings.Add("⚠ No active Graphic under the target has raycastTarget enabled, so the pointer cannot hit this control.");
                }

                foreach (var group in target.GetComponentsInParent<CanvasGroup>(true))
                {
                    if (!group.blocksRaycasts) findings.Add("⚠ CanvasGroup on `" + group.name + "` has blocksRaycasts = false: pointer events pass through this subtree.");
                    if (group.alpha <= 0.001f) findings.Add("⚠ CanvasGroup on `" + group.name + "` has alpha 0: this subtree is invisible.");
                }

                var canvas = target.GetComponentInParent<Canvas>(true);
                if (canvas == null)
                {
                    if (target.transform is RectTransform) findings.Add("⚠ The target is not under a Canvas, so it is not rendered as UI.");
                }
                else
                {
                    var root = canvas.rootCanvas;
                    if (!root.isActiveAndEnabled) findings.Add("⚠ Root canvas `" + root.name + "` is disabled or inactive.");
                    var raycaster = root.GetComponent<GraphicRaycaster>();
                    if (raycaster == null) findings.Add("⚠ Root canvas `" + root.name + "` has no GraphicRaycaster: nothing in it can receive pointer events.");
                    else if (!raycaster.enabled) findings.Add("⚠ The GraphicRaycaster on `" + root.name + "` is disabled.");
                    if (root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera == null)
                        findings.Add("⚠ Root canvas `" + root.name + "` is Screen Space - Camera but has no camera assigned.");
                }

                for (var t = target.transform; t != null; t = t.parent)
                {
                    if (Mathf.Approximately(t.localScale.x, 0f) || Mathf.Approximately(t.localScale.y, 0f))
                    {
                        findings.Add("⚠ `" + t.name + "` has a zero scale " + S(t.localScale) + ".");
                        break;
                    }
                }
            }

            if (Application.isPlaying)
            {
                var eventSystem = EventSystem.current;
                if (eventSystem == null) findings.Add("⚠ There is no active EventSystem in the scene: no UI receives pointer input.");
                else if (eventSystem.currentInputModule == null) findings.Add("⚠ The EventSystem has no active input module.");

                if (target != null && eventSystemHits != null)
                {
                    if (eventSystemHits.Count == 0)
                    {
                        if (eventSystem != null) findings.Add("⚠ The EventSystem raycast found nothing at that point (no raycastTarget graphics or raycasters there).");
                    }
                    else
                    {
                        var top = eventSystemHits[0].gameObject;
                        if (top != null)
                        {
                            if (top == target || top.transform.IsChildOf(target.transform))
                                findings.Add("✔ The pointer reaches the target (topmost EventSystem hit is `" + HierarchyPath(top.transform) + "`).");
                            else
                                findings.Add("⚠ The pointer is blocked: the topmost object under the pointer is `" + HierarchyPath(top.transform) + "`"
                                             + (eventSystemHits[0].module != null ? " (" + eventSystemHits[0].module.GetType().Name + ")" : "") + ", not the target.");
                        }
                    }
                }
            }

            int errors = 0;
            foreach (var e in LogBuffer.Recent(config.consoleLogSeconds, 200)) if (e.IsError) errors++;
            if (errors > 0) findings.Add("⚠ " + errors + " error(s)/exception(s) in the console during the last " + config.consoleLogSeconds + " s — see Recent console.");

            if (findings.Count == 0)
                findings.Add("✔ No obvious blocker in the component state; look at the scripts, the notes and the screenshot.");
        }

        static void AppendPointer(IssueReport r, GameObject target, List<UiPicker.Hit> graphicHits, List<RaycastResult> eventSystemHits, Vector2? pos, bool probed)
        {
            var s = r.Section("Pointer diagnostics");
            if (probed) s.Line("_No click position: the stacks below were probed at the target's centre._");
            if (!Application.isPlaying)
            {
                s.Line("- Edit mode: the EventSystem does not run, so only the visual stack is available.");
            }
            else
            {
                var es = EventSystem.current;
                if (es == null) s.Line("- EventSystem: **none**");
                else
                {
                    string module = es.currentInputModule != null ? es.currentInputModule.GetType().Name : "none";
                    string selected = es.currentSelectedGameObject != null ? "`" + HierarchyPath(es.currentSelectedGameObject.transform) + "`" : "nothing";
                    s.Line("- EventSystem: `" + es.name + "` — input module " + module + ", currently selected: " + selected);
                }
                if (pos.HasValue) s.Line("- Position: (" + F(pos.Value.x) + ", " + F(pos.Value.y) + ") on " + Screen.width + "×" + Screen.height);
                if (eventSystemHits != null)
                {
                    s.Line("- EventSystem raycast, topmost first (" + eventSystemHits.Count + " hits):");
                    int i = 0;
                    foreach (var h in eventSystemHits)
                    {
                        if (h.gameObject == null) continue;
                        if (++i > MaxStack) { s.Line("  - …"); break; }
                        string marker = target == null ? "" : h.gameObject == target ? " ← target" : h.gameObject.transform.IsChildOf(target.transform) ? " ← inside target" : "";
                        s.Line("  " + i + ". `" + HierarchyPath(h.gameObject.transform) + "` — " + (h.module != null ? h.module.GetType().Name : "?") + marker);
                    }
                    if (i == 0) s.Line("  - nothing");
                }
            }
            if (graphicHits != null)
            {
                s.Line("- Graphics at that point in visual order, topmost first (" + graphicHits.Count + "):");
                int i = 0;
                foreach (var h in graphicHits)
                {
                    if (h.GameObject == null) continue;
                    if (++i > MaxStack) { s.Line("  - …"); break; }
                    string marker = target == null ? "" : h.GameObject == target ? " ← target" : h.GameObject.transform.IsChildOf(target.transform) ? " ← inside target" : "";
                    s.Line("  " + i + ". `" + HierarchyPath(h.GameObject.transform) + "` — " + h.Graphic.GetType().Name
                           + ", raycastTarget " + YN(h.RaycastTarget) + ", alpha " + h.EffectiveAlpha.ToString("0.00")
                           + (h.RaycastTarget && !h.PassesRaycastFilters ? ", filtered out by a mask/CanvasGroup" : "") + marker);
                }
                if (i == 0) s.Line("  - nothing");
            }
        }

        static void AppendConsole(IssueReport r, ProductManagerConfig config)
        {
            var s = r.Section("Recent console (last " + config.consoleLogSeconds + " s, errors first)");
            var entries = LogBuffer.Recent(config.consoleLogSeconds, Mathf.Max(1, config.maxConsoleEntries));
            if (entries.Count == 0)
            {
                s.Line("_nothing logged_");
                return;
            }
            foreach (var e in entries)
            {
                string message = e.Message ?? "";
                s.Line("- [" + e.Type + " " + e.Time.ToString("HH:mm:ss") + " f" + e.Frame + "] " + Trunc(message.Replace("\r", "").Replace("\n", " ⏎ "), e.IsError ? 600 : 300));
                if (e.IsError && !string.IsNullOrEmpty(e.StackTrace))
                {
                    var lines = e.StackTrace.Replace("\r", "").Split('\n');
                    int shown = 0;
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (++shown > 6) { s.Line("      …"); break; }
                        s.Line("      " + line.Trim());
                    }
                }
            }
        }

        // ------------------------------------------------------------------ component descriptions

        /// <summary>One line about a component: type, state and the properties that matter for UI bugs.</summary>
        public static string DescribeComponent(Component c)
        {
            if (c == null) return "⚠ Missing script";
            string name = c.GetType().Name;
            string state = c is Behaviour b && !b.enabled ? " (disabled)" : "";
            try
            {
                switch (c)
                {
                    case Image img:
                        return "Image" + state + " — sprite " + Name(img.sprite) + ", type " + img.type
                               + (img.type == Image.Type.Filled ? " fill " + img.fillAmount.ToString("0.00") : "")
                               + ", color " + Col(img.color) + ", raycastTarget " + YN(img.raycastTarget) + ", maskable " + YN(img.maskable);
                    case RawImage raw:
                        return "RawImage" + state + " — texture " + Name(raw.texture) + ", color " + Col(raw.color) + ", raycastTarget " + YN(raw.raycastTarget);
                    case Text text:
                        return "Text" + state + " — \"" + Trunc(text.text, 80) + "\", font " + Name(text.font) + " " + text.fontSize + "px, color " + Col(text.color) + ", raycastTarget " + YN(text.raycastTarget);
                    case Button button:
                        return "Button" + state + " — " + Interactable(button) + ", transition " + button.transition + ", navigation " + button.navigation.mode
                               + ", onClick: " + DescribeEvent(button.onClick);
                    case Toggle toggle:
                        return "Toggle" + state + " — isOn " + YN(toggle.isOn) + ", " + Interactable(toggle) + ", group " + Name(toggle.group)
                               + ", onValueChanged: " + DescribeEvent(toggle.onValueChanged);
                    case Slider slider:
                        return "Slider" + state + " — value " + slider.value.ToString("0.###") + " in [" + slider.minValue + ", " + slider.maxValue + "]"
                               + (slider.wholeNumbers ? " whole" : "") + ", " + Interactable(slider) + ", onValueChanged: " + DescribeEvent(slider.onValueChanged);
                    case Scrollbar scrollbar:
                        return "Scrollbar" + state + " — value " + scrollbar.value.ToString("0.###") + ", size " + scrollbar.size.ToString("0.###") + ", " + Interactable(scrollbar);
                    case Dropdown dropdown:
                        return "Dropdown" + state + " — value " + dropdown.value + " of " + dropdown.options.Count + " options, " + Interactable(dropdown)
                               + ", onValueChanged: " + DescribeEvent(dropdown.onValueChanged);
                    case InputField input:
                        return "InputField" + state + " — text \"" + Trunc(input.text, 60) + "\", " + Interactable(input) + ", onEndEdit: " + DescribeEvent(input.onEndEdit);
                    case Selectable selectable:
                        return name + state + " — " + Interactable(selectable) + ", transition " + selectable.transition + Joined(ReflectedText(c));
                    case ScrollRect scroll:
                        return "ScrollRect" + state + " — horizontal " + YN(scroll.horizontal) + ", vertical " + YN(scroll.vertical) + ", content " + Name(scroll.content)
                               + ", normalizedPosition " + V(scroll.normalizedPosition);
                    case CanvasGroup group:
                        return "CanvasGroup" + state + " — alpha " + group.alpha.ToString("0.00") + ", interactable " + YN(group.interactable)
                               + ", blocksRaycasts " + YN(group.blocksRaycasts) + ", ignoreParentGroups " + YN(group.ignoreParentGroups);
                    case Canvas canvas:
                        return "Canvas" + state + " — " + canvas.renderMode
                               + (canvas.renderMode != RenderMode.ScreenSpaceOverlay ? ", camera " + Name(canvas.worldCamera) : "")
                               + ", sortingLayer " + canvas.sortingLayerName + ", sortingOrder " + canvas.sortingOrder + (canvas.overrideSorting ? " (override)" : "")
                               + ", scaleFactor " + canvas.scaleFactor.ToString("0.###") + ", pixelPerfect " + YN(canvas.pixelPerfect);
                    case CanvasScaler scaler:
                        return "CanvasScaler" + state + " — " + scaler.uiScaleMode + ", reference " + scaler.referenceResolution.x + "×" + scaler.referenceResolution.y
                               + ", " + scaler.screenMatchMode + " match " + scaler.matchWidthOrHeight.ToString("0.##") + ", scaleFactor " + scaler.scaleFactor.ToString("0.##");
                    case GraphicRaycaster raycaster:
                        return "GraphicRaycaster" + state + " — blockingObjects " + raycaster.blockingObjects + ", ignoreReversedGraphics " + YN(raycaster.ignoreReversedGraphics);
                    case HorizontalOrVerticalLayoutGroup layout:
                        return name + state + " — spacing " + layout.spacing + ", padding L" + layout.padding.left + " R" + layout.padding.right + " T" + layout.padding.top + " B" + layout.padding.bottom
                               + ", childAlignment " + layout.childAlignment + ", controlSize " + YN(layout.childControlWidth) + "/" + YN(layout.childControlHeight)
                               + ", forceExpand " + YN(layout.childForceExpandWidth) + "/" + YN(layout.childForceExpandHeight);
                    case GridLayoutGroup grid:
                        return "GridLayoutGroup" + state + " — cellSize " + V(grid.cellSize) + ", spacing " + V(grid.spacing) + ", constraint " + grid.constraint + " " + grid.constraintCount
                               + ", childAlignment " + grid.childAlignment + ", startCorner " + grid.startCorner;
                    case LayoutElement element:
                        return "LayoutElement" + state + " — ignoreLayout " + YN(element.ignoreLayout) + ", min " + element.minWidth + "×" + element.minHeight
                               + ", preferred " + element.preferredWidth + "×" + element.preferredHeight + ", flexible " + element.flexibleWidth + "×" + element.flexibleHeight;
                    case ContentSizeFitter fitter:
                        return "ContentSizeFitter" + state + " — horizontal " + fitter.horizontalFit + ", vertical " + fitter.verticalFit;
                    case AspectRatioFitter aspect:
                        return "AspectRatioFitter" + state + " — " + aspect.aspectMode + ", ratio " + aspect.aspectRatio.ToString("0.###");
                    case Mask mask:
                        return "Mask" + state + " — showMaskGraphic " + YN(mask.showMaskGraphic);
                    case RectMask2D rectMask:
                        return "RectMask2D" + state + " — padding " + rectMask.padding + ", softness " + rectMask.softness;
                    case EventTrigger trigger:
                    {
                        var parts = new List<string>();
                        if (trigger.triggers != null)
                            foreach (var entry in trigger.triggers) parts.Add(entry.eventID + " (" + DescribeEvent(entry.callback) + ")");
                        return "EventTrigger" + state + " — " + parts.Count + " entries" + (parts.Count > 0 ? ": " + string.Join(", ", parts) : "");
                    }
                    case Animator animator:
                        return "Animator" + state + " — controller " + Name(animator.runtimeAnimatorController) + AnimatorState(animator);
                    case Graphic graphic:
                    {
                        string tmp = ReflectedText(c);
                        return name + state + " — " + (tmp.Length > 0 ? tmp + ", " : "") + "color " + Col(graphic.color) + ", raycastTarget " + YN(graphic.raycastTarget);
                    }
                    case MonoBehaviour behaviour:
                        return DescribeScript(behaviour, state);
                    default:
                        return name + state;
                }
            }
            catch (Exception e)
            {
                return name + state + " (could not describe: " + e.GetType().Name + ")";
            }
        }

        static string Interactable(Selectable s)
        {
            if (!s.interactable) return "interactable ✘";
            return s.IsInteractable() ? "interactable ✔" : "interactable ✔ but IsInteractable() ✘ (a CanvasGroup blocks it)";
        }

        static string DescribeScript(MonoBehaviour mb, string state)
        {
            var type = mb.GetType();
            var sb = new StringBuilder();
            sb.Append(type.Name).Append(state).Append(" — ").Append(IsUnityType(type) ? "package script " : "script ").Append(type.FullName)
              .Append(" [").Append(type.Assembly.GetName().Name).Append(']');
            var handlers = new List<string>();
            foreach (var i in type.GetInterfaces())
                if (typeof(IEventSystemHandler).IsAssignableFrom(i) && i != typeof(IEventSystemHandler)) handlers.Add(i.Name.Substring(1).Replace("Handler", ""));
            if (handlers.Count > 0) sb.Append(", handles ").Append(string.Join("/", handlers));
            var events = new List<string>();
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(UnityEventBase).IsAssignableFrom(field.FieldType)) continue;
                if (events.Count >= 6) { events.Add("…"); break; }
                var value = field.GetValue(mb) as UnityEventBase;
                events.Add(field.Name + " (" + (value != null ? DescribeEvent(value) : "null") + ")");
            }
            if (events.Count > 0) sb.Append(", events: ").Append(string.Join(", ", events));
            sb.Append(Joined(ReflectedText(mb)));
            return sb.ToString();
        }

        /// <summary>
        /// Text, font and size of a TextMeshPro component as <c>text "…" (font X, 36pt)</c>, reached by reflection so the
        /// package does not depend on TMP. Empty for anything else.
        /// </summary>
        static string ReflectedText(Component c)
        {
            var type = c.GetType();
            if (type.FullName == null || !type.FullName.StartsWith("TMPro.", StringComparison.Ordinal)) return "";
            try
            {
                var textProp = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (textProp == null || textProp.PropertyType != typeof(string)) return "";
                var text = textProp.GetValue(c) as string ?? "";
                var details = new List<string>();
                var fontProp = type.GetProperty("font", BindingFlags.Instance | BindingFlags.Public);
                var sizeProp = type.GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public);
                if (fontProp != null && fontProp.GetValue(c) is UnityEngine.Object font) details.Add("font " + font.name);
                if (sizeProp != null && sizeProp.GetValue(c) is float size) details.Add(size.ToString("0.#") + "pt");
                return "text \"" + Trunc(text, 80) + "\"" + (details.Count > 0 ? " (" + string.Join(", ", details) + ")" : "");
            }
            catch { return ""; }
        }

        static string Joined(string fragment) => fragment.Length > 0 ? ", " + fragment : "";

        static string AnimatorState(Animator animator)
        {
            try
            {
                if (!Application.isPlaying || animator.runtimeAnimatorController == null || !animator.isActiveAndEnabled || animator.layerCount == 0) return "";
                var clips = animator.GetCurrentAnimatorClipInfo(0);
                var info = animator.GetCurrentAnimatorStateInfo(0);
                string clip = clips != null && clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "hash " + info.shortNameHash;
                return ", playing " + clip + " (t " + info.normalizedTime.ToString("0.00") + ")";
            }
            catch { return ""; }
        }

        /// <summary>Persistent and runtime listener counts of a UnityEvent, with persistent targets spelled out.</summary>
        public static string DescribeEvent(UnityEventBase ev)
        {
            if (ev == null) return "null";
            int persistent = ev.GetPersistentEventCount();
            var sb = new StringBuilder();
            sb.Append(persistent).Append(" persistent");
            if (persistent > 0)
            {
                sb.Append(" [");
                for (int i = 0; i < persistent; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var target = ev.GetPersistentTarget(i);
                    sb.Append(target != null ? target.name + "." : "⚠ missing target.").Append(ev.GetPersistentMethodName(i));
                    if (ev.GetPersistentListenerState(i) == UnityEventCallState.Off) sb.Append(" (OFF)");
                }
                sb.Append(']');
            }
            int runtime = RuntimeListenerCount(ev);
            sb.Append(", ").Append(runtime < 0 ? "? runtime" : runtime + " runtime");
            return sb.ToString();
        }

        /// <summary>Total listeners, or -1 when the runtime count could not be read.</summary>
        public static int ListenerCount(UnityEventBase ev)
        {
            if (ev == null) return 0;
            int runtime = RuntimeListenerCount(ev);
            return runtime < 0 ? -1 : ev.GetPersistentEventCount() + runtime;
        }

        static FieldInfo callsField;
        static FieldInfo runtimeCallsField;

        static int RuntimeListenerCount(UnityEventBase ev)
        {
            try
            {
                if (callsField == null) callsField = typeof(UnityEventBase).GetField("m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);
                var calls = callsField?.GetValue(ev);
                if (calls == null) return -1;
                if (runtimeCallsField == null) runtimeCallsField = calls.GetType().GetField("m_RuntimeCalls", BindingFlags.Instance | BindingFlags.NonPublic);
                return runtimeCallsField?.GetValue(calls) is System.Collections.ICollection list ? list.Count : -1;
            }
            catch { return -1; }
        }

        static UnityEventBase PrimaryEvent(Selectable s, out string name)
        {
            switch (s)
            {
                case Button b: name = "onClick"; return b.onClick;
                case Toggle t: name = "onValueChanged"; return t.onValueChanged;
                case Slider sl: name = "onValueChanged"; return sl.onValueChanged;
                case Scrollbar sb: name = "onValueChanged"; return sb.onValueChanged;
                case Dropdown d: name = "onValueChanged"; return d.onValueChanged;
                default: name = ""; return null;
            }
        }

        public static string DescribeRectTransform(RectTransform rt)
        {
            var parent = rt.parent;
            string sibling = parent != null ? ", sibling " + (rt.GetSiblingIndex() + 1) + " of " + parent.childCount : "";
            return "RectTransform: anchoredPosition " + V(rt.anchoredPosition) + ", sizeDelta " + V(rt.sizeDelta) + ", rect " + F(rt.rect.width) + "×" + F(rt.rect.height)
                   + ", anchors " + V(rt.anchorMin) + "–" + V(rt.anchorMax) + ", pivot " + V(rt.pivot) + ", localScale " + S(rt.localScale)
                   + ", rotation " + F(rt.localEulerAngles.z) + "°" + sibling;
        }

        static string BriefGraphic(GameObject go)
        {
            var graphic = go.GetComponent<Graphic>();
            if (graphic == null) return "";
            if (graphic is Text legacy) return "Text \"" + Trunc(legacy.text, 40) + "\"";
            string tmp = ReflectedText(graphic);
            return graphic.GetType().Name + (tmp.Length > 0 ? " " + tmp : "");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>True for engine, editor and Unity package types; false for the project's own scripts and third-party assets.</summary>
        public static bool IsUnityType(Type type)
        {
            string assembly = type.Assembly.GetName().Name ?? "";
            if (assembly.StartsWith("UnityEngine", StringComparison.Ordinal) || assembly.StartsWith("UnityEditor", StringComparison.Ordinal) || assembly.StartsWith("Unity.", StringComparison.Ordinal)) return true;
            string ns = type.Namespace ?? "";
            return ns.StartsWith("UnityEngine", StringComparison.Ordinal) || ns.StartsWith("TMPro", StringComparison.Ordinal);
        }

        static string HitRelation(GameObject hit, GameObject target)
        {
            if (target == null) return "";
            if (hit.transform.IsChildOf(target.transform)) return "inside the target";
            if (hit.transform.parent != null && hit.transform.parent == target.transform.parent) return "a sibling of the target, next to it";
            return "not inside the target";
        }

        public static string HierarchyPath(Transform t)
        {
            if (t == null) return "";
            var names = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent) names.Add(cur.name);
            names.Reverse();
            return string.Join("/", names);
        }

        static string Name(UnityEngine.Object o) => o != null ? o.name : "None";
        static string YN(bool b) => b ? "✔" : "✘";
        static string F(float f) => f.ToString("0.#");
        static string V(Vector2 v) => "(" + F(v.x) + ", " + F(v.y) + ")";
        static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
        static string S(Vector3 v) => "(" + v.x.ToString("0.####") + ", " + v.y.ToString("0.####") + ", " + v.z.ToString("0.####") + ")";
        static string Col(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

        public static string Trunc(string s, int max)
        {
            if (s == null) return "";
            s = s.Replace("\r", "").Replace("\n", "⏎");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
