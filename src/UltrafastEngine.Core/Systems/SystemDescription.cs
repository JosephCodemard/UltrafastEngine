using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Systems
{
    /// <summary>One element of a system file: an id, a type and its JSON body (kept as JSON so new element
    /// types and fields can be added without breaking old readers).</summary>
    public sealed class SystemElement
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Label { get; set; }
        public JsonObject Json { get; set; }
        /// <summary>Source surfaces (e.g. Zemax LDE rows) this element was built from, if any.</summary>
        public int[] Surfaces { get; set; }

        public string Display => string.IsNullOrEmpty(Label) || Label == Id ? Id : Id + " (" + Label + ")";
    }

    /// <summary>
    /// The system file (<c>.ufs</c>, JSON): everything the engine needs to know about an optical system,
    /// independent of the tool that produced it. Produced by the Zemax binding (sampled from OpticStudio by
    /// ray tracing), written by hand, or generated from Python. Format: docs/reference/system-file.md.
    /// <code>
    /// { "format": "ultrafast-system", "version": 1, "name": "...",
    ///   "source": {...}, "sampling": {"wavelengthsM": [...]},
    ///   "environment": {"indexReference": "air", "air": {"temperatureC": 20, "pressurePa": 101325}},
    ///   "materials": {...}, "coatings": {...}, "inputBeam": {...},
    ///   "elements": [ {"id": "...", "type": "...", ...}, ... ],
    ///   "overrides": [...] }
    /// </code>
    /// </summary>
    public sealed class SystemDescription
    {
        public const string FormatName = "ultrafast-system";
        public const int CurrentVersion = Schema.Migrations.SystemVersion;
        /// <summary>Changes made while upgrading an older file (reported in the run notes).</summary>
        public List<string> MigrationNotes { get; } = new List<string>();

        public string Name { get; set; } = "system";
        public string Description { get; set; }
        public int Version { get; set; } = CurrentVersion;
        public JsonObject Source { get; set; } = new JsonObject();
        public double[] WavelengthsM { get; set; }
        public JsonObject Environment { get; set; } = new JsonObject();
        public JsonObject Materials { get; set; } = new JsonObject();
        public JsonObject Coatings { get; set; } = new JsonObject();
        /// <summary>Mirror substrates defined in the file, by name (material, thicknessM, back face, wedge): used by the mirror's "substrate".</summary>
        public JsonObject Substrates { get; set; } = new JsonObject();
        public JsonObject InputBeam { get; set; }
        /// <summary>"sequential" (the default) or "branched": see <see cref="SystemGraph"/>.</summary>
        public string Topology { get; set; }
        public List<SystemElement> Elements { get; } = new List<SystemElement>();
        public List<OverrideRule> Overrides { get; } = new List<OverrideRule>();
        /// <summary>Unknown top-level keys are preserved on round trip.</summary>
        public JsonObject Extra { get; } = new JsonObject();
        public string FilePath { get; set; }
        public string BaseDirectory { get; set; }

        public double[] SamplingOmega => WavelengthsM?.Select(l => 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / l).ToArray();

        public static SystemDescription Load(string path)
        {
            var json = JsonValue.ParseFile(path).AsObject();
            var s = FromJson(json, Path.GetDirectoryName(Path.GetFullPath(path)));
            s.FilePath = Path.GetFullPath(path);
            return s;
        }

        public static SystemDescription Parse(string jsonText, string baseDirectory = null) => FromJson(JsonValue.Parse(jsonText).AsObject(), baseDirectory);

        public static SystemDescription FromJson(JsonObject o, string baseDirectory = null)
        {
            string fmt = o.GetString("format", FormatName);
            if (fmt != FormatName) throw new JsonException("Not a system file: format is \"" + fmt + "\" (expected \"" + FormatName + "\").");
            var migrationNotes = new List<string>();
            Schema.Migrations.MigrateSystem(o, migrationNotes);
            var s = new SystemDescription
            {
                Name = o.GetString("name", "system"),
                Description = o.GetString("description"),
                Version = o.GetInt("version", CurrentVersion),
                Source = o.GetObject("source") ?? new JsonObject(),
                Environment = o.GetObject("environment") ?? new JsonObject(),
                Materials = o.GetObject("materials") ?? new JsonObject(),
                Coatings = o.GetObject("coatings") ?? new JsonObject(),
                Substrates = o.GetObject("substrates") ?? new JsonObject(),
                InputBeam = o.GetObject("inputBeam"),
                Topology = o.GetString("topology"),
                BaseDirectory = baseDirectory
            };
            s.MigrationNotes.AddRange(migrationNotes);
            if (s.Version > CurrentVersion) throw new JsonException("System file version " + s.Version + " is newer than this engine supports (" + CurrentVersion + ").");
            var sampling = o.GetObject("sampling");
            if (sampling != null)
                s.WavelengthsM = sampling.GetDoubleArray("wavelengthsM") ?? sampling.GetDoubleArray("wavelengthsNm")?.Select(x => x * 1e-9).ToArray();
            var els = o.GetArray("elements") ?? throw new JsonException("System file has no \"elements\" array.");
            int k = 0;
            foreach (var ev in els)
            {
                var eo = ev.AsObject();
                var e = new SystemElement
                {
                    Type = eo.GetRequiredString("type", "elements[" + k + "]"),
                    Id = eo.GetString("id") ?? ("e" + k),
                    Label = eo.GetString("label"),
                    Json = eo,
                    Surfaces = eo.Has("surfaces") ? eo["surfaces"].AsDoubleArray().Select(x => (int)x).ToArray() : null
                };
                s.Elements.Add(e);
                k++;
            }
            s.Overrides.AddRange(OverrideSet.Parse(o.GetArray("overrides"), ModelSource.SystemOverride, "system file"));
            var known = new HashSet<string> { "topology", "format", "version", "name", "description", "source", "sampling", "environment", "materials", "coatings", "inputBeam", "elements", "overrides" };
            foreach (var kv in o) if (!known.Contains(kv.Key)) s.Extra.Set(kv.Key, kv.Value);
            return s;
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject()
                .Set("format", FormatName).Set("version", Version).Set("name", Name);
            if (Description != null) o.Set("description", Description);
            if (Topology != null) o.Set("topology", Topology);
            o.Set("source", Source);
            if (WavelengthsM != null) o.Set("sampling", new JsonObject().Set("wavelengthsM", JsonValue.FromArray(WavelengthsM)));
            if (Environment.Count > 0) o.Set("environment", Environment);
            if (Materials.Count > 0) o.Set("materials", Materials);
            if (Coatings.Count > 0) o.Set("coatings", Coatings);
            if (Substrates.Count > 0) o.Set("substrates", Substrates);
            if (InputBeam != null) o.Set("inputBeam", InputBeam);
            var arr = new JsonArray();
            foreach (var e in Elements)
            {
                e.Json.Set("id", e.Id);
                e.Json.Set("type", e.Type);
                if (e.Label != null) e.Json.Set("label", e.Label);
                arr.Add(e.Json);
            }
            o.Set("elements", arr);
            if (Overrides.Count > 0)
            {
                var ov = new JsonArray();
                foreach (var r in Overrides)
                    ov.Add(new JsonObject().Set("target", r.Target).Set("property", r.Property).Set(r.Model != null ? "model" : "value", r.RawValue).Set("mode", r.Additive ? "add" : "replace"));
                o.Set("overrides", ov);
            }
            foreach (var kv in Extra) o.Set(kv.Key, kv.Value);
            return o;
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson().ToJson(true));
        }

        public SystemElement Find(string id) => Elements.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>A per-wavelength array (or scalar, broadcast) from an element, against the element's own
        /// "wavelengthsM" or the system sampling. Returns (omega, values) or null if the key is absent.</summary>
        public (double[] Omega, double[] Values)? PerWavelength(SystemElement e, string key)
        {
            if (!e.Json.TryGet(key, out var v)) return null;
            var wl = e.Json.GetDoubleArray("wavelengthsM") ?? WavelengthsM;
            double c = PhysicalConstants.SpeedOfLightMPerS;
            if (v.Kind == JsonKind.Number)
            {
                double x = v.AsDouble();
                if (wl == null) return (new[] { 1.0 }, new[] { x });
                return (wl.Select(l => 2 * Math.PI * c / l).ToArray(), wl.Select(_ => x).ToArray());
            }
            var vals = v.AsDoubleArray();
            if (wl == null) throw new JsonException("Element '" + e.Id + "': \"" + key + "\" is an array but neither the element nor the system defines sampling wavelengths.");
            if (vals.Length != wl.Length) throw new JsonException("Element '" + e.Id + "': \"" + key + "\" has " + vals.Length + " values but there are " + wl.Length + " sampling wavelengths.");
            return (wl.Select(l => 2 * Math.PI * c / l).ToArray(), vals);
        }
    }
}
