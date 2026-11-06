using CDLC.TS.Snap;
using Colossal.Mathematics;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>The game's cubic and point in this mod's pure ones (Tests/GameMath compares them with the game's own MathUtils.Position).</summary>
    internal static class CurveConvert
    {
        internal static Vec3 Point(float3 v) => new Vec3(v.x, v.y, v.z);

        internal static Bezier3 Curve(Bezier4x3 b) => new Bezier3(Point(b.a), Point(b.b), Point(b.c), Point(b.d));
    }
}
