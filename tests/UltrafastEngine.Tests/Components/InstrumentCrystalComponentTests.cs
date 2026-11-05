// InstrumentCrystalComponentTests.cs -- component-sheet rows for instruments and chi(2) crystals that the rest of the suite
// does not assert (verification/components/instruments.py and crystals.py plot the same numbers):
// sech^2 autocorrelation ratio, third-order cross-correlation width and floor, SHG-FROG frequency marginal, all at 515 nm /
// 1030 nm / 2 um; and for BBO SHG at the three carriers: energy conservation at high conversion and each port's carrier.
using System;
using System.Linq;
using UltrafastEngine.Instruments;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests.Components
{
    [TestArea("Component sheet: instruments and chi(2) crystals at 515 nm / 1030 nm / 2 um")]
    public class InstrumentCrystalComponentTests
    {
        private static InstrumentOutput Inst(RunResult r, string name) => r.Instruments.First(i => i.Name == name);
        private static double[] Arr(InstrumentOutput i, string suffix) => i.Arrays.First(a => a.Name.EndsWith(suffix)).Data;

        private static RunResult Free(JsonObject pulse, params JsonObject[] instruments) =>
            Go(Sys(O("id", "p", "type", "probe")), Spec(pulse, O("instruments", A(instruments))));

        [Test(Reference = "Intensity autocorrelation of sech^2: FWHM ratio 1.5427 (Diels & Rudolph, Table 9.1)", Tolerance = "0.3 %")]
        public void Autocorrelator_Sech2_HasTheRatio1543_AtEveryCarrier()
        {
            foreach (double lam in ComponentSheet.CarriersM)
                foreach (double tau in ComponentSheet.DurationsS)
                {
                    var p = O("shape", "sech2", "centerWavelengthM", lam, "durationFwhmS", tau, "energyJ", 1e-9);
                    var ac = Inst(Free(p, O("kind", "autocorrelator", "name", "ac")), "ac");
                    Assert.Close(1.5427, ac.Values.GetDouble("acFwhmS", 0) / tau, 3e-3, 0, "AC / FWHM at " + lam + " m, " + tau + " s");
                    Assert.Close(tau, ac.Values.GetDouble("pulseFwhmSech2S", 0), 3e-3, 0, "deconvolved FWHM");
                }
        }

        [Test(Reference = "Third-order correlation of a Gaussian: integral I(t) I^2(t - tau) dt has FWHM sqrt(3/2) tau_p; flat floor at the dynamic range", Tolerance = "0.5 %; 0.01 dB")]
        public void CrossCorrelator_Gaussian_HasWidthRootThreeHalves_AndTheFloor()
        {
            foreach (double lam in ComponentSheet.CarriersM)
                foreach (double tau in ComponentSheet.DurationsS)
                {
                    var cc = Inst(Free(Gauss(lam, tau, 1e-9), O("kind", "crossCorrelator", "name", "cc", "dynamicRangeDb", 110.0, "noise", false, "points", 801)), "cc");
                    Assert.Close(Math.Sqrt(1.5), cc.Values.GetDouble("fwhmS", 0) / tau, 5e-3, 0, "TOCC FWHM / tau at " + lam + " m, " + tau + " s");
                    Assert.Close(-110, cc.Values.GetDouble("prePulseContrastDb", 0), 0, 0.01, "pre-pulse reading at the floor");
                }
        }

        [Test(Reference = "SHG-FROG frequency marginal = autoconvolution of the spectrum: centre 2 w0, rms sqrt(2) sigma_w", Tolerance = "1e-4 centre, 1 % rms")]
        public void Frog_FrequencyMarginal_IsTheSpectrumsAutoconvolution()
        {
            foreach (double lam in ComponentSheet.CarriersM)
            {
                var r = Free(Gauss(lam, 100e-15, 1e-9), O("kind", "frog", "name", "f", "points", 256));
                var f = Inst(r, "f");
                double[] w = Arr(f, "_omega_rad_s"), m = Arr(f, "_frequency_marginal");
                var inW = r.Compiled.Input.Grid.OmegaAbsolute; var inS = r.Compiled.Input.SpectralIntensity();
                var mi = PulseMetrics.Moments(inW, inS); var mf = PulseMetrics.Moments(w, m);
                Assert.Close(2 * mi.Mean, mf.Mean, 1e-4, 0, "centre at " + lam + " m");
                Assert.Close(Math.Sqrt(2) * mi.Rms, mf.Rms, 1e-2, 0, "rms width at " + lam + " m");
            }
        }

        private static JsonObject Bbo(double L) => O("id", "c", "type", "crystal", "crystal", "BBO", "lengthM", L, "cut", "auto",
            "interaction", O("process", "shg"), "beam", O("kind", "constant", "areaM2", 1e-6), "overlap", "planeWave", "chi3", false);

        [Test(Reference = "Manley-Rowe / energy conservation of lossless SHG; out port = SH at lambda / 2, fundamental at lambda", Tolerance = "1e-6 energy, 0.2 % carrier")]
        public void BboShg_ConservesEnergy_AndEachPortHasItsCarrier_AtEveryCarrier()
        {
            foreach (double lam in ComponentSheet.CarriersM)
            {
                var r = Go(Sys(Bbo(2e-3)), Spec(Gauss(lam, 5e-12, 1e-4)));
                var v = r.Records[0].Values;
                Assert.Greater(v["conversionEfficiency"], 0.2, "high conversion at " + lam + " m");
                Assert.Close(r.Records[0].EnergyInJ, v["energy_fundamental_J"] + v["energy_sh_J"], 1e-6, 0, "fundamental + SH = input at " + lam + " m");

                var c = Bbo(1e-3); c.Set("next", O("out", "pout", "fundamental", "pf"));
                var s = Sys(c, O("id", "pout", "type", "probe", "next", "end"), O("id", "pf", "type", "probe", "next", "virtual"));
                s.Topology = SystemGraph.Branched;
                var rb = Go(s, Spec(Gauss(lam, 5e-12, 1e-6)));
                double sh = 2 * Math.PI * C / SpectralCentroid(rb.At["pout"]), fu = 2 * Math.PI * C / SpectralCentroid(rb.At["pf"]);
                Assert.Close(lam / 2, sh, 2e-3, 0, "port out carries the SH at " + lam + " m");
                Assert.Close(lam, fu, 2e-3, 0, "port fundamental carries the fundamental at " + lam + " m");
            }
        }
    }
}
