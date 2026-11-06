using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>A parsed JSON value. The members of an object keep their order and a repeated key is kept (Get takes the last, as the common readers do).</summary>
    public sealed class JsonValue
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        public string Text = "";
        public readonly List<JsonValue> Items = new List<JsonValue>();
        public readonly List<KeyValuePair<string, JsonValue>> Members = new List<KeyValuePair<string, JsonValue>>();

        public JsonValue Get(string key)
        {
            for (int i = Members.Count - 1; i >= 0; i--)
            {
                if (Members[i].Key == key)
                    return Members[i].Value;
            }

            return null;
        }
    }

    /// <summary>
    /// A small strict JSON reader and writer (0.12.67), for the batch interface: the contract is strings in and out, and the tests do not reference the game's Newtonsoft. Strict: no comments,
    /// no trailing commas, no single quotes, no leading zeros, no raw control characters in a string.
    /// </summary>
    public static class MiniJson
    {
        /// <summary>How deep arrays and objects may nest: a request is three deep; the limit keeps a hostile one from exhausting the stack.</summary>
        public const int MaxDepth = 12;

        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            value = null;
            error = null;
            if (string.IsNullOrEmpty(text))
            {
                error = "empty";
                return false;
            }

            var reader = new Reader(text);
            try
            {
                reader.SkipSpace();
                value = reader.ReadValue(0);
                reader.SkipSpace();
                if (!reader.AtEnd)
                    throw reader.Fail("unexpected text after the value");
                return true;
            }
            catch (FormatException ex)
            {
                value = null;
                error = ex.Message;
                return false;
            }
        }

        private sealed class Reader
        {
            private readonly string m_Text;
            private int m_At;

            public Reader(string text)
            {
                m_Text = text;
                // A byte-order mark is not part of the text.
                if (text.Length > 0 && text[0] == (char)0xFEFF)
                    m_At = 1;
            }

            public bool AtEnd => m_At >= m_Text.Length;

            public FormatException Fail(string what) => new FormatException(what + " at " + m_At);

            public void SkipSpace()
            {
                while (m_At < m_Text.Length)
                {
                    char c = m_Text[m_At];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                        break;
                    m_At++;
                }
            }

            public JsonValue ReadValue(int depth)
            {
                if (AtEnd)
                    throw Fail("the text ends where a value is expected");
                char c = m_Text[m_At];
                switch (c)
                {
                    case '{':
                        return ReadObject(depth);
                    case '[':
                        return ReadArray(depth);
                    case '"':
                        return new JsonValue { Kind = JsonKind.String, Text = ReadString() };
                    case 't':
                        Expect("true");
                        return new JsonValue { Kind = JsonKind.Bool, Bool = true };
                    case 'f':
                        Expect("false");
                        return new JsonValue { Kind = JsonKind.Bool };
                    case 'n':
                        Expect("null");
                        return new JsonValue { Kind = JsonKind.Null };
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ReadNumber();
                        throw Fail("unexpected '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(m_Text, m_At, word, 0, word.Length) != 0)
                    throw Fail("expected " + word);
                m_At += word.Length;
            }

            private JsonValue ReadObject(int depth)
            {
                if (depth >= MaxDepth)
                    throw Fail("nested deeper than " + MaxDepth);
                var value = new JsonValue { Kind = JsonKind.Object };
                m_At++;
                SkipSpace();
                if (!AtEnd && m_Text[m_At] == '}')
                {
                    m_At++;
                    return value;
                }

                while (true)
                {
                    SkipSpace();
                    if (AtEnd || m_Text[m_At] != '"')
                        throw Fail("expected a key");
                    string key = ReadString();
                    SkipSpace();
                    if (AtEnd || m_Text[m_At] != ':')
                        throw Fail("expected ':'");
                    m_At++;
                    SkipSpace();
                    value.Members.Add(new KeyValuePair<string, JsonValue>(key, ReadValue(depth + 1)));
                    SkipSpace();
                    if (AtEnd)
                        throw Fail("the object is not closed");
                    char c = m_Text[m_At++];
                    if (c == '}')
                        return value;
                    if (c != ',')
                        throw Fail("expected ',' or '}'");
                }
            }

            private JsonValue ReadArray(int depth)
            {
                if (depth >= MaxDepth)
                    throw Fail("nested deeper than " + MaxDepth);
                var value = new JsonValue { Kind = JsonKind.Array };
                m_At++;
                SkipSpace();
                if (!AtEnd && m_Text[m_At] == ']')
                {
                    m_At++;
                    return value;
                }

                while (true)
                {
                    SkipSpace();
                    value.Items.Add(ReadValue(depth + 1));
                    SkipSpace();
                    if (AtEnd)
                        throw Fail("the array is not closed");
                    char c = m_Text[m_At++];
                    if (c == ']')
                        return value;
                    if (c != ',')
                        throw Fail("expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                m_At++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                        throw Fail("the string is not closed");
                    char c = m_Text[m_At++];
                    if (c == '"')
                        return sb.ToString();
                    if (c < 0x20)
                        throw Fail("a control character in a string");
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (AtEnd)
                        throw Fail("the string is not closed");
                    char e = m_Text[m_At++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (m_At + 4 > m_Text.Length || !int.TryParse(m_Text.Substring(m_At, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                                throw Fail("a bad \\u escape");
                            sb.Append((char)code);
                            m_At += 4;
                            break;
                        default:
                            throw Fail("a bad escape '\\" + e + "'");
                    }
                }
            }

            private JsonValue ReadNumber()
            {
                int start = m_At;
                if (m_Text[m_At] == '-')
                    m_At++;
                if (AtEnd || m_Text[m_At] < '0' || m_Text[m_At] > '9')
                    throw Fail("a number has no digits");
                if (m_Text[m_At] == '0')
                {
                    m_At++;
                    if (!AtEnd && m_Text[m_At] >= '0' && m_Text[m_At] <= '9')
                        throw Fail("a number with a leading zero");
                }
                else
                {
                    SkipDigits();
                }

                if (!AtEnd && m_Text[m_At] == '.')
                {
                    m_At++;
                    if (AtEnd || m_Text[m_At] < '0' || m_Text[m_At] > '9')
                        throw Fail("a number has no digits after the point");
                    SkipDigits();
                }

                if (!AtEnd && (m_Text[m_At] == 'e' || m_Text[m_At] == 'E'))
                {
                    m_At++;
                    if (!AtEnd && (m_Text[m_At] == '+' || m_Text[m_At] == '-'))
                        m_At++;
                    if (AtEnd || m_Text[m_At] < '0' || m_Text[m_At] > '9')
                        throw Fail("a number has no exponent digits");
                    SkipDigits();
                }

                if (!double.TryParse(m_Text.Substring(start, m_At - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsInfinity(number))
                    throw Fail("a number out of range");
                return new JsonValue { Kind = JsonKind.Number, Number = number };
            }

            private void SkipDigits()
            {
                while (!AtEnd && m_Text[m_At] >= '0' && m_Text[m_At] <= '9')
                    m_At++;
            }
        }
    }

    /// <summary>Writes JSON text: objects, arrays, strings and numbers, commas placed by the writer. Calls out of order are a programming error, not checked.</summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder m_Text = new StringBuilder();
        private readonly List<bool> m_Started = new List<bool>();
        private bool m_AfterKey;

        public JsonWriter BeginObject()
        {
            Element();
            m_Text.Append('{');
            m_Started.Add(false);
            return this;
        }

        public JsonWriter EndObject()
        {
            m_Started.RemoveAt(m_Started.Count - 1);
            m_Text.Append('}');
            return this;
        }

        public JsonWriter BeginArray()
        {
            Element();
            m_Text.Append('[');
            m_Started.Add(false);
            return this;
        }

        public JsonWriter EndArray()
        {
            m_Started.RemoveAt(m_Started.Count - 1);
            m_Text.Append(']');
            return this;
        }

        public JsonWriter Key(string key)
        {
            Element();
            AppendQuoted(m_Text, key);
            m_Text.Append(':');
            m_AfterKey = true;
            return this;
        }

        public JsonWriter String(string value)
        {
            Element();
            AppendQuoted(m_Text, value);
            return this;
        }

        public JsonWriter Number(long value)
        {
            Element();
            m_Text.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>A number that round-trips; not a number or infinite is written as null (JSON has no such number).</summary>
        public JsonWriter Number(double value)
        {
            Element();
            m_Text.Append(double.IsNaN(value) || double.IsInfinity(value) ? "null" : value.ToString("R", CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Bool(bool value)
        {
            Element();
            m_Text.Append(value ? "true" : "false");
            return this;
        }

        public override string ToString() => m_Text.ToString();

        /// <summary>Called before each value or key: the comma between the elements of a container; a value after its key needs none.</summary>
        private void Element()
        {
            if (m_AfterKey)
            {
                m_AfterKey = false;
                return;
            }

            if (m_Started.Count == 0)
                return;
            if (m_Started[m_Started.Count - 1])
                m_Text.Append(',');
            m_Started[m_Started.Count - 1] = true;
        }

        /// <summary>A string as JSON text, in quotes, escaped.</summary>
        internal static void AppendQuoted(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            text.Append(c);
                        break;
                }
            }

            text.Append('"');
        }
    }
}
