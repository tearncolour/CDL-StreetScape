using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// Pictures of the area markings as the tool would place them, from made-up roads (no game): an exit and an entry found at a fork by GoreLayout with their lead lines, an island of a bent shape, a strip
    /// along a curve. Each picture puts the items of one case together in the world and draws them from above (MarkingRaster). Usage: Preview.exe root out --area-demos. Geometry only; not the game.
    /// </summary>
    internal static class AreaDemos
    {
        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        /// <summary>The plans of several items in one frame (the world's), as one plan to draw.</summary>
        private static MarkingPlan Together(List<(MarkingPlan Plan, Vec3 At, float Yaw)> items)
        {
            var plan = new MarkingPlan();
            foreach ((MarkingPlan p, Vec3 at, float yaw) in items)
            {
                foreach (MarkingPart part in p.Parts)
                {
                    var shape = new MarkingShape { Colour = part.Colour, Lift = part.Lift };
                    shape.Mesh.Append(part.Mesh, new Vec3(at.X, 0f, at.Z), yaw);
                    plan.Shapes.Add(shape);
                }
            }

            plan.Bake(0f);
            return plan;
        }

        /// <summary>The items of a gore found at a fork: the gore (mk.gore with its outline) and its lead line (mk.line.thick, the solid toward the nose), cut as the tool cuts them.</summary>
        private static List<(MarkingPlan, Vec3, float)> Fork(List<Vec3> mainAway, List<Vec3> rampAway, int rampSide, bool exit, List<Vec3> approach, out string note, float maxLength = 60f, float leadLength = 60f)
        {
            var items = new List<(MarkingPlan, Vec3, float)>();
            GoreSolution g = GoreLayout.Solve(mainAway, rampAway, rampSide, 4f, maxLength);
            if (g == null)
            {
                note = "no fork";
                return items;
            }

            // The item at the nose, +Z the way the traffic runs there.
            Vec3 heading = (g.Main[1] - g.Main[0]).HeadingXZ();
            if (!exit)
                heading = heading * -1f;
            float yaw = EdgeSnap.YawOf(heading);
            var polygon = MarkingAreas.GorePolygon(g.Main, g.Ramp);
            string poly = MarkingPolygon.Encode(MarkingPolygon.ToLocal(polygon, g.Nose, yaw));
            GuideTemplate gore = MarkingTemplates.ById("mk.gore");
            string spec = MarkingTemplates.WithPolygon(gore, gore.Parse(gore.Default().ToString()).With("form", exit ? "exit" : "entry").ToString(), poly, g.Main.Count - 1, g.Blunt);
            items.Add((MarkingTemplates.PlanOf(gore, spec), g.Nose, yaw));

            List<Vec3> lead = exit ? GoreLayout.LeadBefore(approach, g.Nose, leadLength) : GoreLayout.LeadAfter(approach, g.Nose, leadLength);
            // As the tool puts it: the 45 cm line on the ramp side of the through edge (the ramp is on the right of the traffic in these cases).
            lead = MarkingFills.Offset(lead, MarkingLines.ThickWidth * 0.5f);
            GuideTemplate thick = MarkingTemplates.ById("mk.line.thick");
            string thickSpec = thick.Parse(thick.Default().ToString()).With("solid", exit ? "end" : "start").ToString();
            float total = 0f;
            for (int i = 1; i < lead.Count; i++)
                total += Vec3.DistanceXZ(lead[i - 1], lead[i]);
            foreach (RunItem item in MarkingRuns.Items(lead))
                items.Add((MarkingTemplates.PlanOf(thick, MarkingTemplates.WithRun(thick, thickSpec, item.Path, item.Start, total)), item.Position, item.Yaw));
            note = $"{(exit ? "exit" : "entry")}: nose at {g.Nose}, gore {g.Length:0.0} m long, {g.FarWidth:0.0} m wide at its end, lead {total:0.0} m in {items.Count - 1} piece(s)";
            return items;
        }

        public static int Run(string output)
        {
            var tiles = new List<MarkingPlan>();
            var notes = new List<string>();

            // An exit to the right: the through carriageway's right edge straight north from the junction at z = 0; the ramp's left edge starts over it (as edges from the junction's middle do) and curves away.
            var main = new List<Vec3>();
            var ramp = new List<Vec3>();
            for (float z = 0f; z <= 150f; z += 2f)
            {
                main.Add(P(0f, z));
                ramp.Add(P(-0.5f + z * z / 600f, z));
            }

            var approach = new List<Vec3>();
            for (float z = -120f; z <= 0f; z += 5f)
                approach.Add(P(-7f, z));
            tiles.Add(Together(Fork(main, ramp, 1, true, approach, out string n1)));
            notes.Add(n1);

            // An entry from the right: the same shapes, the traffic coming the other way over them (given away from the junction, the ramp's edge on the left of the main's looking that way).
            var mainEntry = main.ConvertAll(p => P(p.X, -p.Z));
            var rampEntry = ramp.ConvertAll(p => P(p.X, -p.Z));
            var departure = new List<Vec3>();
            for (float z = 0f; z <= 120f; z += 5f)
                departure.Add(P(-7f, z));
            tiles.Add(Together(Fork(mainEntry, rampEntry, -1, false, departure, out string n2)));
            notes.Add(n2);

            // The nose of the exit close up: 12 m of gore and 9 m of lead.
            tiles.Add(Together(Fork(main, ramp, 1, true, approach, out string n0, 12f, 9f)));
            notes.Add("close-up: " + n0);

            // A bent island drawn by 13 corners, V lines; and a yellow one with diagonals.
            var crescent = new List<Vec3>();
            for (int i = 0; i <= 6; i++)
            {
                double a = i * Math.PI / 12.0;
                crescent.Add(P((float)(20.0 * (1.0 - Math.Cos(a))), (float)(20.0 * Math.Sin(a))));
            }

            for (int i = 6; i >= 1; i--)
            {
                double a = i * Math.PI / 12.0;
                crescent.Add(P((float)(20.0 - 16.0 * Math.Cos(a)), (float)(16.0 * Math.Sin(a))));
            }

            GuideTemplate island = MarkingTemplates.ById("mk.channel");
            string islandSpec = MarkingTemplates.WithPolygon(island, island.Default().ToString(), MarkingPolygon.Encode(crescent));
            string yellowSpec = MarkingTemplates.WithPolygon(island, island.Parse(island.Default().ToString()).With("fill", "diagonal").With("colour", "y").ToString(), MarkingPolygon.Encode(crescent));
            tiles.Add(Together(new List<(MarkingPlan, Vec3, float)> { (MarkingTemplates.PlanOf(island, islandSpec), Vec3.Zero, 0f), (MarkingTemplates.PlanOf(island, yellowSpec), P(14f, 0f), 0f) }));
            notes.Add("islands: " + crescent.Count + " corners, V lines and yellow diagonals");

            // A strip 2 m wide along a bend of radius 40 m, 70 m long, cut into pieces as the tool cuts a run.
            var arc = new List<Vec3>();
            for (int i = 0; i <= 35; i++)
            {
                double a = i * 2.0 / 40.0;
                arc.Add(P((float)(40.0 * (1.0 - Math.Cos(a))), (float)(40.0 * Math.Sin(a))));
            }

            GuideTemplate strip = MarkingTemplates.ById("mk.strip");
            var stripItems = new List<(MarkingPlan, Vec3, float)>();
            float arcLength = 70f;
            foreach (RunItem item in MarkingRuns.Items(arc))
                stripItems.Add((MarkingTemplates.PlanOf(strip, MarkingTemplates.WithRun(strip, strip.Parse(strip.Default().ToString()).With("width", "2").ToString(), item.Path, item.Start, arcLength)), item.Position, item.Yaw));
            var offsetArc = MarkingFills.Offset(arc, -6f);
            foreach (RunItem item in MarkingRuns.Items(offsetArc))
                stripItems.Add((MarkingTemplates.PlanOf(strip, MarkingTemplates.WithRun(strip, strip.Parse(strip.Default().ToString()).With("width", "3").With("fill", "chevron").With("colour", "w").ToString(), item.Path, item.Start, arcLength)), item.Position, item.Yaw));
            tiles.Add(Together(stripItems));
            notes.Add("strips: yellow diagonals 2 m and white V lines 3 m along a bend, " + (stripItems.Count) + " pieces");

            const int w = 420, h = 840;
            var sheet = new byte[tiles.Count * w * h * 4];
            for (int t = 0; t < tiles.Count; t++)
            {
                byte[] raw = MarkingRaster.Render(tiles[t], w, h, out int rw, out int rh, 0.04f);
                for (int y = 0; y < rh; y++)
                    Array.Copy(raw, y * rw * 4, sheet, (y * tiles.Count * w + t * w) * 4, rw * 4);
            }

            File.WriteAllBytes(Path.Combine(output, "area-demos.png"), Png.Encode(sheet, tiles.Count * w, h, false));
            foreach (string note in notes)
                Console.WriteLine(note);
            return 0;
        }
    }
}
