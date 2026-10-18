using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Models;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Schema
{
    /// <summary>
    /// JSON Schemas (draft-07) for system files and run specs, generated from the option registry so that they match
    /// the engine exactly -- including plugin-provided elements, instruments and keys. Point an editor at them with
    /// <c>"$schema": "../schemas/ufs.schema.json"</c> (VS Code, JetBrains, ...) for completion and checking.
    /// <c>ultrafast schema system|run</c> writes them; the repository ships them in <c>schemas/</c>.
    /// </summary>
    public static class SchemaGenerator
    {
        public const string Draft = "http://json-schema.org/draft-07/schema#";

        /// <summary>Restrict the output to components from these origins ("core", plugin ids); null = all loaded.</summary>
        [ThreadStatic] private static HashSet<string> _origins;
        private static IEnumerable<ComponentSpec> In(string category) => OptionRegistry.InCategory(category).Where(s => _origins == null || _origins.Contains(s.Origin));
        private static IEnumerable<ComponentSpec> CommonOf(string category) => OptionRegistry.Common(category).Where(s => _origins == null || _origins.Contains(s.Origin));

        public static JsonObject SystemSchema(IEnumerable<string> origins = null)
        {
            _origins = origins == null ? null : new HashSet<string>(origins, StringComparer.OrdinalIgnoreCase);
            try { return SystemSchemaCore(); } finally { _origins = null; }
        }

        public static JsonObject RunSchema(IEnumerable<string> origins = null)
        {
            _origins = origins == null ? null : new HashSet<string>(origins, StringComparer.OrdinalIgnoreCase);
            try { return RunSchemaCore(); } finally { _origins = null; }
        }

        private static JsonObject SystemSchemaCore()
        {
            var defs = new JsonObject();
            var root = new JsonObject().Set("$schema", Draft).Set("$id", "https://ultrafastengine.dev/schemas/ufs-" + Migrations.SystemVersion + ".schema.json")
                .Set("title", "UltrafastEngine system file (.ufs)")
                .Set("description", "An optical system as a chain of elements (docs/reference/system-file.md). SI units: the key suffix names the unit (lengthM: metres).");
            var sys = OptionRegistry.Get("system", "");
            var props = Properties(sys, defs);
            props.Set("$schema", new JsonObject().Set("type", "string").Set("description", "Path or URL of this schema (editor support)."));
            props.Set("format", new JsonObject().Set("const", "ultrafast-system").Set("description", "Must be \"ultrafast-system\"."));
            props.Set("version", new JsonObject().Set("type", "integer").Set("minimum", 1).Set("maximum", Migrations.SystemVersion).Set("description", "Format version (older versions are migrated on load)."));
            props.Set("elements", new JsonObject().Set("type", "array").Set("description", "The beam path, in order.").Set("items", new JsonObject().Set("$ref", "#/definitions/element")));
            root.Set("type", "object").Set("required", JsonValue.FromArray(new[] { "format", "elements" })).Set("properties", props);
            defs.Set("element", ElementDefinition(defs));
            root.Set("definitions", defs);
            return root;
        }

        private static JsonObject RunSchemaCore()
        {
            var defs = new JsonObject();
            var root = new JsonObject().Set("$schema", Draft).Set("$id", "https://ultrafastengine.dev/schemas/run-" + Migrations.RunVersion + ".schema.json")
                .Set("title", "UltrafastEngine run spec (run.json)")
                .Set("description", "The pulse, instruments and numerical settings of a run (docs/reference/run-spec.md). SI units.");
            var run = OptionRegistry.Get("run", "");
            var props = Properties(run, defs);
            foreach (var extra in In("runTop")) foreach (var p in extra.AllParams()) props.Set(p.Name, Param(p, defs));
            props.Set("$schema", new JsonObject().Set("type", "string").Set("description", "Path or URL of this schema (editor support)."));
            props.Set("format", new JsonObject().Set("const", "ultrafast-run").Set("description", "Must be \"ultrafast-run\"."));
            props.Set("version", new JsonObject().Set("type", "integer").Set("minimum", 1).Set("maximum", Migrations.RunVersion).Set("description", "Format version (older versions are migrated on load)."));
            props.Set("instruments", new JsonObject().Set("type", "array").Set("description", "Instruments: what you measure, and where (docs/reference/instruments.md).").Set("items", new JsonObject().Set("$ref", "#/definitions/instrument")));
            root.Set("type", "object").Set("required", JsonValue.FromArray(new[] { "format", "pulse" })).Set("properties", props);
            defs.Set("instrument", InstrumentDefinition(defs));
            root.Set("definitions", defs);
            return root;
        }

        // ------------------------------------------------------------------ pieces

        private static JsonObject ElementDefinition(JsonObject defs)
        {
            var specs = In("element").ToList();
            var types = specs.SelectMany(s => new[] { s.Name }.Concat(s.Aliases)).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            var el = new JsonObject().Set("type", "object").Set("required", JsonValue.FromArray(new[] { "id", "type" }))
                .Set("properties", new JsonObject()
                    .Set("id", new JsonObject().Set("type", "string").Set("description", "Unique id (instruments, overrides and probes refer to it)."))
                    .Set("type", new JsonObject().Set("type", "string").Set("enum", JsonValue.FromArray(types)).Set("description", "Element type.")));
            var all = new JsonArray();
            foreach (var s in specs)
            {
                string defName = "element." + s.Name;
                defs.Set(defName, Component(s, defs, new[] { "elementCommon" }, "type", s.Name));
                var names = new[] { s.Name }.Concat(s.Aliases).ToArray();
                all.Add(new JsonObject()
                    .Set("if", new JsonObject().Set("properties", new JsonObject().Set("type", new JsonObject().Set("enum", JsonValue.FromArray(names)))).Set("required", JsonValue.FromArray(new[] { "type" })))
                    .Set("then", new JsonObject().Set("$ref", "#/definitions/" + defName)));
            }
            el.Set("allOf", all);
            return el;
        }

        private static JsonObject InstrumentDefinition(JsonObject defs)
        {
            var specs = In("instrument").ToList();
            var kinds = specs.SelectMany(s => new[] { s.Name }.Concat(s.Aliases)).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            var ins = new JsonObject().Set("type", "object").Set("required", JsonValue.FromArray(new[] { "kind" }))
                .Set("properties", new JsonObject().Set("kind", new JsonObject().Set("type", "string").Set("enum", JsonValue.FromArray(kinds)).Set("description", "Instrument kind.")));
            var all = new JsonArray();
            foreach (var s in specs)
            {
                string defName = "instrument." + s.Name;
                defs.Set(defName, Component(s, defs, new[] { "instrumentCommon" }, "kind", s.Name));
                var names = new[] { s.Name }.Concat(s.Aliases).ToArray();
                all.Add(new JsonObject()
                    .Set("if", new JsonObject().Set("properties", new JsonObject().Set("kind", new JsonObject().Set("enum", JsonValue.FromArray(names)))).Set("required", JsonValue.FromArray(new[] { "kind" })))
                    .Set("then", new JsonObject().Set("$ref", "#/definitions/" + defName)));
            }
            ins.Set("allOf", all);
            return ins;
        }

        /// <summary>A component as an object schema: its parameters + the common groups; unknown keys rejected
        /// (editors flag typos) unless the component is open.</summary>
        private static JsonObject Component(ComponentSpec s, JsonObject defs, IEnumerable<string> commonCategories, string discriminator, string name)
        {
            var props = Properties(s, defs);
            foreach (var cat in commonCategories)
                foreach (var c in CommonOf(cat))
                    foreach (var p in c.AllParams()) if (!props.Has(p.Name)) props.Set(p.Name, Param(p, defs));
            props.Set(discriminator, new JsonObject().Set("type", "string"));
            foreach (var u in new[] { "comment", "description", "notes" }) if (!props.Has(u)) props.Set(u, new JsonObject().Set("description", "Free text."));
            var o = new JsonObject().Set("type", "object").Set("description", Describe(s)).Set("properties", props);
            var req = s.AllParams().Where(p => p.Required).Select(p => p.Name).ToList();
            if (req.Count > 0) o.Set("required", JsonValue.FromArray(req));
            if (!s.AllowAdditional) o.Set("patternProperties", new JsonObject().Set("^_", new JsonObject())).Set("additionalProperties", false);
            return o;
        }

        private static string Describe(ComponentSpec s) => (s.Summary + (string.IsNullOrEmpty(s.Doc) ? "" : " (docs/" + s.Doc + ")") + (s.Origin != "core" ? " [plugin: " + s.Origin + "]" : "")).Trim();

        private static JsonObject Properties(ComponentSpec s, JsonObject defs)
        {
            var props = new JsonObject();
            if (s == null) return props;
            foreach (var p in s.AllParams())
            {
                props.Set(p.Name, Param(p, defs));
                foreach (var a in p.Aliases) props.Set(a, new JsonObject().Set("$ref", "#/definitions/_any").Set("description", "Alias of " + p.Name + "."));
            }
            if (!defs.Has("_any")) defs.Set("_any", new JsonObject());
            return props;
        }

        private static JsonObject Param(ParamSpec p, JsonObject defs)
        {
            var o = new JsonObject();
            string desc = p.Description + (p.Unit != "" ? " [" + p.Unit + "]" : "") + (p.Default != null ? " Default: " + p.Default + "." : "") + (p.DeprecatedBy != null ? " DEPRECATED: use " + p.DeprecatedBy + "." : "");
            switch (p.Type)
            {
                case ParamType.Number: o.Set("type", "number"); break;
                case ParamType.Integer: o.Set("type", "integer"); break;
                case ParamType.Boolean: o.Set("type", "boolean"); break;
                case ParamType.String:
                    if (p.AllowBool && p.Enum != null)
                        o.Set("anyOf", new JsonArray(new JsonValue[] { new JsonObject().Set("type", "string").Set("enum", JsonValue.FromArray(p.Enum)), new JsonObject().Set("type", "boolean") }));
                    else
                    {
                        o.Set("type", "string");
                        if (p.Enum != null) o.Set("enum", JsonValue.FromArray(p.Enum));
                    }
                    break;
                case ParamType.NumberOrArray:
                    o.Set("anyOf", new JsonArray(new JsonValue[] { new JsonObject().Set("type", "number"), new JsonObject().Set("type", "array").Set("items", new JsonObject().Set("type", "number")) }));
                    break;
                case ParamType.Object:
                    if (p.ObjectRef != null && !p.FreeForm)
                    {
                        var sub = Validator.Resolve(p.ObjectRef);
                        if (sub != null)
                        {
                            string dn = "obj." + p.ObjectRef;
                            if (!defs.Has(dn)) { defs.Set(dn, new JsonObject()); defs.Set(dn, Component(sub, defs, new string[0], "_", "")); ((JsonObject)defs[dn]).AsObject().GetObject("properties")?.Remove("_"); }
                            o.Set("anyOf", new JsonArray(new JsonValue[] { new JsonObject().Set("$ref", "#/definitions/" + dn), new JsonObject().Set("type", "string") }));
                            break;
                        }
                    }
                    o.Set("type", "object");
                    break;
                case ParamType.Array:
                    o.Set("type", "array");
                    if (p.ObjectRef != null)
                    {
                        var sub = Validator.Resolve(p.ObjectRef);
                        if (sub != null)
                        {
                            string dn = "obj." + p.ObjectRef;
                            if (!defs.Has(dn)) { defs.Set(dn, new JsonObject()); defs.Set(dn, Component(sub, defs, new string[0], "_", "")); ((JsonObject)defs[dn]).AsObject().GetObject("properties")?.Remove("_"); }
                            o.Set("items", new JsonObject().Set("$ref", "#/definitions/" + dn));
                        }
                    }
                    break;
                case ParamType.Model:
                    var kinds = p.ModelCategory != null ? ModelKinds(p.ModelCategory) : new List<string>();
                    var mobj = new JsonObject().Set("type", "object").Set("properties", new JsonObject().Set("kind", kinds.Count > 0 ? new JsonObject().Set("type", "string").Set("enum", JsonValue.FromArray(kinds)) : new JsonObject().Set("type", "string")));
                    o.Set("anyOf", new JsonArray(new JsonValue[] { new JsonObject().Set("type", "number"), new JsonObject().Set("type", "string"), mobj, new JsonObject().Set("type", "array") }));
                    desc += " A model: a number, a name / \"@callback\" / expression string, or an object with \"kind\"" + (kinds.Count > 0 ? " (" + string.Join(", ", kinds) + ")" : "") + ".";
                    break;
            }
            o.Set("description", desc.Trim());
            if (p.DeprecatedBy != null) o.Set("deprecated", true);
            return o;
        }

        private static List<string> ModelKinds(string category)
        {
            var fromSpecs = OptionRegistry.InCategory("model:" + category).Select(s => s.Name).ToList();
            try { fromSpecs.AddRange(ModelRegistry.CreateDefault().KindsFor(category)); } catch { }
            return fromSpecs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
