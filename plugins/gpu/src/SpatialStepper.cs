using System;
using System.Numerics;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Plugins.Gpu
{
    /// <summary>
    /// Spatio-temporal envelope propagation (<see cref="SpatialEnvelopeProblem"/>) on a device. The nonlinear operator
    /// mirrors the CPU: transverse transform to space (radial: the orthogonal Hankel matrix as a real matrix product
    /// over all frequencies at once -- cuBLAS DGEMM when available; cartesian: 2-D FFT over the transverse axes for
    /// every frequency), batched FFT to time for every transverse point, pointwise Kerr / Raman / vector coupling
    /// (and the gas rotational Raman response as a padded linear convolution per point),
    /// back to frequency, self-steepening, transform back to wavevector space and the UPPE factor.
    /// </summary>
    public sealed class SpatialStepper : DeviceStepper
    {
        private readonly SpatialDeviceSpec _p;
        private readonly int _n, _M, _comp, _block, _nsig, _raman, _rot, _nrot, _m;
        private readonly GpuFft _fftRows, _fftRaman, _fftX, _fftY, _fftRot;
        private readonly GpuBuffer _X, _Y, _Cr, _K, _Kb, _F, _P, _D, _Tm, _Sc, _Up, _Q, _Kr;
        private readonly double[] _rowPeaks;
        private readonly string _user;
        private readonly GpuBuffer _U, _tn, _Fl, _px, _py, _pa;
        private readonly double _dtResp, _lambda0;

        public SpatialStepper(GpuStream s, SpatialDeviceSpec p, bool allowVendor = true)
            : base(s, (p.Vector ? 2 : 1) * p.M, p.N, p.LinearRate, p.LengthM, p.M + 4)
        {
            _p = p; _n = p.N; _M = p.M; _comp = p.Vector ? 2 : 1; _block = _M * _n;
            bool ramanOn = p.RamanKernelFreq != null && p.RamanFraction > 0 && p.N2 != 0;
            _raman = !ramanOn ? 0 : (!p.Vector || p.RamanAnisoFreq == null ? 1 : 2);
            _nsig = _raman == 2 ? 5 : 1;
            _fftRows = new GpuFft(s, _n, _comp * _M, allowVendor);
            if (_raman > 0) _fftRaman = new GpuFft(s, _n, _nsig * _M, allowVendor);
            if (p.Cartesian)
            {
                // element (ix, iy, i) of component c at c block + (ix Ny + iy) n + i
                if (p.Ny > 1) _fftY = new GpuFft(s, p.Ny, _comp * p.Nx * _n, _n, p.Ny * _n, _n, false, (long)_comp * _block);
                _fftX = new GpuFft(s, p.Nx, _comp * p.Ny * _n, p.Ny * _n, p.Nx * p.Ny * _n, p.Ny * _n, false, (long)_comp * _block);
            }
            else
            {
                _Tm = Buf(8L * _M * _M); S.Write(_Tm, p.Matrix);
                _Sc = Buf(8L * _M); S.Write(_Sc, p.Scale);
            }
            _X = Buf(Len * 16L); _Y = Buf(Len * 16L);
            _Cr = Buf((long)_nsig * _block * 16L);
            if (_raman > 0)
            {
                _K = Buf(_n * 16L); S.Write(_K, Natural(p.RamanKernelFreq, 1));
                _Kb = Buf(_n * 16L); S.Write(_Kb, Natural(_raman == 2 ? p.RamanAnisoFreq : p.RamanKernelFreq, 1));
            }
            else { _K = _Cr; _Kb = _Cr; }
            if (p.SelfSteepening)
            {
                var f = new double[_n];
                var om = Natural(p.OmegaOffset);
                for (int i = 0; i < _n; i++) f[i] = 1.0 + om[i] / p.OmegaCenter;
                _F = Buf(_n * 8L); S.Write(_F, f);
            }
            if (p.RotationalKernelFreq != null)
            {
                _rot = p.Vector ? 2 : 1; _nrot = p.Vector ? 3 : 1; _m = p.RotationalKernelFreq.Length;
                _fftRot = new GpuFft(s, _m, _nrot * _M, allowVendor);
                _Q = Buf((long)_nrot * _M * _m * 16L);
                _Kr = Buf(_m * 16L); S.Write(_Kr, p.RotationalKernelFreq);
            }
            else { _Q = _Cr; _m = 1; }
            if (p.Uppe != null) { _Up = Buf(8L * _block); S.Write(_Up, Natural(p.Uppe, _M)); }
            _P = Buf(8L * _block); _D = Buf(8L * _block);
            _rowPeaks = new double[_M];
            if (p.Responses != null)
            {
                _user = UserResponseKernel.Prepare(s, p.Responses);
                _U = Buf(16L * _block);
                _tn = Buf(_n * 8L); S.Write(_tn, Natural(p.Time));
                _Fl = p.Responses.UsesFluence ? Buf(8L * _block) : _P;
                _px = Buf(8L * _M); S.Write(_px, p.PointX);
                _py = Buf(8L * _M); S.Write(_py, p.PointY);
                _pa = Buf(8L * _M); S.Write(_pa, p.PointWeights);
                _dtResp = p.Time.Length > 1 ? p.Time[1] - p.Time[0] : 0;
                _lambda0 = 2 * Math.PI * UltrafastEngine.Numerics.PhysicalConstants.SpeedOfLightMPerS / p.ResponseOmega0;
            }
            else _U = _Cr;
        }

        public static long MemoryNeeded(SpatialDeviceSpec p)
        {
            long block = (long)p.M * p.N, len = block * (p.Vector ? 2 : 1);
            long nsig = p.Vector && p.RamanAnisoFreq != null ? 5 : 1;
            long rot = p.RotationalKernelFreq == null ? 0 : (long)p.RotationalKernelFreq.Length * ((p.Vector ? 3 : 1) * 2L * p.M + 1);
            long user = p.Responses == null ? 0 : block;
            return 16L * (len * 17 + nsig * block * 2 + rot + user) + 8L * (3 * block + (long)p.M * p.M + user + 3L * p.M + p.N);
        }

        /// <summary>Wavevector -> space, from <paramref name="src"/> into <paramref name="dst"/>.</summary>
        private void ToSpace(GpuBuffer src, GpuBuffer dst)
        {
            if (_p.Cartesian)
            {
                S.Copy(src, 0, dst, 0, 16L * Len);
                Fft2(dst, +1);
                return;
            }
            for (int c = 0; c < _comp; c++) MatMul(src, dst, c);
            S.Launch("uf_rowscale", GLen, dst, _Sc, _n, _M, Len, 1);
        }

        /// <summary>Space -> wavevector from <paramref name="data"/> (overwritten) into <paramref name="dst"/>.</summary>
        private void ToK(GpuBuffer data, GpuBuffer dst)
        {
            if (_p.Cartesian) { Fft2(data, -1); S.Copy(data, 0, dst, 0, 16L * Len); return; }
            S.Launch("uf_rowscale", GLen, data, _Sc, _n, _M, Len, 0);
            for (int c = 0; c < _comp; c++) MatMul(data, dst, c);
        }

        /// <summary>dst block c = T (M x M) x src block c (each block an M x 2n real matrix).</summary>
        private void MatMul(GpuBuffer src, GpuBuffer dst, int c)
        {
            long off = 2L * c * _block;
            int cols = 2 * _n;
            if (S.VendorDgemmRowMajor(_Tm, src, dst, _M, cols, _M, off, off)) return;
            S.Launch("uf_matmul", C.Groups((long)_M * cols), _Tm, src, dst, _M, _M, cols, (int)off, (int)off);
        }

        /// <summary>Cartesian transverse 2-D FFT for every sample: -1 space (centred) -> k (standard order); +1 back
        /// with 1/(Nx Ny). The swaps of halves are sign alternations on the k side.</summary>
        private void Fft2(GpuBuffer d, int sign)
        {
            if (sign < 0)
            {
                _fftY?.Transform(d, -1, false); _fftY?.AlternateSign(d);
                _fftX.Transform(d, -1, false); _fftX.AlternateSign(d);
            }
            else
            {
                if (_fftY != null) { _fftY.AlternateSign(d); _fftY.Transform(d, +1, true); }
                _fftX.AlternateSign(d); _fftX.Transform(d, +1, true);
            }
        }

        protected override double Nonlinear(GpuBuffer state, double z, GpuBuffer rate, bool wantPeak)
        {
            ToSpace(state, _X);
            _fftRows.Transform(_X, -1, true);                                   // E(x, t)
            if (_raman > 0)
            {
                S.Launch("uf_sp_raman_in", C.Groups(_block), _X, _block, _p.Vector ? 1 : 0, _nsig, _Cr);
                _fftRaman.Transform(_Cr, +1, false);
                S.Launch("uf_sp_raman_mul", C.Groups((long)_block * _nsig), _Cr, _K, _Kb, _n, _block, _nsig);
                _fftRaman.Transform(_Cr, -1, true);
            }
            if (_rot > 0)
            {
                // orientational response: zero-padded linear convolution of every transverse point's row (centred time)
                S.Launch("uf_rot_in", C.Groups((long)_M * _m), _X, _n, _M, _block, _p.Vector ? 0 : 1, 1.0, _m, _Q);
                _fftRot.Transform(_Q, -1, false);
                S.Launch("uf_rot_mul", C.Groups((long)_m * _nrot * _M), _Q, _Kr, _m, _nrot * _M);
                _fftRot.Transform(_Q, +1, true);
            }
            if (_user != null)
            {
                // extra time-domain responses per transverse point (SpatialEnvelopeProblem.Nonlinear: intensities |E|^2, Aeff = the point's weight)
                int sc = _p.Vector ? 0 : 1;
                bool useF = _p.Responses.UsesFluence;
                if (useF) S.Launch("uf_fluence", Math.Min(_M, C.MaxGroups), _X, _n, _M, _block, sc, 1.0, _dtResp, _Fl);
                double tArr = _p.TArrivalAtStart + _p.ElementCarrierGroupDelay * (z / _p.LengthM);
                S.Launch(_user, C.Groups(_block), _X, _n, _M, _block, sc, 1.0, _tn, tArr, z, _p.LengthM, _p.ResponseOmega0, _lambda0, 0.0,
                    _px, _py, _pa, 1, 1.0, _Fl, useF ? 1 : 0, _U);
            }
            double cc = _p.Vector ? _p.N2 * (1 - _p.RamanFraction) / 3.0 : 0.0;
            S.Launch("uf_sp_kerr", C.Groups(_block), _X, _Cr, _block, _p.Vector ? 1 : 0, _raman, _p.K0, _p.N2, _p.RamanFraction, _p.DTime, cc, _Y, _P, _D,
                _Q, _rot, _m, _n, _M, _U, _user != null ? 1 : 0);
            if (wantPeak) S.Launch("uf_rowpeak", Math.Min(_M, C.MaxGroups), _P, _D, _n, _M, Part);
            _fftRows.Transform(_Y, +1, false);
            if (_F != null) S.Launch("uf_scale_real", GLen, _Y, _F, _n, Len);
            ToK(_Y, rate);
            if (_Up != null) S.Launch("uf_scale_real_mod", GLen, rate, _Up, _block, Len);
            if (!wantPeak) return double.NaN;
            S.Read(Part, _rowPeaks, 0, _M);
            double pk = 0;
            for (int m = 0; m < _M; m++) pk = Math.Max(pk, _rowPeaks[m]);
            return _p.K0 * pk;
        }

        public override void Dispose()
        {
            _fftRows?.Dispose(); _fftRaman?.Dispose(); _fftRot?.Dispose(); _fftX?.Dispose(); _fftY?.Dispose();
            base.Dispose();
        }
    }
}
