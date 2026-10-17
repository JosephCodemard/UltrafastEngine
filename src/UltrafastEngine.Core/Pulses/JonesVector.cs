using System;
using System.Numerics;

namespace UltrafastEngine.Pulses
{
    /// <summary>
    /// Lab-frame (x,y) Jones vector. Used to specify the input pulse's polarization
    /// when building a PulseField; NOT used as a per-frequency state inside PulseField
    /// itself, because polarization can evolve differently at different wavelengths
    /// once you propagate through waveplates/birefringent media/XPM-coupled nonlinear
    /// segments -- that's exactly why PulseField stores full Ax(omega), Ay(omega)
    /// spectra rather than a single scalar field + a single polarization label.
    /// </summary>
    public readonly struct JonesVector
    {
        public Complex Ex { get; }
        public Complex Ey { get; }

        public JonesVector(Complex ex, Complex ey)
        {
            Ex = ex;
            Ey = ey;
        }

        public static JonesVector LinearX => new JonesVector(Complex.One, Complex.Zero);
        public static JonesVector LinearY => new JonesVector(Complex.Zero, Complex.One);
        public static JonesVector Linear45 => new JonesVector(1.0 / Math.Sqrt(2.0), 1.0 / Math.Sqrt(2.0));

        /// <summary>
        /// Right-circular in the common optics-lab convention (Ey lagging Ex by 90 deg,
        /// i.e. -i on Ey, viewed looking toward the source). Sign conventions for circular
        /// polarization vary across textbooks/labs -- verify against your own convention
        /// before relying on the handedness label.
        /// </summary>
        public static JonesVector CircularRight => new JonesVector(
            1.0 / Math.Sqrt(2.0), new Complex(0.0, -1.0) / Math.Sqrt(2.0));

        public static JonesVector CircularLeft => new JonesVector(
            1.0 / Math.Sqrt(2.0), new Complex(0.0, 1.0) / Math.Sqrt(2.0));

        public JonesVector Normalized()
        {
            double norm = Math.Sqrt(Ex.Real * Ex.Real + Ex.Imaginary * Ex.Imaginary +
                                     Ey.Real * Ey.Real + Ey.Imaginary * Ey.Imaginary);
            if (norm < 1e-30) throw new InvalidOperationException("Cannot normalize a near-zero Jones vector.");
            return new JonesVector(Ex / norm, Ey / norm);
        }
    }
}
