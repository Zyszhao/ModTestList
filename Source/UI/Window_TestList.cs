// TestList -- the floating, draggable, scrollable checklist panel.
//
// Owned by the UI task (task-3). This class is created by TestListUiHook (the on-screen
// button, or the mod-settings "open panel" button), so it must stay a public class with a
// public parameterless constructor.
// In-game only.
//
// HOW THE SCROLLBAR HEIGHT IS GUARANTEED ON FRAME 1 (the acceptance question):
//   DoWindowContents runs the layout twice every frame, never once:
//     pass 1 -- TestListUiStyle.BeginMeasure() -> BuildLayout(contentWidth) walks every
//               document/entry, asks TestListEntryView.MeasureHeight(...) and stores the
//               resulting absolute Y offset and height per row, returning the total height;
//               nothing is drawn in this pass and no height is cached between frames;
//     pass 2 -- DrawList(...) draws exactly those offsets inside Widgets.BeginScrollView
//               with viewRect.height == the pass-1 total, culling rows outside the viewport.
//   Measure and draw therefore use the SAME width, the SAME frame, the SAME pinned
//   GameFont.Small and the SAME pure height function (TestListEntryView.MeasureBody), so
//   the content height cannot be stale or disagree with the drawn content on any frame,
//   including the first one. There is no dirty flag to forget to set, because nothing is
//   reused between frames.
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TestList
{
    public class Window_TestList : Window
    {
        private const float HeaderRowHeight = 30f;
        private const float ToolbarRowHeight = 28f;
        private const float ProgressRowHeight = 22f;
        private const float FilterRowHeight = 26f;
        private const float FileHeaderRowHeight = 26f;
        private const float ScrollGutter = 20f;
        private const float CollapsedExtraHeight = 6f;
        private const float MinUsableWidth = 140f;
        private const float MinUsableHeight = 100f;
        private const float SaveStateWidth = 150f;
        private const float RowGap = 4f;
        private const float ToolbarButtonHeight = 24f;
        private const float ListBottomPadding = 8f;

        private enum RowKind
        {
            FileHeader,
            Entry
        }

        /// <summary>One laid-out row. Produced by pass 1, consumed by pass 2 of the same frame.</summary>
        private sealed class LayoutRow
        {
            public RowKind Kind;
            public TestDocument Doc;
            public TestEntry Entry;
            public int DocumentIndex;
            public int StripeIndex;
            public int PassCount;
            public int FailCount;
            public int UntestedCount;
            public int VisibleCount;
            public float Y;
            public float Height;
        }

        /// <summary>
        /// Filter/visibility scratch state. Rebuilt from scratch for every document on every
        /// frame (and again on demand for the bulk buttons), so it can never go stale.
        /// </summary>
        private sealed class DocFilter
        {
            private bool[] statusOk = new bool[0];
            private bool[] textMatch = new bool[0];
            private bool[] ancestorTextMatch = new bool[0];
            private int[] parentIndex = new int[0];
            private readonly Dictionary<string, int> indexById = new Dictionary<string, int>(StringComparer.Ordinal);
            private int count;
            private bool hasFilter;

            public int Count
            {
                get { return count; }
            }

            private void EnsureCapacity(int needed)
            {
                if (statusOk.Length >= needed) return;
                statusOk = new bool[needed];
                textMatch = new bool[needed];
                ancestorTextMatch = new bool[needed];
                parentIndex = new int[needed];
            }

            public void Prepare(TestDocument doc, string needle, bool showUntested, bool showPass, bool showFail, bool hideTested)
            {
                List<TestEntry> entries = doc != null ? doc.Entries : null;
                count = entries != null ? entries.Count : 0;
                hasFilter = !needle.NullOrEmpty();
                EnsureCapacity(count);

                indexById.Clear();
                for (int i = 0; i < count; i++)
                {
                    TestEntry entry = entries[i];
                    if (entry == null || entry.Id.NullOrEmpty()) continue;
                    if (!indexById.ContainsKey(entry.Id)) indexById[entry.Id] = i;
                }

                for (int i = 0; i < count; i++)
                {
                    TestEntry entry = entries[i];
                    statusOk[i] = false;
                    textMatch[i] = false;
                    ancestorTextMatch[i] = false;
                    parentIndex[i] = -1;
                    if (entry == null) continue;

                    statusOk[i] = StatusVisible(entry.Status, showUntested, showPass, showFail, hideTested);
                    textMatch[i] = !hasFilter || MatchesText(entry, needle);

                    if (!entry.ParentId.NullOrEmpty())
                    {
                        int parent;
                        if (indexById.TryGetValue(entry.ParentId, out parent)) parentIndex[i] = parent;
                    }
                }

                // ParentId always points at a preceding heading, so one forward pass propagates
                // "a parent matched" down to the children without recursion or memo state.
                for (int i = 0; i < count; i++)
                {
                    int parent = parentIndex[i];
                    if (parent >= 0 && parent < count && (textMatch[parent] || ancestorTextMatch[parent]))
                    {
                        ancestorTextMatch[i] = true;
                    }
                }
            }

            public bool IsVisible(int index)
            {
                if (index < 0 || index >= count) return false;
                if (!statusOk[index]) return false;
                if (textMatch[index]) return true;
                return hasFilter && ancestorTextMatch[index];
            }
        }

        // ------------------------------------------------------------------
        // Filter predicates (shared by DocFilter and the bulk toolbar actions)
        // ------------------------------------------------------------------

        private static bool StatusVisible(TestStatus status, bool showUntested, bool showPass, bool showFail, bool hideTested)
        {
            switch (status)
            {
                case TestStatus.Pass:
                    if (!showPass) return false;
                    break;
                case TestStatus.Fail:
                    if (!showFail) return false;
                    break;
                default:
                    if (!showUntested) return false;
                    break;
            }
            // "Hide tested" means: hide anything that is Pass or Fail.
            if (hideTested && status != TestStatus.Untested) return false;
            return true;
        }

        private static bool MatchesText(TestEntry entry, string needle)
        {
            if (needle.NullOrEmpty()) return true;
            return ContainsIgnoreCase(entry.Title, needle)
                || ContainsIgnoreCase(entry.Path, needle)
                || ContainsIgnoreCase(entry.Content, needle);
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            if (haystack.NullOrEmpty()) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- per-frame layout scratch (contents rebuilt every frame) ----
        private readonly List<LayoutRow> rows = new List<LayoutRow>();
        private readonly DocFilter docFilter = new DocFilter();

        // ---- per-frame bulk targets (only touched on a button click) ----
        private readonly List<TestEntry> bulkEntries = new List<TestEntry>();
        private readonly List<TestDocument> bulkDocs = new List<TestDocument>();

        // ---- persisted UI state ----
        private Vector2 scroll;
        private string filter = "";
        private bool showUntested = true;
        private bool showPass = true;
        private bool showFail = true;
        private bool hideTested;

        private bool bodyCollapsed;
        private bool restoreHeightPending;
        private float collapsedFromHeight = -1f;

        private Vector2 settingsPosition = new Vector2(-1f, -1f);
        private Vector2 lastPersistedPosition = new Vector2(float.NaN, float.NaN);
        private float lastPersistedHeight = -1f;
        private bool positionInitialised;
        private int framesSinceReloadCheck;
        private bool drawFailureLogged;
        private string drawFailureMessage;

        public Window_TestList()
        {
            // `Title` does not exist on Verse.Window in 1.6.4871 (verified against the
            // decompiled source: the field is `optionalTitle`, which would draw a second,
            // vanilla-styled title and shift the content rect). We draw the title ourselves
            // in the header row instead, so optionalTitle stays null.
            optionalTitle = null;

            doWindowBackground = true;
            doCloseX = false;
            doCloseButton = false;
            closeOnCancel = false;
            closeOnAccept = false;   // Enter must stay inside the feedback text areas.
            resizeable = true;
            draggable = true;
            onlyOneOfTypeAllowed = true;
            absorbInputAroundWindow = false;   // the map stays clickable outside the panel
            // 1.6.4871 replaced the old `preventCameraPitch` flag with preventCameraMotion
            // (Window.cs); setting it keeps the camera from pitching while the panel is used.
            preventCameraMotion = true;

            TestListSettings settings = TestListMod.Settings;
            if (settings != null) settingsPosition = settings.WindowPosition;
        }

        public override Vector2 InitialSize
        {
            get
            {
                TestListSettings settings = TestListMod.Settings;
                float width = settings != null ? settings.EffectiveWindowWidth : 720f;
                float height = Mathf.Clamp(UI.screenHeight - 120f, 280f, 760f);
                return new Vector2(width, height);
            }
        }

        // ------------------------------------------------------------------
        // Open / close / position persistence
        // ------------------------------------------------------------------

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            ApplySettingsPosition();
        }

        public override void PreOpen()
        {
            base.PreOpen();
            TestListSettings settings = TestListMod.Settings;
            if (settings != null)
            {
                settingsPosition = settings.WindowPosition;
                settings.WindowOpen = true;
            }
        }

        public override void PreClose()
        {
            base.PreClose();

            TestListSettings settings = TestListMod.Settings;
            if (settings != null)
            {
                settings.WindowPosition = new Vector2(windowRect.x, windowRect.y);
                settings.WindowOpen = false;
                lastPersistedPosition = settings.WindowPosition;
                lastPersistedHeight = windowRect.height;
            }

            // Do not let closing the panel drop edits that are still inside the auto-save
            // debounce window.
            try
            {
                TestListManager.SaveNow();
            }
            catch (Exception ex)
            {
                TestListLog.Exception("Failed to flush feedback while closing the checklist panel", ex);
            }
        }

        protected override void LateWindowOnGUI(Rect inRect)
        {
            base.LateWindowOnGUI(inRect);
            PersistPositionIfChanged();
        }

        private void ApplySettingsPosition()
        {
            float width = windowRect.width;
            float height = windowRect.height;

            float x;
            float y;
            if (settingsPosition.x < 0f && settingsPosition.y < 0f)
            {
                // (-1,-1) sentinel: default = vertically centred, left of centre horizontally.
                x = UI.screenWidth * 0.25f - width * 0.5f;
                y = (UI.screenHeight - height) * 0.5f;
            }
            else
            {
                x = settingsPosition.x;
                y = settingsPosition.y;
            }

            windowRect.x = ClampToScreenX(x, width);
            windowRect.y = ClampToScreenY(y, height);
            lastPersistedPosition = new Vector2(windowRect.x, windowRect.y);
            lastPersistedHeight = windowRect.height;
            positionInitialised = true;
        }

        private void PersistPositionIfChanged()
        {
            TestListSettings settings = TestListMod.Settings;
            if (settings == null) return;
            if (Event.current == null) return;

            EventType type = Event.current.type;
            if (type != EventType.MouseDrag && type != EventType.MouseUp && type != EventType.Repaint) return;

            Vector2 position = new Vector2(windowRect.x, windowRect.y);
            if (positionInitialised
                && position.x == lastPersistedPosition.x
                && position.y == lastPersistedPosition.y
                && windowRect.height == lastPersistedHeight)
            {
                return;
            }

            positionInitialised = true;
            lastPersistedPosition = position;
            lastPersistedHeight = windowRect.height;
            settings.WindowPosition = position;
        }

        private static float ClampToScreenX(float x, float width)
        {
            float max = Mathf.Max(4f, UI.screenWidth - width - 4f);
            return Mathf.Clamp(x, 4f, max);
        }

        private static float ClampToScreenY(float y, float height)
        {
            float max = Mathf.Max(4f, UI.screenHeight - height - 4f);
            return Mathf.Clamp(y, 4f, max);
        }

        // ------------------------------------------------------------------
        // Frame
        // ------------------------------------------------------------------

        public override void DoWindowContents(Rect inRect)
        {
            if (drawFailureLogged)
            {
                DrawFailureNotice(inRect);
                return;
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWrap = Text.WordWrap;
            try
            {
                TestListUiStyle.BeginPanelText();
                TestListManager.EnsureInitialized();
                HandlePeriodicReload();

                float y = 0f;
                DrawHeaderRow(new Rect(0f, y, inRect.width, HeaderRowHeight));
                y += HeaderRowHeight;

                DrawToolbarRow(new Rect(0f, y, inRect.width, ToolbarRowHeight));
                y += ToolbarRowHeight;

                DrawProgressRow(new Rect(0f, y, inRect.width, ProgressRowHeight));
                y += ProgressRowHeight;

                if (bodyCollapsed)
                {
                    ApplyCollapsedSizing();
                    return;
                }
                RestoreExpandedSizing();

                if (inRect.width < MinUsableWidth || inRect.height < y + MinUsableHeight)
                {
                    return;
                }

                DrawFilterRow(new Rect(0f, y, inRect.width, FilterRowHeight));
                y += FilterRowHeight;

                Rect listRect = new Rect(0f, y, inRect.width, Mathf.Max(24f, inRect.height - y));
                float contentWidth = Mathf.Max(120f, inRect.width - ScrollGutter);

                // PASS 1: measure only. Nothing is drawn, no height survives this frame.
                float contentHeight;
                TestListUiStyle.BeginMeasure();
                try
                {
                    contentHeight = BuildLayout(contentWidth);
                }
                finally
                {
                    TestListUiStyle.EndMeasure();
                }

                // PASS 2: draw at exactly the offsets pass 1 computed.
                DrawList(listRect, contentWidth, contentHeight);
            }
            catch (Exception ex)
            {
                // Never throw out of OnGUI every frame: Window.InnerWindowOnGUI would log the
                // full stack each Repaint forever. Log once and degrade to a notice.
                drawFailureLogged = true;
                drawFailureMessage = ex.Message;
                TestListLog.Exception("Checklist panel OnGUI failed; the panel is disabled until retried.", ex);
                try
                {
                    DrawFailureNotice(inRect);
                }
                catch (Exception)
                {
                    // Drawing the notice itself failed; nothing sensible left to do.
                }
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWrap;
            }
        }

        private void HandlePeriodicReload()
        {
            framesSinceReloadCheck++;
            if (framesSinceReloadCheck < 60) return;
            framesSinceReloadCheck = 0;

            TestListSettings settings = TestListManager.Settings;
            if (settings == null || !settings.NeedsReload) return;

            settings.NeedsReload = false;
            TestListManager.Load();
        }

        // ------------------------------------------------------------------
        // Rows 1-3: header, toolbar, progress
        // ------------------------------------------------------------------

        private void DrawHeaderRow(Rect row)
        {
            TestListUiStyle.Fill(row, TestListUiStyle.PanelBgAlt);

            float cursor = row.x + 2f;
            Rect chevron = new Rect(cursor, row.y + 4f, 24f, 22f);
            if (Widgets.ButtonText(chevron, bodyCollapsed ? ">" : "v"))
            {
                ToggleBodyCollapsed();
            }
            TooltipHandler.TipRegion(chevron, bodyCollapsed ? TestListText.ExpandAll : TestListText.CollapseAll);
            cursor += 28f;

            Rect close = new Rect(row.xMax - 26f, row.y + 4f, 22f, 22f);
            float titleWidth = Mathf.Max(40f, close.x - cursor - 6f);
            TestListUiStyle.DrawColoredLabel(
                new Rect(cursor, row.y, titleWidth, row.height),
                TestListUiStyle.Fit(TestListText.WindowTitle, titleWidth),
                Color.white);

            if (Widgets.ButtonText(close, "X"))
            {
                Close();
            }
            TooltipHandler.TipRegion(close, TestListText.CloseWindowTip);
        }

        private void DrawToolbarRow(Rect row)
        {
            TestListUiStyle.Fill(row, TestListUiStyle.PanelBg);

            float y = row.y + 2f;
            float saveWidth = Mathf.Min(SaveStateWidth, row.width * 0.35f);
            float available = Mathf.Max(80f, row.width - saveWidth - RowGap);
            float[] weights = { 0.9f, 1.25f, 1.3f, 1.7f, 1.55f };
            float weightSum = 0f;
            for (int i = 0; i < weights.Length; i++) weightSum += weights[i];
            float scale = Mathf.Max(30f, (available - RowGap * (weights.Length - 1)) / weightSum);

            float cursor = row.x;
            if (ToolbarButton(new Rect(cursor, y, scale * weights[0], ToolbarButtonHeight), TestListText.Reload, TestListText.ReloadTip))
            {
                TestListManager.Load();
            }
            cursor += scale * weights[0] + RowGap;

            if (ToolbarButton(new Rect(cursor, y, scale * weights[1], ToolbarButtonHeight), TestListText.ExpandAll, null))
            {
                SetEverythingExpanded(true);
            }
            cursor += scale * weights[1] + RowGap;

            if (ToolbarButton(new Rect(cursor, y, scale * weights[2], ToolbarButtonHeight), TestListText.CollapseAll, null))
            {
                SetEverythingExpanded(false);
            }
            cursor += scale * weights[2] + RowGap;

            if (ToolbarButton(new Rect(cursor, y, scale * weights[3], ToolbarButtonHeight), TestListText.MarkAllPass, TestListText.MarkPrompt + " " + TestListText.StatusPassLong))
            {
                MarkVisibleAs(TestStatus.Pass);
            }
            cursor += scale * weights[3] + RowGap;

            if (ToolbarButton(new Rect(cursor, y, scale * weights[4], ToolbarButtonHeight), TestListText.ClearAll, null))
            {
                TestListManager.ClearAllMarks();
            }

            Rect saveRect = new Rect(row.xMax - saveWidth, row.y, saveWidth, row.height);
            TestListUiStyle.DrawLabelAligned(
                saveRect,
                TestListUiStyle.Fit(SaveStateText(), saveWidth),
                SaveStateColor(),
                TextAnchor.MiddleRight);
        }

        private static bool ToolbarButton(Rect rect, string label, string tooltip)
        {
            bool clicked = Widgets.ButtonText(rect, TestListUiStyle.Fit(label, rect.width - 6f));
            if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(rect, tooltip);
            return clicked;
        }

        private static string SaveStateText()
        {
            if (TestListManager.SaveState == TestListSaveState.Error) return TestListText.SaveStateError;
            if (TestListManager.SaveState == TestListSaveState.Pending || TestListManager.HasUnsavedChanges) return TestListText.SaveStatePending;
            return TestListText.SaveStateIdle;
        }

        private static Color SaveStateColor()
        {
            if (TestListManager.SaveState == TestListSaveState.Error) return TestListUiStyle.FailColor;
            if (TestListManager.SaveState == TestListSaveState.Pending || TestListManager.HasUnsavedChanges) return TestListUiStyle.UntestedColor;
            return TestListUiStyle.DimmedText;
        }

        private void DrawProgressRow(Rect row)
        {
            int total;
            int pass;
            int fail;
            int untested;
            TestListManager.CountStatuses(out total, out pass, out fail, out untested);
            int tested = Mathf.Max(0, total - untested);

            string format = TestListText.ProgressFormat;
            const string failSlot = "{3}";
            int slot = format.IndexOf(failSlot, StringComparison.Ordinal);
            if (slot < 0)
            {
                TestListUiStyle.DrawColoredLabel(row, string.Format(format, tested, total, pass, fail, untested), Color.white);
                return;
            }

            // Render once with a one-character sentinel in the broken slot, then split the real
            // rendered text on it. Only the broken count is drawn red; the prefix/suffix are the
            // exact characters string.Format produced, so the line looks like a single label.
            const char sentinel = '\u0001';
            string marked = string.Format(format, tested, total, pass, sentinel.ToString(), untested);
            int at = marked.IndexOf(sentinel);
            if (at < 0)
            {
                TestListUiStyle.DrawColoredLabel(row, string.Format(format, tested, total, pass, fail, untested), Color.white);
                return;
            }

            string prefix = marked.Substring(0, at);
            string suffix = marked.Substring(at + 1);
            string failToken = fail.ToString();

            float x = row.x;
            float prefixWidth = TestListUiStyle.MeasureTextWidth(prefix);
            float failWidth = TestListUiStyle.MeasureTextWidth(failToken);

            TestListUiStyle.DrawColoredLabel(new Rect(x, row.y, prefixWidth + 2f, row.height), prefix, Color.white);
            x += prefixWidth;
            TestListUiStyle.DrawColoredLabel(new Rect(x, row.y, failWidth + 4f, row.height), failToken, TestListUiStyle.FailColor);
            x += failWidth;
            TestListUiStyle.DrawColoredLabel(new Rect(x, row.y, Mathf.Max(10f, row.xMax - x), row.height), suffix, Color.white);
        }

        // ------------------------------------------------------------------
        // Row 4: filter + chips
        // ------------------------------------------------------------------

        private void DrawFilterRow(Rect row)
        {
            float y = row.y + 2f;
            float height = 22f;
            float cursor = row.x;

            float labelWidth = 46f;
            TestListUiStyle.DrawColoredLabel(new Rect(cursor, row.y, labelWidth, row.height), TestListText.FilterLabel, Color.white);
            cursor += labelWidth;

            float fieldWidth = Mathf.Clamp(row.width * 0.28f, 110f, 220f);
            Rect field = new Rect(cursor, y, fieldWidth, height);
            string edited;
            GameFont previousFont = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                GUI.SetNextControlName("TestList_filter");
                edited = Widgets.TextField(field, filter ?? "");
            }
            finally
            {
                Text.Font = previousFont;
            }
            TooltipHandler.TipRegion(field, TestListText.FilterTip);
            if (!string.Equals(edited, filter, StringComparison.Ordinal))
            {
                filter = edited;
            }
            cursor += fieldWidth + 6f;

            float chipsWidth = Mathf.Max(40f, row.xMax - cursor);
            float chipWidth = Mathf.Max(46f, (chipsWidth - RowGap * 3f) / 4f);
            Chip(new Rect(cursor, y, chipWidth, height), TestListText.ShowUntested, ref showUntested, TestListUiStyle.UntestedColor);
            cursor += chipWidth + RowGap;
            Chip(new Rect(cursor, y, chipWidth, height), TestListText.ShowPass, ref showPass, TestListUiStyle.PassColor);
            cursor += chipWidth + RowGap;
            Chip(new Rect(cursor, y, chipWidth, height), TestListText.ShowFail, ref showFail, TestListUiStyle.FailColor);
            cursor += chipWidth + RowGap;
            Chip(new Rect(cursor, y, chipWidth, height), TestListText.HideTested, ref hideTested, TestListUiStyle.FailColor);
        }

        private static void Chip(Rect rect, string label, ref bool value, Color onColor)
        {
            Color color = value ? onColor : TestListUiStyle.DimmedText;
            if (Widgets.ButtonText(rect, TestListUiStyle.Fit(label, rect.width - 6f), true, true, color, true, null))
            {
                value = !value;
            }
        }

        // ------------------------------------------------------------------
        // Row 5: the virtualised list
        // ------------------------------------------------------------------

        /// <summary>
        /// Pass 1. Fills <see cref="rows"/> and returns the total content height.
        /// Pure measuring: no GUI calls, no drawing, no cross-frame caching.
        /// </summary>
        private float BuildLayout(float width)
        {
            rows.Clear();

            TestListData data = TestListManager.Data;
            string needle = (filter ?? "").Trim();
            bool hasFilter = needle.Length > 0;
            float y = 0f;
            int stripe = 0;

            for (int i = 0; i < data.Documents.Count; i++)
            {
                TestDocument doc = data.Documents[i];
                if (doc == null) continue;

                docFilter.Prepare(doc, needle, showUntested, showPass, showFail, hideTested);

                int visible = 0;
                int pass = 0;
                int fail = 0;
                int untested = 0;
                for (int j = 0; j < doc.Entries.Count; j++)
                {
                    TestEntry entry = doc.Entries[j];
                    if (entry == null) continue;
                    switch (entry.Status)
                    {
                        case TestStatus.Pass: pass++; break;
                        case TestStatus.Fail: fail++; break;
                        default: untested++; break;
                    }
                    if (docFilter.IsVisible(j)) visible++;
                }

                if (visible == 0 && hasFilter) continue;

                bool folded = doc.Collapsed && !hasFilter;

                LayoutRow header = new LayoutRow();
                header.Kind = RowKind.FileHeader;
                header.Doc = doc;
                header.DocumentIndex = i;
                header.PassCount = pass;
                header.FailCount = fail;
                header.UntestedCount = untested;
                header.VisibleCount = visible;
                header.Y = y;
                header.Height = FileHeaderRowHeight;
                rows.Add(header);
                y += FileHeaderRowHeight;

                if (folded) continue;

                for (int j = 0; j < doc.Entries.Count; j++)
                {
                    TestEntry entry = doc.Entries[j];
                    if (entry == null || !docFilter.IsVisible(j)) continue;

                    bool expanded = TestListEntryView.IsExpanded(doc, entry);
                    float height = TestListEntryView.MeasureHeight(doc, entry, width, expanded);

                    LayoutRow row = new LayoutRow();
                    row.Kind = RowKind.Entry;
                    row.Doc = doc;
                    row.Entry = entry;
                    row.DocumentIndex = i;
                    row.StripeIndex = stripe++;
                    row.Y = y;
                    row.Height = height;
                    rows.Add(row);
                    y += height;
                }
            }

            return y + ListBottomPadding;
        }

        /// <summary>Pass 2. Draws the rows pass 1 measured, culled against the viewport.</summary>
        private void DrawList(Rect listRect, float contentWidth, float contentHeight)
        {
            if (rows.Count == 0)
            {
                DrawEmptyNotice(listRect);
                return;
            }

            Rect viewRect = new Rect(0f, 0f, contentWidth, Mathf.Max(contentHeight, listRect.height + 1f));
            Vector2 position = scroll;

            Widgets.BeginScrollView(listRect, ref position, viewRect, true);
            try
            {
                scroll = position;

                // Virtualisation: only rows intersecting the visible band are drawn.
                float top = scroll.y - 4f;
                float bottom = scroll.y + listRect.height + 4f;

                for (int i = 0; i < rows.Count; i++)
                {
                    LayoutRow row = rows[i];
                    if (row.Y + row.Height < top) continue;
                    if (row.Y > bottom) continue;

                    if (row.Kind == RowKind.FileHeader)
                    {
                        DrawFileHeaderRow(row, contentWidth);
                    }
                    else
                    {
                        TestListEntryView.Draw(
                            new Rect(0f, row.Y, contentWidth, row.Height),
                            row.Doc,
                            row.Entry,
                            contentWidth,
                            TestListEntryView.IsExpanded(row.Doc, row.Entry),
                            row.StripeIndex);
                    }
                }
            }
            finally
            {
                // Always balanced, even if an entry threw mid-draw.
                Widgets.EndScrollView();
            }
        }

        private void DrawFileHeaderRow(LayoutRow row, float width)
        {
            TestDocument doc = row.Doc;
            Rect rect = new Rect(0f, row.Y, width, row.Height);
            TestListUiStyle.Fill(rect, TestListUiStyle.PanelBgAlt);
            Widgets.DrawHighlightIfMouseover(rect);

            float cursor = rect.x + 4f;
            TestListUiStyle.DrawLabelAligned(
                new Rect(cursor, rect.y, 16f, rect.height),
                doc.Collapsed ? ">" : "v",
                TestListUiStyle.UntestedColor,
                TextAnchor.MiddleCenter);
            cursor += 18f;

            string name = doc.FileName;
            if (name.NullOrEmpty()) name = doc.FileBaseName;
            if (name.NullOrEmpty()) name = "(unnamed)";
            float nameWidth = Mathf.Max(60f, rect.width * 0.34f);
            TestListUiStyle.DrawColoredLabel(new Rect(cursor, rect.y, nameWidth, rect.height), TestListUiStyle.Fit(name, nameWidth), Color.white);
            cursor += nameWidth + 6f;

            string badge;
            Color badgeColor;
            if (!doc.ParseError.NullOrEmpty())
            {
                badge = string.Format(TestListText.ParseErrorFormat, doc.ParseError);
                badgeColor = TestListUiStyle.FailColor;
            }
            else
            {
                badge = string.Format(TestListText.SummaryFormat, row.PassCount, row.FailCount, row.UntestedCount);
                badgeColor = TestListUiStyle.DimmedText;
            }
            float badgeWidth = Mathf.Max(60f, rect.width * 0.3f);
            TestListUiStyle.DrawColoredLabel(new Rect(cursor, rect.y, badgeWidth, rect.height), TestListUiStyle.Fit(badge, badgeWidth), badgeColor);
            cursor += badgeWidth + 6f;

            TestListUiStyle.DrawLabelAligned(
                new Rect(cursor, rect.y, 76f, rect.height),
                doc.IsXml ? TestListText.SourceXml : TestListText.SourceMd,
                TestListUiStyle.DimmedText,
                TextAnchor.MiddleLeft);
            cursor += 80f;

            float modWidth = Mathf.Max(40f, rect.xMax - cursor - 6f);
            TestListUiStyle.DrawLabelAligned(
                new Rect(cursor, rect.y, modWidth, rect.height),
                TestListUiStyle.Fit(doc.ModName ?? "", modWidth),
                TestListUiStyle.PassColor,
                TextAnchor.MiddleRight);

            if (Widgets.ButtonInvisible(rect))
            {
                doc.Collapsed = !doc.Collapsed;
                if (Event.current != null) Event.current.Use();
            }
        }

        private void DrawEmptyNotice(Rect rect)
        {
            TestListData data = TestListManager.Data;
            float width = Mathf.Max(40f, rect.width - 12f);
            float y = rect.y + 8f;
            float lineHeight = 22f;

            string headline;
            if (data.IsEmpty) headline = TestListText.NoDocuments;
            else if (!TestListManager.LastError.NullOrEmpty()) headline = string.Format(TestListText.ParseErrorFormat, TestListManager.LastError);
            else headline = TestListText.FilterTip;

            TestListUiStyle.DrawColoredLabel(
                new Rect(rect.x + 6f, y, width, lineHeight),
                TestListUiStyle.Fit(headline, width),
                data.IsEmpty ? TestListUiStyle.UntestedColor : TestListUiStyle.FailColor);
            y += lineHeight + 4f;

            if (data.IsEmpty)
            {
                TestListUiStyle.DrawColoredLabel(
                    new Rect(rect.x + 6f, y, width, lineHeight),
                    TestListUiStyle.Fit(TestListText.NoDocumentsHint, width),
                    TestListUiStyle.DimmedText);
            }
        }

        private void DrawFailureNotice(Rect inRect)
        {
            float width = Mathf.Max(40f, inRect.width - 12f);
            TestListUiStyle.DrawColoredLabel(
                new Rect(4f, 4f, width, 24f),
                TestListUiStyle.Fit(string.Format(TestListText.ParseErrorFormat, drawFailureMessage ?? ""), width),
                TestListUiStyle.FailColor);

            if (Widgets.ButtonText(new Rect(4f, 32f, 120f, 24f), TestListText.Reload))
            {
                drawFailureLogged = false;
                drawFailureMessage = null;
            }
        }

        // ------------------------------------------------------------------
        // Collapsed sizing
        // ------------------------------------------------------------------

        private void ToggleBodyCollapsed()
        {
            bodyCollapsed = !bodyCollapsed;
            if (bodyCollapsed) collapsedFromHeight = windowRect.height;
            else restoreHeightPending = true;
        }

        private float CollapsedWindowHeight
        {
            get { return HeaderRowHeight + ToolbarRowHeight + ProgressRowHeight + Margin * 2f + CollapsedExtraHeight; }
        }

        private void ApplyCollapsedSizing()
        {
            float target = CollapsedWindowHeight;
            if (windowRect.height != target)
            {
                windowRect = new Rect(windowRect.x, windowRect.y, windowRect.width, target);
            }
        }

        private void RestoreExpandedSizing()
        {
            if (!restoreHeightPending) return;
            restoreHeightPending = false;

            float minimum = HeaderRowHeight + ToolbarRowHeight + ProgressRowHeight + FilterRowHeight + MinUsableHeight + Margin * 2f;
            float target = collapsedFromHeight > 0f ? collapsedFromHeight : InitialSize.y;
            target = Mathf.Clamp(target, minimum, Mathf.Max(minimum, UI.screenHeight - 40f));
            windowRect = new Rect(windowRect.x, windowRect.y, windowRect.width, target);
        }

        // ------------------------------------------------------------------
        // Toolbar actions
        // ------------------------------------------------------------------

        private void SetEverythingExpanded(bool expanded)
        {
            TestListEntryView.SetAllExpanded(TestListManager.Data, expanded);

            TestListData data = TestListManager.Data;
            for (int i = 0; i < data.Documents.Count; i++)
            {
                TestDocument doc = data.Documents[i];
                if (doc != null) doc.Collapsed = !expanded;
            }
            scroll = Vector2.zero;
        }

        /// <summary>Bulk-marks every entry that currently passes the filter (fold state ignored).</summary>
        private void MarkVisibleAs(TestStatus status)
        {
            bulkEntries.Clear();
            bulkDocs.Clear();

            TestListData data = TestListManager.Data;
            string needle = (filter ?? "").Trim();

            for (int i = 0; i < data.Documents.Count; i++)
            {
                TestDocument doc = data.Documents[i];
                if (doc == null) continue;

                docFilter.Prepare(doc, needle, showUntested, showPass, showFail, hideTested);
                for (int j = 0; j < doc.Entries.Count; j++)
                {
                    if (!docFilter.IsVisible(j)) continue;
                    bulkEntries.Add(doc.Entries[j]);
                    bulkDocs.Add(doc);
                }
            }

            TestListManager.SetStatusBulk(bulkEntries, bulkDocs, status);
        }
    }
}
