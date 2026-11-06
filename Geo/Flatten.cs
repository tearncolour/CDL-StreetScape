using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Which ends of a half of a segment are at a junction, and how finely the stretch next to a junction is sampled (0.12.73): the road's strips meet the junction part's there and the other roads'
    /// parts overlap them, so that stretch keeps a regular fine grid and only the open road between the ends is adaptive. The caller knows which ends (StartNodeGeometry or EndNodeGeometry has a
    /// length, as SurfaceReader.NodePart tests); a plain node of two roads, or a dead end, is not given the zone.
    /// </summary>
    public readonly struct Zones
    {
        /// <summary>The length (m, of the longer side) next to a junction that is sampled at a regular step, and that step (m).</summary>
        public const float DefaultLength = 12f, DefaultStep = 2f;

        public readonly bool Start, End;
        public readonly float Length, Step;

        public Zones(bool start, bool end, float length = DefaultLength, float step = DefaultStep)
        {
            Start = start;
            End = end;
            Length = length;
            Step = step;
        }

        /// <summary>No zone at either end: the whole curve is adaptive.</summary>
        public static Zones None => default;
    }

    /// <summary>
    /// How many straight pieces a road's curve needs (0.12.73, pure): the road's sides are cubic Beziers, and a band of surface was always made from 16 equal steps of the parameter whatever the
    /// curve. Wang's bound (Goldman, Pyramid Algorithms 5.6.3; Skia flattens paths with it) says the chord of a piece of a cubic over a parameter step h is within 3/4 of M h^2 of the curve, M the largest
    /// second difference of its control points; so n = ceil(sqrt(3 M / (4 tol))) equal steps keep the polyline within tol of the curve at every parameter. It is taken apart for the ground plane
    /// (lateral, where the paint's edge is) and for the height (where it floats or sinks), each with its own tolerance, and the larger count is used. The bound holds for the whole segment, so a straight
    /// curve is one step and a sharp one gets as many as it needs, up to MaxSegments.
    /// </summary>
    public static class Flatten
    {
        /// <summary>The most steps the open part of a curve gets: a cubic of a road is at most a quarter turn and 100 m, where 64 steps keep it within a millimetre.</summary>
        public const int MaxSegments = 64;

        /// <summary>The steps per unit of the parameter that Wang's bound asks for (not rounded); 0 for a curve with a non-finite control point or a tolerance that is not above zero.</summary>
        private static double Rate(in Bezier3 curve, float tolXz, float tolY)
        {
            Vec3 d0 = curve.A - curve.B * 2f + curve.C, d1 = curve.B - curve.C * 2f + curve.D;
            return Math.Max(Rate(Math.Max(d0.LengthXZ, d1.LengthXZ), tolXz), Rate(Math.Max(Math.Abs(d0.Y), Math.Abs(d1.Y)), tolY));
        }

        private static double Rate(float second, float tolerance)
        {
            if (float.IsNaN(second) || float.IsInfinity(second) || second <= 0f || !(tolerance > 0f))
                return 0.0;
            return Math.Sqrt(0.75 * second / tolerance);
        }

        /// <summary>Wang's bound for the steps of one curve, in [1, max]; 1 for a curve with a non-finite control point (the band's finite check then drops it as before).</summary>
        public static int Segments(in Bezier3 curve, float tolXz, float tolY, int max = MaxSegments) =>
            (int)Math.Min(Math.Max(1, max), Math.Max(1.0, Math.Ceiling(Rate(curve, tolXz, tolY))));

        /// <summary>The length of a curve (m), from 16 chords: a little under the true one.</summary>
        private static float Length(in Bezier3 curve)
        {
            float total = 0f;
            Vec3 last = curve.A;
            for (int i = 1; i <= 16; i++)
            {
                Vec3 p = curve.Point(i / 16f);
                total += (p - last).Length;
                last = p;
            }

            return total;
        }

        /// <summary>
        /// The parameters (0 to 1, strictly rising) for a band between the curves left and right: the open part has equal steps, as many as the one of the curves that needs more asks for (a place
        /// between them has second differences that are a mix of theirs, so the count holds for every stretch of the band, and all the stretches of a part share one list: their edges meet exactly); next
        /// to a junction (zones) the first or last zones.Length metres have steps of zones.Step instead, and the open part is the rest, whose count is Wang's rate over its share of the parameter. The
        /// zone is taken as uniform in the parameter (a road's cubic has nearly even speed), and a curve that is not longer than the zones is all zone.
        /// </summary>
        public static float[] Params(in Bezier3 left, in Bezier3 right, float tolXz, float tolY, Zones zones = default, int max = MaxSegments)
        {
            float length = Math.Max(Length(left), Length(right));
            if (float.IsNaN(length) || float.IsInfinity(length))
                return new[] { 0f, 1f };
            bool zoned = zones.Length > 0f && zones.Step > 0f && length > 1e-4f;
            double start = zoned && zones.Start ? Math.Min(1.0, zones.Length / length) : 0.0, end = zoned && zones.End ? Math.Min(1.0, zones.Length / length) : 0.0;
            double rate = Math.Max(Rate(left, tolXz, tolY), Rate(right, tolXz, tolY));
            var t = new List<float> { 0f };
            if (start + end >= 1.0)
            {
                Steps(t, 0.0, 1.0, ZoneSteps(length, zones.Step, rate));
            }
            else
            {
                double open = 1.0 - start - end;
                if (start > 0.0)
                    Steps(t, 0.0, start, ZoneSteps(start * length, zones.Step, rate * start));
                Steps(t, start, start + open, (int)Math.Min(Math.Max(1, max), Math.Max(1.0, Math.Ceiling(rate * open))));
                if (end > 0.0)
                    Steps(t, 1.0 - end, 1.0, ZoneSteps(end * length, zones.Step, rate * end));
            }

            return t.ToArray();
        }

        /// <summary>
        /// One list of parameters for every curve of a junction part (0.12.73): both halves, the middle line between them and a gore are bands that meet along shared curves, so they must all be cut at the
        /// same places or a sliver is left between them; equal steps, as many as the curve that needs most asks for and never fewer than floor (the 16 that every band had).
        /// </summary>
        public static float[] PartParams(IList<Bezier3> curves, float tolXz, float tolY, int floor = 16, int max = MaxSegments)
        {
            int n = Math.Max(1, floor);
            for (int i = 0; i < curves.Count; i++)
                n = Math.Max(n, Segments(curves[i], tolXz, tolY, max));
            var t = new float[n + 1];
            for (int i = 0; i <= n; i++)
                t[i] = i / (float)n;
            return t;
        }

        private const int MaxZoneSteps = 128;

        /// <summary>
        /// The steps of a zone of the given length (m) at the given step (m): at least one, at most MaxZoneSteps. A zone is never coarser than the tolerance asks of the curve there (needed steps: Wang's
        /// count over the zone's share of the parameter): a tight bend next to a junction is cut finer than the step (2 m chords are 17 to 33 mm off a bend of 30 to 15 m radius).
        /// </summary>
        private static int ZoneSteps(double length, float step, double needed) =>
            (int)Math.Min(MaxZoneSteps, Math.Max(1.0, Math.Max(Math.Ceiling(length / step), Math.Ceiling(needed))));

        /// <summary>The places from from (not added: it is the last one there) to to in count equal steps, to added.</summary>
        private static void Steps(List<float> t, double from, double to, int count)
        {
            for (int i = 1; i <= count; i++)
                t.Add((float)(from + (to - from) * i / count));
        }
    }
}
