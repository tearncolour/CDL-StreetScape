using System;
using System.Globalization;

namespace CDLC.TS.Geo
{
    /// <summary>What the surface under the cursor is, from its normal.</summary>
    public enum SurfaceKind
    {
        None,
        Wall,
        Roof,
        Slope
    }

    /// <summary>What a building-attach support does with what is under the cursor.</summary>
    public enum SurfacePlan
    {
        Nothing,
        Wall,
        Roof,
        HintRoof,
        HintWall,
        HintSlope
    }

    /// <summary>
    /// Wall or roof under the cursor, and which support a board takes there (0.12.77). A text board with a building support chosen goes on whatever is under the cursor: a wall on the wall support, a
    /// roof on the roof support, whichever of the two is picked in the panel (the owner, 2 Nov: "还有建筑物屋顶还是不能放置": he had picked the wall support four times and never the roof one, and over a roof
    /// the wall support showed a hint only). Any other board keeps what the chosen support does. A slope between the two limits (a steep or a domed roof) is told apart from a wall: it is neither. Pure.
    /// </summary>
    public static class BuildingSurface
    {
        /// <summary>A surface whose unit normal's y is at least this is a roof (about 45 degrees); the wall limit is WallMount.MaxNormalY.</summary>
        public const float RoofMinNormalY = 0.7f;

        public static SurfaceKind Classify(float normalX, float normalY, float normalZ)
        {
            double length = Math.Sqrt((double)normalX * normalX + (double)normalY * normalY + (double)normalZ * normalZ);
            if (!(length > 1e-4) || double.IsInfinity(length) || double.IsNaN(length))
                return SurfaceKind.None;
            double up = Math.Abs(normalY) / length;
            if (up <= WallMount.MaxNormalY)
                return SurfaceKind.Wall;
            return up >= RoofMinNormalY ? SurfaceKind.Roof : SurfaceKind.Slope;
        }

        /// <summary>What to do: building is whether the ray hit a building at all; text, whether the board is the text light box (with its supports switched on: textSupports); roofSupport, whether the support chosen is the roof one.</summary>
        public static SurfacePlan Plan(bool building, SurfaceKind surface, bool text, bool textSupports, bool roofSupport)
        {
            if (!building || surface == SurfaceKind.None)
                return SurfacePlan.Nothing;
            if (surface == SurfaceKind.Slope)
                return SurfacePlan.HintSlope;
            if (text && textSupports)
                return surface == SurfaceKind.Roof ? SurfacePlan.Roof : SurfacePlan.Wall;
            if (roofSupport)
                return surface == SurfaceKind.Roof ? SurfacePlan.Roof : SurfacePlan.HintWall;
            return surface == SurfaceKind.Wall ? SurfacePlan.Wall : SurfacePlan.HintRoof;
        }

        /// <summary>One line for the log: what was hit, how it was read and what was decided (the first thirty changes of it).</summary>
        public static string Describe(float normalX, float normalY, float normalZ, int hitEntity, bool building, SurfaceKind surface, SurfacePlan plan, bool text, bool roofSupport)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            double length = Math.Sqrt((double)normalX * normalX + (double)normalY * normalY + (double)normalZ * normalZ);
            double slope = length > 1e-4 ? Math.Acos(Math.Min(1.0, Math.Abs(normalY) / length)) * 180.0 / Math.PI : double.NaN;
            return string.Format(c, "Surface: hit direction ({0:0.00}, {1:0.00}, {2:0.00}) on entity {3}, {4}, {5}{6}; {7} board, {8} support chosen: {9}",
                normalX, normalY, normalZ, hitEntity, building ? "a building" : "no building", surface.ToString().ToLowerInvariant(),
                double.IsNaN(slope) ? "" : string.Format(c, " (tilted {0:0} degrees from the horizontal)", slope), text ? "text" : "other", roofSupport ? "roof" : "wall", plan);
        }
    }
}
