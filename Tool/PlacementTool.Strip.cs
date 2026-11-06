using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Unity.Entities;

namespace CDLC.TS
{
    internal sealed partial class PlacementTool
    {
        /// <summary>The barriers are looked for within this of the post (m): a barrier's piece is pointed at from as far (kPickRadius).</summary>
        private const float kStripSearch = kPickRadius;

        private readonly List<KeyValuePair<Entity, ItemRecord>> m_StripNear = new List<KeyValuePair<Entity, ItemRecord>>();
        private readonly List<(float Lo, float Hi)> m_StripBlocked = new List<(float Lo, float Hi)>();

        private Entity m_StripLogEdge;
        private int m_StripLogs;

        /// <summary>The strip the sign stands on in this frame (laterals; NaN: at the road's side), for the overlay.</summary>
        private float m_StripLo = float.NaN, m_StripHi = float.NaN;

        /// <summary>
        /// A sign on posts of its own or on a cantilever with the cursor over a raised strip between the carriageways stands on the strip (StripPost; posts 0.12.24, cantilevers 0.12.25), not at the
        /// road's side: snap is moved there, facing the traffic beside it on the cursor's side, the posts clear of the barriers on the strip. The planner's two or three posts that do not fit the
        /// strip become one (saved with the item). A cantilever's arm reaches over that traffic's lanes: to the driver's left, or to the right with its mirror; reach is made as long as that needs.
        /// False when the cursor is over no strip, or the support stands on neither.
        /// </summary>
        private bool TryStrip(EdgeInfo info, Entity edge, Vec3 cursor, SignInstance instance, ref SupportDef support, ref uint supportHash, ref float reach, ref EdgeSnapResult snap, out string note)
        {
            note = "";
            bool arm = SupportKinds.OneWayArm(support.Kind);
            if (!arm && support.Kind != SupportKind.SinglePole && support.Kind != SupportKind.DoublePole && support.Kind != SupportKind.TriplePole)
                return false;
            Bezier3 curve = info.Curve;
            Vec3 onCentre = curve.Point(snap.T), heading = snap.Heading, across = heading.RightXZ();
            float cursorLateral = Vec3.Dot(cursor - onCentre, across);
            List<(float Lo, float Hi)> raised = RoadReader.CompositionRaised(EntityManager, edge);
            // What the cross-section says, once a road (the first 30): what a "it does not stand on the median" is checked against.
            if (edge != m_StripLogEdge && m_StripLogs++ < 30)
            {
                m_StripLogEdge = edge;
                Mod.log.Info($"Raised strips: edge {edge.Index}:{edge.Version}: {(raised.Count == 0 ? "none" : string.Join(", ", raised.ConvertAll(r => $"{r.Lo:+0.0;-0.0} to {r.Hi:+0.0;-0.0} m")))} from the centre line.");
            }

            if (!StripPost.Under(raised, cursorLateral, out float lo, out float hi))
                return false;

            BarriersAcross(onCentre, heading, across);
            SupportLayout layout = SupportLayouts.Of(instance.Def, support);
            // A cantilever's board is over the road, not over the strip; its post stands on its base plate (0.6 to 0.8 m across the road).
            float lateral = arm ? StripPost.Across(lo, hi, cursorLateral, 0f, layout.PoleX, layout.Base.HalfX, m_StripBlocked, out bool clear)
                : StripPost.Across(lo, hi, cursorLateral, instance.Def.Width * 0.5f, layout.PoleX, layout.PoleRadius, m_StripBlocked, out clear);
            if (!clear && supportHash == 0u && support.Kind != SupportKind.SinglePole)
            {
                SupportDef single = SupportCatalog.ById(SupportCatalog.SinglePole);
                SupportLayout one = SupportLayouts.Of(instance.Def, single);
                float alone = StripPost.Across(lo, hi, cursorLateral, instance.Def.Width * 0.5f, one.PoleX, one.PoleRadius, m_StripBlocked, out bool fits);
                if (fits)
                {
                    support = single;
                    supportHash = single.Hash;
                    lateral = alone;
                    clear = true;
                }
            }

            info.TryLaterals(EntityManager, out List<float> forward, out List<float> backward);
            int traffic = StripPost.Traffic(lo, hi, cursorLateral, forward, backward, out int lanesSide);
            if (arm)
            {
                support = SupportCatalog.Facing(support, StripPost.ArmToRight(traffic, lanesSide));
                supportHash = support.Hash;
                float length = Carriageway.CantileverArm(lateral, StripPost.Beyond(traffic > 0 ? forward : backward, lateral, lanesSide), instance.Def.Width);
                if (length > 0f)
                    reach = length;
            }

            Vec3 position = onCentre + across * lateral;
            position.Y = snap.Position.Y;
            snap.Side = traffic;
            snap.Lateral = lateral;
            snap.Position = position;
            snap.Facing = traffic > 0 ? heading * -1f : heading;
            snap.Yaw = EdgeSnap.YawOf(snap.Facing);
            snap.CursorDistance = Vec3.DistanceXZ(position, cursor);
            m_StripLo = lo;
            m_StripHi = hi;
            if (m_WantText)
                note = $", on the raised strip {lo:+0.00;-0.00} to {hi:+0.00;-0.00} m at {lateral:+0.00;-0.00} m for the traffic {(traffic > 0 ? "along" : "against")} the road's direction"
                    + (arm ? $", the arm {reach:0.0} m to the driver's {(support.Side < 0 ? "right" : "left")}" : "")
                    + (m_StripBlocked.Count > 0 ? $", {m_StripBlocked.Count} barrier part(s) on it{(clear ? ", the posts beside them" : ", no room beside them")}" : "");
            return true;
        }

        /// <summary>m_StripBlocked: the laterals the barriers near the post take at its place along the road (their pick boxes, a metre along either way).</summary>
        private void BarriersAcross(Vec3 onCentre, Vec3 heading, Vec3 across)
        {
            m_StripBlocked.Clear();
            m_StripNear.Clear();
            if (ChunkSystem.Instance == null)
                return;
            ChunkSystem.Instance.Store.ItemsNear(onCentre.X, onCentre.Z, kStripSearch, m_StripNear);
            foreach (KeyValuePair<Entity, ItemRecord> pair in m_StripNear)
            {
                SignDef def = SignCatalog.ByHash(pair.Value.TypeHash);
                if (def == null || def.Group != MarkingTemplates.BarrierGroup)
                    continue;
                SignParts parts = ChunkBuilder.PartsOfRecord(pair.Value);
                if (parts == null)
                    continue;
                float s = (float)Math.Sin(pair.Value.Yaw), c = (float)Math.Cos(pair.Value.Yaw);
                foreach (PickShape box in parts.Picks)
                {
                    float along0 = float.MaxValue, along1 = float.MinValue, lat0 = float.MaxValue, lat1 = float.MinValue;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        // The item's frame to the world (local +Z is world (sin, 0, cos)), then to the road's at the post.
                        float x = (corner & 1) == 0 ? box.Min.X : box.Max.X, z = (corner & 2) == 0 ? box.Min.Z : box.Max.Z;
                        var world = new Vec3(pair.Value.Position.X + x * c + z * s, 0f, pair.Value.Position.Z - x * s + z * c);
                        Vec3 d = world - onCentre;
                        float along = d.X * heading.X + d.Z * heading.Z, lateral = d.X * across.X + d.Z * across.Z;
                        along0 = Math.Min(along0, along);
                        along1 = Math.Max(along1, along);
                        lat0 = Math.Min(lat0, lateral);
                        lat1 = Math.Max(lat1, lateral);
                    }

                    if (along1 >= -1f && along0 <= 1f)
                        m_StripBlocked.Add((lat0, lat1));
                }
            }
        }
    }
}
