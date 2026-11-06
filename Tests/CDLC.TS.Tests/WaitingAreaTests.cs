using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The left-turn waiting area made from a junction (0.12.0, mk.waiting): its lines, stop line and arrows (GB 5768.3 5.5, Fig. 10) and how long it is made.</summary>
    internal static class WaitingAreaTests
    {
        public static void RunAll()
        {
            Program.Run("waiting area: the polyline helpers", Helpers);
            Program.Run("waiting area: lines, colours, the stop line and the arrows of Fig. 10", Paint);
            Program.Run("waiting area: its length stops short of the oncoming traffic", Length);
            Program.Run("waiting area: the template", Template);
            Program.Run("waiting area: the lines of help", Hints);
        }

        private static void Hints()
        {
            foreach (bool zh in new[] { true, false })
            {
                string road = HintText.Of(new HintInfo { Kind = "mark-waiting", Name = "road", Bad = true }, zh);
                string none = HintText.Of(new HintInfo { Kind = "mark-waiting", Name = "none", Bad = true }, zh);
                string room = HintText.Of(new HintInfo { Kind = "mark-waiting", Name = "room", Count = 1, Bad = true }, zh);
                string ok = HintText.Of(new HintInfo { Kind = "mark-waiting", Count = 2, Left = 17.5f, Manual = true }, zh);
                string white = HintText.Of(new HintInfo { Kind = "mark-waiting", Count = 2, Left = 17.5f, Manual = false, Hung = true }, zh);
                Program.Check(road.Length > 10 && none.Length > 10 && room.Length > 10 && road != none && none != room, (zh ? "zh" : "en") + ": a text for each problem");
                Program.Check(ok.Contains("2") && ok.Contains("17.5") && ok != white && white.Length > ok.Length, (zh ? "zh" : "en") + ": the click says the lanes, the length, the left line and when no oncoming traffic was found: " + ok);
            }
        }

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        private static List<Vec3> Straight(float length) => new List<Vec3> { P(0f, 0f), P(0f, length) };

        private static void Helpers()
        {
            var bent = new List<Vec3> { P(0f, 0f), P(0f, 10f), P(10f, 10f) };
            Program.Near(WaitingArea.Length(bent), 20f, 1e-4f, "length");
            Program.Check(WaitingArea.Length(null) == 0f, "no line, no length");
            Vec3 at = WaitingArea.At(bent, 15f, out Vec3 d);
            Program.Check(Vec3.DistanceXZ(at, P(5f, 10f)) < 1e-4f && Math.Abs(d.X - 1f) < 1e-4f, "a point and the way at 15 m: " + at + " " + d);
            Program.Check(Vec3.DistanceXZ(WaitingArea.At(bent, -3f, out _), P(0f, 0f)) < 1e-4f && Vec3.DistanceXZ(WaitingArea.At(bent, 99f, out _), P(10f, 10f)) < 1e-4f, "clamped to the ends");
            List<Vec3> cut = WaitingArea.Cut(bent, 5f, 15f);
            Program.Near(WaitingArea.Length(cut), 10f, 0.05f, "a cut of 10 m");
            Program.Check(Vec3.DistanceXZ(cut[0], P(0f, 5f)) < 1e-4f && Vec3.DistanceXZ(cut[cut.Count - 1], P(5f, 10f)) < 1e-4f, "from 5 m to 15 m");
            bool fine = true;
            for (int i = 1; i < cut.Count; i++)
                fine &= Vec3.DistanceXZ(cut[i - 1], cut[i]) <= 0.5f + 1e-4f;
            Program.Check(fine, "with a point at least every half metre");
            Program.Check(WaitingArea.Cut(bent, 8f, 8f).Count == 0 && WaitingArea.Cut(new List<Vec3> { P(0f, 0f) }, 0f, 1f).Count == 0, "nothing to cut: nothing");
            Program.Near(WaitingArea.SegmentDistance(P(0f, 0f), P(10f, 0f), P(5f, -1f), P(5f, 1f)), 0f, 1e-6f, "crossing segments: 0");
            Program.Near(WaitingArea.SegmentDistance(P(0f, 0f), P(10f, 0f), P(12f, 3f), P(12f, 9f)), (float)Math.Sqrt(13.0), 1e-4f, "apart: from the nearest ends");
            Program.Near(WaitingArea.SegmentDistance(P(0f, 0f), P(10f, 0f), P(4f, 2f), P(6f, 5f)), 2f, 1e-4f, "beside: to the nearest point");
        }

        private static float DashCoverage(MarkingShape shape) => shape.Area;

        private static void Paint()
        {
            // A straight area 20 m long from 3.5 m along a 40 m axis, two lanes 3.5 wide.
            List<MarkingShape> shapes = WaitingArea.Shapes(Straight(40f), 2, 3.5f, 3.5f, 20f, true, 0.2f, true);
            Program.Check(shapes.Count == 5, "three lines, the stop line and the arrows: " + shapes.Count);
            Program.Check(shapes[0].Colour == MarkingColour.Yellow && shapes[1].Colour == MarkingColour.White && shapes[2].Colour == MarkingColour.White, "the left line yellow, the others white");
            // The lines lie at -1.75, 1.75 and 5.25 m, each half painted (50/50 dashes) over 20 m, 15 cm wide.
            float[] at = { -1.75f, 1.75f, 5.25f };
            for (int k = 0; k < 3; k++)
            {
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (Vec3 p in shapes[k].Mesh.Positions)
                {
                    minX = Math.Min(minX, p.X);
                    maxX = Math.Max(maxX, p.X);
                    minZ = Math.Min(minZ, p.Z);
                    maxZ = Math.Max(maxZ, p.Z);
                }

                Program.Near((minX + maxX) * 0.5f, at[k], 0.01f, "line " + k + " where it lies across");
                Program.Near(maxX - minX, WaitingArea.LineWidth, 0.01f, "line " + k + ": 15 cm wide");
                Program.Check(minZ >= 3.5f - 1e-3f && maxZ <= 23.5f + 1e-3f, "line " + k + " inside the area: " + minZ + ".." + maxZ);
                Program.Near(DashCoverage(shapes[k]), 20f * 0.5f * WaitingArea.LineWidth, 0.06f, "line " + k + ": dashed 50/50");
            }

            // The stop line: across both lanes and the lines' outer sides, 20 cm, its far edge at the end of the area (23.5 m).
            MarkingShape stop = shapes[3];
            float sMinX = float.MaxValue, sMaxX = float.MinValue, sMinZ = float.MaxValue, sMaxZ = float.MinValue;
            foreach (Vec3 p in stop.Mesh.Positions)
            {
                sMinX = Math.Min(sMinX, p.X);
                sMaxX = Math.Max(sMaxX, p.X);
                sMinZ = Math.Min(sMinZ, p.Z);
                sMaxZ = Math.Max(sMaxZ, p.Z);
            }

            Program.Near(sMinX, -1.825f, 0.01f, "the stop line from the left line's outer side");
            Program.Near(sMaxX, 5.325f, 0.01f, "to the right line's");
            Program.Near(sMaxZ, 23.5f, 0.01f, "its far edge at the end");
            Program.Near(sMaxZ - sMinZ, 0.2f, 0.01f, "20 cm");
            Program.Check(stop.Colour == MarkingColour.White && stop.Area > 1.4f, "white, solid");
            Program.Near(WaitingArea.Shapes(Straight(40f), 2, 3.5f, 3.5f, 20f, true, 0.4f, true)[3].Area, 7.15f * 0.4f, 0.02f, "a 40 cm stop line");

            // The arrows: two in each lane (20 m is 9 m or more); in lane 0 the first's tail at 4 m (50 cm after the start), the second's tip 1 m before the stop line (23.5 - 0.2 - 1 = 22.3).
            MarkingShape arrows = shapes[4];
            float lane0Low = float.MaxValue, lane0High = float.MinValue;
            int lane1 = 0;
            foreach (Vec3 p in arrows.Mesh.Positions)
            {
                if (p.X < 1.6f)
                {
                    lane0Low = Math.Min(lane0Low, p.Z);
                    lane0High = Math.Max(lane0High, p.Z);
                }
                else
                {
                    lane1++;
                }
            }

            Program.Near(lane0Low, 4f, 0.01f, "the first arrow's tail 50 cm after the start");
            Program.Near(lane0High, 22.3f, 0.01f, "the second's tip 1 m before the stop line");
            float one = RoadArrows.Shape(ArrowKind.Left, ArrowSize.Line30).Area;
            Program.Near(arrows.Area, 4f * one, 0.01f, "four arrows of 3 m (two lanes, two each)");
            Program.Check(lane1 > 0, "the second lane has its own");

            // A short one (8 m): one arrow a lane; a very short one (4 m): none; arrows off: none; white left line.
            Program.Near(WaitingArea.Shapes(Straight(40f), 1, 3.5f, 3.5f, 8f, true, 0.2f, true)[3].Area, one, 0.01f, "under 9 m: one arrow");
            Program.Near(WaitingArea.Shapes(Straight(40f), 1, 3.5f, 3.5f, 9f, true, 0.2f, true)[3].Area, 2f * one, 0.01f, "from 9 m: two");
            Program.Check(WaitingArea.Shapes(Straight(40f), 1, 3.5f, 3.5f, 4f, true, 0.2f, true).Count == 3, "under 4.5 m: no arrow");
            Program.Check(WaitingArea.Shapes(Straight(40f), 1, 3.5f, 3.5f, 20f, true, 0.2f, false).Count == 3, "arrows off: none");
            Program.Check(WaitingArea.Shapes(Straight(40f), 1, 3.5f, 3.5f, 20f, false, 0.2f, true)[0].Colour == MarkingColour.White, "a white left line when asked");
            // An area that would run past the axis ends with it.
            List<MarkingShape> past = WaitingArea.Shapes(Straight(10f), 1, 3.5f, 3.5f, 30f, true, 0.2f, true);
            float far = float.MinValue;
            foreach (Vec3 p in past[2].Mesh.Positions)
                far = Math.Max(far, p.Z);
            Program.Near(far, 10f, 0.01f, "cut at the end of the axis");
            Program.Check(WaitingArea.Shapes(Straight(0.5f), 1, 3.5f, 0f, 5f, true, 0.2f, true).Count == 0 && WaitingArea.Shapes(null, 1, 3.5f, 0f, 5f, true, 0.2f, true).Count == 0
                && WaitingArea.Shapes(Straight(40f), 0, 3.5f, 0f, 5f, true, 0.2f, true).Count == 0 && WaitingArea.Shapes(Straight(40f), 1, 3.5f, float.NaN, 5f, true, 0.2f, true).Count == 0, "nothing to lay it on: nothing");

            // Along a left turn: the arrows point along it (their tips are left of their tails as the turn goes on), and every triangle faces up.
            List<MarkingShape> turn = WaitingArea.Shapes(WaitingArea.DefaultAxis(), 1, 3.5f, 2f, 14f, true, 0.2f, true);
            bool up = true;
            foreach (MarkingShape s in turn)
            {
                for (int i = 0; i + 2 < s.Mesh.Indices.Count; i += 3)
                {
                    Vec3 a = s.Mesh.Positions[s.Mesh.Indices[i]], b = s.Mesh.Positions[s.Mesh.Indices[i + 1]], c = s.Mesh.Positions[s.Mesh.Indices[i + 2]];
                    up &= MeshBuilder.Cross(b - a, c - a).Y > 0f;
                }
            }

            Program.Check(up, "on a turn every triangle faces up");
            // The yellow (inner) line of a left turn is shorter than the outer white one.
            Program.Check(turn[0].Area < turn[1].Area, "the inner line of the turn is the shorter: " + turn[0].Area + " < " + turn[1].Area);
        }

        private static void Length()
        {
            // A straight axis north; the oncoming lanes cross it at 20 m (a lane east-west, 3.5 m wide): the area (one lane) stops when its front comes within 0.5 m of that lane's side (at 17.75 m),
            // then a metre short: from 3.5 m, in half-metre steps, 13.5 m.
            var across = new List<IList<Vec3>> { new List<Vec3> { P(-20f, 20f), P(20f, 20f) } };
            float l = WaitingArea.AutoLength(Straight(60f), 1, 3.5f, 3.5f, across, new List<float> { 1.75f });
            Program.Near(l, 13.5f, 1e-3f, "it stops a metre short of the clearance before the crossing lane");
            // Two lanes: the area is wider, but the crossing is ahead, not beside: the same.
            Program.Near(WaitingArea.AutoLength(Straight(60f), 2, 3.5f, 3.5f, across, new List<float> { 1.75f }), l, 1e-3f, "two lanes, the same length");
            // A lane alongside, 10 m to the right: no conflict for one lane; for three lanes (to 8.75 m right) it is within the clearance from the start.
            var beside = new List<IList<Vec3>> { new List<Vec3> { P(10f, -5f), P(10f, 80f) } };
            Program.Near(WaitingArea.AutoLength(Straight(60f), 1, 3.5f, 3.5f, beside, new List<float> { 0.5f }), WaitingArea.MaxLength, 1e-3f, "a lane far beside: the longest");
            Program.Near(WaitingArea.AutoLength(Straight(60f), 3, 3.5f, 3.5f, beside, new List<float> { 1.75f }), 0f, 1e-3f, "three lanes reach it: no room at all");
            // Nothing else: the longest, or what the axis leaves (3 m short of its end).
            Program.Near(WaitingArea.AutoLength(Straight(60f), 1, 3.5f, 3.5f, null, null), 30f, 1e-3f, "no other traffic: 30 m");
            Program.Near(WaitingArea.AutoLength(Straight(20f), 1, 3.5f, 3.5f, null, null), 13.5f, 1e-3f, "a short turn: to 3 m before its end");
            Program.Near(WaitingArea.AutoLength(Straight(60f), 1, 3.5f, 3.5f, across, new List<float> { 1.75f }, 10f), 10f, 1e-3f, "or the longest asked for");
            Program.Check(WaitingArea.AutoLength(null, 1, 3.5f, 3.5f, null, null) == 0f && WaitingArea.AutoLength(Straight(60f), 0, 3.5f, 3.5f, null, null) == 0f, "nothing to measure: 0");
            // A half width read as 0 is a line: the clearance alone.
            float thin = WaitingArea.AutoLength(Straight(60f), 1, 3.5f, 3.5f, across, new List<float> { 0f });
            Program.Near(thin, 15.5f, 1e-3f, "a thin lane: it gets nearer (the front within 0.5 m at 20 m, a metre short)");
        }

        private static void Template()
        {
            GuideTemplate t = MarkingTemplates.ById(MarkingTemplates.WaitingId);
            Program.Check(t != null && !MarkingTemplates.IsRun(t) && t.FieldOf(MarkingTemplates.WaitingAxisKey) != null, "a marking of one item, not a run, with its axis");
            // The axis of a placed one: a 30 m straight path; length 12 from 2: lines from 2 to 14.
            string axis = RunPath.Encode(new List<Vec3> { P(100f, 100f), P(100f, 115f), P(100f, 130f) }, out float yaw);
            Program.Check(Math.Abs(yaw) < 1e-4f, "the path heads north: yaw 0");
            string spec = t.Parse(t.Default().ToString()).With(MarkingTemplates.WaitingAxisKey, axis).With("len", "12").With("start", "2").With("lanes", "2").ToString();
            MarkingPlan plan = MarkingTemplates.PlanOf(t, spec);
            Program.Check(plan.Parts.Count >= 2, "white and yellow paint");
            Program.Near(plan.MinZ, 2f, 0.01f, "from 2 m");
            Program.Near(plan.MaxZ, 14f, 0.01f, "to 14 m");
            Program.Near(plan.MinX, -1.825f, 0.01f, "the left line's outer side");
            Program.Near(plan.MaxX, 5.325f, 0.01f, "two lanes to the right line's");
            Program.Check(System.Text.Encoding.UTF8.GetByteCount(spec) <= GuideSpec.MaxBytes, "the settings fit");
            // The longest axis the junction can give (32 pieces of 1.5 m round a quarter turn) still fits.
            var longAxis = new List<Vec3>();
            for (int i = 0; i <= 32; i++)
                longAxis.Add(P((float)(-30.0 * (1.0 - Math.Cos(i * Math.PI / 64.0))), (float)(30.0 * Math.Sin(i * Math.PI / 64.0))));
            string longSpec = t.Parse(t.Default().ToString()).With(MarkingTemplates.WaitingAxisKey, RunPath.Encode(longAxis, out _)).ToString();
            Program.Check(System.Text.Encoding.UTF8.GetByteCount(longSpec) <= GuideSpec.MaxBytes && t.Parse(longSpec).Get(MarkingTemplates.WaitingAxisKey).Length > 100, "the longest axis fits: " + System.Text.Encoding.UTF8.GetByteCount(longSpec));
            // No axis (the thumbnail): the default turn.
            MarkingPlan thumb = MarkingTemplates.PlanOf(t, t.Default().ToString());
            Program.Check(thumb.Parts.Count == 2 && thumb.MinX < -5f, "with no axis a left turn is drawn: " + thumb.MinX);
        }
    }
}
