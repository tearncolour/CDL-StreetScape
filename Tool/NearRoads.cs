using System.Collections.Generic;
using System.Text;
using CDLC.TS.Snap;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// For the log (0.12.95): the road segments near a place where the tool's ray found none, with how far each is in the plane and in height, its level and width, whether the game lists it as a
    /// road (Road component), what the layers its prefab connects are and what its geometry would give the ray. Asked when a road that is plainly there is not found (a cutting, a tunnel mouth: the
    /// owner's "预览不跟路"), so that the next log says whether there is a road under the cursor that the ray misses, and why, or none. Read only, main thread, at most a few lines a session.
    /// </summary>
    internal static class NearRoads
    {
        private struct Iterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Box;
            public List<Entity> Found;

            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Box);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Box))
                    Found.Add(item);
            }
        }

        /// <summary>The edges of the game's net search tree whose bounds reach within radius (m) of the place in the ground plane (an edge's bounds are those of the whole segment: a long box), each once; main thread.</summary>
        internal static List<Entity> Within(SearchSystem search, float3 at, float radius)
        {
            var found = new List<Entity>();
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = search.GetNetSearchTree(true, out Unity.Jobs.JobHandle dependencies);
            dependencies.Complete();
            var iterator = new Iterator { Box = new Bounds2(new float2(at.x - radius, at.z - radius), new float2(at.x + radius, at.z + radius)), Found = found };
            tree.Iterate(ref iterator);
            return found;
        }

        /// <summary>The nearest few road segments within radius (m) of the place, one line each; "none within N m" when there are none.</summary>
        internal static string Describe(EntityManager em, SearchSystem search, float3 at, float radius, int most = 5)
        {
            List<Entity> found = Within(search, at, radius);

            Vec3 here = RoadReader.ToVec(at);
            var rows = new List<(float Distance, string Text)>();
            var seen = new HashSet<Entity>();
            foreach (Entity entity in found)
            {
                if (!seen.Add(entity) || !em.HasComponent<Edge>(entity) || !RoadReader.TryRead(em, entity, out Bezier3 curve, out float width))
                    continue;
                float t = curve.ClosestT(here);
                Vec3 nearest = curve.Point(t);
                float distance = Vec3.DistanceXZ(nearest, here);
                string layers = em.TryGetComponent(entity, out PrefabRef prefab) && em.TryGetComponent(prefab.m_Prefab, out NetData net) ? net.m_ConnectLayers.ToString() : "no prefab data";
                string elevation = em.TryGetComponent(entity, out Elevation lift) ? $"elevation {lift.m_Elevation.x:0.0}/{lift.m_Elevation.y:0.0}" : "no Elevation";
                var line = new StringBuilder();
                line.Append($"edge {entity.Index}:{entity.Version} {distance:0.0} m away in the plane, {nearest.Y - at.y:+0.0;-0.0} m in height, width {width:0.#} m, {(em.HasComponent<Road>(entity) ? "Road" : "no Road component")}, layers {layers}, {elevation}, level {RoadReader.LevelOf(em, entity)}");
                rows.Add((distance, line.ToString()));
            }

            if (rows.Count == 0)
                return $"no road segment within {radius:0} m";
            rows.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var text = new StringBuilder();
            for (int i = 0; i < rows.Count && i < most; i++)
                text.Append(i == 0 ? "" : "; ").Append(rows[i].Text);
            return text.ToString();
        }
    }
}
