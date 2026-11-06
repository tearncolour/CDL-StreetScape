using System;
using System.IO;
using CDLC.TS.Geo;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// The baked detail surfaces (0.12.32) as they look: each tile lit by a low light from the upper left (the normal map, the colour's shade, the mask's smoothness as a little sheen), at twice its
    /// texels' size, beside its relief as grey. Preview.exe root out --detail writes detail-&lt;kind&gt;.png, and the icons of the kerb styles' upgrades (0.12.33) as kerb-&lt;style&gt;.png.
    /// </summary>
    internal static class DetailSheet
    {
        public static int Run(string output)
        {
            Directory.CreateDirectory(output);
            foreach (DetailKind kind in (DetailKind[])Enum.GetValues(typeof(DetailKind)))
            {
                DetailSurface s = DetailSurfaces.Make(kind);
                if (s == null)
                    continue;
                // At most 512 texels each way are shown (the W-beam's 2048 along it: its first 512, from the splice).
                int w = Math.Min(512, s.Width), h = Math.Min(512, s.Height), scale = w <= 256 && h <= 256 ? 2 : 1;
                int width = w * scale * 2 + 8, height = h * scale;
                var rgba = new byte[width * height * 4];
                float lowest = float.MaxValue, highest = float.MinValue;
                foreach (float r in s.Relief)
                {
                    lowest = Math.Min(lowest, r);
                    highest = Math.Max(highest, r);
                }

                var light = Geo.MeshBuilder.Normalize(new Snap.Vec3(-0.6f, 0.5f, 0.45f));
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int o = (y * width + x) * 4;
                        rgba[o + 3] = 255;
                        int side = x / (w * scale + 8);
                        int px = (x - side * (w * scale + 8)) / scale, py = y / scale;
                        if (px >= w || x - side * (w * scale + 8) >= w * scale)
                            continue;
                        int k = py * s.Width + px;
                        float value;
                        if (side == 0)
                        {
                            var n = new Snap.Vec3(s.Normals[k * 3], s.Normals[k * 3 + 1], s.Normals[k * 3 + 2]);
                            float lambert = Math.Max(0f, Snap.Vec3.Dot(n, light));
                            float sheen = (float)Math.Pow(Math.Max(0f, n.Z), 40.0) * s.Smooth[k] * 0.3f;
                            value = Math.Min(1f, (0.25f + 0.75f * lambert) * s.Shade[k] * 0.8f + sheen);
                        }
                        else
                        {
                            value = highest > lowest ? (s.Relief[k] - lowest) / (highest - lowest) : 0.5f;
                        }

                        byte b = (byte)Math.Max(0, Math.Min(255, (int)(value * 255f)));
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = b;
                    }
                }

                File.WriteAllBytes(Path.Combine(output, "detail-" + kind + ".png"), Png.Encode(rgba, width, height));
                if (s.Alpha != null)
                {
                    // Yellow paint (or a green mesh) lit as above, over a dark road where it has holes.
                    var drawn = new byte[w * h * 4];
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int k = y * s.Width + x, o = (y * w + x) * 4;
                            var n = new Snap.Vec3(s.Normals[k * 3], s.Normals[k * 3 + 1], s.Normals[k * 3 + 2]);
                            float lit = (0.35f + 0.65f * Math.Max(0f, Snap.Vec3.Dot(n, light))) * s.Shade[k];
                            bool hole = s.Alpha[k] < 0.5f;
                            float grain = 0.16f + 0.05f * ((x * 7 + y * 13) % 5) / 4f;
                            drawn[o] = (byte)(255f * Math.Min(1f, hole ? grain : 0.92f * lit));
                            drawn[o + 1] = (byte)(255f * Math.Min(1f, hole ? grain : (kind == DetailKind.RoadPaint ? 0.70f : 0.55f) * lit));
                            drawn[o + 2] = (byte)(255f * Math.Min(1f, hole ? grain * 1.05f : 0.08f * lit));
                            drawn[o + 3] = 255;
                        }
                    }

                    File.WriteAllBytes(Path.Combine(output, "detail-" + kind + "-drawn.png"), Png.Encode(drawn, w, h));
                }
                Console.WriteLine($"{kind}: {s.Width} x {s.Height} texels over {s.SizeU:0.###} x {s.SizeV:0.###} m ({s.SizeU / s.Width * 1000f:0.0} mm a texel), relief {lowest * 1000f:0.00} to {highest * 1000f:0.00} mm");
            }

            foreach (KerbStyle style in Kerbs.Styles)
                File.WriteAllBytes(Path.Combine(output, "kerb-" + style + ".png"), Thumbnails.KerbPng(style));

            return 0;
        }
    }
}
