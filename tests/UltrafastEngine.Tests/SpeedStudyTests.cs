using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Stage 1.6: FFTs of any length, SIMD kernels, compute back-ends, parameter studies.</summary>
    [TestArea("FFTs, SIMD kernels and studies")]
    public class SpeedStudyTests
    {
        [Test(Reference = "direct DFT")]
        public void Fft_MixedRadixAndBluesteinMatchTheDft()
        {
            var rnd = new Random(3);
            foreach (int n in new[] { 6, 12, 30, 100, 360, 17, 97, 1000, 1009, 2 * 3 * 5 * 7 * 11 })
            {
                var x = Enumerable.Range(0, n).Select(_ => new Complex(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5)).ToArray();
                foreach (int sign in new[] { -1, 1 })
                {
                    var a = (Complex[])x.Clone();
                    ComplexFft.TransformInPlace(a, sign, false);
                    var r = MixedRadixFft.Naive(x, sign);
                    double err = Enumerable.Range(0, n).Max(i => (a[i] - r[i]).Magnitude) / Math.Sqrt(n);
                    Assert.Less(err, 1e-11, "n = " + n + " sign " + sign);
                }
                var b = (Complex[])x.Clone();
                ComplexFft.TransformInPlace(b, -1, false); ComplexFft.TransformInPlace(b, 1, true);
                Assert.Less(Enumerable.Range(0, n).Max(i => (b[i] - x[i]).Magnitude), 1e-12, "round trip n = " + n);
            }
        }

        [Test]
        public void Simd_KernelsAgreeWithScalar()
        {
            var rnd = new Random(5); int n = 1001;
            var a = Enumerable.Range(0, n).Select(_ => new Complex(rnd.NextDouble(), rnd.NextDouble() - 0.5)).ToArray();
            var b = Enumerable.Range(0, n).Select(_ => new Complex(rnd.NextDouble() - 0.5, rnd.NextDouble())).ToArray();
            var m1 = new Complex[n]; var m2 = new Complex[n]; var x1 = new Complex[n]; var x2 = new Complex[n];
            ComplexKernels.ForceScalar = false; ComplexKernels.Multiply(a, b, m1); ComplexKernels.Axpy(a, 0.37, b, x1);
            ComplexKernels.ForceScalar = true; ComplexKernels.Multiply(a, b, m2); ComplexKernels.Axpy(a, 0.37, b, x2);
            ComplexKernels.ForceScalar = false;
            for (int i = 0; i < n; i++)
            {
                Assert.Less((m1[i] - a[i] * b[i]).Magnitude, 1e-15, "multiply (SIMD " + ComplexKernels.Accelerated + ")");
                Assert.Less((m1[i] - m2[i]).Magnitude, 1e-15, "multiply SIMD = scalar");
                Assert.Less((x1[i] - (a[i] + 0.37 * b[i])).Magnitude, 1e-15, "axpy");
            }
        }

        private sealed class CountingBackend : IComputeBackend
        {
            public int Calls;
            public string Name => "test-counting";
            public string Description => "test back-end";
            public bool Supports(IPropagationProblem p) => true;
            public Complex[] Propagate(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress) { Calls++; return Steppers.PropagateCpu(p, a0, st, stats, progress); }
        }

        [Test]
        public void ComputeBackend_SelectedFromTheRunSpec()
        {
            var be = new CountingBackend();
            ComputeBackends.Register(be);
            var sys = PropagationTests.FlatKerrSystem(0.01);
            var o = O("format", "ultrafast-run", "version", 2, "compute", O("backend", "test-counting", "threads", 2));
            o.Set("pulse", Gauss(1030e-9, 100e-15, 1e-6));
            var r = Simulation.Run(sys, Specs.RunSpec.FromJson(o));
            Assert.Equal(1, be.Calls, "the nonlinear element ran on the selected back-end");
            Assert.True(r.Records[0].Steps.Notes.Any(n => n.Contains("test-counting")), "reported");
            var o2 = O("format", "ultrafast-run", "version", 2, "compute", O("backend", "nonexistent"));
            o2.Set("pulse", Gauss(1030e-9, 100e-15, 1e-6));
            Assert.True(Simulation.Run(sys, Specs.RunSpec.FromJson(o2)).Warnings.Any(w => w.Contains("not available")), "unknown back-end falls back to cpu");
        }

        [Test]
        public void Sobol_SequenceIsStratified()
        {
            var s = new Sobol(6);
            var pts = Enumerable.Range(0, 64).Select(_ => s.Next()).ToArray();   // includes the leading 0
            Assert.Close(0.5, pts[1][0], 0, 1e-12, "x1"); Assert.Close(0.75, pts[2][0], 0, 1e-12, "x2"); Assert.Close(0.25, pts[2][1], 0, 1e-12, "y2");
            for (int d = 0; d < 6; d++)
            {
                var bins = new int[64];
                foreach (var p in pts) bins[(int)(p[d] * 64)]++;
                Assert.True(bins.All(b => b == 1), "dimension " + d + ": one point per 1/64 interval");
            }
            // 2-D: 64 points stratify the unit square in 8 x 8 cells for the first pair
            var cells = new int[64]; foreach (var p in pts) cells[(int)(p[0] * 8) * 8 + (int)(p[1] * 8)]++;
            Assert.True(cells.All(c => c == 1), "(0, 1) projection is a (0, 6, 2)-net");
        }

        private static (SystemDescription, Specs.RunSpec) KerrPair()
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "pair", "materials", O("flat", FlatMaterial(2.5e-20)));
            o.Set("elements", A(O("id", "a", "type", "bulk", "material", "flat", "lengthM", 0.005, "beam", O("kind", "constant", "areaM2", 1e-8)),
                                O("id", "b", "type", "bulk", "material", "flat", "lengthM", 0.01, "beam", O("kind", "constant", "areaM2", 1e-8))));
            var r = O("format", "ultrafast-run", "version", 2, "physics", O("selfSteepening", false, "raman", false));
            r.Set("pulse", O("shape", "gaussian", "centerWavelengthM", 1030e-9, "durationFwhmS", 200e-15, "peakPowerW", 2e6));
            return (SystemDescription.FromJson(o), Specs.RunSpec.FromJson(r));
        }

        [Test(Reference = "B = γ P L", Tolerance = "1e-09, 1 %")]
        public void Study_ScanWithPrefixCacheMatchesFullRuns()
        {
            var (s, r) = KerrPair();
            var spec = StudySpec.FromJson(O("mode", "scan", "sampling", "grid", "parallel", 2,
                "parameters", A(O("path", "element:b/lengthM", "min", 0.005, "max", 0.02, "points", 4)), "outputs", A("record:b/BIntegralRad", "pulse:end/fwhmS")));
            var cached = Studies.Run(s, r, spec);
            spec.PrefixCache = false;
            var full = Studies.Run(s, r, spec);
            Assert.True(cached.Notes.Any(n => n.Contains("prefix cache")), "prefix cache used");
            Assert.Equal(4, cached.Evaluations, "4 grid points");
            for (int i = 0; i < 4; i++)
            {
                int j = full.Parameters.FindIndex(p => p[0] == cached.Parameters[i][0]);
                Assert.Close(full.Outputs[j][0], cached.Outputs[i][0], 1e-9, 0, "B with and without the cache");
                // B of element b is proportional to its length (gamma P0 L)
                double gamma = 2.5e-20 * W(1030e-9) / (C * 1e-8);
                Assert.Close(gamma * 2e6 * cached.Parameters[i][0], cached.Outputs[i][0], 0.01, 0, "B = gamma P L");
            }
        }

        [Test]
        public void Study_OptimizeAndSensitivities()
        {
            var (s, r) = KerrPair();
            double gamma = 2.5e-20 * W(1030e-9) / (C * 1e-8);
            double target = 0.5;
            var opt = Studies.Run(s, r, StudySpec.FromJson(O("mode", "optimize", "parameters", A(O("path", "element:b/lengthM", "min", 0.001, "max", 0.05)),
                "outputs", A("record:b/BIntegralRad"), "objective", O("output", "record:b/BIntegralRad", "goal", "target", "target", target, "maxEvaluations", 60))));
            double lopt = opt.Analysis.GetObject("optimum").GetDouble("element:b/lengthM", 0);
            Assert.Close(target / (gamma * 2e6), lopt, 0.02, 0, "length giving B = 0.5");

            var loc = Studies.Run(s, r, StudySpec.FromJson(O("mode", "sensitivity", "parameters", A(O("path", "element:b/lengthM", "min", 0.005, "max", 0.015), O("path", "element:b/n2", "min", 1e-20, "max", 4e-20)),
                "outputs", A("record:b/BIntegralRad"), "sensitivity", O("method", "local"))));
            var e = loc.Analysis.GetObject("sensitivities").GetObject("record:b/BIntegralRad");
            Assert.Close(1.0, e.GetObject("element:b/lengthM").GetDouble("elasticity", 0), 0.01, 0, "B elasticity to L");
            Assert.Close(1.0, e.GetObject("element:b/n2").GetDouble("elasticity", 0), 0.01, 0, "B elasticity to n2");

            var sob = Studies.Run(s, r, StudySpec.FromJson(O("mode", "sensitivity", "parallel", 4, "parameters", A(O("path", "element:b/lengthM", "min", 0.005, "max", 0.015), O("path", "element:a/lengthM", "min", 0.001, "max", 0.0011)),
                "outputs", A("record:b/BIntegralRad"), "sensitivity", O("method", "sobol", "samples", 512))));
            var so = sob.Analysis.GetObject("sensitivities").GetObject("record:b/BIntegralRad");
            Assert.Close(1.0, so.GetObject("element:b/lengthM").GetDouble("first", 0), 0, 0.1, "first-order index of the only influential parameter");
            Assert.Less(Math.Abs(so.GetObject("element:a/lengthM").GetDouble("total", 1)), 0.05, "an element before b does not change B of b");
        }
    }
}
