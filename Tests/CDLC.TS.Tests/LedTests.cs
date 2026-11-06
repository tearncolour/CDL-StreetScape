using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The LED variable-message signs (GB 5768.2 4.11): the dots, reading a picture as dots, the dots as a picture and as quads, the screens of the catalogue, and how they reach the meshes.</summary>
    internal static class LedTests
    {
        public static void RunAll()
        {
            Program.Run("led: the paint's colours are lit as the LED colours of Table 10", Colours);
            Program.Run("led: how many dots a screen has", Dimensions);
            Program.Run("led: a picture read as dots (the first row of the picture is the bottom)", Quantizing);
            Program.Run("led: dots as a picture, and the picture read back is the same dots", RasterRoundTrip);
            Program.Run("led: lit dots as quads facing the driver, the picture's left at +X", Quads);
            Program.Run("led: the screens of the catalogue (text, lane, speed) and their colours", Screens);
            Program.Run("led: the dots reach the meshes of a support and the parts of a tile", Meshes);
            Program.Run("led: a screen is a cabinet with a frame, not a plate", Cabinet);
            Program.Run("led: the dots are kept by what they are made from, not by the settings' text", PictureKeys);
            Program.Run("led: the text screen's length (0.12.22)", ScreenLength);
        }

        private static void ScreenLength()
        {
            SignDef text = SignCatalog.ById("gb.vms.text");
            GuideTemplate t = text.Template;
            GuideSpec spec = t.Parse(t.Default().ToString()).With("l1", "前方事故").With("l2", "减速慢行");
            float Width(GuideSpec s) => SignInstances.Of(text, s.ToString()).Def.Width;
            float free = Width(spec.With("len", "0"));
            Program.Check(free > 1.5f && free < 3f, "as the text by default: " + free.ToString("0.00"));
            Program.Near(Width(spec.With("len", "6")), 6f, 1e-3f, "a length of 6 m");
            Program.Near(Width(spec.With("len", "6.5")), 6.5f, 1e-3f, "and of 6.5 m");
            Program.Near(Width(spec.With("len", "1")), free, 1e-3f, "shorter than the text: as the text");
            Program.Near(Width(spec.With("len", "40")), VariableSigns.MaxScreenLength, 1e-3f, "no longer than the longest");
            Program.Near(Width(spec.With("len", "6").With("l1", "前方事故注意安全").With("l2", "")), 6f, 1e-3f, "the same screen with another text");
            Program.Check(text.Template.FieldOf("len").Sanitize("6.5m") == "6.5", "the field keeps the figures and one point");
            Program.Check(SignInstances.Of(text, spec.With("len", "6").ToString()).Key != SignInstances.Of(text, spec.With("len", "0").ToString()).Key, "two lengths are two instances");
        }

        private static void PictureKeys()
        {
            SignDef text = SignCatalog.ById("gb.vms.text");
            GuideTemplate t = text.Template;
            string baseSpec = t.Parse(t.Default().ToString()).With("l1", "前方事故").With("l2", "减速慢行").ToString();
            string Key(string spec) => Led.PictureKey(SignInstances.Of(text, spec));
            string k60 = Key(t.Parse(baseSpec).With("speed", "60").ToString()), k100 = Key(t.Parse(baseSpec).With("speed", "100").ToString()), k30 = Key(t.Parse(baseSpec).With("speed", "30").ToString());
            Program.Check(k60 != null && k60.Length > 50, "a screen has a picture key");
            Program.Check(SignInstances.Of(text, t.Parse(baseSpec).With("speed", "60").ToString()).Key != SignInstances.Of(text, t.Parse(baseSpec).With("speed", "100").ToString()).Key, "the settings of two speed bands differ (two instances)");
            Program.Check(k60 == k100 && k100 == k30, "but a screen's layout does not use the band: one picture key");
            Program.Check(Key(t.Parse(baseSpec).With("l1", "前方施工").ToString()) != k60, "other words: another key");
            Program.Check(Key(t.Parse(baseSpec).With("l2", "").ToString()) != k60, "a line less: another key");
            Program.Check(Key(t.Parse(baseSpec).With("cls", "info").ToString()) != k60, "another colour: another key");
            Program.Check(Key(t.Parse(baseSpec).With("h", "50").ToString()) != k60, "another character height: another key");
            SignDef lane = SignCatalog.ById("gb.vms.lane");
            string laneKey = Led.PictureKey(SignInstances.Of(lane, lane.Template.Default().ToString()));
            Program.Check(laneKey != null && laneKey != k60, "another screen, another key");
            Program.Check(Led.PictureKey(SignInstances.Of(SignCatalog.ById("guide.exit.advance"), null)) == null && Led.PictureKey(null) == null, "a board that is not a screen has none");
        }

        private static byte[] Picture(int w, int h, Func<int, int, byte[]> pixel)
        {
            // pixel(x, yFromTop) returns RGBA; the array is the painter's order (the first row at the bottom).
            var rgba = new byte[w * h * 4];
            for (int yTop = 0; yTop < h; yTop++)
            {
                for (int x = 0; x < w; x++)
                {
                    byte[] p = pixel(x, yTop);
                    int at = ((h - 1 - yTop) * w + x) * 4;
                    rgba[at] = p[0];
                    rgba[at + 1] = p[1];
                    rgba[at + 2] = p[2];
                    rgba[at + 3] = p[3];
                }
            }

            return rgba;
        }

        private static readonly byte[] Black = { 0, 0, 0, 255 }, Clear = { 0, 0, 0, 0 }, RedPx = { 214, 28, 36, 255 }, YellowPx = { 255, 214, 0, 255 };

        private static void Colours()
        {
            (GuideColour paint, LedColour led)[] table =
            {
                (GuideColour.Red, LedColour.Red), (GuideColour.Yellow, LedColour.Amber), (GuideColour.Orange, LedColour.Amber), (GuideColour.Green, LedColour.Green),
                (GuideColour.Blue, LedColour.Blue), (GuideColour.White, LedColour.White), (GuideColour.Black, LedColour.Off), (GuideColour.Gray, LedColour.White), (GuideColour.Brown, LedColour.Amber)
            };
            foreach ((GuideColour paint, LedColour led) in table)
            {
                GuideColours.Rgb(paint, out byte r, out byte g, out byte b);
                Program.Check(Led.Classify(r / 255f, g / 255f, b / 255f) == led, paint + " is lit as " + led + ": " + Led.Classify(r / 255f, g / 255f, b / 255f));
            }

            Program.Check(Led.Classify(0.2f, 0f, 0f) == LedColour.Off && Led.Classify(0f, 0f, 0f) == LedColour.Off, "the dark is off");
            Program.Check(Led.Classify(0.63f, 0f, 0.63f) == LedColour.Red && Led.Classify(0f, 0.8f, 0.8f) == LedColour.Green, "purple is red, cyan is green");

            // The colours the dots are shown in read back as themselves.
            foreach (LedColour c in new[] { LedColour.Red, LedColour.Amber, LedColour.Green, LedColour.White, LedColour.Blue })
            {
                Led.Rgb(c, out byte r, out byte g, out byte b);
                Program.Check(Led.Classify(r / 255f, g / 255f, b / 255f) == c, c + " shown reads back as " + c);
            }

            Led.Rgb(LedColour.Off, out byte or, out byte og, out byte ob);
            Program.Check(or == 0 && og == 0 && ob == 0, "off is black");
        }

        private static void Dimensions()
        {
            Led.Dimensions(1.2f, 1.2f, 0.025f, out int x, out int y);
            Program.Check(x == 48 && y == 48, "a 1.2 m screen at 25 mm: 48 x 48: " + x + " x " + y);
            Led.Dimensions(4f, 2f, 0.025f, out x, out y);
            Program.Check(x == 160 && y == 80, "4 x 2 m at 25 mm is 160 x 80 = 12800, under the most: " + x + " x " + y);
            Led.Dimensions(8f, 4f, 0.025f, out x, out y);
            Program.Check(x == 320 && y == 160, "8 x 4 m at 25 mm is 320 x 160 = 51200, under the most: " + x + " x " + y);
            Led.Dimensions(10f, 5f, 0.025f, out x, out y);
            Program.Check(x * y <= Led.MaxDots && x > 340 && y > 170, "10 x 5 m at 25 mm would be 400 x 200 = 80000, over the most: made coarser, not cut: " + x + " x " + y + " = " + x * y);
            Program.Check(x * y <= Led.MaxDots && Math.Abs((float)x / y - 2f) < 0.15f, "and keeps its shape");
            Led.Dimensions(0.02f, 0.02f, 0.025f, out x, out y);
            Program.Check(x == 4 && y == 4, "never fewer than 4: " + x + " x " + y);
            Led.Dimensions(1f, 1f, 0f, out x, out y);
            Program.Check(x == 80 && y == 80 && Led.DefaultPitch == 0.0125f, "no pitch: the default 12.5 mm: " + x);
            Program.Check(Led.DotsPerCharacter == 32, "a character is 32 dots high");

            // The picture the dots are read from has enough pixels to a dot, and the face keeps to its own, smaller size.
            Led.SampleSize(174, 107, out int sw, out int sh);
            Program.Check(sw == 174 * Led.SamplesPerDot && sh == 107 * Led.SamplesPerDot, "a default text screen: six pixels to a dot: " + sw + " x " + sh);
            Led.SampleSize(500, 400, out sw, out sh);
            Program.Check(sw == 3000 && sh == 2400, "500 x 400 dots: 3000 x 2400 is under the most: " + sw + " x " + sh);
            Led.SampleSize(800, 400, out sw, out sh);
            Program.Check(Math.Max(sw, sh) == Led.MaxSampleSide && Math.Abs((float)sw / sh - 2f) < 0.01f, "a huge grid: the long side is kept to the most, the shape is kept: " + sw + " x " + sh);
            Led.FaceSize(174, 107, out int fw, out int fh);
            Program.Check(fw == 696 && fh == 428, "the face: four pixels to a dot: " + fw + " x " + fh);
            Led.FaceSize(288, 194, out fw, out fh);
            Program.Check(fw == 864 && fh == 582, "a wide screen: three pixels to a dot (1024 / 288 is 3.55, the whole number below it): " + fw + " x " + fh);
            Led.FaceSize(349, 171, out fw, out fh);
            Program.Check(fw == 698 && fh == 342, "the fullest text screen: two pixels to a dot: " + fw + " x " + fh);
            Led.FaceSize(480, 360, out fw, out fh);
            Program.Check(fw == 960 && fh == 720, "the traffic-state screen: two pixels to a dot: " + fw + " x " + fh);
            Led.FaceSize(300, 200, out fw, out fh);
            Program.Check(fw == 900 && fh == 600, "300 dots: three pixels to a dot: " + fw + " x " + fh);
            foreach (int dots in new[] { 5, 60, 174, 256, 257, 300, 349, 480, 511, 512, 513, 800, 1024 })
            {
                Led.FaceSize(dots, dots * 3 / 4 + 1, out fw, out fh);
                Program.Check(fw % dots == 0 && fh % (dots * 3 / 4 + 1) == 0 && fw / dots == fh / (dots * 3 / 4 + 1) && fw <= Led.MaxFaceSide, "a whole number of pixels to a dot, the same each way, " + dots + " dots: " + fw + " x " + fh);
            }

            Led.FaceSize(1500, 900, out fw, out fh);
            Program.Check(Math.Max(fw, fh) == Led.MaxFaceSide && Math.Abs((float)fw / fh - 1500f / 900f) < 0.01f, "more dots than the face has pixels: fitted, the shape kept: " + fw + " x " + fh);
            Led.FaceSize(4, 4, out fw, out fh);
            Program.Check(fw == 16 && fh == 16, "the smallest face");

            // The face has no dark lines between dots that are the same colour (the dots stand in front of it, glowing, near by: the face is what is seen from afar), at any size.
            foreach ((int gx, int gy) in new[] { (480, 360), (349, 171), (174, 107), (300, 200) })
            {
                var plain = new LedGrid(gx, gy);
                Led.FaceSize(gx, gy, out int rw, out int rh);
                byte[] raster = Led.Raster(plain, rw, rh);
                int odd = 0;
                for (int p = 0; p < raster.Length; p += 4)
                    odd += raster[p] != 14 || raster[p + 1] != 14 || raster[p + 2] != 14 ? 1 : 0;
                Program.Check(odd == 0, "an unlit " + gx + " x " + gy + " screen's face is one even dark, with no line between dots: " + odd + " pixels differ");
            }
            Led.Dimensions(10f, 10f, 0.025f, out x, out y, 100);
            Program.Check(x * y <= 100 && x >= 4, "a smaller most: " + x + " x " + y);
        }

        private static void Quantizing()
        {
            // 40 x 20 pixels, a red block in the top left quarter (columns 0 to 19, rows 0 to 9 from the top), the rest black: 4 x 2 dots of 10 x 10 pixels.
            byte[] pic = Picture(40, 20, (x, yTop) => x < 20 && yTop < 10 ? RedPx : Black);
            LedGrid grid = Led.Quantize(pic, 40, 20, 4, 2);
            Program.Check(grid.At(0, 0) == LedColour.Red && grid.At(1, 0) == LedColour.Red, "the top left is red");
            Program.Check(grid.At(2, 0) == LedColour.Off && grid.At(3, 0) == LedColour.Off && grid.At(0, 1) == LedColour.Off && grid.At(1, 1) == LedColour.Off && grid.At(2, 1) == LedColour.Off && grid.At(3, 1) == LedColour.Off, "everything else is off (the first row of the array is the bottom: the red is not at the foot)");
            Program.Check(grid.Lit == 2 && grid.Count(LedColour.Red) == 2 && grid.PictureW == 40 && grid.PictureH == 20, "two lit dots, the picture's size kept");

            // A dot is lit when at least half its cell is bright: 5 of 10 columns lit is lit, 4 of 10 is not.
            LedGrid half = Led.Quantize(Picture(10, 10, (x, yTop) => x < 5 ? YellowPx : Black), 10, 10, 1, 1);
            LedGrid less = Led.Quantize(Picture(10, 10, (x, yTop) => x < 4 ? YellowPx : Black), 10, 10, 1, 1);
            Program.Check(half.At(0, 0) == LedColour.Amber && less.At(0, 0) == LedColour.Off, "half the cell lights the dot, less does not");

            // The colour is that of the bright pixels, not of the black between them (a dim average would be a different hue).
            Program.Check(half.At(0, 0) == LedColour.Amber, "yellow on black is amber, not brown or off");

            // Transparent pixels do not light: the corner outside a rounded board.
            LedGrid outside = Led.Quantize(Picture(10, 10, (x, yTop) => Clear), 10, 10, 2, 2);
            Program.Check(outside.Lit == 0, "transparent is off");
            LedGrid opaqueWhite = Led.Quantize(Picture(10, 10, (x, yTop) => new byte[] { 255, 255, 255, 255 }), 10, 10, 2, 2);
            Program.Check(opaqueWhite.Count(LedColour.White) == 4, "opaque white is four white dots");

            // Bad input gives an empty screen, not an exception.
            Program.Check(Led.Quantize(null, 10, 10, 2, 2).Lit == 0 && Led.Quantize(new byte[4], 10, 10, 2, 2).Lit == 0 && Led.Quantize(pic, 0, 0, 2, 2).Lit == 0, "no picture, a short picture, a size of 0: nothing lit");
            LedGrid fine = Led.Quantize(pic, 40, 20, 80, 40);
            Program.Check(fine.DotsX == 80 && fine.DotsY == 40 && fine.At(0, 0) == LedColour.Red && fine.At(79, 39) == LedColour.Off, "more dots than pixels is fine");
        }

        private static void RasterRoundTrip()
        {
            // A column of two dots, the top one lit red: the lit pixels are in the upper half of the picture (the array's last rows).
            var column = new LedGrid(1, 2);
            column.Dots[0] = LedColour.Red;
            byte[] pic = Led.Raster(column, 8, 16);
            Program.Check(pic.Length == 8 * 16 * 4, "the size of the picture");
            int topRow = (16 - 1 - 4) * 8 * 4, bottomRow = (16 - 1 - 12) * 8 * 4;
            Program.Check(pic[topRow + 4 * 4] > 200 && pic[topRow + 4 * 4 + 1] < 80, "red in the upper half (at its middle pixel)");
            Program.Check(pic[bottomRow + 4 * 4] < 30 && pic[bottomRow + 4 * 4 + 1] < 30, "the lower dot is a faint dark square, not lit");
            Program.Check(pic[3] == 255 && pic[pic.Length - 1] == 255, "opaque");
            Program.Check(pic[0] == 0 && pic[1] == 0 && pic[2] == 0, "the corner between dots is black");

            // Every colour in a random grid, 8 x 8 pixels a cell: the picture read back is the same dots.
            var rng = new Random(11);
            for (int trial = 0; trial < 6; trial++)
            {
                var grid = new LedGrid(7 + trial, 5 + trial);
                for (int i = 0; i < grid.Dots.Length; i++)
                    grid.Dots[i] = (LedColour)rng.Next(0, 6);
                byte[] raster = Led.Raster(grid, grid.DotsX * 8, grid.DotsY * 8);
                LedGrid back = Led.Quantize(raster, grid.DotsX * 8, grid.DotsY * 8, grid.DotsX, grid.DotsY);
                int differ = 0;
                for (int i = 0; i < grid.Dots.Length; i++)
                    differ += grid.Dots[i] != back.Dots[i] ? 1 : 0;
                Program.Check(differ == 0, $"trial {trial}: the picture of {grid.DotsX} x {grid.DotsY} dots reads back the same: {differ} differ");
            }

            // A picture that is not a multiple of the dots (cells of different sizes) still reads back.
            var odd = new LedGrid(9, 6);
            odd.Dots[0] = LedColour.Green;
            odd.Dots[17] = LedColour.Blue;
            odd.Dots[53] = LedColour.White;
            LedGrid oddBack = Led.Quantize(Led.Raster(odd, 100, 70), 100, 70, 9, 6);
            Program.Check(oddBack.At(0, 0) == LedColour.Green && oddBack.At(8, 1) == LedColour.Blue && oddBack.At(8, 5) == LedColour.White && oddBack.Lit == 3, "a picture of 100 x 70 for 9 x 6 dots reads back: " + oddBack.Lit);
            Program.Check(Led.Raster(null, 4, 4).Length == 64 && Led.Raster(null, 0, 0).Length == 0, "no grid: black");
        }

        private static void Quads()
        {
            var grid = new LedGrid(4, 2);
            grid.Dots[0] = LedColour.Red;          // top left
            grid.Dots[7] = LedColour.Amber;        // bottom right
            grid.Dots[5] = LedColour.Amber;        // bottom, second from the left
            var meshes = new MeshBuilder[6];
            int added = Led.AddDots(meshes, grid, 10f, 6f, 2f, 1f, 0.5f);
            Program.Check(added == 3, "three lit dots: " + added);
            Program.Check(meshes[(int)LedColour.Red] != null && meshes[(int)LedColour.Red].VertexCount == 4 && meshes[(int)LedColour.Amber].VertexCount == 8, "four vertices for each apart, in the mesh of its colour");
            Program.Check(meshes[(int)LedColour.Green] == null && meshes[(int)LedColour.White] == null && meshes[0] == null, "no mesh for a colour that is not lit, none for off");

            // The picture's left is +X: the top-left dot is at the largest X of the screen (10 + 1 - half a pitch), the top row at the largest Y.
            MeshBuilder red = meshes[(int)LedColour.Red];
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (Vec3 p in red.Positions)
            {
                x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y);
                Program.Check(Math.Abs(p.Z - 0.5f) < 1e-6f, "in the plane z");
            }

            Program.Near((x0 + x1) * 0.5f, 10f + 1f - 0.25f, 1e-4f, "the top-left dot is at the picture's left, which is +X: " + (x0 + x1) * 0.5f);
            Program.Near((y0 + y1) * 0.5f, 6f + 0.5f - 0.25f, 1e-4f, "and in the top row: " + (y0 + y1) * 0.5f);
            Program.Near(x1 - x0, 0.5f, 1e-4f, "a lit dot is its whole cell (0.12.24, no gap to beat with the pixels): " + (x1 - x0));
            Program.Near(y1 - y0, 0.5f, 1e-4f, "as high as its row");
            Program.Near(Led.CellLight, Led.Fill * Led.Fill, 1e-6f, "and as bright as a dot's share of it");

            // Side by side in a row, one colour is one quad, edge to edge with the next.
            var row = new LedGrid(6, 1);
            for (int i = 1; i < 4; i++)
                row.Dots[i] = LedColour.Amber;
            row.Dots[4] = LedColour.Red;
            var runs = new MeshBuilder[6];
            Program.Check(Led.AddDots(runs, row, 0f, 0f, 3f, 0.5f, 0f) == 4 && runs[(int)LedColour.Amber].VertexCount == 4 && runs[(int)LedColour.Red].VertexCount == 4, "three amber dots in a row are one quad, the red one its own");
            runs[(int)LedColour.Amber].Bounds(out Vec3 alo, out Vec3 ahi);
            runs[(int)LedColour.Red].Bounds(out Vec3 rlo, out Vec3 rhi);
            Program.Near(ahi.X - alo.X, 1.5f, 1e-5f, "the amber quad is three cells wide");
            Program.Near(alo.X, rhi.X, 1e-6f, "and meets the red one with no gap");

            // Every quad faces +Z (the driver), with normals the same.
            foreach (MeshBuilder mesh in meshes)
            {
                if (mesh == null)
                    continue;
                for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
                {
                    Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                    Vec3 n = MeshBuilder.Cross(b - a, c - a);
                    Program.Check(n.Z > 0f && Math.Abs(n.X) < 1e-9f && Math.Abs(n.Y) < 1e-9f, "a triangle faces the driver");
                }

                foreach (Vec3 n in mesh.Normals)
                    Program.Check(n.Z == 1f, "the normal is +Z");
            }

            Program.Check(Led.AddDots(null, grid, 0f, 0f, 1f, 1f, 0f) == 0 && Led.AddDots(meshes, null, 0f, 0f, 1f, 1f, 0f) == 0, "nothing to draw, nothing added");
        }

        private static void Cabinet()
        {
            // The depth: 0.12 m and 0.08 of the shorter side, kept to 0.20 to 0.45 m.
            Program.Near(Led.CabinetDepth(1.2f, 1.2f), 0.216f, 1e-5f, "a 1.2 m lane signal: 21.6 cm");
            Program.Near(Led.CabinetDepth(4.0f, 3.0f), 0.36f, 1e-5f, "a 4 x 3 m screen: 36 cm");
            Program.Near(Led.CabinetDepth(0.1f, 0.1f), 0.20f, 1e-5f, "a tiny one: at least 20 cm");
            Program.Near(Led.CabinetDepth(30f, 20f), 0.45f, 1e-5f, "a huge one: at most 45 cm");
            Program.Near(Led.CabinetDepth(5f, 1f), Led.CabinetDepth(1f, 5f), 1e-6f, "it follows the shorter side whichever way the screen lies");
            float last = 0f;
            foreach (float side in new[] { 0.5f, 1f, 1.5f, 2f, 3f, 4f, 5f, 8f })
            {
                float depth = Led.CabinetDepth(side * 1.3f, side);
                Program.Check(depth >= last && depth >= 0.20f && depth <= 0.45f, "the depth never falls as the screen grows: " + side + " m: " + depth);
                last = depth;
            }

            foreach (string id in new[] { "gb.vms.text", "gb.vms.lane", "gb.vms.lane.closed", "gb.vms.speed", "gb.vms.traffic" })
            {
                SignInstance instance = Instance(id);
                float depth = Led.CabinetDepth(instance.Board.Main.W, instance.Board.Main.H);
                Program.Check(depth > 4f * SupportLayouts.BoardThickness, id + ": much thicker than a plate (" + depth + " m against " + SupportLayouts.BoardThickness + ")");
                foreach (SupportDef support in SupportCatalog.All)
                {
                    string what = id + " on " + support.Id;
                    SignParts plain = SignBuilder.Build(instance, support);
                    SupportLayout layout = plain.Layout;
                    float face = SignBuilder.FacePlane(layout, instance);
                    Program.Near(face - layout.BackZ, depth, 1e-5f, what + ": the face is a cabinet's depth in front of the back");

                    // The back plane is where it was: the rails, the plates and the clamps are fixed there, so the cabinet does not move them (the structure is the same as for a plate).
                    float backMost = float.MaxValue, frontMost = float.MinValue;
                    plain.Faces[0].Mesh.Bounds(out Vec3 boxLo, out Vec3 boxHi);
                    foreach (Vec3 p in plain.Metal.Positions)
                    {
                        // Only the board's own metal: what is in the board's rectangle and in front of its back plane (the support's tubes and channels are behind it, its posts and feet beside and below it).
                        if (p.Z >= layout.BackZ - 1e-4f && p.X >= boxLo.X - 1e-4f && p.X <= boxHi.X + 1e-4f && p.Y >= boxLo.Y - 1e-4f && p.Y <= boxHi.Y + 1e-4f)
                        {
                            backMost = Math.Min(backMost, p.Z);
                            frontMost = Math.Max(frontMost, p.Z);
                        }
                    }

                    Program.Near(backMost, layout.BackZ, 1e-4f, what + ": the cabinet's back is the board's back plane");
                    Program.Near(frontMost, face + Led.BezelReach, 1e-4f, what + ": the frame stands out of the face by its reach");

                    // Closed on the sides: the area of the side walls (triangles whose normal is across the board and whose corners lie between the back and the face) is the board's perimeter times the
                    // depth. A rim that stayed a plate's thickness (a gap between it and the face) has a thirtieth of that and would leave the box open.
                    float sideArea = 0f;
                    for (int tri = 0; tri + 2 < plain.Metal.Indices.Count; tri += 3)
                    {
                        Vec3 p0 = plain.Metal.Positions[plain.Metal.Indices[tri]], p1 = plain.Metal.Positions[plain.Metal.Indices[tri + 1]], p2 = plain.Metal.Positions[plain.Metal.Indices[tri + 2]];
                        bool inBoard = p0.X >= boxLo.X - 1e-4f && p0.X <= boxHi.X + 1e-4f && p0.Y >= boxLo.Y - 1e-4f && p0.Y <= boxHi.Y + 1e-4f
                            && p1.X >= boxLo.X - 1e-4f && p1.X <= boxHi.X + 1e-4f && p1.Y >= boxLo.Y - 1e-4f && p1.Y <= boxHi.Y + 1e-4f
                            && p2.X >= boxLo.X - 1e-4f && p2.X <= boxHi.X + 1e-4f && p2.Y >= boxLo.Y - 1e-4f && p2.Y <= boxHi.Y + 1e-4f;
                        bool between = p0.Z >= layout.BackZ - 1e-4f && p1.Z >= layout.BackZ - 1e-4f && p2.Z >= layout.BackZ - 1e-4f && p0.Z <= face + 1e-4f && p1.Z <= face + 1e-4f && p2.Z <= face + 1e-4f;
                        Vec3 n = MeshBuilder.Cross(p1 - p0, p2 - p0);
                        float len = n.Length;
                        if (!inBoard || !between || len < 1e-9f || Math.Abs(n.Z / len) > 0.01f)
                            continue;
                        sideArea += len * 0.5f;
                    }

                    float perimeter = 0f;
                    List<Point2> ring = GuideKit.PartOutline(instance.Board.Main);
                    for (int k = 0; k < ring.Count; k++)
                    {
                        Point2 q0 = ring[k], q1 = ring[(k + 1) % ring.Count];
                        perimeter += (float)Math.Sqrt((q1.X - q0.X) * (q1.X - q0.X) + (q1.Y - q0.Y) * (q1.Y - q0.Y));
                    }

                    Program.Check(Math.Abs(sideArea - perimeter * depth) < 0.01f * perimeter * depth + 1e-4f, what + ": the side walls cover the whole perimeter, back to face: area " + sideArea + " against " + perimeter * depth);

                    // The picture of the face is on one plane at the cabinet's front, and the dots stand in front of it by the lift and behind the frame's front.
                    plain.Faces[0].Mesh.Bounds(out Vec3 fLo, out Vec3 fHi);
                    Program.Near(fLo.Z, face, 1e-5f, what + ": the face picture is at the cabinet's front");
                    Program.Near(fHi.Z, face, 1e-5f, what + ": the face picture is one plane");
                    Program.Check(Led.Lift < Led.BezelReach, what + ": the dots are inside the frame's reach");

                    // The pick: at the face, the box from the back to the front of the frame, so a ray at the screen hits it.
                    PickShape pick = null;
                    foreach (PickShape shape in plain.Picks)
                    {
                        if (shape.Name == "board")
                            pick = shape;
                    }

                    Program.Check(pick != null, what + ": there is a pick for the board");
                    if (pick != null)
                    {
                        Program.Near(pick.PlaneZ, face, 1e-5f, what + ": picked at the face");
                        Program.Near(pick.Min.Z, layout.BackZ, 1e-5f, what + ": the pick box starts at the back");
                        Program.Near(pick.Max.Z, face + Led.BezelReach, 1e-5f, what + ": and ends at the front of the frame");
                    }
                }
            }

            // The frame: four bars; the metal of a screen has the eight faces of each (a box each), more than a plate of the same size would.
            SignInstance lane = Instance("gb.vms.lane");
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            SignParts withFrame = SignBuilder.Build(lane, gantry);
            Program.Check(withFrame.Metal.VertexCount > 0, "the metal has vertices");

            // An ordinary board next to a screen keeps its own thickness: the screen's depth is its own, not the support's.
            SignInstance prohibition = SignInstances.Of(SignCatalog.ChoiceById("gb.prohib.09").Def, null);
            SupportLayout layoutOf = SignBuilder.Build(prohibition, gantry).Layout;
            Program.Near(SignBuilder.FacePlane(layoutOf, prohibition) - layoutOf.BackZ, SupportLayouts.BoardThickness, 1e-6f, "an ordinary board is still a plate");
            Program.Near(SignBuilder.FacePlane(layoutOf, null), layoutOf.FrontZ, 1e-6f, "no instance: the layout's front plane");

            // Mixed on one gantry: the lane screen is a cabinet, the board a plate, on the same back plane.
            var members = new System.Collections.Generic.List<ItemRecord>
            {
                new ItemRecord(SignCatalog.HashOf("gb.vms.lane"), new Snap.Vec3(0f, 0f, 0f), 0f, gantry.Hash, null, 0f, 91u, 0, 0, 0f),
                new ItemRecord(SignCatalog.HashOf("gb.prohib.09"), new Snap.Vec3(0f, 0f, 0f), 0f, gantry.Hash, null, 0f, 91u, 1, 0, 0f)
            };
            SignParts mixed = ChunkBuilder.PartsOfGroup(members);
            Program.Check(mixed != null && mixed.Faces.Count == 2, "a screen and a board on one gantry: two faces");
            if (mixed != null && mixed.Faces.Count == 2)
            {
                float screenZ = float.NaN, plateZ = float.NaN;
                foreach (FacePart f in mixed.Faces)
                {
                    f.Mesh.Bounds(out Vec3 lo, out _);
                    if (f.Instance.IsLed)
                        screenZ = lo.Z;
                    else
                        plateZ = lo.Z;
                }

                Program.Near(plateZ, mixed.Layout.FrontZ, 1e-5f, "the board is at the layout's front plane");
                Program.Check(screenZ > plateZ + 0.1f, "the screen's face is in front of the board's: " + screenZ + " against " + plateZ);
            }
        }

        private static SignInstance Instance(string choiceId)
        {
            SignChoice choice = SignCatalog.ChoiceById(choiceId);
            Program.Check(choice != null, "the catalogue has " + choiceId);
            return SignInstances.Of(choice.Def, choice.Spec);
        }

        private static SignInstance With(string choiceId, string overrides)
        {
            SignChoice choice = SignCatalog.ChoiceById(choiceId);
            GuideSpec spec = choice.Def.Template.Parse(choice.Spec);
            foreach (string pair in overrides.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return SignInstances.Of(choice.Def, spec.ToString());
        }

        private static List<GuideElement> Elements(SignInstance instance) => instance.Board.Main.Elements;

        private static int CountOf(SignInstance instance, GuideElementKind kind, GuideColour colour)
        {
            int n = 0;
            foreach (GuideElement e in Elements(instance))
                n += e.Kind == kind && e.Colour == colour ? 1 : 0;
            return n;
        }

        private static void Screens()
        {
            foreach (string id in new[] { "gb.vms.text", "gb.vms.lane", "gb.vms.lane.closed", "gb.vms.speed" })
            {
                SignInstance instance = Instance(id);
                SignChoice choice = SignCatalog.ChoiceById(id);
                Program.Check(instance.IsLed && instance.Board.LedPitch > 0f && instance.Board.Parts.Count == 1, id + " is an LED screen of one part");
                Program.Check(choice.Group == "variable" && choice.Code == "4.11", id + " is in the variable group, code 4.11");
                Program.Check(instance.Board.Main.Ground == GuideColour.Black && instance.Board.Main.Liner == 0f && instance.Board.Main.Border == 0f, id + ": a black screen with no edge");
                Program.Check(instance.Def.Width >= 0.4f && instance.Def.Width <= 8f && instance.Def.Height >= 0.4f && instance.Def.Height <= 4f, id + ": a sensible size: " + instance.Def.Width + " x " + instance.Def.Height);
                Led.Dimensions(instance.Def.Width, instance.Def.Height, instance.Board.LedPitch, out int dx, out int dy);
                Program.Check(dx * dy <= Led.MaxDots, id + ": " + dx + " x " + dy + " dots");
                Program.Check(SupportPlanner.Auto(instance.Def).Kind == SupportKind.Gantry, id + ": it hangs over the road, on a gantry");
                Program.Check(!instance.Spec.Contains("Alt"), id + ": spec");
            }

            // The text screen: the width follows the longest line, empty lines are left out, the colour is the kind of information (Table 10).
            SignInstance two = Instance("gb.vms.text");
            SignInstance longer = With("gb.vms.text", "l1=前方二十公里处发生交通事故");
            Program.Check(longer.Def.Width > two.Def.Width + 0.5f, "a longer line makes the screen wider: " + two.Def.Width + " to " + longer.Def.Width);
            SignInstance four = With("gb.vms.text", "l3=雨天路滑;l4=注意安全");
            Program.Check(four.Def.Height > two.Def.Height + 0.9f, "four lines make it higher: " + two.Def.Height + " to " + four.Def.Height);
            SignInstance off = With("gb.vms.text", "l1=;l2=");
            Program.Check(off.IsLed && off.Def.Width >= 4f * 0.4f && off.Def.Height >= 2f * 0.4f && Elements(off).Count == 0, "an empty screen is a black screen of four characters and two lines (off, 4.11.1)");
            foreach ((string cls, GuideColour colour) in new[] { ("warn", GuideColour.Yellow), ("info", GuideColour.Green), ("red", GuideColour.Red), ("white", GuideColour.White) })
            {
                SignInstance s = With("gb.vms.text", "cls=" + cls);
                bool all = Elements(s).Count > 0;
                foreach (GuideElement e in Elements(s))
                    all &= e.Colour == colour;
                Program.Check(all && VariableSigns.ClassColour(cls) == colour, "the text of " + cls + " information is " + colour + " (Table 10)");
            }

            SignInstance taller = With("gb.vms.text", "h=60");
            Program.Check(taller.Def.Height > two.Def.Height * 1.3f && taller.Board.LedPitch > two.Board.LedPitch, "a taller character: a bigger screen, coarser dots (32 dots to the character): " + taller.Board.LedPitch);
            Program.Near(two.Board.LedPitch, 0.40f / 32f, 1e-5f, "40 cm characters: 12.5 mm dots, 32 to the character");
            Led.Dimensions(two.Def.Width, two.Def.Height, two.Board.LedPitch, out int dxText, out int dyText);
            Program.Check(dxText == 174 && dyText == 107, "the default text screen (two lines of four characters, 0.15 h between the characters, 0.5 h margins) has 174 x 107 dots: " + dxText + " x " + dyText);

            // The lane screens: a green arrow, a red cross, a yellow arrow down and to the side, nothing.
            Program.Check(CountOf(With("gb.vms.lane", "state=pass"), GuideElementKind.Polygon, GuideColour.Green) == 1 && Elements(With("gb.vms.lane", "state=pass")).Count == 1, "open: one green arrow");
            Program.Check(CountOf(With("gb.vms.lane", "state=closed"), GuideElementKind.Polygon, GuideColour.Red) == 2 && Elements(With("gb.vms.lane", "state=closed")).Count == 2, "closed: a red cross of two bars");
            Program.Check(Elements(With("gb.vms.lane", "state=off")).Count == 0, "off: nothing");
            foreach (string side in new[] { "left", "right" })
            {
                SignInstance merge = With("gb.vms.lane", "state=" + side);
                GuideElement arrow = Elements(merge)[0];
                Program.Check(Elements(merge).Count == 1 && arrow.Colour == GuideColour.Yellow && arrow.Kind == GuideElementKind.Polygon, "merge " + side + ": one yellow arrow");
                // The arrow points down and to its side: its tip is the point furthest from the middle, in the lower half and on that side (the viewer's left for left).
                float mid = LaneSideMiddle(), tipX = 0f, tipY = 0f, far = 0f;
                for (int i = 0; i + 1 < arrow.Points.Length; i += 2)
                {
                    float d = (arrow.Points[i] - mid) * (arrow.Points[i] - mid) + (arrow.Points[i + 1] - mid) * (arrow.Points[i + 1] - mid);
                    if (d > far) { far = d; tipX = arrow.Points[i]; tipY = arrow.Points[i + 1]; }
                }

                Program.Check(tipY > mid && (side == "left" ? tipX < mid : tipX > mid), "merge " + side + ": the tip is down and to the " + side + ": " + tipX + ", " + tipY);
            }

            Program.Check(With("gb.vms.lane", "state=pass").Def.Width == VariableSigns.LaneSide, "a lane signal is 1.2 m square");

            // The speed screen: a red ring (a red oval and a black one inside it) and the limit in yellow digits; without a value, the ring only.
            SignInstance speed = Instance("gb.vms.speed");
            Program.Check(CountOf(speed, GuideElementKind.Oval, GuideColour.Red) == 1 && CountOf(speed, GuideElementKind.Oval, GuideColour.Black) == 1, "a red ring");
            bool digits = false;
            foreach (GuideElement e in Elements(speed))
                digits |= e.Kind == GuideElementKind.Text && e.Text == "60" && e.Colour == GuideColour.Yellow;
            Program.Check(digits, "the limit 60 in yellow");
            SignInstance ring = With("gb.vms.speed", "value=");
            bool anyText = false;
            foreach (GuideElement e in Elements(ring))
                anyText |= e.Kind == GuideElementKind.Text;
            Program.Check(!anyText && CountOf(ring, GuideElementKind.Oval, GuideColour.Red) == 1, "without a value, the ring alone");
        }

        private static float LaneSideMiddle() => VariableSigns.LaneSide * 0.5f;

        // ------------------------------------------------------------------ the meshes

        /// <summary>A grid of 6 x 4 dots with five lit, the same for every instance: what the game's painter would make, small enough to check by hand.</summary>
        private static LedGrid Fixed()
        {
            var grid = new LedGrid(6, 4);
            grid.Dots[0] = LedColour.Red;
            grid.Dots[5] = LedColour.Red;
            grid.Dots[10] = LedColour.Green;
            grid.Dots[18] = LedColour.Amber;
            grid.Dots[23] = LedColour.White;
            return grid;
        }

        private static void Meshes()
        {
            Func<SignInstance, LedGrid> keep = SignBuilder.LedGrids;
            try
            {
                int asked = 0;
                SignInstance screen = Instance("gb.vms.lane");
                SignInstance plain = Instance("gb.prohib.01");

                // No source of dots: only the face.
                SignBuilder.LedGrids = null;
                SignParts none = SignBuilder.Build(screen, SupportCatalog.ById(SupportCatalog.Gantry));
                Program.Check(none.Faces.Count == 1 && Array.TrueForAll(none.Leds, m => m == null || m.VertexCount == 0), "without a source of dots there are none, and the face is there");

                SignBuilder.LedGrids = i => { asked++; return Fixed(); };
                foreach (SupportDef support in SupportCatalog.All)
                {
                    SignParts parts = SignBuilder.Build(screen, support);
                    int vertices = 0;
                    for (int c = 1; c < parts.Leds.Length; c++)
                        vertices += parts.Leds[c] == null ? 0 : parts.Leds[c].VertexCount;
                    Program.Check(vertices == 5 * 4, support.Id + ": five lit dots, four vertices each: " + vertices);
                    Program.Check(parts.Leds[(int)LedColour.Red].VertexCount == 8 && parts.Leds[(int)LedColour.Green].VertexCount == 4 && parts.Leds[(int)LedColour.Amber].VertexCount == 4 && parts.Leds[(int)LedColour.White].VertexCount == 4, support.Id + ": by colour");

                    // Inside the face's rectangle and just in front of it.
                    parts.Faces[0].Mesh.Bounds(out Vec3 fLo, out Vec3 fHi);
                    for (int c = 1; c < parts.Leds.Length; c++)
                    {
                        if (parts.Leds[c] == null)
                            continue;
                        foreach (Vec3 p in parts.Leds[c].Positions)
                        {
                            Program.Check(p.X >= fLo.X - 1e-4f && p.X <= fHi.X + 1e-4f && p.Y >= fLo.Y - 1e-4f && p.Y <= fHi.Y + 1e-4f, support.Id + ": a dot is inside the screen");
                            Program.Near(p.Z, SignBuilder.FacePlane(parts.Layout, screen) + Led.Lift, 1e-5f, support.Id + ": in front of the face by the lift");
                        }
                    }
                }

                // A sign that is not a screen never asks for dots.
                int before = asked;
                SignParts stop = SignBuilder.Build(plain, SupportCatalog.ById(SupportCatalog.SinglePole));
                Program.Check(asked == before && Array.TrueForAll(stop.Leds, m => m == null), "a stop sign asks for no dots and has none");

                // Several boards on a support: only the screens have dots, each where its face is.
                var boards = new List<AssemblyBoard>
                {
                    new AssemblyBoard { Instance = screen, Member = 0, Slot = 0 },
                    new AssemblyBoard { Instance = plain, Member = 1, Slot = 1 },
                    new AssemblyBoard { Instance = Instance("gb.vms.speed"), Member = 2, Slot = 2 }
                };
                SignParts group = SignBuilder.BuildAssembly(boards, SupportCatalog.ById(SupportCatalog.Gantry), 0f);
                int total = 0;
                for (int c = 1; c < group.Leds.Length; c++)
                    total += group.Leds[c] == null ? 0 : group.Leds[c].VertexCount;
                Program.Check(total == 2 * 5 * 4, "two screens and a sign: the dots of the two screens: " + total);

                // The parts of a tile: a part for each colour that is lit, "led:n", in the tile's frame and turned with the sign.
                var kAt = new Vec3(100f, 7f, 50f);
                SignDef def = SignCatalog.ById("gb.vms.lane");
                var record = new ItemRecord(def.Hash, kAt, (float)Math.PI * 0.5f, SupportCatalog.ById(SupportCatalog.Gantry).Hash, null, 14f);
                List<ChunkPart> tile = ChunkBuilder.Build(new[] { record }, new Vec3(96f, 0f, 48f), out int skipped);
                Program.Check(skipped == 0, "nothing skipped");
                var ledParts = new Dictionary<string, int>();
                foreach (ChunkPart part in tile)
                {
                    if (part.Material.StartsWith(ChunkBuilder.LedPrefix, StringComparison.Ordinal))
                        ledParts[part.Material] = (ledParts.TryGetValue(part.Material, out int n) ? n : 0) + part.Mesh.VertexCount;
                }

                Program.Check(ledParts.Count == 4 && ledParts["led:1"] == 8 && ledParts["led:3"] == 4 && ledParts["led:2"] == 4 && ledParts["led:4"] == 4, "four colour parts: " + string.Join(",", ledParts.Keys));

                // Turned a quarter about Y (the sign faces +X) the dots are in front of the face on its +X side: the face and the dots share the plane, the dots are nearer to the viewer.
                float faceX = float.MinValue, dotX = float.MaxValue;
                foreach (ChunkPart part in tile)
                {
                    if (part.Material.StartsWith(ChunkBuilder.FacePrefix, StringComparison.Ordinal))
                        foreach (Vec3 p in part.Mesh.Positions)
                            faceX = Math.Max(faceX, p.X);
                    if (part.Material.StartsWith(ChunkBuilder.LedPrefix, StringComparison.Ordinal))
                        foreach (Vec3 p in part.Mesh.Positions)
                            dotX = Math.Min(dotX, p.X);
                }

                Program.Check(dotX > faceX - 1e-4f - 0.0001f && dotX >= faceX - 1e-3f, "the dots are in front of the face when the sign is turned: " + dotX + " vs " + faceX);
                Program.Check(dotX - faceX < 0.01f, "and no further than the lift: " + (dotX - faceX));
            }
            finally
            {
                SignBuilder.LedGrids = keep;
            }
        }
    }
}
