// TestList -- pure-logic logging shim.
//
// Parsers and the feedback writer are compiled twice:
//   * in-game, where TESTLIST_NO_VERSE is undefined and warnings go to Verse.Log
//   * headless (Verify harness), where TESTLIST_NO_VERSE is defined and warnings
//     go to the console.
// Nothing else in the pure-logic files may reference Verse/RimWorld types.
using System;

namespace TestList
{
    internal static class TestListLog
    {
        internal static void Warning(string message)
        {
#if TESTLIST_NO_VERSE
            Console.Error.WriteLine("[TestList][warn] " + message);
#else
            Verse.Log.Warning("[TestList] " + message);
#endif
        }

        internal static void Message(string message)
        {
#if TESTLIST_NO_VERSE
            Console.Out.WriteLine("[TestList] " + message);
#else
            Verse.Log.Message("[TestList] " + message);
#endif
        }

        internal static void Error(string message)
        {
#if TESTLIST_NO_VERSE
            Console.Error.WriteLine("[TestList][error] " + message);
#else
            Verse.Log.Error("[TestList] " + message);
#endif
        }

        internal static void Exception(string context, Exception ex)
        {
            Error(context + ": " + (ex == null ? "(null)" : ex.ToString()));
        }
    }
}
