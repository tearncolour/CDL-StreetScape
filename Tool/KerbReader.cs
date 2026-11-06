using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// The kerbs of the road upgrade's styles on a road segment (0.12.33, Kerbs): for the segment and for each of its ends at a junction, the composition's side flags say the style of each side
    /// (the junction's composition has the segment's flags: CompositionSelectSystem), its pieces split at the kerbs of their meshes where the outer kerb is (Kerbs.Outer), and the part's curves where
    /// that is in the world (as the game lays its pieces across a part: SurfaceReader). Read only. Main thread.
    /// </summary>
    internal static class KerbReader
    {
        private static Vec3 V(float3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>The outer kerb of one side of a composition (Kerbs.Outer over its pieces split at their kerbs: SurfaceReader.Pieces): x from its middle, the heights in front of it and behind it.</summary>
        internal static bool Outer(EntityManager em, DynamicBuffer<NetCompositionPiece> buffer, bool right, out float x, out float foot, out float top) =>
            Kerbs.Outer(SurfaceReader.Pieces(em, buffer, null), right, out x, out foot, out top);

        /// <summary>Whether a segment has a kerb style on either side (its Upgraded flags).</summary>
        internal static bool Styled(EntityManager em, Entity edge) =>
            em.TryGetComponent(edge, out Upgraded upgraded) && (((uint)upgraded.m_Flags.m_Left | (uint)upgraded.m_Flags.m_Right) & Kerbs.Bits) != 0;

        /// <summary>
        /// The kerb lines of a segment and its two ends; none for one that is gone, has no geometry yet, or is elevated or in a tunnel (no kerb of a footway there). styled false (0.12.49, what a kerb
        /// line snaps to): every outer kerb, of a style or not, on any road.
        /// </summary>
        internal static List<KerbLine> Lines(EntityManager em, Entity edge, bool styled = true)
        {
            var lines = new List<KerbLine>();
            if (!em.Exists(edge) || em.HasComponent<Game.Common.Deleted>(edge) || !em.TryGetComponent(edge, out Composition composition) || !em.TryGetComponent(edge, out EdgeGeometry geometry))
                return lines;
            foreach (bool right in new[] { false, true })
            {
                if (Side(em, composition.m_Edge, right, styled, out KerbStyle style, out float f, out float foot, out float top))
                {
                    var line = new KerbLine { Style = style, Rise = top - foot };
                    AddCurve(line.Foot, geometry.m_Start.m_Left, geometry.m_Start.m_Right, f, foot);
                    AddCurve(line.Foot, geometry.m_End.m_Left, geometry.m_End.m_Right, f, foot);
                    Finish(line, right ? geometry.m_Start.m_Right : geometry.m_Start.m_Left, lines);
                }
            }

            foreach (bool start in new[] { true, false })
            {
                EdgeNodeGeometry g;
                if (start)
                {
                    if (!em.TryGetComponent(edge, out StartNodeGeometry sg))
                        continue;
                    g = sg.m_Geometry;
                }
                else
                {
                    if (!em.TryGetComponent(edge, out EndNodeGeometry eg))
                        continue;
                    g = eg.m_Geometry;
                }

                Entity node = start ? composition.m_StartNode : composition.m_EndNode;
                if (!em.TryGetComponent(node, out NetCompositionData data) || !(data.m_Width > 0.1f))
                    continue;
                foreach (bool right in new[] { false, true })
                {
                    if (!Side(em, node, right, styled, out KerbStyle style, out float f, out float foot, out float top))
                        continue;
                    var line = new KerbLine { Style = style, Rise = top - foot };
                    Segment outer;
                    if (g.m_MiddleRadius > 0f)
                    {
                        // A junction whose middle bends: two pieces one after the other, each the whole width.
                        AddCurve(line.Foot, g.m_Left.m_Left, g.m_Left.m_Right, f, foot);
                        AddCurve(line.Foot, g.m_Right.m_Left, g.m_Right.m_Right, f, foot);
                        outer = g.m_Left;
                    }
                    else
                    {
                        // Two halves side by side, split at the composition's middle (SurfaceReader.NodePart): the kerb is in the half of its side.
                        float split = 0.5f + data.m_MiddleOffset / data.m_Width;
                        if (right ? f <= split : f >= split)
                            continue;
                        Segment half = right ? g.m_Right : g.m_Left;
                        float h = right ? (f - split) / Math.Max(1e-4f, 1f - split) : f / Math.Max(1e-4f, split);
                        AddCurve(line.Foot, half.m_Left, half.m_Right, h, foot);
                        outer = half;
                    }

                    Finish(line, right ? outer.m_Right : outer.m_Left, lines);
                }
            }

            return lines;
        }

        /// <summary>For the log: a segment's side flags (Upgraded) and, on each side of its composition, the style and the kerb found (or the pieces outward of the middle when none was).</summary>
        internal static string Describe(EntityManager em, Entity edge)
        {
            if (!em.TryGetComponent(edge, out Composition composition) || !em.TryGetComponent(composition.m_Edge, out NetCompositionData data)
                || !em.TryGetBuffer(composition.m_Edge, true, out DynamicBuffer<NetCompositionPiece> buffer))
                return $"segment {edge.Index}:{edge.Version}: no composition";
            em.TryGetComponent(edge, out Upgraded upgraded);
            var text = new System.Text.StringBuilder($"segment {edge.Index}:{edge.Version}, flags left {upgraded.m_Flags.m_Left} right {upgraded.m_Flags.m_Right}, width {data.m_Width:0.00} m");
            foreach (bool right in new[] { false, true })
            {
                KerbStyle style = Kerbs.StyleOf((uint)(right ? data.m_Flags.m_Right : data.m_Flags.m_Left));
                text.Append(right ? "; right " : "; left ").Append(style);
                if (style == KerbStyle.None)
                    continue;
                if (Outer(em, buffer, right, out float x, out float foot, out float top))
                {
                    text.Append($": the face at {x:+0.00;-0.00} m, {foot:0.00} to {top:0.00} m");
                    continue;
                }

                text.Append(": no kerb across");
                foreach (SectionPiece p in SurfaceReader.Pieces(em, buffer, em.World.GetOrCreateSystemManaged<PrefabSystem>()))
                {
                    if (right ? p.X > 0f : p.X < 0f)
                        text.Append($" | {p.Name} at {p.X:+0.00;-0.00} w {p.Width:0.00} top {p.Top:0.00}{(p.Surface ? "" : " (no surface)")}{(p.RoadLanes ? " (lanes)" : "")}");
                }
            }

            return text.ToString();
        }

        /// <summary>
        /// The style of one side of a composition and where its outer kerb is (f of the width from the left, the carriageway's and the footway's heights); false without a kerb, and when styled without a
        /// style or on an elevated road or in a tunnel.
        /// </summary>
        private static bool Side(EntityManager em, Entity composition, bool right, bool styled, out KerbStyle style, out float f, out float foot, out float top)
        {
            style = KerbStyle.None;
            f = foot = top = 0f;
            if (composition == Entity.Null || !em.TryGetComponent(composition, out NetCompositionData data) || !(data.m_Width > 0.1f)
                || (styled && (data.m_Flags.m_General & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) != 0)
                || !em.TryGetBuffer(composition, true, out DynamicBuffer<NetCompositionPiece> buffer))
                return false;
            style = Kerbs.StyleOf((uint)(right ? data.m_Flags.m_Right : data.m_Flags.m_Left));
            if ((styled && style == KerbStyle.None) || !Outer(em, buffer, right, out float x, out foot, out top))
                return false;
            f = x / data.m_Width + 0.5f;
            return true;
        }

        /// <summary>Points along a part from its left curve to its right one at f across (the part's own length decides how many), at the carriageway's height; the first is left out when it is where the last one is.</summary>
        private static void AddCurve(List<Vec3> points, Bezier4x3 left, Bezier4x3 right, float f, float foot)
        {
            float length = Math.Max(MathUtils.Length(left.xz), MathUtils.Length(right.xz));
            if (length < 0.05f)
                return;
            int n = Math.Max(2, (int)Math.Ceiling(length / Kerbs.Step) + 1);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Vec3 p = V(math.lerp(MathUtils.Position(left, t), MathUtils.Position(right, t), f)) + new Vec3(0f, foot, 0f);
                if (points.Count > 0 && Vec3.DistanceXZ(points[points.Count - 1], p) < 0.02f)
                    continue;
                points.Add(p);
            }
        }

        /// <summary>The line kept when it is long enough, its footway on the side of the part's outer curve (whose start is across from the line's first point).</summary>
        private static void Finish(KerbLine line, Bezier4x3 outer, List<KerbLine> lines)
        {
            if (line.Foot.Count < 2)
                return;
            Vec3 toOuter = V(outer.a) - line.Foot[0];
            line.Footway = Vec3.Dot((line.Foot[1] - line.Foot[0]).HeadingXZ().RightXZ(), toOuter) >= 0f ? 1 : -1;
            lines.Add(line);
        }
    }
}
