using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// The ground under the posts of the signs (0.12.5, PostGround; the owner: "立柱自动接地"): for every sign on posts of its own (not on a wall or a host), the ground along the line its posts stand on
    /// (GroundProfile, the item's local X) from the surfaces a post may stand on (SurfaceReader with standable: the carriageway, a footway, a median, the deck the sign is on) and else the terrain. Made the
    /// way the areas' fits are made (a few at a time, again when a road under it changes), and read by the renderer with the item (GroundFor). The profiles of the boards of one support, which stand in one
    /// place, are made once.
    /// </summary>
    internal sealed partial class AreaFitSystem
    {
        private sealed class GroundEntry
        {
            public (float, float, float, float, uint, float) Key;
            public GroundProfile Profile;
            public float MinX, MinZ, MaxX, MaxZ;
            public bool Boxed;
        }

        private readonly Dictionary<Entity, GroundEntry> m_Grounds = new Dictionary<Entity, GroundEntry>();
        private readonly List<Entity> m_GroundQueue = new List<Entity>();
        private readonly HashSet<Entity> m_GroundQueued = new HashSet<Entity>();
        private readonly Dictionary<(float, float, float, float, uint, float), GroundProfile> m_SameSpot = new Dictionary<(float, float, float, float, uint, float), GroundProfile>();
        private int m_GroundsMade;

        /// <summary>The farthest along the line of the posts (m) a support of the default reach has a post: the posts of a wide board on two or three posts; a cantilever's post is at 0.</summary>
        private const float kPostReach = 12f;

        /// <summary>How far behind the first post (m) the profile starts: the first post stands on the item's origin, and its pad is under a metre wide.</summary>
        private const float kPostBehind = 3f;

        /// <summary>The key of a profile: where the item stands, which way, and how far its posts can be (its support and reach). No allocation: the renderer asks at every read of every sign.</summary>
        private static (float, float, float, float, uint, float) GroundKeyOf(in ItemRecord record) =>
            (record.Position.X, record.Position.Y, record.Position.Z, record.Yaw, record.SupportHash, (float)Math.Round(record.Reach, 1));

        /// <summary>Whether a sign's posts stand on the ground: a support of its own on posts (not a wall, not a host's post). The planner's support (0) is one on posts.</summary>
        private static bool HasPosts(in ItemRecord record)
        {
            SupportDef support = record.SupportHash == 0u ? null : SupportCatalog.ByHash(record.SupportHash);
            return support == null || !(SupportKinds.OnWall(support.Kind) || SupportKinds.Hosted(support.Kind) || SupportKinds.OverRoad(support.Kind));
        }

        /// <summary>The ground profile of a sign as it is now, for the renderer: null for a marking or a sign not on posts, or while it is not made yet (it is then queued).</summary>
        internal GroundProfile GroundFor(Entity entity, in ItemRecord record)
        {
            if (AreaFitRules.IsAreaHash(record.TypeHash) || !HasPosts(record) || IsMarkingHash(record.TypeHash))
            {
                if (m_Grounds.Count > 0)
                    m_Grounds.Remove(entity);
                return null;
            }

            var key = GroundKeyOf(record);
            if (m_Grounds.TryGetValue(entity, out GroundEntry entry) && entry.Key.Equals(key))
            {
                if (entry.Profile == null)
                    QueueGround(entity);
                return entry.Profile;
            }

            if (entry == null)
                m_Grounds[entity] = entry = new GroundEntry();
            entry.Key = key;
            entry.Profile = null;
            // Another board of the same support (or a sign put back where one stood) has it already.
            if (m_SameSpot.TryGetValue(key, out GroundProfile known))
            {
                entry.Profile = known;
                return known;
            }

            QueueGround(entity);
            return null;
        }

        private readonly Dictionary<uint, bool> m_MarkingHash = new Dictionary<uint, bool>();

        private bool IsMarkingHash(uint hash)
        {
            if (!m_MarkingHash.TryGetValue(hash, out bool marking))
            {
                SignDef def = SignCatalog.ByHash(hash);
                m_MarkingHash[hash] = marking = def == null || def.IsMarking;
            }

            return marking;
        }

        private void QueueGround(Entity entity)
        {
            if (!m_GroundWaiting.Contains(entity) && m_GroundQueued.Add(entity))
                m_GroundQueue.Add(entity);
        }

        private void MakeGround(Entity entity)
        {
            if (!m_Grounds.TryGetValue(entity, out GroundEntry entry))
                return;
            if (!EntityManager.Exists(entity) || !EntityManager.TryGetComponent(entity, out TsItem item))
            {
                m_Grounds.Remove(entity);
                return;
            }

            ItemRecord record = ChunkSystem.ToRecord(item);
            var key = GroundKeyOf(record);
            if (!m_SameSpot.TryGetValue(key, out GroundProfile profile) || profile == null)
            {
                long lap = System.Diagnostics.Stopwatch.GetTimestamp();
                profile = ComputeGround(EntityManager, m_NetSearch, m_Prefabs, m_Terrain, record.Position, record.Yaw, record.Reach, out _, ShadowObserver);
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - lap) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                SlowLog.Add("AreaFitSystem: one post ground", ms, SlowLog.IsSlow(ms) ? $"at ({record.Position.X:0}, {record.Position.Z:0}), {profile.Note}" : null);
                RememberGround(entity, record, key, profile);
            }

            CompleteGround(entity, entry, record, key, profile);
        }

        /// <summary>A profile that was made is kept for the spot (the other boards of the support have it).</summary>
        private void RememberGround(Entity entity, in ItemRecord record, (float, float, float, float, uint, float) key, GroundProfile profile)
        {
            if (m_SameSpot.Count > 20000)
                m_SameSpot.Clear();
            m_SameSpot[key] = profile;
            m_GroundsMade++;
            if (m_GroundsMade <= 20)
                Mod.log.Info($"Post ground: item {entity.Index}:{entity.Version} at ({record.Position.X:0.0}, {record.Position.Y:0.0}, {record.Position.Z:0.0}): {profile.Note}.");
        }

        /// <summary>The sign has its profile: kept for it, and the renderer is told to read it again.</summary>
        private void CompleteGround(Entity entity, GroundEntry entry, in ItemRecord record, (float, float, float, float, uint, float) key, GroundProfile profile)
        {
            entry.Key = key;
            entry.Profile = profile;
            float c = (float)Math.Cos(record.Yaw), s = (float)Math.Sin(record.Yaw);
            float x0 = -kPostBehind, x1 = Math.Max(kPostReach, record.Reach + 4f);
            Vec3 a = new Vec3(record.Position.X + x0 * c, 0f, record.Position.Z - x0 * s), b = new Vec3(record.Position.X + x1 * c, 0f, record.Position.Z - x1 * s);
            entry.MinX = Math.Min(a.X, b.X) - 1f;
            entry.MaxX = Math.Max(a.X, b.X) + 1f;
            entry.MinZ = Math.Min(a.Z, b.Z) - 1f;
            entry.MaxZ = Math.Max(a.Z, b.Z) + 1f;
            entry.Boxed = true;
            m_Ready.Add(entity);
        }

        /// <summary>
        /// The ground along the line of a sign's posts (also for the tool's preview): its local X from -kPostBehind to the reach and 4 m more (a gantry's far post; 12 m at least), every GroundProfile.Step, in the rows
        /// of GroundProfile; at each place PostGround.GroundOf of the surfaces a post may stand on and of the terrain. The box (world) it covers is given back in the note.
        /// </summary>
        internal static GroundProfile ComputeGround(EntityManager em, Game.Net.SearchSystem search, PrefabSystem prefabs, TerrainSystem terrain, Vec3 position, float yaw, float reach, out string note,
            Action<FitJob, FitResult> observe = null)
        {
            GroundRead ground = ReadGround(em, search, prefabs, position, yaw, reach);
            AreaFit surfaces = null;
            if (ground.Read.Strips.Count > 0)
            {
                FitJob job = GroundJob(ground, position, yaw);
                FitResult cut = FitCut.Run(job);
                SurfaceReader.NoteFitMs(true, cut.CutMs);
                observe?.Invoke(job, cut);
                surfaces = cut.Fit;
            }
            else
            {
                SurfaceReader.NoteFitMs(true, 0.0);
            }

            return FinishGround(terrain, ground, surfaces, position, yaw, out note);
        }

        /// <summary>What a ground profile is made from, read from the game on the main thread: the bands a post may stand on along its line.</summary>
        private sealed class GroundRead
        {
            public float X0, X1;
            public float MinX, MinZ, MaxX, MaxZ;
            public SurfaceReader.Read Read;
        }

        private static GroundRead ReadGround(EntityManager em, Game.Net.SearchSystem search, PrefabSystem prefabs, Vec3 position, float yaw, float reach)
        {
            float x0 = -kPostBehind, x1 = Math.Max(kPostReach, reach + 4f);
            float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (float x in new[] { x0, x1 })
            {
                foreach (float z in new[] { -GroundProfile.RowZ, GroundProfile.RowZ })
                {
                    float wx = position.X + x * c + z * s, wz = position.Z - x * s + z * c;
                    minX = Math.Min(minX, wx);
                    maxX = Math.Max(maxX, wx);
                    minZ = Math.Min(minZ, wz);
                    maxZ = Math.Max(maxZ, wz);
                }
            }

            return new GroundRead { X0 = x0, X1 = x1, MinX = minX, MinZ = minZ, MaxX = maxX, MaxZ = maxZ, Read = SurfaceReader.Around(em, search, prefabs, minX, minZ, maxX, maxZ, true) };
        }

        private static FitJob GroundJob(GroundRead ground, Vec3 position, float yaw) => new FitJob
        {
            Heights = true,
            Strips = ground.Read.Strips,
            Origin = position,
            Yaw = yaw,
            MinX = ground.X0,
            MaxX = ground.X1,
            MinZ = -GroundProfile.RowZ,
            MaxZ = GroundProfile.RowZ,
            Margin = 1f
        };

        /// <summary>The profile from the cut surfaces (null: none under the line) and the terrain. Main thread: the terrain.</summary>
        private static GroundProfile FinishGround(TerrainSystem terrain, GroundRead ground, AreaFit surfaces, Vec3 position, float yaw, out string note)
        {
            float x0 = ground.X0, x1 = ground.X1;
            float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            bool originElevated = false;
            if (surfaces != null)
                surfaces.HeightAt(0f, 0f, out _, out originElevated);
            TerrainHeightData data = TerrainWait.HeightData(terrain);
            var seen = new HashSet<int>();
            var found = new List<int>();
            int onSurface = 0, onTerrain = 0, unknown = 0;
            GroundProfile profile = GroundProfile.Sample((x, z) =>
            {
                bool on = false;
                float y = float.NaN;
                if (surfaces != null)
                    on = surfaces.HeightAt(x, z, out y, out _, seen, found);
                float terrainY = float.NaN;
                if (data.isCreated)
                {
                    float wx = position.X + x * c + z * s, wz = position.Z - x * s + z * c;
                    terrainY = TerrainUtils.SampleHeight(ref data, new float3(wx, position.Y, wz)) - position.Y;
                }

                float g = PostGround.GroundOf(on, y, originElevated, terrainY);
                if (float.IsNaN(g))
                    unknown++;
                else if (on && g == y)
                    onSurface++;
                else
                    onTerrain++;
                return g;
            }, x0, x1);
            note = $"{profile.Count} x 3 place(s) from {x0:0} to {x1:0} m: {onSurface} on a road's surface, {onTerrain} on the terrain, {unknown} not followed{(originElevated ? " (the sign is on an elevated road)" : "")}";
            profile.Note = note;
            return profile;
        }
    }
}
