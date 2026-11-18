using System;
using UltrafastEngine.Gain;

namespace UltrafastEngine.Tests
{
    /// <summary>Internal consistency of the gain-medium catalogue: do the emission cross-section fits reproduce the catalogue's lifetime?
    /// The ASE source term of the engine is proportional to sigma_e, so a fit whose Fuchtbauer-Ladenburg integral disagrees with the lifetime
    /// gives a wrong spontaneous-emission power.</summary>
    [TestArea("Gain catalogue consistency (Fuchtbauer-Ladenburg)")]
    public class GainCatalogueConsistencyTests
    {
        private const double C = GainMedium.C, N_YAG = 1.82;

        /// <summary>The radiative rate carried by the band [loM, hiM]: 8 pi n^2 integral sigma_e(nu) nu^2 / c^2 dnu (the expression used in AseStressTests).</summary>
        private static double BandRate(GainMedium m, double loM, double hiM)
        {
            int K = 20000; double nuLo = C / hiM, nuHi = C / loM, dnu = (nuHi - nuLo) / K, s = 0;
            for (int i = 0; i < K; i++) { double nu = nuLo + (i + 0.5) * dnu; s += 8 * Math.PI * N_YAG * N_YAG * m.SigmaE(2 * Math.PI * nu) * nu * nu / (C * C) * dnu; }
            return s;
        }

        [Test(Category = "gain", Reference = "Fuchtbauer-Ladenburg: 1 / tau_rad = 8 pi n^2 integral sigma_e(nu) nu^2 / c^2 dnu (Aull and Jenssen, IEEE JQE 18, 925 (1982)); catalogue lifetime 0.95 ms (+-10 %)", Tolerance = "15 % (10 % lifetime uncertainty + the fit's own)")]
        public void YbYag_EmissionFit_ReproducesTheCatalogueLifetime()
        {
            // Yb has one excited manifold (2F5/2) and a single lower one, so the emission band integrates to the whole radiative rate
            // (the nonradiative rate and radiation trapping are small in YAG); 0.90 - 1.15 um holds every line (zero-phonon 968.8 nm).
            var m = GainMedium.Get("Yb:YAG");
            double tauFl = 1 / BandRate(m, 0.90e-6, 1.15e-6);
            Assert.Close(m.LifetimeS, tauFl, 0.15, 0, "tau from the sigma_e fit (found " + tauFl * 1e3 + " ms) against the catalogue's " + m.LifetimeS * 1e3 + " ms");
        }

        [Test(Category = "gain", Reference = "a single emission line cannot radiate faster than the whole upper level: 1 / tau_line <= 1 / tau", Tolerance = "bound")]
        public void NdYag_SingleLineFit_DoesNotExceedTheTotalDecayRate()
        {
            // The catalogue's Nd:YAG emission is one Gaussian line (1064.2 nm, 0.6 nm FWHM); 4F3/2 also decays to 4I9/2, 4I13/2 and through other
            // Stark lines, so its FL rate is a fraction of 1/tau (found: about 8 %), not equal to it. Only the bound can be checked; the fit's
            // magnitude (2.8e-19 cm^2, Koechner) is not validated by FL and the catalogue says so.
            var m = GainMedium.Get("Nd:YAG");
            double rateLine = BandRate(m, 0.90e-6, 1.45e-6);
            Assert.Less(rateLine, 1 / m.LifetimeS, "single-line radiative rate below the total decay rate");
            Assert.Greater(rateLine, 0.02 / m.LifetimeS, "and not absurdly small (a line carries at least a few percent of the decay)");
        }
    }
}
