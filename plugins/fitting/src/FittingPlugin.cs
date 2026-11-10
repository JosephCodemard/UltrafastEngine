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
using UltrafastEngine.Measured;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Plugins.Fitting
{
    /// <summary>
    /// The fitting plugin (plugins/fitting): fittable "transform" elements, the "fit" run mode (calibration with
    /// uncertainties and predictions against the instruments' recorded data -- the "measured" key, which is part of the
    /// core, docs/reference/measured-data.md), backward propagation, their summary sections, fit.json, and the CLI
    /// reports. Docs: plugins/fitting/README.md.
    /// </summary>
    public sealed class FittingPlugin : IPlugin
    {
        public const string PluginVersion = "2.2.0";
        public string Id => "fitting";
        public string Name => "Fitting: transforms, fits and backward propagation";
        public string Version => PluginVersion;
        public string Description => "Fittable \"transform\" elements, fits to recorded data with replaceable estimators (\"fit\") and backward propagation (\"backward\").";

        public void Register(PluginContext ctx)
        {
            var specs = TransformElement.Specs().ToList();
            ctx.RegisterElement("transform", TransformElement.Compile, specs[0]);
            foreach (var sp in specs.Skip(1)) ctx.RegisterSpecKeys(sp);
            ctx.RegisterRunMode(new FitMode());
            ctx.RegisterRunMode(new BackwardMode());
            ctx.RegisterResultExtension(new FitResults());
            ctx.RegisterReporter(new FitReporter());
            ctx.RegisterSpecKeys(new ComponentSpec("runTop", "fitting")
                .Obj("fit", "Fit transform parameters (and any listed number) to every instrument with measured data (fitting plugin).", "fitting:fit")
                .Obj("backward", "Backward propagation: the pulse is the field measured downstream; find the field at every plane upstream (fitting plugin).", "fitting:backward"));
            ctx.RegisterSpecKeys(new ComponentSpec("fitting:backward", "", "Backward propagation (fitting plugin).").DocAt("plugins/fitting.md#backward-propagation")
                .Str("from", "Plane where the run's pulse was measured: an element id (after it) or \"end\"", "\"end\"")
                .Str("to", "Plane to propagate back to: an element id (after it) or \"input\"", "\"input\"")
                .Num("regularization", "", "Relative Tikhonov level for inverting linear terms (transmission below this x the peak is not recovered)", "1e-3")
                .Enum("active", new[] { "error", "linearized", "identity" }, "Amplifiers, crystals and spatial elements: refuse, undo as the filter they were in a forward reference run, or pass through", "error")
                .Obj("referencePulse", "active = linearized: the input pulse of the forward reference run", "pulse"));
            ctx.RegisterSpecKeys(new ComponentSpec("fitting:fit", "", "Fit (fitting plugin).").DocAt("plugins/fitting.md#fitting")
                .ListOf("parameters", "fitting:parameter", "Numbers to fit by path (in addition to the transform parameters marked for fitting)")
                .Enum("estimator", new[] { "levenbergMarquardt", "cmaes", "mcmc" }, "Fitting algorithm (plugins may register more)", "levenbergMarquardt")
                .Int("maxIterations", "Iterations (LM) / generations (CMA-ES)", "20 (LM), 60 (CMA-ES)")
                .Num("tolerance", "", "Relative chi^2 change (LM) / cost spread (CMA-ES) that ends the fit", "1e-4 (LM), 1e-6 (CMA-ES)")
                .ListOf("conditions", "fitting:condition", "Operating conditions fitted together with one set of shared parameters (energies, angles, ...); empty: one condition, the run itself")
                .ListOf("surfaces", "fitting:surface", "Smooth surfaces: several numbers (e.g. the knots of a spectral phase) as Chebyshev series of a condition variable, with smoothness penalties in both directions")
                .Bool("freezeGrid", "Fix the time/frequency grid (2x the planned bandwidth) for all evaluations", "true")
                .Int("parallel", "Simulations run at the same time (Jacobian columns, CMA-ES population, MCMC walkers)", "1")
                .Int("population", "CMA-ES population", "4 + 3 ln n").Num("sigma0", "", "CMA-ES initial step (in units of each parameter's scale)", "0.3")
                .Bool("polish", "CMA-ES: finish with Levenberg-Marquardt", "true")
                .Int("walkers", "MCMC walkers", "max(2n + 2, 8)").Int("steps", "MCMC steps", "200").Num("burnIn", "", "MCMC burn-in fraction", "0.5")
                .Int("thin", "MCMC thinning", "1").Enum("start", new[] { "optimum", "initial" }, "MCMC start: a Levenberg-Marquardt optimum or the initial values", "optimum")
                .Num("spread", "", "MCMC starting ball (in units of each scale)", "0.1").Int("seed", "Random seed (CMA-ES, MCMC)", "1"));
            ctx.RegisterSpecKeys(new ComponentSpec("fitting:condition", "", "One operating condition of a multi-condition fit (fitting plugin).").DocAt("plugins/fitting.md#fitting-across-conditions")
                .Str("name", "Label (unique); used in the report as condition/instrument", required: true)
                .Bool("heldOut", "Not in the cost: predicted from the fit and compared with its data afterwards", "false")
                .Num("weight", "", "Weight of this condition's residuals in the cost (chi^2 is multiplied by it)", "1")
                .P("variables", ParamType.Any, "", "Named numbers describing the condition ({\"energyJ\": 2e-6}); the inputs of parametric and smooth dependence on the condition")
                .P("set", ParamType.Any, "", "Path -> value written into the files for this condition (a number, or a string such as instrument:spec/measured/file); same paths as fit.parameters"));
            ctx.RegisterSpecKeys(new ComponentSpec("fitting:dependence", "", "How a fitted number depends on the condition (fitting plugin).").DocAt("plugins/fitting.md#fitting-across-conditions")
                .Enum("kind", new[] { "constant", "proportional", "linear", "polynomial", "power", "chebyshev", "network" }, "Form: c0 | c0 u | c0 + c1 u | sum ck u^k | c0 u^c1 | sum ck Tk(xi) | a small tanh network with built-in constraints; u = variable / reference", "linear")
                .Str("variable", "Name of the condition variable (conditions[].variables)", required: true)
                .Int("degree", "polynomial / chebyshev: highest order", "2")
                .Num("reference", "", "u = variable / reference", "mean of the training values")
                .Free("range", ParamType.Array, "chebyshev: [lo, hi] mapped to xi = -1..1 (outside it the series extrapolates)")
                .Num("smoothnessPaths", "", "surface: weight on second differences across the paths", "0").Num("smoothnessVariable", "", "surface (chebyshev): weight on second differences across the variable", "0")
                .Free("coefficients", ParamType.Array, "Per order: {initial, min, max, scale, prior, step} of that coefficient (network: one object {scale, min, max} for all weights)")
                .Int("hidden", "network: tanh units", "4").Enum("output", new[] { "none", "bounded", "positive" }, "network: output constraint (bounded: lo + (hi - lo) sigmoid; positive: softplus), exact by construction", "none")
                .Free("outputRange", ParamType.Array, "network, output bounded: [lo, hi]").Enum("monotone", new[] { "none", "increasing", "decreasing" }, "network: output monotone in the variable (squared weights), exact by construction", "none")
                .Num("weightDecay", "", "network: L2 penalty on the weights (a prior of sigma 1/sqrt(weightDecay))", "1e-4"));
            ctx.RegisterSpecKeys(new ComponentSpec("fitting:surface", "", "A smooth surface of several numbers over a condition variable (fitting plugin).").DocAt("plugins/fitting.md#fitting-across-conditions")
                .Str("name", "Label", required: true)
                .Free("paths", ParamType.Array, "The numbers (same paths as fit.parameters); or pathTemplate + count")
                .Str("pathTemplate", "A path with {i} replaced by 0..count-1 (e.g. element:t/operators/0/knots/phaseRad/{i})").Int("count", "Number of paths for pathTemplate")
                .Enum("kind", new[] { "constant", "proportional", "linear", "polynomial", "chebyshev", "network" }, "Basis over the variable", "chebyshev")
                .Str("variable", "Name of the condition variable", required: true).Int("degree", "Highest order", "2")
                .Num("reference", "", "u = variable / reference").Free("range", ParamType.Array, "[lo, hi] of the Chebyshev map")
                .Num("smoothnessPaths", "", "Weight on second differences across the paths", "0").Num("smoothnessVariable", "", "Weight on second differences across the variable", "0")
                .Free("coefficients", ParamType.Array, "Per order: {initial, min, max, scale, prior, step}")
                .Int("hidden", "network: tanh units", "4").Enum("output", new[] { "none", "bounded", "positive" }, "network: output constraint (bounded: lo + (hi - lo) sigmoid; positive: softplus), exact by construction", "none")
                .Free("outputRange", ParamType.Array, "network, output bounded: [lo, hi]").Enum("monotone", new[] { "none", "increasing", "decreasing" }, "network: output monotone in the variable (squared weights), exact by construction", "none")
                .Num("weightDecay", "", "network: L2 penalty on the weights (a prior of sigma 1/sqrt(weightDecay))", "1e-4"));
            ctx.RegisterSpecKeys(new ComponentSpec("fitting:parameter", "", "One fit parameter.")
                .Str("path", "pulse/..., element:ID/..., instrument:NAME/..., material:NAME/prop, run/..., system/...", required: true)
                .Str("name", "Label", "the path").Num("initial", "", "Start value", "the value in the files")
                .Num("min", "", "Lower bound").Num("max", "", "Upper bound")
                .Num("scale", "", "Typical change", "(max - min) / 4").Num("step", "", "Finite-difference step", "1e-3 x scale")
                .Str("unit", "Unit label for reports")
                .Obj("dependence", "The value follows the condition variable: its coefficients are fitted instead (fitting across conditions)", "fitting:dependence")
                .Bool("perCondition", "Multi-condition fit: one value per training condition instead of one shared value", "false")
                .P("prior", ParamType.Any, "", "Gaussian prior: sigma (mean = initial) or {mean, sigma}"));
        }
    }

    /// <summary>Typed access to the plugin's data on a <see cref="RunResult"/>.</summary>
    public static class FittingResults
    {
        internal const string FitKey = "fitting.fit";

        /// <summary>Instruments compared with recorded data ("measured"): the same list as <see cref="RunResult.Comparisons"/>
        /// (measured data is part of the core; kept for code written against earlier versions of the plugin).</summary>
        public static List<Comparison> Comparisons(this RunResult r) => r.Comparisons;

        /// <summary>The calibration fit, or null if the run was not a fit.</summary>
        public static FitReport Fit(this RunResult r) => r.Extensions.TryGetValue(FitKey, out var o) ? (FitReport)o : null;
        public static void SetFit(this RunResult r, FitReport f) => r.Extensions[FitKey] = f;
    }

    internal sealed class FitMode : IRunMode
    {
        public string Key => "fit";
        public string Description => "calibrate parameters against the measured instruments";
        public RunResult Run(SystemDescription system, RunSpec run, ModelRegistry registry, ProgressCallback progress, CancellationToken cancel)
            => Fitter.Run(system, run, registry, progress, cancel);
    }

    internal sealed class BackwardMode : IRunMode
    {
        public string Key => "backward";
        public string Description => "propagate a measured field backward through the system";
        public RunResult Run(SystemDescription system, RunSpec run, ModelRegistry registry, ProgressCallback progress, CancellationToken cancel)
            => BackwardPropagation.Run(system, run, registry, progress, cancel);
    }

    internal sealed class FitResults : IResultExtension
    {
        public void ContributeSummary(RunResult r, JsonObject o)
        {
            var f = r.Fit();
            if (f != null) o.Set("fit", f.ToJson());
            var b = r.Backward();
            if (b != null) o.Set("backward", b.ToJson());
        }

        public void Write(RunResult r, string directory)
        {
            var f = r.Fit();
            if (f != null)
            {
                File.WriteAllText(Path.Combine(directory, "fit.json"), f.ToJson().ToJson(true));
                if (f.FittedSystem != null) File.WriteAllText(Path.Combine(directory, "fitted-system.ufs"), f.FittedSystem.ToJson(true));
                if (f.FittedRun != null) File.WriteAllText(Path.Combine(directory, "fitted-run.json"), f.FittedRun.ToJson(true));
                if (f.FittedConditions != null) File.WriteAllText(Path.Combine(directory, "fitted-conditions.json"), f.FittedConditions.ToJson(true));
            }
            var b = r.Backward();
            if (b?.ReconstructedPulse != null) File.WriteAllText(Path.Combine(directory, "reconstructed-pulse.json"), b.ReconstructedPulse.ToJson(true));
        }
    }

    internal sealed class FitReporter : IRunReporter
    {
        public void Report(RunResult res, TextWriter w)
        {
            var ci = CultureInfo.InvariantCulture;
            var bk = res.Backward();
            if (bk != null)
            {
                w.WriteLine("  backward: " + bk.From + " -> " + bk.To + " (regularisation " + bk.Regularization.ToString("G2", ci) + "); reconstructed pulse in reconstructed-pulse.json");
                foreach (var s in bk.Steps) w.WriteLine(string.Format(ci, "    {0,-22} {1}{2}", s.Element, s.Method, s.Clipped > 1e-3 ? string.Format(ci, " ({0:P2} of the spectral energy regularised)", s.Clipped) : ""));
            }
            var f = res.Fit();
            if (f != null)
            {
                w.WriteLine(string.Format(ci, "  fit ({0}):", f.Estimator));
                w.WriteLine(string.Format(ci, "  fit: chi^2 {0:G4} -> {1:G4} ({2} points, reduced {3:G3}), {4} iterations, {5} runs{6}", f.Chi2Initial, f.Chi2Final, f.Points, f.ReducedChi2, f.Iterations, f.Evaluations, f.Converged ? "" : " (not converged)"));
                foreach (var p in f.Parameters)
                    w.WriteLine(string.Format(ci, "  {0,-34} {1,14:G6} +- {2,-12:G3} (start {3:G6}){4}", p.Name, p.Value, p.Sigma, p.Initial, p.HitBound ? "  AT BOUND" : ""));
                foreach (var kv in f.PerInstrument)
                    w.WriteLine(double.IsNaN(kv.Value.RmsAfter)
                        ? string.Format(ci, "  {0,-34} chi^2 {1:G3} -> {2:G3}", kv.Key, kv.Value.Before, kv.Value.After)
                        : string.Format(ci, "  {0,-34} rms error {1:P2} -> {2:P2}", kv.Key, kv.Value.RmsBefore, kv.Value.RmsAfter));
            }
        }
    }
}
