using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Putting a sign on a building's wall (0.12.2, SupportKind.WallFlat and WallBracket): what the game's ray tells about the wall, the point it hit and the normal of the surface there (the game's
    /// raycast gives the normal of the triangle of the building's mesh it hit, as its own wall placement uses), made into the place, the facing and the height of the sign. A board flat on the wall
    /// faces out of it (+Z of the sign's frame is the normal); a board on a bracket faces along the wall, the arm going out of it along the sign's X (SupportDef.Side says to which side). The origin of
    /// the sign is the point on the wall at the height of the ground there; the height of the lower edge above it is kept as the item's reach. Pure, so that it is checked offline.
    /// </summary>
    public static class WallMount
    {
        /// <summary>How far off the vertical a surface may lean and still be a wall: the largest up or down part of its normal (about 20 degrees).</summary>
        public const float MaxNormalY = 0.35f;

        /// <summary>How near to the standard height (the sign's clearance) the cursor must put a board for it to take that height, and the lowest and the least highest a lower edge may be (m above the foot of the wall: a tall building allows more, WallHeight.Highest).</summary>
        public const float SnapRange = 0.6f, MinHeight = 0.2f, MaxHeight = 60f;

        /// <summary>
        /// The outward normal of a wall, level: the hit's normal turned toward the viewer (the ray comes from the viewer; a back face's normal points away), with its up or down part taken out. False for a
        /// surface that is not a wall (a roof, a floor, a slope) or for no normal at all.
        /// </summary>
        public static bool TryNormal(Vec3 hitNormal, Vec3 rayDirection, out Vec3 outward)
        {
            outward = Vec3.Zero;
            float length = hitNormal.Length;
            if (!(length > 1e-4f) || !hitNormal.IsFinite)
                return false;
            Vec3 n = hitNormal * (1f / length);
            if (Math.Abs(n.Y) > MaxNormalY)
                return false;
            if (Vec3.Dot(n, rayDirection) > 0f)
                n = n * -1f;
            float level = n.LengthXZ;
            if (level < 1e-4f)
                return false;
            outward = new Vec3(n.X / level, 0f, n.Z / level);
            return true;
        }

        /// <summary>
        /// The height of a board's lower edge above the foot of the wall when the cursor is at middle (m above the foot) and the board is boardHeight high: its middle at the cursor; within SnapRange of
        /// the standard height it is that height, else it is rounded to 10 cm; never below MinHeight or above MaxHeight.
        /// </summary>
        public static float LowerEdge(float middle, float boardHeight, float standard) => WallHeight.Limit(Asked(middle, boardHeight, standard), MaxHeight, out _);

        /// <summary>The lower edge the cursor asks for, before the bounds: its middle at the cursor, the standard height within SnapRange of it, else to 10 cm; the standard one for no height.</summary>
        public static float Asked(float middle, float boardHeight, float standard)
        {
            float lower = middle - boardHeight * 0.5f;
            if (float.IsNaN(lower) || float.IsInfinity(lower))
                lower = standard;
            return Math.Abs(lower - standard) <= SnapRange ? standard : (float)Math.Round(lower * 10f) / 10f;
        }

        /// <summary>The yaw of a board flat on the wall: it faces out (the sign's +Z along the normal; +Z is (sin yaw, 0, cos yaw)).</summary>
        public static float FlatYaw(Vec3 normal) => (float)Math.Atan2(normal.X, normal.Z);

        /// <summary>The way along the wall that a bracket's board faces when it is not turned: the normal turned a quarter (the sign's +Z when its +X is the normal).</summary>
        public static Vec3 Along(Vec3 normal) => new Vec3(-normal.Z, 0f, normal.X);

        /// <summary>Which way along the wall a bracket's board faces so that a viewer at viewer sees its face: +1 along Along, -1 the other way.</summary>
        public static int FacingToward(Vec3 normal, Vec3 point, Vec3 viewer) => Vec3.Dot(Along(normal), viewer - point) >= 0f ? 1 : -1;

        /// <summary>
        /// The yaw of a board on a bracket that faces along the wall (facing +1: along Along, -1 the other way) and the side its arm goes to in the sign's frame: the arm always goes out of the wall, so a
        /// board that faces the other way is the mirror (side -1, the arm along -X; the sign's X axis is (cos yaw, 0, -sin yaw)).
        /// </summary>
        public static float BracketYaw(Vec3 normal, int facing, out int side)
        {
            float yaw = (float)Math.Atan2(-normal.Z, normal.X);
            if (facing >= 0)
            {
                side = 1;
                return yaw;
            }

            side = -1;
            return Wrap(yaw + (float)Math.PI);
        }

        /// <summary>The outward normal of the wall a placed wall sign is on, from its yaw: a flat board faces out of the wall; a bracket's arm goes out of it along side times the sign's X.</summary>
        public static Vec3 NormalOf(SupportKind kind, float yaw, int side)
        {
            if (kind != SupportKind.WallBracket)
                return new Vec3((float)Math.Sin(yaw), 0f, (float)Math.Cos(yaw));
            float s = side < 0 ? -1f : 1f;
            return new Vec3(s * (float)Math.Cos(yaw), 0f, -s * (float)Math.Sin(yaw));
        }

        /// <summary>
        /// The yaw of a wall sign that changes its support (flat to bracket, bracket to flat, or the bracket turned to face the other way along the wall: facing -1) on the same wall, and the side of the new
        /// support; the place on the wall stays.
        /// </summary>
        public static float Convert(SupportKind from, int fromSide, float yaw, SupportKind to, int facing, out int side)
        {
            Vec3 normal = NormalOf(from, yaw, fromSide);
            if (to != SupportKind.WallBracket)
            {
                side = 1;
                return FlatYaw(normal);
            }

            return BracketYaw(normal, facing, out side);
        }

        /// <summary>The facing along the wall of a placed bracket: +1 when its board faces along Along of its wall, -1 the other way.</summary>
        public static int FacingOf(float yaw, int side)
        {
            Vec3 normal = NormalOf(SupportKind.WallBracket, yaw, side);
            var face = new Vec3((float)Math.Sin(yaw), 0f, (float)Math.Cos(yaw));
            return Vec3.Dot(face, Along(normal)) >= 0f ? 1 : -1;
        }

        /// <summary>An angle in -pi to pi.</summary>
        public static float Wrap(float angle)
        {
            double a = angle % (2.0 * Math.PI);
            if (a > Math.PI)
                a -= 2.0 * Math.PI;
            if (a < -Math.PI)
                a += 2.0 * Math.PI;
            return (float)a;
        }
    }
}
