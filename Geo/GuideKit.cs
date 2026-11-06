using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    public enum RoadPlateKind
    {
        /// <summary>国道: white G and number on red, white edge.</summary>
        NationalRoad,

        /// <summary>省道: black S and number on yellow, black edge.</summary>
        ProvincialRoad,

        /// <summary>国家高速公路 (Appendix C): a red strip with 国家高速 above a green plate with the white number; the ring roads' ID digits are smaller and low.</summary>
        NationalExpressway,

        /// <summary>省级高速公路 (Appendix C.2): the same, but the strip is yellow with black "X高速".</summary>
        ProvincialExpressway
    }

    /// <summary>
    /// The pieces of the boards of GB 5768.2, each built once from the standard's own rules: a place name, a distance, an arrow, a road plate, an exit number plate. Sizes are in
    /// metres and follow the figures of Appendix E, which give them in the Chinese character height h; where a figure was read off at low resolution the comment says so.
    /// </summary>
    public static class GuideKit
    {
        /// <summary>Liner and border together: 0.1 h each (Fig. 3).</summary>
        public const float EdgeK = 0.2f;

        /// <summary>The least distance from a character to the edge of the board's inside: 0.4 h (Table 6).</summary>
        public const float MarginK = 0.4f;

        /// <summary>The space between the characters of a place name: 0.2 h (the figures E.208 to E.212; Table 6 asks for at least 0.1 h).</summary>
        public const float NameGapK = 0.2f;

        // ------------------------------------------------------------------ measuring

        /// <summary>The width of a character of a number or a code, in heights of the digits (the font's advance over its cap height). Used to size boxes; the painter then fits the ink in them.</summary>
        public static float LatinAdvance(char c)
        {
            // Condensed against the font's own advance (about 0.78): the standard's lettering is narrower (digit width 1/2 h to h, Table 4), and the painter squeezes the ink to the box.
            if (c >= '0' && c <= '9') return 0.64f;
            switch (c)
            {
                case 'G': return 0.82f;
                case 'S': return 0.70f;
                case 'A': return 0.80f;
                case 'B': return 0.74f;
                case 'X': return 0.74f;
                case 'I': return 0.33f;
                case 'M': return 0.95f;
                case 'W': return 1.05f;
                case '-': return 0.46f;
                case '.': return 0.30f;
                case ':': return 0.28f;
                case ',': return 0.28f;
                case '/': return 0.46f;
                case '~': return 0.60f;
                case ' ': return 0.28f;
                case 'k': return 0.74f;
                case 'm': return 1.15f;
            }

            if (c >= 0x2E80)
                return 1.30f;
            return c >= 'A' && c <= 'Z' ? 0.74f : 0.66f;
        }

        public static float LatinWidth(string text, float capHeight)
        {
            float w = 0f;
            foreach (char c in text ?? "")
                w += LatinAdvance(c);
            return w * capHeight;
        }

        public static float CellWidth(int count, float h, float gap) => count <= 0 ? 0f : count * h + (count - 1) * gap;

        // ------------------------------------------------------------------ parts

        /// <summary>A board part for characters h high with an inside of the given size; liner and border 0.1 h each, outer radius 0.4 h (Fig. 3).</summary>
        public static GuidePart Part(float h, float innerW, float innerH, GuideColour ground, float x = 0f, float y = 0f)
        {
            float edge = EdgeK * h;
            return new GuidePart
            {
                X = x,
                Y = y,
                W = innerW + 2f * edge,
                H = innerH + 2f * edge,
                Ground = ground,
                Liner = 0.1f * h,
                Border = 0.1f * h,
                Radius = 0.4f * h
            };
        }

        // ------------------------------------------------------------------ elements

        public static GuideElement Cells(string text, float x, float y, float h, GuideColour colour, float gap, GuideAlign align = GuideAlign.Left, float width = 0f)
        {
            int n = (text ?? "").Length;
            float w = width > 0f ? width : CellWidth(n, h, gap);
            return new GuideElement { Kind = GuideElementKind.Text, Mode = GuideTextMode.Cell, Text = text, X = x, Y = y, W = w, H = h, Colour = colour, Gap = gap, Align = align };
        }

        /// <summary>Letters or digits whose ink is as high as the box. inkRef: the string whose ink height sets the scale (lower-case unit labels are scaled by the "k" so that "m" is not enlarged to the box).</summary>
        public static GuideElement Ink(string text, float x, float y, float w, float h, GuideColour colour, GuideAlign align = GuideAlign.Left, string inkRef = null)
        {
            var e = new GuideElement { Kind = GuideElementKind.Text, Mode = GuideTextMode.Ink, Text = text, X = x, Y = y, W = w, H = h, Colour = colour, Align = align };
            e.InkRef = inkRef;
            return e;
        }

        public static GuideElement Rect(float x, float y, float w, float h, GuideColour fill, float radius = 0f) =>
            new GuideElement { Kind = GuideElementKind.Rect, X = x, Y = y, W = w, H = h, Colour = fill, RadiusTop = radius, RadiusBottom = radius };

        public static GuideElement Oval(float x, float y, float w, float h, GuideColour fill) =>
            new GuideElement { Kind = GuideElementKind.Oval, X = x, Y = y, W = w, H = h, Colour = fill };

        public static GuideElement Poly(float[] points, GuideColour fill)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i + 1 < points.Length; i += 2)
            {
                x0 = Math.Min(x0, points[i]);
                x1 = Math.Max(x1, points[i]);
                y0 = Math.Min(y0, points[i + 1]);
                y1 = Math.Max(y1, points[i + 1]);
            }

            return new GuideElement { Kind = GuideElementKind.Polygon, Points = points, Colour = fill, X = x0, Y = y0, W = x1 - x0, H = y1 - y0 };
        }

        // ------------------------------------------------------------------ arrows

        /// <summary>
        /// The outline of an arrow of the standard's six (Fig. 204) as x, y pairs (y down) filling a square of the given size at (x, y). A straight arrow of a stem and a triangular head, turned
        /// to the direction; the diagonal ones are turned 45 degrees (8.1.8) and drawn as large as the square allows.
        /// </summary>
        public static float[] ArrowPolygon(GuideArrow dir, float x, float y, float size)
        {
            // Pointing up, centred on the origin, in units of the square: tip at the top, head base at 0.45 of the length, head half width 0.31, stem half width 0.11.
            float[] up =
            {
                0f, -0.5f,
                0.31f, -0.05f,
                0.11f, -0.05f,
                0.11f, 0.5f,
                -0.11f, 0.5f,
                -0.11f, -0.05f,
                -0.31f, -0.05f
            };

            double angle;
            switch (dir)
            {
                case GuideArrow.UpRight: angle = 45; break;
                case GuideArrow.Right: angle = 90; break;
                case GuideArrow.Down: angle = 180; break;
                case GuideArrow.Left: angle = 270; break;
                case GuideArrow.UpLeft: angle = 315; break;
                default: angle = 0; break;
            }

            double a = angle * Math.PI / 180.0;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            var turned = new float[up.Length];
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < up.Length; i += 2)
            {
                // Clockwise on the screen (y down): x' = x cos - y sin, y' = x sin + y cos.
                turned[i] = (float)(up[i] * ca - up[i + 1] * sa);
                turned[i + 1] = (float)(up[i] * sa + up[i + 1] * ca);
                minX = Math.Min(minX, turned[i]);
                maxX = Math.Max(maxX, turned[i]);
                minY = Math.Min(minY, turned[i + 1]);
                maxY = Math.Max(maxY, turned[i + 1]);
            }

            // Centred in the square and as large as fits (the straight arrows are as long as the square is high; the diagonal ones are drawn longer, so that they fill it too).
            float fit = 1f / Math.Max(maxX - minX, maxY - minY);
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
            var points = new float[up.Length];
            for (int i = 0; i < up.Length; i += 2)
            {
                points[i] = x + size * (0.5f + (turned[i] - cx) * fit);
                points[i + 1] = y + size * (0.5f + (turned[i + 1] - cy) * fit);
            }

            return points;
        }

        /// <summary>
        /// The 45 degree arrow of E.166 b (8.1.8 b and d) in a square of the given size at (x, y), as large as the square allows: a head a wide and a long (the tip 81 degrees, the full width
        /// 0.58 a behind the tip, the sides then parallel to the axis, the back parallel to the front), a stem 0.28 a thick, 1.6 a long (E.166 b gives the box as 1.2 a to 2 a; the drawings of the
        /// exit boards, E.213, draw it 1.6 a long, which a measure on the drawing rendered at 5000 px a page gave). The straight arrows of the framework are drawn with ArrowPolygon.
        /// </summary>
        public static float[] DiagonalArrowPolygon(bool leftward, float x, float y, float size)
        {
            const float a = 1f, length = 1.6f, t = 0.14f;
            float x1 = length * 0.5f, x0 = -x1, join = x1 - a + 0.58f * (a - 2f * t);
            float[] p =
            {
                x1, 0f, x1 - 0.58f * a, -0.5f * a, x1 - a, -0.5f * a, join, -t, x0, -t,
                x0, t, join, t, x1 - a, 0.5f * a, x1 - 0.58f * a, 0.5f * a
            };

            // Turned on the screen (y down): -45 degrees points up and to the right, -135 up and to the left.
            double r = (leftward ? -135.0 : -45.0) * Math.PI / 180.0;
            float c = (float)Math.Cos(r), sn = (float)Math.Sin(r);
            var q = new float[p.Length];
            float bx0 = float.MaxValue, by0 = float.MaxValue, bx1 = float.MinValue, by1 = float.MinValue;
            for (int i = 0; i < p.Length; i += 2)
            {
                q[i] = p[i] * c - p[i + 1] * sn;
                q[i + 1] = p[i] * sn + p[i + 1] * c;
                bx0 = Math.Min(bx0, q[i]);
                bx1 = Math.Max(bx1, q[i]);
                by0 = Math.Min(by0, q[i + 1]);
                by1 = Math.Max(by1, q[i + 1]);
            }

            float k = size / Math.Max(bx1 - bx0, by1 - by0);
            float cx = (bx0 + bx1) * 0.5f, cy = (by0 + by1) * 0.5f;
            for (int i = 0; i < q.Length; i += 2)
            {
                q[i] = x + size * 0.5f + (q[i] - cx) * k;
                q[i + 1] = y + size * 0.5f + (q[i + 1] - cy) * k;
            }

            return q;
        }

        /// <summary>An arrow in a square block: the 45 degree ones as E.166 b draws them, the straight ones as the framework's arrow.</summary>
        public static GuideBlock ArrowBlock(GuideArrow dir, float size, GuideColour colour)
        {
            var block = new GuideBlock(size, size);
            float[] outline = dir == GuideArrow.UpRight || dir == GuideArrow.UpLeft
                ? DiagonalArrowPolygon(dir == GuideArrow.UpLeft, 0f, 0f, size)
                : ArrowPolygon(dir, 0f, 0f, size);
            block.Add(Poly(outline, colour));
            return block;
        }

        // ------------------------------------------------------------------ names and distances

        /// <summary>A place name: one cell per character, h high, 0.2 h apart.</summary>
        public static GuideBlock PlaceName(string name, float h, GuideColour colour)
        {
            int n = (name ?? "").Length;
            var block = new GuideBlock(CellWidth(n, h, NameGapK * h), h);
            if (n > 0)
                block.Add(Cells(name, 0f, 0f, h, colour, NameGapK * h));
            return Bilingual.Under(block, name, h, colour);
        }

        /// <summary>The unit after a distance: "km" in letters 0.5 h high, "m" 0.4 h (the figures E.208 a and b).</summary>
        public static float UnitHeight(string unit, float h) => unit == "m" ? 0.4f * h : 0.5f * h;

        public static float UnitWidth(string unit, float h)
        {
            float u = UnitHeight(unit, h);
            return unit == "m" ? 1.26f * u : 2.04f * u;
        }

        /// <summary>A distance: the number as high as the characters (the standard asks that the digits keep their height), the unit small beside it on the same baseline.</summary>
        public static GuideBlock Distance(string value, string unit, float h, GuideColour colour)
        {
            float numberW = LatinWidth(value, h);
            float unitH = UnitHeight(unit, h), unitW = UnitWidth(unit, h);
            float gap = 0.12f * h;
            var block = new GuideBlock(numberW + gap + unitW, h);
            block.Add(Ink(value, 0f, 0f, numberW, h, colour));
            block.Add(Ink(unit, numberW + gap, h - unitH, unitW, unitH, colour, GuideAlign.Left, "k"));
            return block;
        }

        // ------------------------------------------------------------------ plates

        /// <summary>
        /// A road number plate of the given height. Roads (国道, 省道): the code in letters 0.533 of the height on red or yellow with a thin edge (the figures show the plate 1.5 h high with the
        /// code 0.8 h). Expressways (Appendix C, Fig. E.201): a strip 0.20 of the height with the road class in letters 0.1 of the height, and below it the code 0.45 of the height (table C.1) with the
        /// ring roads' two digits at 0.55 of that, low; the widths are those of table C.1 (1.00, 1.25, 1.70 of the height for one, two and four digits) or what the code needs.
        /// </summary>
        public static GuideBlock RoadPlate(RoadPlateKind kind, string code, string sub, string prov, float height)
        {
            code = code ?? "";
            sub = sub ?? "";
            float P = height;
            if (kind == RoadPlateKind.NationalRoad || kind == RoadPlateKind.ProvincialRoad)
            {
                bool red = kind == RoadPlateKind.NationalRoad;
                float cap = 0.533f * P;
                float w = LatinWidth(code, cap) + 0.2f * P;
                var plate = new GuideBlock(w, P);
                GuideElement body = Rect(0f, 0f, w, P, red ? GuideColour.Red : GuideColour.Yellow, 0.1f * P);
                body.StrokeWidth = 0.035f * P;
                body.Stroke = red ? GuideColour.White : GuideColour.Black;
                plate.Add(body);
                plate.Add(Ink(code, 0.1f * P, (P - cap) * 0.5f, w - 0.2f * P, cap, red ? GuideColour.White : GuideColour.Black, GuideAlign.Centre));
                return plate;
            }

            bool national = kind == RoadPlateKind.NationalExpressway;
            float numberCap = 0.45f * P;
            float subCap = 0.55f * numberCap;
            int digits = 0;
            foreach (char c in code + sub)
            {
                if (c >= '0' && c <= '9')
                    digits++;
            }

            // Table C.1: 100 x 100, 125 x 100, 170 x 100 cm, and at 80 km/h and less 70 x 70, 85 x 70, 115 x 70 (not 0.7 of the widths: 119 for four digits); a height between is scaled.
            float table = digits <= 1 ? 1.00f : digits == 2 ? 1.25f : digits == 3 ? 1.45f : 1.70f;
            float tableW = table * P;
            if (Math.Abs(P - 0.70f) < 0.005f)
                tableW = digits <= 1 ? 0.70f : digits == 2 ? 0.85f : digits == 3 ? 1.00f : 1.15f;
            float codeW = LatinWidth(code, numberCap), subW = LatinWidth(sub, subCap);
            float W = Math.Max(tableW, codeW + (subW > 0f ? 0.03f * P + subW : 0f) + 0.22f * P);

            var block = new GuideBlock(W, P);
            // Fig. E.201 (100 cm high; 70 high is the same scaled by 0.7): one edge of 3, the strip 20 (5 + 10 + 5) with characters of 10 cm 10 cm apart, the number's box 45 high 14 below the
            // strip and 15 above the edge (3 + 20 + 14 + 45 + 15 + 3). The corner radius is not dimensioned: 11 cm is measured on the drawing.
            float edge = 0.03f * P, radius = 0.11f * P;
            GuideElement ground = Rect(0f, 0f, W, P, GuideColour.Green, radius);
            ground.StrokeWidth = edge;
            ground.Stroke = GuideColour.White;
            block.Add(ground);

            // The strip, inside the edge, square below and rounded above to follow the plate.
            float stripH = 0.20f * P;
            GuideElement strip = Rect(edge, edge, W - 2f * edge, stripH, national ? GuideColour.Red : GuideColour.Yellow);
            strip.RadiusTop = radius - edge;
            block.Add(strip);

            string stripText = national ? "国家高速" : (string.IsNullOrEmpty(prov) ? "X" : prov) + "高速";
            float charH = 0.10f * P;
            int chars = stripText.Length;
            float gap = chars > 1 ? charH : 0f;
            float textW = CellWidth(chars, charH, gap);
            block.Add(Cells(stripText, (W - textW) * 0.5f, edge + (stripH - charH) * 0.5f, charH, national ? GuideColour.White : GuideColour.Black, gap));

            // The code: in its box, 14 under the strip; the ring roads' two digits low, after it.
            float areaTop = edge + stripH;
            float totalW = codeW + (subW > 0f ? 0.03f * P + subW : 0f);
            float x = (W - totalW) * 0.5f;
            float y = areaTop + 0.14f * P;
            block.Add(Ink(code, x, y, codeW, numberCap, GuideColour.White));
            if (subW > 0f)
                block.Add(Ink(sub, x + codeW + 0.03f * P, y + numberCap - subCap, subW, subCap, GuideColour.White));
            return block;
        }

        /// <summary>
        /// The exit number plate (路41, Fig. E.207, read off in cm and put in the digit height D): a green plate 2.0 D high (the oval 1.6 D, the digits D), 出口 in characters hc (25 or 30 cm,
        /// Table 5) at the left, a white oval with the number in green; a suffix (A, B, A-B) in letters 2/3 D after the digits; the left exit plate puts 左 above 出口.
        /// The figure's 235 x 100 cm for three digits of 50 cm is reproduced exactly.
        /// </summary>
        public static GuidePart ExitNumberPlate(float D, string number, string suffix, bool left, float x, float y)
        {
            number = number ?? "";
            suffix = suffix ?? "";
            float hc = D >= 0.45f ? 0.30f : 0.25f;
            float edge = EdgeK * hc;
            float ovalH = 1.6f * D;
            float suffixCap = D * 2f / 3f;
            float digitsW = LatinWidth(number, D) + (suffix.Length > 0 ? 0.05f * D + LatinWidth(suffix, suffixCap) : 0f);
            // The oval is wider than the digits by 0.7 D: with 0.6 D (the figure's 30 cm for 50 cm digits) the corners of the digits touch the ellipse in a font wider than the standard's.
            float ovalW = Math.Max(digitsW + 0.7f * D, ovalH);

            float leftPad = 0.30f * hc, charGap = 0.267f * hc, ovalGap = 0.333f * hc, rightPad = 0.20f * hc;
            float charsW = 2f * hc + charGap;
            float innerW = leftPad + charsW + ovalGap + ovalW + rightPad;
            float innerH = ovalH + 2f * 0.08f * D;
            // The left exit plate has 左 over 出口: two lines and a line gap of 0.2 hc must fit between the edges even when the digits are small.
            if (left)
                innerH = Math.Max(innerH, 2.2f * hc + 0.2f * hc);
            // The figure: 100 cm for D = 50 cm with a 6 cm edge: the oval has 4 cm above and below it inside the edge.
            var part = Part(hc, innerW, innerH, GuideColour.Green, x, y);
            part.Liner = 0.1f * hc;
            part.Border = 0.1f * hc;

            float ox = edge + leftPad + charsW + ovalGap;
            float oy = edge + (innerH - ovalH) * 0.5f;
            if (left)
            {
                float line = 0.2f * hc;
                float blockH = 2f * hc + line;
                float by = edge + (innerH - blockH) * 0.5f;
                part.Elements.Add(Cells("左", edge + leftPad, by, hc, GuideColour.White, 0f));
                part.Elements.Add(Cells("出口", edge + leftPad, by + hc + line, hc, GuideColour.White, charGap));
            }
            else
            {
                part.Elements.Add(Cells("出口", edge + leftPad, edge + (innerH - hc) * 0.5f, hc, GuideColour.White, charGap));
            }

            part.Elements.Add(Oval(ox, oy, ovalW, ovalH, GuideColour.White));
            float dx = ox + (ovalW - digitsW) * 0.5f;
            float baseline = oy + (ovalH + D) * 0.5f;
            float numberW = LatinWidth(number, D);
            part.Elements.Add(Ink(number, dx, baseline - D, numberW, D, GuideColour.Green));
            if (suffix.Length > 0)
                part.Elements.Add(Ink(suffix, dx + numberW + 0.05f * D, baseline - suffixCap, LatinWidth(suffix, suffixCap), suffixCap, GuideColour.Green));
            return part;
        }

        // ------------------------------------------------------------------ geometry helpers

        /// <summary>The outline of a rounded rectangle round its middle, counter-clockwise, y up (the frame of the mesh builder), with the corners cut in the given number of steps.</summary>
        public static List<Point2> RoundRect(float w, float h, float radius, int steps = 8)
        {
            radius = Math.Max(0f, Math.Min(radius, Math.Min(w, h) * 0.5f));
            var points = new List<Point2>();
            if (radius < 1e-5f)
            {
                points.Add(new Point2(-w * 0.5f, -h * 0.5f));
                points.Add(new Point2(w * 0.5f, -h * 0.5f));
                points.Add(new Point2(w * 0.5f, h * 0.5f));
                points.Add(new Point2(-w * 0.5f, h * 0.5f));
                return points;
            }

            float cx = w * 0.5f - radius, cy = h * 0.5f - radius;
            // Corners in the order lower right, upper right, upper left, lower left: counter-clockwise.
            float[] startAngles = { -90f, 0f, 90f, 180f };
            float[] centresX = { cx, cx, -cx, -cx };
            float[] centresY = { -cy, cy, cy, -cy };
            for (int corner = 0; corner < 4; corner++)
            {
                for (int i = 0; i <= steps; i++)
                {
                    double a = (startAngles[corner] + 90.0 * i / steps) * Math.PI / 180.0;
                    points.Add(new Point2(centresX[corner] + radius * (float)Math.Cos(a), centresY[corner] + radius * (float)Math.Sin(a)));
                }
            }

            return points;
        }

        /// <summary>The outline of a part round the middle of its box, counter-clockwise, y up (the frame of the mesh builder).</summary>
        public static List<Point2> PartOutline(GuidePart part)
        {
            switch (part.Shape)
            {
                case GuidePartShape.Circle:
                {
                    var points = new List<Point2>();
                    const int n = 64;
                    float r = part.W * 0.5f;
                    for (int i = 0; i < n; i++)
                    {
                        double a = 2.0 * Math.PI * i / n;
                        points.Add(new Point2((float)Math.Cos(a) * r, (float)Math.Sin(a) * r));
                    }

                    return points;
                }
                case GuidePartShape.Octagon:
                    return OctagonOutline(part.W);
                case GuidePartShape.Triangle:
                    return TriangleOutlineIn(part.Side > 0f ? part.Side : part.W, part.Radius, true);
                case GuidePartShape.TriangleDown:
                    return TriangleOutlineIn(part.Side > 0f ? part.Side : part.W, part.Radius, false);
                case GuidePartShape.PointRight:
                case GuidePartShape.PointLeft:
                    return PointedOutline(part.W, part.H, part.Side, part.Radius, 0f, part.Shape == GuidePartShape.PointRight);
                default:
                    return RoundRect(part.W, part.H, part.Radius);
            }
        }

        /// <summary>
        /// The outline of a board pointed at one end (路7 a), round the middle of its box, counter-clockwise, y up, moved in by inset (the liner and the border are drawn as these outlines moved in): the
        /// far corners rounded by radius, the corners of the point and its tip by 0.4 of it (Fig. 212 a shows a sharper tip than the board's corners), each radius less the inset.
        /// </summary>
        public static List<Point2> PointedOutline(float w, float h, float point, float radius, float inset, bool toRight)
        {
            point = Math.Max(0f, Math.Min(point, w * 0.5f));
            float s = toRight ? 1f : -1f;
            // The sharp outline, counter-clockwise for the board pointed to the right (mirrored for the left, the order turned so that it stays counter-clockwise).
            var sharp = new List<Point2>
            {
                new Point2(-w * 0.5f, -h * 0.5f), new Point2(w * 0.5f - point, -h * 0.5f), new Point2(w * 0.5f, 0f), new Point2(w * 0.5f - point, h * 0.5f), new Point2(-w * 0.5f, h * 0.5f)
            };
            if (!toRight)
            {
                var mirrored = new List<Point2>();
                for (int i = sharp.Count - 1; i >= 0; i--)
                    mirrored.Add(new Point2(-sharp[i].X, sharp[i].Y));
                sharp = mirrored;
            }

            List<Point2> moved = inset > 0f ? InsetConvex(sharp, inset) : sharp;
            float far = Math.Max(0f, radius - inset), near = Math.Max(0f, 0.4f * radius - inset);
            var radii = new float[moved.Count];
            for (int i = 0; i < moved.Count; i++)
                radii[i] = s * moved[i].X < 0f ? far : near;
            return Fillet(moved, radii);
        }

        /// <summary>A convex polygon (counter-clockwise, y up) moved in by d: each side moved inward along its normal, and the corners where the moved sides meet.</summary>
        public static List<Point2> InsetConvex(IList<Point2> polygon, float d)
        {
            int n = polygon.Count;
            var result = new List<Point2>(n);
            for (int i = 0; i < n; i++)
            {
                Point2 a = polygon[(i + n - 1) % n], b = polygon[i], c = polygon[(i + 1) % n];
                // The side a-b and the side b-c, each moved left (inward for counter-clockwise) by d; their crossing is the new corner.
                Normal(a, b, out float n1x, out float n1y);
                Normal(b, c, out float n2x, out float n2y);
                float ax = a.X + n1x * d, ay = a.Y + n1y * d, dx1 = b.X - a.X, dy1 = b.Y - a.Y;
                float bx = b.X + n2x * d, by = b.Y + n2y * d, dx2 = c.X - b.X, dy2 = c.Y - b.Y;
                float den = dx1 * dy2 - dy1 * dx2;
                if (Math.Abs(den) < 1e-9f)
                {
                    result.Add(new Point2(b.X + n2x * d, b.Y + n2y * d));
                    continue;
                }

                float t = ((bx - ax) * dy2 - (by - ay) * dx2) / den;
                result.Add(new Point2(ax + dx1 * t, ay + dy1 * t));
            }

            return result;
        }

        private static void Normal(Point2 a, Point2 b, out float nx, out float ny)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
            nx = len > 1e-9f ? -dy / len : 0f;
            ny = len > 1e-9f ? dx / len : 0f;
        }

        /// <summary>
        /// A convex polygon (counter-clockwise) with each corner rounded by an arc of its radius (radii[i] for corner i; 0 leaves it sharp), tangent to both sides; a radius is held to what half of the shorter
        /// side allows. Eight steps a quarter turn.
        /// </summary>
        public static List<Point2> Fillet(IList<Point2> polygon, IList<float> radii, int steps = 8)
        {
            int n = polygon.Count;
            var result = new List<Point2>();
            for (int i = 0; i < n; i++)
            {
                Point2 a = polygon[(i + n - 1) % n], v = polygon[i], b = polygon[(i + 1) % n];
                float r = i < radii.Count ? radii[i] : 0f;
                float ux = a.X - v.X, uy = a.Y - v.Y, wx = b.X - v.X, wy = b.Y - v.Y;
                float lu = (float)Math.Sqrt(ux * ux + uy * uy), lw = (float)Math.Sqrt(wx * wx + wy * wy);
                if (!(r > 1e-6f) || lu < 1e-6f || lw < 1e-6f)
                {
                    result.Add(v);
                    continue;
                }

                ux /= lu;
                uy /= lu;
                wx /= lw;
                wy /= lw;
                double theta = Math.Acos(Math.Max(-1.0, Math.Min(1.0, ux * wx + uy * wy)));
                if (theta < 1e-4 || theta > Math.PI - 1e-4)
                {
                    result.Add(v);
                    continue;
                }

                double tanHalf = Math.Tan(theta * 0.5);
                double t = Math.Min(r / tanHalf, Math.Min(lu, lw) * 0.5);
                double rr = t * tanHalf;
                float bxs = ux + wx, bys = uy + wy, bl = (float)Math.Sqrt(bxs * bxs + bys * bys);
                double dist = rr / Math.Sin(theta * 0.5);
                double cx = v.X + bxs / bl * dist, cy = v.Y + bys / bl * dist;
                double t1x = v.X + ux * t, t1y = v.Y + uy * t, t2x = v.X + wx * t, t2y = v.Y + wy * t;
                double a1 = Math.Atan2(t1y - cy, t1x - cx), a2 = Math.Atan2(t2y - cy, t2x - cx);
                while (a2 < a1)
                    a2 += 2.0 * Math.PI;
                double sweep = a2 - a1;
                int count = Math.Max(1, (int)Math.Ceiling(sweep / (Math.PI * 0.5) * steps));
                for (int k = 0; k <= count; k++)
                {
                    double ang = a1 + sweep * k / count;
                    result.Add(new Point2((float)(cx + rr * Math.Cos(ang)), (float)(cy + rr * Math.Sin(ang))));
                }
            }

            return result;
        }

        /// <summary>A regular octagon with a distance of across between two parallel sides, a side on each of the four directions of the axes.</summary>
        public static List<Point2> OctagonOutline(float across)
        {
            var points = new List<Point2>();
            float r = across * 0.5f / (float)Math.Cos(Math.PI / 8);
            for (int i = 0; i < 8; i++)
            {
                double a = Math.PI / 8 + Math.PI / 4 * i;
                points.Add(new Point2((float)Math.Cos(a) * r, (float)Math.Sin(a) * r));
            }

            return points;
        }

        /// <summary>
        /// An equilateral triangle with rounded corners round the middle of its box (the box is side x side * sqrt(3) / 2), counter-clockwise, y up. The corners are circular arcs of the
        /// given radius (the standard's R, Fig. 2 and Table 8). inset: the outline of the triangle that lies this far inside (the distance between parallel sides, measured at right angles):
        /// the inner edge of a border. The inner outline has the centre of gravity of the outer one, so it is not at the middle of the box.
        /// </summary>
        public static List<Point2> TriangleOutline(float side, float radius, bool apexUp, float inset = 0f)
        {
            float height = side * (float)(Math.Sqrt(3.0) / 2.0);
            float inRadius = side / (2f * (float)Math.Sqrt(3.0));
            float innerSide = inset <= 0f ? side : side * Math.Max(0.01f, (inRadius - inset) / inRadius);
            float innerHeight = innerSide * (float)(Math.Sqrt(3.0) / 2.0);
            float r = Math.Max(0f, radius - inset);
            float dir = apexUp ? 1f : -1f;

            // Corners of the (inner) triangle in its own box, counter-clockwise: apex, then the two base corners.
            var corners = new[]
            {
                new Point2(0f, dir * innerHeight * 0.5f),
                new Point2(-innerSide * 0.5f, -dir * innerHeight * 0.5f),
                new Point2(innerSide * 0.5f, -dir * innerHeight * 0.5f)
            };
            if (!apexUp)
            {
                // Apex down: the same corners in the order that keeps the turn counter-clockwise.
                corners = new[] { corners[0], corners[2], corners[1] };
            }

            float shift = inset <= 0f ? 0f : dir * (innerHeight - height) / 6f;
            var outline = new List<Point2>();
            const int steps = 8;
            for (int c = 0; c < 3; c++)
            {
                Point2 v = corners[c], p = corners[(c + 2) % 3], n = corners[(c + 1) % 3];
                if (r < 1e-5f)
                {
                    outline.Add(new Point2(v.X, v.Y + shift));
                    continue;
                }

                float pdx = p.X - v.X, pdy = p.Y - v.Y, plen = (float)Math.Sqrt(pdx * pdx + pdy * pdy);
                float ndx = n.X - v.X, ndy = n.Y - v.Y, nlen = (float)Math.Sqrt(ndx * ndx + ndy * ndy);
                pdx /= plen; pdy /= plen; ndx /= nlen; ndy /= nlen;
                // The interior angle is 60 degrees: the tangent points are r / tan(30 deg) from the corner, the centre of the arc 2 r along the bisector.
                float t = r * (float)Math.Sqrt(3.0);
                float bx = pdx + ndx, by = pdy + ndy, blen = (float)Math.Sqrt(bx * bx + by * by);
                bx /= blen; by /= blen;
                float ox = v.X + bx * 2f * r, oy = v.Y + by * 2f * r;
                double a1 = Math.Atan2(v.Y + pdy * t - oy, v.X + pdx * t - ox);
                double a2 = Math.Atan2(v.Y + ndy * t - oy, v.X + ndx * t - ox);
                while (a2 <= a1)
                    a2 += 2.0 * Math.PI;
                for (int s = 0; s <= steps; s++)
                {
                    double a = a1 + (a2 - a1) * s / steps;
                    outline.Add(new Point2(ox + r * (float)Math.Cos(a), oy + r * (float)Math.Sin(a) + shift));
                }
            }

            return outline;
        }

        /// <summary>The real box of a rounded triangle's outline (its corners are cut by the rounding, so it is a little smaller than side x side * sqrt(3) / 2), and where its middle is in the nominal frame.</summary>
        public static void TriangleBounds(float side, float radius, bool apexUp, out float centreX, out float centreY, out float width, out float height)
        {
            List<Point2> outline = TriangleOutline(side, radius, apexUp);
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (Point2 p in outline)
            {
                x0 = Math.Min(x0, p.X);
                x1 = Math.Max(x1, p.X);
                y0 = Math.Min(y0, p.Y);
                y1 = Math.Max(y1, p.Y);
            }

            centreX = (x0 + x1) * 0.5f;
            centreY = (y0 + y1) * 0.5f;
            width = x1 - x0;
            height = y1 - y0;
        }

        /// <summary>A rounded triangle's outline (inset: the inner edge of a border) in the frame centred on the box of the OUTER outline, which is the box of the part.</summary>
        public static List<Point2> TriangleOutlineIn(float side, float radius, bool apexUp, float inset = 0f)
        {
            TriangleBounds(side, radius, apexUp, out float cx, out float cy, out _, out _);
            List<Point2> outline = TriangleOutline(side, radius, apexUp, inset);
            var moved = new List<Point2>(outline.Count);
            foreach (Point2 p in outline)
                moved.Add(new Point2(p.X - cx, p.Y - cy));
            return moved;
        }

        /// <summary>An outline (box-centre coordinates, y up) as x, y pairs in the frame of a part whose box is w x h (y down).</summary>
        public static float[] ToPartFrame(IList<Point2> outline, float w, float h)
        {
            var points = new float[outline.Count * 2];
            for (int i = 0; i < outline.Count; i++)
            {
                points[i * 2] = w * 0.5f + outline[i].X;
                points[i * 2 + 1] = h * 0.5f - outline[i].Y;
            }

            return points;
        }

        private static readonly Dictionary<string, List<float[]>> s_Paths = new Dictionary<string, List<float[]>>();

        /// <summary>The contours of a path, read once and kept (a pictogram is used by many boards and many sizes).</summary>
        public static List<float[]> Contours(string pathData)
        {
            lock (s_Paths)
            {
                if (!s_Paths.TryGetValue(pathData, out List<float[]> contours))
                    s_Paths[pathData] = contours = GuidePath.Parse(pathData);
                return contours;
            }
        }

        /// <summary>A pictogram: the path drawn in a 100 x 100 box, put into the box x, y, w, h (the box of the 100 x 100 maps exactly onto it).</summary>
        public static GuideElement Picture(string pathData, float x, float y, float w, float h, GuideColour colour) => new GuideElement
        {
            Kind = GuideElementKind.Path, Contours = GuidePath.Map(Contours(pathData), w / 100f, h / 100f, x, y), X = x, Y = y, W = w, H = h, Colour = colour
        };

        /// <summary>A pictogram scaled evenly so that its own bounds fit the box and centred in it; whatever the author's units were.</summary>
        public static GuideElement FitPicture(string pathData, float x, float y, float w, float h, GuideColour colour)
        {
            List<float[]> contours = Contours(pathData);
            GuidePath.Bounds(contours, out float minX, out float minY, out float maxX, out float maxY);
            float bw = Math.Max(1e-6f, maxX - minX), bh = Math.Max(1e-6f, maxY - minY);
            float s = Math.Min(w / bw, h / bh);
            float dx = x + (w - bw * s) * 0.5f - minX * s, dy = y + (h - bh * s) * 0.5f - minY * s;
            return new GuideElement { Kind = GuideElementKind.Path, Contours = GuidePath.Map(contours, s, s, dx, dy), X = x + (w - bw * s) * 0.5f, Y = y + (h - bh * s) * 0.5f, W = bw * s, H = bh * s, Colour = colour };
        }

        /// <summary>A straight bar of a thickness from one point to another, with square ends, as a polygon.</summary>
        public static GuideElement Bar(float x0, float y0, float x1, float y1, float thickness, GuideColour colour)
        {
            float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = -dy / len * thickness * 0.5f, ny = dx / len * thickness * 0.5f;
            return Poly(new[] { x0 + nx, y0 + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, x0 - nx, y0 - ny }, colour);
        }

        /// <summary>A polygon clipped to an axis-aligned box (Sutherland-Hodgman); the countdown boards' stripes are cut to the inside of the board with it.</summary>
        public static float[] ClipToBox(float[] polygon, float x0, float y0, float x1, float y1)
        {
            List<float[]> points = new List<float[]>();
            for (int i = 0; i + 1 < polygon.Length; i += 2)
                points.Add(new[] { polygon[i], polygon[i + 1] });

            for (int edge = 0; edge < 4 && points.Count > 0; edge++)
            {
                var next = new List<float[]>();
                for (int i = 0; i < points.Count; i++)
                {
                    float[] a = points[i], b = points[(i + 1) % points.Count];
                    bool ina = Inside(a, edge, x0, y0, x1, y1), inb = Inside(b, edge, x0, y0, x1, y1);
                    if (ina != inb)
                        next.Add(Cross(a, b, edge, x0, y0, x1, y1));
                    if (inb)
                        next.Add(b);
                }

                points = next;
            }

            var result = new float[points.Count * 2];
            for (int i = 0; i < points.Count; i++)
            {
                result[i * 2] = points[i][0];
                result[i * 2 + 1] = points[i][1];
            }

            return result;
        }

        private static bool Inside(float[] p, int edge, float x0, float y0, float x1, float y1)
        {
            switch (edge)
            {
                case 0: return p[0] >= x0;
                case 1: return p[0] <= x1;
                case 2: return p[1] >= y0;
                default: return p[1] <= y1;
            }
        }

        private static float[] Cross(float[] a, float[] b, int edge, float x0, float y0, float x1, float y1)
        {
            float dx = b[0] - a[0], dy = b[1] - a[1];
            switch (edge)
            {
                case 0: { float t = (x0 - a[0]) / dx; return new[] { x0, a[1] + dy * t }; }
                case 1: { float t = (x1 - a[0]) / dx; return new[] { x1, a[1] + dy * t }; }
                case 2: { float t = (y0 - a[1]) / dy; return new[] { a[0] + dx * t, y0 }; }
                default: { float t = (y1 - a[1]) / dy; return new[] { a[0] + dx * t, y1 }; }
            }
        }
    }
}
