using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UltrafastEngine.Plugins.Fitting;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Memory safety of the fitting plugin: the residual cache keeps numbers, not results; only the latest and best results
    /// (and the Jacobian runs' readings) stay alive. docs/reference/memory.md.</summary>
    [TestArea("Memory safety: fitting")]
    public class FittingMemoryTests
    {
        private const double Lam = 1030e-9;
        private static readonly JsonObject Grid = O("auto", false, "n", 4096, "windowS", 20e-12);
        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static JsonObject Filter(JsonValue fwhm, JsonValue centre)
        {
            var o = O("id", "filter", "type", "transform");
            o.Set("operators", new JsonArray(new[] { O("kind", "spectralAmplitude", "shape", "gaussian", "fwhmNm", fwhm, "centerWavelengthNm", centre) }));
            return o;
        }

        private static void WriteSpectrum(string dir, RunResult r, string file, double noise, int seed)
        {
            var ins = r.Instruments.First(i => i.Name == "spec");
            var x = ins.Arrays.First(a => a.Name.EndsWith("_wavelength_m")).Data; var y = ins.Arrays.First(a => a.Name.EndsWith("_spectrum")).Data;
            double max = y.Max(); var rnd = new Random(seed);
            using (var w = new StreamWriter(Path.Combine(dir, file)))
            {
                w.WriteLine("wavelength_nm,intensity");
                for (int i = 0; i < x.Length; i += 4)
                {
                    double g = Math.Sqrt(-2 * Math.Log(1 - rnd.NextDouble())) * Math.Cos(2 * Math.PI * rnd.NextDouble());
                    w.WriteLine(R(x[i] * 1e9) + "," + R(y[i] + noise * max * g));
                }
            }
        }

        /// <summary>A two-parameter spectral-filter fit on noisy data (many iterations); returns the run spec and system.</summary>
        private static (SystemDescription, RunSpec) Case(JsonObject fitExtra)
        {
            var dir = Path.Combine(Path.GetTempPath(), "uf-mem-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(dir);
            var spec = O("kind", "spectrometer", "name", "spec", "resolutionM", 0.2e-9);
            var truth = Go(Sys(Filter(8.0, 1034.0)), Spec(Gauss(Lam, 100e-15, 1e-6), O("grid", Grid, "instruments", A(spec))));
            WriteSpectrum(dir, truth, "a.csv", 0.02, 3);
            var sys = Sys(Filter(O("value", 12.0, "fit", true, "min", 2.0, "max", 30.0), O("value", 1030.0, "fit", true, "min", 1020.0, "max", 1040.0)));
            var meas = O("kind", "spectrometer", "name", "spec", "resolutionM", 0.2e-9); meas.Set("measured", O("file", "a.csv", "sigma", 0.02));
            var fit = fitExtra ?? O();
            var run = RunSpec.FromJson(O("format", "ultrafast-run", "pulse", Gauss(Lam, 100e-15, 1e-6), "grid", Grid, "fit", fit, "instruments", A(meas)), dir);
            return (sys, run);
        }

        [Test(Reference = "values recorded from the fit before the memory changes (the cache and slimming must not change a number)", Tolerance = "bit-identical")]
        public void Fit_ResultIsBitIdenticalToTheUnboundedCacheVersion()
        {
            var (sys, run) = Case(O("freezeGrid", false));
            var f = Go(sys, run).Fit();
            Assert.Equal(28, f.Evaluations, "evaluations");
            Assert.Equal(6, f.Iterations, "iterations");
            Assert.True(f.Chi2Final == 476.58415279612495, "chi2 final " + R(f.Chi2Final));
            Assert.True(f.Chi2Initial == 56629.0641215897, "chi2 initial " + R(f.Chi2Initial));
            Assert.True(f.Parameters[0].Value == 8.01494160115412 && f.Parameters[0].Sigma == 0.035950718383509626, "fwhm " + R(f.Parameters[0].Value) + " " + R(f.Parameters[0].Sigma));
            Assert.True(f.Parameters[1].Value == 1034.0033467997512 && f.Parameters[1].Sigma == 0.015543084960170995, "centre " + R(f.Parameters[1].Value) + " " + R(f.Parameters[1].Sigma));
            var e = f.Predictions["filter.energyOutJ"]; Assert.True(e.Value == 3.939054203647169E-07 && e.Sigma == 1.3530276447532626E-09, "energy " + R(e.Value) + " " + R(e.Sigma));
            var w = f.Predictions["filter.peakPowerW"]; Assert.True(w.Value == 1682652.9454072735 && w.Sigma == 11657.56909664698, "peak power " + R(w.Value) + " " + R(w.Sigma));
            var d = f.Predictions["spec.rmsWidthM"]; Assert.True(d.Value == 3.035890050528034E-09 && d.Sigma == 1.0801380721154777E-11, "rms width " + R(d.Value) + " " + R(d.Sigma));
        }

        [Test(Reference = "a fit keeps the latest and the best result only (the old residual cache kept up to 65)", Tolerance = "at most 4 full results alive at any time, with 28 evaluations")]
        public void Fit_ManyEvaluations_KeepABoundedNumberOfFullResults()
        {
            var (sys, run) = Case(O("freezeGrid", false));
            var refs = new List<WeakReference[]>();
            int maxAlive = 0;
            Fitter.EvaluationObserver = res =>
            {
                refs.Add(res.At.Values.Select(p => (object)p.Ax).Concat(new object[] { res.Output.Ax }).Select(o => new WeakReference(o)).ToArray());
                if (refs.Count < 6) return;   // let the early evaluations pass; the bound matters once many have run
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                maxAlive = Math.Max(maxAlive, refs.Count(r => r.Any(w => w.IsAlive)));
            };
            RunResult result;
            try { result = Go(sys, run); } finally { Fitter.EvaluationObserver = null; }
            Assert.Greater(refs.Count, 20, "evaluations observed");
            Assert.Less(maxAlive, 5, "evaluations whose pulses are still alive (current, latest, best, and one being replaced)");
            Assert.True(result.Fit().Evaluations == refs.Count, "every evaluation was observed");
        }
    }
}
