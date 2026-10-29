using System;
using System.Numerics;

namespace UltrafastEngine.Plugins.Hitran
{
    /// <summary>
    /// The Faddeeva function w(z) = exp(-z^2) erfc(-i z) for Im z >= 0: the complex Voigt line shape.
    /// Weideman's rational approximation (SIAM J. Numer. Anal. 31, 1497 (1994)), N = 32 terms (relative error about
    /// 1e-12 in the upper half plane), and the asymptotic series for |z| > 100. Re w(x + i y) is the Voigt profile
    /// (up to the factor sqrt(pi) gamma_D), Im w its dispersion.
    /// </summary>
    public static class Faddeeva
    {
        private const int N = 32;
        private static readonly double L = Math.Sqrt(N / Math.Sqrt(2.0));
        private static readonly double[] A = Coefficients();   // A[n-1] = a_n, n = 1..N
        private static readonly double InvSqrtPi = 1.0 / Math.Sqrt(Math.PI);

        private static double[] Coefficients()
        {
            int M = N * 2, M2 = 2 * M;
            var f = new double[M2];   // f[j] for the fft-shifted sample order
            for (int j = 0; j < M2; j++)
            {
                if (j == M) { f[j] = 0; continue; }
                int k = j < M ? j : j - M2;
                double t = L * Math.Tan(k * Math.PI / (2.0 * M));
                f[j] = Math.Exp(-t * t) * (L * L + t * t);
            }
            var a = new double[N];
            for (int n = 1; n <= N; n++)
            {
                double s = 0;
                for (int j = 0; j < M2; j++) s += f[j] * Math.Cos(2.0 * Math.PI * j * n / M2);
                a[n - 1] = s / M2;
            }
            return a;
        }

        /// <summary>w(z), Im z >= 0 (a slightly negative imaginary part from round-off is clamped to 0).</summary>
        public static Complex W(Complex z)
        {
            double x = z.Real, y = Math.Max(0.0, z.Imaginary);
            z = new Complex(x, y);
            double r2 = x * x + y * y;
            if (r2 > 1e4)
            {
                var iz = Complex.One / z; var iz2 = iz * iz;
                return Complex.ImaginaryOne * InvSqrtPi * iz * (1.0 + iz2 * (0.5 + iz2 * (0.75 + iz2 * 1.875)));
            }
            var Lmiz = new Complex(L + y, -x);          // L - i z
            var Lpiz = new Complex(L - y, x);           // L + i z
            var Z = Lpiz / Lmiz;
            Complex p = Complex.Zero;                    // p(Z) = sum_{n=1}^{N} a_n Z^(n-1) by Horner's rule
            for (int n = N; n >= 1; n--) p = p * Z + A[n - 1];
            return 2.0 * p / (Lmiz * Lmiz) + InvSqrtPi / Lmiz;
        }
    }
}
