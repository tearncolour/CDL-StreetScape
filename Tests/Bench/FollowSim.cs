using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.FollowReference;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using CDLC.TS.Tests;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// A node dragged for 90 frames at 60 fps and 10 m/s, and what the owner sees of the items on its roads under each way of following (BENCH_FOLLOWSIM=1; 0.12.74 follow work). Deterministic: the costs of each
    /// step are the measured ns of Calibrate (the pure parts), the writes and the tile builds are modelled (the write's cost is an assumption, the build's duration a parameter: neither is measured offline), and the
    /// frames are a fake clock. The lag is the distance (and the frames) between where the road puts an item at frame f and where the item is: as written (the entity) and as shown (the renderer builds a tile on a
    /// worker and drops the result when an item of the tile was written meanwhile, as ChunkSystem.CollectJobs does, so a tile whose items are written every frame is not shown anew until the writes stop).
    /// </summary>
    internal static class FollowSim
    {
        internal enum Policy { A, B, C }

        internal sealed class Costs
        {
            public double OldEdge, OldItem, OldLine, NewEdge, NewItemBent, NewItemRigid, NewLineBent, NewLinePose, Upsert;

            /// <summary>The write of one item (SetComponentData and what ChunkSystem does with the changed item): not measured offline, an assumption.</summary>
            public double Write = 2000.0;

            /// <summary>The main thread's read of one changed edge's Curve in policy C (the table is made on the worker): not measured offline, an assumption.</summary>
            public double Read = 100.0;
        }

        internal sealed class Options
        {
            public Policy Policy;
            public int Gate;
            public double BudgetNs = 300000.0;
            public double ApplyNs = 150000.0;
            public int Cap = 200;
            public int BuildFrames = 6;
            public int PlanFrames = 6;
            public int MaxJobs = 3;
            public int Tiles = 2;

            /// <summary>The renderer shows a finished build even when an item of the tile was written meanwhile (it is newer than what is shown) and builds the tile again: not what ChunkSystem does today (it drops the result), measured to say what the change would buy.</summary>
            public bool AcceptStale;

            public string Label = "";
        }

        internal sealed class World
        {
            public int Items, Edges, Lines, Tiles;
            public bool Rigid;
            public Bezier3[] Base;
            public Bezier3[][] Curves;
            public RoadRelation[] Relation;
            public int[] EdgeOf, TileOf;
            public Vec3[][] Position;
            public Vec3[] Start;
        }

        internal const int DragFrames = 90;

        internal static Costs Calibrate()
        {
            var c = new Costs();
            Bezier3 road = FollowPerf.Edge(3), now = FollowPerf.Dragged(road, 45, 90), moved = FollowFixture.Moved(road);
            var items = FollowPerf.Items(road, 1000, 5);
            var oldArc = new RefArc(road);
            var oldRel = new RefRelation[1000];
            for (int i = 0; i < 1000; i++)
                RefRelation.TryMeasure(oldArc, items[i].position, items[i].yaw, out oldRel[i]);
            var nowArc = new RefArc(now);
            int k = 0;
            c.OldEdge = FollowPerf.Measure(() => new RefArc(now)).BestNs;
            c.OldItem = FollowPerf.Measure(() => oldRel[k++ % 1000].TryApply(nowArc, FollowMode.Fraction, out _, out _)).BestNs;
            var table = new FollowCurve(road);
            var batch = new FollowBatch(1000);
            int edge = batch.AddEdge(table);
            for (int i = 0; i < 1000; i++)
            {
                table.TryMeasure(items[i].position, items[i].yaw, -1f, out RoadRelation r, out _);
                batch.Add(edge, r, items[i].position, items[i].yaw, items[i].position, items[i].yaw);
            }

            batch.SetNow(edge, new FollowCurve(now), true);
            double bent1000 = FollowPerf.Measure(() => batch.Run(FollowMode.Fraction, 0, 1000)).BestNs;
            c.NewItemBent = bent1000 / 1000.0;
            batch.SetNow(edge, new FollowCurve(moved), true);
            c.NewItemRigid = FollowPerf.Measure(() => batch.Run(FollowMode.Fraction, 0, 1000)).BestNs / 1000.0;
            c.NewEdge = FollowPerf.Measure(() => batch.SetNow(edge, new FollowCurve(now), true)).BestNs;

            var line = new List<Vec3>();
            for (int i = 0; i <= 32; i++)
            {
                float t = table.TAt(table.Length * (0.02f + 0.9f * i / 32f));
                table.At(t, out Vec3 on, out Vec3 h);
                line.Add(on + new Vec3(h.Z, 0f, -h.X) * 6f);
            }

            string path = RunPath.Encode(line, out float yaw);
            var pair = new[] { new RefRelation.Pair { Old = oldArc, Now = nowArc } };
            c.OldLine = FollowPerf.Measure(() => RefPathFollow.TryRemap(path, line[0], yaw, pair, FollowMode.Fraction, out _, out _, out _)).BestNs;
            var work = new LineWorkspace();
            var tables = new[] { table };
            LineFollow.TryMeasure(path, line[0], yaw, tables, 1, work, out LineRelation relation);
            var nowTables = new[] { new FollowCurve(now) };
            c.NewLineBent = FollowPerf.Measure(() =>
            {
                relation.Path = path;
                relation.Position = line[0];
                relation.Yaw = yaw;
                relation.WrittenCount = 0;
                LineFollow.Apply(relation, nowTables, FollowMode.Fraction, 0f, work, out _, out _, out _, out _);
            }).BestNs;
            var movedTables = new[] { new FollowCurve(moved) };
            List<Vec3> decoded = MarkingPolygon.ToWorld(RunPath.Decode(path), line[0], yaw);
            c.NewLinePose = FollowPerf.Measure(() =>
            {
                relation.Position = line[0];
                relation.Yaw = yaw;
                for (int j = 0; j < relation.WrittenCount; j++)
                    relation.Written[j] = decoded[j];
                LineFollow.Apply(relation, movedTables, FollowMode.Fraction, 0f, work, out _, out _, out _, out _);
            }).BestNs;

            var store = new ItemStore<int>();
            uint type = new List<SignDef>(SignCatalog.All)[0].Hash;
            for (int i = 0; i < 1000; i++)
                store.Upsert(i, new ItemRecord(type, new Vec3(i, 0f, 0f), 0f));
            int n = 0;
            c.Upsert = FollowPerf.Measure(() =>
            {
                n++;
                store.Upsert(n % 1000, new ItemRecord(type, new Vec3(n % 1000, 0f, n * 0.01f), 0f));
            }).BestNs;
            return c;
        }

        internal static World Build(int items, int edges, int tiles, bool rigid, int lines)
        {
            var w = new World { Items = items, Edges = edges, Tiles = tiles, Rigid = rigid, Lines = lines };
            w.Base = new Bezier3[edges];
            for (int e = 0; e < edges; e++)
                w.Base[e] = FollowPerf.Edge(e);
            w.Curves = new Bezier3[DragFrames + 1][];
            for (int v = 0; v <= DragFrames; v++)
            {
                var delta = new Vec3(0.8f, 0.02f, -0.6f) * (10f / 60f * v);
                w.Curves[v] = new Bezier3[edges];
                for (int e = 0; e < edges; e++)
                    w.Curves[v][e] = rigid ? FollowFixture.Rigid(w.Base[e], delta, 0f) : FollowFixture.Neighbour(w.Base[e], delta);
            }

            w.Relation = new RoadRelation[items];
            w.EdgeOf = new int[items];
            w.TileOf = new int[items];
            w.Start = new Vec3[items];
            var baseTables = new FollowCurve[edges];
            for (int e = 0; e < edges; e++)
                baseTables[e] = new FollowCurve(w.Base[e]);
            int singles = items - lines;
            for (int i = 0; i < items; i++)
            {
                bool isLine = i < lines;
                int e = isLine ? i % edges : Math.Min(edges - 1, (i - lines) * edges / Math.Max(1, singles));
                w.EdgeOf[i] = e;
                w.TileOf[i] = Math.Min(tiles - 1, (isLine ? e * tiles / edges : (i - lines) * tiles / Math.Max(1, singles)));
                Vec3 place;
                float yaw = 0.3f;
                if (isLine)
                {
                    FollowCurve t = baseTables[e];
                    t.At(t.TAt(t.Length * 0.5f), out Vec3 on, out Vec3 h);
                    place = on + new Vec3(h.Z, 0f, -h.X) * 6f;
                }
                else
                {
                    var random = new Random(1000 + i);
                    Bezier3 c = w.Base[e];
                    float t = (float)random.NextDouble();
                    place = c.Point(t) + c.Tangent(t).RightXZ() * (float)(random.NextDouble() * 16.0 - 8.0) + new Vec3(0f, (float)random.NextDouble() * 2f, 0f);
                    yaw = (float)(random.NextDouble() * 6.0 - 3.0);
                }

                baseTables[e].TryMeasure(place, yaw, -1f, out w.Relation[i], out _);
                w.Start[i] = place;
            }

            w.Position = new Vec3[DragFrames + 1][];
            for (int v = 0; v <= DragFrames; v++)
            {
                var tables = new FollowCurve[edges];
                for (int e = 0; e < edges; e++)
                    tables[e] = new FollowCurve(w.Curves[v][e]);
                w.Position[v] = new Vec3[items];
                for (int i = 0; i < items; i++)
                    tables[w.EdgeOf[i]].TryApply(w.Relation[i], FollowMode.Fraction, out w.Position[v][i], out _);
            }

            return w;
        }

        internal sealed class Result
        {
            public string Label;
            public double[] WriteFrames = new double[3], WriteMetres = new double[3], ShownFrames = new double[3], ShownMetres = new double[3];
            public double MainMean, MainWorst, WorkerMs, SchedMeanUs;
            public int Writes, BuildsStarted, BuildsAccepted, BuildsDiscarded, WriteConverge = -1, ShownConverge = -1;
            public int WriteBursts;
        }

        private static double[] Stats(List<float> values)
        {
            values.Sort();
            double sum = 0.0;
            foreach (float v in values)
                sum += v;
            return new[] { sum / Math.Max(1, values.Count), values.Count == 0 ? 0.0 : values[(int)Math.Min(values.Count - 1, values.Count * 0.95)], values.Count == 0 ? 0.0 : values[values.Count - 1] };
        }

        private sealed class Batch
        {
            public int DeliverAt;
            public List<FollowJob> Jobs = new List<FollowJob>();
        }

        private struct TileJob
        {
            public int Tile, Version, DoneAt;
            public int[] Shown;
        }

        internal static Result Simulate(World w, Options o, Costs c)
        {
            int n = w.Items, tiles = w.Tiles;
            var written = new int[n];
            var shown = new int[n];
            var tileVersion = new int[tiles];
            var tileStale = new bool[tiles];
            var tileJob = new TileJob[tiles];
            var tileBusy = new bool[tiles];
            var members = new List<int>[tiles];
            for (int t = 0; t < tiles; t++)
                members[t] = new List<int>();
            for (int i = 0; i < n; i++)
                members[w.TileOf[i]].Add(i);
            int lastPlan = -1000, writes = 0, started = 0, accepted = 0, discarded = 0;
            var result = new Result { Label = o.Label };
            double Cost(int i) => (i < w.Lines ? (w.Rigid ? c.NewLinePose : c.NewLineBent) : (w.Rigid ? c.NewItemRigid : c.NewItemBent)) + c.Write;
            double OldCost(int i) => (i < w.Lines ? c.OldLine : c.OldItem) + c.Write;

            var sched = new FollowScheduler { TileGateFrames = o.Gate };
            for (int i = 0; i < n; i++)
            {
                sched.Add(i, i < w.Lines ? 1 : 0, w.Start[i].X, w.Start[i].Z, (float)(o.Policy == Policy.A ? OldCost(i) : Cost(i)), w.TileOf[i]);
                sched.Link(i, w.EdgeOf[i]);
            }

            Vec3 camera = w.Start[Math.Min(n - 1, w.Lines + (n - w.Lines) / 2)];
            sched.SetFocus(camera.X, camera.Z, true, w.Curves[0][w.Edges - 1].D.X, w.Curves[0][w.Edges - 1].D.Z);

            // Policy A: the dirty roads, the queue, the timer.
            var dirtyEdge = new bool[w.Edges];
            var queue = new Queue<int>();
            var edgeDone = new bool[w.Edges];
            int lastWrite = -1000, lastEvent = -1000;
            // Policy C: the worker's batches.
            var inFlight = new Queue<Batch>();
            var jobs = new List<FollowJob>();

            var wLagF = new List<float>();
            var wLagM = new List<float>();
            var vLagF = new List<float>();
            var vLagM = new List<float>();
            var mainMs = new List<double>();
            double schedTicks = 0.0;
            int schedCalls = 0;
            double freq = Stopwatch.Frequency;
            double worstMain = 0.0, workerNs = 0.0;
            int frames = DragFrames + 600;
            for (int f = 0; f < frames; f++)
            {
                int version = Math.Min(f + 1, DragFrames);
                double mainNs = 0.0;

                // 1. the tile builds that are done
                for (int t = 0; t < tiles; t++)
                {
                    if (tileBusy[t] && tileJob[t].DoneAt <= f)
                    {
                        tileBusy[t] = false;
                        if (tileJob[t].Version == tileVersion[t] || o.AcceptStale)
                        {
                            accepted++;
                            for (int m = 0; m < members[t].Count; m++)
                                shown[members[t][m]] = tileJob[t].Shown[m];
                            tileStale[t] = tileJob[t].Version != tileVersion[t];
                        }
                        else
                        {
                            discarded++;
                            tileStale[t] = true;
                        }
                    }
                }

                void Write(int item, int v)
                {
                    written[item] = v;
                    writes++;
                    int t = w.TileOf[item];
                    tileVersion[t]++;
                    tileStale[t] = true;
                }

                // 2. the policy
                bool event_ = f < DragFrames;
                if (o.Policy == Policy.A)
                {
                    if (event_)
                    {
                        for (int e = 0; e < w.Edges; e++)
                            dirtyEdge[e] = true;
                        lastEvent = f;
                    }

                    bool any = false;
                    for (int e = 0; e < w.Edges && !any; e++)
                        any = dirtyEdge[e];
                    if (queue.Count == 0 && any && (f - lastWrite >= 15 || f - lastEvent >= 3))
                    {
                        for (int i = 0; i < n; i++)
                        {
                            if (dirtyEdge[w.EdgeOf[i]])
                                queue.Enqueue(i);
                        }

                        Array.Clear(dirtyEdge, 0, dirtyEdge.Length);
                        Array.Clear(edgeDone, 0, edgeDone.Length);
                        lastWrite = f;
                    }

                    int count = 0;
                    while (queue.Count > 0 && count < o.Cap)
                    {
                        int item = queue.Dequeue();
                        int e = w.EdgeOf[item];
                        if (!edgeDone[e])
                        {
                            edgeDone[e] = true;
                            mainNs += c.OldEdge;
                        }

                        mainNs += OldCost(item);
                        Write(item, version);
                        count++;
                    }
                }
                else if (o.Policy == Policy.B)
                {
                    long tick = Stopwatch.GetTimestamp();
                    if (event_)
                    {
                        for (int e = 0; e < w.Edges; e++)
                            sched.EdgeChanged(e, version, f);
                        mainNs += c.NewEdge * w.Edges;
                    }

                    jobs.Clear();
                    sched.Take(f, Math.Max(0.0, o.BudgetNs - mainNs), o.Cap, jobs);
                    foreach (FollowJob j in jobs)
                    {
                        mainNs += Cost(j.Item);
                        Write(j.Item, j.Version);
                    }

                    long mid = Stopwatch.GetTimestamp();
                    foreach (FollowJob j in jobs)
                        sched.Wrote(j.Item, j.Version);
                    double own = (Stopwatch.GetTimestamp() - mid + mid - tick) / freq * 1e9;
                    schedTicks += own;
                    schedCalls++;
                    mainNs += own;
                }
                else
                {
                    long tick = Stopwatch.GetTimestamp();
                    while (inFlight.Count > 0 && inFlight.Peek().DeliverAt <= f)
                    {
                        Batch done = inFlight.Dequeue();
                        foreach (FollowJob j in done.Jobs)
                            sched.Deliver(j.Item, j.Version, f);
                    }

                    jobs.Clear();
                    sched.ApplyReady(f, c.Write, o.ApplyNs, o.Cap, jobs);
                    foreach (FollowJob j in jobs)
                    {
                        mainNs += c.Write;
                        Write(j.Item, j.Version);
                    }

                    foreach (FollowJob j in jobs)
                        sched.Wrote(j.Item, j.Version);
                    if (event_)
                    {
                        for (int e = 0; e < w.Edges; e++)
                            sched.EdgeChanged(e, version, f);
                        mainNs += c.Read * w.Edges;
                        workerNs += c.NewEdge * w.Edges;
                    }

                    var batch = new Batch { DeliverAt = f + 1 };
                    sched.TakeAll(f, int.MaxValue, batch.Jobs);
                    foreach (FollowJob j in batch.Jobs)
                        workerNs += j.Item < w.Lines ? (w.Rigid ? c.NewLinePose : c.NewLineBent) : (w.Rigid ? c.NewItemRigid : c.NewItemBent);
                    if (batch.Jobs.Count > 0)
                        inFlight.Enqueue(batch);
                    double own = (Stopwatch.GetTimestamp() - tick) / freq * 1e9;
                    schedTicks += own;
                    schedCalls++;
                    mainNs += own;
                }

                // 3. the renderer plans and starts builds (ChunkSystem: a plan every PlanFrames at most, MaxJobs at a time, the stale tiles by index = distance)
                int busy = 0;
                for (int t = 0; t < tiles; t++)
                    busy += tileBusy[t] ? 1 : 0;
                bool anyStale = false;
                for (int t = 0; t < tiles; t++)
                    anyStale |= tileStale[t] && !tileBusy[t];
                if (anyStale && f - lastPlan >= o.PlanFrames)
                {
                    lastPlan = f;
                    for (int t = 0; t < tiles && busy < o.MaxJobs; t++)
                    {
                        if (!tileStale[t] || tileBusy[t])
                            continue;
                        var shot = new int[members[t].Count];
                        for (int m = 0; m < shot.Length; m++)
                            shot[m] = written[members[t][m]];
                        tileJob[t] = new TileJob { Tile = t, Version = tileVersion[t], DoneAt = f + o.BuildFrames, Shown = shot };
                        tileBusy[t] = true;
                        tileStale[t] = false;
                        busy++;
                        started++;
                    }
                }

                // 4. what is seen
                mainMs.Add(mainNs / 1e6);
                worstMain = Math.Max(worstMain, mainNs / 1e6);
                if (f < DragFrames)
                {
                    for (int i = 0; i < n; i++)
                    {
                        wLagF.Add(version - written[i]);
                        wLagM.Add((w.Position[version][i] - w.Position[written[i]][i]).Length);
                        vLagF.Add(version - shown[i]);
                        vLagM.Add((w.Position[version][i] - w.Position[shown[i]][i]).Length);
                    }
                }
                else
                {
                    bool allWritten = true, allShown = true;
                    for (int i = 0; i < n; i++)
                    {
                        allWritten &= written[i] == DragFrames;
                        allShown &= shown[i] == DragFrames;
                    }

                    if (allWritten && result.WriteConverge < 0)
                        result.WriteConverge = f - (DragFrames - 1);
                    if (allShown && result.ShownConverge < 0)
                        result.ShownConverge = f - (DragFrames - 1);
                    if (result.WriteConverge >= 0 && result.ShownConverge >= 0)
                        break;
                }
            }

            result.WriteFrames = Stats(wLagF);
            result.WriteMetres = Stats(wLagM);
            result.ShownFrames = Stats(vLagF);
            result.ShownMetres = Stats(vLagM);
            double sum = 0.0;
            for (int f = 0; f < DragFrames && f < mainMs.Count; f++)
                sum += mainMs[f];
            result.MainMean = sum / DragFrames;
            result.MainWorst = worstMain;
            result.Writes = writes;
            result.BuildsStarted = started;
            result.BuildsAccepted = accepted;
            result.BuildsDiscarded = discarded;
            result.WorkerMs = workerNs / 1e6;
            result.SchedMeanUs = schedCalls == 0 ? 0.0 : schedTicks / schedCalls / 1000.0;
            return result;
        }

        internal static string Format(Result r)
        {
            string conv(int v) => v < 0 ? ">600" : v.ToString();
            return $"  {r.Label,-14} write lag {r.WriteFrames[0],5:0.0}/{r.WriteFrames[1],4:0}/{r.WriteFrames[2],3:0} fr {r.WriteMetres[0],5:0.00}/{r.WriteMetres[1],5:0.00}/{r.WriteMetres[2],5:0.00} m" +
                   $" | shown lag {r.ShownFrames[0],5:0.0}/{r.ShownFrames[1],4:0}/{r.ShownFrames[2],3:0} fr {r.ShownMetres[0],5:0.00}/{r.ShownMetres[1],5:0.00}/{r.ShownMetres[2],5:0.00} m" +
                   $" | main {r.MainMean,6:0.000}/{r.MainWorst,6:0.000} ms | {r.Writes,5} writes | builds {r.BuildsStarted,3}/{r.BuildsAccepted,3} | conv {conv(r.WriteConverge),4}/{conv(r.ShownConverge),4}";
        }

        private static Options Opt(Policy p, string label, int gate = 0, int tiles = 2, int build = 6, bool stale = false) => new Options { Policy = p, Label = label, Gate = gate, Tiles = tiles, BuildFrames = build, AcceptStale = stale };

        public static void Run()
        {
            Console.WriteLine($"road follow drag simulation, {(Type.GetType("Mono.Runtime") != null ? "Mono" : ".NET " + Environment.Version)}; node dragged 90 frames at 60 fps and 10 m/s (0.167 m a frame, 15 m in all)");
            Costs c = Calibrate();
            Console.WriteLine($"measured ns: old arc table {c.OldEdge:0}, old item {c.OldItem:0}, old line {c.OldLine:0}; new table+detect {c.NewEdge:0}, new item bent {c.NewItemBent:0.0}, rigid {c.NewItemRigid:0.0}, new line bent {c.NewLineBent:0}, pose {c.NewLinePose:0}; ItemStore.Upsert {c.Upsert:0}");
            Console.WriteLine($"assumed ns (not measurable offline): write of one item {c.Write:0} (SetComponentData and what ChunkSystem does with it), read of one edge's Curve {c.Read:0}; tile build 6 frames (100 ms) on a worker, a plan every 6 frames, 3 jobs at a time, 2 tiles");
            Console.WriteLine("columns: lag = mean/p95/worst over every item and every frame of the drag; write = the entity as written, shown = as the renderer shows it; main = main-thread ms a frame, mean over the drag / worst frame; builds = started/accepted; conv = frames after the last event until every item is written / shown");
            foreach (int items in new[] { 10, 200, 1000 })
            {
                foreach (int edges in new[] { 1, 3, 17 })
                {
                    World w = Build(items, edges, 2, false, 0);
                    Console.WriteLine($"{items} items on {edges} adjacent edge(s), all bent (a node drag's neighbours), 2 tiles");
                    foreach (Options o in Rows())
                        Console.WriteLine(Format(Simulate(w, o, c)));
                }
            }

            Console.WriteLine("1000 items on 17 adjacent edges, all rigid (a whole road moved), 2 tiles");
            World rigid = Build(1000, 17, 2, true, 0);
            foreach (Options o in Rows())
                Console.WriteLine(Format(Simulate(rigid, o, c)));

            Console.WriteLine("a kilometre of wall: 17 line items of 32 chords (one group) and 200 single items, 17 bent edges, 2 tiles");
            World wall = Build(217, 17, 2, false, 17);
            foreach (Options o in Rows())
                Console.WriteLine(Format(Simulate(wall, o, c)));
            Tiles(c);
            WriteCost(c);
            Coverage();
            Resampling();
        }

        private static Options[] Rows() => new[]
        {
            Opt(Policy.A, "A 0.25 s"), Opt(Policy.B, "B 0.3 ms"), Opt(Policy.C, "C worker"),
            Opt(Policy.B, "B + gate 12", 12), Opt(Policy.B, "B + gate 16", 16), Opt(Policy.C, "C + gate 16", 16),
            Opt(Policy.B, "B + stale ok", 0, 2, 6, true), Opt(Policy.C, "C + stale ok", 0, 2, 6, true),
            new Options { Policy = Policy.B, Label = "B 0.5ms+gate14", Gate = 14, BudgetNs = 500000.0 }
        };

        private static void Tiles(Costs c)
        {
            Console.WriteLine("tile rebuilds for a drag of 200 items on 3 adjacent bent edges: the road crosses T tiles (policy B, 0.3 ms); started/accepted builds, frames until shown; the build takes D frames");
            Console.WriteLine($"  {"T",2} {"D",3} {"gate",5} {"builds started",15} {"accepted",9} {"discarded",10} {"shown lag p95 m",16} {"conv shown",11}");
            foreach (int tiles in new[] { 2, 4, 8 })
            {
                World w = Build(200, 3, tiles, false, 0);
                foreach (int build in new[] { 3, 6, 12 })
                {
                    foreach (int gate in new[] { 0, 6, 12, 16, 24 })
                    {
                        Result r = Simulate(w, Opt(Policy.B, "", gate, tiles, build), c);
                        Console.WriteLine($"  {tiles,2} {build,3} {(gate == 0 ? "none" : gate.ToString()),5} {r.BuildsStarted,15} {r.BuildsAccepted,9} {r.BuildsDiscarded,10} {r.ShownMetres[1],16:0.00} {(r.ShownConverge < 0 ? ">600" : r.ShownConverge.ToString()),11}");
                    }
                }
            }
        }

        private static void WriteCost(Costs c)
        {
            Console.WriteLine("sensitivity to the cost of a write (not measured offline): 200 items on 3 bent edges, 2 tiles");
            foreach (double write in new[] { 500.0, 2000.0, 5000.0 })
            {
                var changed = new Costs { OldEdge = c.OldEdge, OldItem = c.OldItem, OldLine = c.OldLine, NewEdge = c.NewEdge, NewItemBent = c.NewItemBent, NewItemRigid = c.NewItemRigid, NewLineBent = c.NewLineBent, NewLinePose = c.NewLinePose, Upsert = c.Upsert, Write = write, Read = c.Read };
                World w = Build(200, 3, 2, false, 0);
                Console.WriteLine($" write {write / 1000.0:0.0} us:");
                foreach (Options o in new[] { Opt(Policy.A, "A 0.25 s"), Opt(Policy.B, "B 0.3 ms"), Opt(Policy.C, "C worker"), Opt(Policy.B, "B + gate 12", 12) })
                    Console.WriteLine(Format(Simulate(w, o, changed)));
            }
        }

        /// <summary>A kilometre of wall cut into items as the placing flow cuts it (RunPath.Split: at most 64 m and 32 chords), the roads made longer: how many items would need more than 32 pieces to keep no chord over 2.5 m.</summary>
        private static void Resampling()
        {
            // 17 edges of 60 m end to end, bending as the kilometre of the earlier bench does.
            var chain = new Bezier3[17];
            Vec3 at0 = Vec3.Zero;
            for (int e = 0; e < 17; e++)
            {
                Vec3 end = at0 + new Vec3((float)Math.Sin(e * 0.6) * 10f, 0.2f, 60f);
                chain[e] = new Bezier3(at0, at0 + new Vec3(0f, 0f, 20f), end - new Vec3(0f, 0f, 20f), end);
                at0 = end;
            }

            var tables = new FollowCurve[17];
            var points = new List<Vec3>();
            var starts = new float[18];
            for (int e = 0; e < 17; e++)
            {
                tables[e] = new FollowCurve(chain[e]);
                starts[e + 1] = starts[e] + tables[e].Length;
            }

            // The line along the chain as the chain's Line makes it: a point every 2 m of the chain's length, whichever edge it is on.
            for (float d = 0f, end = starts[17] - 0.5f; d < end; d += 2f)
            {
                int e = 0;
                while (e < 16 && d >= starts[e + 1])
                    e++;
                tables[e].At(tables[e].TAt(d - starts[e]), out Vec3 on, out Vec3 h);
                points.Add(on + new Vec3(h.Z, 0f, -h.X) * 6f);
            }

            List<List<Vec3>> runs = RunPath.Split(points, 64f);
            Console.WriteLine($"a road made longer, the line of {points.Count} points cut into {runs.Count} items by RunPath.Split (at most 64 m and 32 chords): pieces wanted so that no chord is over 2.5 m, and the longest chord if the text is limited to 32 pieces");
            Console.WriteLine($"  {"length x",8} {"items over 32",14} {"worst wanted",13} {"longest chord, no cut",22} {"longest at cap 32",18} {"sag mm R30",11} {"sag mm R100",12}");
            var work = new LineWorkspace();
            foreach (float ratio in new[] { 1.1f, 1.25f, 1.5f, 2f })
            {
                // The whole chain stretched about its first point: every edge ratio times as long and the nodes still meet.
                var now = new FollowCurve[17];
                Vec3 origin = chain[0].A;
                for (int e = 0; e < 17; e++)
                {
                    Bezier3 c = chain[e];
                    Vec3 Stretch(Vec3 q) => new Vec3(origin.X + (q.X - origin.X) * ratio, q.Y, origin.Z + (q.Z - origin.Z) * ratio);
                    now[e] = new FollowCurve(new Bezier3(Stretch(c.A), Stretch(c.B), Stretch(c.C), Stretch(c.D)));
                }

                int over = 0, worstWanted = 0, items = 0;
                float longestPlain = 0f, longestCapped = 0f;
                foreach (List<Vec3> run in runs)
                {
                    string path = RunPath.Encode(run, out float yaw);
                    if (!LineFollow.TryMeasure(path, run[0], yaw, tables, 17, work, out LineRelation line))
                        continue;
                    items++;
                    LineFollow.Apply(line, now, FollowMode.Fraction, 0f, work, out _, out _, out _, out LineStats plain);
                    LineFollow.TryMeasure(path, run[0], yaw, tables, 17, work, out line);
                    LineFollow.Apply(line, now, FollowMode.Fraction, 2.5f, work, out _, out _, out _, out LineStats capped);
                    longestPlain = Math.Max(longestPlain, plain.LongestChord);
                    longestCapped = Math.Max(longestCapped, capped.LongestChord);
                    worstWanted = Math.Max(worstWanted, capped.Wanted);
                    if (capped.Wanted > RunPath.MaxPieces)
                        over++;
                }

                Console.WriteLine($"  {ratio,8:0.00} {over + " of " + items,14} {worstWanted,13} {longestPlain,20:0.00} m {longestCapped,16:0.00} m {longestCapped * longestCapped / (8f * 30f) * 1000f,11:0.0} {longestCapped * longestCapped / (8f * 100f) * 1000f,12:0.0}");
            }
        }

        /// <summary>How many of the edges a Move It drag updates are a rigid motion of what they were: each edge is made as Move It makes it (both nodes moved: moved with its handles; one node moved: NeighboursJob, a and d kept at the old end, b and c re-ratioed) and classified by RigidMotion.TryDetect.</summary>
        private static void Coverage()
        {
            Console.WriteLine("rigid share of the edges a Move It drag updates (classified by RigidMotion.TryDetect on the curves Move It writes; selection of K connected nodes in a chain, all moved by (12, 0.5, -9) m; a junction node has 3 or 4 edges)");
            Console.WriteLine($"  {"selection",-34} {"edges updated",14} {"rigid",6} {"bent",6} {"share rigid",12}");
            var rows = new List<(string name, int interior, int boundary)>
            {
                ("one node in a road", 0, 2),
                ("one junction node, 3 edges", 0, 3),
                ("one junction node, 4 edges", 0, 4),
                ("2 nodes (1 edge between)", 1, 2),
                ("4 nodes (3 edges between)", 3, 2),
                ("18 nodes (a whole kilometre)", 17, 2),
                ("51 nodes (a 3 km road)", 50, 2)
            };
            foreach ((string name, int interior, int boundary) in rows)
            {
                int rigid = 0, bent = 0;
                var delta = new Vec3(12f, 0.5f, -9f);
                for (int e = 0; e < interior + boundary; e++)
                {
                    Bezier3 curve = FollowPerf.Edge(e);
                    Bezier3 after = e < interior ? FollowFixture.Rigid(curve, delta, 0f) : FollowFixture.Neighbour(curve, delta);
                    if (RigidMotion.TryDetect(curve, after, RigidMotion.Tolerance, out _))
                        rigid++;
                    else
                        bent++;
                }

                Console.WriteLine($"  {name,-34} {interior + boundary,14} {rigid,6} {bent,6} {rigid * 100.0 / (rigid + bent),11:0.0} %");
            }
        }
    }
}
