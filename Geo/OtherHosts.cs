using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Boards on another mod's pole or arm go with it (0.12.106; the owner, 7 Nov: "RIE中移动灯杆位置之后竖杆挂上去的路牌不会跟随"): which host of the lists the other mod gives (TsExtension.OtherHosts) a
    /// board is on, and where that host is now. The other mod's number for a host (HostCandidate.Id, HostArm.Id) is what keeps it known as it moves; the same number is a pole's post and its arm, told
    /// apart by the list. Pure.
    /// </summary>
    internal static class OtherHosts
    {
        /// <summary>How far above or below a board a post's foot may be (m): a pole on a slope, not the one on the road under a bridge.</summary>
        public const float FootHeight = 2.5f;

        /// <summary>The post a board stands at the foot of: the nearest within HostPick.Fixed seen from above, and within FootHeight in height (the earlier of two equally near); -1 when none.</summary>
        public static int PostUnder(IList<HostCandidate> posts, Vec3 at)
        {
            int best = -1;
            float nearest = float.MaxValue;
            for (int i = 0; i < posts.Count; i++)
            {
                float dx = posts[i].X - at.X, dz = posts[i].Z - at.Z, d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d <= HostPick.Fixed && d < nearest && Math.Abs(posts[i].Y - at.Y) <= FootHeight)
                {
                    nearest = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>The arm a board is on (HostPick.AlongArm: on its axis, along its length); the nearest to the axis of two; -1 when none.</summary>
        public static int ArmUnder(IList<HostArm> arms, Vec3 at)
        {
            int best = -1;
            float nearest = float.MaxValue;
            for (int i = 0; i < arms.Count; i++)
            {
                // A board that is not on the arm has no place along it (NaN), and a NaN distance is never the nearest.
                float t = HostPick.AlongArm(arms[i], at);
                float px = arms[i].X + arms[i].DirX * t - at.X, pz = arms[i].Z + arms[i].DirZ * t - at.Z, d = px * px + pz * pz;
                if (d < nearest)
                {
                    nearest = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>A post's pose: its foot; its yaw is nothing (a board on a post goes with its place, not with the way the pole is turned).</summary>
        public static Vec3 PostPose(HostCandidate post, out float yaw)
        {
            yaw = 0f;
            return new Vec3(post.X, post.Y, post.Z);
        }

        /// <summary>An arm's pose: its root on the pole's axis at the height of the arm's axis, and the way it reaches (yaw as the game's Transform gives it: atan2 of x over z).</summary>
        public static Vec3 ArmPose(HostArm arm, out float yaw)
        {
            yaw = RoadRelation.HeadingYaw(new Vec3(arm.DirX, 0f, arm.DirZ));
            return new Vec3(arm.X, arm.Y, arm.Z);
        }

        /// <summary>The host known by a number, as the lists have it now (an arm of that number when onArm, else a post): false when the lists have none of it.</summary>
        public static bool Find(IList<HostCandidate> posts, IList<HostArm> arms, bool onArm, int id, out Vec3 at, out float yaw)
        {
            at = default;
            yaw = 0f;
            if (id < 0)
                return false;
            if (onArm)
            {
                for (int i = 0; i < arms.Count; i++)
                {
                    if (arms[i].Id == id)
                    {
                        at = ArmPose(arms[i], out yaw);
                        return at.IsFinite && !float.IsNaN(yaw);
                    }
                }

                return false;
            }

            for (int i = 0; i < posts.Count; i++)
            {
                if (posts[i].Id == id)
                {
                    at = PostPose(posts[i], out yaw);
                    return at.IsFinite;
                }
            }

            return false;
        }

        /// <summary>Whether a host's pose is not the one it was kept at (a hundredth of a metre, or a tenth of a degree): what the follow is told of.</summary>
        public static bool Moved(Vec3 was, float wasYaw, Vec3 now, float nowYaw) =>
            (now - was).Length > 0.01f || Math.Abs(RoadRelation.Wrap(nowYaw - wasYaw)) > 0.0017f;
    }
}
