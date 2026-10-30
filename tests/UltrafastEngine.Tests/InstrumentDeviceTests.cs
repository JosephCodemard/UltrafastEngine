using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Instruments;
using UltrafastEngine.Results;
using UltrafastEngine.Schema;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Device parameters of instruments: normalisation, resolution, SHG acceptance, detector read-out, filters,
    /// and the settings block every instrument output carries.</summary>
    [TestArea("Instrument device models: resolution, acceptance, detectors, filters")]
    public class InstrumentDeviceTests
    {
        private const double Lam = 1030e-9;

        private static RunResult Measure(JsonObject pulse, params JsonObject[] instruments)
        {
            var sys = Sys(O("id", "g", "type", "gap", "lengthM", 0.01));
            return Go(sys, Spec(pulse, O("instruments", new JsonArray(instruments))));
        }

        private static InstrumentOutput Inst(RunResult r, string name) => r.Instruments.First(i => i.Name == name);
        private static double[] Arr(InstrumentOutput o, string suffix) => o.Arrays.First(a => a.Name == o.Name + "_" + suffix).Data;
        private static double V(InstrumentOutput o, string key) => o.Values[key].AsDouble();

        [Test]
        public void Spectrometer_NormalizeModes_IncludingLegacyBooleans()
        {
            double e = 2e-6;
            var r = Measure(Gauss(Lam, 200e-15, e),
                O("kind", "spectrometer", "name", "peak"),
                O("kind", "spectrometer", "name", "area", "normalize", "area"),
                O("kind", "spectrometer", "name", "none", "normalize", "none"),
                O("kind", "spectrometer", "name", "btrue", "normalize", true),
                O("kind", "spectrometer", "name", "bfalse", "normalize", false));
            Assert.True(r.Warnings.Count == 0, "no warnings: " + string.Join("; ", r.Warnings));
            double Integral(InstrumentOutput o) { var x = Arr(o, "wavelength_m"); var y = Arr(o, "spectrum"); double s = 0; for (int i = 1; i < x.Length; i++) s += 0.5 * (y[i] + y[i - 1]) * (x[i] - x[i - 1]); return s; }
            Assert.Close(1.0, Arr(Inst(r, "peak"), "spectrum").Max(), 1e-12, 0, "peak = 1 (default)");
            Assert.Close(1.0, Integral(Inst(r, "area")), 1e-9, 0, "area = 1");
            Assert.Close(e, Integral(Inst(r, "none")), 1e-3, 0, "none: integral = pulse energy (J/m)");
            Assert.Close(1.0, Arr(Inst(r, "btrue"), "spectrum").Max(), 1e-12, 0, "true = peak");
            Assert.Close(e, Integral(Inst(r, "bfalse")), 1e-3, 0, "false = none");
            Assert.True(Inst(r, "peak").Values.GetString("normalize") == "peak", "mode reported");
            // the schema accepts the legacy boolean and the validator does not flag it
            var issues = Validator.CheckRun(Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", Gauss(Lam, 200e-15, e),
                "instruments", A(O("kind", "spectrometer", "normalize", false), O("kind", "spectrometer", "normalize", "area")))));
            Assert.True(issues.Count == 0, "validator: " + string.Join("; ", issues));
            var bad = Validator.CheckRun(Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", Gauss(Lam, 200e-15, e),
                "instruments", A(O("kind", "spectrometer", "normalize", "max2")))));
            Assert.True(bad.Count == 1, "an unknown mode is reported");
        }

        [Test]
        public void Settings_ListEveryParameterWithItsValueOrDefault()
        {
            var r = Measure(Gauss(Lam, 200e-15, 1e-6), O("kind", "spectrometer", "name", "s", "resolutionM", 0.2e-9), O("kind", "frog", "name", "f"));
            var s = Inst(r, "s").Settings;
            Assert.Close(0.2e-9, s["resolutionM"].AsDouble(), 0, 0, "given value");
            Assert.Close(2048, s["points"].AsDouble(), 0, 0, "default value");
            Assert.True(s.GetString("normalize") == "peak", "enum default");
            var f = Inst(r, "f").Settings;
            Assert.Close(256, f["points"].AsDouble(), 0, 0, "frog points default");
            Assert.True(Inst(r, "f").ToJson().Has("settings"), "written to summary.json");
        }

        [Test(Reference = "SH bandwidth of a 60 fs pulse (~4.4 nm)", Tolerance = "3 %, 5 %")]
        public void Frog_SpectralResolutionAndAcceptance_NarrowAndSmoothTheTrace()
        {
            // a 60 fs pulse at 1030 nm: SH bandwidth ~ 4.4 nm; a 3 nm acceptance clearly narrows the frequency marginal
            var r = Measure(Gauss(Lam, 60e-15, 1e-9),
                O("kind", "frog", "name", "ideal"),
                O("kind", "frog", "name", "acc", "shgAcceptanceFwhmM", 3e-9),
                O("kind", "frog", "name", "res", "spectralResolutionM", 3e-9));
            double Width(InstrumentOutput o) { var w = Arr(o, "omega_rad_s"); var m = Arr(o, "frequency_marginal"); return PulseMetrics.Fwhm(w, m); }
            double w0 = Width(Inst(r, "ideal")), wa = Width(Inst(r, "acc")), wr = Width(Inst(r, "res"));
            double tl = 2 * Math.PI * 0.4413 / 60e-15 * Math.Sqrt(2);
            Assert.Close(tl, w0, 0.03, 0, "ideal SHG-FROG frequency marginal: sqrt2 x the fundamental bandwidth");
            // Gaussians: 1/wa^2 = 1/w0^2 + 1/acc^2 (product), wr^2 = w0^2 + res^2 (convolution)
            double lsh = Lam / 2, acc = 2 * Math.PI * C * 3e-9 / (lsh * lsh), res = 2 * Math.PI * C * 3e-9 / (lsh * lsh);
            Assert.Close(1 / Math.Sqrt(1 / (w0 * w0) + 1 / (acc * acc)), wa, 0.03, 0, "acceptance narrows the SH spectrum");
            Assert.Close(Math.Sqrt(w0 * w0 + res * res), wr, 0.05, 0, "resolution broadens it");
        }

        [Test]
        public void Autocorrelator_ResolutionAndAcceptance()
        {
            var chirped = Gauss(Lam, 30e-15, 1e-9); chirped.Set("chirp", O("gddFs2", 600));
            var r = Measure(Gauss(Lam, 30e-15, 1e-9),
                O("kind", "autocorrelator", "name", "ideal"),
                O("kind", "autocorrelator", "name", "res", "resolutionS", 40e-15),
                O("kind", "autocorrelator", "name", "acc", "shgAcceptanceFwhmM", 0.5e-9));
            var rc = Measure(chirped, O("kind", "autocorrelator", "name", "ideal"), O("kind", "autocorrelator", "name", "acc", "shgAcceptanceFwhmM", 0.5e-9));
            double a0 = V(Inst(r, "ideal"), "acFwhmS"), ar = V(Inst(r, "res"), "acFwhmS"), aa = V(Inst(r, "acc"), "acFwhmS");
            Assert.Close(30e-15 * Math.Sqrt(2), a0, 0.02, 0, "ideal AC of a Gaussian");
            Assert.Close(Math.Sqrt(a0 * a0 + 40e-15 * 40e-15), ar, 0.03, 0, "delay resolution adds in quadrature");
            // a narrow acceptance keeps only the sum frequency 2 w0: S(tau) -> |int E(t) E(t - tau) dt|^2, which for a
            // Gaussian with linear chirp equals the intensity AC exactly (the AC shape is unchanged, only the signal
            // drops) -- the acceptance is checked on the FROG trace, where it narrows the frequency marginal
            Assert.Close(a0, aa, 0.03, 0, "transform-limited pulse: unchanged");
            double c0 = V(Inst(rc, "ideal"), "acFwhmS"), ca = V(Inst(rc, "acc"), "acFwhmS");
            Assert.Close(c0, ca, 0.03, 0, "linearly chirped Gaussian: unchanged as well");
            Assert.Close(0.5e-9, V(Inst(rc, "acc"), "shgAcceptanceFwhmM"), 0, 0, "acceptance reported");
        }

        [Test]
        public void Detector_CalibrationNoiseFloorAndBits_AreReproducible()
        {
            var det = O("calibration", 1.05, "noise", 0.02, "noiseFloor", 1e-3, "bitDepth", 10, "seed", 7);
            var r = Measure(Gauss(Lam, 200e-15, 1e-6),
                O("kind", "energyMeter", "name", "em", "detector", O("calibration", 1.03)),
                O("kind", "spectrometer", "name", "s0"),
                O("kind", "spectrometer", "name", "s1", "detector", det),
                O("kind", "spectrometer", "name", "s2", "detector", det));
            Assert.Close(1.03 * 1e-6, V(Inst(r, "em"), "energyJ"), 0.005, 0, "calibration factor on a reading");
            var y0 = Arr(Inst(r, "s0"), "spectrum"); var y1 = Arr(Inst(r, "s1"), "spectrum"); var y2 = Arr(Inst(r, "s2"), "spectrum");
            Assert.True(y1.SequenceEqual(y2), "same seed: identical noise");
            Assert.False(y1.SequenceEqual(y0.Select(v => 1.05 * v)), "noise was added");
            var levels = y1.Select(v => Math.Round(v / y1.Max() * 1023, 6)).ToArray();
            Assert.True(levels.All(v => Math.Abs(v - Math.Round(v)) < 1e-6), "10-bit quantisation");
            Assert.Close(V(Inst(r, "s0"), "fwhmM"), V(Inst(r, "s1"), "fwhmM"), 0.05, 0, "FWHM re-evaluated on the detected trace, still close");
            Assert.True(Inst(r, "s1").Notes.Any(n => n.StartsWith("detector:")), "the read-out is noted");
        }

        [Test]
        public void Filter_InFrontOfTheInstrument_ActsOnTheMeasuredField()
        {
            var r = Measure(Gauss(Lam, 100e-15, 1e-6),
                O("kind", "energyMeter", "name", "all"),
                O("kind", "energyMeter", "name", "nd", "filter", O("kind", "constant", "value", 0.1)),
                O("kind", "spectrometer", "name", "bp", "filter", O("kind", "gaussian", "centerWavelengthM", 1035e-9, "fwhmM", 4e-9)));
            Assert.Close(0.1 * V(Inst(r, "all"), "energyJ"), V(Inst(r, "nd"), "energyJ"), 1e-9, 0, "ND filter in front of the meter");
            Assert.Close(1035e-9, V(Inst(r, "bp"), "peakWavelengthM"), 2e-4, 0, "band-pass moves the measured peak");
            Assert.Close(1e-6, r.Output.TotalEnergyJ(), 1e-6, 0, "the pulse itself is unchanged");
        }

        [Test]
        public void Polarimeter_ExtinctionRatioScalesStokes()
        {
            var r = Measure(Gauss(Lam, 100e-15, 1e-6), O("kind", "polarimeter", "name", "p0"), O("kind", "polarimeter", "name", "p1", "extinctionRatio", 0.01));
            Assert.Close(1.0, V(Inst(r, "p0"), "s1"), 1e-9, 0, "x polarised");
            Assert.Close(0.99 / 1.01, V(Inst(r, "p1"), "s1"), 1e-9, 0, "(1 - e) / (1 + e)");
        }

        [Test]
        public void EveryInstrumentEnum_RunsWithEachValue()
        {
            // every enum value declared for an instrument parameter is accepted by the instrument without warnings
            var kinds = new[] { "spectrometer", "autocorrelator", "frog" };
            foreach (var k in kinds)
            {
                var spec = OptionRegistry.Get("instrument", k);
                foreach (var p in spec.AllParams().Where(p => p.Enum != null))
                    foreach (var v in p.Enum)
                    {
                        var r = Measure(Gauss(Lam, 100e-15, 1e-9), O("kind", k, "name", "x", p.Name, v));
                        Assert.True(r.Warnings.Count == 0 && r.Instruments.Count == 1, k + "." + p.Name + " = " + v + ": " + string.Join("; ", r.Warnings));
                    }
            }
        }
    }
}
