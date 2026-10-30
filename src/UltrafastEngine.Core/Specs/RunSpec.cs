using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltrafastEngine.Models;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Specs
{
    /// <summary>Time/frequency grid settings. With Auto (default) the engine sizes the window from the system's
    /// group-delay spread and the time step from the spectrum, within MaxN.</summary>
    public sealed class GridSpec
    {
        public int N { get; set; } = 4096;
        /// <summary>Time window (s). NaN: 30x the transform-limited duration (before auto-sizing).</summary>
        public double WindowS { get; set; } = double.NaN;
        public bool Auto { get; set; } = true;
        public int MaxN { get; set; } = 1 << 22;
        /// <summary>Put the carrier on an exact multiple of the frequency step (needed by carrier-resolved mode;
        /// shifts the carrier by &lt; half a bin).</summary>
        public bool SnapCarrier { get; set; } = true;
        /// <summary>Spectral span factor applied to the input spectrum (-40 dB width) when sizing the time step.
        /// NaN: 1.3 for linear systems, 3 when any element is nonlinear.</summary>
        public double SpectralMargin { get; set; } = double.NaN;
        /// <summary>True when the user gave "n" or "dtS": the configured time step is then a floor on resolution.
        /// Otherwise the auto-sizer chooses the time step from the spectrum alone.</summary>
        public bool ExplicitResolution { get; set; }
        /// <summary>Nonlinear runs: minimum half-span of the frequency grid as a fraction of the carrier (default 0.1),
        /// so that new frequencies created by user time-domain responses are not aliased.</summary>
        public double MinSpanFraction { get; set; } = 0.1;
        /// <summary>Minimum number of time samples across the transform-limited FWHM (keeps temporal diagnostics accurate).</summary>
        public double MinPointsPerFwhm { get; set; } = 12;
        /// <summary>Weakest combination of satellites (ghosts, replicas) the window makes room for, relative intensity
        /// (the product of the levels of the satellites combined). Default 1e-8.</summary>
        public double SatelliteLevel { get; set; } = 1e-8;
        /// <summary>With grid.auto false (a manual window): grow the window, keeping the time step, so that ghosts, echoes and
        /// pedestal replicas above grid.satelliteLevel fit. Default false (the manual window is kept and the wrap-around is reported).</summary>
        public bool ExtendForSatellites { get; set; }
        /// <summary>Make a satellite or pedestal that wraps around the window an error instead of a warning. Default false.</summary>
        public bool Strict { get; set; }
        /// <summary>Delayed-light ledger (docs/reference/delayed-light.md): a second, long, linear grid next to the main one.</summary>
        public bool TailEnabled { get; set; }
        /// <summary>Window of the tail grid (s); NaN = sized from the satellites (at least 8 x the main window).</summary>
        public double TailWindowS { get; set; } = double.NaN;
        /// <summary>Tail energy fraction above which the result carries a warning (default 0.01).</summary>
        public double TailWarnFraction { get; set; } = 0.01;
        /// <summary>Take the exact long-grid step for every linear element, also the smooth ones (default false: smooth elements act on main and tail separately).</summary>
        public bool TailExactSteps { get; set; }
        /// <summary>First-order cross-phase of the tail by the main pulse in nonlinear elements (default true).</summary>
        public bool TailCrossPhase { get; set; } = true;

        public static GridSpec FromJson(JsonObject o)
        {
            var g = new GridSpec();
            if (o == null) return g;
            g.N = o.GetInt("n", g.N);
            g.ExplicitResolution = o.Has("n");
            g.WindowS = o.GetDouble("windowS", o.Has("windowPs") ? o.GetDouble("windowPs", 0) * 1e-12 : double.NaN);
            g.Auto = o.GetBool("auto", true);
            g.MaxN = o.GetInt("maxN", g.MaxN);
            g.SnapCarrier = o.GetBool("snapCarrier", true);
            g.SpectralMargin = o.GetDouble("spectralMargin", double.NaN);
            g.MinSpanFraction = o.GetDouble("minSpanFraction", 0.1);
            g.MinPointsPerFwhm = o.GetDouble("minPointsPerFwhm", 12);
            g.SatelliteLevel = o.GetDouble("satelliteLevel", 1e-8);
            g.ExtendForSatellites = o.GetBool("extendForSatellites", false);
            g.Strict = o.GetBool("strict", false);
            var tv = o["tail"];
            if (tv != null && !tv.IsNull)
            {
                if (tv.Kind == JsonKind.Bool) g.TailEnabled = tv.AsBool();
                else if (tv.Kind == JsonKind.Object)
                {
                    var to = tv.AsObject();
                    g.TailEnabled = to.GetBool("enabled", true);
                    g.TailWindowS = to.GetDouble("windowS", to.Has("windowPs") ? to.GetDouble("windowPs", 0) * 1e-12 : double.NaN);
                    g.TailWarnFraction = to.GetDouble("warnEnergyFraction", 0.01);
                    g.TailExactSteps = to.GetBool("exactSteps", false);
                    g.TailCrossPhase = to.GetBool("crossPhase", true);
                }
                else throw new JsonException("grid.tail is true/false or an object {windowS, warnEnergyFraction, exactSteps, crossPhase}.");
                // the tail grid contains the main frequency samples whether or not the carrier is snapped, and a small main window has a
                // coarse frequency step (8.5 ps: 117 GHz = 0.4 nm), so the carrier stays where the user put it unless snapCarrier is given
                if (g.TailEnabled && !o.Has("snapCarrier")) g.SnapCarrier = false;
            }
            if (!(g.SatelliteLevel > 0) || g.SatelliteLevel > 1) throw new JsonException("grid.satelliteLevel is a relative intensity, 0 < satelliteLevel <= 1.");
            return g;
        }
    }

    public sealed class PhysicsOptions
    {
        public bool Nonlinear { get; set; } = true;
        public bool SelfSteepening { get; set; } = true;
        public bool Raman { get; set; } = true;
        /// <summary>"envelope", "carrier" (carrier-resolved real field) or "auto" (carrier only where a model asks for it).</summary>
        public string FieldMode { get; set; } = "auto";
        /// <summary>Highest harmonic the carrier-resolved grid must hold (3 = third harmonic).</summary>
        public double FieldHarmonic { get; set; } = 3.0;
        /// <summary>Add the dispersion of air for air paths quoted relative to air (Zemax convention).</summary>
        public bool AbsoluteIndex { get; set; } = true;
        /// <summary>"jones" (full 2x2 polarisation) or "scalar" (s/p averaged).</summary>
        public string Polarization { get; set; } = "jones";
        public bool Losses { get; set; } = true;
        /// <summary>Default transverse model for bulk elements and gaps without their own "transverse" (null = 1-D).</summary>
        public JsonValue Transverse { get; set; }
        /// <summary>Gaussian-beam (q-parameter) tracking through the system: "auto" (on when inputBeam is a Gaussian
        /// beam), "on" or "off". Elements without a beam of their own then use the tracked beam.</summary>
        public string BeamTracking { get; set; } = "auto";
        /// <summary>Spatio-temporal couplings with the tracked beam: "auto" (tracked; their dispersion is applied
        /// unless the system has traced group delays), "on", "track" (reported only), "profile" (carried by the cartesian profile), "off".</summary>
        public string SpatioTemporal { get; set; } = "auto";
        /// <summary>Photo-ionisation and plasma in every nonlinear bulk element: null/false (default), true, or an object
        /// with model options (docs/reference/ionization.md). Elements can set their own "ionization".</summary>
        public JsonValue Ionization { get; set; }

        public static PhysicsOptions FromJson(JsonObject o)
        {
            var p = new PhysicsOptions();
            if (o == null) return p;
            p.Nonlinear = o.GetBool("nonlinear", true);
            p.SelfSteepening = o.GetBool("selfSteepening", true);
            p.Raman = o.GetBool("raman", true);
            p.FieldMode = o.GetString("fieldMode", "auto");
            p.FieldHarmonic = o.GetDouble("fieldHarmonic", 3.0);
            p.AbsoluteIndex = o.GetBool("absoluteIndex", true);
            p.Polarization = o.GetString("polarization", "jones");
            p.Losses = o.GetBool("losses", true);
            p.Transverse = o.Has("transverse") ? o["transverse"] : null;
            p.Ionization = o.Has("ionization") ? o["ionization"] : null;
            if (o.TryGet("beamTracking", out var bt)) p.BeamTracking = bt.Kind == JsonKind.Bool ? (bt.AsBool() ? "on" : "off") : bt.AsString();
            if (o.TryGet("spatioTemporal", out var st)) p.SpatioTemporal = st.Kind == JsonKind.Bool ? (st.AsBool() ? "on" : "off") : st.AsString().ToLowerInvariant();
            if (p.SpatioTemporal != "auto" && p.SpatioTemporal != "on" && p.SpatioTemporal != "track" && p.SpatioTemporal != "off" && p.SpatioTemporal != "profile")
                throw new JsonException("physics.spatioTemporal must be \"auto\", \"on\", \"track\", \"profile\" or \"off\".");
            return p;
        }
    }

    public sealed class OutputOptions
    {
        public bool Npz { get; set; } = true;
        public bool Csv { get; set; } = true;
        public bool Json { get; set; } = true;
        /// <summary>Maximum points per exported 1D trace (0 = full grid). Traces are cropped to the region holding
        /// the energy first, then decimated only if still longer.</summary>
        public int MaxPoints { get; set; } = 0;

        public static OutputOptions FromJson(JsonObject o)
        {
            var r = new OutputOptions();
            if (o == null) return r;
            r.Npz = o.GetBool("npz", true); r.Csv = o.GetBool("csv", true); r.Json = o.GetBool("json", true);
            r.MaxPoints = o.GetInt("maxPoints", 0);
            return r;
        }
    }

    /// <summary>
    /// The run specification (<c>run.json</c>): the pulse, grid, physics switches, solver, probes, instruments,
    /// overrides and outputs. Format: docs/reference/run-spec.md.
    /// </summary>
    public sealed class RunSpec
    {
        public const string FormatName = "ultrafast-run";
        public PulseSpec Pulse { get; set; }
        public GridSpec Grid { get; set; } = new GridSpec();
        public PhysicsOptions Physics { get; set; } = new PhysicsOptions();
        public StepperSettings Stepper { get; set; } = new StepperSettings();
        /// <summary>"all" (default), "end", or a list of element ids whose full spectra/profiles are stored.</summary>
        public string ProbeMode { get; set; } = "all";
        public HashSet<string> ProbeIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<JsonObject> Instruments { get; } = new List<JsonObject>();
        /// <summary>Detector presets by name ("detectors"): {instrument: {keys filled into an instrument that does not set them}, detector: {read-out}}.
        /// An instrument uses one with <c>"detector": "name"</c> (docs/reference/instruments.md#detector-presets).</summary>
        public JsonObject Detectors { get; set; } = new JsonObject();
        public List<OverrideRule> Overrides { get; } = new List<OverrideRule>();
        public OutputOptions Outputs { get; set; } = new OutputOptions();
        /// <summary>Compute back-end ("cpu", "auto" or a plugin's: "cuda", "opencl"); null = the process default
        /// (<see cref="Numerics.ComputeBackends.DefaultName"/>: ULTRAFAST_BACKEND or "cpu").</summary>
        public string ComputeBackend { get; set; }
        /// <summary>FFT provider for the run ("auto", "managed", "fftw", "accelerate"); null keeps the process default.</summary>
        public string ComputeFft { get; set; }
        /// <summary>Device index for GPU back-ends (-1: the back-end's choice).</summary>
        public int ComputeDevice { get; set; } = -1;
        /// <summary>"auto" back-end: smallest problem (complex samples per state) sent to a GPU (0: the back-end's default).</summary>
        public int GpuMinPoints { get; set; }
        /// <summary>GPU back-ends: concurrent element propagations per device (0: the back-end's default, 4).</summary>
        public int DeviceSlots { get; set; }
        /// <summary>The raw "compute" object (back-end specific keys).</summary>
        public JsonObject ComputeOptions { get; set; }
        public int Threads { get; set; }
        /// <summary>Beam at the system input (used by elements that have no beam of their own).</summary>
        public JsonObject InputBeam { get; set; }
        public string Name { get; set; } = "run";
        public string BaseDirectory { get; set; }
        /// <summary>Changes made while upgrading an older run spec (reported in the run notes).</summary>
        public List<string> MigrationNotes { get; } = new List<string>();
        public JsonObject Raw { get; set; }

        public static RunSpec Load(string path)
        {
            var r = FromJson(JsonValue.ParseFile(path).AsObject(), Path.GetDirectoryName(Path.GetFullPath(path)));
            return r;
        }

        public static RunSpec Parse(string json, string baseDirectory = null) => FromJson(JsonValue.Parse(json).AsObject(), baseDirectory);

        public static RunSpec FromJson(JsonObject o, string baseDirectory = null)
        {
            string fmt = o.GetString("format", FormatName);
            if (fmt != FormatName) throw new JsonException("Not a run spec: format is \"" + fmt + "\" (expected \"" + FormatName + "\").");
            var migrationNotes = new List<string>();
            Schema.Migrations.MigrateRun(o, migrationNotes);
            var r = new RunSpec
            {
                Raw = o,
                BaseDirectory = baseDirectory,
                Name = o.GetString("name", "run"),
                Pulse = PulseSpec.FromJson(o.GetObject("pulse"), baseDirectory),
                Grid = GridSpec.FromJson(o.GetObject("grid")),
                Physics = PhysicsOptions.FromJson(o.GetObject("physics")),
                Outputs = OutputOptions.FromJson(o.GetObject("outputs")),
                InputBeam = o.GetObject("inputBeam")
            };
            var co = o.GetObject("compute");
            if (co != null)
            {
                r.ComputeBackend = co.GetString("backend", null); r.Threads = co.GetInt("threads", 0);
                r.ComputeFft = co.GetString("fft"); r.ComputeDevice = co.GetInt("device", -1);
                r.GpuMinPoints = co.GetInt("gpuMinPoints", 0); r.DeviceSlots = co.GetInt("deviceSlots", 0); r.ComputeOptions = co;
            }
            var st = o.GetObject("stepper");
            if (st != null)
            {
                r.Stepper.Method = st.GetString("method", r.Stepper.Method);
                r.Stepper.Tolerance = st.GetDouble("tolerance", r.Stepper.Tolerance);
                r.Stepper.MaxNonlinearPhasePerStepRad = st.GetDouble("maxPhasePerStepRad", r.Stepper.MaxNonlinearPhasePerStepRad);
                r.Stepper.MaxAreaChangePerStep = st.GetDouble("maxAreaChangePerStep", r.Stepper.MaxAreaChangePerStep);
                r.Stepper.MinStepM = st.GetDouble("minStepM", r.Stepper.MinStepM);
                r.Stepper.MaxStepM = st.GetDouble("maxStepM", r.Stepper.MaxStepM);
                r.Stepper.MaxSteps = st.GetInt("maxSteps", r.Stepper.MaxSteps);
                r.Stepper.FixedSteps = st.GetInt("steps", 0);
            }
            if (o.TryGet("probes", out var pv))
            {
                if (pv.Kind == JsonKind.String) r.ProbeMode = pv.AsString();
                else { r.ProbeMode = "list"; foreach (var id in pv.AsStringArray()) r.ProbeIds.Add(id); }
            }
            var ins = o.GetArray("instruments");
            if (ins != null) foreach (var i in ins) r.Instruments.Add(i.AsObject());
            r.Detectors = o.GetObject("detectors") ?? new JsonObject();
            r.Overrides.AddRange(OverrideSet.Parse(o.GetArray("overrides"), ModelSource.RunOverride, "run spec"));
            r.MigrationNotes.AddRange(migrationNotes);
            return r;
        }

        public bool IsProbe(string elementId, bool isLast)
        {
            switch (ProbeMode.ToLowerInvariant())
            {
                case "all": return true;
                case "end": case "none": return isLast;
                default: return isLast || ProbeIds.Contains(elementId);
            }
        }
    }
}
