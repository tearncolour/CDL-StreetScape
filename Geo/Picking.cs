using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// One thing of a sign that can be pointed at: a post, the foot, the arm (each a box that fits it), or the board (its real outline in the plane of the board).
    /// In the sign's own frame. The delete mode picks by these and not by one box round everything, so that pointing at the empty air next to a post or under an
    /// arm selects nothing.
    /// </summary>
    public sealed class PickShape
    {
        public string Name;

        /// <summary>The box (for the board: its bounds).</summary>
        public Vec3 Min, Max;

        /// <summary>The outline of the board round its middle; null for a box.</summary>
        public Point2[] Outline;

        public float CentreX, CentreY, PlaneZ;

        /// <summary>For a board: the number of the board on its support (AssemblyBoard.Member); -1 for the support's own parts.</summary>
        public int Member = -1;

        /// <summary>A board turned on its post (0.12.8): the shape is turned by Turn (radians, the sense of a yaw) about the upright line at sign-frame X AxisX, z = 0.</summary>
        public float Turn, AxisX, AxisZ;

        /// <summary>A board that leans forward on an arm or a beam (0.12.55): by Tilt (radians) about the line along X at TiltY, TiltZ, before its turn.</summary>
        public float Tilt, TiltY, TiltZ;

        /// <summary>
        /// How much more than Picking.Tolerance a box is grown for the ray (m): the post of a hung support takes small boards and is thin to point at from afar (0.12.8). A grown shape is looked at only
        /// when the ray hits nothing else of the sign, so that the boards in front of the post stay what is picked when the ray is on them.
        /// </summary>
        public float Grow;
    }

    public static class Picking
    {
        /// <summary>How far beside a shape a ray may pass and still pick it (m): a post is 8 cm thick, and a click is not a needle.</summary>
        public const float Tolerance = 0.06f;

        /// <summary>The distance from a point to a segment (m): how near an item is to the line of the cursor, to leave out the ones that cannot be under it before their shapes are looked at.</summary>
        public static float DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            float length2 = dx * dx + dy * dy + dz * dz;
            float t = length2 < 1e-12f ? 0f : ((p.X - a.X) * dx + (p.Y - a.Y) * dy + (p.Z - a.Z) * dz) / length2;
            t = Math.Max(0f, Math.Min(1f, t));
            float ex = a.X + dx * t - p.X, ey = a.Y + dy * t - p.Y, ez = a.Z + dz * t - p.Z;
            return (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);
        }

        /// <summary>
        /// The ray from start to end (world space) against the pick shapes of a sign placed at a position with a yaw (the transform of MeshBuilder.Append).
        /// Returns the distance from the start to the nearest hit; false when nothing of the sign is on the ray.
        /// </summary>
        public static bool Pick(IReadOnlyList<PickShape> shapes, Vec3 position, float yaw, Vec3 start, Vec3 end, out float distance) => Pick(shapes, position, yaw, start, end, out distance, out _);

        /// <summary>As above, and the shape that was hit (the nearest). extraGrow: how much further than Tolerance a box takes the ray (m), for a pick that lets a thin thing win over what is wide (MarkingPick).</summary>
        public static bool Pick(IReadOnlyList<PickShape> shapes, Vec3 position, float yaw, Vec3 start, Vec3 end, out float distance, out PickShape hitShape, float extraGrow = 0f)
        {
            distance = 0f;
            hitShape = null;
            float s = (float)Math.Sin(yaw), c = (float)Math.Cos(yaw);

            // To the sign's frame: the inverse of Append's turn (local +Z is world (sin, 0, cos)).
            Vec3 o = start - position;
            Vec3 d = end - start;
            var lo = new Vec3(o.X * c - o.Z * s, o.Y, o.X * s + o.Z * c);
            var ld = new Vec3(d.X * c - d.Z * s, d.Y, d.X * s + d.Z * c);
            float length = d.Length;
            if (length < 1e-9f)
                return false;

            // The grown shapes (PickShape.Grow) only when nothing else of the sign is on the ray.
            float best = float.MaxValue;
            for (int pass = 0; pass < 2 && best == float.MaxValue; pass++)
            {
                foreach (PickShape shape in shapes)
                {
                    if ((shape.Grow > 0f) != (pass == 1))
                        continue;
                    float t;
                    Vec3 so = lo, sd = ld;
                    if (shape.Turn != 0f)
                    {
                        // Into the frame of a board turned on its post: the inverse of its turn about the post.
                        float ts = (float)Math.Sin(shape.Turn), tc = (float)Math.Cos(shape.Turn), x = lo.X - shape.AxisX, z = lo.Z - shape.AxisZ;
                        so = new Vec3(shape.AxisX + x * tc - z * ts, lo.Y, shape.AxisZ + x * ts + z * tc);
                        sd = new Vec3(ld.X * tc - ld.Z * ts, ld.Y, ld.X * ts + ld.Z * tc);
                    }

                    if (shape.Tilt != 0f)
                    {
                        // Into the frame of a board that leans forward: the inverse of its lean about the line along X.
                        float ls = (float)Math.Sin(shape.Tilt), lc = (float)Math.Cos(shape.Tilt), y = so.Y - shape.TiltY, z = so.Z - shape.TiltZ;
                        so = new Vec3(so.X, shape.TiltY + y * lc + z * ls, shape.TiltZ - y * ls + z * lc);
                        sd = new Vec3(sd.X, sd.Y * lc + sd.Z * ls, -sd.Y * ls + sd.Z * lc);
                    }

                    bool hit = shape.Outline != null ? RayBoard(shape, so, sd, out t) : RayBox(so, sd, shape.Min, shape.Max, Tolerance + shape.Grow + extraGrow, out t);
                    if (hit && t < best)
                    {
                        best = t;
                        hitShape = shape;
                    }
                }
            }

            if (best == float.MaxValue)
                return false;
            distance = best * length;
            return true;
        }

        /// <summary>The ray o + t d, t in 0 to 1, against a box grown by the tolerance; t is where it enters (0 when it starts inside).</summary>
        public static bool RayBox(Vec3 o, Vec3 d, Vec3 min, Vec3 max, float grow, out float t)
        {
            t = 0f;
            float tMin = 0f, tMax = 1f;
            return Slab(o.X, d.X, min.X - grow, max.X + grow, ref tMin, ref tMax) &&
                   Slab(o.Y, d.Y, min.Y - grow, max.Y + grow, ref tMin, ref tMax) &&
                   Slab(o.Z, d.Z, min.Z - grow, max.Z + grow, ref tMin, ref tMax) &&
                   (t = tMin) >= 0f;
        }

        private static bool Slab(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
        {
            if (Math.Abs(direction) < 1e-9f)
                return origin >= min && origin <= max;

            float t1 = (min - origin) / direction;
            float t2 = (max - origin) / direction;
            if (t1 > t2)
            {
                float swap = t1;
                t1 = t2;
                t2 = swap;
            }

            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            return tMin <= tMax;
        }

        /// <summary>The ray against the board: where it crosses the plane of the board, is that point inside the outline (or within the tolerance of it)?</summary>
        private static bool RayBoard(PickShape board, Vec3 o, Vec3 d, out float t)
        {
            t = 0f;
            if (Math.Abs(d.Z) < 1e-9f)
                return false;
            t = (board.PlaneZ - o.Z) / d.Z;
            if (t < 0f || t > 1f)
                return false;

            float x = o.X + d.X * t - board.CentreX;
            float y = o.Y + d.Y * t - board.CentreY;
            return Inside(board.Outline, x, y) || DistanceToOutline(board.Outline, x, y) <= Tolerance;
        }

        /// <summary>The even-odd test of a point against a polygon.</summary>
        public static bool Inside(IList<Point2> polygon, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Point2 a = polygon[i], b = polygon[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }

            return inside;
        }

        public static float DistanceToOutline(IList<Point2> polygon, float x, float y)
        {
            float best = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Point2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                float dx = b.X - a.X, dy = b.Y - a.Y;
                float lengthSquared = dx * dx + dy * dy;
                float u = lengthSquared < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((x - a.X) * dx + (y - a.Y) * dy) / lengthSquared));
                float px = a.X + dx * u - x, py = a.Y + dy * u - y;
                best = Math.Min(best, (float)Math.Sqrt(px * px + py * py));
            }

            return best;
        }
    }
}
