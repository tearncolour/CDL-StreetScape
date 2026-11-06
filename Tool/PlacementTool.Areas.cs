using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Rendering;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using Bezier3 = CDLC.TS.Snap.Bezier3;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// The area markings in the placement tool. An island (导流线, 6.6) is drawn corner by corner: each click adds a corner (on a road it snaps to the line between two lanes or the road's edge within 0.9 m), a
    /// click on the first corner or Enter closes it and places it, Backspace takes the last corner back, a right click or Esc gives the drawing up. The gore of an exit or an entry (线65) is made from the road:
    /// near a junction where a ramp leaves or joins a carriageway (ForkReader) the gore between the two carriageways' edges is worked out (GoreLayout), with the thick dashed and solid line along the approach
    /// up to its nose, and a click places both; away from such a junction the standard's figure is placed as a stamp. Decoration only.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>A click this near (m) the first corner closes the island.</summary>
        private const float kCloseDistance = 1.0f;

        /// <summary>How near the cursor (m) a junction must be for the gore to be made from it.</summary>
        private const float kForkReach = 60f;

        private readonly List<Vec3> m_PolyPoints = new List<Vec3>();

        /// <summary>
        /// How each point of the outline was placed, in step with m_PolyPoints: Kind 0 where the cursor was, 1 on a point of a placed marking, 2 on a side or the middle of a junction's lane, 3 on a road's lane
        /// line or edge, 255 a point the side between two corners on the same line was given to follow its curve. Line is the lane or the edge, S the distance along it, Lateral the place across it.
        /// </summary>
        private struct CornerSnap
        {
            public byte Kind;
            public Entity Line;
            public float S, Lateral, Surface;
        }

        private readonly List<CornerSnap> m_PolySnaps = new List<CornerSnap>();
        private readonly List<KeyValuePair<Entity, ItemRecord>> m_SnapItems = new List<KeyValuePair<Entity, ItemRecord>>();
        private readonly Dictionary<Entity, KeyValuePair<ItemRecord, List<Vec3>>> m_SnapPoints = new Dictionary<Entity, KeyValuePair<ItemRecord, List<Vec3>>>();

        /// <summary>How far the items are looked for whose points a corner may snap to (m): a piece of a line is at most 100 m long and stands at its start.</summary>
        private const float kSnapSearch = 110f;
        private float m_PolyYaw;
        private Entity m_PolyEdge;
        private float m_PolyEdgeT;
        private string m_PolyKey = "";
        private bool m_PolyFits;

        private ForkRead m_Fork;
        private Entity m_ForkEdge;
        private float m_ForkAt = -100f;
        private string m_ForkKey = "";
        private readonly List<(SignDef Def, string Spec, Vec3 Position, float Yaw)> m_ForkItems = new List<(SignDef, string, Vec3, float)>();
        private GoreSolution m_ForkGore;
        private List<Vec3> m_ForkLead = new List<Vec3>();
        private string m_ForkProblem = "";
        private int m_ForkLogs;
        private Entity m_ForkLoggedNode;

        /// <summary>Whether an island is being drawn (its first corner placed).</summary>
        internal static bool PolygonStarted => Instance != null && Instance.m_PolyPoints.Count > 0;

        private void ResetPolygon()
        {
            m_PolyPoints.Clear();
            m_PolySnaps.Clear();
            if (m_SnapPoints.Count > 2000)
                m_SnapPoints.Clear();
            m_PolyKey = "";
            m_PolyEdge = Entity.Null;
        }

        /// <summary>A right click or Esc while an island is being drawn gives the drawing up and nothing else. True when it did.</summary>
        private bool CancelPolygon()
        {
            if (m_PolyPoints.Count == 0)
                return false;
            Mod.log.Info($"Tool: the island being drawn ({m_PolyPoints.Count} corner(s)) is given up.");
            ResetPolygon();
            HidePreview();
            m_Outcome = "the island being drawn is given up";
            return true;
        }

        // ------------------------------------------------------------------ an island, corner by corner

        /// <summary>
        /// Where a corner goes for the cursor, the first that is near enough: a point of a marking already placed (an end of a line, a corner of an island, of a stop line's box: within 0.6 m); in a junction
        /// the left side, the middle or the right side of its lane nearest the cursor (within 0.9 m); on a road the line between two lanes or the edge (within 0.9 m); else the cursor. At the road's surface.
        /// </summary>
        private Vec3 SnapCorner(Vec3 cursorV, RaycastHit hit, Entity node, EdgeInfo info, bool onRoad, out CornerSnap snap)
        {
            snap = default;
            float ground = hit.m_HitPosition.y;
            if (OwnSnap(cursorV, out Vec3 own))
            {
                snap.Kind = 1;
                return new Vec3(own.X, ground, own.Z);
            }

            if (node != Entity.Null && NearestNodeLane(node, cursorV, out Entity lane, out Bezier3 laneCurve, out float width))
            {
                float lt = laneCurve.ClosestT(cursorV);
                Vec3 on = laneCurve.Point(lt);
                Vec3 across = laneCurve.Tangent(lt).HeadingXZ().RightXZ();
                float laneLateral = Vec3.Dot(cursorV - on, across);
                float best = float.MaxValue, place = 0f;
                foreach (float candidate in new[] { -width * 0.5f, 0f, width * 0.5f })
                {
                    if (Math.Abs(candidate - laneLateral) < best)
                    {
                        best = Math.Abs(candidate - laneLateral);
                        place = candidate;
                    }
                }

                if (best <= MarkingSnap.LineRange)
                {
                    snap = new CornerSnap { Kind = 2, Line = lane, S = new ArcTable(laneCurve).SAt(lt), Lateral = place, Surface = 0f };
                    Vec3 q = on + across * place;
                    return new Vec3(q.X, ground, q.Z);
                }
            }

            if (!onRoad)
                return cursorV;
            Bezier3 curve = info.Curve;
            float t = curve.ClosestT(cursorV);
            Vec3 onCentre = curve.Point(t);
            Vec3 right = curve.Tangent(t).HeadingXZ().RightXZ();
            float lateral = Vec3.Dot(cursorV - onCentre, right);
            info.TryLaterals(EntityManager, out List<float> forward, out List<float> backward);
            MarkingSpot spot = MarkingSnap.Line(forward, backward, lateral);
            float surface = hit.m_HitPosition.y - hit.m_Position.y;
            if (spot.Snapped)
                snap = new CornerSnap { Kind = 3, Line = info.Edge, S = info.Arc.SAt(t), Lateral = spot.Lateral, Surface = surface };
            // Along the road the corner stays where the cursor is; across it, on the line.
            Vec3 along = onCentre + (cursorV - onCentre - right * lateral);
            Vec3 p = along + right * spot.Lateral;
            return new Vec3(p.X, onCentre.Y + surface, p.Z);
        }

        /// <summary>The nearest point of a placed marking to the cursor within MarkingSnapPoints.Reach (each item's points kept until it changes).</summary>
        private bool OwnSnap(Vec3 cursor, out Vec3 point)
        {
            point = cursor;
            ChunkSystem chunks = ChunkSystem.Instance;
            if (chunks == null)
                return false;
            m_SnapItems.Clear();
            chunks.Store.ItemsNear(cursor.X, cursor.Z, kSnapSearch, m_SnapItems);
            float best = MarkingSnapPoints.Reach;
            bool found = false;
            foreach (KeyValuePair<Entity, ItemRecord> pair in m_SnapItems)
            {
                if (!m_SnapPoints.TryGetValue(pair.Key, out KeyValuePair<ItemRecord, List<Vec3>> kept) || kept.Key.Spec != pair.Value.Spec || !kept.Key.Position.Equals(pair.Value.Position) || kept.Key.Yaw != pair.Value.Yaw)
                {
                    kept = new KeyValuePair<ItemRecord, List<Vec3>>(pair.Value, MarkingSnapPoints.Of(SignCatalog.ByHash(pair.Value.TypeHash), pair.Value));
                    m_SnapPoints[pair.Key] = kept;
                }

                if (MarkingSnapPoints.Nearest(kept.Value, cursor, best, out Vec3 near))
                {
                    best = Vec3.DistanceXZ(near, cursor);
                    point = near;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>The arc of the line a snapped corner is on (a road's curve, or a junction lane's), or null.</summary>
        private ArcTable ArcOf(CornerSnap snap)
        {
            if (snap.Line == Entity.Null || !EntityManager.Exists(snap.Line))
                return null;
            if (snap.Kind == 3)
            {
                EdgeInfo info = EdgeInfo.Of(EntityManager, snap.Line);
                return info.CurveOk ? info.Arc : null;
            }

            return snap.Kind == 2 && EntityManager.TryGetComponent(snap.Line, out Game.Net.Curve curve) ? new ArcTable(Bezier(curve)) : null;
        }

        /// <summary>The points that make the side between two corners on the same line follow its curve (none when they are not on one line, or there is no room for them).</summary>
        private List<Vec3> CurvedSide(CornerSnap from, CornerSnap to, int room)
        {
            if (from.Kind < 2 || from.Kind == 255 || from.Kind != to.Kind || from.Line != to.Line || Math.Abs(from.Lateral - to.Lateral) > 0.02f || room <= 0)
                return new List<Vec3>();
            ArcTable arc = ArcOf(to);
            return arc == null ? new List<Vec3>() : MarkingSnapPoints.Between(arc, from.S, to.S, to.Lateral, to.Surface, room);
        }

        /// <summary>The snap of the last corner that was clicked (not of a point that only follows a curve).</summary>
        private CornerSnap LastCornerSnap()
        {
            for (int i = m_PolySnaps.Count - 1; i >= 0; i--)
            {
                if (m_PolySnaps[i].Kind != 255)
                    return m_PolySnaps[i];
            }

            return default;
        }

        /// <summary>Whether a new corner would make the outline drawn so far cross itself (closing: the side back to the first corner).</summary>
        private bool Crosses(Vec3 next, bool closing)
        {
            int n = m_PolyPoints.Count;
            if (n < 2)
                return false;
            Vec3 last = m_PolyPoints[n - 1];
            Vec3 to = closing ? m_PolyPoints[0] : next;
            for (int i = closing ? 1 : 0; i + 1 < n - 1; i++)
            {
                if (Polygon.Segments(last, to, m_PolyPoints[i], m_PolyPoints[i + 1]))
                    return true;
            }

            return false;
        }

        private void UpdatePolygon(SignDef def, GuideTemplate template, bool click, Vec3 cursorV, RaycastHit hit, Entity owner, Entity edge, EdgeInfo info, bool onRoad)
        {
            Keyboard keyboard = Keyboard.current;
            if (!m_Typing && keyboard != null && keyboard[Key.Backspace].wasPressedThisFrame && m_PolyPoints.Count > 0)
            {
                // The last corner, with the points its side was given to follow a curve.
                do
                {
                    m_PolyPoints.RemoveAt(m_PolyPoints.Count - 1);
                    if (m_PolySnaps.Count > m_PolyPoints.Count)
                        m_PolySnaps.RemoveAt(m_PolySnaps.Count - 1);
                }
                while (m_PolyPoints.Count > 0 && m_PolySnaps.Count == m_PolyPoints.Count && m_PolySnaps[m_PolySnaps.Count - 1].Kind == 255);

                m_PolyKey = "";
            }

            bool enter = !m_Typing && keyboard != null && (keyboard[Key.Enter].wasPressedThisFrame || keyboard[Key.NumpadEnter].wasPressedThisFrame);
            Vec3 corner = SnapCorner(cursorV, hit, NodeUnder(owner, hit.m_HitEntity), info, onRoad, out CornerSnap snap);
            bool snapped = snap.Kind != 0;
            bool closing = m_PolyPoints.Count >= 3 && Vec3.DistanceXZ(corner, m_PolyPoints[0]) <= kCloseDistance;
            if (closing)
                corner = m_PolyPoints[0];
            bool full = m_PolyPoints.Count >= MarkingPolygon.MaxPoints;
            bool tooNear = m_PolyPoints.Count > 0 && !closing && Vec3.DistanceXZ(corner, m_PolyPoints[m_PolyPoints.Count - 1]) < 0.3f;
            bool crossing = Crosses(corner, closing);

            // The preview is the island of the corners placed so far, filled once there are three: made again only when a corner is added or taken back (or the settings change), not at every move of the
            // cursor (an island's fill is a millisecond or more); the sides to the cursor and back to the first corner are the overlay's lines.
            if (m_PolyPoints.Count >= 3)
            {
                string key = Draft + "|" + m_PolyPoints.Count;
                if (key != m_PolyKey)
                {
                    m_PolyKey = key;
                    string committed = IslandSpec(template, m_PolyPoints, out m_PolyFits);
                    if (m_PolyFits)
                        ShowStampPreview(def, committed, m_PolyPoints[0], m_PolyYaw);
                    else
                        HidePreview();
                }
                else if (m_Preview != null && m_PolyFits)
                {
                    m_Preview.SetActive(true);
                }
            }
            else
            {
                m_PolyFits = false;
                HidePreview();
            }

            DrawPolygonOverlay(corner, closing, !(crossing || tooNear));
            bool bad = (click && (crossing || tooNear || full)) || ((closing || enter) && m_PolyPoints.Count >= 3 && !m_PolyFits);
            SetHint("mark-poly", m_PolyPoints.Count, 0f, 0f, 0f, closing, snapped, def.Id == VanillaClear.Id ? "clear" : def.Id == MarkingTemplates.GridId ? "grid" : "", bad);
            if (m_WantText)
                Debug = $"island: {m_PolyPoints.Count} corner(s){(closing ? ", the cursor closes it" : "")}{(snapped ? ", on a lane line or the edge" : "")}{(crossing ? ", that corner would cross the outline" : "")}{(full ? ", the most corners" : "")}";

            if ((click && closing) || (enter && m_PolyPoints.Count >= 3))
            {
                // The side back to the first corner follows the line both are on.
                if (m_PolySnaps.Count == m_PolyPoints.Count && m_PolySnaps.Count > 0)
                {
                    foreach (Vec3 p in CurvedSide(LastCornerSnap(), m_PolySnaps[0], MarkingPolygon.MaxPoints - m_PolyPoints.Count))
                    {
                        m_PolyPoints.Add(p);
                        m_PolySnaps.Add(new CornerSnap { Kind = 255 });
                    }
                }

                PlacePolygon(def, template);
                return;
            }

            if (!click)
                return;
            if (crossing || tooNear || full)
            {
                m_Outcome = "refused: " + (crossing ? "the corner would make the outline cross itself" : tooNear ? "the corner is on the one before" : "the most corners; close the island");
                return;
            }

            if (m_PolyPoints.Count == 0)
            {
                // The island's frame: its first corner, +Z the way the traffic runs in the lane there (the V lines point back at it).
                m_PolyYaw = YawAt(corner, info, onRoad);
                m_PolyEdge = onRoad ? edge : Entity.Null;
                m_PolyEdgeT = onRoad ? info.Curve.ClosestT(corner) : 0f;
            }

            // The side from the last corner follows the line both corners are on (the room left is kept for the corner and the closing side).
            if (m_PolySnaps.Count == m_PolyPoints.Count && m_PolyPoints.Count > 0)
            {
                foreach (Vec3 p in CurvedSide(LastCornerSnap(), snap, MarkingPolygon.MaxPoints - m_PolyPoints.Count - 4))
                {
                    m_PolyPoints.Add(p);
                    m_PolySnaps.Add(new CornerSnap { Kind = 255 });
                }
            }

            m_PolyPoints.Add(corner);
            m_PolySnaps.Add(snap);
            m_PolyKey = "";
            m_Outcome = $"corner {m_PolyPoints.Count} of an island at {corner}";
        }

        /// <summary>The way the traffic runs at a place on the road (the lane nearest it), turned by \ and [ ]; 0 off a road.</summary>
        private float YawAt(Vec3 place, EdgeInfo info, bool onRoad)
        {
            float yaw = (m_Flip ? Mathf.PI : 0f) + m_Rotation;
            if (!onRoad)
                return yaw;
            Bezier3 curve = info.Curve;
            float t = curve.ClosestT(place);
            Vec3 onCentre = curve.Point(t);
            Vec3 heading = curve.Tangent(t).HeadingXZ();
            info.TryLaterals(EntityManager, out List<float> forward, out List<float> backward);
            MarkingSpot spot = MarkingSnap.Stamp(MarkingAnchor.Lane, forward, backward, Vec3.Dot(place - onCentre, heading.RightXZ()));
            return yaw + EdgeSnap.YawOf(heading) + (spot.Forward ? 0f : Mathf.PI);
        }

        /// <summary>The settings of an island with an outline (world corners): the draft's, the outline in the frame of its first corner; fits is false when it cannot be written into the settings.</summary>
        private string IslandSpec(GuideTemplate template, List<Vec3> world, out bool fits)
        {
            float yaw = m_PolyPoints.Count > 0 ? m_PolyYaw : 0f;
            List<Vec3> local = MarkingPolygon.ToLocal(world, world[0], yaw);
            string draft = MarkingTemplates.ResolveSize(template, Draft, 0f, false);
            string poly = MarkingPolygon.EncodeFitting(local);
            string spec = MarkingTemplates.WithPolygon(template, draft, poly);
            fits = poly.Length > 0 && template.Parse(spec).Get(MarkingPolygon.Key) == poly && MarkingPolygon.Decode(poly).Count >= 3;
            return spec;
        }

        private void PlacePolygon(SignDef def, GuideTemplate template)
        {
            if (m_PolyPoints.Count < 3)
                return;
            string spec = IslandSpec(template, m_PolyPoints, out bool fits);
            int count = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records.Count : 0;
            if (!fits || count >= kMaxItems)
            {
                m_Outcome = "refused: " + (fits ? "too many signs" : "the outline cannot be written into the settings");
                return;
            }

            Vec3 origin = m_PolyPoints[0];
            if (def.Id == VanillaClear.Id)
            {
                // An area drawn to clear the game's markings takes those of both segments and junctions; it stands at the middle of its corners, so that the tool finds it wherever it is pointed at.
                origin = Centroid(m_PolyPoints);
                origin = new Vec3(origin.X, m_PolyPoints[0].Y, origin.Z);
                spec = ClearElevation.WithOutline(template.Parse(spec).With(VanillaClear.ScopeKey, "a"), m_PolyPoints, origin, m_PolyYaw);
                if (spec == null)
                {
                    m_Outcome = "refused: the outline cannot be written into the settings";
                    return;
                }
            }

            Place(def, spec, 0u, 0f, origin, m_PolyYaw, m_PolyEdge, m_PolyEdgeT, $"an {(def.Id == VanillaClear.Id ? "area that clears the game's markings" : def.Id == MarkingTemplates.GridId ? "area of box grid" : "island")} of {m_PolyPoints.Count} corners");
            m_Outcome = $"placed {(def.Id == MarkingTemplates.GridId ? "a box grid" : "an island")} of {m_PolyPoints.Count} corners";
            Mod.log.Info("Tool: " + m_Outcome + $", settings '{spec}'.");
            ResetPolygon();
            DestroyPreview();
        }

        private void DrawPolygonOverlay(Vec3 cursorCorner, bool closing, bool valid)
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            var line = new UnityEngine.Color(0.2f, 1f, 0.35f, 0.9f);
            var bad = new UnityEngine.Color(1f, 0.2f, 0.15f, 0.9f);
            float3 F(Vec3 p) => new float3(p.X, p.Y + 0.12f, p.Z);
            for (int i = 1; i < m_PolyPoints.Count; i++)
                buffer.DrawLine(line, new Line3.Segment(F(m_PolyPoints[i - 1]), F(m_PolyPoints[i])), 0.12f);
            foreach (Vec3 p in m_PolyPoints)
                buffer.DrawCircle(line, F(p), 0.35f);
            if (m_PolyPoints.Count > 0)
            {
                buffer.DrawLine(valid ? line : bad, new Line3.Segment(F(m_PolyPoints[m_PolyPoints.Count - 1]), F(cursorCorner)), 0.08f);
                // The side the island would close with, from the cursor back to the first corner.
                if (m_PolyPoints.Count >= 2 && !closing)
                    buffer.DrawLine(new UnityEngine.Color(0.2f, 1f, 0.35f, 0.45f), new Line3.Segment(F(cursorCorner), F(m_PolyPoints[0])), 0.05f);
                // The first corner: a larger ring, bright when the cursor would close the island.
                buffer.DrawCircle(closing ? new UnityEngine.Color(0.35f, 0.8f, 1f, 0.95f) : line, F(m_PolyPoints[0]), closing ? 1.1f : 0.7f);
            }

            buffer.DrawCircle(valid ? line : bad, F(cursorCorner), 0.25f);
        }

        // ------------------------------------------------------------------ the gore at a fork

        /// <summary>
        /// The gore of an exit or an entry made from the fork near the cursor: true when there is one (the preview, the overlay and a click are dealt with here); false when there is none, and the standard's
        /// figure is placed as a stamp instead.
        /// </summary>
        private bool UpdateGoreAtFork(SignDef def, GuideTemplate template, bool click, Vec3 cursorV, RaycastHit hit, Entity edge)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            // What is read about the junction is kept for a second (the roads do not change while the mouse moves).
            if (m_Fork == null || edge != m_ForkEdge || now - m_ForkAt > 1f || (m_Fork.Found && Vec3.DistanceXZ(m_Fork.NodePosition, cursorV) > kForkReach))
            {
                m_Fork = ForkReader.Find(EntityManager, edge, cursorV, kForkReach);
                m_ForkEdge = edge;
                m_ForkAt = now;
                if (m_Fork.Report.Length > 0 && m_Fork.Node != m_ForkLoggedNode && m_ForkLogs++ < 12)
                {
                    m_ForkLoggedNode = m_Fork.Node;
                    Mod.log.Info(m_Fork.Report + $" Cursor surface {hit.m_HitPosition.y - hit.m_Position.y:0.00} m above the centre line of the road under it.");
                }
            }

            if (!m_Fork.Found)
            {
                if (m_WantText)
                    m_ForkProblem = m_Fork.Why;
                return false;
            }

            string draft = MarkingTemplates.ResolveSize(template, Draft, 0f, false);
            GuideSpec parsed = template.Parse(draft);
            float width = (float)Math.Max(1.5, Math.Min(12.0, parsed.Number("width", 4.0))), length = (float)Math.Max(8.0, Math.Min(120.0, parsed.Number("len", 30.0)));
            float dashed = (float)Math.Max(0.0, Math.Min(200.0, parsed.Number("dashed", 60.0))), solid = (float)Math.Max(MarkingLines.ThickMinSolid, Math.Min(20.0, parsed.Number("solid", 2.0)));
            string key = $"{m_Fork.Node.Index}:{m_Fork.Node.Version}|{m_Fork.MainEdge.Index}|{m_Fork.RampEdge.Index}|{draft}|{MarkingLetters.Version}";
            if (key != m_ForkKey)
            {
                m_ForkKey = key;
                BuildForkItems(def, template, draft, width, length, dashed, solid, hit);
            }

            // No gore between the roads' edges here: the standard's figure is placed instead.
            if (m_ForkItems.Count == 0)
                return false;
            if (m_Preview == null || m_PreviewKey != "fork|" + key)
            {
                DestroyPreview();
                BuildItemsPreview(m_ForkItems);
                m_PreviewKey = "fork|" + key;
            }

            bool valid = true;
            int count = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records.Count : 0;
            if (valid && count + m_ForkItems.Count > kMaxItems)
                valid = false;
            if (m_Preview != null)
            {
                m_Preview.SetActive(valid);
                m_Preview.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            DrawForkOverlay(valid);
            float leadLength = 0f;
            for (int i = 1; i < m_ForkLead.Count; i++)
                leadLength += Vec3.DistanceXZ(m_ForkLead[i - 1], m_ForkLead[i]);
            SetHint("mark-gore-fork", m_ForkItems.Count, m_ForkGore.Length, m_ForkGore.FarWidth, leadLength, m_Fork.Exit, m_Fork.RampOnRight, "", !valid);
            if (m_WantText)
                Debug = $"{(m_Fork.Exit ? "exit" : "entry")} at junction {m_Fork.Node.Index}: gore {m_ForkGore.Length:0.0} m long, {m_ForkGore.FarWidth:0.0} m at its end{(m_ForkGore.Blunt ? $", blunt nose {m_ForkGore.NoseGap:0.00} m" : "")}, sides from {(m_Fork.FromGeometry ? "the road geometry" : "the centre lines")}, lead {leadLength:0.0} m in {m_ForkItems.Count - 1} piece(s)" + (valid ? "" : " [too many signs]");
            if (!click)
                return true;
            if (!valid)
            {
                m_Outcome = "refused: too many signs";
                return true;
            }

            foreach ((SignDef itemDef, string spec, Vec3 position, float yaw) in m_ForkItems)
                Place(itemDef, spec, 0u, 0f, position, yaw, itemDef == def ? m_Fork.MainEdge : m_Fork.SingleEdge, 0f, itemDef == def ? "the gore of a fork" : "the lead of a gore");
            m_Outcome = $"placed the gore of an {(m_Fork.Exit ? "exit" : "entry")} at junction {m_Fork.Node.Index} and its lead in {m_ForkItems.Count - 1} piece(s)";
            Mod.log.Info("Tool: " + m_Outcome + $"; the gore {m_ForkGore.Length:0.0} m long, {m_ForkGore.FarWidth:0.0} m at its far end, nose {m_ForkGore.Nose}{(m_ForkGore.Blunt ? " (blunt)" : "")}, settings '{m_ForkItems[0].Spec}'.");
            m_ForkKey = "";
            DestroyPreview();
            return true;
        }

        /// <summary>The items of the gore at the fork that was read: the gore (its outline in its own frame) and the pieces of its lead line (the thick dashed line with the solid at the nose).</summary>
        private void BuildForkItems(SignDef def, GuideTemplate template, string draft, float width, float length, float dashed, float solid, RaycastHit hit)
        {
            m_ForkItems.Clear();
            m_ForkLead = new List<Vec3>();
            m_ForkGore = GoreLayout.Solve(m_Fork.MainAway, m_Fork.RampAway, m_Fork.RampSideAway, width, length);
            if (m_ForkGore == null)
            {
                m_ForkProblem = "the two roads' edges do not part into a gore here";
                return;
            }

            // The sides read from the centre lines are at the centre line's height: they are put on the surface as the cursor's road is.
            float surface = m_Fork.FromGeometry ? 0f : hit.m_HitPosition.y - hit.m_Position.y;
            List<Vec3> main = Lift(m_ForkGore.Main, surface), ramp = Lift(m_ForkGore.Ramp, surface);
            Vec3 nose = main[0];
            Vec3 away = (main[1] - main[0]).HeadingXZ();
            float yaw = EdgeSnap.YawOf(m_Fork.Exit ? away : away * -1f);

            // The outline in the gore's frame, as few corners a side as the settings need.
            string spec = null;
            for (int perSide = GoreLayout.MaxSidePoints; perSide >= 3 && spec == null; perSide--)
            {
                List<Vec3> m = MarkingAreas.Resample(main, Math.Min(perSide, main.Count)), r = MarkingAreas.Resample(ramp, Math.Min(perSide, ramp.Count));
                if (!m_ForkGore.Blunt)
                    r[0] = m[0];
                List<Vec3> polygon = MarkingAreas.GorePolygon(m, r);
                List<Vec3> local = MarkingPolygon.ToLocal(polygon, nose, yaw);
                string poly = MarkingPolygon.Encode(local);
                if (poly.Length == 0 || poly.Length > MarkingPolygon.MaxChars)
                    continue;
                // The grade: the far end's height over its distance along the gore.
                Vec3 far = MarkingPolygon.ToLocal(new List<Vec3> { m[m.Count - 1] }, nose, yaw)[0];
                float grade = Math.Abs(far.Z) > 1f ? Mathf.Clamp(far.Y / far.Z, -0.3f, 0.3f) : 0f;
                string candidate = MarkingTemplates.WithGrade(template, template.Parse(draft).With("form", m_Fork.Exit ? "exit" : "entry").ToString(), grade);
                candidate = MarkingTemplates.WithPolygon(template, candidate, poly, m.Count - 1, m_ForkGore.Blunt);
                if (template.Parse(candidate).Get(MarkingPolygon.Key) == poly)
                    spec = candidate;
            }

            if (spec == null)
            {
                m_ForkProblem = "the gore's outline cannot be written into the settings";
                return;
            }

            m_ForkItems.Add((def, spec, nose, yaw));

            // The lead: the thick dashed line and the thick solid one at the nose, on the approach (an exit) or on the road that leaves (an entry), on the ramp's side of the through carriageway's edge (Fig. 94: the
            // 45 cm line lies between the edge's line and the lane that leaves).
            SignDef thick = SignCatalog.ById("mk.line.thick");
            if (thick == null || dashed + solid < 1f)
                return;
            List<Vec3> centre = Lift(m_Fork.SingleCentre, hit.m_HitPosition.y - hit.m_Position.y);
            List<Vec3> lead = m_Fork.Exit ? GoreLayout.LeadBefore(centre, nose, dashed + solid) : GoreLayout.LeadAfter(centre, nose, dashed + solid);
            if (lead.Count < 2)
                return;
            m_ForkLead = MarkingFills.Offset(lead, (m_Fork.RampOnRight ? 1f : -1f) * MarkingLines.ThickWidth * 0.5f);
            float total = 0f;
            for (int i = 1; i < m_ForkLead.Count; i++)
                total += Vec3.DistanceXZ(m_ForkLead[i - 1], m_ForkLead[i]);
            GuideTemplate thickTemplate = thick.Template;
            string thickSpec = thickTemplate.Parse(thickTemplate.Default().ToString()).With("solid", m_Fork.Exit ? "end" : "start").With("sl", solid.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).ToString();
            foreach (RunItem item in MarkingRuns.Items(m_ForkLead, MarkingTemplates.PathBudget(thickTemplate, thickSpec)))
                m_ForkItems.Add((thick, MarkingTemplates.WithRun(thickTemplate, thickSpec, item.Path, item.Start, total), item.Position, item.Yaw));
        }

        private static List<Vec3> Lift(IList<Vec3> line, float y)
        {
            var lifted = new List<Vec3>(line.Count);
            foreach (Vec3 p in line)
                lifted.Add(new Vec3(p.X, p.Y + y, p.Z));
            return lifted;
        }

        private void DrawForkOverlay(bool valid)
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            var edgeColour = new UnityEngine.Color(1f, 0.85f, 0.1f, 0.8f);
            float3 F(Vec3 p) => new float3(p.X, p.Y + 0.15f, p.Z);
            // The two edges that were read, as far as the gore and 20 m on.
            float reach = (m_ForkGore?.Length ?? 30f) + 20f;
            foreach (List<Vec3> side in new[] { m_Fork.MainAway, m_Fork.RampAway })
            {
                float walked = 0f;
                for (int i = 1; i < side.Count && walked < reach + 40f; i++)
                {
                    walked += Vec3.DistanceXZ(side[i - 1], side[i]);
                    buffer.DrawLine(edgeColour, new Line3.Segment(F(side[i - 1]), F(side[i])), 0.08f);
                }
            }

            var ok = valid ? new UnityEngine.Color(0.2f, 1f, 0.35f, 0.9f) : new UnityEngine.Color(1f, 0.2f, 0.15f, 0.9f);
            if (m_ForkGore != null)
                buffer.DrawCircle(ok, F(m_ForkGore.Nose), 0.8f);
            buffer.DrawCircle(new UnityEngine.Color(0.35f, 0.8f, 1f, 0.8f), F(m_Fork.NodePosition), 1.5f);
        }

        /// <summary>The preview of several items at once (a gore and its lead): all their paint in one mesh for each colour, in world coordinates.</summary>
        private void BuildItemsPreview(List<(SignDef Def, string Spec, Vec3 Position, float Yaw)> items)
        {
            m_Preview = new GameObject("CDLTS preview") { hideFlags = HideFlags.DontSave, layer = 0 };
            PreviewFaceKeys = new string[0];
            var materials = new Dictionary<string, MeshBuilder>();
            foreach ((SignDef d, string spec, Vec3 position, float yaw) in items)
            {
                MarkingPlan plan = MarkingTemplates.PlanOf(d, spec, false);
                if (plan == null)
                    continue;
                // The gore as it will be drawn: fitted to the road (0.12.4).
                AreaFit fit = MarkingTemplates.WantsFit(d, spec) ? PreviewFit(d, spec, position, yaw) : null;
                foreach (MarkingPart part in plan.Parts)
                {
                    string material = ChunkBuilder.PartMaterial(part);
                    if (!materials.TryGetValue(material, out MeshBuilder mesh))
                        materials[material] = mesh = new MeshBuilder();
                    mesh.Append(fit != null && !part.Solid.HasValue ? fit.Drape(part.Mesh) : part.Mesh, position, yaw);
                }
            }

            foreach (KeyValuePair<string, MeshBuilder> pair in materials)
                AddPartRenderer(pair.Key, pair.Value);
        }
    }
}
