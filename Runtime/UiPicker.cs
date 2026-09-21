using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Alvaris.AiProductManager
{
    /// <summary>
    /// Finds what is under a screen point. Two views of the same spot: every Graphic in visual order (works in Edit
    /// mode and without an EventSystem) and, in Play mode, what the EventSystem's raycasters would actually hit.
    /// </summary>
    public static class UiPicker
    {
        public sealed class Hit
        {
            public GameObject GameObject;
            public Graphic Graphic;
            public Canvas Canvas;
            public bool RaycastTarget;
            /// <summary>Passes the masks / CanvasGroup filters the GraphicRaycaster applies.</summary>
            public bool PassesRaycastFilters;
            /// <summary>Colour alpha times CanvasRenderer and inherited CanvasGroup alpha.</summary>
            public float EffectiveAlpha;

            public bool IsVisible => EffectiveAlpha > 0.02f;
        }

        /// <summary>The camera a screen point must be interpreted with for this canvas (null for overlay canvases).</summary>
        public static Camera EventCameraFor(Canvas canvas)
        {
            if (canvas == null) return null;
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            if (root.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return root.worldCamera != null ? root.worldCamera : Camera.main;
        }

        /// <summary>All active graphics under the screen point (pixels, origin bottom-left), topmost first.</summary>
        public static List<Hit> PickGraphics(Vector2 screenPos)
        {
            var hits = new List<Hit>();
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var canvas in canvases)
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                var cam = EventCameraFor(canvas);
                var graphics = GraphicRegistry.GetGraphicsForCanvas(canvas);
                if (graphics == null) continue;
                for (int i = 0; i < graphics.Count; i++)
                {
                    var g = graphics[i];
                    if (g == null || !g.isActiveAndEnabled || g.depth == -1) continue;
                    var renderer = g.canvasRenderer;
                    if (renderer != null && renderer.cull) continue;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, screenPos, cam)) continue;
                    float alpha = g.color.a;
                    if (renderer != null) alpha *= renderer.GetAlpha() * renderer.GetInheritedAlpha();
                    hits.Add(new Hit
                    {
                        GameObject = g.gameObject,
                        Graphic = g,
                        Canvas = canvas,
                        RaycastTarget = g.raycastTarget,
                        PassesRaycastFilters = SafeRaycast(g, screenPos, cam),
                        EffectiveAlpha = alpha,
                    });
                }
            }
            hits.Sort(CompareTopmostFirst);
            return hits;
        }

        static bool SafeRaycast(Graphic g, Vector2 screenPos, Camera cam)
        {
            try { return g.Raycast(screenPos, cam); }
            catch { return false; }
        }

        static int CompareTopmostFirst(Hit a, Hit b)
        {
            var rootA = a.Canvas.rootCanvas;
            var rootB = b.Canvas.rootCanvas;
            bool overlayA = rootA.renderMode == RenderMode.ScreenSpaceOverlay;
            bool overlayB = rootB.renderMode == RenderMode.ScreenSpaceOverlay;
            if (overlayA != overlayB) return overlayA ? -1 : 1;
            if (!overlayA)
            {
                float depthA = CameraDepth(rootA), depthB = CameraDepth(rootB);
                if (depthA != depthB) return depthB.CompareTo(depthA);
            }
            var sortA = SortingCanvas(a.Canvas);
            var sortB = SortingCanvas(b.Canvas);
            int layerA = SortingLayer.GetLayerValueFromID(sortA.sortingLayerID);
            int layerB = SortingLayer.GetLayerValueFromID(sortB.sortingLayerID);
            if (layerA != layerB) return layerB.CompareTo(layerA);
            if (sortA.sortingOrder != sortB.sortingOrder) return sortB.sortingOrder.CompareTo(sortA.sortingOrder);
            if (rootA != rootB) return rootB.renderOrder.CompareTo(rootA.renderOrder);
            return b.Graphic.depth.CompareTo(a.Graphic.depth);
        }

        static float CameraDepth(Canvas root)
        {
            var cam = root.worldCamera != null ? root.worldCamera : Camera.main;
            return cam != null ? cam.depth : 0f;
        }

        static Canvas SortingCanvas(Canvas canvas) =>
            canvas.isRootCanvas || canvas.overrideSorting ? canvas : canvas.rootCanvas;

        /// <summary>What the EventSystem would hit at this point, topmost first. Empty when there is no EventSystem.</summary>
        public static List<RaycastResult> EventSystemRaycast(Vector2 screenPos)
        {
            var results = new List<RaycastResult>();
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return results;
            var data = new PointerEventData(eventSystem) { position = screenPos };
            try { eventSystem.RaycastAll(data, results); }
            catch { results.Clear(); }
            return results;
        }

        /// <summary>
        /// The nearest object (self included) that reacts to the pointer: a Selectable or any IEventSystemHandler.
        /// Stops at the canvas root. Null when nothing on the way up is interactive.
        /// </summary>
        public static GameObject InteractiveAncestor(GameObject leaf)
        {
            var t = leaf != null ? leaf.transform : null;
            while (t != null)
            {
                if (t.GetComponent<Selectable>() != null || t.GetComponent<IEventSystemHandler>() != null) return t.gameObject;
                if (t.GetComponent<Canvas>() != null) break;
                t = t.parent;
            }
            return null;
        }

        /// <summary>
        /// The nearest interactive object (see <see cref="InteractiveAncestor"/>), or, when the leaf itself is a plain
        /// visual, the single Selectable among its siblings — labels and backgrounds usually sit next to an invisible
        /// click target rather than under it.
        /// </summary>
        public static GameObject InteractiveNear(GameObject leaf)
        {
            var ancestor = InteractiveAncestor(leaf);
            if (ancestor != null) return ancestor;
            var parent = leaf != null ? leaf.transform.parent : null;
            if (parent == null || parent.GetComponent<Canvas>() != null) return null;
            Selectable only = null;
            foreach (var selectable in parent.GetComponentsInChildren<Selectable>(false))
            {
                if (selectable.transform.parent != parent) continue;
                if (only != null) return null;
                only = selectable;
            }
            return only != null ? only.gameObject : null;
        }

        /// <summary>
        /// The default report target at a point: the control the EventSystem would deliver the click to, else the
        /// first visible graphic's nearest control, else that graphic. <paramref name="hitObject"/> is what the
        /// reporter saw under the pointer.
        /// </summary>
        public static GameObject DefaultTarget(List<Hit> graphicHits, List<RaycastResult> eventSystemHits, out GameObject hitObject)
        {
            hitObject = null;
            if (graphicHits != null && graphicHits.Count > 0)
            {
                hitObject = graphicHits[0].GameObject;
                foreach (var hit in graphicHits)
                {
                    if (!hit.IsVisible) continue;
                    hitObject = hit.GameObject;
                    break;
                }
            }
            GameObject eventTop = eventSystemHits != null && eventSystemHits.Count > 0 ? eventSystemHits[0].gameObject : null;
            if (hitObject == null) hitObject = eventTop;
            if (eventTop != null)
            {
                var control = InteractiveAncestor(eventTop);
                if (control != null) return control;
            }
            if (hitObject != null)
            {
                var control = InteractiveNear(hitObject);
                return control != null ? control : hitObject;
            }
            return eventTop;
        }

        /// <summary>Screen-space rectangle (pixels, origin bottom-left) of a UI element or a renderer's bounds.</summary>
        public static bool TryGetScreenRect(GameObject go, out Rect rect)
        {
            rect = default;
            if (go == null) return false;
            var corners = new Vector3[8];
            int count;
            Camera cam;
            if (go.transform is RectTransform rt)
            {
                var canvas = go.GetComponentInParent<Canvas>(true);
                cam = EventCameraFor(canvas);
                rt.GetWorldCorners(corners);
                count = 4;
            }
            else
            {
                var renderer = go.GetComponentInChildren<Renderer>();
                if (renderer == null) return false;
                cam = Camera.main;
                if (cam == null) return false;
                var b = renderer.bounds;
                int n = 0;
                for (int x = 0; x < 2; x++)
                    for (int y = 0; y < 2; y++)
                        for (int z = 0; z < 2; z++)
                            corners[n++] = new Vector3(x == 0 ? b.min.x : b.max.x, y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z);
                count = 8;
            }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                if (float.IsNaN(p.x) || float.IsNaN(p.y)) return false;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }
    }
}
