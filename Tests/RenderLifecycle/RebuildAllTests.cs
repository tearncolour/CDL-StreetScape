using System.Collections.Generic;
using CDLC.TS;
using CDLC.TS.Geo;
using Unity.Entities;
using UnityEngine;

// The road paint's experiment (0.12.83) builds every tile again when the paint's normals change: the system's own store marks them all, and a build that was read at the old versions is out of date.
internal static partial class Program
{
    private static void RebuildAllTiles()
    {
        var system = NewSystem();
        Check(system.RebuildAll() == 0, "no items, no tiles to build again");
        float far = Tiles.Size * 5f + 3f;
        TsItem near = new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "", Position = new Vector3(10f, 0f, 10f) };
        TsItem distant = near;
        distant.Position = new Vector3(far, 0f, 10f);
        Put(system, new Entity { Index = 801 }, near);
        Put(system, new Entity { Index = 802 }, distant);
        var taken = new HashSet<TileKey>();
        system.Store.TakeDirty(taken);
        TileKey a = Tiles.Of(10f, 10f), b = Tiles.Of(far, 10f);
        int va = system.Store.VersionOf(a), vb = system.Store.VersionOf(b);
        Check(taken.Count == 2 && !system.Store.HasDirty, "two tiles, taken: clean");
        Check(system.RebuildAll() == 2, "two tiles are to be built again");
        Check(system.Store.HasDirty && system.Store.VersionOf(a) == va + 1 && system.Store.VersionOf(b) == vb + 1, "both are dirty and a build read at the old versions is out of date");
        Call(system, "DestroyAll");
    }
}
