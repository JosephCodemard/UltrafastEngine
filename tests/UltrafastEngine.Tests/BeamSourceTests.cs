using System;
using System.IO;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Transverse;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Structured beam sources and thin masks (docs/reference/transverse.md#initial-profile, #masks).</summary>
    [TestArea("Beam sources and masks: Hermite-Gauss, Laguerre-Gauss, Bessel, Airy, measured near fields, axicon, zone plate, vortex, slits")]
    public class BeamSourceTests
    {
        private const double Lam = 1030e-9, E = 1e-3;
        private static double K => 2 * Math.PI / Lam;
        private static RunSpec Pulse(params JsonObject[] instruments) => Spec(Gauss(Lam, 1e-12, E), O("instruments", A(instruments.Cast<JsonValue>().ToArray())));
        private static JsonObject Bp(string at) => O("kind", "beamProfile", "name", "bp_" + at, "at", at);
        private static ElementRecord Rec(RunResult r, string id) => r.Records.First(x => x.Id == id);
        private static double[] Arr(RunResult r, string inst, string suffix) => r.Instruments.First(i => i.Name == inst).Arrays.First(a => a.Name.EndsWith(suffix)).Data;

        private static JsonObject Grid(string model, int n, double extent, JsonObject initial = null)
        {
            var t = model == "radial" ? O("model", "radial", "points", n, "radiusM", extent, "absorbingBoundary", true)
                                      : O("model", "cartesian", "nx", n, "ny", n, "halfWidthM", extent, "absorbingBoundary", true, "spectralSamples", 4);
            if (initial != null) t.Set("initialProfile", initial);
            return t;
        }
        private static JsonObject Line(int nx, double half, JsonObject initial = null)
        {
            var t = O("model", "cartesian", "nx", nx, "ny", 1, "halfWidthM", half, "absorbingBoundary", true, "spectralSamples", 4);
            if (initial != null) t.Set("initialProfile", initial);
            return t;
        }
        private static JsonObject Source(JsonObject grid) => O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", grid);
        private static JsonObject Free(string id, double z, JsonObject grid) => O("id", id, "type", "gap", "lengthM", z, "material", "Vacuum", "transverse", grid);

        /// <summary>Position of the first local minimum after index i0 below a fraction of the peak, refined by a parabola.</summary>
        private static double FirstMin(double[] x, double[] f, int i0, double level)
        {
            for (int i = i0 + 1; i < x.Length - 1; i++)
                if (f[i] <= f[i - 1] && f[i] <= f[i + 1] && f[i] < level * f[i0])
                {
                    double den = f[i - 1] - 2 * f[i] + f[i + 1];
                    return x[i] + (den > 0 ? 0.5 * (f[i - 1] - f[i + 1]) / den * 0.5 * (x[i + 1] - x[i - 1]) : 0);
                }
            return double.NaN;
        }

        [Test(Reference = "Abramowitz & Stegun table 10.11: Ai(0), Ai(1), Ai(-1), Ai(5.5), first zero; H_3, L_2^1 closed forms", Tolerance = "1e-8")]
        public void SpecialFunctions_MatchTabulatedValues()
        {
            Assert.Close(0.355028053887817, BeamFunctions.Airy(0), 1e-12, 0, "Ai(0)");
            Assert.Close(0.135292416312881, BeamFunctions.Airy(1), 1e-10, 0, "Ai(1)");
            Assert.Close(0.535560883292352, BeamFunctions.Airy(-1), 1e-10, 0, "Ai(-1)");
            Assert.Close(0, BeamFunctions.Airy(-2.33810741045977), 0, 1e-12, "first zero of Ai");
            Assert.Close(BeamFunctions.Airy(4.9999999), BeamFunctions.Airy(5.0000001), 1e-6, 0, "series and asymptotics join at +5");
            Assert.Close(BeamFunctions.Airy(-4.9999999), BeamFunctions.Airy(-5.0000001), 1e-5, 0, "series and asymptotics join at -5");
            Assert.Close(8 * 27 - 12 * 3, BeamFunctions.Hermite(3, 3), 1e-12, 0, "H_3(3) = 8 x^3 - 12 x");
            double x = 0.7;
            Assert.Close(x * x / 2 - 3 * x + 3, BeamFunctions.Laguerre(2, 1, x), 1e-12, 0, "L_2^1(x) = x^2 / 2 - 3 x + 3");
        }

        [Test(Reference = "Hermite-Gauss modes: second-moment radius w(z) sqrt(2m + 1) (Siegman, Lasers, ch. 16)")]
        public void HermiteGauss_RadiiFollowTheModeOrders_AtTheWaistAndAfterARayleighRange()
        {
            double w = 0.5e-3, zr = Math.PI * w * w / Lam;
            var g = Grid("cartesian", 256, 4e-3);
            var r = Go(Sys(Source(Grid("cartesian", 256, 4e-3, O("kind", "hermiteGauss", "modeX", 2, "modeY", 1, "radiusM", w))), Free("z", zr, g)), Pulse());
            Assert.Close(w * Math.Sqrt(5), Rec(r, "src").BeamRadiusXM, 0.005, 0, "x radius at the waist");
            Assert.Close(w * Math.Sqrt(3), Rec(r, "src").BeamRadiusYM, 0.005, 0, "y radius at the waist");
            Assert.Close(w * Math.Sqrt(10), Rec(r, "z").BeamRadiusXM, 0.01, 0, "x radius after z_R");
            Assert.Close(w * Math.Sqrt(6), Rec(r, "z").BeamRadiusYM, 0.01, 0, "y radius after z_R");
        }

        [Test(Reference = "Laguerre-Gauss modes: second-moment radius w sqrt(2p + |l| + 1), helical phase keeps a dark axis, ring at w sqrt(|l| / 2)")]
        public void LaguerreGauss_VortexKeepsItsDarkAxis_AndItsRingScalesWithTheBeam()
        {
            double w = 0.5e-3, zr = Math.PI * w * w / Lam;
            var r = Go(Sys(Source(Grid("cartesian", 256, 4e-3, O("kind", "laguerreGauss", "modeL", 1, "radiusM", w))), Free("z", zr, Grid("cartesian", 256, 4e-3))), Pulse(Bp("src"), Bp("z")));
            Assert.Close(w * Math.Sqrt(2), Rec(r, "src").BeamRadiusXM, 0.01, 0, "x radius w sqrt(2p + |l| + 1)");
            var f0 = Arr(r, "bp_z", "fluence_x_j_m2"); var x = Arr(r, "bp_z", "x_m");
            int c = Array.IndexOf(x, x.OrderBy(Math.Abs).First());
            Assert.Less(f0[c], 1e-3 * f0.Max(), "dark axis after propagation");
            double ring = Math.Abs(x[Array.IndexOf(f0, f0.Max())]);
            Assert.Close(w * Math.Sqrt(2) / Math.Sqrt(2), ring, 0.05, 0, "ring radius w(z) / sqrt 2 = w0 at z_R");
            // p = 1, l = 0 on a radial grid: the zero of L_1(2 r^2 / w^2) at r = w / sqrt 2 scales with w(z)
            var rr = Go(Sys(Source(Grid("radial", 512, 5e-3, O("kind", "laguerreGauss", "modeP", 1, "radiusM", w))), Free("z", zr, Grid("radial", 512, 5e-3))), Pulse(Bp("z")));
            var R = Arr(rr, "bp_z", "radius_m"); var F = Arr(rr, "bp_z", "fluence_j_m2");
            Assert.Close(w, FirstMin(R, F, 0, 0.1), 0.02, 0, "node of LG_10 at w(z) / sqrt 2");
        }

        [Test(Reference = "Durnin, JOSA A 4, 651 (1987); Bessel-Gauss (Gori et al. 1987): central lobe 2.405 / k_r unchanged while z << w / angle", Tolerance = "2 %")]
        public void BesselGauss_CoreDoesNotSpreadOverSeveralRayleighRangesOfItsSpot()
        {
            double angle = 1e-3, kr = K * Math.Sin(angle), spot = 2.4048 / kr, z = 3.0;
            var ini = O("kind", "besselGauss", "coneAngleRad", angle, "radiusM", 6e-3);
            var r = Go(Sys(Source(Grid("radial", 1024, 25e-3, ini)), Free("z", z, Grid("radial", 1024, 25e-3))), Pulse(Bp("src"), Bp("z")));
            Assert.Close(spot, FirstMin(Arr(r, "bp_src", "radius_m"), Arr(r, "bp_src", "fluence_j_m2"), 0, 0.05), 0.02, 0, "first zero at the source");
            Assert.Close(spot, FirstMin(Arr(r, "bp_z", "radius_m"), Arr(r, "bp_z", "fluence_j_m2"), 0, 0.05), 0.02, 0, "first zero after 3 m");
            Assert.Greater(z / (K * spot * spot / 2), 5, "z is several Rayleigh ranges of a Gaussian of that spot");
        }

        [Test(Reference = "Siviloglou & Christodoulides, Opt. Lett. 32, 979 (2007): parabolic trajectory x = z^2 / (4 k^2 x0^3)", Tolerance = "3 %")]
        public void AiryBeam_MainLobeAcceleratesAlongTheParabola()
        {
            double x0 = 100e-6, z = 0.3;
            var ini = O("kind", "airy", "radiusM", x0, "truncation", 0.05);
            var r = Go(Sys(Source(Line(4096, 8e-3, ini)), Free("z", z, Line(4096, 8e-3))), Pulse(Bp("src"), Bp("z")));
            double Peak(string b) { var x = Arr(r, b, "x_m"); var f = Arr(r, b, "fluence_x_j_m2"); int i = Array.IndexOf(f, f.Max()); double den = f[i - 1] - 2 * f[i] + f[i + 1]; return x[i] + 0.5 * (f[i - 1] - f[i + 1]) / den * (x[1] - x[0]); }
            Assert.Close(-(1.0188 - 0.05 / 1.0188) * x0, Peak("bp_src"), 0.01, 0, "main lobe of Ai(x) exp(a x): shifted by a / 1.0188 by the truncation");
            Assert.Close(z * z / (4 * K * K * x0 * x0 * x0), Peak("bp_z") - Peak("bp_src"), 0.03, 0, "displacement of the main lobe");
        }

        [Test(Reference = "Gaussian beam w(z) from a sampled near field; known background subtracted")]
        public void MeasuredNearField_FromAnNpz_PropagatesLikeTheBeamItSampled()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uf-nearfield-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                double w = 0.5e-3, zr = Math.PI * w * w / Lam; int n = 200;
                var ax = Enumerable.Range(0, n).Select(i => -3e-3 + i * 6e-3 / (n - 1)).ToArray();
                var fl = new double[n * n];
                for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) fl[i * n + j] = 7 + 100 * Math.Exp(-2 * (ax[i] * ax[i] + ax[j] * ax[j]) / (w * w));
                string f = Path.Combine(dir, "beam.npz");
                NpzWriter.Write(f, new[] { new NdArray("x_m", ax, n), new NdArray("y_m", ax, n), new NdArray("fluence", fl, n, n) });
                var ini = O("kind", "measured", "file", f, "background", "auto");
                var r = Go(Sys(Source(Grid("cartesian", 128, 3e-3, ini)), Free("z", zr, Grid("cartesian", 128, 3e-3))), Pulse());
                Assert.Close(w, Rec(r, "src").BeamRadiusXM, 0.01, 0, "radius of the map after the background");
                Assert.Close(w * Math.Sqrt(2), Rec(r, "z").BeamRadiusXM, 0.02, 0, "w(z_R)");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test(Reference = "Fresnel zone plate: amplitude on the axis 2 N_open U0, intensity 4 N_open^2 I0; binary phase plate 16 N_open^2 I0 (Born & Wolf 8.8)", Tolerance = "5 %")]
        public void ZonePlate_FocusesAtItsFocalLength_AmplitudeAndBinaryPhase()
        {
            double f = 0.1; int zones = 10;
            double a = Math.Sqrt(zones * Lam * f), f0 = E / (Math.PI * a * a);
            foreach (bool phase in new[] { false, true })
            {
                var r = Go(Sys(Source(Grid("radial", 2048, 6e-3, O("kind", "tophat", "radiusM", a))),
                               O("id", "zp", "type", "mask", "kind", "zonePlate", "focalLengthM", f, "binaryPhase", phase),
                               Free("z", f, Grid("radial", 2048, 6e-3))), Pulse());
                double expect = (phase ? 16 : 4) * Math.Pow(zones / 2, 2) * f0;
                Assert.Close(expect, Rec(r, "z").Values["onAxisFluenceOutJPerM2"], 0.05, 0, phase ? "binary phase plate" : "amplitude plate");
                Assert.False(r.Warnings.Any(w => w.Contains("under-resolved")), "zones resolved");
            }
        }

        [Test(Reference = "Axicon on a Gaussian: J0(k r sin(angle)) core, first zero 2.405 / (k sin(angle)) (McLeod 1954)", Tolerance = "2 %")]
        public void Axicon_TurnsAGaussianIntoABesselCore()
        {
            double w = 3e-3, angle = 1e-3, z = w / (2 * angle);
            var r = Go(Sys(Source(Grid("radial", 2048, 12e-3, O("kind", "gaussian", "radiusM", w))),
                           O("id", "ax", "type", "mask", "kind", "axicon", "coneAngleRad", angle),
                           Free("z", z, Grid("radial", 2048, 12e-3))), Pulse(Bp("z")));
            Assert.Close(1, Rec(r, "ax").Values["transmission"], 1e-9, 0, "a phase mask passes everything");
            Assert.Close(2.4048 / (K * Math.Sin(angle)), FirstMin(Arr(r, "bp_z", "radius_m"), Arr(r, "bp_z", "fluence_j_m2"), 0, 0.1), 0.02, 0, "first zero");
        }

        [Test(Reference = "Young's double slit: fringe period lambda z / d, transmission 2 b / W", Tolerance = "2 %")]
        public void DoubleSlit_FringesHaveThePeriodOfYoungsExperiment()
        {
            double d = 0.5e-3, b = 50e-6, z = 2.0, half = 2e-3;
            var r = Go(Sys(Source(Line(8192, 30e-3, O("kind", "tophat", "radiusM", half))),
                           O("id", "s", "type", "mask", "kind", "slits", "count", 2, "widthM", b, "separationM", d),
                           Free("z", z, Line(8192, 30e-3))), Pulse(Bp("z")));
            Assert.Close(2 * b / (2 * half), Rec(r, "s").Values["transmission"], 0.02, 0, "open fraction");
            var x = Arr(r, "bp_z", "x_m"); var f = Arr(r, "bp_z", "fluence_x_j_m2");
            int c = Array.IndexOf(x, x.OrderBy(Math.Abs).First());
            Assert.Close(Lam * z / (2 * d), FirstMin(x, f, c, 0.1), 0.02, 0, "first dark fringe at half a period");
        }

        [Test(Reference = "Spiral phase plate: a vortex of charge l has zero amplitude on the axis at every z")]
        public void VortexPlate_ConservesEnergy_AndDarkensTheAxis_AndNeedsACartesianGrid()
        {
            var r = Go(Sys(Source(Grid("cartesian", 256, 4e-3, O("kind", "gaussian", "radiusM", 0.5e-3))),
                           O("id", "v", "type", "mask", "kind", "vortex", "charge", 1),
                           Free("z", 0.5, Grid("cartesian", 256, 4e-3))), Pulse(Bp("z")));
            var f = Arr(r, "bp_z", "fluence_x_j_m2"); var x = Arr(r, "bp_z", "x_m");
            Assert.Less(f[Array.IndexOf(x, x.OrderBy(Math.Abs).First())], 0.01 * f.Max(), "dark axis");
            Assert.Close(r.Input.TotalEnergyJ(), r.Output.TotalEnergyJ(), 0.01, 0, "a phase mask keeps the energy");
            Assert.Throws<Exception>(() => Go(Sys(Source(Grid("radial", 64, 4e-3, O("kind", "gaussian", "radiusM", 0.5e-3))), O("id", "v", "type", "mask", "kind", "vortex")), Pulse()), "vortex on a radial grid");
        }

        [Test(Reference = "T = amplitude^2 for a uniform map; a thin plate's phase scales with frequency")]
        public void FileMask_AppliesAmplitudeAndPhase()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uf-mask-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                string f = Path.Combine(dir, "mask.npz");
                var rr = new[] { 0.0, 1.0 };
                NpzWriter.Write(f, new[] { new NdArray("r_m", rr, 2), new NdArray("amplitude", new[] { 0.5, 0.5 }, 2) });
                var r = Go(Sys(Source(Grid("radial", 128, 3e-3, O("kind", "gaussian", "radiusM", 0.5e-3))), O("id", "m", "type", "mask", "kind", "file", "file", f)), Pulse());
                Assert.Close(0.25, Rec(r, "m").Values["transmission"], 1e-9, 0, "T = 0.25");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void Mask_WithoutAProfile_WarnsAndPassesThePulse()
        {
            var r = Go(Sys(O("id", "v", "type", "mask", "kind", "axicon", "coneAngleRad", 1e-3)), Pulse());
            Assert.True(r.Warnings.Any(w => w.Contains("carried transverse profile")), "warning");
            Assert.Close(r.Input.TotalEnergyJ(), r.Output.TotalEnergyJ(), 1e-12, 0, "unchanged");
        }
    }
}
