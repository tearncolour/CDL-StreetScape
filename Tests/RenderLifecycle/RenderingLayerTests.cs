using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CDLC.TS;
using CDLC.TS.Render;
using UnityEngine;

internal static partial class Program
{
    private static int RegistryLength => ((ICollection)typeof(MeshUpload).GetField("s_Renderers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Count;

    // A test object is not in the system's views: its mesh, which outlives its object, is destroyed with it (a live mesh left over would count in the tests after this one).
    private static void Drop(GameObject go)
    {
        UnityEngine.Object.Destroy(go.GetComponent<MeshFilter>().sharedMesh);
        UnityEngine.Object.Destroy(go);
    }

    // The experiment file's rendering layer must reach the renderers that exist (0.12.81's search by name found 0 of them), and a renderer made after it takes it.
    private static void RenderingLayerRegistry()
    {
        const uint kDefault = uint.MaxValue, kVehicles = 0xFFFF08FFu;
        Check(MeshUpload.RenderingLayer == kDefault, "a renderer is made with every bit set unless the experiment file says otherwise");
        var system = NewSystem();
        Call(system, "EnsureRoot");
        var material = new Material();
        int baseline = MeshUpload.ApplyRenderingLayer(), made = MeshUpload.RenderersMade;
        var objects = new List<GameObject>();
        try
        {
            GameObject a = Render(system, Triangle(), material), b = Render(system, Triangle(), material), c = Render(system, Triangle(), material);
            objects.AddRange(new[] { a, b, c });
            Check(MeshUpload.RenderersMade == made + 3, "each renderer made is counted");
            Check(a.GetComponent<MeshRenderer>().renderingLayerMask == kDefault && c.GetComponent<MeshRenderer>().renderingLayerMask == kDefault, "made with the default mask");
            string census = MeshUpload.MaskCensus();
            Check(census.Contains("0xffffffff") && census.Contains("x" + (baseline + 3)) && census.Contains("alive of " ) && census.Contains($"{baseline + 3} alive"), "the census counts the masks the renderers carry: " + census);

            MeshUpload.RenderingLayer = kVehicles;
            Check(MeshUpload.ApplyRenderingLayer() == baseline + 3, "the change reaches every renderer that exists");
            census = MeshUpload.MaskCensus();
            Check(census.Contains("0xffff08ff (decal layers Vehicles (0x8)) x" + (baseline + 3)) && !census.Contains("0xffffffff"), "and the census reads the new mask from the renderers: " + census);
            Check(a.GetComponent<MeshRenderer>().renderingLayerMask == kVehicles && b.GetComponent<MeshRenderer>().renderingLayerMask == kVehicles && c.GetComponent<MeshRenderer>().renderingLayerMask == kVehicles, "each has the new mask");

            GameObject d = Render(system, Triangle(), material);
            objects.Add(d);
            Check(d.GetComponent<MeshRenderer>().renderingLayerMask == kVehicles, "a renderer made after the change takes it");
            int said = Mod.log.Lines.FindAll(l => l.Contains("0xffff08ff") && l.Contains("from now on")).Count;
            objects.Add(Render(system, Triangle(), material));
            Check(said == 1 && Mod.log.Lines.FindAll(l => l.Contains("0xffff08ff") && l.Contains("from now on")).Count == 1, "the mask a new renderer carries is said once, with the first renderer made with it");

            Drop(b);
            census = MeshUpload.MaskCensus();
            Check(census.Contains($"{baseline + 4} alive of {MeshUpload.RenderersMade} made"), "the census counts the renderers that are alive, not the destroyed one, and the ones made in all: " + census);
            MeshUpload.RenderingLayer = kDefault;
            Check(MeshUpload.ApplyRenderingLayer() == baseline + 4, "a destroyed renderer is not counted and is not touched");
            Check(a.GetComponent<MeshRenderer>().renderingLayerMask == kDefault && b.GetComponent<MeshRenderer>().renderingLayerMask == kVehicles, "the others have the mask back, the destroyed one is left as it was");

            var first = new InvalidOperationException("occlusion");
            int before = MeshUpload.RenderersMade;
            Faults.Before = (operation, _) => { if (operation == "Renderer.occlusion") throw first; };
            Exception actual;
            try { actual = Error(() => Render(system, Triangle(), material)); }
            finally { Faults.Before = null; }
            Check(ReferenceEquals(actual, first) && MeshUpload.RenderersMade == before, "a renderer whose setup threw is not counted");
            Check(MeshUpload.ApplyRenderingLayer() == baseline + 4, "and not held");
        }
        finally
        {
            MeshUpload.RenderingLayer = kDefault;
            foreach (GameObject go in objects)
                if (!go.Destroyed)
                    Drop(go);
            Call(system, "DestroyAll");
        }

        // Tiles come and go for a whole session: the list drops the destroyed renderers when it has reached its bound, so that it never holds more than that and what is alive.
        Call(system, "EnsureRoot");
        int most = 0;
        for (int i = 0; i < 12000; i++)
        {
            Drop(Render(system, Triangle(), material));
            most = Math.Max(most, RegistryLength);
        }

        Check(most <= 4096 && RegistryLength <= 4096, $"12000 renderers made and destroyed never leave more than 4096 entries: at most {most}, {RegistryLength} at the end");
        Call(system, "DestroyAll");
    }
}
