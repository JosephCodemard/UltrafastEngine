using System;
using UltrafastEngine.Pulses;

namespace UltrafastEngine.Pulses
{
    /// <summary>
    /// Extracts group delay (GD = dphi/domega), group delay dispersion
    /// (GDD = d2phi/domega2) and third-order dispersion (TOD = d3phi/domega3) from an
    /// unwrapped spectral phase curve on a UNIFORM omega grid (guaranteed by
    /// FrequencyGrid).
    ///
    /// Real (measured or Zemax-derived) phase data is noisy, and naive
    /// finite-differencing amplifies that noise badly for higher derivatives --
    /// a raw 2-point difference on noisy GDD data can make TOD meaningless. This
    /// class applies an optional centered moving-average smoothing pass to the
    /// phase before differentiating, and uses 4th-order-accurate central
    /// finite-difference stencils (rather than naive 2-point differences) for
    /// better accuracy on well-sampled data. This is a reasonable default, not a
    /// substitute for validating against a known analytic case (see the test
    /// harness) if you need to trust TOD values quantitatively.
    /// </summary>
    public static class DispersionMath
    {
        public static DispersionCurve FromUnwrappedPhase(double[] omega, double[] phaseUnwrapped, int carrierIndex, int smoothingHalfWidth = 2)
        {
            var phase = smoothingHalfWidth > 0 ? MovingAverage(phaseUnwrapped, smoothingHalfWidth) : phaseUnwrapped;
            double h = omega[1] - omega[0]; // uniform spacing, guaranteed by FrequencyGrid

            int n = omega.Length;
            var gd = new double[n];
            var gdd = new double[n];
            var tod = new double[n];
            for (int i = 0; i < n; i++)
            {
                gd[i] = FirstDerivative(phase, i, h);
                gdd[i] = SecondDerivative(phase, i, h);
                tod[i] = ThirdDerivative(phase, i, h);
            }

            return new DispersionCurve
            {
                Omega = omega,
                GroupDelayS = gd,
                GddS2 = gdd,
                TodS3 = tod,
                GdAtCarrierS = gd[carrierIndex],
                GddAtCarrierS2 = gdd[carrierIndex],
                TodAtCarrierS3 = tod[carrierIndex]
            };
        }

        /// <summary>Same result as FromUnwrappedPhase(...).*AtCarrier, computed only at
        /// one index (smoothing applied locally over the 9-point neighbourhood the
        /// stencils need).</summary>
        public static DispersionCurve AtIndex(double[] omega, double[] phaseUnwrapped, int index, int smoothingHalfWidth = 2)
        {
            int n = omega.Length;
            int lo = Math.Max(0, index - 2 - smoothingHalfWidth - 2), hi = Math.Min(n - 1, index + 2 + smoothingHalfWidth + 2);
            var local = new double[hi - lo + 1];
            for (int i = lo; i <= hi; i++)
            {
                if (smoothingHalfWidth <= 0) { local[i - lo] = phaseUnwrapped[i]; continue; }
                int a = Math.Max(0, i - smoothingHalfWidth), b = Math.Min(n - 1, i + smoothingHalfWidth);
                double sum = 0.0;
                for (int k = a; k <= b; k++) sum += phaseUnwrapped[k];
                local[i - lo] = sum / (b - a + 1);
            }
            double h = omega[1] - omega[0];
            int j = index - lo;
            return new DispersionCurve
            {
                GdAtCarrierS = FirstDerivative(local, j, h),
                GddAtCarrierS2 = SecondDerivative(local, j, h),
                TodAtCarrierS3 = ThirdDerivative(local, j, h)
            };
        }

        // 4th-order-accurate central difference where 2 points exist on each side,
        // falling back to a 2nd-order 3-point stencil, then to a 1st-order one-sided
        // difference right at the edges. Edge accuracy is intentionally lower -- the
        // edges of a spectral window are usually low-intensity / not the physically
        // interesting region anyway.
        private static double FirstDerivative(double[] f, int i, double h)
        {
            int n = f.Length;
            if (i >= 2 && i <= n - 3) return (f[i - 2] - 8.0 * f[i - 1] + 8.0 * f[i + 1] - f[i + 2]) / (12.0 * h);
            if (i >= 1 && i <= n - 2) return (f[i + 1] - f[i - 1]) / (2.0 * h);
            if (i == 0) return (f[1] - f[0]) / h;
            return (f[n - 1] - f[n - 2]) / h;
        }

        private static double SecondDerivative(double[] f, int i, double h)
        {
            int n = f.Length;
            if (i >= 2 && i <= n - 3)
                return (-f[i - 2] + 16.0 * f[i - 1] - 30.0 * f[i] + 16.0 * f[i + 1] - f[i + 2]) / (12.0 * h * h);
            if (i >= 1 && i <= n - 2) return (f[i - 1] - 2.0 * f[i] + f[i + 1]) / (h * h);
            return 0.0; // not enough points at the very edge for a meaningful 2nd derivative
        }

        private static double ThirdDerivative(double[] f, int i, double h)
        {
            int n = f.Length;
            if (i >= 2 && i <= n - 3)
                return (-0.5 * f[i - 2] + f[i - 1] - f[i + 1] + 0.5 * f[i + 2]) / (h * h * h);
            return 0.0; // not enough points near the edge for a meaningful 3rd derivative
        }

        private static double[] MovingAverage(double[] x, int halfWidth)
        {
            int n = x.Length;
            var result = new double[n];
            for (int i = 0; i < n; i++)
            {
                int lo = i - halfWidth < 0 ? 0 : i - halfWidth;
                int hi = i + halfWidth >= n ? n - 1 : i + halfWidth;
                double sum = 0.0;
                for (int k = lo; k <= hi; k++) sum += x[k];
                result[i] = sum / (hi - lo + 1);
            }
            return result;
        }
    }
}
