using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Keeps the pictures of the faces inside a memory budget (0.7.0). The tiers of the streaming (TileStreamer) are by distance, so in a very dense place the near and middle tiles can still want
    /// more pictures than the graphics card should hold. The share of the distances that is used goes down, a step at a time, while the pictures are over the budget, and back up while they are well
    /// under it: the near tiles shrink to the ones the camera is in, and come back when the camera moves to a thinner place. Pure, so that it is tested offline.
    /// </summary>
    public static class TextureBudget
    {
        /// <summary>The pictures may take this many bytes of graphics memory (a rough figure: RGBA32 with mips).</summary>
        public const long DefaultBytes = 384L * 1024 * 1024;

        /// <summary>The share of the distances is never taken below this: the tile the camera is in is always near.</summary>
        public const float MinScale = 0.25f;

        /// <summary>Over the budget the scale is multiplied by Down, and under UnderShare of it by Up; in between it stays.</summary>
        public const float Down = 0.85f, Up = 1.08f, UnderShare = 0.6f;

        /// <summary>The scale for the next second, from the scale now and the bytes held.</summary>
        public static float Next(float scale, long bytes, long budget)
        {
            if (bytes > budget)
                return Math.Max(MinScale, scale * Down);
            if (bytes < budget * UnderShare && scale < 1f)
                return Math.Min(1f, scale * Up);
            return scale;
        }

        /// <summary>The options with the near and middle distances scaled (the far limit is the draw distance and does not change: far tiles have small pictures, which are what the budget leaves them).</summary>
        public static StreamOptions Scaled(StreamOptions options, float scale)
        {
            options.NearMetres *= scale;
            options.MidMetres *= scale;
            return options;
        }
    }
}
