using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Crystals
{
    /// <summary>One field of a coupled-wave problem: its carrier, grid, polarisation role and z-wavevector.</summary>
    public sealed class Chi2Field
    {
        public string Name;
        public string Role;          // "slow" / "fast"
        public double Omega0;
        public FrequencyGrid Grid;
        public double AngleRad;      // angle of k to the reference (signal) direction
        public double[] Kz;          // kz(omega) on Grid (rad/m, absolute)
        public double[] Alpha;       // power absorption (1/m) on Grid, or null
        public double N0, Ng;        // index and group index at the carrier
        public double RadiusM;       // 1-D model: 1/e^2 radius of the assumed Gaussian mode
        public double KxDrift;       // cartesian model: lateral drift (walk-off + non-collinear) dx/dz
        public Complex[] Initial;    // spectrum (1-D: N; spatial: M*N in k-space)
        public bool Generated;       // starts from zero (or noise)
    }

    /// <summary>
    /// Coupled-wave chi(2) propagation (plus chi(3) SPM/XPM) of 2 or 3 fields sharing one time grid, each on its own
    /// carrier. State = [field 0; field 1; (field 2)], each N (1-D, sqrt(W)) or M*N (spatial, k-space, sqrt(W/m^2)).
    ///
    /// Linear: L_j = i [kz_j(w) - kz_j(w_j0) - (w - w_j0)/v_ref] - alpha_j/2 (+ diffraction + lateral drift in
    /// spatial models) -- exact dispersion and group-velocity mismatch of every field.
    /// Three-wave (w3 = w1 + w2):  dA1 = i w1 G O A3 A2* e^{-i phi},  dA2 = i w2 G O A3 A1* e^{-i phi},
    /// dA3 = i w3 G O A1 A2 e^{+i phi}, phi(z) = dk0 z - Phi_QPM(z), G = deff sqrt(2/(eps0 c^3 n1 n2 n3)).
    /// Degenerate SHG: dAF = i wF G O AH AF* e^{-i phi}, dAH = i wF G O AF^2 e^{+i phi}, G = deff sqrt(2/(eps0 c^3 nF^2 nH)).
    /// O is the overlap of the assumed Gaussian modes (1-D model) or 1 (spatial models, local coupling).
    /// Domain-resolved QPM: the sign of d flips at <see cref="Walls"/>; the stepper lands on every wall.
    /// These conserve photon numbers exactly (Manley-Rowe) for any O.
    /// </summary>
    public sealed class Chi2Problem : IPropagationProblem, IBreakpoints
    {
        private readonly Chi2Field[] _f;
        private readonly int _n, _M, _K;
        private readonly ITransverseBasis _b;
        private readonly bool _shg;
        private readonly double _gamma, _overlap, _dk0;
        /// <summary>1-D model of crossing beams: the fraction of the coupling left at z, or null.</summary>
        public Func<double, double> Crossing { get; set; }
        private readonly Func<double, double> _qpmPhase;   // Phi_QPM(z) (averaged model) or null
        private readonly Func<double, double> _domainSign; // sign of d(z) (domain model) or null
        private readonly double _n2;
        private readonly bool _steep;
        private readonly double[,] _xpmArea; // 1-D: P/I conversion for SPM (diag) and XPM (off-diag), m^2
        private readonly double[,] _xpmFactor;
        /// <summary>Start (fraction of the half window) and strength (amplitude e-folds at the edge over the crystal) of the absorbing spectral boundary.</summary>
        public const double EdgeStart = 0.9, EdgeStrength = 12;
        public int Length { get; }
        public double LengthM { get; }
        public Complex[] LinearRate { get; }
        public double MaxStepHint { get; }
        /// <summary>Sorted QPM domain-wall positions (domain-resolved model), or null.</summary>
        public double[] Walls { get; set; }

        public double NextBreakpoint(double z)
        {
            if (Walls == null || Walls.Length == 0) return double.PositiveInfinity;
            int i = Array.BinarySearch(Walls, z);
            i = i >= 0 ? i + 1 : ~i;
            return i < Walls.Length ? Walls[i] : double.PositiveInfinity;
        }

        public Chi2Problem(Chi2Field[] fields, bool degenerateShg, double gamma, double overlap, double deltaK0, double lengthM,
            ITransverseBasis basis, Func<double, double> qpmPhase, Func<double, double> domainSign, double n2, bool selfSteepening,
            double vRefInv, bool paraxial, double maxStepHint)
        {
            _f = fields; _K = fields.Length; _shg = degenerateShg; _gamma = gamma; _overlap = overlap; _dk0 = deltaK0;
            _b = basis; _M = basis?.M ?? 1; _n = fields[0].Grid.N; LengthM = lengthM;
            _qpmPhase = qpmPhase; _domainSign = domainSign; _n2 = n2; _steep = selfSteepening; MaxStepHint = maxStepHint;
            Length = _K * _M * _n;
            LinearRate = new Complex[Length];
            for (int j = 0; j < _K; j++)
            {
                var f = fields[j]; var g = f.Grid;
                var ph = new double[_n]; var amp = new double[_n];
                int c = g.CarrierIndex;
                for (int i = 0; i < _n; i++)
                {
                    double w = g.OmegaAbsolute[i];
                    double kz = f.Kz[i];
                    if (double.IsNaN(kz) || w <= 0) { ph[i] = 0; amp[i] = 0; continue; }
                    ph[i] = (kz - f.Kz[c] - (w - f.Omega0) * vRefInv) * lengthM;
                    amp[i] = f.Alpha != null ? Math.Exp(-0.5 * f.Alpha[i] * lengthM) : 1.0;
                    // absorbing spectral boundary: the outer 10 % of the window damps content that would otherwise
                    // wrap around (alias) through the time-domain products; the energy removed is reported
                    double x = Math.Abs(g.OmegaOffset[i]) / (0.5 * g.N * g.DOmega);
                    if (x > EdgeStart) { double u = (x - EdgeStart) / (1 - EdgeStart); amp[i] *= Math.Exp(-EdgeStrength * u * u); }
                }
                Complex[] rate;
                if (basis == null)
                {
                    rate = new Complex[_n];
                    for (int i = 0; i < _n; i++) rate[i] = EnvelopeProblem.Rate(ph[i], amp[i], lengthM);
                }
                else
                {
                    // diffraction uses the field's own index: k = kz / cos(angle) ~ n w / c
                    double cosA = Math.Cos(f.AngleRad);
                    var kx = f.KxDrift != 0 ? Enumerable.Repeat(-f.KxDrift, _n).ToArray() : null;
                    var nArr = new double[_n];
                    for (int i = 0; i < _n; i++) { double w = g.OmegaAbsolute[i]; nArr[i] = w > 0 && !double.IsNaN(f.Kz[i]) ? f.Kz[i] / cosA * PhysicalConstants.SpeedOfLightMPerS / w : 1.0; }
                    rate = SpatialEnvelopeProblem.BuildLinearRate(g, basis, lengthM, ph, amp, w => InterpIndex(g, nArr, w), paraxial, kx);
                }
                Array.Copy(rate, 0, LinearRate, j * _M * _n, rate.Length);
            }
            // 1-D chi(3): SPM area pi w^2/2, XPM overlap pi (wj^2 + wk^2)/2 with factor 2 (same role) or 2/3 (orthogonal)
            _xpmArea = new double[_K, _K]; _xpmFactor = new double[_K, _K];
            for (int a = 0; a < _K; a++)
                for (int b = 0; b < _K; b++)
                {
                    double wa = fields[a].RadiusM, wb = fields[b].RadiusM;
                    _xpmArea[a, b] = a == b ? Math.PI * wa * wa / 2 : Math.PI * (wa * wa + wb * wb) / 2;
                    _xpmFactor[a, b] = a == b ? 1.0 : (fields[a].Role == fields[b].Role ? 2.0 : 2.0 / 3.0);
                }
        }

        private static double InterpIndex(FrequencyGrid g, double[] n, double w)
        {
            double x = (w - g.OmegaAbsolute[0]) / g.DOmega;
            int i = (int)Math.Floor(x);
            if (i < 0) return n[0];
            if (i >= g.N - 1) return n[g.N - 1];
            double t = x - i; return n[i] * (1 - t) + n[i + 1] * t;
        }

        public double AreaAt(double z) => 1.0;

        public double Nonlinear(Complex[] state, double z, Complex[] rate)
        {
            int n = _n, M = _M, K = _K, blk = M * n;
            var data = new Complex[K][];
            for (int j = 0; j < K; j++)
            {
                data[j] = new Complex[blk];
                Array.Copy(state, j * blk, data[j], 0, blk);
                _b?.ToSpace(data[j], n);
            }
            double phi = _dk0 * z - (_qpmPhase?.Invoke(z) ?? 0);
            double sgn = _domainSign?.Invoke(z) ?? 1.0;
            var ePlus = Complex.FromPolarCoordinates(1, phi); var eMinus = Complex.Conjugate(ePlus);
            double g0 = _gamma * sgn * _overlap * (Crossing?.Invoke(z) ?? 1.0);
            double peak = 0; object lk = new object();
            Parallel.For(0, M, () => 0.0, (m, st, localPeak) =>
            {
                var t = new Complex[K][];
                for (int j = 0; j < K; j++) { var row = new Complex[n]; Array.Copy(data[j], m * n, row, 0, n); SpectralTransform.FreqToTimeInPlace(row); t[j] = row; }
                var nt = new Complex[K][];
                for (int j = 0; j < K; j++) nt[j] = new Complex[n];
                double lp = localPeak;
                for (int i = 0; i < n; i++)
                {
                    if (_shg)
                    {
                        Complex aF = t[0][i], aH = t[1][i];
                        double wF = _f[0].Omega0;
                        nt[0][i] = Complex.ImaginaryOne * wF * g0 * aH * Complex.Conjugate(aF) * eMinus;
                        nt[1][i] = Complex.ImaginaryOne * wF * g0 * aF * aF * ePlus;
                    }
                    else
                    {
                        Complex a1 = t[0][i], a2 = t[1][i], a3 = t[2][i];
                        nt[0][i] = Complex.ImaginaryOne * _f[0].Omega0 * g0 * a3 * Complex.Conjugate(a2) * eMinus;
                        nt[1][i] = Complex.ImaginaryOne * _f[1].Omega0 * g0 * a3 * Complex.Conjugate(a1) * eMinus;
                        nt[2][i] = Complex.ImaginaryOne * _f[2].Omega0 * g0 * a1 * a2 * ePlus;
                    }
                    if (_n2 != 0)
                    {
                        for (int a = 0; a < K; a++)
                        {
                            double dn = 0;
                            for (int b = 0; b < K; b++)
                            {
                                double pb = t[b][i].Real * t[b][i].Real + t[b][i].Imaginary * t[b][i].Imaginary;
                                dn += _xpmFactor[a, b] * (_b == null ? pb / _xpmArea[a, b] : pb);
                            }
                            dn *= _n2;
                            double ka = _f[a].Omega0 / PhysicalConstants.SpeedOfLightMPerS;
                            nt[a][i] += Complex.ImaginaryOne * ka * dn * t[a][i];
                            if (ka * dn > lp) lp = ka * dn;
                        }
                    }
                }
                for (int j = 0; j < K; j++)
                {
                    var nf = nt[j]; SpectralTransform.TimeToFreqInPlace(nf);
                    if (_steep) { var g = _f[j].Grid; for (int i = 0; i < n; i++) nf[i] *= 1.0 + g.OmegaOffset[i] / g.OmegaCenter; }
                    Array.Copy(nf, 0, data[j], m * n, n);
                }
                return lp;
            }, lp => { lock (lk) peak = Math.Max(peak, lp); });
            for (int j = 0; j < K; j++)
            {
                _b?.ToK(data[j], n);
                Array.Copy(data[j], 0, rate, j * blk, blk);
            }
            // step guide: chi(2) coupling rate ~ w G O |A|, plus chi(3)
            double amax = 0;
            for (int j = 0; j < K; j++) for (int i = 0; i < blk; i += Math.Max(1, blk / 4096)) amax = Math.Max(amax, state[j * blk + i].Magnitude);
            return Math.Max(peak, 1e-30);
        }
    }
}
