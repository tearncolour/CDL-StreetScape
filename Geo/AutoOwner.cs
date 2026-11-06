using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Whose an automatic marking is (the owner, 4 Nov: "地面自动标线，标了前半截之后后边就没了，后边标上前边又没了"). Marking a stretch again takes away what an earlier marking of it placed: the automatic items inside
    /// its areas. A line along the road is one item that starts where its run starts, and a segment marked alone starts its lines at the middle of the node where it meets the next segment (the node's area is
    /// part of both segments' areas), so the start of a line is no sign of which segment it belongs to: each marking took away the lines the next segment starts there (his log: a new 176 m segment "replaced 5
    /// earlier automatic markings", all of the 56 m segment beside it, and marking that one again "replaced 12", its own 7 and 5 of the other's). The middle of a line's path is inside its own segment. Pure.
    /// </summary>
    public static class AutoOwner
    {
        /// <summary>The furthest the middle of an item's path is from its start (m): an item is at most MarkingRuns.MaxItemLength long. The search for the items of a piece of a stretch reaches this far beyond it.</summary>
        public const float Reach = MarkingRuns.MaxItemLength * 0.5f;

        /// <summary>The place that says which stretch an automatic item belongs to: the middle of the path of a line, and the place of a mark (an arrow, a stop line) or a line that has no path.</summary>
        public static Vec3 Where(in ItemRecord record)
        {
            SignDef def = SignCatalog.ByHash(record.TypeHash);
            if (def?.Template == null || string.IsNullOrEmpty(record.Spec))
                return record.Position;
            List<Vec3> local = RunPath.Decode(def.Template.Parse(record.Spec).Get(RunPath.Key));
            if (local.Count < 2)
                return record.Position;
            float total = 0f;
            for (int i = 1; i < local.Count; i++)
                total += Vec3.DistanceXZ(local[i - 1], local[i]);
            float half = total * 0.5f, walked = 0f;
            Vec3 middle = local[local.Count - 1];
            for (int i = 1; i < local.Count; i++)
            {
                float chord = Vec3.DistanceXZ(local[i - 1], local[i]);
                if (walked + chord >= half)
                {
                    float t = chord > 1e-6f ? (half - walked) / chord : 0f;
                    middle = new Vec3(local[i - 1].X + (local[i].X - local[i - 1].X) * t, local[i - 1].Y + (local[i].Y - local[i - 1].Y) * t, local[i - 1].Z + (local[i].Z - local[i - 1].Z) * t);
                    break;
                }

                walked += chord;
            }

            return MarkingPolygon.ToWorld(new List<Vec3> { middle }, record.Position, record.Yaw)[0];
        }

        /// <summary>Whether an automatic item is inside the area of a piece of the stretch being marked (by where it belongs, not by where it starts).</summary>
        public static bool Belongs(in ItemRecord record, ClearRegion area) => area.Contains(Where(record));
    }
}
