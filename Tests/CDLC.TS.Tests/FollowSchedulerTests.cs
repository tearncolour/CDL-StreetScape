using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using static CDLC.TS.Tests.FollowFixture;

namespace CDLC.TS.Tests
{
    /// <summary>The follow scheduler (when the items follow: events, pool, budget, priority, coalescing, worker hand-off) and the thread safety of the pure follow parts (0.12.74 follow work).</summary>
    internal static class FollowSchedulerTests
    {
        public static void RunAll()
        {
            Program.Run("scheduler: an event reaches the items of its edge by the pool and no others", Pool);
            Program.Run("scheduler: versions that come while an item waits are one follow, at the newest", Coalescing);
            Program.Run("scheduler: the nearest to the camera or the cursor first, in the order they came within a ring", Priority);
            Program.Run("scheduler: a line's items are taken together, all or none", Groups);
            Program.Run("scheduler: the cap, the budget on a fake clock, and at least one", Budget);
            Program.Run("scheduler: an item that has waited long is taken before the near ones", Aging);
            Program.Run("scheduler: a tile is written in a burst, and not again until the gate opens", TileGate);
            Program.Run("scheduler: the worker hand-off drops what a newer version superseded", Worker);
            Program.Run("scheduler: a worker's results go out in tile bursts and nothing is left behind", GateWithWorker);
            Program.Run("scheduler: the same events give the same schedule", Deterministic);
            Program.Run("follow threads: the pure parts give the single thread's results on 8 threads", Threads);
            Program.Run("follow threads: no static state that changes in the follow classes", NoStatics);
        }

        private static FollowScheduler MakePool(int items, int edges)
        {
            var scheduler = new FollowScheduler();
            for (int i = 0; i < items; i++)
            {
                scheduler.Add(i, 0, i * 10f, 0f, 100f);
                scheduler.Link(i, i % edges);
            }

            return scheduler;
        }

        private static void Pool()
        {
            FollowScheduler s = MakePool(12, 3);
            Program.Check(s.EdgeChanged(1, 1, 0) == 4 && s.PendingCount == 4, "an event for an edge with 4 items makes 4 pending");
            Program.Check(s.EdgeChanged(7, 1, 0) == 0 && s.PendingCount == 4, "an edge that has no items makes none");
            var jobs = new List<FollowJob>();
            s.Take(0, 1e9, 100, jobs);
            Program.Check(jobs.Count == 4 && jobs.All(j => j.Item % 3 == 1 && j.Version == 1), "they are the items of that edge, for that version");
            Program.Check(s.Settled == false || s.PendingCount == 0, "taken items are not pending");
            foreach (FollowJob j in jobs)
                s.Wrote(j.Item, j.Version);
            Program.Check(s.Settled && s.DoneVersion(1) == 1, "written: nothing waits");
            s.Remove(4);
            s.EdgeChanged(1, 2, 1);
            Program.Check(s.PendingCount == 3, "an item that was removed is not followed");
            s.Add(4, 0, 0f, 0f, 100f);
            Program.Check(s.EdgeChanged(1, 3, 2) == 3, "and an item added again is in no pool until it is linked");
            var line = new FollowScheduler();
            line.Add(1, 0, 0f, 0f, 1f);
            line.Link(1, 5);
            line.Link(1, 6);
            line.EdgeChanged(5, 1, 0);
            line.EdgeChanged(6, 1, 0);
            Program.Check(line.PendingCount == 1 && line.Coalesced == 0, "an item on two edges that change in the same frame is one pending item");
        }

        private static void Coalescing()
        {
            var s = new FollowScheduler();
            s.Add(1, 0, 0f, 0f, 100f);
            s.Link(1, 9);
            for (int v = 1; v <= 10; v++)
                s.EdgeChanged(9, v, v);
            s.EdgeChanged(9, 4, 11);
            var jobs = new List<FollowJob>();
            s.Take(12, 1e9, 10, jobs);
            Program.Check(jobs.Count == 1 && jobs[0].Version == 10 && s.Coalesced == 9, $"ten versions are one follow at the tenth: {jobs.Count} job(s), version {(jobs.Count > 0 ? jobs[0].Version : -1)}, {s.Coalesced} coalesced; an old version is ignored");
            Program.Check(s.PendingSince(1) == -1, "and it is not pending once taken");
            s.EdgeChanged(9, 11, 13);
            Program.Check(s.PendingCount == 1 && s.PendingSince(1) == 13, "a version that comes after it was taken is a new wait");
        }

        private static void Priority()
        {
            var s = new FollowScheduler();
            // Distances from the camera at the origin: 300, 10, 1000, 40, 12, 45 m; item 4 and 1 are in the same ring (<= 25 m) and 3 and 5 in another (<= 50 m).
            float[] at = { 300f, 10f, 1000f, 40f, 12f, 45f };
            for (int i = 0; i < at.Length; i++)
            {
                s.Add(i, 0, at[i], 0f, 10f);
                s.Link(i, 1);
            }

            s.SetFocus(0f, 0f, false, 0f, 0f);
            s.EdgeChanged(1, 1, 0);
            var jobs = new List<FollowJob>();
            s.Take(0, 1e9, 100, jobs);
            Program.Check(string.Join(",", jobs.Select(j => j.Item)) == "1,4,3,5,0,2", $"ring by ring, the order they came within a ring: {string.Join(",", jobs.Select(j => j.Item))}");
            // The cursor near the far item: it comes first.
            var c = new FollowScheduler();
            for (int i = 0; i < at.Length; i++)
            {
                c.Add(i, 0, at[i], 0f, 10f);
                c.Link(i, 1);
            }

            c.SetFocus(0f, 0f, true, 1000f, 5f);
            c.EdgeChanged(1, 1, 0);
            jobs.Clear();
            c.Take(0, 1e9, 100, jobs);
            Program.Check(jobs.Take(3).Select(j => j.Item).OrderBy(i => i).SequenceEqual(new[] { 1, 2, 4 }) && jobs[3].Item == 3, $"an item near the cursor is as near as two near the camera, and the rest follow: {string.Join(",", jobs.Select(j => j.Item))}");
        }

        private static void Groups()
        {
            var s = new FollowScheduler();
            // A line of 3 items (group 7): one at 20 m, two at 600 m. A single at 200 m, and a single at 30 m.
            s.Add(0, 7, 20f, 0f, 100f);
            s.Add(1, 7, 600f, 0f, 100f);
            s.Add(2, 7, 620f, 0f, 100f);
            s.Add(3, 0, 200f, 0f, 100f);
            s.Add(4, 0, 30f, 0f, 100f);
            for (int i = 0; i < 5; i++)
                s.Link(i, 1);
            s.SetFocus(0f, 0f, false, 0f, 0f);
            s.EdgeChanged(1, 1, 0);
            var jobs = new List<FollowJob>();
            s.Take(0, 1e9, 100, jobs);
            Program.Check(string.Join(",", jobs.Select(j => j.Item)) == "0,1,2,4,3", $"the line comes in the ring of its nearest item, together: {string.Join(",", jobs.Select(j => j.Item))}");
            // Budget for 4 of the 5: the line (3) and one more; the line is never cut in two.
            var t = new FollowScheduler();
            for (int i = 0; i < 5; i++)
            {
                t.Add(i, i < 3 ? 7 : 0, i < 3 ? 20f + i : 200f + i, 0f, 100f);
                t.Link(i, 1);
            }

            t.SetFocus(0f, 0f, false, 0f, 0f);
            t.EdgeChanged(1, 1, 0);
            jobs.Clear();
            t.Take(0, 250.0, 100, jobs);
            Program.Check(jobs.Count == 0 || jobs.Count == 3 || jobs.Count == 5, $"a budget of 2.5 items takes the first whole group or one item, never part of a line: {jobs.Count}");
            Program.Check(jobs.Count == 3 && t.PendingCount == 2, "the line (3 items) is taken as the first unit although it passes the budget, and the rest wait");
            var u = new FollowScheduler();
            for (int i = 0; i < 6; i++)
            {
                u.Add(i, i < 4 ? 7 : 0, 20f + i, 0f, 100f);
                u.Link(i, 1);
            }

            u.SetFocus(0f, 0f, false, 0f, 0f);
            u.EdgeChanged(1, 1, 0);
            jobs.Clear();
            u.Take(0, 1e9, 2, jobs);
            Program.Check(jobs.Count == 4, $"the cap of 2 does not cut a line of 4 that comes first: {jobs.Count}");
            // A single near and a line of 4 farther, cap 3: the single, and the line waits for the next frame (it would pass the cap).
            var v = new FollowScheduler();
            v.Add(0, 0, 5f, 0f, 1f);
            v.Link(0, 1);
            for (int i = 1; i <= 4; i++)
            {
                v.Add(i, 9, 300f + i, 0f, 1f);
                v.Link(i, 1);
            }

            v.SetFocus(0f, 0f, false, 0f, 0f);
            v.EdgeChanged(1, 1, 0);
            jobs.Clear();
            Program.Check(v.Take(0, 1e9, 3, jobs) == 1 && jobs[0].Item == 0 && v.PendingCount == 4, "a line that is not the first unit and would pass the cap waits whole");
            jobs.Clear();
            Program.Check(v.Take(1, 1e9, 3, jobs) == 4, "and is taken first in the next frame");
        }

        private static void Budget()
        {
            var s = MakePool(20, 1);
            s.SetFocus(0f, 0f, false, 0f, 0f);
            s.EdgeChanged(0, 1, 0);
            var jobs = new List<FollowJob>();
            Program.Check(s.Take(0, 350.0, 100, jobs) == 3 && s.PendingCount == 17, "100 ns each, 350 ns: 3 items");
            jobs.Clear();
            Program.Check(s.Take(1, 10.0, 100, jobs) == 1, "a budget below one item still takes one, so that nothing waits for ever");
            jobs.Clear();
            Program.Check(s.Take(2, 1e9, 5, jobs) == 5 && s.PendingCount == 11, "the cap of 5 items");
            jobs.Clear();
            Program.Check(s.Take(3, 0.0, 0, jobs) == 0, "a cap of 0 takes none");
            int frames = 0;
            while (!s.Settled && frames < 100)
            {
                jobs.Clear();
                s.Take(10 + frames, 1e9, 4, jobs);
                foreach (FollowJob j in jobs)
                    s.Wrote(j.Item, j.Version);
                frames++;
            }

            Program.Check(frames <= 3, $"the rest in 3 frames of 4 or fewer: {frames}");
            // Items with different costs are charged each its own.
            var mixed = new FollowScheduler();
            mixed.Add(0, 0, 1f, 0f, 500f);
            mixed.Add(1, 0, 2f, 0f, 50f);
            mixed.Add(2, 0, 3f, 0f, 50f);
            for (int i = 0; i < 3; i++)
                mixed.Link(i, 4);
            mixed.SetFocus(0f, 0f, false, 0f, 0f);
            mixed.EdgeChanged(4, 1, 0);
            jobs.Clear();
            Program.Check(mixed.Take(0, 560.0, 10, jobs) == 2 && jobs[0].Item == 0 && jobs[1].Item == 1, "each is charged its own cost: 500 + 50 fit 560, the third (600) does not");
        }

        private static void Aging()
        {
            var s = new FollowScheduler { MaxWaitFrames = 5 };
            s.Add(0, 0, 3000f, 0f, 100f);
            s.Link(0, 1);
            for (int i = 1; i <= 6; i++)
            {
                s.Add(i, 0, 5f * i, 0f, 100f);
                s.Link(i, 2);
            }

            s.SetFocus(0f, 0f, false, 0f, 0f);
            s.EdgeChanged(1, 1, 0);
            var jobs = new List<FollowJob>();
            bool farTaken = false;
            int takenAt = -1;
            for (int frame = 0; frame < 20 && !farTaken; frame++)
            {
                // A drag: the near items are pending again every frame, and the budget takes one item a frame.
                s.EdgeChanged(2, frame + 1, frame);
                jobs.Clear();
                s.Take(frame, 100.0, 10, jobs);
                foreach (FollowJob j in jobs)
                {
                    s.Wrote(j.Item, j.Version);
                    if (j.Item == 0)
                    {
                        farTaken = true;
                        takenAt = frame;
                    }
                }
            }

            Program.Check(farTaken && takenAt <= 6, $"the far item was taken at frame {takenAt}, not starved by the near ones that came every frame");
            var never = new FollowScheduler { MaxWaitFrames = int.MaxValue };
            never.Add(0, 0, 3000f, 0f, 100f);
            never.Link(0, 1);
            for (int i = 1; i <= 6; i++)
            {
                never.Add(i, 0, 5f * i, 0f, 100f);
                never.Link(i, 2);
            }

            never.SetFocus(0f, 0f, false, 0f, 0f);
            never.EdgeChanged(1, 1, 0);
            bool starved = true;
            for (int frame = 0; frame < 20; frame++)
            {
                never.EdgeChanged(2, frame + 1, frame);
                jobs.Clear();
                never.Take(frame, 100.0, 10, jobs);
                foreach (FollowJob j in jobs)
                {
                    never.Wrote(j.Item, j.Version);
                    if (j.Item == 0)
                        starved = false;
                }
            }

            Program.Check(starved, "without the rule the far item would wait the whole drag (the control of the check above)");
        }

        private static void TileGate()
        {
            // 6 items in tile 3 and 2 in tile 4, a gate of 10 frames, a drag that makes a version every frame, a budget of 3 items.
            var s = new FollowScheduler { TileGateFrames = 10 };
            for (int i = 0; i < 8; i++)
            {
                s.Add(i, 0, i * 2f, 0f, 100f, i < 6 ? 3 : 4);
                s.Link(i, 1);
            }

            s.SetFocus(0f, 0f, false, 0f, 0f);
            var jobs = new List<FollowJob>();
            var writes = new List<string>();
            for (int frame = 0; frame < 40; frame++)
            {
                if (frame < 25)
                    s.EdgeChanged(1, frame + 1, frame);
                jobs.Clear();
                s.Take(frame, 300.0, 100, jobs);
                foreach (FollowJob j in jobs)
                {
                    s.Wrote(j.Item, j.Version);
                    writes.Add(frame + ":" + (j.Item < 6 ? "A" : "B"));
                }
            }

            var tileA = writes.Where(w => w.EndsWith(":A")).Select(w => int.Parse(w.Split(':')[0])).Distinct().ToList();
            // Frame 0: 3 items of A and 2 of B (budget 3 items: A's first 3), the burst of A goes on in frame 1 with the rest; then A is closed until frame 11.
            Program.Check(tileA.Count > 0 && tileA[0] == 0 && tileA[1] == 1, $"a burst the budget cut goes on in the next frame: {string.Join(",", tileA)}");
            var bursts = new List<int[]>();
            foreach (int f in tileA)
            {
                if (bursts.Count > 0 && bursts[bursts.Count - 1][1] == f - 1)
                    bursts[bursts.Count - 1][1] = f;
                else
                    bursts.Add(new[] { f, f });
            }

            bool spaced = true;
            for (int i = 1; i < bursts.Count; i++)
                spaced &= bursts[i][0] - bursts[i - 1][1] >= 10;
            Program.Check(bursts.Count >= 3 && bursts.Count <= 5 && spaced, $"then A is written in bursts, the next 10 frames after the last frame of the one before: {string.Join(" ", bursts.Select(x => x[0] + "-" + x[1]))}");
            Program.Check(s.Settled, "and everything is written in the end");
            // Without the gate every frame writes.
            var open = new FollowScheduler();
            for (int i = 0; i < 6; i++)
            {
                open.Add(i, 0, i * 2f, 0f, 100f, 3);
                open.Link(i, 1);
            }

            int written = 0;
            for (int frame = 0; frame < 10; frame++)
            {
                open.EdgeChanged(1, frame + 1, frame);
                jobs.Clear();
                open.Take(frame, 1e9, 100, jobs);
                foreach (FollowJob j in jobs)
                    open.Wrote(j.Item, j.Version);
                written += jobs.Count;
            }

            Program.Check(written == 60, $"with no gate the tile's 6 items are written in each of 10 frames: {written}");
            // Items of a closed tile coalesce: the burst is for the newest version.
            var held = new FollowScheduler { TileGateFrames = 10 };
            held.Add(1, 0, 1f, 0f, 1f, 7);
            held.Link(1, 1);
            held.EdgeChanged(1, 1, 0);
            jobs.Clear();
            held.Take(0, 1e9, 10, jobs);
            held.Wrote(1, 1);
            for (int v = 2; v <= 8; v++)
                held.EdgeChanged(1, v, v);
            jobs.Clear();
            Program.Check(held.Take(9, 1e9, 10, jobs) == 0, "inside the gate nothing is taken");
            held.EdgeChanged(1, 9, 10);
            Program.Check(held.Take(10, 1e9, 10, jobs) == 1 && jobs[0].Version == 9, "when it opens the item is followed once, for the newest version");
        }

        private static void GateWithWorker()
        {
            var s = new FollowScheduler { TileGateFrames = 12 };
            for (int i = 0; i < 200; i++)
            {
                s.Add(i, 0, i * 3f, 0f, 100f, i < 100 ? 0 : 1);
                s.Link(i, i % 3);
            }

            s.SetFocus(300f, 0f, false, 0f, 0f);
            var inFlight = new Queue<List<FollowJob>>();
            var jobs = new List<FollowJob>();
            var wrote = new int[200];
            int settledAt = -1, neverAt20 = 0;
            for (int frame = 0; frame < 130; frame++)
            {
                if (inFlight.Count > 0)
                    foreach (FollowJob j in inFlight.Dequeue())
                        s.Deliver(j.Item, j.Version, frame);
                jobs.Clear();
                s.ApplyReady(frame, 2000.0, 150000.0, 200, jobs);
                foreach (FollowJob j in jobs)
                {
                    s.Wrote(j.Item, j.Version);
                    wrote[j.Item]++;
                }

                if (frame < 90)
                    for (int e = 0; e < 3; e++)
                        s.EdgeChanged(e, frame + 1, frame);
                var batch = new List<FollowJob>();
                s.TakeAll(frame, int.MaxValue, batch);
                inFlight.Enqueue(batch);
                if (frame == 20)
                    neverAt20 = wrote.Count(x => x == 0);
                if (frame >= 90 && s.Settled && batch.Count == 0 && inFlight.All(b => b.Count == 0) && settledAt < 0)
                    settledAt = frame;
            }

            Program.Check(neverAt20 == 0 && settledAt > 0 && settledAt <= 110 && wrote.Max() <= 10, $"a worker's results are written in gated bursts: every item written by frame 20 ({neverAt20} not), settled at frame {settledAt}, at most {wrote.Max()} writes of an item in 90 frames");
        }

        private static void Worker()
        {
            var s = new FollowScheduler();
            s.Add(1, 0, 10f, 0f, 100f);
            s.Add(2, 0, 20f, 0f, 100f);
            s.Link(1, 5);
            s.Link(2, 5);
            s.SetFocus(0f, 0f, false, 0f, 0f);
            s.EdgeChanged(5, 1, 0);
            var jobs = new List<FollowJob>();
            Program.Check(s.TakeAll(0, 100, jobs) == 2 && s.PendingCount == 0, "the worker takes all that waits");
            // Version 2 comes while the worker is at version 1.
            s.EdgeChanged(5, 2, 1);
            Program.Check(s.PendingCount == 2, "the items wait again for version 2");
            var second = new List<FollowJob>();
            s.TakeAll(1, 100, second);
            foreach (FollowJob j in jobs)
                s.Deliver(j.Item, j.Version, 2);
            Program.Check(s.ReadyCount == 0, "the results of version 1 are dropped: version 2 is under way");
            foreach (FollowJob j in second)
                s.Deliver(j.Item, j.Version, 2);
            var apply = new List<FollowJob>();
            Program.Check(s.ApplyReady(2, 500.0, 600.0, 100, apply) == 1 && apply[0].Item == 1 && apply[0].Version == 2, "the main thread writes what its budget allows (500 ns each, 600 ns: one), nearest first, version 2");
            s.Wrote(apply[0].Item, apply[0].Version);
            apply.Clear();
            Program.Check(s.ApplyReady(3, 500.0, 600.0, 100, apply) == 1 && apply[0].Item == 2, "and the other in the next frame");
            s.Wrote(apply[0].Item, apply[0].Version);
            Program.Check(s.Settled && s.DoneVersion(1) == 2 && s.DoneVersion(2) == 2, "then nothing waits");
            // A result that arrives for a version already written is not written again.
            s.Deliver(1, 2, 4);
            Program.Check(s.ReadyCount == 0, "a result for a version that is written is dropped");
            // A result for version 3 delivered while version 4 was asked for: written, and the item waits again for 4.
            var w = new FollowScheduler();
            w.Add(1, 0, 1f, 0f, 1f);
            w.Link(1, 1);
            w.EdgeChanged(1, 3, 0);
            var j3 = new List<FollowJob>();
            w.TakeAll(0, 10, j3);
            w.Deliver(1, 3, 1);
            w.EdgeChanged(1, 4, 1);
            var out3 = new List<FollowJob>();
            w.ApplyReady(1, 1.0, 1e9, 10, out3);
            Program.Check(out3.Count == 1 && out3[0].Version == 3, "the result of 3 is written");
            w.Wrote(1, 3);
            Program.Check(w.PendingCount == 1, "and the item waits again for 4");
        }

        private static void Deterministic()
        {
            string Run()
            {
                var random = new Random(77);
                var s = new FollowScheduler();
                for (int i = 0; i < 300; i++)
                {
                    s.Add(i, i % 25 == 0 ? 1 + i / 25 : 0, (float)(random.NextDouble() * 2000 - 1000), (float)(random.NextDouble() * 2000 - 1000), 50f + random.Next(200));
                    s.Link(i, random.Next(20));
                }

                var log = new System.Text.StringBuilder();
                var jobs = new List<FollowJob>();
                for (int frame = 0; frame < 60; frame++)
                {
                    s.SetFocus(frame * 3f, 0f, frame % 2 == 0, 0f, frame * 2f);
                    for (int e = 0; e < 5; e++)
                        s.EdgeChanged(random.Next(20), frame + 1, frame);
                    jobs.Clear();
                    s.Take(frame, 6000.0, 40, jobs);
                    foreach (FollowJob j in jobs)
                    {
                        log.Append(j.Item).Append(':').Append(j.Version).Append(' ');
                        s.Wrote(j.Item, j.Version);
                    }

                    log.Append('|');
                }

                return log.ToString();
            }

            string first = Run();
            Program.Check(first.Length > 1000 && first == Run() && first == Run(), "the same events, focus and numbers give the same jobs in the same order, every time");
        }

        // ------------------------------------------------------------------ threads

        /// <summary>What one thread computes from the shared, immutable tables: the batch of 300 items on 6 edges, 5 lines and the cache's tables, as one string of numbers.</summary>
        private static string Compute(FollowCurve[] before, FollowCurve[] after, RoadRelation[] relations, Vec3[] places, float[] yaws, int[] edgeOf, string[] paths, Vec3[] starts, float[] startYaws, LineWorkspace work)
        {
            var batch = new FollowBatch(8);
            for (int e = 0; e < before.Length; e++)
            {
                batch.AddEdge(before[e]);
                batch.SetNow(e, after[e], true);
            }

            for (int i = 0; i < relations.Length; i++)
                batch.Add(edgeOf[i], relations[i], places[i], yaws[i], places[i], yaws[i]);
            var sb = new System.Text.StringBuilder();
            for (int round = 0; round < 40; round++)
            {
                batch.Run(FollowMode.Fraction, 0, relations.Length);
                for (int i = 0; i < relations.Length; i += 7)
                {
                    FollowOutcome o = batch.Result(i, out Vec3 at, out float yaw);
                    if (round == 39)
                        sb.Append((int)o).Append(':').Append(BitConverter.SingleToInt32Bits(at.X)).Append(':').Append(BitConverter.SingleToInt32Bits(at.Z)).Append(':').Append(BitConverter.SingleToInt32Bits(yaw)).Append(' ');
                }
            }

            for (int l = 0; l < paths.Length; l++)
            {
                for (int round = 0; round < 40; round++)
                {
                    FollowLineResult(before, after, paths[l], starts[l], startYaws[l], work, round == 39 ? sb : null);
                }
            }

            return sb.ToString();
        }

        private static void FollowLineResult(FollowCurve[] before, FollowCurve[] after, string path, Vec3 start, float yaw, LineWorkspace work, System.Text.StringBuilder sb)
        {
            if (!LineFollow.TryMeasure(path, start, yaw, before, before.Length, work, out LineRelation line))
                return;
            LineOutcome o = LineFollow.Apply(line, after, FollowMode.Fraction, 0f, work, out string text, out Vec3 at, out float turned, out _);
            sb?.Append((int)o).Append(':').Append(text).Append(':').Append(BitConverter.SingleToInt32Bits(at.X)).Append(':').Append(BitConverter.SingleToInt32Bits(turned)).Append(' ');
        }

        private static void Threads()
        {
            var random = new Random(31);
            const int edges = 6, items = 300, lines = 5;
            var curves = new Bezier3[edges];
            var after = new FollowCurve[edges];
            var before = new FollowCurve[edges];
            for (int e = 0; e < edges; e++)
            {
                curves[e] = RandomCurve(random, 300f, 0.1f, 80f);
                before[e] = new FollowCurve(curves[e]);
                after[e] = new FollowCurve(e % 2 == 0 ? Neighbour(curves[e], new Vec3(7f, 0.2f, -5f)) : Rigid(curves[e], new Vec3(9f, 0f, 2f), 0.3f));
            }

            var relations = new RoadRelation[items];
            var places = new Vec3[items];
            var yaws = new float[items];
            var edgeOf = new int[items];
            for (int i = 0; i < items; i++)
            {
                edgeOf[i] = i % edges;
                places[i] = RandomPlace(random, curves[edgeOf[i]]);
                yaws[i] = (float)(random.NextDouble() * 6 - 3);
                before[edgeOf[i]].TryMeasure(places[i], yaws[i], -1f, out relations[i], out _);
            }

            var paths = new string[lines];
            var starts = new Vec3[lines];
            var startYaws = new float[lines];
            for (int l = 0; l < lines; l++)
            {
                var pts = new List<Vec3>();
                FollowCurve table = before[l];
                for (int i = 0; i <= 24; i++)
                {
                    float t = table.TAt(table.Length * (0.05f + 0.9f * i / 24f));
                    table.At(t, out Vec3 on, out Vec3 h);
                    pts.Add(on + new Vec3(h.Z, 0f, -h.X) * 4f);
                }

                paths[l] = RunPath.Encode(pts, out startYaws[l]);
                starts[l] = pts[0];
            }

            // Lines on one road each: the tables of that road alone.
            string alone = Compute(before, after, relations, places, yaws, edgeOf, paths.Take(0).ToArray(), starts, startYaws, new LineWorkspace());
            string reference = Compute(before, after, relations, places, yaws, edgeOf, paths, starts, startYaws, new LineWorkspace());
            Program.Check(alone.Length > 100 && reference.Length > alone.Length, "the single-thread result is not empty");
            int failures = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var results = new string[8];
                var errors = new Exception[8];
                using (var barrier = new Barrier(8))
                {
                    var threads = new Thread[8];
                    for (int t = 0; t < 8; t++)
                    {
                        int n = t;
                        threads[t] = new Thread(() =>
                        {
                            try
                            {
                                var work = new LineWorkspace();
                                barrier.SignalAndWait();
                                results[n] = Compute(before, after, relations, places, yaws, edgeOf, paths, starts, startYaws, work);
                            }
                            catch (Exception ex)
                            {
                                errors[n] = ex;
                            }
                        });
                        threads[t].Start();
                    }

                    foreach (Thread thread in threads)
                        thread.Join();
                }

                for (int t = 0; t < 8; t++)
                {
                    if (errors[t] != null || results[t] != reference)
                        failures++;
                }
            }

            Program.Check(failures == 0, $"8 threads started together on the same tables give the single thread's numbers to the bit, 3 times: {failures} of 24 differ");
        }

        private static void NoStatics()
        {
            var bad = new List<string>();
            foreach (Type type in new[] { typeof(FollowCurve), typeof(FollowCache), typeof(FollowBatch), typeof(RigidMotion), typeof(LineFollow), typeof(LineRelation), typeof(LineWorkspace), typeof(FollowScheduler), typeof(RoadRelation), typeof(PathFollow) })
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    bool constant = field.IsLiteral;
                    bool readOnlyData = field.IsInitOnly && (field.FieldType.IsPrimitive || field.FieldType == typeof(string));
                    if (!constant && !readOnlyData)
                        bad.Add(type.Name + "." + field.Name);
                }
            }

            Program.Check(bad.Count == 0, "no static field that is not a constant or a read-only number or text: " + string.Join(", ", bad));
        }
    }
}
