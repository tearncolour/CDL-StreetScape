using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>Where the carriageway is beside a post, for the slope of the 立面标记's stripes: to +X (the driver's left), to -X, or on both sides (a post between two carriageways).</summary>
    internal enum RoadSide
    {
        Left,
        Right,
        Both
    }

    public static partial class SignBuilder
    {
        /// <summary>How far the root paint stands off the post (m): outside the post's facets, inside its clamps (Hoop: 5 mm).</summary>
        private const float PaintOff = 0.002f;

        /// <summary>立面标记 (GB 51038-2015 14.6.2): the stripes' width and spacing, and the height above the road they are painted to (m).</summary>
        public const float MarkingStripe = 0.15f, MarkingTop = 2.5f;

        /// <summary>The Qidong drawing 交施-07 杆件喷塑大样: the blue root's height and each ring's (m), and how many rings of each colour.</summary>
        public const float BlueRoot = 1.0f, RootRing = 0.1f;

        public const int RootRings = 3;

        /// <summary>
        /// The paint on a post's root (0.12.57, SupportStyle.Root): a sleeve PaintOff outside the post from its bottom to the paint's top, cut into the stripes or the rings of the paint, each in its solid.
        /// The post is a polygon of sides corners on the radius rFoot at y = 0 and rTop at postTop (a straight post: the two the same), its corners at Tube's angles (a corner to the front) or, faceted,
        /// a flat to the front (Solids.Faceted); the sleeve's corners are at the post's angles, its flats PaintOff outside the post's. All its vertices are the foot's (PostFoot.Paint): it moves with the
        /// foot on the ground, no higher than 1 cm under the post's top (PostFoot.PaintTop). The 立面标记's stripes slope down toward the carriageway; with a carriageway on both sides from the front's
        /// middle down to either side (as the 实体标记 of 14.7.2) and so from the back's middle as well.
        /// </summary>
        private static void PaintRoot(SignParts parts, PostFoot foot, float rFoot, float rTop, float bottom, float postTop, int sides, bool faceted, RoadSide road)
        {
            RootPaint paint = parts.Style.Root;
            if (paint == RootPaint.None)
                return;
            foot.PaintTop = postTop - 0.01f;
            float top = Math.Min(paint == RootPaint.Marking ? MarkingTop : BlueRoot + 2 * RootRings * RootRing, foot.PaintTop);
            if (top < bottom + 0.05f)
                return;
            if (!faceted && Solids.MaxSegments > 0 && sides > Solids.MaxSegments)
                sides = Solids.MaxSegments;
            float RadiusAt(float y) => rFoot + (rTop - rFoot) * y / postTop + (faceted ? 0f : PaintOff);

            var bands = new List<(float From, float To, SolidColour Colour)>();
            double step = 2.0 * Math.PI / sides;
            // Round the post s (m, from the front's middle toward +X) goes with the angle as on the sleeve's flats at the foot; the stripes are q = y + slope * s constant, 15 cm across them is 15 cm × √2
            // of q.
            float chord = 2f * RadiusAt(0f) * (float)Math.Sin(step * 0.5), perAngle = chord / (float)step;
            float slope = road == RoadSide.Right ? -1f : 1f;
            bool level = paint != RootPaint.Marking;
            if (paint == RootPaint.Marking)
            {
                float pitch = MarkingStripe * (float)Math.Sqrt(2.0), reach = (float)Math.PI * perAngle;
                for (int k = (int)Math.Floor((bottom - reach) / pitch); k * pitch < top + reach; k++)
                    bands.Add((k * pitch, (k + 1) * pitch, (k & 1) == 0 ? SolidColour.Yellow : SolidColour.Black));
            }
            else
            {
                bands.Add((bottom, BlueRoot, SolidColour.Blue));
                for (int i = 0; i < 2 * RootRings; i++)
                    bands.Add((BlueRoot + i * RootRing, BlueRoot + (i + 1) * RootRing, i % 2 == 0 ? SolidColour.White : SolidColour.Blue));
            }

            var from = new Dictionary<SolidColour, int>();
            foreach (var band in bands)
                from[band.Colour] = parts.Solid(band.Colour).VertexCount;

            // The pieces of the sleeve: between the corners, cut at the front's and the back's middles (where a chevron turns).
            double phase = faceted ? 0.5 : 0.0;
            var breaks = new List<double> { -Math.PI, 0.0, Math.PI };
            for (int k = -sides; k <= sides; k++)
            {
                double a = (k + phase) * step;
                if (a > -Math.PI + 1e-6 && a < Math.PI - 1e-6 && Math.Abs(a) > 1e-6)
                    breaks.Add(a);
            }

            breaks.Sort();
            Vec3 Radial(double a) => new Vec3((float)Math.Sin(a), 0f, (float)Math.Cos(a));
            for (int j = 0; j + 1 < breaks.Count; j++)
            {
                double a0 = breaks[j], a1 = breaks[j + 1], middle = (a0 + a1) * 0.5;
                double c0 = (Math.Floor(middle / step - phase) + phase) * step;
                Vec3 n0 = Radial(c0), n1 = Radial(c0 + step), flat = MeshBuilder.Normalize(n0 + n1);
                float sigma = level ? 0f : road == RoadSide.Both ? (middle > 0.0 ? 1f : -1f) : slope;
                float s0 = (float)a0 * perAngle, s1 = (float)a1 * perAngle;
                foreach ((float lo, float hi, SolidColour colour) in bands)
                {
                    var poly = new List<(float S, float Y)> { (s0, bottom), (s1, bottom), (s1, top), (s0, top) };
                    poly = Clip(poly, sigma, lo, 1f);
                    poly = Clip(poly, sigma, hi, -1f);
                    if (Area(poly) < 1e-7f)
                        continue;
                    MeshBuilder mesh = parts.Solid(colour);
                    int first = mesh.VertexCount;
                    foreach ((float s, float y) in poly)
                    {
                        float f = (float)((s / perAngle - c0) / step);
                        Vec3 radial = n0 + (n1 - n0) * f;
                        Vec3 p = new Vec3(foot.X, y, foot.Z) + radial * RadiusAt(y) + (faceted ? flat * PaintOff : Vec3.Zero);
                        mesh.Vertex(p, faceted ? flat : MeshBuilder.Normalize(radial), s, y);
                    }

                    for (int i = 1; i + 1 < poly.Count; i++)
                        mesh.TriFacing(first, first + i, first + i + 1, flat);
                }
            }

            foreach (KeyValuePair<SolidColour, int> start in from)
            {
                if (parts.Solids[(int)start.Key].VertexCount > start.Value)
                    foot.Paint.Add((start.Key, start.Value, parts.Solids[(int)start.Key].VertexCount));
            }
        }

        private static float Area(List<(float S, float Y)> poly)
        {
            float twice = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                (float S, float Y) a = poly[i], b = poly[(i + 1) % poly.Count];
                twice += a.S * b.Y - b.S * a.Y;
            }

            return Math.Abs(twice) * 0.5f;
        }

        /// <summary>A convex polygon of (s, y) cut to where y + sigma * s is at least q (side 1) or at most q (side -1).</summary>
        private static List<(float S, float Y)> Clip(List<(float S, float Y)> poly, float sigma, float q, float side)
        {
            var result = new List<(float S, float Y)>(poly.Count + 1);
            for (int i = 0; i < poly.Count; i++)
            {
                (float S, float Y) a = poly[i], b = poly[(i + 1) % poly.Count];
                float da = (a.Y + sigma * a.S - q) * side, db = (b.Y + sigma * b.S - q) * side;
                if (da >= 0f)
                    result.Add(a);
                if (da >= 0f != db >= 0f)
                {
                    float t = da / (da - db);
                    result.Add((a.S + (b.S - a.S) * t, a.Y + (b.Y - a.Y) * t));
                }
            }

            return result;
        }
    }
}
