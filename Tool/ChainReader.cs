using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Colossal.Entities;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;

namespace CDLC.TS
{
    /// <summary>A chain of road segments as it was read: the chain, its segments in its order, the nodes it goes on through, and the approaches to the junctions at its ends (and those it crosses).</summary>
    internal sealed class ChainRead
    {
        public RoadChain Chain;
        public readonly List<Entity> Edges = new List<Entity>();
        public readonly List<Entity> PassNodes = new List<Entity>();
        public readonly List<ChainApproach> Approaches = new List<ChainApproach>();
        public string Log = "";

        /// <summary>The chain was read from a track edge (0.12.66): it follows tracks, and the surface under a line along it is the tracks'.</summary>
        public bool Tracks;

        /// <summary>0.12.47: the next segments whose part of a plain node is in the chain (ChainPiece.Stub): in Edges for their pieces, not segments of the stretch.</summary>
        public readonly HashSet<Entity> Stubs = new HashSet<Entity>();

        /// <summary>How many segments the stretch has (its stubs left out).</summary>
        public int Segments => Edges.Count - Stubs.Count;

        /// <summary>
        /// Whether the read stopped at the reader's limits (RoadChain.MaxPieces, MaxLength) and not at the road's end: a read from another segment of the chain would then reach other ends. The reader
        /// counts the curves' lengths and the chain its pieces', so a chain within a tenth of the length limit counts as capped.
        /// </summary>
        public bool Capped => Chain == null || Edges.Count >= RoadChain.MaxPieces || Chain.Length >= RoadChain.MaxLength * 0.9f;

        /// <summary>The segment at a distance along the chain.</summary>
        public Entity EdgeAt(float s) => Chain == null || Edges.Count == 0 ? Entity.Null : Edges[Math.Min(Edges.Count - 1, Chain.PieceAt(s))];

        /// <summary>The piece of a segment of the chain; null when it is not in it.</summary>
        public ChainPiece PieceOf(Entity edge)
        {
            int i = Edges.IndexOf(edge);
            return Chain != null && i >= 0 && i < Chain.Pieces.Count ? Chain.Pieces[i] : null;
        }
    }

    /// <summary>
    /// Reads a chain of road segments (RoadChain, 0.12.2) from the segment under the cursor, both ways: through every node where the road only goes on (two segments with car lanes meet there), and, when
    /// asked, across a junction to the segment that goes on straightest (within 30 degrees). For each segment its curve, where its car lanes are and run, its speed, its footways and parking; for the
    /// junctions at the chain's ends and those it crosses, the approaches: the lanes that drive into the junction, where each may go there (the junction's lanes that start where it ends: TurnLeft, TurnRight,
    /// UTurn flags; none is straight on), what controls it (TrafficLights, Stop, Yield on those lanes) and a crosswalk across it (a pedestrian lane with the Crosswalk flag). Read only. Main thread.
    /// </summary>
    internal static class ChainReader
    {
        private const float kStraight = 0.866f;

        private static Bezier3 B(Curve c) => new Bezier3(RoadReader.ToVec(c.m_Bezier.a), RoadReader.ToVec(c.m_Bezier.b), RoadReader.ToVec(c.m_Bezier.c), RoadReader.ToVec(c.m_Bezier.d));

        /// <summary>Whether a segment is a road with car lanes (not a path, a track only or a pipe), live and not a preview.</summary>
        /// <summary>
        /// Set while a chain is read from a track edge (0.12.66; the owner: "支持吸附到铁路和地铁线路"): the chain then follows track edges, and only those, through the nodes (a railway, a metro line, a
        /// tram line that is no road); a chain read from a road follows roads as before, and never goes onto a track. Main thread.
        /// </summary>
        private static bool s_Tracks;

        /// <summary>A live edge of the game's railway, metro or tram network (the game marks them with an empty component each).</summary>
        internal static bool IsTrack(EntityManager em, Entity edge) =>
            RoadReader.IsLiveEdge(em, edge) && !em.HasComponent<Game.Tools.Temp>(edge)
            && (em.HasComponent<Game.Net.TrainTrack>(edge) || em.HasComponent<Game.Net.SubwayTrack>(edge) || em.HasComponent<Game.Net.TramTrack>(edge));

        private static bool IsRoad(EntityManager em, Entity edge) => s_Tracks ? IsTrack(em, edge) && !IsCarRoad(em, edge) : IsCarRoad(em, edge);

        private static bool IsCarRoad(EntityManager em, Entity edge)
        {
            if (!RoadReader.IsLiveEdge(em, edge) || em.HasComponent<Game.Tools.Temp>(edge) || !em.TryGetBuffer(edge, true, out DynamicBuffer<Game.Net.SubLane> lanes))
                return false;
            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (lane != Entity.Null && em.HasComponent<Game.Net.CarLane>(lane) && !em.HasComponent<MasterLane>(lane))
                    return true;
            }

            return false;
        }

        /// <summary>The road segments at a node.</summary>
        private static List<Entity> RoadsAt(EntityManager em, Entity node)
        {
            var roads = new List<Entity>();
            if (!em.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> connected))
                return roads;
            for (int i = 0; i < connected.Length; i++)
            {
                if (IsRoad(em, connected[i].m_Edge) && RoadReader.EndsAt(em, connected[i].m_Edge, node))
                    roads.Add(connected[i].m_Edge);
            }

            return roads;
        }

        /// <summary>The way a segment leaves a node (level, length 1): along its curve from that end.</summary>
        private static Vec3 Leaving(EntityManager em, Entity edge, Entity node)
        {
            if (!em.TryGetComponent(edge, out Edge ends) || !em.TryGetComponent(edge, out Curve curve))
                return new Vec3(0f, 0f, 1f);
            Bezier3 b = B(curve);
            return ends.m_Start == node ? b.Tangent(0f).HeadingXZ() : (b.Tangent(1f) * -1f).HeadingXZ();
        }

        /// <summary>The chain from the segment, and (with approaches) the junctions' approaches. single: the segment alone (its ends' nodes are still looked at).</summary>
        /// <summary>
        /// The chain as above; with skipCircuit (the automatic marking: 0.12.6, the markings protocol with CDL : Racing) the chain ends before a segment of Racing's circuit, whose lines Racing draws
        /// (TsExtension.IsReservedRoad). A line the player draws by hand is not stopped.
        /// </summary>
        internal static ChainRead Read(EntityManager em, Entity edge, bool acrossJunctions, bool approaches, bool single, bool skipCircuit, bool stubs = false)
        {
            s_SkipCircuit = skipCircuit;
            try
            {
                return Read(em, edge, acrossJunctions, approaches, single, stubs);
            }
            finally
            {
                s_SkipCircuit = false;
            }
        }

        /// <summary>Set by the overload with skipCircuit for the read it makes (main thread).</summary>
        private static bool s_SkipCircuit;

        /// <summary>As above; stubs (the automatic marking, 0.12.47): a segment alone (single) goes on through a plain node at its ends into the next segment's part of it (Stub).</summary>
        internal static ChainRead Read(EntityManager em, Entity edge, bool acrossJunctions, bool approaches, bool single = false, bool stubs = false) =>
            ReadChain(em, edge, acrossJunctions, approaches, single, stubs, false);

        /// <summary>
        /// The chain of a line drawn from a click (0.12.51): each way from the segment has the reader's limits to itself. Shared, the way ahead took them all on a long road and the line could not go
        /// back past the segment clicked (the owner: "这个方向可以连续但是换个方向又不行了").
        /// </summary>
        internal static ChainRead ReadEachWay(EntityManager em, Entity edge, bool acrossJunctions) => ReadChain(em, edge, acrossJunctions, false, false, false, true);

        private static ChainRead ReadChain(EntityManager em, Entity edge, bool acrossJunctions, bool approaches, bool single, bool stubs, bool eachWay)
        {
            bool before = s_Tracks;
            s_Tracks = !IsCarRoad(em, edge) && IsTrack(em, edge);
            try
            {
                return ReadChainOf(em, edge, acrossJunctions, approaches, single, stubs, eachWay);
            }
            finally
            {
                s_Tracks = before;
            }
        }

        private static ChainRead ReadChainOf(EntityManager em, Entity edge, bool acrossJunctions, bool approaches, bool single, bool stubs, bool eachWay)
        {
            var read = new ChainRead { Tracks = s_Tracks };
            if (!IsRoad(em, edge) || !em.TryGetComponent(edge, out Edge first))
            {
                read.Log = "no road";
                return read;
            }

            // In the chain's order: (edge, reversed, joint after, joins left, joins right).
            var order = new List<(Entity Edge, bool Reversed)> { (edge, false) };
            var joints = new List<(ChainJoint Joint, bool Left, bool Right)> { (ChainJoint.End, false, false) };
            // The roads that join at the node after each piece (0.12.67): what a wall is open at, and whether they were all read.
            var mouthSets = new List<(List<ChainMouth> Mouths, bool Known)> { (null, false) };
            var seen = new HashSet<Entity> { edge };
            var passes = new List<Entity>();
            float firstLength = em.TryGetComponent(edge, out Curve c0) ? c0.m_Length : 0f;
            // What the limits are counted on: the chain so far, or with eachWay the way being read and the first segment.
            int count = 1;
            float length = firstLength;

            // Forward: from the first segment's end node.
            Entity current = edge;
            Entity node = first.m_End;
            while (!single && count < RoadChain.MaxPieces && length < RoadChain.MaxLength)
            {
                if (!Next(em, current, node, acrossJunctions, seen, out Entity next, out ChainJoint joint, out bool left, out bool right, out List<ChainMouth> mouths, out bool mouthsKnown))
                    break;
                joints[joints.Count - 1] = (joint, left, right);
                mouthSets[mouthSets.Count - 1] = (mouths, mouthsKnown);
                if (joint == ChainJoint.Pass)
                    passes.Add(node);
                Edge ends = em.GetComponentData<Edge>(next);
                order.Add((next, ends.m_End == node));
                joints.Add((ChainJoint.End, false, false));
                mouthSets.Add((null, false));
                seen.Add(next);
                count++;
                length += em.TryGetComponent(next, out Curve cn) ? cn.m_Length : 0f;
                current = next;
                node = ends.m_End == node ? ends.m_Start : ends.m_End;
            }

            if (eachWay)
            {
                count = 1;
                length = firstLength;
            }

            // Backward: from the first segment's start node; each segment found goes before the others, its joint after it being the one that was crossed.
            current = edge;
            node = first.m_Start;
            while (!single && count < RoadChain.MaxPieces && length < RoadChain.MaxLength)
            {
                if (!Next(em, current, node, acrossJunctions, seen, out Entity next, out ChainJoint joint, out bool left, out bool right, out List<ChainMouth> mouths, out bool mouthsKnown))
                    break;
                if (joint == ChainJoint.Pass)
                    passes.Add(node);
                Edge ends = em.GetComponentData<Edge>(next);
                // Seen the way the chain runs (toward the node), its left is the walker's right.
                order.Insert(0, (next, ends.m_Start == node));
                joints.Insert(0, (joint, right, left));
                mouthSets.Insert(0, (mouths, mouthsKnown));
                seen.Add(next);
                count++;
                length += em.TryGetComponent(next, out Curve cn) ? cn.m_Length : 0f;
                current = next;
                node = ends.m_End == node ? ends.m_Start : ends.m_End;
            }

            var pieces = new List<ChainPiece>();
            for (int i = 0; i < order.Count; i++)
            {
                ChainPiece piece = Piece(em, order[i].Edge, order[i].Reversed);
                if (piece == null)
                    break;
                // The segment alone: its lines reach the middle of a node at its end where it only meets the next segment (that node is cleared with it), as the clearing tool's lines do.
                if (single && em.TryGetComponent(order[i].Edge, out Edge own))
                {
                    if (RoadsAt(em, own.m_Start).Count == 2)
                        piece.LaneFrom = 0f;
                    if (RoadsAt(em, own.m_End).Count == 2)
                        piece.LaneTo = float.MaxValue;
                }

                piece.After = i == order.Count - 1 ? ChainJoint.End : joints[i].Joint;
                piece.ReaderJoint = piece.After;
                if (i + 1 < order.Count)
                    piece.JointNote = SharedNodeNote(em, order[i].Edge, order[i + 1].Edge);
                piece.JoinLeft = joints[i].Left;
                piece.JoinRight = joints[i].Right;
                if (mouthSets[i].Mouths != null)
                {
                    piece.Mouths.AddRange(mouthSets[i].Mouths);
                    piece.MouthsKnown = mouthSets[i].Known;
                }
                pieces.Add(piece);
                read.Edges.Add(order[i].Edge);
            }

            if (single && stubs && pieces.Count == 1)
            {
                ChainPiece before = Stub(em, edge, first.m_Start, false, out Entity beforeEdge), after = Stub(em, edge, first.m_End, true, out Entity afterEdge);
                if (before != null)
                {
                    before.After = ChainJoint.Pass;
                    pieces.Insert(0, before);
                    read.Edges.Insert(0, beforeEdge);
                    read.Stubs.Add(beforeEdge);
                    passes.Add(first.m_Start);
                }

                if (after != null && afterEdge != beforeEdge)
                {
                    pieces[pieces.Count - 1].After = ChainJoint.Pass;
                    pieces.Add(after);
                    read.Edges.Add(afterEdge);
                    read.Stubs.Add(afterEdge);
                    passes.Add(first.m_End);
                }
            }

            read.Chain = new RoadChain(pieces);
            read.PassNodes.AddRange(passes);
            // 0.12.2: the crosswalks at the nodes the chain goes on through (away from its junctions).
            foreach (Entity pass in passes)
                Crossing(em, read.Chain, pass);
            read.Chain.Crossings.Sort((a, b) => a.S0.CompareTo(b.S0));
            if (approaches && read.Chain.Pieces.Count > 0)
                Approaches(em, read);
            read.Log = $"{read.Segments} segment(s){(read.Stubs.Count > 0 ? $" and the next one's part of {read.Stubs.Count} node(s)" : "")}, {read.Chain.Length:0} m, {passes.Count} node(s) passed through, {read.Approaches.Count} approach(es) to a junction";
            return read;
        }

        /// <summary>The node two segments share, for the log (0.12.66): its index and place, and where each segment's curve ends there: whether they meet at it.</summary>
        private static string SharedNodeNote(EntityManager em, Entity a, Entity b)
        {
            if (!em.TryGetComponent(a, out Edge ea) || !em.TryGetComponent(b, out Edge eb))
                return "";
            Entity node = ea.m_End == eb.m_Start || ea.m_End == eb.m_End ? ea.m_End : ea.m_Start == eb.m_Start || ea.m_Start == eb.m_End ? ea.m_Start : Entity.Null;
            if (node == Entity.Null)
                return "no shared node";
            string place = em.TryGetComponent(node, out Game.Net.Node n) ? $"({n.m_Position.x:0.0}, {n.m_Position.y:0.0}, {n.m_Position.z:0.0})" : "(no position)";
            return $"node {node.Index} {place}, {RoadsAt(em, node).Count} road(s)";
        }

        /// <summary>The shortest part of a plain node (m) that a segment marked alone goes on into.</summary>
        private const float kMinStub = 0.2f;

        /// <summary>
        /// 0.12.47 (the owner: "小节点处没有标线"): the next segment's part of a plain node at an end of a segment marked alone: the other road there, from the node to where it is drawn, the way the
        /// chain runs (atEnd: the node at the chain's end). Before, the lines of the segment reached the node's middle and its other half, cleared with it, had no lines. Null at a junction or a dead
        /// end, beside CDL : Racing's circuit, or where the next segment has no drawn span (or a part under kMinStub).
        /// </summary>
        private static ChainPiece Stub(EntityManager em, Entity edge, Entity node, bool atEnd, out Entity next)
        {
            next = Entity.Null;
            List<Entity> roads = RoadsAt(em, node);
            roads.Remove(edge);
            if (roads.Count != 1 || (s_SkipCircuit && (TsExtension.IsReserved(node) || TsExtension.IsReserved(roads[0]))) || !em.TryGetComponent(roads[0], out Edge ends))
                return null;
            bool nodeAtStart = ends.m_Start == node;
            // At the chain's end the next segment is left from the node (reversed when it ends there); at its start it is come along to the node (reversed when it starts there).
            ChainPiece piece = Piece(em, roads[0], atEnd ? !nodeAtStart : nodeAtStart);
            if (piece == null || float.IsNaN(piece.DrawnFrom))
                return null;
            float length = EdgeInfo.Of(em, roads[0]).Arc.Length;
            if ((nodeAtStart ? piece.DrawnFrom : length - piece.DrawnTo) < kMinStub)
                return null;
            piece.LaneFrom = nodeAtStart ? 0f : piece.DrawnTo;
            piece.LaneTo = nodeAtStart ? piece.DrawnFrom : length;
            piece.Stub = true;
            next = roads[0];
            return piece;
        }

        /// <summary>The segment the chain goes on to from current through node: the only other road there (a node it passes through), or across a junction the straightest one.</summary>
        private static bool Next(EntityManager em, Entity current, Entity node, bool acrossJunctions, HashSet<Entity> seen, out Entity next, out ChainJoint joint, out bool left, out bool right, out List<ChainMouth> mouths, out bool mouthsKnown)
        {
            mouths = null;
            mouthsKnown = false;
            next = Entity.Null;
            joint = ChainJoint.End;
            left = right = false;
            List<Entity> roads = RoadsAt(em, node);
            roads.Remove(current);
            if (roads.Count == 0)
                return false;
            // The automatic marking ends at a node of CDL : Racing's circuit or one where a road of it joins: it does not go on into the circuit, and a junction is not taken for a road that goes on
            // because one of its roads is left out.
            if (s_SkipCircuit && (TsExtension.IsReserved(node) || roads.Exists(r => TsExtension.IsReserved(r))))
                return false;
            if (roads.Count == 1)
            {
                if (seen.Contains(roads[0]))
                    return false;
                next = roads[0];
                joint = ChainJoint.Pass;
                return true;
            }

            if (!acrossJunctions)
                return false;
            // The way the chain arrives: against the way the current segment leaves the node.
            Vec3 arriving = Leaving(em, current, node) * -1f;
            float best = kStraight;
            foreach (Entity road in roads)
            {
                float d = Vec3.Dot(Leaving(em, road, node), arriving);
                if (d > best && !seen.Contains(road))
                {
                    best = d;
                    next = road;
                }
            }

            if (next == Entity.Null)
                return false;
            Vec3 rightOf = arriving.RightXZ();
            // The roads that join, each as the way it leaves the node and half its width (0.12.67). A roundabout, or a road whose curve or width cannot be read, leaves the mouths unknown: the wall is
            // then cut by the lanes as before.
            mouths = new List<ChainMouth>();
            mouthsKnown = !em.HasComponent<Game.Net.Roundabout>(node) && !em.HasComponent<Game.Net.Roundabout>(current) && !em.HasComponent<Game.Net.Roundabout>(next);
            foreach (Entity road in roads)
            {
                if (road == next)
                    continue;
                if (Vec3.Dot(Leaving(em, road, node), rightOf) > 0f)
                    right = true;
                else
                    left = true;
                EdgeInfo joining = EdgeInfo.Of(em, road);
                if (!joining.CurveOk || !(joining.Width > 0f) || em.HasComponent<Game.Net.Roundabout>(road))
                    mouthsKnown = false;
                else
                    mouths.Add(new ChainMouth { Dir = Leaving(em, road, node), HalfWidth = joining.Width * 0.5f });
            }

            joint = ChainJoint.Junction;
            return true;
        }

        /// <summary>A segment as a piece of a chain: its curve, its lanes, where they run along it, its speed, footways and parking.</summary>
        private static ChainPiece Piece(EntityManager em, Entity edge, bool reversed)
        {
            EdgeInfo info = EdgeInfo.Of(em, edge);
            if (!info.CurveOk)
                return null;
            var piece = new ChainPiece { Id = edge.Index, Curve = info.Curve, Reversed = reversed };
            if (info.TryLaterals(em, out List<float> forward, out List<float> backward))
            {
                piece.Forward.AddRange(forward);
                piece.Backward.AddRange(backward);
            }

            piece.HasSpeed = info.TrySpeed(em, out piece.Kmh, out _);
            piece.Raised.AddRange(RoadReader.CompositionRaised(em, edge));
            piece.HalfWidth = Math.Max(2f, info.Width * 0.5f);
            if (RoadReader.TryGameEdges(em, edge, info.Curve, out float gameLeft, out float gameRight))
            {
                piece.GameLeft = gameLeft;
                piece.GameRight = gameRight;
            }

            Carriageway carriageway = info.Carriageway(em);
            piece.FootwayLeft = carriageway.Ok && carriageway.FootwayLeft;
            piece.FootwayRight = carriageway.Ok && carriageway.FootwayRight;
            if (em.TryGetBuffer(edge, true, out DynamicBuffer<Game.Net.SubLane> lanes))
            {
                float from = float.MinValue, to = float.MaxValue;
                int counted = 0;
                float minCar = float.MaxValue, maxCar = float.MinValue;
                var parking = new List<float>();
                for (int i = 0; i < lanes.Length; i++)
                {
                    Entity lane = lanes[i].m_SubLane;
                    if (lane == Entity.Null || !em.TryGetComponent(lane, out Curve curve) || em.HasComponent<MasterLane>(lane))
                        continue;
                    if (em.HasComponent<Game.Net.ParkingLane>(lane))
                    {
                        LaneMath.Across(info.Curve, B(curve), out float p, out _);
                        parking.Add(p);
                        continue;
                    }

                    if (!em.HasComponent<Game.Net.CarLane>(lane))
                        continue;
                    LaneMath.Across(info.Curve, B(curve), out float lateral, out bool with);
                    // 0.12.2: a cycle lane is listed apart (and is not a motor lane); a bus lane is a motor lane, and listed again.
                    if (RoadReader.IsBikeLane(em, lane))
                    {
                        (with ? piece.BikeForward : piece.BikeBackward).Add(lateral);
                        piece.BikeWidth = Math.Max(0.8f, WaitingReader.LaneWidth(em, lane));
                        continue;
                    }

                    if (RoadReader.IsBusLane(em, lane))
                        (with ? piece.BusForward : piece.BusBackward).Add(lateral);
                    minCar = Math.Min(minCar, lateral);
                    maxCar = Math.Max(maxCar, lateral);
                    float a = MarkingRuns.ProjectS(info.Arc, RoadReader.ToVec(curve.m_Bezier.a)), d = MarkingRuns.ProjectS(info.Arc, RoadReader.ToVec(curve.m_Bezier.d));
                    from = Math.Max(from, Math.Min(a, d));
                    to = Math.Min(to, Math.Max(a, d));
                    counted++;
                }

                // 0.12.7: where the segment is drawn (RoadReader.TryDrawnSpan); the lanes' curves only when it has no geometry (they are two halves on a segment that is not one curve, and from them the
                // lines and the stop lines went to the middle of the junctions).
                if (RoadReader.TryDrawnSpan(em, edge, info.Arc, out float drawnFrom, out float drawnTo))
                {
                    piece.LaneFrom = piece.DrawnFrom = drawnFrom;
                    piece.LaneTo = piece.DrawnTo = drawnTo;
                    RoadReader.TryDrawnCorners(em, edge, info.Arc, out piece.StartLeft, out piece.StartRight, out piece.EndLeft, out piece.EndRight);
                }
                else if (counted > 0 && to - from >= 2f)
                {
                    piece.LaneFrom = from;
                    piece.LaneTo = to;
                }

                foreach (float p in parking)
                {
                    if (p > maxCar)
                        piece.ParkingRight = true;
                    if (p < minCar)
                        piece.ParkingLeft = true;
                }
            }

            return piece;
        }

        // ------------------------------------------------------------------ the approaches

        private static void Approaches(EntityManager em, ChainRead read)
        {
            RoadChain chain = read.Chain;
            int last = chain.Pieces.Count - 1;
            // The chain's ends, when they are junctions (three roads or more meet there); a stub's far end is not one of the chain's (the stub ends inside the node before it).
            Entity startNode = NodeAt(em, read.Edges[0], chain.Pieces[0].Reversed, false);
            Entity endNode = NodeAt(em, read.Edges[last], chain.Pieces[last].Reversed, true);
            if (!chain.Pieces[0].Stub && RoadsAt(em, startNode).Count >= 3)
                Add(read, Approach(em, read, 0, startNode, false));
            if (!chain.Pieces[last].Stub && RoadsAt(em, endNode).Count >= 3)
                Add(read, Approach(em, read, last, endNode, true));
            for (int i = 0; i < last; i++)
            {
                if (chain.Pieces[i].After != ChainJoint.Junction)
                    continue;
                Entity node = NodeAt(em, read.Edges[i], chain.Pieces[i].Reversed, true);
                Add(read, Approach(em, read, i, node, true));
                Add(read, Approach(em, read, i + 1, node, false));
            }
        }

        private static void Add(ChainRead read, ChainApproach approach)
        {
            if (approach != null && approach.Lanes.Count > 0)
                read.Approaches.Add(approach);
        }

        /// <summary>The node at the chain's end of a piece (atEnd) or at its start.</summary>
        private static Entity NodeAt(EntityManager em, Entity edge, bool reversed, bool atEnd)
        {
            if (!em.TryGetComponent(edge, out Edge ends))
                return Entity.Null;
            return atEnd != reversed ? ends.m_End : ends.m_Start;
        }

        private struct LaneIn
        {
            public Entity Entity;
            public float Lateral;
            public Lane Lane;
            public float Width;
        }

        /// <summary>The approach of the piece's traffic into the node: with the chain when the node is at the piece's chain end.</summary>
        private static ChainApproach Approach(EntityManager em, ChainRead read, int index, Entity node, bool withChain)
        {
            Entity edge = read.Edges[index];
            ChainPiece piece = read.Chain.Pieces[index];
            if (node == Entity.Null || !em.TryGetComponent(edge, out Edge ends) || !em.TryGetComponent(edge, out Curve edgeCurve) || !em.TryGetBuffer(edge, true, out DynamicBuffer<Game.Net.SubLane> edgeLanes)
                || !em.TryGetBuffer(node, true, out DynamicBuffer<Game.Net.SubLane> nodeLanes))
                return null;
            var approach = new ChainApproach { WithChain = withChain, StopS = withChain ? piece.LanesS1 : piece.LanesS0, PieceIndex = index };
            bool towardEnd = ends.m_End == node;
            Bezier3 edgeBezier = B(edgeCurve);
            var into = new List<LaneIn>();
            for (int i = 0; i < edgeLanes.Length; i++)
            {
                Entity lane = edgeLanes[i].m_SubLane;
                if (!RoadReader.IsMotorLane(em, lane) || !em.TryGetComponent(lane, out Curve laneCurve) || !em.TryGetComponent(lane, out Lane data))
                    continue;
                LaneMath.Across(edgeBezier, B(laneCurve), out float lateral, out bool with);
                // 0.12.7: the half of a lane that ends in the segment's middle is not an approach lane (it read as one with nothing to do at the junction).
                if (with != towardEnd || RoadReader.EndsInsideEdge(em, data, edgeLanes))
                    continue;
                into.Add(new LaneIn { Entity = lane, Lateral = piece.Reversed ? -lateral : lateral, Lane = data, Width = WaitingReader.LaneWidth(em, lane) });
            }

            // From the driver's left: with the chain the smaller laterals; against it the larger.
            into.Sort((a, b) => withChain ? a.Lateral.CompareTo(b.Lateral) : b.Lateral.CompareTo(a.Lateral));
            int signals = 0, stops = 0, yields = 0;
            for (int li = 0; li < into.Count; li++)
            {
                LaneIn lane = into[li];
                var read1 = new ApproachLane { Lateral = lane.Lateral };
                List<Vec3> leftTurn = null;
                float leftLength = float.MaxValue;
                for (int i = 0; i < nodeLanes.Length; i++)
                {
                    Entity n = nodeLanes[i].m_SubLane;
                    if (n == Entity.Null || !em.TryGetComponent(n, out Game.Net.CarLane car) || em.HasComponent<MasterLane>(n) || !em.TryGetComponent(n, out Lane data) || !data.m_StartNode.EqualsIgnoreCurvePos(lane.Lane.m_EndNode))
                        continue;
                    CarLaneFlags f = car.m_Flags;
                    if ((f & (CarLaneFlags.UTurnLeft | CarLaneFlags.UTurnRight)) != 0)
                        read1.UTurn = true;
                    else if ((f & (CarLaneFlags.TurnLeft | CarLaneFlags.GentleTurnLeft)) != 0)
                    {
                        read1.Left = true;
                        // The turn of the lane through the junction, for the guide lines (the tightest when it has several).
                        if (em.TryGetComponent(n, out Curve turnCurve) && turnCurve.m_Length < leftLength)
                        {
                            leftLength = turnCurve.m_Length;
                            leftTurn = Points(B(turnCurve), turnCurve.m_Length);
                        }
                    }
                    else if ((f & (CarLaneFlags.TurnRight | CarLaneFlags.GentleTurnRight)) != 0)
                        read1.Right = true;
                    else
                        read1.Straight = true;
                    if ((f & CarLaneFlags.TrafficLights) != 0)
                        signals++;
                    if ((f & CarLaneFlags.Stop) != 0)
                        stops++;
                    if ((f & CarLaneFlags.Yield) != 0)
                        yields++;
                }

                // Two lane curves of one lane: one lane, with what either may do.
                ApproachLane same = approach.Lanes.Count > 0 && Math.Abs(approach.Lanes[approach.Lanes.Count - 1].Lateral - read1.Lateral) <= MarkingSnap.MergeWithin ? approach.Lanes[approach.Lanes.Count - 1] : null;
                // The left turn of a lane (one for each lane, from the driver's left; the first says whether it is the approach's leftmost lane).
                if (leftTurn != null && (same == null || !same.Left))
                {
                    if (approach.LeftTurns.Count == 0)
                        approach.LeftFromLeftmost = approach.Lanes.Count == 0 || (same != null && approach.Lanes.Count == 1);
                    approach.LeftTurns.Add(leftTurn);
                    approach.LeftWidths.Add(lane.Width);
                }

                if (same != null)
                {
                    same.Straight |= read1.Straight;
                    same.Left |= read1.Left;
                    same.Right |= read1.Right;
                    same.UTurn |= read1.UTurn;
                }
                else
                {
                    approach.Lanes.Add(read1);
                }
            }

            approach.Control = signals > 0 ? JunctionControl.Signals : stops > 0 ? JunctionControl.Stop : yields > 0 ? JunctionControl.Yield : JunctionControl.None;
            approach.CrosswalkAt = Crosswalk(em, read.Chain, approach, nodeLanes, piece, out approach.CrosswalkFar);
            approach.GameCrossLine = GameCrossLine(em, read.Chain, approach, piece, edgeLanes, nodeLanes);
            ReadTraffic(em, into, approach);
            return approach;
        }

        /// <summary>How far apart (m) the fronts of two cars of one standing queue are at most, how slow (m/s) a car of it is, and what is counted behind the last car's front (m).</summary>
        private const float kQueueGap = 15f, kQueueSpeed = 1.5f, kQueueCar = 5f;

        /// <summary>
        /// The approach's traffic (0.12.45, AutoMarking.RoadGuide): the mean speed ratio of its car lanes from the game's traffic flow (CarLane.m_FlowOffset, of the lanes whose LaneFlow measured some
        /// traffic: one no car has used says nothing), and the longest queue standing in one of them: the cars from the lane's end, each within kQueueGap of the one before and slower than kQueueSpeed
        /// (LaneObject: its curve position along the lane, the end at the junction).
        /// </summary>
        private static void ReadTraffic(EntityManager em, List<LaneIn> into, ChainApproach approach)
        {
            float sum = 0f, queue = 0f;
            int measured = 0;
            bool read = false;
            var standing = new List<(float From, bool Stands)>();
            foreach (LaneIn lane in into)
            {
                if (!em.TryGetComponent(lane.Entity, out Game.Net.CarLane car) || em.HasComponent<MasterLane>(lane.Entity))
                    continue;
                read = true;
                if (em.TryGetComponent(lane.Entity, out LaneFlow flow) && flow.m_Distance.x + flow.m_Distance.y + flow.m_Distance.z + flow.m_Distance.w > 0f)
                {
                    sum += TrafficMap.RatioOfOffset(car.m_FlowOffset);
                    measured++;
                }

                if (!em.TryGetBuffer(lane.Entity, true, out DynamicBuffer<LaneObject> objects) || !em.TryGetComponent(lane.Entity, out Curve curve))
                    continue;
                standing.Clear();
                for (int i = 0; i < objects.Length; i++)
                {
                    if (em.TryGetComponent(objects[i].m_LaneObject, out Game.Objects.Moving moving))
                        standing.Add(((1f - objects[i].m_CurvePosition.x) * curve.m_Length, Unity.Mathematics.math.length(moving.m_Velocity) < kQueueSpeed));
                }

                standing.Sort((a, b) => a.From.CompareTo(b.From));
                float last = 0f;
                foreach ((float from, bool stands) in standing)
                {
                    if (!stands || from - last > kQueueGap)
                        break;
                    last = from;
                    queue = Math.Max(queue, from + kQueueCar);
                }
            }

            approach.Flow = measured > 0 ? sum / measured : float.NaN;
            approach.Queue = read ? queue : float.NaN;
        }

        /// <summary>The points of a curve about every 1.2 m (8 to 48 of them).</summary>
        private static List<Vec3> Points(Bezier3 b, float length)
        {
            int n = Math.Max(8, Math.Min(48, (int)Math.Ceiling(length / 1.2f)));
            var points = new List<Vec3>(n + 1);
            for (int i = 0; i <= n; i++)
                points.Add(b.Point(i / (float)n));
            return points;
        }

        /// <summary>
        /// A crosswalk at a node the chain goes on through (0.12.2): a pedestrian lane with the Crosswalk flag that crosses the chain's carriageway there, as the stretch of the chain it covers (its curve's
        /// points projected on the chain, with half its width either side); whether signals control the node's car lanes. Not an unmarked crossing: the game puts one with the Unsafe flag at every node
        /// whose composition has no crosswalk (LaneSystem), where people may cross; 0.12.15 and 0.12.16 took those for crosswalks and cut the barriers at every node of a road (the owner's screenshot,
        /// 25 Oct: "道路节点处不连续").
        /// </summary>
        private static void Crossing(EntityManager em, RoadChain chain, Entity node)
        {
            if (!em.TryGetBuffer(node, true, out DynamicBuffer<Game.Net.SubLane> lanes))
                return;
            bool signals = false;
            for (int i = 0; i < lanes.Length; i++)
            {
                if (em.TryGetComponent(lanes[i].m_SubLane, out Game.Net.CarLane car) && (car.m_Flags & CarLaneFlags.TrafficLights) != 0)
                    signals = true;
            }

            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (lane == Entity.Null || !em.TryGetComponent(lane, out Game.Net.PedestrianLane walk) || (walk.m_Flags & PedestrianLaneFlags.Crosswalk) == 0 || (walk.m_Flags & PedestrianLaneFlags.Unsafe) != 0 || !em.TryGetComponent(lane, out Curve curve))
                    continue;
                Bezier3 b = B(curve);
                float lo = float.MaxValue, hi = float.MinValue, latLo = float.MaxValue, latHi = float.MinValue;
                for (int k = 0; k <= 6; k++)
                {
                    float s = chain.Project(b.Point(k / 6f), out float lateral);
                    lo = Math.Min(lo, s);
                    hi = Math.Max(hi, s);
                    latLo = Math.Min(latLo, lateral);
                    latHi = Math.Max(latHi, lateral);
                }

                // Across the road: from one side of its centre line to the other.
                if (!(latLo < -1f && latHi > 1f))
                    continue;
                float half = WaitingReader.LaneWidth(em, lane) * 0.5f;
                var crossing = new ChainCrossing(lo - half, hi + half, signals);
                if (!chain.Crossings.Exists(c => c.S1 > crossing.S0 && c.S0 < crossing.S1))
                    chain.Crossings.Add(crossing);
            }
        }

        /// <summary>
        /// Whether the game draws a line across the approach (0.12.54): one of its markings across the traffic (ClearMarkingsSystem.IsCrossMarking) among the segment's and the junction's lanes, its middle
        /// within 8 m of the stop and over the approach's half of the road, its ends 1 m apart across it at the least. True when that cannot be told (no clearing system), as before 0.12.54.
        /// </summary>
        private static bool GameCrossLine(EntityManager em, RoadChain chain, ChainApproach approach, ChainPiece piece, DynamicBuffer<Game.Net.SubLane> edgeLanes, DynamicBuffer<Game.Net.SubLane> nodeLanes)
        {
            ClearMarkingsSystem markings = ClearMarkingsSystem.Instance;
            if (markings == null)
                return true;
            // The approach's own half of the road: from the centre line to its outermost lane and 2 m more.
            float lo = 0f, hi = 0f;
            foreach (ApproachLane own in approach.Lanes)
            {
                lo = Math.Min(lo, own.Lateral - 2f);
                hi = Math.Max(hi, own.Lateral + 2f);
            }

            foreach (DynamicBuffer<Game.Net.SubLane> lanes in new[] { edgeLanes, nodeLanes })
            {
                for (int i = 0; i < lanes.Length; i++)
                {
                    Entity lane = lanes[i].m_SubLane;
                    if (lane == Entity.Null || !em.TryGetComponent(lane, out PrefabRef prefab) || !em.TryGetComponent(lane, out Curve curve) || !markings.IsCrossMarking(prefab.m_Prefab))
                        continue;
                    Bezier3 b = B(curve);
                    float s = chain.Project(b.Point(0.5f), out float lateral);
                    chain.Project(b.Point(0f), out float l0);
                    chain.Project(b.Point(1f), out float l1);
                    float beyond = (s - approach.StopS) * approach.Dir;
                    if (beyond > -8f && beyond < 8f && lateral >= lo && lateral <= hi && Math.Abs(l1 - l0) >= 1f)
                        return true;
                }
            }

            return false;
        }

        /// <summary>How far beyond the approach's stop (m, toward the junction) the near side of the crosswalk across it is, and its far side; NaN when none is found.</summary>
        private static float Crosswalk(EntityManager em, RoadChain chain, ChainApproach approach, DynamicBuffer<Game.Net.SubLane> nodeLanes, ChainPiece piece, out float far)
        {
            float best = float.NaN;
            far = float.NaN;
            var edge = new List<(float Lateral, float Near)>();
            float reach = 2f;
            foreach (float x in piece.With)
                reach = Math.Max(reach, Math.Abs(x) + 2f);
            foreach (float x in piece.Against)
                reach = Math.Max(reach, Math.Abs(x) + 2f);
            for (int i = 0; i < nodeLanes.Length; i++)
            {
                Entity lane = nodeLanes[i].m_SubLane;
                if (lane == Entity.Null || !em.TryGetComponent(lane, out Game.Net.PedestrianLane walk) || (walk.m_Flags & PedestrianLaneFlags.Crosswalk) == 0 || (walk.m_Flags & PedestrianLaneFlags.Unsafe) != 0 || !em.TryGetComponent(lane, out Curve curve))
                    continue;
                Bezier3 b = B(curve);
                float half = WaitingReader.LaneWidth(em, lane) * 0.5f;
                float near = float.MaxValue, farSide = float.MinValue;
                var samples = new List<(float Lateral, float Near)>();
                for (int k = 0; k <= 8; k++)
                {
                    float s = chain.Project(b.Point(k / 8f), out float lateral);
                    float beyond = (s - approach.StopS) * approach.Dir;
                    if (Math.Abs(lateral) > reach || beyond < -8f || beyond > 15f)
                        continue;
                    samples.Add((lateral, beyond - half));
                    near = Math.Min(near, beyond - half);
                    farSide = Math.Max(farSide, beyond + half);
                }

                if (samples.Count > 0 && (float.IsNaN(best) || near < best))
                {
                    best = near;
                    far = farSide;
                    edge = samples;
                }
            }

            edge.Sort((p, q) => p.Lateral.CompareTo(q.Lateral));
            approach.CrosswalkEdge.Clear();
            approach.CrosswalkEdge.AddRange(edge);
            return best;
        }
    }
}
