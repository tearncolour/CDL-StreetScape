using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;

namespace CDLC.TS
{
    /// <summary>
    /// A line put on what it lies on while it is drawn (0.12.20 barriers, 0.12.32 paint; the owner: "你这个护栏不能吸附路面高度啊", "修复地面表现悬浮问题"): the chain's line runs at the height of the road's centre
    /// line plus the surface where the run was started, and a line by the edge of a crowned road, on a raised median or a footway, or one started on a footway, stood above or under what it lies on. Each
    /// point takes the height of what is under it (StandHeights); the roads are read window by window and kept while the run or the automatic stretch is drawn (SurfaceHeights). The placed markings are
    /// laid again on the ground as it is when they are drawn (AreaFitSystem): the preview shows what they will be.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        private StandHeights m_Stand;

        /// <summary>The line with each point at the height of what it lies on (paint: the carriageway first); key names it in the cache (see SurfaceHeights.Put), endMoves says that its last point follows the cursor.</summary>
        private List<Vec3> OnSurface(List<Vec3> line, string key, bool endMoves, bool paint = false)
        {
            if (line == null || line.Count == 0)
                return line;
            m_Stand = m_Stand ?? new StandHeights(World);
            long lap = System.Diagnostics.Stopwatch.GetTimestamp();
            int reads = m_Stand.Reads;
            // A line drawn along a railway, a metro or a tram line stands on the tracks' surface (0.12.66).
            bool tracks = m_RunActive && m_RunRead != null && m_RunRead.Tracks;
            List<Vec3> result = m_Stand.Put(EntityManager, line, key, endMoves ? line.Count - 1 : line.Count, paint, tracks);
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - lap) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            SlowLog.Add("Tool: the surface under a line", ms, SlowLog.IsSlow(ms) ? $"{line.Count} point(s), {m_Stand.Reads - reads} window(s) read, {m_Stand.KeptWindows} kept" : null);
            return result;
        }

        /// <summary>
        /// The heights of the ground under a marking or a thing put down with one click (0.12.30, GroundGrid; MarkingGround.GridUnder). Null for an item of no size. keep false: the plan is not kept (a
        /// marking bent along the road has other settings at every move of the cursor).
        /// </summary>
        private GroundGrid GroundUnder(SignDef def, string spec, Vec3 position, float yaw, bool keep = true)
        {
            MarkingPlan plan = MarkingTemplates.PlanOf(def, spec, keep);
            if (plan == null || plan.MaxX - plan.MinX < 0.01f || plan.MaxZ - plan.MinZ < 0.01f)
                return null;
            bool paint = def.Group != MarkingTemplates.BarrierGroup;
            return MarkingGround.GridUnder(plan, position, yaw, world => OnSurface(world, "stamp", true, paint));
        }
    }
}
