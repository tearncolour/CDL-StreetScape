using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>Random roads, places and drags for the follow tests and its bench: a curve as a road is, a place near it, a curve after a node drag by Move It's neighbours rule, and after a translation and a turn.</summary>
    internal static class FollowFixture
    {
        // ------------------------------------------------------------------ random roads and items

        internal static Bezier3 RandomCurve(Random random, float reach = 3000f, float bend = 0.3f, float longest = 150f)
        {
            var a = new Vec3((float)(random.NextDouble() * 2 * reach - reach), (float)(random.NextDouble() * 20.0), (float)(random.NextDouble() * 2 * reach - reach));
            double heading = random.NextDouble() * Math.PI * 2.0, length = 12.0 + random.NextDouble() * (longest - 12.0);
            var d = a + new Vec3((float)(Math.Sin(heading) * length), (float)(random.NextDouble() * 6.0 - 3.0), (float)(Math.Cos(heading) * length));
            Vec3 side = new Vec3((float)Math.Cos(heading), 0f, (float)-Math.Sin(heading));
            float bendB = (float)((random.NextDouble() * 2 - 1) * bend * length), bendC = (float)((random.NextDouble() * 2 - 1) * bend * length);
            return new Bezier3(a, Vec3.Lerp(a, d, 1f / 3f) + side * bendB, Vec3.Lerp(a, d, 2f / 3f) + side * bendC, d);
        }

        /// <summary>A place near the road: inside its length or a little past an end, to either side.</summary>
        internal static Vec3 RandomPlace(Random random, Bezier3 c)
        {
            float t = (float)(random.NextDouble() * 1.1 - 0.05);
            Vec3 on = c.Point(Math.Max(0f, Math.Min(1f, t)));
            Vec3 right = c.Tangent(Math.Max(0f, Math.Min(1f, t))).RightXZ();
            return on + right * (float)(random.NextDouble() * 24.0 - 12.0) + c.Tangent(t < 0f ? 0f : 1f).HeadingXZ() * (t < 0f || t > 1f ? (float)(random.NextDouble() * 4.0) * (t < 0f ? -1f : 1f) : 0f) + new Vec3(0f, (float)(random.NextDouble() * 3.0 - 0.5), 0f);
        }

        /// <summary>The curve after a node was dragged as Move It's neighbours rule does it: the far end stays, the near end moves, the handles are the old ratios of the new chord (per component).</summary>
        internal static Bezier3 Neighbour(Bezier3 c, Vec3 delta)
        {
            Vec3 a = c.A, d = c.D + delta, v = c.D - c.A;
            Vec3 t1 = new Vec3(v.X == 0f ? 0.333f : (c.B.X - a.X) / v.X, v.Y == 0f ? 0.333f : (c.B.Y - a.Y) / v.Y, v.Z == 0f ? 0.333f : (c.B.Z - a.Z) / v.Z);
            Vec3 t2 = new Vec3(v.X == 0f ? 0.667f : (c.C.X - a.X) / v.X, v.Y == 0f ? 0.667f : (c.C.Y - a.Y) / v.Y, v.Z == 0f ? 0.667f : (c.C.Z - a.Z) / v.Z);
            Vec3 w = d - a;
            return new Bezier3(a, new Vec3(a.X + w.X * t1.X, a.Y + w.Y * t1.Y, a.Z + w.Z * t1.Z), new Vec3(a.X + w.X * t2.X, a.Y + w.Y * t2.Y, a.Z + w.Z * t2.Z), d);
        }

        internal static Bezier3 Rigid(Bezier3 c, Vec3 shift, float turn)
        {
            double s = Math.Sin(turn), co = Math.Cos(turn);
            // Each control point is made in double and rounded to a float once, as the game writes them (the rounding of the points is independent: a half step of the coordinate each).
            Vec3 Map(Vec3 p)
            {
                double x = (double)p.X - c.A.X, z = (double)p.Z - c.A.Z;
                return new Vec3((float)(c.A.X + (double)shift.X + x * co + z * s), (float)(p.Y + (double)shift.Y), (float)(c.A.Z + (double)shift.Z + z * co - x * s));
            }

            return new Bezier3(Map(c.A), Map(c.B), Map(c.C), Map(c.D));
        }

        /// <summary>The curve moved by (40, 1, -25) and turned 0.4 radians about its start.</summary>
        public static Bezier3 Moved(Bezier3 c) => Rigid(c, new Vec3(40f, 1f, -25f), 0.4f);
    }
}
