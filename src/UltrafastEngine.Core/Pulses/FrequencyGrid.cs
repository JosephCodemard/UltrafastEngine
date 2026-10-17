using System;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Pulses
{
    /// <summary>
    /// The single shared time/frequency coordinate system for one propagation run.
    /// Every PulseField and every PathElement in that run must reference the SAME
    /// FrequencyGrid instance -- pass it explicitly everywhere rather than letting
    /// it be inferred, so a mismatched grid throws immediately (see ArrayOps) instead
    /// of silently producing wrong numbers.
    ///
    /// Convention (matches the widely-used "centered time / centered offset frequency"
    /// convention from the nonlinear-fibre-optics literature):
    ///   - Time is centered on t=0, ascending, N points, spacing DTime.
    ///   - OmegaOffset (capital Omega, "Omega = omega - omega0") is centered on 0,
    ///     ascending, and is the FFT-conjugate axis of Time.
    ///   - OmegaAbsolute = OmegaCenter + OmegaOffset -- same index order as OmegaOffset,
    ///     just shifted by the carrier. This is what you interpolate physical
    ///     (Zemax-derived) dispersion data onto.
    ///   - CarrierIndex is always exactly N/2 by construction (OmegaOffset[N/2] == 0).
    /// </summary>
    public sealed class FrequencyGrid
    {
        public int N { get; }
        public double DTime { get; }
        public double[] Time { get; }
        public double OmegaCenter { get; }
        public double[] OmegaOffset { get; }
        public double[] OmegaAbsolute { get; }
        public double DOmega { get; }
        public int CarrierIndex => N / 2;

        private FrequencyGrid(int n, double dTime, double[] time, double omegaCenter,
                               double[] omegaOffset, double[] omegaAbsolute, double dOmega)
        {
            N = n;
            DTime = dTime;
            Time = time;
            OmegaCenter = omegaCenter;
            OmegaOffset = omegaOffset;
            OmegaAbsolute = omegaAbsolute;
            DOmega = dOmega;
        }

        /// <summary>
        /// Build a grid from an explicit sample count, time step, and carrier
        /// angular frequency. N must be a power of two (required by ComplexFft).
        /// </summary>
        public static FrequencyGrid Create(int n, double dTimeSeconds, double omegaCenterRadPerS)
        {
            if (n <= 0 || (n & (n - 1)) != 0)
                throw new ArgumentException("FrequencyGrid requires N to be a power of two (got " + n + ").");
            if (dTimeSeconds <= 0)
                throw new ArgumentException("dTimeSeconds must be positive.");

            var time = new double[n];
            for (int i = 0; i < n; i++) time[i] = (i - n / 2) * dTimeSeconds;

            double dOmega = 2.0 * Math.PI / (n * dTimeSeconds);
            var omegaOffset = new double[n];
            var omegaAbsolute = new double[n];
            for (int i = 0; i < n; i++)
            {
                omegaOffset[i] = (i - n / 2) * dOmega;
                omegaAbsolute[i] = omegaCenterRadPerS + omegaOffset[i];
            }

            return new FrequencyGrid(n, dTimeSeconds, time, omegaCenterRadPerS, omegaOffset, omegaAbsolute, dOmega);
        }

        /// <summary>
        /// Convenience constructor: pick a centre wavelength and a total simulation
        /// time window (should comfortably exceed the pulse duration AND, if any
        /// nonlinear segment uses a delayed Raman response, the Raman decay time --
        /// see Convolution's circular-wraparound caveat).
        /// </summary>
        public static FrequencyGrid FromWavelengthWindow(double lambdaCenterM, double totalTimeWindowS, int n)
        {
            double omega0 = 2.0 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / lambdaCenterM;
            double dt = totalTimeWindowS / n;
            return Create(n, dt, omega0);
        }

        /// <summary>
        /// Wavelength axis for plotting only (never used inside the physics engine).
        /// Returns NaN for grid points at or below zero absolute frequency, which can
        /// occur at the edges of a very wide grid -- those points are not physical
        /// wavelengths and should not be plotted.
        /// </summary>
        public double[] WavelengthAxisM()
        {
            var result = new double[N];
            for (int i = 0; i < N; i++)
                result[i] = OmegaAbsolute[i] > 0
                    ? 2.0 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / OmegaAbsolute[i]
                    : double.NaN;
            return result;
        }
    }
}
