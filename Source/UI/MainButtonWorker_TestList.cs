// TestList -- the bottom-bar main button that opens the checklist panel.
//
// WHY A MainButtonDef AND NOT A CUSTOM WINDOW
// -------------------------------------------
// Two earlier attempts drew a floating button by hand:
//   1. a postfix on UIRoot_Play.UIRootOnGUI() -- invisible, because that postfix runs BEFORE
//      WindowStackOnGUI(), and the vanilla resource/alert readouts are windows drawn there,
//      so they covered it;
//   2. a hand-made borderless Verse.Window -- also not visible to the user.
// Both were fighting the vanilla UI. A MainButtonDef instead asks RimWorld to draw the button
// itself: MainButtonsRoot.MainButtonsOnGUI() lays every MainButtonDef out along the bottom
// edge, so no positioning, no draw order, and no layering is our problem any more.
//
// The button takes NO hotkey: `defaultHotKey`/`hotKey` are left unset, so MainButtonsOnGUI()
// never matches it. (Vanilla binds F8 to World and F9 to History, which is precisely why the
// very first release, which swallowed raw F8/F9 events, broke those two keys.)
using RimWorld;
using Verse;

namespace TestList
{
    /// <summary>
    /// Opens (or closes) the TestList checklist panel. Deliberately does NOT inherit
    /// MainButtonWorker_ToggleTab: this button owns a Window, not a MainTabWindow.
    /// </summary>
    public class MainButtonWorker_TestList : MainButtonWorker
    {
        public override void Activate()
        {
            TestListUiHook.TogglePanel();
        }

        /// <summary>Honours the "show the bottom-bar button" setting.</summary>
        public override bool Visible
        {
            get
            {
                TestListSettings settings = TestListMod.Settings;
                if (settings == null) return false;
                return settings.Enabled && settings.ShowFloatingButton;
            }
        }
    }
}
