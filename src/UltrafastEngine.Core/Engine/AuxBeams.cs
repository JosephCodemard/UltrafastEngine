using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Engine
{
    /// <summary>
    /// An auxiliary beam that an active element makes the main beam interact with (an OPA pump, an SFG
    /// partner). It is a PORT with a pluggable source:
    /// <code>
    /// { "role": "pump",
    ///   "pulse": { ...same schema as the run pulse... }          // or
    ///   "from": "path/to/results", "energyJ": 1e-3,              // the output of another run (rescaled if energyJ)
    ///   "beam": { "kind": "gaussian" | "superGaussian" | "flatTop", "radiusM": 2e-3, "order": 6,
    ///             "radiusOfCurvatureM": 1e30, "decenterXM": 0 },
    ///   "delayS": 0 | "auto" }
    /// </code>
    /// delayS is relative to the main beam's co-moving time origin at the element entrance (positive = later);
    /// "auto" puts the aux pulse's centroid on the main pulse's centroid.
    /// </summary>
    public sealed class AuxBeamSpec
    {
        public string Role { get; set; } = "pump";
        public PulseSpec Pulse { get; set; }
        public string FromPath { get; set; }
        public double EnergyJ { get; set; } = double.NaN;
        public JsonObject Beam { get; set; }
        public double DelayS { get; set; }
        public bool DelayAuto { get; set; }
        public string Origin { get; set; }

        public static AuxBeamSpec FromJson(JsonObject o, string baseDirectory, string origin)
        {
            var a = new AuxBeamSpec { Role = o.GetString("role", "pump").ToLowerInvariant(), Origin = origin };
            if (o.Has("from"))
            {
                string p = o.GetString("from");
                if (!Path.IsPathRooted(p) && baseDirectory != null) p = Path.Combine(baseDirectory, p);
                a.FromPath = p;
                a.EnergyJ = o.GetDouble("energyJ", double.NaN);
            }
            else if (o.Has("pulse")) a.Pulse = PulseSpec.FromJson(o.GetObject("pulse"), baseDirectory, origin + ".pulse");
            else throw new JsonException(origin + ": an auxiliary beam needs \"pulse\" or \"from\".");
            a.Beam = o.GetObject("beam") ?? new JsonObject().Set("kind", "gaussian");
            if (o.TryGet("delayS", out var d))
            {
                if (d.Kind == JsonKind.String && string.Equals(d.AsString(), "auto", StringComparison.OrdinalIgnoreCase)) a.DelayAuto = true;
                else a.DelayS = d.AsDouble();
            }
            return a;
        }

        /// <summary>Nominal carrier (rad/s).</summary>
        public double CenterOmega()
        {
            if (Pulse != null) return 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / Pulse.ResolveCenterWavelengthM();
            var s = LoadSaved();
            return s.Grid.OmegaCenter;
        }

        /// <summary>Transform-limited duration estimate (s), for grid sizing.</summary>
        public double DurationEstimateS()
        {
            if (Pulse != null) { double d = Pulse.ResolveDurationS(); return double.IsNaN(d) ? 1e-12 : d; }
            var s = LoadSaved();
            return PulseMetrics.Summarize(s).FwhmS;
        }

        /// <summary>Approximate temporal extent including chirp (s), for grid sizing.</summary>
        public double ExtentEstimateS()
        {
            if (Pulse == null) { var s = LoadSaved(); var pw = PulseMetrics.TemporalPower(s); var (lo, hi) = PulseMetrics.SupportRange(pw, 1e-4); return (hi - lo) * s.Grid.DTime; }
            double tl = DurationEstimateS(), w0 = CenterOmega();
            double dw = 0.441 * 2 * Math.PI / tl; // FWHM spectral width
            double stretch = Math.Abs(Pulse.Gdd) * dw * 2 + Math.Abs(Pulse.Tod) * dw * dw;
            return 4 * tl + stretch;
        }

        public double RadiusXM => Beam.GetDouble("radiusXM", Beam.GetDouble("radiusM", Beam.GetDouble("w0M", double.NaN)));
        public double RadiusYM => Beam.GetDouble("radiusYM", RadiusXM);
        public double CurvatureRadiusM => Beam.GetDouble("radiusOfCurvatureM", double.PositiveInfinity);

        /// <summary>P / I_peak for this beam's shape (m^2): pi w^2/2 for a Gaussian, larger for flat-tops.</summary>
        public double EffectiveAreaM2()
        {
            string kind = Beam.GetString("kind", "gaussian").ToLowerInvariant();
            int order = kind == "flattop" ? 10 : kind == "supergaussian" ? Math.Max(1, Beam.GetInt("order", 4)) : 1;
            double wx = RadiusXM, wy = RadiusYM;
            if (double.IsNaN(wx)) throw new JsonException(Origin + ": the auxiliary beam needs beam.radiusM (1/e^2 radius).");
            // integral of exp(-2 (r/w)^(2N)) over the plane = pi w^2 Gamma(1 + 1/N) / 2^(1/N)
            double g = GammaFn(1 + 1.0 / order);
            return Math.PI * wx * wy * g / Math.Pow(2, 1.0 / order);
        }

        internal static double GammaFn(double x)
        {
            // Lanczos approximation (g = 7)
            double[] c = { 0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
            if (x < 0.5) return Math.PI / (Math.Sin(Math.PI * x) * GammaFn(1 - x));
            x -= 1; double a = c[0]; double t = x + 7.5;
            for (int i = 1; i < 9; i++) a += c[i] / (x + i);
            return Math.Sqrt(2 * Math.PI) * Math.Pow(t, x + 0.5) * Math.Exp(-t) * a;
        }

        private PulseField _saved;

        /// <summary>The output pulse of the referenced results folder.</summary>
        public PulseField LoadSaved()
        {
            if (_saved != null) return _saved;
            if (FromPath == null) throw new InvalidOperationException("not a 'from' beam");
            string dir = Directory.Exists(FromPath) ? FromPath : Path.GetDirectoryName(FromPath);
            string sumPath = Path.Combine(dir, "summary.json"), npz = Path.Combine(dir, "result.npz");
            if (!File.Exists(sumPath) || !File.Exists(npz)) throw new FileNotFoundException(Origin + ": \"from\" needs a results folder with summary.json and result.npz: " + dir);
            var sum = JsonValue.ParseFile(sumPath).AsObject();
            var g = sum.GetObject("outputGrid") ?? sum.GetObject("grid");
            var grid = FrequencyGrid.Create(g.GetInt("n", 0), g.GetDouble("dtS", 0), g.GetDouble("omega0", 0));
            var arrays = NpzWriter.Read(npz).ToDictionary(a => a.Name);
            if (!arrays.ContainsKey("output_ax_re")) throw new JsonException(Origin + ": " + npz + " has no full output spectrum (run it with outputs.maxPoints = 0).");
            Complex[] Get(string re, string im) { var r = arrays[re].Data; var i = arrays[im].Data; return r.Select((x, k) => new Complex(x, i[k])).ToArray(); }
            var ax = Get("output_ax_re", "output_ax_im");
            var ay = arrays.ContainsKey("output_ay_re") ? Get("output_ay_re", "output_ay_im") : new Complex[grid.N];
            if (ax.Length != grid.N) throw new JsonException(Origin + ": grid in summary.json (" + grid.N + ") does not match the saved spectrum (" + ax.Length + ").");
            _saved = new PulseField(grid, ax, ay);
            return _saved;
        }

        /// <summary>The aux pulse on <paramref name="grid"/> (whose carrier is this beam's carrier), delayed as specified.
        /// <paramref name="mainCentroidS"/> is the main pulse's temporal centroid (for "auto").</summary>
        public PulseField Build(FrequencyGrid grid, double mainCentroidS, List<string> notes)
        {
            PulseField p;
            if (Pulse != null) p = Pulse.Build(grid);
            else
            {
                var s = LoadSaved();
                p = GridResampler.Resample(s, grid, out double lost);
                if (lost > 1e-3) notes?.Add(string.Format(CultureInfo.InvariantCulture, "aux beam '{0}' from {1}: {2:P2} of its energy fell outside the element's grid", Role, FromPath, lost));
                if (!double.IsNaN(EnergyJ))
                {
                    double sc = Math.Sqrt(EnergyJ / p.TotalEnergyJ());
                    p = p.WithSpectrum(p.Ax.Select(z => z * sc).ToArray(), p.Ay.Select(z => z * sc).ToArray());
                }
            }
            double delay = DelayS;
            if (DelayAuto) delay = mainCentroidS - PulseMetrics.Moments(grid.Time, PulseMetrics.TemporalPower(p)).Mean;
            if (delay != 0)
            {
                var ax = new Complex[grid.N]; var ay = new Complex[grid.N];
                for (int i = 0; i < grid.N; i++) { var ph = Complex.FromPolarCoordinates(1, grid.OmegaOffset[i] * delay); ax[i] = p.Ax[i] * ph; ay[i] = p.Ay[i] * ph; }
                p = p.WithSpectrum(ax, ay);
            }
            return p;
        }

        public string Describe()
        {
            var ci = CultureInfo.InvariantCulture;
            string src = Pulse != null ? Pulse.Describe() + (double.IsNaN(Pulse.EnergyJ) ? "" : ", " + Pulse.EnergyJ.ToString("G4", ci) + " J") : "from " + FromPath + (double.IsNaN(EnergyJ) ? "" : ", rescaled to " + EnergyJ.ToString("G4", ci) + " J");
            return Role + ": " + src + ", beam " + Beam.GetString("kind", "gaussian") + " w = " + (RadiusXM * 1e3).ToString("G4", ci) + " mm, delay " + (DelayAuto ? "auto" : (DelayS * 1e15).ToString("G4", ci) + " fs");
        }
    }
}
