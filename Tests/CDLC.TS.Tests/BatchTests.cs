using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.67: the batch interface for the interchange designer (DESIGN-solver.md 7 of CSL2InterchangeDesigner): the request's checking and its refusals, the spec built from the fields, the answers
    /// (Submit, Status, Describe), the jobs and their states, the index of what a batch wrote, and the road an item stands beside. The ECS part (the system that matches, writes and replaces) is
    /// not covered here.
    /// </summary>
    internal static class BatchTests
    {
        public static void RunAll()
        {
            Program.Run("batch: a good request is read, the fields make the spec", GoodRequest);
            Program.Run("batch: values the field does not take, and keys that are no field", FieldValues);
            Program.Run("batch: the support and the position of an item", SupportAndPosition);
            Program.Run("batch: an item that cannot be placed is left out and says why", ItemRefused);
            Program.Run("batch: what refuses the whole batch", WholeRefused);
            Program.Run("batch: the id of a batch is hashed, never to 0", Hashing);
            Program.Run("batch: Describe tells the fields of a catalogue id, and what it says is accepted", Describing);
            Program.Run("batch: the jobs, their states and the replacing of a batch", Jobs);
            Program.Run("batch: the answers of Submit and Status claim only what is known", Answers);
            Program.Run("batch: the index of what each batch wrote", Index);
            Program.Run("batch: the road an item stands beside", Host);
        }

        private const string Catalog = "gb.exit.adv.2km";

        private static string Item(int i, string catalog = Catalog, string fields = "{}", string support = "\"\"", string position = "\"x\":10.5,\"y\":2.25,\"z\":-30,\"yaw\":1.5") =>
            "{\"i\":" + i + ",\"catalog\":\"" + catalog + "\",\"fields\":" + fields + ",\"support\":" + support + "," + position + "}";

        private static string Request(string batch, params string[] items) =>
            "{\"v\":1,\"batch\":\"" + batch + "\",\"items\":[" + string.Join(",", items) + "]}";

        private static BatchPlan Parse(string json)
        {
            BatchPlan plan = BatchRequest.Parse(json);
            Program.Check(plan != null, "a plan comes back");
            return plan;
        }

        private static GuideSpec SpecOf(string catalog, string text) => SignCatalog.ChoiceById(catalog).Def.Template.Parse(text);

        private static void GoodRequest()
        {
            BatchPlan plan = Parse(Request("interchange-1a2b", Item(0, fields: "{\"road\":\"g\",\"code\":\"G15\",\"to1\":\"日照\",\"to2\":\"济宁\",\"dist\":\"2\"}", support: "\"gantry.portal\""),
                Item(7, "gb.exit.adv.500", "{\"to1\":\"青岛\"}", "\"pole.single\"", "\"x\":-1,\"y\":0,\"z\":2e2,\"yaw\":-3.0")));
            Program.Check(plan.Error.Length == 0 && plan.Items.Count == 2 && plan.AcceptedCount == 2, "no error, two items, both accepted");
            Program.Check(plan.Batch == "interchange-1a2b" && plan.BatchId == BatchRequest.HashOf("interchange-1a2b") && plan.BatchId != 0u, "the batch id and its hash");
            BatchItem a = plan.Items[0], b = plan.Items[1];
            Program.Check(a.Index == 0 && b.Index == 7, "the request's numbers are kept, not the positions");
            Program.Check(a.Def == SignCatalog.ChoiceById(Catalog).Def && b.Def == SignCatalog.ChoiceById("gb.exit.adv.500").Def, "each item's sign");
            Program.Near(a.X, 10.5f, 1e-6f, "x");
            Program.Near(a.Y, 2.25f, 1e-6f, "y");
            Program.Near(a.Z, -30f, 1e-6f, "z");
            Program.Near(a.Yaw, 1.5f, 1e-6f, "yaw");
            Program.Near(b.Z, 200f, 1e-6f, "an exponent");
            Program.Near(b.Yaw, -3f, 1e-6f, "a negative yaw");

            GuideSpec spec = SpecOf(Catalog, a.Spec);
            Program.Check(spec.Get("road") == "g" && spec.Get("code") == "G15" && spec.Get("to1") == "日照" && spec.Get("to2") == "济宁" && spec.Get("dist") == "2", "the fields are in the spec");
            // What the request does not name stays as the preset has it (unit=km, arrow=b, xnum=145).
            Program.Check(spec.Get("unit") == "km" && spec.Get("arrow") == "b" && spec.Get("xnum") == "145", "the preset's other fields stay");
            string expected = SpecOf(Catalog, SignCatalog.ChoiceById(Catalog).Spec).With("road", "g").With("code", "G15").With("to1", "日照").With("to2", "济宁").With("dist", "2").ToString();
            Program.Check(a.Spec == expected, "the spec is the one GuideSpec.With makes: " + a.Spec);
            Program.Check(SpecOf("gb.exit.adv.500", b.Spec).Get("to1") == "青岛" && SpecOf("gb.exit.adv.500", b.Spec).Get("dist") == "500", "the preset of another id: its own distance, the new place");
            Program.Check(a.SupportHash == SupportCatalog.ById("gantry.portal").Hash && b.SupportHash == SupportCatalog.ById("pole.single").Hash, "the supports");

            BatchPlan bare = Parse(Request("b", "{\"i\":0,\"catalog\":\"" + Catalog + "\",\"x\":1,\"y\":2,\"z\":3,\"yaw\":0,\"mainStation\":1234.5,\"unknown\":[1]}"));
            Program.Check(bare.Items.Count == 1 && bare.Items[0].Accepted && bare.Items[0].SupportHash == 0u && bare.Items[0].IgnoredKeys.Count == 0, "no fields, no support: the preset as it is, the planner's support; other keys are not read");
            Program.Check(bare.Items[0].Spec == SignCatalog.ChoiceById(Catalog).Spec || SpecOf(Catalog, bare.Items[0].Spec).ToString() == SpecOf(Catalog, SignCatalog.ChoiceById(Catalog).Spec).ToString(), "the preset's spec");
        }

        private static void FieldValues()
        {
            GuideTemplate template = SignCatalog.ChoiceById(Catalog).Def.Template;
            GuideField road = template.FieldOf("road");
            Program.Check(road != null && road.Kind == GuideFieldKind.Choice, "road is a choice (the test relies on it)");
            BatchPlan plan = Parse(Request("f", Item(0, fields: "{\"road\":\"no-such-road\",\"to1\":\"一二三四五六七八九十一二三四五六七八九十\",\"bogus\":\"1\",\"alsoBogus\":\"2\",\"bogus\":\"3\"}")));
            BatchItem item = plan.Items[0];
            GuideSpec spec = SpecOf(Catalog, item.Spec);
            Program.Check(spec.Get("road") == road.Default, "a choice that is none of the field's falls back to its default");
            Program.Check(spec.Get("to1").Length == template.FieldOf("to1").MaxLength && spec.Get("to1").Length < 20, "a text is cut to the field's length");
            Program.Check(item.IgnoredKeys.Count == 2 && item.IgnoredKeys[0] == "bogus" && item.IgnoredKeys[1] == "alsoBogus", "the unknown keys in the order they came, each once");

            // Values that are not text: a number, a flag, and what is no value.
            GuideField dist = template.FieldOf("dist");
            Program.Check(dist != null, "dist exists");
            BatchPlan numbers = Parse(Request("n", Item(0, fields: "{\"dist\":5,\"to2\":null,\"code\":[1],\"to1\":{\"a\":1},\"unit\":true}")));
            BatchItem n = numbers.Items[0];
            Program.Check(SpecOf(Catalog, n.Spec).Get("dist") == "5", "a number is its text");
            Program.Check(n.IgnoredKeys.Contains("to2") && n.IgnoredKeys.Contains("code") && n.IgnoredKeys.Contains("to1") && !n.IgnoredKeys.Contains("dist"), "null, an array and an object are no value: ignored");
            Program.Check(SpecOf(Catalog, n.Spec).Get("to2") == SpecOf(Catalog, SignCatalog.ChoiceById(Catalog).Spec).Get("to2"), "an ignored key leaves the preset's value");

            BatchPlan fractional = Parse(Request("n", Item(0, fields: "{\"dist\":2.5}")));
            Program.Check(SpecOf(Catalog, fractional.Items[0].Spec).Get("dist") == "2.5", "a fraction keeps its point");
            BatchPlan notObject = Parse(Request("n", Item(0, fields: "[1,2]")));
            Program.Check(notObject.Items[0].Accepted && notObject.Items[0].IgnoredKeys.Count == 0, "fields that is no object: nothing to read, the preset");

            // The separators of the spec are the template's own business, not the sender's.
            BatchPlan semi = Parse(Request("s", Item(0, fields: "{\"to1\":\"a;b=c\"}")));
            Program.Check(SpecOf(Catalog, semi.Items[0].Spec).Get("to1") == "a;b=c", "a semicolon and an equals sign in a value survive the spec's own escaping");
        }

        private static void SupportAndPosition()
        {
            Program.Check(Parse(Request("p", Item(0, support: "\"\""))).Items[0].SupportHash == 0u, "an empty support is the planner's");
            Program.Check(Parse(Request("p", Item(0, support: "null"))).Items[0].SupportHash == 0u, "a null support is the planner's");
            BatchItem unknown = Parse(Request("p", Item(0, support: "\"pole.nine\""))).Items[0];
            Program.Check(!unknown.Accepted && unknown.Rejected == "unknown support", "an unknown support is refused: " + unknown.Rejected);
            BatchItem host = Parse(Request("p", Item(0, support: "\"attached.pole\""))).Items[0];
            Program.Check(!host.Accepted && host.Rejected.Contains("host"), "a support that needs a host is refused: " + host.Rejected);
            BatchItem wall = Parse(Request("p", Item(0, support: "\"wall.flat\""))).Items[0];
            Program.Check(!wall.Accepted, "so is a wall's");
            BatchItem number = Parse(Request("p", Item(0, support: "5"))).Items[0];
            Program.Check(!number.Accepted && number.Rejected.Contains("string"), "a support that is no string is refused: " + number.Rejected);

            foreach (string id in BatchRequest.SupportIds())
            {
                BatchItem each = Parse(Request("p", Item(0, support: "\"" + id + "\""))).Items[0];
                Program.Check(each.Accepted && each.SupportHash == SupportCatalog.ById(id).Hash, "every support Describe names is accepted: " + id);
            }

            Program.Check(BatchRequest.SupportIds().Count >= 3 && BatchRequest.SupportIds().Contains("pole.single") && BatchRequest.SupportIds().Contains("gantry.portal") && BatchRequest.SupportIds().Contains("cantilever.single"),
                "the contract's examples are among them");
            Program.Check(!BatchRequest.SupportIds().Contains("attached.pole"), "and no support that needs a host");

            // Position: all four numbers, finite and on the map.
            string[] bad =
            {
                "\"y\":0,\"z\":0,\"yaw\":0", "\"x\":0,\"z\":0,\"yaw\":0", "\"x\":0,\"y\":0,\"yaw\":0", "\"x\":0,\"y\":0,\"z\":0", "\"x\":\"1\",\"y\":0,\"z\":0,\"yaw\":0", "\"x\":null,\"y\":0,\"z\":0,\"yaw\":0",
                "\"x\":1e30,\"y\":0,\"z\":0,\"yaw\":0", "\"x\":0,\"y\":-2e6,\"z\":0,\"yaw\":0", "\"x\":0,\"y\":0,\"z\":0,\"yaw\":true"
            };
            foreach (string position in bad)
            {
                BatchItem each = Parse(Request("p", Item(0, position: position))).Items[0];
                Program.Check(!each.Accepted && each.Rejected.Contains("numbers"), "a position that is not four numbers on the map is refused: " + position);
            }

            // The yaw is turned to within half a turn either way; a whole turn more is the same direction.
            BatchItem turned = Parse(Request("p", Item(0, position: "\"x\":0,\"y\":0,\"z\":0,\"yaw\":7"))).Items[0];
            Program.Near(turned.Yaw, 7f - 2f * (float)Math.PI, 1e-4f, "7 rad is 0.717 rad");
            BatchItem back = Parse(Request("p", Item(0, position: "\"x\":0,\"y\":0,\"z\":0,\"yaw\":-100"))).Items[0];
            Program.Check(Math.Abs(back.Yaw) <= (float)Math.PI + 1e-5f && Math.Abs(Math.Sin(back.Yaw) - Math.Sin(-100.0)) < 1e-4, "-100 rad is turned to within half a turn, the same direction");
        }

        private static void ItemRefused()
        {
            string marking = null;
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                if (choice.Def.IsMarking)
                {
                    marking = choice.Id;
                    break;
                }
            }

            Program.Check(marking != null, "the catalogue has a marking (the test needs one)");
            BatchPlan plan = Parse(Request("r", Item(0), Item(1, "no.such.id"), Item(2, marking), Item(3), Item(4, "")));
            Program.Check(plan.Error.Length == 0 && plan.Items.Count == 5 && plan.AcceptedCount == 2, "five items, two accepted, no error for the whole");
            Program.Check(plan.Items[0].Accepted && plan.Items[3].Accepted, "the good ones are accepted");
            Program.Check(!plan.Items[1].Accepted && plan.Items[1].Def == null && plan.Items[1].Rejected == "unknown catalog", "an unknown id: no sign, and says so");
            Program.Check(!plan.Items[2].Accepted && plan.Items[2].Rejected.Contains("marking"), "a marking is not hung as a board");
            Program.Check(!plan.Items[4].Accepted && plan.Items[4].Rejected == "unknown catalog", "an empty id is unknown");
            BatchPlan numberCatalog = Parse("{\"v\":1,\"batch\":\"r\",\"items\":[{\"i\":0,\"catalog\":5,\"x\":0,\"y\":0,\"z\":0,\"yaw\":0},{\"i\":1,\"catalog\":\"" + Catalog + "\",\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}]}");
            Program.Check(numberCatalog.Error.Length == 0 && !numberCatalog.Items[0].Accepted && numberCatalog.Items[1].Accepted, "a catalogue id that is no string is unknown");
        }

        private static void WholeRefused()
        {
            string good = Item(0);
            string[] refused =
            {
                "", "   ", "not json", "[1]", "\"x\"", "{", "{\"v\":1,\"batch\":\"b\",\"items\":[}", "null",
                "{\"batch\":\"b\",\"items\":[" + good + "]}", "{\"v\":2,\"batch\":\"b\",\"items\":[" + good + "]}", "{\"v\":\"1\",\"batch\":\"b\",\"items\":[" + good + "]}",
                "{\"v\":1,\"items\":[" + good + "]}", "{\"v\":1,\"batch\":\"\",\"items\":[" + good + "]}", "{\"v\":1,\"batch\":5,\"items\":[" + good + "]}",
                "{\"v\":1,\"batch\":\"" + new string('b', BatchRequest.MaxBatchLength + 1) + "\",\"items\":[" + good + "]}",
                "{\"v\":1,\"batch\":\"b\"}", "{\"v\":1,\"batch\":\"b\",\"items\":{}}", "{\"v\":1,\"batch\":\"b\",\"items\":[5]}", "{\"v\":1,\"batch\":\"b\",\"items\":[" + good + ",null]}",
                Request("b", "{\"catalog\":\"" + Catalog + "\",\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}"), Request("b", good.Replace("\"i\":0", "\"i\":-1")), Request("b", good.Replace("\"i\":0", "\"i\":1.5")),
                Request("b", good.Replace("\"i\":0", "\"i\":\"0\"")), Request("b", good, good), Request("b", Item(3), Item(4), Item(3)),
                Request("b", Item(0, "no.such.id")), Request("b", Item(0, "no.such.id"), Item(1, "another.unknown"))
            };
            foreach (string json in refused)
            {
                BatchPlan plan = BatchRequest.Parse(json);
                string label = json.Length > 70 ? json.Substring(0, 70) : json;
                Program.Check(plan.Error.Length > 0 && plan.Items.Count == 0 && plan.AcceptedCount == 0, "refused whole: " + label + " (" + plan.Error + ")");
            }

            Program.Check(BatchRequest.Parse(null).Error.Length > 0, "null is refused");
            BatchPlan version = BatchRequest.Parse("{\"v\":2,\"batch\":\"keep-me\",\"items\":[]}");
            Program.Check(version.Error.Contains("version") && version.Batch == "keep-me", "a refused request keeps the batch id it named, for the answer");

            // The count: 300 is the most; 301 is refused whole, with no item written.
            string[] items = new string[BatchRequest.MaxItems + 1];
            for (int i = 0; i < items.Length; i++)
                items[i] = Item(i);
            BatchPlan full = Parse(Request("big", Take(items, BatchRequest.MaxItems)));
            Program.Check(full.Error.Length == 0 && full.Items.Count == 300 && full.AcceptedCount == 300, "300 items are accepted");
            BatchPlan over = Parse(Request("big", items));
            Program.Check(over.Error.Contains("too many") && over.Items.Count == 0, "301 items are refused whole: " + over.Error);

            // A request too large to be one of ours is refused before it is read.
            var huge = new StringBuilder("{\"v\":1,\"batch\":\"h\",\"items\":[],\"pad\":\"");
            huge.Append('x', BatchRequest.MaxChars);
            huge.Append("\"}");
            Program.Check(BatchRequest.Parse(huge.ToString()).Error.Contains("too large"), "a request over the size limit is refused");

            // Not refused: no items at all (the batch is replaced by nothing), and a mix of known and unknown ids.
            BatchPlan empty = Parse(Request("e"));
            Program.Check(empty.Error.Length == 0 && empty.Items.Count == 0 && empty.BatchId != 0u, "no items is a batch that replaces the old one with nothing");
            BatchPlan mixed = Parse(Request("m", Item(0, "no.such.id"), Item(1)));
            Program.Check(mixed.Error.Length == 0 && mixed.AcceptedCount == 1, "one known id is enough");
            BatchPlan unorderedIds = Parse(Request("u", Item(9), Item(2), Item(5)));
            Program.Check(unorderedIds.Error.Length == 0 && unorderedIds.Items[0].Index == 9 && unorderedIds.Items[2].Index == 5, "the numbers need not be in order or from 0");
        }

        private static string[] Take(string[] all, int count)
        {
            var part = new string[count];
            Array.Copy(all, part, count);
            return part;
        }

        private static void Hashing()
        {
            Program.Check(BatchRequest.HashOf("interchange-1a2b") == BatchRequest.HashOf("interchange-1a2b"), "the same id, the same hash");
            Program.Check(BatchRequest.HashOf("interchange-1a2b") != BatchRequest.HashOf("interchange-1a2c"), "another id, another hash");
            Program.Check(BatchRequest.HashOf("interchange-1a2b") == SignCatalog.HashOf("interchange-1a2b"), "FNV-1a, as the catalogue's hashes");
            Program.Check(BatchRequest.HashOf("") != 0u && BatchRequest.HashOf("x") != 0u, "never 0");
            Program.Check(BatchRequest.NonZero(0u) == 1u && BatchRequest.NonZero(1u) == 1u && BatchRequest.NonZero(0xFFFFFFFFu) == 0xFFFFFFFFu && BatchRequest.NonZero(77u) == 77u, "a hash of 0 would be 1, any other stays");
            var seen = new HashSet<uint>();
            for (int x = -50; x <= 50; x++)
            {
                for (int z = -50; z <= 50; z++)
                    seen.Add(BatchRequest.HashOf("interchange-" + x * 1000 + "," + z * 1000));
            }

            Program.Check(seen.Count == 101 * 101, "10 201 ids on a grid of 1 km do not collide: " + seen.Count);
        }

        private static JsonValue Json(string text)
        {
            bool ok = MiniJson.TryParse(text, out JsonValue value, out string error);
            Program.Check(ok, "Describe returns JSON (" + error + ")");
            return value;
        }

        private static void Describing()
        {
            JsonValue d = Json(BatchRequest.Describe(Catalog));
            Program.Check(d != null && d.Get("catalog").Text == Catalog && d.Get("error") == null, "the catalogue id, no error");
            Program.Check(d.Get("template").Text == "guide.exit.advance", "the template: " + d.Get("template").Text);
            JsonValue fields = d.Get("fields");
            GuideTemplate template = SignCatalog.ChoiceById(Catalog).Def.Template;
            Program.Check(fields.Kind == JsonKind.Array && fields.Items.Count == template.Fields.Length, "every field of the template");
            for (int i = 0; i < template.Fields.Length; i++)
            {
                GuideField field = template.Fields[i];
                JsonValue each = fields.Items[i];
                string type = field.Kind == GuideFieldKind.Choice ? "choice" : field.Kind == GuideFieldKind.Flag ? "flag" : "text";
                Program.Check(each.Get("key").Text == field.Key && each.Get("type").Text == type && each.Get("default").Text == field.Default, "field " + field.Key + ": key, type, default");
                if (field.Kind == GuideFieldKind.Text)
                    Program.Check((int)each.Get("maxLength").Number == field.MaxLength && each.Get("digitsOnly").Bool == field.DigitsOnly, "field " + field.Key + ": length and digits");
                if (field.Kind == GuideFieldKind.Choice)
                {
                    JsonValue choices = each.Get("choices");
                    Program.Check(choices.Items.Count == field.Choices.Length, "field " + field.Key + ": every choice");
                    // What is described is what is accepted: each listed value goes through the request unchanged.
                    foreach (JsonValue choice in choices.Items)
                    {
                        BatchPlan plan = Parse(Request("d", Item(0, fields: "{\"" + field.Key + "\":\"" + choice.Get("value").Text + "\"}")));
                        Program.Check(SpecOf(Catalog, plan.Items[0].Spec).Get(field.Key) == choice.Get("value").Text, "choice " + field.Key + "=" + choice.Get("value").Text + " is taken as it is");
                    }
                }

                if (field.Kind == GuideFieldKind.Flag)
                    Program.Check(each.Get("values").Items.Count == 2 && each.Get("values").Items[0].Text == "0" && each.Get("values").Items[1].Text == "1", "field " + field.Key + ": a flag is 0 or 1");
            }

            foreach (string key in new[] { "road", "code", "to1", "to2", "dist" })
                Program.Check(template.FieldOf(key) != null, "the contract's example key " + key + " is a field");

            JsonValue preset = d.Get("preset");
            Program.Check(preset.Get("code").Text == "G204" && preset.Get("to1").Text == "如皋" && preset.Get("dist").Text == "2", "the preset's values: the starting point of the sender's fields");
            JsonValue supports = d.Get("supports");
            Program.Check(supports.Items.Count == BatchRequest.SupportIds().Count && supports.Items[0].Text == BatchRequest.SupportIds()[0], "the supports a batch may name");

            JsonValue unknown = Json(BatchRequest.Describe("no.such.id"));
            Program.Check(unknown.Get("error").Text == "unknown catalog" && unknown.Get("fields") == null, "an unknown id: an error and no fields");
            Program.Check(Json(BatchRequest.Describe(null)).Get("error") != null && Json(BatchRequest.Describe("")).Get("error") != null, "no id: an error");

            // Every board of the catalogue can be described, and what is described parses back.
            int described = 0;
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                JsonValue each = Json(BatchRequest.Describe(choice.Id));
                if (each == null)
                    continue;
                described++;
                Program.Check(choice.Def.IsMarking ? each.Get("error") != null : each.Get("error") == null && each.Get("fields").Kind == JsonKind.Array, "catalogue id " + choice.Id + ": a board has fields, a marking an error");
            }

            Program.Check(described == SignCatalog.Choices.Count && described > 100, "every id of the catalogue was described: " + described);
        }

        private static BatchPlan Plan(string batch, int accepted, params int[] rejected)
        {
            var parts = new List<string>();
            for (int i = 0; i < accepted; i++)
                parts.Add(Item(i));
            foreach (int r in rejected)
                parts.Add(Item(r, "no.such.id"));
            if (accepted == 0 && rejected.Length > 0)
                parts.Add(Item(99));
            return Parse(Request(batch, parts.ToArray()));
        }

        private static void Jobs()
        {
            var book = new BatchBook();
            BatchJob a = book.Submit(Plan("A", 3));
            BatchJob b = book.Submit(Plan("B", 2));
            Program.Check(a.State == BatchState.Queued && !a.Done && book.Count == 2, "a job starts queued");
            Program.Check(book.Next() == a, "the oldest first");
            Program.Check(book.Find(a.Plan.BatchId) == a && book.Find(b.Plan.BatchId) == b && book.Find(12345u) == null, "found by the hash of the id");

            Program.Check(!a.Finish(3, new int[0]), "a queued job is not finished before it began");
            Program.Check(a.Begin() && a.State == BatchState.Preparing && !a.Begin(), "begin once");
            Program.Check(book.Next() == a, "a job being prepared is still the next until it is done");
            Program.Check(a.Finish(2, new[] { 1 }) && a.State == BatchState.Written && a.Written == 2 && a.Unmatched.Count == 1 && a.Unmatched[0] == 1 && a.Done, "finish: written and unmatched");
            Program.Check(!a.Finish(3, new int[0]) && !a.Fail("late") && a.State == BatchState.Written && a.Written == 2, "a finished job does not change");
            Program.Check(book.Next() == b, "the next job is the next one waiting");

            // A failure writes nothing: every accepted item is unmatched.
            BatchJob c = book.Submit(Plan("C", 2, 5));
            Program.Check(c.Begin() && c.Fail("boom") && c.State == BatchState.Failed && c.Written == 0 && c.Error == "boom" && c.Done, "fail from preparing");
            Program.Check(c.Unmatched.Count == 2 && c.Unmatched.Contains(0) && c.Unmatched.Contains(1) && !c.Unmatched.Contains(5), "failed: the accepted items are unmatched, the refused are not counted again");
            BatchJob queuedFail = book.Submit(Plan("D", 1));
            Program.Check(queuedFail.Fail("no city") && queuedFail.State == BatchState.Failed, "a queued job can fail too");

            // The same batch again replaces the waiting job: the waiting one is dropped, never written.
            BatchJob b2 = book.Submit(Plan("B", 4));
            Program.Check(b.Superseded && !b2.Superseded && book.Find(b.Plan.BatchId) == b2, "a waiting job of the same batch is superseded");
            Program.Check(b2.Plan.AcceptedCount == 4, "the new one has the new items");
            BatchJob a2 = book.Submit(Plan("A", 1));
            Program.Check(!a.Superseded && book.Find(a.Plan.BatchId) == a2 && a2.State == BatchState.Queued, "a written job is replaced by the new one, not marked superseded");
            Program.Check(book.Next() == b2, "the superseded job is no longer next");

            // A job being prepared is superseded too (the system looks at the flag between its steps).
            Program.Check(b2.Begin(), "begin");
            BatchJob b3 = book.Submit(Plan("B", 1));
            Program.Check(b2.Superseded && book.Next() == a2 && book.Find(b3.Plan.BatchId) == b3, "a job being prepared is superseded and left");

            // Only the newest MaxKept finished jobs are kept; waiting jobs are never dropped.
            var many = new BatchBook();
            var jobs = new List<BatchJob>();
            for (int i = 0; i < BatchBook.MaxKept + 5; i++)
            {
                BatchJob job = many.Submit(Plan("K" + i, 1));
                job.Begin();
                job.Finish(1, new int[0]);
                jobs.Add(job);
            }

            BatchJob waiting = many.Submit(Plan("waiting", 1));
            Program.Check(many.Count == BatchBook.MaxKept + 1, "the last " + BatchBook.MaxKept + " finished and the waiting one: " + many.Count);
            Program.Check(many.Find(jobs[0].Plan.BatchId) == null && many.Find(jobs[jobs.Count - 1].Plan.BatchId) == jobs[jobs.Count - 1] && many.Next() == waiting, "the oldest are forgotten, the newest and the waiting stay");

            // Leaving the city drops what is waiting (it says so) and forgets the rest.
            BatchJob pending = many.Submit(Plan("pending", 1));
            many.Clear();
            Program.Check(many.Count == 0 && pending.Superseded && waiting.Superseded && !jobs[jobs.Count - 1].Superseded && many.Next() == null, "clear: nothing is left, what waited is marked");
        }

        private static void Answers()
        {
            BatchPlan plan = Parse(Request("answers", Item(0, fields: "{\"to1\":\"A\",\"zzz\":\"1\"}"), Item(1, "no.such.id"), Item(2), Item(3, support: "\"nope\"")));
            JsonValue submit = Json(BatchBook.SubmitJson(plan));
            Program.Check(submit.Get("batch").Text == "answers" && (int)submit.Get("queued").Number == 2 && submit.Get("error") == null, "Submit: batch and queued (the accepted)");
            Program.Check(submit.Get("unmatched") == null && submit.Get("written") == null && submit.Get("state") == null, "Submit claims no unmatched, no written, no state: they are not known yet");
            JsonValue items = submit.Get("items");
            Program.Check(items.Items.Count == 2 && (int)items.Items[0].Get("i").Number == 0 && (int)items.Items[1].Get("i").Number == 2, "the accepted items, by the request's number");
            Program.Check(items.Items[0].Get("ignoredKeys").Items.Count == 1 && items.Items[0].Get("ignoredKeys").Items[0].Text == "zzz", "the ignored key of the first");
            Program.Check(items.Items[1].Get("ignoredKeys").Kind == JsonKind.Array && items.Items[1].Get("ignoredKeys").Items.Count == 0, "an empty array for an item with none: not absent");
            JsonValue rejected = submit.Get("rejected");
            Program.Check(rejected.Items.Count == 2 && (int)rejected.Items[0].Get("i").Number == 1 && rejected.Items[0].Get("reason").Text == "unknown catalog" && (int)rejected.Items[1].Get("i").Number == 3, "the refused items and why");

            BatchPlan clean = Parse(Request("clean", Item(0)));
            Program.Check(Json(BatchBook.SubmitJson(clean)).Get("rejected") == null, "no refused items, no 'rejected'");

            // A whole refusal: the error, the batch id if there was one, nothing that claims a queue.
            JsonValue refused = Json(BatchBook.SubmitJson(BatchRequest.Parse("{\"v\":1,\"batch\":\"too-big\",\"items\":" + "[" + string.Join(",", Take(Many(301), 301)) + "]}")));
            Program.Check(refused.Get("batch").Text == "too-big" && refused.Get("error").Text.Contains("too many") && refused.Get("queued") == null && refused.Get("items") == null, "Submit of a refused batch: batch and error only");
            JsonValue broken = Json(BatchBook.SubmitJson(BatchRequest.Parse("not json")));
            Program.Check(broken.Get("batch").Text == "" && broken.Get("error").Text.StartsWith("bad JSON"), "unreadable text: an empty batch and an error");

            // Status over the life of a job.
            var book = new BatchBook();
            BatchJob job = book.Submit(plan);
            JsonValue queued = Json(BatchBook.StatusJson("answers", job));
            Program.Check(queued.Get("state").Text == "queued" && queued.Get("unmatched") == null && (int)queued.Get("written").Number == 0 && (int)queued.Get("queued").Number == 2, "queued: no 'unmatched' (not known yet)");
            Program.Check(queued.Get("items").Items.Count == 2 && queued.Get("items").Items[0].Get("ignoredKeys").Items[0].Text == "zzz", "the ignored keys are known from the start");
            job.Begin();
            Program.Check(Json(BatchBook.StatusJson("answers", job)).Get("state").Text == "preparing" && Json(BatchBook.StatusJson("answers", job)).Get("unmatched") == null, "preparing: no 'unmatched'");
            job.Finish(1, new[] { 2 });
            JsonValue written = Json(BatchBook.StatusJson("answers", job));
            Program.Check(written.Get("state").Text == "written" && (int)written.Get("written").Number == 1 && written.Get("error") == null, "written: the count");
            Program.Check(written.Get("unmatched").Kind == JsonKind.Array && written.Get("unmatched").Items.Count == 1 && (int)written.Get("unmatched").Items[0].Number == 2, "written: the unmatched, by the request's number");
            Program.Check(written.Get("rejected").Items.Count == 2, "the refused items are still told");

            BatchJob none = new BatchBook().Submit(Parse(Request("none", Item(0), Item(1))));
            none.Begin();
            none.Finish(2, new int[0]);
            JsonValue all = Json(BatchBook.StatusJson("none", none));
            Program.Check(all.Get("unmatched").Kind == JsonKind.Array && all.Get("unmatched").Items.Count == 0, "all written: an empty 'unmatched', not absent (it is known to be empty)");

            BatchJob failed = new BatchBook().Submit(Parse(Request("failed", Item(0), Item(4))));
            failed.Fail("the game's roads could not be read");
            JsonValue bad = Json(BatchBook.StatusJson("failed", failed));
            Program.Check(bad.Get("state").Text == "failed" && (int)bad.Get("written").Number == 0 && bad.Get("error").Text.Contains("roads") && bad.Get("unmatched").Items.Count == 2, "failed: nothing written, an error, the accepted items unmatched");

            JsonValue unknown = Json(BatchBook.StatusJson("who", null));
            Program.Check(unknown.Get("batch").Text == "who" && unknown.Get("state").Text == "failed" && unknown.Get("error").Text == "unknown batch" && unknown.Get("unmatched") == null, "an unknown batch: failed, 'unknown batch', no claim about unmatched");

            Program.Check(BatchBook.StateName(BatchState.Queued) == "queued" && BatchBook.StateName(BatchState.Preparing) == "preparing" && BatchBook.StateName(BatchState.Written) == "written" && BatchBook.StateName(BatchState.Failed) == "failed",
                "the four state names of the contract");
        }

        private static string[] Many(int count)
        {
            var items = new string[count];
            for (int i = 0; i < count; i++)
                items[i] = Item(i);
            return items;
        }

        private static void Index()
        {
            var index = new BatchIndex<int>();
            index.Add(10u, 1);
            index.Add(10u, 2);
            index.Add(20u, 3);
            index.Add(0u, 4);
            Program.Check(index.Count == 3 && index.BatchCount == 2 && index.CountOf(10u) == 2 && index.CountOf(20u) == 1 && index.CountOf(0u) == 0 && index.CountOf(30u) == 0, "batch 0 is no batch and is not kept");

            index.Add(20u, 2);
            Program.Check(index.CountOf(10u) == 1 && index.CountOf(20u) == 2 && index.Count == 3, "a key put in another batch leaves the first");
            index.Add(20u, 2);
            Program.Check(index.CountOf(20u) == 2 && index.Count == 3, "the same key twice is held once");
            index.Add(20u, 3);
            index.Add(20u, 3);
            List<int> held = index.Take(20u);
            Program.Check(held.Count == 2 && held[0] == 3 && held[1] == 2, "an item read again at each change is held once, where it was");
            index.Add(20u, 2);
            index.Add(20u, 3);

            Program.Check(index.Remove(1) && !index.Remove(1) && index.CountOf(10u) == 0 && index.BatchCount == 1, "a key removed; the empty batch is gone");
            Program.Check(!index.Remove(99), "a key that is in no batch");

            List<int> taken = index.Take(20u);
            taken.Sort();
            Program.Check(taken.Count == 2 && taken[0] == 2 && taken[1] == 3 && index.Count == 0 && index.CountOf(20u) == 0 && index.BatchCount == 0, "take: everything the batch held, then nothing");
            Program.Check(index.Take(20u).Count == 0 && index.Take(777u).Count == 0, "taking from an empty or unknown batch: nothing");
            Program.Check(!index.Remove(2) && !index.Remove(3), "what was taken is not held by a batch any more");

            for (int i = 0; i < 300; i++)
                index.Add(5u, i);
            Program.Check(index.CountOf(5u) == 300, "300 in one batch");
            index.Clear();
            Program.Check(index.Count == 0 && index.BatchCount == 0 && index.CountOf(5u) == 0, "clear");
        }

        private static void Host()
        {
            // A road 29 m wide along +Z through the origin (x = 0), another 5 m wide along +X at z = 100, and a third far off.
            var curves = new List<Bezier3>
            {
                Bezier3.Line(new Vec3(0f, 0f, -200f), new Vec3(0f, 0f, 200f)),
                Bezier3.Line(new Vec3(-200f, 0f, 100f), new Vec3(200f, 0f, 100f)),
                Bezier3.Line(new Vec3(1000f, 0f, 0f), new Vec3(1000f, 0f, 100f))
            };
            var widths = new List<float> { 29f, 5f, 8f };
            var boxes = new EdgeBoxes();
            foreach (Bezier3 c in curves)
                boxes.Add(c.A.X, c.A.Z, c.B.X, c.B.Z, c.C.X, c.C.Z, c.D.X, c.D.Z);
            var scratch = new List<int>();
            Func<int, Bezier3> curveOf = i => curves[i];
            Func<int, float> widthOf = i => widths[i];

            Program.Check(BatchHost.Find(boxes, curveOf, widthOf, 16f, 0f, scratch, out float t) == 0, "16 m from the centre line of a road 29 m wide is at its roadside");
            Program.Near(t, 0.5f, 0.02f, "the nearest place on the curve is half way");
            Program.Check(BatchHost.Find(boxes, curveOf, widthOf, 14.5f + EdgeHint.Beside - 0.1f, 0f, scratch, out _) == 0, "just inside the reach beyond the edge");
            Program.Check(BatchHost.Find(boxes, curveOf, widthOf, 14.5f + EdgeHint.Beside + 0.5f, 0f, scratch, out _) == -1, "just outside it: no road");
            Program.Check(BatchHost.Find(boxes, curveOf, widthOf, 0f, 0f, scratch, out _) == 0, "on the road itself");
            Program.Check(BatchHost.Find(boxes, curveOf, widthOf, -16f, -50f, scratch, out _) == 0, "the other side");
            Program.Check(BatchHost.Find(boxes, curveOf, widthOf, 60f, 0f, scratch, out _) == -1, "60 m off the wide road: none");

            // Between two roads the one the item is most inside wins, not the one whose centre line is nearer.
            Program.Check(BatchHost.Find(boxes, curveOf, widthOf, 16f, 95f, scratch, out _) == 0, "at the roadside of the wide road 5 m from the narrow one's centre line: the wide road (1.5 m past its edge against 2.5 m past the narrow one's)");
            Program.Check(BatchHost.Find(boxes, curveOf, widthOf, 40f, 96f, scratch, out _) == 1, "40 m along the narrow road, far from the wide one: the narrow road");

            Program.Check(BatchHost.Find(new EdgeBoxes(), curveOf, widthOf, 0f, 0f, scratch, out _) == -1, "no edges: no road");
            Program.Check(BatchHost.Find(boxes, curveOf, widthOf, 1003f, 50f, scratch, out _) == 2, "a road far from the others is found there");
            Program.Check(BatchHost.SearchRadius == BatchHost.MaxHalfWidth + EdgeHint.Beside, "the search reaches the widest road's edge and the reach beyond it");
        }
    }
}
