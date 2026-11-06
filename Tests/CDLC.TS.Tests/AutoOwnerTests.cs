using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// Whose an automatic marking is (the owner, 4 Nov: "地面自动标线，标了前半截之后后边就没了，后边标上前边又没了"). Two segments marked one after the other share the node between them: its area is in
    /// the areas of both, each starts its lines at the middle of that node, and marking either took away the lines that the other started there, as the earlier marking of the same stretch
    /// (PlacementTool.TakeStretch judged an item by where it starts). Here the two segments are drawn as the tool places them (AutoMarking.Plan, RoadChain.LineParts, MarkingRuns.Items, the run's settings
    /// as PlaceAuto writes them), their areas are put round them, and what each marking would take is counted by the rule that was and by the rule that is.
    /// </summary>
    internal static class AutoOwnerTests
    {
        public static void RunAll()
        {
            Program.Run("auto owner: a mark is where it stands, a line is where its middle is", Where);
            Program.Run("auto owner: marking one segment leaves the lines of the one beside it, and takes its own", Neighbours);
            Program.Run("auto owner: a long line's items belong to the pieces they lie on, within the search's reach", LongLine);
        }

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        private static readonly float[] Two = { 1.75f, 5.25f }, TwoBack = { -1.75f, -5.25f };

        // A segment alone as ChainReader reads it between two plain nodes: its lines reach the middle of the node at each end (LaneFrom 0, LaneTo the segment's end).
        private static RoadChain Segment(Vec3 from, Vec3 to)
        {
            var piece = new ChainPiece { Curve = Bezier3.Line(from, to), Kmh = 50f, HasSpeed = true, After = ChainJoint.End, LaneFrom = 0f, LaneTo = float.MaxValue };
            piece.Forward.AddRange(Two);
            piece.Backward.AddRange(TwoBack);
            return new RoadChain(new[] { piece });
        }

        private static AutoOptions Options()
        {
            AutoOptions o = AutoOptions.Default;
            o.Edges = EdgeLines.All;
            return o;
        }

        // The items PlaceAuto places for the lines of a plan: an ItemRecord for each, with the spec and the place the item is written with, marked automatic.
        private static List<ItemRecord> Placed(RoadChain chain)
        {
            var items = new List<ItemRecord>();
            AutoPlan plan = AutoMarking.Plan(chain, null, Options());
            foreach (AutoRun run in plan.Runs)
            {
                SignDef def = SignCatalog.ById(run.Id);
                GuideTemplate t = def?.Template;
                if (t == null)
                    continue;
                GuideSpec spec = t.Default();
                foreach (KeyValuePair<string, string> kv in run.Settings)
                    spec = spec.With(kv.Key, kv.Value);
                foreach (RunItem item in MarkingRuns.Items(chain.LineParts(run.S0, run.S1, run.LateralAt, 0f), out float total))
                    items.Add(new ItemRecord(def.Hash, item.Position, item.Yaw, 0u, MarkingTemplates.WithRun(t, spec.ToString(), item.Path, item.Start, total)) { Auto = true });
            }

            return items;
        }

        // A rectangle of the ground along z, as the strip of a piece of a segment (grown by half a metre as ItemsOnPiece does).
        private static ClearRegion Strip(float halfWidth, float z0, float z1, ClearScope scope) =>
            new ClearRegion(VanillaClear.Grow(new List<Vec3> { P(-halfWidth, z0), P(halfWidth, z0), P(halfWidth, z1), P(-halfWidth, z1) }, 0.5f), scope, false);

        private static int Taken(List<ItemRecord> items, List<ClearRegion> areas, bool byMiddle)
        {
            int count = 0;
            foreach (ItemRecord item in items)
            {
                foreach (ClearRegion area in areas)
                {
                    if (byMiddle ? AutoOwner.Belongs(item, area) : area.Contains(item.Position))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private static void Where()
        {
            // A mark has no path: its place. A line of 56 m starting at the origin heading +z: its middle is 28 m on.
            var stamp = new ItemRecord(SignCatalog.ById("mk.line.lane").Hash, P(3f, 4f), 0.7f, 0u, null) { Auto = true };
            Vec3 at = AutoOwner.Where(stamp);
            Program.Check(at.X == 3f && at.Z == 4f, "no spec, no path: where it stands");
            List<ItemRecord> line = Placed(Segment(P(0f, 0f), P(0f, 56f)));
            Program.Check(line.Count > 0, "the plan has lines: " + line.Count);
            foreach (ItemRecord item in line)
            {
                Vec3 middle = AutoOwner.Where(item);
                Program.Near(middle.Z, 28f, 0.2f, "the middle of a 56 m line that starts at 0: " + middle.Z);
                Program.Near(middle.X, item.Position.X, 0.05f, "on its own lateral");
            }

            // Turned: a line starting at (10, 10) and heading -z (yaw pi) has its middle 28 m toward -z of it.
            List<ItemRecord> back = Placed(Segment(P(10f, 10f), P(10f, -46f)));
            foreach (ItemRecord item in back)
                Program.Near(AutoOwner.Where(item).Z, 10f - 28f, 0.2f, "heading -z: the middle is 28 m the other way");
            Program.Near(AutoOwner.Reach, 50f, 1e-4f, "the reach is half of the longest item");
        }

        private static void Neighbours()
        {
            // Both segments start at the node at the origin and run away from each other: A 56 m toward +z, B 176 m toward -z (the owner's two segments: his log has both started at the node they share).
            List<ItemRecord> a = Placed(Segment(P(0f, 0f), P(0f, 56f)));
            List<ItemRecord> b = Placed(Segment(P(0f, 0f), P(0f, -176f)));
            Program.Check(a.Count >= 5 && b.Count >= 5, $"each has its lines: {a.Count} and {b.Count} items");
            // The node's hull (4 m each side of its middle: a plain node is short) and each segment's strip between its nodes.
            ClearRegion node = Strip(9f, -4f, 4f, ClearScope.Nodes);
            var areasA = new List<ClearRegion> { Strip(8.8f, 4f, 52f, ClearScope.Edges), node, Strip(9f, 52f, 60f, ClearScope.Nodes) };
            var areasB = new List<ClearRegion> { Strip(8.8f, -172f, -4f, ClearScope.Edges), node, Strip(9f, -180f, -172f, ClearScope.Nodes) };

            // The rule that was: where an item starts. Marking B took the lines A started at the node, marking A those of B.
            int oldA = Taken(a, areasB, false), oldB = Taken(b, areasA, false);
            Program.Check(oldA == a.Count && oldB >= 5 && oldB <= b.Count, $"by where it starts, marking B takes {oldA} of A's {a.Count} items and marking A takes {oldB} of B's {b.Count}: the defect");

            // The rule that is: where the middle is. Neither takes any of the other's, and each takes all of its own.
            Program.Check(Taken(a, areasB, true) == 0, "marking B leaves every line of A");
            Program.Check(Taken(b, areasA, true) == 0, "marking A leaves every line of B");
            Program.Check(Taken(a, areasA, true) == a.Count, "marking A again takes all of A's own: " + Taken(a, areasA, true) + " of " + a.Count);
            Program.Check(Taken(b, areasB, true) == b.Count, "marking B again takes all of B's own: " + Taken(b, areasB, true) + " of " + b.Count);

            // The other way round: the segments run toward the node, so that their lines end in it and start at their far ends.
            List<ItemRecord> c = Placed(Segment(P(0f, 56f), P(0f, 0f)));
            List<ItemRecord> d = Placed(Segment(P(0f, -176f), P(0f, 0f)));
            Program.Check(Taken(c, areasB, true) == 0 && Taken(d, areasA, true) == 0 && Taken(c, areasA, true) == c.Count && Taken(d, areasB, true) == d.Count, "segments that end at the node: the same");
        }

        private static void LongLine()
        {
            // The store is searched by where an item starts: the middle of a line is never further from it than the reach, so that a piece's search (its radius and the reach) finds every line whose middle is on it.
            List<ItemRecord> items = Placed(Segment(P(0f, 0f), P(0f, -176f)));
            int longer = 0;
            foreach (ItemRecord item in items)
            {
                float away = Vec3.DistanceXZ(item.Position, AutoOwner.Where(item));
                Program.Check(away <= AutoOwner.Reach + 0.5f, "the middle is " + away + " m from the start");
                if (away > 30f)
                    longer++;
            }

            Program.Check(longer > 0, "the plan has items of more than 60 m (a 176 m line is cut in 64 m items: the middle 32 m from the start): " + longer);
            // Two pieces of 100 m (kClearPiece): of a line of 176 m cut in items of 64 m the first two items (middles at 32 and 96 m) are on the first piece, the third (middle at 152 m) on the second.
            ClearRegion first = Strip(8.8f, -100f, 0f, ClearScope.Edges), second = Strip(8.8f, -176f, -100f, ClearScope.Edges);
            int inFirst = 0, inSecond = 0, inBoth = 0;
            foreach (ItemRecord item in items)
            {
                bool one = AutoOwner.Belongs(item, first), two = AutoOwner.Belongs(item, second);
                if (one && two)
                    inBoth++;
                else if (one)
                    inFirst++;
                else if (two)
                    inSecond++;
            }

            Program.Check(inFirst == 10 && inSecond == 5 && inBoth == 0, $"each item is on one piece: {inFirst} on the first, {inSecond} on the second, {inBoth} on both, of {items.Count}");
        }
    }
}
