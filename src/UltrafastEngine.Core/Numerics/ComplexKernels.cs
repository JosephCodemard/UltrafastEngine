using System;
using System.Numerics;
#if NET5_0_OR_GREATER
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
#endif

namespace UltrafastEngine.Numerics
{
    /// <summary>
    /// Element-wise complex kernels used in every propagation step (operator products, axpy, exponentials). On .NET 10
    /// they run on 128-bit SIMD vectors (SSE2 on x64, NEON on ARM64 such as Apple silicon) through the portable
    /// System.Runtime.Intrinsics API; on .NET Framework 4.8 a scalar path runs. Results are identical to the scalar
    /// code up to floating-point reassociation (tests compare both).
    /// </summary>
    public static class ComplexKernels
    {
        /// <summary>True when the SIMD path is active.</summary>
        public static bool Accelerated
        {
            get
            {
#if NET5_0_OR_GREATER
                return Vector128.IsHardwareAccelerated && !ForceScalar;
#else
                return false;
#endif
            }
        }

        /// <summary>Switch the SIMD path off (benchmarks, debugging).</summary>
        public static bool ForceScalar { get; set; }

        /// <summary>dst[i] = a[i] * b[i] (dst may alias a or b).</summary>
        public static void Multiply(Complex[] a, Complex[] b, Complex[] dst)
        {
            int n = a.Length;
#if NET5_0_OR_GREATER
            if (Accelerated)
            {
                var da = MemoryMarshal.Cast<Complex, double>(a.AsSpan());
                var db = MemoryMarshal.Cast<Complex, double>(b.AsSpan());
                var dd = MemoryMarshal.Cast<Complex, double>(dst.AsSpan());
                var sign = Vector128.Create(-1.0, 1.0);
                var swap = Vector128.Create(1L, 0L);
                for (int i = 0; i < n; i++)
                {
                    var va = Vector128.Create((ReadOnlySpan<double>)da.Slice(2 * i, 2));
                    var br = Vector128.Create(db[2 * i]); var bi = Vector128.Create(db[2 * i + 1]);
                    var sw = Vector128.Shuffle(va, swap);                    // [ai, ar]
                    var r = va * br + sw * bi * sign;                         // [ar br - ai bi, ai br + ar bi]
                    r.CopyTo(dd.Slice(2 * i, 2));
                }
                return;
            }
#endif
            for (int i = 0; i < n; i++)
            {
                double ar = a[i].Real, ai = a[i].Imaginary, br = b[i].Real, bi = b[i].Imaginary;
                dst[i] = new Complex(ar * br - ai * bi, ar * bi + ai * br);
            }
        }

        /// <summary>dst[i] = y[i] + s * x[i].</summary>
        public static void Axpy(Complex[] y, double s, Complex[] x, Complex[] dst)
        {
            int n = y.Length;
#if NET5_0_OR_GREATER
            if (Accelerated)
            {
                var dy = MemoryMarshal.Cast<Complex, double>(y.AsSpan());
                var dx = MemoryMarshal.Cast<Complex, double>(x.AsSpan());
                var dd = MemoryMarshal.Cast<Complex, double>(dst.AsSpan());
                var vs = Vector128.Create(s);
                int m = 2 * n, i = 0;
                for (; i + 2 <= m; i += 2)
                {
                    var r = Vector128.Create((ReadOnlySpan<double>)dy.Slice(i, 2)) + vs * Vector128.Create((ReadOnlySpan<double>)dx.Slice(i, 2));
                    r.CopyTo(dd.Slice(i, 2));
                }
                return;
            }
#endif
            for (int i = 0; i < n; i++) dst[i] = new Complex(y[i].Real + s * x[i].Real, y[i].Imaginary + s * x[i].Imaginary);
        }

        /// <summary>r[i] = exp(L[i] h) (underflow to 0 below e^-700).</summary>
        public static void Exp(Complex[] L, double h, Complex[] r)
        {
            for (int i = 0; i < L.Length; i++)
            {
                double re = L[i].Real * h, im = L[i].Imaginary * h;
                double m = re < -700 ? 0.0 : Math.Exp(re);
                r[i] = new Complex(m * Math.Cos(im), m * Math.Sin(im));
            }
        }

        /// <summary>Sum of |a[i]|^2.</summary>
        public static double Norm2(Complex[] a)
        {
            double s = 0;
            for (int i = 0; i < a.Length; i++) s += a[i].Real * a[i].Real + a[i].Imaginary * a[i].Imaginary;
            return s;
        }
    }
}
