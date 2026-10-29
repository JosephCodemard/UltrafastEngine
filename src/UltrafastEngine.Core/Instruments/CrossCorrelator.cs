using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Instruments
{
    /// <summary>
    /// Third-order cross-correlator, the standard high-dynamic-range contrast diagnostic: the pulse is frequency doubled
    /// to make a short, clean gate G(t) = |E(t)²|² (optionally through the SHG crystal's acceptance bandwidth), and the
    /// third harmonic of gate x pulse is detected as a function of delay:
    /// <code>S(τ) = ∫ I(t) G(t - τ) dt</code>
    /// so a feature of the pulse at +T (after the peak) appears at τ = +T. Because the gate is made from the pulse itself,
    /// a real post-pulse of relative intensity ε also leaves an artefact of order ε² at -T, as in real devices. The reading
    /// includes the detector noise floor (dynamic range 10^10 .. 10^12 typical), extra temporal resolution, the delay scan
    /// grid (coarse + fine steps) and the polarisation the device accepts.
    /// </summary>
    public sealed class CrossCorrelator : IInstrument
    {
        public string Kind => "crossCorrelator";

        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var p = ctx.Pulse; var s = ctx.Spec; var g = p.Grid; int n = g.N;
            // ---- the field the device accepts (input polariser)
            var ax = p.TemporalAx(); var ay = p.TemporalAy();
            // gate field: with a delayed-light tail the SHG gate comes from the main field only (the probe channel is linear
            // in intensity, so main + tail is right there)
            var gx = ctx.GatePulse != null ? ctx.GatePulse.TemporalAx() : ax; var gy = ctx.GatePulse != null ? ctx.GatePulse.TemporalAy() : ay;
            var e = new Complex[n];
            string pol = s.Has("polarization") ? (s["polarization"].Kind == JsonKind.Number ? s["polarization"].AsDouble().ToString(CultureInfo.InvariantCulture) : s.GetString("polarization", "total")) : "total";
            double[] I = new double[n];
            if (pol == "total")
            {
                for (int i = 0; i < n; i++) I[i] = ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary + ay[i].Real * ay[i].Real + ay[i].Imaginary * ay[i].Imaginary;
                // the SHG gate needs one field: use the dominant component (and note it)
                double ex = gx.Sum(z => z.Magnitude * z.Magnitude), ey = gy.Sum(z => z.Magnitude * z.Magnitude);
                e = ex >= ey ? gx : gy;
                if (Math.Min(ex, ey) > 1e-3 * Math.Max(ex, ey)) o.Notes.Add("pulse is not linearly polarised: the gate is made from the dominant component, the signal from the total intensity");
            }
            else
            {
                double ang = pol == "x" ? 0 : pol == "y" ? Math.PI / 2 : double.Parse(pol, CultureInfo.InvariantCulture) * Math.PI / 180;
                double c = Math.Cos(ang), sn = Math.Sin(ang);
                for (int i = 0; i < n; i++) { e[i] = c * gx[i] + sn * gy[i]; var pr = c * ax[i] + sn * ay[i]; I[i] = pr.Real * pr.Real + pr.Imaginary * pr.Imaginary; }
            }
            // ---- gate: second harmonic of the same field (through the crystal acceptance, if given)
            var e2 = new Complex[n];
            for (int i = 0; i < n; i++) e2[i] = e[i] * e[i];
            double acc = s.GetDouble("shgAcceptanceFwhmM", double.NaN);
            if (!double.IsNaN(acc) && acc > 0)
            {
                var f = SpectralTransform.TimeToFreq(e2);
                double lam2 = Math.PI * PhysicalConstants.SpeedOfLightMPerS / g.OmegaCenter;   // SH wavelength
                double dw = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS * acc / (lam2 * lam2);
                for (int i = 0; i < n; i++) f[i] *= Math.Exp(-2 * Math.Log(2) * Math.Pow(g.OmegaOffset[i] / dw, 2)) ;
                e2 = SpectralTransform.FreqToTime(f);
            }
            var G = e2.Select(z => z.Real * z.Real + z.Imaginary * z.Imaginary).ToArray();
            // ---- S(tau) = sum_t I(t) G(t - tau), linear correlation by zero-padded FFT
            int m = 2 * n;
            var fi = new Complex[m]; var fg = new Complex[m];
            for (int i = 0; i < n; i++) { fi[i] = I[i]; fg[i] = G[i]; }
            var FI = SpectralTransform.TimeToFreq(fi); var FG = SpectralTransform.TimeToFreq(fg);
            var prod = new Complex[m];
            for (int k = 0; k < m; k++) prod[k] = FI[k] * Complex.Conjugate(FG[k]);
            var corr = SpectralTransform.FreqToTime(prod);
            // corr is centred (FFT-shifted convention of SpectralTransform): find the lag axis by locating zero lag
            var lags = new double[m]; var S = new double[m];
            // SpectralTransform works on centred arrays: element index j <-> lag (j - m/2) dt
            for (int j = 0; j < m; j++) { lags[j] = (j - m / 2) * g.DTime; S[j] = Math.Max(0, corr[j].Real); }
            double smax = S.Max();
            if (!(smax > 0)) throw new InvalidOperationException("crossCorrelator: no signal");
            for (int j = 0; j < m; j++) S[j] /= smax;
            double res = s.GetDouble("resolutionS", 0);
            if (res > 0) S = Tools.GaussianSmooth(S, g.DTime, res);
            int jp = Array.IndexOf(S, S.Max());
            double tPeak = lags[jp];
            // ---- scan grid
            double floor = s.Has("noiseFloor") ? s.GetDouble("noiseFloor", 1e-11) : Math.Pow(10, -s.GetDouble("dynamicRangeDb", 110) / 10);
            var range = s.GetDoubleArray("rangeS") ?? new[] { lags[0] * 0.98, lags[m - 1] * 0.98 };
            if (range.Length == 1) range = new[] { -Math.Abs(range[0]), Math.Abs(range[0]) };
            double step = s.GetDouble("stepS", double.NaN);
            int points = s.GetInt("points", 2001);
            if (double.IsNaN(step)) step = (range[1] - range[0]) / Math.Max(1, points - 1);
            var delays = new List<double>();
            for (double t = range[0]; t <= range[1] + 1e-9 * step; t += step) delays.Add(t);
            double fineStep = s.GetDouble("fineStepS", double.NaN), fineRange = s.GetDouble("fineRangeS", double.NaN);
            if (!double.IsNaN(fineStep) && !double.IsNaN(fineRange))
            {
                for (double t = -fineRange; t <= fineRange; t += fineStep) delays.Add(t);
                delays = delays.Distinct().OrderBy(x => x).ToList();
            }
            double lo = lags[0], hi = lags[m - 1];
            int outside = delays.Count(x => x - tPeak < lo || x - tPeak > hi);
            if (outside > 0) o.Notes.Add(outside + " scan delays lie outside the simulated window (" + (lo * 1e12).ToString("G4", CultureInfo.InvariantCulture) + " .. " + (hi * 1e12).ToString("G4", CultureInfo.InvariantCulture) + " ps): reported at the noise floor; widen grid.windowS to simulate them");
            // ---- detection: signal + noise floor (positive detector background with fluctuations, seeded)
            bool noise = s.GetBool("noise", true);
            var rnd = new Random(s.GetInt("seed", 1));
            var meas = new double[delays.Count]; var ideal = new double[delays.Count];
            // generic detector: saturation and attenuation-range stitching (a list of delay ranges, each measured through its own
            // attenuation and divided by it afterwards -- the user configures them; no instrument-specific numbers are built in)
            double sat = s.GetDouble("saturationLevel", 0);
            var segs = new List<(double From, double To, double Att, double Floor)>();
            if (s["segments"] != null && s["segments"].Kind == JsonKind.Array)
                foreach (var sg in s["segments"].AsArray())
                {
                    var so = sg.AsObject();
                    double att = so.Has("attenuation") ? so.GetDouble("attenuation", 1) : so.Has("gain") ? so.GetDouble("gain", 1) : 1.0;
                    if (!(att > 0)) throw new InvalidOperationException("crossCorrelator: a segment's attenuation / gain must be positive.");
                    segs.Add((so.GetDouble("fromS", double.NegativeInfinity), so.GetDouble("toS", double.PositiveInfinity), att, so.Has("noiseFloor") ? so.GetDouble("noiseFloor", floor) : floor));
                }
            int nSat = 0;
            for (int k = 0; k < delays.Count; k++)
            {
                double x = delays[k] + tPeak;   // the scan is centred on the correlation peak (zero delay)
                double v = x < lo || x > hi ? 0 : Tools.Interp(lags, S, x);
                ideal[k] = v;
                double att = 1.0, fl = floor;
                foreach (var sg in segs) if (delays[k] >= sg.From && delays[k] <= sg.To) { att = sg.Att; fl = sg.Floor; break; }
                double nz = noise ? fl * Math.Abs(Gauss(rnd)) : fl;
                double reading = v * att + nz;
                if (sat > 0 && reading > sat) { reading = sat; nSat++; }
                meas[k] = reading / att;
            }
            if (sat > 0) o.Notes.Add(nSat + " of " + delays.Count + " scan points saturate the detector (clipped at " + sat.ToString("G3", CultureInfo.InvariantCulture) + " of the peak)");
            if (segs.Count > 0) o.Notes.Add("attenuation-range stitching: " + segs.Count + " segment(s); each reading is divided by its attenuation, so its noise floor is the detector floor / attenuation");
            var mmax = meas.Max();
            o.Add("delay_s", delays.ToArray(), "s", "delay (positive: after the main pulse)");
            o.Add("signal", meas.Select(v => v / mmax).ToArray(), "", "normalised third-order correlation (with noise floor)");
            o.Add("signal_db", meas.Select(v => 10 * Math.Log10(Math.Max(v / mmax, 1e-300))).ToArray(), "dB", "normalised signal");
            o.Add("signal_ideal", ideal, "", "correlation without noise or floor");
            Tools.Put(o, "noiseFloor", floor);
            Tools.Put(o, "dynamicRangeDb", -10 * Math.Log10(floor));
            Tools.Put(o, "fwhmS", PulseMetrics.Fwhm(lags, S));
            // contrast at requested delays and the strongest pre / post features
            foreach (var dv in s.GetDoubleArray("probeDelaysS") ?? new double[0])
            {
                double x = dv + tPeak;
                double v = x < lo || x > hi ? double.NaN : Tools.Interp(lags, S, x);
                Tools.Put(o, "contrastAt_" + (dv * 1e12).ToString("G4", CultureInfo.InvariantCulture).Replace("-", "m") + "ps_db", 10 * Math.Log10(Math.Max(v, floor)));
            }
            double win = s.GetDouble("windowS", 5 * PulseMetrics.Fwhm(lags, S));
            double pre = 0, post = 0, tpre = double.NaN, tpost = double.NaN;
            for (int k = 0; k < delays.Count; k++)
            {
                if (delays[k] < -win && ideal[k] > pre) { pre = ideal[k]; tpre = delays[k]; }
                if (delays[k] > win && ideal[k] > post) { post = ideal[k]; tpost = delays[k]; }
            }
            Tools.Put(o, "prePulseContrastDb", 10 * Math.Log10(Math.Max(pre, floor))); Tools.Put(o, "prePulseDelayS", tpre);
            Tools.Put(o, "postPulseContrastDb", 10 * Math.Log10(Math.Max(post, floor))); Tools.Put(o, "postPulseDelayS", tpost);
            o.Notes.Add("third-order correlation: a post-pulse at +T of relative intensity e also gives an artefact ~e^2 at -T (gate replica)");
        }

        private static double Gauss(Random r) { double u1 = 1 - r.NextDouble(), u2 = r.NextDouble(); return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }
    }
}
