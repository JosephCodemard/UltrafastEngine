using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Retrieval
{
    /// <summary>
    /// Gerchberg-Saxton phase retrieval (Optik 35, 237 (1972)): the wavefront of a beam from its near-field and far-field
    /// intensities. The two planes are related by a Fourier transform, so the far-field image must be centred and sampled on the
    /// conjugate grid of the near field: far pixel = lambda f / (N dx). The result has the usual ambiguities: piston, and the twin
    /// phase -phi(-x, -y), which has the same near and far intensities when the near field is symmetric.
    /// </summary>
    public static class PhaseRetrieval
    {
        public sealed class Result
        {
            public double[] Phase, Amplitude, ErrorHistory;
            public double FarError => ErrorHistory[ErrorHistory.Length - 1];
        }

        /// <summary>Iterate on near and far amplitudes (nx x ny, row-major [x][y], far field centred). The error is the rms far-field
        /// amplitude mismatch relative to the far-field norm.</summary>
        public static Result GerchbergSaxton(double[] near, double[] far, int nx, int ny, int iterations = 200, double[] startPhase = null, double tolerance = 1e-6)
        {
            if (near.Length != nx * ny || far.Length != nx * ny) throw new ArgumentException("near and far maps must both have nx x ny values");
            if (nx % 2 != 0 || ny % 2 != 0) throw new ArgumentException("Gerchberg-Saxton needs even numbers of pixels");
            int M = nx * ny;
            var b = new CartesianBasis(nx, ny, 1, 1);    // only the transform is used; the pixel size does not enter
            double scale = Math.Sqrt(near.Sum(v => v * v) / Math.Max(1e-300, far.Sum(v => v * v))) * Math.Sqrt(M);
            // far field in FFT order
            var farK = new double[M];
            for (int ix = 0; ix < nx; ix++)
                for (int iy = 0; iy < ny; iy++)
                    farK[((ix + nx / 2) % nx) * ny + (iy + ny / 2) % ny] = far[ix * ny + iy] * scale;
            double norm = Math.Sqrt(farK.Sum(v => v * v));
            var e = new Complex[M];
            for (int m = 0; m < M; m++) e[m] = Complex.FromPolarCoordinates(near[m], startPhase?[m] ?? 0);
            var hist = new List<double>();
            for (int it = 0; it < iterations; it++)
            {
                b.ToK(e, 1);
                double err = 0;
                for (int m = 0; m < M; m++)
                {
                    double a = e[m].Magnitude, d = a - farK[m];
                    err += d * d;
                    e[m] = a > 0 ? e[m] * (farK[m] / a) : new Complex(farK[m], 0);
                }
                hist.Add(Math.Sqrt(err) / norm);
                b.ToSpace(e, 1);
                for (int m = 0; m < M; m++) { double a = e[m].Magnitude; e[m] = a > 0 ? e[m] * (near[m] / a) : new Complex(near[m], 0); }
                if (hist.Count > 1 && hist[hist.Count - 1] < tolerance) break;
            }
            return new Result { Phase = e.Select(z => z.Phase).ToArray(), Amplitude = (double[])near.Clone(), ErrorHistory = hist.ToArray() };
        }

        /// <summary>
        /// Error reduction stagnates in local minima, so it is started from a flat phase and from <paramref name="restarts"/> seeded
        /// smooth random phases (low-order polynomials over the near field), and the run with the smallest far-field error is
        /// continued. Reproducible from <paramref name="seed"/>.
        /// </summary>
        public static Result MultiStart(double[] near, double[] far, int nx, int ny, int iterations = 500, int restarts = 12, int seed = 1)
        {
            var rnd = new Random(seed);
            int trial = Math.Max(20, iterations / 10);
            Result best = GerchbergSaxton(near, far, nx, ny, trial);
            for (int r = 0; r < restarts; r++)
            {
                double a2 = 6 * (rnd.NextDouble() - 0.5), b2 = 6 * (rnd.NextDouble() - 0.5), c2 = 6 * (rnd.NextDouble() - 0.5), a3 = 6 * (rnd.NextDouble() - 0.5), b3 = 6 * (rnd.NextDouble() - 0.5);
                var start = new double[nx * ny];
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                    {
                        double x = 2.0 * i / nx - 1, y = 2.0 * j / ny - 1;
                        start[i * ny + j] = 4 * (a2 * x * x + b2 * y * y + c2 * x * y + a3 * x * x * x + b3 * y * y * y);
                    }
                var t = GerchbergSaxton(near, far, nx, ny, trial, start);
                if (t.FarError < best.FarError) best = t;
            }
            var done = GerchbergSaxton(near, far, nx, ny, iterations, best.Phase);
            done.ErrorHistory = best.ErrorHistory.Concat(done.ErrorHistory).ToArray();
            return done;
        }

        /// <summary>The "gerchbergSaxton" method of the retrieve command: an NPZ with near, far (intensities, [x][y]) and x_m, y_m.
        /// Writes phase.npz in the form initialProfile "measured" and mask "file" read.</summary>
        public static JsonObject Command(JsonObject r, string tracePath, string outputDir)
        {
            var a = NpzWriter.Read(tracePath).ToDictionary(n => n.Name, n => n, StringComparer.OrdinalIgnoreCase);
            foreach (var k in new[] { "near", "far", "x_m", "y_m" })
                if (!a.ContainsKey(k)) throw new JsonException("retrieve gerchbergSaxton: the file needs near, far, x_m and y_m (missing " + k + ").");
            int nx = a["x_m"].Data.Length, ny = a["y_m"].Data.Length;
            double[] Amp(string k) => a[k].Data.Select(v => Math.Sqrt(Math.Max(0, v))).ToArray();
            var op = r.GetObject("options");
            var res = MultiStart(Amp("near"), Amp("far"), nx, ny, op?.GetInt("iterations", 500) ?? 500, op?.GetInt("restarts", 12) ?? 12, op?.GetInt("seed", 1) ?? 1);
            if (outputDir != null)
            {
                Directory.CreateDirectory(outputDir);
                NpzWriter.Write(Path.Combine(outputDir, "phase.npz"), new[] { a["x_m"], a["y_m"], new NdArray("amplitude", res.Amplitude, nx, ny), new NdArray("phase_rad", res.Phase, nx, ny), new NdArray("error_history", res.ErrorHistory) });
            }
            return new JsonObject().Set("method", "gerchbergSaxton").Set("iterations", res.ErrorHistory.Length).Set("twinNote", "phi(x, y) and -phi(-x, -y) have the same intensities for a symmetric near field").Set("farFieldError", res.FarError);
        }
    }
}
