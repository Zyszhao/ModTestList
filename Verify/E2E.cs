// TestList -- end-to-end acceptance driver.
//
// Runs the complete real-world loop against an actual /TestList folder on disk:
//   scan -> assert the document was found with the expected entry count
//        -> simulate an in-game mark + feedback
//        -> write the feedback file
//        -> rescan and assert the feedback file is NOT re-ingested
//        -> read the feedback back and assert the marks reconnected by id
//
// Compiled exactly like the mod's pure-logic sources (TESTLIST_NO_VERSE) against the
// standard net48 reference assemblies.
using System;
using System.Collections.Generic;
using System.IO;

namespace TestList.Verify
{
    internal static class E2E
    {
        private static int failures;

        private static int Main(string[] args)
        {
            string folder = null;
            int expectedMinEntries = 1;
            string expectTitleFragment = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--folder" && i + 1 < args.Length) folder = args[i + 1];
                else if (args[i] == "--min-entries" && i + 1 < args.Length) int.TryParse(args[i + 1], out expectedMinEntries);
                else if (args[i] == "--title-contains" && i + 1 < args.Length) expectTitleFragment = args[i + 1];
            }

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                Console.Error.WriteLine("usage: TestList.E2E --folder <dir> [--min-entries N] [--title-contains X]");
                return 2;
            }

            Console.WriteLine("E2E folder : " + folder);
            Console.WriteLine("CLR        : " + Environment.Version + " / " +
                System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Console.WriteLine(new string('-', 72));

            // ---- record pre-existing state so the run is repeatable ----
            var preExisting = new List<string>();
            foreach (string f in Directory.GetFiles(folder, "*.feedback.md", SearchOption.AllDirectories))
            {
                preExisting.Add(f);
            }
            Console.WriteLine("pre-existing feedback files: " + preExisting.Count);

            // ---- 1. scan ----
            var folders = new List<string> { folder };
            TestListData data = TestListScanner.ScanPaths(folders);
            Check(data != null, "scan returns a dataset");
            if (data == null) return Finish();

            Check(data.Documents.Count == 1, "exactly 1 document found (got " + data.Documents.Count + ")");
            if (data.Documents.Count != 1) return Finish();

            TestDocument doc = data.Documents[0];
            Console.WriteLine("document   : " + doc.FileName + "  (" + doc.Entries.Count + " entries)");
            Console.WriteLine("title      : " + doc.Title);
            Console.WriteLine("feedback   : " + doc.FeedbackPath);

            Check(doc.FileName == "防空系统.md", "file name is 防空系统.md");
            Check(doc.FileBaseName == "防空系统", "base name is 防空系统");
            Check(!doc.IsXml, "parsed as markdown");
            Check(doc.Entries.Count >= expectedMinEntries,
                "at least " + expectedMinEntries + " entries (got " + doc.Entries.Count + ")");
            if (!string.IsNullOrEmpty(expectTitleFragment))
            {
                Check(doc.Title != null && doc.Title.Contains(expectTitleFragment),
                    "document title contains '" + expectTitleFragment + "'");
            }

            // ---- 2. sanity-check the parsed structure ----
            int childless = 0, withParent = 0, emptyTitles = 0, withBody = 0;
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            bool prefixOk = true;
            for (int i = 0; i < doc.Entries.Count; i++)
            {
                TestEntry e = doc.Entries[i];
                if (string.IsNullOrEmpty(e.ParentId)) childless++; else withParent++;
                if (string.IsNullOrEmpty(e.Title)) emptyTitles++;
                if (!string.IsNullOrEmpty(e.Content)) withBody++;
                if (e.Id == null || !e.Id.StartsWith("md:", StringComparison.Ordinal)) prefixOk = false;
                seenIds.Add(e.Id);
            }
            Check(emptyTitles == 0, "every entry has a non-empty title");
            Check(prefixOk, "every entry id has the 'md:' prefix");
            Check(seenIds.Count == doc.Entries.Count, "all ids are unique (" + seenIds.Count + "/" + doc.Entries.Count + ")");
            Check(childless >= 1, "at least one top-level entry (got " + childless + ")");
            Check(withParent >= 1, "at least one nested entry (got " + withParent + ")");
            Check(withBody >= 1, "at least one entry has body text (got " + withBody + ")");

            // ---- 3. simulate an in-game mark + feedback ----
            TestEntry first = null;
            for (int i = 0; i < doc.Entries.Count; i++)
            {
                if (string.IsNullOrEmpty(doc.Entries[i].ParentId)) { first = doc.Entries[i]; break; }
            }
            Check(first != null, "found a top-level entry to mark");
            if (first == null) return Finish();

            const string feedbackText = "E2E 自动化检查：断言 id 能跨重扫还原。";
            first.Status = TestStatus.Fail;
            first.Feedback = feedbackText;
            first.UpdatedUtc = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc);
            string markedId = first.Id;
            Console.WriteLine("marking    : [" + markedId.Substring(0, Math.Min(20, markedId.Length)) + "...] " + first.Title);

            // ---- 4. write the feedback file ----
            string written = null;
            try
            {
                written = FeedbackDocuments.SaveDocument(doc);
                Check(File.Exists(written), "feedback file written: " + written);
            }
            catch (Exception ex)
            {
                Check(false, "SaveDocument threw " + ex.GetType().Name + ": " + ex.Message);
                return Finish();
            }

            byte[] bytes = File.ReadAllBytes(written);
            Check(!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF),
                "written file has no UTF-8 BOM");
            string raw = System.Text.Encoding.UTF8.GetString(bytes);
            Check(raw.StartsWith("<!-- TestList feedback v1 -->\n", StringComparison.Ordinal),
                "first line is exactly the feedback marker + LF");
            Check(raw.IndexOf('\r') < 0, "no CR bytes (LF line endings)");

            // ---- 5. rescan: the feedback file must NOT be ingested ----
            TestListData rescan = TestListScanner.ScanPaths(folders);
            Check(rescan.Documents.Count == 1,
                "rescan still finds exactly 1 document (got " + rescan.Documents.Count + ")");
            for (int i = 0; i < rescan.Documents.Count; i++)
            {
                Check(!rescan.Documents[i].FileName.EndsWith(".feedback.md", StringComparison.OrdinalIgnoreCase),
                    "no feedback document was ingested");
            }

            // ---- 6. read the marks back and prove the ids reconnect ----
            TestDocument again = rescan.Documents.Count == 1 ? rescan.Documents[0] : null;
            Check(again != null, "rescan produced a document");
            if (again != null)
            {
                Check(again.Entries.Count == doc.Entries.Count,
                    "entry count unchanged after rescan (" + again.Entries.Count + ")");
                FeedbackData fb = FeedbackDocuments.ReadFeedback(again);
                TestStatus st;
                Check(fb.Statuses.TryGetValue(markedId, out st) && st == TestStatus.Fail,
                    "the Fail mark was restored by id");
                string text;
                Check(fb.Feedbacks.TryGetValue(markedId, out text) && text == feedbackText,
                    "the feedback text was restored by id");
            }

            // ---- 7. leave the folder exactly as we found it ----
            try
            {
                if (File.Exists(written) && !preExisting.Contains(written))
                {
                    File.Delete(written);
                    Console.WriteLine("cleanup    : removed generated " + Path.GetFileName(written));
                }
                string tmp = written + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch (Exception ex)
            {
                Console.WriteLine("cleanup warning: " + ex.Message);
            }

            return Finish();
        }

        private static void Check(bool ok, string what)
        {
            if (ok)
            {
                Console.WriteLine("  ok   " + what);
            }
            else
            {
                failures++;
                Console.WriteLine("  FAIL " + what);
            }
        }

        private static int Finish()
        {
            Console.WriteLine(new string('-', 72));
            if (failures == 0)
            {
                Console.WriteLine("E2E: ALL CHECKS PASSED");
                return 0;
            }
            Console.WriteLine("E2E: " + failures + " CHECK(S) FAILED");
            return 1;
        }
    }
}
