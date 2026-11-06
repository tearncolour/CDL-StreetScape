using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// Clearing the game's markings (0.12.0, mk.clear): which markings an area takes, the outlines read from a road segment or a junction, where two sets of areas differ, the item itself (no paint, an
    /// outline that can be pointed at under any paint), and the standard's lines drawn after it.
    /// </summary>
    internal static class VanillaClearTests
    {
        public static void RunAll()
        {
            Program.Run("clear: an area takes the markings whose middle is in it, of its scope, keeping the lines across", Regions);
            Program.Run("clear: the area of an item, and where two sets of areas differ", ItemsAndChanges);
            Program.Run("clear: outlines grown, of a segment's sides and of a junction's points", Outlines);
            Program.Run("clear: the item has no paint, an outline in its picture, and is pointed at under paint", Item);
            Program.Run("clear: the standard's lines from the lanes and the speed", Standard);
            Program.Run("clear: the lines of help say what a click clears", Hints);
            Program.Run("island corners: the points of placed markings, and the sides along a line", SnapPoints);
            Program.Run("automatic marking: taken down for now, its entry unlisted and the clear tool's redraw off (0.12.106)", AutoMarkingTakenDown);
        }

        /// <summary>
        /// The owner, 7 Nov: "暂时先把标线中自动标线下了吧，后边全部交给ARL拓展实现". The automatic marking (mk.auto, "重标") is not offered: its panel entry is a hidden choice, the clear tool's "then mark it by the
        /// standard" does not show; its definition and template are still there (the tool's own paths read them), and a switch (AutoMarking.Offered) puts it back.
        /// </summary>
        private static void AutoMarkingTakenDown()
        {
            Program.Check(!AutoMarking.Offered, "the automatic marking is not on offer by default");
            SignChoice auto = SignCatalog.ChoiceById(AutoMarking.Id);
            Program.Check(auto != null && auto.Hidden, "its entry is a hidden choice: not listed in the panel");
            Program.Check(SignCatalog.ById(AutoMarking.Id) != null && SignCatalog.ById(AutoMarking.Id).Template != null && SignCatalog.ById(AutoMarking.Id).Template.Fields.Length > 5, "but its definition and template are there: the tool's own paths do not meet a null");
            // Only the bare custom sign (hidden since 0.12.29) and the automatic marking are hidden: nothing else was taken down with it.
            var hidden = new List<string>();
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                if (choice.Hidden)
                    hidden.Add(choice.Id);
            }

            Program.Check(hidden.Count == 2 && hidden.Contains(AutoMarking.Id), "exactly two choices are hidden, the automatic marking and the bare custom sign: " + string.Join(", ", hidden));
            Program.Check(!SignCatalog.ChoiceById(VanillaClear.Id).Hidden && !SignCatalog.ChoiceById(BarrierAuto.Id).Hidden, "the clear tool and the automatic barrier (by the kind of road) are still listed");

            // The clear tool's "then mark it by the standard" is not shown; with the switch on it is, as it was.
            GuideTemplate clear = SignCatalog.ById(VanillaClear.Id).Template;
            GuideField redraw = clear.FieldOf(VanillaClear.RedrawKey);
            GuideSpec segment = clear.Parse(VanillaClear.ModeKey + "=e"), road = clear.Parse(VanillaClear.ModeKey + "=r"), junction = clear.Parse(VanillaClear.ModeKey + "=n");
            Program.Check(redraw != null && !redraw.Visible(segment) && !redraw.Visible(road) && !redraw.Visible(junction), "the clear tool does not show \"then mark it by the standard\" for a segment, a road or a junction");
            bool was = AutoMarking.Offered;
            try
            {
                AutoMarking.Offered = true;
                Program.Check(redraw.Visible(segment) && redraw.Visible(road) && !redraw.Visible(junction), "with the switch on it shows for a segment and a road, not for a junction, as it did");
                SignChoice listed = SignCatalog.ChoiceById(AutoMarking.Id);
                Program.Check(listed != null && !listed.Hidden && SignCatalog.Choices.Count(choice => choice.Hidden) == 1, "with the switch on the automatic marking is listed again (the catalogue is made again), only the bare custom sign stays hidden");
            }
            finally
            {
                AutoMarking.Offered = was;
            }

            Program.Check(!redraw.Visible(segment) && SignCatalog.ChoiceById(AutoMarking.Id).Hidden, "and with it off again the field and the entry are gone again");
            // A clear item of before, with redraw on in its settings, keeps its text (the key is still a field).
            Program.Check(clear.Parse(VanillaClear.ModeKey + "=e;" + VanillaClear.RedrawKey + "=1").Flag(VanillaClear.RedrawKey), "the redraw key is still a field of the clear tool: a text that has it reads as it did");
        }

        private static void SnapPoints()
        {
            // An island: its corners in the world.
            GuideTemplate island = MarkingTemplates.ById("mk.channel");
            var local = new List<Vec3> { P(0f, 0f), P(2f, 10f), P(-2f, 10f) };
            var item = new ItemRecord(island.Hash, new Vec3(50f, 3f, 60f), (float)(Math.PI / 2.0), 0u, MarkingTemplates.WithPolygon(island, island.Default().ToString(), MarkingPolygon.Encode(local)));
            List<Vec3> corners = MarkingSnapPoints.Of(SignCatalog.ById("mk.channel"), item);
            Program.Check(corners.Count == 3 && Vec3.DistanceXZ(corners[0], P(50f, 60f)) < 1e-3f && Vec3.DistanceXZ(corners[1], P(60f, 58f)) < 1e-3f && corners[1].Y == 3f, "an island: its corners in the world, at its height: " + string.Join(" ", corners));
            // A piece of a line: its two ends.
            GuideTemplate lane = MarkingTemplates.ById("mk.line.lane");
            string path = RunPath.Encode(new List<Vec3> { P(0f, 0f), P(0f, 10f), P(3f, 20f) }, out float yaw);
            var line = new ItemRecord(lane.Hash, new Vec3(100f, 0f, 100f), yaw, 0u, MarkingTemplates.WithPath(lane, lane.Default().ToString(), path));
            List<Vec3> ends = MarkingSnapPoints.Of(SignCatalog.ById("mk.line.lane"), line);
            Program.Check(ends.Count == 2 && Vec3.DistanceXZ(ends[0], P(100f, 100f)) < 1e-3f && Vec3.DistanceXZ(ends[1], P(103f, 120f)) < 0.02f, "a line: its two ends: " + string.Join(" ", ends));
            // A stop line 7 m across: the corners of its box.
            GuideTemplate stop = MarkingTemplates.ById("mk.stop");
            var stopItem = new ItemRecord(stop.Hash, new Vec3(0f, 0f, 0f), 0f, 0u, stop.Default().ToString());
            List<Vec3> box = MarkingSnapPoints.Of(SignCatalog.ById("mk.stop"), stopItem);
            MarkingPlan stopPlan = MarkingTemplates.PlanOf(stop, stop.Default().ToString());
            Program.Check(box.Count == 4 && Math.Abs(box[0].X - stopPlan.MinX) < 1e-4f && Math.Abs(box[2].Z - stopPlan.MaxZ) < 1e-4f, "a stamp: the corners of its paint's box");
            // An area that clears the game's markings, another type, no place: none.
            GuideTemplate clear = MarkingTemplates.ById(VanillaClear.Id);
            Program.Check(MarkingSnapPoints.Of(SignCatalog.ById(VanillaClear.Id), new ItemRecord(clear.Hash, Vec3.Zero, 0f, 0u, MarkingTemplates.WithPolygon(clear, clear.Default().ToString(), MarkingPolygon.Encode(local)))).Count == 0, "a clearing area gives none");
            Program.Check(MarkingSnapPoints.Of(SignCatalog.ById("ban.stop") ?? SignCatalog.ByHash(1u), item).Count == 0 && MarkingSnapPoints.Of(null, item).Count == 0, "a sign or nothing gives none");
            Program.Check(MarkingSnapPoints.Of(SignCatalog.ById("mk.channel"), new ItemRecord(island.Hash, new Vec3(float.NaN, 0f, 0f), 0f, 0u, item.Spec)).Count == 0, "no place: none");

            Program.Check(MarkingSnapPoints.Nearest(corners, P(60.3f, 58.2f), MarkingSnapPoints.Reach, out Vec3 near) && Vec3.DistanceXZ(near, corners[1]) < 1e-4f, "the nearest within reach");
            Program.Check(!MarkingSnapPoints.Nearest(corners, P(61f, 58f), MarkingSnapPoints.Reach, out _) && !MarkingSnapPoints.Nearest(null, P(0f, 0f), 1f, out _), "none beyond reach, none of nothing");
            Program.Check(MarkingSnapPoints.Nearest(new[] { P(0f, 0f), P(0.3f, 0f) }, P(0.25f, 0f), 1f, out Vec3 nearer) && nearer.X == 0.3f, "the nearer of two");
            Program.Check(MarkingSnapPoints.Nearest(new[] { P(0.3f, 0f), P(0f, 0f) }, P(0.25f, 0f), 1f, out Vec3 nearerFirst) && nearerFirst.X == 0.3f, "whichever comes first");

            // A side along a lane line of a 100 m arc: the points between two corners 20 m apart along it, every 2 m, on the line.
            var arc = new ArcTable(new Bezier3(P(0f, 0f), P(0f, 33f), P(10f, 66f), P(30f, 100f)));
            List<Vec3> between = MarkingSnapPoints.Between(arc, 30f, 50f, 1.75f, 0.1f, 40);
            Program.Check(between.Count == 9, "20 m: nine points between, every 2 m: " + between.Count);
            bool onLine = true;
            foreach (Vec3 p in between)
            {
                float t = arc.Curve.ClosestT(p);
                Vec3 c = arc.Curve.Point(t);
                onLine &= Math.Abs(Vec3.DistanceXZ(p, c) - 1.75f) < 0.02f;
            }

            Program.Check(onLine && Math.Abs(between[0].Y - (arc.Curve.Point(arc.TAt(32f)).Y + 0.1f)) < 1e-3f, "each 1.75 m beside the curve, at the surface");
            Program.Check(MarkingSnapPoints.Between(arc, 50f, 30f, 1.75f, 0f, 40).Count == 9, "either way");
            Program.Check(MarkingSnapPoints.Between(arc, 30f, 50f, 1.75f, 0f, 4).Count == 4, "no more than the room left");
            Program.Check(MarkingSnapPoints.Between(arc, 30f, 33f, 1.75f, 0f, 40).Count == 0 && MarkingSnapPoints.Between(null, 0f, 20f, 0f, 0f, 40).Count == 0 && MarkingSnapPoints.Between(arc, 0f, 20f, 0f, 0f, 0).Count == 0, "too close, no line, no room: none");
        }

        private static void Hints()
        {
            foreach (bool zh in new[] { true, false })
            {
                foreach (string mode in new[] { "e", "r", "n" })
                {
                    string none = HintText.Of(new HintInfo { Kind = "mark-clear", Name = mode }, zh);
                    string some = HintText.Of(new HintInfo { Kind = "mark-clear", Name = mode, Count = 3, Left = 120f, Manual = true, Hung = true }, zh);
                    string all = HintText.Of(new HintInfo { Kind = "mark-clear", Name = mode, Count = 3, Left = 120f, Manual = false, Hung = false }, zh);
                    string done = HintText.Of(new HintInfo { Kind = "mark-clear", Name = mode, Count = 3, Left = 120f, Bad = true }, zh);
                    Program.Check(none.Length > 10 && some.Length > 10 && done.Length > 6, mode + (zh ? " zh" : " en") + ": a text for nothing under the cursor, for a click, and for cleared already");
                    Program.Check(none != some && some != all && done != some, mode + (zh ? " zh" : " en") + ": they differ, and the click says what it keeps and whether it redraws: " + some + " / " + all);
                    Program.Check(mode == "n" || some.Contains("120.0"), mode + ": the length of the road: " + some);
                    Program.Check(mode != "r" || some.Contains("3"), "the whole road: how many areas: " + some);
                }

                string island = HintText.Of(new HintInfo { Kind = "mark-poly", Count = 2 }, zh), area = HintText.Of(new HintInfo { Kind = "mark-poly", Count = 2, Name = "clear" }, zh);
                Program.Check(area != island && area.Contains("2") && area.Contains(MarkingPolygon.MaxPoints.ToString()), (zh ? "zh" : "en") + ": an area drawn to clear has its own line of help: " + area);
            }
        }

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        private static VanillaMarking M(float x, float z, MarkingOwner owner = MarkingOwner.Edge, bool crossing = false) => new VanillaMarking { X = x, Z = z, Owner = owner, Crossing = crossing };

        private static void Regions()
        {
            // An L: the square 0..10 less its quarter 5..10 x 5..10.
            var l = new List<Vec3> { P(0f, 0f), P(10f, 0f), P(10f, 5f), P(5f, 5f), P(5f, 10f), P(0f, 10f) };
            var edges = new ClearRegion(l, ClearScope.Edges, true);
            Program.Check(edges.Contains(2f, 2f) && edges.Contains(8f, 2f) && edges.Contains(2f, 8f), "inside the L");
            Program.Check(!edges.Contains(8f, 8f), "not in the quarter the L leaves out");
            Program.Check(!edges.Contains(-1f, 2f) && !edges.Contains(2f, 11f) && !edges.Contains(float.NaN, 2f), "not outside it, nor at no place");
            Program.Check(edges.MinX == 0f && edges.MaxX == 10f && edges.MinZ == 0f && edges.MaxZ == 10f, "its box");
            Program.Check(!new ClearRegion(new List<Vec3> { P(0f, 0f), P(1f, 0f) }, ClearScope.All, false).Contains(0.5f, 0f), "two corners have no inside");
            // A U (a square with a notch from the top): a point in the notch is outside, though a line from it crosses two sides.
            var u = new ClearRegion(new List<Vec3> { P(0f, 0f), P(10f, 0f), P(10f, 10f), P(7f, 10f), P(7f, 5f), P(3f, 5f), P(3f, 10f), P(0f, 10f) }, ClearScope.All, false);
            Program.Check(!u.Contains(5f, 8f) && u.Contains(8.5f, 8f) && u.Contains(1.5f, 8f) && u.Contains(5f, 2f), "a U: the notch is out, its arms and base are in");
            Program.Check(!new ClearRegion(null, ClearScope.All, false).Contains(0f, 0f), "nor none");

            Program.Check(edges.Covers(M(2f, 2f)), "a line of a segment in it is taken");
            Program.Check(!edges.Covers(M(2f, 2f, MarkingOwner.Node)), "a marking of a junction is not, by an area of segments");
            Program.Check(!edges.Covers(M(2f, 2f, MarkingOwner.Edge, true)), "a line across the traffic is kept");
            Program.Check(!edges.Covers(M(2f, 2f, MarkingOwner.Other)), "a marking of anything else is never taken");
            Program.Check(!edges.Covers(M(8f, 8f)), "nor one outside");
            var nodes = new ClearRegion(l, ClearScope.Nodes, false);
            Program.Check(nodes.Covers(M(2f, 2f, MarkingOwner.Node, true)) && !nodes.Covers(M(2f, 2f)), "an area of junctions takes a junction's marking (and the lines across, when it does not keep them), not a segment's");
            var all = new ClearRegion(l, ClearScope.All, false);
            Program.Check(all.Covers(M(2f, 2f)) && all.Covers(M(2f, 2f, MarkingOwner.Node)) && all.Covers(M(2f, 2f, MarkingOwner.Edge, true)) && !all.Covers(M(2f, 2f, MarkingOwner.Other)), "an area of both takes both, never anything else");
            Program.Check(VanillaClear.Covered(new[] { nodes, edges }, M(2f, 2f)) && !VanillaClear.Covered(new[] { nodes }, M(2f, 2f)) && !VanillaClear.Covered(null, M(2f, 2f)), "any of the areas");

            Program.Check(VanillaClear.NodeOfRoad(2, 0) && VanillaClear.NodeOfRoad(2, 1), "a node is the road's where two of its segments meet, alone or with a ramp or a side street");
            Program.Check(!VanillaClear.NodeOfRoad(2, 2) && !VanillaClear.NodeOfRoad(1, 1) && !VanillaClear.NodeOfRoad(3, 0) && !VanillaClear.NodeOfRoad(1, 0), "not where it crosses another road, ends, or splits in three");
            Program.Check(VanillaClear.IsCrossingName("Crosswalk Lane") && VanillaClear.IsCrossingName("Stop Line 0.3") && VanillaClear.IsCrossingName("Pedestrian Crossing Marking") && VanillaClear.IsCrossingName("YieldLine"), "names of lines across");
            Program.Check(!VanillaClear.IsCrossingName("Lane Marking Dashed") && !VanillaClear.IsCrossingName("") && !VanillaClear.IsCrossingName(null), "and of lines along");
        }

        private static ItemRecord Clear(Vec3 at, float yaw, IList<Vec3> local, string scope, string keep)
        {
            GuideTemplate t = MarkingTemplates.ById(VanillaClear.Id);
            string spec = MarkingTemplates.WithPolygon(t, t.Parse(t.Default().ToString()).With(VanillaClear.ScopeKey, scope).With(VanillaClear.KeepKey, keep).ToString(), MarkingPolygon.Encode(local));
            return new ItemRecord(t.Hash, at, yaw, 0u, spec);
        }

        private static void ItemsAndChanges()
        {
            // A rectangle 4 m wide and 10 m long in the item's frame; the item at (100, 0, 200) turned a quarter, so its +Z is the world's +X: the rectangle runs 10 m along +X from the item.
            var local = new List<Vec3> { P(-2f, 0f), P(2f, 0f), P(2f, 10f), P(-2f, 10f) };
            float yaw = (float)(Math.PI / 2.0);
            ClearRegion region = ClearRegion.Of(Clear(new Vec3(100f, 5f, 200f), yaw, local, "e", "lines"));
            Program.Check(region != null && region.Scope == ClearScope.Edges && region.KeepCrossings, "an item of segments that keeps the lines across");
            // The item's +Z is (sin yaw, cos yaw) = +X: 5 m along it is (105, 200).
            Program.Check(region.Contains(105f, 200f) && region.Contains(109f, 201.5f) && !region.Contains(105f, 203f) && !region.Contains(95f, 200f), "its outline in the world: along +X from the item, 4 m wide");
            // 0.12.12: an island drawn by its corners hides the game's markings under it, of every owner, keeping the lines across; the same item is no mk.clear area (the restore button leaves it).
            GuideTemplate islandT = MarkingTemplates.ById(MarkingTemplates.ChannelId);
            string islandSpec = MarkingTemplates.WithPolygon(islandT, islandT.Default().ToString(), MarkingPolygon.Encode(local));
            var islandItem = new ItemRecord(SignCatalog.ById(MarkingTemplates.ChannelId).Hash, new Vec3(100f, 5f, 200f), yaw, 0u, islandSpec);
            ClearRegion under = ClearRegion.OfArea(islandItem);
            Program.Check(under != null && under.Scope == ClearScope.All && under.KeepCrossings, "an island hides the game's markings under it, of segments and junctions, keeping the lines across");
            Program.Check(under.Contains(new Vec3(105f, 5f, 200f)) && !under.Contains(new Vec3(105f, 5f, 203f)) && !under.Contains(new Vec3(105f, 12f, 200f)), "under its outline, on its surface");
            Program.Check(under.Covers(new VanillaMarking { X = 105f, Y = 5f, Z = 200f, Owner = MarkingOwner.Node }) && !under.Covers(new VanillaMarking { X = 105f, Y = 5f, Z = 200f, Owner = MarkingOwner.Node, Crossing = true }), "a hatched gore of a junction, not a crosswalk");
            Program.Check(ClearRegion.Of(islandItem) == null, "an island is not an mk.clear area");
            GuideTemplate simpleT = MarkingTemplates.ById(MarkingTemplates.SimpleGridId);
            ClearRegion gridBox = ClearRegion.OfArea(new ItemRecord(SignCatalog.ById(MarkingTemplates.SimpleGridId).Hash, Vec3.Zero, 0f, 0u, simpleT.Default().ToString()));
            Program.Check(gridBox != null && gridBox.Outline.Count == 4, "a box grid with no outline hides what is under its paint's box");
            Program.Check(ClearRegion.OfArea(new ItemRecord(SignCatalog.ById("mk.stop").Hash, Vec3.Zero, 0f)) == null, "a line is no area");

            ClearRegion n = ClearRegion.Of(Clear(Vec3.Zero, 0f, local, "n", "all"));
            Program.Check(n != null && n.Scope == ClearScope.Nodes && !n.KeepCrossings, "an item of junctions that takes all");
            Program.Check(ClearRegion.Of(Clear(Vec3.Zero, 0f, local, "a", "lines")).Scope == ClearScope.All, "an item of both");
            GuideTemplate t = MarkingTemplates.ById(VanillaClear.Id);
            Program.Check(ClearRegion.Of(new ItemRecord(t.Hash, Vec3.Zero, 0f, 0u, t.Default().ToString())) == null, "an item with no outline yet: no area");
            Program.Check(ClearRegion.Of(new ItemRecord(t.Hash, Vec3.Zero, 0f, 0u, t.Parse(t.Default().ToString()).With(MarkingPolygon.Key, "d0:0|zz|1:1").ToString())) == null, "a damaged outline: no area");
            GuideTemplate stop = MarkingTemplates.ById("mk.stop");
            Program.Check(ClearRegion.Of(new ItemRecord(stop.Hash, Vec3.Zero, 0f, 0u, stop.Default().ToString())) == null, "another marking is no area");
            Program.Check(ClearRegion.Of(new ItemRecord(t.Hash, new Vec3(float.NaN, 0f, 0f), 0f, 0u, Clear(Vec3.Zero, 0f, local, "e", "lines").Spec)) == null, "an item at no place: no area");

            ClearRegion a = ClearRegion.Of(Clear(Vec3.Zero, 0f, local, "e", "lines"));
            ClearRegion b = ClearRegion.Of(Clear(new Vec3(50f, 0f, 0f), 0f, local, "e", "lines"));
            ClearRegion bMoved = ClearRegion.Of(Clear(new Vec3(60f, 0f, 0f), 0f, local, "e", "lines"));
            ClearRegion bAll = ClearRegion.Of(Clear(new Vec3(50f, 0f, 0f), 0f, local, "e", "all"));
            ClearRegion c = ClearRegion.Of(Clear(new Vec3(0f, 0f, 90f), 0f, local, "n", "lines"));
            Program.Check(a.SameAs(ClearRegion.Of(Clear(Vec3.Zero, 0f, local, "e", "lines"))) && !b.SameAs(bMoved) && !b.SameAs(bAll) && !a.SameAs(null), "the same area, another place, another keeping");
            var before = new Dictionary<int, ClearRegion> { { 1, a }, { 2, b } };
            Program.Check(VanillaClear.Changes(before, new Dictionary<int, ClearRegion>(before)).Count == 0, "nothing changed: nothing to look at");
            List<ClearBox> boxes = VanillaClear.Changes(before, new Dictionary<int, ClearRegion> { { 2, bMoved }, { 3, c } });
            Program.Check(boxes.Count == 4, "one gone, one moved (both its places), one new: four boxes: " + boxes.Count);
            bool hasA = false, hasOld = false, hasNew = false, hasC = false;
            foreach (ClearBox box in boxes)
            {
                hasA |= box.MinX == a.MinX && box.MinZ == a.MinZ;
                hasOld |= box.MinX == b.MinX;
                hasNew |= box.MinX == bMoved.MinX;
                hasC |= box.MinZ == c.MinZ;
            }

            Program.Check(hasA && hasOld && hasNew && hasC, "the gone one's, the moved one's old and new, the new one's");
            Program.Check(VanillaClear.Changes(before, null).Count == 2 && VanillaClear.Changes<int>(null, before).Count == 2 && VanillaClear.Changes<int>(null, null).Count == 0, "from or to none");
            Program.Check(VanillaClear.Changes(before, new Dictionary<int, ClearRegion> { { 1, a }, { 2, bAll } }).Count == 2, "a change of what is kept is a change of the area");
        }

        private static float Area(List<Vec3> polygon) => Math.Abs(Polygon.SignedArea2(polygon)) * 0.5f;

        private static void Outlines()
        {
            // A square of 10 grown by 0.5 is a square of 11, whichever way it winds.
            var square = new List<Vec3> { P(0f, 0f), P(10f, 0f), P(10f, 10f), P(0f, 10f) };
            Program.Near(Area(VanillaClear.Grow(square, 0.5f)), 121f, 0.01f, "a square grown, counter-clockwise");
            var clockwise = new List<Vec3>(square);
            clockwise.Reverse();
            Program.Near(Area(VanillaClear.Grow(clockwise, 0.5f)), 121f, 0.01f, "and clockwise");
            Program.Near(VanillaClear.Grow(square, 0.5f)[0].X, -0.5f, 1e-4f, "its corner moves out both ways");
            Program.Check(VanillaClear.Grow(square, 0f)[2].Equals(square[2]) && VanillaClear.Grow(new List<Vec3> { P(0f, 0f), P(1f, 0f) }, 1f).Count == 2, "by nothing, or a line: as it was");
            // A sharp corner moves at most three times the margin.
            var sharp = new List<Vec3> { P(0f, 0f), P(20f, 0.5f), P(0f, 1f) };
            List<Vec3> grownSharp = VanillaClear.Grow(sharp, 0.5f);
            float worst = 0f;
            for (int i = 0; i < 3; i++)
                worst = Math.Max(worst, Vec3.DistanceXZ(grownSharp[i], sharp[i]));
            Program.Check(worst <= 1.5f + 1e-4f && worst > 0.5f, "a sharp corner moves out no more than 1.5 m: " + worst);
            // An L grown keeps all its points inside.
            var l = new List<Vec3> { P(0f, 0f), P(10f, 0f), P(10f, 5f), P(5f, 5f), P(5f, 10f), P(0f, 10f) };
            var lr = new ClearRegion(VanillaClear.Grow(l, 0.5f), ClearScope.All, false);
            bool allIn = true;
            for (float x = 0.1f; x < 10f; x += 0.7f)
            {
                for (float z = 0.1f; z < 10f; z += 0.7f)
                {
                    if (!(x > 5f && z > 5f))
                        allIn &= lr.Contains(x, z);
                }
            }

            Program.Check(allIn && !lr.Contains(8f, 8f) && Area(lr.Outline) > Area(l), "an L grown holds the L, not the quarter it leaves out");

            // A segment's sides: two arcs; every point between them is inside, and a point 0.4 m outside either is too (the margin), 0.8 m is not.
            var left = new List<Vec3>();
            var right = new List<Vec3>();
            for (int i = 0; i <= 9; i++)
            {
                double a = i * 0.05;
                left.Add(P((float)(100.0 - 94.0 * Math.Cos(a)), (float)(94.0 * Math.Sin(a))));
                right.Add(P((float)(100.0 - 106.0 * Math.Cos(a)), (float)(106.0 * Math.Sin(a))));
            }

            var side = new ClearRegion(VanillaClear.SideOutline(left, right, 0.5f), ClearScope.Edges, true);
            bool between = true;
            for (int i = 0; i < 9; i++)
            {
                double a = i * 0.05 + 0.025;
                for (double r = 94.2; r < 106; r += 1.3)
                    between &= side.Contains((float)(100.0 - r * Math.Cos(a)), (float)(r * Math.Sin(a)));
            }

            Program.Check(side.Outline.Count == 20 && between, "the segment's outline holds the road between its sides");
            Program.Check(side.Contains((float)(100.0 - 93.6 * Math.Cos(0.2)), (float)(93.6 * Math.Sin(0.2))) && !side.Contains((float)(100.0 - 93.1 * Math.Cos(0.2)), (float)(93.1 * Math.Sin(0.2))), "0.4 m outside a side is in (the margin), 0.9 m is not");
            Program.Check(VanillaClear.SideOutline(left, new List<Vec3> { P(0f, 0f) }, 0.5f).Count == 0 && VanillaClear.SideOutline(null, right, 0.5f).Count == 0, "a side of one point or none: no outline");

            // A junction's hull: random points, all inside the hull (grown by 0.1), at most 24 corners, counter-clockwise.
            var rng = new Random(5);
            int bad = 0;
            for (int trial = 0; trial < 60; trial++)
            {
                var points = new List<Vec3>();
                int count = 5 + rng.Next(200);
                for (int i = 0; i < count; i++)
                {
                    double a = rng.NextDouble() * Math.PI * 2.0, r = Math.Sqrt(rng.NextDouble()) * 20.0;
                    points.Add(P((float)(r * Math.Cos(a)), (float)(r * Math.Sin(a))));
                }

                List<Vec3> hull = VanillaClear.Hull(points, 24, 1.5f);
                var hr = new ClearRegion(hull, ClearScope.Nodes, false);
                bool ok = hull.Count >= 3 && hull.Count <= 24 && Polygon.SignedArea2(hull) > 0f && MarkingFills.IsSimple(hull);
                foreach (Vec3 p in points)
                    ok &= hr.Contains(p.X, p.Z);
                if (!ok)
                    bad++;
            }

            Program.Check(bad == 0, "every point is inside its junction's outline, of at most 24 corners: " + bad + " of 60 not");
            Program.Check(VanillaClear.Hull(new List<Vec3> { P(0f, 0f), P(1f, 1f), P(2f, 2f), P(3f, 3f) }, 24, 1f).Count == 0, "points in a line: no outline");
            Program.Check(VanillaClear.Hull(new List<Vec3> { P(0f, 0f), P(10f, 0.000001f), P(20f, 0f) }, 24, 1f).Count == 0, "points all but in a line (no area): no outline");
            var ring = new List<Vec3>();
            for (int i = 0; i < 64; i++)
                ring.Add(P((float)(15.0 * Math.Cos(i * Math.PI / 32.0)), (float)(15.0 * Math.Sin(i * Math.PI / 32.0))));
            List<Vec3> ringHull = VanillaClear.Hull(ring, 24, 1f);
            var ringRegion = new ClearRegion(ringHull, ClearScope.Nodes, false);
            bool ringIn = true;
            foreach (Vec3 p in ring)
                ringIn &= ringRegion.Contains(p.X, p.Z);
            Program.Check(ringHull.Count == 24 && ringIn, "a ring of 64 points: a hull of 24 corners, grown to hold them all: " + ringHull.Count);
            Program.Check(VanillaClear.Hull(new List<Vec3> { P(0f, 0f), P(1f, 0f) }, 24, 1f).Count == 0 && VanillaClear.Hull(null, 24, 1f).Count == 0, "two points or none: none");
            List<Vec3> unit = VanillaClear.Hull(new List<Vec3> { P(0f, 0f), P(4f, 0f), P(4f, 4f), P(0f, 4f), P(2f, 2f), P(2f, 0f) }, 24, 0f);
            Program.Check(unit.Count == 4 && Math.Abs(Area(unit) - 16f) < 1e-3f, "the hull of a square's corners, its middle and a point on a side: the square: " + unit.Count);
        }

        private static void Item()
        {
            GuideTemplate t = MarkingTemplates.ById(VanillaClear.Id);
            SignDef def = SignCatalog.ById(VanillaClear.Id);
            Program.Check(t != null && def != null && def.IsMarking && !MarkingTemplates.IsRun(t), "a marking of the catalogue, not a run");
            MarkingPlan thumb = MarkingTemplates.PlanOf(t, t.Default().ToString());
            Program.Check(thumb.Parts.Count == 0 && thumb.Outlines.Count == 1 && Math.Abs(thumb.MaxX - thumb.MinX - 12f) < 1e-3f && Math.Abs(thumb.MaxZ - thumb.MinZ - 6f) < 1e-3f, "with no outline yet: a 12 x 6 m one, its box the plan's");
            byte[] picture = MarkingRaster.Render(thumb, 80, out int w, out int h);
            int red = 0;
            for (int i = 0; i < w * h; i++)
                red += picture[i * 4] > 150 && picture[i * 4 + 1] < 90 ? 1 : 0;
            Program.Check(red > 40 && red < w * h / 4, "its picture is a red dashed outline: " + red + " red pixels");

            // A placed one: 20 x 10 m at (300, 0, 400). Nothing is drawn, nothing is skipped.
            var local = new List<Vec3> { P(-10f, -5f), P(10f, -5f), P(10f, 5f), P(-10f, 5f) };
            string spec = MarkingTemplates.WithPolygon(t, t.Default().ToString(), MarkingPolygon.Encode(local));
            var item = new ItemRecord(t.Hash, new Vec3(300f, 0f, 400f), 0f, 0u, spec);
            List<ChunkPart> parts = ChunkBuilder.Build(new[] { item }, new Vec3(256f, 0f, 384f), out int skipped);
            Program.Check(parts.Count == 0 && skipped == 0, "a tile with it draws nothing and skips nothing: " + parts.Count + " part(s), " + skipped + " skipped");
            SignParts picks = ChunkBuilder.PartsOfRecord(item);
            Program.Check(picks.Picks.Count > 0, "it can be pointed at: " + picks.Picks.Count + " box(es)");
            float top = float.MinValue;
            foreach (PickShape p in picks.Picks)
                top = Math.Max(top, p.Max.Y);
            Program.Check(top <= MarkingPlan.OutlineTop + 1e-4f, "low, under the paint's boxes: " + top);
            Program.Check(Picking.Pick(picks.Picks, item.Position, item.Yaw, new Vec3(303f, 5f, 402f), new Vec3(303f, -5f, 402f), out float clearDistance), "a ray down inside it hits it");
            Program.Check(!Picking.Pick(picks.Picks, item.Position, item.Yaw, new Vec3(320f, 5f, 402f), new Vec3(320f, -5f, 402f), out _), "a ray outside misses");
            // A lane line painted inside the area is hit first by the same ray.
            GuideTemplate lane = MarkingTemplates.ById("mk.line.whitesolid");
            string path = RunPath.Encode(new List<Vec3> { new Vec3(303f, 0f, 395f), new Vec3(303f, 0f, 405f) }, out float laneYaw);
            var line = new ItemRecord(lane.Hash, new Vec3(303f, 0f, 395f), laneYaw, 0u, MarkingTemplates.WithPath(lane, lane.Default().ToString(), path));
            SignParts linePicks = ChunkBuilder.PartsOfRecord(line);
            Program.Check(Picking.Pick(linePicks.Picks, line.Position, line.Yaw, new Vec3(303f, 5f, 402f), new Vec3(303f, -5f, 402f), out float lineDistance) && lineDistance < clearDistance, "a line painted in it is nearer to the eye: " + lineDistance + " < " + clearDistance);
            Program.Check(MarkingTemplates.PlanOf(t, spec).VertexCount == 4, "it weighs its corners in the cache");
        }

        private static StandardLine At(List<StandardLine> lines, float lateral)
        {
            foreach (StandardLine l in lines)
            {
                if (Math.Abs(l.Lateral - lateral) < 0.01f)
                    return l;
            }

            return new StandardLine { Id = "none" };
        }

        private static void Standard()
        {
            // Two lanes each way, 50 km/h: the backward lanes on the left of the edge (right-hand traffic).
            List<StandardLine> city = StandardLines.Plan(new[] { 1.75f, 5.25f }, new[] { -1.75f, -5.25f }, 50f, true);
            Program.Check(city.Count == 5, "two lanes each way: five lines: " + string.Join(", ", city));
            Program.Check(At(city, -7f).Id == "mk.line.edge" && At(city, -7f).Value == "r" && At(city, 7f).Id == "mk.line.edge" && At(city, 7f).Value == "r", "an edge line on each outer side, on the right of its traffic (white)");
            Program.Check(At(city, 0f).Id == "mk.line.doubleyellow", "two lanes or more each way: a double yellow line between them");
            Program.Check(At(city, -3.5f).Id == "mk.line.lane" && At(city, -3.5f).Value == "24" && At(city, 3.5f).Value == "24", "lane lines 2-4 under 60 km/h");

            List<StandardLine> rural = StandardLines.Plan(new[] { 1.75f }, new[] { -1.75f }, 80f, true);
            Program.Check(rural.Count == 3 && At(rural, 0f).Id == "mk.line.centre" && At(rural, -3.5f).Id == "mk.line.edge" && At(rural, 3.5f).Id == "mk.line.edge", "one lane each way: the yellow dashed centre line and two edge lines: " + string.Join(", ", rural));

            List<StandardLine> oneWay = StandardLines.Plan(new[] { -3.5f, 0f, 3.5f }, new float[0], 100f, true);
            Program.Check(oneWay.Count == 4 && At(oneWay, -5.25f).Value == "l" && At(oneWay, 5.25f).Value == "r", "one way: the left edge on the left of the traffic (yellow), the right on its right: " + string.Join(", ", oneWay));
            Program.Check(At(oneWay, -1.75f).Value == "69" && At(oneWay, 1.75f).Value == "69", "lane lines 6-9 from 60 km/h");
            List<StandardLine> unknown = StandardLines.Plan(new[] { -1.75f, 1.75f }, null, 80f, false);
            Program.Check(At(unknown, 0f).Value == "24", "no speed read (whatever the number): 2-4");
            List<StandardLine> uneven = StandardLines.Plan(new[] { 1.75f, 5.25f }, new[] { -1.75f }, 50f, true);
            Program.Check(At(uneven, 0f).Id == "mk.line.centre", "two lanes one way and one the other: the dashed centre line, not the double: " + string.Join(", ", uneven));
            List<StandardLine> sixty = StandardLines.Plan(new[] { -1.75f, 1.75f }, null, 60f, true);
            Program.Check(At(sixty, 0f).Value == "69", "60 km/h is 6-9");

            // A median 9 m wide: the edges each side of it are on the left of their traffic (yellow).
            List<StandardLine> divided = StandardLines.Plan(new[] { 4.5f, 8f }, new[] { -4.5f, -8f }, 70f, true);
            Program.Check(divided.Count == 6, "a divided road: two outer edges, two lane lines, the median's two edges: " + string.Join(", ", divided));
            Program.Check(At(divided, -4.5f + MarkingSnap.DefaultLane * 0.5f).Value == "l" && At(divided, 4.5f - MarkingSnap.DefaultLane * 0.5f).Value == "l", "the median's edges on the left of the traffic");
            Program.Check(At(divided, 0f).Id == "none", "no centre line through the median");

            List<StandardLine> single = StandardLines.Plan(new[] { 0f }, new[] { 0.2f }, 30f, true);
            Program.Check(single.Count == 2 && single[0].Id == "mk.line.edge" && single[1].Id == "mk.line.edge", "one lane for both ways: its two edge lines: " + string.Join(", ", single));
            Program.Check(StandardLines.Plan(null, null, 50f, true).Count == 0 && StandardLines.Plan(new float[0], new[] { float.NaN }, 50f, true).Count == 0, "no lanes: no lines");
            // Each line's marking exists, and its setting is one of its choices.
            foreach (StandardLine line in city)
            {
                GuideTemplate t = MarkingTemplates.ById(line.Id);
                Program.Check(t != null && MarkingTemplates.IsRun(t), line.Id + " is a line that runs along the road");
                if (!string.IsNullOrEmpty(line.Key))
                    Program.Check(t.Parse(t.Default().ToString()).With(line.Key, line.Value).Get(line.Key) == line.Value && t.FieldOf(line.Key) != null, line.Id + ": " + line.Key + "=" + line.Value + " is a setting of it");
            }
        }
    }
}
