using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Optics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Stage 1.2 optical components and Stage 1.3 pedestals: each pinned to an analytic result.</summary>
    [TestArea("Components: beam tracking, ND filters, plates and ghosts, polarisers, Faraday rotators, waveplates, chirped mirrors, aberrations, pedestals")]
    public class ComponentTests
    {
        private const double Lam = 1030e-9;

        private static SystemDescription SysWith(JsonObject materials, params JsonObject[] elements)
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "t");
            if (materials != null) o.Set("materials", materials);
            o.Set("elements", new JsonArray(elements));
            return SystemDescription.FromJson(o);
        }

        private static JsonObject Run(JsonObject pulse, params JsonObject[] extra)
        {
            var o = O("format", "ultrafast-run", "version", 2);
            o.Set("pulse", pulse);
            foreach (var e in extra) foreach (var kv in e) o.Set(kv.Key, kv.Value);
            return o;
        }

        private static RunResult GoJ(SystemDescription s, JsonObject run) => Simulation.Run(s, Specs.RunSpec.FromJson(run));

        // ------------------------------------------------------------------------------------------ beam tracking

        [Test(Reference = "Gaussian optics (ABCD law)", Tolerance = "1e-12, 0.05 %, 1 %")]
        public void BeamTracking_LensFocusMatchesGaussianOptics()
        {
            double w0 = 1e-3, f = 0.5;
            var sys = SysWith(null,
                O("id", "g1", "type", "gap", "lengthM", 0.1),
                O("id", "lens", "type", "lens", "focalLengthM", f),
                O("id", "g2", "type", "gap", "lengthM", 0.5));
            var run = Run(Gauss(Lam, 200e-15, 1e-6), O("inputBeam", O("kind", "gaussian", "w0M", w0)));
            var r = GoJ(sys, run);
            // exact Gaussian ABCD: q0 = i zR -> +0.1 -> lens -> +0.5
            double zr = Math.PI * w0 * w0 / Lam;
            var q = new Complex(0.1, zr); q = q / (-q / f + 1); q += 0.5;
            double wExact = Math.Sqrt(-Lam / (Math.PI * (1 / q).Imaginary));
            var rec = r.Records.Last();
            Assert.Close(wExact, rec.Values["trackedBeamRadiusXM"], 5e-4, 0, "radius after the lens (gaps are air: L/n_air)");
            Assert.Close(Lam * f / (Math.PI * w0), rec.Values["trackedBeamRadiusXM"], 0.01, 0, "focal spot ~ lambda f / (pi w)");
            Assert.Close(rec.Values["trackedBeamRadiusXM"], rec.Values["trackedBeamRadiusYM"], 1e-12, 0, "round beam stays round");
        }

        [Test(Reference = "off-axis mirror: f_t = R cos θ / 2, f_s = R / (2 cos θ)", Tolerance = "0.05 %")]
        public void BeamTracking_OffAxisMirrorIsAstigmatic()
        {
            double w0 = 1e-3, R = 1.0, aoi = 10.0, d = 0.5;
            var sys = SysWith(null,
                O("id", "cm", "type", "mirror", "radiusOfCurvatureM", R, "aoiDeg", aoi),
                O("id", "g", "type", "gap", "lengthM", d));
            var r = GoJ(sys, Run(Gauss(Lam, 200e-15, 1e-6), O("inputBeam", O("kind", "gaussian", "w0M", w0))));
            double c = Math.Cos(aoi * Math.PI / 180), ft = R * c / 2, fs = R / (2 * c), zr = Math.PI * w0 * w0 / Lam;
            // collimated waist at the lens: w(d) = w0 sqrt((1 - d/f)^2 + (d/zR)^2)
            double wx = w0 * Math.Sqrt(Math.Pow(1 - d / ft, 2) + Math.Pow(d / zr, 2));
            double wy = w0 * Math.Sqrt(Math.Pow(1 - d / fs, 2) + Math.Pow(d / zr, 2));
            var v = r.Records.Last().Values;
            Assert.Close(wx, v["trackedBeamRadiusXM"], 5e-4, 0, "tangential (x) radius");
            Assert.Close(wy, v["trackedBeamRadiusYM"], 5e-4, 0, "sagittal (y) radius");
            Assert.True(r.Records[0].Notes.Any(n => n.Contains("off-axis mirror")), "note on the off-axis focal lengths");
        }

        [Test]
        public void BeamTracking_FeedsNonlinearElementsWithoutABeam()
        {
            var sys = SysWith(O("flat", FlatMaterial(2.5e-20)),
                O("id", "g", "type", "gap", "lengthM", 0.2),
                O("id", "nl", "type", "bulk", "material", "flat", "lengthM", 5e-3));
            var r = GoJ(sys, Run(Gauss(Lam, 200e-15, 20e-6), O("inputBeam", O("kind", "gaussian", "w0M", 0.3e-3, "zWaistM", 0.2))));
            Assert.Greater(r.Records[1].BIntegralRad, 0.01, "nonlinear with the tracked beam");
            Assert.True(r.Records[1].Notes.Any(n => n.Contains("tracked Gaussian beam")), "uses the tracked beam");
            Assert.False(r.Warnings.Any(w => w.Contains("has no beam")), "no missing-beam warning");
        }

        // ------------------------------------------------------------------------------------------ ND filter

        private static double Fresnel(double n) => Math.Pow((n - 1) / (n + 1), 2);

        [Test(Reference = "T = 10^−OD", Tolerance = "0.2 %")]
        public void NdFilter_AbsorptiveAndReflectiveOd()
        {
            foreach (var kind in new[] { "absorptive", "reflective" })
            {
                var sys = SysWith(null, O("id", "nd", "type", "ndFilter", "od", 1.0, "kind", kind, "material", "FusedSilica", "thicknessM", 2e-3));
                var r = GoJ(sys, Run(Gauss(Lam, 200e-15, 1e-6)));
                double n = Materials.MaterialLibrary.BuiltIn("FusedSilica").IndexAt(W(Lam));
                double t = 0.1 * (1 - Fresnel(n)) * (1 - Fresnel(n));
                Assert.Close(t, r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ(), 2e-3, 0, kind + " transmission");
                Assert.True(r.Records[0].LossJ.Keys.Any(k => k == (kind == "absorptive" ? "absorption" : "ndFilter (reflected)")), kind + ": loss mechanism");
            }
        }

        [Test]
        public void NdFilter_OdPerWavelengthShapesTheSpectrum()
        {
            var sys = SysWith(null, O("id", "nd", "type", "ndFilter", "od", new[] { 0.0, 2.0 }, "wavelengthsM", new[] { 1000e-9, 1060e-9 }, "material", "FusedSilica", "thicknessM", 1e-3));
            var r = GoJ(sys, Run(Gauss(Lam, 30e-15, 1e-6)));
            var g = r.Output.Grid;
            double n = Materials.MaterialLibrary.BuiltIn("FusedSilica").IndexAt(W(Lam)), tf = Math.Pow(1 - Fresnel(n), 2);
            foreach (var l in new[] { 1020e-9, 1045e-9 })
            {
                int i = Enumerable.Range(0, g.N).OrderBy(k => Math.Abs(g.OmegaAbsolute[k] - W(l))).First();
                double t = (r.Output.Ax[i].Magnitude * r.Output.Ax[i].Magnitude) / (r.Input.Ax[i].Magnitude * r.Input.Ax[i].Magnitude);
                // OD interpolated linearly in omega between the two samples
                double a = W(1000e-9), b = W(1060e-9), od = 2.0 * (g.OmegaAbsolute[i] - a) / (b - a);
                Assert.Close(Math.Pow(10, -od) * tf, t, 0.01, 0, "T near " + l * 1e9 + " nm");
            }
        }

        // ------------------------------------------------------------------------------------------ ghosts

        private static (double Delay, double Ratio) LargestSatellite(UltrafastEngine.Pulses.PulseField p, double tMin, double tMax)
        {
            var pw = PulseMetrics.TemporalPower(p); var t = p.Grid.Time;
            int ip = Array.IndexOf(pw, pw.Max());
            int best = -1;
            for (int i = 0; i < pw.Length; i++)
            {
                double dt = t[i] - t[ip];
                if (dt < tMin || dt > tMax) continue;
                if (best < 0 || pw[i] > pw[best]) best = i;
            }
            return (t[best] - t[ip], pw[best] / pw[ip]);
        }

        [Test(Reference = "ghost delay 2 n_g d / c, level R²", Tolerance = "0.2 %, 3 %")]
        public void Plate_GhostReplicaDelayAndIntensity()
        {
            double d = 5e-3;
            var sys = SysWith(null, O("id", "w", "type", "plate", "material", "FusedSilica", "thicknessM", d, "ghosts", true));
            var r = GoJ(sys, Run(Gauss(Lam, 100e-15, 1e-6)));
            var fs = Materials.MaterialLibrary.BuiltIn("FusedSilica");
            double delay = 2 * fs.GroupIndexAt(W(Lam)) * d / C, R = Fresnel(fs.IndexAt(W(Lam)));
            var (dt, ratio) = LargestSatellite(r.Output, 5e-12, 1e-9);
            Assert.Close(delay, dt, 0.002, 0, "ghost delay 2 n_g d / c");
            Assert.Close(R * R, ratio, 0.03, 0, "ghost relative intensity R^2");
            Assert.Greater(r.Output.Grid.N * r.Output.Grid.DTime / 2, 1.2 * delay, "the grid made room for the ghost");
        }

        [Test]
        public void Plate_GhostTiltedOutOfTheBeamByAWedge()
        {
            var sys = SysWith(null, O("id", "w", "type", "plate", "material", "FusedSilica", "thicknessM", 3e-3, "wedgeDeg", 0.5, "ghosts", O("beamRadiusM", 2e-3)));
            // the ghost is far below grid.satelliteLevel, so the planner makes no room for it: give the window explicitly
            var r = GoJ(sys, Run(Gauss(Lam, 100e-15, 1e-6), O("grid", O("auto", false, "n", 1 << 17, "windowS", 100e-12))));
            var (_, ratio) = LargestSatellite(r.Output, 5e-12, 45e-12);
            Assert.Less(ratio, 1e-10, "a 0.5 deg wedge separates the ghost from a 2 mm beam");
            var auto = GoJ(sys, Run(Gauss(Lam, 100e-15, 1e-6)));
            double delay = 2 * 1.46 * 3e-3 / C;
            Assert.Less(auto.Output.Grid.N * auto.Output.Grid.DTime / 2, delay, "no window room for a satellite below grid.satelliteLevel");
        }

        /// <summary>The Didenko mechanism: a post-pulse overlapping a stretched pulse in a Kerr medium makes a pre-pulse
        /// after compression, with intensity proportional to B^2.</summary>
        [Test]
        public void Ghost_KerrMediumCreatesPrePulsePartner()
        {
            double Pre(double n2)
            {
                var sys = SysWith(O("flat", FlatMaterial(n2)),
                    O("id", "w", "type", "plate", "material", "FusedSilica", "thicknessM", 1e-3, "ghosts", true),
                    O("id", "kerr", "type", "bulk", "material", "flat", "lengthM", 0.01, "beam", O("kind", "constant", "areaM2", 1e-8)),
                    O("id", "comp", "type", "dispersion", "gddFs2", -2.0e6));
                var r = GoJ(sys, Run(O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 150e-15, "energyJ", 2e-5, "chirp", O("gddFs2", 2.0e6)), O("physics", O("selfSteepening", false, "raman", false))));
                return LargestSatellite(r.Output, -15e-12, -5e-12).Ratio;
            }
            double p0 = Pre(0), p1 = Pre(1e-21), p2 = Pre(2e-21);
            Assert.Less(p0, 1e-9, "no pre-pulse without nonlinearity");
            Assert.Greater(p1, 100 * p0, "pre-pulse from the Kerr medium");
            Assert.Close(4.0, p2 / p1, 0.25, 0, "pre-pulse intensity ~ B^2");
        }

        // ------------------------------------------------------------------------------------------ polarisation optics

        private static PulseSummary Out(RunResult r) => PulseMetrics.Summarize(r.Output);

        [Test(Reference = "thin-film transfer matrix at the design angle")]
        public void ThinFilmPolarizer_TransmitsPReflectsS()
        {
            double T(string pol, string output)
            {
                var sys = SysWith(null, O("id", "tfp", "type", "thinFilmPolarizer", "output", output));
                var r = GoJ(sys, Run(O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 300e-15, "energyJ", 1e-6, "polarization", pol)));
                return r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ();
            }
            double tp = T("x", "transmitted"), ts = T("y", "transmitted"), rs = T("y", "reflected"), rp = T("x", "reflected");
            Assert.Greater(tp, 0.9, "Tp");
            Assert.Less(ts, 0.01, "Ts");
            Assert.Greater(rs, 0.99, "Rs");
            Assert.Less(rp, 0.1, "Rp");
            Assert.Greater(tp / ts, 100, "extinction");
        }

        [Test(Reference = "Faraday rotation V(λ) B L, non-reciprocal", Tolerance = "1e-09, 0.01 %, 0.1 %")]
        public void FaradayRotator_RotatesNonReciprocallyWithVerdetDispersion()
        {
            var sys = SysWith(null, O("id", "f1", "type", "faradayRotator", "rotationDeg", 45.0), O("id", "f2", "type", "faradayRotator", "rotationDeg", 45.0));
            var r = GoJ(sys, Run(Gauss(Lam, 300e-15, 1e-6)));
            var a = PulseMetrics.Summarize(r.At["f1"]); var b = Out(r);
            Assert.Close(1.0, a.S2, 1e-4, 0, "45 deg after one pass");
            Assert.Close(-1.0, b.S1, 1e-3, 0, "two passes add to 90 deg");
            var t = new FaradayTerm(Math.PI / 4, W(1064e-9), 0.258e-6, true);
            Assert.Close(Math.PI / 4 * (1.064 * 1.064 - 0.258 * 0.258) / (0.8 * 0.8 - 0.258 * 0.258), t.AngleAt(W(800e-9)), 1e-9, 0, "V ~ 1/(lambda^2 - lambda0^2)");
        }

        [Test(Reference = "retardance 2π Δn(λ) d / λ from the crystal's indices", Tolerance = "1e-09, 0.01 %, 0.1 %, 5 %")]
        public void ThickWaveplate_RetardanceFromMaterialDispersion()
        {
            var mo = Materials.MaterialLibrary.BuiltIn("Quartz"); var me = Materials.MaterialLibrary.BuiltIn("Quartz-e");
            double wd = W(Lam), dn = me.IndexAt(wd) - mo.IndexAt(wd);
            Assert.Close(0.0086, dn, 0.05, 0, "quartz birefringence at 1030 nm");
            double d = 5.5 * Lam / dn;
            var t = new BirefringentPlateTerm("hw", mo, me, d, 0, Math.PI / 4);
            Assert.Close(5.5, t.RetardanceRad(wd) / (2 * Math.PI), 1e-9, 0, "multi-order retardance");
            // compound zero-order: 1 mm and 1 mm - d_half
            var c = new BirefringentPlateTerm("cz", mo, me, 1e-3, 1e-3 - 0.5 * Lam / dn, Math.PI / 4);
            Assert.Close(0.5, c.RetardanceRad(wd) / (2 * Math.PI), 1e-9, 0, "compound zero-order retardance");

            // through the engine: x at 45 deg -> y at the design wavelength; a multi-order plate is more chromatic
            double S1At(int order, double lam)
            {
                var sys = SysWith(null, O("id", "wp", "type", "waveplate", "material", "Quartz", "order", order, "axisDeg", 45.0, "designWavelengthM", Lam));
                return Out(GoJ(sys, Run(Gauss(lam, 2e-12, 1e-6)))).S1;
            }
            Assert.Close(-1.0, S1At(0, Lam), 1e-4, 0, "zero-order half-wave at design");
            Assert.Close(-1.0, S1At(10, Lam), 1e-3, 0, "10th-order half-wave at design");
            Assert.Greater(1 + S1At(10, 1060e-9), 10 * (1 + S1At(0, 1060e-9)), "multi-order plate departs faster off design");
        }

        [Test(Reference = "group-delay difference Δn_g d / c", Tolerance = "5 %")]
        public void ThickWaveplate_SplitsPulseByGroupDelayDifference()
        {
            var mo = Materials.MaterialLibrary.BuiltIn("Quartz"); var me = Materials.MaterialLibrary.BuiltIn("Quartz-e");
            double d = 3e-3, dtau = (me.GroupIndexAt(W(Lam)) - mo.GroupIndexAt(W(Lam))) * d / C;
            var sys = SysWith(null, O("id", "wp", "type", "waveplate", "material", "Quartz", "thicknessM", d, "axisDeg", 45.0));
            var r = GoJ(sys, Run(Gauss(Lam, 20e-15, 1e-6)));
            var ax = r.Output.TemporalAx(); var t = r.Output.Grid.Time;
            // project on the crystal axes (45 deg): the two replicas are separated by dtau
            var ay = r.Output.TemporalAy();
            var u = Enumerable.Range(0, t.Length).Select(i => ((ax[i] + ay[i]) / Math.Sqrt(2)).Magnitude).ToArray();
            var v = Enumerable.Range(0, t.Length).Select(i => ((ax[i] - ay[i]) / Math.Sqrt(2)).Magnitude).ToArray();
            double tu = t[Array.IndexOf(u, u.Max())], tv = t[Array.IndexOf(v, v.Max())];
            Assert.Close(Math.Abs(dtau), Math.Abs(tu - tv), 0.05, 2e-16, "group delay difference of the axes");
        }

        // ------------------------------------------------------------------------------------------ ripple, Zernike

        [Test]
        public void ChirpedMirror_GddRippleAndComplementaryPairs()
        {
            double Gdd(bool pair)
            {
                var sys = SysWith(null, O("id", "cm", "type", "chirpedMirror", "gddS2", -50e-30, "bounces", 2, "rippleGddFs2", 30.0, "ripplePeriodNm", 20.0, "ripplePhaseRad", Math.PI / 2, "complementaryPairs", pair));
                return GoJ(sys, Run(Gauss(Lam, 100e-15, 1e-9))).Records[0].Own.GddS2 * 1e30;
            }
            Assert.Close(-100 + 60, Gdd(false), 1e-3, 0.05, "2 bounces: base + 2 x ripple at its maximum");
            Assert.Close(-100, Gdd(true), 1e-3, 0.05, "complementary pair cancels the ripple");
        }

        [Test(Reference = "Noll, J. Opt. Soc. Am. 66, 207 (1976): ordering and orthonormality")]
        public void Zernike_NollOrderingAndOrthonormality()
        {
            Assert.Equal((2, 0), Zernike.Noll(4), "Z4 defocus");
            Assert.Equal((2, -2), Zernike.Noll(5), "Z5 oblique astigmatism");
            Assert.Equal((4, 0), Zernike.Noll(11), "Z11 spherical");
            Assert.Equal((3, 1), Zernike.Noll(8), "Z8 coma x");
            int nr = 200, nt = 256;
            for (int j = 1; j <= 15; j++)
                for (int k = j; k <= 15; k++)
                {
                    double s = 0;
                    for (int a = 0; a < nr; a++)
                        for (int b = 0; b < nt; b++)
                        {
                            double rho = (a + 0.5) / nr, th = 2 * Math.PI * b / nt;
                            s += Zernike.Value(j, rho, th) * Zernike.Value(k, rho, th) * rho / nr * 2 * Math.PI / nt;
                        }
                    Assert.Close(j == k ? 1 : 0, s / Math.PI, 0, 2e-3, "<Z" + j + ", Z" + k + ">");
                }
        }

        [Test(Reference = "Maréchal approximation S = exp(−(2π σ)²)", Tolerance = "1 %")]
        public void Aberration_StrehlOnOneDBeamAndPhaseOnSpatialBeam()
        {
            // 1-D: Marechal estimate, defocus is not removed (only piston/tilt)
            var sys = SysWith(null, O("id", "ab", "type", "wavefront", "zernikeNollWaves", new[] { 0, 0.3, 0, 0, 0, 0, 0, 0, 0, 0, 0.05 }, "radiusM", 2e-3, "beamRadiusM", 1e-3));
            var r = GoJ(sys, Run(Gauss(Lam, 100e-15, 1e-6)));
            var v = r.Records[0].Values;
            Assert.Greater(v["strehlEstimate"], 0.5, "tilt does not count");
            Assert.Less(v["strehlEstimate"], 0.999, "spherical lowers the Strehl ratio");
            // spatial: pure defocus equals a lens of f = -R^2 / (4 sqrt(3) a lambda)
            double a = 0.25, R = 1e-3, fEq = -R * R / (4 * Math.Sqrt(3) * a * Lam);
            var tv = O("model", "cartesian", "nx", 64, "halfWidthM", 3e-3);
            var s1 = SysWith(null, O("id", "s", "type", "gap", "lengthM", 1e-3, "transverse", tv), O("id", "ab", "type", "wavefront", "zernikeNollWaves", new[] { 0, 0, 0, a }, "radiusM", R), O("id", "g", "type", "gap", "lengthM", 0.3, "transverse", tv));
            var s2 = SysWith(null, O("id", "s", "type", "gap", "lengthM", 1e-3, "transverse", tv), O("id", "ab", "type", "lens", "focalLengthM", fEq), O("id", "g", "type", "gap", "lengthM", 0.3, "transverse", tv));
            var run = Run(Gauss(Lam, 200e-15, 1e-6), O("inputBeam", O("kind", "gaussian", "w0M", 0.6e-3), "physics", O("beamTracking", "off")));
            var w1 = GoJ(s1, run).Records.Last().BeamRadiusXM; var w2 = GoJ(s2, run).Records.Last().BeamRadiusXM;
            Assert.Close(w2, w1, 0.01, 0, "Z4 defocus acts like the equivalent lens");
        }

        // ------------------------------------------------------------------------------------------ pedestals

        [Test]
        public void Pedestal_ReplicaPostPulseAndPrePulse()
        {
            var p = Gauss(Lam, 100e-15, 1e-6);
            p.Set("pedestal", A(O("kind", "postPulse", "delayS", 5e-12, "relativeIntensity", 1e-4), O("kind", "prePulse", "delayS", 3e-12, "relativeIntensity", 1e-6)));
            var r = GoJ(SysWith(null, O("id", "x", "type", "probe")), Run(p));
            var (dpost, rpost) = LargestSatellite(r.Output, 1e-12, 1e-9);
            var (dpre, rpre) = LargestSatellite(r.Output, -1e-9, -1e-12);
            Assert.Close(5e-12, dpost, 0.01, 0, "post-pulse after the main pulse");
            Assert.Close(1e-4, rpost, 0.02, 0, "post-pulse intensity");
            Assert.Close(-3e-12, dpre, 0.01, 0, "pre-pulse before");
            Assert.Close(1e-6, rpre, 0.02, 0, "pre-pulse intensity");
        }

        [Test]
        public void Pedestal_AseLevelAndPhaseNoiseConservesEnergy()
        {
            var p = Gauss(Lam, 100e-15, 1e-6);
            p.Set("pedestal", O("kind", "ase", "relativeIntensity", 1e-8));
            var r = GoJ(SysWith(null, O("id", "x", "type", "probe")), Run(p, O("grid", O("windowS", 40e-12, "auto", false, "n", 8192))));
            var pw = PulseMetrics.TemporalPower(r.Output); var t = r.Output.Grid.Time;
            double pk = pw.Max(), mean = Enumerable.Range(0, pw.Length).Where(i => Math.Abs(t[i]) > 3e-12 && Math.Abs(t[i]) < 15e-12).Average(i => pw[i]);
            Assert.Close(1e-8, mean / pk, 0.2, 0, "ASE mean level");

            double Ped(double rms)
            {
                var q = Gauss(Lam, 50e-15, 1e-6);
                q.Set("pedestal", O("kind", "phaseNoise", "rmsRad", rms, "correlationNm", 0.5));
                var rr = GoJ(SysWith(null, O("id", "x", "type", "probe")), Run(q));
                Assert.Close(rr.Input.TotalEnergyJ(), rr.Output.TotalEnergyJ(), 1e-9, 0, "phase noise conserves energy");
                var s = PulseMetrics.TemporalPower(rr.Output); var tt = rr.Output.Grid.Time;
                double tot = s.Sum(), far = Enumerable.Range(0, s.Length).Where(i => Math.Abs(tt[i]) > 1e-12).Sum(i => s[i]);
                return far / tot;
            }
            double p1 = Ped(0.05), p2 = Ped(0.1);
            Assert.Greater(p2, 2.5 * p1, "pedestal energy grows ~ rms^2");
        }
            // ------------------------------------------------------------------------------------------ cross-correlator

        [Test]
        public void CrossCorrelator_PostPulseArtefactAndNoiseFloor()
        {
            var p = Gauss(Lam, 200e-15, 1e-6);
            p.Set("pedestal", A(O("kind", "postPulse", "delayS", 8e-12, "relativeIntensity", 1e-4)));
            var run = Run(p, O("grid", O("windowS", 60e-12)));
            run.Set("instruments", A(O("kind", "crossCorrelator", "name", "xc", "rangeS", new[] { -20e-12, 20e-12 }, "stepS", 20e-15,
                "dynamicRangeDb", 120.0, "noise", false, "probeDelaysS", new[] { 8e-12, -8e-12, -15e-12 })));
            var r = GoJ(SysWith(null, O("id", "x", "type", "probe")), run);
            var v = r.Instruments.Single(i => i.Name == "xc").Values;
            Assert.Close(-40.0, v.GetDouble("contrastAt_8ps_db", 0), 0, 0.3, "post-pulse at +8 ps, 1e-4");
            Assert.Close(-80.0, v.GetDouble("contrastAt_m8ps_db", 0), 0, 1.0, "gate artefact at -8 ps, ~1e-8");
            Assert.Close(-120.0, v.GetDouble("contrastAt_m15ps_db", 0), 0, 0.5, "noise floor elsewhere");
            Assert.Close(8e-12, v.GetDouble("postPulseDelayS", 0), 0.01, 0, "post-pulse located");
            // the correlation of a Gaussian: FWHM = sqrt(1 + 1/2) x pulse FWHM (gate is I^2: sqrt(2) shorter)
            Assert.Close(Math.Sqrt(1.5) * 200e-15, v.GetDouble("fwhmS", 0), 0.02, 0, "correlation width");
        }
            // ------------------------------------------------------------------------------------------ vector spatial

        private static double Azimuth(Complex ex, Complex ey)
        {
            double xx = ex.Magnitude * ex.Magnitude, yy = ey.Magnitude * ey.Magnitude; var xy = ex * Complex.Conjugate(ey);
            return 0.5 * Math.Atan2(2 * xy.Real, xx - yy);
        }

        /// <summary>Nonlinear ellipse rotation resolved across the beam: on a radial grid the rotation follows the local
        /// intensity (exp(-2 r^2 / w^2)) and on axis it equals the 1-D vector model with the on-axis intensity.</summary>
        [Test]
        public void VectorSpatial_NonlinearEllipseRotationAcrossTheBeam()
        {
            double w = 0.5e-3, L = 2e-3, n2 = 1e-19, E = 2e-5;
            var pulse = O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 2e-12, "energyJ", E, "polarization", O("angleDeg", 0.0, "ellipticity", 0.4));
            var phys = O("physics", O("selfSteepening", false, "raman", false));
            var spatial = SysWith(O("flat", FlatMaterial(n2)), O("id", "k", "type", "bulk", "material", "flat", "lengthM", L, "beam", O("kind", "gaussian", "w0M", w),
                "transverse", O("model", "radial", "points", 48, "radiusM", 2.5 * w)));
            var r = GoJ(spatial, Run(pulse, phys));
            var oneD = SysWith(O("flat", FlatMaterial(n2)), O("id", "k", "type", "bulk", "material", "flat", "lengthM", L, "beam", O("kind", "constant", "areaM2", Math.PI * w * w / 2)));
            var r1 = GoJ(oneD, Run(pulse, phys));
            var g = r.Output.Grid; int ic = g.CarrierIndex;
            double psiIn = Azimuth(r.Input.Ax[ic], r.Input.Ay[ic]);
            int ic1 = r1.Output.Grid.CarrierIndex;
            double rot1 = Azimuth(r1.Output.Ax[ic1], r1.Output.Ay[ic1]) - Azimuth(r1.Input.Ax[ic1], r1.Input.Ay[ic1]);
            // plane wave, Gaussian pulse: theta = -(B/3) sin(2 chi) x sqrt(1/3) (carrier component averages the rotation over I(t))
            double b = 2 * Math.PI / Lam * n2 * L * E / (2e-12 * 1.0645) / (Math.PI * w * w / 2);
            Assert.Close(-b / 3 * Math.Sin(2 * Math.Atan(0.4)) * Math.Sqrt(1.0 / 3), rot1, 0.02, 0, "1-D vector model vs analytic ellipse rotation");
            var prof = r.Output.Profile;
            Assert.True(prof != null && prof.DataY != null, "vector profile carried");
            int f = Enumerable.Range(0, prof.Omega.Length).OrderBy(k => Math.Abs(prof.Omega[k] - g.OmegaAbsolute[ic])).First();
            int i = Enumerable.Range(0, g.N).OrderBy(k => Math.Abs(g.OmegaAbsolute[k] - prof.Omega[f])).First();
            int nf = prof.Omega.Length;
            double Rot(int m) => Azimuth(r.Output.Ax[i] * prof.Data[m * nf + f], r.Output.Ay[i] * prof.DataY[m * nf + f]) - psiIn;
            int m0 = Enumerable.Range(0, prof.Basis.M).OrderBy(m => prof.Basis.Radius[m]).First();
            Assert.Close(rot1, Rot(m0), 0.05, 0, "on-axis rotation = 1-D model at the on-axis intensity");
            int mh = Enumerable.Range(0, prof.Basis.M).OrderBy(m => Math.Abs(prof.Basis.Radius[m] - 0.6 * w)).First();
            double rr = prof.Basis.Radius[mh];
            Assert.Close(Math.Exp(-2 * rr * rr / (w * w)), Rot(mh) / Rot(m0), 0.04, 0, "rotation follows the local intensity");
        }

        [Test(Reference = "ghost delay 2 n_g L / c, level R²", Tolerance = "2 %")]
        public void Ghosts_OfBulkCrystalAndLensEndFaces()
        {
            // a 20 mm YAG rod (Fresnel ghost), an AR-coated BBO crystal and a thick lens: delay 2 n_g L / c, level R^2
            var sys = SysWith(null,
                O("id", "rod", "type", "bulk", "material", "YAG", "lengthM", 0.02, "ghosts", true),
                O("id", "bbo", "type", "crystal", "crystal", "BBO", "lengthM", 2e-3, "interaction", O("process", "shg", "type", "I"), "beam", O("kind", "constant", "radiusM", 1e-3),
                  "ghosts", O("frontReflectance", 0.002, "backReflectance", 0.002)),
                O("id", "lens", "type", "lens", "focalLengthM", 0.5, "material", "FusedSilica", "thicknessM", 5e-3, "ghosts", true));
            var r = GoJ(sys, Run(Gauss(Lam, 200e-15, 1e-9)));
            var rod = r.Records.First(x => x.Id == "rod").Notes.First(n => n.StartsWith("ghost of the end faces"));
            var yag = Materials.MaterialLibrary.BuiltIn("YAG"); double w0 = 2 * Math.PI * 299792458.0 / Lam, n0 = yag.IndexAt(w0);
            double R = Math.Pow((n0 - 1) / (n0 + 1), 2), ng = yag.GroupIndexAt(w0);
            Assert.True(rod.Contains((R * R).ToString("G3", System.Globalization.CultureInfo.InvariantCulture)), "rod ghost level R^2 in the note: " + rod);
            Assert.True(rod.Contains((2 * ng * 0.02 / 299792458.0 * 1e12).ToString("G4", System.Globalization.CultureInfo.InvariantCulture)), "rod ghost delay 2 n_g L / c: " + rod);
            var bbo = r.Records.First(x => x.Id == "bbo").Notes.First(n => n.StartsWith("ghost of the end faces"));
            Assert.True(bbo.Contains("4E-06") || bbo.Contains("4e-06"), "AR-coated crystal: 0.002^2: " + bbo);
            Assert.True(r.Records.First(x => x.Id == "lens").Notes.Any(n => n.StartsWith("ghost of the end faces")), "lens ghost");
            // the ghost is in the field: a contrast measurement sees the rod's post-pulse at its delay
            var r2 = GoJ(SysWith(null, O("id", "rod", "type", "bulk", "material", "YAG", "lengthM", 0.02, "ghosts", true)),
                Run(Gauss(Lam, 200e-15, 1e-9), O("instruments", A(O("kind", "contrast", "name", "c")))));
            Assert.Close(10 * Math.Log10(R * R), r2.Instruments[0].Values["postPulseContrastDb"].AsDouble(), 0.02, 0, "post-pulse level");
        }
    }
}
