using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Alvaris.AiProductManager
{
    /// <summary>Game view captures with the report target outlined, in Play mode (back buffer) and Edit mode (camera render).</summary>
    public static class ScreenshotUtil
    {
        static readonly Color32 OutlineColor = new Color32(255, 40, 40, 255);

        /// <summary>
        /// PNG of the Game view as it is right now. Call from a coroutine after <c>WaitForEndOfFrame</c>; falls back to
        /// rendering the scene cameras when the back buffer cannot be read.
        /// </summary>
        public static byte[] CaptureGameViewNow(Rect? highlight)
        {
            Texture2D tex = null;
            try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (Exception e) { Debug.LogWarning("[AI PM] ScreenCapture failed, rendering the cameras instead: " + e.Message); }
            if (tex == null) tex = RenderCamerasToTexture(Screen.width, Screen.height, null, out _);
            if (tex == null) return null;
            try
            {
                if (highlight.HasValue) DrawOutline(tex, highlight.Value);
                return tex.EncodeToPNG();
            }
            finally { Destroy(tex); }
        }

        /// <summary>
        /// Renders every enabled base camera (depth order) into a texture of the given size. When
        /// <paramref name="highlightTarget"/> is given, its screen rect is measured while the cameras render into that
        /// texture, so the outline lands in the right place even in Edit mode.
        /// </summary>
        public static Texture2D RenderCamerasToTexture(int width, int height, GameObject highlightTarget, out Rect? highlight)
        {
            highlight = null;
            var cameras = new List<Camera>();
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if (cam.cameraType != CameraType.Game || cam.targetTexture != null || IsOverlayCamera(cam)) continue;
                cameras.Add(cam);
            }
            if (cameras.Count == 0) return null;
            cameras.Sort((a, b) => a.depth.CompareTo(b.depth));

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previousActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.black);
                Canvas.ForceUpdateCanvases();
                foreach (var cam in cameras) cam.targetTexture = rt;
                foreach (var cam in cameras) cam.Render();
                if (highlightTarget != null && UiPicker.TryGetScreenRect(highlightTarget, out var rect)) highlight = rect;
                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AI PM] Camera render failed: " + e.Message);
                if (tex != null) Destroy(tex);
                return null;
            }
            finally
            {
                foreach (var cam in cameras) if (cam != null) cam.targetTexture = null;
                RenderTexture.active = previousActive;
                rt.Release();
                Destroy(rt);
            }
        }

        /// <summary>Draws a rectangle outline (screen pixels, origin bottom-left) into a readable texture.</summary>
        public static void DrawOutline(Texture2D tex, Rect rect)
        {
            int thickness = Mathf.Max(2, Mathf.RoundToInt(Mathf.Min(tex.width, tex.height) / 300f));
            int x0 = Mathf.Clamp(Mathf.FloorToInt(rect.xMin) - thickness, 0, tex.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(rect.xMax) + thickness, 0, tex.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(rect.yMin) - thickness, 0, tex.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(rect.yMax) + thickness, 0, tex.height - 1);
            if (x1 <= x0 || y1 <= y0) return;
            FillRect(tex, x0, y0, x1 - x0 + 1, Mathf.Min(thickness, y1 - y0 + 1));
            FillRect(tex, x0, Mathf.Max(y0, y1 - thickness + 1), x1 - x0 + 1, Mathf.Min(thickness, y1 - y0 + 1));
            FillRect(tex, x0, y0, Mathf.Min(thickness, x1 - x0 + 1), y1 - y0 + 1);
            FillRect(tex, Mathf.Max(x0, x1 - thickness + 1), y0, Mathf.Min(thickness, x1 - x0 + 1), y1 - y0 + 1);
            tex.Apply();
        }

        static void FillRect(Texture2D tex, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            var block = new Color32[w * h];
            for (int i = 0; i < block.Length; i++) block[i] = OutlineColor;
            tex.SetPixels32(x, y, w, h, block);
        }

        static bool IsOverlayCamera(Camera cam)
        {
            // URP overlay cameras cannot be rendered on their own; detected by reflection so URP is not a dependency.
            foreach (var c in cam.GetComponents<Component>())
            {
                if (c == null || c.GetType().Name != "UniversalAdditionalCameraData") continue;
                var prop = c.GetType().GetProperty("renderType", BindingFlags.Instance | BindingFlags.Public);
                var value = prop?.GetValue(c);
                return value != null && value.ToString() == "Overlay";
            }
            return false;
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
