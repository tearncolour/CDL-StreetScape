using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// Pictures of the markings laid along the road (0.12.49, MarkingBend), from made-up curves (no game): each drawn along a kerb that bends left or right, the kerb a thin grey line where the marking's
    /// kerb side is (x = MarkingTemplates.KerbAt of its frame). Usage: Preview.exe root out --bend-demos. Geometry only; not the game.
    /// </summary>
    internal static class BendDemos
    {
        /// <summary>A curve from the origin heading +Z, radius r, to the right (+X) or the left, length long.</summary>
        private static List<Vec3> Arc(float r, float length, bool right)
        {
            var points = new List<Vec3>();
            for (float s = 0f; s <= length + 1e-4f; s += 0.5f)
            {
                double a = s / r;
                float x = r - r * (float)Math.Cos(a);
                points.Add(new Vec3(right ? x : -x, 0f, r * (float)Math.Sin(a)));
            }

            return points;
        }

        public static int Run(string output)
        {
            (string Id, float Radius, float Length, bool Right, string More)[] demos =
            {
                ("mk.bay.perpendicular", 25f, 30f, true, ""), ("mk.bay.perpendicular", 25f, 30f, false, ""), ("mk.bay.angled", 30f, 30f, false, ""), ("mk.bay.parallel", 40f, 36f, true, "kerb=1"),
                ("mk.bay.taxi", 20f, 12f, false, ""), ("mk.kerbstop", 40f, 30f, true, ""), ("mk.busbay", 80f, 90f, false, "form=C"), ("mk.nopark.road", 20f, 20f, true, ""), ("mk.chevron", 150f, 80f, false, "")
            };
            var tiles = new List<MarkingPlan>();
            foreach ((string id, float radius, float length, bool right, string more) in demos)
            {
                GuideTemplate template = GuideTemplates.ById(id);
                GuideSpec s = template.Default().With(MarkingBend.Key, MarkingBend.Encode(Arc(radius, length + 2f, right), out _))
                    .With(MarkingBend.PlaceKey, MarkingBend.Drawn).With(MarkingBend.FillKey, ((int)Math.Round(length * 100f)).ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (string pair in more.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    s = s.With(pair.Substring(0, pair.IndexOf('=')), pair.Substring(pair.IndexOf('=') + 1));
                MarkingPlan bent = MarkingTemplates.PlanOf(template, s.ToString());
                var picture = new MarkingPlan();
                foreach (MarkingPart part in bent.Parts)
                {
                    var shape = new MarkingShape { Colour = part.Colour };
                    shape.Mesh.Append(part.Mesh, Vec3.Zero, 0f);
                    picture.Shapes.Add(shape);
                }

                float kerbAt = MarkingTemplates.KerbAt(template, s);
                var kerb = new List<Vec3>();
                for (float z = -2f; z <= length + 2f; z += 0.5f)
                    kerb.Add(bent.Bend.Map(float.IsNaN(kerbAt) ? 0f : kerbAt, z));
                var line = new MarkingShape { Colour = float.IsNaN(kerbAt) ? MarkingColour.Red : MarkingColour.Black };
                RoadMarkings.Ribbon(line.Mesh, kerb, 0.08f);
                picture.Shapes.Add(line);
                picture.Bake(0f);
                tiles.Add(picture);
                Console.WriteLine($"{id} drawn {length} m along a curve of {radius} m to the {(right ? "right" : "left")}: {bent.TriangleCount} triangles, straight {bent.StraightMaxZ:0.0} m long");
            }

            const int w = 360, h = 640;
            var sheet = new byte[tiles.Count * w * h * 4];
            for (int t = 0; t < tiles.Count; t++)
            {
                byte[] raw = MarkingRaster.Render(tiles[t], w, h, out int rw, out int rh, 0.04f);
                for (int y = 0; y < rh; y++)
                    Array.Copy(raw, y * rw * 4, sheet, (y * tiles.Count * w + t * w) * 4, rw * 4);
            }

            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "bend-demos.png"), Png.Encode(sheet, tiles.Count * w, h, false));
            return 0;
        }
    }
}
