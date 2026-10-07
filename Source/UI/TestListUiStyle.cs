// TestList -- shared palette + IMGUI measuring/drawing helpers for the floating panel.
//
// Owned by the UI task (task-3). In-game only: this file is allowed to use Verse and
// UnityEngine freely and is excluded from the headless verification build.
//
// Rules encoded here (they are what keeps the panel from jittering):
//   * every text draw/measure goes through this class, which pins Text.Font to
//     GameFont.Small and Text.Anchor explicitly and restores them afterwards, so a
//     neighbouring RimWorld widget cannot change the metrics under us;
//   * every texture is created lazily from inside OnGUI (Texture2D creation is illegal
//     off the main thread) and a failure degrades to "draw nothing" instead of throwing;
//   * MeasureTextHeight/MeasureTextAreaHeight must only be called from inside OnGUI.
using System;
using UnityEngine;
using Verse;

namespace TestList
{
    public static class TestListUiStyle
    {
        public static readonly Color PassColor = new Color(0.34f, 0.78f, 0.38f, 1f);
        public static readonly Color FailColor = new Color(0.93f, 0.34f, 0.29f, 1f);
        public static readonly Color UntestedColor = new Color(0.72f, 0.73f, 0.75f, 1f);
        public static readonly Color DimmedText = new Color(0.62f, 0.63f, 0.66f, 1f);
        public static readonly Color PanelBg = new Color(0.09f, 0.09f, 0.11f, 0.55f);
        public static readonly Color PanelBgAlt = new Color(0.15f, 0.15f, 0.18f, 0.55f);
        public static readonly Color MarkerIdle = new Color(0.22f, 0.22f, 0.26f, 0.95f);
        public static readonly Color MarkerOutline = new Color(0f, 0f, 0f, 0.65f);

        private static Texture2D solidWhite;
        private static bool solidWhiteFailed;

        private static bool measuring;
        private static GameFont savedFont;
        private static TextAnchor savedAnchor;
        private static bool savedWordWrap;

        private static readonly GUIContent measureBuffer = new GUIContent();

        /// <summary>True while a measure-only pass is active (see BeginMeasure/EndMeasure).</summary>
        public static bool IsMeasuring
        {
            get { return measuring; }
        }

        /// <summary>1x1 white texture, created on first use inside OnGUI. Null when unavailable.</summary>
        public static Texture2D SolidWhite
        {
            get { return GetSolidWhite(); }
        }

        // ------------------------------------------------------------------
        // Text metrics
        // ------------------------------------------------------------------

        /// <summary>Line height of GameFont.Small regardless of the caller's current font.</summary>
        public static float SmallLineHeight
        {
            get
            {
                GameFont prev = Text.Font;
                try
                {
                    Text.Font = GameFont.Small;
                    return Text.LineHeight;
                }
                catch (Exception)
                {
                    return 22f;
                }
                finally
                {
                    Text.Font = prev;
                }
            }
        }

        /// <summary>
        /// Height needed to render <paramref name="text"/> wrapped at <paramref name="width"/>
        /// with the same style Widgets.Label uses. Safe to call from a measure pass or from
        /// normal drawing; never call it before OnGUI (fonts would not be initialised yet).
        /// </summary>
        public static float MeasureTextHeight(string text, float width)
        {
            if (text.NullOrEmpty()) return 0f;
            width = Mathf.Max(1f, width);

            if (measuring)
            {
                try
                {
                    return Text.CalcHeight(text, width);
                }
                catch (Exception)
                {
                    return EstimateTextHeight(text, width);
                }
            }

            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            bool prevWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                return Text.CalcHeight(text, width);
            }
            catch (Exception)
            {
                return EstimateTextHeight(text, width);
            }
            finally
            {
                Text.Font = prevFont;
                Text.Anchor = prevAnchor;
                Text.WordWrap = prevWrap;
            }
        }

        /// <summary>Unwrapped width of <paramref name="text"/> with the standard label style.</summary>
        public static float MeasureTextWidth(string text)
        {
            if (text.NullOrEmpty()) return 0f;

            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            bool prevWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = false;
                return Text.CalcSize(text).x;
            }
            catch (Exception)
            {
                return text.Length * 7f;
            }
            finally
            {
                Text.Font = prevFont;
                Text.Anchor = prevAnchor;
                Text.WordWrap = prevWrap;
            }
        }

        /// <summary>
        /// Height of a read-only text area style render of <paramref name="text"/>.
        /// Used for the body region so the on-screen widget and the measured height agree.
        /// </summary>
        public static float MeasureTextAreaHeight(string text, float width)
        {
            if (text.NullOrEmpty()) return 0f;
            width = Mathf.Max(1f, width);

            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUIStyle style = Text.CurTextAreaReadOnlyStyle;
                if (style == null) return MeasureTextHeight(text, width);
                measureBuffer.text = text;
                float h = style.CalcHeight(measureBuffer, width);
                if (h < Text.LineHeight) h = MeasureTextHeight(text, width);
                return h;
            }
            catch (Exception)
            {
                return MeasureTextHeight(text, width);
            }
            finally
            {
                measureBuffer.text = "";
                Text.Font = prevFont;
                Text.Anchor = prevAnchor;
            }
        }

        /// <summary>
        /// A measure-only context. Wrap the height-accumulating pass of the panel in
        /// BeginMeasure()/EndMeasure() so measuring cannot inherit a font a widget left behind.
        /// Drawing the same values is <b>not</b> required while measuring; only heights are read.
        /// </summary>
        public static void BeginMeasure()
        {
            if (measuring) return;
            measuring = true;
            savedFont = Text.Font;
            savedAnchor = Text.Anchor;
            savedWordWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
        }

        public static void EndMeasure()
        {
            if (!measuring) return;
            measuring = false;
            Text.Font = savedFont;
            Text.Anchor = savedAnchor;
            Text.WordWrap = savedWordWrap;
        }

        /// <summary>Pins a known-good font/anchor for the duration of one panel frame.</summary>
        public static void BeginPanelText()
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
        }

        /// <summary>Truncates with a trailing ellipsis until the text fits <paramref name="width"/>.</summary>
        public static string Fit(string text, float width)
        {
            if (text.NullOrEmpty()) return text ?? "";
            width = Mathf.Max(1f, width);

            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            bool prevWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = false;

                if (Text.CalcSize(text).x <= width) return text;

                const string suffix = "...";
                int lo = 0;
                int hi = text.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (Text.CalcSize(text.Substring(0, mid) + suffix).x <= width) lo = mid;
                    else hi = mid - 1;
                }
                return text.Substring(0, lo) + suffix;
            }
            catch (Exception)
            {
                return text;
            }
            finally
            {
                Text.Font = prevFont;
                Text.Anchor = prevAnchor;
                Text.WordWrap = prevWrap;
            }
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        /// <summary>Solid colour rectangle. No-op when the texture could not be created.</summary>
        public static void Fill(Rect r, Color c)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            if (c.a <= 0f) return;
            Texture2D tex = GetSolidWhite();
            if (tex == null) return;
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, tex);
            GUI.color = prev;
        }

        /// <summary>Alternating row background so long lists stay readable.</summary>
        public static void DrawRowBackground(Rect r, int index)
        {
            Fill(r, (index & 1) == 0 ? PanelBg : PanelBgAlt);
        }

        /// <summary>1px frame around <paramref name="r"/>.</summary>
        public static void DrawOutline(Rect r, Color c)
        {
            if (r.width <= 2f || r.height <= 2f) return;
            Fill(new Rect(r.x, r.y, r.width, 1f), c);
            Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            Fill(new Rect(r.x, r.y, 1f, r.height), c);
            Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }

        public static Color ColorForStatus(TestStatus status)
        {
            switch (status)
            {
                case TestStatus.Pass: return PassColor;
                case TestStatus.Fail: return FailColor;
                default: return UntestedColor;
            }
        }

        /// <summary>Text left-aligned inside <paramref name="r"/> in the given colour.</summary>
        public static void DrawColoredLabel(Rect r, string text, Color color)
        {
            DrawLabelAligned(r, text, color, TextAnchor.UpperLeft);
        }

        /// <summary>Text in the given colour and anchor. Font/anchor are always restored.</summary>
        public static void DrawLabelAligned(Rect r, string text, Color color, TextAnchor anchor)
        {
            if (text.NullOrEmpty()) return;

            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            Color prevColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = anchor;
                GUI.color = color;
                Widgets.Label(r, text);
            }
            catch (Exception)
            {
                // A broken label must never take the whole panel down.
            }
            finally
            {
                GUI.color = prevColor;
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
            }
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private static Texture2D GetSolidWhite()
        {
            if (solidWhite != null) return solidWhite;
            if (solidWhiteFailed) return null;
            // Texture2D creation is only legal on the main thread / inside OnGUI.
            if (Event.current == null) return null;
            try
            {
                Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                tex.name = "TestListSolidWhite";
                tex.hideFlags = HideFlags.HideAndDontSave;
                solidWhite = tex;
                return solidWhite;
            }
            catch (Exception)
            {
                solidWhiteFailed = true;
                return null;
            }
        }

        /// <summary>
        /// Deliberately pessimistic fallback used only if Text.CalcHeight throws (fonts not
        /// ready). Over-estimating keeps the last pixels of a long entry reachable.
        /// </summary>
        private static float EstimateTextHeight(string text, float width)
        {
            if (text.NullOrEmpty()) return 0f;
            float lineHeight = Mathf.Max(8f, SmallLineHeight);
            float usable = Mathf.Max(1f, width);
            int lines = 0;
            int lineStart = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i != text.Length && text[i] != '\n') continue;
                int length = i - lineStart;
                if (length > 0 && text[i - 1] == '\r') length--;
                float estimatedWidth = length * 7f;
                lines += Mathf.Max(1, Mathf.CeilToInt(estimatedWidth / usable));
                lineStart = i + 1;
            }
            return Mathf.Max(1, lines) * lineHeight;
        }
    }
}
