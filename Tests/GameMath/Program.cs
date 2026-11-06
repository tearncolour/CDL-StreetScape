using System;
using CDLC.TS;
using CDLC.TS.Snap;
using Colossal.Mathematics;
using Unity.Mathematics;

namespace CDLC.TS.GameMath
{
    /// <summary>The pure Bezier3 made from the game's Bezier4x3 is the same curve: every parameter, derivative and the end points agree with MathUtils (the one conversion the pure sampler cannot test by itself).</summary>
    internal static class Program
    {
        private static int s_Checks, s_Failed;

        private static void Check(bool ok, string what)
        {
            s_Checks++;
            if (!ok)
            {
                s_Failed++;
                Console.WriteLine("  FAIL " + what);
            }
        }

        private static int Main()
        {
            var random = new System.Random(20261102);
            double worst = 0.0, worstTangent = 0.0;
            for (int i = 0; i < 2000; i++)
            {
                float3 R() => new float3((float)(random.NextDouble() * 400.0 - 200.0), (float)(random.NextDouble() * 40.0 - 20.0), (float)(random.NextDouble() * 400.0 - 200.0));
                var game = new Bezier4x3 { a = R(), b = R(), c = R(), d = R() };
                Bezier3 pure = CurveConvert.Curve(game);
                Check(pure.A.X == game.a.x && pure.A.Y == game.a.y && pure.A.Z == game.a.z && pure.B.X == game.b.x && pure.B.Y == game.b.y && pure.B.Z == game.b.z
                    && pure.C.X == game.c.x && pure.C.Y == game.c.y && pure.C.Z == game.c.z && pure.D.X == game.d.x && pure.D.Y == game.d.y && pure.D.Z == game.d.z, "the control points are copied one for one, x to X, y to Y, z to Z, a to A ... d to D");
                for (int s = 0; s <= 32; s++)
                {
                    float t = s / 32f;
                    float3 g = MathUtils.Position(game, t);
                    Vec3 p = pure.Point(t);
                    worst = Math.Max(worst, Math.Max(Math.Abs(g.x - p.X), Math.Max(Math.Abs(g.y - p.Y), Math.Abs(g.z - p.Z))));
                    float3 gt = MathUtils.Tangent(game, t);
                    Vec3 pt = pure.Tangent(t);
                    // The game's tangent is the derivative divided by 3 (or not: compare directions).
                    float3 gd = math.normalizesafe(gt);
                    Vec3 pd = pt.LengthXZ > 1e-6f ? pt * (1f / (float)Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y + pt.Z * pt.Z)) : pt;
                    worstTangent = Math.Max(worstTangent, Math.Max(Math.Abs(gd.x - pd.X), Math.Max(Math.Abs(gd.y - pd.Y), Math.Abs(gd.z - pd.Z))));
                }

                Check(pure.Point(0f).X == game.a.x && pure.Point(0f).Z == game.a.z && Math.Abs(pure.Point(1f).X - game.d.x) < 1e-3f && Math.Abs(pure.Point(1f).Z - game.d.z) < 1e-3f, "t = 0 is a and t = 1 is d");
            }

            Check(worst < 5e-4, $"the point at every parameter is the game's to within {worst * 1000.0:0.000} mm (a float's rounding of two ways to evaluate a cubic at 200 m)");
            Check(worstTangent < 1e-3, $"the direction of the tangent is the game's (largest difference {worstTangent:0.000000})");
            Console.WriteLine($"{s_Checks} checks, {s_Failed} failed; worst point difference {worst * 1000.0:0.0000} mm, worst tangent difference {worstTangent:0.000000}");
            return s_Failed == 0 ? 0 : 1;
        }
    }
}
