using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
#if AIPM_VIDEO
using UnityEngine.Video;
#endif

namespace Alvaris.AiProductManager.Editor
{
    /// <summary>
    /// Window > AI Product Manager > Playtests: each scripted playtest with its steps, their outcomes, the console errors
    /// logged between them, its screenshots and its video, read from the files the session wrote (<c>playtest.md</c>,
    /// <c>NN-label.png</c>, <c>video*.mp4</c>), and the session in progress, live. The video plays in the window when the
    /// Video module is enabled, and a step's time seeks to that moment; otherwise it opens in the system player.
    /// </summary>
    public sealed class PlaytestsWindow : EditorWindow
    {
        sealed class StepView
        {
            /// <summary>Session time of the step; -1 when unknown.</summary>
            public double Seconds = -1;
            public string Clock;
            public string Action;
            public string Detail;
            public string Image;
            public readonly List<string> Errors = new List<string>();
        }

        sealed class VideoView
        {
            public string Path;
            /// <summary>Session time the recording started at; -1 when the steps do not say.</summary>
            public double Start = -1;
            /// <summary>Length reported when the recording stopped; -1 when unknown.</summary>
            public double Length = -1;
            /// <summary>Still being written: the file cannot be played yet.</summary>
            public bool Recording;
        }

        sealed class SessionView
        {
            public string Folder;
            public int Number;
            public string Title;
            public string Meta = "";
            public bool Live;
            public int ErrorCount;
            public int WarningCount;
            public readonly List<StepView> Steps = new List<StepView>();
            public readonly List<VideoView> Videos = new List<VideoView>();
        }

        const float ListWidth = 250f;
        const float ThumbHeight = 200f;

        static readonly Regex StepLine = new Regex(@"^(\d+)\. `([^`]+)` \*\*([^*]+)\*\* ?(.*)$");
        static readonly Regex ImageLine = new Regex(@"^\s+!\[[^\]]*\]\(([^)]+)\)\s*$");
        static readonly Regex ErrorLine = new Regex(@"^\s+- `(.*)`\s*$");
        static readonly Regex StoppedDetail = new Regex(@"^stopped: (.+?), ([\d.,]+) s");

        readonly List<SessionView> sessions = new List<SessionView>();
        readonly Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>();
        readonly HashSet<string> unreadable = new HashSet<string>();
        readonly HashSet<string> enlarged = new HashSet<string>();
        [SerializeField] string selectedFolder;
        int videoIndex;
        Vector2 listScroll;
        Vector2 stepsScroll;
        double lastLiveRepaint;
        [NonSerialized] GUIStyle titleStyle;
        [NonSerialized] GUIStyle wrapStyle;
        [NonSerialized] GUIStyle okStyle;
        [NonSerialized] GUIStyle warnStyle;
        [NonSerialized] GUIStyle errorStyle;
#if AIPM_VIDEO
        VideoPreview video;
        bool videoWasBusy;
        double lastVideoRepaint;
#endif

        [MenuItem("Window/AI Product Manager/Playtests", false, 11)]
        public static void Open()
        {
            var window = GetWindow<PlaytestsWindow>("AI PM Playtests");
            window.minSize = new Vector2(760, 420);
            window.Show();
            window.Scan();
        }

        void OnEnable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.focusChanged -= OnAppFocusChanged;
            EditorApplication.focusChanged += OnAppFocusChanged;
            Scan();
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.focusChanged -= OnAppFocusChanged;
            StopVideo();
            ClearImages();
        }

        void OnFocus() => Scan();

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            // The hidden player object belongs to the scene being torn down.
            if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.ExitingPlayMode) StopVideo();
        }

        void OnAppFocusChanged(bool focused)
        {
#if AIPM_VIDEO
            video?.OnAppFocus(focused);
#endif
            Repaint();
        }

        // ------------------------------------------------------------------ data

        void Scan()
        {
            sessions.Clear();
            var root = Path.Combine(AiProductManagerSettings.instance.ReportsRootAbsolute, PlaytestSession.PlaytestsFolder);
            var live = PlaytestSession.instance;
            if (Directory.Exists(root))
            {
                foreach (var folder in Directory.GetDirectories(root))
                {
                    if (live.IsActive && SamePath(folder, live.Folder)) continue;
                    sessions.Add(Read(folder));
                }
            }
            sessions.Sort((a, b) => b.Number.CompareTo(a.Number));
            if (live.IsActive) sessions.Insert(0, ReadLive(live));
            if (sessions.Count > 0 && Selected() == null) Select(sessions[0]);
        }

        static SessionView Read(string folder)
        {
            var name = Path.GetFileName(folder);
            var view = new SessionView { Folder = folder, Number = ReportWriter.LeadingNumber(name), Title = name };
            var report = Path.Combine(folder, PlaytestSession.ReportFile);
            try
            {
                if (File.Exists(report))
                {
                    StepView step = null;
                    foreach (var line in File.ReadAllLines(report))
                    {
                        if (line.StartsWith("# ", StringComparison.Ordinal))
                        {
                            int dot = line.IndexOf(" · ", StringComparison.Ordinal);
                            view.Title = dot >= 0 ? line.Substring(dot + 3) : line.Substring(2);
                            continue;
                        }
                        if (line.StartsWith("- Started ", StringComparison.Ordinal))
                        {
                            view.Meta = line.Substring(2);
                            continue;
                        }
                        var m = StepLine.Match(line);
                        if (m.Success)
                        {
                            step = new StepView { Clock = m.Groups[2].Value, Seconds = ParseClock(m.Groups[2].Value), Action = m.Groups[3].Value, Detail = m.Groups[4].Value };
                            view.Steps.Add(step);
                            continue;
                        }
                        if (step == null) continue;
                        m = ImageLine.Match(line);
                        if (m.Success)
                        {
                            step.Image = Path.Combine(folder, m.Groups[1].Value);
                            continue;
                        }
                        m = ErrorLine.Match(line);
                        if (m.Success) step.Errors.Add(m.Groups[1].Value);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AI PM] Could not read " + report + ": " + e.Message);
            }

            if (view.Steps.Count == 0 && Directory.Exists(folder))
            {
                // No report (the session never finished): show the screenshots on their own.
                var pngs = Directory.GetFiles(folder, "*.png");
                Array.Sort(pngs, StringComparer.OrdinalIgnoreCase);
                foreach (var png in pngs)
                    view.Steps.Add(new StepView { Clock = "", Action = "shot", Detail = Path.GetFileNameWithoutExtension(png), Image = png });
            }
            Finish(view);
            return view;
        }

        static SessionView ReadLive(PlaytestSession session)
        {
            var view = new SessionView
            {
                Folder = session.Folder,
                Number = ReportWriter.LeadingNumber(Path.GetFileName(session.Folder)),
                Title = session.Title,
                Live = true,
            };
            foreach (var s in session.Steps)
            {
                var step = new StepView
                {
                    Seconds = s.seconds,
                    Clock = ((int)(s.seconds / 60)).ToString("00") + ":" + (s.seconds % 60).ToString("00.0", CultureInfo.InvariantCulture),
                    Action = s.action,
                    Detail = s.detail,
                    Image = !string.IsNullOrEmpty(s.file) && s.file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? Path.Combine(session.Folder, s.file) : null,
                };
                step.Errors.AddRange(s.errors);
                view.Steps.Add(step);
            }
            Finish(view);
            return view;
        }

        /// <summary>Counts, and the videos on disk matched to the steps that started and stopped them.</summary>
        static void Finish(SessionView view)
        {
            var starts = new List<double>();
            var lengths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in view.Steps)
            {
                view.ErrorCount += step.Errors.Count;
                if (step.Detail.Contains("⚠")) view.WarningCount++;
                if (step.Action != "record") continue;
                if (step.Detail.StartsWith("started", StringComparison.Ordinal)) starts.Add(step.Seconds);
                var m = StoppedDetail.Match(step.Detail);
                if (m.Success && double.TryParse(m.Groups[2].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                    lengths[m.Groups[1].Value] = seconds;
            }

            if (!Directory.Exists(view.Folder)) return;
            var files = new List<string>(Directory.GetFiles(view.Folder, "*.mp4"));
            files.Sort((a, b) => RecordingNumber(a).CompareTo(RecordingNumber(b)));
            foreach (var file in files)
            {
                int n = RecordingNumber(file);
                view.Videos.Add(new VideoView
                {
                    Path = file,
                    Start = n >= 1 && n <= starts.Count ? starts[n - 1] : -1,
                    Length = lengths.TryGetValue(Path.GetFileName(file), out var length) ? length : -1,
                    Recording = view.Live && Playtest.IsRecording && SamePath(Playtest.RecordingFile, file),
                });
            }
        }

        /// <summary>video.mp4 is the first recording of a session, video-N.mp4 the N-th.</summary>
        static int RecordingNumber(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            int dash = name.LastIndexOf('-');
            return dash >= 0 && int.TryParse(name.Substring(dash + 1), out var n) ? n : 1;
        }

        static double ParseClock(string clock)
        {
            int colon = clock.IndexOf(':');
            if (colon < 0) return -1;
            bool ok = int.TryParse(clock.Substring(0, colon), out var minutes);
            ok &= double.TryParse(clock.Substring(colon + 1).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds);
            return ok ? minutes * 60 + seconds : -1;
        }

        SessionView Selected()
        {
            foreach (var s in sessions)
                if (SamePath(s.Folder, selectedFolder)) return s;
            return null;
        }

        void Select(SessionView session)
        {
            if (session == null || SamePath(session.Folder, selectedFolder)) return;
            selectedFolder = session.Folder;
            videoIndex = 0;
            stepsScroll = Vector2.zero;
            enlarged.Clear();
            StopVideo();
            ClearImages();
        }

        Texture2D Image(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (images.TryGetValue(path, out var cached) && cached != null) return cached;
            if (unreadable.Contains(path) || !File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            bool loaded;
            try { loaded = tex.LoadImage(File.ReadAllBytes(path)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { loaded = false; }
            if (!loaded)
            {
                DestroyImmediate(tex);
                // A file still being written loads on a later repaint; one that is a few seconds old is broken.
                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds > 5) unreadable.Add(path);
                return null;
            }
            images[path] = tex;
            return tex;
        }

        void ClearImages()
        {
            foreach (var tex in images.Values)
                if (tex != null) DestroyImmediate(tex);
            images.Clear();
            unreadable.Clear();
        }

        static bool SamePath(string a, string b) =>
            !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
            && string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            EnsureStyles();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Playtests (" + sessions.Count + ")", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Inbox", EditorStyles.toolbarButton)) InboxWindow.Open();
                if (GUILayout.Button("Folder", EditorStyles.toolbarButton))
                {
                    var root = Path.Combine(AiProductManagerSettings.instance.ReportsRootAbsolute, PlaytestSession.PlaytestsFolder);
                    Directory.CreateDirectory(root);
                    EditorUtility.RevealInFinder(root);
                }
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
                {
                    Scan();
                    GUIUtility.ExitGUI();
                }
            }

            if (sessions.Count == 0)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.HelpBox("No playtests yet. Ask Claude Code for /pm playtest <what to check>, or drive one with the pm_* Unity CLI commands or the Playtest C# API.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(listScroll, GUILayout.Width(ListWidth)))
                {
                    listScroll = scroll.scrollPosition;
                    foreach (var session in sessions) DrawListItem(session);
                }
                var line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
                EditorGUI.DrawRect(line, new Color(0f, 0f, 0f, 0.25f));
                var selected = Selected();
                if (selected != null)
                    using (new EditorGUILayout.VerticalScope())
                        DrawDetail(selected, position.width - ListWidth - 1f);
            }
        }

        void DrawListItem(SessionView session)
        {
            var rect = GUILayoutUtility.GetRect(0f, 44f, GUILayout.ExpandWidth(true));
            if (SamePath(session.Folder, selectedFolder)) EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.90f, 0.30f));
            GUI.Label(new Rect(rect.x + 6, rect.y + 4, rect.width - 12, 18), (session.Live ? "● " : "") + session.Number.ToString("0000") + " · " + session.Title, EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.x + 6, rect.y + 23, rect.width - 12, 16), Counts(session), EditorStyles.miniLabel);
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                Select(session);
                Event.current.Use();
                Repaint();
                // The detail pane changes shape; start over from the next layout pass.
                GUIUtility.ExitGUI();
            }
        }

        static string Counts(SessionView session)
        {
            var text = (session.Live ? "live · " : "") + session.Steps.Count + (session.Steps.Count == 1 ? " step" : " steps");
            if (session.WarningCount > 0) text += " · " + session.WarningCount + " ⚠";
            if (session.ErrorCount > 0) text += " · " + session.ErrorCount + (session.ErrorCount == 1 ? " console error" : " console errors");
            if (session.Videos.Count > 0) text += " · video";
            return text;
        }

        void DrawDetail(SessionView session, float width)
        {
            GUILayout.Space(4);
            GUILayout.Label(session.Title, titleStyle);
            GUILayout.Label(session.Live ? "In progress · " + Clock((float)PlaytestSession.instance.Elapsed) : session.Meta, EditorStyles.miniLabel);
            GUILayout.Label(Counts(session), EditorStyles.miniLabel);

            var report = Path.Combine(session.Folder, PlaytestSession.ReportFile);
            var summary = Path.Combine(session.Folder, PlaytestSession.SummaryFile);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!File.Exists(report)))
                    if (GUILayout.Button("Open report", GUILayout.Width(95))) EditorUtility.OpenWithDefaultApp(report);
                using (new EditorGUI.DisabledScope(!File.Exists(summary)))
                {
                    if (GUILayout.Button("Copy for Claude", GUILayout.Width(115)))
                    {
                        EditorGUIUtility.systemCopyBuffer = File.ReadAllText(summary);
                        ShowNotification(new GUIContent("Copied — paste it into Claude Code"), 2);
                    }
                }
                if (GUILayout.Button("Reveal", GUILayout.Width(65))) EditorUtility.RevealInFinder(File.Exists(report) ? report : session.Folder);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(session.Live))
                    if (GUILayout.Button("Delete", GUILayout.Width(65))) Delete(session);
            }
            GUILayout.Space(6);

            float videoWidth = session.Videos.Count > 0 ? Mathf.Clamp(width * 0.4f, 220f, 420f) : 0f;
            using (new EditorGUILayout.HorizontalScope())
            {
                float stepsWidth = width - videoWidth - (videoWidth > 0 ? 14f : 4f);
                using (var scroll = new EditorGUILayout.ScrollViewScope(stepsScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none, GUILayout.Width(stepsWidth), GUILayout.ExpandHeight(true)))
                {
                    stepsScroll = scroll.scrollPosition;
                    if (session.Steps.Count == 0) EditorGUILayout.HelpBox("No steps yet.", MessageType.None);
                    // Room for the vertical scrollbar and the box's margins and padding.
                    foreach (var step in session.Steps) DrawStep(session, step, stepsWidth - 34f);
                }
                if (videoWidth > 0)
                {
                    GUILayout.Space(6);
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(videoWidth)))
                        DrawVideo(session, videoWidth);
                }
            }
        }

        void DrawStep(SessionView session, StepView step, float width)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (VideoAt(session, step.Seconds, out int index, out double at))
                    {
                        if (GUILayout.Button(new GUIContent(step.Clock, "Show this moment in the video"), EditorStyles.miniButton, GUILayout.Width(54)))
                            Seek(session, index, at);
                    }
                    else
                    {
                        GUILayout.Label(step.Clock, EditorStyles.miniLabel, GUILayout.Width(54));
                    }
                    GUILayout.Label(step.Action, EditorStyles.boldLabel, GUILayout.Width(50));
                    var style = step.Detail.Contains("⚠") ? warnStyle : step.Detail.Contains("✔") ? okStyle : wrapStyle;
                    // The Markdown code quotes around paths are noise here.
                    GUILayout.Label(step.Detail.Replace("`", ""), style, GUILayout.Width(Mathf.Max(80f, width - 124f)));
                }
                foreach (var error in step.Errors)
                    GUILayout.Label("⚠ " + error, errorStyle, GUILayout.Width(Mathf.Max(80f, width - 12f)));

                var tex = Image(step.Image);
                if (tex == null) return;
                bool big = enlarged.Contains(step.Image);
                float height = big ? Mathf.Min(tex.height, 720f) : ThumbHeight;
                float w = Mathf.Min(width - 10f, height * tex.width / tex.height);
                height = w * tex.height / tex.width;
                var rect = GUILayoutUtility.GetRect(w, height, GUILayout.Width(w), GUILayout.Height(height));
                GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Zoom);
                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    if (Event.current.clickCount == 2) EditorUtility.OpenWithDefaultApp(step.Image);
                    else if (!enlarged.Remove(step.Image)) enlarged.Add(step.Image);
                    Event.current.Use();
                    Repaint();
                }
                GUILayout.Label(Path.GetFileName(step.Image) + (big ? " · click to shrink" : " · click to enlarge") + " · double-click to open", EditorStyles.miniLabel);
            }
        }

        /// <summary>The recording that covers a session time, and where in it that time falls.</summary>
        static bool VideoAt(SessionView session, double seconds, out int index, out double at)
        {
            index = -1;
            at = 0;
#if AIPM_VIDEO
            if (seconds < 0) return false;
            for (int i = 0; i < session.Videos.Count; i++)
            {
                var v = session.Videos[i];
                if (v.Recording || v.Start < 0 || seconds < v.Start - 0.05) continue;
                if (v.Length >= 0 && seconds > v.Start + v.Length + 0.5) continue;
                index = i;
                at = Math.Max(0, seconds - v.Start);
                return true;
            }
#endif
            return false;
        }

        void DrawVideo(SessionView session, float width)
        {
            videoIndex = Mathf.Clamp(videoIndex, 0, session.Videos.Count - 1);
            var current = session.Videos[videoIndex];
            using (new EditorGUILayout.HorizontalScope())
            {
                if (session.Videos.Count > 1)
                {
                    var names = session.Videos.ConvertAll(v => Path.GetFileName(v.Path)).ToArray();
                    int picked = EditorGUILayout.Popup(videoIndex, names, GUILayout.Width(120));
                    if (picked != videoIndex)
                    {
                        videoIndex = picked;
                        current = session.Videos[videoIndex];
                        StopVideo();
                    }
                }
                else
                {
                    GUILayout.Label(Path.GetFileName(current.Path), EditorStyles.boldLabel);
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(current.Recording))
                    if (GUILayout.Button(new GUIContent("Open", "Play it in the system video player"), EditorStyles.miniButton, GUILayout.Width(48)))
                        EditorUtility.OpenWithDefaultApp(current.Path);
            }

            if (current.Recording)
            {
                EditorGUILayout.HelpBox("Recording… The video shows here once it stops.", MessageType.Info);
                return;
            }
#if AIPM_VIDEO
            if (video == null || !SamePath(video.Path, current.Path))
            {
                StopVideo();
                video = new VideoPreview(current.Path, 0);
            }
            if (video.Failed)
            {
                EditorGUILayout.HelpBox("This video cannot play here (" + video.Error + "). Open plays it in the system player.", MessageType.Warning);
                if (GUILayout.Button("Retry", EditorStyles.miniButton, GUILayout.Width(60))) StopVideo();
                return;
            }

            float aspect = video.Aspect > 0f ? video.Aspect : 9f / 16f;
            float height = Mathf.Min(width / aspect, Mathf.Max(160f, position.height - 190f));
            float w = height * aspect;
            var rect = GUILayoutUtility.GetRect(w, height, GUILayout.Width(w), GUILayout.Height(height));
            EditorGUI.DrawRect(rect, Color.black);
            if (video.Ready) GUI.DrawTexture(rect, video.Texture, ScaleMode.ScaleToFit);
            else GUI.Label(rect, video.Waiting ? "Loading… (Unity plays video while it is the active app)" : "Loading…", EditorStyles.centeredGreyMiniLabel);

            using (new EditorGUI.DisabledScope(!video.Ready))
            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(w)))
            {
                if (GUILayout.Button(video.IsPlaying ? "Pause" : "Play", EditorStyles.miniButton, GUILayout.Width(50))) video.Toggle();
                float length = (float)video.Length;
                float time = (float)video.Time;
                float picked = GUILayout.HorizontalSlider(time, 0f, Mathf.Max(0.01f, length));
                if (Mathf.Abs(picked - time) > 0.05f) video.Seek(picked);
                GUILayout.Label(Clock(time) + " / " + Clock(length), EditorStyles.miniLabel, GUILayout.Width(70));
            }
            if (current.Start >= 0)
                GUILayout.Label("Click a step's time to see that moment.", EditorStyles.miniLabel);
#else
            EditorGUILayout.HelpBox("The Video module is disabled in this project, so Open plays the video in the system player.", MessageType.Info);
#endif
        }

        void Seek(SessionView session, int index, double seconds)
        {
#if AIPM_VIDEO
            var path = session.Videos[index].Path;
            videoIndex = index;
            if (video != null && SamePath(video.Path, path) && !video.Failed)
            {
                video.Seek(seconds);
                return;
            }
            StopVideo();
            video = new VideoPreview(path, seconds);
#endif
        }

        void Delete(SessionView session)
        {
            if (!EditorUtility.DisplayDialog("Delete playtest", "Delete " + Path.GetFileName(session.Folder) + " with its screenshots and video?", "Delete", "Cancel")) return;
            StopVideo();
            ClearImages();
            try { Directory.Delete(session.Folder, true); }
            catch (Exception e) { Debug.LogError("[AI PM] Could not delete " + session.Folder + ": " + e.Message); }
            selectedFolder = null;
            Scan();
            GUIUtility.ExitGUI();
        }

        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, wordWrap = true };
            wrapStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
            okStyle = new GUIStyle(wrapStyle);
            okStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.40f, 0.80f, 0.45f) : new Color(0.10f, 0.50f, 0.20f);
            warnStyle = new GUIStyle(wrapStyle);
            warnStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.98f, 0.66f, 0.20f) : new Color(0.70f, 0.40f, 0.00f);
            errorStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            errorStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.95f, 0.40f, 0.40f) : new Color(0.75f, 0.10f, 0.10f);
        }

        static string Clock(float seconds) => ((int)(seconds / 60)) + ":" + ((int)(seconds % 60)).ToString("00");

        // ------------------------------------------------------------------ live session and video ticking

        void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            var live = PlaytestSession.instance;
            var first = sessions.Count > 0 ? sessions[0] : null;
            bool listShowsLive = first != null && first.Live;
            if (live.IsActive != listShowsLive || (listShowsLive && first.Steps.Count != live.Steps.Count))
            {
                Scan();
                Repaint();
            }
            else if (listShowsLive && now - lastLiveRepaint > 1.0)
            {
                // The elapsed time in the header.
                lastLiveRepaint = now;
                Repaint();
            }
#if AIPM_VIDEO
            if (video == null)
            {
                videoWasBusy = false;
                return;
            }
            video.Tick();
            bool busy = video.Busy;
            if (busy)
            {
                // Edit mode runs the player loop, and with it the VideoPlayer, only when asked to.
                if (!EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
                if (now - lastVideoRepaint > 1.0 / 30.0)
                {
                    lastVideoRepaint = now;
                    Repaint();
                }
            }
            else if (videoWasBusy)
            {
                Repaint();
            }
            videoWasBusy = busy;
#endif
        }

        void StopVideo()
        {
#if AIPM_VIDEO
            video?.Dispose();
            video = null;
#endif
        }

#if AIPM_VIDEO
        /// <summary>
        /// A hidden VideoPlayer decoding one MP4 for the window to draw. It runs in the player loop, which Edit mode runs
        /// only on request (the window asks every tick) and only while Unity is the active app, so the preview waits
        /// while Unity is in the background and holds playback until it is back.
        /// </summary>
        sealed class VideoPreview : IDisposable
        {
            /// <summary>Seconds of running player loop allowed for opening the file.</summary>
            const double PrepareTimeout = 15;

            GameObject host;
            VideoPlayer player;
            double pendingSeek;
            bool holdOnNextFrame;
            bool playPending;
            int playAtFrame;
            bool resumeOnFocus;
            int lastFrame;
            double lastFrameAt;
            double lastTickAt;
            double preparingFor;

            public VideoPreview(string path, double seekTo)
            {
                Path = path;
                pendingSeek = seekTo;
                lastFrame = UnityEngine.Time.frameCount;
                lastFrameAt = lastTickAt = EditorApplication.timeSinceStartup;
                host = EditorUtility.CreateGameObjectWithHideFlags("AI PM video preview", HideFlags.HideAndDontSave, typeof(VideoPlayer));
                player = host.GetComponent<VideoPlayer>();
                player.playOnAwake = false;
                player.source = VideoSource.Url;
                player.url = path;
                player.renderMode = VideoRenderMode.APIOnly;
                player.audioOutputMode = VideoAudioOutputMode.None;
                player.isLooping = false;
                player.skipOnDrop = true;
                player.errorReceived += OnError;
                player.prepareCompleted += OnPrepared;
                player.frameReady += OnFrameReady;
                player.Prepare();
            }

            public string Path { get; }
            public bool Failed { get; private set; }
            public string Error { get; private set; }
            public bool Ready => player != null && player.isPrepared && player.texture != null;
            public bool IsPlaying => player != null && (player.isPlaying || playPending || resumeOnFocus) && !holdOnNextFrame;
            public Texture Texture => player != null ? player.texture : null;
            public float Aspect => player != null && player.height > 0 ? (float)player.width / player.height : 0f;
            public double Length => player != null ? player.length : 0;
            public double Time => player != null ? player.time : 0;

            /// <summary>Needs player-loop updates: preparing, about to play, playing, or decoding the frame to hold.</summary>
            public bool Busy => player != null && !Failed && (!player.isPrepared || player.isPlaying || playPending);

            /// <summary>Busy, but the player loop has not run for a second: Unity is in the background.</summary>
            public bool Waiting => Busy && EditorApplication.timeSinceStartup - lastFrameAt > 1.0;

            public void Tick()
            {
                if (player == null || Failed) return;
                double now = EditorApplication.timeSinceStartup;
                bool advanced = UnityEngine.Time.frameCount != lastFrame;
                if (advanced)
                {
                    lastFrame = UnityEngine.Time.frameCount;
                    lastFrameAt = now;
                }
                double elapsed = now - lastTickAt;
                lastTickAt = now;
                if (!player.isPrepared)
                {
                    // Only time with a running player loop counts: opening the file happens there.
                    if (advanced) preparingFor += Math.Min(elapsed, 0.5);
                    if (preparingFor < PrepareTimeout) return;
                    Failed = true;
                    Error = "timed out opening the file";
                    return;
                }
                if (playPending && UnityEngine.Time.frameCount >= playAtFrame)
                {
                    playPending = false;
                    player.Play();
                }
            }

            /// <summary>Holds playback while Unity is in the background; its clock would jump when it is back.</summary>
            public void OnAppFocus(bool focused)
            {
                if (player == null || !player.isPrepared) return;
                if (!focused)
                {
                    if (!IsPlaying || resumeOnFocus) return;
                    playPending = false;
                    player.Pause();
                    resumeOnFocus = true;
                }
                else if (resumeOnFocus)
                {
                    resumeOnFocus = false;
                    PlaySoon();
                }
            }

            public void Toggle()
            {
                if (player == null) return;
                if (IsPlaying)
                {
                    playPending = false;
                    resumeOnFocus = false;
                    player.Pause();
                    return;
                }
                HoldNextFrame(false);
                if (player.time >= player.length - 0.1) player.time = 0;
                PlaySoon();
            }

            /// <summary>Jumps to a time; when paused, decodes that frame and holds it.</summary>
            public void Seek(double seconds)
            {
                if (player == null) return;
                if (!player.isPrepared)
                {
                    pendingSeek = seconds;
                    return;
                }
                bool playing = IsPlaying;
                player.time = Math.Max(0, Math.Min(seconds, player.length));
                if (playing) return;
                HoldNextFrame(true);
                PlaySoon();
            }

            /// <summary>
            /// Plays once the player loop has run twice more. Its clock stands still while it does not run, and the first
            /// update after a pause would count the whole pause as playback and skip ahead.
            /// </summary>
            void PlaySoon()
            {
                playPending = true;
                playAtFrame = UnityEngine.Time.frameCount + 2;
            }

            void HoldNextFrame(bool hold)
            {
                holdOnNextFrame = hold;
                player.sendFrameReadyEvents = hold;
            }

            void OnError(VideoPlayer source, string message)
            {
                Failed = true;
                Error = message;
            }

            void OnPrepared(VideoPlayer source)
            {
                if (pendingSeek > 0) source.time = Math.Min(pendingSeek, source.length);
                pendingSeek = 0;
                HoldNextFrame(true);
                PlaySoon();
            }

            void OnFrameReady(VideoPlayer source, long frame)
            {
                if (!holdOnNextFrame) return;
                HoldNextFrame(false);
                source.Pause();
            }

            public void Dispose()
            {
                if (player != null)
                {
                    player.errorReceived -= OnError;
                    player.prepareCompleted -= OnPrepared;
                    player.frameReady -= OnFrameReady;
                    player.Stop();
                }
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                player = null;
                host = null;
            }
        }
#endif
    }
}
