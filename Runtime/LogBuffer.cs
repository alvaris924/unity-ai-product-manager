using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alvaris.AiProductManager
{
    /// <summary>
    /// Ring buffer of recent console output, so a report can show what the game said around the time of the issue.
    /// The Editor installs it on load; a build that uses the overlay should call <see cref="Install"/> once at startup.
    /// </summary>
    public static class LogBuffer
    {
        public struct Entry
        {
            public DateTime Time;
            public LogType Type;
            public string Message;
            public string StackTrace;
            public int Frame;

            public bool IsError => Type == LogType.Error || Type == LogType.Exception || Type == LogType.Assert;
        }

        const int Capacity = 400;
        static readonly List<Entry> entries = new List<Entry>(Capacity);
        static bool installed;

        public static int Count => entries.Count;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            Application.logMessageReceived += OnLog;
        }

        public static void Clear() => entries.Clear();

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            // Skip our own chatter so a report does not quote the report that preceded it.
            if (condition != null && condition.StartsWith("[AI PM]", StringComparison.Ordinal)) return;
            if (entries.Count >= Capacity) entries.RemoveRange(0, Capacity / 4);
            entries.Add(new Entry
            {
                Time = DateTime.Now,
                Type = type,
                Message = condition,
                StackTrace = stackTrace,
                Frame = Time.frameCount,
            });
        }

        /// <summary>
        /// Entries from the last <paramref name="seconds"/>: errors and exceptions first, then the rest, newest first
        /// within each group, capped at <paramref name="max"/>.
        /// </summary>
        public static List<Entry> Recent(int seconds, int max)
        {
            var cutoff = DateTime.Now.AddSeconds(-Mathf.Max(1, seconds));
            var result = new List<Entry>();
            for (int i = entries.Count - 1; i >= 0 && result.Count < max; i--)
                if (entries[i].IsError && entries[i].Time >= cutoff) result.Add(entries[i]);
            for (int i = entries.Count - 1; i >= 0 && result.Count < max; i--)
                if (!entries[i].IsError && entries[i].Time >= cutoff) result.Add(entries[i]);
            return result;
        }
    }
}
