using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Schema;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Instruments
{
    /// <summary>
    /// The parts every real instrument shares, applied around <see cref="IInstrument.Measure"/> by the executor:
    /// <list type="bullet">
    /// <item><c>"filter"</c>: a spectral filter model in front of the instrument (optics, detector spectral response),
    /// applied to the field before it is measured;</item>
    /// <item><c>"detector"</c>: the read-out of the instrument's recorded arrays and readings -- calibration factor,
    /// multiplicative noise, additive noise floor (or dynamic range), ADC bit depth -- after which the instrument's
    /// width / peak readings are re-evaluated on the detected trace;</item>
    /// <item>the <c>settings</c> block of every output: each declared parameter with the value used (given or default),
    /// so a results file says exactly what device it describes.</item>
    /// </list>
    /// </summary>
    public static class InstrumentDevice
    {
        /// <summary>Recorded arrays and readings of each instrument kind that the detector model acts on, and how to
        /// re-evaluate the derived readings ((x array, y array) → keys).</summary>
        private sealed class Readout
        {
            public string[] Arrays = new string[0];
            public string[] Scalars = new string[0];
            public Action<InstrumentOutput> Remeasure;
        }

        private static readonly Dictionary<string, Readout> Readouts = new Dictionary<string, Readout>(StringComparer.OrdinalIgnoreCase)
        {
            { "spectrometer", new Readout { Arrays = new[] { "spectrum" }, Scalars = new[] { "energyJ" }, Remeasure = o =>
                {
                    var x = Arr(o, "wavelength_m"); var y = Arr(o, "spectrum"); if (x == null || y == null) return;
                    double mx = y.Max(); int ip = Array.IndexOf(y, mx);
                    Tools.Put(o, "peakWavelengthM", x[ip]);
                    var (m, rms) = PulseMetrics.Moments(x, y.Select(v => Math.Max(v, 0)).ToArray());
                    Tools.Put(o, "centroidWavelengthM", m); Tools.Put(o, "rmsWidthM", rms);
                    Tools.Put(o, "fwhmM", PulseMetrics.Fwhm(x, y));
                    var db = o.Arrays.FirstOrDefault(a => a.Name.EndsWith("_spectrum_db", StringComparison.Ordinal));
                    if (db != null) for (int i = 0; i < y.Length; i++) db.Data[i] = 10 * Math.Log10(Math.Max(y[i] / Math.Max(mx, 1e-300), 1e-30));
                } } },
            { "scope", new Readout { Arrays = new[] { "signal_w" }, Remeasure = o =>
                {
                    var x = Arr(o, "time_s"); var y = Arr(o, "signal_w"); if (x == null || y == null) return;
                    Tools.Put(o, "fwhmS", PulseMetrics.Fwhm(x, y)); Tools.Put(o, "peakW", y.Max());
                } } },
            { "autocorrelator", new Readout { Arrays = new[] { "signal" }, Remeasure = o =>
                {
                    if (!o.Values.Has("acFwhmS")) return;
                    var x = Arr(o, "delay_s"); var y = Arr(o, "signal"); if (x == null || y == null) return;
                    double fw = PulseMetrics.Fwhm(x, y);
                    Tools.Put(o, "acFwhmS", fw); Tools.Put(o, "pulseFwhmGaussianS", fw / Math.Sqrt(2)); Tools.Put(o, "pulseFwhmSech2S", fw / 1.5427);
                } } },
            { "frog", new Readout { Arrays = new[] { "trace" } } },
            { "dscan", new Readout { Arrays = new[] { "trace", "shg_total" } } },
            { "energyMeter", new Readout { Scalars = new[] { "energyJ", "averagePowerW" } } },
            { "camera", new Readout() },          // has its own sensor model
            { "crossCorrelator", new Readout() }, // has its own noise floor
        };

        private static double[] Arr(InstrumentOutput o, string suffix) => o.Arrays.FirstOrDefault(a => a.Name.EndsWith("_" + suffix, StringComparison.Ordinal))?.Data;

        /// <summary>The field the instrument sees through its "filter" (spectral transmission and phase).</summary>
        public static PulseField ApplyFilter(InstrumentContext ctx, InstrumentOutput o)
        {
            if (!ctx.Spec.Has("filter")) return ctx.Pulse;
            var spec = ModelSpec.FromJson(ctx.Spec["filter"], ModelSource.RunOverride, "instrument " + o.Name + ".filter");
            var f = ctx.Registry.Create<ISpectralFilter>("filter", spec);
            if (f is Physics.ExpressionFilter ef) ef.Omega0 = ctx.Pulse.Grid.OmegaCenter;
            var g = ctx.Pulse.Grid; int n = g.N;
            var T = new double[n]; var ph = new double[n];
            f.Evaluate(g.OmegaAbsolute, T, ph);
            var ax = new Complex[n]; var ay = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                double t = double.IsNaN(T[i]) ? 0 : Math.Max(0, T[i]);
                var h = Complex.FromPolarCoordinates(Math.Sqrt(t), double.IsNaN(ph[i]) ? 0 : ph[i]);
                ax[i] = ctx.Pulse.Ax[i] * h; ay[i] = ctx.Pulse.Ay[i] * h;
            }
            o.Notes.Add("filter in front of the instrument: " + f.Description);
            return new PulseField(g, ax, ay, ctx.Pulse.RepRateHz) { Profile = ctx.Pulse.Profile };
        }

        /// <summary>The axis array of each curve instrument that "axis" resamples (every 1-D array of the same length is
        /// resampled with it; "_db" arrays are recomputed from their linear counterpart or binned in linear units).</summary>
        private static readonly Dictionary<string, string[]> AxisArrays = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "spectrometer", new[] { "wavelength_m" } }, { "scope", new[] { "time_s" } }, { "photodiode", new[] { "time_s" } },
            { "autocorrelator", new[] { "delay_s" } }, { "crossCorrelator", new[] { "delay_s" } }, { "thirdOrderCorrelator", new[] { "delay_s" } },
            { "polarimeter", new[] { "wavelength_m" } }, { "delayScan", new[] { "delay_s" } }, { "contrast", new[] { "time_s" } },
            { "beamProfile", new[] { "radius_m", "x_m" } }, { "phaseMatchingCurve", new[] { "detuning_deg", "temperature_c", "period_m", "wavelength_m" } },
        };

        /// <summary>
        /// The instrument's sampling of its axis ("axis": where it actually takes readings): explicit points
        /// (<c>values</c>) or <c>from</c> / <c>to</c> with <c>step</c> or <c>points</c>, each reading integrating over
        /// <c>binWidth</c> (a number, or "pixel" = the local spacing) and blurred by a Gaussian <c>resolution</c> (FWHM).
        /// Units are those of the axis (m for wavelengths, s for delays and time). Readings outside the simulated range
        /// are zero (no light there), with a note.
        /// </summary>
        public static void ApplyAxis(JsonObject ispec, InstrumentOutput o)
        {
            var ax = ispec.GetObject("axis");
            if (ax == null) return;
            if (!AxisArrays.TryGetValue(o.Kind, out var names)) { o.Notes.Add("\"axis\" is not used by a " + o.Kind + " (no single curve axis; it has its own sampling keys)"); return; }
            var xa = names.Select(nm => o.Arrays.FirstOrDefault(a => a.Name.EndsWith("_" + nm, StringComparison.Ordinal) && (a.Shape == null || a.Shape.Length <= 1))).FirstOrDefault(a => a != null);
            if (xa == null || xa.Data.Length < 2) return;
            int n = xa.Data.Length;
            var ord = Enumerable.Range(0, n).OrderBy(i => xa.Data[i]).ToArray();
            var fx = ord.Select(i => xa.Data[i]).ToArray();
            double[] target;
            if (ax.Has("values")) target = ax.GetDoubleArray("values");
            else
            {
                double from = ax.GetDouble("from", fx[0]), to = ax.GetDouble("to", fx[n - 1]);
                if (ax.Has("step")) { double st = Math.Abs(ax.GetDouble("step", 0)); if (!(st > 0)) throw new JsonException("instrument " + o.Name + ": axis.step must be > 0"); int m = Math.Min(1000000, (int)Math.Floor((to - from) / st + 1e-9) + 1); target = Enumerable.Range(0, m).Select(i => from + i * st).ToArray(); }
                else { int m = ax.GetInt("points", n); target = Enumerable.Range(0, m).Select(i => m == 1 ? from : from + (to - from) * i / (m - 1)).ToArray(); }
            }
            if (target == null || target.Length == 0) throw new JsonException("instrument " + o.Name + ": axis gives no points");
            double res = ax.GetDouble("resolution", 0), sig = res / (2 * Math.Sqrt(2 * Math.Log(2)));
            var bw = ax.TryGet("binWidth", out var bv) ? bv : null;
            bool pixel = bw != null && bw.Kind == JsonKind.String && bw.AsString() == "pixel";
            double binFixed = bw != null && bw.Kind == JsonKind.Number ? Math.Abs(bw.AsDouble()) : 0;
            var tsort = target.OrderBy(v => v).ToArray();
            double Bin(int k)
            {
                if (!pixel) return binFixed;
                int j = Array.BinarySearch(tsort, target[k]); if (j < 0) j = ~j;
                double lo = j > 0 ? tsort[j] - tsort[j - 1] : double.NaN, hi = j < tsort.Length - 1 ? tsort[j + 1] - tsort[j] : double.NaN;
                return double.IsNaN(lo) ? (double.IsNaN(hi) ? 0 : hi) : double.IsNaN(hi) ? lo : 0.5 * (lo + hi);
            }
            // trapezoid weights of the simulated axis
            var dx = new double[n];
            for (int i = 0; i < n; i++) dx[i] = 0.5 * ((i < n - 1 ? fx[i + 1] - fx[i] : 0) + (i > 0 ? fx[i] - fx[i - 1] : 0));
            int outside = target.Count(t => t < fx[0] || t > fx[n - 1]);
            double[] Resample(double[] yraw)
            {
                var y = ord.Select(i => yraw[i]).ToArray();
                var r = new double[target.Length];
                for (int k = 0; k < target.Length; k++)
                {
                    double t = target[k], b = Bin(k);
                    if (t < fx[0] - b / 2 - 3 * sig || t > fx[n - 1] + b / 2 + 3 * sig) { r[k] = 0; continue; }
                    double reach = b / 2 + 4 * sig, num = 0, den = 0;
                    if (reach > 0)
                    {
                        int i0 = Array.BinarySearch(fx, t - reach); if (i0 < 0) i0 = ~i0; i0 = Math.Max(0, i0 - 1);
                        for (int i = i0; i < n && fx[i] <= t + reach; i++)
                        {
                            double u = fx[i] - t, w;
                            if (sig > 0 && b > 0) w = 0.5 * (Erf((u + b / 2) / (Math.Sqrt(2) * sig)) - Erf((u - b / 2) / (Math.Sqrt(2) * sig)));
                            else if (sig > 0) w = Math.Exp(-0.5 * u * u / (sig * sig));
                            else w = Math.Abs(u) <= b / 2 ? 1 : 0;
                            num += w * dx[i] * y[i]; den += w * dx[i];
                        }
                    }
                    r[k] = den > 0 ? num / den : Interpolation.Linear(fx, y, t);
                    if (t < fx[0] || t > fx[n - 1]) r[k] = den > 0 ? r[k] : 0;
                }
                return r;
            }
            var same = o.Arrays.Where(a => a != xa && a.Data.Length == n && (a.Shape == null || a.Shape.Length <= 1)).ToList();
            foreach (var a in same.Where(a => !a.Name.EndsWith("_db", StringComparison.Ordinal)))
            {
                var nd = Resample(a.Data);
                Replace(o, a, nd);
            }
            foreach (var a in same.Where(a => a.Name.EndsWith("_db", StringComparison.Ordinal)))
            {
                var lin = o.Arrays.FirstOrDefault(b => b.Name == a.Name.Substring(0, a.Name.Length - 3) && b.Data.Length == target.Length);
                double[] nd;
                if (lin != null) { double mx = Math.Max(lin.Data.Max(), 1e-300); nd = lin.Data.Select(v => 10 * Math.Log10(Math.Max(v / mx, 1e-30))).ToArray(); }
                else { var l = Resample(a.Data.Select(v => Math.Pow(10, v / 10)).ToArray()); double mx = Math.Max(l.Max(), 1e-300); double floor = a.Data.Min(); nd = l.Select(v => Math.Max(floor, 10 * Math.Log10(Math.Max(v / mx, 1e-30)))).ToArray(); }
                Replace(o, a, nd);
            }
            Replace(o, xa, (double[])target.Clone());
            var ci = CultureInfo.InvariantCulture;
            string desc = target.Length + " readings" + (ax.Has("values") ? " at the given points" : string.Format(ci, " from {0:G4} to {1:G4}", target.Min(), target.Max()));
            if (pixel) desc += ", each integrated over its pixel"; else if (binFixed > 0) desc += string.Format(ci, ", each integrated over {0:G3}", binFixed);
            if (res > 0) desc += string.Format(ci, ", resolution {0:G3} (FWHM)", res);
            o.Notes.Add("axis: " + desc + (outside > 0 ? " (" + outside + " outside the simulated range read zero)" : ""));
            if (Readouts.TryGetValue(o.Kind, out var ro)) ro.Remeasure?.Invoke(o);
        }

        private static void Replace(InstrumentOutput o, Serialization.NdArray a, double[] data)
        {
            int i = o.Arrays.IndexOf(a);
            o.Arrays[i] = new Serialization.NdArray(a.Name, data, data.Length) { Unit = a.Unit, Description = a.Description };
        }

        /// <summary>The detector's spectral response (quantum efficiency ...) and the photon-counting conversion, on the instruments with a wavelength axis.</summary>
        private static void ApplySpectralResponse(JsonObject d, InstrumentOutput o)
        {
            var resp = d["response"]; bool photons = d.GetString("counting", "energy").Equals("photons", StringComparison.OrdinalIgnoreCase);
            if ((resp == null || resp.IsNull) && !photons) return;
            var axis = o.Arrays.FirstOrDefault(a => a.Name.EndsWith("_wavelength_m", StringComparison.Ordinal));
            if (axis == null || !Readouts.TryGetValue(o.Kind, out var ro) || ro.Arrays.Length == 0) { o.Notes.Add("detector response / counting need a spectral instrument (a wavelength axis); ignored for a " + o.Kind); return; }
            double[] rw = null, rv = null;
            if (resp != null && !resp.IsNull)
            {
                var ro2 = resp.AsObject(); rw = ro2.GetDoubleArray("wavelengthsM"); rv = ro2.GetDoubleArray("values");
                if (rw == null || rv == null || rw.Length != rv.Length || rw.Length < 1) throw new InvalidOperationException("detector.response needs wavelengthsM and values of the same length.");
                var ord = Enumerable.Range(0, rw.Length).OrderBy(i => rw[i]).ToArray(); rw = ord.Select(i => rw[i]).ToArray(); rv = ord.Select(i => rv[i]).ToArray();
            }
            double lam0 = axis.Data.Length > 0 ? axis.Data[axis.Data.Length / 2] : 1;
            foreach (var suffix in ro.Arrays)
            {
                var a = o.Arrays.FirstOrDefault(x => x.Name.EndsWith("_" + suffix, StringComparison.Ordinal));
                if (a == null || a.Data.Length != axis.Data.Length) continue;
                for (int i = 0; i < a.Data.Length; i++)
                {
                    double f = 1.0;
                    if (photons) f *= axis.Data[i] / lam0;
                    if (rw != null) f *= Tools.Interp(rw, rv, axis.Data[i]);
                    a.Data[i] *= f;
                }
            }
            o.Notes.Add("detector: " + (photons ? "photon counting (energy spectrum x lambda / " + (lam0 * 1e9).ToString("F1", CultureInfo.InvariantCulture) + " nm)" : "") + (photons && rw != null ? ", " : "") + (rw != null ? "spectral response (" + rw.Length + " point" + (rw.Length == 1 ? "" : "s") + ")" : ""));
        }

        private static double Erf(double x)
        {
            // Abramowitz-Stegun 7.1.26 (|error| < 1.5e-7)
            double t = 1 / (1 + 0.3275911 * Math.Abs(x));
            double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return x >= 0 ? y : -y;
        }

        /// <summary>
        /// Resolves a detector preset: <c>"detector": "name"</c> (a run-spec "detectors" entry, or a JSON file), or <c>{"preset": name | file, ...overrides}</c>.
        /// The preset's <c>instrument</c> keys fill instrument parameters the instrument does not set (saturation, segments, noise floor ...); its
        /// <c>detector</c> object becomes the instrument's detector, with the instrument's own keys on top. Returns the spec unchanged without a preset.
        /// </summary>
        public static JsonObject ResolveDetector(JsonObject ispec, Specs.RunSpec run)
        {
            var dv = ispec["detector"];
            if (dv == null || dv.IsNull) return ispec;
            string name = null; JsonObject over = null;
            if (dv.Kind == JsonKind.String) name = dv.AsString();
            else if (dv.Kind == JsonKind.Object && dv.AsObject().Has("preset")) { over = dv.AsObject(); name = over.GetString("preset"); }
            else return ispec;
            JsonObject preset = run?.Detectors?.GetObject(name);
            if (preset == null)
            {
                string path = run?.BaseDirectory != null && !System.IO.Path.IsPathRooted(name) ? System.IO.Path.Combine(run.BaseDirectory, name) : name;
                if (System.IO.File.Exists(path)) preset = JsonValue.ParseFile(path).AsObject();
            }
            if (preset == null) throw new InvalidOperationException("detector preset \"" + name + "\" is neither in the run spec's \"detectors\" nor a file.");
            var res = new JsonObject();
            foreach (var kv in ispec) if (kv.Key != "detector") res.Set(kv.Key, kv.Value);
            var inst = preset.GetObject("instrument");
            if (inst != null) foreach (var kv in inst) if (!res.Has(kv.Key)) res.Set(kv.Key, kv.Value);
            var det = new JsonObject();
            var pd = preset.GetObject("detector");
            if (pd != null) foreach (var kv in pd) det.Set(kv.Key, kv.Value);
            if (over != null) foreach (var kv in over) if (kv.Key != "preset") det.Set(kv.Key, kv.Value);
            if (det.Count > 0) res.Set("detector", det);
            return res;
        }

        /// <summary>Apply the "detector" read-out model to the recorded arrays and readings.</summary>
        public static void ApplyDetector(JsonObject ispec, InstrumentOutput o)
        {
            var d = ispec.GetObject("detector");
            if (d == null) return;
            if (!Readouts.TryGetValue(o.Kind, out var ro) || (ro.Arrays.Length == 0 && ro.Scalars.Length == 0))
            {
                o.Notes.Add("\"detector\" is not used by a " + o.Kind + " (it has its own detector model or records no trace)");
                return;
            }
            ApplySpectralResponse(d, o);
            double cal = d.GetDouble("calibration", 1.0);
            double rel = d.GetDouble("noise", 0);
            double floor = d.Has("noiseFloor") ? d.GetDouble("noiseFloor", 0) : d.Has("dynamicRangeDb") ? Math.Pow(10, -d.GetDouble("dynamicRangeDb", 100) / 10) : 0;
            int bits = d.GetInt("bitDepth", 0);
            double satLevel = d.GetDouble("saturationLevel", 0);
            var rnd = new Random(d.GetInt("seed", 1));
            foreach (var suffix in ro.Arrays)
            {
                var a = o.Arrays.FirstOrDefault(x => x.Name.EndsWith("_" + suffix, StringComparison.Ordinal));
                if (a == null) continue;
                var y = a.Data; double peak = y.Length > 0 ? y.Max() : 0;
                for (int i = 0; i < y.Length; i++)
                {
                    double v = y[i] * (1 + rel * Gauss(rnd)) + floor * peak * Gauss(rnd);
                    y[i] = cal * v;
                }
                if (satLevel > 0) { double cap = satLevel * peak * cal; for (int i = 0; i < y.Length; i++) if (y[i] > cap) y[i] = cap; }
                if (bits > 0 && bits < 31)
                {
                    double fs = y.Max(); double levels = (1 << bits) - 1;
                    if (fs > 0) for (int i = 0; i < y.Length; i++) y[i] = Math.Round(Math.Max(0, Math.Min(1, y[i] / fs)) * levels) / levels * fs;
                }
            }
            foreach (var key in ro.Scalars)
            {
                if (!o.Values.TryGet(key, out var v) || v.Kind != JsonKind.Number) continue;
                Tools.Put(o, key, cal * v.AsDouble() * (1 + rel * Gauss(rnd)));
            }
            ro.Remeasure?.Invoke(o);
            var parts = new List<string>();
            if (cal != 1) parts.Add("calibration x" + cal.ToString("G4", CultureInfo.InvariantCulture));
            if (rel > 0) parts.Add("noise " + (rel * 100).ToString("G3", CultureInfo.InvariantCulture) + " % rms");
            if (floor > 0) parts.Add("floor " + floor.ToString("G3", CultureInfo.InvariantCulture) + " of the peak (" + (-10 * Math.Log10(floor)).ToString("G3", CultureInfo.InvariantCulture) + " dB)");
            if (satLevel > 0) parts.Add("saturation at " + satLevel.ToString("G3", CultureInfo.InvariantCulture) + " of the peak");
            if (bits > 0) parts.Add(bits + "-bit ADC");
            if (parts.Count > 0) o.Notes.Add("detector: " + string.Join(", ", parts) + "; readings re-evaluated on the detected trace");
        }

        private static double Gauss(Random r) { double u1 = 1 - r.NextDouble(), u2 = r.NextDouble(); return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }

        /// <summary>Every declared parameter of the instrument (and the common keys it uses) with the value in effect:
        /// the one given, or the documented default (a number when the default is numeric, otherwise its description).</summary>
        public static JsonObject Settings(JsonObject ispec, string kind)
        {
            var o = new JsonObject();
            var specs = new List<ComponentSpec>();
            var own = OptionRegistry.Get("instrument", kind);
            if (own != null) specs.Add(own);
            foreach (var p in specs.SelectMany(s => s.AllParams()))
            {
                if (ispec.TryGet(p.Name, out var v)) { o.Set(p.Name, v); continue; }
                if (p.Default == null) continue;
                o.Set(p.Name, p.Enum != null && p.Enum.Contains(p.Default.Trim('"')) ? (JsonValue)p.Default.Trim('"') : ParseDefault(p.Default));
            }
            foreach (var key in new[] { "sampling", "filter", "detector", "beam", "axis" })
                if (ispec.TryGet(key, out var v)) o.Set(key, v);
            return o;
        }

        private static JsonValue ParseDefault(string d)
        {
            var t = d.Trim();
            if (t.Length > 1 && t[0] == '"' && t[t.Length - 1] == '"') return t.Substring(1, t.Length - 2);
            if (t == "true") return true;
            if (t == "false") return false;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return x;
            return "default: " + t;
        }
    }
}
