using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Unity.Entities;

namespace CDLC.TS
{
    /// <summary>
    /// Boards on another mod's pole or arm (0.12.106; the owner, 7 Nov: "RIE中移动灯杆位置之后竖杆挂上去的路牌不会跟随"): the pole is not the game's, so it has no Transform event. The board is linked to it by
    /// the other mod's number for it (OtherHosts: HostCandidate.Id, HostArm.Id) and the pole is looked at a few times a second, where it was last seen (the other mod answers by place); when its pose is not
    /// the one kept the follow is told, and the same motion takes the board (FollowWatch.LinkHosted). A pole that cannot be found for a few seconds (taken away, or moved further than kOtherReach in
    /// one look) lets its boards go: they stay where they are, and are looked for a host again.
    /// </summary>
    internal sealed partial class ItemRoadSystem
    {
        private struct OtherHost
        {
            public bool Arm;
            public int Id;
            public Vec3 At;
            public float Yaw;
            public int Missed;
        }

        /// <summary>How often the poles of another mod are looked at (s).</summary>
        private const float kOtherPollSeconds = 0.25f;

        /// <summary>How far from where it was last seen a pole is looked for (m): a pole put elsewhere with the move tool is followed to there.</summary>
        private const float kOtherReach = 40f;

        /// <summary>How many looks in a row may not find a pole before it is let go (kOtherPollSeconds each).</summary>
        private const int kOtherMissedLimit = 12;

        private readonly Dictionary<Entity, OtherHost> m_OtherHosts = new Dictionary<Entity, OtherHost>();
        private readonly Dictionary<Entity, Entity> m_OtherOf = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, float> m_OtherRetry = new Dictionary<Entity, float>();
        private readonly List<HostCandidate> m_OtherPosts = new List<HostCandidate>();
        private readonly List<HostArm> m_OtherArms = new List<HostArm>();
        private readonly List<Entity> m_OtherKeys = new List<Entity>();
        private float m_OtherPollAt;
        private bool m_OtherFailed;

        /// <summary>The follow's key for another mod's host: no entity of the game has a negative index, and a post and an arm of one number are two hosts.</summary>
        private static Entity OtherKey(bool arm, int id) => new Entity { Index = -1 - (id * 2 + (arm ? 1 : 0)), Version = 1 };

        /// <summary>The lists of the other mods' poles and arms round a place, in m_OtherPosts and m_OtherArms; false when they failed (once in the log).</summary>
        private bool AskOtherHosts(float x, float z, float radius)
        {
            m_OtherPosts.Clear();
            m_OtherArms.Clear();
            try
            {
                TsExtension.AskHosts(x, z, radius, m_OtherPosts, m_OtherArms);
                return true;
            }
            catch (Exception ex)
            {
                m_OtherPosts.Clear();
                m_OtherArms.Clear();
                if (!m_OtherFailed)
                    Mod.log.Warn("Follow: the extension's poles and arms were not read: " + ex.Message);
                m_OtherFailed = true;
                return false;
            }
        }

        /// <summary>
        /// The board is fixed to the post it stands at the foot of, or the arm it is on, of another mod's: linked (or found linked) and true; false when there is none, and the game's own posts are looked at.
        /// Not asked again for a minute when there is none.
        /// </summary>
        private bool EnsureOtherHostLink(Entity entity, TsItem item, bool arm, float now)
        {
            if (m_OtherOf.ContainsKey(entity) && m_Follow.IsHostLinked(entity))
                return true;
            if (m_OtherRetry.TryGetValue(entity, out float notBefore) && now < notBefore)
                return false;
            var at = new Vec3(item.Position.x, item.Position.y, item.Position.z);
            int pick = -1, id = -1;
            if (AskOtherHosts(at.X, at.Z, HostPick.Fixed + 1f))
            {
                pick = arm ? OtherHosts.ArmUnder(m_OtherArms, at) : OtherHosts.PostUnder(m_OtherPosts, at);
                id = pick < 0 ? -1 : arm ? m_OtherArms[pick].Id : m_OtherPosts[pick].Id;
            }

            if (id < 0)
            {
                m_OtherRetry[entity] = now + kHostRetrySeconds + entity.Index % 16;
                return false;
            }

            Vec3 pose = arm ? OtherHosts.ArmPose(m_OtherArms[pick], out float yaw) : OtherHosts.PostPose(m_OtherPosts[pick], out yaw);
            Entity key = OtherKey(arm, id);
            // The pole as it is now is what the link is made against; boards linked before it moved are told it has.
            bool moved = m_OtherHosts.TryGetValue(key, out OtherHost kept) && OtherHosts.Moved(kept.At, kept.Yaw, pose, yaw);
            if (moved || !m_OtherHosts.ContainsKey(key))
                m_OtherHosts[key] = new OtherHost { Arm = arm, Id = id, At = pose, Yaw = yaw };
            m_OtherRetry.Remove(entity);
            m_OtherOf[entity] = key;
            m_Follow.LinkHosted(entity, key, pose, yaw, at, item.Yaw, unchecked((int)item.Group));
            if (moved)
                m_Follow.Dirty(key);
            return true;
        }

        /// <summary>The poles of another mod that boards are linked to are looked at; the follow is told of those whose pose is not the one kept.</summary>
        private void StepOtherHosts()
        {
            if (m_OtherHosts.Count == 0)
                return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < m_OtherPollAt)
                return;
            m_OtherPollAt = now + kOtherPollSeconds;
            ForgetOtherLinks();
            if (!TsExtension.ProvidesHosts)
                return;
            m_OtherKeys.Clear();
            m_OtherKeys.AddRange(m_OtherHosts.Keys);
            foreach (Entity key in m_OtherKeys)
            {
                OtherHost host = m_OtherHosts[key];
                Vec3 at = default;
                float yaw = 0f;
                bool asked = AskOtherHosts(host.At.X, host.At.Z, kOtherReach);
                // A look that failed is not a pole that is gone.
                if (!asked)
                    continue;
                if (!OtherHosts.Find(m_OtherPosts, m_OtherArms, host.Arm, host.Id, out at, out yaw))
                {
                    if (++host.Missed >= kOtherMissedLimit)
                    {
                        m_Follow.EdgeGone(key);
                        m_OtherHosts.Remove(key);
                        Mod.log.Info($"Follow: the pole {host.Id} of another mod has not been seen for {kOtherMissedLimit * kOtherPollSeconds:0} s near ({host.At.X:0.#}, {host.At.Z:0.#}): its boards stay where they are.");
                    }
                    else
                        m_OtherHosts[key] = host;
                    continue;
                }

                host.Missed = 0;
                if (OtherHosts.Moved(host.At, host.Yaw, at, yaw))
                {
                    host.At = at;
                    host.Yaw = yaw;
                    m_OtherHosts[key] = host;
                    m_Follow.Dirty(key);
                }
                else
                    m_OtherHosts[key] = host;
            }
        }

        /// <summary>Boards that are gone, or are no longer linked, are dropped from the list, and the poles nothing is linked to with them.</summary>
        private void ForgetOtherLinks()
        {
            List<Entity> dropped = null;
            foreach (KeyValuePair<Entity, Entity> link in m_OtherOf)
            {
                if (!EntityManager.Exists(link.Key) || !m_Follow.IsHostLinked(link.Key))
                    (dropped ?? (dropped = new List<Entity>())).Add(link.Key);
            }

            if (dropped != null)
            {
                foreach (Entity board in dropped)
                    m_OtherOf.Remove(board);
            }

            m_OtherKeys.Clear();
            foreach (Entity key in m_OtherHosts.Keys)
            {
                if (!m_OtherOf.ContainsValue(key))
                    m_OtherKeys.Add(key);
            }

            foreach (Entity key in m_OtherKeys)
                m_OtherHosts.Remove(key);
        }
    }
}
