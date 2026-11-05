using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Pulses
{
    /// <summary>
    /// Contrast-degrading components of an input pulse ("pedestal" in the run spec's pulse), applied after the main
    /// pulse is built and scaled (energyJ / peakPowerW refer to the main pulse):
    /// <list type="bullet">
    /// <item><c>replica</c> / <c>prePulse</c> / <c>postPulse</c>: a coherent copy of the main pulse, delayed by delayS
    /// (prePulse: earlier), with relativeIntensity (peak intensity ratio) or relativeEnergy, phaseRad and extra
    /// gddFs2.</item>
    /// <item><c>ase</c>: incoherent amplified spontaneous emission, a random field with the pulse's spectrum (or a
    /// Gaussian of bandwidthFwhmM) under a long envelope of durationS, of mean intensity relativeIntensity x the main
    /// peak (or energyFraction of the main energy spread over durationS). Not compressible.</item>
    /// <item><c>phaseNoise</c>: random spectral phase (rmsRad) and amplitude (amplitudeRms) noise correlated over
    /// correlationNm (or correlationRadPerS): the coherent pedestal of stretcher / grating / spatial-light-modulator
    /// imperfections.</item>
    /// <item><c>exponential</c>: an incoherent pedestal level x exp(-|t|/riseS) before and exp(-t/fallS) after the
    /// peak -- a phenomenological fit form for measured contrast curves.</item>
    /// <item><c>measured</c>: an incoherent pedestal whose intensity follows a recorded contrast trace (csv, or timesS and
    /// relativeIntensities), with the pulse's spectrum; rows near t = 0 can be excluded (excludeWithinS).</item>
    /// </list>
    /// Random components use a seed (default 1) and are reproducible.
    /// </summary>
    public static class Pedestals
    {
        /// <summary>One photon per frequency mode with a random phase (the quantum-noise seed of supercontinuum and
        /// parametric generation; pyNLO's "one_photon_freq"): |A_k|^2 dt / N = hbar omega_k in each bin.</summary>
        public static PulseField QuantumNoise(PulseField p, JsonObject spec)
        {
            if (spec.GetString("kind", "onePhotonPerMode").Equals("none", StringComparison.OrdinalIgnoreCase)) return p;
            var g = p.Grid; int n = g.N;
            var rnd = new Random(spec.GetInt("seed", 1));
            bool both = spec.GetString("polarization", "same").Equals("both", StringComparison.OrdinalIgnoreCase);
            var ax = (Complex[])p.Ax.Clone(); var ay = (Complex[])p.Ay.Clone();
            double ex = ax.Sum(c => c.Magnitude * c.Magnitude), ey = ay.Sum(c => c.Magnitude * c.Magnitude);
            double fx = both ? 1 : ex / Math.Max(ex + ey, 1e-300), fy = both ? 1 : ey / Math.Max(ex + ey, 1e-300);
            const double hbar = 1.054571817e-34;
            for (int i = 0; i < n; i++)
            {
                double w = g.OmegaAbsolute[i];
                if (w <= 0) continue;
                double a = Math.Sqrt(hbar * w * n / g.DTime);
                if (fx > 0) ax[i] += Complex.FromPolarCoordinates(a * Math.Sqrt(fx), 2 * Math.PI * rnd.NextDouble());
                if (fy > 0) ay[i] += Complex.FromPolarCoordinates(a * Math.Sqrt(fy), 2 * Math.PI * rnd.NextDouble());
            }
            return new PulseField(g, ax, ay, p.RepRateHz) { Profile = p.Profile };
        }

        public static PulseField Apply(PulseField p, IEnumerable<JsonObject> components, JonesVector pol)
        {
            var g = p.Grid; int n = g.N;
            // the main pulse's scalar spectrum (along the polarisation vector)
            var a = new Complex[n];
            for (int i = 0; i < n; i++) a[i] = Complex.Conjugate(pol.Ex) * p.Ax[i] + Complex.Conjugate(pol.Ey) * p.Ay[i];
            var main = (Complex[])a.Clone();
            double peak = p.PeakPowerW(), energy = p.TotalEnergyJ();
            var list = components.ToList();
            // 1) phase / amplitude noise acts on the main pulse
            foreach (var c in list.Where(x => x.GetString("kind", "").Equals("phaseNoise", StringComparison.OrdinalIgnoreCase)))
            {
                double rms = c.GetDouble("rmsRad", 0), arms = c.GetDouble("amplitudeRms", 0);
                double corr = c.GetDouble("correlationRadPerS", double.NaN);
                if (double.IsNaN(corr) && c.Has("correlationNm"))
                {
                    double lam = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / g.OmegaCenter;
                    corr = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS * c.GetDouble("correlationNm", 0) * 1e-9 / (lam * lam);
                }
                if (double.IsNaN(corr)) corr = 10 * g.DOmega;
                var rnd = new Random(c.GetInt("seed", 1));
                var ph = CorrelatedNoise(n, g.DOmega, corr, rnd, rms);
                var am = CorrelatedNoise(n, g.DOmega, corr, rnd, arms);
                for (int i = 0; i < n; i++) main[i] *= Math.Max(0, 1 + am[i]) * Complex.FromPolarCoordinates(1, ph[i]);
            }
            var total = (Complex[])main.Clone();
            var specAmp = a.Select(z => z.Magnitude).ToArray();
            double sMax = specAmp.Max();
            foreach (var c in list)
            {
                string k = c.GetString("kind", "").ToLowerInvariant();
                switch (k)
                {
                    case "replica": case "prepulse": case "postpulse":
                    {
                        double delay = Math.Abs(c.GetDouble("delayS", 0)) * (k == "prepulse" ? -1 : k == "postpulse" ? 1 : Math.Sign(c.GetDouble("delayS", 0)));
                        double fieldFactor;
                        if (c.Has("relativeEnergy")) fieldFactor = Math.Sqrt(c.GetDouble("relativeEnergy", 0));
                        else fieldFactor = Math.Sqrt(c.GetDouble("relativeIntensity", 1e-6));
                        double phi = c.GetDouble("phaseRad", 0), gdd = c.GetDouble("gddFs2", 0) * 1e-30 + c.GetDouble("gddS2", 0);
                        for (int i = 0; i < n; i++)
                        {
                            double d = g.OmegaOffset[i];
                            // a delay tau appears as exp(+i d tau) in this engine's convention (positive dphi/dw = later)
                            total[i] += fieldFactor * a[i] * Complex.FromPolarCoordinates(1, d * delay + phi + gdd * d * d / 2);
                        }
                        break;
                    }
                    case "ase": case "exponential": case "measured":
                    {
                        var rnd = new Random(c.GetInt("seed", 1));
                        // temporal envelope of the intensity (relative)
                        var env = new double[n];
                        double level;
                        if (k == "ase")
                        {
                            double dur = c.GetDouble("durationS", 10e-9);
                            for (int i = 0; i < n; i++) { double t = g.Time[i] - c.GetDouble("delayS", 0); env[i] = Math.Exp(-Math.Pow(2 * t / dur, 12) * Math.Log(2)); }
                            level = c.Has("energyFraction") ? c.GetDouble("energyFraction", 0) * energy / dur / Math.Max(peak, 1e-300) : c.GetDouble("relativeIntensity", 1e-8);
                        }
                        else if (k == "measured")
                        {
                            MeasuredEnvelope(c, g.Time, env);
                            level = 1;
                        }
                        else
                        {
                            double rise = c.GetDouble("riseS", 1e-12), fall = c.GetDouble("fallS", rise);
                            for (int i = 0; i < n; i++) { double t = g.Time[i]; env[i] = t < 0 ? Math.Exp(t / rise) : Math.Exp(-t / fall); }
                            level = c.GetDouble("level", c.GetDouble("relativeIntensity", 1e-6));
                        }
                        // white complex Gaussian noise under the envelope, filtered with the spectral shape
                        var e = new Complex[n];
                        for (int i = 0; i < n; i++) e[i] = Math.Sqrt(env[i]) * Gauss(rnd);
                        var f = SpectralTransform.TimeToFreq(e);
                        double[] shape;
                        if (c.Has("bandwidthFwhmM") || c.Has("bandwidthFwhmNm"))
                        {
                            double lam = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / g.OmegaCenter;
                            double bw = c.GetDouble("bandwidthFwhmM", c.GetDouble("bandwidthFwhmNm", 0) * 1e-9);
                            double lc = c.GetDouble("centerWavelengthM", lam);
                            double wc = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / lc, dw = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS * bw / (lc * lc);
                            shape = g.OmegaAbsolute.Select(w => Math.Exp(-2 * Math.Log(2) * Math.Pow((w - wc) / dw, 2))).ToArray();   // field amplitude: intensity FWHM dw
                        }
                        else shape = specAmp.Select(v => sMax > 0 ? v / sMax : 0).ToArray();
                        for (int i = 0; i < n; i++) f[i] *= shape[i];
                        var et = SpectralTransform.FreqToTime(f);
                        if (k == "measured")
                        {
                            // intensity(t) = trace(t) x main peak on average: the filter removes the fraction 1 - mean(shape^2) of white noise
                            double sh2 = shape.Average(v => v * v);
                            double scm = sh2 > 0 ? Math.Sqrt(peak / sh2) : 0;
                            for (int i = 0; i < n; i++) total[i] += scm * f[i];
                            break;
                        }
                        // normalise: mean intensity where the envelope is ~1 equals level x peak
                        double s = 0, w8 = 0;
                        for (int i = 0; i < n; i++) if (env[i] > 0.5) { s += et[i].Real * et[i].Real + et[i].Imaginary * et[i].Imaginary; w8++; }
                        if (w8 == 0 || s == 0) break;
                        double sc = Math.Sqrt(level * peak / (s / w8));
                        for (int i = 0; i < n; i++) total[i] += sc * f[i];
                        break;
                    }
                }
            }
            var ax = new Complex[n]; var ay = new Complex[n];
            // the part of the original pulse orthogonal to pol is kept as it was
            for (int i = 0; i < n; i++)
            {
                var orthX = p.Ax[i] - a[i] * pol.Ex; var orthY = p.Ay[i] - a[i] * pol.Ey;
                ax[i] = total[i] * pol.Ex + orthX; ay[i] = total[i] * pol.Ey + orthY;
            }
            return new PulseField(g, ax, ay, p.RepRateHz) { Profile = p.Profile };
        }

        /// <summary>The measured trace (intensity relative to the main peak) on the grid's times: log-linear between points, zero or the
        /// end values outside, points within excludeWithinS of t = 0 ignored.</summary>
        private static void MeasuredEnvelope(JsonObject c, double[] time, double[] env)
        {
            var t = c.GetDoubleArray("timesS"); var v = c.GetDoubleArray("relativeIntensities");
            double ex = c.GetDouble("excludeWithinS", 0);
            bool hold = c.GetString("outside", "zero").Equals("hold", StringComparison.OrdinalIgnoreCase);
            var pts = Enumerable.Range(0, t.Length).Where(i => Math.Abs(t[i]) >= ex && v[i] > 0 && !double.IsNaN(v[i]) && !double.IsNaN(t[i]))
                                .OrderBy(i => t[i]).Select(i => (t: t[i], lv: Math.Log(v[i]))).ToList();
            if (pts.Count < 2) throw new ArgumentException("pedestal measured: fewer than two usable trace points");
            for (int i = 0; i < time.Length; i++)
            {
                double x = time[i];
                if (x < pts[0].t || x > pts[pts.Count - 1].t) { env[i] = hold ? Math.Exp(x < pts[0].t ? pts[0].lv : pts[pts.Count - 1].lv) : 0; continue; }
                int lo = 0, hi = pts.Count - 1;
                while (hi - lo > 1) { int mid = (lo + hi) / 2; if (pts[mid].t <= x) lo = mid; else hi = mid; }
                double f = pts[hi].t > pts[lo].t ? (x - pts[lo].t) / (pts[hi].t - pts[lo].t) : 0;
                env[i] = Math.Exp(pts[lo].lv + f * (pts[hi].lv - pts[lo].lv));
            }
        }

        private static double Gauss(Random r) { double u1 = 1 - r.NextDouble(), u2 = r.NextDouble(); return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }

        /// <summary>Real Gaussian noise of the given rms, correlated over corrW (Gaussian correlation function).</summary>
        public static double[] CorrelatedNoise(int n, double dOmega, double corrW, Random rnd, double rms)
        {
            var x = new double[n];
            if (rms == 0) return x;
            for (int i = 0; i < n; i++) x[i] = Gauss(rnd);
            // smooth with a Gaussian kernel of width corrW
            double sig = Math.Max(corrW / dOmega, 1e-9);
            int half = (int)Math.Ceiling(3 * sig);
            var k = new double[2 * half + 1];
            for (int j = -half; j <= half; j++) k[j + half] = Math.Exp(-0.5 * j * j / (sig * sig));
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int j = -half; j <= half; j++) { int m = i + j; if (m < 0 || m >= n) continue; s += k[j + half] * x[m]; }
                y[i] = s;
            }
            double mean = y.Average(), v = y.Average(z => (z - mean) * (z - mean));
            double sc = v > 0 ? rms / Math.Sqrt(v) : 0;
            for (int i = 0; i < n; i++) y[i] = (y[i] - mean) * sc;
            return y;
        }
    }
}
