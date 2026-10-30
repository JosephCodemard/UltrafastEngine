using System;
using System.Linq;
using UltrafastEngine.Lasers;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Zemax;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Phase 2.3: oscillators, saturable absorbers, apertures on profiles, broadband chi(2), ASE, thermal lens export.</summary>
    [TestArea("Lasers: Haus master equation, absorbers, cavities, apertures, carrier-resolved χ(2)")]
    public class LaserTests
    {
        private static JsonObject Grid(int n, double window) => O("grid", O("auto", false, "n", n, "windowS", window));
        private static double EnergyAbove(UltrafastEngine.Pulses.PulseField p, double f)
        {
            var g = p.Grid; var sp = p.SpectralIntensity(); double e = 0;
            for (int i = 0; i < g.N; i++) if (g.OmegaAbsolute[i] > f * g.OmegaCenter) e += sp[i] * g.DTime / g.N;
            return e;
        }

        private static JsonObject Sech(double lam, double fwhm, double e) => O("shape", "sech2", "centerWavelengthM", lam, "durationFwhmS", fwhm, "energyJ", e);

        [Test(Category = "laser", Reference = "soliton area theorem (Haus master equation)", Tolerance = "1e-12, 10 %")]
        public void Haus_SolitonModeLocking_ObeysTheAreaTheorem()
        {
            var osc = O("id", "laser", "type", "oscillator", "model", "haus", "repRateHz", 80e6, "outputCoupler", 0.05,
                "gain", O("smallSignalGain", 1.5, "saturationEnergyJ", 5e-9, "bandwidthM", 100e-9, "shape", "gaussian"),
                "lossPerRoundTrip", 0.02, "gddFs2", -500.0, "spmRadPerW", 1.5e-6,
                "absorber", O("kind", "kerrLens", "modulationDepth", 0.03, "kappaPerW", 3e-8),
                "start", "pulse", "roundTrips", O("max", 8000));
            var r = Go(Sys(osc), Spec(Sech(800e-9, 60e-15, 1e-9), Grid(4096, 8e-12)));
            var v = r.Records[0].Values;
            Assert.Close(1, v["converged"], 0, 0, "steady state reached");
            Assert.Close(1, v["pulsesInCavity"], 0, 0, "single pulse");
            Assert.Close(1.0, v["fwhmOverSoliton"], 0.1, 0, "FWHM vs 3.53 |GDD| / (delta E)");
            Assert.Close(80e6, r.Output.RepRateHz, 1e-12, 0, "output repetition rate");
            Assert.Close(v["outputEnergyJ"] * 80e6, v["averagePowerW"], 1e-12, 0, "average power");
        }

        [Test(Category = "laser", Reference = "cw laser: E = E_sat (g0 / L − 1)", Tolerance = "0.2 %")]
        public void Haus_CwLaser_GainClampsAtTheLoss()
        {
            // no absorber, no dispersion: E (at the gain) = E_sat (g0 / L - 1), L = -ln((1 - l)(1 - T_oc))
            double g0 = Math.Log(2.0), esat = 10e-9, l = 0.03, toc = 0.1;
            var osc = O("id", "laser", "type", "oscillator", "model", "haus", "repRateHz", 100e6, "outputCoupler", toc,
                "gain", O("smallSignalGain", 2.0, "saturationEnergyJ", esat), "lossPerRoundTrip", l, "roundTrips", O("max", 3000));
            var r = Go(Sys(osc), Spec(Sech(1030e-9, 300e-15, 1e-12), Grid(1024, 20e-12)));
            double L = -Math.Log((1 - l) * (1 - toc)), E = esat * (g0 / L - 1);
            Assert.Close(toc / (1 - toc) * E, r.Records[0].Values["outputEnergyJ"], 2e-3, 0, "clamped output energy");
            Assert.Close(L, r.Records[0].Values["netGainPerRoundTrip"], 2e-3, 0, "saturated gain = loss");
        }

        [Test(Category = "laser", Reference = "slow absorber: q0 (1 − e^−S) / S", Tolerance = "1e-09, 2 %")]
        public void Sesam_SlowAbsorber_SaturatesAsTheEnergyIntegral()
        {
            // short pulse on a slow absorber (no recovery during the pulse): absorbed fraction = q0 (1 - e^-S) / S, S = E / E_sat
            double q0 = 0.01, ns = 0.002, esat = 1e-9;
            foreach (double S in new[] { 0.01, 1.0, 5.0 })
            {
                var sys = Sys(O("id", "sa", "type", "saturableAbsorber", "kind", "slow", "modulationDepth", q0, "nonsaturableLoss", ns, "saturationEnergyJ", esat, "recoveryTimeS", 1e-9));
                var r = Go(sys, Spec(Gauss(1030e-9, 200e-15, S * esat)));
                double lossFrac = 1 - r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ();
                double expect = 1 - Math.Exp(-ns) * (1 - q0 * (1 - Math.Exp(-S)) / S);
                Assert.Close(expect, lossFrac, 0.02, 0, "loss at S = " + S);
                Assert.Close(r.Input.TotalEnergyJ() - r.Output.TotalEnergyJ(), r.Records[0].LossJ["saturable absorption"], 1e-9, 0, "budget");
            }
        }

        [Test(Category = "laser", Reference = "unsaturated fast absorber 1 − exp(−q0)", Tolerance = "1 %")]
        public void FastAbsorbers_LoseLessAtHighPower()
        {
            foreach (var spec in new[] { O("kind", "fast", "modulationDepth", 0.05, "saturationPowerW", 1e5), O("kind", "kerrLens", "modulationDepth", 0.05, "kappaPerW", 1e-6) })
            {
                double Loss(double e)
                {
                    var el = O("id", "sa", "type", "saturableAbsorber"); foreach (var kv in spec) el.Set(kv.Key, kv.Value);
                    var r = Go(Sys(el), Spec(Gauss(800e-9, 50e-15, e)));
                    return 1 - r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ();
                }
                Assert.Close(1 - Math.Exp(-0.05), Loss(1e-15), 0.01, 0, spec.GetString("kind") + " low-power loss");
                Assert.Less(Loss(1e-8), 0.6 * Loss(1e-15), spec.GetString("kind") + " saturates");
            }
        }

        [Test(Category = "laser")]
        public void Cavity_YbSesamOscillator_SolitonAndQmlDiagnostics()
        {
            var osc = O("id", "laser", "type", "oscillator", "model", "cavity", "repRateHz", 80e6, "outputCoupler", 0.05,
                "gain", O("medium", "Yb:YAG", "lengthM", 0.002, "slices", 6, "modeRadiusM", 60e-6, "passesPerRoundTrip", 2,
                          "pump", O("kind", "cw", "powerW", 8, "wavelengthM", 969e-9, "radiusM", 70e-6, "passes", 2)),
                "elements", A(O("id", "cm", "type", "chirpedMirror", "gddS2", -1500e-30, "bounces", 12),
                              O("id", "sesam", "type", "saturableAbsorber", "kind", "slow", "modulationDepth", 0.01, "nonsaturableLoss", 0.003,
                                "saturationFluenceJPerM2", 0.5, "spotRadiusM", 80e-6, "recoveryTimeS", 1e-12)),
                "start", "pulse", "seedEnergyJ", 1e-9, "roundTrips", O("max", 4000));
            var r = Go(Sys(osc), Spec(Sech(1030e-9, 300e-15, 1e-12), Grid(1024, 30e-12)));
            var v = r.Records[0].Values;
            Assert.Close(1, v["converged"], 0, 0, "converged");
            Assert.Close(1, v["pulsesInCavity"], 0, 0, "single pulse");
            Assert.Close(1.0, v["fwhmOverSoliton"], 0.15, 0, "soliton area theorem in a real cavity");
            Assert.Greater(v["qmlParameter"], 1, "stable against Q-switched mode-locking");
            Assert.Greater(v["averagePowerW"], 0.3, "watt-level output");
            Assert.Less(v["averagePowerW"], v["absorbedPumpPowerW"], "less out than absorbed");
            Assert.Close(-1500e-30 * 12, v["gddPerRoundTripS2"], 0.05, 0, "round-trip GDD (mirrors + crystal)");
            Assert.True(r.Records[0].Arrays.Any(a => a.Name.EndsWith("evolutionPower")), "evolution recorded");
        }

        [Test(Category = "laser", Reference = "ABCD eigenmode z_R = f, w0 = √(λf/π)", Tolerance = "8 %")]
        public void Cavity_SpatialLensGuide_ReproducesTheEigenmode()
        {
            // unfolded period: crystal, gap f, lens f, gap f -> ABCD [[0, f], [-1/f, 0]]: eigenmode z_R = f, w0 = sqrt(lambda f / pi)
            double f = 0.05, lam = 800e-9, w0 = Math.Sqrt(lam * f / Math.PI);
            JsonObject Tv(double R) => O("model", "radial", "points", 32, "radiusM", R);
            var osc = O("id", "laser", "type", "oscillator", "model", "cavity", "repRateHz", 80e6, "outputCoupler", 0.05,
                "gain", O("smallSignalGain", 1.3, "saturationEnergyJ", 10e-9),
                "elements", A(O("id", "xtal", "type", "bulk", "material", "Sapphire", "lengthM", 1e-3, "nonlinear", false, "beam", O("kind", "gaussian", "w0M", 0.7 * w0), "transverse", Tv(0.6e-3)),
                              O("id", "g1", "type", "gap", "material", "Vacuum", "lengthM", f, "transverse", Tv(1.5e-3)),
                              O("id", "lens", "type", "lens", "focalLengthM", f),
                              O("id", "g2", "type", "gap", "material", "Vacuum", "lengthM", f, "transverse", Tv(1.5e-3)),
                              O("id", "ap", "type", "aperture", "softRadiusM", 2 * w0)),
                "start", "pulse", "roundTrips", O("max", 40, "checkEvery", 1000));
            var r = Go(Sys(osc), Spec(Sech(lam, 100e-15, 1e-9), Grid(256, 4e-12)));
            Assert.True(r.Output.Profile != null, "profile carried out of the cavity");
            var (wx, _) = r.Output.Profile.SecondMomentRadius();
            Assert.Close(w0, wx, 0.08, 0, "beam settles near the lens-guide eigenmode (soft aperture selects it)");
        }

        [Test(Category = "laser")]
        public void Diagnostics_CountPulsesAndBackground()
        {
            int n = 1000; var p = new double[n];
            for (int i = 0; i < n; i++) p[i] = Math.Exp(-Math.Pow((i - 300) / 10.0, 2)) + 0.7 * Math.Exp(-Math.Pow((i - 700) / 10.0, 2));
            Assert.Equal(2, Diagnostics.CountPulses(p), "two pulses");
            var q = p.Select((x, i) => Math.Exp(-Math.Pow((i - 500) / 10.0, 2)) + 1e-3).ToArray();
            Assert.Equal(1, Diagnostics.CountPulses(q), "one pulse on a background");
            Assert.Greater(Diagnostics.BackgroundFraction(q, 1, 20), 0.03, "cw background found");
        }

        [Test(Category = "spatial")]
        public void Aperture_OnACarriedProfile_HardAndSoft()
        {
            double w = 0.5e-3, R = 0.5e-3, a = 0.6e-3;
            foreach (var (ap, expect) in new[] { (O("id", "ap", "type", "aperture", "radiusM", R), 1 - Math.Exp(-2 * R * R / (w * w))),
                                                 (O("id", "ap", "type", "aperture", "softRadiusM", a), 1 / (1 + w * w / (a * a))) })
            {
                var sys = Sys(O("id", "g", "type", "gap", "material", "Vacuum", "lengthM", 1e-6, "beam", O("kind", "gaussian", "w0M", w, "wavelengthM", 1030e-9),
                                "transverse", O("model", "radial", "points", 128, "radiusM", 2.5e-3)), ap);
                var r = Go(sys, Spec(Gauss(1030e-9, 300e-15, 1e-9)));
                Assert.Close(expect, r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ(), ap.Has("softRadiusM") ? 0.01 : 0.02, 0, ap.Has("softRadiusM") ? "soft aperture" : "hard aperture");
                Assert.True(r.Output.Profile != null, "profile carried on");
            }
        }

        [Test(Category = "nonlinear", Reference = "low-conversion SHG P = K P²", Tolerance = "0.1 %, 5 %")]
        public void Chi2Response_CarrierResolvedShg_MatchesLowConversionFormula()
        {
            // dispersionless material (n = 2): phase matched; P_SH = K P^2 with K = 8 pi^2 d^2 L^2 / (eps0 c n^3 lambda^2 A)
            double d = 2e-12, L = 1e-3, n = 2.0, lam = 1.03e-6, w = 50e-6, Ae = Math.PI * w * w / 2, E = 1e-8, T = 1e-12;
            var so = O("format", "ultrafast-system", "name", "chi2", "materials", O("flat", FlatMaterial(0, n)));
            so.Set("elements", A(O("id", "x", "type", "bulk", "material", "flat", "lengthM", L, "beam", O("kind", "constant", "areaM2", Ae),
                                   "timeResponse", O("kind", "chi2", "deffPmPerV", d * 1e12))));
            var sys = UltrafastEngine.Systems.SystemDescription.FromJson(so);
            var r = Go(sys, Spec(Gauss(lam, T, E), O("physics", O("fieldMode", "carrier", "raman", false, "selfSteepening", true), "grid", O("n", 8192, "windowS", 10e-12))));
            var pw = PulseMetrics.TemporalPower(r.Input); double dt = r.Input.Grid.DTime;
            double K = 8 * Math.PI * Math.PI * d * d * L * L / (8.8541878128e-12 * C * n * n * n * lam * lam * Ae);
            double eSh = K * pw.Sum(p => p * p) * dt;
            // the SH is either outside the envelope band (booked as "harmonics") or inside it (above 1.5 omega0)
            r.Records[0].LossJ.TryGetValue("harmonics (out of band)", out double outBand);
            var g = r.Output.Grid; var sp = r.Output.SpectralIntensity(); double inBand = 0;
            for (int i = 0; i < g.N; i++) if (g.OmegaAbsolute[i] > 1.5 * g.OmegaCenter) inBand += sp[i] * g.DTime / g.N;
            Assert.Close(eSh, outBand + inBand, 0.05, 0, "SH energy (low conversion)");
            // strong conversion: energy is conserved (the chi(2) drive is dealiased) -- in-band loss = out-of-band harmonics
            var r2 = Go(sys, Spec(Gauss(lam, T, 1e-6), O("physics", O("fieldMode", "carrier", "raman", false), "grid", O("n", 8192, "windowS", 10e-12))));
            double inLoss = r2.Input.TotalEnergyJ() - r2.Output.TotalEnergyJ();
            r2.Records[0].LossJ.TryGetValue("harmonics (out of band)", out double ob2);
            Assert.Close(inLoss, ob2, 1e-3, 1e-15, "energy conserved at high conversion");
            Assert.Greater(ob2 + EnergyAbove(r2.Output, 1.5), 0.2e-6, "strong SHG");
        }

        [Test(Category = "gain")]
        public void Ase_SeedsNoise_AndTheBudgetCloses()
        {
            RunResult Amp(bool ase) => Go(Sys(O("id", "amp", "type", "amplifier", "geometry", "multipass", "passes", 6, "medium", "Yb:YAG", "lengthM", 0.005,
                            "inversion", O("fraction", 0.3), "modeRadiusM", 0.3e-3, "spm", false, "ase", ase)), Spec(Gauss(1030e-9, 1e-12, 1e-15), Grid(2048, 50e-12)));
            var r0 = Amp(false); var r = Amp(true);
            var rec = r.Records[0];
            Assert.Greater(rec.Values["aseSeedEnergyJ"], 0, "spontaneous emission added");
            Assert.Greater(r.Output.TotalEnergyJ(), r0.Output.TotalEnergyJ() + 5 * rec.Values["aseSeedEnergyJ"], "amplified ASE on top of the amplified seed");
            Assert.Close(r.Input.TotalEnergyJ() - r.Output.TotalEnergyJ(), rec.LossJ.Values.Sum(), 1e-6, 1e-21, "budget closes");
        }

        [Test(Category = "nonlinear")]
        public void ExpressionResponse_SeesPositionAndFluence()
        {
            JsonObject Plate(string expr) => O("id", "p", "type", "bulk", "material", "FusedSilica", "lengthM", 0.002, "beam", O("kind", "gaussian", "w0M", 0.3e-3),
                "transverse", O("model", "radial", "points", 32, "radiusM", 1.5e-3), "replaceKerr", true, "timeResponse", O("kind", "expression", "expr", expr));
            double B(string expr) => Go(Sys(Plate(expr)), Spec(Gauss(1030e-9, 300e-15, 1e-5))).Records[0].BIntegralRad;
            double b0 = B("2.5e-20 * I"), bOut = B("2.5e-20 * I * (r > 2e-3)"), bIn = B("2.5e-20 * I * (r < 2e-3) * spatial");
            Assert.Greater(b0, 0.05, "reference");
            Assert.Less(bOut, 1e-9, "nothing outside the beam");
            Assert.Close(b0, bIn, 1e-9, 0, "r and spatial variables");
            double bF = B("1e-21 * F");
            Assert.Greater(bF, 0, "running fluence F available");
        }

        [Test(Category = "zemax", Reference = "GRIN thin lens n_r2 = −1 / (2 f L)", Tolerance = "1e-09")]
        public void ThermalLens_ExportsAsGradient1()
        {
            var sys = Sys(O("id", "g", "type", "gain", "medium", "Yb:YAG", "lengthM", 0.004, "slices", 8, "modeRadiusM", 0.3e-3,
                            "pump", O("kind", "cw", "powerW", 40, "wavelengthM", 940e-9, "radiusM", 0.4e-3, "profile", "tophat"), "thermalLens", O("rodRadiusM", 2e-3)));
            var r = Go(sys, Spec(Gauss(1030e-9, 10e-12, 1e-12)));
            var plan = ThermalLensExport.Plan(r.SummaryJson(), "g", 1e-3, 4.0);
            double f = r.Records[0].Values["thermalLensFocalLengthM"];
            Assert.Close(-1 / (2 * f * 0.004) * 1e-6, plan.Nr2PerLensUnit2, 1e-9, 0, "Nr2 per mm^2 from f_th");
            Assert.Greater(plan.N0, 1.8, "n0 = YAG index + Delta n");
            var fake = new FakeOpticStudio("slab");
            fake.SetGradient1(2, plan.N0, plan.Nr2PerLensUnit2, plan.DeltaT);
            Assert.Close(plan.Nr2PerLensUnit2, fake.Gradients[2].Nr2, 0, 0, "written");
        }
    }
}
