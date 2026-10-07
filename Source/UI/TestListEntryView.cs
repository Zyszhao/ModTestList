// TestList -- one checklist entry: title row, marker bar, and (when expanded) the body.
//
// Owned by the UI task (task-3). In-game only.
//
// HEIGHT CONTRACT: MeasureHeight() and Draw() both funnel through MeasureBody(), and
// MeasureBody() is a pure function of (entry text, content width, font). The window calls
// MeasureHeight() in its measure pass and Draw() in its draw pass *within the same frame*,
// with the same width and the same pinned GameFont.Small, so the scroll view's content
// height can never disagree with what is actually drawn.
//
// The only state kept between frames is the set of expanded entry ids and the per-entry
// content scroll offsets -- never a measured height.
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TestList
{
    public static class TestListEntryView
    {
        public const float TitleRowHeight = 24f;
        public const float MarkerBarHeight = TestListStatusBar.Height;
        public const float TitleIndentPerLevel = 14f;

        private const float BodyPadding = 6f;
        private const float BodyLeftPadding = 10f;
        private const float MetaHeight = 18f;
        private const float ContentGap = 4f;
        private const float MinFeedbackEditHeight = 56f;
        private const float FeedbackLabelWidth = 74f;
        private const float InnerScrollbarGutter = 18f;

        private static readonly Color TitleColor = new Color(0.94f, 0.94f, 0.96f, 1f);
        private static readonly Color BodyColor = new Color(0.86f, 0.87f, 0.89f, 1f);

        /// <summary>Ids of the entries the player expanded. The only cross-frame UI cache.</summary>
        private static readonly HashSet<string> expandedIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Per-entry scroll offset of the body's own scroll view.</summary>
        private static readonly Dictionary<string, Vector2> contentScroll = new Dictionary<string, Vector2>(StringComparer.Ordinal);

        // ------------------------------------------------------------------
        // Expansion state
        // ------------------------------------------------------------------

        public static string Key(TestDocument doc, TestEntry entry)
        {
            if (entry == null) return "";
            if (!entry.Id.NullOrEmpty()) return entry.Id;
            return (doc != null ? doc.FileBaseName : "") + "|" + entry.SourceLine + "|" + (entry.RawHeading ?? entry.Title ?? "");
        }

        public static bool IsExpanded(TestDocument doc, TestEntry entry)
        {
            string key = Key(doc, entry);
            return key.Length != 0 && expandedIds.Contains(key);
        }

        public static void SetExpanded(TestDocument doc, TestEntry entry, bool expanded)
        {
            string key = Key(doc, entry);
            if (key.Length == 0) return;
            if (expanded) expandedIds.Add(key);
            else
            {
                expandedIds.Remove(key);
                contentScroll.Remove(key);
            }
        }

        public static void ToggleExpanded(TestDocument doc, TestEntry entry)
        {
            SetExpanded(doc, entry, !IsExpanded(doc, entry));
        }

        /// <summary>Expand/collapse every entry of every document.</summary>
        public static void SetAllExpanded(TestListData data, bool expanded)
        {
            if (!expanded)
            {
                expandedIds.Clear();
                contentScroll.Clear();
                return;
            }
            if (data == null) return;
            for (int i = 0; i < data.Documents.Count; i++)
            {
                TestDocument doc = data.Documents[i];
                if (doc == null) continue;
                for (int j = 0; j < doc.Entries.Count; j++)
                {
                    string key = Key(doc, doc.Entries[j]);
                    if (key.Length != 0) expandedIds.Add(key);
                }
            }
        }

        // ------------------------------------------------------------------
        // Measuring
        // ------------------------------------------------------------------

        /// <summary>Height of the full entry block at the given content width.</summary>
        public static float MeasureHeight(TestDocument doc, TestEntry entry, float width, bool expanded)
        {
            float height = TitleRowHeight + MarkerBarHeight;
            if (expanded) height += MeasureBody(entry, width).TotalHeight;
            return height;
        }

        /// <summary>Height of the editable feedback area: at least three lines (>= 56 px).</summary>
        public static float FeedbackEditHeight
        {
            get { return Mathf.Max(MinFeedbackEditHeight, TestListUiStyle.SmallLineHeight * 3f + 6f); }
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        /// <summary>
        /// Draws the block whose top-left is <paramref name="rect"/>.Position and whose height
        /// must be MeasureHeight(...). The caller guarantees the rect is inside the visible
        /// scroll region (virtualisation lives in Window_TestList).
        /// </summary>
        public static void Draw(Rect rect, TestDocument doc, TestEntry entry, float width, bool expanded, int rowIndex)
        {
            if (entry == null) return;

            float height = MeasureHeight(doc, entry, width, expanded);
            TestListUiStyle.DrawRowBackground(new Rect(rect.x, rect.y, width, height), rowIndex);            TestListUiStyle.Fill(new Rect(rect.x, rect.y, 2f, height), TestListUiStyle.ColorForStatus(entry.Status));

            float y = rect.y;
            DrawTitleRow(rect.x, y, width, doc, entry, expanded);
            y += TitleRowHeight;

            float indent = Mathf.Max(0, entry.Level - 1) * TitleIndentPerLevel;
            float barX = rect.x + BodyLeftPadding + indent;
            float barWidth = Mathf.Max(60f, rect.xMax - barX - BodyLeftPadding);
            TestStatus picked;
            if (TestListStatusBar.Draw(new Rect(barX, y, barWidth, MarkerBarHeight), entry.Status, out picked))
            {
                ApplyStatus(doc, entry, picked);
            }
            y += MarkerBarHeight;

            if (!expanded) return;

            BodyLayout layout = MeasureBody(entry, width);
            float innerX = rect.x + BodyLeftPadding;
            float innerWidth = Mathf.Max(40f, width - BodyLeftPadding * 2f);
            y += BodyPadding;

            if (layout.ContentRegionHeight > 0f)
            {
                DrawContent(new Rect(innerX, y, innerWidth, layout.ContentRegionHeight), doc, entry, layout);
                y += layout.ContentRegionHeight + ContentGap;
            }

            string meta = BuildMetaText(entry);
            TestListUiStyle.DrawColoredLabel(
                new Rect(innerX, y, innerWidth, MetaHeight),
                TestListUiStyle.Fit(meta, innerWidth),
                TestListUiStyle.DimmedText);
            y += MetaHeight;

            DrawFeedback(new Rect(innerX, y, innerWidth, layout.FeedbackEditHeight), doc, entry);
        }

        // ------------------------------------------------------------------
        // Parts
        // ------------------------------------------------------------------

        private static void DrawTitleRow(float x, float y, float width, TestDocument doc, TestEntry entry, bool expanded)
        {
            Rect row = new Rect(x, y, width, TitleRowHeight);
            Widgets.DrawHighlightIfMouseover(row);

            float indent = Mathf.Max(0, entry.Level - 1) * TitleIndentPerLevel;
            float cursor = x + 4f + indent;

            TestListUiStyle.DrawLabelAligned(
                new Rect(cursor, y, 16f, TitleRowHeight),
                expanded ? "v" : ">",
                TestListUiStyle.DimmedText,
                TextAnchor.MiddleCenter);
            cursor += 18f;

            TestListUiStyle.DrawLabelAligned(
                new Rect(cursor, y, 34f, TitleRowHeight),
                TestStatusUtil.Badge(entry.Status),
                TestListUiStyle.ColorForStatus(entry.Status),
                TextAnchor.MiddleLeft);
            cursor += 36f;

            float titleWidth = Mathf.Max(24f, x + width - cursor - 4f);
            string title = entry.Title.NullOrEmpty() ? (entry.RawHeading ?? "") : entry.Title;
            TestListUiStyle.DrawColoredLabel(
                new Rect(cursor, y, titleWidth, TitleRowHeight),
                TestListUiStyle.Fit(title, titleWidth),
                TitleColor);

            // Whole row is the hit target; GUI.Button consumes the mouse event, so the click
            // cannot reach the map behind the panel.
            if (Widgets.ButtonInvisible(row))
            {
                ToggleExpanded(doc, entry);
                if (Event.current != null) Event.current.Use();
            }
        }

        private static void DrawContent(Rect region, TestDocument doc, TestEntry entry, BodyLayout layout)
        {
            string content = entry.Content ?? "";
            if (content.Length == 0) return;

            string key = Key(doc, entry);
            if (!layout.ContentScrolls)
            {
                TestListUiStyle.DrawColoredLabel(
                    new Rect(region.x, region.y, region.width, layout.ContentMeasuredHeight),
                    content,
                    BodyColor);
                return;
            }

            float innerWidth = Mathf.Max(40f, region.width - InnerScrollbarGutter);
            float contentHeight = layout.ContentMeasuredHeight + 2f;
            Vector2 position;
            if (!contentScroll.TryGetValue(key, out position)) position = Vector2.zero;

            Widgets.BeginScrollView(region, ref position, new Rect(0f, 0f, innerWidth, contentHeight), true);
            try
            {
                TestListUiStyle.DrawColoredLabel(new Rect(0f, 0f, innerWidth, contentHeight), content, BodyColor);
            }
            finally
            {
                Widgets.EndScrollView();
            }
            contentScroll[key] = position;
        }

        private static void DrawFeedback(Rect row, TestDocument doc, TestEntry entry)
        {
            float labelWidth = Mathf.Min(FeedbackLabelWidth, Mathf.Max(40f, row.width * 0.28f));
            TestListUiStyle.DrawColoredLabel(
                new Rect(row.x, row.y, labelWidth, 18f),
                TestListText.FeedbackLabel,
                TestListUiStyle.DimmedText);

            Rect editRect = new Rect(row.x + labelWidth, row.y, Mathf.Max(40f, row.width - labelWidth), row.height);
            string current = entry.Feedback ?? "";
            string edited;

            // Pin the font so the text area cannot inherit a font another widget changed, and
            // give the control a stable name so its focus state is queryable.
            GameFont previousFont = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                GUI.SetNextControlName("TestList_feedback_" + Key(doc, entry));
                edited = Widgets.TextArea(editRect, current);
            }
            finally
            {
                Text.Font = previousFont;
            }

            // Bound while typing: the manager debounces and auto-saves.
            if (!string.Equals(edited, current, StringComparison.Ordinal))
            {
                TestListManager.SetFeedback(doc, entry, edited);
            }
        }

        private static void ApplyStatus(TestDocument doc, TestEntry entry, TestStatus status)
        {
            TestListManager.SetStatus(doc, entry, status);
            TestListSettings settings = TestListManager.Settings;
            if (status == TestStatus.Fail && settings != null && settings.AutoExpandBroken)
            {
                SetExpanded(doc, entry, true);
            }
        }

        private static string BuildMetaText(TestEntry entry)
        {
            string line = string.Format(TestListText.LineFormat, entry.SourceLine + 1);
            string path = entry.Path;
            if (path.NullOrEmpty()) path = entry.RawHeading;
            if (path.NullOrEmpty()) return line;
            return path + "   (" + line + ")";
        }

        // ------------------------------------------------------------------
        // Shared layout maths -- used by BOTH the measure pass and the draw pass
        // ------------------------------------------------------------------

        private struct BodyLayout
        {
            public float ContentRegionHeight;
            public float ContentMeasuredHeight;
            public bool ContentScrolls;
            public float FeedbackEditHeight;
            public float TotalHeight;
        }

        private static BodyLayout MeasureBody(TestEntry entry, float width)
        {
            BodyLayout layout = default(BodyLayout);
            float innerWidth = InnerWidth(width);

            string content = entry.Content ?? "";
            if (content.Length > 0)
            {
                float measured = TestListUiStyle.MeasureTextHeight(content, innerWidth);
                TestListSettings settings = TestListManager.Settings;
                float maxHeight = settings != null ? settings.EffectiveMaxContentHeight : 220f;
                layout.ContentMeasuredHeight = measured;
                layout.ContentScrolls = measured > maxHeight + 0.5f;
                layout.ContentRegionHeight = Mathf.Min(measured, maxHeight);
            }

            layout.FeedbackEditHeight = FeedbackEditHeight;
            layout.TotalHeight =
                BodyPadding +
                (layout.ContentRegionHeight > 0f ? layout.ContentRegionHeight + ContentGap : 0f) +
                MetaHeight +
                layout.FeedbackEditHeight +
                BodyPadding;
            return layout;
        }

        private static float InnerWidth(float width)
        {
            return Mathf.Max(40f, width - BodyLeftPadding * 2f);
        }
    }
}
