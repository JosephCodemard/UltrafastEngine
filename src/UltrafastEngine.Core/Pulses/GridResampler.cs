using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Pulses
{
    /// <summary>
    /// Moves pulses between grids. Grids derived from one another by power-of-two changes of the time window and
    /// of the time step, with carriers on multiples of the frequency step, are related EXACTLY: padding/cropping
    /// in time, a carrier shift in time (exact sub-bin shift), and padding/cropping in frequency are all
    /// band-limited operations. Other grid pairs fall back to time-domain interpolation.
    /// </summary>
    public static class GridResampler
    {
        /// <summary>A grid with window x 2^windowPow2, time step x 2^dtPow2, and carrier near omega0 snapped to the new
        /// frequency step.</summary>
        public static FrequencyGrid Derive(FrequencyGrid g, int windowPow2, int dtPow2, double omega0)
        {
            double window = g.N * g.DTime * Math.Pow(2, windowPow2);
            double dt = g.DTime * Math.Pow(2, dtPow2);
            int n = (int)Math.Round(window / dt);
            if (n < 16 || (n & (n - 1)) != 0) throw new ArgumentException("derived grid would have N = " + n);
            double dw = 2 * Math.PI / window;
            return FrequencyGrid.Create(n, dt, Math.Round(omega0 / dw) * dw);
        }

        private static bool PowerOfTwoRatio(double ratio, out int k)
        {
            k = (int)Math.Round(Math.Log(ratio, 2));
            return Math.Abs(ratio - Math.Pow(2, k)) < 1e-9 * ratio;
        }

        /// <summary>Resample a spectrum (Complex[] on <paramref name="src"/>) to <paramref name="dst"/>. Returns the
        /// fraction of energy that fell outside the destination window or band.</summary>
        public static Complex[] Resample(Complex[] spectrum, FrequencyGrid src, FrequencyGrid dst, out double lostFraction)
        {
            double e0 = 0; for (int i = 0; i < spectrum.Length; i++) e0 += Norm(spectrum[i]);
            e0 = e0 / src.N * src.DTime;
            if (e0 == 0) { lostFraction = 0; return new Complex[dst.N]; }
            double wSrc = src.N * src.DTime, wDst = dst.N * dst.DTime;
            if (PowerOfTwoRatio(wDst / wSrc, out int a) && PowerOfTwoRatio(dst.DTime / src.DTime, out _))
                return Exact(spectrum, src, dst, a, e0, out lostFraction);
            return Interp(spectrum, src, dst, e0, out lostFraction);
        }

        private static double Norm(Complex c) => c.Real * c.Real + c.Imaginary * c.Imaginary;

        private static Complex[] Exact(Complex[] spectrum, FrequencyGrid src, FrequencyGrid dst, int a, double e0, out double lost)
        {
            var at = SpectralTransform.FreqToTime(spectrum);
            int n = src.N;
            // 1. window change at the source time step
            int n2 = a >= 0 ? n << a : n >> (-a);
            var bt = new Complex[n2];
            int off = n2 / 2 - n / 2; // index shift so t = 0 stays at the centre
            for (int i = 0; i < n; i++) { int j = i + off; if (j >= 0 && j < n2) bt[j] = at[i]; }
            // 2. carrier shift (exact in time): a'(t) = a(t) exp(+i delta t)
            double delta = dst.OmegaCenter - src.OmegaCenter;
            if (delta != 0)
                for (int j = 0; j < n2; j++) { double t = (j - n2 / 2) * src.DTime; bt[j] *= Complex.FromPolarCoordinates(1, delta * t); }
            // 3. time-step change: pad / crop the spectrum (same frequency step)
            var bs = SpectralTransform.TimeToFreq(bt);
            int nd = dst.N;
            var outS = new Complex[nd];
            double scale = (double)nd / n2; // = dt_src / dt_dst
            int off2 = nd / 2 - n2 / 2;
            for (int k = 0; k < n2; k++) { int j = k + off2; if (j >= 0 && j < nd) outS[j] = bs[k] * scale; }
            double e1 = 0; for (int j = 0; j < nd; j++) e1 += Norm(outS[j]);
            e1 = e1 / nd * dst.DTime;
            lost = Math.Max(0, 1 - e1 / e0);
            return outS;
        }

        /// <summary>Unrelated grids: the complex envelope, moved to the destination carrier
        /// (a'(t) = a(t) exp(i (w0' - w0) t)), is interpolated in time (cubic Catmull-Rom). Accurate when the source
        /// time step resolves the envelope and the carrier shift (true for pulses re-used at their own wavelength).</summary>
        private static Complex[] Interp(Complex[] spectrum, FrequencyGrid src, FrequencyGrid dst, double e0, out double lost)
        {
            var at = SpectralTransform.FreqToTime(spectrum);
            double delta = dst.OmegaCenter - src.OmegaCenter;
            int n = src.N;
            var b = new Complex[n];
            for (int i = 0; i < n; i++) b[i] = at[i] * Complex.FromPolarCoordinates(1, delta * src.Time[i]);
            var outT = new Complex[dst.N];
            for (int j = 0; j < dst.N; j++)
            {
                double x = dst.Time[j] / src.DTime + n / 2;
                int i1 = (int)Math.Floor(x);
                if (i1 < 1 || i1 >= n - 2) continue;
                double t = x - i1;
                Complex p0 = b[i1 - 1], p1 = b[i1], p2 = b[i1 + 1], p3 = b[i1 + 2];
                outT[j] = 0.5 * ((2.0 * p1) + (-p0 + p2) * t + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * (t * t) + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * (t * t * t));
            }
            var outS = SpectralTransform.TimeToFreq(outT);
            double e1 = 0; for (int j = 0; j < dst.N; j++) e1 += Norm(outS[j]);
            e1 = e1 / dst.N * dst.DTime;
            lost = Math.Max(0, 1 - e1 / e0);
            return outS;
        }

        public static PulseField Resample(PulseField p, FrequencyGrid dst, out double lostFraction)
        {
            if (ReferenceEquals(p.Grid, dst)) { lostFraction = 0; return p; }
            var ax = Resample(p.Ax, p.Grid, dst, out double lx);
            bool hasY = p.Ay.Any(c => c != Complex.Zero);
            double ly = 0;
            var ay = hasY ? Resample(p.Ay, p.Grid, dst, out ly) : new Complex[dst.N];
            double ex = 0, ey = 0;
            for (int i = 0; i < p.Grid.N; i++) { ex += Norm(p.Ax[i]); ey += Norm(p.Ay[i]); }
            lostFraction = ex + ey > 0 ? (lx * ex + ly * ey) / (ex + ey) : 0;
            return new PulseField(dst, ax, ay, p.RepRateHz) { Profile = p.Profile };
        }
    }
}
