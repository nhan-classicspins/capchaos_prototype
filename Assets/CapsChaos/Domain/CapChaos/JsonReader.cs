using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Domain
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// One parsed JSON value. Engine-free on purpose: the level format (GDD §6) is parsed inside
    /// Game.Domain so the whole load → validate → play path runs in the headless gate.
    /// Objects keep member ORDER and remember where each value started, for error messages.
    /// </summary>
    public sealed class JsonValue
    {
        public JsonKind Kind { get; }
        public int Line { get; }
        public int Column { get; }
        public bool Bool { get; }
        public double Number { get; }
        public string String { get; }
        public IReadOnlyList<JsonValue> Items { get; }
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members { get; }

        private readonly Dictionary<string, JsonValue> _byName;

        private JsonValue(JsonKind kind, int line, int column, bool b = false, double n = 0, string s = null,
            List<JsonValue> items = null, List<KeyValuePair<string, JsonValue>> members = null)
        {
            Kind = kind; Line = line; Column = column; Bool = b; Number = n; String = s;
            Items = items ?? (IReadOnlyList<JsonValue>)Array.Empty<JsonValue>();
            Members = members ?? (IReadOnlyList<KeyValuePair<string, JsonValue>>)Array.Empty<KeyValuePair<string, JsonValue>>();
            if (members != null)
            {
                _byName = new Dictionary<string, JsonValue>(members.Count, StringComparer.Ordinal);
                foreach (var m in members) _byName[m.Key] = m.Value;
            }
        }

        public bool TryGet(string name, out JsonValue value)
        {
            value = null;
            return _byName != null && _byName.TryGetValue(name, out value);
        }

        public string Where => $"line {Line}, col {Column}";

        internal static JsonValue Null(int l, int c) => new JsonValue(JsonKind.Null, l, c);
        internal static JsonValue Of(bool b, int l, int c) => new JsonValue(JsonKind.Bool, l, c, b: b);
        internal static JsonValue Of(double n, int l, int c) => new JsonValue(JsonKind.Number, l, c, n: n);
        internal static JsonValue Of(string s, int l, int c) => new JsonValue(JsonKind.String, l, c, s: s);
        internal static JsonValue Of(List<JsonValue> a, int l, int c) => new JsonValue(JsonKind.Array, l, c, items: a);
        internal static JsonValue Of(List<KeyValuePair<string, JsonValue>> o, int l, int c) => new JsonValue(JsonKind.Object, l, c, members: o);
    }

    public sealed class JsonParseException : Exception
    {
        public int Line { get; }
        public int Column { get; }
        public JsonParseException(string message, int line, int column)
            : base($"{message} (line {line}, col {column})") { Line = line; Column = column; }
    }

    /// <summary>Strict RFC 8259 reader: no comments, no trailing commas, duplicate keys rejected.</summary>
    public static class JsonReader
    {
        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new Parser(text);
            p.SkipWs();
            var v = p.Value(0);
            p.SkipWs();
            if (!p.End) throw p.Error("unexpected trailing content");
            return v;
        }

        private sealed class Parser
        {
            private const int MaxDepth = 64;
            private readonly string _s;
            private int _i, _line = 1, _col = 1;

            public Parser(string s)
            {
                _s = s;
                if (_s.Length > 0 && _s[0] == '﻿') _i = 1;   // tolerate a UTF-8 BOM
            }

            public bool End => _i >= _s.Length;
            public JsonParseException Error(string m) => new JsonParseException(m, _line, _col);

            private char Peek => _i < _s.Length ? _s[_i] : '\0';

            private char Next()
            {
                char c = _s[_i++];
                if (c == '\n') { _line++; _col = 1; } else _col++;
                return c;
            }

            public void SkipWs()
            {
                while (!End && (Peek == ' ' || Peek == '\t' || Peek == '\n' || Peek == '\r')) Next();
            }

            public JsonValue Value(int depth)
            {
                if (depth > MaxDepth) throw Error("nesting too deep");
                if (End) throw Error("unexpected end of input");
                int l = _line, c = _col;
                switch (Peek)
                {
                    case '{': return Obj(depth, l, c);
                    case '[': return Arr(depth, l, c);
                    case '"': return JsonValue.Of(Str(), l, c);
                    case 't': Word("true"); return JsonValue.Of(true, l, c);
                    case 'f': Word("false"); return JsonValue.Of(false, l, c);
                    case 'n': Word("null"); return JsonValue.Null(l, c);
                    default:
                        if (Peek == '-' || (Peek >= '0' && Peek <= '9')) return JsonValue.Of(Num(), l, c);
                        throw Error($"unexpected character '{Peek}'");
                }
            }

            private void Word(string w)
            {
                foreach (char ch in w)
                {
                    if (End || Peek != ch) throw Error($"invalid literal, expected '{w}'");
                    Next();
                }
            }

            private JsonValue Obj(int depth, int l, int c)
            {
                Next(); // {
                var members = new List<KeyValuePair<string, JsonValue>>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                SkipWs();
                if (Peek == '}') { Next(); return JsonValue.Of(members, l, c); }
                while (true)
                {
                    SkipWs();
                    if (Peek != '"') throw Error("expected a property name");
                    string key = Str();
                    if (!seen.Add(key)) throw Error($"duplicate property '{key}'");
                    SkipWs();
                    if (Peek != ':') throw Error("expected ':'");
                    Next();
                    SkipWs();
                    members.Add(new KeyValuePair<string, JsonValue>(key, Value(depth + 1)));
                    SkipWs();
                    if (Peek == ',') { Next(); continue; }
                    if (Peek == '}') { Next(); return JsonValue.Of(members, l, c); }
                    throw Error("expected ',' or '}'");
                }
            }

            private JsonValue Arr(int depth, int l, int c)
            {
                Next(); // [
                var items = new List<JsonValue>();
                SkipWs();
                if (Peek == ']') { Next(); return JsonValue.Of(items, l, c); }
                while (true)
                {
                    SkipWs();
                    items.Add(Value(depth + 1));
                    SkipWs();
                    if (Peek == ',') { Next(); continue; }
                    if (Peek == ']') { Next(); return JsonValue.Of(items, l, c); }
                    throw Error("expected ',' or ']'");
                }
            }

            private string Str()
            {
                Next(); // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("unterminated string");
                    char ch = Next();
                    if (ch == '"') return sb.ToString();
                    if (ch < 0x20) throw Error("control character in string");
                    if (ch != '\\') { sb.Append(ch); continue; }
                    if (End) throw Error("unterminated escape");
                    char e = Next();
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
                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                if (End) throw Error("truncated \\u escape");
                                int h = Hex(Next());
                                if (h < 0) throw Error("invalid \\u escape");
                                code = code * 16 + h;
                            }
                            sb.Append((char)code);
                            break;
                        default: throw Error($"invalid escape '\\{e}'");
                    }
                }
            }

            private static int Hex(char c) =>
                c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

            private double Num()
            {
                int start = _i;
                if (Peek == '-') Next();
                if (Peek == '0') Next();
                else if (Peek >= '1' && Peek <= '9') { while (Peek >= '0' && Peek <= '9') Next(); }
                else throw Error("invalid number");
                if (Peek == '.')
                {
                    Next();
                    if (!(Peek >= '0' && Peek <= '9')) throw Error("invalid number");
                    while (Peek >= '0' && Peek <= '9') Next();
                }
                if (Peek == 'e' || Peek == 'E')
                {
                    Next();
                    if (Peek == '+' || Peek == '-') Next();
                    if (!(Peek >= '0' && Peek <= '9')) throw Error("invalid number");
                    while (Peek >= '0' && Peek <= '9') Next();
                }
                return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
