using System;
using UnityEngine;

namespace Alvaris.AiProductManager
{
    /// <summary>Mouse button that opens the report popup.</summary>
    public enum PointerButton
    {
        Left = 0,
        Right = 1,
        Middle = 2,
    }

    /// <summary>What lands on the clipboard when a report is filed.</summary>
    public enum ClipboardContent
    {
        Nothing,
        /// <summary>A few lines: category, target, findings and the report path. Paste it into Claude Code.</summary>
        Summary,
        /// <summary>The whole Markdown report.</summary>
        FullReport,
    }

    /// <summary>
    /// Everything the in-game overlay needs to know. The Editor settings page (Project Settings > AI Product Manager)
    /// produces one of these; a build that installs the overlay by hand can construct it directly.
    /// </summary>
    [Serializable]
    public sealed class ProductManagerConfig
    {
        [Header("Trigger (Game view, Play mode)")]
        [Tooltip("Mouse button that opens the report popup over the element under the pointer.")]
        public PointerButton triggerButton = PointerButton.Right;
        public bool triggerNeedsCtrl;
        public bool triggerNeedsAlt;
        public bool triggerNeedsShift;

        [Header("Popup")]
        [Tooltip("What can be wrong. Each entry is a one-click button in the popup.")]
        public string[] categories = DefaultCategories();
        [Tooltip("Size multiplier for the in-game popup. 0 = automatic (based on the game resolution).")]
        public float overlayScale;
        [Tooltip("While the popup is open the EventSystem is disabled, so clicking a category does not also click the UI underneath.")]
        public bool disableEventSystemWhilePopupOpen = true;

        [Header("Report content")]
        [Tooltip("Capture the Game view (with the target outlined) and save it next to the report.")]
        public bool captureScreenshot = true;
        [Tooltip("Console entries younger than this many seconds are included in the report.")]
        public int consoleLogSeconds = 60;
        public int maxConsoleEntries = 25;

        [HideInInspector]
        [Tooltip("Absolute folder that receives Inbox/ and Resolved/. Empty = Application.persistentDataPath/AIProductManager.")]
        public string reportsRoot = "";
        [HideInInspector]
        public ClipboardContent clipboard = ClipboardContent.Summary;

        public static string[] DefaultCategories() => new[]
        {
            "Button doesn't work",
            "Wrong text or value",
            "Wrong position or size",
            "Wrong look (colour, sprite, font)",
            "Overlaps or hidden behind something",
            "Missing feedback (sound, animation)",
            "Should not be visible now",
            "Something else",
        };

        public ProductManagerConfig Clone()
        {
            var copy = (ProductManagerConfig)MemberwiseClone();
            copy.categories = categories != null && categories.Length > 0 ? (string[])categories.Clone() : DefaultCategories();
            return copy;
        }
    }
}
