using System;
using System.Linq;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>The coherent tile source, <c>initialProfile.kind = "tiles"</c> (docs/reference/transverse.md#coherent-tiles): a tiled-aperture array of sub-beams.</summary>
    [TestArea("Coherent tile source: N sub-beams with piston, tilt and delay; combined far field, grating lobes, phase and delay errors")]
    public class TileSourceTests
    {
        private const double Lam = 1030e-9, F = 0.2, Pitch = 200e-6, Rt = 40e-6;
        private const int Nx = 2048;
        private static readonly double Dx = 5e-6;

        private static JsonObject Tiles(int n, params object[] extra)
        {
            var o = O("kind", "tiles", "layout", "square", "countX", n, "pitchM", Pitch, "tileRadiusM", Rt);
            foreach (var kv in O(extra)) o.Set(kv.Key, kv.Value);
            return o;
        }
        private static JsonArray Nums(params double[] v) { var a = new JsonArray(); foreach (var x in v) a.Add(x); return a; }

        /// <summary>Far field of the array: a lens and a focal-length gap on the same grid; returns the x cut of the focal plane.</summary>
        private static (double[] x, double[] f) Far(JsonObject tiles, double fwhmS = 1e-12, int samples = 4)
        {
            JsonObject Grid() => O("model", "cartesian", "nx", Nx, "ny", 1, "halfWidthM", Nx * Dx / 2, "absorbingBoundary", true, "spectralSamples", samples);
            var g0 = Grid(); g0.Set("initialProfile", tiles);
            var src = O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", g0);
            var foc = O("id", "z", "type", "gap", "lengthM", F, "material", "Vacuum", "transverse", Grid());
            var r = Go(Sys(src, O("id", "lens", "type", "lens", "focalLengthM", F), foc),
                       Spec(Gauss(Lam, fwhmS, 1e-3), O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "z")))));
            var bp = r.Instruments.First(i => i.Name == "bp");
            return (bp.Arrays.First(a => a.Name.EndsWith("x_m")).Data, bp.Arrays.First(a => a.Name.EndsWith("fluence_x_j_m2")).Data);
        }
        private static double OnAxis((double[] x, double[] f) s) { int i = Array.IndexOf(s.x.Select(Math.Abs).ToArray(), s.x.Select(Math.Abs).Min()); return s.f[i]; }

        [Test(Reference = "N equal, in-phase, coherent beams: on-axis far-field peak = N x the peak of one beam carrying all the energy (N^2 for equal power per beam); Goodman ch. 4 (array theorem)", Tolerance = "2 %")]
        public void InPhaseTiles_CombinePeakIntensity_ByN()
        {
            double one = OnAxis(Far(Tiles(1)));
            foreach (int n in new[] { 3, 7 })
                Assert.Close(n, OnAxis(Far(Tiles(n))) / one, 0.02, 0, n + " tiles");
        }

        [Test(Reference = "array theorem: the far field of tiles on a pitch p has lobes at lambda / p (grating lobes), spacing F lambda / p on the focal plane", Tolerance = "1.5 %")]
        public void Tiles_GiveGratingLobes_AtWavelengthOverPitch()
        {
            var (x, f) = Far(Tiles(7));
            double lobe = F * Lam / Pitch; int i1 = Enumerable.Range(0, x.Length).Where(i => Math.Abs(x[i] - lobe) < 0.3 * lobe).OrderByDescending(i => f[i]).First();
            Assert.Close(lobe, x[i1], 0.015, 0, "first lobe");
            int i2 = Enumerable.Range(0, x.Length).Where(i => Math.Abs(x[i] - 2 * lobe) < 0.3 * lobe).OrderByDescending(i => f[i]).First();
            Assert.Close(2 * lobe, x[i2], 0.015, 0, "second lobe");
        }

        [Test(Reference = "piston errors: the on-axis peak of the realised phases is |sum exp(i phi)|^2 / N^2 of the in-phase peak (exact for the draw); its expectation is 1/N + (1 - 1/N) exp(-sigma^2)", Tolerance = "1.5 %")]
        public void PistonErrors_CostTheRealisedCoherenceSum()
        {
            int n = 7; double ref0 = OnAxis(Far(Tiles(n)));
            foreach (double sigma in new[] { 0.3, 0.8, 1.5 })
            {
                var rnd = new Random(7); var ph = Enumerable.Range(0, n).Select(_ => sigma * Math.Sqrt(-2 * Math.Log(1 - rnd.NextDouble())) * Math.Cos(2 * Math.PI * rnd.NextDouble())).ToArray();
                double re = ph.Sum(Math.Cos), im = ph.Sum(Math.Sin), expect = (re * re + im * im) / (n * n);
                Assert.Close(expect, OnAxis(Far(Tiles(n, "pistonRad", Nums(ph)))) / ref0, 0.015, 0, "sigma " + sigma);
            }
            // the average over draws follows exp(-sigma^2) (the arithmetic behind the statement, many draws)
            var rr = new Random(3); double sg = 0.8, acc = 0; int draws = 20000;
            for (int d = 0; d < draws; d++) { double re = 0, im = 0; for (int t = 0; t < n; t++) { double p = sg * Math.Sqrt(-2 * Math.Log(1 - rr.NextDouble())) * Math.Cos(2 * Math.PI * rr.NextDouble()); re += Math.Cos(p); im += Math.Sin(p); } acc += (re * re + im * im) / (n * n); }
            Assert.Close(1.0 / n + (1 - 1.0 / n) * Math.Exp(-sg * sg), acc / draws, 0.01, 0, "expectation");
        }

        [Test(Reference = "delay mismatch of two in-phase Gaussian pulses (intensity FWHM T): the energy on axis is 2E + 2 Int A(t) A(t - tau) dt, so relative to tau = 0 it is (1 + C) / 2 with the field overlap C = exp(-ln2 (tau / T)^2)", Tolerance = "3 %")]
        public void DelayMismatch_CostsTheTemporalOverlap_OfTheTwoPulses()
        {
            double T = 30e-15; var pa = O("kind", "tiles", "layout", "square", "countX", 2, "pitchM", Pitch, "tileRadiusM", Rt);
            double ref0 = OnAxis(Far(pa, T, 24));
            foreach (double tau in new[] { 15e-15, 30e-15, 60e-15 })
            {
                var d = Far(Tiles(2, "delaysS", Nums(0, tau)), T, tau > 40e-15 ? 96 : 24);   // the stored frequencies must resolve exp(i omega tau): spacing x tau under ~0.5 rad
                double c = Math.Exp(-Math.Log(2) * (tau / T) * (tau / T));
                Assert.Close((1 + c) / 2, OnAxis(d) / ref0, 0.03, 0, "tau " + tau * 1e15 + " fs");
            }
        }

        [Test(Reference = "geometry: a hex layout of r rings has 1 + 3 r (r + 1) tiles, each at distance pitch from its nearest neighbour; a ring of n tiles lies on radius R at angles 2 pi i / n; explicit positionsM are used as given", Tolerance = "1e-12")]
        public void Layouts_HexRingAndExplicit_PlaceTheTiles()
        {
            var hex = UltrafastEngine.Transverse.TileSource.Layout(O("kind", "tiles", "layout", "hex", "rings", 2, "pitchM", Pitch), "t");
            Assert.Equal(19, hex.Count, "hex, 2 rings");
            foreach (var t in hex)
                Assert.Close(Pitch, hex.Where(u => !(u.X == t.X && u.Y == t.Y)).Min(u => Math.Sqrt((u.X - t.X) * (u.X - t.X) + (u.Y - t.Y) * (u.Y - t.Y))), 1e-12, 0, "nearest neighbour");
            double R = 1e-3; var ring = UltrafastEngine.Transverse.TileSource.Layout(O("kind", "tiles", "layout", "ring", "count", 6, "ringRadiusM", R), "t");
            Assert.Equal(6, ring.Count, "ring count");
            for (int i = 0; i < 6; i++) { Assert.Close(R * Math.Cos(Math.PI * i / 3), ring[i].X, 0, 1e-15, "ring x"); Assert.Close(R * Math.Sin(Math.PI * i / 3), ring[i].Y, 0, 1e-15, "ring y"); }
            var ex = UltrafastEngine.Transverse.TileSource.Layout(O("kind", "tiles", "positionsM", A(Nums(1e-3, 2e-3), Nums(-3e-3, 0))), "t");
            Assert.Close(-3e-3, ex[1].X, 0, 1e-18, "explicit"); Assert.Close(2e-3, ex[0].Y, 0, 1e-18, "explicit");
        }

        [Test(Reference = "a tile tilted by theta (tileTiltXRad) focuses at F theta (Goodman, Fourier optics ch. 5)", Tolerance = "1 %")]
        public void TiltedTile_FocusesAtFocalLengthTimesAngle()
        {
            double theta = 2e-3; var (x, f) = Far(Tiles(1, "tileTiltXRad", Nums(theta)));
            Assert.Close(F * theta, x.Zip(f, (a, b) => a * b).Sum() / f.Sum(), 0.01, 0, "focus");
        }

        [Test(Reference = "a tophat tile on a line grid is a slit of width 2a: its far field is sinc^2 with the first zero at F lambda / (2 a) (Fraunhofer, Goodman sec. 4.4); a = 8.5 grid steps so that the slit holds exactly 17 samples", Tolerance = "1 grid step of the focal plane")]
        public void TophatTile_HasTheSlitFarField()
        {
            double a = 8.5 * Dx; var (x, f) = Far(Tiles(1, "tileShape", "tophat", "tileRadiusM", a));
            double x0 = F * Lam / (2 * a);
            int iz = Enumerable.Range(0, x.Length).Where(i => x[i] > 0.8 * x0 && x[i] < 1.2 * x0).OrderBy(i => f[i]).First();
            Assert.Close(x0, x[iz], 0, x[1] - x[0], "first zero");
        }

        [Test(Reference = "two in-phase tiles with amplitudes a_1, a_2 (unit total energy): the on-axis far-field peak scales as |a_1 + a_2|^2 / (a_1^2 + a_2^2); [1, 0.5] gives 0.9 of the equal-amplitude peak", Tolerance = "1 %")]
        public void UnequalAmplitudes_CostTheCombiningEfficiency()
        {
            double eq = OnAxis(Far(Tiles(2))), un = OnAxis(Far(Tiles(2, "amplitudes", Nums(1, 0.5))));
            Assert.Close(0.9, un / eq, 0.01, 0, "peak ratio");
        }

        [Test(Reference = "sign convention: delaysS = tau > 0 makes the tile's pulse arrive later: its field moves by +tau in the frame, as a delay element of delayS = tau moves the arrival time by +tau (E = Re{A e^{-i omega t}}: spectral factor e^{+i omega tau})", Tolerance = "3 % of tau")]
        public void TileDelay_HasTheSignOfADelayElement()
        {
            double tau = 60e-15, T = 30e-15;
            RunResult Run(JsonObject tiles, bool delayElement)
            {
                JsonObject Grid() => O("model", "cartesian", "nx", 256, "ny", 1, "halfWidthM", 1e-3, "spectralSamples", 96);
                var g0 = Grid(); g0.Set("initialProfile", tiles);
                var els = new System.Collections.Generic.List<JsonObject> { O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", g0) };
                if (delayElement) els.Add(O("id", "d", "type", "delay", "delayS", tau));
                els.Add(O("id", "z", "type", "gap", "lengthM", 1e-3, "material", "Vacuum", "transverse", Grid()));
                return Go(Sys(els.ToArray()), Spec(Gauss(Lam, T, 1e-3)));
            }
            // the tile delay is relative to the frame, so it moves the field; a delay element moves the frame (the arrival time) instead
            var r0 = Run(Tiles(1), false);
            double tTile = TemporalCentroid(Run(Tiles(1, "delaysS", Nums(tau)), false).Output) - TemporalCentroid(r0.Output);
            double tElem = Run(Tiles(1), true).FinalArrivalS - r0.FinalArrivalS;
            Assert.Close(tau, tElem, 1e-9, 0, "delay element: arrival");
            Assert.Close(tElem, tTile, 0, 0.03 * tau, "tile delay: same sign and size");
        }

        [Test(Reference = "validation: per-tile arrays of the wrong length and missing sizes are refused", Tolerance = "exact")]
        public void BadTileSpec_IsRefused()
        {
            Assert.Throws<Exception>(() => Far(Tiles(3, "pistonRad", Nums(0, 1))));
            Assert.Throws<Exception>(() => Far(O("kind", "tiles", "layout", "hex", "rings", 1)));
        }
    }
}
