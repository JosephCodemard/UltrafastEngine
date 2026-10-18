using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Models;
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
            double cal = d.GetDouble("calibration", 1.0);
            double rel = d.GetDouble("noise", 0);
            double floor = d.Has("noiseFloor") ? d.GetDouble("noiseFloor", 0) : d.Has("dynamicRangeDb") ? Math.Pow(10, -d.GetDouble("dynamicRangeDb", 100) / 10) : 0;
            int bits = d.GetInt("bitDepth", 0);
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
            foreach (var key in new[] { "sampling", "filter", "detector", "beam" })
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
