using System;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.100 (the owner, 5 Nov 21:4x: "我道路删除了，之前在上边的隔音墙却还在"): the log has the wall (Placed bar.noise at (-2761.321, -4623.722), one item of 28.6 m, on road 311936:1 at 1293.7 m, -11.95 m from
    /// the centre line, the road 24 m wide and 48 m long, the last segment of its chain: from (-2748.1, -4605.1) to (-2792.3, -4623.9)); it had been placed 2 s before the road went, and no "removed with
    /// it" line follows. The segment before the deleted one ends at that node: the wall is 22.8 m from it, inside the 24 m that "beside" gives (half the width, 12 m, and 12 m more), and the wall was
    /// relinked to a road in reach. A road covers an item that is past its END by only a little: EdgeHint.TakesOver.
    /// </summary>
    internal static class TakeOverTests
    {
        public static void RunAll()
        {
            Program.Run("take over: the owner's wall past the end of the segment before the deleted one is not taken over", OwnersWall);
            Program.Run("take over: a road that replaces the old one (a split, two edges joined) keeps what stood on it", Replacement);
            Program.Run("take over: past an end by up to the reach, either end, and not beyond it", Ends);
            Program.Run("take over: beside a road as before: the width, the 12 m, the curve", Beside);
        }

        private static Bezier3 Straight(Vec3 a, Vec3 d) => Bezier3.Line(a, d);

        private static void OwnersWall()
        {
            // The segment before the deleted one: along the same line, ending at the node (-2748.1, -4605.1), 24 m wide; the deleted segment ran on from there to (-2792.3, -4623.9).
            Vec3 node = new Vec3(-2748.1f, 404.8f, -4605.1f), tail = new Vec3(-2792.3f, 404.7f, -4623.9f);
            Vec3 along = (tail - node).HeadingXZ();
            Bezier3 neighbour = Straight(node - along * 140f, node);
            var wall = new Vec3(-2761.321f, 404.784f, -4623.722f);
            Program.Check(Vec3.DistanceXZ(wall, node) > 22.5f && Vec3.DistanceXZ(wall, node) < 23.1f, $"the wall is 22.8 m from the node: {Vec3.DistanceXZ(wall, node):0.0}");
            Program.Check(EdgeHint.IsBeside(neighbour, 24f, wall, out _, out float distance) && distance < 24f, $"which is beside the neighbour by the old rule (reach 24 m): {distance:0.0} m");
            Program.Check(!EdgeHint.TakesOver(neighbour, 24f, wall, out float t, out _, out float overrun), "and the neighbour does not take it over");
            Program.Check(t > 0.999f && overrun > 18.5f && overrun < 20.5f, $"the wall is {overrun:0.0} m past its end (19.4 worked out by hand), t {t:0.0000}");
            // A neighbour that is narrower has the same answer a fortiori (the old rule's reach is 6 + 12 = 18 m: not beside either).
            Program.Check(!EdgeHint.IsBeside(neighbour, 12f, wall, out _, out _) && !EdgeHint.TakesOver(neighbour, 12f, wall, out _, out _, out _), "a neighbour of 12 m: not beside, not taken over");
            // The wall's end, 28.6 m on along the deleted segment, is farther still.
            var farther = wall + along * 28.6f;
            Program.Check(!EdgeHint.TakesOver(neighbour, 24f, farther, out _, out _, out _), "and a wall that started farther on is not taken over either");
        }

        private static void Replacement()
        {
            // A road of 100 m along +Z is replaced by two: 0 to 40 and 40 to 100 (a node put on it). An item at 20 m beside it, one at 40 m (the node), one at 70 m, and one at 45 m 12 m to the side: each is
            // taken over by one of the two, with the same width.
            Bezier3 a = Straight(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 40f)), b = Straight(new Vec3(0f, 0f, 40f), new Vec3(0f, 0f, 100f));
            foreach (float z in new[] { 20f, 40f, 70f, 45f })
            {
                foreach (float side in new[] { 0f, 8f, 12f, -12f })
                {
                    var at = new Vec3(side, 0f, z);
                    bool one = EdgeHint.TakesOver(a, 24f, at, out _, out _, out _);
                    bool two = EdgeHint.TakesOver(b, 24f, at, out _, out _, out _);
                    Program.Check(one || two, $"an item at {z:0} m along and {side:+0;-0;0} m to the side is taken over by one of the two new edges");
                }
            }

            // At the node exactly, at the lateral of the edge of a road 24 m wide (the owner's wall's place): both edges end there, overrun 0, both take it over.
            var atNode = new Vec3(11.95f, 0f, 40f);
            bool firstTakes = EdgeHint.TakesOver(a, 24f, atNode, out _, out _, out float firstOverrun);
            bool secondTakes = EdgeHint.TakesOver(b, 24f, atNode, out _, out _, out float secondOverrun);
            Program.Check(firstTakes && secondTakes && firstOverrun < 0.01f && secondOverrun < 0.01f, $"at the node itself, at the edge of the road: both take it over, overrun {firstOverrun:0.00} and {secondOverrun:0.00}");
            // A road made again over the same line and a little shorter at each end (the node moved by 3 m): the items near the ends are within the reach.
            Bezier3 shorter = Straight(new Vec3(0f, 0f, 3f), new Vec3(0f, 0f, 97f));
            Program.Check(EdgeHint.TakesOver(shorter, 24f, new Vec3(5f, 0f, 1f), out _, out _, out _) && EdgeHint.TakesOver(shorter, 24f, new Vec3(5f, 0f, 99f), out _, out _, out _), "an item 2 m past each end of a road made again 3 m shorter: taken over");
        }

        private static void Ends()
        {
            Bezier3 road = Straight(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f));
            Program.Check(EdgeHint.TakesOver(road, 24f, new Vec3(4f, 0f, 105.9f), out _, out _, out float o1) && Math.Abs(o1 - 5.9f) < 0.01f, $"5.9 m past the far end, 4 m aside: taken over (overrun {o1:0.00})");
            Program.Check(!EdgeHint.TakesOver(road, 24f, new Vec3(4f, 0f, 106.1f), out _, out _, out float o2) && Math.Abs(o2 - 6.1f) < 0.01f, $"6.1 m past it: not (overrun {o2:0.00})");
            Program.Check(EdgeHint.TakesOver(road, 24f, new Vec3(-4f, 0f, -5.9f), out _, out _, out float o3) && Math.Abs(o3 - 5.9f) < 0.01f, $"5.9 m past the near end: taken over (overrun {o3:0.00})");
            Program.Check(!EdgeHint.TakesOver(road, 24f, new Vec3(-4f, 0f, -6.1f), out _, out _, out float o4) && Math.Abs(o4 - 6.1f) < 0.01f, $"6.1 m past it: not (overrun {o4:0.00})");
            Program.Check(!EdgeHint.TakesOver(road, 24f, new Vec3(0f, 0f, -19.4f), out _, out _, out _), "19.4 m past the near end: not");
            // On a bend the way out of the end is the tangent there, not the chord: a quarter circle of 60 m radius ending heading +X, an item 4 m on along +X and 5 m off its line.
            Bezier3 bend = new Bezier3(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 33.1f), new Vec3(26.9f, 0f, 60f), new Vec3(60f, 0f, 60f));
            Program.Check(EdgeHint.TakesOver(bend, 24f, new Vec3(64f, 0f, 63f), out float t, out _, out float bo) && t > 0.999f && Math.Abs(bo - 4f) < 0.3f, $"on a bend, 4 m past the end along its tangent: taken over, overrun {bo:0.0}");
            Program.Check(!EdgeHint.TakesOver(bend, 24f, new Vec3(70f, 0f, 63f), out _, out _, out float far) && far > 9f, $"10 m past it: not, overrun {far:0.0}");
            // An item beside the road's middle, a long way from both ends: always taken over, however far it is from the ends.
            Program.Check(EdgeHint.TakesOver(road, 24f, new Vec3(20f, 0f, 50f), out float midT, out _, out float mid) && mid == 0f && midT > 0.4f && midT < 0.6f, "beside the middle: taken over, no overrun, the parameter is in the middle");
            // An item BEHIND the end (inside the road's length, close to its end and on its centre line) has an interior projection or an end with overrun 0.
            Program.Check(EdgeHint.TakesOver(road, 24f, new Vec3(0f, 0f, 99.99f), out _, out _, out float inside) && inside < 0.01f, $"an item on the line just inside the end: taken over, overrun {inside:0.0000}");
        }

        private static void Beside()
        {
            Bezier3 road = Straight(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f));
            Program.Check(EdgeHint.IsBeside(road, 24f, new Vec3(23.9f, 0f, 50f), out _, out _) && !EdgeHint.IsBeside(road, 24f, new Vec3(24.1f, 0f, 50f), out _, out _), "24 m road: beside up to 24 m from the centre line (12 + 12)");
            Program.Check(EdgeHint.IsBeside(road, 6f, new Vec3(15f, 0f, 50f), out _, out _) && !EdgeHint.IsBeside(road, 6f, new Vec3(15.1f, 0f, 50f), out _, out _), "6 m road: beside up to 15 m (3 + 12)");
            Program.Check(!EdgeHint.TakesOver(road, 24f, new Vec3(24.1f, 0f, 50f), out _, out _, out _) && EdgeHint.TakesOver(road, 24f, new Vec3(23.9f, 0f, 50f), out _, out _, out _), "taken over where beside and covered along: the same 24 m");
            EdgeHint.IsBeside(road, 24f, new Vec3(7f, 0f, 30f), out float t, out float d);
            Program.Check(Math.Abs(t - 0.3f) < 0.01f && Math.Abs(d - 7f) < 0.01f, $"the parameter and the distance: {t:0.000}, {d:0.00}");
            // Height does not count (the plane only): an item 40 m above the road is as beside as one on it.
            Program.Check(EdgeHint.IsBeside(road, 24f, new Vec3(5f, 40f, 50f), out _, out _), "height is left to RoadLevels, not to this rule");
        }
    }
}
