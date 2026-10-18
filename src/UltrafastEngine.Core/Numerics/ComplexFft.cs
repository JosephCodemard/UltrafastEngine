using System;
using System.Numerics;
using System.Threading.Tasks;

namespace UltrafastEngine.Numerics
{
    /// <summary>
    /// In-place iterative radix-2 Cooley-Tukey FFT for power-of-two lengths (the time grids); other lengths go to
    /// <see cref="MixedRadixFft"/> (mixed radix for smooth lengths, Bluestein otherwise).
    ///
    /// Normalization convention (matches numpy.fft / most DSP textbooks):
    ///   Forward: unnormalized sum,  X[k] = sum_n x[n] * exp(-2*pi*i*k*n/N)
    ///   Inverse: normalized,        x[n] = (1/N) * sum_k X[k] * exp(+2*pi*i*k*n/N)
    /// This pairing satisfies the discrete Parseval relation:
    ///   sum_n |x[n]|^2 = (1/N) * sum_k |X[k]|^2
    /// which EnergyNormalization relies on -- if you ever change this convention,
    /// update EnergyNormalization.ComputeEnergyJFromFreqDomain to match.
    /// </summary>
    public static class ComplexFft
    {
        public static Complex[] Forward(Complex[] input)
        {
            var data = (Complex[])input.Clone();
            TransformInPlace(data, exponentSign: -1, normalize: false);
            return data;
        }

        public static Complex[] Inverse(Complex[] input)
        {
            var data = (Complex[])input.Clone();
            TransformInPlace(data, exponentSign: +1, normalize: true);
            return data;
        }

        // Twiddle tables, cached per transform length: table[k] = exp(-2*pi*i*k/n),
        // k = 0..n/2-1. Every butterfly pass of length `len` uses table[k * (n/len)]
        // (conjugated for the +i exponent). This replaces the original running product
        // `w *= wLen`, whose rounding error grows linearly with k -- negligible at
        // N = 8k, but ~1e-10 relative by N = 4M, which is the grid size a nanosecond-
        // scale stretcher run needs. A table is also faster (no complex multiply per k).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Complex[]> TwiddleCache =
            new System.Collections.Concurrent.ConcurrentDictionary<int, Complex[]>();

        private static Complex[] GetTwiddles(int n) => TwiddleCache.GetOrAdd(n, len =>
        {
            var t = new Complex[Math.Max(1, len / 2)];
            for (int k = 0; k < t.Length; k++)
            {
                double ang = -2.0 * Math.PI * k / len;
                t[k] = new Complex(Math.Cos(ang), Math.Sin(ang));
            }
            return t;
        });

        /// <summary>In-place radix-2 transform with an explicit exponent sign
        /// (-1: sum x e^{-i...}, +1: sum x e^{+i...}). normalize divides by N.
        /// Public so SpectralTransform can pick the physics sign convention without an
        /// extra clone/scale pass.</summary>
        public static void TransformInPlace(Complex[] a, int exponentSign, bool normalize)
        {
            int n = a.Length;
            if (n == 0) return;
            if ((n & (n - 1)) != 0) { MixedRadixFft.Transform(a, exponentSign, normalize); return; }   // any length: mixed radix / Bluestein

            // Bit-reversal permutation
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    var tmp = a[i];
                    a[i] = a[j];
                    a[j] = tmp;
                }
            }

            // Iterative Cooley-Tukey butterfly passes. Passes themselves MUST run in
            // order (each depends on the previous pass's output), but within a single
            // pass, every block (each starting index i, stepping by `len`) touches only
            // its own disjoint slice a[i .. i+len) -- no reads or writes cross block
            // boundaries, so the inner loop is safe to parallelize with zero
            // synchronization.
            bool parallel = n >= ParallelismSettings.MinLengthToParallelize && ParallelismSettings.MaxDegreeOfParallelism > 1;
            var twiddles = GetTwiddles(n);
            bool conjugate = exponentSign > 0;
            if (!parallel)
            {
                // serial path (small transforms, called from many threads at once): no delegates, no allocation
                for (int len = 2; len <= n; len <<= 1)
                {
                    int half = len / 2, stride = n / len;
                    for (int i = 0; i < n; i += len)
                        for (int k = 0; k < half; k++)
                        {
                            var tw = twiddles[k * stride];
                            double wr = tw.Real, wi = conjugate ? -tw.Imaginary : tw.Imaginary;
                            var u = a[i + k]; var x = a[i + k + half];
                            double vr = x.Real * wr - x.Imaginary * wi, vi = x.Real * wi + x.Imaginary * wr;
                            a[i + k] = new Complex(u.Real + vr, u.Imaginary + vi);
                            a[i + k + half] = new Complex(u.Real - vr, u.Imaginary - vi);
                        }
                }
                if (normalize) { double inv1 = 1.0 / n; for (int i = 0; i < n; i++) a[i] *= inv1; }
                return;
            }
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = ParallelismSettings.MaxDegreeOfParallelism };

            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len / 2;
                int stride = n / len;
                int blockCount = n / len;

                void DoBlock(int blockIndex)
                {
                    int i = blockIndex * len;
                    for (int k = 0; k < half; k++)
                    {
                        var w = twiddles[k * stride];
                        if (conjugate) w = Complex.Conjugate(w);
                        var u = a[i + k];
                        var v = a[i + k + half] * w;
                        a[i + k] = u + v;
                        a[i + k + half] = u - v;
                    }
                }

                if (parallel && blockCount > 1)
                    Parallel.For(0, blockCount, parallelOptions, DoBlock);
                else if (parallel && half >= ParallelismSettings.MinLengthToParallelize)
                {
                    // Last few passes have only 1-2 huge blocks: parallelize over k instead
                    // (each k touches a disjoint pair of elements, so still race-free).
                    for (int b = 0; b < blockCount; b++)
                    {
                        int i0 = b * len;
                        Parallel.For(0, ParallelismSettings.MaxDegreeOfParallelism, parallelOptions, chunk =>
                        {
                            int per = (half + ParallelismSettings.MaxDegreeOfParallelism - 1) / ParallelismSettings.MaxDegreeOfParallelism;
                            int k0 = chunk * per, k1 = Math.Min(half, k0 + per);
                            for (int k = k0; k < k1; k++)
                            {
                                var w = twiddles[k * stride];
                                if (conjugate) w = Complex.Conjugate(w);
                                var u = a[i0 + k];
                                var v = a[i0 + k + half] * w;
                                a[i0 + k] = u + v;
                                a[i0 + k + half] = u - v;
                            }
                        });
                    }
                }
                else
                    for (int b = 0; b < blockCount; b++) DoBlock(b);
            }

            if (normalize)
            {
                double inv = 1.0 / n;
                for (int i = 0; i < n; i++) a[i] *= inv;
            }
        }
    }

    public static class ArrayShift
    {
        public static Complex[] FftShift(Complex[] a) => SwapHalves(a);
        public static Complex[] IfftShift(Complex[] a) => SwapHalves(a);

        /// <summary>fftshift / ifftshift in place (identical for even lengths).</summary>
        public static void SwapHalvesInPlace(Complex[] a)
        {
            int half = a.Length / 2;
            for (int i = 0; i < half; i++) { var t = a[i]; a[i] = a[i + half]; a[i + half] = t; }
        }

        private static Complex[] SwapHalves(Complex[] a)
        {
            int n = a.Length;
            int half = n / 2;
            var result = new Complex[n];
            Array.Copy(a, half, result, 0, n - half);
            Array.Copy(a, 0, result, n - half, half);
            return result;
        }
    }

    /// <summary>
    /// The engine's single entry point for moving between time domain and frequency
    /// domain. Every array on both sides is in the "centered / monotonic ascending"
    /// order that FrequencyGrid.Time and FrequencyGrid.OmegaOffset use -- callers
    /// never need to think about raw DFT bin ordering or call ComplexFft directly.
    ///
    /// This is the standard "centered time grid + centered frequency grid" convention
    /// used throughout the nonlinear-fibre-optics literature (e.g. the widely used
    /// Dudley/Genty/Travers supercontinuum-simulation reference code), where a shift
    /// is required both before and after the raw FFT because the physical time axis
    /// does not start at t=0.
    /// </summary>
    public static class SpectralTransform
    {
        // *** SIGN CONVENTION (fixed -- was inverted until this revision) ***
        // The whole engine uses the PHYSICS convention, consistent with the NLSE as
        // written in Agrawal / Dudley-Genty-Coen and with pyNLO:
        //   E(t) = Re{ A(t) exp(-i w0 t) },   A(t) = (1/N) sum_k  A~(W_k) exp(-i W_k t)
        //   A~(W) = sum_n A(t_n) exp(+i W t_n)
        // so grid bin W (OmegaOffset) really IS physical frequency w0 + W, and
        //   * propagation multiplies A~ by exp(+i phi(w)), phi = k(w) z  (=> positive
        //     group delay dphi/dw DELAYS the pulse, positive GDD puts red at the front)
        //   * SPM is +i gamma |A|^2 A, self-steepening is (1 + W/w0), Raman red-shifts.
        // The previous revision used numpy-style forward exp(-i...) for time->frequency
        // while the propagation/nonlinear operators assumed the physics convention. The
        // net effect was a MIRRORED frequency axis: GDD+SPM stayed mutually consistent
        // (so tests 2/3 passed), but every odd-order term (GD, TOD), self-steepening and
        // Raman acted in the wrong direction, and plotted spectra were flipped about the
        // carrier. Normalisation is unchanged (unnormalised time->freq, 1/N back), so
        // EnergyNormalization's discrete Parseval relation still holds exactly.
        public static Complex[] TimeToFreq(Complex[] timeDomainCentered)
        {
            var a = ArrayShift.IfftShift(timeDomainCentered); // fresh array -- safe to transform in place
            Fft.Provider.TransformInPlace(a, +1, false);
            return ArrayShift.FftShift(a);
        }

        /// <summary>In-place versions (no allocation): the array is overwritten with its transform.</summary>
        public static void TimeToFreqInPlace(Complex[] a)
        {
            ArrayShift.SwapHalvesInPlace(a);
            Fft.Provider.TransformInPlace(a, +1, false);
            ArrayShift.SwapHalvesInPlace(a);
        }

        public static void FreqToTimeInPlace(Complex[] a)
        {
            ArrayShift.SwapHalvesInPlace(a);
            Fft.Provider.TransformInPlace(a, -1, true);
            ArrayShift.SwapHalvesInPlace(a);
        }

        public static Complex[] FreqToTime(Complex[] freqDomainCentered)
        {
            var a = ArrayShift.IfftShift(freqDomainCentered);
            Fft.Provider.TransformInPlace(a, -1, true);
            return ArrayShift.FftShift(a);
        }
    }

    /// <summary>
    /// Pluggable FFT backend. The engine only ever needs one primitive -- an in-place power-of-two complex
    /// transform with a chosen exponent sign and optional 1/N normalisation -- so swapping in FFTW, MKL,
    /// cuFFT or a vendor library is a matter of implementing this interface and assigning
    /// <see cref="Fft.Provider"/> once at start-up (see docs/reference/extending.md).
    /// </summary>
    public interface IFft
    {
        /// <summary>Human-readable backend name (reported in results metadata).</summary>
        string Name { get; }
        /// <summary>In-place transform. exponentSign -1: X[k] = sum x[n] e^{-2 pi i k n/N}; +1: e^{+...}.
        /// normalize divides by N. Any length (time grids are powers of two).</summary>
        void TransformInPlace(Complex[] data, int exponentSign, bool normalize);
    }

    /// <summary>The built-in managed radix-2 FFT (parallelised for large N).</summary>
    public sealed class ManagedRadix2Fft : IFft
    {
        public string Name => "managed-radix2";
        public void TransformInPlace(Complex[] data, int exponentSign, bool normalize) => ComplexFft.TransformInPlace(data, exponentSign, normalize);
    }

    /// <summary>Global FFT backend selection.</summary>
    public static class Fft
    {
        private static IFft _provider = new ManagedRadix2Fft();
        /// <summary>The backend used by <see cref="SpectralTransform"/>. Thread-safe to read; set once at start-up.</summary>
        public static IFft Provider
        {
            get => _provider;
            set => _provider = value ?? throw new System.ArgumentNullException(nameof(value));
        }
    }
}
