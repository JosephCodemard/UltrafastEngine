// ValidityTests.cs -- structured validity findings (summary.json "validity", physics.strictValidity), the zero-frequency check,
// pulse- and beam-averaged B-integral (also from a stepper that does not report it, like the GPU steppers), and the half-maximum
// structure behind the FWHM (peak count, energy inside the FWHM, the main lobe: mainLobeFwhmS, mainLobeEnergyFraction).
// Prompted by a user's 46-pass multipass-cell report (docs/design/history.md, Stage 11).
using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Run validity and pulse metrics")]
    public class ValidityTests
    {
        private const double Lam = 1030e-9, N2 = 2.5e-20, Area = 1e-8;

        private static SystemDescription KerrPlates(int count, double lengthM)
        {
            var s = O("format", "ultrafast-system", "name", "kerr plates", "materials", O("flat", FlatMaterial(N2)));
            s.Set("elements", new JsonArray(Enumerable.Range(1, count).Select(i =>
                (JsonValue)O("id", "plate" + i, "type", "bulk", "material", "flat", "lengthM", lengthM, "beam", O("kind", "constant", "areaM2", Area)))));
            return SystemDescription.FromJson(s);
        }

        private static JsonObject Physics(bool strict = false) => O("physics", O("selfSteepening", false, "raman", false, "strictValidity", strict));

        [Test]
        public void CriticalPower_InEveryPass_GivesOneWarningNamingAllPasses_AndOneValidityEntryEach()
        {
            // 3e6 W in a constant 1e-8 m^2 area: P / P_cr ~ 0.7 in each plate (P_cr = 3.77 lambda^2 / (8 pi n0 n2) ~ 4.4 MW)
            var r = Go(KerrPlates(3, 1e-5), Spec(O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 200e-15, "peakPowerW", 3e6), Physics()));
            var v = r.Validity.Where(x => x.Code == "criticalPower").ToList();
            Assert.Equal(3, v.Count, "one validity entry per plate");
            Assert.True(v.Select(x => x.ElementId).SequenceEqual(new[] { "plate1", "plate2", "plate3" }), "entries name each plate in order");
            Assert.True(v.All(x => x.Value > x.Threshold), "value above threshold");
            var w = r.Warnings.Where(x => x.Contains("critical power")).ToList();
            Assert.Equal(1, w.Count, "one human warning for the check");
            Assert.True(w[0].Contains("plate2") && w[0].Contains("plate3") && w[0].Contains("3 elements"), "the warning names the later passes: " + w[0]);
            var json = r.SummaryJson();
            Assert.Equal(3, json.GetArray("validity").Count, "summary.json validity");
        }

        [Test]
        public void StrictValidity_RaisesAfterTheRun_WithTheResultAttached()
        {
            ValidityException ex = null;
            try { Go(KerrPlates(2, 1e-5), Spec(O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 200e-15, "peakPowerW", 3e6), Physics(strict: true))); }
            catch (ValidityException e) { ex = e; }
            Assert.True(ex != null, "strictValidity raises ValidityException");
            Assert.True(ex.Result != null && ex.Result.Validity.Count == 2, "result kept");
            Assert.True(ex.Message.Contains("critical power"), "message carries the warning");
            // below the threshold nothing is raised
            var ok = Go(KerrPlates(2, 1e-5), Spec(O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 200e-15, "peakPowerW", 1e5), Physics(strict: true)));
            Assert.Equal(0, ok.Validity.Count, "no findings at low power");
        }

        [Test(Reference = "Gaussian pulse and beam with an instantaneous Kerr index: power-weighted mean over peak = int P^2 / (int P max P) = 1/sqrt 2 in time, 1/2 over a Gaussian cross-section", Tolerance = "1e-6 (SPM leaves |A(t)| unchanged in a dispersionless medium)")]
        public void BIntegral_PulseAndBeamAveraged_AreTheAnalyticFractionsOfThePeak()
        {
            double gamma = N2 * (2 * Math.PI * 299792458.0 / Lam) / (299792458.0 * Area), L = 0.01, p0 = 0.5 / (gamma * L);
            var r = Go(KerrPlates(1, L), Spec(O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 100e-15, "peakPowerW", p0), Physics()));
            var rec = r.Records[0];
            Assert.Close(0.5, rec.BIntegralRad, 1e-3, 0, "peak B = gamma P0 L");
            Assert.Close(1 / Math.Sqrt(2), rec.BIntegralPulseAveragedRad / rec.BIntegralRad, 1e-6, 0, "pulse-averaged / peak");
            Assert.Close(0.5, rec.BIntegralBeamAveragedRad / rec.BIntegralPulseAveragedRad, 1e-12, 0, "beam-averaged / pulse-averaged");
            // the estimate used when a stepper does not report the mean (GPU): the same for an unchanged Gaussian
            Assert.Close(1 / Math.Sqrt(2), Executor.KerrShapeFactor(r.Output), 1e-6, 0, "shape-factor estimate");
            var tot = r.SummaryJson().GetObject("totals");
            Assert.Close(rec.BIntegralPulseAveragedRad, tot.GetDouble("bIntegralPulseAveragedRad", double.NaN), 1e-12, 0, "totals");
        }

        [Test]
        public void ZeroFrequency_IsReportedOnlyWhenTheSpectrumReachesIt()
        {
            // a grid whose lowest 64 bins are at omega <= 0 and blocked by the medium (amplitude 0)
            int n = 256; double dt = 1e-15;
            var g0 = FrequencyGrid.Create(n, dt, 1.0);
            double dw = g0.DOmega;
            var g = FrequencyGrid.Create(n, dt, 64 * dw);
            var amp = g.OmegaAbsolute.Select(w => w <= 0 ? 0.0 : 1.0).ToArray();
            Complex[] Spectrum(double widthBins) => g.OmegaAbsolute.Select(w => (Complex)Math.Exp(-0.5 * Math.Pow((w - g.OmegaCenter) / (widthBins * dw), 2))).ToArray();
            var zero = new Complex[n];
            Assert.True(!Executor.ReachedZeroFrequency(g, amp, Spectrum(4), zero), "a narrow spectrum (4 bins rms, 64 bins from zero) does not reach zero frequency");
            Assert.True(Executor.ReachedZeroFrequency(g, amp, Spectrum(30), zero), "a broad spectrum (30 bins rms) does");
            var open = amp.Select(_ => 1.0).ToArray();
            Assert.True(!Executor.ReachedZeroFrequency(g, open, Spectrum(30), zero), "no blocked bins: nothing to report");
        }

        [Test(Reference = "Gaussian: energy inside the FWHM = erf(sqrt(ln 2)) = 0.76100; sech^2: tanh(arcsech(1/sqrt 2)) = 1/sqrt 2", Tolerance = "1e-4 (sampling)")]
        public void HalfMaximumStructure_CountsSeparatePulses_AndTheEnergyInsideTheFwhm()
        {
            var t = Enumerable.Range(0, 4096).Select(i => (i - 2048) * 0.01).ToArray();
            var gauss = t.Select(x => Math.Exp(-4 * Math.Log(2) * x * x)).ToArray();   // FWHM 1
            var (c1, f1) = PulseMetrics.HalfMaximumStructure(gauss);
            Assert.Equal(1, c1, "single Gaussian");
            Assert.Close(0.76100, f1, 1e-4, 0, "Gaussian energy inside the FWHM");
            var sech2 = t.Select(x => Math.Pow(1 / Math.Cosh(x), 2)).ToArray();
            Assert.Close(1 / Math.Sqrt(2), PulseMetrics.HalfMaximumStructure(sech2).EnergyFraction, 1e-4, 0, "sech^2 energy inside the FWHM");
            // two pulses (the second at 0.6 of the first, 5 FWHM apart): two peaks; the FWHM spans both and holds most of the energy
            var two = t.Select(x => Math.Exp(-4 * Math.Log(2) * x * x) + 0.6 * Math.Exp(-4 * Math.Log(2) * (x - 5) * (x - 5))).ToArray();
            var (c2, _) = PulseMetrics.HalfMaximumStructure(two);
            Assert.Equal(2, c2, "double pulse");
            Assert.Close(5 + 0.5 + 0.5 * Math.Sqrt(Math.Log(0.6 / 0.5) / Math.Log(2)), PulseMetrics.Fwhm(t, two), 2e-3, 0, "FWHM from the outer crossings spans both pulses");
            // a satellite below half maximum is a single pulse
            var sat = t.Select(x => Math.Exp(-4 * Math.Log(2) * x * x) + 0.4 * Math.Exp(-4 * Math.Log(2) * (x - 5) * (x - 5))).ToArray();
            Assert.Equal(1, PulseMetrics.HalfMaximumStructure(sat).Count, "satellite at 0.4");
        }

        // ------------------------------------------------------------------ pulse-averaged B without a stepper report (E1)

        /// <summary>A plate-and-gap chain like a multipass-cell pass: FusedSilica plates and nonlinear air gaps (tracked Gaussian beam).</summary>
        internal static SystemDescription PlatesAndGaps(int plates = 3)
        {
            var els = new System.Collections.Generic.List<JsonObject>();
            for (int i = 1; i <= plates; i++)
            {
                els.Add(O("id", "plate" + i, "type", "plate", "material", "FusedSilica", "thicknessM", 5e-3));
                els.Add(O("id", "gap" + i, "type", "gap", "lengthM", 0.2, "nonlinear", true));
            }
            return Sys(els.ToArray());
        }

        /// <summary>The run of the E1 report: sech^2, 1030 nm, 160 fs, 30 uJ, tracked Gaussian beam w = 0.6 mm.</summary>
        internal static Specs.RunSpec PlatesAndGapsRun(JsonObject compute, bool raman = true) => Spec(O("shape", "sech2", "centerWavelengthM", Lam, "durationFwhmS", 160e-15, "energyJ", 30e-6),
            O("inputBeam", O("kind", "gaussian", "w0M", 0.6e-3), "compute", compute, "physics", O("raman", raman)));

        /// <summary>Stands for a device back-end: runs the CPU stepper but hands back only what the device steppers report (steps, peak B),
        /// not the power-weighted mean -- exactly what the CUDA / OpenCL steppers do.</summary>
        private sealed class UnreportedMeanBackend : IComputeBackend
        {
            public const string Id = "test-unreported-mean";
            public string Name => Id;
            public string Description => "test: CPU stepper without the pulse-averaged B report";
            public bool Supports(IPropagationProblem problem) => true;
            public Complex[] Propagate(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress)
            {
                var own = new StepStatistics();
                var r = Steppers.PropagateCpu(p, a0, st, own, progress);
                stats.Method = own.Method; stats.Accepted = own.Accepted; stats.Rejected = own.Rejected; stats.BIntegralRad = own.BIntegralRad;
                stats.MinStepM = own.MinStepM; stats.MaxStepM = own.MaxStepM;
                return r;
            }
        }

        [Test(Reference = "CPU stepper (power-weighted integral) of the same run; the estimate from entry and exit shapes is exact for an unchanged shape and an instantaneous Kerr response", Tolerance = "1 % without Raman (the shape changes a little: B ~ 0.2 rad per plate); 15 % with the delayed (rotational) Raman response of the air gaps")]
        public void BIntegral_PulseAveraged_FromAStepperThatDoesNotReportIt_IsEstimatedNotZero()
        {
            // regression E1: the GPU steppers do not accumulate the power-weighted B; StepStatistics defaulted to 0, which the executor took
            // as a measured value, so summary totals and every element reported bIntegralPulseAveragedRad = bIntegralBeamAveragedRad = 0
            Assert.True(double.IsNaN(new StepStatistics().BIntegralPulseAveragedRad), "a fresh StepStatistics says 'not reported' (NaN)");
            ComputeBackends.Register(new UnreportedMeanBackend());
            foreach (bool raman in new[] { false, true })
            {
                var cpu = Go(PlatesAndGaps(), PlatesAndGapsRun(O("backend", "cpu"), raman));
                var dev = Go(PlatesAndGaps(), PlatesAndGapsRun(O("backend", UnreportedMeanBackend.Id), raman));
                Assert.True(dev.Records.Any(r => r.Steps != null && r.Steps.Notes.Any(n => n.Contains(UnreportedMeanBackend.Id))), "the test back-end ran");
                Assert.True(dev.Records.Where(r => r.BIntegralRad > 0).All(r => r.Notes.Any(n => n.StartsWith("pulse-averaged B-integral estimated"))), "estimated, and says so");
                // the estimate assumes an instantaneous response: the delayed rotational Raman response of air shifts the weighting (~8 % in the gaps)
                CompareAveragedB(cpu, dev, raman ? 0.15 : 1e-2);
            }
        }

        /// <summary>Pulse- and beam-averaged B of two runs element by element and in the totals (GpuTests uses it for the real device).</summary>
        internal static void CompareAveragedB(RunResult cpu, RunResult other, double tol)
        {
            int nonlinear = 0;
            foreach (var rc in cpu.Records.Where(r => r.BIntegralRad > 0))
            {
                var ro = other.Records.First(r => r.Id == rc.Id);
                nonlinear++;
                Assert.Greater(ro.BIntegralPulseAveragedRad, 0, rc.Id + ": pulse-averaged B reported (not 0)");
                Assert.Close(rc.BIntegralRad, ro.BIntegralRad, 1e-6, 0, rc.Id + ": peak B");
                Assert.Close(rc.BIntegralPulseAveragedRad, ro.BIntegralPulseAveragedRad, tol, 0, rc.Id + ": pulse-averaged B");
                Assert.Close(rc.BIntegralBeamAveragedRad, ro.BIntegralBeamAveragedRad, tol, 0, rc.Id + ": beam-averaged B");
            }
            Assert.Equal(6, nonlinear, "three plates and three nonlinear gaps");
            var tc = cpu.SummaryJson().GetObject("totals"); var to = other.SummaryJson().GetObject("totals");
            Assert.Greater(tc.GetDouble("bIntegralPulseAveragedRad", double.NaN), 0.3, "the chain is nonlinear (CPU total)");
            foreach (var k in new[] { "bIntegralPulseAveragedRad", "bIntegralBeamAveragedRad" })
                Assert.Close(tc.GetDouble(k, double.NaN), to.GetDouble(k, double.NaN), tol, 0, "totals " + k);
        }

        [Test(Reference = "sech^2 pulse, instantaneous Kerr: int sech^4 / int sech^2 = (4/3) / 2 = 2/3 of the peak B in time; Gaussian beam: 1/2 of that over the cross-section (int I^2 dA / (I0 int I dA))", Tolerance = "1e-4 (sampling of the pulse; SPM leaves |A(t)| unchanged without dispersion)")]
        public void BIntegral_PulseAveraged_OfASech2Pulse_IsTwoThirdsOfThePeak_AlsoWhenEstimated()
        {
            // P(t) = P0 sech^2(t/T): int P^2 dt / (P0 int P dt) = int sech^4 x dx / int sech^2 x dx = (4/3) / 2 = 2/3
            double gamma = N2 * (2 * Math.PI * 299792458.0 / Lam) / (299792458.0 * Area), L = 0.01, p0 = 0.5 / (gamma * L);
            ComputeBackends.Register(new UnreportedMeanBackend());
            foreach (var backend in new[] { "cpu", UnreportedMeanBackend.Id })
            {
                var r = Go(KerrPlates(1, L), Spec(O("shape", "sech2", "centerWavelengthM", Lam, "durationFwhmS", 100e-15, "peakPowerW", p0),
                    O("physics", O("selfSteepening", false, "raman", false), "compute", O("backend", backend))));
                var rec = r.Records[0];
                Assert.Close(0.5, rec.BIntegralRad, 1e-3, 0, backend + ": peak B = gamma P0 L");
                Assert.Close(2.0 / 3.0, rec.BIntegralPulseAveragedRad / rec.BIntegralRad, 1e-4, 0, backend + ": pulse-averaged / peak = 2/3");
                Assert.Close(1.0 / 3.0, rec.BIntegralBeamAveragedRad / rec.BIntegralRad, 1e-4, 0, backend + ": beam-averaged / peak = 1/3");
            }
        }

        // ------------------------------------------------------------------ main lobe of a multi-peaked pulse

        [Test(Reference = "Gaussian P = exp(-4 ln2 t^2 / T^2): main lobe = FWHM T; 5 % points at t = T sqrt(ln 20 / (4 ln 2)), energy between them erf(sqrt(ln 20)) = 0.985625", Tolerance = "1e-4 (sampling)")]
        public void MainLobe_OfAGaussian_IsTheFwhm_AndHoldsErfOfSqrtLn20()
        {
            var t = Enumerable.Range(0, 4096).Select(i => (i - 2048) * 0.01).ToArray();
            var gauss = t.Select(x => Math.Exp(-4 * Math.Log(2) * x * x)).ToArray();   // FWHM 1
            var (fw, frac) = PulseMetrics.MainLobe(t, gauss);
            Assert.Close(PulseMetrics.Fwhm(t, gauss), fw, 1e-12, 0, "main-lobe FWHM = FWHM for a single pulse");
            Assert.Close(1.0, fw, 1e-4, 0, "main-lobe FWHM");
            Assert.Close(0.985625, frac, 1e-4, 0, "energy between the 5 % points = erf(sqrt(ln 20))");
        }

        [Test(Reference = "Gaussian FWHM 1 centred on the last sample: the half of the lobe inside the window is 0.5 wide (lower bound), flagged; a centred one is not flagged", Tolerance = "1e-3 (sampling)")]
        public void MainLobe_TouchingTheWindowEdge_IsFiniteAndFlagged()
        {
            var t = Enumerable.Range(0, 2048).Select(i => (i - 1024) * 0.01).ToArray();
            var g = t.Select(x => Math.Exp(-4 * Math.Log(2) * x * x)).ToArray();
            PulseMetrics.MainLobe(t, g, out bool centred);
            Assert.True(!centred, "centred pulse is not at the edge");
            var edge = t.Select(x => Math.Exp(-4 * Math.Log(2) * (x - t[t.Length - 1]) * (x - t[t.Length - 1]))).ToArray();
            var (fw, frac) = PulseMetrics.MainLobe(t, edge, out bool at);
            Assert.True(at, "lobe at the window edge is flagged");
            Assert.True(!double.IsNaN(fw) && !double.IsNaN(frac), "finite width and fraction");
            Assert.Close(0.5, fw, 2e-3, 0, "width to the window edge = half the FWHM");
            var left = t.Select(x => Math.Exp(-4 * Math.Log(2) * (x - t[0]) * (x - t[0]))).ToArray();
            Assert.Close(0.5, PulseMetrics.MainLobe(t, left, out bool atL).Fwhm, 2e-3, 0, "left edge");
            Assert.True(atL, "left edge flagged");
        }

        [Test(Reference = "two Gaussians of FWHM T, 5 T apart, the second at 0.6: main lobe = T and holds erf(sqrt(ln 20)) / 1.6 of the energy; fwhmS spans both: 5 T + T/2 + (T/2) sqrt(log2(0.6/0.5))", Tolerance = "1e-4 ... 2e-3 (sampling)")]
        public void MainLobe_OfADoublePulse_IsTheSinglePulseWidth_WhileTheFwhmSpansBoth()
        {
            var t = Enumerable.Range(0, 4096).Select(i => (i - 2048) * 0.01).ToArray();
            var two = t.Select(x => Math.Exp(-4 * Math.Log(2) * x * x) + 0.6 * Math.Exp(-4 * Math.Log(2) * (x - 5) * (x - 5))).ToArray();
            Assert.Close(5 + 0.5 + 0.5 * Math.Sqrt(Math.Log(0.6 / 0.5) / Math.Log(2)), PulseMetrics.Fwhm(t, two), 2e-3, 0, "fwhmS spans both pulses");
            var (fw, frac) = PulseMetrics.MainLobe(t, two);
            Assert.Close(1.0, fw, 1e-4, 0, "main-lobe FWHM = the single-pulse FWHM");
            Assert.Close(0.985625 / 1.6, frac, 1e-4, 0, "main-lobe energy fraction = erf(sqrt(ln 20)) x 1 / (1 + 0.6)");
            // the higher pulse defines the main lobe, wherever it is
            var swapped = t.Select(x => 0.6 * Math.Exp(-4 * Math.Log(2) * x * x) + Math.Exp(-4 * Math.Log(2) * (x - 5) * (x - 5) / (0.5 * 0.5))).ToArray();
            Assert.Close(0.5, PulseMetrics.MainLobe(t, swapped).Fwhm, 1e-3, 0, "main lobe = the narrower, higher pulse");
            // overlapping lobes: the energy window ends at the minimum between them (above 5 %), so the satellite stays outside
            var close = t.Select(x => Math.Exp(-4 * Math.Log(2) * x * x) + 0.6 * Math.Exp(-4 * Math.Log(2) * (x - 1.5) * (x - 1.5))).ToArray();
            int imin = Enumerable.Range(2048, 150).OrderBy(i => close[i]).First();
            Assert.True(close[imin] > 0.05 && close[imin] < 0.5, "the minimum between the lobes lies between 5 % and 50 %");
            double floor = 0.05 * close.Max();
            int lo5 = Enumerable.Range(0, 2048).First(i => close[i] > floor);   // left 5 %-of-peak point (the left wing is a plain Gaussian)
            double a = lo5 - 1 + (floor - close[lo5 - 1]) / (close[lo5] - close[lo5 - 1]), inside = 0, tot = 0;
            for (int i = 0; i < t.Length - 1; i++) { double s = 0.5 * (close[i] + close[i + 1]); tot += s; if (i >= lo5 && i < imin) inside += s; }
            inside += 0.5 * (floor + close[lo5]) * (lo5 - a);
            Assert.Close(inside / tot, PulseMetrics.MainLobe(t, close).EnergyFraction, 1e-9, 0, "window from the 5 % point to the minimum");
            Assert.Close(1.0, PulseMetrics.MainLobe(t, close).Fwhm, 0.05, 0, "main-lobe FWHM close to the single pulse's");
        }

        [Test]
        public void MainLobe_IsReportedForEveryPulse_InTheSummary_AndByPulseSummary()
        {
            // a single Gaussian through a dispersionless plate: main-lobe width = fwhmS, satellitePeakCount 0
            var r = Go(KerrPlates(1, 1e-3), Spec(O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 100e-15, "peakPowerW", 1.0),
                O("physics", O("selfSteepening", false, "raman", false), "instruments", A(O("kind", "pulseSummary", "name", "ps", "at", "end")))));
            var js = r.SummaryJson();
            foreach (var o in new[] { js.GetObject("output"), (JsonObject)js.GetArray("elements")[0], })
            {
                var q = o.GetObject("pulse") ?? o;
                Assert.Close(q.GetDouble("fwhmS", double.NaN), q.GetDouble("mainLobeFwhmS", double.NaN), 1e-12, 0, "mainLobeFwhmS = fwhmS");
                Assert.Close(0.985625, q.GetDouble("mainLobeEnergyFraction", double.NaN), 2e-3, 0, "mainLobeEnergyFraction");
                Assert.Equal(0, q.GetInt("satellitePeakCount", -1), "satellitePeakCount");
            }
            var ps = r.Instruments.First(i => i.Name == "ps").Values;
            Assert.Close(js.GetObject("output").GetDouble("mainLobeFwhmS", double.NaN), ps.GetDouble("mainLobeFwhmS", double.NaN), 1e-12, 0, "pulseSummary mainLobeFwhmS");
            Assert.Close(js.GetObject("output").GetDouble("mainLobeEnergyFraction", double.NaN), ps.GetDouble("mainLobeEnergyFraction", double.NaN), 1e-12, 0, "pulseSummary mainLobeEnergyFraction");
            Assert.Equal(1, ps.GetInt("peakCount", -1), "pulseSummary peakCount");
        }
    }
}
