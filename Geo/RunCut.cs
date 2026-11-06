using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The paint of a wide marking along a road cut to the road it lies on (0.12.83; the owner, 3 Nov, of a hatched strip: "这种组合式的地面标线还有问题": bars out over the water beyond a bridge's edge and on the
    /// grass under it). A run's paint was laid from its path alone (MarkingGround.LayRun: each point at the height of the surface under it, or at the terrain's within 3 m, or at its own), and what is
    /// beside the path, the strip's width to its right or left, was never looked at: where the road is narrower than the strip, or the line leaves it, the paint stayed, at the height of whatever it came
    /// to. The areas fitted to the road (AreaFit.Drape) are cut to its surface; a run's paint is now cut to it too (AreaFit.DrapeNear: the surface at the paint's own height, so that a road under a deck
    /// does not take what the deck's edge leaves). Only the markings that spread sideways are (Wants); a line of 15 cm lies where its path is. Pure.
    /// </summary>
    public static class RunCut
    {
        /// <summary>The share of a marking's paint (by area) that must lie over no surface for it to be cut: below it the loss is the seams between the pieces of the road, and the marking is drawn as laid.</summary>
        public const float MinLostShare = 0.005f;

        /// <summary>How far (m) beyond the paint's own box the roads are read for the cut: a piece that only reaches into it is a cell of the fit.</summary>
        public const float ReadMargin = 1f;

        /// <summary>How far (m) beyond the path the paint may reach (the widest strip is 12 m): a road changed within it makes the marking be laid again.</summary>
        public const float Reach = 13f;

        private static readonly string[] s_Ids = { "mk.strip", "mk.rumble.longitudinal" };

        /// <summary>Whether the paint of a marking is cut to the road: the markings that run along a road and spread sideways. A barrier's solids stand on what they stand on, and a line of 15 cm lies where its path is.</summary>
        public static bool Wants(SignDef def) => def != null && def.IsMarking && Array.IndexOf(s_Ids, def.Id) >= 0;

        /// <summary>The box (world) of a marking's paint, widened by ReadMargin: the roads under it are read for the cut.</summary>
        public static void WorldBox(MarkingPlan plan, Vec3 position, float yaw, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            var corners = new List<Vec3>
            {
                new Vec3(plan.MinX, 0f, plan.MinZ), new Vec3(plan.MaxX, 0f, plan.MinZ), new Vec3(plan.MaxX, 0f, plan.MaxZ), new Vec3(plan.MinX, 0f, plan.MaxZ)
            };
            minX = minZ = float.MaxValue;
            maxX = maxZ = float.MinValue;
            foreach (Vec3 p in MarkingPolygon.ToWorld(corners, position, yaw))
            {
                minX = Math.Min(minX, p.X);
                minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X);
                maxZ = Math.Max(maxZ, p.Z);
            }

            minX -= ReadMargin;
            minZ -= ReadMargin;
            maxX += ReadMargin;
            maxZ += ReadMargin;
        }

        /// <summary>
        /// The fit when the paint of the plan must be cut to it (the share of the paint that lies over no surface at its height is at least MinLostShare), else null: the marking is drawn as it was laid, bit
        /// for bit. lostShare is that share; the cut meshes are kept by the fit (AreaFit.DrapedNearOf), so the tile builders find them made.
        /// </summary>
        public static AreaFit Needed(MarkingPlan plan, AreaFit fit, out float lostShare)
        {
            lostShare = 0f;
            if (plan == null || fit == null)
                return null;
            float total = 0f, lost = 0f;
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Solid.HasValue || part.Mesh == null || part.Mesh.VertexCount == 0)
                    continue;
                total += AreaFitRules.Area(part.Mesh);
                fit.DrapedNearOf(part.Mesh, out float partLost);
                lost += partLost;
            }

            if (!(total > 0f))
                return null;
            lostShare = lost / total;
            return lostShare >= MinLostShare ? fit : null;
        }
    }
}
