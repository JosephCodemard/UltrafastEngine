using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;

namespace UltrafastEngine.Optics
{
    /// <summary>Pairs of ordinary / extraordinary built-in materials for birefringent optics (waveplates).</summary>
    public static class BirefringentMaterials
    {
        private static readonly Dictionary<string, (string O, string E)> Pairs = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            { "Quartz", ("Quartz", "Quartz-e") }, { "CrystalQuartz", ("Quartz", "Quartz-e") },
            { "MgF2", ("MgF2", "MgF2-e") }, { "Sapphire", ("Sapphire", "Sapphire-e") },
            { "Calcite", ("Calcite", "Calcite-e") }, { "YVO4", ("YVO4", "YVO4-e") },
            { "aBBO", ("aBBO", "aBBO-e") }, { "alpha-BBO", ("aBBO", "aBBO-e") },
        };

        public static IEnumerable<string> Names => new[] { "Quartz", "MgF2", "Sapphire", "Calcite", "YVO4", "aBBO" };

        /// <summary>Ordinary and extraordinary material names for a birefringent crystal, or null.</summary>
        public static (string O, string E)? Get(string name) => name != null && Pairs.TryGetValue(name.Trim(), out var p) ? p : ((string, string)?)null;
    }

    // ============================================================================================ birefringent plate

    /// <summary>
    /// A thick birefringent plate (multi-order, true zero-order or a compound zero-order pair) with the full material
    /// dispersion of both axes: J = R(θ) diag(exp(i n_f ω d / c), exp(i n_s ω d / c)) R(-θ) per plate, where f / s are
    /// the fast and slow axes (o or e depending on the sign of the birefringence). A compound plate is a second plate of
    /// thickness d2 with its axes crossed. The retardance Γ(ω) = (n_s - n_f) ω (d1 - d2) / c therefore follows the
    /// material dispersion, and the plate's common dispersion (and the group delay difference between the axes) is
    /// part of the term.
    /// </summary>
    public sealed class BirefringentPlateTerm : JonesTerm
    {
        public Material Ordinary { get; } public Material Extraordinary { get; }
        public double ThicknessM { get; } public double SecondThicknessM { get; } public double AxisAngleRad { get; }
        private readonly string _label;

        public BirefringentPlateTerm(string label, Material o, Material e, double thicknessM, double secondThicknessM, double fastAxisRad)
        {
            _label = label; Ordinary = o; Extraordinary = e; ThicknessM = thicknessM; SecondThicknessM = secondThicknessM; AxisAngleRad = fastAxisRad;
            Mechanism = "polarization";
        }

        public override string Description => string.Format(CultureInfo.InvariantCulture, "{0}: birefringent plate {1}/{2}, d = {3:G5} mm{4}, fast axis {5:G4} deg",
            _label, Ordinary.Name, Extraordinary.Name, ThicknessM * 1e3, SecondThicknessM > 0 ? " - " + (SecondThicknessM * 1e3).ToString("G5", CultureInfo.InvariantCulture) + " mm (compound)" : "", AxisAngleRad * 180 / Math.PI);

        /// <summary>True if n_e &gt; n_o at omega (positive uniaxial: the ordinary axis is fast).</summary>
        public bool PositiveAt(double w) => Extraordinary.IndexAt(w) > Ordinary.IndexAt(w);

        public double RetardanceRad(double w)
        {
            double no = Ordinary.IndexAt(w), ne = Extraordinary.IndexAt(w);
            return Math.Abs(ne - no) * w * (ThicknessM - SecondThicknessM) / PhysicalConstants.SpeedOfLightMPerS;
        }

        protected override void Jones(double[] omega, Complex[] xx, Complex[] xy, Complex[] yx, Complex[] yy)
        {
            int n = omega.Length;
            var no = new double[n]; var ne = new double[n];
            Ordinary.Index.Evaluate(omega, no); Extraordinary.Index.Evaluate(omega, ne);
            // the fast/slow assignment uses the sign of the birefringence at the first finite sample (it does not change sign in the bands used)
            bool positive = true;
            for (int i = 0; i < n; i++) if (!double.IsNaN(no[i]) && !double.IsNaN(ne[i])) { positive = ne[i] > no[i]; break; }
            double c = PhysicalConstants.SpeedOfLightMPerS;
            for (int i = 0; i < n; i++)
            {
                double w = omega[i];
                if (w <= 0 || double.IsNaN(no[i]) || double.IsNaN(ne[i])) { xx[i] = xy[i] = yx[i] = yy[i] = Complex.Zero; continue; }
                double nf = positive ? no[i] : ne[i], ns = positive ? ne[i] : no[i];
                // first plate: fast axis at theta
                JonesSpectrum.RotatedDiagonal(AxisAngleRad, Complex.FromPolarCoordinates(1, nf * w * ThicknessM / c), Complex.FromPolarCoordinates(1, ns * w * ThicknessM / c), out var a11, out var a12, out var a21, out var a22);
                if (SecondThicknessM > 0)
                {
                    // second plate: fast axis crossed (theta + 90 deg)
                    JonesSpectrum.RotatedDiagonal(AxisAngleRad + Math.PI / 2, Complex.FromPolarCoordinates(1, nf * w * SecondThicknessM / c), Complex.FromPolarCoordinates(1, ns * w * SecondThicknessM / c), out var b11, out var b12, out var b21, out var b22);
                    xx[i] = b11 * a11 + b12 * a21; xy[i] = b11 * a12 + b12 * a22;
                    yx[i] = b21 * a11 + b22 * a21; yy[i] = b21 * a12 + b22 * a22;
                }
                else { xx[i] = a11; xy[i] = a12; yx[i] = a21; yy[i] = a22; }
            }
        }

        /// <summary>Mean group delay of the two axes (the frame follows the average).</summary>
        public override double GroupDelayAt(double omega)
        {
            double c = PhysicalConstants.SpeedOfLightMPerS;
            double ng = 0.5 * (Ordinary.GroupIndexAt(omega) + Extraordinary.GroupIndexAt(omega));
            return ng * (ThicknessM + SecondThicknessM) / c;
        }
    }

    // ============================================================================================ Faraday rotation

    /// <summary>
    /// Faraday rotation by θ(ω) = θ_d (λ_d² - λ0²) / (λ² - λ0²) (Verdet constant V ∝ 1 / (λ² - λ0²); λ0 = 0 gives the
    /// 1/λ² law), or a constant angle. Non-reciprocal: the sign follows the propagation direction relative to the field
    /// ("direction": -1 for the return pass of an isolator), so two passes add rather than cancel.
    /// </summary>
    public sealed class FaradayTerm : JonesTerm
    {
        public double DesignAngleRad { get; } public double DesignOmega { get; } public double PoleWavelengthM { get; } public bool Dispersive { get; }
        public FaradayTerm(double designAngleRad, double designOmega, double poleWavelengthM, bool dispersive)
        { DesignAngleRad = designAngleRad; DesignOmega = designOmega; PoleWavelengthM = poleWavelengthM; Dispersive = dispersive; Mechanism = "polarization"; }

        public override string Description => string.Format(CultureInfo.InvariantCulture, "Faraday rotation {0:G5} deg at {1:G5} nm{2}", DesignAngleRad * 180 / Math.PI,
            2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / DesignOmega * 1e9, Dispersive ? ", V ~ 1/(lambda^2 - " + (PoleWavelengthM * 1e9).ToString("G4", CultureInfo.InvariantCulture) + " nm^2)" : "");

        public double AngleAt(double w)
        {
            if (!Dispersive || w <= 0) return DesignAngleRad;
            double c = PhysicalConstants.SpeedOfLightMPerS, l = 2 * Math.PI * c / w, ld = 2 * Math.PI * c / DesignOmega, l0 = PoleWavelengthM;
            return DesignAngleRad * (ld * ld - l0 * l0) / (l * l - l0 * l0);
        }

        protected override void Jones(double[] omega, Complex[] xx, Complex[] xy, Complex[] yx, Complex[] yy)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double a = AngleAt(omega[i]), cs = Math.Cos(a), sn = Math.Sin(a);
                xx[i] = cs; xy[i] = -sn; yx[i] = sn; yy[i] = cs;
            }
        }
        public override double GroupDelayAt(double omega) => 0;
    }

    // ============================================================================================ ghosts

    /// <summary>
    /// Ghost replicas of a plate: the part of the beam reflected twice inside (back surface, then front surface) leaves
    /// with the main beam, delayed by the extra round trip. The transmitted field becomes E (1 + Σ_k g^k e^{ikψ(ω)})
    /// per s / p with g = r_back r_front' (internal reflections), ψ = 2 n(ω) ω d cos θ_t / c, the extra internal
    /// absorption of the round trips, and a mode-overlap factor η_k for the lateral offset (2 k d tan θ_t cos θ) and the
    /// angular deviation of a wedge. The frame (the main pulse) is not shifted: the replicas appear at +k τ_ghost.
    /// Energy brought back into the beam is booked as a negative "ghost" loss.
    /// </summary>
    public sealed class GhostTerm : LinearTerm
    {
        private readonly Func<double, (Complex Gs, Complex Gp)> _g;       // single round-trip factor (incl. phase, absorption)
        private readonly double[] _overlap;
        private readonly double _b11, _b12, _b21, _b22;
        public int Orders => _overlap.Length;
        public double DelayS { get; }

        public GhostTerm(Func<double, (Complex Gs, Complex Gp)> roundTrip, double[] overlapPerOrder, double[] basis, double delayS)
        {
            _g = roundTrip; _overlap = overlapPerOrder; _b11 = basis[0]; _b12 = basis[1]; _b21 = basis[2]; _b22 = basis[3]; DelayS = delayS;
            Mechanism = "ghost";
        }

        public override string Description => string.Format(CultureInfo.InvariantCulture, "ghost replicas ({0} order{1}, +{2:G4} ps each, overlap {3})", Orders, Orders > 1 ? "s" : "", DelayS * 1e12,
            string.Join("/", _overlap.Select(x => x.ToString("G3", CultureInfo.InvariantCulture))));
        public override double GroupDelayAt(double omega) => 0;
        public override DispersionPoint DispersionAt(double omega) => new DispersionPoint();

        public override JonesSpectrum Evaluate(OmegaAxis axis)
        {
            int n = axis.N;
            var xx = new Complex[n]; var xy = new Complex[n]; var yx = new Complex[n]; var yy = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                double w = axis.Omega[i];
                Complex hs = 1, hp = 1;
                if (w > 0)
                {
                    var (gs, gp) = _g(w);
                    Complex ps = 1, pp = 1;
                    for (int k = 0; k < _overlap.Length; k++) { ps *= gs; pp *= gp; hs += _overlap[k] * ps; hp += _overlap[k] * pp; }
                    if (double.IsNaN(hs.Real) || double.IsNaN(hp.Real)) { hs = 1; hp = 1; }
                }
                xx[i] = _b11 * _b11 * hs + _b21 * _b21 * hp;
                xy[i] = _b11 * _b12 * hs + _b21 * _b22 * hp;
                yx[i] = xy[i];
                yy[i] = _b12 * _b12 * hs + _b22 * _b22 * hp;
            }
            return JonesSpectrum.Full(xx, xy, yx, yy);
        }

        /// <summary>Field overlap of a replica displaced by s and tilted by δ with the main Gaussian beam of radius w:
        /// exp(-s²/(2w²)) exp(-(π w δ / λ)² / 2).</summary>
        public static double Overlap(double shiftM, double tiltRad, double beamRadiusM, double lambdaM)
        {
            if (!(beamRadiusM > 0)) return 1.0;
            return Math.Exp(-shiftM * shiftM / (2 * beamRadiusM * beamRadiusM)) * Math.Exp(-0.5 * Math.Pow(Math.PI * beamRadiusM * tiltRad / lambdaM, 2));
        }
    }

    // ============================================================================================ GDD ripple

    /// <summary>
    /// GDD ripple of a dispersive (chirped) coating: GDD_r(ω) = A sin(2π (ω - ω0) / Ω + φ), i.e. a phase
    /// -A (Ω / 2π)² sin(2π (ω - ω0) / Ω + φ), per bounce. Complementary pairs ("pair": true) alternate φ and φ + π so
    /// the ripple of an even number of bounces cancels.
    /// </summary>
    public sealed class GddRippleTerm : PhaseTerm
    {
        public double AmplitudeS2 { get; } public double PeriodRadPerS { get; } public double PhaseRad { get; } public int Bounces { get; } public bool Pair { get; }
        public double OmegaRef { get; }
        public GddRippleTerm(double amplitudeS2, double periodRadPerS, double phaseRad, double omegaRef, int bounces, bool pair)
        {
            AmplitudeS2 = amplitudeS2; PeriodRadPerS = periodRadPerS; PhaseRad = phaseRad; OmegaRef = omegaRef; Bounces = Math.Max(1, bounces); Pair = pair; Mechanism = "dispersion";
        }
        public override string Description => string.Format(CultureInfo.InvariantCulture, "GDD ripple {0:G4} fs^2, period {1:G4} THz, {2} bounce(s){3}", AmplitudeS2 * 1e30, PeriodRadPerS / (2 * Math.PI) * 1e-12, Bounces, Pair ? " (complementary pairs)" : "");

        /// <summary>Effective number of bounces adding with the same phase (pairs: odd remainder only).</summary>
        public double Effective => Pair ? Bounces % 2 : Bounces;

        protected override void Phase(double[] omega, double[] phi)
        {
            double k = 2 * Math.PI / PeriodRadPerS, a = Effective * AmplitudeS2 / (k * k);
            for (int i = 0; i < omega.Length; i++) phi[i] = -a * Math.Sin(k * (omega[i] - OmegaRef) + PhaseRad);
        }
    }

    // ============================================================================================ Zernike / figure

    /// <summary>Zernike polynomials, Noll ordering and normalisation (rms = coefficient over the unit disc).</summary>
    public static class Zernike
    {
        /// <summary>Noll index j (1-based) -> radial order n and azimuthal m (negative = sine term).</summary>
        public static (int N, int M) Noll(int j)
        {
            if (j < 1) throw new ArgumentException("Noll index starts at 1");
            int n = 0, j1 = j - 1;
            while (j1 > n) { n++; j1 -= n; }
            int m = ((j % 2 == 0) ? 1 : -1) * ((n % 2) + 2 * ((j1 + ((n + 1) % 2)) / 2));
            return (n, m);
        }

        public static double Radial(int n, int m, double rho)
        {
            m = Math.Abs(m);
            double s = 0;
            for (int k = 0; k <= (n - m) / 2; k++)
                s += (k % 2 == 0 ? 1 : -1) * Fact(n - k) / (Fact(k) * Fact((n + m) / 2 - k) * Fact((n - m) / 2 - k)) * Math.Pow(rho, n - 2 * k);
            return s;
        }

        private static double Fact(int k) { double f = 1; for (int i = 2; i <= k; i++) f *= i; return f; }

        /// <summary>Noll-normalised Zernike j at (rho, theta).</summary>
        public static double Value(int j, double rho, double theta)
        {
            var (n, m) = Noll(j);
            double r = Radial(n, m, rho);
            if (m == 0) return Math.Sqrt(n + 1) * r;
            return Math.Sqrt(2.0 * (n + 1)) * r * (m > 0 ? Math.Cos(m * theta) : Math.Sin(-m * theta));
        }
    }

    /// <summary>
    /// A wavefront error W(x, y) in metres of optical path: Zernike terms (in waves at a reference wavelength over a
    /// normalisation radius), and/or a random surface figure (Gaussian-correlated, given rms and correlation length,
    /// seeded). The wavefront is an optical path difference, applied as a phase ω W / c at every frequency
    /// (non-dispersive: exact for mirrors).
    /// </summary>
    public sealed class Aberration
    {
        public List<(int J, double Waves)> Terms { get; } = new List<(int, double)>();
        public double RadiusM { get; set; } = double.NaN;
        public double ReferenceWavelengthM { get; set; }
        /// <summary>Factor from the given numbers to wavefront (2 cos θ for a surface figure on a mirror, n - 1 for a
        /// refracting surface; 1 when the numbers already are wavefront).</summary>
        public double SurfaceFactor { get; set; } = 1.0;
        public double RandomRmsM { get; set; }
        public double CorrelationLengthM { get; set; } = double.NaN;
        public int Seed { get; set; } = 1;
        public bool Any => Terms.Any(t => t.Waves != 0) || RandomRmsM > 0;

        public string Describe()
        {
            var parts = new List<string>();
            if (Terms.Count > 0) parts.Add("Zernike " + string.Join(", ", Terms.Where(t => t.Waves != 0).Select(t => "Z" + t.J + " = " + t.Waves.ToString("G3", CultureInfo.InvariantCulture))) + " waves over r = " + (RadiusM * 1e3).ToString("G4", CultureInfo.InvariantCulture) + " mm");
            if (RandomRmsM > 0) parts.Add(string.Format(CultureInfo.InvariantCulture, "random figure {0:G3} nm rms, correlation {1:G3} mm (seed {2})", RandomRmsM * 1e9, CorrelationLengthM * 1e3, Seed));
            if (SurfaceFactor != 1) parts.Add("x " + SurfaceFactor.ToString("G4", CultureInfo.InvariantCulture) + " (surface -> wavefront)");
            return string.Join("; ", parts);
        }

        /// <summary>Optical path difference (m) of the Zernike part at (x, y).</summary>
        public double Opd(double x, double y, bool radialOnly)
        {
            double r = Math.Sqrt(x * x + y * y) / RadiusM, th = Math.Atan2(y, x), s = 0;
            foreach (var (j, a) in Terms)
            {
                if (a == 0) continue;
                if (radialOnly && Zernike.Noll(j).M != 0) continue;
                s += a * Zernike.Value(j, r, th);
            }
            return s * ReferenceWavelengthM * SurfaceFactor;
        }

        /// <summary>Random figure (m of wavefront) on a cartesian grid, Gaussian correlation function, rms RandomRmsM.</summary>
        public double[] RandomFigure(Transverse.CartesianBasis b)
        {
            int nx = b.Nx, ny = b.Ny, M = b.M;
            var rnd = new Random(Seed);
            var f = new Complex[M];
            for (int m = 0; m < M; m++)
            {
                double u1 = 1 - rnd.NextDouble(), u2 = rnd.NextDouble();
                f[m] = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            }
            // filter with a Gaussian of correlation length L: PSD ~ exp(-k^2 L^2 / 4)
            double L = double.IsNaN(CorrelationLengthM) ? 0.1 * b.Extent : CorrelationLengthM;
            var g = (Complex[])f.Clone();
            b.ToK(g, 1);
            for (int m = 0; m < M; m++) g[m] *= Math.Exp(-b.K2[m] * L * L / 8);
            b.ToSpace(g, 1);
            double mean = g.Average(z => z.Real), var = g.Average(z => (z.Real - mean) * (z.Real - mean));
            double sc = var > 0 ? RandomRmsM * SurfaceFactor / Math.Sqrt(var) : 0;
            return g.Select(z => (z.Real - mean) * sc).ToArray();
        }

        /// <summary>Maréchal estimate of the Strehl ratio for a Gaussian beam (1/e² radius w) at wavelength λ: the
        /// intensity-weighted wavefront rms after removing piston and tilt.</summary>
        public (double RmsM, double Strehl) StrehlForGaussian(double w, double lambda)
        {
            int ng = 41;
            double sw = 0;
            var xs = new List<double>(); var ys = new List<double>(); var ws = new List<double>(); var vs = new List<double>();
            for (int i = 0; i < ng; i++)
                for (int k = 0; k < ng; k++)
                {
                    double x = (i - (ng - 1) / 2.0) / ng * 5 * w, y = (k - (ng - 1) / 2.0) / ng * 5 * w;
                    double wt = Math.Exp(-2 * (x * x + y * y) / (w * w));
                    xs.Add(x); ys.Add(y); ws.Add(wt); vs.Add(Opd(x, y, false)); sw += wt;
                }
            // weighted least squares fit a + b x + c y (piston, tilt)
            double S(Func<int, double> f) { double s = 0; for (int i = 0; i < xs.Count; i++) s += ws[i] * f(i); return s; }
            double s1 = S(i => 1), sx = S(i => xs[i]), sy = S(i => ys[i]), sxx = S(i => xs[i] * xs[i]), syy = S(i => ys[i] * ys[i]), sxy = S(i => xs[i] * ys[i]);
            double sv = S(i => vs[i]), sxv = S(i => xs[i] * vs[i]), syv = S(i => ys[i] * vs[i]);
            var A = new[,] { { s1, sx, sy }, { sx, sxx, sxy }, { sy, sxy, syy } };
            var rhs = new[] { sv, sxv, syv };
            var p = Solve3(A, rhs);
            double rms2 = S(i => { double r = vs[i] - p[0] - p[1] * xs[i] - p[2] * ys[i]; return r * r; }) / s1;
            if (RandomRmsM > 0) rms2 += RandomRmsM * SurfaceFactor * RandomRmsM * SurfaceFactor;
            double rms = Math.Sqrt(rms2);
            return (rms, Math.Exp(-Math.Pow(2 * Math.PI * rms / lambda, 2)));
        }

        private static double[] Solve3(double[,] a, double[] b)
        {
            double det(double[,] m) => m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            double d = det(a);
            if (Math.Abs(d) < 1e-300) return new double[3];
            var r = new double[3];
            for (int k = 0; k < 3; k++)
            {
                var m = (double[,])a.Clone();
                for (int i = 0; i < 3; i++) m[i, k] = b[i];
                r[k] = det(m) / d;
            }
            return r;
        }
    }
}
