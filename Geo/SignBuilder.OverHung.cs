using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public static partial class SignBuilder
    {
        /// <summary>
        /// A board hung under a structure over the road (0.12.81, SupportDef.Hanger; the sizes are this mod's: GB 5768.2 draws no hung board): its channels clamped round a tube that passes behind its upper part
        /// (HangOnTubes, as on a cantilever's arm; the board leans about it as there), the tube as wide as the board, and from the tube at each of SupportLayout.HangerXs a rod up to a plate on the soffit. The rods' plates
        /// have four bolts; two sway braces run from the tube's outer rods back and up to plates OverRoad.BraceRun behind (the bolts of the plates are full detail only). The structure is the game's and is not drawn: the plates' top faces are
        /// the soffit's plane, SupportLayouts.PlateInto sunk into it.
        /// </summary>
        private static void BuildOverHung(SupportLayout layout, SignParts parts, List<Placement> placed)
        {
            float tube = layout.HangRadius, soffit = layout.HangerTop, y = layout.ArmY;
            foreach (Placement p in placed)
            {
                float[] channels = HangOnTubes(layout, parts, p, new[] { y }, x => layout.HangRadius);
                float rod = SupportLayouts.HangerRodRadius(p.Def.Width), reach = 0f;
                foreach (float x in channels)
                    reach = Math.Max(reach, Math.Abs(x - p.Cx) + 0.03f);
                foreach (float x in layout.HangerXs)
                    reach = Math.Max(reach, Math.Abs(x) + rod);
                // The tube reaches a little past the outermost clamp and rod, and stays behind the board's width.
                float half = Math.Min(p.Def.Width * 0.5f - 0.005f, reach + 0.04f);
                Solids.Tube(parts.Metal, new Vec3(p.Cx - half, y, 0f), new Vec3(p.Cx + half, y, 0f), tube, tube, 12, true, true);
                foreach (float x in layout.HangerXs)
                {
                    Solids.Tube(parts.Metal, new Vec3(p.Cx + x, y, 0f), new Vec3(p.Cx + x, soffit - SupportLayouts.PlateThick * 0.5f, 0f), rod, rod, 8, true, true);
                    SoffitPlate(parts, layout, p.Cx + x, 0f);
                }

                // The braces stand on the outermost rods' feet, so that each brace and its rod make a triangle with the soffit. Far tiles keep them: they are as far back as anything of it.
                foreach (float x in new[] { layout.HangerXs[0], layout.HangerXs[layout.HangerXs.Length - 1] })
                {
                    Solids.Tube(parts.Metal, new Vec3(p.Cx + x, y, 0f), new Vec3(p.Cx + x, soffit - SupportLayouts.PlateThick * 0.5f, -OverRoad.BraceRun), rod * 0.8f, rod * 0.8f, 8, true, true);
                    SoffitPlate(parts, layout, p.Cx + x, -OverRoad.BraceRun);
                }
            }
        }

        /// <summary>A plate under the soffit at (x, z) with its four bolts (full detail): its top face SupportLayouts.PlateInto over the soffit's plane, so that it is not the structure's own face.</summary>
        private static void SoffitPlate(SignParts parts, SupportLayout layout, float x, float z)
        {
            float low = layout.HangerTop - SupportLayouts.PlateThick, high = layout.HangerTop + SupportLayouts.PlateInto, half = OverRoad.PlateHalf;
            Solids.Box(parts.Metal, new Vec3(x, (low + high) * 0.5f, z), new Vec3(half, (high - low) * 0.5f, half));
            if (t_Reduced)
                return;
            float bolt = half - 0.025f;
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                    Solids.Tube(parts.Metal, new Vec3(x + sx * bolt, low - 0.011f, z + sz * bolt), new Vec3(x + sx * bolt, low + 0.002f, z + sz * bolt), 0.007f, 0.007f, 6, true, true);
            }
        }
    }
}
