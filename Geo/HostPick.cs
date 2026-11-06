using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>A street-lamp post or a signal pole of the game's, or another mod's pole (TsExtension.OtherHosts): where its foot is, and which of the two it is.</summary>
    public struct HostCandidate
    {
        public const int Lamp = 1, Signal = 2;

        public float X, Y, Z;
        public int Kind;

        /// <summary>The game's entity index of the post (for the log and for telling two posts at the same place apart), -1 when not known.</summary>
        public int Id;

        /// <summary>
        /// 0.12.51, another mod's pole (0 and null: a post of the game's, as before): its radius where the boards go (m; SupportLayouts.HostRadius otherwise), the highest the top of a board may reach
        /// (m over its foot; Assembly.HighestOnHost and the board's height otherwise), and the heights its own heads take (lo, hi, lo, hi... in m over its foot, with their room) that no board enters.
        /// </summary>
        public float Radius, Highest;

        public float[] Busy;
    }

    /// <summary>
    /// The arm of another mod's signal pole (0.12.51, TsExtension.OtherHosts) that boards may be clamped to (SupportKind.AttachedArm): its root on the pole's axis (X, Z; Y the height of the arm's axis,
    /// world), the way it reaches (DirX, DirZ, level and of length 1), how long it is from the root to its tip (m), its radius at the root and at the tip (0: as at the root), the way the heads on it
    /// face (FaceX, FaceZ: a board faces so too and stands on that side), the stretches along it where boards may go (from, to, from, to... in m from the root, clear of the pole, the braces and the
    /// heads, and the room each stretch has above and below the arm's axis (Room)), and the other mod's number for it.
    /// </summary>
    public struct HostArm
    {
        public float X, Y, Z, DirX, DirZ, Length, Radius, TipRadius, FaceX, FaceZ;
        public float[] Free;

        /// <summary>
        /// For each stretch of Free (the same pairs, as the other mod gave them: not cut by the boards already on the arm), how far below and above the arm's axis a board may reach there (m): Room[i] for
        /// the stretch from Free[i], the room below (the board's lower edge no lower than that under the axis), Room[i + 1] the room above (its upper edge no higher). Null from a version of the other mod
        /// that gives none: HostPick.DefaultBelow and DefaultAbove. Written by the other mod by name (RIE 0.2.12, PoleHosts.RoomOf).
        /// </summary>
        public float[] Room;
        public int Id;

        /// <summary>The radius of the arm at a distance from its root (tapered from Radius to TipRadius).</summary>
        public float RadiusAt(float along) => TipRadius > 0f && Length > 0f ? Radius + (TipRadius - Radius) * System.Math.Max(0f, System.Math.Min(1f, along / Length)) : Radius;
    }

    /// <summary>Which post an attached sign (SupportKind.Attached) is fixed to: the nearest one to the cursor within a radius. Pure, so that the choice is tested.</summary>
    public static class HostPick
    {
        /// <summary>How far from the cursor a post is looked for when a sign is put on one (m): the post that is nearest to the cursor within this is the host.</summary>
        public const float Radius = 8f;

        /// <summary>How far from a placed attached sign its post may be (m) for the sign to count as fixed to it: the sign stands at the foot of its host.</summary>
        public const float Fixed = 1.5f;

        /// <summary>
        /// The index of the post nearest to (x, z) within radius, -1 when there is none. The height does not count (the cursor is on the ground and the foot of a post is where the ground is). Of two
        /// posts equally near the earlier one is taken, so that the choice does not flicker as the list is read in another order.
        /// </summary>
        public static int Nearest(IList<HostCandidate> posts, float x, float z, float radius, out float distance)
        {
            distance = float.MaxValue;
            int best = -1;
            if (posts == null)
                return best;
            for (int i = 0; i < posts.Count; i++)
            {
                float dx = posts[i].X - x, dz = posts[i].Z - z;
                float d = (float)System.Math.Sqrt(dx * dx + dz * dz);
                if (d <= radius && d < distance)
                {
                    distance = d;
                    best = i;
                }
            }

            if (best < 0)
                distance = float.MaxValue;
            return best;
        }

        /// <summary>The kind as the hint names it: "lamp", "signal", or "".</summary>
        public static string Name(int kind) => kind == HostCandidate.Lamp ? "lamp" : kind == HostCandidate.Signal ? "signal" : "";

        /// <summary>
        /// Where a board of a height goes on a post (0.12.51): its lower edge wanted at lower, kept between lowest and the post's Highest less its height, and out of the heights its heads take (Busy):
        /// the nearest place that is free, below a head first when one is as near as the other. NaN when the post has no room for it.
        /// </summary>
        public static float FitHeight(HostCandidate post, float lower, float height, float lowest, float highest)
        {
            float top = post.Highest > 0f ? Math.Min(highest + height, post.Highest) : highest + height;
            var free = new List<(float Lo, float Hi)> { (lowest, top) };
            if (post.Busy != null)
            {
                for (int i = 0; i + 1 < post.Busy.Length; i += 2)
                {
                    float lo = Math.Min(post.Busy[i], post.Busy[i + 1]), hi = Math.Max(post.Busy[i], post.Busy[i + 1]);
                    var next = new List<(float Lo, float Hi)>();
                    foreach ((float a, float b) in free)
                    {
                        if (hi <= a || lo >= b)
                        {
                            next.Add((a, b));
                            continue;
                        }

                        if (lo > a)
                            next.Add((a, lo));
                        if (hi < b)
                            next.Add((hi, b));
                    }

                    free = next;
                }
            }

            float best = float.NaN, distance = float.MaxValue;
            foreach ((float a, float b) in free)
            {
                if (b - a < height - 1e-4f)
                    continue;
                float at = Math.Max(a, Math.Min(b - height, lower));
                float d = Math.Abs(at - lower);
                if (d < distance - 1e-4f || (Math.Abs(d - distance) <= 1e-4f && at < best))
                {
                    distance = d;
                    best = at;
                }
            }

            return best;
        }

        /// <summary>How far an arm may be from the cursor (m, in the ground plane) to be the one a board goes on, and how near a placed board must be to its arm to count as clamped to it.</summary>
        public const float ArmReach = 3f, ArmFixed = 0.5f;

        /// <summary>How far above or below an arm's axis a placed board's middle may be (m) for the board to be on that arm.</summary>
        public const float ArmHeightFixed = 0.3f;

        /// <summary>Where along the arm (m from its root) a placed board's middle is, when it is on that arm (within ArmFixed of its axis seen from above and ArmHeightFixed of it in height); NaN when it is not.</summary>
        public static float AlongArm(HostArm arm, Vec3 at)
        {
            if (Math.Abs(at.Y - arm.Y) > ArmHeightFixed)
                return float.NaN;
            float t = (at.X - arm.X) * arm.DirX + (at.Z - arm.Z) * arm.DirZ;
            float px = arm.X + arm.DirX * t - at.X, pz = arm.Z + arm.DirZ * t - at.Z;
            return t >= -ArmFixed && t <= arm.Length + ArmFixed && px * px + pz * pz <= ArmFixed * ArmFixed ? t : float.NaN;
        }

        /// <summary>The room below and above an arm's axis (m) where the other mod gives none (its Room is null): what a head's depth under an arm and a board's top over it allow in the plainest case.</summary>
        public const float DefaultBelow = 0.5f, DefaultAbove = 0.3f;

        /// <summary>The room below and above an arm's axis at a place along it (m from the root): that of the stretch of Free that holds it, else of the nearest one; the defaults without Room.</summary>
        public static void RoomAt(HostArm arm, float along, out float below, out float above)
        {
            below = DefaultBelow;
            above = DefaultAbove;
            float[] free = arm.Free;
            if (arm.Room == null || free == null || arm.Room.Length != free.Length)
                return;
            float distance = float.MaxValue;
            for (int i = 0; i + 1 < free.Length; i += 2)
            {
                float from = Math.Min(free[i], free[i + 1]), to = Math.Max(free[i], free[i + 1]);
                float d = along < from ? from - along : along > to ? along - to : 0f;
                if (d < distance)
                {
                    distance = d;
                    below = arm.Room[i];
                    above = arm.Room[i + 1];
                }
            }
        }

        /// <summary>
        /// How far (m, + up) a board may be moved from where it hangs by default (its upper part over the arm: SupportLayouts.ArmInset) for it to stay within the room below and above the arm's axis: from
        /// the lowest, where its lower edge is at the room's bottom, to the highest, where its top (with a plate on it) is at the room's top. Never less than 0 either way: where the board does not
        /// fit the room even where it hangs (a tall board under an arm with its heads close), it stays there and cannot be moved the other way.
        /// </summary>
        public static void DropRange(float boardHeight, float extraTop, float below, float above, out float lowest, out float highest)
        {
            float inset = SupportLayouts.ArmInset(boardHeight);
            lowest = Math.Min(0f, boardHeight - inset - below);
            highest = Math.Max(0f, above - inset - extraTop);
        }

        /// <summary>
        /// A board of a width moved along an arm (m from its root) from where its middle is: the nearest place that is free (HostArm.Free, with the stretches the other boards take left out by the caller)
        /// to from + direction * step in the direction of the move, the nearest edge of a stretch further on when its own is no further than it is. NaN when there is nowhere further in that direction.
        /// </summary>
        public static float StepOnArm(HostArm arm, float from, float width, int direction, float step)
        {
            float[] free = arm.Free ?? new[] { 0.1f, arm.Length - 0.1f };
            float target = from + direction * step, best = float.NaN, distance = float.MaxValue;
            for (int i = 0; i + 1 < free.Length; i += 2)
            {
                float lo = Math.Min(free[i], free[i + 1]) + width * 0.5f, hi = Math.Max(free[i], free[i + 1]) - width * 0.5f;
                if (hi < lo - 1e-4f)
                    continue;
                float at = Math.Max(lo, Math.Min(hi, target));
                if ((at - from) * direction <= 1e-4f)
                {
                    // Its own stretch ends here: the one further on, from its near edge.
                    float edge = direction > 0 ? lo : hi;
                    if ((edge - from) * direction <= 1e-4f)
                        continue;
                    at = edge;
                }

                float d = Math.Abs(at - target);
                if (d < distance)
                {
                    distance = d;
                    best = at;
                }
            }

            return best;
        }

        /// <summary>
        /// The arm nearest to (x, z) within reach (its axis seen from above, from the root to the tip), with how far along it (m from the root) the point nearest to (x, z) is; -1 when there is none. Of
        /// two arms as near the earlier one.
        /// </summary>
        public static int NearestArm(IList<HostArm> arms, float x, float z, float reach, out float along, out float distance)
        {
            along = 0f;
            distance = float.MaxValue;
            int best = -1;
            if (arms == null)
                return best;
            for (int i = 0; i < arms.Count; i++)
            {
                HostArm arm = arms[i];
                if (!(arm.Length > 0f))
                    continue;
                float t = Math.Max(0f, Math.Min(arm.Length, (x - arm.X) * arm.DirX + (z - arm.Z) * arm.DirZ));
                float px = arm.X + arm.DirX * t - x, pz = arm.Z + arm.DirZ * t - z;
                float d = (float)Math.Sqrt(px * px + pz * pz);
                if (d <= reach && d < distance)
                {
                    distance = d;
                    along = t;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>The stretches of free (from, to, from, to...) that the taken ones (from, to) leave.</summary>
        public static float[] WithoutTaken(float[] free, IList<(float From, float To)> taken)
        {
            var left = new List<(float From, float To)>();
            for (int i = 0; i + 1 < free.Length; i += 2)
                left.Add((Math.Min(free[i], free[i + 1]), Math.Max(free[i], free[i + 1])));
            foreach ((float from, float to) in taken)
            {
                var next = new List<(float From, float To)>();
                foreach ((float a, float b) in left)
                {
                    if (to <= a || from >= b)
                    {
                        next.Add((a, b));
                        continue;
                    }

                    if (from > a)
                        next.Add((a, from));
                    if (to < b)
                        next.Add((to, b));
                }

                left = next;
            }

            var result = new float[left.Count * 2];
            for (int i = 0; i < left.Count; i++)
            {
                result[2 * i] = left[i].From;
                result[2 * i + 1] = left[i].To;
            }

            return result;
        }

        /// <summary>
        /// Where along an arm (m from its root) the middle of a board of a width goes: as near as can be to wanted, in a free stretch (HostArm.Free) that holds the board whole, the one nearest to wanted;
        /// the arm's own length when it lists none (from 0.1 m to 0.1 m short of its tip). NaN when no stretch holds it.
        /// </summary>
        public static float PlaceOnArm(HostArm arm, float wanted, float width)
        {
            float[] free = arm.Free ?? new[] { 0.1f, arm.Length - 0.1f };
            float best = float.NaN, distance = float.MaxValue;
            for (int i = 0; i + 1 < free.Length; i += 2)
            {
                float from = Math.Min(free[i], free[i + 1]) + width * 0.5f, to = Math.Max(free[i], free[i + 1]) - width * 0.5f;
                if (to < from - 1e-4f)
                    continue;
                float at = Math.Max(from, Math.Min(to, wanted));
                if (Math.Abs(at - wanted) < distance)
                {
                    distance = Math.Abs(at - wanted);
                    best = at;
                }
            }

            return best;
        }
    }
}
