using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// An island drawn by the corners of a placed one (world x,z, from the log's "corner N of an island at (...)" lines), from above, with each fill: Preview.exe root out --island x,z x,z ...
    /// File out/island.png. Geometry only, unfitted (no road).
    /// </summary>
    internal static class IslandProbe
    {
        public static int Run(string output, IList<string> corners)
        {
            var world = new List<Vec3>();
            foreach (string c in corners)
            {
                string[] xz = c.Split(',');
                world.Add(new Vec3(float.Parse(xz[0], CultureInfo.InvariantCulture), 0f, float.Parse(xz[1], CultureInfo.InvariantCulture)));
            }

            if (world.Count < 3)
            {
                Console.WriteLine("FAIL three corners at least");
                return 1;
            }

            Vec3 origin = world[0];
            var local = new List<Vec3>();
            foreach (Vec3 p in world)
                local.Add(p - origin);
            GuideTemplate island = MarkingTemplates.ById(MarkingTemplates.ChannelId);
            var plans = new List<MarkingPlan>();
            foreach (string fill in new[] { "chevron", "diagonal" })
            {
                string spec = MarkingTemplates.WithPolygon(island, island.Parse(island.Default().ToString()).With("fill", fill).ToString(), MarkingPolygon.Encode(local));
                MarkingPlan plan = MarkingTemplates.PlanOf(island, spec);
                int triangles = 0;
                foreach (MarkingPart part in plan.Parts)
                    triangles += part.Mesh.TriangleCount;
                Console.WriteLine($"island {fill}: {plan.Parts.Count} part(s), {triangles} triangle(s)");
                plans.Add(plan);
            }

            const int w = 700, h = 700;
            var sheet = new byte[plans.Count * w * h * 4];
            for (int t = 0; t < plans.Count; t++)
            {

                byte[] raw = MarkingRaster.Render(plans[t], w, h, out int rw, out int rh, 0.06f);
                for (int y = 0; y < rh; y++)
                    Array.Copy(raw, y * rw * 4, sheet, (y * plans.Count * w + t * w) * 4, rw * 4);
            }

            File.WriteAllBytes(Path.Combine(output, "island.png"), Png.Encode(sheet, plans.Count * w, h, false));
            return 0;
        }
    }
}
