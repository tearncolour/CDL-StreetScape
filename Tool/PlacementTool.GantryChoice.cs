using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Unity.Mathematics;

namespace CDLC.TS
{
    internal sealed partial class PlacementTool
    {
        /// <summary>
        /// The span a gantry would have at the cursor, for the planner's choice between the portal gantry and the space truss (SupportPlanner.AutoFor, 0.12.78): the road's width between two posts as the
        /// tool will set it, worked out only where the road asks for a gantry at all (three lanes or more, an exit on the left or a wide ramp); 0 when it does not or the road is not known.
        /// </summary>
        private float GantrySpanAt(EdgeInfo info, bool onRoad, float3 cursor, SignDef def, int lanesSame, bool exitLeft)
        {
            if (!onRoad || (lanesSame < 3 && !exitLeft))
                return 0f;
            int side = EdgeSnap.Solve(info.Arc, info.Width, RoadReader.ToVec(cursor), EdgeSnapOptions.Default).Side;
            float inner = SupportLayouts.Of(def, SupportCatalog.ById(SupportCatalog.Gantry)).InnerReach;
            return info.Carriageway(EntityManager).GantrySpan(side, inner, Margin, UnityEngine.Mathf.Max(0f, inner - kReferenceReach));
        }
    }
}
