using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The foliage surface of a vertical greening wall (0.12.78: DetailKind.Foliage, DetailSurfaces.Foliage, WallFoliage, DetailPixels.FoliageColour, SolidColour.Blossom*): saved numbers appended at the end,
    /// deterministic pixels that repeat without a seam, the blossom share the picture colour assumes, the five blossom colours and the leaves-only one, the stages and the names the material goes by.
    /// </summary>
    internal static class FoliageTests
    {
        private static DetailSurface s_Surface;

        private static DetailSurface Surface => s_Surface ?? (s_Surface = DetailSurfaces.Make(DetailKind.Foliage));

        public static void RunAll()
        {
            Program.Run("foliage: saved numbers, registered end to end (kind, colours, tile, names, stages)", Registered);
            Program.Run("foliage: deterministic pixels, no NaN, the cost of baking", Deterministic);
            Program.Run("foliage: tiles without a seam (the relief, the shade, the blossoms)", Seamless);
            Program.Run("foliage: the blossoms' share, size and the five colours", Blossoms);
            Program.Run("foliage: the colour map on a worker, the leaves-only wall, the pictures", Colours);
        }

        private static void Registered()
        {
            // The kinds are appended in the order the branches were merged: the painted beams (0.12.78, 23 and 24), then the foliage (25). Neither was released; what was is not renumbered.
            Program.Check((int)DetailKind.Foliage == (int)DetailKind.WBeam2Painted + 1 && Enum.GetValues(typeof(DetailKind)).Cast<int>().Max() == (int)DetailKind.Foliage && (int)DetailKind.Brick == 22, "Foliage is the last kind, after the painted beams: saved by number, nothing renumbered");
            Program.Check((int)SolidColour.BlossomRed == 34 && (int)SolidColour.BlossomPink == 35 && (int)SolidColour.BlossomYellow == 36 && (int)SolidColour.BlossomWhite == 37 && (int)SolidColour.BlossomMixed == 38 && (int)SolidColour.PoleLightGrey == 33,
                "the blossom colours are 34 to 38 after the 33 of the pole's light grey");
            Program.Check(Enum.GetValues(typeof(SolidColour)).Cast<int>().Max() < SignParts.SolidCount && new SignParts().Solids.Length == SignParts.SolidCount, "every colour has its slot");
            (float u, float v) = DetailSurfaces.TileOf(DetailKind.Foliage);
            Program.Check(u == DetailSurfaces.FoliageUp && v == DetailSurfaces.FoliageAlong && !DetailSurfaces.HasHoles(DetailKind.Foliage), "a tile of 1 m up by 2 m along, solid (no holes, drawn with the game's opaque material)");
            Program.Check(Surface != null && Surface.Kind == DetailKind.Foliage && Surface.Mix != null && Surface.Hue != null && Surface.Alpha == null && Surface.Mix.Length == Surface.Width * Surface.Height, "the surface has the blossoms' maps");
            foreach (DetailKind other in Enum.GetValues(typeof(DetailKind)))
            {
                if (other == DetailKind.None || other == DetailKind.Foliage)
                    continue;
                DetailSurface s = DetailSurfaces.Make(other);
                Program.Check(s != null && s.Mix == null && s.Hue == null, other + ": no blossom maps");
            }

            foreach (SolidColour colour in new[] { SolidColour.BlossomRed, SolidColour.BlossomPink, SolidColour.BlossomYellow, SolidColour.BlossomWhite, SolidColour.BlossomMixed })
            {
                string name = ChunkBuilder.DetailMaterial(DetailKind.Foliage, colour);
                Program.Check(ChunkBuilder.DetailOf(name, out DetailKind k, out SolidColour c) && k == DetailKind.Foliage && c == colour && name == "detail:" + (int)DetailKind.Foliage + ":" + (int)colour, "the material's name says its kind and colour: " + name);
                Program.Check(ChunkBuilder.SolidColourOf(ChunkBuilder.SolidMaterial(colour)) == colour && WallFoliage.Is(colour), colour + ": a plain solid keeps its colour too");
                byte[] rgb = Raster.SolidRgb(colour);
                Program.Check(rgb.Length == 3 && rgb[1] > 60 && rgb[1] < 200, colour + ": in the pictures the leaves with its blossoms: " + string.Join(",", rgb));
            }

            Program.Check(!WallFoliage.Is(SolidColour.Green) && !WallFoliage.Is(SolidColour.PoleLightGrey) && WallFoliage.OfChoice("l") == null && WallFoliage.OfChoice("") == null && WallFoliage.OfChoice(null) == null, "the green and the leaves-only choice are no blossom colour");
            Program.Check(WallFoliage.OfChoice("r") == SolidColour.BlossomRed && WallFoliage.OfChoice("p") == SolidColour.BlossomPink && WallFoliage.OfChoice("y") == SolidColour.BlossomYellow && WallFoliage.OfChoice("w") == SolidColour.BlossomWhite && WallFoliage.OfChoice("m") == SolidColour.BlossomMixed, "the choices");

            // The stages of a surface without holes (the foliage's): the colour map, its compression, the kind's maps, the material.
            var order = new List<DetailStage>();
            for (DetailStage stage = DetailStage.Prepare; stage != DetailStage.Done && order.Count < 20; stage = DetailStages.Next(stage, DetailSurfaces.HasHoles(DetailKind.Foliage), false))
                order.Add(stage);
            Program.Check(string.Join(",", order) == "Prepare,Colour,Compress,Normal,Mask,Material", "the stages of a solid surface: " + string.Join(",", order));

            // A material of the template's own colours is still told apart: no other plan part uses the kind.
            MarkingPlan plan = MarkingTemplates.PlanOf(MarkingTemplates.ById(MarkingTemplates.LivingWallId), "bloom=r");
            Program.Check(plan.Parts.Any(p => p.Detail == DetailKind.Foliage && p.Solid == SolidColour.BlossomRed && p.Mesh.VertexCount > 0), "a wall with red blossoms draws its face in the red foliage");
            Program.Check(!MarkingTemplates.PlanOf(MarkingTemplates.ById(MarkingTemplates.LivingWallId), "bloom=l").Parts.Any(p => p.Detail == DetailKind.Foliage && WallFoliage.Is(p.Solid.Value)), "the leaves-only wall has no blossom colour on its face");
        }

        private static void Deterministic()
        {
            var watch = Stopwatch.StartNew();
            DetailSurface a = DetailSurfaces.Make(DetailKind.Foliage), b = Task.Run(() => DetailSurfaces.Make(DetailKind.Foliage)).Result;
            watch.Stop();
            Program.Check(a.Width == 256 && a.Height == 512 && a.SizeU == 1f && a.SizeV == 2f, "256 by 512 texels over 1 m by 2 m");
            Program.Check(Same(a.Relief, b.Relief) && Same(a.Normals, b.Normals) && Same(a.Shade, b.Shade) && Same(a.Smooth, b.Smooth) && Same(a.Mix, b.Mix) && Same(a.Hue, b.Hue), "made twice, once on a worker: the same pixels (a fixed seed)");
            bool finite = true, inRange = true;
            for (int k = 0; k < a.Relief.Length; k++)
            {
                float n = a.Normals[k * 3] + a.Normals[k * 3 + 1] + a.Normals[k * 3 + 2] + a.Relief[k] + a.Shade[k] + a.Smooth[k] + a.Mix[k] + a.Hue[k];
                finite &= !float.IsNaN(n) && !float.IsInfinity(n);
                inRange &= a.Mix[k] >= 0f && a.Mix[k] <= 1f && a.Hue[k] >= 0f && a.Hue[k] < 1f && a.Shade[k] > 0.3f && a.Shade[k] <= 1f && a.Smooth[k] >= 0f && a.Smooth[k] <= 1f && Math.Abs(a.Relief[k]) < 0.02f;
            }

            Program.Check(finite, "no NaN or infinity in any map");
            Program.Check(inRange, "every texel in range (blossom share and hue 0 to 1, shade over 0.3, smoothness 0 to 1, relief under 2 cm)");
            Program.Check(watch.ElapsedMilliseconds < 4000, $"baked twice in {watch.ElapsedMilliseconds} ms (a worker's job, not the main thread's)");
        }

        private static bool Same(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        /// <summary>The largest step between neighbouring texels of a map along a row (across the tile's edge too when wrap) or a column.</summary>
        private static float Steps(float[] map, int w, int h, bool alongRows, bool seamOnly)
        {
            float worst = 0f;
            if (alongRows)
            {
                for (int j = 0; j < h; j++)
                {
                    if (seamOnly)
                        worst = Math.Max(worst, Math.Abs(map[j * w + w - 1] - map[j * w]));
                    else
                        for (int i = 1; i < w; i++)
                            worst = Math.Max(worst, Math.Abs(map[j * w + i] - map[j * w + i - 1]));
                }
            }
            else
            {
                for (int i = 0; i < w; i++)
                {
                    if (seamOnly)
                        worst = Math.Max(worst, Math.Abs(map[(h - 1) * w + i] - map[i]));
                    else
                        for (int j = 1; j < h; j++)
                            worst = Math.Max(worst, Math.Abs(map[j * w + i] - map[(j - 1) * w + i]));
                }
            }

            return worst;
        }

        private static void Seamless()
        {
            DetailSurface s = Surface;
            foreach ((string what, float[] map) in new[] { ("relief", s.Relief), ("shade", s.Shade), ("blossom share", s.Mix) })
            {
                foreach (bool rows in new[] { true, false })
                {
                    float seam = Steps(map, s.Width, s.Height, rows, true), inside = Steps(map, s.Width, s.Height, rows, false);
                    Program.Check(seam <= inside + 1e-6f, $"{what}: the step across the tile's {(rows ? "right/left" : "top/bottom")} edge ({seam:0.0000}) is no larger than the largest inside it ({inside:0.0000})");
                }
            }

            // The surface at a point and at the same point a tile away (up, along, both, and from before the tile's start) is the same: the cells of the leaves and of the blossoms wrap, the noise wraps. A blossom on the edge
            // therefore continues on the other side, and the clumping of the blossoms does not break at it.
            float worstSeam = 0f;
            int sampled = 0, blossoms = 0;
            for (float a = -0.12f; a < 1.12f; a += 0.0173f)
            {
                for (float b = -0.12f; b < 2.12f; b += 0.0317f)
                {
                    var home = DetailSurfaces.FoliageSample(a, b);
                    foreach ((float du, float dv) in new[] { (1f, 0f), (0f, 2f), (-1f, 2f) })
                    {
                        var away = DetailSurfaces.FoliageSample(a + du, b + dv);
                        worstSeam = Math.Max(worstSeam, Math.Max(Math.Abs(home.Height - away.Height), Math.Max(Math.Abs(home.Mix - away.Mix), Math.Abs(home.Hue - away.Hue))));
                    }

                    sampled++;
                    if (home.Mix > 0.5f)
                        blossoms++;
                }
            }

            Program.Check(sampled > 3000 && blossoms > 300 && worstSeam < 2e-3f, $"a point and its copy a tile away are the same (worst difference {worstSeam:0.00000} over {sampled} points, {blossoms} in a blossom)");

            // And the blossoms are cut by no edge: the share along the first and the last column is alike.
            double left = 0.0, right = 0.0, top = 0.0, bottom = 0.0;
            for (int j = 0; j < s.Height; j++)
            {
                left += s.Mix[j * s.Width];
                right += s.Mix[j * s.Width + s.Width - 1];
            }

            for (int i = 0; i < s.Width; i++)
            {
                top += s.Mix[i];
                bottom += s.Mix[(s.Height - 1) * s.Width + i];
            }

            Program.Check(Math.Abs(left - right) / Math.Max(1.0, left + right) < 0.15 && Math.Abs(top - bottom) / Math.Max(1.0, top + bottom) < 0.15, $"the blossoms on the opposite edges are alike: {left:0}/{right:0} and {top:0}/{bottom:0} of {s.Height} and {s.Width}");

            // Normals lean no more than the relief allows: no texel is steeper than a 45 degree dome.
            float tilt = 0f;
            for (int k = 0; k < s.Relief.Length; k++)
                tilt = Math.Max(tilt, (float)Math.Acos(Math.Min(1f, s.Normals[k * 3 + 2])));
            Program.Check(tilt < Math.PI / 2.4, $"the steepest texel leans {tilt * 180.0 / Math.PI:0} degrees");
        }

        private static void Blossoms()
        {
            DetailSurface s = Surface;
            int n = s.Width * s.Height;
            double covered = 0.0;
            int whole = 0, partial = 0;
            for (int k = 0; k < n; k++)
            {
                covered += s.Mix[k];
                if (s.Mix[k] >= 0.999f)
                    whole++;
                else if (s.Mix[k] > 0.001f)
                    partial++;
            }

            double share = covered / n;
            Program.Check(Math.Abs(share - WallFoliage.Coverage) < 0.05, $"the blossoms cover {share:0.000} of the face, the pictures assume {WallFoliage.Coverage}");
            Program.Check(whole > partial * 2 && partial > 0, $"the blossoms' edge is soft by a texel: {whole} whole texels, {partial} on an edge");

            // A blossom 6.4 to 8.4 cm across on a texel of 3.9 mm: count them by their domes (a texel of the relief over 6 mm that is a local maximum in 9 cm).
            int domes = 0;
            for (int j = 4; j < s.Height - 4; j += 1)
            {
                for (int i = 4; i < s.Width - 4; i++)
                {
                    int k = j * s.Width + i;
                    if (s.Mix[k] < 0.999f || s.Relief[k] < 0.0095f)
                        continue;
                    bool top = true;
                    for (int dj = -9; dj <= 9 && top; dj++)
                    {
                        for (int di = -9; di <= 9 && top; di++)
                        {
                            int jj = j + dj, ii = i + di;
                            if ((dj != 0 || di != 0) && jj >= 0 && jj < s.Height && ii >= 0 && ii < s.Width)
                                top = s.Relief[jj * s.Width + ii] < s.Relief[k] || (s.Relief[jj * s.Width + ii] == s.Relief[k] && (dj < 0 || (dj == 0 && di < 0)));
                        }
                    }

                    if (top)
                        domes++;
                }
            }

            double perSquareMetre = domes / (s.SizeU * s.SizeV);
            Program.Check(perSquareMetre > 15 && perSquareMetre < 100, $"{domes} blossoms in a tile of 2 m2: {perSquareMetre:0} to the square metre (cells of 10 cm: at most 100)");

            // The mixed wall's palette: every blossom has one of four colours, in the shares set, and the hue of a blossom is the same all over it.
            var counts = new int[4];
            for (int k = 0; k < n; k++)
            {
                if (s.Mix[k] < 0.999f)
                    continue;
                float hue = s.Hue[k];
                counts[hue < 0.25f ? 0 : hue < 0.55f ? 1 : hue < 0.75f ? 2 : 3]++;
            }

            int total = counts.Sum();
            Program.Check(counts.All(c => c > total / 10), $"red, pink, yellow and white blossoms all there on the mixed wall: {string.Join(" / ", counts)} texels");
            Program.Check(WallFoliage.Palette(0.1f) == WallFoliage.Blossom(SolidColour.BlossomRed) && WallFoliage.Palette(0.4f) == WallFoliage.Blossom(SolidColour.BlossomPink) && WallFoliage.Palette(0.6f) == WallFoliage.Blossom(SolidColour.BlossomYellow) && WallFoliage.Palette(0.9f) == WallFoliage.Blossom(SolidColour.BlossomWhite), "the palette's four colours by the hue");
        }

        private static (double R, double G, double B) Mean(byte[] pixels, Func<int, bool> pick)
        {
            double r = 0.0, g = 0.0, b = 0.0;
            int count = 0;
            for (int k = 0; k < pixels.Length / 4; k++)
            {
                if (!pick(k))
                    continue;
                r += pixels[k * 4];
                g += pixels[k * 4 + 1];
                b += pixels[k * 4 + 2];
                count++;
            }

            return count == 0 ? (0.0, 0.0, 0.0) : (r / count, g / count, b / count);
        }

        private static void Colours()
        {
            DetailSurface s = Surface;
            var means = new Dictionary<SolidColour, (double R, double G, double B)>();
            foreach (SolidColour colour in new[] { SolidColour.BlossomRed, SolidColour.BlossomPink, SolidColour.BlossomYellow, SolidColour.BlossomWhite, SolidColour.BlossomMixed, SolidColour.Green })
            {
                byte[] map = DetailPixels.FoliageColour(s, colour);
                byte[] again = Task.Run(() => DetailPixels.FoliageColour(s, colour)).Result;
                Program.Check(map.Length == s.Width * s.Height * 4 && Same(map, again), colour + ": four bytes a texel, the same on a worker");
                bool opaque = true;
                for (int k = 0; k < map.Length / 4; k++)
                    opaque &= map[k * 4 + 3] == 255;
                Program.Check(opaque, colour + ": opaque (a solid surface, no holes)");
                means[colour] = Mean(map, k => s.Mix[k] >= 0.999f);
                (double lr, double lg, double lb) = Mean(map, k => s.Mix[k] <= 0.0001f);
                Program.Check(lg > lr * 1.6 && lg > lb * 1.6, $"{colour}: the leaves are green where there is no blossom ({lr:0}, {lg:0}, {lb:0})");
            }

            (double rr, double rg, double rb) = means[SolidColour.BlossomRed];
            (double pr, double pg, double pb) = means[SolidColour.BlossomPink];
            (double yr, double yg, double yb) = means[SolidColour.BlossomYellow];
            (double wr, double wg, double wb) = means[SolidColour.BlossomWhite];
            Program.Check(rr > rg * 2.5 && rr > rb * 2.5, $"red blossoms are red ({rr:0}, {rg:0}, {rb:0})");
            Program.Check(pr > pg * 1.5 && pb > pg * 1.1, $"pink blossoms are pink ({pr:0}, {pg:0}, {pb:0})");
            Program.Check(yr > yb * 3.0 && yg > yb * 3.0, $"yellow blossoms are yellow ({yr:0}, {yg:0}, {yb:0})");
            Program.Check(wr > 140 && wg > 140 && wb > 140 && Math.Abs(wr - wb) < 40, $"white blossoms are white ({wr:0}, {wg:0}, {wb:0})");
            (double mr, double mg, double mb) = means[SolidColour.BlossomMixed];
            Program.Check(mr > 190 && mr < 240 && mg > 105 && mg < 165 && mb > 90 && mb < 140, $"the mixed blossoms are the average of the four colours by their shares ({mr:0}, {mg:0}, {mb:0})");
            (double gr, double gg, double gb) = means[SolidColour.Green];
            Program.Check(gg > gr * 1.6 && gg > gb * 1.6, $"the leaves-only wall is green where the others have blossoms ({gr:0}, {gg:0}, {gb:0})");

            // Plain green: leaves only, the same on every texel that is a blossom elsewhere.
            byte[] leaves = DetailPixels.FoliageColour(s, SolidColour.Green), red = DetailPixels.FoliageColour(s, SolidColour.BlossomRed);
            int differs = 0, leafOnly = 0;
            for (int k = 0; k < s.Width * s.Height; k++)
            {
                bool same = leaves[k * 4] == red[k * 4] && leaves[k * 4 + 1] == red[k * 4 + 1] && leaves[k * 4 + 2] == red[k * 4 + 2];
                if (s.Mix[k] <= 0f)
                {
                    leafOnly++;
                    Program.Check(same, "a texel with no blossom is the same in every colour");
                    if (!same)
                        break;
                }
                else if (!same)
                {
                    differs++;
                }
            }

            Program.Check(differs > 0 && leafOnly > 0, $"the colour changes only where a blossom is ({differs} texels of {leafOnly + differs})");

            // In the pictures (the catalogue's thumbnail) the five colours differ and each reads as its colour: the face is the leaves with PictureWeight of the blossoms' colour over them.
            var pictures = new[] { SolidColour.BlossomRed, SolidColour.BlossomPink, SolidColour.BlossomYellow, SolidColour.BlossomWhite, SolidColour.BlossomMixed }.ToDictionary(c => c, c => Raster.SolidRgb(c));
            Program.Check(pictures.Values.Select(p => string.Join(",", p)).Distinct().Count() == 5, "the five colours differ in the pictures");
            byte[] picRed = pictures[SolidColour.BlossomRed], picPink = pictures[SolidColour.BlossomPink], picYellow = pictures[SolidColour.BlossomYellow], picWhite = pictures[SolidColour.BlossomWhite], picMixed = pictures[SolidColour.BlossomMixed];
            Program.Check(picRed[0] > picRed[1] * 1.8 && picRed[0] > picRed[2] * 1.8, "red reads red: " + string.Join(",", picRed));
            Program.Check(picPink[0] > picPink[1] * 1.3 && picPink[2] > picPink[1] && picPink[0] > 150, "pink reads pink: " + string.Join(",", picPink));
            Program.Check(picYellow[0] > picYellow[2] * 2.5 && picYellow[1] > picYellow[2] * 2.5, "yellow reads yellow: " + string.Join(",", picYellow));
            Program.Check(picWhite.Sum(b => b) == pictures.Values.Max(p => p.Sum(b => b)) && picWhite.Min() > 130, "white is the lightest: " + string.Join(",", picWhite));
            Program.Check(picMixed[0] > picMixed[1] && picMixed[1] > picMixed[2], "the mixed wall is a warm mean: " + string.Join(",", picMixed));
            Program.Check(Math.Abs(WallFoliage.PictureWeight - WallFoliage.Coverage) > 0.2f, "the picture's weight is stylised, apart from the measured coverage");
        }
    }
}
