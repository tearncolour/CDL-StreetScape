using System;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The span of a gantry and the arm of a cantilever set by hand (0.12.78): the ranges, the tenths, the buttons, and the length that is the player's until Auto gives the planner's back.</summary>
    internal static class SpanSliderTests
    {
        public static void RunAll()
        {
            Program.Run("span slider: the ranges of a gantry and of the arms", Ranges);
            Program.Run("span slider: tenths, typed numbers and the buttons", Values);
            Program.Run("span slider: a length set by hand is for its kind and Auto gives the planner's back", Override);
            Program.Run("span slider: the clearance has its own range, tenths and typed numbers", ClearanceValues);
            Program.Run("span slider: the clearance and the span are set apart, for one kind, and one Auto gives both back", ClearanceOverride);
        }

        private static void Ranges()
        {
            Program.Check(SpanSlider.Min(SupportKind.Gantry) == SupportLayouts.MinSpan && SpanSlider.Max(SupportKind.Gantry) == SupportLayouts.MaxSpan && SupportLayouts.MinSpan == 6f && SupportLayouts.MaxSpan == 40f, "a gantry: 6 to 40 m, the layout's own");
            foreach (SupportKind kind in new[] { SupportKind.Cantilever, SupportKind.DoubleCantilever, SupportKind.FCantilever })
                Program.Check(SpanSlider.Applies(kind) && SpanSlider.Min(kind) == 1f && SpanSlider.Max(kind) == SupportPlanner.OctaArmMax && SupportPlanner.OctaArmMax == 16f, kind + ": an arm of 1 to 16 m");
            foreach (SupportKind kind in new[] { SupportKind.SinglePole, SupportKind.DoublePole, SupportKind.TriplePole, SupportKind.Attached, SupportKind.AttachedArm, SupportKind.WallFlat, SupportKind.WallBracket })
                Program.Check(!SpanSlider.Applies(kind) && float.IsNaN(SpanSlider.Within(kind, 10f)), kind + ": no length to set");
        }

        private static void Values()
        {
            Program.Check(SpanSlider.Within(SupportKind.Gantry, 18.4f) == 18.4f && SpanSlider.Within(SupportKind.Gantry, 18.4000001f) == 18.4f && SpanSlider.Within(SupportKind.Gantry, 18.3999f) == 18.4f, "the tenth: a slider's float noise lands on it");
            Program.Check(SpanSlider.Within(SupportKind.Gantry, 2f) == 6f && SpanSlider.Within(SupportKind.Gantry, 99f) == 40f && SpanSlider.Within(SupportKind.Cantilever, 0.2f) == 1f && SpanSlider.Within(SupportKind.Cantilever, 30f) == 16f, "inside the range");
            Program.Check(SpanSlider.Within(SupportKind.Gantry, 12.26f) == 12.3f && SpanSlider.Within(SupportKind.Gantry, 12.24f) == 12.2f, "rounded to the nearest tenth");
            Program.Check(float.IsNaN(SpanSlider.Within(SupportKind.Gantry, float.NaN)) && float.IsNaN(SpanSlider.Within(SupportKind.Gantry, float.PositiveInfinity)), "not a number: none");

            Program.Check(SpanSlider.Typed(SupportKind.Gantry, 22.5f) == 22.5f && SpanSlider.Typed(SupportKind.Gantry, 22.34f) == 22.3f, "typed: the metres, in tenths");
            Program.Check(SpanSlider.Typed(SupportKind.Gantry, 0f) == 0f && SpanSlider.Typed(SupportKind.Gantry, -3f) == 0f, "typed: 0 or less is the support's own length");
            Program.Check(float.IsNaN(SpanSlider.Typed(SupportKind.Gantry, float.NaN)), "typed: not a number is nothing");
            Program.Check(SpanSlider.Typed(SupportKind.Gantry, 200f) == 40f && SpanSlider.Typed(SupportKind.Gantry, 3f) == 6f, "typed: past the range is the end of it");

            Program.Check(SpanSlider.Step == 0.5f, "the slider's step is half a metre");
            bool onGrid = true;
            for (int i = 0; i <= 68; i++)
                onGrid &= SpanSlider.Within(SupportKind.Gantry, 6f + i * SpanSlider.Step) == 6f + i * SpanSlider.Step && SpanSlider.Within(SupportKind.Gantry, 6f + i * SpanSlider.Step + 1e-6f) == 6f + i * SpanSlider.Step;
            Program.Check(onGrid, "all 69 half metres from 6 to 40 m are held as they are, with float noise or without");
            float shown = 10f;
            for (int i = 0; i < 1000; i++)
                shown = SpanSlider.Within(SupportKind.Gantry, shown + (i % 2 == 0 ? SpanSlider.Step : -SpanSlider.Step));
            Program.Check(shown == 10f, "a thousand steps of the buttons back and forth drift nowhere: " + shown);
            shown = 39.8f;
            for (int i = 0; i < 5; i++)
                shown = SpanSlider.Within(SupportKind.Gantry, shown + SpanSlider.Step);
            Program.Check(shown == 40f, "stopped at the end of the range: " + shown);
        }

        private static void ClearanceValues()
        {
            Program.Check(SpanSlider.ClearanceStep == 0.1f && SpanSlider.Step == 0.5f, "the clearance's step is a tenth, the span's half a metre");
            foreach (SupportKind kind in new[] { SupportKind.Gantry, SupportKind.Cantilever, SupportKind.DoubleCantilever, SupportKind.FCantilever })
                Program.Check(SpanSlider.WithinClearance(kind, 1f) == 3f && SpanSlider.WithinClearance(kind, 99f) == 9f && SpanSlider.WithinClearance(kind, 5.5f) == 5.5f, kind + ": 3 to 9 m");
            foreach (SupportKind kind in new[] { SupportKind.SinglePole, SupportKind.DoublePole, SupportKind.Attached, SupportKind.WallFlat })
                Program.Check(float.IsNaN(SpanSlider.WithinClearance(kind, 5.5f)) && float.IsNaN(SpanSlider.TypedClearance(kind, 5.5f)), kind + ": no clearance to set");
            Program.Check(SpanSlider.WithinClearance(SupportKind.Gantry, 4.4000001f) == 4.4f && SpanSlider.WithinClearance(SupportKind.Gantry, 4.3999f) == 4.4f && SpanSlider.WithinClearance(SupportKind.Gantry, 4.46f) == 4.5f && SpanSlider.WithinClearance(SupportKind.Gantry, 4.44f) == 4.4f, "the tenth: float noise lands on it, the rest is rounded");
            Program.Check(float.IsNaN(SpanSlider.WithinClearance(SupportKind.Gantry, float.NaN)) && float.IsNaN(SpanSlider.WithinClearance(SupportKind.Gantry, float.NegativeInfinity)), "not a number: none");
            bool onGrid = true;
            for (int i = 0; i <= 60; i++)
                onGrid &= SpanSlider.WithinClearance(SupportKind.Gantry, 3f + i * SpanSlider.ClearanceStep) == (float)Math.Round(3.0 + i * 0.1, 1) && SpanSlider.WithinClearance(SupportKind.Gantry, 3f + i * SpanSlider.ClearanceStep + 1e-6f) == (float)Math.Round(3.0 + i * 0.1, 1);
            Program.Check(onGrid, "all 61 tenths from 3 to 9 m are held as they are, with float noise or without");
            Program.Check(SpanSlider.TypedClearance(SupportKind.Gantry, 4.5f) == 4.5f && SpanSlider.TypedClearance(SupportKind.Gantry, 4.54f) == 4.5f && SpanSlider.TypedClearance(SupportKind.Gantry, 200f) == 9f && SpanSlider.TypedClearance(SupportKind.Gantry, 1f) == 3f, "typed: the metres, in tenths, within the range");
            Program.Check(SpanSlider.TypedClearance(SupportKind.Gantry, 0f) == 0f && SpanSlider.TypedClearance(SupportKind.Gantry, -3f) == 0f && float.IsNaN(SpanSlider.TypedClearance(SupportKind.Gantry, float.NaN)), "typed: 0 or less is the support's own, not a number is nothing");
            Program.Check(SpanSlider.ShownClearance(SupportKind.Gantry, null, 5.5f) == 5.5f, "no state at all: the support's own");
        }

        private static void ClearanceOverride()
        {
            var manual = new SpanOverride();
            Program.Check(!manual.ClearanceManual && float.IsNaN(manual.ClearanceFor(SupportKind.Gantry)) && SpanSlider.ShownClearance(SupportKind.Gantry, manual, 5.5f) == 5.5f, "none set: the support's own is shown");
            manual.SetClearance(SupportKind.Gantry, 4.5f);
            Program.Check(manual.ClearanceManual && !manual.Manual && manual.ClearanceFor(SupportKind.Gantry) == 4.5f && SpanSlider.ShownClearance(SupportKind.Gantry, manual, 5.5f) == 4.5f, "set: the player's, and the span is still the planner's");
            Program.Check(float.IsNaN(manual.ClearanceFor(SupportKind.Cantilever)) && SpanSlider.ShownClearance(SupportKind.Cantilever, manual, 5.5f) == 5.5f, "set for a gantry: a cantilever's is its own");
            manual.Set(SupportKind.Gantry, 22.5f);
            Program.Check(manual.Manual && manual.ClearanceManual && manual.For(SupportKind.Gantry) == 22.5f && manual.ClearanceFor(SupportKind.Gantry) == 4.5f, "the span set after the clearance: both kept, apart");
            manual.SetClearance(SupportKind.Gantry, 0f);
            Program.Check(!manual.ClearanceManual && manual.For(SupportKind.Gantry) == 22.5f, "the clearance 0 clears the clearance only");
            manual.SetClearance(SupportKind.Gantry, 4.5f);
            manual.SetClearance(SupportKind.Gantry, float.NaN);
            Program.Check(!manual.ClearanceManual && manual.Manual, "not a number clears it too, the span stays");
            manual.SetClearance(SupportKind.Gantry, 500f);
            Program.Check(manual.ClearanceFor(SupportKind.Gantry) == 9f, "set beyond the range: the end of it");
            manual.Clear();
            Program.Check(!manual.Manual && !manual.ClearanceManual && SpanSlider.ShownClearance(SupportKind.Gantry, manual, 5.5f) == 5.5f, "Auto gives both back");
            manual.Set(SupportKind.Gantry, 22.5f);
            manual.SetClearance(SupportKind.Gantry, 4.5f);
            manual.SetClearance(SupportKind.Cantilever, 4f);
            Program.Check(manual.ClearanceFor(SupportKind.Cantilever) == 4f && float.IsNaN(manual.ClearanceFor(SupportKind.Gantry)) && !manual.Manual && manual.Kind == SupportKind.Cantilever, "set for another kind: what was set for the first is dropped, one kind at a time");
            manual.Set(SupportKind.Cantilever, 6f);
            manual.Set(SupportKind.Gantry, 20f);
            Program.Check(float.IsNaN(manual.ClearanceFor(SupportKind.Cantilever)) && manual.For(SupportKind.Gantry) == 20f, "and the span of another kind drops the clearance too");
            manual.SetClearance(SupportKind.Gantry, 4.5f);
            manual.SetClearance(SupportKind.SinglePole, 4.5f);
            Program.Check(!manual.ClearanceManual && !manual.Manual, "a clearance for a kind with none clears both");
            manual.Set(SupportKind.Gantry, 20f);
            manual.SetClearance(SupportKind.Gantry, 4.5f);
            manual.Set(SupportKind.SinglePole, 5f);
            Program.Check(!manual.ClearanceManual && !manual.Manual, "a length for a kind with none clears both too");
        }

        private static void Override()
        {
            var manual = new SpanOverride();
            Program.Check(!manual.Manual && float.IsNaN(manual.For(SupportKind.Gantry)) && SpanSlider.Shown(SupportKind.Gantry, manual, 13.7f) == 13.7f, "none set: the planner's length is shown");
            manual.Set(SupportKind.Gantry, 22.5f);
            Program.Check(manual.Manual && manual.For(SupportKind.Gantry) == 22.5f && SpanSlider.Shown(SupportKind.Gantry, manual, 13.7f) == 22.5f, "set: it is the player's, whatever the planner says");
            Program.Check(float.IsNaN(manual.For(SupportKind.Cantilever)) && SpanSlider.Shown(SupportKind.Cantilever, manual, 4.5f) == 4.5f, "set for a gantry: a cantilever's arm is still the planner's");
            Program.Check(float.IsNaN(manual.For(SupportKind.SinglePole)) && SpanSlider.Shown(SupportKind.SinglePole, manual, 0f) == 0f, "a post has none");
            manual.Set(SupportKind.Gantry, 500f);
            Program.Check(manual.For(SupportKind.Gantry) == 40f, "set beyond the range: the end of it");
            manual.Clear();
            Program.Check(!manual.Manual && SpanSlider.Shown(SupportKind.Gantry, manual, 13.7f) == 13.7f, "Auto gives the planner's back");
            manual.Set(SupportKind.Gantry, 22.5f);
            manual.Set(SupportKind.Gantry, 0f);
            Program.Check(!manual.Manual, "0 is Auto too");
            manual.Set(SupportKind.Gantry, 22.5f);
            manual.Set(SupportKind.Gantry, float.NaN);
            Program.Check(!manual.Manual, "not a number is Auto too");
            manual.Set(SupportKind.Gantry, 22.5f);
            manual.Set(SupportKind.Cantilever, 6f);
            Program.Check(manual.For(SupportKind.Cantilever) == 6f && float.IsNaN(manual.For(SupportKind.Gantry)) && manual.Kind == SupportKind.Cantilever, "a length set for another kind replaces the first: one at a time");
            manual.Set(SupportKind.SinglePole, 5f);
            Program.Check(!manual.Manual, "a kind with no length clears it");
            Program.Check(SpanSlider.Shown(SupportKind.Gantry, null, 9f) == 9f, "no state at all: the planner's");
        }
    }
}
