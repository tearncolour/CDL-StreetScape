using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// BENCH_MAINTHREAD=1: what the owner's 0.12.73 log shows on the main thread, measured offline (.NET Framework, not the game's Mono): (1) a planter's plan made the first time (what PlantSystem's pass did
    /// on the main thread for each record whose plan the tile workers had not made) against kept, and the pass over its anchors; (2) the arrays of a mesh for Unity (MeshTangents and the copy MeshUpload.Prepare
    /// does) for the meshes of a preview item, which the run preview put on the main thread until 0.12.75.
    /// </summary>
    internal static class MainThread
    {
        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        public static void Run()
        {
            Console.WriteLine("== planted templates: the plan the first time, kept, and the pass over its anchors");
            Console.WriteLine($"  {"template",-18} {"length",6} {"first ms",9} {"kept us",9} {"anchors",8} {"offer ms",9}");
            foreach (string id in MarkingTemplates.PlantedIds)
            {
                GuideTemplate t = MarkingTemplates.ById(id);
                if (t == null)
                    continue;
                foreach (float length in new[] { 30f, 100f, 180f })
                {
                    var line = new List<Vec3>();
                    for (float s = 0f; s < length + 0.01f; s += 2f)
                        line.Add(new Vec3(0f, 0f, s));
                    string spec = MarkingTemplates.WithRun(t, t.Default().ToString(), RunPath.Encode(line, out _), 0f, 0f);
                    MarkingTemplates.ClearCaches();
                    long t0 = Stopwatch.GetTimestamp();
                    MarkingPlan plan = MarkingTemplates.PlanOf(t, spec);
                    double first = Ms(Stopwatch.GetTimestamp() - t0);
                    t0 = Stopwatch.GetTimestamp();
                    for (int i = 0; i < 200; i++)
                        MarkingTemplates.PlanOf(t, spec);
                    double kept = Ms(Stopwatch.GetTimestamp() - t0) * 1000.0 / 200;
                    int anchors = plan == null ? 0 : plan.BakedProps.Count;
                    var ledger = new PlantLedger<(int, int), int>();
                    var gone = new List<int>();
                    t0 = Stopwatch.GetTimestamp();
                    ledger.BeginPass();
                    for (int i = 0; i < anchors; i++)
                    {
                        PropAnchor anchor = plan.BakedProps[i];
                        var at = new Vec3(anchor.Local.X, anchor.Local.Y, anchor.Local.Z);
                        ledger.Offer((1, i), new PlantWish { At = at, Yaw = anchor.Yaw, Hash = PlantHash.Of(at, anchor.Yaw, (uint)i, anchor.Kind), Anchor = anchor }, p => true, gone);
                    }

                    double offer = Ms(Stopwatch.GetTimestamp() - t0);
                    Console.WriteLine($"  {id,-18} {length,5:0}m {first,9:0.00} {kept,9:0.0} {anchors,8} {offer,9:0.000}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("== the arrays of a mesh (what MeshUpload.Prepare does besides the copies: the tangents) by vertex count");
            Console.WriteLine($"  {"vertices",9} {"tangents ms",12} {"arrays ms",10}");
            foreach (int target in new[] { 20000, 100000, 300000 })
            {
                var mesh = new MeshBuilder();
                var rng = new Random(7);
                while (mesh.VertexCount < target)
                {
                    float ox = (float)rng.NextDouble() * 100f, oz = (float)rng.NextDouble() * 100f;
                    int a = mesh.Vertex(new Vec3(ox, 0f, oz), new Vec3(0f, 1f, 0f), 0f, 0f);
                    mesh.Vertex(new Vec3(ox + 1f, 0f, oz), new Vec3(0f, 1f, 0f), 1f, 0f);
                    mesh.Vertex(new Vec3(ox, 0f, oz + 1f), new Vec3(0f, 1f, 0f), 0f, 1f);
                    mesh.Vertex(new Vec3(ox + 1f, 0f, oz + 1f), new Vec3(0f, 1f, 0f), 1f, 1f);
                    mesh.Tri(a, a + 1, a + 2);
                    mesh.Tri(a + 1, a + 3, a + 2);
                }

                double tangents = Best(5, () => MeshTangents.Compute(mesh));
                double arrays = Best(5, () =>
                {
                    int n = mesh.VertexCount;
                    var p = new float[n * 3];
                    var nr = new float[n * 3];
                    var uv = new float[n * 2];
                    var tg = new float[n * 4];
                    for (int i = 0; i < n; i++)
                    {
                        p[i * 3] = mesh.Positions[i].X;
                        nr[i * 3] = mesh.Normals[i].X;
                        uv[i * 2] = mesh.U[i];
                        tg[i * 4] = uv[i * 2];
                    }
                });
                Console.WriteLine($"  {mesh.VertexCount,9} {tangents,12:0.0} {arrays,10:0.0}");
            }
        }

        private static double Best(int runs, Action action)
        {
            double best = double.MaxValue;
            for (int i = 0; i < runs; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                action();
                best = Math.Min(best, Ms(Stopwatch.GetTimestamp() - t0));
            }

            return best;
        }
    }
}
