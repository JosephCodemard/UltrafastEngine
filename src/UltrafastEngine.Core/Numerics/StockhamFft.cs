using System;
using System.Numerics;
using System.Threading.Tasks;

namespace UltrafastEngine.Numerics
{
    /// <summary>
    /// Managed power-of-two FFT: radix-4 Stockham autosort (a final radix-2 stage when log2 N is odd). Compared with the
    /// earlier in-place radix-2 Cooley-Tukey it needs no bit-reversal pass, halves the number of passes over memory and
    /// reads its twiddles from one exact table (exp(-2 pi i k / N), k &lt; N), so rounding does not grow with k.
    /// Same conventions as <see cref="ComplexFft.TransformInPlace"/>: exponent sign -1 or +1, optional 1/N.
    /// Large transforms (N &gt;= 2^16) split each pass across threads; smaller ones run serially, because thread
    /// dispatch then costs more than it saves (it made an 8192-point transform slower than a serial one).
    /// </summary>
    public static class StockhamFft
    {
        /// <summary>Transforms at or above this length use several threads per pass.</summary>
        public const int ParallelThreshold = 1 << 16;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, double[]> Tables =
            new System.Collections.Concurrent.ConcurrentDictionary<int, double[]>();

        [ThreadStatic] private static Complex[] _scratch;

        /// <summary>cos and sin of -2 pi k / n for k &lt; n, interleaved (computed directly, not by recurrence).</summary>
        private static double[] Table(int n) => Tables.GetOrAdd(n, len =>
        {
            var t = new double[2 * len];
            for (int k = 0; k < len; k++)
            {
                double ang = -2.0 * Math.PI * k / len;
                t[2 * k] = Math.Cos(ang); t[2 * k + 1] = Math.Sin(ang);
            }
            return t;
        });

        /// <summary>In-place transform of a power-of-two length array.</summary>
        public static void Transform(Complex[] a, int exponentSign, bool normalize)
        {
            int n = a.Length;
            if (n <= 1) return;
            if ((n & (n - 1)) != 0) throw new ArgumentException("StockhamFft needs a power-of-two length");
            var tw = Table(n);
            double sg = exponentSign > 0 ? -1.0 : 1.0;   // conjugate the twiddles for the + exponent
            var y = _scratch;
            if (y == null || y.Length < n) _scratch = y = new Complex[Math.Max(n, 16)];
            Complex[] src = a, dst = y;
            bool parallel = n >= ParallelThreshold && ParallelismSettings.MaxDegreeOfParallelism > 1;
            int len = n, s = 1;
            while (len >= 4)
            {
                Radix4Pass(src, dst, len, s, n, tw, sg, parallel);
                var t = src; src = dst; dst = t;
                len >>= 2; s <<= 2;
            }
            if (len == 2)
            {
                for (int q = 0; q < s; q++)
                {
                    var p0 = src[q]; var p1 = src[q + s];
                    dst[q] = new Complex(p0.Real + p1.Real, p0.Imaginary + p1.Imaginary);
                    dst[q + s] = new Complex(p0.Real - p1.Real, p0.Imaginary - p1.Imaginary);
                }
                var t = src; src = dst; dst = t;
            }
            if (!ReferenceEquals(src, a)) Array.Copy(src, a, n);
            if (normalize)
            {
                double inv = 1.0 / n;
                for (int i = 0; i < n; i++) a[i] = new Complex(a[i].Real * inv, a[i].Imaginary * inv);
            }
        }

        /// <summary>One radix-4 Stockham pass: sub-transforms of length <paramref name="len"/>, stride <paramref name="s"/>.</summary>
        private static void Radix4Pass(Complex[] x, Complex[] y, int len, int s, int n, double[] tw, double sg, bool parallel)
        {
            int m = len / 4, step = n / len;   // twiddle W_len^p = table[p * step]
            if (parallel && m >= 8)
            {
                int chunks = Math.Min(ParallelismSettings.MaxDegreeOfParallelism, m);
                Parallel.For(0, chunks, new ParallelOptions { MaxDegreeOfParallelism = chunks }, c =>
                {
                    int p0 = (int)((long)m * c / chunks), p1 = (int)((long)m * (c + 1) / chunks);
                    Block(x, y, p0, p1, m, s, step, n, tw, sg);
                });
            }
            else if (parallel && s >= 1024)
            {
                int chunks = Math.Min(ParallelismSettings.MaxDegreeOfParallelism, s / 256);
                Parallel.For(0, chunks, new ParallelOptions { MaxDegreeOfParallelism = chunks }, c =>
                {
                    int q0 = (int)((long)s * c / chunks), q1 = (int)((long)s * (c + 1) / chunks);
                    BlockQ(x, y, m, s, q0, q1, step, n, tw, sg);
                });
            }
            else Block(x, y, 0, m, m, s, step, n, tw, sg);
        }

        private static void Block(Complex[] x, Complex[] y, int p0, int p1, int m, int s, int step, int n, double[] tw, double sg)
        {
            for (int p = p0; p < p1; p++) Butterflies(x, y, p, m, s, 0, s, step, n, tw, sg);
        }

        private static void BlockQ(Complex[] x, Complex[] y, int m, int s, int q0, int q1, int step, int n, double[] tw, double sg)
        {
            for (int p = 0; p < m; p++) Butterflies(x, y, p, m, s, q0, q1, step, n, tw, sg);
        }

        private static void Butterflies(Complex[] x, Complex[] y, int p, int m, int s, int q0, int q1, int step, int n, double[] tw, double sg)
        {
            int k1 = p * step, k2 = 2 * k1, k3 = 3 * k1;   // all < n
            double w1r = tw[2 * k1], w1i = sg * tw[2 * k1 + 1];
            double w2r = tw[2 * k2], w2i = sg * tw[2 * k2 + 1];
            double w3r = tw[2 * k3], w3i = sg * tw[2 * k3 + 1];
            int i0 = s * p, i1 = s * (p + m), i2 = s * (p + 2 * m), i3 = s * (p + 3 * m);
            int o0 = s * 4 * p, o1 = o0 + s, o2 = o0 + 2 * s, o3 = o0 + 3 * s;
            for (int q = q0; q < q1; q++)
            {
                Complex a = x[q + i0], b = x[q + i1], c = x[q + i2], d = x[q + i3];
                double apcR = a.Real + c.Real, apcI = a.Imaginary + c.Imaginary;
                double amcR = a.Real - c.Real, amcI = a.Imaginary - c.Imaginary;
                double bpdR = b.Real + d.Real, bpdI = b.Imaginary + d.Imaginary;
                // j (b - d) with j = -i sg' : for the -i exponent (sg = +1) the "j" factor is -i, for +i it is +i
                double bmdR = b.Real - d.Real, bmdI = b.Imaginary - d.Imaginary;
                double jR = sg * bmdI, jI = -sg * bmdR;          // (-i sg) * (bmdR + i bmdI)
                y[q + o0] = new Complex(apcR + bpdR, apcI + bpdI);
                double t1R = amcR + jR, t1I = amcI + jI;          // a - c - i sg (b - d)
                y[q + o1] = new Complex(w1r * t1R - w1i * t1I, w1r * t1I + w1i * t1R);
                double t2R = apcR - bpdR, t2I = apcI - bpdI;
                y[q + o2] = new Complex(w2r * t2R - w2i * t2I, w2r * t2I + w2i * t2R);
                double t3R = amcR - jR, t3I = amcI - jI;
                y[q + o3] = new Complex(w3r * t3R - w3i * t3I, w3r * t3I + w3i * t3R);
            }
        }
    }
}
