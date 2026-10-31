using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Engine
{
    /// <summary>One varied parameter of a study.</summary>
    public sealed class StudyParameter
    {
        /// <summary>"element:ID/key", "material:NAME/property", "pulse/key[/sub]", "run/a/b", "system/a/b".</summary>
        public string Path;
        public double Min = double.NaN, Max = double.NaN, Nominal = double.NaN;
        public double[] Values;
        public bool Log;
        public int Points = 5;
        public string Name => Path;

        public double FromUnit(double u)
        {
            if (Values != null && Values.Length > 0) return Values[Math.Min(Values.Length - 1, (int)Math.Floor(u * Values.Length))];
            return Log ? Math.Exp(Math.Log(Min) + u * (Math.Log(Max) - Math.Log(Min))) : Min + u * (Max - Min);
        }
        public double ToUnit(double v) => Log ? (Math.Log(v) - Math.Log(Min)) / (Math.Log(Max) - Math.Log(Min)) : (v - Min) / (Max - Min);
        public double NominalValue => !double.IsNaN(Nominal) ? Nominal : Values != null && Values.Length > 0 ? Values[Values.Length / 2] : Log ? Math.Sqrt(Min * Max) : 0.5 * (Min + Max);

        public static StudyParameter FromJson(JsonObject o)
        {
            var p = new StudyParameter
            {
                Path = o.GetRequiredString("path", "study parameter"),
                Min = o.GetDouble("min", double.NaN), Max = o.GetDouble("max", double.NaN), Nominal = o.GetDouble("nominal", double.NaN),
                Values = o.GetDoubleArray("values"), Log = o.GetString("scale", "linear").Equals("log", StringComparison.OrdinalIgnoreCase), Points = o.GetInt("points", 5)
            };
            if (p.Values == null && (double.IsNaN(p.Min) || double.IsNaN(p.Max))) throw new JsonException("study parameter " + p.Path + " needs min and max (or values).");
            if (p.Log && (p.Min <= 0 || p.Max <= 0)) throw new JsonException("study parameter " + p.Path + ": log scale needs positive bounds.");
            return p;
        }
    }

    /// <summary>A study: what to vary, what to record, and how (docs/reference/studies.md).</summary>
    public sealed class StudySpec
    {
        public string Mode = "scan";            // scan | optimize | sensitivity
        public string Sampling = "grid";        // grid | random | lhs | sobol
        public int Samples = 64;
        public int Seed = 1;
        public int Parallel = 1;
        public bool FreezeGrid = true, PrefixCache = true;
        public List<StudyParameter> Parameters = new List<StudyParameter>();
        public List<string> Outputs = new List<string>();
        /// <summary>Whole arrays to keep per point, as "instrument/array": written to study.npz (one row per point).</summary>
        public List<string> ArrayOutputs = new List<string>();
        // optimisation
        public string Objective; public string Goal = "min"; public double Target = double.NaN; public int MaxEvaluations = 200; public double Tolerance = 1e-4;
        // sensitivity
        public string SensitivityMethod = "local"; public double StepFraction = 0.01;
        // coherence (repeated runs with independent quantum noise)
        public int Trials = 10; public string CoherenceAt = "end";

        public static StudySpec FromJson(JsonObject o)
        {
            var s = new StudySpec
            {
                Mode = o.GetString("mode", "scan").ToLowerInvariant(),
                Sampling = o.GetString("sampling", "grid").ToLowerInvariant(),
                Samples = o.GetInt("samples", 64), Seed = o.GetInt("seed", 1), Parallel = Math.Max(1, o.GetInt("parallel", 1)),
                FreezeGrid = o.GetBool("freezeGrid", true), PrefixCache = o.GetBool("prefixCache", true),
            };
            if (s.Mode == "coherence")
            {
                var co = o.GetObject("coherence") ?? new JsonObject();
                s.Trials = Math.Max(2, co.GetInt("trials", o.GetInt("trials", 10)));
                s.CoherenceAt = co.GetString("at", o.GetString("at", "end"));
            }
            foreach (var p in o.GetArray("parameters") ?? (s.Mode == "coherence" ? new JsonArray() : throw new JsonException("study needs \"parameters\""))) s.Parameters.Add(StudyParameter.FromJson(p.AsObject()));
            foreach (var x in o.GetArray("outputs") ?? new JsonArray()) s.Outputs.Add(x.AsString());
            foreach (var x in o.GetArray("arrayOutputs") ?? new JsonArray()) s.ArrayOutputs.Add(x.AsString());
            var ob = o.GetObject("objective");
            if (ob != null)
            {
                s.Objective = ob.GetRequiredString("output", "objective"); s.Goal = ob.GetString("goal", "min").ToLowerInvariant(); s.Target = ob.GetDouble("target", double.NaN);
                s.MaxEvaluations = ob.GetInt("maxEvaluations", 200); s.Tolerance = ob.GetDouble("tolerance", 1e-4);
                if (!s.Outputs.Contains(s.Objective)) s.Outputs.Add(s.Objective);
            }
            var se = o.GetObject("sensitivity");
            if (se != null) { s.SensitivityMethod = se.GetString("method", "local").ToLowerInvariant(); s.StepFraction = se.GetDouble("stepFraction", 0.01); if (se.Has("samples")) s.Samples = se.GetInt("samples", s.Samples); }
            if (s.Outputs.Count == 0 && s.Mode != "coherence") throw new JsonException("study needs \"outputs\" (e.g. \"pulse:end/fwhmS\").");
            if (s.Mode == "optimize" && s.Objective == null) throw new JsonException("an optimize study needs \"objective\": {\"output\": ..., \"goal\": \"min\" | \"max\" | \"target\"}.");
            return s;
        }
    }

    /// <summary>The evaluated points of a study.</summary>
    public sealed class StudyResult
    {
        public List<string> ParameterNames = new List<string>(), OutputNames = new List<string>();
        public List<double[]> Parameters = new List<double[]>(), Outputs = new List<double[]>();
        public List<string> Errors = new List<string>();
        /// <summary>Kept arrays by name: one row per point, in the order of <see cref="Parameters"/>.</summary>
        public Dictionary<string, List<double[]>> Arrays = new Dictionary<string, List<double[]>>();
        public JsonObject Analysis = new JsonObject();
        public List<string> Notes = new List<string>();
        public double ElapsedS;
        public int Evaluations => Parameters.Count;

        public JsonObject ToJson()
        {
            var rows = new JsonArray();
            for (int i = 0; i < Parameters.Count; i++)
            {
                var r = new JsonObject();
                for (int k = 0; k < ParameterNames.Count; k++) r.Set(ParameterNames[k], Parameters[i][k]);
                for (int k = 0; k < OutputNames.Count; k++) r.Set(OutputNames[k], Outputs[i][k]);
                if (Errors[i] != null) r.Set("error", Errors[i]);
                rows.Add(r);
            }
            return new JsonObject().Set("format", "ultrafast-study-result").Set("parameters", JsonValue.FromArray(ParameterNames)).Set("outputs", JsonValue.FromArray(OutputNames))
                .Set("evaluations", Evaluations).Set("elapsedS", ElapsedS).Set("analysis", Analysis).Set("notes", JsonValue.FromArray(Notes)).Set("rows", rows);
        }

        public void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "study.json"), ToJson().ToJson(true));
            var sb = new StringBuilder();
            sb.Append(string.Join(",", ParameterNames.Concat(OutputNames).Select(n => n.Replace(",", ";")))).Append(",error\n");
            for (int i = 0; i < Parameters.Count; i++)
                sb.Append(string.Join(",", Parameters[i].Concat(Outputs[i]).Select(v => double.IsNaN(v) ? "" : v.ToString("G9", CultureInfo.InvariantCulture)))).Append(',').Append((Errors[i] ?? "").Replace(",", ";").Replace("\n", " ")).Append('\n');
            File.WriteAllText(Path.Combine(dir, "study.csv"), sb.ToString());
            if (Arrays.Count > 0)
            {
                var list = new List<NdArray>();
                foreach (var kv in Arrays.Where(a => a.Value.Count > 0))
                {
                    int rows = kv.Value.Count, cols = kv.Value[0].Length;
                    var flat = new double[rows * cols];
                    for (int r = 0; r < rows; r++) Array.Copy(kv.Value[r], 0, flat, r * cols, cols);
                    list.Add(new NdArray(kv.Key.Replace('/', '_'), flat, rows, cols) { Description = "one row per study point" });
                }
                NpzWriter.Write(Path.Combine(dir, "study.npz"), list);
            }
            var co = Analysis.GetObject("coherence");
            if (co != null)
            {
                var wl = co.GetDoubleArray("wavelength_m"); var g = co.GetDoubleArray("g12"); var sp = co.GetDoubleArray("spectrum");
                var cb = new StringBuilder("wavelength_nm,g12,mean_spectrum\n");
                for (int i = 0; i < wl.Length; i++) cb.Append((wl[i] * 1e9).ToString("G9", CultureInfo.InvariantCulture)).Append(',').Append(g[i].ToString("G6", CultureInfo.InvariantCulture)).Append(',').Append(sp[i].ToString("G6", CultureInfo.InvariantCulture)).Append('\n');
                File.WriteAllText(Path.Combine(dir, "coherence.csv"), cb.ToString());
            }
        }
    }

    /// <summary>
    /// Parameter studies on top of the engine (docs/reference/studies.md): scans (grid, random, Latin hypercube, Sobol),
    /// optimisation (Nelder-Mead on the scaled box), sensitivities (local elasticities; global Sobol indices with the
    /// Saltelli / Jansen estimators). The grid is frozen at the nominal plan so every point is computed on the same
    /// samples; when all varied parameters act on elements after some index the run restarts there from the cached
    /// nominal pulse (prefix caching); points run in parallel batches.
    /// </summary>
    public static class Studies
    {
        public static StudyResult Run(SystemDescription system, RunSpec run, StudySpec spec, ModelRegistry registry = null, Action<int, int> progress = null, CancellationToken cancel = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            registry = registry ?? ModelRegistry.CreateDefault();
            if (spec.Mode == "coherence") return Coherence(system, run, spec, registry, progress, cancel, sw);
            var res = new StudyResult();
            res.ParameterNames.AddRange(spec.Parameters.Select(p => p.Name));
            res.OutputNames.AddRange(spec.Outputs);
            var sysJson = (JsonObject)JsonValue.Parse(system.ToJson().ToJson(false));
            var runJson = (JsonObject)JsonValue.Parse(run.Raw.ToJson(false));
            // nominal run (and the frozen grid)
            var nomVals = spec.Parameters.Select(p => p.NominalValue).ToArray();
            var (s0, r0) = Build(sysJson, runJson, spec.Parameters, nomVals, system.BaseDirectory, run.BaseDirectory);
            bool branched = string.Equals(system.Topology, SystemGraph.Branched, StringComparison.OrdinalIgnoreCase);
            var cache = branched && spec.PrefixCache && spec.FreezeGrid ? new GraphCache() : null;
            var nominal = Simulation.Run(s0, r0, registry);
            if (spec.FreezeGrid)
            {
                var g = nominal.Output.Grid;
                var gridObj = runJson.GetObject("grid") ?? new JsonObject();
                gridObj.Set("auto", false).Set("n", nominal.Compiled.Grid.N).Set("windowS", nominal.Compiled.Grid.N * nominal.Compiled.Grid.DTime);
                runJson.Set("grid", gridObj);
                (s0, r0) = Build(sysJson, runJson, spec.Parameters, nomVals, system.BaseDirectory, run.BaseDirectory);
                nominal = Simulation.Run(s0, r0, registry);
                if (cache != null)
                {
                    var c0 = Compiler.Compile(s0, r0, registry);
                    c0.GraphCache = cache; cache.Store = true;
                    Executor.Execute(c0, null, cancel);
                    cache.Store = false;
                }
                res.Notes.Add(string.Format(CultureInfo.InvariantCulture, "grid frozen at N = {0}, window {1:G4} ps", nominal.Compiled.Grid.N, nominal.Compiled.Grid.N * nominal.Compiled.Grid.DTime * 1e12));
            }
            int first = spec.PrefixCache && spec.FreezeGrid && !branched ? FirstAffected(system, spec.Parameters) : 0;
            var kept = new System.Collections.Concurrent.ConcurrentDictionary<double[], double[][]>();
            if (first > 0) res.Notes.Add("prefix cache: every point restarts at element #" + first + " (" + system.Elements[first].Id + ")");
            int done = 0, total = 0;
            var lockObj = new object();
            (double[] Out, string Err) Eval(double[] values)
            {
                cancel.ThrowIfCancellationRequested();
                var (s, r) = Build(sysJson, runJson, spec.Parameters, values, system.BaseDirectory, run.BaseDirectory);
                RunResult rr;
                string err = null;
                double[] outv;
                try
                {
                    if (cache != null)
                    {
                        var c = Compiler.Compile(s, r, registry);
                        c.GraphCache = cache;
                        rr = Executor.Execute(c, null, cancel);
                    }
                    else if (first > 0 && !(r.Raw?.Has("fit") ?? false))
                    {
                        var c = Compiler.Compile(s, r, registry);
                        rr = Executor.ExecuteFrom(c, nominal, first, cancel);
                    }
                    else rr = Simulation.Run(s, r, registry, null, cancel);
                    outv = spec.Outputs.Select(o => Extract(rr, o)).ToArray();
                    if (spec.ArrayOutputs.Count > 0) kept[values] = spec.ArrayOutputs.Select(a => ExtractArray(rr, a)).ToArray();
                }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { err = ex.Message; outv = spec.Outputs.Select(_ => double.NaN).ToArray(); }
                lock (lockObj) progress?.Invoke(++done, total);
                return (outv, err);
            }
            double[][] Batch(IList<double[]> points)
            {
                total += points.Count;
                var outs = new double[points.Count][]; var errs = new string[points.Count];
                var po = new ParallelOptions { MaxDegreeOfParallelism = spec.Parallel, CancellationToken = cancel };
                System.Threading.Tasks.Parallel.For(0, points.Count, po, i => { var e = Eval(points[i]); outs[i] = e.Out; errs[i] = e.Err; });
                // rows in the order of the points (not of completion), so parallel studies are reproducible
                for (int i = 0; i < points.Count; i++)
                    if (kept.TryGetValue(points[i], out var rows))
                        for (int k = 0; k < rows.Length; k++)
                        {
                            if (!res.Arrays.TryGetValue(spec.ArrayOutputs[k], out var list)) res.Arrays[spec.ArrayOutputs[k]] = list = new List<double[]>();
                            list.Add(rows[k]);
                        }
                for (int i = 0; i < points.Count; i++) { res.Parameters.Add((double[])points[i].Clone()); res.Outputs.Add(outs[i]); res.Errors.Add(errs[i]); }
                return outs;
            }
            int d = spec.Parameters.Count;
            switch (spec.Mode)
            {
                case "scan":
                {
                    var pts = Sample(spec, d).Select(u => u.Select((x, k) => spec.Parameters[k].FromUnit(x)).ToArray()).ToList();
                    if (spec.Sampling == "grid") pts = GridPoints(spec.Parameters);
                    Batch(pts);
                    break;
                }
                case "optimize":
                {
                    int io = spec.Outputs.IndexOf(spec.Objective);
                    double Obj(double[] u)
                    {
                        var v = u.Select((x, k) => spec.Parameters[k].FromUnit(Math.Max(0, Math.Min(1, x)))).ToArray();
                        double y = Batch(new[] { v })[0][io];
                        if (double.IsNaN(y)) return double.PositiveInfinity;
                        return spec.Goal == "max" ? -y : spec.Goal == "target" ? (y - spec.Target) * (y - spec.Target) : y;
                    }
                    var start = spec.Parameters.Select(p => p.ToUnit(p.NominalValue)).ToArray();
                    var (best, fbest, evals) = NelderMead(Obj, start, spec.MaxEvaluations, spec.Tolerance);
                    var bv = best.Select((x, k) => spec.Parameters[k].FromUnit(Math.Max(0, Math.Min(1, x)))).ToArray();
                    var o = new JsonObject();
                    for (int k = 0; k < d; k++) o.Set(spec.Parameters[k].Name, bv[k]);
                    int ib = res.Parameters.FindIndex(p => p.SequenceEqual(bv));
                    res.Analysis.Set("optimum", o).Set("objective", spec.Objective).Set("goal", spec.Goal).Set("value", ib >= 0 ? res.Outputs[ib][io] : double.NaN).Set("evaluations", evals);
                    break;
                }
                case "sensitivity":
                    if (spec.SensitivityMethod == "sobol") SobolIndices(spec, res, Batch);
                    else LocalSensitivity(spec, res, Batch, nomVals);
                    break;
                default: throw new JsonException("study mode must be scan, optimize or sensitivity");
            }
            if (cache != null) res.Notes.Add("node cache: " + cache.Hits + " element runs reused from the nominal run");
            res.ElapsedS = sw.Elapsed.TotalSeconds;
            return res;
        }

        // ------------------------------------------------------------------------------------------ building points

        /// <summary>System and run with the parameter values applied (element / material via overrides, others by editing the JSON).</summary>
        /// <summary>
        /// First-order spectral coherence (Dudley, Genty &amp; Coen, Rev. Mod. Phys. 78, 1135 (2006)): the system is run
        /// <c>trials</c> times with independent one-photon-per-mode noise on the input (pulse.noise, seed = seed + trial),
        /// and |g12(lambda)| = |&lt;E_i*(lambda) E_j(lambda)&gt;_{i != j}| / &lt;|E(lambda)|^2&gt; is evaluated at
        /// <c>at</c>; its spectrally weighted mean is the single-number coherence.
        /// </summary>
        private static StudyResult Coherence(SystemDescription system, RunSpec run, StudySpec spec, ModelRegistry registry, Action<int, int> progress, CancellationToken cancel, System.Diagnostics.Stopwatch sw)
        {
            var res = new StudyResult();
            res.ParameterNames.Add("trial");
            res.OutputNames.AddRange(spec.Outputs);
            var sysJson = (JsonObject)JsonValue.Parse(system.ToJson().ToJson(false));
            var runJson = (JsonObject)JsonValue.Parse(run.Raw.ToJson(false));
            var pulseJ = runJson.GetObject("pulse") ?? throw new JsonException("coherence study: the run spec needs a pulse");
            var noise = pulseJ.GetObject("noise") ?? new JsonObject().Set("kind", "onePhotonPerMode");
            if (noise.GetString("kind", "onePhotonPerMode").Equals("none", StringComparison.OrdinalIgnoreCase)) noise.Set("kind", "onePhotonPerMode");
            pulseJ.Set("noise", noise);
            // freeze the grid at the plan of the first trial
            noise.Set("seed", spec.Seed);
            var (s0, r0) = Build(sysJson, runJson, new List<StudyParameter>(), new double[0], system.BaseDirectory, run.BaseDirectory);
            var first = Simulation.Run(s0, r0, registry, null, cancel);
            var gridObj = runJson.GetObject("grid") ?? new JsonObject();
            gridObj.Set("auto", false).Set("n", first.Compiled.Grid.N).Set("windowS", first.Compiled.Grid.N * first.Compiled.Grid.DTime);
            runJson.Set("grid", gridObj);
            var fields = new Pulses.PulseField[spec.Trials];
            var outs = new double[spec.Trials][]; var errs = new string[spec.Trials];
            int done = 0; var lk = new object();
            var po = new ParallelOptions { MaxDegreeOfParallelism = spec.Parallel, CancellationToken = cancel };
            System.Threading.Tasks.Parallel.For(0, spec.Trials, po, t =>
            {
                var rj = (JsonObject)JsonValue.Parse(runJson.ToJson(false));
                rj.GetObject("pulse").GetObject("noise").Set("seed", spec.Seed + t);
                try
                {
                    var (s, r) = Build(sysJson, rj, new List<StudyParameter>(), new double[0], system.BaseDirectory, run.BaseDirectory);
                    var rr = Simulation.Run(s, r, registry, null, cancel);
                    if (!rr.At.TryGetValue(spec.CoherenceAt, out var p)) throw new JsonException("coherence: no pulse recorded at \"" + spec.CoherenceAt + "\"");
                    fields[t] = p;
                    outs[t] = spec.Outputs.Select(o => Extract(rr, o)).ToArray();
                }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { errs[t] = ex.Message; outs[t] = spec.Outputs.Select(_ => double.NaN).ToArray(); }
                lock (lk) progress?.Invoke(++done, spec.Trials);
            });
            for (int t = 0; t < spec.Trials; t++) { res.Parameters.Add(new double[] { t }); res.Outputs.Add(outs[t]); res.Errors.Add(errs[t]); }
            var ok = fields.Where(f => f != null).ToArray();
            if (ok.Length >= 2)
            {
                var g = ok[0].Grid; int n = g.N;
                var num = new Complex[n]; var den = new double[n]; int pairs = 0;
                for (int i = 0; i < ok.Length; i++)
                {
                    for (int k = 0; k < n; k++) den[k] += ok[i].Ax[k].Magnitude * ok[i].Ax[k].Magnitude + ok[i].Ay[k].Magnitude * ok[i].Ay[k].Magnitude;
                    for (int j = i + 1; j < ok.Length; j++)
                    {
                        pairs++;
                        for (int k = 0; k < n; k++) num[k] += Complex.Conjugate(ok[i].Ax[k]) * ok[j].Ax[k] + Complex.Conjugate(ok[i].Ay[k]) * ok[j].Ay[k];
                    }
                }
                var g12 = new double[n]; var meanS = new double[n]; double wsum = 0, gsum = 0;
                for (int k = 0; k < n; k++)
                {
                    meanS[k] = den[k] / ok.Length;
                    g12[k] = meanS[k] > 0 ? (num[k] / pairs).Magnitude / meanS[k] : 0;
                    wsum += meanS[k]; gsum += meanS[k] * g12[k];
                }
                // export on the part of the spectrum above -40 dB, ascending in wavelength
                double smax = meanS.Max();
                var idx = Enumerable.Range(0, n).Where(k => meanS[k] > 1e-4 * smax && g.OmegaAbsolute[k] > 0).OrderByDescending(k => g.OmegaAbsolute[k]).ToArray();
                var co = new JsonObject()
                    .Set("trials", ok.Length).Set("at", spec.CoherenceAt).Set("meanCoherence", wsum > 0 ? gsum / wsum : double.NaN)
                    .Set("wavelength_m", JsonValue.FromArray(idx.Select(k => 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / g.OmegaAbsolute[k])))
                    .Set("g12", JsonValue.FromArray(idx.Select(k => g12[k])))
                    .Set("spectrum", JsonValue.FromArray(idx.Select(k => meanS[k] / smax)));
                res.Analysis.Set("coherence", co);
                res.Notes.Add(string.Format(CultureInfo.InvariantCulture, "spectral coherence at {0}: mean |g12| = {1:G3} over {2} trials ({3} pairs)", spec.CoherenceAt, wsum > 0 ? gsum / wsum : double.NaN, ok.Length, pairs));
            }
            else res.Notes.Add("coherence: fewer than two successful trials");
            res.Notes.Add(string.Format(CultureInfo.InvariantCulture, "grid frozen at N = {0}", first.Compiled.Grid.N));
            res.ElapsedS = sw.Elapsed.TotalSeconds;
            return res;
        }

        public static (SystemDescription, RunSpec) Build(JsonObject sysJson, JsonObject runJson, IList<StudyParameter> ps, double[] values, string sysBase, string runBase)
        {
            var sj = (JsonObject)JsonValue.Parse(sysJson.ToJson(false));
            var rj = (JsonObject)JsonValue.Parse(runJson.ToJson(false));
            var ov = rj.GetArray("overrides") ?? new JsonArray();
            for (int k = 0; k < ps.Count; k++)
            {
                string path = ps[k].Path; double v = values[k];
                int c = path.IndexOf(':'), sl = path.IndexOf('/');
                if (c > 0 && (sl < 0 || c < sl))
                {
                    string kind = path.Substring(0, c), rest = path.Substring(c + 1);
                    int s2 = rest.IndexOf('/');
                    if (s2 < 0) throw new JsonException("study path " + path + ": expected " + kind + ":NAME/property");
                    string name = rest.Substring(0, s2), prop = rest.Substring(s2 + 1);
                    if (kind == "element" && prop.Contains("/"))
                    {
                        var el = (sj.GetArray("elements") ?? new JsonArray()).Select(x => x.AsObject()).FirstOrDefault(x => x.GetString("id") == name) ?? throw new JsonException("study path " + path + ": no element " + name);
                        SetPath(el, prop, v);
                    }
                    else if (kind == "element" || kind == "material" || kind == "type") ov.Add(new JsonObject().Set("target", kind + ":" + name).Set("property", prop).Set("value", v).Set("source", "study"));
                    else throw new JsonException("study path " + path + ": unknown target " + kind);
                }
                else if (path.StartsWith("pulse/")) SetPath(rj.GetObject("pulse"), path.Substring(6), v);
                else if (path.StartsWith("run/")) SetPath(rj, path.Substring(4), v);
                else if (path.StartsWith("system/")) SetPath(sj, path.Substring(7), v);
                else throw new JsonException("study path " + path + ": use element:ID/key, material:NAME/property, pulse/..., run/... or system/...");
            }
            if (ov.Count > 0) rj.Set("overrides", ov);
            return (SystemDescription.FromJson(sj, sysBase), RunSpec.FromJson(rj, runBase));
        }

        private static void SetPath(JsonObject o, string path, double v)
        {
            var parts = path.Split('/');
            JsonValue cur = o;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (cur.Kind == JsonKind.Array) { cur = cur.AsArray()[int.Parse(parts[i], CultureInfo.InvariantCulture)]; continue; }
                var co = cur.AsObject();
                if (!co.Has(parts[i])) co.Set(parts[i], new JsonObject());
                cur = co[parts[i]];
            }
            string last = parts[parts.Length - 1];
            if (cur.Kind == JsonKind.Array) cur.AsArray()[int.Parse(last, CultureInfo.InvariantCulture)] = v;
            else cur.AsObject().Set(last, v);
        }

        /// <summary>Index of the first element any parameter can change (0 when a parameter acts on the pulse, run or whole system).</summary>
        public static int FirstAffected(SystemDescription s, IEnumerable<StudyParameter> ps)
        {
            int first = int.MaxValue;
            foreach (var p in ps)
            {
                if (p.Path.StartsWith("element:"))
                {
                    string id = p.Path.Substring(8).Split('/')[0];
                    int i = s.Elements.FindIndex(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase) || (id.EndsWith("*") && e.Id.StartsWith(id.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)));
                    first = Math.Min(first, i < 0 ? 0 : i);
                }
                else if (p.Path.StartsWith("material:"))
                {
                    string m = p.Path.Substring(9).Split('/')[0];
                    int i = s.Elements.FindIndex(e => e.Json.ToJson(false).IndexOf("\"" + m + "\"", StringComparison.OrdinalIgnoreCase) >= 0);
                    first = Math.Min(first, i < 0 ? 0 : i);
                }
                else first = 0;
            }
            return first == int.MaxValue ? 0 : first;
        }

        // ------------------------------------------------------------------------------------------ outputs

        /// <summary>
        /// A scalar from a result: "pulse:ID/metric" (pulse summary at a location: fwhmS, peakPowerW, energyJ, ...),
        /// "record:ID/field" (per-element record: BIntegralRad, EnergyOutJ, ... or a value key), "instrument:NAME/key",
        /// "energy:ID".
        /// </summary>
        /// <summary>A whole array of an instrument, "name/array".</summary>
        public static double[] ExtractArray(RunResult r, string spec)
        {
            int s = spec.IndexOf('/');
            if (s < 0) throw new JsonException("array output \"" + spec + "\": expected instrument/array");
            string name = spec.Substring(0, s), array = spec.Substring(s + 1);
            var inst = r.Instruments.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)) ?? throw new JsonException("array output " + spec + ": no instrument " + name);
            var a = inst.Arrays.FirstOrDefault(x => string.Equals(x.Name, array, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, RunResult.Sanitize(name) + "_" + array, StringComparison.OrdinalIgnoreCase)) ?? throw new JsonException("array output " + spec + ": arrays are " + string.Join(", ", inst.Arrays.Select(x => x.Name)));
            return a.Data;
        }

        public static double Extract(RunResult r, string spec)
        {
            int c = spec.IndexOf(':'); if (c < 0) throw new JsonException("output \"" + spec + "\": expected kind:location/key");
            string kind = spec.Substring(0, c), rest = spec.Substring(c + 1);
            int s = rest.IndexOf('/');
            string loc = s < 0 ? rest : rest.Substring(0, s), key = s < 0 ? "" : rest.Substring(s + 1);
            switch (kind)
            {
                case "energy": return r.At.TryGetValue(loc, out var pe) ? pe.TotalEnergyJ() : double.NaN;
                case "pulse":
                {
                    if (!r.At.TryGetValue(loc, out var p)) throw new JsonException("output " + spec + ": no pulse at " + loc);
                    return Member(PulseMetrics.Summarize(p), key, spec);
                }
                case "record":
                {
                    var rec = r.Records.FirstOrDefault(x => string.Equals(x.Id, loc, StringComparison.OrdinalIgnoreCase)) ?? throw new JsonException("output " + spec + ": no element " + loc);
                    if (rec.Values.TryGetValue(key, out var v)) return v;
                    if (rec.LossJ.TryGetValue(key, out var lv)) return lv;
                    if (key.StartsWith("out.", StringComparison.OrdinalIgnoreCase)) return Member(rec.Out, key.Substring(4), spec);
                    return Member(rec, key, spec);
                }
                case "instrument":
                {
                    var o = r.Instruments.FirstOrDefault(x => string.Equals(x.Name, loc, StringComparison.OrdinalIgnoreCase)) ?? throw new JsonException("output " + spec + ": no instrument " + loc);
                    var v = o.Values[key];
                    return v == null || v.IsNull ? double.NaN : v.AsDouble();
                }
                default: throw new JsonException("output kind must be pulse, record, instrument or energy (got " + kind + ")");
            }
        }

        private static double Member(object o, string name, string spec)
        {
            var t = o.GetType();
            var f = t.GetFields(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            object v = f?.GetValue(o);
            if (f == null)
            {
                var p = t.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p == null) throw new JsonException("output " + spec + ": unknown field \"" + name + "\" (" + string.Join(", ", t.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(x => x.FieldType == typeof(double)).Select(x => x.Name).Concat(t.GetProperties().Where(x => x.PropertyType == typeof(double)).Select(x => x.Name))) + ")");
                v = p.GetValue(o);
            }
            return v is double d ? d : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------------------------------ samplers

        public static List<double[]> GridPoints(IList<StudyParameter> ps)
        {
            var axes = ps.Select(p => p.Values ?? Enumerable.Range(0, p.Points).Select(i => p.FromUnit(p.Points == 1 ? 0.5 : (double)i / (p.Points - 1))).ToArray()).ToArray();
            var res = new List<double[]> { new double[0] };
            foreach (var ax in axes) res = res.SelectMany(prefix => ax.Select(v => prefix.Concat(new[] { v }).ToArray())).ToList();
            return res;
        }

        /// <summary>Unit-cube samples: random, Latin hypercube or Sobol.</summary>
        public static List<double[]> Sample(StudySpec spec, int d)
        {
            var rnd = new Random(spec.Seed);
            int n = spec.Samples;
            switch (spec.Sampling)
            {
                case "random": return Enumerable.Range(0, n).Select(_ => Enumerable.Range(0, d).Select(__ => rnd.NextDouble()).ToArray()).ToList();
                case "lhs":
                {
                    var cols = Enumerable.Range(0, d).Select(_ => Enumerable.Range(0, n).OrderBy(__ => rnd.Next()).Select(i => (i + rnd.NextDouble()) / n).ToArray()).ToArray();
                    return Enumerable.Range(0, n).Select(i => cols.Select(c => c[i]).ToArray()).ToList();
                }
                case "sobol": { var s = new Sobol(d); s.Next(); return Enumerable.Range(0, n).Select(_ => s.Next()).ToList(); }
                case "grid": return new List<double[]>();
                default: throw new JsonException("sampling must be grid, random, lhs or sobol");
            }
        }

        // ------------------------------------------------------------------------------------------ optimisation

        /// <summary>Nelder-Mead on the unit box (points clamped); returns the best point, value and evaluation count.</summary>
        public static (double[] Best, double Value, int Evaluations) NelderMead(Func<double[], double> f, double[] x0, int maxEval, double tol)
        {
            int n = x0.Length, evals = 0;
            double F(double[] x) { evals++; return f(x.Select(v => Math.Max(0, Math.Min(1, v))).ToArray()); }
            var simplex = new List<(double[] X, double F)>();
            simplex.Add((x0, F(x0)));
            for (int i = 0; i < n; i++) { var x = (double[])x0.Clone(); x[i] = x[i] + (x[i] < 0.75 ? 0.2 : -0.2); simplex.Add((x, F(x))); }
            while (evals < maxEval)
            {
                simplex = simplex.OrderBy(s => s.F).ToList();
                if (Math.Abs(simplex[n].F - simplex[0].F) <= tol * (Math.Abs(simplex[0].F) + 1e-30) && Spread(simplex) < 1e-3) break;
                var c = new double[n];
                for (int i = 0; i < n; i++) for (int k = 0; k < n; k++) c[k] += simplex[i].X[k] / n;
                double[] Pt(double t) => c.Select((v, k) => v + t * (simplex[n].X[k] - v)).ToArray();
                var xr = Pt(-1); double fr = F(xr);
                if (fr < simplex[0].F) { var xe = Pt(-2); double fe = F(xe); simplex[n] = fe < fr ? (xe, fe) : (xr, fr); }
                else if (fr < simplex[n - 1].F) simplex[n] = (xr, fr);
                else
                {
                    var xc = fr < simplex[n].F ? Pt(-0.5) : Pt(0.5); double fc = F(xc);
                    if (fc < Math.Min(fr, simplex[n].F)) simplex[n] = (xc, fc);
                    else for (int i = 1; i <= n; i++) { var x = simplex[i].X.Select((v, k) => simplex[0].X[k] + 0.5 * (v - simplex[0].X[k])).ToArray(); simplex[i] = (x, F(x)); }
                }
            }
            var b = simplex.OrderBy(s => s.F).First();
            return (b.X.Select(v => Math.Max(0, Math.Min(1, v))).ToArray(), b.F, evals);
        }

        private static double Spread(List<(double[] X, double F)> s) { double m = 0; for (int i = 1; i < s.Count; i++) for (int k = 0; k < s[0].X.Length; k++) m = Math.Max(m, Math.Abs(s[i].X[k] - s[0].X[k])); return m; }

        // ------------------------------------------------------------------------------------------ sensitivities

        private static void LocalSensitivity(StudySpec spec, StudyResult res, Func<IList<double[]>, double[][]> batch, double[] nom)
        {
            int d = spec.Parameters.Count;
            var pts = new List<double[]> { nom };
            var steps = new double[d];
            for (int k = 0; k < d; k++)
            {
                var p = spec.Parameters[k];
                double h = !double.IsNaN(p.Min) && !double.IsNaN(p.Max) ? spec.StepFraction * (p.Max - p.Min) : spec.StepFraction * Math.Max(Math.Abs(nom[k]), 1e-30);
                steps[k] = h;
                var a = (double[])nom.Clone(); a[k] += h; var b = (double[])nom.Clone(); b[k] -= h;
                pts.Add(a); pts.Add(b);
            }
            var y = batch(pts);
            var outJ = new JsonObject();
            for (int j = 0; j < spec.Outputs.Count; j++)
            {
                var po = new JsonObject();
                for (int k = 0; k < d; k++)
                {
                    double dy = (y[1 + 2 * k][j] - y[2 + 2 * k][j]) / (2 * steps[k]);
                    po.Set(spec.Parameters[k].Name, new JsonObject().Set("derivative", dy).Set("elasticity", y[0][j] != 0 ? dy * nom[k] / y[0][j] : double.NaN));
                }
                outJ.Set(spec.Outputs[j], po);
            }
            res.Analysis.Set("method", "local (central differences)").Set("sensitivities", outJ);
        }

        private static void SobolIndices(StudySpec spec, StudyResult res, Func<IList<double[]>, double[][]> batch)
        {
            int d = spec.Parameters.Count, n = spec.Samples;
            var s = new Sobol(2 * d); s.Next();
            var A = new List<double[]>(); var B = new List<double[]>();
            for (int i = 0; i < n; i++) { var u = s.Next(); A.Add(u.Take(d).ToArray()); B.Add(u.Skip(d).ToArray()); }
            double[] V(double[] u) => u.Select((x, k) => spec.Parameters[k].FromUnit(x)).ToArray();
            var pts = new List<double[]>();
            pts.AddRange(A.Select(V)); pts.AddRange(B.Select(V));
            for (int k = 0; k < d; k++) for (int i = 0; i < n; i++) { var ab = (double[])A[i].Clone(); ab[k] = B[i][k]; pts.Add(V(ab)); }
            var y = batch(pts);
            var outJ = new JsonObject();
            for (int j = 0; j < spec.Outputs.Count; j++)
            {
                var fA = Enumerable.Range(0, n).Select(i => y[i][j]).ToArray(); var fB = Enumerable.Range(0, n).Select(i => y[n + i][j]).ToArray();
                var all = fA.Concat(fB).Where(v => !double.IsNaN(v)).ToArray();
                double mean = all.Average(), var = all.Average(v => (v - mean) * (v - mean));
                var po = new JsonObject();
                for (int k = 0; k < d; k++)
                {
                    var fAB = Enumerable.Range(0, n).Select(i => y[2 * n + k * n + i][j]).ToArray();
                    double si = 0, st = 0;
                    for (int i = 0; i < n; i++) { si += fB[i] * (fAB[i] - fA[i]); st += (fA[i] - fAB[i]) * (fA[i] - fAB[i]); }
                    si /= n * var; st /= 2 * n * var;
                    po.Set(spec.Parameters[k].Name, new JsonObject().Set("first", si).Set("total", st));
                }
                outJ.Set(spec.Outputs[j], po);
            }
            res.Analysis.Set("method", "Sobol indices (Saltelli first order, Jansen total), " + n + " base samples").Set("sensitivities", outJ);
        }
    }

    /// <summary>
    /// Sobol low-discrepancy sequence (Gray-code construction) for up to 16 dimensions, direction numbers from Joe &amp; Kuo
    /// (2008, new-joe-kuo-6.21201). The first point is 0 (callers usually skip it).
    /// </summary>
    public sealed class Sobol
    {
        private static readonly (int S, int A, int[] M)[] Table =
        {
            (1, 0, new[] { 1 }), (2, 1, new[] { 1, 3 }), (3, 1, new[] { 1, 3, 1 }), (3, 2, new[] { 1, 1, 1 }), (4, 1, new[] { 1, 1, 3, 3 }),
            (4, 4, new[] { 1, 3, 5, 13 }), (5, 2, new[] { 1, 1, 5, 5, 17 }), (5, 4, new[] { 1, 1, 5, 5, 5 }), (5, 7, new[] { 1, 1, 7, 11, 19 }),
            (5, 11, new[] { 1, 1, 5, 1, 1 }), (5, 13, new[] { 1, 1, 1, 3, 11 }), (5, 14, new[] { 1, 3, 5, 5, 31 }), (6, 1, new[] { 1, 3, 3, 9, 7, 49 }),
            (6, 13, new[] { 1, 1, 1, 15, 21, 21 }), (6, 16, new[] { 1, 3, 1, 13, 27, 49 }),
        };
        public const int MaxDimensions = 16;
        private const int Bits = 32;
        private readonly uint[][] _v;
        private readonly uint[] _x;
        private uint _index;
        public int Dimensions { get; }

        public Sobol(int d)
        {
            if (d < 1 || d > MaxDimensions) throw new ArgumentException("Sobol sequence: 1 to " + MaxDimensions + " dimensions");
            Dimensions = d; _v = new uint[d][]; _x = new uint[d];
            _v[0] = Enumerable.Range(1, Bits).Select(i => 1u << (Bits - i)).ToArray();
            for (int j = 1; j < d; j++)
            {
                var (s, a, m) = Table[j - 1];
                var v = new uint[Bits];
                for (int i = 0; i < s; i++) v[i] = (uint)m[i] << (Bits - 1 - i);
                for (int i = s; i < Bits; i++)
                {
                    v[i] = v[i - s] ^ (v[i - s] >> s);
                    for (int k = 1; k < s; k++) if (((a >> (s - 1 - k)) & 1) != 0) v[i] ^= v[i - k];
                }
                _v[j] = v;
            }
        }

        public double[] Next()
        {
            var r = new double[Dimensions];
            for (int j = 0; j < Dimensions; j++) r[j] = _x[j] / 4294967296.0;
            // Gray code: flip the direction number of the lowest zero bit of the index
            int c = 0; uint idx = _index; while ((idx & 1) != 0) { idx >>= 1; c++; }
            for (int j = 0; j < Dimensions; j++) _x[j] ^= _v[j][c];
            _index++;
            return r;
        }
    }
}
