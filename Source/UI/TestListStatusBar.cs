// TestList -- the 3-way 正常 / 异常 / 未测试 marker bar for one checklist entry.
//
// Owned by the UI task (task-3). In-game only.
//
// Every segment is a real IMGUI button (Widgets.ButtonInvisible -> GUI.Button), so the
// mouse event is consumed by IMGUI and cannot fall through to the map behind the panel.
using UnityEngine;
using Verse;

namespace TestList
{
    public static class TestListStatusBar
    {
        /// <summary>Row height of one marker bar.</summary>
        public const float Height = 26f;

        private const float SegmentGap = 3f;

        /// <summary>
        /// Draws the three segments filling <paramref name="rect"/>.
        /// Returns true and sets <paramref name="picked"/> when the player clicked one.
        /// </summary>
        public static bool Draw(Rect rect, TestStatus current, out TestStatus picked)
        {
            picked = current;
            if (rect.width <= 3f || rect.height <= 1f) return false;

            bool clicked = false;
            float segmentWidth = (rect.width - SegmentGap * 2f) / 3f;
            if (segmentWidth <= 1f) return false;

            for (int i = 0; i < 3; i++)
            {
                TestStatus status = StatusForSegment(i);
                Rect segment = new Rect(rect.x + i * (segmentWidth + SegmentGap), rect.y, segmentWidth, rect.height);
                if (DrawSegment(segment, status, status == current))
                {
                    picked = status;
                    clicked = true;
                }
            }
            return clicked;
        }

        private static TestStatus StatusForSegment(int index)
        {
            switch (index)
            {
                case 0: return TestStatus.Pass;
                case 1: return TestStatus.Fail;
                default: return TestStatus.Untested;
            }
        }

        /// <summary>Returns true when this segment was clicked. Consumes the mouse event on click.</summary>
        private static bool DrawSegment(Rect segment, TestStatus status, bool selected)
        {
            Color statusColor = TestListUiStyle.ColorForStatus(status);

            if (selected)
            {
                TestListUiStyle.Fill(segment, statusColor);
                TestListUiStyle.DrawOutline(segment, TestListUiStyle.MarkerOutline);
            }
            else
            {
                TestListUiStyle.Fill(segment, TestListUiStyle.MarkerIdle);
                Color dim = statusColor;
                dim.a = 0.22f;
                TestListUiStyle.Fill(segment.ContractedBy(1f), dim);
                TestListUiStyle.DrawOutline(segment, TestListUiStyle.MarkerOutline);
            }

            Color textColor = selected ? Color.black : TestListUiStyle.DimmedText;
            TestListUiStyle.DrawLabelAligned(segment, TestStatusUtil.ShortLabel(status), textColor, TextAnchor.MiddleCenter);

            if (!selected) Widgets.DrawHighlightIfMouseover(segment);

            bool hit = Widgets.ButtonInvisible(segment);
            if (hit)
            {
                // Widgets.ButtonInvisible already consumes the event via GUI.Button; the
                // explicit Use() is belt-and-braces against a future refactor of that helper.
                if (Event.current != null) Event.current.Use();
                return true;
            }
            return false;
        }
    }
}
