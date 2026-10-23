using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Plugins.Fitting
{
    /// <summary>
    /// The problem an estimator solves: minimise sum r(x)^2 over the box [Min, Max]. r stacks the data residuals
    /// ((simulated - measured) / sigma), the priors ((x - mean) / sigma) and the smoothness penalties. Work in the
    /// scaled variables u = (x - X0) / Scale where convenient. Each evaluation is a full simulation.
    /// </summary>
    public sealed class FitProblem
    {
        public int N { get; internal set; }
        public double[] X0 { get; internal set; }
        public double[] Min { get; internal set; }
        public double[] Max { get; internal set; }
        public double[] Scale { get; internal set; }
        public double[] Step { get; internal set; }
        public string[] Names { get; internal set; }
        /// <summary>Residuals at x (a simulation). Throws OperationCanceledException when cancelled.</summary>
        public Func<double[], double[]> Residuals { get; internal set; }
        /// <summary>Evaluations allowed to run at the same time (population estimators).</summary>
        public int Parallel { get; internal set; } = 1;
        public CancellationToken Cancel { get; internal set; }

        public double Cost(double[] x) { var r = Residuals(x); double s = 0; foreach (var v in r) s += v * v; return s; }
        public double[] Clip(double[] x) { var y = new double[N]; for (int i = 0; i < N; i++) y[i] = Math.Max(Min[i], Math.Min(Max[i], x[i])); return y; }

        /// <summary>Costs of several points (in parallel up to <see cref="Parallel"/>).</summary>
        public double[] Costs(IReadOnlyList<double[]> xs)
        {
            var c = new double[xs.Count];
            if (Parallel <= 1) { for (int i = 0; i < xs.Count; i++) c[i] = Cost(xs[i]); return c; }
            System.Threading.Tasks.Parallel.For(0, xs.Count, new ParallelOptions { MaxDegreeOfParallelism = Parallel, CancellationToken = Cancel }, i => c[i] = Cost(xs[i]));
            return c;
        }
    }

    /// <summary>What an estimator returns. Uncertainties are added afterwards by the fit (covariance at X), unless
    /// <see cref="Samples"/> holds posterior samples (then the believed layer uses them).</summary>
    public sealed class EstimatorResult
    {
        public double[] X;
        public bool Converged;
        public int Iterations;
        public List<double> CostHistory = new List<double>();
        public List<string> Notes = new List<string>(), Warnings = new List<string>();
        /// <summary>Posterior samples (rows: samples, in physical units), or null.</summary>
        public List<double[]> Samples;
        public double AcceptanceRate = double.NaN;
    }

    /// <summary>A replaceable fitting algorithm, selected by <c>"estimator"</c> in the fit block. Register more with
    /// <see cref="Estimators.Register"/> (another plugin can add, e.g., an ensemble Kalman or a learned estimator).</summary>
    public interface IEstimator
    {
        string Name { get; }
        string Description { get; }
        EstimatorResult Estimate(FitProblem problem, JsonObject options);
    }

    public static class Estimators
    {
        private static readonly Dictionary<string, IEstimator> Map = new Dictionary<string, IEstimator>(StringComparer.OrdinalIgnoreCase);
        static Estimators()
        {
            Register(new LevenbergMarquardt());
            Register(new CmaEs());
            Register(new EnsembleMcmc());
        }
        public static void Register(IEstimator e) { lock (Map) Map[e.Name] = e; }
        public static IReadOnlyList<IEstimator> All { get { lock (Map) return Map.Values.OrderBy(e => e.Name).ToList(); } }
        public static IEstimator Get(string name)
        {
            lock (Map)
            {
                if (Map.TryGetValue(name, out var e)) return e;
                var alias = name.ToLowerInvariant().Replace("-", "").Replace("_", "");
                if (alias == "lm" || alias == "levenbergmarquardt") return Map["levenbergMarquardt"];
                if (alias == "cma" || alias == "cmaes") return Map["cmaes"];
                if (alias == "mcmc" || alias == "emcee" || alias == "ensemble") return Map["mcmc"];
                throw new JsonException("fit: unknown estimator \"" + name + "\" (" + string.Join(", ", Map.Keys) + ")");
            }
        }
    }

    // ==================================================================================== Levenberg-Marquardt

    /// <summary>Levenberg-Marquardt on scaled variables with bounds and forward-difference Jacobians (one run per
    /// parameter per iteration). Local: start close. Options: maxIterations (20), tolerance (1e-4).</summary>
    public sealed class LevenbergMarquardt : IEstimator
    {
        public string Name => "levenbergMarquardt";
        public string Description => "Levenberg-Marquardt (local, gradient-based; fast)";

        public EstimatorResult Estimate(FitProblem p, JsonObject o)
        {
            int maxIt = o.GetInt("maxIterations", 20); double tol = o.GetDouble("tolerance", 1e-4);
            int n = p.N; var res = new EstimatorResult();
            var x = (double[])p.X0.Clone(); var r = p.Residuals(x); double cost = r.Sum(v => v * v);
            res.CostHistory.Add(cost);
            double lambda = 1e-2; int it;
            for (it = 1; it <= maxIt; it++)
            {
                var J = FitMath.Jacobian(p, x, r);
                int m = r.Length;
                var JtJ = new double[n, n]; var g = new double[n];
                for (int a = 0; a < n; a++)
                {
                    for (int k = 0; k < m; k++) g[a] += J[k, a] * r[k];
                    for (int b = 0; b < n; b++) { double s = 0; for (int k = 0; k < m; k++) s += J[k, a] * J[k, b]; JtJ[a, b] = s; }
                }
                bool improved = false;
                for (int tries = 0; tries < 8; tries++)
                {
                    var A = (double[,])JtJ.Clone();
                    for (int a = 0; a < n; a++) A[a, a] += lambda * Math.Max(JtJ[a, a], 1e-12);
                    var d = FitMath.Solve(A, g.Select(v => -v).ToArray());
                    if (d == null) { lambda *= 10; continue; }
                    var xt = new double[n];
                    for (int a = 0; a < n; a++) xt[a] = Math.Max(p.Min[a], Math.Min(p.Max[a], x[a] + d[a] * p.Scale[a]));
                    var rt = p.Residuals(xt); double ct = rt.Sum(v => v * v);
                    if (ct < cost)
                    {
                        double rel = (cost - ct) / Math.Max(1e-300, cost);
                        double stepRel = Enumerable.Range(0, n).Max(a => Math.Abs(xt[a] - x[a]) / p.Scale[a]);
                        x = xt; r = rt; cost = ct; lambda = Math.Max(1e-7, lambda / 3); improved = true;
                        res.CostHistory.Add(cost);
                        if (rel < tol || stepRel < 1e-6) res.Converged = true;
                        break;
                    }
                    lambda *= 4;
                }
                if (!improved) { res.Converged = true; res.Notes.Add("no further improvement (local minimum within the step tolerance)"); break; }
                if (res.Converged) break;
            }
            res.Iterations = Math.Min(it, maxIt);
            if (!res.Converged) res.Warnings.Add("the fit stopped at maxIterations = " + maxIt + " before converging");
            res.X = x;
            return res;
        }
    }

    // ==================================================================================== CMA-ES

    /// <summary>
    /// Covariance-matrix adaptation evolution strategy (Hansen): a global, derivative-free search that copes with
    /// several minima and noisy costs, at the price of many more runs. Works in the scaled variables, inside the
    /// bounds (give min / max for every parameter). Options: population (4 + 3 ln n), sigma0 (0.3 of the scale),
    /// maxIterations (60), tolerance (1e-6 relative cost spread), polish (true: finish with Levenberg-Marquardt).
    /// </summary>
    public sealed class CmaEs : IEstimator
    {
        public string Name => "cmaes";
        public string Description => "CMA-ES (global, derivative-free; many runs)";

        public EstimatorResult Estimate(FitProblem p, JsonObject o)
        {
            int n = p.N; var res = new EstimatorResult();
            int lam = o.GetInt("population", 4 + (int)Math.Floor(3 * Math.Log(n)));
            int mu = lam / 2;
            var w = Enumerable.Range(0, mu).Select(i => Math.Log(mu + 0.5) - Math.Log(i + 1)).ToArray();
            double ws = w.Sum(); for (int i = 0; i < mu; i++) w[i] /= ws;
            double mueff = 1 / w.Sum(v => v * v);
            double cc = (4 + mueff / n) / (n + 4 + 2 * mueff / n), cs = (mueff + 2) / (n + mueff + 5);
            double c1 = 2 / ((n + 1.3) * (n + 1.3) + mueff), cmu = Math.Min(1 - c1, 2 * (mueff - 2 + 1 / mueff) / ((n + 2) * (n + 2) + mueff));
            double damps = 1 + 2 * Math.Max(0, Math.Sqrt((mueff - 1) / (n + 1)) - 1) + cs, chiN = Math.Sqrt(n) * (1 - 1.0 / (4 * n) + 1.0 / (21 * n * n));
            double sigma = o.GetDouble("sigma0", 0.3);
            int maxIt = o.GetInt("maxIterations", 60); double tol = o.GetDouble("tolerance", 1e-6);
            var rnd = new Random(o.GetInt("seed", 1));
            var mean = new double[n];                      // scaled: x = X0 + u * Scale
            var Cm = new double[n, n]; for (int i = 0; i < n; i++) Cm[i, i] = 1;
            var pc = new double[n]; var ps = new double[n];
            double[] X(double[] u) { var x = new double[n]; for (int i = 0; i < n; i++) x[i] = Math.Max(p.Min[i], Math.Min(p.Max[i], p.X0[i] + u[i] * p.Scale[i])); return x; }
            double best = p.Cost(p.X0); var bestU = new double[n];
            res.CostHistory.Add(best);
            int it;
            for (it = 1; it <= maxIt; it++)
            {
                p.Cancel.ThrowIfCancellationRequested();
                var (B, D) = FitMath.Eigen(Cm);
                var zs = new List<double[]>(); var ys = new List<double[]>(); var us = new List<double[]>();
                for (int k = 0; k < lam; k++)
                {
                    var z = Enumerable.Range(0, n).Select(_ => FitMath.Gauss(rnd)).ToArray();
                    var y = new double[n];
                    for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < n; j++) s += B[i, j] * Math.Sqrt(Math.Max(D[j], 1e-20)) * z[j]; y[i] = s; }
                    var u = new double[n]; for (int i = 0; i < n; i++) u[i] = mean[i] + sigma * y[i];
                    // stay inside the box (in scaled units)
                    for (int i = 0; i < n; i++)
                    {
                        double lo = (p.Min[i] - p.X0[i]) / p.Scale[i], hi = (p.Max[i] - p.X0[i]) / p.Scale[i];
                        if (u[i] < lo) u[i] = lo + Math.Min(hi - lo, lo - u[i]); if (u[i] > hi) u[i] = hi - Math.Min(hi - lo, u[i] - hi);
                        y[i] = (u[i] - mean[i]) / sigma;
                    }
                    zs.Add(z); ys.Add(y); us.Add(u);
                }
                var cost = p.Costs(us.Select(X).ToList());
                var order = Enumerable.Range(0, lam).OrderBy(k => cost[k]).ToArray();
                if (cost[order[0]] < best) { best = cost[order[0]]; bestU = (double[])us[order[0]].Clone(); }
                res.CostHistory.Add(best);
                var yw = new double[n];
                for (int i = 0; i < mu; i++) for (int j = 0; j < n; j++) yw[j] += w[i] * ys[order[i]][j];
                for (int j = 0; j < n; j++) mean[j] += sigma * yw[j];
                // C^-1/2 yw
                var cy = new double[n];
                for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < n; j++) { double t = 0; for (int k = 0; k < n; k++) t += B[k, j] * yw[k]; s += B[i, j] * t / Math.Sqrt(Math.Max(D[j], 1e-20)); } cy[i] = s; }
                for (int i = 0; i < n; i++) ps[i] = (1 - cs) * ps[i] + Math.Sqrt(cs * (2 - cs) * mueff) * cy[i];
                double psn = Math.Sqrt(ps.Sum(v => v * v));
                bool hsig = psn / Math.Sqrt(1 - Math.Pow(1 - cs, 2 * it)) / chiN < 1.4 + 2.0 / (n + 1);
                for (int i = 0; i < n; i++) pc[i] = (1 - cc) * pc[i] + (hsig ? Math.Sqrt(cc * (2 - cc) * mueff) * yw[i] : 0);
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        double rank = 0; for (int k = 0; k < mu; k++) rank += w[k] * ys[order[k]][i] * ys[order[k]][j];
                        Cm[i, j] = (1 - c1 - cmu) * Cm[i, j] + c1 * (pc[i] * pc[j] + (hsig ? 0 : cc * (2 - cc) * Cm[i, j])) + cmu * rank;
                    }
                sigma *= Math.Exp((cs / damps) * (psn / chiN - 1));
                double spread = (cost[order[lam - 1]] - cost[order[0]]) / Math.Max(1e-300, Math.Abs(cost[order[0]]));
                if (spread < tol || sigma < 1e-8) { res.Converged = true; break; }
            }
            res.Iterations = Math.Min(it, maxIt);
            res.X = X(bestU);
            res.Notes.Add(string.Format(CultureInfo.InvariantCulture, "CMA-ES: population {0}, {1} generations, final step size {2:G3} (scaled)", lam, res.Iterations, sigma));
            if (o.GetBool("polish", true))
            {
                var lm = new LevenbergMarquardt().Estimate(new FitProblem { N = n, X0 = res.X, Min = p.Min, Max = p.Max, Scale = p.Scale, Step = p.Step, Names = p.Names, Residuals = p.Residuals, Cancel = p.Cancel },
                    new JsonObject().Set("maxIterations", o.GetInt("polishIterations", 10)));
                res.X = lm.X; res.CostHistory.AddRange(lm.CostHistory.Skip(1));
                res.Notes.Add("polished with Levenberg-Marquardt (" + lm.Iterations + " iterations)");
                res.Converged = true;
            }
            else if (!res.Converged) res.Warnings.Add("CMA-ES stopped at maxIterations = " + maxIt);
            return res;
        }
    }

    // ==================================================================================== ensemble MCMC

    /// <summary>
    /// Affine-invariant ensemble sampler (Goodman &amp; Weare 2010, "emcee" stretch move) of the posterior
    /// p(x) ~ exp(-chi^2 / 2) inside the bounds, priors included. Starts from a Levenberg-Marquardt optimum (option
    /// start: "optimum" | "initial"). Returns the samples (after burn-in): the believed layer reports medians and
    /// credible intervals from them. Options: walkers (max(2n + 2, 8)), steps (200), burnIn (0.5), thin (1), seed,
    /// spread (0.1 of the scale for the starting ball). Costs walkers x steps runs.
    /// </summary>
    public sealed class EnsembleMcmc : IEstimator
    {
        public string Name => "mcmc";
        public string Description => "ensemble MCMC (posterior samples; credible intervals; many runs)";

        public EstimatorResult Estimate(FitProblem p, JsonObject o)
        {
            int n = p.N; var res = new EstimatorResult();
            var start = (double[])p.X0.Clone();
            if (o.GetString("start", "optimum") == "optimum")
            {
                var lm = new LevenbergMarquardt().Estimate(p, new JsonObject().Set("maxIterations", o.GetInt("maxIterations", 20)));
                start = lm.X; res.CostHistory.AddRange(lm.CostHistory);
                res.Notes.Add("MCMC started from the Levenberg-Marquardt optimum (" + lm.Iterations + " iterations)");
            }
            int W = Math.Max(o.GetInt("walkers", Math.Max(2 * n + 2, 8)), 2 * n + 2); if (W % 2 == 1) W++;
            int steps = o.GetInt("steps", 200), thin = Math.Max(1, o.GetInt("thin", 1));
            double burn = o.GetDouble("burnIn", 0.5), spread = o.GetDouble("spread", 0.1), a = 2.0;
            var rnd = new Random(o.GetInt("seed", 1));
            var walkers = new double[W][]; var lp = new double[W];
            for (int k = 0; k < W; k++) walkers[k] = p.Clip(Enumerable.Range(0, n).Select(i => start[i] + spread * p.Scale[i] * FitMath.Gauss(rnd)).ToArray());
            var c0 = p.Costs(walkers); for (int k = 0; k < W; k++) lp[k] = -0.5 * c0[k];
            var samples = new List<double[]>(); int accepted = 0, proposed = 0;
            double bestLp = lp.Max(); var best = (double[])walkers[Array.IndexOf(lp, bestLp)].Clone();
            for (int s = 0; s < steps; s++)
            {
                p.Cancel.ThrowIfCancellationRequested();
                // two half-ensembles updated in turn (parallel within a half)
                for (int half = 0; half < 2; half++)
                {
                    var idx = Enumerable.Range(0, W).Where(k => k % 2 == half).ToArray();
                    var other = Enumerable.Range(0, W).Where(k => k % 2 != half).ToArray();
                    var props = new List<double[]>(); var zs = new double[idx.Length];
                    for (int t = 0; t < idx.Length; t++)
                    {
                        var xj = walkers[other[rnd.Next(other.Length)]];
                        double u = rnd.NextDouble(); double z = Math.Pow((a - 1) * u + 1, 2) / a; zs[t] = z;
                        var y = new double[n]; for (int i = 0; i < n; i++) y[i] = xj[i] + z * (walkers[idx[t]][i] - xj[i]);
                        props.Add(y);
                    }
                    var inside = props.Select(y => Enumerable.Range(0, n).All(i => y[i] >= p.Min[i] && y[i] <= p.Max[i])).ToArray();
                    var toEval = Enumerable.Range(0, props.Count).Where(t => inside[t]).ToList();
                    var cs = p.Costs(toEval.Select(t => props[t]).ToList());
                    var lpy = Enumerable.Repeat(double.NegativeInfinity, props.Count).ToArray();
                    for (int q = 0; q < toEval.Count; q++) lpy[toEval[q]] = -0.5 * cs[q];
                    for (int t = 0; t < idx.Length; t++)
                    {
                        proposed++;
                        double logr = (n - 1) * Math.Log(zs[t]) + lpy[t] - lp[idx[t]];
                        if (Math.Log(rnd.NextDouble()) < logr) { walkers[idx[t]] = props[t]; lp[idx[t]] = lpy[t]; accepted++; if (lpy[t] > bestLp) { bestLp = lpy[t]; best = (double[])props[t].Clone(); } }
                    }
                }
                res.CostHistory.Add(-2 * bestLp);
                if (s >= burn * steps && (s % thin) == 0) foreach (var wk in walkers) samples.Add((double[])wk.Clone());
            }
            res.Samples = samples; res.X = best; res.Converged = true; res.Iterations = steps;
            res.AcceptanceRate = proposed > 0 ? (double)accepted / proposed : double.NaN;
            res.Notes.Add(string.Format(CultureInfo.InvariantCulture, "MCMC: {0} walkers x {1} steps, {2} samples after burn-in, acceptance {3:P0}", W, steps, samples.Count, res.AcceptanceRate));
            if (res.AcceptanceRate < 0.15) res.Warnings.Add("MCMC acceptance rate is low (" + res.AcceptanceRate.ToString("P0", CultureInfo.InvariantCulture) + "): the chains may not have mixed -- run more steps or tighten the bounds");
            if (steps < 50 * Math.Max(1, n) / 4) res.Notes.Add("few MCMC steps for " + n + " parameters: treat the intervals as indicative");
            return res;
        }
    }

    // ==================================================================================== linear algebra

    public static class FitMath
    {
        public static double Gauss(Random r) { double u1 = 1 - r.NextDouble(), u2 = r.NextDouble(); return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }

        /// <summary>Forward-difference Jacobian with respect to the scaled variables (dr / d(x / Scale)).</summary>
        public static double[,] Jacobian(FitProblem p, double[] x, double[] r0)
        {
            int n = p.N, m = r0.Length; var J = new double[m, n];
            var pts = new List<double[]>(); var hs = new double[n];
            for (int a = 0; a < n; a++)
            {
                var xp = (double[])x.Clone(); double h = p.Step[a]; if (xp[a] + h > p.Max[a]) h = -h;
                xp[a] += h; hs[a] = h; pts.Add(xp);
            }
            var rs = new double[n][];
            if (p.Parallel <= 1) for (int a = 0; a < n; a++) rs[a] = p.Residuals(pts[a]);
            else Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = p.Parallel, CancellationToken = p.Cancel }, a => rs[a] = p.Residuals(pts[a]));
            for (int a = 0; a < n; a++)
            {
                if (rs[a].Length != m) throw new InvalidOperationException("fit: the number of residuals changed between runs (an instrument failed?)");
                double hsc = hs[a] / p.Scale[a];
                for (int k = 0; k < m; k++) J[k, a] = (rs[a][k] - r0[k]) / hsc;
            }
            return J;
        }

        public static double[] Solve(double[,] A, double[] b)
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

        /// <summary>Symmetric eigen-decomposition (Jacobi): columns of V are eigenvectors, D the eigenvalues.</summary>
        public static (double[,] V, double[] D) Eigen(double[,] H)
        {
            int n = H.GetLength(0);
            var A = (double[,])H.Clone(); var V = new double[n, n];
            for (int i = 0; i < n; i++) V[i, i] = 1;
            for (int sweep = 0; sweep < 100; sweep++)
            {
                double off = 0, diag = 0; for (int i = 0; i < n; i++) { diag += A[i, i] * A[i, i]; for (int j = i + 1; j < n; j++) off += A[i, j] * A[i, j]; }
                if (off <= 1e-30 * Math.Max(diag, 1e-300)) break;
                for (int p = 0; p < n; p++)
                    for (int q = p + 1; q < n; q++)
                    {
                        if (Math.Abs(A[p, q]) < 1e-300) continue;
                        double th = (A[q, q] - A[p, p]) / (2 * A[p, q]);
                        double t = th == 0 ? 1 : Math.Sign(th) / (Math.Abs(th) + Math.Sqrt(th * th + 1));
                        double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                        for (int k = 0; k < n; k++) { double akp = A[k, p], akq = A[k, q]; A[k, p] = c * akp - s * akq; A[k, q] = s * akp + c * akq; }
                        for (int k = 0; k < n; k++) { double apk = A[p, k], aqk = A[q, k]; A[p, k] = c * apk - s * aqk; A[q, k] = s * apk + c * aqk; }
                        for (int k = 0; k < n; k++) { double vkp = V[k, p], vkq = V[k, q]; V[k, p] = c * vkp - s * vkq; V[k, q] = s * vkp + c * vkq; }
                    }
            }
            return (V, Enumerable.Range(0, n).Select(i => A[i, i]).ToArray());
        }

        /// <summary>Pseudo-inverse of a symmetric PSD matrix; also the eigenvectors of negligible eigenvalues
        /// (unidentifiable parameter combinations), whose parameters get infinite variance.</summary>
        public static (double[,] Inverse, List<double[]> Null) PseudoInverse(double[,] H)
        {
            int n = H.GetLength(0);
            var (V, D) = Eigen(H);
            double emax = D.Select(Math.Abs).DefaultIfEmpty(0).Max();
            var inv = new double[n, n]; var nul = new List<double[]>();
            for (int k = 0; k < n; k++)
            {
                double ev = D[k];
                if (ev > 1e-10 * emax && ev > 0) { for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) inv[i, j] += V[i, k] * V[j, k] / ev; }
                else nul.Add(Enumerable.Range(0, n).Select(i => V[i, k]).ToArray());
            }
            foreach (var d in nul) for (int i = 0; i < n; i++) if (Math.Abs(d[i]) > 0.1) inv[i, i] = double.PositiveInfinity;
            return (inv, nul);
        }
    }
}
