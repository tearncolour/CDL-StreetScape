using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// The attached support (0.6.0, GB 5768.2 4.8.12 d): a board fixed to a street-lamp post or a signal pole of the game's. The post nearest to the cursor within HostPick.Radius is the host (HostFinder
    /// reads them from the game, HostPick chooses); the tool refuses a board that is too large for a post of the game's (what a single post carries, SupportPlanner.FitsHost) and a cursor that is not
    /// on a road (the sign faces the road's traffic), and says which in the line of help. Read only: no game object is changed, and what is saved is the sign's own item, as for any support.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        private HostFinder m_Hosts;

        /// <summary>The posts within the radius of the cursor the last time a place was worked out (for the rings the overlay draws).</summary>
        private readonly List<HostCandidate> m_NearPosts = new List<HostCandidate>();

        private HostFinder Hosts => m_Hosts ?? (m_Hosts = new HostFinder(EntityManager));

        /// <summary>
        /// Whether a placed attached sign has lost its post: no lamp post or signal pole within HostPick.Fixed of where it stands. False for a sign on any other support, and for one whose post is there.
        /// The editor asks when it is built, not every frame.
        /// </summary>
        internal bool HostMissing(Unity.Entities.Entity entity)
        {
            if (entity == Unity.Entities.Entity.Null || !EntityManager.Exists(entity) || !EntityManager.HasComponent<TsItem>(entity))
                return false;
            TsItem item = EntityManager.GetComponentData<TsItem>(entity);
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            SupportKind kind = def == null ? SupportKind.SinglePole : SupportCatalog.For(def, item.SupportHash).Kind;
            if (!SupportKinds.Hosted(kind))
                return false;
            if (SupportKinds.OnArm(kind))
            {
                // On an arm (0.12.51): one whose axis is at the board's middle, at its height, running square to the way the board faces (within 15 degrees).
                var at = new Snap.Vec3(item.Position.x, item.Position.y, item.Position.z);
                float faceX = (float)Math.Sin(item.Yaw), faceZ = (float)Math.Cos(item.Yaw);
                foreach (HostArm arm in Hosts.ArmsAround(item.Position.x, item.Position.z, item.Position.y, World))
                {
                    if (!float.IsNaN(HostPick.AlongArm(arm, at)) && Math.Abs(arm.DirX * faceX + arm.DirZ * faceZ) < 0.26f)
                        return false;
                }

                return true;
            }

            IList<HostCandidate> posts = Hosts.Around(item.Position.x, item.Position.z, item.Position.y, World);
            return HostPick.Nearest(posts, item.Position.x, item.Position.z, HostPick.Fixed, out _) < 0;
        }

        /// <summary>The post a placed attached support stands at (0.12.51: another mod's carries its radius, highest and heads' heights); a blank one for any other support or when none is there.</summary>
        private HostCandidate HostOf(ItemRecord lead)
        {
            SupportDef support = SupportCatalog.ByHash(lead.SupportHash);
            if (support == null || support.Kind != SupportKind.Attached)
                return default;
            IList<HostCandidate> posts = Hosts.Around(lead.Position.X, lead.Position.Z, lead.Position.Y, World);
            int pick = HostPick.Nearest(posts, lead.Position.X, lead.Position.Z, HostPick.Fixed, out _);
            return pick < 0 ? default : posts[pick];
        }

        /// <summary>
        /// Finds the post an attached board is fixed to. Null when one is found (host is it); otherwise the hint kind that says why not: "host-large", "host-road" or "host-none". Fills m_NearPosts with the
        /// posts within the radius, for the overlay.
        /// </summary>
        private int m_HostLargeLogs;
        private string m_HostLargeLast = "";

        private string FindHost(SignDef def, bool onRoad, float3 cursor, out HostCandidate host)
        {
            host = default;
            if (!SupportPlanner.FitsHost(def))
            {
                if (m_HostLargeLogs < 12 && m_HostLargeLast != def.Id)
                {
                    m_HostLargeLogs++;
                    Mod.log.Info($"Host: {def.Id} is {def.Width:0.00} x {def.Height:0.00} m, {SupportPlanner.AreaOf(def):0.00} m2: a pole carries up to {SupportPlanner.SinglePostMaxWidth:0.0} x {SupportPlanner.SinglePostMaxHeight:0.0} m and {SupportPlanner.SinglePostMaxArea:0.0} m2 (the planner would give it {SupportPlanner.Auto(def).Id}).");
                }

                m_HostLargeLast = def.Id;
                return "host-large";
            }

            if (!onRoad)
                return "host-road";

            IList<HostCandidate> posts = Hosts.Around(cursor.x, cursor.z, cursor.y, World);
            for (int i = 0; i < posts.Count; i++)
            {
                float dx = posts[i].X - cursor.x, dz = posts[i].Z - cursor.z;
                if (dx * dx + dz * dz <= HostPick.Radius * HostPick.Radius)
                    m_NearPosts.Add(posts[i]);
            }

            int pick = HostPick.Nearest(posts, cursor.x, cursor.z, HostPick.Radius, out _);
            if (pick < 0)
                return "host-none";
            host = posts[pick];
            return null;
        }
    }
}
