using System;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>
    /// Encodes Game view frames into an H.264 MP4 with the Editor's built-in MediaEncoder, so recording needs no extra
    /// package. Each frame is written as many times as the wall clock says, so the video plays in real time even when
    /// the Editor renders slower than the requested frame rate. Must be disposed, or the file is left unfinished.
    /// </summary>
    sealed class PlaytestRecorder : IDisposable
    {
        readonly int fps;
        readonly float maxSeconds;
        MediaEncoder encoder;
        Texture2D frame;
        int width;
        int height;
        double startTime;

        public PlaytestRecorder(string path, int fps, float maxSeconds)
        {
            FilePath = path;
            this.fps = Mathf.Clamp(fps, 1, 60);
            this.maxSeconds = Mathf.Max(1f, maxSeconds);
        }

        public string FilePath { get; }
        public int FramesWritten { get; private set; }
        public int FramesSkipped { get; private set; }
        public int FramesPerSecond => fps;
        public float Seconds => FramesWritten / (float)fps;
        public bool IsFull => Seconds >= maxSeconds;
        public Vector2Int Size => new Vector2Int(width, height);

        public void Add(Texture2D source)
        {
            if (source == null || IsFull) return;
            if (encoder == null && !Open(source)) return;
            // H.264 needs even sizes; a Game view resized mid-recording no longer fits the track and is skipped.
            if ((source.width & ~1) != width || (source.height & ~1) != height)
            {
                FramesSkipped++;
                return;
            }

            var input = source;
            if (source.format != TextureFormat.RGBA32 || source.width != width || source.height != height)
            {
                frame.SetPixels(source.GetPixels(0, 0, width, height));
                frame.Apply(false);
                input = frame;
            }

            // Frames follow the wall clock: a frame that arrives early is dropped, a late one fills the gap (at most a second).
            int due = (int)((EditorApplication.timeSinceStartup - startTime) * fps) + 1;
            int copies = Mathf.Min(due - FramesWritten, fps);
            if (copies <= 0) return;
            for (int i = 0; i < copies && !IsFull; i++)
            {
                encoder.AddFrame(input);
                FramesWritten++;
            }
        }

        bool Open(Texture2D first)
        {
            width = first.width & ~1;
            height = first.height & ~1;
            if (width < 2 || height < 2) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var track = new VideoTrackAttributes
            {
                frameRate = new MediaRational(fps),
                width = (uint)width,
                height = (uint)height,
                includeAlpha = false,
                bitRateMode = VideoBitrateMode.High,
            };
            encoder = new MediaEncoder(FilePath, track);
            frame = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            startTime = EditorApplication.timeSinceStartup;
            return true;
        }

        public void Dispose()
        {
            if (encoder != null)
            {
                try { encoder.Dispose(); }
                catch (Exception e) { Debug.LogWarning("[AI PM] Closing the playtest video failed: " + e.Message); }
                encoder = null;
            }
            if (frame != null)
            {
                UnityEngine.Object.DestroyImmediate(frame);
                frame = null;
            }
        }
    }
}
