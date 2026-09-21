using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Alvaris.AiProductManager
{
    /// <summary>
    /// The in-game part. Watches the Game view for the trigger click, shows the "what's wrong here?" popup over the
    /// element under the pointer, then captures a screenshot and files the report. The Editor installs it when Play
    /// mode starts; it lives on a hidden object that survives scene loads. Runtime IMGUI is used on purpose: it needs
    /// no scene setup and works with either input backend.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ProductManagerOverlay : MonoBehaviour
    {
        public static ProductManagerOverlay Instance { get; private set; }

        public static ProductManagerOverlay Install(ProductManagerConfig config)
        {
            ProductManager.Config = config;
            if (Instance != null) return Instance;
            var go = new GameObject("AI Product Manager (overlay)") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying) DontDestroyOnLoad(go);
            Instance = go.AddComponent<ProductManagerOverlay>();
            return Instance;
        }

        public static void Uninstall()
        {
            if (Instance == null) return;
            var go = Instance.gameObject;
            Instance = null;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        const float PopupWidth = 300f;
        const float Pad = 10f;
        const float RowH = 28f;
        const float Gap = 4f;

        static ProductManagerConfig Config => ProductManager.Config;

        // popup state
        bool popupOpen;
        Vector2 popupGuiPos;                 // scaled GUI units
        Vector2 pointerScreenPos;            // pixels, origin bottom-left
        GameObject target, originalTarget, hitObject;
        List<UiPicker.Hit> graphicHits;
        List<RaycastResult> eventSystemHits;
        [SerializeField, HideInInspector] EventSystem suspendedEventSystem; // serialized so a domain reload cannot leave it disabled
        bool hideForCapture;
        bool busy;
        string toast;
        float toastUntil;
        Rect? flashRect;
        float flashUntil;

        // styles (built inside OnGUI)
        GUIStyle boxStyle, titleStyle, nameStyle, pathStyle, buttonStyle, smallButtonStyle, hintStyle, toastStyle;
        Texture2D boxTex, buttonTex, buttonHoverTex, outlineTex, toastTex;

        public bool IsPopupOpen => popupOpen;
        public GameObject CurrentTarget => target;

        /// <summary>GUI scale: configured value, or automatic from the game resolution.</summary>
        public float OverlayScale
        {
            get
            {
                float s = Config.overlayScale;
                if (s > 0.01f) return s;
                return Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 480f, 1f, 4f);
            }
        }

        void OnEnable()
        {
            // After a domain reload in Play mode the object survives but the static does not: re-register instead of
            // letting Install() create a second overlay, and never leave the EventSystem switched off.
            if (Instance == null) Instance = this;
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            if (!popupOpen) RestoreEventSystem();
        }

        void OnDestroy()
        {
            RestoreEventSystem();
            if (Instance == this) Instance = null;
            foreach (var t in new[] { boxTex, buttonTex, buttonHoverTex, outlineTex, toastTex }) if (t != null) Destroy(t);
        }

        void OnGUI()
        {
            if (hideForCapture) return;
            var e = Event.current;
            Vector2 rawMouse = e.mousePosition; // window pixels, origin top-left, before any matrix
            GUI.depth = -10000;
            EnsureStyles();
            float s = OverlayScale;
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            try
            {
                if (!popupOpen && !busy && e.type == EventType.MouseDown && IsTrigger(e))
                {
                    OpenPopup(new Vector2(rawMouse.x, Screen.height - rawMouse.y), rawMouse / s);
                    e.Use();
                }
                if (popupOpen) DrawPopup(e, s);
                DrawFlash(s);
                DrawToast(s);
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        bool IsTrigger(Event e)
        {
            var c = Config;
            return e.button == (int)c.triggerButton && e.control == c.triggerNeedsCtrl && e.alt == c.triggerNeedsAlt && e.shift == c.triggerNeedsShift;
        }

        // ------------------------------------------------------------------ popup

        /// <summary>Opens the popup for whatever is at <paramref name="screenPos"/> (pixels, origin bottom-left).</summary>
        public void OpenPopup(Vector2 screenPos, Vector2 guiPos)
        {
            pointerScreenPos = screenPos;
            eventSystemHits = UiPicker.EventSystemRaycast(screenPos);
            graphicHits = UiPicker.PickGraphics(screenPos);
            target = UiPicker.DefaultTarget(graphicHits, eventSystemHits, out hitObject);
            originalTarget = target;
            popupGuiPos = guiPos;
            popupOpen = true;
            SuspendEventSystem();
        }

        public void ClosePopup()
        {
            popupOpen = false;
            RestoreEventSystem();
        }

        /// <summary>Files the open popup's target under the given category (used by the Editor's automation menu).</summary>
        public void SubmitCategory(int index)
        {
            var cats = Config.categories;
            if (!popupOpen || cats == null || index < 0 || index >= cats.Length) return;
            Submit(cats[index]);
        }

        void DrawPopup(Event e, float s)
        {
            var cats = Config.categories ?? Array.Empty<string>();
            bool detailed = ProductManager.CanRequestDetailed;
            float screenW = Screen.width / s, screenH = Screen.height / s;
            float height = Pad + 22 + 4 + 18 + 16 + 4 + 24 + 8 + cats.Length * (RowH + Gap) + (detailed ? RowH + Gap : 0) + 16 + Pad;
            var rect = new Rect(
                Mathf.Clamp(popupGuiPos.x + 8, 4, Mathf.Max(4, screenW - PopupWidth - 4)),
                Mathf.Clamp(popupGuiPos.y + 8, 4, Mathf.Max(4, screenH - height - 4)),
                PopupWidth, height);

            if (target != null && UiPicker.TryGetScreenRect(target, out var targetRect)) DrawOutline(ToGui(targetRect, s), 2f);

            GUI.Box(rect, GUIContent.none, boxStyle);
            float x = rect.x + Pad, y = rect.y + Pad, w = rect.width - Pad * 2;
            GUI.Label(new Rect(x, y, w - 30, 22), "What's wrong here?", titleStyle);
            if (GUI.Button(new Rect(rect.xMax - Pad - 24, y, 24, 22), "x", smallButtonStyle))
            {
                ClosePopup();
                return;
            }
            y += 26;
            GUI.Label(new Rect(x, y, w, 18), target != null ? target.name : "(nothing under the pointer)", nameStyle);
            y += 18;
            GUI.Label(new Rect(x, y, w, 16), target != null ? ShortPath(target.transform) : "The report will describe the screen at this position.", pathStyle);
            y += 20;

            float bw = (w - Gap * 2) / 3f;
            GUI.enabled = target != null && target.transform.parent != null;
            if (GUI.Button(new Rect(x, y, bw, 24), "Parent", smallButtonStyle)) target = target.transform.parent.gameObject;
            GUI.enabled = graphicHits != null && graphicHits.Count > 1;
            if (GUI.Button(new Rect(x + bw + Gap, y, bw, 24), "Next under", smallButtonStyle)) CycleUnder();
            GUI.enabled = target != originalTarget;
            if (GUI.Button(new Rect(x + (bw + Gap) * 2, y, bw, 24), "Reset", smallButtonStyle)) target = originalTarget;
            GUI.enabled = true;
            y += 32;

            for (int i = 0; i < cats.Length; i++)
            {
                if (GUI.Button(new Rect(x, y, w, RowH), cats[i], buttonStyle))
                {
                    Submit(cats[i]);
                    return;
                }
                y += RowH + Gap;
            }
            if (detailed)
            {
                if (GUI.Button(new Rect(x, y, w, RowH), "Add notes... (opens an Editor window)", buttonStyle))
                {
                    SubmitDetailed();
                    return;
                }
                y += RowH + Gap;
            }
            GUI.Label(new Rect(x, y, w, 16), "Esc or click outside to cancel", hintStyle);

            if (e.type == EventType.MouseDown)
            {
                if (!rect.Contains(e.mousePosition)) ClosePopup();
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                ClosePopup();
                e.Use();
            }
        }

        void CycleUnder()
        {
            if (graphicHits == null || graphicHits.Count == 0) return;
            int index = -1;
            for (int i = 0; i < graphicHits.Count; i++) if (graphicHits[i].GameObject == hitObject) { index = i; break; }
            var next = graphicHits[(index + 1) % graphicHits.Count];
            hitObject = next.GameObject;
            var interactive = UiPicker.InteractiveNear(hitObject);
            target = interactive != null ? interactive : hitObject;
        }

        void SuspendEventSystem()
        {
            if (!Config.disableEventSystemWhilePopupOpen) return;
            var es = EventSystem.current;
            if (es == null || !es.enabled) return;
            suspendedEventSystem = es;
            es.enabled = false;
        }

        void RestoreEventSystem()
        {
            if (suspendedEventSystem == null) return;
            suspendedEventSystem.enabled = true;
            suspendedEventSystem = null;
        }

        // ------------------------------------------------------------------ filing

        void Submit(string category)
        {
            var t = target;
            var h = hitObject;
            ClosePopup();
            StartCoroutine(FileRoutine(t, h, category, false));
        }

        void SubmitDetailed()
        {
            var t = target;
            var h = hitObject;
            ClosePopup();
            StartCoroutine(FileRoutine(t, h, "", true));
        }

        IEnumerator FileRoutine(GameObject t, GameObject h, string category, bool detailed)
        {
            busy = true;
            byte[] png = null;
            if (Config.captureScreenshot)
            {
                hideForCapture = true;
                yield return new WaitForEndOfFrame();
                png = CaptureNow(t);
                hideForCapture = false;
            }
            busy = false;

            IssueReport report;
            try
            {
                report = ReportBuilder.Build(t, h, category, "", pointerScreenPos, graphicHits, eventSystemHits);
                report.ScreenshotPng = png;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowToast("Report failed: " + e.Message);
                yield break;
            }

            if (detailed)
            {
                if (!ProductManager.RequestDetailed(report)) ShowToast("No Editor window available for notes");
                yield break;
            }
            try
            {
                ProductManager.FileReport(report);
                ShowToast("Report #" + report.Number.ToString("0000") + " saved" + (Config.clipboard == ClipboardContent.Nothing ? "" : " - summary copied, paste it into Claude Code"));
                if (report.TargetScreenRect.HasValue) Flash(report.TargetScreenRect.Value);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowToast("Report failed: " + e.Message);
            }
        }

        static byte[] CaptureNow(GameObject t)
        {
            try
            {
                Rect? highlight = t != null && UiPicker.TryGetScreenRect(t, out var rect) ? rect : (Rect?)null;
                return ScreenshotUtil.CaptureGameViewNow(highlight);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AI PM] Screenshot failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Takes a Game view screenshot at the end of the current frame with the overlay hidden.</summary>
        public void CaptureScreenshotAsync(GameObject t, Action<byte[]> done)
        {
            StartCoroutine(CaptureRoutine(t, done));
        }

        IEnumerator CaptureRoutine(GameObject t, Action<byte[]> done)
        {
            hideForCapture = true;
            yield return new WaitForEndOfFrame();
            var png = CaptureNow(t);
            hideForCapture = false;
            done?.Invoke(png);
        }

        // ------------------------------------------------------------------ feedback

        public void ShowToast(string message)
        {
            toast = message;
            toastUntil = Time.unscaledTime + 4f;
        }

        void Flash(Rect screenRect)
        {
            flashRect = screenRect;
            flashUntil = Time.unscaledTime + 1.2f;
        }

        void DrawToast(float s)
        {
            if (toast == null) return;
            if (Time.unscaledTime > toastUntil)
            {
                toast = null;
                return;
            }
            float screenW = Screen.width / s, screenH = Screen.height / s;
            float w = Mathf.Min(420f, screenW - 20f);
            var content = new GUIContent(toast);
            float h = toastStyle.CalcHeight(content, w);
            GUI.Box(new Rect((screenW - w) / 2f, screenH - h - 16f, w, h), content, toastStyle);
        }

        void DrawFlash(float s)
        {
            if (!flashRect.HasValue) return;
            if (Time.unscaledTime > flashUntil)
            {
                flashRect = null;
                return;
            }
            DrawOutline(ToGui(flashRect.Value, s), 3f);
        }

        Rect ToGui(Rect screenRect, float s) =>
            new Rect(screenRect.xMin / s, (Screen.height - screenRect.yMax) / s, screenRect.width / s, screenRect.height / s);

        void DrawOutline(Rect r, float thickness)
        {
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, r.width, thickness), outlineTex);
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - thickness, r.width, thickness), outlineTex);
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, thickness, r.height), outlineTex);
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.yMin, thickness, r.height), outlineTex);
        }

        static string ShortPath(Transform t)
        {
            var path = ReportBuilder.HierarchyPath(t);
            return path.Length <= 44 ? path : "…" + path.Substring(path.Length - 43);
        }

        // ------------------------------------------------------------------ styles

        void EnsureStyles()
        {
            if (boxStyle != null && boxTex != null) return;
            boxTex = Solid(new Color(0.09f, 0.10f, 0.12f, 0.96f));
            buttonTex = Solid(new Color(0.20f, 0.22f, 0.26f, 1f));
            buttonHoverTex = Solid(new Color(0.24f, 0.42f, 0.78f, 1f));
            outlineTex = Solid(new Color(1f, 0.25f, 0.2f, 1f));
            toastTex = Solid(new Color(0.10f, 0.45f, 0.25f, 0.95f));

            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = boxTex;
            boxStyle.border = new RectOffset(2, 2, 2, 2);

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            titleStyle.normal.textColor = Color.white;

            nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip, wordWrap = false };
            nameStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);

            pathStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, clipping = TextClipping.Clip, wordWrap = false };
            pathStyle.normal.textColor = new Color(0.75f, 0.78f, 0.82f);

            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 6, 4, 4), border = new RectOffset(2, 2, 2, 2) };
            buttonStyle.normal.background = buttonTex;
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.background = buttonHoverTex;
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.active.background = buttonHoverTex;
            buttonStyle.active.textColor = Color.white;
            buttonStyle.focused.background = buttonTex;
            buttonStyle.focused.textColor = Color.white;

            smallButtonStyle = new GUIStyle(buttonStyle) { fontSize = 11, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(4, 4, 2, 2) };

            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            hintStyle.normal.textColor = new Color(0.6f, 0.62f, 0.66f);

            toastStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(12, 12, 8, 8) };
            toastStyle.normal.background = toastTex;
            toastStyle.normal.textColor = Color.white;
        }

        static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
