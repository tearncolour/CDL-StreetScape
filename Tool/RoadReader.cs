using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Colossal.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// Reads the road network for the snapping kernel: the centre curve of an edge and the width of its cross-section, and which edge a
    /// raycast hit belongs to. Read only; nothing here changes game data. Main thread.
    /// </summary>
    internal static class RoadReader
    {
        internal static Vec3 ToVec(float3 v) => new Vec3(v.x, v.y, v.z);

        internal static bool IsLiveEdge(EntityManager em, Entity edge) =>
            edge != Entity.Null && em.Exists(edge) && em.HasComponent<Edge>(edge) && em.HasComponent<Curve>(edge) && !em.HasComponent<Deleted>(edge);

        /// <summary>
        /// Whether a lane is a cycle lane (0.12.2): a road lane whose prefab is for bicycles only (NetLaneData BicyclesOnly). The game makes them car lanes, and until 0.12.2 every reader here counted them as
        /// lanes of the motor traffic (an edge line was drawn beyond the cycle lane, a lane screen hung over it).
        /// </summary>
        internal static bool IsBikeLane(EntityManager em, Entity lane) =>
            em.TryGetComponent(lane, out Game.Prefabs.PrefabRef prefab) && em.TryGetComponent(prefab.m_Prefab, out Game.Prefabs.NetLaneData data) && (data.m_Flags & Game.Prefabs.LaneFlags.BicyclesOnly) != 0;

        /// <summary>
        /// Whether a lane of an edge ends inside it (0.12.7): on a segment that is not one curve the game lays each lane as two halves (LaneSystem.CreateEdgeLanes, path nodes 0, 2 and 4 along), and the
        /// first half ends where the second starts, in the middle of the segment. Such a half does not run into the junction: it is not one of the approach's lanes.
        /// </summary>
        internal static bool EndsInsideEdge(EntityManager em, Lane lane, DynamicBuffer<Game.Net.SubLane> edgeLanes)
        {
            for (int i = 0; i < edgeLanes.Length; i++)
            {
                Entity other = edgeLanes[i].m_SubLane;
                if (other != Entity.Null && em.TryGetComponent(other, out Lane next) && next.m_StartNode.EqualsIgnoreCurvePos(lane.m_EndNode))
                    return true;
            }

            return false;
        }

        /// <summary>A lane of the motor traffic: a car lane that is not a master lane of a junction's group and not a cycle lane.</summary>
        internal static bool IsMotorLane(EntityManager em, Entity lane) =>
            lane != Entity.Null && em.Exists(lane) && em.HasComponent<Game.Net.CarLane>(lane) && !em.HasComponent<MasterLane>(lane) && !IsBikeLane(em, lane);

        /// <summary>A bus lane: a motor lane for public transport only (CarLaneFlags.PublicOnly).</summary>
        internal static bool IsBusLane(EntityManager em, Entity lane) =>
            em.TryGetComponent(lane, out Game.Net.CarLane car) && (car.m_Flags & CarLaneFlags.PublicOnly) != 0;

        /// <summary>Whether the edge's composition is elevated (a viaduct or a bridge: the game's Elevated flag).</summary>
        internal static bool IsElevated(EntityManager em, Entity edge) =>
            em.TryGetComponent(edge, out Composition composition) && composition.m_Edge != Entity.Null && em.TryGetComponent(composition.m_Edge, out NetCompositionData data)
            && (data.m_Flags.m_General & CompositionFlags.General.Elevated) != 0;

        /// <summary>
        /// 0.12.101, for the log: how far the game's own surface reaches to the left and to the right of a segment's curve at its middle (EdgeGeometry: the end of the first half of each side), both
        /// positive. The edge a barrier is given is worked out from the composition's width and its lanes (BarrierAuto.EdgeOf); this is what the game draws.
        /// </summary>
        internal static bool TryGameEdges(EntityManager em, Entity edge, Bezier3 curve, out float left, out float right)
        {
            left = right = float.NaN;
            if (!em.TryGetComponent(edge, out EdgeGeometry geometry))
                return false;
            Vec3 l = ToVec(geometry.m_Start.m_Left.d), r = ToVec(geometry.m_Start.m_Right.d);
            if (!l.IsFinite || !r.IsFinite)
                return false;
            left = -AcrossCurve(curve, l);
            right = AcrossCurve(curve, r);
            return true;
        }

        private static float AcrossCurve(Bezier3 curve, Vec3 p)
        {
            float t = curve.ClosestT(p, 8);
            return Vec3.Dot(p - curve.Point(t), curve.Tangent(t).HeadingXZ().RightXZ());
        }

        /// <summary>The centre line of an edge and the width of its cross-section (m), taken from its composition.</summary>
        internal static bool TryRead(EntityManager em, Entity edge, out Bezier3 curve, out float width)
        {
            curve = default;
            width = 0f;
            if (!IsLiveEdge(em, edge) || !em.TryGetComponent(edge, out Curve c))
                return false;

            curve = new Bezier3(ToVec(c.m_Bezier.a), ToVec(c.m_Bezier.b), ToVec(c.m_Bezier.c), ToVec(c.m_Bezier.d));
            if (em.TryGetComponent(edge, out Composition composition) && composition.m_Edge != Entity.Null &&
                em.TryGetComponent(composition.m_Edge, out NetCompositionData data))
                width = data.m_Width;
            return true;
        }

        /// <summary>For the log (0.12.94): the level of the segment's cross-section (ground, elevated, tunnel, with a lowered side) and the collision mask the game gives it (NetUtils.GetCollisionMask), which is what the tool's ray has to share.</summary>
        internal static string LevelOf(EntityManager em, Entity edge)
        {
            if (!em.TryGetComponent(edge, out Composition composition) || composition.m_Edge == Entity.Null || !em.TryGetComponent(composition.m_Edge, out NetCompositionData data))
                return "no cross-section";
            var general = data.m_Flags.m_General;
            string level = (general & CompositionFlags.General.Elevated) != 0 ? "elevated" : (general & CompositionFlags.General.Tunnel) != 0 ? "tunnel" : "ground";
            string lowered = ((data.m_Flags.m_Left & CompositionFlags.Side.Lowered) != 0 ? " lowered left" : "") + ((data.m_Flags.m_Right & CompositionFlags.Side.Lowered) != 0 ? " lowered right" : "");
            string raised = ((data.m_Flags.m_Left | data.m_Flags.m_Right) & CompositionFlags.Side.Raised) != 0 ? " raised side" : "";
            return $"{level}{lowered}{raised}, collision {Game.Net.NetUtils.GetCollisionMask(data, true)}";
        }

        /// <summary>What TryLanes found, for the log: what the lanes of the edge were by the two ways of telling them apart.</summary>
        internal struct LanesReport
        {
            public int SubLanes, CarLanes, Masters;

            /// <summary>How many of the car lanes (not masters) run each way by the old test (which end of the edge the lane starts nearer to) and by the new one (the tangent); the distinct places across (merged within half a metre).</summary>
            public int OldForward, OldBackward, NewForward, NewBackward, DistinctForward, DistinctBackward;

            /// <summary>The lateral places of the lanes, positive to the right of the edge's direction, with F for with the edge and B for against it.</summary>
            public string Places;

            public override string ToString() =>
                $"{SubLanes} sublane(s), {CarLanes} car lane(s), {Masters} master(s) left out; by the starts {OldForward}+{OldBackward}, by the tangents {NewForward}+{NewBackward}, distinct {DistinctForward}+{DistinctBackward}; across the road [{Places}] m";
        }

        /// <summary>
        /// The car lanes of an edge by direction: those that run the way the edge does (forward) and those against it (backward), as distinct lanes: a lane is told apart by where its middle runs
        /// against the edge's centre line (LaneMath.Across), and lanes within half a metre of one another that run the same way count once. The master lanes of the junctions' groups are not
        /// counted (they stand for the lanes of the group), nor are lanes that are not car lanes. False when the edge cannot be read or has no car lane.
        /// </summary>
        internal static bool TryLanes(EntityManager em, Entity edge, out int forward, out int backward) => TryLanes(em, edge, out forward, out backward, out _);

        internal static bool TryLanes(EntityManager em, Entity edge, out int forward, out int backward, out LanesReport report)
        {
            forward = 0;
            backward = 0;
            report = default;
            if (!IsLiveEdge(em, edge) || !em.TryGetComponent(edge, out Curve edgeCurve) || !em.TryGetBuffer(edge, true, out DynamicBuffer<Game.Net.SubLane> lanes))
                return false;

            var edgeBezier = new Bezier3(ToVec(edgeCurve.m_Bezier.a), ToVec(edgeCurve.m_Bezier.b), ToVec(edgeCurve.m_Bezier.c), ToVec(edgeCurve.m_Bezier.d));
            var laterals = new List<float>();
            var forwards = new List<bool>();
            report.SubLanes = lanes.Length;
            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (lane == Entity.Null || !em.Exists(lane) || !em.HasComponent<Game.Net.CarLane>(lane) || !em.TryGetComponent(lane, out Curve laneCurve) || IsBikeLane(em, lane))
                    continue;
                report.CarLanes++;
                if (em.HasComponent<MasterLane>(lane))
                {
                    report.Masters++;
                    continue;
                }

                // The old test, kept to be logged beside the new one.
                float3 start = laneCurve.m_Bezier.a;
                if (math.distancesq(start, edgeCurve.m_Bezier.a) <= math.distancesq(start, edgeCurve.m_Bezier.d))
                    report.OldForward++;
                else
                    report.OldBackward++;

                var bezier = new Bezier3(ToVec(laneCurve.m_Bezier.a), ToVec(laneCurve.m_Bezier.b), ToVec(laneCurve.m_Bezier.c), ToVec(laneCurve.m_Bezier.d));
                LaneMath.Across(edgeBezier, bezier, out float lateral, out bool with);
                laterals.Add(lateral);
                forwards.Add(with);
                if (with)
                    report.NewForward++;
                else
                    report.NewBackward++;
            }

            LaneMath.CountDistinct(laterals, forwards, out List<float> distinctForward, out List<float> distinctBackward);
            report.DistinctForward = distinctForward.Count;
            report.DistinctBackward = distinctBackward.Count;
            var places = new List<string>();
            foreach (float x in distinctBackward)
                places.Add(x.ToString("0.0") + "B");
            foreach (float x in distinctForward)
                places.Add(x.ToString("0.0") + "F");
            places.Sort((a, b) => float.Parse(a.Substring(0, a.Length - 1), System.Globalization.CultureInfo.InvariantCulture).CompareTo(float.Parse(b.Substring(0, b.Length - 1), System.Globalization.CultureInfo.InvariantCulture)));
            report.Places = string.Join(" ", places);
            forward = distinctForward.Count;
            backward = distinctBackward.Count;
            return forward + backward > 0;
        }

        /// <summary>
        /// Where the car lanes of an edge are across it: the lateral place (m, positive to the right of the edge's direction) of each, in forward for those that run the way the edge does and in backward for those
        /// against it. As TryLanes (the same test of direction, master lanes left out) but every lane is listed; MarkingSnap merges them. Read only; false when the edge has no car lane.
        /// </summary>
        internal static bool TryLaneLaterals(EntityManager em, Entity edge, List<float> forward, List<float> backward)
        {
            // 0.12.7: from the road's cross-section first (where the game puts its lanes); the lanes' own curves only when that cannot be read.
            if (TryCompositionLaterals(em, edge, forward, backward))
            {
                if (s_ComparedLaterals < 12)
                    CompareWithSubLanes(em, edge, forward, backward);
                return true;
            }

            return TrySubLaneLaterals(em, edge, forward, backward);
        }

        private static int s_ComparedLaterals;

        /// <summary>For the log, the first few times: the lanes from the cross-section against those measured from the lanes' curves (0.12.6 and before), when they differ.</summary>
        private static void CompareWithSubLanes(EntityManager em, Entity edge, List<float> forward, List<float> backward)
        {
            var f = new List<float>();
            var b = new List<float>();
            if (!TrySubLaneLaterals(em, edge, f, b))
                return;
            List<float> mf = MarkingSnap.Merged(f), mb = MarkingSnap.Merged(b), cf = MarkingSnap.Merged(forward), cb = MarkingSnap.Merged(backward);
            bool same = mf.Count == cf.Count && mb.Count == cb.Count;
            for (int i = 0; same && i < mf.Count; i++)
                same &= System.Math.Abs(mf[i] - cf[i]) < 0.3f;
            for (int i = 0; same && i < mb.Count; i++)
                same &= System.Math.Abs(mb[i] - cb[i]) < 0.3f;
            if (same)
                return;
            s_ComparedLaterals++;
            string F(List<float> list) => string.Join(", ", list.ConvertAll(v => v.ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture)));
            Mod.log.Info($"Lanes: segment {edge.Index}:{edge.Version} from its cross-section with it [{F(cf)}] against [{F(cb)}]; from the lanes' curves ({f.Count} + {b.Count} lane piece(s)) with it [{F(mf)}] against [{F(mb)}]. The cross-section is used.");
        }

        /// <summary>
        /// The motor lanes of a segment from its cross-section (NetCompositionLane of its composition, 0.12.7): a lane at x from the composition's middle is x / width + 1/2 of the way from the segment's
        /// left side to its right (EdgeGeometry), as the game lays its lanes out (LaneSystem.CreateEdgeLane); its lateral is measured from the segment's curve at the middle of the segment. A lane with
        /// LaneFlags.Invert runs against the segment, a two-way lane both ways. Car lanes only (LaneFlags.Road), not the masters of a group, not cycle lanes. The lanes' own curves (TrySubLaneLaterals)
        /// are split in two halves on a segment that is not one curve, and their ends are moved to meet the lanes of the next segment: measured from them, a road of two lanes each way came out as lanes of
        /// alternating ways at odd places, and three double yellow lines were drawn on it (the owner's screenshot, 21 Oct).
        /// </summary>
        internal static bool TryCompositionLaterals(EntityManager em, Entity edge, List<float> forward, List<float> backward)
        {
            forward.Clear();
            backward.Clear();
            if (!IsLiveEdge(em, edge) || !em.TryGetComponent(edge, out Curve edgeCurve) || !em.TryGetComponent(edge, out Composition composition) || !em.TryGetComponent(edge, out EdgeGeometry geometry)
                || !em.TryGetComponent(composition.m_Edge, out NetCompositionData data) || !em.TryGetBuffer(composition.m_Edge, true, out DynamicBuffer<NetCompositionLane> lanes) || !(data.m_Width > 0.5f))
                return false;
            var edgeBezier = new Bezier3(ToVec(edgeCurve.m_Bezier.a), ToVec(edgeCurve.m_Bezier.b), ToVec(edgeCurve.m_Bezier.c), ToVec(edgeCurve.m_Bezier.d));
            // The middle of the segment's sides: the end of its first half (the start of its second).
            Vec3 left = ToVec(geometry.m_Start.m_Left.d), right = ToVec(geometry.m_Start.m_Right.d);
            if (!left.IsFinite || !right.IsFinite || Vec3.DistanceXZ(left, right) < 0.5f)
                return false;
            for (int i = 0; i < lanes.Length; i++)
            {
                NetCompositionLane lane = lanes[i];
                LaneFlags flags = lane.m_Flags;
                if ((flags & LaneFlags.Road) == 0 || (flags & (LaneFlags.Master | LaneFlags.BicyclesOnly | LaneFlags.Virtual | LaneFlags.Secondary | LaneFlags.Utility)) != 0)
                    continue;
                if (em.TryGetComponent(lane.m_Lane, out NetLaneData laneData) && (laneData.m_Flags & LaneFlags.BicyclesOnly) != 0)
                    continue;
                float lateral = LaneMath.CrossSectionLateral(edgeBezier, left, right, lane.m_Position.x, data.m_Width);
                if ((flags & LaneFlags.Twoway) != 0)
                {
                    forward.Add(lateral);
                    backward.Add(lateral);
                }
                else if ((flags & LaneFlags.Invert) != 0)
                {
                    backward.Add(lateral);
                }
                else
                {
                    forward.Add(lateral);
                }
            }

            return forward.Count + backward.Count > 0;
        }

        /// <summary>
        /// The outer kerb of a segment nearest a lateral place (0.12.33, Kerbs.Outer: where a carriageway meets a footway), as a lateral from the segment's curve measured as CompositionRaised measures, and
        /// how high it is; false when neither side has one or the cross-section cannot be read.
        /// </summary>
        internal static bool CompositionKerb(EntityManager em, Entity edge, float near, out float lateral, out float rise)
        {
            lateral = rise = 0f;
            if (!IsLiveEdge(em, edge) || !em.TryGetComponent(edge, out Curve edgeCurve) || !em.TryGetComponent(edge, out Composition composition) || !em.TryGetComponent(edge, out EdgeGeometry geometry)
                || !em.TryGetComponent(composition.m_Edge, out NetCompositionData data) || !em.TryGetBuffer(composition.m_Edge, true, out DynamicBuffer<NetCompositionPiece> buffer) || !(data.m_Width > 0.5f))
                return false;
            var edgeBezier = new Bezier3(ToVec(edgeCurve.m_Bezier.a), ToVec(edgeCurve.m_Bezier.b), ToVec(edgeCurve.m_Bezier.c), ToVec(edgeCurve.m_Bezier.d));
            Vec3 left = ToVec(geometry.m_Start.m_Left.d), right = ToVec(geometry.m_Start.m_Right.d);
            if (!left.IsFinite || !right.IsFinite || Vec3.DistanceXZ(left, right) < 0.5f)
                return false;
            bool found = false;
            foreach (bool side in new[] { false, true })
            {
                if (!KerbReader.Outer(em, buffer, side, out float x, out float foot, out float top))
                    continue;
                float at = LaneMath.CrossSectionLateral(edgeBezier, left, right, x, data.m_Width);
                if (!found || Math.Abs(at - near) < Math.Abs(lateral - near))
                {
                    lateral = at;
                    rise = top - foot;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// The raised strips between a segment's carriageways (SurfaceSection.Raised: kerbs and medians between its lanes), as laterals from the segment's curve (lo, hi), left to right, measured as
        /// TryCompositionLaterals measures the lanes. Empty when the segment has none or its cross-section cannot be read.
        /// </summary>
        internal static List<(float Lo, float Hi)> CompositionRaised(EntityManager em, Entity edge)
        {
            var result = new List<(float Lo, float Hi)>();
            if (!IsLiveEdge(em, edge) || !em.TryGetComponent(edge, out Curve edgeCurve) || !em.TryGetComponent(edge, out Composition composition) || !em.TryGetComponent(edge, out EdgeGeometry geometry)
                || !em.TryGetComponent(composition.m_Edge, out NetCompositionData data) || !em.TryGetBuffer(composition.m_Edge, true, out DynamicBuffer<NetCompositionPiece> buffer) || !(data.m_Width > 0.5f))
                return result;
            var edgeBezier = new Bezier3(ToVec(edgeCurve.m_Bezier.a), ToVec(edgeCurve.m_Bezier.b), ToVec(edgeCurve.m_Bezier.c), ToVec(edgeCurve.m_Bezier.d));
            Vec3 left = ToVec(geometry.m_Start.m_Left.d), right = ToVec(geometry.m_Start.m_Right.d);
            if (!left.IsFinite || !right.IsFinite || Vec3.DistanceXZ(left, right) < 0.5f)
                return result;
            foreach ((float x0, float x1) in SurfaceSection.Raised(SurfaceReader.Pieces(em, buffer, null)))
            {
                float a = LaneMath.CrossSectionLateral(edgeBezier, left, right, x0, data.m_Width), b = LaneMath.CrossSectionLateral(edgeBezier, left, right, x1, data.m_Width);
                result.Add((Math.Min(a, b), Math.Max(a, b)));
            }

            result.Sort((p, q) => p.Lo.CompareTo(q.Lo));
            return result;
        }

        /// <summary>Whether a node is a junction (three roads or more meet there), not a plain node between two pieces of a road or its end.</summary>
        internal static bool IsJunction(EntityManager em, Entity node)
        {
            if (node == Entity.Null || !em.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges))
                return false;
            int ending = 0;
            for (int i = 0; i < edges.Length; i++)
                ending += EndsAt(em, edges[i].m_Edge, node) ? 1 : 0;
            return ending >= 3;
        }

        /// <summary>
        /// Whether a segment starts or ends at a node (0.12.52). A node's ConnectedEdge also lists the segments it is joined to along their middle (a building's or a path's connection), which the
        /// game's own readers leave out this way; taken as the next piece of a road, the chain went from the node to the far end of such a segment.
        /// </summary>
        internal static bool EndsAt(EntityManager em, Entity edge, Entity node) => em.TryGetComponent(edge, out Edge ends) && (ends.m_Start == node || ends.m_End == node);

        /// <summary>
        /// 0.12.51: where along a segment's curve (m) its drawn area starts and ends at its left and right edges (its EdgeGeometry's corners): at a slanted mouth they differ (ChainPiece.StartLeft...).
        /// False for a segment that is not a live edge or has no geometry.
        /// </summary>
        internal static bool TryDrawnCorners(EntityManager em, Entity edge, ArcTable arc, out float startLeft, out float startRight, out float endLeft, out float endRight)
        {
            startLeft = startRight = endLeft = endRight = float.NaN;
            if (arc == null || !IsLiveEdge(em, edge) || !em.TryGetComponent(edge, out EdgeGeometry geometry))
                return false;
            startLeft = MarkingRuns.ProjectS(arc, ToVec(geometry.m_Start.m_Left.a));
            startRight = MarkingRuns.ProjectS(arc, ToVec(geometry.m_Start.m_Right.a));
            endLeft = MarkingRuns.ProjectS(arc, ToVec(geometry.m_End.m_Left.d));
            endRight = MarkingRuns.ProjectS(arc, ToVec(geometry.m_End.m_Right.d));
            return true;
        }

        /// <summary>
        /// Where along a segment it is drawn (m along its curve, arc): from where its sides start to where they end (EdgeGeometry, which the game trims to the junctions' parts), the part that both sides
        /// have. False when that is under 2 m or the segment has no geometry. 0.12.7: the lines of a segment are kept there, out of the junctions (the lanes' curves cannot tell it on a segment that is
        /// not one curve: each lane is two halves there, the first ending where the second starts).
        /// </summary>
        internal static bool TryDrawnSpan(EntityManager em, Entity edge, ArcTable arc, out float s0, out float s1)
        {
            s0 = 0f;
            s1 = 0f;
            if (arc == null || !IsLiveEdge(em, edge) || !em.TryGetComponent(edge, out EdgeGeometry geometry))
                return false;
            return LaneMath.CommonSpan(MarkingRuns.ProjectS(arc, ToVec(geometry.m_Start.m_Left.a)), MarkingRuns.ProjectS(arc, ToVec(geometry.m_Start.m_Right.a)),
                MarkingRuns.ProjectS(arc, ToVec(geometry.m_End.m_Left.d)), MarkingRuns.ProjectS(arc, ToVec(geometry.m_End.m_Right.d)), 2f, out s0, out s1);
        }

        /// <summary>The motor lanes of a segment measured from the lanes' own curves (what 0.12.6 and before did; now the fallback when the cross-section cannot be read).</summary>
        internal static bool TrySubLaneLaterals(EntityManager em, Entity edge, List<float> forward, List<float> backward)
        {
            forward.Clear();
            backward.Clear();
            if (!IsLiveEdge(em, edge) || !em.TryGetComponent(edge, out Curve edgeCurve) || !em.TryGetBuffer(edge, true, out DynamicBuffer<Game.Net.SubLane> lanes))
                return false;

            var edgeBezier = new Bezier3(ToVec(edgeCurve.m_Bezier.a), ToVec(edgeCurve.m_Bezier.b), ToVec(edgeCurve.m_Bezier.c), ToVec(edgeCurve.m_Bezier.d));
            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (!IsMotorLane(em, lane) || !em.TryGetComponent(lane, out Curve laneCurve))
                    continue;
                var bezier = new Bezier3(ToVec(laneCurve.m_Bezier.a), ToVec(laneCurve.m_Bezier.b), ToVec(laneCurve.m_Bezier.c), ToVec(laneCurve.m_Bezier.d));
                LaneMath.Across(edgeBezier, bezier, out float lateral, out bool with);
                (with ? forward : backward).Add(lateral);
            }

            return forward.Count + backward.Count > 0;
        }

        /// <summary>
        /// Every lane of an edge across it (0.12.1, for its real width): where its middle runs (LaneMath.Across), how wide it is (its prefab's NetLaneData.m_Width) and what it is: on the carriageway (a car
        /// lane, a parking lane, a track: NetLaneData Road, Parking or Track), a footway (Pedestrian), or neither. The master lanes of junction groups, the markings (secondary lanes) and utility lanes are
        /// left out. Read only; false when the edge has none.
        /// </summary>
        internal static bool TryCrossLanes(EntityManager em, Entity edge, List<CrossLane> into)
        {
            into.Clear();
            if (!IsLiveEdge(em, edge) || !em.TryGetComponent(edge, out Curve edgeCurve) || !em.TryGetBuffer(edge, true, out DynamicBuffer<Game.Net.SubLane> lanes))
                return false;
            var edgeBezier = new Bezier3(ToVec(edgeCurve.m_Bezier.a), ToVec(edgeCurve.m_Bezier.b), ToVec(edgeCurve.m_Bezier.c), ToVec(edgeCurve.m_Bezier.d));
            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (lane == Entity.Null || !em.Exists(lane) || em.HasComponent<MasterLane>(lane) || em.HasComponent<Game.Net.SecondaryLane>(lane) || em.HasComponent<Game.Net.UtilityLane>(lane)
                    || !em.TryGetComponent(lane, out Curve laneCurve) || !em.TryGetComponent(lane, out Game.Prefabs.PrefabRef prefab) || !em.TryGetComponent(prefab.m_Prefab, out Game.Prefabs.NetLaneData data))
                    continue;
                Game.Prefabs.LaneFlags flags = data.m_Flags;
                CrossLaneKind kind = (flags & Game.Prefabs.LaneFlags.Pedestrian) != 0 ? CrossLaneKind.Pedestrian
                    : (flags & (Game.Prefabs.LaneFlags.Road | Game.Prefabs.LaneFlags.Parking | Game.Prefabs.LaneFlags.Track)) != 0 ? CrossLaneKind.Carriageway : CrossLaneKind.Other;
                if (kind == CrossLaneKind.Other)
                    continue;
                var bezier = new Bezier3(ToVec(laneCurve.m_Bezier.a), ToVec(laneCurve.m_Bezier.b), ToVec(laneCurve.m_Bezier.c), ToVec(laneCurve.m_Bezier.d));
                LaneMath.Across(edgeBezier, bezier, out float lateral, out _);
                into.Add(new CrossLane(lateral, data.m_Width, kind));
            }

            return into.Count > 0;
        }

        /// <summary>What TryLaneCentres found, for the log: how the lanes of the edge were told apart.</summary>
        internal struct LaneReport
        {
            public int SubLanes, CarLanes, Masters, Facing, Crossed, Nearest, Distinct;

            public override string ToString() =>
                $"{SubLanes} sublane(s), {CarLanes} car lane(s), {Masters} master lane(s) left out, {Facing} facing the sign ({Crossed} cross the beam's plane, {Nearest} taken at their nearest point), {Distinct} distinct";
        }

        /// <summary>
        /// The lanes of an edge that face a sign standing at position and turned by yaw, as the sign-frame X of the middle of each (m from the sign's position, +X toward the driver's left: the
        /// way the sign faces is +Z, the traffic it faces runs against that). Each lane is taken where its curve crosses the plane of the beam (LaneMath.LaneX), the sort and the merging of lanes
        /// within half a metre are LaneMath.Arrange's: from the driver's left to the right, the largest X first. Lanes beside or behind the position (X under 0.3 m) are left out, unless bothSides (the post of a double cantilever, which has an arm on each side). The master lanes
        /// of junctions are not counted. False when the edge cannot be read or has no such lane.
        /// </summary>
        internal static bool TryLaneCentres(EntityManager em, Entity edge, float3 position, float yaw, List<float> centres, out LaneReport report, int arms = 1)
        {
            centres.Clear();
            report = default;
            if (!IsLiveEdge(em, edge) || !em.TryGetBuffer(edge, true, out DynamicBuffer<Game.Net.SubLane> lanes))
                return false;

            Vec3 origin = ToVec(position);
            var xs = new List<float>();
            report.SubLanes = lanes.Length;
            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (lane == Entity.Null || !em.Exists(lane) || !em.HasComponent<Game.Net.CarLane>(lane) || !em.TryGetComponent(lane, out Curve laneCurve) || IsBikeLane(em, lane))
                    continue;
                report.CarLanes++;
                if (em.HasComponent<MasterLane>(lane))
                {
                    report.Masters++;
                    continue;
                }

                var bezier = new Bezier3(ToVec(laneCurve.m_Bezier.a), ToVec(laneCurve.m_Bezier.b), ToVec(laneCurve.m_Bezier.c), ToVec(laneCurve.m_Bezier.d));
                float x = LaneMath.LaneX(bezier, origin, yaw, out float t, out bool crossed);
                if (!LaneMath.FacesSign(bezier, t, yaw))
                    continue;
                report.Facing++;
                if (crossed)
                    report.Crossed++;
                else
                    report.Nearest++;
                xs.Add(x);
            }

            // arms: the way the arm reaches (1: to the driver's left, the lanes of X above 0; -1: to the right, a cantilever from a median, 0.12.25) or both (0: a double cantilever, whose post stands in
            // the traffic; the lanes on its right are the right arm's).
            centres.AddRange(arms > 0 ? LaneMath.Arrange(xs, 0.3f) : arms < 0 ? LaneMath.Arrange(xs, float.NegativeInfinity, -0.3f) : LaneMath.Arrange(xs, float.NegativeInfinity));
            report.Distinct = centres.Count;
            return centres.Count > 0;
        }

        /// <summary>What was read to decide the speed limit of a road, raw, for the log: the figures the game keeps, not the ones we derive.</summary>
        internal struct SpeedReport
        {
            public int CarLanes;

            /// <summary>The highest and the lowest CarLane.m_SpeedLimit among the car lanes (the game's unit; a lane that has none has 0), and the highest m_DefaultSpeedLimit.</summary>
            public float LaneMax, LaneMin, DefaultMax;

            /// <summary>The RoadComposition.m_SpeedLimit of the edge's cross-section (the road's own figure; -1 when it has none) and its Road component's flags.</summary>
            public float Composition;

            /// <summary>Whether the cross-section runs by the game's highway rules (RoadComposition.m_Flags has UseHighwayRules): a motorway or an expressway, not a city street.</summary>
            public bool Highway;

            public override string ToString() =>
                $"{CarLanes} car lane(s): CarLane.m_SpeedLimit {LaneMin:0.###}..{LaneMax:0.###}, m_DefaultSpeedLimit max {DefaultMax:0.###}, RoadComposition.m_SpeedLimit {(Composition < 0f ? "none" : Composition.ToString("0.###"))}, {(Highway ? "highway rules" : "city street rules")}";
        }

        /// <summary>
        /// The speed limit of a road (km/h). The road's own figure (RoadComposition.m_SpeedLimit) is taken when the cross-section has one; else the highest limit of its car lanes. **Both are in
        /// the game's unit, metres per second** (the road prefab states 100 km/h and the game keeps 27.778): the log of 0.4.8 shows the two side by side on a 100 km/h road, 27.778 and 27.778.
        /// 0.4.2 to 0.4.8 took the composition's figure for km/h and so read every road as 28 km/h (the smallest size band); the conversion is SpeedMath's, which is tested. Why 0.3.x and 0.4.0
        /// read 240 and 200 on other roads is not explained by this (those figures were lane limits times 3.6 and so would be 66.7 and 55.6 m/s: the roads' own limits may have been changed by
        /// something else). False when neither figure is there.
        /// </summary>
        internal static bool TrySpeedKmh(EntityManager em, Entity edge, out float kmh) => TrySpeedKmh(em, edge, out kmh, out _);

        /// <summary>Whether the road runs by the game's highway rules (a motorway or an expressway); false for a city street and when the road cannot be read.</summary>
        internal static bool IsHighwayRules(EntityManager em, Entity edge) =>
            IsLiveEdge(em, edge) && em.TryGetComponent(edge, out Composition composition) && composition.m_Edge != Entity.Null && em.TryGetComponent(composition.m_Edge, out RoadComposition road) &&
            (road.m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0;

        internal static bool TrySpeedKmh(EntityManager em, Entity edge, out float kmh, out SpeedReport report)
        {
            kmh = 0f;
            report = new SpeedReport { Composition = -1f, LaneMin = float.MaxValue };
            if (!IsLiveEdge(em, edge))
                return false;

            if (em.TryGetBuffer(edge, true, out DynamicBuffer<Game.Net.SubLane> lanes))
            {
                for (int i = 0; i < lanes.Length; i++)
                {
                    Entity lane = lanes[i].m_SubLane;
                    if (lane == Entity.Null || !em.Exists(lane) || !em.TryGetComponent(lane, out Game.Net.CarLane car))
                        continue;
                    report.CarLanes++;
                    report.LaneMax = math.max(report.LaneMax, car.m_SpeedLimit);
                    report.LaneMin = math.min(report.LaneMin, car.m_SpeedLimit);
                    report.DefaultMax = math.max(report.DefaultMax, car.m_DefaultSpeedLimit);
                }
            }

            if (report.CarLanes == 0)
                report.LaneMin = 0f;

            if (em.TryGetComponent(edge, out Composition composition) && composition.m_Edge != Entity.Null && em.TryGetComponent(composition.m_Edge, out RoadComposition road))
            {
                if (road.m_SpeedLimit > 0f)
                    report.Composition = road.m_SpeedLimit;
                report.Highway = (road.m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0;
            }

            return SpeedMath.TryKmh(report.Composition, report.LaneMax, out kmh);
        }

        /// <summary>
        /// A segment as the ray through its surface needs it (RoadUnderCursor, 0.12.96): its centre line, the width of its cross-section, how far its surface is above the centre line (the composition's
        /// highest surface piece: what the game adds to the centre line's height when it tests a ray), and whether it is covered (a tunnel: under the hill, not under the cursor). False for what is not a
        /// live edge with a composition.
        /// </summary>
        internal static bool TryReadSurface(EntityManager em, Entity edge, out Bezier3 curve, out float width, out float rise, out bool covered)
        {
            rise = 0f;
            covered = false;
            if (!TryRead(em, edge, out curve, out width) || !em.TryGetComponent(edge, out Composition composition) || composition.m_Edge == Entity.Null
                || !em.TryGetComponent(composition.m_Edge, out NetCompositionData data))
                return false;
            rise = data.m_SurfaceHeight.max;
            covered = (data.m_Flags.m_General & CompositionFlags.General.Tunnel) != 0;
            return true;
        }

        /// <summary>
        /// The edge a raycast hit is on. On a plain stretch the owner is the edge; on a junction the owner is a node and the hit entity is the
        /// edge, and a bare node has only its connected edges, of which the nearest to the hit is taken.
        /// </summary>
        internal static bool TryResolveEdge(EntityManager em, Entity owner, Entity hitEntity, float3 hitPosition, out Entity edge)
        {
            edge = Entity.Null;
            if (IsLiveEdge(em, owner))
            {
                edge = owner;
                return true;
            }

            if (IsLiveEdge(em, hitEntity))
            {
                edge = hitEntity;
                return true;
            }

            Entity node = owner != Entity.Null && em.HasComponent<Node>(owner) ? owner : (hitEntity != Entity.Null && em.HasComponent<Node>(hitEntity) ? hitEntity : Entity.Null);
            if (node == Entity.Null || !em.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> connected))
                return false;

            float best = float.MaxValue;
            Vec3 at = ToVec(hitPosition);
            for (int i = 0; i < connected.Length; i++)
            {
                if (!TryRead(em, connected[i].m_Edge, out Bezier3 curve, out _))
                    continue;
                float distance = Vec3.DistanceXZ(curve.Point(curve.ClosestT(at)), at);
                if (distance < best)
                {
                    best = distance;
                    edge = connected[i].m_Edge;
                }
            }

            return edge != Entity.Null;
        }
    }
}
