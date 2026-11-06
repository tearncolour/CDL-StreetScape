using System;
using System.Collections.Generic;
using System.Threading;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// SignCatalog.ByHash as a table lookup. The walk it replaced (every sign, its id hashed again, 4 to 13 microseconds a call) is kept here as the reference: the owner's 0.12.75 log had ItemRoadSystem's
    /// round at 1.26 ms against 0.14 ms in 0.12.73, and the one new thing each item of a round did was this walk (HostKindOf in NoteLinked).
    /// </summary>
    internal static class SignHashIndexTests
    {
        public static void RunAll()
        {
            Program.Run("sign hash index: the table gives what the walk gave", AgainstTheWalk);
            Program.Run("sign hash index: first of two with one hash, the legacy templates, the old test board", Corners);
            Program.Run("sign hash index: a table made again gets its own index, and eight threads agree", Rebuilt);
        }

        private static SignDef Walk(uint hash)
        {
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Hash == hash)
                    return def;
            }

            if (hash == SignCatalog.HashOf(SignCatalog.LegacyTestExit))
                return SignCatalog.ById("guide.exit.advance");
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Template == null)
                    continue;
                foreach (string old in def.Template.LegacyIds)
                {
                    if (SignCatalog.HashOf(old) == hash)
                        return def;
                }
            }

            return null;
        }

        private static void AgainstTheWalk()
        {
            int differing = 0;
            foreach (SignDef def in SignCatalog.All)
            {
                if (!ReferenceEquals(SignCatalog.ByHash(def.Hash), Walk(def.Hash)) || !ReferenceEquals(SignCatalog.ByHash(def.Hash), def))
                    differing++;
            }

            Program.Check(differing == 0, $"every sign of the catalogue is found by its hash, as the walk found it ({differing} differ of {SignCatalog.All.Count})");
            var odd = new List<uint> { 0u, 1u, 7u, 0xDEADBEEFu, uint.MaxValue, SignCatalog.HashOf("no.such.sign"), SignCatalog.HashOf("") };
            foreach (SignDef def in SignCatalog.All)
                odd.Add(def.Hash ^ 1u);
            int wrong = 0;
            foreach (uint hash in odd)
            {
                if (!ReferenceEquals(SignCatalog.ByHash(hash), Walk(hash)))
                    wrong++;
            }

            Program.Check(wrong == 0, $"{odd.Count} hashes that are no sign's, and the walk's answer for each ({wrong} differ)");
        }

        private static void Corners()
        {
            var first = new SignDef { Id = "twin.sign" };
            var second = new SignDef { Id = "twin.sign" };
            Dictionary<uint, SignDef> table = SignCatalog.HashTable(new[] { first, second, new SignDef { Id = "other.sign" } });
            Program.Check(table.Count == 2 && ReferenceEquals(table[SignCatalog.HashOf("twin.sign")], first), "of two signs with one hash the first is the one a walk finds");

            var early = new SignDef { Id = "early", Template = new GuideTemplate { LegacyIds = new[] { "old.sign", "older.sign" } } };
            var late = new SignDef { Id = "late", Template = new GuideTemplate { LegacyIds = new[] { "old.sign", "other.sign" } } };
            Dictionary<uint, SignDef> legacyTable = SignCatalog.LegacyTable(new[] { early, late });
            Program.Check(legacyTable.Count == 3 && ReferenceEquals(legacyTable[SignCatalog.HashOf("old.sign")], early) && ReferenceEquals(legacyTable[SignCatalog.HashOf("other.sign")], late) && ReferenceEquals(legacyTable[SignCatalog.HashOf("older.sign")], early),
                "an old id two templates claim belongs to the first, as a walk over the templates finds it; the others to their own");

            int legacy = 0, wrong = 0;
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Template == null)
                    continue;
                foreach (string old in def.Template.LegacyIds)
                {
                    legacy++;
                    if (!ReferenceEquals(SignCatalog.ByHash(SignCatalog.HashOf(old)), Walk(SignCatalog.HashOf(old))) || SignCatalog.ByHash(SignCatalog.HashOf(old)) == null)
                        wrong++;
                }
            }

            Program.Check(wrong == 0, $"the {legacy} ids that templates replaced are found by their old hash, as the walk found them ({wrong} differ)");
            SignDef exit = SignCatalog.ByHash(SignCatalog.HashOf(SignCatalog.LegacyTestExit));
            Program.Check(exit != null && ReferenceEquals(exit, SignCatalog.ById("guide.exit.advance")), "the first builds' test board is the exit advance board");
            Program.Check(SignCatalog.ById("nothing") == null && SignCatalog.ByHash(SignCatalog.HashOf("nothing")) == null, "an id and a hash that are nothing give null");
        }

        private static void Rebuilt()
        {
            System.Reflection.FieldInfo every = typeof(SignCatalog).GetField("s_Every", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            System.Reflection.FieldInfo templates = typeof(SignCatalog).GetField("s_Templates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Program.Check(every != null && templates != null, "the catalogue's tables are where the test looks for them");
            if (every == null || templates == null)
                return;
            uint hash = SignCatalog.All[SignCatalog.All.Count / 2].Hash;
            SignDef old = SignCatalog.ByHash(hash);
            int stale = 0, differing = 0;
            for (int round = 0; round < 6; round++)
            {
                every.SetValue(null, null);
                templates.SetValue(null, null);
                var seen = new SignDef[8];
                var start = new Barrier(8);
                var threads = new Thread[8];
                for (int t = 0; t < 8; t++)
                {
                    int index = t;
                    threads[t] = new Thread(() =>
                    {
                        start.SignalAndWait();
                        seen[index] = SignCatalog.ByHash(hash);
                    });
                    threads[t].Start();
                }

                foreach (Thread thread in threads)
                    thread.Join();
                foreach (SignDef found in seen)
                {
                    if (found == null || !ReferenceEquals(found, seen[0]))
                        differing++;
                }

                if (!ReferenceEquals(seen[0], SignCatalog.ById(seen[0].Id)))
                    stale++;
            }

            Program.Check(differing == 0, $"eight threads asking at once all got one sign, 6 rounds over ({differing} did not)");
            Program.Check(stale == 0, $"the sign found is the one of the table that stands now, not of the one the index was made from ({stale} stale)");
            SignDef fresh = SignCatalog.ByHash(hash);
            Program.Check(fresh != null && !ReferenceEquals(fresh, old) && ReferenceEquals(fresh, SignCatalog.ById(fresh.Id)), "after the tables were made again the index follows them");
        }
    }
}
