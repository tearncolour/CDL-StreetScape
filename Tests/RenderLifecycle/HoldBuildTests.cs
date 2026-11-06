using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CDLC.TS;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using Unity.Entities;
using UnityEngine;

// The road follow writes the items of a tile over some frames; a build of the tile that starts meanwhile is thrown away at the next write (0.12.83). ChunkSystem keeps a held tile's build back for at most
// kMaxHoldFrames, and counts the finished builds it threw away.
internal static partial class Program
{
    private static TsItem SignAt(float x, float z) => new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "", Position = new Vector3(x, 0f, z) };

    private static void HoldBuilds()
    {
        var system = NewSystem();
        Put(system, new Entity { Index = 901 }, SignAt(10f, 10f));
        TileKey tile = Tiles.Of(10f, 10f);
        var wishes = Field<List<TileWish>>(system, "m_ToBuild");
        var jobs = Field<IDictionary>(system, "m_Jobs");
        bool holding = true;
        system.HoldBuild = key => holding && key.Equals(tile);
        SetField(system, "m_Frame", 10);
        wishes.Add(new TileWish { Key = tile, Tier = TileTier.Near, Distance = 0f });
        Call(system, "StartJobs");
        Check(jobs.Count == 0 && wishes.Count == 1, "a tile the follow is writing is not built, and its wish is kept for later");
        SetField(system, "m_Frame", 99);
        Call(system, "StartJobs");
        Check(jobs.Count == 0 && wishes.Count == 1, "still held 89 frames after it was first asked");
        SetField(system, "m_Frame", 100);
        Call(system, "StartJobs");
        Check(jobs.Count == 1 && wishes.Count == 0, "built after 90 frames whatever the follow says");
        Call(system, "DestroyAll");

        // A tile that is not held is built at once, and a hold that is over starts nothing it keeps.
        system = NewSystem();
        Put(system, new Entity { Index = 902 }, SignAt(10f, 10f));
        wishes = Field<List<TileWish>>(system, "m_ToBuild");
        jobs = Field<IDictionary>(system, "m_Jobs");
        holding = true;
        system.HoldBuild = key => holding;
        SetField(system, "m_Frame", 10);
        wishes.Add(new TileWish { Key = tile, Tier = TileTier.Near, Distance = 0f });
        Call(system, "StartJobs");
        holding = false;
        SetField(system, "m_Frame", 12);
        Call(system, "StartJobs");
        Check(jobs.Count == 1 && wishes.Count == 0, "the follow is done with the tile: built at the next look");
        Call(system, "DestroyAll");

        // The window of a tile starts again each time the follow holds it: a hold that was let go of is not counted on from the first.
        system = NewSystem();
        Put(system, new Entity { Index = 903 }, SignAt(10f, 10f));
        wishes = Field<List<TileWish>>(system, "m_ToBuild");
        jobs = Field<IDictionary>(system, "m_Jobs");
        holding = true;
        system.HoldBuild = key => holding;
        SetField(system, "m_Frame", 10);
        wishes.Add(new TileWish { Key = tile, Tier = TileTier.Near, Distance = 0f });
        Call(system, "StartJobs");
        holding = false;
        SetField(system, "m_Frame", 50);
        Call(system, "Held", tile);
        holding = true;
        SetField(system, "m_Frame", 120);
        Check((bool)Call(system, "Held", tile), "held again at frame 120: the window began there, not at frame 10");
        SetField(system, "m_Frame", 209);
        Check((bool)Call(system, "Held", tile), "and is still held 89 frames later");
        SetField(system, "m_Frame", 210);
        Check(!(bool)Call(system, "Held", tile), "and let go at 90");
        Call(system, "DestroyAll");

        // Without a follow nothing is held.
        system = NewSystem();
        Put(system, new Entity { Index = 904 }, SignAt(10f, 10f));
        wishes = Field<List<TileWish>>(system, "m_ToBuild");
        jobs = Field<IDictionary>(system, "m_Jobs");
        wishes.Add(new TileWish { Key = tile, Tier = TileTier.Near, Distance = 0f });
        Call(system, "StartJobs");
        Check(system.HoldBuild == null && jobs.Count == 1, "no hold, built at once");
        Call(system, "DestroyAll");
    }

    private static void SupersededBuilds()
    {
        var system = NewSystem();
        Put(system, new Entity { Index = 911 }, SignAt(10f, 10f));
        TileKey tile = Tiles.Of(10f, 10f);
        var wishes = Field<List<TileWish>>(system, "m_ToBuild");
        var jobs = Field<IDictionary>(system, "m_Jobs");
        wishes.Add(new TileWish { Key = tile, Tier = TileTier.Near, Distance = 0f });
        Call(system, "StartJobs");
        Check(jobs.Count == 1 && system.SupersededTotal == 0, "a build started");
        // An item of the tile is written while it is built.
        Put(system, new Entity { Index = 912 }, SignAt(20f, 20f));
        WaitForJobs(jobs);
        Call(system, "CollectJobs");
        Check(system.SupersededTotal == 1 && jobs.Count == 0, "the finished build was of the tile as it was: thrown away and counted");
        Check(Field<Queue<TileResult>>(system, "m_Ready").Count == 0, "and not made into objects");

        // A build of the tile as it is now is kept.
        wishes = Field<List<TileWish>>(system, "m_ToBuild");
        wishes.Add(new TileWish { Key = tile, Tier = TileTier.Near, Distance = 0f });
        Call(system, "StartJobs");
        WaitForJobs(jobs);
        Call(system, "CollectJobs");
        Check(system.SupersededTotal == 1 && Field<Queue<TileResult>>(system, "m_Ready").Count == 1, "a build of the tile as it is: kept, and not counted");
        Call(system, "DestroyAll");
    }

    private static void WaitForJobs(IDictionary jobs)
    {
        var tasks = new List<Task>();
        foreach (object job in jobs.Values)
            tasks.Add((Task)job.GetType().GetField("Task", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(job));
        Task.WaitAll(tasks.ToArray());
    }
}
