using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using CDLC.TS.Tests;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// Fixed 16 steps against steps that follow the curve (BENCH_ADAPTIVE=1; 0.12.73): synthetic cubic sides of a road piece (arcs of 30 to 5000 m radius, vertical curves, a change of grade, curves whose
    /// bend changes along them) made into bands the two ways, with how many triangles, how far the polyline is from the true curve (in the plan, and in height at the same place in the plan: where paint
    /// would float or sink) and the time; then the bands of a junction-like set through AreaFit.FromStrips. The pure parts only: the game's own curves and compositions are not here.
    /// </summary>
    internal static class Adaptive
    {
        private const int Dense = 4096;
        private static readonly (float Xz, float Y)[] Tolerances = { (0.020f, 0.010f), (0.010f, 0.003f), (0.005f, 0.002f) };

        public static void Run()
        {
            Console.WriteLine("Fixed 16 steps against the steps the curve needs (Wang's bound, equal steps of the parameter); deviations in mm, worst over the left side, the middle and the right side of the band.");
            Console.WriteLine("tolerances (plan, height) in mm: " + string.Join("  ", Array.ConvertAll(Tolerances, t => $"({t.Xz * 1000f:0},{t.Y * 1000f:0})")));
            Console.WriteLine();
            Console.WriteLine("case                          | fixed 16: tris  plan  floor |  (20,10): n   plan  floor | (10,3): n   plan  floor   us | (5,2): n   plan  floor | bisect(10,3): n  plan floor | least n at (10,3)");
            foreach ((string name, Bezier3 left, Bezier3 right) in Cases())
            {
                var row = new StringBuilder(name.PadRight(30) + "|");
                float[] fixedT = Uniform(16);
                Measure(left, right, fixedT, out float xzF, out float yF);
                row.Append($" {2 * 16,12} {xzF * 1000f,6:0.0} {yF * 1000f,6:0.0} |");
                for (int k = 0; k < Tolerances.Length; k++)
                {
                    float[] ts = Flatten.Params(left, right, Tolerances[k].Xz, Tolerances[k].Y);
                    Measure(left, right, ts, out float xz, out float y);
                    row.Append($" {ts.Length - 1,8} {xz * 1000f,6:0.0} {y * 1000f,6:0.0}");
                    if (k == 1)
                        row.Append($" {Micro(left, right, Tolerances[k].Xz, Tolerances[k].Y),6:0.0}");
                    row.Append(" |");
                }

                float[] bisect = Bisect(left, right, Tolerances[1].Xz, Tolerances[1].Y, 6);
                Measure(left, right, bisect, out float bxz, out float by);
                row.Append($" {bisect.Length - 1,9} {bxz * 1000f,5:0.0} {by * 1000f,5:0.0} | {Least(left, right, Tolerances[1].Xz, Tolerances[1].Y),5}");
                Console.WriteLine(row);
            }

            Console.WriteLine();
            Console.WriteLine("time of one band of 16 steps: " + MicroFixed(Cases()[3].Left, Cases()[3].Right).ToString("0.0") + " us (the same closures, for the comparison in the table: us column is the adaptive one with its counting).");
            Console.WriteLine();
            foreach (string mix in new[] { "straight-heavy", "mixed", "curvy" })
                Junction4(mix);
            ZoneSizes();
            foreach (string mix in new[] { "straight-heavy", "mixed", "curvy" })
                RoadRun(mix);
        }

        // ------------------------------------------------------------------ the curves

        /// <summary>A road piece as two cubic sides: the centre line an arc of radius radius (0: straight) and length length (at most a quarter turn), a vertical profile y = grade * s + (end / L^2) s^2 (as the cubic's heights), the sides 3.5 m apart.</summary>
        private static (Bezier3 Left, Bezier3 Right) Piece(float radius, float length, float grade, float curveEnd, float h = 1.75f)
        {
            float theta = radius > 0f ? Math.Min((float)(Math.PI / 2.0), length / radius) : 0f;
            // Quadratic y(t) = length * grade * t + curveEnd * t^2 as a cubic's heights.
            float q1 = length * grade * 0.5f, q2 = length * grade + curveEnd;
            float[] y = { 0f, (0f + 2f * q1) / 3f, (2f * q1 + q2) / 3f, q2 };
            return (Side(radius, theta, length, +h, y), Side(radius, theta, length, -h, y));
        }

        private static Bezier3 Side(float radius, float theta, float length, float offset, float[] y)
        {
            Vec3 a, b, c, d;
            if (theta <= 1e-6f)
            {
                a = new Vec3(-offset, y[0], 0f);
                d = new Vec3(-offset, y[3], length);
                b = new Vec3(-offset, y[1], length / 3f);
                c = new Vec3(-offset, y[2], 2f * length / 3f);
                return new Bezier3(a, b, c, d);
            }

            // Turning right round the centre (radius, 0): the left side is the outer one.
            float r = radius + offset, k = 4f / 3f * (float)Math.Tan(theta / 4f) * r;
            Func<float, Vec3> q = phi => new Vec3(radius - r * (float)Math.Cos(phi), 0f, r * (float)Math.Sin(phi));
            Func<float, Vec3> tangent = phi => new Vec3((float)Math.Sin(phi), 0f, (float)Math.Cos(phi));
            Vec3 p0 = q(0f), p3 = q(theta);
            Vec3 p1 = p0 + tangent(0f) * k, p2 = p3 - tangent(theta) * k;
            return new Bezier3(new Vec3(p0.X, y[0], p0.Z), new Vec3(p1.X, y[1], p1.Z), new Vec3(p2.X, y[2], p2.Z), new Vec3(p3.X, y[3], p3.Z));
        }

        private static List<(string Name, Bezier3 Left, Bezier3 Right)> Cases()
        {
            var list = new List<(string, Bezier3, Bezier3)>();
            void Add(string name, (Bezier3 Left, Bezier3 Right) piece) => list.Add((name, piece.Left, piece.Right));
            Add("straight 60 m, flat", Piece(0f, 60f, 0f, 0f));
            Add("straight 100 m, 3 % grade", Piece(0f, 100f, 0.03f, 0f));
            foreach (float radius in new[] { 30f, 50f, 100f, 200f, 500f, 1000f, 5000f })
                Add($"arc R={radius:0} L=60 (quarter turn at most)", Piece(radius, 60f, 0f, 0f));
            Add("arc R=100 L=20", Piece(100f, 20f, 0f, 0f));
            Add("arc R=100 L=100 (quarter turn)", Piece(100f, 100f, 0f, 0f));
            Add("arc R=500 L=20", Piece(500f, 20f, 0f, 0f));
            Add("arc R=500 L=100", Piece(500f, 100f, 0f, 0f));
            foreach (float rv in new[] { 500f, 1000f, 2000f, 5000f, 20000f })
                Add($"crest Rv={rv:0} (straight L=60)", Piece(0f, 60f, 0f, -3600f / (2f * rv)));
            Add("ramp: grade 0 to 6 % (L=60)", Piece(0f, 60f, 0f, 60f * 0.06f * 0.5f));
            Add("ramp on an arc R=200 (L=60)", Piece(200f, 60f, 0.02f, 60f * 0.06f * 0.5f));
            // Curves whose bend changes along them: a straight that tightens (a hook) and an S.
            list.Add(("hook: straight into a tight end", Straightened(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 20f), new Vec3(0f, 0f, 40f), new Vec3(-25f, 0f, 55f), 1.75f).Left, Straightened(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 20f), new Vec3(0f, 0f, 40f), new Vec3(-25f, 0f, 55f), 1.75f).Right));
            // A plain node of two roads whose centre lines are 2 m apart (the owner's log of 1 Nov: #109587 into #109446): the node part's sides, an S of 6 m.
            (Bezier3 jl, Bezier3 jr) = Straightened(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 2f), new Vec3(2f, 0f, 4f), new Vec3(2f, 0f, 6f), 1.75f);
            list.Add(("plain node: 2 m jump over 6 m", jl, jr));
            list.Add(("S curve (L about 70)", Straightened(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 30f), new Vec3(18f, 0f, 30f), new Vec3(18f, 0f, 70f), 1.75f).Left, Straightened(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 30f), new Vec3(18f, 0f, 30f), new Vec3(18f, 0f, 70f), 1.75f).Right));
            return list;
        }

        /// <summary>The two sides of a road whose centre line is the cubic p0..p3, 2 * half apart (each control point moved along the normal of the polygon at it: close enough for a bench).</summary>
        private static (Bezier3 Left, Bezier3 Right) Straightened(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, float half)
        {
            Vec3[] p = { p0, p1, p2, p3 };
            var left = new Vec3[4];
            var right = new Vec3[4];
            for (int i = 0; i < 4; i++)
            {
                Vec3 ahead = (p[Math.Min(3, i + 1)] - p[Math.Max(0, i - 1)]).HeadingXZ();
                Vec3 normal = new Vec3(-ahead.Z, 0f, ahead.X);
                left[i] = p[i] + normal * half;
                right[i] = p[i] - normal * half;
            }

            return (new Bezier3(left[0], left[1], left[2], left[3]), new Bezier3(right[0], right[1], right[2], right[3]));
        }

        // ------------------------------------------------------------------ the arithmetic

        private static float[] Uniform(int n)
        {
            var t = new float[n + 1];
            for (int i = 0; i <= n; i++)
                t[i] = i / (float)n;
            return t;
        }

        /// <summary>The union of the places a bisection needs for either curve: a piece is taken when the bound over it alone is within the tolerance, else halved (at most 2^depth pieces).</summary>
        private static float[] Bisect(Bezier3 left, Bezier3 right, float tolXz, float tolY, int depth)
        {
            var set = new SortedSet<float>();
            foreach (Bezier3 curve in new[] { left, right })
            {
                var stack = new Stack<(Bezier3 C, float T0, float T1, int D)>();
                stack.Push((curve, 0f, 1f, 0));
                set.Add(0f);
                set.Add(1f);
                while (stack.Count > 0)
                {
                    (Bezier3 c, float t0, float t1, int d) = stack.Pop();
                    Vec3 d0 = c.A - c.B * 2f + c.C, d1 = c.B - c.C * 2f + c.D;
                    float xz = 0.75f * Math.Max(d0.LengthXZ, d1.LengthXZ), y = 0.75f * Math.Max(Math.Abs(d0.Y), Math.Abs(d1.Y));
                    if ((xz <= tolXz && y <= tolY) || d >= depth)
                        continue;
                    Vec3 p01 = Vec3.Lerp(c.A, c.B, 0.5f), p12 = Vec3.Lerp(c.B, c.C, 0.5f), p23 = Vec3.Lerp(c.C, c.D, 0.5f);
                    Vec3 p012 = Vec3.Lerp(p01, p12, 0.5f), p123 = Vec3.Lerp(p12, p23, 0.5f), mid = Vec3.Lerp(p012, p123, 0.5f);
                    float tm = (t0 + t1) * 0.5f;
                    set.Add(tm);
                    stack.Push((new Bezier3(c.A, p01, p012, mid), t0, tm, d + 1));
                    stack.Push((new Bezier3(mid, p123, p23, c.D), tm, t1, d + 1));
                }
            }

            var result = new float[set.Count];
            set.CopyTo(result);
            return result;
        }

        /// <summary>The worst deviation of the polylines of the band's two sides and its middle (taken at the places ts) from the true curves, against 4096 places: in the plan (the nearest point of the polyline), and in height at that point of the plan.</summary>
        private static void Measure(Bezier3 left, Bezier3 right, float[] ts, out float plan, out float floating)
        {
            plan = floating = 0f;
            foreach (float share in new[] { 0f, 0.5f, 1f })
            {
                Func<float, Vec3> at = t => Vec3.Lerp(left.Point(t), right.Point(t), share);
                var poly = new Vec3[ts.Length];
                for (int i = 0; i < ts.Length; i++)
                    poly[i] = at(ts[i]);
                for (int s = 0; s <= Dense; s++)
                {
                    Vec3 p = at(s / (float)Dense);
                    float best = float.MaxValue, height = 0f;
                    for (int i = 0; i + 1 < poly.Length; i++)
                    {
                        Vec3 a = poly[i], b = poly[i + 1];
                        float dx = b.X - a.X, dz = b.Z - a.Z, l2 = dx * dx + dz * dz;
                        float u = l2 > 1e-12f ? Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / l2)) : 0f;
                        float ex = a.X + u * dx - p.X, ez = a.Z + u * dz - p.Z, d = (float)Math.Sqrt(ex * ex + ez * ez);
                        if (d < best)
                        {
                            best = d;
                            height = Math.Abs(a.Y + u * (b.Y - a.Y) - p.Y);
                        }
                    }

                    plan = Math.Max(plan, best);
                    floating = Math.Max(floating, height);
                }
            }
        }

        /// <summary>The least number of steps that any places of the parameter could make for the pair of sides at the tolerance: from each place the farthest next one whose chord stays within it (the same-parameter deviation, on a grid of 512), the larger of the two sides.</summary>
        private static int Least(Bezier3 left, Bezier3 right, float tolXz, float tolY)
        {
            return Math.Max(Least(left, tolXz, tolY), Least(right, tolXz, tolY));
        }

        private static int Least(Bezier3 curve, float tolXz, float tolY)
        {
            const int grid = 512;
            var p = new Vec3[grid + 1];
            for (int i = 0; i <= grid; i++)
                p[i] = curve.Point(i / (float)grid);
            int steps = 0, from = 0;
            while (from < grid)
            {
                int to = from + 1;
                while (to < grid && Within(p, from, to + 1, tolXz, tolY))
                    to++;
                from = to;
                steps++;
            }

            return steps;
        }

        private static bool Within(Vec3[] p, int from, int to, float tolXz, float tolY)
        {
            for (int i = from + 1; i < to; i++)
            {
                Vec3 on = Vec3.Lerp(p[from], p[to], (i - from) / (float)(to - from));
                Vec3 off = p[i] - on;
                if (off.LengthXZ > tolXz || Math.Abs(off.Y) > tolY)
                    return false;
            }

            return true;
        }

        private static double Micro(Bezier3 left, Bezier3 right, float tolXz, float tolY)
        {
            var stretch = new PaintableStretch(0f, 1f, 0f);
            const int repeats = 3000;
            SurfaceSection.BandsAdaptive(left, right, stretch, tolXz, tolY, "warm");
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++)
                SurfaceSection.BandsAdaptive(left, right, stretch, tolXz, tolY, "b");
            return clock.Elapsed.TotalMilliseconds * 1000.0 / repeats;
        }

        private static double MicroFixed(Bezier3 left, Bezier3 right)
        {
            var stretch = new PaintableStretch(0f, 1f, 0f);
            const int repeats = 3000;
            Func<float, Vec3> l = t => left.Point(t), r = t => right.Point(t);
            SurfaceSection.Bands(l, r, stretch, 16, "warm");
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++)
                SurfaceSection.Bands(l, r, stretch, 16, "b");
            return clock.Elapsed.TotalMilliseconds * 1000.0 / repeats;
        }

        // ------------------------------------------------------------------ a junction-like set through the fit

        // ------------------------------------------------------------------ a four-arm junction

        private enum Policy { Fixed, Everywhere, Zones, ZonesFloor }

        private static readonly float[][] OpenShares = { new[] { 0f, 0.12f }, new[] { 0.12f, 0.5f }, new[] { 0.5f, 0.88f }, new[] { 0.88f, 1f } };
        private static readonly float[][] NodeShares = { new[] { 0f, 0.3f }, new[] { 0.3f, 0.7f }, new[] { 0.7f, 1f } };

        private static Bezier3 Turn(Bezier3 c, double angle)
        {
            float s = (float)Math.Sin(angle), co = (float)Math.Cos(angle);
            Func<Vec3, Vec3> map = p => new Vec3(p.X * co + p.Z * s, p.Y, -p.X * s + p.Z * co);
            return OnGround(new Bezier3(map(c.A), map(c.B), map(c.C), map(c.D)));
        }

        /// <summary>
        /// A junction of four arms of 7 m roads as the game builds one: for each arm the segment half (45 m from z = 15 outward, an arc of the mix's radius, four stretches across) and the junction part
        /// from z = 15 in to z = 2 (two halves, left side to the middle line and the middle line to the right side, flaring out 4 m at the inner end, three stretches across each), the arms 90 degrees
        /// apart and a few degrees off, one height surface for all. steps > 0 makes every band of that many equal steps (the reference); else by policy. middleGap is the worst distance between the
        /// polylines of the two halves along their shared middle line (m), crossGap the worst distance between the corner points of the segment half and of the junction part at their cross-section line.
        /// </summary>
        private static List<SurfaceStrip> BuildJunction(string mix, Policy policy, int steps, Zones zones, out float middleGap, out float crossGap) =>
            BuildJunction(mix, policy, steps, zones, out middleGap, out crossGap, new List<(Bezier3, bool)>());

        private static List<SurfaceStrip> BuildJunction(string mix, Policy policy, int steps, Zones zones, out float middleGap, out float crossGap, List<(Bezier3 Line, bool Straight)> lines)
        {
            var strips = new List<SurfaceStrip>();
            var random = new Random(5);
            const float h = 3.5f;
            middleGap = crossGap = 0f;
            float tolXz = Tolerances[1].Xz, tolY = Tolerances[1].Y;
            for (int arm = 0; arm < 4; arm++)
            {
                double angle = arm * Math.PI / 2.0 + (random.NextDouble() - 0.5) * 0.28;
                float radius = Radius(mix, random) * (random.Next(2) == 0 ? 1f : -1f);
                (Bezier3 sl, Bezier3 sr) = Piece(Math.Abs(radius) > 9000f ? 0f : Math.Abs(radius), 45f, 0f, 0f, h);
                // The segment half starts at z = 15 (a turn to the left is the same arc mirrored).
                Func<Bezier3, Bezier3> up = c =>
                {
                    Func<Vec3, Vec3> map = p => new Vec3(radius < 0f ? -p.X : p.X, p.Y, p.Z + 15f);
                    return Turn(new Bezier3(map(c.A), map(c.B), map(c.C), map(c.D)), angle);
                };
                Bezier3 left = up(sl), right = up(sr);
                // Mirrored, the left side of the arc is on the right.
                if (radius < 0f)
                    (left, right) = (right, left);
                Func<float, float, float, Bezier3> side = (x0, xm, xe) => Turn(new Bezier3(new Vec3(x0, 0f, 15f), new Vec3(x0, 0f, 10f), new Vec3(xm, 0f, 6f), new Vec3(xe, 0f, 2f)), angle);
                Bezier3 nl = side(-h, -h - 1.5f, -h - 4f), nm = side(0f, 1f, 2.2f), nr = side(h, h + 0.5f, h + 1.2f);
                crossGap = Math.Max(crossGap, Math.Max((left.Point(0f) - nl.Point(0f)).Length, (right.Point(0f) - nr.Point(0f)).Length));

                foreach (float[] share in OpenShares)
                {
                    var stretch = new PaintableStretch(share[0], share[1], 0f);
                    if (steps > 0)
                        strips.AddRange(SurfaceSection.Bands(t => left.Point(t), t => right.Point(t), stretch, steps, "r"));
                    else if (policy == Policy.Fixed)
                        strips.AddRange(SurfaceSection.Bands(t => left.Point(t), t => right.Point(t), stretch, 16, "f"));
                    else
                        strips.AddRange(SurfaceSection.BandsAdaptive(left, right, stretch, tolXz, tolY, "a", policy == Policy.Everywhere ? Zones.None : zones));
                }

                float[] listL, listR;
                if (steps > 0 || policy == Policy.Fixed || policy == Policy.Zones)
                {
                    listL = listR = Uniform(steps > 0 ? steps : 16);
                }
                else if (policy == Policy.ZonesFloor)
                {
                    listL = listR = Flatten.PartParams(new[] { nl, nm, nr }, tolXz, tolY);
                }
                else
                {
                    listL = Flatten.Params(nl, nm, tolXz, tolY);
                    listR = Flatten.Params(nm, nr, tolXz, tolY);
                }

                foreach (float[] share in NodeShares)
                {
                    var stretch = new PaintableStretch(share[0], share[1], 0f);
                    strips.AddRange(SurfaceSection.BandsAt(t => nl.Point(t), t => nm.Point(t), stretch, listL, "nl"));
                    strips.AddRange(SurfaceSection.BandsAt(t => nm.Point(t), t => nr.Point(t), stretch, listR, "nr"));
                }

                middleGap = Math.Max(middleGap, PolylineGap(nm, listL, listR));
                lines.Add((nm, false));
                lines.Add((Bezier3.Line(left.Point(0f), right.Point(0f)), true));
            }

            return strips;
        }

        /// <summary>The widest sliver in a fit along a line (mm): at 500 places of the line the fit is asked every millimetre across it for 30 mm each way; the longest run with no piece in it. A line the fit covers has none.</summary>
        private static float Sliver(AreaFit fit, Bezier3 line, bool straight)
        {
            var seen = new HashSet<int>();
            var found = new List<int>();
            int worst = 0;
            for (int i = 1; i < 500; i++)
            {
                float t = i / 500f;
                Vec3 p = line.Point(t), ahead = (straight ? line.D - line.A : line.Tangent(t)).HeadingXZ();
                Vec3 across = new Vec3(-ahead.Z, 0f, ahead.X);
                int run = 0;
                for (int k = -30; k <= 30; k++)
                {
                    if (fit.HeightAt(p.X + across.X * k * 0.001f, p.Z + across.Z * k * 0.001f, out _, out _, seen, found))
                    {
                        run = 0;
                    }
                    else
                    {
                        run++;
                        worst = Math.Max(worst, run);
                    }
                }
            }

            return worst;
        }

        /// <summary>The worst distance (m, plan and height together) from a point of one polyline of a curve to the other polyline of it, either way.</summary>
        private static float PolylineGap(Bezier3 curve, float[] a, float[] b)
        {
            float worst = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                float[] from = pass == 0 ? a : b, to = pass == 0 ? b : a;
                for (int i = 0; i < from.Length; i++)
                {
                    Vec3 p = curve.Point(from[i]);
                    float best = float.MaxValue;
                    for (int j = 0; j + 1 < to.Length; j++)
                    {
                        Vec3 q0 = curve.Point(to[j]), q1 = curve.Point(to[j + 1]);
                        float dx = q1.X - q0.X, dz = q1.Z - q0.Z, l2 = dx * dx + dz * dz;
                        float u = l2 > 1e-12f ? Math.Max(0f, Math.Min(1f, ((p.X - q0.X) * dx + (p.Z - q0.Z) * dz) / l2)) : 0f;
                        float ex = q0.X + u * dx - p.X, ez = q0.Z + u * dz - p.Z, ey = q0.Y + u * (q1.Y - q0.Y) - p.Y;
                        best = Math.Min(best, (float)Math.Sqrt(ex * ex + ez * ez + ey * ey));
                    }

                    worst = Math.Max(worst, best);
                }
            }

            return worst;
        }

        private static void Junction4(string mix)
        {
            Console.WriteLine($"four-arm junction, arms {mix}: 4 segment halves of 45 m (4 stretches) and 4 junction parts (2 halves of 3 stretches), window 80 x 80 m, grid 0.25 m against a reference of 256 steps everywhere");
            Console.WriteLine("policy                         steps  pieces  FromStrips ms  ForHeights ms | places missing  extra | height diff worst/mean mm | middle line: polyline gap, sliver in the fit mm | cross-section line: sliver mm");
            AreaFit reference = AreaFit.FromStrips(BuildJunction(mix, Policy.Fixed, 256, Zones.None, out _, out _), new Vec3(0f, 0f, 0f), 0f, -40f, 40f, -40f, 40f);
            foreach ((string name, Policy policy, Zones zones) in new[]
            {
                ("all fixed 16", Policy.Fixed, Zones.None),
                ("adaptive everywhere", Policy.Everywhere, Zones.None),
                ("adaptive open road + zones", Policy.Zones, new Zones(true, false)),
                ("  same, junction part floor 16", Policy.ZonesFloor, new Zones(true, false))
            })
            {
                var lines = new List<(Bezier3 Line, bool Straight)>();
                List<SurfaceStrip> strips = BuildJunction(mix, policy, 0, zones, out float middle, out float cross, lines);
                int count = 0;
                foreach (SurfaceStrip strip in strips)
                    count += strip.Left.Count - 1;
                AreaFit fit = null, heights = null;
                double ms = Time(() => fit = AreaFit.FromStrips(strips, new Vec3(0f, 0f, 0f), 0f, -40f, 40f, -40f, 40f));
                double msH = Time(() => heights = AreaFit.ForHeights(strips, new Vec3(0f, 0f, 0f), 0f, -40f, 40f, -40f, 40f));
                Against(reference, fit, out int missing, out int extra, out int total, out float worst, out float mean);
                float middleSliver = 0f, crossSliver = 0f;
                foreach ((Bezier3 line, bool straight) in lines)
                {
                    float sliver = Sliver(fit, line, straight);
                    if (straight)
                        crossSliver = Math.Max(crossSliver, sliver);
                    else
                        middleSliver = Math.Max(middleSliver, sliver);
                }

                Console.WriteLine($"{name,-30} {count,5} {fit.PieceCount,7} {ms,13:0.00} {msH,14:0.00} | {missing,7} {extra,6} ({100.0 * (missing + extra) / total:0.00} %) | {worst * 1000f,8:0.0} {mean * 1000f,6:0.00} | {middle * 1000f,10:0.0} {middleSliver,10:0} | {crossSliver,8:0}  (probe: cross-section gap of the corner points {cross * 1000f:0.0} mm)");
            }

            Console.WriteLine();
        }

        /// <summary>The zone lengths and steps on the mixed junction: what the other values do to the pieces, the time and the agreement with the reference.</summary>
        private static void ZoneSizes()
        {
            Console.WriteLine("end zone length and step (adaptive open road, junction parts fixed 16), four-arm junction, arms mixed:");
            Console.WriteLine("zone length  step |  steps  pieces  FromStrips ms | places missing+extra of total | height diff worst mm");
            AreaFit reference = AreaFit.FromStrips(BuildJunction("mixed", Policy.Fixed, 256, Zones.None, out _, out _), new Vec3(0f, 0f, 0f), 0f, -40f, 40f, -40f, 40f);
            foreach ((float length, float step) in new[] { (0f, 2f), (6f, 2f), (12f, 2f), (24f, 2f), (12f, 1f), (12f, 4f), (12f, 8f) })
            {
                List<SurfaceStrip> strips = BuildJunction("mixed", Policy.Zones, 0, length > 0f ? new Zones(true, false, length, step) : Zones.None, out _, out _);
                int count = 0;
                foreach (SurfaceStrip strip in strips)
                    count += strip.Left.Count - 1;
                AreaFit fit = null;
                double ms = Time(() => fit = AreaFit.FromStrips(strips, new Vec3(0f, 0f, 0f), 0f, -40f, 40f, -40f, 40f));
                Against(reference, fit, out int missing, out int extra, out int total, out float worst, out _);
                Console.WriteLine($"{length,10:0} {step,5:0.#} | {count,6} {fit.PieceCount,7} {ms,13:0.00} | {missing + extra,6} of {total,6} | {worst * 1000f,8:0.0}");
            }

            Console.WriteLine();
        }

        /// <summary>A fit against a reference over a grid of 0.25 m: the places only the reference has (missing), only the fit has (extra), how many the reference has, and the height difference where both do.</summary>
        private static void Against(AreaFit reference, AreaFit fit, out int missing, out int extra, out int total, out float worst, out float mean)
        {
            missing = extra = total = 0;
            worst = 0f;
            double sum = 0.0;
            int both = 0;
            var seen = new HashSet<int>();
            var found = new List<int>();
            for (float x = -40f; x <= 40f; x += 0.25f)
            {
                for (float z = -40f; z <= 40f; z += 0.25f)
                {
                    bool inRef = reference.HeightAt(x, z, out float yr, out _, seen, found), inFit = fit.HeightAt(x, z, out float yf, out _, seen, found);
                    if (inRef)
                        total++;
                    if (inRef && inFit)
                    {
                        both++;
                        float d = Math.Abs(yr - yf);
                        worst = Math.Max(worst, d);
                        sum += d;
                    }
                    else if (inRef)
                    {
                        missing++;
                    }
                    else if (inFit)
                    {
                        extra++;
                    }
                }
            }

            mean = both > 0 ? (float)(sum / both) : 0f;
        }

        /// <summary>A gently rolling ground: the height of every control point from its place (a grade across, a swell along), so the cubic is one that follows it.</summary>
        private static Bezier3 OnGround(Bezier3 c)
        {
            Func<Vec3, Vec3> lift = p => new Vec3(p.X, 0.012f * p.X - 0.008f * p.Z + 0.6f * (float)Math.Sin(p.X / 35f) * (float)Math.Cos(p.Z / 50f), p.Z);
            return new Bezier3(lift(c.A), lift(c.B), lift(c.C), lift(c.D));
        }

        /// <summary>
        /// A run of parts along one road (what a wall's surface read sees: StandHeights, a window of 120 m): 4 parts of 30 m, each curve a cubic of the mix's radius, 4 stretches across each (lanes and
        /// shoulders), the parts edge to edge, the road bending at each joint by the arc it has made; through AreaFit.ForHeights and FromStrips over the window.
        /// </summary>
        private static void RoadRun(string mix)
        {
            Console.WriteLine($"road run, {mix}: 4 parts of 30 m with 4 stretches across each (edge to edge), window 120 x 6 m");
            var random = new Random(23);
            var fixedStrips = new List<SurfaceStrip>();
            var adaptiveStrips = new List<SurfaceStrip>();
            float[][] shares = { new[] { 0f, 0.12f }, new[] { 0.12f, 0.5f }, new[] { 0.5f, 0.88f }, new[] { 0.88f, 1f } };
            // The road as a chain of arcs of one radius each (one radius for the run, as the mixes say), laid end to end.
            float radius = Radius(mix, random) * (random.Next(2) == 0 ? 1f : -1f);
            float heading = 0f;
            Vec3 at = new Vec3(0f, 0f, -60f);
            for (int part = 0; part < 4; part++)
            {
                (Bezier3 l, Bezier3 r) = Piece(Math.Abs(radius) > 9000f ? 0f : Math.Abs(radius), 30f, 0.01f, 0f);
                float turn = Math.Abs(radius) > 9000f ? 0f : (float)(30.0 / Math.Abs(radius)) * Math.Sign(radius);
                float s = (float)Math.Sin(heading), c = (float)Math.Cos(heading);
                Func<Bezier3, Bezier3> put = b =>
                {
                    Func<Vec3, Vec3> map = p =>
                    {
                        float x = radius < 0f ? -p.X : p.X;
                        return new Vec3(at.X + x * c + p.Z * s, p.Y + at.Y, at.Z - x * s + p.Z * c);
                    };
                    return new Bezier3(map(b.A), map(b.B), map(b.C), map(b.D));
                };
                Bezier3 left = put(l), right = put(r);
                foreach (float[] share in shares)
                {
                    var stretch = new PaintableStretch(share[0], share[1], 0f);
                    fixedStrips.AddRange(SurfaceSection.Bands(t => left.Point(t), t => right.Point(t), stretch, 16, "f"));
                    adaptiveStrips.AddRange(SurfaceSection.BandsAdaptive(left, right, stretch, Tolerances[1].Xz, Tolerances[1].Y, "a"));
                }

                Vec3 end = (left.D + right.D) * 0.5f;
                at = new Vec3(end.X, end.Y, end.Z);
                heading += turn;
            }

            int fs = 0, ads = 0;
            foreach (SurfaceStrip s in fixedStrips)
                fs += s.Left.Count - 1;
            foreach (SurfaceStrip s in adaptiveStrips)
                ads += s.Left.Count - 1;
            AreaFit fa = null, fb = null, ha = null, hb = null;
            double msFa = Time(() => fa = AreaFit.FromStrips(fixedStrips, new Vec3(0f, 0f, 0f), 0f, -60f, 60f, -3f, 3f));
            double msFb = Time(() => fb = AreaFit.FromStrips(adaptiveStrips, new Vec3(0f, 0f, 0f), 0f, -60f, 60f, -3f, 3f));
            double msHa = Time(() => ha = AreaFit.ForHeights(fixedStrips, new Vec3(0f, 0f, 0f), 0f, -60f, 60f, -3f, 3f));
            double msHb = Time(() => hb = AreaFit.ForHeights(adaptiveStrips, new Vec3(0f, 0f, 0f), 0f, -60f, 60f, -3f, 3f));
            Console.WriteLine($"   radius {(Math.Abs(radius) > 9000f ? "straight" : radius.ToString("0"))}: fixed 16 {fs,4} steps (quads), FromStrips {fa.PieceCount,4} pieces {msFa,6:0.00} ms, ForHeights {ha.PieceCount,4} pieces {msHa,6:0.00} ms");
            Console.WriteLine($"                 adaptive  {ads,4} steps (quads), FromStrips {fb.PieceCount,4} pieces {msFb,6:0.00} ms, ForHeights {hb.PieceCount,4} pieces {msHb,6:0.00} ms");
            Compare(ha, hb, out int onlyA, out int onlyB, out int both, out float worst, out float mean);
            Console.WriteLine($"   ForHeights places of the 0.5 m grid: both {both}, only fixed {onlyA}, only adaptive {onlyB}; height difference worst {worst * 1000f:0.0} mm, mean {mean * 1000f:0.00} mm");
            Console.WriteLine();
        }

        private static float Radius(string mix, Random random)
        {
            double pick = random.NextDouble();
            switch (mix)
            {
                case "straight-heavy": return pick < 0.7 ? 10000f : pick < 0.9 ? (float)(200 + random.NextDouble() * 800) : (float)(50 + random.NextDouble() * 150);
                case "curvy": return (float)(30 + random.NextDouble() * 120);
                default: return pick < 0.45 ? 10000f : pick < 0.8 ? (float)(200 + random.NextDouble() * 800) : (float)(30 + random.NextDouble() * 170);
            }
        }

        /// <summary>The time of one run (ms): the least of seven means of ten runs each, after a warm-up (a collection or a switch of the processor inflates one batch, not all).</summary>
        private static double Time(Action work)
        {
            work();
            work();
            double best = double.MaxValue;
            for (int batch = 0; batch < 7; batch++)
            {
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 10; i++)
                    work();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds / 10.0);
            }

            return best;
        }

        private static void Compare(AreaFit a, AreaFit b, out int onlyA, out int onlyB, out int both, out float worst, out float mean)
        {
            onlyA = onlyB = both = 0;
            worst = 0f;
            double sum = 0.0;
            var seen = new HashSet<int>();
            var found = new List<int>();
            for (float x = -32f; x <= 32f; x += 0.5f)
            {
                for (float z = -8f; z <= 8f; z += 0.5f)
                {
                    bool inA = a.HeightAt(x, z, out float ya, out _, seen, found), inB = b.HeightAt(x, z, out float yb, out _, seen, found);
                    if (inA && inB)
                    {
                        both++;
                        float d = Math.Abs(ya - yb);
                        worst = Math.Max(worst, d);
                        sum += d;
                    }
                    else if (inA)
                    {
                        onlyA++;
                    }
                    else if (inB)
                    {
                        onlyB++;
                    }
                }
            }

            mean = both > 0 ? (float)(sum / both) : 0f;
        }
    }
}
