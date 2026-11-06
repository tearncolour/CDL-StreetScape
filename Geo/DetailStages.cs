namespace CDLC.TS.Geo
{
    /// <summary>The steps of making a baked-detail material, in the order they are taken (0.12.76: one went in a frame before, 40.7 ms for a tree grate in the owner's 0.12.75 log).</summary>
    public enum DetailStage
    {
        /// <summary>The game's material library is looked at (once) and the surface is there; the pixels of its maps are started on workers.</summary>
        Prepare,

        /// <summary>The colour map's texture is made from the pixels a worker made.</summary>
        Colour,

        /// <summary>A surface with holes: the coverage of its alpha put in the mips of the colour map.</summary>
        Holes,

        /// <summary>The colour map is compressed.</summary>
        Compress,

        /// <summary>The kind's normal map (once for every colour of the kind).</summary>
        Normal,

        /// <summary>The kind's mask map (once for every colour of the kind).</summary>
        Mask,

        /// <summary>The material itself: copied from the game's, its textures put in, validated.</summary>
        Material,

        Done
    }

    /// <summary>The order of the stages of a detail material: the engine part (TsMaterials) takes one stage at a time and this says which comes next.</summary>
    public static class DetailStages
    {
        public static DetailStage Next(DetailStage stage, bool holes, bool mapsMade)
        {
            switch (stage)
            {
                case DetailStage.Prepare:
                    return DetailStage.Colour;
                case DetailStage.Colour:
                    return holes ? DetailStage.Holes : DetailStage.Compress;
                case DetailStage.Holes:
                    return DetailStage.Compress;
                case DetailStage.Compress:
                    return mapsMade ? DetailStage.Material : DetailStage.Normal;
                case DetailStage.Normal:
                    return DetailStage.Mask;
                case DetailStage.Mask:
                    return DetailStage.Material;
                default:
                    return DetailStage.Done;
            }
        }

        /// <summary>Whether another stage may be started in an update that has spent spentMs of budgetMs: the stage that is running is never cut short, so one starts only while there is budget left.</summary>
        public static bool MayStart(double spentMs, double budgetMs) => spentMs < budgetMs;
    }
}
