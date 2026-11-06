using System;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The editor's picture of a text light box (the owner, 2 Nov: a black box where the board's picture is): the letters as the viewer sees them, filled in the glow colour on a dark ground, from the
    /// plan's glyph meshes. The board has no picture of its own (its letters are solids), so the generic painter gave a black face. Pure; RGBA32, the bottom row first, as the painters give it.
    /// </summary>
    public static class TextFacePreview
    {
        /// <summary>The longest side of the picture (pixels): the editor shows it at most 300 by 140 of its own units.</summary>
        public const int LongSide = 512;

        public static readonly byte[] Ground = { 22, 24, 28 };

        private const int Samples = 3;

        /// <summary>
        /// The picture of a text board from its plan, or null when there is nothing to draw (no layout, no plan). The width and height follow the board's box; a text with no letter is the dark ground.
        /// </summary>
        public static byte[] Raster(TextBoard text, TextPlanResult plan, byte[] glow, out int width, out int height)
        {
            width = height = 0;
            if (text == null || text.Layout == null || plan == null || plan.Layout == null || plan.Meshes == null || glow == null || glow.Length < 3)
                return null;
            // The board's box: a leaning letter reaches out of its cell, so the box is wider for it (on an arc the layout's box has it already).
            float lean = text.Job != null && text.Layout.Arc == null ? text.Job.Art.Slant * text.Job.LetterHeight : 0f;
            float boardW = Math.Max(TextBoxes.MinBox, text.Layout.Width + lean), boardH = Math.Max(TextBoxes.MinBox, text.Layout.Height);
            float scale = LongSide / Math.Max(boardW, boardH);
            width = Math.Max(16, (int)Math.Round(boardW * scale));
            height = Math.Max(16, (int)Math.Round(boardH * scale));
            var cover = new float[width * height];
            // The colour of an emoji's layers: painted over the glow picture, in order, each by its own coverage (the glow colour is the board's, the layers' the palette's).
            var emojiColour = new byte[width * height * 4];
            var emojiShare = new float[width * height];
            TextLayoutResult placed = plan.Layout;
            float mpe = placed.MetresPerEm;
            for (int g = 0; g < plan.Meshes.Length && g < placed.Glyphs.Count; g++)
            {
                GlyphMesh mesh = plan.Meshes[g];
                if (mesh == null || mesh.Points == null || mesh.Triangles == null)
                    continue;
                PlacedGlyph glyph = placed.Glyphs[g];
                if (mesh.Emoji != null && mesh.Emoji.Layers.Count > 0)
                {
                    foreach (EmojiLayer layer in mesh.Emoji.Layers)
                    {
                        var layerCover = new float[width * height];
                        for (int t = 0; t + 2 < layer.Mesh.Triangles.Length; t += 3)
                        {
                            Point2 a = At(layer.Mesh, layer.Mesh.Triangles[t], glyph, mpe, boardW, boardH, scale), b = At(layer.Mesh, layer.Mesh.Triangles[t + 1], glyph, mpe, boardW, boardH, scale), c = At(layer.Mesh, layer.Mesh.Triangles[t + 2], glyph, mpe, boardW, boardH, scale);
                            Fill(layerCover, width, height, a, b, c);
                        }

                        byte[] rgb = CDLC.TS.Geo.Raster.SolidRgb(layer.Colour);
                        for (int i = 0; i < layerCover.Length; i++)
                        {
                            float k = Math.Min(1f, layerCover[i]);
                            if (k <= 0f)
                                continue;
                            // Premultiplied, "over": the colour so far keeps what the layer does not cover; the share is the coverage of all the layers together.
                            for (int ch = 0; ch < 3; ch++)
                                emojiColour[i * 4 + ch] = (byte)Math.Round(Math.Min(255f, emojiColour[i * 4 + ch] * (1f - k) + rgb[ch] * k));
                            emojiShare[i] = emojiShare[i] * (1f - k) + k;
                        }
                    }

                    continue;
                }

                // Viewer's frame, y up: the board's middle is the origin; pixel row 0 is the bottom.
                for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
                {
                    Point2 a = At(mesh, mesh.Triangles[t], glyph, mpe, boardW, boardH, scale), b = At(mesh, mesh.Triangles[t + 1], glyph, mpe, boardW, boardH, scale), c = At(mesh, mesh.Triangles[t + 2], glyph, mpe, boardW, boardH, scale);
                    Fill(cover, width, height, a, b, c);
                }
            }

            var rgba = new byte[width * height * 4];
            for (int i = 0; i < cover.Length; i++)
            {
                float k = Math.Min(1f, cover[i]);
                float e = emojiShare[i];
                for (int c = 0; c < 3; c++)
                {
                    float own = Ground[c] + (glow[c] - Ground[c]) * k;
                    // emojiColour is premultiplied by the share: what the layers show is the colour itself, and the rest is the glow picture.
                    rgba[i * 4 + c] = (byte)Math.Round(Math.Min(255f, own * (1f - e) + emojiColour[i * 4 + c]));
                }

                rgba[i * 4 + 3] = 255;
            }

            return rgba;
        }

        private static Point2 At(GlyphMesh mesh, int index, PlacedGlyph glyph, float metresPerEm, float boardW, float boardH, float scale)
        {
            Point2 p = mesh.Points[index];
            float cos = (float)Math.Cos(glyph.Angle), sin = (float)Math.Sin(glyph.Angle);
            float x = glyph.X + (cos * p.X - sin * p.Y) * metresPerEm, y = glyph.Y + (sin * p.X + cos * p.Y) * metresPerEm;
            return new Point2((x + boardW * 0.5f) * scale, (y + boardH * 0.5f) * scale);
        }

        /// <summary>Adds the share of each pixel a triangle covers (a grid of samples a pixel), in pixel coordinates with y up.</summary>
        private static void Fill(float[] cover, int width, int height, Point2 a, Point2 b, Point2 c)
        {
            float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (Math.Abs(area) < 1e-9f)
                return;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), x1 = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), y1 = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            float step = 1f / Samples, share = 1f / (Samples * Samples);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float inside = 0f;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float px = x + (sx + 0.5f) * step, py = y + (sy + 0.5f) * step;
                            float w0 = (b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px);
                            float w1 = (c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px);
                            float w2 = (a.X - px) * (b.Y - py) - (a.Y - py) * (b.X - px);
                            if (area > 0f ? (w0 >= 0f && w1 >= 0f && w2 >= 0f) : (w0 <= 0f && w1 <= 0f && w2 <= 0f))
                                inside += share;
                        }
                    }

                    if (inside > 0f)
                        cover[y * width + x] += inside;
                }
            }
        }
    }
}
