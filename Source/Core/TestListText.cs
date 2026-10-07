// TestList -- in-game translated strings.
//
// The headless verification build (TESTLIST_NO_VERSE) uses the fallback class
// declared in TestListModels.cs instead of this one.
#if !TESTLIST_NO_VERSE
using Verse;

namespace TestList
{
    /// <summary>
    /// Every user-visible string in the mod. Keys live in
    /// Languages/English/Keyed/TestList.xml and Languages/ChineseSimplified/Keyed/TestList.xml.
    /// </summary>
    internal static class TestListText
    {
        private static string T(string key, string fallback)
        {
            string s = ("TestList_" + key).Translate();
            // Verse returns the key itself when it is missing from the language files.
            if (s.NullOrEmpty() || s == "TestList_" + key) return fallback;
            return s;
        }

        internal static string StatusPass { get { return T("StatusPass", "PASS"); } }
        internal static string StatusFail { get { return T("StatusFail", "BROKEN"); } }
        internal static string StatusUntested { get { return T("StatusUntested", "UNTESTED"); } }

        internal static string StatusPassLong { get { return T("StatusPassLong", "Working"); } }
        internal static string StatusFailLong { get { return T("StatusFailLong", "Broken"); } }
        internal static string StatusUntestedLong { get { return T("StatusUntestedLong", "Untested"); } }

        internal static string WindowTitle { get { return T("WindowTitle", "Test Checklist"); } }
        internal static string TabChecklist { get { return T("TabChecklist", "Checklist"); } }
        internal static string TabOutput { get { return T("TabOutput", "Feedback file"); } }

        internal static string Reload { get { return T("Reload", "Reload"); } }
        internal static string ReloadTip { get { return T("ReloadTip", "Re-read every enabled mod's /TestList folder from disk."); } }
        internal static string ExpandAll { get { return T("ExpandAll", "Expand all"); } }
        internal static string CollapseAll { get { return T("CollapseAll", "Collapse all"); } }
        internal static string MarkAllPass { get { return T("MarkAllPass", "Mark all passing"); } }
        internal static string ClearAll { get { return T("ClearAll", "Clear all marks"); } }
        internal static string CopyOutputPath { get { return T("CopyOutputPath", "Copy output path"); } }

        internal static string FilterLabel { get { return T("Filter", "Filter"); } }
        internal static string FilterTip { get { return T("FilterTip", "Show only entries whose title, path or body contains this text."); } }
        internal static string ShowUntested { get { return T("ShowUntested", "Untested"); } }
        internal static string ShowPass { get { return T("ShowPass", "Working"); } }
        internal static string ShowFail { get { return T("ShowFail", "Broken"); } }
        internal static string HideTested { get { return T("HideTested", "Hide tested"); } }
        internal static string OnlyFail { get { return T("OnlyFail", "Only broken"); } }

        internal static string FeedbackLabel { get { return T("Feedback", "Feedback"); } }
        internal static string FeedbackPlaceholder { get { return T("FeedbackPlaceholder", "Type what you observed..."); } }
        internal static string NoFeedback { get { return T("NoFeedback", "(no feedback)"); } }

        internal static string MarkPrompt { get { return T("MarkPrompt", "Mark"); } }

        internal static string NoDocuments { get { return T("NoDocuments", "No /TestList checklist found in any enabled mod."); } }
        internal static string NoDocumentsHint { get { return T("NoDocumentsHint", "Create a folder named 'TestList' at the root of a mod and put .md or .xml checklists inside it, then press Reload."); } }

        internal static string SearchFolders { get { return T("SearchFolders", "Searched folders"); } }
        // The bottom-bar main button (a MainButtonDef). Its on-screen text comes from the
        // <label> in Defs/MainButtonDefs/TestList_MainButtons.xml, not from these keys.
        internal static string OpenTestListButton { get { return T("OpenTestListButton", "Checklist"); } }
        internal static string OpenTestListTip { get { return T("OpenTestListTip", "Open or close the test checklist panel."); } }
        internal static string CloseWindowTip { get { return T("CloseWindowTip", "Close"); } }

        internal static string SettingsOpenPanel { get { return T("SettingsOpenPanel", "Open the checklist panel now"); } }
        internal static string SettingsPanelOnlyInGame { get { return T("SettingsPanelOnlyInGame", "The panel can only be opened while a game is loaded."); } }
        internal static string SettingsFoundFormat { get { return T("SettingsFoundFormat", "{0} checklist file(s), {1} test item(s) found."); } }

        internal static string ProgressFormat { get { return T("ProgressFormat", "{0}/{1} tested  |  {2} working  {3} broken  {4} untested"); } }
        internal static string SummaryFormat { get { return T("SummaryFormat", "{0} working / {1} broken / {2} untested"); } }
        internal static string LineFormat { get { return T("LineFormat", "line {0}"); } }

        internal static string SourceXml { get { return T("SourceXml", "XML"); } }
        internal static string SourceMd { get { return T("SourceMd", "Markdown"); } }

        internal static string ParseErrorFormat { get { return T("ParseErrorFormat", "Could not read this checklist: {0}"); } }

        internal static string SaveStateIdle { get { return T("SaveStateIdle", "Saved"); } }
        internal static string SaveStatePending { get { return T("SaveStatePending", "Saving..."); } }
        internal static string SaveStateError { get { return T("SaveStateError", "Save failed"); } }

        internal static string OutputPathFormat { get { return T("OutputPathFormat", "Writes to: {0}"); } }
        internal static string Copied { get { return T("Copied", "Copied to clipboard."); } }

        internal static string SettingEnable { get { return T("SettingEnable", "Enable in-game checklist overlay"); } }
        internal static string SettingShowButton { get { return T("SettingShowButton", "Show the checklist button in the bottom bar"); } }
        internal static string SettingWindowWidth { get { return T("SettingWindowWidth", "Panel width"); } }
        internal static string SettingMaxBlockHeight { get { return T("SettingMaxBlockHeight", "Max content height per entry before its own scrollbar"); } }
        internal static string SettingAutoSaveDelay { get { return T("SettingAutoSaveDelay", "Auto-save delay after a change (seconds)"); } }
        internal static string SettingWriteShared { get { return T("SettingWriteShared", "Also write a combined /TestList/TestList.feedback.md per mod"); } }
        internal static string SettingExtraPaths { get { return T("SettingExtraPaths", "Extra folders to scan (one absolute path per line)"); } }
        internal static string SettingAutoExpandFirst { get { return T("SettingAutoExpandFirst", "Auto-expand entries you mark as broken"); } }

        internal static string RestartRequired { get { return T("RestartRequired", "Takes effect immediately; the panel is refreshed on change."); } }
    }
}
#endif
