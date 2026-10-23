using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Plugins.Fitting
{
    /// <summary>
    /// Finds the transform parameters marked for fitting in a system file: fit objects with <c>"fit": true</c>, and the
    /// names listed in an operator's <c>"fit"</c> (with its <c>"bounds"</c> and <c>"priors"</c>). Also builds the
    /// smoothness regularisers of fitted spline knots.
    /// </summary>
    public static class FitCollector
    {
        /// <summary>Second-difference smoothness penalty over a run of parameters (spline knots).</summary>
        public sealed class Smoothness { public int[] Indices; public double Weight; public string Name; }

        public static int CountFlags(JsonObject element)
        {
            int n = 0;
            foreach (var op in (element.GetArray("operators") ?? new JsonArray()).Where(v => v.Kind == JsonKind.Object).Select(v => v.AsObject()))
            {
                n += CountObjects(op);
                var names = op.GetArray("fit");
                if (names != null) n += names.Count;
            }
            return n;
        }

        private static int CountObjects(JsonValue v)
        {
            if (v == null) return 0;
            if (FitValues.IsFitObject(v)) return v.AsObject().GetBool("fit", false) ? 1 : 0;
            if (v.Kind == JsonKind.Object) return v.AsObject().Sum(kv => kv.Key == "fit" || kv.Key == "bounds" || kv.Key == "priors" ? 0 : CountObjects(kv.Value));
            if (v.Kind == JsonKind.Array) return v.AsArray().Sum(CountObjects);
            return 0;
        }

        /// <summary>Every flagged parameter of every transform element, plus the smoothness regularisers.</summary>
        public static (List<FitParameter> Parameters, List<Smoothness> Regularizers) Collect(JsonObject sys)
        {
            var ps = new List<FitParameter>(); var regs = new List<Smoothness>();
            foreach (var el in (sys.GetArray("elements") ?? new JsonArray()).Select(v => v.AsObject()))
            {
                if (!string.Equals(el.GetString("type", ""), "transform", StringComparison.OrdinalIgnoreCase)) continue;
                string id = el.GetString("id", "transform");
                var ops = el.GetArray("operators") ?? new JsonArray();
                for (int k = 0; k < ops.Count; k++)
                {
                    if (ops[k].Kind != JsonKind.Object) continue;
                    var op = ops[k].AsObject();
                    if (!op.GetBool("enabled", true)) continue;
                    string kind = op.GetString("kind", "op");
                    string basePath = "element:" + id + "/operators/" + k.ToString(CultureInfo.InvariantCulture);
                    // a) fit objects anywhere inside the operator
                    Walk(op, basePath, id + "." + kind + (CountKind(ops, kind) > 1 ? "#" + k : ""), "", ps);
                    // b) names listed in "fit"
                    var names = op.GetArray("fit");
                    if (names == null) continue;
                    var bounds = op.GetObject("bounds"); var priors = op.GetObject("priors");
                    foreach (var nv in names)
                    {
                        string name = nv.AsString();
                        var (container, key) = Locate(op, name);
                        if (container == null) throw new JsonException("transform '" + id + "' operator #" + k + " (" + kind + "): \"fit\" names \"" + name + "\", which is not in the operator (give it a value first)");
                        string path = basePath + "/" + name.Replace('.', '/');
                        string label = id + "." + kind + (CountKind(ops, kind) > 1 ? "#" + k : "") + "." + name;
                        var target = container.AsObject()[key];
                        double[] b = bounds?.GetDoubleArray(name);
                        JsonValue prior = priors != null && priors.TryGet(name, out var pv) ? pv : null;
                        if (target.Kind == JsonKind.Array)
                        {
                            var idx = new List<int>();
                            for (int i = 0; i < target.AsArray().Count; i++)
                            {
                                idx.Add(ps.Count);
                                ps.Add(Make(path + "/" + i, label + "[" + i + "]", key, FitValues.Value(target.AsArray()[i], double.NaN), b, prior, target.AsArray()[i]));
                            }
                            double sm = op.GetDouble("smoothness", 0);
                            if (sm > 0 && idx.Count >= 3) regs.Add(new Smoothness { Indices = idx.ToArray(), Weight = sm, Name = label });
                        }
                        else ps.Add(Make(path, label, key, FitValues.Value(target, double.NaN), b, prior, target));
                    }
                }
            }
            // the same number flagged twice (object flag and "fit" list): keep the first
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keep = new List<FitParameter>(); var map = new Dictionary<int, int>();
            for (int i = 0; i < ps.Count; i++) if (seen.Add(ps[i].Path)) { map[i] = keep.Count; keep.Add(ps[i]); }
            foreach (var r in regs) r.Indices = r.Indices.Where(map.ContainsKey).Select(i => map[i]).ToArray();
            return (keep, regs);
        }

        private static int CountKind(JsonArray ops, string kind) => ops.Count(o => o.Kind == JsonKind.Object && string.Equals(o.AsObject().GetString("kind", ""), kind, StringComparison.OrdinalIgnoreCase));

        private static (JsonValue Container, string Key) Locate(JsonObject op, string dotted)
        {
            var parts = dotted.Split('.');
            JsonValue cur = op;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (cur.Kind != JsonKind.Object || !cur.AsObject().TryGet(parts[i], out var nx)) return (null, null);
                cur = nx;
            }
            if (cur.Kind != JsonKind.Object || !cur.AsObject().Has(parts[parts.Length - 1])) return (null, null);
            return (cur, parts[parts.Length - 1]);
        }

        private static void Walk(JsonValue v, string path, string label, string key, List<FitParameter> ps)
        {
            if (v == null) return;
            if (FitValues.IsFitObject(v))
            {
                var o = v.AsObject();
                if (o.GetBool("fit", false)) ps.Add(Make(path, o.GetString("name", label), key, o.GetDouble("value", double.NaN), null, null, v));
                return;
            }
            if (v.Kind == JsonKind.Object)
            {
                foreach (var kv in v.AsObject())
                {
                    if (kv.Key == "fit" || kv.Key == "bounds" || kv.Key == "priors" || kv.Key == "model") continue;
                    Walk(kv.Value, path + "/" + kv.Key, label + "." + kv.Key, kv.Key, ps);
                }
            }
            else if (v.Kind == JsonKind.Array)
            {
                var a = v.AsArray();
                for (int i = 0; i < a.Count; i++) Walk(a[i], path + "/" + i, label + "[" + i + "]", key, ps);
            }
        }

        private static FitParameter Make(string path, string label, string key, double value, double[] bounds, JsonValue prior, JsonValue source)
        {
            var p = new FitParameter { Path = path, Name = label, Initial = value, Origin = "transform", Unit = FitValues.UnitOf(key) };
            if (FitValues.IsFitObject(source))
            {
                var o = source.AsObject();
                p.Min = o.GetDouble("min", p.Min); p.Max = o.GetDouble("max", p.Max);
                p.Scale = o.GetDouble("scale", double.NaN); p.Step = o.GetDouble("step", double.NaN);
                if (o.Has("unit")) p.Unit = o.GetString("unit", p.Unit);
                if (o.TryGet("prior", out var pr)) prior = prior ?? pr;
            }
            if (bounds != null && bounds.Length == 2) { p.Min = bounds[0]; p.Max = bounds[1]; }
            if (prior != null && !prior.IsNull)
            {
                if (prior.Kind == JsonKind.Number) { p.PriorMean = value; p.PriorSigma = prior.AsDouble(); }
                else { var po = prior.AsObject(); p.PriorMean = po.GetDouble("mean", value); p.PriorSigma = po.GetDouble("sigma", double.NaN); }
                if (!(p.PriorSigma > 0)) throw new JsonException("fit parameter " + label + ": prior sigma must be > 0");
            }
            if (double.IsNaN(p.Scale) && double.IsInfinity(p.Max - p.Min)) p.Scale = FitValues.TypicalScale(key, value);
            if (p.Min >= p.Max) throw new JsonException("fit parameter " + label + ": min must be below max");
            return p;
        }
    }
}
