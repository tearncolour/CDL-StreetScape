using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Colossal.Entities;
using Game.Net;
using Game.UI;
using Unity.Entities;

namespace CDLC.TS
{
    /// <summary>
    /// The game's road network as the pure walks read it (TrafficWalk for the traffic-state screens, JunctionRead for the junction signs): edges by the index of their entity, built when first asked
    /// for (the centre line as points, the name of the road and, when asked, the car lanes with where each leads at its end). What flows on them (the congestion of each direction, an accident) is
    /// not this mod's to read since 0.12.29: the extension that does gives the graph an annotator (TsExtension). Read only; nothing here changes game data. Main thread; one instance for one reading
    /// (it keeps what it read).
    /// </summary>
    internal sealed class GameRoadGraph : ITrafficGraph
    {
        private readonly EntityManager m_Em;
        private readonly NameSystem m_Names;
        private readonly bool m_ReadLanes;
        private readonly Action<Entity, TwEdge, Bezier3> m_Annotate;
        private readonly Dictionary<int, Entity> m_EdgeEntities = new Dictionary<int, Entity>();
        private readonly Dictionary<int, Entity> m_NodeEntities = new Dictionary<int, Entity>();
        private readonly Dictionary<int, TwEdge> m_Built = new Dictionary<int, TwEdge>();
        private readonly Dictionary<int, string> m_NameOf = new Dictionary<int, string>();

        public int LanesRead, EdgesBuilt;

        /// <summary>For the log of the lane reading: the lanes of the edges read with where each leads (only when the lanes are read).</summary>
        public readonly List<string> LaneLog = new List<string>();

        /// <summary>annotate: what is read of each edge built besides its shape (the edge's entity, the edge, its centre line); null for nothing.</summary>
        public GameRoadGraph(EntityManager em, NameSystem names, bool readLanes, Action<Entity, TwEdge, Bezier3> annotate = null)
        {
            m_Em = em;
            m_Names = names;
            m_ReadLanes = readLanes;
            m_Annotate = annotate;
        }

        public void Register(Entity edge) => m_EdgeEntities[edge.Index] = edge;

        public bool TryEdge(int id, out TwEdge edge)
        {
            if (m_Built.TryGetValue(id, out edge))
                return edge != null;
            edge = null;
            if (m_EdgeEntities.TryGetValue(id, out Entity entity))
                edge = Build(entity, id);
            m_Built[id] = edge;
            return edge != null;
        }

        public IReadOnlyList<int> EdgesAt(int node)
        {
            var list = new List<int>();
            if (!m_NodeEntities.TryGetValue(node, out Entity entity) || !m_Em.TryGetBuffer(entity, true, out DynamicBuffer<ConnectedEdge> connected))
                return list;
            for (int i = 0; i < connected.Length; i++)
            {
                Entity e = connected[i].m_Edge;
                if (!IsRoad(e) || !RoadReader.EndsAt(m_Em, e, entity))
                    continue;
                m_EdgeEntities[e.Index] = e;
                if (!list.Contains(e.Index))
                    list.Add(e.Index);
            }

            return list;
        }

        private bool IsRoad(Entity e) => RoadReader.IsLiveEdge(m_Em, e) && m_Em.HasComponent<Road>(e) && !m_Em.HasComponent<Game.Tools.Temp>(e);

        private TwEdge Build(Entity entity, int id)
        {
            if (!IsRoad(entity) || !m_Em.TryGetComponent(entity, out Game.Net.Edge ends) || !m_Em.TryGetComponent(entity, out Curve curve) || !m_Em.TryGetBuffer(entity, true, out DynamicBuffer<Game.Net.SubLane> lanes))
                return null;

            var bezier = new Bezier3(RoadReader.ToVec(curve.m_Bezier.a), RoadReader.ToVec(curve.m_Bezier.b), RoadReader.ToVec(curve.m_Bezier.c), RoadReader.ToVec(curve.m_Bezier.d));
            int steps = Math.Max(2, Math.Min(14, (int)Math.Ceiling(curve.m_Length / 25f) + 1));
            var points = new float[steps * 2];
            for (int i = 0; i < steps; i++)
            {
                Vec3 p = bezier.Point(i / (float)(steps - 1));
                points[i * 2] = p.X;
                points[i * 2 + 1] = p.Z;
            }

            m_NodeEntities[ends.m_Start.Index] = ends.m_Start;
            m_NodeEntities[ends.m_End.Index] = ends.m_End;

            // The car lanes, for the junction signs (the master lanes of a group stand for its lanes and are left out).
            var edge = new TwEdge { Id = id, StartNode = ends.m_Start.Index, EndNode = ends.m_End.Index, Points = points };
            for (int i = 0; m_ReadLanes && i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (lane == Entity.Null || !m_Em.TryGetComponent(lane, out Game.Net.CarLane car) || m_Em.HasComponent<MasterLane>(lane) || !m_Em.TryGetComponent(lane, out Curve laneCurve))
                    continue;
                var laneBezier = new Bezier3(RoadReader.ToVec(laneCurve.m_Bezier.a), RoadReader.ToVec(laneCurve.m_Bezier.b), RoadReader.ToVec(laneCurve.m_Bezier.c), RoadReader.ToVec(laneCurve.m_Bezier.d));
                LaneMath.Across(bezier, laneBezier, out float lateral, out bool with);
                LanesRead++;
                int moves = MovesAt(lane, with ? ends.m_End : ends.m_Start, out int matched, out string flags);
                edge.Lanes.Add(new TwLane { Lateral = lateral, WithEdge = with, Moves = moves });
                edge.Highway |= (car.m_Flags & CarLaneFlags.Highway) != 0;
                if (LaneLog.Count < 60)
                    LaneLog.Add($"edge {id} lane {lanes[i].m_SubLane.Index}: {lateral:0.0} m {(lateral >= 0f ? "right" : "left")} of the centre line, runs {(with ? "with" : "against")} the edge, own flags {car.m_Flags}, leads to {matched} junction lane(s) at its end {flags} -> moves {moves}");
            }

            // The speeds are RIE's (its annotation sets them); the junction signs read lanes only, and an edge with none in either direction is not a road to them.
            if (m_ReadLanes)
            {
                edge.DirectionsFromLanes();
                if (m_Em.TryGetComponent(entity, out Elevation elevation))
                    edge.Rise = Math.Max(Math.Abs(elevation.m_Elevation.x), Math.Abs(elevation.m_Elevation.y));
            }

            int aggregate = 0;
            string name = "";
            if (m_Em.TryGetComponent(entity, out Aggregated aggregated) && aggregated.m_Aggregate != Entity.Null)
            {
                aggregate = aggregated.m_Aggregate.Index;
                if (!m_NameOf.TryGetValue(aggregate, out name))
                    m_NameOf[aggregate] = name = NameOf(aggregated.m_Aggregate);
            }

            edge.Name = name;
            edge.Road = aggregate;
            EdgesBuilt++;
            m_Annotate?.Invoke(entity, edge, bezier);
            return edge;
        }

        /// <summary>
        /// Where a lane leads at the end it runs to: the junction lanes of the node (the node's sub-lanes) that start where this lane ends (the same path node), each with the movement its flags say
        /// (CarLaneFlags.TurnLeft and GentleTurnLeft, Forward, TurnRight and GentleTurnRight, the U-turns). The lanes of the junction are made by the game's LaneSystem, which sets those flags on every lane
        /// inside an intersection; **that a lane's end node is the start node of the lanes that continue it is read from the game's code, and is checked by the log of the first readings** (LaneLog).
        /// </summary>
        private int MovesAt(Entity lane, Entity node, out int matched, out string flags)
        {
            matched = 0;
            flags = "";
            if (!m_Em.TryGetComponent(lane, out Game.Net.Lane own) || !m_Em.TryGetBuffer(node, true, out DynamicBuffer<Game.Net.SubLane> nodeLanes))
                return 0;
            int moves = 0;
            var seen = new List<string>();
            for (int i = 0; i < nodeLanes.Length; i++)
            {
                Entity other = nodeLanes[i].m_SubLane;
                if (other == Entity.Null || !m_Em.TryGetComponent(other, out Game.Net.Lane lane2) || !m_Em.TryGetComponent(other, out Game.Net.CarLane car2) || m_Em.HasComponent<MasterLane>(other))
                    continue;
                if (!lane2.m_StartNode.EqualsIgnoreCurvePos(own.m_EndNode))
                    continue;
                matched++;
                CarLaneFlags f = car2.m_Flags;
                if ((f & (CarLaneFlags.TurnLeft | CarLaneFlags.GentleTurnLeft)) != 0)
                    moves |= JrMoves.Left;
                if ((f & (CarLaneFlags.TurnRight | CarLaneFlags.GentleTurnRight)) != 0)
                    moves |= JrMoves.Right;
                if ((f & CarLaneFlags.Forward) != 0)
                    moves |= JrMoves.Ahead;
                if ((f & (CarLaneFlags.UTurnLeft | CarLaneFlags.UTurnRight)) != 0)
                    moves |= JrMoves.UTurn;
                if (seen.Count < 6)
                    seen.Add(f.ToString());
            }

            flags = seen.Count > 0 ? "[" + string.Join("; ", seen) + "]" : "[]";
            return moves;
        }

        /// <summary>The name the game shows for a road (its aggregate). A text that is only a localisation id (no entry for it in the language) is no name.</summary>
        private string NameOf(Entity aggregate)
        {
            try
            {
                string text = m_Names.GetRenderedLabelName(aggregate);
                if (string.IsNullOrWhiteSpace(text))
                    return "";
                text = text.Trim();
                bool ascii = true;
                foreach (char c in text)
                    ascii &= c < 128;
                return ascii && text.IndexOf('.') > 0 && text.IndexOf(' ') < 0 ? "" : text;
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
