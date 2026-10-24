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
        /// <summary>Resolution (Gaussian FWHM, m) of the spectrometer that recorded the measured spectrum; 0 = unknown / ideal.</summary>
        public double SpectrumResolutionM { get; set; }
        /// <summary>Remove the recording resolution by Richardson-Lucy deconvolution before use.</summary>
        public bool SpectrumDeconvolve { get; set; }
        public int SpectrumDeconvolveIterations { get; set; } = 30;
        /// <summary>Outside the measured wavelengths: "zero" (no light there) or "gaussian" (Gaussian tails fitted to the data).</summary>
        public string SpectrumExtrapolate { get; set; } = "zero";
        /// <summary>Background of the measured spectrum: "none" (default), "auto" (median of the darker edge), "min", or
        /// "value" (then <see cref="SpectrumBackgroundValue"/> is subtracted).</summary>
        public string SpectrumBackground { get; set; } = "none";
        /// <summary>Background level subtracted when <see cref="SpectrumBackground"/> is "value" (intensity units).</summary>
        public double SpectrumBackgroundValue { get; set; }
        /// <summary>After background subtraction, values below this fraction of the peak are set to zero.</summary>
        public double SpectrumThreshold { get; set; }
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
                if (p.SpectrumWavelengthsM.Length != p.SpectrumIntensity.Length) throw new JsonException(origin + ".spectrum: wavelengths and intensity differ in length.");
                p.SpectrumResolutionM = sp.GetDouble("resolutionM", sp.Has("resolutionNm") ? sp.GetDouble("resolutionNm", 0) * 1e-9 : 0);
                p.SpectrumDeconvolve = sp.GetBool("deconvolve", false);
                p.SpectrumDeconvolveIterations = sp.GetInt("deconvolveIterations", 30);
                p.SpectrumExtrapolate = sp.GetString("extrapolate", "zero").ToLowerInvariant();
                if (sp.Has("background"))
                {
                    var bgv = sp["background"];
                    if (bgv.Kind == JsonKind.Number) { p.SpectrumBackground = "value"; p.SpectrumBackgroundValue = bgv.AsDouble(); }
                    else if (bgv.Kind == JsonKind.Bool) p.SpectrumBackground = bgv.AsBool() ? "auto" : "none";
                    else
                    {
                        p.SpectrumBackground = bgv.AsString().ToLowerInvariant();
                        if (p.SpectrumBackground != "auto" && p.SpectrumBackground != "min" && p.SpectrumBackground != "none")
                            throw new JsonException(origin + ".spectrum.background must be a number, \"auto\", \"min\" or \"none\".");
                    }
                }
                p.SpectrumThreshold = sp.GetDouble("threshold", 0);
                if (p.SpectrumThreshold < 0 || p.SpectrumThreshold >= 1) throw new JsonException(origin + ".spectrum.threshold is a fraction of the peak, 0 <= threshold < 1.");
                if (p.SpectrumExtrapolate != "zero" && p.SpectrumExtrapolate != "gaussian") throw new JsonException(origin + ".spectrum.extrapolate must be \"zero\" or \"gaussian\".");
                if (p.SpectrumDeconvolve && !(p.SpectrumResolutionM > 0)) throw new JsonException(origin + ".spectrum.deconvolve needs resolutionM (the recording spectrometer's resolution).");
                if (!o.Has("shape")) p.Shape = "spectrum";
                try { p.Prepared(); }
                catch (ArgumentException ex) { throw new JsonException(origin + ".spectrum: " + ex.Message.Replace("measured spectrum: ", "")); }
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

        private PreparedSpectrum _prepared; private object[] _preparedKey;

        /// <summary>The measured spectrum after cleaning (sorted, repeated wavelengths merged, non-finite rows dropped,
        /// background removed): see <see cref="MeasuredSpectrumPreparation"/>. Null when the pulse has no measured spectrum.</summary>
        public PreparedSpectrum Prepared()
        {
            if (SpectrumWavelengthsM == null || SpectrumIntensity == null) return null;
            var key = new object[] { SpectrumWavelengthsM, SpectrumIntensity, SpectrumPhaseRad, SpectrumBackground, SpectrumBackgroundValue, SpectrumThreshold };
            if (_prepared != null && _preparedKey != null && key.Zip(_preparedKey, (a, b) => Equals(a, b)).All(x => x)) return _prepared;
            _prepared = MeasuredSpectrumPreparation.Prepare(SpectrumWavelengthsM, SpectrumIntensity, SpectrumPhaseRad, SpectrumBackground, SpectrumBackgroundValue, SpectrumThreshold);
            _preparedKey = key;
            return _prepared;
        }

        /// <summary>Centre wavelength: explicit, or intensity-weighted mean of the measured spectrum.</summary>
        public double ResolveCenterWavelengthM()
        {
            if (!double.IsNaN(CenterWavelengthM)) return CenterWavelengthM;
            var pr = Prepared();
            if (pr != null)
            {
                double num = 0, den = 0;
                for (int i = 0; i < pr.WavelengthsM.Length; i++) { num += pr.WavelengthsM[i] * pr.Intensity[i]; den += pr.Intensity[i]; }
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
                var (lamM, intenM, phM) = MeasuredSpectrum();
                var order = Enumerable.Range(0, lamM.Length).OrderBy(i => -lamM[i]).ToArray();
                double c = PhysicalConstants.SpeedOfLightMPerS;
                var w = order.Select(i => 2 * Math.PI * c / lamM[i]).ToArray();
                // convert per-wavelength spectral density to per-frequency: S(w) = S(l) l^2 / (2 pi c)
                var s = order.Select(i => Math.Max(0, intenM[i]) * lamM[i] * lamM[i]).ToArray();
                var ph = phM != null ? order.Select(i => phM[i]).ToArray() : new double[w.Length];
                var li = new LinearInterpolator(w, s);
                IInterpolator pi = w.Length >= 4 ? (IInterpolator)new NaturalCubicSpline(w, ph) : new LinearInterpolator(w, ph);
                // Gaussian tails outside the measured range (extrapolate: gaussian): a parabola fitted to ln S(w) over the
                // part of the data above 1 % of the peak (exact for a truncated Gaussian), continued from each edge
                double qa = 0, qb = 0, qc = 0, gmRef = 0; bool tails = false;
                if (SpectrumExtrapolate == "gaussian")
                {
                    double smax = s.Max(), wm = w.Average(), ws = Math.Max(1e-300, (w[w.Length - 1] - w[0]) / 2);
                    var A = new double[3, 3]; var bb = new double[3];
                    for (int k = 0; k < w.Length; k++)
                    {
                        if (!(s[k] > 0.01 * smax)) continue;
                        double u = (w[k] - wm) / ws, wt = s[k] / smax, y = Math.Log(s[k] / smax);
                        double[] f = { 1, u, u * u };
                        for (int r = 0; r < 3; r++) { bb[r] += wt * f[r] * y; for (int q = 0; q < 3; q++) A[r, q] += wt * f[r] * f[q]; }
                    }
                    var sol = Solve3(A, bb);
                    if (sol != null && sol[2] < 0) { qa = sol[0]; qb = sol[1] / ws; qc = sol[2] / (ws * ws); tails = true; gmRef = wm; }
                }
                double Q(double x) => qb * (x - gmRef) + qc * (x - gmRef) * (x - gmRef);
                double Tail(double x, int edge) => s[edge] * Math.Exp(Q(x) - Q(w[edge]));
                for (int i = 0; i < n; i++)
                {
                    double x = grid.OmegaAbsolute[i];
                    if (x < w[0] || x > w[w.Length - 1])
                    {
                        if (!tails) continue;
                        int e = x < w[0] ? 0 : w.Length - 1;
                        a[i] = Complex.FromPolarCoordinates(Math.Sqrt(Math.Max(0, Tail(x, e))), ph[e]);
                        continue;
                    }
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
            if (double.IsNaN(e0) || double.IsInfinity(e0)) throw new InvalidOperationException("Input pulse energy is not a finite number (check the pulse values" + (shape == "spectrum" ? " and the measured spectrum" : "") + ").");
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

        private static double[] Solve3(double[,] A, double[] b)
        {
            int n = 3; var M = (double[,])A.Clone(); var x = (double[])b.Clone();
            for (int c = 0; c < n; c++)
            {
                int piv = c; for (int r = c + 1; r < n; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[piv, c])) piv = r;
                if (Math.Abs(M[piv, c]) < 1e-300) return null;
                if (piv != c) { for (int k = 0; k < n; k++) { var t = M[c, k]; M[c, k] = M[piv, k]; M[piv, k] = t; } var tb = x[c]; x[c] = x[piv]; x[piv] = tb; }
                for (int r = c + 1; r < n; r++) { double f = M[r, c] / M[c, c]; for (int k = c; k < n; k++) M[r, k] -= f * M[c, k]; x[r] -= f * x[c]; }
            }
            for (int r = n - 1; r >= 0; r--) { double s = x[r]; for (int k = r + 1; k < n; k++) s -= M[r, k] * x[k]; x[r] = s / M[r, r]; }
            return x;
        }

        /// <summary>The measured spectrum as used: deconvolved from the recording resolution when asked
        /// (Richardson-Lucy on a uniform wavelength grid), else as given.</summary>
        public (double[] WavelengthsM, double[] Intensity, double[] PhaseRad) MeasuredSpectrum()
        {
            var pr = Prepared();
            if (!SpectrumDeconvolve || !(SpectrumResolutionM > 0)) return (pr.WavelengthsM, pr.Intensity, pr.PhaseRad);
            var lx = pr.WavelengthsM; var ly = pr.Intensity.Select(v => Math.Max(0, v)).ToArray();
            double lo = lx[0], hi = lx[lx.Length - 1];
            double minStep = Enumerable.Range(1, lx.Length - 1).Select(i => lx[i] - lx[i - 1]).Where(d => d > 0).DefaultIfEmpty(SpectrumResolutionM / 5).Min();
            double step = Math.Min(minStep, SpectrumResolutionM / 5);
            int m = Math.Min(20000, (int)Math.Ceiling((hi - lo) / step) + 1); step = (hi - lo) / (m - 1);
            var x = Enumerable.Range(0, m).Select(i => lo + i * step).ToArray();
            var interp = new LinearInterpolator(lx, ly);
            var d0 = x.Select(v => Math.Max(0, interp.Evaluate(v))).ToArray();
            double sig = SpectrumResolutionM / (2 * Math.Sqrt(2 * Math.Log(2))) / step;
            int half = (int)Math.Ceiling(4 * sig);
            var k = Enumerable.Range(-half, 2 * half + 1).Select(j => Math.Exp(-0.5 * j * j / (sig * sig))).ToArray();
            double ks = k.Sum(); for (int j = 0; j < k.Length; j++) k[j] /= ks;
            double[] Conv(double[] a)
            {
                var r = new double[m];
                for (int i = 0; i < m; i++) { double v = 0, wsum = 0; for (int j = -half; j <= half; j++) { int q = i + j; if (q < 0 || q >= m) continue; v += k[j + half] * a[q]; wsum += k[j + half]; } r[i] = wsum > 0 ? v / wsum : 0; }
                return r;
            }
            var est = (double[])d0.Clone();
            double floor = 1e-12 * d0.Max();
            for (int it = 0; it < SpectrumDeconvolveIterations; it++)
            {
                var blur = Conv(est);
                var ratio = new double[m]; for (int i = 0; i < m; i++) ratio[i] = d0[i] / Math.Max(blur[i], floor);
                var corr = Conv(ratio);
                for (int i = 0; i < m; i++) est[i] *= corr[i];
            }
            double[] phase = null;
            if (pr.PhaseRad != null) { var pl = new LinearInterpolator(lx, pr.PhaseRad); phase = x.Select(pl.Evaluate).ToArray(); }
            return (x, est, phase);
        }

        public string Describe()
        {
            var ci = CultureInfo.InvariantCulture;
            string d = Shape + " @ " + (ResolveCenterWavelengthM() * 1e9).ToString("F2", ci) + " nm";
            if (Shape.ToLowerInvariant() != "spectrum") d += ", " + (ResolveDurationS() * 1e15).ToString("G4", ci) + " fs TL";
            else if (Prepared() != null)
            {
                var pr = Prepared();
                d += string.Format(ci, ", measured {0:F1}-{1:F1} nm ({2} points", pr.WavelengthsM[0] * 1e9, pr.WavelengthsM[pr.WavelengthsM.Length - 1] * 1e9, pr.WavelengthsM.Length);
                if (pr.Background != 0) d += string.Format(ci, ", background {0:G3} % removed", 100 * pr.BackgroundRelative);
                if (SpectrumResolutionM > 0) d += string.Format(ci, ", resolution {0:G3} nm{1}", SpectrumResolutionM * 1e9, SpectrumDeconvolve ? ", deconvolved" : "");
                d += pr.PhaseRad != null ? ", with phase" : ", flat phase";
                d += SpectrumExtrapolate == "gaussian" ? ", Gaussian tails outside)" : ", no light outside)";
            }
            if (Gdd != 0) d += ", GDD " + (Gdd * 1e30).ToString("G4", ci) + " fs^2";
            if (Tod != 0) d += ", TOD " + (Tod * 1e45).ToString("G4", ci) + " fs^3";
            d += ", pol " + PolarizationText;
            return d;
        }
    }
}
