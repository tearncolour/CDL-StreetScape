using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The lighting of a board (0.12.80, SignLighting): a light box's housing behind an internally lit board, and the lamp bars of an externally lit one. Everything is geometry of the assembly, in the
    /// tile's meshes: the glow is the existing white glow solid, lit at night by TsMaterials (GlowNits times FaceNight), so by day a lamp is a white plastic lens and nothing of it shines.
    /// </summary>
    public static partial class SignBuilder
    {
        /// <summary>The light box's housing: the least and the most depth (m).</summary>
        public const float BoxDepthMin = 0.10f, BoxDepthMax = 0.22f;

        /// <summary>
        /// An external lamp bar: how far in front of the face its arm carries it, how far beyond the board's edge it hangs, its section and the thickness of its lens (m). The reach is one length for every
        /// board, not one that grows with the board's height: a lamp bar on one edge is even enough only on a small board (IlluminanceRatio), and a bar on each edge is, at any reach from 0.35 m to 0.6 m
        /// (the model's scan, 0.3 m gives a ratio of 3.9 on the largest board): so the layout is two opposed bars with a short arm, not one bar on a long one.
        /// </summary>
        public const float BarReach = 0.40f, BarOffset = 0.10f, BarBody = 0.06f, BarLensDepth = 0.012f, BarArmRadius = 0.014f;

        /// <summary>A bar's length is BarFill of the board's width at BarChordIn of its height from the edge; no bar where that is under BarLeast of the board's width.</summary>
        public const float BarFill = 0.9f, BarChordIn = 0.04f, BarLeast = 0.35f;

        /// <summary>Where a lamp bar's lens aims on the face, as a share of the face's height from the bar's own edge: at the far part, so that the near part is not over-lit.</summary>
        public const float BarAim = 0.5f;

        /// <summary>The depth of the housing of a lit board: shallow for a small board, deeper for a large one, within the bounds above.</summary>
        public static float BoxDepthOf(float width, float height) => Math.Max(BoxDepthMin, Math.Min(BoxDepthMax, 0.07f + 0.045f * Math.Max(width, height)));

        /// <summary>
        /// The ratio of the greatest to the least illuminance across a face of this width and height lit by lamp bars (a line of point sources along the width) over its top edge and under its bottom edge
        /// (top and bottom say which of them are there), BarReach in front of the face and BarOffset beyond the edge, each aimed BarAim of the way across the face with a narrow beam (cosine cubed):
        /// the inverse square of the distance and the cosines of the lamp's and the face's angles, on a grid of points of the face. A MODEL, not a measurement: it is how the layout was chosen and what the
        /// tests hold it to; 4.8.16 c 2 asks the ratio to be under 4 and that is a property of the lamp's optics, which a drawing cannot show.
        /// </summary>
        public static float IlluminanceRatio(float width, float height, bool top, bool bottom, float reach)
        {
            double min = double.MaxValue, max = 0.0;
            const int nx = 16, ny = 40, bar = 60;
            for (int ix = 0; ix < nx; ix++)
            {
                double x = (ix + 0.5) / nx * width;
                for (int iy = 0; iy < ny; iy++)
                {
                    double y = (iy + 0.5) / ny * height;
                    double e = 0.0;
                    for (int side = 0; side < 2; side++)
                    {
                        if (side == 0 ? !top : !bottom)
                            continue;
                        double ly = side == 0 ? height + BarOffset : -BarOffset, ay = side == 0 ? height * (1.0 - BarAim) : height * BarAim;
                        double my = ay - ly, mz = reach, md = Math.Sqrt(my * my + mz * mz);
                        for (int k = 0; k < bar; k++)
                        {
                            double lx = (k + 0.5) / bar * width;
                            double dx = x - lx, dy = y - ly, dz = reach;
                            double d2 = dx * dx + dy * dy + dz * dz, d = Math.Sqrt(d2);
                            double cosLamp = Math.Max(0.0, (dy * my + dz * mz) / (d * md));
                            e += dz / d * cosLamp * cosLamp * cosLamp / d2;
                        }
                    }

                    min = Math.Min(min, e);
                    max = Math.Max(max, e);
                }
            }

            return min <= 0.0 ? float.PositiveInfinity : (float)(max / min);
        }

        /// <summary>
        /// The lighting of the main board of a placement (SignLighting.Of the instance's settings; none draws nothing and a classic sign has none). Internal: a housing of BoxDepthOf behind the board, so
        /// the board is a light box; the rim round the face is the board's own edge. External: a lamp bar over the top edge and one under the bottom edge, each on two arms from the board's back out to
        /// BarReach in front of the face, with the lens toward the face. The housing and the arms are the board's steel; the lens is the white glow solid. A far tile (reduced detail) has the housing and
        /// the bars, not the arms.
        /// </summary>
        private static void AddLighting(SignParts parts, Placement placement, float backZ, float faceZ)
        {
            SignInstance instance = placement.Board.Instance;
            LightingKind kind = instance.Settings == null ? LightingKind.None : SignLighting.Of(instance.Settings);
            if (kind == LightingKind.None)
                return;

            float width = placement.Def.Width, height = placement.Def.Height, cx = placement.Cx, cy = placement.Cy;
            MeshBuilder steel = BackOf(parts);
            if (kind == LightingKind.Internal)
            {
                float depth = BoxDepthOf(width, height);
                Solids.Box(steel, new Vec3(cx, cy, backZ - depth * 0.5f), new Vec3(width * 0.5f, height * 0.5f, depth * 0.5f));
                return;
            }

            MeshBuilder lens = parts.Solid(SolidColour.GlowWhite);
            float barZ = faceZ + BarReach;
            foreach (float side in new[] { 1f, -1f })
            {
                // The bar is as long as the board is wide where it is (a circle, an octagon or a triangle is narrower toward its edge: ChordAt, a little in from the edge) and not more than 90 % of it, so it
                // never hangs past the outline; a side where the board comes to a point, a third of its width or less, has no bar.
                float length = SignCatalog.ChordAt(placement.Def, side * (height * 0.5f - BarChordIn * height)) * BarFill;
                if (length < width * BarLeast)
                    continue;
                // The bar over the top edge looks down at the face, the one under the bottom edge looks up: the housing is BarBody tall, its lens on the side toward the face.
                float edge = cy + side * height * 0.5f, y = edge + side * BarOffset, half = length * 0.5f;
                Solids.Box(steel, new Vec3(cx, y, barZ), new Vec3(half, BarBody * 0.5f, BarBody * 0.5f));
                Solids.Box(lens, new Vec3(cx, y - side * (BarBody * 0.5f + BarLensDepth * 0.5f), barZ), new Vec3(half * 0.96f, BarLensDepth * 0.5f, BarBody * 0.4f));
                if (t_Reduced)
                    continue;
                // Two arms near the ends of the bar, from the board's back at its edge out and up to the bar: where the board is as wide as the bar, so they stand on it.
                foreach (float k in new[] { -0.38f, 0.38f })
                    Solids.Tube(steel, new Vec3(cx + k * length, edge, backZ), new Vec3(cx + k * length, y, barZ), BarArmRadius, BarArmRadius, 6, false, false);
            }
        }
    }
}
