using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The kerb of a road's side as the road upgrade sets it (0.12.33, route A of scratch/design/RESEARCH-kerbs-upgrades.md; the owner: "通过道路升级修改路肩，加入国标各种路肩样式"): two of the game's
    /// side flags no part of the game reads (CompositionFlags.Side.TertiaryTrack 0x40, QuaternaryTrack 0x80), which reach the junction ends with the segment's, say which of three.
    /// </summary>
    public enum KerbStyle
    {
        /// <summary>The game's own kerb.</summary>
        None = 0,

        /// <summary>立缘石: an upright granite kerb, the stones 1 m long (CJJ 37-2012 (2016) 5.5.2: exposed 10 to 15 cm by a footway; the game's step is 20 cm and stays).</summary>
        Raised = 1,

        /// <summary>
        /// 平缘石 (CJJ 37 5.5.3, CJJ 193 5.5.6: level with the footway, by a green strip and where it must be crossed without a step). The game's 20 cm step cannot be taken away by drawing (the road's
        /// own pieces would have to change, route C): until then it is drawn as a granite slope from the carriageway up to the footway over 0.35 m, which hides the step.
        /// </summary>
        Flush = 2,

        /// <summary>立缘石 with 平石: the upright kerb with a 0.3 m gutter of flat stones on the carriageway in front of it (CJJ 169 9.2.5: a gutter within 0.3 m of the carriageway's edge).</summary>
        Gutter = 3
    }

    /// <summary>The kerb of one side of a part of a road (a segment's half, a junction's part): its foot line in the world and how high it rises.</summary>
    public sealed class KerbLine
    {
        public readonly List<Vec3> Foot = new List<Vec3>();
        public float Rise;
        public KerbStyle Style;

        /// <summary>+1: the footway is on the right of the way the foot line runs; -1: on its left.</summary>
        public int Footway;

        /// <summary>Where the line starts along its kerb (m), for the joints' place in the stones' texture.</summary>
        public float Along;
    }

    /// <summary>The kerbs of the road upgrade's styles: where they are in a cross-section and the bodies drawn for them (decoration over the game's own kerb). Sizes this mod's estimates where no clause gives them. Pure.</summary>
    public static class Kerbs
    {
        /// <summary>The two side flags (CompositionFlags.Side): bit 0x40 (TertiaryTrack) is the style's low bit, 0x80 (QuaternaryTrack) its high bit.</summary>
        public const uint LowBit = 0x40u, HighBit = 0x80u, Bits = LowBit | HighBit;

        /// <summary>The styles an upgrade sets, in the order of their buttons.</summary>
        public static readonly KerbStyle[] Styles = { KerbStyle.Raised, KerbStyle.Flush, KerbStyle.Gutter };

        /// <summary>How far apart the foot line is sampled along a curve (m).</summary>
        public const float Step = 1f;

        /// <summary>How far (m) a point of a kerb line is moved onto its kerb (SnapToFeet): more than a line at one lateral is off the kerb on a curve or where the road gets wider at a node.</summary>
        public const float SnapReach = 4f;

        /// <summary>
        /// A kerb line on its kerb (0.12.49): each point moved to the nearest point (in the ground plane) of the foot lines of the kerbs on its side within reach, then outward (m) towards the footway, its
        /// height kept (the line is laid on the ground after); a point with no foot within reach stays. Points that come to lie on one another are dropped. A line at one lateral from the road's centre
        /// line is off the kerb where the game's geometry bends otherwise (a curve, a node, another width).
        /// </summary>
        public static List<Vec3> SnapToFeet(IList<Vec3> line, IList<KerbLine> feet, float outward, float reach)
        {
            var snapped = new List<Vec3>(line.Count);
            foreach (Vec3 p in line)
            {
                Vec3 best = p;
                float nearest = reach * reach;
                foreach (KerbLine kerb in feet)
                {
                    List<Vec3> foot = kerb.Foot;
                    for (int i = 1; i < foot.Count; i++)
                    {
                        Vec3 a = foot[i - 1], b = foot[i];
                        float dx = b.X - a.X, dz = b.Z - a.Z, length2 = dx * dx + dz * dz;
                        if (length2 < 1e-8f)
                            continue;
                        float t = Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / length2));
                        float x = a.X + dx * t, z = a.Z + dz * t, d2 = (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z);
                        if (d2 >= nearest)
                            continue;
                        nearest = d2;
                        Vec3 away = (b - a).HeadingXZ().RightXZ() * (kerb.Footway * outward);
                        best = new Vec3(x + away.X, p.Y, z + away.Z);
                    }
                }

                if (snapped.Count == 0 || Vec3.DistanceXZ(snapped[snapped.Count - 1], best) >= 0.05f)
                    snapped.Add(best);
            }

            return snapped;
        }

        /// <summary>
        /// The kerb stone's top (m, into the footway from the face: as wide as the game's own kerb stone, 0.2 m in its footway pieces, which it covers), the chamfer of its edge, how far its face stands
        /// in front of the game's (so the game's face does not show through), and how far over the footway its top is.
        /// </summary>
        public const float TopWidth = 0.2f, Chamfer = 0.02f, Proud = 0.005f, Over = 0.004f;

        /// <summary>The gutter's width (CJJ 169 9.2.5: within 0.3 m) and the slope of the flush style (m across).</summary>
        public const float GutterWidth = 0.3f, SlopeWidth = 0.35f;

        /// <summary>The smallest step (m) between the carriageway and the footway that is a kerb.</summary>
        public const float MinRise = 0.05f;

        public static KerbStyle StyleOf(uint sideFlags) => (KerbStyle)(((sideFlags & LowBit) != 0 ? 1 : 0) | ((sideFlags & HighBit) != 0 ? 2 : 0));

        /// <summary>The side flags of a style (what its upgrade sets) and the ones it clears.</summary>
        public static uint SetBits(KerbStyle style) => (((int)style & 1) != 0 ? LowBit : 0u) | (((int)style & 2) != 0 ? HighBit : 0u);

        public static uint ClearBits(KerbStyle style) => Bits & ~SetBits(style);

        /// <summary>The side flags as they are now with the style bits of an earlier copy put back: what a save took out of a road is given back without undoing a change made to its other flags meanwhile.</summary>
        public static uint WithStyleOf(uint current, uint copy) => (current & ~Bits) | (copy & Bits);

        /// <summary>
        /// The outer kerb of one side of a cross-section (right: +X, the composition's right): outward of the last piece of the carriageway (a surface with road lanes), past the pieces at its level
        /// without lanes (the strip in front of a footway's kerb: Split), the first surface piece higher by MinRise or more (a footway); the face is where the two meet (half way when they leave a
        /// sliver between them, which the game's mesh closes with the face). x (m from the composition's middle), the heights of the strip in front of it and of the footway over the road's. False when
        /// that side has none (a shoulder, a wall, a bridge's parapet) or its kerb is not straight (a bus bay's: Uneven).
        /// </summary>
        public static bool Outer(IList<SectionPiece> pieces, bool right, out float x, out float foot, out float top)
        {
            x = foot = top = 0f;
            if (pieces == null || pieces.Count == 0)
                return false;
            var sorted = new List<SectionPiece>();
            foreach (SectionPiece p in pieces)
            {
                if (p.Surface && !p.Layer && !p.OtherLevel && p.Width > 0.01f)
                    sorted.Add(p);
            }

            sorted.Sort((a, b) => right ? a.X.CompareTo(b.X) : b.X.CompareTo(a.X));
            int last = -1;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].RoadLanes)
                    last = i;
            }

            if (last < 0)
                return false;
            float level = sorted[last].Top;
            int k = last + 1;
            while (k < sorted.Count && sorted[k].Top - level < MinRise)
                k++;
            if (k >= sorted.Count || sorted[k].Uneven)
                return false;
            SectionPiece before = sorted[k - 1], next = sorted[k];
            float beforeEdge = right ? before.X + before.Width * 0.5f : before.X - before.Width * 0.5f, nextEdge = right ? next.X - next.Width * 0.5f : next.X + next.Width * 0.5f;
            x = (beforeEdge + nextEdge) * 0.5f;
            foot = before.Top;
            top = next.Top;
            return true;
        }

        /// <summary>
        /// The kerbs inside a piece's mesh (0.12.37): walked in from its left edge (lowest x) and from its right edge, the face where each rises by MinRise or more (Face), the heights in front of each
        /// and behind them. Uneven when a face is there but not straight along the piece (a bus bay's).
        /// </summary>
        public struct PieceFaces
        {
            public bool Left, Right, Uneven;
            public float LeftX, RightX, LeftFoot, RightFoot, Top;

            /// <summary>The faces of a piece's own mesh where the game lays it in its composition: x mirrored unless its section is inverted (BatchMeshHelpers), moved by the piece's offset.</summary>
            public PieceFaces Placed(bool inverted, float offsetX, float offsetY)
            {
                PieceFaces f = this;
                if (!inverted)
                {
                    f.Left = Right;
                    f.Right = Left;
                    f.LeftX = -RightX;
                    f.RightX = -LeftX;
                    f.LeftFoot = RightFoot;
                    f.RightFoot = LeftFoot;
                }

                f.LeftX += offsetX;
                f.RightX += offsetX;
                f.LeftFoot += offsetY;
                f.RightFoot += offsetY;
                f.Top += offsetY;
                return f;
            }
        }

        public static PieceFaces FacesOf(IList<Vec3> vertices)
        {
            var faces = new PieceFaces();
            if (vertices == null || vertices.Count == 0)
                return faces;
            bool left = Face(vertices, true, out float lx, out float lf, out float lt, out bool leftUneven);
            bool right = Face(vertices, false, out float rx, out float rf, out float rt, out bool rightUneven);
            faces.Uneven = leftUneven || rightUneven;
            if (faces.Uneven)
                return faces;
            faces.Left = left;
            faces.Right = right && (!left || rx > lx + 0.01f);
            faces.LeftX = lx;
            faces.LeftFoot = lf;
            faces.RightX = rx;
            faces.RightFoot = rf;
            faces.Top = Math.Max(left ? lt : float.MinValue, faces.Right ? rt : float.MinValue);
            return faces;
        }

        /// <summary>
        /// A piece of a cross-section as it is across (faces: its mesh's kerbs, in the composition): the strip at the lower level in front of each face and the higher part between, each a piece of its
        /// own; the piece as it is when its mesh has no kerb (Uneven when it has one that is not straight).
        /// </summary>
        public static void Split(SectionPiece whole, PieceFaces faces, List<SectionPiece> into)
        {
            whole.Uneven = faces.Uneven;
            if (!faces.Left && !faces.Right)
            {
                into.Add(whole);
                return;
            }

            float x0 = whole.X - whole.Width * 0.5f, x1 = whole.X + whole.Width * 0.5f;
            float a = faces.Left ? Math.Max(x0, Math.Min(x1, faces.LeftX)) : x0, b = faces.Right ? Math.Max(a, Math.Min(x1, faces.RightX)) : x1;
            void Part(float from, float to, float top)
            {
                if (to - from < 0.01f)
                    return;
                SectionPiece part = whole;
                part.X = (from + to) * 0.5f;
                part.Width = to - from;
                part.Top = top;
                into.Add(part);
            }

            Part(x0, a, faces.LeftFoot);
            Part(a, b, faces.Top);
            Part(b, x1, faces.RightFoot);
        }

        /// <summary>
        /// The kerb's face in a piece's mesh (0.12.34): its vertices (x across, y up, z along the piece), each row across (the vertices at one z) walked in from an edge (right: from the lowest x, up
        /// x) to the first place it is MinRise or more higher than where it starts; the face is there (at the last low point before it when it is a slope). False when no row has one, and when the rows
        /// do not agree within 5 cm (uneven: a bus bay's kerb bends in and out along the piece).
        /// </summary>
        public static bool Face(IList<Vec3> vertices, bool right, out float x, out float foot, out float top, out bool uneven)
        {
            x = foot = top = 0f;
            var rows = new Dictionary<int, Dictionary<int, (float Low, float High)>>();
            foreach (Vec3 v in vertices)
            {
                int z = (int)Math.Round(v.Z * 10f), across = (int)Math.Round(v.X * 1000f);
                if (!rows.TryGetValue(z, out Dictionary<int, (float Low, float High)> row))
                    rows[z] = row = new Dictionary<int, (float Low, float High)>();
                row[across] = row.TryGetValue(across, out (float Low, float High) h) ? (Math.Min(h.Low, v.Y), Math.Max(h.High, v.Y)) : (v.Y, v.Y);
            }

            bool found = false;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Dictionary<int, (float Low, float High)> row in rows.Values)
            {
                var keys = new List<int>(row.Keys);
                keys.Sort();
                if (!right)
                    keys.Reverse();
                float start = row[keys[0]].Low;
                for (int i = 1; i < keys.Count; i++)
                {
                    (float low, float high) = row[keys[i]];
                    if (high - start < MinRise)
                        continue;
                    float at = (low - start < MinRise * 0.5f ? keys[i] : keys[i - 1]) / 1000f;
                    lo = Math.Min(lo, at);
                    hi = Math.Max(hi, at);
                    if (!found)
                    {
                        x = at;
                        foot = start;
                        top = high;
                        found = true;
                    }

                    break;
                }
            }

            uneven = found && hi - lo > 0.05f;
            return found && !uneven;
        }

        /// <summary>The bodies of a kerb: the stones (granite), and the gutter of the Gutter style (concrete flags); empty for None or a line of no length.</summary>
        public static List<MarkingShape> Shapes(KerbLine kerb)
        {
            var stone = new MarkingShape { Solid = SolidColour.Granite, Detail = DetailKind.KerbStone };
            var gutter = new MarkingShape { Solid = SolidColour.Concrete, Detail = DetailKind.GutterStone };
            var shapes = new List<MarkingShape> { stone, gutter };
            if (kerb == null || kerb.Style == KerbStyle.None || kerb.Foot.Count < 2)
                return shapes;
            float r = Math.Max(MinRise, Math.Min(0.6f, kerb.Rise)), s = kerb.Footway >= 0 ? 1f : -1f;
            const float sink = 0.02f;
            var profile = kerb.Style == KerbStyle.Flush
                ? new List<(float u, float v)> { (-SlopeWidth, -sink), (-SlopeWidth, Over), (-Proud, r + Over), (TopWidth, r + Over), (TopWidth, -sink) }
                : new List<(float u, float v)> { (-Proud, -sink), (-Proud, r - Chamfer), (Chamfer - Proud, r + Over), (TopWidth, r + Over), (TopWidth, -sink) };
            Barriers.Sweep(stone.Mesh, kerb.Foot, Mirror(profile, s), true, true, true, kerb.Along);
            if (kerb.Style == KerbStyle.Gutter)
            {
                var flags = new List<(float u, float v)> { (-Proud - GutterWidth, -sink), (-Proud - GutterWidth, Over), (-Proud, Over), (-Proud, -sink) };
                Barriers.Sweep(gutter.Mesh, kerb.Foot, Mirror(flags, s), true, true, true, kerb.Along);
            }

            return shapes;
        }

        /// <summary>A profile with u toward the footway (+1: the line's right): mirrored for a footway on the left (the sweep finds its outward side by itself).</summary>
        private static List<(float u, float v)> Mirror(List<(float u, float v)> profile, float side)
        {
            if (side > 0f)
                return profile;
            var mirrored = new List<(float u, float v)>(profile.Count);
            foreach ((float u, float v) in profile)
                mirrored.Add((-u, v));
            return mirrored;
        }
    }
}
