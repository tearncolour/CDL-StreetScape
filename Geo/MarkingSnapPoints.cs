using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The points of a placed marking that a corner of an island being drawn snaps to (0.12.0): the corners of an area's outline (an island, a gore), the two ends of a line that runs along a road, and the
    /// four corners of the box of a marking of one piece (a stop line's ends, a crosswalk's corners; of a marking bent along the road, 0.12.49, where the bend puts the corners of its straight box). In the
    /// world, at the item's height. An area that clears the game's markings has no paint and gives none. Pure.
    /// </summary>
    public static class MarkingSnapPoints
    {
        /// <summary>How near (m) the cursor must be to one of them for a corner to snap to it.</summary>
        public const float Reach = 0.6f;

        public static List<Vec3> Of(SignDef def, ItemRecord record)
        {
            var points = new List<Vec3>();
            if (def == null || !def.IsMarking || def.Template == null || def.Id == VanillaClear.Id || !record.Position.IsFinite || float.IsNaN(record.Yaw) || float.IsInfinity(record.Yaw))
                return points;
            GuideTemplate template = def.Template;
            GuideSpec spec = template.Parse(record.Spec);
            List<Vec3> local = template.FieldOf(MarkingPolygon.Key) != null ? MarkingPolygon.Decode(spec.Get(MarkingPolygon.Key)) : new List<Vec3>();
            if (local.Count < 3 && MarkingTemplates.IsRun(template))
            {
                List<Vec3> path = RunPath.Decode(spec.Get(RunPath.Key));
                if (path.Count >= 2)
                    local = new List<Vec3> { path[0], path[path.Count - 1] };
            }

            if (local.Count == 0)
            {
                MarkingPlan plan = MarkingTemplates.PlanOf(def, record.Spec);
                if (plan == null || plan.Parts.Count == 0)
                    return points;
                local = plan.Bend != null
                    ? new List<Vec3> { plan.Bend.Map(plan.StraightMinX, plan.StraightMinZ), plan.Bend.Map(plan.StraightMaxX, plan.StraightMinZ), plan.Bend.Map(plan.StraightMaxX, plan.StraightMaxZ), plan.Bend.Map(plan.StraightMinX, plan.StraightMaxZ) }
                    : new List<Vec3> { new Vec3(plan.MinX, 0f, plan.MinZ), new Vec3(plan.MaxX, 0f, plan.MinZ), new Vec3(plan.MaxX, 0f, plan.MaxZ), new Vec3(plan.MinX, 0f, plan.MaxZ) };
            }

            foreach (Vec3 p in MarkingPolygon.ToWorld(local, record.Position, record.Yaw))
                points.Add(new Vec3(p.X, record.Position.Y, p.Z));
            return points;
        }

        /// <summary>The nearest of the points to a place on the ground within reach; false when none is.</summary>
        public static bool Nearest(IEnumerable<Vec3> points, Vec3 at, float reach, out Vec3 nearest)
        {
            nearest = at;
            float best = reach;
            bool found = false;
            if (points == null)
                return false;
            foreach (Vec3 p in points)
            {
                float d = Vec3.DistanceXZ(p, at);
                if (d <= best)
                {
                    best = d;
                    nearest = p;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// The points between two corners of an island that lie on the same line along a road (s0 and s1 along the arc at one lateral place): the side follows the line's curve, a point every 2 m (and at
        /// most maxPoints of them), the corners themselves not included.
        /// </summary>
        public static List<Vec3> Between(ArcTable arc, float s0, float s1, float lateral, float surface, int maxPoints)
        {
            var points = new List<Vec3>();
            if (arc == null || float.IsNaN(s0) || float.IsNaN(s1) || maxPoints <= 0)
                return points;
            float span = Math.Abs(s1 - s0);
            int pieces = Math.Min(maxPoints + 1, (int)Math.Floor(span / 2f));
            if (pieces < 2)
                return points;
            for (int i = 1; i < pieces; i++)
            {
                float s = s0 + (s1 - s0) * i / pieces;
                float t = arc.TAt(s);
                Vec3 p = arc.Curve.Point(t) + arc.Curve.Tangent(t).HeadingXZ().RightXZ() * lateral;
                points.Add(new Vec3(p.X, p.Y + surface, p.Z));
            }

            return points;
        }
    }
}
