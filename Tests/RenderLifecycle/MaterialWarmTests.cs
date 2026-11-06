using System.Collections.Generic;
using System.Linq;
using CDLC.TS;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using Unity.Entities;
using UnityEngine;

// The materials of a tile (0.12.76; the owner's 0.12.75 log: a part of 460 vertices took 40.7 ms because its material was made in it): a material is made in an update of its own, a tile waits
// for a baked-detail material that is on its way, and asks for its materials as soon as it is built.
internal static partial class Program
{
    private static TileResult TileOfMaterials(ChunkSystem system, Entity entity, params string[] materials)
    {
        Put(system, entity, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" });
        var key = Tiles.Of(0f, 0f);
        var mesh = new PreparedMesh
        {
            Positions = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
            Normals = new Vector3[3], Uvs = new Vector2[3], Indices = new[] { 0, 1, 2 }
        };
        return new TileResult
        {
            Key = key, Tier = TileTier.Near, Version = system.Store.VersionOf(key), Epoch = system.Epoch,
            Parts = materials.Select(material => new PreparedPart { Material = material, Mesh = mesh }).ToList()
        };
    }

    private static void ResetMaterials(System.Func<string, MaterialNeed> rule)
    {
        TsMaterials.NeedRule = rule;
        TsMaterials.Made.Clear();
        TsMaterials.Asked.Clear();
    }

    private static void MaterialsInTheirOwnUpdates()
    {
        // Three materials the main thread has to make (solid:*) among two that are there: one is made in an update, the next waits for the next.
        ResetMaterials(name => name.StartsWith("solid:") ? MaterialNeed.Make : MaterialNeed.Have);
        int made0 = TsMaterials.MadeCount;
        var system = NewSystem();
        Queue(system, TileOfMaterials(system, new Entity { Index = 701 }, ChunkBuilder.MetalMaterial, "solid:1", "solid:2", ChunkBuilder.MetalMaterial, "solid:3"));
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 2 && TsMaterials.MadeCount - made0 == 1, $"the first update makes one material and the part before it: {LiveMeshes} part(s), {TsMaterials.MadeCount - made0} made");
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 4 && TsMaterials.MadeCount - made0 == 2, $"the second makes the next in its own update, the part that has its material goes with it and the one after waits: {LiveMeshes}, {TsMaterials.MadeCount - made0}");
        Check(system.RendererCount == 0, "the tile is not shown before its last part");
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 5 && system.RendererCount == 5 && TsMaterials.MadeCount - made0 == 3, "the third finishes the tile, three updates for three materials");
        Call(system, "DestroyAll");
        ResetMaterials(null);
    }

    private static void MaterialsThatHaveBeenMadeAreNotHeldUp()
    {
        // Everything is there: the whole tile in one update, as before.
        ResetMaterials(name => MaterialNeed.Have);
        var system = NewSystem();
        Queue(system, TileOfMaterials(system, new Entity { Index = 711 }, "solid:1", "solid:2", "solid:3", ChunkBuilder.MetalMaterial));
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 4 && system.RendererCount == 4, "materials that are there are made into objects in one update");
        Call(system, "DestroyAll");

        // The budget is spent (the tests' -1): the first part of an update is still made, and a material that has to be made is not made after other parts.
        ResetMaterials(name => name.StartsWith("solid:") ? MaterialNeed.Make : MaterialNeed.Have);
        system = NewSystem();
        Queue(system, TileOfMaterials(system, new Entity { Index = 712 }, "solid:1", ChunkBuilder.MetalMaterial));
        Call(system, "ApplyStep", -1d);
        Check(LiveMeshes == 1 && TsMaterials.Made.Contains("solid:1"), "an update with no budget left still makes its first part, and its material, so that nothing waits for ever");
        Call(system, "DestroyAll");
        ResetMaterials(null);
    }

    private static void ATileWaitsForADetailMaterial()
    {
        const string detail = "detail:17:30";
        ResetMaterials(name => name == detail ? MaterialNeed.Wait : MaterialNeed.Have);
        var system = NewSystem();
        Time.realtimeSinceStartup = 100f;
        Queue(system, TileOfMaterials(system, new Entity { Index = 721 }, ChunkBuilder.MetalMaterial, detail, ChunkBuilder.MetalMaterial));
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 1, "the part before the detail material is made, the part with it waits");
        Call(system, "ApplyStep", double.MaxValue);
        Time.realtimeSinceStartup = 101.4f;
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 1 && !TsMaterials.Made.Contains(detail), "it waits for the stages of its material for 1.5 s and the main thread does not make the material meanwhile");

        // The material is there before the time is up: the part goes on.
        TsMaterials.Made.Add(detail);
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 3 && system.RendererCount == 3, "the tile is finished as soon as the material is there");
        Call(system, "DestroyAll");

        // It does not come: after 1.5 s the main thread makes it, whole, as it did before the stages.
        ResetMaterials(name => name == detail ? MaterialNeed.Wait : MaterialNeed.Have);
        int made0 = TsMaterials.MadeCount;
        system = NewSystem();
        Time.realtimeSinceStartup = 200f;
        Queue(system, TileOfMaterials(system, new Entity { Index = 722 }, ChunkBuilder.MetalMaterial, detail, ChunkBuilder.MetalMaterial));
        Call(system, "ApplyStep", double.MaxValue);
        Time.realtimeSinceStartup = 201.49f;
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 1, "just under 1.5 s: still waiting");
        Time.realtimeSinceStartup = 201.5f;
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 3 && TsMaterials.MadeCount - made0 == 1 && TsMaterials.Made.Contains(detail), "after 1.5 s the main thread makes the material and the tile is finished");
        Call(system, "DestroyAll");

        // The wait is counted from the part that waits: a part that is made resets it.
        ResetMaterials(name => name.StartsWith("detail:") ? MaterialNeed.Wait : MaterialNeed.Have);
        system = NewSystem();
        Time.realtimeSinceStartup = 300f;
        Queue(system, TileOfMaterials(system, new Entity { Index = 723 }, "detail:1:1", "detail:2:2"));
        Call(system, "ApplyStep", double.MaxValue);
        Time.realtimeSinceStartup = 301.5f;
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 1 && TsMaterials.Made.Contains("detail:1:1"), "the first is made after its time");
        Time.realtimeSinceStartup = 301.6f;
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 1 && !TsMaterials.Made.Contains("detail:2:2"), "the second has its own 1.5 s: the wait of the first is not carried over");
        Time.realtimeSinceStartup = 303.1f;
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 2, "and is made after it");
        Call(system, "DestroyAll");
        ResetMaterials(null);
    }

    private static void ATileAsksForItsMaterialsWhenBuilt()
    {
        ResetMaterials(name => MaterialNeed.Have);
        var system = NewSystem();
        TileResult tile = TileOfMaterials(system, new Entity { Index = 731 }, "detail:3:5", ChunkBuilder.MetalMaterial, "detail:3:5", "solid:1", "detail:3:5", ChunkBuilder.MetalMaterial);
        Call(system, "AskForMaterials", tile);
        Check(TsMaterials.Asked.Count == 3 && TsMaterials.Asked.Distinct().Count() == 3 && TsMaterials.Asked.Contains("detail:3:5") && TsMaterials.Asked.Contains("solid:1") && TsMaterials.Asked.Contains(ChunkBuilder.MetalMaterial), "each distinct material of a tile is asked for once: " + string.Join(", ", TsMaterials.Asked));
        TsMaterials.Asked.Clear();
        Call(system, "AskForMaterials", TileOfMaterials(system, new Entity { Index = 732 }));
        Check(TsMaterials.Asked.Count == 0, "a tile with no part asks for nothing");
        Call(system, "DestroyAll");
        ResetMaterials(null);
    }

    private static void AMaterialIsNotMadeAfterHalfTheBudget()
    {
        // A part that takes 400 ms of a budget of 600 uses more than its half: the material after it is for the next update. (The sleep is the part's own cost; an oversleep that spends the whole budget also ends the update.)
        ResetMaterials(name => name.StartsWith("solid:") ? MaterialNeed.Make : MaterialNeed.Have);
        TsMaterials.SleepMs["slow"] = 400;
        var system = NewSystem();
        Queue(system, TileOfMaterials(system, new Entity { Index = 741 }, "slow", "solid:1"));
        Call(system, "ApplyStep", 600d);
        Check(LiveMeshes == 1 && !TsMaterials.Made.Contains("solid:1"), $"a material is not made after a part that used more than half of the budget: {LiveMeshes}");
        Call(system, "ApplyStep", 600d);
        Check(LiveMeshes == 2 && TsMaterials.Made.Contains("solid:1"), "it is made in the next update, its first part");
        Call(system, "DestroyAll");

        // Within the half (a part of 5 ms of 600) the material is made in the same update.
        ResetMaterials(name => name.StartsWith("solid:") ? MaterialNeed.Make : MaterialNeed.Have);
        TsMaterials.SleepMs["slow"] = 5;
        system = NewSystem();
        Queue(system, TileOfMaterials(system, new Entity { Index = 742 }, "slow", "solid:1"));
        Call(system, "ApplyStep", 600d);
        Check(LiveMeshes == 2 && TsMaterials.Made.Contains("solid:1"), "within half the budget the material goes with the part before it");
        Call(system, "DestroyAll");

        // A tile that replaces a built one has three times the budget (kReplaceFactor): the same 400 ms are within the half of 1800.
        ResetMaterials(name => name.StartsWith("solid:") ? MaterialNeed.Make : MaterialNeed.Have);
        TsMaterials.SleepMs["slow"] = 400;
        system = NewSystem();
        Queue(system, TileOfMaterials(system, new Entity { Index = 743 }, ChunkBuilder.MetalMaterial));
        Call(system, "ApplyStep", double.MaxValue);
        Check(system.RendererCount == 1, "the tile is built");
        Queue(system, TileOfMaterials(system, new Entity { Index = 744 }, "slow", "solid:2"));
        Call(system, "ApplyStep", 600d);
        Check(TsMaterials.Made.Contains("solid:2"), "a tile that replaces a built one is given three times the budget, and the gate counts with it");
        Call(system, "DestroyAll");
        TsMaterials.SleepMs.Clear();
        ResetMaterials(null);
    }

    private static void AMaterialIsAskedForOncePerTile()
    {
        ResetMaterials(name => MaterialNeed.Have);
        var system = NewSystem();
        Queue(system, TileOfMaterials(system, new Entity { Index = 751 }, ChunkBuilder.MetalMaterial, ChunkBuilder.MetalMaterial, ChunkBuilder.MetalMaterial, ChunkBuilder.MetalMaterial));
        Call(system, "ApplyStep", double.MaxValue);
        Check(TsMaterials.Asked.Count(name => name == ChunkBuilder.MetalMaterial) == 1, "a material that is there is asked for once for a tile, not for each of its parts: " + TsMaterials.Asked.Count);
        Call(system, "DestroyAll");
        ResetMaterials(null);
    }

    private static void ABuiltTileAsksOnTheWayToTheQueue()
    {
        ResetMaterials(name => MaterialNeed.Have);
        var system = NewSystem();
        TileResult tile = TileOfMaterials(system, new Entity { Index = 761 }, "detail:4:2", ChunkBuilder.MetalMaterial);
        System.Type jobType = typeof(ChunkSystem).GetNestedType("Job", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        object job = System.Activator.CreateInstance(jobType, true);
        jobType.GetField("Task").SetValue(job, System.Threading.Tasks.Task.FromResult(tile));
        jobType.GetField("Tier").SetValue(job, tile.Tier);
        jobType.GetField("Version").SetValue(job, tile.Version);
        Field<System.Collections.IDictionary>(system, "m_Jobs").Add(tile.Key, job);
        Call(system, "CollectJobs");
        Check(Field<Queue<TileResult>>(system, "m_Ready").Count == 1, "the finished build is queued to be made into objects");
        Check(TsMaterials.Asked.Contains("detail:4:2") && TsMaterials.Asked.Contains(ChunkBuilder.MetalMaterial), "and its materials were asked for on the way: the baked-detail ones start to be made while it waits");
        Call(system, "DestroyAll");
        ResetMaterials(null);
    }

    private static void TheStagesRunEveryUpdate()
    {
        ResetMaterials(null);
        var system = NewSystem();
        int before = TsMaterials.WarmCalls;
        Call(system, "OnUpdate");
        Check(TsMaterials.WarmCalls == before + 1, "an update of the chunk renderer gives the stages of the detail materials their turn");
        Call(system, "DestroyAll");
    }
}
