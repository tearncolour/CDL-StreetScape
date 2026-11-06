using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using Colossal.Entities;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using ObjectTransform = Game.Objects.Transform;

namespace CDLC.TS
{
    /// <summary>
    /// The street-lamp posts and signal poles of the game's, for the attached support (SupportKind.Attached): every object with the game's StreetLight or TrafficLight component and a position, read from
    /// the world (not from the road's sub-object lists, so that the lamps of a junction and of a lane count as much as those along an edge), near a place. Read only; the results are kept while the cursor
    /// stays near the place and for a second, since asking is a walk over every lamp of the city. Posts whose foot is far from the ground at the place are left out: that is a lamp head or a signal head
    /// on an arm, not the post. The first posts that are found are written to the log with their prefab and its size, so that the radius the attached support assumes (SupportLayouts.HostRadius) can be
    /// set from what the game has. Main thread.
    /// </summary>
    internal sealed class HostFinder
    {
        /// <summary>How far above or below the ground at the place a post's foot may be (m): a post on a slope is, a head on an arm is not.</summary>
        private const float kGroundTolerance = 2.5f;

        private const float kKeepMetres = 2f;
        private const float kKeepSeconds = 1f;
        private const int kMaxLogged = 12;

        private readonly EntityManager m_Em;
        private EntityQuery m_Lamps, m_Signals;
        private bool m_Ready;
        private PrefabSystem m_Prefabs;
        private readonly List<HostCandidate> m_Posts = new List<HostCandidate>();

        /// <summary>0.12.51: another mod's poles and arms near the place (TsExtension.OtherHosts), asked with the game's posts and kept as long.</summary>
        private readonly List<HostCandidate> m_Others = new List<HostCandidate>();
        private readonly List<HostArm> m_Arms = new List<HostArm>();
        private float m_X, m_Z, m_Y, m_At = -1000f;
        private int m_Logged;
        private int m_Walks;

        /// <summary>How many lamps and signal poles the last walk went over (for the log and the tests of the tool).</summary>
        internal int LampsSeen { get; private set; }

        internal int SignalsSeen { get; private set; }

        internal HostFinder(EntityManager em)
        {
            m_Em = em;
        }

        private void Prepare()
        {
            if (m_Ready)
                return;
            m_Lamps = m_Em.CreateEntityQuery(ComponentType.ReadOnly<StreetLight>(), ComponentType.ReadOnly<ObjectTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_Signals = m_Em.CreateEntityQuery(ComponentType.ReadOnly<TrafficLight>(), ComponentType.ReadOnly<ObjectTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_Ready = true;
        }

        /// <summary>
        /// The posts within HostPick.Radius (and a little more, so that the list stays good while the cursor moves) of the place, whose foot is at the height of the ground there (groundY). The list is
        /// kept until the place moves 2 m or a second has gone by.
        /// </summary>
        internal IList<HostCandidate> Around(float x, float z, float groundY, World world)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            float dx = x - m_X, dz = z - m_Z;
            if (now - m_At < kKeepSeconds && dx * dx + dz * dz < kKeepMetres * kKeepMetres && Math.Abs(groundY - m_Y) < 1f)
                return m_Posts;

            Prepare();
            m_Prefabs = m_Prefabs ?? world?.GetOrCreateSystemManaged<PrefabSystem>();
            m_X = x;
            m_Z = z;
            m_Y = groundY;
            m_At = now;
            m_Posts.Clear();
            float radius = HostPick.Radius + 3f;
            LampsSeen = Collect(m_Lamps, HostCandidate.Lamp, x, z, groundY, radius);
            SignalsSeen = Collect(m_Signals, HostCandidate.Signal, x, z, groundY, radius);
            m_Others.Clear();
            m_Arms.Clear();
            try
            {
                TsExtension.AskHosts(x, z, radius, m_Others, m_Arms);
            }
            catch (Exception ex)
            {
                m_Others.Clear();
                m_Arms.Clear();
                Mod.log.Warn("Attached: the extension's poles and arms were not read: " + ex.Message);
            }

            // Its poles as signal poles whose foot is at the ground there; they come after the game's, which keep the tie (HostPick.Nearest takes the earlier).
            foreach (HostCandidate other in m_Others)
            {
                if (Math.Abs(other.Y - groundY) > kGroundTolerance)
                    continue;
                HostCandidate post = other;
                post.Kind = HostCandidate.Signal;
                m_Posts.Add(post);
            }
            if (m_Walks++ < 3 || (m_Posts.Count > 0 && m_Logged < kMaxLogged))
                Log(x, z, groundY);
            return m_Posts;
        }

        private int Collect(EntityQuery query, int kind, float x, float z, float groundY, float radius)
        {
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            using (NativeArray<ObjectTransform> transforms = query.ToComponentDataArray<ObjectTransform>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Unity.Mathematics.float3 p = transforms[i].m_Position;
                    float dx = p.x - x, dz = p.z - z;
                    if (dx * dx + dz * dz > radius * radius || Math.Abs(p.y - groundY) > kGroundTolerance)
                        continue;
                    m_Posts.Add(new HostCandidate { X = p.x, Y = p.y, Z = p.z, Kind = kind, Id = entities[i].Index });
                }

                return entities.Length;
            }
        }

        /// <summary>Another mod's signal arms near a place (TsExtension.OtherHosts), read with the posts and kept as long as they are.</summary>
        internal IList<HostArm> ArmsAround(float x, float z, float groundY, World world)
        {
            Around(x, z, groundY, world);
            return m_Arms;
        }

        private void Log(float x, float z, float groundY)
        {
            if (m_Posts.Count == 0)
            {
                Mod.log.Info($"Attached: no lamp post or signal pole within {HostPick.Radius + 3f:0} m of {x:0.#}, {z:0.#} (ground {groundY:0.#}); {LampsSeen} lamp(s) and {SignalsSeen} signal pole(s) in the city.");
                return;
            }

            if (m_Logged >= kMaxLogged)
                return;
            m_Logged++;
            var lines = new List<string> { $"Attached: {m_Posts.Count} post(s) near {x:0.#}, {z:0.#} (ground {groundY:0.#}); {LampsSeen} lamp(s) and {SignalsSeen} signal pole(s) in the city; {m_Others.Count} pole(s) and {m_Arms.Count} arm(s) of the extension" };
            for (int i = 0; i < m_Posts.Count && i < 4; i++)
                lines.Add("  " + (m_Posts[i].Radius > 0f ? $"the extension's pole #{m_Posts[i].Id} foot {m_Posts[i].X:0.0}, {m_Posts[i].Y:0.0}, {m_Posts[i].Z:0.0}, radius {m_Posts[i].Radius:0.000}, highest {m_Posts[i].Highest:0.00}, {(m_Posts[i].Busy?.Length ?? 0) / 2} head(s)" : Describe(m_Posts[i])));
            Mod.log.Info(string.Join("\n", lines));
        }

        /// <summary>One post for the log: which kind, where, its prefab and the size the prefab says (the whole object, with its arm and head).</summary>
        private string Describe(HostCandidate post)
        {
            string text = $"{HostPick.Name(post.Kind)} #{post.Id} foot {post.X:0.0}, {post.Y:0.0}, {post.Z:0.0}";
            try
            {
                Entity entity = FindEntity(post);
                if (entity != Entity.Null && m_Em.TryGetComponent(entity, out PrefabRef prefab))
                {
                    string name = m_Prefabs != null ? m_Prefabs.GetPrefabName(prefab.m_Prefab) : "#" + prefab.m_Prefab.Index;
                    if (m_Em.TryGetComponent(prefab.m_Prefab, out ObjectGeometryData geometry))
                        text += $", prefab \"{name}\", size {geometry.m_Size.x:0.##} x {geometry.m_Size.y:0.##} x {geometry.m_Size.z:0.##}, bounds x {geometry.m_Bounds.min.x:0.##}..{geometry.m_Bounds.max.x:0.##} y {geometry.m_Bounds.min.y:0.##}..{geometry.m_Bounds.max.y:0.##} z {geometry.m_Bounds.min.z:0.##}..{geometry.m_Bounds.max.z:0.##}";
                    else
                        text += $", prefab \"{name}\"";
                }
            }
            catch (Exception ex)
            {
                text += " (prefab not read: " + ex.Message + ")";
            }

            return text;
        }

        private Entity FindEntity(HostCandidate post)
        {
            EntityQuery query = post.Kind == HostCandidate.Signal ? m_Signals : m_Lamps;
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (entities[i].Index == post.Id)
                        return entities[i];
                }
            }

            return Entity.Null;
        }
    }
}
