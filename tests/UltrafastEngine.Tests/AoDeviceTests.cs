using System;
using System.Diagnostics;
using System.Linq;
using UltrafastEngine.Plugins.Ao;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>The ao plugin elements (docs/plugins/ao.md): deformableMirror, slm, spectralShaper, aopdf.</summary>
    [TestArea("AO plugin: deformable mirror, SLM, spectral shaper and AOPDF elements")]
    public class AoDeviceTests
    {
        private const double Lam = 1030e-9;

        // ------------------------------------------------------------------------------------------ deformable mirror
        private static RunResult DmFocus(JsonObject dm, double f = 0.1, double w = 0.35e-3, double lam = Lam)
        {
            JsonObject G(JsonObject ip = null) { var t = O("model", "cartesian", "nx", 256, "ny", 256, "halfWidthM", 1.024e-3, "spectralSamples", 1, "absorbingBoundary", true); if (ip != null) t.Set("initialProfile", ip); return t; }
            return Go(Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", G(O("kind", "gaussian", "radiusM", w))), dm,
                          O("id", "lens", "type", "lens", "focalLengthM", f), O("id", "z", "type", "gap", "lengthM", f, "material", "Vacuum", "transverse", G())),
                      Spec(Gauss(lam, 1e-12, 1e-6), O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "z")))));
        }
        private static double Cx(RunResult r) { var x = r.Instruments.First(i => i.Name == "bp").Arrays.First(a => a.Name.EndsWith("x_m")).Data; var f = r.Instruments.First(i => i.Name == "bp").Arrays.First(a => a.Name.EndsWith("fluence_x_j_m2")).Data; return x.Zip(f, (a, b) => a * b).Sum() / f.Sum(); }

        private static JsonObject Dm(double slope, double aoi = 0, double coupling = 0.15)
        {
            int n = 12; double p = 125e-6; var cmd = new JsonArray();
            for (int i = 0; i < n; i++) { var row = new JsonArray(); for (int j = 0; j < n; j++) row.Add((i - (n - 1) / 2.0) * p * slope); cmd.Add(row); }
            return O("id", "dm", "type", "deformableMirror", "actuatorsX", n, "actuatorsY", n, "pitchM", p, "commandsM", cmd, "influenceCoupling", coupling, "incidenceAngleRad", aoi);
        }

        [Test(Reference = "influence function: the surface at a neighbouring actuator equals the stated coupling (definition), and a tilt ramp on the actuators gives a wavefront tilt 2 cos(aoi) dh/dx that moves the focus of a lens by f theta (geometric optics)", Tolerance = "2 % of the shift (the lattice sum of the influence functions sets dh/dx, computed from the same functions)")]
        public void TiltRamp_OnActuators_MovesTheFocus_ByFocalLengthTimesWavefrontTilt()
        {
            Assert.Close(0.15, DmGeometry.Influence(125e-6, 125e-6, 0.15), 1e-12, 0, "neighbour coupling");
            double slope = 2e-3, p = 125e-6; int n = 12;
            // surface slope at the centre from the same influence functions and commands
            var act = DmGeometry.Layout(O("actuatorsX", n, "actuatorsY", n), p, "test");
            double H(double x, double y) => act.Sum(a => (a[0] * slope) * DmGeometry.Influence(Math.Sqrt((x - a[0]) * (x - a[0]) + (y - a[1]) * (y - a[1])), p, 0.15));
            // first-moment theorem: the focus of a lens sits at f/k times the intensity-weighted mean of the phase gradient, k phi = 2 k h
            double w = 0.35e-3, h = 1e-6, num = 0, den = 0;
            for (int i = -60; i <= 60; i++) for (int j = -60; j <= 60; j++)
            {
                double x = i * 10e-6, y = j * 10e-6, wI = Math.Exp(-2 * (x * x + y * y) / (w * w));
                num += wI * (H(x + h, y) - H(x - h, y)) / (2 * h); den += wI;
            }
            double dhdx = num / den;
            var r0 = DmFocus(Dm(slope));
            var r60 = DmFocus(Dm(slope, aoi: Math.PI / 3));
            double f = 0.1;
            Assert.Close(f * 2 * dhdx, Cx(r0), 0.02, 0, "focus shift at normal incidence");
            Assert.Close(0.5, Cx(r60) / Cx(r0), 0.01, 0, "surface-to-wavefront factor 2 cos(aoi): cos 60 deg = 0.5");
        }

        [Test(Reference = "stroke limit and drive resolution: commands clip at +-stroke; a b-bit drive over +-stroke has 2^b - 1 steps", Tolerance = "exact (1e-12)")]
        public void StrokeAndDriveBits_ClipAndQuantiseCommands()
        {
            double p = 125e-6;
            JsonObject Mk(double cmd, int bits) { var c = new JsonArray(); for (int k = 0; k < 4; k++) c.Add(cmd); return O("id", "dm", "type", "deformableMirror", "actuatorsX", 2, "actuatorsY", 2, "pitchM", p, "commandsM", c, "strokeM", 1e-6, "driveBits", bits); }
            // a 3x-stroke command is the same surface as exactly the stroke
            Assert.Close(Cx(DmFocus(Mk(1e-6, 0))), Cx(DmFocus(Mk(3e-6, 0))), 1e-12, 1e-15, "clipped");
            // a uniform piston has no tilt: the focus stays on axis for any command
            Assert.Close(0, Cx(DmFocus(Mk(0.7e-6, 3))), 0, 1e-7, "piston");
            try { DmFocus(O("id", "dm", "type", "deformableMirror", "actuatorsX", 2, "pitchM", p, "driveBits", 8)); Assert.True(false, "driveBits without strokeM must be refused"); } catch (UltrafastEngine.Models.ModelException) { }
        }

        [Test(Reference = "hex layout: odd rows shifted by half a pitch, row spacing pitch sqrt(3)/2, so every interior actuator has six neighbours at one pitch", Tolerance = "1e-12")]
        public void HexLayout_HasSixNeighboursAtOnePitch()
        {
            double p = 1e-3; var a = DmGeometry.Layout(O("layout", "hex", "actuatorsX", 5, "actuatorsY", 5), p, "t");
            var c = a[12]; int near = a.Count(q => Math.Abs(Math.Sqrt((q[0] - c[0]) * (q[0] - c[0]) + (q[1] - c[1]) * (q[1] - c[1])) - p) < 1e-12);
            Assert.Equal(6, near, "neighbours");
            var ring = DmGeometry.Layout(O("layout", "ring", "ringCounts", new JsonArray().Add(1).Add(6).Add(12)), p, "t");
            Assert.Equal(19, ring.Count, "1 + 6 + 12");
        }

        // ------------------------------------------------------------------------------------------ SLM
        private const double Dx = 2e-6, Pitch = 8e-6, F = 0.05; private const int Nx = 2048, Cells = 512;
        private static JsonObject Line(JsonObject initial = null) { var t = O("model", "cartesian", "nx", Nx, "ny", 1, "halfWidthM", Nx * Dx / 2, "absorbingBoundary", true, "spectralSamples", 4); if (initial != null) t.Set("initialProfile", initial); return t; }
        private static RunResult SlmFocus(JsonObject slm, double lam = Lam, double w = 0.3e-3) =>
            Go(Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", Line(O("kind", "gaussian", "radiusM", w))), slm, O("id", "lens", "type", "lens", "focalLengthM", F),
                   O("id", "z", "type", "gap", "lengthM", F, "material", "Vacuum", "transverse", Line())),
               Spec(Gauss(lam, 1e-12, 1e-3), O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "z"), O("kind", "beamProfile", "name", "bp0", "at", "src")))));
        private static double[] Arr(RunResult r, string n, string suffix) => r.Instruments.First(i => i.Name == n).Arrays.First(a => a.Name.EndsWith(suffix)).Data;
        private static JsonObject Panel(string key, Func<int, double> f, params object[] extra)
        {
            var m = new JsonArray(); for (int i = 0; i < Cells; i++) m.Add(new JsonArray().Add(f(i)));
            var o = O("id", "slm", "type", "slm", "pixelsX", Cells, "pixelsY", 1, "pitchM", Pitch, "pitchYM", 1.0, "offsetXM", 0.5 * Dx);
            o.Set(key, m);
            foreach (var kv in O(extra)) o.Set(kv.Key, kv.Value);
            return o;
        }
        private static double Window(RunResult r, double x0, double half)
        {
            var x = Arr(r, "bp", "x_m"); var fl = Arr(r, "bp", "fluence_x_j_m2"); double norm = Arr(r, "bp0", "fluence_x_j_m2").Sum();
            return x.Zip(fl, (a, b) => Math.Abs(a - x0) <= half ? b : 0).Sum() / norm;
        }
        /// <summary>First-order efficiency of a periodic phase staircase sampled with 4 grid cells per pixel, whose phases are scaled by <paramref name="scale"/>.</summary>
        private static double SampledFirstOrder(double[] pixelPhase, double scale)
        {
            int n = pixelPhase.Length, ns = 4 * n; double re = 0, im = 0;
            for (int k = 0; k < ns; k++) { double a = scale * pixelPhase[k / 4] - 2 * Math.PI * k / ns; re += Math.Cos(a) / ns; im += Math.Sin(a) / ns; }
            return re * re + im * im;
        }

        [Test(Reference = "N-level blazed grating written with grey levels through the slm element: first-order efficiency of the sampled staircase (4 samples per pixel) and the continuum formula [sin(pi/N) / (pi/N)]^2 (Goodman, Fourier optics, sec. 5.2)", Tolerance = "0.6 % of the efficiency (sampled staircase), 2 % (continuum)")]
        public void Slm_GreyLevelBlaze_FirstOrderEfficiency_FollowsSincOfOneOverN()
        {
            foreach (int n in new[] { 4, 8, 16 })
            {
                var r = SlmFocus(Panel("greyLevel", i => i % n, "greyLevels", n, "dispersion", "none"));
                double x1 = Lam * F / (n * Pitch), eff = Window(r, x1, 0.4 * x1);
                var ph = Enumerable.Range(0, n).Select(k => 2 * Math.PI * k / n).ToArray();
                Assert.Close(SampledFirstOrder(ph, 1), eff, 0.006, 0, "N = " + n + " (sampled staircase)");
                Assert.Close(Math.Pow(Math.Sin(Math.PI / n) / (Math.PI / n), 2), eff, 0.02, 0, "N = " + n + " (continuous staircase)");
            }
        }

        [Test(Reference = "binary phase grating (4 pixels at phase 0, 4 at pi) through two grey levels: diffraction orders at +-lambda f / (8 pixels) carrying the sampled-staircase Fourier weight, none at the zero order (a pi step has c_0 = 0)", Tolerance = "0.6 % of the order efficiency; zero order below 1e-4")]
        public void Slm_PixelReplicaOrders_SitAtWavelengthTimesFocalLengthOverPeriod()
        {
            var r = SlmFocus(Panel("greyLevel", i => (i / 4) % 2, "greyLevels", 2, "dispersion", "none"));
            double x1 = Lam * F / (8 * Pitch);
            double eff = Window(r, x1, 0.4 * x1), effM = Window(r, -x1, 0.4 * x1);
            Assert.Close(SampledFirstOrder(new double[] { 0, 0, 0, 0, Math.PI, Math.PI, Math.PI, Math.PI }, 1), eff, 0.006, 0, "+1 order");
            Assert.Close(eff, effM, 1e-3, 0, "-1 order");
            Assert.Less(Window(r, 0, 0.4 * x1), 1e-4, "zero order (a pi step has none)");
        }

        [Test(Reference = "liquid-crystal dispersion: the 2 pi reset written at the design wavelength is 2 pi s(lambda) at lambda, s = lambda_d dn(lambda) / (lambda dn(lambda_d)); the first order of the wrapped ramp is the sampled Fourier coefficient with the phases scaled by s", Tolerance = "1.5 % of the efficiency")]
        public void Slm_WrappedRamp_AtOtherWavelengths_FollowsTheLiquidCrystalDispersion()
        {
            int n = 16; var cauchy = Slm.GenericNematicCauchy;
            double Dn(double l) => cauchy[0] + cauchy[1] / (l * l);
            var ph = Enumerable.Range(0, n).Select(k => 2 * Math.PI * k / n).ToArray();
            foreach (double lam in new[] { 900e-9, 1030e-9, 1200e-9 })
            {
                var r = SlmFocus(Panel("greyLevel", i => i % n, "greyLevels", n, "designWavelengthM", Lam), lam);
                double x1 = lam * F / (n * Pitch), eff = Window(r, x1, 0.4 * x1);
                double s = Lam * Dn(lam) / (lam * Dn(Lam));
                Assert.Close(SampledFirstOrder(ph, s), eff, 0.015, 0, "lambda = " + lam * 1e9 + " nm, s = " + s.ToString("F4"));
            }
            // the achromatic option scales with omega only: a different law, so the two must differ at 900 nm
            var rn = SlmFocus(Panel("greyLevel", i => i % n, "greyLevels", n, "designWavelengthM", Lam, "dispersion", "none"), 900e-9);
            double x9 = 900e-9 * F / (n * Pitch), effNone = Window(rn, x9, 0.4 * x9);
            Assert.Close(SampledFirstOrder(ph, Lam / 900e-9), effNone, 0.015, 0, "dispersion none: s = lambda_d / lambda");
        }

        [Test(Reference = "phase-target input: the target is wrapped to 2 pi and mapped to the nearest grey level of the (here S-shaped) curve; the result is the same as writing those grey levels directly", Tolerance = "1e-12 (identical fields)")]
        public void Slm_PhaseTarget_IsMappedToTheNearestGreyLevel_OfANonlinearCurve()
        {
            int levels = 64; var lut = Enumerable.Range(0, levels).Select(k => 2 * Math.PI * (0.5 + 0.5 * Math.Tanh(2 * ((double)k / levels - 0.5)) / Math.Tanh(1.0)) ).ToArray();
            for (int k = 0; k < levels; k++) Assert.Equal(k, Slm.NearestLevel(lut, lut[k] + 1e-9, 2 * Math.PI), "level " + k);
            Assert.Equal(0, Slm.NearestLevel(lut, 2 * Math.PI - 1e-6, 2 * Math.PI), "just below 2 pi is level 0 (wrap)");
            Func<int, double> target = i => 0.37 * i;       // a ramp that wraps
            Func<int, double> grey = i => Slm.NearestLevel(lut, target(i), 2 * Math.PI);
            var a = SlmFocus(Panel("phaseTargetRad", target, "greyLevels", levels, "lutKind", "tanh", "dispersion", "none"));
            var b = SlmFocus(Panel("greyLevel", grey, "greyLevels", levels, "lutKind", "tanh", "dispersion", "none"));
            var fa = Arr(a, "bp", "fluence_x_j_m2"); var fb = Arr(b, "bp", "fluence_x_j_m2");
            for (int i = 0; i < fa.Length; i++) Assert.Close(fb[i], fa[i], 1e-12, 1e-30, "fluence " + i);
        }

        [Test(Slow = true, Reference = "S2 scale spike (docs/design/notes/spikes.md): a 1920 x 1080 panel on a 2048 x 1280 grid, one frequency, ran in 1.9 s; here the panel is the slm element written with 8-bit grey levels (a phase-only lens-and-grating pattern from a callback); a Gaussian overfilling the panel passes the Gaussian power on the panel", Tolerance = "budget 13 s (the S2 limit); transmission 1e-3 of the erf product")]
        public void Slm_FullPanel_1920x1080_8Bit_RunsWithinTheScaleBudget()
        {
            int nx = 1920, ny = 1080; double pitch = 8e-6, w = 6e-3;
            var reg = UltrafastEngine.Models.ModelRegistry.CreateDefault();
            reg.RegisterCallback("target", (UltrafastEngine.Models.FieldCallback)((x, y, z, o) => { for (int i = 0; i < o.Length; i++) o[i] = 2 * Math.PI * (x[i] / 1e-3 + 0.5 * (x[i] * x[i] + y[i] * y[i]) / 1e-5); }));
            JsonObject G(JsonObject ip = null) { var t = O("model", "cartesian", "nx", 2048, "ny", 1280, "halfWidthM", 1024 * pitch, "halfWidthYM", 640 * pitch, "spectralSamples", 1); if (ip != null) t.Set("initialProfile", ip); return t; }
            var sys = Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", G(O("kind", "gaussian", "radiusM", w))),
                          O("id", "slm", "type", "slm", "pixelsX", nx, "pixelsY", ny, "pitchM", pitch, "greyLevels", 256, "phaseTargetRad", "@target", "offsetXM", 0.5 * pitch, "offsetYM", 0.5 * pitch),
                          O("id", "prop", "type", "gap", "lengthM", 0.2, "material", "Vacuum", "transverse", G()));
            var spec = Spec(O("shape", "cw", "centerWavelengthM", 800e-9, "powerW", 1.0), O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "end")), "outputs", O("keepProfiles", "used")));
            var sw = Stopwatch.StartNew(); var res = Simulation.Run(sys, spec, reg); sw.Stop();
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "slm 1920 x 1080, 256 grey levels, cw: {0:F2} s", sw.Elapsed.TotalSeconds));
            Assert.Less(sw.Elapsed.TotalSeconds, 13.0, "time");
            double Erf(double z) => AoMath.Erf(z);
            double expect = Erf(Math.Sqrt(2) * nx * pitch / 2 / w) * Erf(Math.Sqrt(2) * ny * pitch / 2 / w) / (Erf(Math.Sqrt(2) * 1024 * pitch / w) * Erf(Math.Sqrt(2) * 640 * pitch / w));
            Assert.Close(expect, res.Records.First(x => x.Id == "slm").Values["transmission"], 1e-3, 0, "power on the panel");
        }

        // ------------------------------------------------------------------------------------------ temporal shapers
        private static RunResult Temporal(JsonObject el, double fwhm = 30e-15, double window = 4e-12) => Go(Sys(el), Spec(Gauss(800e-9, fwhm, 1e-6), O("grid", O("windowS", window))));   // the planner does not know the time extent of a shaper: set the window
        private static double[] TimeAxis(RunResult r) => r.Output.Grid.Time;
        private static double[] Power(RunResult r) => PulseMetrics.TemporalPower(r.Output);

        [Test(Reference = "erf by series and continued fraction against tabulated values (Abramowitz and Stegun table 7.1): erf(0.5) = 0.5204998778130465, erf(1) = 0.8427007929497149, erf(2) = 0.9953222650189527, erf(3.5) = 0.9999992569016276", Tolerance = "1e-12")]
        public void Erf_MatchesTabulatedValues() { foreach (var (x, v) in new[] { (0.5, 0.5204998778130465), (1.0, 0.8427007929497149), (2.0, 0.9953222650189527), (3.5, 0.9999992569016276), (-1.0, -0.8427007929497149) }) Assert.Close(v, AoMath.Erf(x), 0, 1e-12, "erf " + x); }

        [Test(Reference = "an aopdf with delayFs moves the pulse by that group delay (shift theorem); efficiency scales the energy", Tolerance = "centroid 0.05 fs, energy 1e-9")]
        public void Aopdf_Delay_MovesThePulse_AndEfficiencyScalesTheEnergy()
        {
            var r0 = Temporal(O("id", "d", "type", "aopdf", "delayFs", 0.0)); var r1 = Temporal(O("id", "d", "type", "aopdf", "delayFs", 250.0, "efficiency", 0.4));
            Assert.Close(250e-15, TemporalCentroid(r1.Output) - TemporalCentroid(r0.Output), 0, 0.05e-15, "delay");
            Assert.Close(0.4, r1.Output.TotalEnergyJ() / r0.Output.TotalEnergyJ(), 1e-9, 0, "efficiency");
        }

        [Test(Reference = "linear chirp: a transform-limited Gaussian (FWHM t0) through GDD phi2 has FWHM t0 sqrt(1 + (4 ln2 phi2 / t0^2)^2) (Diels and Rudolph, Ultrashort laser pulse phenomena, eq. 2.4.23)", Tolerance = "0.5 %")]
        public void Aopdf_Gdd_BroadensTheGaussianAsTheTextbookFormula()
        {
            double t0 = 30e-15, gdd = 800e-30;
            var r = Temporal(O("id", "d", "type", "aopdf", "gddFs2", 800.0)); var m = PulseMetrics.Moments(TimeAxis(r), Power(r));
            Assert.Close(t0 * Math.Sqrt(1 + Math.Pow(4 * Math.Log(2) * gdd / (t0 * t0), 2)), 2 * Math.Sqrt(2 * Math.Log(2)) * m.Rms, 0.005, 0, "FWHM");
        }

        [Test(Reference = "finite acoustic window: with GDD phi2 the programmed group delay is phi2 (omega - omega0); components with |phi2 (omega - omega0)| > window/2 are not diffracted, so the transmitted fraction of a Gaussian spectrum (FWHM d_omega = 4 ln2 / t0) is erf(a / (sigma sqrt 2)) with a = window / (2 phi2) and sigma = d_omega / (2 sqrt(2 ln 2))", Tolerance = "0.3 % of the energy fraction")]
        public void Aopdf_AcousticWindow_CutsTheSpectrumWhereTheProgrammedDelayLeavesIt()
        {
            double t0 = 30e-15, gdd = 800e-30, window = 1.5e-12;
            var r0 = Temporal(O("id", "d", "type", "aopdf", "gddFs2", 800.0)); var r1 = Temporal(O("id", "d", "type", "aopdf", "gddFs2", 800.0, "windowS", window));
            double sigma = 4 * Math.Log(2) / t0 / (2 * Math.Sqrt(2 * Math.Log(2))), a = window / (2 * gdd);
            Assert.Close(AoMath.Erf(a / (sigma * Math.Sqrt(2))), r1.Output.TotalEnergyJ() / r0.Output.TotalEnergyJ(), 0.003, 0, "transmitted fraction");
            var r2 = Temporal(O("id", "d", "type", "aopdf", "delayFs", 3000.0, "windowS", 1e-12));
            Assert.Less(r2.Output.TotalEnergyJ() / r0.Output.TotalEnergyJ(), 1e-9, "a 3 ps delay is outside a 1 ps window");
        }

        [Test(Reference = "spurious replica H (1 + a exp(i omega tau)): a second pulse at tau with amplitude a (energy a^2), by the shift theorem", Tolerance = "energy ratio 1e-3, position 0.2 fs")]
        public void Aopdf_Replica_AddsASecondPulse_AtTheStatedDelay()
        {
            var r = Temporal(O("id", "d", "type", "aopdf", "replicaAmplitude", 0.2, "replicaDelayS", 600e-15));
            var t = TimeAxis(r); var p = Power(r); double dt = t[1] - t[0];
            double main = 0, rep = 0, rc = 0; for (int i = 0; i < t.Length; i++) { if (Math.Abs(t[i]) < 200e-15) main += p[i]; else if (Math.Abs(t[i] - 600e-15) < 200e-15) { rep += p[i]; rc += p[i] * t[i]; } }
            Assert.Close(0.04, rep / main, 1e-3, 0, "replica energy");
            Assert.Close(600e-15, rc / rep, 0, 0.2e-15, "replica position");
        }

        [Test(Reference = "Weiner (Rev. Sci. Instrum. 71, 1929 (2000)): a Fourier-plane mask with pixel gaps is a comb in frequency of period d_omega (one pixel); active fraction f gives side pulses at +-2 pi / d_omega with energy [sin(pi f) / (pi f)]^2 of the main pulse; a Gaussian spot of 1/e^2 radius w0 multiplies the first harmonic by exp(-(pi w0 / pitch)^2)", Tolerance = "1 % of the side-pulse energy ratio, 1 fs position")]
        public void SpectralShaper_PixelGaps_MakeSidePulses_AtTwoPiOverThePixelBandwidth()
        {
            double lam = 800e-9, c = 299792458.0, T = 0.8e-12, dw = 2 * Math.PI / T, D = 1e5, f = 0.75;
            double pitch = D * lam * lam * dw / (2 * Math.PI * c), w0 = pitch / 40;
            var r = Temporal(O("id", "s", "type", "spectralShaper", "pixels", 161, "pixelPitchM", pitch, "dispersionMPerM", D, "spotRadiusM", w0, "fillFactor", f, "gapTransmission", 0.0));
            var t = TimeAxis(r); var p = Power(r);
            double main = 0, plus = 0, minus = 0, pc = 0;
            for (int i = 0; i < t.Length; i++) { if (Math.Abs(t[i]) < 0.2e-12) main += p[i]; else if (Math.Abs(t[i] - T) < 0.2e-12) { plus += p[i]; pc += p[i] * t[i]; } else if (Math.Abs(t[i] + T) < 0.2e-12) minus += p[i]; }
                        double c1 = Math.Sin(Math.PI * f) / (Math.PI * f) * Math.Exp(-Math.Pow(Math.PI * w0 / pitch, 2));
            Assert.Close(c1 * c1, plus / main, 0.01, 0, "+T side pulse");
            Assert.Close(c1 * c1, minus / main, 0.01, 0, "-T side pulse");
            Assert.Close(T, pc / plus, 0, 1e-15, "side pulse position");
        }

        [Test(Reference = "shift theorem on the pixelated mask: pixels carrying the linear phase tau (omega_j - omega0) delay the pulse by tau (the main pulse of a staircase; Weiner)", Tolerance = "centroid of the main pulse within 2 fs of tau (the replica comb is outside the window of the centroid)")]
        public void SpectralShaper_LinearPixelPhase_DelaysThePulse()
        {
            double lam = 800e-9, c = 299792458.0, D = 1e5, pitch = 1e-6, tau = 40e-15;   // fine pixels: the replica pulses are at 2 pi / d_omega, far from the pulse
            double dwPix = pitch * 2 * Math.PI * c / (D * lam * lam);
            int n = 4001; var ph = new JsonArray();
            for (int j = 0; j < n; j++) { double dOmega = -(j - (n - 1) / 2.0) * dwPix; ph.Add(tau * dOmega); }       // x grows with wavelength, i.e. falls with omega
            var r0 = Temporal(O("id", "s", "type", "spectralShaper", "pixels", n, "pixelPitchM", pitch, "dispersionMPerM", D, "spotRadiusM", 0.5e-6));
            var r1 = Temporal(O("id", "s", "type", "spectralShaper", "pixels", n, "pixelPitchM", pitch, "dispersionMPerM", D, "spotRadiusM", 0.5e-6, "phaseRad", ph, "phaseWrapRad", 0.0));
            var t = TimeAxis(r1); var p = Power(r1);
            double m = 0, mt = 0; for (int i = 0; i < t.Length; i++) if (Math.Abs(t[i] - tau) < 100e-15) { m += p[i]; mt += p[i] * t[i]; }
            Assert.Close(tau, mt / m, 0, 2e-15, "delay");
            Assert.Close(1, r0.Output.TotalEnergyJ() / r1.Output.TotalEnergyJ(), 0.01, 0, "lossless apart from the spot blur");
        }
    }
}
