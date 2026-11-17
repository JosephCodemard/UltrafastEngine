// Chi2Stepper.cs -- chi(2) coupled-wave propagation (Crystals.Chi2Problem, 1-D model) on a device: SHG and three-wave mixing
// (SFG, DFG, OPA) with optional chi(3) SPM / XPM and self-steepening, the same interaction-picture steppers as the CPU (DeviceStepper),
// QPM domain walls as stepper breakpoints. The coupling g0 and e^{i phi} at each z come from Chi2Problem.CouplingAt on the host,
// so they are the CPU's values bit for bit. Spatial chi(2) models stay on the CPU.
//
// Classes: Chi2Stepper (MemoryNeeded(spec))
using System;
using UltrafastEngine.Crystals;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Plugins.Gpu
{
    public sealed class Chi2Stepper : DeviceStepper
    {
        private readonly Chi2DeviceSpec _p;
        private readonly int _n, _K, _gN;
        private readonly GpuFft _fft;
        private readonly GpuBuffer _T, _xf, _xa, _St;
        private readonly double[] _ka;
        private readonly bool _mixed;
        private readonly GpuFftF _fftF;
        private readonly GpuBuffer _Tf, _Rf;

        public bool Mixed => _mixed;

        public Chi2Stepper(GpuStream s, Chi2DeviceSpec p, bool allowVendorFft = true, bool mixed = false)
            : base(s, p.K, p.N, p.LinearRate, p.LengthM, s.Context.Groups(p.N) + 4)
        {
            _mixed = mixed && GpuFft.BuiltInSupports(p.N);
            if (_mixed) { _fftF = new GpuFftF(s, p.N, p.K); _Tf = Buf(8L * p.K * p.N); _Rf = Buf(8L * p.K * p.N); }
            _p = p; _n = p.N; _K = p.K;
            _fft = new GpuFft(s, _n, _K, allowVendorFft);
            _T = Buf(Len * 16L);
            _xf = Buf(8L * _K * _K); S.Write(_xf, p.XpmFactor);
            _xa = Buf(8L * _K * _K); S.Write(_xa, p.XpmArea);
            if (p.Steepening != null) { _St = Buf(8L * Len); S.Write(_St, Natural(p.Steepening, _K)); }
            _ka = new double[3];
            for (int j = 0; j < _K; j++) _ka[j] = p.Omega0[j] / PhysicalConstants.SpeedOfLightMPerS;
            _gN = C.Groups(_n);
        }

        public static long MemoryNeeded(Chi2DeviceSpec p) => 16L * p.K * p.N * 16 + 8L * p.K * p.N;

        protected override double NextBreakpoint(double z) => _p.NextBreakpoint(z);

        protected override double Nonlinear(GpuBuffer state, double z, GpuBuffer rate, bool wantPeak)
        {
            var (g0, ep) = _p.CouplingAt(z);
            double w2 = _K > 2 ? _p.Omega0[2] : 0.0;
            if (_mixed)
            {
                S.Launch("uf_d2f", GLen, state, _Tf, Len);
                _fftF.Transform(_Tf, -1, true);
                S.Launch("uf_chi2_f", _gN, _Tf, _n, _K, _p.Shg ? 1 : 0, g0, ep.Real, ep.Imaginary, _p.Omega0[0], _p.Omega0[1], w2,
                    _p.N2, _ka[0], _ka[1], _ka[2], _xf, _xa, _Rf, Part, 0);
                _fftF.Transform(_Rf, +1, false);
                S.Launch("uf_f2d", GLen, _Rf, rate, Len);
            }
            else
            {
                S.Copy(state, 0, _T, 0, 16L * Len);
                _fft.Transform(_T, -1, true);                      // A_j(t)
                S.Launch("uf_chi2", _gN, _T, _n, _K, _p.Shg ? 1 : 0, g0, ep.Real, ep.Imaginary, _p.Omega0[0], _p.Omega0[1], w2,
                    _p.N2, _ka[0], _ka[1], _ka[2], _xf, _xa, rate, Part, 0);
                _fft.Transform(rate, +1, false);
            }
            if (_St != null) S.Launch("uf_scale_real_mod", GLen, rate, _St, Len, Len);
            if (!wantPeak) return double.NaN;
            S.Read(Part, PartHost, 0, _gN);
            double pk = 0;
            for (int g = 0; g < _gN; g++) pk = Math.Max(pk, PartHost[g]);
            return Math.Max(pk, 1e-30);                         // as Chi2Problem.Nonlinear
        }

        public override void Dispose()
        {
            _fft?.Dispose(); _fftF?.Dispose();
            base.Dispose();
        }
    }
}
