using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>
    /// Photo-ionisation and plasma in chi(2) crystals (docs/reference/ionization.md, "chi(2) crystals"): the electron density against the
    /// closed-form multiphoton integral, the energy the ionisation takes, the Drude phase of a prepared plasma, the catalogue band gaps and the
    /// energy bookkeeping of a real SHG with a plasma.
    /// </summary>
    [TestArea("Ionisation in chi(2) crystals")]
    public class Chi2IonizationTests
    {
        private const double Eps0 = 8.8541878128e-12, Me = 9.1093837015e-31, Qe = 1.602176634e-19;
        private const double N = 1.7;

        /// <summary>A dispersion-free crystal with d = 0 (no conversion): a bulk medium of index 1.7, so only the plasma acts.</summary>
        private static JsonObject Bulk() => O("name", "bulk", "no", O("kind", "constant", "value", 1.8), "ne", O("kind", "constant", "value", N), "deffPmPerV", 0.0, "n2", 0.0);

        private static JsonObject Element(double L, object ion) => O("id", "x", "type", "crystal", "crystal", Bulk(), "lengthM", L, "cut", O("thetaDeg", 90, "phiDeg", 0),
            "interaction", O("process", "shg", "type", "0"), "overlap", O("model", "planeWave", "radiusM", 100e-6), "chi3", false, "ionization", ion, "output", "fundamental");

        private static RunSpec NoSteep(JsonObject pulse) => Spec(pulse, O("physics", O("selfSteepening", false)));

        private static JsonObject Mpi(double sigmaK, double rho0, double tauC) => O("model", "mpi", "photons", 4, "sigmaK", sigmaK, "bandGapEv", 6.5, "effectiveMass", 0.5,
            "neutralDensityM3", 2e28, "electronDensityM3", rho0, "collisionTimeS", tauC, "recombinationTimeS", 1e30, "avalanche", false);

        [Test(Category = "physics", Reference = "closed form: rho = rho_n (1 - exp(-int sigma_K I^K dt)) for a Gaussian pulse (multiphoton rate, no avalanche)", Tolerance = "0.5 %")]
        public void Density_FollowsTheMultiphotonIntegral_AndTheIonisationEnergyIsTaken()
        {
            double lam = 1030e-9, fwhm = 100e-15, w = 100e-6, L = 0.2e-3, e = 1e-6, rhoN = 2e28, U = 6.5 * Qe;
            double area = Math.PI * w * w / 2, p0 = e * Math.Sqrt(4 * Math.Log(2) / Math.PI) / fwhm, i0 = p0 / area;
            double target = 1e-7;   // ionised fraction at the end of the pulse: U rho A L is 0.3 % of the pulse energy, so the pulse is hardly changed
            double s = 1; for (int k = 0; k < 4; k++) s *= i0;
            // int I^4 dt of a Gaussian: I0^4 fwhm sqrt(pi / (16 ln2)) ... computed numerically below
            double integral = 0; int n = 40001; double t0 = 4 * fwhm, dt = 2 * t0 / (n - 1);
            for (int i = 0; i < n; i++) integral += Math.Pow(Math.Exp(-4 * Math.Log(2) * Math.Pow(-t0 + i * dt, 2) / (fwhm * fwhm)), 4) * dt;
            double sigmaK = -Math.Log(1 - target) / (s * integral);
            var r = Go(Sys(Element(L, Mpi(sigmaK, 0, 1e-9))), NoSteep(Gauss(lam, fwhm, e)));
            var v = r.Records[0].Values;
            double want = rhoN * target;
            Assert.Close(want, v["peakElectronDensityM3"], 5e-3, 0, "peak electron density");
            // the ionisation takes U per electron: dE = U rho A_eff L (the collision time is long, so inverse bremsstrahlung is negligible)
            double lost = r.Records[0].EnergyInJ - r.Records[0].EnergyOutJ;
            Assert.Close(U * want * area * L, lost, 0.03, 0, "energy taken by the ionisation");
            Assert.True(r.Records[0].LossJ.ContainsKey("ionisation and plasma absorption"), "the energy budget names the plasma");
        }

        [Test(Category = "physics", Reference = "Drude model: dn = -rho / (2 n rho_c), rho_c = eps0 m w^2 / e^2; phase = k dn L", Tolerance = "0.5 %")]
        public void PreparedPlasma_GivesTheDrudePhase()
        {
            double lam = 1030e-9, fwhm = 100e-15, L = 0.1e-3, rho0 = 1e25, w0 = 2 * Math.PI * 2.99792458e8 / lam;
            JsonObject Run(bool on) => Element(L, on ? (object)O("model", "mpi", "photons", 4, "sigmaK", 0.0, "bandGapEv", 6.5, "effectiveMass", 0.5, "neutralDensityM3", 2e28, "electronDensityM3", rho0,
                "collisionTimeS", 1e-9, "recombinationTimeS", 1e30, "avalanche", false, "absorption", false) : false);
            var with = Go(Sys(Run(true)), NoSteep(Gauss(lam, fwhm, 1e-9)));
            var without = Go(Sys(Run(false)), NoSteep(Gauss(lam, fwhm, 1e-9)));
            int ip = Enumerable.Range(0, with.Output.Ax.Length).OrderByDescending(i => with.Output.Ax[i].Magnitude).First();
            double dphi = (with.Output.Ax[ip] / without.Output.Ax[ip]).Phase;
            double rhoC = Eps0 * Me * w0 * w0 / (Qe * Qe);
            double want = (w0 / 2.99792458e8) * L * (-rho0 / (2 * N * rhoC));
            Assert.Close(want, dphi, 5e-3, 0, "Drude phase at the pulse peak");
            Assert.Close(1.0, with.Records[0].EnergyOutJ / without.Records[0].EnergyOutJ, 1e-6, 0, "no absorption, no energy change");
        }

        [Test(Reference = "catalogue: the band gap of BBO is read from the crystal entry; a crystal without data is refused with a message", Tolerance = "exact")]
        public void CatalogueBandGap_IsUsed_AndAMissingOneIsRefused()
        {
            var bbo = O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 1e-3, "cut", O("thetaDeg", 29.2, "phiDeg", 0), "interaction", O("process", "shg", "type", "I"),
                "overlap", O("model", "planeWave", "radiusM", 100e-6), "ionization", true, "output", "sh");
            var r = Go(Sys(bbo), NoSteep(Gauss(800e-9, 50e-15, 1e-9)));
            Assert.True(r.Records[0].Values.ContainsKey("peakElectronDensityM3"), "the plasma reports its peak density");
            Assert.True(r.Records[0].Notes.Any(n => n.Contains("Keldysh")), "the band gap is traced to the catalogue / description");
            var nodata = Element(1e-3, O("model", "keldysh"));
            Assert.Throws<ModelException>(() => Go(Sys(nodata), NoSteep(Gauss(1030e-9, 100e-15, 1e-9))), "no band-gap data for the bulk crystal");
        }

        [Test(Category = "physics", Reference = "energy conservation: input - output = sum of the budget entries (SH, plasma); the SH of a plasma-free run is recovered when the rate is zero", Tolerance = "1e-6")]
        public void Shg_WithPlasma_KeepsTheEnergyBudget_AndZeroRateRecoversTheNoPlasmaResult()
        {
            double lam = 1030e-9, fwhm = 200e-15, w = 100e-6, L = 3e-3, e = 5e-6;
            JsonObject Make(object ion) => O("id", "x", "type", "crystal", "crystal", O("name", "ideal", "no", O("kind", "constant", "value", 1.8), "ne", O("kind", "constant", "value", N), "deffPmPerV", 2.0, "n2", 0.0),
                "lengthM", L, "cut", O("thetaDeg", 90, "phiDeg", 0), "interaction", O("process", "shg", "type", "0"), "overlap", O("model", "planeWave", "radiusM", w), "chi3", false, "ionization", ion, "output", "sh");
            var off = Go(Sys(Make(false)), NoSteep(Gauss(lam, fwhm, e)));
            var zero = Go(Sys(Make(Mpi(0.0, 0, 1e-9))), NoSteep(Gauss(lam, fwhm, e)));
            Assert.Close(off.Records[0].Values["conversionEfficiency"], zero.Records[0].Values["conversionEfficiency"], 1e-9, 0, "zero ionisation rate changes nothing");
            double sg = 0;
            { double i0 = e * Math.Sqrt(4 * Math.Log(2) / Math.PI) / fwhm / (Math.PI * w * w / 2); sg = 0.02 / (Math.Pow(i0, 4) * fwhm * 0.5); }
            var on = Go(Sys(Make(Mpi(sg, 0, 1e-9))), NoSteep(Gauss(lam, fwhm, e)));
            var rec = on.Records[0];
            Assert.Close(rec.EnergyInJ - rec.EnergyOutJ, rec.LossJ.Values.Sum(), 1e-6, 1e-20, "energy budget with the plasma");
            Assert.Greater(on.Records[0].Values["peakElectronDensityM3"], 0, "electrons were made");
            Assert.Less(on.Records[0].Values["conversionEfficiency"], off.Records[0].Values["conversionEfficiency"] * 1.0000001, "the plasma does not increase the conversion");
        }

        [Test(Category = "physics", Reference = "a radial chi(2) model: the on-axis plasma density equals the 1-D (Gaussian) one; energy budget closes", Tolerance = "1 %, 1e-6")]
        public void RadialModel_OnAxisDensityMatchesTheOneDimensionalPeak()
        {
            double lam = 1030e-9, fwhm = 100e-15, w = 100e-6, L = 0.2e-3, e = 1e-6, area = Math.PI * w * w / 2, i0 = e * Math.Sqrt(4 * Math.Log(2) / Math.PI) / fwhm / area;
            double integral = 0; int n = 40001; double t0 = 4 * fwhm, dt = 2 * t0 / (n - 1);
            for (int i = 0; i < n; i++) integral += Math.Pow(Math.Exp(-4 * Math.Log(2) * Math.Pow(-t0 + i * dt, 2) / (fwhm * fwhm)), 4) * dt;
            double sigmaK = -Math.Log(1 - 1e-7) / (Math.Pow(i0, 4) * integral);
            RunResult Make(JsonObject tv)
            {
                var el = Element(L, Mpi(sigmaK, 0, 1e-9)); el.Remove("overlap"); el.Set("beam", O("kind", "gaussian", "w0M", w));
                if (tv != null) el.Set("transverse", tv);
                return Go(Sys(el), NoSteep(Gauss(lam, fwhm, e)));
            }
            var oneD = Make(null); var radial = Make(O("model", "radial", "points", 160, "radiusM", 4 * w));
            double a = oneD.Records[0].Values["peakElectronDensityM3"], b = radial.Records[0].Values["peakElectronDensityM3"];
            Assert.Close(a, b, 0.01, 0, "on-axis peak density, radial vs 1-D (the innermost Hankel point is slightly off axis)");
            var rec = radial.Records[0];
            Assert.Close(rec.EnergyInJ - rec.EnergyOutJ, rec.LossJ.Values.Sum(), 1e-6, 1e-20, "energy budget");
        }

        [Test(Category = "physics", Reference = "a crystal with d = 0 is a bulk element: the default (Keldysh) plasma gives the same electron density and energy as a bulk element of the same index, band gap, mass and density", Tolerance = "0.2 %")]
        public void KeldyshCrystalWithZeroD_EqualsTheBulkPlasma()
        {
            double lam = 1030e-9, fwhm = 100e-15, w = 100e-6, L = 0.2e-3, e = 10e-6;
            var ion = O("bandGapEv", 6.5, "effectiveMass", 0.5, "neutralDensityM3", 2e28, "collisionTimeS", 1e-15, "recombinationTimeS", 1e30);
            var cry = Go(Sys(Element(L, ion)), NoSteep(Gauss(lam, fwhm, e)));
            var o = O("format", "ultrafast-system", "version", 2, "name", "t", "materials", O("m", FlatMaterial(0.0, N)));
            o.Set("elements", A(O("id", "x", "type", "bulk", "material", "m", "lengthM", L, "beam", O("kind", "constant", "areaM2", Math.PI * w * w / 2), "ionization", ion)));
            var bulk = Go(UltrafastEngine.Systems.SystemDescription.FromJson(o), NoSteep(Gauss(lam, fwhm, e)));
            double a = cry.Records[0].Values["peakElectronDensityM3"], b = bulk.Records[0].Values["peakElectronDensityM3"];
            Assert.Greater(b, 1e18, "the test ionises (bulk density)");
            Assert.Close(b, a, 2e-3, 0, "peak electron density, crystal vs bulk");
            Assert.Close(bulk.Records[0].EnergyOutJ, cry.Records[0].EnergyOutJ, 2e-3, 0, "output energy, crystal vs bulk");
        }

        [Test(Category = "physics", Reference = "photon order of each field's own carrier: the fundamental of 800 nm SHG in BBO (6.56 eV gap) is a 5-photon process, not the 2-photon one of the 400 nm carrier", Tolerance = "factor 1e3")]
        public void EachFieldIonisesAtItsOwnCarrier_NoSpuriousPlasmaFromTheHarmonicsRate()
        {
            // BBO SHG at 800 nm, 1 uJ in 100 um, 100 fs: I ~ 1e14 W/m^2 ... the fundamental alone cannot make a measurable density with a 5-photon rate
            var bbo = O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 1e-6, "cut", O("thetaDeg", 29.2, "phiDeg", 0), "interaction", O("process", "shg", "type", "I"),
                "overlap", O("model", "planeWave", "radiusM", 100e-6), "ionization", true, "output", "sh");
            var r = Go(Sys(bbo), NoSteep(Gauss(800e-9, 100e-15, 1e-6)));
            double rho = r.Records[0].Values["peakElectronDensityM3"];
            // the same fluence at a two-photon rate would ionise a few percent of 2e28 /m^3; the 5-photon rate gives orders of magnitude less
            Assert.Less(rho, 1e22, "no spurious plasma from the fundamental (5-photon order)");
        }
    }
}
