using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What one move of a drawn line's end costs on a long road (BENCH_DRAG=1; 0.12.51, the owner: "优化超长链摆放时候的掉帧问题"): the tool makes the line again at every half metre the end moves, so
    /// each figure is one such move with the end that far from the start: the line along the chain (at a lateral, or by the road's edge as a roadside barrier is), its items, the overlay's points and
    /// the preview's meshes (made only up to 20 items). Pure parts only; the surface heights (cached windows) and Unity's mesh upload are not here.
    /// </summary>
    internal static class Drag
    {
        private static double Time(int repeats, Action action)
        {
            action();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++)
                action();
            return clock.Elapsed.TotalMilliseconds / repeats;
        }

        /// <summary>A six-lane road of segments of 60 m (the owner's long roads were 48 to 51 segments over 3 km), winding and rolling.</summary>
        private static RoadChain Road(int segments)
        {
            float[] with = { 1.75f, 5.25f, 8.75f }, against = { -1.75f, -5.25f, -8.75f };
            var pieces = new List<ChainPiece>();
            Vec3 at = Vec3.Zero;
            double heading = 0.0;
            for (int i = 0; i < segments; i++)
            {
                double h1 = heading + Math.Sin(i * 0.7) * 0.2;
                float rise = (float)(Math.Sin(i * 0.9) * 3.0);
                Vec3 d0 = new Vec3((float)Math.Sin(heading), 0f, (float)Math.Cos(heading)), d1 = new Vec3((float)Math.Sin(h1), 0f, (float)Math.Cos(h1));
                Vec3 end = at + (d0 + d1) * 30f + new Vec3(0f, rise, 0f);
                var piece = new ChainPiece { Curve = new Bezier3(at, at + d0 * 20f, end - d1 * 20f, end), Kmh = 80f, HasSpeed = true, HalfWidth = 12f, After = i == segments - 1 ? ChainJoint.End : ChainJoint.Pass };
                piece.Forward.AddRange(with);
                piece.Backward.AddRange(against);
                pieces.Add(piece);
                at = end;
                heading = h1;
            }

            return new RoadChain(pieces);
        }

        public static void Run()
        {
            RoadChain chain = Road(50);
            Console.WriteLine($"a drawn line's end moved on a road of {chain.Pieces.Count} segments, {chain.Length:0} m; milliseconds for one move:");
            // preview: all the items' meshes (as until 0.12.50); end item: the one made at every move since 0.12.51 (the others are kept, or made within a budget a frame once they change).
            Console.WriteLine($"  {"end from the start",-20} {"line",8} {"by edge",8} {"items",8} {"overlay",8} {"preview",9} {"end item",9} {"items",6}");
            foreach ((string id, bool byEdge) in new[] { ("bar.noise", true), ("bar.wbeam", true), ("mk.line.lane", false) })
            {
                GuideTemplate template = MarkingTemplates.ById(id);
                string spec = template.Default().ToString();
                Console.WriteLine("  " + id + ":");
                foreach (float reach in new[] { 100f, 500f, 1000f, 2000f, 2990f })
                {
                    float s0 = 5f, s1 = s0 + reach;
                    float lateral = 10.5f;
                    var profile = new EdgeProfile(chain, 1);
                    Func<float, float> edge = s => profile.At(s) + 0.5f;
                    List<Vec3> line = null;
                    double plain = Time(5, () => chain.Line(s0, s1, lateral, 0f));
                    double alongEdge = byEdge ? Time(3, () => line = chain.Line(s0, s1, edge, 0f)) : 0.0;
                    if (!byEdge)
                        line = chain.Line(s0, s1, lateral, 0f);
                    List<RunItem> items = null;
                    double cut = Time(5, () => items = MarkingRuns.Items(line));
                    double overlay = Time(5, () => OverlayLine.Simplify(line));
                    double preview = double.NaN, endItem = double.NaN;
                    if (items.Count <= 20)
                    {
                        float length = reach;
                        // One part: a barrier's place in its part and a marking's along the whole line are the same.
                        void Make(RunItem item, MeshBuilder mesh)
                        {
                            MarkingPlan plan = MarkingTemplates.PlanOf(template, MarkingTemplates.WithRun(template, spec, item.Path, item.Start, length), false);
                            foreach (MarkingPart part in plan.Parts)
                                mesh.Append(part.Mesh, item.Position, item.Yaw);
                        }

                        preview = Time(1, () =>
                        {
                            var mesh = new MeshBuilder();
                            foreach (RunItem item in items)
                                Make(item, mesh);
                        });
                        endItem = Time(3, () => Make(items[items.Count - 1], new MeshBuilder()));
                    }

                    string Ms(double ms) => double.IsNaN(ms) ? "    (off)" : ms.ToString("0.00").PadLeft(9);
                    Console.WriteLine($"  {reach,10:0} m {"",8} {plain,8:0.00} {alongEdge,8:0.00} {cut,8:0.00} {overlay,8:0.00} {Ms(preview)} {Ms(endItem)} {items.Count,6}");
                }
            }
        }
    }
}
