using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.101 (the owner, 5 Nov 23:5x, a guard rail along a wide road bend: "这个吸附有一点点问题"): the log of that placement says "+3.66 m at the start ... along the edge, -0.49 m off it": the click was
    /// snapped to the outer edge of the outermost lane (MarkingSnap.LinePlaces), the painted edge line, 0.49 m inside the edge a roadside barrier is given (0.35 m inside the road's edge), and the rail
    /// stood 0.84 m inside the road. A roadside barrier whose line snapped to the outermost line is put at the road's edge.
    /// </summary>
    internal static class RoadsideSnapTests
    {
        public static void RunAll()
        {
            Program.Run("roadside snap: the owner's road, the edge line is 0.49 m inside the edge a barrier is given", OwnersRoad);
            Program.Run("roadside snap: only the outermost line of a side is taken to the edge", OnlyTheOutermost);
            Program.Run("roadside snap: the cursor's own place, a kerb strip, no lanes and a left side", Unchanged);
            Program.Run("roadside snap: the edge of a road that is not a chain yet is the chain's", EdgeOfRoadIsEdgeOf);
            Program.Run("roadside snap: the log says where the game's surface reaches", GameEdgesInTheLog);
        }

        private static readonly float[] kTwoLanes = { 1.83f };

        private static MarkingSpot Snap(IList<float> with, IList<float> against, float cursor) => MarkingSnap.BarrierLine(with, against, null, cursor);

        private static void OwnersRoad()
        {
            // Lanes 1.83 m each side of the centre line: the outer lines at +-3.66 m (the log's +3.66), the road 4.5 m a side: the edge of a barrier 4.15 m.
            var with = new List<float> { 1.83f };
            var against = new List<float> { -1.83f };
            float edge = BarrierAuto.EdgeOfRoad(4.5f, with, against, 1);
            Program.Check(Math.Abs(edge - 4.15f) < 1e-4f, $"the edge of his road is 4.15 m: {edge:0.000}");
            MarkingSpot spot = Snap(with, against, 3.4f);
            Program.Check(spot.Snapped && Math.Abs(spot.Lateral - 3.66f) < 0.01f, $"a click at 3.4 m snaps to the lane's edge line, 3.66 m (the log's): {spot.Lateral:0.00}");
            Program.Check(Math.Abs(edge - spot.Lateral - 0.49f) < 0.01f, $"which is 0.49 m inside the barrier's edge, as his log says: {edge - spot.Lateral:0.00} m");
            MarkingSpot moved = BarrierAuto.RoadsideSpot(spot, with, against, edge);
            Program.Check(Math.Abs(moved.Lateral - 4.15f) < 1e-4f && moved.Snapped && moved.Forward == spot.Forward && moved.HadLanes == spot.HadLanes && !moved.OnRaised, $"the barrier is put at the edge: {moved.Lateral:0.00} m, still snapped, the side kept");
            // The click anywhere that snaps to that line does it: from 2.76 to 4.56 m (0.9 m each way).
            bool all = true;
            for (float cursor = 2.8f; cursor <= 4.5f; cursor += 0.1f)
            {
                MarkingSpot s = Snap(with, against, cursor);
                all &= Math.Abs(BarrierAuto.RoadsideSpot(s, with, against, edge).Lateral - 4.15f) < 1e-4f;
            }

            Program.Check(all, "every click within the snap range of that line is taken to the edge");
            // The offset the tool works out from the edge is then 0, and the player's own is added to it.
            Program.Check(Math.Abs(Math.Abs(moved.Lateral) - edge) < 1e-4f, "so the barrier's offset from the road's edge is 0");
        }

        private static void OnlyTheOutermost()
        {
            // Four lanes, two each way, a double line in the middle: places at -7.25, -3.75, 0, 3.75, 7.25. Only the outer two are the edge lines.
            var with = new List<float> { 1.75f, 5.25f };
            var against = new List<float> { -1.75f, -5.25f };
            float edge = BarrierAuto.EdgeOfRoad(8.5f, with, against, 1);
            Program.Check(Math.Abs(edge - 8.15f) < 1e-4f, $"the edge of this road: {edge:0.00}");
            foreach (float place in new[] { -3.5f, 0f, 3.5f })
            {
                MarkingSpot s = Snap(with, against, place);
                MarkingSpot r = BarrierAuto.RoadsideSpot(s, with, against, edge);
                Program.Check(s.Snapped && Math.Abs(r.Lateral - s.Lateral) < 1e-5f, $"a line between lanes at {s.Lateral:+0.00;-0.00} m stays where it is");
            }

            MarkingSpot right = BarrierAuto.RoadsideSpot(Snap(with, against, 6.9f), with, against, edge);
            MarkingSpot left = BarrierAuto.RoadsideSpot(Snap(with, against, -6.9f), with, against, BarrierAuto.EdgeOfRoad(8.5f, with, against, -1));
            Program.Check(Math.Abs(right.Lateral - 8.15f) < 1e-4f && Math.Abs(left.Lateral + 8.15f) < 1e-4f, $"the outer line of each side goes to its edge: {left.Lateral:+0.00;-0.00} and {right.Lateral:+0.00;-0.00} m");
            // A one-way road: all its lanes run with the segment, two of them either side of the centre line: the outer lines are at -3.5 and +3.5 m, and each goes to its own edge.
            var oneWay = new List<float> { -1.75f, 1.75f };
            MarkingSpot leftLine = Snap(oneWay, null, -3.4f), rightLine = Snap(oneWay, null, 3.4f);
            MarkingSpot movedLeft = BarrierAuto.RoadsideSpot(leftLine, oneWay, null, BarrierAuto.EdgeOfRoad(6f, oneWay, null, -1));
            MarkingSpot movedRight = BarrierAuto.RoadsideSpot(rightLine, oneWay, null, BarrierAuto.EdgeOfRoad(6f, oneWay, null, 1));
            Program.Check(leftLine.Snapped && Math.Abs(leftLine.Lateral + 3.5f) < 0.01f && Math.Abs(movedLeft.Lateral + 5.65f) < 1e-4f && Math.Abs(movedRight.Lateral - 5.65f) < 1e-4f, $"a one-way road: the outer lines at -3.50 and +3.50 m go to the edges, {movedLeft.Lateral:+0.00;-0.00} and {movedRight.Lateral:+0.00;-0.00} m");
            // The left edge line of lanes that all lie on one side of the curve is the lanes' own edge, 0.00 m: it is not an edge of the road, and the line stays.
            var oneSide = new List<float> { 1.75f, 5.25f };
            MarkingSpot inner = Snap(oneSide, null, -0.3f);
            Program.Check(inner.Snapped && Math.Abs(BarrierAuto.RoadsideSpot(inner, oneSide, null, 7f).Lateral - inner.Lateral) < 1e-5f, "lanes all on one side of the curve: the line at its inner edge stays");
        }

        private static void Unchanged()
        {
            var with = new List<float> { 1.83f };
            var against = new List<float> { -1.83f };
            float edge = 4.15f;
            // The cursor beyond the snap range of any line: its own place, not snapped, as it was.
            MarkingSpot free = Snap(with, against, 4.9f);
            Program.Check(!free.Snapped && Math.Abs(BarrierAuto.RoadsideSpot(free, with, against, edge).Lateral - 4.9f) < 1e-5f, "a click that took the cursor's own place (4.9 m) is as it was");
            MarkingSpot between = Snap(with, against, 2.3f);
            Program.Check(!between.Snapped && Math.Abs(BarrierAuto.RoadsideSpot(between, with, against, edge).Lateral - 2.3f) < 1e-5f, "so is one between the lines");
            // On a kerb strip: it keeps its place.
            MarkingSpot onKerb = MarkingSnap.BarrierLine(with, against, new List<(float Lo, float Hi)> { (-0.4f, 0.4f) }, 0.2f);
            Program.Check(onKerb.OnRaised && Math.Abs(BarrierAuto.RoadsideSpot(onKerb, with, against, edge).Lateral - onKerb.Lateral) < 1e-5f, "a barrier on a kerb strip keeps it");
            // Not snapped, and on the outermost line by chance (a spot made by hand, not by BarrierLine): not an edge line, it stays. A kerb strip on that very line stays too.
            var atLine = new MarkingSpot { Lateral = 3.66f, Snapped = false, HadLanes = true, Forward = true };
            Program.Check(Math.Abs(BarrierAuto.RoadsideSpot(atLine, with, against, edge).Lateral - 3.66f) < 1e-5f, "a spot that was not snapped, at the place of the outer line: it stays");
            MarkingSpot strip = MarkingSnap.BarrierLine(with, against, new List<(float Lo, float Hi)> { (3.46f, 3.86f) }, 3.6f);
            Program.Check(strip.OnRaised && Math.Abs(strip.Lateral - 3.66f) < 0.01f && Math.Abs(BarrierAuto.RoadsideSpot(strip, with, against, edge).Lateral - strip.Lateral) < 1e-5f, $"a barrier on a kerb strip at the place of the outer line ({strip.Lateral:0.00} m): it stays");
            // Snapped but no lanes to say where the lines are: nothing to move it by.
            var lost = new MarkingSpot { Lateral = 1.5f, Snapped = true, HadLanes = true, Forward = true };
            Program.Check(Math.Abs(BarrierAuto.RoadsideSpot(lost, null, null, edge).Lateral - 1.5f) < 1e-5f && Math.Abs(BarrierAuto.RoadsideSpot(lost, new List<float>(), new List<float>(), edge).Lateral - 1.5f) < 1e-5f, "a snapped spot and no lanes: it stays");
            // No lanes known: the cursor.
            MarkingSpot none = Snap(null, null, 3.0f);
            Program.Check(!none.Snapped && Math.Abs(BarrierAuto.RoadsideSpot(none, null, null, edge).Lateral - 3.0f) < 1e-5f, "no lanes known: the cursor's place");
            // An unusable edge: as it was.
            MarkingSpot s = Snap(with, against, 3.5f);
            Program.Check(Math.Abs(BarrierAuto.RoadsideSpot(s, with, against, 0f).Lateral - s.Lateral) < 1e-5f && Math.Abs(BarrierAuto.RoadsideSpot(s, with, against, float.NaN).Lateral - s.Lateral) < 1e-5f, "an edge of 0 or none: the line stays");
            // The road's own line at the middle, on the centre line: not an edge.
            MarkingSpot centre = Snap(with, against, 0.2f);
            Program.Check(centre.Snapped && Math.Abs(BarrierAuto.RoadsideSpot(centre, with, against, edge).Lateral - centre.Lateral) < 1e-5f, "the middle line is not an edge");
        }

        private static ChainPiece Piece(float half, params float[] lanes)
        {
            var piece = new ChainPiece { Curve = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f)), Reversed = false, Kmh = 50f, HasSpeed = true, After = ChainJoint.End, HalfWidth = half };
            foreach (float lane in lanes)
                (lane >= 0f ? piece.Forward : piece.Backward).Add(lane);
            piece.ReaderJoint = ChainJoint.End;
            return piece;
        }

        private static void EdgeOfRoadIsEdgeOf()
        {
            ChainPiece piece = Piece(4.5f, 1.83f, -1.83f);
            var chain = new RoadChain(new[] { piece });
            foreach (int side in new[] { -1, 1 })
            {
                float fromChain = new EdgeProfile(chain, side).At(50f), direct = BarrierAuto.EdgeOfRoad(4.5f, new List<float> { 1.83f }, new List<float> { -1.83f }, side);
                Program.Check(Math.Abs(fromChain - direct) < 1e-4f, $"side {side:+0;-0}: the chain's profile {fromChain:0.000}, the road as read {direct:0.000}");
            }

            // The lanes count when they reach out past the half width less the inset: lanes at 4.2 m in a road of 6 m a side put the edge at 6 m (the road's), either side.
            var wide = new List<float> { 4.2f };
            var wideAgainst = new List<float> { -4.2f };
            Program.Check(Math.Abs(BarrierAuto.EdgeOfRoad(6f, wide, wideAgainst, 1) - 6f) < 1e-4f && Math.Abs(BarrierAuto.EdgeOfRoad(6f, wide, wideAgainst, -1) - 6f) < 1e-4f, "lanes out at 4.2 m each side: the edge is the road's 6 m on both, the lists of both ways are used");
            // No lane list: still an edge (the half width less the inset), and a missing list is no failure.
            Program.Check(Math.Abs(BarrierAuto.EdgeOfRoad(6f, null, null, 1) - 5.65f) < 1e-4f, "a road with no lanes known: the half width less the inset");
            // A road narrower than 2 m a side is read as 2 m (the chain does the same).
            Program.Check(Math.Abs(BarrierAuto.EdgeOfRoad(1f, null, null, -1) - 1.65f) < 1e-4f, "a road of less than 2 m a side is taken as 2 m");
        }

        private static void GameEdgesInTheLog()
        {
            ChainPiece piece = Piece(4.5f, 1.83f, -1.83f);
            piece.GameLeft = 4.5f;
            piece.GameRight = 4.62f;
            var chain = new RoadChain(new[] { piece });
            string text = chain.Describe(0f, 100f);
            Program.Check(text.Contains("edge L 4.15 R 4.15 (game surface L 4.50 R 4.62)"), "a piece with the game's sides says them next to the barrier's edges: " + text.Substring(0, Math.Min(text.Length, 140)));
            ChainPiece half = Piece(4.5f, 1.83f, -1.83f);
            half.GameLeft = 4.5f;
            Program.Check(!new RoadChain(new[] { half }).Describe(0f, 100f).Contains("game surface"), "one with only one side read says nothing of them");
            ChainPiece plain = Piece(4.5f, 1.83f, -1.83f);
            Program.Check(!new RoadChain(new[] { plain }).Describe(0f, 100f).Contains("game surface"), "one with no sides read says nothing of them");
            // A piece read against its direction swaps them: its left is the chain's right.
            ChainPiece reversed = Piece(4.5f, 1.83f, -1.83f);
            reversed.Reversed = true;
            reversed.GameLeft = 4.5f;
            reversed.GameRight = 4.62f;
            string text2 = new RoadChain(new[] { reversed }).Describe(0f, 100f);
            Program.Check(text2.Contains("game surface L 4.62 R 4.50"), "a piece the chain runs against swaps them: " + text2.Substring(0, Math.Min(text2.Length, 140)));
        }
    }
}
