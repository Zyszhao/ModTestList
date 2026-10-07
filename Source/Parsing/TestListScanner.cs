// TestList -- /TestList folder scanner.
//
// Finds every checklist file a mod (or an explicitly configured folder) offers, parses it
// and returns the finished TestListData. This is the only place that touches the file
// system for input, and it is deliberately defensive: one unreadable file, one bad mod or
// one malformed XML document must never abort a scan.
//
// Compiled BOTH in-game and headless (TESTLIST_NO_VERSE). The only Verse type touched is
// ModLister, and only inside `#if !TESTLIST_NO_VERSE`. Under TESTLIST_NO_VERSE the
// headless entry point is ScanPaths(folders).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TestList
{
    public static class TestListScanner
    {
        /// <summary>Exact first line of every feedback document this mod writes.</summary>
        public const string FeedbackMarker = "<!-- TestList feedback v1 -->";

        /// <summary>
        /// Prefix test used to recognise - and refuse to re-ingest - our own output. It also
        /// catches feedback documents written by a future format version.
        /// </summary>
        public const string FeedbackMarkerPrefix = "<!-- TestList feedback";

        /// <summary>Input files above this size are skipped; a checklist is never this big.</summary>
        private const long MaxFileBytes = 4L * 1024L * 1024L;

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Scans every enabled mod root plus settings.ExtraScanPaths for a "TestList" folder.
        /// </summary>
        public static TestListData Scan(TestListSettings settings)
        {
#if TESTLIST_NO_VERSE
            // The headless verification build has no ModLister and no ModSettings, so the
            // only folders available are the explicit extra paths. Behaviour for discovery,
            // filtering, ids and sorting is identical because it is the same code path.
            List<string> explicitFolders = new List<string>();
            if (settings != null && settings.ExtraScanPaths != null)
            {
                explicitFolders.AddRange(settings.ExtraScanPaths);
            }
            return ScanPaths(explicitFolders);
#else
            TestListData data = new TestListData();
            try
            {
                data.LoadedUtc = DateTime.UtcNow;

                HashSet<string> seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> seenModNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                List<ModRoot> mods = DiscoverActiveMods(data);
                for (int i = 0; i < mods.Count; i++)
                {
                    ModRoot mod = mods[i];
                    try
                    {
                        List<string> candidates = FindTestListFolders(mod.RootDir);
                        if (candidates.Count == 0) continue;

                        // A mod counts as scanned as soon as it has a /TestList folder, even
                        // when that folder turns out to be empty.
                        if (seenModNames.Add(mod.Name)) data.ScannedModNames.Add(mod.Name);

                        for (int j = 0; j < candidates.Count; j++)
                        {
                            string folder = candidates[j];
                            if (!seenFolders.Add(folder)) continue;
                            ScanFolder(data, folder, mod.Name, mod.PackageId, mod.RootDir, seenFiles);
                        }
                    }
                    catch (Exception ex)
                    {
                        data.LastError = "Failed to scan mod '" + mod.Name + "': " + ex.Message;
                        TestListLog.Exception("Failed to scan mod '" + mod.Name + "'", ex);
                    }
                }

                // Extra configured folders stand in for a mod's /TestList folder.
                if (settings != null && settings.ExtraScanPaths != null)
                {
                    for (int i = 0; i < settings.ExtraScanPaths.Count; i++)
                    {
                        string configured = settings.ExtraScanPaths[i];
                        if (string.IsNullOrEmpty(configured)) continue;
                        try
                        {
                            string folder = Path.GetFullPath(configured);
                            if (!Directory.Exists(folder)) continue;
                            if (!seenFolders.Add(folder)) continue;

                            string parent = "";
                            try { parent = Path.GetDirectoryName(folder) ?? ""; }
                            catch (Exception) { parent = ""; }

                            ScanFolder(data, folder, FolderName(folder), "", parent, seenFiles);
                        }
                        catch (Exception ex)
                        {
                            TestListLog.Exception("Ignoring scan path '" + configured + "'", ex);
                        }
                    }
                }

                SortDocuments(data);
            }
            catch (Exception ex)
            {
                data.LastError = "Scan failed: " + ex.Message;
                TestListLog.Exception("Scan failed", ex);
            }
            return data;
#endif
        }

        /// <summary>
        /// Scans explicit folders as if each were a mod's /TestList folder, with no mod
        /// metadata available. This is the headless-testable entry point: it needs no Verse
        /// types at all. Folders that do not exist are silently ignored, and it never throws.
        /// ModName is the containing folder's name; ModPackageId and ModRootDir stay empty.
        /// </summary>
        public static TestListData ScanPaths(IEnumerable<string> folders)
        {
            TestListData data = new TestListData();
            try
            {
                data.LoadedUtc = DateTime.UtcNow;

                HashSet<string> seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (folders != null)
                {
                    foreach (string configured in folders)
                    {
                        if (string.IsNullOrEmpty(configured)) continue;
                        try
                        {
                            string folder = Path.GetFullPath(configured);
                            if (!Directory.Exists(folder)) continue;
                            if (!seenFolders.Add(folder)) continue;

                            ScanFolder(data, folder, FolderName(folder), "", "", seenFiles);
                        }
                        catch (Exception ex)
                        {
                            data.LastError = "Failed to scan " + configured + ": " + ex.Message;
                            TestListLog.Exception("Failed to scan " + configured, ex);
                        }
                    }
                }

                SortDocuments(data);
            }
            catch (Exception ex)
            {
                data.LastError = "ScanPaths failed: " + ex.Message;
                TestListLog.Exception("ScanPaths failed", ex);
            }
            return data;
        }

        /// <summary>
        /// Absolute output path used for one document's feedback file: the file name plus
        /// ".feedback.md", next to the source file inside its /TestList folder.
        /// </summary>
        public static string GetFeedbackPath(TestDocument doc)
        {
            if (doc == null) return "";
            try
            {
                string directory = "";
                if (!string.IsNullOrEmpty(doc.SourcePath))
                {
                    try { directory = Path.GetDirectoryName(doc.SourcePath) ?? ""; }
                    catch (Exception) { directory = ""; }
                }

                if (string.IsNullOrEmpty(directory))
                {
                    // No source path: fall back to <mod root>\TestList.
                    string modRoot = doc.ModRootDir ?? "";
                    directory = string.IsNullOrEmpty(modRoot) ? "" : Path.Combine(modRoot, "TestList");
                }

                string fileName = (doc.FileBaseName ?? "") + ".feedback.md";
                return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
            }
            catch (Exception ex)
            {
                TestListLog.Exception("GetFeedbackPath failed for '" + doc.FileName + "'", ex);
                return "";
            }
        }

        // ------------------------------------------------------------------
        // Mod discovery
        // ------------------------------------------------------------------

        /// <summary>One mod root worth probing for a /TestList folder.</summary>
        private sealed class ModRoot
        {
            public string Name = "";
            public string PackageId = "";
            public string RootDir = "";
        }

#if !TESTLIST_NO_VERSE
        /// <summary>Every installed mod that is active and has a root directory.</summary>
        private static List<ModRoot> DiscoverActiveMods(TestListData data)
        {
            List<ModRoot> mods = new List<ModRoot>();
            try
            {
                foreach (Verse.ModMetaData mod in Verse.ModLister.AllInstalledMods)
                {
                    try
                    {
                        if (mod == null) continue;
                        if (!mod.Active) continue;
                        if (mod.RootDir == null) continue;

                        ModRoot root = new ModRoot();
                        root.Name = mod.Name ?? "";
                        root.PackageId = mod.PackageId ?? "";
                        root.RootDir = mod.RootDir.FullName;
                        mods.Add(root);
                    }
                    catch (Exception ex)
                    {
                        TestListLog.Warning("Skipping an unreadable installed mod: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                data.LastError = "Could not enumerate installed mods: " + ex.Message;
                TestListLog.Exception("Could not enumerate installed mods", ex);
            }
            return mods;
        }
#endif

        /// <summary>
        /// The /TestList folder of a mod root, in every spelling the file system may use.
        /// Case-insensitive matching matters on Linux/macOS and for hand-made folders.
        /// </summary>
        private static List<string> FindTestListFolders(string modRootDir)
        {
            List<string> folders = new List<string>();
            if (string.IsNullOrEmpty(modRootDir)) return folders;

            try
            {
                string direct = Path.Combine(modRootDir, "TestList");
                if (Directory.Exists(direct)) folders.Add(direct);

                string[] subdirectories = Directory.GetDirectories(modRootDir);
                for (int i = 0; i < subdirectories.Length; i++)
                {
                    if (!string.Equals(Path.GetFileName(subdirectories[i]), "TestList", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool duplicate = false;
                    for (int j = 0; j < folders.Count; j++)
                    {
                        if (string.Equals(folders[j], subdirectories[i], StringComparison.OrdinalIgnoreCase))
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate) folders.Add(subdirectories[i]);
                }
            }
            catch (Exception ex)
            {
                TestListLog.Exception("Could not list /TestList folders under '" + modRootDir + "'", ex);
            }

            return folders;
        }

        // ------------------------------------------------------------------
        // Folder scanning
        // ------------------------------------------------------------------

        private static void ScanFolder(TestListData data, string testListFolder, string modName,
            string packageId, string modRootDir, HashSet<string> seenFiles)
        {
            List<string> files = new List<string>();
            CollectFiles(testListFolder, files);
            for (int i = 0; i < files.Count; i++)
            {
                TryAddDocument(data, files[i], modName, packageId, modRootDir, seenFiles);
            }
        }

        /// <summary>
        /// Files directly inside the /TestList folder plus one level of subfolders - never
        /// deeper, so a checklist folder holding art or sources cannot explode the scan.
        /// </summary>
        private static void CollectFiles(string folder, List<string> into)
        {
            try
            {
                string[] direct = Directory.GetFiles(folder);
                for (int i = 0; i < direct.Length; i++)
                {
                    if (IsCandidateFile(direct[i])) into.Add(direct[i]);
                }

                string[] subfolders = Directory.GetDirectories(folder);
                for (int i = 0; i < subfolders.Length; i++)
                {
                    string[] nested;
                    try
                    {
                        nested = Directory.GetFiles(subfolders[i]);
                    }
                    catch (Exception ex)
                    {
                        TestListLog.Exception("Could not list '" + subfolders[i] + "'", ex);
                        continue;
                    }

                    for (int j = 0; j < nested.Length; j++)
                    {
                        if (IsCandidateFile(nested[j])) into.Add(nested[j]);
                    }
                }
            }
            catch (Exception ex)
            {
                TestListLog.Exception("Could not list '" + folder + "'", ex);
            }
        }

        private static bool IsCandidateFile(string path)
        {
            string extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension)) return false;
            return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryAddDocument(TestListData data, string path, string modName,
            string packageId, string modRootDir, HashSet<string> seenFiles)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (!seenFiles.Add(fullPath)) return;

                string fileName = Path.GetFileName(fullPath);
                if (string.IsNullOrEmpty(fileName)) return;

                // Never re-ingest our own feedback documents.
                if (fileName.EndsWith(".feedback.md", StringComparison.OrdinalIgnoreCase)) return;
                if (string.Equals(fileName, "TestList.feedback.md", StringComparison.OrdinalIgnoreCase)) return;

                FileInfo info = new FileInfo(fullPath);
                if (!info.Exists) return;
                if (info.Length > MaxFileBytes)
                {
                    data.LastError = "Skipped '" + fileName + "' (" + info.Length +
                        " bytes): larger than the 4 MB checklist limit.";
                    TestListLog.Warning(data.LastError);
                    return;
                }

                string text = ReadAllTextShared(fullPath);
                if (text == null) return;

                // Second guard, independent of the file name: any file whose first non-empty
                // line is our feedback marker is output, not input.
                if (StartsWithFeedbackMarker(text)) return;

                TestDocument doc = new TestDocument();
                doc.SourcePath = fullPath;
                doc.FileName = fileName;
                doc.FileBaseName = Path.GetFileNameWithoutExtension(fileName);
                doc.ModName = modName ?? "";
                doc.ModPackageId = packageId ?? "";
                doc.ModRootDir = modRootDir ?? "";

                string extension = Path.GetExtension(fileName);
                List<TestEntry> entries;
                if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    entries = TestListXmlParser.ParseXmlList(text, doc.FileBaseName);
                    if (entries != null)
                    {
                        doc.IsXml = true;
                    }
                    else
                    {
                        // Not really XML (or not a document we understand): read it as markdown.
                        doc.IsXml = false;
                        entries = TestListMarkdownParser.ParseMarkdownList(text, doc.FileBaseName);
                    }
                }
                else
                {
                    doc.IsXml = false;
                    entries = TestListMarkdownParser.ParseMarkdownList(text, doc.FileBaseName);
                }

                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++) doc.Entries.Add(entries[i]);
                }

                // Document title: the first level-1 heading, else the file name.
                doc.Title = doc.FileBaseName;
                for (int i = 0; i < doc.Entries.Count; i++)
                {
                    if (doc.Entries[i].Level != 1) continue;
                    if (!string.IsNullOrEmpty(doc.Entries[i].Title)) doc.Title = doc.Entries[i].Title;
                    break;
                }

                doc.FeedbackPath = GetFeedbackPath(doc);
                data.Documents.Add(doc);
            }
            catch (Exception ex)
            {
                data.LastError = "Failed to read '" + path + "': " + ex.Message;
                TestListLog.Exception("Failed to read checklist file '" + path + "'", ex);
            }
        }

        /// <summary>Reads the whole file while letting an external editor keep it open.</summary>
        private static string ReadAllTextShared(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false), true))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>True when the first non-empty line starts with the feedback marker prefix.</summary>
        private static bool StartsWithFeedbackMarker(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            int index = 0;
            while (index < text.Length)
            {
                int newline = text.IndexOf('\n', index);
                string line = newline < 0 ? text.Substring(index) : text.Substring(index, newline - index);
                string candidate = line.Trim('\uFEFF', ' ', '\t', '\r');
                if (candidate.Length > 0)
                {
                    return candidate.StartsWith(FeedbackMarkerPrefix, StringComparison.Ordinal);
                }
                if (newline < 0) break;
                index = newline + 1;
            }
            return false;
        }

        /// <summary>ModName, then FileName, then SourcePath; all case-insensitive.</summary>
        private static void SortDocuments(TestListData data)
        {
            data.Documents.Sort(delegate (TestDocument a, TestDocument b)
            {
                int result = string.Compare(a.ModName, b.ModName, StringComparison.OrdinalIgnoreCase);
                if (result != 0) return result;

                result = string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
                if (result != 0) return result;

                return string.Compare(a.SourcePath, b.SourcePath, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>The last path segment, used as ModName for explicitly scanned folders.</summary>
        private static string FolderName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (trimmed.Length == 0) return path;
                return Path.GetFileName(trimmed) ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
