using System;
using CDLC.TS;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using Game.Rendering;
using Unity.Entities;
using UnityEngine;

// A new plan for changed items is made after 6 frames or 0.1 s (0.12.79; Cadence). The owner's 0.12.78 hold ran at 11 to 20 frames a second: 6 frames were up to half a second of waiting for each
// round of the chain items read, tiles planned, built on workers, made into objects.
internal static partial class Program
{
    private static void PlanCadence()
    {
        var system = Loaded(500f);
        Field<CameraUpdateSystem>(system, "m_Camera").activeCamera = new Camera();
        SetField(system, "m_Frame", 10);
        Call(system, "Plan");
        Check(Field<int>(system, "m_PlannedAtFrame") == 10, "the first plan is made at once");

        TsItem item = new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" };
        Put(system, new Entity { Index = 701 }, item);
        SetField(system, "m_Frame", 12);
        Time.realtimeSinceStartup = 500.05f;
        Call(system, "Plan");
        Check(Field<int>(system, "m_PlannedAtFrame") == 10, "2 frames and 0.05 s after the last plan: not yet");
        Time.realtimeSinceStartup = 500.09f;
        Call(system, "Plan");
        Check(Field<int>(system, "m_PlannedAtFrame") == 10, "2 frames and 0.09 s after the last plan: still not yet");

        Time.realtimeSinceStartup = 500.11f;
        Call(system, "Plan");
        Check(Field<int>(system, "m_PlannedAtFrame") == 12, "2 frames but 0.11 s (18 frames a second): planned, not after the sixth frame");

        Put(system, new Entity { Index = 702 }, item);
        SetField(system, "m_Frame", 14);
        Time.realtimeSinceStartup = 500.115f;
        Call(system, "Plan");
        Check(Field<int>(system, "m_PlannedAtFrame") == 12, "2 frames and 0.005 s later: not yet");

        SetField(system, "m_Frame", 17);
        Time.realtimeSinceStartup = 500.118f;
        Call(system, "Plan");
        Check(Field<int>(system, "m_PlannedAtFrame") == 12, "5 frames in 0.003 s: not yet");

        SetField(system, "m_Frame", 18);
        Time.realtimeSinceStartup = 500.12f;
        Call(system, "Plan");
        Check(Field<int>(system, "m_PlannedAtFrame") == 18, "6 frames in 0.01 s: the frame count still rules a fast frame rate");
        Call(system, "DestroyAll");
    }
}

// The hold's end line says how many tiles were made into objects and how many of them replaced a tile already drawn (0.12.80): the owner's 0.12.79 log could not tell a first build from a rebuild.
internal static partial class Program
{
    private static void RebuiltTiles()
    {
        ResetMaterials(name => MaterialNeed.Have);
        var system = NewSystem();
        int built0 = system.BuiltTotal, rebuilt0 = system.RebuiltTotal;
        Queue(system, TileOfMaterials(system, new Entity { Index = 771 }, ChunkBuilder.MetalMaterial));
        Call(system, "ApplyStep", double.MaxValue);
        Check(system.BuiltTotal - built0 == 1 && system.RebuiltTotal - rebuilt0 == 0, $"a tile made for the first time is a build, not a rebuild: {system.BuiltTotal - built0} built, {system.RebuiltTotal - rebuilt0} again");
        Queue(system, TileOfMaterials(system, new Entity { Index = 772 }, ChunkBuilder.MetalMaterial));
        Call(system, "ApplyStep", double.MaxValue);
        Check(system.BuiltTotal - built0 == 2 && system.RebuiltTotal - rebuilt0 == 1, $"the same tile made again over the drawn one is a rebuild: {system.BuiltTotal - built0} built, {system.RebuiltTotal - rebuilt0} again");
        Call(system, "DestroyAll");
        ResetMaterials(null);
    }
}

// The draw distance of the signs and markings is the player's (0.12.103): the far limit of the system, the tiles it shows and the tiles it plans follow it at once, and it is not set from a number that is not one.
internal static partial class Program
{
    private static void ViewDistance()
    {
        // The starting distance is the file's (PreferencesStore sets it before the system is made); a number outside the range is brought into it.
        Check(ChunkSystem.StartingViewDistance == 700f, "the starting distance is 700 m until the preferences say another");
        ChunkSystem.StartingViewDistance = 2000f;
        var set = NewSystem();
        Check(set.ViewDistance == 2000f && Field<StreamOptions>(set, "m_Options").FarMetres == 2000f, "a system made after the preferences were read starts at their distance");
        Call(set, "DestroyAll");
        ChunkSystem.StartingViewDistance = 99999f;
        var clamped = NewSystem();
        Check(clamped.ViewDistance == 3000f && Field<StreamOptions>(clamped, "m_Options").FarMetres == 3000f, "one made from a number out of range starts at the most");
        Call(clamped, "DestroyAll");
        ChunkSystem.StartingViewDistance = 700f;

        var system = Loaded(900f);
        Check(system.ViewDistance == 700f && Field<StreamOptions>(system, "m_Options").FarMetres == 700f, "the default system draws to 700 m");
        TileKey tile = Tiles.Of(0f, 0f);
        Put(system, new Entity { Index = 901 }, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" });
        var items = new System.Collections.Generic.List<ItemRecord>();
        int version = system.Store.Snapshot(tile, items);
        Queue(system, TileWorker.Build(tile, TileTier.Far, version, system.Epoch, items));
        Call(system, "ApplyStep", double.MaxValue);
        var views = Field<System.Collections.IDictionary>(system, "m_Views");
        object view = views[tile];
        SetField(system, "m_CamX", 1100f);
        SetField(system, "m_CamZ", 0f);
        bool hidden = !(bool)Call(system, "ShouldShow", view, false) && !(bool)Call(system, "ShouldShow", view, true);
        Check(hidden, "a far tile about 900 m from the camera is not shown at 700 m");

        system.SetViewDistance(2000f);
        Check(system.ViewDistance == 2000f && Field<StreamOptions>(system, "m_Options").FarMetres == 2000f, "set to 2 km");
        Check((bool)Call(system, "ShouldShow", view, false) && (bool)Call(system, "ShouldShow", view, true), "the same tile is shown at 2 km");
        SetField(system, "m_CamX", 2400f);
        Check(!(bool)Call(system, "ShouldShow", view, false), "and is not shown from 2.2 km, whatever it was");
        Check(Field<StreamOptions>(system, "m_Options").NearMetres == 70f && Field<StreamOptions>(system, "m_Options").MidMetres == 250f, "the near and middle limits did not move");
        Check(Field<bool>(system, "m_Replan"), "the tiles are planned again at once");

        // A plan follows with no camera move and no change of items: the flag alone makes it, and it clears.
        var camera = new Camera();
        Field<CameraUpdateSystem>(system, "m_Camera").activeCamera = camera;
        // The camera has not moved, nothing changed and the last plan was 20 frames ago (inside the safety period): only the flag asks for one.
        SetField(system, "m_PlannedAtFrame", 480);
        SetField(system, "m_PlanX", 0f);
        SetField(system, "m_PlanZ", 0f);
        SetField(system, "m_Frame", 500);
        Time.realtimeSinceStartup = 900.2f;
        SetField(system, "m_PlannedAtSeconds", 900f);
        SetField(system, "m_PlannedStoreChanges", system.Store.Changes);
        SetField(system, "m_Replan", false);
        Call(system, "Plan");
        Check(Field<int>(system, "m_PlannedAtFrame") == 480, "with no flag, no move and no change the plan is not made");
        SetField(system, "m_Replan", true);
        Call(system, "Plan");
        Check(Field<int>(system, "m_PlannedAtFrame") == 500 && !Field<bool>(system, "m_Replan"), "with the flag alone it is, and the flag is cleared");
        SetField(system, "m_Frame", 510);
        Time.realtimeSinceStartup = 900.5f;
        Call(system, "Plan");
        Check(Field<int>(system, "m_PlannedAtFrame") == 500, "and no plan is made again for nothing");

        // The picture budget scales the near and middle limits only (the stub has no pictures: the scale goes back up): the far limit stays the player's.
        SetField(system, "m_Scale", 0.5f);
        Call(system, "AdaptBudget");
        StreamOptions scaled = Field<StreamOptions>(system, "m_Options");
        Check(scaled.FarMetres == 2000f && scaled.NearMetres < 70f && scaled.NearMetres > 30f && scaled.MidMetres < 250f, $"the budget scales near ({scaled.NearMetres:0} m) and middle ({scaled.MidMetres:0} m) and leaves the far limit at {scaled.FarMetres:0} m");
        system.SetViewDistance(1500f);
        StreamOptions both = Field<StreamOptions>(system, "m_Options");
        Check(both.FarMetres == 1500f && Math.Abs(both.NearMetres - scaled.NearMetres) < 1e-3f, "a new distance keeps the scale the budget has made");
        system.SetViewDistance(2000f);

        // The same distance again, or a number that is not one, changes nothing.
        SetField(system, "m_Replan", false);
        system.SetViewDistance(2000f);
        Check(!Field<bool>(system, "m_Replan") && system.ViewDistance == 2000f, "the same distance again is no change and plans nothing");
        system.SetViewDistance(float.NaN);
        Check(system.ViewDistance == 700f && Field<bool>(system, "m_Replan"), "a number that is not one is the default distance");
        system.SetViewDistance(50f);
        Check(system.ViewDistance == 300f && Field<StreamOptions>(system, "m_Options").FarMetres == 300f, "one below the range is the least");
        Call(system, "DestroyAll");
    }
}

// What the panel's figures are made from (0.12.104): the tiles of the store with their distance, items, tier and the bytes of their meshes, counted as the parts are made into objects.
internal static partial class Program
{
    private static void FootprintCollected()
    {
        var system = Loaded(950f);
        SetField(system, "m_HaveCamera", false);
        var into = new FootprintInput();
        system.CollectFootprint(into);
        Check(into.Tiles.Count == 0 && into.Items == 0 && into.ItemBytes == 0, "a system with no item has no tile and no item bytes");
        TileKey tile = Tiles.Of(0f, 0f), other = Tiles.Of(Tiles.Size * 3f, 0f);
        Put(system, new Entity { Index = 951 }, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "abcde" });
        Put(system, new Entity { Index = 952 }, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" });
        system.CollectFootprint(into);
        Check(into.Items == 2 && into.ItemBytes == Footprint.ItemBytes(5) + Footprint.ItemBytes(0) && into.Tiles.Count == 0, "two items are counted and their records, though the camera is not known yet: no tile");

        SetField(system, "m_HaveCamera", true);
        SetField(system, "m_CamX", 100f);
        SetField(system, "m_CamZ", 50f);
        var items = new System.Collections.Generic.List<ItemRecord>();
        int version = system.Store.Snapshot(tile, items);
        TileResult result = TileWorker.Build(tile, TileTier.Far, version, system.Epoch, items);
        long expected = 0;
        foreach (PreparedPart part in result.Parts)
            expected += Footprint.MeshBytes(part.Mesh.VertexCount, part.Mesh.Indices.Length);
        Check(result.Parts.Count > 0 && expected > 0, $"the tile's parts have meshes: {result.Parts.Count} part(s), {expected} bytes by the rule");
        Queue(system, result);
        Call(system, "ApplyStep", double.MaxValue);
        system.CollectFootprint(into);
        Check(into.Tiles.Count == 1 && into.Tiles[0].Tier == TileTier.Far && into.Tiles[0].Items == 2 && into.Tiles[0].MeshBytes == expected, $"the built tile: far, 2 items, {into.Tiles[0].MeshBytes} bytes (the parts': {expected})");
        Check(into.Renderers > 0 && into.Renderers == system.RendererCount, $"the renderers are the objects of the built tiles: {into.Renderers}");
        Check(Math.Abs(into.Tiles[0].Distance - TileStreamer.DistanceToTile(tile, 100f, 50f)) < 1e-3f && into.Tiles[0].Distance == 0f, "the distance is from the camera to the tile's nearest point (the camera is inside it)");

        // A tile with items that is not built is there too, with no tier and no bytes; its distance is the camera's.
        Put(system, new Entity { Index = 953 }, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" });
        system.Store.Upsert(new Entity { Index = 954 }, new ItemRecord { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Position = new CDLC.TS.Snap.Vec3(Tiles.Size * 3f + 10f, 0f, 10f), Spec = "" });
        system.CollectFootprint(into);
        TileCost farTile = into.Tiles.Find(t => t.Tier == TileTier.None);
        Check(into.Tiles.Count == 2 && farTile.Items == 1 && farTile.MeshBytes == 0 && Math.Abs(farTile.Distance - TileStreamer.DistanceToTile(other, 100f, 50f)) < 1e-3f, $"a tile with an item and no view: none, no bytes, {farTile.Distance:0} m away");

        // A built tile whose items are gone is held with no items until the plan drops it.
        system.Store.Remove(new Entity { Index = 951 });
        system.Store.Remove(new Entity { Index = 952 });
        system.Store.Remove(new Entity { Index = 953 });
        system.CollectFootprint(into);
        TileCost held = into.Tiles.Find(t => t.Tier == TileTier.Far);
        Check(held.Items == 0 && held.MeshBytes == expected && into.Tiles.Count == 2, "the built tile whose items are gone is still there, with no items and its bytes");
        FootprintReport report = Footprint.Build(into);
        Check(report.MeshBytes == expected && !report.HasModel, "and the report holds its meshes and has no model to tell a step from (no tile has items)");
        Call(system, "DestroyAll");
    }
}
