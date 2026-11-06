using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The paint of every marking template that runs along a road, as 0.12.82 built it over a path at one height: a straight one and a bend, one number each (every buffer of every part to the last bit).
    /// The golden file (Golden/runcut-flat-0.12.82.txt) was made by the code of 0.12.82 (main d530646) in a worktree of it. The bars of a strip follow the path's height since 0.12.83 and the paint of a
    /// strip is cut to the road; neither may move one vertex of a marking on the flat.
    /// </summary>
    internal static class RunCutPrint
    {
        private static List<Vec3> Straight()
        {
            var points = new List<Vec3>();
            for (float z = 0f; z <= 40.001f; z += 2f)
                points.Add(new Vec3(0f, 0f, z));
            return points;
        }

        private static List<Vec3> Bend()
        {
            // A quarter turn to the right of radius 30 m, in chords of about 2 m.
            var points = new List<Vec3>();
            for (int i = 0; i <= 24; i++)
            {
                double a = i * (Math.PI / 2.0) / 24.0;
                points.Add(new Vec3((float)(30.0 * (1.0 - Math.Cos(a))), 0f, (float)(30.0 * Math.Sin(a))));
            }

            return points;
        }

        private static IEnumerable<string> Variants(GuideTemplate template)
        {
            yield return template.Default().ToString();
            if (template.Id == "mk.strip")
            {
                foreach (string fill in new[] { "chevron", "diagonal", "cycle" })
                {
                    foreach (string side in new[] { "r", "l" })
                        yield return $"width=3;side={side};fill={fill};colour=y;line=40;outline=20;tip=e";
                }
            }

            if (template.Id == "mk.line.doubleyellow")
            {
                foreach (string gap in new[] { "50", "100", "200" })
                    yield return template.Parse(template.Default().ToString()).With("gap", gap).ToString();
            }
        }

        private static ulong Fold(ulong h, uint v) => (h ^ v) * 1099511628211UL;

        private static ulong Print(MeshBuilder mesh, ulong h)
        {
            h = Fold(h, (uint)mesh.VertexCount);
            foreach (Vec3 p in mesh.Positions)
            {
                h = Fold(h, (uint)BitConverter.SingleToInt32Bits(p.X));
                h = Fold(h, (uint)BitConverter.SingleToInt32Bits(p.Y));
                h = Fold(h, (uint)BitConverter.SingleToInt32Bits(p.Z));
            }

            foreach (Vec3 n in mesh.Normals)
            {
                h = Fold(h, (uint)BitConverter.SingleToInt32Bits(n.X));
                h = Fold(h, (uint)BitConverter.SingleToInt32Bits(n.Y));
                h = Fold(h, (uint)BitConverter.SingleToInt32Bits(n.Z));
            }

            foreach (int i in mesh.Indices)
                h = Fold(h, (uint)i);
            return h;
        }

        /// <summary>"template|variant|path|number" for every run marking.</summary>
        public static List<string> Lines()
        {
            var lines = new List<string>();
            foreach (GuideTemplate template in MarkingTemplates.All)
            {
                if (!MarkingTemplates.IsRun(template) || !template.Id.StartsWith("mk.", StringComparison.Ordinal))
                    continue;
                int variant = 0;
                foreach (string settings in Variants(template))
                {
                    foreach ((string name, List<Vec3> points) in new[] { ("straight", Straight()), ("bend", Bend()) })
                    {
                        string path = RunPath.Encode(points, out _);
                        string spec = MarkingTemplates.WithRun(template, template.Parse(settings).ToString(), path, 0f, 0f);
                        MarkingPlan plan = MarkingTemplates.PlanOf(template, spec, false);
                        ulong h = 14695981039346656037UL;
                        foreach (MarkingPart part in plan.Parts)
                        {
                            h = Fold(h, (uint)part.Colour);
                            h = Fold(h, part.Solid.HasValue ? 1u : 0u);
                            h = Print(part.Mesh, h);
                        }

                        lines.Add(template.Id + "|" + variant + "|" + name + "|" + h.ToString("x16"));
                    }

                    variant++;
                }
            }

            return lines;
        }

        public static int Write(string file)
        {
            List<string> lines = Lines();
            File.WriteAllLines(file, lines, new UTF8Encoding(false));
            Console.WriteLine(lines.Count + " builds written to " + file);
            return 0;
        }
    }
}
