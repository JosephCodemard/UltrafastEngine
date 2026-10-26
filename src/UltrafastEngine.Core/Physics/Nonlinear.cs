using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;

namespace UltrafastEngine.Physics
{
    /// <summary>The nonlinear properties of one element, after overrides are resolved.</summary>
    public sealed class NonlinearMedium
    {
        public double N2 { get; set; }
        public double RamanFraction { get; set; }
        public IDelayedResponse Raman { get; set; }
        public bool EnableRaman { get; set; } = true;
        public bool EnableSelfSteepening { get; set; } = true;
        /// <summary>When true the built-in Kerr (+Raman) response is switched off and only <see cref="ExtraResponses"/> act.</summary>
        public bool ReplaceKerr { get; set; }
        public List<ITimeDomainResponse> ExtraResponses { get; } = new List<ITimeDomainResponse>();
        /// <summary>Orientational (rotational Raman) response of molecular gases: an anisotropic, delayed index tensor
        /// (docs/reference/gases.md). Null for atoms and solids.</summary>
        public IAnisotropicResponse Rotational { get; set; }
        public bool NeedsCarrierResolution => ExtraResponses.Any(r => r.NeedsCarrierResolution);
        public bool IsActive => (!ReplaceKerr && N2 != 0) || ExtraResponses.Count > 0 || Rotational != null;
        public double EffectiveRamanFraction => EnableRaman && Raman != null ? RamanFraction : 0.0;
    }

    /// <summary>
    /// A delayed anisotropic (orientational) response: Δn_∥(t) = ∫ R(t - t') I(t') dt' for a field polarised along the
    /// pump, -1/2 of it perpendicular. For general fields the index tensor is
    /// Δn_ij = R ⊛ [ (3/2) Re(A_i A_j*) - (1/2) δ_ij |A|² ] (so circular light sees 1/4 of the linear response).
    /// </summary>
    public interface IAnisotropicResponse
    {
        string Description { get; }
        /// <summary>Linear, causal convolution (R ⊛ x)(t) on a uniform grid of step dt (units of R: m^2/W/s).</summary>
        double[] Convolve(double[] x, double dt);
        /// <summary>∫ R dt (m^2/W): the adiabatic (long-pulse) nonlinear index of the response.</summary>
        double AdiabaticN2 { get; }
    }

    /// <summary>
    /// What a stepper integrates: dA/dz = L(omega) A + N(A, z), with L diagonal in frequency (the
    /// frame-referenced linear operator of the element per unit length) and N the nonlinear operator.
    /// </summary>
    public interface IPropagationProblem
    {
        int Length { get; }
        double LengthM { get; }
        /// <summary>L per unit length (1/m), complex, one per state entry.</summary>
        Complex[] LinearRate { get; }
        /// <summary>Nonlinear rate dA/dz|_NL at position z (m from element start). Returns the peak of
        /// k0 * Re(Delta n) (1/m), used for the B-integral and step control.</summary>
        double Nonlinear(Complex[] state, double z, Complex[] rateOut);
        double AreaAt(double z);
    }

    /// <summary>Optional: a problem whose nonlinear rate is discontinuous at known positions (e.g. QPM domain walls).
    /// The adaptive stepper lands exactly on each breakpoint, so every step sees a smooth right-hand side.</summary>
    public interface IBreakpoints
    {
        /// <summary>The first breakpoint strictly after <paramref name="z"/> (m), or +infinity.</summary>
        double NextBreakpoint(double z);
    }

    /// <summary>Statistics of one nonlinear element's integration (reported per element).</summary>
    public sealed class StepStatistics
    {
        public string Method { get; set; }
        public int Accepted { get; set; }
        public int Rejected { get; set; }
        public double MinStepM { get; set; } = double.PositiveInfinity;
        public double MaxStepM { get; set; }
        public double BIntegralRad { get; set; }
        public string Representation { get; set; } = "envelope";
        public List<string> Notes { get; } = new List<string>();
    }

    // ============================================================================== envelope problem

    /// <summary>
    /// Two-polarisation envelope NLSE in the co-moving frame (state = [Ax; Ay] spectra, sqrt(W) units):
    /// N_x = i k0 [ (dn_x + dn_user) A_x + n2 (1-fR)/3 A_y^2 A_x* / Aeff ] (x)(1 + Omega/omega0),
    /// dn_x = n2 [ (1-fR)(I_x + 2/3 I_y) + fR (h_R * I) ], the isotropic chi(3) vector coupling in the linear
    /// basis (XPM 2/3 plus the coherent term, so circular light correctly sees 2/3 of the linear SPM).
    /// When the input is purely x-polarised and the medium is isotropic, only Ax is propagated.
    /// </summary>
    public sealed class EnvelopeProblem : IPropagationProblem
    {
        private readonly FrequencyGrid _g;
        private readonly NonlinearMedium _m;
        private readonly IBeamModel _beam;
        private readonly Complex[] _ramanKernelFreq, _ramanAnisoFreq;
        private readonly double _k0;
        private readonly bool _scalar;
        private readonly double _tArrival0, _tauElement;
        private readonly string _id;
        private readonly double[] _tLab;

        public int Length { get; }
        public double LengthM { get; }
        public Complex[] LinearRate { get; }
        public bool Scalar => _scalar;

        public EnvelopeProblem(FrequencyGrid grid, NonlinearMedium medium, IBeamModel beam, double lengthM,
            double[] phX, double[] ampX, double[] phY, double[] ampY, bool scalar, double tArrivalAtStart, double elementCarrierGroupDelay, string elementId)
        {
            _g = grid; _m = medium; _beam = beam; LengthM = lengthM; _scalar = scalar; _id = elementId;
            _tArrival0 = tArrivalAtStart; _tauElement = elementCarrierGroupDelay;
            _k0 = grid.OmegaCenter / PhysicalConstants.SpeedOfLightMPerS;
            int n = grid.N;
            Length = scalar ? n : 2 * n;
            LinearRate = new Complex[Length];
            for (int i = 0; i < n; i++)
            {
                LinearRate[i] = Rate(phX[i], ampX[i], lengthM);
                if (!scalar) LinearRate[n + i] = Rate((phY ?? phX)[i], (ampY ?? ampX)[i], lengthM);
            }
            if (medium.EffectiveRamanFraction > 0)
                (_ramanKernelFreq, _ramanAnisoFreq) = RamanKernels(medium.Raman, grid, !scalar);
            _tLab = new double[n];
        }

        /// <summary>The Raman kernel (unit sum on the grid) in the frequency domain, and -- for two polarisations and a
        /// response with a polarisation structure -- its anisotropic part h_b on the same normalisation (else null).</summary>
        internal static (Complex[] Total, Complex[] Aniso) RamanKernels(IDelayedResponse r, FrequencyGrid grid, bool vector)
        {
            int n = grid.N;
            var raw = r.Evaluate(grid.Time);
            double s = 0; for (int i = 0; i < n; i++) s += raw[i];
            s *= grid.DTime;
            double scale = s > 0 && Math.Abs(s - 1) > 1e-12 ? 1 / s : 1;
            var hc = new Complex[n];
            for (int i = 0; i < n; i++) hc[i] = raw[i] * scale;
            var total = SpectralTransform.TimeToFreq(hc);
            Complex[] aniso = null;
            if (vector && r is IVectorDelayedResponse v && v.AnisotropicFraction > 0)
            {
                var hb = v.EvaluateAnisotropic(grid.Time);
                var bc = new Complex[n];
                for (int i = 0; i < n; i++) bc[i] = hb[i] * scale;
                aniso = SpectralTransform.TimeToFreq(bc);
            }
            return (total, aniso);
        }

        /// <summary>
        /// Vector Raman index changes (Hellwarth; Lin &amp; Agrawal 2004): with h = h_a + h_b,
        /// dn_x = fR [h_a * I + h_b * I_x], dn_y = fR [h_a * I + h_b * I_y], and a coupling dn_xy = fR h_b * Re(A_x A_y^*)
        /// acting as dn_xy A_y on x and dn_xy A_x on y (all in units of n2 x intensity).
        /// </summary>
        internal static void VectorRaman(double[] ix, double[] iy, double[] it, double[] rxy, Complex[] total, Complex[] aniso, double dt,
            out double[] rx, out double[] ry, out double[] rcross)
        {
            int n = it.Length;
            var all = Convolution.CircularConvolveWithKernelFreq(it, total, dt);
            var allB = Convolution.CircularConvolveWithKernelFreq(it, aniso, dt);
            var bx = Convolution.CircularConvolveWithKernelFreq(ix, aniso, dt);
            var by = Convolution.CircularConvolveWithKernelFreq(iy, aniso, dt);
            rcross = Convolution.CircularConvolveWithKernelFreq(rxy, aniso, dt);
            rx = new double[n]; ry = new double[n];
            for (int i = 0; i < n; i++) { double iso = all[i] - allB[i]; rx[i] = iso + bx[i]; ry[i] = iso + by[i]; }
        }

        /// <summary>The largest nonlinear index change seen by the pulse, from the power-weighted effective index
        /// Re(sum_c A_c^* N_c / (i k0)) / sum_c |A_c|^2 (N_c: the nonlinear rate of component c): every coupling term
        /// is included, so circular light gives 2/3 of the linear value, as it should.</summary>
        internal static double PeakIndexChange(Complex[] ax, Complex[] ay, Complex[] nx, Complex[] ny, double k0)
        {
            int n = ax.Length; double pmax = 0;
            for (int i = 0; i < n; i++) { double p = ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary + (ay != null ? ay[i].Real * ay[i].Real + ay[i].Imaginary * ay[i].Imaginary : 0); if (p > pmax) pmax = p; }
            if (!(pmax > 0)) return 0;
            double peak = 0;
            for (int i = 0; i < n; i++)
            {
                double p = ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary + (ay != null ? ay[i].Real * ay[i].Real + ay[i].Imaginary * ay[i].Imaginary : 0);
                if (p < 1e-6 * pmax) continue;
                var s = Complex.Conjugate(ax[i]) * nx[i];
                if (ay != null && ny != null) s += Complex.Conjugate(ay[i]) * ny[i];
                double dn = Math.Abs(s.Imaginary / (k0 * p));   // Re(s / i) = Im(s)
                if (dn > peak) peak = dn;
            }
            return peak;
        }

        /// <summary>The sampled Raman kernel scaled to unit sum on the grid, so the delayed fraction is exactly fR
        /// whatever the time step (an analytic normalisation is off by a few % when dt is not much smaller than the oscillation period).</summary>
        internal static double[] NormalizedKernel(double[] h, double dt)
        {
            double s = 0; for (int i = 0; i < h.Length; i++) s += h[i];
            s *= dt;
            if (s > 0 && Math.Abs(s - 1) > 1e-12) for (int i = 0; i < h.Length; i++) h[i] /= s;
            return h;
        }

        internal static Complex Rate(double phase, double amp, double lengthM)
        {
            double la = amp > 1e-300 ? Math.Log(amp) : -690.0;
            return new Complex(la / lengthM, phase / lengthM);
        }

        public double AreaAt(double z) => _beam.EffectiveArea(Math.Max(0, Math.Min(LengthM, z)));

        /// <summary>Per-thread work arrays of the fast path (the problem object may be shared by parallel runs).</summary>
        private sealed class Work
        {
            public int N;
            public Complex[] Ax, Ay, C, Nx, Ny;
            public double[] Ix, Iy, It, Rxy, Ram, RamX, RamY, RamXY, AllB;
            public Work(int n)
            {
                N = n; Ax = new Complex[n]; Ay = new Complex[n]; C = new Complex[n]; Nx = new Complex[n]; Ny = new Complex[n];
                Ix = new double[n]; Iy = new double[n]; It = new double[n]; Rxy = new double[n]; Ram = new double[n];
                RamX = new double[n]; RamY = new double[n]; RamXY = new double[n]; AllB = new double[n];
            }
        }
        [ThreadStatic] private static Work _work;

        /// <summary>Kerr + Raman + self-steepening without user responses or rotational Raman: the same arithmetic as the
        /// general path, in place on reused arrays (no allocation per evaluation; large arrays would otherwise go to the
        /// large-object heap at every stage).</summary>
        public bool FastPath => UseFastPath && _m.Rotational == null && _m.ExtraResponses.Count == 0;

        /// <summary>Process-wide switch for the in-place fast path (tests compare it with the general path).</summary>
        public static bool UseFastPath = true;

        /// <summary>The data a device back-end (GPU) needs to evaluate this problem's nonlinear operator itself; null when
        /// the problem uses features only the CPU path implements (user responses, rotational Raman).</summary>
        public EnvelopeDeviceSpec DeviceSpec()
        {
            if (_m.Rotational != null || _m.ExtraResponses.Count != 0) return null;
            bool kerr = !_m.ReplaceKerr && _m.N2 != 0;
            return new EnvelopeDeviceSpec
            {
                N = _g.N, Scalar = _scalar, K0 = _k0, N2 = kerr ? _m.N2 : 0, RamanFraction = kerr ? _m.EffectiveRamanFraction : 0,
                RamanKernelFreq = kerr && _m.EffectiveRamanFraction > 0 ? _ramanKernelFreq : null,
                RamanAnisoFreq = kerr && _m.EffectiveRamanFraction > 0 && !_scalar ? _ramanAnisoFreq : null,
                SelfSteepening = _m.EnableSelfSteepening, OmegaOffset = _g.OmegaOffset, OmegaCenter = _g.OmegaCenter, DTime = _g.DTime,
                LinearRate = LinearRate, LengthM = LengthM, AreaAt = AreaAt
            };
        }

        private static void ConvolveInPlace(double[] signal, Complex[] kFreq, double dt, Complex[] c, double[] result)
        {
            int n = signal.Length;
            for (int i = 0; i < n; i++) c[i] = signal[i];
            SpectralTransform.TimeToFreqInPlace(c);
            for (int i = 0; i < n; i++) c[i] = c[i] * kFreq[i];
            SpectralTransform.FreqToTimeInPlace(c);
            for (int i = 0; i < n; i++) result[i] = c[i].Real * dt;
        }

        private double NonlinearFast(Complex[] state, double z, Complex[] rate)
        {
            int n = _g.N;
            var w = _work;
            if (w == null || w.N != n) _work = w = new Work(n);
            double aeff = AreaAt(z);
            var ax = w.Ax; var ay = _scalar ? null : w.Ay;
            Array.Copy(state, 0, ax, 0, n);
            SpectralTransform.FreqToTimeInPlace(ax);
            if (!_scalar) { Array.Copy(state, n, ay, 0, n); SpectralTransform.FreqToTimeInPlace(ay); }
            var ix = w.Ix; var iy = w.Iy; var it = w.It;
            for (int i = 0; i < n; i++)
            {
                ix[i] = (ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary) / aeff;
                iy[i] = _scalar ? 0 : (ay[i].Real * ay[i].Real + ay[i].Imaginary * ay[i].Imaginary) / aeff;
                it[i] = ix[i] + iy[i];
            }
            double fR = _m.EffectiveRamanFraction;
            bool kerr = !_m.ReplaceKerr && _m.N2 != 0;
            bool vecRaman = false;
            if (kerr && fR > 0)
            {
                if (!_scalar && _ramanAnisoFreq != null)
                {
                    vecRaman = true;
                    for (int i = 0; i < n; i++) w.Rxy[i] = (ax[i] * Complex.Conjugate(ay[i])).Real / aeff;
                    ConvolveInPlace(it, _ramanKernelFreq, _g.DTime, w.C, w.Ram);
                    ConvolveInPlace(it, _ramanAnisoFreq, _g.DTime, w.C, w.AllB);
                    ConvolveInPlace(ix, _ramanAnisoFreq, _g.DTime, w.C, w.RamX);
                    ConvolveInPlace(iy, _ramanAnisoFreq, _g.DTime, w.C, w.RamY);
                    ConvolveInPlace(w.Rxy, _ramanAnisoFreq, _g.DTime, w.C, w.RamXY);
                    for (int i = 0; i < n; i++) { double iso = w.Ram[i] - w.AllB[i]; w.RamX[i] = iso + w.RamX[i]; w.RamY[i] = iso + w.RamY[i]; }
                }
                else ConvolveInPlace(it, _ramanKernelFreq, _g.DTime, w.C, w.Ram);
            }
            var nx = w.Nx; var ny = _scalar ? null : w.Ny;
            double cc = kerr ? _m.N2 * (1 - fR) / (3.0 * aeff) : 0.0;
            for (int i = 0; i < n; i++)
            {
                double dnx = 0, dny = 0;
                if (kerr)
                {
                    double rx = fR > 0 ? fR * (vecRaman ? w.RamX[i] : w.Ram[i]) : 0.0;
                    double ry = fR > 0 ? fR * (vecRaman ? w.RamY[i] : w.Ram[i]) : 0.0;
                    dnx = _m.N2 * ((1 - fR) * (ix[i] + 2.0 / 3.0 * iy[i]) + rx);
                    dny = _m.N2 * ((1 - fR) * (iy[i] + 2.0 / 3.0 * ix[i]) + ry);
                }
                nx[i] = Complex.ImaginaryOne * _k0 * new Complex(dnx, 0) * ax[i];
                if (!_scalar)
                {
                    ny[i] = Complex.ImaginaryOne * _k0 * new Complex(dny, 0) * ay[i];
                    if (cc != 0)
                    {
                        nx[i] += Complex.ImaginaryOne * _k0 * cc * ay[i] * ay[i] * Complex.Conjugate(ax[i]);
                        ny[i] += Complex.ImaginaryOne * _k0 * cc * ax[i] * ax[i] * Complex.Conjugate(ay[i]);
                    }
                    if (vecRaman) { double d = _m.N2 * fR * w.RamXY[i]; nx[i] += Complex.ImaginaryOne * _k0 * d * ay[i]; ny[i] += Complex.ImaginaryOne * _k0 * d * ax[i]; }
                }
            }
            double peak = PeakIndexChange(ax, ay, nx, ny, _k0);
            SpectralTransform.TimeToFreqInPlace(nx);
            if (!_scalar) SpectralTransform.TimeToFreqInPlace(ny);
            if (_m.EnableSelfSteepening)
                for (int i = 0; i < n; i++)
                {
                    double f = 1.0 + _g.OmegaOffset[i] / _g.OmegaCenter;
                    nx[i] *= f;
                    if (ny != null) ny[i] *= f;
                }
            Array.Copy(nx, 0, rate, 0, n);
            if (!_scalar) Array.Copy(ny, 0, rate, n, n);
            return _k0 * peak;
        }

        public double Nonlinear(Complex[] state, double z, Complex[] rate)
        {
            if (FastPath) return NonlinearFast(state, z, rate);
            int n = _g.N;
            double aeff = AreaAt(z);
            Complex[] axF = new Complex[n], ayF = null;
            Array.Copy(state, 0, axF, 0, n);
            Complex[] ax = null, ay = null;
            if (_scalar) ax = SpectralTransform.FreqToTime(axF);
            else
            {
                ayF = new Complex[n]; Array.Copy(state, n, ayF, 0, n);
                if (ParallelismSettings.MaxDegreeOfParallelism > 1 && n >= ParallelismSettings.MinLengthToParallelize)
                    Parallel.Invoke(() => ax = SpectralTransform.FreqToTime(axF), () => ay = SpectralTransform.FreqToTime(ayF));
                else { ax = SpectralTransform.FreqToTime(axF); ay = SpectralTransform.FreqToTime(ayF); }
            }

            var ix = new double[n]; var iy = new double[n]; var it = new double[n];
            for (int i = 0; i < n; i++)
            {
                ix[i] = (ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary) / aeff;
                if (!_scalar) iy[i] = (ay[i].Real * ay[i].Real + ay[i].Imaginary * ay[i].Imaginary) / aeff;
                it[i] = ix[i] + iy[i];
            }

            var dnx = new double[n]; var dny = new double[n];
            double fR = _m.EffectiveRamanFraction;
            bool kerr = !_m.ReplaceKerr && _m.N2 != 0;
            double[] ram = null, ramX = null, ramY = null, ramXY = null;
            if (kerr)
            {
                if (fR > 0)
                {
                    if (!_scalar && _ramanAnisoFreq != null)
                    {
                        var rxy = new double[n];
                        for (int i = 0; i < n; i++) rxy[i] = (ax[i] * Complex.Conjugate(ay[i])).Real / aeff;
                        VectorRaman(ix, iy, it, rxy, _ramanKernelFreq, _ramanAnisoFreq, _g.DTime, out ramX, out ramY, out ramXY);
                    }
                    else ram = Convolution.CircularConvolveWithKernelFreq(it, _ramanKernelFreq, _g.DTime);
                }
                for (int i = 0; i < n; i++)
                {
                    double rx = fR > 0 ? fR * (ramX != null ? ramX[i] : ram[i]) : 0.0;
                    double ry = fR > 0 ? fR * (ramY != null ? ramY[i] : ram[i]) : 0.0;
                    dnx[i] = _m.N2 * ((1 - fR) * (ix[i] + 2.0 / 3.0 * iy[i]) + rx);
                    dny[i] = _m.N2 * ((1 - fR) * (iy[i] + 2.0 / 3.0 * ix[i]) + ry);
                }
            }
            double[] dnxy = null;
            if (_m.Rotational != null)
            {
                if (_scalar) { var c0 = _m.Rotational.Convolve(ix, _g.DTime); for (int i = 0; i < n; i++) dnx[i] += c0[i]; }
                else
                {
                    var qx = new double[n]; var qy = new double[n]; var qxy = new double[n];
                    for (int i = 0; i < n; i++) { qx[i] = 1.5 * ix[i] - 0.5 * it[i]; qy[i] = 1.5 * iy[i] - 0.5 * it[i]; qxy[i] = 1.5 * (ax[i] * Complex.Conjugate(ay[i])).Real / aeff; }
                    var cx = _m.Rotational.Convolve(qx, _g.DTime); var cy = _m.Rotational.Convolve(qy, _g.DTime); dnxy = _m.Rotational.Convolve(qxy, _g.DTime);
                    for (int i = 0; i < n; i++) { dnx[i] += cx[i]; dny[i] += cy[i]; }
                }
            }
            double[] uRe = null, uIm = null;
            if (_m.ExtraResponses.Count > 0)
            {
                uRe = new double[n]; uIm = new double[n];
                double tArr = _tArrival0 + _tauElement * (z / LengthM);
                for (int i = 0; i < n; i++) _tLab[i] = _g.Time[i] + tArr;
                var ctx = new ResponseContext
                {
                    T = _g.Time, TArrival = tArr, TLab = _tLab, Z = z, ElementLength = LengthM, Intensity = it, IntensityX = ix, IntensityY = iy,
                    InstantIntensity = it, Field = null, Omega0 = _g.OmegaCenter, CarrierResolved = false, EffectiveAreaM2 = aeff, ElementId = _id
                };
                var re = new double[n]; var im = new double[n];
                foreach (var resp in _m.ExtraResponses)
                {
                    Array.Clear(re, 0, n); Array.Clear(im, 0, n);
                    resp.Evaluate(ctx, re, im);
                    for (int i = 0; i < n; i++) { uRe[i] += re[i]; uIm[i] += im[i]; }
                }
            }

            var nx = new Complex[n];
            Complex[] ny = _scalar ? null : new Complex[n];
            double cc = kerr ? _m.N2 * (1 - fR) / (3.0 * aeff) : 0.0;
            for (int i = 0; i < n; i++)
            {
                double ux = uRe != null ? uRe[i] : 0, ui = uIm != null ? uIm[i] : 0;
                var dX = new Complex(dnx[i] + ux, ui);
                nx[i] = Complex.ImaginaryOne * _k0 * dX * ax[i];
                if (!_scalar)
                {
                    var dY = new Complex(dny[i] + ux, ui);
                    ny[i] = Complex.ImaginaryOne * _k0 * dY * ay[i];
                    if (cc != 0)
                    {
                        nx[i] += Complex.ImaginaryOne * _k0 * cc * ay[i] * ay[i] * Complex.Conjugate(ax[i]);
                        ny[i] += Complex.ImaginaryOne * _k0 * cc * ax[i] * ax[i] * Complex.Conjugate(ay[i]);
                    }
                    if (dnxy != null) { nx[i] += Complex.ImaginaryOne * _k0 * dnxy[i] * ay[i]; ny[i] += Complex.ImaginaryOne * _k0 * dnxy[i] * ax[i]; }
                    if (ramXY != null) { double d = _m.N2 * fR * ramXY[i]; nx[i] += Complex.ImaginaryOne * _k0 * d * ay[i]; ny[i] += Complex.ImaginaryOne * _k0 * d * ax[i]; }
                }
            }
            double peak = PeakIndexChange(ax, _scalar ? null : ay, nx, ny, _k0);

            var nxF = SpectralTransform.TimeToFreq(nx);
            var nyF = _scalar ? null : SpectralTransform.TimeToFreq(ny);
            if (_m.EnableSelfSteepening)
            {
                for (int i = 0; i < n; i++)
                {
                    double f = 1.0 + _g.OmegaOffset[i] / _g.OmegaCenter;
                    nxF[i] *= f;
                    if (nyF != null) nyF[i] *= f;
                }
            }
            Array.Copy(nxF, 0, rate, 0, n);
            if (!_scalar) Array.Copy(nyF, 0, rate, n, n);
            return _k0 * peak;
        }
    }

    /// <summary>What a device back-end needs to evaluate an <see cref="EnvelopeProblem"/> itself (arrays in the engine's
    /// centred order: <see cref="FrequencyGrid.OmegaOffset"/> ascending, time centred). See
    /// <see cref="EnvelopeProblem.DeviceSpec"/>.</summary>
    public sealed class EnvelopeDeviceSpec
    {
        public int N;
        public bool Scalar;
        public double K0, N2, RamanFraction, OmegaCenter, DTime, LengthM;
        public bool SelfSteepening;
        public Complex[] RamanKernelFreq, RamanAnisoFreq, LinearRate;
        public double[] OmegaOffset;
        public Func<double, double> AreaAt;
    }

    // ============================================================================== field (carrier-resolved)

    /// <summary>
    /// Maps between the envelope grid and a finer carrier-resolved REAL-field grid with the same time window.
    /// The envelope carrier must sit on a multiple of the frequency step (the grid planner snaps it).
    /// e(t) = sqrt(2) Re[a(t) exp(-i w0 t)], so the cycle-average of e^2 is the power |a|^2.
    /// </summary>
    public sealed class FieldGridMapping
    {
        public FrequencyGrid Envelope { get; }
        public FrequencyGrid Field { get; }
        public int Oversample { get; }
        public int CarrierBin { get; }
        public int DroppedBins { get; }

        public FieldGridMapping(FrequencyGrid env, double highestHarmonic = 3.0, int minOversample = 2)
        {
            Envelope = env;
            double w = env.N / 2 * env.DOmega;
            int m = Math.Max(minOversample, (int)Math.Ceiling((highestHarmonic * env.OmegaCenter + w) / w));
            int p = 1; while (p < m) p <<= 1;
            Oversample = p;
            Field = FrequencyGrid.Create(env.N * p, env.DTime / p, 0.0);
            CarrierBin = (int)Math.Round(env.OmegaCenter / env.DOmega);
            if (Math.Abs(CarrierBin * env.DOmega - env.OmegaCenter) > 1e-6 * env.DOmega)
                throw new InvalidOperationException("Carrier-resolved mode needs the carrier on a multiple of the frequency step (grid.snapCarrier = true).");
            int dropped = 0;
            for (int i = 0; i < env.N; i++) if (FieldIndex(i) <= Field.N / 2) dropped++;
            DroppedBins = dropped;
        }

        public int FieldIndex(int envIndex) => Field.N / 2 + CarrierBin + (envIndex - Envelope.N / 2);

        public Complex[] ToField(Complex[] envSpectrum)
        {
            int nf = Field.N;
            var e = new Complex[nf];
            double s = Oversample / Math.Sqrt(2.0);
            for (int i = 0; i < Envelope.N; i++)
            {
                int j = FieldIndex(i);
                if (j <= nf / 2 || j >= nf) continue;
                e[j] = s * envSpectrum[i];
                e[nf - j] = Complex.Conjugate(e[j]);
            }
            return e;
        }

        public Complex[] ToEnvelope(Complex[] fieldSpectrum)
        {
            var a = new Complex[Envelope.N];
            double s = Math.Sqrt(2.0) / Oversample;
            for (int i = 0; i < Envelope.N; i++)
            {
                int j = FieldIndex(i);
                if (j <= Field.N / 2 || j >= Field.N) continue;
                a[i] = s * fieldSpectrum[j];
            }
            return a;
        }

        /// <summary>Positive-frequency axis of the field grid (omega &gt; 0), carrier at CarrierBin - 1.</summary>
        public OmegaAxis PositiveAxis()
        {
            int nf = Field.N;
            var w = new double[nf / 2 - 1];
            for (int k = 0; k < w.Length; k++) w[k] = Field.OmegaAbsolute[nf / 2 + 1 + k];
            return new OmegaAxis(w, CarrierBin - 1);
        }
    }

    /// <summary>
    /// Carrier-resolved scalar (x) propagation of the real field e(t) (unidirectional, full-field Kerr):
    /// dE(w)/dz = L(w) E + i (w/c) F[dn_re e] - (|w|/c) F[dn_im e], with
    /// dn = n2 [ (1-fR) (2/3) e^2 / Aeff + fR h_R * I_env ] + user responses. The (2/3) makes the
    /// cycle-averaged SPM identical to the envelope model; third-harmonic generation and self-steepening come
    /// out automatically. User responses see t_lab with optical-cycle resolution.
    /// </summary>
    public sealed class FieldProblem : IPropagationProblem
    {
        /// <summary>Index at the carrier (handed to responses, e.g. chi(2)).</summary>
        public double N0 { get; set; } = double.NaN;
        private bool _dealias => _m.ExtraResponses.Any(r => r is Chi2Response);
        private readonly FieldGridMapping _map;
        private readonly NonlinearMedium _m;
        private readonly IBeamModel _beam;
        private readonly Complex[] _ramanKernelFreq;
        private readonly double _tArrival0, _tauElement;
        private readonly string _id;
        private readonly double[] _tLab;
        public int Length { get; }
        public double LengthM { get; }
        public Complex[] LinearRate { get; }

        public FieldProblem(FieldGridMapping map, NonlinearMedium medium, IBeamModel beam, double lengthM, double[] phPos, double[] ampPos,
            double tArrivalAtStart, double elementCarrierGroupDelay, string elementId)
        {
            _map = map; _m = medium; _beam = beam; LengthM = lengthM; _id = elementId;
            _tArrival0 = tArrivalAtStart; _tauElement = elementCarrierGroupDelay;
            var g = map.Field;
            int nf = g.N;
            Length = nf;
            LinearRate = new Complex[nf];
            for (int k = 0; k < phPos.Length; k++)
            {
                int j = nf / 2 + 1 + k;
                var r = EnvelopeProblem.Rate(phPos[k], ampPos[k], lengthM);
                LinearRate[j] = r;
                LinearRate[nf - j] = Complex.Conjugate(r);
            }
            if (medium.EffectiveRamanFraction > 0)
            {
                var h = EnvelopeProblem.NormalizedKernel(medium.Raman.Evaluate(g.Time), g.DTime);
                var hc = new Complex[nf];
                for (int i = 0; i < nf; i++) hc[i] = h[i];
                _ramanKernelFreq = SpectralTransform.TimeToFreq(hc);
            }
            _tLab = new double[nf];
        }

        public double AreaAt(double z) => _beam.EffectiveArea(Math.Max(0, Math.Min(LengthM, z)));

        public double Nonlinear(Complex[] state, double z, Complex[] rate)
        {
            var g = _map.Field;
            int nf = g.N;
            double aeff = AreaAt(z);
            // analytic signal: 2x positive frequencies, DC once, negatives dropped
            var an = new Complex[nf];
            an[nf / 2] = state[nf / 2];
            for (int j = nf / 2 + 1; j < nf; j++) an[j] = 2.0 * state[j];
            var a = SpectralTransform.FreqToTime(an);
            var e = new double[nf]; var ienv = new double[nf]; var iinst = new double[nf];
            for (int i = 0; i < nf; i++)
            {
                e[i] = a[i].Real;
                ienv[i] = (a[i].Real * a[i].Real + a[i].Imaginary * a[i].Imaginary) / (2 * aeff);
                iinst[i] = e[i] * e[i] / aeff;
            }
            var dnRe = new double[nf]; var dnIm = new double[nf];
            double fR = _m.EffectiveRamanFraction;
            if (!_m.ReplaceKerr && _m.N2 != 0)
            {
                double[] ram = fR > 0 ? Convolution.CircularConvolveWithKernelFreq(ienv, _ramanKernelFreq, g.DTime) : null;
                for (int i = 0; i < nf; i++) dnRe[i] = _m.N2 * ((1 - fR) * (2.0 / 3.0) * iinst[i] + (fR > 0 ? fR * ram[i] : 0));
            }
            if (_m.Rotational != null) { var cr = _m.Rotational.Convolve(ienv, g.DTime); for (int i = 0; i < nf; i++) dnRe[i] += cr[i]; }
            if (_m.ExtraResponses.Count > 0)
            {
                double tArr = _tArrival0 + _tauElement * (z / LengthM);
                for (int i = 0; i < nf; i++) _tLab[i] = g.Time[i] + tArr;
                var ctx = new ResponseContext
                {
                    T = g.Time, TArrival = tArr, TLab = _tLab, Z = z, ElementLength = LengthM, Intensity = ienv, IntensityX = ienv, IntensityY = new double[nf],
                    InstantIntensity = iinst, Field = e, Omega0 = _map.Envelope.OmegaCenter, CarrierResolved = true, EffectiveAreaM2 = aeff, ElementId = _id, N0 = N0
                };
                var re = new double[nf]; var im = new double[nf];
                foreach (var r in _m.ExtraResponses)
                {
                    Array.Clear(re, 0, nf); Array.Clear(im, 0, nf);
                    r.Evaluate(ctx, re, im);
                    for (int i = 0; i < nf; i++) { dnRe[i] += re[i]; dnIm[i] += im[i]; }
                }
            }
            double peak = 0;
            var gr = new Complex[nf]; var gi = new Complex[nf];
            bool anyIm = false;
            for (int i = 0; i < nf; i++)
            {
                gr[i] = dnRe[i] * e[i];
                if (dnIm[i] != 0) { gi[i] = dnIm[i] * e[i]; anyIm = true; }
                // B-integral uses the cycle-averaged index change (2/3 of the instantaneous peak for Kerr)
                double pk = Math.Abs(dnRe[i]);
                if (pk > peak) peak = pk;
            }
            var GR = SpectralTransform.TimeToFreq(gr);
            var GI = anyIm ? SpectralTransform.TimeToFreq(gi) : null;
            double c = PhysicalConstants.SpeedOfLightMPerS, w0 = _map.Envelope.OmegaCenter;
            for (int j = 0; j < nf / 2; j++) rate[j] = Complex.Zero;   // negative frequencies are not propagated (analytic signal)
            // quadratic (chi(2)) products alias around the Nyquist frequency and break energy conservation: drop the
            // nonlinear drive above 2/3 of the grid's bandwidth (the "2/3 rule"); raise physics.fieldHarmonic for more room
            double wCut = _dealias ? (2.0 / 3.0) * g.OmegaAbsolute[nf - 1] : double.PositiveInfinity;
            for (int j = nf / 2 + 1; j < nf; j++)
            {
                if (g.OmegaAbsolute[j] > wCut) { rate[j] = Complex.Zero; continue; }
                double w = g.OmegaAbsolute[j];
                double ws = _m.EnableSelfSteepening ? w : Math.Sign(w) * w0;
                var v = Complex.ImaginaryOne * (ws / c) * GR[j];
                if (GI != null) v -= (Math.Abs(ws) / c) * GI[j];
                rate[j] = v;
            }
            rate[nf / 2] = Complex.Zero;
            return peak * w0 / c * 0.75; // cycle-average of (2/3)*2*I -> I : report envelope-equivalent peak
        }
    }

    // ============================================================================== steppers

    public sealed class StepperSettings
    {
        /// <summary>"adaptive" (ERK4(3)-IP, default) or "rk4ip" (fixed steps).</summary>
        public string Method { get; set; } = "adaptive";
        /// <summary>Local relative error tolerance for the adaptive method.</summary>
        public double Tolerance { get; set; } = 1e-6;
        /// <summary>Fixed method: maximum nonlinear phase per step (rad). Adaptive: initial-step guess.</summary>
        public double MaxNonlinearPhasePerStepRad { get; set; } = 0.01;
        public double MaxAreaChangePerStep { get; set; } = 0.05;
        public double MinStepM { get; set; } = 1e-9;
        public double MaxStepM { get; set; } = double.PositiveInfinity;
        public int MaxSteps { get; set; } = 2_000_000;
        /// <summary>Fixed method: optional explicit step count (0 = choose automatically).</summary>
        public int FixedSteps { get; set; }
    }

    /// <summary>Interaction-picture Runge-Kutta integrators (Hult 2007; Balac &amp; Mahe 2013).</summary>
    /// <summary>A problem with linear position-dependent terms in its N operator (index / absorption profiles): their
    /// rate limits the step like a nonlinear phase but is not counted in the B-integral.</summary>
    public interface IStaticRate { double StaticPhaseRate { get; } }

    public static class Steppers
    {
        private static double StaticLimit(IPropagationProblem p, StepperSettings st)
            => p is IStaticRate s && s.StaticPhaseRate > 0 ? 0.2 / s.StaticPhaseRate : double.PositiveInfinity;   // linear terms: RK4 is accurate to ~1e-5 at 0.2 rad per step

        private static Complex[] ExpL(Complex[] L, double h) { var r = new Complex[L.Length]; ComplexKernels.Exp(L, h, r); return r; }

        private static void Mul(Complex[] a, Complex[] b, Complex[] dst) => ComplexKernels.Multiply(a, b, dst);
        private static Complex[] Axpy(Complex[] y, double s, Complex[] x) { var r = new Complex[y.Length]; ComplexKernels.Axpy(y, s, x, r); return r; }

        private static double Norm(Complex[] a) { double s = 0; for (int i = 0; i < a.Length; i++) s += a[i].Real * a[i].Real + a[i].Imaginary * a[i].Imaginary; return Math.Sqrt(s); }

        /// <summary>Propagate with the current compute back-end (CPU unless the run selected another that supports the problem).</summary>
        public static Complex[] Propagate(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress = null)
        {
            var b = ComputeBackends.Current;
            if (b != null && !(b is CpuBackend))
            {
                if (b is AutoBackend) return b.Propagate(p, a0, st, stats, progress);
                if (b.Supports(p)) { stats.Notes.Add("compute back-end: " + b.Name); return b.Propagate(p, a0, st, stats, progress); }
                stats.Notes.Add(b.Name + ": " + ((b as IDeviceBackend)?.WhyNot(p) ?? "this element is not supported") + "; ran on the CPU");
            }
            return PropagateCpu(p, a0, st, stats, progress);
        }

        /// <summary>The managed CPU steppers.</summary>
        public static Complex[] PropagateCpu(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress = null)
        {
            if (string.Equals(st.Method, "rk4ip", StringComparison.OrdinalIgnoreCase) || string.Equals(st.Method, "fixed", StringComparison.OrdinalIgnoreCase))
                return FixedRk4Ip(p, a0, st, stats, progress);
            return AdaptiveErk43Ip(p, a0, st, stats, progress);
        }

        /// <summary>Step limit from the beam-size variation: |ln A(z+h)/A(z)| &lt;= limit.</summary>
        private static double AreaLimitedStep(IPropagationProblem p, double z, double h, double limit)
        {
            for (int it = 0; it < 40; it++)
            {
                double a0 = p.AreaAt(z), a1 = p.AreaAt(z + h), am = p.AreaAt(z + 0.5 * h);
                if (a0 <= 0 || a1 <= 0) return h;
                double v = Math.Abs(Math.Log(am / a0)) + Math.Abs(Math.Log(a1 / am));
                if (v <= limit) return h;
                h *= 0.5;
            }
            return h;
        }

        /// <summary>Classic RK4IP with a fixed step count chosen from the estimated B-integral and beam variation.</summary>
        public static Complex[] FixedRk4Ip(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress)
        {
            int n = p.Length;
            var tmp = new Complex[n];
            double peakRate = p.Nonlinear(a0, 0, tmp);
            int steps = st.FixedSteps;
            if (steps <= 0)
            {
                double b = 2.0 * peakRate * p.LengthM;
                long fromB = (long)Math.Ceiling(b / Math.Max(1e-9, st.MaxNonlinearPhasePerStepRad));
                double logVar = 0; int samples = 1001; double prev = p.AreaAt(0);
                for (int i = 1; i < samples; i++) { double ar = p.AreaAt(p.LengthM * i / (samples - 1)); if (ar > 0 && prev > 0) logVar += Math.Abs(Math.Log(ar / prev)); prev = ar; }
                long fromA = (long)Math.Ceiling(logVar / Math.Max(1e-6, st.MaxAreaChangePerStep));
                long fromMax = double.IsInfinity(st.MaxStepM) ? 1 : (long)Math.Ceiling(p.LengthM / st.MaxStepM);
                double hs = StaticLimit(p, st); if (!double.IsInfinity(hs)) fromMax = Math.Max(fromMax, (long)Math.Ceiling(p.LengthM / hs));
                steps = (int)Math.Min(st.MaxSteps, Math.Max(1, Math.Max(fromB, Math.Max(fromA, fromMax))));
            }
            double h = p.LengthM / steps;
            var half = ExpL(p.LinearRate, 0.5 * h);
            var a = (Complex[])a0.Clone();
            var k = new Complex[n];
            int every = Math.Max(1, steps / 200);
            stats.Method = "rk4ip (fixed)";
            for (int s = 0; s < steps; s++)
            {
                double z = s * h;
                var aI = new Complex[n]; Mul(half, a, aI);
                double pk = p.Nonlinear(a, z, k);
                var k1 = new Complex[n]; Mul(half, k, k1);
                p.Nonlinear(Axpy(aI, 0.5 * h, k1), z + 0.5 * h, k); var k2 = (Complex[])k.Clone();
                p.Nonlinear(Axpy(aI, 0.5 * h, k2), z + 0.5 * h, k); var k3 = (Complex[])k.Clone();
                var t4 = Axpy(aI, h, k3); Mul(half, t4, t4);
                p.Nonlinear(t4, z + h, k); var k4 = k;
                var beta = new Complex[n];
                for (int i = 0; i < n; i++) beta[i] = aI[i] + h / 6.0 * (k1[i] + 2.0 * k2[i] + 2.0 * k3[i]);
                Mul(half, beta, beta);
                for (int i = 0; i < n; i++) a[i] = beta[i] + h / 6.0 * k4[i];
                k = new Complex[n];
                stats.Accepted++; stats.BIntegralRad += pk * h;
                stats.MinStepM = Math.Min(stats.MinStepM, h); stats.MaxStepM = Math.Max(stats.MaxStepM, h);
                if (progress != null && ((s + 1) % every == 0 || s == steps - 1)) progress((s + 1.0) / steps);
            }
            return a;
        }

        /// <summary>
        /// Embedded ERK4(3)-IP with first-same-as-last (Balac &amp; Mahe, Comput. Phys. Commun. 184, 1211 (2013)):
        /// the 4th-order RK4IP solution A4 and a 3rd-order companion A3 = beta + h/30 (2 k4 + 3 k5), k5 = N(A4),
        /// share all stages; the relative difference |A4 - A3| / |A4| controls the step
        /// (h_new = h min(2, max(0.5, 0.9 (tol/err)^(1/4)))). k5 is reused as the next step's k1 (FSAL).
        /// </summary>
        public static Complex[] AdaptiveErk43Ip(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress)
        {
            int n = p.Length;
            double L = p.LengthM;
            var a = (Complex[])a0.Clone();
            var kN = new Complex[n];
            double pk = p.Nonlinear(a, 0, kN); // N(A) at z = 0 (FSAL seed)
            double h = pk > 0 ? Math.Min(L, st.MaxNonlinearPhasePerStepRad / pk) : L;
            h = Math.Min(h, st.MaxStepM);
            double z = 0;
            var bp = p as IBreakpoints;
            Complex[] half = new Complex[n], aI = new Complex[n], k1 = new Complex[n], k2 = new Complex[n], k3 = new Complex[n], k4 = new Complex[n],
                      k5 = new Complex[n], tmp = new Complex[n], beta = new Complex[n], a4 = new Complex[n];
            stats.Method = "erk4(3)-ip (adaptive, tol " + st.Tolerance.ToString("G2", System.Globalization.CultureInfo.InvariantCulture) + ")";
            int guard = 0;
            double lastReport = 0;
            while (z < L * (1 - 1e-12))
            {
                if (++guard > st.MaxSteps) { stats.Notes.Add("maximum step count reached at z = " + z.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " m; finished with the last step size"); }
                h = Math.Min(h, L - z);
                h = Math.Min(h, st.MaxStepM);
                h = Math.Min(h, StaticLimit(p, st));
                h = AreaLimitedStep(p, z, h, st.MaxAreaChangePerStep);
                h = Math.Max(h, Math.Min(st.MinStepM, L - z));
                bool onWall = false;
                if (bp != null)
                {
                    double zb = bp.NextBreakpoint(z + 1e-12 * L);
                    if (zb < z + h * (1 + 1e-12)) { h = zb - z; onWall = true; }
                }
                double zEnd = onWall ? z + h * (1 - 1e-9) : z + h; // evaluate end-of-step stages on this side of a wall
                ComplexKernels.Exp(p.LinearRate, 0.5 * h, half);
                Mul(half, a, aI);
                Mul(half, kN, k1);
                ComplexKernels.Axpy(aI, 0.5 * h, k1, tmp); p.Nonlinear(tmp, z + 0.5 * h, k2);
                ComplexKernels.Axpy(aI, 0.5 * h, k2, tmp); p.Nonlinear(tmp, z + 0.5 * h, k3);
                ComplexKernels.Axpy(aI, h, k3, tmp); Mul(half, tmp, tmp);
                p.Nonlinear(tmp, zEnd, k4);
                for (int i = 0; i < n; i++) beta[i] = aI[i] + h / 6.0 * (k1[i] + 2.0 * k2[i] + 2.0 * k3[i]);
                Mul(half, beta, beta);
                for (int i = 0; i < n; i++) a4[i] = beta[i] + h / 6.0 * k4[i];
                double pk5 = p.Nonlinear(a4, zEnd, k5);
                double num = 0;
                for (int i = 0; i < n; i++)
                {
                    var a3 = beta[i] + h / 30.0 * (2.0 * k4[i] + 3.0 * k5[i]);
                    var d = a4[i] - a3;
                    num += d.Real * d.Real + d.Imaginary * d.Imaginary;
                }
                double den = Norm(a4);
                double err = den > 0 ? Math.Sqrt(num) / den : 0.0;
                bool forced = h <= st.MinStepM * 1.0000001 || guard > st.MaxSteps;
                if (err <= st.Tolerance || forced)
                {
                    stats.Accepted++;
                    stats.BIntegralRad += 0.5 * (pk + pk5) * h;
                    stats.MinStepM = Math.Min(stats.MinStepM, h); stats.MaxStepM = Math.Max(stats.MaxStepM, h);
                    z += h; pk = pk5;
                    { var t = a; a = a4; a4 = t; t = kN; kN = k5; k5 = t; }   // a <- a4, kN <- k5 (buffers swapped, not copied)
                    if (onWall && z < L * (1 - 1e-12)) pk = p.Nonlinear(a, z + 1e-9 * h, kN); // the other side of the wall
                    if (forced && err > st.Tolerance && !stats.Notes.Contains("tolerance not met at minimum step")) stats.Notes.Add("tolerance not met at minimum step");
                    if (progress != null && (z / L - lastReport >= 0.005 || z >= L * (1 - 1e-12))) { lastReport = z / L; progress(Math.Min(1.0, z / L)); }
                }
                else stats.Rejected++;
                double factor = err > 0 ? 0.9 * Math.Pow(st.Tolerance / err, 0.25) : 2.0;
                h *= Math.Min(2.0, Math.Max(0.5, factor));
                if (guard > st.MaxSteps + 10) break;
            }
            return a;
        }
    }
}
