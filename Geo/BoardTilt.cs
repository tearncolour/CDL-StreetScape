using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// How far a board over the carriageway leans forward, its top toward the traffic (0.12.55; JTG D82-2009 2.3.5-4 "板面宜前倾0°～15°", GB 51038-2015 4.3.7-4 and Fig. 4.3.7 b): in steps of Step
    /// degrees, 0 to Max steps (TsItem's two high flag bits). The boards on an arm or a beam lean, and those on another mod's signal arm (车行道上方附着式); those on a post stand upright. A new one
    /// leans Default steps, 5°: this mod's choice within the range.
    /// </summary>
    public static class BoardTilt
    {
        public const float Step = 5f;
        public const int Max = 3, Default = 1;

        public static float Radians(int steps) => Math.Max(0, Math.Min(Max, steps)) * Step * (float)(Math.PI / 180.0);

        /// <summary>The steps of a lean in radians (the nearest).</summary>
        public static int StepsOf(float radians) => Math.Max(0, Math.Min(Max, (int)Math.Round(radians / (Step * Math.PI / 180.0))));

        /// <summary>Whether the boards of a support lean (not those on its post).</summary>
        public static bool Leans(SupportKind kind) => SupportKinds.Hung(kind) || SupportKinds.OnArm(kind);

        /// <summary>As above for a support: the framed cantilever's boards hang plumb from their rods (0.12.57: the Hangzhou photos and fig. A.1); a board hung under a structure over the road leans (0.12.81).</summary>
        public static bool Leans(SupportDef support) => support != null && !support.Framed && (Leans(support.Kind) || support.Hanger);
    }
}
