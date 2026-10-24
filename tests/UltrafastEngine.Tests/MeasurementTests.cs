using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltrafastEngine.Measured;
using UltrafastEngine.Plugins.Fitting;
using UltrafastEngine.Instruments;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Phase 2.5: instrument sampling (near / far field, apertures), measured-data comparison and the
    /// calibration fit (docs/measurements.md).</summary>
    public class MeasurementTests
    {
        private const double Lam = 1030e-9;

        private static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "uf-meas-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        private static JsonObject V(RunResult r, string name) => r.Instruments.First(i => i.Name == name).Values;
        private static double[] Arr(RunResult r, string name) => r.Instruments.SelectMany(i => i.Arrays).First(a => a.Name == name).Data;

        /// <summary>A collimated Gaussian beam (radial model) after L of vacuum, with the given instruments.</summary>
        private static RunResult Beam(JsonArray instruments, double w0 = 300e-6, double L = 0.1, double fwhm = 300e-15)
        {
            var sys = Sys(O("id", "g", "type", "gap", "material", "Vacuum", "lengthM", L,
                            "beam", O("kind", "gaussian", "w0M", w0, "wavelengthM", Lam),
                            "transverse", O("model", "radial", "points", 64, "radiusM", 1.5e-3)));
            var r = Go(sys, Spec(Gauss(Lam, fwhm, 1e-6), O("instruments", instruments)));
            Assert.Equal(0, r.Warnings.Count(x => x.StartsWith("Instrument")), "instrument warnings: " + string.Join("; ", r.Warnings));
            return r;
        }

        // ------------------------------------------------------------------ sampling

        [Test(Category = "instruments")]
        public void Sampling_Integrated_IsTheWholeBeam()
        {
            var r = Beam(A(O("kind", "spectrometer", "name", "plain", "at", "g"),
                           O("kind", "spectrometer", "name", "whole", "at", "g", "sampling", O("plane", "far", "aperture", O("kind", "integrated")))));
            Assert.Close(V(r, "plain").GetDouble("fwhmM", 0), V(r, "whole").GetDouble("fwhmM", 1), 1e-12, 0, "same spectrum");
            Assert.True(V(r, "whole").GetString("sampling").Contains("whole beam"), "described as the whole beam");
        }

        [Test(Category = "instruments")]
        public void Sampling_GaussianApertures_TransmitTheAnalyticFraction()
        {
            double w0 = 300e-6, L = 0.1, f = 0.5, zr = Math.PI * w0 * w0 / Lam;
            double w = w0 * Math.Sqrt(1 + (L / zr) * (L / zr)), wff = Lam * f / (Math.PI * w0);
            var r = Beam(A(
                O("kind", "powerMeter", "name", "pin_near", "at", "g", "sampling", O("plane", "near", "aperture", O("kind", "pinhole", "radiusM", 200e-6))),
                O("kind", "powerMeter", "name", "pin_far", "at", "g", "sampling", O("plane", "far", "focalLengthM", f, "aperture", O("kind", "pinhole", "radiusM", wff))),
                O("kind", "powerMeter", "name", "slit_near", "at", "g", "sampling", O("plane", "near", "aperture", O("kind", "slit", "widthM", 200e-6))),
                O("kind", "powerMeter", "name", "pin_off", "at", "g", "sampling", O("plane", "near", "aperture", O("kind", "pinhole", "radiusM", 50e-6, "centerXM", 300e-6)))));
            Assert.Close(1 - Math.Exp(-2 * 200e-6 * 200e-6 / (w * w)), V(r, "pin_near").GetDouble("samplingTransmission", 0), 0.02, 0, "near-field pinhole 1 - exp(-2a^2/w^2)");
            Assert.Close(1 - Math.Exp(-2.0), V(r, "pin_far").GetDouble("samplingTransmission", 0), 0.02, 0, "far-field pinhole of radius w_ff = lambda f / (pi w0)");
            Assert.Close(Erf(200e-6 / (Math.Sqrt(2) * w)), V(r, "slit_near").GetDouble("samplingTransmission", 0), 0.02, 0, "slit erf(W / (sqrt2 w))");
            // small pinhole off axis: T ~ area x local fluence = pi a^2 x (2 / (pi w^2)) exp(-2 x^2 / w^2)
            Assert.Close(2 * 50e-6 * 50e-6 / (w * w) * Math.Exp(-2 * 300e-6 * 300e-6 / (w * w)), V(r, "pin_off").GetDouble("samplingTransmission", 0), 0.05, 0, "off-axis pinhole");
            // the power meter reads the transmitted energy
            Assert.Close(1e-6 * V(r, "pin_near").GetDouble("samplingTransmission", 0), V(r, "pin_near").GetDouble("energyJ", 0), 1e-6, 0, "energy behind the pinhole");
        }

        [Test(Category = "instruments")]
        public void Sampling_FarFieldFibre_On1DBeam_FavoursTheBlue()
        {
            // 1-D: an ideal Gaussian; its focal spot scales with wavelength, so a small fibre couples short wavelengths better
            var sys = Sys(O("id", "plate", "type", "bulk", "material", "FusedSilica", "lengthM", 1e-3, "nonlinear", false,
                            "beam", O("kind", "gaussian", "w0M", 1e-3)));
            var r = Go(sys, Spec(Gauss(800e-9, 15e-15, 1e-9), O("instruments", A(
                O("kind", "spectrometer", "name", "all", "at", "plate"),
                O("kind", "spectrometer", "name", "fibre", "at", "plate", "sampling", O("plane", "far", "focalLengthM", 0.1, "aperture", O("kind", "fibre", "radiusM", 5e-6)))))));
            Assert.Less(V(r, "fibre").GetDouble("centroidWavelengthM", 0), V(r, "all").GetDouble("centroidWavelengthM", 0) - 0.2e-9, "fibre spectrum shifted to the blue");
            Assert.True(r.Instruments.First(i => i.Name == "fibre").Notes.Any(n => n.Contains("1-D beam")), "explains the 1-D sampling");
        }

        [Test(Category = "instruments")]
        public void Sampling_IgnoredByImagingInstruments_AndBadPlaneRejected()
        {
            var r = Beam(A(O("kind", "nearFieldCamera", "name", "cam", "at", "g", "sampling", O("plane", "far"))));
            Assert.True(r.Instruments[0].Notes.Any(n => n.Contains("sampling")), "camera notes that sampling does not apply");
            bool reported;
            try
            {
                var sys = Sys(O("id", "g", "type", "gap", "material", "Vacuum", "lengthM", 0.1, "beam", O("kind", "gaussian", "w0M", 300e-6), "transverse", O("model", "radial", "points", 32)));
                var bad = Go(sys, Spec(Gauss(Lam, 300e-15, 1e-6), O("instruments", A(O("kind", "spectrometer", "name", "s", "at", "g", "sampling", O("plane", "middle"))))));
                reported = bad.Warnings.Any(w => w.Contains("sampling.plane"));
            }
            catch (JsonException e) { reported = e.Message.Contains("sampling.plane"); }
            Assert.True(reported, "an unknown plane is reported");
        }

        // ------------------------------------------------------------------ measured data

        [Test(Category = "instruments")]
        public void MeasuredData_CsvUnitsAndColumns()
        {
            var d = TempDir();
            var p = Path.Combine(d, "s.csv");
            File.WriteAllText(p, "# a comment\nwavelength_nm;counts;err\n800;10;1\n801;20;1\n802;10;1\n");
            var md = MeasuredData.Load(p);
            Assert.Equal(3, md.Columns["counts"].Length, "rows");
            Assert.Equal("wavelength_nm", md.Find("wavelength", "x"), "found by stem");
            Assert.Equal("err", md.Find("sigma", "err"), "sigma column");
            Assert.Close(1e-9, MeasuredData.Unit("wavelength_nm"), 0, 0, "nm");
            Assert.Close(1e-15, MeasuredData.Unit("delay_fs"), 0, 0, "fs");
            Assert.Close(1e-30, MeasuredData.Unit("gdd_fs2"), 0, 0, "fs^2");
            Assert.Close(1e-6, MeasuredData.Unit("energy_uj"), 0, 0, "uJ");
            Assert.Close(1, MeasuredData.Unit("counts"), 0, 0, "no unit");
            var p2 = Path.Combine(d, "n.txt");
            File.WriteAllText(p2, "1 2\n3 4\n");
            var m2 = MeasuredData.Load(p2);
            Assert.Equal("col0", m2.Order[0], "no header: col0, col1");
            Assert.Close(3, m2.Columns["col0"][1], 0, 0, "whitespace separated");
        }

        [Test(Category = "instruments")]
        public void MeasuredData_NpzRoundTrip()
        {
            var d = TempDir();
            var p = Path.Combine(d, "t.npz");
            var t = new double[] { 1, 2, 3, 4, 5, 6 };
            NpzWriter.Write(p, new[] { new NdArray("trace", t, 2, 3), new NdArray("delay_fs", new double[] { -1, 1 }) });
            var md = MeasuredData.Load(p);
            Assert.Equal(2, md.Shapes["trace"].Length, "2-D shape kept");
            Assert.Close(6, md.Columns["trace"][5], 0, 0, "values");
            Assert.Close(-1, md.Columns["delay_fs"][0], 0, 0, "axis");
        }

        [Test(Category = "instruments")]
        public void Comparison_DataEqualToTheSimulation_GivesZeroResidual()
        {
            var sys = Sys(O("id", "plate", "type", "bulk", "material", "FusedSilica", "lengthM", 3e-3, "beam", O("kind", "gaussian", "w0M", 80e-6, "zWaistM", 1.5e-3)));
            var pulse = Gauss(800e-9, 40e-15, 100e-9);
            var r0 = Go(sys, Spec(pulse, O("instruments", A(O("kind", "spectrometer", "name", "s"), O("kind", "autocorrelator", "name", "ac"),
                                                           O("kind", "dscan", "name", "ds", "material", "FusedSilica", "insertionM", new[] { -2e-3, 2e-3 }, "steps", 9)))));
            var d = TempDir();
            // the simulated readings, written as a lab would: nm and fs columns, scaled counts, and an NPZ trace
            var lam = Arr(r0, "s_wavelength_m"); var sp = Arr(r0, "s_spectrum");
            var sb = new StringBuilder("wavelength_nm,counts\n");
            for (int i = 0; i < lam.Length; i++) if (lam[i] > 760e-9 && lam[i] < 840e-9) sb.Append((lam[i] * 1e9).ToString("R", CultureInfo.InvariantCulture)).Append(',').Append((5000 * sp[i]).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(Path.Combine(d, "spec.csv"), sb.ToString());
            var dl = Arr(r0, "ac_delay_s"); var ac = Arr(r0, "ac_signal");
            var sb2 = new StringBuilder("delay_fs,signal\n");
            for (int i = 0; i < dl.Length; i++) if (Math.Abs(dl[i]) < 200e-15) sb2.Append((dl[i] * 1e15).ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(ac[i].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(Path.Combine(d, "ac.csv"), sb2.ToString());
            var ins = Arr(r0, "ds_insertion_m"); var wl = Arr(r0, "ds_shg_wavelength_m"); var tr = Arr(r0, "ds_trace");
            NpzWriter.Write(Path.Combine(d, "ds.npz"), new[] { new NdArray("insertion_mm", ins.Select(x => x * 1e3).ToArray()), new NdArray("wavelength_nm", wl.Select(x => x * 1e9).ToArray()), new NdArray("trace", tr, ins.Length, wl.Length) });

            var r = Go(sys, Spec(pulse, O("instruments", A(
                O("kind", "spectrometer", "name", "s", "measured", O("file", Path.Combine(d, "spec.csv"))),
                O("kind", "autocorrelator", "name", "ac", "measured", Path.Combine(d, "ac.csv")),
                O("kind", "dscan", "name", "ds", "material", "FusedSilica", "insertionM", new[] { -2e-3, 2e-3 }, "steps", 9, "measured", O("file", Path.Combine(d, "ds.npz"))),
                O("kind", "energyMeter", "name", "pm", "measured", O("values", O("energyJ", A(r0.Output.TotalEnergyJ() * 1.03, r0.Output.TotalEnergyJ() * 0.01))))))));
            Assert.Equal(4, r.Comparisons.Count, "four comparisons: " + string.Join("; ", r.Warnings));
            var c = r.Comparisons.ToDictionary(x => x.Instrument);
            Assert.Less(c["s"].RmsError, 1e-6, "spectrum residual");
            Assert.Less(c["ac"].RmsError, 1e-6, "autocorrelation residual");
            Assert.Less(c["ds"].RmsError, 1e-6, "d-scan trace residual");
            Assert.Equal("curve", c["s"].Mode, "curve");
            Assert.Equal("trace", c["ds"].Mode, "trace");
            Assert.Equal("values", c["pm"].Mode, "values");
            Assert.Close(-3.0, c["pm"].Residuals[0], 1e-6, 0, "scalar residual (sim - meas) / sigma");
            Assert.Close(9.0, c["pm"].Chi2, 1e-6, 0, "scalar chi2");
            Assert.True(r.SummaryJson().GetArray("comparisons").Count == 4, "summary lists the comparisons");
        }

        [Test(Category = "instruments")]
        public void Comparison_Normalisations_AndBackground()
        {
            var sys = Sys(O("id", "p", "type", "probe"));
            var r0 = Go(sys, Spec(Gauss(800e-9, 30e-15, 1e-9), O("instruments", A(O("kind", "spectrometer", "name", "s")))));
            var lam = Arr(r0, "s_wavelength_m"); var sp = Arr(r0, "s_spectrum");
            var d = TempDir();
            var sb = new StringBuilder("wavelength_nm,counts\n");
            double mx = sp.Max();
            for (int i = 0; i < lam.Length; i++) if (lam[i] > 700e-9 && lam[i] < 900e-9) sb.Append((lam[i] * 1e9).ToString("R", CultureInfo.InvariantCulture)).Append(',').Append((250 + 1000 * sp[i] / mx).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(Path.Combine(d, "s.csv"), sb.ToString());
            foreach (var norm in new[] { "peak", "maximum", "area", "scale" })
            {
                var r = Go(sys, Spec(Gauss(800e-9, 30e-15, 1e-9), O("instruments", A(O("kind", "spectrometer", "name", "s",
                    "measured", O("file", Path.Combine(d, "s.csv"), "background", "auto", "normalize", norm))))));
                Assert.Less(r.Comparisons[0].RmsError, 1e-3, norm + ": offset removed, shapes agree");
                if (norm == "scale") Assert.Close(1000 / mx, V(r, "s").GetDouble("measuredScale", 0), 1e-3, 0, "best-fit scale factor");
            }
            // without the background, the offset shows up
            var rb = Go(sys, Spec(Gauss(800e-9, 30e-15, 1e-9), O("instruments", A(O("kind", "spectrometer", "name", "s", "measured", Path.Combine(d, "s.csv"))))));
            Assert.Greater(rb.Comparisons[0].RmsError, 0.05, "uncorrected background is a large residual");
        }

        [Test(Category = "instruments")]
        public void Comparison_MissingFile_IsAWarningNotAFailure()
        {
            var r = Go(Sys(O("id", "p", "type", "probe")), Spec(Gauss(800e-9, 30e-15, 1e-9), O("instruments", A(O("kind", "spectrometer", "name", "s", "measured", "/no/such/file.csv")))));
            Assert.Equal(0, r.Comparisons.Count, "nothing compared");
            Assert.True(r.Warnings.Any(w => w.Contains("measured data not compared") && w.Contains("not found")), "warning names the problem");
            Assert.True(r.Instruments.Any(i => i.Name == "s"), "the instrument still ran");
        }

        // ------------------------------------------------------------------ fit

        [Test]
        public void PseudoInverse_FindsTheNullDirection()
        {
            var (inv, nul) = Fitter.PseudoInverse(new double[,] { { 1, 1 }, { 1, 1 } });
            Assert.Equal(1, nul.Count, "one unidentifiable combination");
            Assert.Close(Math.Abs(nul[0][0]), Math.Abs(nul[0][1]), 1e-9, 0, "direction (1, -1) / sqrt 2");
            Assert.Less(nul[0][0] * nul[0][1], 0, "opposite signs");
            Assert.True(double.IsPositiveInfinity(inv[0, 0]) && double.IsPositiveInfinity(inv[1, 1]), "parameters in a null direction get an infinite variance");
            var (inv2, nul2) = Fitter.PseudoInverse(new double[,] { { 4, 0 }, { 0, 2 } });
            Assert.Equal(0, nul2.Count, "full rank");
            Assert.Close(0.5, inv2[1, 1], 1e-12, 0, "plain inverse");
        }

        [Test]
        public void FitPaths_ReadAndWriteTheFiles()
        {
            var sys = (JsonObject)JsonValue.Parse("{\"elements\":[{\"id\":\"amp\",\"pump\":{\"powerW\":60},\"betweenPasses\":{\"materials\":[{\"name\":\"FS\",\"lengthM\":0.003}]}}]}");
            var run = (JsonObject)JsonValue.Parse("{\"pulse\":{\"energyJ\":1e-6}}");
            Assert.Close(60, Fitter.Get(sys, run, "element:amp/pump/powerW"), 0, 0, "nested element value");
            Assert.Close(0.003, Fitter.Get(sys, run, "element:amp/betweenPasses/materials/0/lengthM"), 0, 0, "inside a list");
            Fitter.Set(sys, run, "element:amp/betweenPasses/materials/0/lengthM", 0.004);
            Assert.Close(0.004, Fitter.Get(sys, run, "element:amp/betweenPasses/materials/0/lengthM"), 0, 0, "written into the list");
            Fitter.Set(sys, run, "pulse/chirp/gddFs2", 120);
            Assert.Close(120, Fitter.Get(sys, run, "pulse/chirp/gddFs2"), 0, 0, "created when missing");
            Fitter.Set(sys, run, "material:FusedSilica/n2", 3e-20);
            Assert.Equal("material:FusedSilica", run.GetArray("overrides")[0].AsObject().GetString("target"), "material values become overrides");
            Assert.Throws<JsonException>(() => Fitter.Get(sys, run, "nonsense/x"), "unknown root");
            Assert.Throws<JsonException>(() => Fitter.Get(sys, run, "element:nope/x"), "unknown element");
        }

        [Test(Category = "fit")]
        public void Fit_RecoversEnergyAndChirp_FromSpectrumAndAutocorrelation()
        {
            // the tutorial-4 problem without noise: data simulated at 260 nJ and +120 fs^2; the fit starts at 200 nJ, 0 fs^2
            var sys = Sys(O("id", "plate", "type", "bulk", "material", "FusedSilica", "lengthM", 5e-3, "beam", O("kind", "gaussian", "w0M", 60e-6, "zWaistM", 2.5e-3)),
                          O("id", "air", "type", "gap", "lengthM", 0.05));
            JsonObject Pulse(double e, double gdd) { var p = Gauss(800e-9, 40e-15, e); p.Set("chirp", O("gddFs2", gdd)); return p; }
            var truth = Go(sys, Spec(Pulse(260e-9, 120), O("instruments", A(O("kind", "spectrometer", "name", "s"), O("kind", "autocorrelator", "name", "ac")))));
            var d = TempDir();
            var lam = Arr(truth, "s_wavelength_m"); var sp = Arr(truth, "s_spectrum");
            var sb = new StringBuilder("wavelength_nm,counts\n");
            for (int i = 0; i < lam.Length; i++) if (lam[i] > 740e-9 && lam[i] < 860e-9) sb.Append((lam[i] * 1e9).ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(sp[i].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(Path.Combine(d, "s.csv"), sb.ToString());
            var dl = Arr(truth, "ac_delay_s"); var ac = Arr(truth, "ac_signal");
            var sb2 = new StringBuilder("delay_ps,signal\n");
            for (int i = 0; i < dl.Length; i++) if (Math.Abs(dl[i]) < 0.3e-12) sb2.Append((dl[i] * 1e12).ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(ac[i].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(Path.Combine(d, "ac.csv"), sb2.ToString());
            var fit = O("parameters", A(O("name", "energy", "path", "pulse/energyJ", "min", 50e-9, "max", 500e-9),
                                        O("name", "chirp", "path", "pulse/chirp/gddFs2", "min", -400.0, "max", 400.0)));
            var r = Go(sys, Spec(Pulse(200e-9, 0), O("fit", fit, "instruments", A(
                O("kind", "spectrometer", "name", "s", "measured", O("file", Path.Combine(d, "s.csv"), "sigma", 0.005)),
                O("kind", "autocorrelator", "name", "ac", "measured", O("file", Path.Combine(d, "ac.csv"), "sigma", 0.01)),
                O("kind", "pulseSummary", "name", "sum")))));
            var f = r.Fit();
            Assert.True(f != null && f.Converged, "fit converged");
            Assert.Close(260e-9, f.Parameters[0].Value, 0.01, 0, "energy recovered");
            Assert.Close(120, f.Parameters[1].Value, 0, 3, "chirp recovered");
            Assert.Less(f.Chi2Final, 1e-2 * f.Chi2Initial, "chi2 dropped by > 100x");
            Assert.True(f.Parameters.All(p => p.Sigma > 0 && !double.IsNaN(p.Sigma)), "uncertainties reported");
            Assert.Equal(2, f.Correlation.GetLength(0), "correlation matrix");
            Assert.True(f.Predictions.ContainsKey("sum.fwhmS") && f.Predictions.ContainsKey("plate.bIntegralRad"), "predictions for unmeasured instruments and elements");
            Assert.Close(truth.Output.TotalEnergyJ(), r.Output.TotalEnergyJ(), 0.01, 0, "the returned run is the best fit");
            Assert.True(r.Instruments.First(i => i.Name == "s").Arrays.Any(a => a.Name.EndsWith("_simulated_sigma")), "1-sigma band on the compared curve");
            var js = f.ToJson();
            Assert.True(js.GetObject("predictions").Has("sum.fwhmS") && js.GetArray("parameters").Count == 2, "fit.json content");
        }

        [Test(Category = "fit")]
        public void Fit_ParameterTheDataDoNotSee_IsFlagged()
        {
            // a power meter only sees the energy, not the chirp
            var sys = Sys(O("id", "p", "type", "probe"));
            var pulse = Gauss(800e-9, 30e-15, 1e-9); pulse.Set("chirp", O("gddFs2", 0.0));
            var fit = O("parameters", A(O("name", "energy", "path", "pulse/energyJ", "min", 0.1e-9, "max", 10e-9),
                                        O("name", "chirp", "path", "pulse/chirp/gddFs2", "min", -100.0, "max", 100.0)), "maxIterations", 5);
            var r = Go(sys, Spec(pulse, O("fit", fit, "instruments", A(O("kind", "energyMeter", "name", "pm", "measured", O("values", O("energyJ", A(2e-9, 0.02e-9))))))));
            Assert.Close(2e-9, r.Fit().Parameters[0].Value, 1e-3, 0, "energy fitted");
            Assert.True(r.Fit().Warnings.Any(w => w.Contains("chirp") && (w.Contains("does not affect") || w.Contains("unidentifiable"))), "chirp flagged: " + string.Join("; ", r.Fit().Warnings));
        }

        private static double Erf(double x)
        {
            // Abramowitz & Stegun 7.1.26 (absolute error < 1.5e-7)
            double t = 1 / (1 + 0.3275911 * Math.Abs(x));
            double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return x >= 0 ? y : -y;
        }
    }
}
