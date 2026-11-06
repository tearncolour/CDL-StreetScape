using System.Collections.Generic;
using System.Reflection;
using CDLC.TS;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using Unity.Entities;
using UnityEngine;

// After a city is loaded (0.12.27; the owner: "主要还是加载时间优化"): a tile's first build waits a little for the posts' grounds near the camera, the budgets grow while the frames are fast, and the load is
// reported once.
internal static partial class Program
{
    private static void SetField(ChunkSystem system, string name, object value) =>
        typeof(ChunkSystem).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(system, value);

    /// <summary>A system as it is right after a load at the time `at`: the items read, the camera planned.</summary>
    private static ChunkSystem Loaded(float at)
    {
        var system = NewSystem();
        Time.realtimeSinceStartup = at;
        SetField(system, "m_LoadedAt", at);
        SetField(system, "m_ReadAt", -1f);
        SetField(system, "m_LoadReported", false);
        SetField(system, "m_HaveCamera", true);
        SetField(system, "m_Resync", false);
        typeof(ChunkSystem).GetProperty("Synced", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(system, true);
        return system;
    }

    private static void FirstBuildsWait()
    {
        AreaFitSystem.Instance = new AreaFitSystem { NearWaitingValue = 3 };
        var system = Loaded(100f);
        Put(system, new Entity { Index = 601 }, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" });
        var wishes = Field<List<TileWish>>(system, "m_ToBuild");
        var jobs = Field<System.Collections.IDictionary>(system, "m_Jobs");
        wishes.Add(new TileWish { Key = Tiles.Of(0f, 0f), Tier = TileTier.Near, Distance = 0f });
        Call(system, "StartJobs");
        Check(jobs.Count == 0 && wishes.Count == 1, "a tile not built yet waits while grounds near the camera are being made");
        Time.realtimeSinceStartup = 101f;
        Call(system, "StartJobs");
        Check(jobs.Count == 0 && wishes.Count == 1, "still within the 1.5 s");
        Time.realtimeSinceStartup = 101.6f;
        Call(system, "StartJobs");
        Check(jobs.Count == 1 && wishes.Count == 0, "no longer than 1.5 s after the items were read");
        Call(system, "DestroyAll");

        // When the grounds near the camera are made, at once.
        AreaFitSystem.Instance = new AreaFitSystem { NearWaitingValue = 0 };
        system = Loaded(200f);
        Put(system, new Entity { Index = 602 }, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" });
        wishes = Field<List<TileWish>>(system, "m_ToBuild");
        jobs = Field<System.Collections.IDictionary>(system, "m_Jobs");
        wishes.Add(new TileWish { Key = Tiles.Of(0f, 0f), Tier = TileTier.Near, Distance = 0f });
        Call(system, "StartJobs");
        Check(jobs.Count == 1, "nothing to wait for: built at once");
        Call(system, "DestroyAll");

        // Long after a load, never.
        AreaFitSystem.Instance = new AreaFitSystem { NearWaitingValue = 5 };
        system = Loaded(300f);
        SetField(system, "m_LoadReported", true);
        Put(system, new Entity { Index = 603 }, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" });
        wishes = Field<List<TileWish>>(system, "m_ToBuild");
        jobs = Field<System.Collections.IDictionary>(system, "m_Jobs");
        wishes.Add(new TileWish { Key = Tiles.Of(0f, 0f), Tier = TileTier.Near, Distance = 0f });
        Call(system, "StartJobs");
        Check(jobs.Count == 1, "once the load is reported a sign placed is built at once");
        Call(system, "DestroyAll");
        AreaFitSystem.Instance = null;
    }

    /// <summary>The tiles built at once on workers: three, and twice as many while the first-time work runs under the loading screen (LoadBudget.Loading); never more than that.</summary>
    private static void LoadingJobs()
    {
        AreaFitSystem.Instance = new AreaFitSystem { NearWaitingValue = 0 };
        foreach (bool loading in new[] { false, true })
        {
            LoadBudget.Loading = loading;
            var system = Loaded(500f);
            SetField(system, "m_LoadReported", true);
            var wishes = Field<List<TileWish>>(system, "m_ToBuild");
            var jobs = Field<System.Collections.IDictionary>(system, "m_Jobs");
            for (int i = 0; i < 9; i++)
            {
                var key = Tiles.Of(i * Tiles.Size * 2f, 0f);
                Put(system, new Entity { Index = 900 + i + (loading ? 20 : 0) }, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "", Position = new Vector3(i * Tiles.Size * 2f, 0f, 0f) });
                wishes.Add(new TileWish { Key = key, Tier = TileTier.Near, Distance = i });
            }

            Call(system, "StartJobs");
            Check(jobs.Count == (loading ? 6 : 3), (loading ? "loading: six tiles on workers at once: " : "steady: three tiles on workers at once: ") + jobs.Count);
            Call(system, "DestroyAll");
        }

        LoadBudget.Loading = false;
        AreaFitSystem.Instance = null;
    }

    private static void LoadBudgets()
    {
        var system = Loaded(400f);
        void Frames(float seconds, int n)
        {
            Time.unscaledDeltaTime = seconds;
            for (int i = 0; i < n; i++)
                Call(system, "UpdateLoadFactor");
        }

        Frames(1f / 60f, 60);
        Check(ChunkSystem.LoadFactor == 3f, "fast frames after a load: three times the budgets");
        Frames(1f / 40f, 60);
        Check(ChunkSystem.LoadFactor == 2f, "40 frames a second: twice");
        Frames(1f / 24f, 60);
        Check(ChunkSystem.LoadFactor == 1f, "slow frames: as they are");
        Frames(1f / 60f, 60);
        Check(ChunkSystem.LoadFactor == 3f, "back up when the frames are fast again");
        Time.realtimeSinceStartup = 400f + 121f;
        Frames(1f / 60f, 1);
        Check(ChunkSystem.LoadFactor == 1f, "two minutes after the load: as they are");
        Time.realtimeSinceStartup = 401f;
        SetField(system, "m_LoadReported", true);
        Frames(1f / 60f, 1);
        Check(ChunkSystem.LoadFactor == 1f, "and once the signs round the camera are complete");
        Call(system, "DestroyAll");
    }

    private static void LoadReport()
    {
        AreaFitSystem.Instance = new AreaFitSystem { NearWaitingValue = 2 };
        var system = Loaded(500f);
        Put(system, new Entity { Index = 701 }, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" });
        Time.realtimeSinceStartup = 510f;
        Call(system, "ReportLoad", 1.0);
        Check(!Field<bool>(system, "m_LoadReported"), "not complete while grounds near the camera wait");
        AreaFitSystem.Instance.NearWaitingValue = 0;
        Call(system, "ReportLoad", 1.0);
        Check(Field<bool>(system, "m_LoadReported") && ChunkSystem.LoadFactor == 1f, "complete: reported once, the budgets back to their own");
        Check(System.Math.Abs(Field<double>(system, "m_LoadMainMs") - 2.0) < 1e-9, "the main thread's time of the frames until then");
        Call(system, "DestroyAll");

        // Something still waiting three minutes after the load: reported anyway, with what waits.
        AreaFitSystem.Instance = new AreaFitSystem { NearWaitingValue = 7 };
        system = Loaded(600f);
        Put(system, new Entity { Index = 702 }, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" });
        Time.realtimeSinceStartup = 600f + 181f;
        Call(system, "ReportLoad", 1.0);
        Check(Field<bool>(system, "m_LoadReported"), "after three minutes it is reported as not complete");
        Call(system, "DestroyAll");
        AreaFitSystem.Instance = null;
    }
}

// The horizon tier (0.12.73; the owner: "全封闭隔音墙离远了就消失了"): a tile that has a noise wall is built beyond the far limit with the wall alone, and is shown as far as its own limit.
internal static partial class Program
{
    private static void HorizonTile()
    {
        var system = Loaded(600f);
        TileKey tile = Tiles.Of(0f, 0f);
        Put(system, new Entity { Index = 801 }, new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" });
        Put(system, new Entity { Index = 802 }, new TsItem { TypeHash = SignCatalog.ById(MarkingTemplates.NoiseCoverId).Hash, Spec = "" });
        Check(system.Store.HasLongReach(tile) && system.Store.ItemCount(tile) == 2, "the tile has a sign and a wall, and the store knows it has a wall");

        var wishes = Field<List<TileWish>>(system, "m_ToBuild");
        var jobs = Field<System.Collections.IDictionary>(system, "m_Jobs");
        wishes.Add(new TileWish { Key = tile, Tier = TileTier.Horizon, Distance = 2000f });
        Call(system, "StartJobs");
        Check(jobs.Count == 1, "the horizon tile is built on a worker");
        object job = null;
        foreach (object value in jobs.Values)
            job = value;
        TileResult horizon = ((System.Threading.Tasks.Task<TileResult>)job.GetType().GetField("Task").GetValue(job)).Result;
        Check(horizon.Error == null && horizon.Items == 1 && horizon.Tier == TileTier.Horizon && horizon.Parts.Count > 0, "it is made of the wall alone: " + horizon.Items + " item(s), " + horizon.Error);
        Check(horizon.Parts.TrueForAll(part => !part.Material.StartsWith(ChunkBuilder.FacePrefix, System.StringComparison.Ordinal)), "and has no sign's face");
        Call(system, "DestroyAll");

        // Its objects are shown as far as the horizon's limit, a far tile's only to the far limit, and the hysteresis is the same.
        system = Loaded(700f);
        Put(system, new Entity { Index = 811 }, new TsItem { TypeHash = SignCatalog.ById(MarkingTemplates.NoiseCoverId).Hash, Spec = "" });
        var items = new List<ItemRecord>();
        int version = system.Store.Snapshot(tile, items);
        var views = Field<System.Collections.IDictionary>(system, "m_Views");
        foreach (TileTier tier in new[] { TileTier.Horizon, TileTier.Far, TileTier.Mid })
        {
            Queue(system, TileWorker.Build(tile, tier, version, system.Epoch, items));
            Call(system, "ApplyStep", double.MaxValue);
            object view = views[tile];
            SetField(system, "m_CamX", 2000f);
            SetField(system, "m_CamZ", 0f);
            bool shownAt2km = (bool)Call(system, "ShouldShow", view, false);
            SetField(system, "m_CamX", 3200f);
            bool keptAt32km = (bool)Call(system, "ShouldShow", view, true);
            bool shownAt32km = (bool)Call(system, "ShouldShow", view, false);
            Check(tier == TileTier.Horizon ? shownAt2km && keptAt32km && !shownAt32km : !shownAt2km && !keptAt32km,
                $"{tier}: shown at 2 km {shownAt2km}, kept at 3.2 km {keptAt32km}, shown anew at 3.2 km {shownAt32km}");
            // The far and the horizon tiles cast no shadows (beyond the shadow distance); the middle one's solid parts do.
            var objects = (List<GameObject>)view.GetType().GetField("Objects").GetValue(view);
            int casting = 0;
            foreach (GameObject go in objects)
            {
                if (go.GetComponent<MeshRenderer>().shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On)
                    casting++;
            }

            Check(tier == TileTier.Mid ? casting > 0 : casting == 0, $"{tier}: {casting} of {objects.Count} renderer(s) cast shadows");
            Call(system, "DestroyAll");
            system = Loaded(710f);
            Put(system, new Entity { Index = 812 }, new TsItem { TypeHash = SignCatalog.ById(MarkingTemplates.NoiseCoverId).Hash, Spec = "" });
            views = Field<System.Collections.IDictionary>(system, "m_Views");
            version = system.Store.Snapshot(tile, items = new List<ItemRecord>());
        }

        Call(system, "DestroyAll");
    }
}
