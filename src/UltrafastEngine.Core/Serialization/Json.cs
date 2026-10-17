using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace UltrafastEngine.Serialization
{
    /// <summary>JSON value kinds.</summary>
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// A small, dependency-free JSON document model used for the system file (.ufs), the run spec, results
    /// metadata and the C API. Accepts // and /* */ comments and trailing commas (config files are hand
    /// edited); always writes strict JSON. Numbers are doubles (written round-trip exact, invariant culture).
    /// Chosen over System.Text.Json so the core has no NuGet dependency and needs no reflection (NativeAOT).
    /// </summary>
    public abstract class JsonValue
    {
        public abstract JsonKind Kind { get; }

        public static readonly JsonValue Null = new JsonNull();

        public bool IsNull => Kind == JsonKind.Null;

        // ---------- typed accessors (throw with a clear message on mismatch) ----------
        public virtual double AsDouble() => throw new JsonException("Expected a number, found " + Kind + ".");
        public virtual bool AsBool() => throw new JsonException("Expected true/false, found " + Kind + ".");
        public virtual string AsString() => throw new JsonException("Expected a string, found " + Kind + ".");
        public virtual JsonArray AsArray() => throw new JsonException("Expected an array, found " + Kind + ".");
        public virtual JsonObject AsObject() => throw new JsonException("Expected an object, found " + Kind + ".");
        public int AsInt() => (int)Math.Round(AsDouble());

        /// <summary>Array of numbers -> double[] (a single number becomes a 1-element array).</summary>
        public double[] AsDoubleArray()
        {
            if (Kind == JsonKind.Number) return new[] { AsDouble() };
            var a = AsArray();
            var r = new double[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = a[i].AsDouble();
            return r;
        }

        public double[][] AsDoubleMatrix()
        {
            var a = AsArray();
            var r = new double[a.Count][];
            for (int i = 0; i < r.Length; i++) r[i] = a[i].AsDoubleArray();
            return r;
        }

        public string[] AsStringArray()
        {
            if (Kind == JsonKind.String) return new[] { AsString() };
            var a = AsArray();
            return a.Select(v => v.AsString()).ToArray();
        }

        // ---------- construction helpers ----------
        public static implicit operator JsonValue(double v) => new JsonNumber(v);
        public static implicit operator JsonValue(int v) => new JsonNumber(v);
        public static implicit operator JsonValue(bool v) => v ? JsonBool.True : JsonBool.False;
        public static implicit operator JsonValue(string v) => v == null ? Null : new JsonString(v);

        public static JsonArray FromArray(IEnumerable<double> values) => new JsonArray(values.Select(v => (JsonValue)new JsonNumber(v)));
        public static JsonArray FromArray(IEnumerable<string> values) => new JsonArray(values.Select(v => (JsonValue)new JsonString(v)));
        public static JsonArray FromMatrix(double[][] rows) => new JsonArray(rows.Select(r => (JsonValue)FromArray(r)));

        // ---------- parse / write ----------
        public static JsonValue Parse(string text) => new JsonParser(text).ParseDocument();
        public static JsonValue ParseFile(string path) => Parse(File.ReadAllText(path));

        public string ToJson(bool indented = true)
        {
            var sb = new StringBuilder();
            Write(sb, indented ? 0 : -1);
            return sb.ToString();
        }

        public override string ToString() => ToJson(false);

        internal abstract void Write(StringBuilder sb, int indent);

        internal static void NewLine(StringBuilder sb, int indent)
        {
            if (indent < 0) return;
            sb.Append('\n');
            sb.Append(' ', indent * 2);
        }

        internal static string FormatNumber(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "null"; // strict JSON has no NaN/Inf
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
            string s = v.ToString("R", CultureInfo.InvariantCulture);
            return s;
        }
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    public sealed class JsonNull : JsonValue
    {
        public override JsonKind Kind => JsonKind.Null;
        /// <summary>NaN/Infinity are written as null (strict JSON), so null reads back as NaN where a number is expected.</summary>
        public override double AsDouble() => double.NaN;
        internal override void Write(StringBuilder sb, int indent) => sb.Append("null");
    }

    public sealed class JsonBool : JsonValue
    {
        public static readonly JsonBool True = new JsonBool(true), False = new JsonBool(false);
        public bool Value { get; }
        private JsonBool(bool v) { Value = v; }
        public override JsonKind Kind => JsonKind.Bool;
        public override bool AsBool() => Value;
        internal override void Write(StringBuilder sb, int indent) => sb.Append(Value ? "true" : "false");
    }

    public sealed class JsonNumber : JsonValue
    {
        public double Value { get; }
        public JsonNumber(double v) { Value = v; }
        public override JsonKind Kind => JsonKind.Number;
        public override double AsDouble() => Value;
        public override bool AsBool() => Value != 0;
        internal override void Write(StringBuilder sb, int indent) => sb.Append(FormatNumber(Value));
    }

    public sealed class JsonString : JsonValue
    {
        public string Value { get; }
        public JsonString(string v) { Value = v ?? ""; }
        public override JsonKind Kind => JsonKind.String;
        public override string AsString() => Value;
        public override double AsDouble()
        {
            // Accept "1e-3" style strings and the explicit non-finite tokens.
            if (Value == "NaN") return double.NaN;
            if (Value == "Infinity" || Value == "inf") return double.PositiveInfinity;
            if (Value == "-Infinity" || Value == "-inf") return double.NegativeInfinity;
            if (double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            return base.AsDouble();
        }
        internal override void Write(StringBuilder sb, int indent) => WriteString(sb, Value);

        internal static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    public sealed class JsonArray : JsonValue, IEnumerable<JsonValue>
    {
        private readonly List<JsonValue> _items;
        public JsonArray() { _items = new List<JsonValue>(); }
        public JsonArray(IEnumerable<JsonValue> items) { _items = new List<JsonValue>(items); }
        public override JsonKind Kind => JsonKind.Array;
        public override JsonArray AsArray() => this;
        public int Count => _items.Count;
        public JsonValue this[int i] { get => _items[i]; set => _items[i] = value ?? Null; }
        public JsonArray Add(JsonValue v) { _items.Add(v ?? Null); return this; }
        public IEnumerator<JsonValue> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        internal override void Write(StringBuilder sb, int indent)
        {
            if (_items.Count == 0) { sb.Append("[]"); return; }
            // Short numeric arrays stay on one line -- keeps per-wavelength data readable.
            bool flat = indent < 0 || _items.All(v => v.Kind == JsonKind.Number || v.Kind == JsonKind.Bool || v.Kind == JsonKind.Null || (v.Kind == JsonKind.String && ((JsonString)v).Value.Length < 24));
            sb.Append('[');
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0) sb.Append(flat && indent >= 0 ? ", " : ",");
                if (!flat) NewLine(sb, indent + 1);
                _items[i].Write(sb, flat ? -1 : indent + 1);
            }
            if (!flat) NewLine(sb, indent);
            sb.Append(']');
        }
    }

    /// <summary>Insertion-ordered JSON object with case-sensitive keys.</summary>
    public sealed class JsonObject : JsonValue, IEnumerable<KeyValuePair<string, JsonValue>>
    {
        private readonly List<KeyValuePair<string, JsonValue>> _items = new List<KeyValuePair<string, JsonValue>>();
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);

        public override JsonKind Kind => JsonKind.Object;
        public override JsonObject AsObject() => this;
        public int Count => _items.Count;
        public IEnumerable<string> Keys => _items.Select(p => p.Key);

        public JsonValue this[string key]
        {
            get => _index.TryGetValue(key, out int i) ? _items[i].Value : null;
            set => Set(key, value);
        }

        public JsonObject Set(string key, JsonValue value)
        {
            value = value ?? Null;
            if (_index.TryGetValue(key, out int i)) _items[i] = new KeyValuePair<string, JsonValue>(key, value);
            else { _index[key] = _items.Count; _items.Add(new KeyValuePair<string, JsonValue>(key, value)); }
            return this;
        }

        /// <summary>Remove a key (keeps the order of the others). Returns false if it was not there.</summary>
        public bool Remove(string key)
        {
            if (!_index.TryGetValue(key, out int i)) return false;
            _items.RemoveAt(i);
            _index.Clear();
            for (int k = 0; k < _items.Count; k++) _index[_items[k].Key] = k;
            return true;
        }

        public bool Has(string key) => _index.ContainsKey(key) && !this[key].IsNull;
        public bool TryGet(string key, out JsonValue v) { v = this[key]; return v != null && !v.IsNull; }

        public double GetDouble(string key, double fallback) => TryGet(key, out var v) ? v.AsDouble() : fallback;
        public double? GetDoubleOrNull(string key) => TryGet(key, out var v) ? v.AsDouble() : (double?)null;
        public int GetInt(string key, int fallback) => TryGet(key, out var v) ? v.AsInt() : fallback;
        public bool GetBool(string key, bool fallback) => TryGet(key, out var v) ? v.AsBool() : fallback;
        public string GetString(string key, string fallback = null) => TryGet(key, out var v) ? (v.Kind == JsonKind.String ? v.AsString() : v.ToJson(false)) : fallback;
        public JsonObject GetObject(string key) => TryGet(key, out var v) ? v.AsObject() : null;
        public JsonArray GetArray(string key) => TryGet(key, out var v) ? v.AsArray() : null;
        public double[] GetDoubleArray(string key) => TryGet(key, out var v) ? v.AsDoubleArray() : null;

        public double GetRequiredDouble(string key, string context)
        {
            if (!TryGet(key, out var v)) throw new JsonException(context + ": missing required number \"" + key + "\".");
            return v.AsDouble();
        }

        public string GetRequiredString(string key, string context)
        {
            if (!TryGet(key, out var v)) throw new JsonException(context + ": missing required string \"" + key + "\".");
            return v.AsString();
        }

        public IEnumerator<KeyValuePair<string, JsonValue>> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        internal override void Write(StringBuilder sb, int indent)
        {
            if (_items.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NewLine(sb, indent < 0 ? -1 : indent + 1);
                JsonString.WriteString(sb, _items[i].Key);
                sb.Append(indent < 0 ? ":" : ": ");
                _items[i].Value.Write(sb, indent < 0 ? -1 : indent + 1);
            }
            NewLine(sb, indent);
            sb.Append('}');
        }
    }

    internal sealed class JsonParser
    {
        private readonly string _s;
        private int _p;

        public JsonParser(string s) { _s = s ?? ""; }

        public JsonValue ParseDocument()
        {
            SkipWs();
            var v = ParseValue();
            SkipWs();
            if (_p < _s.Length) throw Error("Unexpected trailing content");
            return v;
        }

        private JsonException Error(string msg)
        {
            int line = 1, col = 1;
            for (int i = 0; i < _p && i < _s.Length; i++) { if (_s[i] == '\n') { line++; col = 1; } else col++; }
            return new JsonException("JSON parse error at line " + line + ", column " + col + ": " + msg + ".");
        }

        private void SkipWs()
        {
            while (_p < _s.Length)
            {
                char c = _s[_p];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') { _p++; continue; }
                if (c == '/' && _p + 1 < _s.Length && _s[_p + 1] == '/')
                {
                    while (_p < _s.Length && _s[_p] != '\n') _p++;
                    continue;
                }
                if (c == '/' && _p + 1 < _s.Length && _s[_p + 1] == '*')
                {
                    int end = _s.IndexOf("*/", _p + 2, StringComparison.Ordinal);
                    if (end < 0) throw Error("Unterminated /* comment");
                    _p = end + 2;
                    continue;
                }
                break;
            }
        }

        private JsonValue ParseValue()
        {
            if (_p >= _s.Length) throw Error("Unexpected end of input");
            char c = _s[_p];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return new JsonString(ParseString());
                case 't': Expect("true"); return JsonBool.True;
                case 'f': Expect("false"); return JsonBool.False;
                case 'n': Expect("null"); return JsonValue.Null;
                case 'N': Expect("NaN"); return new JsonNumber(double.NaN);
                case 'I': Expect("Infinity"); return new JsonNumber(double.PositiveInfinity);
                default:
                    if (c == '-' || c == '+' || (c >= '0' && c <= '9') || c == '.') return ParseNumber();
                    throw Error("Unexpected character '" + c + "'");
            }
        }

        private void Expect(string word)
        {
            if (string.CompareOrdinal(_s, _p, word, 0, word.Length) != 0) throw Error("Expected '" + word + "'");
            _p += word.Length;
        }

        private JsonObject ParseObject()
        {
            var o = new JsonObject();
            _p++; // {
            SkipWs();
            if (_p < _s.Length && _s[_p] == '}') { _p++; return o; }
            while (true)
            {
                SkipWs();
                if (_p < _s.Length && _s[_p] == '}') { _p++; return o; } // trailing comma
                if (_p >= _s.Length || _s[_p] != '"') throw Error("Expected a quoted key");
                string key = ParseString();
                SkipWs();
                if (_p >= _s.Length || _s[_p] != ':') throw Error("Expected ':' after key \"" + key + "\"");
                _p++;
                SkipWs();
                o.Set(key, ParseValue());
                SkipWs();
                if (_p >= _s.Length) throw Error("Unterminated object");
                if (_s[_p] == ',') { _p++; continue; }
                if (_s[_p] == '}') { _p++; return o; }
                throw Error("Expected ',' or '}'");
            }
        }

        private JsonArray ParseArray()
        {
            var a = new JsonArray();
            _p++; // [
            SkipWs();
            if (_p < _s.Length && _s[_p] == ']') { _p++; return a; }
            while (true)
            {
                SkipWs();
                if (_p < _s.Length && _s[_p] == ']') { _p++; return a; } // trailing comma
                a.Add(ParseValue());
                SkipWs();
                if (_p >= _s.Length) throw Error("Unterminated array");
                if (_s[_p] == ',') { _p++; continue; }
                if (_s[_p] == ']') { _p++; return a; }
                throw Error("Expected ',' or ']'");
            }
        }

        private string ParseString()
        {
            _p++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (_p >= _s.Length) throw Error("Unterminated string");
                char c = _s[_p++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (_p >= _s.Length) throw Error("Bad escape");
                char e = _s[_p++];
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
                        if (_p + 4 > _s.Length) throw Error("Bad \\u escape");
                        sb.Append((char)int.Parse(_s.Substring(_p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _p += 4;
                        break;
                    default: throw Error("Unknown escape \\" + e);
                }
            }
        }

        private JsonValue ParseNumber()
        {
            int start = _p;
            if (_s[_p] == '-' || _s[_p] == '+') _p++;
            if (_p < _s.Length && _s[_p] == 'I') { Expect("Infinity"); return new JsonNumber(_s[start] == '-' ? double.NegativeInfinity : double.PositiveInfinity); }
            while (_p < _s.Length)
            {
                char c = _s[_p];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+') _p++;
                else break;
            }
            string tok = _s.Substring(start, _p - start);
            if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw Error("Invalid number '" + tok + "'");
            return new JsonNumber(d);
        }
    }
}
