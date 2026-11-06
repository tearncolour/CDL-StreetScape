using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The base of a post (0.12.53; 22MR601 交通标志支撑结构设计图, scratch/research-supports-*/REPORT.md): a steel flange on an embedded positioning plate (定位法兰) at the top of the foundation's
    /// pedestal, which is level with the ground (22MR601 9.2.2, JTG/T 3671 3.3.1-7); the anchor bolts through both, each with two nuts; the ribs between the post and the flange. HalfX is across the
    /// road (the arm's or the span's way), HalfZ along it; the bolts are relative to the post's axis.
    /// </summary>
    public sealed class BasePlate
    {
        public float HalfX, HalfZ, Thick, LowerThick, BoltRadius, BoltAbove, RibHigh, RibOut, RibTop, RibEnd, RibThick;
        public float[] BoltX, BoltZ;

        /// <summary>A round flange (0.12.57, 八角锥杆), of radius HalfX (= HalfZ); else rectangular.</summary>
        public bool Round;

        /// <summary>The ribs' directions round a round post (degrees from +X toward +Z); null on an H leg, whose eight ribs stand beside its web and outside its flanges.</summary>
        public float[] RibAngles;

        /// <summary>
        /// A roadside post by its tube (22MR601 pp.74, 76): φ89 on 200 × 200 × 20 over 200 × 200 × 10, 4 M16 120 apart, ribs 150 × 50 × 10 (20 at the top, 30 at the end); φ114 on 300 × 300 × 20 over
        /// 300 × 300 × 20, 4 M20 200 apart, ribs 150 × 60 × 10 (30, 30). The bolts at the corners, the ribs on the centre lines. A φ76 post takes the φ89 base (the atlas has no smaller one); a larger
        /// tube (the posts of a large board on two or three) the cantilever's base of the nearest post (ArmPost).
        /// </summary>
        public static BasePlate Post(float postRadius)
        {
            if (postRadius > 0.06f)
                return ArmPost(postRadius < 0.12f ? 0.1095f : postRadius);
            bool small = postRadius <= 0.045f;
            float half = small ? 0.10f : 0.15f, bolt = small ? 0.06f : 0.10f;
            return new BasePlate
            {
                HalfX = half, HalfZ = half, Thick = 0.02f, LowerThick = small ? 0.01f : 0.02f,
                BoltRadius = small ? 0.008f : 0.010f, BoltAbove = small ? 0.045f : 0.06f,
                BoltX = new[] { -bolt, bolt, -bolt, bolt }, BoltZ = new[] { -bolt, -bolt, bolt, bolt },
                RibHigh = 0.15f, RibOut = small ? 0.05f : 0.06f, RibTop = small ? 0.02f : 0.03f, RibEnd = 0.03f, RibThick = 0.01f,
                RibAngles = new[] { 0f, 90f, 180f, 270f }
            };
        }

        /// <summary>
        /// A cantilever's post (22MR601 pp.79, 81, 85): φ219 on 600 × 400 × 20 with 8 M24 and 4 ribs 250 × 90 × 10; φ273 on 600 × 500 × 25 with 10 M30 and 6 ribs 300 × 110 × 12; φ377 on 800 × 700 × 30
        /// with 12 M30 and 8 ribs 300 × 140 × 12; the larger posts (φ325 of a double cantilever, φ426) on the last. The long side across the road (p.81: the plate's 600 across, the foundation's 2900
        /// along the road), the positioning plate under it 20 mm. The bolts 65 mm in from the edges as on the one plate drawn in plan (p.81: four along each long side, one in the middle of each short
        /// side); the atlas does not draw the 8 and 12 in plan, so they keep the same rows (12: two on each short side). The six ribs as p.81's section has them: two along the road, four within 30
        /// degrees of the arm's way.
        /// </summary>
        public static BasePlate ArmPost(float postRadius)
        {
            int size = postRadius <= 0.11f ? 0 : postRadius <= 0.14f ? 1 : 2;
            float hx = size == 0 ? 0.30f : size == 1 ? 0.30f : 0.40f, hz = size == 0 ? 0.20f : size == 1 ? 0.25f : 0.35f;
            float ex = hx - 0.065f, ez = hz - 0.065f;
            var bx = new System.Collections.Generic.List<float> { -ex, -ex / 3f, ex / 3f, ex, -ex, -ex / 3f, ex / 3f, ex };
            var bz = new System.Collections.Generic.List<float> { -ez, -ez, -ez, -ez, ez, ez, ez, ez };
            if (size == 1)
            {
                bx.AddRange(new[] { -ex, ex });
                bz.AddRange(new[] { 0f, 0f });
            }
            else if (size == 2)
            {
                bx.AddRange(new[] { -ex, -ex, ex, ex });
                bz.AddRange(new[] { -ez / 3f, ez / 3f, -ez / 3f, ez / 3f });
            }

            return new BasePlate
            {
                HalfX = hx, HalfZ = hz, Thick = size == 0 ? 0.02f : size == 1 ? 0.025f : 0.03f, LowerThick = 0.02f,
                BoltRadius = size == 0 ? 0.012f : 0.015f, BoltAbove = size == 0 ? 0.075f : 0.09f,
                BoltX = bx.ToArray(), BoltZ = bz.ToArray(),
                RibHigh = size == 0 ? 0.25f : 0.30f, RibOut = size == 0 ? 0.09f : size == 1 ? 0.11f : 0.14f, RibTop = 0.05f, RibEnd = 0.05f, RibThick = size == 0 ? 0.01f : 0.012f,
                RibAngles = size == 0 ? new[] { 0f, 90f, 180f, 270f } : size == 1 ? new[] { 30f, 90f, 150f, 210f, 270f, 330f } : new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f }
            };
        }

        /// <summary>
        /// 八角锥杆's post by the radius of its corners at the foot (0.12.57): Shanghai DG/TJ 08-2362-2021 fig. A.4.3-6, -7: under 320 法兰9 Ø500 with 8-M30 on Ø410, 320 and over 法兰10 Ø600 with 8-M42
        /// on Ø480 (the slots taken as round holes); 28 mm thick (a maker's octagonal pole: φ550 × 28, szcxjt.com), over a positioning plate as the others'. Eight ribs, one on the middle of each of
        /// the post's flats (a maker's photo, scratch/research-supports-urban/full-i07/dl-i07-t10.jpg), the bolts between them; the ribs' sizes are 22MR601's for φ273 (300 high, 12 thick), 40 mm in
        /// from the plate's edge.
        /// </summary>
        public static BasePlate OctaPost(float footRadius)
        {
            bool large = footRadius >= 0.16f;
            float radius = large ? 0.30f : 0.25f, ring = large ? 0.24f : 0.205f;
            var bx = new float[8];
            var bz = new float[8];
            var ribs = new float[8];
            for (int i = 0; i < 8; i++)
            {
                double a = Math.PI * (2 * i + 1) / 8;
                bx[i] = ring * (float)Math.Cos(a);
                bz[i] = ring * (float)Math.Sin(a);
                ribs[i] = 45f * i;
            }

            float inscribed = footRadius * (float)Math.Cos(Math.PI / 8);
            return new BasePlate
            {
                Round = true, HalfX = radius, HalfZ = radius, Thick = 0.028f, LowerThick = 0.02f,
                BoltRadius = large ? 0.021f : 0.015f, BoltAbove = large ? 0.126f : 0.09f, BoltX = bx, BoltZ = bz,
                RibHigh = 0.30f, RibOut = radius - 0.04f - inscribed, RibTop = 0.05f, RibEnd = 0.05f, RibThick = 0.012f, RibAngles = ribs
            };
        }

        /// <summary>A gantry's post, the planar truss's (22MR601 p.94): 750 × 750 × 20 over the same, 12 M30 (65 in from the edges, 210 and 200 between), 8 ribs 300 × 150 × 12.</summary>
        public static BasePlate Gantry()
        {
            float e = 0.31f, i = 0.10f;
            return new BasePlate
            {
                HalfX = 0.375f, HalfZ = 0.375f, Thick = 0.02f, LowerThick = 0.02f, BoltRadius = 0.015f, BoltAbove = 0.10f,
                BoltX = new[] { -e, -i, i, e, -e, -i, i, e, -e, -e, e, e },
                BoltZ = new[] { -e, -e, -e, -e, e, e, e, e, -i, i, -i, i },
                RibHigh = 0.30f, RibOut = 0.15f, RibTop = 0.05f, RibEnd = 0.05f, RibThick = 0.012f,
                RibAngles = new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f }
            };
        }

        /// <summary>
        /// Each H leg of the space truss's posts (22MR601 p.102): 500 × 400 × 20 over the same (the 400 along the road, 纵向), 6 M36 (340 apart across, 120 along), 8 ribs 250 × 100 × 14; p.102 marks
        /// them beside the web and outside the flanges without placing them exactly, so they stand at the web's two faces and the flanges' outer faces, two each.
        /// </summary>
        public static BasePlate GantryLeg() => new BasePlate
        {
            HalfX = 0.25f, HalfZ = 0.20f, Thick = 0.02f, LowerThick = 0.02f, BoltRadius = 0.018f, BoltAbove = 0.11f,
            BoltX = new[] { -0.17f, -0.17f, -0.17f, 0.17f, 0.17f, 0.17f },
            BoltZ = new[] { -0.12f, 0f, 0.12f, -0.12f, 0f, 0.12f },
            RibHigh = 0.25f, RibOut = 0.10f, RibTop = 0.05f, RibEnd = 0.05f, RibThick = 0.014f
        };
    }
}
