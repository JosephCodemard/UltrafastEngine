using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;

namespace UltrafastEngine.Numerics
{
    /// <summary>
    /// FFT of any length: recursive mixed-radix Cooley-Tukey for lengths whose prime factors are all ≤ 13
    /// (radix-2/3/4/5/7/... butterflies by direct small DFTs), and Bluestein's chirp-z algorithm (a power-of-two
    /// convolution) for lengths with a larger prime factor. Same conventions as <see cref="ComplexFft"/>
    /// (unnormalised forward, exponent sign chosen by the caller, optional 1/N).
    /// </summary>
    public static class MixedRadixFft
    {
        private const int MaxSmallPrime = 13;

        /// <summary>Prime factors of n (ascending, with multiplicity); 4s are merged from pairs of 2s for speed.</summary>
        public static List<int> Factor(int n)
        {
            var f = new List<int>();
            int m = n;
            while (m % 4 == 0) { f.Add(4); m /= 4; }
            for (int p = 2; p * p <= m; p++) while (m % p == 0) { f.Add(p); m /= p; }
            if (m > 1) f.Add(m);
            return f;
        }

        public static bool IsSmooth(int n) { foreach (var p in Factor(n)) if (p > MaxSmallPrime && p != 4) return false; return true; }

        private static readonly ConcurrentDictionary<long, Complex[]> Roots = new ConcurrentDictionary<long, Complex[]>();

        /// <summary>w[j] = exp(sign 2πi j / n), j = 0..n-1.</summary>
        private static Complex[] RootsOf(int n, int sign) => Roots.GetOrAdd(((long)n << 1) | (sign > 0 ? 1L : 0L), _ =>
        {
            var w = new Complex[n];
            for (int j = 0; j < n; j++) { double a = sign * 2 * Math.PI * j / n; w[j] = new Complex(Math.Cos(a), Math.Sin(a)); }
            return w;
        });

        public static void Transform(Complex[] a, int sign, bool normalize)
        {
            int n = a.Length;
            if (n <= 1) return;
            if ((n & (n - 1)) == 0) { ComplexFft.TransformInPlace(a, sign, normalize); return; }
            if (IsSmooth(n))
            {
                var factors = Factor(n).ToArray();
                var outp = new Complex[n];
                Recurse(a, 0, 1, n, outp, 0, factors, 0, RootsOf(n, sign), n);
                Array.Copy(outp, a, n);
            }
            else Bluestein(a, sign);
            if (normalize) { double inv = 1.0 / n; for (int i = 0; i < n; i++) a[i] *= inv; }
        }

        // decimation in time: X[k + q m] = sum_r W_n^{r (k + q m)} Y_r[k], Y_r = DFT of x[r :: p]
        private static void Recurse(Complex[] x, int off, int stride, int n, Complex[] outp, int outOff, int[] f, int fi, Complex[] w, int N)
        {
            if (n == 1) { outp[outOff] = x[off]; return; }
            int p = f[fi], m = n / p;
            for (int r = 0; r < p; r++) Recurse(x, off + r * stride, stride * p, m, outp, outOff + r * m, f, fi + 1, w, N);
            int step = N / n;                       // W_n^j = W_N^{j step}
            var t = new Complex[p]; var y = new Complex[p];
            for (int k = 0; k < m; k++)
            {
                for (int r = 0; r < p; r++) t[r] = outp[outOff + r * m + k] * w[(int)((long)r * k * step % N)];
                for (int q = 0; q < p; q++)
                {
                    Complex s = Complex.Zero;
                    for (int r = 0; r < p; r++) s += t[r] * w[(int)((long)r * q * m * step % N)];
                    y[q] = s;
                }
                for (int q = 0; q < p; q++) outp[outOff + k + q * m] = y[q];
            }
        }

        /// <summary>Bluestein: X_k = c_k sum_j (x_j c_j) conj(c_{k-j}), c_j = exp(sign π i j² / n), as a power-of-two convolution.</summary>
        private static void Bluestein(Complex[] a, int sign)
        {
            int n = a.Length, m = 1; while (m < 2 * n - 1) m <<= 1;
            var c = new Complex[n];
            long twoN = 2L * n;
            for (int j = 0; j < n; j++) { double ang = sign * Math.PI * ((long)j * j % twoN) / n; c[j] = new Complex(Math.Cos(ang), Math.Sin(ang)); }
            var u = new Complex[m]; var v = new Complex[m];
            for (int j = 0; j < n; j++) u[j] = a[j] * c[j];
            v[0] = Complex.Conjugate(c[0]);
            for (int j = 1; j < n; j++) { v[j] = Complex.Conjugate(c[j]); v[m - j] = v[j]; }
            ComplexFft.TransformInPlace(u, -1, false); ComplexFft.TransformInPlace(v, -1, false);
            for (int i = 0; i < m; i++) u[i] *= v[i];
            ComplexFft.TransformInPlace(u, +1, true);
            for (int k = 0; k < n; k++) a[k] = u[k] * c[k];
        }

        /// <summary>Reference O(n²) DFT (tests).</summary>
        public static Complex[] Naive(Complex[] x, int sign)
        {
            int n = x.Length; var r = new Complex[n];
            for (int k = 0; k < n; k++) { Complex s = 0; for (int j = 0; j < n; j++) { double a = sign * 2 * Math.PI * ((long)j * k % n) / n; s += x[j] * new Complex(Math.Cos(a), Math.Sin(a)); } r[k] = s; }
            return r;
        }
    }
}
