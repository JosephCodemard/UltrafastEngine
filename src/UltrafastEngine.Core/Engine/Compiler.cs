using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Specs;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Engine
{
    /// <summary>A nonlinear stretch of an element: medium, beam, and the element's linear operator over its full length.</summary>
    public sealed class NonlinearSegment
    {
        public NonlinearMedium Medium { get; set; }
        public IBeamModel Beam { get; set; }
        public double LengthM { get; set; }
        public Material Material { get; set; }
        public List<LinearTerm> Background { get; } = new List<LinearTerm>();
        public bool CarrierResolved { get; set; }
    }

    /// <summary>Surface data used for fluence / damage diagnostics.</summary>
    public sealed class SurfaceInfo
    {
        public double BeamRadiusXM { get; set; } = double.NaN;
        public double BeamRadiusYM { get; set; } = double.NaN;
        public LidtSpec Lidt { get; set; }
        public double AoiRad { get; set; }
        public string Coating { get; set; }
    }

    /// <summary>An element after compilation: its linear terms (before and after any nonlinear segment) and the
    /// nonlinear segment itself.</summary>
    public sealed class CompiledElement
    {
        public SystemElement Source { get; set; }
        public string Id { get; set; }
        public string Type { get; set; }
        public string Label { get; set; }
        public bool Enabled { get; set; } = true;
        public double LengthM { get; set; }
        public List<LinearTerm> Pre { get; } = new List<LinearTerm>();
        public NonlinearSegment Nonlinear { get; set; }
        public List<LinearTerm> Post { get; } = new List<LinearTerm>();
        /// <summary>Optional: called with the pulse entering the element just before it is executed (run-time
        /// adjustments such as a gain scaled to a target output, and reports that need the actual pulse).</summary>
        public Action<Pulses.PulseField, ElementRecord> Prepare { get; set; }
        public SurfaceInfo Surface { get; set; }
        /// <summary>Elements that are more than linear terms + a nonlinear segment (spatial, crystal, user, lens...).</summary>
        public IActiveElement Active { get; set; }
        public bool Probe { get; set; }
        public List<string> Notes { get; } = new List<string>();
        /// <summary>Tracked Gaussian beam at the entrance and exit (null when beam tracking is off).</summary>
        public Optics.BeamState BeamIn { get; set; }
        public Optics.BeamState BeamOut { get; set; }
        /// <summary>Largest delay (s, either sign) of satellite pulses the element creates (ghost replicas): the grid
        /// planner makes room for them.</summary>
        public double SatelliteDelayS { get; set; }
        /// <summary>The satellites the element creates, one entry per order: delay (s) and intensity relative to the main
        /// pulse. The grid planner makes room for every combination of satellites (across all elements) whose product
        /// level is above grid.satelliteLevel. An element that sets only <see cref="SatelliteDelayS"/> counts as one
        /// satellite of level 1 (always given room).</summary>
        public List<(double DelayS, double Level)> Satellites { get; } = new List<(double, double)>();
        /// <summary>Refractive index the beam sees inside the element (for Gaussian beam tracking); NaN = unknown (free space).</summary>
        public double TrackingIndex { get; set; } = double.NaN;
        /// <summary>Thin lenses inside the element for Gaussian beam tracking: position from the entrance (m) and power
        /// 1/f (1/m, vacuum-equivalent, from the optical path difference), e.g. the slices of a thermal lens. The tracker
        /// crosses the element at <see cref="TrackingIndex"/> and applies each lens at its position.</summary>
        public List<(double ZM, double InvFocalM)> TrackLenses { get; set; }

        public IEnumerable<LinearTerm> AllLinear => Pre.Concat(Nonlinear?.Background ?? Enumerable.Empty<LinearTerm>()).Concat(Post);

        public double GroupDelayAt(double w) => AllLinear.Sum(t => { double g = t.GroupDelayAt(w); return double.IsNaN(g) ? 0 : g; });
    }

    /// <summary>Everything needed to execute a run: grid, input pulse, compiled elements, provenance.</summary>
    public sealed class CompiledRun
    {
        public SystemDescription System { get; set; }
        public RunSpec Run { get; set; }
        public ModelRegistry Registry { get; set; }
        public FrequencyGrid Grid { get; set; }
        public PulseField Input { get; set; }
        public List<CompiledElement> Elements { get; } = new List<CompiledElement>();
        public ProvenanceLog Provenance { get; } = new ProvenanceLog();
        public List<string> Notes { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public Dictionary<string, Material> Materials { get; } = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        public GridPlanner.GridPlan GridPlan { get; set; }
        public double RequestedOmega0 { get; set; }
        /// <summary>Tracked Gaussian beam at the system input (null when beam tracking is off).</summary>
        public Optics.BeamState BeamAtInput { get; set; }
    }

    /// <summary>
    /// Turns a system description + run spec into a <see cref="CompiledRun"/>: resolves materials and
    /// overrides (recording provenance), builds every element's linear terms and nonlinear segment, sizes the
    /// grid, and builds the input pulse.
    /// </summary>
    public static partial class Compiler
    {
        public static readonly string[] ElementTypes =
            { "dispersion", "bulk", "gap", "fiber", "interface", "mirror", "grating", "gratingPair", "chirpedMirror", "aperture", "filter", "waveplate", "polarizer", "rotator", "probe", "custom", "lens", "user", "crystal", "gain", "amplifier", "saturableAbsorber", "sesam", "oscillator",
              "plate", "ndFilter", "thinFilmPolarizer", "faradayRotator", "wavefront" };

        private sealed class Ctx
        {
            public SystemDescription Sys; public RunSpec Run; public ModelRegistry Reg; public OverrideSet Ov; public CompiledRun Out;
            public HashSet<OverrideRule> Used = new HashSet<OverrideRule>();
            public BasisTracker Basis = new BasisTracker();
            public Material EnvAir;
            public bool AirReferenced;
            public bool EnvAirInGaps;
            public double Omega0;
            public Optics.BeamState Track;
            /// <summary>Track spatio-temporal couplings with the beam / apply their group-delay dispersion to the pulse.</summary>
            public bool StcTrack, StcPhase;
        }

        public static CompiledRun Compile(SystemDescription sys, RunSpec run, ModelRegistry registry = null)
        {
            Plugins.PluginManager.EnsureLoaded();
            registry = registry ?? ModelRegistry.CreateDefault();
            var c = new Ctx { Sys = sys, Run = run, Reg = registry, Ov = new OverrideSet(), Out = new CompiledRun { System = sys, Run = run, Registry = registry } };
            c.Ov.AddRange(sys.Overrides);
            c.Ov.AddRange(run.Overrides);
            c.Out.Notes.AddRange(sys.MigrationNotes);
            c.Out.Notes.AddRange(run.MigrationNotes);
            // unknown keys (typos, options of another element type) against the option registry
            var optionWarnings = Schema.Validator.CheckSystem(sys).Concat(Schema.Validator.CheckRun(run)).ToList();
            // ULTRAFAST_STRICT_OPTIONS=1 turns them into errors (CI, development)
            if (optionWarnings.Count > 0 && Environment.GetEnvironmentVariable("ULTRAFAST_STRICT_OPTIONS") == "1") throw new JsonException(string.Join("\n", optionWarnings));
            c.Out.Warnings.AddRange(optionWarnings);

            var env = sys.Environment ?? new JsonObject();
            c.AirReferenced = string.Equals(env.GetString("indexReference", sys.Source.GetString("kind") == "zemax" ? "air" : "vacuum"), "air", StringComparison.OrdinalIgnoreCase);
            var airSpec = env.GetObject("air");
            c.EnvAir = new Material
            {
                Name = "Air(environment)",
                Index = airSpec != null ? AirIndexFrom(c, airSpec, "environment.air") : new CiddorAirIndex(),
                IsGas = true
            };
            // opt-in: the environment's air (humidity, temperature, pressure, gas lines) is the "Air" of every gap
            c.EnvAirInGaps = airSpec != null && airSpec.GetBool("applyToGaps", false);
            if (c.EnvAirInGaps) c.Out.Notes.Add("environment.air is applied to every air gap: " + c.EnvAir.Index.Description + (c.EnvAir.Index is IAttenuation ? " (with absorption)" : "") + "; a gap's own \"air\" overrides it.");

            double lambda0 = run.Pulse.ResolveCenterWavelengthM();
            c.Omega0 = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / lambda0;
            c.Out.RequestedOmega0 = c.Omega0;

            // ---- Gaussian beam tracking (q parameters through the system)
            string btMode = (run.Physics.BeamTracking ?? "auto").ToLowerInvariant();
            var ibj = run.InputBeam ?? sys.InputBeam;
            if (btMode != "off" && btMode != "false" && ibj != null)
            {
                c.Track = Optics.BeamState.FromGaussianSpec(ibj, lambda0);
                if (c.Track != null) c.Out.Notes.Add("Gaussian beam tracking from the input beam: " + c.Track.Describe() + " (elements without a beam of their own use the tracked beam).");
            }
            string stMode = run.Physics.SpatioTemporal ?? "auto";
            if (c.Track != null && stMode != "off")
            {
                c.StcTrack = true;
                bool traced = sys.Elements.Any(el => el.Json.Has("groupDelayS"));
                c.StcPhase = stMode == "on" || (stMode == "auto" && !traced);
                if (stMode == "auto" && traced) c.Out.Notes.Add("spatio-temporal couplings are tracked but their group-delay dispersion is not applied: the system has traced group delays (groupDelayS), which already contain it (physics.spatioTemporal = \"on\" applies it).");
            }
            if ((btMode == "on" || btMode == "true") && c.Track == null)
                c.Out.Warnings.Add("physics.beamTracking is on but there is no Gaussian inputBeam to start from (give inputBeam {\"kind\": \"gaussian\", \"w0M\": ..., \"zWaistM\": ...}).");
            c.Out.BeamAtInput = c.Track?.Clone();

            // ---- elements
            for (int i = 0; i < sys.Elements.Count; i++)
            {
                var e = sys.Elements[i];
                CompiledElement ce;
                try { ce = CompileElement(c, e); }
                catch (JsonException ex) { throw new ModelException("Element '" + e.Id + "' (" + e.Type + "): " + ex.Message, ex); }
                catch (ArgumentException ex) { throw new ModelException("Element '" + e.Id + "' (" + e.Type + "): " + ex.Message, ex); }
                c.Out.Elements.Add(ce);
                // nominal carrier after this element (SHG, idler output...): later elements default to it
                if (ce.Active != null && !double.IsNaN(ce.Active.OutputOmega0) && ce.Active.OutputOmega0 > 0) c.Omega0 = ce.Active.OutputOmega0;
            }
            var ids = c.Out.Elements.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (ids.Count > 0) c.Out.Warnings.Add("Duplicate element ids: " + string.Join(", ", ids) + " (overrides and probes address the first match).");

            // ---- probes and instruments
            var instrumentAt = new HashSet<string>(run.Instruments.Select(o => o.GetString("at", "end")), StringComparer.OrdinalIgnoreCase);
            var enabled = c.Out.Elements.Where(x => x.Enabled).ToList();
            for (int i = 0; i < c.Out.Elements.Count; i++)
            {
                var ce = c.Out.Elements[i];
                bool last = enabled.Count > 0 && ReferenceEquals(ce, enabled[enabled.Count - 1]);
                ce.Probe = ce.Enabled && (run.IsProbe(ce.Id, last) || instrumentAt.Contains(ce.Id));
            }
            foreach (var at in instrumentAt)
                if (at != "end" && at != "input" && c.Out.Elements.All(x => !string.Equals(x.Id, at, StringComparison.OrdinalIgnoreCase)))
                    c.Out.Warnings.Add("Instrument location \"" + at + "\" matches no element id.");

            // ---- grid & pulse (at the run pulse's own carrier, not the nominal carrier at the end of the system)
            c.Omega0 = c.Out.RequestedOmega0;
            BuildGridAndPulse(c);

            // ---- field mode per nonlinear segment
            string fm = (run.Physics.FieldMode ?? "auto").ToLowerInvariant();
            foreach (var ce in c.Out.Elements.Where(x => x.Nonlinear != null))
            {
                ce.Nonlinear.CarrierResolved = fm == "carrier" || (fm == "auto" && ce.Nonlinear.Medium.NeedsCarrierResolution);
                if (ce.Nonlinear.CarrierResolved && Math.Abs(c.Out.Input.Ay.Sum(z => z.Magnitude)) > 0)
                    c.Out.Warnings.Add("Element '" + ce.Id + "': carrier-resolved propagation is scalar (x); the y component is propagated linearly only.");
            }

            foreach (var r in c.Ov.Rules)
                if (!c.Used.Contains(r) && !r.Target.StartsWith("material:", StringComparison.OrdinalIgnoreCase))
                    c.Out.Warnings.Add("Override " + r.Target + " / " + r.Property + " (" + r.Origin + ") matched nothing.");
            foreach (var kv in c.Out.Materials)
                foreach (var p in kv.Value.Provenance)
                    c.Out.Provenance.Record("material:" + kv.Key, p.Key, p.Value.Value, p.Value.Source, p.Value.Origin);
            foreach (var r in c.Ov.Rules.Where(r => r.Target.StartsWith("material:", StringComparison.OrdinalIgnoreCase)))
                if (!c.Out.Materials.Keys.Any(k => r.Matches("material", k, null)))
                    c.Out.Warnings.Add("Override " + r.Target + " / " + r.Property + " (" + r.Origin + "): material not used in this system.");
            return c.Out;
        }

        private static CompilerServices Services(Ctx c) => new CompilerServices
        {
            System = c.Sys, Run = c.Run, Registry = c.Reg, Provenance = c.Out.Provenance, Omega0 = c.Omega0, Warnings = c.Out.Warnings,
            MaterialLookup = n => GetMaterial(c, n),
            NumFn = (e, k, f) => Num(c, e, k, f), StrFn = (e, k, f) => Str(c, e, k, f), FindFn = (e, k) => Find(c, e, k),
            PropertiesFn = e => ReadSpatialProperties(c, e),
            TrackedFn = n => TrackedBeam(c, n),
            TrackFn = () => c.Track,
            CompileFn = (e, prefix) =>
            {
                var se = new SystemElement { Type = e.GetRequiredString("type", prefix), Id = prefix + "." + (e.GetString("id") ?? "e"), Label = e.GetString("label"), Json = e };
                CompiledElement ce;
                var saved = c.Track; c.Track = null;   // nested (cavity) elements are not on the main beam path
                try { ce = CompileElement(c, se); }
                catch (JsonException ex) { throw new JsonException("cavity element '" + se.Id + "': " + ex.Message); }
                finally { c.Track = saved; }
                if (ce.Nonlinear != null)
                {
                    string fm = (c.Run.Physics.FieldMode ?? "auto").ToLowerInvariant();
                    ce.Nonlinear.CarrierResolved = fm == "carrier" || (fm == "auto" && ce.Nonlinear.Medium.NeedsCarrierResolution);
                }
                return ce;
            }
        };

        // ================================================================================== materials

        /// <summary>The index model of an air specification: any registered "index" kind (e.g. the hitran plugin's), else Ciddor
        /// from temperatureC, pressurePa, humidity (0..1) and co2ppm.</summary>
        private static IRefractiveIndex AirIndexFrom(Ctx c, JsonObject airSpec, string origin)
        {
            var js = airSpec.Has("kind") ? airSpec : new JsonObject().Set("kind", "ciddor").Set("temperatureC", airSpec.GetDouble("temperatureC", 20)).Set("pressurePa", airSpec.GetDouble("pressurePa", 101325)).Set("humidity", airSpec.GetDouble("humidity", 0)).Set("co2ppm", airSpec.GetDouble("co2ppm", 450));
            return c.Reg.Create<IRefractiveIndex>("index", ModelSpec.FromJson(js, ModelSource.SystemData, origin));
        }

        private static void UseEnvironmentAir(Material m, IRefractiveIndex index)
        {
            m.Index = index;
            if (index is IAttenuation att) m.Attenuation = att;
            m.Provenance["index"] = (index.Description, ModelSource.SystemData, "system environment.air");
        }

        /// <summary>The air of one gap: environment.air (if any) with the gap's own "air" keys on top.</summary>
        private static Material GapAir(Ctx c, SystemElement e)
        {
            var merged = new JsonObject();
            var envAir = (c.Sys.Environment ?? new JsonObject()).GetObject("air");
            if (envAir != null) foreach (var kv in envAir) if (kv.Key != "applyToGaps") merged.Set(kv.Key, kv.Value);
            foreach (var kv in e.Json.GetObject("air")) merged.Set(kv.Key, kv.Value);
            var m = MaterialLibrary.BuiltIn("Air"); m.Name = "Air";
            UseEnvironmentAir(m, AirIndexFrom(c, merged, "element " + e.Id + ".air"));
            m.Provenance["index"] = (m.Index.Description, ModelSource.SystemData, "element " + e.Id + ".air");
            return m;
        }

        private static Material GetMaterial(Ctx c, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "Air";
            if (c.Out.Materials.TryGetValue(name, out var m)) return m;
            JsonObject def = null;
            foreach (var kv in c.Sys.Materials) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) { def = kv.Value.AsObject(); break; }
            if (def != null) m = MaterialLibrary.FromJson(name, def, c.Reg, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase) ? null : GetMaterial(c, n), ModelSource.SystemData, "system materials." + name);
            else
            {
                m = MaterialLibrary.BuiltIn(name);
                m.Name = name;
                if (c.EnvAirInGaps && string.Equals(MaterialLibrary.Canonical(name), "Air", StringComparison.OrdinalIgnoreCase)) UseEnvironmentAir(m, c.EnvAir.Index);
            }
            // material-targeted overrides, lowest source first so higher sources win
            foreach (var r in c.Ov.Rules.Where(r => r.Matches("material", name, null)).OrderBy(r => r.Source))
            {
                var o = new JsonObject();
                string key = r.Property;
                if (string.Equals(key, "index", StringComparison.OrdinalIgnoreCase) && r.Model != null) o.Set("index", r.RawValue);
                else o.Set(key, r.RawValue);
                MaterialLibrary.Apply(m, o, c.Reg, r.Source, r.Origin);
                c.Used.Add(r);
            }
            c.Out.Materials[name] = m;
            return m;
        }

        // ================================================================================== overrides helpers

        private static OverrideRule Find(Ctx c, SystemElement e, string property)
        {
            var r = c.Ov.FindReplace("element", e.Id, e.Type, property);
            if (r != null) c.Used.Add(r);
            return r;
        }

        private static double Num(Ctx c, SystemElement e, string property, double fallback, out ModelSource src)
        {
            var r = Find(c, e, property);
            if (r != null) { src = r.Source; return r.RawValue.AsDouble(); }
            src = e.Json.Has(property) ? ModelSource.SystemData : ModelSource.EngineDefault;
            return e.Json.GetDouble(property, fallback);
        }

        private static double Num(Ctx c, SystemElement e, string property, double fallback) => Num(c, e, property, fallback, out _);

        private static string Str(Ctx c, SystemElement e, string property, string fallback)
        {
            var r = Find(c, e, property);
            if (r != null) return r.RawValue.Kind == JsonKind.String ? r.RawValue.AsString() : r.RawValue.ToJson(false);
            return e.Json.GetString(property, fallback);
        }

        private static bool Bool(Ctx c, SystemElement e, string property, bool fallback)
        {
            var r = Find(c, e, property);
            if (r != null) return r.RawValue.AsBool();
            return e.Json.GetBool(property, fallback);
        }

        private static ModelSpec Model(Ctx c, SystemElement e, string property, out ModelSource source)
        {
            var r = Find(c, e, property);
            if (r != null) { source = r.Source; return r.Model; }
            source = ModelSource.SystemData;
            return e.Json.Has(property) ? ModelSpec.FromJson(e.Json[property], ModelSource.SystemData, "element " + e.Id + "." + property) : null;
        }

        private static Func<double, double> PerOmegaFunction(Ctx c, SystemElement e, string key, double scale, double fallback)
        {
            var pw = c.Sys.PerWavelength(e, key);
            if (pw == null) return _ => fallback;
            var (w, v) = pw.Value;
            if (w.Length == 1) { double x = v[0] * scale; return _ => x; }
            var o = Enumerable.Range(0, w.Length).OrderBy(i => w[i]).ToArray();
            var ws = o.Select(i => w[i]).ToArray(); var vs = o.Select(i => v[i] * scale).ToArray();
            var li = new LinearInterpolator(ws, vs);
            return x => x <= ws[0] ? vs[0] : x >= ws[ws.Length - 1] ? vs[vs.Length - 1] : li.Evaluate(x);
        }

        // ================================================================================== elements

        private static CompiledElement CompileElement(Ctx c, SystemElement e)
        {
            var ce = new CompiledElement { Source = e, Id = e.Id, Type = e.Type, Label = e.Label ?? e.Id };
            ce.Enabled = Bool(c, e, "enabled", true);
            ce.BeamIn = c.Track?.Clone();
            if (!ce.Enabled) { ce.Notes.Add("disabled"); ce.BeamOut = ce.BeamIn; return ce; }
            string type = e.Type.ToLowerInvariant();
            switch (type)
            {
                case "dispersion": CompileDispersion(c, e, ce); break;
                case "bulk": case "gap": case "medium": CompileBulk(c, e, ce, type == "gap"); break;
                case "interface": case "surface": case "mirror": case "grating": CompileInterface(c, e, ce, type); break;
                case "gratingpair": CompileGratingPair(c, e, ce); break;
                case "chirpedmirror": CompileChirpedMirror(c, e, ce); break;
                case "aperture": CompileAperture(c, e, ce); break;
                case "filter": case "custom":
                {
                    var spec = Model(c, e, e.Json.Has("filter") ? "filter" : "model", out var src) ?? throw new JsonException("needs a \"filter\" (or \"model\") spectral-filter model.");
                    var f = c.Reg.Create<ISpectralFilter>("filter", spec);
                    if (f is ExpressionFilter ef) ef.Omega0 = c.Omega0;
                    ce.Post.Add(new FilterTerm(f, type == "custom" ? "custom" : e.Json.GetString("mechanism", "filter")));
                    c.Out.Provenance.Record("element:" + e.Id, "filter", f.Description, src, spec.Origin);
                    break;
                }
                case "waveplate":
                {
                    if (CompileThickWaveplate(c, e, ce)) break;
                    double waves = e.Json.Has("retardanceWaves") ? Num(c, e, "retardanceWaves", 0.5) :
                        (e.Json.GetString("plate", "half").ToLowerInvariant().StartsWith("q") ? 0.25 : 0.5);
                    double lam = Num(c, e, "designWavelengthM", 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0);
                    double ang = Angle(c, e, "axis", 0);
                    ce.Post.Add(new WaveplateTerm(waves, 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / lam, ang));
                    break;
                }
                case "polarizer":
                    ce.Post.Add(new PolarizerTerm(Angle(c, e, "axis", 0), Num(c, e, "extinctionRatio", 1e5), Num(c, e, "transmission", 1.0)));
                    break;
                case "rotator":
                    ce.Post.Add(new RotatorTerm(Angle(c, e, "angle", 0)));
                    break;
                case "fiber": case "fibre": case "waveguide":
                    CompileFiber(c, e, ce);
                    break;
                case "plate": case "window": case "ndfilter":
                    CompilePlate(c, e, ce, type == "window" ? "plate" : type);
                    break;
                case "thinfilmpolarizer": case "tfp":
                    CompileTfp(c, e, ce);
                    break;
                case "faradayrotator": case "faraday":
                    CompileFaraday(c, e, ce);
                    break;
                case "wavefront": case "aberration":
                    CompileWavefront(c, e, ce);
                    break;
                case "probe": case "marker":
                    ce.Probe = true;
                    break;
                case "lens":
                {
                    double f = Num(c, e, "focalLengthM", double.NaN);
                    if (double.IsNaN(f) || f == 0) throw new JsonException("lens needs a non-zero focalLengthM.");
                    var medium = GetMaterial(c, Str(c, e, "medium", "Air"));
                    Material lm = e.Json.Has("material") ? GetMaterial(c, Str(c, e, "material", null)) : null;
                    double fxl = Num(c, e, "focalLengthXM", f), fyl = Num(c, e, "focalLengthYM", f);
                    var lensAb = AberrationFrom(c, e, lm != null ? lm.IndexAt(c.Omega0) - medium.IndexAt(c.Omega0) : 0.5);
                    ce.Active = new Optics.SurfaceOpticsElement("thin lens", fxl, fyl, lm, medium, c.Omega0, lensAb);
                    double th = Num(c, e, "thicknessM", 0);
                    if (lm != null && th > 0) ce.Pre.Add(new BulkTerm(lm, th));
                    c.Out.Provenance.Record("element:" + e.Id, "lens", ce.Active.Description, ModelSource.SystemData, "system file");
                    break;
                }
                case "user":
                {
                    var spec = Model(c, e, "callback", out var src) ?? throw new JsonException("user element needs \"callback\": \"@name\" (a registered element callback).");
                    string name = spec.Json.GetString("name") ?? throw new JsonException("user element: callback needs a name");
                    var cb = c.Reg.GetCallback<ElementCallback>(name, "element " + e.Id);
                    var prm = e.Json.GetObject("params");
                    ce.Active = new UserElement(name, cb, prm?.ToJson(false));
                    ce.LengthM = Num(c, e, "lengthM", 0);
                    c.Out.Provenance.Record("element:" + e.Id, "user", ce.Active.Description, src, spec.Origin);
                    break;
                }
                case "crystal":
                    Crystals.CrystalCompiler.Compile(Services(c), e, ce);
                    break;
                case "gain":
                    Gain.GainCompiler.Compile(Services(c), e, ce, false);
                    break;
                case "amplifier":
                    Gain.GainCompiler.Compile(Services(c), e, ce, true);
                    break;
                case "saturableabsorber": case "sesam":
                    Lasers.SaturableAbsorberElement.Compile(Services(c), e, ce);
                    break;
                case "oscillator":
                    Lasers.OscillatorCompiler.Compile(Services(c), e, ce);
                    break;
                default:
                    if (Plugins.PluginManager.TryGetElementCompiler(e.Type, out var pc)) { pc(Services(c), e, ce); break; }
                    if (string.Equals(e.Type, "transform", StringComparison.OrdinalIgnoreCase))
                        throw new JsonException("element '" + e.Id + "' is a \"transform\", which needs the fitting plugin (not installed: see `ultrafast plugins list` and docs/plugins/installing.md).");
                    throw new JsonException("unknown element type \"" + e.Type + "\". Known types: " + string.Join(", ", ElementTypes.Concat(Plugins.PluginManager.ElementTypes)) + ".");
            }
            if ((type == "bulk" || type == "crystal" || type == "gain" || type == "amplifier" || type == "lens") && e.Json.Has("ghosts"))
                AddElementGhosts(c, e, ce, type);
            // additive filters on any element ("property": "filter", "mode": "add")
            foreach (var r in c.Ov.FindAdditive("element", e.Id, e.Type, "filter"))
            {
                c.Used.Add(r);
                var f = c.Reg.Create<ISpectralFilter>("filter", r.Model);
                if (f is ExpressionFilter ef) ef.Omega0 = c.Omega0;
                ce.Post.Add(new FilterTerm(f, "filter"));
                c.Out.Provenance.Record("element:" + e.Id, "filter (added)", f.Description, r.Source, r.Origin);
            }
            // scalar polarisation option collapses Jones terms
            if (string.Equals(c.Run.Physics.Polarization, "scalar", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < ce.Pre.Count; i++) if (ce.Pre[i] is JonesTerm) ce.Pre[i] = new ScalarizedTerm(ce.Pre[i]);
                for (int i = 0; i < ce.Post.Count; i++) if (ce.Post[i] is JonesTerm) ce.Post[i] = new ScalarizedTerm(ce.Post[i]);
            }
            if (!c.Run.Physics.Losses)
            {
                ce.Pre.RemoveAll(t => t.IsFlat && t.Mechanism != "dispersion");
                ce.Post.RemoveAll(t => t.IsFlat && t.Mechanism != "dispersion");
            }
            if (c.Track != null) TrackBeam(c, e, ce, type);
            ce.BeamOut = c.Track?.Clone();
            return ce;
        }

        /// <summary>The tracked Gaussian beam at the entrance of the element being compiled, as a beam model for a
        /// medium of index n (null when tracking is off).</summary>
        private static IBeamModel TrackedBeam(Ctx c, double n) => c.Track?.ToModel(n);

        /// <summary>Index of a named material at the carrier, or 1 when it is unknown.</summary>
        private static double IndexOrOne(Ctx c, string name)
        {
            if (string.IsNullOrEmpty(name)) return 1.0;
            try { double n = GetMaterial(c, name).IndexAt(c.Omega0); return n > 0 && !double.IsNaN(n) ? n : 1.0; } catch { return 1.0; }
        }

        /// <summary>dn/dω (s) of a named material at the carrier (central difference), 0 when unknown.</summary>
        private static double IndexSlope(Ctx c, string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            try
            {
                var m = GetMaterial(c, name); double h = 1e-4 * c.Omega0;
                double d = (m.IndexAt(c.Omega0 + h) - m.IndexAt(c.Omega0 - h)) / (2 * h);
                return double.IsNaN(d) ? 0 : d;
            }
            catch { return 0; }
        }

        /// <summary>The group-delay dispersion an element adds from the spatio-temporal couplings of the incoming beam
        /// (docs/reference/components.md#spatio-temporal-couplings): applied to the pulse as a GDD term.</summary>
        private static void AddCouplingGdd(Ctx c, CompiledElement ce, double gdd)
        {
            if (gdd == 0 || double.IsNaN(gdd) || !c.StcPhase) return;
            ce.Post.Add(new PolynomialPhaseTerm(ce.Label + " (angular dispersion)", c.Omega0, 0, gdd, 0, 0));
            ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "spatio-temporal coupling: {0:G4} fs^2 of group-delay dispersion from the angular / spatial dispersion of the beam (the spectrum stays the field on each frequency's chief ray)", gdd * 1e30));
        }

        /// <summary>Advance the tracked Gaussian beam through one element (docs/reference/beam-tracking.md).</summary>
        private static void TrackBeam(Ctx c, SystemElement e, CompiledElement ce, string type)
        {
            TrackBeamCore(c, e, ce, type);
            if (!c.StcTrack) c.Track.ClearCouplings();
            else AddCouplingGdd(c, ce, c.Track.TakePendingGdd());
        }

        private static void TrackBeamCore(Ctx c, SystemElement e, CompiledElement ce, string type)
        {
            var t = c.Track;
            switch (type)
            {
                case "bulk": case "gap": case "medium":
                {
                    string mat = Find(c, e, "material")?.RawValue?.AsString() ?? e.Json.GetString("material", type == "gap" ? "Air" : null);
                    t.Propagate(ce.LengthM, IndexOrOne(c, mat));
                    break;
                }
                case "lens":
                {
                    double f = Num(c, e, "focalLengthM", double.NaN);
                    double fx = Num(c, e, "focalLengthXM", f), fy = Num(c, e, "focalLengthYM", f);
                    t.LensInMedium(fx, fy, IndexOrOne(c, Str(c, e, "medium", "Air")));
                    break;
                }
                case "interface": case "surface": case "mirror": case "grating":
                {
                    string mode = Str(c, e, "mode", type == "mirror" ? "reflect" : type == "grating" ? "diffract" : "refract").ToLowerInvariant();
                    double n1 = IndexOrOne(c, Str(c, e, "before", "Air"));
                    double aoiSigned = Num(c, e, "aoiDeg", double.NaN) * Math.PI / 180;
                    if (double.IsNaN(aoiSigned)) aoiSigned = Num(c, e, "aoiRad", 0);
                    if (e.Json.Has("aoiRad") && e.Json["aoiRad"].Kind == JsonKind.Array) aoiSigned = e.Json.GetDoubleArray("aoiRad").Average();
                    if (e.Json.Has("aoiDeg") && e.Json["aoiDeg"].Kind == JsonKind.Array) aoiSigned = e.Json.GetDoubleArray("aoiDeg").Average() * Math.PI / 180;
                    double aoi = Math.Abs(aoiSigned);
                    bool tx = Optics.BeamState.TangentialIsX(Angle(c, e, "planeOfIncidence", Math.PI / 2), out bool snapped);
                    if (snapped && aoi > 1e-6) ce.Notes.Add("beam tracking: the plane of incidence is not along x or y; astigmatism is applied about the nearer axis (" + (tx ? "x" : "y") + ")");
                    double roc = Num(c, e, "radiusOfCurvatureM", double.NaN);
                    double fl = Num(c, e, "focalLengthM", double.NaN);
                    if (mode == "reflect")
                    {
                        if (!double.IsNaN(roc) || !double.IsNaN(fl))
                        {
                            double R = !double.IsNaN(roc) ? roc : 2 * fl;
                            var (ft, fs) = Optics.BeamState.MirrorFocalLengths(R, aoi);
                            t.LensInMedium(tx ? ft : fs, tx ? fs : ft, n1);
                            if (aoi > 1e-3) ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "off-axis mirror: f_t = {0:G5} m (tangential, {2}), f_s = {1:G5} m (sagittal)", ft, fs, tx ? "x" : "y"));
                        }
                    }
                    else if (mode == "diffract" && (e.Json.Has("linesPerMm") || e.Json.Has("linesPerM") || Find(c, e, "linesPerMm") != null))
                    {
                        double lpm = e.Json.Has("linesPerM") && !e.Json.Has("linesPerMm") ? Num(c, e, "linesPerM", 0) : Num(c, e, "linesPerMm", 0) * 1e3;
                        int order = (int)Num(c, e, "order", -1);
                        double thOut = t.Grating(aoiSigned, 1 / lpm, order, tx);
                        if (double.IsNaN(thOut)) ce.Notes.Add("beam tracking: diffraction order " + order + " is evanescent at the carrier -- the tracked beam is not diffracted");
                        else
                        {
                            ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "grating: order {0} leaves at {1:F3} deg (incidence {2:F3} deg, unfolded and signed), angular dispersion {3:G4} urad/nm", order, thOut * 180 / Math.PI, aoiSigned * 180 / Math.PI,
                                order * lpm / Math.Cos(thOut) * 1e-3));   // d theta / d lambda = m / (period cos theta_out), rad/m -> urad/nm
                        }
                    }
                    else
                    {
                        double n2 = IndexOrOne(c, Str(c, e, "after", mode == "refract" ? "Air" : Str(c, e, "before", "Air")));
                        if (mode == "refract" && Math.Abs(n2 - n1) > 1e-12)
                            t.TiltedInterface(n1, n2, aoiSigned, tx, IndexSlope(c, Str(c, e, "before", "Air")), IndexSlope(c, Str(c, e, "after", "Air")));
                        if (!double.IsNaN(fl)) t.LensInMedium(fl, fl, mode == "refract" ? n2 : n1);
                        else if (!double.IsNaN(roc) && mode == "refract") { double P = (n2 - n1) / roc; t.Apply(1, 0, -P, 1, 1, 0, -P, 1); }
                    }
                    break;
                }
                case "gratingpair":
                {
                    t.Propagate(ce.LengthM, 1.0);
                    // single pass: the output carries the pair's spatial dispersion G cos(alpha) / cos^2(beta) d beta / d omega
                    if ((int)Num(c, e, "passes", 2) == 1)
                    {
                        double lpm = e.Json.Has("linesPerMm") ? Num(c, e, "linesPerMm", 0) * 1e3 : Num(c, e, "linesPerM", double.NaN);
                        double al = Angle(c, e, "aoi", double.NaN), g = Num(c, e, "separationM", double.NaN);
                        int order = Math.Abs((int)Num(c, e, "order", -1));   // as GratingPairTerm: sin(beta) = |m| lambda / d - sin(alpha)
                        double sb = order * 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0 * lpm - Math.Sin(al);
                        if (Math.Abs(sb) < 1 && !double.IsNaN(g))
                        {
                            double cb = Math.Sqrt(1 - sb * sb);
                            double dbdw = -2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS * order * lpm / (c.Omega0 * c.Omega0 * cb);
                            double eel = g * Math.Cos(al) / (cb * cb) * dbdw;
                            bool tx = Optics.BeamState.TangentialIsX(Angle(c, e, "planeOfIncidence", Math.PI / 2), out _);
                            t.AddSpatialDispersion(eel, tx);
                            ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "single pass: spatial chirp {0:G4} mm/nm at the output", eel * (-c.Omega0 * c.Omega0 / (2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS)) * 1e3 * 1e-9));
                        }
                    }
                    break;
                }
                default:
                    if (ce.TrackLenses != null && ce.LengthM > 0)
                    {
                        double n = ce.TrackingIndex > 0 ? ce.TrackingIndex : 1.0, z = 0;
                        foreach (var (zl, invF) in ce.TrackLenses.OrderBy(l => l.ZM))
                        {
                            double zc = Math.Max(z, Math.Min(ce.LengthM, zl));
                            t.Propagate(zc - z, n); z = zc;
                            if (invF != 0) t.Lens(1 / invF, 1 / invF);
                        }
                        t.Propagate(ce.LengthM - z, n);
                        break;
                    }
                    if (TrackComponent(c, e, ce, t, type)) break;
                    if (ce.LengthM > 0)
                    {
                        bool known = ce.TrackingIndex > 0;
                        t.Propagate(ce.LengthM, known ? ce.TrackingIndex : 1.0);
                        if (!known) ce.Notes.Add("beam tracking: element treated as " + ce.LengthM.ToString("G4", CultureInfo.InvariantCulture) + " m of free space");
                    }
                    break;
            }
        }

        private static double Angle(Ctx c, SystemElement e, string baseName, double fallbackRad)
        {
            if (Find(c, e, baseName + "Deg") != null || e.Json.Has(baseName + "Deg")) return Num(c, e, baseName + "Deg", 0) * Math.PI / 180;
            return Num(c, e, baseName + "Rad", fallbackRad);
        }

        private static void CompileDispersion(Ctx c, SystemElement e, CompiledElement ce)
        {
            ce.LengthM = Num(c, e, "lengthM", 0);
            var gd = c.Sys.PerWavelength(e, "groupDelayS");
            if (gd != null)
            {
                var gy = c.Sys.PerWavelength(e, "groupDelayYS");
                var t = new SampledGroupDelayTerm(ce.Label, gd.Value.Omega, gd.Value.Values, gy?.Values);
                ce.Pre.Add(t);
                c.Out.Provenance.Record("element:" + e.Id, "dispersion", t.Description, ModelSource.SystemData, "system file");
            }
            else if (e.Json.Has("gddS2") || e.Json.Has("todS3") || e.Json.Has("fodS4") || e.Json.Has("groupDelayAtCarrierS") || e.Json.Has("gddFs2") || e.Json.Has("todFs3") || e.Json.Has("fodFs4"))
            {
                double wr = e.Json.Has("referenceWavelengthM") ? 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / e.Json.GetDouble("referenceWavelengthM", 0) : c.Omega0;
                var t = new PolynomialPhaseTerm(ce.Label, wr, Num(c, e, "groupDelayAtCarrierS", 0),
                    Num(c, e, "gddS2", 0) + Num(c, e, "gddFs2", 0) * 1e-30, Num(c, e, "todS3", 0) + Num(c, e, "todFs3", 0) * 1e-45, Num(c, e, "fodS4", 0) + Num(c, e, "fodFs4", 0) * 1e-60);
                ce.Pre.Add(t);
                c.Out.Provenance.Record("element:" + e.Id, "dispersion", t.Description, ModelSource.SystemData, "system file");
            }
            else if (c.Sys.PerWavelength(e, "phaseRad") != null)
            {
                var ph = c.Sys.PerWavelength(e, "phaseRad").Value;
                var wl = ph.Omega.Select(w => 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / w).ToArray();
                var spec = new ModelSpec("table", new JsonObject().Set("kind", "table").Set("wavelengthsM", JsonValue.FromArray(wl)).Set("phaseRad", JsonValue.FromArray(ph.Values)).Set("outside", "hold"), ModelSource.SystemData, "element " + e.Id);
                ce.Pre.Add(new FilterTerm(c.Reg.Create<ISpectralFilter>("filter", spec), "dispersion"));
            }
            var tr = c.Sys.PerWavelength(e, "transmission");
            if (tr != null)
                ce.Pre.Add(MakeLoss(tr.Value.Omega, tr.Value.Values, e.Json.GetString("lossMechanism", "transmission"), ce.Label + " transmission"));
            double air = Num(c, e, "airPathM", 0);
            if (air > 0 && c.AirReferenced && c.Run.Physics.AbsoluteIndex)
            {
                ce.Pre.Add(new AirExcessTerm(c.EnvAir, air));
                ce.Notes.Add("absolute index: added dispersion of " + air.ToString("G5", CultureInfo.InvariantCulture) + " m of air (data quoted relative to air)");
            }
        }

        private static LinearTerm MakeLoss(double[] omega, double[] powerT, string mechanism, string description)
        {
            if (powerT.All(v => Math.Abs(v - powerT[0]) < 1e-12)) return new FlatLossTerm(powerT[0], mechanism, description);
            return new SampledLossTerm(omega, powerT, mechanism, description);
        }

        private static void CompileBulk(Ctx c, SystemElement e, CompiledElement ce, bool isGap)
        {
            var matOv = Find(c, e, "material");
            string matName = matOv != null ? matOv.RawValue.AsString() : e.Json.GetString("material", isGap ? "Air" : null);
            if (string.IsNullOrEmpty(matName)) throw new JsonException("bulk element needs a \"material\".");
            var m = GetMaterial(c, matName);
            if (isGap && e.Json.Has("air") && e.Json["air"].Kind == JsonKind.Object && string.Equals(MaterialLibrary.Canonical(matName), "Air", StringComparison.OrdinalIgnoreCase))
            {
                m = GapAir(c, e);
                ce.Notes.Add("air of this gap: " + m.Index.Description);
            }
            ce.LengthM = Num(c, e, "lengthM", double.NaN, out var lenSrc);
            if (double.IsNaN(ce.LengthM) || ce.LengthM < 0) throw new JsonException("needs a non-negative \"lengthM\".");

            var background = new List<LinearTerm>();
            var gd = c.Sys.PerWavelength(e, "groupDelayS");
            string dispSource = Str(c, e, "dispersionSource", "auto").ToLowerInvariant();
            var idxSrc = m.Provenance.TryGetValue("index", out var ip) ? ip.Source : ModelSource.MaterialDefault;
            bool useSampled = gd != null && dispSource != "material" && (dispSource == "data" || idxSrc < ModelSource.SystemOverride);
            if (useSampled)
            {
                var gy = c.Sys.PerWavelength(e, "groupDelayYS");
                var t = new SampledGroupDelayTerm(ce.Label, gd.Value.Omega, gd.Value.Values, gy?.Values);
                background.Add(t);
                c.Out.Provenance.Record("element:" + e.Id, "dispersion", t.Description, ModelSource.SystemData, "system file (traced)");
                if (c.AirReferenced && c.Run.Physics.AbsoluteIndex && m.IsGas && string.Equals(MaterialLibrary.Canonical(matName), "Air", StringComparison.OrdinalIgnoreCase))
                    background.Add(new AirExcessTerm(c.EnvAir, ce.LengthM));
            }
            else
            {
                var t = new BulkTerm(m, ce.LengthM);
                background.Add(t);
                c.Out.Provenance.Record("element:" + e.Id, "dispersion", "material " + m.Name + " (" + m.Index.Description + "), L = " + ce.LengthM.ToString("G6", CultureInfo.InvariantCulture) + " m",
                    gd != null ? idxSrc : ModelSource.MaterialDefault, gd != null ? "material index overridden -> traced data ignored" : "material");
            }
            var tr = c.Sys.PerWavelength(e, "transmission");
            if (tr != null) background.Add(MakeLoss(tr.Value.Omega, tr.Value.Values, "absorption", ce.Label + " internal transmission"));
            else if (m.Attenuation != null) background.Add(new AttenuationTerm(m.Attenuation, ce.LengthM, m.Name));

            // ---- nonlinear medium
            var med = new NonlinearMedium
            {
                N2 = m.N2, RamanFraction = m.RamanFraction, Raman = m.Raman,
                EnableRaman = c.Run.Physics.Raman, EnableSelfSteepening = c.Run.Physics.SelfSteepening
            };
            if (c.Run.Physics.Raman && m.Rotational != null)
            {
                med.Rotational = m.Rotational;
                c.Out.Provenance.Record("element:" + e.Id, "rotationalRaman", m.Rotational.Description + string.Format(CultureInfo.InvariantCulture, "; adiabatic n2 {0:G3} m^2/W", m.Rotational.AdiabaticN2), ModelSource.MaterialDefault, "material " + m.Name);
            }
            var n2r = Find(c, e, "n2");
            if (n2r != null) med.N2 = n2r.RawValue.AsDouble();
            c.Out.Provenance.Record("element:" + e.Id, "n2", med.N2.ToString("G4", CultureInfo.InvariantCulture) + " m^2/W", n2r?.Source ?? (m.Provenance.TryGetValue("n2", out var np) ? np.Source : ModelSource.MaterialDefault), n2r?.Origin ?? "material " + m.Name);
            var rr = Find(c, e, "raman");
            if (rr != null && rr.RawValue.Kind == JsonKind.Bool && !rr.RawValue.AsBool()) med.Rotational = null;
            if (e.Json.TryGet("raman", out var rawRaman) && rawRaman.Kind == JsonKind.Bool && !rawRaman.AsBool()) med.Rotational = null;
            if (rr != null)
            {
                med.Raman = c.Reg.Create<IDelayedResponse>("delayedResponse", rr.Model);
                med.RamanFraction = rr.Model.Json.GetDouble("fR", !double.IsNaN(MaterialLibrary.DefaultRamanFraction(rr.Model.Kind)) ? MaterialLibrary.DefaultRamanFraction(rr.Model.Kind) : med.RamanFraction);
                if (med.Raman == null) med.RamanFraction = 0;
            }
            var ssr = Find(c, e, "selfSteepening"); if (ssr != null) med.EnableSelfSteepening = ssr.RawValue.AsBool();
            // time responses: global adds, element adds, and replace rules (which switch the built-in Kerr off)
            foreach (var r in c.Ov.FindAdditive("global", null, null, "timeResponse").Concat(c.Ov.FindAdditive("element", e.Id, e.Type, "timeResponse")))
            {
                c.Used.Add(r);
                var resp = c.Reg.Create<ITimeDomainResponse>("timeResponse", r.Model);
                med.ExtraResponses.Add(resp);
                c.Out.Provenance.Record("element:" + e.Id, "timeResponse (added)", resp.Description, r.Source, r.Origin);
            }
            if (e.Json.Has("timeResponse"))
            {
                var spec = ModelSpec.FromJson(e.Json["timeResponse"], ModelSource.SystemData, "element " + e.Id + ".timeResponse");
                med.ExtraResponses.Add(c.Reg.Create<ITimeDomainResponse>("timeResponse", spec));
            }
            var rep = Find(c, e, "timeResponse") ?? c.Ov.FindReplace("global", null, null, "timeResponse");
            if (rep != null)
            {
                c.Used.Add(rep);
                med.ReplaceKerr = true;
                med.ExtraResponses.Clear();
                var resp = c.Reg.Create<ITimeDomainResponse>("timeResponse", rep.Model);
                med.ExtraResponses.Add(resp);
                c.Out.Provenance.Record("element:" + e.Id, "timeResponse (replaces Kerr)", resp.Description, rep.Source, rep.Origin);
            }
            if (Bool(c, e, "replaceKerr", false)) med.ReplaceKerr = true;
            // photo-ionisation and plasma (element "ionization" > override > physics.ionization)
            var ionOv = Find(c, e, "ionization");
            JsonValue ionV = ionOv != null ? ionOv.RawValue : e.Json.Has("ionization") ? e.Json["ionization"] : c.Run.Physics.Ionization;
            bool ionOn = ionV != null && !ionV.IsNull && (ionV.Kind == JsonKind.Object || (ionV.Kind == JsonKind.Bool && ionV.AsBool()));
            if (ionOn && c.Run.Physics.Nonlinear)
            {
                var pr = PlasmaResponse.For(m, c.Omega0, ionV.Kind == JsonKind.Object ? ionV.AsObject() : null, out _);
                med.ExtraResponses.Add(pr);
                c.Out.Provenance.Record("element:" + e.Id, "ionization", pr.Description, ionOv?.Source ?? ModelSource.SystemData, ionOv?.Origin ?? (e.Json.Has("ionization") ? "system file" : "run spec physics.ionization"));
            }

            bool wantNl = c.Run.Physics.Nonlinear && Bool(c, e, "nonlinear", !isGap) && med.IsActive && ce.LengthM > 0; // gaps are linear unless asked
            var props = ReadSpatialProperties(c, e);
            // transverse (spatial) model: element > override > run default (physics.transverse)
            var tvOv = Find(c, e, "transverse");
            var tv = Transverse.TransverseSpec.FromJson(tvOv != null ? tvOv.RawValue : e.Json.Has("transverse") ? e.Json["transverse"] : c.Run.Physics.Transverse);
            if (tv.IsSpatial)
            {
                var sb = new Transverse.SpatialBulkElement { Spec = tv, Medium = wantNl ? med : null, Material = m, LengthM = ce.LengthM, Properties = props };
                sb.Background.AddRange(background);
                var bs = Model(c, e, "beam", out var bsrc2);
                if (bs == null && c.Track != null) { sb.Beam = TrackedBeam(c, m.IndexAt(c.Omega0)); ce.Notes.Add("beam: the tracked Gaussian beam (" + sb.Beam.Description + ")"); }
                else if (bs == null) { var ib = c.Run.InputBeam ?? c.Sys.InputBeam; if (ib != null) bs = ModelSpec.FromJson(ib, ModelSource.SystemData, "inputBeam"); }
                if (bs != null)
                {
                    if (bs.Kind == "gaussian" && !bs.Json.Has("wavelengthM") && !bs.Json.Has("zRayleighM")) bs.Json.Set("wavelengthM", 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0);
                    if (bs.Kind == "gaussian" && !bs.Json.Has("n")) bs.Json.Set("n", m.IndexAt(c.Omega0));
                    sb.Beam = c.Reg.Create<IBeamModel>("beam", bs);
                }
                ce.Active = sb;
                c.Out.Provenance.Record("element:" + e.Id, "transverse", tv.Model + (tv.Model == "radial" ? " (" + tv.Points + " points)" : " (" + tv.Nx + " x " + tv.Ny + ")") + ", transport " + tv.Transport, tvOv?.Source ?? ModelSource.SystemData, tvOv?.Origin ?? "system file");
                if (wantNl && med.EffectiveRamanFraction > 0) c.Out.Provenance.Record("element:" + e.Id, "raman", med.Raman.Description + ", fR = " + med.RamanFraction.ToString("G3", CultureInfo.InvariantCulture), rr?.Source ?? ModelSource.MaterialDefault, rr?.Origin ?? "material " + m.Name);
                return;
            }
            IBeamModel beam = null;
            if (wantNl)
            {
                var bspec = Model(c, e, "beam", out var bsrc);
                if (bspec == null && c.Track != null)
                {
                    beam = TrackedBeam(c, m.IndexAt(c.Omega0));
                    c.Out.Provenance.Record("element:" + e.Id, "beam", beam.Description + " (tracked)", ModelSource.EngineDefault, "Gaussian beam tracking");
                    ce.Notes.Add("beam: the tracked Gaussian beam (" + beam.Description + ")");
                }
                else if (bspec == null)
                {
                    var ib = c.Run.InputBeam ?? c.Sys.InputBeam;
                    if (ib != null) { bspec = ModelSpec.FromJson(ib, c.Run.InputBeam != null ? ModelSource.RunOverride : ModelSource.SystemData, "inputBeam"); bsrc = bspec.Source; ce.Notes.Add("no beam on this element: using inputBeam (z measured from the element start)"); }
                }
                if (bspec != null)
                {
                    if (bspec.Kind == "gaussian" && !bspec.Json.Has("wavelengthM") && !bspec.Json.Has("zRayleighM")) bspec.Json.Set("wavelengthM", 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0);
                    if (bspec.Kind == "gaussian" && !bspec.Json.Has("n")) bspec.Json.Set("n", m.IndexAt(c.Omega0));
                    beam = c.Reg.Create<IBeamModel>("beam", bspec);
                    c.Out.Provenance.Record("element:" + e.Id, "beam", beam.Description, bsrc, bspec.Origin);
                }
                else if (beam == null)
                {
                    c.Out.Warnings.Add("Element '" + e.Id + "' is nonlinear (n2 = " + med.N2.ToString("G3", CultureInfo.InvariantCulture) + ") but has no beam; propagated linearly. Add \"beam\" (or inputBeam).");
                    wantNl = false;
                }
            }
            if (props != null) ApplySpatialPropertiesIn1D(c, e, ce, props, beam, med, background, ref wantNl);
            if (wantNl)
            {
                var seg = new NonlinearSegment { Medium = med, Beam = beam, LengthM = ce.LengthM, Material = m };
                seg.Background.AddRange(background);
                ce.Nonlinear = seg;
                if (med.EffectiveRamanFraction > 0) c.Out.Provenance.Record("element:" + e.Id, "raman", med.Raman.Description + ", fR = " + med.RamanFraction.ToString("G3", CultureInfo.InvariantCulture), rr?.Source ?? ModelSource.MaterialDefault, rr?.Origin ?? "material " + m.Name);
            }
            else ce.Pre.AddRange(background);
        }

        /// <summary>indexProfile / absorptionProfile / n2Scale of an element (docs/reference/spatial-properties.md).</summary>
        private static SpatialProperties ReadSpatialProperties(Ctx c, SystemElement e)
        {
            ISpatialField F(string key)
            {
                var spec = Model(c, e, key, out var src);
                if (spec == null) return null;
                var f = c.Reg.Create<ISpatialField>("field", spec);
                c.Out.Provenance.Record("element:" + e.Id, key, f.Description, src, spec.Origin);
                return f;
            }
            var p = new SpatialProperties { IndexChange = F("indexProfile"), Absorption = F("absorptionProfile"), N2Scale = F("n2Scale") };
            return p.Any ? p : null;
        }

        /// <summary>
        /// Without a transverse model the beam has no transverse structure: absorption and n2 scale are averaged over
        /// the beam (Gaussian beam model; intensity-weighted for alpha, intensity^2-weighted for n2) along z; an index
        /// change only shifts the phase / group delay on axis and is not applied (note).
        /// </summary>
        private static void ApplySpatialPropertiesIn1D(Ctx c, SystemElement e, CompiledElement ce, SpatialProperties props, IBeamModel beam, NonlinearMedium med, List<LinearTerm> background, ref bool wantNl)
        {
            double L = ce.LengthM; int nz = 48, ng = 24;
            if (beam == null)
            {
                var bs = Model(c, e, "beam", out _);
                if (bs != null)
                {
                    if (bs.Kind == "gaussian" && !bs.Json.Has("wavelengthM") && !bs.Json.Has("zRayleighM")) bs.Json.Set("wavelengthM", 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0);
                    try { beam = c.Reg.Create<IBeamModel>("beam", bs); } catch { beam = null; }
                }
            }
            double Avg(ISpatialField f, double z, int power)
            {
                if (beam == null) return f.At(0, 0, z);
                double wx = beam.RadiusX(z), wy = beam.RadiusY(z), s = 0, sw = 0;
                var xs = new List<double>(); var ys = new List<double>(); var ws = new List<double>();
                for (int i = 0; i < ng; i++) for (int j = 0; j < ng; j++)
                {
                    double x = (i - (ng - 1) / 2.0) / ng * 6 * wx, y = (j - (ng - 1) / 2.0) / ng * 6 * wy;
                    xs.Add(x); ys.Add(y); ws.Add(Math.Pow(Math.Exp(-2 * (x * x / (wx * wx) + y * y / (wy * wy))), power));
                }
                var v = f.AtPoints(xs.ToArray(), ys.ToArray(), z);
                for (int k = 0; k < v.Length; k++) { s += ws[k] * v[k]; sw += ws[k]; }
                return s / sw;
            }
            string how = beam == null ? "on axis (no beam model)" : "averaged over the Gaussian beam";
            if (props.Absorption != null && L > 0)
            {
                double integ = 0;
                for (int k = 0; k < nz; k++) integ += Avg(props.Absorption, L * (k + 0.5) / nz, 1) * L / nz;
                background.Add(new FlatLossTerm(Math.Exp(-integ), "absorption", "absorptionProfile " + how));
                ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "absorptionProfile {0}: T = {1:G6} (use a transverse model for the spatial shaping it causes)", how, Math.Exp(-integ)));
            }
            if (props.N2Scale != null && wantNl && L > 0)
            {
                double sc = 0; for (int k = 0; k < nz; k++) sc += Avg(props.N2Scale, L * (k + 0.5) / nz, 2) / nz;
                med.N2 *= sc;
                ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "n2Scale {0} (intensity^2-weighted): n2 x {1:G5}", how, sc));
            }
            if (props.IndexChange != null)
                ce.Notes.Add("indexProfile has no effect in a 1-D element (a uniform index change is only a phase); add \"transverse\": {\"model\": \"radial\"} (or cartesian) to see lensing / guiding");
        }

        private static void CompileInterface(Ctx c, SystemElement e, CompiledElement ce, string type)
        {
            string mode = Str(c, e, "mode", type == "mirror" ? "reflect" : type == "grating" ? "diffract" : "refract").ToLowerInvariant();
            var before = GetMaterial(c, Str(c, e, "before", "Air"));
            var after = GetMaterial(c, Str(c, e, "after", mode == "refract" ? "Air" : Str(c, e, "before", "Air")));

            // coating: override (object or name) > element (object or name) > none
            ICoating coating = null; string coatingDesc = null;
            var cr = Find(c, e, "coating");
            JsonValue cv = cr != null ? cr.RawValue : e.Json["coating"];
            if (cv != null && !cv.IsNull)
            {
                JsonObject co;
                if (cv.Kind == JsonKind.String)
                {
                    string name = cv.AsString();
                    co = null;
                    foreach (var kv in c.Sys.Coatings) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) co = kv.Value.AsObject();
                    if (co == null && (name.Equals("none", StringComparison.OrdinalIgnoreCase) || name.Length == 0)) co = new JsonObject().Set("kind", "none");
                    if (co == null) throw new JsonException("coating \"" + name + "\" is not defined in the system's \"coatings\" section.");
                    if (!co.Has("name")) co.Set("name", name);
                }
                else co = cv.AsObject();
                coating = CoatingFactory.Create(co, n => GetMaterial(c, n), c.Omega0, "element " + e.Id + ".coating");
                coatingDesc = coating.Description;
                c.Out.Provenance.Record("element:" + e.Id, "coating", coating.Description, cr?.Source ?? ModelSource.SystemData, cr?.Origin ?? "system file");
            }
            else if (mode == "reflect" && !e.Json.Has("after") && ResolveSubstrate(c, e) == null)
            {
                coating = CoatingFactory.Create(new JsonObject().Set("kind", "ideal").Set("R", 1.0), n => GetMaterial(c, n), c.Omega0, "element " + e.Id);
                ce.Notes.Add("mirror without coating or substrate: ideal R = 1");
            }
            else if (mode == "diffract")
            {
                double eff = Num(c, e, "efficiency", 1.0);
                coating = CoatingFactory.Create(new JsonObject().Set("kind", "efficiency").Set("s", Num(c, e, "efficiencyS", eff)).Set("p", Num(c, e, "efficiencyP", eff)), n => GetMaterial(c, n), c.Omega0, "element " + e.Id);
            }

            // angle of incidence (per wavelength from ray tracing, or scalar)
            Func<double, double> aoi;
            var aoiOv = Find(c, e, "aoiRad") ?? Find(c, e, "aoiDeg");
            if (aoiOv != null) { double a = aoiOv.Property.EndsWith("Deg", StringComparison.OrdinalIgnoreCase) ? aoiOv.RawValue.AsDouble() * Math.PI / 180 : aoiOv.RawValue.AsDouble(); aoi = _ => a; }
            else if (e.Json.Has("aoiRad")) aoi = PerOmegaFunction(c, e, "aoiRad", 1.0, 0);
            else if (e.Json.Has("aoiDeg")) aoi = PerOmegaFunction(c, e, "aoiDeg", Math.PI / 180, 0);
            else aoi = _ => 0.0;
            double aoi0 = aoi(c.Omega0);

            // polarisation basis
            double[] basis;
            if (e.Json.Has("kIn") && e.Json.Has("kOut") && e.Json.Has("normal"))
                basis = c.Basis.Surface(Vec3.From(e.Json.GetDoubleArray("kIn")), Vec3.From(e.Json.GetDoubleArray("kOut")), Vec3.From(e.Json.GetDoubleArray("normal")));
            else
                basis = BasisTracker.FromPsi(Angle(c, e, "planeOfIncidence", Math.PI / 2));

            // clip first (at the surface), then the surface itself, then scatter
            ce.Surface = new SurfaceInfo { AoiRad = aoi0, Coating = coatingDesc ?? "bare" };
            AddClip(c, e, ce);
            var substrateSpec = mode == "reflect" ? ResolveSubstrate(c, e) : null;
            if (substrateSpec != null)
                ce.Pre.Add(CompileMirrorSubstrate(c, e, ce, substrateSpec, before, coating, aoi, basis, out after));
            else
            {
                if (ResolveSubstrate(c, e) != null) ce.Notes.Add("substrate: only a reflecting mirror has substrate echoes; ignored");
                ce.Pre.Add(new InterfaceTerm(ce.Label, before, after, coating, mode, aoi, basis));
            }
            double sigma = Num(c, e, "roughnessRmsM", 0);
            if (sigma > 0) ce.Pre.Add(new ScatterTerm(sigma, before, after, mode, aoi));
            AddCoatingRipple(c, e, ce);
            double scatterDelay = substrateSpec != null && ce.Satellites.Count > 0 ? ce.Satellites[0].DelayS : 0.0;
            AddDelayedScatter(c, e, ce, scatterDelay, ce.Post, new SurfaceGeometry { Before = before, After = after, Aoi = aoi, Mode = mode });
            ce.Surface.Lidt = e.Json.Has("lidt") ? LidtSpec.FromJson(e.Json.GetObject("lidt")) : coating?.Lidt ?? (mode == "refract" ? after.Lidt : null) ?? before.Lidt;
            // curved mirror / focal length: acts on a carried transverse profile (spatial elements downstream)
            double roc = Num(c, e, "radiusOfCurvatureM", double.NaN);
            double fl = Num(c, e, "focalLengthM", double.IsNaN(roc) ? double.NaN : (mode == "reflect" ? roc / 2 : double.NaN));
            bool tx = Optics.BeamState.TangentialIsX(Angle(c, e, "planeOfIncidence", Math.PI / 2), out _);
            double fx = double.PositiveInfinity, fy = double.PositiveInfinity;
            if (!double.IsNaN(fl) && fl != 0)
            {
                if (mode == "reflect" && Math.Abs(aoi0) > 1e-9)
                {
                    // off-axis curved mirror: tangential f_t = R cos(theta)/2, sagittal f_s = R / (2 cos(theta))
                    var (ft, fs) = Optics.BeamState.MirrorFocalLengths(2 * fl, Math.Abs(aoi0));
                    fx = tx ? ft : fs; fy = tx ? fs : ft;
                }
                else { fx = fl; fy = fl; }
            }
            double sf = mode == "reflect" ? 2 * Math.Cos(aoi0) : Math.Abs(after.IndexAt(c.Omega0) - before.IndexAt(c.Omega0));
            var surfAb = AberrationFrom(c, e, sf);
            if (!double.IsInfinity(fx) || !double.IsInfinity(fy) || surfAb != null)
                ce.Active = new Optics.SurfaceOpticsElement(mode == "reflect" ? "curved mirror" : "surface", fx, fy, null, before, c.Omega0, surfAb);
        }

        private static void AddClip(Ctx c, SystemElement e, CompiledElement ce)
        {
            double wx = Num(c, e, "beamRadiusXM", Num(c, e, "beamRadiusM", double.NaN));
            double wy = Num(c, e, "beamRadiusYM", Num(c, e, "beamRadiusM", wx));
            var bspec = Model(c, e, "beam", out _);
            if (double.IsNaN(wx) && bspec != null)
            {
                if (bspec.Kind == "gaussian" && !bspec.Json.Has("wavelengthM") && !bspec.Json.Has("zRayleighM")) bspec.Json.Set("wavelengthM", 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0);
                var b = c.Reg.Create<IBeamModel>("beam", bspec); wx = b.RadiusX(0); wy = b.RadiusY(0);
            }
            if (double.IsNaN(wx) && c.Track != null) { wx = c.Track.RadiusX; wy = c.Track.RadiusY; }
            if (ce.Surface != null) { ce.Surface.BeamRadiusXM = wx; ce.Surface.BeamRadiusYM = wy; }
            IAperture ap = null;
            var apSpec = Model(c, e, "aperture", out _);
            if (apSpec != null) ap = c.Reg.Create<IAperture>("aperture", apSpec);
            else
            {
                double sd = Num(c, e, "semiDiameterM", double.NaN);
                if (double.IsNaN(sd) && e.Json.Has("clearApertureM")) sd = Num(c, e, "clearApertureM", double.NaN) / 2;
                if (!double.IsNaN(sd) && sd > 0) ap = new CircularAperture(sd);
            }
            if (ap == null) return;
            if (double.IsNaN(wx)) { ce.Notes.Add("aperture given but no beam radius at the surface: clipping not evaluated"); return; }
            var bxs = c.Sys.PerWavelength(e, "beamRadiusM");
            if (bxs != null && bxs.Value.Values.Length > 1)
            {
                var t = bxs.Value.Values.Select(w => ap.PowerTransmission(w, w, 0, 0)).ToArray();
                ce.Pre.Add(MakeLoss(bxs.Value.Omega, t, "clip", ap.Description + " (lambda-dependent beam size)"));
            }
            else
            {
                double t = ap.PowerTransmission(wx, wy, Num(c, e, "decenterXM", 0), Num(c, e, "decenterYM", 0));
                if (t < 1 - 1e-15) ce.Pre.Add(new FlatLossTerm(t, "clip", ap.Description + " on w = " + (wx * 1e3).ToString("G4", CultureInfo.InvariantCulture) + " mm"));
            }
        }

        private static void CompileGratingPair(Ctx c, SystemElement e, CompiledElement ce)
        {
            double lpm = e.Json.Has("linesPerMm") ? Num(c, e, "linesPerMm", 0) * 1e3 : Num(c, e, "linesPerM", double.NaN);
            if (double.IsNaN(lpm)) throw new JsonException("gratingPair needs linesPerMm (or linesPerM).");
            double aoi = Angle(c, e, "aoi", double.NaN);
            if (double.IsNaN(aoi)) throw new JsonException("gratingPair needs aoiDeg (or aoiRad).");
            double g = Num(c, e, "separationM", double.NaN);
            if (double.IsNaN(g)) throw new JsonException("gratingPair needs separationM (perpendicular distance; negative for a stretcher).");
            int order = (int)Num(c, e, "order", -1), passes = (int)Num(c, e, "passes", 2);
            var t = new GratingPairTerm(lpm, aoi, g, order, passes, 1.0);
            ce.Pre.Add(t);
            ce.LengthM = Math.Abs(g) * passes * 2;
            double eff = Num(c, e, "efficiency", 1.0);
            if (eff < 1) ce.Pre.Add(new FlatLossTerm(Math.Pow(eff, 2 * passes), "grating", "grating efficiency " + eff.ToString("G4", CultureInfo.InvariantCulture) + " x " + (2 * passes) + " diffractions"));
            c.Out.Provenance.Record("element:" + e.Id, "dispersion", t.Description, ModelSource.SystemData, "system file (analytic Treacy)");
        }

        private static void CompileChirpedMirror(Ctx c, SystemElement e, CompiledElement ce)
        {
            int bounces = (int)Num(c, e, "bounces", 1);
            double wr = e.Json.Has("referenceWavelengthM") ? 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / Num(c, e, "referenceWavelengthM", 0) : c.Omega0;
            var t = new PolynomialPhaseTerm(ce.Label + " (" + bounces + " bounces)", wr, 0, bounces * (Num(c, e, "gddS2", 0) + Num(c, e, "gddFs2", 0) * 1e-30),
                bounces * (Num(c, e, "todS3", 0) + Num(c, e, "todFs3", 0) * 1e-45), bounces * (Num(c, e, "fodS4", 0) + Num(c, e, "fodFs4", 0) * 1e-60));
            ce.Pre.Add(t);
            double R = Num(c, e, "reflectance", 1.0);
            if (R < 1) ce.Pre.Add(new FlatLossTerm(Math.Pow(R, bounces), "coating", "chirped mirror reflectance " + R.ToString("G5", CultureInfo.InvariantCulture) + "^" + bounces));
            // GDD ripple per bounce (amplitude, period in nm at the carrier or in THz), complementary pairs cancel
            double amp = Num(c, e, "rippleGddS2", 0) + Num(c, e, "rippleGddFs2", 0) * 1e-30;
            if (amp != 0)
            {
                double period = Num(c, e, "ripplePeriodRadPerS", double.NaN);
                if (double.IsNaN(period) && (e.Json.Has("ripplePeriodNm") || Find(c, e, "ripplePeriodNm") != null))
                {
                    double lam = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / wr;
                    period = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS * Num(c, e, "ripplePeriodNm", 0) * 1e-9 / (lam * lam);
                }
                if (double.IsNaN(period) && (e.Json.Has("ripplePeriodThz") || Find(c, e, "ripplePeriodThz") != null)) period = 2 * Math.PI * Num(c, e, "ripplePeriodThz", 0) * 1e12;
                if (double.IsNaN(period) || period <= 0) throw new JsonException("chirpedMirror ripple needs ripplePeriodNm, ripplePeriodThz or ripplePeriodRadPerS.");
                var rt = new Optics.GddRippleTerm(amp, period, Num(c, e, "ripplePhaseRad", 0), wr, bounces, Bool(c, e, "complementaryPairs", false));
                ce.Pre.Add(rt);
                c.Out.Provenance.Record("element:" + e.Id, "ripple", rt.Description, ModelSource.SystemData, "system file");
            }
        }

        private static void CompileAperture(Ctx c, SystemElement e, CompiledElement ce)
        {
            ce.Surface = new SurfaceInfo();
            var ap = new Lasers.ApertureElement
            {
                RadiusM = Num(c, e, "radiusM", double.NaN), SoftRadiusM = Num(c, e, "softRadiusM", double.NaN), SoftOrder = (int)Num(c, e, "order", 1),
                HalfX = Num(c, e, "halfWidthXM", double.NaN), HalfY = Num(c, e, "halfWidthYM", Num(c, e, "halfWidthXM", double.NaN)),
                CenterX = Num(c, e, "decenterXM", 0), CenterY = Num(c, e, "decenterYM", 0)
            };
            bool model = e.Json.Has("aperture") || Find(c, e, "aperture") != null;
            if (!model)
            {
                if (!double.IsNaN(ap.RadiusM)) e.Json.Set("semiDiameterM", ap.RadiusM);
                else if (!double.IsNaN(ap.HalfX)) e.Json.Set("aperture", new JsonObject().Set("kind", "rectangular").Set("halfWidthXM", ap.HalfX).Set("halfWidthYM", ap.HalfY));
                else if (!(ap.SoftRadiusM > 0)) throw new JsonException("aperture needs radiusM, halfWidthXM/halfWidthYM, softRadiusM, or an \"aperture\" model.");
            }
            int before = ce.Pre.Count;
            if (!(ap.SoftRadiusM > 0)) AddClip(c, e, ce);
            else
            {
                // soft aperture on a 1-D Gaussian beam (order 1): T = 1 / (1 + w^2 / a^2)
                double wx = Num(c, e, "beamRadiusXM", Num(c, e, "beamRadiusM", double.NaN));
                if (double.IsNaN(wx) && c.Track != null) wx = Math.Sqrt(c.Track.RadiusX * c.Track.RadiusY);
                if (!double.IsNaN(wx)) ce.Pre.Add(new FlatLossTerm(1 / (1 + wx * wx / (ap.SoftRadiusM * ap.SoftRadiusM)), "clip", ap.Description + " on w = " + (wx * 1e3).ToString("G4", CultureInfo.InvariantCulture) + " mm"));
            }
            if (model && double.IsNaN(ap.RadiusM) && double.IsNaN(ap.HalfX) && !(ap.SoftRadiusM > 0)) return;   // an aperture model: 1-D clip only
            ap.Fallback.AddRange(ce.Pre.Skip(before));
            ce.Pre.RemoveRange(before, ce.Pre.Count - before);
            ce.Active = ap;
        }

        // ================================================================================== grid + pulse

        /// <summary>True if the pulse has more than 1e-6 of its peak power in the outer 5 % of the time window.</summary>
        private static bool TouchesEdges(Pulses.PulseField p)
        {
            var pw = Results.PulseMetrics.TemporalPower(p);
            double pk = pw.Max(); int n = pw.Length, m = Math.Max(1, n / 20);
            for (int i = 0; i < m; i++) if (pw[i] > 1e-6 * pk || pw[n - 1 - i] > 1e-6 * pk) return true;
            return false;
        }

        /// <summary>Peak power of the provisional pulse after the group delays of the given elements (their spectral
        /// phase integrated from the carrier). Used only for the B-integral estimate of the grid planner.</summary>
        internal static double PeakPowerAfter(PulseField p0, IList<Func<double, double>> taus)
        {
            var tl = Results.PulseMetrics.TemporalPower(p0).Max();
            if (taus.Count == 0) return tl;
            var g = p0.Grid; int n = g.N, c0 = g.CarrierIndex;
            var om = g.OmegaAbsolute;
            var spec = new double[n]; double smax = 0;
            for (int i = 0; i < n; i++) { spec[i] = p0.Ax[i].Magnitude * p0.Ax[i].Magnitude + p0.Ay[i].Magnitude * p0.Ay[i].Magnitude; smax = Math.Max(smax, spec[i]); }
            Func<int, double> tau = i =>
            {
                if (spec[i] < 1e-8 * smax) return double.NaN;
                double t = 0;
                foreach (var f in taus) { double v; try { v = f(om[i]); } catch { return double.NaN; } if (double.IsNaN(v) || double.IsInfinity(v)) return double.NaN; t += v; }
                return t;
            };
            var phi = new double[n]; var tv = new double[n];
            for (int i = 0; i < n; i++) tv[i] = tau(i);
            double tc = double.IsNaN(tv[c0]) ? 0 : tv[c0];
            for (int i = c0 + 1; i < n; i++) { double a = double.IsNaN(tv[i - 1]) ? tc : tv[i - 1], b = double.IsNaN(tv[i]) ? a : tv[i]; phi[i] = phi[i - 1] + 0.5 * (a + b - 2 * tc) * g.DOmega; }
            for (int i = c0 - 1; i >= 0; i--) { double a = double.IsNaN(tv[i + 1]) ? tc : tv[i + 1], b = double.IsNaN(tv[i]) ? a : tv[i]; phi[i] = phi[i + 1] - 0.5 * (a + b - 2 * tc) * g.DOmega; }
            var ax = new Complex[n]; var ay = new Complex[n];
            for (int i = 0; i < n; i++) { var e = Complex.FromPolarCoordinates(1, phi[i]); ax[i] = p0.Ax[i] * e; ay[i] = p0.Ay[i] * e; }
            var tx = SpectralTransform.FreqToTime(ax); var ty = SpectralTransform.FreqToTime(ay);
            double pk = 0;
            for (int i = 0; i < n; i++) pk = Math.Max(pk, tx[i].Real * tx[i].Real + tx[i].Imaginary * tx[i].Imaginary + ty[i].Real * ty[i].Real + ty[i].Imaginary * ty[i].Imaginary);
            // same normalisation as TemporalPower (the phase keeps the energy); a pulse stretched beyond the provisional
            // window wraps around and beats with itself, which can only overestimate: capped at the transform limit
            double scale = tx.Sum(v => v.Real * v.Real + v.Imaginary * v.Imaginary) + ty.Sum(v => v.Real * v.Real + v.Imaginary * v.Imaginary);
            double e0 = Results.PulseMetrics.TemporalPower(p0).Sum();
            if (scale > 0 && e0 > 0) pk *= e0 / scale;
            return Math.Min(pk, Results.PulseMetrics.TransformLimitedPower(p0).Max() * 1.0001);
        }

        /// <summary>Delay room (s) the window needs for the satellites of <paramref name="elements"/>: the largest delay at
        /// which the satellites of all elements together can reach <paramref name="threshold"/> relative intensity.
        /// The satellites of successive elements combine (a ghost of a ghost); satellites that land at the same delay
        /// (identical plates) are summed coherently in amplitude, the worst case. Making room for every order of every
        /// element instead would grow the window linearly with the number of plates (20 plates with ghosts:
        /// nanoseconds) for satellites far below any measurable level.</summary>
        public static double SatelliteRoom(IEnumerable<CompiledElement> elements, double threshold, List<string> notes = null)
        {
            var groups = new List<List<(double DelayS, double Amp)>>();
            foreach (var e in elements)
            {
                List<(double, double)> gr;
                if (e.Satellites.Count > 0) gr = e.Satellites.Where(x => x.DelayS != 0 && x.Level > 0).Select(x => (Math.Abs(x.DelayS), Math.Sqrt(Math.Min(1, x.Level)))).ToList();
                else if (e.SatelliteDelayS != 0) gr = new List<(double, double)> { (Math.Abs(e.SatelliteDelayS), 1.0) };
                else continue;
                if (gr.Count > 0) groups.Add(gr);
            }
            if (groups.Count == 0) return 0;
            threshold = Math.Min(1, Math.Max(threshold, 1e-300));
            double ampThr = Math.Sqrt(threshold);
            double minDelay = groups.SelectMany(g => g).Min(x => x.DelayS);
            double bin = Math.Max(1e-16, 0.02 * minDelay);
            // worst-case growth of an amplitude through the remaining elements: prune what can never reach the threshold
            double growth = groups.Aggregate(1.0, (acc, g) => acc * (1 + g.Sum(x => x.Amp)));
            double prune = 1e-2 * ampThr / growth;
            // amplitude (upper bound) per delay bin: the generating function prod_e (1 + sum_k a_ek z^tau_ek)
            var dist = new Dictionary<long, double> { [0] = 1.0 };
            foreach (var g in groups)
            {
                var next = new Dictionary<long, double>(dist);
                foreach (var kv in dist)
                    foreach (var (d, a) in g)
                    {
                        double v = kv.Value * a;
                        if (v < prune) continue;
                        long key = kv.Key + (long)Math.Round(d / bin);
                        next.TryGetValue(key, out double old); next[key] = old + v;
                    }
                dist = next;
                if (dist.Count > 200000) { prune *= 10; dist = dist.Where(x => x.Key == 0 || x.Value >= prune).ToDictionary(x => x.Key, x => x.Value); }
            }
            double room = dist.Where(x => x.Value >= ampThr).Select(x => x.Key * bin).DefaultIfEmpty(0).Max();
            double all = groups.Sum(gr => gr.Max(x => x.DelayS));
            if (notes != null && all > room * 1.0001)
                notes.Add(string.Format(CultureInfo.InvariantCulture, "Satellites: the window makes room for {0:G4} ps of ghost/replica delay (satellites and their combinations down to {1:G2} relative intensity, grid.satelliteLevel); the longest delays of all {2} sources added up would need {3:G4} ps. Weaker satellites may wrap around the window.",
                    room * 1e12, threshold, groups.Count, all * 1e12));
            return room;
        }

        /// <summary>
        /// Which satellites (ghosts, echoes, replicas of elements) and pedestal replicas of the input pulse do not fit the
        /// window: for each one above <paramref name="level"/> (relative intensity) that lies beyond half the window minus the
        /// pulse extent, a sentence with its source, delay, level, energy and where it wraps to.
        /// </summary>
        private static List<string> WrapReport(Ctx c, double windowS, double pulseHalfExtentS, double pulseEnergyJ, double level)
        {
            var found = new List<(string What, double Delay, double Level)>();
            foreach (var e in c.Out.Elements.Where(x => x.Enabled))
            {
                int k = 0;
                foreach (var (d, lv) in e.Satellites) { k++; if (d != 0 && lv >= level) found.Add(("element '" + e.Id + "' satellite " + k, d, lv)); }
                if (e.Satellites.Count == 0 && e.SatelliteDelayS != 0) found.Add(("element '" + e.Id + "' satellite", e.SatelliteDelayS, 1.0));
            }
            foreach (var p in c.Run.Pulse.Pedestal)
            {
                string kind = p.GetString("kind", "").ToLowerInvariant();
                if (kind != "replica" && kind != "prepulse" && kind != "postpulse") continue;
                double d = p.GetDouble("delayS", 0); if (kind == "prepulse") d = -Math.Abs(d);
                double lv = p.GetDouble("relativeIntensity", p.GetDouble("relativeEnergy", 1.0));
                if (d != 0 && lv >= level) found.Add(("pedestal " + kind, d, lv));
            }
            var res = new List<string>();
            double half = 0.5 * windowS;
            foreach (var f in found.OrderByDescending(x => x.Level))
            {
                if (Math.Abs(f.Delay) + pulseHalfExtentS <= half) continue;
                double wrapped = ((f.Delay + half) % windowS + windowS) % windowS - half;
                if (res.Count >= 8) { res.Add("(more satellites wrap; the list is cut at 8)"); break; }
                res.Add(string.Format(CultureInfo.InvariantCulture,
                    "Window: {0} at {1:+0.###;-0.###} ps (relative intensity {2:G3} = {3:F1} dB, about {4:G3} J of {5:G3} J) does not fit the +-{6:G4} ps window and wraps around to {7:+0.###;-0.###} ps. Give grid.windowS >= {8:G4} ps, set grid.extendForSatellites, or use grid.tail.",
                    f.What, f.Delay * 1e12, f.Level, 10 * Math.Log10(Math.Max(f.Level, 1e-300)), f.Level * pulseEnergyJ, pulseEnergyJ, half * 1e12, wrapped * 1e12, 2 * 1.25 * (Math.Abs(f.Delay) + pulseHalfExtentS) * 1e12));
            }
            return res;
        }

        private static void BuildGridAndPulse(Ctx c)
        {
            var run = c.Run;
            var ps = run.Pulse;
            var prep = ps.Shape.Equals("spectrum", StringComparison.OrdinalIgnoreCase) ? ps.Prepared() : null;
            if (prep != null) { c.Out.Notes.AddRange(prep.Notes); c.Out.Warnings.AddRange(prep.Warnings); }
            double tl = ps.ResolveDurationS();
            if (double.IsNaN(tl))
            {
                // measured spectrum: transform-limit estimate from its width
                if (prep == null) throw new ModelException("pulse: shape \"spectrum\" needs a measured spectrum (pulse.spectrum).");
                var wl = prep.WavelengthsM; var I = prep.Intensity;
                double cc = PhysicalConstants.SpeedOfLightMPerS;
                var w = wl.Select(l => 2 * Math.PI * cc / l).ToArray();
                var ord = Enumerable.Range(0, w.Length).OrderBy(i => w[i]).ToArray();
                double fw = Results.PulseMetrics.Fwhm(ord.Select(i => w[i]).ToArray(), ord.Select(i => I[i] * wl[i] * wl[i]).ToArray());
                if (double.IsNaN(fw) || fw <= 0) fw = (w.Max() - w.Min()) / 3;
                tl = 0.441 * 2 * Math.PI / fw;
            }
            double window0 = double.IsNaN(run.Grid.WindowS) ? 30 * tl : run.Grid.WindowS;
            // provisional grid: resolve the transform limit well
            double dt0 = Math.Min(window0 / run.Grid.N, tl / 10);
            int n0 = GridPlanner.NextPow2((long)Math.Ceiling(window0 / dt0));
            n0 = Math.Max(64, Math.Min(n0, run.Grid.MaxN));
            var g0 = GridPlanner.Create(n0, window0, c.Omega0, false, out _);
            var p0 = ps.Build(g0, false);
            // a chirped (or long, structured) input may not fit 30 transform limits: widen the provisional window
            // (same dt) until the pulse no longer reaches its edges, so the planner measures its true extent
            if (double.IsNaN(run.Grid.WindowS))
            {
                for (int it = 0; it < 16 && TouchesEdges(p0) && n0 * 2 <= run.Grid.MaxN; it++)
                {
                    window0 *= 2; n0 *= 2;
                    g0 = GridPlanner.Create(n0, window0, c.Omega0, false, out _);
                    p0 = ps.Build(g0, false);
                }
            }

            var tau = c.Out.Elements.Where(x => x.Enabled).Select(x => (Func<double, double>)x.GroupDelayAt).ToList();
            bool anyNl = c.Out.Elements.Any(x => x.Nonlinear != null);
            // spectral margin: nonlinear broadening scales with the B-integral (estimated from the input peak power)
            // the peak power at each nonlinear element follows the dispersion of the elements before it (a stretched pulse
            // in an amplifier has a much lower peak power than the input), so the B estimate applies their group delays
            double bEst = 0, k0 = c.Omega0 / PhysicalConstants.SpeedOfLightMPerS;
            var enabled = c.Out.Elements.Where(x => x.Enabled).ToList();
            for (int ei = 0; ei < enabled.Count; ei++)
            {
                var x = enabled[ei];
                if (x.Nonlinear == null || x.Nonlinear.Medium.ReplaceKerr) continue;
                double pw = PeakPowerAfter(p0, enabled.Take(ei).Select(e => (Func<double, double>)e.GroupDelayAt).ToList());
                double inv = 0; int ns = 200;
                for (int k = 0; k < ns; k++) inv += x.Nonlinear.LengthM / ns / x.Nonlinear.Beam.EffectiveArea(x.Nonlinear.LengthM * (k + 0.5) / ns);
                bEst += k0 * x.Nonlinear.Medium.N2 * pw * inv;
            }
            double margin = double.IsNaN(run.Grid.SpectralMargin) ? (anyNl ? Math.Max(3.0, 1.0 + bEst) : 1.3) : run.Grid.SpectralMargin;
            if (anyNl) c.Out.Notes.Add(string.Format(CultureInfo.InvariantCulture, "Estimated total B-integral (peak power after the dispersion before each element, no losses): {0:G3} rad; spectral margin {1:G3}.", bEst, margin));
            var plan = GridPlanner.Plan(p0, tau, margin, anyNl ? run.Grid.MinSpanFraction : 0.0, window0, run.Grid.ExplicitResolution ? run.Grid.N : n0, run.Grid.MaxN, run.Grid.Auto || run.Grid.ExtendForSatellites, run.Grid.ExplicitResolution, Results.PulseMetrics.Fwhm(g0.Time, Results.PulseMetrics.TransformLimitedPower(p0)) / run.Grid.MinPointsPerFwhm,
                (run.Grid.TailEnabled ? 0.0 : SatelliteRoom(c.Out.Elements.Where(x => x.Enabled), run.Grid.SatelliteLevel, c.Out.Notes)) + run.Pulse.SatelliteHalfWindowS());
            if (run.Grid.TailEnabled) c.Out.Notes.Add("grid.tail: ghosts, echoes and other satellites are carried on the long tail grid; the main window is sized for the pulse only.");
            c.Out.GridPlan = plan;
            c.Out.Notes.AddRange(plan.Notes);
            c.Out.Warnings.AddRange(plan.Warnings);
            if (!run.Grid.TailEnabled)
            {
                var wraps = WrapReport(c, plan.WindowS, 5 * tl, p0.TotalEnergyJ(), run.Grid.SatelliteLevel);
                if (wraps.Count > 0)
                {
                    if (run.Grid.Strict) throw new JsonException("grid.strict: " + string.Join(" ", wraps));
                    c.Out.Warnings.AddRange(wraps);
                }
            }
            var grid = GridPlanner.Create(plan.N, plan.WindowS, c.Omega0, run.Grid.SnapCarrier, out double w0s);
            if (w0s != c.Omega0)
                c.Out.Notes.Add(string.Format(CultureInfo.InvariantCulture, "Carrier snapped to the frequency grid: {0:F4} -> {1:F4} nm.",
                    2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0 * 1e9, 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / w0s * 1e9));
            c.Out.Grid = grid;
            c.Out.Input = ps.Build(grid);
            c.Out.Notes.Add("Input pulse: " + ps.Describe() + string.Format(CultureInfo.InvariantCulture, ", {0:G4} J", c.Out.Input.TotalEnergyJ()));
        }
    }

    /// <summary>Compiler helpers handed to element compilers living in other namespaces (crystals, gain media).</summary>
    public sealed class CompilerServices
    {
        internal CompilerServices() { }
        internal Func<string, Material> MaterialLookup;
        public SystemDescription System { get; internal set; }
        public RunSpec Run { get; internal set; }
        public ModelRegistry Registry { get; internal set; }
        public ProvenanceLog Provenance { get; internal set; }
        public double Omega0 { get; internal set; }
        public List<string> Warnings { get; internal set; }
        internal Func<SystemElement, string, double, double> NumFn;
        internal Func<SystemElement, string, string, string> StrFn;
        internal Func<SystemElement, string, OverrideRule> FindFn;
        public Material GetMaterial(string name) => MaterialLookup(name);
        internal Func<SystemElement, SpatialProperties> PropertiesFn;
        /// <summary>The element's position-dependent properties (indexProfile, absorptionProfile, n2Scale), or null.</summary>
        public SpatialProperties SpatialProperties(SystemElement e) => PropertiesFn(e);
        public double Num(SystemElement e, string key, double fallback) => NumFn(e, key, fallback);
        internal Func<JsonObject, string, CompiledElement> CompileFn;
        /// <summary>Compile a nested element (e.g. inside an oscillator cavity); its id becomes "prefix.id".</summary>
        public CompiledElement CompileNested(JsonObject element, string prefix) => CompileFn(element, prefix);
        public string Str(SystemElement e, string key, string fallback) => StrFn(e, key, fallback);
        public OverrideRule Find(SystemElement e, string key) => FindFn(e, key);
        internal Func<double, IBeamModel> TrackedFn;
        internal Func<Optics.BeamState> TrackFn;
        /// <summary>The tracked Gaussian beam at the entrance of the element being compiled, as a beam model in a medium of
        /// index n (z from the element start); null when beam tracking is off.</summary>
        public IBeamModel TrackedBeam(double n) => TrackedFn?.Invoke(n);
        /// <summary>The tracked beam state itself (null when tracking is off). Do not modify it.</summary>
        public Optics.BeamState Tracked => TrackFn?.Invoke();
    }

    /// <summary>Extra dispersion of air relative to vacuum, (n_air - 1) w L / c, for paths whose data were quoted
    /// relative to air (OpticStudio's convention).</summary>
    public sealed class AirExcessTerm : PhaseTerm
    {
        public override bool IsSmooth => true;
        private readonly Material _air; private readonly double _l;
        public AirExcessTerm(Material air, double lengthM) { _air = air; _l = lengthM; Mechanism = "dispersion"; }
        public override string Description => "air (absolute index), " + _l.ToString("G5", CultureInfo.InvariantCulture) + " m";
        protected override void Phase(double[] omega, double[] phi)
        {
            _air.Index.Evaluate(omega, phi);
            for (int i = 0; i < omega.Length; i++) phi[i] = (phi[i] - 1) * omega[i] * _l / PhysicalConstants.SpeedOfLightMPerS;
        }
    }

    /// <summary>Total integrated scatter of a rough surface (Davies/Bennett): reflection TIS = (4 pi sigma n1 cos(t1)/lambda)^2,
    /// transmission TIS = (2 pi sigma |n1 cos t1 - n2 cos t2| / lambda)^2; power transmission exp(-TIS).</summary>
    public sealed class ScatterTerm : PhaseTerm
    {
        public override bool IsSmooth => true;
        private readonly double _s; private readonly Material _a, _b; private readonly string _mode; private readonly Func<double, double> _aoi;
        public ScatterTerm(double sigma, Material before, Material after, string mode, Func<double, double> aoi) { _s = sigma; _a = before; _b = after; _mode = mode; _aoi = aoi; Mechanism = "scatter"; }
        /// <summary>Total integrated scatter of one surface: reflection (4 pi sigma n1 cos t1 / lambda)^2, refraction (2 pi sigma |n1 cos t1 - n2 cos t2| / lambda)^2.</summary>
        public static double Tis(double sigmaM, double lambdaM, double n1, double theta1, double n2, string mode)
        {
            if (mode == "refract")
            {
                double s2 = n1 * Math.Sin(theta1) / n2, c2 = s2 < 1 ? Math.Sqrt(1 - s2 * s2) : 0;
                return Math.Pow(2 * Math.PI * sigmaM * Math.Abs(n1 * Math.Cos(theta1) - n2 * c2) / lambdaM, 2);
            }
            return Math.Pow(4 * Math.PI * sigmaM * n1 * Math.Cos(theta1) / lambdaM, 2);
        }
        public override string Description => "surface scatter, sigma = " + (_s * 1e9).ToString("G3", CultureInfo.InvariantCulture) + " nm rms";
        protected override void Phase(double[] omega, double[] phi) { for (int i = 0; i < omega.Length; i++) phi[i] = 0; }
        protected override void Amplitude(double[] omega, double[] amp)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double w = omega[i]; if (w <= 0) { amp[i] = 0; continue; }
                double lam = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / w;
                double n1 = _a.IndexAt(w), t1 = _aoi(w);
                double tis;
                if (_mode == "refract")
                {
                    double n2 = _b.IndexAt(w);
                    double s2 = n1 * Math.Sin(t1) / n2;
                    double c2 = s2 < 1 ? Math.Sqrt(1 - s2 * s2) : 0;
                    tis = Math.Pow(2 * Math.PI * _s * Math.Abs(n1 * Math.Cos(t1) - n2 * c2) / lam, 2);
                }
                else tis = Math.Pow(4 * Math.PI * _s * n1 * Math.Cos(t1) / lam, 2);
                amp[i] = Math.Exp(-0.5 * tis);
            }
        }
        public override double GroupDelayAt(double omega) => 0;
    }

    /// <summary>Polarisation-averaged version of a Jones term (physics.polarization = "scalar").</summary>
    public sealed class ScalarizedTerm : LinearTerm
    {
        private readonly LinearTerm _t;
        public ScalarizedTerm(LinearTerm t) { _t = t; Mechanism = t.Mechanism; }
        public override string Description => _t.Description + " [s/p averaged]";
        public override double GroupDelayAt(double omega) => _t.GroupDelayAt(omega);
        public override JonesSpectrum Evaluate(OmegaAxis axis)
        {
            var j = _t.Evaluate(axis);
            var h = new Complex[axis.N];
            for (int i = 0; i < axis.N; i++) { j.Get(i, out var a, out _, out _, out var d); h[i] = 0.5 * (a + d); }
            return JonesSpectrum.Scalar(h);
        }
        public override bool TryPhaseAmplitude(OmegaAxis axis, out double[] phX, out double[] ampX, out double[] phY, out double[] ampY)
        {
            var j = Evaluate(axis);
            phX = new double[axis.N]; ampX = new double[axis.N]; phY = null; ampY = null;
            double prev = 0;
            for (int i = 0; i < axis.N; i++)
            {
                ampX[i] = j.Xx[i].Magnitude;
                double p = j.Xx[i].Phase;
                if (i > 0) { double d = p - prev; d -= 2 * Math.PI * Math.Round(d / (2 * Math.PI)); phX[i] = phX[i - 1] + d; } else phX[i] = p;
                prev = p;
            }
            return true;
        }
    }
}
