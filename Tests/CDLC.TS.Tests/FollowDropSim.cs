using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// What the owner waits for after he drops a road with Move It (0.12.83 follow work): the production FollowScheduler, the pacing of FollowGate and a model of what ChunkSystem does with a written item
    /// (a plan every 6 frames or 0.1 s, three builds on workers that take D frames, a build dropped when an item of its tile was written meanwhile, the objects made two frames after it). The real costs of
    /// an item are the owner's: his log of 3 Nov has 130 us a looked-at item on average, 100 to 500 us a written one, where the scheduler's own prior was 2.5 us (a single) and 12 us (a line).
    /// Deterministic. The game's cost (the main thread's real time, the rebuild) is NOT measured here: these are what the log lets one assume.
    /// </summary>
    internal static class FollowDropSim
    {
        internal sealed class Policy
        {
            public string Label = "";
            /// <summary>What the scheduler is told a single item and a line cost (ns): the priors of 0.12.82 (2.5 and 12 microseconds) until a policy says otherwise.</summary>
            public double SingleNs = 2500.0, LineNs = 12000.0;
            public bool TileOrder, HoldBuilds, LookAt;
            public int AtomicGroupMax, MaxWait = 30, Gate = 13;
            public double BoostMs, BaseMs = 0.5;
        }

        internal sealed class Costs
        {
            /// <summary>The real time on the main thread (us) of a single item that moves, a line item that moves, and an item looked at that does not move (the owner's log: 130 us over all, 100 to 500 per write).</summary>
            public double Single = 150.0, Line = 200.0, Unchanged = 60.0;
        }

        internal sealed class Result
        {
            public string Label;
            public double FrameMs;
            public int[] NearWritten, NearShown, AllWritten, AllShown;
            public double MeanFollowMs, P95FollowMs, WorstFollowMs;
            public int WorkingFrames, Builds, Dropped, Accepted;
            public int NearCount, MovedCount;
        }

        internal enum Kind { Single, Line, Unchanged }

        internal sealed class World
        {
            public int Count;
            public Kind[] Kind0;
            public int[] Group, Tile, Edge;
            public float[] X, Z;

            /// <summary>Where the camera looks at the ground (the camera itself stands at the origin): where the player waits for the items to follow.</summary>
            public float FocusX, FocusZ;
        }

        /// <summary>The city of the owner's 20:35 move: some 400 items on 120 roads over a square of 5 x 5 tiles round the camera; a quarter are single signs, 40 % pieces of lines (3 to 17 pieces a line), the rest are looked at and do not move.</summary>
        internal static World Build(int items = 420, int seed = 7, float focusZ = 0f)
        {
            var random = new Random(seed);
            var w = new World { Count = items, FocusZ = focusZ };
            w.Kind0 = new Kind[items];
            w.Group = new int[items];
            w.Tile = new int[items];
            w.Edge = new int[items];
            w.X = new float[items];
            w.Z = new float[items];
            int edges = Math.Max(1, items * 2 / 7), group = 1, i = 0;
            while (i < items)
            {
                double pick = random.NextDouble();
                if (pick < 0.25)
                {
                    Place(w, i, random, edges, Kind.Single, 0, (float)(random.NextDouble() * 960 - 480), (float)(random.NextDouble() * 960 - 480));
                    i++;
                }
                else if (pick < 0.65)
                {
                    int pieces = 3 + random.Next(15);
                    double heading = random.NextDouble() * Math.PI * 2;
                    float x = (float)(random.NextDouble() * 760 - 380), z = (float)(random.NextDouble() * 760 - 380);
                    int edge = random.Next(edges);
                    for (int k = 0; k < pieces && i < items; k++, i++)
                    {
                        Place(w, i, random, edge, Kind.Line, group, x + (float)(Math.Sin(heading) * 64 * k), z + (float)(Math.Cos(heading) * 64 * k));
                        w.Edge[i] = (edge + k / 3) % edges;
                    }

                    group++;
                }
                else
                {
                    Place(w, i, random, edges, Kind.Unchanged, 0, (float)(random.NextDouble() * 960 - 480), (float)(random.NextDouble() * 960 - 480));
                    i++;
                }
            }

            return w;
        }

        private static void Place(World w, int i, Random random, int edges, Kind kind, int group, float x, float z)
        {
            w.Kind0[i] = kind;
            w.Group[i] = group;
            w.X[i] = x;
            w.Z[i] = z + w.FocusZ;
            w.Tile[i] = FollowWatch<int, int>.TileOf(new CDLC.TS.Snap.Vec3(x, 0f, z + w.FocusZ));
            w.Edge[i] = random.Next(edges);
        }

        private static int Pct(List<int> sorted, double p) => sorted.Count == 0 ? -1 : sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(p * (sorted.Count - 1) + 0.5))];

        internal static Result Run(World w, Policy p, Costs c, double frameMs, int maxFrames = 1500, double nearMetres = 150.0)
        {
            int n = w.Count;
            var random = new Random(3);
            var sched = new FollowScheduler { TileGateFrames = p.Gate, MaxWaitFrames = p.MaxWait, TileOrder = p.TileOrder, AtomicGroupMax = p.AtomicGroupMax };
            for (int i = 0; i < n; i++)
            {
                sched.Add(i, w.Group[i], w.X[i], w.Z[i], (float)(w.Kind0[i] == Kind.Line ? p.LineNs : p.SingleNs), w.Tile[i]);
                sched.Link(i, w.Edge[i]);
            }

            sched.SetFocus(0f, 0f, p.LookAt, w.FocusX, w.FocusZ);
            int edges = w.Edge.Max() + 1;
            for (int e = 0; e < edges; e++)
                sched.EdgeChanged(e, 1, 0);

            int build = Math.Max(2, (int)Math.Ceiling(60.0 / frameMs)), apply = frameMs > 25.0 ? 1 : 2;
            var writtenAt = new int[n];
            var shownAt = new int[n];
            var writeVersion = new int[n];
            for (int i = 0; i < n; i++)
            {
                writtenAt[i] = -1;
                shownAt[i] = -1;
            }

            var tileVersion = new Dictionary<int, int>();
            var startedVersion = new Dictionary<int, int>();
            var doneAt = new Dictionary<int, int>();
            var jobVersion = new Dictionary<int, int>();
            var itemsOfTile = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (!itemsOfTile.TryGetValue(w.Tile[i], out List<int> list))
                    itemsOfTile[w.Tile[i]] = list = new List<int>();
                list.Add(i);
                tileVersion[w.Tile[i]] = 0;
                startedVersion[w.Tile[i]] = 0;
            }

            var jobs = new List<FollowJob>();
            var followMs = new List<double>();
            double scale = 1.0;
            int lastPlan = -1000, builds = 0, dropped = 0, accepted = 0, workingFrames = 0;
            for (int f = 0; f < maxFrames; f++)
            {
                // The builds that are done.
                foreach (int tile in doneAt.Keys.ToList())
                {
                    if (doneAt[tile] > f)
                        continue;
                    doneAt.Remove(tile);
                    if (jobVersion[tile] == tileVersion[tile])
                    {
                        accepted++;
                        foreach (int item in itemsOfTile[tile])
                        {
                            if (writtenAt[item] >= 0 && shownAt[item] < 0 && writeVersion[item] <= jobVersion[tile])
                                shownAt[item] = f;
                        }
                    }
                    else
                    {
                        dropped++;
                    }
                }

                // The follow.
                sched.SetFocus(0f, 0f, p.LookAt, w.FocusX, w.FocusZ);
                double boost = p.BoostMs < 0.0 ? FollowGate.BoostMs(sched.QuietFrames(f), frameMs) : p.BoostMs > 0.0 && sched.QuietFrames(f) >= FollowGate.BoostQuietFrames ? p.BoostMs : 0.0;
                sched.NearBoostNs = boost * 1e6 / scale;
                jobs.Clear();
                int taken = sched.Take(f, FollowGate.PacedBudgetNs(p.BaseMs, scale), 200, jobs);
                if (taken > 0)
                {
                    workingFrames++;
                    double real = 0.0, model = 0.0;
                    foreach (FollowJob job in jobs)
                    {
                        Kind kind = w.Kind0[job.Item];
                        double cost = (kind == Kind.Line ? c.Line : kind == Kind.Single ? c.Single : c.Unchanged) * (0.7 + 0.6 * random.NextDouble());
                        real += cost * 1000.0;
                        model += kind == Kind.Line ? p.LineNs : p.SingleNs;
                        if (kind != Kind.Unchanged)
                        {
                            int tile = w.Tile[job.Item];
                            tileVersion[tile]++;
                            writtenAt[job.Item] = f;
                            writeVersion[job.Item] = tileVersion[tile];
                        }

                        sched.Wrote(job.Item, job.Version);
                    }

                    scale = FollowGate.NextScale(scale, real, model);
                    followMs.Add(real / 1e6);
                }

                // ChunkSystem: a plan every six frames starts the builds of the tiles that changed, nearest first, three at a time.
                if (f - lastPlan >= 6)
                {
                    lastPlan = f;
                    int running = doneAt.Count;
                    var dirty = tileVersion.Keys.Where(t => tileVersion[t] > startedVersion[t] && !doneAt.ContainsKey(t) && !(p.HoldBuilds && sched.IsBursting(t))).OrderBy(t => TileDistance(t)).ToList();
                    foreach (int tile in dirty)
                    {
                        if (running >= 3)
                            break;
                        jobVersion[tile] = tileVersion[tile];
                        startedVersion[tile] = tileVersion[tile];
                        doneAt[tile] = f + build + apply;
                        running++;
                        builds++;
                    }
                }
            }

            var near = new List<int>[2];
            var res = new Result { Label = p.Label, FrameMs = frameMs, WorkingFrames = workingFrames, Builds = builds, Dropped = dropped, Accepted = accepted };
            var nearW = new List<int>();
            var nearS = new List<int>();
            var allW = new List<int>();
            var allS = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (w.Kind0[i] == Kind.Unchanged)
                    continue;
                res.MovedCount++;
                int wr = writtenAt[i], sh = shownAt[i] < 0 ? maxFrames : shownAt[i];
                bool isNear = Math.Sqrt((double)(w.X[i] - w.FocusX) * (w.X[i] - w.FocusX) + (double)(w.Z[i] - w.FocusZ) * (w.Z[i] - w.FocusZ)) <= nearMetres;
                if (isNear)
                    res.NearCount++;
                allW.Add(wr < 0 ? maxFrames : wr);
                allS.Add(sh);
                if (isNear)
                {
                    nearW.Add(wr < 0 ? maxFrames : wr);
                    nearS.Add(sh);
                }
            }

            foreach (List<int> list in new[] { nearW, nearS, allW, allS })
                list.Sort();
            res.NearWritten = new[] { Pct(nearW, 0.5), Pct(nearW, 0.95), Pct(nearW, 1.0) };
            res.NearShown = new[] { Pct(nearS, 0.5), Pct(nearS, 0.95), Pct(nearS, 1.0) };
            res.AllWritten = new[] { Pct(allW, 0.5), Pct(allW, 0.95), Pct(allW, 1.0) };
            res.AllShown = new[] { Pct(allS, 0.5), Pct(allS, 0.95), Pct(allS, 1.0) };
            followMs.Sort();
            res.MeanFollowMs = followMs.Count == 0 ? 0.0 : followMs.Average();
            res.P95FollowMs = followMs.Count == 0 ? 0.0 : followMs[Math.Min(followMs.Count - 1, (int)(followMs.Count * 0.95))];
            res.WorstFollowMs = followMs.Count == 0 ? 0.0 : followMs[followMs.Count - 1];
            return res;
        }

        private static double TileDistance(int tile)
        {
            FollowScheduler.TileOrigin(tile, out float x, out float z);
            float cx = x + Tiles.Size * 0.5f, cz = z + Tiles.Size * 0.5f;
            return Math.Sqrt((double)cx * cx + (double)cz * cz);
        }

        /// <summary>A line of a table: frames, and seconds at the frame rate the run was made at.</summary>
        internal static string Format(Result r)
        {
            string S(int[] v) => $"{v[0],4} {v[1],4} {v[2],4} ({v[0] * r.FrameMs / 1000.0:0.00} {v[1] * r.FrameMs / 1000.0:0.00} {v[2] * r.FrameMs / 1000.0:0.00} s)";
            return $"{r.Label,-46} near written {S(r.NearWritten)} shown {S(r.NearShown)} | all written {S(r.AllWritten)} shown {S(r.AllShown)} | follow ms/frame mean {r.MeanFollowMs:0.00} p95 {r.P95FollowMs:0.00} worst {r.WorstFollowMs:0.00}; builds {r.Builds} dropped {r.Dropped}";
        }

        internal static Policy Production(string label, double boostMs, int slice = -1) => new Policy
        {
            Label = label, SingleNs = FollowWatch<int, int>.SingleNs, LineNs = FollowWatch<int, int>.LineNs, TileOrder = true, MaxWait = FollowGate.MaxWaitFrames, AtomicGroupMax = slice < 0 ? FollowGate.GroupSlice : slice, HoldBuilds = true, LookAt = true, BoostMs = boostMs
        };

        /// <summary>The steps from 0.12.83 to what the system sets now, each adding to the one before; the last is the production policy (FollowGate.BoostMs decides the boost).</summary>
        internal static Policy[] Policies() => new[]
        {
            new Policy { Label = "0.12.83: model costs 2.5/12 us, items by ring" },
            new Policy { Label = "+ costs from his log (100/150 us)", SingleNs = FollowWatch<int, int>.SingleNs, LineNs = FollowWatch<int, int>.LineNs },
            new Policy { Label = "+ tiles in order, aging 400 frames", SingleNs = FollowWatch<int, int>.SingleNs, LineNs = FollowWatch<int, int>.LineNs, TileOrder = true, MaxWait = FollowGate.MaxWaitFrames },
            new Policy { Label = "+ lines of over 24 items a slice at a time", SingleNs = FollowWatch<int, int>.SingleNs, LineNs = FollowWatch<int, int>.LineNs, TileOrder = true, MaxWait = FollowGate.MaxWaitFrames, AtomicGroupMax = FollowGate.GroupSlice },
            new Policy { Label = "+ no build of a tile being written", SingleNs = FollowWatch<int, int>.SingleNs, LineNs = FollowWatch<int, int>.LineNs, TileOrder = true, MaxWait = FollowGate.MaxWaitFrames, AtomicGroupMax = FollowGate.GroupSlice, HoldBuilds = true },
            new Policy { Label = "+ the point looked at is a focus", SingleNs = FollowWatch<int, int>.SingleNs, LineNs = FollowWatch<int, int>.LineNs, TileOrder = true, MaxWait = FollowGate.MaxWaitFrames, AtomicGroupMax = FollowGate.GroupSlice, HoldBuilds = true, LookAt = true },
            Production("+ near boost (production)", -1.0)
        };

        /// <summary>What one Take costs the main thread with this many items waiting (us, this machine, .NET 10; the game's Mono is slower): the pick is not in the budget it keeps.</summary>
        internal static double TakeMicroseconds(int pending, bool tileOrder)
        {
            double best = double.MaxValue;
            for (int run = 0; run < 5; run++)
                best = Math.Min(best, TakeMicrosecondsOnce(pending, tileOrder, 200));
            return best;
        }

        private static double TakeMicrosecondsOnce(int pending, bool tileOrder, int calls)
        {
            var random = new Random(5);
            var s = new FollowScheduler { TileGateFrames = 13, TileOrder = tileOrder, AtomicGroupMax = tileOrder ? 8 : 0, MaxWaitFrames = 400 };
            for (int i = 0; i < pending; i++)
            {
                float x = (float)(random.NextDouble() * 960 - 480), z = (float)(random.NextDouble() * 960 - 480);
                s.Add(i, 1 + i / 12, x, z, 100000f, FollowWatch<int, int>.TileOf(new CDLC.TS.Snap.Vec3(x, 0f, z)));
                s.Link(i, i);
                s.EdgeChanged(i, 1, 0);
            }

            s.SetFocus(0f, 0f, false, 0f, 0f);
            var jobs = new List<FollowJob>();
            int version = 1;
            double total = 0.0;
            for (int call = 0; call < calls + 60; call++)
            {
                jobs.Clear();
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                s.Take(call + 1, 500000.0, 200, jobs);
                if (call >= 60)
                    total += (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1e6 / System.Diagnostics.Stopwatch.Frequency;
                foreach (FollowJob job in jobs)
                {
                    s.Wrote(job.Item, job.Version);
                    s.EdgeChanged(job.Item, ++version, call + 1);
                }
            }

            return total / calls;
        }

        /// <summary>What writing a line item's new path costs apart from the engine: the settings text made again (MarkingTemplates.WithPath) and the path read out of the old one, per write (us and bytes).</summary>
        internal static (double WithPathUs, double WithPathBytes, double DecodeUs, double DecodeBytes) LineWriteCost()
        {
            GuideTemplate lane = MarkingTemplates.ById("mk.line.lane");
            var points = new List<CDLC.TS.Snap.Vec3>();
            for (int i = 0; i < 33; i++)
                points.Add(new CDLC.TS.Snap.Vec3(i * 2f, 0f, (float)Math.Sin(i * 0.2) * 3f));
            string path = RunPath.Encode(points, out _);
            string spec = MarkingTemplates.WithPath(lane, lane.Default().ToString(), path);
            const int n = 2000;
            for (int i = 0; i < 200; i++)
            {
                MarkingTemplates.WithPath(lane, spec, path);
                RunPath.Decode(path);
            }

            long b0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < n; i++)
                MarkingTemplates.WithPath(lane, spec, path);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            long b1 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < n; i++)
                RunPath.Decode(path);
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            long b2 = GC.GetAllocatedBytesForCurrentThread();
            double us = 1e6 / System.Diagnostics.Stopwatch.Frequency / n;
            return ((t1 - t0) * us, (b1 - b0) / (double)n, (t2 - t1) * us, (b2 - b1) / (double)n);
        }

        public static void Print()
        {
            (double wu, double wb, double du, double db) = LineWriteCost();
            Console.WriteLine($"A line item's new text: WithPath {wu:0.0} us and {wb:0} bytes, the path decoded {du:0.0} us and {db:0} bytes (a path of 33 points; a line item is written with both and more)");
            foreach (int pending in new[] { 500, 2000, 8000 })
                Console.WriteLine($"Take with {pending} items waiting: {TakeMicroseconds(pending, false):0} us by rings, {TakeMicroseconds(pending, true):0} us by tiles (a frame is 15000 us)");
            Costs c = new Costs();
            foreach ((float focus, double fps) in new[] { (0f, 66.0), (350f, 66.0), (350f, 40.0), (350f, 30.0) })
            {
                World w = Build(focusZ: focus);
                double frameMs = 1000.0 / fps;
                Console.WriteLine($"-- {fps:0} fps, the camera {focus:0} m from the point it looks at, {w.Count} items; frames and (seconds) as p50 p95 max; near = within 150 m of the point looked at");
                foreach (Policy p in Policies())
                    Console.WriteLine(Format(Run(w, p, c, frameMs)));
                foreach (double boost in new[] { 1.0, 3.0 })
                    Console.WriteLine(Format(Run(w, Production($"  (boost fixed at {boost:0} ms)", boost), c, frameMs)));
                Console.WriteLine(Format(Run(w, Production("  (alternative: slices from 8 items)", -1.0, 8), c, frameMs)));
            }
        }
    }
}
