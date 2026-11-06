using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using CDLC.TS.Geo;

namespace CDLC.TS.Render
{
    /// <summary>
    /// Paints a laid-out guide board (GuideBoard) with GDI+: for each part the liner, the white border and the ground, then its elements clipped to the inside, in the units of the
    /// picture that covers the box round all parts. Characters are drawn as outlines (GraphicsPath) so that they can be placed by what they look like: a Chinese character is centred in
    /// its square cell, and letters and digits are scaled so that their ink is as high as the standard's box, which a font's line height cannot give. No Unity types.
    /// </summary>
    internal static class GuidePainter
    {
        private static float s_RefX = float.NaN, s_RefY;
        private static FontFamily s_RefFamily;
        private static FontStyle s_RefStyle;

        /// <summary>Draws the board of an instance into a bitmap of the instance's picture size: the Chinese characters in the Chinese font, the letters and figures in theirs.</summary>
        internal static void Draw(Graphics g, PaintFonts fonts, SignInstance instance, int width, int height)
        {
            GuideBoard board = instance.Board;
            float x0 = instance.BoundsX0, y0 = instance.BoundsY0;
            float sx = width / (instance.BoundsX1 - instance.BoundsX0), sy = height / (instance.BoundsY1 - instance.BoundsY0);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Colour(board.Main.Ground));

            foreach (GuidePart part in board.Parts)
            {
                float px = (part.X - x0) * sx, py = (part.Y - y0) * sy;
                if (part.Shape == GuidePartShape.PointRight || part.Shape == GuidePartShape.PointLeft)
                {
                    DrawPointedPart(g, fonts, part, px, py, sx, sy);
                    continue;
                }

                if (part.Shape != GuidePartShape.RoundRect)
                {
                    DrawShapedPart(g, fonts, part, px, py, sx, sy);
                    continue;
                }

                var outer = new RectangleF(px, py, part.W * sx, part.H * sy);
                float liner = part.Liner * sx, edge = part.Edge * sx;
                using (var ground = new SolidBrush(Colour(part.Ground)))
                using (var border = new SolidBrush(Colour(part.BorderColour)))
                {
                    Fill(g, ground, outer, part.Radius * sx, part.Radius * sx);
                    var ring = Inset(outer, liner, liner * (sy / sx));
                    Fill(g, border, ring, Math.Max(0f, part.Radius - part.Liner) * sx, Math.Max(0f, part.Radius - part.Liner) * sx);
                    var inner = Inset(outer, edge, edge * (sy / sx));
                    Fill(g, ground, inner, part.InnerRadius * sx, part.InnerRadius * sx);

                    // What bleeds over the border (a band that meets the border in the same colour) is a background: drawn first, outside the clip, grown on the three sides
                    // that touch the border. The rest is drawn over it, clipped to the inside.
                    foreach (GuideElement e in part.Elements)
                    {
                        if (e.Bleed <= 0f)
                            continue;
                        float b = e.Bleed;
                        var grown = new GuideElement
                        {
                            Kind = e.Kind, Colour = e.Colour, X = e.X - b, Y = e.Y - b, W = e.W + 2f * b, H = e.H + (e.BleedBottom ? 2f * b : b),
                            RadiusTop = e.RadiusTop + b, RadiusBottom = e.RadiusBottom + (e.BleedBottom ? b : 0f)
                        };
                        DrawElement(g, fonts, grown, px, py, sx, sy);
                    }

                    using (GraphicsPath clip = RoundRect(inner, part.InnerRadius * sx, part.InnerRadius * sx))
                    {
                        g.SetClip(clip);
                        foreach (GuideElement e in part.Elements)
                            DrawElement(g, fonts, e, px, py, sx, sy);
                        g.ResetClip();
                    }
                }
            }
        }

        /// <summary>
        /// A part that is not a rounded rectangle (a circle, an octagon, a triangle): filled with its ground colour over its outline, then its elements in order, no clip (the rings, bands and
        /// figures of these signs are inside the outline by construction, and a hard clip would take the anti-aliasing off the edge). A player's picture is the exception: it fills a box, and is
        /// clipped to the outline (0.12.29).
        /// </summary>
        private static void DrawShapedPart(Graphics g, PaintFonts fonts, GuidePart part, float px, float py, float sx, float sy)
        {
            using (var outline = new GraphicsPath())
            {
                if (part.Shape == GuidePartShape.Circle)
                {
                    outline.AddEllipse(px, py, part.W * sx, part.H * sy);
                }
                else
                {
                    IList<Point2> corners = GuideKit.PartOutline(part);
                    var points = new PointF[corners.Count];
                    for (int i = 0; i < points.Length; i++)
                        points[i] = new PointF(px + (part.W * 0.5f + corners[i].X) * sx, py + (part.H * 0.5f - corners[i].Y) * sy);
                    outline.AddPolygon(points);
                }

                using (var ground = new SolidBrush(Colour(part.Ground)))
                    g.FillPath(ground, outline);

                foreach (GuideElement e in part.Elements)
                {
                    if (e.Kind != GuideElementKind.Image)
                    {
                        DrawElement(g, fonts, e, px, py, sx, sy);
                        continue;
                    }

                    g.SetClip(outline);
                    DrawElement(g, fonts, e, px, py, sx, sy);
                    g.ResetClip();
                }
            }
        }

        /// <summary>A pointed board (路7 a): the outline in its ground colour, the border inside the liner, the ground inside the border, and its elements clipped to that.</summary>
        private static void DrawPointedPart(Graphics g, PaintFonts fonts, GuidePart part, float px, float py, float sx, float sy)
        {
            bool right = part.Shape == GuidePartShape.PointRight;
            PointF[] Map(List<Point2> outline)
            {
                var points = new PointF[outline.Count];
                for (int i = 0; i < points.Length; i++)
                    points[i] = new PointF(px + (part.W * 0.5f + outline[i].X) * sx, py + (part.H * 0.5f - outline[i].Y) * sy);
                return points;
            }

            PointF[] outer = Map(GuideKit.PointedOutline(part.W, part.H, part.Side, part.Radius, 0f, right));
            PointF[] ring = Map(GuideKit.PointedOutline(part.W, part.H, part.Side, part.Radius, part.Liner, right));
            PointF[] inner = Map(GuideKit.PointedOutline(part.W, part.H, part.Side, part.Radius, part.Edge, right));
            using (var ground = new SolidBrush(Colour(part.Ground)))
            using (var border = new SolidBrush(Colour(part.BorderColour)))
            {
                g.FillPolygon(ground, outer);
                g.FillPolygon(border, ring);
                g.FillPolygon(ground, inner);
            }

            using (var clip = new GraphicsPath())
            {
                clip.AddPolygon(inner);
                g.SetClip(clip);
                foreach (GuideElement e in part.Elements)
                    DrawElement(g, fonts, e, px, py, sx, sy);
                g.ResetClip();
            }
        }

        private static Color Colour(GuideColour c)
        {
            GuideColours.Rgb(c, out byte r, out byte g, out byte b);
            return Color.FromArgb(r, g, b);
        }

        private static RectangleF Inset(RectangleF r, float dx, float dy) => new RectangleF(r.X + dx, r.Y + dy, Math.Max(0f, r.Width - 2f * dx), Math.Max(0f, r.Height - 2f * dy));

        // ------------------------------------------------------------------ shapes

        /// <summary>A rectangle with the given radius at the two top corners and at the two bottom corners.</summary>
        private static GraphicsPath RoundRect(RectangleF r, float top, float bottom)
        {
            var path = new GraphicsPath();
            top = Math.Min(top, Math.Min(r.Width, r.Height) * 0.5f);
            bottom = Math.Min(bottom, Math.Min(r.Width, r.Height) * 0.5f);
            float l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
            if (top > 0.01f)
            {
                path.AddArc(l, t, 2f * top, 2f * top, 180f, 90f);
                path.AddArc(rt - 2f * top, t, 2f * top, 2f * top, 270f, 90f);
            }
            else
            {
                path.AddLine(l, t, rt, t);
            }

            if (bottom > 0.01f)
            {
                path.AddArc(rt - 2f * bottom, b - 2f * bottom, 2f * bottom, 2f * bottom, 0f, 90f);
                path.AddArc(l, b - 2f * bottom, 2f * bottom, 2f * bottom, 90f, 90f);
            }
            else
            {
                path.AddLine(rt, b, l, b);
            }

            path.CloseFigure();
            return path;
        }

        private static void Fill(Graphics g, Brush brush, RectangleF r, float top, float bottom)
        {
            if (r.Width <= 0f || r.Height <= 0f)
                return;
            using (GraphicsPath path = RoundRect(r, top, bottom))
                g.FillPath(brush, path);
        }

        // ------------------------------------------------------------------ elements

        private static void DrawElement(Graphics g, PaintFonts fonts, GuideElement e, float px, float py, float sx, float sy)
        {
            var box = new RectangleF(px + e.X * sx, py + e.Y * sy, e.W * sx, e.H * sy);
            using (var brush = new SolidBrush(Colour(e.Colour)))
            {
                switch (e.Kind)
                {
                    case GuideElementKind.Rect:
                        Fill(g, brush, box, e.RadiusTop * sx, e.RadiusBottom * sx);
                        if (e.StrokeWidth > 0f)
                        {
                            // The stroke is inside the box.
                            float w = e.StrokeWidth * sx;
                            RectangleF middle = Inset(box, w * 0.5f, w * 0.5f);
                            using (GraphicsPath path = RoundRect(middle, Math.Max(0f, e.RadiusTop * sx - w * 0.5f), Math.Max(0f, e.RadiusBottom * sx - w * 0.5f)))
                            using (var pen = new Pen(Colour(e.Stroke), w) { LineJoin = LineJoin.Round })
                                g.DrawPath(pen, path);
                        }

                        break;
                    case GuideElementKind.Oval:
                        g.FillEllipse(brush, box);
                        break;
                    case GuideElementKind.Path:
                    {
                        // The contours are filled together by the non-zero winding rule, as SVG does: counter-clockwise shapes join, a clockwise one is a hole.
                        if (e.Contours == null)
                            break;
                        using (var path = new GraphicsPath(FillMode.Winding))
                        {
                            foreach (float[] contour in e.Contours)
                            {
                                var points = new PointF[contour.Length / 2];
                                for (int i = 0; i < points.Length; i++)
                                    points[i] = new PointF(px + contour[i * 2] * sx, py + contour[i * 2 + 1] * sy);
                                if (points.Length >= 3)
                                    path.AddPolygon(points);
                            }

                            g.FillPath(brush, path);
                        }

                        break;
                    }

                    case GuideElementKind.Polygon:
                    case GuideElementKind.Arrow:
                    {
                        var points = new PointF[e.Points.Length / 2];
                        for (int i = 0; i < points.Length; i++)
                            points[i] = new PointF(px + e.Points[i * 2] * sx, py + e.Points[i * 2 + 1] * sy);
                        g.FillPolygon(brush, points);
                        break;
                    }

                    case GuideElementKind.Image:
                        CustomPictures.Draw(g, e, box);
                        break;

                    case GuideElementKind.Text:
                        if (!string.IsNullOrEmpty(e.Text))
                        {
                            if (e.Mode == GuideTextMode.Cell)
                                DrawCells(g, fonts, brush, e, box, sx, sy);
                            else
                                DrawInk(g, fonts, brush, e, box);
                        }

                        break;
                }
            }
        }

        /// <summary>Where the ink of a full-width character's middle lies in a path drawn at an em of 1 (the font's line is not the em: this finds the offset once for the font).</summary>
        private static void Reference(FontFamily family, FontStyle style)
        {
            if (ReferenceEquals(s_RefFamily, family) && s_RefStyle == style && !float.IsNaN(s_RefX))
                return;
            using (var path = new GraphicsPath())
            {
                path.AddString("国", family, (int)style, 100f, new PointF(0f, 0f), StringFormat.GenericTypographic);
                RectangleF b = path.GetBounds();
                s_RefX = (b.X + b.Width * 0.5f) / 100f;
                s_RefY = (b.Y + b.Height * 0.5f) / 100f;
                s_RefFamily = family;
                s_RefStyle = style;
            }
        }

        /// <summary>Each character in a square cell as high as the box, a gap between cells, the whole run aligned in the box. The character's ink is centred in its cell by the reference character's, so that every character sits at the same place.</summary>
        private static void DrawCells(Graphics g, PaintFonts fonts, Brush brush, GuideElement e, RectangleF box, float sx, float sy)
        {
            // Characters in square cells are the Chinese font's (a figure in a cell is centred as a character is).
            FontFamily family = fonts.Cjk;
            FontStyle style = fonts.CjkStyle;
            Reference(family, style);
            float cellW = e.H * sx, cellH = e.H * sy, pitch = (e.H + e.Gap) * sx;
            float total = cellW * e.Text.Length + e.Gap * sx * Math.Max(0, e.Text.Length - 1);
            float left = e.Align == GuideAlign.Right ? box.Right - total : e.Align == GuideAlign.Centre ? box.Left + (box.Width - total) * 0.5f : box.Left;
            float em = cellH;
            for (int i = 0; i < e.Text.Length; i++)
            {
                string c = e.Text.Substring(i, 1);
                if (c == " ")
                    continue;
                using (var path = new GraphicsPath())
                {
                    float cx = left + i * pitch + cellW * 0.5f, cy = box.Top + cellH * 0.5f;
                    path.AddString(c, family, (int)style, em, new PointF(cx - s_RefX * em, cy - s_RefY * em), StringFormat.GenericTypographic);
                    g.FillPath(brush, path);
                }
            }
        }

        /// <summary>
        /// Letters and digits: the ink as high as the box (or as the ink of the reference string), the bottom of the ink on the bottom of the box, squeezed when wider than the box. A Line (an English
        /// line under Chinese) is as high as a capital H is and stands on its baseline, whatever letters it has: a "g" goes below the box.
        /// </summary>
        private static void DrawInk(Graphics g, PaintFonts fonts, Brush brush, GuideElement e, RectangleF box)
        {
            // Letters and figures are the letters' font, its reference string with them.
            FontFamily family = fonts.For(e.Text, out FontStyle style);
            using (var path = new GraphicsPath())
            {
                path.AddString(e.Text, family, (int)style, 100f, new PointF(0f, 0f), StringFormat.GenericTypographic);
                RectangleF b = path.GetBounds();
                if (b.Width <= 0f || b.Height <= 0f)
                    return;

                float referenceHeight = b.Height, referenceBottom = b.Bottom;
                if (e.Mode == GuideTextMode.Line)
                {
                    using (var capital = new GraphicsPath())
                    {
                        capital.AddString("H", family, (int)style, 100f, new PointF(0f, 0f), StringFormat.GenericTypographic);
                        RectangleF hb = capital.GetBounds();
                        referenceHeight = hb.Height;
                        referenceBottom = hb.Bottom;
                    }
                }
                else if (!string.IsNullOrEmpty(e.InkRef))
                {
                    using (var reference = new GraphicsPath())
                    {
                        reference.AddString(e.InkRef, family, (int)style, 100f, new PointF(0f, 0f), StringFormat.GenericTypographic);
                        referenceHeight = reference.GetBounds().Height;
                    }
                }

                float scale = box.Height / referenceHeight;
                float natural = b.Width * scale;
                float squeeze = natural > box.Width && box.Width > 0f ? Math.Max(0.5f, box.Width / natural) : 1f;
                float width = natural * squeeze;
                float left = e.Align == GuideAlign.Right ? box.Right - width : e.Align == GuideAlign.Centre ? box.Left + (box.Width - width) * 0.5f : box.Left;
                var matrix = new Matrix(scale * squeeze, 0f, 0f, scale, left - b.Left * scale * squeeze, box.Bottom - referenceBottom * scale);
                path.Transform(matrix);
                g.FillPath(brush, path);
            }
        }
    }
}
