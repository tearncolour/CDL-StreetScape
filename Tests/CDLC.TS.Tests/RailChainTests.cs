using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.66 (the owner: "支持吸附到铁路和地铁线路"): the part of drawing a barrier along a railway, a metro line or a tram line that is pure. A track edge has no car lane, no speed limit and no
    /// crosswalk: the chain of such pieces must still have its spans, its line, its edge and the line's lateral at the cursor. (The reading of the game's track edges is ECS and is not here.)
    /// </summary>
    internal static class RailChainTests
    {
        public static void RunAll()
        {
            Program.Run("rail chain: pieces with no lane make a chain, a line and its spans", NoLanes);
            Program.Run("rail chain: the wall's lateral is where the cursor is and the edge is the track bed's", Lateral);
        }

        private static ChainPiece Track(Vec3 a, Vec3 d, ChainJoint after, float half = 4.5f)
        {
            // A track edge: no lanes, no speed, the half width of its composition.
            var piece = new ChainPiece { Curve = Bezier3.Line(a, d), After = after, HalfWidth = half };
            piece.ReaderJoint = after;
            return piece;
        }

        private static void NoLanes()
        {
            var chain = new RoadChain(new[] { Track(new Vec3(0f, 20f, 0f), new Vec3(0f, 20f, 120f), ChainJoint.Pass), Track(new Vec3(0f, 20f, 120f), new Vec3(40f, 20f, 240f), ChainJoint.End) });
            Program.Check(chain.Breaks.Count == 0 && chain.Length > 245f && chain.Length < 248f, "two track pieces make one chain: " + chain.Length);
            List<ChainSpan> spans = chain.Spans(3.8f, JunctionMode.Side);
            Program.Check(spans.Count == 1 && spans[0].To - spans[0].From > 244f, "a wall 3.8 m from the centre line is one span the whole way: " + string.Join(" ", spans));
            List<Vec3> line = chain.Line(spans[0].From, spans[0].To, 3.8f, 0f);
            float longest = 0f, high = 0f;
            for (int i = 1; i < line.Count; i++)
                longest = Math.Max(longest, Vec3.DistanceXZ(line[i - 1], line[i]));
            foreach (Vec3 p in line)
                high = Math.Max(high, Math.Abs(p.Y - 20f));
            Program.Check(line.Count > 100 && longest < 2.6f && high < 0.01f, $"its line has {line.Count} points, no step over {longest:0.0} m, at the track's height ({high * 1000f:0.0} mm)");
            // The first point is 3.8 m to the right of the track's start, the way the track runs (+Z: the right is +X... as the chain's right).
            Vec3 first = line[0];
            Program.Check(Math.Abs(Math.Abs(first.X) - 3.8f) < 0.01f, $"3.8 m from the centre line at its start: {first.X:0.00}");
        }

        private static void Lateral()
        {
            // Without car lanes the line is where the cursor is, snapped to nothing.
            MarkingSpot spot = MarkingSnap.BarrierLine(new List<float>(), new List<float>(), null, 3.2f);
            Program.Check(Math.Abs(spot.Lateral - 3.2f) < 1e-4f && !spot.Snapped && !spot.HadLanes, $"the line is at the cursor ({spot.Lateral:0.00} m), not snapped, no lanes");
            MarkingSpot left = MarkingSnap.BarrierLine(new List<float>(), new List<float>(), null, -4.1f);
            Program.Check(Math.Abs(left.Lateral + 4.1f) < 1e-4f, "and on the left too: " + left.Lateral);
            // The edge it follows is the track bed's: the half width less the inset, 1.5 m at least.
            ChainPiece bed = Track(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), ChainJoint.End, 4.5f);
            Program.Near(BarrierAuto.EdgeOf(bed, 1), 4.15f, 1e-3f, "the edge of a track bed 4.5 m to a side");
            Program.Near(BarrierAuto.EdgeOf(Track(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), ChainJoint.End, 1.2f), -1), 1.5f, 1e-3f, "a narrow one: 1.5 m");
            // The profile along two pieces of different width changes over the node, not in a step.
            var chain = new RoadChain(new[] { Track(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), ChainJoint.Pass, 4.5f), Track(new Vec3(0f, 0f, 100f), new Vec3(0f, 0f, 200f), ChainJoint.End, 6.5f) });
            var profile = new EdgeProfile(chain, 1);
            float steepest = 0f;
            for (float s = 90f; s < 110f; s += 0.1f)
                steepest = Math.Max(steepest, Math.Abs(profile.At(s + 0.1f) - profile.At(s)) / 0.1f);
            Program.Check(profile.At(50f) < profile.At(150f) && steepest < 0.5f, $"the edge goes from {profile.At(50f):0.00} to {profile.At(150f):0.00} m, at most {steepest * 100f:0} % across the node");
        }
    }
}
