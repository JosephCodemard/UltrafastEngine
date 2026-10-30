using System;
using System.Linq;
using UltrafastEngine.Instruments;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Cameras, wavefront sensor and virtual instruments against Gaussian-beam and pulse analytics.</summary>
    [TestArea("Cameras, wavefront sensor, Wigner and space-time probes")]
    public class DiagnosticsTests
    {
        private const double Lam = 1030e-9;

        private static RunResult FreeSpace(string model, JsonArray instruments, double w0 = 300e-6, double L = 0.1)
        {
            var tv = model == "radial" ? O("model", "radial", "points", 64, "radiusM", 1.5e-3) : O("model", "cartesian", "nx", 64, "ny", 64, "halfWidthM", 1.5e-3);
            var sys = Sys(O("id", "g", "type", "gap", "material", "Vacuum", "lengthM", L, "beam", O("kind", "gaussian", "w0M", w0, "wavelengthM", Lam), "transverse", tv));
            var r = Go(sys, Spec(Gauss(Lam, 300e-15, 1e-6), O("instruments", instruments)));
            Assert.Equal(0, r.Warnings.Count(x => x.StartsWith("Instrument")), "instrument warnings: " + string.Join("; ", r.Warnings));
            return r;
        }

        private static JsonObject V(RunResult r, string name) => r.Instruments.First(i => i.Name == name).Values;

        [Test(Category = "instruments", Reference = "Gaussian optics: w(z), far field λf/(πw0), R(z)", Tolerance = "0.5 %, 1 %")]
        public void Cameras_GaussianBeam_NearFarFieldAndWavefrontMatchGaussianOptics()
        {
            double w0 = 300e-6, L = 0.1, zr = Math.PI * w0 * w0 / Lam;
            foreach (var model in new[] { "radial", "cartesian" })
            {
                var r = FreeSpace(model, A(
                    O("kind", "nearFieldCamera", "name", "nf", "at", "g"),
                    O("kind", "nearFieldCamera", "name", "nfWaist", "at", "g", "propagateM", -L),
                    O("kind", "farFieldCamera", "name", "ff", "at", "g", "focalLengthM", 1.0),
                    O("kind", "wavefrontSensor", "name", "wfs", "at", "g")), w0, L);
                Assert.Close(w0 * Math.Sqrt(1 + Math.Pow(L / zr, 2)), V(r, "nf").GetDouble("radiusXM", 0), 0.01, 0, model + " near-field radius");
                Assert.Close(w0, V(r, "nfWaist").GetDouble("radiusXM", 0), 0.01, 0, model + " refocused to the waist");
                Assert.Close(Lam * 1.0 / (Math.PI * w0), V(r, "ff").GetDouble("radiusXM", 0), 0.005, 0, model + " far-field radius lambda f / (pi w0)");
                Assert.Close(1e-6, V(r, "ff").GetDouble("energyJ", 0), 0.005, 0, model + " far-field energy");
                Assert.Close(L * (1 + zr * zr / (L * L)), V(r, "wfs").GetDouble("radiusOfCurvatureM", 0), model == "radial" ? 0.01 : 0.02, 0, model + " wavefront radius of curvature");
                Assert.Less(V(r, "wfs").GetDouble("rmsWithoutDefocusWaves", 1), 1e-3, model + " no aberration beyond defocus");
            }
        }

        [Test(Category = "instruments")]
        public void Camera_On1DElement_ImagesTheBeamModelAndSaysSo()
        {
            var sys = Sys(O("id", "plate", "type", "bulk", "material", "FusedSilica", "lengthM", 0.01, "nonlinear", false, "beam", O("kind", "gaussian", "w0M", 200e-6, "zWaistM", 0.0)));
            var r = Go(sys, Spec(Gauss(Lam, 300e-15, 1e-6), O("instruments", A(O("kind", "nearFieldCamera", "at", "plate", "bitDepth", 10)))));
            var ins = r.Instruments.First();
            Assert.True(ins.Notes.Any(n => n.Contains("ideal Gaussian")), "synthetic-beam note");
            double zr = Math.PI * 200e-6 * 200e-6 * 1.45 / Lam;
            Assert.Close(200e-6 * Math.Sqrt(1 + Math.Pow(0.01 / zr, 2)), ins.Values.GetDouble("radiusXM", 0), 0.02, 0, "radius from the beam model");
            Assert.Close(10, ins.Values.GetDouble("bitDepth", 0), 0, 0, "sensor bit depth recorded");
        }

        [Test(Reference = "known Zernike coefficients")]
        public void Zernike_FitRecoversKnownCoefficients()
        {
            int n = 64; double a = 1e-3;
            var xs = Enumerable.Range(0, n).Select(i => (i - 31.5) * 2.2 * a / n).ToArray();
            var map = new double[n * n];
            var truth = new double[15]; truth[3] = 0.25; truth[4] = -0.1; truth[7] = 0.05; truth[10] = 0.02;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double rho = Math.Sqrt(xs[i] * xs[i] + xs[j] * xs[j]) / a, th = Math.Atan2(xs[j], xs[i]);
                    map[i * n + j] = rho > 1 ? double.NaN : Enumerable.Range(1, 15).Sum(k => truth[k - 1] * Zernike.Z(k, rho, th));
                }
            var c = Zernike.Fit(map, xs, xs, a, 15);
            for (int k = 0; k < 15; k++) Assert.Close(truth[k], c[k], 0, 1e-9, "Z" + (k + 1));
        }

        [Test(Category = "instruments", Reference = "Wigner time marginal = |A(t)|²; known linear chirp", Tolerance = "1 %")]
        public void Wigner_MarginalIsThePowerAndFieldProbeRecoversTheChirp()
        {
            var pulse = Gauss(800e-9, 20e-15, 1e-9); pulse.Set("chirp", O("gddFs2", 500));
            var sys = Sys(O("id", "air", "type", "gap", "material", "Vacuum", "lengthM", 1e-3));
            var r = Go(sys, Spec(pulse, O("instruments", A(O("kind", "wigner", "at", "input"), O("kind", "fieldProbe", "at", "input")))));
            var w = r.Instruments.First(i => i.Kind == "wigner");
            var W = w.Arrays.First(x => x.Name.EndsWith("distribution")); var t = w.Arrays.First(x => x.Name.EndsWith("time_s")).Data;
            int nt = t.Length, nw = W.Data.Length / nt;
            var marg = Enumerable.Range(0, nt).Select(i => Enumerable.Range(0, nw).Sum(l => W.Data[i * nw + l])).ToArray();
            var p = PulseMetrics.TemporalPower(r.Input); var tg = r.Input.Grid.Time;
            var pi = t.Select(x => Interp(tg, p, x)).ToArray();
            Assert.Greater(Correlation(marg, pi), 0.999, "time marginal of W = |A(t)|^2");
            Assert.True(w.Values.GetBool("virtual", false), "flagged virtual");
            var fp = r.Instruments.First(i => i.Kind == "fieldProbe");
            var gdd = fp.Arrays.First(x => x.Name.EndsWith("gdd_s2")).Data; var S = fp.Arrays.First(x => x.Name.EndsWith("spectrum")).Data;
            int k = Array.IndexOf(S, S.Max());
            Assert.Close(500e-30, gdd[k], 0.01, 0, "GDD at the spectral peak");
        }

        [Test(Category = "instruments")]
        public void SpaceTime_LinearGaussian_HasNoSpatioTemporalCoupling()
        {
            var r = FreeSpace("radial", A(O("kind", "spaceTime", "at", "g")));
            var v = r.Instruments.First().Values;
            Assert.Close(300e-15, v.GetDouble("onAxisFwhmS", 0), 0.01, 0, "on-axis duration");
            Assert.Close(v.GetDouble("onAxisFwhmS", 0), v.GetDouble("fwhmAtRadiusS", 0), 1e-3, 0, "same duration at r = w");
            Assert.Less(Math.Abs(v.GetDouble("pulseFrontDelayS", 1)), 1e-15, "flat pulse front (free space, no chromatic optics)");
        }

        private static double Interp(double[] x, double[] y, double v)
        {
            if (v <= x[0]) return y[0]; if (v >= x[x.Length - 1]) return y[y.Length - 1];
            int i = Array.BinarySearch(x, v); if (i >= 0) return y[i]; i = ~i;
            double t = (v - x[i - 1]) / (x[i] - x[i - 1]); return y[i - 1] * (1 - t) + y[i] * t;
        }

        private static double Correlation(double[] a, double[] b)
        {
            double ma = a.Average(), mb = b.Average(), sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < a.Length; i++) { sab += (a[i] - ma) * (b[i] - mb); saa += (a[i] - ma) * (a[i] - ma); sbb += (b[i] - mb) * (b[i] - mb); }
            return sab / Math.Sqrt(saa * sbb);
        }
    }
}
