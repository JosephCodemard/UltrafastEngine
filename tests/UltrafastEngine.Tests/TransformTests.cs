using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Plugins.Fitting;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Stage 2: transform elements, backward propagation, fits with transforms, estimators, priors, layers
    /// (docs/plugins/fitting.md).</summary>
    [TestArea("Fitting plugin: transforms, backward propagation, fits")]
    public class TransformTests
    {
        private const double Lam = 1030e-9;

        private static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "uf-tr-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        private static JsonObject Tr(string id, params JsonObject[] ops) { var o = O("id", id, "type", "transform"); o.Set("operators", new JsonArray(ops)); return o; }
        private static JsonObject FitV(double v, double min, double max) => O("value", v, "fit", true, "min", min, "max", max);
        private static RunSpec SpecGrid(JsonObject pulse, JsonObject extra = null)
        {
            var e = extra ?? new JsonObject();
            if (!e.Has("grid")) e.Set("grid", O("auto", false, "n", 4096, "windowS", 20e-12));
            return Spec(pulse, e);
        }

        private static double MaxRelDiff(Complex[] a, Complex[] b)
        {
            double m = a.Max(z => z.Magnitude), d = 0;
            for (int i = 0; i < a.Length; i++) d = Math.Max(d, (a[i] - b[i]).Magnitude);
            return d / m;
        }

        // ------------------------------------------------------------------ transform element

        [Test(Category = "transform")]
        public void Transform_SpectralPhase_EqualsDispersionElement()
        {
            var pulse = Gauss(Lam, 100e-15, 1e-6);
            var a = Go(Sys(Tr("t", O("kind", "spectralPhase", "gddFs2", 3000.0, "todFs3", 2e4))), SpecGrid(pulse));
            var b = Go(Sys(O("id", "d", "type", "dispersion", "gddFs2", 3000.0, "todFs3", 2e4)), SpecGrid(pulse));
            Assert.Less(MaxRelDiff(a.Output.Ax, b.Output.Ax), 1e-6, "same field as a dispersion element");
            Assert.Close(b.Output.PeakPowerW(), a.Output.PeakPowerW(), 1e-6, 0, "peak power");
            Assert.True(a.Records[0].Terms.Any(t => t.Contains("GDD 3000")), "described");
        }

        [Test(Category = "transform")]
        public void Transform_GaussianFilter_AndEnergy_GoToTheLossBudget()
        {
            var pulse = Gauss(Lam, 300e-15, 1e-6);
            var r = Go(Sys(Tr("t", O("kind", "spectralAmplitude", "shape", "gaussian", "fwhmNm", 3.0, "centerWavelengthNm", 1031.0), O("kind", "energy", "transmission", 0.5))), SpecGrid(pulse));
            var g = r.Output.Grid; var s0 = r.Input.SpectralIntensity(); var s1 = r.Output.SpectralIntensity();
            int imax = Array.IndexOf(s0, s0.Max());
            for (int i = imax - 40; i <= imax + 40; i += 10)
            {
                double l = 2 * Math.PI * C / g.OmegaAbsolute[i], T = 0.5 * Math.Exp(-4 * Math.Log(2) * Math.Pow((l - 1031e-9) / 3e-9, 2));
                Assert.Close(T, s1[i] / s0[i], 1e-6, 0, "transmission at " + (l * 1e9).ToString("F2"));
            }
            double lost = r.Records[0].LossJ["transform"];
            Assert.Close(r.Input.TotalEnergyJ() - r.Output.TotalEnergyJ(), lost, 1e-9, 0, "energy budget");
        }

        [Test(Category = "transform")]
        public void Transform_Replica_IsAPostPulseAtTheRightLevel()
        {
            var r = Go(Sys(Tr("etalon", O("kind", "replica", "delayPs", 5.0, "relativeIntensity", 1e-3))),
                Spec(Gauss(Lam, 200e-15, 1e-6), O("instruments", A(O("kind", "contrast", "name", "c")))));
            var v = r.Instruments.First().Values;
            Assert.Close(-30, v.GetDouble("postPulseContrastDb", 0), 0.02, 0, "post-pulse at 10 log10(1e-3)");
            var g = r.Output.Grid;
            Assert.Greater(g.N * g.DTime, 10e-12, "the grid planner made room for the satellite");
        }

        [Test(Category = "transform")]
        public void Transform_JonesRotation_TurnsXIntoY()
        {
            var r = Go(Sys(Tr("rot", O("kind", "jones", "rotationDeg", 90.0))), SpecGrid(Gauss(Lam, 200e-15, 1e-6), O("instruments", A(O("kind", "polarimeter", "name", "p")))));
            Assert.Close(-1, r.Instruments.First().Values.GetDouble("s1", 0), 1e-6, 1e-6, "s1 = -1 (y polarised)");
            var r2 = Go(Sys(Tr("wp", O("kind", "jones", "retardanceRad", Math.PI / 2, "axisDeg", 45.0))), SpecGrid(Gauss(Lam, 200e-15, 1e-6), O("instruments", A(O("kind", "polarimeter", "name", "p")))));
            Assert.Close(1, Math.Abs(r2.Instruments.First().Values.GetDouble("s3", 0)), 1e-6, 1e-6, "quarter-wave at 45 deg: circular");
        }

        [Test(Category = "transform", Reference = "minimum-phase filter of a Lorentzian: one-sided exponential")]
        public void Transform_MinimumPhase_IsCausal()
        {
            // a Lorentzian power transmission: the minimum-phase filter is a one-sided exponential response
            int n = 4096; double dw = 2e11, tau = 5e-13;
            var T = Enumerable.Range(0, n).Select(i => { double w = (i - n / 2) * dw; return 1 / (1 + w * w * tau * tau); }).ToArray();
            var h = MinimumPhase.FromTransmission(T, 1e-12);
            var ht = SpectralTransform.FreqToTime(h);
            double before = 0, all = 0;
            for (int i = 0; i < n; i++) { double e = ht[i].Magnitude * ht[i].Magnitude; all += e; if (i < n / 2 - 2) before += e; }
            Assert.Less(before / all, 1e-3, "impulse response vanishes before t = 0 (causal)");
            // and a pure amplitude filter (zero phase) is acausal (symmetric response)
            var hz = SpectralTransform.FreqToTime(T.Select(t => new Complex(Math.Sqrt(t), 0)).ToArray());
            double b2 = 0, a2 = 0; for (int i = 0; i < n; i++) { double e = hz[i].Magnitude * hz[i].Magnitude; a2 += e; if (i < n / 2 - 2) b2 += e; }
            Assert.Greater(b2 / a2, 0.2, "zero-phase filter rings before t = 0");
        }

        [Test(Category = "transform")]
        public void Transform_Spline_InterpolatesKnots()
        {
            var s = new Spline(new[] { 3.0, 1.0, 2.0, 4.0 }, new[] { 9.0, 1.0, 4.0, 16.0 });
            Assert.Close(4.0, s.Eval(2.0), 1e-12, 0, "through the knots");
            Assert.Close(6.25, s.Eval(2.5), 1e-12, 0, "a parabola is reproduced exactly (not-a-knot ends)");
            Assert.Close(1.0, s.Eval(0.0), 1e-12, 0, "constant outside");
        }

        [Test(Category = "transform")]
        public void Transform_EveryOperator_InvertsToTheInput()
        {
            var pulse = Gauss(Lam, 150e-15, 1e-6);
            var ops = new[]
            {
                O("kind", "spectralPhase", "gddFs2", 5000.0, "fodFs4", 1e6),
                O("kind", "spectralAmplitude", "shape", "tilt", "tiltDbPerNm", -0.2, "passive", false),
                O("kind", "energy", "transmission", 0.3),
                O("kind", "delay", "delayFs", 120.0),
                O("kind", "jones", "retardanceRad", 0.7, "axisDeg", 20.0, "rotationDeg", 10.0, "diattenuation", 0.2),
                O("kind", "replica", "delayPs", 1.0, "relativeIntensity", 0.01),
                O("kind", "phaseNoise", "rmsRad", 0.3, "correlationNm", 0.5),
            };
            var r0 = Go(Sys(), SpecGrid(pulse));
            var p = r0.Output;
            var ctx = new TransformContext { Omega0 = p.Grid.OmegaCenter, ElementId = "t" };
            int k = 0;
            foreach (var o in ops)
            {
                var op = TransformOperators.Create(o, k++, ctx);
                var q = op.Invert(op.Apply(p), 1e-9);
                Assert.Less(MaxRelDiff(p.Ax, q.Ax), 1e-6, o.GetString("kind") + ": inverse(apply(p)) = p");
            }
        }

        [Test(Category = "transform", Reference = "Maréchal exp(−(2π σ)²)", Tolerance = "1e-12, 1e-09")]
        public void Transform_Wavefront_ActsOnTheSpatialProfile()
        {
            var sys = Sys(O("id", "g1", "type", "gap", "material", "Vacuum", "lengthM", 0.01, "beam", O("kind", "gaussian", "w0M", 1e-3, "wavelengthM", Lam),
                            "transverse", O("model", "radial", "points", 64, "radiusM", 4e-3)),
                          Tr("ab", O("kind", "wavefront", "zernike", O("11", 0.1))));
            var r = Go(sys, Spec(Gauss(Lam, 300e-15, 1e-6), O("instruments", A(O("kind", "wavefrontSensor", "name", "wfs", "zernikeTerms", 15)))));
            var rec = r.Records.First(x => x.Id == "ab");
            Assert.Close(Math.Exp(-Math.Pow(2 * Math.PI * 0.1, 2)), rec.Values["strehlEstimate"], 1e-9, 0, "Strehl estimate");
            Assert.Greater(r.Instruments.First().Values.GetDouble("rmsWaves", 0), 0.05, "the sensor sees the spherical aberration");
            // on a 1-D beam it is reported only
            var r1 = Go(Sys(Tr("ab", O("kind", "wavefront", "zernike", O("4", 0.2)))), SpecGrid(Gauss(Lam, 300e-15, 1e-6)));
            Assert.True(r1.Records[0].Notes.Any(x => x.Contains("reported only")), "1-D note");
            Assert.Close(r1.Input.PeakPowerW(), r1.Output.PeakPowerW(), 1e-12, 0, "pulse unchanged");
        }

        [Test(Category = "transform")]
        public void Transform_Pedestal_AddsAnIncoherentBackground()
        {
            var r = Go(Sys(Tr("ase", O("kind", "pedestal", "type", "ase", "relativeIntensity", 1e-6, "durationS", 5e-12))),
                Spec(Gauss(Lam, 300e-15, 1e-6), O("grid", O("auto", false, "n", 16384, "windowS", 20e-12), "instruments", A(O("kind", "contrast", "name", "c", "windowS", 1.5e-12)))));
            double pre = r.Instruments.First().Values.GetDouble("prePulseContrastDb", 0);
            Assert.Greater(pre, -66, "pedestal raises the pre-pulse level to about -60 dB");
            Assert.Less(pre, -54, "... but not above it");
        }

        [Test(Category = "transform")]
        public void Transform_Schema_AcceptsFitObjects_AndFlagsTypos()
        {
            var sysJ = O("format", "ultrafast-system", "version", 1, "name", "t");
            sysJ.Set("elements", A(Tr("t", O("kind", "spectralPhase", "gddFs2", FitV(0, -1e4, 1e4), "tod", 3.0))));
            var w = UltrafastEngine.Schema.Validator.CheckSystem(SystemDescription.FromJson(sysJ));
            Assert.False(w.Any(x => x.Contains("gddFs2")), "fit object accepted: " + string.Join("; ", w));
            Assert.True(w.Any(x => x.Contains("\"tod\"")), "unknown key reported: " + string.Join("; ", w));
        }

        // ------------------------------------------------------------------ backward propagation

        private static RunResult Backward(SystemDescription sys, PulseField measured, JsonObject backward, JsonObject extra = null)
        {
            var spec = O("format", "ultrafast-run");
            spec.Set("pulse", BackwardPropagation.PulseJson(measured, "end"));
            spec.Set("backward", backward ?? new JsonObject());
            spec.Set("grid", O("auto", false, "n", measured.Grid.N, "windowS", measured.Grid.N * measured.Grid.DTime));
            if (extra != null) foreach (var kv in extra) spec.Set(kv.Key, kv.Value);
            return Go(sys, RunSpec.FromJson(spec));
        }

        [Test(Category = "transform")]
        public void Backward_LinearSystem_RecoversTheInputPulse()
        {
            var sys = Sys(O("id", "glass", "type", "bulk", "material", "FusedSilica", "lengthM", 0.02, "nonlinear", false),
                          Tr("shaper", O("kind", "spectralPhase", "gddFs2", -2000.0, "todFs3", 3e4), O("kind", "spectralAmplitude", "shape", "gaussian", "fwhmNm", 60.0), O("kind", "energy", "transmission", 0.8)),
                          O("id", "d", "type", "dispersion", "gddFs2", 1500.0));
            var fw = Go(sys, SpecGrid(Gauss(Lam, 60e-15, 1e-6)));
            var bw = Backward(sys, fw.Output, null, O("instruments", A(O("kind", "pulseSummary", "name", "atInput", "at", "input"))));
            Assert.Close(fw.Input.TotalEnergyJ(), bw.Input.TotalEnergyJ(), 2e-3, 0, "energy at the input");
            Assert.Close(fw.Records[0].Out.FwhmS > 0 ? 60e-15 : 0, bw.Instruments.First().Values.GetDouble("fwhmS", 0), 0.01, 0, "transform-limited 60 fs input recovered");
            Assert.True(bw.Records.Count == 3 && bw.Records.All(x => x.Notes.Any(n => n.StartsWith("backward"))), "a record per element");
            Assert.True(bw.Backward().ReconstructedPulse.GetString("shape") == "spectrum", "reconstructed pulse object");
        }

        [Test(Category = "transform")]
        public void Backward_NonlinearPlate_UndoesSelfPhaseModulation()
        {
            var mat = FlatMaterial(3e-20);
            var sysJ = O("format", "ultrafast-system", "version", 1, "name", "spm");
            sysJ.Set("materials", O("Kerr", mat));
            sysJ.Set("elements", A(O("id", "plate", "type", "bulk", "material", "Kerr", "lengthM", 0.01, "beam", O("kind", "constant", "radiusM", 60e-6))));
            var sys = SystemDescription.FromJson(sysJ);
            var fw = Go(sys, SpecGrid(Gauss(Lam, 200e-15, 2e-6)));
            Assert.Greater(fw.Records[0].BIntegralRad, 2, "strong SPM (B > 2 rad)");
            Assert.Greater(RmsSpectralWidth(fw.Output), 2 * RmsSpectralWidth(fw.Input), "spectrum broadened");
            var bw = Backward(sys, fw.Output, null);
            Assert.Close(RmsSpectralWidth(fw.Input), RmsSpectralWidth(bw.Input), 0.01, 0, "input bandwidth recovered");
            Assert.Close(fw.Input.PeakPowerW(), bw.Input.PeakPowerW(), 0.01, 0, "input peak power recovered");
        }

        [Test(Category = "transform")]
        public void Backward_ActiveElements_NeedAMode()
        {
            var sys = Sys(O("id", "user1", "type", "user", "callback", "@noop"), O("id", "d", "type", "dispersion", "gddFs2", 500.0));
            var reg = UltrafastEngine.Models.ModelRegistry.CreateDefault();
            reg.RegisterCallback("noop", (UltrafastEngine.Models.ElementCallback)(c => { }));
            var fw = Go(sys, SpecGrid(Gauss(Lam, 100e-15, 1e-6)), reg);
            var spec = O("format", "ultrafast-run"); spec.Set("pulse", BackwardPropagation.PulseJson(fw.Output, "end")); spec.Set("grid", O("auto", false, "n", 4096, "windowS", 20e-12));
            spec.Set("backward", new JsonObject());
            Assert.Throws<JsonException>(() => UltrafastEngine.Simulation.Run(sys, RunSpec.FromJson(spec), reg), "refused by default");
            spec.Set("backward", O("active", "identity"));
            var bw = UltrafastEngine.Simulation.Run(sys, RunSpec.FromJson(spec), reg);
            Assert.Close(100e-15, bw.Records[0].Out != null ? PulseMetrics.Summarize(bw.Input).FwhmS : 0, 0.01, 0, "identity mode passes through; dispersion undone");
            spec.Set("backward", O("active", "linearized", "referencePulse", Gauss(Lam, 100e-15, 1e-6)));
            var bl = UltrafastEngine.Simulation.Run(sys, RunSpec.FromJson(spec), reg);
            Assert.Close(100e-15, PulseMetrics.Summarize(bl.Input).FwhmS, 0.01, 0, "linearised mode");
        }

        // ------------------------------------------------------------------ fits with transforms

        private static string WriteSpectrumCsv(string dir, RunResult r, string name)
        {
            var ins = r.Instruments.First(i => i.Name == name);
            var x = ins.Arrays.First(a => a.Name.EndsWith("_wavelength_m")).Data; var y = ins.Arrays.First(a => a.Name.EndsWith("_spectrum")).Data;
            var path = Path.Combine(dir, name + ".csv");
            using (var w = new StreamWriter(path))
            {
                w.WriteLine("wavelength_nm,intensity");
                for (int i = 0; i < x.Length; i += 4) w.WriteLine((x[i] * 1e9).ToString("R", CultureInfo.InvariantCulture) + "," + y[i].ToString("R", CultureInfo.InvariantCulture));
            }
            return path;
        }

        private static string WriteDscanNpz(string dir, RunResult r, string name)
        {
            var ins = r.Instruments.First(i => i.Name == name);
            var path = Path.Combine(dir, name + ".npz");
            var g = ins.Arrays.First(a => a.Name.EndsWith("_gdd_s2")); var l = ins.Arrays.First(a => a.Name.EndsWith("_shg_wavelength_m")); var t = ins.Arrays.First(a => a.Name.EndsWith("_trace"));
            NpzWriter.Write(path, new[] { new NdArray("gdd_s2", g.Data, g.Data.Length), new NdArray("shg_wavelength_m", l.Data, l.Data.Length), new NdArray("trace", t.Data, t.Shape) });
            return path;
        }

        private static SystemDescription FilterSystem(JsonValue fwhm, JsonValue center) =>
            Sys(Tr("filter", O("kind", "spectralAmplitude", "shape", "gaussian", "fwhmNm", fwhm, "centerWavelengthNm", center)));

        [Test(Category = "transform")]
        public void Fit_TransformFlags_RecoverAFilterFromASpectrum()
        {
            var dir = TempDir();
            var pulse = Gauss(Lam, 100e-15, 1e-6);
            var truth = Go(FilterSystem(8.0, 1034.0), Spec(pulse, O("instruments", A(O("kind", "spectrometer", "name", "spec", "resolutionM", 0.2e-9)))));
            WriteSpectrumCsv(dir, truth, "spec");
            var sys = FilterSystem(FitV(12, 2, 30), FitV(1030, 1020, 1040));
            var run = RunSpec.FromJson(O("format", "ultrafast-run", "pulse", pulse, "fit", new JsonObject(),
                "instruments", A(O("kind", "spectrometer", "name", "spec", "resolutionM", 0.2e-9, "measured", O("file", "spec.csv", "sigma", 0.01)))), dir);
            var r = Go(sys, run);
            var f = r.Fit();
            Assert.Equal(2, f.Parameters.Count, "two marked parameters, no paths given");
            Assert.Close(8.0, f.Parameters.First(p => p.Name.EndsWith("fwhmNm")).Value, 0.01, 0, "FWHM recovered");
            Assert.Close(1034.0, f.Parameters.First(p => p.Name.EndsWith("centerWavelengthNm")).Value, 1e-4, 0, "centre recovered");
            Assert.True(f.Parameters.All(p => p.Origin == "transform"), "origin");
            // three layers, influence, provenance, fitted system
            var j = f.ToJson();
            var layers = j.GetObject("layers");
            Assert.Greater(layers.GetObject("simulated").GetDouble("chi2", 0), layers.GetObject("inferred").GetDouble("chi2", 1), "the fit improves on the description");
            Assert.True(layers.GetObject("believed").GetObject("parameters").Count() == 2, "believed parameters");
            Assert.Close(1.0, f.Influence.Values.First()["spec"], 1e-9, 0, "all information from the spectrometer");
            Assert.True(j.GetObject("provenance").GetArray("data")[0].AsObject().GetString("sha256", "").Length == 64, "data file hashed");
            r.WriteAll(Path.Combine(dir, "out"));
            var fitted = SystemDescription.Load(Path.Combine(dir, "out", "fitted-system.ufs"));
            var op = fitted.Elements[0].Json.GetArray("operators")[0].AsObject();
            Assert.True(op["fwhmNm"].Kind == JsonKind.Number, "fit objects replaced by numbers");
            Assert.Close(8.0, op.GetDouble("fwhmNm", 0), 0.01, 0, "fitted value in the system file");
            // apply the fitted transform to a new input
            var r2 = Go(fitted, Spec(Gauss(Lam, 50e-15, 1e-6)));
            Assert.Less(r2.Output.TotalEnergyJ(), r2.Input.TotalEnergyJ() * 0.6, "the fitted filter acts on a new, broader input");
            Assert.True(File.Exists(Path.Combine(dir, "out", "fitted-run.json")), "fitted run written");
        }

        [Test(Category = "transform")]
        public void Fit_DScan_RecoversGddAndTod()
        {
            var dir = TempDir();
            var pulse = Gauss(800e-9, 20e-15, 1e-9);
            var ins = O("kind", "dscan", "name", "ds", "gddRangeS2", new[] { -600e-30, 600e-30 }, "steps", 41);
            var truth = Go(Sys(Tr("residual", O("kind", "spectralPhase", "gddFs2", 150.0, "todFs3", 1500.0))), Spec(pulse, O("instruments", A(ins))));
            WriteDscanNpz(dir, truth, "ds");
            var sys = Sys(Tr("residual", O("kind", "spectralPhase", "gddFs2", FitV(0, -400, 400), "todFs3", FitV(0, -5000, 5000))));
            var mi = (JsonObject)JsonValue.Parse(ins.ToJson(false)); mi.Set("measured", O("file", "ds.npz", "sigma", 0.01));
            var r = Go(sys, RunSpec.FromJson(O("format", "ultrafast-run", "pulse", pulse, "fit", O("maxIterations", 30), "instruments", A(mi)), dir));
            var f = r.Fit();
            Assert.Close(150, f.Parameters[0].Value, 0.02, 1, "GDD (sign resolved by the d-scan)");
            Assert.Close(1500, f.Parameters[1].Value, 0.05, 30, "TOD");
            Assert.Less(f.Parameters[0].Sigma, 10, "GDD error bar");
        }

        [Test(Category = "transform")]
        public void Fit_Prior_ConstrainsAnUnmeasuredParameter_AndShowsInTheInfluenceMap()
        {
            var dir = TempDir();
            var pulse = Gauss(Lam, 100e-15, 1e-6);
            var truth = Go(FilterSystem(8.0, 1034.0), Spec(pulse, O("instruments", A(O("kind", "spectrometer", "name", "spec")))));
            WriteSpectrumCsv(dir, truth, "spec");
            // a delay operator does not change the spectrum: only its prior determines it
            var sys = Sys(Tr("t", O("kind", "spectralAmplitude", "shape", "gaussian", "fwhmNm", FitV(10, 2, 30), "centerWavelengthNm", 1034.0),
                               O("kind", "delay", "delayFs", O("value", 0.0, "fit", true, "prior", 50.0))));
            var r = Go(sys, RunSpec.FromJson(O("format", "ultrafast-run", "pulse", pulse, "fit", new JsonObject(),
                "instruments", A(O("kind", "spectrometer", "name", "spec", "measured", O("file", "spec.csv", "sigma", 0.01)))), dir));
            var f = r.Fit();
            var d = f.Parameters.First(p => p.Name.Contains("delayFs"));
            Assert.Close(50, d.Sigma, 0.02, 0, "sigma = prior sigma");
            Assert.Close(1.0, f.Influence[d.Name]["priors"], 1e-6, 0, "determined by its prior only");
            Assert.True(r.Warnings.Any(w => w.Contains("only by its prior")), "warning names the prior");
        }

        [Test(Category = "transform")]
        public void Fit_InstrumentResolution_IsAFitParameter()
        {
            var dir = TempDir();
            var pulse = Gauss(Lam, 300e-15, 1e-6);   // ~5 nm spectrum
            var truth = Go(Sys(), Spec(pulse, O("instruments", A(O("kind", "spectrometer", "name", "spec", "resolutionM", 3e-9)))));
            WriteSpectrumCsv(dir, truth, "spec");
            var r = Go(Sys(), RunSpec.FromJson(O("format", "ultrafast-run", "pulse", pulse,
                "fit", O("parameters", A(O("path", "instrument:spec/resolutionM", "initial", 1e-9, "min", 0.1e-9, "max", 8e-9))),
                "instruments", A(O("kind", "spectrometer", "name", "spec", "resolutionM", 1e-9, "measured", O("file", "spec.csv", "sigma", 0.005)))), dir));
            Assert.Close(3e-9, r.Fit().Parameters[0].Value, 0.03, 0, "spectrometer resolution recovered");
        }

        [Test(Category = "transform")]
        public void Fit_CmaEs_FindsTheGlobalMinimum()
        {
            var dir = TempDir();
            var pulse = Gauss(Lam, 100e-15, 1e-6);
            var truth = Go(FilterSystem(8.0, 1034.0), Spec(pulse, O("instruments", A(O("kind", "spectrometer", "name", "spec")))));
            WriteSpectrumCsv(dir, truth, "spec");
            // start far away on the other side of the spectrum (LM from here reaches the same answer only slowly)
            var sys = FilterSystem(FitV(25, 2, 30), FitV(1022, 1015, 1045));
            var r = Go(sys, RunSpec.FromJson(O("format", "ultrafast-run", "pulse", pulse, "fit", O("estimator", "cmaes", "maxIterations", 40, "seed", 3),
                "instruments", A(O("kind", "spectrometer", "name", "spec", "measured", O("file", "spec.csv", "sigma", 0.01)))), dir));
            var f = r.Fit();
            Assert.Equal("cmaes", f.Estimator, "estimator");
            Assert.Close(1034.0, f.Parameters[1].Value, 1e-4, 0, "centre");
            Assert.Close(8.0, f.Parameters[0].Value, 0.01, 0, "FWHM");
        }

        [Test(Category = "transform", Reference = "known truth inside the 95 % intervals")]
        public void Fit_Mcmc_GivesCredibleIntervalsAroundTheTruth()
        {
            var dir = TempDir();
            var pulse = Gauss(Lam, 100e-15, 1e-6);
            var truth = Go(FilterSystem(8.0, 1034.0), Spec(pulse, O("instruments", A(O("kind", "spectrometer", "name", "spec", "points", 256)))));
            WriteSpectrumCsv(dir, truth, "spec");
            var sys = FilterSystem(FitV(9, 2, 30), FitV(1033, 1020, 1040));
            var r = Go(sys, RunSpec.FromJson(O("format", "ultrafast-run", "pulse", pulse, "grid", O("auto", false, "n", 1024, "windowS", 8e-12),
                "fit", O("estimator", "mcmc", "walkers", 8, "steps", 60, "freezeGrid", false),
                "instruments", A(O("kind", "spectrometer", "name", "spec", "points", 256, "measured", O("file", "spec.csv", "sigma", 0.02)))), dir));
            var f = r.Fit();
            Assert.Greater(f.Samples, 100, "posterior samples");
            foreach (var p in f.Parameters)
            {
                double t = p.Name.EndsWith("fwhmNm") ? 8.0 : 1034.0;
                Assert.True(p.Lo95 <= t && t <= p.Hi95, p.Name + ": 95 % interval [" + p.Lo95 + ", " + p.Hi95 + "] contains " + t);
                Assert.Less(p.Hi95 - p.Lo95, t * 0.05, p.Name + ": interval is informative");
            }
        }

        [Test(Category = "transform")]
        public void Fit_SmoothSplinePhase_FromAFrog()
        {
            var dir = TempDir();
            var pulse = Gauss(800e-9, 25e-15, 1e-9);
            var frog = O("kind", "frog", "name", "frog", "points", 128);
            var truth = Go(Sys(Tr("t", O("kind", "spectralPhase", "gddFs2", 200.0))), Spec(pulse, O("instruments", A(frog))));
            var ins = truth.Instruments.First();
            var path = Path.Combine(dir, "frog.npz");
            NpzWriter.Write(path, new[] { new NdArray("delay_s", ins.Arrays.First(a => a.Name.EndsWith("_delay_s")).Data, 128), new NdArray("wavelength_m", ins.Arrays.First(a => a.Name.EndsWith("_wavelength_m")).Data, 128), new NdArray("trace", ins.Arrays.First(a => a.Name.EndsWith("_trace")).Data, 128, 128) });
            // 7 knots of free phase (covering the spectrum to its wings) with smoothness; GDD sign is ambiguous for SHG FROG, so compare the trace error only
            var knots = O("wavelengthsNm", new[] { 720.0, 750.0, 775.0, 800.0, 825.0, 855.0, 890.0 }, "phaseRad", new[] { 2.0, 1.0, 0.3, 0.0, 0.3, 1.0, 2.0 });   // not zero: at a flat phase the trace does not depend on the sign of the chirp (zero gradient)
            var sys = Sys(Tr("t", O("kind", "spectralPhase", "knots", knots, "fit", A("knots.phaseRad"), "bounds", O("knots.phaseRad", new[] { -10.0, 10.0 }), "smoothness", 0.01)));
            var mf = (JsonObject)JsonValue.Parse(frog.ToJson(false)); mf.Set("measured", O("file", "frog.npz", "sigma", 0.01));
            var r = Go(sys, RunSpec.FromJson(O("format", "ultrafast-run", "pulse", pulse, "fit", O("maxIterations", 25), "instruments", A(mf)), dir));
            var f = r.Fit();
            Assert.Equal(7, f.Parameters.Count, "one parameter per knot");
            Assert.Less(f.PerInstrument["frog"].RmsAfter, 0.25 * f.PerInstrument["frog"].RmsBefore, "the FROG error drops by more than 4x");
        }
    }
}
