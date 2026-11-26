using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Zemax
{
    /// <summary>Maps LDE rows (by material, comment, surface type or coating name) to system-file media.</summary>
    public sealed class MediumRule
    {
        public string MatchMaterial, MatchComment, MatchType;
        /// <summary>Name used in the system file's "materials" section (default: the Zemax material name).</summary>
        public string Material;
        public bool Nonlinear;
        /// <summary>Optional material definition written to the system file (n2, raman, gas, index...).</summary>
        public JsonObject Definition;
        /// <summary>Optional beam for nonlinear elements when GBPS/GBPW is unavailable.</summary>
        public JsonObject Beam;

        public bool Matches(SurfaceRow r) =>
            (MatchComment == null || string.Equals((r.Comment ?? "").Trim(), MatchComment, StringComparison.OrdinalIgnoreCase)) &&
            (MatchMaterial == null || string.Equals((r.Material ?? "").Trim(), MatchMaterial, StringComparison.OrdinalIgnoreCase)) &&
            (MatchType == null || (r.TypeName ?? "").IndexOf(MatchType, StringComparison.OrdinalIgnoreCase) >= 0) &&
            (MatchComment != null || MatchMaterial != null || MatchType != null);

        public static MediumRule FromJson(JsonObject o) => new MediumRule
        {
            MatchMaterial = o.GetString("matchMaterial"), MatchComment = o.GetString("matchComment"), MatchType = o.GetString("matchType"),
            Material = o.GetString("material"), Nonlinear = o.GetBool("nonlinear", false), Definition = o.GetObject("definition"), Beam = o.GetObject("beam")
        };
    }

    /// <summary>Maps surfaces (by comment, Zemax coating name, material or type) to a coating definition.</summary>
    public sealed class CoatingRule
    {
        public string MatchComment, MatchCoating, MatchMaterial, MatchType;
        public JsonObject Coating;
        public JsonObject Lidt;
        public double? RoughnessRmsM;

        public bool Matches(SurfaceRow r) =>
            (MatchComment == null || string.Equals((r.Comment ?? "").Trim(), MatchComment, StringComparison.OrdinalIgnoreCase)) &&
            (MatchCoating == null || string.Equals((r.Coating ?? "").Trim(), MatchCoating, StringComparison.OrdinalIgnoreCase)) &&
            (MatchMaterial == null || string.Equals((r.Material ?? "").Trim(), MatchMaterial, StringComparison.OrdinalIgnoreCase)) &&
            (MatchType == null || (r.TypeName ?? "").IndexOf(MatchType, StringComparison.OrdinalIgnoreCase) >= 0) &&
            (MatchComment != null || MatchCoating != null || MatchMaterial != null || MatchType != null);

        public static CoatingRule FromJson(JsonObject o) => new CoatingRule
        {
            MatchComment = o.GetString("matchComment"), MatchCoating = o.GetString("matchCoating"), MatchMaterial = o.GetString("matchMaterial"), MatchType = o.GetString("matchType"),
            Coating = o.GetObject("coating"), Lidt = o.GetObject("lidt"), RoughnessRmsM = o.GetDoubleOrNull("roughnessRmsM")
        };
    }

    /// <summary>
    /// The export's "zernike" block: read OpticStudio's Zernike Standard Coefficients at the carrier and emit a "wavefront"
    /// element (docs/plugins/zemax.md#zernike-aberrations).
    /// <code>
    /// "zernike": { "field": 1, "maxTerm": 37, "sampling": 64, "position": "end", "radiusM": null, "opdSign": -1,
    ///              "removePiston": true, "removeTilt": false }
    /// </code>
    /// </summary>
    public sealed class ZernikeExport
    {
        /// <summary>Field number (1-based) whose wavefront is read.</summary>
        public int Field { get; set; } = 1;
        /// <summary>Highest Zernike Standard term read (Noll index).</summary>
        public int MaxTerm { get; set; } = 37;
        /// <summary>Pupil sampling of the fit: 32, 64, 128, 256, 512, 1024, 2048 points across.</summary>
        public int Sampling { get; set; } = 64;
        /// <summary>"end" (after every other element, default) or "start" (before them).</summary>
        public string Position { get; set; } = "end";
        /// <summary>Normalisation radius in the engine (m); default: OpticStudio's entrance pupil radius (ENPD / 2).</summary>
        public double? RadiusM { get; set; }
        /// <summary>Engine wavefront W (extra optical path, m) = OpdSign x OpticStudio OPD. -1 (default): OpticStudio's positive OPD is a
        /// wavefront ahead of the reference sphere (less optical path); confirmed by HARDWARE-CHECKLIST.md step 4b.</summary>
        public double OpdSign { get; set; } = -1;
        /// <summary>Drop Z1: piston relative to the chief ray would act as a pure group delay in the engine.</summary>
        public bool RemovePiston { get; set; } = true;
        /// <summary>Drop Z2 and Z3 (tilt relative to the chief ray: a pointing error).</summary>
        public bool RemoveTilt { get; set; }

        public static readonly int[] Samplings = { 32, 64, 128, 256, 512, 1024, 2048 };

        public static ZernikeExport FromJson(JsonObject o)
        {
            var z = new ZernikeExport
            {
                Field = o.GetInt("field", 1), MaxTerm = o.GetInt("maxTerm", 37), Sampling = o.GetInt("sampling", 64), Position = o.GetString("position", "end"),
                RadiusM = o.GetDoubleOrNull("radiusM"), OpdSign = o.GetDouble("opdSign", -1), RemovePiston = o.GetBool("removePiston", true), RemoveTilt = o.GetBool("removeTilt", false)
            };
            if (z.Field < 1) throw new JsonException("export zernike.field starts at 1.");
            if (z.MaxTerm < 1 || z.MaxTerm > 231) throw new JsonException("export zernike.maxTerm must be 1..231 (OpticStudio's Zernike Standard limit).");
            if (Array.IndexOf(Samplings, z.Sampling) < 0) throw new JsonException("export zernike.sampling must be one of " + string.Join(", ", Samplings) + ".");
            if (z.Position != "end" && z.Position != "start") throw new JsonException("export zernike.position must be \"end\" or \"start\".");
            if (z.OpdSign != 1 && z.OpdSign != -1) throw new JsonException("export zernike.opdSign must be +1 or -1.");
            if (z.RadiusM.HasValue && !(z.RadiusM.Value > 0)) throw new JsonException("export zernike.radiusM must be positive.");
            return z;
        }
    }

    /// <summary>
    /// Settings for exporting an OpticStudio system to a system file (<c>export.json</c>, docs/plugins/zemax.md):
    /// <code>
    /// { "format": "ultrafast-zemax-export", "name": "stretcher",
    ///   "pulse": { ...run-spec pulse... },                     // optional: sets carrier + auto sampling range
    ///   "sampling": { "mode": "auto" | "explicit" | "zemax", "minWavelengthM": 980e-9, "maxWavelengthM": 1080e-9,
    ///                 "count": 64, "batchSize": 24, "geometry": "operands" | "batchRayTrace", "carrierWavelengthM": 1.03e-6 },
    ///   "beam": { "waistRadiusM": 1e-3, "waistToSurface1M": 0, "m2": 1.1 },   // input beam for GBPS/GBPW
    ///   "media":    [ { "matchMaterial": "ARGON", "material": "Ar@1.5bar", "nonlinear": true,
    ///                   "definition": { "gas": { "species": "Ar", "pressureBar": 1.5 } } } ],
    ///   "coatings": [ { "matchComment": "MPC_MIRROR", "coating": { "kind": "gdd", "R": 0.999, "gddS2": -150e-30 } } ],
    ///   "coatingSource": "rules" | "zemaxOperand",  "coatingOperand": { ... },
    ///   "zernike": { "field": 1, "maxTerm": 37 },              // optional: OpticStudio's wavefront -> a "wavefront" element
    ///   "apertures": true, "vectors": true, "mergeLinear": true }
    /// </code>
    /// </summary>
    public sealed class ExportConfig
    {
        public string Name { get; set; }
        public PulseSpec Pulse { get; set; }
        public string SamplingMode { get; set; } = "auto";
        public double MinWavelengthM { get; set; } = double.NaN;
        public double MaxWavelengthM { get; set; } = double.NaN;
        public int SampleCount { get; set; } = 48;
        public int BatchSize { get; set; } = 24;
        public string Geometry { get; set; } = "operands";
        public double CarrierWavelengthM { get; set; } = double.NaN;
        /// <summary>Auto range: spectrum above this level (dB below peak) ...</summary>
        public double AutoThresholdDb { get; set; } = -40;
        /// <summary>... widened by this factor about its centre (room for nonlinear broadening).</summary>
        public double AutoMargin { get; set; } = 2.0;
        public double? BeamWaistM { get; set; }
        public double BeamWaistToSurface1M { get; set; }
        public double BeamM2 { get; set; } = 1.0;
        public List<MediumRule> Media { get; } = new List<MediumRule>();
        public List<CoatingRule> Coatings { get; } = new List<CoatingRule>();
        public string CoatingSource { get; set; } = "rules";
        public CoatingOperandSpec CoatingOperand { get; set; }
        public bool Apertures { get; set; } = true;
        public bool Vectors { get; set; } = true;
        public bool MergeLinear { get; set; } = true;
        public bool Vignetting { get; set; } = true;
        /// <summary>Null: no Zernike read.</summary>
        public ZernikeExport Zernike { get; set; }
        public JsonObject Raw { get; set; } = new JsonObject();

        public static ExportConfig Load(string path) => FromJson(JsonValue.ParseFile(path).AsObject(), Path.GetDirectoryName(Path.GetFullPath(path)));

        public static ExportConfig FromJson(JsonObject o, string baseDir = null)
        {
            var c = new ExportConfig { Raw = o, Name = o.GetString("name") };
            if (o.Has("pulse")) c.Pulse = PulseSpec.FromJson(o.GetObject("pulse"), baseDir, "export.pulse");
            var s = o.GetObject("sampling");
            if (s != null)
            {
                c.SamplingMode = s.GetString("mode", "auto");
                c.MinWavelengthM = s.GetDouble("minWavelengthM", double.NaN); c.MaxWavelengthM = s.GetDouble("maxWavelengthM", double.NaN);
                c.SampleCount = s.GetInt("count", 48); c.BatchSize = s.GetInt("batchSize", 24); c.Geometry = s.GetString("geometry", "operands");
                c.CarrierWavelengthM = s.GetDouble("carrierWavelengthM", double.NaN);
                c.AutoThresholdDb = s.GetDouble("thresholdDb", -40); c.AutoMargin = s.GetDouble("margin", 2.0);
            }
            var b = o.GetObject("beam");
            if (b != null) { c.BeamWaistM = b.GetDoubleOrNull("waistRadiusM"); c.BeamWaistToSurface1M = b.GetDouble("waistToSurface1M", 0); c.BeamM2 = b.GetDouble("m2", 1.0); }
            foreach (var m in o.GetArray("media") ?? new JsonArray()) c.Media.Add(MediumRule.FromJson(m.AsObject()));
            foreach (var m in o.GetArray("coatings") ?? new JsonArray()) c.Coatings.Add(CoatingRule.FromJson(m.AsObject()));
            c.CoatingSource = o.GetString("coatingSource", "rules");
            var co = o.GetObject("coatingOperand");
            if (c.CoatingSource == "zemaxOperand")
            {
                c.CoatingOperand = new CoatingOperandSpec();
                if (co != null)
                {
                    c.CoatingOperand.Type = co.GetString("type", "CODA");
                    c.CoatingOperand.DataArgument = co.GetString("dataArgument", "hx");
                    c.CoatingOperand.PhaseInDegrees = co.GetBool("phaseInDegrees", true);
                    var codes = co.GetObject("codes");
                    if (codes != null) foreach (var kv in codes) c.CoatingOperand.Codes[kv.Key] = kv.Value.AsInt();
                }
            }
            c.Apertures = o.GetBool("apertures", true); c.Vectors = o.GetBool("vectors", true);
            c.MergeLinear = o.GetBool("mergeLinear", true); c.Vignetting = o.GetBool("vignetting", true);
            var zk = o.Has("zernike") ? o["zernike"] : null;
            if (zk != null && !zk.IsNull && !(zk is JsonBool b0 && !b0.Value)) c.Zernike = zk is JsonBool ? new ZernikeExport() : ZernikeExport.FromJson(zk.AsObject());
            return c;
        }

        /// <summary>The sampler options this export asks for (lengths converted to the lens units of <paramref name="os"/>).</summary>
        public SamplerOptions ToSamplerOptions(IOpticStudio os, Action<string> log) => new SamplerOptions
        {
            WavelengthsUm = SampleWavelengthsUm(),
            KeepCurrentWavelengths = string.Equals(SamplingMode, "zemax", StringComparison.OrdinalIgnoreCase),
            BatchSize = BatchSize,
            GeometryFromOperands = !string.Equals(Geometry, "batchRayTrace", StringComparison.OrdinalIgnoreCase),
            CarrierUm = CarrierUm(),
            BeamWaist = BeamWaistM.HasValue ? BeamWaistM.Value / os.LensUnitToMeters : (double?)null,
            BeamWaistToSurface1 = BeamWaistToSurface1M / os.LensUnitToMeters,
            BeamM2 = BeamM2,
            CoatingOperand = CoatingOperand,
            Zernike = Zernike,
            Log = log
        };

        public double CarrierUm()
        {
            if (!double.IsNaN(CarrierWavelengthM)) return CarrierWavelengthM * 1e6;
            if (Pulse != null) return Pulse.ResolveCenterWavelengthM() * 1e6;
            throw new JsonException("export config: give sampling.carrierWavelengthM or a pulse.");
        }

        /// <summary>The sample wavelengths (um) for modes "auto" and "explicit" (null for "zemax" = use the WDE).</summary>
        public double[] SampleWavelengthsUm()
        {
            string mode = SamplingMode.ToLowerInvariant();
            if (mode == "zemax") return null;
            double lo, hi;
            if (mode == "explicit" || (!double.IsNaN(MinWavelengthM) && !double.IsNaN(MaxWavelengthM)))
            {
                lo = MinWavelengthM; hi = MaxWavelengthM;
                if (double.IsNaN(lo) || double.IsNaN(hi)) throw new JsonException("sampling mode explicit needs minWavelengthM and maxWavelengthM.");
            }
            else if (Pulse != null)
            {
                // span of the pulse spectrum above the threshold, widened by the margin (in frequency)
                var g = FrequencyGrid.FromWavelengthWindow(Pulse.ResolveCenterWavelengthM(), Math.Max(40 * (double.IsNaN(Pulse.ResolveDurationS()) ? 100e-15 : Pulse.ResolveDurationS()), 1e-12), 8192);
                var p = Pulse.Build(g);
                var spec = p.SpectralIntensity(); double pk = spec.Max(), thr = pk * Math.Pow(10, AutoThresholdDb / 10);
                int a = Array.FindIndex(spec, v => v >= thr), z = Array.FindLastIndex(spec, v => v >= thr);
                double wLo = g.OmegaAbsolute[a], wHi = g.OmegaAbsolute[z], wc = 0.5 * (wLo + wHi), half = Math.Max(0.5 * (wHi - wLo) * AutoMargin, 0.02 * g.OmegaCenter);
                double c = PhysicalConstants.SpeedOfLightMPerS;
                lo = 2 * Math.PI * c / (wc + half); hi = 2 * Math.PI * c / Math.Max(wc - half, 0.3 * g.OmegaCenter);
            }
            else
            {
                double l0 = CarrierUm() * 1e-6; lo = 0.95 * l0; hi = 1.05 * l0;
            }
            return Sampler.EvenlySpacedInFrequency(lo * 1e9, hi * 1e9, SampleCount);
        }
    }
}
