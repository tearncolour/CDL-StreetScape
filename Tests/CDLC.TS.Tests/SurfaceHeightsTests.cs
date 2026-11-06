using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The heights a barrier's line stands on, read window by window and kept (0.12.21, SurfaceHeights): the roads are read once for a line that does not change, and a line whose end follows the cursor is not read again at every step.</summary>
    internal static class SurfaceHeightsTests
    {
        public static void RunAll()
        {
            Program.Run("surface heights: a window read is kept", Kept);
            Program.Run("surface heights: an end that moves along is not read at every step", Moving);
            Program.Run("surface heights: the terrain where there is no road, and what is kept is by key", Terrain);
            Program.Run("surface heights: a stretch with no surface between two that have is not the terrain's (0.12.60)", Holes);
            Program.Run("surface heights: a short stretch at the start or the end goes on at the surface's grade", EdgeHoles);
            Program.Run("surface heights: a foot just outside the surface takes its height, not the terrain's (0.12.63)", NearEdge);
            Program.Run("surface heights: a foot at a deck's edge keeps the deck's height over a road that crosses under it (0.12.68)", NearUnderpass);
        }

        /// <summary>
        /// The road of Road with places left out (x ranges with no surface), and terrain 2 m under it, within the 3 m a line may step down to it: what the owner's ramp joint was (the line dipped to the
        /// ground where the composition changed, and came up again).
        /// </summary>
        private sealed class Gapped
        {
            private readonly List<SurfaceStrip> m_Strips = new List<SurfaceStrip>();

            public Gapped(params float[] ranges)
            {
                for (int r = 0; r + 1 < ranges.Length; r += 2)
                {
                    var strip = new SurfaceStrip();
                    for (float x = ranges[r]; x <= ranges[r + 1] + 0.01f; x += 2f)
                    {
                        strip.Add(new Vec3(x, Road.Height(x), -5f), new Vec3(x, Road.Height(x), 5f));
                    }

                    m_Strips.Add(strip);
                }
            }

            public AreaFit Read(float minX, float minZ, float maxX, float maxZ, float originY) =>
                AreaFit.FromStrips(m_Strips, new Vec3(0f, originY, 0f), 0f, minX, maxX, minZ, maxZ);

            /// <summary>The first of the strips, for a test that joins it with another surface.</summary>
            public SurfaceStrip Strip => m_Strips[0];

            public static float Under(float x, float y, float z) => Road.Height(x) - 2f;
        }

        private static float Worst(List<Vec3> line, float from, float to)
        {
            float worst = 0f;
            foreach (Vec3 p in line)
            {
                if (p.X >= from && p.X <= to)
                    worst = Math.Max(worst, Math.Abs(p.Y - Road.Height(p.X)));
            }

            return worst;
        }

        private static void Holes()
        {
            // A 6 m hole in the road at 100 m: the points in it are on the road's plane, not 2 m down in the ground.
            var road = new Gapped(-100f, 99f, 107f, 300f);
            var heights = new SurfaceHeights();
            List<Vec3> line = Line(150f);
            List<Vec3> put = heights.Put(line, "hole", line.Count, road.Read, Gapped.Under);
            Program.Check(Worst(put, 0f, 150f) < 0.002f, $"the line over a 6 m hole stands on the road's plane all the way: {Worst(put, 0f, 150f) * 1000f:0.0} mm off");

            // A hole across the boundary of two windows (the 30th point is at 58 m): windows are read apart and the hole is filled on the whole line.
            var across = new Gapped(-100f, 55f, 65f, 300f);
            List<Vec3> over = new SurfaceHeights().Put(line, "across", line.Count, across.Read, Gapped.Under);
            Program.Check(Worst(over, 0f, 150f) < 0.002f, $"a hole where two windows meet is filled too: {Worst(over, 0f, 150f) * 1000f:0.0} mm off");

            // A line of paint (bridge: false) is as it was: the terrain's where there is no surface (0.12.32 laid it so, and the owner's report was of a wall).
            List<Vec3> paint = new SurfaceHeights().Put(line, "paint", line.Count, road.Read, Gapped.Under, bridge: false);
            bool dipped = false;
            foreach (Vec3 p in paint)
                dipped |= p.X > 100f && p.X < 106f && Math.Abs(p.Y - (Road.Height(p.X) - 2f)) < 0.002f;
            Program.Check(dipped && Worst(paint, 0f, 98f) < 0.002f, "a line of paint over the hole has the terrain's heights in it, and the road's outside it");

            // A hole of 40 m is a gap in the road and not a joint: the terrain under it.
            var wide = new Gapped(-100f, 59f, 101f, 300f);
            List<Vec3> gap = new SurfaceHeights().Put(line, "wide", line.Count, wide.Read, Gapped.Under);
            bool ground = true;
            foreach (Vec3 p in gap)
            {
                if (p.X > 62f && p.X < 98f)
                    ground &= Math.Abs(p.Y - (Road.Height(p.X) - 2f)) < 0.002f;
            }

            Program.Check(ground, $"a hole longer than {SurfaceHeights.MaxHole:0} m keeps the terrain's heights");

            // The line is not on the road anywhere (beside it): nothing to join, the terrain's as before.
            List<Vec3> beside = Line(60f, 50f);
            List<Vec3> off = new SurfaceHeights().Put(beside, "beside", beside.Count, new Gapped(-100f, 300f).Read, Gapped.Under);
            bool terrain = true;
            foreach (Vec3 p in off)
                terrain &= Math.Abs(p.Y - (Road.Height(p.X) - 2f)) < 0.002f;
            Program.Check(terrain, "a line beside the road has the terrain's heights");

            // Kept windows keep which of their points had a surface: the same line put again comes out the same.
            List<Vec3> again = heights.Put(line, "hole", line.Count, road.Read, Gapped.Under);
            bool same = again.Count == put.Count;
            for (int i = 0; same && i < again.Count; i++)
                same = again[i].Y == put[i].Y;
            Program.Check(same, "put again (the windows kept) the line is the same");
        }

        private static void NearUnderpass()
        {
            // A deck (z -5 to 5) with a road under it that runs across (x 38 to 50, 8 m lower), and a line 0.2 m outside the deck's edge, over a parapet a post does not stand on: the road under the line is
            // 0 m from it and the deck's own piece 0.2 m, and the wall took the road's height (the owner's, 1 Nov 22:27: it dived 8.5 m at an underpass and came up again).
            var strips = new List<SurfaceStrip>();
            var deck = new SurfaceStrip();
            for (float x = -100f; x <= 300f; x += 10f)
            {
                deck.Add(new Vec3(x, Road.Height(x), -5f), new Vec3(x, Road.Height(x), 5f));
            }

            var under = new SurfaceStrip();
            for (float z = -30f; z <= 30f; z += 10f)
            {
                under.Add(new Vec3(38f, Road.Height(44f) - 8f, z), new Vec3(50f, Road.Height(44f) - 8f, z));
            }

            strips.Add(deck);
            strips.Add(under);
            SurfaceHeights.ReadSurface read = (minX, minZ, maxX, maxZ, originY) => AreaFit.ForHeights(strips, new Vec3(0f, originY, 0f), 0f, minX, maxX, minZ, maxZ);
            List<Vec3> line = Line(100f, 5.2f);
            List<Vec3> put = new SurfaceHeights().Put(line, "under", line.Count, read, Gapped.Under);
            float worst = Worst(put, 0f, 100f);
            Program.Check(worst < 0.002f, $"a foot 0.2 m outside a deck keeps the deck's height over a road that crosses under it: {worst * 1000f:0.0} mm off");

            // The same line right over the road under, with no deck piece near it, has no surface at the line's height: the terrain's, as for any line off a surface.
            List<Vec3> away = Line(100f, 9f);
            List<Vec3> ground = new SurfaceHeights().Put(away, "away", away.Count, read, Gapped.Under);
            bool onGround = true;
            foreach (Vec3 p in ground)
                onGround &= Math.Abs(p.Y - (Road.Height(p.X) - 2f)) < 0.002f;
            Program.Check(onGround, "4 m outside the deck the line is on the ground, over the road under it too");
        }

        private static void NearEdge()
        {
            // The road is 10 m wide (z -5 to 5) and the terrain 2 m under it, as beside a deck: a line 1.2 m outside its edge (z 6.2) and one 4 m outside (z 9).
            var road = new Gapped(-100f, 300f);
            List<Vec3> close = new SurfaceHeights().Put(Line(100f, 6.2f), "close", Line(100f, 6.2f).Count, road.Read, Gapped.Under);
            Program.Check(Worst(close, 0f, 100f) < 0.002f, $"1.2 m outside the edge the line stands at the road's height: {Worst(close, 0f, 100f) * 1000f:0.0} mm off");

            List<Vec3> edge = new SurfaceHeights().Put(Line(100f, 7.4f), "edge", Line(100f, 7.4f).Count, road.Read, Gapped.Under);
            Program.Check(Worst(edge, 0f, 100f) < 0.002f, $"2.4 m outside it still does (NearSurface {SurfaceHeights.NearSurface:0.0} m): {Worst(edge, 0f, 100f) * 1000f:0.0} mm off");

            List<Vec3> far = new SurfaceHeights().Put(Line(100f, 9f), "far", Line(100f, 9f).Count, road.Read, Gapped.Under);
            bool ground = true;
            foreach (Vec3 p in far)
                ground &= Math.Abs(p.Y - (Road.Height(p.X) - 2f)) < 0.002f;
            Program.Check(ground, "4 m outside it the line is on the ground");

            // A line of paint is as it was: it has no use of the near surface.
            List<Vec3> paint = new SurfaceHeights().Put(Line(100f, 6.2f), "paint", Line(100f, 6.2f).Count, road.Read, Gapped.Under, bridge: false);
            bool lowered = true;
            foreach (Vec3 p in paint)
                lowered &= Math.Abs(p.Y - (Road.Height(p.X) - 2f)) < 0.002f;
            Program.Check(lowered, "paint beside the road keeps the terrain's height");

            // The nearest of two surfaces is the one taken: a deck 6 m up on the right of the line and the road on its left.
            var deck = new SurfaceStrip();
            for (float x = -100f; x <= 300f; x += 10f)
            {
                deck.Add(new Vec3(x, 6f, 7f), new Vec3(x, 6f, 12f));
            }

            AreaFit Both(float minX, float minZ, float maxX, float maxZ, float originY)
            {
                var strips = new List<SurfaceStrip> { new Gapped(-100f, 300f).Strip, deck };
                return AreaFit.FromStrips(strips, new Vec3(0f, originY, 0f), 0f, minX, maxX, minZ, maxZ);
            }

            List<Vec3> between = new SurfaceHeights().Put(Line(100f, 5.9f), "between", Line(100f, 5.9f).Count, Both, Gapped.Under);
            Program.Check(Worst(between, 0f, 100f) < 0.002f, "a foot 0.9 m from the road and 1.1 m from a deck above it takes the road's height (the nearer, and within the rise of the item)");
        }

        private static void EdgeHoles()
        {
            // The road starts 8 m along the line (a junction's part that does not reach the start): the first points go on at its grade.
            var late = new Gapped(8f, 300f);
            List<Vec3> line = Line(100f);
            List<Vec3> put = new SurfaceHeights().Put(line, "late", line.Count, late.Read, Gapped.Under);
            Program.Check(Worst(put, 0f, 100f) < 0.002f, $"a start 8 m short of the road is on the road's plane: {Worst(put, 0f, 100f) * 1000f:0.0} mm off");

            // 20 m short is the ground beside a road that begins there, not a joint.
            var later = new Gapped(20f, 300f);
            List<Vec3> far = new SurfaceHeights().Put(line, "later", line.Count, later.Read, Gapped.Under);
            bool ground = true;
            foreach (Vec3 p in far)
            {
                if (p.X < 18f)
                    ground &= Math.Abs(p.Y - (Road.Height(p.X) - 2f)) < 0.002f;
            }

            Program.Check(ground, $"a start more than {SurfaceHeights.MaxEdgeHole:0} m short of the road keeps the terrain's heights");

            // The road ends 6 m before the end of the line.
            var early = new Gapped(-100f, 94f);
            List<Vec3> tail = new SurfaceHeights().Put(line, "early", line.Count, early.Read, Gapped.Under);
            Program.Check(Worst(tail, 0f, 100f) < 0.002f, $"an end 6 m past the road goes on at its grade: {Worst(tail, 0f, 100f) * 1000f:0.0} mm off");

            // A steep surface is not carried on at its steepness: the grade of a filled stretch is at most 15 %.
            var steep = new SurfaceStrip();
            for (float x = 10f; x <= 100f; x += 2f)
            {
                steep.Add(new Vec3(x, 0.5f * (x - 10f), -5f), new Vec3(x, 0.5f * (x - 10f), 5f));
            }

            AreaFit Read(float minX, float minZ, float maxX, float maxZ, float originY) =>
                AreaFit.FromStrips(new List<SurfaceStrip> { steep }, new Vec3(0f, originY, 0f), 0f, minX, maxX, minZ, maxZ);
            List<Vec3> ramp = new SurfaceHeights().Put(Line(100f), "steep", Line(100f).Count, Read, NoGround);
            // The point at 8 m is within NearSurface of the slope's foot (10 m) and takes its plane (-1 m); the ones before it go back from there at 15 %: -1 - 0.15 x 8.
            Program.Check(Math.Abs(ramp[0].Y - (-2.2f)) < 0.01f, $"the first point 10 m before a 50 % slope is {ramp[0].Y:0.00} m (carried back at 15 % from the first point near the slope: -2.2 m, not -5 m)");
        }

        /// <summary>A road 10 m wide along X from -100 to 300 m, rising a millimetre a metre from 5 cm; its reader counts the reads.</summary>
        private sealed class Road
        {
            public int Reads;
            private readonly SurfaceStrip m_Strip = new SurfaceStrip();

            public Road(float z0 = -5f, float z1 = 5f)
            {
                for (float x = -100f; x <= 300f; x += 10f)
                {
                    m_Strip.Add(new Vec3(x, Height(x), z0), new Vec3(x, Height(x), z1));
                }
            }

            public static float Height(float x) => 0.05f + 0.001f * x;

            public AreaFit Read(float minX, float minZ, float maxX, float maxZ, float originY)
            {
                Reads++;
                return AreaFit.FromStrips(new List<SurfaceStrip> { m_Strip }, new Vec3(0f, originY, 0f), 0f, minX, maxX, minZ, maxZ);
            }
        }

        /// <summary>The points of a line along X from 0 at the centre line's height (0): every 2 m as the chain's line makes them, and the last at the end.</summary>
        private static List<Vec3> Line(float length, float z = 0f)
        {
            var line = new List<Vec3>();
            for (float d = 0f; length - d >= 0.5f || d == 0f; d += 2f)
                line.Add(new Vec3(d, 0f, z));
            line.Add(new Vec3(length, 0f, z));
            return line;
        }

        private static bool OnRoad(List<Vec3> line, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (Math.Abs(line[i].Y - Road.Height(line[i].X)) > 0.002f)
                    return false;
            }

            return true;
        }

        private static float NoGround(float x, float y, float z) => float.NaN;

        private static void Kept()
        {
            var road = new Road();
            var heights = new SurfaceHeights();
            List<Vec3> line = Line(150f);
            List<Vec3> first = heights.Put(line, "a", line.Count, road.Read, NoGround);
            int windows = (line.Count + SurfaceHeights.Window - 1) / SurfaceHeights.Window;
            Program.Check(first.Count == line.Count && OnRoad(first, first.Count), "every point of the line stands on the road's surface");
            Program.Check(road.Reads == windows && heights.Reads == windows, $"a read for each of the {windows} windows: {road.Reads}");

            List<Vec3> again = heights.Put(line, "a", line.Count, road.Read, NoGround);
            Program.Check(road.Reads == windows, "the same line again is not read again: " + road.Reads);
            bool same = again.Count == first.Count;
            for (int i = 0; same && i < again.Count; i++)
                same = again[i].X == first[i].X && again[i].Y == first[i].Y && again[i].Z == first[i].Z;
            Program.Check(same, "and comes out as it did");

            // The points the caller gave are not changed by the kept heights.
            Program.Check(line[0].Y == 0f && line[line.Count - 1].Y == 0f, "the line given is left as it was");

            heights.Clear();
            heights.Put(line, "a", line.Count, road.Read, NoGround);
            Program.Check(road.Reads == 2 * windows, "after Clear it is read again: " + road.Reads);
        }

        private static void Moving()
        {
            var road = new Road();
            var heights = new SurfaceHeights();
            int steps = 0;
            bool onRoad = true;
            int lastCount = 0;
            // The end of a run drawn from 0 m follows the cursor from 20 m to 250 m in half metres; the points are those of Line (the grid starts at 0), the last one is the end.
            for (float end = 20f; end <= 250f; end += 0.5f)
            {
                List<Vec3> line = Line(end);
                List<Vec3> put = heights.Put(line, "run", line.Count - 1, road.Read, NoGround);
                onRoad &= put.Count == line.Count && OnRoad(put, put.Count);
                lastCount = line.Count;
                steps++;
            }

            Program.Check(onRoad, "at every step every point of the line stands on the road's surface");
            int windows = (lastCount + SurfaceHeights.Window - 1) / SurfaceHeights.Window;
            // A window is read once when its points are all there, and the one the end is in again each time the end leaves what was read for it (MovingReach).
            int bound = windows + (int)Math.Ceiling(230f / SurfaceHeights.MovingReach) * 2 + 2;
            Program.Check(road.Reads <= bound, $"{steps} steps of the end read the road {road.Reads} time(s), at most {bound}");
            Program.Check(road.Reads * 10 < steps, $"far fewer reads than steps ({road.Reads} for {steps})");
            Console.WriteLine($"  surface heights: {steps} steps of a run's end over 230 m read the road {road.Reads} time(s) ({windows} windows at the end; before 0.12.21 every step read the whole line)");

            // An end that goes back is not read again either: the points were read for the window already.
            int before = road.Reads;
            for (float end = 250f; end >= 200f; end -= 0.5f)
            {
                List<Vec3> line = Line(end);
                List<Vec3> put = heights.Put(line, "run", line.Count - 1, road.Read, NoGround);
                onRoad &= OnRoad(put, put.Count);
            }

            Program.Check(onRoad && road.Reads - before <= 2, $"the end going back stands on the road and costs {road.Reads - before} read(s)");
        }

        private static void Terrain()
        {
            var road = new Road();
            var heights = new SurfaceHeights();
            int asked = 0;

            // Beside the road (z 50 m): the terrain's height when it is within 3 m of the line's, else the line's own.
            List<Vec3> line = Line(40f, 50f);
            List<Vec3> level = heights.Put(line, "beside", line.Count, road.Read, (x, y, z) => { asked++; return 0.4f; });
            bool near = true;
            foreach (Vec3 p in level)
                near &= Math.Abs(p.Y - 0.4f) < 1e-5f;
            Program.Check(near && asked == line.Count, $"beside the road every point takes the terrain's height ({asked} asked)");

            List<Vec3> bridge = heights.Put(line, "bridge", line.Count, road.Read, (x, y, z) => -9f);
            Program.Check(bridge.TrueForAll(p => p.Y == 0f), "terrain more than 3 m under the line is not stood on");
            List<Vec3> none = heights.Put(line, "none", line.Count, road.Read, NoGround);
            Program.Check(none.TrueForAll(p => p.Y == 0f), "no terrain: the line keeps its height");

            // On the road the terrain is not asked.
            asked = 0;
            List<Vec3> on = heights.Put(Line(40f), "on", Line(40f).Count, road.Read, (x, y, z) => { asked++; return 0f; });
            Program.Check(asked == 0 && OnRoad(on, on.Count), "on the road the terrain is not asked");

            // What is kept is by key and by the points' number.
            int reads = road.Reads;
            heights.Put(line, "beside", line.Count, road.Read, NoGround);
            Program.Check(road.Reads == reads, "the same key is kept");
            heights.Put(line, "other", line.Count, road.Read, NoGround);
            Program.Check(road.Reads > reads, "another key is read");
            reads = road.Reads;
            List<Vec3> shorter = Line(30f, 50f);
            List<Vec3> cut = heights.Put(shorter, "beside", shorter.Count, road.Read, NoGround);
            Program.Check(cut.Count == shorter.Count && road.Reads > reads, "a window of another size under the same key is made again, not given as it was kept");
        }
    }
}
