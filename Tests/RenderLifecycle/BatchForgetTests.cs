using System.Collections.Generic;
using System.Linq;
using CDLC.TS;
using CDLC.TS.Geo;
using Unity.Entities;

// The batch interface (0.12.67) destroys a batch's items and creates the new ones in the same frame. The renderer finds a deletion by comparing the number of items, which an equal number of new
// ones hides, so it is told (Forget); what the items had registered with it goes at the same time.
internal static partial class Program
{
    private static void ForgetReplaced()
    {
        var system = NewSystem();
        var screen = new Entity { Index = 801 };
        var board = new Entity { Index = 802 };
        var kept = new Entity { Index = 803 };
        TsItem plain = new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" };
        Put(system, screen, Traffic());
        Put(system, board, plain);
        Put(system, kept, plain);
        Check(system.Records.Count == 3 && system.TrafficScreens.Contains(screen), "three items read, one of them a traffic screen");

        int changes = system.Store.Changes;
        system.Forget(new List<Entity> { screen, board });
        Check(system.Records.Count == 1 && system.Records.ContainsKey(kept) && !system.Records.ContainsKey(board) && !system.Records.ContainsKey(screen), "the replaced items are forgotten at once, the others stay");
        Check(system.Store.Changes != changes, "the store's change count moved, so that the next update raises Revision once");
        Check(!system.TrafficScreens.Contains(screen), "the screen's registration goes with it");

        changes = system.Store.Changes;
        system.Forget(new List<Entity> { screen, new Entity { Index = 899 } });
        Check(system.Store.Changes == changes && system.Records.Count == 1, "an item that is not known is no change");
        Call(system, "DestroyAll");
    }

    // The tool's delete and undo (0.12.78) tell the renderer of the one item they destroy: the count check that would find it runs once in 120 frames, so a deleted sign stayed for one or two seconds.
    private static void ForgetDeleted()
    {
        var system = NewSystem();
        var screen = new Entity { Index = 811 };
        var sign = new Entity { Index = 812 };
        var kept = new Entity { Index = 813 };
        TsItem plain = new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" };
        Put(system, screen, Traffic());
        Put(system, sign, plain);
        Put(system, kept, plain);

        int changes = system.Store.Changes;
        system.Forget(sign);
        Check(system.Records.Count == 2 && !system.Records.ContainsKey(sign) && system.Records.ContainsKey(kept), "the deleted item is gone from the renderer's store at once, the others stay");
        Check(system.Store.Changes != changes, "the store's change count moved, so that its tile is built again");
        system.Forget(screen);
        Check(!system.TrafficScreens.Contains(screen) && system.Records.Count == 1, "the screen's registration goes with it");

        changes = system.Store.Changes;
        system.Forget(new Entity { Index = 899 });
        Check(system.Store.Changes == changes, "an item that is not known is no change");
        Call(system, "DestroyAll");
    }

    // The panel's restore and the save's leak check destroy items (0.12.82): the store is told in the same call, not by the count check that runs once in 120 frames, so that the tool never reads
    // a record whose entity is gone.
    private static void ForgetDestroyed()
    {
        var system = NewSystem();
        var screen = new Entity { Index = 821 };
        var sign = new Entity { Index = 822 };
        var kept = new Entity { Index = 823 };
        var already = new Entity { Index = 824 };
        TsItem plain = new TsItem { TypeHash = SignCatalog.ById("gb.prohib.09").Hash, Spec = "" };
        Put(system, screen, Traffic());
        Put(system, sign, plain);
        Put(system, kept, plain);
        Put(system, already, plain);
        system.EntityManager.DestroyEntity(already);

        int changes = system.Store.Changes;
        system.Destroy(new List<Entity> { screen, sign, already });
        Check(!system.EntityManager.Exists(screen) && !system.EntityManager.Exists(sign) && system.EntityManager.Exists(kept), "the items are destroyed, the others stay");
        Check(system.Records.Count == 1 && system.Records.ContainsKey(kept), "and none of them is left in the renderer's store, one that was gone already included");
        Check(system.Store.Changes != changes && !system.TrafficScreens.Contains(screen), "the store's change count moved and the screen's registration went");
        Call(system, "DestroyAll");
    }

    // The published state of an automatic screen is kept by the item's index (VmsAuto); the world reuses an index with the next version, so a forgotten old screen must not clear the state of the new one.
    private static void AutoIndexReused()
    {
        var system = NewSystem();
        var old = new Entity { Index = 831, Version = 1 };
        var fresh = new Entity { Index = 831, Version = 2 };
        var other = new Entity { Index = 832, Version = 1 };
        SignDef def = SignCatalog.ById("gb.vms.text");
        TsItem auto = new TsItem { TypeHash = def.Hash, Spec = def.Template.Default().With("src", "auto").ToString() };
        var message = new VmsMessage { On = true, Kind = "jam", Class = "warn", Lines = new[] { "SLOW" } };
        VmsAuto.Set(831, message);
        VmsAuto.Set(832, message);
        Put(system, old, auto);
        Put(system, other, auto);
        system.EntityManager.DestroyEntity(old);
        Put(system, fresh, auto);
        Check(Field<HashSet<Entity>>(system, "m_Auto").Count == 3, "three automatic screens known, the old one still among them");

        system.Forget(old);
        Check(VmsAuto.Get(831) != null, "the old screen goes: what the reader published for the index the new screen has is kept");
        system.Forget(other);
        Check(VmsAuto.Get(832) == null, "a screen that is the only one of its index takes its published state with it");
        system.Forget(fresh);
        Check(VmsAuto.Get(831) == null, "and the last one of an index takes it too");
        Call(system, "DestroyAll");
    }
}
