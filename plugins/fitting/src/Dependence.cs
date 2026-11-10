// Dependence.cs -- how a fitted number depends on the operating condition (fitting plugin, multi-condition fits).
//
//                 "network": a small tanh network of the variable with constraints built into its structure (see below).
//   Dependence    one fitted quantity (or, for a "surface", a row of them) as a function of one named condition variable:
//                 constant, proportional (c0 u), linear, polynomial, power (c0 u^c1) or Chebyshev series; the coefficients
//                 are ordinary fit parameters (priors, bounds, uncertainties, influence map).
//   Value(x, pathIndex, theta)   the quantity at a condition; Penalty(x, parameters) the smoothness residuals of a surface
//                 (second differences across the paths -- e.g. spectral knots -- and across the variable).
//   Dependence.Parse(...)        builds one from a "dependence" block of a fit parameter or an entry of fit.surfaces[].
//
// u = theta / reference (reference defaults to the mean of the training values); xi = 2 (theta - lo) / (hi - lo) - 1 on the
// training range (or "range"), so a held-out condition outside the range is an extrapolation. The coefficients live at
// rep.Parameters[First + pathIndex * Order + k].

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Plugins.Fitting
{
    public sealed class Dependence
    {
        public string Name, Kind, Variable;
        public string[] Paths;
        public int Degree, Order, First;
        public double Reference = 1, Lo, Hi, WeightPaths, WeightVariable;
        // network: xi -> H tanh units -> one output per path; constraints are exact by construction
        public int Hidden;
        public string Output = "none", Monotone = "none";
        public double OutLo, OutHi = 1, WeightDecay;

        public static readonly string[] Kinds = { "constant", "proportional", "linear", "polynomial", "power", "chebyshev", "network" };

        /// <summary>Number of weights of a network: 2 H (input weights and biases) + paths (H + 1) (output weights and offsets).</summary>
        public int NetCount => 2 * Hidden + Paths.Length * (Hidden + 1);
        private int OutIndex(int pi, int j) => First + 2 * Hidden + pi * (Hidden + 1) + j;

        private double NetValue(double[] x, int pi, double theta)
        {
            double xi = Xi(theta), raw = x[OutIndex(pi, Hidden)];
            bool mono = Monotone != "none"; double sign = Monotone == "decreasing" ? -1 : 1;
            for (int j = 0; j < Hidden; j++)
            {
                double a = x[First + j], v = x[OutIndex(pi, j)];
                if (mono) { a = a * a; v = sign * v * v; }
                raw += v * Math.Tanh(a * xi + x[First + Hidden + j]);
            }
            switch (Output)
            {
                case "bounded": return OutLo + (OutHi - OutLo) / (1 + Math.Exp(-raw));
                case "positive": return raw > 30 ? raw : Math.Log(1 + Math.Exp(raw));
                default: return raw;
            }
        }

        /// <summary>The coefficient index of path <paramref name="pi"/>, order <paramref name="k"/>.</summary>
        public int Index(int pi, int k) => First + pi * Order + k;

        /// <summary>The basis value of order k at the condition value theta.</summary>
        private double Basis(int k, double theta)
        {
            double u = theta / Reference;
            switch (Kind)
            {
                case "constant": return 1;
                case "proportional": return u;
                case "linear": return k == 0 ? 1 : u;
                case "polynomial": return Math.Pow(u, k);
                case "chebyshev": return Chebyshev(k, Xi(theta));
                default: throw new InvalidOperationException(Kind);
            }
        }

        private double Xi(double theta) => Hi > Lo ? 2 * (theta - Lo) / (Hi - Lo) - 1 : 0;

        private static double Chebyshev(int k, double xi)
        {
            if (k == 0) return 1;
            double a = 1, b = xi;
            for (int j = 2; j <= k; j++) { double c = 2 * xi * b - a; a = b; b = c; }
            return b;
        }

        public double Value(double[] x, int pi, double theta)
        {
            if (Kind == "network") return NetValue(x, pi, theta);
            if (Kind == "power")
            {
                double u = theta / Reference;
                if (!(u > 0)) throw new JsonException("fit dependence " + Name + ": a power law needs a positive " + Variable);
                return x[Index(pi, 0)] * Math.Pow(u, x[Index(pi, 1)]);
            }
            double s = 0;
            for (int k = 0; k < Order; k++) s += x[Index(pi, k)] * Basis(k, theta);
            return s;
        }

        /// <summary>Smoothness residuals of a multi-path surface: second differences across the paths (per order) and across the
        /// variable (the surface sampled at 2 Order + 3 points of xi), each divided by the coefficient scale.</summary>
        public IEnumerable<double> Penalty(double[] x, List<FitParameter> ps)
        {
            if (Kind == "network")
            {
                if (WeightDecay > 0) { double sw = Math.Sqrt(WeightDecay); for (int i = 0; i < NetCount; i++) yield return sw * x[First + i]; }
                yield break;
            }
            if (Paths.Length >= 3 && WeightPaths > 0)
            {
                double sw = Math.Sqrt(WeightPaths);
                for (int k = 0; k < Order; k++)
                    for (int i = 1; i + 1 < Paths.Length; i++)
                        yield return sw * (x[Index(i - 1, k)] - 2 * x[Index(i, k)] + x[Index(i + 1, k)]) / ps[Index(i, k)].Scale;
            }
            if (Kind == "chebyshev" && Order >= 3 && WeightVariable > 0)
            {
                double sw = Math.Sqrt(WeightVariable);
                int m = 2 * Order + 3;
                for (int i = 0; i < Paths.Length; i++)
                {
                    double scale = ps[Index(i, 0)].Scale;
                    for (int j = 1; j + 1 < m; j++)
                    {
                        double s(int q) { double xi = -1 + 2.0 * q / (m - 1), v = 0; for (int k = 0; k < Order; k++) v += x[Index(i, k)] * Chebyshev(k, xi); return v; }
                        yield return sw * (s(j - 1) - 2 * s(j) + s(j + 1)) / scale;
                    }
                }
            }
        }

        public JsonObject ToJson(double[] x)
        {
            var coef = new JsonArray();
            if (Kind == "network")
                return new JsonObject().Set("name", Name).Set("kind", Kind).Set("variable", Variable).Set("paths", JsonValue.FromArray(Paths)).Set("hidden", Hidden)
                    .Set("output", Output).Set("monotone", Monotone).Set("outputRange", new JsonArray(new JsonValue[] { OutLo, OutHi }))
                    .Set("range", new JsonArray(new JsonValue[] { Lo, Hi })).Set("weights", JsonValue.FromArray(Enumerable.Range(0, NetCount).Select(i => x[First + i]).ToArray()));
            for (int i = 0; i < Paths.Length; i++) coef.Add(new JsonArray(Enumerable.Range(0, Order).Select(k => (JsonValue)x[Index(i, k)])));
            return new JsonObject().Set("name", Name).Set("kind", Kind).Set("variable", Variable).Set("paths", JsonValue.FromArray(Paths)).Set("degree", Degree)
                .Set("reference", Reference).Set("range", new JsonArray(new JsonValue[] { Lo, Hi })).Set("coefficients", coef);
        }

        /// <summary>Evaluate the stored form (<see cref="ToJson"/>) at a condition value: path -> value.</summary>
        public static Dictionary<string, double> Evaluate(JsonObject d, double theta)
        {
            var dep = new Dependence { Name = d.GetString("name"), Kind = d.GetString("kind"), Variable = d.GetString("variable"), Degree = d.GetInt("degree", 0), Reference = d.GetDouble("reference", 1) };
            var rg = d.GetArray("range"); dep.Lo = rg[0].AsDouble(); dep.Hi = rg[1].AsDouble();
            dep.Paths = d.GetArray("paths").Select(v => v.AsString()).ToArray();
            if (dep.Kind == "network")
            {
                dep.Hidden = d.GetInt("hidden", 4); dep.Output = d.GetString("output", "none"); dep.Monotone = d.GetString("monotone", "none");
                var orr = d.GetArray("outputRange"); dep.OutLo = orr[0].AsDouble(); dep.OutHi = orr[1].AsDouble();
                var w = d.GetArray("weights").Select(v => v.AsDouble()).ToArray(); dep.First = 0;
                var on = new Dictionary<string, double>();
                for (int i = 0; i < dep.Paths.Length; i++) on[dep.Paths[i]] = dep.Value(w, i, theta);
                return on;
            }
            var rows = d.GetArray("coefficients").Select(r => r.AsArray().Select(v => v.AsDouble()).ToArray()).ToArray();
            dep.Order = rows[0].Length; dep.First = 0;
            var x = rows.SelectMany(r => r).ToArray();
            var o = new Dictionary<string, double>();
            for (int i = 0; i < dep.Paths.Length; i++) o[dep.Paths[i]] = dep.Value(x, i, theta);
            return o;
        }

        private static Dependence ParseNetwork(Dependence d, JsonObject spec, List<FitCondition> conds, JsonObject sys, JsonObject run, List<FitParameter> outParams)
        {
            d.Hidden = spec.GetInt("hidden", 4);
            d.Output = spec.GetString("output", "none"); d.Monotone = spec.GetString("monotone", "none");
            if (d.Hidden < 1) throw new JsonException("fit network " + d.Name + ": hidden must be >= 1");
            if (Array.IndexOf(new[] { "none", "bounded", "positive" }, d.Output) < 0) throw new JsonException("fit network " + d.Name + ": output must be none, bounded or positive");
            if (Array.IndexOf(new[] { "none", "increasing", "decreasing" }, d.Monotone) < 0) throw new JsonException("fit network " + d.Name + ": monotone must be none, increasing or decreasing");
            d.WeightDecay = spec.GetDouble("weightDecay", 1e-4);
            var orr = spec.GetArray("outputRange");
            if (d.Output == "bounded")
            {
                if (orr == null || orr.Count != 2 || !(orr[1].AsDouble() > orr[0].AsDouble())) throw new JsonException("fit network " + d.Name + ": output \"bounded\" needs outputRange [lo, hi] with lo < hi");
                d.OutLo = orr[0].AsDouble(); d.OutHi = orr[1].AsDouble();
            }
            foreach (var c in conds) if (!c.Variables.ContainsKey(d.Variable)) throw new JsonException("fit network " + d.Name + ": condition " + c.Name + " has no variable \"" + d.Variable + "\"");
            var tv = conds.Where(c => !c.HeldOut).Select(c => c.Variables[d.Variable]).ToList();
            var rg = spec.GetArray("range");
            d.Lo = rg != null ? rg[0].AsDouble() : tv.Min(); d.Hi = rg != null ? rg[1].AsDouble() : tv.Max();
            if (!(d.Hi > d.Lo)) throw new JsonException("fit network " + d.Name + ": the training conditions must differ in \"" + d.Variable + "\" (or give range)");
            bool mono = d.Monotone != "none";
            double sign = d.Monotone == "decreasing" ? -1 : 1;
            void Add(string name, double initial, double scale)
            {
                outParams.Add(new FitParameter { Name = name, Path = "network:" + d.Name + "/" + name, Origin = "network", Initial = initial, Scale = scale, Step = 1e-3 * scale, Min = -12, Max = 12, Dep = d });
            }
            // units tile the input range; the output starts at the file value of its path, with a gentle slope
            for (int j = 0; j < d.Hidden; j++) Add(d.Name + ".a" + j, mono ? Math.Sqrt(2.0) : 2.0, 1.0);
            for (int j = 0; j < d.Hidden; j++)
            {
                double t = d.Hidden == 1 ? 0 : -0.8 + 1.6 * j / (d.Hidden - 1);
                Add(d.Name + ".b" + j, -2.0 * t, 1.0);
            }
            for (int pi = 0; pi < d.Paths.Length; pi++)
            {
                double file = FitValues.Get(sys, run, d.Paths[pi]);
                for (int j = 0; j < d.Hidden; j++) Add(d.Name + ".v" + pi + "_" + j, mono ? Math.Sqrt(0.3 / d.Hidden) : 0.3 / d.Hidden * (j % 2 == 0 ? 1 : -1), 1.0);
                double c0;
                switch (d.Output)
                {
                    case "bounded": { double q = double.IsNaN(file) ? 0.5 : Math.Max(0.02, Math.Min(0.98, (file - d.OutLo) / (d.OutHi - d.OutLo))); c0 = Math.Log(q / (1 - q)); break; }
                    case "positive": c0 = double.IsNaN(file) || file <= 0 ? 0 : (file > 30 ? file : Math.Log(Math.Exp(file) - 1)); break;
                    default: c0 = double.IsNaN(file) ? 0 : file; break;
                }
                Add(d.Name + ".c" + pi, c0, d.Output == "none" && !double.IsNaN(file) && file != 0 ? Math.Abs(file) : 1.0);
            }
            var coef = spec.GetObject("coefficients");   // optional {"scale":..., "min":..., "max":...} for all weights
            if (coef != null) foreach (var q in outParams.Where(q => q.Dep == d)) { if (coef.Has("scale")) { q.Scale = coef.GetDouble("scale", 1); q.Step = 1e-3 * q.Scale; } if (coef.Has("min")) q.Min = coef.GetDouble("min", -12); if (coef.Has("max")) q.Max = coef.GetDouble("max", 12); }
            d.Order = 0;
            return d;
        }

        /// <summary>Build a dependence from <paramref name="spec"/> and append its coefficient parameters to <paramref name="outParams"/>.
        /// <paramref name="paths"/> is one path (a parameter's dependence) or many (a surface).</summary>
        public static Dependence Parse(JsonObject spec, string name, string[] paths, List<FitCondition> conds, JsonObject sys, JsonObject run, List<FitParameter> outParams, int firstIndex)
        {
            var d = new Dependence
            {
                Name = name, Paths = paths, First = firstIndex,
                Kind = spec.GetString("kind", paths.Length > 1 ? "chebyshev" : "linear"),
                Variable = spec.GetRequiredString("variable", "fit dependence " + name),
                Degree = spec.GetInt("degree", 2),
                WeightPaths = spec.GetDouble("smoothnessPaths", 0), WeightVariable = spec.GetDouble("smoothnessVariable", 0)
            };
            if (d.Kind == "network") return ParseNetwork(d, spec, conds, sys, run, outParams);
            if (Array.IndexOf(Kinds, d.Kind) < 0) throw new JsonException("fit dependence " + name + ": kind must be one of " + string.Join(", ", Kinds));
            if (paths.Length > 1 && d.Kind == "power") throw new JsonException("fit surface " + name + ": a power law is for a single parameter");
            switch (d.Kind)
            {
                case "constant": case "proportional": d.Order = 1; break;
                case "linear": case "power": d.Order = 2; break;
                default: d.Order = d.Degree + 1; break;
            }
            if (d.Order < 1) throw new JsonException("fit dependence " + name + ": degree must be >= 0");
            var train = conds.Where(c => !c.HeldOut).ToList();
            foreach (var c in conds) if (!c.Variables.ContainsKey(d.Variable)) throw new JsonException("fit dependence " + name + ": condition " + c.Name + " has no variable \"" + d.Variable + "\" (give conditions[].variables)");
            var tv = train.Select(c => c.Variables[d.Variable]).ToList();
            d.Reference = spec.GetDouble("reference", double.NaN);
            if (double.IsNaN(d.Reference)) { d.Reference = tv.Average(); if (d.Reference == 0) d.Reference = 1; }
            var rg = spec.GetArray("range");
            d.Lo = rg != null ? rg[0].AsDouble() : tv.Min(); d.Hi = rg != null ? rg[1].AsDouble() : tv.Max();
            if (d.Kind == "chebyshev" && !(d.Hi > d.Lo)) throw new JsonException("fit dependence " + name + ": the training conditions must differ in \"" + d.Variable + "\" (or give range)");
            if (tv.Distinct().Count() < d.Order && d.Kind != "constant") throw new JsonException(string.Format(CultureInfo.InvariantCulture, "fit dependence {0}: {1} coefficients per quantity need at least {1} distinct training values of {2}", name, d.Order, d.Variable));
            var coefSpecs = spec.GetArray("coefficients") ?? new JsonArray();
            for (int pi = 0; pi < paths.Length; pi++)
            {
                double file = FitValues.Get(sys, run, paths[pi]);
                for (int k = 0; k < d.Order; k++)
                {
                    var o = k < coefSpecs.Count && coefSpecs[k].Kind == JsonKind.Object ? (JsonObject)JsonValue.Parse(coefSpecs[k].ToJson(false)) : new JsonObject();
                    o.Set("path", paths[pi]);
                    o.Set("name", paths.Length > 1 ? name + "[" + pi + "].c" + k : name + ".c" + k);
                    bool lead = k == 0;
                    if (!o.Has("initial")) o.Set("initial", d.Kind == "power" && k == 1 ? 1.0 : lead && !double.IsNaN(file) ? file : 0.0);
                    var p = FitParameter.FromJson(o);
                    if (double.IsNaN(p.Scale))
                    {
                        double s0 = !double.IsNaN(file) && file != 0 ? Math.Abs(file) : 1;
                        string key = paths[pi].Substring(paths[pi].LastIndexOf('/') + 1);
                        if (!int.TryParse(key, out _)) { }   // a numeric tail is a list index: take the key before it
                        else { var parts = paths[pi].Split('/'); key = parts.Length > 1 ? parts[parts.Length - 2] : key; }
                        p.Scale = d.Kind == "power" && k == 1 ? 0.5 : FitValues.TypicalScale(key, s0);
                    }
                    p.Dep = d; p.Origin = "dependence";
                    outParams.Add(p);
                }
            }
            return d;
        }
    }
}
