using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Net;
using Game.Rendering;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// Placing an area that clears the game's own markings (mk.clear, VanillaClear, ClearMarkingsSystem): the road segment under the cursor, the whole road it belongs to (its segments and the nodes between
    /// them that no other road meets), the junction under the cursor, or an area drawn corner by corner. The outline is read from the road's surface (EdgeGeometry for a segment, the junction's surface
    /// from its segments' node geometry) and grown by half a metre; a segment longer than 100 m is cleared in pieces, each an item that can be pointed at. With "redraw" a segment or a road is marked by the
    /// automatic marking instead (0.12.46, ClearByAuto). Before 0.12.46 "redraw" placed the standard's lines are placed on each
    /// segment as ordinary markings (StandardLines). Every area already placed is drawn as a red outline while the tool is open for markings, so that it can be found, edited and deleted.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>The longest piece of a segment that is one area (m): an item is found by the tool within 70 m of where it stands.</summary>
        private const float kClearPiece = 100f;

        /// <summary>The most segments of a road that one click clears.</summary>
        private const int kClearMaxEdges = 200;

        private sealed class ClearPiece
        {
            public List<Vec3> Outline;
            public ClearScope Scope;
            public Entity Edge;
            public Vec3 Position;
            public float Yaw;
            public bool Done;
            public bool Strip;

            /// <summary>The automatic marking (0.12.2): an area that keeps the game's crossing lines is here and they are to go now; it is replaced.</summary>
            public bool Upgrade;

            private List<Vec3> m_Drawn;

            /// <summary>The outline as the overlay draws it (OverlayLine).</summary>
            public List<Vec3> Drawn => m_Drawn ?? (m_Drawn = OverlayLine.Simplify(Outline));
        }

        private string m_ClearKey = "";
        private readonly List<ClearPiece> m_ClearPieces = new List<ClearPiece>();
        private readonly List<Entity> m_ClearEdges = new List<Entity>();

        /// <summary>The nodes between two segments of the road that a click on the whole road clears too: the standard's lines run on through them.</summary>
        private readonly HashSet<Entity> m_ClearPassNodes = new HashSet<Entity>();
        private float m_ClearLength;
        private string m_ClearProblem = "";

        private static readonly UnityEngine.Color kClearNew = new UnityEngine.Color(1f, 0.55f, 0.1f, 0.95f);
        private static readonly UnityEngine.Color kClearOld = new UnityEngine.Color(1f, 0.2f, 0.15f, 0.8f);
        private static readonly UnityEngine.Color kClearBad = new UnityEngine.Color(0.6f, 0.6f, 0.6f, 0.8f);

        private void UpdateClear(SignDef def, GuideTemplate template, bool click, Vec3 cursorV, RaycastHit hit, Entity owner, Entity edge, EdgeInfo info, bool onRoad)
        {
            GuideSpec draft = template.Parse(Draft);
            string mode = draft.Get(VanillaClear.ModeKey) ?? "e";
            if (mode == "a")
            {
                UpdatePolygon(def, template, click, cursorV, hit, owner, edge, info, onRoad);
                return;
            }

            if (m_PolyPoints.Count > 0)
                ResetPolygon();
            HidePreview();
            Entity node = mode == "n" ? NodeAt(owner, hit.m_HitEntity, edge, info, onRoad, cursorV) : Entity.Null;
            // 0.12.1: "this segment" with the cursor on a junction clears the junction (the owner clicked a ramp's junction and was told it was cleared already: the segment beside it was).
            Entity under = mode == "e" ? NodeUnder(owner, hit.m_HitEntity) : Entity.Null;
            if (under != Entity.Null)
            {
                mode = "n";
                node = under;
            }

            if (AutoMarking.Offered && draft.Flag(VanillaClear.RedrawKey) && (mode == "e" || mode == "r"))
            {
                ClearByAuto(draft, click, cursorV, hit, edge, info, onRoad);
                return;
            }

            Entity target = mode == "n" ? node : onRoad ? edge : Entity.Null;
            int clearRevision = ClearMarkingsSystem.Instance != null ? ClearMarkingsSystem.Instance.Revision : 0;
            string key = mode + "|" + target.Index + ":" + target.Version + "|" + clearRevision;
            if (key != m_ClearKey)
            {
                m_ClearKey = key;
                ReadClearPieces(mode, target);
            }

            bool keepLines = !draft.Is(VanillaClear.KeepKey, "all");
            int open = 0;
            foreach (ClearPiece piece in m_ClearPieces)
                open += piece.Done ? 0 : 1;
            int count = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records.Count : 0;
            bool valid = open > 0 && count + open <= kMaxItems;
            string note = m_ClearPieces.Count == 0 ? (m_ClearProblem.Length > 0 ? m_ClearProblem : mode == "n" ? "no junction under the cursor" : "no road under the cursor")
                : open == 0 ? "already cleared" : count + open > kMaxItems ? "too many signs" : "";
            if (m_WantText)
                Debug = $"clear ({mode}): {m_ClearPieces.Count} area(s), {open} new, {m_ClearEdges.Count} segment(s), {m_ClearLength:0} m" + (note.Length > 0 ? "; " + note : "") + (valid ? "" : " [cannot place]");
            SetHint("mark-clear", m_ClearPieces.Count, m_ClearLength, 0f, 0f, false, keepLines, mode, !valid && m_ClearPieces.Count > 0);
            DrawClearPieces(valid);
            if (!click)
                return;
            if (!valid)
            {
                m_Outcome = "refused: " + note;
                return;
            }

            PlaceClear(def, template);
        }

        /// <summary>
        /// A segment or a road cleared with "then mark it by the standard" (0.12.46; the owner, on 0.12.45: "这重标好像和之前没区别啊": the redraw had placed the lines along only, dashed up to the
        /// junctions): it is the automatic marking of it (按国标自动重标: the lines along and, at its junctions, the guide lane lines and the arrows by the road's state) with the clear tool's range and
        /// keep. A click that marks a stretch, or chooses one marked before, hands the tool over to that entry with the stretch chosen, so that its settings are in the panel.
        /// </summary>
        private void ClearByAuto(GuideSpec clear, bool click, Vec3 cursorV, RaycastHit hit, Entity edge, EdgeInfo info, bool onRoad)
        {
            SignDef auto = SignCatalog.ById(AutoMarking.Id);
            UpdateAuto(auto, auto.Template, click, cursorV, hit, edge, info, onRoad, AutoMarking.FromClear(auto.Template, clear).ToString());
            if (!click || m_AutoSelected == Entity.Null)
                return;
            SelectedId = AutoMarking.Id;
            EditorTarget++;
            EditorRevision++;
            Mod.log.Info("Tool: the clear tool's \"then mark it by the standard\" handed over to the automatic marking, its stretch chosen.");
        }

        /// <summary>The junction for the cursor: the node it is over, or the end of the segment under it that it is within 30 m of.</summary>
        private Entity NodeAt(Entity owner, Entity hitEntity, Entity edge, EdgeInfo info, bool onRoad, Vec3 cursor)
        {
            if (owner != Entity.Null && EntityManager.HasComponent<Node>(owner))
                return owner;
            if (hitEntity != Entity.Null && EntityManager.HasComponent<Node>(hitEntity))
                return hitEntity;
            if (!onRoad || !EntityManager.TryGetComponent(edge, out Edge ends))
                return Entity.Null;
            Entity best = Entity.Null;
            float bestDistance = 30f;
            foreach (Entity n in new[] { ends.m_Start, ends.m_End })
            {
                if (EntityManager.TryGetComponent(n, out Node data))
                {
                    float d = Vec3.DistanceXZ(RoadReader.ToVec(data.m_Position), cursor);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = n;
                    }
                }
            }

            return best;
        }

        /// <summary>What a click would clear: the areas of the segment, the road or the junction, and which of them are cleared already (an area of the same scope takes the middle of each).</summary>
        private void ReadClearPieces(string mode, Entity target)
        {
            m_ClearPieces.Clear();
            m_ClearEdges.Clear();
            m_ClearPassNodes.Clear();
            m_ClearLength = 0f;
            m_ClearProblem = "";
            if (target == Entity.Null || !EntityManager.Exists(target))
                return;
            if (mode == "n")
            {
                AddNodePiece(target);
                if (m_ClearPieces.Count == 0)
                    m_ClearProblem = "the junction's surface could not be read";
            }
            else
            {
                var edges = mode == "r" ? RoadEdges(target) : new List<Entity> { target };
                foreach (Entity e in edges)
                    AddEdgePieces(e);
                if (mode == "e" && EntityManager.TryGetComponent(target, out Edge own))
                {
                    // 0.12.1: the ends of one segment too, where it only meets the next segment of a road (the game keeps the lines across such a node as the node's): else a stub of the game's lines stays
                    // at each end (the owner's first try, "清除标线没用").
                    foreach (Entity n in new[] { own.m_Start, own.m_End })
                    {
                        if (LiveEdgesAt(n) == 2 && !m_ClearPassNodes.Contains(n))
                        {
                            m_ClearPassNodes.Add(n);
                            AddNodePiece(n);
                        }
                    }
                }
                if (mode == "r")
                {
                    // The nodes between two segments of the road that no other road meets: their markings continue the road's.
                    var nodes = new HashSet<Entity>();
                    var set = new HashSet<Entity>(edges);
                    foreach (Entity e in edges)
                    {
                        if (!EntityManager.TryGetComponent(e, out Edge ends))
                            continue;
                        foreach (Entity n in new[] { ends.m_Start, ends.m_End })
                        {
                            if (!nodes.Contains(n) && PassesThrough(n, set))
                            {
                                nodes.Add(n);
                                m_ClearPassNodes.Add(n);
                                AddNodePiece(n);
                            }
                        }
                    }
                }

                if (m_ClearPieces.Count == 0)
                    m_ClearProblem = "the road's surface could not be read";
            }

            IReadOnlyList<ClearRegion> regions = ClearMarkingsSystem.Instance != null ? ClearMarkingsSystem.Instance.Regions : null;
            foreach (ClearPiece piece in m_ClearPieces)
            {
                if (regions == null)
                    break;
                foreach (ClearRegion region in regions)
                {
                    if ((region.Scope == piece.Scope || region.Scope == ClearScope.All) && region.Contains(piece.Position))
                        piece.Done = true;
                }
            }
        }

        private static Vec3 Centroid(List<Vec3> outline)
        {
            float x = 0f, y = 0f, z = 0f;
            foreach (Vec3 p in outline)
            {
                x += p.X;
                y += p.Y;
                z += p.Z;
            }

            return outline.Count == 0 ? Vec3.Zero : new Vec3(x / outline.Count, y / outline.Count, z / outline.Count);
        }

        /// <summary>The segments of the road a segment belongs to (its aggregate: the road with one name), at most kClearMaxEdges; the segment alone when it has none.</summary>
        private List<Entity> RoadEdges(Entity edge)
        {
            var edges = new List<Entity> { edge };
            if (!EntityManager.TryGetComponent(edge, out Aggregated aggregated) || aggregated.m_Aggregate == Entity.Null || !EntityManager.TryGetBuffer(aggregated.m_Aggregate, true, out DynamicBuffer<AggregateElement> elements))
                return edges;
            for (int i = 0; i < elements.Length && edges.Count < kClearMaxEdges; i++)
            {
                Entity e = elements[i].m_Edge;
                if (e != edge && RoadReader.IsLiveEdge(EntityManager, e) && !EntityManager.HasComponent<Game.Tools.Temp>(e))
                    edges.Add(e);
            }

            return edges;
        }

        /// <summary>How many live road segments meet at a node.</summary>
        private int LiveEdgesAt(Entity node)
        {
            if (!EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> connected))
                return 0;
            int n = 0;
            for (int i = 0; i < connected.Length; i++)
            {
                if (RoadReader.IsLiveEdge(EntityManager, connected[i].m_Edge) && RoadReader.EndsAt(EntityManager, connected[i].m_Edge, node))
                    n++;
            }

            return n;
        }

        /// <summary>Whether a node is cleared with the road: two of its segments meet there and at most one other (VanillaClear.NodeOfRoad).</summary>
        private bool PassesThrough(Entity node, HashSet<Entity> road)
        {
            if (!EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> connected))
                return false;
            int ours = 0, others = 0;
            for (int i = 0; i < connected.Length; i++)
            {
                Entity e = connected[i].m_Edge;
                if (!RoadReader.IsLiveEdge(EntityManager, e) || !RoadReader.EndsAt(EntityManager, e, node))
                    continue;
                if (road.Contains(e))
                    ours++;
                else
                    others++;
            }

            return VanillaClear.NodeOfRoad(ours, others);
        }

        private static void Sample(List<Vec3> into, Bezier4x3 curve, int steps, bool skipFirst)
        {
            for (int i = skipFirst ? 1 : 0; i <= steps; i++)
                into.Add(RoadReader.ToVec(MathUtils.Position(curve, i / (float)steps)));
        }

        /// <summary>The areas of a segment: its surface from one end to the other (EdgeGeometry: two halves, each with a left and a right side), in pieces of at most kClearPiece.</summary>
        private void AddEdgePieces(Entity edge)
        {
            if (!EntityManager.TryGetComponent(edge, out EdgeGeometry geometry))
                return;
            var left = new List<Vec3>();
            var right = new List<Vec3>();
            Sample(left, geometry.m_Start.m_Left, 8, false);
            Sample(left, geometry.m_End.m_Left, 8, true);
            Sample(right, geometry.m_Start.m_Right, 8, false);
            Sample(right, geometry.m_End.m_Right, 8, true);
            float length = 0f;
            for (int i = 1; i < left.Count; i++)
                length += (Vec3.DistanceXZ(left[i - 1], left[i]) + Vec3.DistanceXZ(right[i - 1], right[i])) * 0.5f;
            if (!(length > 0.5f) || !(length < 5000f))
                return;
            int pieces = Math.Max(1, (int)Math.Ceiling(length / kClearPiece));
            const int perPiece = 8;
            List<Vec3> l = MarkingAreas.Resample(left, pieces * perPiece + 1), r = MarkingAreas.Resample(right, pieces * perPiece + 1);
            m_ClearEdges.Add(edge);
            m_ClearLength += length;
            for (int p = 0; p < pieces; p++)
            {
                List<Vec3> pl = l.GetRange(p * perPiece, perPiece + 1), pr = r.GetRange(p * perPiece, perPiece + 1);
                List<Vec3> outline = VanillaClear.SideOutline(pl, pr, VanillaClear.Margin);
                if (outline.Count < 3 || !MarkingFills.IsSimple(outline))
                    continue;
                Vec3 a = (pl[perPiece / 2] + pr[perPiece / 2]) * 0.5f, b = (pl[perPiece / 2 + 1] + pr[perPiece / 2 + 1]) * 0.5f;
                Vec3 heading = (b - a).HeadingXZ();
                m_ClearPieces.Add(new ClearPiece { Outline = outline, Scope = ClearScope.Edges, Edge = edge, Position = a, Yaw = EdgeSnap.YawOf(heading), Strip = true });
            }
        }

        /// <summary>The area of a junction: the hull of its surface (the node geometry of each of its segments at this end), grown by half a metre.</summary>
        private void AddNodePiece(Entity node)
        {
            if (!EntityManager.TryGetComponent(node, out Node data) || !EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> connected))
                return;
            Vec3 centre = RoadReader.ToVec(data.m_Position);
            var points = new List<Vec3>();
            for (int i = 0; i < connected.Length; i++)
            {
                Entity e = connected[i].m_Edge;
                if (!EntityManager.TryGetComponent(e, out Edge ends))
                    continue;
                EdgeNodeGeometry g;
                if (ends.m_Start == node && EntityManager.TryGetComponent(e, out StartNodeGeometry start))
                    g = start.m_Geometry;
                else if (ends.m_End == node && EntityManager.TryGetComponent(e, out EndNodeGeometry end))
                    g = end.m_Geometry;
                else
                    continue;
                foreach (Bezier4x3 curve in new[] { g.m_Left.m_Left, g.m_Left.m_Right, g.m_Right.m_Left, g.m_Right.m_Right, g.m_Middle })
                {
                    for (int k = 0; k <= 4; k++)
                    {
                        Vec3 p = RoadReader.ToVec(MathUtils.Position(curve, k / 4f));
                        if (p.IsFinite && Vec3.DistanceXZ(p, centre) < 150f)
                            points.Add(p);
                    }
                }
            }

            List<Vec3> hull = VanillaClear.Hull(points, 24, VanillaClear.Margin);
            if (hull.Count < 3)
                return;
            m_ClearPieces.Add(new ClearPiece { Outline = hull, Scope = ClearScope.Nodes, Edge = Entity.Null, Position = centre, Yaw = 0f });
        }

        private void PlaceClear(SignDef def, GuideTemplate template)
        {
            GuideSpec draft = template.Parse(Draft);
            int placed = 0, failed = 0;
            foreach (ClearPiece piece in m_ClearPieces)
            {
                if (piece.Done)
                    continue;
                string scope = piece.Scope == ClearScope.Nodes ? "n" : piece.Scope == ClearScope.All ? "a" : "e";
                string spec = ClearElevation.WithOutline(draft.With(VanillaClear.ScopeKey, scope), piece.Outline, piece.Position, piece.Yaw, piece.Strip);
                if (spec == null)
                {
                    failed++;
                    continue;
                }

                float edgeT = 0f;
                if (piece.Edge != Entity.Null)
                {
                    EdgeInfo info = EdgeInfo.Of(EntityManager, piece.Edge);
                    edgeT = info.CurveOk ? info.Curve.ClosestT(piece.Position) : 0f;
                }

                Place(def, spec, 0u, 0f, piece.Position, piece.Yaw, piece.Edge, edgeT, "an area that clears the game's markings");
                piece.Done = true;
                placed++;
            }

            // The pieces stay as they are (marked done) until the cursor moves to another road: the areas just placed are read by the renderer in the next frame.
            m_Outcome = $"cleared the game's markings in {placed} area(s){(failed > 0 ? $" ({failed} outline(s) could not be written)" : "")}";
            Mod.log.Info("Tool: " + m_Outcome + $" over {m_ClearEdges.Count} segment(s), {m_ClearLength:0} m.");
        }

        // ------------------------------------------------------------------ the overlay

        private void DrawClearPieces(bool valid)
        {
            if (m_ClearPieces.Count == 0)
                return;
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            foreach (ClearPiece piece in m_ClearPieces)
                DrawOutline(buffer, piece.Drawn, piece.Done || !valid ? kClearBad : kClearNew, 0.3f);
        }

        private static void DrawOutline(OverlayRenderSystem.Buffer buffer, IList<Vec3> outline, UnityEngine.Color colour, float width)
        {
            for (int i = 0; i < outline.Count; i++)
            {
                Vec3 a = outline[i], b = outline[(i + 1) % outline.Count];
                buffer.DrawLine(colour, new Line3.Segment(new float3(a.X, a.Y + 0.15f, a.Z), new float3(b.X, b.Y + 0.15f, b.Z)), width);
            }
        }

        /// <summary>The areas already placed within 600 m of the camera, as red outlines: drawn while the tool is open for markings (or to edit or delete), so that they can be found.</summary>
        private void DrawClearAreas()
        {
            ClearMarkingsSystem system = ClearMarkingsSystem.Instance;
            Camera camera = m_Camera != null ? m_Camera.activeCamera : null;
            if (system == null || system.Regions.Count == 0 || camera == null)
                return;
            Vector3 eye = camera.transform.position;
            OverlayRenderSystem.Buffer buffer = default;
            bool have = false;
            foreach (ClearRegion region in system.Regions)
            {
                if (region.FromArea || region.MaxX < eye.x - 600f || region.MinX > eye.x + 600f || region.MaxZ < eye.z - 600f || region.MinZ > eye.z + 600f)
                    continue;
                if (!have)
                {
                    buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
                    dependencies.Complete();
                    have = true;
                }

                DrawOutline(buffer, region.Drawn, kClearOld, 0.18f);
            }
        }
    }
}
