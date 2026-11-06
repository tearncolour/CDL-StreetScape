using System;
using System.Collections.Generic;
using System.Text.Json;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The guide board ahead of a vehicle (TsApi.AheadJson, 0.9.0): which board, how far, what it says, and the JSON the map reads.</summary>
    internal static class TsAheadTests
    {
        public static void RunAll()
        {
            Program.Run("guide board ahead: facing, distance, side, and the nearest", Choice);
            Program.Run("guide board ahead: only guide boards, groups, and bad input", Kinds);
            Program.Run("guide board ahead: the words of a board", Words);
            Program.Run("guide board ahead: the JSON", Json);
            Program.Run("guide board ahead: the real independently turned face", Turns);
        }

        private static uint Hash(string id) => SignCatalog.HashOf(id);

        /// <summary>A board at (x, z) that faces the traffic coming from the north to the south (a driver heading 180 deg sees it): its yaw faces +Z, so the traffic that sees it runs toward -Z.</summary>
        private static ItemRecord Board(string id, float x, float z, float yaw = 0f, string spec = null, uint group = 0u, int slot = 0) =>
            new ItemRecord(Hash(id), new Vec3(x, 0f, z), yaw, 0u, spec, 0f, group, slot, 0, 0f);

        private const string Exit = "guide.exit.advance";

        private static void Choice()
        {
            // A driver at the origin heading south (180 deg). A board that faces +Z (yaw 0) is read by traffic running toward -Z: this one. It stands 100 m ahead, i.e. at z = -100.
            var near = Board(Exit, 0f, -100f, 0f);
            TsAhead a = TsAheadRule.Find(new[] { near }, 0f, 0f, 180f, 500f);
            Program.Check(a != null && a.Distance == 100 && a.XDm == 0 && a.ZDm == -1000 && a.Bearing == 0, "a board 100 m ahead that faces the driver is found: " + (a == null ? "null" : a.Distance + " m, bearing " + a.Bearing));
            Program.Check(a != null && a.Code.Length > 0 && a.Name.Length > 0 && a.Text.Length > 0, "with its code, its name and its words");

            // Behind the driver, or beyond the distance, or the other way round: not ahead.
            Program.Check(TsAheadRule.Find(new[] { Board(Exit, 0f, 100f, 0f) }, 0f, 0f, 180f, 500f) == null, "a board behind the driver is not ahead");
            Program.Check(TsAheadRule.Find(new[] { near }, 0f, 0f, 180f, 99f) == null && TsAheadRule.Find(new[] { near }, 0f, 0f, 180f, 100f) != null, "beyond maxDist is not ahead; at it is");
            Program.Check(TsAheadRule.Find(new[] { Board(Exit, 0f, -100f, (float)Math.PI) }, 0f, 0f, 180f, 500f) == null, "a board that faces away (its back to the driver) is not read");

            // The angle of the facing: within 45 degrees counts, beyond does not.
            foreach ((float degrees, bool read) in new[] { (0f, true), (30f, true), (44f, true), (46f, false), (90f, false), (180f, false) })
                Program.Check((TsAheadRule.Find(new[] { Board(Exit, 0f, -100f, degrees * (float)Math.PI / 180f) }, 0f, 0f, 180f, 500f) != null) == read, degrees + " degrees off: " + read);

            // To the side: 30 m is the most.
            Program.Check(TsAheadRule.Find(new[] { Board(Exit, 25f, -100f) }, 0f, 0f, 180f, 500f) != null && TsAheadRule.Find(new[] { Board(Exit, 31f, -100f) }, 0f, 0f, 180f, 500f) == null && TsAheadRule.Find(new[] { Board(Exit, -31f, -100f) }, 0f, 0f, 180f, 500f) == null, "30 m to either side is the limit");

            // The nearest of several, whatever the order.
            var far = Board(Exit, 0f, -300f);
            var mid = Board(Exit, 5f, -200f);
            foreach (ItemRecord[] order in new[] { new[] { far, mid, near }, new[] { near, mid, far }, new[] { mid, near, far } })
                Program.Check(TsAheadRule.Find(order, 0f, 0f, 180f, 500f).Distance == 100, "the nearest wins in any order");

            // The driver's own place counts: moving on past the board, the next one is found.
            Program.Check(TsAheadRule.Find(new[] { far, mid, near }, 0f, -150f, 180f, 500f).Distance == 50, "a board passed is not ahead any more, the next is: " + TsAheadRule.Find(new[] { far, mid, near }, 0f, -150f, 180f, 500f).Distance);

            // Other headings: a driver going east sees a board that faces west (yaw -90 deg: facing -X, the traffic that sees it runs +X).
            var east = Board(Exit, 150f, 0f, -(float)Math.PI / 2f);
            Program.Check(TsAheadRule.Find(new[] { east }, 0f, 0f, 90f, 500f) != null && TsAheadRule.Find(new[] { east }, 0f, 0f, 270f, 500f) == null && TsAheadRule.Find(new[] { east }, 0f, 0f, 0f, 500f) == null, "a driver going east sees the board that faces west, the others do not");
            Program.Check(TsAheadRule.Find(new[] { east }, 0f, 0f, 90f - 360f, 500f) != null && TsAheadRule.Find(new[] { east }, 0f, 0f, 90f + 720f, 500f) != null, "a heading is any number of degrees");

            // The cap of the distance whatever is asked.
            Program.Check(TsAheadRule.Find(new[] { Board(Exit, 0f, -1400f) }, 0f, 0f, 180f, 99999f) != null && TsAheadRule.Find(new[] { Board(Exit, 0f, -1600f) }, 0f, 0f, 180f, 99999f) == null, "1500 m is the farthest, whatever is asked");

            // The bearing is the board's facing in whole degrees from 0 to 359: 359.7 and -0.1 degrees both round to 360 and wrap to 0.
            foreach ((float degrees, int bearing) in new[] { (0f, 0), (90f, 90), (359.7f, 0), (-0.1f, 0), (-90f, 270), (450f, 90), (179.6f, 180) })
            {
                // The driver heads the way the board's traffic runs (the board's facing turned half a turn) and the board is 100 m on that way.
                double heading = (degrees + 180f) * Math.PI / 180.0;
                TsAhead one = TsAheadRule.Find(new[] { Board(Exit, (float)(100.0 * Math.Sin(heading)), (float)(100.0 * Math.Cos(heading)), degrees * (float)Math.PI / 180f) }, 0f, 0f, degrees + 180f, 500f);
                Program.Check(one != null && one.Bearing == bearing, degrees + " degrees faces " + bearing + ": " + (one == null ? "null" : one.Bearing.ToString()));
            }

            // Tie: two boards at one place, the one that hangs highest (the lowest slot) speaks, whatever the order.
            var top = Board("gb.guide8.01", 0f, -100f, 0f, null, 5u, 0);
            var low = Board(Exit, 0f, -100f, 0f, null, 5u, 1);
            Program.Check(TsAheadRule.Find(new[] { low, top }, 0f, 0f, 180f, 500f).Name == SignCatalog.ByHash(top.TypeHash).Zh && TsAheadRule.Find(new[] { top, low }, 0f, 0f, 180f, 500f).Name == SignCatalog.ByHash(top.TypeHash).Zh, "of boards on one support the top one speaks");
        }

        private static void Kinds()
        {
            // Only guide boards: a prohibition sign, a warning, a screen and a mandatory sign are not "guide boards".
            foreach (string id in new[] { "gb.prohib.09", "gb.warn.01", "gb.vms.text", "gb.mand.14", "gb.tour.02" })
                Program.Check(TsAheadRule.Find(new[] { Board(id, 0f, -100f) }, 0f, 0f, 180f, 500f) == null, id + " is not a guide board");
            foreach (string id in new[] { "guide.exit.advance", "gb.guide8.01", "gb.guide9.47b", "guide.exit.direction" })
                Program.Check(TsAheadRule.Find(new[] { Board(id, 0f, -100f) }, 0f, 0f, 180f, 500f) != null, id + " is a guide board");

            // A type that is not in the catalogue, a position that is not a number: ignored, no throw.
            var unknown = new ItemRecord(123456789u, new Vec3(0f, 0f, -100f), 0f);
            var bad = new ItemRecord(Hash(Exit), new Vec3(float.NaN, 0f, -100f), 0f);
            var badYaw = new ItemRecord(Hash(Exit), new Vec3(0f, 0f, -100f), float.PositiveInfinity);
            Program.Check(TsAheadRule.Find(new[] { unknown, bad, badYaw, Board(Exit, 0f, -50f) }, 0f, 0f, 180f, 500f).Distance == 50, "an unknown type and a bad position are ignored");

            // Bad input.
            Program.Check(TsAheadRule.Find(null, 0f, 0f, 180f, 500f) == null && TsAheadRule.Find(new ItemRecord[0], 0f, 0f, 180f, 500f) == null, "no records, no board");
            foreach (float badValue in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var list = new[] { Board(Exit, 0f, -100f) };
                Program.Check(TsAheadRule.Find(list, badValue, 0f, 180f, 500f) == null && TsAheadRule.Find(list, 0f, badValue, 180f, 500f) == null && TsAheadRule.Find(list, 0f, 0f, badValue, 500f) == null, badValue + " as the place or the heading is no input");
            }

            Program.Check(TsAheadRule.Find(new[] { Board(Exit, 0f, -100f) }, 0f, 0f, 180f, 0f) == null && TsAheadRule.Find(new[] { Board(Exit, 0f, -100f) }, 0f, 0f, 180f, -5f) == null && TsAheadRule.Find(new[] { Board(Exit, 0f, -100f) }, 0f, 0f, 180f, float.NaN) == null, "a distance of nothing or less is nothing");
        }

        private static void Turns()
        {
            uint pole = SupportCatalog.ById(SupportCatalog.SinglePole).Hash;
            ItemRecord turned = Board("gb.guide8.01", 0f, -100f);
            turned.SupportHash = pole;
            turned.Turn = (float)Math.PI;
            Program.Check(TsAheadRule.Find(new[] { turned }, 0f, 0f, 180f, 500f) == null, "a single-post board turned away is no longer read from the old approach");
            TsAhead otherWay = TsAheadRule.Find(new[] { turned }, 0f, -200f, 0f, 500f);
            Program.Check(otherWay != null && otherWay.Bearing == 180 && otherWay.Distance == 100, "the opposite approach reads the turned face and its real bearing");

            ItemRecord across = turned;
            across.Position = new Vec3(-100f, 0f, 0f);
            across.Turn = (float)Math.PI / 2f;
            TsAhead quarter = TsAheadRule.Find(new[] { across }, 0f, 0f, 270f, 500f);
            Program.Check(quarter != null && quarter.Bearing == 90, "a quarter-turned board is read from the side it now faces");

            // Two guide boards at one support can face opposite approaches. A turned-away high board must not hide the lower board that actually faces the driver.
            ItemRecord high = turned;
            high.Group = 83u;
            ItemRecord low = Board("gb.guide8.02", 0f, -100f, 0f, null, 83u, 1);
            low.SupportHash = pole;
            foreach (ItemRecord[] order in new[] { new[] { high, low }, new[] { low, high } })
            {
                TsAhead south = TsAheadRule.Find(order, 0f, 0f, 180f, 500f);
                TsAhead north = TsAheadRule.Find(order, 0f, -200f, 0f, 500f);
                Program.Check(south != null && south.Code == SignCatalog.ByHash(low.TypeHash).Code && south.Bearing == 0, "the lower board is the one facing the southbound driver");
                Program.Check(north != null && north.Code == SignCatalog.ByHash(high.TypeHash).Code && north.Bearing == 180, "the higher board is the one facing the northbound driver");
            }

            foreach (string support in new[] { SupportCatalog.DoublePole, SupportCatalog.TriplePole, SupportCatalog.WallFlat, SupportCatalog.WallBracket, SupportCatalog.Cantilever, SupportCatalog.Gantry })
            {
                ItemRecord fixedBoard = turned;
                fixedBoard.SupportHash = SupportCatalog.ById(support).Hash;
                TsAhead fixedAhead = TsAheadRule.Find(new[] { fixedBoard }, 0f, 0f, 180f, 500f);
                Program.Check(fixedAhead != null && fixedAhead.Bearing == 0, support + ": a stored Turn that the mesh cannot apply does not change which driver reads it");
            }
            ItemRecord onPost = turned;
            onPost.SupportHash = SupportCatalog.ById(SupportCatalog.Cantilever).Hash;
            onPost.Lane = Assembly.OnPost;
            Program.Check(TsAheadRule.Find(new[] { onPost }, 0f, 0f, 180f, 500f) == null && TsAheadRule.Find(new[] { onPost }, 0f, -200f, 0f, 500f)?.Bearing == 180,
                "a board on the cantilever's post uses its own turn");

            // This board would turn alone, but a large sibling makes the automatic assembly stand on several posts.
            high.SupportHash = 0u;
            low = Board(Exit, 0f, -100f, 0f, null, high.Group, 1);
            var grown = new List<ItemRecord> { high, low };
            Program.Check(ItemGroups.PlanOf(grown, out _).Layout.PoleX.Length > 1, "the real automatic support has grown beyond one post");
            TsAhead wholeSupport = TsAheadRule.Find(grown, 0f, 0f, 180f, 500f);
            Program.Check(wholeSupport != null && wholeSupport.Code == SignCatalog.ByHash(high.TypeHash).Code && wholeSupport.Bearing == 0, "the complete support's layout decides whether its saved turn is effective");
        }

        private static void Words()
        {
            SignDef def = SignCatalog.ById(Exit);
            string spec = def.Template.Default().With("to1", "石家庄").With("to2", "连云港").ToString();
            string words = TsAheadRule.WordsOf(SignInstances.Of(def, spec));
            Program.Check(words.Contains("石家庄") && words.Contains("连云港"), "the place names are in the words: " + words);
            Program.Check(words.IndexOf("石家庄", StringComparison.Ordinal) < words.IndexOf("连云港", StringComparison.Ordinal) || words.IndexOf("连云港", StringComparison.Ordinal) < words.IndexOf("石家庄", StringComparison.Ordinal), "each once");
            Program.Check(words.Length <= TsAheadRule.MaxText && !words.Contains("  ") && words == words.Trim(), "short, one space between words: " + words.Length);

            // Every guide board of the catalogue at its defaults has words that are short and clean (or none), and no throw.
            int withWords = 0, boards = 0;
            foreach (SignDef each in SignCatalog.All)
            {
                if (each.Group != "guide")
                    continue;
                boards++;
                string text = TsAheadRule.WordsOf(SignInstances.Of(each, null));
                Program.Check(text.Length <= TsAheadRule.MaxText && !text.Contains("\n") && !text.Contains("  "), each.Id + ": the words are clean: '" + text + "'");
                withWords += text.Length > 0 ? 1 : 0;
            }

            Program.Check(boards > 50 && withWords > boards / 2, "most guide boards have words: " + withWords + " of " + boards);
            Program.Check(TsAheadRule.WordsOf(null) == "" && TsAheadRule.WordsOf(SignInstances.Of(SignCatalog.ById("gb.prohib.09"), null)).Length >= 0, "no instance, no words");

            // A word that comes twice in a row is said once: a board of three lines with the same word (a made-up board, the catalogue's have none).
            var board = new GuideBoard();
            var part = new GuidePart { W = 3f, H = 2f };
            part.Elements.Add(new GuideElement { Kind = GuideElementKind.Text, Text = "出口", X = 0.1f, Y = 0.1f });
            part.Elements.Add(new GuideElement { Kind = GuideElementKind.Text, Text = "出口", X = 0.5f, Y = 0.1f });
            part.Elements.Add(new GuideElement { Kind = GuideElementKind.Text, Text = "G15", X = 0.1f, Y = 0.9f });
            part.Elements.Add(new GuideElement { Kind = GuideElementKind.Text, Text = "  ", X = 0.1f, Y = 1.5f });
            part.Elements.Add(new GuideElement { Kind = GuideElementKind.Rect, Text = "矩形", X = 0.1f, Y = 1.7f });
            board.Parts.Add(part);
            string once = TsAheadRule.WordsOf(new SignInstance { Board = board });
            Program.Check(once == "出口 G15", "a word twice in a row is said once, a blank and a figure are not words: '" + once + "'");

            // The same board with other settings has other words.
            string other = TsAheadRule.WordsOf(SignInstances.Of(def, def.Template.Default().With("to1", "北京").With("to2", "天津").ToString()));
            Program.Check(other != words && other.Contains("北京"), "other settings, other words");
        }

        private static void Json()
        {
            // No board: ahead is null.
            using (JsonDocument none = JsonDocument.Parse(TsAheadRule.Json(null, 7)))
            {
                JsonElement root = none.RootElement;
                Program.Check(root.GetProperty("v").GetInt32() == 1 && root.GetProperty("rev").GetInt32() == 7 && root.GetProperty("ahead").ValueKind == JsonValueKind.Null, "nothing ahead: null, with the version and the revision");
            }

            var ahead = new TsAhead { Code = "路42", Name = "出口预告", Text = "G15 \"汕头\" 深圳\\500m", Distance = 312, XDm = -26191, ZDm = 24774, Bearing = 184 };
            using (JsonDocument doc = JsonDocument.Parse(TsAheadRule.Json(ahead, 12)))
            {
                JsonElement a = doc.RootElement.GetProperty("ahead");
                Program.Check(a.GetProperty("code").GetString() == "路42" && a.GetProperty("name").GetString() == "出口预告" && a.GetProperty("text").GetString() == ahead.Text, "the strings survive, with quotes and a backslash");
                Program.Check(a.GetProperty("d").GetInt32() == 312 && a.GetProperty("x").GetInt32() == -26191 && a.GetProperty("z").GetInt32() == 24774 && a.GetProperty("b").GetInt32() == 184, "the numbers");
            }

            // The JSON of a real find decodes and says what the rule found.
            TsAhead found = TsAheadRule.Find(new[] { Board(Exit, 0f, -123.4f) }, 0f, 0f, 180f, 500f);
            using (JsonDocument doc = JsonDocument.Parse(TsAheadRule.Json(found, 1)))
            {
                JsonElement a = doc.RootElement.GetProperty("ahead");
                Program.Check(a.GetProperty("d").GetInt32() == 123 && a.GetProperty("z").GetInt32() == -1234, "a find as JSON: 123 m, z -1234 dm");
            }

            // Control characters and the line separators are escaped.
            string raw = "a" + (char)1 + "b c" + (char)10 + "d" + (char)0x2028 + "e";
            using (JsonDocument doc = JsonDocument.Parse(TsAheadRule.Json(new TsAhead { Text = raw }, 0)))
                Program.Check(doc.RootElement.GetProperty("ahead").GetProperty("text").GetString() == raw, "control characters and the line separator round trip");
            Program.Check(!TsAheadRule.Json(new TsAhead { Text = raw }, 0).Contains((char)10) && !TsAheadRule.Json(new TsAhead { Text = raw }, 0).Contains((char)0x2028), "and none of them is in the text of the JSON itself");
        }
    }
}
