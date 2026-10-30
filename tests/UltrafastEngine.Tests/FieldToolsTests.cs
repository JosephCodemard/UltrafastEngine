using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Results;
using UltrafastEngine.Retrieval;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Cw sources, the absorbing boundary of nonlinear elements, Gerchberg-Saxton, near-field cleaning and PNG input.</summary>
    [TestArea("Cw sources, nonlinear absorbing boundary, Gerchberg-Saxton, near-field maps")]
    public class FieldToolsTests
    {
        private const double Lam = 1030e-9;
        private static ElementRecord Rec(RunResult r, string id) => r.Records.First(x => x.Id == id);
        private static JsonObject Cw(double p) => O("shape", "cw", "centerWavelengthM", Lam, "powerW", p);

        [Test(Reference = "Fresnel integral on the axis, 4 I0 at N = 1 (Born & Wolf 8.8); power = I x area", Tolerance = "1.5 %")]
        public void CwSource_IsOneFrequency_AndThroughAnIrisGivesTheFresnelIntensity()
        {
            double a = 1e-3, r0 = 3e-3, p = 2.0, z = a * a / Lam, i0 = p / (Math.PI * r0 * r0);
            JsonObject Grid(JsonObject ini = null) { var t = O("model", "radial", "points", 1024, "radiusM", 12e-3, "absorbingBoundary", true); if (ini != null) t.Set("initialProfile", ini); return t; }
            var r = Go(Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", Grid(O("kind", "tophat", "radiusM", r0))),
                           O("id", "iris", "type", "aperture", "radiusM", a),
                           O("id", "z", "type", "gap", "lengthM", z, "material", "Vacuum", "transverse", Grid())),
                       Spec(Cw(p), O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "z")))));
            Assert.Equal(1, r.Output.Profile.Omega.Length, "one stored frequency");
            Assert.Close(p * 1e-9, r.Input.TotalEnergyJ(), 1e-9, 0, "energy = power x window");
            Assert.Close(4 * i0, r.Instruments.First(i => i.Name == "bp").Values.GetDouble("peakIntensityWPerM2", 0), 0.015, 0, "peak intensity");
        }

        [Test(Reference = "B = k n2 I L of a constant intensity; no spectral change", Tolerance = "1e-3")]
        public void CwSource_InAKerrPlate_PicksUpOnlyAPhase()
        {
            var o = O("format", "ultrafast-system", "version", 1, "name", "t", "materials", O("k", FlatMaterial(2.5e-20)));
            o.Set("elements", A(O("id", "p", "type", "bulk", "material", "k", "lengthM", 0.01, "beam", O("kind", "constant", "areaM2", 1e-10))));
            var r = Go(Systems.SystemDescription.FromJson(o), Spec(Cw(1e5), O("physics", O("raman", false, "selfSteepening", false))));
            Assert.Close(2 * Math.PI / Lam * 2.5e-20 * 1e5 / 1e-10 * 0.01, r.Records[0].BIntegralRad, 1e-3, 0, "B-integral");
            Assert.Close(r.Input.TotalEnergyJ(), r.Output.TotalEnergyJ(), 1e-6, 0, "energy");
        }

        [Test(Reference = "separable Fresnel integral on the axis: same pattern as the linear absorber", Tolerance = "3 %")]
        public void AbsorbingBoundary_InANonlinearElement_RemovesTheWrappedLight()
        {
            // a Kerr medium with a negligible n2 takes the stepper; the hard edge wraps around without the layer
            double Run(bool absorb, out RunResult res)
            {
                var t = O("model", "cartesian", "nx", 256, "ny", 1, "halfWidthM", 8e-3, "absorbingBoundary", absorb, "spectralSamples", 4);
                var src = O("model", "cartesian", "nx", 256, "ny", 1, "halfWidthM", 8e-3, "spectralSamples", 4, "initialProfile", O("kind", "tophat", "radiusM", 1e-3));
                var o = O("format", "ultrafast-system", "version", 1, "name", "t", "materials", O("k", O("index", O("kind", "constant", "value", 1.0), "n2", 1e-30)));
                o.Set("elements", A(O("id", "s", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", src), O("id", "a", "type", "aperture", "halfWidthXM", 0.3e-3, "halfWidthYM", 1.0),
                                    O("id", "z", "type", "bulk", "material", "k", "lengthM", 3.0, "nonlinear", true, "transverse", t)));
                res = Go(Systems.SystemDescription.FromJson(o), Spec(Gauss(Lam, 1e-12, 1e-12)));
                return Rec(res, "z").Values["onAxisFluenceOutJPerM2"];
            }
            // reference: the linear path with its multiplicative absorber on the same grid and a large window
            var lin = O("model", "cartesian", "nx", 2048, "ny", 1, "halfWidthM", 64e-3, "absorbingBoundary", true, "spectralSamples", 4);
            var ls = (JsonObject)JsonValue.Parse(lin.ToJson(false)); ls.Set("initialProfile", O("kind", "tophat", "radiusM", 1e-3));
            var rl = Go(Sys(O("id", "s", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", ls), O("id", "a", "type", "aperture", "halfWidthXM", 0.3e-3, "halfWidthYM", 1.0),
                            O("id", "z", "type", "gap", "lengthM", 3.0, "material", "Vacuum", "transverse", lin)), Spec(Gauss(Lam, 1e-12, 1e-12)));
            double reference = Rec(rl, "z").Values["onAxisFluenceOutJPerM2"];
            double with = Run(true, out var rw), without = Run(false, out _);
            Assert.Close(reference, with, 0.03, 0, "with the layer");
            Assert.Greater(Math.Abs(without / reference - 1), 2 * Math.Abs(with / reference - 1), "the layer improves the result");
            Assert.True(Rec(rw, "z").LossJ.ContainsKey("grid boundary"), "booked");
            Assert.Close(rw.Input.TotalEnergyJ() - rw.Output.TotalEnergyJ(), rw.Records.Sum(x => x.LossJ.Values.Sum()), 1e-6, 0, "budget closes");
        }

        private static (double[] Near, double[] Far, double[] Truth) Gs(int n)
        {
            // a decentred Gaussian (asymmetric, so the twin solution is not equivalent) with coma-like and defocus phase
            var nearC = new Complex[n * n]; var truth = new double[n * n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double x = (i - n / 2) / (n / 8.0), y = (j - n / 2) / (n / 8.0);
                    double a = Math.Exp(-(x - 0.2) * (x - 0.2) - (y + 0.1) * (y + 0.1)) * (1 + 0.5 * Math.Exp(-(x + 0.5) * (x + 0.5) * 4 - y * y * 4));
                    double ph = 0.8 * (x * x + y * y) + 0.6 * x * (x * x + y * y) - 0.4 * y;
                    truth[i * n + j] = ph; nearC[i * n + j] = Complex.FromPolarCoordinates(a, ph);
                }
            var b = new CartesianBasis(n, n, 1, 1);
            var k = (Complex[])nearC.Clone(); b.ToK(k, 1);
            var far = new double[n * n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) far[((i + n / 2) % n) * n + (j + n / 2) % n] = k[i * n + j].Magnitude;
            return (nearC.Select(z => z.Magnitude).ToArray(), far, truth);
        }

        [Test(Reference = "Gerchberg & Saxton, Optik 35, 237 (1972): a known phase recovered from two intensities", Tolerance = "far field 1 %, phase rms 0.1 rad over the beam")]
        public void GerchbergSaxton_RecoversAKnownWavefront()
        {
            int n = 128;
            var (near, far, truth) = Gs(n);
            var res = PhaseRetrieval.MultiStart(near, far, n, n, 2000);
            Assert.Less(res.FarError, 0.01, "far-field amplitude error");
            Assert.Less(res.ErrorHistory.Last(), res.ErrorHistory[0], "the error falls");
            double sw = 0, sd = 0, sd2 = 0;
            for (int m = 0; m < n * n; m++)
            {
                double w = near[m] * near[m]; var d = Complex.FromPolarCoordinates(1, res.Phase[m] - truth[m]);
                sw += w; sd += w * d.Phase; sd2 += w * d.Phase * d.Phase;
            }
            double rms = Math.Sqrt(Math.Max(0, sd2 / sw - Math.Pow(sd / sw, 2)));
            Assert.Less(rms, 0.1, "weighted rms phase error (piston removed)");
        }

        [Test]
        public void GerchbergSaxton_Command_WritesAPhaseMapTheEngineReads()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uf-gs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                int n = 64; var (near, far, _) = Gs(n);
                var ax = Enumerable.Range(0, n).Select(i => (i - n / 2) * 50e-6).ToArray();
                string f = Path.Combine(dir, "pair.npz");
                NpzWriter.Write(f, new[] { new NdArray("x_m", ax, n), new NdArray("y_m", ax, n), new NdArray("near", near.Select(v => v * v).ToArray(), n, n), new NdArray("far", far.Select(v => v * v).ToArray(), n, n) });
                var o = Commands.Retrieve(O("method", "gerchbergSaxton", "trace", f, "output", dir, "options", O("iterations", 50)), dir);
                Assert.Greater(o.GetInt("iterations", 0), 49, "iterations");
                var r = Go(Sys(O("id", "s", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", O("model", "cartesian", "nx", 64, "ny", 64, "halfWidthM", 1.6e-3, "spectralSamples", 2, "initialProfile", O("kind", "measured", "file", Path.Combine(dir, "phase.npz"))))), Spec(Gauss(Lam, 1e-12, 1e-6)));
                Assert.Close(r.Input.TotalEnergyJ(), r.Output.TotalEnergyJ(), 1e-9, 0, "the map runs");
            }
            finally { Directory.Delete(dir, true); }
        }

        private static byte[] Png(int w, int h, int depth, Func<int, int, int> pixel)
        {
            var raw = new MemoryStream();
            for (int y = 0; y < h; y++)
            {
                raw.WriteByte((byte)(y % 2 + 1));   // filters 1 (sub) and 2 (up), to exercise the decoder
                var row = new int[w * depth / 8];
                for (int x = 0; x < w; x++) { int v = pixel(x, y); if (depth == 8) row[x] = v; else { row[2 * x] = v >> 8; row[2 * x + 1] = v & 255; } }
                for (int i = 0; i < row.Length; i++)
                {
                    int bpp = depth / 8, pred = y % 2 == 0 ? (i >= bpp ? row[i - bpp] : 0) : Prev(pixel, i, y - 1, w, depth);
                    raw.WriteByte((byte)(row[i] - pred));
                }
            }
            var z = new MemoryStream(); z.WriteByte(0x78); z.WriteByte(1);
            using (var d = new DeflateStream(z, CompressionMode.Compress, true)) { var a = raw.ToArray(); d.Write(a, 0, a.Length); }
            var png = new MemoryStream(); png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            void Chunk(string t, byte[] data)
            {
                var len = BitConverter.GetBytes(data.Length).Reverse().ToArray(); png.Write(len, 0, 4);
                var tb = System.Text.Encoding.ASCII.GetBytes(t); png.Write(tb, 0, 4); png.Write(data, 0, data.Length); png.Write(new byte[4], 0, 4);
            }
            var hdr = BitConverter.GetBytes(w).Reverse().Concat(BitConverter.GetBytes(h).Reverse()).Concat(new byte[] { (byte)depth, 0, 0, 0, 0 }).ToArray();
            Chunk("IHDR", hdr); Chunk("IDAT", z.ToArray()); Chunk("IEND", new byte[0]);
            return png.ToArray();
        }
        private static int Prev(Func<int, int, int> pixel, int i, int y, int w, int depth)
        {
            if (y < 0) return 0;
            if (depth == 8) return pixel(i, y);
            int v = pixel(i / 2, y); return i % 2 == 0 ? v >> 8 : v & 255;
        }

        [Test(Reference = "Gaussian beam w = 2 sigma of a sampled image")]
        public void MeasuredPng_WithBackgroundCentreAndThreshold_GivesTheBeam()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uf-png-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                int n = 100; double px = 20e-6, w = 0.3e-3;
                // a decentred Gaussian on a background of 1000 counts in a 16-bit image
                Func<int, int, int> img = (x, y) => (int)(1000 + 50000 * Math.Exp(-2 * (Math.Pow((x - 60) * px, 2) + Math.Pow((y - 45) * px, 2)) / (w * w)));
                string f = Path.Combine(dir, "beam.png"); File.WriteAllBytes(f, Png(n, n, 16, img));
                var (pw, ph, data) = PngReader.ReadGray(f);
                Assert.Equal(n, pw, "width"); Assert.Equal(n, ph, "height");
                Assert.Close(img(60, 45), data[45 * n + 60], 0, 0.5, "pixel");
                var ini = O("kind", "measured", "file", f, "pixelSizeM", px, "background", "auto", "center", true, "threshold", 1e-3);
                var r = Go(Sys(O("id", "s", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", O("model", "cartesian", "nx", 128, "ny", 128, "halfWidthM", 1.2e-3, "spectralSamples", 2, "initialProfile", ini))), Spec(Gauss(Lam, 1e-12, 1e-6)));
                Assert.Close(w, Rec(r, "s").BeamRadiusXM, 0.02, 0, "radius");
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
