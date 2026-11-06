using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A sign placed along a line (0.12.69; the owner, looking at the settings of 警46 线形诱导标: "这个应该可以线性摆放"). A template may take two settings, "line" (off by default: one click places one
    /// sign as before) and "gap" (the spacing in metres; empty is automatic), and with the first on the first click sets where the line starts and the second where it ends: a sign of the
    /// kind stands on its own post at every spacing along that side of the road, each as a click at that place would make it (PostSnap). Pure: the reading of the roads is the tool's.
    /// </summary>
    public static class SignLine
    {
        public const string LineKey = "line", GapKey = "gap";

        /// <summary>The least and the most a typed spacing may be (m); the automatic one follows the curve (Spacing).</summary>
        public const float MinGap = 2f, MaxGap = 200f;

        /// <summary>The most signs one line places: the line is cut there (the hint says so).</summary>
        public const int MaxItems = 200;

        /// <summary>A station on a span's end is in it (m).</summary>
        private const float kEdge = 1e-3f;

        /// <summary>Whether the sign is placed along a line with these settings: its template takes the setting, it is shown for them (not every form of a sign makes a line), and it is on.</summary>
        public static bool On(SignDef def, string spec)
        {
            GuideTemplate template = def?.Template;
            if (template == null || !template.Linear)
                return false;
            GuideField field = template.FieldOf(LineKey);
            if (field == null)
                return false;
            GuideSpec parsed = template.Parse(spec);
            return (field.Visible == null || field.Visible(parsed)) && parsed.Flag(LineKey);
        }

        /// <summary>The spacing typed in the settings (m); 0 when it is empty or not a number: automatic.</summary>
        public static float TypedGap(SignDef def, string spec)
        {
            GuideTemplate template = def?.Template;
            if (template == null || template.FieldOf(GapKey) == null)
                return 0f;
            double typed = template.Parse(spec).Number(GapKey, 0.0);
            return double.IsNaN(typed) || typed <= 0.0 ? 0f : (float)typed;
        }

        /// <summary>The settings a sign is placed with: the line settings are the drawing's, not the sign's, and are put back to their defaults (the editor of a placed sign does not show them).</summary>
        public static string Stripped(SignDef def, string spec)
        {
            GuideTemplate template = def?.Template;
            if (template == null || !template.Linear || template.FieldOf(LineKey) == null)
                return spec;
            GuideSpec parsed = template.Parse(spec);
            if (!parsed.Flag(LineKey) && parsed.Get(GapKey).Length == 0)
                return spec;
            return parsed.With(LineKey, "0").With(GapKey, "").ToString();
        }

        /// <summary>The settings with the typed spacing set (the settings the line is drawn with change while it is drawn).</summary>
        public static string WithGap(SignDef def, string spec, string gap) => def?.Template == null || def.Template.FieldOf(GapKey) == null ? spec : def.Template.Parse(spec).With(GapKey, gap).ToString();

        /// <summary>A sign on one of these supports stands on a post of its own beside the road, and so can stand at every spacing of a line; a beam, an arm, a host's pole or a wall cannot.</summary>
        public static bool CanStand(SupportKind kind) => !SupportKinds.Hung(kind) && !SupportKinds.Hosted(kind) && !SupportKinds.OnWall(kind) && !SupportKinds.OnArm(kind) && !SupportKinds.OverRoad(kind);

        /// <summary>
        /// The spacing (m) of a line over a road whose tightest curve has this radius (m; float.MaxValue or less than 0: straight). A typed spacing is kept between MinGap and MaxGap. Automatic: **the
        /// rule of the delineators, borrowed** (MarkingStuds.DelineatorSpacing: GB 5768.3-2025 Table 7 from the radius, 30 m on a straight); GB 5768.2 gives no spacing for 警46 that is in this
        /// repository, and nothing here says this is the standard's for it.
        /// </summary>
        public static float Spacing(float typed, float minRadius)
        {
            float gap = typed > 0f ? typed : MarkingStuds.DelineatorSpacing(minRadius, MarkingStuds.StraightOther);
            return Math.Max(MinGap, Math.Min(MaxGap, gap));
        }

        /// <summary>
        /// The distances along the road (s) at which a sign of the line stands: the start, then every spacing toward the end (either way), as far as the end; those that are not in one of the
        /// stretches the road allows (spans: a junction's mouth, a crosswalk are left free) are left out, and the grid goes on over them. At most max; capped says the line was cut there.
        /// </summary>
        public static List<float> Stations(List<ChainSpan> spans, float start, float end, float spacing, int max, out bool capped)
        {
            capped = false;
            var stations = new List<float>();
            if (spans == null || spans.Count == 0 || !(spacing > 0f) || float.IsNaN(start) || float.IsNaN(end))
                return stations;
            float direction = end >= start ? 1f : -1f, length = Math.Abs(end - start);
            List<ChainSpan> parts = RoadChain.Clip(spans, start, end);
            // A line shorter than a metre has no part to be clipped to: its start alone, when that is on the road.
            for (int k = 0; ; k++)
            {
                float along = k * spacing;
                if (along > length + kEdge)
                    break;
                float s = start + direction * along;
                if (!InAny(length < MarkingRuns.MinLength ? spans : parts, s))
                    continue;
                if (stations.Count >= max)
                {
                    capped = true;
                    break;
                }

                stations.Add(s);
            }

            return stations;
        }

        private static bool InAny(List<ChainSpan> parts, float s)
        {
            foreach (ChainSpan part in parts)
            {
                if (s >= Math.Min(part.From, part.To) - kEdge && s <= Math.Max(part.From, part.To) + kEdge)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Where a sign stands beside a road's edge and which way it faces, as the tool's click makes it (PlacementTool.UpdatePlace uses this too, so that a place of a line is the place of a click): on the
        /// side of the edge's direction roadSide (+1 right, -1 left), postDistance from its centre line (Carriageway.PostDistance), facing the traffic that comes to it; its height is the centre line's
        /// plus surface (the road's surface above it). cursor is a point the nearest place on the centre curve is found from.
        /// </summary>
        public static EdgeSnapResult PostSnap(ArcTable arc, float roadWidth, Vec3 cursor, int roadSide, float postDistance, float surface)
        {
            var options = EdgeSnapOptions.Default;
            options.Side = roadSide > 0 ? SnapSide.Right : SnapSide.Left;
            options.Margin = postDistance - roadWidth * 0.5f;
            EdgeSnapResult snap = EdgeSnap.Solve(arc, roadWidth, cursor, options);
            Vec3 position = snap.Position;
            position.Y = arc.Curve.Point(snap.T).Y + surface;
            snap.Position = position;
            return snap;
        }

        /// <summary>The side of a piece's edge a line on the chain's side is on: the chain's right is the edge's left where the piece runs against its edge.</summary>
        public static int EdgeSide(int chainSide, bool pieceReversed) => pieceReversed ? -chainSide : chainSide;
    }
}
