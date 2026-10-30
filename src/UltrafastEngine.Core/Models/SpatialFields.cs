using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Models
{
    /// <summary>
    /// A scalar quantity that varies with position inside an element: f(x, y, z) with x, y measured from the beam
    /// axis and z from the element start (m). Used for position-dependent material properties -- an index change
    /// Delta n(x, y, z) (thermal lens, GRIN, density gradient), an extra absorption alpha(x, y, z), a scale factor on
    /// n2 -- and by gain media for doping and pump profiles. Evaluated on whole arrays of points.
    /// </summary>
    public interface ISpatialField
    {
        string Description { get; }
        /// <summary>False if f does not depend on z (evaluated once per element instead of once per step).</summary>
        bool DependsOnZ { get; }
        void Evaluate(double[] x, double[] y, double[] z, double[] valueOut);
    }

    /// <summary>Values already sampled on a grid's points (the absorbing boundary layer).</summary>
    public sealed class PointValuesField : ISpatialField
    {
        private readonly double[] _v;
        public PointValuesField(double[] values, string description) { _v = values; Description = description; }
        public string Description { get; }
        public bool DependsOnZ => false;
        public void Evaluate(double[] x, double[] y, double[] z, double[] valueOut) => System.Array.Copy(_v, valueOut, _v.Length);
    }

    /// <summary>f(x, y, z) from a registered callback (C#, Python or C).</summary>
    public delegate void FieldCallback(double[] x, double[] y, double[] z, double[] valueOut);

    public static class SpatialField
    {
        public static double At(this ISpatialField f, double x, double y, double z)
        {
            var o = new double[1]; f.Evaluate(new[] { x }, new[] { y }, new[] { z }, o); return o[0];
        }

        /// <summary>Values at the given points for one z.</summary>
        public static double[] AtPoints(this ISpatialField f, double[] x, double[] y, double z)
        {
            var o = new double[x.Length]; var zz = new double[x.Length];
            for (int i = 0; i < zz.Length; i++) zz[i] = z;
            f.Evaluate(x, y, zz, o);
            return o;
        }

        internal static void Register(ModelRegistry r)
        {
            r.Register<ISpatialField>("field", "constant", (s, _) => new ConstantField(s.GetRequired("value")));
            r.Register<ISpatialField>("field", "expression", (s, _) => new ExpressionField(s.Json.GetRequiredString("expr", s.Origin), s.Params()));
            r.Register<ISpatialField>("field", "gaussian", (s, _) => new GaussianField(s.GetDouble("amplitude", 1.0), s.GetRequired("radiusM"), s.Json.GetInt("order", 1),
                s.GetDouble("centerXM", 0), s.GetDouble("centerYM", 0), s.GetDouble("background", 0)));
            r.Register<ISpatialField>("field", "superGaussian", (s, _) => new GaussianField(s.GetDouble("amplitude", 1.0), s.GetRequired("radiusM"), s.Json.GetInt("order", 4),
                s.GetDouble("centerXM", 0), s.GetDouble("centerYM", 0), s.GetDouble("background", 0)));
            r.Register<ISpatialField>("field", "table", (s, _) => TableField.FromSpec(s));
            r.Register<ISpatialField>("field", "callback", (s, reg) => new CallbackField(s.Json.GetString("name"), reg.GetCallback<FieldCallback>(s.Json.GetString("name"), s.Origin)));
        }
    }

    public sealed class ConstantField : ISpatialField
    {
        private readonly double _v;
        public ConstantField(double v) { _v = v; }
        public string Description => "constant " + _v.ToString("G4", CultureInfo.InvariantCulture);
        public bool DependsOnZ => false;
        public void Evaluate(double[] x, double[] y, double[] z, double[] o) { for (int i = 0; i < o.Length; i++) o[i] = _v; }
    }

    /// <summary>Expression in x, y, z, r = sqrt(x^2 + y^2), theta = atan2(y, x) (and params), e.g.
    /// "-dn0 * (r/w)^2" or "alpha0 * exp(-z/L0)".</summary>
    public sealed class ExpressionField : ISpatialField
    {
        private static readonly string[] Vars = { "x", "y", "z", "r", "theta" };
        private readonly CompiledExpression _e;
        public ExpressionField(string expr, IReadOnlyDictionary<string, double> p) { _e = CompiledExpression.Compile(expr, Vars, p); DependsOnZ = System.Text.RegularExpressions.Regex.IsMatch(expr, @"(?<![A-Za-z0-9_])z(?![A-Za-z0-9_])"); }
        public string Description => "expression \"" + _e.Text + "\"";
        public bool DependsOnZ { get; }
        public void Evaluate(double[] x, double[] y, double[] z, double[] o)
        {
            int n = o.Length; var r = new double[n]; var th = new double[n];
            for (int i = 0; i < n; i++) { r[i] = Math.Sqrt(x[i] * x[i] + y[i] * y[i]); th[i] = Math.Atan2(y[i], x[i]); }
            var v = _e.Evaluate(new Dictionary<string, double[]> { { "x", x }, { "y", y }, { "z", z }, { "r", r }, { "theta", th } }, n);
            Array.Copy(v, o, n);
        }
    }

    /// <summary>amplitude * exp(-2 (r / radius)^(2 order)) + background (order 1: Gaussian with 1/e^2 radius).</summary>
    public sealed class GaussianField : ISpatialField
    {
        private readonly double _a, _w, _x0, _y0, _bg; private readonly int _ord;
        public GaussianField(double amplitude, double radiusM, int order, double x0, double y0, double background)
        { _a = amplitude; _w = radiusM; _ord = Math.Max(1, order); _x0 = x0; _y0 = y0; _bg = background; }
        public string Description => string.Format(CultureInfo.InvariantCulture, "{0} {1:G4} x exp(-2 (r/{2:G4} mm)^{3}){4}", _ord == 1 ? "gaussian" : "super-Gaussian", _a, _w * 1e3, 2 * _ord, _bg != 0 ? " + " + _bg.ToString("G4", CultureInfo.InvariantCulture) : "");
        public bool DependsOnZ => false;
        public void Evaluate(double[] x, double[] y, double[] z, double[] o)
        {
            for (int i = 0; i < o.Length; i++)
            {
                double dx = x[i] - _x0, dy = y[i] - _y0, u = (dx * dx + dy * dy) / (_w * _w);
                o[i] = _bg + _a * Math.Exp(-2 * Math.Pow(u, _ord));
            }
        }
    }

    /// <summary>
    /// Gridded data with linear interpolation: "axes" = "r", "z", "rz", "xy", "xyz" (or "x", "y", "xz"), with the
    /// axis arrays ("r", "x", "y", "z", ascending, m) and "values" flattened in axis order (last axis fastest).
    /// "outside": "clamp" (default: nearest edge value) or a number used beyond the grid. Typical use: a temperature
    /// or index map exported from a finite-element model.
    /// </summary>
    public sealed class TableField : ISpatialField
    {
        private readonly string _axes; private readonly double[][] _ax; private readonly double[] _v; private readonly double? _outside;
        private TableField(string axes, double[][] ax, double[] v, double? outside) { _axes = axes; _ax = ax; _v = v; _outside = outside; }

        public static TableField FromSpec(ModelSpec s)
        {
            string axes = s.Json.GetString("axes", "r").ToLowerInvariant();
            var arrs = axes.Select(c => s.Json.GetDoubleArray(c.ToString()) ?? throw new JsonException(s.Origin + ": table field with axes \"" + axes + "\" needs an array \"" + c + "\"")).ToArray();
            var v = s.Json.GetDoubleArray("values") ?? throw new JsonException(s.Origin + ": table field needs \"values\"");
            long need = arrs.Aggregate(1L, (a, b) => a * b.Length);
            if (v.Length != need) throw new JsonException(string.Format(CultureInfo.InvariantCulture, "{0}: table field has {1} values, axes \"{2}\" need {3}", s.Origin, v.Length, axes, need));
            foreach (var a in arrs) for (int i = 1; i < a.Length; i++) if (!(a[i] > a[i - 1])) throw new JsonException(s.Origin + ": table axes must be strictly ascending");
            double? outside = null;
            if (s.Json.TryGet("outside", out var ov) && ov.Kind == JsonKind.Number) outside = ov.AsDouble();
            return new TableField(axes, arrs, v, outside);
        }

        public string Description => string.Format(CultureInfo.InvariantCulture, "table over ({0}), {1} values", string.Join(", ", _axes.Select(c => c.ToString())), _v.Length);
        public bool DependsOnZ => _axes.Contains('z');

        public void Evaluate(double[] x, double[] y, double[] z, double[] o)
        {
            int d = _axes.Length; var q = new double[d];
            for (int i = 0; i < o.Length; i++)
            {
                bool outside = false;
                for (int k = 0; k < d; k++)
                {
                    char c = _axes[k];
                    q[k] = c == 'r' ? Math.Sqrt(x[i] * x[i] + y[i] * y[i]) : c == 'x' ? x[i] : c == 'y' ? y[i] : z[i];
                    if (q[k] < _ax[k][0] || q[k] > _ax[k][_ax[k].Length - 1]) outside = true;
                }
                o[i] = outside && _outside.HasValue ? _outside.Value : Interp(q, d);
            }
        }

        private double Interp(double[] q, int d)
        {
            var lo = new int[d]; var t = new double[d];
            for (int k = 0; k < d; k++)
            {
                var a = _ax[k]; int n = a.Length;
                if (n == 1) { lo[k] = 0; t[k] = 0; continue; }
                double v = Math.Max(a[0], Math.Min(a[n - 1], q[k]));
                int j = Array.BinarySearch(a, v); if (j < 0) j = ~j - 1;
                j = Math.Max(0, Math.Min(n - 2, j));
                lo[k] = j; t[k] = (v - a[j]) / (a[j + 1] - a[j]);
            }
            double acc = 0;
            for (int corner = 0; corner < (1 << d); corner++)
            {
                double w = 1; int idx = 0;
                for (int k = 0; k < d; k++)
                {
                    int bit = (corner >> k) & 1;
                    if (_ax[k].Length == 1 && bit == 1) { w = 0; break; }
                    w *= bit == 1 ? t[k] : 1 - t[k];
                    idx = idx * _ax[k].Length + lo[k] + bit;
                }
                if (w != 0) acc += w * _v[idx];
            }
            return acc;
        }
    }

    public sealed class CallbackField : ISpatialField
    {
        private readonly FieldCallback _cb; private readonly string _name;
        public CallbackField(string name, FieldCallback cb) { _name = name; _cb = cb ?? throw new ArgumentNullException(nameof(cb)); }
        public string Description => "callback @" + _name;
        public bool DependsOnZ => true;
        public void Evaluate(double[] x, double[] y, double[] z, double[] o) => _cb(x, y, z, o);
    }

    /// <summary>
    /// The position-dependent properties of one element: an index change Delta n (real, frequency independent,
    /// added to the material index), an extra intensity absorption coefficient alpha (1/m) and a scale factor on n2.
    /// </summary>
    public sealed class SpatialProperties
    {
        public ISpatialField IndexChange, Absorption, N2Scale;
        public bool Any => IndexChange != null || Absorption != null || N2Scale != null;
        public bool DependsOnZ => (IndexChange?.DependsOnZ ?? false) || (Absorption?.DependsOnZ ?? false) || (N2Scale?.DependsOnZ ?? false);

        public string Describe() => string.Join("; ", new[] {
            IndexChange != null ? "Delta n: " + IndexChange.Description : null,
            Absorption != null ? "alpha: " + Absorption.Description : null,
            N2Scale != null ? "n2 x " + N2Scale.Description : null }.Where(s => s != null));

        /// <summary>Combine: index changes and absorptions add, n2 scales multiply.</summary>
        public static SpatialProperties Combine(SpatialProperties a, SpatialProperties b)
        {
            if (a == null || !a.Any) return b; if (b == null || !b.Any) return a;
            return new SpatialProperties { IndexChange = Sum(a.IndexChange, b.IndexChange), Absorption = Sum(a.Absorption, b.Absorption), N2Scale = Product(a.N2Scale, b.N2Scale) };
        }

        private static ISpatialField Sum(ISpatialField p, ISpatialField q) => p == null ? q : q == null ? p : new CombinedField(p, q, false);
        private static ISpatialField Product(ISpatialField p, ISpatialField q) => p == null ? q : q == null ? p : new CombinedField(p, q, true);

        private sealed class CombinedField : ISpatialField
        {
            private readonly ISpatialField _a, _b; private readonly bool _mul;
            public CombinedField(ISpatialField a, ISpatialField b, bool mul) { _a = a; _b = b; _mul = mul; }
            public string Description => "(" + _a.Description + (_mul ? ") x (" : ") + (") + _b.Description + ")";
            public bool DependsOnZ => _a.DependsOnZ || _b.DependsOnZ;
            public void Evaluate(double[] x, double[] y, double[] z, double[] o)
            {
                var t = new double[o.Length]; _a.Evaluate(x, y, z, o); _b.Evaluate(x, y, z, t);
                for (int i = 0; i < o.Length; i++) o[i] = _mul ? o[i] * t[i] : o[i] + t[i];
            }
        }
    }
}
