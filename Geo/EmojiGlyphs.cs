using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>One coloured layer of an emoji on a lit board: its face as a mesh in em units, and the solid colour it is lit in (the nearest of the glow colours of the board's palette).</summary>
    public sealed class EmojiLayer
    {
        public GlyphMesh Mesh;
        public SolidColour Colour;

        /// <summary>The layer's own colour in the font, RGB: what the glow colour was snapped from.</summary>
        public byte R, G, B;
    }

    /// <summary>
    /// A colour emoji as a lit board is made of it (0.12.78): its silhouette (the union of every layer: the plan's frame holds this and the return is made of it) and its layers in the order they are drawn,
    /// each a little in front of the one before so that no two faces are in one plane. A GlyphMesh with Emoji set draws its layers in their colours instead of its face in the text's glow colour.
    /// </summary>
    public sealed class EmojiPlan
    {
        public List<EmojiLayer> Layers = new List<EmojiLayer>();

        /// <summary>How far a layer is in front of the one before (m): 1.5 mm.</summary>
        public const float LayerStep = 0.0015f;

        /// <summary>The most layers drawn: a glyph with more has the rest left out (the biggest layers are kept: the face, the eyes, not the highlights).</summary>
        public const int MaxLayers = 24;

        /// <summary>The most points all the layers of one emoji may have, in total: over it the flatness is doubled until it is under (an emoji of 30 layers of dozens of curves each).</summary>
        public const int MaxPoints = 2400;

        public int FaceVertices
        {
            get
            {
                int v = 0;
                foreach (EmojiLayer layer in Layers)
                    v += layer.Mesh.FaceVertices;
                return v;
            }
        }

        public int FaceTriangles
        {
            get
            {
                int t = 0;
                foreach (EmojiLayer layer in Layers)
                    t += layer.Mesh.FaceTriangles;
                return t;
            }
        }

        /// <summary>About what the layers hold in memory (bytes): for the glyph cache's budget.</summary>
        public long Bytes
        {
            get
            {
                long b = 32;
                foreach (EmojiLayer layer in Layers)
                    b += 16 + layer.Mesh.Bytes;
                return b;
            }
        }
    }

    /// <summary>
    /// The colours of an emoji put on the palette a lit board has (0.12.78): a layer is drawn in the colour of the palette that is nearest to its colour in the font. Pure.
    /// </summary>
    public static class EmojiColours
    {
        /// <summary>
        /// The colours an emoji may be lit in: the board's glow colours, and the plain (unlit) solids for what is dark in the font: black for an outline, the brown of a dark skin and of hair. A plain solid is
        /// not lit at night, which is how a dark colour should look on a sign.
        /// </summary>
        public static readonly SolidColour[] Palette =
        {
            SolidColour.GlowRed, SolidColour.GlowGold, SolidColour.GlowWarm, SolidColour.GlowWhite, SolidColour.GlowBlue, SolidColour.GlowPink, SolidColour.GlowGreen, SolidColour.Black, SolidColour.DarkBrown,
            SolidColour.Wood
        };

        /// <summary>The colour of the palette nearest to (r, g, b), by distance in a perceptual-ish space (the weighted RGB of "redmean": a cheap approximation of the eye's distance).</summary>
        public static SolidColour Nearest(byte r, byte g, byte b)
        {
            SolidColour best = SolidColour.GlowWarm;
            double bestDistance = double.MaxValue;
            foreach (SolidColour colour in Palette)
            {
                byte[] rgb = Raster.SolidRgb(colour);
                double d = Distance(r, g, b, rgb[0], rgb[1], rgb[2]);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = colour;
                }
            }

            return best;
        }

        /// <summary>The "redmean" distance of two RGB colours (squared): the red and blue weights follow how red the pair is.</summary>
        public static double Distance(int r1, int g1, int b1, int r2, int g2, int b2)
        {
            double rm = (r1 + r2) * 0.5, dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            return (2.0 + rm / 256.0) * dr * dr + 4.0 * dg * dg + (2.0 + (255.0 - rm) / 256.0) * db * db;
        }
    }
}
