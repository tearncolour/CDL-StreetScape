using System;
using System.Collections.Generic;
using CDLC.TS.FollowReference;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using static CDLC.TS.Tests.FollowFixture;

namespace CDLC.TS.Tests
{
    /// <summary>The faster road follow against the frozen copy of ca86e49 (FollowReference): the same answers, on thousands of random roads, items and drags (0.12.74 follow work).</summary>
    internal static class FollowFastTests
    {
        public static void RunAll()
        {
            Program.Run("follow fast: the table is ArcTable's table", Tables);
            Program.Run("follow fast: the nearest point is as near as the scan and the golden section find it", Closest);
            Program.Run("follow fast: the relation and its place are the reference's, on the same curve and on a bent one", Relations);
            Program.Run("follow fast: a rigid motion is found, and only when every point of the curve is within the tolerance", RigidMotions);
            Program.Run("follow fast: a batch gives the reference's outcome and place for every item", Batch);
            Program.Run("follow fast: the cache keeps a table per curve and a collision is a miss", Cache);
            Program.Run("follow fast: a line is the reference's line; the threshold, the pose-only step, the resampling", Lines);
        }

        // ------------------------------------------------------------------ random roads and items

        // ------------------------------------------------------------------ the pieces

        private static void Tables()
        {
            var random = new Random(1);
            float worstLength = 0f, worstT = 0f, worstS = 0f;
            for (int k = 0; k < 2000; k++)
            {
                Bezier3 c = RandomCurve(random);
                var reference = new ArcTable(c);
                var table = new FollowCurve(c);
                worstLength = Math.Max(worstLength, Math.Abs(reference.Length - table.Length));
                for (int i = 0; i < 8; i++)
                {
                    float s = (float)(random.NextDouble() * 1.2 - 0.1) * reference.Length, t = (float)(random.NextDouble() * 1.2 - 0.1);
                    worstT = Math.Max(worstT, Math.Abs(reference.TAt(s) - table.TAt(s)));
                    worstS = Math.Max(worstS, Math.Abs(reference.SAt(t) - table.SAt(t)));
                }
            }

            Program.Check(worstLength == 0f && worstT == 0f && worstS == 0f, $"length, TAt and SAt are ArcTable's to the bit: {worstLength}, {worstT}, {worstS}");
            var dot = new Vec3(5f, 0f, 5f);
            Program.Check(!new FollowCurve(new Bezier3(dot, dot, dot, dot)).IsUsable && !new FollowCurve(new Bezier3(Vec3.Zero, new Vec3(float.NaN, 0f, 1f), Vec3.Zero, Vec3.Zero)).IsUsable, "a curve of no length and one that is not a number are not usable");
            var cusp = new Bezier3(Vec3.Zero, Vec3.Zero, new Vec3(0f, 0f, 40f), new Vec3(0f, 0f, 100f));
            new FollowCurve(cusp).At(0f, out _, out Vec3 heading);
            Program.Check(Math.Abs(heading.Z - 1f) < 1e-5f, "a collapsed end has the chord's heading");
        }

        private static void Closest()
        {
            var random = new Random(2);
            double worstWorse = 0.0, worstVsReference = 0.0, ambiguousWorst = 0.0;
            int hintFaster = 0, total = 0, iterations = 0, maxIterations = 0, ambiguous = 0;
            for (int k = 0; k < 4000; k++)
            {
                Bezier3 c = RandomCurve(random, k % 2 == 0 ? 3000f : 50f, k % 3 == 0 ? 0.3f : 0.1f);
                var table = new FollowCurve(c);
                Vec3 p = RandomPlace(random, c);
                float tNew = table.ClosestT(p, -1f, out int n);
                float tRef = Ref.ClosestT(c, p);
                double dNew = DistD(c, tNew, p), dRef = DistD(c, tRef, p), dDense = Dense(c, p);
                worstWorse = Math.Max(worstWorse, dNew - dDense);
                worstVsReference = Math.Max(worstVsReference, dNew - dRef);
                if (Math.Abs(tNew - tRef) > 2e-3f)
                {
                    ambiguous++;
                    ambiguousWorst = Math.Max(ambiguousWorst, dRef - dNew);
                }

                iterations += n;
                maxIterations = Math.Max(maxIterations, n);
                total++;
                float warm = table.ClosestT(p, tNew, out int nw);
                if (nw < n)
                    hintFaster++;
                worstWorse = Math.Max(worstWorse, DistD(c, warm, p) - dDense);
            }

            // A hairpin: a point between the two legs has a nearest point on each, the table's coarse best can be on the wrong leg; and a hint that is nowhere near must not matter.
            double worstHairpin = 0.0, worstFarHint = 0.0;
            int hairpins = 0;
            for (int k = 0; k < 3000; k++)
            {
                float width = 4f + (float)random.NextDouble() * 8f, length = 40f + (float)random.NextDouble() * 60f, other = 30f + (float)random.NextDouble() * 80f;
                var u = new Bezier3(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, length), new Vec3(width, 0f, other), new Vec3(width, 0f, 0f));
                var tab = new FollowCurve(u);
                var q = new Vec3(width * 0.5f + (float)(random.NextDouble() - 0.5) * 0.1f, 0f, (float)random.NextDouble() * Math.Min(length, other) * 0.6f);
                float tq = tab.ClosestT(q, -1f, out _);
                double dense = Dense(u, q);
                worstHairpin = Math.Max(worstHairpin, DistD(u, tq, q) - dense);
                float far = tab.ClosestT(q, (float)random.NextDouble(), out _);
                worstFarHint = Math.Max(worstFarHint, DistD(u, far, q) - dense);
                hairpins++;
            }

            Program.Check(worstHairpin < 1e-5, $"between the legs of a hairpin it finds the nearer leg: {worstHairpin * 1000.0:0.000} mm over {hairpins} places");
            Program.Check(worstFarHint < 1e-5, $"and a hint that is not near the answer does not change it: {worstFarHint * 1000.0:0.000} mm");
            var line = new FollowCurve(Bezier3.Line(Vec3.Zero, new Vec3(0f, 0f, 100f)));
            float before = line.ClosestT(new Vec3(3f, 0f, -20f), -1f, out int stepsBefore), past = line.ClosestT(new Vec3(3f, 0f, 150f), -1f, out int stepsPast);
            Program.Check(before == 0f && stepsBefore == 0 && past == 1f && stepsPast == 0, $"a place before the start or past the end is the end itself, found without a step: {before} in {stepsBefore}, {past} in {stepsPast}");
            Program.Check(worstWorse < 1e-5, $"never farther than the densest scan finds it: {worstWorse * 1000.0:0.000} mm");
            Program.Check(worstVsReference < 1e-5, $"never farther than the reference's answer: {worstVsReference * 1000.0:0.000} mm");
            Program.Check(ambiguous < 60, $"it is another point than the reference's only where there are two bends' minima, and then the nearer: {ambiguous} of {total}, the reference {ambiguousWorst * 1000.0:0.000} mm farther at most");
            Program.Check(maxIterations <= 24 && iterations / (double)total < 3.5, $"few steps: mean {iterations / (double)total:0.00}, most {maxIterations}; a hint shortened {hintFaster}");
        }

        /// <summary>The point of the curve at t in double precision (x, y, z), so that what is measured is not the float rounding of the curve's own evaluation.</summary>
        internal static double[] PointD(Bezier3 c, double t)
        {
            double u = 1 - t, w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
            return new[] { c.A.X * w0 + c.B.X * w1 + c.C.X * w2 + c.D.X * w3, c.A.Y * w0 + c.B.Y * w1 + c.C.Y * w2 + c.D.Y * w3, c.A.Z * w0 + c.B.Z * w1 + c.C.Z * w2 + c.D.Z * w3 };
        }

        private static double[] TangentD(Bezier3 c, double t)
        {
            double u = 1 - t;
            double x = 3 * (u * u * (c.B.X - c.A.X) + 2 * u * t * (c.C.X - c.B.X) + t * t * (c.D.X - c.C.X));
            double z = 3 * (u * u * (c.B.Z - c.A.Z) + 2 * u * t * (c.C.Z - c.B.Z) + t * t * (c.D.Z - c.C.Z));
            double l = Math.Sqrt(x * x + z * z);
            if (l < 1e-6)
            {
                x = c.D.X - c.A.X;
                z = c.D.Z - c.A.Z;
                l = Math.Sqrt(x * x + z * z);
            }

            return new[] { x / l, z / l };
        }

        private static double DistD(Bezier3 c, double t, Vec3 p)
        {
            double[] q = PointD(c, t);
            return Math.Sqrt((q[0] - p.X) * (q[0] - p.X) + (q[2] - p.Z) * (q[2] - p.Z));
        }

        /// <summary>The nearest distance in the ground plane by a scan of 8192 steps (double precision).</summary>
        private static double Dense(Bezier3 c, Vec3 p)
        {
            double bestD = double.MaxValue;
            const int steps = 8192;
            for (int i = 0; i <= steps; i++)
                bestD = Math.Min(bestD, DistD(c, (double)i / steps, p));
            return bestD;
        }

        /// <summary>The nearest parameter in double precision: the scan, then a golden section of 60 steps round the best.</summary>
        internal static double ExactT(Bezier3 c, Vec3 p)
        {
            int best = 0;
            double bestD = double.MaxValue;
            const int steps = 4096;
            for (int i = 0; i <= steps; i++)
            {
                double d = DistD(c, (double)i / steps, p);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            double lo = Math.Max(0.0, (best - 1.0) / steps), hi = Math.Min(1.0, (best + 1.0) / steps);
            for (int i = 0; i < 60; i++)
            {
                double m1 = hi - (hi - lo) * 0.6180339887498949, m2 = lo + (hi - lo) * 0.6180339887498949;
                if (DistD(c, m1, p) < DistD(c, m2, p))
                    hi = m2;
                else
                    lo = m1;
            }

            return (lo + hi) * 0.5;
        }

        /// <summary>Where a place on the old curve goes on the new one, every step but the table's lookups in double precision: the answer both the reference and the new code are measured against.</summary>
        internal static void ExactPlace(Bezier3 old, Bezier3 now, Vec3 place, float yaw, FollowMode mode, out Vec3 position, out float newYaw)
        {
            double t = ExactT(old, place);
            double[] on = PointD(old, t), h = TangentD(old, t);
            double dx = place.X - on[0], dz = place.Z - on[2];
            double along = dx * h[0] + dz * h[1], lateral = dx * h[1] - dz * h[0], height = place.Y - on[1];
            double relYaw = RoadRelation.Wrap((float)(yaw - Math.Atan2(h[0], h[1])));
            var oldArc = new ArcTable(old);
            var nowArc = new ArcTable(now);
            double fraction = oldArc.SAt((float)t) / oldArc.Length;
            double s = mode == FollowMode.NearestEnd ? (fraction < 0.5 ? fraction * oldArc.Length : nowArc.Length - (oldArc.Length - fraction * oldArc.Length)) : fraction * nowArc.Length;
            double t2 = nowArc.TAt((float)s);
            double[] on2 = PointD(now, t2), h2 = TangentD(now, t2);
            position = new Vec3((float)(on2[0] + h2[0] * along + h2[1] * lateral), (float)(on2[1] + height), (float)(on2[2] + h2[1] * along - h2[0] * lateral));
            newYaw = RoadRelation.Wrap((float)(relYaw + Math.Atan2(h2[0], h2[1])));
        }

        private static void Relations()
        {
            var random = new Random(3);
            double sameNew = 0.0, sameRef = 0.0, bentNew = 0.0, bentRef = 0.0, yawNew = 0.0, yawRef = 0.0, vsRef = 0.0;
            int measured = 0, ties = 0;
            for (int k = 0; k < 4000; k++)
            {
                Bezier3 c = RandomCurve(random, 0f, 0.1f, 60f);
                Bezier3 other = k % 3 == 0 ? Rigid(c, new Vec3(20f, 1f, -35f), 0.9f) : Neighbour(c, new Vec3((float)(random.NextDouble() * 30 - 15), 0.5f, (float)(random.NextDouble() * 30 - 15)));
                var table = new FollowCurve(c);
                var refArc = new RefArc(c);
                Vec3 p = RandomPlace(random, c);
                float yaw = (float)(random.NextDouble() * 6.0 - 3.0);
                bool a = RefRelation.TryMeasure(refArc, p, yaw, out RefRelation r), b = table.TryMeasure(p, yaw, -1f, out RoadRelation n, out float tMine);
                Program.Check(a == b, "the same roads can be measured");
                if (!a || !b)
                    continue;
                if (Math.Abs(tMine - ExactT(c, p)) > 2e-3)
                {
                    // Two nearest points as near as each other: either is an answer.
                    ties++;
                    continue;
                }

                measured++;
                var ot = new FollowCurve(other);
                var otherRef = new RefArc(other);
                foreach (FollowMode mode in new[] { FollowMode.Fraction, FollowMode.NearestEnd })
                {
                    ExactPlace(c, c, p, yaw, mode, out _, out float exactSameYaw);
                    ExactPlace(c, other, p, yaw, mode, out Vec3 exactBent, out float exactBentYaw);
                    r.TryApply(refArc, mode, out Vec3 pr, out float yr);
                    table.TryApply(n, mode, out Vec3 pn, out float yn);
                    sameNew = Math.Max(sameNew, (pn - p).Length);
                    sameRef = Math.Max(sameRef, (pr - p).Length);
                    r.TryApply(otherRef, mode, out Vec3 br, out float byr);
                    ot.TryApply(n, mode, out Vec3 bn, out float byn);
                    bentNew = Math.Max(bentNew, (bn - exactBent).Length);
                    bentRef = Math.Max(bentRef, (br - exactBent).Length);
                    vsRef = Math.Max(vsRef, (bn - br).Length);
                    yawNew = Math.Max(yawNew, Math.Max(Math.Abs(RoadRelation.Wrap(yn - exactSameYaw)), Math.Abs(RoadRelation.Wrap(byn - exactBentYaw))));
                    yawRef = Math.Max(yawRef, Math.Max(Math.Abs(RoadRelation.Wrap(yr - exactSameYaw)), Math.Abs(RoadRelation.Wrap(byr - exactBentYaw))));
                }
            }

            Program.Check(measured > 3300 && ties < 150, $"most places can be measured: {measured}, ties of two nearest points set aside: {ties}");
            Program.Check(sameNew < 2e-5, $"on the curve it was measured on, the place comes back within 0.02 mm (the float rounding at 100 m): {sameNew * 1000.0:0.0000} mm; the reference's own: {sameRef * 1000.0:0.0000} mm");
            Program.Check(bentNew < 1e-4, $"on a moved or bent curve it is within 0.1 mm of the exact place (double precision): {bentNew * 1000.0:0.0000} mm; the reference's own: {bentRef * 1000.0:0.0000} mm; the two differ by {vsRef * 1000.0:0.0000} mm");
            Program.Check(yawNew < 2e-5, $"and the yaw is the exact one: {yawNew:0.0000000} rad; the reference's own: {yawRef:0.0000000}");
            Program.Check(vsRef <= bentRef + bentNew + 1e-5, "the new code differs from the reference by no more than the reference is off the exact place");
        }

        private static void RigidMotions()
        {
            var random = new Random(4);
            int found = 0, notFound = 0, falseLive = 0;
            double worstImage = 0.0, worstVsFull = 0.0;
            for (int k = 0; k < 4000; k++)
            {
                Bezier3 c = RandomCurve(random, k % 4 == 0 ? 6000f : 100f, 0.1f);
                float turn = (float)(random.NextDouble() * 6.0 - 3.0);
                Bezier3 moved = Rigid(c, new Vec3((float)(random.NextDouble() * 400 - 200), (float)(random.NextDouble() * 6 - 3), (float)(random.NextDouble() * 400 - 200)), turn);
                // The far handles' errors that a float curve at 6 km has: not the motion's.
                bool exact = RigidMotion.TryDetect(c, moved, RigidMotion.Tolerance, out RigidMotion m);
                if (exact)
                {
                    found++;
                    // Property: every point of the curve is within the limit of the image. The limit is the tolerance at small coordinates and the float rounding at 6 km.
                    float reach = Math.Max(1f, Math.Max(Math.Abs(c.A.X), Math.Abs(c.A.Z)) + 200f), limit = Math.Max(RigidMotion.Tolerance, 2f * reach * 1.1920929e-7f);
                    for (int i = 0; i <= 20; i++)
                        worstImage = Math.Max(worstImage, (moved.Point(i / 20f) - m.Map(c.Point(i / 20f))).Length / limit);
                    // And the follow by the motion is the full follow's.
                    var oldArc = new RefArc(c);
                    var nowArc = new RefArc(moved);
                    var table = new FollowCurve(moved);
                    Vec3 place = RandomPlace(random, c);
                    if (k % 4 != 0 && RefRelation.TryMeasure(oldArc, place, 0.4f, out RefRelation r) && r.TryApply(nowArc, FollowMode.Fraction, out Vec3 full, out float fullYaw))
                    {
                        r.TryApply(oldArc, FollowMode.Fraction, out Vec3 onOld, out float oldYaw);
                        worstVsFull = Math.Max(worstVsFull, (m.Map(onOld) - full).Length);
                        worstVsFull = Math.Max(worstVsFull, Math.Abs(RoadRelation.Wrap(m.MapYaw(oldYaw) - fullYaw)) * 50.0);
                    }
                }
                else
                {
                    notFound++;
                }

                // A bend: a control point moved by more than the tolerance (twice the float noise at 6 km at least) is never taken for a rigid motion.
                float amount = k % 4 == 0 ? 0.01f : 0.0006f;
                var bentCurve = new Bezier3(moved.A, moved.B + new Vec3(amount, 0f, 0f), moved.C, moved.D);
                if (RigidMotion.TryDetect(c, bentCurve, RigidMotion.Tolerance, out _) && k % 4 != 0)
                    falseLive++;
                float stretch = k % 4 == 0 ? 0.02f : 0.0006f;
                var longer = new Bezier3(moved.A, moved.B, moved.C, moved.D + Vec3.Lerp(Vec3.Zero, moved.D - moved.A, stretch / Math.Max(1f, (moved.D - moved.A).Length)));
                if (RigidMotion.TryDetect(c, longer, RigidMotion.Tolerance, out _) && k % 4 != 0)
                    falseLive++;
            }

            Program.Check(found > 3500, $"a curve moved and turned is found to be: {found} of 4000 (the others are the float rounding at 6 km, {notFound})");
            Program.Check(worstImage < 2.0, $"every point of a curve taken as rigid is within the limit of the image (the limit: 0.1 mm, or the float rounding at the coordinates): {worstImage:0.00} of the limit");
            Program.Check(worstVsFull < 1e-3, $"the motion's place is the full follow's: {worstVsFull * 1000.0:0.000} mm");
            Program.Check(falseLive == 0, $"a control point moved 0.6 mm, or an end 0.6 mm farther, is never rigid: {falseLive} taken");
            Bezier3 straight = Bezier3.Line(Vec3.Zero, new Vec3(0f, 0f, 100f));
            Program.Check(!RigidMotion.TryDetect(straight, Neighbour(straight, new Vec3(5f, 0f, 0f)), RigidMotion.Tolerance, out _), "a straight road whose end was dragged sideways is not rigid");
            Program.Check(!RigidMotion.TryDetect(straight, new Bezier3(Vec3.Zero, new Vec3(float.NaN, 0f, 0f), Vec3.Zero, Vec3.Zero), RigidMotion.Tolerance, out _), "nor a curve that is not a number");
        }

        private static void Batch()
        {
            var random = new Random(5);
            double worst = 0.0, worstYaw = 0.0, worstRigidEdge = 0.0;
            int compared = 0, mismatched = 0, rigidItems = 0, fullItems = 0;
            var batch = new FollowBatch(4);
            var cache = new FollowCache();
            var counts = new int[4];
            for (int round = 0; round < 300; round++)
            {
                batch.Clear();
                int roads = 1 + random.Next(6);
                var old = new Bezier3[roads];
                var now = new Bezier3[roads];
                var itemsOf = new List<(int road, RefRelation r, Vec3 at, float yaw, Vec3 current, float currentYaw, FollowMode mode, int kind)>();
                var refs = new RefArc[roads];
                var nowRefs = new RefArc[roads];
                for (int e = 0; e < roads; e++)
                {
                    old[e] = RandomCurve(random, 500f, 0.1f);
                    now[e] = e % 3 == 0 ? Rigid(old[e], new Vec3(30f, 2f, -10f), 0.5f) : e % 3 == 1 ? Neighbour(old[e], new Vec3(12f, 0.3f, -9f)) : old[e];
                    refs[e] = new RefArc(old[e]);
                    nowRefs[e] = new RefArc(now[e]);
                    batch.AddEdge(cache.Get(old[e]));
                    batch.SetNow(e, cache.Get(now[e]), true);
                }

                FollowMode mode = round % 2 == 0 ? FollowMode.Fraction : FollowMode.NearestEnd;
                for (int i = 0, n = 1 + random.Next(40); i < n; i++)
                {
                    int e = random.Next(roads);
                    Vec3 place = RandomPlace(random, old[e]);
                    float yaw = (float)(random.NextDouble() * 6.0 - 3.0);
                    var fresh = new FollowCurve(old[e]);
                    if (!fresh.TryMeasure(place, yaw, -1f, out RoadRelation n2, out _))
                        continue;
                    var r = new RefRelation { Fraction = n2.Fraction, Length = n2.Length, Along = n2.Along, Lateral = n2.Lateral, Height = n2.Height, Yaw = n2.Yaw, PlacedAt = place, PlacedYaw = yaw };
                    // kind 0: where the relation put it; 1: moved by the player; 2: turned by the player; 3: last written elsewhere (the road moved before).
                    int kind = random.Next(8) < 5 ? 0 : 1 + random.Next(2);
                    Vec3 current = place;
                    float currentYaw = yaw;
                    if (kind == 1)
                        current += new Vec3(0.3f, 0f, 0f);
                    else if (kind == 2)
                        currentYaw += 0.5f;
                    batch.Add(e, n2, place, yaw, current, currentYaw);
                    itemsOf.Add((e, r, place, yaw, current, currentYaw, mode, kind));
                }

                batch.Run(mode, 0, 1000);
                rigidItems += batch.RigidItems;
                fullItems += batch.FullItems;
                for (int i = 0; i < itemsOf.Count; i++)
                {
                    var item = itemsOf[i];
                    FollowOutcome expected = item.r.Follow(nowRefs[item.road], mode, item.current, item.currentYaw, out Vec3 expectedAt, out float expectedYaw);
                    FollowOutcome got = batch.Result(i, out Vec3 at, out float yaw);
                    compared++;
                    counts[(int)got]++;
                    if (got != expected)
                    {
                        // Only a place at the edge of the write tolerance may be called either.
                        item.r.TryApply(nowRefs[item.road], mode, out Vec3 full, out _);
                        float distance = (full - item.current).Length;
                        bool either = (expected == FollowOutcome.Unchanged || expected == FollowOutcome.Moved) && (got == FollowOutcome.Unchanged || got == FollowOutcome.Moved);
                        if (!either || Math.Abs(distance - RoadRelation.WriteTolerance) > 5e-5f)
                            mismatched++;
                        continue;
                    }

                    if (got == FollowOutcome.Moved)
                    {
                        if (item.road % 3 == 0)
                            worstRigidEdge = Math.Max(worstRigidEdge, (at - expectedAt).Length);
                        else
                            worst = Math.Max(worst, (at - expectedAt).Length);
                        worstYaw = Math.Max(worstYaw, Math.Abs(RoadRelation.Wrap(yaw - expectedYaw)));
                    }
                    else if (got == FollowOutcome.Detached || got == FollowOutcome.Unchanged)
                    {
                        Program.Check((at - item.current).Length == 0f, "an item that is not to be written keeps its own place");
                    }
                }
            }

            Program.Check(compared > 3000 && mismatched == 0, $"every outcome is the reference's: {compared} items, {mismatched} unlike");
            Program.Check(worst < 1e-4 && worstYaw < 1e-4, $"every place the full follow writes is the reference's within 0.1 mm (float rounding at 500 m): {worst * 1000.0:0.0000} mm, yaw {worstYaw:0.0000000}");
            Program.Check(worstRigidEdge < 2.5e-4, $"and by a rigid motion within 0.1 mm of tolerance and the float rounding at 500 m: {worstRigidEdge * 1000.0:0.0000} mm");
            Program.Check(counts[(int)FollowOutcome.Moved] > 500 && counts[(int)FollowOutcome.Detached] > 200 && counts[(int)FollowOutcome.Unchanged] > 100, $"the cases were there: moved {counts[1]}, detached {counts[2]}, unchanged {counts[0]}");
            Program.Check(rigidItems > 100 && fullItems > 100, $"both ways were taken: rigid {rigidItems}, full {fullItems}");
            var gone = new FollowBatch();
            gone.AddEdge(new FollowCurve(Bezier3.Line(Vec3.Zero, new Vec3(0f, 0f, 50f))));
            var dot = new Vec3(1f, 0f, 1f);
            gone.SetNow(0, new FollowCurve(new Bezier3(dot, dot, dot, dot)), true);
            new FollowCurve(Bezier3.Line(Vec3.Zero, new Vec3(0f, 0f, 50f))).TryMeasure(new Vec3(2f, 0f, 20f), 0f, -1f, out RoadRelation one, out _);
            gone.Add(0, one, new Vec3(2f, 0f, 20f), 0f, new Vec3(2f, 0f, 20f), 0f);
            gone.Run(FollowMode.Fraction, 0, 10);
            Program.Check(gone.Result(0, out _, out _) == FollowOutcome.Unmeasurable, "a road made into a dot takes nothing");
        }

        private static void Cache()
        {
            var cache = new FollowCache(4);
            var a = Bezier3.Line(Vec3.Zero, new Vec3(0f, 0f, 50f));
            FollowCurve first = cache.Get(a);
            Program.Check(ReferenceEquals(first, cache.Get(a)) && cache.Hits == 1 && cache.Misses == 1, "the same curve is the same table");
            Program.Check(!ReferenceEquals(first, cache.Get(Bezier3.Line(Vec3.Zero, new Vec3(0f, 0f, 50.001f)))), "another curve is another table");
            Program.Check(first.IsCurve(a) && !first.IsCurve(Bezier3.Line(Vec3.Zero, new Vec3(0f, 0f, 50.001f))), "a table is checked against the numbers of its curve: a collision of the hash is a miss");
            for (int i = 0; i < 20; i++)
                cache.Get(Bezier3.Line(Vec3.Zero, new Vec3(0f, 0f, 60f + i)));
            Program.Check(cache.Count <= 8, $"two generations of 4 at most: {cache.Count}");
            Program.Check(cache.Get(Bezier3.Line(Vec3.Zero, new Vec3(0f, 0f, 79f))).Length > 78f, "the newest is still there");
        }

        private static void Lines()
        {
            var random = new Random(6);
            double worstMap = 0.0, worstText = 0.0, worstRefMap = 0.0;
            int onTheLimit = 0;
            double worstHeight = 0.0;
            int wrongRoad = 0;
            int compared = 0;
            var work = new LineWorkspace();
            for (int k = 0; k < 600; k++)
            {
                int roads = 1 + random.Next(3);
                var old = new Bezier3[roads];
                var now = new Bezier3[roads];
                Vec3 at = new Vec3(100f, 0f, 100f);
                for (int e = 0; e < roads; e++)
                {
                    // Roads end to end.
                    Bezier3 c = RandomCurve(random, 0f, 0.08f);
                    Vec3 shift = at - c.A;
                    old[e] = new Bezier3(c.A + shift, c.B + shift, c.C + shift, c.D + shift);
                    at = old[e].D;
                    now[e] = e % 2 == 0 ? Neighbour(old[e], new Vec3(6f, 0.2f, -4f)) : old[e];
                }

                var points = new List<Vec3>();
                var tables = new FollowCurve[roads];
                var tablesNow = new FollowCurve[roads];
                var pairs = new RefRelation.Pair[roads];
                for (int e = 0; e < roads; e++)
                {
                    tables[e] = new FollowCurve(old[e]);
                    tablesNow[e] = new FollowCurve(now[e]);
                    pairs[e] = new RefRelation.Pair { Old = new RefArc(old[e]), Now = new RefArc(now[e]) };
                    float lateral = (float)(random.NextDouble() * 10.0 - 5.0);
                    for (int i = 0; i < 6; i++)
                    {
                        float t = 0.05f + 0.9f * i / 5f;
                        points.Add(old[e].Point(t) + old[e].Tangent(t).RightXZ() * lateral);
                    }
                }

                string path = RunPath.Encode(points, out float yaw);
                if (path.Length == 0)
                    continue;
                bool a = RefPathFollow.TryRemap(path, points[0], yaw, pairs, FollowMode.Fraction, out string refPath, out Vec3 refPos, out float refYaw);
                bool b = LineFollow.TryMeasure(path, points[0], yaw, tables, roads, work, out LineRelation line);
                Program.Check(a == b, "a line that can be followed by the reference can be measured");
                if (!a || !b)
                    continue;
                LineOutcome outcome = LineFollow.Apply(line, tablesNow, FollowMode.Fraction, 0f, work, out string newPath, out Vec3 newPos, out float newYaw, out _);
                var refMappedNow = new List<Vec3>();
                List<Vec3> worldLine = MarkingPolygon.ToWorld(RunPath.Decode(path), points[0], yaw);
                RefRelation.TryMapPoints(pairs, FollowMode.Fraction, worldLine, refMappedNow);
                // The follow puts a mapped point that steps back along the line on the last one kept (RunPath.StepForward, 0.12.97; the reference has no such rule, and these lines, made of points
                // of roads that have nothing to do with each other, step back at every road): the exact places go through the same rule before they are compared with the map.
                int placed = Math.Min(line.Points, refMappedNow.Count);
                var exactPlaces = new Vec3[placed];
                for (int i = 0; i < placed; i++)
                {
                    Vec3 world = worldLine[i];
                    int chosen = line.Road[i], exactRoad = -1;
                    double nearest = double.MaxValue, mineDistance = 0.0;
                    var exactOn = new double[roads][];
                    var exactDistance = new double[roads];
                    for (int e2 = 0; e2 < roads; e2++)
                    {
                        double[] on = PointD(old[e2], ExactT(old[e2], world));
                        double distance = Math.Sqrt((on[0] - world.X) * (on[0] - world.X) + (on[2] - world.Z) * (on[2] - world.Z));
                        exactOn[e2] = on;
                        exactDistance[e2] = distance;
                        if (e2 == chosen)
                            mineDistance = distance;
                    }

                    // The road at the point's level among those that lie over one another (RoadLevels): the rule is the same, the distances are exact.
                    for (int e2 = 0; e2 < roads; e2++)
                    {
                        bool atLevel = Math.Abs(world.Y - exactOn[e2][1]) <= RoadLevels.LevelGap;
                        int onLevel = 0;
                        for (int e3 = 0; e3 < roads; e3++)
                            onLevel += Math.Abs(world.Y - exactOn[e3][1]) <= RoadLevels.LevelGap ? 1 : 0;
                        bool displaced = false;
                        if (!atLevel && onLevel > 0 && onLevel < roads)
                        {
                            for (int e3 = 0; e3 < roads; e3++)
                                displaced |= Math.Abs(world.Y - exactOn[e3][1]) <= RoadLevels.LevelGap && Math.Abs(exactOn[e2][1] - exactOn[e3][1]) > RoadLevels.StackGap;
                        }

                        if (!displaced && exactDistance[e2] < nearest)
                        {
                            nearest = exactDistance[e2];
                            exactRoad = e2;
                        }
                    }

                    if (chosen != exactRoad && mineDistance - nearest > 1e-4)
                        wrongRoad++;
                    ExactPlace(old[chosen], now[chosen], world, 0f, FollowMode.Fraction, out Vec3 exact, out _);
                    exactPlaces[i] = exact;
                    worstRefMap = Math.Max(worstRefMap, (refMappedNow[i] - exact).Length);
                }

                RunPath.StepForward(exactPlaces, placed);
                for (int i = 0; i < placed; i++)
                    worstMap = Math.Max(worstMap, (work.Mapped[i] - exactPlaces[i]).Length);
                if (outcome == LineOutcome.Unchanged)
                {
                    List<Vec3> expected = MarkingPolygon.ToWorld(RunPath.Decode(refPath), refPos, refYaw), had = MarkingPolygon.ToWorld(RunPath.Decode(path), points[0], yaw);
                    for (int i = 0; i < Math.Min(expected.Count, had.Count); i++)
                        worstText = Math.Max(worstText, (expected[i] - had[i]).Length);
                    compared++;
                    continue;
                }

                Program.Check(outcome == LineOutcome.Rewritten || outcome == LineOutcome.PoseOnly, "a line on roads that moved is written");
                // A chord whose turn is within a hair of the limit of RunPath.SpurCosine is a toss-up between the points as mapped and the points read back from the text (rounded to a centimetre and 0.1 mrad):
                // the two sides may then leave out different points. Such a line is not compared in its text (the owner's real lines are never that near; a few of the 600 random ones are).
                if (OnTheLimit(refMappedNow))
                {
                    onTheLimit++;
                    compared++;
                    continue;
                }

                List<Vec3> mine = MarkingPolygon.ToWorld(RunPath.Decode(newPath), newPos, newYaw), theirs = MarkingPolygon.ToWorld(RunPath.Decode(refPath), refPos, refYaw);
                Program.Check(mine.Count == theirs.Count, "the same number of points");
                // On the ground to the text's own resolution; in height to a grade's (a thousandth of a chord: 4 cm on one of 40 m, as these lines have): which chords carry a height step is not the reference's once
                // the points that step back are left out.
                for (int i = 0; i < Math.Min(mine.Count, theirs.Count); i++)
                {
                    worstText = Math.Max(worstText, Vec3.DistanceXZ(mine[i], theirs[i]));
                    worstHeight = Math.Max(worstHeight, Math.Abs(mine[i].Y - theirs[i].Y));
                }

                compared++;
            }

            Program.Check(compared > 400 && worstMap < 1.5e-4 && wrongRoad == 0, $"the points of the map are the exact places (double precision) within 0.15 mm (float rounding at 600 m), each by the nearest road: {worstMap * 1000.0:0.000} mm, {wrongRoad} by another road, of {compared} lines; the reference's own: {worstRefMap * 1000.0:0.0} mm");
            Program.Check(worstText < 0.04, $"and the text written is the reference's text within its noise and the text's own rounding: {worstText * 1000.0:0.0} mm");
            Program.Check(worstHeight < 0.08, $"and its heights are the reference's within a grade's rounding on these long chords: {worstHeight * 1000.0:0.0} mm");
            Program.Check(onTheLimit <= 6, $"few of the random lines sit on the limit of the step-back rule: {onTheLimit} of {compared}");
            LinesThreshold(work);
        }

        /// <summary>Whether a chord of the points turns back on the last kept one within 0.003 of the cosine RunPath.SpurCosine (the same walk as RunPath.StepForward).</summary>
        private static bool OnTheLimit(List<Vec3> points)
        {
            float dirX = 0f, dirZ = 0f;
            Vec3 last = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                float dx = points[i].X - last.X, dz = points[i].Z - last.Z, length = (float)Math.Sqrt(dx * dx + dz * dz);
                if (length > 1e-4f && (dirX != 0f || dirZ != 0f))
                {
                    float cosine = (dx * dirX + dz * dirZ) / length;
                    if (Math.Abs(cosine - RunPath.SpurCosine) < 0.003f)
                        return true;
                    if (cosine < RunPath.SpurCosine)
                        continue;
                }

                if (length > 1e-4f)
                {
                    dirX = dx / length;
                    dirZ = dz / length;
                }

                last = points[i];
            }

            return false;
        }

        private static void LinesThreshold(LineWorkspace work)
        {
            Bezier3 road = new Bezier3(new Vec3(0f, 0f, 0f), new Vec3(8f, 1f, 20f), new Vec3(8f, 2f, 40f), new Vec3(0f, 3f, 60f));
            var table = new FollowCurve(road);
            var points = new List<Vec3>();
            for (int i = 0; i <= 24; i++)
            {
                float t = table.TAt(table.Length * (0.1f + 0.8f * i / 24f));
                points.Add(road.Point(t) + road.Tangent(t).RightXZ() * 3f);
            }

            string path = RunPath.Encode(points, out float yaw);
            var tables = new[] { table };
            LineFollow.TryMeasure(path, points[0], yaw, tables, 1, work, out LineRelation line);
            Program.Check(LineFollow.Apply(line, tables, FollowMode.Fraction, 0f, work, out _, out _, out _, out _) == LineOutcome.Unchanged, "the road as it was: nothing to write");
            // Moved: the text is kept, the pose is written.
            var shifted = new[] { new FollowCurve(Rigid(road, new Vec3(30f, 1f, -20f), 0.7f)) };
            LineOutcome moved = LineFollow.Apply(line, shifted, FollowMode.Fraction, 0f, work, out string keptPath, out Vec3 keptPos, out float keptYaw, out _);
            RigidMotion.TryDetect(road, shifted[0].Curve, RigidMotion.Tolerance, out RigidMotion motion);
            Program.Check(moved == LineOutcome.PoseOnly && keptPath == path && (keptPos - motion.Map(points[0])).Length < 0.002f && Math.Abs(RoadRelation.Wrap(keptYaw - motion.MapYaw(yaw))) < 1e-4f, $"a road moved and turned: the same text, the place and yaw the motion gives: {moved}");
            LineFollow.TryMeasure(path, points[0], yaw, tables, 1, work, out line);
            // A drag in steps of 0.4 mm for 300 frames: the line is written about every third frame and is never more than the threshold from where the road puts it.
            double drift = 0.0;
            int written = 0;
            for (int f = 1; f <= 300; f++)
            {
                float shift = 0.0004f * f;
                var creep = new[] { new FollowCurve(Rigid(road, new Vec3(shift, 0f, 0f), 0f)) };
                if (LineFollow.Apply(line, creep, FollowMode.Fraction, 0f, work, out _, out _, out _, out _) != LineOutcome.Unchanged)
                    written++;
                drift = Math.Max(drift, (line.Position - (points[0] + new Vec3(shift, 0f, 0f))).Length);
            }

            Program.Check(written > 80 && written < 130 && drift < 0.00105, $"steps of 0.4 mm: {written} writes in 300 frames, the line never more than {drift * 1000.0:0.000} mm from the road's place");
            LineFollow.TryMeasure(path, points[0], yaw, tables, 1, work, out line);
            var bigger = new[] { new FollowCurve(Rigid(road, new Vec3(0.0025f, 0f, 0f), 0f)) };
            Program.Check(LineFollow.Apply(line, bigger, FollowMode.Fraction, 0f, work, out _, out _, out _, out _) == LineOutcome.PoseOnly, "2.5 mm is written (as a pose: the shape is the same)");
            // A bend: the text is made again.
            LineFollow.TryMeasure(path, points[0], yaw, tables, 1, work, out line);
            var bent = new[] { new FollowCurve(Neighbour(road, new Vec3(9f, 0f, 3f))) };
            Program.Check(LineFollow.Apply(line, bent, FollowMode.Fraction, 0f, work, out string text, out _, out _, out _) == LineOutcome.Rewritten && text != path, "a bent road: the text is made again");
            // Longer by a half: the chords grow; with a limit they are cut to it as far as 32 pieces go.
            LineFollow.TryMeasure(path, points[0], yaw, tables, 1, work, out line);
            var longer = new[] { new FollowCurve(new Bezier3(road.A, road.B + new Vec3(0f, 0f, 5f), road.C + new Vec3(0f, 0f, 10f), road.D + new Vec3(0f, 0f, 15f))) };
            LineFollow.Apply(line, longer, FollowMode.Fraction, 0f, work, out _, out _, out _, out LineStats plain);
            LineFollow.TryMeasure(path, points[0], yaw, tables, 1, work, out line);
            LineOutcome cut = LineFollow.Apply(line, longer, FollowMode.Fraction, line.LongestChord + 0.01f, work, out string cutPath, out _, out _, out LineStats limited);
            Program.Check(plain.LongestChord > line.LongestChord * 1.15f && limited.LongestChord <= line.LongestChord + 0.03f && limited.Pieces >= 24 && limited.Pieces <= RunPath.MaxPieces && cut == LineOutcome.Rewritten,
                $"a road made 1.25 times longer: chords {plain.LongestChord:0.00} m grow to {limited.LongestChord:0.00} m again with {limited.Pieces} pieces (the limit {line.LongestChord:0.00})");
            Program.Check(RunPath.Decode(cutPath).Count == limited.Pieces + 1, "and the text has them");
            // More than 32 pieces wanted: the text holds 32 and says how many it asked for.
            LineFollow.TryMeasure(path, points[0], yaw, tables, 1, work, out line);
            LineFollow.Apply(line, longer, FollowMode.Fraction, line.LongestChord * 0.5f, work, out string cappedPath, out _, out _, out LineStats capped);
            Program.Check(capped.Pieces == RunPath.MaxPieces && capped.Wanted > RunPath.MaxPieces && RunPath.Decode(cappedPath).Count == RunPath.MaxPieces + 1 && capped.LongestChord <= plain.LongestChord + 0.01f, $"asked for {capped.Wanted}, the text holds {capped.Pieces}, and no chord is longer than the uncut line's ({capped.LongestChord:0.00} against {plain.LongestChord:0.00} m)");
            var dot = new Vec3(1f, 0f, 1f);
            Program.Check(LineFollow.Apply(line, new[] { new FollowCurve(new Bezier3(dot, dot, dot, dot)) }, FollowMode.Fraction, 0f, work, out _, out _, out _, out _) == LineOutcome.Failed, "a road with no shape fails the line and writes nothing");
        }
    }
}
