// TestList -- headless verification suites.
//
// These run against the REAL parsing/feedback sources with TESTLIST_NO_VERSE defined.
// Every assertion here is an acceptance criterion for the shipped mod.
//
// Fixtures are numbered by 0-based source line in the comments: SourceLine assertions
// are checked against those, so if you edit a fixture you MUST re-derive them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TestList.Verify
{
    // =====================================================================
    // Markdown parsing
    // =====================================================================
    internal static class MarkdownParserSuite
    {
        internal static void Run()
        {
            Test.Suite("Markdown parsing");

            // Fixture line numbers (0-based) are noted on the right.
            string md = string.Join("\n", new[] {
                "# 防空系统验证",                              // 0
                "",                                            // 1
                "总体说明。",                                  // 2
                "",                                            // 3
                "## 1. SAM 发射器",                            // 4
                "",                                            // 5
                "准备步骤:",                                   // 6
                "- 造一个 SAM 发射器",                         // 7
                "- 放一个飞行敌人",                            // 8
                "",                                            // 9
                "```",                                         // 10
                "# 这一行在代码块里，不能变成标题",            // 11
                "### 也不能",                                  // 12
                "```",                                         // 13
                "",                                            // 14
                "### 1.1 锁定目标",                            // 15
                "",                                            // 16
                "应当锁定。",                                  // 17
                "",                                            // 18
                "Setext 一级标题",                             // 19
                "================",                            // 20
                "",                                            // 21
                "#nospace 不是标题",                           // 22
                "",                                            // 23
                "#### 深层小结",                               // 24
                "",                                            // 25
                "**加粗**与`代码`与[链接](https://example.com)要去掉标记", // 26
                "",                                            // 27
                "## 尾部兄弟项",                               // 28
                "",                                            // 29
                "尾部正文",                                    // 30
            });

            List<TestEntry> entries = TestListMarkdownParser.ParseMarkdownList(md, "防空系统");
            Test.Check(entries != null, "parse", "ParseMarkdownList never returns null");
            if (entries == null) return;

            // Headings: lines 0, 4, 15, 19(setext), 24, 28 => 6 entries.
            Test.Equal(6, entries.Count, "entry count");

            Test.Equal("防空系统验证", entries[0].Title, "entries[0].Title");
            Test.Equal(1, entries[0].Level, "entries[0].Level");
            Test.Equal("防空系统验证", entries[0].Path, "entries[0].Path (level 1 == Title)");
            Test.Equal(0, entries[0].SourceLine, "entries[0].SourceLine");
            Test.Check(entries[0].ParentId == null, "entries[0].ParentId is null for a root heading");

            Test.Equal("1. SAM 发射器", entries[1].Title, "entries[1].Title");
            Test.Equal(2, entries[1].Level, "entries[1].Level");
            Test.Equal(4, entries[1].SourceLine, "entries[1].SourceLine");
            Test.Equal("防空系统验证 / 1. SAM 发射器", entries[1].Path, "entries[1].Path");
            Test.Equal(entries[0].Id, entries[1].ParentId, "entries[1].ParentId");
            Test.Contains(entries[1].Content, "- 造一个 SAM 发射器", "entries[1].Content keeps bullets");
            // The fenced block sits between heading 1 and heading 2, so it belongs to entries[1].
            Test.Contains(entries[1].Content, "```", "entries[1].Content keeps the fenced block verbatim");
            Test.Contains(entries[1].Content, "# 这一行在代码块里", "fenced '#' line is body text, not a heading");

            Test.Equal("1.1 锁定目标", entries[2].Title, "entries[2].Title");
            Test.Equal(3, entries[2].Level, "entries[2].Level");
            Test.Equal(15, entries[2].SourceLine, "entries[2].SourceLine");
            Test.Equal("防空系统验证 / 1. SAM 发射器 / 1.1 锁定目标", entries[2].Path, "entries[2].Path");
            Test.Equal(entries[1].Id, entries[2].ParentId, "entries[2].ParentId");
            Test.Contains(entries[2].Content, "应当锁定。", "entries[2].Content");
            Test.NotContains(entries[2].Content, "代码块里", "entries[2].Content does not reach back into the fence");

            Test.Equal("Setext 一级标题", entries[3].Title, "setext heading text");
            Test.Equal(1, entries[3].Level, "setext '===' is level 1");
            Test.Equal(19, entries[3].SourceLine, "setext SourceLine (the text line, not the underline)");

            Test.Equal("深层小结", entries[4].Title, "entries[4].Title");
            Test.Equal(4, entries[4].Level, "entries[4].Level");
            Test.Equal("Setext 一级标题 / 深层小结", entries[4].Path, "entries[4].Path after a level jump");

            Test.Equal("尾部兄弟项", entries[5].Title, "entries[5].Title");
            Test.Equal(2, entries[5].Level, "entries[5].Level");
            Test.Equal(28, entries[5].SourceLine, "entries[5].SourceLine");
            // A level-2 heading after a level-1 heading is its child.
            Test.Equal(entries[3].Id, entries[5].ParentId, "entries[5].ParentId is the nearest shallower heading");

            // Markdown emphasis must be stripped from a title.
            string emphasis = string.Join("\n", new[] {
                "# 标题 **加粗** 与 `代码` 与 [链接](https://example.com) 与 *斜体*",
                "",
                "正文",
            });
            List<TestEntry> emphasisEntries = TestListMarkdownParser.ParseMarkdownList(emphasis, "emphasis");
            Test.Equal(1, emphasisEntries.Count, "emphasis fixture entry count");
            if (emphasisEntries.Count == 1)
            {
                Test.Equal("标题 加粗 与 代码 与 链接 与 斜体", emphasisEntries[0].Title,
                    "emphasis markers stripped from the title");
            }

            // ---- '#' without a space is not a heading ----
            Test.Check(!HasTitle(entries, "#nospace"), "'#nospace' did not create an entry ('# ' requires a space)");
            Test.Contains(entries[3].Content, "#nospace 不是标题", "'#nospace' stays in the body text");

            // ---- BOM + CRLF ----
            // "\uFEFF# A\r\n" = line 0, "\r\n" = 1, "body1\r\n" = 2, "\r\n" = 3, "## B\r\n" = 4
            string crlf = "\uFEFF# A\r\n\r\nbody1\r\n\r\n## B\r\n\r\nbody2\r\n";
            List<TestEntry> crlfEntries = TestListMarkdownParser.ParseMarkdownList(crlf, "crlf");
            Test.Equal(2, crlfEntries.Count, "CRLF+BOM entry count");
            Test.Equal("A", crlfEntries[0].Title, "CRLF+BOM first title (BOM stripped)");
            Test.Equal("B", crlfEntries[1].Title, "CRLF+BOM second title");
            Test.Equal(0, crlfEntries[0].SourceLine, "CRLF+BOM first SourceLine");
            Test.Equal(4, crlfEntries[1].SourceLine, "CRLF+BOM second SourceLine");
            Test.Equal("A / B", crlfEntries[1].Path, "CRLF+BOM second Path");
            Test.Contains(crlfEntries[0].Content, "body1", "CRLF body captured");
            Test.NotContains(crlfEntries[0].Title, "\uFEFF", "BOM is not part of the title");

            // ---- ids are deterministic and distinct ----
            List<TestEntry> again = TestListMarkdownParser.ParseMarkdownList(md, "防空系统");
            Test.Equal(entries.Count, again.Count, "re-parse entry count");
            bool idsStable = true;
            bool idsUnique = true;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Id != again[i].Id) idsStable = false;
                if (!seen.Add(entries[i].Id)) idsUnique = false;
            }
            Test.Check(idsStable, "ids: stable across two parses of identical input");
            Test.Check(idsUnique, "ids: unique within a document");
            Test.Check(entries[0].Id != null && entries[0].Id.StartsWith("md:", StringComparison.Ordinal),
                "ids: carry the 'md:' prefix (got " + Test.Describe(entries[0].Id) + ")");

            // A different file name must not collide with the same content.
            List<TestEntry> otherFile = TestListMarkdownParser.ParseMarkdownList(md, "另一个文件");
            Test.Check(otherFile.Count > 0 && otherFile[0].Id != entries[0].Id,
                "ids: include the file name (no cross-file collision)");

            // ---- empty / hostile input never throws ----
            List<TestEntry> empty = TestListMarkdownParser.ParseMarkdownList("", "empty");
            Test.Check(empty != null && empty.Count == 0, "empty input yields an empty list");
            List<TestEntry> nullInput = TestListMarkdownParser.ParseMarkdownList(null, "null");
            Test.Check(nullInput != null && nullInput.Count == 0, "null input yields an empty list, never null");
            List<TestEntry> plain = TestListMarkdownParser.ParseMarkdownList("普通文本没有标题", "plain");
            Test.Check(plain != null && plain.Count == 0, "heading-free input yields an empty list");
            try
            {
                TestListMarkdownParser.ParseMarkdownList("###\n\n####   \n\n#\n", "weird");
                Test.Ok("degenerate/empty headings do not throw");
            }
            catch (Exception ex)
            {
                Test.Fail("degenerate headings", "threw " + ex.GetType().Name + ": " + ex.Message);
            }
            try
            {
                TestListMarkdownParser.ParseMarkdownList("```\n# 未闭合的代码块\n", "unclosed");
                Test.Ok("an unclosed fence does not throw");
            }
            catch (Exception ex)
            {
                Test.Fail("unclosed fence", "threw " + ex.GetType().Name + ": " + ex.Message);
            }

            // ---- content boundary: a sibling heading ends the previous content ----
            string siblings = "# P\n\nalpha\n\n## C1\n\none\n\n## C2\n\ntwo\n";
            List<TestEntry> sib = TestListMarkdownParser.ParseMarkdownList(siblings, "sib");
            Test.Equal(3, sib.Count, "sibling entry count");
            Test.Contains(sib[1].Content, "one", "C1 keeps its own body");
            Test.NotContains(sib[1].Content, "two", "C1 body does not absorb its sibling");
            Test.Equal("P / C1", sib[1].Path, "C1 path");
            Test.Equal("P / C2", sib[2].Path, "C2 path");
        }

        private static bool HasTitle(List<TestEntry> entries, string fragment)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Title != null && entries[i].Title.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }
    }

    // =====================================================================
    // XML parsing
    // =====================================================================
    internal static class XmlParserSuite
    {
        internal static void Run()
        {
            Test.Suite("XML parsing");

            // ---- shape 1: dedicated test list ----
            // Produces 4 entries: the two root children, plus the nested <Test> as its own
            // entry, and the third root child which carries the nested one.
            string testList = string.Join("\n", new[] {
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                "<TestList>",
                "  <Test>",
                "    <Title>SAM 发射器可以拦截</Title>",
                "    <Steps>造一个 SAM 发射器</Steps>",
                "    <Expect>导弹升空并命中</Expect>",
                "  </Test>",
                "  <Test Label=\"天基拦截器冷却\">",
                "    <Desc>连续发射两次</Desc>",
                "  </Test>",
                "  <Test>",
                "    <Title>目标分类</Title>",
                "    <Test>",
                "      <Title>飞行单位被识别</Title>",
                "    </Test>",
                "  </Test>",
                "</TestList>",
            });

            List<TestEntry> entries = TestListXmlParser.ParseXmlList(testList, "防空系统");
            Test.Check(entries != null, "dedicated shape: returns a list for a valid <TestList>");
            if (entries != null)
            {
                Test.Equal(4, entries.Count, "dedicated entry count (nested <Test> becomes its own entry)");
                if (entries.Count == 4)
                {
                    Test.Equal("SAM 发射器可以拦截", entries[0].Title, "entries[0].Title from <Title>");
                    Test.Contains(entries[0].Content, "造一个 SAM 发射器", "entries[0].Content includes <Steps>");
                    Test.Contains(entries[0].Content, "导弹升空并命中", "entries[0].Content includes <Expect>");
                    Test.Equal("天基拦截器冷却", entries[1].Title, "Label attribute used as the title");
                    Test.Contains(entries[1].Content, "连续发射两次", "entries[1].Content includes <Desc>");
                    Test.Equal("目标分类", entries[2].Title, "parent entry title");
                    Test.Equal("飞行单位被识别", entries[3].Title, "nested <Test> title");
                    Test.Equal(entries[2].Id, entries[3].ParentId, "nested entry points at its parent id");
                    Test.Check(entries[0].ParentId == null, "root-level entry has a null ParentId");
                }

                bool xmlPrefixed = true;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                bool unique = true;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Id == null || !entries[i].Id.StartsWith("xml:", StringComparison.Ordinal)) xmlPrefixed = false;
                    if (!ids.Add(entries[i].Id)) unique = false;
                }
                Test.Check(xmlPrefixed, "dedicated shape: ids carry the 'xml:' prefix");
                Test.Check(unique, "dedicated shape: ids are unique");

                List<TestEntry> again = TestListXmlParser.ParseXmlList(testList, "防空系统");
                bool stable = again != null && again.Count == entries.Count;
                if (stable)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (entries[i].Id != again[i].Id) stable = false;
                    }
                }
                Test.Check(stable, "dedicated shape: ids are deterministic across parses");
            }

            // ---- shape 2: a RimWorld Defs-style file ----
            string defs = string.Join("\n", new[] {
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                "<Defs>",
                "  <ThingDef>",
                "    <defName>RukayaSAMLauncher</defName>",
                "    <label>卢卡娅防空发射器</label>",
                "    <description>防空</description>",
                "  </ThingDef>",
                "  <ThingDef>",
                "    <defName>RukayaSkyInterceptor</defName>",
                "    <label>天基拦截器</label>",
                "  </ThingDef>",
                "</Defs>",
            });

            List<TestEntry> defEntries = TestListXmlParser.ParseXmlList(defs, "Defs");
            Test.Check(defEntries != null, "defs shape: returns a list for a <Defs> document");
            if (defEntries != null)
            {
                Test.Equal(2, defEntries.Count, "defs entry count");
                if (defEntries.Count == 2)
                {
                    Test.Equal("卢卡娅防空发射器", defEntries[0].Title, "defs title taken from <label>");
                    Test.Contains(defEntries[0].Content, "RukayaSAMLauncher", "defs content keeps the element XML");
                    Test.Equal("天基拦截器", defEntries[1].Title, "second defs title");
                }
            }

            // ---- malformed XML returns null so the caller can fall back to markdown ----
            Test.Check(TestListXmlParser.ParseXmlList("<TestList><Test>", "broken") == null,
                "malformed: unclosed XML returns null (markdown fallback signal)");
            Test.Check(TestListXmlParser.ParseXmlList("", "empty") == null,
                "malformed: empty input returns null");
            Test.Check(TestListXmlParser.ParseXmlList("这不是 XML", "text") == null,
                "malformed: plain text returns null");
            Test.Check(TestListXmlParser.ParseXmlList(null, "null") == null,
                "malformed: null input returns null without throwing");

            // ---- well-formed but empty ----
            List<TestEntry> emptyList = TestListXmlParser.ParseXmlList("<TestList></TestList>", "empty");
            Test.Check(emptyList != null && emptyList.Count == 0,
                "well-formed empty: <TestList></TestList> yields an empty list, not null");
        }
    }

    // =====================================================================
    // Feedback round trip
    // =====================================================================
    internal static class FeedbackRoundTripSuite
    {
        internal const string Marker = "<!-- TestList feedback v1 -->";

        internal static void Run()
        {
            Test.Suite("Feedback round trip");

            string mdPath = Test.WriteDataFile(@"RoundTrip\防空系统.md", string.Join("\n", new[] {
                "# 防空系统验证",
                "",
                "## 1. SAM 发射器可以拦截",
                "",
                "造好发射器后放一个飞行敌人。",
                "",
                "## 2. 天基拦截器冷却",
                "",
                "- 连续发射两次",
                "",
            }));

            TestDocument doc = MakeDoc(mdPath, "防空系统");
            // The fixture below has 3 headings: "# 防空系统验证", "## 1. ..." and "## 2. ...".
            Test.Equal(3, doc.Entries.Count, "source entries parsed for the round trip");
            if (doc.Entries.Count != 3) return;

            // Mutate exactly as the in-game UI would.
            doc.Entries[0].Status = TestStatus.Pass;
            doc.Entries[0].Feedback = "正常命中，无报错";
            doc.Entries[0].UpdatedUtc = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc);
            doc.Entries[2].Status = TestStatus.Fail;
            // Multi-line feedback must survive the round trip.
            doc.Entries[2].Feedback = "第一次正常\r\n第二次没有冷却提示";
            doc.Entries[2].UpdatedUtc = new DateTime(2026, 5, 4, 12, 5, 0, DateTimeKind.Utc);

            string rendered = FeedbackDocuments.WriteDocument(doc);
            Test.Check(rendered != null && rendered.Length > 0, "render: WriteDocument produced content");
            if (!string.IsNullOrEmpty(rendered))
            {
                Test.Check(rendered.StartsWith(Marker, StringComparison.Ordinal),
                    "marker: file starts byte-exactly with the feedback marker");
                Test.Contains(rendered, "<!-- testlist:id=", "render: an id marker is emitted");
                Test.Contains(rendered, doc.Entries[0].Id, "render: entry 0's id appears in the output");
                Test.Contains(rendered, "Status: pass", "render: pass status token rendered");
                Test.Contains(rendered, "Status: fail", "render: fail status token rendered");
                Test.Contains(rendered, "防空系统验证", "render: document title rendered");
                Test.NotContains(rendered, "\r", "render: no CR bytes in the rendered string");
            }

            string saved = FeedbackDocuments.SaveDocument(doc);
            Test.Equal(FeedbackDocuments.GetOutputPath(doc), saved, "SaveDocument returns the output path");
            Test.Check(File.Exists(saved), "save: feedback file exists at " + saved);
            Test.Equal("防空系统.feedback.md", Path.GetFileName(saved), "output file name");

            // ---- byte-level format guarantees ----
            byte[] bytes = File.ReadAllBytes(saved);
            Test.Check(bytes.Length >= 3 && !(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF),
                "encoding: file has no UTF-8 BOM");
            string raw = Encoding.UTF8.GetString(bytes);
            Test.NotContains(raw, "\r\n", "encoding: file uses LF line endings only");
            Test.Check(raw.StartsWith(Marker + "\n", StringComparison.Ordinal),
                "encoding: first line is exactly the marker followed by LF");
            // Nothing may be left behind by the atomic write.
            Test.Check(!File.Exists(saved + ".tmp"), "atomic write: no leftover .tmp file");

            // ---- read back ----
            FeedbackData fb = FeedbackDocuments.ReadFeedback(doc);
            Test.Check(fb != null, "readback: ReadFeedback never returns null");
            if (fb == null) return;

            TestStatus s0;
            Test.Check(fb.Statuses.TryGetValue(doc.Entries[0].Id, out s0) && s0 == TestStatus.Pass,
                "readback: entry 0 status restored as pass");
            TestStatus s1;
            Test.Check(fb.Statuses.TryGetValue(doc.Entries[2].Id, out s1) && s1 == TestStatus.Fail,
                "readback: entry 2 status restored as fail");
            string f0;
            Test.Check(fb.Feedbacks.TryGetValue(doc.Entries[0].Id, out f0) && f0 == "正常命中，无报错",
                "readback: entry 0 feedback restored exactly");
            string f1;
            Test.Check(fb.Feedbacks.TryGetValue(doc.Entries[2].Id, out f1) &&
                       NormaliseNewlines(f1) == NormaliseNewlines("第一次正常\r\n第二次没有冷却提示"),
                "readback: multi-line feedback restored exactly (got " + Test.Describe(f1) + ")");
            DateTime u0;
            if (fb.UpdatedUtc.TryGetValue(doc.Entries[0].Id, out u0))
            {
                Test.Equal(new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc), u0.ToUniversalTime(),
                    "readback: timestamp value");
            }
            else
            {
                Test.Fail("readback", "entry 0 timestamp was not restored");
            }

            // ---- a document whose feedback file does not exist yet ----
            string freshPath = Test.WriteDataFile(@"RoundTrip\fresh.md", "# T\n\n## A\n\nbody\n");
            TestDocument fresh = MakeDoc(freshPath, "fresh");
            FeedbackData none = FeedbackDocuments.ReadFeedback(fresh);
            Test.Check(none != null && none.IsEmpty, "missing file: absent feedback file yields empty data, no throw");

            // ---- hand-edited / hostile feedback file ----
            string handPath = Test.WriteDataFile(@"RoundTrip\hand.md", "# T\n\n## A\n\nbody\n");
            TestDocument hand = MakeDoc(handPath, "hand");
            var handText = new StringBuilder();
            handText.Append(Marker).Append('\n');
            handText.Append("# T\n\n");
            handText.Append(IdLine(hand.Entries[0].Id)).Append('\n');
            handText.Append("- Title: A\n");
            handText.Append("- Path: A\n");
            handText.Append("- Status: 异常\n");
            handText.Append("- Updated: not-a-timestamp\n");
            handText.Append("- Feedback: -\n");
            handText.Append(IdLine("md:unknown-id")).Append('\n');
            handText.Append("- Status: bogus-value\n");
            // Written with a BOM to prove the reader tolerates one.
            File.WriteAllText(FeedbackDocuments.GetOutputPath(hand), handText.ToString(), new UTF8Encoding(true));

            FeedbackData handFb = FeedbackDocuments.ReadFeedback(hand);
            Test.Check(handFb != null, "hand-edited: hand-edited file parses without throwing");
            if (handFb != null && hand.Entries.Count > 0)
            {
                TestStatus hs;
                Test.Check(handFb.Statuses.TryGetValue(hand.Entries[0].Id, out hs) && hs == TestStatus.Fail,
                    "hand-edited: Chinese status value 异常 maps to Fail");
                string hf;
                Test.Check(handFb.Feedbacks.TryGetValue(hand.Entries[0].Id, out hf) && hf == "",
                    "hand-edited: literal '-' feedback reads back as empty");
                Test.Check(!handFb.UpdatedUtc.ContainsKey(hand.Entries[0].Id),
                    "hand-edited: unparseable timestamp is dropped, not thrown");
                // Whether an unrelated id is stored or skipped is an implementation
                // detail; what matters is that the known ids stay correct and nothing
                // is mis-assigned.
                TestStatus known;
                Test.Check(handFb.Statuses.TryGetValue(hand.Entries[0].Id, out known) && known == TestStatus.Fail,
                    "hand-edited: the unknown-id block did not disturb the known entry");
            }

            // ---- combined file ----
            var data = new TestListData();
            data.Documents.Add(doc);
            data.Documents.Add(fresh);
            string dir = Path.GetDirectoryName(mdPath);
            string combined = FeedbackDocuments.WriteCombined(dir, data);
            Test.Check(combined != null && combined.StartsWith(Marker, StringComparison.Ordinal),
                "combined: combined file also starts with the marker");
            string combinedPath = FeedbackDocuments.SaveCombined(dir, data);
            Test.Check(File.Exists(combinedPath), "combined: combined file written to " + combinedPath);
            Test.Equal("TestList.feedback.md", Path.GetFileName(combinedPath), "combined file name");

            // ---- status token helpers ----
            TestStatus parsed;
            Test.Check(TestStatusUtil.TryParseToken("PASS", out parsed) && parsed == TestStatus.Pass,
                "tokens: 'PASS' parses (case-insensitive)");
            Test.Check(TestStatusUtil.TryParseToken("broken", out parsed) && parsed == TestStatus.Fail,
                "tokens: 'broken' parses as Fail");
            Test.Check(TestStatusUtil.TryParseToken("untested", out parsed) && parsed == TestStatus.Untested,
                "tokens: 'untested' parses");
            Test.Check(!TestStatusUtil.TryParseToken("nonsense", out parsed),
                "tokens: an unknown token is rejected");
            Test.Check(!TestStatusUtil.TryParseToken(null, out parsed),
                "tokens: null token is rejected without throwing");
            // The model layer deliberately speaks only the stable on-disk English tokens;
            // Chinese values are a hand-editing convenience handled at the document layer,
            // which the hand-edited case above already exercises end to end.
            Test.Check(!TestStatusUtil.TryParseToken("正常", out parsed),
                "tokens: '正常' is NOT claimed by the model layer (document layer handles it)");
            Test.Equal("pass", TestStatusUtil.Token(TestStatus.Pass), "Token(Pass)");
            Test.Equal("fail", TestStatusUtil.Token(TestStatus.Fail), "Token(Fail)");
            Test.Equal("untested", TestStatusUtil.Token(TestStatus.Untested), "Token(Untested)");
        }

        private static string NormaliseNewlines(string s)
        {
            return s == null ? null : s.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string IdLine(string id)
        {
            return "<!-- testlist:id=" + id + " -->";
        }

        internal static TestDocument MakeDoc(string sourcePath, string baseName)
        {
            var doc = new TestDocument();
            doc.SourcePath = sourcePath;
            doc.FileBaseName = baseName;
            doc.FileName = Path.GetFileName(sourcePath);
            doc.ModName = "Verification";
            doc.ModPackageId = "verify.testlist";
            doc.ModRootDir = Path.GetDirectoryName(sourcePath);
            doc.IsXml = false;
            doc.FeedbackPath = FeedbackDocuments.GetOutputPath(doc);
            string text = File.ReadAllText(sourcePath);
            List<TestEntry> entries = TestListMarkdownParser.ParseMarkdownList(text, baseName);
            for (int i = 0; i < entries.Count; i++) doc.Entries.Add(entries[i]);
            doc.Title = baseName;
            return doc;
        }
    }

    // =====================================================================
    // Scanner: discovery, filtering, output re-ingestion guard
    // =====================================================================
    internal static class ScannerSuite
    {
        internal static void Run()
        {
            Test.Suite("Scanner discovery");

            string root = Path.Combine(Test.DataDir, "TempTestMod");
            Directory.CreateDirectory(root);

            string mdPath = Test.WriteDataFile(@"TempTestMod\md.md", string.Join("\n", new[] {
                "# 清单 A",
                "",
                "## 项 1",
                "",
                "内容 1",
                "",
                "## 项 2",
                "",
                "内容 2",
                "",
            }));
            string defsPath = Test.WriteDataFile(@"TempTestMod\defs.xml", string.Join("\n", new[] {
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                "<Defs>",
                "  <ThingDef>",
                "    <defName>X1</defName>",
                "    <label>X 一号</label>",
                "  </ThingDef>",
                "</Defs>",
            }));
            Test.WriteDataFile(@"TempTestMod\notxml.xml", "这不是 XML，应当回退到 markdown 解析\n\n## 回退项\n\n正文\n");
            Test.WriteDataFile(@"TempTestMod\empty.md", "");

            var folders = new List<string> { root };
            TestListData data = TestListScanner.ScanPaths(folders);
            Test.Check(data != null, "scan: ScanPaths returns a dataset");
            if (data == null) return;

            // 4 source documents: md.md, defs.xml, notxml.xml (markdown fallback), empty.md.
            // No feedback file exists yet at this point.
            Test.Equal(4, data.Documents.Count, "document count before any feedback file exists");

            TestDocument mdDoc = FindDoc(data, "md.md");
            TestDocument xmlDoc = FindDoc(data, "defs.xml");
            TestDocument fallback = FindDoc(data, "notxml.xml");
            TestDocument emptyDoc = FindDoc(data, "empty.md");
            Test.Check(mdDoc != null, "discovery: md.md discovered");
            Test.Check(xmlDoc != null, "discovery: defs.xml discovered");
            Test.Check(fallback != null, "discovery: malformed-XML file still discovered");
            Test.Check(emptyDoc != null, "discovery: empty .md still discovered (0 entries)");

            if (mdDoc != null)
            {
                Test.Equal("md", mdDoc.FileBaseName, "md: FileBaseName");
                Test.Equal("md.md", mdDoc.FileName, "md: FileName");
                // Fixture headings: "# 清单 A", "## 项 1", "## 项 2".
                Test.Equal(3, mdDoc.Entries.Count, "md: entry count");
                Test.Equal(Path.GetFullPath(mdPath), mdDoc.SourcePath, "md: SourcePath is absolute and exact");
                Test.Equal("md.feedback.md", Path.GetFileName(mdDoc.FeedbackPath), "md: FeedbackPath file name");
                Test.Equal(Path.Combine(root, "md.feedback.md"), mdDoc.FeedbackPath, "md: FeedbackPath exact location");
                Test.Equal("清单 A", mdDoc.Title, "md: document title from the first level-1 heading");
            }
            if (xmlDoc != null)
            {
                Test.Check(xmlDoc.IsXml, "defs.xml: marked as XML");
                Test.Equal(1, xmlDoc.Entries.Count, "defs.xml: entry count");
                if (xmlDoc.Entries.Count > 0)
                {
                    Test.Equal("X 一号", xmlDoc.Entries[0].Title, "defs.xml: entry title from <label>");
                }
            }
            if (fallback != null)
            {
                Test.Check(!fallback.IsXml, "fallback: malformed XML falls back to markdown parsing");
                Test.Equal(1, fallback.Entries.Count, "fallback: markdown fallback found 1 entry");
            }
            if (emptyDoc != null)
            {
                Test.Equal(0, emptyDoc.Entries.Count, "empty.md: no entries");
            }

            // ---- write real feedback, then rescan: it must NOT be re-ingested ----
            if (mdDoc != null)
            {
                mdDoc.Entries[0].Status = TestStatus.Fail;
                mdDoc.Entries[0].Feedback = "发射后没有导弹";
                FeedbackDocuments.SaveDocument(mdDoc);
                Test.Check(File.Exists(mdDoc.FeedbackPath), "re-ingest guard: feedback file really exists on disk");

                TestListData second = TestListScanner.ScanPaths(folders);
                Test.Equal(4, second.Documents.Count,
                    "document count after rescan (the .feedback.md must be skipped)");
                Test.Check(FindDoc(second, "md.feedback.md") == null,
                    "re-ingest guard: md.feedback.md is not treated as a checklist");
                TestDocument againDoc = FindDoc(second, "md.md");
                Test.Check(againDoc != null && againDoc.Entries.Count == 3,
                    "re-ingest guard: the source document still has exactly 3 entries");

                // The whole point of the id scheme: marks must reconnect on reload.
                if (againDoc != null && againDoc.Entries.Count == 3)
                {
                    FeedbackData restored = FeedbackDocuments.ReadFeedback(againDoc);
                    TestStatus rs;
                    Test.Check(restored.Statuses.TryGetValue(againDoc.Entries[0].Id, out rs) && rs == TestStatus.Fail,
                        "round trip through disk: the Fail mark is restored after a rescan");
                    string rf;
                    Test.Check(restored.Feedbacks.TryGetValue(againDoc.Entries[0].Id, out rf) && rf == "发射后没有导弹",
                        "round trip through disk: the feedback text is restored after a rescan");
                }

                // A combined file under the same folder must also be skipped.
                var all = new TestListData();
                all.Documents.AddRange(second.Documents);
                FeedbackDocuments.SaveCombined(root, all);
                TestListData third = TestListScanner.ScanPaths(folders);
                Test.Equal(4, third.Documents.Count,
                    "document count with both per-file and combined feedback present");
                Test.Check(FindDoc(third, "TestList.feedback.md") == null,
                    "re-ingest guard: TestList.feedback.md is skipped");
            }
        }

        private static TestDocument FindDoc(TestListData data, string fileName)
        {
            if (data == null) return null;
            for (int i = 0; i < data.Documents.Count; i++)
            {
                if (string.Equals(data.Documents[i].FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    return data.Documents[i];
            }
            return null;
        }
    }
}
