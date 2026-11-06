using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// How fast the items follow a road that was dropped (0.12.83 follow work, the owner: "移动完道路之后，需要一段时间才能变过来"): the scheduler's tile order, the slices of a long line, the boost for what is
    /// near, the burst's timing line, the costs the pacing starts from and a simulation of a drop with the owner's real costs.
    /// </summary>
    internal static class FollowSpeedTests
    {
        public static void RunAll()
        {
            Program.Run("follow speed: the tiles are written one after another, the nearest tile first, and not item by item by their own distances", TileOrder);
            Program.Run("follow speed: the point looked at ranks a tile as the camera does", TileOrderCursor);
            Program.Run("follow speed: an item that has waited long is first in a tile order too, and an item with no tile is ranked by its own place", TileOrderAging);
            Program.Run("follow speed: a long line is taken a few items at a time, tile by tile, and a short one stays whole", Slices);
            Program.Run("follow speed: what is near has a budget over the base one and what is far has not", NearBoost);
            Program.Run("follow speed: the quiet frames, the burst of a tile and the number of a tile", QuietAndBurst);
            Program.Run("follow speed: the boost by the frame time and the frame time's average", Boost);
            Program.Run("follow speed: the timing line of a burst says where the frames' time went", Timing);
            Program.Run("follow speed: the costs the pacing starts from are the owner's, and a watch that is cleared keeps its options", Priors);
            Program.Run("follow speed: a move over the bound is told with the numbers it is made of", Descriptions);
            Program.Run("follow speed: an item past the end of a road that turns is within the bound by its distance along", BoundAlong);
            Program.Run("follow speed: a drop of 420 items with the owner's costs: near items shown sooner, no build thrown away, a frame no longer than its budget", Drop);
            Program.Run("follow speed: the pick of many waiting items costs little", PickCost);
        }

        private static FollowScheduler Tiled(params (int Item, int Group, float X, float Z)[] items)
        {
            var s = new FollowScheduler { TileOrder = true };
            foreach ((int item, int group, float x, float z) in items)
            {
                s.Add(item, group, x, z, 100f, FollowWatch<int, int>.TileOf(new Vec3(x, 0f, z)));
                s.Link(item, 1);
            }

            s.SetFocus(96f, 96f, false, 0f, 0f);
            s.EdgeChanged(1, 1, 0);
            return s;
        }

        private static string Order(FollowScheduler s, int frame = 0, double budget = 1e9, int cap = 100)
        {
            var jobs = new List<FollowJob>();
            s.Take(frame, budget, cap, jobs);
            return string.Join(",", jobs.Select(j => j.Item));
        }

        private static void TileOrder()
        {
            // The camera is in the tile (0, 0). Item 0 is just over the edge in the next tile (97 m from the camera, ring 2), 1 is in the corner of the camera's tile (135 m, ring 3), 2 is at the camera.
            string rings = Order(Plain((0, 193f, 96f), (1, 1f, 1f), (2, 96f, 96f)));
            Program.Check(rings == "2,0,1", "by their own distances the next tile's item comes before the corner of the camera's tile: " + rings);
            FollowScheduler tiles = Tiled((0, 0, 193f, 96f), (1, 0, 1f, 1f), (2, 0, 96f, 96f));
            Program.Check(Order(tiles) == "1,2,0", "by tiles the camera's tile is written whole (in the order its items came) before the next one: " + Order(Tiled((0, 0, 193f, 96f), (1, 0, 1f, 1f), (2, 0, 96f, 96f))));

            // Three tiles in a row: the ones farther along come last, whatever order their items came in.
            string row = Order(Tiled((0, 0, 500f, 96f), (1, 0, 300f, 96f), (2, 0, 100f, 96f), (3, 0, 520f, 90f), (4, 0, 310f, 100f), (5, 0, 110f, 100f)));
            Program.Check(row == "2,5,1,4,0,3", "tile by tile, nearest first, and in the order they came within a tile: " + row);

            // Six tiles in a row, the items came farthest first: every tile has its own place in the order, not only the first few.
            string six = Order(Tiled((0, 0, 1000f, 96f), (1, 0, 800f, 96f), (2, 0, 650f, 96f), (3, 0, 500f, 96f), (4, 0, 300f, 96f), (5, 0, 100f, 96f)));
            Program.Check(six == "5,4,3,2,1,0", "six tiles in a row are written nearest first: " + six);

            // A whole line is in the tile of its nearest item, and in the order the first of its items came: its far item came first and the single came second.
            string line = Order(Tiled((0, 7, 500f, 96f), (1, 0, 50f, 96f), (2, 7, 100f, 96f)));
            Program.Check(line == "0,2,1", "the line comes where its far item came, in the near tile, before the single that came after: " + line);

            // The budget cuts in the middle of a tile: the tile goes on next frame, and no other tile starts before it is done.
            FollowScheduler cut = Tiled((0, 0, 100f, 96f), (1, 0, 110f, 96f), (2, 0, 120f, 96f), (3, 0, 300f, 96f), (4, 0, 310f, 96f));
            var jobs = new List<FollowJob>();
            cut.Take(0, 200.0, 100, jobs);
            Program.Check(string.Join(",", jobs.Select(j => j.Item)) == "0,1", "a budget of two items: the first two of the first tile");
            foreach (FollowJob j in jobs)
                cut.Wrote(j.Item, j.Version);
            jobs.Clear();
            cut.Take(1, 200.0, 100, jobs);
            Program.Check(string.Join(",", jobs.Select(j => j.Item)) == "2,3", "and then the last of the tile and the first of the next, in the same order: " + string.Join(",", jobs.Select(j => j.Item)));

            // Not in a tile order the scheduler is as it was.
            Program.Check(new FollowScheduler().TileOrder == false && new FollowScheduler().AtomicGroupMax == 0 && new FollowScheduler().NearBoostNs == 0.0, "the options are off unless asked for");
        }

        private static void TileOrderCursor()
        {
            FollowScheduler Make(bool cursor)
            {
                var s = new FollowScheduler { TileOrder = true };
                // Pending order: the middle tile's item, the far tile's, the camera's own.
                foreach ((int item, float x) in new[] { (0, 300f), (1, 500f), (2, 96f) })
                {
                    s.Add(item, 0, x, 96f, 100f, FollowWatch<int, int>.TileOf(new Vec3(x, 0f, 96f)));
                    s.Link(item, 1);
                }

                s.SetFocus(96f, 96f, cursor, 500f, 96f);
                s.EdgeChanged(1, 1, 0);
                return s;
            }

            Program.Check(Order(Make(false)) == "2,0,1", "the camera alone: the tile it is in, the next, the one after: " + Order(Make(false)));
            Program.Check(Order(Make(true)) == "2,1,0", "the point looked at counts as the camera does: its tile comes before the one between: " + Order(Make(true)));
        }

        private static FollowScheduler Plain(params (int Item, float X, float Z)[] items)
        {
            var s = new FollowScheduler();
            foreach ((int item, float x, float z) in items)
            {
                s.Add(item, 0, x, z, 100f, FollowWatch<int, int>.TileOf(new Vec3(x, 0f, z)));
                s.Link(item, 1);
            }

            s.SetFocus(96f, 96f, false, 0f, 0f);
            s.EdgeChanged(1, 1, 0);
            return s;
        }

        private static void TileOrderAging()
        {
            var s = new FollowScheduler { TileOrder = true, MaxWaitFrames = 5 };
            s.Add(0, 0, 500f, 96f, 100f, FollowWatch<int, int>.TileOf(new Vec3(500f, 0f, 96f)));
            s.Link(0, 1);
            s.EdgeChanged(1, 1, 0);
            for (int i = 1; i <= 3; i++)
            {
                s.Add(i, 0, 96f, 96f, 100f, FollowWatch<int, int>.TileOf(new Vec3(96f, 0f, 96f)));
                s.Link(i, 2);
            }

            s.SetFocus(96f, 96f, false, 0f, 0f);
            s.EdgeChanged(2, 1, 9);
            Program.Check(Order(s, 10) == "0,1,2,3", "the far item has waited 10 frames, over the 5: it is first");
            var t = new FollowScheduler { TileOrder = true, MaxWaitFrames = 400 };
            t.Add(0, 0, 500f, 96f, 100f, FollowWatch<int, int>.TileOf(new Vec3(500f, 0f, 96f)));
            t.Link(0, 1);
            t.EdgeChanged(1, 1, 0);
            for (int i = 1; i <= 3; i++)
            {
                t.Add(i, 0, 96f, 96f, 100f, FollowWatch<int, int>.TileOf(new Vec3(96f, 0f, 96f)));
                t.Link(i, 2);
            }

            t.SetFocus(96f, 96f, false, 0f, 0f);
            t.EdgeChanged(2, 1, 9);
            Program.Check(Order(t, 10) == "1,2,3,0", "within 400 frames the near tile goes first, though the far item came first");

            // Items with no tile are ranked by their own place among the tiles.
            var u = new FollowScheduler { TileOrder = true };
            u.Add(0, 0, 300f, 96f, 100f, FollowWatch<int, int>.TileOf(new Vec3(300f, 0f, 96f)));
            u.Add(1, 0, 100f, 96f, 100f, -1);
            u.Add(2, 0, 600f, 96f, 100f, -1);
            for (int i = 0; i < 3; i++)
                u.Link(i, 1);
            u.SetFocus(96f, 96f, false, 0f, 0f);
            u.EdgeChanged(1, 1, 0);
            Program.Check(Order(u) == "1,0,2", "an item with no tile is ranked by its own distance, between the tiles");
        }

        private static void Slices()
        {
            // A line of 12 items in the camera's tile, and a single beside it.
            FollowScheduler Make(int max)
            {
                var s = new FollowScheduler { TileOrder = true, AtomicGroupMax = max };
                for (int i = 0; i < 12; i++)
                {
                    s.Add(i, 7, 20f + i * 10f, 96f, 100f, FollowWatch<int, int>.TileOf(new Vec3(20f + i * 10f, 0f, 96f)));
                    s.Link(i, 1);
                }

                s.Add(12, 0, 50f, 50f, 100f, FollowWatch<int, int>.TileOf(new Vec3(50f, 0f, 50f)));
                s.Link(12, 1);
                s.SetFocus(96f, 96f, false, 0f, 0f);
                s.EdgeChanged(1, 1, 0);
                return s;
            }

            var jobs = new List<FollowJob>();
            FollowScheduler whole = Make(0);
            whole.Take(0, 300.0, 100, jobs);
            Program.Check(jobs.Count == 12, "a line is whole in a frame when asked to be: " + jobs.Count);
            FollowScheduler sliced = Make(8);
            jobs.Clear();
            sliced.Take(0, 300.0, 100, jobs);
            Program.Check(string.Join(",", jobs.Select(j => j.Item)) == "0,1,2", "a long line is three items of a budget of three, in the order they came: " + string.Join(",", jobs.Select(j => j.Item)));
            foreach (FollowJob j in jobs)
                sliced.Wrote(j.Item, j.Version);
            jobs.Clear();
            sliced.Take(1, 300.0, 100, jobs);
            Program.Check(string.Join(",", jobs.Select(j => j.Item)) == "3,4,5", "and the next three the next frame: " + string.Join(",", jobs.Select(j => j.Item)));

            // At least one item is taken whatever the budget.
            FollowScheduler tiny = Make(8);
            jobs.Clear();
            tiny.Take(0, 1.0, 100, jobs);
            Program.Check(jobs.Count == 1 && jobs[0].Item == 0, "a budget of nothing takes the first item");

            // A line of four is whole as before, though the limit is eight.
            var shortLine = new FollowScheduler { TileOrder = true, AtomicGroupMax = 8 };
            for (int i = 0; i < 4; i++)
            {
                shortLine.Add(i, 7, 50f + i, 50f, 100f, FollowWatch<int, int>.TileOf(new Vec3(50f, 0f, 50f)));
                shortLine.Link(i, 1);
            }

            shortLine.SetFocus(96f, 96f, false, 0f, 0f);
            shortLine.EdgeChanged(1, 1, 0);
            jobs.Clear();
            shortLine.Take(0, 100.0, 100, jobs);
            Program.Check(jobs.Count == 4, "a line of four is taken whole: " + jobs.Count);

            // Eight items are a whole line, nine are cut: the size of the line, not what is left of it.
            foreach ((int size, int expected) in new[] { (8, 8), (9, 3) })
            {
                var edge = new FollowScheduler { TileOrder = true, AtomicGroupMax = 8 };
                for (int i = 0; i < size; i++)
                {
                    edge.Add(i, 7, 50f + i, 50f, 100f, FollowWatch<int, int>.TileOf(new Vec3(50f, 0f, 50f)));
                    edge.Link(i, 1);
                }

                edge.SetFocus(96f, 96f, false, 0f, 0f);
                edge.EdgeChanged(1, 1, 0);
                jobs.Clear();
                edge.Take(0, 300.0, 100, jobs);
                Program.Check(jobs.Count == expected, $"a line of {size} items with a budget of three: {jobs.Count}, expected {expected}");
            }

            // The rest of a long line is still cut when only a few of its items are left.
            var rest = new FollowScheduler { TileOrder = true, AtomicGroupMax = 8 };
            for (int i = 0; i < 12; i++)
            {
                rest.Add(i, 7, 50f + i, 50f, 100f, FollowWatch<int, int>.TileOf(new Vec3(50f, 0f, 50f)));
                rest.Link(i, 1);
            }

            rest.SetFocus(96f, 96f, false, 0f, 0f);
            rest.EdgeChanged(1, 1, 0);
            for (int frame = 0; frame < 3; frame++)
            {
                jobs.Clear();
                rest.Take(frame, 100.0, 100, jobs);
                foreach (FollowJob j in jobs)
                    rest.Wrote(j.Item, j.Version);
            }

            Program.Check(rest.PendingCount == 9, "three frames of one item each leave nine of twelve: " + rest.PendingCount);
            jobs.Clear();
            rest.Take(3, 100.0, 100, jobs);
            Program.Check(jobs.Count == 1, "and a line with six left is still taken one item at a time: " + jobs.Count);

            // A long line goes tile by tile with the rest: its far members are not pulled to the near tile, which they were as a whole line.
            FollowScheduler Spread(int max)
            {
                var s = new FollowScheduler { TileOrder = true, AtomicGroupMax = max };
                for (int i = 0; i < 10; i++)
                {
                    float x = i == 0 ? 96f : 500f + i;
                    s.Add(i, 7, x, 96f, 100f, FollowWatch<int, int>.TileOf(new Vec3(x, 0f, 96f)));
                    s.Link(i, 1);
                }

                s.Add(10, 0, 300f, 96f, 100f, FollowWatch<int, int>.TileOf(new Vec3(300f, 0f, 96f)));
                s.Link(10, 1);
                s.SetFocus(96f, 96f, false, 0f, 0f);
                s.EdgeChanged(1, 1, 0);
                return s;
            }

            Program.Check(Order(Spread(0)) == "0,1,2,3,4,5,6,7,8,9,10", "a whole line goes where its nearest item is: " + Order(Spread(0)));
            Program.Check(Order(Spread(8)) == "0,10,1,2,3,4,5,6,7,8,9", "a long line's far items wait for the tiles between: " + Order(Spread(8)));
        }

        private static void NearBoost()
        {
            FollowScheduler Make(int near, int far)
            {
                var s = new FollowScheduler { NearMetres = 200f };
                int id = 0;
                for (int i = 0; i < near; i++, id++)
                {
                    s.Add(id, 0, 10f + i, 0f, 100f);
                    s.Link(id, 1);
                }

                for (int i = 0; i < far; i++, id++)
                {
                    s.Add(id, 0, 1000f + i, 0f, 100f);
                    s.Link(id, 1);
                }

                s.SetFocus(0f, 0f, false, 0f, 0f);
                s.EdgeChanged(1, 1, 0);
                return s;
            }

            var jobs = new List<FollowJob>();
            FollowScheduler a = Make(10, 10);
            a.Take(0, 300.0, 100, jobs);
            Program.Check(jobs.Count == 3, "no boost: the base budget, three items");
            FollowScheduler b = Make(10, 10) ;
            b.NearBoostNs = 500.0;
            jobs.Clear();
            b.Take(0, 300.0, 100, jobs);
            Program.Check(jobs.Count == 8 && jobs.All(j => j.Item < 10), "a boost of 500 over 300: eight near items: " + jobs.Count);
            FollowScheduler c = Make(2, 10);
            c.NearBoostNs = 500.0;
            jobs.Clear();
            c.Take(0, 300.0, 100, jobs);
            Program.Check(jobs.Count == 3 && jobs.Count(j => j.Item < 2) == 2, "two near items and then the base budget for the far ones, not the boost: " + jobs.Count);
            FollowScheduler d = Make(0, 10);
            d.NearBoostNs = 500.0;
            jobs.Clear();
            d.Take(0, 300.0, 100, jobs);
            Program.Check(jobs.Count == 3, "nothing near: the base budget");
            FollowScheduler e = Make(10, 0);
            e.NearBoostNs = 500.0;
            jobs.Clear();
            e.Take(0, 300.0, 5, jobs);
            Program.Check(jobs.Count == 5, "the cap holds the boost too");

            // Exactly at the distance is near; a little farther is not.
            foreach ((float x, int expected) in new[] { (200f, 4), (200.5f, 1) })
            {
                var edge = new FollowScheduler { NearMetres = 200f, NearBoostNs = 300.0 };
                for (int i = 0; i < 6; i++)
                {
                    edge.Add(i, 0, x, 0f, 100f);
                    edge.Link(i, 1);
                }

                edge.SetFocus(0f, 0f, false, 0f, 0f);
                edge.EdgeChanged(1, 1, 0);
                jobs.Clear();
                edge.Take(0, 100.0, 100, jobs);
                Program.Check(jobs.Count == expected, $"an item at {x} m with NearMetres 200: {jobs.Count} taken, expected {expected}");
            }

            // The edge of near: the tile's nearest edge in a tile order, an item's own place otherwise.
            var tiles = new FollowScheduler { TileOrder = true, NearMetres = 100f };
            tiles.Add(0, 0, 150f, 96f, 100f, FollowWatch<int, int>.TileOf(new Vec3(150f, 0f, 96f)));
            tiles.Add(1, 0, 400f, 96f, 100f, FollowWatch<int, int>.TileOf(new Vec3(400f, 0f, 96f)));
            tiles.Link(0, 1);
            tiles.Link(1, 1);
            tiles.SetFocus(96f, 96f, false, 0f, 0f);
            tiles.EdgeChanged(1, 1, 0);
            tiles.NearBoostNs = 1000.0;
            jobs.Clear();
            tiles.Take(0, 100.0, 100, jobs);
            Program.Check(jobs.Count == 1 && jobs[0].Item == 0, "the camera's own tile is near, the tile 200 m on is not: " + jobs.Count);
        }

        private static void QuietAndBurst()
        {
            var s = new FollowScheduler { TileGateFrames = 10 };
            s.Add(0, 0, 0f, 0f, 100f, 5);
            s.Link(0, 1);
            Program.Check(s.QuietFrames(100) > 1000000, "before any event the road has been quiet for ever");
            s.EdgeChanged(2, 1, 50);
            Program.Check(s.QuietFrames(60) > 1000000, "an event for a road with no items is no event");
            s.EdgeChanged(1, 1, 50);
            Program.Check(s.QuietFrames(50) == 0 && s.QuietFrames(53) == 3, "an event reaches an item: quiet is counted from it");
            s.EdgeChanged(1, 2, 54);
            Program.Check(s.QuietFrames(54) == 0, "and from the last one");

            // A tile is being written while a burst is on.
            var t = new FollowScheduler { TileGateFrames = 10 };
            for (int i = 0; i < 6; i++)
            {
                t.Add(i, 0, i, 0f, 100f, 77);
                t.Link(i, 1);
            }

            t.EdgeChanged(1, 1, 0);
            Program.Check(!t.IsBursting(77), "nothing written, no burst");
            var jobs = new List<FollowJob>();
            t.Take(0, 250.0, 100, jobs);
            Program.Check(jobs.Count == 2 && t.IsBursting(77), "two of six: the burst is on");
            foreach (FollowJob j in jobs)
                t.Wrote(j.Item, j.Version);
            jobs.Clear();
            t.Take(1, 250.0, 100, jobs);
            Program.Check(t.IsBursting(77), "four of six: still");
            foreach (FollowJob j in jobs)
                t.Wrote(j.Item, j.Version);
            jobs.Clear();
            t.Take(2, 250.0, 100, jobs);
            Program.Check(!t.IsBursting(77) && t.PendingCount == 0, "all six: the burst is over");
            Program.Check(!t.IsBursting(5), "a tile that was never written is not bursting");

            // The number of a tile and its corner.
            var random = new Random(9);
            bool inverse = true;
            for (int k = 0; k < 500; k++)
            {
                int x = random.Next(-300, 300), z = random.Next(-300, 300);
                var key = new TileKey(x, z);
                FollowScheduler.TileOrigin(FollowWatch<int, int>.TileOf(key), out float ox, out float oz);
                inverse &= Math.Abs(ox - x * Tiles.Size) < 1e-3f && Math.Abs(oz - z * Tiles.Size) < 1e-3f;
                float px = (x + 0.5f) * Tiles.Size, pz = (z + 0.25f) * Tiles.Size;
                inverse &= FollowWatch<int, int>.TileOf(new Vec3(px, 0f, pz)) == FollowWatch<int, int>.TileOf(key);
            }

            Program.Check(inverse, "the corner of a tile from its number is where the tile starts, and a place in the tile has its number");
        }

        private static void Boost()
        {
            Program.Check(FollowGate.BoostQuietFrames == 3, "a road is quiet after three frames");
            Program.Check(FollowGate.BoostMs(0, 15.0) == 0.0 && FollowGate.BoostMs(2, 15.0) == 0.0, "a road that was changed this frame or the one before has no boost");
            Program.Check(FollowGate.BoostMs(3, 15.0) == 2.0 && FollowGate.BoostMs(1000, 22.0) == 2.0, "quiet and 45 frames a second or better: 2 ms");
            Program.Check(FollowGate.BoostMs(3, 22.1) == 1.0 && FollowGate.BoostMs(3, 40.0) == 1.0, "down to 25 frames a second: 1 ms");
            Program.Check(FollowGate.BoostMs(3, 40.1) == 0.0 && FollowGate.BoostMs(3, 100.0) == 0.0, "a slow game gets none");
            Program.Check(FollowGate.BoostMs(3, 0.0) == 0.0 && FollowGate.BoostMs(3, -1.0) == 0.0 && FollowGate.BoostMs(3, double.NaN) == 0.0, "no frame time, no boost");
            Program.Check(FollowGate.NextFrameMs(0.0, 20.0) == 20.0, "the first frame is the average");
            Program.Near((float)FollowGate.NextFrameMs(20.0, 30.0), 22f, 1e-4f, "a fifth of the new frame");
            double average = 0.0;
            for (int i = 0; i < 100; i++)
                average = FollowGate.NextFrameMs(average, 16.0);
            Program.Near((float)average, 16f, 1e-3f, "and it settles on the frame time");
        }

        private static void Timing()
        {
            var r = new FollowReport();
            r.NoteFrame(10.0, 100, 5.00, 1.0, 6.0, 0, true);
            r.NoteFrame(30.0, 104, 5.07, 2.0, 20.0, 1, false);
            r.NoteFrame(12.0, 105, 5.09, 0.5, 8.0, 0, true);
            Program.Check(r.Frames == 3 && Math.Abs(r.Ms - 52.0) < 1e-9 && r.WorstFrameMs == 30.0, "the frames and their time");
            Program.Check(Math.Abs(r.TakeMs - 3.5) < 1e-9 && Math.Abs(r.WriteMs - 34.0) < 1e-9 && r.Collections == 1 && r.BoostedFrames == 2, "the pick, the writing, the collections and the boosted frames");
            Program.Check(r.WorstTakeMs == 2.0 && r.WorstWriteMs == 20.0 && r.WorstCollections == 1, "the worst frame's own parts, not the last frame's");
            Program.Check(r.SpanFrames == 6 && r.FirstFrame == 100 && r.LastFrame == 105 && Math.Abs(r.LastSeconds - r.FirstSeconds - 0.09) < 1e-9, "from the first frame with work to the last");
            r.BuildsDropped = 4;
            r.BuildsAccepted = 7;
            r.PhasesWritten = 9;
            r.RunsNotRebased = 2;
            string line = r.FormatTiming();
            Program.Check(line.StartsWith("Follow (timing): "), "it is a line of its own");
            Program.Check(line.Contains("last 6 frame(s), 0.09 s; 3 of them with work (2 boosted), 3 waiting"), "the span and the frames with and without work: " + line);
            Program.Check(line.Contains("took 52.0 ms: pick 3.5, writing the items 34.0, the follow and the rest 14.5"), "where the time went: " + line);
            Program.Check(line.Contains("1 garbage collection(s) in them; the worst frame 30.0 ms (pick 2.0, writing 20.0, 1 collection(s)); the renderer threw 4 tile build(s) away and finished 7; 9 piece(s) of walls had their start along the line made again, 2 run(s) were not laid end to end and were left."), "the worst frame, the renderer's builds and the walls' phases: " + line);
            Program.Check(new FollowReport().SpanFrames == 0, "no frame, no span");
            var plain = new FollowReport();
            plain.NoteFrame(5.0);
            Program.Check(plain.Frames == 1 && plain.SpanFrames == 0 && plain.TakeMs == 0.0, "the old note of a frame leaves the new parts alone");
        }

        private static void Priors()
        {
            Program.Check(FollowWatch<int, int>.SingleNs == 100000f && FollowWatch<int, int>.LineNs == 150000f, "a single item costs 100 us and a line item 150 us until the frames say otherwise (his log: 130 us an item)");
            Program.Check(FollowGate.PacedBudgetNs(0.5, 1.0) / FollowWatch<int, int>.SingleNs == 5.0, "the base budget of 0.5 ms is five single items at the first frame, not two hundred");
            var watch = new FollowWatch<int, int>();
            watch.Scheduler.TileGateFrames = 17;
            watch.Scheduler.MaxWaitFrames = 400;
            watch.Scheduler.TileOrder = true;
            watch.Scheduler.AtomicGroupMax = 8;
            watch.Scheduler.NearMetres = 250f;
            watch.Clear();
            FollowScheduler s = watch.Scheduler;
            Program.Check(s.TileGateFrames == 17 && s.MaxWaitFrames == 400 && s.TileOrder && s.AtomicGroupMax == 8 && s.NearMetres == 250f, "a city that is loaded keeps the scheduler's options");
            Program.Check(watch.LastTakeMs == 0.0, "the pick's time is not known before a frame");
        }

        private static void Descriptions()
        {
            string single = FollowReport.DescribeSingle("Entity(1:2)", 0.7f, 3.5f, 1.26f, 0.5f);
            Program.Check(single == "Entity(1:2) (the road's control points moved 0.70 m, the item is 3.5 m across and 1.3 m along, it turned 29 degrees)", single);
            string line = FollowReport.DescribeLine("Entity(3:4)", 2.5f, 6.0f, -1.0f, 1.2f);
            Program.Check(line == "Entity(3:4) (a line: the roads' control points moved 2.50 m, up to 6.0 m from the road, it turned -57 degrees, its length changed to 1.20 times)", line);
            var report = new FollowReport();
            report.AddSingle(FollowOutcome.Moved, 5f, 1f, single);
            report.AddSingle(FollowOutcome.Moved, 1f, 5f, null);
            Program.Check(report.OverBound == 1 && report.Warnings.Count == 1 && report.Warnings[0].Contains("the road's control points moved 0.70 m"), "the warning carries the description of the item that was over");
        }

        private static void BoundAlong()
        {
            // A road of 100 m turned 0.4 radians about its start: its end moves 39.7 m, an item 4 m past the end 41.3 m. Without the 4 m the bound is 40.7 m and the item is called a surprise.
            Bezier3 road = Bezier3.Line(new Vec3(100f, 0f, 0f), new Vec3(100f, 0f, 100f)), current = road;
            var cache = new FollowCache();
            var at = new Vec3(100f, 0f, 104f);
            var watch = new FollowWatch<int, int> { Writing = true };
            Func<int, FollowCurve> table = edge => cache.Get(current);
            FollowWatch<int, int>.PlaceOf placeOf = (int item, bool wantPath, out Vec3 position, out float yaw, out string path) =>
            {
                position = at;
                yaw = 0f;
                path = null;
                return true;
            };
            Program.Check(watch.LinkSingle(1, 1, table(1), at, 0f), "an item 4 m past the end is linked");
            current = FollowFixture.Rigid(road, Vec3.Zero, 0.4f);
            watch.Dirty(1);
            var writes = new List<FollowWrite<int>>();
            watch.StepWrite(table, placeOf, FollowMode.Fraction, 0, 1e9, 200, writes);
            watch.NoteFrame(0.1);
            FollowReport report = watch.Finished();
            Program.Check(writes.Count == 1 && report != null && report.Written == 1, "it is written");
            float move = (writes[0].Position - at).Length, bare = FollowGate.ControlMove(road, current) + FollowGate.Slack;
            Program.Check(move > bare + 0.3f, $"it moved {move:0.00} m, more than the road's own change and the slack ({bare:0.00} m): the case is one where the distance along matters");
            Program.Check(report.OverBound == 0, $"and it is within the bound that counts its distance along: {report.OverBound} over");
        }

        private static void Drop()
        {
            FollowDropSim.World w = FollowDropSim.Build(focusZ: 350f);
            var costs = new FollowDropSim.Costs();
            FollowDropSim.Policy[] policies = FollowDropSim.Policies();
            FollowDropSim.Policy before = policies[0], production = policies[policies.Length - 1];
            double frame = 1000.0 / 66.0;
            FollowDropSim.Result was = FollowDropSim.Run(w, before, costs, frame), now = FollowDropSim.Run(w, production, costs, frame);
            Program.Check(was.MovedCount > 200 && was.NearCount > 20, "a city of items that move, some of them near what is looked at: " + was.MovedCount + ", " + was.NearCount);
            Program.Check(now.NearShown[2] <= 48 && now.NearShown[2] < was.NearShown[2], $"the near items are all shown within 48 frames (0.73 s at 66 a second) and sooner than before: {now.NearShown[2]} against {was.NearShown[2]}");
            Program.Check(now.NearWritten[2] <= 24 && now.NearWritten[2] < was.NearWritten[2] * 0.6, $"and written within 24 frames, in well under the time: {now.NearWritten[2]} against {was.NearWritten[2]}");
            Program.Check(now.Dropped == 0 && was.Dropped > 0, $"no tile build is thrown away when the renderer waits for the follow: {now.Dropped} against {was.Dropped}");
            Program.Check(now.Builds < was.Builds, $"and fewer are made: {now.Builds} against {was.Builds}");
            Program.Check(now.WorstFollowMs <= 3.6 && now.WorstFollowMs < was.WorstFollowMs * 0.6, $"no frame of the follow is longer than the boost and a wall: {now.WorstFollowMs:0.0} ms against {was.WorstFollowMs:0.0}");
            Program.Check(now.AllShown[2] <= 150 && now.AllShown[2] <= was.AllShown[2] && now.AllWritten[2] <= 120, $"and every item is shown in the end, no later than before: {now.AllWritten[2]} written, {now.AllShown[2]} shown frames against {was.AllShown[2]}");
            Program.Check(now.MeanFollowMs < 2.0, $"and a frame with work takes 2 ms at most on average: {now.MeanFollowMs:0.00} ms (a wall is followed whole, so that its pieces' phases are made again in one go)");

            // A slow game gets no boost: the same as without it.
            FollowDropSim.Policy noBoost = FollowDropSim.Production("no boost", 0.0);
            FollowDropSim.Result slowA = FollowDropSim.Run(w, production, costs, 1000.0 / 20.0), slowB = FollowDropSim.Run(w, noBoost, costs, 1000.0 / 20.0);
            Program.Check(slowA.NearShown.SequenceEqual(slowB.NearShown) && slowA.AllShown.SequenceEqual(slowB.AllShown), "at 20 frames a second the boost does nothing");
            FollowDropSim.Result again = FollowDropSim.Run(w, production, costs, frame);
            Program.Check(again.NearShown.SequenceEqual(now.NearShown) && again.AllShown.SequenceEqual(now.AllShown) && again.Builds == now.Builds, "the same drop gives the same schedule");
        }

        private static void PickCost()
        {
            double fewer = FollowDropSim.TakeMicroseconds(2000, false), more = FollowDropSim.TakeMicroseconds(8000, true);
            // A frame is 15,000 us and the budget 500: the pick is not counted in it, so it must stay a small part of it (the game's Mono is slower than this).
            Program.Check(fewer < 1500.0 && more < 3000.0, $"a pick with 2,000 items waiting {fewer:0} us, with 8,000 {more:0} us");
        }
    }
}
