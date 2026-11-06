using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// Road barriers as they are checked (0.12.49): the entries after the shape and the view (id, or id|key=value;...) laid along a line and drawn by the software renderer, side by side in
    /// barrier-&lt;shape&gt;-&lt;view&gt;.png. Shapes: straight (12 m), bend (radius 15 m, 80 degrees, 2 m chords as a road gives them), bendcut (the same bend as BarrierPath cuts it, 0.12.84), slope (12 m at 8 %), split (the bend as two pieces of one run,
    /// cut at its point nearest 7.3 m, as long runs are). Views: front (the traffic's side at an angle, as the thumbnails), back, square, squareback, top, end (along the line from its start), endback.
    /// Usage: Preview.exe root out --barrier-views shape view id ... (TS_PICTURE_SIZE: each picture's size, 900 by default).
    /// </summary>
    internal static class BarrierViews
    {
        public static int Run(string output, IList<string> args)
        {
            int at = -1;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "--barrier-views")
                    at = i;
            }

            if (at < 0 || args.Count < at + 4)
            {
                Console.WriteLine("usage: --barrier-views <straight|bend|bendcut|slope|split> <front|back|square|squareback|top|end|endback> <id[|k=v;...]> ...");
                return 1;
            }

            string shape = args[at + 1], view = args[at + 2];
            int size = int.TryParse(Environment.GetEnvironmentVariable("TS_PICTURE_SIZE"), out int asked) && asked >= 64 ? asked : 900;
            Vec3 dir;
            switch (view)
            {
                case "back": dir = new Vec3(0.75f, -0.42f, 0.52f); break;
                case "square": dir = new Vec3(-1f, -0.08f, 0.0001f); break;
                case "squareback": dir = new Vec3(1f, -0.08f, 0.0001f); break;
                case "top": dir = new Vec3(0.05f, -1f, 0.3f); break;
                case "end": dir = new Vec3(0.25f, -0.25f, 1f); break;
                case "endback": dir = new Vec3(-0.25f, -0.25f, -1f); break;
                default: dir = new Vec3(-0.75f, -0.42f, 0.52f); break;
            }

            List<Vec3> line = Line(shape);
            var wanted = new List<string>();
            for (int i = at + 3; i < args.Count; i++)
                wanted.Add(args[i]);
            int cols = Math.Min(2, wanted.Count), rows = (wanted.Count + cols - 1) / cols;
            var sheet = new byte[cols * size * rows * size * 4];
            for (int i = 0; i < sheet.Length; i += 4)
            {
                sheet[i] = 74;
                sheet[i + 1] = 74;
                sheet[i + 2] = 78;
                sheet[i + 3] = 255;
            }

            for (int n = 0; n < wanted.Count; n++)
            {
                int bar = wanted[n].IndexOf('|');
                string id = bar < 0 ? wanted[n] : wanted[n].Substring(0, bar), settings = bar < 0 ? "" : wanted[n].Substring(bar + 1);
                SignDef def = SignCatalog.ById(id);
                if (def == null || !def.IsMarking)
                {
                    Console.WriteLine("no such barrier: " + id);
                    continue;
                }

                var bodies = new List<(MeshBuilder Mesh, byte[] Rgb)>();
                var screens = new HashSet<MeshBuilder>();
                int triangles = 0;
                float cut = shape == "split" ? 7.3f : 0f, length = Length(line);
                foreach ((List<Vec3> piece, float phase) in cut > 0f ? Pieces(line, cut) : new List<(List<Vec3>, float)> { (line, 0f) })
                {
                    GuideSpec spec = def.Template.Default();
                    foreach (string pair in settings.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq > 0)
                            spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
                    }

                    string path = RunPath.Encode(piece, out float yaw);
                    MarkingPlan plan = MarkingTemplates.PlanOf(def.Template, MarkingTemplates.WithRun(def.Template, spec.ToString(), path, phase, length));
                    triangles += plan.TriangleCount;
                    foreach (MarkingPart part in plan.Parts)
                    {
                        var placed = new MeshBuilder();
                        placed.Append(part.Mesh, piece[0], yaw);
                        bodies.Add((placed, Raster.SolidRgb(part.Solid ?? SolidColour.White)));
                        if (DetailSurfaces.HasHoles(part.Detail) || (part.Solid.HasValue && SolidGlass.Is(part.Solid.Value)))
                            screens.Add(placed);
                    }
                }

                Console.WriteLine($"{wanted[n]} {shape} {view}: {triangles} triangles");
                byte[] raw = Raster.RenderBodies(bodies, dir, size, out int w, out int h, new RasterOptions { Transparent = true }, screens);
                int ox = n % cols * size + Math.Max(0, (size - w) / 2), oy = n / cols * size + Math.Max(0, (size - h) / 2);
                for (int y = 0; y < Math.Min(h, size); y++)
                {
                    for (int x = 0; x < Math.Min(w, size); x++)
                    {
                        int s = (y * w + x) * 4;
                        if (raw[s + 3] == 0)
                            continue;
                        int d = ((oy + Math.Min(h, size) - 1 - y) * cols * size + ox + x) * 4;
                        Array.Copy(raw, s, sheet, d, 3);
                    }
                }
            }

            File.WriteAllBytes(Path.Combine(output, $"barrier-{shape}-{view}.png"), Png.Encode(sheet, cols * size, rows * size, false));
            return 0;
        }

        private static List<Vec3> Line(string shape)
        {
            var line = new List<Vec3>();
            if (shape == "bendcut")
            {
                const float rc = 15f;
                return BarrierPath.Sample(s => new Vec3(rc * (1f - (float)Math.Cos(s / rc)), 0f, rc * (float)Math.Sin(s / rc)), 0f, rc * (float)(80.0 * Math.PI / 180.0));
            }

            if (shape == "bend" || shape == "split")
            {
                const float r = 15f;
                double sweep = 80.0 * Math.PI / 180.0;
                int chords = (int)Math.Ceiling(r * sweep / 2.0);
                for (int i = 0; i <= chords; i++)
                {
                    double a = sweep * i / chords;
                    line.Add(new Vec3((float)(r * (1.0 - Math.Cos(a))), 0f, (float)(r * Math.Sin(a))));
                }

                return line;
            }

            line.Add(Vec3.Zero);
            line.Add(new Vec3(0f, shape == "slope" ? 0.96f : 0f, 12f));
            return line;
        }

        private static float Length(IList<Vec3> line)
        {
            float total = 0f;
            for (int i = 1; i < line.Count; i++)
                total += Vec3.DistanceXZ(line[i - 1], line[i]);
            return total;
        }

        /// <summary>The line as two pieces of one run, cut at its point nearest s (pieces meet at a point of the line, as MarkingRuns cuts the points it takes every 2 m along a road), with where each starts.</summary>
        private static List<(List<Vec3>, float)> Pieces(List<Vec3> line, float s)
        {
            int cut = 1;
            float along = 0f, best = float.MaxValue, at = 0f;
            for (int i = 1; i + 1 < line.Count; i++)
            {
                along += Vec3.DistanceXZ(line[i - 1], line[i]);
                if (Math.Abs(along - s) < best)
                {
                    best = Math.Abs(along - s);
                    cut = i;
                    at = along;
                }
            }

            return new List<(List<Vec3>, float)> { (line.GetRange(0, cut + 1), 0f), (line.GetRange(cut, line.Count - cut), at) };
        }
    }
}
