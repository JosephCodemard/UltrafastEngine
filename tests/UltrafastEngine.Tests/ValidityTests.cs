// ValidityTests.cs -- structured validity findings (summary.json "validity", physics.strictValidity), the zero-frequency check,
// pulse- and beam-averaged B-integral, and the half-maximum structure behind the FWHM (peak count, energy inside the FWHM).
// Prompted by a user's 46-pass multipass-cell report (docs/design/history.md, Stage 11).
using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
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
    }
}
