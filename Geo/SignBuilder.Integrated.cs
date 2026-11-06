using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// 上海型综合杆 (0.12.57): the Shanghai multi-function pole after DG/TJ 08-2362-2021 (S1) and the 2018 合杆整治技术导则 (S2), scratch/research-supports-urban/REPORT.md card A and
    /// scratch/research-octagon/REPORT.md. What a figure of either gives is named; what only photos show is marked an estimate.
    /// </summary>
    public static partial class SignBuilder
    {
        /// <summary>The upper pole (副杆): Ø160 aluminium (S1 table A.4.2-2), to 12.5 m (S2 appendix B: H2); the lamp's head at 12 m (S2: H1 of types C1, D1, E1).</summary>
        public const float UpperRadius = 0.08f, IntegratedHeight = 12.5f, LampHeight = 12.0f;

        /// <summary>
        /// The lamp's arm (S1 fig. A.4.3-11: Ø76 at the clamp, Ø60 at the lamp, 1.5 to 3 m): 2.5 m out (within the figure's range, not measured), rising LampRise to the head over its S (an estimate
        /// from S2's drawings and the Zhuguang Rd photo, ±30 %); its oval clamp 280 × 156 × 240 (S1 A.1.3).
        /// </summary>
        public const float LampReach = 2.5f, LampRise = 0.75f;

        /// <summary>The slots (卡槽, S1 fig. A.3.5-1: a 14 mm mouth) on four faces from 2.5 m (S1 A.2.2); the door's foot at 0.5 m (S2: h2), its top 1.3 m and width 65 (S1 figs. A.2.3-2, A.2.2).</summary>
        public const float SlotFrom = 2.5f, SlotMouth = 0.014f, DoorBottom = 0.5f, DoorTop = 1.3f, DoorWidth = 0.065f;

        /// <summary>
        /// The Shanghai pole's door (检修门), built with its foot so that it moves with it on the ground: a panel 3 mm proud of the main pole's back flat (away from the road; the photos show it on a
        /// side face, which one is not known), from DoorBottom to DoorTop, in a dark seam 3 mm wide (the gap round the door, which is what shows of it on a dark pole).
        /// </summary>
        private static void IntegratedDoor(SignParts parts, SupportLayout layout, PostFoot foot)
        {
            if (t_Reduced)
                return;
            float flat = (float)Math.Cos(Math.PI / layout.PoleSides), face = layout.PoleRadiusAt(DoorBottom) * flat;
            Solids.Box(parts.Metal, new Vec3(0f, (DoorBottom + DoorTop) * 0.5f, -face - 0.0015f), new Vec3(DoorWidth * 0.5f, (DoorTop - DoorBottom) * 0.5f, 0.0015f + 0.002f));
            MeshBuilder seam = parts.Solid(SolidColour.Black);
            int from = seam.VertexCount;
            Solids.Box(seam, new Vec3(0f, (DoorBottom + DoorTop) * 0.5f, -face), new Vec3(DoorWidth * 0.5f + 0.003f, (DoorTop - DoorBottom) * 0.5f + 0.003f, 0.0025f));
            foot.Paint.Add((SolidColour.Black, from, seam.VertexCount));
        }

        /// <summary>
        /// The Shanghai pole above its arms and along its faces: the slots' mouths (dark strips 2 mm proud on the four faces that look along and across the road, from SlotFrom to under the lowest arm's
        /// plate), the collar at their foot and the one over the joint of the main and the upper pole (装饰件, S1 A.1.6; their sizes estimated from the Valmont and Zhuguang Rd photos), the upper pole
        /// with its reserved flange on top (S2: 预留法兰), the lamp's arm on its clamp reaching over the road (side) and the lamp's head (0.70 × 0.30 × 0.10, an estimate from the photos).
        /// </summary>
        private static void IntegratedFittings(SignParts parts, SupportLayout layout, float side)
        {
            int sides = layout.PoleSides;
            float flat = (float)Math.Cos(Math.PI / sides), top = layout.PoleTop, topR = layout.PoleRadiusAt(top);
            float slotTop = layout.ArmYs[layout.ArmYs.Length - 1] - SupportLayouts.JointFlangeOf(layout) - 0.05f;
            if (!t_Reduced && slotTop > SlotFrom + 0.3f)
            {
                MeshBuilder dark = parts.Solid(SolidColour.Black);
                for (int k = 0; k < 4; k++)
                {
                    var n = new Vec3((float)Math.Sin(k * Math.PI / 2), 0f, (float)Math.Cos(k * Math.PI / 2));
                    var across = new Vec3(n.Z, 0f, -n.X);
                    float r0 = layout.PoleRadiusAt(SlotFrom) * flat, r1 = layout.PoleRadiusAt(slotTop) * flat;
                    Vec3 a = n * (r0 + 0.001f), b = n * (r1 + 0.001f);
                    Solids.Slab(dark, new[] { a + across * (SlotMouth * 0.5f) + new Vec3(0f, SlotFrom, 0f), a - across * (SlotMouth * 0.5f) + new Vec3(0f, SlotFrom, 0f),
                        b - across * (SlotMouth * 0.5f) + new Vec3(0f, slotTop, 0f), b + across * (SlotMouth * 0.5f) + new Vec3(0f, slotTop, 0f) }, n * 0.001f);
                }

                float ringR = layout.PoleRadiusAt(SlotFrom) + 0.012f;
                Solids.Faceted(parts.Metal, new Vec3(0f, SlotFrom - 0.06f, 0f), new Vec3(0f, SlotFrom, 0f), ringR, ringR, sides, true, true);
            }

            // The collar over the joint, and a cone from it to the upper pole.
            float collarR = topR + 0.02f, collarTop = top + 0.15f;
            Solids.Faceted(parts.Metal, new Vec3(0f, top - 0.05f, 0f), new Vec3(0f, collarTop, 0f), collarR, collarR, sides, true, true);
            Solids.Tube(parts.Metal, new Vec3(0f, collarTop, 0f), new Vec3(0f, collarTop + 0.12f, 0f), collarR * flat, UpperRadius, 24, false, false);
            float poleTop = Math.Max(IntegratedHeight, collarTop + 1.0f);
            Solids.Tube(parts.Metal, new Vec3(0f, collarTop + 0.12f, 0f), new Vec3(0f, poleTop, 0f), UpperRadius, UpperRadius, 24, false, false);
            Solids.Tube(parts.Metal, new Vec3(0f, poleTop, 0f), new Vec3(0f, poleTop + 0.012f, 0f), UpperRadius + 0.012f, UpperRadius + 0.012f, 24, true, true);
            parts.Picks.Add(PostPick(0f, 0f, UpperRadius + 0.02f, collarTop, poleTop + 0.012f));

            // The lamp: its clamp round the upper pole, the arm along a cubic from the clamp to the head (level at both ends), the head beyond its end.
            float lampY = Math.Min(LampHeight, poleTop - 0.3f), clampY = lampY - LampRise;
            Solids.Tube(parts.Metal, new Vec3(0f, clampY - 0.12f, 0f), new Vec3(0f, clampY + 0.12f, 0f), UpperRadius + 0.012f, UpperRadius + 0.012f, 24, true, true);
            Solids.Box(parts.Metal, new Vec3(side * (UpperRadius + 0.03f), clampY, 0f), new Vec3(0.04f, 0.10f, 0.078f));
            var p0 = new Vec3(side * (UpperRadius + 0.06f), clampY, 0f);
            var p3 = new Vec3(side * LampReach, lampY, 0f);
            Vec3 p1 = new Vec3(side * LampReach * 0.5f, clampY, 0f), p2 = new Vec3(side * LampReach * 0.5f, lampY, 0f);
            const int steps = 10;
            Vec3 last = p0;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps, u = 1f - t;
                Vec3 at = p0 * (u * u * u) + p1 * (3f * u * u * t) + p2 * (3f * u * t * t) + p3 * (t * t * t);
                float r0 = 0.038f - 0.008f * (i - 1) / steps, r1 = 0.038f - 0.008f * i / steps;
                Solids.Tube(parts.Metal, last, at, r0, r1, 10, false, i == steps);
                last = at;
            }

            Solids.Box(parts.Metal, new Vec3(side * (LampReach + 0.33f), lampY, 0f), new Vec3(0.35f, 0.05f, 0.15f));
            Solids.Box(parts.Solid(SolidColour.LightGray), new Vec3(side * (LampReach + 0.36f), lampY - 0.052f, 0f), new Vec3(0.28f, 0.003f, 0.11f));
        }
    }
}
