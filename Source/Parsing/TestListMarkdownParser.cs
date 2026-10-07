// TestList -- markdown checklist parser.
//
// One .md/.markdown file becomes one TestDocument; every ATX or setext heading becomes
// one TestEntry. Entry ids are content-derived SHA1 hashes, so an in-game mark stays
// attached to the same heading across restarts and across edits elsewhere in the file.
//
// This file is compiled BOTH in-game and headless (TESTLIST_NO_VERSE). It must not
// reference Verse/RimWorld/UnityEngine types, not even through a using directive.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TestList
{
    public static class TestListMarkdownParser
    {
        /// <summary>ATX heading: 1-6 hashes at column 0, at least one space/tab, then the text.</summary>
        private static readonly Regex AtxHeading = new Regex(@"^(#{1,6})[ \t]+(.*)$", RegexOptions.Compiled);

        /// <summary>Setext underline: a line of only '=' (level 1) or '-' (level 2).</summary>
        private static readonly Regex SetextUnderline = new Regex(@"^ {0,3}(=+|-+)[ \t]*$", RegexOptions.Compiled);

        /// <summary>Start of a fenced code block: ``` or ~~~, indented at most three spaces.</summary>
        private static readonly Regex FenceStart = new Regex(@"^ {0,3}(`{3,}|~{3,})", RegexOptions.Compiled);

        /// <summary>Inline link or image: [label](target) / ![alt](target) -> label.</summary>
        private static readonly Regex LinkOrImage = new Regex(@"!?\[([^\[\]]*)\]\([^()]*\)", RegexOptions.Compiled);

        /// <summary>
        /// Parses a markdown checklist. Never throws and never returns null: a hostile or
        /// truncated file still yields whatever entries could be read, possibly none.
        /// </summary>
        /// <param name="markdown">Raw file text; UTF-8 BOM, CRLF and LF are all tolerated.</param>
        /// <param name="fileNameForRootEntry">File name without extension; it feeds the entry ids.</param>
        public static List<TestEntry> ParseMarkdownList(string markdown, string fileNameForRootEntry)
        {
            List<TestEntry> entries = new List<TestEntry>();
            try
            {
                if (markdown == null) return entries;

                string text = markdown;
                if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
                text = text.Replace("\r\n", "\n").Replace('\r', '\n');
                // Explicit char[] + StringSplitOptions: the one-argument Split(char) form
                // resolves to a Split(char, StringSplitOptions) overload that does not exist
                // on .NET Framework 4.8 / Unity's Mono profile, which fails at runtime.
                string[] lines = text.Split(new char[] { '\n' }, StringSplitOptions.None);

                List<Heading> headings = CollectHeadings(lines);
                BuildEntries(headings, lines, fileNameForRootEntry ?? "", entries);
            }
            catch (Exception ex)
            {
                TestListLog.Warning("Failed to parse markdown checklist '" +
                    (fileNameForRootEntry ?? "?") + "': " + ex.Message);
            }
            return entries;
        }

        // ------------------------------------------------------------------
        // Heading discovery
        // ------------------------------------------------------------------

        /// <summary>One heading found in the source, before entries are built.</summary>
        private sealed class Heading
        {
            /// <summary>0-based line of the heading's first (text) line.</summary>
            public int Line;

            /// <summary>1-6.</summary>
            public int Level;

            /// <summary>The heading line exactly as written, trimmed.</summary>
            public string RawHeading;

            /// <summary>Heading text with markdown emphasis/links stripped.</summary>
            public string Title;

            /// <summary>First line that belongs to this heading's body.</summary>
            public int ContentStart;
        }

        /// <summary>
        /// Walks the file once and records every real heading. Fenced code blocks, HTML
        /// comments and '#'-without-a-space lines are not headings.
        /// </summary>
        private static List<Heading> CollectHeadings(string[] lines)
        {
            List<Heading> headings = new List<Heading>();
            bool[] consumed = new bool[lines.Length];
            bool inFence = false;
            char fenceChar = '`';
            int fenceLength = 3;
            bool inComment = false;

            for (int i = 0; i < lines.Length; i++)
            {
                if (consumed[i]) continue;
                string line = lines[i];

                if (inFence)
                {
                    if (IsFenceClose(line, fenceChar, fenceLength)) inFence = false;
                    continue;
                }

                if (inComment)
                {
                    if (line.IndexOf("-->", StringComparison.Ordinal) >= 0) inComment = false;
                    continue;
                }

                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                // A whole-line HTML comment hides everything until its closing marker.
                if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
                {
                    if (trimmed.IndexOf("-->", StringComparison.Ordinal) < 0) inComment = true;
                    continue;
                }

                Match fence = FenceStart.Match(line);
                if (fence.Success)
                {
                    string run = fence.Groups[1].Value;
                    inFence = true;
                    fenceChar = run[0];
                    fenceLength = run.Length;
                    continue;
                }

                Match atx = AtxHeading.Match(line);
                if (atx.Success)
                {
                    headings.Add(new Heading
                    {
                        Line = i,
                        Level = atx.Groups[1].Value.Length,
                        RawHeading = trimmed,
                        Title = StripMarkdown(atx.Groups[2].Value),
                        ContentStart = i + 1
                    });
                    continue;
                }

                // Setext: this non-blank text line plus an underline on the very next line.
                if (i + 1 < lines.Length && !consumed[i + 1])
                {
                    Match underline = SetextUnderline.Match(lines[i + 1]);
                    if (underline.Success)
                    {
                        headings.Add(new Heading
                        {
                            Line = i,
                            Level = underline.Groups[1].Value[0] == '=' ? 1 : 2,
                            RawHeading = trimmed,
                            Title = StripMarkdown(trimmed),
                            ContentStart = i + 2
                        });
                        consumed[i + 1] = true;
                    }
                }
            }

            return headings;
        }

        /// <summary>True when the line closes a fence opened with <paramref name="fenceChar"/>.</summary>
        private static bool IsFenceClose(string line, char fenceChar, int minLength)
        {
            string trimmed = line.Trim();
            if (trimmed.Length < minLength) return false;

            int n = 0;
            while (n < trimmed.Length && trimmed[n] == fenceChar) n++;
            if (n < minLength) return false;

            for (int i = n; i < trimmed.Length; i++)
            {
                if (trimmed[i] != ' ' && trimmed[i] != '\t') return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Entry construction
        // ------------------------------------------------------------------

        /// <summary>
        /// Turns the heading list into entries. Content runs from the line after the heading
        /// to the line before the next heading whose Level is &lt;= this Level, so nested
        /// headings stay verbatim inside their parent's body.
        /// </summary>
        private static void BuildEntries(List<Heading> headings, string[] lines, string fileBase, List<TestEntry> entries)
        {
            List<TestEntry> stack = new List<TestEntry>();

            for (int h = 0; h < headings.Count; h++)
            {
                Heading heading = headings[h];

                int endLine = lines.Length - 1;
                for (int j = h + 1; j < headings.Count; j++)
                {
                    if (headings[j].Level <= heading.Level)
                    {
                        endLine = headings[j].Line - 1;
                        break;
                    }
                }

                while (stack.Count > 0 && stack[stack.Count - 1].Level >= heading.Level)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                TestEntry parent = stack.Count > 0 ? stack[stack.Count - 1] : null;

                TestEntry entry = new TestEntry();
                entry.RawHeading = heading.RawHeading;
                entry.Title = heading.Title;
                entry.Level = heading.Level;
                entry.Content = JoinContent(lines, heading.ContentStart, endLine);
                entry.SourceLine = heading.Line;
                entry.ParentId = parent != null ? parent.Id : null;
                entry.Path = parent != null ? parent.Path + " / " + entry.Title : entry.Title;
                entry.Id = MakeId(fileBase, heading.Line, heading.RawHeading);

                entries.Add(entry);
                stack.Add(entry);
            }
        }

        /// <summary>
        /// Joins lines [start..end] with LF, dropping blank lines at both ends only.
        /// Everything in between - nested headings, fences, bullets - is kept verbatim.
        /// </summary>
        private static string JoinContent(string[] lines, int start, int end)
        {
            if (end < start) return "";

            int first = start;
            int last = end;
            while (first <= last && IsBlank(lines[first])) first++;
            while (last >= first && IsBlank(lines[last])) last--;
            if (last < first) return "";

            StringBuilder sb = new StringBuilder();
            for (int i = first; i <= last; i++)
            {
                if (i > first) sb.Append('\n');
                sb.Append(lines[i]);
            }
            return sb.ToString();
        }

        private static bool IsBlank(string line)
        {
            return line == null || line.Trim().Length == 0;
        }

        /// <summary>Removes markdown emphasis, code ticks and link targets from heading text.</summary>
        private static string StripMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string result = text;
            for (int pass = 0; pass < 8; pass++)
            {
                string next = LinkOrImage.Replace(result, "$1");
                if (string.Equals(next, result, StringComparison.Ordinal)) break;
                result = next;
            }
            result = result.Replace("**", "").Replace("*", "").Replace("`", "");
            return result.Trim();
        }

        // ------------------------------------------------------------------
        // Ids
        // ------------------------------------------------------------------

        /// <summary>"md:" + SHA1(FileBaseName \u0001 SourceLine \u0001 RawHeading), lowercase hex.</summary>
        private static string MakeId(string fileBase, int sourceLine, string rawHeading)
        {
            string payload = (fileBase ?? "") + "\u0001" +
                sourceLine.ToString(CultureInfo.InvariantCulture) + "\u0001" + (rawHeading ?? "");
            return "md:" + Sha1Hex(payload);
        }

        private static string Sha1Hex(string value)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
