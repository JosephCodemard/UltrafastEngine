using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Pulses
{
    /// <summary>
    /// The input pulse, as described in the run spec:
    /// <code>
    /// "pulse": {
    ///   "shape": "gaussian" | "sech2" | "superGaussian" | "spectrum",
    ///   "centerWavelengthM": 1.03e-6,
    ///   "durationFwhmS": 200e-15,            // intensity FWHM of the transform limit (or "bandwidthFwhmM")
    ///   "order": 2,                          // superGaussian only
    ///   "energyJ": 1e-6,                     // or "averagePowerW" + "repRateHz", or "peakPowerW"
    ///   "repRateHz": 1e5,
    ///   "chirp": {"gddS2": 0, "todS3": 0, "fodS4": 0},
    ///   "polarization": "x" | "y" | "45" | "-45" | "rcp" | "lcp" | {"angleDeg": 30, "ellipticity": 0} | [exRe, exIm, eyRe, eyIm],
    ///   "spectrum": {"wavelengthsM": [...], "intensity": [...], "phaseRad": [...]} | {"csv": "spectrum.csv"},
    ///   "delayS": 0
    /// }
    /// </code>
    /// </summary>
    public sealed class PulseSpec
    {
        public string Shape { get; set; } = "gaussian";
        public double CenterWavelengthM { get; set; } = double.NaN;
        public double DurationFwhmS { get; set; } = double.NaN;
        public double BandwidthFwhmM { get; set; } = double.NaN;
        public int SuperGaussianOrder { get; set; } = 2;
        public double EnergyJ { get; set; } = double.NaN;
        public double AveragePowerW { get; set; } = double.NaN;
        public double PeakPowerW { get; set; } = double.NaN;
        public double RepRateHz { get; set; }
        public double Gdd { get; set; }
        public double Tod { get; set; }
        public double Fod { get; set; }
        public JonesVector Polarization { get; set; } = JonesVector.LinearX;
        public double[] SpectrumWavelengthsM { get; set; }
        public double[] SpectrumIntensity { get; set; }
        public double[] SpectrumPhaseRad { get; set; }
        public double DelayS { get; set; }
        public string PolarizationText { get; set; } = "x";
        /// <summary>Pedestal / contrast components added to the pulse (docs/reference/run-spec.md#pedestal).</summary>
        public List<JsonObject> Pedestal { get; } = new List<JsonObject>();
        /// <summary>Quantum noise added to the input: {"kind": "onePhotonPerMode" | "none", "seed": n, "polarization": "same" | "both"}.</summary>
        public JsonObject Noise { get; set; }

        public static PulseSpec FromJson(JsonObject o, string baseDirectory, string origin = "pulse")
        {
            if (o == null) throw new JsonException("Run spec has no \"pulse\" section.");
            var p = new PulseSpec
            {
                Shape = o.GetString("shape", "gaussian"),
                CenterWavelengthM = o.GetDouble("centerWavelengthM", o.Has("centerWavelengthNm") ? o.GetDouble("centerWavelengthNm", 0) * 1e-9 : double.NaN),
                DurationFwhmS = o.GetDouble("durationFwhmS", o.Has("durationFwhmFs") ? o.GetDouble("durationFwhmFs", 0) * 1e-15 : double.NaN),
                BandwidthFwhmM = o.GetDouble("bandwidthFwhmM", o.Has("bandwidthFwhmNm") ? o.GetDouble("bandwidthFwhmNm", 0) * 1e-9 : double.NaN),
                SuperGaussianOrder = o.GetInt("order", 2),
                EnergyJ = o.GetDouble("energyJ", double.NaN),
                AveragePowerW = o.GetDouble("averagePowerW", double.NaN),
                PeakPowerW = o.GetDouble("peakPowerW", double.NaN),
                RepRateHz = o.GetDouble("repRateHz", 0),
                DelayS = o.GetDouble("delayS", 0)
            };
            var ch = o.GetObject("chirp");
            if (ch != null) { p.Gdd = ch.GetDouble("gddS2", ch.GetDouble("gddFs2", 0) * 1e-30); p.Tod = ch.GetDouble("todS3", ch.GetDouble("todFs3", 0) * 1e-45); p.Fod = ch.GetDouble("fodS4", ch.GetDouble("fodFs4", 0) * 1e-60); }
            if (o.Has("polarization")) { p.Polarization = ParsePolarization(o["polarization"], origin); p.PolarizationText = o["polarization"].ToJson(false); }
            if (o.TryGet("noise", out var nv) && !nv.IsNull)
            {
                p.Noise = nv.Kind == JsonKind.Object ? nv.AsObject() : nv.Kind == JsonKind.String ? new JsonObject().Set("kind", nv.AsString()) : null;
                string nk = p.Noise?.GetString("kind", "onePhotonPerMode").ToLowerInvariant();
                if (p.Noise != null && nk != "onephotonpermode" && nk != "none") throw new JsonException(origin + ".noise: unknown kind (onePhotonPerMode, none).");
            }
            if (o.TryGet("pedestal", out var pv) && !pv.IsNull)
            {
                if (pv.Kind == JsonKind.Array) foreach (var x in pv.AsArray()) p.Pedestal.Add(x.AsObject());
                else p.Pedestal.Add(pv.AsObject());
                foreach (var x in p.Pedestal)
                {
                    string k = x.GetString("kind", "").ToLowerInvariant();
                    if (k != "replica" && k != "prepulse" && k != "postpulse" && k != "ase" && k != "phasenoise" && k != "exponential")
                        throw new JsonException(origin + ".pedestal: unknown kind \"" + x.GetString("kind", "") + "\" (replica, prePulse, postPulse, ase, phaseNoise, exponential).");
                }
            }
            var sp = o.GetObject("spectrum");
            if (sp != null)
            {
                if (sp.Has("csv"))
                {
                    string path = sp.GetString("csv");
                    if (!Path.IsPathRooted(path) && baseDirectory != null) path = Path.Combine(baseDirectory, path);
                    LoadCsv(path, out var wl, out var inten, out var ph, sp.GetString("wavelengthUnit", "nm"));
                    p.SpectrumWavelengthsM = wl; p.SpectrumIntensity = inten; p.SpectrumPhaseRad = ph;
                }
                else
                {
                    p.SpectrumWavelengthsM = sp.GetDoubleArray("wavelengthsM") ?? sp.GetDoubleArray("wavelengthsNm")?.Select(x => x * 1e-9).ToArray();
                    p.SpectrumIntensity = sp.GetDoubleArray("intensity");
                    p.SpectrumPhaseRad = sp.GetDoubleArray("phaseRad");
                }
                if (p.SpectrumWavelengthsM == null || p.SpectrumIntensity == null) throw new JsonException(origin + ".spectrum needs wavelengths and intensity (or a csv).");
                if (!o.Has("shape")) p.Shape = "spectrum";
            }
            return p;
        }

        public static JonesVector ParsePolarization(JsonValue v, string origin)
        {
            if (v.Kind == JsonKind.String)
            {
                switch (v.AsString().Trim().ToLowerInvariant())
                {
                    case "x": case "linearx": case "h": case "horizontal": return JonesVector.LinearX;
                    case "y": case "lineary": case "v": case "vertical": return JonesVector.LinearY;
                    case "45": case "linear45": case "d": return JonesVector.Linear45;
                    case "-45": case "a": return new JonesVector(1 / Math.Sqrt(2), -1 / Math.Sqrt(2));
                    case "rcp": case "circularright": case "r": return JonesVector.CircularRight;
                    case "lcp": case "circularleft": case "l": return JonesVector.CircularLeft;
                    default: throw new JsonException(origin + ": unknown polarization \"" + v.AsString() + "\" (x, y, 45, -45, rcp, lcp, or an object/array).");
                }
            }
            if (v.Kind == JsonKind.Array)
            {
                var a = v.AsDoubleArray();
                if (a.Length != 4) throw new JsonException(origin + ": polarization array must be [exRe, exIm, eyRe, eyIm].");
                return new JonesVector(new Complex(a[0], a[1]), new Complex(a[2], a[3])).Normalized();
            }
            var o = v.AsObject();
            double ang = o.Has("angleDeg") ? o.GetDouble("angleDeg", 0) * Math.PI / 180 : o.GetDouble("angleRad", 0);
            double ell = o.GetDouble("ellipticity", 0); // tan(chi), -1..1 (+1 = right-circular in this convention)
            double chi = Math.Atan(ell);
            // Jones vector of an ellipse with azimuth ang and ellipticity angle chi
            var ex = new Complex(Math.Cos(ang) * Math.Cos(chi), Math.Sin(ang) * Math.Sin(chi));
            var ey = new Complex(Math.Sin(ang) * Math.Cos(chi), -Math.Cos(ang) * Math.Sin(chi));
            return new JonesVector(ex, ey).Normalized();
        }

        public static void LoadCsv(string path, out double[] wavelengthsM, out double[] intensity, out double[] phase, string unit = "nm")
        {
            double scale = unit == "m" ? 1 : unit == "um" ? 1e-6 : 1e-9;
            var w = new List<double>(); var I = new List<double>(); var ph = new List<double>(); bool anyPh = false;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { ',', ';', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) continue; // header
                if (parts.Length < 2) throw new FormatException(path + ": rows need at least wavelength and intensity.");
                w.Add(x * scale); I.Add(double.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture));
                if (parts.Length >= 3) { ph.Add(double.Parse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture)); anyPh = true; } else ph.Add(0);
            }
            if (w.Count == 0) throw new FormatException(path + ": no data rows.");
            wavelengthsM = w.ToArray(); intensity = I.ToArray(); phase = anyPh ? ph.ToArray() : null;
        }

        /// <summary>Centre wavelength: explicit, or intensity-weighted mean of the measured spectrum.</summary>
        public double ResolveCenterWavelengthM()
        {
            if (!double.IsNaN(CenterWavelengthM)) return CenterWavelengthM;
            if (SpectrumWavelengthsM != null)
            {
                double num = 0, den = 0;
                for (int i = 0; i < SpectrumWavelengthsM.Length; i++) { num += SpectrumWavelengthsM[i] * SpectrumIntensity[i]; den += SpectrumIntensity[i]; }
                if (den > 0) return num / den;
            }
            throw new JsonException("pulse: centerWavelengthM is required.");
        }

        /// <summary>Transform-limited intensity FWHM, from durationFwhmS or from bandwidthFwhmM (time-bandwidth product of the shape).</summary>
        public double ResolveDurationS()
        {
            if (!double.IsNaN(DurationFwhmS)) return DurationFwhmS;
            if (!double.IsNaN(BandwidthFwhmM))
            {
                double l = ResolveCenterWavelengthM(), c = PhysicalConstants.SpeedOfLightMPerS;
                double dnu = c * BandwidthFwhmM / (l * l);
                double tbp = Shape.ToLowerInvariant() == "sech2" ? 0.315 : 0.441;
                return tbp / dnu;
            }
            if (Shape.ToLowerInvariant() == "spectrum") return double.NaN;
            throw new JsonException("pulse: durationFwhmS (or bandwidthFwhmM) is required for shape \"" + Shape + "\".");
        }

        public double ResolveEnergyJ(double peakPowerFactor)
        {
            if (!double.IsNaN(EnergyJ)) return EnergyJ;
            if (!double.IsNaN(AveragePowerW) && RepRateHz > 0) return AveragePowerW / RepRateHz;
            if (!double.IsNaN(PeakPowerW)) return PeakPowerW * peakPowerFactor;
            throw new JsonException("pulse: give energyJ, or averagePowerW + repRateHz, or peakPowerW.");
        }

        /// <summary>Build the pulse on <paramref name="grid"/>.</summary>
        public PulseField Build(FrequencyGrid grid) => Build(grid, true);

        /// <summary>The pulse on a grid; <paramref name="withNoise"/> false leaves out the quantum noise (grid planning).</summary>
        public PulseField Build(FrequencyGrid grid, bool withNoise)
        {
            var pol = Polarization.Normalized();
            int n = grid.N;
            var a = new Complex[n];
            string shape = Shape.ToLowerInvariant();
            if (shape == "spectrum")
            {
                var order = Enumerable.Range(0, SpectrumWavelengthsM.Length).OrderBy(i => -SpectrumWavelengthsM[i]).ToArray();
                double c = PhysicalConstants.SpeedOfLightMPerS;
                var w = order.Select(i => 2 * Math.PI * c / SpectrumWavelengthsM[i]).ToArray();
                // convert per-wavelength spectral density to per-frequency: S(w) = S(l) l^2 / (2 pi c)
                var s = order.Select(i => Math.Max(0, SpectrumIntensity[i]) * SpectrumWavelengthsM[i] * SpectrumWavelengthsM[i]).ToArray();
                var ph = SpectrumPhaseRad != null ? order.Select(i => SpectrumPhaseRad[i]).ToArray() : new double[w.Length];
                var li = new LinearInterpolator(w, s);
                IInterpolator pi = w.Length >= 4 ? (IInterpolator)new NaturalCubicSpline(w, ph) : new LinearInterpolator(w, ph);
                for (int i = 0; i < n; i++)
                {
                    double x = grid.OmegaAbsolute[i];
                    if (x < w[0] || x > w[w.Length - 1]) continue;
                    a[i] = Complex.FromPolarCoordinates(Math.Sqrt(Math.Max(0, li.Evaluate(x))), pi.Evaluate(x));
                }
            }
            else
            {
                double fwhm = ResolveDurationS();
                var at = new Complex[n];
                for (int i = 0; i < n; i++)
                {
                    double t = grid.Time[i] - DelayS;
                    double env;
                    switch (shape)
                    {
                        case "gaussian": { double s = fwhm / (2 * Math.Sqrt(2 * Math.Log(2))); env = Math.Exp(-t * t / (4 * s * s)); break; }
                        case "sech2": { double t0 = fwhm / (2 * Math.Log(1 + Math.Sqrt(2))); env = 1 / Math.Cosh(t / t0); break; }
                        case "supergaussian":
                        {
                            int m = Math.Max(1, SuperGaussianOrder);
                            double t0 = fwhm / 2 / Math.Pow(Math.Log(2), 1.0 / (2 * m));
                            env = Math.Exp(-0.5 * Math.Pow(Math.Abs(t / t0), 2 * m));
                            break;
                        }
                        default: throw new JsonException("pulse: unknown shape \"" + Shape + "\" (gaussian, sech2, superGaussian, spectrum).");
                    }
                    at[i] = env;
                }
                a = SpectralTransform.TimeToFreq(at);
            }
            // chirp (spectral phase about the carrier)
            if (Gdd != 0 || Tod != 0 || Fod != 0)
                for (int i = 0; i < n; i++)
                {
                    double d = grid.OmegaOffset[i];
                    a[i] *= Complex.FromPolarCoordinates(1, Gdd * d * d / 2 + Tod * d * d * d / 6 + Fod * d * d * d * d / 24);
                }
            if (shape == "spectrum" && DelayS != 0)
                for (int i = 0; i < n; i++) a[i] *= Complex.FromPolarCoordinates(1, grid.OmegaOffset[i] * DelayS);

            var ax = new Complex[n]; var ay = new Complex[n];
            for (int i = 0; i < n; i++) { ax[i] = a[i] * pol.Ex; ay[i] = a[i] * pol.Ey; }
            var pulse = new PulseField(grid, ax, ay, RepRateHz);
            double e0 = pulse.TotalEnergyJ();
            if (e0 <= 0) throw new InvalidOperationException("Input pulse has zero energy on this grid (is the spectrum inside the grid's frequency range?).");
            double target;
            if (double.IsNaN(EnergyJ) && double.IsNaN(AveragePowerW) && !double.IsNaN(PeakPowerW))
                target = PeakPowerW / pulse.PeakPowerW() * e0;
            else target = ResolveEnergyJ(1.0);
            double sc = Math.Sqrt(target / e0);
            for (int i = 0; i < n; i++) { ax[i] *= sc; ay[i] *= sc; }
            var built = new PulseField(grid, ax, ay, RepRateHz);
            if (Pedestal.Count > 0) built = Pedestals.Apply(built, Pedestal, pol);
            return Noise != null && withNoise ? Pedestals.QuantumNoise(built, Noise) : built;
        }

        /// <summary>Half-window the pulse's satellites need (largest replica delay, a few decay times of an exponential pedestal).</summary>
        public double SatelliteHalfWindowS()
        {
            double h = 0;
            foreach (var x in Pedestal)
            {
                string k = x.GetString("kind", "").ToLowerInvariant();
                if (k == "replica" || k == "prepulse" || k == "postpulse") h = Math.Max(h, Math.Abs(x.GetDouble("delayS", 0)));
                if (k == "exponential") h = Math.Max(h, 3 * Math.Max(x.GetDouble("riseS", 0), x.GetDouble("fallS", 0)));
                if (k == "phasenoise")
                {
                    // scattered light spreads over ~ 2 pi / (correlation width): keep a few of those in the window
                    double corr = x.GetDouble("correlationRadPerS", double.NaN);
                    if (double.IsNaN(corr) && x.Has("correlationNm")) { double l = ResolveCenterWavelengthM(); corr = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS * x.GetDouble("correlationNm", 0) * 1e-9 / (l * l); }
                    if (corr > 0) h = Math.Max(h, 3 * 2 * Math.PI / corr);
                }
            }
            return h;
        }

        public string Describe()
        {
            var ci = CultureInfo.InvariantCulture;
            string d = Shape + " @ " + (ResolveCenterWavelengthM() * 1e9).ToString("F2", ci) + " nm";
            if (Shape.ToLowerInvariant() != "spectrum") d += ", " + (ResolveDurationS() * 1e15).ToString("G4", ci) + " fs TL";
            if (Gdd != 0) d += ", GDD " + (Gdd * 1e30).ToString("G4", ci) + " fs^2";
            if (Tod != 0) d += ", TOD " + (Tod * 1e45).ToString("G4", ci) + " fs^3";
            d += ", pol " + PolarizationText;
            return d;
        }
    }
}
