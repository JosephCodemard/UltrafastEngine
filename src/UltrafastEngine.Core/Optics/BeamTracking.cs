using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Optics
{
    /// <summary>
    /// An astigmatic Gaussian beam described by its two complex beam parameters (x and y), stored in REDUCED form
    /// q̂ = q / n (vacuum-equivalent distances), so that the ABCD law needs no index bookkeeping at interfaces:
    /// <code>1/q̂ = n/R - i λ0 M² / (π w²)</code> with λ0 the vacuum wavelength. Propagation over a length L in a medium
    /// of index n adds L/n; a thin lens subtracts 1/f; a curved mirror in a medium of index n subtracts 2n/R_eff with
    /// the off-axis focal lengths f_t = R cos θ / 2 (tangential plane) and f_s = R / (2 cos θ) (sagittal plane).
    /// Simple astigmatism only: the principal axes are the lab x and y (a fold plane at an arbitrary angle is
    /// snapped to the nearer axis, with a note).
    /// <para>
    /// Spatio-temporal couplings (Kostenbauder, IEEE J. Quantum Electron. 26, 1148 (1990)): per axis the beam also
    /// carries the spatial dispersion E = dx/dω (m s) and the angular dispersion F = dθ̂/dω (s, reduced angle θ̂ = n θ)
    /// of its frequency components, measured from the carrier's ray. An element with ABCD matrix and its own E_el, F_el
    /// maps them as E' = A E + B F + E_el, F' = C E + D F + F_el; the field is then
    /// U(x, ω) = A(ω) u_q(x - E Δω) exp(i k0 F Δω (x - E Δω)), with A(ω) the field on each frequency's own (chief) ray.
    /// Keeping A on the chief ray costs a spectral phase per element. It is computed exactly: five rays at ω0 + j h (j = -2 .. 2)
    /// are traced with the beam through gaps, thin lenses and tilted surfaces (Snell, grating equation), each collecting its
    /// optical phase (excess path k n s, grating phase 2 π m y / Λ, lens phase). Differences across the bundle give the GDD, TOD and
    /// FOD of each element (<see cref="TakePendingDispersion"/>), to all orders of θ(ω). To second order these are
    /// k0 (A C E^2 + 2 B C E F + B D F^2) for an ABCD step (k0 B F^2 for a gap, -k0 E^2 / f for a lens) and 2 k0 a F_el E_in for a
    /// dispersing surface; after a system that cancels the couplings the total equals the Kostenbauder time element: Treacy for a
    /// grating pair, 0 for a 4f zero-dispersion stretcher.
    /// </para>
    /// </summary>
    public sealed class BeamState
    {
        /// <summary>Spatial dispersion dx/dω (m s) in x and y.</summary>
        public double Ex { get; private set; }
        public double Ey { get; private set; }
        /// <summary>Angular dispersion dθ̂/dω (s, reduced angle n θ) in x and y.</summary>
        public double Fx { get; private set; }
        public double Fy { get; private set; }
        // the ray bundle: position and real angle from the carrier's ray per axis, and the phase collected since the last take
        private const int Rays = 5;
        private double[] _xr = new double[Rays], _ar = new double[Rays], _yr = new double[Rays], _br = new double[Rays], _phase = new double[Rays];
        private double Omega0 => K0 * PhysicalConstants.SpeedOfLightMPerS;
        private double RayStep => 0.004 * Omega0;
        /// <summary>Index against frequency of the present medium; null means <see cref="MediumIndex"/> at all frequencies.</summary>
        private Func<double, double> _mediumOf;
        private double RayOmega(int j) => Omega0 + (j - Rays / 2) * RayStep;
        private double RayK(int j) => RayOmega(j) / PhysicalConstants.SpeedOfLightMPerS;

        /// <summary>GDD, TOD (s^2, s^3) and FOD (s^4) of the phase collected by the steps since the last call; clears it.</summary>
        public (double Gdd, double Tod, double Fod) TakePendingDispersion()
        {
            var p = _phase; double h = RayStep;
            var r = ((-p[0] + 16 * p[1] - 30 * p[2] + 16 * p[3] - p[4]) / (12 * h * h),
                     (p[4] - 2 * p[3] + 2 * p[1] - p[0]) / (2 * h * h * h),
                     (p[4] - 4 * p[3] + 6 * p[2] - 4 * p[1] + p[0]) / (h * h * h * h));
            _phase = new double[Rays];
            return r;
        }
        /// <summary>The (reduced) ray matrix [A, B, C, D] accumulated since the beam was made, per axis.</summary>
        public double[] MatrixX { get; private set; } = { 1, 0, 0, 1 };
        public double[] MatrixY { get; private set; } = { 1, 0, 0, 1 };

        /// <summary>Product a b of two [A, B, C, D] matrices.</summary>
        public static double[] Multiply(double[] a, double[] b) => new[] { a[0] * b[0] + a[1] * b[2], a[0] * b[1] + a[1] * b[3], a[2] * b[0] + a[3] * b[2], a[2] * b[1] + a[3] * b[3] };

        /// <summary>The matrix between two accumulated matrices of the same beam (unit determinant): after = segment x before.</summary>
        public static double[] Segment(double[] before, double[] after) => Multiply(after, new[] { before[3], -before[1], -before[2], before[0] });

        private void Step(bool x, double a, double b, double c, double d)
        {
            if (x) MatrixX = Multiply(new[] { a, b, c, d }, MatrixX); else MatrixY = Multiply(new[] { a, b, c, d }, MatrixY);
            // the step has been applied to q already, so go back one step; the on-axis field goes as 1 / sqrt(A + B / q_in)
            var qOut = x ? Qx : Qy;
            var qIn = (d * qOut - b) / (-c * qOut + a);
            GouyRad += 0.5 * (a + b / qIn).Phase;
        }

        /// <summary>Accumulated Gouy phase of the on-axis field (rad, both planes), for E = Re{A exp(-i w t)}:
        /// -atan(z / z_R) from a waist to z. Not in the pulse's field: it goes to the path ledger.</summary>
        public double GouyRad { get; private set; }

        /// <summary>True when the beam carries spatial or angular dispersion.</summary>
        public bool HasCouplings => Ex != 0 || Fx != 0 || Ey != 0 || Fy != 0;
        /// <summary>Vacuum wavenumber of the carrier (rad/m).</summary>
        public double K0 => 2 * Math.PI / Lambda0;
        /// <summary>Reduced complex beam parameters (m).</summary>
        public Complex Qx { get; private set; }
        public Complex Qy { get; private set; }
        /// <summary>Vacuum wavelength (m).</summary>
        public double Lambda0 { get; }
        public double M2x { get; } = 1;
        public double M2y { get; } = 1;
        /// <summary>Index of the medium the beam is in (for reporting the real wavefront curvature).</summary>
        public double MediumIndex { get; private set; } = 1;
        /// <summary>Distance travelled since the system input (m, geometric).</summary>
        public double PathM { get; private set; }

        public BeamState(Complex qx, Complex qy, double lambda0, double m2x = 1, double m2y = 1, double n = 1)
        {
            Qx = qx; Qy = qy; Lambda0 = lambda0; M2x = m2x; M2y = m2y; MediumIndex = n;
        }

        public BeamState Clone() => new BeamState(Qx, Qy, Lambda0, M2x, M2y, MediumIndex)
        {
            _mediumOf = _mediumOf, GouyRad = GouyRad, PathM = PathM, MatrixX = MatrixX, MatrixY = MatrixY, Ex = Ex, Ey = Ey, Fx = Fx, Fy = Fy, _xr = (double[])_xr.Clone(), _ar = (double[])_ar.Clone(), _yr = (double[])_yr.Clone(), _br = (double[])_br.Clone(), _phase = (double[])_phase.Clone()
        };

        /// <summary>Beam from an inputBeam / beam spec of kind "gaussian": w0M (or w0xM/w0yM), zWaistM (waist position
        /// measured from the system input, positive = waist downstream), n, m2, wavelengthM (default: the carrier).</summary>
        public static BeamState FromGaussianSpec(JsonObject j, double defaultLambda0)
        {
            if (j == null || !string.Equals(j.GetString("kind", "gaussian"), "gaussian", StringComparison.OrdinalIgnoreCase)) return null;
            double lam = j.GetDouble("wavelengthM", defaultLambda0);
            double n = j.GetDouble("n", 1.0);
            double m2 = j.GetDouble("m2", 1.0), m2x = j.GetDouble("m2x", m2), m2y = j.GetDouble("m2y", m2);
            double w0 = j.GetDouble("w0M", double.NaN);
            double w0x = j.GetDouble("w0xM", w0), w0y = j.GetDouble("w0yM", w0);
            double zw = j.GetDouble("zWaistM", 0), zwx = j.GetDouble("zWaistXM", zw), zwy = j.GetDouble("zWaistYM", zw);
            double zrx, zry;
            if (j.Has("zRayleighM") || j.Has("zRayleighXM"))
            {
                zrx = j.GetDouble("zRayleighXM", j.GetDouble("zRayleighM", double.NaN));
                zry = j.GetDouble("zRayleighYM", j.GetDouble("zRayleighM", double.NaN));
            }
            else
            {
                if (double.IsNaN(w0x) || double.IsNaN(w0y)) return null;
                zrx = Math.PI * w0x * w0x * n / (m2x * lam);
                zry = Math.PI * w0y * w0y * n / (m2y * lam);
            }
            // actual q at z = 0: q = (0 - zw) + i zR ; reduced q̂ = q / n
            return new BeamState(new Complex(-zwx, zrx) / n, new Complex(-zwy, zry) / n, lam, m2x, m2y, n);
        }

        private static Complex Abcd(Complex q, double a, double b, double c, double d) => (a * q + b) / (c * q + d);

        /// <summary>Free propagation over a geometric length in a medium of index n.</summary>
        public void Propagate(double lengthM, double n, Func<double, double> nOf = null)
        {
            if (!(n > 0)) n = 1;
            EnterMedium(n, nOf);
            Qx += lengthM / n; Qy += lengthM / n; MediumIndex = n; PathM += lengthM;
            Step(true, 1, lengthM / n, 0, 1); Step(false, 1, lengthM / n, 0, 1);
            for (int j = 0; j < Rays; j++)
            {
                _phase[j] += RayK(j) * RayIndex(j) * lengthM * (1 / Math.Cos(_ar[j]) + 1 / Math.Cos(_br[j]) - 2);
                _xr[j] += lengthM * Math.Tan(_ar[j]); _yr[j] += lengthM * Math.Tan(_br[j]);
            }
            Ex += lengthM / n * Fx; Ey += lengthM / n * Fy;
        }

        /// <summary>Index of the present medium for ray j.</summary>
        private double RayIndex(int j) => _mediumOf != null ? _mediumOf(RayOmega(j)) : MediumIndex;

        /// <summary>The rays cross a normal surface into a new medium: each refracts by its own index, while the reduced beam
        /// quantities do not change.</summary>
        private void EnterMedium(double n, Func<double, double> nOf)
        {
            if (nOf == null) { if (n != MediumIndex) _mediumOf = null; return; }
            bool same = true;
            for (int j = 0; j < Rays; j++) same &= RayIndex(j) == nOf(RayOmega(j));
            if (same) { _mediumOf = nOf; return; }
            var before = Enumerable.Range(0, Rays).Select(RayIndex).ToArray();
            RaySurface(0, 0, true, j => (before[j], nOf(RayOmega(j)), 0.0));
            RaySurface(0, 0, false, j => (before[j], nOf(RayOmega(j)), 0.0));
            _mediumOf = nOf;
        }

        /// <summary>Thin lens (focal lengths in x and y; infinity = no power in that plane).</summary>
        public void Lens(double fx, double fy)
        {
            if (!double.IsInfinity(fx) && fx != 0) { Qx = Abcd(Qx, 1, 0, -1 / fx, 1); Fx -= Ex / fx; RayLens(fx, 1, true); Step(true, 1, 0, -1 / fx, 1); }
            if (!double.IsInfinity(fy) && fy != 0) { Qy = Abcd(Qy, 1, 0, -1 / fy, 1); Fy -= Ey / fy; RayLens(fy, 1, false); Step(false, 1, 0, -1 / fy, 1); }
        }

        /// <summary>Lens power in a medium of index n (curved mirror in that medium): reduced C = -n/f.</summary>
        public void LensInMedium(double fx, double fy, double n)
        {
            if (!double.IsInfinity(fx) && fx != 0) { Qx = Abcd(Qx, 1, 0, -n / fx, 1); Fx -= n * Ex / fx; RayLens(fx, n, true); Step(true, 1, 0, -n / fx, 1); }
            if (!double.IsInfinity(fy) && fy != 0) { Qy = Abcd(Qy, 1, 0, -n / fy, 1); Fy -= n * Ey / fy; RayLens(fy, n, false); Step(false, 1, 0, -n / fy, 1); }
        }

        /// <summary>Refraction at a flat surface at angle of incidence theta1 from n1 into n2: the tangential axis is
        /// magnified (reduced ABCD diag(cos θ2 / cos θ1, cos θ1 / cos θ2)); the sagittal axis is unchanged.
        /// <para>With the index functions <paramref name="n1Of"/>, <paramref name="n2Of"/> (of ω) the refraction also disperses the beam in
        /// the plane of incidence (Snell's law at fixed incidence): F_el = n2 dθ2/dω = (sin θ1 dn1/dω - sin θ2 dn2/dω) / cos θ2, with
        /// the angles signed in the unfolded beam (the same sign on both faces of a plate, opposite signs on the faces of
        /// a prism), and the ray bundle crosses the tilted surface exactly.</para></summary>
        public void TiltedInterface(double n1, double n2, double theta1, bool tangentialIsX, Func<double, double> n1Of = null, Func<double, double> n2Of = null)
        {
            if (Math.Abs(theta1) < 1e-12) { EnterMedium(n2, n2Of); MediumIndex = n2; return; }
            MediumIndex = n2;
            double s1 = Math.Sin(theta1), s2 = n1 * s1 / n2;
            if (Math.Abs(s2) >= 1) return; // total internal reflection: not a refracting beam
            double c1 = Math.Cos(theta1), c2 = Math.Sqrt(1 - s2 * s2);
            double a = c2 / c1, d = c1 / c2;
            if (tangentialIsX) Qx = Abcd(Qx, a, 0, 0, d); else Qy = Abcd(Qy, a, 0, 0, d);
            Step(tangentialIsX, a, 0, 0, d);
            if (n1Of == null || n2Of == null) { Disperse(a, d, 0, tangentialIsX); return; }
            double h = 1e-4 * Omega0, dn1 = (n1Of(Omega0 + h) - n1Of(Omega0 - h)) / (2 * h), dn2 = (n2Of(Omega0 + h) - n2Of(Omega0 - h)) / (2 * h);
            Disperse(a, d, (s1 * dn1 - s2 * dn2) / c2, tangentialIsX);
            RaySurface(theta1, Math.Asin(s2), tangentialIsX, j => (n1Of(RayOmega(j)), n2Of(RayOmega(j)), 0.0));
            _mediumOf = n2Of;
        }

        /// <summary>
        /// A grating of period <paramref name="periodM"/> used in order <paramref name="order"/> at the signed angle of
        /// incidence θ_in: sin θ_out = sin θ_in + m λ0 / Λ (unfolded beam; a second grating that undoes the dispersion
        /// has θ_in = θ_out of the first and the opposite order). Anamorphic magnification cos θ_out / cos θ_in in the
        /// plane of incidence and angular dispersion dθ_out/dω = -2 π c m / (ω0^2 Λ cos θ_out). Returns the
        /// diffraction angle, or NaN when the order is evanescent.
        /// </summary>
        public double Grating(double thetaIn, double periodM, int order, bool tangentialIsX)
        {
            double so = Math.Sin(thetaIn) + order * Lambda0 / periodM;
            if (Math.Abs(so) >= 1) return double.NaN;
            double to = Math.Asin(so), ci = Math.Cos(thetaIn), co = Math.Cos(to);
            double a = co / ci, d = ci / co;
            if (tangentialIsX) Qx = Abcd(Qx, a, 0, 0, d); else Qy = Abcd(Qy, a, 0, 0, d);
            Step(tangentialIsX, a, 0, 0, d);
            double w0 = Omega0;
            double fel = -2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS * order / (w0 * w0 * periodM * co);
            Disperse(a, d, fel, tangentialIsX);
            RaySurface(thetaIn, to, tangentialIsX, j => (1.0, 1.0, order * 2 * Math.PI / periodM));
            return to;
        }

        /// <summary>
        /// One ray per frequency through a flat surface whose normal makes the angle theta1 with the carrier's incoming ray and
        /// theta2 with its outgoing ray. A ray is carried to the surface (position y along it), obeys n2 k sin(out) = n1 k sin(in) + G
        /// (G: grating vector, 0 for refraction), collects the grating phase G y, and is carried to the plane normal to the outgoing
        /// carrier; every leg is signed optical path, so a ray that meets the tilted surface early or late gets the right phase.
        /// </summary>
        private void RaySurface(double theta1, double theta2, bool x, Func<int, (double N1, double N2, double G)> medium)
        {
            var pos = x ? _xr : _yr; var ang = x ? _ar : _br;
            double c1 = Math.Cos(theta1), s1 = Math.Sin(theta1), c2 = Math.Cos(theta2), s2 = Math.Sin(theta2);
            for (int j = 0; j < Rays; j++)
            {
                var (n1, n2, g) = medium(j);
                double k = RayK(j), d = ang[j], x0 = pos[j];
                double t = x0 * s1 / Math.Cos(d + theta1);                               // along the ray to the surface
                double y = x0 * c1 + t * Math.Sin(d) * c1 + t * Math.Cos(d) * s1;           // position along the surface
                double sout = (n1 * k * Math.Sin(theta1 + d) + g) / (n2 * k);
                if (Math.Abs(sout) >= 1) continue;
                double dOut = Math.Asin(sout) - theta2;
                double zOut = y * s2, xOut = y * c2;                                         // on the surface, in the outgoing frame
                double t2 = -zOut / Math.Cos(dOut);                                          // back to the plane at the origin
                _phase[j] += k * n1 * t + g * y + k * n2 * t2;
                pos[j] = xOut + t2 * Math.Sin(dOut); ang[j] = dOut;
            }
        }

        /// <summary>A thin lens on the bundle: phase -k n x^2 / 2 f_eff in reduced form, reduced angle changed by -n x / f.</summary>
        private void RayLens(double f, double n, bool x)
        {
            var pos = x ? _xr : _yr; var ang = x ? _ar : _br;
            double m = MediumIndex > 0 ? MediumIndex : 1;
            for (int j = 0; j < Rays; j++)
            {
                double xx = pos[j];
                _phase[j] -= RayK(j) * n * xx * xx / (2 * f);
                ang[j] = Math.Atan(Math.Tan(ang[j]) - n / m * xx / f);
            }
        }

        /// <summary>
        /// Couplings through an element with the diagonal matrix (a, 0, 0, d) and its own angular dispersion
        /// <paramref name="fel"/> in one plane. Its tilt phase k0 F_el Δω x, written about the chief ray x = E' Δω,
        /// leaves k0 F_el E' Δω^2 on the spectrum: GDD 2 k0 F_el a E_in.
        /// </summary>
        private void Disperse(double a, double d, double fel, bool x)
        {
            double e = x ? Ex : Ey, f = x ? Fx : Fy;
            double e2 = a * e, f2 = d * f + fel;
            if (x) { Ex = e2; Fx = f2; } else { Ey = e2; Fy = f2; }
        }

        /// <summary>
        /// An element that shifts every frequency sideways by <paramref name="eel"/> Δω (m s) in one plane without
        /// changing the beam otherwise (the output of a single-pass grating pair, whose own phase is exact): the field
        /// is displaced with its chief rays, so the spectrum is unchanged.
        /// </summary>
        public void AddSpatialDispersion(double eel, bool x)
        {
            if (x) Ex += eel; else Ey += eel;
            var pos = x ? _xr : _yr;
            for (int j = 0; j < Rays; j++) pos[j] += eel * (RayOmega(j) - Omega0);
        }

        /// <summary>Forget the spatio-temporal couplings (physics.spatioTemporal = "off").</summary>
        public void ClearCouplings() { Ex = Ey = Fx = Fy = 0; _xr = new double[Rays]; _ar = new double[Rays]; _yr = new double[Rays]; _br = new double[Rays]; _phase = new double[Rays]; }

        /// <summary>A general (reduced) ABCD matrix per axis (no dispersion of its own).</summary>
        public void Apply(double ax, double bx, double cx, double dx, double ay, double by, double cy, double dy)
        {
            Qx = Abcd(Qx, ax, bx, cx, dx); Qy = Abcd(Qy, ay, by, cy, dy);
            Step(true, ax, bx, cx, dx); Step(false, ay, by, cy, dy);
            double ex = Ex, ey = Ey;
            for (int j = 0; j < Rays; j++)
            {
                // the matrices are reduced, so the rays' slopes enter times their index
                double k = RayK(j), nj = RayIndex(j), xx = _xr[j], aa = nj * Math.Tan(_ar[j]), yy = _yr[j], bb = nj * Math.Tan(_br[j]);
                _phase[j] += k / 2 * (ax * cx * xx * xx + 2 * bx * cx * xx * aa + bx * dx * aa * aa + ay * cy * yy * yy + 2 * by * cy * yy * bb + by * dy * bb * bb);
                _xr[j] = ax * xx + bx * aa; _ar[j] = Math.Atan((cx * xx + dx * aa) / nj);
                _yr[j] = ay * yy + by * bb; _br[j] = Math.Atan((cy * yy + dy * bb) / nj);
            }
            Ex = ax * ex + bx * Fx; Fx = cx * ex + dx * Fx;
            Ey = ay * ey + by * Fy; Fy = cy * ey + dy * Fy;
        }

        public double RadiusX => Radius(Qx, M2x);
        public double RadiusY => Radius(Qy, M2y);

        private double Radius(Complex q, double m2)
        {
            double im = (1 / q).Imaginary;
            return im < 0 ? Math.Sqrt(-Lambda0 * m2 / (Math.PI * im)) : double.NaN;
        }

        /// <summary>Wavefront radius of curvature in the current medium (m; +inf flat; positive diverging).</summary>
        public double CurvatureX => Curv(Qx);
        public double CurvatureY => Curv(Qy);
        private double Curv(Complex q) { double re = (1 / q).Real; return Math.Abs(re) < 1e-300 ? double.PositiveInfinity : MediumIndex / re; }

        /// <summary>Distance to the waist (m, in a medium of index n; positive = waist ahead).</summary>
        public double DistanceToWaistX(double n) => -(n * Qx).Real;
        public double DistanceToWaistY(double n) => -(n * Qy).Real;

        /// <summary>The beam as a Gaussian beam model for an element of index n starting here (z from the element start).</summary>
        public GaussianBeam ToModel(double n)
        {
            if (!(n > 0)) n = 1;
            var qx = n * Qx; var qy = n * Qy;
            double zrx = Math.Max(qx.Imaginary, 1e-30), zry = Math.Max(qy.Imaginary, 1e-30);
            return new GaussianBeam
            {
                ZwX = -qx.Real, ZwY = -qy.Real, ZrX = zrx, ZrY = zry,
                W0X = Math.Sqrt(zrx * Lambda0 * M2x / (Math.PI * n)), W0Y = Math.Sqrt(zry * Lambda0 * M2y / (Math.PI * n))
            };
        }

        public string Describe() => string.Format(CultureInfo.InvariantCulture, "w = {0:G4} x {1:G4} mm, R = {2:G4} / {3:G4} m",
            RadiusX * 1e3, RadiusY * 1e3, CurvatureX, CurvatureY);

        /// <summary>Off-axis focal lengths of a spherical mirror of radius R at angle of incidence theta:
        /// tangential f_t = R cos θ / 2 (in the plane of incidence), sagittal f_s = R / (2 cos θ).</summary>
        public static (double Ft, double Fs) MirrorFocalLengths(double radiusM, double aoiRad)
        {
            double c = Math.Cos(aoiRad);
            return (radiusM * c / 2, radiusM / (2 * c));
        }

        /// <summary>True if the plane of incidence (fold plane) is closer to the x-z plane than to y-z: the s direction
        /// psi (angle from local x, 90 deg = folding in x-z) decides.</summary>
        public static bool TangentialIsX(double psiRad, out bool snapped)
        {
            double s = Math.Abs(Math.Sin(psiRad)), c = Math.Abs(Math.Cos(psiRad));
            snapped = Math.Min(s, c) > 1e-6;
            return s >= c;
        }
    }

    /// <summary>Observable spatio-temporal couplings of a tracked beam carrying a pulse.</summary>
    public static class Couplings
    {
        /// <summary>
        /// Pulse-front tilt dt/dx (s/m) of the intensity in one plane: the slope of the intensity-weighted mean arrival
        /// time across the beam. The field of the tracked beam is built from the pulse's spectrum A(ω) (the field on each
        /// frequency's chief ray) as a Gaussian beam per frequency, displaced by E Δω and tilted by F Δω:
        /// U(x, ω) = A(ω) exp(-i k0 (x - E Δω)^2 / 2 q̂) exp(i k0 F Δω (x - E Δω)). The mean arrival time at x is the
        /// power-weighted group delay, t(x) = ∫ |U|^2 ∂arg U/∂ω dω / ∫ |U|^2 dω (exact for the first moment, and free of
        /// the time window), on 96 points across the beam. It contains the tilt from angular dispersion (k0 F / n:
        /// Hebling's tan γ = λ dθ/dλ) and the tilt from spatial chirp with temporal chirp (Akturk et al., Opt. Express 13,
        /// 8642 (2005)).
        /// </summary>
        public static double PulseFrontTilt(BeamState b, PulseField p, bool x)
        {
            double E = x ? b.Ex : b.Ey, F = x ? b.Fx : b.Fy; var q = x ? b.Qx : b.Qy;
            if (E == 0 && F == 0) return 0;
            var g = p.Grid; int n = g.N; double k0 = b.K0, w0 = g.OmegaCenter;
            double w = x ? b.RadiusX : b.RadiusY;
            if (double.IsNaN(w)) return double.NaN;
            // spectral power and group delay of each polarisation (central differences of the phase, on the support)
            var comps = new[] { p.Ax, p.Ay };
            var pw = new double[2][]; var tau = new double[2][];
            double s0 = 0, s1 = 0, s2 = 0;
            for (int c = 0; c < 2; c++)
            {
                var a = comps[c]; pw[c] = new double[n]; tau[c] = new double[n];
                double mx = 0; for (int i = 0; i < n; i++) { pw[c][i] = a[i].Real * a[i].Real + a[i].Imaginary * a[i].Imaginary; mx = Math.Max(mx, pw[c][i]); }
                if (mx == 0) continue;
                for (int i = 0; i < n; i++)
                {
                    if (pw[c][i] < 1e-10 * mx) { pw[c][i] = 0; continue; }
                    // mean of the one-bin phase steps on either side (each below pi while the pulse fits the window)
                    double sum = 0; int cnt = 0;
                    if (i > 0 && a[i - 1] != Complex.Zero) { sum += (a[i] * Complex.Conjugate(a[i - 1])).Phase / (g.OmegaAbsolute[i] - g.OmegaAbsolute[i - 1]); cnt++; }
                    if (i < n - 1 && a[i + 1] != Complex.Zero) { sum += (a[i + 1] * Complex.Conjugate(a[i])).Phase / (g.OmegaAbsolute[i + 1] - g.OmegaAbsolute[i]); cnt++; }
                    if (cnt == 0) continue;
                    tau[c][i] = sum / cnt;
                    double d = g.OmegaAbsolute[i] - w0; s0 += pw[c][i]; s1 += pw[c][i] * d; s2 += pw[c][i] * d * d;
                }
            }
            if (!(s0 > 0)) return double.NaN;
            double sw = Math.Sqrt(Math.Max(0, s2 / s0 - Math.Pow(s1 / s0, 2)));
            double half = 2.5 * Math.Sqrt(w * w + Math.Pow(2 * E * sw, 2));
            const int NX = 96;
            var invq = 1 / q; double reQ = invq.Real, imQ = invq.Imaginary;
            double a0 = 0, ax = 0, axx = 0, at = 0, axt = 0;
            for (int k = 0; k < NX; k++)
            {
                double xx = -half + 2 * half * (k + 0.5) / NX, fl = 0, ft = 0;
                for (int c = 0; c < 2; c++)
                    for (int i = 0; i < n; i++)
                    {
                        if (pw[c][i] == 0) continue;
                        double dw = g.OmegaAbsolute[i] - w0, u = xx - E * dw;
                        double m2 = pw[c][i] * Math.Exp(k0 * u * u * imQ);                          // |U|^2 (Im 1/q̂ < 0)
                        double tf = -k0 * E * u * reQ + k0 * F * u - k0 * F * E * dw;                // d arg(beam factor) / d omega; curvature phase +k u^2 / 2R
                        fl += m2; ft += m2 * (tau[c][i] + tf);
                    }
                if (!(fl > 0)) continue;
                double tbar = ft / fl;
                a0 += fl; ax += fl * xx; axx += fl * xx * xx; at += fl * tbar; axt += fl * xx * tbar;
            }
            double den = a0 * axx - ax * ax;
            return den > 0 ? (a0 * axt - ax * at) / den : double.NaN;
        }

        /// <summary>The pulse with a cartesian line profile built from the tracked beam's Gaussian field with its couplings (see
        /// <see cref="PulseFrontTilt"/>), at every frequency bin of the pulse, for the spaceTime instrument.</summary>
        public static PulseField WithTrackedProfile(BeamState b, PulseField p, bool x)
        {
            double E = x ? b.Ex : b.Ey, F = x ? b.Fx : b.Fy, w = x ? b.RadiusX : b.RadiusY; var q = x ? b.Qx : b.Qy;
            var g = p.Grid; int n = g.N; double w0 = g.OmegaCenter;
            var sp = p.SpectralIntensity(); double s0 = 0, s2 = 0;
            for (int i = 0; i < n; i++) { double d = g.OmegaAbsolute[i] - w0; s0 += sp[i]; s2 += sp[i] * d * d; }
            double half = 4 * Math.Sqrt(w * w + 4 * E * E * s2 / Math.Max(1e-300, s0));
            const int NX = 128;
            var basis = new Transverse.CartesianBasis(NX, 1, half, half);
            var data = new Complex[NX * n]; var invq = 1 / q;
            for (int i = 0; i < n; i++)
            {
                double dw = g.OmegaAbsolute[i] - w0, sum = 0;
                for (int m = 0; m < NX; m++)
                {
                    double u = basis.X[m] - E * dw;
                    // E = Re{A exp(-i w t)}: curvature gives +k u^2 / 2R, the same as a carried profile
                    var v = Complex.Exp(new Complex(b.K0 * u * u * invq.Imaginary / 2, b.K0 * u * u * invq.Real / 2 + b.K0 * F * dw * u));
                    data[m * n + i] = v; sum += basis.Weights[m] * (v.Real * v.Real + v.Imaginary * v.Imaginary);
                }
                double sc = sum > 0 ? 1 / Math.Sqrt(sum) : 0;
                for (int m = 0; m < NX; m++) data[m * n + i] *= sc;
            }
            var r = p.WithSpectrum(p.Ax, p.Ay);
            r.Profile = new Transverse.TransverseProfile(basis, (double[])g.OmegaAbsolute.Clone(), data, "tracked beam");
            return r;
        }

        /// <summary>Record the couplings of a tracked beam (summary values of an element; docs/reference/components.md#spatio-temporal-couplings).</summary>
        public static void Report(BeamState b, PulseField p, System.Collections.Generic.IDictionary<string, double> values)
        {
            if (b == null || !b.HasCouplings) return;
            double w0 = p.Grid.OmegaCenter, lam = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / w0, dwdl = -w0 / lam, n = b.MediumIndex > 0 ? b.MediumIndex : 1;
            foreach (bool x in new[] { true, false })
            {
                double E = x ? b.Ex : b.Ey, F = x ? b.Fx : b.Fy; string a = x ? "X" : "Y";
                if (E == 0 && F == 0) continue;
                values["trackedAngularDispersion" + a + "RadPerM"] = F / n * dwdl;
                values["trackedSpatialChirp" + a + "MPerM"] = E * dwdl;
                double pft = PulseFrontTilt(b, p, x);
                values["trackedPulseFrontTilt" + a + "SPerM"] = pft;
                values["trackedPulseFrontTilt" + a + "Deg"] = Math.Atan(PhysicalConstants.SpeedOfLightMPerS / n * pft) * 180 / Math.PI;
            }
        }
    }
}
