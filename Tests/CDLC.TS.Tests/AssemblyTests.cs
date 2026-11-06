using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>Offline checks of several boards on one support (GB 5768.2 4.8.2, 4.8.6, 4.8.12 c): the order, the room they take, where each one hangs, and the support built for the whole.</summary>
    internal static class AssemblyTests
    {
        public static void RunAll()
        {
            Program.Run("assembly: boards are put in the order of 4.8.6", Ordering);
            Program.Run("assembly: an auxiliary board goes under the board it explains", AuxiliaryOrder);
            Program.Run("assembly: the order keeps up to twelve boards, a support's own limit is Attach's", MostFour);
            Program.Run("assembly: the support is made for the whole of the boards", Totals);
            Program.Run("assembly: stacked boards are one over the other, hung boards side by side with their lower edges level", Arrangement);
            Program.Run("assembly: one board is what it was", OneBoard);
            Program.Run("assembly: several boards on every form of support", ManyBoards);
            Program.Run("assembly: picking tells the boards apart", PickMembers);
            Program.Run("groups: the order of the members and the numbers they are given", GroupOrder);
            Program.Run("groups: the boards of one group are drawn as one support", GroupDrawn);
            Program.Run("groups: unknown members and a group with nothing known", GroupUnknown);
            Program.Run("groups: a change of group or slot rebuilds the tile", GroupDiff);
            Program.Run("groups: a hit on a board names the member", GroupPick);
            Program.Run("groups: the layout is the support's for the whole", GroupLayout);
            Program.Run("lanes: a board over a lane stands at its offset, clear of the posts", LaneArrange);
            Program.Run("lanes: the span or the arm grows to reach a lane", LaneReach);
            Program.Run("lanes: the arm of a cantilever is the reach that was asked for", ArmReach);
            Program.Run("lanes: boards over lanes and boards side by side, and the overlaps", LaneMixed);
            Program.Run("lane math: where a lane crosses the plane of the beam", LaneCrossing);
            Program.Run("lane math: the lanes of a straight road and of a bend", LaneRoads);
            Program.Run("lane math: facing, merging and sorting", LaneArrangeMath);
            Program.Run("lane math: where a lane is across its edge and which way it goes", LaneAcross);
            Program.Run("lanes: posts have no lanes", LaneOnPosts);
            Program.Run("lanes: one lane for each board", LaneSpread);
            Program.Run("picking: a board on a beam or an arm is hit before the beam", BoardsBeforeBeam);
            Program.Run("lanes: a lone board over a lane is drawn, picked and kept apart as a group of one", LaneSingle);
            Program.Run("lanes: the lane and the offset are part of what changes a tile", LaneDiff);
        }

        private static AssemblyBoard Laned(SignDef def, int slot, int lane, float offset) =>
            new AssemblyBoard { Instance = SignInstances.Of(def, null), Slot = slot, Member = slot, Lane = lane, OffsetX = offset };

        private static float CentreX(MeshBuilder mesh)
        {
            mesh.Bounds(out Vec3 lo, out Vec3 hi);
            return (lo.X + hi.X) * 0.5f;
        }

        private static void LaneArrange()
        {
            SignDef p = Fixed("prohibition"), m = Fixed("mandatory"), w = Fixed("warning");
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            var boards = new List<AssemblyBoard> { Laned(p, 0, 1, 9.0f), Laned(m, 1, 2, 6.0f), Laned(w, 2, 3, 3.0f) };
            List<AssemblyBoard> ordered = Assembly.Order(boards);
            AssemblyPlan plan = Assembly.Plan(ordered, gantry, 0f);
            Program.Check(plan.Hung && plan.Placed.Count == 3, "a gantry with three boards over lanes");
            for (int i = 0; i < 3; i++)
            {
                Program.Check(ReferenceEquals(plan.Placed[i].Board, ordered[i]), "the placements are in the order of the boards");
                Program.Near(plan.Placed[i].Cx, ordered[i].OffsetX, 1e-4f, "board " + i + " stands at its offset");
                Program.Near(plan.Placed[i].Bottom, plan.Layout.BottomClearance, 1e-5f, "the lower edges are level");
            }

            Program.Check(Assembly.Overlaps(plan.Placed, true).Count == 0, "lanes 3 m apart: no overlap");
            Program.Near(plan.Layout.ArmLength, gantry.ArmLength, 1e-4f, "a span that is long enough stays the default one");

            // The same through the build: the face of each board is round its offset.
            SignParts parts = SignBuilder.BuildAssembly(boards, gantry, 0f);
            Program.Check(parts.Faces.Count == 3, "a face for each board");
            for (int i = 0; i < 3; i++)
                Program.Near(CentreX(parts.Faces[i].Mesh), ordered[i].OffsetX, 0.01f, "the face of board " + i + " is round its offset");

            // Too near a post: kept clear of the post and its knee brace (the post's radius and 1.3 m, and half the board).
            var near = new List<AssemblyBoard> { Laned(p, 0, 1, 0.5f) };
            AssemblyPlan nearPlan = Assembly.Plan(Assembly.Order(near), gantry, 0f);
            float lo = Assembly.GantryClear + p.Width * 0.5f;
            Program.Near(nearPlan.Placed[0].Cx, lo, 1e-4f, "a board over a lane next to the post is moved out of the way of the brace");
            var far = new List<AssemblyBoard> { Laned(p, 0, 1, 13.9f) };
            AssemblyPlan farPlan = Assembly.Plan(Assembly.Order(far), gantry, 0f);
            Program.Check(farPlan.Placed[0].Cx + p.Width * 0.5f <= farPlan.Layout.ArmLength - Assembly.GantryClear + 1e-3f, "and clear of the far post");
        }

        private static void LaneReach()
        {
            SignDef p = Fixed("prohibition");
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry), cantilever = SupportCatalog.ById(SupportCatalog.Cantilever);

            // A board over a lane far across the road lengthens the span, with the room the end asks for; asking for less does not shorten it.
            var farBoard = Assembly.Order(new List<AssemblyBoard> { Laned(p, 0, 1, 20f) });
            AssemblyPlan wide = Assembly.Plan(farBoard, gantry, 0f);
            Program.Check(wide.Layout.ArmLength >= 20f + p.Width * 0.5f + Assembly.GantryClear - 1e-3f, "the span reaches the board and the room beyond it: " + wide.Layout.ArmLength);
            Program.Near(wide.Placed[0].Cx, 20f, 1e-3f, "and the board is where its lane is");
            AssemblyPlan asked = Assembly.Plan(farBoard, gantry, 10f);
            Program.Check(asked.Layout.ArmLength >= wide.Layout.ArmLength - 1e-3f, "asking for a span of 10 m does not cut it short");
            AssemblyPlan wider = Assembly.Plan(farBoard, gantry, 30f);
            Program.Near(wider.Layout.ArmLength, 30f, 1e-3f, "asking for more than it needs is what it gets");

            // A cantilever: the arm reaches the board and its end clearance (the default arm is 5 m).
            var lane = Assembly.Order(new List<AssemblyBoard> { Laned(p, 0, 1, 6f) });
            AssemblyPlan arm = Assembly.Plan(lane, cantilever, 0f);
            Program.Check(arm.Layout.ArmLength >= 6f + p.Width * 0.5f - Assembly.ArmTipInside - 1e-3f, "the arm reaches the board over a lane: " + arm.Layout.ArmLength);
            Program.Near(arm.Placed[0].Cx, 6f, 1e-3f, "the board is where its lane is");
            Program.Near(Assembly.RequiredReach(lane, SupportKind.Cantilever), 6f + p.Width * 0.5f - Assembly.ArmTipInside, 1e-4f, "the reach a lane needs");
            Program.Near(Assembly.RequiredReach(lane, SupportKind.SinglePole), 0f, 1e-6f, "a post needs no reach");
            Program.Near(Assembly.RequiredReach(Assembly.Order(new List<AssemblyBoard> { Laned(p, 0, 0, 0f) }), SupportKind.Gantry), 0f, 1e-6f, "a board that is not over a lane needs none");
        }

        private static void ArmReach()
        {
            SignDef p = Fixed("prohibition");
            SupportDef cantilever = SupportCatalog.ById(SupportCatalog.Cantilever);
            Program.Near(SupportLayouts.Of(p, cantilever, 0f).ArmLength, cantilever.ArmLength, 1e-4f, "no reach: the support's own arm");
            Program.Near(SupportLayouts.Of(p, cantilever, 9f).ArmLength, 9f, 1e-4f, "an arm of 9 m is 9 m (it was ignored until 0.3.2)");
            Program.Check(SupportLayouts.Of(p, cantilever, 3f).ArmLength >= Assembly.BoardFromPost + p.Width - Assembly.ArmTipInside - 1e-3f, "a short arm is made long enough for the board");
            Program.Near(SupportLayouts.Of(p, cantilever, 90f).ArmLength, SupportLayouts.MaxSpan, 1e-4f, "never more than the longest span");
            Program.Check(SupportLayouts.Of(p, cantilever, 9f).BoardCentreX > SupportLayouts.Of(p, cantilever, 5f).BoardCentreX, "the board hangs near the end of a longer arm");
        }

        private static void LaneMixed()
        {
            SignDef p = Fixed("prohibition"), m = Fixed("mandatory"), w = Fixed("warning");
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);

            // One board over a lane, two side by side: the two are together in the middle of the span, the first of them at the viewer's left; the order of the list is that of the boards.
            var boards = new List<AssemblyBoard> { Laned(p, 0, 1, 11f), Laned(m, 1, 0, 0f), Laned(w, 2, 0, 0f) };
            List<AssemblyBoard> ordered = Assembly.Order(boards);
            AssemblyPlan plan = Assembly.Plan(ordered, gantry, 0f);
            Program.Near(plan.Placed[0].Cx, 11f, 1e-3f, "the board over a lane is at its lane");
            Program.Check(plan.Placed[1].Cx > plan.Placed[2].Cx, "of the two side by side the first is at the viewer's left (the larger X)");
            Program.Near(plan.Placed[1].Cx - plan.Placed[1].Def.Width * 0.5f - (plan.Placed[2].Cx + plan.Placed[2].Def.Width * 0.5f), Assembly.SideGap, 1e-3f, "with the usual gap between them");
            Program.Near((plan.Placed[1].Cx + plan.Placed[1].Def.Width * 0.5f + plan.Placed[2].Cx - plan.Placed[2].Def.Width * 0.5f) * 0.5f, plan.Layout.BoardCentreX, 1e-3f, "centred together on the span");

            // The width the support is made for is that of the boards side by side, not of all of them.
            SignDef total = Assembly.TotalDef(ordered, true);
            Program.Near(total.Width, m.Width + w.Width + Assembly.SideGap, 1e-4f, "the total is the width of the two that are side by side");
            var allLaned = Assembly.Order(new List<AssemblyBoard> { Laned(p, 0, 1, 9f), Laned(m, 1, 2, 6f), Laned(w, 2, 3, 3f) });
            Program.Near(Assembly.TotalDef(allLaned, true).Width, Math.Max(p.Width, Math.Max(m.Width, w.Width)), 1e-4f, "when all are over lanes it is the widest board");

            // Two boards over one lane overlap; so does one over a lane that stands in the side by side ones.
            var same = Assembly.Order(new List<AssemblyBoard> { Laned(p, 0, 2, 6f), Laned(m, 1, 2, 6f) });
            List<int[]> overlaps = Assembly.Overlaps(Assembly.Plan(same, gantry, 0f).Placed, true);
            Program.Check(overlaps.Count == 1, "two boards over one lane overlap: " + overlaps.Count);
            var centre = Assembly.Order(new List<AssemblyBoard> { Laned(p, 0, 2, 7f), Laned(m, 1, 0, 0f) });
            AssemblyPlan centrePlan = Assembly.Plan(centre, gantry, 0f);
            Program.Check(Assembly.Overlaps(centrePlan.Placed, true).Count == 1, "a board over a lane under the board in the middle overlaps it");
            Program.Check(Assembly.Overlaps(centrePlan.Placed, false).Count == 0, "on posts nothing overlaps sideways");
        }

        private static void LaneSpread()
        {
            SignDef p = Fixed("prohibition"), m = Fixed("mandatory"), w = Fixed("warning");
            List<AssemblyBoard> ordered = Assembly.Order(new List<AssemblyBoard> { Laned(w, 2, 0, 0f), Laned(p, 0, 0, 0f), Laned(m, 1, 0, 0f) });
            var centres = new List<float> { 9f, 6f, 3f };

            // The first board of the order (the prohibition sign) to the leftmost lane, the second to the next, and so on.
            List<KeyValuePair<int, float>> spread = Assembly.SpreadOverLanes(ordered, centres);
            Program.Check(spread.Count == 3, "a lane for each board");
            for (int i = 0; i < 3; i++)
            {
                Program.Check(spread[i].Key == i + 1, "board " + i + " is over lane " + (i + 1));
                Program.Near(spread[i].Value, centres[i], 1e-6f, "at the middle of that lane");
            }

            // Fewer lanes than boards: the last boards stay side by side. More lanes than boards: the last lanes stay free.
            List<KeyValuePair<int, float>> few = Assembly.SpreadOverLanes(ordered, new List<float> { 9f, 6f });
            Program.Check(few[0].Key == 1 && few[1].Key == 2 && few[2].Key == 0 && few[2].Value == 0f, "two lanes for three boards: the third is automatic");
            List<KeyValuePair<int, float>> many = Assembly.SpreadOverLanes(ordered, new List<float> { 12f, 9f, 6f, 3f, 1.5f });
            Program.Check(many.Count == 3 && many[2].Key == 3, "five lanes for three boards: the first three are used");
            Program.Check(Assembly.SpreadOverLanes(ordered, new List<float>()).TrueForAll(pair => pair.Key == 0), "no lanes, no lane");

            // Applied to the boards and built: each face is at its lane, in the order of the boards, and nothing overlaps.
            for (int i = 0; i < 3; i++)
            {
                ordered[i].Lane = spread[i].Key;
                ordered[i].OffsetX = spread[i].Value;
            }

            SignParts parts = SignBuilder.BuildAssembly(ordered, SupportCatalog.ById(SupportCatalog.Gantry), 0f);
            for (int i = 0; i < 3; i++)
                Program.Near(CentreX(parts.Faces[i].Mesh), centres[i], 0.01f, "the face of board " + i + " is over its lane");
            Program.Check(Assembly.Overlaps(Assembly.Plan(ordered, SupportCatalog.ById(SupportCatalog.Gantry), 0f).Placed, true).Count == 0, "three lanes 3 m apart: no overlap");
        }

        private static void LaneCrossing()
        {
            // A sign at the origin facing +Z (yaw 0): the beam's plane is z = 0; its X axis is +X. A lane that runs along Z at x = 7 crosses at (7, 0, 0).
            var lane = Bezier3.Line(new Vec3(7f, 0f, 50f), new Vec3(7f, 0f, -50f));
            float x = LaneMath.LaneX(lane, Vec3.Zero, 0f, out float t, out bool crossed);
            Program.Check(crossed, "a lane along Z crosses the plane");
            Program.Near(x, 7f, 1e-3f, "at x = 7");
            Program.Near(t, 0.5f, 1e-3f, "in the middle of its curve");
            Program.Check(LaneMath.FacesSign(lane, t, 0f), "a lane that runs toward -Z faces a sign that looks toward +Z");
            Program.Check(!LaneMath.FacesSign(Bezier3.Line(new Vec3(7f, 0f, -50f), new Vec3(7f, 0f, 50f)), 0.5f, 0f), "a lane that runs away does not");

            // Not through the sign's foot: the crossing is where the plane cuts the lane, not the lane's point nearest the foot. A lane that runs slantwise (a bend near the sign) from (4, 0, 30) to (10, 0, -30):
            // z = 0 at x = 7, while its nearest point to the origin is nearer to x = 4 or so.
            var slant = Bezier3.Line(new Vec3(4f, 0f, 30f), new Vec3(10f, 0f, -30f));
            float xs = LaneMath.LaneX(slant, Vec3.Zero, 0f, out _, out bool slantCrossed);
            Program.Check(slantCrossed, "a slanting lane crosses");
            Program.Near(xs, 7f, 1e-2f, "at the middle of the slant, where the plane cuts it");

            // Turned: the sign turned by 90 degrees (yaw pi/2) looks toward +X; its X axis is -Z (the driver's left). A lane along X at z = -5 (so at +5 on the sign's X axis).
            var turned = Bezier3.Line(new Vec3(40f, 0f, -5f), new Vec3(-40f, 0f, -5f));
            float xt = LaneMath.LaneX(turned, Vec3.Zero, (float)(Math.PI * 0.5), out float tt, out bool turnedCrossed);
            Program.Check(turnedCrossed, "crosses the turned plane");
            Program.Near(xt, 5f, 1e-2f, "at 5 m on the turned sign's X axis");
            Program.Check(LaneMath.FacesSign(turned, tt, (float)(Math.PI * 0.5)), "and it runs toward the turned sign's face");

            // A lane that stops short of the plane: nearest point, not crossed.
            var short1 = Bezier3.Line(new Vec3(9f, 0f, 60f), new Vec3(9f, 0f, 10f));
            float xn = LaneMath.LaneX(short1, Vec3.Zero, 0f, out _, out bool shortCrossed);
            Program.Check(!shortCrossed, "a lane that ends before the plane does not cross it");
            Program.Near(xn, 9f, 1e-2f, "its nearest point is still at x = 9");

            // CrossingT: none, one, and the plane through the first sample.
            Program.Near(LaneMath.CrossingT(Bezier3.Line(new Vec3(1f, 0f, 5f), new Vec3(1f, 0f, 9f)), Vec3.Zero, new Vec3(0f, 0f, 1f)), -1f, 1e-6f, "no crossing is -1");
            Program.Near(LaneMath.CrossingT(Bezier3.Line(new Vec3(1f, 0f, 0f), new Vec3(1f, 0f, 9f)), Vec3.Zero, new Vec3(0f, 0f, 1f)), 0f, 1e-6f, "a curve that starts on the plane crosses at 0");
            var curved = new Bezier3(new Vec3(3f, 0f, 40f), new Vec3(8f, 0f, 20f), new Vec3(8f, 0f, -20f), new Vec3(3f, 0f, -40f));
            float tc = LaneMath.CrossingT(curved, Vec3.Zero, new Vec3(0f, 0f, 1f));
            Program.Check(tc > 0.45f && tc < 0.55f && Math.Abs(curved.Point(tc).Z) < 1e-2f, "an S or arc crosses once where z is 0: t = " + tc);
        }

        private static void LaneRoads()
        {
            // Five lanes 4 m apart at 3.7 .. 19.7 m on a straight road, all running toward the sign (-Z), and five more running away at -4 .. -20 (the other carriageway).
            var xs = new List<float>();
            for (int i = 0; i < 5; i++)
            {
                float laneX = 3.7f + 4f * i;
                var toward = Bezier3.Line(new Vec3(laneX, 0f, 80f), new Vec3(laneX, 0f, -80f));
                float x = LaneMath.LaneX(toward, Vec3.Zero, 0f, out float t, out _);
                if (LaneMath.FacesSign(toward, t, 0f))
                    xs.Add(x);
                var away = Bezier3.Line(new Vec3(-laneX - 0.3f, 0f, -80f), new Vec3(-laneX - 0.3f, 0f, 80f));
                float ax = LaneMath.LaneX(away, Vec3.Zero, 0f, out float at, out _);
                if (LaneMath.FacesSign(away, at, 0f))
                    xs.Add(ax);
            }

            List<float> lanes = LaneMath.Arrange(xs, 0.3f);
            Program.Check(lanes.Count == 5, "five lanes face the sign: " + lanes.Count);
            for (int i = 0; i < 5; i++)
                Program.Near(lanes[i], 19.7f - 4f * i, 1e-2f, "lane " + (i + 1) + " from the driver's left is at " + (19.7f - 4f * i));

            // A bend: the road turns by 30 degrees about a point 120 m to its left; the lanes are arcs, the sign stands at the right edge with its yaw along the road there. Each arc crosses the plane
            // of the beam at its own radius: the offsets are the radii differences, not the distances from the sign's foot.
            const float radius = 120f;
            float yaw = 0f;
            var arcs = new List<float>();
            for (int i = 0; i < 4; i++)
            {
                float laneRadius = radius - (2f + 4f * i);   // lanes to the left of the sign are nearer the centre of the bend
                arcs.Add(laneRadius);
            }

            var bendXs = new List<float>();
            foreach (float lr in arcs)
            {
                // The arc about the centre (radius, 0, 0): the sign at the origin, the road heading toward +Z there (yaw 0); traffic that faces the sign runs toward -Z. A point of the lane at angle a is
                // (radius - lr * cos a, 0, lr * sin a); the lane runs from a = +0.5 rad (z > 0) to a = -0.5 rad (z < 0).
                // A cubic with the standard arc handles follows the arc to well under a centimetre.
                var smooth = FromArc(radius, lr, 0.5f, -0.5f);
                float x = LaneMath.LaneX(smooth, Vec3.Zero, yaw, out _, out bool crossed);
                Program.Check(crossed, "an arc lane crosses the plane");
                bendXs.Add(x);
            }

            // At z = 0 the arc of radius lr is at x = radius - lr: the lanes are at 2, 6, 10, 14 m... measured from the sign: radius - lr = 2 + 4 i.
            for (int i = 0; i < 4; i++)
                Program.Near(bendXs[i], 2f + 4f * i, 0.02f, "bend: lane " + i + " is at " + (2f + 4f * i) + " where the beam's plane cuts it: " + bendXs[i]);
        }

        /// <summary>A cubic Bezier that follows the arc of the given radius about (cx, 0, 0) from angle a0 to a1 (the standard 4/3 tan(da/4) handles).</summary>
        private static Bezier3 FromArc(float cx, float lr, float a0, float a1)
        {
            float da = a1 - a0;
            float k = 4f / 3f * (float)Math.Tan(da / 4f);
            Vec3 P(float a) => new Vec3(cx - lr * (float)Math.Cos(a), 0f, lr * (float)Math.Sin(a));
            Vec3 D(float a) => new Vec3(lr * (float)Math.Sin(a), 0f, lr * (float)Math.Cos(a));
            return new Bezier3(P(a0), P(a0) + D(a0) * k, P(a1) - D(a1) * k, P(a1));
        }

        private static void LaneAcross()
        {
            // An edge 40 m long along +Z; lanes: forward ones to the right of its direction (+X on the right of +Z is... the right of travel is (az, -ax) = (1, 0) for a +Z edge: +X), backward to the left.
            Bezier3 edge = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 40f));
            LaneMath.Across(edge, Bezier3.Line(new Vec3(3.5f, 0f, 0f), new Vec3(3.5f, 0f, 40f)), out float lat, out bool fwd);
            Program.Near(lat, 3.5f, 1e-3f, "a lane 3.5 m to the right");
            Program.Check(fwd, "running with the edge");
            LaneMath.Across(edge, Bezier3.Line(new Vec3(-3.5f, 0f, 40f), new Vec3(-3.5f, 0f, 0f)), out lat, out fwd);
            Program.Near(lat, -3.5f, 1e-3f, "a lane 3.5 m to the left");
            Program.Check(!fwd, "running against the edge");

            // The old test (which end of the edge the lane's start is nearer to) is wrong for a lane that starts part way along: this one starts at z = 25 and runs on to 40, it is forward, and its
            // start is nearer to the end of the edge (distance 15) than to its start (25).
            Bezier3 late = Bezier3.Line(new Vec3(3.5f, 0f, 25f), new Vec3(3.5f, 0f, 40f));
            Vec3 start = late.A;
            bool oldSaysForward = Vec3.DistanceXZ(start, edge.A) <= Vec3.DistanceXZ(start, edge.D);
            LaneMath.Across(edge, late, out lat, out fwd);
            Program.Check(!oldSaysForward && fwd, "a lane that starts part way: the old test said backward, the tangent says forward");

            // A short edge on a wide road: 6 m long, lanes out to 10.5 m either side: still right (and so was the old test in this plain case).
            Bezier3 shortEdge = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 6f));
            for (int i = 0; i < 4; i++)
            {
                float x = 1.5f + 3f * i;
                LaneMath.Across(shortEdge, Bezier3.Line(new Vec3(x, 0f, 0f), new Vec3(x, 0f, 6f)), out lat, out fwd);
                Program.Check(fwd && Math.Abs(lat - x) < 1e-3f, "short edge, forward lane " + i);
                LaneMath.Across(shortEdge, Bezier3.Line(new Vec3(-x, 0f, 6f), new Vec3(-x, 0f, 0f)), out lat, out fwd);
                Program.Check(!fwd && Math.Abs(lat + x) < 1e-3f, "short edge, backward lane " + i);
            }

            // A bend: an edge of radius 100 turning left; a lane 3 m to the right of it is at lateral +3, one 3 m to the left at -3, whatever the point of the bend its middle is at.
            Bezier3 arc = FromArc(100f, 100f, 0.5f, -0.5f);
            Bezier3 inner = FromArc(100f, 103f, 0.5f, -0.5f);
            Bezier3 outer = FromArc(100f, 97f, 0.5f, -0.5f);
            LaneMath.Across(arc, inner, out float latInner, out bool innerFwd);
            LaneMath.Across(arc, outer, out float latOuter, out bool outerFwd);
            Program.Near(Math.Abs(latInner), 3f, 0.05f, "on a bend: 3 m off, one side: " + latInner);
            Program.Near(Math.Abs(latOuter), 3f, 0.05f, "and 3 m off the other: " + latOuter);
            Program.Check(latInner * latOuter < 0f, "on opposite sides");
            Program.Check(innerFwd == outerFwd, "both run the way the edge does");

            // 0.12.7, from the cross-section: a road of two lanes each way, 14 m wide, its lanes 1.75 and 5.25 m either side of the middle; the sides 7 m left and right of an edge along +Z.
            foreach (float x in new[] { -5.25f, -1.75f, 1.75f, 5.25f })
                Program.Near(LaneMath.CrossSectionLateral(edge, new Vec3(-7f, 0f, 20f), new Vec3(7f, 0f, 20f), x, 14f), x, 1e-3f, "a lane of the cross-section at " + x);
            // The same on a bend, the sides 7 m off the curve at its middle: the laterals are the cross-section's, each lane once (the lanes' own curves came in two halves each on such a segment).
            Vec3 middle = arc.Point(0.5f), right = arc.Tangent(0.5f).HeadingXZ().RightXZ();
            foreach (float x in new[] { -5.25f, -1.75f, 1.75f, 5.25f })
                Program.Near(LaneMath.CrossSectionLateral(arc, middle - right * 7f, middle + right * 7f, x, 14f), x, 0.02f, "and on a bend, at " + x);
            // A cross-section narrower than a metre is taken as a metre wide, as the game does.
            Program.Near(LaneMath.CrossSectionLateral(edge, new Vec3(-0.5f, 0f, 20f), new Vec3(0.5f, 0f, 20f), 0.25f, 0.4f), 0.25f, 1e-3f, "a narrow cross-section");

            // Where a segment is drawn: its left side from 12 to 70 m, its right from 9 to 74 (a bend): the lines go where both are, 12 to 70; either order; a stub of 1 m is no span.
            Program.Check(LaneMath.CommonSpan(12f, 9f, 70f, 74f, 2f, out float s0, out float s1) && Math.Abs(s0 - 12f) < 1e-4f && Math.Abs(s1 - 70f) < 1e-4f, "the part both sides have: " + s0 + " to " + s1);
            Program.Check(LaneMath.CommonSpan(70f, 74f, 12f, 9f, 2f, out s0, out s1) && Math.Abs(s0 - 12f) < 1e-4f && Math.Abs(s1 - 70f) < 1e-4f, "whichever way the sides were projected");
            Program.Check(!LaneMath.CommonSpan(10f, 10f, 11f, 11f, 2f, out _, out _) && !LaneMath.CommonSpan(float.NaN, 0f, 5f, 5f, 2f, out _, out _), "a stub or nothing is no span");

            // A collapsed edge (a point) does not throw, and gives some answer.
            LaneMath.Across(Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0f)), Bezier3.Line(new Vec3(2f, 0f, 0f), new Vec3(2f, 0f, 5f)), out lat, out fwd);
            Program.Check(!float.IsNaN(lat), "a collapsed edge gives a number");

            // Counting: eight lanes of which four are one lane drawn twice (a bus lane over a car lane) is four distinct lanes.
            var places = new List<float> { 1.5f, 1.6f, 4.5f, 4.4f, 7.5f, 7.5f, 10.5f, 10.7f };
            var way = new List<bool> { true, true, true, true, true, true, true, true };
            LaneMath.CountDistinct(places, way, out List<float> forward, out List<float> backward);
            Program.Check(forward.Count == 4 && backward.Count == 0, "eight lanes in four places are four lanes: " + forward.Count);
            Program.Check(forward[0] < forward[3], "sorted from the left of the edge's direction to the right");

            // Two lanes at one place that run opposite ways are two lanes (a two-way lane).
            LaneMath.CountDistinct(new List<float> { 2f, 2f }, new List<bool> { true, false }, out forward, out backward);
            Program.Check(forward.Count == 1 && backward.Count == 1, "a place with a lane each way is a lane each way");
            LaneMath.CountDistinct(new List<float>(), new List<bool>(), out forward, out backward);
            Program.Check(forward.Count == 0 && backward.Count == 0, "no lanes, no count");
        }

        private static void LaneArrangeMath()
        {
            // Lanes within half a metre are one (a bus lane drawn over a lane); those under the minimum are dropped; the rest from the largest X.
            List<float> r = LaneMath.Arrange(new[] { 3.7f, 11.7f, 11.9f, 7.7f, 19.7f, 0.1f, -3f, 15.7f }, 0.3f);
            Program.Check(r.Count == 5, "5 lanes after merging and dropping: " + r.Count);
            Program.Near(r[0], 19.7f, 1e-6f, "the leftmost first");
            Program.Near(r[4], 3.7f, 1e-6f, "the rightmost last");
            Program.Check(LaneMath.Arrange(new List<float>(), 0.3f).Count == 0, "nothing in, nothing out");
            Program.Check(LaneMath.Arrange(new[] { 0.1f, -4f }, 0.3f).Count == 0, "everything beside or behind the post is dropped");
            Program.Check(LaneMath.Arrange(new[] { 5f, 5f, 5.2f }, 0.3f).Count == 1, "equal lanes are one");
        }

        private static void LaneOnPosts()
        {
            SignDef p = Fixed("prohibition"), m = Fixed("mandatory");
            SupportDef pole = SupportCatalog.ById(SupportCatalog.SinglePole);
            var boards = Assembly.Order(new List<AssemblyBoard> { Laned(p, 0, 2, 9f), Laned(m, 1, 3, 4f) });
            AssemblyPlan plan = Assembly.Plan(boards, pole, 0f);
            Program.Check(!plan.Hung, "a post is not hung");
            foreach (Placement placement in plan.Placed)
                Program.Near(placement.Cx, plan.Layout.BoardCentreX, 1e-5f, "on a post the boards are on its middle line, whatever lane they were given");
            Program.Near(plan.Total.Width, Math.Max(p.Width, m.Width), 1e-4f, "stacked, the total is as wide as the widest");
        }

        private static ItemRecord LaneRecord(SignDef def, int lane, float offset, uint support = 0u, float reach = 0f) =>
            new ItemRecord(def.Hash, kAt, 0f, support, null, reach, 0u, 0, lane, offset);

        private static void LaneSingle()
        {
            SignDef p = Real("prohibition");
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            ItemRecord over = LaneRecord(p, 2, 7.5f, gantry.Hash);
            ItemRecord plain = LaneRecord(p, 0, 0f, gantry.Hash);

            SignParts viaRecord = ChunkBuilder.PartsOfRecord(over);
            Program.Check(viaRecord != null && viaRecord.Faces.Count == 1, "a lone board over a lane has its parts");
            Program.Near(CentreX(viaRecord.Faces[0].Mesh), 7.5f, 0.02f, "its face is over the lane");
            Program.Check(!ReferenceEquals(viaRecord, ChunkBuilder.PartsOfRecord(plain)), "a board that is not over a lane is built another way");
            Program.Check(ReferenceEquals(viaRecord, ChunkBuilder.PartsOfRecord(over)), "and the meshes are kept");
            Program.Check(ChunkBuilder.PartsOfRecord(new ItemRecord(4242u, kAt, 0f)) == null, "a type that is gone has no parts");

            // In a tile: drawn once, at the lane, with one metal mesh next to a plain sign.
            List<ChunkPart> parts = ChunkBuilder.Build(new[] { over, plain }, Vec3.Zero, out int skipped);
            Program.Check(skipped == 0, "nothing is skipped");
            int metal = 0;
            float minFaceX = float.MaxValue, maxFaceX = float.MinValue;
            foreach (ChunkPart part in parts)
            {
                if (part.Material == ChunkBuilder.MetalMaterial)
                    metal++;
                if (part.Material.StartsWith(ChunkBuilder.FacePrefix, StringComparison.Ordinal))
                {
                    part.Mesh.Bounds(out Vec3 lo, out Vec3 hi);
                    minFaceX = Math.Min(minFaceX, lo.X);
                    maxFaceX = Math.Max(maxFaceX, hi.X);
                }
            }

            Program.Check(metal == 1, "one metal mesh for the tile");
            Program.Check(maxFaceX > kAt.X + 7.5f - 0.5f, "the face over the lane is in the tile at its place: " + maxFaceX);

            // Picked as a group of one: the board is member 0, the post belongs to the support.
            SignParts pickParts = ChunkBuilder.PartsOfRecord(over);
            PickShape board = null;
            foreach (PickShape shape in pickParts.Picks)
            {
                if (shape.Name == "board")
                    board = shape;
            }

            Program.Check(board != null && board.Member == 0, "its board is member 0");
            var at = new Vec3((board.Min.X + board.Max.X) * 0.5f, (board.Min.Y + board.Max.Y) * 0.5f, 6f);
            bool hit = Picking.Pick(pickParts.Picks, Vec3.Zero, 0f, at, new Vec3(at.X, at.Y, -6f), out _, out PickShape hitShape);
            Program.Check(hit && hitShape.Member == 0, "a ray at the board over the lane hits it");
            var air = new Vec3(3f, board.Min.Y - 1.5f, 6f);
            Program.Check(!Picking.Pick(pickParts.Picks, Vec3.Zero, 0f, air, new Vec3(air.X, air.Y, -6f), out _), "the air under the beam between the post and the board is not picked");
        }

        private static void BoardsBeforeBeam()
        {
            SignDef p = Fixed("prohibition"), m = Fixed("mandatory"), w = Fixed("warning");
            var real = new[] { Real("prohibition"), Real("mandatory"), Real("warning") };
            foreach (string kind in new[] { SupportCatalog.Gantry, SupportCatalog.Cantilever })
            {
                SupportDef support = SupportCatalog.ById(kind);
                foreach (bool lanes in new[] { false, true })
                {
                    var boards = new List<AssemblyBoard>
                    {
                        Laned(p, 0, lanes ? 1 : 0, 9f), Laned(m, 1, lanes ? 2 : 0, 6f), Laned(w, 2, lanes ? 3 : 0, 3f), Laned(real[0], 3, lanes ? 4 : 0, 1.5f)
                    };
                    // Two metres apart, so that no board covers another.
                    for (int i = 0; i < boards.Count; i++)
                        boards[i].OffsetX = 9f - 2f * i;

                    SignParts parts = SignBuilder.BuildAssembly(boards, support, 0f);
                    var seen = new HashSet<int>();
                    foreach (PickShape shape in parts.Picks)
                    {
                        if (shape.Name != "board")
                            continue;
                        var at = new Vec3((shape.Min.X + shape.Max.X) * 0.5f, (shape.Min.Y + shape.Max.Y) * 0.5f, 6f);
                        bool hit = Picking.Pick(parts.Picks, Vec3.Zero, 0f, at, new Vec3(at.X, at.Y, -6f), out _, out PickShape hitShape);
                        Program.Check(hit && hitShape.Name == "board" && hitShape.Member == shape.Member, $"{kind}{(lanes ? " over lanes" : "")}: a ray at the middle of board {shape.Member} hits the board, not the beam (it hit {(hit ? hitShape.Name : "nothing")})");
                        seen.Add(shape.Member);
                    }

                    Program.Check(seen.Count == 4, $"{kind}: all four boards can be picked: {seen.Count}");
                }
            }
        }

        private static void LaneDiff()
        {
            SignDef p = Real("prohibition");
            ItemRecord a = LaneRecord(p, 0, 0f), b = LaneRecord(p, 2, 6f), c = LaneRecord(p, 2, 9f), d = LaneRecord(p, 3, 6f);
            Program.Check(TileDiff.Same(a, a) && TileDiff.Same(b, b), "an item is the same as itself");
            Program.Check(!TileDiff.Same(a, b) && !TileDiff.Same(b, c) && !TileDiff.Same(b, d), "another lane or another place over it is a change");
            var was = new Dictionary<int, ItemRecord> { { 1, b } };
            var now = new Dictionary<int, ItemRecord> { { 1, c } };
            Program.Check(TileDiff.Dirty(was, now).Contains(Tiles.Of(kAt.X, kAt.Z)), "the tile of a board that moved to another lane is rebuilt");

            // Two sets of members that differ only in a lane are built apart.
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            SignDef m = Real("mandatory");
            var one = new List<ItemRecord> { LaneRecord(p, 2, 4f, gantry.Hash), LaneRecord(m, 0, 0f, gantry.Hash) };
            one[0] = withGroup(one[0], 31u, 0);
            one[1] = withGroup(one[1], 31u, 1);
            var two = new List<ItemRecord> { withGroup(LaneRecord(p, 2, 8f, gantry.Hash), 31u, 0), withGroup(LaneRecord(m, 0, 0f, gantry.Hash), 31u, 1) };
            Program.Check(!ReferenceEquals(ChunkBuilder.PartsOfGroup(one), ChunkBuilder.PartsOfGroup(two)), "members that differ in the place over a lane are built apart");
            Program.Check(ReferenceEquals(ChunkBuilder.PartsOfGroup(one), ChunkBuilder.PartsOfGroup(one)), "and kept");
        }

        private static ItemRecord withGroup(ItemRecord r, uint group, int slot)
        {
            r.Group = group;
            r.Slot = slot;
            return r;
        }

        private static SignDef Real(string group, int skip = 0)
        {
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Group == group && skip-- == 0)
                    return def;
            }

            Program.Check(false, "the catalogue has a sign of the group " + group);
            return SignCatalog.All[0];
        }

        private static readonly Vec3 kAt = new Vec3(100f, 3f, 200f);

        private static ItemRecord Record(SignDef def, uint group, int slot, uint support = 0u, float reach = 0f, string spec = null) =>
            new ItemRecord(def.Hash, kAt, 0.5f, support, spec, reach, group, slot);

        private static void GroupOrder()
        {
            SignDef p = Real("prohibition"), m = Real("mandatory"), w = Real("warning");
            ItemRecord a = Record(w, 9u, 2), b = Record(p, 9u, 0), c = Record(m, 9u, 1), tie = Record(p, 9u, 0, spec: "x=1");
            Program.Check(ItemGroups.Compare(b, c) < 0 && ItemGroups.Compare(c, a) < 0 && ItemGroups.Compare(a, b) > 0, "members are in the order of their slots");
            Program.Check(ItemGroups.Compare(b, b) == 0, "a member is equal to itself");
            Program.Check(ItemGroups.Compare(b, tie) != 0 && ItemGroups.Compare(b, tie) == -ItemGroups.Compare(tie, b), "two members of one slot are told apart, the same way round every time");

            var keyed = new List<KeyValuePair<int, ItemRecord>>
            {
                new KeyValuePair<int, ItemRecord>(1, a), new KeyValuePair<int, ItemRecord>(2, b), new KeyValuePair<int, ItemRecord>(3, c), new KeyValuePair<int, ItemRecord>(4, tie)
            };
            List<KeyValuePair<int, ItemRecord>> sorted = ItemGroups.Sorted(keyed, Comparer<int>.Default);
            Program.Check(sorted.Count == 4 && sorted[3].Key == 1 && sorted[2].Key == 3, "sorted by slot: the warning sign last, the mandatory one before it");
            var again = new List<KeyValuePair<int, ItemRecord>>(keyed);
            again.Reverse();
            List<KeyValuePair<int, ItemRecord>> sortedAgain = ItemGroups.Sorted(again, Comparer<int>.Default);
            bool same = sorted.Count == sortedAgain.Count;
            for (int i = 0; same && i < sorted.Count; i++)
                same = sorted[i].Key == sortedAgain[i].Key;
            Program.Check(same, "the order does not depend on the order the records came in");

            var singles = new List<KeyValuePair<int, ItemRecord>>();
            var groups = new Dictionary<uint, List<KeyValuePair<int, ItemRecord>>>();
            keyed.Add(new KeyValuePair<int, ItemRecord>(5, Record(p, 0u, 0)));
            ItemGroups.Split(keyed, singles, groups);
            Program.Check(singles.Count == 1 && singles[0].Key == 5 && groups.Count == 1 && groups[9u].Count == 4, "a sign with no group is on a support of its own; the others are one group");

            var records = new List<ItemRecord> { a, b, c };
            Program.Check(ItemGroups.NextSlot(records) == 3 && ItemGroups.NextSlot(new List<ItemRecord>()) == 0, "a board that is added goes after the others");
            var random = new Random(1);
            var seen = new HashSet<uint>();
            for (int i = 0; i < 200; i++)
            {
                uint id = ItemGroups.NewId(records, random);
                Program.Check(id != 0u && id != 9u && seen.Add(id), "a new group id is not 0, not in use and not one that was given before");
            }
        }

        private static void GroupDrawn()
        {
            SignDef p = Real("prohibition"), m = Real("mandatory"), aux = Real("auxiliary");
            var group = new List<ItemRecord> { Record(p, 5u, 0), Record(aux, 5u, 1), Record(m, 5u, 2) };
            List<ChunkPart> parts = ChunkBuilder.Build(group, Vec3.Zero, out int skipped);
            Program.Check(skipped == 0, "nothing of the group is skipped");
            int metal = 0, faces = 0, vertices = 0;
            var faceKeys = new HashSet<string>();
            foreach (ChunkPart part in parts)
            {
                vertices += part.Mesh.VertexCount;
                if (part.Material == ChunkBuilder.MetalMaterial)
                    metal++;
                if (part.Material.StartsWith(ChunkBuilder.FacePrefix, StringComparison.Ordinal))
                {
                    faces++;
                    faceKeys.Add(part.Material);
                }
            }

            Program.Check(metal == 1, "one metal mesh for the support and all its boards: " + metal);
            Program.Check(faces == 3 && faceKeys.Count == 3, "a face mesh for each of the three boards, each with its own picture: " + faces);

            // The same group in another order of the list draws the same.
            var reversed = new List<ItemRecord>(group);
            reversed.Reverse();
            int vertices2 = 0;
            foreach (ChunkPart part in ChunkBuilder.Build(reversed, Vec3.Zero, out _))
                vertices2 += part.Mesh.VertexCount;
            Program.Check(vertices == vertices2, "the order of the records does not change the drawing");

            // One support for all three: fewer vertices than three supports of their own.
            int separate = 0;
            foreach (ItemRecord one in group)
            {
                ItemRecord alone = one;
                alone.Group = 0u;
                foreach (ChunkPart part in ChunkBuilder.Build(new[] { alone }, Vec3.Zero, out _))
                    separate += part.Mesh.VertexCount;
            }

            Program.Check(vertices < separate, "three boards on one support are cheaper than three supports: " + vertices + " vs " + separate);

            // Placed at the lead's position, relative to the origin of the tile.
            var tile = new Vec3(96f, 0f, 192f);
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (ChunkPart part in ChunkBuilder.Build(group, tile, out _))
            {
                part.Mesh.Bounds(out Vec3 lo, out Vec3 hi);
                minX = Math.Min(minX, lo.X);
                maxX = Math.Max(maxX, hi.X);
            }

            Program.Check(minX > kAt.X - tile.X - 8f && maxX < kAt.X - tile.X + 8f, "the group stands at the place of its lead: " + minX + " to " + maxX);

            // Next to a sign of its own, in the same tile.
            var withSingle = new List<ItemRecord>(group) { Record(p, 0u, 0) };
            int metalMeshes = 0;
            foreach (ChunkPart part in ChunkBuilder.Build(withSingle, Vec3.Zero, out _))
            {
                if (part.Material == ChunkBuilder.MetalMaterial)
                    metalMeshes++;
            }

            Program.Check(metalMeshes == 1, "one tile, one metal mesh, whatever it holds");

            // The support of the lead: a double post asked for is the one that is drawn, whatever the planner would say of the whole.
            SupportDef doublePole = SupportCatalog.ById(SupportCatalog.DoublePole);
            var onDouble = new List<ItemRecord> { Record(p, 6u, 0, doublePole.Hash), Record(m, 6u, 1, doublePole.Hash) };
            Program.Check(ChunkBuilder.PartsOfGroup(onDouble).Layout.Kind == SupportKind.DoublePole, "the lead's support is the one that is drawn");
            Program.Check(ReferenceEquals(ChunkBuilder.PartsOfGroup(onDouble), ChunkBuilder.PartsOfGroup(onDouble)), "the meshes of a group are built once");
        }

        private static void GroupUnknown()
        {
            SignDef p = Real("prohibition");
            var ghost = new ItemRecord(12345u, kAt, 0f, 0u, null, 0f, 8u, 1);
            var mixed = new List<ItemRecord> { Record(p, 8u, 0), ghost };
            List<ChunkPart> parts = ChunkBuilder.Build(mixed, Vec3.Zero, out int skipped);
            Program.Check(skipped == 0 && parts.Count > 0, "a member of a type that is gone does not stop the others being drawn");
            List<ChunkPart> none = ChunkBuilder.Build(new[] { ghost, new ItemRecord(12346u, kAt, 0f, 0u, null, 0f, 8u, 2) }, Vec3.Zero, out int skippedAll);
            Program.Check(none.Count == 0 && skippedAll == 2, "a group of types that are all gone is skipped and counted: " + skippedAll);
            Program.Check(ChunkBuilder.PartsOfGroup(new[] { ghost }) == null, "no board, no parts");
            var bad = new ItemRecord(p.Hash, new Vec3(float.NaN, 0f, 0f), 0f, 0u, null, 0f, 8u, 0);
            ChunkBuilder.Build(new[] { bad, Record(p, 8u, 1) }, Vec3.Zero, out int skippedBad);
            Program.Check(skippedBad == 2, "a lead that is not a place takes the group with it: " + skippedBad);
        }

        private static void GroupDiff()
        {
            SignDef p = Real("prohibition");
            ItemRecord before = Record(p, 0u, 0);
            ItemRecord grouped = Record(p, 4u, 0);
            ItemRecord slotted = Record(p, 4u, 1);
            Program.Check(TileDiff.Same(before, before) && !TileDiff.Same(before, grouped) && !TileDiff.Same(grouped, slotted), "a change of group or slot is a change");
            var was = new Dictionary<int, ItemRecord> { { 1, before } };
            var now = new Dictionary<int, ItemRecord> { { 1, grouped } };
            Program.Check(TileDiff.Dirty(was, now).Contains(Tiles.Of(kAt.X, kAt.Z)), "the tile of an item that joined a group is rebuilt");
        }

        private static void GroupPick()
        {
            SignDef p = Real("prohibition"), m = Real("mandatory"), aux = Real("auxiliary");
            // In the order of Compare: member 0 is the first slot, and so on; the order the boards hang in is another thing.
            var members = new List<ItemRecord> { Record(p, 3u, 0), Record(aux, 3u, 1), Record(m, 3u, 2) };
            SignParts parts = ChunkBuilder.PartsOfGroup(members);
            var found = new HashSet<int>();
            foreach (PickShape shape in parts.Picks)
            {
                if (shape.Name == "board")
                    found.Add(shape.Member);
            }

            Program.Check(found.Count == 3 && found.Contains(0) && found.Contains(1) && found.Contains(2), "every member's board can be hit");
            foreach (PickShape shape in parts.Picks)
            {
                if (shape.Name != "board")
                    continue;
                var at = new Vec3((shape.Min.X + shape.Max.X) * 0.5f, (shape.Min.Y + shape.Max.Y) * 0.5f, 6f);
                Vec3 start = kAt + RotateY(at, 0.5f);
                Vec3 end = kAt + RotateY(new Vec3(at.X, at.Y, -6f), 0.5f);
                bool hit = Picking.Pick(parts.Picks, kAt, 0.5f, start, end, out _, out PickShape hitShape);
                Program.Check(hit && hitShape.Member == shape.Member, "a ray at the middle of member " + shape.Member + " hits that member's board (hit " + (hit ? hitShape.Member.ToString() : "nothing") + ")");
            }

            // The post is no board's.
            PickShape post = null;
            foreach (PickShape shape in parts.Picks)
            {
                if (shape.Name == "post")
                    post = shape;
            }

            Program.Check(post != null && post.Member == -1, "a post belongs to the support, not to a board");
        }

        /// <summary>A point of the sign's frame in the world's frame, for a sign turned by yaw about Y (the same turn the tool and the renderer use).</summary>
        private static Vec3 RotateY(Vec3 p, float yaw)
        {
            float cos = (float)Math.Cos(yaw), sin = (float)Math.Sin(yaw);
            return new Vec3(p.X * cos + p.Z * sin, p.Y, -p.X * sin + p.Z * cos);
        }

        private static void GroupLayout()
        {
            SignDef p = Real("prohibition"), m = Real("mandatory"), w = Real("warning");
            var members = new List<ItemRecord> { Record(p, 2u, 0), Record(m, 2u, 1), Record(w, 2u, 2) };
            bool ok = ItemGroups.LayoutOf(members, out SupportLayout layout, out SupportDef support, out SignDef total, out List<AssemblyBoard> ordered);
            Program.Check(ok && ordered.Count == 3 && support != null && total != null, "a group has a layout");
            SignParts parts = ChunkBuilder.PartsOfGroup(members);
            Program.Check(layout.Kind == parts.Layout.Kind && Math.Abs(layout.PoleTop - parts.Layout.PoleTop) < 1e-4f && Math.Abs(layout.BoardCentreX - parts.Layout.BoardCentreX) < 1e-4f, "the layout of the group is the one it is built with");
            Program.Check(total.Height > p.Height + m.Height * 0.99f, "stacked, the whole is as high as its boards together: " + total.Height);

            var ghost = new ItemRecord(777u, kAt, 0f, 0u, null, 0f, 2u, 0);
            Program.Check(!ItemGroups.LayoutOf(new[] { ghost }, out _, out _, out _, out _), "no board of a known type, no layout");

            // The reach of the lead counts: a gantry of another span.
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            var narrow = new List<ItemRecord> { Record(p, 2u, 0, gantry.Hash, 10f), Record(m, 2u, 1, gantry.Hash, 10f) };
            var wide = new List<ItemRecord> { Record(p, 2u, 0, gantry.Hash, 20f), Record(m, 2u, 1, gantry.Hash, 20f) };
            ItemGroups.LayoutOf(narrow, out SupportLayout a, out _, out _, out _);
            ItemGroups.LayoutOf(wide, out SupportLayout b, out _, out _, out _);
            Program.Near(a.ArmLength, 10f, 1e-4f, "a span of 10 m for the group");
            Program.Near(b.ArmLength, 20f, 1e-4f, "a span of 20 m for the group");
            Program.Check(!ReferenceEquals(ChunkBuilder.PartsOfGroup(narrow), ChunkBuilder.PartsOfGroup(wide)), "another span is another set of meshes");
        }

        private static readonly Dictionary<string, SignDef> s_Fixed = new Dictionary<string, SignDef>();

        /// <summary>
        /// A plain sign of a group, made here: the order and the room do not depend on what the catalogue holds of a group (the signs of the chapters come with the packs of the catalogue), only on the
        /// group and the size. The same def comes back for the same group and number.
        /// </summary>
        private static SignDef Fixed(string group, int skip = 0)
        {
            string id = "test." + group + "." + skip;
            if (s_Fixed.TryGetValue(id, out SignDef made))
                return made;
            bool round = group == "prohibition" || group == "mandatory";
            float width = round ? 0.8f + 0.1f * skip : group == "auxiliary" ? 0.8f : 0.9f;
            float height = round ? width : group == "auxiliary" ? 0.4f : 0.8f;
            made = new SignDef
            {
                Id = id,
                Code = id,
                Zh = id,
                En = id,
                Group = group,
                Shape = round ? ShapeKind.Circle : ShapeKind.RoundRect,
                CornerRadius = round ? 0f : 0.05f,
                Width = width,
                Height = height,
                TextureWidth = (int)(width * 200f),
                TextureHeight = (int)(height * 200f)
            };
            s_Fixed[id] = made;
            return made;
        }

        private static AssemblyBoard Board(SignDef def, int slot, int member = -1, string spec = null) =>
            new AssemblyBoard { Instance = SignInstances.Of(def, spec), Slot = slot, Member = member < 0 ? slot : member };

        private static string Names(IReadOnlyList<AssemblyBoard> boards)
        {
            var parts = new List<string>();
            foreach (AssemblyBoard b in boards)
                parts.Add(b.Instance.Def.Group + "#" + b.Member);
            return string.Join(", ", parts);
        }

        private static void Ordering()
        {
            SignDef prohibition = Fixed("prohibition"), mandatory = Fixed("mandatory"), warning = Fixed("warning");

            // Put on in any order, they come out as prohibition, mandatory, warning (4.8.6).
            List<AssemblyBoard> ordered = Assembly.Order(new[] { Board(warning, 0), Board(prohibition, 1), Board(mandatory, 2) });
            Program.Check(ordered.Count == 3 && ordered[0].Instance.Def == prohibition && ordered[1].Instance.Def == mandatory && ordered[2].Instance.Def == warning,
                "prohibition, mandatory, warning whatever the order they were added: " + Names(ordered));

            // Two of one rank keep the order they were added in.
            SignDef prohibition2 = Fixed("prohibition", 1);
            ordered = Assembly.Order(new[] { Board(prohibition2, 0), Board(warning, 1), Board(prohibition, 2) });
            Program.Check(ordered[0].Instance.Def == prohibition2 && ordered[1].Instance.Def == prohibition && ordered[2].Instance.Def == warning,
                "equal ranks keep their order: " + Names(ordered));

            // The order does not depend on the order of the list that is given.
            var shuffled = new List<AssemblyBoard> { Board(mandatory, 2), Board(warning, 0), Board(prohibition, 1) };
            List<AssemblyBoard> again = Assembly.Order(shuffled);
            Program.Check(Names(again) == Names(Assembly.Order(new[] { Board(warning, 0), Board(prohibition, 1), Board(mandatory, 2) })), "the list's order does not matter, the slots do");

            // Anything that is not one of the three comes after them.
            SignDef guide = null;
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Template != null && def.Group == "guide")
                {
                    guide = def;
                    break;
                }
            }

            Program.Check(guide != null, "there is a guide board in the catalogue");
            if (guide != null)
            {
                ordered = Assembly.Order(new[] { Board(guide, 0), Board(warning, 1) });
                Program.Check(ordered[0].Instance.Def == warning && ordered[1].Instance.Def.Id == guide.Id, "a guide board after the warning sign: " + Names(ordered));
            }
        }

        private static void AuxiliaryOrder()
        {
            SignDef prohibition = Fixed("prohibition"), mandatory = Fixed("mandatory"), warning = Fixed("warning"), aux = Fixed("auxiliary"), aux2 = Fixed("auxiliary", 1);

            // The auxiliary sign added after the prohibition sign is under it, ahead of the mandatory one added later.
            List<AssemblyBoard> ordered = Assembly.Order(new[] { Board(prohibition, 0), Board(aux, 1), Board(mandatory, 2) });
            Program.Check(ordered[0].Instance.Def == prohibition && ordered[1].Instance.Def == aux && ordered[2].Instance.Def == mandatory, "the auxiliary sign follows its board: " + Names(ordered));

            // It stays with its board when the ranks would move that board: a warning sign with its auxiliary sign, then a prohibition sign: the pair of the warning sign stays together.
            ordered = Assembly.Order(new[] { Board(warning, 0), Board(aux, 1), Board(prohibition, 2) });
            Program.Check(ordered.Count == 3 && ordered[0].Instance.Def == prohibition && ordered[1].Instance.Def == warning && ordered[2].Instance.Def == aux, "the auxiliary sign stays under the warning sign: " + Names(ordered));

            // Two auxiliary signs of one board: in the order they were added.
            ordered = Assembly.Order(new[] { Board(prohibition, 0), Board(aux2, 2), Board(aux, 1) });
            Program.Check(ordered[0].Instance.Def == prohibition && ordered[1].Instance.Def == aux && ordered[2].Instance.Def == aux2, "two auxiliary signs under one board keep their order: " + Names(ordered));

            // One that was added before any board has nothing to explain: it goes first rather than being lost.
            ordered = Assembly.Order(new[] { Board(aux, 0), Board(prohibition, 1) });
            Program.Check(ordered.Count == 2 && ordered[0].Instance.Def == aux, "an auxiliary sign with no board before it is first: " + Names(ordered));

            Program.Check(Assembly.IsAuxiliary(aux) && !Assembly.IsAuxiliary(prohibition), "the group tells what is auxiliary");
        }

        private static void MostFour()
        {
            SignDef p = Fixed("prohibition"), m = Fixed("mandatory"), w = Fixed("warning"), p2 = Fixed("prohibition", 1), m2 = Fixed("mandatory", 1);

            // Five boards stay five: the order does not cut at four any more (a gantry carries twelve); a post's and an arm's four is Attach's.
            List<AssemblyBoard> five = Assembly.Order(new[] { Board(p, 0), Board(m, 1), Board(w, 2), Board(p2, 3), Board(m2, 4) });
            Program.Check(five.Count == 5, "the order keeps five boards: " + five.Count);
            Program.Check(Assembly.MaxBoards == 4 && Assembly.MaxBoardsGantry == 12 && Assembly.HardMax == 12, "four on a post or an arm, twelve on a gantry");
            Program.Check(Assembly.MaxFor(SupportKind.Gantry) == 12 && Assembly.MaxFor(SupportKind.Cantilever) == 4 && Assembly.MaxFor(SupportKind.SinglePole) == 4
                && Assembly.MaxFor(SupportKind.DoublePole) == 4 && Assembly.MaxFor(SupportKind.TriplePole) == 4, "the limit by kind of support");

            // Thirteen are cut to twelve, the one added last is the one left off.
            var thirteen = new List<AssemblyBoard>();
            for (int i = 0; i < 13; i++)
                thirteen.Add(Board(i % 2 == 0 ? p : m, i));
            List<AssemblyBoard> ordered = Assembly.Order(thirteen);
            Program.Check(ordered.Count == Assembly.HardMax, "twelve boards at the most: " + ordered.Count);
            bool lost = true;
            foreach (AssemblyBoard b in ordered)
                lost &= b.Slot != 12;
            Program.Check(lost, "the board added last is the one left off: " + Names(ordered));
            Program.Check(Assembly.Order(new List<AssemblyBoard>()).Count == 0, "no boards, no order");
        }

        private static void Totals()
        {
            SignDef a = Fixed("prohibition"), b = Fixed("mandatory"), c = Fixed("warning");
            var boards = new List<AssemblyBoard> { Board(a, 0), Board(b, 1), Board(c, 2) };
            List<AssemblyBoard> ordered = Assembly.Order(boards);

            Program.Check(ReferenceEquals(Assembly.TotalDef(Assembly.Order(new[] { Board(a, 0) }), false), a), "one board is its own total");

            SignDef stacked = Assembly.TotalDef(ordered, false);
            float widest = 0f, heights = 0f;
            foreach (AssemblyBoard board in ordered)
            {
                widest = Math.Max(widest, board.Instance.Def.Width);
                heights += board.Instance.Def.Height;
            }

            Program.Near(stacked.Width, widest, 1e-4f, "stacked: as wide as the widest");
            Program.Near(stacked.Height, heights + Assembly.StackGap * (ordered.Count - 1), 1e-4f, "stacked: as high as all of them with the gaps");

            SignDef hung = Assembly.TotalDef(ordered, true);
            float widths = 0f, highest = 0f;
            foreach (AssemblyBoard board in ordered)
            {
                widths += board.Instance.Def.Width;
                highest = Math.Max(highest, board.Instance.Def.Height);
            }

            Program.Near(hung.Width, widths + Assembly.SideGap * (ordered.Count - 1), 1e-4f, "hung: as wide as all of them with the gaps");
            Program.Near(hung.Height, highest, 1e-4f, "hung: as high as the highest");
            Program.Check(!ReferenceEquals(stacked, a) && stacked.Template == null, "the total is a plain rectangle, and the first board's def is left alone");
            Program.Near(a.Width, ordered[0].Instance.Def.Width, 1e-6f, "the board's own def is not changed by the total");

            // A plate on top of a board that is not the top one takes room in the stack: the board above is further up. A guide board with one is the test.
            SignDef exit = null;
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Template != null && SignInstances.Of(def, null).Def.ExtraTop > 0.01f)
                {
                    exit = def;
                    break;
                }
            }

            Program.Check(exit != null, "there is a guide board with a plate on top of it");
            if (exit != null)
            {
                SignDef top = Fixed("prohibition");
                var withPlate = Assembly.Order(new[] { Board(top, 0), Board(exit, 1) });
                // Rank 3 for the guide board: it is the lower one, and its plate is between it and the prohibition sign.
                Program.Check(withPlate[1].Instance.Def.ExtraTop > 0.01f, "the guide board is the lower one");
                SignDef total = Assembly.TotalDef(withPlate, false);
                float expected = withPlate[0].Instance.Def.Height + withPlate[1].Instance.Def.Height + withPlate[1].Instance.Def.ExtraTop + Assembly.StackGap;
                Program.Near(total.Height, expected, 1e-4f, "the plate of the lower board is counted in the stack");
                Program.Near(total.ExtraTop, withPlate[0].Instance.Def.ExtraTop, 1e-6f, "the plate on top of the whole is the top board's own");
            }
        }

        private static IEnumerable<SupportDef> SupportsFor(SignDef total)
        {
            foreach (SupportDef support in SupportCatalog.All)
            {
                if (support.Kind == SupportKind.DoublePole && total.Width < 0.7f)
                    continue;
                if (support.Kind == SupportKind.TriplePole && total.Width < 3.0f)
                    continue;
                yield return support;
            }
        }

        private static void Arrangement()
        {
            SignDef p = Fixed("prohibition"), m = Fixed("mandatory"), w = Fixed("warning");
            List<AssemblyBoard> ordered = Assembly.Order(new[] { Board(w, 0), Board(p, 1), Board(m, 2) });

            // Stacked on a post.
            SignDef stackedTotal = Assembly.TotalDef(ordered, false);
            SupportDef pole = SupportPlanner.Auto(stackedTotal);
            SupportLayout layout = SupportLayouts.Of(stackedTotal, pole);
            List<Placement> stacked = Assembly.Arrange(ordered, false, layout);
            Program.Check(stacked.Count == 3, "three placements");
            Program.Near(stacked[2].Bottom, layout.BottomClearance, 1e-5f, "stacked: the lowest board is at the clearance");
            for (int i = 0; i < stacked.Count; i++)
            {
                Program.Near(stacked[i].Cx, layout.BoardCentreX, 1e-5f, "stacked: on the middle line of the support");
                Program.Check(ReferenceEquals(stacked[i].Board, ordered[i]), "stacked: placements are in the order of the boards");
                if (i > 0)
                {
                    float gap = stacked[i - 1].Bottom - stacked[i].Top - stacked[i].Def.ExtraTop;
                    Program.Near(gap, Assembly.StackGap, 1e-4f, "stacked: the gap between two boards");
                }
            }

            Program.Near(stacked[0].Top, layout.BottomClearance + stackedTotal.Height, 1e-4f, "stacked: the top of the top board is the top of the whole");

            // Side by side under an arm or a beam.
            SignDef hungTotal = Assembly.TotalDef(ordered, true);
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            Program.Check(gantry != null, "the gantry is in the catalogue");
            if (gantry == null)
                return;
            SupportLayout beam = SupportLayouts.Of(hungTotal, gantry);
            List<Placement> hung = Assembly.Arrange(ordered, true, beam);
            float lowest = float.MaxValue, left = float.MinValue, right = float.MaxValue;
            for (int i = 0; i < hung.Count; i++)
            {
                Program.Near(hung[i].Bottom, beam.BottomClearance, 1e-5f, "hung: lower edges level at the clearance (4.8.12 c)");
                lowest = Math.Min(lowest, hung[i].Bottom);
                left = Math.Max(left, hung[i].Cx + hung[i].Def.Width * 0.5f);
                right = Math.Min(right, hung[i].Cx - hung[i].Def.Width * 0.5f);
                if (i > 0)
                {
                    Program.Check(hung[i].Cx < hung[i - 1].Cx, "hung: the first of the order is at the viewer's left, which is the larger X in the sign's frame");
                    float gap = (hung[i - 1].Cx - hung[i - 1].Def.Width * 0.5f) - (hung[i].Cx + hung[i].Def.Width * 0.5f);
                    Program.Near(gap, Assembly.SideGap, 1e-4f, "hung: the gap between two boards");
                }
            }

            Program.Near((left + right) * 0.5f, beam.BoardCentreX, 1e-4f, "hung: all of them are centred on the beam");
            Program.Near(left - right, hungTotal.Width, 1e-4f, "hung: they take the room the support was made for");
        }

        private static void OneBoard()
        {
            foreach (SignDef def in SignCatalog.All)
            {
                SignInstance instance = SignInstances.Of(def, null);
                SignParts planned = SignBuilder.BuildAssembly(new[] { new AssemblyBoard { Instance = instance, Slot = 0, Member = 0 } }, null, 0f);
                Program.Check(planned.Layout.Kind == SupportPlanner.Auto(instance.Def).Kind, def.Id + ": the support for one board is the planner's for it");
                SignParts direct = SignBuilder.Build(instance, SupportPlanner.Auto(instance.Def), 0f);
                Program.Check(planned.Face.VertexCount == direct.Face.VertexCount && planned.Metal.VertexCount == direct.Metal.VertexCount, def.Id + ": one board is built the same by both ways");
                int members = 0;
                foreach (PickShape shape in planned.Picks)
                {
                    if (shape.Name == "board")
                    {
                        members++;
                        Program.Check(shape.Member == 0, def.Id + ": its board is member 0");
                    }
                    else
                    {
                        Program.Check(shape.Member == -1, def.Id + ": a part of the support belongs to no board");
                    }
                }

                Program.Check(members >= 1, def.Id + ": a board to pick");
            }
        }

        private static IEnumerable<List<AssemblyBoard>> Sets()
        {
            SignDef p = Fixed("prohibition"), m = Fixed("mandatory"), w = Fixed("warning"), a = Fixed("auxiliary");
            yield return new List<AssemblyBoard> { Board(p, 0), Board(m, 1) };
            yield return new List<AssemblyBoard> { Board(w, 0), Board(p, 1), Board(m, 2) };
            yield return new List<AssemblyBoard> { Board(p, 0), Board(a, 1), Board(m, 2), Board(w, 3) };
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Template != null && SignInstances.Of(def, null).Def.ExtraTop > 0.01f)
                {
                    yield return new List<AssemblyBoard> { Board(p, 0), Board(def, 1) };
                    break;
                }
            }
        }

        private static void ManyBoards()
        {
            foreach (List<AssemblyBoard> boards in Sets())
            {
                List<AssemblyBoard> ordered = Assembly.Order(boards);
                string set = Names(ordered);
                var supports = new List<SupportDef> { null };
                foreach (SupportDef support in SupportsFor(Assembly.TotalDef(ordered, false)))
                    supports.Add(support);

                foreach (SupportDef support in supports)
                {
                    string what = set + " on " + (support == null ? "the planner's support" : support.Id);
                    SignParts parts = SignBuilder.BuildAssembly(boards, support, 0f);
                    SupportLayout layout = parts.Layout;
                    bool hung = SupportKinds.Hung(layout.Kind);
                    if (support != null)
                        Program.Check(layout.Kind == support.Kind, what + ": the support is the one asked for");

                    // Every mesh is sound.
                    var meshes = new List<MeshBuilder> { parts.Metal, parts.Concrete };
                    foreach (FacePart facePart in parts.Faces)
                        meshes.Add(facePart.Mesh);
                    foreach (MeshBuilder mesh in meshes)
                    {
                        Program.Check(mesh.Indices.Count % 3 == 0 && mesh.Normals.Count == mesh.VertexCount && mesh.U.Count == mesh.VertexCount, what + ": the meshes agree");
                        foreach (int index in mesh.Indices)
                        {
                            if (index < 0 || index >= mesh.VertexCount)
                            {
                                Program.Check(false, what + ": an index points outside the vertices");
                                break;
                            }
                        }
                    }

                    foreach (FacePart facePart in parts.Faces)
                    {
                        for (int i = 0; i < facePart.Mesh.VertexCount; i++)
                        {
                            if (!(facePart.Mesh.Normals[i].Z > 0.999f))
                            {
                                Program.Check(false, what + ": a face looks away from the road");
                                break;
                            }
                        }
                    }

                    // One face for each board, in the order they hang, each with its own picture.
                    Program.Check(parts.Faces.Count == ordered.Count, what + ": a face for each board: " + parts.Faces.Count);
                    for (int i = 0; i < parts.Faces.Count && i < ordered.Count; i++)
                        Program.Check(ReferenceEquals(parts.Faces[i].Instance, ordered[i].Instance), what + ": face " + i + " has the picture of board " + i);

                    parts.FaceBounds(out Vec3 fMin, out Vec3 fMax);
                    parts.Metal.Bounds(out Vec3 mMin, out Vec3 mMax);

                    // The faces together are as high or as wide as the arrangement says.
                    List<Placement> placed = Assembly.Arrange(ordered, hung, layout);
                    float lowest = float.MaxValue, highest = float.MinValue;
                    foreach (Placement pl in placed)
                    {
                        lowest = Math.Min(lowest, pl.Bottom);
                        highest = Math.Max(highest, pl.Top + pl.Def.ExtraTop);
                    }

                    for (int i = 0; i < parts.Faces.Count && i < placed.Count; i++)
                    {
                        parts.Faces[i].Mesh.Bounds(out Vec3 lo, out Vec3 hi);
                        Program.Near(lo.Y, placed[i].Bottom, 0.01f, what + ": board " + i + " hangs where it was placed");
                        Program.Check(placed[i].Cx >= lo.X - 0.01f && placed[i].Cx <= hi.X + 0.01f, what + ": board " + i + " is round the place it was given");
                    }

                    Program.Near(fMin.Y, layout.BottomClearance, 2e-3f, what + ": the lowest edge of all is at the clearance");
                    Program.Check(fMax.Y <= highest + 0.02f, what + ": nothing above the boards and the plates on them: " + fMax.Y + " vs " + highest);
                    Program.Check(hung || layout.Hanger || layout.Kind == SupportKind.WallFlat || layout.Kind == SupportKind.AttachedArm || (layout.Kind == SupportKind.WallBracket ? layout.PoleTop >= fMax.Y + 0.09f : layout.PoleTop <= fMax.Y + 1e-3f && layout.PoleTop > lowest),
                        what + ": the post ends behind the highest board, a bracket's tie above them (flat on a wall and on a signal arm there is none)");
                    Program.Check(!hung || layout.PoleTop >= layout.ArmY - layout.CrankRise + (layout.Kind == SupportKind.Gantry ? layout.BeamDepth * 0.5f : 0f), what + ": the posts rise to the beam (a cranked arm from its root)");
                    Program.Check(mMax.Y >= fMax.Y, what + ": the support rises above the faces");
                    if (hung)
                    {
                        // The tube passes behind the boards: the arm inside the height of the tallest of them; a truss overlapping it. Nothing hangs below it but the boards themselves.
                        float tallestTop = layout.BottomClearance;
                        float tallest = 0f;
                        foreach (Placement pl in placed)
                        {
                            if (pl.Top - layout.BottomClearance > tallest)
                            {
                                tallest = pl.Top - layout.BottomClearance;
                                tallestTop = pl.Top;
                            }
                        }

                        if (layout.Framed)
                        {
                            // The framed cantilever (0.12.57): its arm over the frames of all.
                            Program.Check(layout.ArmY > tallestTop + SupportLayouts.FrameFace, what + ": the framed boards hang under the arm: " + layout.ArmY + " vs top " + tallestTop);
                        }
                        else if (SupportKinds.Arm(layout.Kind))
                        {
                            Program.Check(layout.HangY < tallestTop && layout.ArmYs[layout.ArmYs.Length - 1] > tallestTop - tallest - 0.3f, what + ": the arms pass behind the tallest board: " + layout.HangY + " vs top " + tallestTop);
                        }
                        else
                        {
                            float overlap = Math.Min(tallestTop, layout.HangY + layout.BeamDepth) - Math.Max(tallestTop - tallest, layout.HangY);
                            Program.Check(overlap >= Math.Min(tallest, layout.BeamDepth) - layout.ChordRadius - 1e-3f, what + ": the truss and the tallest board overlap by " + overlap);
                            float floor = layout.HeightLimit ? 4.5f : 5.5f;
                            Program.Check(layout.HangY - layout.ChordRadius >= floor - 1e-3f && layout.BottomClearance >= floor - 1e-3f, what + ": the truss and the boards are above the clearance of the road");
                        }
                        if (layout.Kind == SupportKind.DoubleCantilever)
                        {
                            // Two arms: the boards are under one arm or the other, none beyond an end, none within a metre of the post.
                            Program.Check(fMax.X <= layout.ArmLength + Assembly.ArmTipInside + 1e-3f && fMin.X >= -(layout.ArmLength + Assembly.ArmTipInside) - 1e-3f, what + ": the boards are on the arms, their ends behind the farthest");
                            foreach (Placement pl in placed)
                                Program.Check(Math.Abs(pl.Cx) - pl.Def.Width * 0.5f >= 1.0f - 1e-3f, what + ": board " + pl.Board.Member + " hangs clear of the post: " + pl.Cx);
                        }
                        else
                        {
                            // Along the arm, which reaches to -X on the mirrored cantilever (ArmSide).
                            float near = layout.ArmSide > 0f ? fMin.X : -fMax.X, far = layout.ArmSide > 0f ? fMax.X : -fMin.X;
                            Program.Check(far <= layout.ArmLength + Assembly.ArmTipInside + 1e-3f && near >= Assembly.BoardFromPost - 1e-3f, what + ": the boards are between the post and the arm's end (or between the posts)");
                        }
                    }

                    // The boards do not overlap one another (the boxes of two members never share area).
                    var boxes = new List<PickShape>();
                    foreach (PickShape shape in parts.Picks)
                    {
                        if (shape.Name == "board")
                            boxes.Add(shape);
                    }

                    for (int i = 0; i < boxes.Count; i++)
                    {
                        for (int j = i + 1; j < boxes.Count; j++)
                        {
                            if (boxes[i].Member == boxes[j].Member)
                                continue;
                            bool overlapX = boxes[i].Min.X < boxes[j].Max.X - 1e-3f && boxes[j].Min.X < boxes[i].Max.X - 1e-3f;
                            bool overlapY = boxes[i].Min.Y < boxes[j].Max.Y - 1e-3f && boxes[j].Min.Y < boxes[i].Max.Y - 1e-3f;
                            Program.Check(!(overlapX && overlapY), what + ": board " + boxes[i].Member + " overlaps board " + boxes[j].Member);
                        }
                    }
                }
            }
        }

        private static void PickMembers()
        {
            SignDef p = Fixed("prohibition"), m = Fixed("mandatory"), w = Fixed("warning");
            var boards = new List<AssemblyBoard> { Board(w, 0, 10), Board(p, 1, 11), Board(m, 2, 12) };
            SignParts parts = SignBuilder.BuildAssembly(boards, null, 0f);
            var seen = new HashSet<int>();
            foreach (PickShape shape in parts.Picks)
            {
                if (shape.Name == "board")
                    seen.Add(shape.Member);
                else
                    Program.Check(shape.Member == -1, "a part of the support is no board's: " + shape.Name);
            }

            Program.Check(seen.Count == 3 && seen.Contains(10) && seen.Contains(11) && seen.Contains(12), "every board can be picked by the number it was given: " + seen.Count);

            // A ray at each board hits that board.
            foreach (PickShape shape in parts.Picks)
            {
                if (shape.Name != "board")
                    continue;
                var at = new Vec3((shape.Min.X + shape.Max.X) * 0.5f, (shape.Min.Y + shape.Max.Y) * 0.5f, 5f);
                var to = new Vec3(at.X, at.Y, -5f);
                bool hit = Picking.Pick(parts.Picks, Vec3.Zero, 0f, at, to, out float distance, out PickShape hitShape);
                Program.Check(hit && hitShape.Member == shape.Member, "a ray through the middle of board " + shape.Member + " hits it" + (hit ? " (hit " + hitShape.Member + ")" : ""));
            }
        }
    }
}
