using System;
using System.Numerics;

namespace UltrafastEngine.Numerics
{
    /// <summary>
    /// Small, deliberately unglamorous elementwise array helpers. Kept as plain
    /// static functions (not operator overloads) so every step of the RK4IP
    /// solver reads as an explicit sequence of named operations -- easier to
    /// audit against the algorithm's mathematical description than a wall of
    /// overloaded operators would be.
    /// </summary>
    public static class ArrayOps
    {
        public static Complex[] Add(Complex[] a, Complex[] b)
        {
            RequireSameLength(a, b);
            var result = new Complex[a.Length];
            for (int i = 0; i < a.Length; i++) result[i] = a[i] + b[i];
            return result;
        }

        public static Complex[] Scale(Complex[] a, double s)
        {
            var result = new Complex[a.Length];
            for (int i = 0; i < a.Length; i++) result[i] = a[i] * s;
            return result;
        }

        /// <summary>Elementwise (Hadamard) product -- used to apply a frequency-domain
        /// phasor (phase/transmission) onto a spectrum: result[i] = phasor[i] * field[i].</summary>
        public static Complex[] Multiply(Complex[] a, Complex[] b)
        {
            RequireSameLength(a, b);
            var result = new Complex[a.Length];
            for (int i = 0; i < a.Length; i++) result[i] = a[i] * b[i];
            return result;
        }

        public static double[] Abs2(Complex[] a)
        {
            var result = new double[a.Length];
            for (int i = 0; i < a.Length; i++)
                result[i] = a[i].Real * a[i].Real + a[i].Imaginary * a[i].Imaginary;
            return result;
        }

        private static void RequireSameLength(Complex[] a, Complex[] b)
        {
            if (a.Length != b.Length)
                throw new ArgumentException("Array length mismatch: " + a.Length + " vs " + b.Length +
                    ". This almost always means a PulseField was propagated against the wrong FrequencyGrid.");
        }
    }
}
