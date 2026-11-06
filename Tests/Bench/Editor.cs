using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What opening the editor of a board costs on the main thread (BENCH_EDITOR=1, 0.12.63; the owner: "加载参数显示会卡住主线程"): TsUiSystem makes, at every change of a board's settings, its layout
    /// (SignInstances.Of), its picture (LedScreens.PaintFace, 768 px at most, GDI+), the clear pixels of a cut-out picture and the PNG, and then the JSON of its settings; this times the first three for each
    /// board of the catalogue and counts what the panel will have to make of its fields. Desktop .NET: the game's Mono is slower, so the times are a lower bound.
    /// </summary>
    internal static class Editor
    {
        public static void Run()
        {
            var rows = new List<(string Id, int Fields, int Options, double Layout, double Paint, double Encode, int W, int H)>();
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                if (choice.Def.IsMarking || choice.Def.Template == null)
                    continue;
                var clock = Stopwatch.StartNew();
                SignInstance instance = SignInstances.Of(choice.Def, choice.Spec);
                double layout = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                byte[] raw = LedScreens.PaintFace(instance, out int w, out int h);
                double paint = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                LegendPainter.CutAway(instance, raw, w, h);
                byte[] png = Png.Encode(raw, w, h, true);
                double encode = clock.Elapsed.TotalMilliseconds;
                GuideField[] fields = choice.Def.Template.Fields;
                rows.Add((choice.Id, fields.Length, fields.Sum(f => f.Choices?.Length ?? 0), layout, paint, encode, w, h));
            }

            Console.WriteLine($"editor of a board: {rows.Count} boards of the catalogue (a template's default settings), first time each, milliseconds:");
            double Total((string Id, int Fields, int Options, double Layout, double Paint, double Encode, int W, int H) r) => r.Layout + r.Paint + r.Encode;
            foreach (var r in rows.OrderByDescending(Total).Take(12))
                Console.WriteLine($"  {r.Id,-26} {r.W}x{r.H}  layout {r.Layout,6:0.0}  paint {r.Paint,6:0.0}  encode {r.Encode,5:0.0}  = {Total(r),6:0.0} ms   {r.Fields} settings, {r.Options} options");
            var sorted = rows.Select(Total).OrderBy(x => x).ToList();
            Console.WriteLine($"  median {sorted[sorted.Count / 2]:0.0} ms, 90th {sorted[sorted.Count * 9 / 10]:0.0} ms, worst {sorted[sorted.Count - 1]:0.0} ms; over 16 ms: {sorted.Count(x => x > 16)}, over 33 ms: {sorted.Count(x => x > 33)}");
            var byFields = rows.OrderByDescending(r => r.Fields).Take(8).ToList();
            Console.WriteLine("  the most settings: " + string.Join(", ", byFields.Select(r => $"{r.Id} {r.Fields}")));
            Console.WriteLine($"  settings per board: median {rows.Select(r => r.Fields).OrderBy(x => x).ElementAt(rows.Count / 2)}, most {rows.Max(r => r.Fields)}; boards with over 12: {rows.Count(r => r.Fields > 12)}, over 20: {rows.Count(r => r.Fields > 20)}");

            // The editor of a marking (0.12.63): its plan and its picture from above, made on the main thread at every change.
            var marks = new List<(string Id, double Plan, double Raster, double Encode)>();
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                if (!choice.Def.IsMarking || choice.Def.Template == null)
                    continue;
                string text = choice.Def.Template.Parse(choice.Spec ?? "").ToString();
                var clock = Stopwatch.StartNew();
                MarkingPlan plan = MarkingTemplates.PlanOf(choice.Def, text);
                double planMs = clock.Elapsed.TotalMilliseconds;
                if (plan == null || plan.Parts.Count == 0)
                    continue;
                clock.Restart();
                byte[] raw = MarkingRaster.Render(plan, 256, out int w, out int h);
                double rasterMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                Png.Encode(raw, w, h, false);
                marks.Add((choice.Id, planMs, rasterMs, clock.Elapsed.TotalMilliseconds));
            }

            double MarkTotal((string Id, double Plan, double Raster, double Encode) r) => r.Plan + r.Raster + r.Encode;
            var markSorted = marks.Select(MarkTotal).OrderBy(x => x).ToList();
            Console.WriteLine($"editor of a marking: {marks.Count} markings, first time each: median {markSorted[markSorted.Count / 2]:0.0} ms, 90th {markSorted[markSorted.Count * 9 / 10]:0.0}, worst {markSorted[markSorted.Count - 1]:0.0}; over 16 ms: {markSorted.Count(x => x > 16)}");
            foreach (var r in marks.OrderByDescending(MarkTotal).Take(6))
                Console.WriteLine($"  {r.Id,-24} plan {r.Plan,6:0.0}  raster {r.Raster,6:0.0}  encode {r.Encode,5:0.0}  = {MarkTotal(r),6:0.0} ms");

            // The same board again with one letter changed, as typing does: the layout is new, the font and the painter are warm.
            SignChoice sample = SignCatalog.Choices.First(c => c.Id == rows.OrderByDescending(Total).First().Id);
            var again = new List<double>();
            for (int i = 0; i < 8; i++)
            {
                string spec = sample.Def.Template.Parse(sample.Spec ?? "").With(sample.Def.Template.Fields.First(f => f.Kind == GuideFieldKind.Text).Key, "测试" + i).ToString();
                var clock = Stopwatch.StartNew();
                SignInstance instance = SignInstances.Of(sample.Def, spec);
                byte[] raw = LedScreens.PaintFace(instance, out int w, out int h);
                LegendPainter.CutAway(instance, raw, w, h);
                Png.Encode(raw, w, h, true);
                again.Add(clock.Elapsed.TotalMilliseconds);
            }

            Console.WriteLine($"  {sample.Id} with a letter typed (warm): {string.Join(", ", again.Select(x => x.ToString("0.0")))} ms");
        }
    }
}
