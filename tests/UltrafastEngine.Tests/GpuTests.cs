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
    [TestArea("GPU back-ends (CUDA / OpenCL) against the CPU")]
    public class GpuTests
    {
        private static GpuBackend Device()
        {
            foreach (GpuBackend b in new GpuBackend[] { GpuPlugin.Cuda, GpuPlugin.OpenCl })
                if (b.Unavailable == null && b.Devices.Any(d => d.Usable)) return b;
            Assert.Skip("no CUDA or OpenCL device with double precision (" + GpuPlugin.OpenCl.Unavailable + ")");
            return null;
        }

        /// <summary>Every back-end with a usable double-precision device (CUDA and OpenCL on an NVIDIA machine); skips when none.</summary>
        private static GpuBackend[] AllDevices()
        {
            var all = new GpuBackend[] { GpuPlugin.Cuda, GpuPlugin.OpenCl }.Where(b => b.Unavailable == null && b.Devices.Any(d => d.Usable)).ToArray();
            if (all.Length == 0) Assert.Skip("no CUDA or OpenCL device with double precision (" + GpuPlugin.OpenCl.Unavailable + ")");
            return all;
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

        /// <summary>True when an element ran on the device to the end (a device failure falls back to the CPU with a "failed" note).</summary>
        private static bool OnDevice(RunResult r) => r.Records.Any(x => x.Steps != null && x.Steps.Notes.Any(n => n.StartsWith("device: ")))
            && !r.Records.Any(x => x.Steps != null && x.Steps.Notes.Any(n => n.Contains(" failed (")));
        private static string Notes(RunResult r) => string.Join(" | ", r.Records.Where(x => x.Steps != null).SelectMany(x => x.Steps.Notes));

        private static (RunResult cpu, RunResult dev) Pair(string kind, int n, int tr, JsonObject compute, JsonObject stepper = null)
        {
            var (sys, cpuRun) = GpuSelfTest.Problem(kind, n, tr, O("backend", "cpu"));
            var (_, devRun) = GpuSelfTest.Problem(kind, n, tr, compute);
            if (stepper != null) { cpuRun.Stepper.Method = stepper.GetString("method", "adaptive"); devRun.Stepper.Method = cpuRun.Stepper.Method; }
            return (Simulation.Run(sys, cpuRun), Simulation.Run(sys, devRun));
        }

        [Test(Category = "gpu", Reference = "cross-check: the managed CPU FFT")]
        public void Fft_BuiltInAndVendor_MatchTheManagedFft()
        {
            var b = Device();
            var s = GpuBackend.StreamFor(b.Context(b.Devices.First(d => d.Usable).Index));
            foreach (int n in new[] { 2, 4, 8, 512, 2048, 8192 })
                Assert.Less(GpuSelfTest.FftCheck(s, n, 3, true), 1e-13, "contiguous batch, N = " + n);
            Assert.Less(GpuSelfTest.FftCheck(s, 1024, 2, false), 1e-13, "built-in kernels");
            Assert.Less(GpuSelfTest.StridedFftCheck(s), 1e-13, "strided axis (cartesian transverse FFT)");
        }

        [Test(Category = "gpu", Reference = "cross-check: the CPU solver", Tolerance = "1e-09")]
        public void Envelope_ScalarAndVector_MatchTheCpu_AdaptiveAndFixed()
        {
            foreach (var b in AllDevices())
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

        /// <summary>A 1-D gas cell with the rotational (orientational) Raman response of a molecular gas.</summary>
        private static RunResult GasCell(string backend, GpuBackend b, JsonObject gas, JsonValue polarization, string method, int n = 2048)
        {
            var sys = Systems.SystemDescription.FromJson(O("format", "ultrafast-system", "version", 2, "name", "gas", "materials", O("g", gas),
                "elements", A(O("id", "cell", "type", "bulk", "material", "g", "lengthM", 1.0, "beam", O("kind", "constant", "areaM2", 2e-8)))));
            var pulse = O("shape", "gaussian", "centerWavelengthM", 800e-9, "durationFwhmS", 150e-15, "energyJ", 60e-6);
            pulse.Set("polarization", polarization);
            var run = O("format", "ultrafast-run", "version", 2, "pulse", pulse, "grid", O("n", n, "windowS", 10e-12),
                "stepper", O("method", method), "compute", backend == "cpu" ? O("backend", "cpu") : On(b));
            return Simulation.Run(sys, Specs.RunSpec.FromJson(run));
        }

        [Test(Category = "gpu", Reference = "cross-check: the CPU solver (RotationalRamanResponse.Convolve, EnvelopeProblem.Nonlinear)", Tolerance = "1e-09")]
        public void GasRotationalRaman_ScalarAndVector_MatchTheCpu()
        {
            // backlog B3 kernel 1: any molecular-gas element (multipass-cell air gaps) used to stay on the CPU
            var cases = new (string name, JsonObject gas, JsonValue pol)[]
            {
                ("N2 linear (scalar)", O("gas", O("species", "N2", "pressureBar", 1.0)), "x"),
                ("N2 elliptical (vector)", O("gas", O("species", "N2", "pressureBar", 1.0)), O("angleDeg", 30.0, "ellipticity", 0.3)),
                ("air, n2Scale 0.7, 45 deg (vector, scaled kernel)", O("gas", O("species", "Air", "pressureBar", 2.0), "n2Scale", 0.7), "45"),
            };
            var devices = AllDevices();
            foreach (var (name0, gas, pol) in cases)
                foreach (var method in new[] { "adaptive", "rk4ip" })
                {
                    var c = GasCell("cpu", null, gas, pol, method);
                    foreach (var b in devices)
                    {
                    string name = b.Name + " " + name0;
                    var d = GasCell(b.Name, b, gas, pol, method);
                    Assert.True(OnDevice(d), name + " " + method + " ran on the device: " + Notes(d));
                    Console.WriteLine("    " + name + " " + method + ": rel. field error " + Diff(d, c).ToString("E2") + ", B " + c.Records[0].BIntegralRad.ToString("F2") + " rad");
                    Assert.Greater(c.Records[0].BIntegralRad, 0.5, name + ": real nonlinearity");
                    Assert.Less(Diff(d, c), 1e-9, name + " " + method + ": output field");
                    Assert.Close(c.Records[0].BIntegralRad, d.Records[0].BIntegralRad, 1e-9, 0, name + " " + method + ": B-integral");
                    Assert.Equal(c.Records[0].Steps.Accepted, d.Records[0].Steps.Accepted, name + " " + method + ": same steps");
                    }
                }
            // the orientational part matters here: switching it off moves the field by far more than the tolerance
            var off = GasCell("cpu", null, O("gas", O("species", "N2", "pressureBar", 1.0), "rotationalRaman", false), "x", "adaptive");
            Assert.Greater(Diff(off, GasCell("cpu", null, O("gas", O("species", "N2", "pressureBar", 1.0)), "x", "adaptive")), 1e-3, "rotational Raman is a real term");
        }

        /// <summary>An expression that uses every function, operator and response variable: a real index change (Kerr-like terms
        /// with t, t_lab, F, Ix, Iy, z / L) plus a small term (1e-10) that only exercises the rest. Comparisons are on quantities
        /// that are bit-identical on the CPU and the device (t, z, exact zeros), so no threshold can flip on a rounding difference.</summary>
        internal const string RichExpr =
            "n2x*I*(1 + 0.2*tanh(t/80e-15)) + 2e-6*F/(F + Fs) + 5e-21*Ix*if(t_lab > t_arrival, 1, 0.5) + 3e-21*Iy*gauss(t, 150e-15)"
            + " + 1e-21*min(I, 2e15, Ix + 1e14)*(1 + 0.1*cos(omega0*t/50))*step(1e-13 - t) + 1e-21*max(Ix, Iy)*clamp(t/1e-13, -0.5, 0.5)"
            + " + 1e-10*(round(t/1e-14)*1e-3 + floor(z/L*3) + ceil(z/L) + sign(t) + log(1 + I/1e15) + log10(1 + Ix/1e15) + sqrt(abs(t/1e-13))"
            + " + sinh(t/1e-12) + cosh(t/1e-12) + asin(0.3*z/L) + acos(0.3) + atan(t/1e-13) + tan(0.2*z/L) + pow(2, z/L) + 2^(z/L) + 3**0.5"
            + " + !(t > 0) + ((t > 0) && (z >= 0)) + ((t < 0) || (Iy == 0)) + (t != 0) + (t <= 0) + (t == 0) + sech(t/1e-13) + exp(-abs(t)/1e-13)"
            + " + atan2(t, 1e-13) + hypot(t/1e-13, 1) + theta + r/1e-3 + x/1e-3 + y/1e-3 + spatial + E + I_inst/1e16 + Aeff/1e-8 + lambda0/1e-6 - -e + pi)";
        internal const string RichExprIm = "1e-23*I*clamp(t/1e-13, -1, 1)^2 + 1e-12*sech(t/1e-13)*log10(1 + Aeff/1e-12)*1e-3";

        private static RunResult Responses(JsonObject compute, JsonValue pol, JsonObject transverse, string expr = RichExpr, bool extra = true)
        {
            var el = O("id", "p", "type", "bulk", "material", "kerr", "lengthM", 5e-3, "timeResponse", O("kind", "expression", "expr", expr, "exprIm", RichExprIm, "params", O("n2x", 1e-20, "Fs", 200.0)));
            if (transverse == null) el.Set("beam", O("kind", "constant", "radiusM", 40e-6));
            else { el.Set("beam", O("kind", "gaussian", "w0M", 60e-6)); el.Set("transverse", transverse); }
            var sys = Systems.SystemDescription.FromJson(O("format", "ultrafast-system", "version", 2, "name", "ur",
                "materials", O("kerr", O("index", O("kind", "constant", "value", 1.45), "n2", 2.5e-20, "raman", O("kind", "silica"), "ramanFraction", 0.18)), "elements", A(el)));
            var pulse = O("shape", "gaussian", "centerWavelengthM", 1030e-9, "durationFwhmS", 100e-15, "energyJ", transverse == null ? 2e-6 : 1e-6);
            pulse.Set("polarization", pol);
            var run = O("format", "ultrafast-run", "version", 2, "pulse", pulse, "grid", O("auto", false, "n", transverse == null ? 2048 : 512, "windowS", 2e-12), "compute", compute);
            if (extra)   // more responses, summed in order: two-photon absorption and an extra Kerr term
                run.Set("overrides", A(O("target", "element:p", "property", "timeResponse", "mode", "add", "model", O("kind", "twoPhotonAbsorption", "beta", 1e-11)),
                                       O("target", "element:p", "property", "timeResponse", "mode", "add", "model", O("kind", "kerr", "n2", 5e-21))));
            return Simulation.Run(sys, Specs.RunSpec.FromJson(run));
        }

        [Test(Category = "gpu", Reference = "cross-check: the CPU solver (CompiledExpression.Evaluate, ExpressionTimeResponse, TwoPhotonAbsorptionResponse, ExtraKerrResponse)", Tolerance = "1e-09")]
        public void ExpressionResponses_CompiledToKernels_MatchTheCpu()
        {
            // backlog B3 kernel 3: user time responses used to keep the element on the CPU
            var devices = AllDevices();
            var cases = new (string name, JsonValue pol, JsonObject tv)[]
            {
                ("1-D scalar", "x", null),
                ("1-D vector", O("angleDeg", 25.0, "ellipticity", 0.3), null),
                ("radial", "x", O("model", "radial", "points", 24, "radiusM", 0.3e-3)),
                ("cartesian vector", O("angleDeg", 10.0, "ellipticity", 0.2), O("model", "cartesian", "nx", 16, "ny", 16, "halfWidthM", 0.3e-3)),
            };
            foreach (var (name, pol, tv) in cases)
            {
                var c = Responses(O("backend", "cpu"), pol, tv);
                var plain = Responses(O("backend", "cpu"), pol, tv, "0", false);
                Assert.Greater(Diff(plain, c), 1e-3, name + ": the responses change the field");
                foreach (var b in devices)
                {
                    var d = Responses(On(b), pol, tv);
                    string label = b.Name + " " + name;
                    Assert.True(OnDevice(d), label + " ran on the device: " + Notes(d));
                    Console.WriteLine("    " + label + ": rel. field error " + Diff(d, c).ToString("E2") + ", B " + c.Records[0].BIntegralRad.ToString("F2") + " rad");
                    Assert.Less(Diff(d, c), 1e-9, label + ": output field");
                    Assert.Close(c.Records[0].BIntegralRad, d.Records[0].BIntegralRad, 1e-9, 0, label + ": B-integral");
                    Assert.Close(c.Output.TotalEnergyJ(), d.Output.TotalEnergyJ(), 1e-9, 0, label + ": energy (two-photon and imaginary-part loss)");
                }
            }
        }

        [Test(Category = "gpu", Reference = "cross-check: the CPU solver (Crystals.Chi2Problem.Nonlinear, Steppers.AdaptiveErk43Ip with domain walls)", Tolerance = "1e-09")]
        public void Chi2CoupledWaves_ShgQpmAndOpa_MatchTheCpu()
        {
            // backlog B3 kernel 4: chi(2) crystals used to run on the CPU only
            var devices = AllDevices();
            JsonObject Ppln() => O("id", "x", "type", "crystal", "crystal", "MgO:LiNbO3", "lengthM", 1e-3, "temperatureC", 24.5,
                "interaction", O("process", "shg", "qpm", O("model", "domains")), "beam", O("kind", "gaussian", "w0M", 50e-6), "chi3", false, "output", "sh");
            JsonObject Gp(double lam, double fwhm, double e) => O("shape", "gaussian", "centerWavelengthM", lam, "durationFwhmS", fwhm, "energyJ", e);
            // Domain-resolved QPM: the adaptive stepper lands on every wall, and whether a step reaches a wall or leaves a sliver
            // before it depends on the last bits of the step size; at the default tolerance (1e-6) two CPU runs whose tolerances differ
            // by 1e-7 relative already differ by 3e-5 in the field. So the walls are checked with fixed steps (identical step
            // sequence) and with the adaptive stepper at tolerance 1e-10 (identical step sequence in practice; measured 1e-13).
            var cases = new (string name, JsonObject el, JsonObject pulse, JsonObject stepper)[]
            {
                ("BBO type-I SHG, chi(3), adaptive", O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 1e-3, "interaction", O("process", "shg", "type", "I"),
                    "beam", O("kind", "gaussian", "w0M", 100e-6), "output", "sh"), Gp(1030e-9, 200e-15, 20e-6), O("method", "adaptive")),
                ("BBO type-I SHG, chi(3), rk4ip", O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 1e-3, "interaction", O("process", "shg", "type", "I"),
                    "beam", O("kind", "gaussian", "w0M", 100e-6), "output", "sh"), Gp(1030e-9, 200e-15, 20e-6), O("method", "rk4ip")),
                ("PPLN SHG, resolved domains, fixed steps", Ppln(), Gp(1550e-9, 1e-12, 50e-9), O("method", "rk4ip")),
                ("PPLN SHG, resolved domains (walls), adaptive 1e-10", Ppln(), Gp(1550e-9, 1e-12, 50e-9), O("method", "adaptive", "tolerance", 1e-10)),
                ("BBO type-I OPA (three waves), chi(3)", O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 2e-3, "interaction", O("process", "opa", "type", "I"),
                    "beam", O("kind", "gaussian", "w0M", 150e-6), "aux", A(O("role", "pump", "pulse", Gp(515e-9, 300e-15, 20e-6), "beam", O("kind", "gaussian", "radiusM", 150e-6), "delayS", 0.0)),
                    "output", "signal"), Gp(800e-9, 300e-15, 1e-9), O("method", "adaptive")),
            };
            foreach (var (name, el, pulse, stepper) in cases)
            {
                RunResult R(JsonObject compute) => Simulation.Run(Systems.SystemDescription.FromJson(O("format", "ultrafast-system", "version", 2, "name", "chi2", "elements", A(el))),
                    Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", pulse, "stepper", stepper, "compute", compute)));
                var c = R(O("backend", "cpu"));
                double gain = c.Output.TotalEnergyJ() / c.Input.TotalEnergyJ();
                Assert.True(c.Records[0].Values.ContainsKey("conversionEfficiency") ? c.Records[0].Values["conversionEfficiency"] > 1e-4 : gain > 2, name + ": real chi(2) interaction");
                foreach (var b in devices)
                {
                    var d = R(On(b));
                    string label = b.Name + " " + name;
                    Assert.True(OnDevice(d), label + " ran on the device: " + Notes(d));
                    Console.WriteLine("    " + label + ": rel. field error " + Diff(d, c).ToString("E2") + ", energy out / in " + gain.ToString("G4"));
                    Assert.Less(Diff(d, c), 1e-9, label + ": output field");
                    Assert.Close(c.Output.TotalEnergyJ(), d.Output.TotalEnergyJ(), 1e-9, 0, label + ": energy");
                    Assert.Equal(c.Records[0].Steps.Accepted, d.Records[0].Steps.Accepted, label + ": same steps");
                }
            }
        }

        [Test(Category = "gpu")]
        public void CallbackResponses_StayOnTheCpu_WithTheReason()
        {
            var b = AllDevices()[0];
            var reg = Models.ModelRegistry.CreateDefault();
            reg.RegisterCallback("gpuTestKerr", (Models.TimeResponseCallback)((ctx, re, im) => { for (int i = 0; i < ctx.N; i++) re[i] = 1e-20 * ctx.Intensity[i]; }));
            var sys = Systems.SystemDescription.FromJson(O("format", "ultrafast-system", "version", 2, "name", "cb",
                "materials", O("kerr", O("index", O("kind", "constant", "value", 1.45), "n2", 2.5e-20)),
                "elements", A(O("id", "p", "type", "bulk", "material", "kerr", "lengthM", 5e-3, "beam", O("kind", "constant", "radiusM", 40e-6)))));
            var run = O("format", "ultrafast-run", "version", 2, "pulse", O("shape", "gaussian", "centerWavelengthM", 1030e-9, "durationFwhmS", 100e-15, "energyJ", 2e-6),
                "grid", O("auto", false, "n", 2048, "windowS", 2e-12), "compute", On(b));
            run.Set("overrides", A(O("target", "element:p", "property", "timeResponse", "mode", "add", "model", O("kind", "callback", "name", "gpuTestKerr"))));
            var r = Simulation.Run(sys, Specs.RunSpec.FromJson(run), reg);
            Assert.False(OnDevice(r), "a callback is user code: the element runs on the CPU");
            Assert.True(Notes(r).Contains("without a device form"), "the note says why: " + Notes(r));
        }

        [Test(Category = "gpu", Reference = "cross-check: the CPU solver (SpatialEnvelopeProblem.Nonlinear with the orientational response)", Tolerance = "1e-09")]
        public void GasRotationalRaman_Spatial_RadialAndCartesian_MatchTheCpu()
        {
            var devices = AllDevices();
            foreach (var (model, pol) in new (string, JsonValue)[] { ("radial", "x"), ("cartesian", O("angleDeg", 20.0, "ellipticity", 0.3)) })
            {
                RunResult R(JsonObject compute)
                {
                    var tv = model == "radial" ? O("model", "radial", "points", 24, "radiusM", 1.5e-3) : O("model", "cartesian", "nx", 16, "ny", 16, "halfWidthM", 1.5e-3);
                    var sys = Systems.SystemDescription.FromJson(O("format", "ultrafast-system", "version", 2, "name", "gas3d", "materials", O("g", O("gas", O("species", "N2", "pressureBar", 1.0))),
                        "elements", A(O("id", "cell", "type", "bulk", "material", "g", "lengthM", 0.3, "beam", O("kind", "gaussian", "w0M", 200e-6), "transverse", tv))));
                    var pulse = O("shape", "gaussian", "centerWavelengthM", 800e-9, "durationFwhmS", 150e-15, "energyJ", 300e-6);
                    pulse.Set("polarization", pol);
                    return Simulation.Run(sys, Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", pulse,
                        "grid", O("auto", false, "n", 1024, "windowS", 6e-12), "compute", compute)));
                }
                var c = R(O("backend", "cpu"));
                Assert.Greater(c.Records[0].BIntegralRad, 0.3, model + ": real nonlinearity");
                foreach (var b in devices)
                {
                    var d = R(On(b));
                    Assert.True(OnDevice(d), b.Name + " " + model + " ran on the device: " + Notes(d));
                    Console.WriteLine("    " + b.Name + " " + model + ": rel. field error " + Diff(d, c).ToString("E2") + ", B " + c.Records[0].BIntegralRad.ToString("F2") + " rad");
                    Assert.Less(Diff(d, c), 1e-9, b.Name + " " + model + ": output field");
                    Assert.Close(c.Records[0].BIntegralRad, d.Records[0].BIntegralRad, 1e-9, 0, b.Name + " " + model + ": B-integral");
                }
            }
        }

        [Test(Category = "gpu", Reference = "cross-check: the CPU solver", Tolerance = "1e-09")]
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

        [Test(Category = "gpu", Reference = "cross-check: the CPU solver")]
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

        [Test(Category = "gpu", Reference = "cross-check (C6 of the S2 roadmap): the S2 primitives -- a coherent tile source and a modulator -- feeding a nonlinear spatial plate give the CPU result on the device", Tolerance = "1e-9 relative")]
        public void TilesAndModulator_IntoANonlinearPlate_MatchTheCpu()
        {
            // the tile source (initialProfile) and the modulator (an active element) run on the host; the plate runs on the
            // device: the device must take the carried, modulated profile exactly. Two panels: an ideal one and a realistic one
            // (fill factor, gaps, cross-talk, 8-level quantisation with wrap, zero-order leak, rotation, fixed-phase chromatic
            // rule) with an elliptical (vector) pulse, Raman and self-steepening.
            var devices = AllDevices();
            JsonObject G(JsonObject ip = null) { var t = O("model", "cartesian", "nx", 128, "ny", 32, "halfWidthM", 1.6e-3, "halfWidthYM", 0.4e-3, "spectralSamples", 8); if (ip != null) t.Set("initialProfile", ip); return t; }
            var phase = new JsonArray();
            for (int i = 0; i < 32; i++) { var row = new JsonArray(); for (int j = 0; j < 8; j++) row.Add(0.5 * Math.Sin(i / 3.0) + 0.2 * j); phase.Add(row); }
            foreach (bool realistic in new[] { false, true })
            {
                var mod = O("id", "mod", "type", "modulator", "pitchXM", 100e-6, "pitchYM", 100e-6, "phaseRad", phase, "outsideTransmission", 1.0);
                if (realistic)
                    foreach (var kv in O("fillFactor", 0.9, "gapTransmission", 0.1, "crosstalkSigmaCells", 0.5, "quantisationLevels", 8, "phaseWrapRad", 2 * Math.PI,
                        "zeroOrderLeak", 0.05, "rotationRad", 0.1, "chromatic", "fixedPhase")) mod.Set(kv.Key, kv.Value);
                var els = new JsonArray(new JsonValue[]
                {
                    O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", G(O("kind", "tiles", "layout", "square", "countX", 3, "pitchM", 0.6e-3, "tileRadiusM", 0.2e-3, "pistonRad", A(0.0, 0.4, -0.3)))),
                    mod,
                    O("id", "k", "type", "bulk", "material", "kerr", "lengthM", 2e-3, "transverse", G())
                });
                var kerr = realistic ? O("index", O("kind", "constant", "value", 1.45), "n2", 3e-20, "raman", O("kind", "silica"), "ramanFraction", 0.18)
                                     : O("index", O("kind", "constant", "value", 1.45), "n2", 3e-20);
                var sys = UltrafastEngine.Systems.SystemDescription.FromJson(O("format", "ultrafast-system", "version", 2, "name", "c6", "materials", O("kerr", kerr), "elements", els));
                var pulse = O("shape", "gaussian", "centerWavelengthM", 1030e-9, "durationFwhmS", 100e-15, "energyJ", 2e-6);
                if (realistic) pulse.Set("polarization", O("angleDeg", 15.0, "ellipticity", 0.3));
                RunResult R(JsonObject compute) => Simulation.Run(sys, UltrafastEngine.Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", pulse,
                    "physics", O("raman", realistic, "selfSteepening", realistic), "compute", compute, "instruments", A(O("kind", "beamProfile", "name", "bp", "at", "end")))));
                var cpu = R(O("backend", "cpu"));
                string label0 = realistic ? "realistic panel, vector" : "ideal panel";
                foreach (var b in devices)
                {
                    var dev = R(On(b));
                    string label = b.Name + " " + label0;
                    Assert.True(OnDevice(dev), label + ": the plate ran on the device: " + Notes(dev));
                    Console.WriteLine("    " + label + ": rel. field error " + Diff(dev, cpu).ToString("E2") + ", B " + cpu.Records.Sum(r => r.BIntegralRad).ToString("F2") + " rad");
                    Assert.Less(Diff(dev, cpu), 1e-9, label + ": output field");
                    var fc = cpu.Instruments[0].Arrays.First(a => a.Name.EndsWith("fluence_map_j_m2")).Data; var fd = dev.Instruments[0].Arrays.First(a => a.Name.EndsWith("fluence_map_j_m2")).Data;
                    double peak = fc.Max(); for (int i = 0; i < fc.Length; i++) Assert.Close(fc[i], fd[i], 0, 1e-9 * peak, label + ": fluence map");
                }
            }
        }
    }
}
