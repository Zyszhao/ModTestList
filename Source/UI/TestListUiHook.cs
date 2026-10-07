// TestList -- Harmony hooks and the single funnel that opens/closes the panel.
//
// Four patches live here:
//   1. RimWorld.UIRoot_Play.UIRootOnGUI    (postfix) -- draws the on-screen open button.
//   2. Verse.TickManager.TickManagerUpdate (postfix) -- the debounced auto-save pump.
//   3. Verse.Root.Shutdown                 (postfix) -- flush pending feedback on quit to OS.
//   4. Verse.GenScene.GoToMainMenu         (postfix) -- same, for the "quit to menu" path.
//
// Verified against the RimWorld 1.6 decompilation shipped with this toolchain:
//   * Source/RimWorld/UIRoot_Play.cs:21   public override void UIRootOnGUI()
//   * Source/Verse/TickManager.cs:371     public void TickManagerUpdate()   <-- NOT "DoTick"
//   * Source/Verse/Root.cs:193            public static void Shutdown()     <-- NOT "OnApplicationQuit"
//   * Source/Verse/GenScene.cs:31         public static void GoToMainMenu()
//
// NO HOTKEY IS REGISTERED, DELIBERATELY. The first release grabbed raw F8/F9 KeyDown events
// from this very postfix, which runs BEFORE UIRoot_Play.HandleLowPriorityShortcuts() -- so it
// consumed F8 and broke the vanilla "view world" hotkey. The panel is opened by clicking the
// on-screen button, or from Options > Mod settings. This mod never touches the keyboard.
//
// SAFETY: a postfix on UIRootOnGUI runs once per frame, forever. Any exception escaping it
// would therefore be thrown (and logged by Log.Error) every single frame until the game is
// closed, which both spams the log and can break the rest of the frame's GUI. Every hook body
// below is wrapped in try/catch; the GUI hook additionally latches a `guiBroken` flag on the
// first failure so it disables itself instead of repeating.
using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace TestList
{
    public static class TestListUiHook
    {
        // ------------------------------------------------------------------
        // Safety latches
        // ------------------------------------------------------------------

        /// <summary>
        /// True once the overlay GUI hook has thrown. The postfix returns immediately from
        /// then on, so a persistent failure costs one log line instead of one per frame.
        /// </summary>
        private static bool guiBroken;

        /// <summary>Failures already logged by the tick hook; keeps a broken tick quiet.</summary>
        private static int autoSaveFailures;

        private const int MaxAutoSaveFailuresLogged = 3;

        /// <summary>True when the overlay disabled itself after a failure.</summary>
        public static bool OverlayDisabled
        {
            get { return guiBroken; }
        }

        private static bool targetsVerified;

        /// <summary>True once we have logged that the panel really opened.</summary>
        private static bool loggedFirstOpen;

        // ------------------------------------------------------------------
        // Panel open / close
        // ------------------------------------------------------------------

        /// <summary>
        /// True when a Window_TestList is on the stack. Safe to call from OnGUI and from a
        /// patch: Find.WindowStack is null in the entry scene and during long events.
        /// </summary>
        public static bool IsPanelOpen
        {
            get
            {
                WindowStack stack = Find.WindowStack;
                if (stack == null) return false;
                return stack.IsOpen(typeof(Window_TestList));
            }
        }

        public static void TogglePanel()
        {
            if (IsPanelOpen) ClosePanel();
            else OpenPanel();
        }

        public static void OpenPanel()
        {
            WindowStack stack = Find.WindowStack;
            if (stack == null) return;
            try
            {
                // Parses the mods' /TestList folders on first use. Idempotent.
                TestListManager.EnsureInitialized();
                if (!stack.IsOpen(typeof(Window_TestList)))
                {
                    stack.Add(new Window_TestList());
                }
                SyncWindowOpenSetting();

                if (!loggedFirstOpen)
                {
                    loggedFirstOpen = true;
                    int total, pass, fail, untested;
                    TestListManager.CountStatuses(out total, out pass, out fail, out untested);
                    TestListLog.Message("Checklist panel opened: " +
                        TestListManager.Data.Documents.Count + " document(s), " + total + " test item(s).");
                }
            }
            catch (Exception ex)
            {
                TestListLog.Exception("Failed to open the test checklist panel", ex);
            }
        }

        public static void ClosePanel()
        {
            WindowStack stack = Find.WindowStack;
            if (stack == null) return;
            try
            {
                stack.TryRemove(typeof(Window_TestList), false);
                SyncWindowOpenSetting();
            }
            catch (Exception ex)
            {
                TestListLog.Exception("Failed to close the test checklist panel", ex);
            }
        }

        /// <summary>Mirrors the live window-stack state into the persisted setting.</summary>
        public static void SyncWindowOpenSetting()
        {
            try
            {
                if (TestListMod.Settings == null) return;
                TestListMod.Settings.WindowOpen = IsPanelOpen;
            }
            catch (Exception ex)
            {
                TestListLog.Exception("Failed to persist the panel open state", ex);
            }
        }

        // ------------------------------------------------------------------
        // Patch 1: the per-frame GUI hook
        // ------------------------------------------------------------------
        //
        // NOTE: this postfix deliberately draws NOTHING. Two earlier releases drew a floating
        // button here; both were invisible, because UIRootOnGUI() runs BEFORE
        // WindowStackOnGUI(), and the vanilla resource/alert readouts are windows drawn inside
        // WindowStackOnGUI() -- so they covered the button. The open affordance is now a
        // proper MainButtonDef in the bottom bar (Defs/MainButtonDefs/), which RimWorld lays
        // out itself. This patch is kept only to verify our Harmony targets once, so a broken
        // hook is reported instead of failing silently.

        [HarmonyPatch(typeof(RimWorld.UIRoot_Play), nameof(RimWorld.UIRoot_Play.UIRootOnGUI))]
        [HarmonyPostfix]
        private static void OverlayPostfix()
        {
            if (guiBroken) return;

            try
            {
                VerifyPatchTargetsOnce();
            }
            catch (Exception ex)
            {
                guiBroken = true;
                TestListLog.Exception("TestList Harmony target verification failed", ex);
            }
        }

        // ------------------------------------------------------------------
        // Patch 2: the auto-save pump
        // ------------------------------------------------------------------

        /// <summary>
        /// TickManager.TickManagerUpdate() is called every frame from Game.UpdatePlay()
        /// (Source/Verse/Game.cs:678) and runs even while the game is paused -- which is
        /// exactly when the player is typing feedback, so it is a better auto-save pump than
        /// DoSingleTick(), which only runs while time advances.
        /// </summary>
        [HarmonyPatch(typeof(Verse.TickManager), nameof(Verse.TickManager.TickManagerUpdate))]
        [HarmonyPostfix]
        private static void AutoSavePostfix()
        {
            try
            {
                VerifyPatchTargetsOnce();
                if (TestListMod.Settings == null) return;
                TestListManager.TickAutoSave();
            }
            catch (Exception ex)
            {
                // Never let this throw into the game's tick loop. Keep trying (a transient
                // file lock must not permanently disable saving) but stop spamming the log.
                autoSaveFailures++;
                if (autoSaveFailures <= MaxAutoSaveFailuresLogged)
                {
                    TestListLog.Exception("TestList auto-save tick failed", ex);
                }
            }
        }

        // ------------------------------------------------------------------
        // Patch 3: flush on quit to OS
        // ------------------------------------------------------------------

        /// <summary>
        /// Root.OnApplicationQuit does not exist in the 1.6 decompilation, so the flush is
        /// attached to Root.Shutdown() (Source/Verse/Root.cs:193) instead. That is the method
        /// RimWorld's "Quit to OS" flow calls (Source/RimWorld/MainMenuDrawer.cs:205, :227,
        /// :230, :238 and Source/Verse/GenCommandLine.cs:64). It is a static method, so this
        /// postfix is static and parameterless.
        /// </summary>
        [HarmonyPatch(typeof(Verse.Root), nameof(Verse.Root.Shutdown))]
        [HarmonyPostfix]
        private static void QuitPostfix()
        {
            try
            {
                TestListManager.FlushOnQuit();
            }
            catch (Exception ex)
            {
                // Logging during shutdown must not throw back into Root.Shutdown().
                try { TestListLog.Exception("TestList flush on quit failed", ex); }
                catch { }
            }
        }

        // ------------------------------------------------------------------
        // Patch 4: flush on quit to the main menu
        // ------------------------------------------------------------------

        /// <summary>
        /// The "quit to main menu" path does not go through Root.Shutdown, so without this a
        /// mark made inside the auto-save debounce window would be lost on the way out.
        /// GenScene.GoToMainMenu (Source/Verse/GenScene.cs:31) is static and parameterless.
        /// </summary>
        [HarmonyPatch(typeof(Verse.GenScene), nameof(Verse.GenScene.GoToMainMenu))]
        [HarmonyPostfix]
        private static void GoToMainMenuPostfix()
        {
            try
            {
                TestListManager.FlushOnQuit();
            }
            catch (Exception ex)
            {
                try { TestListLog.Exception("TestList flush on going to the main menu failed", ex); }
                catch { }
            }
        }

        // ------------------------------------------------------------------
        // Patch-target self check
        // ------------------------------------------------------------------

        /// <summary>
        /// Harmony's PatchAll throws (and TestListMod.EnsureHarmonyPatched swallows it) when a
        /// patch class names a target that no longer exists. Run this once at runtime so a
        /// renamed/removed game method yields one clear warning naming it, instead of a
        /// silently dead hook. Idempotent; re-entrant-safe; never throws.
        /// </summary>
        public static void VerifyPatchTargets()
        {
            if (targetsVerified) return;
            targetsVerified = true;

            VerifyTarget(typeof(RimWorld.UIRoot_Play), "UIRootOnGUI");
            VerifyTarget(typeof(Verse.TickManager), "TickManagerUpdate");
            VerifyTarget(typeof(Verse.Root), "Shutdown");
            VerifyTarget(typeof(Verse.GenScene), "GoToMainMenu");
        }

        private static void VerifyPatchTargetsOnce()
        {
            if (targetsVerified) return;
            try
            {
                VerifyPatchTargets();
            }
            catch (Exception ex)
            {
                targetsVerified = true;
                TestListLog.Exception("Harmony target verification failed", ex);
            }
        }

        private static void VerifyTarget(Type type, string methodName)
        {
            try
            {
                MethodInfo method = AccessTools.Method(type, methodName);
                if (method == null)
                {
                    TestListLog.Warning(
                        "Harmony target not found: " + type.FullName + "." + methodName +
                        " -- that TestList hook is inactive on this game build.");
                }
            }
            catch (Exception ex)
            {
                TestListLog.Exception(
                    "Could not verify Harmony target " + type.FullName + "." + methodName, ex);
            }
        }
    }
}
