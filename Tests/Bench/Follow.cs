using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What it takes for the items of a road to follow it when it is moved (BENCH_FOLLOW=1; 0.12.74, the owner: "用MoveIt移动道路之后应该能够自动跟随道路变化"): the accuracy of the path text a line
    /// is kept in and what repeated follows do to it, the cost of making a kilometre of wall again by the smooth map and by the pure parts of a rebuild, and the sag a longer road gives a line of
    /// the same number of chords. Pure parts only: the chain read, the surface heights and the writes are the game's and are not here.
    /// </summary>
    internal static class Follow
    {
        private static double Time(int repeats, Action action)
        {
            action();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++)
                action();
            return clock.Elapsed.TotalMilliseconds / repeats;
        }

        private static Bezier3 Moved(Bezier3 c, float dx, float dz)
        {
            var d = new Vec3(dx, 0f, dz);
            return new Bezier3(c.A + d, c.B + d, c.C + d, c.D + d);
        }

        /// <summary>The line of a wall at a lateral from a road: the points as the chain's Line makes them, a chord of about 2 m.</summary>
        private static List<Vec3> Line(Bezier3 road, float lateral, float from, float to, float chord)
        {
            var arc = new ArcTable(road);
            var points = new List<Vec3>();
            int n = Math.Max(1, (int)Math.Round((to - from) / chord));
            for (int i = 0; i <= n; i++)
            {
                float t = arc.TAt(from + (to - from) * i / n);
                points.Add(road.Point(t) + road.Tangent(t).RightXZ() * lateral);
            }

            return points;
        }

        public static void Run()
        {
            var random = new Random(11);
            Console.WriteLine("1. The path text: a line of 24 chords of 2.5 m, the points of Encode then Decode against the points (millimetres)");
            double worstAll = 0.0, sum = 0.0;
            int lines = 300;
            for (int k = 0; k < lines; k++)
            {
                float bow = (float)(random.NextDouble() * 40.0 - 20.0), lateral = (float)(random.NextDouble() * 12.0 - 6.0);
                Bezier3 road = new Bezier3(Vec3.Zero, new Vec3(bow, 1f, 20f), new Vec3(bow, 2f, 40f), new Vec3(0f, 3f, 60f));
                List<Vec3> line = Line(road, lateral, 2f, 62f > new ArcTable(road).Length ? new ArcTable(road).Length - 2f : 62f, 2.5f);
                string path = RunPath.Encode(line, out float yaw);
                List<Vec3> back = MarkingPolygon.ToWorld(RunPath.Decode(path), line[0], yaw);
                double worst = 0.0;
                for (int i = 0; i < Math.Min(line.Count, back.Count); i++)
                    worst = Math.Max(worst, (back[i] - line[i]).Length);
                worstAll = Math.Max(worstAll, worst);
                sum += worst;
            }

            Console.WriteLine($"   worst point of a line: mean {sum / lines * 1000.0:0.0}, worst of {lines} lines {worstAll * 1000.0:0.0} mm (the length of each chord is rounded to a centimetre and that is not corrected: it adds up along the path)");
            string again = RunPath.Encode(MarkingPolygon.ToWorld(RunPath.Decode(RunPath.Encode(Line(new Bezier3(Vec3.Zero, new Vec3(8f, 1f, 20f), new Vec3(8f, 2f, 40f), new Vec3(0f, 3f, 60f)), 3f, 2f, 55f, 2.5f), out float y0)), Vec3.Zero, y0), out _);
            string first = RunPath.Encode(Line(new Bezier3(Vec3.Zero, new Vec3(8f, 1f, 20f), new Vec3(8f, 2f, 40f), new Vec3(0f, 3f, 60f)), 3f, 2f, 55f, 2.5f), out float y1);
            Console.WriteLine($"   Encode(Decode(path)) is the same text: {(again.Length > 0 && again.Substring(0, Math.Min(again.Length, 20)) == first.Substring(0, Math.Min(first.Length, 20)))}");

            Console.WriteLine("2. A road dragged in steps (a Move It drag is many updates): the line written again each time from the text of the one before, against mapped from the first measure each time; millimetres from the exact place");
            Console.WriteLine($"   {"steps",6} {"from the text",14} {"from the first measure",24}");
            Bezier3 start = new Bezier3(Vec3.Zero, new Vec3(8f, 1f, 20f), new Vec3(8f, 2f, 40f), new Vec3(0f, 3f, 60f));
            List<Vec3> original = Line(start, 3f, 2f, 55f, 2.5f);
            foreach (int steps in new[] { 1, 5, 20, 60 })
            {
                string path = RunPath.Encode(original, out float yaw);
                Vec3 position = original[0];
                float heading = yaw;
                Bezier3 previous = start;
                double iterated = 0.0, direct = 0.0;
                var steppedPath = path;
                for (int s = 1; s <= steps; s++)
                {
                    Bezier3 now = Moved(start, 40f * s / steps, -25f * s / steps);
                    if (PathFollow.TryRemap(steppedPath, position, heading, new ArcTable(previous), new ArcTable(now), FollowMode.Fraction, out string text, out Vec3 at, out float turned))
                    {
                        steppedPath = text;
                        position = at;
                        heading = turned;
                    }

                    previous = now;
                }

                List<Vec3> final = MarkingPolygon.ToWorld(RunPath.Decode(steppedPath), position, heading);
                var exactDelta = new Vec3(40f, 0f, -25f);
                for (int i = 0; i < Math.Min(final.Count, original.Count); i++)
                    iterated = Math.Max(iterated, (final[i] - (original[i] + exactDelta)).Length);
                var mapped = new List<Vec3>();
                RoadRelation.TryMapPoints(new ArcTable(start), new ArcTable(Moved(start, 40f, -25f)), FollowMode.Fraction, original, mapped);
                string oneStep = RunPath.Encode(mapped, out float oneYaw);
                List<Vec3> once = MarkingPolygon.ToWorld(RunPath.Decode(oneStep), mapped[0], oneYaw);
                for (int i = 0; i < Math.Min(once.Count, original.Count); i++)
                    direct = Math.Max(direct, (once[i] - (original[i] + exactDelta)).Length);
                Console.WriteLine($"   {steps,6} {iterated * 1000.0,14:0.0} {direct * 1000.0,24:0.0}");
            }

            Console.WriteLine("3. A kilometre of wall made again, milliseconds (pure parts only; the chain read, the heights and the writes are the game's)");
            Bezier3[] edges = new Bezier3[17];
            Vec3 at0 = Vec3.Zero;
            for (int i = 0; i < edges.Length; i++)
            {
                Vec3 end = at0 + new Vec3((float)Math.Sin(i * 0.6) * 10f, 0.2f, 60f);
                edges[i] = new Bezier3(at0, at0 + new Vec3(0f, 0f, 20f), end - new Vec3(0f, 0f, 20f), end);
                at0 = end;
            }

            var pairs = new List<RoadRelation.RoadPair>();
            var movedPairs = new List<RoadRelation.RoadPair>();
            var wall = new List<Vec3>();
            for (int i = 0; i < edges.Length; i++)
            {
                pairs.Add(new RoadRelation.RoadPair { Old = new ArcTable(edges[i]), Now = new ArcTable(Moved(edges[i], 25f, 9f)) });
                wall.AddRange(Line(edges[i], 9f, 1f, pairs[i].Old.Length - 1f, 2f));
            }

            List<List<Vec3>> runs = null;
            double split = Time(20, () => runs = RunPath.Split(wall, 64f));
            var texts = new List<string>();
            var yaws = new List<float>();
            double encode = Time(20, () =>
            {
                texts.Clear();
                yaws.Clear();
                foreach (List<Vec3> run in runs)
                {
                    texts.Add(RunPath.Encode(run, out float y));
                    yaws.Add(y);
                }
            });
            double measure = 0.0, remap = 0.0;
            int items = runs.Count;
            remap = Time(10, () =>
            {
                for (int i = 0; i < runs.Count; i++)
                    PathFollow.TryRemap(texts[i], runs[i][0], yaws[i], pairs, FollowMode.Fraction, out _, out _, out _);
            });
            // An item runs along two or three of the edges, not all 17: the roads it is mapped by are those it overlaps.
            double near = Time(10, () =>
            {
                for (int i = 0; i < runs.Count; i++)
                {
                    int from = Math.Max(0, Math.Min(pairs.Count - 3, i - 1));
                    PathFollow.TryRemap(texts[i], runs[i][0], yaws[i], pairs.GetRange(from, 3), FollowMode.Fraction, out _, out _, out _);
                }
            });
            Console.WriteLine($"   follow by the smooth map with the 3 roads an item overlaps: all the items {near:0.000} ms, one item {near / items:0.000} ms");
            var one = new List<Vec3>();
            measure = Time(10, () =>
            {
                foreach (List<Vec3> run in runs)
                    RoadRelation.TryMapPoints(pairs, FollowMode.Fraction, run, one);
            });
            double arcs = Time(20, () =>
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    var a = new ArcTable(edges[i]);
                    if (a.Length < 0f)
                        throw new InvalidOperationException();
                }
            });
            Console.WriteLine($"   {wall.Count} points, {items} items of at most 64 m, {edges.Length} edges");
            Console.WriteLine($"   cut into items (RunPath.Split) {split:0.000}; write each as a path (RunPath.Encode) {encode:0.000}");
            Console.WriteLine($"   follow by the smooth map: all the items {remap:0.000} ms (the map of the points alone {measure:0.000}); one item {remap / items:0.000} ms; the arc tables of the 17 edges {arcs:0.000} ms");
            Console.WriteLine($"   a rebuild adds, in the game: the chain read from the edges (ChainReader, ECS), the heights window by window (SurfaceHeights over the net search tree and the surface cache), and the writes");
            var chain = ChainOfEdges(edges);
            double lineMs = Time(5, () => chain.Line(2f, chain.Length - 2f, 9f, 0f));
            var profile = new EdgeProfile(chain, 1);
            double edgeProfile = Time(5, () => chain.Line(2f, chain.Length - 2f, s => profile.At(s) + 0.5f, 0f));
            double profileMs = Time(20, () => new EdgeProfile(chain, 1));
            Console.WriteLine($"   the pure part of a rebuild: the line along the chain at a lateral {lineMs:0.000} ms, by the road's edge {edgeProfile:0.000} ms (the profile itself {profileMs:0.000} ms)");

            Console.WriteLine("4. A road made longer takes a line of the same number of chords with it: the chord grows, and so does the sag of a bend (millimetres, chord c, radius R: c^2 / (8 R))");
            Console.WriteLine($"   {"R (m)",8} {"length x1",10} {"x1.5",8} {"x2",8}   (chord 2.5 m before)");
            foreach (float radius in new[] { 30f, 50f, 100f, 200f })
                Console.WriteLine($"   {radius,8:0} {2.5f * 2.5f / (8f * radius) * 1000f,10:0.0} {3.75f * 3.75f / (8f * radius) * 1000f,8:0.0} {5f * 5f / (8f * radius) * 1000f,8:0.0}");
        }

        private static RoadChain ChainOfEdges(Bezier3[] edges)
        {
            float[] with = { 1.75f, 5.25f }, against = { -1.75f, -5.25f };
            var pieces = new List<ChainPiece>();
            for (int i = 0; i < edges.Length; i++)
            {
                var piece = new ChainPiece { Curve = edges[i], Kmh = 80f, HasSpeed = true, HalfWidth = 9f, After = i == edges.Length - 1 ? ChainJoint.End : ChainJoint.Pass };
                piece.Forward.AddRange(with);
                piece.Backward.AddRange(against);
                pieces.Add(piece);
            }

            return new RoadChain(pieces);
        }
    }
}
