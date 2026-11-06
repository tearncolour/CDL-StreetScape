using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using UnityEngine.InputSystem;

namespace CDLC.TS
{
    internal sealed partial class PlacementTool
    {
        /// <summary>
        /// The next post stands in the road (the "/" key and the button of the support page, the same switch; MidRoad). A setting of the session that stays when another sign is chosen: the button shows
        /// it, and a support that is no post leaves it alone and says so in the line of help.
        /// </summary>
        internal static bool Mid { get; private set; }

        private static int s_MidLogs;

        internal static void SetMid(bool on)
        {
            if (Mid == on)
                return;
            Mid = on;
            if (s_MidLogs++ < 20)
                Mod.log.Info($"Tool: posts in the middle of the road {(on ? "on" : "off")}.");
        }

        private void ReadMidKey(Keyboard keyboard)
        {
            if (keyboard[Key.Slash].wasPressedThisFrame)
                SetMid(!Mid);
        }

        /// <summary>
        /// With the mode on, a sign on posts with the cursor over the carriageway stands where the cursor is across it (MidRoad.Lateral), facing the traffic of the lane nearest to it, instead of at the
        /// road's side. False (nothing changed) when the mode is off, the support is no post, or the cursor is not over the carriageway. A raised strip is TryStrip's, which comes first.
        /// </summary>
        private bool TryMid(EdgeInfo info, Vec3 cursor, SupportDef support, Carriageway carriageway, ref EdgeSnapResult snap, out string note)
        {
            note = "";
            if (!Mid || carriageway == null || !MidRoad.Takes(support.Kind))
                return false;
            Vec3 onCentre = info.Curve.Point(snap.T), heading = snap.Heading, across = heading.RightXZ();
            float cursorLateral = Vec3.Dot(cursor - onCentre, across);
            if (!MidRoad.Inside(cursorLateral, carriageway.Ok, carriageway.Left, carriageway.Right, carriageway.RoadHalf))
                return false;
            float lateral = MidRoad.Lateral(cursorLateral, carriageway.Ok, carriageway.Left, carriageway.Right, carriageway.RoadHalf);
            info.TryLaterals(EntityManager, out List<float> forward, out List<float> backward);
            int traffic = MidRoad.Traffic(lateral, forward, backward);
            Vec3 position = onCentre + across * lateral;
            position.Y = snap.Position.Y;
            snap.Side = traffic;
            snap.Lateral = lateral;
            snap.Position = position;
            snap.Facing = traffic > 0 ? heading * -1f : heading;
            snap.Yaw = EdgeSnap.YawOf(snap.Facing);
            snap.CursorDistance = Vec3.DistanceXZ(position, cursor);
            snap.TooFar = false;
            if (m_WantText)
                note = $", in the road at {lateral:+0.00;-0.00} m from the centre line for the traffic {(traffic > 0 ? "along" : "against")} the road's direction";
            return true;
        }
    }
}
