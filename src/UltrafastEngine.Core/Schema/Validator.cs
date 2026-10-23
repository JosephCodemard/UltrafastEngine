using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Schema
{
    /// <summary>
    /// Checks system files and run specs against the option registry: unknown keys (with a "did you mean"
    /// suggestion), wrong JSON types and deprecated keys. Problems are warnings -- the engine ignores keys it does
    /// not know -- so a typo such as "lenghtM" no longer goes unnoticed. Components without a declared spec (e.g. a
    /// plugin that declares none) are not checked.
    /// </summary>
    public static class Validator
    {
        /// <summary>Keys accepted everywhere (comments and editor metadata).</summary>
        private static readonly HashSet<string> Universal = new HashSet<string>(StringComparer.Ordinal) { "$schema", "comment", "_comment", "notes", "description" };

        public static List<string> CheckSystem(SystemDescription sys)
        {
            var w = new List<string>();
            if (sys == null) return w;
            foreach (var e in sys.Elements)
            {
                var spec = OptionRegistry.Get("element", e.Type);
                if (spec == null || e.Json == null) continue;
                CheckObject(e.Json, spec, "Element '" + e.Id + "' (" + e.Type + ")", w, new[] { "elementCommon" });
            }
            if (sys.Materials != null)
                foreach (var kv in sys.Materials)
                    if (kv.Value.Kind == JsonKind.Object) CheckObject(kv.Value.AsObject(), OptionRegistry.Get("material", ""), "Material '" + kv.Key + "'", w);
            return w.Distinct().ToList();
        }

        public static List<string> CheckRun(RunSpec run)
        {
            var w = new List<string>();
            var o = run?.Raw;
            if (o == null) return w;
            var top = OptionRegistry.Get("run", "");
            if (top != null) CheckObject(o, top, "Run spec", w, new[] { "runTop" });
            var instruments = o.GetArray("instruments");
            if (instruments != null)
                foreach (var iv in instruments)
                {
                    if (iv.Kind != JsonKind.Object) continue;
                    var i = iv.AsObject();
                    string kind = i.GetString("kind", "");
                    var spec = OptionRegistry.Get("instrument", kind);
                    if (spec == null) continue;
                    CheckObject(i, spec, "Instrument '" + (i.GetString("name") ?? kind) + "' (" + kind + ")", w, new[] { "instrumentCommon" });
                }
            return w.Distinct().ToList();
        }

        /// <summary>Check one object against a spec (plus every spec in the given extra categories, e.g. the common
        /// instrument keys contributed by plugins). Recurses into declared nested objects and models.</summary>
        public static void CheckObject(JsonObject o, ComponentSpec spec, string where, List<string> warnings, IEnumerable<string> extraCategories = null)
        {
            if (o == null || spec == null) return;
            var known = new Dictionary<string, ParamSpec>(StringComparer.Ordinal);
            void AddAll(ComponentSpec s) { foreach (var p in s.AllParams()) { if (!known.ContainsKey(p.Name)) known[p.Name] = p; foreach (var a in p.Aliases) if (!known.ContainsKey(a)) known[a] = p; } }
            AddAll(spec);
            if (extraCategories != null)
                foreach (var cat in extraCategories)
                    foreach (var s in OptionRegistry.Common(cat)) AddAll(s);
            foreach (var kv in o)
            {
                if (Universal.Contains(kv.Key) || kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                if (!known.TryGetValue(kv.Key, out var p))
                {
                    if (spec.AllowAdditional) continue;
                    var sug = OptionRegistry.Suggest(kv.Key, known.Keys);
                    warnings.Add(where + ": unknown parameter \"" + kv.Key + "\"" + (sug != null ? " (did you mean \"" + sug + "\"?)" : "") + " -- it is ignored.");
                    continue;
                }
                if (p.DeprecatedBy != null) warnings.Add(where + ": \"" + kv.Key + "\" is deprecated; use " + p.DeprecatedBy + ".");
                if (kv.Value == null || kv.Value.IsNull) continue;
                string bad = TypeProblem(p, kv.Value);
                if (bad != null) { warnings.Add(where + ": \"" + kv.Key + "\" should be " + bad + "."); continue; }
                if (p.FreeForm) continue;
                if (p.Fittable && kv.Value.Kind == JsonKind.Object)
                {
                    var fs = p.ObjectRef != null ? Resolve(p.ObjectRef) : null;
                    if (fs != null) CheckObject(kv.Value.AsObject(), fs, where + "." + kv.Key, warnings);
                    continue;
                }
                if (p.Type == ParamType.Object && p.ObjectRef != null && kv.Value.Kind == JsonKind.Object)
                {
                    var sub = Resolve(p.ObjectRef);
                    if (sub != null) CheckObject(kv.Value.AsObject(), sub, where + "." + kv.Key, warnings);
                }
                else if (p.Type == ParamType.Array && p.ObjectRef != null && kv.Value.Kind == JsonKind.Array)
                {
                    var sub = Resolve(p.ObjectRef);
                    int k = 0;
                    if (sub != null) foreach (var item in kv.Value.AsArray()) { if (item.Kind == JsonKind.Object) CheckObject(item.AsObject(), sub, where + "." + kv.Key + "[" + k + "]", warnings); k++; }
                }
                else if (p.Type == ParamType.Model && kv.Value.Kind == JsonKind.Object && p.ModelCategory != null)
                {
                    var mo = kv.Value.AsObject();
                    var ms = OptionRegistry.Get("model:" + p.ModelCategory, mo.GetString("kind", ""));
                    if (ms != null) CheckObject(mo, ms, where + "." + kv.Key + " (" + p.ModelCategory + " model \"" + mo.GetString("kind", "") + "\")", warnings, new[] { "modelCommon" });
                }
            }
            foreach (var p in spec.AllParams().Where(x => x.Required))
                if (!o.Has(p.Name) && !p.Aliases.Any(o.Has)) warnings.Add(where + ": missing required parameter \"" + p.Name + "\".");
        }

        /// <summary>"category:name" or "category" (sections) -> spec.</summary>
        public static ComponentSpec Resolve(string reference)
        {
            int i = reference.LastIndexOf(':');
            // "fitting:measured" style refs are categories with an empty name; "model:index:sellmeier" is not used
            var direct = OptionRegistry.Get(reference, "");
            if (direct != null) return direct;
            return i > 0 ? OptionRegistry.Get(reference.Substring(0, i), reference.Substring(i + 1)) : null;
        }

        private static string TypeProblem(ParamSpec p, JsonValue v)
        {
            if (p.Fittable && v.Kind == JsonKind.Object) return v.AsObject().Has("value") ? null : "a number, or a fit object with \"value\"";
            switch (p.Type)
            {
                case ParamType.Number: return v.Kind == JsonKind.Number ? null : "a number" + (p.Unit != "" ? " (" + p.Unit + ")" : "");
                case ParamType.Integer: return v.Kind == JsonKind.Number && Math.Abs(v.AsDouble() - Math.Round(v.AsDouble())) < 1e-9 ? null : "an integer";
                case ParamType.Boolean: return v.Kind == JsonKind.Bool ? null : "true or false";
                case ParamType.String:
                    if (p.AllowBool && v.Kind == JsonKind.Bool) return null;
                    if (v.Kind != JsonKind.String) return p.Enum != null ? "one of " + string.Join(", ", p.Enum.Select(x => "\"" + x + "\"")) + (p.AllowBool ? " (or true / false)" : "") : "a string";
                    if (p.Enum != null && !p.Enum.Contains(v.AsString(), StringComparer.OrdinalIgnoreCase)) return "one of " + string.Join(", ", p.Enum.Select(x => "\"" + x + "\""));
                    return null;
                case ParamType.Object: return v.Kind == JsonKind.Object || (p.ObjectRef != null && v.Kind == JsonKind.String) ? null : "an object";
                case ParamType.Array: return v.Kind == JsonKind.Array ? null : "an array";
                case ParamType.NumberOrArray: return v.Kind == JsonKind.Number || v.Kind == JsonKind.Array ? null : "a number or an array of numbers";
                default: return null;
            }
        }
    }
}
