// TestList -- mod settings.
//
// Owned by the Lead. Read by the manager, the scanner and the UI.
// Every field is plain data so it can be serialised by Scribe without ceremony.
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TestList
{
    public class TestListSettings : ModSettings
    {
        /// <summary>Master switch for the in-game overlay.</summary>
        public bool Enabled = true;

        /// <summary>
        /// Show the checklist button in the bottom bar. That button is a MainButtonDef, so
        /// RimWorld draws and lays it out itself; this flag only hides or shows it. The panel
        /// can always still be opened from Options > Mod settings.
        /// </summary>
        public bool ShowFloatingButton = true;

        /// <summary>Panel width in UI pixels.</summary>
        public float WindowWidth = 720f;

        /// <summary>
        /// Content region height cap for one expanded entry. Content taller than this
        /// gets its own scrollbar so one huge checklist item cannot push everything off
        /// screen.
        /// </summary>
        public float MaxBlockContentHeight = 220f;

        /// <summary>Seconds of no edits before the feedback file is written.</summary>
        public float AutoSaveDelaySeconds = 1.5f;

        /// <summary>Also emit one combined &lt;modroot&gt;/TestList/TestList.feedback.md per mod.</summary>
        public bool WriteCombinedPerMod = true;

        /// <summary>Automatically expand an entry the moment it is marked broken.</summary>
        public bool AutoExpandBroken = true;

        /// <summary>Extra absolute folders to scan, on top of every enabled mod root.</summary>
        public List<string> ExtraScanPaths = new List<string>();

        /// <summary>Remembered panel position; (-1,-1) means "use the default".</summary>
        public Vector2 WindowPosition = new Vector2(-1f, -1f);

        /// <summary>Remembered panel open/closed state.</summary>
        public bool WindowOpen = false;

        /// <summary>True when the master toggle or scan paths changed and a reload is due.</summary>
        public bool NeedsReload = false;

        public float EffectiveAutoSaveDelay
        {
            get { return Mathf.Clamp(AutoSaveDelaySeconds, 0.25f, 30f); }
        }

        public float EffectiveWindowWidth
        {
            // Keep the panel usable on every resolution RimWorld supports.
            get { return Mathf.Clamp(WindowWidth, 420f, Mathf.Max(420f, UI.screenWidth - 40f)); }
        }

        public float EffectiveMaxContentHeight
        {
            get { return Mathf.Clamp(MaxBlockContentHeight, 60f, 2000f); }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref ShowFloatingButton, "showFloatingButton", true);
            Scribe_Values.Look(ref WindowWidth, "windowWidth", 720f);
            Scribe_Values.Look(ref MaxBlockContentHeight, "maxBlockContentHeight", 220f);
            Scribe_Values.Look(ref AutoSaveDelaySeconds, "autoSaveDelaySeconds", 1.5f);
            Scribe_Values.Look(ref WriteCombinedPerMod, "writeCombinedPerMod", true);
            Scribe_Values.Look(ref AutoExpandBroken, "autoExpandBroken", true);
            Scribe_Collections.Look(ref ExtraScanPaths, "extraScanPaths", LookMode.Value);
            Scribe_Values.Look(ref WindowPosition, "windowPosition", new Vector2(-1f, -1f));
            Scribe_Values.Look(ref WindowOpen, "windowOpen", false);

            if (ExtraScanPaths == null) ExtraScanPaths = new List<string>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) NeedsReload = true;
        }
    }
}
