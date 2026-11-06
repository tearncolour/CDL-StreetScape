using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// What MarkingGround.Lay makes of a kerb line (线48, 线50) over a complete read of the roads: both sides, three kerb heights, a flat and a sloping road, the footway on the kerb's side of the face, for a
    /// line placed at the foot and for one that an older version placed on the kerb's top; one number each (the laid settings, the origin's lift, the worst distance from the ground and the points). The
    /// golden file (Golden/kerbline-0.12.83.txt) was made by the code of 0.12.83 (main 2f41e01) in a worktree of it. A kerb line laid from a provisional read keeps what it had (0.12.84); a lay from a
    /// complete read may not move a bit.
    /// </summary>
    internal static class KerbLinePrint
    {
        private static ulong Fold(ulong h, uint v) => (h ^ v) * 1099511628211UL;

        private static ulong Print(string text, ulong h)
        {
            foreach (char c in text)
                h = Fold(h, c);
            return h;
        }

        /// <summary>The settings of a run along +Z over 24 m on the heights y(z) (the item's own frame is made by RunPath).</summary>
        private static string Spec(GuideTemplate template, string settings, Func<float, float> y)
        {
            var points = new List<Vec3>();
            for (float z = 0f; z <= 24.001f; z += 2f)
                points.Add(new Vec3(0f, y(z), z));
            string path = RunPath.Encode(points, out _);
            return MarkingTemplates.WithRun(template, template.Parse(settings).ToString(), path, 0f);
        }

        private static string One(SignDef def, string side, int height, float slope, bool top)
        {
            float rise = height / 100f, sign = side == "r" ? 1f : -1f;
            var position = new Vec3(100f, top ? 10f + rise : 10f, 200f);
            string spec = Spec(def.Template, $"side={side};height={height}", z => 10f + slope * z);
            // The footway on the kerb's side of the face (x = 100), the kerb's height over the carriageway.
            Func<List<Vec3>, List<Vec3>> ground = world =>
            {
                var on = new List<Vec3>(world.Count);
                foreach (Vec3 p in world)
                    on.Add(new Vec3(p.X, 10f + slope * (p.Z - 200f) + ((p.X - 100f) * sign >= 0f ? rise : 0f), p.Z));
                return on;
            };
            MarkingGround.Laid laid = MarkingGround.Lay(def, spec, position, 0f, ground);
            ulong h = 14695981039346656037UL;
            h = Print(laid.Spec, h);
            h = Fold(h, (uint)BitConverter.SingleToInt32Bits(laid.Dy));
            h = Fold(h, (uint)BitConverter.SingleToInt32Bits(laid.Worst));
            h = Fold(h, (uint)laid.Points);
            return h.ToString("x16");
        }

        /// <summary>"template|variant|case|number" for every kerb line over a complete read.</summary>
        public static List<string> Lines()
        {
            var lines = new List<string>();
            foreach (string id in new[] { "mk.kerb.nopark", "mk.kerb.nolong" })
            {
                SignDef def = SignCatalog.ById(id);
                foreach (string side in new[] { "r", "l" })
                {
                    foreach (int height in new[] { 15, 20, 40 })
                    {
                        foreach (float slope in new[] { 0f, 0.04f })
                        {
                            foreach (bool top in new[] { false, true })
                                lines.Add($"{id}|{side}{height}|{(slope == 0f ? "flat" : "slope")}{(top ? "-top" : "")}|{One(def, side, height, slope, top)}");
                        }
                    }
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
