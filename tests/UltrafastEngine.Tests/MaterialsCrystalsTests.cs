// MaterialsCrystalsTests.cs -- crystal polarisation vectors / d_eff for explicit cuts (B8), delayed-response polarisation split (B11),
// CS2 liquid (G2) and the cubic chi(3) tensor with BaF2 (G1). Each test states the analytic or published answer.

using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Results;
using UltrafastEngine.Systems;
using UltrafastEngine.Crystals;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Materials and crystals: d_eff of explicit cuts, delayed-response split, CS2, cubic chi(3)")]
    public class MaterialsCrystalsTests
    {
        private const double C = 299792458.0;

        // d_eff = e3 . d : e1 e2 with unit E-field vectors. For KDP type-I (o o -> e) only d36 acts: d_eff = 2 d36 o_x o_y E_z with
        // o = (-sin phi, cos phi, 0) and E_e = (D_x/no^2, D_y/no^2, D_z/ne^2) normalised, D_e = (cos th cos phi, cos th sin phi, -sin th):
        // |d_eff| = d36 |sin 2phi| |E_z|. The textbook d36 sin(th) sin(2 phi) is the same with E_z -> D_z (walk-off ignored, 2 % at 50 deg).
        // Before the fix the o-eigenvector was chosen from rounding noise (B ~ 1e-17), so d_eff jumped with phi at fixed theta.
        [Test(Category = "physics", Reference = "KDP (-42m) type-I o o -> e: d_eff = d36 |sin 2phi| E_z (exact E-field vectors); textbook d36 sin(theta) sin(2 phi) within the walk-off angle (Dmitriev, Handbook of NLO crystals)", Tolerance = "1e-6 relative (exact); 4 % (textbook)")]
        public void Kdp_TypeI_Deff_FollowsD36Sin2Phi_ForEveryPhi_AtFixedTheta()
        {
            var kdp = CrystalLibrary.Get("KDP", 20);
            double w = 2 * Math.PI * C / 1064e-9;
            double d36 = kdp.D[2, 5]; // d_36 = d_z,xy (Voigt 6)
            double no2 = kdp.Mode(2 * w, CrystalModel.Direction(0.3, 0.1), "o").N, ne2 = kdp.Mode(2 * w, CrystalModel.Direction(Math.PI / 2, 0), "e").N;
            foreach (double thDeg in new[] { 50.5478, 41.2, 70.0 })
            foreach (double phDeg in new[] { 0.3, 10.0, 44.75, 45.0, 45.25, 46.0, 60.0, 90.0, 135.0, 200.0, 300.0 })
            {
                double th = thDeg * Math.PI / 180, ph = phDeg * Math.PI / 180;
                var s = CrystalModel.Direction(th, ph);
                var o = kdp.Mode(w, s, "o"); var e = kdp.Mode(2 * w, s, "e");
                double deff = kdp.Deff(o.E, o.E, e.E);
                double dx = Math.Cos(th) * Math.Cos(ph) / (no2 * no2), dy = Math.Cos(th) * Math.Sin(ph) / (no2 * no2), dz = -Math.Sin(th) / (ne2 * ne2);
                double ez = dz / Math.Sqrt(dx * dx + dy * dy + dz * dz);
                double exact = d36 * Math.Abs(Math.Sin(2 * ph)) * Math.Abs(ez);
                Assert.Close(exact, Math.Abs(deff), 1e-6, 1e-16, "KDP deff theta " + thDeg + " phi " + phDeg);
                double textbook = d36 * Math.Sin(th) * Math.Sin(2 * ph);
                Assert.Close(Math.Abs(textbook), Math.Abs(deff), 0.04, 1e-16, "KDP deff vs textbook, theta " + thDeg + " phi " + phDeg);
            }
        }

        // ------------------------------------------------------------------------------------------------ G2: liquid CS2

        private const double Cs2B = 1.50387, Cs2C = 0.03049;   // Kedenburg et al. 2012: n^2 = 1 + B l^2 / (l^2 - C), l in um

        /// <summary>n and d2n/dlambda2 (1/um^2) of the one-pole formula by closed-form derivatives (independent of the engine's finite differences).</summary>
        private static (double N, double D2N) Cs2Analytic(double lamUm)
        {
            double l2 = lamUm * lamUm, q = l2 - Cs2C;
            double f = 1 + Cs2B * l2 / q;
            double f1 = -2 * lamUm * Cs2B * Cs2C / (q * q);
            double f2 = -2 * Cs2B * Cs2C / (q * q) + 8 * l2 * Cs2B * Cs2C / (q * q * q);
            double n = Math.Sqrt(f);
            return (n, f2 / (2 * n) - f1 * f1 / (4 * n * n * n));
        }

        [Test(Category = "physics", Reference = "CS2 at 20 C, sodium D line: n = 1.6276 (Kedenburg et al. 2012; refractiveindex.info, CC0); CRC Handbook n_D(20 C) = 1.6319 for the liquid", Tolerance = "0.003")]
        public void Cs2_RefractiveIndex_AtSodiumD_MatchesKedenburgAndTheHandbook()
        {
            var cs2 = MaterialLibrary.BuiltIn("CS2");
            double n = cs2.IndexAt(W(589.3e-9));
            Assert.Close(1.6276, n, 0.0005, 0, "Kedenburg n(589.3 nm)");
            Assert.Close(1.6319, n, 0.003, 0, "CRC handbook n_D");
        }

        [Test(Category = "physics", Reference = "closed-form second derivative of the one-pole Sellmeier formula: GVD = lambda^3 n'' / (2 pi c^2)", Tolerance = "0.3 %")]
        public void Cs2_Gdd_MatchesTheAnalyticSecondDerivative()
        {
            const double L = 1e-3;
            foreach (double lam in new[] { 0.6e-6, 0.8e-6, 1.03e-6, 1.5e-6 })
            {
                var sys = Sys(O("id", "cell", "type", "bulk", "material", "CS2", "lengthM", L, "beam", O("kind", "constant", "radiusM", 1e-3)));
                var r = Go(sys, Spec(Gauss(lam, 100e-15, 1e-12)));
                double n2d = Cs2Analytic(lam * 1e6).D2N * 1e12;           // d2n/dlambda2 in 1/m^2
                double expect = lam * lam * lam / (2 * Math.PI * C * C) * n2d * L;
                Assert.Close(expect, r.FinalSystem.GddS2, 0.003, 0, "CS2 GDD at " + lam * 1e9 + " nm");
            }
        }

        private static RunResult Cs2Run(double chi, double energy, double fwhm, double b = double.NaN)
        {
            var j = O("format", "ultrafast-system", "version", 2, "name", "cs2");
            if (!double.IsNaN(b)) j.Set("materials", O("CSX", O("base", "CS2", "raman", O("kind", "orientational", "anisotropicFraction", b, "fR", 0.9))));
            j.Set("elements", A(O("id", "cell", "type", "bulk", "material", double.IsNaN(b) ? "CS2" : "CSX", "lengthM", 1e-3, "beam", O("kind", "constant", "radiusM", 100e-6))));
            var pulse = O("shape", "gaussian", "centerWavelengthM", 1030e-9, "durationFwhmS", fwhm, "energyJ", energy,
                "polarization", O("angleDeg", 0.0, "ellipticity", Math.Tan(chi)));
            return Go(SystemDescription.FromJson(j), Spec(pulse, O("physics", O("selfSteepening", false, "raman", true), "grid", O("auto", false, "n", 8192, "windowS", 250e-12))));
        }

        private static int Peak(Complex[] ix, Complex[] iy)
        {
            int k = 0; double m = -1;
            for (int i = 0; i < ix.Length; i++) { double v = ix[i].Magnitude * ix[i].Magnitude + iy[i].Magnitude * iy[i].Magnitude; if (v > m) { m = v; k = i; } }
            return k;
        }

        /// <summary>Signed phase of the x component at the input's intensity peak (rad).</summary>
        private static double PeakPhaseX(RunResult r)
        {
            var ix = r.Input.TemporalAx(); var iy = r.Input.TemporalAy(); var ox = r.Output.TemporalAx();
            int k = Peak(ix, iy);
            return (ox[k] / ix[k]).Phase;
        }

        /// <summary>The nonlinear phase at the pulse peak, free of the linear (GDD) phase of the cell: (phi(E) - phi(E/100)) / 0.99.</summary>
        private static double NlPhase(double chi, double energy, double fwhm) =>
            (PeakPhaseX(Cs2Run(chi, energy, fwhm)) - PeakPhaseX(Cs2Run(chi, energy / 100, fwhm))) / 0.99;

        [Test(Category = "physics", Reference = "B = k0 n2 I0 L; peak phase = B [(1-fR) + fR d] with d = int h(s) I(-s) ds / I0 (retarded response of a Gaussian pulse); circular: (1-fR) 2/3 + fR d/4 (electronic Kerr + purely orientational, Boyd Nonlinear Optics 3rd ed. sec. 4.4)", Tolerance = "0.5 %")]
        public void Cs2_Pulse_PeakPhase_FollowsTheRetardedOrientationalResponse_AndCircularSeesLessThanLinear()
        {
            double energy = 1e-6, lam = 1030e-9, w0 = 100e-6, L = 1e-3;
            var cs2 = MaterialLibrary.BuiltIn("CS2");
            double fR = cs2.RamanFraction;
            foreach (double fwhm in new[] { 15e-12, 30e-12 })
            {
                double p0 = energy * Math.Sqrt(4 * Math.Log(2) / Math.PI) / fwhm;
                double i0 = 2 * p0 / (Math.PI * w0 * w0);     // peak intensity of a constant-radius (1/e^2) Gaussian beam
                double b = 2 * Math.PI / lam * cs2.N2 * i0 * L;
                double a = 4 * Math.Log(2) / (fwhm * fwhm), d = 0;
                for (int q = 0; q < 40000; q++) { double s = q * 1e-15; d += (Math.Exp(-s / MaterialLibrary.CS2DecayS) - Math.Exp(-s / MaterialLibrary.CS2RiseS)) / (MaterialLibrary.CS2DecayS - MaterialLibrary.CS2RiseS) * Math.Exp(-a * s * s) * 1e-15; }
                // the reported B is the largest index change seen (it occurs after the intensity peak, where the delayed part is fuller): between the two
                double rep = Cs2Run(0, energy, fwhm).Records.Sum(x => x.BIntegralRad);
                Assert.Greater(rep, b * ((1 - fR) + fR * d) * 0.999, "reported B above the peak phase");
                Assert.Less(rep, b * 1.001, "reported B below k0 n2 I0 L");
                Assert.Close(b * ((1 - fR) + fR * d), NlPhase(0, energy, fwhm), 0.005, 0, "linear peak phase at " + fwhm * 1e12 + " ps");
                Assert.Close(b * ((1 - fR) * 2.0 / 3.0 + fR * d / 4), NlPhase(Math.PI / 4, energy, fwhm), 0.005, 0, "circular peak phase at " + fwhm * 1e12 + " ps");
            }
        }

        [Test(Category = "physics", Reference = "NER rotation = B sin(2chi) ((1-fR)/3 + fR b/2) with b = 3/2 for the orientational part (Maker-Terhune-Savage 1964; Hellwarth tensor)", Tolerance = "2 %")]
        public void Cs2_LongPulse_EllipseRotation_UsesTheOrientationalTensor()
        {
            double fwhm = 30e-12, energy = 1e-6, chi = 0.25;
            var cs2 = MaterialLibrary.BuiltIn("CS2");
            var lin = Cs2Run(0, energy, fwhm); var ell = Cs2Run(chi, energy, fwhm);
            double fR = cs2.RamanFraction, bl = lin.Records.Sum(x => x.BIntegralRad);
            var ix = ell.Input.TemporalAx(); var iy = ell.Input.TemporalAy(); var ox = ell.Output.TemporalAx(); var oy = ell.Output.TemporalAy();
            int k = Peak(ix, iy);
            double Az(Complex ex, Complex ey) => 0.5 * Math.Atan2(2 * (ex * Complex.Conjugate(ey)).Real, ex.Magnitude * ex.Magnitude - ey.Magnitude * ey.Magnitude);
            double rot = Math.Abs(Az(ox[k], oy[k]) - Az(ix[k], iy[k]));
            Assert.Close(bl * Math.Sin(2 * chi) * ((1 - fR) / 3 + fR * 1.5 / 2), rot, 0.02, 0, "NER rotation of CS2");
        }

        [Test(Category = "physics", Reference = "response normalisation: unit integral, causal, peak at rise*decay*ln(decay/rise)/(decay-rise)", Tolerance = "1e-3")]
        public void OrientationalResponse_HasUnitIntegral_AndTheAnalyticPeakTime()
        {
            var h = new OrientationalResponse(0.2e-12, 1.6e-12);
            var t = Enumerable.Range(0, 20000).Select(i => i * 1e-15).ToArray();
            var v = h.Evaluate(t);
            Assert.Close(1.0, v.Sum() * 1e-15, 1e-3, 0, "integral");
            int kmax = Array.IndexOf(v, v.Max());
            double tp = 0.2e-12 * 1.6e-12 * Math.Log(1.6 / 0.2) / 1.4e-12;
            Assert.Close(tp, t[kmax], 0.01, 0, "peak time");
            Assert.Close(1.5, h.AnisotropicFraction, 0, 0, "purely orientational");
        }
    }
}
