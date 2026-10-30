using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>
    /// Speed work that must not change results: the Stockham FFT, native FFT providers (FFTW, Accelerate), the
    /// "compute" options, the allocation-free envelope fast path and the "auto" back-end. Reference: the earlier code
    /// paths, which stay available (ComplexFft.Radix2InPlace, EnvelopeProblem.UseFastPath = false); agreement is
    /// required to rounding level (1e-12 relative), because the arithmetic is the same up to summation order.
    /// </summary>
    [TestArea("CPU FFTs and compute back-ends")]
    public class ComputeTests
    {
        private static Complex[] Random(int n, int seed)
        {
            var r = new Random(seed);
            return Enumerable.Range(0, n).Select(_ => new Complex(r.NextDouble() - 0.5, r.NextDouble() - 0.5)).ToArray();
        }

        private static double RelErr(Complex[] a, Complex[] b)
        {
            double e = 0, m = 0;
            for (int i = 0; i < a.Length; i++) { e = Math.Max(e, (a[i] - b[i]).Magnitude); m = Math.Max(m, b[i].Magnitude); }
            return e / Math.Max(m, 1e-300);
        }

        [Test(Category = "compute", Reference = "direct DFT")]
        public void Stockham_MatchesRadix2AndTheDft_ForEveryPowerOfTwo()
        {
            for (int log = 1; log <= 17; log++)
            {
                int n = 1 << log;
                var x = Random(n, log);
                foreach (int sign in new[] { -1, 1 })
                {
                    var a = (Complex[])x.Clone(); var b = (Complex[])x.Clone();
                    StockhamFft.Transform(a, sign, sign > 0);
                    ComplexFft.Radix2InPlace(b, sign, sign > 0);
                    Assert.Less(RelErr(a, b), 1e-13, "Stockham vs radix-2, N = " + n + ", sign " + sign);
                    if (n <= 512)
                    {
                        var r = MixedRadixFft.Naive(x, sign);
                        if (sign > 0) for (int i = 0; i < n; i++) r[i] /= n;
                        Assert.Less(RelErr(a, r), 1e-12, "Stockham vs the DFT, N = " + n);
                    }
                }
                var c = (Complex[])x.Clone();
                StockhamFft.Transform(c, -1, false); StockhamFft.Transform(c, 1, true);
                Assert.Less(RelErr(c, x), 1e-13, "round trip N = " + n);
            }
            Assert.Throws<ArgumentException>(() => StockhamFft.Transform(new Complex[12], -1, false), "power of two only");
        }

        [Test(Category = "compute")]
        public void FftProviders_ManagedAlwaysWorks_NativeOnesPassTheSelfCheckOrSayWhy()
        {
            Assert.True(FftProviders.Get("managed", out _) == FftProviders.Managed, "managed");
            Assert.True(FftProviders.Get("auto", out _) != null, "auto never fails");
            Assert.True(FftProviders.Get("nonsense", out var why) == null && why.Contains("unknown"), "unknown provider is refused with a reason");
            foreach (var name in new[] { "fftw", "accelerate" })
            {
                var p = FftProviders.Get(name, out var w);
                if (p == null) { Assert.True(!string.IsNullOrEmpty(w), name + " unavailable: a reason is given"); continue; }
                Assert.True(FftProviders.SelfCheck(p, out var bad), name + " self-check: " + bad);
                // any length (FFTW) or the managed fallback (Accelerate) -- 1000 = 2^3 5^3, 1009 prime
                foreach (int n in new[] { 1000, 1009, 8192 })
                {
                    var x = Random(n, n); var a = (Complex[])x.Clone(); var b = (Complex[])x.Clone();
                    p.TransformInPlace(a, -1, false); ComplexFft.TransformInPlace(b, -1, false);
                    Assert.Less(RelErr(a, b), 1e-12, name + " N = " + n);
                }
            }
            var prev = Fft.Provider;
            try
            {
                Assert.True(FftProviders.Select("nonsense").Contains("not available"), "Select reports the fallback");
                Assert.True(Fft.Provider == FftProviders.Managed, "and falls back to managed");
            }
            finally { Fft.Provider = prev; }
        }

        private static SystemDescription KerrPlate(bool raman)
        {
            var mat = FlatMaterial(3e-20);
            if (raman) { mat.Set("raman", O("kind", "silica")); mat.Set("ramanFraction", 0.18); }
            var j = O("format", "ultrafast-system", "version", 2, "name", "compute");
            j.Set("materials", O("Kerr", mat));
            j.Set("elements", A(O("id", "plate", "type", "bulk", "material", "Kerr", "lengthM", 5e-3, "beam", O("kind", "constant", "radiusM", 60e-6))));
            return SystemDescription.FromJson(j);
        }

        private static RunResult KerrRun(SystemDescription sys, double ellipticity, JsonObject compute = null)
        {
            var pulse = O("shape", "gaussian", "centerWavelengthM", 1030e-9, "durationFwhmS", 100e-15, "energyJ", 2e-6,
                "polarization", O("angleDeg", 10.0, "ellipticity", ellipticity));
            var extra = O("physics", O("selfSteepening", true, "raman", true), "grid", O("auto", false, "n", 2048, "windowS", 4e-12));
            if (compute != null) extra.Set("compute", compute);
            return Go(sys, Spec(pulse, extra));
        }

        private static double OutputDiff(RunResult a, RunResult b) =>
            Math.Max(RelErr(a.Output.TemporalAx(), b.Output.TemporalAx()), RelErr(a.Output.TemporalAy(), b.Output.TemporalAy()));

        [Test(Category = "compute")]
        public void EnvelopeFastPath_GivesTheGeneralPathResult_ScalarAndVectorWithRaman()
        {
            foreach (var (raman, ell) in new[] { (false, 0.0), (true, 0.0), (true, 0.4) })
            {
                var sys = KerrPlate(raman);
                RunResult fast, slow;
                try { EnvelopeProblem.UseFastPath = true; fast = KerrRun(sys, ell); EnvelopeProblem.UseFastPath = false; slow = KerrRun(sys, ell); }
                finally { EnvelopeProblem.UseFastPath = true; }
                Assert.Greater(fast.Records.Sum(r => r.BIntegralRad), 1, "real nonlinearity (B > 1)");
                Assert.Less(OutputDiff(fast, slow), 1e-11, "raman " + raman + ", ellipticity " + ell);
                Assert.Equal(slow.Records[0].Steps.Accepted, fast.Records[0].Steps.Accepted, "same step sequence");
            }
        }

        [Test(Category = "compute")]
        public void ComputeFftOption_SelectsTheProvider_AndResultsAgree()
        {
            var sys = KerrPlate(true);
            var managed = KerrRun(sys, 0.3, O("fft", "managed"));
            var auto = KerrRun(sys, 0.3, O("fft", "auto"));
            Assert.Less(OutputDiff(managed, auto), 1e-10, "auto (" + Fft.Provider.Name + ") vs managed");
            var bad = KerrRun(sys, 0.3, O("fft", "accelerate"));
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                Assert.True(bad.Warnings.Any(w => w.Contains("not available")), "Accelerate off macOS: warning and fallback");
            Assert.Less(OutputDiff(managed, bad), 1e-10, "fallback result");
        }

        [Test(Category = "compute")]
        public void AutoBackend_WithoutDevices_RunsOnTheCpu()
        {
            var sys = KerrPlate(false);
            var cpu = KerrRun(sys, 0);
            var auto = KerrRun(sys, 0, O("backend", "auto", "device", 0, "gpuMinPoints", 1));
            Assert.True(ComputeBackends.Get("auto") is AutoBackend, "auto is built in");
            Assert.True(auto.Warnings.All(w => !w.Contains("not available")), "auto is a known back-end");
            if (!ComputeBackends.Available.OfType<IDeviceBackend>().Any(d => d.Unavailable == null))
                Assert.Less(OutputDiff(cpu, auto), 1e-13, "no device: identical CPU result");
            var devs = ComputeBackends.AllDevices();
            Assert.True(devs.Count >= 1 && devs[0].Backend == "cpu", "the device list starts with the CPU");
        }
    }
}
