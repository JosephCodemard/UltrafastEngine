using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Transverse
{
    /// <summary>Special functions of the beam sources: Hermite and associated Laguerre polynomials, the Airy function Ai.</summary>
    public static class BeamFunctions
    {
        /// <summary>Physicists' Hermite polynomial H_n(x) (H_0 = 1, H_1 = 2x), by recurrence.</summary>
        public static double Hermite(int n, double x)
        {
            double a = 1, b = 2 * x;
            if (n == 0) return a;
            for (int k = 1; k < n; k++) { double c = 2 * x * b - 2 * k * a; a = b; b = c; }
            return b;
        }

        /// <summary>Associated Laguerre polynomial L_p^a(x) (L_0 = 1, L_1 = 1 + a - x), by recurrence.</summary>
        public static double Laguerre(int p, int a, double x)
        {
            double l0 = 1, l1 = 1 + a - x;
            if (p == 0) return l0;
            for (int k = 1; k < p; k++) { double c = ((2 * k + 1 + a - x) * l1 - (k + a) * l0) / (k + 1); l0 = l1; l1 = c; }
            return l1;
        }

        /// <summary>Airy function Ai(x): Maclaurin series for |x| &lt; 5, the asymptotic series beyond (Abramowitz &amp; Stegun 10.4.2, 10.4.59, 10.4.60).</summary>
        public static double Airy(double x)
        {
            if (Math.Abs(x) < 5)
            {
                const double c1 = 0.355028053887817239, c2 = 0.258819403792806798;
                double f = 1, g = x, tf = 1, tg = x, x3 = x * x * x;
                for (int k = 1; k < 60; k++)
                {
                    tf *= x3 / ((3 * k - 1) * (3 * k));
                    tg *= x3 / ((3 * k) * (3 * k + 1));
                    f += tf; g += tg;
                    if (Math.Abs(tf) + Math.Abs(tg) < 1e-17 * (Math.Abs(f) + Math.Abs(g))) break;
                }
                return c1 * f - c2 * g;
            }
            double ax = Math.Abs(x), z = 2.0 / 3 * ax * Math.Sqrt(ax);
            // u_k = u_(k-1) (6k-5)(6k-3)(6k-1) / (216 k (2k-1)); terms are cut where they stop shrinking
            const int K = 12;
            var u = new double[K]; u[0] = 1;
            for (int k = 1; k < K; k++) u[k] = u[k - 1] * (6 * k - 5) * (6 * k - 3) * (6 * k - 1) / (216.0 * k * (2 * k - 1));
            if (x > 0)
            {
                double s = 0, zk = 1, sign = 1;
                for (int k = 0; k < K; k++) { s += sign * u[k] / zk; zk *= z; sign = -sign; }
                return Math.Exp(-z) / (2 * Math.Sqrt(Math.PI) * Math.Sqrt(Math.Sqrt(ax))) * s;
            }
            double p = 0, q = 0, zp = 1, sgn = 1;
            for (int k = 0; k < K; k += 2) { p += sgn * u[k] / zp; q += sgn * u[k + 1] / (zp * z); zp *= z * z; sgn = -sgn; }
            double a = z + Math.PI / 4;
            return (Math.Sin(a) * p - Math.Cos(a) * q) / (Math.Sqrt(Math.PI) * Math.Sqrt(Math.Sqrt(ax)));
        }
    }

    /// <summary>
    /// A transverse map from a file: amplitude and phase on x-y axes (arrays <c>x_m</c>, <c>y_m</c>, 2-D [x][y]) or against the
    /// radius (<c>r_m</c>). NPZ keys: <c>fluence</c> (any scale) or <c>amplitude</c>, and optionally <c>phase_rad</c>.
    /// </summary>
    public sealed class TransverseMap
    {
        public double[] X, Y, R, Amplitude, Phase;
        public bool Radial => R != null;

        /// <summary>Load a map. <paramref name="background"/>: a number subtracted from the fluence, or "auto" (the median of the map's edge).</summary>
        public static TransverseMap Load(string path, JsonValue background, string origin)
        {
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException(origin + ": \"file\" is needed (an NPZ with x_m, y_m or r_m, and fluence or amplitude).");
            if (!File.Exists(path)) throw new InvalidOperationException(origin + ": file not found: " + path);
            var a = NpzWriter.Read(path).ToDictionary(n => n.Name, n => n, StringComparer.OrdinalIgnoreCase);
            var map = new TransverseMap();
            NdArray Opt(string k) => a.TryGetValue(k, out var v) ? v : null;
            var fl = Opt("fluence"); var am = Opt("amplitude") ?? Opt("transmission");
            if (fl == null && am == null) throw new InvalidOperationException(origin + ": the file has neither \"fluence\" nor \"amplitude\" (found: " + string.Join(", ", a.Keys) + ").");
            var vals = fl ?? am;
            if (Opt("r_m") != null)
            {
                map.R = Opt("r_m").Data;
                if (vals.Data.Length != map.R.Length) throw new InvalidOperationException(origin + ": the map must have one value per r_m.");
                if (map.R.Zip(map.R.Skip(1), (p, q) => q - p).Any(d => !(d > 0))) throw new InvalidOperationException(origin + ": r_m must increase.");
            }
            else
            {
                map.X = Opt("x_m")?.Data ?? throw new InvalidOperationException(origin + ": the file needs r_m, or x_m and y_m.");
                map.Y = Opt("y_m")?.Data ?? throw new InvalidOperationException(origin + ": the file needs r_m, or x_m and y_m.");
                if (vals.Data.Length != map.X.Length * map.Y.Length) throw new InvalidOperationException(origin + ": the map must have shape [x][y] (x_m x y_m values).");
            }
            var data = (double[])vals.Data.Clone();
            if (fl != null)
            {
                double bg = 0;
                if (background != null && !background.IsNull)
                    bg = background.Kind == JsonKind.Number ? background.AsDouble() : map.EdgeMedian(data);
                for (int i = 0; i < data.Length; i++) data[i] = Math.Sqrt(Math.Max(0, data[i] - bg));
            }
            map.Amplitude = data;
            map.Phase = Opt("phase_rad")?.Data;
            if (map.Phase != null && map.Phase.Length != data.Length) throw new InvalidOperationException(origin + ": phase_rad must have the shape of the map.");
            return map;
        }

        private double EdgeMedian(double[] d)
        {
            var e = new List<double>();
            if (Radial) e.Add(d[d.Length - 1]);
            else
            {
                int nx = X.Length, ny = Y.Length;
                for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) if (i == 0 || j == 0 || i == nx - 1 || j == ny - 1) e.Add(d[i * ny + j]);
            }
            e.Sort();
            return e[e.Count / 2];
        }

        private static bool Locate(double[] a, double v, out int i, out double t)
        {
            i = 0; t = 0;
            if (v < a[0] || v > a[a.Length - 1]) return false;
            int lo = 0, hi = a.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (a[mid] <= v) lo = mid; else hi = mid; }
            i = lo; t = hi > lo ? (v - a[lo]) / (a[hi] - a[lo]) : 0;
            return true;
        }

        /// <summary>Bilinear sample (linear on a radial map) of a map array at (x, y); 0 outside the map.</summary>
        public double Sample(double[] d, double x, double y)
        {
            if (d == null) return 0;
            if (Radial)
            {
                if (!Locate(R, Math.Sqrt(x * x + y * y), out int i, out double t)) return 0;
                return d[i] * (1 - t) + d[Math.Min(i + 1, R.Length - 1)] * t;
            }
            if (!Locate(X, x, out int ix, out double u) || !Locate(Y, y, out int iy, out double v)) return 0;
            int ny = Y.Length, ix1 = Math.Min(ix + 1, X.Length - 1), iy1 = Math.Min(iy + 1, ny - 1);
            return d[ix * ny + iy] * (1 - u) * (1 - v) + d[ix1 * ny + iy] * u * (1 - v) + d[ix * ny + iy1] * (1 - u) * v + d[ix1 * ny + iy1] * u * v;
        }
    }

    /// <summary>
    /// The structured beam sources of <c>initialProfile</c> (docs/reference/transverse.md#initial-profile): amplitude (with its sign) and
    /// phase at each basis point. w is the 1/e^2 radius of the fundamental Gaussian (of the envelope for Bessel-Gauss).
    /// </summary>
    public static class BeamShapes
    {
        public static bool IsStructured(string kind) => kind == "hermitegauss" || kind == "laguerregauss" || kind == "besselgauss" || kind == "airy" || kind == "measured";

        public static void Fill(string kind, ITransverseBasis b, JsonObject s, double wx, double wy, double omega, double dx, double dy, string origin, double[] amp, out double[] phase)
        {
            phase = null;
            int M = b.M;
            bool radial = b.Kind == "radial";
            switch (kind)
            {
                case "hermitegauss":
                {
                    int mx = s.GetInt("modeX", 0), my = s.GetInt("modeY", 0);
                    if (mx < 0 || my < 0) throw new InvalidOperationException("element " + origin + ": modeX and modeY must be >= 0.");
                    if (radial && (mx != 0 || my != 0)) throw new InvalidOperationException("element " + origin + ": a Hermite-Gauss mode needs a cartesian transverse grid (\"model\": \"cartesian\").");
                    for (int m = 0; m < M; m++)
                    {
                        double x = (radial ? b.Radius[m] : b.X[m] - dx) / wx, y = (radial ? 0 : b.Y[m] - dy) / wy;
                        amp[m] = BeamFunctions.Hermite(mx, Math.Sqrt(2) * x) * BeamFunctions.Hermite(my, Math.Sqrt(2) * y) * Math.Exp(-x * x - y * y);
                    }
                    break;
                }
                case "laguerregauss":
                {
                    int p = s.GetInt("modeP", 0), l = s.GetInt("modeL", 0), al = Math.Abs(l);
                    if (p < 0) throw new InvalidOperationException("element " + origin + ": modeP must be >= 0.");
                    if (radial && l != 0) throw new InvalidOperationException("element " + origin + ": a Laguerre-Gauss mode with modeL != 0 has a helical phase: it needs a cartesian transverse grid.");
                    phase = new double[M];
                    for (int m = 0; m < M; m++)
                    {
                        double x = radial ? b.Radius[m] : b.X[m] - dx, y = radial ? 0 : b.Y[m] - dy, u = 2 * (x * x + y * y) / (wx * wx);
                        amp[m] = Math.Pow(u, al / 2.0) * BeamFunctions.Laguerre(p, al, u) * Math.Exp(-u / 2);
                        phase[m] = l * Math.Atan2(y, x);
                    }
                    break;
                }
                case "besselgauss":
                {
                    double kr = s.GetDouble("radialWavenumberPerM", double.NaN);
                    if (double.IsNaN(kr)) kr = omega / PhysicalConstants.SpeedOfLightMPerS * Math.Sin(s.GetDouble("coneAngleRad", double.NaN));
                    if (!(kr > 0)) throw new InvalidOperationException("element " + origin + ": besselGauss needs coneAngleRad or radialWavenumberPerM.");
                    for (int m = 0; m < M; m++)
                    {
                        double x = radial ? b.Radius[m] : b.X[m] - dx, y = radial ? 0 : b.Y[m] - dy, r2 = x * x + y * y;
                        amp[m] = Bessel.J0(kr * Math.Sqrt(r2)) * Math.Exp(-r2 / (wx * wx));
                    }
                    break;
                }
                case "airy":
                {
                    if (radial) throw new InvalidOperationException("element " + origin + ": an Airy beam needs a cartesian transverse grid.");
                    double a = s.GetDouble("truncation", 0.1);
                    if (!(a > 0)) throw new InvalidOperationException("element " + origin + ": truncation must be > 0 (a pure Airy beam has infinite energy).");
                    for (int m = 0; m < M; m++)
                    {
                        double x = (b.X[m] - dx) / wx, y = (b.Y[m] - dy) / wy;
                        amp[m] = BeamFunctions.Airy(x) * Math.Exp(a * x) * (b is CartesianBasis c && c.Ny == 1 ? 1 : BeamFunctions.Airy(y) * Math.Exp(a * y));
                    }
                    break;
                }
                case "measured":
                {
                    var map = TransverseMap.Load(s.GetString("file", null), s["background"], "element " + origin + ".initialProfile");
                    if (radial && !map.Radial) throw new InvalidOperationException("element " + origin + ": an x-y map needs a cartesian transverse grid (or give r_m for a radial one).");
                    if (map.Phase != null) phase = new double[M];
                    for (int m = 0; m < M; m++)
                    {
                        double x = radial ? b.Radius[m] : b.X[m] - dx, y = radial ? 0 : b.Y[m] - dy;
                        amp[m] = map.Sample(map.Amplitude, x, y);
                        if (phase != null) phase[m] = map.Sample(map.Phase, x, y);
                    }
                    break;
                }
            }
        }
    }
}
