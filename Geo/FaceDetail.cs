using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// How big the picture of a face is (0.7.0). A board 300 m away is a few pixels wide, and a picture of 768 pixels for it is 1.7 MB of graphics memory that nobody sees: the faces of near tiles
    /// are painted at full size, those of middle tiles at half and those of far tiles at a few dozen pixels (a thousand different boards then take tens of megabytes, not gigabytes). The material
    /// of a face is named by the board and the detail, so that the same board at two details is two pictures.
    /// </summary>
    public static class FaceDetail
    {
        public const int Full = 0, Half = 1, Small = 2;

        /// <summary>The longest side of the picture (pixels) at each detail.</summary>
        public static int MaxSide(int detail) => detail == Full ? SignInstance.MaxTexture : detail == Half ? 384 : 96;

        public static int OfTier(TileTier tier) => tier == TileTier.Near ? Full : tier == TileTier.Mid ? Half : Small;

        public static SupportDetail SupportOf(TileTier tier) => tier == TileTier.Far || tier == TileTier.Horizon || tier == TileTier.None ? SupportDetail.Reduced : SupportDetail.Full;

        // ------------------------------------------------------------------ the names of face materials

        private const string Prefix0 = ChunkBuilder.FacePrefix, Prefix1 = "face1:", Prefix2 = "face2:";

        /// <summary>The name of the material of a board's face at a detail: "face:" for full size (as it has always been), "face1:" for half, "face2:" for small.</summary>
        public static string Material(string instanceKey, int detail) => (detail == Full ? Prefix0 : detail == Half ? Prefix1 : Prefix2) + instanceKey;

        public static bool IsFace(string material) =>
            material.StartsWith(Prefix0, StringComparison.Ordinal) || material.StartsWith(Prefix1, StringComparison.Ordinal) || material.StartsWith(Prefix2, StringComparison.Ordinal);

        /// <summary>The board and the detail a face material is named for. False for a material that is not a face's.</summary>
        public static bool TryParse(string material, out string instanceKey, out int detail)
        {
            instanceKey = null;
            detail = Full;
            if (material == null)
                return false;
            if (material.StartsWith(Prefix0, StringComparison.Ordinal))
            {
                instanceKey = material.Substring(Prefix0.Length);
                return true;
            }

            if (material.StartsWith(Prefix1, StringComparison.Ordinal))
            {
                instanceKey = material.Substring(Prefix1.Length);
                detail = Half;
                return true;
            }

            if (material.StartsWith(Prefix2, StringComparison.Ordinal))
            {
                instanceKey = material.Substring(Prefix2.Length);
                detail = Small;
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ the pictures

        /// <summary>
        /// The instance as it is painted at a detail: the same board with a smaller picture (the longest side at most maxSide, the shape kept, a multiple of four pixels), which is all the painter
        /// needs to paint it small. An instance that has no board (a classic sign) or is not larger than that is returned as it is.
        /// </summary>
        public static SignInstance Sized(SignInstance instance, int maxSide)
        {
            if (instance == null || instance.Board == null)
                return instance;
            int longest = Math.Max(instance.TexW, instance.TexH);
            if (longest <= maxSide)
                return instance;
            float scale = maxSide / (float)longest;
            int w = Math.Max(8, (int)Math.Round(instance.TexW * scale / 4f) * 4), h = Math.Max(8, (int)Math.Round(instance.TexH * scale / 4f) * 4);
            return new SignInstance
            {
                Def = instance.Def,
                Key = instance.Key,
                Spec = instance.Spec,
                Board = instance.Board,
                Settings = instance.Settings,
                TexW = w,
                TexH = h,
                PixelsPerMetre = instance.PixelsPerMetre * scale,
                BoundsX0 = instance.BoundsX0,
                BoundsY0 = instance.BoundsY0,
                BoundsX1 = instance.BoundsX1,
                BoundsY1 = instance.BoundsY1
            };
        }

        /// <summary>
        /// The colour a face has until its picture is there (and for ever for a screen seen from afar): the ground colour of the main board, dark for an LED screen, grey for what is not known.
        /// A pure look at the layout: no painting, so it is there at once.
        /// </summary>
        public static void Placeholder(SignInstance instance, out byte r, out byte g, out byte b)
        {
            if (instance != null && instance.IsLed)
            {
                r = 14;
                g = 14;
                b = 16;
                return;
            }

            if (instance == null || instance.Board == null || instance.Board.Parts.Count == 0)
            {
                r = 150;
                g = 150;
                b = 150;
                return;
            }

            // A sign with a fluorescent frame (示16, the school bus 示40) has the frame for its ground and the sign inside it as two elements, white and then the ground of the sign: the sign is what
            // the face is, so its colour is the one to stand in for it.
            GuidePart main = instance.Board.Main;
            GuideColour colour = main.Ground;
            if (main.Ground == GuideColour.FluorescentYellowGreen && main.Elements.Count >= 2 && main.Elements[0].Kind == GuideElementKind.Rect && main.Elements[1].Kind == GuideElementKind.Rect)
                colour = main.Elements[1].Colour;
            GuideColours.Rgb(colour, out r, out g, out b);
        }

        /// <summary>Whether a face at this detail is never painted: an LED screen seen from afar is a dark plate (its dots are not built either), and painting it would read every dot of its picture.</summary>
        public static bool NeverPainted(SignInstance instance, int detail) => instance != null && instance.IsLed && detail == Small;
    }
}
