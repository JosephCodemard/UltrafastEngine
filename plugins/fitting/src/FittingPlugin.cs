using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using UltrafastEngine.Engine;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Schema;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Plugins.Fitting
{
    /// <summary>
    /// The fitting plugin (plugins/fitting): the "measured" instrument key (comparison with recorded CSV / NPZ /
    /// JSON data), the "fit" run mode (calibration with uncertainties and predictions), their summary sections,
    /// fit.json, and the CLI reports. Docs: plugins/fitting/README.md.
    /// </summary>
    public sealed class FittingPlugin : IPlugin
    {
        public string Id => "fitting";
        public string Name => "Fitting: measured data and calibration";
        public string Version => "2.1.8-beta";
        public string Description => "Compares instruments with recorded data (\"measured\") and calibrates parameters with error bars (\"fit\").";

        public void Register(PluginContext ctx)
        {
            ctx.RegisterInstrumentHook(new MeasuredHook());
            ctx.RegisterRunMode(new FitMode());
            ctx.RegisterResultExtension(new FitResults());
            ctx.RegisterReporter(new FitReporter());
            ctx.RegisterSpecKeys(new ComponentSpec("instrumentCommon", "fitting")
                .Obj("measured", "Recorded data to compare this instrument with: a file name, or an object (file, columns, normalize, sigma, background, rangeX, units, values, weight). Fitting plugin.", "fitting:measured"));
            ctx.RegisterSpecKeys(new ComponentSpec("runTop", "fitting")
                .Obj("fit", "Calibrate parameters against every instrument with measured data (fitting plugin).", "fitting:fit"));
            ctx.RegisterSpecKeys(new ComponentSpec("fitting:measured", "", "Recorded data for one instrument (fitting plugin).")
                .Str("file", "CSV, NPZ or JSON file, relative to the run file")
                .Free("columns", ParamType.Object, "Column / array names: x, y, sigma (curves); rows, cols, trace (traces)")
                .Enum("normalize", new[] { "peak", "maximum", "area", "scale", "none" }, "How data and simulation are normalised before comparing", "peak")
                .Num("sigma", "", "Uncertainty of each point relative to the data peak", "0.02")
                .P("background", ParamType.Any, "", "A number to subtract, or \"auto\" (median at the axis edges)")
                .Arr("rangeX", "SI", "Compare only this [min, max] of the measured axis")
                .Str("xUnit", "Unit of the x column (overrides the name suffix)").Str("yUnit", "Unit of the y column")
                .Str("rowUnit", "Unit of the trace rows").Str("colUnit", "Unit of the trace columns")
                .Free("values", ParamType.Object, "Scalar readings {name: value | [value, sigma]}")
                .Num("valueSigma", "", "Relative uncertainty of values given without one", "0.02")
                .Num("valueWeight", "", "Extra weight of the scalar residuals", "1")
                .Num("weight", "", "Weight of this instrument in the fit's chi^2", "1")
                .Free("simulated", ParamType.Object, "Which arrays of the instrument to compare with: x, y | rows, cols, trace"));
            ctx.RegisterSpecKeys(new ComponentSpec("fitting:fit", "", "Calibration fit (fitting plugin).")
                .ListOf("parameters", "fitting:parameter", "The parameters to fit", required: true)
                .Int("maxIterations", "Levenberg-Marquardt iterations", "20")
                .Num("tolerance", "", "Relative chi^2 change that ends the fit", "1e-4")
                .Bool("freezeGrid", "Fix the time/frequency grid (2x the planned bandwidth) for all evaluations", "true"));
            ctx.RegisterSpecKeys(new ComponentSpec("fitting:parameter", "", "One fit parameter.")
                .Str("path", "pulse/..., element:ID/..., material:NAME/prop, run/..., system/...", required: true)
                .Str("name", "Label", "the path").Num("initial", "", "Start value", "the value in the files")
                .Num("min", "", "Lower bound").Num("max", "", "Upper bound")
                .Num("scale", "", "Typical change", "(max - min) / 4").Num("step", "", "Finite-difference step", "1e-3 x scale")
                .Str("unit", "Unit label for reports"));
        }
    }

    /// <summary>Typed access to the plugin's data on a <see cref="RunResult"/>.</summary>
    public static class FittingResults
    {
        internal const string ComparisonsKey = "fitting.comparisons", FitKey = "fitting.fit";

        /// <summary>Instruments compared with recorded data ("measured").</summary>
        public static List<Comparison> Comparisons(this RunResult r)
        {
            if (!r.Extensions.TryGetValue(ComparisonsKey, out var o)) r.Extensions[ComparisonsKey] = o = new List<Comparison>();
            return (List<Comparison>)o;
        }

        /// <summary>The calibration fit, or null if the run was not a fit.</summary>
        public static FitReport Fit(this RunResult r) => r.Extensions.TryGetValue(FitKey, out var o) ? (FitReport)o : null;
        public static void SetFit(this RunResult r, FitReport f) => r.Extensions[FitKey] = f;
    }

    internal sealed class MeasuredHook : IInstrumentHook
    {
        public IReadOnlyList<string> Keys { get; } = new[] { "measured" };
        public void AfterMeasure(InstrumentHookContext h)
        {
            if (!h.Spec.TryGet("measured", out var m)) return;
            h.Result.Comparisons().Add(Measurements.Compare(h.Output, m, h.BaseDirectory));
        }
    }

    internal sealed class FitMode : IRunMode
    {
        public string Key => "fit";
        public string Description => "calibrate parameters against the measured instruments";
        public RunResult Run(SystemDescription system, RunSpec run, ModelRegistry registry, ProgressCallback progress, CancellationToken cancel)
            => Fitter.Run(system, run, registry, progress, cancel);
    }

    internal sealed class FitResults : IResultExtension
    {
        public void ContributeSummary(RunResult r, JsonObject o)
        {
            var comps = r.Comparisons();
            if (comps.Count > 0)
            {
                o.Set("comparisons", new JsonArray(comps.Select(c => (JsonValue)c.ToJson())));
                o.Set("measuredChi2", comps.Sum(c => c.Chi2));
            }
            var f = r.Fit();
            if (f != null) o.Set("fit", f.ToJson());
        }

        public void Write(RunResult r, string directory)
        {
            var f = r.Fit();
            if (f != null) File.WriteAllText(Path.Combine(directory, "fit.json"), f.ToJson().ToJson(true));
        }
    }

    internal sealed class FitReporter : IRunReporter
    {
        public void Report(RunResult res, TextWriter w)
        {
            var ci = CultureInfo.InvariantCulture;
            var f = res.Fit();
            if (f != null)
            {
                w.WriteLine(string.Format(ci, "  fit: chi^2 {0:G4} -> {1:G4} ({2} points, reduced {3:G3}), {4} iterations, {5} runs{6}", f.Chi2Initial, f.Chi2Final, f.Points, f.ReducedChi2, f.Iterations, f.Evaluations, f.Converged ? "" : " (not converged)"));
                foreach (var p in f.Parameters)
                    w.WriteLine(string.Format(ci, "  {0,-34} {1,14:G6} +- {2,-12:G3} (start {3:G6}){4}", p.Name, p.Value, p.Sigma, p.Initial, p.HitBound ? "  AT BOUND" : ""));
                foreach (var kv in f.PerInstrument)
                    w.WriteLine(double.IsNaN(kv.Value.RmsAfter)
                        ? string.Format(ci, "  {0,-34} chi^2 {1:G3} -> {2:G3}", kv.Key, kv.Value.Before, kv.Value.After)
                        : string.Format(ci, "  {0,-34} rms error {1:P2} -> {2:P2}", kv.Key, kv.Value.RmsBefore, kv.Value.RmsAfter));
                return;
            }
            var comps = res.Comparisons();
            if (comps.Count == 0) return;
            w.WriteLine("  measured data:");
            foreach (var c in comps)
            {
                string head = string.Format(ci, "    {0,-22} {1,-14} chi^2 {2,-10:G4} ({3} points)", c.Instrument, c.Kind, c.Chi2, c.Points);
                if (!double.IsNaN(c.RmsError)) head += string.Format(ci, "  rms error {0:P2}", c.RmsError);
                w.WriteLine(head);
                foreach (var v in c.Values)
                    w.WriteLine(string.Format(ci, "      {0}: measured {1:G4} +- {2:G2}, simulated {3:G4} ({4:+0.0;-0.0} sigma)", v.Name, v.Measured, v.Sigma, v.Simulated, (v.Simulated - v.Measured) / v.Sigma));
            }
        }
    }
}
