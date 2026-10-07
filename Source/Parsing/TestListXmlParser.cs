// TestList -- XML checklist parser.
//
// Accepts two shapes:
//   1. A dedicated checklist document (root <TestList>, children <Test>/<TestItem>),
//      including nested <Test> elements.
//   2. Any other well-formed XML (typically a RimWorld Defs file): every top-level
//      element becomes one entry, titled from <label> / <defName> when present.
//
// Returning null is the "this is not XML at all, caller should fall back to markdown"
// signal; it is used only for malformed input.
//
// This file is compiled BOTH in-game and headless (TESTLIST_NO_VERSE) and therefore must
// not reference Verse/RimWorld/UnityEngine types.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace TestList
{
    public static class TestListXmlParser
    {
        /// <summary>Direct child element names that can supply an entry title.</summary>
        private static readonly HashSet<string> TitleFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Title", "Label", "Name", "Description", "Desc", "Steps", "Expect", "Expected"
        };

        /// <summary>
        /// Parses XML checklist text. Returns null only when the input is not well-formed
        /// XML (so the scanner can retry it as markdown); a well-formed document always
        /// yields a list, possibly empty.
        /// </summary>
        public static List<TestEntry> ParseXmlList(string xml, string fileNameForRootEntry)
        {
            if (string.IsNullOrEmpty(xml))
            {
                TestListLog.Warning("XML checklist '" + (fileNameForRootEntry ?? "?") +
                    "' is empty; not treating it as XML.");
                return null;
            }

            try
            {
                XNode root = ParseTree(xml);
                List<TestEntry> entries = new List<TestEntry>();
                string fileBase = fileNameForRootEntry ?? "";
                if (root == null) return entries; // well-formed but contains no elements
                if (LooksLikeTestListDocument(root)) BuildDedicatedEntries(root, fileBase, entries);
                else BuildDefinitionEntries(root, fileBase, entries);
                return entries;
            }
            catch (Exception ex)
            {
                TestListLog.Warning("'" + (fileNameForRootEntry ?? "?") +
                    "' is not a test-list XML document (" + ex.Message + "); falling back to markdown.");
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Lightweight DOM (keeps IXmlLineInfo line numbers, which XmlDocument loses)
        // ------------------------------------------------------------------

        private sealed class XNode
        {
            public string Name = "";
            public string LocalName = "";
            public readonly List<KeyValuePair<string, string>> Attributes = new List<KeyValuePair<string, string>>();
            public readonly List<XNode> Children = new List<XNode>();
            public readonly StringBuilder Text = new StringBuilder();

            /// <summary>0-based source line, or -1 when the reader cannot report one.</summary>
            public int Line = -1;

            /// <summary>Index within the parent (used as the id line number when Line is -1).</summary>
            public int Index;
        }

        private static XNode ParseTree(string xml)
        {
            XNode root = null;
            List<XNode> roots = new List<XNode>();
            // A List used as a stack on purpose: System.Collections.Generic.Stack<T> lives in
            // System.dll for .NET Framework but is resolved against mscorlib by this build's
            // reference set, which makes it fail to load at runtime outside the editor.
            List<XNode> open = new List<XNode>();

            XmlReaderSettings settings = new XmlReaderSettings();
            settings.IgnoreComments = true;
            settings.IgnoreProcessingInstructions = true;
            settings.IgnoreWhitespace = false;
            settings.DtdProcessing = DtdProcessing.Prohibit;
            settings.XmlResolver = null;
            settings.CheckCharacters = false;

            using (StringReader textReader = new StringReader(xml))
            using (XmlReader reader = XmlReader.Create(textReader, settings))
            {
                while (reader.Read())
                {
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                        {
                            XNode node = new XNode();
                            node.Name = reader.Name;
                            node.LocalName = reader.LocalName;

                            IXmlLineInfo lineInfo = reader as IXmlLineInfo;
                            node.Line = lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LineNumber - 1 : -1;

                            if (reader.HasAttributes)
                            {
                                for (int i = 0; i < reader.AttributeCount; i++)
                                {
                                    reader.MoveToAttribute(i);
                                    node.Attributes.Add(new KeyValuePair<string, string>(reader.LocalName, reader.Value));
                                }
                                reader.MoveToElement();
                            }

                            if (open.Count == 0)
                            {
                                node.Index = roots.Count;
                                roots.Add(node);
                            }
                            else
                            {
                                XNode parent = open[open.Count - 1];
                                node.Index = parent.Children.Count;
                                parent.Children.Add(node);
                            }

                            if (!reader.IsEmptyElement) open.Add(node);
                            break;
                        }

                        case XmlNodeType.EndElement:
                            if (open.Count > 0) open.RemoveAt(open.Count - 1);
                            break;

                        case XmlNodeType.Text:
                        case XmlNodeType.CDATA:
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            if (open.Count > 0) open[open.Count - 1].Text.Append(reader.Value);
                            break;
                    }
                }
            }

            if (roots.Count > 0) root = roots[0];
            return root;
        }

        // ------------------------------------------------------------------
        // Shape 1: dedicated <TestList> document
        // ------------------------------------------------------------------

        private static bool LooksLikeTestListDocument(XNode root)
        {
            if (string.Equals(root.LocalName, "TestList", StringComparison.OrdinalIgnoreCase)) return true;
            if (IsTestElement(root)) return true;
            if (root.Children.Count == 0) return false;
            for (int i = 0; i < root.Children.Count; i++)
            {
                if (!IsTestElement(root.Children[i])) return false;
            }
            return true;
        }

        private static bool IsTestElement(XNode node)
        {
            if (node == null) return false;
            return string.Equals(node.LocalName, "Test", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.LocalName, "TestItem", StringComparison.OrdinalIgnoreCase);
        }

        private static void BuildDedicatedEntries(XNode root, string fileBase, List<TestEntry> entries)
        {
            if (IsTestElement(root))
            {
                EmitTestNode(root, null, null, 1, fileBase, entries);
                return;
            }

            for (int i = 0; i < root.Children.Count; i++)
            {
                if (IsTestElement(root.Children[i])) EmitTestNode(root.Children[i], null, null, 1, fileBase, entries);
            }
        }

        private static void EmitTestNode(XNode node, string parentId, string parentPath, int level,
            string fileBase, List<TestEntry> entries)
        {
            string label = AttributeValue(node, "Label");
            string defName = AttributeValue(node, "defName");

            string title = label;
            if (string.IsNullOrEmpty(title)) title = defName;
            if (string.IsNullOrEmpty(title)) title = FirstTitleFieldText(node);
            if (string.IsNullOrEmpty(title)) title = node.Text.ToString().Trim();
            if (string.IsNullOrEmpty(title)) title = node.LocalName;
            title = title.Trim();

            string path = string.IsNullOrEmpty(parentPath) ? title : parentPath + " / " + title;
            string key = string.IsNullOrEmpty(defName) ? path : defName;

            TestEntry entry = new TestEntry();
            entry.RawHeading = title;
            entry.Title = title;
            entry.Level = level;
            entry.SourceLine = SourceLineOf(node);
            entry.ParentId = parentId;
            entry.Path = path;
            entry.Content = BuildTestContent(node);
            entry.Id = MakeId(fileBase, entry.SourceLine, key);
            entries.Add(entry);

            for (int i = 0; i < node.Children.Count; i++)
            {
                XNode child = node.Children[i];
                if (IsTestElement(child)) EmitTestNode(child, entry.Id, path, level + 1, fileBase, entries);
            }
        }

        /// <summary>
        /// The body of a dedicated <Test>: its non-nested fields, rendered as
        /// "FieldName: text" for leaf fields and as pretty-printed XML otherwise.
        /// </summary>
        private static string BuildTestContent(XNode node)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < node.Children.Count; i++)
            {
                XNode child = node.Children[i];
                if (IsTestElement(child)) continue;

                if (child.Children.Count == 0)
                {
                    string text = child.Text.ToString().Trim();
                    if (text.Length > 0) parts.Add(child.LocalName + ": " + text);
                }
                else
                {
                    parts.Add(PrettyPrint(child, 0));
                }
            }

            if (parts.Count == 0) return node.Text.ToString().Trim();
            return string.Join("\n", parts.ToArray());
        }

        private static string FirstTitleFieldText(XNode node)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                XNode child = node.Children[i];
                if (!TitleFieldNames.Contains(child.LocalName)) continue;
                string text = child.Text.ToString().Trim();
                if (text.Length > 0) return text;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Shape 2: RimWorld Defs / arbitrary XML
        // ------------------------------------------------------------------

        private static void BuildDefinitionEntries(XNode root, string fileBase, List<TestEntry> entries)
        {
            // A single-def document (root itself carries defName/label) becomes one entry.
            if (FieldText(root, "defName") != null || FieldText(root, "label") != null)
            {
                AddDefinitionEntry(root, fileBase, entries);
                return;
            }

            for (int i = 0; i < root.Children.Count; i++) AddDefinitionEntry(root.Children[i], fileBase, entries);
        }

        private static void AddDefinitionEntry(XNode node, string fileBase, List<TestEntry> entries)
        {
            string defName = FieldText(node, "defName");
            string label = FieldText(node, "label");

            string title = !string.IsNullOrEmpty(label) ? label
                : (!string.IsNullOrEmpty(defName) ? defName : node.LocalName);
            string key = !string.IsNullOrEmpty(defName) ? defName : title;

            TestEntry entry = new TestEntry();
            entry.RawHeading = title;
            entry.Title = title;
            entry.Level = 1;
            entry.SourceLine = SourceLineOf(node);
            entry.ParentId = null;
            entry.Path = title;
            entry.Content = PrettyPrint(node, 0);
            entry.Id = MakeId(fileBase, entry.SourceLine, key);
            entries.Add(entry);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>Trimmed text of the first direct child named <paramref name="name"/>, else null.</summary>
        private static string FieldText(XNode node, string name)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                XNode child = node.Children[i];
                if (!string.Equals(child.LocalName, name, StringComparison.OrdinalIgnoreCase)) continue;
                string text = child.Text.ToString().Trim();
                return text.Length > 0 ? text : null;
            }
            return null;
        }

        private static string AttributeValue(XNode node, string name)
        {
            for (int i = 0; i < node.Attributes.Count; i++)
            {
                if (string.Equals(node.Attributes[i].Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    string value = node.Attributes[i].Value;
                    return value == null ? null : value.Trim();
                }
            }
            return null;
        }

        private static int SourceLineOf(XNode node)
        {
            return node.Line >= 0 ? node.Line : node.Index;
        }

        /// <summary>Deterministic pretty-printed XML for one element, 2-space indented.</summary>
        private static string PrettyPrint(XNode node, int indent)
        {
            StringBuilder sb = new StringBuilder();
            PrettyPrintInto(sb, node, indent);
            return sb.ToString();
        }

        private static void PrettyPrintInto(StringBuilder sb, XNode node, int indent)
        {
            string pad = new string(' ', indent * 2);
            sb.Append('<').Append(node.Name);
            for (int i = 0; i < node.Attributes.Count; i++)
            {
                sb.Append(' ').Append(node.Attributes[i].Key).Append("=\"")
                  .Append(EscapeAttribute(node.Attributes[i].Value)).Append('"');
            }

            string text = node.Text.ToString().Trim();
            if (node.Children.Count == 0 && text.Length == 0)
            {
                sb.Append(" />");
                return;
            }

            sb.Append('>');
            if (node.Children.Count == 0)
            {
                sb.Append(EscapeText(text));
            }
            else
            {
                string innerPad = new string(' ', (indent + 1) * 2);
                for (int i = 0; i < node.Children.Count; i++)
                {
                    sb.Append('\n').Append(innerPad);
                    PrettyPrintInto(sb, node.Children[i], indent + 1);
                }
                if (text.Length > 0) sb.Append('\n').Append(innerPad).Append(EscapeText(text));
                sb.Append('\n').Append(pad);
            }
            sb.Append("</").Append(node.Name).Append('>');
        }

        private static string EscapeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string EscapeAttribute(string value)
        {
            return EscapeText(value).Replace("\"", "&quot;");
        }

        /// <summary>"xml:" + SHA1(FileBaseName \u0001 SourceLine \u0001 defName-or-path), lowercase hex.</summary>
        private static string MakeId(string fileBase, int sourceLine, string key)
        {
            string payload = (fileBase ?? "") + "\u0001" +
                sourceLine.ToString(CultureInfo.InvariantCulture) + "\u0001" + (key ?? "");
            return "xml:" + Sha1Hex(payload);
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
