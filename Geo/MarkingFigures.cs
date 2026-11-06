using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The five forms of the bus bay marking of Fig. 32 (线25): a) dashed tapers and a solid line; b) dashed tapers and a hatched band; c) solid line, zigzags and the words; d) hatched band, zigzags and the words; e) dashed all along, zigzags and the words.</summary>
    public enum BusBayForm
    {
        A,
        B,
        C,
        D,
        E
    }

    /// <summary>
    /// The figures of GB 5768.3-2025 whose dimensions are only in the drawings (read from the pages, noted where construed): the diamond of 线11, the pedestrian arrow of Fig. 17, the parking
    /// bays (线14 to 线24) and the arrow in a bay (线17), the bus bays (线25) and the kerb-side stop (线26), the humps (线27 to 线29), the no-parking lines on the pavement (线49, 线51), the centre
    /// circles (线55, 线56), the grids (线57, 线58), the variable guide lane line (线62) and the forbidden-turn marks (线63, 线64). Frame: X right, Z the way the traffic runs. Pure.
    /// </summary>
    public static class MarkingFigures
    {
        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        // ---- 5.8.6 线11 人行横道预告标识线 (Fig. 15): a white diamond 150 wide and 300 long, its band 20 wide; the point toward the driver at z = 0.
        public static MarkingShape Diamond()
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            MarkingFills.Outline(shape.Mesh, new List<Vec3> { P(0f, 0f), P(0.75f, 1.5f), P(0f, 3f), P(-0.75f, 1.5f) }, 0.2f);
            return shape;
        }

        // ---- 5.8.8 Fig. 17: the arrow that splits a wide crosswalk, 1 m long (5.8.8): shaft 15 wide and 60 long, head 40 long and 15 wider than the shaft each side; drawn along +Z from z = 0.
        public static MarkingShape PedestrianArrow()
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            Polygon.Fill(shape.Mesh, new List<Vec3> { P(-0.075f, 0f), P(-0.075f, 0.6f), P(-0.225f, 0.6f), P(0f, 1f), P(0.225f, 0.6f), P(0.075f, 0.6f), P(0.075f, 0f) });
            return shape;
        }

        // ---- 5.10.4 线17 (Fig. 23): the arrow in a bay, 120 long: shaft 6 wide, head 48 long and 6 wider each side; its tip 50 below the bay's far edge.
        public const float BayArrowTipFromEdge = 0.5f;

        public static MarkingShape BayArrow()
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            Polygon.Fill(shape.Mesh, new List<Vec3> { P(-0.03f, 0f), P(-0.03f, 0.72f), P(-0.09f, 0.72f), P(0f, 1.2f), P(0.09f, 0.72f), P(0.03f, 0.72f), P(0.03f, 0f) });
            return shape;
        }

        // ---- 5.10.2/5.10.3 线14 to 线16, 5.10.5 线18/线19, 5.10.8 线22 to 线24: a row of bays along a kerb that runs +Z at x = 0, the bays on its left (toward -X, the carriageway).
        /// <summary>The widths of the bay lines (5.10.2): 6 to 10 cm; 10 for the time-limited bays (5.10.8).</summary>
        public static readonly float[] BayLineWidths = { 0.06f, 0.08f, 0.10f };

        /// <summary>
        /// The dividers leave the kerb every pitch (m) at angleDeg from the kerb's direction (90: perpendicular bays, and parallel bays with the pitch as the bay's length; 30 to 60: angled bays,
        /// 5.10.3 b), each depth m long; the outer line joins their ends; the kerb-side line is drawn only when kerbLine (5.10.3: it may be left out). Solid when side.Off is 0, else dashed
        /// (线19 taxi stand: 50 on 25 off, read off Fig. 25; 线22 to 线24: 60 on 60 off, 10 wide, read off Figs. 28 to 30).
        /// </summary>
        public static MarkingShape BayRow(int count, float pitch, float depth, float angleDeg, LineSide side, bool kerbLine = false)
        {
            var shape = new MarkingShape { Colour = side.Colour };
            if (count < 1 || !(pitch > 0f) || !(depth > 0f) || float.IsNaN(angleDeg) || angleDeg <= 0f || angleDeg > 90f || !(side.Width > 0f))
                return shape;
            double a = angleDeg * Math.PI / 180.0;
            var dir = new Vec3(-(float)Math.Sin(a), 0f, (float)Math.Cos(a));
            var spec = new DashSpec(side.Off > 0f ? side.On : 1f, side.Off > 0f ? side.Off : 0f, side.Width, side.Colour);
            for (int i = 0; i <= count; i++)
            {
                Vec3 foot = P(0f, i * pitch);
                Append(shape, RoadMarkings.Dashed(new List<Vec3> { foot, foot + dir * depth }, spec));
            }

            Vec3 far0 = dir * depth, far1 = P(0f, count * pitch) + dir * depth;
            Append(shape, RoadMarkings.Dashed(new List<Vec3> { far0, far1 }, spec));
            if (kerbLine)
                Append(shape, RoadMarkings.Dashed(new List<Vec3> { P(0f, 0f), P(0f, count * pitch) }, spec));
            return shape;
        }

        /// <summary>The centre of bay i of a row (for its arrow, its word or its figure) and the direction its length runs (from the kerb into the carriageway).</summary>
        public static Vec3 BayCentre(int i, float pitch, float depth, float angleDeg, out Vec3 along)
        {
            double a = angleDeg * Math.PI / 180.0;
            along = new Vec3(-(float)Math.Sin(a), 0f, (float)Math.Cos(a));
            return P(0f, (i + 0.5f) * pitch) + along * (depth * 0.5f);
        }

        /// <summary>The words of a taxi stand (线18/线19, Fig. 24): each character 80 wide and 120 high, 50 apart, stacked along the bay from its nearest end, centred across; the same for 校车, 公交车, 网约车 (5.10.5).</summary>
        public const float BayWordWidth = 0.8f, BayWordHeight = 1.2f, BayWordGap = 0.5f;

        /// <summary>The time of a time-limited bay (线22 to 线24, 5.10.8): digits 60 high, as one box of the given text.</summary>
        public const float BayTimeHeight = 0.6f;

        public static List<MarkingText> BayWords(string text, Vec3 centre, float width, float height, float gap) => BayWords(text, centre, width, height, gap, P(0f, 1f));

        /// <summary>The words of a bay read along a direction (a unit vector in the ground plane; the way the characters' tops point): the first character is the nearest, the box turned to it.</summary>
        public static List<MarkingText> BayWords(string text, Vec3 centre, float width, float height, float gap, Vec3 far)
        {
            var list = new List<MarkingText>();
            if (string.IsNullOrEmpty(text))
                return list;
            Vec3 axis = far.HeadingXZ();
            float yaw = (float)Math.Atan2(axis.X, axis.Z);
            int n = text.Length;
            float total = n * height + (n - 1) * gap;
            for (int i = 0; i < n; i++)
            {
                if (yaw != 0f)
                {
                    float along = -total * 0.5f + height * 0.5f + i * (height + gap);
                    list.Add(new MarkingText { Text = text.Substring(i, 1), X = centre.X + axis.X * along, Z = centre.Z + axis.Z * along, Width = width, Length = height, Yaw = yaw });
                    continue;
                }

                // 5.14.1 arranges the characters from near to far along the traffic: the first is the nearest, the lowest z (Figs. 24 and 33: the picture reads 车 交 公 from its top).
                float z = centre.Z - total * 0.5f + height * 0.5f + i * (height + gap);
                list.Add(new MarkingText { Text = text.Substring(i, 1), X = centre.X, Z = z, Width = width, Length = height });
            }

            return list;
        }

        // ---- 5.10.6 线20 (Fig. 26) and 5.10.9 (Fig. 31): the hatched zone beside a disabled bay (120 wide, 45 degrees, lines 6 to 10 wide, 1 m apart) and the filled space between bays (the same lines).
        public const float DisabledZoneWidth = 1.2f;

        public static MarkingShape HatchedZone(float x0, float z0, float x1, float z1, float lineWidth = 0.08f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            float w = RoadMarkings.NearestOf(BayLineWidths, lineWidth);
            List<Vec3> rect = MarkingFills.Rectangle(Math.Min(x0, x1), Math.Min(z0, z1), Math.Max(x0, x1), Math.Max(z0, z1));
            MarkingFills.Outline(shape.Mesh, rect, w);
            MarkingFills.Hatch(shape.Mesh, rect, w, 1f, 45f);
            return shape;
        }

        // ---- 5.10.7 线21 (Fig. 27): the bicycle parking area, a rectangle 6 to 10 wide; the bicycle figure (线35) goes inside (the caller adds it).
        public static MarkingShape BicycleBay(float width, float length, float lineWidth = 0.08f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(width > 0f) || !(length > 0f))
                return shape;
            MarkingFills.Outline(shape.Mesh, MarkingFills.Rectangle(-width * 0.5f, 0f, width * 0.5f, length), RoadMarkings.NearestOf(BayLineWidths, lineWidth));
            return shape;
        }

        // ---- 5.11.3 to 5.11.5 线25 港湾式停靠站标线 (Fig. 32): the bay on the right of the lane, its kerb at x = depth; the lane edge at x = 0; the tapers taperIn (25 m or more, 5.11.4) and taperOut, the
        // straight part between them. All white: the taper line dashed 100/100 and 45 wide; the solid line 45; the hatched band 45-wide lines 1 m apart at 45 degrees (its width across is this
        // mod's 1 m: the figure draws it without a dimension); the zigzags (实折线) 20 wide at a 60-degree apex with a bar across each end, 50 from the end; the words 100 wide and 250 high, 200 apart.
        public const float BayTaperMin = 25f, BusWordWidth = 1f, BusWordHeight = 2.5f, BusWordGap = 2f, BusZigzagWidth = 0.2f, BusZigzagEnd = 0.5f, BusBandWidth = 1f;

        public static float BusStopMinimumLength(float depth, float edgeWidth, string words)
        {
            int count = string.IsNullOrEmpty(words) ? 0 : words.Length;
            float text = count * BusWordHeight + Math.Max(0, count - 1) * BusWordGap;
            float swing = Math.Max(0.2f, depth - 0.1f - edgeWidth - 2f * BusZigzagWidth);
            return text + 2f * (BusZigzagEnd + BusWordGap + BusZigzagWidth * 0.5f + 3f * swing / (float)Math.Sqrt(3.0));
        }

        public static List<MarkingShape> BusBay(BusBayForm form, float taperIn, float straight, float taperOut, float depth, string words = "公交车")
        {
            var shapes = new List<MarkingShape>();
            if (!(taperIn > 0f) || !(straight > 0f) || !(taperOut > 0f) || !(depth > 0f))
                return shapes;
            if (form == BusBayForm.C || form == BusBayForm.D || form == BusBayForm.E)
                straight = Math.Max(straight, BusStopMinimumLength(depth, 0.45f, words));
            float z1 = taperIn, z2 = taperIn + straight, z3 = taperIn + straight + taperOut;
            var lane = new MarkingShape { Colour = MarkingColour.White };
            var dashed = new DashSpec(1f, 1f, 0.45f, MarkingColour.White);
            var solid = new DashSpec(1f, 0f, 0.45f, MarkingColour.White);
            // The tapers: dashed, except in form e where the whole line is dashed (the tapers included).
            Append(lane, RoadMarkings.Dashed(new List<Vec3> { P(depth, 0f), P(0f, z1) }, dashed));
            Append(lane, RoadMarkings.Dashed(new List<Vec3> { P(0f, z2), P(depth, z3) }, dashed));
            switch (form)
            {
                case BusBayForm.A:
                case BusBayForm.C:
                    Append(lane, RoadMarkings.Dashed(new List<Vec3> { P(0f, z1), P(0f, z2) }, solid));
                    break;
                case BusBayForm.E:
                    Append(lane, RoadMarkings.Dashed(new List<Vec3> { P(0f, z1), P(0f, z2) }, dashed));
                    break;
                default:
                    {
                        // b) and d): a band along the lane side, its two edges 45 wide, hatched 45-wide at 1 m, 45 degrees, tapering into the dashed line at both ends.
                        var band = new List<Vec3> { P(0f, z1), P(0.225f, z1 + BusBandWidth), P(0.225f + BusBandWidth, z1 + BusBandWidth), P(0.225f + BusBandWidth, z2 - BusBandWidth), P(0.225f, z2 - BusBandWidth), P(0f, z2) };
                        MarkingFills.Outline(lane.Mesh, band, 0.225f);
                        MarkingFills.Hatch(lane.Mesh, band, 0.45f, 1f, 45f);
                        break;
                    }
            }

            shapes.Add(lane);
            if (form == BusBayForm.C || form == BusBayForm.D || form == BusBayForm.E)
                shapes.Add(StopWordsAndZigzags(straight, z1, depth, 0.45f, words));
            return shapes;
        }

        // ---- 5.11.7 线26 路边式停靠站标线 (Fig. 33): the lane-side edge line 20 wide along the stop (length m, from z = 0), the zigzags 20 wide at both ends, the words in the middle; the kerb at x = depth.
        public static List<MarkingShape> KerbStop(float length, float depth, string words = "公交车")
        {
            var shapes = new List<MarkingShape>();
            if (!(length > 0f) || !(depth > 0f))
                return shapes;
            length = Math.Max(length, BusStopMinimumLength(depth, 0.2f, words));
            var edge = new MarkingShape { Colour = MarkingColour.White };
            Append(edge, RoadMarkings.Dashed(new List<Vec3> { P(0f, 0f), P(0f, length) }, new DashSpec(1f, 0f, 0.2f, MarkingColour.White)));
            shapes.Add(edge);
            shapes.Add(StopWordsAndZigzags(length, 0f, depth, 0.2f, words));
            return shapes;
        }

        /// <summary>
        /// The inside of a stop from z0 over length: the words (100 x 250, 200 apart, the first character nearest) in the middle, and at each end a zigzag region of at least three
        /// legs at a 60-degree apex swinging between the lane-side line and the kerb, closed by a bar across, 50 from the ends of the stop (the figure: bars, then the zigzag).
        /// </summary>
        private static MarkingShape StopWordsAndZigzags(float length, float z0, float depth, float lineWidth, string words)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            float inner0 = lineWidth, inner1 = depth - 0.1f;
            // At a 60-degree turn the mitre extends a full stroke width beyond the centreline.
            float swing = Math.Max(0.2f, inner1 - inner0 - 2f * BusZigzagWidth);
            float leg = swing / (float)Math.Sqrt(3.0);
            int wordsCount = string.IsNullOrEmpty(words) ? 0 : words.Length;
            float wordsLength = wordsCount * BusWordHeight + Math.Max(0, wordsCount - 1) * BusWordGap;
            float zigLength = Math.Max(0f, (length - wordsLength - 2f * BusZigzagEnd - 2f * BusWordGap - BusZigzagWidth) * 0.5f);
            int legs = Math.Max(3, (int)Math.Floor((zigLength + 1e-5f) / leg));
            foreach (float start in new[] { z0 + BusZigzagEnd, z0 + length - BusZigzagEnd - legs * leg })
            {
                var pts = new List<Vec3>();
                for (int k = 0; k <= legs; k++)
                    pts.Add(P(k % 2 == 0 ? inner0 + BusZigzagWidth : inner1 - BusZigzagWidth, start + k * leg));
                MarkingLines.ZigzagRibbon(shape.Mesh, pts, BusZigzagWidth);
                // The bars across at both ends of the zigzag.
                RoadMarkings.Rect(shape.Mesh, inner0, start - BusZigzagWidth * 0.5f, inner1, start + BusZigzagWidth * 0.5f);
                RoadMarkings.Rect(shape.Mesh, inner0, start + legs * leg - BusZigzagWidth * 0.5f, inner1, start + legs * leg + BusZigzagWidth * 0.5f);
            }

            if (wordsCount > 0)
                shape.Texts.AddRange(BayWords(words, P((inner0 + inner1) * 0.5f, z0 + length * 0.5f), BusWordWidth, BusWordHeight, BusWordGap));
            return shape;
        }

        // ---- 5.12.2 线27 大型减速丘标线 (Fig. 35): a white chevron 180 wide and 180 long, its strokes 30 wide, the apex the way the traffic runs (Figs. 36 and 39); from z = 0.
        public static MarkingShape HumpChevron()
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            // The arms rise at atan(180 / 90) from the horizontal; a stroke 30 wide perpendicular is 30 / cos of that from the vertical... in the plane: inner apex 30 / sin(angle) below the outer, inner feet 30 / cos(angle) inside.
            double ang = Math.Atan2(1.8, 0.9);
            float dz = (float)(0.3 / Math.Sin(ang)), dx = (float)(0.3 / Math.Cos(ang));
            Polygon.Fill(shape.Mesh, new List<Vec3> { P(-0.9f, 0f), P(0f, 1.8f), P(0.9f, 0f), P(0.9f - dx, 0f), P(0f, 1.8f - dz), P(-0.9f + dx, 0f) });
            return shape;
        }

        // ---- 5.12.2 线28 小型减速丘标线 (Figs. 37, 38): a row of white triangles 30 wide and 45 long, 60 apart (read off Fig. 37: the gap equals the base), across the road on each side of the hump,
        // the apex toward the traffic that approaches (Fig. 38: both rows point away from the hump); from z = 0 (the apex) to the base at 0.45 on the hump's side.
        public static MarkingShape SmallHumpTriangles(int count = 5)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (count < 1)
                return shape;
            float x = -(count - 1) * 0.3f;
            for (int i = 0; i < count; i++, x += 0.6f)
                Polygon.Fill(shape.Mesh, new List<Vec3> { P(x, 0f), P(x + 0.15f, 0.45f), P(x - 0.15f, 0.45f) });
            return shape;
        }

        // ---- 5.12.5 线29 减速丘预告标记 (Fig. 40): white bars 30 thick across the lane centre, 30 to 240 wide in steps of 30 toward the hump, their spacing shrinking (610, 540, 490, 420, 370, 300, 240 between successive bars, read off the figure as centre to centre); the nearest bar 1 m before the hump (5.12.5). From z = 0 (the farthest, narrowest bar) toward the hump.
        public static readonly float[] HumpWarningSpacing = { 6.1f, 5.4f, 4.9f, 4.2f, 3.7f, 3.0f, 2.4f };

        public static MarkingShape HumpWarningBars()
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            float z = 0f;
            for (int i = 0; i < 8; i++)
            {
                float width = 0.3f * (i + 1);
                RoadMarkings.Rect(shape.Mesh, -width * 0.5f, z - 0.15f, width * 0.5f, z + 0.15f);
                if (i < HumpWarningSpacing.Length)
                    z += HumpWarningSpacing[i];
            }

            return shape;
        }

        /// <summary>The length of 线29 from the first bar's centre to the last's.</summary>
        public static float HumpWarningLength()
        {
            float z = 0f;
            foreach (float s in HumpWarningSpacing)
                z += s;
            return z;
        }

        // ---- 6.3.1 线49 路面上禁止车辆停放线 (Fig. 72): two white lines 5 wide and 5 apart along the kerb (x = 0, the lines on its left at x -0.15..0), closed by white bars 5 wide at both ends;
        // 6.3.2 线51 (Fig. 74): one white line 5 wide, with bars 5 wide and 15 long (the figure: 10 at one end, up to 15 at the other) at both ends.
        public static MarkingShape NoParkingLine(float length, bool twoLines, float fromKerb = 0f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(length > 0f))
                return shape;
            float x = -fromKerb;
            if (twoLines)
            {
                RoadMarkings.Rect(shape.Mesh, x - 0.15f, 0f, x - 0.10f, length);
                RoadMarkings.Rect(shape.Mesh, x - 0.05f, 0f, x, length);
                RoadMarkings.Rect(shape.Mesh, x - 0.15f, 0f, x, 0.05f);
                RoadMarkings.Rect(shape.Mesh, x - 0.15f, length - 0.05f, x, length);
            }
            else
            {
                RoadMarkings.Rect(shape.Mesh, x - 0.05f, 0f, x, length);
                RoadMarkings.Rect(shape.Mesh, x - 0.10f, 0f, x + 0.05f, 0.05f);
                RoadMarkings.Rect(shape.Mesh, x - 0.10f, length - 0.05f, x + 0.05f, length);
            }

            return shape;
        }

        // ---- 6.7.2 线55 圆形中心圈 (Fig. 82): a white ring of diameter 1.2 m or more, its band 15 to 20 wide, filled with strips 15 to 20 wide 30 to 40 apart at 45 degrees; 线56 菱形中心圈 (Fig. 83): a
        // rhombus whose diagonals are 1.5 m or more, band 15 to 20, strips 15 to 20 wide 30 to 60 apart, parallel to the sides (each half its own way).
        public static MarkingShape CentreCircle(float diameter = 1.2f, float band = 0.15f, float gap = 0.3f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            float d = Math.Max(1.2f, diameter), b = Math.Max(0.15f, Math.Min(0.2f, band)), g = Math.Max(0.3f, Math.Min(0.4f, gap));
            var outer = new List<Vec3>();
            Polygon.Arc(outer, 0f, 0f, d * 0.5f, 0f, (float)(2 * Math.PI) * 47f / 48f, 47);
            var inner = new List<Vec3>();
            Polygon.Arc(inner, 0f, 0f, d * 0.5f - b, 0f, (float)(2 * Math.PI) * 47f / 48f, 47);
            MarkingFills.Outline(shape.Mesh, outer, b);
            MarkingFills.Hatch(shape.Mesh, inner, b, b + g, 45f, (b + g) * 0.5f);
            return shape;
        }

        public static MarkingShape CentreDiamond(float diagonal = 1.5f, float band = 0.15f, float gap = 0.3f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            float h = Math.Max(1.5f, diagonal) * 0.5f, b = Math.Max(0.15f, Math.Min(0.2f, band)), g = Math.Max(0.3f, Math.Min(0.6f, gap));
            var rhombus = new List<Vec3> { P(-h, 0f), P(0f, -h), P(h, 0f), P(0f, h) };
            MarkingFills.Outline(shape.Mesh, rhombus, b);
            float inset = b * (float)Math.Sqrt(2.0);
            var left = new List<Vec3> { P(-h + inset, 0f), P(0f, -h + inset), P(0f, h - inset) };
            var right = new List<Vec3> { P(0f, -h + inset), P(h - inset, 0f), P(0f, h - inset) };
            MarkingFills.Hatch(shape.Mesh, left, b, b + g, 45f, (b + g) * 0.5f);
            MarkingFills.Hatch(shape.Mesh, right, b, b + g, 135f, (b + g) * 0.5f);
            return shape;
        }

        // ---- 6.8.2 线57 网状线 (Fig. 84): a white outline 20 wide round the area, grid lines 10 wide at 45 degrees to it both ways, 1 to 5 m apart; 6.8.3 线58 简化网状线 (Fig. 85): the outline and the two diagonals, all 40 or 45 wide, no side over 12 m.
        public static MarkingShape Grid(float width, float length, float pitch = 1f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(width > 0f) || !(length > 0f))
                return shape;
            float p = Math.Max(1f, Math.Min(5f, pitch));
            List<Vec3> rect = MarkingFills.Rectangle(-width * 0.5f, 0f, width * 0.5f, length);
            MarkingFills.Outline(shape.Mesh, rect, 0.2f);
            MarkingFills.Hatch(shape.Mesh, rect, 0.1f, p, 45f, p * 0.5f);
            MarkingFills.Hatch(shape.Mesh, rect, 0.1f, p, 135f, p * 0.5f);
            return shape;
        }

        /// <summary>
        /// 线57 on any simple polygon (0.12.3): the outline 20 wide inside it and the grid lines 10 wide at 45 degrees to the item's frame (the road's way when it was drawn: 6.8.2 asks 45 degrees to the
        /// outline, which a box along a road has), pitch apart, cut to the polygon.
        /// </summary>
        public static MarkingShape GridArea(IList<Vec3> polygon, float pitch = 1f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (polygon == null || polygon.Count < 3 || !MarkingFills.IsSimple(polygon) || Math.Abs(Polygon.SignedArea2(polygon)) < 0.02f)
                return shape;
            float p = Math.Max(1f, Math.Min(5f, pitch));
            MarkingFills.Outline(shape.Mesh, polygon, 0.2f);
            MarkingFills.Hatch(shape.Mesh, polygon, 0.1f, p, 45f, p * 0.5f);
            MarkingFills.Hatch(shape.Mesh, polygon, 0.1f, p, 135f, p * 0.5f);
            return shape;
        }

        public const float SimpleGridMaxSide = 12f;

        public static MarkingShape SimpleGrid(float width, float length, float lineWidth = 0.4f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(width > 0f) || !(length > 0f) || width > SimpleGridMaxSide || length > SimpleGridMaxSide)
                return shape;
            float w = RoadMarkings.NearestOf(RoadMarkings.CrosswalkStripeWidths, lineWidth);
            List<Vec3> rect = MarkingFills.Rectangle(-width * 0.5f, 0f, width * 0.5f, length);
            MarkingFills.Outline(shape.Mesh, rect, w);
            var diagonals = new MeshBuilder();
            RoadMarkings.Ribbon(diagonals, new List<Vec3> { P(-width * 0.5f, 0f), P(width * 0.5f, length) }, w);
            RoadMarkings.Ribbon(diagonals, new List<Vec3> { P(width * 0.5f, 0f), P(-width * 0.5f, length) }, w);
            MarkingFills.ClipConvex(shape.Mesh, rect, diagonals);
            return shape;
        }

        // ---- 6.10.2 线62 可变导向车道线 (Fig. 90): a white solid line 15 or 20 wide with ticks 15 wide every 100 on the lane's side, each at 45 degrees reaching 30 across, leaning forward.
        public static MarkingShape VariableGuideLaneLine(IList<Vec3> centre, bool ticksOnRight, float width = 0.15f, float phase = 0f)
        {
            float w = Math.Abs(width - 0.2f) < Math.Abs(width - 0.15f) ? 0.2f : 0.15f;
            MarkingShape shape = RoadMarkings.Dashed(centre, new DashSpec(1f, 0f, w, MarkingColour.White));
            if (centre == null || centre.Count < 2)
                return shape;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(centre, at);
            // The ticks are at 0.5, 1.5 ... m of the whole line: a piece that starts phase metres along begins with the next of them.
            float first = float.IsNaN(phase) || float.IsInfinity(phase) ? 0.5f : (((0.5f - phase) % 1f) + 1f) % 1f;
            for (float s = first; s < total; s += 1f)
            {
                Vec3 p = RoadMarkings.PointAt(centre, at, s);
                int i = 0;
                while (i + 2 < centre.Count && at[i + 1] < s)
                    i++;
                Vec3 heading = (centre[i + 1] - centre[i]).HeadingXZ();
                Vec3 side = heading.RightXZ() * (ticksOnRight ? 1f : -1f);
                Vec3 root = p + side * (w * 0.5f);
                Vec3 tip = root + side * 0.3f + heading * 0.3f;
                RoadMarkings.Ribbon(shape.Mesh, new List<Vec3> { root - heading * 0.05f, tip }, 0.15f);
            }

            return shape;
        }

        // ---- 6.11 线63 禁止掉头标记 and 线64 禁止转弯标记 (Figs. 92, 93): a yellow guide arrow of the size of 5.13 and, 50 to 100 to its left, a yellow cross as wide and as long as the arrow
        // (the bars as wide as the arrow's shaft: the standard gives no width for them); the time (yellow, 5.14 sizes) below is the caller's.
        public static MarkingShape ForbiddenTurn(ArrowKind kind, ArrowSize size, float gap = 0.5f)
        {
            MarkingShape arrow = RoadArrows.Shape(kind, size);
            var shape = new MarkingShape { Colour = MarkingColour.Yellow };
            float ax0 = float.MaxValue, ax1 = float.MinValue, az1 = float.MinValue;
            foreach (Vec3 p in arrow.Mesh.Positions)
            {
                ax0 = Math.Min(ax0, p.X);
                ax1 = Math.Max(ax1, p.X);
                az1 = Math.Max(az1, p.Z);
            }

            float width = ax1 - ax0, length = az1, g = Math.Max(0.5f, Math.Min(1f, gap));
            float bar = 0.3f * RoadArrows.ScaleOf(size);
            // The cross to the left: its right edge gap from the arrow's left edge.
            float cx = ax0 - g - width * 0.5f;
            RoadMarkings.Ribbon(shape.Mesh, new List<Vec3> { P(cx - width * 0.5f, 0f), P(cx + width * 0.5f, length) }, bar);
            RoadMarkings.Ribbon(shape.Mesh, new List<Vec3> { P(cx + width * 0.5f, 0f), P(cx - width * 0.5f, length) }, bar);
            Append(shape, arrow);
            return shape;
        }

        /// <summary>Appends another shape's mesh and words to this one.</summary>
        public static void Append(MarkingShape into, MarkingShape other)
        {
            into.Mesh.Append(other.Mesh, Vec3.Zero, 0f);
            into.Texts.AddRange(other.Texts);
        }
    }
}
