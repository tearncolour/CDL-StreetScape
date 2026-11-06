using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The pictograms of 路1+ (gb.guide8.01c): one road width for the whole board (MultiRoadK of the character height, the stem, the bars and the ramps alike), the road ends of Fig. 205 (the gable of
    /// Arm: a right-angled point on the road's axis) and the rounded inner corners of its junctions (Fillet), and a grade-separated junction drawn as the road it crosses broken where the stem
    /// passes over it, with ramps that leave the stem and join the bar tangentially (no stroke crosses another). Pure geometry in the board's frame (x right, y down, the stem on x = 0).
    /// </summary>
    public static partial class RoadGuideSigns
    {
        /// <summary>The width of every road of the board, in character heights (Fig. 205's roads are 0.4 h; a board of several junctions is drawn a little finer).</summary>
        internal const float MultiRoadK = 0.36f;

        /// <summary>The free blue between the stem and the bar it passes over, in character heights.</summary>
        internal const float MultiPassGap = 0.14f;

        /// <summary>Where each kind reaches, in character heights from the bar's axis: the arm to the point, the ramps up and down.</summary>
        internal static float MultiArmOf(char kind) => kind == 'l' ? 2.7f : kind == 'd' ? 3.1f : 1.5f;

        internal const float MultiRampReach = 1.7f, MultiRampRun = 2.0f, MultiLoopRadius = 0.62f;

        /// <summary>The stem: up from the foot to the tip (y goes down, so the tip is the smaller y), a gable at the tip.</summary>
        internal static float[] MultiStem(float foot, float tip, float w) => Arm(0f, foot, 0f, -1f, foot - tip, w, true);

        /// <summary>
        /// The bar of a junction on one side of the stem (side 1 right, -1 left) at height y, to the point at arm from the stem's axis. At grade it springs from the stem with the inner corners
        /// rounded; a grade-separated junction's bar starts a gap away from the stem (the stem passes over it).
        /// </summary>
        internal static void MultiBar(List<float[]> roads, int side, float y, float arm, float w, float r, bool separated, float gap)
        {
            float hw = w * 0.5f;
            if (separated)
            {
                float x0 = hw + gap;
                roads.Add(Arm(side * x0, y, side, 0f, arm - x0, w, true));
                return;
            }

            roads.Add(Arm(0f, y, side, 0f, arm, w, true));
            roads.Add(Fillet(side * hw, y - hw, 0f, -1f, side, 0f, r));
            roads.Add(Fillet(side * hw, y + hw, 0f, 1f, side, 0f, r));
        }

        /// <summary>The points of a cubic Bezier curve, n + 1 of them, as x, y pairs.</summary>
        internal static List<float> MultiCurve(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, int n = 24)
        {
            var pts = new List<float>((n + 1) * 2);
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n, u = 1f - t;
                float a = u * u * u, b = 3f * u * u * t, c = 3f * u * t * t, d = t * t * t;
                pts.Add(a * x0 + b * x1 + c * x2 + d * x3);
                pts.Add(a * y0 + b * y1 + c * y2 + d * y3);
            }

            return pts;
        }

        /// <summary>
        /// The ramps of a diamond interchange on one side of the stem at bar height y: one above and one below the bar, each leaving the stem a ramp-reach away from the bar at a shallow angle and
        /// bending into the bar to run along it (they join it tangentially), so that the ramp is the stem's own lane leaving and joining the crossing road.
        /// </summary>
        internal static void MultiDiamond(List<float[]> roads, int side, float y, float w, float h)
        {
            float ry = MultiRampReach * h, rx = MultiRampRun * h;
            foreach (int up in new[] { -1, 1 })
                roads.Add(Thick(MultiCurve(0f, y + up * ry, side * 0.16f * rx, y + up * ry * 0.62f, side * 0.5f * rx, y, side * rx, y), w));
        }

        /// <summary>
        /// The loop ramp of a half cloverleaf on one side of the stem, above the bar (the P): the stem is the ring's inner side; the ring leaves the stem above the bar, goes round on the
        /// outside and comes back to the bar's axis tangentially, where the bar goes on (a loop turns a quarter circle further than the bar: the ring's lowest point is on the bar's axis).
        /// The centre line is a circle of radius rc whose leftmost point is the stem's axis, so the ring's inner edge touches the stem's outer edge: the ring is the stem's bulge, not a ball.
        /// </summary>
        internal static void MultiLoop(List<float[]> roads, int side, float y, float w, float h)
        {
            float rc = MultiLoopRadius * h;
            float cx = side * rc, cy = y - rc;
            // The ring, from the bar's axis (its lowest point, travelling away from the stem) round to the stem's axis: three quarters of a circle, as a thick line.
            var line = new List<float>();
            const int n = 48;
            for (int i = 0; i <= n; i++)
            {
                double t = Math.PI * 1.5 * i / n;
                // y goes down: angle 0 points along +x, the lowest point is at +90 degrees.
                double ang = side > 0 ? Math.PI * 0.5 - t : Math.PI * 0.5 + t;
                line.Add(cx + (float)Math.Cos(ang) * rc);
                line.Add(cy + (float)Math.Sin(ang) * rc);
            }

            roads.Add(Thick(line, w));
        }

        /// <summary>How far above and below the bar's axis a junction's pictogram reaches (character heights are in h).</summary>
        internal static void MultiExtent(char kind, float w, float h, out float up, out float down)
        {
            float hw = w * 0.5f;
            switch (kind)
            {
                case 'd':
                    up = down = MultiRampReach * h + hw;
                    break;
                case 'l':
                    up = 2f * MultiLoopRadius * h + hw + 0.05f * h;
                    down = hw + MultiPassGap * h;
                    break;
                default:
                    up = down = hw + 0.05f * h;
                    break;
            }
        }
    }
}
