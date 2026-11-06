using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What the automatic marking of a stretch costs (BENCH_AUTO=1): the chain of a long road (its samples), the plan (the lines, the junctions, the sight distance of a two-lane road), the lines and the
    /// items cut from them, and the paint of every item (what the tiles build when they are placed). Pure parts only, on this runtime (Unity's Mono when run with it).
    /// </summary>
    internal static class Auto
    {
        private static double Time(int repeats, Action action)
        {
            action();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++)
                action();
            return clock.Elapsed.TotalMilliseconds / repeats;
        }

        private static void Line(string what, double ms) => Console.WriteLine($"  {what,-78} {ms,10:0.000} ms");

        private static ChainPiece Piece(Bezier3 curve, float[] forward, float[] backward, float kmh, ChainJoint after)
        {
            var piece = new ChainPiece { Curve = curve, Kmh = kmh, HasSpeed = true, After = after };
            piece.Forward.AddRange(forward);
            piece.Backward.AddRange(backward);
            return piece;
        }

        /// <summary>A rolling, winding road of segments of 100 m: a crest and a bend every few segments.</summary>
        private static List<ChainPiece> Road(float length, float[] forward, float[] backward, float kmh)
        {
            var pieces = new List<ChainPiece>();
            int n = (int)(length / 100f);
            Vec3 at = new Vec3(0f, 0f, 0f);
            double heading = 0.0;
            for (int i = 0; i < n; i++)
            {
                double turn = Math.Sin(i * 0.7) * 0.35;
                double h1 = heading + turn;
                float rise = (float)(Math.Sin(i * 0.9) * 6.0);
                Vec3 d0 = new Vec3((float)Math.Sin(heading), 0f, (float)Math.Cos(heading)), d1 = new Vec3((float)Math.Sin(h1), 0f, (float)Math.Cos(h1));
                Vec3 end = at + (d0 + d1) * 50f + new Vec3(0f, rise, 0f);
                var curve = new Bezier3(at, at + d0 * 33f + new Vec3(0f, rise * 0.4f, 0f), end - d1 * 33f - new Vec3(0f, rise * 0.4f, 0f), end);
                pieces.Add(Piece(curve, forward, backward, kmh, i == n - 1 ? ChainJoint.End : ChainJoint.Pass));
                at = end;
                heading = h1;
            }

            return pieces;
        }

        public static void Run()
        {
            Console.WriteLine("automatic marking of a stretch, milliseconds:");
            float[] one = { 1.75f }, oneBack = { -1.75f }, two = { 1.75f, 5.25f }, twoBack = { -1.75f, -5.25f };
            foreach ((string name, float length, float[] f, float[] b, float kmh) in new[] { ("two-lane road 1 km 60 km/h", 1000f, one, oneBack, 60f), ("two-lane road 3 km 80 km/h", 3000f, one, oneBack, 80f), ("four-lane road 3 km 50 km/h", 3000f, two, twoBack, 50f) })
            {
                List<ChainPiece> pieces = Road(length, f, b, kmh);
                RoadChain chain = null;
                Line(name + ": the chain", Time(5, () => chain = new RoadChain(Road(length, f, b, kmh))));
                AutoPlan plan = null;
                Line(name + ": the plan", Time(3, () => plan = AutoMarking.Plan(chain, null, AutoOptions.Default)));
                AutoOptions noSight = AutoOptions.Default;
                noSight.NoPassing = false;
                Line(name + ": the plan without the sight distance", Time(3, () => AutoMarking.Plan(chain, null, noSight)));
                Line(name + ": the sight distance alone (both ways)", Time(3, () =>
                {
                    SightDistance.Ahead(chain, true, 1.75f, -1.75f, s => 6f, 550f);
                    SightDistance.Ahead(chain, false, -1.75f, 1.75f, s => 6f, 550f);
                }));
                var lines = new List<List<Vec3>>();
                int items = 0;
                Line(name + ": the lines and their items", Time(3, () =>
                {
                    lines.Clear();
                    items = 0;
                    foreach (AutoRun run in plan.Runs)
                    {
                        List<Vec3> line = chain.Line(run.S0, run.S1, run.LateralAt, 0f);
                        lines.Add(line);
                        items += MarkingRuns.Items(line).Count;
                    }
                }));
                int points = 0;
                foreach (List<Vec3> l in lines)
                    points += l.Count;
                Console.WriteLine($"    {plan.Runs.Count} line(s), {items} item(s), {points} overlay point(s)");
                Line(name + ": the paint of every item (no cache)", Time(1, () =>
                {
                    foreach (AutoRun run in plan.Runs)
                    {
                        GuideTemplate t = MarkingTemplates.ById(run.Id);
                        GuideSpec spec = t.Default();
                        foreach (KeyValuePair<string, string> kv in run.Settings)
                            spec = spec.With(kv.Key, kv.Value);
                        List<Vec3> line = chain.Line(run.S0, run.S1, run.LateralAt, 0f);
                        foreach (RunItem item in MarkingRuns.Items(line))
                            t.Marking(t.Parse(MarkingTemplates.WithRun(t, spec.ToString(), item.Path, item.Start, 100f))).Bake(0f);
                    }
                }));
                Line(name + ": the overlay, simplified", Time(3, () =>
                {
                    int kept = 0;
                    foreach (List<Vec3> l in lines)
                        kept += OverlayLine.Simplify(l, 0.05f).Count;
                }));
                int simplified = 0;
                foreach (List<Vec3> l in lines)
                    simplified += OverlayLine.Simplify(l, 0.05f).Count;
                Console.WriteLine($"    the overlay simplified to {simplified} point(s) of {points}");
            }

            // 0.12.4: an area marking fitted to the road: a gore 60 m long over a junction's part and two roads in bands of 10 cells (as SurfaceReader makes them), and an island of 40 x 8 m.
            var main = new List<Vec3>();
            var ramp = new List<Vec3>();
            for (int i = 0; i <= 24; i++)
            {
                float z = i * 2.5f;
                main.Add(new Vec3(0f, 0f, z));
                ramp.Add(new Vec3(8f * (z / 60f) * (z / 60f), 0f, z));
            }

            MarkingShape gore = MarkingAreas.Gore(main, ramp, true);
            var strips = new List<SurfaceStrip>();
            foreach ((float x0, float x1, float z0, float z1) in new[] { (-10f, 18f, -20f, 20f), (-10f, -0.5f, 20f, 70f), (1.5f, 22f, 20f, 70f), (-10f, 18f, 60f, 100f) })
            {
                var strip = new SurfaceStrip();
                for (int i = 0; i <= 8; i++)
                {
                    float z = z0 + (z1 - z0) * i / 8f;
                    strip.Add(new Vec3(x0, 0.04f * z, z), new Vec3(x1, 0.04f * z, z));
                }

                strips.Add(strip);
            }

            AreaFit fit = null;
            Line("a gore over a junction: the fit from 4 bands of road", Time(20, () => fit = AreaFit.FromStrips(strips, Vec3.Zero, 0f, -1f, 9f, 0f, 60f)));
            MeshBuilder draped = null;
            Line($"a gore over a junction: its paint ({gore.Mesh.TriangleCount} triangles) cut and laid on {fit.PieceCount} piece(s)", Time(20, () => draped = fit.Drape(gore.Mesh)));
            Console.WriteLine($"    {draped.TriangleCount} triangle(s) of paint left");
            var island = MarkingAreas.Island(new List<Vec3> { new Vec3(-4f, 0f, 0f), new Vec3(4f, 0f, 0f), new Vec3(0f, 0f, 40f) }, 0.15f, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White);
            AreaFit ground = AreaFit.FromGround((x, z) => 0.02f * x + 0.03f * z, -4f, 4f, 0f, 40f);
            Line($"an island on the ground: its paint ({island.Mesh.TriangleCount} triangles) on {ground.PieceCount} cell(s)", Time(20, () => ground.Drape(island.Mesh)));

            // 0.12.5: a sign's posts on the ground: the profile from the surfaces (a road, its footway, a verge band) and the terrain, the feet moved, the support copied into a tile.
            var road = new List<SurfaceStrip>();
            foreach ((float x0, float x1, float y) in new[] { (-3f, 0.5f, 0.2f), (0.5f, 11f, 0f), (11f, 14f, 0.2f) })
            {
                var strip = new SurfaceStrip();
                for (int i = 0; i <= 8; i++)
                {
                    float z = -20f + 5f * i;
                    strip.Add(new Vec3(x0, y + 0.03f * z, z), new Vec3(x1, y + 0.03f * z, z));
                }

                road.Add(strip);
            }

            GroundProfile profile = null;
            Line("a sign on posts: the ground profile from 3 bands and the terrain (25 x 3 places)", Time(50, () =>
            {
                AreaFit surfaces = AreaFit.FromStrips(road, Vec3.Zero, 0f, -3f, 12f, -GroundProfile.RowZ, GroundProfile.RowZ, 1f);
                var seen = new HashSet<int>();
                var found = new List<int>();
                profile = GroundProfile.Sample((x, z) => PostGround.GroundOf(surfaces.HeightAt(x, z, out float y, out _, seen, found), y, false, -0.8f - 0.2f * x), -3f, 12f);
            }));
            var board = new SignDef { Id = "bench.board", Code = "b", Zh = "b", En = "b", Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = 4f, Height = 2f, BottomClearance = SupportCatalog.RoadsideClearance, TextureWidth = 400, TextureHeight = 200, Keywords = new string[0] };
            SignParts twin = SignBuilder.Build(new SignInstance { Def = board, Key = board.Id, Spec = "", TexW = 400, TexH = 200 }, SupportCatalog.ById(SupportCatalog.DoublePole), 0f);
            twin.FootTags(out int[] metalTags, out _, out _);
            var tile = new MeshBuilder();
            Line($"a sign on posts: its feet moved and its metal ({twin.Metal.VertexCount} vertices) copied into a tile", Time(200, () =>
            {
                PostGround.Offsets(twin, profile, out float[] top, out float[] bottom);
                tile.Positions.Clear();
                tile.Normals.Clear();
                tile.U.Clear();
                tile.V.Clear();
                tile.Indices.Clear();
                PostGround.Append(tile, twin.Metal, metalTags, top, bottom, Vec3.Zero, 0.3f);
            }));
            Line("the same copy without the ground (before 0.12.5)", Time(200, () =>
            {
                tile.Positions.Clear();
                tile.Normals.Clear();
                tile.U.Clear();
                tile.V.Clear();
                tile.Indices.Clear();
                tile.Append(twin.Metal, Vec3.Zero, 0.3f);
            }));
        }
    }
}
