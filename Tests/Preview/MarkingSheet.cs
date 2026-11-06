using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// The road markings of RoadMarkings (GB 5768.3-2025, decoration only) seen from straight above, to look at: an approach of three lanes with its dashed lane lines, the stop line and the crosswalk; a
    /// two-way road with the yellow centre line and the stop-and-give-way line with its word; and a bend with the slow dashes. The word is drawn from the mod's font into its box, as the renderer will.
    /// Usage: Preview.exe root out --markings. This is the geometry only, in a software picture: nothing of it is in the game.
    /// </summary>
    internal static class MarkingSheet
    {
        private const float Scale = 40f;

        private sealed class Panel
        {
            public string Title;
            public float X0, X1, Z0, Z1;
            public readonly List<(MarkingShape Shape, Vec3 Offset)> Shapes = new List<(MarkingShape, Vec3)>();
            public Action<Graphics, Func<float, float, PointF>> Road;
        }

        public static int Run(string output, string fontFile)
        {
            int first = Draw(output, fontFile, "markings.png", new List<Panel> { Approach(), TwoWay(), Bend(), ArrowPanel(), SizePanel() });
            if (first != 0)
                return first;
            int second = Draw(output, fontFile, "markings-2.png", new List<Panel> { BayPanel(), BusPanel(), HumpPanel(), AreaPanel(), RumblePanel(), TollPanel(), GorePanel() });
            return second != 0 ? second : Draw(output, fontFile, "markings-3.png", new List<Panel> { LetterPanel() });
        }

        private static int Draw(string output, string fontFile, string fileName, List<Panel> panels)
        {
            int width = 0, height = 0;
            foreach (Panel panel in panels)
            {
                width += (int)((panel.X1 - panel.X0) * Scale) + 20;
                height = Math.Max(height, (int)((panel.Z1 - panel.Z0) * Scale) + 50);
            }

            using (var fonts = new PrivateFontCollection())
            using (var bitmap = new Bitmap(width + 20, height + 20, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                FontFamily family = null;
                try
                {
                    fonts.AddFontFile(fontFile);
                    family = fonts.Families[0];
                }
                catch (Exception ex)
                {
                    Console.WriteLine("markings: the font could not be loaded (" + ex.Message + "); the word is drawn as a box");
                }

                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(40, 40, 44));
                float left = 20f;
                foreach (Panel panel in panels)
                {
                    float panelWidth = (panel.X1 - panel.X0) * Scale, panelHeight = (panel.Z1 - panel.Z0) * Scale;
                    float top = 40f;
                    Func<float, float, PointF> map = (x, z) => new PointF(left + (x - panel.X0) * Scale, top + panelHeight - (z - panel.Z0) * Scale);
                    g.SetClip(new RectangleF(left, top, panelWidth, panelHeight));
                    g.Clear(Color.FromArgb(40, 40, 44));
                    g.FillRectangle(new SolidBrush(Color.FromArgb(0x5a, 0x5a, 0x5e)), left, top, panelWidth, panelHeight);
                    panel.Road?.Invoke(g, map);
                    foreach ((MarkingShape shape, Vec3 offset) in panel.Shapes)
                    {
                        using (var brush = new SolidBrush(shape.Colour == MarkingColour.Yellow ? Color.FromArgb(0xf2, 0xc4, 0x1a) : Color.FromArgb(0xf4, 0xf4, 0xf0)))
                        {
                            // All the triangles of a shape as one path filled by the winding rule: the seam between the two triangles of a quad does not show as a hairline.
                            MeshBuilder mesh = shape.Mesh;
                            using (var union = new GraphicsPath(FillMode.Winding))
                            {
                                for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
                                {
                                    var points = new PointF[3];
                                    for (int k = 0; k < 3; k++)
                                    {
                                        Vec3 p = mesh.Positions[mesh.Indices[i + k]];
                                        points[k] = map(p.X + offset.X, p.Z + offset.Z);
                                    }

                                    union.AddPolygon(points);
                                }

                                g.FillPath(brush, union);
                            }

                            // The words are the font's own outlines (MarkingLetters), triangulated like the lines.
                            if (shape.Texts.Count > 0)
                            {
                                var letters = new MeshBuilder();
                                MarkingLetters.AddTexts(letters, shape.Texts);
                                using (var union = new GraphicsPath(FillMode.Winding))
                                {
                                    for (int i = 0; i + 2 < letters.Indices.Count; i += 3)
                                    {
                                        var points = new PointF[3];
                                        for (int k = 0; k < 3; k++)
                                        {
                                            Vec3 p = letters.Positions[letters.Indices[i + k]];
                                            points[k] = map(p.X + offset.X, p.Z + offset.Z);
                                        }

                                        union.AddPolygon(points);
                                    }

                                    g.FillPath(brush, union);
                                }
                            }
                        }
                    }

                    g.ResetClip();
                    using (var font = new Font("Segoe UI", 10f))
                        g.DrawString(panel.Title, font, Brushes.White, left, 12f);
                    left += panelWidth + 20f;
                }

                string path = Path.Combine(output, fileName);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine($"markings: {path}, {bitmap.Width}x{bitmap.Height}");
            }

            return 0;
        }

        private static void DrawWord(Graphics g, Func<float, float, PointF> map, Brush brush, FontFamily family, MarkingText text, Vec3 offset)
        {
            PointF a = map(text.X + offset.X - text.Width * 0.5f, text.Z + offset.Z + text.Length * 0.5f);
            PointF b = map(text.X + offset.X + text.Width * 0.5f, text.Z + offset.Z - text.Length * 0.5f);
            var box = new RectangleF(a.X, a.Y, b.X - a.X, b.Y - a.Y);
            if (family == null)
            {
                using (var pen = new Pen(brush, 2f))
                    g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                return;
            }

            using (var path = new GraphicsPath())
            {
                path.AddString(text.Text, family, (int)FontStyle.Bold, 100f, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF bounds = path.GetBounds();
                var matrix = new Matrix();
                matrix.Translate(-bounds.X, -bounds.Y);
                matrix.Scale(box.Width / bounds.Width, box.Height / bounds.Height, MatrixOrder.Append);
                matrix.Translate(box.X, box.Y, MatrixOrder.Append);
                path.Transform(matrix);
                g.FillPath(brush, path);
            }
        }

        /// <summary>Three lanes of 3.5 m going up the picture: white dashed lines between them (6-9), the stop line (30 cm), the crosswalk (40 cm stripes).</summary>
        private static Panel Approach()
        {
            var panel = new Panel { Title = "three lanes: 6-9 lines, stop line, crosswalk", X0 = -6.5f, X1 = 6.5f, Z0 = 0f, Z1 = 30f };
            const float lane = 3.5f;
            for (int i = -1; i <= 1; i += 2)
            {
                var line = new List<Vec3> { new Vec3(i * lane * 0.5f, 0f, 0f), new Vec3(i * lane * 0.5f, 0f, 17f) };
                panel.Shapes.Add((RoadMarkings.Dashed(line, RoadMarkings.LaneLineFast()), Vec3.Zero));
            }

            panel.Shapes.Add((RoadMarkings.StopLine(3f * lane, 0.3f), new Vec3(0f, 0f, 17f)));
            panel.Shapes.Add((RoadMarkings.Crosswalk(3f * lane, 0.4f, 3f), new Vec3(0f, 0f, 20.5f)));
            return panel;
        }

        /// <summary>A two-way road: the yellow centre line (4-6) and, on the driver's lane, the stop-and-give-way line with the word.</summary>
        private static Panel TwoWay()
        {
            var panel = new Panel { Title = "two-way: centre line 4-6, give-way line and the word", X0 = -4.5f, X1 = 4.5f, Z0 = 0f, Z1 = 30f };
            var centre = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 30f) };
            panel.Shapes.Add((RoadMarkings.Dashed(centre, RoadMarkings.CentreLine()), Vec3.Zero));
            panel.Shapes.Add((RoadMarkings.GiveWay(3.5f, new[] { 0f }), new Vec3(1.75f, 0f, 22f)));
            return panel;
        }

        /// <summary>The eleven guide arrows of Fig. 43 (6 m), in the order of Table 1, 2.5 m apart, to set beside the figure.</summary>
        private static Panel ArrowPanel()
        {
            var panel = new Panel { Title = "guide arrows, Fig. 43 (6 m), the order of Table 1", X0 = -1.5f, X1 = 11 * 2.8f - 1.3f, Z0 = -0.5f, Z1 = 6.5f };
            int i = 0;
            foreach (ArrowKind kind in Enum.GetValues(typeof(ArrowKind)))
                panel.Shapes.Add((RoadArrows.Shape(kind, ArrowSize.Line31), new Vec3(2.8f * i++, 0f, 0f)));
            return panel;
        }

        /// <summary>The four sizes (Figs. 42 to 45) of the straight-or-left arrow side by side.</summary>
        private static Panel SizePanel()
        {
            var panel = new Panel { Title = "the four sizes: 3 m, 6 m, 9 m, 4.5 m", X0 = -1.5f, X1 = 12f, Z0 = -0.5f, Z1 = 9.5f };
            float x = 0f;
            foreach (ArrowSize size in new[] { ArrowSize.Line30, ArrowSize.Line31, ArrowSize.Line32, ArrowSize.Line33 })
            {
                panel.Shapes.Add((RoadArrows.Shape(ArrowKind.StraightLeft, size), new Vec3(x, 0f, 0f)));
                x += 1.5f + 1.5f * RoadArrows.ScaleOf(size);
            }

            return panel;
        }

        private static void Add(Panel panel, MarkingShape shape, float x = 0f, float z = 0f) => panel.Shapes.Add((shape, new Vec3(x, 0f, z)));

        private static void Add(Panel panel, IEnumerable<MarkingShape> shapes, float x = 0f, float z = 0f)
        {
            foreach (MarkingShape s in shapes)
                Add(panel, s, x, z);
        }

        /// <summary>Bays along a kerb at x = 0 (the kerb on the right): perpendicular with arrows and a disabled bay's zone, angled, parallel with the taxi words, time-limited dashed.</summary>
        private static Panel BayPanel()
        {
            var panel = new Panel { Title = "bays: perpendicular (arrow, 线17), disabled zone (线20), angled 45, taxi (线18), time-limited (线22)", X0 = -8f, X1 = 1f, Z0 = -0.5f, Z1 = 52f };
            Add(panel, MarkingFigures.BayRow(3, 2.5f, 5f, 90f, LineSide.Solid(0.1f, MarkingColour.White)));
            for (int i = 0; i < 3; i++)
            {
                Vec3 c = MarkingFigures.BayCentre(i, 2.5f, 5f, 90f, out _);
                Add(panel, Rotate(MarkingFigures.BayArrow(), -90f), c.X + 0.6f, c.Z);
            }

            Add(panel, MarkingFigures.HatchedZone(-5f, 7.5f, 0f, 7.5f + 1.2f));
            Add(panel, MarkingFigures.BayRow(3, 3.5f, 5f, 45f, LineSide.Solid(0.08f, MarkingColour.White)), 0f, 10f);
            MarkingShape taxi = MarkingFigures.BayRow(2, 6f, 2.5f, 90f, LineSide.Solid(0.1f, MarkingColour.White), kerbLine: true);
            for (int i = 0; i < 2; i++)
            {
                Vec3 c = MarkingFigures.BayCentre(i, 6f, 2.5f, 90f, out _);
                taxi.Texts.AddRange(MarkingFigures.BayWords("出租车", c, MarkingFigures.BayWordWidth, MarkingFigures.BayWordHeight, MarkingFigures.BayWordGap));
            }

            Add(panel, taxi, 0f, 25f);
            Add(panel, MarkingFigures.BayRow(2, 6f, 2.5f, 90f, LineSide.Dashed(0.1f, MarkingColour.White, 0.6f, 0.6f)), 0f, 39f);
            return panel;
        }

        private static MarkingShape Rotate(MarkingShape shape, float degrees)
        {
            var turned = new MarkingShape { Colour = shape.Colour };
            turned.Mesh.Append(shape.Mesh, Vec3.Zero, degrees * (float)Math.PI / 180f);
            return turned;
        }

        /// <summary>The bus bay in forms a) to e) side by side (kerb on the right of each), and the kerb stop.</summary>
        private static Panel BusPanel()
        {
            var panel = new Panel { Title = "bus bays 线25 a) to e) (tapers 25, straight 30, depth 3) and the kerb stop 线26", X0 = -0.5f, X1 = 6 * 5f, Z0 = -0.5f, Z1 = 81f };
            int i = 0;
            foreach (BusBayForm form in Enum.GetValues(typeof(BusBayForm)))
                Add(panel, MarkingFigures.BusBay(form, 25f, 30f, 25f, 3f), 5f * i++, 0f);
            Add(panel, MarkingFigures.KerbStop(20f, 3f, "校车"), 5f * i, 25f);
            return panel;
        }

        /// <summary>The humps: 线27, 线28, 线29 and the pedestrian arrow and diamond.</summary>
        private static Panel HumpPanel()
        {
            var panel = new Panel { Title = "线27 hump chevron, 线28 small hump, 线29 warning bars, 线11 diamond, Fig. 17 arrow", X0 = -2f, X1 = 10f, Z0 = -0.5f, Z1 = 31f };
            Add(panel, MarkingFigures.HumpChevron(), 0f, 0f);
            Add(panel, MarkingFigures.SmallHumpTriangles(5), 0f, 3f);
            Add(panel, MarkingFigures.HumpWarningBars(), 4f, 0f);
            Add(panel, MarkingFigures.Diamond(), 8f, 0f);
            Add(panel, MarkingFigures.PedestrianArrow(), 8f, 5f);
            return panel;
        }

        /// <summary>Areas: the grids, the centre circle and diamond, no-parking lines, the variable lane line, the no-U-turn mark.</summary>
        private static Panel AreaPanel()
        {
            var panel = new Panel { Title = "线57 grid, 线58 simple grid, 线55/56 centre marks, 线49/51, 线62, 线63", X0 = -1f, X1 = 24f, Z0 = -0.5f, Z1 = 12f };
            Add(panel, MarkingFigures.Grid(6f, 8f, 1f), 3f, 0f);
            Add(panel, MarkingFigures.SimpleGrid(5f, 8f), 9.5f, 0f);
            Add(panel, MarkingFigures.CentreCircle(2f), 14f, 2f);
            Add(panel, MarkingFigures.CentreDiamond(2f), 14f, 6f);
            Add(panel, MarkingFigures.NoParkingLine(8f, true), 17f, 0f);
            Add(panel, MarkingFigures.NoParkingLine(8f, false), 17.6f, 0f);
            Add(panel, MarkingFigures.VariableGuideLaneLine(new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 10f) }, true), 18.5f, 0f);
            Add(panel, MarkingFigures.ForbiddenTurn(ArrowKind.UTurn, ArrowSize.Line31), 22f, 2f);
            return panel;
        }

        /// <summary>Rumble: a group of transverse bars, the longitudinal blocks with their taper.</summary>
        private static Panel RumblePanel()
        {
            var panel = new Panel { Title = "线66 transverse rumble (two groups), 线67/68 longitudinal with taper", X0 = -2.5f, X1 = 4f, Z0 = -0.5f, Z1 = 40f };
            Add(panel, MarkingWarnings.TransverseRumble(3.5f, 2));
            Add(panel, MarkingWarnings.LongitudinalRumble(40f, taper: true), 2.5f, 0f);
            return panel;
        }

        /// <summary>Toll: three groups of the plaza bars and the island; the level crossing.</summary>
        private static Panel TollPanel()
        {
            var panel = new Panel { Title = "线69 toll plaza (nearest three groups), 线70 toll island, 线71 level crossing", X0 = -6.5f, X1 = 10f, Z0 = -1f, Z1 = 20f };
            Add(panel, MarkingWarnings.TollPlazaGroup(6f, 1), -3.5f, 0f);
            Add(panel, MarkingWarnings.TollPlazaGroup(6f, 1), -3.5f, 5f);
            Add(panel, MarkingWarnings.TollPlazaGroup(6f, 2), -3.5f, 14f);
            Add(panel, MarkingWarnings.TollIsland(2.2f), 2f, 0f);
            Add(panel, MarkingWarnings.LevelCrossing(3.5f), 6.5f, 1f);
            return panel;
        }

        /// <summary>The exit gore and a channelising island.</summary>
        private static Panel GorePanel()
        {
            var panel = new Panel { Title = "线65 exit gore (30 m), 导流线 island (chevron) and obstacle approach (yellow)", X0 = -1f, X1 = 14f, Z0 = -20f, Z1 = 31f };
            Add(panel, MarkingWarnings.ExitGore(30f, 4f, 20f));
            Add(panel, MarkingWarnings.Channelising(new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(1.5f, 0f, 20f), new Vec3(-1.5f, 0f, 20f) }, 0.15f, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White), 8f, 0f);
            Add(panel, MarkingWarnings.ObstacleApproach(20f, 1f, opposing: true), 12f, 0f);
            return panel;
        }

        /// <summary>The lettering of the standard's markings, from the font: 停 in its 100 x 250 box (Fig. 76), the words of a bus bay (100 x 250, 200 apart, the first character nearest), a taxi stand, the railway words, a limit and a time.</summary>
        private static Panel LetterPanel()
        {
            var panel = new Panel { Title = "lettering from the font: 停 (Fig. 76), 公交车 (Fig. 33), 出租车 (Fig. 24), 铁 路 (Fig. 110), 100 / 80 (Fig. 49), 17-18 7-8 (Fig. 87)", X0 = -1f, X1 = 28f, Z0 = -0.5f, Z1 = 9f };
            var stop = new MarkingShape { Colour = MarkingColour.White };
            stop.Texts.Add(new MarkingText { Text = "停", X = 0f, Z = 1.25f, Width = 1f, Length = 2.5f });
            Add(panel, stop);
            var bus = new MarkingShape { Colour = MarkingColour.White };
            bus.Texts.AddRange(MarkingFigures.BayWords("公交车", new Vec3(0f, 0f, 4.5f), MarkingFigures.BusWordWidth, MarkingFigures.BusWordHeight, MarkingFigures.BusWordGap));
            Add(panel, bus, 3f, 0f);
            var taxi = new MarkingShape { Colour = MarkingColour.White };
            taxi.Texts.AddRange(MarkingFigures.BayWords("出租车", new Vec3(0f, 0f, 2f), MarkingFigures.BayWordWidth, MarkingFigures.BayWordHeight, MarkingFigures.BayWordGap));
            Add(panel, taxi, 6f, 0f);
            var rail = new MarkingShape { Colour = MarkingColour.White };
            rail.Texts.Add(new MarkingText { Text = "铁", X = 0f, Z = 1f, Width = 0.7f, Length = 2f });
            rail.Texts.Add(new MarkingText { Text = "路", X = 1.2f, Z = 1f, Width = 0.7f, Length = 2f });
            Add(panel, rail, 8.5f, 0f);
            var limit = new MarkingShape { Colour = MarkingColour.Yellow };
            limit.Texts.Add(new MarkingText { Text = "100", X = 0f, Z = 3f, Width = 3f, Length = 6f });
            Add(panel, limit, 13f, 0f);
            var min = new MarkingShape { Colour = MarkingColour.White };
            min.Texts.Add(new MarkingText { Text = "80", X = 0f, Z = 3f, Width = 2f, Length = 6f });
            Add(panel, min, 17.5f, 0f);
            var time = new MarkingShape { Colour = MarkingColour.White };
            time.Texts.Add(new MarkingText { Text = "17-18", X = 0f, Z = 1.5f, Width = 2.5f, Length = 1.2f });
            time.Texts.Add(new MarkingText { Text = "7-8", X = 0f, Z = 0.2f, Width = 1.5f, Length = 1.2f });
            Add(panel, time, 22f, 0f);
            return panel;
        }

        /// <summary>A bend of radius 18 m: the slow lane line (2-4) following it.</summary>
        private static Panel Bend()
        {
            var panel = new Panel { Title = "a bend: 2-4 line follows the curve", X0 = -2f, X1 = 22f, Z0 = 0f, Z1 = 22f };
            const float r = 18f;
            var arc = new List<Vec3>();
            for (int i = 0; i <= 60; i++)
            {
                double a = Math.PI * 0.5 * i / 60;
                arc.Add(new Vec3((float)(r * (1.0 - Math.Cos(a))), 0f, (float)(r * Math.Sin(a))));
            }

            panel.Shapes.Add((RoadMarkings.Dashed(arc, RoadMarkings.LaneLineSlow()), Vec3.Zero));
            return panel;
        }
    }
}
