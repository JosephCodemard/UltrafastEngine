using System.Numerics;

namespace UltrafastEngine.Numerics
{
    /// <summary>
    /// FFT-based circular convolution of two real signals sampled on the same
    /// grid, scaled by dt to approximate the continuous convolution integral
    /// int(signal(t') * kernel(t - t') dt').
    ///
    /// CAVEAT: this is a *circular* convolution -- if the kernel (e.g. the
    /// Raman response, which decays over ~100 fs for fused silica) has not
    /// fully decayed to ~0 within the simulation time window, energy will
    /// wrap around from one end of the window to the other. Keep the time
    /// window comfortably larger than the kernel's decay time; this is not
    /// checked automatically.
    /// </summary>
    public static class Convolution
    {
        public static double[] CircularConvolve(double[] signal, double[] kernel, double dt)
        {
            var kFreq = SpectralTransform.TimeToFreq(ToComplex(kernel));
            return CircularConvolveWithKernelFreq(signal, kFreq, dt);
        }

        /// <summary>Same as CircularConvolve, but takes the kernel's spectrum
        /// pre-transformed (via SpectralTransform.TimeToFreq(ToComplex(kernel))) instead
        /// of the raw kernel array. The kernel is constant for the whole duration of one
        /// nonlinear segment's propagation (it's a property of the material, not the
        /// evolving pulse), so a caller doing many convolutions against the SAME kernel
        /// -- e.g. once per RK4 stage, every sub-step -- should compute kFreq ONCE
        /// up-front and reuse it here, rather than re-transforming an unchanged array
        /// on every call.</summary>
        public static double[] CircularConvolveWithKernelFreq(double[] signal, System.Numerics.Complex[] kFreq, double dt)
        {
            int n = signal.Length;
            var sFreq = SpectralTransform.TimeToFreq(ToComplex(signal));

            var prodFreq = new Complex[n];
            for (int i = 0; i < n; i++) prodFreq[i] = sFreq[i] * kFreq[i];

            var prodTime = SpectralTransform.FreqToTime(prodFreq);

            var result = new double[n];
            for (int i = 0; i < n; i++) result[i] = prodTime[i].Real * dt;
            return result;
        }

        private static Complex[] ToComplex(double[] real)
        {
            var c = new Complex[real.Length];
            for (int i = 0; i < real.Length; i++) c[i] = real[i];
            return c;
        }
    }
}
