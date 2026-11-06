using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A cluster of code points as a glyph (0.12.78): looked for in a chain of fonts, and made, from the first font that draws it, as a monochrome glyph (the font's plain outline: Segoe UI Symbol) or as a
    /// colour emoji (the layers of COLR version 0, each in its own colour, and their union as the silhouette that the frame and the return use). Pure: the chain is what it is given.
    /// </summary>
    public static class EmojiMesh
    {
        /// <summary>The glyphs and the layers of a cluster in one font: null when the font cannot draw it (a code point it has no glyph for, no outline).</summary>
        public static GlyphMesh Build(FontOutlines font, string cluster, int bucket)
        {
            if (font == null || !font.Ok || string.IsNullOrEmpty(cluster))
                return null;
            List<int> scalars = TextClusters.Scalars(cluster);
            List<int> glyphs = font.Shape(scalars);
            if (glyphs == null || glyphs.Count == 0)
                return null;
            double flatness = TextGlyphs.BucketFlatness(bucket) * font.UnitsPerEm;
            // The glyphs of the run that draw something: a glyph with colour layers, or one with an outline and a width (a variation selector has a width and no outline).
            var drawn = new List<int>();
            foreach (int g in glyphs)
            {
                if (font.LayersOf(g) != null || (font.AdvanceOfGlyph(g) > 0f && font.TryGlyphById(g, 0.0, out GlyphOutline probe) && !probe.IsEmpty))
                    drawn.Add(g);
            }

            if (drawn.Count == 0)
                return null;
            // A run of several glyphs: the font draws two regional indicators (a flag) as two letters side by side, and so are they here. Any other run (a family, a couple: three people that the colour glyphs
            // of the font set in one em box by COLR version 1, which is not read) is drawn as its first glyph, one person that can be told; set side by side or on one another they are not a family.
            bool letters = scalars.Count > 0 && scalars.TrueForAll(TextClusters.IsRegionalIndicator);
            if (!letters && drawn.Count > 1)
                drawn.RemoveRange(1, drawn.Count - 1);
            if (drawn.Count > MaxSideBySide)
                drawn.RemoveRange(MaxSideBySide, drawn.Count - MaxSideBySide);
            if (drawn.Count == 1)
            {
                ColourLayer[] layers = font.LayersOf(drawn[0]);
                if (layers != null)
                {
                    GlyphMesh coloured = Coloured(font, layers, drawn[0], flatness, bucket, 1f, 0f);
                    if (coloured != null)
                        return coloured;
                }

                return font.TryGlyphById(drawn[0], flatness, out GlyphOutline outline) && !outline.IsEmpty ? MeshOf(outline, font.UnitsPerEm, flatness) : null;
            }

            return SideBySide(font, drawn, flatness, bucket);
        }

        /// <summary>The most glyphs a cell shows side by side (a flag's two letters).</summary>
        public const int MaxSideBySide = 2;

        /// <summary>The glyphs of a cell in a row, each a share of the cell's width (all of them as wide as the cell together), moved to its place; the layers of all of them in one plan.</summary>
        private static GlyphMesh SideBySide(FontOutlines font, List<int> drawn, double flatness, int bucket)
        {
            float cell = 0f;
            foreach (int g in drawn)
                cell = Math.Max(cell, font.AdvanceOfGlyph(g) / font.UnitsPerEm);
            if (cell <= 0f)
                cell = 1f;
            float each = cell / drawn.Count;
            var layers = new List<EmojiLayer>();
            var loops = new List<List<Point2>>();
            for (int i = 0; i < drawn.Count; i++)
            {
                // The glyph's own box is its advance wide; it is scaled to its slot and put in the slot's middle.
                float own = font.AdvanceOfGlyph(drawn[i]) > 0f ? font.AdvanceOfGlyph(drawn[i]) / font.UnitsPerEm : cell;
                float shrink = Math.Min(1f, each / own);
                float x = i * each + (each - own * shrink) * 0.5f;
                GlyphMesh one;
                ColourLayer[] colour = font.LayersOf(drawn[i]);
                if (colour != null)
                    one = Coloured(font, colour, drawn[i], flatness, bucket, shrink, x);
                else if (font.TryGlyphById(drawn[i], flatness, out GlyphOutline outline) && !outline.IsEmpty)
                    one = Moved(MeshOf(outline, font.UnitsPerEm, flatness), shrink, x);
                else
                    continue;
                if (one == null)
                    continue;
                loops.AddRange(one.Loops);
                if (one.Emoji != null)
                    layers.AddRange(one.Emoji.Layers);
                else
                    layers.Add(new EmojiLayer { Mesh = one, Colour = SolidColour.GlowWarm, R = 250, G = 226, B = 180 });
            }

            if (loops.Count == 0)
                return null;
            GlyphMesh silhouette = TextGlyphs.FromLoops(loops, cell);
            if (silhouette == null)
                return null;
            silhouette.Emoji = new EmojiPlan { Layers = layers };
            return silhouette;
        }

        /// <summary>
        /// The mesh of an outline. Contours that overlap or touch (a net of holes, a ring and its spokes, a stroke laid over another) are not triangulated right as they are, the triangles cover what is not
        /// filled: such an outline is filled by the winding rule on a grid and its outline traced again (Traced).
        /// </summary>
        private static GlyphMesh MeshOf(GlyphOutline outline, float unitsPerEm, double flatness)
        {
            GlyphMesh mesh = TextGlyphs.FromOutline(outline, unitsPerEm);
            if (mesh == null || mesh.IsSound(out _))
                return mesh;
            return Whole(Traced(TextGlyphs.ContoursOf(outline, unitsPerEm), mesh.Advance, (float)(flatness / unitsPerEm)) ?? mesh);
        }

        /// <summary>
        /// The mesh as it is, or, when its triangles do not add up to its area (the triangulator knots on a ring of dozens of holes: the keys of a keyboard), the same outline with its holes filled: loops
        /// with no hole are triangulated whole. A glyph with stray triangles would show them, one with its holes filled shows the shape.
        /// </summary>
        private static GlyphMesh Whole(GlyphMesh mesh)
        {
            if (mesh == null || mesh.IsSound(out _))
                return mesh;
            List<List<Point2>> outers = mesh.Loops.FindAll(loop => CutOutline.Area(loop) > 0f);
            return outers.Count == 0 ? mesh : TextGlyphs.FromLoops(outers, mesh.Advance);
        }

        /// <summary>
        /// Loops filled by the winding rule on a grid and traced again, as a mesh (GlyphRaster; the same as the silhouette of an emoji). The triangulator can knot on loops whose corners line up (the
        /// stripes of a crane, the windows of a tram): when the triangles of the traced outline do not add up to its area it is traced again with the tolerance doubled, up to four times, which is
        /// enough for the emoji that were found to need it; if none adds up the last is kept (Whole then fills its holes). Null when the loops hold nothing.
        /// </summary>
        private static GlyphMesh Traced(List<List<Point2>> loops, float advance, float tolerance)
        {
            GlyphMesh mesh = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                List<List<Point2>> traced = GlyphRaster.Offset(loops, 0f, tolerance * (1 << attempt));
                if (traced.Count == 0)
                    return mesh;
                mesh = TextGlyphs.FromLoops(traced, advance);
                if (mesh.IsSound(out _))
                    return mesh;
            }

            return mesh;
        }

        /// <summary>A mesh scaled about the origin and moved right by x (em).</summary>
        private static GlyphMesh Moved(GlyphMesh mesh, float scale, float x)
        {
            if (mesh == null)
                return null;
            var loops = new List<List<Point2>>(mesh.Loops.Count);
            foreach (List<Point2> loop in mesh.Loops)
            {
                var moved = new List<Point2>(loop.Count);
                foreach (Point2 p in loop)
                    moved.Add(new Point2(p.X * scale + x, p.Y * scale));
                loops.Add(moved);
            }

            return TextGlyphs.FromLoops(loops, mesh.Advance * scale);
        }

        private static GlyphMesh Coloured(FontOutlines font, ColourLayer[] layers, int baseGlyph, double flatness, int bucket, float scale, float shiftX)
        {
            float upe = font.UnitsPerEm;
            float advance = Math.Max(0f, font.AdvanceOfGlyph(baseGlyph)) / upe;
            var made = new List<(GlyphMesh Mesh, ColourLayer Layer, float Area)>();
            double flat = flatness;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                made.Clear();
                int points = 0;
                foreach (ColourLayer layer in layers)
                {
                    if (!font.TryGlyphById(layer.Glyph, flat, out GlyphOutline outline) || outline.IsEmpty)
                        continue;
                    GlyphMesh mesh = MeshOf(outline, upe, flat);
                    if (mesh != null && (scale != 1f || shiftX != 0f))
                        mesh = Moved(mesh, scale, shiftX);
                    if (mesh == null)
                        continue;
                    points += mesh.LoopPoints;
                    made.Add((mesh, layer, Math.Abs(mesh.Area)));
                }

                if (points <= EmojiPlan.MaxPoints || attempt == 3)
                    break;
                flat *= 2.0;
            }

            if (made.Count == 0)
                return null;
            // The layers kept when there are too many: the largest by area, drawn in their own order.
            if (made.Count > EmojiPlan.MaxLayers)
            {
                float cut = made.Select(m => m.Area).OrderByDescending(a => a).ElementAt(EmojiPlan.MaxLayers - 1);
                var kept = new List<(GlyphMesh Mesh, ColourLayer Layer, float Area)>();
                foreach (var m in made)
                {
                    if (m.Area >= cut && kept.Count < EmojiPlan.MaxLayers)
                        kept.Add(m);
                }

                made = kept;
            }

            // The silhouette: every layer's loops together, filled by the winding and traced again: the shape the layers cover.
            var all = new List<List<Point2>>();
            foreach (var m in made)
                all.AddRange(m.Mesh.Loops);
            GlyphMesh silhouette = Whole(Traced(all, advance, (float)(flat / upe)) ?? TextGlyphs.FromLoops(all, advance));
            if (silhouette == null)
                return null;
            var plan = new EmojiPlan();
            foreach (var m in made)
            {
                var layer = new EmojiLayer { Mesh = m.Mesh, R = m.Layer.R, G = m.Layer.G, B = m.Layer.B };
                // A layer that is "the text's colour" (alpha 0) is lit in the first colour of the palette, the board's own.
                layer.Colour = m.Layer.A == 0 ? SolidColour.GlowWarm : EmojiColours.Nearest(m.Layer.R, m.Layer.G, m.Layer.B);
                plan.Layers.Add(layer);
            }

            silhouette.Emoji = plan;
            return silhouette;
        }

        /// <summary>The mesh of a cluster from the first font of the list that draws it (the symbol and emoji fonts of the machine, in the order they are tried); null when none does.</summary>
        public static GlyphMesh FromChain(IReadOnlyList<FontOutlines> chain, string cluster, int bucket)
        {
            if (chain == null)
                return null;
            foreach (FontOutlines font in chain)
            {
                GlyphMesh mesh = Build(font, cluster, bucket);
                if (mesh != null)
                    return mesh;
            }

            return null;
        }
    }
}
