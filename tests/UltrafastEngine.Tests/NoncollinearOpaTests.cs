// NoncollinearOpaTests.cs -- non-collinear OPA and DFG, with the angle stated or taken from the arms' tilts.
// A NOPA in a Branched system: the pump arrives on a tilted arm, the seed from a virtual input on the crystal's signal port.
// Checks: the lab tilts of signal, idler and pump conserve transverse momentum; the plane-wave parametric gain with a
// non-collinear mismatch (Cerullo & De Silvestri 2003); the arms, externalAngleDeg and noncollinearDeg give the same cut;
// Manley-Rowe in DFG; Sequential and Branched forms of one crystal agree.

using System;
using System.Linq;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Non-collinear OPA and DFG: angle from the arms, output tilts, parametric gain")]
    public class NoncollinearOpaTests
    {
        private const double C = 299792458.0, Eps0 = 8.8541878128e-12, Hbar = 1.054571817e-34;

        /// <summary>A Branched NOPA: the run's pulse is the 515 nm pump, on an arm tilted by <paramref name="tiltRad"/>; the 800 nm seed is a
        /// virtual input on the crystal's signal port, along the lab axis.</summary>
        private static SystemDescription Nopa(double tiltRad, JsonObject interaction = null, string process = "opa", string output = "signal", double lengthM = 1e-3)
        {
            var x = O("id", "nopa", "type", "crystal", "crystal", "BBO", "lengthM", lengthM, "cut", "auto", "chi3", false,
                "interaction", interaction ?? O("process", process, "type", "I"), "mainRole", "pump", "output", output,
                "beam", O("kind", "gaussian", "w0M", 300e-6),
                "aux", A(O("role", "signal", "fromPort", true, "beam", O("kind", "gaussian", "radiusM", 300e-6))),
                "inputs", O("signal", O("pulse", Gauss(800e-9, 1e-12, 1e-9), "delayS", 0.0)));
            string other = output == "signal" ? "idler" : "signal";
            x.Set("next", O("out", "end", other, "virtual", "pump", "virtual"));
            var s = Sys(O("id", "arm", "type", "delay", "delayS", 0.0, "tiltXRad", tiltRad, "next", "nopa"), x);
            s.Topology = SystemGraph.Branched;
            return s;
        }

        private static Specs.RunSpec PumpRun() => Spec(Gauss(515e-9, 1e-12, 50e-6));

        [Test(Category = "physics", Reference = "transverse momentum at the exit face: w_p sin t_p = w_s sin t_s + w_i sin t_i", Tolerance = "1e-6")]
        public void Nopa_OutputTilts_ConserveTransverseMomentum()
        {
            double tilt = 4 * Math.PI / 180;
            var v = Go(Nopa(tilt), PumpRun()).Records.First(r => r.Id == "nopa").Values;
            double D(string k) => v[k] * Math.PI / 180;
            double ws = 2 * Math.PI * C / v["wavelengthM_signal"], wi = 2 * Math.PI * C / v["wavelengthM_idler"], wp = 2 * Math.PI * C / v["wavelengthM_pump"];
            Assert.Close(wp, ws + wi, 1e-12, 0, "carriers");
            Assert.Close(0.0, v["outputTiltDeg_signal"], 0, 1e-12, "the seed keeps the lab axis");
            Assert.Close(4.0, v["outputTiltDeg_pump"], 1e-9, 0, "the pump keeps its arm's direction");
            Assert.Close(4.0, v["externalAngleDeg"], 1e-9, 0, "the arms set the angle");
            // the idler's tilt is Snell's of its internal direction; momentum balance is the check
            Assert.Close(wp * Math.Sin(D("outputTiltDeg_pump")), ws * Math.Sin(D("outputTiltDeg_signal")) + wi * Math.Sin(D("outputTiltDeg_idler")), 1e-6, 0, "transverse momentum");
            Assert.Greater(v["outputTiltDeg_idler"], v["outputTiltDeg_pump"], "the idler leaves on the pump's side, beyond it");
            Assert.Greater(v["angleToSignalDeg_idler"], v["angleToSignalDeg_pump"], "and inside, too");
        }

        // ------------------------------------------------------------------------------------------ plane-wave gain

        private const double NIdeal = 1.7, NoIdeal = 1.8, DIdeal = 2e-12;

        /// <summary>A dispersion-free uniaxial crystal (Chi2Tests): type 0 eee, phase matched collinearly at every wavelength, so a
        /// non-collinear angle gives a known mismatch.</summary>
        private static JsonObject Ideal() => O("name", "ideal", "no", O("kind", "constant", "value", NoIdeal), "ne", O("kind", "constant", "value", NIdeal),
            "deffPmPerV", DIdeal * 1e12, "n2", 0.0);

        private static double PeakPower(double e, double fwhm) => e * Math.Sqrt(4 * Math.Log(2) / Math.PI) / fwhm;

        private static double PulseAverage(Func<double, double> f, double p0, double fwhm)
        {
            double s = 0, sw = 0; int n = 20001; double t0 = 3 * fwhm, dt = 2 * t0 / (n - 1);
            for (int i = 0; i < n; i++) { double t = -t0 + i * dt, p = p0 * Math.Exp(-4 * Math.Log(2) * t * t / (fwhm * fwhm)); s += p * f(p); sw += p; }
            return s / sw;
        }

        /// <summary>Extraordinary index of the ideal crystal at polar angle theta.</summary>
        private static double Ne(double theta) => 1 / Math.Sqrt(Math.Pow(Math.Cos(theta) / NoIdeal, 2) + Math.Pow(Math.Sin(theta) / NIdeal, 2));

        [Test(Category = "physics", Reference = "Cerullo & De Silvestri, Rev. Sci. Instrum. 74, 1 (2003): G = 1 + (Gamma/g)^2 sinh^2(g L), g^2 = Gamma^2 - (dk/2)^2", Tolerance = "2 %")]
        public void Nopa_PlaneWaveGain_FollowsTheMismatchedParametricGain()
        {
            double w = 100e-6, L = 5e-3, ep = 1e-6, es = 1e-15, fwhm = 1e-12, alphaDeg = 0.4;
            var el = O("id", "opa", "type", "crystal", "crystal", Ideal(), "lengthM", L, "cut", O("thetaDeg", 90, "phiDeg", 0),
                "interaction", O("process", "opa", "type", "0", "noncollinearDeg", alphaDeg), "overlap", O("model", "planeWave", "radiusM", w), "chi3", false,
                "aux", A(O("role", "pump", "pulse", Gauss(1030e-9, fwhm, ep), "beam", O("kind", "gaussian", "radiusM", w), "delayS", 0.0)), "output", "signal");
            var v = Go(Sys(el), Spec(Gauss(1550e-9, fwhm, es), O("physics", O("selfSteepening", false)))).Records[0].Values;
            // the mismatch of the crossed geometry: k_s + sqrt(k_i^2 - (k_p sin a)^2) - k_p cos a, with the e index of each direction
            double ws = 2 * Math.PI * C / 1550e-9, wp = 2 * Math.PI * C / 1030e-9, wi = wp - ws, a = alphaDeg * Math.PI / 180, th = Math.PI / 2;
            double kp = Ne(th + a) * wp / C, ks = Ne(th) * ws / C, b = 0, ki = 0;
            for (int it = 0; it < 20; it++) { ki = Ne(th + b) * wi / C; b = Math.Asin(kp * Math.Sin(a) / ki); }
            double dk = ks + Math.Sqrt(ki * ki - Math.Pow(kp * Math.Sin(a), 2)) - kp * Math.Cos(a);
            Assert.Greater(Math.Abs(dk) * L, 1.5, "the mismatch matters");
            Assert.Close(dk, v["deltaK0PerM"], 2e-3, 0, "mismatch (carriers snapped to the grid)");
            double gamma = DIdeal * Math.Sqrt(2 / (Eps0 * Math.Pow(C, 3) * Math.Pow(NIdeal, 3))) * Math.Sqrt(2 / (Math.PI * w * w));
            double G(double p)
            {
                double G2 = gamma * gamma * ws * wi * p, g2 = G2 - dk * dk / 4;
                if (g2 > 0) return 1 + G2 / g2 * Math.Pow(Math.Sinh(Math.Sqrt(g2) * L), 2);
                if (g2 < 0) return 1 + G2 / -g2 * Math.Pow(Math.Sin(Math.Sqrt(-g2) * L), 2);
                return 1 + G2 * L * L;
            }
            double expected = PulseAverage(G, PeakPower(ep, fwhm), fwhm);
            double collinear = PulseAverage(p => Math.Pow(Math.Cosh(gamma * Math.Sqrt(ws * wi * p) * L), 2), PeakPower(ep, fwhm), fwhm);
            Assert.Less(expected, 0.8 * collinear, "the mismatch lowers the gain");
            Assert.Close(expected, v["energy_signal_J"] / es, 0.02, 0, "signal gain");
        }

        // ------------------------------------------------------------------------------------------ geometry

        [Test(Reference = "the arms, externalAngleDeg and noncollinearDeg state the same geometry", Tolerance = "1e-6")]
        public void Nopa_CutFromTheArms_EqualsTheStatedAngle()
        {
            var run = PumpRun();
            var arms = Go(Nopa(4 * Math.PI / 180), run).Records.First(r => r.Id == "nopa").Values;
            var ext = Go(Nopa(0, O("process", "opa", "type", "I", "externalAngleDeg", 4.0)), run).Records.First(r => r.Id == "nopa").Values;
            var inside = Go(Nopa(0, O("process", "opa", "type", "I", "noncollinearDeg", arms["noncollinearDeg"])), run).Records.First(r => r.Id == "nopa").Values;
            Assert.Close(4.0 / 1.67, arms["noncollinearDeg"], 0.02, 0, "refracted into the crystal (n_p near 1.67)");
            foreach (var other in new[] { ext, inside })
            {
                Assert.Close(arms["thetaDeg"], other["thetaDeg"], 1e-6, 0, "cut");
                Assert.Close(arms["noncollinearDeg"], other["noncollinearDeg"], 1e-6, 0, "internal angle");
                // an external angle refracts at the pump's grid carrier, a stated internal one does not: 0.01 / m, 1e-5 rad over the crystal
                Assert.Close(arms["deltaK0PerM"], other["deltaK0PerM"], 0, 0.02, "mismatch");
                Assert.Close(arms["energy_signal_J"], other["energy_signal_J"], 1e-6, 0, "amplified signal");
            }
            Assert.Less(Math.Abs(arms["deltaK0PerM"]) * 1e-3, 0.3, "phase matched");
            Assert.Greater(arms["energy_signal_J"], 2e-9, "the seed is amplified");
        }

        [Test(Reference = "the idler must leave with a signal and a pump to set the direction", Tolerance = "exact")]
        public void AngleFromTheArms_WithoutASignalInput_IsRefused()
        {
            var s = Nopa(0.05);
            var x = s.Elements.First(e => e.Id == "nopa").Json;
            x.Set("aux", A(O("role", "idler", "fromPort", true, "beam", O("kind", "gaussian", "radiusM", 300e-6))));
            x.Set("inputs", O("idler", O("pulse", Gauss(1500e-9, 1e-12, 1e-9), "delayS", 0.0)));
            x.Set("next", O("out", "end", "idler", "virtual", "pump", "virtual"));
            Assert.Throws<Exception>(() => Go(s, PumpRun()), "signal");
        }

        [Test(Category = "physics", Reference = "Manley-Rowe: one signal and one idler photon per pump photon", Tolerance = "1e-4")]
        public void NoncollinearDfg_ConservesPhotons()
        {
            var r = Go(Nopa(4 * Math.PI / 180, process: "dfg", output: "idler", lengthM: 2e-3), Spec(Gauss(515e-9, 1e-12, 200e-6)));
            var v = r.Records.First(x => x.Id == "nopa").Values;
            Assert.Greater(v["energy_idler_J"], 1e-8, "the idler is made");
            Assert.Less(Math.Abs(v["manleyRoweResidual"]), 1e-4, "signal and idler gain photons at the same rate");
            // pump photons lost = idler photons made (narrowband pulses: one photon energy each)
            double hp = Hbar * 2 * Math.PI * C / v["wavelengthM_pump"], hi = Hbar * 2 * Math.PI * C / v["wavelengthM_idler"];
            Assert.Close((200e-6 - v["energy_pump_J"]) / hp, v["energy_idler_J"] / hi, 1e-3, 0, "pump photons");
        }

        [Test(Reference = "one crystal as a Sequential and as a Branched system", Tolerance = "1e-9")]
        public void SequentialAndBranched_GiveTheSameNopa()
        {
            JsonObject Crystal() => O("id", "nopa", "type", "crystal", "crystal", "BBO", "lengthM", 1e-3, "cut", "auto", "chi3", false,
                "interaction", O("process", "opa", "type", "I", "externalAngleDeg", 4.0), "mainRole", "pump", "output", "signal",
                "beam", O("kind", "gaussian", "w0M", 300e-6), "aux", A(O("role", "signal", "pulse", Gauss(800e-9, 1e-12, 1e-9), "delayS", 0.0, "beam", O("kind", "gaussian", "radiusM", 300e-6))));
            var seq = Go(Sys(Crystal()), PumpRun());
            var b = Sys(Crystal().Set("next", O("out", "end", "idler", "virtual", "pump", "virtual"))); b.Topology = SystemGraph.Branched;
            var br = Go(b, PumpRun());
            Assert.Close(seq.Output.TotalEnergyJ(), br.Output.TotalEnergyJ(), 1e-9, 0, "signal");
            Assert.Close(seq.Records[0].Values["outputTiltDeg_idler"], br.Records[0].Values["outputTiltDeg_idler"], 1e-9, 0, "idler direction");
        }
    }
}
