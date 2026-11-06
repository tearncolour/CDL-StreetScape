using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public static partial class SignBuilder
    {
        /// <summary>
        /// A height-limit frame (0.12.78, SupportDef.HeightLimit; the owner's photo of a tunnel entrance's): at each end a ladder column of two tubes joined by rungs on a plate, and across them, in front,
        /// a light Warren beam of two chords and zigzag diagonals in one plane, a little past the outer tubes; every tube in bands of yellow and black (LimitBands), a rung's own colour alternating up the
        /// column, its ends at the tubes' axes (inside them: a rung's round end never shows at an eight-sided tube's corner). The boards hang on the chords as on any gantry's (BuildGantry). Reduced detail: tubes of one yellow, no rungs. The foot rides down with the ground, never up (a banded tube cannot be shortened).
        /// </summary>
        private static void BuildLimitFrame(SupportLayout layout, SignParts parts)
        {
            float span = layout.ArmLength, r = layout.PoleRadius, chordR = layout.ChordRadius, half = SupportLayouts.LimitColumnGap * 0.5f, zc = SupportLayouts.LimitColumnZ;
            float yLow = layout.ChordYs[0], yTop = layout.ChordYs[1];
            foreach (float px in layout.PoleX)
            {
                float plateTop = GantryFoot(layout, parts, px, zc, layout.Base.HalfX + SupportLayouts.PedestalMargin, layout.Base.HalfZ + SupportLayouts.PedestalMargin, out PostFoot foot);
                foot.MaxRaise = 0f;
                foot.Split = plateTop + 0.05f;
                foreach (float side in new[] { -half, half })
                    BandedTube(parts, new Vec3(px + side, plateTop, zc), new Vec3(px + side, layout.PoleTop, zc), r, 8, false, true, foot);
                if (!t_Reduced)
                {
                    int k = 0;
                    for (float y = plateTop + 0.35f; y < layout.PoleTop - 0.25f; y += SupportLayouts.LimitRungPitch, k++)
                    {
                        float rung = SupportLayouts.LimitRungRadius;
                        Solids.Tube(parts.Solid(k % 2 == 0 ? SolidColour.Yellow : SolidColour.Black), new Vec3(px - half, y, zc), new Vec3(px + half, y, zc), rung, rung, 6, false, false);
                    }
                }

                parts.Picks.Add(new PickShape { Name = "post", Min = new Vec3(px - half - r + 0.02f, plateTop, zc - r + 0.02f), Max = new Vec3(px + half + r - 0.02f, layout.PoleTop, zc + r - 0.02f) });
            }

            float x0 = -(half + r + SupportLayouts.LimitOverhang), x1 = span + half + r + SupportLayouts.LimitOverhang, web = SupportLayouts.LimitWebRadius;
            foreach (float y in layout.ChordYs)
                BandedTube(parts, new Vec3(x0, y, 0f), new Vec3(x1, y, 0f), chordR, 8, true, true, null);
            Nodes(x0 + 0.05f, x1 - 0.05f, SupportLayouts.LimitBay(layout.BeamDepth), out float n0, out float dx, out int bays);
            BandedTube(parts, new Vec3(n0, yLow, 0f), new Vec3(n0, yTop, 0f), web, 6, false, false, null);
            BandedTube(parts, new Vec3(n0 + dx * bays, yLow, 0f), new Vec3(n0 + dx * bays, yTop, 0f), web, 6, false, false, null);
            for (int i = 0; i < bays; i++)
            {
                float xa = n0 + dx * i, xb = xa + dx;
                bool up = i % 2 == 0;
                BandedTube(parts, new Vec3(xa, up ? yLow : yTop, 0f), new Vec3(xb, up ? yTop : yLow, 0f), web, 6, false, false, null);
            }
        }

        /// <summary>
        /// A tube from a to b in the yellow and black bands of LimitBands, in the coloured solids; with foot, the first band is the one that rides down with it (PostFoot.TubeSolids). Reduced detail:
        /// one yellow tube.
        /// </summary>
        private static void BandedTube(SignParts parts, Vec3 a, Vec3 b, float radius, int sides, bool capA, bool capB, PostFoot foot)
        {
            Vec3 axis = b - a;
            float length = axis.Length;
            if (length < 1e-6f)
                return;
            Vec3 d = axis * (1f / length);
            if (t_Reduced)
            {
                MeshBuilder yellow = parts.Solid(SolidColour.Yellow);
                int first = yellow.VertexCount;
                Solids.Tube(yellow, a, b, radius, radius, sides, capA, capB);
                foot?.TubeSolids.Add((SolidColour.Yellow, first, yellow.VertexCount));
                return;
            }

            var bands = LimitBands.Of(length);
            for (int i = 0; i < bands.Count; i++)
            {
                MeshBuilder mesh = parts.Solid(bands[i].Colour);
                int first = mesh.VertexCount;
                Solids.Tube(mesh, a + d * bands[i].From, a + d * bands[i].To, radius, radius, sides, capA && i == 0, capB && i == bands.Count - 1);
                if (foot != null && i == 0)
                    foot.TubeSolids.Add((bands[i].Colour, first, mesh.VertexCount));
            }
        }
    }
}
