using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.49 (the owner: "停车线/公交岛划线可以跟随弯道/用线吸附放置"): markings of one piece bent along the road (MarkingBend): the line's text, the bend of a point, the cut of the paint, the bays, the bus
    /// stops, the no-parking lines and the chevrons laid along a curve as long as they were drawn, the ground read under them, and their settings within the field.
    /// </summary>
    internal static class BendTests
    {
        public static void RunAll()
        {
            Program.Run("bend: the line's text keeps a curve", Text);
            Program.Run("bend: a point across the line is on its offset", Map);
            Program.Run("bend: the paint cut along the line", Cut);
            Program.Run("bend: bays, stops and lines laid along a curve as long as drawn", Catalogue);
            Program.Run("bend: the ground under a bent marking", Ground);
        }

        /// <summary>A bend to the right (toward +X) of radius r from the origin heading +Z: the centre at (r, 0), length long, points every step.</summary>
        private static List<Vec3> Arc(float r, float length, float step = 0.5f)
        {
            var points = new List<Vec3>();
            for (float s = 0f; s <= length + 1e-4f; s += step)
            {
                double a = s / r;
                points.Add(new Vec3(r - r * (float)Math.Cos(a), 0f, r * (float)Math.Sin(a)));
            }

            return points;
        }

        private static float ToCentre(Vec3 p, float r) => (float)Math.Sqrt((p.X - r) * (p.X - r) + p.Z * p.Z);

        /// <summary>Points of the item's frame in the world (the item at the origin, turned by yaw).</summary>
        private static List<Vec3> World(IList<Vec3> local, float yaw) => MarkingPolygon.ToWorld(local, Vec3.Zero, yaw);

        private static void Text()
        {
            List<Vec3> arc = Arc(20f, 30f);
            string text = MarkingBend.Encode(arc, out float yaw);
            MarkingBend bend = MarkingBend.Decode(text);
            // The polyline of the arc is a little shorter than 30 m: 29 chords of a little over a metre.
            Program.Check(bend != null && bend.Points.Count == 30, $"29 chords of a metre or a little more: {text}");
            Program.Near(yaw, 0.5f * 1.034f / 20f, 0.003f, "the yaw is the first chord's");
            float worst = 0f;
            foreach (Vec3 p in World(new List<Vec3>(bend.Points), yaw))
                worst = Math.Max(worst, Math.Abs(ToCentre(p, 20f) - 20f));
            Program.Check(worst < 0.01f, $"every point on the curve within 1 cm: {worst * 100f:0.00} cm");
            Program.Near(bend.Length, 30f, 0.15f, "as long as the line (the chord to the centimetre)");
            // A long line: at most MaxPieces chords, longer ones.
            MarkingBend far = MarkingBend.Decode(MarkingBend.Encode(Arc(200f, 300f), out _));
            Program.Check(far.Points.Count == MarkingBend.MaxPieces + 1 && Math.Abs(far.Length - 300f) < 1f, $"300 m in {far.Points.Count - 1} chords");
            Program.Check(MarkingBend.Encode(new List<Vec3> { Vec3.Zero, new Vec3(0f, 0f, 0.5f) }, out _) == "", "shorter than a chord: none");
            Program.Check(MarkingBend.Decode("") == null && MarkingBend.Decode("x:1") == null && MarkingBend.Decode("100:1,a") == null, "a damaged text: none");
            MarkingBend straight = MarkingBend.Decode("250:");
            Program.Check(straight != null && Math.Abs(straight.Length - 2.5f) < 1e-4f, "one chord");
        }

        private static void Map()
        {
            MarkingBend bend = MarkingBend.Decode(MarkingBend.Encode(Arc(20f, 30f), out float yaw));
            float worst = 0f;
            for (float z = 0f; z <= 29.8f; z += 0.25f)
            {
                foreach (float x in new[] { -5.5f, -2.5f, 0f, 3f })
                    worst = Math.Max(worst, Math.Abs(ToCentre(World(new[] { bend.Map(x, z) }, yaw)[0], 20f) - (20f - x)));
            }

            Program.Check(worst < 0.012f, $"x across: on the curve x nearer its centre, within 1.2 cm: {worst * 100f:0.00} cm");
            Program.Check(Vec3.DistanceXZ(bend.Map(0f, 0f), Vec3.Zero) < 1e-4f && Vec3.DistanceXZ(bend.Map(2f, 0f), new Vec3(2f, 0f, 0f)) < 1e-4f, "the start: the item's frame");
            // Beyond the ends: straight on.
            Vec3 end = bend.Map(0f, bend.Length), past = bend.Map(0f, bend.Length + 5f);
            Program.Near(Vec3.DistanceXZ(end, past), 5f, 1e-3f, "past the end: straight on");
            Vec3 before = bend.Map(0f, -2f);
            Program.Check(Math.Abs(before.X) < 1e-4f && Math.Abs(before.Z + 2f) < 1e-4f, "before the start: straight back");
        }

        private static float Area(MeshBuilder mesh, out int bad)
        {
            float area = 0f;
            bad = 0;
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[t]], b = mesh.Positions[mesh.Indices[t + 1]], c = mesh.Positions[mesh.Indices[t + 2]];
                Vec3 n = MeshBuilder.Cross(b - a, c - a);
                area += n.Length * 0.5f;
                bad += n.Y > 1e-9f ? 0 : 1;
            }

            return area;
        }

        private static void Cut()
        {
            var mesh = new MeshBuilder();
            RoadMarkings.Rect(mesh, -0.05f, 0f, 0.05f, 10f);
            int before = mesh.TriangleCount;
            float area0 = Area(mesh, out int bad0);
            var cuts = new List<float>();
            for (int i = 1; i < 10; i++)
                cuts.Add(i);
            MarkingBend.Cut(mesh, cuts);
            float area1 = Area(mesh, out int bad1);
            Program.Check(bad0 == 0 && bad1 == 0, $"every piece faces up ({bad1} do not)");
            Program.Near(area1, area0, 1e-4f, "the area is kept");
            Program.Check(mesh.TriangleCount >= 10 * before, $"cut into the ten metres: {mesh.TriangleCount} triangles of {before}");
            // A cut crosses the strip's two sides and the diagonal the two triangles share: three new points at each, the diagonal's made once for both.
            Program.Check(mesh.VertexCount == 4 + 3 * cuts.Count, $"a cut point once for its edge: {mesh.VertexCount} vertices");
            foreach (float c in cuts)
                Program.Check(mesh.Positions.Exists(p => Math.Abs(p.Z - c) < 1e-5f), $"a point at the cut {c}");
            // A triangle that does not reach a cut is kept as it is.
            var small = new MeshBuilder();
            RoadMarkings.Rect(small, 0f, 0.2f, 1f, 0.8f);
            MarkingBend.Cut(small, cuts);
            Program.Check(small.TriangleCount == 2 && small.VertexCount == 4, "between two cuts: as it was");
        }

        private static GuideTemplate Template(string id) => GuideTemplates.ById(id);

        /// <summary>The spec of a template drawn along an arc (to the right, radius r) for length, or placed with one click (fill empty).</summary>
        private static string Drawn(string id, float r, float length, string more = "")
        {
            GuideTemplate t = Template(id);
            GuideSpec s = t.Default().With(MarkingBend.Key, MarkingBend.Encode(Arc(r, length + 2f), out _));
            if (t.FieldOf(MarkingBend.PlaceKey) != null)
                s = s.With(MarkingBend.PlaceKey, MarkingBend.Drawn).With(MarkingBend.FillKey, ((int)Math.Round(length * 100f)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (string pair in more.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                s = s.With(pair.Substring(0, pair.IndexOf('=')), pair.Substring(pair.IndexOf('=') + 1));
            return s.ToString();
        }

        private static float PaintArea(MarkingPlan plan)
        {
            float area = 0f;
            foreach (MarkingPart part in plan.Parts)
                area += Area(part.Mesh, out _);
            return area;
        }

        private static void Catalogue()
        {
            MarkingLetters.Font = FontLetterTests.Font;
            string[] drawn = { "mk.bay.parallel", "mk.bay.angled", "mk.bay.perpendicular", "mk.bay.arrow", "mk.bay.taxi", "mk.bay.timed", "mk.busbay", "mk.kerbstop", "mk.nopark.road", "mk.chevron" };
            string[] click = { "mk.bay.disabled", "mk.bay.bicycle" };
            foreach (string id in drawn)
                Program.Check(MarkingTemplates.Bends(Template(id)) && Template(id).FieldOf(MarkingBend.PlaceKey) != null, id + ": bent, and may be drawn along the road");
            foreach (string id in click)
                Program.Check(MarkingTemplates.Bends(Template(id)) && Template(id).FieldOf(MarkingBend.PlaceKey) == null, id + ": bent where it is clicked");
            Program.Check(!MarkingTemplates.Bends(Template("mk.stop")) && !MarkingTemplates.Bends(Template("mk.arrow.straight")), "a stop line and an arrow are not bent");
            Program.Check(MarkingTemplates.KerbAt(Template("mk.kerbstop"), Template("mk.kerbstop").Default()) == 3f && MarkingTemplates.KerbAt(Template("mk.bay.parallel"), Template("mk.bay.parallel").Default()) == 0f
                && float.IsNaN(MarkingTemplates.KerbAt(Template("mk.chevron"), Template("mk.chevron").Default())), "the kerb in each one's frame");

            foreach (string id in drawn)
            {
                GuideTemplate t = Template(id);
                MarkingPlan straight = MarkingTemplates.PlanOf(t, t.Default().ToString());
                Program.Check(straight.Bend == null, id + ": with no line, straight as before");
                // Placed with one click on a curve of 30 m: the same paint, bent; laid along a straight line, cut but as it was.
                GuideSpec one = t.Default().With(MarkingBend.Key, MarkingBend.Encode(Arc(30f, straight.MaxZ + 1f), out float yaw));
                MarkingPlan bent = MarkingTemplates.PlanOf(t, one.ToString());
                Program.Check(bent.Bend != null && Math.Abs(bent.StraightMaxZ - straight.MaxZ) < 0.01f, $"{id}: the straight box is the plan's ({bent.StraightMaxZ:0.00} of {straight.MaxZ:0.00})");
                MarkingPlan along = MarkingTemplates.PlanOf(t, t.Default().With(MarkingBend.Key, MarkingBend.Encode(new List<Vec3> { Vec3.Zero, new Vec3(0f, 0f, straight.MaxZ + 1f) }, out _)).ToString());
                float a0 = PaintArea(straight), a1 = PaintArea(along), a2 = PaintArea(bent);
                Program.Check(Math.Abs(a1 - a0) < 0.005f * a0 && a2 > 0.9f * a0 && a2 < 1.25f * a0, $"{id}: no paint lost by the cuts ({a1:0.000} of {a0:0.000} m2), bent {a2:0.00} m2 (longer outside the curve, shorter inside: a stop is on the kerb side)");
                foreach (MarkingPart part in bent.Parts)
                    Program.Check(!part.Upright && !part.Solid.HasValue, id + ": paint on the road only");
                Program.Check(bent.Outlines.Count == 0 && bent.Props.Count == 0, id + ": no outlines and no props (they are not bent)");
                // The bays and the no-parking line from the kerb (x 0, on the curve: the inner side is the footway) into the road, none over the kerb.
                if (id.StartsWith("mk.bay.", StringComparison.Ordinal) || id == "mk.nopark.road")
                {
                    int over = 0, near = 0;
                    foreach (MarkingPart part in bent.Parts)
                    {
                        foreach (Vec3 p in World(part.Mesh.Positions, yaw))
                        {
                            float d = ToCentre(p, 30f);
                            near += Math.Abs(d - 30f) < 0.1f ? 1 : 0;
                            over += d < 29.94f ? 1 : 0;
                        }
                    }

                    Program.Check(near > 0 && over == 0, $"{id}: the paint from the kerb on the curve, none over it ({over} points over, {near} at it)");
                }
            }

            // Drawn along the road: as long as the line.
            MarkingPlan Of(string id, float length, string more = "") => MarkingTemplates.PlanOf(Template(id), Drawn(id, 40f, length, more));
            MarkingPlan perpendicular = Of("mk.bay.perpendicular", 26f);
            Program.Check(perpendicular.Bend != null && Math.Abs(perpendicular.StraightMaxZ - 25f) < 0.1f, $"26 m of perpendicular bays of 2.5 m: ten, 25 m ({perpendicular.StraightMaxZ:0.00})");
            MarkingPlan parallel = Of("mk.bay.parallel", 26f);
            Program.Near(parallel.StraightMaxZ, 24f, 0.1f, "26 m of parallel bays of 6 m: four, 24 m");
            MarkingPlan angled = Of("mk.bay.angled", 20f);
            float overhang = 5.5f * (float)Math.Cos(Math.PI / 4.0);
            Program.Check(angled.StraightMaxZ <= 20f + 0.1f && angled.StraightMaxZ > 20f - 2.5f - 0.1f, $"20 m of angled bays: as many as fit with the far one's lean ({angled.StraightMaxZ:0.00} m, lean {overhang:0.00})");
            MarkingPlan many = Of("mk.bay.perpendicular", 400f);
            Program.Near(many.StraightMaxZ, MarkingTemplates.MaxDrawnBays * 2.5f, 0.1f, "a very long row: the most bays");
            Program.Near(Of("mk.kerbstop", 30f).StraightMaxZ, 30f, 0.15f, "a kerb-side stop drawn 30 m long");
            MarkingPlan bay = Of("mk.busbay", 100f);
            Program.Near(bay.StraightMaxZ, 100f, 0.3f, "a bus bay drawn 100 m: two tapers of 25 m and 50 m between");
            Program.Near(Of("mk.nopark.road", 18f).StraightMaxZ, 18f, 0.01f, "a no-parking line drawn 18 m");
            Program.Check(Of("mk.chevron", 80f).StraightMaxZ > 50f, "chevrons drawn 80 m: the second group");
            // The fields the drawn length takes the place of are hidden.
            GuideSpec drawnSpec = Template("mk.bay.perpendicular").Parse(Drawn("mk.bay.perpendicular", 40f, 20f));
            Program.Check(!Template("mk.bay.perpendicular").FieldOf("count").Visible(drawnSpec) && Template("mk.bay.perpendicular").FieldOf("count").Visible(Template("mk.bay.perpendicular").Default()), "the count only when placed with one click");

            // The longest settings fit the field whole: a line of 32 chords turning hard, the ground under a long stop, the longest word.
            foreach (string id in drawn)
            {
                GuideTemplate t = Template(id);
                var zigzag = new List<Vec3>();
                for (int i = 0; i <= 40; i++)
                    zigzag.Add(new Vec3(i % 2 == 0 ? 0f : 2.5f, 0f, i * 7.5f));
                string bend = MarkingBend.Encode(zigzag, out _);
                var grid = new StringBuilder("-1550,1550,-120,30000,4,8");
                for (int k = 0; k < 32; k++)
                    grid.Append(",-123");
                GuideSpec s = t.Default().With(MarkingBend.Key, bend).With(GroundGrid.Key, grid.ToString());
                if (t.FieldOf(MarkingBend.PlaceKey) != null)
                    s = s.With(MarkingBend.PlaceKey, MarkingBend.Drawn).With(MarkingBend.FillKey, "300000");
                foreach (GuideField f in t.Fields)
                {
                    if (f.Kind == GuideFieldKind.Text && !f.DigitsOnly && f.Key != MarkingBend.Key && f.Key != GroundGrid.Key && f.Key != "g")
                        s = s.With(f.Key, new string('公', f.MaxLength));
                }

                string written = s.ToString();
                GuideSpec back = t.Parse(written);
                Program.Check(Encoding.UTF8.GetByteCount(written) <= GuideSpec.MaxBytes && back.Get(MarkingBend.Key) == bend && back.Get(GroundGrid.Key) == grid.ToString(),
                    $"{id}: the longest settings fit ({Encoding.UTF8.GetByteCount(written)} bytes), the line and the ground whole");
            }
        }

        /// <summary>A bent marking's ground: read over its straight box where the bend puts each point, the paint at the ground's height where it lies.</summary>
        private static void Ground()
        {
            GuideTemplate t = Template("mk.bay.perpendicular");
            string spec = t.Default().With(MarkingBend.Key, MarkingBend.Encode(Arc(25f, 12f), out float yaw)).ToString();
            MarkingPlan plan = MarkingTemplates.PlanOf(t, spec);
            // The ground rises 5 % toward +X in the world (the item at the origin, yaw 0).
            Func<Vec3, float> height = p => 0.05f * p.X;
            var asked = new List<Vec3>();
            GroundGrid grid = MarkingGround.GridUnder(plan, Vec3.Zero, yaw, world =>
            {
                asked.AddRange(world);
                var on = new List<Vec3>();
                foreach (Vec3 p in world)
                    on.Add(new Vec3(p.X, height(p), p.Z));
                return on;
            });
            Program.Check(Math.Abs(grid.X0 - plan.StraightMinX) < 1e-4f && Math.Abs(grid.Z1 - plan.StraightMaxZ) < 1e-4f, "the grid over the straight box");
            Program.Check(asked.Exists(p => Math.Abs(ToCentre(p, 25f) - 25f) < 0.05f && p.Z > 8f), "a point of the grid at the kerb far along the curve, where the bend puts it");
            MarkingPlan laid = MarkingTemplates.PlanOf(t, MarkingTemplates.WithGround(t, spec, grid));
            float worst = 0f;
            foreach (MarkingPart part in laid.Parts)
            {
                foreach (Vec3 p in World(part.Mesh.Positions, yaw))
                    worst = Math.Max(worst, Math.Abs(p.Y - part.Lift - height(p)));
            }

            Program.Check(worst < 0.03f, $"the paint on the ground where it lies, within 3 cm: {worst * 100f:0.0} cm");
            // The points an island snaps to: the corners of the straight box where the bend puts them, on the curve (the kerb side) and the bays' depth out from it.
            var item = new ItemRecord(t.Hash, Vec3.Zero, yaw, 0u, spec);
            List<Vec3> corners = MarkingSnapPoints.Of(SignCatalog.ById("mk.bay.perpendicular"), item);
            Program.Check(corners.Count == 4 && Math.Abs(ToCentre(corners[1], 25f) - 25f + plan.StraightMaxX) < 0.03f && Math.Abs(ToCentre(corners[2], 25f) - 25f + plan.StraightMaxX) < 0.03f
                && Math.Abs(ToCentre(corners[0], 25f) - 25f + plan.StraightMinX) < 0.03f, "a bent marking snaps at the corners of its paint, along the curve: " + string.Join(" ", corners));
        }
    }
}
