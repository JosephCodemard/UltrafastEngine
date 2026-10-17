using System;

namespace UltrafastEngine.Numerics
{
    /// <summary>
    /// Standard phase-unwrapping (equivalent to numpy.unwrap / MATLAB's unwrap):
    /// walks the array and adds/subtracts 2*pi whenever the step-to-step jump
    /// exceeds pi, so the result is a continuous phase curve suitable for
    /// numerical differentiation (GD/GDD/TOD).
    ///
    /// IMPORTANT: the input array must already be sorted in ascending order of
    /// the independent variable (angular frequency) -- unwrapping an unsorted
    /// array produces meaningless results. Callers (e.g. any OPD-derived phase) are
    /// responsible for sorting first.
    /// </summary>
    public static class PhaseUnwrap
    {
        public static double[] Unwrap(double[] phase)
        {
            if (phase.Length == 0) return Array.Empty<double>();

            var result = new double[phase.Length];
            result[0] = phase[0];
            double cumulativeOffset = 0.0;

            for (int i = 1; i < phase.Length; i++)
            {
                double delta = phase[i] - phase[i - 1];
                if (delta > Math.PI) cumulativeOffset -= 2.0 * Math.PI;
                else if (delta < -Math.PI) cumulativeOffset += 2.0 * Math.PI;
                result[i] = phase[i] + cumulativeOffset;
            }
            return result;
        }
    }
}
