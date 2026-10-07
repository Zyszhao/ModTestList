// TestList -- mod entry point.
//
// Owned by the Lead. Bootstraps Harmony, owns the settings instance, and exposes the
// settings dialog. Contains no UI drawing and no parsing; those live in their own files.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace TestList
{
    public class TestListMod : Mod
    {
        /// <summary>
        /// Global settings instance. Assigned in the constructor, which RimWorld runs while
        /// building the mod list - before any UI or parsing code can ask for it.
        /// </summary>
        public static TestListSettings Settings;

        public const string HarmonyId = "zyshzao.testlist";

        private static Harmony harmony;
        private static bool harmonyFailed;

        public TestListMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<TestListSettings>();
        }

        public override string SettingsCategory()
        {
            return TestListText.WindowTitle;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            TestListSettingsUI.Draw(inRect, Settings);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            // A change to the master toggle or the scan paths should be reflected at once.
            if (Settings != null && Settings.Enabled && !TestListManager.HasLoaded)
            {
                TestListManager.EnsureInitialized();
            }
        }

        // ------------------------------------------------------------------
        // Harmony bootstrap
        // ------------------------------------------------------------------

        internal static bool HarmonyAvailable
        {
            get { return !harmonyFailed; }
        }

        /// <summary>
        /// Idempotent. Called from the StaticConstructorOnStartup bootstrap. A failure here
        /// is logged but never thrown, so a Harmony problem cannot break game startup.
        /// </summary>
        internal static void EnsureHarmonyPatched()
        {
            if (harmony != null || harmonyFailed) return;
            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                TestListLog.Message("Harmony patches applied.");
            }
            catch (Exception ex)
            {
                harmonyFailed = true;
                harmony = null;
                TestListLog.Exception("Failed to apply Harmony patches; the checklist overlay will be unavailable.", ex);
            }
        }

        /// <summary>Removes every patch this mod applied. Used by the headless/self-test path.</summary>
        internal static void UnpatchAll()
        {
            if (harmony == null) return;
            try
            {
                harmony.UnpatchAll(HarmonyId);
            }
            catch (Exception ex)
            {
                TestListLog.Exception("Failed to unpatch", ex);
            }
            finally
            {
                harmony = null;
            }
        }
    }
}
