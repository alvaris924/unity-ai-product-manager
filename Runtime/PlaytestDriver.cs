using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Alvaris.AiProductManager
{
    /// <summary>
    /// Plays scripted gestures (tap, hold, drag, joystick push) through <see cref="SimulatedPointer"/>s and grabs Game
    /// view frames at the end of a frame, the only moment the back buffer can be read. The Editor installs it on demand in
    /// Play mode, hidden and kept across scene loads; nothing is added to scenes or builds. Timing uses unscaled time, so
    /// gestures still land while the game is paused with <c>Time.timeScale = 0</c>.
    /// </summary>
    public sealed class PlaytestDriver : MonoBehaviour
    {
        /// <summary>How long a tap keeps the finger down.</summary>
        public const float TapSeconds = 0.08f;

        /// <summary>What a finished gesture did.</summary>
        public sealed class GestureResult
        {
            public int Id;
            public string Kind;
            public Vector2 From;
            public Vector2 To;
            public GameObject PressHit;
            public GameObject PressHandler;
            public GameObject Clicked;
            public GameObject Dragged;
            public bool Cancelled;
        }

        sealed class Gesture
        {
            public int Id;
            public string Kind;
            public SimulatedPointer Pointer;
            public Func<float, Vector2> Path;
            public float Seconds;
            public float StartTime = -1f;
            public Action<GestureResult> Done;
        }

        public static PlaytestDriver Instance { get; private set; }

        readonly List<Gesture> gestures = new List<Gesture>();
        int nextGestureId = 1;
        int nextPointerId = -100;

        Action<Texture2D> frameSink;
        float frameInterval;
        float nextFrameTime;
        Coroutine frameRoutine;

        /// <summary>The running driver, created if needed. Null outside Play mode.</summary>
        public static PlaytestDriver Ensure()
        {
            if (Instance != null) return Instance;
            if (!Application.isPlaying) return null;
            var go = new GameObject("AI Product Manager (playtest)") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            return go.AddComponent<PlaytestDriver>();
        }

        public static void Uninstall()
        {
            if (Instance == null) return;
            var go = Instance.gameObject;
            Instance = null;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        void OnEnable()
        {
            // Re-register after a mid-Play recompile, which keeps the object but not the static.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDisable()
        {
            CancelAll();
            StopFrameStream();
            if (Instance == this) Instance = null;
        }

        public int ActiveGestures => gestures.Count;
        public bool IsStreamingFrames => frameSink != null;

        // ------------------------------------------------------------------ gestures

        /// <summary>Press and release at one point (pixels, origin bottom-left).</summary>
        public int Tap(Vector2 screenPos, Action<GestureResult> done) =>
            Add("tap", t => screenPos, TapSeconds, done);

        /// <summary>Press at one point and keep the finger still for a while (a long press), then lift it.</summary>
        public int Hold(Vector2 screenPos, float seconds, Action<GestureResult> done) =>
            Add("hold", t => screenPos, Mathf.Max(TapSeconds, seconds), done);

        /// <summary>Press at <paramref name="from"/>, move in a straight line to <paramref name="to"/> over the given time, lift.</summary>
        public int Drag(Vector2 from, Vector2 to, float seconds, Action<GestureResult> done)
        {
            float duration = Mathf.Max(0.05f, seconds);
            return Add("drag", t => Vector2.Lerp(from, to, Mathf.Clamp01(t / duration)), duration, done);
        }

        /// <summary>
        /// Joystick push: press at a point, then keep sliding the finger in one direction at a steady speed. The
        /// distance keeps growing, so fixed sticks stay at full deflection and floating sticks that re-centre on the
        /// finger keep being pushed; points past the screen edge are fine because only the offset is read.
        /// </summary>
        public int Push(Vector2 from, Vector2 direction, float pixelsPerSecond, float seconds, Action<GestureResult> done)
        {
            var dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
            float speed = Mathf.Max(1f, pixelsPerSecond);
            return Add("push", t => from + dir * speed * t, Mathf.Max(0.05f, seconds), done);
        }

        /// <summary>Lifts every finger now, reporting each gesture as cancelled.</summary>
        public void CancelAll()
        {
            foreach (var g in gestures.ToArray()) Finish(g, true);
            gestures.Clear();
        }

        int Add(string kind, Func<float, Vector2> path, float seconds, Action<GestureResult> done)
        {
            var g = new Gesture
            {
                Id = nextGestureId++,
                Kind = kind,
                Pointer = new SimulatedPointer(nextPointerId--),
                Path = path,
                Seconds = seconds,
                Done = done,
            };
            gestures.Add(g);
            return g.Id;
        }

        void Update()
        {
            if (gestures.Count == 0) return;
            float now = Time.unscaledTime;
            foreach (var g in gestures.ToArray())
            {
                if (g.StartTime < 0f)
                {
                    g.StartTime = now;
                    if (!g.Pointer.Press(g.Path(0f)))
                    {
                        Finish(g, true);
                        gestures.Remove(g);
                    }
                    continue;
                }
                float t = now - g.StartTime;
                g.Pointer.Move(g.Path(Mathf.Min(t, g.Seconds)));
                if (t < g.Seconds) continue;
                Finish(g, false);
                gestures.Remove(g);
            }
        }

        void Finish(Gesture g, bool cancelled)
        {
            var end = g.Pointer.IsPressed ? g.Pointer.Position : g.Path(g.Seconds);
            if (g.Pointer.IsPressed) g.Pointer.Release(end);
            var result = new GestureResult
            {
                Id = g.Id,
                Kind = g.Kind,
                From = g.Path(0f),
                To = end,
                PressHit = g.Pointer.PressHit,
                PressHandler = g.Pointer.PressHandler,
                Clicked = g.Pointer.Clicked,
                Dragged = g.Pointer.Dragged,
                Cancelled = cancelled,
            };
            try { g.Done?.Invoke(result); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ frames

        /// <summary>Grabs the Game view (overlay canvases included) at the end of this frame and hands it over; the texture is destroyed afterwards.</summary>
        public void CaptureEndOfFrame(Action<Texture2D> done)
        {
            StartCoroutine(CaptureRoutine(done));
        }

        IEnumerator CaptureRoutine(Action<Texture2D> done)
        {
            yield return new WaitForEndOfFrame();
            var tex = Grab();
            try { done?.Invoke(tex); }
            catch (Exception e) { Debug.LogException(e); }
            finally { if (tex != null) Destroy(tex); }
        }

        /// <summary>Hands a Game view frame to <paramref name="sink"/> about <paramref name="framesPerSecond"/> times a second of real time until stopped.</summary>
        public void StartFrameStream(float framesPerSecond, Action<Texture2D> sink)
        {
            StopFrameStream();
            frameSink = sink;
            frameInterval = 1f / Mathf.Clamp(framesPerSecond, 1f, 60f);
            nextFrameTime = 0f;
            frameRoutine = StartCoroutine(FrameStreamRoutine());
        }

        public void StopFrameStream()
        {
            frameSink = null;
            if (frameRoutine != null) StopCoroutine(frameRoutine);
            frameRoutine = null;
        }

        IEnumerator FrameStreamRoutine()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (frameSink != null)
            {
                yield return endOfFrame;
                if (frameSink == null) yield break;
                float now = Time.unscaledTime;
                if (now < nextFrameTime) continue;
                // The next grab is one interval after this one and never in the past, so a slow stretch cannot turn into a burst.
                nextFrameTime = nextFrameTime <= 0f || now - nextFrameTime > frameInterval ? now + frameInterval : nextFrameTime + frameInterval;
                var tex = Grab();
                if (tex == null) continue;
                try { frameSink?.Invoke(tex); }
                catch (Exception e) { Debug.LogException(e); }
                finally { Destroy(tex); }
            }
        }

        static Texture2D Grab()
        {
            try { return ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (Exception e)
            {
                Debug.LogWarning("[AI PM] Playtest capture failed: " + e.Message);
                return null;
            }
        }
    }
}
