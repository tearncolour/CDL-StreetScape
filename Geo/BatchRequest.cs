using System;
using System.Collections.Generic;
using System.Globalization;

namespace CDLC.TS.Geo
{
    /// <summary>One item of a batch as the request asked for it, checked and turned into what the game places; Rejected says why it is not written (empty when it is).</summary>
    public sealed class BatchItem
    {
        /// <summary>The request's own number for the item ("i"), which the answers point back with.</summary>
        public int Index;

        public SignDef Def;

        /// <summary>The written spec of the board (the preset's settings with the request's fields over them); empty for a sign with no settings.</summary>
        public string Spec = "";

        /// <summary>The support's hash (SupportCatalog); 0 where the planner chooses it.</summary>
        public uint SupportHash;

        public float X, Y, Z, Yaw;

        /// <summary>The keys of "fields" that the template has no field for, or whose value is no text, number or flag: not used.</summary>
        public readonly List<string> IgnoredKeys = new List<string>();

        public string Rejected = "";

        public bool Accepted => Rejected.Length == 0;
    }

    /// <summary>A batch request after checking: the items in the request's order, or the reason the whole batch is refused (Error).</summary>
    public sealed class BatchPlan
    {
        public string Error = "";
        public string Batch = "";
        public uint BatchId;
        public readonly List<BatchItem> Items = new List<BatchItem>();

        public int AcceptedCount
        {
            get
            {
                int count = 0;
                foreach (BatchItem item in Items)
                    count += item.Accepted ? 1 : 0;
                return count;
            }
        }
    }

    /// <summary>
    /// The batch interface's request (0.12.67; the contract of the interchange designer, DESIGN-solver.md 7): {"v":1,"batch":"...","items":[{"i":0,"catalog":"gb.exit.adv.2km","fields":{...},
    /// "support":"","x":..,"y":..,"z":..,"yaw":..,"mainStation":..}]}. A request that cannot be read, is for another version, has more than MaxItems items or names no known catalogue id is
    /// refused whole; an item that cannot be placed (an unknown id, a position that is no number) is left out and says why. Pure: the game is not asked for anything here.
    /// </summary>
    public static class BatchRequest
    {
        public const int Version = 1;

        public const int MaxItems = 300;

        /// <summary>The longest request read (characters): 300 items with long texts are about 150 000.</summary>
        public const int MaxChars = 2000000;

        public const int MaxBatchLength = 128;

        /// <summary>The farthest an item's position may be from the origin (m); the map is 14 km across.</summary>
        public const float MaxCoordinate = 1000000f;

        public static BatchPlan Parse(string json)
        {
            var plan = new BatchPlan();
            if (json != null && json.Length > MaxChars)
                return Refuse(plan, "the request is too large (" + json.Length + " characters, at most " + MaxChars + ")");
            if (!MiniJson.TryParse(json, out JsonValue root, out string parseError))
                return Refuse(plan, "bad JSON: " + parseError);
            if (root.Kind != JsonKind.Object)
                return Refuse(plan, "bad JSON: the request is not an object");

            JsonValue batch = root.Get("batch");
            if (batch != null && batch.Kind == JsonKind.String && batch.Text.Length <= MaxBatchLength)
                plan.Batch = batch.Text;
            JsonValue version = root.Get("v");
            if (version == null || version.Kind != JsonKind.Number || version.Number != Version)
                return Refuse(plan, "unsupported version (this is " + Version + ")");
            if (batch == null || batch.Kind != JsonKind.String || batch.Text.Length == 0 || batch.Text.Length > MaxBatchLength)
                return Refuse(plan, "no batch id (a string of 1 to " + MaxBatchLength + " characters)");
            plan.BatchId = HashOf(batch.Text);

            JsonValue items = root.Get("items");
            if (items == null || items.Kind != JsonKind.Array)
                return Refuse(plan, "no items array");
            if (items.Items.Count > MaxItems)
                return Refuse(plan, "too many items (" + items.Items.Count + ", at most " + MaxItems + ")");

            var seen = new HashSet<int>();
            for (int n = 0; n < items.Items.Count; n++)
            {
                JsonValue each = items.Items[n];
                if (each.Kind != JsonKind.Object)
                    return Refuse(plan, "item " + n + " is not an object");
                JsonValue number = each.Get("i");
                if (number == null || number.Kind != JsonKind.Number || number.Number < 0 || number.Number > int.MaxValue || Math.Floor(number.Number) != number.Number)
                    return Refuse(plan, "item " + n + " has no valid i (a whole number from 0)");
                int index = (int)number.Number;
                if (!seen.Add(index))
                    return Refuse(plan, "item index " + index + " is used twice");
                plan.Items.Add(ReadItem(index, each));
            }

            // Items to write or none at all: a batch that names no known catalogue id is a mistake of the sender's, not a batch to replace the old one with.
            if (plan.Items.Count > 0 && plan.Items.TrueForAll(i => i.Def == null))
                return Refuse(plan, "no known catalog id");
            return plan;
        }

        /// <summary>FNV-1a of the batch id, never 0 (0 is "no batch" in TsItem).</summary>
        public static uint HashOf(string batch) => NonZero(SignCatalog.HashOf(batch));

        /// <summary>A hash that is 0 (no string is known to have one) is 1: 0 is "no batch".</summary>
        public static uint NonZero(uint hash) => hash == 0u ? 1u : hash;

        private static BatchPlan Refuse(BatchPlan plan, string error)
        {
            plan.Error = error;
            plan.Items.Clear();
            return plan;
        }

        private static BatchItem ReadItem(int index, JsonValue each)
        {
            var item = new BatchItem { Index = index };
            JsonValue catalog = each.Get("catalog");
            SignChoice choice = catalog != null && catalog.Kind == JsonKind.String ? SignCatalog.ChoiceById(catalog.Text) : null;
            if (choice == null)
            {
                item.Rejected = "unknown catalog";
                return item;
            }

            if (choice.Def.IsMarking)
            {
                // A marking is painted along a road's lanes by its own tools, not hung at a point.
                item.Def = choice.Def;
                item.Rejected = "a marking is not a board";
                return item;
            }

            item.Def = choice.Def;
            if (!Number(each, "x", out item.X) || !Number(each, "y", out item.Y) || !Number(each, "z", out item.Z) || !Number(each, "yaw", out item.Yaw)
                || Math.Abs(item.X) > MaxCoordinate || Math.Abs(item.Y) > MaxCoordinate || Math.Abs(item.Z) > MaxCoordinate)
            {
                item.Rejected = "x, y, z and yaw must be numbers";
                return item;
            }

            item.Yaw = (float)Math.IEEERemainder(item.Yaw, 2.0 * Math.PI);

            JsonValue support = each.Get("support");
            if (support != null && support.Kind == JsonKind.String && support.Text.Length > 0)
            {
                SupportDef def = SupportCatalog.ById(support.Text);
                if (def == null || NeedsHost(def.Kind))
                {
                    item.Rejected = def == null ? "unknown support" : "this support needs a host";
                    return item;
                }

                item.SupportHash = def.Hash;
            }
            else if (support != null && support.Kind != JsonKind.String && support.Kind != JsonKind.Null)
            {
                item.Rejected = "support must be a string";
                return item;
            }

            item.Spec = SpecOf(choice, each.Get("fields"), item.IgnoredKeys);
            return item;
        }

        /// <summary>A support that holds the board to something that is not ours (a lamp post, a wall): the batch has no such host to give.</summary>
        public static bool NeedsHost(SupportKind kind) =>
            kind == SupportKind.Attached || kind == SupportKind.AttachedArm || kind == SupportKind.WallFlat || kind == SupportKind.WallBracket || kind == SupportKind.OverRoad;

        private static bool Number(JsonValue each, string key, out float value)
        {
            value = 0f;
            JsonValue number = each.Get(key);
            if (number == null || number.Kind != JsonKind.Number)
                return false;
            value = (float)number.Number;
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>The preset's spec with each field of the request put over it by GuideSpec.With (a value the field does not take becomes the field's default); the keys that are no field are noted.</summary>
        private static string SpecOf(SignChoice choice, JsonValue fields, List<string> ignored)
        {
            GuideTemplate template = choice.Def.Template;
            if (template == null)
            {
                if (fields != null && fields.Kind == JsonKind.Object)
                    AddUnique(ignored, fields.Members);
                return "";
            }

            GuideSpec spec = string.IsNullOrEmpty(choice.Spec) ? template.Default() : template.Parse(choice.Spec);
            if (fields == null || fields.Kind != JsonKind.Object)
                return spec.ToString();
            foreach (KeyValuePair<string, JsonValue> member in fields.Members)
            {
                string text = TextOf(member.Value);
                if (template.FieldOf(member.Key) == null || text == null)
                {
                    if (!ignored.Contains(member.Key))
                        ignored.Add(member.Key);
                    continue;
                }

                spec = spec.With(member.Key, text);
            }

            return spec.ToString();
        }

        private static void AddUnique(List<string> keys, List<KeyValuePair<string, JsonValue>> members)
        {
            foreach (KeyValuePair<string, JsonValue> member in members)
            {
                if (!keys.Contains(member.Key))
                    keys.Add(member.Key);
            }
        }

        /// <summary>The text of a field's value: a string as it is (the contract), a number or a flag as the template would write it; null for what is no value (null, an array, an object).</summary>
        private static string TextOf(JsonValue value)
        {
            switch (value.Kind)
            {
                case JsonKind.String:
                    return value.Text;
                case JsonKind.Number:
                    return Math.Floor(value.Number) == value.Number && Math.Abs(value.Number) < 1e15
                        ? ((long)value.Number).ToString(CultureInfo.InvariantCulture)
                        : value.Number.ToString("R", CultureInfo.InvariantCulture);
                case JsonKind.Bool:
                    return value.Bool ? "1" : "0";
                default:
                    return null;
            }
        }

        /// <summary>The supports a batch may name: those of the panel that stand by themselves (the ids of SupportCatalog.Listed that need no host).</summary>
        public static List<string> SupportIds()
        {
            var ids = new List<string>();
            foreach (SupportDef def in SupportCatalog.Listed)
            {
                if (!NeedsHost(def.Kind))
                    ids.Add(def.Id);
            }

            return ids;
        }

        /// <summary>
        /// What a catalogue id takes, for the sender (Describe): {"catalog", "template", "zh", "en", "fields":[{"key", "type": text | choice | flag, "default", "maxLength", "digitsOnly", "choices":[{"value",
        /// "zh", "en"}]}], "preset":{key: value}, "supports":[ids]}, or {"catalog", "error"} for an id that is no board.
        /// </summary>
        public static string Describe(string catalogId)
        {
            var w = new JsonWriter().BeginObject().Key("catalog").String(catalogId ?? "");
            SignChoice choice = string.IsNullOrEmpty(catalogId) ? null : SignCatalog.ChoiceById(catalogId);
            if (choice == null || choice.Def.IsMarking)
                return w.Key("error").String(choice == null ? "unknown catalog" : "a marking is not a board").EndObject().ToString();

            w.Key("template").String(choice.Def.Id).Key("zh").String(choice.Zh ?? "").Key("en").String(choice.En ?? "");
            GuideTemplate template = choice.Def.Template;
            GuideSpec preset = template == null ? null : string.IsNullOrEmpty(choice.Spec) ? template.Default() : template.Parse(choice.Spec);
            w.Key("fields").BeginArray();
            if (template != null)
            {
                foreach (GuideField field in template.Fields)
                    DescribeField(w, field);
            }

            w.EndArray().Key("preset").BeginObject();
            if (template != null)
            {
                foreach (GuideField field in template.Fields)
                    w.Key(field.Key).String(preset.Get(field.Key));
            }

            w.EndObject().Key("supports").BeginArray();
            foreach (string id in SupportIds())
                w.String(id);
            return w.EndArray().EndObject().ToString();
        }

        private static void DescribeField(JsonWriter w, GuideField field)
        {
            w.BeginObject().Key("key").String(field.Key).Key("type").String(field.Kind == GuideFieldKind.Choice ? "choice" : field.Kind == GuideFieldKind.Flag ? "flag" : "text")
                .Key("default").String(field.Default ?? "");
            if (field.Kind == GuideFieldKind.Text)
                w.Key("maxLength").Number((long)field.MaxLength).Key("digitsOnly").Bool(field.DigitsOnly);
            if (field.Kind == GuideFieldKind.Text && field.Negative)
                w.Key("signed").Bool(true);
            if (field.Kind == GuideFieldKind.Flag)
                w.Key("values").BeginArray().String("0").String("1").EndArray();
            if (field.Kind == GuideFieldKind.Choice)
            {
                w.Key("choices").BeginArray();
                foreach (GuideChoice choice in field.Choices)
                    w.BeginObject().Key("value").String(choice.Value).Key("zh").String(choice.Zh ?? "").Key("en").String(choice.En ?? "").EndObject();
                w.EndArray();
            }

            w.EndObject();
        }
    }
}
