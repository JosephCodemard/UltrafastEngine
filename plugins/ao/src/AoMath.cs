// AoMath.cs -- small helpers shared by the ao plugin elements.
//   AoMath.Erf(x)                     error function, |error| < 1e-12 (series below |x| = 3, continued fraction above); net48 has no Math.Erf
//   AoMath.ReadMap(services, e, key, nx, ny, origin)   a [x][y] map given as a nested array, a flat array (with nx, ny), or an NPZ array
//   AoMath.Cauchy(coefficients)       dn(omega) from [A, B_m2, C_m4]: dn = A + B / lambda^2 + C / lambda^4
using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Plugins.Ao
{
    public static class AoMath
    {
        public static double Erf(double x)
        {
            double a = Math.Abs(x);
            double r;
            if (a < 3)
            {
                double term = a, sum = a, a2 = a * a;
                for (int n = 1; n < 80; n++) { term *= -a2 / n; double t = term / (2 * n + 1); sum += t; if (Math.Abs(t) < 1e-17 * Math.Abs(sum)) break; }
                r = 2 / Math.Sqrt(Math.PI) * sum;
            }
            else if (a > 6) r = 1;
            else
            {
                // erfc(x) = exp(-x^2) / (x sqrt(pi)) * 1 / (1 + (1/(2x^2)) / (1 + (2/(2x^2)) / (1 + ...)))
                double z = 2 * a * a, f = 1;
                for (int k = 60; k >= 1; k--) f = 1 + k / z / f;
                r = 1 - Math.Exp(-a * a) / (a * Math.Sqrt(Math.PI)) / f;
            }
            return x < 0 ? -r : r;
        }

        public static Func<double, double> Cauchy(double[] k)
        {
            double tw = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS;
            double A = k[0], B = k.Length > 1 ? k[1] : 0, C = k.Length > 2 ? k[2] : 0;
            return w => { double l2 = Math.Pow(tw / w, 2); return A + B / l2 + C / (l2 * l2); };
        }

        /// <summary>An array [x][y] as a flat x-major array. Forms: nested rows, a flat array with <paramref name="nx"/> * <paramref name="ny"/> values,
        /// or the named array of an NPZ file given under "file". Returns null when the key is absent. nx / ny are set from the shape when 0.</summary>
        public static double[] ReadMap(CompilerServices s, SystemElement e, string key, string npzKey, ref int nx, ref int ny, string origin, double pitchX = double.NaN, double pitchY = double.NaN)
        {
            if (e.Json.TryGet("file", out var fv) && npzKey != null)
            {
                string path = fv.AsString();
                string dir = s.System?.BaseDirectory ?? s.Run?.BaseDirectory;
                if (dir != null && !System.IO.Path.IsPathRooted(path)) path = System.IO.Path.Combine(dir, path);
                if (!System.IO.File.Exists(path)) throw new JsonException(origin + ": file not found: " + path);
                var arr = NpzWriter.Read(path).FirstOrDefault(a => string.Equals(a.Name, npzKey, StringComparison.OrdinalIgnoreCase));
                if (arr != null)
                {
                    if (arr.Shape.Length == 2 && (nx == 0 || ny == 0)) { nx = arr.Shape[0]; ny = arr.Shape[1]; }
                    return arr.Data;
                }
            }
            if (!e.Json.TryGet(key, out var v)) return null;
            if (v.Kind == JsonKind.String)
            {
                string name = v.AsString();
                if (!name.StartsWith("@") || !(pitchX > 0) || nx < 1 || ny < 1) throw new JsonException(origin + ": " + key + " is an array [x][y], a callback \"@name\" (needs the pixel counts and pitch)" + (npzKey != null ? ", or " + npzKey + " in the NPZ file." : "."));
                var cb = s.Registry.GetCallback<UltrafastEngine.Models.FieldCallback>(name.Substring(1), origin);
                int n = nx * ny; var x = new double[n]; var y = new double[n]; var z = new double[n]; var val = new double[n];
                double py = pitchY > 0 ? pitchY : pitchX;
                for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) { x[i * ny + j] = (i - (nx - 1) / 2.0) * pitchX; y[i * ny + j] = (j - (ny - 1) / 2.0) * py; }
                cb(x, y, z, val);
                return val;
            }
            var a0 = v.AsArray();
            bool nested = a0.Count > 0 && a0[0].Kind == JsonKind.Array;
            if (nested)
            {
                var rows = v.AsDoubleMatrix();
                if (nx == 0 || ny == 0) { nx = rows.Length; ny = rows[0].Length; }
                int nyl = ny; if (rows.Length != nx || rows.Any(r => r.Length != nyl)) throw new JsonException(origin + ": " + key + " must be [" + nx + "][" + ny + "].");
                var flat = new double[nx * ny];
                for (int i = 0; i < nx; i++) Array.Copy(rows[i], 0, flat, i * ny, ny);
                return flat;
            }
            var f = v.AsDoubleArray();
            if (nx == 0 || ny == 0) { nx = f.Length; ny = 1; }
            if (f.Length != nx * ny) throw new JsonException(origin + ": " + key + " has " + f.Length + " values, expected " + nx + " x " + ny + " = " + nx * ny + ".");
            return f;
        }
    }
}
