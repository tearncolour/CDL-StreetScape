using System;
using System.Linq;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The eyedropper of the dock (0.12.88; the owner: a button under the dock's three, click a placed item to reuse its style and settings, then place): its mode, what a pick takes of the support, and its line of help and keys.</summary>
    internal static class StylePickTests
    {
        public static void RunAll()
        {
            Program.Run("style pick: the tool's modes", Modes);
            Program.Run("style pick: what pointing passes over", Paint);
            Program.Run("style pick: the span or arm the player set, and the clearance", SpanAndClearance);
            Program.Run("style pick: the distance from the kerb", Margin);
            Program.Run("style pick: a support with none of these", Nothing);
            Program.Run("style pick: the line of help and the keys", Hints);
        }

        private static void Modes()
        {
            Program.Check(StylePick.Mode == 3, "the eyedropper is mode 3");
            Program.Check(StylePick.ModeOf(0) == 0 && StylePick.ModeOf(1) == 1 && StylePick.ModeOf(2) == 2 && StylePick.ModeOf(3) == 3, "place, delete, edit and the eyedropper are what the panel asks for");
            Program.Check(StylePick.ModeOf(-1) == 0 && StylePick.ModeOf(4) == 0 && StylePick.ModeOf(99) == 0 && StylePick.ModeOf(int.MaxValue) == 0, "anything else is placing, as before");
            Program.Check(new[] { 0, 1, 2 }.All(mode => mode != StylePick.Mode), "no other mode is the eyedropper's");
        }

        private static void Paint()
        {
            Program.Check(StylePick.PassesPaint(0, false), "placing passes the paint over: it looks for a support to join");
            Program.Check(!StylePick.PassesPaint(0, true), "Ctrl while placing takes paint too: a marking can be picked");
            Program.Check(!StylePick.PassesPaint(1, false) && !StylePick.PassesPaint(2, false) && !StylePick.PassesPaint(StylePick.Mode, false), "deleting, editing and the eyedropper take paint");
            var keys = new System.Collections.Generic.HashSet<int>();
            foreach (int mode in new[] { 0, 1, 2, StylePick.Mode })
                foreach (bool picking in new[] { false, true })
                    Program.Check(keys.Add(StylePick.PointingKey(mode, picking)), "the reading of mode " + mode + (picking ? " with Ctrl" : "") + " has a key of its own");
        }

        private static void SpanAndClearance()
        {
            // A gantry: the span the player set is the support's own (BaseReach), not what the boards made it (Reach).
            StylePick.Carried gantry = StylePick.Of(SupportKind.Gantry, true, true, 24.3f, 26.8f, 0f, float.NaN);
            Program.Check(gantry.Span == 24.3f && gantry.Clearance == 0f, "the span of a gantry by hand: its own, 24.3 m, not the 26.8 m the boards made of it");
            Program.Check(StylePick.Of(SupportKind.Gantry, true, false, 0f, 20.4f, 0f, float.NaN).Span == 20.4f, "no base length kept: the length of the item");
            Program.Check(StylePick.Of(SupportKind.Gantry, false, true, 24.3f, 26.8f, 0f, float.NaN).Span == 0f, "a span the planner made is the planner's again for the next support");
            Program.Check(StylePick.Of(SupportKind.Cantilever, true, true, 5.4f, 5.4f, 0f, float.NaN).Span == 5.4f, "the arm of a cantilever by hand");
            Program.Check(StylePick.Of(SupportKind.Gantry, true, true, 100f, 100f, 0f, float.NaN).Span == SupportLayouts.MaxSpan, "kept inside the longest span a gantry has");
            Program.Check(StylePick.Of(SupportKind.Gantry, true, true, 1f, 1f, 0f, float.NaN).Span == SupportLayouts.MinSpan, "and the shortest");
            Program.Check(StylePick.Of(SupportKind.Cantilever, true, true, 0.4f, 0.4f, 0f, float.NaN).Span == SpanSlider.MinArm, "an arm is at least the shortest arm");
            Program.Check(StylePick.Of(SupportKind.Gantry, true, true, 0f, 0f, 0f, float.NaN).Span == 0f && StylePick.Of(SupportKind.Gantry, true, true, float.NaN, float.NaN, 0f, float.NaN).Span == 0f, "no length, or none that is a number: the planner's");
            Program.Check(StylePick.Of(SupportKind.Gantry, false, true, 14f, 14f, 4.5f, float.NaN).Clearance == 4.5f, "the clearance of a height-limit frame is taken whatever the span was");
            Program.Check(StylePick.Of(SupportKind.Gantry, false, true, 14f, 14f, 0f, float.NaN).Clearance == 0f, "no clearance set: the support's own");
            Program.Check(StylePick.Of(SupportKind.Gantry, false, true, 14f, 14f, 50f, float.NaN).Clearance == SupportLayouts.MaxClearance, "a clearance is kept within the range");
            Program.Check(StylePick.Of(SupportKind.Gantry, false, true, 14f, 14f, 1f, float.NaN).Clearance == SupportLayouts.MinClearance, "and its lowest");
            Program.Check(StylePick.Of(SupportKind.DoubleCantilever, true, true, 6f, 6f, 5f, float.NaN).Span == 6f, "each arm of a double cantilever has one");
            Program.Check(StylePick.Of(SupportKind.FCantilever, true, true, 6f, 6f, 5f, float.NaN).Clearance == 5f, "and an F cantilever");
        }

        private static void Margin()
        {
            StylePick.Carried post = StylePick.Of(SupportKind.SinglePole, false, false, 0f, 0f, 0f, 1.34f);
            Program.Check(post.HasMargin && Math.Abs(post.Margin - 1.3f) < 1e-5f, "a post's distance from the kerb, in tenths of a metre");
            foreach (SupportKind kind in new[] { SupportKind.DoublePole, SupportKind.TriplePole, SupportKind.Cantilever, SupportKind.FCantilever, SupportKind.Gantry })
                Program.Check(StylePick.Of(kind, false, false, 0f, 0f, 0f, 0.7f).HasMargin, kind + ": the support the editor sets the distance of takes it");
            Program.Check(StylePick.Of(SupportKind.SinglePole, false, false, 0f, 0f, 0f, -100f).Margin == KerbMargin.Min && StylePick.Of(SupportKind.SinglePole, false, false, 0f, 0f, 0f, 100f).Margin == KerbMargin.Max, "kept inside the range of the slider");
            Program.Check(!StylePick.Of(SupportKind.SinglePole, false, false, 0f, 0f, 0f, float.NaN).HasMargin, "a margin that cannot be read (a post on a strip between the carriageways, a road that is gone) is left as it is");
            Program.Check(!StylePick.Of(SupportKind.SinglePole, false, false, 0f, 0f, 0f, float.PositiveInfinity).HasMargin && !StylePick.Of(SupportKind.SinglePole, false, false, 0f, 0f, 0f, float.NegativeInfinity).HasMargin, "and one that is no number");
            Program.Check(StylePick.Of(SupportKind.SinglePole, false, false, 0f, 0f, 0f, 0f).HasMargin, "a margin of 0 is a margin");
        }

        private static void Nothing()
        {
            foreach (SupportKind kind in new[] { SupportKind.SinglePole, SupportKind.DoublePole, SupportKind.TriplePole })
            {
                StylePick.Carried post = StylePick.Of(kind, true, true, 24f, 24f, 5f, float.NaN);
                Program.Check(post.Span == 0f && post.Clearance == 0f && !post.HasMargin, kind + ": a post has no span and no clearance whatever the item says");
            }

            foreach (SupportKind kind in new[] { SupportKind.WallFlat, SupportKind.WallBracket, SupportKind.Attached, SupportKind.OverRoad })
            {
                StylePick.Carried other = StylePick.Of(kind, true, true, 24f, 24f, 5f, 1.2f);
                Program.Check(other.Span == 0f && other.Clearance == 0f && !other.HasMargin, kind + ": a wall, a host or a structure takes none of the three");
            }

            foreach (SupportKind kind in Enum.GetValues(typeof(SupportKind)))
            {
                StylePick.Carried carried = StylePick.Of(kind, true, true, 12f, 12f, 5f, 1f);
                Program.Check((carried.Span > 0f) == SpanSlider.Applies(kind) && (carried.HasMargin) == KerbMargin.Edits(kind), kind + ": the span is taken for the hung supports and the distance for the ones the editor moves");
            }
        }

        private static void Hints()
        {
            string zh = HintText.Of(new HintInfo { Kind = "pick-mode" }, true), en = HintText.Of(new HintInfo { Kind = "pick-mode" }, false);
            Program.Check(zh.Length > 0 && en.Length > 0 && zh.Contains("吸取") && en.Contains("pick"), "the line of help of the eyedropper says what a click does: " + zh + " | " + en);
            Program.Check(!zh.Contains("Ctrl") && !en.Contains("Ctrl"), "it does not tell the player to hold a key: the button is the way in");
            string ctrl = HintText.Of(new HintInfo { Kind = "pick", Bad = true }, true);
            Program.Check(ctrl.Contains("Ctrl"), "while placing, with Ctrl and nothing under the cursor, the line still says how to pick");
            foreach (bool chinese in new[] { true, false })
            {
                var rows = HintText.Keys(new HintInfo { Kind = "pick-mode" }, chinese);
                Program.Check(rows.Count > 0 && rows.Any(row => row[1].Contains(chinese ? "吸取" : "pick")), "the keys of the eyedropper name the click: " + (chinese ? "zh" : "en"));
                Program.Check(!rows.Any(row => row[0].Contains("Ctrl") && row[0].Contains("click") || row[0].Contains("Ctrl 单击")), "no row asks for Ctrl + click in the eyedropper");
                Program.Check(rows.Any(row => row[0].Contains("Esc")), "and the last row leaves it");
            }

            Program.Check(HintText.Kinds.Contains("pick-mode"), "the kind is in the list the tests run over");
        }
    }
}
