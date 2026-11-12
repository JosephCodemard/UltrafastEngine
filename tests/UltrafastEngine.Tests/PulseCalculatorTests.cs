using System;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Pulse calculator (time-bandwidth product, transform limit, chirp) against closed-form results.</summary>
    [TestArea("Pulse calculator: time-bandwidth product, transform limit, chirp")]
    public class PulseCalculatorTests
    {
        [Test(Reference = "Diels & Rudolph, Ultrashort Laser Pulse Phenomena, table 2.1: 2 ln2 / pi = 0.4413, sech^2 0.3148", Tolerance = "1e-4")]
        public void Tbp_OfGaussianAndSech2_MatchesTheTextbookValues()
        {
            Assert.Close(0.441271, PulseCalculator.TbpOf("gaussian"), 1e-5, 0, "Gaussian");
            Assert.Close(0.314834, PulseCalculator.TbpOf("sech2"), 1e-5, 0, "sech2");
            Assert.Close(0.8859, PulseCalculator.TbpOf("rectangular"), 1e-4, 0, "rectangular");
        }

        [Test(Reference = "dnu = c dlambda / lambda^2", Tolerance = "1e-9")]
        public void Limit_FromWavelengthBandwidth_IsInverseOfDurationLimit()
        {
            var r = PulseCalculator.Solve(O("op", "limit", "shape", "gaussian", "bandwidthFwhmM", 40e-9, "centerWavelengthM", 1030e-9));
            double dnu = 299792458.0 * 40e-9 / (1030e-9 * 1030e-9);
            Assert.Close(dnu, r.GetDouble("bandwidthFwhmHz", 0), 1e-9, 0, "dnu");
            Assert.Close(0.441271 / dnu, r.GetDouble("durationFwhmS", 0), 1e-5, 0, "tau");
        }

        [Test(Reference = "Gaussian chirp: tau = tau0 sqrt(1 + (4 ln2 GDD / tau0^2)^2)", Tolerance = "1e-6")]
        public void Analyze_ChirpedGaussian_RecoversTheGdd()
        {
            double tau0 = 50e-15, gdd = 2000e-30, tau = tau0 * Math.Sqrt(1 + Math.Pow(4 * Math.Log(2) * gdd / (tau0 * tau0), 2));
            double dnu = PulseCalculator.TbpOf("gaussian") / tau0;
            var r = PulseCalculator.Solve(O("op", "analyze", "durationFwhmS", tau, "bandwidthFwhmHz", dnu));
            Assert.Close(2000.0, r.GetDouble("gddMagnitudeFs2", 0), 1e-6, 0, "GDD fs^2");
            Assert.Close(tau / tau0, r.GetDouble("stretchFactor", 0), 1e-9, 0, "stretch");
        }

        [Test(Reference = "Gaussian: sigma_t sigma_nu = 1/(4 pi) and FWHM product 2 ln2 / pi", Tolerance = "1 %")]
        public void Measure_SampledGaussianPulse_GivesTheTransformLimit()
        {
            int n = 4001; double tau = 100e-15, lam0 = 800e-9, c = 299792458.0;
            double dnu = PulseCalculator.TbpOf("gaussian") / tau;
            var t = new double[n]; var it = new double[n]; var lam = new double[n]; var sp = new double[n];
            double sT = tau / (2 * Math.Sqrt(2 * Math.Log(2))), sF = dnu / (2 * Math.Sqrt(2 * Math.Log(2))), nu0 = c / lam0;
            for (int i = 0; i < n; i++)
            {
                t[i] = (i - n / 2) * 5e-15 / 4; it[i] = Math.Exp(-t[i] * t[i] / (2 * sT * sT));
                double nu = nu0 + (i - n / 2) * sF * 0.004; lam[i] = c / nu;   // decreasing wavelength with i... S(lambda) = S(nu) c / lambda^2
                sp[i] = Math.Exp(-Math.Pow(nu - nu0, 2) / (2 * sF * sF)) * c / (lam[i] * lam[i]);
            }
            var r = PulseCalculator.Solve(O("op", "measure", "timeS", t, "intensity", it, "wavelengthM", lam, "spectrum", sp));
            Assert.Close(1.0, r.GetDouble("ratioToLimit", 0), 0.01, 0, "ratio");
            Assert.Close(1 / (4 * Math.PI), r.GetDouble("timeBandwidthProductRms", 0), 0.01, 0, "rms product");
        }

        [Test]
        public void Limit_WithBothDurationAndBandwidth_IsRejected()
        {
            Assert.Throws<JsonException>(() => PulseCalculator.Solve(O("op", "limit", "durationFwhmS", 1e-13, "bandwidthFwhmHz", 1e12)));
        }
    }
}
