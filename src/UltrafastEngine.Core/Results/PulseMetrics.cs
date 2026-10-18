using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;

namespace UltrafastEngine.Results
{
    /// <summary>Scalar pulse characteristics computed from a <see cref="PulseField"/>.</summary>
    public sealed class PulseSummary
    {
        public double EnergyJ, PeakPowerW, FwhmS, RmsWidthS, TransformLimitFwhmS, SpectralFwhmM, CentroidWavelengthM, PeakWavelengthM,
            SpectralRmsWidthM, TimeBandwidthProduct, TemporalStrehl, PulseGddS2, PulseTodS3, EdgeEnergyFraction, CentroidTimeS,
            PolarizationDegree, S1, S2, S3;
    }

    public static class PulseMetrics
    {
        /// <summary>Full width at half maximum of y(x) with linear interpolation of the crossings (outermost crossings).</summary>
        public static double Fwhm(double[] x, double[] y)
        {
            int n = y.Length; if (n < 3) return double.NaN;
            int im = 0; for (int i = 1; i < n; i++) if (y[i] > y[im]) im = i;
            double half = 0.5 * y[im];
            if (half <= 0) return double.NaN;
            int l = 0; while (l < n && y[l] < half) l++;
            int r = n - 1; while (r >= 0 && y[r] < half) r--;
            if (l == 0 || r == n - 1 || l > r) return double.NaN;
            double xl = x[l - 1] + (half - y[l - 1]) / (y[l] - y[l - 1]) * (x[l] - x[l - 1]);
            double xr = x[r] + (half - y[r]) / (y[r + 1] - y[r]) * (x[r + 1] - x[r]);
            return Math.Abs(xr - xl);
        }

        public static (double Mean, double Rms) Moments(double[] x, double[] w)
        {
            double s = 0, m = 0;
            for (int i = 0; i < x.Length; i++) if (!double.IsNaN(x[i])) { s += w[i]; m += w[i] * x[i]; }
            if (s <= 0) return (double.NaN, double.NaN);
            m /= s;
            double v = 0;
            for (int i = 0; i < x.Length; i++) if (!double.IsNaN(x[i])) v += w[i] * (x[i] - m) * (x[i] - m);
            return (m, Math.Sqrt(v / s));
        }

        public static double[] TemporalPower(PulseField p)
        {
            var ax = p.TemporalAx(); var ay = p.TemporalAy();
            var r = new double[p.Grid.N];
            for (int i = 0; i < r.Length; i++) r[i] = ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary + ay[i].Real * ay[i].Real + ay[i].Imaginary * ay[i].Imaginary;
            return r;
        }

        /// <summary>Transform-limited temporal power (same spectrum, flat phase, both polarisations incoherently).</summary>
        public static double[] TransformLimitedPower(PulseField p)
        {
            int n = p.Grid.N;
            var mx = new Complex[n]; var my = new Complex[n];
            for (int i = 0; i < n; i++) { mx[i] = p.Ax[i].Magnitude; my[i] = p.Ay[i].Magnitude; }
            var tx = SpectralTransform.FreqToTime(mx);
            var ty = SpectralTransform.FreqToTime(my);
            var r = new double[n];
            for (int i = 0; i < n; i++) r[i] = tx[i].Real * tx[i].Real + tx[i].Imaginary * tx[i].Imaginary + ty[i].Real * ty[i].Real + ty[i].Imaginary * ty[i].Imaginary;
            return r;
        }

        /// <summary>Spectral power density per unit wavelength on the grid's wavelength axis (arbitrary scale,
        /// Jacobian included so it integrates to energy over lambda).</summary>
        public static double[] SpectrumPerWavelength(PulseField p, out double[] wavelengthM)
        {
            var s = p.SpectralIntensity();
            wavelengthM = p.Grid.WavelengthAxisM();
            var r = new double[s.Length];
            double c = PhysicalConstants.SpeedOfLightMPerS;
            for (int i = 0; i < s.Length; i++) r[i] = double.IsNaN(wavelengthM[i]) ? 0 : s[i] * 2 * Math.PI * c / (wavelengthM[i] * wavelengthM[i]);
            return r;
        }

        /// <summary>Fit phi(w) ~ a + b dw + GDD dw^2/2 + TOD dw^3/6 over the part of the spectrum above
        /// -10 dB of its peak, weighted by intensity (x polarisation, or y if x is empty).</summary>
        public static (double Gdd, double Tod) FitSpectralPhase(PulseField p)
        {
            var g = p.Grid;
            bool useX = p.Ax.Sum(c => c.Magnitude * c.Magnitude) >= p.Ay.Sum(c => c.Magnitude * c.Magnitude);
            var a = useX ? p.Ax : p.Ay;
            int n = g.N;
            var I = a.Select(c => c.Magnitude * c.Magnitude).ToArray();
            double pk = I.Max(); if (pk <= 0) return (double.NaN, double.NaN);
            // unwrap only across the significant region, starting at the peak
            int ip = Array.IndexOf(I, pk);
            int lo = ip, hi = ip;
            while (lo > 0 && I[lo - 1] > 0.1 * pk) lo--;
            while (hi < n - 1 && I[hi + 1] > 0.1 * pk) hi++;
            if (hi - lo < 4) return (double.NaN, double.NaN);
            var ph = new double[hi - lo + 1];
            ph[0] = a[lo].Phase;
            for (int i = lo + 1; i <= hi; i++)
            {
                double d = a[i].Phase - a[i - 1].Phase;
                d -= 2 * Math.PI * Math.Round(d / (2 * Math.PI));
                ph[i - lo] = ph[i - lo - 1] + d;
            }
            // weighted least squares for cubic in x = dw (scaled)
            double xs = g.DOmega * (hi - lo) / 2;
            var M = new double[4, 4]; var v = new double[4];
            for (int i = lo; i <= hi; i++)
            {
                double x = g.OmegaOffset[i] / xs, w = I[i];
                var b = new[] { 1, x, x * x, x * x * x };
                for (int r = 0; r < 4; r++) { v[r] += w * b[r] * ph[i - lo]; for (int c2 = 0; c2 < 4; c2++) M[r, c2] += w * b[r] * b[c2]; }
            }
            var sol = Solve4(M, v);
            if (sol == null) return (double.NaN, double.NaN);
            return (2 * sol[2] / (xs * xs), 6 * sol[3] / (xs * xs * xs));
        }

        private static double[] Solve4(double[,] A, double[] b)
        {
            int n = 4; var a = (double[,])A.Clone(); var x = (double[])b.Clone();
            for (int c = 0; c < n; c++)
            {
                int piv = c; for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
                if (Math.Abs(a[piv, c]) < 1e-300) return null;
                for (int k = 0; k < n; k++) { var t = a[c, k]; a[c, k] = a[piv, k]; a[piv, k] = t; }
                { var t = x[c]; x[c] = x[piv]; x[piv] = t; }
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue;
                    double f = a[r, c] / a[c, c];
                    for (int k = c; k < n; k++) a[r, k] -= f * a[c, k];
                    x[r] -= f * x[c];
                }
            }
            for (int i = 0; i < n; i++) x[i] /= a[i, i];
            return x;
        }

        public static PulseSummary Summarize(PulseField p)
        {
            var g = p.Grid;
            var s = new PulseSummary { EnergyJ = p.TotalEnergyJ() };
            var pt = TemporalPower(p);
            s.PeakPowerW = pt.Max();
            s.FwhmS = Fwhm(g.Time, pt);
            var (tm, trms) = Moments(g.Time, pt);
            s.CentroidTimeS = tm; s.RmsWidthS = trms;
            var tl = TransformLimitedPower(p);
            s.TransformLimitFwhmS = Fwhm(g.Time, tl);
            s.TemporalStrehl = tl.Max() > 0 ? s.PeakPowerW / tl.Max() : double.NaN;
            var spec = p.SpectralIntensity();
            // spectral FWHM in frequency, converted to wavelength at the centroid
            double dwFwhm = Fwhm(g.OmegaAbsolute, spec);
            var (wm, wrms) = Moments(g.OmegaAbsolute, spec);
            double c = PhysicalConstants.SpeedOfLightMPerS;
            s.CentroidWavelengthM = 2 * Math.PI * c / wm;
            s.SpectralFwhmM = 2 * Math.PI * c * dwFwhm / (wm * wm);
            s.SpectralRmsWidthM = 2 * Math.PI * c * wrms / (wm * wm);
            int ipk = Array.IndexOf(spec, spec.Max());
            s.PeakWavelengthM = 2 * Math.PI * c / g.OmegaAbsolute[ipk];
            s.TimeBandwidthProduct = s.FwhmS * dwFwhm / (2 * Math.PI);
            var (gdd, tod) = FitSpectralPhase(p);
            s.PulseGddS2 = gdd; s.PulseTodS3 = tod;
            // energy in the outer 5 % of the window on each side: wrap-around warning
            int edge = Math.Max(1, g.N / 20); double tot = pt.Sum(), e = 0;
            for (int i = 0; i < edge; i++) e += pt[i] + pt[g.N - 1 - i];
            s.EdgeEnergyFraction = tot > 0 ? e / tot : 0;
            // Stokes (spectrally integrated)
            double S0 = 0, S1 = 0, S2 = 0, S3 = 0;
            for (int i = 0; i < g.N; i++)
            {
                var x = p.Ax[i]; var y = p.Ay[i];
                double xx = x.Real * x.Real + x.Imaginary * x.Imaginary, yy = y.Real * y.Real + y.Imaginary * y.Imaginary;
                var xy = x * Complex.Conjugate(y);
                S0 += xx + yy; S1 += xx - yy; S2 += 2 * xy.Real; S3 += 2 * xy.Imaginary; // S3 = +1 for the "rcp" input
            }
            if (S0 > 0) { s.S1 = S1 / S0; s.S2 = S2 / S0; s.S3 = S3 / S0; s.PolarizationDegree = Math.Sqrt(s.S1 * s.S1 + s.S2 * s.S2 + s.S3 * s.S3); }
            return s;
        }

        /// <summary>Index range [lo, hi] holding all but 'fraction' of the power (for cropping exports).</summary>
        public static (int Lo, int Hi) SupportRange(double[] y, double fraction = 1e-6, int pad = 8)
        {
            double tot = y.Sum(); if (tot <= 0) return (0, y.Length - 1);
            double acc = 0; int lo = 0;
            for (; lo < y.Length; lo++) { acc += y[lo]; if (acc > fraction * tot / 2) break; }
            acc = 0; int hi = y.Length - 1;
            for (; hi >= 0; hi--) { acc += y[hi]; if (acc > fraction * tot / 2) break; }
            lo = Math.Max(0, lo - pad); hi = Math.Min(y.Length - 1, hi + pad);
            if (hi <= lo) return (0, y.Length - 1);
            return (lo, hi);
        }
    }
}
