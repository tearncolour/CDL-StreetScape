using System;
using System.Collections.Generic;
using CDLC.TS.FollowReference;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    internal static partial class FollowPerf
    {
        /// <summary>The same scenarios as the baseline, with the new code: one table per curve version, a batch of arrays, the rigid path, lines measured once.</summary>
        static partial void RunNewPart()
        {
            Console.WriteLine();
            Console.WriteLine("after: FollowCurve / FollowBatch / RigidMotion / LineFollow (evals = steps of the nearest-point search + evaluations of the curve)");
            Bezier3 road = Edge(3), now = Dragged(road, 45, 90);
            var oldTable = new FollowCurve(road);
            var nowTable = new FollowCurve(now);
            var items = Items(road, 1000, 5);
            var relations = new RoadRelation[items.Length];
            for (int i = 0; i < items.Length; i++)
                oldTable.TryMeasure(items[i].position, items[i].yaw, -1f, out relations[i], out _);

            Console.WriteLine("1. one item, one edge");
            int k = 0;
            long steps = 0;
            Row("measure (nearest point + relation), table given", Measure(() => oldTable.TryMeasure(items[k++ % 1000].position, 0.3f, -1f, out _, out _)));
            for (int i = 0; i < 1000; i++)
            {
                oldTable.ClosestT(items[i].position, -1f, out int n);
                steps += n;
            }

            Console.WriteLine($"  nearest point: {steps / 1000.0:0.00} Newton steps an item (each is one evaluation of the curve and its two derivatives), plus 1 for the point and heading: {steps / 1000.0 + 1:0.0} evaluations");
            float t0 = 0.5f;
            Row("measure with a hint (the point before in a run)", Measure(() =>
            {
                oldTable.TryMeasure(items[k++ % 1000].position, 0.3f, t0, out _, out _);
            }));
            Row("apply to a new curve, table given", Measure(() => nowTable.TryApply(relations[k++ % 1000], FollowMode.Fraction, out _, out _)));
            Row("per edge: a table (64 steps, the arc lengths and the points)", Measure(() => new FollowCurve(now)));
            var cache = new FollowCache();
            cache.Get(now);
            Row("per edge: the table from the cache (fingerprint, hit)", Measure(() => cache.Get(now)));
            Row("per edge: the nearest point search alone", Measure(() => oldTable.ClosestT(items[k++ % 1000].position, -1f, out _)));
            Row("per edge: detect a rigid motion", Measure(() => RigidMotion.TryDetect(road, now, RigidMotion.Tolerance, out _)));

            Console.WriteLine("2. a line item of 32 chords of 2 m, along one road");
            var line = new List<Vec3>();
            for (int i = 0; i <= 32; i++)
            {
                float t = oldTable.TAt(oldTable.Length * (0.02f + 0.9f * i / 32f));
                line.Add(road.Point(t) + road.Tangent(t).RightXZ() * 6f);
            }

            string path = RunPath.Encode(line, out float lineYaw);
            var work = new LineWorkspace();
            var tables = new[] { oldTable };
            Row("measure once (when linked): the 33 points' relations", Measure(() => LineFollow.TryMeasure(path, line[0], lineYaw, tables, 1, work, out _)), 33);
            LineFollow.TryMeasure(path, line[0], lineYaw, tables, 1, work, out LineRelation lineRelation);
            var nowTables = new[] { nowTable };
            string text = null;
            Row("follow a frame: bent road, text made again (map + Encode)", Measure(() =>
            {
                // The relation as it was at the baseline each frame, so that the same work is done each time.
                lineRelation.Path = path;
                lineRelation.Position = line[0];
                lineRelation.Yaw = lineYaw;
                lineRelation.WrittenCount = 0;
                LineFollow.Apply(lineRelation, nowTables, FollowMode.Fraction, 0f, work, out text, out _, out _, out _);
            }));
            List<Vec3> decoded = MarkingPolygon.ToWorld(RunPath.Decode(path), line[0], lineYaw);
            var moved = new[] { new FollowCurve(CDLC.TS.Tests.FollowFixture.Moved(road)) };
            LineFollow.TryMeasure(path, line[0], lineYaw, tables, 1, work, out lineRelation);
            LineFollow.Apply(lineRelation, moved, FollowMode.Fraction, 0f, work, out _, out _, out _, out _);
            LineFollow.TryMeasure(path, line[0], lineYaw, tables, 1, work, out lineRelation);
            Row("follow a frame: road moved and turned, only the pose (the text is kept)", Measure(() =>
            {
                lineRelation.Position = line[0];
                lineRelation.Yaw = lineYaw;
                for (int j = 0; j < lineRelation.WrittenCount; j++)
                    lineRelation.Written[j] = decoded[j];
                LineFollow.Apply(lineRelation, moved, FollowMode.Fraction, 0f, work, out _, out _, out _, out _);
            }));
            LineFollow.TryMeasure(path, line[0], lineYaw, tables, 1, work, out lineRelation);
            Row("follow a frame: road as it was (nothing to write)", Measure(() => LineFollow.Apply(lineRelation, tables, FollowMode.Fraction, 0f, work, out _, out _, out _, out _)));
            GC.KeepAlive(text);

            Console.WriteLine("3. a batch of items on ONE edge, one frame of a drag (the table of the new curve + the batch run)");
            foreach (bool rigid in new[] { false, true })
            {
                foreach (int n in new[] { 1, 10, 100, 1000 })
                {
                    var batch = new FollowBatch(1000);
                    int edge = batch.AddEdge(oldTable);
                    for (int i = 0; i < n; i++)
                        batch.Add(edge, relations[i], items[i].position, items[i].yaw, items[i].position, items[i].yaw);
                    Bezier3 target = rigid ? CDLC.TS.Tests.FollowFixture.Moved(road) : now;
                    int count = n;
                    Row($"{count} item(s), {(rigid ? "rigid motion" : "bent curve")}", Measure(() =>
                    {
                        batch.SetNow(edge, new FollowCurve(target), true);
                        batch.Run(FollowMode.Fraction, 0, count);
                    }), count);
                }
            }

            Console.WriteLine("4. a batch on 17 ADJACENT edges, one frame (17 tables + the batch run)");
            var edges = new Bezier3[17];
            var movedEdges = new Bezier3[17];
            var rigidEdges = new Bezier3[17];
            var bigBatch = new FollowBatch(2000);
            var rigidBatch = new FollowBatch(2000);
            for (int e = 0; e < 17; e++)
            {
                edges[e] = Edge(e);
                movedEdges[e] = Dragged(edges[e], 45, 90);
                rigidEdges[e] = CDLC.TS.Tests.FollowFixture.Moved(edges[e]);
                var table = new FollowCurve(edges[e]);
                int a = bigBatch.AddEdge(table), b = rigidBatch.AddEdge(table);
                var placed = Items(edges[e], 60, 100 + e);
                for (int i = 0; i < 60; i++)
                {
                    table.TryMeasure(placed[i].position, placed[i].yaw, -1f, out RoadRelation r, out _);
                    bigBatch.Add(a, r, placed[i].position, placed[i].yaw, placed[i].position, placed[i].yaw);
                    rigidBatch.Add(b, r, placed[i].position, placed[i].yaw, placed[i].position, placed[i].yaw);
                }
            }

            foreach (int n in new[] { 17, 170, 1020 })
            {
                int per = n / 17;
                // The run takes the first n items; items are stored edge by edge in 60s, so a slice of n takes whole edges: for the comparison the same number of items spread over the edges is taken as the slice of each.
                var slice = new FollowBatch(2000);
                var sliceRigid = new FollowBatch(2000);
                for (int e = 0; e < 17; e++)
                {
                    var table = new FollowCurve(edges[e]);
                    int a = slice.AddEdge(table), b = sliceRigid.AddEdge(table);
                    var placed = Items(edges[e], 60, 100 + e);
                    for (int i = 0; i < per; i++)
                    {
                        table.TryMeasure(placed[i].position, placed[i].yaw, -1f, out RoadRelation r, out _);
                        slice.Add(a, r, placed[i].position, placed[i].yaw, placed[i].position, placed[i].yaw);
                        sliceRigid.Add(b, r, placed[i].position, placed[i].yaw, placed[i].position, placed[i].yaw);
                    }
                }

                int total = n;
                Row($"{n} item(s) on 17 edges, bent", Measure(() =>
                {
                    for (int e = 0; e < 17; e++)
                        slice.SetNow(e, new FollowCurve(movedEdges[e]), true);
                    slice.Run(FollowMode.Fraction, 0, total);
                }), n);
                Row($"{n} item(s) on 17 edges, all rigid", Measure(() =>
                {
                    for (int e = 0; e < 17; e++)
                        sliceRigid.SetNow(e, new FollowCurve(rigidEdges[e]), true);
                    sliceRigid.Run(FollowMode.Fraction, 0, total);
                }), n);
            }

            GC.KeepAlive(bigBatch);
            GC.KeepAlive(rigidBatch);

            Console.WriteLine("5. lines: 17 items of 32 chords, one per edge (the relations measured once when linked)");
            var lineRelations = new LineRelation[17];
            var lineTables = new FollowCurve[17][];
            var lineNow = new FollowCurve[17][];
            for (int e = 0; e < 17; e++)
            {
                var table = new FollowCurve(edges[e]);
                var pts = new List<Vec3>();
                for (int i = 0; i <= 32; i++)
                {
                    float t = table.TAt(table.Length * (0.02f + 0.9f * i / 32f));
                    pts.Add(edges[e].Point(t) + edges[e].Tangent(t).RightXZ() * 6f);
                }

                string text2 = RunPath.Encode(pts, out float yaw2);
                lineTables[e] = new[] { table };
                lineNow[e] = new[] { new FollowCurve(movedEdges[e]) };
                LineFollow.TryMeasure(text2, pts[0], yaw2, lineTables[e], 1, work, out lineRelations[e]);
            }

            var texts = new string[17];
            var positions = new Vec3[17];
            var yaws = new float[17];
            for (int e = 0; e < 17; e++)
            {
                texts[e] = lineRelations[e].Path;
                positions[e] = lineRelations[e].Position;
                yaws[e] = lineRelations[e].Yaw;
            }

            Row("17 line items, a node drag frame (tables of the new curves given, text made again)", Measure(() =>
            {
                for (int e = 0; e < 17; e++)
                {
                    lineRelations[e].Path = texts[e];
                    lineRelations[e].Position = positions[e];
                    lineRelations[e].Yaw = yaws[e];
                    lineRelations[e].WrittenCount = 0;
                    LineFollow.Apply(lineRelations[e], lineNow[e], FollowMode.Fraction, 0f, work, out _, out _, out _, out _);
                }
            }), 17);
            Console.WriteLine("6. the nearest point search against the reference: steps and agreement on 20000 places");
            var random = new Random(9);
            long newSteps = 0, hintSteps = 0, refEvals = 0;
            double worst = 0.0;
            for (int i = 0; i < 20000; i++)
            {
                Bezier3 c = CDLC.TS.Tests.FollowFixture.RandomCurve(random, 100f, 0.1f, 60f);
                var table = new FollowCurve(c);
                Vec3 p = CDLC.TS.Tests.FollowFixture.RandomPlace(random, c);
                float t = table.ClosestT(p, -1f, out int n);
                table.ClosestT(p, t + 0.01f, out int m);
                newSteps += n;
                hintSteps += m;
                long before = Ref.Evals;
                float tr = Ref.ClosestT(c, p);
                refEvals += Ref.Evals - before;
                worst = Math.Max(worst, Math.Abs(Math.Sqrt((c.Point(t).X - p.X) * (c.Point(t).X - p.X) + (c.Point(t).Z - p.Z) * (c.Point(t).Z - p.Z)) - Math.Sqrt((c.Point(tr).X - p.X) * (c.Point(tr).X - p.X) + (c.Point(tr).Z - p.Z) * (c.Point(tr).Z - p.Z))));
            }

            Console.WriteLine($"  reference: {refEvals / 20000.0:0.0} evaluations of the curve a search; new: {newSteps / 20000.0:0.00} steps (a hint 1 % of the curve away: {hintSteps / 20000.0:0.00}); the distance differs from the reference's by at most {worst * 1000.0:0.0000} mm");
        }
    }
}
