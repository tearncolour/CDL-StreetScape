using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Render
{
    /// <summary>A mesh of one material of a tile, ready to be made into a Unity mesh.</summary>
    internal sealed class PreparedPart
    {
        public string Material;
        public PreparedMesh Mesh;
    }

    /// <summary>What a worker made of a tile: its parts, which tier and which version of the tile's items they are of, or what went wrong.</summary>
    internal sealed class TileResult
    {
        public TileKey Key;
        public TileTier Tier;

        /// <summary>The version of the tile's items the worker read (ItemStore.Snapshot): a result whose version is not the tile's now is out of date.</summary>
        public int Version;

        /// <summary>The generation of the renderer the job was started in (a city that was left, an item set that was dropped): a result of an older one is thrown away.</summary>
        public int Epoch;

        public List<PreparedPart> Parts;
        public int Skipped;
        public int Items;
        public int Vertices;
        public double Ms;
        public string Error;
    }

    /// <summary>
    /// Makes the meshes of a tile on a worker thread (0.7.0): the tile's items are read from a copy, the supports and the boards are built (they are kept by the caches, which are locked), the
    /// meshes of the parts are put together and turned into the arrays Unity takes. Nothing here touches a Unity object; the main thread makes the meshes, the renderers and the materials from the
    /// result, a little each frame. Until 0.6.1 all of it was done on the main thread, tile by tile, so that one tile of 70 signs took 350 ms.
    /// </summary>
    internal static class TileWorker
    {
        /// <summary>A mesh is cut at this many vertices: the main thread makes one mesh in about a millisecond at this size.</summary>
        internal const int MaxVertices = 30000;

        /// <summary>A running average of the build time of a tile (ms), for the follow's gate: written by the workers, read by the main thread; a torn or stale read is harmless.</summary>
        internal static volatile float RecentBuildMsStore;

        internal static double RecentBuildMs
        {
            get => RecentBuildMsStore;
            private set => RecentBuildMsStore = (float)value;
        }

        internal static Task<TileResult> Start(TileKey key, TileTier tier, int version, int epoch, List<ItemRecord> items) =>
            Task.Run(() => Build(key, tier, version, epoch, items));

        /// <summary>The work of the job; public to the assembly so that it can be run on the calling thread (a probe, a test) as well.</summary>
        internal static TileResult Build(TileKey key, TileTier tier, int version, int epoch, List<ItemRecord> items)
        {
            var clock = Stopwatch.StartNew();
            var result = new TileResult { Key = key, Tier = tier, Version = version, Epoch = epoch, Items = items.Count, Parts = new List<PreparedPart>() };
            try
            {
                List<ChunkPart> parts = ChunkBuilder.BuildDetailed(items, Tiles.Centre(key), ChunkDetail.Of(tier), out int skipped, MaxVertices);
                result.Skipped = skipped;
                foreach (ChunkPart part in parts)
                {
                    if (part.Mesh.VertexCount == 0)
                        continue;
                    result.Parts.Add(new PreparedPart { Material = part.Material, Mesh = MeshUpload.Prepare(part.Mesh) });
                    result.Vertices += part.Mesh.VertexCount;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.ToString();
            }

            result.Ms = clock.Elapsed.TotalMilliseconds;
            RecentBuildMs = RecentBuildMs * 0.8 + result.Ms * 0.2;
            return result;
        }
    }
}
