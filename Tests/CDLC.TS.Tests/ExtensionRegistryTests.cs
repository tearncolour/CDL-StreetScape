using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.93 (API 2: AutoRoadLiner beside the Realistic Information Extension): the rules of the registry the engine side (TsExtension) asks, with the entity as an int: nothing registered answers
    /// as before, any extension saying yes is yes, a revision change of any one changes the folded one, the poles of all of them go into the same lists in the order they joined, the same name
    /// replaces its hooks, and only an extension that says it supplies live content turns the screens on.
    /// </summary>
    internal static class ExtensionRegistryTests
    {
        public static void RunAll()
        {
            Program.Run("extension registry: nothing registered answers as before", Empty);
            Program.Run("extension registry: any extension saying yes is yes", AnyYes);
            Program.Run("extension registry: the revision changes when the revision of any extension does", Revision);
            Program.Run("extension registry: the poles of every extension, in the order they joined", Hosts);
            Program.Run("extension registry: the same name replaces its hooks and keeps its place", Replace);
            Program.Run("extension registry: only live content turns the screens on", LiveContentFlag);
            Program.Run("extension registry: a hook that is not given is not asked", Missing);
        }

        private static ExtensionRegistry<int>.Hooks Named(string name) => new ExtensionRegistry<int>.Hooks { Name = name };

        private static void Empty()
        {
            var registry = new ExtensionRegistry<int>();
            Program.Check(!registry.HidesMarking(1) && !registry.IsReservedRoad(1), "no extension hides or reserves anything");
            Program.Check(registry.FoldRevision(-1) == -1 && registry.FoldRevision(12345) == 12345, "the revision it is given comes back unchanged: the key is as it was without extensions");
            var posts = new List<HostCandidate>();
            var arms = new List<HostArm>();
            registry.AddHosts(0f, 0f, 10f, posts, arms);
            Program.Check(posts.Count == 0 && arms.Count == 0, "no poles are added");
            Program.Check(!registry.ProvidesLiveContent && registry.Names.Count == 0, "no live content, no names");
        }

        private static void AnyYes()
        {
            var registry = new ExtensionRegistry<int>();
            var first = Named("first");
            first.HidesMarking = lane => lane == 7;
            first.IsReservedRoad = road => road == 100;
            var second = Named("second");
            second.HidesMarking = lane => lane == 8;
            second.IsReservedRoad = road => road == 200;
            registry.Add(first);
            registry.Add(second);
            Program.Check(registry.HidesMarking(7) && registry.HidesMarking(8) && !registry.HidesMarking(9), "a lane either hides is hidden, another is not");
            Program.Check(registry.IsReservedRoad(100) && registry.IsReservedRoad(200) && !registry.IsReservedRoad(300), "a road either reserves is reserved, another is not");
        }

        private static void Revision()
        {
            int a = 1, b = 1;
            var registry = new ExtensionRegistry<int>();
            var first = Named("first");
            first.ReservedRevision = () => a;
            var second = Named("second");
            second.ReservedRevision = () => b;
            registry.Add(first);
            registry.Add(second);
            int seed = 5, before = registry.FoldRevision(seed);
            a = 2;
            int afterFirst = registry.FoldRevision(seed);
            b = 2;
            int afterSecond = registry.FoldRevision(seed);
            Program.Check(before != afterFirst && afterFirst != afterSecond && before != afterSecond, "a change of either revision changes the folded one, and of both another");
            a = 1;
            b = 1;
            Program.Check(registry.FoldRevision(seed) == before, "the same revisions give the same number again");
            Program.Check(registry.FoldRevision(seed) != registry.FoldRevision(seed + 1), "the seed (the single point's own revision) counts too");

            // One extension's revision moving up by one while another's moves down by one must not cancel out.
            a = 2;
            b = 0;
            int crossed = registry.FoldRevision(seed);
            a = 1;
            b = 1;
            Program.Check(crossed != registry.FoldRevision(seed), "one up and one down is not the same as none");
        }

        private static void Hosts()
        {
            var registry = new ExtensionRegistry<int>();
            var first = Named("first");
            first.OtherHosts = (x, z, r, posts, arms) => posts.Add(new HostCandidate { Id = 1, X = x });
            var second = Named("second");
            second.OtherHosts = (x, z, r, posts, arms) =>
            {
                posts.Add(new HostCandidate { Id = 2, X = x });
                arms.Add(new HostArm { Id = 3, X = x });
            };
            registry.Add(first);
            registry.Add(second);
            var postList = new List<HostCandidate> { new HostCandidate { Id = 0 } };
            var armList = new List<HostArm>();
            registry.AddHosts(4f, 0f, 10f, postList, armList);
            Program.Check(postList.Count == 3 && postList[0].Id == 0 && postList[1].Id == 1 && postList[2].Id == 2, "what was in the list stays first, then each extension's in the order they joined");
            Program.Check(armList.Count == 1 && armList[0].Id == 3 && postList[1].X == 4f, "the arms and the place asked reach the extension");
        }

        private static void Replace()
        {
            var registry = new ExtensionRegistry<int>();
            var first = Named("first");
            first.HidesMarking = lane => true;
            var second = Named("second");
            second.IsReservedRoad = road => true;
            registry.Add(first);
            registry.Add(second);
            var again = Named("first");
            again.IsReservedRoad = road => road == 5;
            registry.Add(again);
            Program.Check(registry.Names.Count == 2 && registry.Names[0] == "first" && registry.Names[1] == "second", "two names, the replaced one keeps its place");
            Program.Check(!registry.HidesMarking(1), "the old hook of that name is gone");
            Program.Check(registry.IsReservedRoad(5) && registry.IsReservedRoad(6), "the new one answers, and so does the other extension");
            Program.Check(Refused(registry, null), "no hooks are refused");
            Program.Check(Refused(registry, new ExtensionRegistry<int>.Hooks()), "hooks without a name are refused");
            Program.Check(registry.Names.Count == 2, "and nothing was added");
        }

        private static bool Refused(ExtensionRegistry<int> registry, ExtensionRegistry<int>.Hooks hooks)
        {
            try
            {
                registry.Add(hooks);
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }

        private static void LiveContentFlag()
        {
            var registry = new ExtensionRegistry<int>();
            registry.Add(Named("lines"));
            Program.Check(!registry.ProvidesLiveContent, "an extension that draws lines only does not switch the screens on");
            var source = Named("traffic");
            source.ProvidesLiveContent = true;
            registry.Add(source);
            Program.Check(registry.ProvidesLiveContent, "one that supplies the content does");
            var replaced = Named("traffic");
            registry.Add(replaced);
            Program.Check(!registry.ProvidesLiveContent, "registered again without it, it does not any more");
        }

        private static void Missing()
        {
            var registry = new ExtensionRegistry<int>();
            registry.Add(Named("bare"));
            var posts = new List<HostCandidate>();
            registry.AddHosts(0f, 0f, 1f, posts, new List<HostArm>());
            Program.Check(!registry.HidesMarking(1) && !registry.IsReservedRoad(1) && registry.FoldRevision(9) == 9 && posts.Count == 0, "hooks that are null are skipped, not called");
        }
    }
}
