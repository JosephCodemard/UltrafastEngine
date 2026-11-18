// EnsembleStepper.cs -- many 1-D envelope problems (study points) integrated in one set of kernel launches.
//
// Every member is integrated with the adaptive ERK4(3)-IP scheme exactly as DeviceStepper.Adaptive does it alone: its own step size,
// error control, accept / reject, B-integral and statistics; the device only does the arithmetic of all members in the same launches
// (component-major layout, see Kernels "ensembles"). Per step the host writes one small parameter block (step sizes, 1 / Aeff and the
// coherent coupling at the stage positions, the accept mask) and reads one (errors, norms, peaks): 2 writes + 1 read per step for the
// whole ensemble instead of per member. Members whose propagation has finished take zero-length steps until the last one is done.
//
// Members must share N, scalar / vector, the Raman and orientational kernels, self-steepening, the grid and the stepper settings
// (GpuBackend.EnsembleKey); they may differ in the linear operator, length, beam area, k0, n2 and fR. Extra time-domain responses
// (user kernels) run alone.
//
// Classes: EnsembleStepper (MemoryNeeded(spec, members), Propagate(jobs))
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;

namespace UltrafastEngine.Plugins.Gpu
{
    public sealed class EnsembleStepper : IDisposable
    {
        private readonly GpuStream S;
        private readonly GpuContext C;
        private readonly EnvelopeDeviceSpec[] _p;
        private readonly int _B, _n, _comp, _block, _tot, _nsig, _raman, _rot, _nrot, _m, _gTot, _gBlock;
        private readonly bool _scalar;
        private readonly GpuFft _fftState, _fftRaman, _fftRot;
        private readonly GpuBuffer _L, _T, _Cr, _K, _Kb, _Q, _Kr, _F, _P, _D, _prm, _mc, _out;
        private readonly double[] _prmHost, _outHost, _cc0;
        private readonly List<GpuBuffer> _all = new List<GpuBuffer>();
        // offsets in the parameter block: h, then (1 / Aeff, coherent coupling) at the stages z, z + h / 2, z + h, then the mask
        private int OffInv(int stage) => _B * (1 + 2 * stage);
        private int OffCc(int stage) => _B * (2 + 2 * stage);
        private int OffMask => _B * 7;

        public EnsembleStepper(GpuStream s, IList<EnvelopeDeviceSpec> specs, bool allowVendorFft = true)
        {
            S = s; C = s.Context; _p = new EnvelopeDeviceSpec[specs.Count]; specs.CopyTo(_p, 0);
            var p0 = _p[0];
            _B = _p.Length; _n = p0.N; _scalar = p0.Scalar; _comp = _scalar ? 1 : 2;
            _block = _B * _n; _tot = _comp * _block;
            bool ramanOn = p0.RamanKernelFreq != null && p0.RamanFraction > 0 && p0.N2 != 0;
            _raman = !ramanOn ? 0 : (_scalar || p0.RamanAnisoFreq == null ? 1 : 2);
            _nsig = _raman == 2 ? 5 : 1;
            _gTot = C.Groups(_tot); _gBlock = C.Groups(_block);
            _fftState = new GpuFft(s, _n, _comp * _B, allowVendorFft);
            if (_raman > 0) _fftRaman = new GpuFft(s, _n, _nsig * _B, allowVendorFft);
            // linear operator of every member, component-major
            var lin = new Complex[_tot];
            for (int b = 0; b < _B; b++)
            {
                var nat = DeviceStepper.Natural(_p[b].LinearRate, _comp);
                for (int c = 0; c < _comp; c++) Array.Copy(nat, c * _n, lin, (c * _B + b) * _n, _n);
            }
            _L = Buf(_tot * 16L); S.Write(_L, lin);
            _T = Buf(_tot * 16L);
            _Cr = Buf((long)_nsig * _block * 16L);
            if (_raman > 0)
            {
                _K = Buf(_n * 16L); S.Write(_K, DeviceStepper.Natural(p0.RamanKernelFreq, 1));
                _Kb = Buf(_n * 16L); S.Write(_Kb, DeviceStepper.Natural(_raman == 2 ? p0.RamanAnisoFreq : p0.RamanKernelFreq, 1));
            }
            else { _K = _Cr; _Kb = _Cr; }
            if (p0.RotationalKernelFreq != null)
            {
                _rot = _scalar ? 1 : 2; _nrot = _scalar ? 1 : 3; _m = p0.RotationalKernelFreq.Length;
                _fftRot = new GpuFft(s, _m, _nrot * _B, allowVendorFft);
                _Q = Buf((long)_nrot * _B * _m * 16L);
                _Kr = Buf(_m * 16L); S.Write(_Kr, p0.RotationalKernelFreq);
            }
            else { _Q = _Cr; _m = 1; }
            if (p0.SelfSteepening)
            {
                var f = new double[_n];
                var om = DeviceStepper.Natural(p0.OmegaOffset);
                for (int i = 0; i < _n; i++) f[i] = 1.0 + om[i] / p0.OmegaCenter;
                _F = Buf(_n * 8L); S.Write(_F, f);
            }
            _P = Buf(_block * 8L); _D = Buf(_block * 8L);
            var mc = new double[3 * _B]; _cc0 = new double[_B];
            for (int b = 0; b < _B; b++) { mc[b] = _p[b].K0; mc[_B + b] = _p[b].N2; mc[2 * _B + b] = _p[b].RamanFraction; _cc0[b] = _p[b].N2 * (1 - _p[b].RamanFraction) / 3.0; }
            _mc = Buf(8L * mc.Length); S.Write(_mc, mc);
            _prmHost = new double[8 * _B]; _prm = Buf(8L * _prmHost.Length);
            _outHost = new double[3 * _B]; _out = Buf(8L * _outHost.Length);
        }

        private GpuBuffer Buf(long bytes) { var b = C.Alloc(bytes); _all.Add(b); return b; }

        /// <summary>Device memory for <paramref name="members"/> problems like <paramref name="p"/> (bytes).</summary>
        public static long MemoryNeeded(EnvelopeDeviceSpec p, int members)
        {
            long len = (long)p.N * (p.Scalar ? 1 : 2) * members, block = (long)p.N * members;
            long nsig = p.RamanAnisoFreq != null && !p.Scalar ? 5 : 1;
            long rot = p.RotationalKernelFreq == null ? 0 : (long)p.RotationalKernelFreq.Length * (p.Scalar ? 1 : 3) * members;
            return 16L * (len * 14 + nsig * block + rot + 3L * p.N) + 8L * (2 * block + 12L * members);
        }

        /// <summary>Set the stage values (1 / Aeff and cc0 / Aeff) of member b at position z.</summary>
        private void Stage(int b, int stage, double z)
        {
            double aeff = _p[b].AreaAt(z);
            _prmHost[OffInv(stage) + b] = 1.0 / aeff;
            _prmHost[OffCc(stage) + b] = _cc0[b] / aeff;
        }

        /// <summary>rate = N(state) for every member at the stage positions; with <paramref name="peakOffset"/> &gt;= 0 the
        /// members' peaks (k0 x peak index change) go to out[peakOffset + b].</summary>
        private void Nonlinear(GpuBuffer state, int stage, GpuBuffer rate, int peakOffset)
        {
            S.Copy(state, 0, _T, 0, 16L * _tot);
            _fftState.Transform(_T, -1, true);
            if (_raman > 0)
            {
                S.Launch("uf_sp_raman_in", _gBlock, _T, _block, _scalar ? 0 : 1, _nsig, _Cr);
                _fftRaman.Transform(_Cr, +1, false);
                S.Launch("uf_sp_raman_mul", C.Groups((long)_block * _nsig), _Cr, _K, _Kb, _n, _block, _nsig);
                _fftRaman.Transform(_Cr, -1, true);
            }
            if (_rot > 0)
            {
                S.Launch("uf_rot_in", C.Groups((long)_B * _m), _T, _n, _B, _block, _scalar ? 1 : 0, 1.0, _m, _Q);
                _fftRot.Transform(_Q, -1, false);
                S.Launch("uf_rot_mul", C.Groups((long)_m * _nrot * _B), _Q, _Kr, _m, _nrot * _B);
                _fftRot.Transform(_Q, +1, true);
            }
            S.Launch("uf_ens_kerr", _gBlock, _T, _Cr, _Q, _n, _B, _scalar ? 1 : 0, _raman, _rot, _m, _prm, OffInv(stage), OffCc(stage), _mc, _p[0].DTime, rate, _P, _D);
            if (peakOffset >= 0) S.Launch("uf_ens_peak", Math.Min(_B, C.MaxGroups), _P, _D, _n, _B, _mc, _out, peakOffset);
            _fftState.Transform(rate, +1, false);
            if (_F != null) S.Launch("uf_scale_real", _gTot, rate, _F, _n, _tot);
        }

        private static double AreaLimitedStep(Func<double, double> area, double z, double h, double limit)
        {
            for (int it = 0; it < 40; it++)
            {
                double a0 = area(z), a1 = area(z + h), am = area(z + 0.5 * h);
                if (a0 <= 0 || a1 <= 0) return h;
                double v = Math.Abs(Math.Log(am / a0)) + Math.Abs(Math.Log(a1 / am));
                if (v <= limit) return h;
                h *= 0.5;
            }
            return h;
        }

        /// <summary>Propagate every member (initial fields in the engine's centred order); returns the output fields.</summary>
        public Complex[][] Propagate(IList<Complex[]> initial, IList<StepperSettings> settings, IList<StepStatistics> stats)
        {
            int B = _B;
            long bytes = _tot * 16L;
            GpuBuffer a = Buf(bytes), kN = Buf(bytes), half = Buf(bytes), aI = Buf(bytes), k1 = Buf(bytes), k2 = Buf(bytes), k3 = Buf(bytes),
                      k4 = Buf(bytes), k5 = Buf(bytes), tmp = Buf(bytes), beta = Buf(bytes), a4 = Buf(bytes);
            var a0 = new Complex[_tot];
            for (int b = 0; b < B; b++)
            {
                var nat = DeviceStepper.Natural(initial[b], _comp);
                for (int c = 0; c < _comp; c++) Array.Copy(nat, c * _n, a0, (c * B + b) * _n, _n);
            }
            S.Write(a, a0);
            var z = new double[B]; var h = new double[B]; var pk = new double[B]; var lastReport = new double[B];
            var guard = new int[B]; var done = new bool[B];
            var Lm = new double[B];
            for (int b = 0; b < B; b++)
            {
                Lm[b] = _p[b].LengthM; Stage(b, 0, 0);
                stats[b].Method = "erk4(3)-ip (adaptive, tol " + settings[b].Tolerance.ToString("G2", CultureInfo.InvariantCulture) + ")";
            }
            S.Write(_prm, _prmHost);
            Nonlinear(a, 0, kN, 2 * B);
            S.Read(_out, _outHost);
            for (int b = 0; b < B; b++)
            {
                var st = settings[b];
                pk[b] = _outHost[2 * B + b];
                h[b] = pk[b] > 0 ? Math.Min(Lm[b], st.MaxNonlinearPhasePerStepRad / pk[b]) : Lm[b];
                h[b] = Math.Min(h[b], st.MaxStepM);
                done[b] = !(z[b] < Lm[b] * (1 - 1e-12));
            }
            var hStep = new double[B];
            int active = 0; foreach (var d in done) if (!d) active++;
            while (active > 0)
            {
                for (int b = 0; b < B; b++)
                {
                    if (done[b]) { hStep[b] = 0; _prmHost[b] = 0; continue; }
                    var st = settings[b];
                    if (++guard[b] > st.MaxSteps) stats[b].Notes.Add("maximum step count reached at z = " + z[b].ToString("G4", CultureInfo.InvariantCulture) + " m; finished with the last step size");
                    double hb = Math.Min(h[b], Lm[b] - z[b]);
                    hb = Math.Min(hb, st.MaxStepM);
                    hb = AreaLimitedStep(_p[b].AreaAt, z[b], hb, st.MaxAreaChangePerStep);
                    hb = Math.Max(hb, Math.Min(st.MinStepM, Lm[b] - z[b]));
                    hStep[b] = hb; _prmHost[b] = hb;
                    Stage(b, 1, z[b] + 0.5 * hb); Stage(b, 2, z[b] + hb);
                }
                S.Write(_prm, _prmHost);
                S.Launch("uf_ens_exp", _gTot, _L, _prm, 0.5, half, _tot, _n, B);
                S.Launch("uf_mul", _gTot, half, a, aI, _tot);
                S.Launch("uf_mul", _gTot, half, kN, k1, _tot);
                S.Launch("uf_ens_axpy", _gTot, aI, _prm, 0.5, k1, tmp, _tot, _n, B); Nonlinear(tmp, 1, k2, -1);
                S.Launch("uf_ens_axpy", _gTot, aI, _prm, 0.5, k2, tmp, _tot, _n, B); Nonlinear(tmp, 1, k3, -1);
                S.Launch("uf_ens_axpy_mul", _gTot, half, aI, _prm, k3, tmp, _tot, _n, B);
                Nonlinear(tmp, 2, k4, -1);
                S.Launch("uf_ens_rk_beta", _gTot, half, aI, k1, k2, k3, _prm, beta, _tot, _n, B);
                S.Launch("uf_ens_rk_final", _gTot, beta, k4, _prm, a4, _tot, _n, B);
                Nonlinear(a4, 2, k5, 2 * B);
                S.Launch("uf_ens_rk_err", Math.Min(B, C.MaxGroups), beta, a4, k4, k5, _prm, _n, B, _comp, _out, 0);
                S.Read(_out, _outHost);
                for (int b = 0; b < B; b++)
                {
                    _prmHost[OffMask + b] = 0;
                    if (done[b]) continue;
                    var st = settings[b]; var sb = stats[b];
                    double num = _outHost[b], den = _outHost[B + b], pk5 = _outHost[2 * B + b], hb = hStep[b];
                    double err = den > 0 ? Math.Sqrt(num) / Math.Sqrt(den) : 0.0;
                    bool forced = hb <= st.MinStepM * 1.0000001 || guard[b] > st.MaxSteps;
                    if (err <= st.Tolerance || forced)
                    {
                        sb.Accepted++;
                        sb.BIntegralRad += 0.5 * (pk[b] + pk5) * hb;
                        sb.MinStepM = Math.Min(sb.MinStepM, hb); sb.MaxStepM = Math.Max(sb.MaxStepM, hb);
                        z[b] += hb; pk[b] = pk5;
                        _prmHost[OffMask + b] = 1;
                        if (forced && err > st.Tolerance && !sb.Notes.Contains("tolerance not met at minimum step")) sb.Notes.Add("tolerance not met at minimum step");
                        if (z[b] >= Lm[b] * (1 - 1e-12)) done[b] = true;
                    }
                    else sb.Rejected++;
                    double factor = err > 0 ? 0.9 * Math.Pow(st.Tolerance / err, 0.25) : 2.0;
                    h[b] = hb * Math.Min(2.0, Math.Max(0.5, factor));
                    if (guard[b] > st.MaxSteps + 10) done[b] = true;
                }
                S.Write(_prm, _prmHost);
                S.Launch("uf_ens_select", _gTot, a, a4, kN, k5, _prm, OffMask, _tot, _n, B);
                active = 0; foreach (var d in done) if (!d) active++;
            }
            var res = new Complex[_tot];
            S.Read(a, res);
            var outs = new Complex[B][];
            for (int b = 0; b < B; b++)
            {
                var nat = new Complex[_comp * _n];
                for (int c = 0; c < _comp; c++) Array.Copy(res, (c * B + b) * _n, nat, c * _n, _n);
                outs[b] = DeviceStepper.Natural(nat, _comp);
            }
            return outs;
        }

        public void Dispose()
        {
            _fftState?.Dispose(); _fftRaman?.Dispose(); _fftRot?.Dispose();
            foreach (var b in _all) b.Dispose();
            _all.Clear();
        }
    }
}
