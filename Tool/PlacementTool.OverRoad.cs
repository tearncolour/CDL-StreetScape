using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// A board on the face of a structure over the road (0.12.81, SupportKind.OverRoad; GB 5768.2 4.8.12 d, Fig. 8 a): with the over-road support chosen, the road under the cursor is found as for any sign
    /// (the tool's ray takes the nets), the structures that cross over it are read from the game's nets (OverRoadReader: elevated compositions, their curve and height range; no mesh), and OverRoad.Find
    /// says which one carries the board, the point on its face above the road, its underside and the height of the face. The board faces the traffic of the road below (its face is the structure's side
    /// toward the oncoming traffic) and stands wholly within the face, low in it (Fig. 8 a), so that every spacer has the structure behind it; a face too low for the board refuses it. The item stands on the face at the road's height and keeps
    /// the height of its lower edge as its reach; it has the road under it as its road (the road's going takes it too), and it does not follow the structure (a bridge that is demolished leaves the
    /// board where it was, as a board on a wall does). The hung variant (0.12.81, SupportDef.Hanger) hangs under the soffit instead: it needs the soffit at least OverRoad.HungNeeded over the road and the structure
    /// OverRoad.HungDepth deep, stands OverRoad.HangSetback in from the face line, and keeps the soffit's height over the road as its reach (the lower edge follows from it, OverRoad.HungLowerEdge).
    /// </summary>
    internal sealed partial class PlacementTool
    {
        private readonly List<OverStructure> m_OverStructures = new List<OverStructure>();
        private Game.Net.SearchSystem m_OverNetSearch;
        private int m_OverLogs;

        /// <summary>The support chosen in the panel is the one over the road.</summary>
        private static bool OverRoadChosen()
        {
            SupportDef support = SupportCatalog.ById(SelectedSupport);
            return support != null && SupportKinds.OverRoad(support.Kind);
        }

        /// <summary>
        /// Place mode with the over-road support: the board on the structure over the road under the cursor (or the reason it cannot go there: no road, no structure over it, no room under it), its
        /// preview, the line of help, and a click places it.
        /// </summary>
        private void UpdatePlaceOverRoad(SignChoice choice, SupportDef chosen, bool click)
        {
            SignDef def = choice.Def;
            m_NearPosts.Clear();
            if (JunctionFill.CanRead(def.Id))
                SetReadNote(null);
            if (!GetRaycastResult(out Entity owner, out RaycastHit hit) || !RoadReader.TryResolveEdge(EntityManager, owner, hit.m_HitEntity, hit.m_HitPosition, out Entity edge))
            {
                HidePreview();
                SetHint("over-none", 0, 0f, 0f, 0f, false, false, chosen.Zh, true);
                m_Outcome = "ignored: no road under the cursor";
                return;
            }

            EdgeInfo info = EdgeInfo.Of(EntityManager, edge);
            if (!info.CurveOk)
            {
                HidePreview();
                SetHint("over-none", 0, 0f, 0f, 0f, false, false, chosen.Zh, true);
                m_Outcome = "ignored: the road under the cursor cannot be read";
                return;
            }

            float3 cursor = hit.m_HitPosition;
            float at = info.Curve.ClosestT(new Vec3(cursor.x, cursor.y, cursor.z), 16);
            Vec3 roadPoint = info.Curve.Point(at);
            m_OverNetSearch = m_OverNetSearch ?? World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            OverRoadReader.Around(EntityManager, m_OverNetSearch, roadPoint.X, roadPoint.Z, roadPoint.Y, edge, m_OverStructures);
            if (OverRoad.Find(m_OverStructures, info.Curve, info.Width, roadPoint.Y, at, out OverSpot spot) < 0)
            {
                HidePreview();
                SetHint("over-none", 0, 0f, 0f, 0f, false, false, chosen.Zh, true);
                m_Outcome = $"ignored: no structure over the road at {roadPoint} ({m_OverStructures.Count} elevated edge(s) near)";
                return;
            }

            // The size band of a sign that says "auto" is the road's under it, as on any road.
            bool hasSpeed = info.TrySpeed(EntityManager, out float kmh, out RoadReader.SpeedReport report);
            string spec = GuideTemplates.ResolveSpeed(def, Draft, hasSpeed ? kmh : 0f, hasSpeed, hasSpeed && report.Highway);
            SignInstance instance = SignInstances.Of(def, spec);
            bool hung = chosen.Hanger;
            OverPlacement plan = OverRoad.Place(spot, roadPoint.Y, instance.Def.Height, instance.Def.ExtraTop, hung);
            if (plan.Refusal != OverRefusal.None)
            {
                HidePreview();
                bool low = plan.Refusal == OverRefusal.TooLow;
                SetHint(low ? "over-low" : "over-thin", 0, plan.Left, plan.Need, 0f, false, hung, chosen.Zh, true);
                m_Outcome = !low ? $"refused: {def.Id} does not hang under the structure (it reaches {plan.Left:0.0} m back from its face, it needs {plan.Need:0.0} m)"
                    : hung ? $"refused: {def.Id} ({instance.Def.Height:0.0} m high) does not hang under the structure ({plan.Left:0.00} m over the road, it needs {plan.Need:0.00} m)"
                    : $"refused: {def.Id} ({instance.Def.Height:0.0} m high) does not stand within the face of the structure ({plan.Left:0.00} m high, it needs {plan.Need:0.00} m)";
                return;
            }

            // The item's reach: the lower edge's height on the face, the soffit's height for a hung board (SupportLayouts: its lower edge follows from it).
            float lower = plan.Lower, reach = plan.Reach;
            Vec3 position = plan.Origin;
            float yaw = OverRoad.Yaw(spot.Face) + (m_Flip ? Mathf.PI : 0f);
            bool valid = true;
            string note = m_WantText ? $"over road {edge.Index}:{edge.Version} t {at:0.00}, structure {spot.Structure}, underside {spot.Underside:0.00} m over the road, face {spot.FaceHeight:0.00} m high, lower edge {lower:0.00} m, face ({spot.Face.X:0.00}, {spot.Face.Z:0.00}), width of the face {Vec3.DistanceXZ(spot.Point, roadPoint):0.0} m off the road's line" : "over";
            var records = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records : null;
            if (records != null && records.Count >= kMaxItems)
            {
                valid = false;
                if (m_WantText)
                    note += "; too many signs";
            }

            if (m_WantText)
                Debug = note + (valid ? "" : " [cannot place]");
            SetHint("over", 0, lower, spot.Underside, 0f, false, hung, chosen.Zh, !valid);
            ShowPreview(instance, chosen.Hash, reach, position, yaw);
            DrawOverlay(true, info.Arc, info.Width, default, position, yaw, valid, def);
            if (!click)
                return;
            if (valid)
            {
                if (m_OverLogs++ < 12)
                    Mod.log.Info($"Over: {note}.");
                Place(def, spec, chosen.Hash, reach, position, yaw, edge, at, note);
                m_Outcome = "placed over a road: " + note;
            }
            else
            {
                m_Outcome = "refused: " + note;
            }
        }
    }
}
