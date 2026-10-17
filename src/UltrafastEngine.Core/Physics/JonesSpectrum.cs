using System;
using System.Numerics;
using UltrafastEngine.Pulses;

namespace UltrafastEngine.Physics
{
    /// <summary>
    /// A uniform, ascending angular-frequency axis on which linear terms are evaluated: the envelope grid
    /// (FrequencyGrid.OmegaAbsolute) or the positive half of the carrier-resolved field grid. The carrier
    /// <see cref="Omega0"/> sits exactly on sample <see cref="CarrierIndex"/>.
    /// </summary>
    public sealed class OmegaAxis
    {
        public double[] Omega { get; }
        public double Omega0 { get; }
        public int CarrierIndex { get; }
        public double DOmega { get; }
        public int N => Omega.Length;

        public OmegaAxis(double[] omega, int carrierIndex)
        {
            Omega = omega; CarrierIndex = carrierIndex; Omega0 = omega[carrierIndex];
            DOmega = omega.Length > 1 ? omega[1] - omega[0] : 1.0;
        }

        public static OmegaAxis FromGrid(FrequencyGrid g) => new OmegaAxis(g.OmegaAbsolute, g.CarrierIndex);
    }

    /// <summary>
    /// A 2x2 Jones transfer function per frequency in the (transported) lab x/y basis:
    /// [Ax'; Ay'] = [[Xx, Xy], [Yx, Yy]] [Ax; Ay]. Stored compactly: Scalar (only Xx, applied to both
    /// polarisations), Diagonal (Xx, Yy), or Full. Amplitudes are in sqrt(power) units, so |entry|^2 is a power
    /// transmission.
    /// </summary>
    public sealed class JonesSpectrum
    {
        public int N { get; }
        public Complex[] Xx { get; private set; }
        public Complex[] Xy { get; private set; }
        public Complex[] Yx { get; private set; }
        public Complex[] Yy { get; private set; }

        public bool IsScalar => Yy == null && Xy == null;
        public bool IsDiagonal => Xy == null;

        private JonesSpectrum(int n) { N = n; }

        public static JonesSpectrum Identity(int n)
        {
            var j = new JonesSpectrum(n) { Xx = new Complex[n] };
            for (int i = 0; i < n; i++) j.Xx[i] = Complex.One;
            return j;
        }

        public static JonesSpectrum Scalar(Complex[] h) => new JonesSpectrum(h.Length) { Xx = h };
        public static JonesSpectrum Diagonal(Complex[] hx, Complex[] hy) => new JonesSpectrum(hx.Length) { Xx = hx, Yy = hy };
        public static JonesSpectrum Full(Complex[] xx, Complex[] xy, Complex[] yx, Complex[] yy) =>
            new JonesSpectrum(xx.Length) { Xx = xx, Xy = xy, Yx = yx, Yy = yy };

        public static JonesSpectrum FromPhaseAmplitude(double[] phase, double[] amp)
        {
            var h = new Complex[phase.Length];
            for (int i = 0; i < h.Length; i++) h[i] = Complex.FromPolarCoordinates(amp == null ? 1.0 : amp[i], phase[i]);
            return Scalar(h);
        }

        private Complex[] YyOrXx => Yy ?? Xx;

        /// <summary>Returns other * this (i.e. apply this first, then other).</summary>
        public JonesSpectrum Then(JonesSpectrum other)
        {
            if (other.N != N) throw new ArgumentException("Jones spectra of different lengths.");
            if (IsScalar && other.IsScalar)
            {
                var r = new Complex[N];
                for (int i = 0; i < N; i++) r[i] = Xx[i] * other.Xx[i];
                return Scalar(r);
            }
            if (IsDiagonal && other.IsDiagonal)
            {
                var rx = new Complex[N]; var ry = new Complex[N];
                var ay = YyOrXx; var by = other.YyOrXx;
                for (int i = 0; i < N; i++) { rx[i] = Xx[i] * other.Xx[i]; ry[i] = ay[i] * by[i]; }
                return Diagonal(rx, ry);
            }
            var xx = new Complex[N]; var xy = new Complex[N]; var yx = new Complex[N]; var yy = new Complex[N];
            for (int i = 0; i < N; i++)
            {
                Get(i, out var a11, out var a12, out var a21, out var a22);
                other.Get(i, out var b11, out var b12, out var b21, out var b22);
                xx[i] = b11 * a11 + b12 * a21; xy[i] = b11 * a12 + b12 * a22;
                yx[i] = b21 * a11 + b22 * a21; yy[i] = b21 * a12 + b22 * a22;
            }
            return Full(xx, xy, yx, yy);
        }

        public void Get(int i, out Complex xx, out Complex xy, out Complex yx, out Complex yy)
        {
            xx = Xx[i];
            yy = Yy != null ? Yy[i] : Xx[i];
            xy = Xy != null ? Xy[i] : Complex.Zero;
            yx = Yx != null ? Yx[i] : Complex.Zero;
        }

        /// <summary>Apply to spectral envelopes (returns new arrays).</summary>
        public void Apply(Complex[] ax, Complex[] ay, out Complex[] bx, out Complex[] by)
        {
            bx = new Complex[N]; by = new Complex[N];
            if (IsScalar) { for (int i = 0; i < N; i++) { bx[i] = Xx[i] * ax[i]; by[i] = Xx[i] * ay[i]; } return; }
            if (IsDiagonal) { var y = YyOrXx; for (int i = 0; i < N; i++) { bx[i] = Xx[i] * ax[i]; by[i] = y[i] * ay[i]; } return; }
            for (int i = 0; i < N; i++)
            {
                bx[i] = Xx[i] * ax[i] + Xy[i] * ay[i];
                by[i] = Yx[i] * ax[i] + Yy[i] * ay[i];
            }
        }

        /// <summary>Multiply every entry by a scalar spectrum (e.g. to remove a common phase).</summary>
        public void ScaleBy(Complex[] s)
        {
            for (int i = 0; i < N; i++) Xx[i] *= s[i];
            if (Yy != null) for (int i = 0; i < N; i++) Yy[i] *= s[i];
            if (Xy != null) for (int i = 0; i < N; i++) { Xy[i] *= s[i]; Yx[i] *= s[i]; }
        }

        /// <summary>Power transmission for unpolarised light, (|Xx|^2+|Xy|^2+|Yx|^2+|Yy|^2)/2, at sample i.</summary>
        public double MeanPowerTransmission(int i)
        {
            Get(i, out var a, out var b, out var c, out var d);
            return 0.5 * (a.Magnitude * a.Magnitude + b.Magnitude * b.Magnitude + c.Magnitude * c.Magnitude + d.Magnitude * d.Magnitude);
        }

        // ------------------------------------------------------------------ static helpers for 2x2 algebra
        /// <summary>Rotation matrix R(theta) = [[c, s], [-s, c]] (rotates the basis by +theta).</summary>
        public static void Rotation(double theta, out double c, out double s) { c = Math.Cos(theta); s = Math.Sin(theta); }

        /// <summary>J = R(-theta) diag(a, b) R(theta): an element with eigen-axes at angle theta to x.</summary>
        public static void RotatedDiagonal(double theta, Complex a, Complex b, out Complex xx, out Complex xy, out Complex yx, out Complex yy)
        {
            double c = Math.Cos(theta), s = Math.Sin(theta);
            xx = a * c * c + b * s * s;
            yy = a * s * s + b * c * c;
            xy = (a - b) * c * s;
            yx = xy;
        }
    }
}
