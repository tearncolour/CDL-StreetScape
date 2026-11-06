using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using CDLC.TS.Render;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

internal static partial class Program
{
    private static PreparedMesh Triangle(int count = 3)
    {
        var data = new PreparedMesh
        {
            Positions = new Vector3[count], Normals = new Vector3[count], Uvs = new Vector2[count],
            Indices = new[] { 0, 2, 1 }
        };
        data.Positions[0] = new Vector3(-3, 7, 2);
        data.Normals[0] = new Vector3(0, 1, 0);
        data.Uvs[0] = new Vector2(.25f, .75f);
        data.Bounds.SetMinMax(new Vector3(-3, -4, -5), new Vector3(6, 7, 8));
        return data;
    }

#if !MG01_BASELINE
    private static int PendingCleanup => ((ICollection)typeof(MeshUpload).GetField("s_Failed", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Count;
#endif

    private static Exception Error(Action action)
    {
        try { action(); }
        catch (TargetInvocationException ex) { return ex.InnerException; }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("The injected failure was not reported.");
    }

    private static bool Contains(Exception actual, Exception expected) =>
        ReferenceEquals(actual, expected) || actual is AggregateException aggregate && aggregate.InnerExceptions.Any(ex => Contains(ex, expected));

    private static GameObject Render(ChunkSystem system, PreparedMesh data, Material material, bool shadows = false) =>
        (GameObject)Call(system, "CreateRenderer", "transaction tile", data, material, new Vec3(100, 200, 300), shadows);

    private static void UploadAndRendererContracts()
    {
        foreach (int count in new[] { 3, 65000, 65001, 65535, 65536 })
        foreach (bool readable in new[] { false, true })
        {
            PreparedMesh data = Triangle(count);
            Mesh mesh = MeshUpload.ToMesh(data, "unchanged contract", readable);
            Check(ReferenceEquals(mesh.vertices, data.Positions) && ReferenceEquals(mesh.normals, data.Normals) && ReferenceEquals(mesh.uv, data.Uvs), "prepared vertex/normal/UV buffers remain borrowed and unchanged");
            Check(ReferenceEquals(mesh.Indices, data.Indices) && mesh.Topology == MeshTopology.Triangles && mesh.Submesh == 0 && !mesh.RecalculateBounds, "triangle ordering and submesh/bounds flags are preserved");
            Check(mesh.indexFormat == (count <= 65000 ? IndexFormat.UInt16 : IndexFormat.UInt32), "the existing 65000 index cutoff is preserved");
            Check(mesh.bounds.Min.x == -3 && mesh.bounds.Min.y == -4 && mesh.bounds.Max.z == 8, "authored bounds survive upload");
            Check(readable ? !mesh.Uploaded.HasValue : mesh.Uploaded == true, "keepReadable preserves the existing upload contract");
            Console.WriteLine($"CONTRACT mesh {count} readable={readable} index={mesh.indexFormat} uploaded={mesh.Uploaded} p={mesh.vertices[0].x},{mesh.vertices[0].y},{mesh.vertices[0].z} n={mesh.normals[0].x},{mesh.normals[0].y},{mesh.normals[0].z} uv={mesh.uv[0].x},{mesh.uv[0].y} indices={string.Join(",", mesh.Indices)} bounds={mesh.bounds.Min.x},{mesh.bounds.Min.y},{mesh.bounds.Min.z}:{mesh.bounds.Max.x},{mesh.bounds.Max.y},{mesh.bounds.Max.z}");
            UnityEngine.Object.Destroy(mesh);
        }

        foreach (bool shadows in new[] { false, true })
        {
            var system = NewSystem();
            Call(system, "EnsureRoot");
            var material = new Material();
            GameObject go = Render(system, Triangle(), material, shadows);
#if !MG01_BASELINE
            Check(!go.activeSelf, "a completed part is hidden before it is published to the tile owner");
#endif
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            Check(ReferenceEquals(renderer.sharedMaterial, material) && renderer.renderingLayerMask == uint.MaxValue, "the borrowed material and all lighting layers are preserved");
            Check(renderer.lightProbeUsage == LightProbeUsage.BlendProbes && renderer.reflectionProbeUsage == ReflectionProbeUsage.BlendProbes && renderer.receiveShadows, "probe and receive-shadow policy is preserved");
            Check(renderer.shadowCastingMode == (shadows ? ShadowCastingMode.On : ShadowCastingMode.Off) && renderer.motionVectorGenerationMode == MotionVectorGenerationMode.Camera && renderer.allowOcclusionWhenDynamic, "cast shadows, camera motion vectors and occlusion policy are preserved");
            Check(go.transform.position.x == 100 && go.transform.position.y == 200 && go.transform.position.z == 300 && ReferenceEquals(go.transform.Parent, Field<GameObject>(system, "m_Root").transform), "tile local origin and parent are unchanged");
            // The caller hid the completed object in the old implementation; it is now hidden before publication.
            go.SetActive(false);
            Console.WriteLine($"CONTRACT renderer shadows={shadows} layer={renderer.renderingLayerMask} light={renderer.lightProbeUsage} reflection={renderer.reflectionProbeUsage} cast={renderer.shadowCastingMode} receive={renderer.receiveShadows} motion={renderer.motionVectorGenerationMode} occlusion={renderer.allowOcclusionWhenDynamic} position={go.transform.position.x},{go.transform.position.y},{go.transform.position.z} active={go.activeSelf}");
            UnityEngine.Object.Destroy(go);
            UnityEngine.Object.Destroy(go.GetComponent<MeshFilter>().sharedMesh);
            Check(!material.Destroyed, "the material stays borrowed on normal disposal");
            Call(system, "DestroyAll");
        }
    }

#if !MG01_BASELINE
    private static void AllocationFailures()
    {
        foreach (string point in new[] { "Mesh.construct", "Mesh.name", "Mesh.hideFlags", "Mesh.indexFormat", "Mesh.vertices", "Mesh.normals", "Mesh.uv", "Mesh.indices", "Mesh.bounds", "Mesh.upload" })
        {
            var first = new InvalidOperationException(point);
            Faults.Before = (operation, _) => { if (operation == point) throw first; };
            Exception actual;
            try { actual = Error(() => MeshUpload.ToMesh(Triangle(), "upload fails")); }
            finally { Faults.Before = null; }
            Check(ReferenceEquals(actual, first), point + " preserves the first upload error");
            Check(LiveMeshes == 0 && PendingCleanup == 0, point + " leaves no unowned mesh");
        }

        foreach (string point in new[] { "GameObject.construct", "GameObject.hideFlags", "GameObject.layer", "Transform.parent", "Transform.position", "GameObject.Add.MeshFilter", "MeshFilter.mesh", "GameObject.Add.MeshRenderer", "Renderer.material", "Renderer.layer", "Renderer.lightProbe", "Renderer.reflectionProbe", "Renderer.shadow", "Renderer.receive", "Renderer.motion", "Renderer.occlusion", "GameObject.active" })
        {
            var system = NewSystem();
            Call(system, "EnsureRoot");
            int objects = GameObject.Created.Count;
            var material = new Material();
            var first = new InvalidOperationException(point);
            Faults.Before = (operation, _) => { if (operation == point) throw first; };
            Exception actual;
            try { actual = Error(() => Render(system, Triangle(), material)); }
            finally { Faults.Before = null; }
            Check(ReferenceEquals(actual, first), point + " preserves the first object error");
            Check(LiveMeshes == 0 && PendingCleanup == 0, point + " releases the independent mesh");
            Check(GameObject.Created.Skip(objects).All(go => go.Destroyed), point + " releases attached components through its owned object");
            Check(!material.Destroyed, point + " leaves the borrowed material alive");
            Call(system, "DestroyAll");
        }
    }

    private static void FailedCleanupRetry()
    {
        var upload = new InvalidOperationException("upload");
        var release = new InvalidOperationException("release mesh");
        Faults.Before = (operation, _) =>
        {
            if (operation == "Mesh.upload") throw upload;
            if (operation == "Destroy.Mesh") throw release;
        };
        Exception actual;
        try { actual = Error(() => MeshUpload.ToMesh(Triangle(), "retained")); }
        finally { Faults.Before = null; }
        Check(Contains(actual, upload) && Contains(actual, release), "cleanup failure reports both the original and release error");
        Check(LiveMeshes == 1 && PendingCleanup == 1, "a mesh whose destroy failed stays owned");
        Faults.Before = (operation, _) => { if (operation == "Destroy.Mesh") throw release; };
        try { Check(Contains(Error(MeshUpload.RetryCleanup), release), "a failed retry remains observable"); }
        finally { Faults.Before = null; }
        Check(LiveMeshes == 1 && PendingCleanup == 1, "a repeated cleanup failure retains ownership");
        MeshUpload.RetryCleanup();
        Check(LiveMeshes == 0 && PendingCleanup == 0, "a later retry releases exactly the retained mesh");

        var system = NewSystem();
        Call(system, "EnsureRoot");
        int start = GameObject.Created.Count;
        var material = new Material();
        var setup = new InvalidOperationException("renderer setup");
        var goRelease = new InvalidOperationException("release object");
        Faults.Before = (operation, _) =>
        {
            if (operation == "Renderer.receive") throw setup;
            if (operation == "Destroy.GameObject") throw goRelease;
        };
        try { actual = Error(() => Render(system, Triangle(), material)); }
        finally { Faults.Before = null; }
        Check(Contains(actual, setup) && Contains(actual, goRelease), "object cleanup preserves the two failures");
        GameObject retained = GameObject.Created[start];
        Mesh dependent = retained.GetComponent<MeshFilter>().sharedMesh;
        Check(!retained.Destroyed && !dependent.Destroyed && !retained.activeSelf && PendingCleanup == 1, "a failed object release retains its still-referenced mesh without showing the partial renderer");
        Faults.Calls.Clear();
        Faults.Before = (operation, _) => { if (operation == "Destroy.Mesh") throw release; };
        try { actual = Error(MeshUpload.RetryCleanup); }
        finally { Faults.Before = null; }
        Check(retained.Destroyed && !dependent.Destroyed && PendingCleanup == 1 && Contains(actual, release), "partial cleanup retains only the mesh after releasing its object");
        Check(Faults.Calls.IndexOf("Destroy.GameObject") < Faults.Calls.IndexOf("Destroy.Mesh"), "cleanup releases the object before the mesh it references");
        Faults.Calls.Clear();
        // World teardown reaches the same pending owner even when no tile was published.
        Call(system, "DestroyAll");
        Check(dependent.Destroyed && PendingCleanup == 0 && !material.Destroyed, "World cleanup drains failed allocation ownership and preserves the material");
        Check(Faults.Calls.Count(op => op == "Destroy.GameObject") == 1, "retry does not destroy the already released failed object again (only the root)");
        Faults.Calls.Clear();
        MeshUpload.RetryCleanup();
        Check(Faults.Calls.Count == 0, "empty cleanup retries do no engine work");

        // One failed owner must not prevent independent retained allocations from being released.
        Faults.Before = (operation, _) =>
        {
            if (operation == "Mesh.upload") throw upload;
            if (operation == "Destroy.Mesh") throw release;
        };
        try
        {
            Error(() => MeshUpload.ToMesh(Triangle(), "retained A"));
            Error(() => MeshUpload.ToMesh(Triangle(), "retained B"));
        }
        finally { Faults.Before = null; }
        Check(PendingCleanup == 2 && LiveMeshes == 2, "separate failures retain separate allocations");
        Mesh blocked = Mesh.Created[Mesh.Created.Count - 1];
        Faults.Before = (operation, value) => { if (operation == "Destroy.Mesh" && ReferenceEquals(value, blocked)) throw release; };
        try { actual = Error(MeshUpload.RetryCleanup); }
        finally { Faults.Before = null; }
        Check(Contains(actual, release) && LiveMeshes == 1 && PendingCleanup == 1, "retry attempts every independent owner even if one fails");
        MeshUpload.RetryCleanup();
        Check(LiveMeshes == 0 && PendingCleanup == 0, "the final retained owner can be released later");

        var old = NewSystem();
        Call(old, "EnsureRoot");
        Faults.Before = (operation, _) =>
        {
            if (operation == "Renderer.receive") throw setup;
            if (operation == "Destroy.GameObject") throw goRelease;
        };
        try { Error(() => Render(old, Triangle(), material)); }
        finally { Faults.Before = null; }
        var next = NewSystem();
        Queue(next, Tile(next, new Entity { Index = 701 }));
        Call(next, "ApplyStep", double.MaxValue);
        Check(PendingCleanup == 1 && LiveMeshes == 3 && next.RendererCount == 2, "old failed ownership remains separate from a new World's installed tile");
        Call(old, "DestroyAll");
        Check(PendingCleanup == 0 && LiveMeshes == 2 && next.RendererCount == 2, "old cleanup releases only its failed objects without touching the new World's tile");
        Call(next, "DestroyAll");
        Check(LiveMeshes == 0, "the new World retains its normal cleanup contract");
    }

    private static void FailedPartRetry()
    {
        foreach (string point in new[] { "GameObject.construct", "GameObject.hideFlags" })
        {
            var rootSystem = NewSystem();
            Queue(rootSystem, Tile(rootSystem, new Entity { Index = 801 }));
            int start = GameObject.Created.Count;
            var first = new InvalidOperationException("root " + point);
            Faults.Before = (operation, _) => { if (operation == point) throw first; };
            Exception actual;
            try { actual = Error(() => Call(rootSystem, "ApplyStep", double.MaxValue)); }
            finally { Faults.Before = null; }
            Check(ReferenceEquals(actual, first) && Field<GameObject>(rootSystem, "m_Root") == null, point + " does not publish an incomplete root");
            Check(LiveMeshes == 0 && GameObject.Created.Skip(start).All(go => go.Destroyed), point + " leaves no unowned root object or mesh");
            Call(rootSystem, "ApplyStep", double.MaxValue);
            Check(LiveMeshes == 2 && rootSystem.RendererCount == 2 && GameObject.Created.Skip(start).Count(go => !go.Destroyed) == 3, point + " reattempts the root and the same tile without duplicate meshes or an orphan root");
            Call(rootSystem, "DestroyAll");
            Check(LiveMeshes == 0 && PendingCleanup == 0, point + " preserves normal teardown after recovery");
        }

        var system = NewSystem();
        Queue(system, Tile(system, new Entity { Index = 601 }));
        bool thrown = false;
        Faults.Before = (operation, _) =>
        {
            if (!thrown && operation == "Renderer.material") { thrown = true; throw new InvalidOperationException("first part"); }
        };
        try { Error(() => Call(system, "ApplyStep", double.MaxValue)); }
        finally { Faults.Before = null; }
        Check(LiveMeshes == 0 && system.RendererCount == 0, "a failed tile part publishes neither orphan resources nor a partial view");
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 2 && system.RendererCount == 2, "retry repeats the failed part instead of skipping it");
        Call(system, "DestroyAll");
        Check(LiveMeshes == 0 && PendingCleanup == 0, "the retried tile tears down normally");
    }
#endif
}
