using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// Draws the kerbs of the road upgrade's styles (0.12.33, Kerbs, KerbReader): every road segment whose side flags name a style gets its kerb bodies, made again when it or a junction at its end
    /// changes, drawn in tiles of the map (Tiles.Size) with a mesh for each material, like the signs. Nothing is saved: the style is the road's own (Game.Net.Upgraded, which the game saves), the
    /// bodies come from the road as it is. Main thread, in the main loop, within a budget a frame.
    /// </summary>
    internal sealed partial class KerbSystem : GameSystemBase
    {
        private const double kBudgetMs = 2.0;

        private readonly Dictionary<Entity, (TileKey Tile, List<MarkingPart> Parts)> m_Edges = new Dictionary<Entity, (TileKey, List<MarkingPart>)>();
        private readonly Dictionary<TileKey, List<GameObject>> m_Tiles = new Dictionary<TileKey, List<GameObject>>();
        private readonly HashSet<TileKey> m_Dirty = new HashSet<TileKey>();
        private readonly List<Entity> m_Queue = new List<Entity>();
        private readonly HashSet<Entity> m_Queued = new HashSet<Entity>();

        /// <summary>The segments made while a road piece's mesh was not known (the game was still loading it: SurfaceReader takes it as one surface, and finds no kerb there): made again once a piece is known.</summary>
        private readonly ProvisionalLedger<Entity> m_Provisional = new ProvisionalLedger<Entity>();
        private readonly List<Entity> m_Redo = new List<Entity>();
        private EntityQuery m_Styled, m_Changed;
        private GameObject m_Root;
        private bool m_Scan, m_ReportLoad;
        private int m_Made, m_Errors, m_LoadFrames, m_Described;

        /// <summary>The segments described in the log after a load (KerbReader.Describe): the first ones made.</summary>
        private const int kDescribed = 24;
        private double m_LoadMs;

        /// <summary>The compositions of a loaded city are made in its first frames (they are not saved), and its segments with a style come as they are: the load is reported after this many frames.</summary>
        private const int kLoadFrames = 120;

        internal static KerbSystem Instance { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            m_Styled = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Upgraded>(), ComponentType.ReadOnly<Road>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
            m_Changed = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Road>() },
                Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
        }

        [Preserve]
        protected override void OnDestroy()
        {
            Clear();
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        /// <summary>The state of the last city was let go at the end of the deserialization (LoadWarmSystem.Early): the end of the loading does not do it again.</summary>
        private bool m_ResetEarly;

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_ResetEarly = LoadWarmSystem.Early(serializationContext);
            if (m_ResetEarly)
                ResetForLoad(GameMode.Game);
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (m_ResetEarly)
            {
                m_ResetEarly = false;
                return;
            }

            ResetForLoad(mode);
        }

        private void ResetForLoad(GameMode mode)
        {
            Clear();
            m_Scan = m_ReportLoad = mode == GameMode.Game;
            m_LoadMs = 0.0;
            m_LoadFrames = 0;
        }

        /// <summary>The styled roads still to be scanned or made, for the load's hold on the loading screen.</summary>
        internal int FirstUseWaiting => (m_Scan ? 1 : 0) + m_Queue.Count;

        internal string Describe() => $"kerbs: {m_Edges.Count} segment(s) with a kerb style, {m_Tiles.Count} tile(s), {m_Queue.Count} waiting, {m_Made} made";

        [Preserve]
        protected override void OnUpdate()
        {
            if (GameManager.instance.gameMode != GameMode.Game)
                return;
            var clock = Stopwatch.StartNew();
            try
            {
                if (m_Scan)
                {
                    m_Scan = false;
                    using (NativeArray<Entity> styled = m_Styled.ToEntityArray(Allocator.Temp))
                    {
                        foreach (Entity edge in styled)
                            Queue(edge);
                    }
                }

                if (!m_Changed.IsEmptyIgnoreFilter)
                    QueueChanged();
                SurfaceReader.Poll(EntityManager);
                if (m_Provisional.TakeRedo(SurfaceReader.KerbEpoch, m_Redo))
                {
                    foreach (Entity edge in m_Redo)
                        Queue(edge);
                    m_Redo.Clear();
                }

                int index = 0;
                while (index < m_Queue.Count && clock.Elapsed.TotalMilliseconds < LoadBudget.Scaled(kBudgetMs))
                {
                    Entity edge = m_Queue[index++];
                    m_Queued.Remove(edge);
                    Make(edge);
                }

                m_Queue.RemoveRange(0, index);
                if (m_Queue.Count == 0 && m_Dirty.Count > 0)
                    Draw();
                if (m_ReportLoad)
                {
                    m_LoadMs += clock.Elapsed.TotalMilliseconds;
                    if (++m_LoadFrames >= kLoadFrames && m_Queue.Count == 0)
                    {
                        m_ReportLoad = false;
                        Mod.log.Info($"Kerbs after the city was loaded: {Describe()}; the main thread gave them {m_LoadMs:0} ms.");
                    }
                }
            }
            catch (Exception ex)
            {
                if (m_Errors++ < 10)
                    Mod.log.Error("Kerbs failed: " + ex);
                m_Queue.Clear();
                m_Queued.Clear();
            }

            SlowLog.Add("KerbSystem", clock.Elapsed.TotalMilliseconds, null);
        }

        private void Queue(Entity edge)
        {
            if (m_Queued.Add(edge))
                m_Queue.Add(edge);
        }

        /// <summary>A segment that changed or went, and the segments at a junction that changed (their ends there are its parts), are made again.</summary>
        private void QueueChanged()
        {
            using (NativeArray<Entity> changed = m_Changed.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in changed)
                {
                    if (EntityManager.HasComponent<Edge>(e))
                    {
                        if (m_Edges.ContainsKey(e) || KerbReader.Styled(EntityManager, e))
                            Queue(e);
                    }
                    else if (EntityManager.TryGetBuffer(e, true, out DynamicBuffer<ConnectedEdge> edges))
                    {
                        for (int i = 0; i < edges.Length; i++)
                        {
                            Entity edge = edges[i].m_Edge;
                            if (m_Edges.ContainsKey(edge) || KerbReader.Styled(EntityManager, edge))
                                Queue(edge);
                        }
                    }
                }
            }
        }

        private void Make(Entity edge)
        {
            if (m_Edges.TryGetValue(edge, out (TileKey Tile, List<MarkingPart> Parts) old))
            {
                m_Dirty.Add(old.Tile);
                m_Edges.Remove(edge);
            }

            if (!KerbReader.Styled(EntityManager, edge))
                return;
            if (m_Described < kDescribed)
            {
                m_Described++;
                Mod.log.Info("Kerbs: " + KerbReader.Describe(EntityManager, edge));
            }

            int provisional = SurfaceReader.ProvisionalReads;
            List<KerbLine> lines = KerbReader.Lines(EntityManager, edge);
            if (SurfaceReader.ProvisionalReads != provisional)
                m_Provisional.Note(edge);
            if (lines.Count == 0)
                return;
            var plan = new MarkingPlan();
            foreach (KerbLine line in lines)
                plan.Shapes.AddRange(Kerbs.Shapes(line));
            plan.Bake(0f);
            Vec3 middle = lines[0].Foot[lines[0].Foot.Count / 2];
            TileKey tile = Tiles.Of(middle.X, middle.Z);
            m_Edges[edge] = (tile, plan.Parts);
            m_Dirty.Add(tile);
            m_Made++;
        }

        /// <summary>The tiles whose kerbs changed are drawn again: the parts of their segments in a mesh for each material, about the tile's middle.</summary>
        private void Draw()
        {
            EnsureRoot();
            var byTile = new Dictionary<TileKey, Dictionary<string, List<MeshBuilder>>>();
            foreach (KeyValuePair<Entity, (TileKey Tile, List<MarkingPart> Parts)> pair in m_Edges)
            {
                if (!m_Dirty.Contains(pair.Value.Tile))
                    continue;
                if (!byTile.TryGetValue(pair.Value.Tile, out Dictionary<string, List<MeshBuilder>> materials))
                    byTile[pair.Value.Tile] = materials = new Dictionary<string, List<MeshBuilder>>();
                Vec3 origin = Tiles.Centre(pair.Value.Tile);
                foreach (MarkingPart part in pair.Value.Parts)
                {
                    if (part.Mesh.VertexCount == 0)
                        continue;
                    string material = ChunkBuilder.PartMaterial(part);
                    if (!materials.TryGetValue(material, out List<MeshBuilder> meshes))
                        materials[material] = meshes = new List<MeshBuilder>();
                    // A mesh is cut before Unity's 16-bit indices run out.
                    if (meshes.Count == 0 || meshes[meshes.Count - 1].VertexCount + part.Mesh.VertexCount > ChunkBuilder.MaxVertices)
                        meshes.Add(new MeshBuilder());
                    meshes[meshes.Count - 1].Append(part.Mesh, origin * -1f, 0f);
                }
            }

            foreach (TileKey tile in m_Dirty)
            {
                if (m_Tiles.TryGetValue(tile, out List<GameObject> objects))
                {
                    MeshUpload.DestroyAll(objects);
                    m_Tiles.Remove(tile);
                }

                if (!byTile.TryGetValue(tile, out Dictionary<string, List<MeshBuilder>> materials))
                    continue;
                var made = new List<GameObject>();
                Vec3 origin = Tiles.Centre(tile);
                foreach (KeyValuePair<string, List<MeshBuilder>> pair in materials)
                {
                    foreach (MeshBuilder builder in pair.Value)
                    {
                        Mesh mesh = null;
                        GameObject go = null;
                        try
                        {
                            mesh = MeshUpload.ToMesh(builder, $"CDLTS kerbs {tile} {pair.Key}");
                            go = new GameObject($"CDLTS kerbs {tile} {pair.Key}") { hideFlags = HideFlags.DontSave, layer = 0 };
                            go.transform.SetParent(m_Root.transform, false);
                            go.transform.position = new Vector3(origin.X, origin.Y, origin.Z);
                            MeshUpload.AddRenderer(go, mesh, TsMaterials.OfBody(pair.Key), true);
                            made.Add(go);
                        }
                        catch (Exception error)
                        {
                            MeshUpload.CleanupFailed(mesh, go, error);
                            throw;
                        }
                    }
                }

                m_Tiles[tile] = made;
            }

            m_Dirty.Clear();
        }

        private void EnsureRoot()
        {
            if (m_Root == null)
                m_Root = new GameObject("CDLTS kerbs") { hideFlags = HideFlags.DontSave };
        }

        private void Clear()
        {
            SurfaceReader.ClearFaces();
            foreach (List<GameObject> objects in m_Tiles.Values)
                MeshUpload.DestroyAll(objects);
            m_Tiles.Clear();
            m_Edges.Clear();
            m_Dirty.Clear();
            m_Queue.Clear();
            m_Queued.Clear();
            m_Provisional.Clear(SurfaceReader.KerbEpoch);
            m_Redo.Clear();
            m_Made = m_Described = 0;
        }
    }
}
