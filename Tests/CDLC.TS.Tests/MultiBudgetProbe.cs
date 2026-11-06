using System;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>Probes of the junctions-ahead board (run with "multi-budget" or "multi-contours k=v ..."): the bytes its spec takes at the limits (to size them against the 480 bytes of the saved field), and the boxes of the figure's contours.</summary>
    internal static class MultiBudgetProbe
    {
        public static int Run()
        {
            SignDef def = SignCatalog.ById(RoadGuideSigns.MultiId);
            GuideTemplate t = def.Template;
            GuideSpec s = GuideSpec.Defaults(t);
            Console.WriteLine($"defaults: {Encoding.UTF8.GetByteCount(s.ToString())} bytes, {t.Fields.Length} fields");
            foreach (int names in new[] { 4, 6, 8, 10 })
            {
                foreach (int latin in new[] { 0, 8, 16, 26 })
                {
                    s = GuideSpec.Defaults(t).With("n", "3");
                    for (int i = 1; i <= 3; i++)
                    {
                        s = s.With("k" + i, i == 3 ? "d" : "p").With("b" + i, "b");
                        foreach (string k in new[] { "lz", "rz", "xz" })
                            s = s.With(k + i, new string('路', names));
                        foreach (string k in new[] { "le", "re", "xe" })
                            s = s.With(k + i, new string('a', latin));
                    }

                    s = s.With("road", new string('路', names)).With("dz", new string('路', names)).With("de", new string('a', latin)).With("cz", "中山大道快速").With("ce", new string('a', Math.Min(latin, 24)));
                    int bytes = Encoding.UTF8.GetByteCount(s.ToString());
                    bool kept = true;
                    for (int i = 1; i <= 3; i++)
                        kept &= s.Get("lz" + i).Length == names && s.Get("xz" + i).Length == names && s.Get("le" + i).Length == latin;
                    Console.WriteLine($"names {names,2}, English {latin,2}: {bytes,3} bytes after the trim{(kept ? "" : "  <- the spec cut something")}");
                }
            }

            return 0;
        }

        public static int Contours(string[] pairs)
        {
            GuideTemplate template = GuideTemplates.ById(RoadGuideSigns.MultiId);
            GuideSpec s = GuideSpec.Defaults(template);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                s = s.With(pairs[i], pairs[i + 1]);
            GuideBoard b = template.Layout(s);
            float h = b.H;
            Console.WriteLine($"the board's h = {h} (band 0: {RoadGuideSigns.CharHeight(0)}), road = {RoadGuideSigns.MultiRoadK * h}, board {b.Main.W / h:0.00} h x {b.Main.H / h:0.00} h");
            foreach (GuideElement e in b.Main.Elements)
            {
                if (e.Kind != GuideElementKind.Path)
                    continue;
                for (int k = 0; k < e.Contours.Count; k++)
                {
                    float[] c = e.Contours[k];
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                    for (int i = 0; i + 1 < c.Length; i += 2)
                    {
                        x0 = Math.Min(x0, c[i]);
                        x1 = Math.Max(x1, c[i]);
                        y0 = Math.Min(y0, c[i + 1]);
                        y1 = Math.Max(y1, c[i + 1]);
                    }

                    Console.WriteLine($"  #{k,2}: {c.Length / 2,3} points, x {x0 / h,6:0.000}..{x1 / h,6:0.000} h, y {y0 / h,6:0.000}..{y1 / h,6:0.000} h, {(x1 - x0) / h:0.000} x {(y1 - y0) / h:0.000}");
                }
            }

            return 0;
        }
    }
}
