// TileSource.cs -- the coherent tile source: an input beam made of N sub-beams (a tiled-aperture amplifier array).
//
//   static class TileSource
//     static List<Tile> Layout(JsonObject spec, string origin)             positions from layout square | hex | ring | explicit positionsM
//     static Complex[] Field(ITransverseBasis b, JsonObject spec, double[] omegas, string origin)
//                                                                          sum of the tiles' complex fields, [m * omegas.Length + f], NOT normalised
//   Each tile t: amplitude a_t, Gaussian (1/e^2 intensity radius tileRadiusM; or tophat / superGaussian) centred at (x_t, y_t),
//   phase  piston_t + k(omega) (sin(tiltX_t) (x - x_t) + sin(tiltY_t) (y - y_t)) + (omega - omega_ref) delay_t,
//   with omega_ref the central stored frequency (the piston is the phase at the carrier; the delay moves the envelope: spectral factor e^{+i omega tau}
//   for E = Re{A e^{-i omega t}}). The sum is normalised by the caller (TransverseOps.FromShape) to unit energy at every frequency, so N tiles
//   share the pulse energy: the combined on-axis far-field peak is N x that of one tile carrying all of it (N^2 for equal powers per tile).
// See docs/reference/transverse.md#coherent-tiles.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Transverse
{
    public static class TileSource
    {
        public struct Tile { public double X, Y; }

        /// <summary>Tile centres. hex: axial coordinates (i, j) with max(|i|, |j|, |i+j|) &lt;= rings, ordered by j then i; 1, 7, 19, 37, 61 tiles for rings 0..4.</summary>
        public static List<Tile> Layout(JsonObject s, string origin)
        {
            var t = new List<Tile>();
            if (s.Has("positionsM"))
            {
                foreach (var p in s["positionsM"].AsDoubleMatrix())
                {
                    if (p.Length != 2) throw new InvalidOperationException("element " + origin + ": initialProfile.positionsM is a list of [x, y] pairs.");
                    t.Add(new Tile { X = p[0], Y = p[1] });
                }
                return t;
            }
            string layout = s.GetString("layout", "square").ToLowerInvariant();
            double pitch = s.GetDouble("pitchM", double.NaN);
            if (layout == "ring")
            {
                int n = s.GetInt("count", 6); double R = s.GetDouble("ringRadiusM", pitch * n / (2 * Math.PI));
                if (!(R > 0)) throw new InvalidOperationException("element " + origin + ": a ring of tiles needs ringRadiusM (or pitchM).");
                for (int i = 0; i < n; i++) t.Add(new Tile { X = R * Math.Cos(2 * Math.PI * i / n), Y = R * Math.Sin(2 * Math.PI * i / n) });
                return t;
            }
            if (!(pitch > 0)) throw new InvalidOperationException("element " + origin + ": tiles need pitchM (centre-to-centre distance).");
            if (layout == "hex")
            {
                int R = s.GetInt("rings", 1);
                for (int j = -R; j <= R; j++)
                    for (int i = -R; i <= R; i++)
                        if (Math.Abs(i + j) <= R) t.Add(new Tile { X = pitch * (i + j / 2.0), Y = pitch * j * Math.Sqrt(3) / 2 });
                return t;
            }
            if (layout != "square") throw new InvalidOperationException("element " + origin + ": tiles layout must be \"square\", \"hex\", \"ring\" or give positionsM.");
            int nx = s.GetInt("countX", s.GetInt("count", 2)), ny = s.GetInt("countY", s.Has("countX") ? 1 : nx);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    t.Add(new Tile { X = (i - (nx - 1) / 2.0) * pitch, Y = (j - (ny - 1) / 2.0) * pitch });
            return t;
        }

        private static double[] PerTile(JsonObject s, string key, int n, double fallback, string origin)
        {
            if (!s.TryGet(key, out var v)) return Enumerable.Repeat(fallback, n).ToArray();
            var a = v.AsDoubleArray();
            if (a.Length == 1) return Enumerable.Repeat(a[0], n).ToArray();
            if (a.Length != n) throw new InvalidOperationException("element " + origin + ": initialProfile." + key + " has " + a.Length + " values for " + n + " tiles.");
            return a;
        }

        public static Complex[] Field(ITransverseBasis b, JsonObject s, double[] omegas, string origin)
        {
            var tiles = Layout(s, origin); int n = tiles.Count, nf = omegas.Length, M = b.M;
            if (n < 1) throw new InvalidOperationException("element " + origin + ": no tiles.");
            double w = s.GetDouble("tileRadiusM", double.NaN);
            if (!(w > 0)) throw new InvalidOperationException("element " + origin + ": tiles need tileRadiusM (1/e^2 radius of one tile; hard radius for tileShape tophat).");
            string shape = s.GetString("tileShape", "gaussian").ToLowerInvariant(); int order = Math.Max(1, s.GetInt("order", 4));
            var amp = PerTile(s, "amplitudes", n, 1, origin); var piston = PerTile(s, "pistonRad", n, 0, origin);
            var tx = PerTile(s, "tileTiltXRad", n, 0, origin); var ty = PerTile(s, "tileTiltYRad", n, 0, origin); var delay = PerTile(s, "delaysS", n, 0, origin);
            double noise = s.GetDouble("pistonRmsRad", 0);
            if (noise > 0)
            {
                var rnd = new Random(s.GetInt("seed", 1));
                for (int t = 0; t < n; t++)
                {
                    double u1 = 1 - rnd.NextDouble(), u2 = rnd.NextDouble();
                    piston[t] += noise * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
                }
            }
            double wRef = omegas[nf / 2];
            var data = new Complex[M * nf];
            for (int t = 0; t < n; t++)
            {
                var g = new double[M];
                for (int m = 0; m < M; m++)
                {
                    double x = b.X[m] - tiles[t].X, y = b.Y[m] - tiles[t].Y, r2 = (x * x + y * y) / (w * w);
                    g[m] = shape == "tophat" ? (r2 <= 1 ? 1 : 0) : shape == "supergaussian" ? Math.Exp(-Math.Pow(r2, order)) : Math.Exp(-r2);
                    g[m] *= amp[t];
                }
                for (int f = 0; f < nf; f++)
                {
                    double k = omegas[f] / PhysicalConstants.SpeedOfLightMPerS, st = Math.Sin(tx[t]), sy = Math.Sin(ty[t]);
                    double ph0 = piston[t] + (omegas[f] - wRef) * delay[t];
                    for (int m = 0; m < M; m++)
                    {
                        if (g[m] == 0) continue;
                        double ph = ph0 + k * (st * (b.X[m] - tiles[t].X) + sy * (b.Y[m] - tiles[t].Y));
                        data[m * nf + f] += Complex.FromPolarCoordinates(g[m], ph);
                    }
                }
            }
            return data;
        }
    }
}
