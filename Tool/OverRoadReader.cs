using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// Reads the structures that cross over a road from the game's nets (0.12.81, SupportKind.OverRoad): the edges of the net search tree near a point whose composition is elevated (CompositionFlags.General.Elevated:
    /// a bridge, an overpass, an elevated road; the game's own test, NetUtils and GeometrySystem use it) and not a tunnel, with their centre curve and the height range of their cross-section (NetCompositionData.
    /// HeightRange: the lowest and highest part of it above the curve, as the game makes the edge's bounds from it, GeometrySystem). ECS components only: no mesh and no geometry asset is touched
    /// (GeometryGate: the game's loader and a synchronous read of the same asset must not meet). Main thread; the tree is the game's, read the way ItemRoadSystem reads it.
    /// </summary>
    internal static class OverRoadReader
    {
        /// <summary>How far round the road's point the structures are looked for (m): a bridge's centre line passes within half its width of the road's point, and the widest is about 40 m.</summary>
        internal const float Reach = 30f;

        private struct Iterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Box;
            public float RoadY, Floor;
            public List<Entity> Found;

            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Box);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                // Only what reaches above the road: its box's top is higher than the road plus the least free height, so a road on the ground beside it is not a candidate.
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Box) && bounds.m_Bounds.max.y > RoadY + Floor)
                    Found.Add(item);
            }
        }

        /// <summary>The structures over a road around a point of it (x, z), for OverRoad.Find. roadEdge is left out (a road is not over itself); the list is the caller's and is filled.</summary>
        internal static void Around(EntityManager em, Game.Net.SearchSystem search, float x, float z, float roadY, Entity roadEdge, List<OverStructure> into)
        {
            into.Clear();
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = search.GetNetSearchTree(true, out JobHandle dependencies);
            dependencies.Complete();
            var found = new List<Entity>();
            var iterator = new Iterator
            {
                Box = new Bounds2(new float2(x - Reach, z - Reach), new float2(x + Reach, z + Reach)),
                RoadY = roadY,
                Floor = OverRoad.MinFree,
                Found = found
            };
            tree.Iterate(ref iterator);
            foreach (Entity edge in found)
            {
                if (edge == roadEdge || !Read(em, edge, out OverStructure structure))
                    continue;
                into.Add(structure);
            }
        }

        /// <summary>One edge as a structure: a live edge with a curve and a composition that is elevated and not a tunnel. False for a road on the ground, a tunnel, a node or an edge that cannot be read.</summary>
        private static bool Read(EntityManager em, Entity edge, out OverStructure structure)
        {
            structure = default;
            if (!RoadReader.IsLiveEdge(em, edge) || !em.TryGetComponent(edge, out Composition composition) || composition.m_Edge == Entity.Null
                || !em.TryGetComponent(composition.m_Edge, out NetCompositionData data) || !em.TryGetComponent(edge, out Curve curve))
                return false;
            CompositionFlags.General general = data.m_Flags.m_General;
            var bezier = new Bezier3(RoadReader.ToVec(curve.m_Bezier.a), RoadReader.ToVec(curve.m_Bezier.b), RoadReader.ToVec(curve.m_Bezier.c), RoadReader.ToVec(curve.m_Bezier.d));
            return OverRoad.Describe((general & CompositionFlags.General.Elevated) != 0, (general & CompositionFlags.General.Tunnel) != 0, data.m_Width, data.m_HeightRange.min, data.m_HeightRange.max,
                edge.Index, bezier, out structure);
        }
    }
}
