using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;

namespace UltrafastEngine.Plugins.Gpu
{
    /// <summary>
    /// Interaction-picture Runge-Kutta integration on a device, the same schemes as the CPU
    /// (<see cref="Steppers.AdaptiveErk43Ip"/> and <see cref="Steppers.FixedRk4Ip"/>): identical stages, step control
    /// and B-integral accounting. Subclasses supply the nonlinear operator. Everything stays on the device; per step the
    /// host reads back only the error and peak partial results (a few hundred doubles).
    /// </summary>
    public abstract class DeviceStepper : IDisposable
    {
        protected readonly GpuStream S;
        protected readonly GpuContext C;
        protected readonly int Len, Rows, RowLen;
        protected readonly GpuBuffer L, Part;
        protected readonly double[] PartHost;
        protected readonly int GLen;
        private readonly List<GpuBuffer> _all = new List<GpuBuffer>();
        protected readonly double LengthM;

        /// <param name="rows">number of contiguous spectra of length <paramref name="rowLen"/> in the state</param>
        protected DeviceStepper(GpuStream s, int rows, int rowLen, Complex[] linearRate, double lengthM, int extraPart)
        {
            S = s; C = s.Context; Rows = rows; RowLen = rowLen; Len = rows * rowLen; LengthM = lengthM;
            GLen = C.Groups(Len);
            PartHost = new double[Math.Max(2 * GLen, extraPart)];
            Part = Buf(PartHost.Length * 8L);
            L = Buf(Len * 16L); S.Write(L, Natural(linearRate, rows));
        }

        protected GpuBuffer Buf(long bytes) { var b = C.Alloc(bytes); _all.Add(b); return b; }

        /// <summary>Centred engine order -> natural FFT order, per row of the given count (swap of halves).</summary>
        internal static Complex[] Natural(Complex[] centred, int rows)
        {
            int n = centred.Length / rows, h = n / 2;
            var r = new Complex[centred.Length];
            for (int c = 0; c < rows; c++)
                for (int j = 0; j < n; j++) r[c * n + j] = centred[c * n + (j + h) % n];
            return r;
        }

        internal static double[] Natural(double[] centred, int rows = 1)
        {
            int n = centred.Length / rows, h = n / 2;
            var r = new double[centred.Length];
            for (int c = 0; c < rows; c++)
                for (int j = 0; j < n; j++) r[c * n + j] = centred[c * n + (j + h) % n];
            return r;
        }

        /// <summary>rate = N(state) at z; k0 x peak index change when <paramref name="wantPeak"/> (else NaN).</summary>
        protected abstract double Nonlinear(GpuBuffer state, double z, GpuBuffer rate, bool wantPeak);
        protected virtual double AreaAt(double z) => 1.0;

        private void Exp(double h, GpuBuffer dst) => S.Launch("uf_exp", GLen, L, h, dst, Len);
        private void Mul(GpuBuffer a, GpuBuffer b, GpuBuffer dst) => S.Launch("uf_mul", GLen, a, b, dst, Len);
        private void Axpy(GpuBuffer y, double s, GpuBuffer x, GpuBuffer dst) => S.Launch("uf_axpy", GLen, y, s, x, dst, Len);

        private double AreaLimitedStep(double z, double h, double limit)
        {
            for (int it = 0; it < 40; it++)
            {
                double a0 = AreaAt(z), a1 = AreaAt(z + h), am = AreaAt(z + 0.5 * h);
                if (a0 <= 0 || a1 <= 0) return h;
                double v = Math.Abs(Math.Log(am / a0)) + Math.Abs(Math.Log(a1 / am));
                if (v <= limit) return h;
                h *= 0.5;
            }
            return h;
        }

        public Complex[] Propagate(Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress)
        {
            Complex[] r;
            if (string.Equals(st.Method, "rk4ip", StringComparison.OrdinalIgnoreCase) || string.Equals(st.Method, "fixed", StringComparison.OrdinalIgnoreCase))
                r = Fixed(a0, st, stats, progress);
            else r = Adaptive(a0, st, stats, progress);
            return Natural(r, Rows);   // the swap is its own inverse (even lengths)
        }

        private Complex[] Adaptive(Complex[] a0In, StepperSettings st, StepStatistics stats, Action<double> progress)
        {
            double Lm = LengthM;
            long bytes = Len * 16L;
            GpuBuffer a = Buf(bytes), kN = Buf(bytes), half = Buf(bytes), aI = Buf(bytes), k1 = Buf(bytes), k2 = Buf(bytes), k3 = Buf(bytes),
                      k4 = Buf(bytes), k5 = Buf(bytes), tmp = Buf(bytes), beta = Buf(bytes), a4 = Buf(bytes);
            S.Write(a, Natural(a0In, Rows));
            double pk = Nonlinear(a, 0, kN, true);
            double h = pk > 0 ? Math.Min(Lm, st.MaxNonlinearPhasePerStepRad / pk) : Lm;
            h = Math.Min(h, st.MaxStepM);
            double z = 0, lastReport = 0;
            int guard = 0;
            stats.Method = "erk4(3)-ip (adaptive, tol " + st.Tolerance.ToString("G2", CultureInfo.InvariantCulture) + ")";
            while (z < Lm * (1 - 1e-12))
            {
                if (++guard > st.MaxSteps) stats.Notes.Add("maximum step count reached at z = " + z.ToString("G4", CultureInfo.InvariantCulture) + " m; finished with the last step size");
                h = Math.Min(h, Lm - z);
                h = Math.Min(h, st.MaxStepM);
                h = AreaLimitedStep(z, h, st.MaxAreaChangePerStep);
                h = Math.Max(h, Math.Min(st.MinStepM, Lm - z));
                double zEnd = z + h;
                Exp(0.5 * h, half);
                Mul(half, a, aI);
                Mul(half, kN, k1);
                Axpy(aI, 0.5 * h, k1, tmp); Nonlinear(tmp, z + 0.5 * h, k2, false);
                Axpy(aI, 0.5 * h, k2, tmp); Nonlinear(tmp, z + 0.5 * h, k3, false);
                S.Launch("uf_axpy_mul", GLen, half, aI, h, k3, tmp, Len);
                Nonlinear(tmp, zEnd, k4, false);
                S.Launch("uf_rk_beta", GLen, half, aI, k1, k2, k3, h, beta, Len);
                S.Launch("uf_rk_final", GLen, beta, k4, h, a4, Len);
                double pk5 = Nonlinear(a4, zEnd, k5, true);
                S.Launch("uf_rk_err", GLen, beta, a4, k4, k5, h, Len, Part, 0);
                S.Read(Part, PartHost, 0, 2 * GLen);
                double num = 0, den = 0;
                for (int g = 0; g < GLen; g++) { num += PartHost[g]; den += PartHost[GLen + g]; }
                double err = den > 0 ? Math.Sqrt(num) / Math.Sqrt(den) : 0.0;
                bool forced = h <= st.MinStepM * 1.0000001 || guard > st.MaxSteps;
                if (err <= st.Tolerance || forced)
                {
                    stats.Accepted++;
                    stats.BIntegralRad += 0.5 * (pk + pk5) * h;
                    stats.MinStepM = Math.Min(stats.MinStepM, h); stats.MaxStepM = Math.Max(stats.MaxStepM, h);
                    z += h; pk = pk5;
                    { var t = a; a = a4; a4 = t; t = kN; kN = k5; k5 = t; }
                    if (forced && err > st.Tolerance && !stats.Notes.Contains("tolerance not met at minimum step")) stats.Notes.Add("tolerance not met at minimum step");
                    if (progress != null && (z / Lm - lastReport >= 0.005 || z >= Lm * (1 - 1e-12))) { lastReport = z / Lm; progress(Math.Min(1.0, z / Lm)); }
                }
                else stats.Rejected++;
                double factor = err > 0 ? 0.9 * Math.Pow(st.Tolerance / err, 0.25) : 2.0;
                h *= Math.Min(2.0, Math.Max(0.5, factor));
                if (guard > st.MaxSteps + 10) break;
            }
            var res = new Complex[Len];
            S.Read(a, res);
            return res;
        }

        private Complex[] Fixed(Complex[] a0In, StepperSettings st, StepStatistics stats, Action<double> progress)
        {
            long bytes = Len * 16L;
            GpuBuffer a = Buf(bytes), k = Buf(bytes), half = Buf(bytes), aI = Buf(bytes), k1 = Buf(bytes), k2 = Buf(bytes), k3 = Buf(bytes),
                      tmp = Buf(bytes), beta = Buf(bytes);
            S.Write(a, Natural(a0In, Rows));
            double peakRate = Nonlinear(a, 0, k, true);
            int steps = st.FixedSteps;
            if (steps <= 0)
            {
                double b = 2.0 * peakRate * LengthM;
                long fromB = (long)Math.Ceiling(b / Math.Max(1e-9, st.MaxNonlinearPhasePerStepRad));
                double logVar = 0; int samples = 1001; double prev = AreaAt(0);
                for (int i = 1; i < samples; i++) { double ar = AreaAt(LengthM * i / (samples - 1)); if (ar > 0 && prev > 0) logVar += Math.Abs(Math.Log(ar / prev)); prev = ar; }
                long fromA = (long)Math.Ceiling(logVar / Math.Max(1e-6, st.MaxAreaChangePerStep));
                long fromMax = double.IsInfinity(st.MaxStepM) ? 1 : (long)Math.Ceiling(LengthM / st.MaxStepM);
                steps = (int)Math.Min(st.MaxSteps, Math.Max(1, Math.Max(fromB, Math.Max(fromA, fromMax))));
            }
            double h = LengthM / steps;
            Exp(0.5 * h, half);
            int every = Math.Max(1, steps / 200);
            stats.Method = "rk4ip (fixed)";
            for (int s = 0; s < steps; s++)
            {
                double z = s * h;
                Mul(half, a, aI);
                double pk = Nonlinear(a, z, k, true);
                Mul(half, k, k1);
                Axpy(aI, 0.5 * h, k1, tmp); Nonlinear(tmp, z + 0.5 * h, k2, false);
                Axpy(aI, 0.5 * h, k2, tmp); Nonlinear(tmp, z + 0.5 * h, k3, false);
                S.Launch("uf_axpy_mul", GLen, half, aI, h, k3, tmp, Len);
                Nonlinear(tmp, z + h, k, false);
                S.Launch("uf_rk_beta", GLen, half, aI, k1, k2, k3, h, beta, Len);
                S.Launch("uf_rk_final", GLen, beta, k, h, a, Len);
                stats.Accepted++; stats.BIntegralRad += pk * h;
                stats.MinStepM = Math.Min(stats.MinStepM, h); stats.MaxStepM = Math.Max(stats.MaxStepM, h);
                if (progress != null && ((s + 1) % every == 0 || s == steps - 1)) progress((s + 1.0) / steps);
            }
            var res = new Complex[Len];
            S.Read(a, res);
            return res;
        }

        public virtual void Dispose()
        {
            foreach (var b in _all) b.Dispose();
            _all.Clear();
        }
    }

    /// <summary>
    /// The 1-D envelope problem (<see cref="EnvelopeProblem"/>: scalar or vector Kerr, scalar or vector Raman,
    /// self-steepening) on a device; the nonlinear operator mirrors EnvelopeProblem.NonlinearFast.
    /// </summary>
    public sealed class EnvelopeStepper : DeviceStepper
    {
        private readonly EnvelopeDeviceSpec _p;
        private readonly int _n, _comp, _nsig, _raman, _gN;
        private readonly GpuFft _fftState, _fftRaman;
        private readonly GpuBuffer _T, _C, _K, _Kb, _F, _P, _D;
        private readonly double _cc0;

        public EnvelopeStepper(GpuStream s, EnvelopeDeviceSpec p, bool allowVendorFft = true)
            : base(s, p.Scalar ? 1 : 2, p.N, p.LinearRate, p.LengthM, 4 * s.Context.Groups(p.N * (p.Scalar ? 1 : 2)) + 4)
        {
            _p = p;
            _n = p.N; _comp = Rows;
            bool ramanOn = p.RamanKernelFreq != null && p.RamanFraction > 0 && p.N2 != 0;
            _raman = !ramanOn ? 0 : (p.Scalar || p.RamanAnisoFreq == null ? 1 : 2);
            _nsig = _raman == 2 ? 5 : 1;
            _fftState = new GpuFft(s, _n, _comp, allowVendorFft);
            if (_raman > 0) _fftRaman = new GpuFft(s, _n, _nsig, allowVendorFft);
            _T = Buf(Len * 16L);
            _C = Buf(_nsig * _n * 16L);
            if (_raman > 0)
            {
                _K = Buf(_n * 16L); S.Write(_K, Natural(p.RamanKernelFreq, 1));
                _Kb = Buf(_n * 16L); S.Write(_Kb, Natural(_raman == 2 ? p.RamanAnisoFreq : p.RamanKernelFreq, 1));
            }
            else { _K = _C; _Kb = _C; }
            if (p.SelfSteepening)
            {
                var f = new double[_n];
                var om = Natural(p.OmegaOffset);
                for (int i = 0; i < _n; i++) f[i] = 1.0 + om[i] / p.OmegaCenter;
                _F = Buf(_n * 8L); S.Write(_F, f);
            }
            _P = Buf(_n * 8L); _D = Buf(_n * 8L);
            _gN = C.Groups(_n);
            _cc0 = p.N2 * (1 - p.RamanFraction) / 3.0;
        }

        /// <summary>Device memory this stepper needs for a problem (bytes), to check against the device size.</summary>
        public static long MemoryNeeded(EnvelopeDeviceSpec p)
        {
            long len = (long)p.N * (p.Scalar ? 1 : 2);
            long nsig = p.RamanAnisoFreq != null && !p.Scalar ? 5 : 1;
            return 16L * (len * 15 + nsig * p.N * 2 + 2 * p.N) + 8L * 3 * p.N;
        }

        protected override double AreaAt(double z) => _p.AreaAt(z);

        protected override double Nonlinear(GpuBuffer state, double z, GpuBuffer rate, bool wantPeak)
        {
            double aeff = _p.AreaAt(z), invA = 1.0 / aeff;
            int po = 2 * GLen;
            S.Copy(state, 0, _T, 0, 16L * Len);
            _fftState.Transform(_T, -1, true);                      // A(t)
            if (_raman > 0)
            {
                S.Launch("uf_env_raman_in", _gN, _T, _n, _p.Scalar ? 1 : 0, _nsig, invA, _C);
                _fftRaman.Transform(_C, +1, false);
                S.Launch("uf_env_raman_mul", C.Groups((long)_n * _nsig), _C, _K, _Kb, _n, _nsig);
                _fftRaman.Transform(_C, -1, true);
            }
            S.Launch("uf_env_kerr", _gN, _T, _C, _n, _p.Scalar ? 1 : 0, _raman, invA, _p.K0, _p.N2, _p.RamanFraction, _p.DTime,
                _cc0 / aeff, rate, _P, _D, Part, po);
            if (wantPeak) S.Launch("uf_peak", _gN, _P, _D, _n, Part, po, _gN, po + _gN);
            _fftState.Transform(rate, +1, false);
            if (_F != null) S.Launch("uf_scale_real", GLen, rate, _F, _n, Len);
            if (!wantPeak) return double.NaN;
            S.Read(Part, PartHost, po + _gN, _gN);
            double pk = 0;
            for (int g = 0; g < _gN; g++) pk = Math.Max(pk, PartHost[g]);
            return _p.K0 * pk;
        }

        public override void Dispose()
        {
            _fftState?.Dispose(); _fftRaman?.Dispose();
            base.Dispose();
        }
    }
}
