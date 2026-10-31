using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Schema
{
    /// <summary>JSON value type of a parameter.</summary>
    public enum ParamType
    {
        Number, Integer, String, Boolean, Object, Array,
        /// <summary>A number or an array of numbers (per-wavelength data, ranges).</summary>
        NumberOrArray,
        /// <summary>A model: a number, a string ("@callback", a library name) or an object with "kind" (docs/reference/models.md).</summary>
        Model,
        /// <summary>Anything (free-form data passed through, e.g. a user element's "params").</summary>
        Any
    }

    /// <summary>One parameter of an element, instrument, model or run-spec section.</summary>
    public sealed class ParamSpec
    {
        public string Name;
        public ParamType Type;
        /// <summary>SI unit of the value ("m", "s", "J", "rad", "s^2", "W/m^2"), or "" for dimensionless.</summary>
        public string Unit = "";
        public string Description = "";
        /// <summary>Default value as shown in the docs ("0", "auto", "the carrier").</summary>
        public string Default;
        public bool Required;
        /// <summary>Allowed string values.</summary>
        public string[] Enum;
        /// <summary>For enum parameters: true / false are accepted too (legacy boolean form; documented in the description).</summary>
        public bool AllowBool;
        /// <summary>For Number / NumberOrArray parameters: an object is accepted too -- a fit parameter
        /// <c>{"value": ..., "fit": true, "min": ..., "max": ..., "prior": ...}</c> (fitting plugin); its spec is
        /// <see cref="ObjectRef"/> when registered.</summary>
        public bool Fittable;
        /// <summary>For Model parameters: the model category ("index", "beam", "filter", ...).</summary>
        public string ModelCategory;
        /// <summary>For Object parameters: the spec of the nested object (category/name), if it is a registered component.</summary>
        public string ObjectRef;
        /// <summary>Free-form value: its contents are not checked for unknown keys.</summary>
        public bool FreeForm;
        /// <summary>Set when the parameter is deprecated: the replacement (shown in docs and warnings).</summary>
        public string DeprecatedBy;
        public string[] Aliases = new string[0];

        public ParamSpec Clone() => (ParamSpec)MemberwiseClone();
    }

    /// <summary>
    /// The declared options of one component: an element type, an instrument kind, a model kind, or a section of the
    /// run spec / system file. Single source of truth for the JSON Schemas, the reference tables, the Python type
    /// hints and the unknown-key checks (docs/reference/schema.md).
    /// </summary>
    public sealed class ComponentSpec
    {
        /// <summary>"element", "instrument", "model:&lt;category&gt;", "run", "pulse", "grid", "physics", "stepper",
        /// "outputs", "system", "sampling", "imaging", "sensor", ...</summary>
        public string Category;
        /// <summary>Type / kind name ("bulk", "spectrometer", "sellmeier"); "" for sections.</summary>
        public string Name;
        public string[] Aliases = new string[0];
        public string Summary = "";
        /// <summary>Longer explanation (Markdown).</summary>
        public string Description = "";
        /// <summary>"core" or the plugin id that provides the component.</summary>
        public string Origin = "core";
        /// <summary>Documentation page (relative to docs/), e.g. "reference/elements.md#bulk".</summary>
        public string Doc;
        public List<ParamSpec> Params = new List<ParamSpec>();
        /// <summary>Common parameter groups this component also accepts ("elementCommon", "instrumentCommon", "beamSize" ...).</summary>
        public List<string> Includes = new List<string>();
        /// <summary>Unknown keys are allowed (the component reads free-form data).</summary>
        public bool AllowAdditional;
        /// <summary>Beam ports of an element type, in order; the first of each is the default. Null means in and out.</summary>
        public string[] InputPorts, OutputPorts;

        public ComponentSpec(string category, string name, string summary = "") { Category = category; Name = name ?? ""; Summary = summary ?? ""; }

        public ComponentSpec WithOrigin(string origin) { Origin = origin; return this; }
        public ComponentSpec Alias(params string[] names) { Aliases = Aliases.Concat(names).ToArray(); return this; }
        public ComponentSpec Include(params string[] groups) { Includes.AddRange(groups); return this; }
        public ComponentSpec Describe(string markdown) { Description = markdown; return this; }
        public ComponentSpec DocAt(string page) { Doc = page; return this; }
        public ComponentSpec Open() { AllowAdditional = true; return this; }
        public ComponentSpec Ports(string[] inputs, string[] outputs) { InputPorts = inputs; OutputPorts = outputs; return this; }

        public ComponentSpec P(string name, ParamType type, string unit, string description, string def = null, bool required = false, string[] enumValues = null)
        {
            Params.Add(new ParamSpec { Name = name, Type = type, Unit = unit ?? "", Description = description ?? "", Default = def, Required = required, Enum = enumValues });
            return this;
        }
        public ComponentSpec Num(string name, string unit, string description, string def = null, bool required = false) => P(name, ParamType.Number, unit, description, def, required);
        public ComponentSpec Int(string name, string description, string def = null, bool required = false) => P(name, ParamType.Integer, "", description, def, required);
        public ComponentSpec Str(string name, string description, string def = null, bool required = false) => P(name, ParamType.String, "", description, def, required);
        public ComponentSpec Bool(string name, string description, string def = null) => P(name, ParamType.Boolean, "", description, def);
        public ComponentSpec Enum(string name, string[] values, string description, string def = null, bool required = false) => P(name, ParamType.String, "", description, def, required, values);
        public ComponentSpec Arr(string name, string unit, string description, string def = null, bool required = false) => P(name, ParamType.NumberOrArray, unit, description, def, required);
        public ComponentSpec Obj(string name, string description, string objectRef = null, string def = null, bool required = false)
        {
            Params.Add(new ParamSpec { Name = name, Type = ParamType.Object, Description = description, ObjectRef = objectRef, Default = def, Required = required });
            return this;
        }
        public ComponentSpec Free(string name, ParamType type, string description)
        {
            Params.Add(new ParamSpec { Name = name, Type = type, Description = description, FreeForm = true });
            return this;
        }
        public ComponentSpec ListOf(string name, string objectRef, string description, bool required = false)
        {
            Params.Add(new ParamSpec { Name = name, Type = ParamType.Array, ObjectRef = objectRef, Description = description, Required = required });
            return this;
        }
        public ComponentSpec Model(string name, string category, string description, string def = null, bool required = false)
        {
            Params.Add(new ParamSpec { Name = name, Type = ParamType.Model, ModelCategory = category, Description = description, Default = def, Required = required });
            return this;
        }
        /// <summary>The last parameter (an enum) also accepts true / false.</summary>
        public ComponentSpec OrBool() { Params[Params.Count - 1].AllowBool = true; return this; }
        /// <summary>The last parameter may also be a fit-parameter object (<paramref name="objectRef"/>, default "fitting:value").</summary>
        public ComponentSpec Fittable(string objectRef = "fitting:value") { var p = Params[Params.Count - 1]; p.Fittable = true; p.ObjectRef = objectRef; return this; }
        /// <summary>Mark the last added parameter as deprecated in favour of <paramref name="replacement"/>.</summary>
        public ComponentSpec Deprecated(string replacement) { Params[Params.Count - 1].DeprecatedBy = replacement; return this; }
        /// <summary>Alternative spellings of the last added parameter.</summary>
        public ComponentSpec Also(params string[] aliases) { var p = Params[Params.Count - 1]; p.Aliases = p.Aliases.Concat(aliases).ToArray(); return this; }

        /// <summary>All parameters including the included groups.</summary>
        public IEnumerable<ParamSpec> AllParams()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Params) if (seen.Add(p.Name)) yield return p;
            foreach (var g in Includes)
            {
                var gs = OptionRegistry.Get("group", g);
                if (gs == null) continue;
                foreach (var p in gs.AllParams()) if (seen.Add(p.Name)) yield return p;
            }
        }

        public ParamSpec Find(string key) => AllParams().FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.Ordinal) || p.Aliases.Contains(key));
    }

    /// <summary>All declared components (built in and from plugins). Thread-safe.</summary>
    public static class OptionRegistry
    {
        private static readonly object Lock = new object();
        private static readonly List<ComponentSpec> _all = new List<ComponentSpec>();
        private static bool _builtIns;

        private static void EnsureBuiltIns()
        {
            if (_builtIns) return;
            lock (Lock)
            {
                if (_builtIns) return;
                _builtIns = true;
                BuiltInOptions.RegisterAll(s => _all.Add(s));
            }
        }

        /// <summary>Add or replace a component (a later spec with the same category and name replaces the earlier one).</summary>
        public static void Add(ComponentSpec spec)
        {
            EnsureBuiltIns();
            lock (Lock)
            {
                _all.RemoveAll(x => x.Category == spec.Category && string.Equals(x.Name, spec.Name, StringComparison.OrdinalIgnoreCase));
                _all.Add(spec);
            }
        }

        public static ComponentSpec Get(string category, string name)
        {
            EnsureBuiltIns();
            lock (Lock)
                return _all.FirstOrDefault(x => x.Category == category && (string.Equals(x.Name, name ?? "", StringComparison.OrdinalIgnoreCase) || x.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase))));
        }

        public static IReadOnlyList<ComponentSpec> All { get { EnsureBuiltIns(); Plugins.PluginManager.EnsureLoaded(); lock (Lock) return _all.ToList(); } }

        public static IEnumerable<ComponentSpec> InCategory(string category) => All.Where(x => x.Category == category).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase);

        /// <summary>Specs that contribute common keys: every spec in <paramref name="category"/> (plugins add theirs
        /// there) plus the parameter group of that name.</summary>
        public static IEnumerable<ComponentSpec> Common(string category)
        {
            foreach (var s in InCategory(category)) yield return s;
            var g = Get("group", category);
            if (g != null) yield return g;
        }

        // ------------------------------------------------------------------ suggestions

        /// <summary>Closest known name (Damerau-free Levenshtein, case-insensitive), or null if nothing is close.</summary>
        public static string Suggest(string key, IEnumerable<string> known)
        {
            string best = null; int bd = int.MaxValue;
            foreach (var k in known)
            {
                int d = Distance(key.ToLowerInvariant(), k.ToLowerInvariant());
                if (d < bd) { bd = d; best = k; }
            }
            return best != null && bd <= Math.Max(2, key.Length / 3) ? best : null;
        }

        private static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
