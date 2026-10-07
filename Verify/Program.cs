// TestList -- headless verification driver.
//
// Compiled with TESTLIST_NO_VERSE against the same parsing/feedback sources the mod
// ships, and executed outside RimWorld. Exit code 0 means every assertion passed.
//
// Usage: TestList.Verify.exe --data <dir> [--keep]
using System;
using System.Collections.Generic;
using System.IO;

namespace TestList.Verify
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string dataDir = null;
            bool compileOnly = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--data" && i + 1 < args.Length) dataDir = args[i + 1];
                if (args[i] == "--compile-only") compileOnly = true;
            }
            if (compileOnly)
            {
                Console.WriteLine("compile-only: harness source compiled successfully");
                return 0;
            }
            if (dataDir.NullOrEmpty())
            {
                dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
            }
            dataDir = Path.GetFullPath(dataDir);

            Test.DataDir = dataDir;
            if (Directory.Exists(dataDir))
            {
                try { Directory.Delete(dataDir, true); }
                catch (Exception ex) { Console.Error.WriteLine("could not clean data dir: " + ex.Message); }
            }
            Directory.CreateDirectory(dataDir);

            Console.WriteLine("TestList headless verification");
            Console.WriteLine("data dir: " + dataDir);
            Console.WriteLine(new string('-', 72));

            try
            {
                MarkdownParserSuite.Run();
                XmlParserSuite.Run();
                FeedbackRoundTripSuite.Run();
                ScannerSuite.Run();
            }
            catch (Exception ex)
            {
                Test.Fail("harness", "unhandled exception: " + ex);
            }

            Console.WriteLine(new string('-', 72));
            Console.WriteLine(string.Format(
                "{0} passed, {1} failed, {2} assertions",
                Test.PassedChecks, Test.FailedChecks, Test.TotalChecks));

            if (Test.FailedChecks > 0)
            {
                Console.WriteLine();
                Console.WriteLine("FAILURES:");
                for (int i = 0; i < Test.Failures.Count; i++)
                {
                    Console.WriteLine("  * " + Test.Failures[i]);
                }
                return 1;
            }

            Console.WriteLine("ALL CHECKS PASSED");
            return 0;
        }
    }

    /// <summary>Minimal assertion recorder. Collects every failure instead of throwing.</summary>
    internal static class Test
    {
        internal static string DataDir;
        internal static int TotalChecks;
        internal static int PassedChecks;
        internal static int FailedChecks;
        internal static readonly List<string> Failures = new List<string>();

        private static string currentSuite = "?";

        internal static void Suite(string name)
        {
            currentSuite = name;
            Console.WriteLine();
            Console.WriteLine("== " + name + " ==");
        }

        internal static void Ok(string what)
        {
            TotalChecks++;
            PassedChecks++;
            Console.WriteLine("  ok   " + what);
        }

        internal static void Fail(string context, string message)
        {
            TotalChecks++;
            FailedChecks++;
            string line = currentSuite + " / " + context + ": " + message;
            Failures.Add(line);
            Console.WriteLine("  FAIL " + line);
        }

        /// <summary>
        /// Two-argument form for a self-describing condition, e.g.
        /// Test.Check(entry.ParentId == null, "root entry has no parent").
        /// </summary>
        internal static void Check(bool condition, string describe)
        {
            if (condition) Ok(describe);
            else Fail("check", describe);
        }

        internal static void Check(bool condition, string context, string describe)
        {
            if (condition) Ok(describe);
            else Fail(context, describe);
        }

        internal static void Equal(object expected, object actual, string context)
        {
            bool same = expected == null ? actual == null : expected.Equals(actual);
            if (same) Ok(context + " == " + Describe(expected));
            else Fail(context, "expected " + Describe(expected) + " but got " + Describe(actual));
        }

        internal static void Contains(string haystack, string needle, string context)
        {
            if (haystack != null && haystack.IndexOf(needle, StringComparison.Ordinal) >= 0)
                Ok(context + " contains " + Describe(needle));
            else
                Fail(context, "expected to contain " + Describe(needle) + " in " + Describe(haystack));
        }

        internal static void NotContains(string haystack, string needle, string context)
        {
            if (haystack == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
                Ok(context + " does not contain " + Describe(needle));
            else
                Fail(context, "expected NOT to contain " + Describe(needle) + " but found it in " + Describe(haystack));
        }

        /// <summary>Writes a file into the harness data directory and returns its full path.</summary>
        internal static string WriteDataFile(string relativePath, string content, bool bom = false)
        {
            string full = Path.Combine(DataDir, relativePath);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(full));
            var enc = new System.Text.UTF8Encoding(bom);
            File.WriteAllText(full, content, enc);
            return full;
        }

        internal static string Describe(object v)
        {
            if (v == null) return "<null>";
            string s = v as string;
            if (s == null) return v.ToString();
            if (s.Length > 180) s = s.Substring(0, 180) + "...";
            return "\"" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }
    }
}
