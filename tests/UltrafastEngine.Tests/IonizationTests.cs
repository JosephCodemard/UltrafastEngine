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
        [Test(Reference = "Couairon & Mysyrowicz, Phys. Rep. 441, 47 (2007), eq. 6 and 15: the avalanche is fed by inverse-bremsstrahlung absorption, which the Drude imaginary part already is", Tolerance = "1e-4")]
        public void Plasma_Avalanche_DoesNotAbsorbTheFieldASecondTime()
        {
            // A weak pulse (about 5e11 W/m2) in a prepared plasma that absorbs 33 % of the amplitude: the avalanche cannot change the density
            // over the pulse, so the output energy must be the same with it on or off. Counting the energy twice would double the absorption.
            var off = PreparedPlasma(1e24, 0.01, 10e-15, false, avalanche: false);
            var on = PreparedPlasma(1e24, 0.01, 10e-15, false, avalanche: true);
            double eOff = off.Output.TotalEnergyJ(), eOn = on.Output.TotalEnergyJ();
            Assert.Less(eOff / off.Input.TotalEnergyJ(), 0.9, "the plasma absorbs (the test can tell a double count)");
            Assert.Close(eOff, eOn, 1e-4, 0, "output energy with and without the avalanche");
        }

        // ----------------------------------------------------------------------------------------------- Drude dispersion

        private const double E0 = 1.602176634e-19, Me = 9.1093837015e-31, Eps0 = 8.8541878128e-12, Cc = 2.99792458e8;

        /// <summary>A prepared plasma of fixed density in a dispersionless n = 1.45 solid: no ionisation (sigmaK = 0), no Kerr.</summary>
        private static RunResult PreparedPlasma(double rho, double lenM, double fwhmS, bool carrierResolved, bool freqResolved = true, double tauC = 20e-15, double lam = 800e-9, bool avalanche = false)
        {
            var mat = FlatMaterial(0.0, 1.45);
            var ion = O("model", "mpi", "sigmaK", 0.0, "photons", 6, "bandGapEv", 9.0, "neutralDensityM3", 2e28, "electronDensityM3", rho, "collisionTimeS", tauC, "recombinationTimeS", 1e30, "avalanche", avalanche, "frequencyResolved", freqResolved);
            var sys = Sys(O("id", "cell", "type", "bulk", "material", "m", "lengthM", lenM, "beam", O("kind", "constant", "areaM2", 1e-7), "ionization", ion));
            var o = O("format", "ultrafast-system", "version", 2, "name", "t", "materials", O("m", mat));
            o.Set("elements", sys.ToJson()["elements"]);
            var extra = carrierResolved ? O("physics", O("selfSteepening", false, "fieldMode", "carrier")) : O("physics", O("selfSteepening", false));
            extra.Set("stepper", O("tolerance", 1e-11));
            return Go(SystemDescription.FromJson(o), Spec(Gauss(lam, fwhmS, 1e-9), extra));
        }

        private static RunResult IonisingGas(bool carrier, bool freqResolved, double lenM)
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "cell", "materials", O("argon", O("gas", O("species", "Ar", "pressureBar", 1.0), "n2", 0.0)));
            o.Set("elements", A(O("id", "cell", "type", "bulk", "material", "argon", "lengthM", lenM, "beam", O("kind", "constant", "areaM2", 1e-7),
                "ionization", O("model", "mpi", "photons", 4, "sigmaK", 1e11 / Math.Pow(1e18, 4), "avalanche", false, "frequencyResolved", freqResolved, "recombinationTimeS", 1e30))));
            var run = O("format", "ultrafast-run", "version", 2, "physics", carrier ? O("selfSteepening", false, "fieldMode", "carrier") : O("selfSteepening", false));
            run.Set("pulse", O("shape", "gaussian", "centerWavelengthM", 2e-6, "durationFwhmS", 10e-15, "energyJ", 1.1e-3));
            return Simulation.Run(SystemDescription.FromJson(o), Specs.RunSpec.FromJson(run));
        }

        [Test(Reference = "drift energy of electrons born at rest in a linearly polarised field: U_p = e² E0² / (4 m ω²) per electron (Corkum, Phys. Rev. Lett. 71, 1994 (1993))", Tolerance = "2 %")]
        public void Plasma_FrequencyResolved_TakesTheDriftEnergyOfTheElectronsBornInTheField()
        {
            // The electron current obeys dJ/dt = (e²/m) ρ(t) E, so electrons born in the pulse carry kinetic energy; after a long pulse the
            // part that stays is the drift energy, U_p of the field at birth (cycle-averaged over the birth phase). The carrier-frequency form
            // Δn(t) E(t) is a pure phase modulation and misses it. A 150 fs, 800 nm pulse (75 cycles) in argon with a 4-photon rate, in a 20 µm cell
            // (weak plasma: the first-order result holds to 0.1 %): the extra energy loss of the frequency-resolved run over the carrier-value run is
            // ∫ ρ̇(t) U_p(t) dt x area x length. The exact first-order energy change of the input pulse under the operator agrees with U_p to 0.3 %.
            double L = 2e-5, area = 1e-7, lam = 800e-9, w0 = 2 * Math.PI * Cc / lam, sig = 1e11 / Math.Pow(1e18, 4), N = 1e5 / (1.380649e-23 * 293.15);
            RunResult Run(bool fr)
            {
                var o = O("format", "ultrafast-system", "version", 2, "name", "cell", "materials", O("argon", O("gas", O("species", "Ar", "pressureBar", 1.0), "n2", 0.0)));
                o.Set("elements", A(O("id", "cell", "type", "bulk", "material", "argon", "lengthM", L, "beam", O("kind", "constant", "areaM2", area),
                    "ionization", O("model", "mpi", "photons", 4, "sigmaK", sig, "avalanche", false, "frequencyResolved", fr, "recombinationTimeS", 1e30, "collisionTimeS", 1e-3))));
                var run = O("format", "ultrafast-run", "version", 2, "physics", O("selfSteepening", false), "stepper", O("tolerance", 1e-10));
                run.Set("pulse", O("shape", "gaussian", "centerWavelengthM", lam, "durationFwhmS", 150e-15, "energyJ", 1.596e-2));
                return Simulation.Run(SystemDescription.FromJson(o), Specs.RunSpec.FromJson(run));
            }
            var a = Run(true); var b = Run(false);
            double extra = b.Output.TotalEnergyJ() - a.Output.TotalEnergyJ(), heat = 0, dt = a.Input.Grid.DTime;
            foreach (var p in PulseMetrics.TemporalPower(a.Input))
            {
                double I = p / area, rate = sig * I * I * I * I * N, up = E0 * E0 * I / (2 * Me * Eps0 * Cc * w0 * w0);
                heat += rate * up * dt;
            }
            heat *= area * L;
            Assert.Greater(heat, 1e-7, "a measurable heating energy");
            Assert.Close(heat, extra, 0.02, 0, "extra absorbed energy = integral of (birth rate x U_p)");
            // the carrier-value run takes only the ionisation energy
            double ionisation = 0; foreach (var p in PulseMetrics.TemporalPower(a.Input)) ionisation += sig * Math.Pow(p / area, 4) * N * 15.76 * E0 * dt;
            Assert.Close(ionisation * area * L, b.Input.TotalEnergyJ() - b.Output.TotalEnergyJ(), 0.03, 0, "ionisation energy U_i per electron, both runs");
        }

        private static System.Numerics.Complex DrudeFactor(double rho, double n0, double w, double nu, double lenM) =>
            System.Numerics.Complex.Exp(System.Numerics.Complex.ImaginaryOne * (w / Cc) * (-rho * E0 * E0 / (2 * n0 * Eps0 * Me) / (w * new System.Numerics.Complex(w, nu))) * lenM);

        [Test(Reference = "Drude response of free electrons: Δn(ω) = -ω_p² / (2 n0 ω (ω + iν)) (Couairon & Mysyrowicz, Phys. Rep. 441, 47 (2007))", Tolerance = "1e-8 (solver tolerance 1e-11), both field models")]
        public void Plasma_FixedDensity_FollowsTheFrequencyResolvedDrudeIndex_InEnvelopeAndCarrierMode()
        {
            // A prepared plasma of 1e24 /m^3 (no ionisation, no recombination) acts as a linear filter exp(i (ω/c) Δn(ω) L) on a 10 fs, 800 nm
            // pulse: 15.5 rad of phase and 33 % amplitude absorption at the carrier, with the 1/ω² dependence across the 0.13 ω0 bandwidth. The ratio of the
            // output spectra with and without the plasma must be that filter. Before the frequency-resolved operator the envelope used the carrier value
            // and carrier-resolved mode weighted it by ω / ω0²; frequencyResolved: false (below) is the carrier value and differs by far more than the tolerance.
            double rho = 1e24, L = 0.01, nu = 1 / 20e-15, n0 = 1.45;
            foreach (bool carrier in new[] { false, true })
            {
                var with = PreparedPlasma(rho, L, 10e-15, carrier); var without = PreparedPlasma(0, L, 10e-15, carrier);
                var w = with.Output.Grid.OmegaAbsolute; double amax = without.Output.Ax.Max(z => z.Magnitude), worst = 0, worstOld = 0; int used = 0;
                var old = PreparedPlasma(rho, L, 10e-15, carrier, false);
                for (int j = 0; j < w.Length; j++)
                {
                    if (without.Output.Ax[j].Magnitude < 0.01 * amax || w[j] <= 0) continue;
                    var f = DrudeFactor(rho, n0, w[j], nu, L);
                    worst = Math.Max(worst, (with.Output.Ax[j] / without.Output.Ax[j] - f).Magnitude);
                    worstOld = Math.Max(worstOld, (old.Output.Ax[j] / without.Output.Ax[j] - f).Magnitude);
                    used++;
                }
                Assert.Greater(used, 30, "bins above 1 % of the peak");
                Assert.Less(worst, 1e-8, (carrier ? "carrier-resolved" : "envelope") + ": output / input spectrum against the Drude filter");
                Assert.Greater(worstOld, 0.1, "frequencyResolved: false is the carrier-frequency value, which this test can tell apart");
            }
        }

        [Test(Reference = "the same Drude filter in the radial spatial model (a uniform plasma commutes with diffraction): energy transmission", Tolerance = "1e-7")]
        public void Plasma_FixedDensity_SpatialModel_AppliesTheSameDrudeFilter()
        {
            // The plasma is uniform, so it commutes with the diffraction of a wide beam: the energy with and without it differ by the
            // spectrum-weighted |filter|² of the input pulse, exactly as in the 1-D model.
            double rho = 1e24, L = 0.01, nu = 1 / 20e-15, n0 = 1.45;
            RunResult Run(double d, bool fr)
            {
                var ion = O("model", "mpi", "sigmaK", 0.0, "photons", 6, "bandGapEv", 9.0, "neutralDensityM3", 2e28, "electronDensityM3", d, "collisionTimeS", 20e-15, "recombinationTimeS", 1e30, "avalanche", false, "frequencyResolved", fr);
                var o = O("format", "ultrafast-system", "version", 2, "name", "t", "materials", O("m", FlatMaterial(0.0, 1.45)));
                o.Set("elements", A(O("id", "cell", "type", "bulk", "material", "m", "lengthM", L, "beam", O("kind", "gaussian", "w0M", 1.5e-3),
                    "transverse", O("model", "radial", "points", 32, "radiusM", 5e-3), "ionization", ion)));
                var run = O("format", "ultrafast-run", "version", 2, "physics", O("selfSteepening", false), "stepper", O("tolerance", 1e-10));
                run.Set("pulse", O("shape", "gaussian", "centerWavelengthM", 800e-9, "durationFwhmS", 10e-15, "energyJ", 1e-9));
                return Simulation.Run(SystemDescription.FromJson(o), Specs.RunSpec.FromJson(run));
            }
            var with = Run(rho, true); var without = Run(0, true); var old = Run(rho, false);
            var g = with.Input.Grid; double num = 0, den = 0;
            for (int j = 0; j < g.N; j++)
            {
                if (!(g.OmegaAbsolute[j] > 0)) continue;
                double pw = with.Input.Ax[j].Magnitude * with.Input.Ax[j].Magnitude, f = DrudeFactor(rho, n0, g.OmegaAbsolute[j], nu, L).Magnitude;
                num += pw * f * f; den += pw;
            }
            double expected = num / den, got = with.Output.TotalEnergyJ() / without.Output.TotalEnergyJ(), gotOld = old.Output.TotalEnergyJ() / without.Output.TotalEnergyJ();
            Assert.Close(expected, got, 1e-7, 0, "energy transmission of the plasma, spatial model");
            Assert.Greater(Math.Abs(gotOld / expected - 1), 1e-3, "the carrier-frequency value would differ (this test can tell them apart)");
        }

        [Test(Reference = "narrow-band limit: the frequency-resolved operator tends to the carrier value as the bandwidth goes to zero; R(ω0) = 1", Tolerance = "1e-15; difference halves with the bandwidth")]
        public void Plasma_FrequencyResolved_TendsToTheCarrierValue_ForNarrowSpectra()
        {
            var pl = new PlasmaResponse { CollisionTimeS = 20e-15 };
            double w0 = W(800e-9);
            Assert.Close(1, pl.Spectral(w0, w0).Real, 0, 1e-15, "R(ω0), real part"); Assert.Close(0, pl.Spectral(w0, w0).Imaginary, 0, 1e-15, "R(ω0), imaginary part");
            Assert.Close(0, pl.Spectral(-w0, w0).Magnitude, 0, 0, "no negative frequencies");
            // the output of the two operators differs at first order in the bandwidth: 40 fs against 80 fs
            double Diff(double fwhm)
            {
                var a = PreparedPlasma(1e23, 0.003, fwhm, false); var b = PreparedPlasma(1e23, 0.003, fwhm, false, false);
                double d = 0, n = 0; for (int j = 0; j < a.Output.Ax.Length; j++) { var x = a.Output.Ax[j] - b.Output.Ax[j]; d += x.Magnitude * x.Magnitude; n += a.Output.Ax[j].Magnitude * a.Output.Ax[j].Magnitude; }
                return Math.Sqrt(d / n);
            }
            double d40 = Diff(40e-15), d80 = Diff(80e-15);
            Assert.Close(2, d40 / d80, 0.1, 0, "difference between the two operators scales with the bandwidth");
            Assert.Less(d80, 0.05, "and is small for an 80 fs pulse");
        }

        [Test(Reference = "cross-check: the envelope model against the carrier-resolved field model, same operator", Tolerance = "blueshift 2 %, energy 1 %, density 1e-4")]
        public void Plasma_EnvelopeAgreesWithCarrierResolved_ForA10FsMidInfraredPulse()
        {
            // 10 fs, 2 µm pulse in argon (MPI rate of order 4, so both models share the rate: PPT and ADK change to the quasi-static rate in carrier mode).
            // The blueshift (12 % of the carrier at 2 cm without the 1/ω² dependence, 7 % with it) and the ionisation loss agree between the models.
            foreach (double L in new[] { 0.02, 0.1 })
            {
                var e = IonisingGas(false, true, L); var c = IonisingGas(true, true, L);
                double w0 = e.Input.Grid.ReportOmega;
                double se = (SpectralCentroid(e.Output) - SpectralCentroid(e.Input)) / w0, sc = (SpectralCentroid(c.Output) - SpectralCentroid(c.Input)) / w0;
                double le = 1 - e.Output.TotalEnergyJ() / e.Input.TotalEnergyJ(), lc = 1 - c.Output.TotalEnergyJ() / c.Input.TotalEnergyJ();
                Assert.Greater(se, 0.05, "blueshift (fraction of the carrier) at L = " + L);
                Assert.Close(sc, se, 0.02, 0, "blueshift, envelope vs carrier-resolved, L = " + L);
                Assert.Close(lc, le, 0.01, 0, "energy loss, envelope vs carrier-resolved, L = " + L);
                Assert.Close(e.Records[0].Values["peakElectronDensityM3"], c.Records[0].Values["peakElectronDensityM3"], 1e-4, 0, "peak electron density");
                var o = IonisingGas(false, false, L);
                double so = (SpectralCentroid(o.Output) - SpectralCentroid(o.Input)) / w0;
                Assert.Greater(Math.Abs(so / se - 1), 0.2, "the carrier-frequency Drude term overestimates the blueshift (L = " + L + ")");
            }
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
