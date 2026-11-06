using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The rules of fitting an area marking to the road (0.12.4, AreaFit): which kinds of item are areas, how much of an area the road covers, and whether the area is painted on the road only (its own
    /// place is on the road, or the road covers at least MinCover of it) or laid whole on the ground (an island drawn on a square or a car park). Pure.
    /// </summary>
    public static class AreaFitRules
    {
        /// <summary>The least share of an area the road must cover for it to be painted on the road only, when its own place is not on it.</summary>
        public const float MinCover = 0.15f;

        private static HashSet<uint> s_AreaHashes;

        /// <summary>Whether a kind of item is one of the area markings (the hashes of their ids, made once).</summary>
        public static bool IsAreaHash(uint hash)
        {
            if (s_AreaHashes == null)
            {
                var set = new HashSet<uint>();
                foreach (string id in new[] { MarkingTemplates.ChannelId, MarkingTemplates.GridId, MarkingTemplates.SimpleGridId, MarkingTemplates.GoreId })
                    set.Add(SignCatalog.HashOf(id));
                s_AreaHashes = set;
            }

            return s_AreaHashes.Contains(hash);
        }

        /// <summary>The area of a mesh's triangles on the ground.</summary>
        public static float Area(MeshBuilder mesh)
        {
            float sum = 0f;
            if (mesh == null)
                return 0f;
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                sum += Math.Abs(Polygon.Cross(mesh.Positions[mesh.Indices[t]], mesh.Positions[mesh.Indices[t + 1]], mesh.Positions[mesh.Indices[t + 2]])) * 0.5f;
            return sum;
        }

        /// <summary>How much of the area (its outline, or the box of its paint) the road's pieces cover, as a share.</summary>
        public static float Cover(AreaFit road, MarkingPlan plan, string spec, SignDef def)
        {
            if (road == null || plan == null)
                return 0f;
            List<Vec3> outline = def?.Template != null ? MarkingPolygon.Decode(def.Template.Parse(spec ?? "").Get(MarkingPolygon.Key)) : new List<Vec3>();
            if (outline.Count < 3)
                outline = MarkingFills.Rectangle(plan.MinX, plan.MinZ, plan.MaxX, plan.MaxZ);
            var mesh = new MeshBuilder();
            if (Polygon.Fill(mesh, outline) == 0)
                return 0f;
            float whole = Area(mesh), covered = Area(road.Drape(mesh));
            return whole > 1e-4f ? Math.Min(1f, covered / whole) : 0f;
        }

        /// <summary>Whether the area is painted on the road only (else on the ground): its place (the item's origin, or a step along its +Z either way) is on the road, or the road covers MinCover of it.</summary>
        public static bool OnRoad(AreaFit road, MarkingPlan plan, string spec, SignDef def, out float cover)
        {
            cover = 0f;
            if (road == null || road.PieceCount == 0)
                return false;
            cover = Cover(road, plan, spec, def);
            bool here = road.HeightAt(0f, 0f, out _) || road.HeightAt(0f, 0.3f, out _) || road.HeightAt(0f, -0.3f, out _);
            return here || cover >= MinCover;
        }
    }
}
