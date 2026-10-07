// TestList -- settings dialog body.
//
// Owned by the Lead. Split out of TestListMod.cs so the Mod subclass stays tiny.
using System.Collections.Generic;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace TestList
{
    internal static class TestListSettingsUI
    {
        private static string extraPathsBuffer;
        private static Vector2 extraPathsScroll = Vector2.zero;

        internal static void Draw(Rect inRect, TestListSettings settings)
        {
            if (settings == null) return;

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            bool enabled = settings.Enabled;
            listing.CheckboxLabeled(TestListText.SettingEnable, ref enabled);
            if (enabled != settings.Enabled)
            {
                settings.Enabled = enabled;
                settings.NeedsReload = true;
            }

            listing.CheckboxLabeled(TestListText.SettingShowButton, ref settings.ShowFloatingButton);
            listing.CheckboxLabeled(TestListText.SettingWriteShared, ref settings.WriteCombinedPerMod);
            listing.CheckboxLabeled(TestListText.SettingAutoExpandFirst, ref settings.AutoExpandBroken);

            listing.GapLine();

            listing.Label(TestListText.SettingWindowWidth + ": " + Mathf.RoundToInt(settings.WindowWidth) + " px");
            settings.WindowWidth = listing.Slider(settings.WindowWidth, 420f, 1400f);

            listing.Label(TestListText.SettingMaxBlockHeight + ": " + Mathf.RoundToInt(settings.MaxBlockContentHeight) + " px");
            settings.MaxBlockContentHeight = listing.Slider(settings.MaxBlockContentHeight, 60f, 800f);

            listing.Label(TestListText.SettingAutoSaveDelay + ": " + settings.AutoSaveDelaySeconds.ToString("0.0") + " s");
            settings.AutoSaveDelaySeconds = listing.Slider(settings.AutoSaveDelaySeconds, 0.25f, 15f);

            listing.GapLine();

            listing.Label(TestListText.SettingExtraPaths);
            Rect pathRect = listing.GetRect(96f);
            extraPathsScroll = GUI.BeginScrollView(pathRect, extraPathsScroll, new Rect(0f, 0f, pathRect.width - 20f, 200f));
            if (extraPathsBuffer == null)
            {
                extraPathsBuffer = settings.ExtraScanPaths == null
                    ? ""
                    : string.Join("\n", settings.ExtraScanPaths.ToArray());
            }
            string edited = GUI.TextArea(new Rect(0f, 0f, pathRect.width - 20f, 200f), extraPathsBuffer);
            GUI.EndScrollView();
            if (!string.Equals(edited, extraPathsBuffer, System.StringComparison.Ordinal))
            {
                extraPathsBuffer = edited;
                ApplyExtraPaths(settings);
            }

            listing.GapLine();

            // A guaranteed second way in. If the on-screen button is ever hidden, broken or
            // scrolled off, the player can always reach the panel from the mod settings page.
            if (listing.ButtonText(TestListText.SettingsOpenPanel))
            {
                if (Verse.Current.ProgramState == Verse.ProgramState.Playing)
                {
                    TestListUiHook.OpenPanel();
                }
                else
                {
                    Messages.Message(TestListText.SettingsPanelOnlyInGame, MessageTypeDefOf.RejectInput, false);
                }
            }

            if (listing.ButtonText(TestListText.Reload))
            {
                extraPathsBuffer = null;
                TestListManager.Load();
            }

            if (TestListManager.HasLoaded)
            {
                int total, pass, fail, untested;
                TestListManager.CountStatuses(out total, out pass, out fail, out untested);
                listing.Label(string.Format(TestListText.ProgressFormat, total - untested, total, pass, fail, untested));
                for (int i = 0; i < TestListManager.Data.ScannedModNames.Count; i++)
                {
                    listing.Label("  - " + TestListManager.Data.ScannedModNames[i]);
                }
                if (TestListManager.Data.ScannedModNames.Count == 0)
                {
                    listing.Label("  " + TestListText.NoDocuments);
                }
            }

            listing.End();
        }

        private static void ApplyExtraPaths(TestListSettings settings)
        {
            if (settings == null) return;
            List<string> paths = new List<string>();
            string[] lines = (extraPathsBuffer ?? "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim().Trim('"');
                if (line.Length == 0) continue;
                try
                {
                    if (Directory.Exists(line)) paths.Add(line);
                }
                catch
                {
                    // Malformed path text is ignored while the player is still typing.
                }
            }
            settings.ExtraScanPaths = paths;
            settings.NeedsReload = true;
        }
    }
}
