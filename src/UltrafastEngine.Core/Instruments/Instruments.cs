using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Instruments
{
    /// <summary>What an instrument sees: the full pulse at its location plus the run for context.</summary>
    public sealed class InstrumentContext
    {
        public PulseField Pulse { get; set; }
        /// <summary>With a delayed-light tail: the main field alone on the same (long) grid as <see cref="Pulse"/> (= main + tail).
        /// Instruments that gate with a nonlinear process (the cross-correlator's SHG gate) use it; null otherwise.</summary>
        public PulseField GatePulse { get; set; }
        public string Location { get; set; }
        public RunResult Run { get; set; }
        public JsonObject Spec { get; set; }
        public ModelRegistry Registry { get; set; }
    }

    /// <summary>An instrument's readings: scalar values and arrays (exported to summary.json, result.npz and CSV).</summary>
    public sealed class InstrumentOutput
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Location { get; set; }
        public JsonObject Values { get; } = new JsonObject();
        public List<NdArray> Arrays { get; } = new List<NdArray>();
        public List<string> Notes { get; } = new List<string>();
        /// <summary>The device parameters in effect (given or default): written as "settings" in summary.json.</summary>
        public JsonObject Settings { get; set; }

        public void Add(string suffix, double[] data, string unit, string description, params int[] shape)
        {
            Arrays.Add(new NdArray(RunResult.Sanitize(Name) + "_" + suffix, data, shape) { Unit = unit, Description = Kind + ": " + description });
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject().Set("name", Name).Set("kind", Kind).Set("at", Location).Set("values", Values)
                .Set("arrays", JsonValue.FromArray(Arrays.Select(a => a.Name)));
            if (Settings != null) o.Set("settings", Settings);
            if (Notes.Count > 0) o.Set("notes", JsonValue.FromArray(Notes));
            return o;
        }

        /// <summary>1D arrays sharing a length go into one CSV; 2D arrays get their own file.</summary>
        public void WriteCsv(string dir)
        {
            var ci = CultureInfo.InvariantCulture;
            var oneD = Arrays.Where(a => a.Shape.Length == 1).GroupBy(a => a.Data.Length);
            int k = 0;
            foreach (var grp in oneD)
            {
                var cols = grp.ToList();
                var sb = new StringBuilder();
                sb.Append(string.Join(",", cols.Select(c => c.Name))).Append('\n');
                for (int i = 0; i < cols[0].Data.Length; i++)
                    sb.Append(string.Join(",", cols.Select(c => double.IsNaN(c.Data[i]) ? "" : c.Data[i].ToString("G9", ci)))).Append('\n');
                File.WriteAllText(Path.Combine(dir, RunResult.Sanitize(Name) + (k++ == 0 ? "" : "_" + k) + ".csv"), sb.ToString());
            }
            foreach (var a in Arrays.Where(a => a.Shape.Length == 2))
            {
                var sb = new StringBuilder();
                for (int r = 0; r < a.Rows; r++)
                {
                    for (int c = 0; c < a.Cols; c++) { if (c > 0) sb.Append(','); sb.Append(a.Data[r * a.Cols + c].ToString("G6", ci)); }
                    sb.Append('\n');
                }
                File.WriteAllText(Path.Combine(dir, a.Name + ".csv"), sb.ToString());
            }
        }
    }

    /// <summary>
    /// A virtual measurement device. Instruments never change the pulse; they read it at a location ("at": an
    /// element id, "input" or "end") and report what the real device would show, including its resolution.
    /// Add your own by implementing this and calling <see cref="InstrumentFactory.Register"/>.
    /// </summary>
    public interface IInstrument
    {
        string Kind { get; }
        void Measure(InstrumentContext ctx, InstrumentOutput output);
    }

    public static class InstrumentFactory
    {
        private static readonly Dictionary<string, Func<JsonObject, IInstrument>> Kinds = new Dictionary<string, Func<JsonObject, IInstrument>>(StringComparer.OrdinalIgnoreCase)
        {
            { "energyMeter", s => new EnergyMeter() },
            { "powerMeter", s => new EnergyMeter() },
            { "spectrometer", s => new Spectrometer() },
            { "scope", s => new Scope() },
            { "photodiode", s => new Scope() },
            { "autocorrelator", s => new Autocorrelator() },
            { "frog", s => new Frog() },
            { "xfrog", s => new Frog("xfrog") },
            { "sfgFrog", s => new Frog("xfrog") },
            { "dscan", s => new DScan() },
            { "contrast", s => new ContrastMeter() },
            { "crossCorrelator", s => new CrossCorrelator() },
            { "thirdOrderCorrelator", s => new CrossCorrelator() },
            { "polarimeter", s => new Polarimeter() },
            { "pulseSummary", s => new PulseSummaryInstrument() },
            { "compressor", s => new IdealCompressor() },
            { "conversion", s => new ConversionMeter() },
            { "phaseMatchingCurve", s => new PhaseMatchingCurve() },
            { "delayScan", s => new DelayScan() },
            { "beamProfile", s => new BeamProfileInstrument() },
            { "camera", s => new Camera("near") },
            { "nearFieldCamera", s => new Camera("near") },
            { "farFieldCamera", s => new Camera("far") },
            { "wavefrontSensor", s => new WavefrontSensor() },
            { "wigner", s => new WignerInstrument() },
            { "fieldProbe", s => new FieldProbe() },
            { "spaceTime", s => new SpaceTime() },
        };

        public static IEnumerable<string> Known => Kinds.Keys.OrderBy(k => k);

        public static void Register(string kind, Func<JsonObject, IInstrument> factory) { lock (Kinds) Kinds[kind] = factory; }

        public static IInstrument Create(JsonObject spec)
        {
            string kind = spec.GetString("kind") ?? throw new JsonException("instrument needs a \"kind\".");
            lock (Kinds)
                if (Kinds.TryGetValue(kind, out var f)) return f(spec);
            throw new JsonException("unknown instrument kind \"" + kind + "\" (known: " + string.Join(", ", Known) + ").");
        }
    }

    // ======================================================================================== helpers

    internal static class Tools
    {
        public static readonly double C = PhysicalConstants.SpeedOfLightMPerS;

        /// <summary>A smaller grid holding the pulse: cropped to its temporal support (with padding) and with a time
        /// step resolving its spectral support. The field is interpolated (complex, linear) onto the new grid.</summary>
        public static PulseField Compact(PulseField p, int maxN, out string note, double padFactor = 1.6)
        {
            note = null;
            var g = p.Grid;
            var pt = PulseMetrics.TemporalPower(p);
            var sp = p.SpectralIntensity();
            var (tl, th) = PulseMetrics.SupportRange(pt, 1e-7, 4);
            var (sl, sh) = PulseMetrics.SupportRange(sp, 1e-7, 4);
            double t0 = 0.5 * (g.Time[tl] + g.Time[th]);
            double span = Math.Max((g.Time[th] - g.Time[tl]) * padFactor, 32 * g.DTime);
            double dwSpan = (sh - sl + 1) * g.DOmega;
            double dt = Math.Min(g.DTime * Math.Max(1, Math.Floor(2 * Math.PI / (1.6 * dwSpan) / g.DTime)), 2 * Math.PI / (1.6 * dwSpan));
            dt = Math.Max(dt, g.DTime);
            double fw = PulseMetrics.Fwhm(g.Time, pt);
            if (!double.IsNaN(fw) && fw / 40 > g.DTime) dt = Math.Min(dt, fw / 40); // keep traces resolved in time
            else if (!double.IsNaN(fw)) dt = g.DTime;
            int n = GridPow2(span / dt);
            if (n > maxN) { n = maxN; dt = span / n; note = "trace grid limited to " + maxN + " points: pulse too long/broad to resolve fully"; }
            var ng = FrequencyGrid.Create(n, dt, g.OmegaCenter);
            var ax = p.TemporalAx(); var ay = p.TemporalAy();
            // spectral centroid offset -> keep carrier
            var nx = new Complex[n]; var ny = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                double t = ng.Time[i] + t0;
                double f = (t - g.Time[0]) / g.DTime;
                int k = (int)Math.Floor(f);
                if (k < 0 || k >= g.N - 1) continue;
                double u = f - k;
                nx[i] = ax[k] * (1 - u) + ax[k + 1] * u;
                ny[i] = ay[k] * (1 - u) + ay[k + 1] * u;
            }
            double scale = Math.Sqrt(p.TotalEnergyJ() / Math.Max(1e-300, nx.Concat(ny).Sum(c => c.Magnitude * c.Magnitude) * dt));
            for (int i = 0; i < n; i++) { nx[i] *= scale; ny[i] *= scale; }
            return new PulseField(ng, SpectralTransform.TimeToFreq(nx), SpectralTransform.TimeToFreq(ny), p.RepRateHz);
        }

        public static int GridPow2(double v) { int p = 16; while (p < v && p < (1 << 24)) p <<= 1; return p; }

        public static double[] GaussianSmooth(double[] y, double dx, double fwhm)
        {
            if (fwhm <= 0 || dx <= 0) return (double[])y.Clone();
            double s = fwhm / (2 * Math.Sqrt(2 * Math.Log(2))) / Math.Abs(dx);
            int h = (int)Math.Ceiling(4 * s);
            if (h < 1) return (double[])y.Clone();
            if (h > 32)
            {
                // FFT convolution (zero-padded, normalised at the edges like the direct sum)
                int n = y.Length, m = 1; while (m < n + 2 * h) m <<= 1;
                var a = new Complex[m]; var wgt = new Complex[m];
                for (int i = 0; i < n; i++) { a[i] = y[i]; wgt[i] = 1; }
                var fa = ComplexFft.Forward(a); var fw = ComplexFft.Forward(wgt);
                for (int q = 0; q < m; q++)
                {
                    int kk = q <= m / 2 ? q : q - m;
                    double g = Math.Exp(-2 * Math.PI * Math.PI * s * s * kk * (double)kk / ((double)m * m));
                    fa[q] *= g; fw[q] *= g;
                }
                var ra = ComplexFft.Inverse(fa); var rw = ComplexFft.Inverse(fw);
                var outp = new double[n];
                for (int i = 0; i < n; i++) outp[i] = rw[i].Real > 1e-12 ? ra[i].Real / rw[i].Real : 0;
                return outp;
            }
            var k = new double[2 * h + 1]; double ks = 0;
            for (int i = -h; i <= h; i++) { k[i + h] = Math.Exp(-0.5 * i * i / (s * s)); ks += k[i + h]; }
            var r = new double[y.Length];
            for (int i = 0; i < y.Length; i++)
            {
                double acc = 0, w = 0;
                for (int j = -h; j <= h; j++) { int q = i + j; if (q < 0 || q >= y.Length) continue; acc += k[j + h] * y[q]; w += k[j + h]; }
                r[i] = w > 0 ? acc / w : 0;
            }
            return r;
        }

        public static double[] Linspace(double a, double b, int n) { var r = new double[n]; for (int i = 0; i < n; i++) r[i] = n == 1 ? a : a + (b - a) * i / (n - 1); return r; }

        public static double Interp(double[] x, double[] y, double v)
        {
            if (v <= x[0]) return y[0]; if (v >= x[x.Length - 1]) return y[y.Length - 1];
            int i = Array.BinarySearch(x, v); if (i >= 0) return y[i]; i = ~i;
            return y[i - 1] + (v - x[i - 1]) / (x[i] - x[i - 1]) * (y[i] - y[i - 1]);
        }

        /// <summary>"normalize": "peak" | "area" | "none", or the legacy true (peak) / false (none).</summary>
        public static string NormalizeMode(JsonObject s, string fallback)
        {
            if (!s.TryGet("normalize", out var v)) return fallback;
            if (v.Kind == JsonKind.Bool) return v.AsBool() ? "peak" : "none";
            string m = v.AsString().Trim().ToLowerInvariant();
            if (m == "max" || m == "true") return "peak";
            if (m == "integral" || m == "sum") return "area";
            if (m == "false" || m == "off" || m == "raw") return "none";
            if (m != "peak" && m != "area" && m != "none") throw new JsonException("spectrometer \"normalize\": \"" + m + "\" (use \"peak\", \"area\" or \"none\")");
            return m;
        }

        public static void Put(InstrumentOutput o, string key, double v) => o.Values.Set(key, double.IsNaN(v) || double.IsInfinity(v) ? JsonValue.Null : (JsonValue)v);
    }

    // ======================================================================================== instruments

    /// <summary>Energy / power meter. Options: "rangeM": [min, max] spectral window, "polarizerDeg": analyser angle.</summary>
    public sealed class EnergyMeter : IInstrument
    {
        public string Kind => "energyMeter";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var p = ctx.Pulse; var g = p.Grid; var s = ctx.Spec;
            var range = s.GetDoubleArray("rangeM");
            double? pol = s.Has("polarizerDeg") ? s.GetDouble("polarizerDeg", 0) * Math.PI / 180 : (double?)null;
            double sum = 0;
            // the band [range[0], range[1]] in angular frequency; each bin covers [w - dw/2, w + dw/2] and counts with the fraction
            // of that interval inside the band, so the edges are not snapped to whole bins (error falls as dw^2)
            double wBandLo = 0, wBandHi = double.PositiveInfinity;
            if (range != null)
            {
                wBandLo = range[1] > 0 ? 2 * Math.PI * Tools.C / range[1] : 0;
                wBandHi = range[0] > 0 ? 2 * Math.PI * Tools.C / range[0] : double.PositiveInfinity;
            }
            for (int i = 0; i < g.N; i++)
            {
                double w = g.OmegaAbsolute[i];
                double weight = 1.0;
                if (range != null)
                {
                    double hw = 0.5 * g.DOmega;
                    double ov = Math.Min(w + hw, wBandHi) - Math.Max(w - hw, wBandLo);
                    weight = Math.Max(0, Math.Min(1, ov / g.DOmega));
                    if (weight <= 0) continue;
                }
                if (pol.HasValue)
                {
                    var a = p.Ax[i] * Math.Cos(pol.Value) + p.Ay[i] * Math.Sin(pol.Value);
                    sum += weight * (a.Real * a.Real + a.Imaginary * a.Imaginary);
                }
                else sum += weight * (p.Ax[i].Real * p.Ax[i].Real + p.Ax[i].Imaginary * p.Ax[i].Imaginary + p.Ay[i].Real * p.Ay[i].Real + p.Ay[i].Imaginary * p.Ay[i].Imaginary);
            }
            double e = sum / g.N * g.DTime;
            Tools.Put(o, "energyJ", e);
            double rep = s.GetDouble("repRateHz", p.RepRateHz);
            if (rep > 0) Tools.Put(o, "averagePowerW", e * rep);
            double ein = ctx.Run.Input.TotalEnergyJ();
            Tools.Put(o, "transmissionFromInput", ein > 0 ? e / ein : double.NaN);
        }
    }

    /// <summary>Grating spectrometer: Gaussian instrument function ("resolutionM", FWHM), wavelength window
    /// ("rangeM"), "points", optional "polarizerDeg". Reports peak, centroid, FWHM, RMS and -10 dB widths.</summary>
    public sealed class Spectrometer : IInstrument
    {
        public string Kind => "spectrometer";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var p = ctx.Pulse; var g = p.Grid; var s = ctx.Spec;
            int pts = s.GetInt("points", 2048);
            double res = s.GetDouble("resolutionM", 0);
            var sp = p.SpectralIntensity();
            if (s.Has("polarizerDeg"))
            {
                double a = s.GetDouble("polarizerDeg", 0) * Math.PI / 180;
                for (int i = 0; i < g.N; i++) { var f = p.Ax[i] * Math.Cos(a) + p.Ay[i] * Math.Sin(a); sp[i] = f.Real * f.Real + f.Imaginary * f.Imaginary; }
            }
            var (sl, sh) = PulseMetrics.SupportRange(sp, 1e-6, 8);
            var range = s.GetDoubleArray("rangeM");
            double lmin = range != null ? range[0] : 2 * Math.PI * Tools.C / g.OmegaAbsolute[sh];
            double lmax = range != null ? range[1] : 2 * Math.PI * Tools.C / Math.Max(1e-300, g.OmegaAbsolute[sl]);
            if (range == null) { double pad = 0.1 * (lmax - lmin) + 3 * res; lmin -= pad; lmax += pad; }
            lmin = Math.Max(lmin, 1e-9);
            var lam = Tools.Linspace(lmin, lmax, pts);
            // per-wavelength density: S(l) = S(w) * 2 pi c / l^2, interpolated in omega
            var w = g.OmegaAbsolute;
            var y = new double[pts];
            for (int i = 0; i < pts; i++)
            {
                double wi = 2 * Math.PI * Tools.C / lam[i];
                double v = Tools.Interp(w, sp, wi);
                y[i] = v * 2 * Math.PI * Tools.C / (lam[i] * lam[i]);
            }
            // frequency bins narrower than the wavelength sampling are not resolved by point interpolation -> smooth by bin width too
            double dl = lam[1] - lam[0];
            y = Tools.GaussianSmooth(y, dl, Math.Max(res, 0));
            double mx = y.Max();
            // wavelength calibration error of the instrument (the reported axis is shifted)
            double lamOffset = s.GetDouble("wavelengthOffsetM", 0);
            if (lamOffset != 0) for (int i = 0; i < pts; i++) lam[i] += lamOffset;
            string norm = Tools.NormalizeMode(s, "peak");
            double area = 0; for (int i = 1; i < pts; i++) area += 0.5 * (y[i] + y[i - 1]) * (lam[i] - lam[i - 1]);
            double energy = 0; for (int i = 0; i < g.N; i++) energy += sp[i]; energy *= g.DTime / g.N;
            double scale = norm == "peak" ? (mx > 0 ? 1 / mx : 1) : norm == "area" ? (area > 0 ? 1 / area : 1) : (area > 0 ? energy / area : 1);
            var yn = y.Select(v => v * scale).ToArray();
            o.Add("wavelength_m", lam, "m", "wavelength axis");
            o.Add("spectrum", yn, norm == "peak" ? "norm." : norm == "area" ? "1/m" : "J/m",
                "measured spectrum per unit wavelength (" + (norm == "peak" ? "peak = 1" : norm == "area" ? "area = 1" : "spectral energy density") + ")");
            if (s.GetBool("db", false)) o.Add("spectrum_db", y.Select(v => 10 * Math.Log10(Math.Max(v / Math.Max(mx, 1e-300), 1e-30))).ToArray(), "dB", "measured spectrum relative to its peak, dB");
            o.Values.Set("normalize", norm);
            Tools.Put(o, "energyJ", energy);
            Tools.Put(o, "peakWavelengthM", lam[Array.IndexOf(y, mx)]);
            var (m, rms) = PulseMetrics.Moments(lam, y);
            Tools.Put(o, "centroidWavelengthM", m);
            Tools.Put(o, "rmsWidthM", rms);
            Tools.Put(o, "fwhmM", PulseMetrics.Fwhm(lam, y));
            double thr = 0.1 * mx; int a0 = Array.FindIndex(y, v => v >= thr), a1 = Array.FindLastIndex(y, v => v >= thr);
            Tools.Put(o, "width10dBM", a0 >= 0 ? lam[a1] - lam[a0] : double.NaN);
            Tools.Put(o, "resolutionM", res);
        }
    }

    /// <summary>Photodiode + oscilloscope: temporal power convolved with a Gaussian impulse response
    /// ("riseTimeS" 10-90 %, or "bandwidthHz"). Useful for stretched pulses (ns) and pre/post pulses.</summary>
    public sealed class Scope : IInstrument
    {
        public string Kind => "scope";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var p = ctx.Pulse; var g = p.Grid; var s = ctx.Spec;
            double rise = s.Has("bandwidthHz") ? 0.35 / s.GetDouble("bandwidthHz", 1e9) : s.GetDouble("riseTimeS", 0);
            double fwhm = rise / 1.0 * (2 * Math.Sqrt(2 * Math.Log(2))) / 2.563; // Gaussian: 10-90 % rise = 2.563 sigma
            var pt = PulseMetrics.TemporalPower(p);
            var y = Tools.GaussianSmooth(pt, g.DTime, fwhm);
            var (lo, hi) = PulseMetrics.SupportRange(y, 1e-6, 8);
            int pts = s.GetInt("points", 4096);
            var t = Executor_Crop(g.Time, lo, hi, pts); var v = Executor_Crop(y, lo, hi, pts);
            o.Add("time_s", t, "s", "time"); o.Add("signal_w", v, "W", "detected power (impulse-response limited)");
            Tools.Put(o, "fwhmS", PulseMetrics.Fwhm(t, v));
            Tools.Put(o, "peakW", v.Max());
            Tools.Put(o, "riseTimeS", rise);
        }
        internal static double[] Executor_Crop(double[] a, int lo, int hi, int max) => Engine.Executor.Crop(a, lo, hi, max);
    }

    /// <summary>Autocorrelator: "type": "intensity" (background-free SHG, default) or "interferometric" (fringe-resolved).
    /// Reports the AC FWHM and the deconvolved pulse FWHM for Gaussian (1/sqrt2) and sech^2 (1/1.543) shapes.</summary>
    public sealed class Autocorrelator : IInstrument
    {
        public string Kind => "autocorrelator";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var s = ctx.Spec;
            string type = s.GetString("type", "intensity").ToLowerInvariant();
            var p = Tools.Compact(ctx.Pulse, s.GetInt("maxN", 1 << 16), out var note, 2.2);
            if (note != null) o.Notes.Add(note);
            var g = p.Grid; int n = g.N;
            var pt = PulseMetrics.TemporalPower(p);
            double accM = s.GetDouble("shgAcceptanceFwhmM", 0), resS = s.GetDouble("resolutionS", 0);
            if (!type.StartsWith("interf") && type != "iac" && type != "frac" && accM > 0)
            {
                // finite SHG bandwidth: integrate the SHG-FROG signal over the crystal's acceptance window
                int m = Math.Min(Tools.GridPow2(Math.Max(64, s.GetInt("acceptancePoints", 512))), 1024);
                var ax = p.TemporalAx();
                double win = g.N * g.DTime, dtm = win / m;
                var gm = FrequencyGrid.Create(m, dtm, g.OmegaCenter);
                var e = new Complex[m];
                for (int i = 0; i < m; i++) { double f = (gm.Time[i] - g.Time[0]) / g.DTime; int k = (int)Math.Floor(f); double u = f - k; if (k >= 0 && k < n - 1) e[i] = ax[k] * (1 - u) + ax[k + 1] * u; }
                var tr = Frog.Trace(e, "shg", m);
                var omg = gm.OmegaOffset.Select(x => x + 2 * g.OmegaCenter).ToArray();
                Frog.ApplySpectralResponse(tr, m, m, omg, s, true, o, "spectralResolutionM");
                var ac = new double[m];
                for (int d = 0; d < m; d++) for (int k = 0; k < m; k++) ac[d] += tr[d * m + k];
                ac = Tools.GaussianSmooth(ac, dtm, resS);
                double mx = ac.Max(); for (int i = 0; i < m; i++) ac[i] /= Math.Max(mx, 1e-300);
                var tau = gm.Time.ToArray();
                o.Add("delay_s", tau, "s", "delay"); o.Add("signal", ac, "norm.", "intensity autocorrelation (finite SHG acceptance)");
                double fw = PulseMetrics.Fwhm(tau, ac);
                Tools.Put(o, "acFwhmS", fw);
                Tools.Put(o, "pulseFwhmGaussianS", fw / Math.Sqrt(2));
                Tools.Put(o, "pulseFwhmSech2S", fw / 1.5427);
                if (resS > 0) Tools.Put(o, "resolutionS", resS);
            }
            else if (!type.StartsWith("interf") && type != "iac" && type != "frac")
            {
                // G2(tau) = int I(t) I(t - tau) dt via FFT (circular; the compact grid is padded)
                var c = new Complex[n]; for (int i = 0; i < n; i++) c[i] = pt[i];
                var f = SpectralTransform.TimeToFreq(c);
                for (int i = 0; i < n; i++) f[i] = f[i] * Complex.Conjugate(f[i]);
                var ac = SpectralTransform.FreqToTime(f).Select(z => z.Real).ToArray();
                if (resS > 0) { ac = Tools.GaussianSmooth(ac, g.DTime, resS); Tools.Put(o, "resolutionS", resS); }   // delay resolution of the scan
                double mx = ac.Max(); for (int i = 0; i < n; i++) ac[i] /= mx;
                var (lo, hi) = PulseMetrics.SupportRange(ac, 1e-6, 8);
                var tau = Engine.Executor.Crop(g.Time, lo, hi, 0); var y = Engine.Executor.Crop(ac, lo, hi, 0);
                o.Add("delay_s", tau, "s", "delay"); o.Add("signal", y, "norm.", "intensity autocorrelation");
                double fw = PulseMetrics.Fwhm(tau, y);
                Tools.Put(o, "acFwhmS", fw);
                Tools.Put(o, "pulseFwhmGaussianS", fw / Math.Sqrt(2));
                Tools.Put(o, "pulseFwhmSech2S", fw / 1.5427);
            }
            else
            {
                // IAC(tau) = int |(E(t) + E(t - tau))^2|^2 dt with the carrier; tau sampled at lambda0/(16c)
                var ax = p.TemporalAx();
                double w0 = g.OmegaCenter;
                var I = pt; double fwI = PulseMetrics.Fwhm(g.Time, I);
                double range = s.GetDouble("rangeS", 3 * (double.IsNaN(fwI) ? 100e-15 : fwI));
                double dtau = 2 * Math.PI / w0 / 16;
                // the delay axis is symmetric with an odd number of points, so it contains d = 0 (the fringe maximum, S(0) = 8 x background);
                // the step is lambda0/(16 c) unless the "points" cap forces a coarser one (then a note says the fringes are under-sampled)
                int kHalf = (int)Math.Floor(range / dtau);
                int cap = Math.Max(3, s.GetInt("points", 4001)); if (cap % 2 == 0) cap--;
                int m = 2 * kHalf + 1;
                double[] tau;
                if (m <= cap) { tau = new double[m]; for (int k = 0; k < m; k++) tau[k] = (k - kHalf) * dtau; }
                else
                {
                    m = cap; tau = Tools.Linspace(-range, range, m); tau[m / 2] = 0;
                    double st = 2 * range / (m - 1);
                    if (st > dtau * 1.0001) o.Notes.Add("interferometric autocorrelation: \"points\" = " + cap + " gives a delay step of " + (st * 1e15).ToString("G3", CultureInfo.InvariantCulture) + " fs, coarser than lambda0/(16c) = " + (dtau * 1e15).ToString("G3", CultureInfo.InvariantCulture) + " fs: the fringes are under-sampled and peakToBackground is biased low");
                }
                var y = new double[m];
                for (int k = 0; k < m; k++)
                {
                    double sh = tau[k] / g.DTime; int si = (int)Math.Floor(sh); double u = sh - si;
                    var ph = Complex.FromPolarCoordinates(1, w0 * tau[k]);
                    double acc = 0;
                    for (int i = 0; i < n; i++)
                    {
                        int j = i - si; Complex d = Complex.Zero;
                        if (j - 1 >= 0 && j < n) d = ax[j] * (1 - u) + ax[j - 1] * u;
                        var e = ax[i] + d * ph;
                        var e2 = e * e;
                        acc += e2.Real * e2.Real + e2.Imaginary * e2.Imaginary;
                    }
                    y[k] = acc;
                }
                double bg = y[0], mxv = y.Max();
                for (int k = 0; k < m; k++) y[k] /= Math.Max(1e-300, bg);
                o.Add("delay_s", tau, "s", "delay"); o.Add("signal", y, "norm. to background", "interferometric autocorrelation (8:1 peak:background for a clean pulse)");
                Tools.Put(o, "peakToBackground", mxv / Math.Max(1e-300, bg));
            }
        }
    }

    /// <summary>FROG trace: "type": "shg" (default), "pg" (polarisation gate) or "sd" (self-diffraction); "points" (delay and frequency,
    /// default 256). I(w, tau) = |int E_sig(t, tau) e^{i w t} dt|^2 with E_sig = E(t) E(t - tau) (SHG), E(t) |E(t - tau)|^2 (PG) or
    /// E(t)^2 E*(t - tau) (SD; Kane, Trebino, Opt. Lett. 18, 823 (1993)).
    /// The <c>xfrog</c> kind (alias <c>sfgFrog</c>) gates with a KNOWN reference pulse ("reference": a pulse object as in the run spec, any
    /// carrier): E_sig = E(t) R(t - tau) at the sum frequency w + w_ref (cross-correlation FROG, Linden et al., Appl. Phys. B 1999).
    /// The reading records its type ("frogType") and, for XFROG, the reference field, so a retrieval needs no further input.</summary>
    public sealed class Frog : IInstrument
    {
        private readonly string _kind;
        public Frog(string kind = "frog") { _kind = kind; }
        public string Kind => _kind;
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var s = ctx.Spec;
            bool xf = _kind == "xfrog";
            string type = xf ? "x" : s.GetString("type", "shg").ToLowerInvariant();
            if (type != "shg" && type != "pg" && type != "sd" && type != "x") throw new InvalidOperationException("frog: type must be \"shg\", \"pg\" or \"sd\" (got \"" + type + "\").");
            int m = s.GetInt("points", 256);
            var p = Tools.Compact(ctx.Pulse, 1 << 14, out var note, 2.0);
            if (note != null) o.Notes.Add(note);
            // resample to an m-point grid spanning the compact window
            var g0 = p.Grid; var a0 = p.TemporalAx(); var b0 = p.TemporalAy();
            double win = g0.N * g0.DTime;
            double dt = win / m;
            var g = FrequencyGrid.Create(m, dt, g0.OmegaCenter);
            var e = new Complex[m];
            for (int i = 0; i < m; i++)
            {
                double f = (g.Time[i] - g0.Time[0]) / g0.DTime; int k = (int)Math.Floor(f); double u = f - k;
                if (k < 0 || k >= g0.N - 1) continue;
                e[i] = (a0[k] * (1 - u) + a0[k + 1] * u) + (b0[k] * (1 - u) + b0[k + 1] * u) * 0; // x polarisation (FROG uses one polarisation)
            }
            Complex[] refField = null; double wRef = 0;
            if (xf)
            {
                var rj = s.GetObject("reference") ?? throw new InvalidOperationException("xfrog: \"reference\" (the known gate pulse, as a run-spec pulse object) is required.");
                if (!rj.Has("energyJ") && !rj.Has("averagePowerW") && !rj.Has("peakPowerW")) { rj = (JsonObject)JsonValue.Parse(rj.ToJson(false)); rj.Set("energyJ", 1e-9); }
                var rs = PulseSpec.FromJson(rj, null, "xfrog.reference");
                wRef = 2 * Math.PI * Tools.C / rs.ResolveCenterWavelengthM();
                var rg = FrequencyGrid.Create(m, dt, wRef);
                refField = rs.Build(rg, false).TemporalAx();
            }
            var trace = Trace(e, type, m, refField);
            var delays = g.Time.ToArray();
            double wc = type == "shg" ? 2 * g.OmegaCenter : type == "x" ? g.OmegaCenter + wRef : g.OmegaCenter;
            var omg = g.OmegaOffset.Select(x => x + wc).ToArray();
            ApplySpectralResponse(trace, m, m, omg, s, type == "shg", o, "spectralResolutionM");
            double mx = trace.Max(); if (mx > 0) for (int i = 0; i < trace.Length; i++) trace[i] /= mx;
            o.Add("delay_s", delays, "s", "delay axis (rows)");
            o.Add("omega_rad_s", omg, "rad/s", "signal angular frequency (columns)");
            o.Add("wavelength_m", omg.Select(x => x > 0 ? 2 * Math.PI * Tools.C / x : double.NaN).ToArray(), "m", "signal wavelength (columns)");
            string label = type == "shg" ? "SHG" : type == "pg" ? "PG" : type == "sd" ? "SD" : "XFROG (sum frequency)";
            o.Add("trace", trace, "norm.", label + "-FROG trace [delay, frequency]", m, m);
            if (xf)
            {
                o.Add("reference_time_s", delays, "s", "time axis of the reference field");
                o.Add("reference_field_re", refField.Select(z => z.Real).ToArray(), "sqrt(W)", "reference field envelope, real part (about reference_omega_rad_s)");
                o.Add("reference_field_im", refField.Select(z => z.Imaginary).ToArray(), "sqrt(W)", "reference field envelope, imaginary part");
                o.Add("reference_omega_rad_s", new[] { wRef }, "rad/s", "carrier of the reference pulse");
                Tools.Put(o, "referenceOmegaRadS", wRef);
            }
            o.Values.Set("frogType", xf ? "xfrog" : type);
            // marginals
            var dm = new double[m]; var fm = new double[m];
            for (int d = 0; d < m; d++) for (int k = 0; k < m; k++) { dm[d] += trace[d * m + k]; fm[k] += trace[d * m + k]; }
            o.Add("delay_marginal", dm, "arb.", "delay marginal"); o.Add("frequency_marginal", fm, "arb.", "frequency marginal");
            Tools.Put(o, "delayMarginalFwhmS", PulseMetrics.Fwhm(delays, dm));
            Tools.Put(o, "delayStepS", dt);
        }

        /// <summary>The FROG signal |FT[E(t) G(t - tau)]|^2 on an m x m grid (rows: delay, columns: frequency offset).</summary>
        internal static double[] Trace(Complex[] e, string type, int m, Complex[] reference = null)
        {
            var trace = new double[m * m];
            var row = new Complex[m];
            for (int d = 0; d < m; d++)
            {
                int shift = d - m / 2;
                for (int i = 0; i < m; i++)
                {
                    int j = i - shift;
                    if (j < 0 || j >= m) { row[i] = Complex.Zero; continue; }
                    switch (type)
                    {
                        case "pg": row[i] = e[i] * (Complex)(e[j].Magnitude * e[j].Magnitude); break;
                        case "sd": row[i] = e[i] * e[i] * Complex.Conjugate(e[j]); break;
                        case "x": row[i] = e[i] * reference[j]; break;
                        default: row[i] = e[i] * e[j]; break;
                    }
                }
                var f = SpectralTransform.TimeToFreq(row);
                for (int k = 0; k < m; k++) trace[d * m + k] = f[k].Real * f[k].Real + f[k].Imaginary * f[k].Imaginary;
            }
            return trace;
        }

        /// <summary>Phase-matching acceptance of the SHG crystal ("shgAcceptanceFwhmM": FWHM of the SH wavelength
        /// window) and the resolution of the spectrometer (<paramref name="resolutionKey"/>: Gaussian FWHM in
        /// wavelength at the signal) applied to a [rows x cols] trace whose columns are at absolute frequencies
        /// <paramref name="omega"/> (ascending, uniform).</summary>
        internal static void ApplySpectralResponse(double[] trace, int rows, int cols, double[] omega, JsonObject s, bool shg, InstrumentOutput o, string resolutionKey)
        {
            double acc = s.GetDouble("shgAcceptanceFwhmM", 0), res = s.GetDouble(resolutionKey, 0);
            int kc = cols / 2; double wc = omega[kc]; double lc = 2 * Math.PI * Tools.C / wc;
            if (acc > 0)
            {
                if (!shg) o.Notes.Add("shgAcceptanceFwhmM ignored: no SHG crystal in this geometry");
                else
                {
                    double dwAcc = 2 * Math.PI * Tools.C * acc / (lc * lc);
                    for (int k = 0; k < cols; k++)
                    {
                        double x = (omega[k] - wc) / dwAcc, h = Math.Exp(-4 * Math.Log(2) * x * x);
                        for (int r = 0; r < rows; r++) trace[r * cols + k] *= h;
                    }
                    Tools.Put(o, "shgAcceptanceFwhmM", acc);
                }
            }
            if (res > 0 && cols > 1)
            {
                double dw = omega[1] - omega[0], fwW = 2 * Math.PI * Tools.C * res / (lc * lc);
                var row = new double[cols];
                for (int r = 0; r < rows; r++)
                {
                    Array.Copy(trace, r * cols, row, 0, cols);
                    var sm = Tools.GaussianSmooth(row, dw, fwW);
                    Array.Copy(sm, 0, trace, r * cols, cols);
                }
                Tools.Put(o, resolutionKey, res);
            }
        }
    }

    /// <summary>Dispersion scan: SHG spectrum vs glass insertion ("material", "insertionM": [min, max], "steps")
    /// or vs added GDD ("gddRangeS2": [min, max]). Reports the insertion/GDD of maximum SHG and the pulse FWHM there.</summary>
    public sealed class DScan : IInstrument
    {
        public string Kind => "dscan";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var s = ctx.Spec;
            int steps = s.GetInt("steps", 101);
            var p = Tools.Compact(ctx.Pulse, 1 << 13, out var note, 3.0);
            if (note != null) o.Notes.Add(note);
            var g = p.Grid; int n = g.N;
            bool glass = !s.Has("gddRangeS2");
            double[] range = glass ? (s.GetDoubleArray("insertionM") ?? new[] { -2e-3, 2e-3 }) : s.GetDoubleArray("gddRangeS2");
            Material mat = glass ? MaterialLibrary.BuiltIn(s.GetString("material", "FusedSilica")) : null;
            var phiUnit = new double[n];
            if (glass)
            {
                var nIdx = new double[n];
                mat.Index.Evaluate(g.OmegaAbsolute, nIdx);
                double ng0 = mat.GroupIndexAt(g.OmegaCenter), n0 = mat.IndexAt(g.OmegaCenter);
                for (int i = 0; i < n; i++)
                {
                    double w = g.OmegaAbsolute[i];
                    // phase per metre, carrier phase and group delay removed
                    phiUnit[i] = double.IsNaN(nIdx[i]) ? 0 : (nIdx[i] * w - n0 * g.OmegaCenter - ng0 * (w - g.OmegaCenter)) / Tools.C;
                }
            }
            else for (int i = 0; i < n; i++) phiUnit[i] = 0.5 * g.OmegaOffset[i] * g.OmegaOffset[i];
            var x = Tools.Linspace(range[0], range[1], steps);
            // SHG spectral window around 2 w0
            var (sl, sh) = PulseMetrics.SupportRange(p.SpectralIntensity(), 1e-6, 4);
            int cols = Math.Min(256, n);
            var trace = new double[steps * cols];
            var peak = new double[steps]; var fwhm = new double[steps];
            var shgOmega = new double[cols];
            for (int k = 0; k < steps; k++)
            {
                var a = new Complex[n];
                for (int i = 0; i < n; i++) a[i] = p.Ax[i] * Complex.FromPolarCoordinates(1, phiUnit[i] * x[k]);
                var at = SpectralTransform.FreqToTime(a);
                var pw = at.Select(c => c.Real * c.Real + c.Imaginary * c.Imaginary).ToArray();
                fwhm[k] = PulseMetrics.Fwhm(g.Time, pw);
                var sq = new Complex[n]; for (int i = 0; i < n; i++) sq[i] = at[i] * at[i];
                var sf = SpectralTransform.TimeToFreq(sq);
                double tot = 0;
                for (int c = 0; c < cols; c++)
                {
                    int idx = n / 2 - cols / 2 + c;
                    double v = sf[idx].Real * sf[idx].Real + sf[idx].Imaginary * sf[idx].Imaginary;
                    trace[k * cols + c] = v; tot += v;
                    if (k == 0) shgOmega[c] = 2 * g.OmegaCenter + g.OmegaOffset[idx];
                }
                peak[k] = tot;
            }
            ApplyResponse(trace, steps, cols, shgOmega, s, o, peak);
            double mx = trace.Max(); if (mx > 0) for (int i = 0; i < trace.Length; i++) trace[i] /= mx;
            o.Add(glass ? "insertion_m" : "gdd_s2", x, glass ? "m" : "s^2", "scan axis (rows)");
            o.Add("shg_wavelength_m", shgOmega.Select(w => 2 * Math.PI * Tools.C / w).ToArray(), "m", "SHG wavelength (columns)");
            o.Add("trace", trace, "norm.", "d-scan trace [scan, SHG frequency]", steps, cols);
            o.Add("shg_total", peak, "arb.", "integrated SHG vs scan");
            o.Add("fwhm_s", fwhm, "s", "pulse FWHM vs scan");
            int best = Array.IndexOf(peak, peak.Max());
            Tools.Put(o, glass ? "optimalInsertionM" : "optimalGddS2", x[best]);
            Tools.Put(o, "fwhmAtOptimumS", fwhm[best]);
            int bestT = Enumerable.Range(0, steps).Where(i => !double.IsNaN(fwhm[i])).OrderBy(i => fwhm[i]).FirstOrDefault();
            Tools.Put(o, "shortestFwhmS", fwhm[bestT]);
            if (glass) o.Values.Set("material", mat.Name);
        }

        private static void ApplyResponse(double[] trace, int steps, int cols, double[] shgOmega, JsonObject s, InstrumentOutput o, double[] total)
        {
            if (!(s.GetDouble("shgAcceptanceFwhmM", 0) > 0) && !(s.GetDouble("spectralResolutionM", 0) > 0)) return;
            Frog.ApplySpectralResponse(trace, steps, cols, shgOmega, s, true, o, "spectralResolutionM");
            for (int k = 0; k < steps; k++) { double t = 0; for (int c = 0; c < cols; c++) t += trace[k * cols + c]; total[k] = t; }
        }
    }

    /// <summary>Temporal contrast (3rd-order cross-correlator style): normalised intensity in dB with a gate of
    /// "resolutionS" (FWHM), reports the highest pre-pulse / post-pulse level outside +-"windowS" of the peak and the
    /// fraction of energy outside +-3 FWHM (pedestal).</summary>
    public sealed class ContrastMeter : IInstrument
    {
        public string Kind => "contrast";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var p = ctx.Pulse; var g = p.Grid; var s = ctx.Spec;
            var pt = PulseMetrics.TemporalPower(p);
            double res = s.GetDouble("resolutionS", 0);
            var y = Tools.GaussianSmooth(pt, g.DTime, res);
            double mx = y.Max(); int ip = Array.IndexOf(y, mx);
            double fw = PulseMetrics.Fwhm(g.Time, y);
            double win = s.GetDouble("windowS", 5 * (double.IsNaN(fw) ? 100e-15 : fw));
            double dyn = s.GetDouble("dynamicRangeDb", 120);
            var db = y.Select(v => Math.Max(10 * Math.Log10(Math.Max(v / mx, 1e-300)), -dyn)).ToArray();
            double pre = -dyn, post = -dyn, ped = 0, tot = 0;
            for (int i = 0; i < g.N; i++)
            {
                double dt = g.Time[i] - g.Time[ip];
                if (dt < -win) pre = Math.Max(pre, db[i]);
                if (dt > win) post = Math.Max(post, db[i]);
                tot += pt[i];
                if (!double.IsNaN(fw) && Math.Abs(dt) > 3 * fw) ped += pt[i];
            }
            int pts = s.GetInt("points", 8192);
            o.Add("time_s", Engine.Executor.Crop(g.Time, 0, g.N - 1, pts), "s", "time");
            o.Add("intensity_db", Engine.Executor.CropMax(db, 0, g.N - 1, pts), "dB", "normalised intensity");
            Tools.Put(o, "prePulseContrastDb", pre); Tools.Put(o, "postPulseContrastDb", post);
            Tools.Put(o, "pedestalEnergyFraction", tot > 0 ? ped / tot : double.NaN);
            Tools.Put(o, "fwhmS", fw);
        }
    }

    /// <summary>Polarimeter: spectrally integrated Stokes parameters, degree of polarisation, azimuth and
    /// ellipticity; plus spectrally resolved s1, s2, s3 over the spectral support.</summary>
    public sealed class Polarimeter : IInstrument
    {
        public string Kind => "polarimeter";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var p = ctx.Pulse; var g = p.Grid;
            var sum = PulseMetrics.Summarize(p);
            // finite extinction ratio of the analysers: every measured Stokes component is scaled by (1 - e) / (1 + e)
            double ext = ctx.Spec.GetDouble("extinctionRatio", 0), kext = (1 - ext) / (1 + ext);
            Tools.Put(o, "s1", kext * sum.S1); Tools.Put(o, "s2", kext * sum.S2); Tools.Put(o, "s3", kext * sum.S3);
            Tools.Put(o, "degreeOfPolarization", kext * sum.PolarizationDegree);
            Tools.Put(o, "azimuthDeg", 0.5 * Math.Atan2(sum.S2, sum.S1) * 180 / Math.PI);
            double chi = 0.5 * Math.Asin(Math.Max(-1, Math.Min(1, sum.S3 / Math.Max(1e-300, sum.PolarizationDegree))));
            Tools.Put(o, "ellipticityAngleDeg", chi * 180 / Math.PI);
            var sp = p.SpectralIntensity();
            var (lo, hi) = PulseMetrics.SupportRange(sp, 1e-4, 2);
            int m = hi - lo + 1;
            var wl = new double[m]; var s1 = new double[m]; var s2 = new double[m]; var s3 = new double[m];
            for (int k = 0; k < m; k++)
            {
                int i = lo + k;
                var x = p.Ax[i]; var y = p.Ay[i];
                double xx = x.Magnitude * x.Magnitude, yy = y.Magnitude * y.Magnitude, s0 = xx + yy;
                var xy = x * Complex.Conjugate(y);
                wl[k] = 2 * Math.PI * Tools.C / g.OmegaAbsolute[i];
                s1[k] = s0 > 0 ? (xx - yy) / s0 : 0; s2[k] = s0 > 0 ? 2 * xy.Real / s0 : 0; s3[k] = s0 > 0 ? 2 * xy.Imaginary / s0 : 0;
            }
            int max = ctx.Spec.GetInt("points", 1024);
            o.Add("wavelength_m", Engine.Executor.Crop(wl, 0, m - 1, max), "m", "wavelength");
            o.Add("s1", Engine.Executor.Crop(s1, 0, m - 1, max), "", "normalised Stokes s1 vs wavelength");
            o.Add("s2", Engine.Executor.Crop(s2, 0, m - 1, max), "", "normalised Stokes s2 vs wavelength");
            o.Add("s3", Engine.Executor.Crop(s3, 0, m - 1, max), "", "normalised Stokes s3 vs wavelength");
        }
    }

    /// <summary>All scalar pulse metrics (duration, TL, Strehl, bandwidth, chirp, Stokes...).</summary>
    public sealed class PulseSummaryInstrument : IInstrument
    {
        public string Kind => "pulseSummary";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var s = PulseMetrics.Summarize(ctx.Pulse); // explicit list: no reflection (NativeAOT/trimming safe)
            Tools.Put(o, "energyJ", s.EnergyJ); Tools.Put(o, "peakPowerW", s.PeakPowerW); Tools.Put(o, "fwhmS", s.FwhmS);
            Tools.Put(o, "rmsWidthS", s.RmsWidthS); Tools.Put(o, "transformLimitFwhmS", s.TransformLimitFwhmS); Tools.Put(o, "temporalStrehl", s.TemporalStrehl);
            Tools.Put(o, "spectralFwhmM", s.SpectralFwhmM); Tools.Put(o, "spectralRmsWidthM", s.SpectralRmsWidthM); Tools.Put(o, "centroidWavelengthM", s.CentroidWavelengthM);
            Tools.Put(o, "peakWavelengthM", s.PeakWavelengthM); Tools.Put(o, "timeBandwidthProduct", s.TimeBandwidthProduct);
            Tools.Put(o, "pulseGddS2", s.PulseGddS2); Tools.Put(o, "pulseTodS3", s.PulseTodS3); Tools.Put(o, "centroidTimeS", s.CentroidTimeS);
            Tools.Put(o, "edgeEnergyFraction", s.EdgeEnergyFraction); Tools.Put(o, "degreeOfPolarization", s.PolarizationDegree);
            Tools.Put(o, "s1", s.S1); Tools.Put(o, "s2", s.S2); Tools.Put(o, "s3", s.S3);
        }
    }

    /// <summary>Ideal compressor: finds the GDD (and, with "optimizeTod": true, TOD) that maximises peak power and
    /// reports the compressed pulse. Answers "how short could this pulse be after a matched compressor?"</summary>
    public sealed class IdealCompressor : IInstrument
    {
        public string Kind => "compressor";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            // work on a compact copy of the pulse (cropped to its temporal support) -- the search needs ~100 FFTs
            var p = Tools.Compact(ctx.Pulse, ctx.Spec.GetInt("maxN", 1 << 17), out var note, 1.6);
            if (note != null) o.Notes.Add(note);
            var g = p.Grid; int n = g.N;
            var (gdd0, tod0) = PulseMetrics.FitSpectralPhase(p);
            if (double.IsNaN(gdd0)) gdd0 = 0; if (double.IsNaN(tod0)) tod0 = 0;
            Func<double, double, double> peakAt = (gdd, tod) =>
            {
                var a = new Complex[n]; var b = new Complex[n];
                for (int i = 0; i < n; i++)
                {
                    double d = g.OmegaOffset[i];
                    var ph = Complex.FromPolarCoordinates(1, -(gdd * d * d / 2 + tod * d * d * d / 6));
                    a[i] = p.Ax[i] * ph; b[i] = p.Ay[i] * ph;
                }
                return PulseMetrics.TemporalPower(new PulseField(g, a, b)).Max();
            };
            double Golden(Func<double, double> f, double lo, double hi)
            {
                double gr = (Math.Sqrt(5) - 1) / 2;
                double c = hi - gr * (hi - lo), d = lo + gr * (hi - lo), fc = f(c), fd = f(d);
                for (int it = 0; it < 45; it++)
                {
                    if (fc > fd) { hi = d; d = c; fd = fc; c = hi - gr * (hi - lo); fc = f(c); }
                    else { lo = c; c = d; fc = fd; d = lo + gr * (hi - lo); fd = f(d); }
                }
                return 0.5 * (lo + hi);
            }
            // Coarse scans (robust to a poor phase fit and to several local maxima, e.g. after strong SPM), then
            // golden-section refinement inside the best bracket. Steps are a fraction of the transform-limit scale
            // so the peak-power ridge cannot be stepped over.
            double tl = PulseMetrics.Fwhm(g.Time, PulseMetrics.TransformLimitedPower(p));
            double scale = double.IsNaN(tl) ? 1e-28 : tl * tl;
            (double best, double val) Scan(Func<double, double> f, double lo, double hi, int m)
            {
                double bx = 0.5 * (lo + hi), bv = double.NegativeInfinity;
                for (int i = 0; i < m; i++)
                {
                    double x = lo + (hi - lo) * i / (m - 1), v = f(x);
                    if (v > bv) { bv = v; bx = x; }
                }
                return (bx, bv);
            }
            double half = Math.Abs(gdd0) + 12 * scale;
            int nScan = (int)Math.Min(801, Math.Max(61, Math.Ceiling(2 * half / (0.25 * scale)) + 1));
            var (bestX, _) = Scan(x => peakAt(x, 0), gdd0 - half, gdd0 + half, nScan);
            double span = 2 * half / (nScan - 1);
            double gddBest = Golden(x => peakAt(x, 0), bestX - span, bestX + span);
            double todBest = 0;
            if (ctx.Spec.GetBool("optimizeTod", true) && !double.IsNaN(tl))
            {
                double ts = 12 * scale * tl, tstep = 2 * ts / 120;
                var (t0, _) = Scan(x => peakAt(gddBest, x), -ts, ts, 121);
                double todC = Golden(x => peakAt(gddBest, x), t0 - tstep, t0 + tstep);
                double gddC = Golden(x => peakAt(x, todC), gddBest - 4 * span, gddBest + 4 * span);
                if (peakAt(gddC, todC) > peakAt(gddBest, 0)) { gddBest = gddC; todBest = todC; }
            }
            // never report a "compressor" that is worse than none
            if (peakAt(0, 0) >= peakAt(gddBest, todBest)) { gddBest = 0; todBest = 0; }
            // The reported pulse is the input on its own grid with the best phase. The compact copy resolves the
            // spectrum, not the compressed peak: its time step can be a fifth of the compressed FWHM after strong SPM,
            // and the half-maximum crossings interpolated on it were 4 % short at B = 40 rad (Escoto et al., JOSA B
            // 39, 1694 (2022),).
            var full = ctx.Pulse; var fg = full.Grid; int fn = fg.N;
            var aa = new Complex[fn]; var bb = new Complex[fn];
            for (int i = 0; i < fn; i++)
            {
                double d = fg.OmegaOffset[i];
                var ph = Complex.FromPolarCoordinates(1, -(gddBest * d * d / 2 + todBest * d * d * d / 6));
                aa[i] = full.Ax[i] * ph; bb[i] = full.Ay[i] * ph;
            }
            var comp = new PulseField(fg, aa, bb, full.RepRateHz);
            var sm = PulseMetrics.Summarize(comp);
            Tools.Put(o, "compressorGddS2", -gddBest); Tools.Put(o, "compressorTodS3", -todBest);
            Tools.Put(o, "compressedFwhmS", sm.FwhmS); Tools.Put(o, "compressedPeakPowerW", sm.PeakPowerW);
            Tools.Put(o, "transformLimitFwhmS", sm.TransformLimitFwhmS); Tools.Put(o, "temporalStrehl", sm.TemporalStrehl);
            var pt = PulseMetrics.TemporalPower(comp);
            var (lo2, hi2) = PulseMetrics.SupportRange(pt, 1e-6, 8);
            int pts = ctx.Spec.GetInt("points", 4096);
            o.Add("time_s", Engine.Executor.Crop(fg.Time, lo2, hi2, pts), "s", "time");
            o.Add("power_w", Engine.Executor.CropMax(pt, lo2, hi2, pts), "W", "compressed pulse power");
        }
    }
}
