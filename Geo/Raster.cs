using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    [Flags]
    public enum RasterLayers
    {
        Concrete = 1,
        Metal = 2,
        Face = 4,
        All = 7,

        /// <summary>A body with holes (a wire mesh, 0.12.34) or a sheet of glass (0.12.49): drawn in its colour on every other pixel, what is behind it showing through the rest.</summary>
        Screen = 8,

        /// <summary>The painted solids alone, without the steel (0.12.59): Metal draws them too (All has them), but its post and arms are not wanted in a thumbnail of a sign that is made of them.</summary>
        Solids = 16
    }

    /// <summary>The picture on a face: RGBA, bottom row first.</summary>
    public struct RasterPicture
    {
        public byte[] Pixels;
        public int Width, Height;
    }

    public struct RasterOptions
    {
        /// <summary>A transparent background and no ground (for a thumbnail); otherwise a grey-blue background with a strip of ground.</summary>
        public bool Transparent;

        /// <summary>What is drawn; 0 means everything.</summary>
        public RasterLayers Layers;
    }

    /// <summary>
    /// A small software renderer: orthographic, a depth buffer, flat colours per material with a lamp from the camera's upper left, the picture on the face. It
    /// uses the real meshes, their winding (front faces clockwise on the screen, as Unity culls) and their picture coordinates, so what it shows is what the
    /// game's renderer is given. The mod uses it for the thumbnails of the panel; the preview program uses it to look at the signs. Pure.
    /// </summary>
    public static class Raster
    {
        private static readonly byte[] Background = { 70, 78, 86 };
        private static readonly byte[] MetalColour = { 176, 181, 188 };
        private static readonly byte[] ConcreteColour = { 154, 152, 146 };

        /// <summary>
        /// Renders a sign as seen by a camera that looks along dir (the scene lies in front of it); RGBA with the bottom row first. The picture is the face's texture
        /// (also bottom row first). With a focus the window is a square of 2 x halfSize round that point; otherwise it is the bounds of what is drawn.
        /// </summary>
        public static byte[] Render(SignParts parts, byte[] picture, int pw, int ph, Vec3 dir, int maxSize, out int w, out int h, Vec3? focus = null, float halfSize = 0f, RasterOptions options = default)
        {
            var pictures = new RasterPicture[parts.Faces.Count];
            for (int i = 0; i < pictures.Length; i++)
                pictures[i] = new RasterPicture { Pixels = picture, Width = pw, Height = ph };
            return Render(parts, pictures, dir, maxSize, out w, out h, focus, halfSize, options);
        }

        /// <summary>As above for a support with several boards: pictures[i] is the picture of parts.Faces[i].</summary>
        public static byte[] Render(SignParts parts, IReadOnlyList<RasterPicture> pictures, Vec3 dir, int maxSize, out int w, out int h, Vec3? focus = null, float halfSize = 0f, RasterOptions options = default)
        {
            RasterLayers layers = options.Layers == 0 ? RasterLayers.All : options.Layers;
            var meshes = new List<(MeshBuilder mesh, RasterLayers layer, RasterPicture picture, byte[] flat)>();
            if ((layers & RasterLayers.Concrete) != 0) meshes.Add((parts.Concrete, RasterLayers.Concrete, default, ConcreteColour));
            if ((layers & RasterLayers.Metal) != 0)
                meshes.Add((parts.Metal, RasterLayers.Metal, default, SolidRgb(parts.MetalColour)));
            // Painted solids (an electric box's body) are shaded as the steel is.
            if ((layers & (RasterLayers.Metal | RasterLayers.Solids)) != 0)
            {
                for (int c = 0; c < parts.Solids.Length; c++)
                {
                    if (parts.Solids[c] != null && parts.Solids[c].VertexCount > 0)
                        meshes.Add((parts.Solids[c], RasterLayers.Metal, default, SolidRgb((SolidColour)c)));
                }
            }
            if ((layers & RasterLayers.Face) != 0)
            {
                for (int i = 0; i < parts.Faces.Count; i++)
                    meshes.Add((parts.Faces[i].Mesh, RasterLayers.Face, i < pictures.Count ? pictures[i] : default, null));

                // The lit dots of LED screens stand in front of their faces: flat and bright, not shaded (they glow).
                for (int c = 1; c < parts.Leds.Length; c++)
                {
                    if (parts.Leds[c] == null || parts.Leds[c].VertexCount == 0)
                        continue;
                    Led.Rgb((LedColour)c, out byte lr, out byte lg, out byte lb);
                    meshes.Add((parts.Leds[c], RasterLayers.Face, default, new[] { lr, lg, lb }));
                }
            }

            return Draw(meshes, dir, maxSize, out w, out h, focus, halfSize, options);
        }

        /// <summary>The colour of a painted solid in a picture: a road barrier's parts, an electric box's body.</summary>
        public static byte[] SolidRgb(SolidColour colour)
        {
            switch (colour)
            {
                case SolidColour.Metal: return MetalColour;
                case SolidColour.Concrete: return ConcreteColour;
                case SolidColour.Blue: return new byte[] { 30, 72, 150 };
                case SolidColour.Yellow: return new byte[] { 232, 182, 30 };
                case SolidColour.Black: return new byte[] { 30, 30, 32 };
                case SolidColour.Red: return new byte[] { 200, 30, 26 };
                case SolidColour.Green: return new byte[] { 36, 120, 70 };
                case SolidColour.Orange: return new byte[] { 240, 92, 24 };
                case SolidColour.LightGray: return new byte[] { 204, 207, 202 };
                case SolidColour.Steel: return new byte[] { 186, 190, 194 };
                case SolidColour.Soil: return new byte[] { 52, 36, 24 };
                case SolidColour.Wood: return new byte[] { 116, 78, 44 };
                case SolidColour.DarkGray: return new byte[] { 48, 50, 52 };
                case SolidColour.Granite: return new byte[] { 158, 158, 156 };
                case SolidColour.Gold: return new byte[] { 212, 160, 48 };
                case SolidColour.GlowRed: return new byte[] { 214, 34, 30 };
                case SolidColour.GlowGold: return new byte[] { 240, 186, 52 };
                case SolidColour.GlowWarm: return new byte[] { 250, 226, 180 };
                case SolidColour.GlowWhite: return new byte[] { 240, 240, 252 };
                case SolidColour.GlowBlue: return new byte[] { 60, 120, 245 };
                case SolidColour.GlowPink: return new byte[] { 245, 110, 170 };
                case SolidColour.GlowGreen: return new byte[] { 70, 210, 110 };
                case SolidColour.CastIron: return new byte[] { 46, 44, 42 };
                case SolidColour.ResinGreen: return new byte[] { 62, 150, 128 };
                case SolidColour.DarkBrown: return new byte[] { 80, 62, 52 };
                case SolidColour.Glass: return new byte[] { 196, 222, 218 };
                case SolidColour.GlassBlue: return new byte[] { 140, 186, 226 };
                case SolidColour.GlassGreen: return new byte[] { 52, 152, 93 };
                case SolidColour.Brick: return new byte[] { 160, 74, 52 };
                case SolidColour.GreyBrick: return new byte[] { 140, 142, 142 };
                case SolidColour.Graphite: return new byte[] { 37, 40, 42 };
                case SolidColour.PoleDarkGrey: return new byte[] { 58, 59, 64 };
                case SolidColour.PoleLightGrey: return new byte[] { 177, 179, 191 };
                default: return WallFoliage.Is(colour) ? WallFoliage.PictureRgb(colour) : new byte[] { 232, 234, 232 };
            }
        }

        /// <summary>
        /// Bodies of flat colours, shaded by the lamp (a road barrier's parts: MarkingRaster draws a barrier with this), seen along dir; RGBA with the bottom row first. The bodies in screens are
        /// wire meshes (RasterLayers.Screen).
        /// </summary>
        public static byte[] RenderBodies(IList<(MeshBuilder Mesh, byte[] Rgb)> bodies, Vec3 dir, int maxSize, out int w, out int h, RasterOptions options = default, ICollection<MeshBuilder> screens = null)
        {
            var meshes = new List<(MeshBuilder mesh, RasterLayers layer, RasterPicture picture, byte[] flat)>();
            foreach ((MeshBuilder mesh, byte[] rgb) in bodies)
                meshes.Add((mesh, screens != null && screens.Contains(mesh) ? RasterLayers.Screen : RasterLayers.Metal, default, rgb));
            return Draw(meshes, dir, maxSize, out w, out h, null, 0f, options);
        }

        /// <summary>
        /// Draws the meshes: a face with its picture, a lit dot (a face's layer with a colour) flat and bright, anything else in its colour shaded by the lamp.
        /// </summary>
        private static byte[] Draw(List<(MeshBuilder mesh, RasterLayers layer, RasterPicture picture, byte[] flat)> meshes, Vec3 dir, int maxSize, out int w, out int h, Vec3? focus, float halfSize, RasterOptions options)
        {
            dir = MeshBuilder.Normalize(dir);
            Vec3 right = MeshBuilder.Normalize(MeshBuilder.Cross(new Vec3(0f, 1f, 0f), dir));
            Vec3 up = MeshBuilder.Cross(dir, right);
            Vec3 light = MeshBuilder.Normalize(dir * -1f + up * 0.7f + right * -0.5f);

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach ((MeshBuilder mesh, RasterLayers _, RasterPicture __, byte[] ___) in meshes)
            {
                foreach (Vec3 p in mesh.Positions)
                {
                    float x = Vec3.Dot(right, p), y = Vec3.Dot(up, p);
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            }

            if (focus.HasValue)
            {
                // A close-up: the window is a square of 2 x halfSize around the focus point, seen along dir.
                minX = Vec3.Dot(right, focus.Value) - halfSize; maxX = minX + 2f * halfSize;
                minY = Vec3.Dot(up, focus.Value) - halfSize; maxY = minY + 2f * halfSize;
            }

            float scale = maxSize / Math.Max(maxX - minX, maxY - minY);
            int margin = options.Transparent ? 0 : 12;
            w = (int)Math.Ceiling((maxX - minX) * scale) + 2 * margin;
            h = (int)Math.Ceiling((maxY - minY) * scale) + 2 * margin;
            var image = new byte[w * h * 4];
            var depth = new float[w * h];
            for (int i = 0; i < w * h; i++)
            {
                if (!options.Transparent)
                {
                    image[i * 4] = Background[0]; image[i * 4 + 1] = Background[1]; image[i * 4 + 2] = Background[2]; image[i * 4 + 3] = 255;
                }

                depth[i] = float.MaxValue;
            }

            // A strip of "ground" under the sign, so that the foot's height can be judged.
            if (!options.Transparent)
            {
                for (int y = 0; y < h; y++)
                {
                    float worldY = minY + (y - margin) / scale;
                    if (worldY > 0f)
                        continue;
                    for (int x = 0; x < w; x++)
                    {
                        int d = (y * w + x) * 4;
                        image[d] = 92; image[d + 1] = 104; image[d + 2] = 82;
                    }
                }
            }

            foreach ((MeshBuilder mesh, RasterLayers layer, RasterPicture face, byte[] flat) in meshes)
            {
                bool isFace = layer == RasterLayers.Face, screen = layer == RasterLayers.Screen;
                byte[] picture = face.Pixels;
                int pw = face.Width, ph = face.Height;
                // A face's flat colour is a lit dot's (not shaded); the bodies' are shaded.
                bool glow = isFace && flat != null;
                byte[] colour = flat ?? MetalColour;
                for (int t = 0; t < mesh.TriangleCount; t++)
                {
                    int ia = mesh.Indices[t * 3], ib = mesh.Indices[t * 3 + 1], ic = mesh.Indices[t * 3 + 2];
                    Project(mesh.Positions[ia], right, up, dir, minX, minY, scale, margin, out float ax, out float ay, out float az);
                    Project(mesh.Positions[ib], right, up, dir, minX, minY, scale, margin, out float bx, out float by, out float bz);
                    Project(mesh.Positions[ic], right, up, dir, minX, minY, scale, margin, out float cx, out float cy, out float cz);

                    // Front faces are clockwise on the screen (y up); the others are not drawn.
                    float area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
                    if (area >= 0f)
                        continue;

                    int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
                    int x1 = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                    int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
                    int y1 = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = x + 0.5f, py = y + 0.5f;
                            float w0 = ((bx - px) * (cy - py) - (cx - px) * (by - py)) / area;
                            float w1 = ((cx - px) * (ay - py) - (ax - px) * (cy - py)) / area;
                            float w2 = 1f - w0 - w1;
                            if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f || (screen && ((x + y) & 1) != 0))
                                continue;

                            float z = w0 * az + w1 * bz + w2 * cz;
                            int pixel = y * w + x;
                            if (z >= depth[pixel])
                                continue;
                            depth[pixel] = z;

                            Vec3 n = mesh.Normals[ia] * w0 + mesh.Normals[ib] * w1 + mesh.Normals[ic] * w2;
                            n = MeshBuilder.Normalize(n);
                            float shade = 0.38f + 0.62f * Math.Max(0f, Vec3.Dot(n, light));
                            byte r, g, b;
                            if (glow)
                            {
                                r = flat[0]; g = flat[1]; b = flat[2];
                                shade = 1f;
                            }
                            else if (isFace && picture != null)
                            {
                                float u = w0 * mesh.U[ia] + w1 * mesh.U[ib] + w2 * mesh.U[ic];
                                float v = w0 * mesh.V[ia] + w1 * mesh.V[ib] + w2 * mesh.V[ic];
                                int sx = Math.Min(pw - 1, Math.Max(0, (int)(u * (pw - 1) + 0.5f)));
                                int sy = Math.Min(ph - 1, Math.Max(0, (int)(v * (ph - 1) + 0.5f)));
                                int s = (sy * pw + sx) * 4;
                                r = picture[s]; g = picture[s + 1]; b = picture[s + 2];
                                shade = 0.55f + 0.45f * shade;
                            }
                            else
                            {
                                r = colour[0]; g = colour[1]; b = colour[2];
                            }

                            int d = pixel * 4;
                            image[d] = (byte)Math.Min(255f, r * shade);
                            image[d + 1] = (byte)Math.Min(255f, g * shade);
                            image[d + 2] = (byte)Math.Min(255f, b * shade);
                            image[d + 3] = 255;
                        }
                    }
                }
            }

            return image;
        }

        private static void Project(Vec3 p, Vec3 right, Vec3 up, Vec3 dir, float minX, float minY, float scale, int margin, out float x, out float y, out float z)
        {
            x = (Vec3.Dot(right, p) - minX) * scale + margin;
            y = (Vec3.Dot(up, p) - minY) * scale + margin;
            z = Vec3.Dot(dir, p);
        }
    }
}
