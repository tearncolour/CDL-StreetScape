using System;
using System.Collections.Generic;
using System.Globalization;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The adaptive sampler as SurfaceReader wires it (0.12.75): the bands of a half and of a junction part, the fingerprint, the shadow comparison (deviation, corners, areas), the gate that bounds its cost,
    /// and the statistics and the summary schedule; and the shadow against the fixed sampler on random roads. The game's own data (the curves and compositions of a real road, the conversion of its
    /// types) is not here: Tests/GameMath checks the conversion against the game's own functions.
    /// </summary>
    internal static class SamplingTests
    {
        public static void RunAll()
        {
            Program.Run("sampling: the fingerprint tells the samplers apart", Print);
            Program.Run("sampling: the fixed places, and the bands of a half are the adaptive ones of each stretch", Half);
            Program.Run("sampling: a junction part has one list of places for every curve, so its pieces meet exactly", Node);
            Program.Run("sampling: the measure of a sampled part against its curves", Measured);
            Program.Run("sampling: corners, pieces and areas of bands", Bands);
            Program.Run("sampling: the gate bounds what the shadow costs a frame", Gate);
            Program.Run("sampling: the statistics, the worst parts and the verdict", Stats);
            Program.Run("sampling: the summary is due at 100, at 2000 and every three minutes, at most six times", Schedule);
            Program.Run("sampling: the shadow against the fixed sampler on random roads", Equivalence);
        }

        private const float Eps = 1e-5f;

        // ------------------------------------------------------------------ fixtures

        /// <summary>A random centre line of a road: steps of 8 to 30 m turning up to 40 degrees at each control point, heights moving up to 1 m (the fixture of AdaptiveTests).</summary>
        internal static Vec3[] Centre(Random random, double turn = 0.7)
        {
            var c = new Vec3[4];
            double heading = random.NextDouble() * Math.PI * 2.0;
            double x = 0.0, z = 0.0, y = 0.0;
            for (int i = 0; i < 4; i++)
            {
                c[i] = new Vec3((float)x, (float)y, (float)z);
                double length = 8.0 + random.NextDouble() * 22.0;
                heading += (random.NextDouble() - 0.5) * turn;
                x += Math.Sin(heading) * length;
                z += Math.Cos(heading) * length;
                y += (random.NextDouble() - 0.5) * 2.0;
            }

            return c;
        }

        /// <summary>The cubic that is the centre line moved sideways by offset (m, positive to the left of the way it goes) at each control point.</summary>
        internal static Bezier3 Side(Vec3[] c, float offset)
        {
            var p = new Vec3[4];
            for (int i = 0; i < 4; i++)
            {
                Vec3 ahead = (c[Math.Min(3, i + 1)] - c[Math.Max(0, i - 1)]).HeadingXZ();
                p[i] = c[i] + new Vec3(-ahead.Z, 0f, ahead.X) * offset;
            }

            return new Bezier3(p[0], p[1], p[2], p[3]);
        }

        internal static List<PaintableStretch> Stretches(Random random)
        {
            var list = new List<PaintableStretch>();
            float at = 0f;
            while (at < 0.95f && list.Count < 4)
            {
                float next = Math.Min(1f, at + 0.1f + (float)random.NextDouble() * 0.4f);
                if (random.Next(4) != 0)
                    list.Add(new PaintableStretch(at, next, (float)random.NextDouble() * 0.2f));
                at = next;
            }

            if (list.Count == 0)
                list.Add(new PaintableStretch(0f, 1f, 0f));
            return list;
        }

        internal static List<SurfaceStrip> FixedBands(Bezier3 left, Bezier3 right, IList<PaintableStretch> stretches, string source)
        {
            var bands = new List<SurfaceStrip>();
            foreach (PaintableStretch s in stretches)
                bands.AddRange(SurfaceSection.Bands(t => left.Point(t), t => right.Point(t), s, PartSampling.FixedSamples, source));
            return bands;
        }

        private static bool SamePoints(SurfaceStrip a, SurfaceStrip b)
        {
            if (a.Left.Count != b.Left.Count || a.Right.Count != b.Right.Count)
                return false;
            for (int i = 0; i < a.Left.Count; i++)
            {
                if (a.Left[i].X != b.Left[i].X || a.Left[i].Y != b.Left[i].Y || a.Left[i].Z != b.Left[i].Z || a.Right[i].X != b.Right[i].X || a.Right[i].Y != b.Right[i].Y || a.Right[i].Z != b.Right[i].Z)
                    return false;
            }

            return a.Source == b.Source && a.Filler == b.Filler;
        }

        // ------------------------------------------------------------------ the fingerprint

        private static ulong PrintOf(bool adaptive)
        {
            var f = new Fingerprint();
            f.Add(42);
            PartSampling.AddPrint(ref f, adaptive);
            return f.Value;
        }

        private static void Print()
        {
            Program.Check(PrintOf(false) != PrintOf(true), "a part made by the fixed sampler and one by the adaptive have other fingerprints");
            Program.Check(PrintOf(true) == PrintOf(true) && PrintOf(false) == PrintOf(false), "the same sampler is the same fingerprint");

            // Everything the adaptive output depends on is in it, in this order: a change of any of them is another fingerprint (a result made with other tolerances is never served).
            var expected = new Fingerprint();
            expected.Add(42);
            expected.Add(1);
            expected.Add(PartSampling.Version);
            expected.Add(PartSampling.TolXz);
            expected.Add(PartSampling.TolY);
            expected.Add(Zones.DefaultLength);
            expected.Add(Zones.DefaultStep);
            expected.Add(Flatten.MaxSegments);
            expected.Add(PartSampling.FixedSamples);
            Program.Check(expected.Value == PrintOf(true), "the adaptive print is the version, both tolerances, the zone, the most steps and the fixed floor");
            var fixedPrint = new Fingerprint();
            fixedPrint.Add(42);
            fixedPrint.Add(0);
            Program.Check(fixedPrint.Value == PrintOf(false), "the fixed print is only that it is the fixed one (no tolerance: it has none)");
            var other = new Fingerprint();
            other.Add(42);
            other.Add(1);
            other.Add(PartSampling.Version + 1);
            other.Add(PartSampling.TolXz);
            other.Add(PartSampling.TolY);
            other.Add(Zones.DefaultLength);
            other.Add(Zones.DefaultStep);
            other.Add(Flatten.MaxSegments);
            other.Add(PartSampling.FixedSamples);
            Program.Check(other.Value != PrintOf(true), "another version is another print");
            Program.Check(PartSampling.TolXz == 0.010f && PartSampling.TolY == 0.003f && PartSampling.Version >= 1, "10 mm in the plan, 3 mm in height (the plan's numbers)");
        }

        // ------------------------------------------------------------------ the bands

        private static void Half()
        {
            IList<float> places = PartSampling.FixedAt;
            Program.Check(places.Count == PartSampling.FixedSamples + 1 && places[0] == 0f && places[16] == 1f && Math.Abs(places[5] - 5f / 16f) < Eps, "the fixed places are 17 equal ones");
            bool readOnly = false;
            try
            {
                places[3] = 0.5f;
            }
            catch (NotSupportedException)
            {
                readOnly = true;
            }

            Program.Check(readOnly, "and they cannot be changed by a caller");

            var random = new Random(20261103);
            int bad = 0, shared = 0, zoned = 0;
            for (int i = 0; i < 200; i++)
            {
                Vec3[] c = Centre(random);
                Bezier3 left = Side(c, 1.75f), right = Side(c, -1.75f);
                List<PaintableStretch> stretches = Stretches(random);
                var zones = new Zones(i % 3 == 1 || i % 3 == 2, i % 3 == 2);
                var probe = new SamplingProbe();
                List<SurfaceStrip> bands = PartSampling.HalfBands(left, right, stretches, zones, "half", probe);
                var expected = new List<SurfaceStrip>();
                foreach (PaintableStretch s in stretches)
                    expected.AddRange(SurfaceSection.BandsAdaptive(left, right, s, PartSampling.TolXz, PartSampling.TolY, "half", zones));
                bool same = bands.Count == expected.Count;
                for (int k = 0; same && k < bands.Count; k++)
                    same = SamePoints(bands[k], expected[k]);
                if (!same)
                    bad++;
                bool one = bands.Count > 0;
                foreach (SurfaceStrip b in bands)
                    one &= b.Left.Count == bands[0].Left.Count;
                if (one)
                    shared++;
                if (probe.Contexts.Count != 1 || probe.Contexts[0].At.Count != bands[0].Left.Count || probe.Contexts[0].Across != (IList<PaintableStretch>)stretches)
                    bad++;
                if (zones.Start || zones.End)
                    zoned++;
            }

            Program.Check(bad == 0, $"a half is what BandsAdaptive gives for each of its stretches, and the probe has its curves, places and stretches ({bad} differ)");
            Program.Check(shared == 200, "all the stretches of a half have the same places");
            Program.Check(zoned > 100, "(zones were in two thirds of them)");

            // A straight open road is one piece; a zone at an end keeps its regular steps.
            Bezier3 l = Bezier3.Line(new Vec3(0, 0, 0), new Vec3(60, 0, 0)), r = Bezier3.Line(new Vec3(0, 0, 3.5f), new Vec3(60, 0, 3.5f));
            var one1 = new List<PaintableStretch> { new PaintableStretch(0f, 1f, 0f) };
            Program.Check(PartSampling.HalfBands(l, r, one1, Zones.None, "s")[0].Left.Count == 2, "a straight half with no junction is a single piece");
            int atJunction = PartSampling.HalfBands(l, r, one1, new Zones(true, false), "s")[0].Left.Count;
            Program.Check(atJunction >= 2 + 5, "next to a junction it keeps the regular fine steps there: " + atJunction + " points");
            Program.Check(PartSampling.HalfBands(l, r, new List<PaintableStretch>(), Zones.None, "s").Count == 0, "no stretch, no band");
        }

        private static void Node()
        {
            var random = new Random(20261104);
            int sliverFree = 0, openedBySeparate = 0, fillers = 0, shortLists = 0, orderBad = 0;
            double worstSeparate = 0.0;
            for (int i = 0; i < 300; i++)
            {
                Vec3[] c = Centre(random, 1.4);
                Bezier3 outerLeft = Side(c, 3.5f), middle = Side(c, 0f), outerRight = Side(c, -3.5f);
                var whole = new List<PaintableStretch> { new PaintableStretch(0f, 1f, 0f) };
                var some = Stretches(random);
                var gore = new List<PaintableStretch> { new PaintableStretch(0f, 1f, 0.05f) };
                var pieces = new List<NodePiece>
                {
                    new NodePiece(outerLeft, middle, whole, false),
                    new NodePiece(middle, outerRight, whole, false),
                    new NodePiece(outerLeft, outerRight, some, true)
                };
                var probe = new SamplingProbe();
                List<SurfaceStrip> bands = PartSampling.NodeBands(pieces, "node", probe);
                int expectedBands = 1 + 1 + some.Count;
                if (bands.Count != expectedBands || probe.Contexts.Count != 3)
                    orderBad++;
                bool one = true;
                foreach (SurfaceStrip b in bands)
                    one &= b.Left.Count == bands[0].Left.Count;
                if (one && bands.Count == expectedBands)
                    sliverFree++;
                if (bands.Count == expectedBands && bands[0].Left.Count < PartSampling.FixedSamples + 1)
                    shortLists++;
                // The middle line is the right side of the first band and the left side of the second: the same points, exactly.
                bool exact = bands.Count >= 2;
                for (int k = 0; exact && k < bands[0].Right.Count; k++)
                    exact = (bands[0].Right[k] - bands[1].Left[k]).Length < 1e-4f;
                if (!exact)
                    orderBad++;
                for (int k = 2; k < bands.Count; k++)
                {
                    if (bands[k].Filler)
                        fillers++;
                }

                if (bands[0].Filler || bands[1].Filler)
                    orderBad++;

                // The control: each piece with a list of its own (what the first wiring would have done) leaves a sliver along the middle line where the two lists differ.
                List<SurfaceStrip> a = PartSampling.HalfBands(outerLeft, middle, whole, Zones.None, "a");
                List<SurfaceStrip> b2 = PartSampling.HalfBands(middle, outerRight, whole, Zones.None, "b");
                if (a[0].Left.Count != b2[0].Left.Count)
                {
                    double gap = Gap(a[0].Right, b2[0].Left);
                    worstSeparate = Math.Max(worstSeparate, gap);
                    if (gap > 0.0005)
                        openedBySeparate++;
                }
            }

            Program.Check(orderBad == 0, "the pieces come in order, the probe has a context for each piece, and the middle line is the same points in both halves: " + orderBad);
            Program.Check(sliverFree == 300, "every band of a part has the same places");
            Program.Check(shortLists == 0, "never fewer than the fixed 16 steps in a part");
            Program.Check(fillers > 0, "the bands of a filler piece are marked as fillers");
            Program.Check(openedBySeparate > 0, $"(control) with a list for each piece the middle line is off by up to {worstSeparate * 1000.0:0.0} mm between the halves in {openedBySeparate} of 300 parts: the single list is needed");
            Program.Check(PartSampling.NodeBands(new List<NodePiece>(), "n").Count == 0, "no piece, no band");
        }

        /// <summary>The largest distance (m, plan) from a point of one polyline to the other polyline (two lines along the same curve with other places).</summary>
        private static double Gap(IList<Vec3> a, IList<Vec3> b)
        {
            double worst = 0.0;
            foreach (Vec3 p in a)
                worst = Math.Max(worst, DistanceToLine(p, b));
            foreach (Vec3 p in b)
                worst = Math.Max(worst, DistanceToLine(p, a));
            return worst;
        }

        private static double DistanceToLine(Vec3 p, IList<Vec3> line)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                double dx = line[i + 1].X - line[i].X, dz = line[i + 1].Z - line[i].Z, l2 = dx * dx + dz * dz;
                double t = l2 > 1e-12 ? Math.Max(0.0, Math.Min(1.0, ((p.X - line[i].X) * dx + (p.Z - line[i].Z) * dz) / l2)) : 0.0;
                double ex = line[i].X + dx * t - p.X, ez = line[i].Z + dz * t - p.Z;
                best = Math.Min(best, Math.Sqrt(ex * ex + ez * ez));
            }

            return best;
        }

        // ------------------------------------------------------------------ the measure

        /// <summary>The same measure by brute force: 4096 places, the polyline of the places `at` against the curve.</summary>
        private static double Dense(Bezier3 curve, IList<float> at, bool height)
        {
            double worst = 0.0;
            int k = 0;
            for (int s = 0; s <= 4096; s++)
            {
                float u = s / 4096f;
                while (k + 2 < at.Count && at[k + 1] < u)
                    k++;
                float span = at[k + 1] - at[k];
                Vec3 on = Vec3.Lerp(curve.Point(at[k]), curve.Point(at[k + 1]), span > 0f ? (u - at[k]) / span : 0f), truth = curve.Point(u);
                worst = Math.Max(worst, height ? Math.Abs(on.Y - truth.Y) : Vec3.DistanceXZ(on, truth));
            }

            return worst;
        }

        private static void Measured()
        {
            SamplingMeasure none = SamplingCheck.Measure(null);
            Program.Check(none.AdaptivePlan == 0f && none.FixedPlan == 0f && none.ReferenceArea == 0.0, "no probe measures nothing");
            Program.Check(SamplingCheck.Measure(new SamplingProbe()).BetweenPlan == 0f, "a probe with no curve measures nothing");

            // A straight road: both samplers are exact.
            Bezier3 l = Bezier3.Line(new Vec3(0, 0, 0), new Vec3(100, 0, 0)), r = Bezier3.Line(new Vec3(0, 0, 3.5f), new Vec3(100, 0, 3.5f));
            var whole = new List<PaintableStretch> { new PaintableStretch(0f, 1f, 0f) };
            var probe = new SamplingProbe();
            PartSampling.HalfBands(l, r, whole, Zones.None, "s", probe);
            SamplingMeasure m = SamplingCheck.Measure(probe);
            Program.Check(m.AdaptivePlan < 1e-4f && m.FixedPlan < 1e-4f && m.BetweenPlan < 1e-4f && m.AdaptiveHeight < 1e-4f, "a straight road is off by nothing");
            Program.Near((float)m.ReferenceArea, 350f, 0.01f, "its dense area is 100 x 3.5 m: " + m.ReferenceArea);
            var sub = new List<PaintableStretch> { new PaintableStretch(0.25f, 0.75f, 0f) };
            var probe2 = new SamplingProbe();
            PartSampling.HalfBands(l, r, sub, Zones.None, "s", probe2);
            Program.Near((float)SamplingCheck.Measure(probe2).ReferenceArea, 175f, 0.01f, "and a stretch from a quarter to three quarters of the width is half of it");

            // The places of the measure are 0 to 1 inclusive: with two the ends only (a polyline through them is on the curve there), with three the middle too.
            Bezier3 bent = new Bezier3(new Vec3(0, 0, 0), new Vec3(40, 0, 0), new Vec3(60, 0, 0), new Vec3(60, 0, 40));
            var bentProbe = new SamplingProbe();
            bentProbe.Add(bent, bent, new[] { 0f, 1f }, whole);
            SamplingMeasure ends = SamplingCheck.Measure(bentProbe, 2);
            Program.Check(ends.AdaptivePlan < 1e-4f && ends.AdaptiveHeight < 1e-4f, "two places measure the two ends and find a chord exact there: " + ends.AdaptivePlan * 1000f + " mm");
            SamplingMeasure middle = SamplingCheck.Measure(bentProbe, 3);
            Program.Check(middle.AdaptivePlan > 1f, "three also measure the middle, where a chord over a bend is metres off: " + middle.AdaptivePlan + " m");

            // A tight arc, R = 30 m over a quarter turn: the fixed 16 steps are some 40 mm off the curve (the bench's figure), the adaptive ones within the tolerance.
            const float R = 30f, K = 0.5523f * R;
            Bezier3 arc = new Bezier3(new Vec3(R, 0, 0), new Vec3(R, 0, K), new Vec3(K, 0, R), new Vec3(0, 0, R));
            Bezier3 inner = new Bezier3(new Vec3(R - 3.5f, 0, 0), new Vec3((R - 3.5f), 0, K * (R - 3.5f) / R), new Vec3(K * (R - 3.5f) / R, 0, R - 3.5f), new Vec3(0, 0, R - 3.5f));
            var probe3 = new SamplingProbe();
            PartSampling.HalfBands(arc, inner, whole, Zones.None, "arc", probe3);
            SamplingMeasure a = SamplingCheck.Measure(probe3);
            Program.Check(a.FixedPlan > 0.03f && a.FixedPlan < 0.06f, "the fixed steps are 3 to 6 cm off a 30 m arc: " + a.FixedPlan * 1000f + " mm");
            Program.Check(a.AdaptivePlan <= PartSampling.TolXz + SamplingStats.Slack, "the adaptive ones are within 10 mm: " + a.AdaptivePlan * 1000f + " mm");
            Program.Check(a.BetweenPlan > 0.02f, "so the two differ by what the fixed one is off: " + a.BetweenPlan * 1000f + " mm");
            Program.Check(probe3.Contexts[0].At.Count > 17, "with more steps than the fixed one: " + probe3.Contexts[0].At.Count);
            Program.Check(Math.Abs(a.FixedPlan - Dense(arc, PartSampling.FixedAt, false)) < 0.003, "the 64 places find the fixed polyline's worst within 3 mm of the 4096-place one");

            // A crest of 500 m: the height of the fixed steps is out by millimetres, the adaptive within 3 mm.
            Bezier3 crestL = new Bezier3(new Vec3(0, 0, 0), new Vec3(33, 4.4f, 0), new Vec3(66, 4.4f, 0), new Vec3(100, 0, 0));
            Bezier3 crestR = new Bezier3(new Vec3(0, 0, 3.5f), new Vec3(33, 4.4f, 3.5f), new Vec3(66, 4.4f, 3.5f), new Vec3(100, 0, 3.5f));
            var probe4 = new SamplingProbe();
            PartSampling.HalfBands(crestL, crestR, whole, Zones.None, "crest", probe4);
            SamplingMeasure h = SamplingCheck.Measure(probe4);
            Program.Check(h.AdaptiveHeight <= PartSampling.TolY + SamplingStats.Slack && h.FixedHeight > h.AdaptiveHeight, $"a crest: fixed {h.FixedHeight * 1000f:0.0} mm, adaptive {h.AdaptiveHeight * 1000f:0.0} mm");

            // Random roads, with and without zones: the adaptive polyline is within the tolerance of the curve at the 64 places, and never above what a dense check finds.
            var random = new Random(20261105);
            int over = 0, above = 0;
            float worstRatio = 0f;
            for (int i = 0; i < 300; i++)
            {
                Vec3[] c = Centre(random);
                Bezier3 left = Side(c, 1.75f), right = Side(c, -1.75f);
                var p = new SamplingProbe();
                PartSampling.HalfBands(left, right, Stretches(random), new Zones(i % 2 == 0, i % 4 == 0), "r", p);
                SamplingMeasure x = SamplingCheck.Measure(p);
                if (x.AdaptivePlan > PartSampling.TolXz + SamplingStats.Slack || x.AdaptiveHeight > PartSampling.TolY + SamplingStats.Slack)
                    over++;
                double densePlan = Math.Max(Dense(left, p.Contexts[0].At, false), Dense(right, p.Contexts[0].At, false));
                double denseHeight = Math.Max(Dense(left, p.Contexts[0].At, true), Dense(right, p.Contexts[0].At, true));
                if (x.AdaptivePlan > densePlan + 1e-5 || x.AdaptiveHeight > denseHeight + 1e-5)
                    above++;
                worstRatio = Math.Max(worstRatio, x.AdaptivePlan / PartSampling.TolXz);
            }

            Program.Check(over == 0, $"300 random roads: the adaptive polyline is within 10 mm and 3 mm of the curve ({over} over; the worst reaches {worstRatio:0.00} of the plan's tolerance)");
            Program.Check(above == 0, "and the 64 places never see more than the dense check does");
            Program.Check(worstRatio > 0.2f, "(the bound is not a loose one)");
        }

        // ------------------------------------------------------------------ corners, pieces, areas

        private static SurfaceStrip Strip(params float[] xz)
        {
            // Left line along z = 0, right along z = 2, at the given x places.
            var s = new SurfaceStrip();
            foreach (float x in xz)
                s.Add(new Vec3(x, 0f, 0f), new Vec3(x, 0f, 2f));
            return s;
        }

        private static void Bands()
        {
            SurfaceStrip a = Strip(0, 5, 10), b = Strip(0, 3, 10);
            CornerResult same = SamplingCheck.Corners(new[] { a }, new[] { b }, 0.001f);
            Program.Check(same.Checked == 4 && same.Bad == 0 && !same.CountDiffers, "two bands with the same ends have the same corners, however they are cut between");
            SurfaceStrip moved = new SurfaceStrip();
            moved.Add(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 2f));
            moved.Add(new Vec3(10.002f, 0f, 0f), new Vec3(10f, 0f, 2f));
            CornerResult two = SamplingCheck.Corners(new[] { a }, new[] { moved }, 0.001f);
            Program.Check(two.Checked == 4 && two.Bad == 1, "one corner 2 mm off is one bad corner at a limit of 1 mm: " + two.Bad);
            SurfaceStrip near = new SurfaceStrip();
            near.Add(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 2f));
            near.Add(new Vec3(10.0005f, 0f, 0f), new Vec3(10f, 0f, 2f));
            Program.Check(SamplingCheck.Corners(new[] { a }, new[] { near }, 0.001f).Bad == 0, "and 0.5 mm is not");

            // Each of the four corners counts, and an inner point does not.
            for (int corner = 0; corner < 4; corner++)
            {
                var one = new SurfaceStrip();
                Vec3 leftFirst = new Vec3(0f, 0f, 0f), rightFirst = new Vec3(0f, 0f, 2f), leftLast = new Vec3(10f, 0f, 0f), rightLast = new Vec3(10f, 0f, 2f);
                Vec3 step = new Vec3(0.003f, 0f, 0f);
                if (corner == 0)
                    leftFirst = leftFirst + step;
                else if (corner == 1)
                    rightFirst = rightFirst + step;
                else if (corner == 2)
                    leftLast = leftLast + step;
                else
                    rightLast = rightLast + step;
                one.Add(leftFirst, rightFirst);
                one.Add(leftLast, rightLast);
                CornerResult each = SamplingCheck.Corners(new[] { a }, new[] { one }, 0.001f);
                Program.Check(each.Checked == 4 && each.Bad == 1, "corner " + corner + " 3 mm off is one bad corner: " + each.Bad);
            }

            SurfaceStrip inner = new SurfaceStrip();
            inner.Add(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 2f));
            inner.Add(new Vec3(5f, 0f, 0.5f), new Vec3(5f, 0f, 2.5f));
            inner.Add(new Vec3(10f, 0f, 0f), new Vec3(10f, 0f, 2f));
            Program.Check(SamplingCheck.Corners(new[] { a }, new[] { inner }, 0.001f).Bad == 0, "a point between the ends is not a corner");
            SurfaceStrip nan = new SurfaceStrip();
            nan.Add(new Vec3(float.NaN, 0f, 0f), new Vec3(0f, 0f, 2f));
            nan.Add(new Vec3(10f, 0f, 0f), new Vec3(10f, 0f, 2f));
            Program.Check(SamplingCheck.Corners(new[] { a }, new[] { nan }, 0.001f).Bad == 1, "a corner that is not a number is bad, not equal");
            CornerResult count = SamplingCheck.Corners(new[] { a, b }, new[] { a }, 0.001f);
            Program.Check(count.CountDiffers && count.Checked == 4 && count.Bad == 0, "another number of bands is told, and the bands both have are compared");
            Program.Check(SamplingCheck.Corners(new List<SurfaceStrip>(), new List<SurfaceStrip>(), 0.001f).Checked == 0, "no bands, no corners");
            Program.Check(SamplingCheck.Corners(new[] { new SurfaceStrip() }, new[] { a }, 0.001f).Checked == 0, "an empty band is skipped");

            Program.Check(SamplingCheck.Pieces(new[] { a, b }) == 4 && SamplingCheck.Pieces(new[] { new SurfaceStrip() }) == 0, "pieces are the steps between places");
            Program.Near((float)SamplingCheck.PlanArea(new[] { a }), 20f, 1e-4f, "a 10 x 2 m band is 20 m2");
            Program.Near((float)SamplingCheck.PlanArea(new[] { a, b }), 40f, 1e-4f, "two are 40: the cut between the ends does not change it");
            SurfaceStrip reversed = new SurfaceStrip();
            foreach (float x in new[] { 10f, 5f, 0f })
                reversed.Add(new Vec3(x, 0f, 0f), new Vec3(x, 0f, 2f));
            Program.Near((float)SamplingCheck.PlanArea(new[] { reversed }), 20f, 1e-4f, "whichever way it runs");
            Program.Check(SamplingCheck.PlanArea(new[] { nan }) == 0.0 || !double.IsNaN(SamplingCheck.PlanArea(new[] { nan })), "a band with a point that is not a number does not make the area one");

            // The strips of the bands as the fixed sampler lays them have the area of a straight road.
            Bezier3 l = Bezier3.Line(new Vec3(0, 0, 0), new Vec3(100, 0, 0)), r = Bezier3.Line(new Vec3(0, 0, 3.5f), new Vec3(100, 0, 3.5f));
            Program.Near((float)SamplingCheck.PlanArea(FixedBands(l, r, new List<PaintableStretch> { new PaintableStretch(0f, 1f, 0f) }, "x")), 350f, 0.01f, "the plan area of bands laid along a road");
        }

        // ------------------------------------------------------------------ the gate

        private static void Gate()
        {
            // 0.12.75: the comparison is made on a worker, so a frame admits as many parts as it costs the main thread to copy (PerFrame), and the queue is bounded.
            var gate = new ShadowGate();
            Program.Check(gate.Admit(1, 0) && gate.Sent == 1 && gate.Skipped == 0, "the first part is admitted and counted as sent");
            int admitted = 1;
            for (int i = 0; i < 100; i++)
            {
                if (gate.Admit(1, 0))
                    admitted++;
            }

            Program.Check(admitted == ShadowGate.PerFrame && gate.Sent == ShadowGate.PerFrame && gate.Skipped == 100 - (ShadowGate.PerFrame - 1), "a frame admits PerFrame parts and counts the rest as skipped: " + admitted + " " + gate.Skipped);
            Program.Check(gate.Admit(2, 0), "the next frame starts again");

            // The queue: no more than MaxOutstanding wait for the worker.
            gate = new ShadowGate();
            Program.Check(gate.Admit(1, ShadowGate.MaxOutstanding - 1) && !gate.Admit(1, ShadowGate.MaxOutstanding) && gate.Skipped == 1 && gate.Sent == 1, "a full queue admits none, and counts it");
            Program.Check(!gate.Admit(1, ShadowGate.MaxOutstanding + 50) && gate.Skipped == 2, "and over-full the same");

            // A part that made nothing to compare does not count.
            gate = new ShadowGate();
            gate.Admit(3, 0);
            gate.Admit(3, 0);
            gate.Withdraw();
            Program.Check(gate.Sent == 1, "a withdrawn part is not sent");
            for (int i = 0; i < ShadowGate.PerFrame; i++)
                gate.Admit(3, 0);
            Program.Check(gate.Sent == ShadowGate.PerFrame && gate.Skipped == 1, "its place in the frame is free again: " + gate.Sent + " " + gate.Skipped);
            var none = new ShadowGate();
            none.Withdraw();
            Program.Check(none.Sent == 0 && none.Admit(0, 0), "a withdraw with nothing sent leaves the counters at 0");

            // At most 2000 in all, over any number of frames.
            gate = new ShadowGate();
            for (int frame = 0; frame < 5000 && !gate.Finished; frame++)
            {
                for (int i = 0; i < 10; i++)
                    gate.Admit(frame, 0);
            }

            Program.Check(ShadowGate.MaxParts == 2000 && gate.Sent == 2000 && gate.Finished && !gate.Admit(99999, 0) && gate.Skipped == 0, "2000 are sent and then no more are taken (and none are counted as skipped)");
            gate.Reset();
            Program.Check(gate.Sent == 0 && gate.Skipped == 0 && !gate.Finished && gate.Admit(0, 0), "a new city starts again");
            var busy = new ShadowGate();
            busy.Admit(5, ShadowGate.MaxOutstanding);
            Program.Check(busy.Skipped == 1 && busy.Sent == 0, "(a gate that has skipped one)");
            busy.Reset();
            Program.Check(busy.Skipped == 0 && busy.Sent == 0, "a reset forgets what was skipped and what was sent");
        }

        // ------------------------------------------------------------------ the statistics

        private static PartComparison Comparison(string part, float plan = 0.004f, float height = 0.001f, double fixedMs = 0.02, double adaptiveMs = 0.01, int badCorners = 0, bool countDiffers = false, double area = 100.0, double adaptiveArea = 100.0, double fixedArea = 100.0, double shadowMs = 0.05)
        {
            return new PartComparison
            {
                Part = part,
                FixedMs = fixedMs,
                AdaptiveMs = adaptiveMs,
                ShadowMs = shadowMs,
                FixedPieces = 48,
                AdaptivePieces = 30,
                Measure = new SamplingMeasure { AdaptivePlan = plan, AdaptiveHeight = height, FixedPlan = 0.02f, FixedHeight = 0.004f, BetweenPlan = 0.02f, BetweenHeight = 0.003f, ReferenceArea = area },
                Corners = new CornerResult { Checked = 16, Bad = badCorners, CountDiffers = countDiffers },
                FixedArea = fixedArea,
                AdaptiveArea = adaptiveArea
            };
        }

        private static void Stats()
        {
            var stats = new SamplingStats();
            var gate = new ShadowGate();
            Program.Check(stats.GateFailures().Count == 1 && stats.GateFailures()[0].StartsWith("nothing"), "nothing compared: the gate says so");
            stats.Add(Comparison("segment 1"));
            Program.Check(stats.GateFailures().Count == 0, "one clean comparison meets the gate: " + string.Join("; ", stats.GateFailures()));

            // Each part of the gate fails alone.
            var s = new SamplingStats();
            s.Add(Comparison("p", plan: PartSampling.TolXz + SamplingStats.Slack * 0.5f));
            Program.Check(s.OverTolerance == 0, "the tolerance and half the float noise is not over");
            s.Add(Comparison("p", plan: PartSampling.TolXz + SamplingStats.Slack * 2f));
            s.Add(Comparison("p", height: PartSampling.TolY + SamplingStats.Slack * 2f));
            Program.Check(s.OverTolerance == 2 && s.GateFailures().Count == 1 && s.GateFailures()[0].Contains("over the tolerance"), "over in the plan or in height: counted, and the only failure");
            s = new SamplingStats();
            s.Add(Comparison("p", badCorners: 2));
            Program.Check(s.CornersBad == 2 && s.CornersChecked == 16 && s.GateFailures().Count == 1 && s.GateFailures()[0].Contains("corner"), "a bad corner fails the gate");
            s = new SamplingStats();
            s.Add(Comparison("p", countDiffers: true));
            Program.Check(s.CountDiffers == 1 && s.GateFailures().Count == 1, "another number of bands fails it");
            s = new SamplingStats();
            s.Add(Comparison("p", adaptiveArea: 100.0 * 1.006));
            Program.Check(s.GateFailures().Count == 1 && s.GateFailures()[0].Contains("area") && Math.Abs(s.AreaAdaptive - 0.006) < 1e-9, "an area 0.6 % off the reference fails it");
            s = new SamplingStats();
            s.Add(Comparison("p", adaptiveArea: 100.0 * 1.004, fixedArea: 100.0 * 1.02));
            Program.Check(s.GateFailures().Count == 0 && Math.Abs(s.AreaFixed - 0.02) < 1e-9 && Math.Abs(s.AreaBetween - (100.4 - 102.0) / 102.0 * -1.0) < 1e-9, "0.4 % is within it, and the fixed one's own error is kept");
            s = new SamplingStats();
            s.Add(Comparison("p", fixedMs: 0.01, adaptiveMs: 0.03));
            Program.Check(s.GateFailures().Count == 1 && s.GateFailures()[0].Contains("took"), "an adaptive sampling that takes longer than the fixed fails it");
            s = new SamplingStats();
            s.Add(Comparison("p", area: 0.0));
            Program.Check(s.AreaAdaptive == 0.0 && s.AreaFixed == 0.0, "a part with no reference area does not divide by it");

            // The worst twenty: 60 parts, the ratios rising and falling; the list is bounded, sorted, and holds the largest.
            s = new SamplingStats();
            var random = new Random(3);
            float largest = 0f;
            for (int i = 0; i < 60; i++)
            {
                float plan = 0.001f + (float)random.NextDouble() * 0.0089f;
                largest = Math.Max(largest, plan);
                s.Add(Comparison("part " + i, plan: plan));
            }

            string report = s.Report(FeatureState.Shadow, gate);
            string[] lines = report.Split('\n');
            Program.Check(SamplingStats.WorstKept == 20 && lines.Length == 2 + 20, "the report is the counters, the gate and the worst twenty: " + lines.Length + " lines");
            double previous = double.MaxValue;
            bool sorted = true;
            for (int i = 2; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(": ", StringComparison.Ordinal);
                int of = lines[i].IndexOf(" of the tolerance", StringComparison.Ordinal);
                double ratio = double.Parse(lines[i].Substring(colon + 2, of - colon - 2), CultureInfo.InvariantCulture);
                sorted &= ratio <= previous + 1e-9;
                previous = ratio;
            }

            Program.Check(sorted, "sorted from the worst");
            Program.Check(lines[2].Contains($"{largest / PartSampling.TolXz:0.00} of the tolerance"), "the first is the largest: " + lines[2].Substring(0, Math.Min(60, lines[2].Length)));
            Program.Check(lines[1].Contains("gate: met so far"), "and the gate line says it is met");

            // Made counters and the line for the load.
            s.NoteMade(100, 1.5);
            s.NoteMade(50, 0.5);
            Program.Check(s.MadeParts == 2 && s.MadePieces == 150 && Math.Abs(s.MadeMs - 2.0) < 1e-9, "the parts made by the sampler in use are counted");
            string line = s.Describe(FeatureState.Shadow, gate);
            Program.Check(line.StartsWith("sampling shadow: 2 part(s) made with 150 piece(s) in 2.0 ms; 60 compared, 0 not taken (the frame or the queue was full)"), "the line: " + line.Substring(0, Math.Min(110, line.Length)));
            Program.Check(!new SamplingStats().Describe(FeatureState.On, gate).Contains("compared"), "with nothing compared and not in shadow the line is only what was made");
            Program.Check(new SamplingStats().Describe(FeatureState.Off, gate).StartsWith("sampling off: 0 part(s)"), "the state is named");

            // Invariant numbers whatever the machine's language.
            CultureInfo before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                string german = s.Describe(FeatureState.Shadow, gate);
                Program.Check(german.Contains("2.0 ms") && !german.Contains("2,0 ms"), "the numbers have a point after a language that writes a comma");
            }
            catch (CultureNotFoundException)
            {
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }

            s.Reset();
            Program.Check(s.Compared == 0 && s.MadeParts == 0 && s.AdaptivePlan == 0f && s.CornersChecked == 0 && s.Report(FeatureState.Shadow, gate).Split('\n').Length == 2, "a new city starts again");
            var worst = new SamplingStats();
            worst.Add(Comparison("a", plan: 0.001f));
            worst.Add(Comparison("b", plan: 0.002f));
            worst.Add(Comparison("c", plan: 0.003f, shadowMs: 0.01));
            Program.Check(worst.WorstShadowMs == 0.05 && Math.Abs(worst.ShadowMs - 0.11) < 1e-9 && worst.FixedPieces == 144 && worst.AdaptivePieces == 90, "the sums, and the dearest part is the dearest and not the last: " + worst.WorstShadowMs);
        }

        private static void Schedule()
        {
            var s = new SamplingStats();
            Program.Check(!s.SummaryDue(0.0), "nothing compared, nothing to tell");
            for (int i = 0; i < 99; i++)
                s.Add(Comparison("p"));
            Program.Check(!s.SummaryDue(10.0), "99 are not 100");
            s.Add(Comparison("p"));
            Program.Check(s.SummaryDue(11.0), "the hundredth is due");
            s.MarkPrinted(11.0);
            Program.Check(!s.SummaryDue(12.0), "and once");
            s.Add(Comparison("p"));
            Program.Check(!s.SummaryDue(100.0), "new ones are not told before three minutes");
            Program.Check(s.SummaryDue(11.0 + SamplingStats.SummarySeconds), "after three minutes they are");
            s.MarkPrinted(191.0);
            Program.Check(!s.SummaryDue(1000.0), "with nothing new there is nothing to tell, however long it is");
            for (int i = 0; i < 1899; i++)
                s.Add(Comparison("p"));
            Program.Check(s.Compared == 2000 - 0 && s.SummaryDue(192.0), "the last one of the 2000 is due at once: " + s.Compared);
            s.MarkPrinted(192.0);
            Program.Check(!s.SummaryDue(192.5), "once");

            // No more than six in all.
            var many = new SamplingStats();
            double now = 0.0;
            int printed = 0;
            for (int i = 0; i < 40; i++)
            {
                many.Add(Comparison("p"));
                for (int k = 0; k < 50; k++)
                    many.Add(Comparison("p"));
                now += 200.0;
                if (many.SummaryDue(now))
                {
                    many.MarkPrinted(now);
                    printed++;
                }
            }

            Program.Check(printed == SamplingStats.MaxSummaries, $"at most {SamplingStats.MaxSummaries} summaries in a session: {printed}");
        }

        // ------------------------------------------------------------------ the shadow against the fixed sampler

        private static void Equivalence()
        {
            var random = new Random(20261106);
            int badCorners = 0, countDiffers = 0, over = 0, partsWithMoreSteps = 0, areaOver = 0, fitOver = 0, fits = 0;
            string worstDetail = "";
            double worstArea = 0.0, worstFit = 0.0, fixedPieces = 0.0, adaptivePieces = 0.0, fixedError = 0.0, adaptiveError = 0.0;
            for (int i = 0; i < 300; i++)
            {
                bool node = i % 3 == 2;
                SamplingProbe probe = new SamplingProbe();
                Vec3 mid = default;
                List<SurfaceStrip> fixedBands = new List<SurfaceStrip>(), adaptiveBands;
                if (!node)
                {
                    Vec3[] c = Centre(random, i % 2 == 0 ? 0.7 : 0.15);
                    mid = new Bezier3(c[0], c[1], c[2], c[3]).Point(0.5f);
                    Bezier3 left = Side(c, 1.75f), right = Side(c, -1.75f);
                    List<PaintableStretch> stretches = Stretches(random);
                    fixedBands = FixedBands(left, right, stretches, "h");
                    adaptiveBands = PartSampling.HalfBands(left, right, stretches, new Zones(i % 4 == 0, i % 5 == 0), "h", probe);
                }
                else
                {
                    Vec3[] c = Centre(random, 1.1);
                    Bezier3 outerLeft = Side(c, 3.5f), middle = Side(c, 0f), outerRight = Side(c, -3.5f);
                    List<PaintableStretch> leftHalf = Stretches(random), rightHalf = Stretches(random);
                    var pieces = new List<NodePiece> { new NodePiece(outerLeft, middle, leftHalf, false), new NodePiece(middle, outerRight, rightHalf, false) };
                    foreach (NodePiece p in pieces)
                        fixedBands.AddRange(FixedBands(p.Left, p.Right, p.Across, "n"));
                    adaptiveBands = PartSampling.NodeBands(pieces, "n", probe);
                }

                CornerResult corners = SamplingCheck.Corners(fixedBands, adaptiveBands, SamplingStats.CornerLimit);
                badCorners += corners.Bad;
                if (corners.CountDiffers)
                    countDiffers++;
                SamplingMeasure m = SamplingCheck.Measure(probe);
                if (m.AdaptivePlan > PartSampling.TolXz + SamplingStats.Slack || m.AdaptiveHeight > PartSampling.TolY + SamplingStats.Slack)
                    over++;
                double reference = m.ReferenceArea, adaptiveArea = SamplingCheck.PlanArea(adaptiveBands);
                if (reference > 1e-6)
                {
                    double off = Math.Abs(adaptiveArea - reference) / reference;
                    worstArea = Math.Max(worstArea, off);
                    if (off > SamplingStats.AreaLimit)
                        areaOver++;
                }

                double fixedArea = SamplingCheck.PlanArea(fixedBands);
                if (reference > 1e-6)
                {
                    fixedError += Math.Abs(fixedArea - reference);
                    adaptiveError += Math.Abs(adaptiveArea - reference);
                }

                int fp = SamplingCheck.Pieces(fixedBands), ap = SamplingCheck.Pieces(adaptiveBands);
                fixedPieces += fp;
                adaptivePieces += ap;
                if (ap > fp)
                    partsWithMoreSteps++;

                // The area fit of a marking box laid over the part's first stretch: the covered area (the pieces of road under it) of the two sets of bands.
                if (!node && i % 6 == 1)
                {
                    AreaFit a = AreaFit.FromStrips(fixedBands, mid, 0f, -6f, 6f, -6f, 6f);
                    AreaFit b = AreaFit.FromStrips(adaptiveBands, mid, 0f, -6f, 6f, -6f, 6f);
                    double areaA = Covered(a, 6f), areaB = Covered(b, 6f);
                    if (areaA > 5.0)
                    {
                        fits++;
                        double diff = Math.Abs(areaA - areaB) / areaA, cells = Math.Abs(areaA - areaB) / 0.04;
                        if (diff > worstFit)
                            worstDetail = $"road {i}: fixed {areaA:0.00} adaptive {areaB:0.00}, plan areas {SamplingCheck.PlanArea(fixedBands):0.00} {SamplingCheck.PlanArea(adaptiveBands):0.00}, bands {fixedBands.Count} {adaptiveBands.Count}, pieces {a.PieceCount} {b.PieceCount}";
                        worstFit = Math.Max(worstFit, diff);
                        if (diff > 0.01 && cells > 4.0)
                            fitOver++;
                    }
                }
            }

            Program.Check(badCorners == 0 && countDiffers == 0, $"300 random parts: the corners are the same points in both samplings and both lay the same bands in the same order ({badCorners} corner(s) apart, {countDiffers} with another band count)");
            Program.Check(over == 0, $"the adaptive polyline is within the tolerance of the curve in every part ({over} over)");
            Program.Check(areaOver == 0, $"and its plan area is within {SamplingStats.AreaLimit * 100.0:0.0} % of a dense reference (the worst is {worstArea * 100.0:0.000} %)");
            Program.Check(fits >= 20 && fitOver == 0, $"an area fit over a gentle road covers the same area of a 12 m square to within 1 % (or four cells of the 0.2 m grid) whichever bands it is made from ({fits} fits; the worst differs by {worstFit * 100.0:0.00} %: {worstDetail})");
            Program.Check(adaptiveError < fixedError * 0.5, $"over all the parts the adaptive bands are nearer the dense area than the fixed ones are ({adaptiveError:0.000} m2 of error against {fixedError:0.000} m2: the tight bends are where the fixed ones are wrong)");
            Program.Check(partsWithMoreSteps > 0 && partsWithMoreSteps < 300, $"the adaptive sampler has more steps in some parts and fewer in others ({partsWithMoreSteps} of 300 have more; pieces {fixedPieces:0} fixed, {adaptivePieces:0} adaptive)");
        }

        /// <summary>The area (m2) of the square of the given half side round the origin that the fit covers, counted on a grid of 0.2 m (the marking's own question: is this place on the road).</summary>
        private static double Covered(AreaFit fit, float half)
        {
            const float Step = 0.2f;
            int n = (int)Math.Round(2f * half / Step), covered = 0;
            var seen = new HashSet<int>();
            var found = new List<int>();
            for (int ix = 0; ix < n; ix++)
            {
                for (int iz = 0; iz < n; iz++)
                {
                    if (fit.HeightAt(-half + (ix + 0.5f) * Step, -half + (iz + 0.5f) * Step, out _, out _, seen, found))
                        covered++;
                }
            }

            return covered * (double)Step * Step;
        }
    }
}
