using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The road-surface figures of GB 5768.3 5.15 (MarkingPictograms), the speed figures and the speed-limit face of 5.14 (MarkingSpeed, MarkingLetters.AddFigures).</summary>
    internal static class MarkingSymbolTests
    {
        public static void RunAll()
        {
            Program.Run("symbols: the direction arrow has the measures of Fig. 52", Arrow);
            Program.Run("symbols: the figures are as wide as asked, centred, white, flat and up-facing", Figures);
            Program.Run("symbols: the pedestrian, the bicycle and the arrow in a row, a metre apart (Fig. 58)", Row);
            Program.Run("symbols: the shared-road mark has its chevrons over the bicycle (Fig. 54)", Shared);
            Program.Run("speed: the bands and ranges of Table 3", Table3);
            Program.Run("speed: the figures of 线34, their colours, sizes and order", SpeedFigures);
            Program.Run("speed: the sign's face on the road (5.14.3, Fig. 50)", Face);
            Program.Run("symbols: the catalogue's templates make what they say", Templates);
        }

        private static void Extents(MarkingShape shape, out float x0, out float x1, out float z0, out float z1, out float y0, out float y1)
        {
            x0 = z0 = y0 = float.MaxValue;
            x1 = z1 = y1 = float.MinValue;
            foreach (Vec3 p in shape.Mesh.Positions)
            {
                x0 = Math.Min(x0, p.X);
                x1 = Math.Max(x1, p.X);
                z0 = Math.Min(z0, p.Z);
                z1 = Math.Max(z1, p.Z);
                y0 = Math.Min(y0, p.Y);
                y1 = Math.Max(y1, p.Y);
            }
        }

        private static void Sound(MarkingShape shape, string what)
        {
            Program.Check(shape.Mesh.TriangleCount > 0, what + ": it has paint");
            int bad = 0, down = 0;
            for (int i = 0; i + 2 < shape.Mesh.Indices.Count; i += 3)
            {
                Vec3 a = shape.Mesh.Positions[shape.Mesh.Indices[i]], b = shape.Mesh.Positions[shape.Mesh.Indices[i + 1]], c = shape.Mesh.Positions[shape.Mesh.Indices[i + 2]];
                if (!a.IsFinite || !b.IsFinite || !c.IsFinite)
                    bad++;
                else if (MeshBuilder.Cross(b - a, c - a).Y < -1e-9f)
                    down++;
            }

            Program.Check(bad == 0, what + ": no point that is not a number");
            Program.Check(down == 0, what + ": every triangle faces up (" + down + " do not)");
            Extents(shape, out _, out _, out _, out _, out float y0, out float y1);
            Program.Check(Math.Abs(y0) < 1e-6f && Math.Abs(y1) < 1e-6f, what + ": flat at the road's height");
        }

        private static float Area(MarkingShape shape) => shape.Area;

        private static void Arrow()
        {
            MarkingShape arrow = MarkingPictograms.Arrow();
            Sound(arrow, "arrow");
            Extents(arrow, out float x0, out float x1, out float z0, out float z1, out _, out _);
            Program.Near(z1 - z0, 1.5f, 1e-4f, "150 long");
            Program.Near(x1 - x0, 0.24f, 1e-4f, "the head is 8 + 8 + 8 = 24 wide");
            Program.Near(Area(arrow), 0.08f * 0.9f + 0.5f * 0.24f * 0.6f, 1e-4f, "a shaft 8 wide and 90 long and a head 24 wide and 60 long: " + Area(arrow));
            Program.Near((x0 + x1) * 0.5f, 0f, 1e-4f, "centred across");
            Program.Near((z0 + z1) * 0.5f, 0f, 1e-4f, "and along");
            // The arrow is a size of its own: it does not follow the width of the figure it goes with.
            MarkingShape small = MarkingPictograms.Bicycle(0.8f, true), large = MarkingPictograms.Bicycle(2.0f, true);
            float smallArrow = ArrowLength(small), largeArrow = ArrowLength(large);
            Program.Near(smallArrow, 1.5f, 0.01f, "the arrow of a small bicycle is 1.5 m: " + smallArrow);
            Program.Near(largeArrow, 1.5f, 0.01f, "and of a large one: " + largeArrow);
        }

        /// <summary>The length of the arrow on top of a figure: the part of the shape above the figure's last row of paint that is wider than the shaft (the arrow is the thin thing on top).</summary>
        private static float ArrowLength(MarkingShape shape)
        {
            float top = TopOf(shape), bottom = float.MaxValue;
            for (int i = 0; i + 2 < shape.Mesh.Indices.Count; i += 3)
            {
                // The arrow's triangles: the ones whose points are all within 0.13 m of the middle across (the figure's own centre is the middle of its box, the arrow is on it).
                float zMin = float.MaxValue;
                bool thin = true;
                for (int k = 0; k < 3; k++)
                {
                    Vec3 p = shape.Mesh.Positions[shape.Mesh.Indices[i + k]];
                    zMin = Math.Min(zMin, p.Z);
                    thin &= Math.Abs(p.X - ArrowAxis(shape)) < 0.125f;
                }

                if (thin && zMin > top - 1.55f)
                    bottom = Math.Min(bottom, zMin);
            }

            return top - bottom;
        }

        private static float TopOf(MarkingShape shape)
        {
            Extents(shape, out _, out _, out _, out float z1, out _, out _);
            return z1;
        }

        /// <summary>Where the arrow's axis is across the figure: the figure's own middle.</summary>
        private static float ArrowAxis(MarkingShape shape)
        {
            // The arrow's head tip is the highest point of the shape.
            float top = TopOf(shape);
            foreach (Vec3 p in shape.Mesh.Positions)
            {
                if (Math.Abs(p.Z - top) < 1e-5f)
                    return p.X;
            }

            return 0f;
        }

        private static void Figures()
        {
            foreach ((string name, MarkingShape shape, float width, float aspectLow, float aspectHigh) in new[]
            {
                ("bicycle", MarkingPictograms.Bicycle(1.6f, false), 1.6f * 23.5f / 25f, 1.5f, 2.2f),
                ("electric bicycle", MarkingPictograms.ElectricBicycle(1.5f, false), 1.5f * 0.94f, 1.3f, 2.0f),
                ("wheelchair", MarkingPictograms.Wheelchair(1.2f), 1.2f * 0.9f, 0.7f, 1.2f)
            })
            {
                Sound(shape, name);
                Extents(shape, out float x0, out float x1, out float z0, out float z1, out _, out _);
                Program.Near((x0 + x1) * 0.5f, 0f, 1e-3f, name + ": centred across");
                Program.Near((z0 + z1) * 0.5f, 0f, 1e-3f, name + ": centred along");
                Program.Check(Math.Abs((x1 - x0) - width) < width * 0.1f, name + ": about as wide as asked (" + width.ToString("0.00") + "): " + (x1 - x0));
                float aspect = (x1 - x0) / (z1 - z0);
                Program.Check(aspect > aspectLow && aspect < aspectHigh, name + ": the proportions of its figure: " + aspect);
                Program.Check(shape.Colour == MarkingColour.White && shape.Texts.Count == 0, name + ": white paint, no words (5.15.1)");
            }

            // Twice the width, four times the paint; a size is a scale.
            float a = Area(MarkingPictograms.Bicycle(1.0f, false)), b = Area(MarkingPictograms.Bicycle(2.0f, false));
            Program.Near(b / a, 4f, 0.02f, "twice as wide is four times the paint: " + b / a);
            // The size is limited, and what is not a number is a size too.
            Extents(MarkingPictograms.Bicycle(50f, false), out float lx0, out float lx1, out _, out _, out _, out _);
            Program.Check(lx1 - lx0 <= MarkingPictograms.MaxWidth + 1e-3f, "a figure is at most " + MarkingPictograms.MaxWidth + " m wide: " + (lx1 - lx0));
            Extents(MarkingPictograms.Bicycle(float.NaN, false), out float nx0, out float nx1, out _, out _, out _, out _);
            Program.Check(nx1 - nx0 > 0.1f && !float.IsNaN(nx0), "a width that is not a number is the smallest");
            // The bicycle's wheels are rings: the hub is open (a hole), so the figure is not a block.
            MarkingShape bike = MarkingPictograms.Bicycle(2.0f, false);
            Extents(bike, out float bx0, out _, out float bz0, out _, out _, out _);
            float k = 2.0f / 25f;
            // The front wheel's centre is (4.8, 4.6) cells in the figure's grid, whose left edge is the wheel's (0.8) and whose lowest paint is the wheels' (0.6): inside the tyre, off the fork, there is no paint.
            Program.Check(!Covered(bike, bx0 + (4.8f - 1.5f - 0.8f) * k, bz0 + (4.6f - 1.0f - 0.6f) * k), "inside a wheel is open");
            Program.Check(Covered(bike, bx0 + (4.8f - 0.8f) * k, bz0 + (4.6f - 3.4f - 0.6f) * k), "and the tyre round it is paint");
        }

        /// <summary>Whether a point is under one of the shape's triangles.</summary>
        private static bool Covered(MarkingShape shape, float x, float z)
        {
            for (int i = 0; i + 2 < shape.Mesh.Indices.Count; i += 3)
            {
                Vec3 a = shape.Mesh.Positions[shape.Mesh.Indices[i]], b = shape.Mesh.Positions[shape.Mesh.Indices[i + 1]], c = shape.Mesh.Positions[shape.Mesh.Indices[i + 2]];
                float d1 = (x - b.X) * (a.Z - b.Z) - (a.X - b.X) * (z - b.Z), d2 = (x - c.X) * (b.Z - c.Z) - (b.X - c.X) * (z - c.Z), d3 = (x - a.X) * (c.Z - a.Z) - (c.X - a.X) * (z - a.Z);
                bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
                if (!(neg && pos))
                    return true;
            }

            return false;
        }

        private static void Row()
        {
            // The pedestrian alone: 1.6 m high.
            MarkingShape ped = MarkingPictograms.Pedestrian(1.6f, false, false);
            Sound(ped, "pedestrian");
            Extents(ped, out float px0, out float px1, out float pz0, out float pz1, out _, out _);
            Program.Check(Math.Abs((pz1 - pz0) - 1.6f * 16.9f / 18f) < 0.08f, "the pedestrian is as high as the figure's grid (the ink is 16.9 of its 18 rows): " + (pz1 - pz0));
            Program.Check((px1 - px0) / (pz1 - pz0) > 0.45f && (px1 - px0) / (pz1 - pz0) < 0.85f, "and about two thirds as wide as it is high (12 by 18 cells): " + (px1 - px0) / (pz1 - pz0));
            // With the bicycle before it and the arrow after it, a metre between each.
            MarkingShape row = MarkingPictograms.Pedestrian(1.6f, true, true);
            Sound(row, "the row");
            Extents(row, out _, out _, out float rz0, out float rz1, out _, out _);
            float bike = 1.6f * 15.6f / 25f;
            float expected = bike + 1f + 1.6f + 1f + 1.5f;
            Program.Check(Math.Abs((rz1 - rz0) - expected) < 0.25f, "bicycle, a metre, pedestrian, a metre, arrow: " + (rz1 - rz0).ToString("0.00") + " m, expected about " + expected.ToString("0.00"));
            // Order: the paint nearest the driver (low Z) is the bicycle's wheels, the far end is the arrow: the widest row in the lowest fifth is a bicycle, in the highest fifth the arrow is thin.
            float lowWidth = WidthIn(row, rz0, rz0 + (rz1 - rz0) * 0.2f), highWidth = WidthIn(row, rz1 - (rz1 - rz0) * 0.2f, rz1);
            Program.Check(lowWidth > 1.0f && highWidth < 0.3f, "the bicycle is at the near end and the thin arrow at the far end: " + lowWidth.ToString("0.00") + " / " + highWidth.ToString("0.00"));
            MarkingShape withBike = MarkingPictograms.Pedestrian(1.6f, true, false), withArrow = MarkingPictograms.Pedestrian(1.6f, false, true);
            Extents(withBike, out _, out _, out float wz0, out float wz1, out _, out _);
            Extents(withArrow, out _, out _, out float az0, out float az1, out _, out _);
            Program.Check(Math.Abs((wz1 - wz0) - (bike + 1f + 1.6f)) < 0.2f, "with the bicycle only: " + (wz1 - wz0));
            Program.Check(Math.Abs((az1 - az0) - (1.6f + 1f + 1.5f)) < 0.2f, "with the arrow only: " + (az1 - az0));
        }

        /// <summary>The width of the paint between two heights.</summary>
        private static float WidthIn(MarkingShape shape, float z0, float z1)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i + 2 < shape.Mesh.Indices.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    Vec3 p = shape.Mesh.Positions[shape.Mesh.Indices[i + k]];
                    if (p.Z >= z0 && p.Z <= z1)
                    {
                        lo = Math.Min(lo, p.X);
                        hi = Math.Max(hi, p.X);
                    }
                }
            }

            return hi < lo ? 0f : hi - lo;
        }

        private static void Shared()
        {
            MarkingShape shared = MarkingPictograms.Shared(1.5f);
            Sound(shared, "shared");
            Extents(shared, out float x0, out float x1, out float z0, out float z1, out _, out _);
            Program.Near(x1 - x0, 1.5f, 0.01f, "as wide as its chevrons: " + (x1 - x0));
            Program.Check((z1 - z0) / (x1 - x0) > 1.2f && (z1 - z0) / (x1 - x0) < 1.8f, "taller than wide, 20 cells by 30 (Fig. 54): " + (z1 - z0) / (x1 - x0));
            // The chevrons point the way the traffic runs: their apex is the highest point and is in the middle across.
            float topX = ArrowAxis(shared);
            Program.Near(topX, (x0 + x1) * 0.5f, 0.01f, "the apex is in the middle");
            // The bicycle is under them: the lowest fifth is narrower than the chevrons (the bicycle is 19 cells of the 18.4 + the figure).
            float lowest = WidthIn(shared, z0, z0 + (z1 - z0) * 0.3f), highest = WidthIn(shared, z1 - (z1 - z0) * 0.08f, z1);
            Program.Check(lowest > 1.0f, "the bicycle is at the near end: " + lowest);
            Program.Check(highest < 0.5f, "the apex of the chevrons is at the far end: " + highest);
            // Two bands, each 2.4 cells thick (up and down the figure) with 2.4 cells between: a vertical cut a little off the middle crosses the chevrons in two runs of that length.
            float cell = 1.5f / 18.4f;
            var runs = new List<float>();
            var starts = new List<float>();
            float runStart = float.NaN;
            float cut = (x0 + x1) * 0.5f + 0.3f;
            for (float z = z0 + (z1 - z0) * 0.45f; z <= z1; z += 0.002f)
            {
                bool inside = Covered(shared, cut, z);
                if (inside && float.IsNaN(runStart))
                    runStart = z;
                else if (!inside && !float.IsNaN(runStart))
                {
                    runs.Add(z - runStart);
                    starts.Add(runStart);
                    runStart = float.NaN;
                }
            }

            Program.Check(runs.Count == 2, "two chevron bands cross a cut across them: " + runs.Count);
            foreach (float run in runs)
                Program.Near(run, 2.4f * cell, 0.012f, "each band is 2.4 cells thick: " + run);
            if (runs.Count == 2)
                Program.Near(starts[1] - (starts[0] + runs[0]), 2.4f * cell, 0.012f, "and 2.4 cells apart: " + (starts[1] - (starts[0] + runs[0])));
            // Not under 0.5 m wide (5.15.3).
            Extents(MarkingPictograms.Shared(0.1f), out float sx0, out float sx1, out _, out _, out _, out _);
            Program.Check(sx1 - sx0 >= MarkingPictograms.MinWidth - 1e-4f, "it is never narrower than " + MarkingPictograms.MinWidth + " m: " + (sx1 - sx0));
        }

        private static void Table3()
        {
            Program.Check(MarkingSpeed.BandOf(120f) == SpeedBand.High && MarkingSpeed.BandOf(100f) == SpeedBand.High, "100 and over is the high band");
            Program.Check(MarkingSpeed.BandOf(99f) == SpeedBand.Mid && MarkingSpeed.BandOf(41f) == SpeedBand.Mid && MarkingSpeed.BandOf(60f) == SpeedBand.Mid, "over 40 and under 100 is the middle band");
            Program.Check(MarkingSpeed.BandOf(40f) == SpeedBand.Low && MarkingSpeed.BandOf(0f) == SpeedBand.Low, "40 and under is the low band");
            Program.Near(MarkingSpeed.Height(SpeedBand.High, 0f), 4.5f, 1e-4f, "100 and over: 450 to 900 cm, the smallest");
            Program.Near(MarkingSpeed.Height(SpeedBand.High, 1f), 9f, 1e-4f, "the largest");
            Program.Near(MarkingSpeed.Height(SpeedBand.Mid, 0f), 3f, 1e-4f, "over 40, under 100: 300 to 600");
            Program.Near(MarkingSpeed.Height(SpeedBand.Mid, 1f), 6f, 1e-4f, "");
            Program.Near(MarkingSpeed.Height(SpeedBand.Low, 0f), 1.5f, 1e-4f, "40 and under: 150 to 300");
            Program.Near(MarkingSpeed.Height(SpeedBand.Low, 1f), 3f, 1e-4f, "");
            Program.Near(MarkingSpeed.Height(SpeedBand.Mid, 0.5f), 4.5f, 1e-4f, "the middle of a range");
            Program.Near(MarkingSpeed.Spacing(SpeedBand.High, 0f), 3f, 1e-4f, "spacing 300 to 600");
            Program.Near(MarkingSpeed.Spacing(SpeedBand.Mid, 1f), 4f, 1e-4f, "200 to 400");
            Program.Near(MarkingSpeed.Spacing(SpeedBand.Low, 0f), 1f, 1e-4f, "100 to 200");
            Program.Near(MarkingSpeed.Height(SpeedBand.Mid, 5f), 6f, 1e-4f, "a position beyond the range is the end of it");
            Program.Near(MarkingSpeed.Height(SpeedBand.Mid, float.NaN), 4.5f, 1e-4f, "and one that is not a number is the middle");
            Program.Check(MarkingSpeed.Clean("1a2b0") == "120" && MarkingSpeed.Clean("123456") == "1234" && MarkingSpeed.Clean(null) == "" && MarkingSpeed.Clean("六十") == "", "figures only, at most four");
        }

        private static List<Vec3> PaintOf(MarkingShape shape)
        {
            var mesh = new MeshBuilder();
            mesh.Append(shape.Mesh, Vec3.Zero, 0f);
            MarkingLetters.AddTexts(mesh, shape.Texts);
            return mesh.Positions;
        }

        private static void Box(MarkingShape shape, out float x0, out float x1, out float z0, out float z1)
        {
            x0 = z0 = float.MaxValue;
            x1 = z1 = float.MinValue;
            foreach (Vec3 p in PaintOf(shape))
            {
                x0 = Math.Min(x0, p.X);
                x1 = Math.Max(x1, p.X);
                z0 = Math.Min(z0, p.Z);
                z1 = Math.Max(z1, p.Z);
            }
        }

        private static void SpeedFigures()
        {
            MarkingLetters.Font = FontLetterTests.Font;
            Program.Check(FontLetterTests.Font != null, "the font is there");
            List<MarkingShape> top = MarkingSpeed.Figures("60", "40", "max", SpeedBand.Mid, 0.5f);
            Program.Check(top.Count == 1 && top[0].Colour == MarkingColour.Yellow, "the top speed alone is yellow");
            Box(top[0], out float x0, out float x1, out float z0, out float z1);
            Program.Near(z1 - z0, 4.5f, 0.1f, "a middle size in the middle band is 450 cm high: " + (z1 - z0));
            // Two figures: each about a third of the height wide (Table 3: width is a third of the height), together under two thirds of it.
            Program.Check(x1 - x0 > 0.45f * 4.5f / 3f * 2f && x1 - x0 < 1.0f * 4.5f / 3f * 2f + 0.01f, "two figures 150 cm wide each, less the squeeze: " + (x1 - x0));
            Program.Near((x0 + x1) * 0.5f, 0f, 0.02f, "centred across");
            Program.Near((z0 + z1) * 0.5f, 0f, 0.02f, "and along");

            List<MarkingShape> low = MarkingSpeed.Figures("60", "40", "min", SpeedBand.Mid, 0.5f);
            Program.Check(low.Count == 1 && low[0].Colour == MarkingColour.White, "the lowest speed is white");
            Box(low[0], out float lx0, out float lx1, out _, out _);
            List<MarkingShape> pair = MarkingSpeed.Figures("100", "80", "pair", SpeedBand.High, 0f);
            Program.Check(pair.Count == 3, "a pair is the two numbers and the bar between them: " + pair.Count);
            MarkingShape yellow = pair.Find(s => s.Colour == MarkingColour.Yellow), white = pair.Find(s => s.Texts.Count > 0 && s.Colour == MarkingColour.White), bar = pair.Find(s => s.Texts.Count == 0);
            Program.Check(yellow != null && white != null && bar != null, "yellow top, white lowest, and a bar");
            Box(yellow, out _, out _, out float yz0, out float yz1);
            Box(white, out _, out _, out float wz0, out float wz1);
            Program.Check(yz0 > wz1, "the top speed is further along than the lowest, which is nearer the driver (Fig. 49): " + yz0 + " > " + wz1);
            Program.Near(yz1 - yz0, 4.5f, 0.1f, "the smallest size in the high band is 450 cm");
            Program.Near(yz0 - wz1, 3f, 0.1f, "and the spacing between the two is 300 cm (Table 3)");
            Box(bar, out float bx0, out float bx1, out float bz0, out float bz1);
            Program.Check(bz0 > wz1 && bz1 < yz0 && Math.Abs((bz0 + bz1) * 0.5f) < 0.02f, "the bar is between the two numbers");
            Program.Near(bx1 - bx0, 0.15f, 1e-3f, "15 cm wide");
            Program.Near(bz1 - bz0, 0.6f * 3f, 0.01f, "and 0.6 of the spacing long (read from Fig. 49)");
            // Three figures are as high as two, and wider; a 1 is narrower than a 0.
            Box(MarkingSpeed.Figures("100", "", "max", SpeedBand.Mid, 0.5f)[0], out float tx0, out float tx1, out float tz0, out float tz1);
            Box(MarkingSpeed.Figures("000", "", "max", SpeedBand.Mid, 0.5f)[0], out float ox0, out float ox1, out _, out _);
            Program.Near(tz1 - tz0, 4.5f, 0.1f, "three figures are as high");
            Program.Check(tx1 - tx0 < ox1 - ox0 - 0.1f, "a 1 is narrower than a 0 (100 is narrower than 000): " + (tx1 - tx0) + " / " + (ox1 - ox0));
            // A blank or damaged number is the default, not nothing.
            Program.Check(MarkingSpeed.Figures("", "", "max", SpeedBand.Mid, 0.5f).Count == 1 && MarkingSpeed.Figures("xx", "yy", "pair", SpeedBand.Low, 0.5f).Count == 3, "a number that is not one is the default");
            Program.Check(lx1 - lx0 > 1f && lx1 - lx0 < 3.1f, "the lowest speed, two figures of 150 cm cells, is about 2.6 m wide: " + (lx1 - lx0));

            // The text boxes of a number are figures (Digits): the width is the cell of one figure, the length the height.
            MarkingText box = top[0].Texts[0];
            Program.Check(box.Digits && Math.Abs(box.Length - 4.5f) < 1e-4f && Math.Abs(box.Width - 1.5f) < 1e-4f, "the box says figures, 450 high and 150 a figure");
            MarkingLetters.Font = null;
            int none = MarkingLetters.AddFigures(new MeshBuilder(), "60", 0f, 0f, 1f, 3f);
            Program.Check(none == 0, "without the font there is nothing to draw, and no exception");
            MarkingLetters.Font = FontLetterTests.Font;
        }

        private static void Face()
        {
            MarkingLetters.Font = FontLetterTests.Font;
            List<MarkingShape> face = MarkingSpeed.Face("40", 4f);
            Program.Check(face.Count == 3, "a ring, a face and the figures: " + face.Count);
            MarkingShape ring = face.Find(s => s.Colour == MarkingColour.Red), white = face.Find(s => s.Colour == MarkingColour.White), black = face.Find(s => s.Colour == MarkingColour.Black);
            Program.Check(ring != null && white != null && black != null, "red, white and black");
            Extents(ring, out float x0, out float x1, out float z0, out float z1, out _, out _);
            Program.Near(z1 - z0, 4f, 0.02f, "the long axis is 4 m");
            Program.Near((x1 - x0) * 2.5f, 4f, 0.05f, "2.5 times as long as it is wide: " + (x1 - x0));
            Program.Near(Area(ring) + Area(white), (float)(Math.PI * 0.5 * (x1 - x0) * 0.5 * (z1 - z0)), 0.03f, "the ring and the face are the ellipse: " + (Area(ring) + Area(white)));
            // The ring is a tenth of the circle's diameter wide: the face inside it is 0.8 of the diameter, so the ring is (1 - 0.64) / 0.64 of the face's area.
            Program.Near(Area(ring) / Area(white), 0.36f / 0.64f, 0.02f, "the ring is a tenth of the diameter wide: " + Area(ring) / Area(white));
            // The figures are in the layer over the white, and inside the face.
            Program.Check(black.Lift > white.Lift && black.Lift > 0f && ring.Lift == 0f && white.Lift == 0f, "the figures are in a layer over the white");
            Box(black, out float bx0, out float bx1, out float bz0, out float bz1);
            Extents(white, out float wx0, out float wx1, out float wz0, out float wz1, out _, out _);
            Program.Check(bx0 > wx0 && bx1 < wx1 && bz0 > wz0 && bz1 < wz1, "the figures are inside the face");
            // Every corner of the row of figures is inside the face's ellipse.
            foreach (float cx in new[] { bx0, bx1 })
            {
                foreach (float cz in new[] { bz0, bz1 })
                {
                    float ex = cx / ((wx1 - wx0) * 0.5f), ez = cz / ((wz1 - wz0) * 0.5f);
                    Program.Check(ex * ex + ez * ez < 1f, "a corner of the figures is inside the ellipse of the face: " + (ex * ex + ez * ez));
                }
            }

            // Three figures are narrower than two (they must fit the same face), and every row is inside the ellipse. Not that they are shorter: the 1 of the traffic sign font is narrow, so a row of
            // 120 can be narrower than one of 40 (whose 4 is wide) and be as high as the face lets it be (0.12.28).
            Box(MarkingSpeed.Face("120", 4f)[2], out float tx0, out float tx1, out _, out _);
            Program.Check((tx1 - tx0) / 3f < (bx1 - bx0) / 2f, "a figure of 120 is narrower than one of 40 on the same face: " + (tx1 - tx0) / 3f + " / " + (bx1 - bx0) / 2f);
            foreach (string speed in new[] { "5", "40", "60", "80", "100", "120" })
            {
                Box(MarkingSpeed.Face(speed, 4f)[2], out float sx0, out float sx1, out float sz0, out float sz1);
                foreach (float cx in new[] { sx0, sx1 })
                {
                    foreach (float cz in new[] { sz0, sz1 })
                    {
                        float ex = cx / ((wx1 - wx0) * 0.5f), ez = cz / ((wz1 - wz0) * 0.5f);
                        Program.Check(ex * ex + ez * ez < 1f, speed + ": a corner of the figures is inside the ellipse: " + (ex * ex + ez * ez));
                    }
                }
            }

            Extents(MarkingSpeed.Face("40", 20f)[0], out _, out _, out float lz0, out float lz1, out _, out _);
            Program.Near(lz1 - lz0, 6f, 0.02f, "6 m at the most");
            Extents(MarkingSpeed.Face("40", float.NaN)[0], out _, out _, out float nz0, out float nz1, out _, out _);
            Program.Near(nz1 - nz0, 4f, 0.02f, "a length that is not a number is 4 m");
            Program.Check(MarkingSpeed.Face("", 4f).Count == 2, "a face with no figure is a ring and a face");
        }

        private static void Templates()
        {
            MarkingLetters.Font = FontLetterTests.Font;
            string[] ids = { "mk.speed", "mk.speedface", "mk.sym.bicycle", "mk.sym.arrow", "mk.sym.shared", "mk.sym.ebike", "mk.sym.pedestrian", "mk.sym.wheelchair" };
            string[] codes = { "线34", "5.14.3（图50）", "线35、线36", "线36", "线37", "线38", "线39", "线40" };
            for (int i = 0; i < ids.Length; i++)
            {
                SignDef def = SignCatalog.ById(ids[i]);
                Program.Check(def != null && def.IsMarking && def.Code == codes[i], ids[i] + " is in the catalogue as " + codes[i] + ": " + (def == null ? "missing" : def.Code));
                MarkingPlan plan = MarkingTemplates.PlanOf(def, def.Template.Default().ToString());
                Program.Check(plan.TriangleCount >= 3 && plan.MaxX > plan.MinX && plan.MaxZ > plan.MinZ, ids[i] + ": paint of the default");
            }

            // The settings do what they say.
            GuideTemplate speed = MarkingTemplates.ById("mk.speed");
            string spec = speed.Default().ToString();
            MarkingPlan mid = MarkingTemplates.PlanOf(speed, spec);
            MarkingPlan high = MarkingTemplates.PlanOf(speed, speed.Parse(spec).With("band", "high").ToString());
            MarkingPlan low = MarkingTemplates.PlanOf(speed, speed.Parse(spec).With("band", "low").ToString());
            Program.Check(high.MaxZ - high.MinZ > mid.MaxZ - mid.MinZ && mid.MaxZ - mid.MinZ > low.MaxZ - low.MinZ, "the band sets the height: " + (low.MaxZ - low.MinZ) + " < " + (mid.MaxZ - mid.MinZ) + " < " + (high.MaxZ - high.MinZ));
            Program.Near(mid.MaxZ - mid.MinZ, 4.5f, 0.1f, "the middle band's middle size is 450 cm");
            // "auto" is the road's band.
            string auto = speed.Parse(spec).With("band", "auto").ToString();
            Program.Check(speed.Parse(MarkingTemplates.ResolveSize(speed, auto, 120f, true)).Is("band", "high") && speed.Parse(MarkingTemplates.ResolveSize(speed, auto, 30f, true)).Is("band", "low") && speed.Parse(MarkingTemplates.ResolveSize(speed, auto, 60f, true)).Is("band", "mid"), "auto takes the road's band");
            Program.Check(speed.Parse(MarkingTemplates.ResolveSize(speed, auto, 0f, false)).Is("band", "mid"), "and the middle one when the road is not known");
            Program.Check(speed.Parse(MarkingTemplates.ResolveSize(speed, speed.Parse(spec).With("band", "high").ToString(), 30f, true)).Is("band", "high"), "a band that was chosen is kept");
            // The arrow flag adds the arrow.
            GuideTemplate bicycle = MarkingTemplates.ById("mk.sym.bicycle");
            MarkingPlan plain = MarkingTemplates.PlanOf(bicycle, bicycle.Default().ToString()), arrow = MarkingTemplates.PlanOf(bicycle, bicycle.Parse(bicycle.Default().ToString()).With("arrow", "1").ToString());
            Program.Check(arrow.MaxZ - arrow.MinZ > plain.MaxZ - plain.MinZ + 2f && arrow.TriangleCount > plain.TriangleCount, "the arrow is added in front: " + (plain.MaxZ - plain.MinZ) + " -> " + (arrow.MaxZ - arrow.MinZ));
            // A size is in centimetres and goes to a multiple of 10.
            MarkingPlan w158 = MarkingTemplates.PlanOf(bicycle, bicycle.Parse(bicycle.Default().ToString()).With("width", "158").ToString()), w160 = MarkingTemplates.PlanOf(bicycle, bicycle.Parse(bicycle.Default().ToString()).With("width", "160").ToString());
            Program.Near(w158.MaxX - w158.MinX, w160.MaxX - w160.MinX, 1e-4f, "158 cm is 160 cm (5.15.1: to a multiple of 10 cm)");
            // The speed numbers and their face stand in a lane.
            Program.Check(MarkingSnap.AnchorOf("mk.speed") == MarkingAnchor.Lane && MarkingSnap.AnchorOf("mk.speedface") == MarkingAnchor.Lane && MarkingSnap.AnchorOf("mk.sym.bicycle") == MarkingAnchor.Free, "the speed marks are in a lane, a figure for cyclists stays where it is put (a cycle lane is not a car lane)");
            MarkingLetters.Font = null;
        }
    }
}
