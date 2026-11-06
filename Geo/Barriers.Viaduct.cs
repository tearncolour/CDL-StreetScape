using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// How planters are held on a viaduct's crash wall (0.12.49), the four ways of the Beijing guide to vertical greening (北京市立体绿化技术指南 2024, 3.2.2.1 (5)): hung on its outer face (外挂), astride
    /// it (骑跨/马鞍式), standing on its top (置顶), or a trough cast with the wall (预留种植槽, CJJ/T 75-2023 5.3.4: "新建高架道路、天桥等沿口宜预留种植槽").
    /// </summary>
    public enum PlanterMount
    {
        Hung,
        Saddle,
        OnTop,
        Trough
    }

    /// <summary>Planters on a viaduct's crash wall (高架防撞墙花箱, 0.12.49).</summary>
    public sealed class ViaductPlanterOptions
    {
        public PlanterMount Mount = PlanterMount.Hung;

        /// <summary>Whether the mod draws the crash wall too; without it the planters fit a wall of WallHeight and WallTop with upright faces that the player has.</summary>
        public bool Wall = true;

        public float WallHeight = 1.0f, WallTop = 0.3f;

        /// <summary>A box's length (m): the modules are 1 to 2 m.</summary>
        public float BoxLength = 1f;

        public SolidColour BoxColour = SolidColour.White, BracketColour = SolidColour.Metal;

        /// <summary>The plants: GreenPlants.Flower (the roses of Shanghai's elevated roads) or GreenPlants.Shrub; Mixed makes every third box shrubs (Yan'an Road's ten roses to five golden privets).</summary>
        public string Plants = GreenPlants.Flower;

        public bool Mixed;
    }

    /// <summary>A footway flower bowl's form (0.12.49): a stone bowl on a foot (花钵), or a plain round planter of glass-fibre concrete (圆形花箱).</summary>
    public enum BowlStyle
    {
        Stone,
        Round
    }

    public static partial class Barriers
    {
        /// <summary>The F-shape of a crash wall's face toward the traffic: an upright lip of 75 mm, a 55° face to 255 mm, an 84° face to the top (the F-shape of the usual drawings, not a clause).</summary>
        private const float Lip = 0.075f, Knee = 0.255f, Tan55 = 1.4281f, Tan84 = 9.5144f;

        /// <summary>A planter box's sizes (m), estimates from the suppliers' photos and listings (PP troughs 0.22 to 0.24 m wide and 0.26 to 0.33 m deep): 0.35 wide, 0.32 deep, walls 12 mm, a rolled rim.</summary>
        public const float BoxWidth = 0.35f, BoxDepth = 0.32f, BoxWall = 0.012f;

        /// <summary>The clear gap between a hung box's side and the wall's face (m; its rim's lip 12 mm less), and between two boxes along the wall (their lips 16 mm apart).</summary>
        public const float HungGap = 0.03f, BoxGap = 0.04f;

        /// <summary>
        /// The section of the crash wall (防撞墙) of height h and top width top: by the roadside (median false) its back upright on the line and its F-shaped face toward the traffic (side +1: the traffic is
        /// on the line's right), in a median F-shaped both ways about the line. The first corner is the back's (the middle's) foot, so that the caps are fans from it.
        /// </summary>
        private static List<(float u, float v)> CrashWall(float h, float top, int side, bool median)
        {
            float s = side >= 0 ? 1f : -1f, edge = median ? top * 0.5f : top;
            float knee = edge + (h - Knee) / Tan84, lip = knee + (Knee - Lip) / Tan55;
            var face = new List<(float u, float v)> { (lip, -Embed), (lip, Lip), (knee, Knee), (edge, h) };
            var profile = new List<(float u, float v)> { (0f, -Embed) };
            if (!median)
            {
                foreach ((float u, float v) in face)
                    profile.Add((s * u, v));
                profile.Add((0f, h));
                return profile;
            }

            foreach ((float u, float v) in face)
                profile.Add((u, v));
            for (int i = face.Count - 1; i >= 0; i--)
                profile.Add((-face[i].u, face[i].v));
            return profile;
        }

        /// <summary>
        /// Where a face of the wall is across (module x) depth metres under its top: out +1 the face on the line's right, -1 on its left. A roadside wall's back is upright on the line; its other face and a
        /// median wall's are F-shaped when the mod draws the wall (shaped), upright on a wall of the player's.
        /// </summary>
        private static float FaceAt(float out_, float depth, float top, int side, bool median, bool shaped)
        {
            float s = side >= 0 ? 1f : -1f, lean = shaped ? Math.Max(0f, depth) / Tan84 : 0f;
            if (median)
                return out_ * (top * 0.5f + lean);
            return out_ == s ? s * (top + lean) : 0f;
        }

        /// <summary>
        /// A planter box between x0 and x1 across, from y0 to its rim at y1, z0 to z1 along, in a module's frame, made as its surfaces are seen: the bottom, the sides up to a rim rolled 12 mm out and 25 mm
        /// down, the rim's top BoxWall wide, the inside of the walls down to the soil, the soil 3 cm under the rim (Shanghai's upkeep rules for the elevated roads' boxes, 沪绿容〔2021〕41号: "土面低于箱口
        /// 3cm 以上"); the box and its soil together are closed (they are seen from the street under the viaduct). The soil's height.
        /// </summary>
        private static float PlanterBox(MeshBuilder box, MeshBuilder soil, Module m, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            const float t = BoxWall, lip = 0.012f, lipHeight = 0.025f, below = 0.03f;
            if (x0 > x1)
                (x0, x1) = (x1, x0);
            float yl = y1 - lipHeight, ys = y1 - below;
            Vec3 up = MeshBuilder.Normalize(Up - m.A * m.K);
            // The rectangles of the box across and along: its sides, its lip's outside, the inside of its walls.
            (float x0, float x1, float z0, float z1) side = (x0, x1, z0, z1), outer = (x0 - lip, x1 + lip, z0 - lip, z1 + lip), inner = (x0 + t, x1 - t, z0 + t, z1 - t);

            void Flat(MeshBuilder mesh, (float x, float z)[] c, float y, Vec3 n) =>
                Quad(mesh, m.At(c[0].x, y, c[0].z), m.At(c[1].x, y, c[1].z), m.At(c[2].x, y, c[2].z), m.At(c[3].x, y, c[3].z), n, c[0].x, c[0].z, c[1].x, c[1].z, c[2].x, c[2].z, c[3].x, c[3].z);

            // The four upright faces of a rectangle from ya to yb, facing out (sign +1) or in (-1).
            void Walls((float x0, float x1, float z0, float z1) r, float ya, float yb, float sign)
            {
                Quad(box, m.At(r.x0, ya, r.z0), m.At(r.x0, ya, r.z1), m.At(r.x0, yb, r.z1), m.At(r.x0, yb, r.z0), m.R * -sign, r.z0, ya, r.z1, ya, r.z1, yb, r.z0, yb);
                Quad(box, m.At(r.x1, ya, r.z0), m.At(r.x1, ya, r.z1), m.At(r.x1, yb, r.z1), m.At(r.x1, yb, r.z0), m.R * sign, r.z0, ya, r.z1, ya, r.z1, yb, r.z0, yb);
                Quad(box, m.At(r.x0, ya, r.z0), m.At(r.x1, ya, r.z0), m.At(r.x1, yb, r.z0), m.At(r.x0, yb, r.z0), m.A * -sign, r.x0, ya, r.x1, ya, r.x1, yb, r.x0, yb);
                Quad(box, m.At(r.x0, ya, r.z1), m.At(r.x1, ya, r.z1), m.At(r.x1, yb, r.z1), m.At(r.x0, yb, r.z1), m.A * sign, r.x0, ya, r.x1, ya, r.x1, yb, r.x0, yb);
            }

            // The flat ring between an outer and an inner rectangle at y, facing n.
            void Ring((float x0, float x1, float z0, float z1) o, (float x0, float x1, float z0, float z1) i, float y, Vec3 n)
            {
                Flat(box, new[] { (o.x0, o.z0), (i.x0, i.z0), (i.x0, i.z1), (o.x0, o.z1) }, y, n);
                Flat(box, new[] { (o.x1, o.z0), (i.x1, i.z0), (i.x1, i.z1), (o.x1, o.z1) }, y, n);
                Flat(box, new[] { (o.x0, o.z0), (o.x1, o.z0), (i.x1, i.z0), (i.x0, i.z0) }, y, n);
                Flat(box, new[] { (o.x0, o.z1), (o.x1, o.z1), (i.x1, i.z1), (i.x0, i.z1) }, y, n);
            }

            Flat(box, new[] { (x0, z0), (x1, z0), (x1, z1), (x0, z1) }, y0, up * -1f);
            Walls(side, y0, yl, 1f);
            Ring(outer, side, yl, up * -1f);
            Walls(outer, yl, y1, 1f);
            Ring(outer, inner, y1, up);
            Walls(inner, ys, y1, -1f);
            Flat(soil, new[] { (inner.x0, inner.z0), (inner.x1, inner.z0), (inner.x1, inner.z1), (inner.x0, inner.z1) }, ys, up);
            return ys;
        }

        /// <summary>A flat bar from (x0, y0) to (x1, y1) in a module's cross-section at z, thick across its length in that plane and wide along z.</summary>
        private static void Strut(MeshBuilder mesh, Module m, float x0, float y0, float x1, float y1, float z, float thick, float wide)
        {
            float dx = x1 - x0, dy = y1 - y0, length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-4f)
                return;
            float nx = -dy / length * thick * 0.5f, ny = dx / length * thick * 0.5f;
            var corners = new List<Vec3>(8);
            for (int i = 0; i < 8; i++)
            {
                float sn = (i & 1) != 0 ? 1f : -1f, along = (i & 2) != 0 ? 1f : 0f, sz = (i & 4) != 0 ? 1f : -1f;
                corners.Add(m.At(x0 + dx * along + nx * sn, y0 + dy * along + ny * sn, z + sz * wide * 0.5f));
            }

            Solids.Convex(mesh, corners, BlockFaces);
        }

        private static readonly int[][] BlockFaces = { new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 } };

        /// <summary>A bolt's head (or an expansion bolt's nut) on a face: a short six-sided stud from a (on the face: no end there) to b, 24 mm across.</summary>
        private static void Bolt(MeshBuilder mesh, Vec3 a, Vec3 b) => Solids.Tube(mesh, a, b, 0.012f, 0.012f, 6, false, true);

        /// <summary>
        /// Planters along a viaduct's crash wall (高架桥防撞墙花箱): the wall (CrashWall) when the mod draws it, and on it modules of one box each, BoxLength long with BoxGap between them, laid out along the
        /// whole run (phase, whole) so that the pieces go on in step and none hangs over an end; or a trough cast with the wall all along it. A hung box hangs on its outer face on two steel brackets (a
        /// flat bar hooked over the wall's top and bolted to it, down the face, an arm under the box with a lip at its end and a strut back to the face), a saddle box sits on a sheet-steel saddle whose two
        /// skirts come down the wall's faces 0.3 m with V-notches between the modules (as the Shenzhen photos), a box on the top on two bolted sleepers. In a median the boxes are astride or on the top
        /// (a hung one would hang over the traffic); a trough needs the mod's own wall by the roadside, and is hung boxes otherwise. The plants are the game's, two to a metre of box. Sizes are the usual
        /// products' and drawings' estimates.
        /// </summary>
        public static List<MarkingShape> ViaductPlanters(IList<Vec3> line, ViaductPlanterOptions o, int side, bool median, float phase, float whole, bool first, bool last, List<PropAnchor> props)
        {
            var concrete = new MarkingShape { Solid = SolidColour.Concrete };
            var box = new MarkingShape { Solid = o.BoxColour, Detail = o.BoxColour == SolidColour.Wood ? DetailKind.None : DetailKind.Plastic };
            var soil = new MarkingShape { Solid = SolidColour.Soil };
            var steel = new MarkingShape { Solid = o.BracketColour };
            var shapes = new List<MarkingShape> { concrete, box, soil, steel };
            if (line == null || line.Count < 2)
                return shapes;
            float h = Math.Max(0.6f, Math.Min(1.5f, o.WallHeight)), top = Math.Max(0.2f, Math.Min(0.6f, o.WallTop)), length = Math.Max(0.6f, Math.Min(2f, o.BoxLength));
            float s = side >= 0 ? 1f : -1f, out_ = -s;
            PlanterMount mount = o.Mount;
            if (mount == PlanterMount.Trough && (median || !o.Wall))
                mount = PlanterMount.Hung;
            if (mount == PlanterMount.Hung && median)
                mount = PlanterMount.Saddle;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            if (!(whole > 0f))
                whole = phase + total;
            if (o.Wall)
                Sweep(concrete.Mesh, line, CrashWall(h, top, side, median), true, first, last, phase);
            if (mount == PlanterMount.Trough)
            {
                Trough(concrete.Mesh, soil.Mesh, line, at, total, h, out_, phase, whole, first, last, o, props);
                return shapes;
            }

            // The wall's top across, in a module's frame.
            float xa = median ? -top * 0.5f : Math.Min(0f, s * top), xb = median ? top * 0.5f : Math.Max(0f, s * top);
            // As many boxes as the whole run holds 1 cm in from its ends, the row centred on it.
            float pitch = length + BoxGap;
            int count = (int)Math.Floor((whole - 0.02f + BoxGap + 1e-4f) / pitch);
            float start = (whole - (count * pitch - BoxGap)) * 0.5f;
            const float off = 0.012f, sheet = 0.004f, skirt = 0.3f, notch = 0.12f, sleeper = 0.04f;
            for (int i = 0; i < count; i++)
            {
                float g = start + length * 0.5f + i * pitch, local = g - phase;
                if (!Holds(local, total, last))
                    continue;
                Module m = Module.On(line, at, local);
                WallSpan(line, at, total, m, local - length * 0.5f - BoxWall, local + length * 0.5f + BoxWall, out float minX, out float maxX);
                // Across: a hung box moved out as far as a wall that bends toward it comes in under it; a saddle clear of both faces by off, and by as far as a bend brings the wall out under its ends.
                float offLeft = off + Math.Max(0f, -minX), offRight = off + Math.Max(0f, maxX), x0, x1, y0;
                switch (mount)
                {
                    case PlanterMount.Hung:
                        x0 = out_ * (HungGap + Math.Max(0f, out_ < 0f ? -minX : maxX));
                        x1 = x0 + out_ * BoxWidth;
                        y0 = h - 0.05f - BoxDepth;
                        break;
                    case PlanterMount.Saddle:
                        x0 = xa - offLeft - 0.02f;
                        x1 = xb + offRight + 0.02f;
                        y0 = h + sheet;
                        break;
                    default:
                    {
                        float mid = (xa + xb) * 0.5f, half = Math.Min(top + 0.04f, 0.4f) * 0.5f;
                        x0 = mid - half;
                        x1 = mid + half;
                        y0 = h + sleeper;
                        break;
                    }
                }

                // Along: on the inside of a bend two straight boxes meet sooner on their far side; each is shortened to keep to its side of the joints (BoxGap / 2 less its lip from the plane across the line there).
                float z0 = Clear(line, at, total, phase, m, g - pitch * 0.5f, -length * 0.5f, -1f, x0, x1), z1 = Clear(line, at, total, phase, m, g + pitch * 0.5f, length * 0.5f, 1f, x0, x1);
                float span = z1 - z0, soilTop = PlanterBox(box.Mesh, soil.Mesh, m, x0, x1, y0, y0 + BoxDepth, z0, z1);
                switch (mount)
                {
                    case PlanterMount.Hung:
                    {
                        float zb = span * 0.5f - Math.Min(0.25f, span * 0.25f), zm = (z0 + z1) * 0.5f;
                        foreach (float z in new[] { zm - zb, zm + zb })
                        {
                            // The bracket's bar (50 mm wide) against the wall where it comes out furthest under it.
                            WallSpan(line, at, total, m, local + z - 0.025f, local + z + 0.025f, out float lo, out float hi);
                            HungBracket(steel.Mesh, m, out_ < 0f ? lo : hi, z, h, y0, out_, x1);
                        }

                        break;
                    }
                    case PlanterMount.Saddle:
                    {
                        Block(steel.Mesh, m, xa - offLeft, xb + offRight, h, h + sheet, z0 + 0.01f, z1 - 0.01f);
                        foreach (float e in new[] { -1f, 1f })
                        {
                            float clear = e < 0f ? offLeft : offRight;
                            float inTop = FaceAt(e, 0f, top, side, median, o.Wall) + e * clear, inBottom = FaceAt(e, skirt, top, side, median, o.Wall) + e * clear;
                            var corners = new List<Vec3>(8);
                            for (int c = 0; c < 8; c++)
                            {
                                bool low = (c & 2) != 0, outer = (c & 1) != 0;
                                float zz = (c & 4) != 0 ? z1 - 0.01f - (low ? notch : 0f) : z0 + 0.01f + (low ? notch : 0f);
                                corners.Add(m.At((low ? inBottom : inTop) + (outer ? e * sheet : 0f), low ? h - skirt : h + sheet, zz));
                            }

                            Solids.Convex(steel.Mesh, corners, BlockFaces);
                            float face = FaceAt(e, skirt * 0.5f, top, side, median, o.Wall) + e * (clear + sheet);
                            Bolt(steel.Mesh, m.At(face, h - skirt * 0.5f, (z0 + z1) * 0.5f), m.At(face + e * 0.01f, h - skirt * 0.5f, (z0 + z1) * 0.5f));
                        }

                        break;
                    }
                    default:
                    {
                        // Each sleeper on the wall's top where it is under it (on a bend the wall moves across under a straight box).
                        float zs = span * 0.3f, zm = (z0 + z1) * 0.5f;
                        foreach (float z in new[] { zm - zs, zm + zs })
                        {
                            float d = LateralAt(line, at, total, m, local + z);
                            Block(steel.Mesh, m, d + xa + 0.01f, d + xb - 0.01f, h, h + sleeper, z - sleeper * 0.5f, z + sleeper * 0.5f);
                            foreach (float x in new[] { d + xa + 0.05f, d + xb - 0.05f })
                                Bolt(steel.Mesh, m.At(x, h + sleeper, z), m.At(x, h + sleeper + 0.008f, z));
                        }

                        break;
                    }
                }

                if (props == null)
                    continue;
                int n = Math.Max(1, (int)Math.Round(span / 0.5f));
                string kind = o.Mixed && i % 3 == 2 ? GreenPlants.Shrub : o.Plants;
                float room = Math.Min(span / n, Math.Abs(x1 - x0) - 2f * BoxWall);
                for (int p = 0; p < n; p++)
                    props.Add(new PropAnchor { Local = m.At((x0 + x1) * 0.5f, soilTop, z0 + span * (p + 0.5f) / n), Yaw = YawOf(m.A), Kind = kind, Room = room });
            }

            return shapes;
        }

        /// <summary>Where the line is across a module's frame (m along its right) d metres along the line (beyond its ends on their chords drawn on).</summary>
        private static float LateralAt(IList<Vec3> line, IList<float> at, float total, Module m, float d)
        {
            Vec3 p = d < 0f ? line[0] + (line[1] - line[0]).HeadingXZ() * d
                : d > total ? line[line.Count - 1] + (line[line.Count - 1] - line[line.Count - 2]).HeadingXZ() * (d - total) : RoadMarkings.PointAt(line, at, d);
            return Vec3.Dot(p - m.O, m.R);
        }

        /// <summary>
        /// The end of a box (z in its module m, end -1 the near one, +1 the far one, its sides across at x0 and x1) kept behind the joint jointG metres along the whole run: its rim's lip no nearer to the
        /// plane across the line there (square to its heading) than BoxGap / 2 less the lip, as on a straight run; a box is only ever shortened.
        /// </summary>
        private static float Clear(IList<Vec3> line, IList<float> at, float total, float phase, Module m, float jointG, float z, float end, float x0, float x1)
        {
            const float lip = 0.012f;
            float d = jointG - phase;
            Vec3 joint = d < 0f ? line[0] + (line[1] - line[0]).HeadingXZ() * d
                : d > total ? line[line.Count - 1] + (line[line.Count - 1] - line[line.Count - 2]).HeadingXZ() * (d - total) : RoadMarkings.PointAt(line, at, d);
            Vec3 across = HeadingAt(line, at, Math.Max(0f, Math.Min(total, d)));
            float need = float.MinValue;
            foreach (float x in new[] { Math.Min(x0, x1) - lip, Math.Max(x0, x1) + lip })
            {
                Vec3 corner = m.O + m.R * x + m.A * (z + end * lip);
                need = Math.Max(need, end * ((corner.X - joint.X) * across.X + (corner.Z - joint.Z) * across.Z) + (BoxGap * 0.5f - lip));
            }

            float cos = Math.Max(0.5f, Vec3.Dot(m.A, across));
            return end * (end * z - Math.Max(0f, need) / cos);
        }

        /// <summary>How far the line goes across a module's frame (m along its right, the least and the most) between d0 and d1 metres along it: at its ends and at the line's points between them.</summary>
        private static void WallSpan(IList<Vec3> line, IList<float> at, float total, Module m, float d0, float d1, out float min, out float max)
        {
            min = Math.Min(LateralAt(line, at, total, m, d0), LateralAt(line, at, total, m, d1));
            max = Math.Max(LateralAt(line, at, total, m, d0), LateralAt(line, at, total, m, d1));
            for (int i = 0; i < line.Count; i++)
            {
                if (at[i] <= d0 || at[i] >= d1)
                    continue;
                float x = Vec3.Dot(line[i] - m.O, m.R);
                min = Math.Min(min, x);
                max = Math.Max(max, x);
            }
        }

        /// <summary>
        /// A hung box's bracket at z in the box's module, the wall's back at wall across there (on a bend the wall moves across under a straight box), the box's outer side at outer: an 8 mm flat bar 50 mm
        /// wide hooked over the wall's top (0.15 m on it) and down its outer face, bolted to it, an arm under the box out to a lip at its outer side, a strut from the arm's end back to the bar 0.32 m under it.
        /// </summary>
        private static void HungBracket(MeshBuilder mesh, Module m, float wall, float z, float h, float bottom, float out_, float outer)
        {
            const float t = 0.008f, wide = 0.05f;
            float X(float w) => wall + out_ * w;
            float z0 = z - wide * 0.5f, z1 = z + wide * 0.5f;
            Block(mesh, m, X(-0.15f), X(t), h, h + 0.006f, z0, z1);
            Block(mesh, m, X(0f), X(t), bottom - 0.35f, h + 0.006f, z0, z1);
            Bolt(mesh, m.At(X(t), bottom - 0.2f, z), m.At(X(t + 0.01f), bottom - 0.2f, z));
            Block(mesh, m, X(t), outer + out_ * 0.012f, bottom - t, bottom, z0, z1);
            Block(mesh, m, outer, outer + out_ * 0.012f, bottom, bottom + 0.06f, z0, z1);
            Strut(mesh, m, outer - out_ * 0.01f, bottom - t, X(t), bottom - 0.32f, z, t, 0.04f);
        }

        /// <summary>
        /// The planting trough cast with the wall (预留种植槽) on its outer side (out_): inside 0.45 m wide and 0.40 m deep, its outer wall and its floor 0.12 m thick, its rim 5 cm under the wall's top, the soil
        /// 5 cm under the rim, closed by end walls at the ends of the whole run; the plants every 0.5 m along its middle. Sizes estimated (Xiamen's guidance: troughs 0.60 m; Beijing's: soil 0.3 m and more).
        /// </summary>
        private static void Trough(MeshBuilder concrete, MeshBuilder soil, IList<Vec3> line, IList<float> at, float total, float h, float out_, float phase, float whole, bool first, bool last,
            ViaductPlanterOptions o, List<PropAnchor> props)
        {
            const float inside = 0.45f, thick = 0.12f, deep = 0.40f;
            float rim = h - 0.05f, floor = rim - deep, bottom = floor - thick, d0 = first ? thick : 0f, d1 = last ? thick : 0f;
            List<Vec3> inner = Trimmed(line, at, d0, total - d1);
            if (inner.Count >= 2)
            {
                Sweep(concrete, inner, Rect(Math.Min(0f, out_ * (inside + thick)), Math.Max(0f, out_ * (inside + thick)), bottom, floor), true, true, true, phase + d0);
                Sweep(concrete, inner, Rect(Math.Min(out_ * inside, out_ * (inside + thick)), Math.Max(out_ * inside, out_ * (inside + thick)), floor, rim), true, true, true, phase + d0);
                Sweep(soil, inner, Rect(Math.Min(0f, out_ * inside), Math.Max(0f, out_ * inside), floor, rim - 0.05f), true, true, true, phase + d0);
            }

            if (first)
                Block(concrete, Module.On(line, at, 0f), 0f, out_ * (inside + thick), bottom, rim, 0f, thick);
            if (last)
                Block(concrete, Module.On(line, at, total), 0f, out_ * (inside + thick), bottom, rim, -thick, 0f);
            if (props == null)
                return;
            const float pitch = 0.5f;
            int count = (int)Math.Floor((whole - 2f * thick - pitch) / pitch + 1e-4f) + 1;
            float start = (whole - (count - 1) * pitch) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float local = start + i * pitch - phase;
                if (!Holds(local, total, last))
                    continue;
                Vec3 ahead = HeadingAt(line, at, local);
                string kind = o.Mixed && i / 2 % 3 == 2 ? GreenPlants.Shrub : o.Plants;
                props.Add(new PropAnchor { Local = RoadMarkings.PointAt(line, at, local) + ahead.RightXZ() * (out_ * inside * 0.5f) + Up * (rim - 0.05f), Yaw = YawOf(ahead), Kind = kind, Room = pitch });
            }
        }

        /// <summary>
        /// Flower bowls on a footway (人行道花钵) every spacing metres along the line: a stone bowl on a foot (a round plinth, a neck, the bowl swelling to a rolled rim) or a round planter of glass-fibre
        /// concrete a little wider at the top, diameter across its rim; the soil 5 to 6 cm under the rim and one of the game's plants in it. The proportions are estimates from photos.
        /// </summary>
        public static List<MarkingShape> FlowerBowls(IList<Vec3> line, BowlStyle style, float diameter, SolidColour colour, string plants, float spacing, float phase, List<PropAnchor> props)
        {
            var bowl = new MarkingShape { Solid = colour, Detail = colour == SolidColour.Granite ? DetailKind.Stone : DetailKind.Concrete };
            var soil = new MarkingShape { Solid = SolidColour.Soil };
            var shapes = new List<MarkingShape> { bowl, soil };
            if (line == null || line.Count < 2)
                return shapes;
            float k = Math.Max(0.6f, Math.Min(1.5f, diameter));
            // (radius, height) for a bowl 1 m across, outside from the foot up and over the rim down to the soil.
            (float r, float y)[] unit = style == BowlStyle.Stone
                ? new[] { (0.30f, 0f), (0.30f, 0.10f), (0.22f, 0.12f), (0.15f, 0.16f), (0.15f, 0.26f), (0.22f, 0.30f), (0.42f, 0.45f), (0.47f, 0.56f), (0.505f, 0.60f), (0.505f, 0.68f), (0.45f, 0.68f), (0.44f, 0.63f) }
                : new[] { (0.44f, 0f), (0.50f, 0.70f), (0.46f, 0.70f), (0.455f, 0.645f) };
            var profile = new List<(float r, float y)> { (unit[0].r * k, -FootSink) };
            foreach ((float r, float y) in unit)
                profile.Add((r * k, y * k));
            (float soilR, float soilY) = profile[profile.Count - 1];
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in MarkingStuds.Stations(total, Math.Max(1.5f, spacing), phase))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Lathe(bowl.Mesh, p, profile, 20);
                Disc(soil.Mesh, p + Up * soilY, soilR, 20);
                props?.Add(new PropAnchor { Local = p + Up * soilY, Yaw = YawOf(HeadingAt(line, at, s)), Kind = plants, Room = soilR * 2f });
            }

            return shapes;
        }

        /// <summary>A body of revolution about the upright through at: each step of the profile (radius, height) a ring of quads facing out of it (its outer side going up faces out, its inner side going down faces in), texture coordinates in metres.</summary>
        private static void Lathe(MeshBuilder mesh, Vec3 at, IList<(float r, float y)> profile, int sides)
        {
            float v = 0f;
            for (int j = 0; j + 1 < profile.Count; j++)
            {
                (float r0, float y0) = profile[j];
                (float r1, float y1) = profile[j + 1];
                float dr = r1 - r0, dy = y1 - y0, length = (float)Math.Sqrt(dr * dr + dy * dy);
                if (length < 1e-5f)
                    continue;
                float nr = dy / length, ny = -dr / length, round = (r0 + r1) * 0.5f;
                int first = mesh.VertexCount;
                for (int i = 0; i <= sides; i++)
                {
                    double a = 2.0 * Math.PI * i / sides;
                    var radial = new Vec3((float)Math.Cos(a), 0f, (float)Math.Sin(a));
                    Vec3 n = MeshBuilder.Normalize(radial * nr + Up * ny);
                    mesh.Vertex(at + radial * r0 + Up * y0, n, (float)a * round, v);
                    mesh.Vertex(at + radial * r1 + Up * y1, n, (float)a * round, v + length);
                }

                for (int i = 0; i < sides; i++)
                {
                    int a0 = first + i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                    mesh.QuadFacing(a0, b0, b1, a1, MeshBuilder.Normalize(mesh.Normals[a0] + mesh.Normals[b0]));
                }

                v += length;
            }
        }

        /// <summary>A flat disc facing up, centred at c, texture coordinates in metres.</summary>
        private static void Disc(MeshBuilder mesh, Vec3 c, float radius, int sides)
        {
            int middle = mesh.Vertex(c, Up, 0f, 0f);
            for (int i = 0; i <= sides; i++)
            {
                double a = 2.0 * Math.PI * i / sides;
                float x = (float)Math.Cos(a) * radius, z = (float)Math.Sin(a) * radius;
                mesh.Vertex(c + new Vec3(x, 0f, z), Up, x, z);
            }

            for (int i = 0; i < sides; i++)
                mesh.TriFacing(middle, middle + 1 + i, middle + 2 + i, Up);
        }
    }
}
