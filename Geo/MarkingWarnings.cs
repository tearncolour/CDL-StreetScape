using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The channelising fills of chapter 6 and the warning markings of chapter 7 of GB 5768.3-2025: the channelising island (导流线, 6.6.3), the exit and entry gore (线65, 6.12.2, Fig. 94), the
    /// transverse and longitudinal rumble markings (线66 to 线68, 7.3), the toll plaza bars (线69, 7.4, Table 6), the toll island (线70, 7.5), the level crossing (线71, 7.6.1), and the obstacle
    /// approach (7.2.2). Frame: X right, Z the way the traffic runs. Pure.
    /// </summary>
    public static class MarkingWarnings
    {
        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        // ---- 6.6.3 导流线: an area bounded by a solid outline (as wide as the lines it joins) and filled with hatching: white between traffic the same way, yellow between opposing traffic; between
        // motor vehicles the lines are 40 or 45 wide 1 m apart at 45 degrees (V-shaped for an island between lanes the same way: each half leaning the other way so that the V points at the traffic,
        // Fig. 79; plain diagonals between opposing lanes, Fig. 80); between a motor lane and a cycle lane 30 wide 2 m apart (Fig. 81).
        public enum ChannelFill
        {
            Chevron,
            Diagonal,
            CycleSide
        }

        /// <summary>The island as a polygon (either order); its outline width; the fill. For the chevron fill the polygon is cut by the line x = axisX (the island's axis) and each half hatched its own way.</summary>
        public static MarkingShape Channelising(IList<Vec3> island, float outlineWidth, ChannelFill fill, MarkingColour colour, float axisX = 0f, float lineWidth = 0.45f) =>
            MarkingAreas.Island(island, outlineWidth, fill, colour, lineWidth);

        /// <summary>The part of a convex-enough polygon on one side of x = axisX (Sutherland-Hodgman against one line).</summary>
        private static List<Vec3> ClipHalf(IList<Vec3> polygon, float axisX, bool left) => ClipSide(polygon, P(axisX, 0f), P(axisX, 1f), left);

        /// <summary>The part of a convex-enough polygon on the left (or right) of the directed line a to b.</summary>
        private static List<Vec3> ClipSide(IList<Vec3> polygon, Vec3 a, Vec3 b, bool left)
        {
            var result = new List<Vec3>();
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                Vec3 p = polygon[i], q = polygon[(i + 1) % n];
                float sp = Polygon.Cross(a, b, p), sq = Polygon.Cross(a, b, q);
                bool pin = left ? sp >= 0f : sp <= 0f, qin = left ? sq >= 0f : sq <= 0f;
                if (pin)
                    result.Add(p);
                if (pin != qin)
                    result.Add(Vec3.Lerp(p, q, sp / (sp - sq)));
            }

            return result;
        }

        /// <summary>
        /// V-shaped lines (V 型线) in an area about an axis from a to b (the V points at a, toward the traffic): the half on the left of the axis is hatched leaning one way and the half on the
        /// right the other, both 45 degrees from the axis, with the width and pitch given.
        /// </summary>
        public static void ChevronHatch(MeshBuilder mesh, IList<Vec3> area, Vec3 a, Vec3 b, float width, float pitch)
        {
            double axis = Math.Atan2(b.X - a.X, b.Z - a.Z) * 180.0 / Math.PI;
            MarkingFills.Hatch(mesh, ClipSide(area, a, b, true), width, pitch, (float)(axis + 135.0));
            MarkingFills.Hatch(mesh, ClipSide(area, a, b, false), width, pitch, (float)(axis + 45.0));
        }

        // ---- 6.12.2 线65 出入口标线 (Fig. 94): at an exit, the gore between the through lanes and the ramp: a thick solid line (粗实线, 45 wide, 200 or more) at the nose on the through side, two edge
        // lines (20) along the gore's sides, V-shaped lines (45 wide, 100 apart, 45 degrees) inside, and the thick dashed line (粗虚线, 45 wide, 3 m on 3 m off) along the through lane before the nose.
        // The gore is a triangle: its nose at z = 0 on the through lane's edge (x = 0), widening to width at z = length (the ramp on the right); the dashed line runs from z = -dashedLength to 0.
        public static MarkingShape ExitGore(float length, float width, float dashedLength = 60f, float noseSolid = 2f, bool rampOnRight = true)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(length > 0f) || !(width > 0f))
                return shape;
            float x = rampOnRight ? 1f : -1f, lineX = rampOnRight ? 0.225f : -0.225f;
            // The thick dashed line (3-3, 45 wide) along the through edge before the nose, then the thick solid line (45 wide, 200 or more) up to the nose: both on the ramp's side of the through edge.
            if (dashedLength > 0f)
                MarkingFigures.Append(shape, RoadMarkings.Dashed(new List<Vec3> { P(lineX, -dashedLength), P(lineX, 0f) }, new DashSpec(3f, 3f, 0.45f, MarkingColour.White)));
            float solid = Math.Max(2f, noseSolid);
            RoadMarkings.Rect(shape.Mesh, 0f, 0f, 0.45f * x, Math.Min(solid, length));
            if (length - solid < 1f)
                return shape;
            // The gore from the nose at z = solid: the through side straight on, the ramp side out to the width at the far end; its edge lines and V lines as MarkingAreas.Gore makes them.
            var main = new List<Vec3> { P(0f, solid), P(0f, length) };
            var ramp = new List<Vec3> { P(0f, solid), P(width * x, length) };
            MarkingFigures.Append(shape, MarkingAreas.Gore(main, ramp, true));
            return shape;
        }

        /// <summary>
        /// The same marking for an entry (Fig. 94 b): the exit's figure run the other way over the road: the gore is widest where the traffic first meets it and narrows to the nose, and the thick solid line and then
        /// the thick dashed line follow the nose.
        /// </summary>
        public static MarkingShape EntryGore(float length, float width, float dashedLength = 60f, float noseSolid = 2f, bool rampOnRight = true)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(length > 0f) || !(width > 0f))
                return shape;
            float x = rampOnRight ? 1f : -1f, lineX = rampOnRight ? 0.225f : -0.225f;
            float solid = Math.Max(2f, noseSolid);
            // The gore before the nose (its wide end first, where the merging lane comes in), then the thick solid line from the nose and the thick dashed line after it.
            RoadMarkings.Rect(shape.Mesh, 0f, -Math.Min(solid, length), 0.45f * x, 0f);
            if (dashedLength > 0f)
                MarkingFigures.Append(shape, RoadMarkings.Dashed(new List<Vec3> { P(lineX, 0f), P(lineX, dashedLength) }, new DashSpec(3f, 3f, 0.45f, MarkingColour.White)));
            if (length - solid < 1f)
                return shape;
            var main = new List<Vec3> { P(0f, -solid), P(0f, -length) };
            var ramp = new List<Vec3> { P(0f, -solid), P(width * x, -length) };
            MarkingFigures.Append(shape, MarkingAreas.Gore(main, ramp, false));
            return shape;
        }

        /// <summary>A shape turned about the line across the road at z = 0 (every z becomes -z); the triangles keep facing up.</summary>
        public static MarkingShape Mirrored(MarkingShape source)
        {
            var shape = new MarkingShape { Colour = source.Colour, Lift = source.Lift, Upright = source.Upright };
            var up = new Vec3(0f, 1f, 0f);
            var map = new int[source.Mesh.VertexCount];
            for (int i = 0; i < source.Mesh.VertexCount; i++)
            {
                Vec3 p = source.Mesh.Positions[i];
                map[i] = shape.Mesh.Vertex(new Vec3(p.X, p.Y, -p.Z), up, source.Mesh.U[i], source.Mesh.V[i]);
            }

            for (int i = 0; i + 2 < source.Mesh.Indices.Count; i += 3)
                shape.Mesh.TriFacing(map[source.Mesh.Indices[i]], map[source.Mesh.Indices[i + 1]], map[source.Mesh.Indices[i + 2]], up);
            foreach (MarkingText t in source.Texts)
                shape.Texts.Add(new MarkingText { Text = t.Text, X = t.X, Z = -t.Z, Width = t.Width, Length = t.Length, Yaw = t.Yaw, Digits = t.Digits });
            return shape;
        }

        // ---- 7.3.2 线66 横向减速标线 (Fig. 101, Table 5): groups of white bars across the lane, each bar 45 thick, 45 between bars, made of pieces 100 long with 3 to 5 cm slots (4.4.3) between them;
        // two bars a group for the first six groups, three from the seventh; the groups 17, 20, 23, 26, 28, 30, 32, 32, 32 m apart (Table 5, for a long downgrade); on an ordinary road up to three groups 32 m apart.
        public static readonly float[] TransverseRumbleSpacing = { 17f, 20f, 23f, 26f, 28f, 30f, 32f, 32f, 32f };

        /// <summary>How many bars group i (0-based) has: two up to the sixth, three from the seventh (Table 5).</summary>
        public static int TransverseRumbleBars(int group) => group < 6 ? 2 : 3;

        /// <summary>One group of bars across a lane of the given width, its first bar at z = 0.</summary>
        public static MarkingShape TransverseRumbleGroup(float laneWidth, int bars, float slot = 0.04f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(laneWidth > 0f) || bars < 1)
                return shape;
            float s = Math.Max(0.03f, Math.Min(0.05f, slot));
            int pieces = (int)Math.Floor((laneWidth + s + 1e-5f) / (1f + s));
            float start = -(pieces + Math.Max(0, pieces - 1) * s) * 0.5f;
            for (int b = 0; b < bars; b++)
            {
                float z0 = b * 0.9f;
                // Fig. 101 dimensions each piece at 100 cm; spare lane width stays in equal side margins.
                for (int i = 0; i < pieces; i++)
                {
                    float x = start + i * (1f + s);
                    RoadMarkings.Rect(shape.Mesh, x, z0, x + 1f, z0 + 0.45f);
                }
            }

            return shape;
        }

        /// <summary>The groups of a downgrade (Table 5) from z = 0, as many as asked (up to ten), with their spacing; or ordinary groups 32 m apart when table is false.</summary>
        public static MarkingShape TransverseRumble(float laneWidth, int groups, bool table = true)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(laneWidth > 0f) || groups < 1)
                return shape;
            float z = 0f;
            int n = Math.Min(groups, table ? 10 : 3);
            for (int g = 0; g < n; g++)
            {
                MarkingShape one = TransverseRumbleGroup(laneWidth, table ? TransverseRumbleBars(g) : 2);
                shape.Mesh.Append(one.Mesh, P(0f, z), 0f);
                z += table ? (g < TransverseRumbleSpacing.Length ? TransverseRumbleSpacing[g] : 32f) : 32f;
            }

            return shape;
        }

        // ---- 7.3.3 线67 纵向减速标线 (Fig. 104): blocks 30 wide and 1 m long with 45-degree ends (菱形块), 1 m apart, 5 cm from the lane line, along the lane's edge; 线68 (Fig. 105): over the first
        // 30 m the blocks widen from 10 to 30. Along +Z from z = 0, on the right of x = 0 (the lane line's edge is at x = 0): the blocks at x 0.05 .. 0.35.
        public static MarkingShape LongitudinalRumble(float length, bool taper = false, bool rightSide = true, float phase = 0f, float total = 0f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(length > 0f) || float.IsInfinity(length) || float.IsNaN(phase) || float.IsInfinity(phase))
                return shape;
            float sign = rightSide ? 1f : -1f;
            float end = total > 0f ? total : phase + length;
            var window = MarkingFills.Rectangle(-1f, 0f, 1f, length);
            for (float station = Math.Max(0f, 2f * (float)Math.Floor((phase - 1.3f) / 2f)); station < phase + length; station += 2f)
            {
                float w = taper && station < 30f ? 0.1f + 0.2f * (station / 30f) : 0.3f;
                if (station + 1f + w > end + 1e-4f)
                    break;
                float z = station - phase;
                float x0 = sign * 0.05f, x1 = sign * (0.05f + w);
                // Fig. 104 measures the parallel long edge at 100 cm, not the enclosing box (100 + width).
                var block = new MeshBuilder();
                Polygon.Fill(block, new List<Vec3> { P(x0, z), P(x1, z + w), P(x1, z + w + 1f), P(x0, z + 1f) });
                MarkingFills.ClipConvex(shape.Mesh, window, block);
            }

            return shape;
        }

        public static MarkingShape LongitudinalRumble(IList<Vec3> path, bool taper, bool rightSide, float phase, float total)
        {
            var result = new MarkingShape { Colour = MarkingColour.White };
            if (path == null || path.Count < 2)
                return result;
            var at = new List<float>();
            float length = RoadMarkings.Lengths(path, at);
            MarkingShape straight = LongitudinalRumble(length, taper, rightSide, phase, total);
            List<Vec3> offset = MarkingFills.Offset(path, 1f);
            for (int i = 0; i + 1 < path.Count; i++)
            {
                float span = at[i + 1] - at[i];
                if (span < 1e-5f)
                    continue;
                var piece = new MeshBuilder();
                MarkingFills.ClipConvex(piece, MarkingFills.Rectangle(-1f, at[i], 1f, at[i + 1]), straight.Mesh);
                for (int k = 0; k < piece.VertexCount; k++)
                {
                    Vec3 p = piece.Positions[k];
                    float t = (p.Z - at[i]) / span;
                    Vec3 centre = Vec3.Lerp(path[i], path[i + 1], t);
                    Vec3 side = Vec3.Lerp(offset[i] - path[i], offset[i + 1] - path[i + 1], t);
                    piece.Positions[k] = centre + side * p.X;
                }
                result.Mesh.Append(piece, Vec3.Zero, 0f);
            }
            return result;
        }

        // ---- 7.4 线69 收费广场减速标线 (Fig. 107, Table 6): groups of one to three rows of dashed bars across the road, the dashes 50 long with 40 gaps, 45 thick, rows 45 apart; the first group 50 m
        // before the plaza's centre line, then 5, 9, 13, 17, 20, 23, 26, 28, 30 m between groups and 32 from the eleventh on; five to twelve groups.
        public static readonly float[] TollPlazaSpacing = { 5f, 9f, 13f, 17f, 20f, 23f, 26f, 28f, 30f };
        public const float TollPlazaFirst = 50f;

        public static int TollPlazaRows(int group) => group < 2 ? 1 : group < 6 ? 2 : 3;

        public static MarkingShape TollPlazaGroup(float roadWidth, int rows)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(roadWidth > 0f) || rows < 1)
                return shape;
            int pieces = (int)Math.Floor((roadWidth + 0.4f + 1e-5f) / 0.9f);
            float start = -(pieces * 0.5f + Math.Max(0, pieces - 1) * 0.4f) * 0.5f;
            for (int r = 0; r < rows; r++)
            {
                float z0 = r * 0.9f;
                for (int i = 0; i < pieces; i++)
                {
                    float x = start + i * 0.9f;
                    RoadMarkings.Rect(shape.Mesh, x, z0, x + 0.5f, z0 + 0.45f);
                }
            }

            return shape;
        }

        /// <summary>The groups from the farthest (z = 0) toward the plaza: groups of them (5 to 12), the first group nearest the plaza being 50 m before its centre line, which is then at z = TollPlazaLength(groups) + 50.</summary>
        public static MarkingShape TollPlaza(float roadWidth, int groups)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            int n = Math.Max(5, Math.Min(12, groups));
            if (!(roadWidth > 0f))
                return shape;
            // Group 0 is nearest the plaza; the farthest is group n - 1 at z = 0.
            float z = 0f;
            for (int g = n - 1; g >= 0; g--)
            {
                MarkingShape one = TollPlazaGroup(roadWidth, TollPlazaRows(g));
                shape.Mesh.Append(one.Mesh, P(0f, z), 0f);
                if (g > 0)
                    z += g - 1 < TollPlazaSpacing.Length ? TollPlazaSpacing[g - 1] : 32f;
            }

            return shape;
        }

        public static float TollPlazaLength(int groups)
        {
            int n = Math.Max(5, Math.Min(12, groups));
            float z = 0f;
            for (int g = n - 1; g > 0; g--)
                z += g - 1 < TollPlazaSpacing.Length ? TollPlazaSpacing[g - 1] : 32f;
            return z;
        }

        // ---- 7.5 线70 收费岛标线 (Fig. 109): toward the traffic, a thick solid line 20 wide and 500 long on the island's axis, then the island's outline (15 wide) widening over 1000 to the island's
        // width, filled with V lines 45 wide 1 m apart at 45 degrees; yellow where the island divides the directions, else white. From z = 0 (the tip of the thick line) along +Z.
        public static MarkingShape TollIsland(float islandWidth, bool dividesDirections = false)
        {
            var shape = new MarkingShape { Colour = dividesDirections ? MarkingColour.Yellow : MarkingColour.White };
            if (!(islandWidth > 0f))
                return shape;
            RoadMarkings.Rect(shape.Mesh, -0.1f, 0f, 0.1f, 5f);
            var nose = new List<Vec3> { P(0f, 5f), P(islandWidth * 0.5f, 15f), P(-islandWidth * 0.5f, 15f) };
            MarkingFills.Outline(shape.Mesh, nose, 0.15f);
            var inner = new List<Vec3> { P(0f, 5.5f), P(islandWidth * 0.5f - 0.2f, 14.85f), P(-islandWidth * 0.5f + 0.2f, 14.85f) };
            List<Vec3> left = ClipHalf(inner, 0f, true), right = ClipHalf(inner, 0f, false);
            MarkingFills.Hatch(shape.Mesh, left, 0.45f, 1f, 135f);
            MarkingFills.Hatch(shape.Mesh, right, 0.45f, 1f, 45f);
            return shape;
        }

        // ---- 7.6.1 线71 铁路道口标线 (Fig. 110): on a lane, from the stop line (40 wide) forward: the crossed lines (an X 40 wide, 6 m long and 3 m wide; 2.5 m wide on a lane of 3 m or less), the
        // words 铁 and 路 beside the X (2 m high, 70 wide; 1.7 m and 60 on a narrow lane), the transverse dashed line (40 wide, 60 on 60 off) across the lane; the no-overtaking line of 30 m or more
        // on each side is the lane line's business (6.1/6.2). The layout along the lane (how far the X stands from the stop line) is read off Fig. 110 as: stop line at z = 0, the dashed line 1 m
        // before the X, the words level with the X's middle.
        public static List<MarkingShape> LevelCrossing(float laneWidth)
        {
            var shapes = new List<MarkingShape>();
            if (!(laneWidth > 0f))
                return shapes;
            bool narrow = laneWidth <= 3f;
            float xw = narrow ? 2.5f : 3f, charH = narrow ? 1.7f : 2f, charW = narrow ? 0.6f : 0.7f;
            var shape = new MarkingShape { Colour = MarkingColour.White };
            // The stop line across the lane at z = 0 (40 wide), the transverse dashed line 2 m on, the X from 4 m to 10 m.
            RoadMarkings.Rect(shape.Mesh, -laneWidth * 0.5f, -0.4f, laneWidth * 0.5f, 0f);
            MarkingFigures.Append(shape, RoadMarkings.Dashed(new List<Vec3> { P(-laneWidth * 0.5f, 2f), P(laneWidth * 0.5f, 2f) }, new DashSpec(0.6f, 0.6f, 0.4f, MarkingColour.White)));
            float z0 = 4f, z1 = 10f;
            RoadMarkings.Ribbon(shape.Mesh, new List<Vec3> { P(-xw * 0.5f, z0), P(xw * 0.5f, z1) }, 0.4f);
            RoadMarkings.Ribbon(shape.Mesh, new List<Vec3> { P(xw * 0.5f, z0), P(-xw * 0.5f, z1) }, 0.4f);
            float mid = (z0 + z1) * 0.5f;
            shape.Texts.Add(new MarkingText { Text = "铁", X = -xw * 0.5f - 0.2f - charW * 0.5f, Z = mid, Width = charW, Length = charH });
            shape.Texts.Add(new MarkingText { Text = "路", X = xw * 0.5f + 0.2f + charW * 0.5f, Z = mid, Width = charW, Length = charH });
            shapes.Add(shape);
            return shapes;
        }

        // ---- 7.2.2 接近障碍物标线 (Figs. 98 to 100): round an obstacle in the carriageway, an outline (the colour and width of the lane line it continues) 30 to 60 from the obstacle, filled yellow
        // with diagonals when the obstacle sits on the centre line (opposing traffic), white with V lines when between lanes the same way; the lines 45 wide 1 m apart at 45 degrees. The area is a
        // triangle from the taper's start to the obstacle's near face: the obstacle obstacleWidth wide at z = length, the point at z = 0.
        public static MarkingShape ObstacleApproach(float length, float obstacleWidth, bool opposing, float outlineWidth = 0.15f, float clearance = 0.3f)
        {
            if (!(length > 0f) || !(obstacleWidth > 0f))
                return new MarkingShape { Colour = opposing ? MarkingColour.Yellow : MarkingColour.White };
            float c = Math.Max(0.3f, Math.Min(0.6f, clearance));
            float half = obstacleWidth * 0.5f + c;
            var area = new List<Vec3> { P(0f, 0f), P(half, length), P(-half, length) };
            return Channelising(area, outlineWidth, opposing ? ChannelFill.Diagonal : ChannelFill.Chevron, opposing ? MarkingColour.Yellow : MarkingColour.White, 0f, 0.45f);
        }
    }
}
