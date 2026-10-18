using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using UltrafastEngine.Engine;
using UltrafastEngine.Instruments;
using UltrafastEngine.Plugins;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Plugins.Fitting
{
    /// <summary>
    /// One free parameter of a calibration fit. "path" addresses a number in the files:
    /// <c>pulse/energyJ</c>, <c>pulse/chirp/gddFs2</c>, <c>element:stretcher/separationM</c>,
    /// <c>element:amp/pump/powerW</c>, <c>material:FusedSilica/n2</c> (as an override), <c>run/...</c>, <c>system/...</c>.
    /// </summary>
    public sealed class FitParameter
    {
        public string Name, Path, Unit;
        public double Initial, Min = double.NegativeInfinity, Max = double.PositiveInfinity, Step = double.NaN, Scale = double.NaN;
        public double Value, Sigma = double.NaN;
        public bool HitBound;

        public static FitParameter FromJson(JsonObject o)
        {
            var p = new FitParameter
            {
                Path = o.GetRequiredString("path", "fit.parameters[]"),
                Initial = o.GetDouble("initial", double.NaN), Min = o.GetDouble("min", double.NegativeInfinity), Max = o.GetDouble("max", double.PositiveInfinity),
                Step = o.GetDouble("step", double.NaN), Scale = o.GetDouble("scale", double.NaN), Unit = o.GetString("unit", "")
            };
            p.Name = o.GetString("name", p.Path);
            if (p.Min >= p.Max) throw new JsonException("fit parameter " + p.Name + ": min must be below max");
            return p;
        }
    }

    /// <summary>The outcome of a calibration fit: parameters with uncertainties, correlations, residuals, predictions.</summary>
    public sealed class FitReport
    {
        public List<FitParameter> Parameters = new List<FitParameter>();
        public List<double> CostHistory = new List<double>();
        public double Chi2Initial, Chi2Final, ReducedChi2;
        public int Points, Evaluations, Iterations;
        public bool Converged;
        public double[,] Correlation;
        public List<string> Warnings = new List<string>(), Notes = new List<string>();
        public Dictionary<string, (double Before, double After, double RmsBefore, double RmsAfter)> PerInstrument = new Dictionary<string, (double, double, double, double)>();
        /// <summary>Predicted values with 1-sigma uncertainty from the parameter covariance: "instrument.value" / "element.quantity".</summary>
        public Dictionary<string, (double Value, double Sigma)> Predictions = new Dictionary<string, (double, double)>();

        private static JsonValue F(double v) => double.IsNaN(v) || double.IsInfinity(v) ? JsonValue.Null : (JsonValue)v;

        public JsonObject ToJson()
        {
            var o = new JsonObject().Set("converged", Converged).Set("iterations", Iterations).Set("evaluations", Evaluations)
                .Set("points", Points).Set("chi2Initial", Chi2Initial).Set("chi2Final", Chi2Final).Set("reducedChi2", F(ReducedChi2));
            o.Set("parameters", new JsonArray(Parameters.Select(p => (JsonValue)new JsonObject().Set("name", p.Name).Set("path", p.Path).Set("unit", p.Unit)
                .Set("initial", p.Initial).Set("value", p.Value).Set("sigma", F(p.Sigma)).Set("min", double.IsInfinity(p.Min) ? JsonValue.Null : (JsonValue)p.Min)
                .Set("max", double.IsInfinity(p.Max) ? JsonValue.Null : (JsonValue)p.Max).Set("atBound", p.HitBound))));
            if (Correlation != null)
            {
                int n = Parameters.Count; var rows = new JsonArray();
                for (int i = 0; i < n; i++) rows.Add(new JsonArray(Enumerable.Range(0, n).Select(j => F(Correlation[i, j]))));
                o.Set("correlation", rows);
            }
            o.Set("costHistory", JsonValue.FromArray(CostHistory.ToArray()));
            var pi = new JsonObject();
            foreach (var kv in PerInstrument) pi.Set(kv.Key, new JsonObject().Set("chi2Before", F(kv.Value.Before)).Set("chi2After", F(kv.Value.After)).Set("rmsErrorBefore", F(kv.Value.RmsBefore)).Set("rmsErrorAfter", F(kv.Value.RmsAfter)));
            o.Set("instruments", pi);
            var pr = new JsonObject();
            foreach (var kv in Predictions) pr.Set(kv.Key, new JsonObject().Set("value", F(kv.Value.Value)).Set("sigma", F(kv.Value.Sigma)));
            o.Set("predictions", pr);
            o.Set("warnings", JsonValue.FromArray(Warnings));
            o.Set("notes", JsonValue.FromArray(Notes));
            return o;
        }
    }

    /// <summary>
    /// Calibration ("fit" in the run spec, docs/plugins/fitting.md): finds the parameter values that make the simulated
    /// instruments agree with the recorded ones (weighted least squares, Levenberg-Marquardt with bounds and
    /// finite-difference derivatives), then reports uncertainties, correlations and identifiability, and predicts
    /// every instrument and element -- measured or not, upstream or downstream -- with 1-sigma error bars.
    /// </summary>
    public static class Fitter
    {
        private sealed class Eval { public RunResult Result; public double[] R; public double Cost; }

        public static RunResult Run(SystemDescription system, RunSpec run, ModelRegistry registry, ProgressCallback progress = null, CancellationToken cancel = default)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var fo = run.Raw.GetObject("fit");
            var rep = new FitReport();
            foreach (var pv in fo.GetArray("parameters") ?? throw new JsonException("fit needs \"parameters\""))
                rep.Parameters.Add(FitParameter.FromJson(pv.AsObject()));
            if (rep.Parameters.Count == 0) throw new JsonException("fit: no parameters");
            int maxIt = fo.GetInt("maxIterations", 20);
            double tol = fo.GetDouble("tolerance", 1e-4);
            bool freeze = fo.GetBool("freezeGrid", true);
            var sysJson = (JsonObject)JsonValue.Parse(system.ToJson().ToJson(false));
            var runJson = (JsonObject)JsonValue.Parse(run.Raw.ToJson(false));
            runJson.Set("fit", JsonValue.Null);
            foreach (var p in rep.Parameters)
            {
                if (double.IsNaN(p.Initial)) p.Initial = Get(sysJson, runJson, p.Path);
                if (double.IsNaN(p.Initial)) throw new JsonException("fit parameter " + p.Name + ": no \"initial\" and no number at that path");
                p.Initial = Math.Max(p.Min, Math.Min(p.Max, p.Initial));
                if (double.IsNaN(p.Scale)) p.Scale = !double.IsInfinity(p.Max - p.Min) ? (p.Max - p.Min) / 4 : Math.Max(Math.Abs(p.Initial), 1e-30);
                if (double.IsNaN(p.Step)) p.Step = 1e-3 * p.Scale;
            }
            int n = rep.Parameters.Count;
            int evals = 0;
            Eval Evaluate(double[] x, bool measure)
            {
                cancel.ThrowIfCancellationRequested();
                var sj = (JsonObject)JsonValue.Parse(sysJson.ToJson(false));
                var rj = (JsonObject)JsonValue.Parse(runJson.ToJson(false));
                for (int i = 0; i < n; i++) Set(sj, rj, rep.Parameters[i].Path, x[i]);
                var sd = SystemDescription.FromJson(sj, system.BaseDirectory);
                var rs = RunSpec.FromJson(rj, run.BaseDirectory);
                var res = Executor.Execute(Compiler.Compile(sd, rs, registry), null, cancel);
                evals++;
                progress?.Invoke("fit", evals, 0, 0);
                var r = res.Comparisons().SelectMany(c => c.Residuals).ToArray();
                return new Eval { Result = res, R = r, Cost = r.Sum(v => v * v) };
            }
            var x0 = rep.Parameters.Select(p => p.Initial).ToArray();
            var e0 = Evaluate(x0, true);
            if (e0.R.Length == 0) throw new JsonException("fit: no instrument has \"measured\" data to fit");
            if (freeze)
            {
                var g = e0.Result.Input.Grid;
                // twice the planned bandwidth: the parameters move during the fit (more SPM, other chirps)
                runJson.Set("grid", new JsonObject().Set("auto", false).Set("n", 2 * g.N).Set("windowS", g.N * g.DTime));
                rep.Notes.Add(string.Format(CultureInfo.InvariantCulture, "grid frozen at N = {0}, window = {1:G4} ps for all evaluations (2x the planned bandwidth)", 2 * g.N, g.N * g.DTime * 1e12));
                e0 = Evaluate(x0, true);
            }
            rep.Chi2Initial = e0.Cost; rep.Points = e0.R.Length; rep.CostHistory.Add(e0.Cost);
            var before = e0.Result.Comparisons().ToDictionary(c => c.Instrument, c => (c.Chi2, c.RmsError));
            if (rep.Points < n) rep.Warnings.Add("fewer data points than parameters: the fit is under-determined");
            // ---------------------------------------------------------------- Levenberg-Marquardt (scaled variables)
            var x = (double[])x0.Clone(); var cur = e0; double lambda = 1e-2;
            double[,] J = null; List<Eval> jacRuns = null;
            int it;
            for (it = 1; it <= maxIt; it++)
            {
                (J, jacRuns) = Jacobian(x, cur, rep, Evaluate);
                int m = cur.R.Length;
                var JtJ = new double[n, n]; var g = new double[n];
                for (int a = 0; a < n; a++)
                {
                    for (int k = 0; k < m; k++) g[a] += J[k, a] * cur.R[k];
                    for (int b = 0; b < n; b++) { double s = 0; for (int k = 0; k < m; k++) s += J[k, a] * J[k, b]; JtJ[a, b] = s; }
                }
                bool improved = false;
                for (int tries = 0; tries < 8; tries++)
                {
                    var A = (double[,])JtJ.Clone();
                    for (int a = 0; a < n; a++) A[a, a] += lambda * Math.Max(JtJ[a, a], 1e-12);
                    var d = Solve(A, g.Select(v => -v).ToArray());
                    if (d == null) { lambda *= 10; continue; }
                    var xt = new double[n];
                    for (int a = 0; a < n; a++) { var p = rep.Parameters[a]; xt[a] = Math.Max(p.Min, Math.Min(p.Max, x[a] + d[a] * p.Scale)); }
                    var et = Evaluate(xt, true);
                    if (et.Cost < cur.Cost)
                    {
                        double rel = (cur.Cost - et.Cost) / Math.Max(1e-300, cur.Cost);
                        double stepRel = Enumerable.Range(0, n).Max(a => Math.Abs(xt[a] - x[a]) / rep.Parameters[a].Scale);
                        x = xt; cur = et; lambda = Math.Max(1e-7, lambda / 3); improved = true;
                        rep.CostHistory.Add(cur.Cost);
                        if (rel < tol || stepRel < 1e-6) { rep.Converged = true; }
                        break;
                    }
                    lambda *= 4;
                }
                if (!improved) { rep.Converged = true; rep.Notes.Add("no further improvement (local minimum within the step tolerance)"); break; }
                if (rep.Converged) break;
            }
            rep.Iterations = Math.Min(it, maxIt);
            if (!rep.Converged) rep.Warnings.Add("the fit stopped at maxIterations = " + maxIt + " before converging");
            // ---------------------------------------------------------------- uncertainties at the optimum
            (J, jacRuns) = Jacobian(x, cur, rep, Evaluate);
            int mm = cur.R.Length;
            var H = new double[n, n];
            for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) { double s = 0; for (int k = 0; k < mm; k++) s += J[k, a] * J[k, b]; H[a, b] = s; }
            rep.Chi2Final = cur.Cost; rep.ReducedChi2 = mm > n ? cur.Cost / (mm - n) : double.NaN;
            double s2 = rep.ReducedChi2 > 1 ? rep.ReducedChi2 : 1;
            if (rep.ReducedChi2 > 1) rep.Notes.Add(string.Format(CultureInfo.InvariantCulture, "reduced chi^2 = {0:G3} > 1: uncertainties scaled by sqrt({0:G3}) (the model or the stated noise does not explain all of the data)", rep.ReducedChi2));
            var (C, nullDirs) = PseudoInverse(H);
            for (int a = 0; a < n; a++)
            {
                var p = rep.Parameters[a]; p.Value = x[a];
                p.Sigma = Math.Sqrt(Math.Max(0, C[a, a] * s2)) * p.Scale;
                p.HitBound = Math.Abs(x[a] - p.Min) < 1e-9 * p.Scale || Math.Abs(x[a] - p.Max) < 1e-9 * p.Scale;
                if (p.HitBound) rep.Warnings.Add("parameter " + p.Name + " ended at a bound (" + p.Value.ToString("G5", CultureInfo.InvariantCulture) + "): widen min/max or check the model");
                if (H[a, a] <= 1e-12 * Math.Max(1e-300, Enumerable.Range(0, n).Max(b => H[b, b]))) rep.Warnings.Add("parameter " + p.Name + " does not affect any measured instrument: it is not determined by the data");
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
            // ---------------------------------------------------------------- predictions with error bars
            var best = cur.Result;
            Predict(best, jacRuns, rep, C, s2);
            foreach (var c in best.Comparisons())
            {
                before.TryGetValue(c.Instrument, out var b0);
                rep.PerInstrument[c.Instrument] = (b0.Item1, c.Chi2, b0.Item2, c.RmsError);
            }
            rep.Evaluations = evals;
            best.SetFit(rep);
            best.ElapsedS = clock.Elapsed.TotalSeconds;
            best.Notes.Add(string.Format(CultureInfo.InvariantCulture, "fit: chi^2 {0:G4} -> {1:G4} in {2} iterations ({3} runs)", rep.Chi2Initial, rep.Chi2Final, rep.Iterations, evals));
            foreach (var w in rep.Warnings) best.Warnings.Add("Fit: " + w);
            return best;
        }

        private static (double[,], List<Eval>) Jacobian(double[] x, Eval cur, FitReport rep, Func<double[], bool, Eval> evaluate)
        {
            int n = x.Length, m = cur.R.Length; var J = new double[m, n]; var runs = new List<Eval>();
            for (int a = 0; a < n; a++)
            {
                var p = rep.Parameters[a]; var xp = (double[])x.Clone();
                double h = p.Step; if (xp[a] + h > p.Max) h = -h;
                xp[a] += h;
                var e = evaluate(xp, true); runs.Add(e);
                if (e.R.Length != m) throw new InvalidOperationException("fit: the number of residuals changed between runs (an instrument failed?)");
                double hs = h / p.Scale;   // derivative with respect to the scaled variable
                for (int k = 0; k < m; k++) J[k, a] = (e.R[k] - cur.R[k]) / hs;
            }
            return (J, runs);
        }

        /// <summary>1-sigma of every instrument scalar and of the per-element pulse metrics, by linear propagation:
        /// sigma^2 = g^T C g s2, with g from the Jacobian runs; also a 1-sigma band on each measured curve.</summary>
        private static void Predict(RunResult best, List<Eval> runs, FitReport rep, double[,] C, double s2)
        {
            int n = rep.Parameters.Count;
            double Sig(Func<RunResult, double> f)
            {
                double v0 = f(best); if (double.IsNaN(v0)) return double.NaN;
                var gr = new double[n];
                for (int a = 0; a < n; a++)
                {
                    var p = rep.Parameters[a]; double h = p.Step; if (p.Value + h > p.Max) h = -h;
                    double v = f(runs[a].Result); if (double.IsNaN(v)) return double.NaN;
                    gr[a] = (v - v0) / (h / p.Scale);
                }
                double s = 0; for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) s += gr[a] * C[a, b] * gr[b];
                return Math.Sqrt(Math.Max(0, s * s2));
            }
            foreach (var ins in best.Instruments)
                foreach (var kv in ins.Values.ToList())
                {
                    if (kv.Value.Kind != JsonKind.Number || kv.Key.StartsWith("measured", StringComparison.Ordinal)) continue;
                    string name = ins.Name, key = kv.Key;
                    double v0 = kv.Value.AsDouble();
                    double sg = Sig(r => r.Instruments.FirstOrDefault(i => i.Name == name)?.Values.GetDouble(key, double.NaN) ?? double.NaN);
                    if (!double.IsNaN(sg)) { rep.Predictions[name + "." + key] = (v0, sg); ins.Values.Set(key + "Sigma", sg); }
                }
            foreach (var rec in best.Records)
            {
                string id = rec.Id;
                void P(string q, Func<ElementRecord, double> f)
                {
                    double v0 = f(rec); if (double.IsNaN(v0)) return;
                    double sg = Sig(r => { var x = r.Records.FirstOrDefault(y => y.Id == id); return x == null ? double.NaN : f(x); });
                    if (!double.IsNaN(sg)) rep.Predictions[id + "." + q] = (v0, sg);
                }
                P("energyOutJ", r => r.EnergyOutJ); P("fwhmS", r => r.Out?.FwhmS ?? double.NaN); P("peakPowerW", r => r.Out?.PeakPowerW ?? double.NaN);
                P("spectralFwhmM", r => r.Out?.SpectralFwhmM ?? double.NaN); P("bIntegralRad", r => r.BIntegralRad);
            }
            // a 1-sigma band on the compared simulated curve of each measured instrument
            foreach (var ins in best.Instruments)
            {
                var a0 = ins.Arrays.FirstOrDefault(a => a.Name.EndsWith("_simulated_on_measured"));
                if (a0 == null) continue;
                var band = new double[a0.Data.Length];
                var others = runs.Select(r => r.Result.Instruments.FirstOrDefault(i => i.Name == ins.Name)?.Arrays.FirstOrDefault(a => a.Name == a0.Name)?.Data).ToArray();
                if (others.Any(o => o == null || o.Length != band.Length)) continue;
                for (int k = 0; k < band.Length; k++)
                {
                    var gr = new double[n];
                    for (int a = 0; a < n; a++) { var p = rep.Parameters[a]; double h = p.Step; if (p.Value + h > p.Max) h = -h; gr[a] = (others[a][k] - a0.Data[k]) / (h / p.Scale); }
                    double s = 0; for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) s += gr[a] * C[a, b] * gr[b];
                    band[k] = Math.Sqrt(Math.Max(0, s * s2));
                }
                ins.Arrays.Add(new NdArray(a0.Name.Replace("_simulated_on_measured", "_simulated_sigma"), band, a0.Shape) { Unit = a0.Unit, Description = ins.Kind + ": 1-sigma uncertainty of the fitted reading (parameter covariance)" });
            }
        }

        // ---------------------------------------------------------------- paths into the files
        private static (JsonObject Obj, string Key) Resolve(JsonObject sys, JsonObject run, string path, bool create)
        {
            string p = path.Trim();
            JsonObject root; string rest;
            if (p.StartsWith("pulse/", StringComparison.OrdinalIgnoreCase)) { root = run.GetObject("pulse"); rest = p.Substring(6); }
            else if (p.StartsWith("run/", StringComparison.OrdinalIgnoreCase)) { root = run; rest = p.Substring(4); }
            else if (p.StartsWith("system/", StringComparison.OrdinalIgnoreCase)) { root = sys; rest = p.Substring(7); }
            else if (p.StartsWith("element:", StringComparison.OrdinalIgnoreCase))
            {
                int sl = p.IndexOf('/'); if (sl < 0) throw new JsonException("fit path " + path + ": expected element:ID/property");
                string id = p.Substring(8, sl - 8); rest = p.Substring(sl + 1);
                root = (sys.GetArray("elements") ?? new JsonArray()).Select(e => e.AsObject()).FirstOrDefault(e => string.Equals(e.GetString("id"), id, StringComparison.OrdinalIgnoreCase))
                       ?? throw new JsonException("fit path " + path + ": no element '" + id + "'");
            }
            else throw new JsonException("fit path " + path + ": must start with pulse/, element:ID/, material:NAME/, run/ or system/");
            var parts = rest.Split('/');
            var o = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                // a list index: "element:amp/betweenPasses/materials/0/lengthM"
                if (int.TryParse(parts[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int ix) && i + 1 < parts.Length - 1 && o.GetArray(parts[i]) is JsonArray arr)
                {
                    if (ix >= arr.Count) throw new JsonException("fit path " + path + ": index " + ix + " is beyond the " + arr.Count + " entries of '" + parts[i] + "'");
                    o = arr[ix].AsObject(); i++; continue;
                }
                var next = o.GetObject(parts[i]);
                if (next == null) { if (!create) return (null, null); next = new JsonObject(); o.Set(parts[i], next); }
                o = next;
            }
            return (o, parts[parts.Length - 1]);
        }

        public static double Get(JsonObject sys, JsonObject run, string path)
        {
            if (path.StartsWith("material:", StringComparison.OrdinalIgnoreCase)) return double.NaN;
            var (o, k) = Resolve(sys, run, path, false);
            return o != null && o.TryGet(k, out var v) && v.Kind == JsonKind.Number ? v.AsDouble() : double.NaN;
        }

        public static void Set(JsonObject sys, JsonObject run, string path, double value)
        {
            if (path.StartsWith("material:", StringComparison.OrdinalIgnoreCase))
            {
                int sl = path.IndexOf('/'); if (sl < 0) throw new JsonException("fit path " + path + ": expected material:NAME/property");
                var ov = run.GetArray("overrides"); if (ov == null) { ov = new JsonArray(); run.Set("overrides", ov); }
                ov.Add(new JsonObject().Set("target", path.Substring(0, sl)).Set("property", path.Substring(sl + 1)).Set("value", value));
                return;
            }
            var (o, k) = Resolve(sys, run, path, true);
            o.Set(k, value);
        }

        // ---------------------------------------------------------------- small linear algebra
        private static double[] Solve(double[,] A, double[] b)
        {
            int n = b.Length; var M = (double[,])A.Clone(); var x = (double[])b.Clone();
            for (int c = 0; c < n; c++)
            {
                int piv = c; for (int r = c + 1; r < n; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[piv, c])) piv = r;
                if (Math.Abs(M[piv, c]) < 1e-300) return null;
                if (piv != c) { for (int k = 0; k < n; k++) { var t = M[c, k]; M[c, k] = M[piv, k]; M[piv, k] = t; } var tb = x[c]; x[c] = x[piv]; x[piv] = tb; }
                for (int r = c + 1; r < n; r++) { double f = M[r, c] / M[c, c]; for (int k = c; k < n; k++) M[r, k] -= f * M[c, k]; x[r] -= f * x[c]; }
            }
            for (int r = n - 1; r >= 0; r--) { double s = x[r]; for (int k = r + 1; k < n; k++) s -= M[r, k] * x[k]; x[r] = s / M[r, r]; }
            return x;
        }

        /// <summary>Pseudo-inverse of a symmetric positive semi-definite matrix (Jacobi eigen-decomposition); also
        /// returns the eigenvectors whose eigenvalues are negligible (unidentifiable parameter combinations).</summary>
        public static (double[,] Inverse, List<double[]> Null) PseudoInverse(double[,] H)
        {
            int n = H.GetLength(0);
            var A = (double[,])H.Clone(); var V = new double[n, n];
            for (int i = 0; i < n; i++) V[i, i] = 1;
            for (int sweep = 0; sweep < 100; sweep++)
            {
                double off = 0; for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) off += A[i, j] * A[i, j];
                if (off < 1e-30) break;
                for (int p = 0; p < n; p++)
                    for (int q = p + 1; q < n; q++)
                    {
                        if (Math.Abs(A[p, q]) < 1e-300) continue;
                        double th = (A[q, q] - A[p, p]) / (2 * A[p, q]);
                        double t = Math.Sign(th) / (Math.Abs(th) + Math.Sqrt(th * th + 1)); if (th == 0) t = 1;
                        double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                        for (int k = 0; k < n; k++) { double akp = A[k, p], akq = A[k, q]; A[k, p] = c * akp - s * akq; A[k, q] = s * akp + c * akq; }
                        for (int k = 0; k < n; k++) { double apk = A[p, k], aqk = A[q, k]; A[p, k] = c * apk - s * aqk; A[q, k] = s * apk + c * aqk; }
                        for (int k = 0; k < n; k++) { double vkp = V[k, p], vkq = V[k, q]; V[k, p] = c * vkp - s * vkq; V[k, q] = s * vkp + c * vkq; }
                    }
            }
            double emax = Enumerable.Range(0, n).Max(i => Math.Abs(A[i, i]));
            var inv = new double[n, n]; var nul = new List<double[]>();
            for (int k = 0; k < n; k++)
            {
                double ev = A[k, k];
                if (ev > 1e-10 * emax && ev > 0) { for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) inv[i, j] += V[i, k] * V[j, k] / ev; }
                else nul.Add(Enumerable.Range(0, n).Select(i => V[i, k]).ToArray());
            }
            // an unidentifiable direction has (effectively) infinite variance: mark the parameters involved
            foreach (var d in nul) for (int i = 0; i < n; i++) if (Math.Abs(d[i]) > 0.1) inv[i, i] = double.PositiveInfinity;
            return (inv, nul);
        }
    }
}
