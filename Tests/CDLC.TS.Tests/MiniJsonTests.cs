using System;
using System.Text;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>0.12.67: the strict JSON reader and the writer of the batch interface (the contract is strings in and out; the tests have no Newtonsoft).</summary>
    internal static class MiniJsonTests
    {
        public static void RunAll()
        {
            Program.Run("json: scalars, nesting and whitespace", Scalars);
            Program.Run("json: strings and escapes", Strings);
            Program.Run("json: numbers", Numbers);
            Program.Run("json: malformed text is refused with a reason, never thrown", Malformed);
            Program.Run("json: the depth limit holds", Depth);
            Program.Run("json: the writer escapes and places commas", Writer);
            Program.Run("json: what the writer writes, the reader reads back", RoundTrip);
        }

        private static JsonValue Parse(string text)
        {
            bool ok = MiniJson.TryParse(text, out JsonValue value, out string error);
            Program.Check(ok && error == null, $"'{text}' parses (error: {error})");
            return value;
        }

        private static void Scalars()
        {
            Program.Check(Parse("true").Bool && Parse("true").Kind == JsonKind.Bool, "true");
            Program.Check(!Parse("false").Bool && Parse("false").Kind == JsonKind.Bool, "false");
            Program.Check(Parse("null").Kind == JsonKind.Null, "null");

            JsonValue v = Parse(" \t\r\n{ \"v\" : 1 , \"items\" : [ { \"i\" : 0 } , [ ] , { } ] }\n ");
            Program.Check(v.Kind == JsonKind.Object && v.Members.Count == 2, "an object of two members");
            Program.Check(v.Get("v").Number == 1.0, "v is 1");
            JsonValue items = v.Get("items");
            Program.Check(items.Kind == JsonKind.Array && items.Items.Count == 3, "three items");
            Program.Check(items.Items[0].Get("i").Number == 0.0, "the first has i = 0");
            Program.Check(items.Items[1].Kind == JsonKind.Array && items.Items[1].Items.Count == 0, "an empty array");
            Program.Check(items.Items[2].Kind == JsonKind.Object && items.Items[2].Members.Count == 0, "an empty object");
            Program.Check(v.Get("nothing") == null, "an absent key is null");

            JsonValue dup = Parse("{\"a\":1,\"b\":2,\"a\":3}");
            Program.Check(dup.Members.Count == 3 && dup.Get("a").Number == 3.0, "a repeated key is kept and the last wins");
            Program.Check(dup.Members[0].Key == "a" && dup.Members[1].Key == "b", "the members keep their order");

            JsonValue bom = Parse("\uFEFF{\"a\":1}");
            Program.Check(bom.Get("a").Number == 1.0, "a byte-order mark is skipped");
        }

        private static void Strings()
        {
            Program.Check(Parse("\"\"").Text == "", "an empty string");
            Program.Check(Parse("\"a\\\"b\\\\c\\/d\"").Text == "a\"b\\c/d", "quote, backslash and solidus");
            Program.Check(Parse("\"\\b\\f\\n\\r\\t\"").Text == "\b\f\n\r\t", "the control escapes");
            Program.Check(Parse("\"\\u00e9\\u4E2D\\u6587\"").Text == "é中文", "\\u escapes, lower and upper case hex");
            Program.Check(Parse("\"\\ud83d\\ude00\"").Text == "\ud83d\ude00", "a surrogate pair stays a pair");
            Program.Check(Parse("\"日照 济宁\"").Text == "日照 济宁", "raw non-ASCII text");
            Program.Check(Parse("{\"to1\":\"日照\"}").Get("to1").Text == "日照", "a Chinese value in an object");
        }

        private static void Numbers()
        {
            Program.Check(Parse("0").Number == 0.0, "0");
            Program.Check(Parse("-0").Number == 0.0, "-0");
            Program.Check(Parse("17").Number == 17.0, "17");
            Program.Check(Parse("-2.5").Number == -2.5, "-2.5");
            Program.Check(Parse("1234.5").Number == 1234.5, "1234.5");
            Program.Check(Parse("1e3").Number == 1000.0, "1e3");
            Program.Check(Parse("1E+3").Number == 1000.0, "1E+3");
            Program.Check(Parse("25e-1").Number == 2.5, "25e-1");
            Program.Check(Parse("0.001").Number == 0.001, "0.001");
            Program.Check(Parse("[1,2]").Items[1].Number == 2.0, "numbers in an array");
        }

        private static void Malformed()
        {
            string[] bad =
            {
                "", "   ", "{", "[", "{\"a\"", "{\"a\":", "{\"a\":1", "{\"a\":1,}", "[1,]", "[1 2]", "{\"a\" 1}", "{a:1}", "{'a':1}", "'a'",
                "\"abc", "\"a\\x\"", "\"\\u12\"", "\"\\uZZZZ\"", "\"a\nb\"", "01", "-", "1.", ".5", "1e", "1e+", "+1", "tru", "nul", "falsey", "{}x", "[] []", "1 2", "NaN", "Infinity",
                "1e999", "{\"a\":1}}", "[1,,2]", "{,}", "\"\\"
            };
            foreach (string text in bad)
            {
                bool ok = MiniJson.TryParse(text, out JsonValue value, out string error);
                Program.Check(!ok && value == null && !string.IsNullOrEmpty(error), $"'{text.Replace("\n", "\\n")}' is refused with a reason (got ok {ok}, '{error}')");
            }

            Program.Check(!MiniJson.TryParse(null, out _, out string none) && none == "empty", "null text is refused");
        }

        private static void Depth()
        {
            string ok = new string('[', MiniJson.MaxDepth) + new string(']', MiniJson.MaxDepth);
            Program.Check(MiniJson.TryParse(ok, out _, out _), "nesting at the limit parses");
            string deep = new string('[', MiniJson.MaxDepth + 1) + new string(']', MiniJson.MaxDepth + 1);
            Program.Check(!MiniJson.TryParse(deep, out _, out string error) && error.Contains("deeper"), "one deeper is refused");
            // 200 000 levels must not overflow the stack: refused at the limit, never recursed into.
            string hostile = new string('[', 200000);
            Program.Check(!MiniJson.TryParse(hostile, out _, out _), "a hostile depth is refused");
            var objects = new StringBuilder();
            for (int i = 0; i < MiniJson.MaxDepth + 1; i++)
                objects.Append("{\"a\":");
            Program.Check(!MiniJson.TryParse(objects.ToString(), out _, out string objectError) && objectError.Contains("deeper"), "objects are limited too");
        }

        private static void Writer()
        {
            string text = new JsonWriter().BeginObject().Key("batch").String("a\"b\\c\n\t\r\u0001").Key("queued").Number(3L).Key("items").BeginArray()
                .BeginObject().Key("i").Number(0L).Key("ignoredKeys").BeginArray().EndArray().EndObject()
                .BeginObject().Key("i").Number(1L).Key("ignoredKeys").BeginArray().String("x").String("y").EndArray().EndObject()
                .EndArray().Key("ok").Bool(true).Key("no").Bool(false).EndObject().ToString();
            Program.Check(text == "{\"batch\":\"a\\\"b\\\\c\\n\\t\\r\\u0001\",\"queued\":3,\"items\":[{\"i\":0,\"ignoredKeys\":[]},{\"i\":1,\"ignoredKeys\":[\"x\",\"y\"]}],\"ok\":true,\"no\":false}", "the text is exact: " + text);
            Program.Check(new JsonWriter().BeginArray().Number(1.5).Number(double.NaN).Number(double.PositiveInfinity).Number(-2L).EndArray().ToString() == "[1.5,null,null,-2]", "doubles, and no NaN or infinity");
            Program.Check(new JsonWriter().BeginObject().EndObject().ToString() == "{}", "an empty object");
            Program.Check(new JsonWriter().BeginArray().BeginArray().EndArray().BeginArray().EndArray().EndArray().ToString() == "[[],[]]", "arrays in an array");
            Program.Check(new JsonWriter().String(null).ToString() == "\"\"", "a null string is empty");
        }

        private static void RoundTrip()
        {
            string tricky = "a\"b\\c/d\n\r\t\b\f\u0001\u001f 日照 \ud83d\ude00 é";
            string text = new JsonWriter().BeginObject().Key("k\"ey").String(tricky).Key("n").Number(0.1 + 0.2).Key("m").Number(-1e-7).EndObject().ToString();
            JsonValue v = Parse(text);
            Program.Check(v.Get("k\"ey").Text == tricky, "a string with every escape comes back whole");
            Program.Check(v.Get("n").Number == 0.1 + 0.2, "a double comes back exactly");
            Program.Check(v.Get("m").Number == -1e-7, "a small double comes back exactly");
        }
    }
}
