using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using UnityEngine;
using UnityEngine.Rendering;

namespace CDLC.TS.Render
{
    /// <summary>A mesh as the arrays Unity takes, made on any thread (plain structs and arrays: no Unity object is made), to be turned into a Mesh on the main thread.</summary>
    internal sealed class PreparedMesh
    {
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector2[] Uvs;

        /// <summary>From the texture coordinates (MeshTangents), for the normal maps of the baked detail (0.12.32).</summary>
        public Vector4[] Tangents;
        public int[] Indices;
        public Bounds Bounds;

        public int VertexCount => Positions.Length;
    }

    /// <summary>Turns a MeshBuilder into a Unity mesh and sets up a renderer the way the other CDL mods' renderers are set up. Prepare may run on a worker; the rest is main thread.</summary>
    internal static class MeshUpload
    {
        /// <summary>The arrays of a mesh, made without touching Unity (so a worker can make them): what 0.6.1 did on the main thread, for every vertex of every part of a tile, in one go.</summary>
        internal static PreparedMesh Prepare(MeshBuilder data)
        {
            int count = data.VertexCount;
            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            var tangents = new Vector4[count];
            float[] t = MeshTangents.Compute(data);
            for (int i = 0; i < count; i++)
            {
                Vec3 p = data.Positions[i];
                Vec3 n = data.Normals[i];
                positions[i] = new Vector3(p.X, p.Y, p.Z);
                normals[i] = new Vector3(n.X, n.Y, n.Z);
                uvs[i] = new Vector2(data.U[i], data.V[i]);
                tangents[i] = new Vector4(t[i * 4], t[i * 4 + 1], t[i * 4 + 2], t[i * 4 + 3]);
            }

            // The bounds are set by hand: a mesh far from its object's origin otherwise gets the wrong ones and is culled while it is on screen.
            var bounds = new Bounds();
            if (count > 0)
            {
                data.Bounds(out Vec3 min, out Vec3 max);
                bounds.SetMinMax(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, max.Y, max.Z));
            }

            return new PreparedMesh { Positions = positions, Normals = normals, Uvs = uvs, Tangents = tangents, Indices = data.Indices.ToArray(), Bounds = bounds };
        }

        internal static Mesh ToMesh(MeshBuilder data, string name, bool keepReadable = false) => ToMesh(Prepare(data), name, keepReadable);

        internal static Mesh ToMesh(PreparedMesh data, string name, bool keepReadable = false)
        {
            Mesh mesh = null;
            try
            {
                mesh = new Mesh();
                mesh.name = name;
                mesh.hideFlags = HideFlags.DontSave;
                mesh.indexFormat = data.VertexCount <= Footprint.Index16MaxVertices ? IndexFormat.UInt16 : IndexFormat.UInt32;
                mesh.vertices = data.Positions;
                mesh.normals = data.Normals;
                mesh.uv = data.Uvs;
                mesh.tangents = data.Tangents;
                mesh.SetIndices(data.Indices, MeshTopology.Triangles, 0, false);
                mesh.bounds = data.Bounds;
                if (!keepReadable)
                    mesh.UploadMeshData(true);
                return mesh;
            }
            catch (Exception error)
            {
                CleanupFailed(mesh, null, error);
                throw;
            }
        }

        // Schedule destruction of the GameObject before its independent mesh; if scheduling fails, retain both for retry.
        private sealed class FailedRenderer
        {
            internal Mesh Mesh;
            internal GameObject Object;
            internal bool IsEmpty => Mesh == null && Object == null;

            internal void Release()
            {
                if (Object != null)
                {
                    UnityEngine.Object.Destroy(Object);
                    Object = null;
                }
                if (Mesh != null)
                {
                    UnityEngine.Object.Destroy(Mesh);
                    Mesh = null;
                }
            }
        }

        private static readonly List<FailedRenderer> s_Failed = new List<FailedRenderer>();

        internal static void CleanupFailed(Mesh mesh, GameObject go, Exception allocationError)
        {
            if (mesh == null && go == null)
                return;
            var owned = new FailedRenderer { Mesh = mesh, Object = go };
            try { owned.Release(); }
            catch (Exception cleanupError)
            {
                if (!owned.IsEmpty)
                    s_Failed.Add(owned);
                throw new AggregateException("Renderer allocation and cleanup failed.", allocationError, cleanupError);
            }
        }

        internal static void RetryCleanup()
        {
            List<Exception> failures = null;
            for (int i = s_Failed.Count - 1; i >= 0; i--)
            {
                FailedRenderer owned = s_Failed[i];
                try { owned.Release(); }
                catch (Exception error) { (failures ?? (failures = new List<Exception>())).Add(error); }
                if (owned.IsEmpty)
                    s_Failed.RemoveAt(i);
            }
            if (failures != null)
                throw new AggregateException("Failed renderer cleanup is still pending.", failures);
        }

        /// <summary>Meshes are independent Unity objects: destroying their GameObjects does not release them, including while a tile is only partly made.</summary>
        internal static void DestroyAll(List<GameObject> objects)
        {
            foreach (GameObject go in objects)
            {
                if (go == null)
                    continue;
                var filter = go.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    UnityEngine.Object.Destroy(filter.sharedMesh);
                UnityEngine.Object.Destroy(go);
            }
        }

        /// <summary>
        /// The rendering layer mask of every renderer made here: the game's batches have every bit set, here the decal layers (bits 8 to 15, which HDRP/Lit takes its decal layers from) are
        /// DecalPolicy's, unless the experiment file (TsMaterials) says otherwise.
        /// </summary>
        internal static uint RenderingLayer = DecalPolicy.ObjectRenderingLayer;

        private static readonly List<MeshRenderer> s_Renderers = new List<MeshRenderer>();
        private static int s_Made, s_PruneAt = 4096;
        private static uint? s_LoggedLayer;

        /// <summary>How many renderers were made in all: with the count ApplyRenderingLayer reaches it tells a silent zero from a session that made none.</summary>
        internal static int RenderersMade => s_Made;

        internal static MeshRenderer AddRenderer(GameObject go, Mesh mesh, Material material, bool castShadows)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderingLayerMask = RenderingLayer;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            renderer.allowOcclusionWhenDynamic = true;
            // Registered last: a renderer whose setup threw is destroyed by the caller and was never held. Tiles come and go all session, so the destroyed ones are dropped when the list has doubled.
            if (s_Renderers.Count >= s_PruneAt)
            {
                s_Renderers.RemoveAll(r => r == null);
                s_PruneAt = Math.Max(4096, s_Renderers.Count * 2);
            }

            s_Renderers.Add(renderer);
            s_Made++;
            // What a new renderer carries is the experiment's whole point, and nothing else says it: once for each mask the renderers are made with.
            if (s_LoggedLayer != RenderingLayer)
            {
                s_LoggedLayer = RenderingLayer;
                Mod.log.Info($"Materials: renderers are made with the rendering layer mask {DecalMasks.DescribeRendering(RenderingLayer)} from now on ({s_Made} made so far).");
            }

            return renderer;
        }

        /// <summary>What the renderers that exist carry, read from them, for the log: how many there are, how many were made, and each rendering layer mask with its count.</summary>
        internal static string MaskCensus()
        {
            s_Renderers.RemoveAll(r => r == null);
            var counts = new SortedDictionary<uint, int>();
            foreach (MeshRenderer r in s_Renderers)
            {
                uint mask = r.renderingLayerMask;
                counts[mask] = counts.TryGetValue(mask, out int n) ? n + 1 : 1;
            }

            var parts = new List<string>();
            foreach (KeyValuePair<uint, int> pair in counts)
                parts.Add($"{DecalMasks.DescribeRendering(pair.Key)} x{pair.Value}");
            return $"this mod's renderers: {s_Renderers.Count} alive of {s_Made} made; rendering layer masks {(parts.Count == 0 ? "none" : string.Join(", ", parts))}";
        }

        /// <summary>Gives every renderer made here that still exists the current RenderingLayer, and returns how many. Not Unity's object search: it did not find the tiles' objects (0.12.81's experiment reached 0).</summary>
        internal static int ApplyRenderingLayer()
        {
            s_Renderers.RemoveAll(r => r == null);
            foreach (MeshRenderer r in s_Renderers)
                r.renderingLayerMask = RenderingLayer;
            return s_Renderers.Count;
        }
    }
}
