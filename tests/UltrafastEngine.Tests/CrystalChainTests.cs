// CrystalChainTests.cs -- crystals in a chain, and how phase matching limits them.
// SHG then SFG to the third harmonic in a Branched system: each port keeps its own carrier (the fundamental and the second
// harmonic differ, which the same-colour tests of fed ports cannot see). Conversion against crystal length and bandwidth.

using System;
using System.Linq;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Crystal chains and phase-matching limits: SHG to THG, length against bandwidth")]
    public class CrystalChainTests
    {
        private const double Lam = 1030e-9, C = 299792458.0;

        private static JsonObject Beam() => O("kind", "gaussian", "w0M", 300e-6);
        private static JsonObject Shg(double lengthM, JsonValue next) => O("id", "shg", "type", "crystal", "crystal", "BBO", "lengthM", lengthM, "interaction", O("process", "shg", "type", "I"),
            "cut", "auto", "beam", Beam(), "output", "sh", "record", A("fundamental"), "next", next);
        private static JsonObject Thg(double lengthM) => O("id", "thg", "type", "crystal", "crystal", "BBO", "lengthM", lengthM, "interaction", O("process", "sfg", "type", "I"),
            "cut", "auto", "beam", Beam(), "output", "sum", "record", A("a", "b"), "next", O("out", "end", "a", "virtual", "b", "virtual"),
            "aux", A(O("role", "b", "fromPort", true, "beam", O("kind", "gaussian", "radiusM", 300e-6))));

        private static SystemDescription Chain(bool shIsFed = true, double lengthM = 200e-6)
        {
            var s = Sys(Shg(lengthM, shIsFed ? O("out", "stage", "fundamental", "thg") : O("out", "thg", "fundamental", "stage")),
                O("id", "stage", "type", "delay", "delayS", 0.0, "next", "thg:b"), Thg(lengthM));
            s.Topology = SystemGraph.Branched;
            return s;
        }

        [Test(Category = "physics", Reference = "Manley-Rowe: one photon each of w and 2w per photon of 3w", Tolerance = "1e-3")]
        public void ShgThenSfg_ConservesPhotonsAtTheThirdHarmonic()
        {
            // narrowband: a broad pulse shifts the mean photon energy of each wave as it converts, by 0.4 % at 100 fs
            var r = Go(Chain(), Spec(Gauss(Lam, 2e-12, 2e-3)));
            var s = r.Records.First(x => x.Id == "shg").Values; var t = r.Records.First(x => x.Id == "thg").Values;
            Assert.Greater(t["energy_sum_J"], 10e-6, "the third harmonic is made");
            double lostW = s["energy_fundamental_J"] - t["energy_a_J"], lost2W = s["energy_sh_J"] - t["energy_b_J"];
            Assert.Close(lostW, lost2W / 2, 1e-3, 0, "w and 2w lose photons at the same rate");
            Assert.Close(lostW * 3, t["energy_sum_J"], 1e-3, 0, "each photon of 3w takes one of each");
        }

        [Test(Category = "physics", Reference = "the crystal does not care which beam is called main", Tolerance = "1e-3")]
        public void ShgThenSfg_MainAndFedBeamsCanSwap()
        {
            var run = Spec(Gauss(Lam, 100e-15, 100e-6));
            Assert.Close(Go(Chain(), run).Output.TotalEnergyJ(), Go(Chain(shIsFed: false), run).Output.TotalEnergyJ(), 1e-3, 0, "third harmonic energy");
        }

        [Test(Category = "physics", Reference = "BBO type I THG of 1064 nm: theta = 31.3 deg (Dmitriev, Handbook of Nonlinear Optical Crystals)", Tolerance = "0.3 deg")]
        public void AutoCut_OfAFedThirdHarmonicStage_IsPhaseMatched()
        {
            var r = Go(Chain(), Spec(Gauss(1064e-9, 100e-15, 1e-9)));
            var v = r.Records.First(x => x.Id == "thg").Values;
            Assert.Close(31.3, v["thetaDeg"], 0, 0.3, "cut");
            Assert.Less(Math.Abs(v["deltaK0PerM"]) * 200e-6, 0.3, "phase matched, within the snapping of carriers to the grid");
        }

        [Test(Category = "physics", Reference = "Weiner, Ultrafast Optics: efficiency grows as L^2 for L << tau / GVM, as L beyond it", Tolerance = "2 %")]
        public void Conversion_GrowsAsLengthSquared_ThenLinearlyOnceWalkOffLimitsIt()
        {
            double Eta(double tau, double l) => Go(Sys(O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", l, "interaction", O("process", "shg", "type", "I"), "cut", "auto", "chi3", false,
                "beam", O("kind", "gaussian", "w0M", 1e-3), "output", "sh")), Spec(Gauss(Lam, tau, 1e-9))).Records[0].Values["energy_sh_J"];
            Assert.Close(4, Eta(2e-12, 2e-3) / Eta(2e-12, 1e-3), 0.02, 0, "narrowband: L^2");
            Assert.Close(2, Eta(30e-15, 4e-3) / Eta(30e-15, 2e-3), 0.02, 0, "broadband, beyond tau / GVM = 0.3 mm: L");
        }

        [Test(Category = "physics", Reference = "sinc^2 phase-matching bandwidth: 0.886 / (GVM L); GVM from the crystal's group indices", Tolerance = "3 %")]
        public void SecondHarmonicBandwidth_IsSetByTheCrystal()
        {
            const double gvmSPerM = 94.5e-12; // BBO type I at 1030 nm, group indices of Eimerl et al. (1987)
            foreach (double l in new[] { 2e-3, 4e-3 })
            {
                var r = Go(Sys(O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", l, "interaction", O("process", "shg", "type", "I"), "cut", "auto", "chi3", false,
                    "beam", O("kind", "gaussian", "w0M", 1e-3), "output", "sh")), Spec(Gauss(Lam, 15e-15, 1e-9)));
                double dLam = Results.PulseMetrics.Summarize(r.Output).SpectralFwhmM, lam2 = Lam / 2;
                Assert.Close(0.886 / (gvmSPerM * l), C * dLam / (lam2 * lam2), 0.03, 0, "bandwidth at L = " + l);
            }
        }

        [Test(Category = "physics", Reference = "third-order cross-correlation of Gaussians: FWHM = sqrt(tau^2 + (tau / sqrt 2)^2)", Tolerance = "1 %")]
        public void DelayScanOfTheThirdHarmonic_IsTheCrossCorrelation()
        {
            const double tau = 100e-15, d = 100e-15;
            var values = A(-d, 0.0, d);
            var study = O("parameters", A(O("path", "element:stage/delayS", "values", values, "nominal", 0.0)), "outputs", A("energy:end"));
            // thin crystals: walk-off in 200 um already adds 1 %
            var y = Engine.Studies.Run(Chain(lengthM: 10e-6), Spec(Gauss(Lam, tau, 1e-9)), Engine.StudySpec.FromJson(study)).Outputs;
            double ratio = Math.Sqrt(y[0][0] * y[2][0]) / y[1][0]; // the geometric mean cancels the small offset of the peak
            Assert.Close(tau * Math.Sqrt(1.5), d * Math.Sqrt(-4 * Math.Log(2) / Math.Log(ratio)), 0.01, 0, "FWHM");
        }

        private static JsonObject Crossing(double w, double l, double tau, params object[] interaction) => O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", l, "chi3", false, "cut", "auto",
            "interaction", O(new object[] { "process", "sfg", "type", "I" }.Concat(interaction).ToArray()), "beam", O("kind", "gaussian", "w0M", w), "deffPmPerV", 2.0, "output", "sum",
            "aux", A(O("role", "b", "pulse", Gauss(Lam, tau, 1e-9), "delayS", 0.0, "beam", O("kind", "gaussian", "radiusM", w))));

        [Test(Category = "physics", Reference = "type I phase matching of crossing equal beams: n_e(2w, theta) = n_o(w) cos(alpha / 2)", Tolerance = "0.01 deg")]
        public void NoncollinearShg_CutFollowsTheAnalyticCondition()
        {
            double alpha = 4 * Math.PI / 180, w = 2 * Math.PI * C / Lam;
            var bbo = Crystals.CrystalLibrary.Get("BBO", double.NaN);
            var o = new double[1]; var e = new double[1];
            bbo.Nx.Evaluate(new[] { w }, o); double noW = o[0];
            bbo.Nx.Evaluate(new[] { 2 * w }, o); double no2 = o[0];
            bbo.Nz.Evaluate(new[] { 2 * w }, e); double ne2 = e[0];
            double n = noW * Math.Cos(alpha / 2), sin2 = (1 / (n * n) - 1 / (no2 * no2)) / (1 / (ne2 * ne2) - 1 / (no2 * no2));
            var v = Go(Sys(Crossing(1e-3, 100e-6, 1e-12, "noncollinearDeg", 4.0)), Spec(Gauss(Lam, 1e-12, 1e-9))).Records[0].Values;
            Assert.Close(Math.Asin(Math.Sqrt(sin2)) * 180 / Math.PI, v["thetaDeg"], 0, 0.01, "cut");
            Assert.Close(4.0, v["noncollinearDeg"], 1e-6, 0, "the angle is kept");
            Assert.Close(2.0, v["angleToSumDeg_a"], 1e-3, 0, "equal colours: the sum bisects them");
            Assert.Close(-2.0, v["angleToSumDeg_b"], 1e-3, 0, "the other side");
        }

        [Test(Category = "physics", Reference = "sinc^2(dk L / 2) in the crossed geometry", Tolerance = "1 %")]
        public void NoncollinearShg_DetuningFollowsSincSquared()
        {
            double eta0 = double.NaN;
            foreach (double det in new[] { 0.0, 0.1, 0.2 })
            {
                var v = Go(Sys(Crossing(5e-3, 1e-3, 3e-12, "noncollinearDeg", 4.0).Set("detuneDeg", det)), Spec(Gauss(Lam, 3e-12, 1e-9))).Records[0].Values;
                if (det == 0) { eta0 = v["energy_sum_J"]; continue; }
                double a = v["deltaK0PerM"] * 1e-3 / 2;
                Assert.Greater(Math.Abs(a), 0.3, "detuning is significant");
                Assert.Close(Math.Pow(Math.Sin(a) / a, 2), v["energy_sum_J"] / eta0, 0.01, 0, "sinc^2 at " + det);
            }
        }

        [Test(Category = "physics", Reference = "Gaussians crossing at mid-crystal: effective length l sqrt(pi) erf(L / 2 l), l^2 = (wa^2 + wb^2) / slope^2", Tolerance = "2 %")]
        public void CrossingBeams_InteractOverTheirOverlapOnly()
        {
            double w = 100e-6, L = 5e-3;
            double Sum(double deg) => Go(Sys(Crossing(w, L, 5e-12, "noncollinearDeg", deg)), Spec(Gauss(Lam, 5e-12, 1e-9))).Records[0].Values["energy_sum_J"];
            double LEff(double deg) { double slope = 2 * Math.Tan(deg / 2 * Math.PI / 180), l = Math.Sqrt(2) * w / slope; return l * Math.Sqrt(Math.PI) * Erf(L / (2 * l)); }
            Assert.Close(Math.Pow(LEff(4) / LEff(2), 2), Sum(4) / Sum(2), 0.02, 0, "conversion follows the overlap");
        }

        private static double Erf(double x)
        {
            double s = x, t = x; for (int k = 1; k < 80; k++) { t *= -x * x / k; s += t / (2 * k + 1); }
            return 2 / Math.Sqrt(Math.PI) * s;
        }

        private static SystemDescription CrossedAutocorrelator(double delayS, JsonObject transverse, double w)
        {
            var x = O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 20e-6, "chi3", false, "cut", "auto", "interaction", O("process", "sfg", "type", "I", "externalAngleDeg", 3.0),
                "beam", O("kind", "gaussian", "w0M", w), "output", "sum", "next", "end", "aux", A(O("role", "b", "fromPort", true, "beam", O("kind", "gaussian", "radiusM", w))));
            if (transverse != null) x.Set("transverse", transverse);
            var s = Sys(O("id", "bs", "type", "beamSplitter", "reflectivity", 0.5, "next", O("c", "f", "d", "stage")), O("id", "f", "type", "delay", "delayS", 0.0, "next", "x"),
                O("id", "stage", "type", "delay", "delayS", delayS, "next", "x:b"), x);
            s.Topology = SystemGraph.Branched;
            return s;
        }

        [Test(Category = "physics", Reference = "autocorrelation with geometric smearing: FWHM^2 = 2 tau^2 + (w sqrt(ln 2) 2 sin(phi / 2) / c)^2", Tolerance = "1 %")]
        public void CrossedAutocorrelator_OnACartesianGrid_HasTheGeometricSmearing()
        {
            const double tau = 60e-15, w = 200e-6, d = 60e-15;
            var run = Spec(Gauss(Lam, tau, 1e-9), O("grid", O("n", 512, "windowS", 2.56e-12, "auto", false)));
            var grid = O("model", "cartesian", "nx", 64, "ny", 1, "halfWidthM", 800e-6, "spectralSamples", 4); // ny = 1 gives the shape of the trace, not its energy
            double E(double delay) => Go(CrossedAutocorrelator(delay, grid, w), run).Output.TotalEnergyJ();
            double smear = w * Math.Sqrt(Math.Log(2)) * 2 * Math.Sin(1.5 * Math.PI / 180) / C;
            double ratio = Math.Sqrt(E(d) * E(-d)) / E(0);
            Assert.Close(Math.Sqrt(2 * tau * tau + smear * smear), d * Math.Sqrt(-4 * Math.Log(2) / Math.Log(ratio)), 0.01, 0, "FWHM");
        }

        [Test(Reference = "cross-check: 1-D coupling against a 2-D Cartesian grid", Tolerance = "1 %")]
        public void CrossedBeams_OneDimensionAndCartesianGiveTheSameEnergy()
        {
            var run = Spec(Gauss(Lam, 300e-15, 2e-9));
            double a = Go(CrossedAutocorrelator(0, null, 200e-6), run).Output.TotalEnergyJ();
            double b = Go(CrossedAutocorrelator(0, O("model", "cartesian", "nx", 64, "ny", 64, "halfWidthM", 800e-6, "spectralSamples", 2), 200e-6), run).Output.TotalEnergyJ();
            Assert.Close(a, b, 0.01, 0, "sum energy");
        }

        private static SystemDescription Mixer(double tiltRad, JsonObject interaction = null)
        {
            var thg = Thg(10e-6); if (interaction != null) thg.Set("interaction", interaction);
            var s = Sys(Shg(10e-6, O("out", "stage", "fundamental", "thg")), O("id", "stage", "type", "delay", "delayS", 0.0, "tiltXRad", tiltRad, "next", "thg:b"), thg);
            s.Topology = SystemGraph.Branched;
            return s;
        }

        [Test(Category = "physics", Reference = "tangential momentum and Snell: w3 sin t3 = w1 sin t1 + w2 sin t2", Tolerance = "1e-3")]
        public void CrossedThg_RecordsTheOutputAngles()
        {
            double tilt = 3 * Math.PI / 180;
            var v = Go(Mixer(tilt), Spec(Gauss(Lam, 100e-15, 1e-6))).Records.First(x => x.Id == "thg").Values;
            Assert.Close(3.0, v["externalAngleDeg"], 1e-6, 0, "the arms set the angle");
            Assert.Close(0.0, v["outputTiltDeg_a"], 0, 1e-9, "the fundamental keeps its direction");
            Assert.Close(3.0, v["outputTiltDeg_b"], 1e-9, 0, "the second harmonic keeps its direction");
            // the carriers are the grid's: their ratio is 2 to the grid spacing, here 1e-3
            Assert.Close(Math.Asin(2 * Math.Sin(tilt) / 3) * 180 / Math.PI, v["outputTiltDeg_sum"], 1e-3, 0, "the third harmonic leaves between them");
        }

        [Test(Reference = "the arms and the interaction state the same geometry", Tolerance = "1e-9")]
        public void CrossedThg_AngleFromTheArms_EqualsTheStatedAngle()
        {
            var run = Spec(Gauss(Lam, 100e-15, 1e-6));
            var stated = O("process", "sfg", "type", "I", "externalAngleDeg", -3.0);
            Assert.Close(Go(Mixer(0, stated), run).Output.TotalEnergyJ(), Go(Mixer(3 * Math.PI / 180), run).Output.TotalEnergyJ(), 1e-9, 0, "third harmonic");
        }

        [Test(Reference = "a crossing angle changes the cut, or the beam would not phase match", Tolerance = "exact")]
        public void CrossedThg_NeedsADifferentCutThanCollinear()
        {
            var run = Spec(Gauss(Lam, 100e-15, 1e-6));
            var collinear = Go(Mixer(0), run).Records.First(x => x.Id == "thg").Values;
            var crossed = Go(Mixer(5 * Math.PI / 180), run).Records.First(x => x.Id == "thg").Values;
            Assert.Greater(Math.Abs(crossed["thetaDeg"] - collinear["thetaDeg"]), 0.01, "the cut moves");
            Assert.Less(Math.Abs(crossed["deltaK0PerM"]) * 10e-6, 0.3, "and still phase matches");
        }

        [Test(Reference = "one beam cannot cross itself", Tolerance = "exact")]
        public void NoncollinearShg_OfOneBeam_IsRefusedWithAPointer()
        {
            var x = O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 10e-6, "cut", "auto", "interaction", O("process", "shg", "type", "I", "noncollinearDeg", 4.0), "beam", Beam(), "output", "sh");
            Assert.Throws<Exception>(() => Go(Sys(x), Spec(Gauss(Lam, 100e-15, 1e-9))), "sfg");
        }

        [Test(Reference = "a y tilt is not in the crystal's plane: it must say so", Tolerance = "exact")]
        public void YTiltOnAnArm_Warns()
        {
            var s = Chain(); s.Elements.First(e => e.Id == "stage").Json.Set("tiltYRad", 0.01);
            Assert.True(Go(s, Spec(Gauss(Lam, 100e-15, 1e-9))).Warnings.Any(w => w.Contains("y tilts")), "warned");
        }
    }
}
