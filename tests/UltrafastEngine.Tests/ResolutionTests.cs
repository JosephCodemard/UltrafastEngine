using System;
using System.IO;
using System.Linq;
using UltrafastEngine.Instruments;
using UltrafastEngine.Measured;
using UltrafastEngine.Plugins.Fitting;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Finite resolution and sampling of measurements: the common instrument "axis", measured input spectra
    /// (resolution, deconvolution, extrapolation) and the resolution check of recorded data.</summary>
    [TestArea("Instrument axes, resolution and measured-data resolution")]
    public class ResolutionTests
    {
        private const double Lam = 1030e-9;

        private static RunResult Measure(JsonObject pulse, params JsonObject[] instruments) => Go(Sys(), Spec(pulse, O("instruments", new JsonArray(instruments))));
        private static InstrumentOutput Inst(RunResult r, string name) => r.Instruments.First(i => i.Name == name);
        private static double[] Arr(InstrumentOutput o, string suffix) => o.Arrays.First(a => a.Name == o.Name + "_" + suffix).Data;

        [Test(Category = "instruments")]
        public void Axis_ExplicitPixels_WithPixelBins()
        {
            var px = Enumerable.Range(0, 50).Select(i => 1020e-9 + i * 0.4e-9).ToArray();
            var r = Measure(Gauss(Lam, 300e-15, 1e-6), O("kind", "spectrometer", "name", "s", "axis", O("values", px, "binWidth", "pixel")));
            var s = Inst(r, "s");
            Assert.Equal(50, Arr(s, "wavelength_m").Length, "one reading per pixel");
            Assert.Equal(50, Arr(s, "spectrum").Length, "spectrum on the pixels");
            Assert.Close(Lam, s.Values.GetDouble("peakWavelengthM", 0), 3e-4, 0, "peak re-evaluated on the pixels");
            Assert.True(s.Notes.Any(n => n.StartsWith("axis: 50 readings")), "described");
            Assert.True(s.Settings.Has("axis"), "settings echo the axis");
        }

        [Test(Category = "instruments")]
        public void Axis_Resolution_EqualsTheSpectrometerResolution()
        {
            var r = Measure(Gauss(Lam, 300e-15, 1e-6),
                O("kind", "spectrometer", "name", "a", "resolutionM", 3e-9),
                O("kind", "spectrometer", "name", "b", "axis", O("from", 1010e-9, "to", 1050e-9, "points", 801, "resolution", 3e-9)));
            Assert.Close(Inst(r, "a").Values.GetDouble("fwhmM", 0), Inst(r, "b").Values.GetDouble("fwhmM", 1), 0.01, 0, "same blur either way");
        }

        [Test(Category = "instruments")]
        public void Axis_CoarseDelaySteps_OnTheAutocorrelator_AndOutsideReadsZero()
        {
            var r = Measure(Gauss(Lam, 200e-15, 1e-6), O("kind", "autocorrelator", "name", "ac", "axis", O("from", -2e-12, "to", 2e-12, "step", 20e-15)));
            var ac = Inst(r, "ac");
            Assert.Equal(201, Arr(ac, "delay_s").Length, "201 delay steps");
            Assert.Close(200e-15 * Math.Sqrt(2), ac.Values.GetDouble("acFwhmS", 0), 0.03, 0, "AC FWHM from the coarse scan");
            var r2 = Measure(Gauss(Lam, 200e-15, 1e-6), O("kind", "spectrometer", "name", "s", "axis", O("values", new[] { 900e-9, 1030e-9 })));
            Assert.Equal(0.0, Arr(Inst(r2, "s"), "spectrum")[0], "no light far outside the spectrum");
        }

        [Test(Category = "instruments")]
        public void Axis_DbArraysFollowTheirLinearCurves()
        {
            var r = Measure(Gauss(Lam, 300e-15, 1e-6), O("kind", "crossCorrelator", "name", "cc", "noise", false, "axis", O("from", -3e-12, "to", 3e-12, "step", 50e-15, "binWidth", "pixel")));
            var cc = Inst(r, "cc"); var lin = Arr(cc, "signal"); var db = Arr(cc, "signal_db");
            double mx = lin.Max();
            for (int i = 0; i < lin.Length; i += 13) Assert.Close(10 * Math.Log10(Math.Max(lin[i] / mx, 1e-30)), db[i], 1e-9, 1e-9, "dB recomputed from the binned signal");
        }

        private static JsonObject MeasuredGaussianSpectrum(double fwhmNm, double resolutionNm, double halfRangeNm)
        {
            // a Gaussian spectrum of FWHM fwhm, recorded with a Gaussian resolution: FWHM sqrt(fwhm^2 + res^2)
            double rec = Math.Sqrt(fwhmNm * fwhmNm + resolutionNm * resolutionNm);
            var l = Enumerable.Range(0, 801).Select(i => 1030 - halfRangeNm + 2 * halfRangeNm * i / 800.0).ToArray();
            var y = l.Select(v => Math.Exp(-4 * Math.Log(2) * Math.Pow((v - 1030) / rec, 2))).ToArray();
            return O("wavelengthsNm", l, "intensity", y);
        }

        [Test(Category = "instruments")]
        public void InputSpectrum_Deconvolution_RemovesTheRecordingResolution()
        {
            var sp = MeasuredGaussianSpectrum(2.0, 2.0, 15);
            sp.Set("resolutionNm", 2.0); sp.Set("deconvolve", true); sp.Set("deconvolveIterations", 200);
            var r = Go(Sys(), Spec(O("shape", "spectrum", "energyJ", 1e-6, "spectrum", sp)));
            double fw = PulseMetrics.Summarize(r.Input).SpectralFwhmM;
            Assert.Close(2e-9, fw, 0.06, 0, "the true 2 nm FWHM is recovered from the 2.83 nm recording");
            var plain = MeasuredGaussianSpectrum(2.0, 2.0, 15);
            var r0 = Go(Sys(), Spec(O("shape", "spectrum", "energyJ", 1e-6, "spectrum", plain)));
            Assert.Close(Math.Sqrt(8) * 1e-9, PulseMetrics.Summarize(r0.Input).SpectralFwhmM, 0.02, 0, "without deconvolution the recording is used as given");
        }

        [Test(Category = "instruments")]
        public void InputSpectrum_GaussianTails_ReplaceTheTruncatedWings()
        {
            var cut = MeasuredGaussianSpectrum(10.0, 0, 6);   // only the central +-6 nm of a 10 nm FWHM spectrum
            var rz = Go(Sys(), Spec(O("shape", "spectrum", "energyJ", 1e-6, "spectrum", cut)));
            var tails = MeasuredGaussianSpectrum(10.0, 0, 6); tails.Set("extrapolate", "gaussian");
            var rg = Go(Sys(), Spec(O("shape", "spectrum", "energyJ", 1e-6, "spectrum", tails)));
            var full = Go(Sys(), Spec(O("shape", "spectrum", "energyJ", 1e-6, "spectrum", MeasuredGaussianSpectrum(10.0, 0, 40))));
            double f = full.Input.PeakPowerW();
            Assert.Close(f, rg.Input.PeakPowerW(), 0.03, 0, "Gaussian tails restore the full pulse");
            Assert.Greater(Math.Abs(rz.Input.PeakPowerW() - f) / f, 0.05, "zero outside the data changes the pulse noticeably");
        }

        [Test(Category = "instruments")]
        public void MeasuredData_ResolutionMismatch_IsReported()
        {
            var dir = Path.Combine(Path.GetTempPath(), "uf-res-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "s.csv"), "wavelength_nm,intensity\n1020,0.1\n1030,1\n1040,0.1\n");
            var run = RunSpec.FromJson(O("format", "ultrafast-run", "pulse", Gauss(Lam, 300e-15, 1e-6), "instruments", new JsonArray(new JsonValue[] {
                O("kind", "spectrometer", "name", "bad", "resolutionM", 0.1e-9, "measured", O("file", "s.csv", "resolutionM", 2e-9)),
                O("kind", "spectrometer", "name", "coarse", "resolutionM", 4e-9, "measured", O("file", "s.csv", "resolutionM", 2e-9)),
                O("kind", "spectrometer", "name", "good", "resolutionM", 2e-9, "measured", O("file", "s.csv", "resolutionM", 2e-9)) })), dir);
            var r = Go(Sys(), run);
            Assert.True(r.Comparisons.First(c => c.Instrument == "bad").Notes.Any(n => n.Contains("blurred by")), "finer simulated instrument: blurred to the recording's resolution");
            Assert.True(r.Comparisons.First(c => c.Instrument == "coarse").Notes.Any(n => n.Contains("set the instrument's resolutionM")), "coarser simulated instrument: mismatch reported");
            Assert.True(r.Comparisons.First(c => c.Instrument == "good").Notes.Any(n => n.Contains("matches the recording")), "match confirmed");
        }
    }
}
