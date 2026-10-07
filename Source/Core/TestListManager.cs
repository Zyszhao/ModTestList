// TestList -- central runtime state.
//
// Owned by the Lead. This is the only place that mutates TestEntry status/feedback,
// and the only place that decides when a feedback file is written. The UI must go
// through this class rather than touching entries or files directly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace TestList
{
    public enum TestListSaveState
    {
        Idle = 0,
        Pending = 1,
        Error = 2
    }

    public static class TestListManager
    {
        private static TestListData data;
        private static bool initialized;
        private static string lastError;

        /// <summary>Absolute paths whose feedback file failed to write, for the UI to show.</summary>
        private static readonly HashSet<string> failedOutputs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static float lastEditRealtime = -1f;
        private static bool dirty;
        private static TestListSaveState saveState = TestListSaveState.Idle;

        public static TestListSettings Settings
        {
            get { return TestListMod.Settings; }
        }

        /// <summary>Never null; returns an empty dataset before the first successful load.</summary>
        public static TestListData Data
        {
            get
            {
                if (data == null) data = new TestListData();
                return data;
            }
        }

        public static bool HasLoaded
        {
            get { return initialized && data != null; }
        }

        public static string LastError
        {
            get { return lastError; }
        }

        public static TestListSaveState SaveState
        {
            get { return saveState; }
        }

        /// <summary>True when edits are waiting to be flushed to disk.</summary>
        public static bool HasUnsavedChanges
        {
            get { return dirty; }
        }

        public static ICollection<string> FailedOutputs
        {
            get { return failedOutputs; }
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        /// <summary>Loads once, lazily. Safe to call from OnGUI and from a Harmony hook.</summary>
        public static void EnsureInitialized()
        {
            if (initialized) return;
            Load();
        }

        /// <summary>Full rescan: parse every source file and re-apply saved feedback.</summary>
        public static void Load()
        {
            initialized = true;
            lastError = null;
            failedOutputs.Clear();
            dirty = false;
            saveState = TestListSaveState.Idle;

            try
            {
                TestListData loaded = TestListScanner.Scan(Settings);
                ApplyStoredFeedback(loaded, FeedbackMergeMode.Restore);
                data = loaded;
                if (loaded.LastError != null) lastError = loaded.LastError;
                TestListLog.Message(string.Format(
                    "Loaded {0} checklist document(s), {1} entry(ies) from {2} enabled mod(s).",
                    loaded.Documents.Count, loaded.EntryCount, loaded.ScannedModNames.Count));
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                TestListLog.Exception("Failed to scan for /TestList folders", ex);
            }
        }

        /// <summary>
        /// Discards every stored mark and feedback, then re-reads the source files.
        /// Used by the "clear all marks" button.
        /// </summary>
        public static void ClearAllMarks()
        {
            TestListData d = Data;
            for (int i = 0; i < d.Documents.Count; i++)
            {
                TestDocument doc = d.Documents[i];
                for (int j = 0; j < doc.Entries.Count; j++)
                {
                    TestEntry e = doc.Entries[j];
                    e.Status = TestStatus.Untested;
                    e.Feedback = "";
                    e.UpdatedUtc = null;
                    e.Dirty = true;
                }
                MarkDirty(doc);
            }
            SaveNow();
        }

        // ------------------------------------------------------------------
        // Lookup
        // ------------------------------------------------------------------

        public static TestDocument FindDocument(string sourcePath)
        {
            if (sourcePath.NullOrEmpty()) return null;
            TestListData d = Data;
            for (int i = 0; i < d.Documents.Count; i++)
            {
                if (string.Equals(d.Documents[i].SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
                    return d.Documents[i];
            }
            return null;
        }

        public static string GetOutputPath(TestDocument doc)
        {
            if (doc == null) return "";
            return FeedbackDocuments.GetOutputPath(doc);
        }

        /// <summary>Aggregate counts over the whole dataset. Cheap enough for OnGUI.</summary>
        public static void CountStatuses(out int total, out int pass, out int fail, out int untested)
        {
            total = pass = fail = untested = 0;
            TestListData d = Data;
            for (int i = 0; i < d.Documents.Count; i++)
            {
                List<TestEntry> es = d.Documents[i].Entries;
                for (int j = 0; j < es.Count; j++)
                {
                    total++;
                    switch (es[j].Status)
                    {
                        case TestStatus.Pass: pass++; break;
                        case TestStatus.Fail: fail++; break;
                        default: untested++; break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Mutation -- the only sanctioned way to change a mark
        // ------------------------------------------------------------------

        public static void SetStatus(TestDocument doc, TestEntry entry, TestStatus status)
        {
            if (entry == null) return;
            if (entry.Status == status && entry.UpdatedUtc.HasValue) return;
            entry.Status = status;
            entry.UpdatedUtc = DateTime.UtcNow;
            entry.Dirty = true;
            if (doc != null) MarkDirty(doc);
        }

        public static void SetFeedback(TestDocument doc, TestEntry entry, string feedback)
        {
            if (entry == null) return;
            string v = feedback ?? "";
            if (string.Equals(entry.Feedback, v, StringComparison.Ordinal)) return;
            entry.Feedback = v;
            entry.UpdatedUtc = DateTime.UtcNow;
            entry.Dirty = true;
            if (doc != null) MarkDirty(doc);
        }

        /// <summary>Bulk-mark every visible (already filtered) entry. Caller passes the list.</summary>
        public static void SetStatusBulk(List<TestEntry> entries, List<TestDocument> docs, TestStatus status)
        {
            if (entries == null) return;
            for (int i = 0; i < entries.Count; i++)
            {
                TestEntry e = entries[i];
                e.Status = status;
                e.UpdatedUtc = DateTime.UtcNow;
                e.Dirty = true;
                TestDocument doc = docs != null && i < docs.Count ? docs[i] : null;
                if (doc != null) MarkDirty(doc);
            }
        }

        // ------------------------------------------------------------------
        // Saving
        // ------------------------------------------------------------------

        /// <summary>Marks a document for auto-save and (re)arms the debounce timer.</summary>
        public static void MarkDirty(TestDocument doc)
        {
            if (doc == null) return;
            dirty = true;
            saveState = TestListSaveState.Pending;
            lastEditRealtime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Called every frame from a TickManager patch. Writes the feedback file once the
        /// player has stopped editing for EffectiveAutoSaveDelay seconds.
        /// </summary>
        public static void TickAutoSave()
        {
            if (!dirty) return;
            if (lastEditRealtime < 0f) return;
            if (Time.realtimeSinceStartup - lastEditRealtime < Settings.EffectiveAutoSaveDelay) return;
            SaveNow();
        }

        /// <summary>Flushes every touched document immediately. Called on quit and on demand.</summary>
        public static void SaveNow()
        {
            if (!dirty)
            {
                saveState = TestListSaveState.Idle;
                return;
            }

            bool anyFailure = false;
            TestListData d = Data;
            HashSet<string> modsTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < d.Documents.Count; i++)
            {
                TestDocument doc = d.Documents[i];
                bool docDirty = false;
                for (int j = 0; j < doc.Entries.Count; j++)
                {
                    if (doc.Entries[j].Dirty) { docDirty = true; break; }
                }
                if (!docDirty) continue;

                try
                {
                    FeedbackDocuments.SaveDocument(doc);
                    for (int j = 0; j < doc.Entries.Count; j++) doc.Entries[j].Dirty = false;
                    modsTouched.Add(doc.ModRootDir);
                }
                catch (Exception ex)
                {
                    anyFailure = true;
                    failedOutputs.Add(FeedbackDocuments.GetOutputPath(doc));
                    TestListLog.Exception("Failed to write feedback for " + doc.FileName, ex);
                }
            }

            if (Settings.WriteCombinedPerMod)
            {
                foreach (string modRoot in modsTouched)
                {
                    try
                    {
                        FeedbackDocuments.SaveCombined(modRoot, d);
                    }
                    catch (Exception ex)
                    {
                        anyFailure = true;
                        TestListLog.Exception("Failed to write combined feedback for " + modRoot, ex);
                    }
                }
            }

            dirty = anyFailure;
            saveState = anyFailure ? TestListSaveState.Error : TestListSaveState.Idle;
            if (!anyFailure)
            {
                TestListLog.Message("Feedback saved.");
            }
        }

        /// <summary>Forces a flush; used when the game shuts down.</summary>
        public static void FlushOnQuit()
        {
            if (dirty) SaveNow();
        }

        // ------------------------------------------------------------------
        // Internal
        // ------------------------------------------------------------------

        /// <summary>
        /// Applies the on-disk feedback documents on top of a freshly parsed dataset.
        /// </summary>
        internal static void ApplyStoredFeedback(TestListData target, FeedbackMergeMode mode)
        {
            if (target == null) return;
            for (int i = 0; i < target.Documents.Count; i++)
            {
                TestDocument doc = target.Documents[i];
                FeedbackData fb;
                try
                {
                    fb = FeedbackDocuments.ReadFeedback(doc);
                }
                catch (Exception ex)
                {
                    TestListLog.Exception("Failed to read feedback for " + doc.FileName, ex);
                    continue;
                }
                if (fb == null || fb.IsEmpty) continue;

                for (int j = 0; j < doc.Entries.Count; j++)
                {
                    TestEntry e = doc.Entries[j];
                    TestStatus s;
                    if (fb.Statuses.TryGetValue(e.Id, out s))
                    {
                        e.Status = s;
                        DateTime u;
                        if (fb.UpdatedUtc.TryGetValue(e.Id, out u)) e.UpdatedUtc = u;
                    }
                    else if (mode == FeedbackMergeMode.Rebuild)
                    {
                        e.Status = TestStatus.Untested;
                        e.UpdatedUtc = null;
                    }

                    string text;
                    if (fb.Feedbacks.TryGetValue(e.Id, out text))
                    {
                        e.Feedback = text;
                    }
                    else if (mode == FeedbackMergeMode.Rebuild)
                    {
                        e.Feedback = "";
                    }

                    e.Dirty = false;
                }
            }
        }
    }
}
