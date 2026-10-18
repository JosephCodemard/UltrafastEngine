using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Models
{
    /// <summary>
    /// Where a model (or any overridable value) came from. Higher values win: a run-spec override beats a
    /// system-file override, which beats data the system file carries for that element (e.g. the Zemax-traced
    /// group delay), which beats the material's own default, which beats the engine default.
    /// </summary>
    public enum ModelSource
    {
        EngineDefault = 0,
        MaterialDefault = 1,
        SystemData = 2,
        SystemOverride = 3,
        RunOverride = 4,
        Api = 5
    }

    /// <summary>
    /// A model description as it appears in JSON: <c>{"kind": "sellmeier", "B": [...], "C": [...]}</c>,
    /// <c>{"kind": "expression", "expr": "...", "params": {...}}</c>, <c>{"kind": "callback", "name": "myModel"}</c>,
    /// or a bare number (shorthand for <c>{"kind": "constant", "value": x}</c>).
    /// The same three override forms -- data, expression, callback -- work for every overridable property.
    /// </summary>
    public sealed class ModelSpec
    {
        public string Kind { get; }
        public JsonObject Json { get; }
        public ModelSource Source { get; set; }
        /// <summary>Free text saying where the spec was found (file + path), for the provenance report.</summary>
        public string Origin { get; set; }

        public ModelSpec(string kind, JsonObject json, ModelSource source = ModelSource.EngineDefault, string origin = null)
        {
            Kind = (kind ?? throw new ArgumentNullException(nameof(kind))).Trim();
            Json = json ?? new JsonObject().Set("kind", kind);
            Source = source;
            Origin = origin;
        }

        public static ModelSpec FromJson(JsonValue v, ModelSource source, string origin)
        {
            if (v == null || v.IsNull) return null;
            if (v.Kind == JsonKind.Number)
                return new ModelSpec("constant", new JsonObject().Set("kind", "constant").Set("value", v.AsDouble()), source, origin);
            if (v.Kind == JsonKind.String)
            {
                // "expression text" shorthand, or "@name" for a registered callback.
                string s = v.AsString();
                if (s.StartsWith("@", StringComparison.Ordinal))
                    return new ModelSpec("callback", new JsonObject().Set("kind", "callback").Set("name", s.Substring(1)), source, origin);
                return new ModelSpec("expression", new JsonObject().Set("kind", "expression").Set("expr", s), source, origin);
            }
            var o = v.AsObject();
            string kind = o.GetString("kind");
            if (string.IsNullOrEmpty(kind))
            {
                if (o.Has("expr")) kind = "expression";
                else if (o.Has("callback")) { kind = "callback"; o.Set("name", o.GetString("callback")); }
                else if (o.Has("value")) kind = "constant";
                else throw new JsonException(origin + ": model object needs a \"kind\" (or \"expr\" / \"callback\" / \"value\").");
            }
            return new ModelSpec(kind, o, source, origin);
        }

        public double GetDouble(string key, double fallback) => Json.GetDouble(key, fallback);
        public double GetRequired(string key) => Json.GetRequiredDouble(key, Describe());
        public IReadOnlyDictionary<string, double> Params()
        {
            var p = new Dictionary<string, double>(StringComparer.Ordinal);
            var po = Json.GetObject("params");
            if (po != null) foreach (var kv in po) p[kv.Key] = kv.Value.AsDouble();
            return p;
        }

        public string Describe()
        {
            switch (Kind)
            {
                case "constant": return "constant " + Json.GetDouble("value", double.NaN).ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
                case "expression": return "expression \"" + Json.GetString("expr") + "\"";
                case "callback": return "callback @" + Json.GetString("name");
                default:
                    var name = Json.GetString("name");
                    return Kind + (string.IsNullOrEmpty(name) ? "" : " (" + name + ")");
            }
        }

        public override string ToString() => Describe();
    }

    /// <summary>One line of the provenance report: which model/value was finally used for a target+property, and why.</summary>
    public sealed class ProvenanceEntry
    {
        public string Target { get; set; }
        public string Property { get; set; }
        public string Value { get; set; }
        public ModelSource Source { get; set; }
        public string Origin { get; set; }
        /// <summary>Lower-priority candidates that were shadowed (so a user can see that an override took effect).</summary>
        public List<string> Shadowed { get; } = new List<string>();

        public JsonObject ToJson()
        {
            var o = new JsonObject()
                .Set("target", Target).Set("property", Property).Set("value", Value)
                .Set("source", Source.ToString()).Set("origin", Origin);
            if (Shadowed.Count > 0) o.Set("shadowed", JsonValue.FromArray(Shadowed));
            return o;
        }
    }

    /// <summary>Collects provenance entries during compilation; exported as results/provenance.json.</summary>
    public sealed class ProvenanceLog
    {
        private readonly List<ProvenanceEntry> _entries = new List<ProvenanceEntry>();
        public IReadOnlyList<ProvenanceEntry> Entries => _entries;

        public ProvenanceEntry Record(string target, string property, string value, ModelSource source, string origin)
        {
            var e = new ProvenanceEntry { Target = target, Property = property, Value = value, Source = source, Origin = origin };
            _entries.Add(e);
            return e;
        }

        public JsonArray ToJson() => new JsonArray(_entries.Select(e => (JsonValue)e.ToJson()));
    }

    /// <summary>
    /// An override rule: <c>{"target": "element:plate1", "property": "timeResponse", "model": {...}}</c>.
    /// Targets: <c>material:NAME</c>, <c>element:ID</c>, <c>element:*</c> (all elements), <c>type:bulk</c> (all
    /// elements of a type), or <c>global</c>. Properties are listed in docs/reference/models.md.
    /// <c>"mode": "replace"</c> (default) or <c>"add"</c> (for additive properties such as timeResponse and
    /// filter: the model is applied in addition to the existing ones).
    /// </summary>
    public sealed class OverrideRule
    {
        public string Target { get; set; }
        public string Property { get; set; }
        public ModelSpec Model { get; set; }
        public JsonValue RawValue { get; set; }
        public bool Additive { get; set; }
        public ModelSource Source { get; set; }
        public string Origin { get; set; }

        public static OverrideRule FromJson(JsonObject o, ModelSource source, string origin)
        {
            var r = new OverrideRule
            {
                Target = o.GetRequiredString("target", origin),
                Property = o.GetRequiredString("property", origin),
                Additive = string.Equals(o.GetString("mode", "replace"), "add", StringComparison.OrdinalIgnoreCase),
                Source = source,
                Origin = origin
            };
            JsonValue mv = o["model"] ?? o["value"];
            if (mv == null) throw new JsonException(origin + ": override needs \"model\" or \"value\".");
            r.RawValue = mv;
            r.Model = ModelSpec.FromJson(mv, source, origin);
            return r;
        }

        public bool Matches(string kind, string id, string type)
        {
            if (string.Equals(Target, "global", StringComparison.OrdinalIgnoreCase)) return kind == "global";
            int colon = Target.IndexOf(':');
            if (colon < 0) return false;
            string tk = Target.Substring(0, colon).Trim().ToLowerInvariant();
            string tv = Target.Substring(colon + 1).Trim();
            if (tk == "type") return kind == "element" && string.Equals(tv, type, StringComparison.OrdinalIgnoreCase);
            if (tk != kind) return false;
            if (tv == "*") return true;
            if (tv.EndsWith("*", StringComparison.Ordinal))
                return id != null && id.StartsWith(tv.Substring(0, tv.Length - 1), StringComparison.OrdinalIgnoreCase);
            return string.Equals(tv, id, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>All override rules from the system file and the run spec, with priority-ordered lookup.</summary>
    public sealed class OverrideSet
    {
        private readonly List<OverrideRule> _rules = new List<OverrideRule>();
        public IReadOnlyList<OverrideRule> Rules => _rules;

        public void Add(OverrideRule r) => _rules.Add(r);
        public void AddRange(IEnumerable<OverrideRule> rs) { foreach (var r in rs) _rules.Add(r); }

        public static List<OverrideRule> Parse(JsonArray arr, ModelSource source, string origin)
        {
            var list = new List<OverrideRule>();
            if (arr == null) return list;
            for (int i = 0; i < arr.Count; i++) list.Add(OverrideRule.FromJson(arr[i].AsObject(), source, origin + " overrides[" + i + "]"));
            return list;
        }

        /// <summary>The single winning replace-mode rule (highest source; the later rule wins a tie), or null.</summary>
        public OverrideRule FindReplace(string kind, string id, string type, string property)
        {
            OverrideRule best = null;
            foreach (var r in _rules)
            {
                if (r.Additive || !string.Equals(r.Property, property, StringComparison.OrdinalIgnoreCase) || !r.Matches(kind, id, type)) continue;
                if (best == null || r.Source >= best.Source) best = r;
            }
            return best;
        }

        /// <summary>All add-mode rules for a property, in source order (system file before run spec).</summary>
        public IEnumerable<OverrideRule> FindAdditive(string kind, string id, string type, string property) =>
            _rules.Where(r => r.Additive && string.Equals(r.Property, property, StringComparison.OrdinalIgnoreCase) && r.Matches(kind, id, type))
                  .OrderBy(r => r.Source);

        /// <summary>Rules that never matched anything (reported as warnings -- usually a typo in an id).</summary>
        public List<OverrideRule> Unused(Func<OverrideRule, bool> wasUsed) => _rules.Where(r => !wasUsed(r)).ToList();
    }
}
