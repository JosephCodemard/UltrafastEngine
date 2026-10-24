using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltrafastEngine.Engine;
using UltrafastEngine.Measured;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>
    /// Measured data in the core: cleaning of measured input spectra (order, repeated wavelengths, non-finite rows),
    /// background subtraction, comparisons restricted to the overlap of recording and simulation and matched to the
    /// recording's resolution, and the satellite (ghost) room of the time window.
    /// </summary>
    public class MeasuredSpectrumTests
    {
        private const double Lam = 1030e-9;

        private static double[] Wl(double lo, double hi, int n) => Enumerable.Range(0, n).Select(i => (lo + (hi - lo) * i / (n - 1)) * 1e-9).ToArray();
        private static double[] Gaussian(double[] wl, double fwhmNm, double floor) => wl.Select(l => Math.Exp(-4 * Math.Log(2) * Math.Pow((l * 1e9 - 1030) / fwhmNm, 2)) + floor).ToArray();

        private static RunResult RunSpectrum(double[] wl, double[] I, JsonObject extra = null)
        {
            var sp = O("wavelengthsM", wl, "intensity", I);
            if (extra != null) foreach (var kv in extra) sp.Set(kv.Key, kv.Value);
            return Go(Sys(O("id", "p", "type", "probe")), Spec(O("spectrum", sp, "energyJ", 1e-6)));
        }

        private static double Tl(RunResult r) => PulseMetrics.Fwhm(r.Output.Grid.Time, PulseMetrics.TransformLimitedPower(r.Output));

        [Test(Category = "measured")]
        public void InputSpectrum_RepeatedWavelength_NoLongerGivesNaN()
        {
            var wl = Wl(1000, 1060, 200); var I = Gaussian(wl, 6, 0);
            var clean = RunSpectrum(wl, I);
            var dup = (double[])wl.Clone(); dup[60] = dup[59];                 // a digitiser repeated a point
            var r = RunSpectrum(dup, I);
            Assert.True(!double.IsNaN(r.Output.TotalEnergyJ()) && Math.Abs(r.Output.TotalEnergyJ() - 1e-6) < 1e-9, "finite energy");
            Assert.Close(Tl(clean), Tl(r), 0.002, 0, "same transform limit as the clean recording");
            Assert.True(r.Compiled.Notes.Any(n => n.Contains("repeated a wavelength")), "merging reported");
        }

        [Test(Category = "measured")]
        public void InputSpectrum_OutOfOrderRows_AreSortedAndReported()
        {
            var wl = Wl(1000, 1060, 200); var I = Gaussian(wl, 6, 0);
            var clean = RunSpectrum(wl, I);
            // 29 rows out of order (swapped with their neighbour), plus a row that repeats the next one's wavelength
            var w2 = (double[])wl.Clone(); var i2 = (double[])I.Clone();
            for (int k = 0; k < 29; k++) { int a = 5 + 6 * k; (w2[a], w2[a + 1]) = (w2[a + 1], w2[a]); (i2[a], i2[a + 1]) = (i2[a + 1], i2[a]); }
            w2[190] = w2[191];
            var r = RunSpectrum(w2, i2);
            Assert.Close(Tl(clean), Tl(r), 0.003, 0, "transform limit");
            Assert.True(r.Compiled.Notes.Any(n => n.Contains("29 row(s) were out of wavelength order")), "out-of-order rows counted: " + string.Join(" | ", r.Compiled.Notes));
        }

        [Test(Category = "measured")]
        public void InputSpectrum_NonFiniteRows_AreDroppedWithAWarning()
        {
            var wl = Wl(1000, 1060, 200); var I = Gaussian(wl, 6, 0);
            I[10] = double.NaN; wl[20] = double.NaN;
            var r = RunSpectrum(wl, I);
            Assert.True(!double.IsNaN(r.Output.PeakPowerW()), "finite result");
            Assert.True(r.Compiled.Warnings.Any(w => w.Contains("2 row(s)") && w.Contains("dropped")), "dropped rows reported");
        }

        [Test(Category = "measured")]
        public void InputSpectrum_FlatFloor_ShortensTheTransformLimit_AndAutoBackgroundRestoresIt()
        {
            // a wide recording (950-1110 nm) of a 6.5 nm spectrum with a flat 0.66 % floor
            var wl = Wl(950, 1110, 1200);
            var clean = RunSpectrum(wl, Gaussian(wl, 6.5, 0));
            var floor = RunSpectrum(wl, Gaussian(wl, 6.5, 0.0066));
            var auto = RunSpectrum(wl, Gaussian(wl, 6.5, 0.0066), O("background", "auto"));
            var given = RunSpectrum(wl, Gaussian(wl, 6.5, 0.0066), O("background", 0.0066));
            Assert.Less(Tl(floor), 0.6 * Tl(clean), "the floor shortens the transform limit");
            Assert.True(floor.Compiled.Warnings.Any(w => w.Contains("floor of 0.656 %")), "floor reported: " + string.Join(" | ", floor.Compiled.Warnings));
            Assert.Close(Tl(clean), Tl(auto), 0.005, 0, "background auto restores the transform limit");
            Assert.Close(Tl(clean), Tl(given), 0.005, 0, "a given background restores it too");
            Assert.False(auto.Compiled.Warnings.Any(w => w.Contains("floor of")), "no warning once subtracted");
        }

        [Test(Category = "measured")]
        public void InputSpectrum_AutoBackground_IgnoresTheCutOffEdge()
        {
            // an MPC output cut off by the spectrometer at 1100 nm: the long-wavelength edge is bright, the short one dark
            var wl = Wl(950, 1100, 900);
            var I = wl.Select(l => Math.Exp(-4 * Math.Log(2) * Math.Pow((l * 1e9 - 1070) / 40, 2)) + 0.05).ToArray();
            var prep = MeasuredSpectrumPreparation.Prepare(wl, I, null, "auto");
            Assert.Close(0.05, prep.Background, 0.02, 0, "floor from the darker edge, not the cut-off one");
            Assert.Close(0, prep.Intensity[0], 0, 1e-3, "short edge at zero after subtraction");
            Assert.Greater(prep.Intensity[prep.Intensity.Length - 1], 0.15, "the cut-off edge keeps its light");
        }

        [Test(Category = "measured")]
        public void InputSpectrum_Threshold_ZeroesNoiseBelowIt()
        {
            var wl = Wl(1000, 1060, 300); var I = Gaussian(wl, 6, 0);
            var rnd = new Random(3); for (int i = 0; i < I.Length; i++) I[i] += 2e-3 * (rnd.NextDouble() - 0.5);
            var prep = MeasuredSpectrumPreparation.Prepare(wl, I, null, "none", 0, 0.01);
            Assert.True(prep.Intensity.Count(v => v == 0) > 100, "the noisy wings are zeroed");
            Assert.True(prep.Intensity.All(v => v >= 0), "no negative values");
        }

        [Test(Category = "measured")]
        public void InputSpectrum_Csv_WithBackgroundKey()
        {
            var dir = Path.Combine(Path.GetTempPath(), "uf-ms-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(dir);
            var wl = Wl(950, 1110, 800); var I = Gaussian(wl, 6.5, 0.01);
            var sb = new StringBuilder("wavelength_nm,intensity\n");
            for (int i = wl.Length - 1; i >= 0; i--) sb.Append((wl[i] * 1e9).ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(I[i].ToString("R", CultureInfo.InvariantCulture)).Append('\n');   // descending
            File.WriteAllText(Path.Combine(dir, "s.csv"), sb.ToString());
            var run = Specs.RunSpec.FromJson(O("format", "ultrafast-run", "pulse", O("spectrum", O("csv", "s.csv", "background", "auto"), "energyJ", 1e-6)), dir);
            var r = Go(Sys(O("id", "p", "type", "probe")), run);
            var clean = RunSpectrum(wl, Gaussian(wl, 6.5, 0));
            Assert.Close(Tl(clean), Tl(r), 0.005, 0, "csv + background auto");
            Assert.True(r.Compiled.Notes.Any(n => n.Contains("background")), "background noted");
        }

        [Test(Category = "measured")]
        public void InputSpectrum_BadBackground_IsAClearError()
        {
            var wl = Wl(1000, 1060, 50); var I = Gaussian(wl, 6, 0);
            Assert.Throws<JsonException>(() => Specs.RunSpec.FromJson(O("format", "ultrafast-run", "pulse", O("spectrum", O("wavelengthsM", wl, "intensity", I, "background", "median"), "energyJ", 1e-6))), "unknown background mode");
            Assert.Throws<JsonException>(() => Specs.RunSpec.FromJson(O("format", "ultrafast-run", "pulse", O("spectrum", O("wavelengthsM", wl, "intensity", I, "background", 5.0), "energyJ", 1e-6))), "background above the peak");
        }

        // ---------------------------------------------------------------- comparisons with recorded data

        private static string TempDir() { var d = Path.Combine(Path.GetTempPath(), "uf-cmp-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(d); return d; }

        [Test(Category = "measured")]
        public void Comparison_RecordingWiderThanTheSimulation_ComparesOnlyTheOverlap()
        {
            var sys = Sys(O("id", "p", "type", "probe"));
            var pulse = Gauss(Lam, 200e-15, 1e-9);
            var r0 = Go(sys, Spec(pulse, O("instruments", A(O("kind", "spectrometer", "name", "s", "axis", O("from", 1020e-9, "to", 1040e-9, "step", 0.05e-9))))));
            var lam = r0.Instruments[0].Arrays.First(a => a.Name == "s_wavelength_m").Data; var sp = r0.Instruments[0].Arrays.First(a => a.Name == "s_spectrum").Data;
            var ord = Enumerable.Range(0, lam.Length).OrderBy(i => lam[i]).ToArray(); lam = ord.Select(i => lam[i]).ToArray(); sp = ord.Select(i => sp[i]).ToArray();
            var d = TempDir(); var sb = new StringBuilder("wavelength_nm,counts\n");
            // the recording spans 1000-1060 nm; the simulated spectrometer only 1020-1040 nm
            for (double l = 1000; l <= 1060; l += 0.05)
            {
                double v = l < 1020 || l > 1040 ? 0 : Measurements.Interp0(lam, sp, l * 1e-9);
                sb.Append(l.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append((7 * v).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
            File.WriteAllText(Path.Combine(d, "s.csv"), sb.ToString());
            var r = Go(sys, Spec(pulse, O("instruments", A(O("kind", "spectrometer", "name", "s", "axis", O("from", 1020e-9, "to", 1040e-9, "step", 0.05e-9), "measured", Path.Combine(d, "s.csv"))))));
            var c = r.Comparisons.Single();
            Assert.True(c.Notes.Any(n => n.Contains("outside the simulated instrument's range")), "points outside reported: " + string.Join(" | ", c.Notes));
            Assert.Less(c.Points, 450, "only the overlap is compared");
            Assert.Less(c.RmsError, 1e-3, "and it agrees");
        }

        [Test(Category = "measured")]
        public void Comparison_BeyondACroppedReading_IsComparedWithZero()
        {
            // the intensity autocorrelator crops its trace to the signal; a recording over +-1 ps is compared with zero
            // beyond the trace, not left out
            var sys = Sys(O("id", "p", "type", "probe"));
            var pulse = Gauss(Lam, 100e-15, 1e-9);
            var r0 = Go(sys, Spec(pulse, O("instruments", A(O("kind", "autocorrelator", "name", "ac")))));
            var t = r0.Instruments[0].Arrays.First(a => a.Name == "ac_delay_s").Data; var y = r0.Instruments[0].Arrays.First(a => a.Name == "ac_signal").Data;
            Assert.Less(t.Max(), 0.9e-12, "the simulated trace is cropped");
            var d = TempDir(); var sb = new StringBuilder("delay_fs,signal\n");
            for (double tau = -1000; tau <= 1000; tau += 5) sb.Append(tau.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(Measurements.Interp0(t, y, tau * 1e-15).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(Path.Combine(d, "ac.csv"), sb.ToString());
            var c = Go(sys, Spec(pulse, O("instruments", A(O("kind", "autocorrelator", "name", "ac", "measured", Path.Combine(d, "ac.csv")))))).Comparisons.Single();
            Assert.Equal(401, c.Points, "every point compared");
            Assert.True(c.Notes.Any(n => n.Contains("compared with zero")), "noted");
            Assert.Less(c.RmsError, 1e-6, "and they agree");
        }

        [Test(Category = "measured")]
        public void Comparison_CoarserRecording_IsMatchedByBlurringTheSimulation()
        {
            var sys = Sys(O("id", "p", "type", "probe"));
            var pulse = Gauss(Lam, 300e-15, 1e-9);
            // the "lab" spectrometer has 2 nm resolution; the simulated one is ideal
            var lab = Go(sys, Spec(pulse, O("instruments", A(O("kind", "spectrometer", "name", "s", "resolutionM", 2e-9, "axis", O("from", 1015e-9, "to", 1045e-9, "step", 0.1e-9))))));
            var lam = lab.Instruments[0].Arrays.First(a => a.Name == "s_wavelength_m").Data; var sp = lab.Instruments[0].Arrays.First(a => a.Name == "s_spectrum").Data;
            var d = TempDir(); var sb = new StringBuilder("wavelength_nm,counts\n");
            for (int i = 0; i < lam.Length; i++) sb.Append((lam[i] * 1e9).ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(sp[i].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(Path.Combine(d, "s.csv"), sb.ToString());
            JsonObject Inst(bool match) => O("kind", "spectrometer", "name", "s", "axis", O("from", 1015e-9, "to", 1045e-9, "step", 0.1e-9),
                "measured", O("file", Path.Combine(d, "s.csv"), "resolutionM", 2e-9, "matchResolution", match));
            var matched = Go(sys, Spec(pulse, O("instruments", A(Inst(true))))).Comparisons.Single();
            var raw = Go(sys, Spec(pulse, O("instruments", A(Inst(false))))).Comparisons.Single();
            Assert.True(matched.Notes.Any(n => n.Contains("blurred by")), "blur noted");
            Assert.Less(matched.RmsError, 0.01, "matched to the recording's resolution");
            Assert.Greater(raw.RmsError, 5 * matched.RmsError, "without matching the resolution the comparison is biased");
        }

        // ---------------------------------------------------------------- satellites and the time window

        [Test(Category = "measured")]
        public void Window_ManyPlatesWithGhosts_DoesNotGrowWithEveryOrderOfEveryPlate()
        {
            // 20 AR-coated plates (0.2 % per face): each ghost is 4e-6; two together 1.6e-11 -- below grid.satelliteLevel
            var els = Enumerable.Range(0, 20).Select(i => O("id", "p" + i, "type", "bulk", "material", "FusedSilica", "lengthM", 0.02, "ghosts", O("frontReflectance", 0.002, "backReflectance", 0.002))).ToArray();
            var r = Go(Sys(els), Spec(Gauss(Lam, 200e-15, 1e-9), O("physics", O("nonlinear", false))));
            double delay = 2 * 1.46 * 0.02 / 299792458.0;                    // one ghost: ~195 ps
            double window = r.Output.Grid.N * r.Output.Grid.DTime;
            Assert.Less(window, 2.2e-9, "window far below the 20-ghost sum (was 4.4 ns for uncoated plates)");
            Assert.Greater(window / 2, 1.05 * delay, "room for the ghosts themselves");
            Assert.True(r.Compiled.Notes.Any(n => n.StartsWith("Satellites:")), "the window choice is explained");
        }

        [Test(Category = "measured")]
        public void SatelliteRoom_SumsCoherentlyAtEqualDelays_AndHonoursTheLevel()
        {
            CompiledElement Ghost(double delay, double level, int orders = 3)
            {
                var e = new CompiledElement();
                for (int k = 1; k <= orders; k++) e.Satellites.Add((k * delay, Math.Pow(level, k)));
                e.SatelliteDelayS = orders * delay;
                return e;
            }
            // one ghost source of level 1e-3: orders 1 (1e-3) and 2 (1e-6) are above 1e-8, order 3 (1e-9) is not
            Assert.Close(2e-10, Compiler.SatelliteRoom(new[] { Ghost(1e-10, 1e-3) }, 1e-8), 1e-9, 0, "one source");
            // two identical sources: the single ghosts add in amplitude (4e-3), pairs 3 x 1e-6 x ... : up to 3 delays
            double r2 = Compiler.SatelliteRoom(new[] { Ghost(1e-10, 1e-3), Ghost(1e-10, 1e-3) }, 1e-8);
            Assert.Close(3e-10, r2, 1e-9, 0, "two identical sources");
            // a lower satelliteLevel makes room for more
            Assert.Greater(Compiler.SatelliteRoom(new[] { Ghost(1e-10, 1e-3), Ghost(1e-10, 1e-3) }, 1e-12), r2, "lower level, more room");
            // an element that only states a delay is always given room
            var old = new CompiledElement { SatelliteDelayS = 5e-10 };
            Assert.Close(5e-10 + 2e-10, Compiler.SatelliteRoom(new[] { old, Ghost(1e-10, 1e-3) }, 1e-8), 1e-9, 0, "legacy satellite + ghost");
        }
    }
}
