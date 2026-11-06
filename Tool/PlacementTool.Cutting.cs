using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Colossal.Mathematics;
using Game.Common;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// A road in a cutting (0.12.96; the owner, 5 Nov, after three builds that did not help): the game's ray hits the terrain above it, which is nearer to the camera, and reports that, so the cursor
    /// "found no road" and a sign was put down free. The road is open to the sky and the player points at it. Where the ray's hit is the terrain, the camera's own ray is put through the surface of the
    /// roads near (RoadUnderCursor), and a road it meets inside its width, at least a metre and a half under that ground, takes the place of the terrain hit: the owner and the hit are those of a hit on
    /// the road, so that everything after (the edge, the snap, the preview, the heights) goes as for any road. A road that is covered (a tunnel) is left out: the hill over it is what the cursor is on.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        private int m_CuttingLogs;
        private Entity m_CuttingLogEdge;

        /// <summary>
        /// Puts the road the cursor points at, under the terrain the game's ray hit, in place of that hit (owner and hit are changed; true when they were). Not with the underground view on (the ray then
        /// takes the roads under the ground itself) and not when the hit is no terrain.
        /// </summary>
        private bool RoadBelowGround(ref Entity owner, ref RaycastHit hit)
        {
            if (requireUnderground || owner == Entity.Null || !EntityManager.HasComponent<Game.Common.Terrain>(owner) || m_Camera.activeCamera == null)
                return false;
            m_NoRoadSearch = m_NoRoadSearch ?? World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            Line3.Segment ray = ToolRaycastSystem.CalculateRaycastLine(m_Camera.activeCamera);
            // A slanted view puts the place on the road far from where the ray crosses the terrain's height: the depth below the ground and a long slant give tens of metres.
            var candidates = new List<RoadUnderCursor.Candidate>();
            var edges = new List<Entity>();
            foreach (Entity edge in NearRoads.Within(m_NoRoadSearch, hit.m_HitPosition, 80f))
            {
                if (!EntityManager.HasComponent<Game.Net.Road>(edge) || EntityManager.HasComponent<Temp>(edge)
                    || !RoadReader.TryReadSurface(EntityManager, edge, out Bezier3 curve, out float width, out float rise, out bool covered) || covered)
                    continue;
                candidates.Add(new RoadUnderCursor.Candidate { Curve = curve, Width = width, Rise = rise });
                edges.Add(edge);
            }

            if (!RoadUnderCursor.Pick(candidates, RoadReader.ToVec(ray.a), RoadReader.ToVec(ray.b), hit.m_HitPosition.y, out RoadUnderCursor.Found found))
                return false;

            Entity road = edges[found.Index];
            if (road != m_CuttingLogEdge && m_CuttingLogs++ < 30)
            {
                m_CuttingLogEdge = road;
                Mod.log.Info($"Tool: the ray hit the ground at ({hit.m_HitPosition.x:0.0}, {hit.m_HitPosition.y:0.0}, {hit.m_HitPosition.z:0.0}); the camera's ray goes through the surface of the road segment {road.Index}:{road.Version} under it, "
                    + $"{hit.m_HitPosition.y - found.Hit.Y:0.0} m lower at ({found.Hit.X:0.0}, {found.Hit.Y:0.0}, {found.Hit.Z:0.0}), {(found.Outside < 0f ? -found.Outside : 0f):0.0} m inside its edge: that road is taken.");
            }

            owner = road;
            hit.m_HitEntity = road;
            hit.m_Position = new float3(found.Centre.X, found.Centre.Y, found.Centre.Z);
            hit.m_HitPosition = new float3(found.Hit.X, found.Hit.Y, found.Hit.Z);
            hit.m_CurvePosition = found.T;
            return true;
        }
    }
}
