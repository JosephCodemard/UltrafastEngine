using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using UltrafastEngine.Numerics;
using UltrafastEngine.Plugins.Gpu;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>
    /// The gpu plugin (plugins/gpu) against the CPU path. Reference: the CPU result of the same run -- the device runs
    /// the same interaction-picture schemes with the same arithmetic, so outputs must agree to rounding (1e-9 relative
    /// is required; ~1e-15 is typical). Needs an OpenCL device with double precision: on machines without a GPU, PoCL
    /// (pip install pocl-binary-distribution; docs/testing.md) provides one on the CPU. Skipped when none is found.
    /// The CUDA back-end shares the kernels and the steppers; it is exercised by `ultrafast devices --selftest` on
    /// NVIDIA machines.
    /// </summary>
    public class GpuTests
    {
        private static GpuBackend Device()
        {
            foreach (GpuBackend b in new GpuBackend[] { GpuPlugin.Cuda, GpuPlugin.OpenCl })
                if (b.Unavailable == null && b.Devices.Any(d => d.Usable)) return b;
            Assert.Skip("no CUDA or OpenCL device with double precision (" + GpuPlugin.OpenCl.Unavailable + ")");
            return null;
        }

        private static JsonObject On(GpuBackend b, JsonObject extra = null)
        {
            var o = O("backend", b.Name, "gpuMinPoints", 1);
            if (extra != null) foreach (var kv in extra) o.Set(kv.Key, kv.Value);
            return o;
        }

        private static double Diff(RunResult a, RunResult b)
        {
            double e = 0, m = 0;
            foreach (var (x, y) in new[] { (a.Output.TemporalAx(), b.Output.TemporalAx()), (a.Output.TemporalAy(), b.Output.TemporalAy()) })
                for (int i = 0; i < x.Length; i++) { e = Math.Max(e, (x[i] - y[i]).Magnitude); m = Math.Max(m, y[i].Magnitude); }
            return e / m;
        }

        private static bool OnDevice(RunResult r) => r.Records.Any(x => x.Steps != null && x.Steps.Notes.Any(n => n.StartsWith("device: ")));
        private static string Notes(RunResult r) => string.Join(" | ", r.Records.Where(x => x.Steps != null).SelectMany(x => x.Steps.Notes));

        private static (RunResult cpu, RunResult dev) Pair(string kind, int n, int tr, JsonObject compute, JsonObject stepper = null)
        {
            var (sys, cpuRun) = GpuSelfTest.Problem(kind, n, tr, O("backend", "cpu"));
            var (_, devRun) = GpuSelfTest.Problem(kind, n, tr, compute);
            if (stepper != null) { cpuRun.Stepper.Method = stepper.GetString("method", "adaptive"); devRun.Stepper.Method = cpuRun.Stepper.Method; }
            return (Simulation.Run(sys, cpuRun), Simulation.Run(sys, devRun));
        }

        [Test(Category = "gpu")]
        public void Fft_BuiltInAndVendor_MatchTheManagedFft()
        {
            var b = Device();
            var s = GpuBackend.StreamFor(b.Context(b.Devices.First(d => d.Usable).Index));
            foreach (int n in new[] { 2, 4, 8, 512, 2048, 8192 })
                Assert.Less(GpuSelfTest.FftCheck(s, n, 3, true), 1e-13, "contiguous batch, N = " + n);
            Assert.Less(GpuSelfTest.FftCheck(s, 1024, 2, false), 1e-13, "built-in kernels");
            Assert.Less(GpuSelfTest.StridedFftCheck(s), 1e-13, "strided axis (cartesian transverse FFT)");
        }

        [Test(Category = "gpu")]
        public void Envelope_ScalarAndVector_MatchTheCpu_AdaptiveAndFixed()
        {
            var b = Device();
            foreach (var kind in new[] { "scalar", "vector" })
                foreach (var method in new[] { "adaptive", "rk4ip" })
                {
                    var (c, d) = Pair(kind, 2048, 0, On(b), O("method", method));
                    Assert.True(OnDevice(d), kind + " " + method + " ran on the device: " + Notes(d));
                    Assert.Greater(c.Records.Sum(r => r.BIntegralRad), 1, "real nonlinearity");
                    Assert.Less(Diff(d, c), 1e-9, kind + " " + method + ": output field");
                    Assert.Close(c.Records[0].BIntegralRad, d.Records[0].BIntegralRad, 1e-9, 0, kind + " " + method + ": B-integral");
                    Assert.Equal(c.Records[0].Steps.Accepted, d.Records[0].Steps.Accepted, kind + " " + method + ": same steps");
                }
        }

        [Test(Category = "gpu")]
        public void Spatial_RadialAndCartesian_MatchTheCpu()
        {
            var b = Device();
            foreach (var (kind, n, tr) in new[] { ("radial", 256, 24), ("cartesian", 128, 16) })
            {
                var (c, d) = Pair(kind, n, tr, On(b));
                Assert.True(OnDevice(d), kind + " ran on the device: " + Notes(d));
                Assert.Greater(c.Records.Sum(r => r.BIntegralRad), 0.5, kind + ": nonlinear");
                Assert.Less(Diff(d, c), 1e-9, kind + ": output field");
                Assert.Close(c.Records[0].BIntegralRad, d.Records[0].BIntegralRad, 1e-9, 0, kind + ": B-integral");
            }
        }

        [Test(Category = "gpu")]
        public void Spatial_UppeCoupling_MatchesTheCpu()
        {
            var b = Device();
            var (sys, cpuRun) = GpuSelfTest.Problem("radial", 256, 24, O("backend", "cpu"));
            var el = sys.ToJson();
            var tv = el.GetArray("elements")[0].AsObject().GetObject("transverse");
            tv.Set("equation", "uppe");
            var sys2 = Systems.SystemDescription.FromJson(el);
            var (_, devRun) = GpuSelfTest.Problem("radial", 256, 24, On(b));
            var c = Simulation.Run(sys2, cpuRun); var d = Simulation.Run(sys2, devRun);
            Assert.True(OnDevice(d), "ran on the device: " + Notes(d));
            Assert.Less(Diff(d, c), 1e-9, "UPPE output field");
        }

        [Test(Category = "gpu")]
        public void SmallOrUnsupportedProblems_StayOnTheCpu_WithANote()
        {
            var b = Device();
            var (c, d) = Pair("scalar", 1024, 0, O("backend", b.Name, "gpuMinPoints", 1 << 20));
            Assert.False(OnDevice(d), "below gpuMinPoints");
            Assert.True(Notes(d).Contains("small problem"), "the note says why: " + Notes(d));
            Assert.Less(Diff(d, c), 1e-14, "identical CPU result");
            // "auto" never picks an OpenCL CPU device (PoCL) unless allowed
            if (b is OpenClBackend && b.Devices.All(x => x.Notes.StartsWith("CPU device")))
            {
                var (_, a) = Pair("scalar", 2048, 0, O("backend", "auto", "gpuMinPoints", 1));
                Assert.False(OnDevice(a), "auto skips CPU OpenCL devices");
            }
        }

        [Test(Category = "gpu")]
        public void ConcurrentRuns_ShareTheDevice_AndMatchTheCpu()
        {
            // Phase D: parallel runs (studies, fits) each get their own stream / queue on the device
            var b = Device();
            var (sys, cpuRun) = GpuSelfTest.Problem("vector", 2048, 0, O("backend", "cpu"));
            var cpu = Simulation.Run(sys, cpuRun);
            var results = new RunResult[6];
            Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 3 }, i =>
            {
                var (s2, r2) = GpuSelfTest.Problem("vector", 2048, 0, On(b, O("deviceSlots", 8)));
                results[i] = Simulation.Run(s2, r2);
            });
            foreach (var r in results)
            {
                Assert.True(OnDevice(r), "on the device: " + Notes(r));
                Assert.Less(Diff(r, cpu), 1e-9, "concurrent run matches the CPU");
            }
            Assert.Equal(0, b.RunningOn(b.Devices.First(d => d.Usable).Index), "all device slots released");
        }

        [Test(Category = "gpu")]
        public void DevicesCommand_ListsTheCpuAndTheDevice_AndTheSelfTestPasses()
        {
            var b = Device();
            var r = Commands.Devices(true, false, b.Name);
            var devs = r.GetArray("devices").Select(x => x.AsObject()).ToList();
            Assert.Equal("cpu", devs[0].GetString("backend", ""), "CPU first");
            Assert.True(devs.Any(x => x.GetString("backend", "") == b.Name), "the device is listed");
            var t = r.GetArray("selftest")[0].AsObject();
            Assert.True(t.GetBool("passed", false), "self-test: " + t.ToJson(false));
        }
    }
}
