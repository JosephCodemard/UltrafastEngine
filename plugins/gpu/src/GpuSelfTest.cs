using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Plugins.Gpu
{
    /// <summary>
    /// Standard problems run on a device and on the CPU (`ultrafast devices --selftest [--benchmark]`,
    /// <c>uf.devices(selftest=True)</c>, <c>uf_devices_json</c>): FFTs against the managed FFT, then 1-D scalar and
    /// vector Kerr + Raman + self-steepening and radial / cartesian spatial self-focusing against the CPU result.
    /// A check passes when the device output matches the CPU to 1e-6 (relative, largest sample); both use the same
    /// adaptive scheme, so they normally agree to ~1e-10.
    /// </summary>
    public static class GpuSelfTest
    {
        public const double Tolerance = 1e-6;

        /// <summary>One of the standard problems: "scalar", "vector", "radial", "cartesian", with grid size n (1-D)
        /// or n samples x transverse points.</summary>
        public static (SystemDescription, RunSpec) Problem(string kind, int n, int transverse, JsonObject compute)
        {
            var mat = new JsonObject().Set("index", new JsonObject().Set("kind", "constant").Set("value", 1.45)).Set("n2", 2.7e-20)
                .Set("raman", new JsonObject().Set("kind", "silica")).Set("ramanFraction", 0.18);
            var el = new JsonObject().Set("id", "plate").Set("type", "bulk").Set("material", "Kerr");
            var pulse = new JsonObject().Set("shape", "gaussian").Set("centerWavelengthM", 1030e-9).Set("durationFwhmS", 100e-15);
            double window = 2e-12;
            switch (kind)
            {
                case "vector":
                    el.Set("lengthM", 5e-3).Set("beam", new JsonObject().Set("kind", "constant").Set("radiusM", 60e-6));
                    pulse.Set("energyJ", 2e-6).Set("polarization", new JsonObject().Set("angleDeg", 10.0).Set("ellipticity", 0.4));
                    break;
                case "radial":
                    el.Set("lengthM", 3e-3).Set("beam", new JsonObject().Set("kind", "gaussian").Set("w0M", 60e-6))
                      .Set("transverse", new JsonObject().Set("model", "radial").Set("points", transverse).Set("radiusM", 0.3e-3));
                    pulse.Set("energyJ", 1e-6);
                    break;
                case "cartesian":
                    el.Set("lengthM", 3e-3).Set("beam", new JsonObject().Set("kind", "gaussian").Set("w0M", 60e-6))
                      .Set("transverse", new JsonObject().Set("model", "cartesian").Set("nx", transverse).Set("ny", transverse).Set("halfWidthM", 0.3e-3));
                    pulse.Set("energyJ", 1e-6).Set("polarization", new JsonObject().Set("angleDeg", 0.0).Set("ellipticity", 0.3));
                    break;
                default:
                    el.Set("lengthM", 5e-3).Set("beam", new JsonObject().Set("kind", "constant").Set("radiusM", 40e-6));
                    pulse.Set("energyJ", 2e-6);
                    break;
            }
            var sys = new JsonObject().Set("format", "ultrafast-system").Set("version", 2).Set("name", "gpu-selftest-" + kind)
                .Set("materials", new JsonObject().Set("Kerr", mat)).Set("elements", new JsonArray(new JsonValue[] { el }));
            var run = new JsonObject().Set("format", "ultrafast-run").Set("version", 2).Set("pulse", pulse)
                .Set("physics", new JsonObject().Set("selfSteepening", true).Set("raman", true))
                .Set("grid", new JsonObject().Set("auto", false).Set("n", n).Set("windowS", window));
            if (compute != null) run.Set("compute", compute);
            return (SystemDescription.FromJson(sys), RunSpec.FromJson(run));
        }

        private static double Diff(RunResult a, RunResult b)
        {
            double e = 0, m = 0;
            foreach (var (x, y) in new[] { (a.Output.TemporalAx(), b.Output.TemporalAx()), (a.Output.TemporalAy(), b.Output.TemporalAy()) })
                for (int i = 0; i < x.Length; i++) { e = Math.Max(e, (x[i] - y[i]).Magnitude); m = Math.Max(m, y[i].Magnitude); }
            return m > 0 ? e / m : e;
        }

        private static JsonObject DeviceCompute(GpuBackend b, int device) =>
            new JsonObject().Set("backend", b.Name).Set("device", device).Set("gpuMinPoints", 1);

        private static bool RanOnDevice(RunResult r) => r.Records.Any(x => x.Steps != null && x.Steps.Notes.Any(n => n.StartsWith("device: ")));

        private static string FailNote(RunResult r) => string.Join("; ", r.Records.Where(x => x.Steps != null).SelectMany(x => x.Steps.Notes).Where(n => n.Contains(" failed") || n.Contains("ran on the CPU")));

        public static JsonObject Run(GpuBackend b, int device, bool benchmark)
        {
            var res = new JsonObject().Set("backend", b.Name);
            if (b.Unavailable != null) return res.Set("passed", false).Set("error", b.Unavailable);
            if (device < 0) device = b.Devices.FirstOrDefault(d => d.Usable)?.Index ?? 0;
            res.Set("device", device).Set("name", device < b.Devices.Count ? b.Devices[device].Name : "?");
            var checks = new List<JsonValue>();
            bool all = true;
            void Add(string name, bool ok, double err, string note, double seconds)
            {
                all &= ok;
                var c = new JsonObject().Set("check", name).Set("passed", ok).Set("relativeError", err).Set("seconds", seconds);
                if (!string.IsNullOrEmpty(note)) c.Set("note", note);
                checks.Add(c);
            }
            // FFTs
            try
            {
                var ctx = b.Context(device);
                var s = GpuBackend.StreamFor(ctx);
                var sw = Stopwatch.StartNew();
                double err = FftCheck(s, 4096, 3, true) ;
                err = Math.Max(err, FftCheck(s, 2048, 2, false));
                err = Math.Max(err, StridedFftCheck(s));
                Add("fft", err < 1e-12, err, ctx.VendorFftName ?? "built-in Stockham", sw.Elapsed.TotalSeconds);
            }
            catch (Exception ex) { Add("fft", false, double.NaN, ex.Message.Split('\n')[0], 0); res.Set("checks", new JsonArray(checks.ToArray())).Set("passed", false); return res; }
            // propagation problems
            foreach (var (kind, n, tr) in new[] { ("scalar", 4096, 0), ("vector", 4096, 0), ("radial", 512, 32), ("cartesian", 256, 16) })
            {
                try
                {
                    var (sys, cpuRun) = Problem(kind, n, tr, new JsonObject().Set("backend", "cpu"));
                    var (_, devRun) = Problem(kind, n, tr, DeviceCompute(b, device));
                    var rc = Simulation.Run(sys, cpuRun);
                    var sw = Stopwatch.StartNew();
                    var rd = Simulation.Run(sys, devRun);
                    double t = sw.Elapsed.TotalSeconds;
                    bool used = RanOnDevice(rd);
                    double err = Diff(rd, rc);
                    Add(kind, used && err < Tolerance, err, used ? null : "did not run on the device: " + FailNote(rd), t);
                }
                catch (Exception ex) { Add(kind, false, double.NaN, ex.Message.Split('\n')[0], 0); }
            }
            res.Set("checks", new JsonArray(checks.ToArray())).Set("passed", all);
            if (benchmark) res.Set("benchmark", Benchmark(b, device));
            return res;
        }

        /// <summary>Times the CPU and the device on growing problems (seconds per run; speed-up = cpu / device).</summary>
        public static JsonArray Benchmark(GpuBackend b, int device)
        {
            var rows = new List<JsonValue>();
            foreach (var (kind, n, tr) in new[] { ("scalar", 8192, 0), ("scalar", 65536, 0), ("vector", 32768, 0), ("radial", 1024, 64), ("cartesian", 512, 32) })
            {
                var row = new JsonObject().Set("problem", kind).Set("n", n);
                if (tr > 0) row.Set("transversePoints", kind == "cartesian" ? tr * tr : tr);
                try
                {
                    var (sys, cpuRun) = Problem(kind, n, tr, new JsonObject().Set("backend", "cpu"));
                    var (_, devRun) = Problem(kind, n, tr, DeviceCompute(b, device));
                    Simulation.Run(sys, devRun);   // warm-up (kernel compilation, plans)
                    var sw = Stopwatch.StartNew(); var rc = Simulation.Run(sys, cpuRun); double tc = sw.Elapsed.TotalSeconds;
                    sw.Restart(); var rd = Simulation.Run(sys, devRun); double td = sw.Elapsed.TotalSeconds;
                    row.Set("cpuSeconds", tc).Set("deviceSeconds", td).Set("speedup", td > 0 ? tc / td : 0).Set("steps", rc.Records[0].Steps?.Accepted ?? 0)
                       .Set("relativeError", Diff(rd, rc)).Set("onDevice", RanOnDevice(rd));
                }
                catch (Exception ex) { row.Set("error", ex.Message.Split('\n')[0]); }
                rows.Add(row);
            }
            return new JsonArray(rows.ToArray());
        }

        private static Complex[] Rand(int n, int seed)
        {
            var r = new Random(seed);
            return Enumerable.Range(0, n).Select(_ => new Complex(r.NextDouble() - 0.5, r.NextDouble() - 0.5)).ToArray();
        }

        public static double FftCheck(GpuStream s, int n, int batch, bool vendor)
        {
            var x = Rand(n * batch, n);
            double worst = 0;
            using (var buf = s.Context.Alloc(16L * n * batch))
            using (var f = new GpuFft(s, n, batch, vendor))
                foreach (int sign in new[] { -1, 1 })
                {
                    s.Write(buf, x);
                    f.Transform(buf, sign, sign > 0);
                    var got = new Complex[n * batch];
                    s.Read(buf, got);
                    for (int b = 0; b < batch; b++)
                    {
                        var r = new Complex[n]; Array.Copy(x, b * n, r, 0, n);
                        ComplexFft.TransformInPlace(r, sign, sign > 0);
                        double e = 0, m = 0;
                        for (int i = 0; i < n; i++) { e = Math.Max(e, (got[b * n + i] - r[i]).Magnitude); m = Math.Max(m, r[i].Magnitude); }
                        worst = Math.Max(worst, e / m);
                    }
                }
            return worst;
        }

        /// <summary>FFT along the middle axis of a [4][16][8] array against the managed FFT.</summary>
        public static double StridedFftCheck(GpuStream s)
        {
            int a = 4, nq = 16, inner = 8;
            var x = Rand(a * nq * inner, 7);
            using (var buf = s.Context.Alloc(16L * x.Length))
            using (var f = new GpuFft(s, nq, a * inner, inner, nq * inner, inner, false, x.Length))
            {
                s.Write(buf, x);
                f.Transform(buf, -1, false);
                var got = new Complex[x.Length]; s.Read(buf, got);
                double worst = 0;
                for (int i0 = 0; i0 < a; i0++)
                    for (int k = 0; k < inner; k++)
                    {
                        var r = new Complex[nq];
                        for (int q = 0; q < nq; q++) r[q] = x[(i0 * nq + q) * inner + k];
                        ComplexFft.TransformInPlace(r, -1, false);
                        double m = r.Max(c => c.Magnitude);
                        for (int q = 0; q < nq; q++) worst = Math.Max(worst, (got[(i0 * nq + q) * inner + k] - r[q]).Magnitude / m);
                    }
                return worst;
            }
        }
    }
}
