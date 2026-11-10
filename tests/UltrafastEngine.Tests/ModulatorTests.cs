using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>The <c>modulator</c> element (docs/reference/modulator.md): a pixelated phase / amplitude device on the carried profile.</summary>
    [TestArea("Modulator: pixelated phase and amplitude devices (SLM, DM surface), quantisation, fill factor, chromatic rules")]
    public class ModulatorTests
    {
        private const double Lam = 1030e-9, F = 0.05, Dx = 2e-6, Pitch = 8e-6;
        private const int Nx = 2048, Cells = 512;
        private static RunSpec Pulse(double lam, params JsonObject[] instruments) => Spec(Gauss(lam, 1e-12, 1e-3), O("instruments", A(instruments.Cast<JsonValue>().ToArray())));
        private static JsonObject Line(JsonObject initial = null)
        {
            var t = O("model", "cartesian", "nx", Nx, "ny", 1, "halfWidthM", Nx * Dx / 2, "absorbingBoundary", true, "spectralSamples", 4);
            if (initial != null) t.Set("initialProfile", initial);
            return t;
        }
        private static JsonObject Source(double w) => O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", Line(O("kind", "gaussian", "radiusM", w)));
        private static JsonObject Free(string id, double z) => O("id", id, "type", "gap", "lengthM", z, "material", "Vacuum", "transverse", Line());
        private static JsonObject Lens(double f) => O("id", "lens", "type", "lens", "focalLengthM", f);
        private static JsonObject Bp(string at) => O("kind", "beamProfile", "name", "bp", "at", at);
        private static double[] Arr(RunResult r, string suffix) => r.Instruments.First(i => i.Name == "bp").Arrays.First(a => a.Name.EndsWith(suffix)).Data;

        /// <summary>A [x][1] map from a function of the x cell.</summary>
        private static JsonArray Map(Func<int, double> f) { var a = new JsonArray(); for (int i = 0; i < Cells; i++) a.Add(new JsonArray().Add(f(i))); return a; }

        private static JsonObject Mod(JsonArray phase, params object[] extra)
        {
            var o = O("id", "mod", "type", "modulator", "pitchXM", Pitch, "pitchYM", 1.0, "phaseRad", phase, "designWavelengthM", Lam, "offsetXM", 0.5 * Dx);   // device edges on grid-cell edges: every grid cell lies in one device cell
            foreach (var kv in O(extra)) o.Set(kv.Key, kv.Value);
            return o;
        }
        private static double X(int i) => (i - (Cells - 1) / 2.0) * Pitch;
        private static RunResult Focal(JsonObject mod, double lam = Lam, double w = 0.3e-3, double f = F, UltrafastEngine.Models.ModelRegistry reg = null) =>
            Go(Sys(Source(w), mod, Lens(f), Free("z", f)), Pulse(lam, Bp("z"), O("kind", "beamProfile", "name", "bp0", "at", "src")), reg);
        /// <summary>Sum of the x cut of the input beam: the same grid, so a window sum over this is an efficiency of the incident energy.</summary>
        private static double Incident(RunResult r) => r.Instruments.First(i => i.Name == "bp0").Arrays.First(a => a.Name.EndsWith("fluence_x_j_m2")).Data.Sum();

        private static double Centroid(double[] x, double[] f) => x.Zip(f, (a, b) => a * b).Sum() / f.Sum();
        private static double Window(double[] x, double[] f, double x0, double half, double norm) => x.Zip(f, (a, b) => Math.Abs(a - x0) <= half ? b : 0).Sum() / norm;

        [Test(Reference = "a pure phase tilt k theta x moves the focus of a lens by f theta (geometric optics, Goodman, Fourier optics ch. 5)", Tolerance = "1 %")]
        public void PhaseRamp_MovesTheFocus_ByFocalLengthTimesAngle()
        {
            double theta = 3e-3, k = 2 * Math.PI / Lam;
            var r = Focal(Mod(Map(i => k * theta * X(i))));
            Assert.Close(F * theta, Centroid(Arr(r, "x_m"), Arr(r, "fluence_x_j_m2")), 0.01, 0, "focus shift");
            Assert.Close(1, r.Records.First(x => x.Id == "mod").Values["transmission"], 1e-9, 0, "a phase device is lossless");
        }

        [Test(Reference = "N-level blazed grating, first-order efficiency [sin(pi/N) / (pi/N)]^2 (Goodman, Fourier optics, sec. 5.2; N = 8: 94.96 %); the sampled field (4 grid cells per device cell) gives sin(pi/4)... exact value of the sampled staircase, within 1.5 % of the continuum for N = 4", Tolerance = "1.5 % of the efficiency")]
        public void QuantisedBlaze_FirstOrderEfficiency_FollowsSincOfOneOverN()
        {
            foreach (int n in new[] { 4, 8, 16 })
            {
                // one period = n cells, the ramp 0 .. 2 pi in n steps, written with n levels
                var r = Focal(Mod(Map(i => 2 * Math.PI * (i % n) / n), "quantisationLevels", n, "phaseWrapRad", 2 * Math.PI));
                double x1 = Lam * F / (n * Pitch), eff = Window(Arr(r, "x_m"), Arr(r, "fluence_x_j_m2"), x1, 0.4 * x1, Incident(r));
                // the grid holds 4 samples per device cell: the staircase is sampled, c_1 = (1/n) sin(pi/4) / sin(pi/(4n)) / 4 * 4 ... evaluated directly
                double c1 = 0; int ns = 4 * n;
                for (int k = 0; k < ns; k++) c1 += Math.Cos(2 * Math.PI * (k / 4) / n - 2 * Math.PI * k / ns) / ns;
                double s1 = 0; for (int k = 0; k < ns; k++) s1 += Math.Sin(2 * Math.PI * (k / 4) / n - 2 * Math.PI * k / ns) / ns;
                Assert.Close(c1 * c1 + s1 * s1, eff, 0.006, 0, "N = " + n + " (sampled staircase)");
                double continuum = Math.Pow(Math.Sin(Math.PI / n) / (Math.PI / n), 2);
                Assert.Close(continuum, eff, 0.02, 0, "N = " + n + " (continuous staircase)");
            }
        }

        [Test(Reference = "comb of active cells (fill 3/4 of a 4-sample pitch, edges on grid-cell edges): transmission 3/4; discrete Fourier series c_0 = 3/4, |c_1| = 1/4, so the first replica / zero order = 1/9; replicas at lambda f / pitch", Tolerance = "1e-6 transmission, 1 % order ratio")]
        public void FillFactor_SetsTheTransmission_AndTheReplicaOrders()
        {
            double f = 0.01;                       // short focus: the replicas (lambda f / pitch = 1.3 mm) stay on the grid
            var r = Focal(Mod(Map(i => 0.0), "fillFactor", 0.75, "offsetXM", 0.0), Lam, 0.3e-3, f);
            Assert.Close(0.75, r.Records.First(x => x.Id == "mod").Values["transmission"], 1e-6, 0, "open fraction");
            double x1 = Lam * f / Pitch;
            var xs = Arr(r, "x_m"); var fl = Arr(r, "fluence_x_j_m2");
            double p0 = Window(xs, fl, 0, 0.4 * x1, 1), p1 = Window(xs, fl, x1, 0.4 * x1, 1);
            Assert.Close(1.0 / 9, p1 / p0, 0.01, 0, "first replica / zero order = |c1/c0|^2");
        }

        [Test(Reference = "a wrapped optical path is a grating: the first order leaves at lambda / period, so its position scales with lambda; an unwrapped ramp is a prism that deflects every colour by the same angle", Tolerance = "1.5 %")]
        public void WrappedRamp_DispersesLikeAGrating_UnwrappedRampDoesNot()
        {
            double lam2 = 1.2e-6, period = 8 * Pitch;            // 8 cells per period
            double Pos(double lam, bool wrapped)
            {
                var map = wrapped ? Map(i => 2 * Math.PI * (i % 8) / 8) : Map(i => 2 * Math.PI * X(i) / period);
                var r = Focal(Mod(map, wrapped ? new object[] { "phaseWrapRad", 2 * Math.PI } : new object[0]), lam);
                var xs = Arr(r, "x_m"); var f = Arr(r, "fluence_x_j_m2");
                // the diffracted / deflected light: weight beyond 0.5 of the nominal 1030 nm position
                double cut = 0.5 * Lam * F / period;
                return xs.Zip(f, (a, b) => a > cut ? a * b : 0).Sum() / xs.Zip(f, (a, b) => a > cut ? b : 0).Sum();
            }
            Assert.Close(lam2 / Lam, Pos(lam2, true) / Pos(Lam, true), 0.015, 0, "wrapped: x1 ~ lambda");
            Assert.Close(1, Pos(lam2, false) / Pos(Lam, false), 0.015, 0, "unwrapped ramp: the deflection angle does not depend on lambda");
        }

        [Test(Reference = "fixedPhase: the written phase does not scale with frequency, so a ramp k0 theta x deflects by theta lambda / lambda0 (diffractive / geometric-phase device)", Tolerance = "1 %")]
        public void FixedPhase_RampDeflectionScalesWithWavelength()
        {
            double theta = 3e-3, k = 2 * Math.PI / Lam, lam2 = 1.2e-6;
            var r = Focal(Mod(Map(i => k * theta * X(i)), "chromatic", "fixedPhase"), lam2);
            Assert.Close(F * theta * lam2 / Lam, Centroid(Arr(r, "x_m"), Arr(r, "fluence_x_j_m2")), 0.01, 0, "shift");
        }

        [Test(Reference = "arrays outside the device block the light: outsideTransmission = 0 and an array covering half the beam transmits the Gaussian power inside it", Tolerance = "0.5 %")]
        public void OffsetArray_ClipsTheBeam_LikeAnAperture()
        {
            // 256 cells cover x in [0, 2048 um] (offset +1024 um); Gaussian w = 0.6 mm
            var m = O("id", "mod", "type", "modulator", "cellsX", 256, "cellsY", 1, "pitchXM", Pitch, "pitchYM", 1.0, "offsetXM", 128 * Pitch);
            var r = Focal(m, Lam, 0.6e-3);
            // fluence exp(-2 x^2 / w^2): the half x > 0 holds 1/2; the array ends at 2048 um = 3.4 w, where the tail is below 1e-10
            double expect = 0.5;
            Assert.Close(expect, r.Records.First(x => x.Id == "mod").Values["transmission"], 0.005, 0, "clipped fraction");
        }
        [Test(Reference = "the three ways to give a map (inline array, NPZ file, callback \"@name\" evaluated at the cell centres) are the same device: identical focal fields", Tolerance = "1e-12")]
        public void Maps_InlineFileAndCallback_AreIdentical()
        {
            double theta = 3e-3, k = 2 * Math.PI / Lam;
            var reg = UltrafastEngine.Models.ModelRegistry.CreateDefault();
            reg.RegisterCallback("ramp", (UltrafastEngine.Models.FieldCallback)((x, y, z, o) => { for (int i = 0; i < o.Length; i++) o[i] = k * theta * x[i]; }));
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mod-map-" + Guid.NewGuid().ToString("N") + ".npz");
            NpzWriter.Write(file, new[] { new NdArray("phase_rad", Enumerable.Range(0, Cells).Select(i => k * theta * X(i)).ToArray(), Cells, 1) });
            try
            {
                var inline = Arr(Focal(Mod(Map(i => k * theta * X(i)))), "fluence_x_j_m2");
                var cb = Arr(Focal(O("id", "mod", "type", "modulator", "cellsX", Cells, "cellsY", 1, "pitchXM", Pitch, "pitchYM", 1.0, "phaseRad", "@ramp", "designWavelengthM", Lam, "offsetXM", 0.5 * Dx), reg: reg), "fluence_x_j_m2");
                var fl = Arr(Focal(O("id", "mod", "type", "modulator", "pitchXM", Pitch, "pitchYM", 1.0, "file", file, "designWavelengthM", Lam, "offsetXM", 0.5 * Dx)), "fluence_x_j_m2");
                double peak = inline.Max();
                for (int i = 0; i < inline.Length; i++) { Assert.Close(inline[i], cb[i], 0, 1e-12 * peak, "callback"); Assert.Close(inline[i], fl[i], 0, 1e-12 * peak, "file"); }
            }
            finally { System.IO.File.Delete(file); }
        }

        [Test(Reference = "a continuous-wave source and a pulse at the carrier see the same (monochromatic) device: the cw focal fluence pattern equals that of a 1 ns pulse", Tolerance = "2e-5 of the peak: a 1 ns pulse has a 0.4 GHz bandwidth, so the optical-path phase differs from the carrier's by ~1e-6 of its value across the band")]
        public void CwRun_EqualsThePulse_AtTheCarrier()
        {
            double k = 2 * Math.PI / Lam; var mod = Mod(Map(i => k * 3e-3 * X(i)));
            var sysJ = Sys(Source(0.3e-3), mod, Lens(F), Free("z", F));
            var pulse = Go(sysJ, Spec(Gauss(Lam, 1e-9, 1e-3), O("instruments", A(Bp("z")))));
            var cw = Go(Sys(Source(0.3e-3), Mod(Map(i => k * 3e-3 * X(i))), Lens(F), Free("z", F)), Spec(O("shape", "cw", "centerWavelengthM", Lam, "powerW", 1.0), O("instruments", A(Bp("z")))));
            var a = Arr(pulse, "fluence_x_j_m2"); var b = Arr(cw, "fluence_x_j_m2");
            double pa = a.Max(), pb = b.Max();
            for (int i = 0; i < a.Length; i++) Assert.Close(a[i] / pa, b[i] / pb, 0, 2e-5, "pattern");
        }

        [Test(Reference = "the interpolation error of the stored frequencies is 0 for a phase-free device and falls as the number of stored frequencies grows (compare K and 2K); too few frequencies warn", Tolerance = "error ratio 2.5-6 per doubling of K (1/K^2); below the warning level 0.05 at K = 32; warning at K = 3")]
        public void SpectralSamplingError_FallsWithTheStoredFrequencies_AndWarnsWhenTooFew()
        {
            var screen = Map(i => 40 * Math.Sin(2 * Math.PI * i / 37.0));             // 40 rad at the design wavelength: strongly chromatic
            double Err(int k, JsonObject m, out RunResult res)
            {
                var src = Source(0.3e-3); src["transverse"].AsObject().Set("spectralSamples", k);
                var free = Free("z", 1e-3); free["transverse"].AsObject().Set("spectralSamples", k);
                res = Go(Sys(src, m, free), Spec(Gauss(Lam, 30e-15, 1e-3)));
                return res.Records.First(x => x.Id == "mod").Values["spectralSamplingError"];
            }
            double e4 = Err(4, Mod(screen), out _), e8 = Err(8, Mod(screen), out _), e16 = Err(16, Mod(screen), out _), e32 = Err(32, Mod(screen), out _);
            // measured 1.43, 0.45, 0.10, 0.032: each doubling of K cuts the error 3-4.4 times (a 1/K^2 law); the test asks for 2.5-6
            foreach (var (a, b, name) in new[] { (e4, e8, "4 -> 8"), (e8, e16, "8 -> 16"), (e16, e32, "16 -> 32") })
                Assert.True(a / b > 2.5 && a / b < 6, "K " + name + ": error ratio " + a / b);
            Assert.True(e32 < 0.05, "no warning level at K = 32: " + e32);
            Assert.Close(0, Err(16, Mod(Map(i => 0.0)), out _), 0, 1e-12, "no phase, no error");
            Err(3, Mod(screen), out var coarse);
            Assert.True(coarse.Warnings.Any(w => w.Contains("do not resolve")), "warning when under-sampled");
        }

        [Test(Reference = "wavefront sensor slope maps: a tilt of the beam by theta has lambda dW/dx = theta everywhere on the beam and lambda dW/dy = 0", Tolerance = "1 %")]
        public void WavefrontSensor_SlopeMaps_ReportTheTilt()
        {
            double theta = 2e-3, w = 1e-3;
            var grid = O("model", "cartesian", "nx", 128, "ny", 128, "halfWidthM", 4e-3, "spectralSamples", 1,
                         "initialProfile", O("kind", "gaussian", "radiusM", w, "tiltXRad", theta));
            var r = Go(Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", grid)),
                       Spec(Gauss(Lam, 1e-12, 1e-3), O("instruments", A(O("kind", "wavefrontSensor", "name", "wfs", "at", "src", "pixels", 48)))));
            var wfs = r.Instruments.First(i => i.Name == "wfs");
            double Mean(string name) { var a = wfs.Arrays.First(x => x.Name.EndsWith(name)).Data; var v = a.Where(d => !double.IsNaN(d)).ToArray(); return v.Average(); }
            Assert.Close(theta, Mean("slope_x_rad"), 0.01, 0, "slope x");
            Assert.Close(0, Mean("slope_y_rad"), 0, 2e-5, "slope y");
        }

        [Test(Reference = "wavefront sensor slope maps of a diverging beam (radius of curvature R): W = r^2 / (2 R lambda), so lambda dW/dx = x / R and lambda dW/dy = y / R", Tolerance = "2 % of 1/R")]
        public void WavefrontSensor_SlopeMaps_OfADefocusedBeam_AreLinear()
        {
            double R = 1.0, w = 1e-3;          // the wavefront step between lenslets (167 um) stays under pi out to 2 w
            var grid = O("model", "cartesian", "nx", 128, "ny", 128, "halfWidthM", 4e-3, "spectralSamples", 1,
                         "initialProfile", O("kind", "gaussian", "radiusM", w, "radiusOfCurvatureM", R));
            var r = Go(Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", grid)),
                       Spec(Gauss(Lam, 1e-12, 1e-3), O("instruments", A(O("kind", "wavefrontSensor", "name", "wfs", "at", "src", "pixels", 48)))));
            var wfs = r.Instruments.First(i => i.Name == "wfs");
            double[] G(string n) => wfs.Arrays.First(x => x.Name.EndsWith(n)).Data;
            var xm = G("x_m"); var ym = G("y_m"); var sx = G("slope_x_rad"); var sy = G("slope_y_rad"); int px = xm.Length, py = ym.Length;
            double nx = 0, dx = 0, ny = 0, dy = 0;
            for (int i = 0; i < px; i++) for (int j = 0; j < py; j++)
            {
                double a = sx[i * py + j], b = sy[i * py + j];
                if (!double.IsNaN(a)) { nx += a * xm[i]; dx += xm[i] * xm[i]; }
                if (!double.IsNaN(b)) { ny += b * ym[j]; dy += ym[j] * ym[j]; }
            }
            Assert.Close(1 / R, nx / dx, 0.02, 0, "d slope_x / dx");
            Assert.Close(1 / R, ny / dy, 0.02, 0, "d slope_y / dy");
        }

        [Test(Reference = "zeroOrderLeak eta: t = a[(1-eta) e^(i phi) + eta] is passive: a flat map transmits exactly 1; a 2-level 0/pi grating with c0 = 0 has zero-order power eta^2 of the light", Tolerance = "1e-9 transmission, 1 % order power")]
        public void ZeroOrderLeak_IsPassive_AndSetsTheZeroOrder()
        {
            var flat = Focal(Mod(Map(i => 0.0), "zeroOrderLeak", 0.3));
            Assert.Close(1, flat.Records.First(x => x.Id == "mod").Values["transmission"], 1e-9, 0, "flat map");
            double eta = 0.2; double x0 = 0;
            var r = Focal(Mod(Map(i => (i % 2) * Math.PI), "zeroOrderLeak", eta));
            var xs = Arr(r, "x_m"); var f = Arr(r, "fluence_x_j_m2");
            // alternating 0 / pi cells: c0 = 0, so the zero order carries eta^2 (amplitude eta) of the incident energy
            Assert.Close(eta * eta, Window(xs, f, x0, 0.1e-3, Incident(r)), 0.01, 0, "zero-order power");
        }

        [Test(Reference = "a quantised continuous phase at a wavelength off the design one: the level just below 2 pi wraps to level 0, not to a full turn (which would differ off-design)", Tolerance = "exact")]
        public void Quantisation_WrapsTheTopHalfLevel_ToLevelZero()
        {
            var m = new UltrafastEngine.Optics.ModulatorElement { Nx = 2, Ny = 1, PitchXM = 2e-6, PitchYM = 1, PhaseRad = new[] { 2 * Math.PI - 0.01, 1.0 }, DesignOmega = 1e15, QuantisationLevels = 8, FillFactor = 1 };
            m.Prepare();
            var cells = m.CellsAt(new UltrafastEngine.Transverse.CartesianBasis(4, 1, 2e-6, 0.5), 1.3e15);
            Assert.Close(1, cells[1].Magnitude, 0, 1e-9, "unit amplitude");
            // cell 0 (x < 0): level 0 => phase 0 at every frequency; level 8 would give 2 pi * 1.3 = 1.88 rad
            Assert.Close(0, cells[1].Phase, 0, 1e-9, "phase of the top half-level");
        }

        [Test(Reference = "a quadratic phase -k x^2 / (2 f) is a thin lens: a Gaussian waist w at the device focuses at z = f to lambda f / (pi w) (Siegman, Lasers, ch. 17), as the lens element does", Tolerance = "2 % of the analytic radius, 1 % of the lens element's")]
        public void QuadraticPhase_FocusesLikeAThinLens()
        {
            double k = 2 * Math.PI / Lam, w = 0.3e-3;
            double Radius(JsonObject optic) => Go(Sys(Source(w), optic, Free("z", F)), Pulse(Lam, Bp("z"))).Instruments.First(i => i.Name == "bp").Values.GetDouble("radiusXM", double.NaN);
            double rm = Radius(Mod(Map(i => -k * X(i) * X(i) / (2 * F)))), rl = Radius(Lens(F));
            Assert.Close(Lam * F / (Math.PI * w), rm, 0.02, 0, "modulator lens: analytic focal radius");
            Assert.Close(rl, rm, 0.01, 0, "modulator lens: the lens element");
        }

        [Test(Reference = "chromatic index with a constant index modulation dn: phase = phi omega dn / (omega_d dn) = phi omega / omega_d, the opd rule", Tolerance = "1e-12 of the peak")]
        public void IndexRule_WithConstantDeltaN_IsTheOpdRule()
        {
            double lam2 = 1.2e-6; var map = Map(i => 2 * Math.PI * (i % 16) / 16);
            var a = Arr(Focal(Mod(map, "phaseWrapRad", 2 * Math.PI), lam2), "fluence_x_j_m2");
            var b = Arr(Focal(Mod(map, "phaseWrapRad", 2 * Math.PI, "chromatic", "index", "deltaIndexCauchy", A(0.2)), lam2), "fluence_x_j_m2");
            double peak = a.Max();
            for (int i = 0; i < a.Length; i++) Assert.Close(a[i], b[i], 0, 1e-12 * peak, "pattern");
        }

        [Test(Reference = "chromatic index: phase(omega) = phi_d alpha with alpha = omega dn(omega) / (omega_d dn(omega_d)) (liquid-crystal SLM, extended Cauchy dn = A + B / lambda^2); the same device as fixedPhase with the map multiplied by alpha; an n-level wrapped blaze then has the first-order efficiency of a staircase of depth 2 pi alpha, |c_1|^2 = |(1/N) sum_k exp(i 2 pi (alpha level_k - k / N))|^2 (direct Fourier coefficient, Goodman sec. 5.2)", Tolerance = "1e-12 of the peak for the equivalence; 1 % efficiency")]
        public void IndexRule_ScalesThePhaseByTheDispersion_AndSetsTheBlazeEfficiency()
        {
            double lam2 = 1.2e-6, A0 = 0.15, B0 = 0.02e-12, k = 2 * Math.PI / Lam;
            double Dn(double l) => A0 + B0 / (l * l);
            double alpha = (Lam / lam2) * Dn(lam2) / Dn(Lam);
            var cauchy = A(A0, B0);
            // equivalence on an unwrapped ramp (the wrap is applied at the design wavelength, before the chromatic scaling)
            // and with a cw source at lam2 (alpha is the factor at one frequency)
            RunResult Cw(JsonObject mod) => Go(Sys(Source(0.3e-3), mod, Lens(F), Free("z", F)), Spec(O("shape", "cw", "centerWavelengthM", lam2, "powerW", 1.0), O("instruments", A(Bp("z")))));
            var ri = Cw(Mod(Map(i => k * 2e-3 * X(i)), "chromatic", "index", "deltaIndexCauchy", cauchy));
            double lamCw = 2 * Math.PI * UltrafastEngine.Numerics.PhysicalConstants.SpeedOfLightMPerS / ri.At["src"].Profile.Omega[0];   // the cw line (snapped to its grid)
            double alphaCw = (Lam / lamCw) * Dn(lamCw) / Dn(Lam);
            var a = Arr(ri, "fluence_x_j_m2");
            var b = Arr(Cw(Mod(Map(i => alphaCw * k * 2e-3 * X(i)), "chromatic", "fixedPhase")), "fluence_x_j_m2");
            double peak = a.Max();
            for (int i = 0; i < a.Length; i++) Assert.Close(a[i], b[i], 0, 1e-12 * peak, "index = fixedPhase x alpha");
            // a 16-level blaze at lam2: depth 2 pi alpha (alpha = 0.833 here, against 0.858 for the opd rule)
            int n = 16;
            var r = Focal(Mod(Map(i => 2 * Math.PI * (i % n) / n), "phaseWrapRad", 2 * Math.PI, "chromatic", "index", "deltaIndexCauchy", cauchy), lam2);
            double x1 = lam2 * F / (n * Pitch), eff = Window(Arr(r, "x_m"), Arr(r, "fluence_x_j_m2"), x1, 0.4 * x1, Incident(r));
            double re = 0, im = 0; int ns = 4 * n;
            for (int j = 0; j < ns; j++) { double ph = 2 * Math.PI * alpha * (j / 4) / n - 2 * Math.PI * j / ns; re += Math.Cos(ph) / ns; im += Math.Sin(ph) / ns; }
            Assert.Close(re * re + im * im, eff, 0.01, 0, "first-order efficiency at depth alpha");
        }

        [Test(Reference = "cross-talk kernel w applied separably to the written map: a single raised cell of phase 1 with w = [1, 2, 1] / 4 gives 1/4, 1/2, 1/4 (discrete convolution); the Gaussian sigma equals the explicit kernel of its weights", Tolerance = "1e-12")]
        public void CrosstalkKernel_BlursTheWrittenMap_ByDiscreteConvolution()
        {
            Complex[] Cells(Action<UltrafastEngine.Optics.ModulatorElement> set)
            {
                var m = new UltrafastEngine.Optics.ModulatorElement { Nx = 5, Ny = 1, PitchXM = 2e-6, PitchYM = 1, PhaseRad = new[] { 0, 0, 1.0, 0, 0 }, DesignOmega = 1e15 };
                set(m); m.Prepare();
                // grid cells of 1 um centred at -5 ... 4 um: the odd ones lie inside device cells 0 ... 4
                var c = m.CellsAt(new UltrafastEngine.Transverse.CartesianBasis(10, 1, 5e-6, 0.5), 1e15);
                return Enumerable.Range(0, 5).Select(j => c[2 * j + 1]).ToArray();
            }
            var k = Cells(m => m.CrosstalkKernel = new[] { 1.0, 2, 1 });
            var expect = new[] { 0, 0.25, 0.5, 0.25, 0 };
            for (int j = 0; j < 5; j++) Assert.Close(expect[j], k[j].Phase, 0, 1e-12, "cell " + j);
            double s = 0.8; var w = Enumerable.Range(-3, 7).Select(i => Math.Exp(-0.5 * i * i / (s * s))).ToArray();
            var g = Cells(m => m.CrosstalkSigmaCells = s); var e = Cells(m => m.CrosstalkKernel = w);
            for (int j = 0; j < 5; j++) Assert.Close(e[j].Phase, g[j].Phase, 0, 1e-12, "Gaussian sigma = its kernel, cell " + j);
        }

        [Test(Reference = "geometry: a device rotated by +theta maps its cell centre (u, v) to the lab point R(theta) (u, v); a 2 x 2 map rotated by 90 deg puts cell (1, 0) at (+1, +1) um", Tolerance = "1e-12")]
        public void Rotation_TurnsTheCellGrid_AboutTheArrayCentre()
        {
            double Phase(double rot, int m)
            {
                var d = new UltrafastEngine.Optics.ModulatorElement { Nx = 2, Ny = 2, PitchXM = 2e-6, PitchYM = 2e-6, PhaseRad = new[] { 0, 0.5, 1.0, 1.5 }, DesignOmega = 1e15, RotationRad = rot };
                d.Prepare();
                return d.CellsAt(new UltrafastEngine.Transverse.CartesianBasis(4, 4, 2e-6, 2e-6), 1e15)[m].Phase;
            }
            // grid cell (ix, iy) = (3, 3) is centred at (+1, +1) um; (1, 3) at (-1, +1) um
            Assert.Close(1.5, Phase(0, 15), 0, 1e-12, "no rotation: cell (1, 1)");
            Assert.Close(1.0, Phase(Math.PI / 2, 15), 0, 1e-12, "90 deg: cell (1, 0) at (+1, +1) um");
            Assert.Close(1.5, Phase(Math.PI / 2, 7), 0, 1e-12, "90 deg: cell (1, 1) at (-1, +1) um");
        }

        [Test(Reference = "area average over a grid cell: a cell half on the array and half outside gets (t_cell + t_outside) / 2; gaps transmit gapTransmission, so a fill factor 1/2 with gap amplitude g passes 1/2 + g^2 / 2 of the power when every grid cell lies in a cell or a gap", Tolerance = "1e-12 edge cell, 1e-6 power")]
        public void EdgeCellsAndGaps_AreAreaAverages()
        {
            var d = new UltrafastEngine.Optics.ModulatorElement { Nx = 2, Ny = 1, PitchXM = 2e-6, PitchYM = 1, DesignOmega = 1e15, OutsideTransmission = 0.2 };
            d.Prepare();
            var c = d.CellsAt(new UltrafastEngine.Transverse.CartesianBasis(4, 1, 2e-6, 0.5), 1e15);
            Assert.Close(0.6, c[0].Real, 0, 1e-12, "grid cell [-2.5, -1.5] um across the array edge at -2 um");
            Assert.Close(1, c[1].Real, 0, 1e-12, "inside");
            var r = Focal(Mod(Map(i => 0.0), "fillFactor", 0.5, "gapTransmission", 0.6), Lam, 0.3e-3, 0.01);
            Assert.Close(0.5 + 0.5 * 0.36, r.Records.First(x => x.Id == "mod").Values["transmission"], 1e-6, 0, "power through cells and gaps");
        }

        [Test(Reference = "a callback map is read when the run compiles, so N trials of a loop that changes the callback equal N runs from N map files", Tolerance = "1e-12 of the peak")]
        public void Trials_WithACallbackMap_EqualRunsFromMapFiles()
        {
            double k = 2 * Math.PI / Lam, theta = 0;
            var reg = UltrafastEngine.Models.ModelRegistry.CreateDefault();
            reg.RegisterCallback("loop", (UltrafastEngine.Models.FieldCallback)((x, y, z, o) => { for (int i = 0; i < o.Length; i++) o[i] = k * theta * x[i]; }));
            var cbMod = O("id", "mod", "type", "modulator", "cellsX", Cells, "cellsY", 1, "pitchXM", Pitch, "pitchYM", 1.0, "phaseRad", "@loop", "designWavelengthM", Lam, "offsetXM", 0.5 * Dx);
            foreach (double th in new[] { 1e-3, -2e-3, 3e-3 })
            {
                theta = th;
                var cb = Arr(Focal(cbMod, reg: reg), "fluence_x_j_m2");
                string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mod-trial-" + Guid.NewGuid().ToString("N") + ".npz");
                NpzWriter.Write(file, new[] { new NdArray("phase_rad", Enumerable.Range(0, Cells).Select(i => k * th * X(i)).ToArray(), Cells, 1) });
                try
                {
                    var fl = Arr(Focal(O("id", "mod", "type", "modulator", "pitchXM", Pitch, "pitchYM", 1.0, "file", file, "designWavelengthM", Lam, "offsetXM", 0.5 * Dx)), "fluence_x_j_m2");
                    double peak = fl.Max();
                    for (int i = 0; i < fl.Length; i++) Assert.Close(fl[i], cb[i], 0, 1e-12 * peak, "trial theta = " + th);
                }
                finally { System.IO.File.Delete(file); }
            }
        }

        [Test(Reference = "one stored frequency (spectralSamples = 1) sits at the energy centroid of the spectrum (the carrier of a 1 ns Gaussian, within one grid bin); the cw source's single bin is snapped to its grid lattice, 1.2e-6 from the requested carrier (the run notes print it), so the monochromatic focal patterns agree to the pattern change of that offset", Tolerance = "stored frequency within 1e-7 of the carrier; pattern 3e-6 of the peak")]
        public void OneStoredFrequency_IsAtTheCarrier_AndTheCwPatternMatches()
        {
            double k = 2 * Math.PI / Lam;
            JsonObject One(JsonObject el) { el["transverse"].AsObject().Set("spectralSamples", 1); return el; }
            RunResult R(JsonObject pulse) => Go(Sys(One(Source(0.3e-3)), Mod(Map(i => k * 3e-3 * X(i))), Lens(F), One(Free("z", F))), Spec(pulse, O("instruments", A(Bp("z")))));
            var rp = R(Gauss(Lam, 1e-9, 1e-3)); var rc = R(O("shape", "cw", "centerWavelengthM", Lam, "powerW", 1.0));
            Assert.Close(W(Lam), rp.At["src"].Profile.Omega[0], 1e-7, 0, "the stored frequency of the pulse is its carrier (it was the low edge of the band before 2026-11-10)");
            var a = Arr(rp, "fluence_x_j_m2"); var b = Arr(rc, "fluence_x_j_m2");
            double pa = a.Max(), pb = b.Max();
            for (int i = 0; i < a.Length; i++) Assert.Close(a[i] / pa, b[i] / pb, 0, 3e-6, "pattern");
        }

        [Test(Reference = "the reported spectralSamplingError (a whole-bin interpolation residual) bounds the true error of the focal fluence map with K stored frequencies, measured against K = 64 (spike S2)", Tolerance = "bound holds at K = 8, 16, 32; the true error falls with K")]
        public void SpectralSamplingError_BoundsTheTrueErrorOfTheFocalMap()
        {
            var screen = Map(i => 40 * Math.Sin(2 * Math.PI * i / 37.0));
            (double proxy, double[] map) Run(int kk)
            {
                var src = Source(0.3e-3); src["transverse"].AsObject().Set("spectralSamples", kk);
                var z = Free("z", F); z["transverse"].AsObject().Set("spectralSamples", kk);
                var r = Go(Sys(src, Mod(screen), Lens(F), z), Spec(Gauss(Lam, 30e-15, 1e-3), O("instruments", A(Bp("z")))));
                return (r.Records.First(x => x.Id == "mod").Values["spectralSamplingError"], Arr(r, "fluence_x_j_m2"));
            }
            var refMap = Run(64).map; double norm = Math.Sqrt(refMap.Sum(v => v * v));
            double last = double.MaxValue;
            foreach (int kk in new[] { 8, 16, 32 })
            {
                var (proxy, map) = Run(kk);
                double err = Math.Sqrt(map.Zip(refMap, (a, b) => (a - b) * (a - b)).Sum()) / norm;
                Assert.True(proxy >= err, "K = " + kk + ": proxy " + proxy + " >= true error " + err);
                Assert.True(err < last, "true error falls with K: " + err);
                last = err;
            }
        }

        [Test(Reference = "naming (plan-ao-digital-twin-2026-11.md): \"phaseAtDesign\" is a phase given at the design wavelength and scaled with omega, the opd rule", Tolerance = "exact")]
        public void PhaseAtDesign_IsTheOpdRule()
        {
            var map = Map(i => 2 * Math.PI * (i % 16) / 16);
            var a = Arr(Focal(Mod(map, "phaseWrapRad", 2 * Math.PI), 1.2e-6), "fluence_x_j_m2");
            var b = Arr(Focal(Mod(map, "phaseWrapRad", 2 * Math.PI, "chromatic", "phaseAtDesign"), 1.2e-6), "fluence_x_j_m2");
            for (int i = 0; i < a.Length; i++) Assert.Equal(a[i], b[i], "pattern");
        }

        [Test(Reference = "validation: a map of the wrong shape, both phase and path, and a quantisation of one level are refused with a message", Tolerance = "exact")]
        public void BadInput_IsRefusedWithAMessage()
        {
            Assert.Throws<Exception>(() => Focal(Mod(Map(i => 0.0), "crosstalkKernel", A(-0.2, 1.4, -0.2))));      // negative weights: gain
            Assert.Throws<Exception>(() => Focal(Mod(Map(i => 0.0), "deltaIndexCauchy", A(0.2))));                // needs chromatic index
            Assert.Throws<Exception>(() => Focal(Mod(Map(i => 0.0), "chromatic", "index")));                      // needs a dn model
            Assert.Throws<Exception>(() => Focal(O("id", "mod", "type", "modulator", "cellsX", 10, "cellsY", 1, "pitchXM", Pitch, "phaseRad", Map(i => 0.0))));
            Assert.Throws<Exception>(() => Focal(Mod(Map(i => 0.0), "opdM", Map(i => 0.0))));
            Assert.Throws<Exception>(() => Focal(Mod(Map(i => 0.0), "quantisationLevels", 1)));
        }
    }
}
