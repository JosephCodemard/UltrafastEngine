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

        [Test(Reference = "a crystal treats its beams as collinear: a tilted arm must say so", Tolerance = "exact")]
        public void TiltedArmIntoACrystal_Warns()
        {
            var s = Chain();
            s.Elements.First(e => e.Id == "stage").Json.Set("tiltXRad", 0.01);
            var r = Go(s, Spec(Gauss(Lam, 100e-15, 1e-9)));
            Assert.True(r.Warnings.Any(w => w.Contains("collinear")), "warned");
        }

        [Test(Reference = "only opa and dfg have a non-collinear geometry", Tolerance = "exact")]
        public void NoncollinearSfg_IsRefusedNotIgnored()
        {
            var t = Thg(200e-6); t["interaction"].AsObject().Set("noncollinearDeg", 2.0);
            var s = Sys(Shg(200e-6, O("out", "stage", "fundamental", "thg")), O("id", "stage", "type", "delay", "delayS", 0.0, "next", "thg:b"), t);
            s.Topology = SystemGraph.Branched;
            Assert.Throws<Exception>(() => Go(s, Spec(Gauss(Lam, 100e-15, 1e-9))), "noncollinearDeg");
        }
    }
}
