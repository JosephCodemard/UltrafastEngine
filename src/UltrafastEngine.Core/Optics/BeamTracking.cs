using System;
using System.Globalization;
using System.Numerics;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
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
    /// </summary>
    public sealed class BeamState
    {
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

        public BeamState Clone() => new BeamState(Qx, Qy, Lambda0, M2x, M2y, MediumIndex) { PathM = PathM };

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
        public void Propagate(double lengthM, double n)
        {
            if (!(n > 0)) n = 1;
            Qx += lengthM / n; Qy += lengthM / n; MediumIndex = n; PathM += lengthM;
        }

        /// <summary>Thin lens (focal lengths in x and y; infinity = no power in that plane).</summary>
        public void Lens(double fx, double fy)
        {
            if (!double.IsInfinity(fx) && fx != 0) Qx = Abcd(Qx, 1, 0, -1 / fx, 1);
            if (!double.IsInfinity(fy) && fy != 0) Qy = Abcd(Qy, 1, 0, -1 / fy, 1);
        }

        /// <summary>Lens power in a medium of index n (curved mirror in that medium): reduced C = -n/f.</summary>
        public void LensInMedium(double fx, double fy, double n)
        {
            if (!double.IsInfinity(fx) && fx != 0) Qx = Abcd(Qx, 1, 0, -n / fx, 1);
            if (!double.IsInfinity(fy) && fy != 0) Qy = Abcd(Qy, 1, 0, -n / fy, 1);
        }

        /// <summary>Refraction at a flat surface at angle of incidence theta1 from n1 into n2: the tangential axis is
        /// magnified (reduced ABCD diag(cos θ2 / cos θ1, cos θ1 / cos θ2)); the sagittal axis is unchanged.</summary>
        public void TiltedInterface(double n1, double n2, double theta1, bool tangentialIsX)
        {
            MediumIndex = n2;
            if (Math.Abs(theta1) < 1e-12) return;
            double s2 = n1 * Math.Sin(theta1) / n2;
            if (Math.Abs(s2) >= 1) return; // total internal reflection: not a refracting beam
            double c1 = Math.Cos(theta1), c2 = Math.Sqrt(1 - s2 * s2);
            double a = c2 / c1, d = c1 / c2;
            if (tangentialIsX) Qx = Abcd(Qx, a, 0, 0, d); else Qy = Abcd(Qy, a, 0, 0, d);
        }

        /// <summary>A general (reduced) ABCD matrix per axis.</summary>
        public void Apply(double ax, double bx, double cx, double dx, double ay, double by, double cy, double dy)
        {
            Qx = Abcd(Qx, ax, bx, cx, dx); Qy = Abcd(Qy, ay, by, cy, dy);
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
}
