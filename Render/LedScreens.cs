using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Render
{
    /// <summary>
    /// The dots of the LED screens (variable-message signs): a screen is painted like any board (LegendPainter) and read dot by dot (Led.Quantize); the grid is kept for the instance, since the
    /// mesh of a tile and the picture of the face both come from it. No Unity types here: the same file is compiled into the preview program. Main thread.
    /// </summary>
    public static class LedScreens
    {
        private const int kMaxCached = 400;
        private static readonly Dictionary<string, LedGrid> s_Grids = new Dictionary<string, LedGrid>();

        /// <summary>The grids by what they are made from (Led.PictureKey): another instance with the same board (only the road's speed band differs) takes the grid that is there.</summary>
        private static readonly Dictionary<string, LedGrid> s_ByPicture = new Dictionary<string, LedGrid>();

        /// <summary>How many grids were painted, and how many were found by the picture (for the log and the tests of the preview program).</summary>
        public static int Painted { get; private set; }

        public static int FoundByPicture { get; private set; }

        /// <summary>The dots of an LED screen; null for anything else. Painted and read the first time, then kept.</summary>
        public static LedGrid Grid(SignInstance instance)
        {
            if (instance == null || !instance.IsLed)
                return null;
            lock (s_Grids)
            {
                if (s_Grids.TryGetValue(instance.Key, out LedGrid cached))
                    return cached;
            }

            string pictureKey = Led.PictureKey(instance);
            if (pictureKey != null)
            {
                lock (s_Grids)
                {
                    if (s_ByPicture.TryGetValue(pictureKey, out LedGrid same))
                    {
                        if (s_Grids.Count > kMaxCached)
                            s_Grids.Clear();
                        s_Grids[instance.Key] = same;
                        FoundByPicture++;
                        return same;
                    }
                }
            }

            // The picture the dots are read from is painted for them: several pixels to a dot, not the 768 pixels a board's face has (which would give a wide screen's dots two or three).
            Led.Dimensions(instance.Board, instance.Def.Width, instance.Def.Height, out int dotsX, out int dotsY);
            Led.SampleSize(dotsX, dotsY, out int sampleW, out int sampleH, instance.Board.LedSamples);
            SignInstance sized = new SignInstance
            {
                Def = instance.Def,
                Key = instance.Key,
                Spec = instance.Spec,
                Board = instance.Board,
                Settings = instance.Settings,
                TexW = sampleW,
                TexH = sampleH,
                PixelsPerMetre = sampleW / Math.Max(0.01f, instance.BoundsX1 - instance.BoundsX0),
                BoundsX0 = instance.BoundsX0,
                BoundsY0 = instance.BoundsY0,
                BoundsX1 = instance.BoundsX1,
                BoundsY1 = instance.BoundsY1
            };
            byte[] picture = LegendPainter.Paint(sized, out int w, out int h);
            LedGrid grid = Led.Quantize(picture, w, h, dotsX, dotsY);
            lock (s_Grids)
            {
                if (s_Grids.Count > kMaxCached)
                    s_Grids.Clear();
                if (s_ByPicture.Count > kMaxCached)
                    s_ByPicture.Clear();
                s_Grids[instance.Key] = grid;
                if (pictureKey != null)
                    s_ByPicture[pictureKey] = grid;
                Painted++;
            }

            return grid;
        }

        /// <summary>
        /// The picture of a face: for an LED screen the dots as a picture (black with its lit dots: what it looks like from afar, under the lit dots that stand in front of it), for any other
        /// board what the painter makes. Same order as LegendPainter.Paint; the fonts are SignFonts'.
        /// </summary>
        public static byte[] PaintFace(SignInstance instance, out int w, out int h)
        {
            if (instance != null && instance.IsLed)
            {
                LedGrid grid = Grid(instance);
                if (grid != null && grid.PictureW > 0 && grid.PictureH > 0)
                {
                    // The face has its own size, four pixels to a dot (up to 1024 on the long side): the picture the dots were read from is much larger and is not kept.
                    Led.FaceSize(grid.DotsX, grid.DotsY, out w, out h);
                    return Led.Raster(grid, w, h);
                }
            }

            return LegendPainter.Paint(instance, out w, out h);
        }

        public static void Clear()
        {
            lock (s_Grids)
            {
                s_Grids.Clear();
                s_ByPicture.Clear();
            }
        }
    }
}
