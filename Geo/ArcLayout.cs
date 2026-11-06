using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>A circle fitted to points seen from above (ArcFit.Fit): the radius is signed, positive when the surface bulges toward the viewer (a round tower seen from outside), negative when it curves round him (a concave front).</summary>
    public readonly struct ArcFitResult
    {
        public readonly bool Ok;
        public readonly float Radius;
        public readonly float Residual;
        public readonly string Why;

        /// <summary>How many of the points the circle was fitted to (those that were not dropped as far off).</summary>
        public readonly int Kept;

        public ArcFitResult(bool ok, float radius, float residual, string why, int kept = 0)
        {
            Ok = ok;
            Radius = radius;
            Residual = residual;
            Why = why ?? "";
            Kept = kept;
        }

        public static ArcFitResult Flat(string why, float residual = 0f) => new ArcFitResult(false, 0f, residual, why);
    }

    /// <summary>
    /// A circle through the points of a facade (0.12.76: a text board on a curved building front): a least-squares circle (Kasa's fit) over the points the tool sampled along the wall, seen from above,
    /// tried again without the points that are far off it, and refused (the board stays flat, with the reason) when the points are few, near one line, spread over too little, fit badly, or the
    /// circle is outside the radii the builder bends to. Pure.
    /// </summary>
    public static class ArcFit
    {
        /// <summary>The tightest and the widest circle a board is bent to (m): a wider one is flat to the eye (a 12 m board on R = 200 m is 9 mm off a plane in its middle).</summary>
        public const float MinRadius = 2f, MaxRadius = 200f;

        /// <summary>The least the sampled points spread along the wall (m), the points needed, and the rms distance of the points from the circle that is accepted (m): a wall of a building is not a perfect cylinder.</summary>
        public const float MinSpread = 0.8f, MaxResidual = 0.06f;

        public const int MinPoints = 3;

        /// <summary>
        /// The circle through points (x, z in metres, seen from above), for a viewer on the side of outward (a unit vector in the same plane: the wall's normal where the middle point was hit). Radius &gt; 0
        /// when the circle's centre is behind the wall (on the side opposite outward), &lt; 0 when it is on the viewer's side. boardWidth: the board's width, for the sagitta test (below 1 cm over it: flat).
        /// </summary>
        public static ArcFitResult Fit(IReadOnlyList<(float X, float Z)> points, float outwardX, float outwardZ, float boardWidth)
        {
            if (points == null || points.Count < MinPoints)
                return ArcFitResult.Flat("fewer than " + MinPoints + " points");
            double len = Math.Sqrt((double)outwardX * outwardX + (double)outwardZ * outwardZ);
            if (len < 1e-6)
                return ArcFitResult.Flat("no outward direction");
            double ox = outwardX / len, oz = outwardZ / len;

            var used = new List<(double X, double Z)>(points.Count);
            foreach ((float X, float Z) p in points)
            {
                if (!float.IsNaN(p.X) && !float.IsNaN(p.Z) && !float.IsInfinity(p.X) && !float.IsInfinity(p.Z))
                    used.Add((p.X, p.Z));
            }

            if (used.Count < MinPoints)
                return ArcFitResult.Flat("fewer than " + MinPoints + " usable points");

            // The spread along the wall: the largest distance between two points.
            double spread = 0.0;
            for (int i = 0; i < used.Count; i++)
            {
                for (int j = i + 1; j < used.Count; j++)
                {
                    double dx = used[i].X - used[j].X, dz = used[i].Z - used[j].Z;
                    spread = Math.Max(spread, Math.Sqrt(dx * dx + dz * dz));
                }
            }

            if (spread < MinSpread)
                return ArcFitResult.Flat("the points spread over only " + spread.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " m");

            // Fit; while the fit is poor drop the point that is farthest off and fit again, but keep at least 60 % of the points (a few rays that hit a balcony, not half the wall).
            int removable = used.Count - Math.Max(MinPoints, (int)Math.Ceiling(used.Count * 0.6));
            while (true)
            {
                if (!Kasa(used, out double cx, out double cz, out double r))
                    return ArcFitResult.Flat("the points lie on a line");
                double sum = 0.0, worst = -1.0;
                int worstAt = -1;
                for (int i = 0; i < used.Count; i++)
                {
                    double d = Math.Abs(Math.Sqrt((used[i].X - cx) * (used[i].X - cx) + (used[i].Z - cz) * (used[i].Z - cz)) - r);
                    sum += d * d;
                    if (d > worst)
                    {
                        worst = d;
                        worstAt = i;
                    }
                }

                double rms = Math.Sqrt(sum / used.Count);
                if (rms > MaxResidual * 0.5 && removable > 0 && worst > 2.0 * rms)
                {
                    used.RemoveAt(worstAt);
                    removable--;
                    continue;
                }

                if (rms > MaxResidual)
                    return ArcFitResult.Flat("the points are " + rms.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " m off any circle", (float)rms);
                if (r < MinRadius)
                    return ArcFitResult.Flat("a radius of " + r.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m is tighter than " + MinRadius.ToString("0") + " m", (float)rms);
                if (r > MaxRadius)
                    return ArcFitResult.Flat("a radius of " + r.ToString("0") + " m is flat enough", (float)rms);
                double sagitta = boardWidth > 0f ? Sagitta((float)r, boardWidth) : 0.0;
                if (boardWidth > 0f && sagitta < 0.01)
                    return ArcFitResult.Flat("the wall is " + (sagitta * 1000.0).ToString("0") + " mm off a plane over the board", (float)rms);

                // The middle of the points is where the board stands: the centre is behind the wall when it is on the side opposite outward from there.
                double mx = 0.0, mz = 0.0;
                foreach ((double X, double Z) p in used)
                {
                    mx += p.X;
                    mz += p.Z;
                }

                mx /= used.Count;
                mz /= used.Count;
                bool convex = (cx - mx) * ox + (cz - mz) * oz < 0.0;
                return new ArcFitResult(true, (float)(convex ? r : -r), (float)rms, "", used.Count);
            }
        }

        /// <summary>How far the middle of a chord of this width is from the arc of a circle of this radius (m).</summary>
        public static double Sagitta(float radius, float chord)
        {
            double r = Math.Abs(radius), half = chord * 0.5;
            if (r < 1e-6 || half >= r)
                return r;
            return r - Math.Sqrt(r * r - half * half);
        }

        /// <summary>The algebraic circle through the points: minimises the sum of (x2 + z2 + A x + B z + C)2, a linear problem.</summary>
        private static bool Kasa(List<(double X, double Z)> points, out double cx, out double cz, out double r)
        {
            cx = cz = r = 0.0;
            int n = points.Count;
            double mx = 0.0, mz = 0.0;
            foreach ((double X, double Z) p in points)
            {
                mx += p.X;
                mz += p.Z;
            }

            mx /= n;
            mz /= n;
            // Centred coordinates keep the sums well conditioned for points far from the origin.
            double suu = 0, suv = 0, svv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
            foreach ((double X, double Z) p in points)
            {
                double u = p.X - mx, v = p.Z - mz;
                suu += u * u;
                suv += u * v;
                svv += v * v;
                suuu += u * u * u;
                svvv += v * v * v;
                suvv += u * v * v;
                svuu += v * u * u;
            }

            double det = suu * svv - suv * suv;
            double scale = (suu + svv) * (suu + svv);
            if (Math.Abs(det) < 1e-9 * Math.Max(scale, 1e-12))
                return false;
            double b1 = 0.5 * (suuu + suvv), b2 = 0.5 * (svvv + svuu);
            double uc = (b1 * svv - b2 * suv) / det, vc = (b2 * suu - b1 * suv) / det;
            cx = mx + uc;
            cz = mz + vc;
            r = Math.Sqrt(uc * uc + vc * vc + (suu + svv) / n);
            return !(double.IsNaN(r) || double.IsInfinity(r));
        }
    }

    /// <summary>A point put on the surface: its place in the board's frame (x to the viewer's right, z toward the viewer, from the contact point on the wall) and the angle a flat piece there is turned by to face along the normal.</summary>
    public readonly struct ArcPoint
    {
        public readonly float X, Z, Theta;

        public ArcPoint(float x, float z, float theta)
        {
            X = x;
            Z = z;
            Theta = theta;
        }
    }

    /// <summary>
    /// The surface a text board stands on (0.12.76): flat, or a cylinder of a signed radius (positive: bulging toward the viewer; negative: curving round him) whose contact point with the board's middle is
    /// the origin. A piece of the board at x along the flat layout, y up and d in front of the wall (along the normal) is put on the cylinder with the same angle for every d, so that a stud is the
    /// same length all along the board and the letters keep their spacing at the face: the angle is x / (R + faceOffset). The artistic arc text of the fonts is a baseline on the same maths.
    /// </summary>
    public readonly struct ArcSurface
    {
        public readonly float Radius, FaceOffset;

        /// <summary>radius 0 (or beyond the bounds ArcFit keeps to) is flat. faceOffset: how far in front of the wall the letters' faces are (m), the layer whose arc length is the text's.</summary>
        public ArcSurface(float radius, float faceOffset)
        {
            float a = Math.Abs(radius);
            bool bent = a >= ArcFit.MinRadius && a <= ArcFit.MaxRadius && !float.IsNaN(radius) && a > faceOffset * 2f;
            Radius = bent ? radius : 0f;
            FaceOffset = faceOffset;
        }

        public bool Flat => Radius == 0f;

        /// <summary>The angle at x along the flat layout (radians): the arc of the face layer. 0 when flat.</summary>
        public float Theta(float x)
        {
            if (Flat)
                return 0f;
            double ring = (double)Radius + FaceOffset;
            // The face layer of a concave wall has a signed radius too: R + d with R negative.
            return (float)(x / ring);
        }

        /// <summary>The point at x along the layout, d in front of the wall, in the board's frame. Flat: (x, d).</summary>
        public ArcPoint At(float x, float d)
        {
            if (Flat)
                return new ArcPoint(x, d, 0f);
            double theta = Theta(x), ring = (double)Radius + d;
            return new ArcPoint((float)(ring * Math.Sin(theta)), (float)(-Radius + ring * Math.Cos(theta)), (float)theta);
        }

        /// <summary>The wall's own z at x (d = 0).</summary>
        public float WallZ(float x) => At(x, 0f).Z;

        /// <summary>
        /// The x along the flat layout of a point of the board's frame (the inverse of At at a layer d): used to read where on the arc a facade point is (the tool's arrow keys move along the wall). Flat: x.
        /// </summary>
        public float LayoutX(float x, float z, float d)
        {
            if (Flat)
                return x;
            // A ring of a concave wall at d has a negative signed radius: the angle is read on the right side of the centre.
            double sign = (double)Radius + d >= 0.0 ? 1.0 : -1.0;
            double theta = Math.Atan2(sign * x, sign * (z + Radius));
            // The face layer's ring is the one the layout's length is measured on.
            return (float)(theta * ((double)Radius + FaceOffset));
        }
    }

    /// <summary>A glyph set on the surface: where its middle is, and the angle it is turned about the vertical to face along the wall's normal there.</summary>
    public readonly struct ArcGlyph
    {
        public readonly float X, Z, Theta;

        public ArcGlyph(float x, float z, float theta)
        {
            X = x;
            Z = z;
            Theta = theta;
        }
    }

    public static class ArcLayout
    {
        /// <summary>
        /// A glyph whose origin is at xOrigin along the flat layout and which is advance wide, set on a surface at d in front of the wall: its middle is put on the surface and the glyph is turned to the
        /// tangent there, flat (a character is rigid). The caller builds the glyph around its own middle and moves it by this.
        /// </summary>
        public static ArcGlyph Place(in ArcSurface surface, float xOrigin, float advance, float d)
        {
            float middle = xOrigin + advance * 0.5f;
            ArcPoint p = surface.At(middle, d);
            return new ArcGlyph(p.X, p.Z, p.Theta);
        }

        /// <summary>
        /// The straight pieces a rail from x0 to x1 (flat layout) is cut into so that none is more than maxSag off the arc at layer d: each piece's two ends in the board's frame. A flat surface: one piece.
        /// </summary>
        public static List<(ArcPoint A, ArcPoint B)> Chords(in ArcSurface surface, float x0, float x1, float d, float maxSag = 0.004f)
        {
            var pieces = new List<(ArcPoint, ArcPoint)>();
            if (x1 < x0)
            {
                float t = x0;
                x0 = x1;
                x1 = t;
            }

            if (surface.Flat || x1 - x0 < 1e-5f)
            {
                pieces.Add((surface.At(x0, d), surface.At(x1, d)));
                return pieces;
            }

            double ring = Math.Abs((double)surface.Radius + d);
            // A chord of angle 2a is ring (1 - cos a) off the arc in its middle.
            double a = Math.Acos(Math.Max(-1.0, Math.Min(1.0, 1.0 - maxSag / Math.Max(ring, 1e-3))));
            double step = Math.Max(2.0 * a * ring, 0.02);
            int n = Math.Max(1, (int)Math.Ceiling((x1 - x0) / step));
            for (int i = 0; i < n; i++)
            {
                float xa = x0 + (x1 - x0) * i / n, xb = x0 + (x1 - x0) * (i + 1) / n;
                pieces.Add((surface.At(xa, d), surface.At(xb, d)));
            }

            return pieces;
        }
    }
}
