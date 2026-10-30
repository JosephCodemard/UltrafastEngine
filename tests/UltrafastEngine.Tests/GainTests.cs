using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Gain;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Position-dependent material properties (docs/spatial-properties.md).</summary>
    [TestArea("Position-dependent properties of spatial elements")]
    public class SpatialPropertyTests
    {
        private const double Lam = 1030e-9;
        private static JsonObject Beam(double w0) => O("kind", "gaussian", "w0M", w0, "wavelengthM", Lam);

        [Test(Category = "spatial", Reference = "GRIN thin lens f = 1 / (n0 g² L); waist λf/(πw0)", Tolerance = "3 %")]
        public void IndexProfile_ParabolicGrin_FocusesLikeAThinLens()
        {
            // Delta n = -1/2 n0 g^2 r^2 over L: thin lens f = 1 / (n0 g^2 L); waist at f is lambda f / (pi w0)
            double L = 5e-3, n0 = 1.45, f = 0.2, g2 = 1 / (n0 * f * L), w0 = 0.5e-3;
            var reg = ModelRegistry.CreateDefault();
            reg.RegisterCallback("grin", (FieldCallback)((x, y, z, o) => { for (int i = 0; i < o.Length; i++) o[i] = -0.5 * n0 * g2 * (x[i] * x[i] + y[i] * y[i]); }));
            foreach (var prof in new JsonValue[] { "-0.5*" + n0.ToString("R") + "*" + g2.ToString("R") + "*r^2", "@grin" })
            {
                var sys = Sys(O("id", "rod", "type", "bulk", "material", "FusedSilica", "lengthM", L, "nonlinear", false, "beam", Beam(w0),
                                "transverse", O("model", "radial", "points", 96, "radiusM", 2e-3), "indexProfile", prof),
                              O("id", "toFocus", "type", "gap", "material", "Vacuum", "lengthM", f, "transverse", O("model", "radial", "points", 128, "radiusM", 2e-3)));
                var r = Go(sys, Spec(Gauss(Lam, 300e-15, 1e-9)), reg);
                Assert.Close(Lam * f / (Math.PI * w0), r.Records[1].BeamRadiusXM, 0.03, 0, "GRIN focus (" + prof.ToJson(false) + ")");
            }
        }

        [Test(Category = "spatial", Reference = "T = (1 − exp(−α0 L)) / (α0 L) for a matched Gaussian", Tolerance = "0.3 %, 1 %")]
        public void AbsorptionProfile_GaussianAlpha_ExactSpatialTransmissionAndBeamAveraged1D()
        {
            // alpha(r) = alpha0 exp(-2 r^2 / w^2) on a beam of the same radius: T = (1 - exp(-alpha0 L)) / (alpha0 L)
            double w = 0.5e-3, L = 0.01, a0 = 100;
            JsonObject El(bool spatial) => spatial
                ? O("id", "abs", "type", "bulk", "material", "FusedSilica", "lengthM", L, "nonlinear", false, "beam", Beam(w), "transverse", O("model", "radial", "points", 96, "radiusM", 2.5e-3),
                    "absorptionProfile", O("kind", "gaussian", "amplitude", a0, "radiusM", w))
                : O("id", "abs", "type", "bulk", "material", "FusedSilica", "lengthM", L, "nonlinear", false, "beam", Beam(w),
                    "absorptionProfile", O("kind", "gaussian", "amplitude", a0, "radiusM", w));
            var rs = Go(Sys(El(true)), Spec(Gauss(Lam, 300e-15, 1e-9)));
            Assert.Close((1 - Math.Exp(-a0 * L)) / (a0 * L), rs.Output.TotalEnergyJ() / rs.Input.TotalEnergyJ(), 0.003, 0, "spatial transmission");
            Assert.True(rs.Records[0].LossJ.ContainsKey("absorption"), "absorption in the energy budget");
            var r1 = Go(Sys(El(false)), Spec(Gauss(Lam, 300e-15, 1e-9)));
            // 1-D: intensity-weighted mean alpha = alpha0 / 2 -> T = exp(-alpha0 L / 2)
            Assert.Close(Math.Exp(-a0 * L / 2), r1.Output.TotalEnergyJ() / r1.Input.TotalEnergyJ(), 0.01, 0, "1-D beam-averaged transmission");
            Assert.True(r1.Records[0].Notes.Concat(r1.Compiled.Elements[0].Notes).Any(n => n.Contains("absorptionProfile")), "1-D note");
        }

        [Test(Category = "spatial")]
        public void FieldKinds_ExpressionTableAndCallback_Agree()
        {
            double w = 0.4e-3, L = 0.005;
            var rr = Enumerable.Range(0, 201).Select(i => i * 2.5e-3 / 200).ToArray();
            var reg = ModelRegistry.CreateDefault();
            reg.RegisterCallback("alpha", (FieldCallback)((x, y, z, o) => { for (int i = 0; i < o.Length; i++) o[i] = 50 + 40 * (x[i] * x[i] + y[i] * y[i]) / (w * w) * (1 + z[i] / L); }));
            var tableZ = new[] { 0.0, L };
            var vals = tableZ.SelectMany(z => rr.Select(r => 50 + 40 * r * r / (w * w) * (1 + z / L))).ToArray();
            var specs = new JsonValue[]
            {
                "50 + 40*r^2/" + (w * w).ToString("R") + "*(1 + z/" + L.ToString("R") + ")",
                O("kind", "table", "axes", "zr", "z", tableZ, "r", rr, "values", vals),
                "@alpha"
            };
            var T = specs.Select(sp =>
            {
                var sys = Sys(O("id", "a", "type", "bulk", "material", "FusedSilica", "lengthM", L, "nonlinear", false, "beam", Beam(w),
                                "transverse", O("model", "radial", "points", 64, "radiusM", 2e-3), "absorptionProfile", sp));
                var r = Go(sys, Spec(Gauss(Lam, 300e-15, 1e-9)), reg);
                return r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ();
            }).ToArray();
            Assert.Close(T[0], T[1], 2e-3, 0, "table vs expression");
            Assert.Close(T[0], T[2], 1e-6, 0, "callback vs expression");
            Assert.Less(T[0], 0.9, "absorbs");
        }

        [Test(Category = "spatial")]
        public void N2Scale_ScalesTheKerrPhase()
        {
            double B(double scale)
            {
                var sys = Sys(O("id", "p", "type", "bulk", "material", "FusedSilica", "lengthM", 0.002, "beam", Beam(0.3e-3),
                                "transverse", O("model", "radial", "points", 48, "radiusM", 1.5e-3), "n2Scale", scale));
                return Go(sys, Spec(Gauss(Lam, 300e-15, 1e-5))).Records[0].BIntegralRad;
            }
            double b1 = B(1), b2 = B(2), b0 = B(0);
            Assert.Greater(b1, 0.05, "reference B");
            Assert.Close(2 * b1, b2, 0.05, 0, "n2Scale 2 doubles B");
            Assert.Less(b0, 1e-6 + 1e-3 * b1, "n2Scale 0 removes the Kerr effect");
        }
    }

    /// <summary>Gain media, pumping, the gain and amplifier elements (docs/gain.md).</summary>
    [TestArea("Gain media, pumping, thermal lens and position-dependent properties")]
    public class GainTests
    {
        private const double Lam = 1030e-9;
        private static ElementRecord Rec(RunResult r, string id) => r.Records.First(x => x.Id == id);

        [Test(Category = "gain", Reference = "McCumber relation; published Yb:YAG σ_e(1030 nm) ≈ 2.1e-24 m² and emission peaks", Tolerance = "1e-09, 0.02 %, 0.15 %, 2 %, 15 %")]
        public void GainMedia_LibraryPeaksAndMcCumber()
        {
            var yb = GainMedium.Get("Yb:YAG");
            Assert.Close(1030e-9, yb.EmissionPeakM(1000e-9, 1060e-9), 1.5e-3, 0, "Yb:YAG emission peak");
            Assert.Close(2.1e-24, yb.SigmaE(GainMedium.Omega(1030e-9)), 0.15, 0, "Yb:YAG sigma_e(1030)");
            // McCumber: sigma_e / sigma_a = exp((E_zl - h nu) / kT)
            double w = GainMedium.Omega(1030e-9), ezl = GainMedium.H * GainMedium.C / yb.ZeroLineM, hv = GainMedium.H * w / (2 * Math.PI);
            Assert.Close(Math.Exp((ezl - hv) / (GainMedium.Kb * yb.TemperatureK)), yb.SigmaE(w) / yb.SigmaA(w), 1e-9, 0, "McCumber ratio");
            var ti = GainMedium.Get("Ti:sapphire");
            Assert.Close(795e-9, ti.EmissionPeakM(700e-9, 900e-9), 0.02, 0, "Ti:sapphire emission peak");
            Assert.Close(0, ti.SigmaA(GainMedium.Omega(800e-9)), 0, 1e-30, "four-level: no signal absorption");
            Assert.Close(1064.2e-9, GainMedium.Get("Nd:YAG").EmissionPeakM(1050e-9, 1080e-9), 2e-4, 0, "Nd:YAG emission peak");
            Assert.Throws<JsonException>(() => GainMedium.Get("unobtainium"), "unknown medium");
        }

        [Test(Category = "gain", Reference = "Frantz & Nodvik, J. Appl. Phys. 34, 2346 (1963)", Tolerance = "1e-12, 1e-09, 0.03 %")]
        public void GainElement_FrantzNodvik_SaturatedEnergyGain()
        {
            double w = 0.5e-3, A = Math.PI * w * w / 2;
            foreach (double E in new[] { 1e-9, 2e-3, 1e-2, 5e-2 })
            {
                var sys = Sys(O("id", "g", "type", "gain", "medium", "Yb:YAG", "lengthM", 0.005, "inversion", O("fraction", 0.25), "spm", false, "modeRadiusM", w));
                var r = Go(sys, Spec(Gauss(Lam, 10e-12, E)));
                var v = Rec(r, "g").Values;
                double x = E / A / v["saturationFluenceJPerM2"], G0 = v["smallSignalGain"];
                double G = Math.Log(1 + G0 * (Math.Exp(x) - 1)) / x;
                Assert.Close(G, r.Output.TotalEnergyJ() / E, 3e-4, 0, "Frantz-Nodvik gain at " + E + " J");
                Assert.Close(v["extractedEnergyJ"], r.Output.TotalEnergyJ() - E, 1e-9, 1e-18, "extracted energy = energy gained");
                Assert.Close(-v["extractedEnergyJ"], Rec(r, "g").LossJ["gain (from inversion)"], 1e-12, 0, "energy budget entry");
            }
        }

        [Test(Category = "gain", Reference = "G = exp((σ_e β − σ_a (1 − β)) N L)", Tolerance = "1e-09, 0.1 %")]
        public void GainElement_SmallSignalGainSpec_IsReproduced()
        {
            var sys = Sys(O("id", "g", "type", "gain", "medium", "Yb:YAG", "lengthM", 0.003, "inversion", O("smallSignalGain", 3.0), "spm", false, "modeRadiusM", 1e-3));
            var r = Go(sys, Spec(Gauss(Lam, 10e-12, 1e-9)));
            Assert.Close(3.0, Rec(r, "g").Values["smallSignalGain"], 1e-9, 0, "inversion set from the gain");
            Assert.Close(3.0, r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ(), 1e-3, 0, "weak-signal gain");
        }

        [Test(Category = "gain", Reference = "rate-equation steady state N2/Nt = σ_ap Φ / ((σ_ap + σ_ep) Φ + 1/τ)", Tolerance = "1 %")]
        public void Pumping_CwThinCrystal_RateEquationSteadyState()
        {
            // weakly absorbing (low doping): N2 / N_t = sigma_ap Phi / ((sigma_ap + sigma_ep) Phi + 1 / tau)
            double P = 20, wp = 0.5e-3, lp = 940e-9, Nt = 1e25;
            var sys = Sys(O("id", "g", "type", "gain", "medium", "Yb:YAG", "dopingDensityM3", Nt, "lengthM", 1e-4, "slices", 4, "modeRadiusM", 0.2e-3,
                            "pump", O("kind", "cw", "powerW", P, "wavelengthM", lp, "radiusM", wp, "profile", "tophat"), "thermalLens", false));
            var r = Go(sys, Spec(Gauss(Lam, 10e-12, 1e-12)));
            var m = GainMedium.Get("Yb:YAG"); double wpo = GainMedium.Omega(lp), sap = m.SigmaA(wpo), sep = m.SigmaE(wpo);
            double phi = P / (Math.PI * wp * wp) / (GainMedium.H * GainMedium.C / lp);
            double beta = sap * phi / ((sap + sep) * phi + 1 / m.LifetimeS);
            Assert.Close(beta, Rec(r, "g").Values["inversionFractionBefore"], 0.01, 0, "cw steady-state inversion");
            Assert.True(r.Records[0].Notes.Any(n => n.Contains("single shot")), "single-shot note without a repetition rate");
        }

        [Test(Category = "gain", Reference = "f = 2πK w²/(P_h dn/dT) (top hat), πK w²/(P_h dn/dT) (Gaussian; Innocenzi 1990)", Tolerance = "1e-09")]
        public void ThermalLens_TophatAndGaussianPump_MatchAnalyticFocalLengths()
        {
            foreach (var profile in new[] { "tophat", "gaussian" })
            {
                double wp = 0.4e-3;
                var sys = Sys(O("id", "g", "type", "gain", "medium", "Yb:YAG", "lengthM", 0.002, "slices", 8, "modeRadiusM", 0.3e-3,
                                "pump", O("kind", "cw", "powerW", 30, "wavelengthM", 940e-9, "radiusM", wp, "profile", profile),
                                "thermalLens", O("rodRadiusM", 2e-3, "heatFraction", 0.1)));
                var v = Rec(Go(sys, Spec(Gauss(Lam, 10e-12, 1e-12))), "g").Values;
                var m = GainMedium.Get("Yb:YAG");
                double Ph = v["heatW"];
                Assert.Close(0.1 * v["absorbedPumpPowerW"], Ph, 1e-9, 0, "heat = heatFraction x absorbed pump power");
                // tophat: f = 2 pi K w^2 / (P_h dn/dT); Gaussian (1/e^2 radius w): f = pi K w^2 / (P_h dn/dT)
                double f = (profile == "tophat" ? 2 : 1) * Math.PI * m.ThermalConductivity * wp * wp / (Ph * m.DnDT);
                Assert.Close(f, v["thermalLensFocalLengthM"], profile == "tophat" ? 0.01 : 0.04, 0, profile + " thermal focal length");
            }
        }

        [Test(Category = "gain", Reference = "small-signal regen growth (G0² (1 − L))^N", Tolerance = "0.2 %")]
        public void Regen_SmallSignal_GrowsAsGainSquaredTimesCavityTransmission()
        {
            int N = 5; double loss = 0.05;
            var sys = Sys(O("id", "rg", "type", "amplifier", "geometry", "regenerative", "roundTrips", N, "medium", "Yb:YAG", "lengthM", 0.004,
                            "inversion", O("fraction", 0.2), "spm", false, "modeRadiusM", 0.5e-3, "cavity", O("lossPerRoundTrip", loss)));
            var r = Go(sys, Spec(Gauss(Lam, 10e-12, 1e-12)));
            var v = Rec(r, "rg").Values; double G0 = v["smallSignalGain"];
            Assert.Close(Math.Pow(G0 * G0 * (1 - loss), N), r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ(), 2e-3, 0, "(G0^2 (1 - L))^N");
            Assert.Close(N, v["roundTrips"], 0, 0, "round trips");
            var arr = Rec(r, "rg").Arrays.First(a => a.Name.EndsWith("energyPerRoundTrip"));
            Assert.Equal(N + 1, arr.Data.Length, "energy per round trip (with the seed)");
        }

        [Test(Category = "gain")]
        public void Regen_AutoRoundTrips_EjectsAtTheEnergyMaximum_AndConservesEnergy()
        {
            var sys = Sys(O("id", "rg", "type", "amplifier", "geometry", "regenerative", "roundTrips", "auto", "medium", "Ti:sapphire", "lengthM", 0.01, "modeRadiusM", 0.4e-3,
                            "pump", O("kind", "pulsed", "energyJ", 6e-3, "wavelengthM", 532e-9, "radiusM", 0.45e-3, "passes", 2), "cavity", O("lossPerRoundTrip", 0.05)));
            var p = Gauss(800e-9, 100e-12, 1e-9);
            var r = Go(sys, Spec(p));
            var rec = Rec(r, "rg"); var v = rec.Values;
            var hist = rec.Arrays.First(a => a.Name.EndsWith("energyPerRoundTrip")).Data;
            int rt = (int)v["roundTrips"];
            Assert.Close(hist.Take(hist.Length).Max(), r.Output.TotalEnergyJ(), 1e-9, 0, "output is the maximum of the build-up");
            Assert.Close(hist.Skip(1).ToList().IndexOf(hist.Skip(1).Max()) + 1, rt, 0, 0, "ejection round trip");
            Assert.Greater(r.Output.TotalEnergyJ(), 1e-4, "amplified to the 100s of uJ level");
            Assert.Less(v["extractedEnergyJ"], v["storedEnergyJ"] * 1.0001, "cannot extract more than stored above transparency");
            double budget = rec.LossJ.Values.Sum();
            Assert.Close(r.Input.TotalEnergyJ() - r.Output.TotalEnergyJ(), budget, 1e-9, 1e-15, "energy budget closes");
        }

        [Test(Category = "gain")]
        public void TimeOrderedSaturation_RedLeadingChirp_ShiftsTheSpectrumRed()
        {
            // a strongly saturated pass: the leading colours see more gain; positive chirp (red first) -> red shift
            double Cent(double gdd)
            {
                var sys = Sys(O("id", "s", "type", "dispersion", "gddS2", gdd),
                              O("id", "g", "type", "gain", "medium", "Yb:YAG", "lengthM", 0.005, "inversion", O("fraction", 0.3), "spm", false, "modeRadiusM", 0.3e-3, "segments", 16));
                var r = Go(sys, Spec(Gauss(Lam, 300e-15, 5e-3)));
                return SpectralCentroid(r.Output);
            }
            double pos = Cent(+2e-24), neg = Cent(-2e-24);
            Assert.Greater(neg, pos, "omega centroid: red-leading chirp ends redder than blue-leading");
        }

        [Test(Category = "gain")]
        public void SteadyState_PulseTrain_ConvergesAndMatchesTheFullModel()
        {
            var sys = Sys(O("id", "mp", "type", "amplifier", "geometry", "multipass", "passes", 6, "medium", "Yb:YAG", "lengthM", 0.004, "modeRadiusM", 0.4e-3,
                            "pump", O("kind", "cw", "powerW", 40, "wavelengthM", 940e-9, "radiusM", 0.45e-3, "passes", 2),
                            "betweenPasses", O("loss", 0.02, "gddFs2", 500.0)));
            var p = Gauss(Lam, 20e-12, 2e-6); p.Set("repRateHz", 5000.0);
            var r = Go(sys, Spec(p));
            var rec = Rec(r, "mp"); var v = rec.Values;
            Assert.Close(1, v["converged"], 0, 0, "train converged");
            var train = rec.Arrays.First(a => a.Name.EndsWith("energyVsPulse")).Data;
            Assert.Greater(train.Length, 3, "several pulses iterated");
            Assert.Close(train.Last(), r.Output.TotalEnergyJ(), 0.02, 0, "energy model of the train vs full model of the last pulse");
            Assert.Close(r.Output.TotalEnergyJ() * 5000, v["averageOutputPowerW"], 1e-9, 0, "average power");
            Assert.True(v["thermalLensFocalLengthM"] > 0, "thermal lens reported (positive dn/dT)");
            Assert.Equal(7, rec.Arrays.First(a => a.Name.EndsWith("energyPerPass")).Data.Length, "energy per pass array");
            double budget = rec.LossJ.Values.Sum();
            Assert.Close(r.Input.TotalEnergyJ() - r.Output.TotalEnergyJ(), budget, 1e-9, 1e-15, "energy budget closes");
        }

        [Test(Category = "gain")]
        public void SpatialGain_RadialModel_ThermalLensAndPerPointInversion()
        {
            var sys = Sys(O("id", "rod", "type", "gain", "medium", "Yb:YAG", "lengthM", 0.004, "slices", 8,
                            "pump", O("kind", "cw", "powerW", 60, "wavelengthM", 940e-9, "radiusM", 0.6e-3, "profile", "superGaussian", "passes", 2),
                            "beam", O("kind", "gaussian", "w0M", 0.5e-3), "transverse", O("model", "radial", "points", 40), "thermalLens", O("rodRadiusM", 2e-3)));
            var p = Gauss(Lam, 2e-12, 1e-4); p.Set("repRateHz", 10000.0);
            var r = Go(sys, Spec(p, O("instruments", A(O("kind", "wavefrontSensor", "name", "wfs", "at", "rod")))));
            var rec = Rec(r, "rod");
            Assert.Greater(rec.Values["gain"], 1.2, "amplifies");
            Assert.True(rec.Notes.Any(n => n.Contains("thermal lens")), "thermal lens applied");
            var inv = rec.Arrays.First(a => a.Name.EndsWith("inversionVsCellBefore")).Data;
            var rad = rec.Arrays.First(a => a.Name.EndsWith("cellXM")).Data;
            Assert.Greater(inv[0], 5 * inv[Array.IndexOf(rad, rad.Max())], "inversion inside the pump, not outside");
            // the thermal lens converges the beam: wavefront radius of curvature is negative (converging) and of the order of f_th
            double R = r.Instruments.First(i => i.Name == "wfs").Values.GetDouble("radiusOfCurvatureM", 0);
            Assert.Less(R, 0, "converging wavefront after the thermal lens");
            double budget = rec.LossJ.Values.Sum();
            Assert.Close(r.Input.TotalEnergyJ() - r.Output.TotalEnergyJ(), budget, 1e-6, 1e-15, "energy budget closes");
        }
    }
}
