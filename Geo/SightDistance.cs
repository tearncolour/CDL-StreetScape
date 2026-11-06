using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The sight distance along a chain of road (0.12.2), from its own geometry: the game keeps no sight distance, and nothing of what stands beside a road (a building, a tree) can be asked. So a driver is
    /// taken to see along a straight line from the eye to an oncoming car while that line stays inside the road's whole width (its footways too: what stands beyond them is taken to hide the view) and above its
    /// surface (a crest hides what is beyond it). The eye and the object are 1.2 m above the road (this mod's figures, the usual ones for the overtaking sight distance). Table A.1 of GB 5768.3-2025 (informative)
    /// gives the overtaking sight distance M2 by speed (interpolated between its rows, extrapolated beyond them); none above 80 km/h (such roads are divided). Pure.
    /// </summary>
    public static class SightDistance
    {
        public const float EyeHeight = 1.2f, ObjectHeight = 1.2f;

        /// <summary>How far apart the places are that the sight is worked out from and to (m).</summary>
        public const float Step = 5f;

        /// <summary>Table A.1: the speeds, the overtaking sight distances M2 (m) and the stopping sight distances M1 (m).</summary>
        public static readonly float[] Speeds = { 30f, 40f, 50f, 60f, 80f, 100f, 120f };

        public static readonly float[] Stopping = { 30f, 40f, 55f, 75f, 110f, 160f, 210f };

        public static readonly float[] Passing = { 150f, 200f, 325f, 350f, 550f };

        /// <summary>The overtaking sight distance for a speed (m); NaN above 80 km/h (Table A.1 has none: the roads are divided) or for no speed.</summary>
        public static float Overtaking(float kmh)
        {
            if (float.IsNaN(kmh) || kmh <= 0f || kmh > 80f + 1e-3f)
                return float.NaN;
            return Interpolate(kmh, Speeds, Passing, Passing.Length);
        }

        /// <summary>The stopping sight distance for a speed (m).</summary>
        public static float StoppingSight(float kmh) => float.IsNaN(kmh) || kmh <= 0f ? float.NaN : Interpolate(kmh, Speeds, Stopping, Stopping.Length);

        private static float Interpolate(float x, float[] xs, float[] ys, int n)
        {
            int i = 0;
            while (i < n - 2 && x > xs[i + 1])
                i++;
            float t = (x - xs[i]) / (xs[i + 1] - xs[i]);
            return Math.Max(0f, ys[i] + (ys[i + 1] - ys[i]) * t);
        }

        /// <summary>
        /// The sight distance (m) ahead from every place of the chain Step apart (place k at the distance k Step; the last at the chain's end), for the traffic with the chain (ahead is the larger distance)
        /// or against it: from an eye at driverLateral to an oncoming car at targetLateral (both m right of the chain), while the line of sight crosses every place in between within clearance(s) of the
        /// centre line (plan view) and rises above the road's centre there (profile: its slope from the eye is at least that to every centre point passed). Worked out up to max metres: a place that sees max
        /// or more has max. (0.12.3: a cone of directions and the steepest slope carried along, a constant cost a place in the way; 0.12.2 tested every line again at every place, 30 ms for 3 km.)
        /// </summary>
        public static float[] Ahead(RoadChain chain, bool withChain, float driverLateral, float targetLateral, Func<float, float> clearance, float max)
        {
            if (chain == null || chain.Length < Step || clearance == null)
                return new float[0];
            int n = (int)Math.Floor(chain.Length / Step) + 1;
            var centre = new Vec3[n];
            var right = new Vec3[n];
            var room = new float[n];
            for (int k = 0; k < n; k++)
            {
                float s = Math.Min(chain.Length, k * Step);
                centre[k] = chain.PointAt(s, 0f);
                right[k] = chain.RightAt(s);
                room[k] = clearance(s);
            }

            int reach = (int)Math.Ceiling(max / Step);
            var result = new float[n];
            int dir = withChain ? 1 : -1;
            for (int i = 0; i < n; i++)
            {
                Vec3 eye = centre[i] + right[i] * driverLateral + new Vec3(0f, EyeHeight, 0f);
                // Directions from the eye as the tangent of their angle from an axis (fx, fz): the cone of those that still pass every place so far inside its room, and the steepest rise to the road so far (a
                // line of sight must pass above every crest). The axis is the way ahead until the first place is passed, then the middle of the directions through it: the cone stays inside those, less than
                // a half turn wide, so nothing in it is a right angle or more from the axis (on a tight bend the way ahead would be).
                double fx = -dir * right[i].Z, fz = dir * right[i].X;
                double lo = double.NegativeInfinity, hi = double.PositiveInfinity, steepest = double.NegativeInfinity;
                int seen = 0;
                for (int d = 1; d <= reach; d++)
                {
                    int j = i + dir * d;
                    if (j < 0 || j >= n)
                    {
                        // The chain ends: the view is not known to be blocked beyond, it is taken as clear (the next stretch is another road).
                        seen = reach;
                        break;
                    }

                    Vec3 target = centre[j] + right[j] * targetLateral + new Vec3(0f, ObjectHeight, 0f);
                    double tx = target.X - eye.X, tz = target.Z - eye.Z;
                    double along = tx * fx + tz * fz;
                    if (!(along > 0.0))
                        break;
                    double tan = (tx * fz - tz * fx) / along;
                    double rise = (target.Y - eye.Y) / Math.Sqrt(tx * tx + tz * tz);
                    if (tan < lo - kHair || tan > hi + kHair || rise < steepest - kHair)
                        break;
                    seen = d;

                    // The place j now stands between the eye and everything beyond it: its room from one side to the other, and its height.
                    Vec3 a = centre[j] - right[j] * room[j], b = centre[j] + right[j] * room[j];
                    if (d == 1)
                        Axis(a.X - eye.X, a.Z - eye.Z, b.X - eye.X, b.Z - eye.Z, ref fx, ref fz);
                    Arc(a.X - eye.X, a.Z - eye.Z, b.X - eye.X, b.Z - eye.Z, fx, fz, out double arcLo, out double arcHi);
                    lo = Math.Max(lo, arcLo);
                    hi = Math.Min(hi, arcHi);
                    if (lo > hi + kHair)
                    {
                        // No line passes the place: nothing beyond it is seen (unless the chain ends right there).
                        if (j + dir < 0 || j + dir >= n)
                            seen = reach;
                        break;
                    }

                    double cx = centre[j].X - eye.X, cz = centre[j].Z - eye.Z;
                    double flat = Math.Sqrt(cx * cx + cz * cz);
                    if (flat > 1e-6)
                        steepest = Math.Max(steepest, (centre[j].Y - eye.Y) / flat);
                }

                result[i] = Math.Min(max, seen * Step);
            }

            return result;
        }

        /// <summary>The middle of the directions from the eye to two points (plan view, relative to the eye; that of the turn less than a half between them), unless they are opposite (the eye is between them: the axis stays).</summary>
        private static void Axis(double ax, double az, double bx, double bz, ref double fx, ref double fz)
        {
            double la = Math.Sqrt(ax * ax + az * az), lb = Math.Sqrt(bx * bx + bz * bz);
            if (la < 1e-9 || lb < 1e-9)
                return;
            double mx = ax / la + bx / lb, mz = az / la + bz / lb, lm = Math.Sqrt(mx * mx + mz * mz);
            if (lm < 1e-6)
                return;
            fx = mx / lm;
            fz = mz / lm;
        }

        /// <summary>A hair of tangent or slope: a line that just grazes an edge passes.</summary>
        private const double kHair = 1e-9;

        /// <summary>
        /// The directions from the eye that pass between two points (plan view, relative to the eye), as tangents of their angle from the way ahead (fx, fz); only those ahead count (the cone never turns
        /// a right angle from it). Both ahead: between their tangents. Both behind: none. One of each: from the one ahead to the side the short way to the other one leaves by.
        /// </summary>
        private static void Arc(double ax, double az, double bx, double bz, double fx, double fz, out double lo, out double hi)
        {
            double alongA = ax * fx + az * fz, sideA = ax * fz - az * fx;
            double alongB = bx * fx + bz * fz, sideB = bx * fz - bz * fx;
            bool frontA = alongA > 0.0, frontB = alongB > 0.0;
            if (frontA && frontB)
            {
                double ta = sideA / alongA, tb = sideB / alongB;
                lo = Math.Min(ta, tb);
                hi = Math.Max(ta, tb);
            }
            else if (!frontA && !frontB)
            {
                lo = double.PositiveInfinity;
                hi = double.NegativeInfinity;
            }
            else
            {
                double alongF = frontA ? alongA : alongB, sideF = frontA ? sideA : sideB, alongK = frontA ? alongB : alongA, sideK = frontA ? sideB : sideA;
                double tf = sideF / alongF;
                if (alongF * sideK - sideF * alongK > 0.0)
                {
                    lo = tf;
                    hi = double.PositiveInfinity;
                }
                else
                {
                    lo = double.NegativeInfinity;
                    hi = tf;
                }
            }
        }
    }
}
