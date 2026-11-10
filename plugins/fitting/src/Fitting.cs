using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using UltrafastEngine.Engine;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Measured;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Plugins.Fitting
{
    /// <summary>
    /// One free parameter of a fit: a number in the files addressed by <see cref="Path"/> --
    /// <c>pulse/energyJ</c>, <c>element:stretcher/separationM</c>, <c>element:shaper/operators/0/gddFs2</c> (a transform),
    /// <c>instrument:spec/resolutionM</c>, <c>material:FusedSilica/n2</c> (as an override), <c>run/...</c>, <c>system/...</c>.
    /// </summary>
    public sealed class FitParameter
    {
        public string Name, Path, Unit;
        /// <summary>"explicit" (listed in fit.parameters) or "transform" (marked in a transform element).</summary>
        public string Origin = "explicit";
        public double Initial, Min = double.NegativeInfinity, Max = double.PositiveInfinity, Step = double.NaN, Scale = double.NaN;
        /// <summary>Gaussian prior (NaN sigma: none).</summary>
        public double PriorMean = double.NaN, PriorSigma = double.NaN;
        public double Value, Sigma = double.NaN;
        /// <summary>Believed layer: median and 68 % / 95 % intervals (from posterior samples, or value +- 1 / 1.96 sigma).</summary>
        public double Median = double.NaN, Lo68 = double.NaN, Hi68 = double.NaN, Lo95 = double.NaN, Hi95 = double.NaN;
        public bool HitBound;
        /// <summary>Listed with <c>"perCondition": true</c>: one value per training condition (this slot belongs to <see cref="Condition"/>).</summary>
        public bool PerCondition;
        /// <summary>The condition this value applies to (null: shared by all conditions).</summary>
        public string Condition;
        /// <summary>This parameter is a coefficient of a dependence on the condition (see <see cref="Dependence"/>); the files get the evaluated value.</summary>
        public Dependence Dep;

        public FitParameter Clone() => (FitParameter)MemberwiseClone();

        public static FitParameter FromJson(JsonObject o)
        {
            var p = new FitParameter
            {
                Path = o.GetRequiredString("path", "fit.parameters[]"),
                Initial = o.GetDouble("initial", double.NaN), Min = o.GetDouble("min", double.NegativeInfinity), Max = o.GetDouble("max", double.PositiveInfinity),
                Step = o.GetDouble("step", double.NaN), Scale = o.GetDouble("scale", double.NaN), Unit = o.GetString("unit", ""),
                PerCondition = o.GetBool("perCondition", false)
            };
            p.Name = o.GetString("name", p.Path);
            if (o.TryGet("prior", out var pr) && !pr.IsNull)
            {
                if (pr.Kind == JsonKind.Number) p.PriorSigma = pr.AsDouble();
                else { p.PriorMean = pr.AsObject().GetDouble("mean", double.NaN); p.PriorSigma = pr.AsObject().GetDouble("sigma", double.NaN); }
                if (!(p.PriorSigma > 0)) throw new JsonException("fit parameter " + p.Name + ": prior sigma must be > 0");
            }
            if (p.Min >= p.Max) throw new JsonException("fit parameter " + p.Name + ": min must be below max");
            return p;
        }
    }

    /// <summary>One operating condition of a multi-condition fit (<c>fit.conditions[]</c>): the files are the base files plus
    /// <see cref="Set"/>; the shared parameters are common to all conditions. A held-out condition is never in the cost: it is
    /// predicted and compared with its data afterwards.</summary>
    public sealed class FitCondition
    {
        public string Name = "";
        public bool HeldOut;
        public double Weight = 1;
        /// <summary>Named numbers describing the condition (energyJ, angleRad, ...): the inputs of parametric and smooth dependence.</summary>
        public Dictionary<string, double> Variables = new Dictionary<string, double>();
        /// <summary>Path -> value written into the files for this condition (a number, a string such as a data file, or an object).</summary>
        public List<(string Path, JsonValue Value)> Set = new List<(string, JsonValue)>();
        /// <summary>The frozen grid of this condition (set during the fit).</summary>
        internal JsonObject Grid;

        public static FitCondition FromJson(JsonObject o)
        {
            var c = new FitCondition { Name = o.GetRequiredString("name", "fit.conditions[]"), HeldOut = o.GetBool("heldOut", false), Weight = o.GetDouble("weight", 1) };
            if (!(c.Weight > 0)) throw new JsonException("fit condition " + c.Name + ": weight must be > 0");
            var vars = o.GetObject("variables");
            if (vars != null) foreach (var kv in vars) c.Variables[kv.Key] = kv.Value.AsDouble();
            var set = o.GetObject("set");
            if (set != null) foreach (var kv in set) c.Set.Add((kv.Key, JsonValue.Parse(kv.Value.ToJson(false))));
            return c;
        }
    }

    /// <summary>How well one condition is described: chi^2 and reduced chi^2 of its data (not of priors).</summary>
    public sealed class ConditionReport
    {
        public string Name; public bool HeldOut; public int Points; public double Chi2, ReducedChi2, Chi2Initial;
        public Dictionary<string, double> Variables = new Dictionary<string, double>();
        public JsonObject ToJson()
        {
            var v = new JsonObject(); foreach (var kv in Variables) v.Set(kv.Key, kv.Value);
            return new JsonObject().Set("name", Name).Set("heldOut", HeldOut).Set("variables", v).Set("points", Points).Set("chi2Initial", Chi2Initial).Set("chi2", Chi2)
                .Set("reducedChi2", double.IsNaN(ReducedChi2) || double.IsInfinity(ReducedChi2) ? JsonValue.Null : (JsonValue)ReducedChi2);
        }
    }

    /// <summary>The outcome of a fit: parameters with uncertainties, correlations, residuals, the three result layers
    /// (simulated, inferred, believed), the influence map, provenance and the fitted system / run files.</summary>
    public sealed class FitReport
    {
        public List<FitParameter> Parameters = new List<FitParameter>();
        public List<double> CostHistory = new List<double>();
        public string Estimator = "levenbergMarquardt";
        public double Chi2Initial, Chi2Final, DataChi2Final, ReducedChi2;
        public int Points, Evaluations, Iterations;
        public bool Converged;
        public double[,] Correlation;
        public List<string> Warnings = new List<string>(), Notes = new List<string>();
        public Dictionary<string, (double Before, double After, double RmsBefore, double RmsAfter)> PerInstrument = new Dictionary<string, (double, double, double, double)>();
        /// <summary>Predicted values with 1-sigma uncertainty from the parameter covariance: "instrument.value" / "element.quantity".</summary>
        public Dictionary<string, (double Value, double Sigma)> Predictions = new Dictionary<string, (double, double)>();
        /// <summary>Influence map: for each parameter, the share of its Fisher information that each instrument (and "priors") provides.</summary>
        public Dictionary<string, Dictionary<string, double>> Influence = new Dictionary<string, Dictionary<string, double>>();
        public JsonObject Simulated = new JsonObject(), Inferred = new JsonObject(), Provenance = new JsonObject();
        public int Samples; public double AcceptanceRate = double.NaN;
        /// <summary>The system / run files with the fitted values in place (transform fit objects replaced by numbers).</summary>
        public JsonObject FittedSystem, FittedRun;
        /// <summary>Multi-condition fit: one entry per condition (training and held-out); empty for a single-condition fit.</summary>
        public List<ConditionReport> Conditions = new List<ConditionReport>();
        /// <summary>Multi-condition fit with per-condition parameters: their fitted values per condition ("fitted-conditions.json").</summary>
        public JsonObject FittedConditions;

        private static JsonValue F(double v) => double.IsNaN(v) || double.IsInfinity(v) ? JsonValue.Null : (JsonValue)v;

        public JsonObject ToJson()
        {
            var o = new JsonObject().Set("estimator", Estimator).Set("converged", Converged).Set("iterations", Iterations).Set("evaluations", Evaluations)
                .Set("points", Points).Set("chi2Initial", Chi2Initial).Set("chi2Final", Chi2Final).Set("dataChi2Final", DataChi2Final).Set("reducedChi2", F(ReducedChi2));
            o.Set("parameters", new JsonArray(Parameters.Select(p => (JsonValue)new JsonObject().Set("name", p.Name).Set("path", p.Path).Set("origin", p.Origin).Set("unit", p.Unit).Set("condition", p.Condition == null ? JsonValue.Null : (JsonValue)p.Condition)
                .Set("initial", p.Initial).Set("value", p.Value).Set("sigma", F(p.Sigma)).Set("min", F(p.Min)).Set("max", F(p.Max)).Set("atBound", p.HitBound)
                .Set("prior", double.IsNaN(p.PriorSigma) ? JsonValue.Null : new JsonObject().Set("mean", p.PriorMean).Set("sigma", p.PriorSigma)))));
            if (Correlation != null)
            {
                int n = Parameters.Count; var rows = new JsonArray();
                for (int i = 0; i < n; i++) rows.Add(new JsonArray(Enumerable.Range(0, n).Select(j => F(Correlation[i, j]))));
                o.Set("correlation", rows);
            }
            if (Conditions.Count > 0) o.Set("conditions", new JsonArray(Conditions.Select(c => (JsonValue)c.ToJson())));
            o.Set("costHistory", JsonValue.FromArray(CostHistory.ToArray()));
            var pi = new JsonObject();
            foreach (var kv in PerInstrument) pi.Set(kv.Key, new JsonObject().Set("chi2Before", F(kv.Value.Before)).Set("chi2After", F(kv.Value.After)).Set("rmsErrorBefore", F(kv.Value.RmsBefore)).Set("rmsErrorAfter", F(kv.Value.RmsAfter)));
            o.Set("instruments", pi);
            var pr = new JsonObject();
            foreach (var kv in Predictions) pr.Set(kv.Key, new JsonObject().Set("value", F(kv.Value.Value)).Set("sigma", F(kv.Value.Sigma)));
            o.Set("predictions", pr);
            // the three layers
            var believed = new JsonObject().Set("description", Samples > 0 ? "posterior from " + Samples + " MCMC samples: medians and credible intervals; predictions by linear propagation of the sample covariance" : "best fit with 1-sigma (covariance at the optimum) and Gaussian intervals; predictions by linear propagation");
            var bp = new JsonObject();
            foreach (var p in Parameters)
                bp.Set(p.Name, new JsonObject().Set("value", Samples > 0 ? p.Median : p.Value).Set("sigma", F(p.Sigma)).Set("interval68", new JsonArray(new[] { F(p.Lo68), F(p.Hi68) })).Set("interval95", new JsonArray(new[] { F(p.Lo95), F(p.Hi95) })));
            believed.Set("parameters", bp).Set("predictions", pr);
            if (Samples > 0) believed.Set("samples", Samples).Set("acceptanceRate", F(AcceptanceRate));
            o.Set("layers", new JsonObject().Set("simulated", Simulated).Set("inferred", Inferred).Set("believed", believed));
            var inf = new JsonObject();
            foreach (var kv in Influence) { var io = new JsonObject(); foreach (var x in kv.Value) io.Set(x.Key, F(x.Value)); inf.Set(kv.Key, io); }
            o.Set("influence", inf);
            o.Set("provenance", Provenance);
            o.Set("warnings", JsonValue.FromArray(Warnings));
            o.Set("notes", JsonValue.FromArray(Notes));
            return o;
        }
    }

    /// <summary>
    /// The fit ("fit" in the run spec, docs/plugins/fitting.md): finds the parameter values -- transform operators
    /// marked for fitting, and any number in the files listed in fit.parameters -- that make the simulated instruments
    /// agree with the recorded ones. A replaceable estimator minimises chi^2 (data + priors + smoothness); the fit then
    /// reports uncertainties, correlations, identifiability and the influence map, predicts every instrument and element
    /// with error bars, and writes the system with the fitted values for use on new inputs.
    /// </summary>
    public static class Fitter
    {
        private sealed class CondEval { public FitCondition Cond; public RunResult Result; public double[] Data; }
        private sealed class Eval
        {
            public List<CondEval> Cond = new List<CondEval>();
            /// <summary>The first training condition's result (the one returned by the run).</summary>
            public RunResult Result; public double[] Data; public double[] R; public double Cost;
        }

        public static RunResult Run(SystemDescription system, RunSpec run, ModelRegistry registry, ProgressCallback progress = null, CancellationToken cancel = default)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var started = DateTime.UtcNow;
            var fv = run.Raw["fit"];
            var fo = fv.Kind == JsonKind.Object ? fv.AsObject() : new JsonObject();
            var rep = new FitReport();
            var sysJson = (JsonObject)JsonValue.Parse(system.ToJson().ToJson(false));
            var runJson = (JsonObject)JsonValue.Parse(run.Raw.ToJson(false));
            runJson.Set("fit", JsonValue.Null);
            // ------------------------------------------------------------ conditions (one implicit condition when none are listed)
            var conds = new List<FitCondition>();
            var ca = fo.GetArray("conditions");
            bool multi = ca != null && ca.Count > 0;
            if (multi)
            {
                foreach (var cv in ca) conds.Add(FitCondition.FromJson(cv.AsObject()));
                if (conds.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != conds.Count) throw new JsonException("fit.conditions: names must be unique");
                if (conds.All(c => c.HeldOut)) throw new JsonException("fit.conditions: at least one condition must be a training condition (not heldOut)");
            }
            else conds.Add(new FitCondition());
            string Pre(FitCondition c) => multi ? c.Name + "/" : "";
            // ------------------------------------------------------------ parameters: explicit + marked in transforms
            var deps = new List<Dependence>();
            foreach (var pv in fo.GetArray("parameters") ?? new JsonArray())
            {
                var po = pv.AsObject();
                if (po.Has("dependence"))
                {
                    if (!multi) throw new JsonException("fit parameter " + po.GetString("name", po.GetString("path", "")) + ": \"dependence\" needs fit.conditions");
                    string pn = po.GetString("name", po.GetString("path", ""));
                    deps.Add(Dependence.Parse(po.GetObject("dependence"), pn, new[] { po.GetRequiredString("path", "fit.parameters[]") }, conds, sysJson, runJson, rep.Parameters, rep.Parameters.Count));
                    continue;
                }
                var fp = FitParameter.FromJson(po);
                if (!fp.PerCondition) { rep.Parameters.Add(fp); continue; }
                if (!multi) throw new JsonException("fit parameter " + fp.Name + ": \"perCondition\" needs fit.conditions");
                foreach (var c in conds.Where(c => !c.HeldOut))
                {
                    if (c.Set.Any(s => string.Equals(s.Path, fp.Path, StringComparison.OrdinalIgnoreCase))) throw new JsonException("fit parameter " + fp.Name + ": condition " + c.Name + " also sets " + fp.Path);
                    var q = fp.Clone(); q.Name = fp.Name + "[" + c.Name + "]"; q.Condition = c.Name; rep.Parameters.Add(q);
                }
            }
            foreach (var sv in fo.GetArray("surfaces") ?? new JsonArray())
            {
                if (!multi) throw new JsonException("fit.surfaces needs fit.conditions");
                var so = sv.AsObject();
                string sn = so.GetRequiredString("name", "fit.surfaces[]");
                string[] paths;
                if (so.Has("paths")) paths = so.GetArray("paths").Select(v => v.AsString()).ToArray();
                else
                {
                    string tpl = so.GetRequiredString("pathTemplate", "fit.surfaces[" + sn + "] (paths or pathTemplate + count)");
                    paths = Enumerable.Range(0, so.GetInt("count", 0)).Select(i => tpl.Replace("{i}", i.ToString(CultureInfo.InvariantCulture))).ToArray();
                }
                if (paths.Length == 0) throw new JsonException("fit.surfaces[" + sn + "]: no paths");
                deps.Add(Dependence.Parse(so, sn, paths, conds, sysJson, runJson, rep.Parameters, rep.Parameters.Count));
            }
            foreach (var p in rep.Parameters.Where(p => p.Condition == null && p.Dep == null))
                foreach (var c in conds)
                    if (c.Set.Any(s => string.Equals(s.Path, p.Path, StringComparison.OrdinalIgnoreCase))) throw new JsonException("fit parameter " + p.Name + " is shared, but condition " + c.Name + " sets " + p.Path + " (use \"perCondition\": true)");
            var (tp, regs) = FitCollector.Collect(sysJson);
            int offset = rep.Parameters.Count;
            foreach (var r in regs) r.Indices = r.Indices.Select(i => i + offset).ToArray();
            foreach (var p in tp)
            {
                if (rep.Parameters.Any(q => string.Equals(q.Path, p.Path, StringComparison.OrdinalIgnoreCase))) { rep.Notes.Add("parameter " + p.Path + " is both listed and marked in its transform: the listed entry is used"); continue; }
                rep.Parameters.Add(p);
            }
            if (rep.Parameters.Count == 0) throw new JsonException("fit: no parameters -- mark numbers in a transform element with {\"value\": ..., \"fit\": true} (or list them in its \"fit\"), or give fit.parameters");
            foreach (var p in rep.Parameters)
            {
                if (double.IsNaN(p.Initial)) p.Initial = FitValues.Get(sysJson, runJson, p.Path);
                if (double.IsNaN(p.Initial)) throw new JsonException("fit parameter " + p.Name + ": no \"initial\" and no number at " + p.Path);
                p.Initial = Math.Max(p.Min, Math.Min(p.Max, p.Initial));
                if (double.IsNaN(p.PriorMean) && !double.IsNaN(p.PriorSigma)) p.PriorMean = p.Initial;
                if (double.IsNaN(p.Scale)) p.Scale = !double.IsInfinity(p.Max - p.Min) ? (p.Max - p.Min) / 4 : FitValues.TypicalScale(p.Path.Substring(p.Path.LastIndexOf('/') + 1), p.Initial);
                if (double.IsNaN(p.Step)) p.Step = 1e-3 * p.Scale;
            }
            int n = rep.Parameters.Count;
            string estName = fo.GetString("estimator", "levenbergMarquardt");
            var estimator = Estimators.Get(estName);
            rep.Estimator = estimator.Name;
            if (estimator.Name != "levenbergMarquardt" && rep.Parameters.Any(p => double.IsInfinity(p.Max - p.Min)))
                rep.Warnings.Add(estimator.Name + " searches inside the bounds: give min and max for every parameter (unbounded ones use +-10 x scale around the start)");
            int evals = 0;
            Eval Evaluate(double[] x, bool withHeld = false)
            {
                cancel.ThrowIfCancellationRequested();
                var ev = new Eval();
                foreach (var c in conds)
                {
                    if (c.HeldOut && !withHeld) continue;
                    var sj = (JsonObject)JsonValue.Parse(sysJson.ToJson(false));
                    var rj = (JsonObject)JsonValue.Parse(runJson.ToJson(false));
                    if (c.Grid != null) rj.Set("grid", JsonValue.Parse(c.Grid.ToJson(false)));
                    foreach (var (path, value) in c.Set) FitValues.SetValue(sj, rj, path, value);
                    for (int i = 0; i < n; i++)
                    {
                        var p = rep.Parameters[i];
                        if (p.Dep != null) continue;
                        if (p.Condition == null || string.Equals(p.Condition, c.Name, StringComparison.OrdinalIgnoreCase)) FitValues.Set(sj, rj, p.Path, x[i]);
                    }
                    foreach (var d in deps)
                        for (int pi = 0; pi < d.Paths.Length; pi++) FitValues.Set(sj, rj, d.Paths[pi], d.Value(x, pi, c.Variables[d.Variable]));
                    var sd = SystemDescription.FromJson(sj, system.BaseDirectory);
                    var rs = RunSpec.FromJson(rj, run.BaseDirectory);
                    var res = Executor.Execute(Compiler.Compile(sd, rs, registry), null, cancel);
                    int e = Interlocked.Increment(ref evals);
                    progress?.Invoke("fit", e, 0, 0);
                    var data = res.Comparisons().SelectMany(q => q.Residuals).ToArray();
                    if (c.Weight != 1) { double w = Math.Sqrt(c.Weight); for (int k = 0; k < data.Length; k++) data[k] *= w; }
                    ev.Cond.Add(new CondEval { Cond = c, Result = res, Data = data });
                }
                var train = ev.Cond.Where(q => !q.Cond.HeldOut).ToList();
                ev.Result = train[0].Result;
                ev.Data = train.SelectMany(q => q.Data).ToArray();
                ev.R = ev.Data.Concat(Penalties(x, rep.Parameters, regs, deps)).ToArray();
                ev.Cost = ev.R.Sum(v => v * v);
                return ev;
            }
            var x0 = rep.Parameters.Select(p => p.Initial).ToArray();
            var e0 = Evaluate(x0, true);
            if (e0.Data.Length == 0) throw new JsonException("fit: no instrument has \"measured\" data to fit");
            if (fo.GetBool("freezeGrid", true))
            {
                // twice the planned bandwidth: the parameters move during the fit (more SPM, other chirps); each condition has its own grid
                foreach (var ce in e0.Cond)
                {
                    var g = ce.Result.Input.Grid;
                    ce.Cond.Grid = new JsonObject().Set("auto", false).Set("n", 2 * g.N).Set("windowS", g.N * g.DTime);
                    rep.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0}grid frozen at N = {1}, window = {2:G4} ps for all evaluations (2x the planned bandwidth)", multi ? "condition " + ce.Cond.Name + ": " : "", 2 * g.N, g.N * g.DTime * 1e12));
                }
                e0 = Evaluate(x0, true);
            }
            var firstTrain = conds.First(c => !c.HeldOut);
            if (firstTrain.Grid != null) runJson.Set("grid", JsonValue.Parse(firstTrain.Grid.ToJson(false)));   // the fitted run keeps the first condition's grid
            rep.Chi2Initial = e0.Cost; rep.Points = e0.Cond.Where(q => !q.Cond.HeldOut).Sum(q => q.Result.Comparisons().Sum(c => c.Points));   // points outside an instrument's range (residual 0) do not count
            var before = e0.Cond.SelectMany(q => q.Result.Comparisons().Select(c => (Key: Pre(q.Cond) + c.Instrument, c.Chi2, c.RmsError))).ToDictionary(t => t.Key, t => (t.Chi2, t.RmsError));
            if (rep.Points < n) rep.Warnings.Add("fewer data points than parameters: the fit is under-determined (priors and smoothness help)");
            rep.Simulated = Layer(e0, rep, x0, "the system as described (initial parameter values): the forward model before fitting", multi);
            // ------------------------------------------------------------ estimate
            var cache = new Dictionary<string, Eval>();
            string Key(double[] x) => string.Join(",", x.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
            cache[Key(x0)] = e0;
            var problem = new FitProblem
            {
                N = n, X0 = x0, Names = rep.Parameters.Select(p => p.Name).ToArray(),
                Min = rep.Parameters.Select(p => double.IsInfinity(p.Min) && estimator.Name != "levenbergMarquardt" ? p.Initial - 10 * p.Scale : p.Min).ToArray(),
                Max = rep.Parameters.Select(p => double.IsInfinity(p.Max) && estimator.Name != "levenbergMarquardt" ? p.Initial + 10 * p.Scale : p.Max).ToArray(),
                Scale = rep.Parameters.Select(p => p.Scale).ToArray(), Step = rep.Parameters.Select(p => p.Step).ToArray(),
                Parallel = Math.Max(1, fo.GetInt("parallel", 1)), Cancel = cancel,
                Residuals = x => { var k = Key(x); Eval ev; lock (cache) if (cache.TryGetValue(k, out ev)) return ev.R; ev = Evaluate(x); lock (cache) { if (cache.Count > 64) cache.Clear(); cache[k] = ev; } return ev.R; }
            };
            var er = estimator.Estimate(problem, fo);
            rep.CostHistory.AddRange(er.CostHistory.Count > 0 ? er.CostHistory : new List<double> { e0.Cost });
            rep.Converged = er.Converged; rep.Iterations = er.Iterations;
            rep.Notes.AddRange(er.Notes); rep.Warnings.AddRange(er.Warnings);
            var x = er.X;
            // the optimum with the held-out conditions evaluated too (they are never in the cost)
            Eval cur;
            if (cache.TryGetValue(Key(x), out var cx) && cx.Cond.Count == conds.Count) cur = cx;
            else cur = Evaluate(x, true);
            // ------------------------------------------------------------ uncertainties at the optimum
            var (J, jacRuns) = Jacobian(x, cur, rep, xx => Evaluate(xx, true));
            int mm = cur.R.Length, md = cur.Data.Length, mPts = cur.Cond.Where(q => !q.Cond.HeldOut).Sum(q => q.Result.Comparisons().Sum(c => c.Points));   // mPts: compared points only (not those outside a range)
            var H = new double[n, n];
            for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) { double s = 0; for (int k = 0; k < mm; k++) s += J[k, a] * J[k, b]; H[a, b] = s; }
            rep.Chi2Final = cur.Cost; rep.DataChi2Final = cur.Data.Sum(v => v * v);
            rep.ReducedChi2 = mPts > n ? rep.DataChi2Final / (mPts - n) : double.NaN;
            double s2 = rep.ReducedChi2 > 1 ? rep.ReducedChi2 : 1;
            if (rep.ReducedChi2 > 1) rep.Notes.Add(string.Format(CultureInfo.InvariantCulture, "reduced chi^2 = {0:G3} > 1: covariance uncertainties scaled by sqrt({0:G3}) (the model or the stated noise does not explain all of the data)", rep.ReducedChi2));
            var (C, nullDirs) = FitMath.PseudoInverse(H);
            if (er.Samples != null && er.Samples.Count > 2 * n)
            {
                // posterior covariance from the samples, in scaled units (no chi^2 scaling: the likelihood already has it)
                C = new double[n, n]; s2 = 1;
                var mean = Enumerable.Range(0, n).Select(a => er.Samples.Average(sm => sm[a])).ToArray();
                foreach (var sm in er.Samples) for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) C[a, b] += (sm[a] - mean[a]) * (sm[b] - mean[b]) / (rep.Parameters[a].Scale * rep.Parameters[b].Scale);
                for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) C[a, b] /= er.Samples.Count - 1;
                rep.Samples = er.Samples.Count; rep.AcceptanceRate = er.AcceptanceRate;
            }
            for (int a = 0; a < n; a++)
            {
                var p = rep.Parameters[a]; p.Value = x[a];
                p.Sigma = Math.Sqrt(Math.Max(0, C[a, a] * s2)) * p.Scale;
                if (er.Samples != null && er.Samples.Count > 2 * n)
                {
                    var v = er.Samples.Select(sm => sm[a]).OrderBy(z => z).ToArray();
                    double Q(double q) { double i = q * (v.Length - 1); int lo = (int)Math.Floor(i); int hi = Math.Min(v.Length - 1, lo + 1); return v[lo] + (v[hi] - v[lo]) * (i - lo); }
                    p.Median = Q(0.5); p.Lo68 = Q(0.16); p.Hi68 = Q(0.84); p.Lo95 = Q(0.025); p.Hi95 = Q(0.975);
                }
                else { p.Median = p.Value; p.Lo68 = p.Value - p.Sigma; p.Hi68 = p.Value + p.Sigma; p.Lo95 = p.Value - 1.96 * p.Sigma; p.Hi95 = p.Value + 1.96 * p.Sigma; }
                p.HitBound = Math.Abs(x[a] - p.Min) < 1e-9 * p.Scale || Math.Abs(x[a] - p.Max) < 1e-9 * p.Scale;
                if (p.HitBound) rep.Warnings.Add("parameter " + p.Name + " ended at a bound (" + p.Value.ToString("G5", CultureInfo.InvariantCulture) + "): widen min/max or check the model");
                double hd = 0; for (int k = 0; k < md; k++) hd += J[k, a] * J[k, a];
                double hmax = Enumerable.Range(0, n).Max(b => { double s = 0; for (int k = 0; k < md; k++) s += J[k, b] * J[k, b]; return s; });
                if (hd <= 1e-12 * Math.Max(1e-300, hmax)) rep.Warnings.Add("parameter " + p.Name + " does not affect any measured instrument: it is not determined by the data" + (double.IsNaN(p.PriorSigma) ? "" : " (only by its prior)"));
            }
            rep.Correlation = new double[n, n];
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                {
                    double d = Math.Sqrt(Math.Max(1e-300, C[a, a] * C[b, b]));
                    rep.Correlation[a, b] = a == b ? 1 : C[a, b] / d;
                    if (b > a && Math.Abs(rep.Correlation[a, b]) > 0.95)
                        rep.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "parameters {0} and {1} are strongly correlated ({2:F3}): the data constrain a combination of them better than each one -- add an instrument that separates them", rep.Parameters[a].Name, rep.Parameters[b].Name, rep.Correlation[a, b]));
                }
            foreach (var dir in nullDirs)
                rep.Warnings.Add("unidentifiable combination of parameters: " + string.Join(" + ", Enumerable.Range(0, n).Where(a => Math.Abs(dir[a]) > 0.1).Select(a => dir[a].ToString("F2", CultureInfo.InvariantCulture) + " x " + rep.Parameters[a].Name)));
            // ------------------------------------------------------------ influence map: who constrains what
            var rows = new List<(string Owner, int Count)>();
            foreach (var q in cur.Cond.Where(q => !q.Cond.HeldOut))
                foreach (var c in q.Result.Comparisons()) rows.Add((Pre(q.Cond) + c.Instrument, c.Residuals.Length));
            rows.Add(("priors", mm - md));
            for (int a = 0; a < n; a++)
            {
                double tot = 0; for (int k = 0; k < mm; k++) tot += J[k, a] * J[k, a];
                var share = new Dictionary<string, double>(); int row = 0;
                foreach (var (owner, cnt) in rows)
                {
                    double s = 0; for (int k = row; k < row + cnt; k++) s += J[k, a] * J[k, a];
                    row += cnt;
                    if (cnt > 0) share[owner] = tot > 0 ? s / tot : double.NaN;
                }
                rep.Influence[rep.Parameters[a].Name] = share;
            }
            // ------------------------------------------------------------ predictions with error bars
            var best = cur.Result;
            for (int k = 0; k < cur.Cond.Count; k++)
            {
                var ce = cur.Cond[k];
                Predict(k, cur, jacRuns, rep, C, s2, Pre(ce.Cond));
                foreach (var c in ce.Result.Comparisons())
                {
                    before.TryGetValue(Pre(ce.Cond) + c.Instrument, out var b0);
                    rep.PerInstrument[Pre(ce.Cond) + c.Instrument] = (b0.Item1, c.Chi2, b0.Item2, c.RmsError);
                }
                // nominal (simulated-layer) curves next to the fitted ones, for the figures
                foreach (var ins in ce.Result.Instruments)
                {
                    var nom = e0.Cond[k].Result.Instruments.FirstOrDefault(i => i.Name == ins.Name);
                    var a0 = nom?.Arrays.FirstOrDefault(a => a.Name.EndsWith("_simulated_on_measured", StringComparison.Ordinal));
                    if (a0 != null) ins.Arrays.Add(new NdArray(a0.Name.Replace("_simulated_on_measured", "_nominal_on_measured"), (double[])a0.Data.Clone(), a0.Shape) { Unit = a0.Unit, Description = ins.Kind + ": simulated reading before the fit (initial parameter values)" });
                }
            }
            if (multi) ConditionReports(rep, e0, cur, n, conds);
            rep.Inferred = Layer(cur, rep, x, "the best fit: the forward model with the fitted parameters (" + estimator.Name + ")", multi);
            rep.Evaluations = evals;
            // ------------------------------------------------------------ fitted files and provenance
            BuildFittedFiles(rep, sysJson, runJson, x, conds, deps);
            rep.Provenance = Provenance(rep, cur, fo, estimator, started, clock.Elapsed.TotalSeconds, system, run, multi);
            best.SetFit(rep);
            best.ElapsedS = clock.Elapsed.TotalSeconds;
            best.Notes.Add(string.Format(CultureInfo.InvariantCulture, "fit ({0}): chi^2 {1:G4} -> {2:G4} in {3} iterations ({4} runs)", estimator.Name, rep.Chi2Initial, rep.Chi2Final, rep.Iterations, evals));
            foreach (var w in rep.Warnings) best.Warnings.Add("Fit: " + w);
            return best;
        }

        /// <summary>Prior and smoothness residuals (appended after the data residuals).</summary>
        private static IEnumerable<double> Penalties(double[] x, List<FitParameter> ps, List<FitCollector.Smoothness> regs, List<Dependence> deps)
        {
            for (int a = 0; a < ps.Count; a++)
                if (!double.IsNaN(ps[a].PriorSigma)) yield return (x[a] - ps[a].PriorMean) / ps[a].PriorSigma;
            foreach (var d in deps) foreach (var r in d.Penalty(x, ps)) yield return r;
            foreach (var r in regs)
            {
                double sw = Math.Sqrt(r.Weight);
                for (int i = 1; i + 1 < r.Indices.Length; i++)
                {
                    int a = r.Indices[i - 1], b = r.Indices[i], c = r.Indices[i + 1];
                    yield return sw * (x[a] - 2 * x[b] + x[c]) / ps[b].Scale;
                }
            }
        }

        private static JsonObject Layer(Eval e, FitReport rep, double[] x, string description, bool multi)
        {
            var o = new JsonObject().Set("description", description).Set("chi2", e.Cost).Set("dataChi2", e.Data.Sum(v => v * v));
            var po = new JsonObject();
            for (int i = 0; i < rep.Parameters.Count; i++) po.Set(rep.Parameters[i].Name, x[i]);
            o.Set("parameters", po);
            var io = new JsonObject();
            foreach (var ce in e.Cond)
            {
                string pre = multi ? ce.Cond.Name + "/" : "";
                foreach (var ins in ce.Result.Instruments)
                {
                    var c = ce.Result.Comparisons().FirstOrDefault(q => q.Instrument == ins.Name);
                    var vo = new JsonObject();
                    foreach (var kv in ins.Values) if (kv.Value.Kind == JsonKind.Number && !kv.Key.StartsWith("measured", StringComparison.Ordinal) && !kv.Key.EndsWith("Sigma", StringComparison.Ordinal)) vo.Set(kv.Key, kv.Value);
                    var one = new JsonObject().Set("values", vo);
                    if (c != null) { one.Set("chi2", c.Chi2); if (!double.IsNaN(c.RmsError)) one.Set("rmsError", c.RmsError); }
                    io.Set(pre + ins.Name, one);
                }
            }
            o.Set("instruments", io);
            if (e.Result.Output != null) o.Set("output", new JsonObject().Set("energyJ", e.Result.Output.TotalEnergyJ()).Set("fwhmS", e.Result.Records.LastOrDefault()?.Out?.FwhmS ?? double.NaN));
            return o;
        }

        /// <summary>Per-condition chi^2 (data only) before and after; warns when a held-out condition is predicted much worse
        /// than the training conditions are fitted (a twin that only fits its training data).</summary>
        private static void ConditionReports(FitReport rep, Eval e0, Eval cur, int nParams, List<FitCondition> conds)
        {
            var ci = CultureInfo.InvariantCulture;
            int trainPoints = cur.Cond.Where(q => !q.Cond.HeldOut).Sum(q => q.Result.Comparisons().Sum(c => c.Points));
            double trainRed = trainPoints > nParams ? cur.Data.Sum(v => v * v) / (trainPoints - nParams) : double.NaN;
            for (int k = 0; k < cur.Cond.Count; k++)
            {
                var ce = cur.Cond[k];
                int pts = ce.Result.Comparisons().Sum(c => c.Points);
                double chi = ce.Data.Sum(v => v * v);
                var cr = new ConditionReport { Name = ce.Cond.Name, HeldOut = ce.Cond.HeldOut, Points = pts, Chi2 = chi, Chi2Initial = e0.Cond[k].Data.Sum(v => v * v), Variables = new Dictionary<string, double>(ce.Cond.Variables) };
                cr.ReducedChi2 = pts > 0 ? chi / pts : double.NaN;   // chi^2 per point (a held-out condition had no parameters fitted to it)
                rep.Conditions.Add(cr);
                if (ce.Cond.HeldOut && pts > 0 && cr.ReducedChi2 > 2 * Math.Max(1, double.IsNaN(trainRed) ? 1 : trainRed))
                    rep.Warnings.Add(string.Format(ci, "held-out condition {0}: chi^2 per point {1:G3} against {2:G3} for the training conditions -- the model does not predict conditions it was not fitted to", ce.Cond.Name, cr.ReducedChi2, double.IsNaN(trainRed) ? 1 : Math.Max(1, trainRed)));
            }
        }

        private static void BuildFittedFiles(FitReport rep, JsonObject sysJson, JsonObject runJson, double[] x, List<FitCondition> conds, List<Dependence> deps)
        {
            var sj = (JsonObject)JsonValue.Parse(sysJson.ToJson(false));
            var rj = (JsonObject)JsonValue.Parse(runJson.ToJson(false));
            rj.Remove("fit");
            // the frozen grid stays in the fitted run: it reproduces the fitted result exactly
            for (int i = 0; i < rep.Parameters.Count; i++)
            {
                var p = rep.Parameters[i];
                if (p.Condition != null || p.Dep != null) continue;   // per-condition values go to fitted-conditions.json
                if (p.Origin == "transform") FitValues.Freeze(sj, rj, p.Path, x[i]);
                else FitValues.Set(sj, rj, p.Path, x[i]);
            }
            if (conds.Count > 1 || conds[0].Name.Length > 0)
            {
                var fc = new JsonObject().Set("description", "Per-condition inputs of the fit: each condition = the fitted system and run, plus 'set' (paths written into the files), the frozen grid and its fitted per-condition parameters.");
                foreach (var c in conds)
                {
                    var set = new JsonObject(); foreach (var (path, value) in c.Set) set.Set(path, value);
                    var cp = new JsonObject();
                    for (int i = 0; i < rep.Parameters.Count; i++) if (string.Equals(rep.Parameters[i].Condition, c.Name, StringComparison.OrdinalIgnoreCase)) cp.Set(rep.Parameters[i].Path, x[i]);
                    foreach (var d in deps) for (int pi = 0; pi < d.Paths.Length; pi++) cp.Set(d.Paths[pi], d.Value(x, pi, c.Variables[d.Variable]));
                    var vars = new JsonObject(); foreach (var kv in c.Variables) vars.Set(kv.Key, kv.Value);
                    fc.Set(c.Name, new JsonObject().Set("heldOut", c.HeldOut).Set("variables", vars).Set("set", set).Set("fitted", cp).Set("grid", c.Grid == null ? JsonValue.Null : (JsonValue)c.Grid));
                }
                if (deps.Count > 0) fc.Set("dependences", new JsonArray(deps.Select(d => (JsonValue)d.ToJson(x))));
                rep.FittedConditions = fc;
            }
            // the transform's own fit lists are spent: the fitted system is a plain description
            foreach (var el in (sj.GetArray("elements") ?? new JsonArray()).Select(v => v.AsObject()))
                foreach (var op in (el.GetArray("operators") ?? new JsonArray()).Where(v => v.Kind == JsonKind.Object).Select(v => v.AsObject()))
                {
                    op.Remove("fit"); op.Remove("bounds"); op.Remove("priors");
                    FreezeAll(op);
                }
            var src = sj.GetObject("source") ?? new JsonObject();
            src.Set("fitted", new JsonObject().Set("by", "ultrafast fitting plugin " + FittingPlugin.PluginVersion).Set("estimator", rep.Estimator).Set("report", "fit.json")
                .Set("parameters", new JsonObject().Also(o => { foreach (var p in rep.Parameters) o.Set(p.Name, new JsonObject().Set("value", p.Value).Set("sigma", double.IsNaN(p.Sigma) || double.IsInfinity(p.Sigma) ? JsonValue.Null : (JsonValue)p.Sigma)); })));
            sj.Set("source", src);
            rep.FittedSystem = sj; rep.FittedRun = rj;
        }

        /// <summary>Replace every remaining fit object by its value.</summary>
        private static void FreezeAll(JsonValue v)
        {
            if (v.Kind == JsonKind.Object)
            {
                var o = v.AsObject();
                foreach (var key in o.Select(kv => kv.Key).ToList())
                {
                    var c = o[key];
                    if (FitValues.IsFitObject(c)) o.Set(key, FitValues.Value(c, 0)); else FreezeAll(c);
                }
            }
            else if (v.Kind == JsonKind.Array)
            {
                var a = v.AsArray();
                for (int i = 0; i < a.Count; i++) { if (FitValues.IsFitObject(a[i])) a[i] = FitValues.Value(a[i], 0); else FreezeAll(a[i]); }
            }
        }

        private static JsonObject Provenance(FitReport rep, Eval cur, JsonObject fo, IEstimator est, DateTime started, double elapsed, SystemDescription system, RunSpec run, bool multi)
        {
            var data = new JsonArray();
            foreach (var ce in cur.Cond)
            foreach (var c in ce.Result.Comparisons())
            {
                var d = new JsonObject().Set("instrument", c.Instrument).Set("kind", c.Kind).Set("at", c.Location).Set("points", c.Points);
                if (multi) d.Set("condition", ce.Cond.Name).Set("heldOut", ce.Cond.HeldOut);
                if (c.File != null) d.Set("file", c.File);
                if (c.FullPath != null && File.Exists(c.FullPath))
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                    using (var fs = File.OpenRead(c.FullPath))
                        d.Set("sha256", BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant());
                data.Add(d);
            }
            var opts = (JsonObject)JsonValue.Parse(fo.ToJson(false));
            opts.Remove("parameters");
            return new JsonObject().Set("engine", EngineInfo.Version).Set("plugin", "fitting " + FittingPlugin.PluginVersion)
                .Set("estimator", est.Name).Set("estimatorDescription", est.Description).Set("options", opts)
                .Set("startedUtc", started.ToString("o", CultureInfo.InvariantCulture)).Set("elapsedS", elapsed)
                .Set("system", system.Name ?? "").Set("data", data)
                .Set("parameters", new JsonArray(rep.Parameters.Select(p => (JsonValue)new JsonObject().Set("name", p.Name).Set("path", p.Path).Set("origin", p.Origin))));
        }

        private static (double[,], List<Eval>) Jacobian(double[] x, Eval cur, FitReport rep, Func<double[], Eval> evaluate)
        {
            int n = x.Length, m = cur.R.Length; var J = new double[m, n]; var runs = new List<Eval>();
            for (int a = 0; a < n; a++)
            {
                var p = rep.Parameters[a]; var xp = (double[])x.Clone();
                double h = p.Step; if (xp[a] + h > p.Max) h = -h;
                xp[a] += h;
                var e = evaluate(xp); runs.Add(e);
                if (e.R.Length != m) throw new InvalidOperationException("fit: the number of residuals changed between runs (an instrument failed?)");
                double hs = h / p.Scale;   // derivative with respect to the scaled variable
                for (int k = 0; k < m; k++) J[k, a] = (e.R[k] - cur.R[k]) / hs;
            }
            return (J, runs);
        }

        /// <summary>1-sigma of every instrument scalar and of the per-element pulse metrics, by linear propagation:
        /// sigma^2 = g^T C g s2, with g from the Jacobian runs; also a 1-sigma band on each measured curve.</summary>
        private static void Predict(int k0, Eval cur, List<Eval> evals, FitReport rep, double[,] C, double s2, string pre)
        {
            int n = rep.Parameters.Count;
            var best = cur.Cond[k0].Result;
            var runs = evals.Select(e => e.Cond[k0].Result).ToList();
            double H(int a) { var p = rep.Parameters[a]; double h = p.Step; if (p.Value + h > p.Max) h = -h; return h / p.Scale; }
            double Sig(Func<RunResult, double> f)
            {
                double v0 = f(best); if (double.IsNaN(v0)) return double.NaN;
                var gr = new double[n];
                for (int a = 0; a < n; a++) { double v = f(runs[a]); if (double.IsNaN(v)) return double.NaN; gr[a] = (v - v0) / H(a); }
                double s = 0; for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) if (gr[a] != 0 && gr[b] != 0) s += gr[a] * C[a, b] * gr[b];
                return Math.Sqrt(Math.Max(0, s * s2));
            }
            foreach (var ins in best.Instruments)
                foreach (var kv in ins.Values.ToList())
                {
                    if (kv.Value.Kind != JsonKind.Number || kv.Key.StartsWith("measured", StringComparison.Ordinal)) continue;
                    string name = ins.Name, key = kv.Key;
                    double v0 = kv.Value.AsDouble();
                    double sg = Sig(r => r.Instruments.FirstOrDefault(i => i.Name == name)?.Values.GetDouble(key, double.NaN) ?? double.NaN);
                    if (!double.IsNaN(sg)) { rep.Predictions[pre + name + "." + key] = (v0, sg); ins.Values.Set(key + "Sigma", sg); }
                }
            foreach (var rec in best.Records)
            {
                string id = rec.Id;
                void P(string q, Func<ElementRecord, double> f)
                {
                    double v0 = f(rec); if (double.IsNaN(v0)) return;
                    double sg = Sig(r => { var x = r.Records.FirstOrDefault(y => y.Id == id); return x == null ? double.NaN : f(x); });
                    if (!double.IsNaN(sg)) rep.Predictions[pre + id + "." + q] = (v0, sg);
                }
                P("energyOutJ", r => r.EnergyOutJ); P("fwhmS", r => r.Out?.FwhmS ?? double.NaN); P("peakPowerW", r => r.Out?.PeakPowerW ?? double.NaN);
                P("spectralFwhmM", r => r.Out?.SpectralFwhmM ?? double.NaN); P("bIntegralRad", r => r.BIntegralRad);
            }
            foreach (var ins in best.Instruments)
            {
                var a0 = ins.Arrays.FirstOrDefault(a => a.Name.EndsWith("_simulated_on_measured"));
                if (a0 == null) continue;
                var band = new double[a0.Data.Length];
                var others = runs.Select(r => r.Instruments.FirstOrDefault(i => i.Name == ins.Name)?.Arrays.FirstOrDefault(a => a.Name == a0.Name)?.Data).ToArray();
                if (others.Any(o => o == null || o.Length != band.Length)) continue;
                for (int k = 0; k < band.Length; k++)
                {
                    var gr = new double[n];
                    for (int a = 0; a < n; a++) gr[a] = (others[a][k] - a0.Data[k]) / H(a);
                    double s = 0; for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) if (gr[a] != 0 && gr[b] != 0) s += gr[a] * C[a, b] * gr[b];
                    band[k] = Math.Sqrt(Math.Max(0, s * s2));
                }
                ins.Arrays.Add(new NdArray(a0.Name.Replace("_simulated_on_measured", "_simulated_sigma"), band, a0.Shape) { Unit = a0.Unit, Description = ins.Kind + ": 1-sigma uncertainty of the fitted reading (parameter covariance)" });
            }
        }

        // kept for callers of the previous version
        public static double Get(JsonObject sys, JsonObject run, string path) => FitValues.Get(sys, run, path);
        public static void Set(JsonObject sys, JsonObject run, string path, double value) => FitValues.Set(sys, run, path, value);
        public static (double[,] Inverse, List<double[]> Null) PseudoInverse(double[,] H) => FitMath.PseudoInverse(H);
    }

    internal static class JsonObjectExtensions
    {
        public static JsonObject Also(this JsonObject o, Action<JsonObject> f) { f(o); return o; }
    }
}
