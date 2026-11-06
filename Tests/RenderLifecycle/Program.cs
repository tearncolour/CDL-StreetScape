using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CDLC.TS;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using Unity.Entities;
using UnityEngine;

internal static partial class Program
{
    private static int s_Checks;

    private static void Main(string[] args)
    {
        // The traffic-state screens are decorated with their live item as with the Realistic Information Extension (0.12.29).
        LiveContent.On = true;
        var cases = new Dictionary<string, Action>
        {
            ["replace"] = ReplacedTrafficScreen,
            ["refresh"] = RefreshFromSavedSettings,
            ["partial"] = PartialTileCleanup,
            ["deleted"] = DeletedTileCleanup,
            ["complete"] = CompleteTileCleanup,
            ["contracts"] = UploadAndRendererContracts,
            ["rendering-layer"] = RenderingLayerRegistry,
            ["first-builds"] = FirstBuildsWait,
            ["load-budgets"] = LoadBudgets,
            ["loading-jobs"] = LoadingJobs,
            ["load-report"] = LoadReport,
            ["forget"] = ForgetReplaced,
            ["forget one"] = ForgetDeleted,
            ["forget destroyed"] = ForgetDestroyed,
            ["auto index reused"] = AutoIndexReused,
            ["plan-cadence"] = PlanCadence,
            ["view-distance"] = ViewDistance,
            ["footprint"] = FootprintCollected,
            ["rebuilt-tiles"] = RebuiltTiles,
            ["rebuild-all"] = RebuildAllTiles,
            ["hold-build"] = HoldBuilds,
            ["superseded-builds"] = SupersededBuilds,
            ["horizon"] = HorizonTile,
            ["materials-own-update"] = MaterialsInTheirOwnUpdates,
            ["materials-not-held-up"] = MaterialsThatHaveBeenMadeAreNotHeldUp,
            ["materials-wait"] = ATileWaitsForADetailMaterial,
            ["materials-asked"] = ATileAsksForItsMaterialsWhenBuilt,
            ["materials-warm-step"] = TheStagesRunEveryUpdate,
            ["materials-half-budget"] = AMaterialIsNotMadeAfterHalfTheBudget,
            ["materials-asked-once"] = AMaterialIsAskedForOncePerTile,
            ["materials-collect"] = ABuiltTileAsksOnTheWayToTheQueue,
#if !MG01_BASELINE
            ["allocation-failures"] = AllocationFailures,
            ["cleanup-retry"] = FailedCleanupRetry,
            ["part-retry"] = FailedPartRetry
#endif
        };
        if (args.Length == 0)
        {
            foreach (Action test in cases.Values) test();
        }
        else
        {
            foreach (string name in args) cases[name]();
        }
        Console.WriteLine($"RESULT=PASS ({s_Checks} render lifecycle checks; production ChunkSystem, TileWorker and MeshUpload with engine substitutes)");
    }

    private static void Check(bool condition, string message)
    {
        s_Checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static object Call(ChunkSystem system, string name, params object[] arguments) =>
        typeof(ChunkSystem).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(system, arguments);

    private static T Field<T>(ChunkSystem system, string name) =>
        (T)typeof(ChunkSystem).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(system);

    private static ChunkSystem NewSystem()
    {
        var system = new ChunkSystem();
        system.Initialize();
        return system;
    }

    private static void Put(ChunkSystem system, Entity entity, TsItem item)
    {
        system.EntityManager.SetComponentData(entity, item);
        Call(system, "Put", entity, item);
    }

    private static TsItem Traffic(string spec = "range=2000;jn=5") => new TsItem { TypeHash = SignCatalog.ById(TrafficMap.TemplateId).Hash, Spec = spec };

    private static void ReplacedTrafficScreen()
    {
        var system = NewSystem();
        var entity = new Entity { Index = 101 };
        var other = new Entity { Index = 102 };
        Put(system, entity, Traffic());
        Put(system, other, Traffic());
        Check(system.TrafficScreens.Contains(entity), "a traffic screen is registered");

        var def = SignCatalog.ById("guide.exit.advance");
        string spec = GuideSpec.Parse(def.Template, "to1=上海;to2=苏州;dist=900;unit=m").ToString();
        var replacement = new TsItem { TypeHash = def.Hash, Spec = spec };
        Put(system, entity, replacement);
        Check(!system.TrafficScreens.Contains(entity), "replacing a traffic screen removes its registration");

        TrafficMap.Set(other.Index, new TmNetwork { Known = true }, 1);
        typeof(ChunkSystem).GetField("m_Resync", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(system, false);
        Call(system, "Sync", Stopwatch.StartNew());
        Check(system.Store[entity].Spec == spec, "an actual traffic-change sync preserves the replacement's saved destinations and distance");

        // An ECS replacement can precede the changed-component read. A stale registration must be safe too.
        Field<HashSet<Entity>>(system, "m_Traffic").Add(entity);
        Call(system, "RefreshTraffic");
        Check(!system.TrafficScreens.Contains(entity), "refresh itself rejects a stale registration for an ordinary board");
        Check(system.Store[entity].Spec == spec, "refresh never parses an ordinary board through the traffic template");
        system.EntityManager.DestroyEntity(other);
        Call(system, "RefreshTraffic");
        Check(!system.TrafficScreens.Contains(other), "refresh removes a destroyed screen");
        Call(system, "DestroyAll");
    }

    private static void RefreshFromSavedSettings()
    {
        var system = NewSystem();
        var entity = new Entity { Index = 201 };
        TsItem source = Traffic();
        Put(system, entity, source);
        ItemRecord derived = system.Store[entity];
        derived.Spec = "range=500;jn=2;id=9;rev=99";
        system.Store.Upsert(entity, derived);
        TrafficMap.Set(entity.Index, new TmNetwork { Known = true }, 7);
        Call(system, "RefreshTraffic");
        var template = SignCatalog.ById(TrafficMap.TemplateId).Template;
        GuideSpec drawn = GuideSpec.Parse(template, system.Store[entity].Spec);
        Check(drawn.Get("range") == "2000" && drawn.Get("jn") == "5", "refresh rebuilds from the ECS settings, not a previously decorated record");
        Check(drawn.Get("id") == "201" && drawn.Get("rev") == "7", "refresh applies the current live identity and revision");
        Check(system.EntityManager.GetComponentData<TsItem>(entity).Spec == source.Spec, "runtime refresh does not write live fields into the saved component");
        Call(system, "DestroyAll");
    }

    private static TileResult Tile(ChunkSystem system, Entity entity)
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
            Parts = new List<PreparedPart>
            {
                new PreparedPart { Material = ChunkBuilder.MetalMaterial, Mesh = mesh },
                new PreparedPart { Material = ChunkBuilder.MetalMaterial, Mesh = mesh }
            }
        };
    }

    private static void Queue(ChunkSystem system, TileResult result) => Field<Queue<TileResult>>(system, "m_Ready").Enqueue(result);
    private static int LiveMeshes => Mesh.Created.Count(mesh => !mesh.Destroyed);

    private static void PartialTileCleanup()
    {
        var system = NewSystem();
        Queue(system, Tile(system, new Entity { Index = 301 }));
        Call(system, "ApplyStep", -1d);
        Check(LiveMeshes == 1, "the first frame creates only one part of the tile");
        Call(system, "DestroyAll");
        Check(LiveMeshes == 0, "leaving a city destroys the independent mesh of an unfinished tile");
    }

    private static void DeletedTileCleanup()
    {
        var system = NewSystem();
        var entity = new Entity { Index = 401 };
        Queue(system, Tile(system, entity));
        Call(system, "ApplyStep", -1d);
        system.Store.Remove(entity);
        system.EntityManager.DestroyEntity(entity);
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 0, "deleting the final item during apply releases every prepared part's mesh");
        Check(system.RendererCount == 0, "a tile whose items disappeared is not installed");
        Call(system, "DestroyAll");
    }

    private static void CompleteTileCleanup()
    {
        var system = NewSystem();
        Queue(system, Tile(system, new Entity { Index = 501 }));
        Call(system, "ApplyStep", double.MaxValue);
        Check(LiveMeshes == 2 && system.RendererCount == 2, "the complete tile is installed normally");
        Call(system, "DestroyAll");
        Check(LiveMeshes == 0, "normal and unfinished tiles share the same mesh cleanup contract");
    }
}
