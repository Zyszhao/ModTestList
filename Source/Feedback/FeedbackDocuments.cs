// TestList -- feedback document writer / reader.
//
// THE ON-DISK FORMAT LIVES HERE AND IS FROZEN.
// Every file this class writes starts with the literal marker line
//
//     <!-- TestList feedback v1 -->
//
// which is exactly how the scanner (Source/Parsing/TestListScanner.cs) recognises our
// own output and refuses to re-ingest it as a checklist input. Changing that first line
// would make the mod feed its own feedback files back into the checklist: do not.
//
// Each entry block is keyed by an id marker line:
//
//     <!-- testlist:id=md:1a2b3c... -->
//
// ReadBack() keys purely on those marker lines, so the file survives hand editing:
// headings, bullet order and stray prose are all ignored.
//
// PURITY: this file is compiled twice -- in-game, and headless with TESTLIST_NO_VERSE
// defined (the Verify harness compiles Core + Parsing + Feedback only). It therefore
// uses ONLY System.*, the TestList model types and TestListLog. Never Verse,
// RimWorld or UnityEngine, and never Verse's string extension methods.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TestList
{
    public static class FeedbackDocuments
    {
        /// <summary>First line of every file this class writes. Frozen interface.</summary>
        public const string FileMarker = "<!-- TestList feedback v1 -->";

        /// <summary>Prefix of the per-entry id marker line. Frozen interface.</summary>
        public const string IdMarkerPrefix = "<!-- testlist:id=";

        /// <summary>Suffix of the per-entry id marker line (note the leading space).</summary>
        public const string IdMarkerSuffix = " -->";

        /// <summary>Suffix of one document's feedback file: "防空系统" -> "防空系统.feedback.md".</summary>
        public const string FeedbackFileSuffix = ".feedback.md";

        /// <summary>File name of the per-mod combined feedback document.</summary>
        public const string CombinedFileName = "TestList.feedback.md";

        /// <summary>Folder (inside a mod root) that is scanned for checklists and written to.</summary>
        public const string TestListFolderName = "TestList";

        /// <summary>UTC timestamp format used by both "- Generated:" and "- Updated:".</summary>
        private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

        /// <summary>Placeholder emitted for "no value" on the - Updated: / - Feedback: lines.</summary>
        private const string EmptyPlaceholder = "-";

        /// <summary>UTF-8 without a byte order mark: the file is read back by other tooling.</summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Decoding is deliberately lenient (throwOnInvalidBytes: false): a partially
        /// mangled file should still yield the feedback we can decode instead of making
        /// the whole document unreadable.
        /// </summary>
        private static readonly UTF8Encoding Utf8Lenient = new UTF8Encoding(false, false);

        private static readonly string[] LineSeparators = { "\r\n", "\n", "\r" };

        // ------------------------------------------------------------------
        // Paths
        // ------------------------------------------------------------------

        /// <summary>Absolute path of the feedback file for this document.</summary>
        public static string GetOutputPath(TestDocument doc)
        {
            if (doc == null) return "";
            try
            {
                // Normal case: the feedback file sits next to the checklist it describes,
                // i.e. inside the same /TestList folder.
                string dir = string.IsNullOrEmpty(doc.SourcePath)
                    ? ""
                    : Path.GetDirectoryName(doc.SourcePath);

                if (string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(doc.ModRootDir))
                {
                    // No usable source path (or a bare file name): fall back to the mod's
                    // /TestList folder, which is where a scanned document always lives.
                    dir = Path.Combine(doc.ModRootDir, TestListFolderName);
                }
                if (string.IsNullOrEmpty(dir)) return "";

                string baseName = doc.FileBaseName;
                if (string.IsNullOrEmpty(baseName) && !string.IsNullOrEmpty(doc.FileName))
                {
                    baseName = Path.GetFileNameWithoutExtension(doc.FileName);
                }
                if (string.IsNullOrEmpty(baseName)) return "";

                return Path.Combine(dir, baseName + FeedbackFileSuffix);
            }
            catch (Exception ex)
            {
                TestListLog.Exception("Failed to compute the feedback output path", ex);
                return "";
            }
        }

        // ------------------------------------------------------------------
        // Rendering -- pure string work, never touches disk
        // ------------------------------------------------------------------

        /// <summary>Renders the full feedback markdown for one document. Pure string work.</summary>
        public static string WriteDocument(TestDocument doc)
        {
            string generated = TimestampNow();
            string body = doc == null ? "" : RenderDocumentBody(doc, generated);
            return FileMarker + "\n" + body;
        }

        /// <summary>Renders one combined file covering every document that came from modRootDir.</summary>
        public static string WriteCombined(string modRootDir, TestListData data)
        {
            string generated = TimestampNow();
            List<TestDocument> docs = SelectDocuments(modRootDir, data);

            StringBuilder sb = new StringBuilder();
            sb.Append(FileMarker).Append('\n');
            for (int i = 0; i < docs.Count; i++)
            {
                if (i > 0) sb.Append('\n'); // one blank line between document blocks
                sb.Append(RenderDocumentBody(docs[i], generated));
            }
            return sb.ToString();
        }

        /// <summary>
        /// One document block, WITHOUT the leading file marker: the document header, then
        /// one block per entry in document order. Always ends with exactly one '\n'.
        /// </summary>
        private static string RenderDocumentBody(TestDocument doc, string generated)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("# ").Append(OneLine(DocumentTitle(doc))).Append('\n');
            sb.Append('\n');
            sb.Append("- Source: `").Append(OneLine(doc.FileName)).Append("`\n");
            sb.Append("- Mod: ").Append(OneLine(doc.ModName))
              .Append(" (`").Append(OneLine(doc.ModPackageId)).Append("`)\n");
            sb.Append("- Generated: ").Append(generated).Append('\n');

            List<TestEntry> entries = doc.Entries;
            if (entries == null || entries.Count == 0) return sb.ToString();

            sb.Append('\n');
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append('\n'); // one blank line between entry blocks
                AppendEntryBlock(sb, entries[i], i + 1);
            }
            return sb.ToString();
        }

        /// <summary>One entry block: "## n. title", blank line, id marker, then the fields.</summary>
        private static void AppendEntryBlock(StringBuilder sb, TestEntry entry, int number)
        {
            // Mirror the checklist heading level (clamped to 1..6) so the feedback file
            // reads like the source document.
            int level = entry == null ? 1 : entry.Level;
            if (level < 1) level = 1;
            if (level > 6) level = 6;

            string title = OneLine(entry == null ? "" : entry.Title);
            string id = entry == null ? "" : (entry.Id ?? "");

            sb.Append(new string('#', level)).Append(' ');
            sb.Append(number.ToString(CultureInfo.InvariantCulture)).Append(". ");
            sb.Append(title).Append('\n');
            sb.Append('\n');

            sb.Append(IdMarkerPrefix).Append(id).Append(IdMarkerSuffix).Append('\n');
            sb.Append("- Title: ").Append(title).Append('\n');
            sb.Append("- Path: ").Append(OneLine(entry == null ? "" : entry.Path)).Append('\n');
            sb.Append("- Status: ")
              .Append(TestStatusUtil.Token(entry == null ? TestStatus.Untested : entry.Status))
              .Append('\n');
            sb.Append("- Updated: ").Append(FormatUpdated(entry)).Append('\n');
            sb.Append("- Feedback: ")
              .Append(FormatFeedback(entry == null ? "" : entry.Feedback))
              .Append('\n');
        }

        private static string DocumentTitle(TestDocument doc)
        {
            if (!string.IsNullOrEmpty(doc.Title)) return doc.Title;
            if (!string.IsNullOrEmpty(doc.FileBaseName)) return doc.FileBaseName;
            if (!string.IsNullOrEmpty(doc.FileName)) return Path.GetFileNameWithoutExtension(doc.FileName);
            return "Untitled";
        }

        private static string FormatUpdated(TestEntry entry)
        {
            if (entry == null || !entry.UpdatedUtc.HasValue) return EmptyPlaceholder;
            return entry.UpdatedUtc.Value.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Single-line rendering of the feedback: CR/LF become the two-character escape
        /// "\n", nothing else is escaped. A CRLF pair counts as one line break. Empty or
        /// whitespace-only feedback becomes the "-" placeholder.
        /// </summary>
        private static string FormatFeedback(string feedback)
        {
            if (string.IsNullOrWhiteSpace(feedback)) return EmptyPlaceholder;
            string normalised = feedback.Replace("\r\n", "\n").Replace('\r', '\n');
            return normalised.Replace("\n", "\\n");
        }

        /// <summary>Keeps a field on one line even if a caller feeds us a multi-line title.</summary>
        private static string OneLine(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0) return value;
            return value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }

        private static string TimestampNow()
        {
            return DateTime.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>Documents belonging to modRootDir, sorted by ModName then FileName.</summary>
        private static List<TestDocument> SelectDocuments(string modRootDir, TestListData data)
        {
            List<TestDocument> docs = new List<TestDocument>();
            if (data == null || data.Documents == null) return docs;

            for (int i = 0; i < data.Documents.Count; i++)
            {
                TestDocument doc = data.Documents[i];
                if (doc == null) continue;
                // An empty modRootDir means "every document" (defensive; the caller always
                // passes the root of the mod it just saved).
                if (!string.IsNullOrEmpty(modRootDir) &&
                    !string.Equals(doc.ModRootDir, modRootDir, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                docs.Add(doc);
            }
            docs.Sort(CompareDocuments);
            return docs;
        }

        private static int CompareDocuments(TestDocument a, TestDocument b)
        {
            int c = string.Compare(a.ModName ?? "", b.ModName ?? "", StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            c = string.Compare(a.FileName ?? "", b.FileName ?? "", StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            // Deterministic tie-break: two same-named files in one mod.
            return string.Compare(a.SourcePath ?? "", b.SourcePath ?? "", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // Writing
        // ------------------------------------------------------------------

        /// <summary>Writes WriteDocument(doc) to GetOutputPath(doc). Creates the directory. Returns the path.</summary>
        public static string SaveDocument(TestDocument doc)
        {
            string path = GetOutputPath(doc);
            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException(
                    "Cannot resolve the feedback output path for document '" +
                    (doc == null ? "(null)" : (doc.FileName ?? "")) + "'.");
            }

            WriteAllTextAtomic(path, WriteDocument(doc));
            return path;
        }

        /// <summary>Writes WriteCombined(...) to &lt;modRootDir&gt;\TestList\TestList.feedback.md. Returns the path.</summary>
        public static string SaveCombined(string modRootDir, TestListData data)
        {
            if (string.IsNullOrEmpty(modRootDir))
            {
                TestListLog.Warning("SaveCombined called without a mod root directory; nothing was written.");
                return "";
            }

            string path = Path.Combine(Path.Combine(modRootDir, TestListFolderName), CombinedFileName);
            WriteAllTextAtomic(path, WriteCombined(modRootDir, data));
            return path;
        }

        /// <summary>
        /// UTF-8 without BOM, LF only, directory created on demand. The text goes to a
        /// sibling "&lt;target&gt;.tmp" first and is then swapped in, so a crash mid-write can
        /// never corrupt a previously good feedback file. Rethrows after cleaning up the
        /// temp file so TestListManager can record the failure.
        /// </summary>
        private static void WriteAllTextAtomic(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            try
            {
                // Raw bytes: guaranteed no BOM, and embedded '\n' are never translated.
                File.WriteAllBytes(tmp, Utf8NoBom.GetBytes(text ?? ""));

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, null);
                        return;
                    }
                    catch (Exception replaceEx)
                    {
                        // File.Replace is unavailable on some volumes / for some share modes.
                        // Overwriting in place still leaves a complete file either way.
                        TestListLog.Warning(
                            "File.Replace failed for " + path + " (" + replaceEx.GetType().Name +
                            "); falling back to an overwriting copy.");
                    }

                    File.Copy(tmp, path, true);
                    TryDelete(tmp);
                    return;
                }

                File.Move(tmp, path);
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Best effort only: never mask the real failure.
            }
        }

        // ------------------------------------------------------------------
        // Read-back
        // ------------------------------------------------------------------

        /// <summary>Parses a feedback file previously produced by this class. Never throws.</summary>
        public static FeedbackData ReadFeedback(TestDocument doc)
        {
            FeedbackData result = new FeedbackData();
            try
            {
                string path = GetOutputPath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;

                string text = DecodeFileBytes(File.ReadAllBytes(path));
                if (string.IsNullOrEmpty(text)) return result;

                string[] lines = text.Split(LineSeparators, StringSplitOptions.None);
                string currentId = null;

                for (int i = 0; i < lines.Length; i++)
                {
                    string id;
                    if (TryParseIdMarker(lines[i], out id))
                    {
                        currentId = id;
                        continue;
                    }
                    // The id marker is the sole key: everything before the first marker,
                    // and every non-field line, is ignored (headings, prose, lists).
                    if (currentId == null) continue;

                    string key;
                    string value;
                    if (!TryParseFieldLine(lines[i], out key, out value)) continue;

                    switch (key)
                    {
                        case "status":
                            result.Statuses[currentId] = ParseStatus(value);
                            break;

                        case "feedback":
                            result.Feedbacks[currentId] = DecodeFeedback(value);
                            break;

                        case "updated":
                            DateTime when;
                            if (DateTime.TryParse(
                                    value,
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                    out when))
                            {
                                result.UpdatedUtc[currentId] = when;
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                TestListLog.Exception("Failed to read a feedback document", ex);
                return new FeedbackData();
            }
            return result;
        }

        /// <summary>Decodes file bytes as UTF-8, stripping a byte order mark when present.</summary>
        private static string DecodeFileBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            int offset = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) offset = 3;
            return Utf8Lenient.GetString(bytes, offset, bytes.Length - offset);
        }

        /// <summary>True for a line of the form "&lt;!-- testlist:id=XXXX --&gt;".</summary>
        private static bool TryParseIdMarker(string line, out string id)
        {
            id = null;
            if (line == null) return false;

            string t = line.Trim();
            if (t.Length < 7) return false;
            if (!t.StartsWith("<!--", StringComparison.Ordinal)) return false;
            if (!t.EndsWith("-->", StringComparison.Ordinal)) return false;

            // Inner text between "<!--" and "-->".
            string inner = t.Substring(4, t.Length - 7);
            int at = inner.IndexOf("testlist:id=", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;

            string value = inner.Substring(at + "testlist:id=".Length).Trim();
            if (value.Length == 0) return false;

            id = value;
            return true;
        }

        /// <summary>
        /// True for a "- Key: value" line (leading whitespace and -, * or + bullets are
        /// tolerated). key is lower-cased; value keeps its trailing whitespace.
        /// </summary>
        private static bool TryParseFieldLine(string line, out string key, out string value)
        {
            key = null;
            value = null;
            if (line == null) return false;

            string t = line.Trim();
            if (t.Length == 0) return false;

            int start = 0;
            if (t[0] == '-' || t[0] == '*' || t[0] == '+') start = 1;
            while (start < t.Length && (t[start] == ' ' || t[start] == '\t')) start++;

            int colon = t.IndexOf(':', start);
            if (colon <= start) return false;

            key = t.Substring(start, colon - start).Trim().ToLowerInvariant();
            if (key.Length == 0) return false;

            value = t.Substring(colon + 1).TrimStart(' ', '\t');
            return true;
        }

        /// <summary>
        /// Status token -> TestStatus. Goes through the frozen TestStatusUtil.TryParseToken
        /// first; the Chinese literals it does not know are handled by a local map.
        /// </summary>
        private static TestStatus ParseStatus(string value)
        {
            if (string.IsNullOrEmpty(value)) return TestStatus.Untested;

            string token = value.Trim();
            TestStatus status;
            if (TestStatusUtil.TryParseToken(token, out status)) return status;

            switch (token)
            {
                case "正常": return TestStatus.Pass;
                case "异常": return TestStatus.Fail;
                case "未测试": return TestStatus.Untested;
                default: return TestStatus.Untested;
            }
        }

        /// <summary>Reverses the "\n" escape; a literal "-" means "no feedback".</summary>
        private static string DecodeFeedback(string value)
        {
            string v = value ?? "";
            string trimmed = v.Trim();
            if (trimmed.Length == 0) return "";
            if (trimmed == EmptyPlaceholder) return "";
            return v.Replace("\\n", "\n");
        }
    }
}
