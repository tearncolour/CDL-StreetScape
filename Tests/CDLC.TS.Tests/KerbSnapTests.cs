using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// Barriers on the kerbs between carriageways and the arrow keys' nudge (0.12.22; the owner's BRT road: "不能吸附中间的路沿", and "左右方向键微调...即靠近/远离道路").
    /// </summary>
    internal static class KerbSnapTests
    {
        public static void RunAll()
        {
            Program.Run("kerb snap: the raised strips between the carriageways", Raised);
            Program.Run("kerb snap: a barrier started over a kerb stands on it", BarrierLine);
            Program.Run("kerb snap: the strips across a chain that runs against a segment", Chain);
            Program.Run("kerb snap: the hint tells the nudge keys and the nudge", Hint);
        }

        private static SectionPiece P(float x0, float x1, float top, bool lanes, bool blocks = false, bool other = false) =>
            new SectionPiece { X = (x0 + x1) * 0.5f, Width = x1 - x0, Top = top, Surface = true, RoadLanes = lanes, BlocksTraffic = blocks, OtherLevel = other, Name = "p" };

        /// <summary>A BRT road 30 m wide: footways, two lanes each side, 0.5 m kerbs, the BRT lanes in the middle with a raised island between them, and a hidden piece under it.</summary>
        private static List<SectionPiece> Brt() => new List<SectionPiece>
        {
            P(-15f, -12f, 0.2f, false),
            P(-12f, -5f, 0f, true),
            P(-5f, -4.7f, 0.2f, false),
            P(-4.7f, -4.5f, 0.2f, false),
            P(-4.5f, -0.5f, 0f, true),
            P(-0.5f, 0.5f, 0.15f, false, true),
            P(-0.5f, 0.5f, -2f, false, false, true),
            P(0.5f, 4.5f, 0f, true),
            P(4.5f, 5f, 0.2f, false),
            P(5f, 12f, 0f, true),
            P(12f, 15f, 0.2f, false)
        };

        private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;

        private static void Raised()
        {
            List<(float X0, float X1)> strips = SurfaceSection.Raised(Brt());
            Program.Check(strips.Count == 3, "three strips between the carriageways (the footways are outside them): " + strips.Count);
            if (strips.Count == 3)
            {
                Program.Check(Near(strips[0].X0, -5f) && Near(strips[0].X1, -4.5f), $"the left kerb, its two pieces joined: {strips[0].X0}..{strips[0].X1}");
                Program.Check(Near(strips[1].X0, -0.5f) && Near(strips[1].X1, 0.5f), "the island between the BRT lanes (it stops traffic), not the hidden piece under it");
                Program.Check(Near(strips[2].X0, 4.5f) && Near(strips[2].X1, 5f), "the right kerb");
            }

            var flat = new List<SectionPiece> { P(-15f, -12f, 0.2f, false), P(-12f, 12f, 0f, true), P(12f, 15f, 0.2f, false) };
            Program.Check(SurfaceSection.Raised(flat).Count == 0, "a road with footways only has none");
            Program.Check(SurfaceSection.Raised(new List<SectionPiece> { P(-3f, 3f, 0.2f, false) }).Count == 0, "a cross-section without lanes has none");
            Program.Check(SurfaceSection.Raised(new List<SectionPiece> { P(-6f, -0.02f, 0f, true), P(-0.02f, 0.02f, 0.01f, false), P(0.02f, 6f, 0f, true) }).Count == 0, "a piece as high as the carriageway is not a kerb");
        }

        private static void BarrierLine()
        {
            var raised = new List<(float Lo, float Hi)> { (-5f, -4.5f), (-0.5f, 0.5f), (2f, 6f) };
            float[] forward = { 1.75f, 5.25f }, back = { -1.75f, -5.25f };
            MarkingSpot on = MarkingSnap.BarrierLine(forward, back, raised, -4.6f);
            Program.Check(on.OnRaised && Near(on.Lateral, -4.75f), "over a narrow kerb: its middle " + on.Lateral);
            MarkingSpot beside = MarkingSnap.BarrierLine(forward, back, raised, -5.25f);
            Program.Check(beside.OnRaised && Near(beside.Lateral, -4.75f), "within 30 cm of it, on the lane line that the plain snap would take: still the kerb");
            MarkingSpot lane = MarkingSnap.BarrierLine(forward, back, raised, -5.9f);
            MarkingSpot plain = MarkingSnap.Line(forward, back, -5.9f);
            Program.Check(!lane.OnRaised && Near(lane.Lateral, plain.Lateral), "away from it as a line between lanes is snapped " + lane.Lateral);
            Program.Check(Near(MarkingSnap.BarrierLine(forward, back, raised, 0.2f).Lateral, 0f), "the island's middle");
            Program.Check(Near(MarkingSnap.BarrierLine(forward, back, raised, 4.5f).Lateral, 4f), "a wide median: its middle when the cursor is near it");
            Program.Check(Near(MarkingSnap.BarrierLine(forward, back, raised, 2.7f).Lateral, 2.7f), "and where the cursor is away from its middle");
            Program.Check(Near(MarkingSnap.BarrierLine(forward, back, raised, 1.8f).Lateral, 2.3f), "never nearer its side than 30 cm");
            Program.Check(!MarkingSnap.BarrierLine(forward, back, null, 3f).OnRaised, "no strips: the plain snap");
        }

        private static void Chain()
        {
            var a = new ChainPiece { Curve = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f)), Reversed = false };
            var b = new ChainPiece { Curve = Bezier3.Line(new Vec3(0f, 0f, 200f), new Vec3(0f, 0f, 100f)), Reversed = true };
            a.Raised.Add((-5f, -4.5f));
            a.Raised.Add((1f, 2f));
            b.Raised.Add((-5f, -4.5f));
            b.Raised.Add((1f, 2f));
            var chain = new RoadChain(new[] { a, b });
            Program.Check(a.RaisedAcross.Count == 2 && Near(a.RaisedAcross[0].Lo, -5f) && Near(a.RaisedAcross[1].Hi, 2f), "a piece that runs with the chain keeps its strips");
            Program.Check(b.RaisedAcross.Count == 2 && Near(b.RaisedAcross[0].Lo, -2f) && Near(b.RaisedAcross[0].Hi, -1f) && Near(b.RaisedAcross[1].Lo, 4.5f) && Near(b.RaisedAcross[1].Hi, 5f),
                "one that runs against it has them mirrored and in order: " + string.Join(", ", b.RaisedAcross.ConvertAll(r => $"{r.Lo}..{r.Hi}")));
            Program.Check(chain.Pieces.Count == 2, "the chain has its two pieces");
        }

        private static void Hint()
        {
            string zero = HintText.Of(new HintInfo { Kind = "mark-run-start" }, true);
            Program.Check(HintText.Keys(new HintInfo { Kind = "mark-run-start" }, true).Exists(k => k[0] == "← →" && k[1].StartsWith("近", StringComparison.Ordinal)) && !zero.Contains("已"), "the keys are told, and no nudge when there is none");
            string far = HintText.Of(new HintInfo { Kind = "mark-run-end", X = 0.3f, Left = 20f, Need = 200f }, true);
            Program.Check(far.Contains("远离 0.30 m"), "the nudge away from the road: " + far);
            string near = HintText.Of(new HintInfo { Kind = "bar-auto", Name = "City", X = -0.5f, Need = 2f, Left = 100f, Count = 8 }, false);
            Program.Check(near.Contains("0.50 m nearer"), "in English, nearer, the automatic barriers' roadside lines: " + near);
        }
    }
}
