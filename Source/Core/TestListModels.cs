// TestList -- shared data model.
//
// THE CONTRACT FILE. Every other source file in this mod codes against the types
// and signatures declared here. Do not change them without updating all callers.
//
// Editing rule: only the Lead edits this file.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TestList
{
    /// <summary>Test verdict for one checklist entry.</summary>
    public enum TestStatus
    {
        Untested = 0,
        Pass = 1,
        Fail = 2
    }

    public static class TestStatusUtil
    {
        /// <summary>Marker token used in feedback documents. Stable on-disk format.</summary>
        public static string Token(TestStatus status)
        {
            switch (status)
            {
                case TestStatus.Pass: return "pass";
                case TestStatus.Fail: return "fail";
                default: return "untested";
            }
        }

        public static bool TryParseToken(string token, out TestStatus status)
        {
            status = TestStatus.Untested;
            // string.IsNullOrEmpty, NOT the Verse NullOrEmpty extension: this file is
            // compiled both in-game and headless (TESTLIST_NO_VERSE), and must not
            // depend on a Verse extension method being in scope.
            if (string.IsNullOrEmpty(token)) return false;
            switch (token.Trim().ToLowerInvariant())
            {
                case "pass": case "ok": case "normal": case "good": case "p":
                    status = TestStatus.Pass; return true;
                case "fail": case "broken": case "error": case "bad": case "f":
                    status = TestStatus.Fail; return true;
                case "untested": case "none": case "todo": case "?":
                    status = TestStatus.Untested; return true;
                default:
                    return false;
            }
        }

        /// <summary>Short text drawn inside the 3-way marker bar.</summary>
        public static string ShortLabel(TestStatus status)
        {
            switch (status)
            {
                case TestStatus.Pass: return TestListText.StatusPass;
                case TestStatus.Fail: return TestListText.StatusFail;
                default: return TestListText.StatusUntested;
            }
        }

        /// <summary>Multi-character label used when there is room for it.</summary>
        public static string LongLabel(TestStatus status)
        {
            switch (status)
            {
                case TestStatus.Pass: return TestListText.StatusPassLong;
                case TestStatus.Fail: return TestListText.StatusFailLong;
                default: return TestListText.StatusUntestedLong;
            }
        }

        /// <summary>Single-character badge used in collapsed rows and summaries.</summary>
        public static string Badge(TestStatus status)
        {
            switch (status)
            {
                case TestStatus.Pass: return "[v]";
                case TestStatus.Fail: return "[x]";
                default: return "[ ]";
            }
        }
    }

    /// <summary>One markable checklist entry: a heading plus the content beneath it.</summary>
    public class TestEntry
    {
        /// <summary>Stable identity used to match feedback documents back onto input documents.</summary>
        public string Id;

        /// <summary>Heading text with markdown emphasis/links stripped.</summary>
        public string Title = "";

        /// <summary>Original heading line, e.g. "## 3. SAM launcher fires".</summary>
        public string RawHeading = "";

        /// <summary>Markdown heading level (1-6); 0 for a filename-synthesised entry.</summary>
        public int Level = 1;

        /// <summary>Body markdown between this heading and the next heading.</summary>
        public string Content = "";

        /// <summary>Zero-based source line of the heading, for display only.</summary>
        public int SourceLine;

        /// <summary>Parent entry id, or null for a top-level entry.</summary>
        public string ParentId;

        /// <summary>Title-path from the document root, e.g. "防空 / SAM / 3. 发射".</summary>
        public string Path = "";

        public TestStatus Status = TestStatus.Untested;
        public string Feedback = "";

        /// <summary>Last in-game modification time (UTC). Null when never touched in game.</summary>
        public DateTime? UpdatedUtc;

        /// <summary>True when the player changed the status or the feedback text in game.</summary>
        public bool Dirty;
    }

    /// <summary>One parsed input document (.md / .xml) from a mod's /TestList folder.</summary>
    public class TestDocument
    {
        /// <summary>Absolute path of the source file.</summary>
        public string SourcePath = "";

        /// <summary>File name without extension. Used as the first level of the checklist.</summary>
        public string FileBaseName = "";

        /// <summary>File name with extension.</summary>
        public string FileName = "";

        /// <summary>Display name of the mod this document came from.</summary>
        public string ModName = "";

        /// <summary>PackageId of the mod this document came from.</summary>
        public string ModPackageId = "";

        /// <summary>Root directory of the mod this document came from.</summary>
        public string ModRootDir = "";

        /// <summary>
        /// Absolute path of the feedback file for this document. Set by the scanner via
        /// TestListScanner.GetFeedbackPath so the UI and the manager never re-derive it.
        /// </summary>
        public string FeedbackPath = "";

        public bool IsXml;

        /// <summary>Document title: first level-1 heading, else the file base name.</summary>
        public string Title = "";

        public readonly List<TestEntry> Entries = new List<TestEntry>();

        /// <summary>Per-file fold state in the UI (true = collapsed).</summary>
        public bool Collapsed;

        /// <summary>Non-null when the file could not be parsed.</summary>
        public string ParseError;
    }

    /// <summary>All documents found during one scan.</summary>
    public class TestListData
    {
        public readonly List<TestDocument> Documents = new List<TestDocument>();

        /// <summary>Mods whose /TestList folder was scanned, whether or not it had files.</summary>
        public readonly List<string> ScannedModNames = new List<string>();

        public DateTime LoadedUtc = DateTime.UtcNow;

        public string LastError;

        public int EntryCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Documents.Count; i++) n += Documents[i].Entries.Count;
                return n;
            }
        }

        /// <summary>True when nothing at all was found.</summary>
        public bool IsEmpty
        {
            get { return Documents.Count == 0; }
        }
    }

    /// <summary>
    /// The feedback document as read back from disk. Keyed by entry id so later
    /// "untested" marks are distinguishable from "no record at all".
    /// </summary>
    public class FeedbackData
    {
        public readonly Dictionary<string, TestStatus> Statuses =
            new Dictionary<string, TestStatus>(StringComparer.Ordinal);

        public readonly Dictionary<string, string> Feedbacks =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public readonly Dictionary<string, DateTime> UpdatedUtc =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);

        public bool IsEmpty
        {
            get { return Statuses.Count == 0 && Feedbacks.Count == 0; }
        }
    }

    /// <summary>Merge policy when a freshly parsed input document meets an existing feedback file.</summary>
    public enum FeedbackMergeMode
    {
        /// <summary>Existing feedback wins (normal game start).</summary>
        Restore = 0,
        /// <summary>Input file wins; stale feedback keys are dropped (explicit refresh).</summary>
        Rebuild = 1
    }

#if TESTLIST_NO_VERSE
    /// <summary>
    /// String helpers used by the pure-logic sources. In-game these come from Verse's
    /// GenText extensions; the headless verification build needs its own copies.
    /// </summary>
    internal static class TestListStringExtensions
    {
        internal static bool NullOrEmpty(this string s)
        {
            return string.IsNullOrEmpty(s);
        }

        internal static bool NullOrEmpty(this System.Collections.Generic.List<string> list)
        {
            return list == null || list.Count == 0;
        }

        internal static bool NotNull(this string s)
        {
            return !string.IsNullOrEmpty(s);
        }
    }

    /// <summary>
    /// Headless stand-in for the real TestListSettings, which derives from Verse.ModSettings
    /// and therefore cannot compile without RimWorld/UnityEngine. Only the members the
    /// pure-logic sources actually read are mirrored here.
    ///
    /// This is the ONLY definition of TestListSettings in the headless build - do not add
    /// another one, or the harness will fail with CS0101 (duplicate class).
    /// </summary>
    public class TestListSettings
    {
        public bool Enabled = true;
        public bool WriteCombinedPerMod = true;
        public bool NeedsReload = false;
        public List<string> ExtraScanPaths = new List<string>();
    }

    /// <summary>
    /// Strings used by the shared model. The in-game build replaces this with a
    /// Verse-translated version (see Localization/TestListText.cs). The headless
    /// verification build has no Translator, so it falls back to fixed Chinese text
    /// matching the shipped language files.
    /// </summary>
    internal static class TestListText
    {
        internal const string StatusPass = "正常";
        internal const string StatusFail = "异常";
        internal const string StatusUntested = "未测试";

        internal const string StatusPassLong = "正常工作";
        internal const string StatusFailLong = "异常";
        internal const string StatusUntestedLong = "未测试";

        internal const string NoFeedback = "（无反馈）";
    }
#endif
}
