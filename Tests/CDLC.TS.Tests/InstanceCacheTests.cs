using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The board cache (0.7.1): a settings string finds its instance by the text it was written as, and every way of writing the same settings finds the same instance.</summary>
    internal static class InstanceCacheTests
    {
        public static void RunAll()
        {
            Program.Run("board cache: every way of writing the same settings is the same board", SameBoard);
            Program.Run("board cache: different settings are different boards, and a cleared cache makes them again", Different);
        }

        private static SignDef Def(string id)
        {
            SignDef def = SignCatalog.ById(id);
            if (def == null)
                throw new InvalidOperationException("no sign " + id);
            return def;
        }

        private static void SameBoard()
        {
            SignInstances.ClearCaches();
            SignDef def = Def("guide.exit.advance");
            GuideSpec defaults = def.Template.Default();
            string canonical = defaults.With("to1", "北京").With("dist", "500").ToString();

            SignInstance a = SignInstances.Of(def, canonical);
            SignInstance b = SignInstances.Of(def, canonical);
            Program.Check(ReferenceEquals(a, b), "the same text twice is one board");

            // Other ways to write the same settings: the keys in another order, a key written that has its default, an extra key that is not a field, spaces.
            string reordered = "dist=500;to1=北京";
            string withDefaults = canonical + ";speed=auto";
            SignInstance c = SignInstances.Of(def, reordered), d = SignInstances.Of(def, withDefaults);
            Program.Check(ReferenceEquals(a, c) && ReferenceEquals(a, d), "a string that says the same in other words is the same board: " + (ReferenceEquals(a, c)) + " / " + ReferenceEquals(a, d));
            Program.Check(ReferenceEquals(SignInstances.Of(def, reordered), c), "and it is found by its own text the second time");

            // Null and empty are the defaults.
            SignInstance n = SignInstances.Of(def, null), e = SignInstances.Of(def, ""), f = SignInstances.Of(def, defaults.ToString());
            Program.Check(ReferenceEquals(n, e) && ReferenceEquals(n, f), "null, empty and the written defaults are one board");

            // A classic sign has one board whatever the text.
            SignDef classic = null;
            foreach (SignDef candidate in SignCatalog.All)
            {
                if (candidate.Template == null)
                {
                    classic = candidate;
                    break;
                }
            }

            if (classic != null)
                Program.Check(ReferenceEquals(SignInstances.Of(classic, null), SignInstances.Of(classic, "x=1")), "a sign without a template ignores the text");

            // The key of the board is the canonical one whatever text found it.
            Program.Check(a.Key == c.Key && a.Key == d.Key && a.Key.StartsWith(def.Id + "#", StringComparison.Ordinal), "the key is the canonical text: " + a.Key);
            Program.Check(SignInstances.FromKey(a.Key) != null && SignInstances.FromKey(a.Key).Key == a.Key, "and a key finds its board again");
        }

        private static void Different()
        {
            SignInstances.ClearCaches();
            SignDef def = Def("guide.exit.advance");
            GuideSpec defaults = def.Template.Default();
            SignInstance one = SignInstances.Of(def, defaults.With("to1", "北京").ToString());
            SignInstance two = SignInstances.Of(def, defaults.With("to1", "上海").ToString());
            Program.Check(!ReferenceEquals(one, two) && one.Key != two.Key, "different settings are different boards");
            Program.Check(ReferenceEquals(one, SignInstances.Of(def, defaults.With("to1", "北京").ToString())), "and each is found again");

            // The same text on two kinds of sign is two boards (the kind is in the cache's key).
            SignDef other = Def("guide.exit.direction");
            Program.Check(!ReferenceEquals(SignInstances.Of(def, "to1=北京"), SignInstances.Of(other, "to1=北京")), "the same text on another kind is another board");

            // After a clear the board is made again (a new object), with the same key and size.
            string key = one.Key;
            int texW = one.TexW, texH = one.TexH;
            SignInstances.ClearCaches();
            SignInstance again = SignInstances.Of(def, defaults.With("to1", "北京").ToString());
            Program.Check(!ReferenceEquals(one, again) && again.Key == key && again.TexW == texW && again.TexH == texH, "a cleared cache makes it again, the same");

            // Many different texts: more than the cache holds (1500 a generation) keep working, and the ones used again stay the same object.
            SignInstance kept = SignInstances.Of(def, defaults.With("to1", "留").ToString());
            var all = new List<SignInstance>();
            for (int i = 0; i < 3300; i++)
            {
                all.Add(SignInstances.Of(def, defaults.With("dist", (i % 99 + 1).ToString()).With("to1", "城" + i).ToString()));
                if (i % 100 == 0)
                    SignInstances.Of(def, defaults.With("to1", "留").ToString());
            }

            Program.Check(all.Count == 3300 && all[0].Key != all[1].Key, "thousands of boards are made");
            Program.Check(ReferenceEquals(kept, SignInstances.Of(def, defaults.With("to1", "留").ToString())), "one that is used all the time stays the same object while thousands pass");
        }
    }
}
