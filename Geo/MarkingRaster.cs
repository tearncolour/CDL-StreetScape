using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A marking seen from straight above, as a picture (RGBA, 8 bits, the first row the top): the road surface in a dark grey and the paint in its colours, scaled to fit with a margin, the way the
    /// traffic runs up the picture. This is the thumbnail of the panel and the picture in the editor for a marking (a marking has no face that is painted). Paint is drawn in the order of its layers
    /// (MarkingPart.Lift), the higher over the lower, as the road shows it. Pure; no System.Drawing.
    /// </summary>
    public static class MarkingRaster
    {
        /// <summary>Raise this when the picture of a marking changes (the road colour, the margins, the scaling, the colours), so that the files of the old art are not reused.</summary>
        public const int Revision = 4;

        private static readonly byte[] kRoad = { 0x4a, 0x4a, 0x4e, 0xff };

        /// <summary>The paint of each colour as it is seen on the picture (MarkingColour as the index).</summary>
        private static readonly byte[][] kPaint =
        {
            new byte[] { 0xf2, 0xf2, 0xee, 0xff },
            new byte[] { 0xf2, 0xc4, 0x1a, 0xff },
            new byte[] { 0xd0, 0x22, 0x1c, 0xff },
            new byte[] { 0x10, 0x10, 0x12, 0xff }
        };

        private const byte NoPaint = 255;

        /// <summary>Under this many pixels to the metre the paint is given a least width (about a pixel): a picture of a long road with markers or posts on it would otherwise show specks nobody can see.</summary>
        public const float WidenBelow = 20f;

        /// <summary>The picture of a plan, size x size pixels; a plan with no paint is the plain road. Four samples to a pixel (2 x 2), so the thin lines are not lost.</summary>
        public static byte[] Render(MarkingPlan plan, int size, out int width, out int height, float margin = 0.08f)
        {
            return Render(plan, size, size, out width, out height, margin);
        }

        public static byte[] Render(MarkingPlan plan, int maxWidth, int maxHeight, out int width, out int height, float margin)
        {
            // A road barrier is bodies, not paint: seen from the road's side at an angle (0.12.13).
            if (plan != null && plan.HasSolids)
                return Barrier(plan, Math.Max(maxWidth, maxHeight), out width, out height);
            width = Math.Max(8, maxWidth);
            height = Math.Max(8, maxHeight);
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
                Array.Copy(kRoad, 0, pixels, i * 4, 4);
            if (plan == null || (plan.Parts.Count == 0 && plan.Outlines.Count == 0))
                return pixels;

            // The box of the paint as the picture shows it: across, and along the road (up the picture) for paint on the road, or up the wall for paint on an upright surface; and of the outlines.
            float minX = float.MaxValue, maxX = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (MarkingPart part in plan.Parts)
            {
                foreach (Vec3 p in part.Mesh.Positions)
                {
                    float v = part.Upright ? p.Y : p.Z;
                    minX = Math.Min(minX, p.X);
                    maxX = Math.Max(maxX, p.X);
                    minV = Math.Min(minV, v);
                    maxV = Math.Max(maxV, v);
                }
            }

            foreach (List<Vec3> outline in plan.Outlines)
            {
                foreach (Vec3 p in outline)
                {
                    minX = Math.Min(minX, p.X);
                    maxX = Math.Max(maxX, p.X);
                    minV = Math.Min(minV, p.Z);
                    maxV = Math.Max(maxV, p.Z);
                }
            }

            if (minX > maxX)
                return pixels;
            float w = Math.Max(0.05f, maxX - minX), h = Math.Max(0.05f, maxV - minV);
            float usableW = width * (1f - 2f * margin), usableH = height * (1f - 2f * margin);
            float scale = Math.Min(usableW / w, usableH / h);
            float cx = (minX + maxX) * 0.5f, cz = (minV + maxV) * 0.5f;

            // Four samples to a pixel (2 x 2): the colour of the paint that covers each, the highest layer last.
            var samples = new byte[width * height * 4];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = NoPaint;
            var order = new List<MarkingPart>(plan.Parts);
            order.Sort((a, b) => a.Lift != b.Lift ? a.Lift.CompareTo(b.Lift) : ((int)a.Colour).CompareTo((int)b.Colour));
            foreach (MarkingPart part in order)
            {
                byte colour = (byte)Math.Max(0, Math.Min(kPaint.Length - 1, (int)part.Colour));
                MeshBuilder mesh = part.Mesh;
                for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
                {
                    Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                    float av = part.Upright ? a.Y : a.Z, bv = part.Upright ? b.Y : b.Z, cv = part.Upright ? c.Y : c.Z;
                    float ax = width * 0.5f + (a.X - cx) * scale, ay = height * 0.5f - (av - cz) * scale;
                    float bx = width * 0.5f + (b.X - cx) * scale, by = height * 0.5f - (bv - cz) * scale;
                    float qx = width * 0.5f + (c.X - cx) * scale, qy = height * 0.5f - (cv - cz) * scale;
                    Fill(samples, width, height, colour, ax, ay, bx, by, qx, qy);
                }
            }

            // The outlines: a red dashed line of 10 pixels on and 6 off, at least 2 pixels wide, over the paint.
            if (plan.Outlines.Count > 0)
            {
                MeshBuilder dashes = OutlineDashes(plan.Outlines, 10f / scale, 6f / scale, Math.Max(0.12f, 1.1f / scale));
                byte red = (byte)MarkingColour.Red;
                for (int i = 0; i + 2 < dashes.Indices.Count; i += 3)
                {
                    Vec3 a = dashes.Positions[dashes.Indices[i]], b = dashes.Positions[dashes.Indices[i + 1]], c = dashes.Positions[dashes.Indices[i + 2]];
                    Fill(samples, width, height, red, width * 0.5f + (a.X - cx) * scale, height * 0.5f - (a.Z - cz) * scale, width * 0.5f + (b.X - cx) * scale, height * 0.5f - (b.Z - cz) * scale,
                        width * 0.5f + (c.X - cx) * scale, height * 0.5f - (c.Z - cz) * scale);
                }
            }

            if (scale < WidenBelow)
                Widen(samples, width, height);
            for (int i = 0; i < width * height; i++)
            {
                int r = 0, g = 0, b = 0;
                for (int s = 0; s < 4; s++)
                {
                    byte[] c = samples[i * 4 + s] == NoPaint ? kRoad : kPaint[samples[i * 4 + s]];
                    r += c[0];
                    g += c[1];
                    b += c[2];
                }

                pixels[i * 4] = (byte)(r / 4);
                pixels[i * 4 + 1] = (byte)(g / 4);
                pixels[i * 4 + 2] = (byte)(b / 4);
            }

            return pixels;
        }

        /// <summary>The dashes of closed outlines (on, off and half the width in metres) as flat quads, the pattern going on round each outline.</summary>
        internal static MeshBuilder OutlineDashes(List<List<Vec3>> outlines, float on, float off, float halfWidth)
        {
            var mesh = new MeshBuilder();
            var up = new Vec3(0f, 1f, 0f);
            if (!(on > 0f) || !(off >= 0f) || !(halfWidth > 0f))
                return mesh;
            foreach (List<Vec3> outline in outlines)
            {
                float along = 0f;
                for (int i = 0; i < outline.Count; i++)
                {
                    Vec3 a = outline[i], b = outline[(i + 1) % outline.Count];
                    float length = Vec3.DistanceXZ(a, b);
                    if (length < 1e-4f)
                        continue;
                    Vec3 d = (b - a) * (1f / length);
                    Vec3 side = new Vec3(d.Z, 0f, -d.X) * halfWidth;
                    float period = on + off;
                    // The dashes that fall on this side: from where the pattern stands at its start.
                    float first = -(along % period);
                    for (float t = first; t < length; t += period)
                    {
                        float t0 = Math.Max(0f, t), t1 = Math.Min(length, t + on);
                        if (t1 - t0 < 1e-4f)
                            continue;
                        Vec3 p0 = a + d * t0, p1 = a + d * t1;
                        int v0 = mesh.Vertex(p0 - side, up, 0f, 0f), v1 = mesh.Vertex(p1 - side, up, 0f, 0f), v2 = mesh.Vertex(p1 + side, up, 0f, 0f), v3 = mesh.Vertex(p0 + side, up, 0f, 0f);
                        mesh.Tri(v0, v1, v2);
                        mesh.Tri(v0, v2, v3);
                    }

                    along += length;
                }
            }

            return mesh;
        }

        /// <summary>Every sample that has paint gives its colour to the samples round it that have none (a ring of half a pixel): the least width of the paint in a small picture.</summary>
        private static void Widen(byte[] samples, int width, int height)
        {
            int sw = width * 2, sh = height * 2;
            byte At(int x, int y) => samples[((y >> 1) * width + (x >> 1)) * 4 + ((y & 1) << 1) + (x & 1)];
            var grown = (byte[])samples.Clone();
            for (int y = 0; y < sh; y++)
            {
                for (int x = 0; x < sw; x++)
                {
                    byte here = At(x, y);
                    if (here == NoPaint)
                        continue;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= sw || ny >= sh)
                                continue;
                            int index = ((ny >> 1) * width + (nx >> 1)) * 4 + ((ny & 1) << 1) + (nx & 1);
                            if (grown[index] == NoPaint)
                                grown[index] = here;
                        }
                    }
                }
            }

            Array.Copy(grown, samples, samples.Length);
        }

        /// <summary>Sets the colour of every sample point (a 2 x 2 grid in each pixel) that is inside the triangle.</summary>
        private static void Fill(byte[] samples, int width, int height, byte colour, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float minX = Math.Min(ax, Math.Min(bx, cx)), maxX = Math.Max(ax, Math.Max(bx, cx));
            float minY = Math.Min(ay, Math.Min(by, cy)), maxY = Math.Max(ay, Math.Max(by, cy));
            int x0 = Math.Max(0, (int)Math.Floor(minX)), x1 = Math.Min(width - 1, (int)Math.Ceiling(maxX));
            int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(height - 1, (int)Math.Ceiling(maxY));
            float area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
            if (Math.Abs(area) < 1e-9f)
                return;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float px = x + 0.25f + sx * 0.5f, py = y + 0.25f + sy * 0.5f;
                            float w0 = ((bx - px) * (cy - py) - (cx - px) * (by - py)) / area;
                            float w1 = ((cx - px) * (ay - py) - (ax - px) * (cy - py)) / area;
                            float w2 = 1f - w0 - w1;
                            if (w0 >= 0f && w1 >= 0f && w2 >= 0f)
                                samples[(y * width + x) * 4 + sy * 2 + sx] = colour;
                        }
                    }
                }
            }
        }

        /// <summary>A barrier's picture: its solids (and any paint, as white) seen from the side of the line at an angle, on a transparent ground, top row first as the paint's pictures.</summary>
        private static byte[] Barrier(MarkingPlan plan, int size, out int width, out int height)
        {
            var bodies = new List<(MeshBuilder Mesh, byte[] Rgb)>();
            var screens = new HashSet<MeshBuilder>();
            foreach (MarkingPart part in plan.Parts)
            {
                bodies.Add((part.Mesh, Raster.SolidRgb(part.Solid ?? SolidColour.White)));
                if (DetailSurfaces.HasHoles(part.Detail) || (part.Solid.HasValue && SolidGlass.Is(part.Solid.Value)))
                    screens.Add(part.Mesh);
            }
            // Drawn a little inside the picture, then put in the middle of a square of the road's colour (the pictures of the panel are square), the top row first.
            int inner = Math.Max(8, (int)(size * 0.9f));
            byte[] bottomFirst = Raster.RenderBodies(bodies, new Vec3(-0.75f, -0.42f, 0.52f), inner, out int bw, out int bh, new RasterOptions { Transparent = true }, screens);
            width = height = Math.Max(8, size);
            var square = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
                Array.Copy(kRoad, 0, square, i * 4, 4);
            int ox = (width - bw) / 2, oy = (height - bh) / 2;
            for (int y = 0; y < bh && oy + y < height; y++)
            {
                for (int x = 0; x < bw && ox + x < width; x++)
                {
                    int s = (y * bw + x) * 4;
                    if (bottomFirst[s + 3] == 0 || ox + x < 0 || oy + y < 0)
                        continue;
                    int d = ((height - 1 - (oy + y)) * width + ox + x) * 4;
                    square[d] = bottomFirst[s];
                    square[d + 1] = bottomFirst[s + 1];
                    square[d + 2] = bottomFirst[s + 2];
                    square[d + 3] = 255;
                }
            }

            return square;
        }

        public static byte[] Png(MarkingPlan plan, int size)
        {
            byte[] raw = Render(plan, size, out int w, out int h);
            return CDLC.TS.Geo.Png.Encode(raw, w, h, false);
        }
    }
}
