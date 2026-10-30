using System;
using System.Linq;
using UltrafastEngine.Materials;
using UltrafastEngine.Physics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Stage 1.5: ionisation rates (PPT, ADK, Keldysh) and the plasma response.</summary>
    [TestArea("Photo-ionisation and plasma")]
    public class IonizationTests
    {
        [Test(Reference = "tabulated Dawson and elliptic-integral values", Tolerance = "1e-12, 1e-07, 1e-06")]
        public void SpecialFunctions_DawsonAndElliptic()
        {
            Assert.Close(0.5380795069127684, IonizationMath.Dawson(1.0), 1e-7, 0, "D(1)");
            Assert.Close(0.0993359924, IonizationMath.Dawson(0.1), 1e-7, 0, "D(0.1)");
            Assert.Close(0.1021340744, IonizationMath.Dawson(5.0), 1e-6, 0, "D(5)");
            var (k, e) = IonizationMath.Elliptic(0.5);
            Assert.Close(1.8540746773013719, k, 1e-12, 0, "K(m = 0.5)");
            Assert.Close(1.3506438810476755, e, 1e-12, 0, "E(m = 0.5)");
        }

        private static double Slope(IIonizationRate r, double i) => Math.Log(r.Rate(1.1 * i) / r.Rate(i)) / Math.Log(1.1);

        [Test(Reference = "PPT rate: multiphoton I^K and tunnelling (ADK) limits", Tolerance = "3 %, 15 %")]
        public void Ppt_MultiphotonAndTunnelLimits()
        {
            double w800 = W(800e-9);
            var ar = new PptRate(15.760, w800, 1, 1);
            // multiphoton: W ~ I^K, K = 11 photons of 1.55 eV for argon
            Assert.Close(11, Slope(ar, 1e13), 0.03, 0, "multiphoton order at 1e9 W/cm^2 (gamma ~ 100)");
            Assert.Less(Slope(ar, 1e17), Slope(ar, 1e13), "the effective order falls towards the tunnel regime");
            // tunnel limit (gamma << 1, long wavelength): PPT -> cycle-averaged ADK
            double wLong = W(10e-6);
            var pLong = new PptRate(15.760, wLong, 1, 1); var adk = new AdkRate(15.760, 1, 1);
            Assert.Close(1.0, pLong.Rate(1e18) / adk.Rate(1e18), 0.15, 0, "PPT/ADK at gamma ~ 0.1");
            // order of magnitude at 1e14 W/cm^2, 800 nm
            double w = ar.Rate(1e18);
            Assert.True(w > 1e9 && w < 1e13, "argon rate at 1e14 W/cm^2: " + w.ToString("G3") + " /s");
        }

        [Test(Reference = "Keldysh: K = ceil(U_g / ħω)", Tolerance = "8 %")]
        public void Keldysh_MultiphotonOrderForFusedSilica()
        {
            var k = new KeldyshRate(9.0, 0.635, W(800e-9), 2.1e28, 1.45);
            Assert.Close(6, Slope(k, 1e15), 0.08, 0, "6-photon absorption across a 9 eV gap at 800 nm");
            Assert.Greater(k.Rate(1e17), k.Rate(1e16) * 1e3, "steep");
        }

        [Test(Reference = "ρ = ∫ W dt; Drude blue shift", Tolerance = "3 %")]
        public void Plasma_ElectronDensityBlueshiftAndAbsorption()
        {
            double lam = 800e-9, E = 1e-3, area = 1e-7, tau = 50e-15;
            var o = O("format", "ultrafast-system", "version", 2, "name", "cell", "materials", O("argon", O("gas", O("species", "Ar", "pressureBar", 1.0), "n2", 0.0)));
            o.Set("elements", A(O("id", "cell", "type", "bulk", "material", "argon", "lengthM", 1e-3, "beam", O("kind", "constant", "areaM2", area), "ionization", O("avalanche", false))));
            var run = O("format", "ultrafast-run", "version", 2, "physics", O("selfSteepening", false));
            run.Set("pulse", O("shape", "gaussian", "centerWavelengthM", lam, "durationFwhmS", tau, "energyJ", E));
            var r = Simulation.Run(SystemDescription.FromJson(o), Specs.RunSpec.FromJson(run));
            var rec = r.Records[0];
            // expected density: rho_n int W(I(t)) dt on the input pulse (low ionisation fraction)
            var ppt = new PptRate(15.760, W(lam), 1, 1);
            var pw = PulseMetrics.TemporalPower(r.Input); double dt = r.Input.Grid.DTime, s = 0;
            foreach (var p in pw) s += ppt.Rate(p / area) * dt;
            double rhoN = 1e5 / (1.380649e-23 * 293.15);
            Assert.Close(rhoN * s, rec.Values["peakElectronDensityM3"], 0.03, 0, "electron density = rho_n int W dt");
            Assert.Greater(SpectralCentroid(r.Output), SpectralCentroid(r.Input), "plasma blue-shifts the pulse");
            Assert.True(rec.LossJ.ContainsKey("absorption") && rec.LossJ["absorption"] > 0, "ionisation takes energy from the pulse");
        }
            private static RunResult SpatialCell(string equation, bool plasma, double energy)
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "fil", "materials", O("argon", O("gas", O("species", "Ar", "pressureBar", 1.0))));
            var el = O("id", "cell", "type", "bulk", "material", "argon", "lengthM", 0.02, "beam", O("kind", "gaussian", "w0M", 30e-6, "zWaistM", 0.01),
                "transverse", O("model", "radial", "points", 64, "radiusM", 0.4e-3, "equation", equation));
            if (plasma) el.Set("ionization", true);
            o.Set("elements", A(el));
            var run = O("format", "ultrafast-run", "version", 2);
            run.Set("pulse", O("shape", "gaussian", "centerWavelengthM", 800e-9, "durationFwhmS", 40e-15, "energyJ", energy));
            return Simulation.Run(SystemDescription.FromJson(o), Specs.RunSpec.FromJson(run));
        }

        [Test(Slow = true, Reference = "cross-check: the envelope model for paraxial beams", Tolerance = "1e-06, 1 %")]
        public void Uppe_AgreesWithEnvelopeForParaxialBeams_AndPlasmaClampsTheFocus()
        {
            var env = SpatialCell("envelope", false, 2e-5); var up = SpatialCell("uppe", false, 2e-5);
            Assert.Close(env.Records[0].BIntegralRad, up.Records[0].BIntegralRad, 0.01, 0, "UPPE = envelope coupling for a paraxial (w0 = 30 um) beam");
            Assert.Close(env.Output.TotalEnergyJ(), up.Output.TotalEnergyJ(), 1e-6, 0, "energy");
            var kerrOnly = SpatialCell("uppe", false, 2e-4); var withPlasma = SpatialCell("uppe", true, 2e-4);
            Assert.Greater(withPlasma.Records[0].Values["peakElectronDensityM3"], 1e21, "plasma at the focus");
            Assert.Less(withPlasma.Records[0].Values["peakFluenceOutJPerM2"], kerrOnly.Records[0].Values["peakFluenceOutJPerM2"], "plasma defocusing lowers the peak fluence");
            Assert.Less(withPlasma.Output.TotalEnergyJ(), kerrOnly.Output.TotalEnergyJ(), "ionisation losses");
        }
    }
}
