using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The three bands of design speed that GB 5768.3 Table 3 sizes road-surface words and figures by.</summary>
    public enum SpeedBand
    {
        /// <summary>40 km/h and under.</summary>
        Low,

        /// <summary>Over 40 and under 100 km/h.</summary>
        Mid,

        /// <summary>100 km/h and over.</summary>
        High
    }

    /// <summary>
    /// The road-surface speed limit of GB 5768.3-2025 5.14.2 (线34): the figures of the top speed in yellow, of the lowest speed in white (used with the top one), sized by Table 3, and 5.14.3 (Fig. 50): the
    /// speed-limit sign's face painted on the road as an ellipse 2.5 times as long as it is wide, its long axis along the way. Table 3 gives each band a range for the height of the figures, the width of one and
    /// the spacing between lines; the figure's width is a third of its height at both ends of every range, so a size is a position in the range. Pure.
    /// </summary>
    public static class MarkingSpeed
    {
        /// <summary>Table 3: the height of the figures (m) of each band, from the smallest to the largest. The width of a figure is a third of it.</summary>
        public static readonly float[][] Heights = { new[] { 1.5f, 3f }, new[] { 3f, 6f }, new[] { 4.5f, 9f } };

        /// <summary>Table 3: the spacing between two lines of figures (m), from the smallest to the largest.</summary>
        public static readonly float[][] Spacings = { new[] { 1f, 2f }, new[] { 2f, 4f }, new[] { 3f, 6f } };

        /// <summary>The band of a design speed (km/h): 100 and over, over 40 and under 100, and 40 and under.</summary>
        public static SpeedBand BandOf(float kmh) => kmh >= 100f ? SpeedBand.High : kmh > 40f ? SpeedBand.Mid : SpeedBand.Low;

        /// <summary>The height (m) at a position in the band's range: 0 the smallest, 1 the largest.</summary>
        public static float Height(SpeedBand band, float position) => Lerp(Heights[(int)band], position);

        /// <summary>The spacing (m) at a position in the band's range.</summary>
        public static float Spacing(SpeedBand band, float position) => Lerp(Spacings[(int)band], position);

        private static float Lerp(float[] range, float t) => range[0] + (range[1] - range[0]) * Math.Max(0f, Math.Min(1f, float.IsNaN(t) ? 0.5f : t));

        /// <summary>The figures with only digits and at most four of them (a speed is 5 to 120); anything else is a blank.</summary>
        public static string Clean(string figures)
        {
            if (string.IsNullOrEmpty(figures))
                return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in figures)
            {
                if (c >= '0' && c <= '9' && sb.Length < 4)
                    sb.Append(c);
            }

            return sb.ToString();
        }

        private static MarkingShape Row(string figures, MarkingColour colour, float z, float height)
        {
            var shape = new MarkingShape { Colour = colour };
            shape.Texts.Add(new MarkingText { Text = figures, X = 0f, Z = z, Width = height / 3f, Length = height, Digits = true });
            return shape;
        }

        /// <summary>
        /// The figures of the speed (线34). The top speed is yellow and can stand alone; the lowest is white and goes with the top one (5.14.2), the lowest nearer the driver and the top further, with a white bar
        /// between (Fig. 49: the bar is read from the figure, 15 cm wide and 0.6 of the spacing long). kind: "max", "min" or "pair". The figures of the first line are nearest (the way the traffic runs is +Z).
        /// </summary>
        public static List<MarkingShape> Figures(string top, string lowest, string kind, SpeedBand band, float position)
        {
            var shapes = new List<MarkingShape>();
            // A blank or damaged setting is the default (60 top, 40 lowest) rather than nothing painted.
            string max = Clean(top), min = Clean(lowest);
            if (max.Length == 0)
                max = "60";
            if (min.Length == 0)
                min = "40";
            float h = Height(band, position), s = Spacing(band, position);
            if (kind == "min")
            {
                shapes.Add(Row(min, MarkingColour.White, 0f, h));
                return shapes;
            }

            if (kind == "pair")
            {
                float offset = (h + s) * 0.5f;
                shapes.Add(Row(min, MarkingColour.White, -offset, h));
                shapes.Add(Row(max, MarkingColour.Yellow, offset, h));
                var bar = new MarkingShape { Colour = MarkingColour.White };
                RoadMarkings.Rect(bar.Mesh, -0.075f, -s * 0.3f, 0.075f, s * 0.3f);
                shapes.Add(bar);
                return shapes;
            }

            shapes.Add(Row(max, MarkingColour.Yellow, 0f, h));
            return shapes;
        }

        /// <summary>The semi-axes of the face's outer and inner ellipse as shares of the circle's diameter D of the sign it is the stretched image of: the red ring is 0.1 D wide (GB 5768.2 prohibition signs), the long axis 2.5 times the short.</summary>
        public const float Stretch = 2.5f, RingShare = 0.1f;

        private static List<Vec3> Ellipse(float semiX, float semiZ, int segments)
        {
            var points = new List<Vec3>(segments);
            for (int i = 0; i < segments; i++)
            {
                double a = 2.0 * Math.PI * i / segments;
                points.Add(new Vec3(semiX * (float)Math.Cos(a), 0f, semiZ * (float)Math.Sin(a)));
            }

            return points;
        }

        /// <summary>
        /// The speed-limit sign's face painted on the road (5.14.3, Fig. 50): the sign's circle stretched 2.5 times along the way the traffic runs, its long axis length (m, at most 6) long: a red ring, a white face
        /// inside it and the limit in black on the face (the figures are in the layer over the white, so that they do not flicker with it). The figures are as big as the face lets them be: a row of them never goes outside
        /// the ellipse, so three figures are smaller than two.
        /// </summary>
        public static List<MarkingShape> Face(string figures, float length)
        {
            var shapes = new List<MarkingShape>();
            length = Math.Max(1.5f, Math.Min(6f, float.IsNaN(length) ? 4f : length));
            float d = length / Stretch;
            float outerX = 0.5f * d, outerZ = 0.5f * length, innerX = (0.5f - RingShare) * d, innerZ = (0.5f - RingShare) * length;
            List<Vec3> outer = Ellipse(outerX, outerZ, 64), inner = Ellipse(innerX, innerZ, 64);
            var ring = new MarkingShape { Colour = MarkingColour.Red };
            Polygon.Fill(ring.Mesh, outer, new List<IList<Vec3>> { inner });
            shapes.Add(ring);
            var face = new MarkingShape { Colour = MarkingColour.White };
            Polygon.Fill(face.Mesh, inner);
            shapes.Add(face);

            string clean = Clean(figures);
            if (clean.Length > 0)
            {
                // The widest the row can be across the face, and the highest it can be there: the corners of the row's box stay inside the inner ellipse. The row is as wide as the font's figures make it
                // (MarkingLetters.FigureRowWidth; the 0's width for each when there is no font), and a cell is made narrower when they would be wider than 0.74 of the face.
                float cell = Math.Min(0.36f * d, 0.74f * d / (clean.Length * 0.94f));
                float row = MarkingLetters.FigureRowWidth(clean, cell);
                if (float.IsNaN(row))
                    row = cell * (clean.Length * (MarkingLetters.FigureInk + MarkingLetters.FigureGap) - MarkingLetters.FigureGap);
                if (row > 0.74f * d)
                {
                    cell *= 0.74f * d / row;
                    row = 0.74f * d;
                }

                float reach = (float)Math.Sqrt(Math.Max(0.05, 1.0 - Math.Pow(row * 0.5f / innerX, 2.0)));
                float height = Math.Min(1.125f * d, 2f * innerZ * reach * 0.92f);
                var digits = new MarkingShape { Colour = MarkingColour.Black };
                digits.Texts.Add(new MarkingText { Text = clean, X = 0f, Z = 0f, Width = cell, Length = height, Digits = true });
                digits.Layer(1);
                shapes.Add(digits);
            }

            return shapes;
        }
    }
}
