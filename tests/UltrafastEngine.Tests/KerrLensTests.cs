using System;
using System.Linq;
using UltrafastEngine.Materials;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Results;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Self-focusing of the 1-D Gaussian beam by the pulse's Kerr lens (kerrLens; docs/reference/nonlinear.md).</summary>
    [TestArea("Kerr lens of the 1-D beam")]
    public class KerrLensTests
    {
        private const double Lam = 1030e-9;

        private static GaussianBeam Beam(double w0, double n) { double zr = Math.PI * w0 * w0 * n / Lam; return new GaussianBeam { W0X = w0, W0Y = w0, ZwX = 0, ZwY = 0, ZrX = zr, ZrY = zr }; }

        // model critical power: diffraction w'' = Lambda^2 / w^3 balances the lens K w, K = 2 c n2 P / (pi w^4)  ->  P_c = lambda^2 / (2 pi c n n2)
        private static double ModelCriticalPowerW(double n2, double n, double c) => Lam * Lam / (2 * Math.PI * c * n * n2);

        [Test(Reference = "Ermakov equation w'' = w0^4 (1 - P/P_c) / (zR^2 w^3): w^2 = w0^2 [1 + (1 - P/P_c)(z/zR)^2]", Tolerance = "1e-6 (RK4)")]
        public void Integrate_RoundBeamAtItsWaist_FollowsTheExactSolution()
        {
            double n = 1.45, n2 = 2.7e-20, w0 = 100e-6, L = 0.06;
            var b = Beam(w0, n);
            foreach (var (c, name) in new[] { (1.0, "secondMoment"), (4.0, "paraxial") })
                foreach (double frac in new[] { 0.0, 0.5, 1.0, 2.0 })
                {
                    double pc = ModelCriticalPowerW(n2, n, c), p = frac * pc;
                    var lensed = KerrLens.Integrate(b, n2, n, p, L, c, out double collapse);
                    // frac = 2: the beam collapses at z = zR / sqrt(P/Pc - 1)
                    double zCollapse = frac > 1 ? b.ZrX / Math.Sqrt(frac - 1) : double.PositiveInfinity;
                    foreach (double z in new[] { 0.01, 0.03, 0.06 })
                    {
                        if (z >= 0.9 * zCollapse) continue;
                        double expected = w0 * Math.Sqrt(1 + (1 - frac) * Math.Pow(z / b.ZrX, 2));
                        if (frac == 1.0) expected = w0;   // self-trapped: the radius does not change at all
                        Assert.Close(expected, lensed.RadiusX(z), 2e-5, 0, name + ", P/Pc = " + frac + ", z = " + z);
                    }
                    if (frac > 1) Assert.True(!double.IsNaN(collapse) && Math.Abs(collapse / zCollapse - 1) < 0.02, name + ": collapse distance zR / sqrt(P/Pc - 1) (found " + collapse + ", expected " + zCollapse + ")");
                    else Assert.True(double.IsNaN(collapse), name + ": no collapse below the critical power");
                }
        }

        [Test(Reference = "Marburger, Prog. Quantum Electron. 4, 35 (1975): P_cr = 3.77 lambda^2 / (8 pi n0 n2)", Tolerance = "1 % (the ratio 4/3.77 of the second-moment model)")]
        public void ModelCriticalPower_IsWithinSixPercentOfMarburger()
        {
            double n = 1.45, n2 = 2.7e-20;
            double marburger = PhysicalConstants.CriticalPowerFactor * Lam * Lam / (8 * Math.PI * n * n2);
            Assert.Close(4.0 / PhysicalConstants.CriticalPowerFactor, ModelCriticalPowerW(n2, n, 1.0) / marburger, 0.01, 0, "P_c(second moment) / P_cr(Marburger)");
        }

        private static (double exitX, double powerW, double b, double peakI) Run(string lens, double energyJ, double w0, double n, double L, double n2)
        {
            var el = O("id", "k", "type", "bulk", "material", "FusedSilica", "lengthM", L, "n2", n2, "selfSteepening", false,
                       "beam", O("kind", "gaussian", "w0M", w0, "zWaistM", 0.0, "wavelengthM", Lam, "n", n));
            if (lens != null) el.Set("kerrLens", lens);
            var r = Go(Sys(el), Spec(Gauss(Lam, 300e-15, energyJ)));
            var rec = r.Records[0];
            rec.Values.TryGetValue("kerrLensExitRadiusXM", out double wx); rec.Values.TryGetValue("kerrLensPowerW", out double pw);
            return (wx, pw, rec.BIntegralRad, rec.PeakIntensityWPerM2);
        }

        [Test(Reference = "Ermakov solution with the pulse's own peak power", Tolerance = "0.1 %")]
        public void KerrLens_InAnElement_ReproducesTheExactBeamAndRaisesTheB()
        {
            double w0 = 100e-6, L = 0.04, n2 = 2.7e-20;
            double n = MaterialLibrary.BuiltIn("FusedSilica").IndexAt(2 * Math.PI * C / Lam);
            double pc = ModelCriticalPowerW(n2, n, 1.0);
            double energy = 0.5 * pc * 300e-15 / 0.94;   // peak power of a Gaussian pulse is 0.94 E / tau
            var off = Run(null, energy, w0, n, L, n2);
            var on = Run("peak", energy, w0, n, L, n2);
            double zr = Math.PI * w0 * w0 * n / Lam;
            double expected = w0 * Math.Sqrt(1 + (1 - on.powerW / pc) * Math.Pow(L / zr, 2));
            Assert.Close(0.5, on.powerW / pc, 0.03, 0, "test power is about half the critical power");
            Assert.Close(expected, on.exitX, 1e-3, 0, "exit radius = Ermakov solution for the engine's peak power");
            Assert.Less(on.exitX, w0 * Math.Sqrt(1 + Math.Pow(L / zr, 2)) * 0.99, "the lens makes the exit radius smaller than the free beam's");
            Assert.Greater(on.b, off.b * 1.01, "a smaller beam has a higher intensity and a larger B");
        }

        [Test(Reference = "P -> 0: the lens vanishes", Tolerance = "1e-5")]
        public void KerrLens_AtLowPower_ChangesNothing()
        {
            double w0 = 100e-6, L = 0.04, n2 = 2.7e-20;
            double n = MaterialLibrary.BuiltIn("FusedSilica").IndexAt(2 * Math.PI * C / Lam);
            var off = Run(null, 1e-13, w0, n, L, n2); var on = Run("peak", 1e-13, w0, n, L, n2);
            Assert.Close(off.b, on.b, 1e-5, 0, "B at 1e-13 J (P / P_c = 1e-7)");
            double zr = Math.PI * w0 * w0 * n / Lam;
            Assert.Close(w0 * Math.Sqrt(1 + Math.Pow(L / zr, 2)), on.exitX, 1e-5, 0, "free-beam radius");
        }

        [Test(Reference = "B = k0 n2 I L is linear in n2; the element key n2 overrides the material's (docs/reference/system-file.md)", Tolerance = "1e-3 (the pulse reshapes slightly at B = 0.1)")]
        public void ElementN2_OverridesTheMaterial()
        {
            // bug found while writing the Kerr-lens tests: the element's own "n2" was ignored (only run-spec overrides were read)
            double B(double? n2)
            {
                var el = O("id", "k", "type", "bulk", "material", "FusedSilica", "lengthM", 0.01, "selfSteepening", false, "beam", O("kind", "constant", "areaM2", 1e-8));
                if (n2 != null) el.Set("n2", n2.Value);
                return Go(Sys(el), Spec(Gauss(Lam, 300e-15, 1e-7))).Records[0].BIntegralRad;
            }
            double n2Material = MaterialLibrary.BuiltIn("FusedSilica").N2, b0 = B(null);
            Assert.Greater(b0, 1e-3, "reference B");
            Assert.Close(2 * b0, B(2 * n2Material), 1e-3, 0, "n2 on the element doubles B");
        }

        [Test(Reference = "weighted power sum(P^2)/sum(P) <= peak power", Tolerance = "exact")]
        public void PulseWeightedPower_IsBelowThePeakAndEqualsItForAFlatPulse()
        {
            Assert.Close(5.0, KerrLens.EffectivePowerW(new[] { 5.0, 5.0, 5.0, 5.0 }, "pulseWeighted"), 1e-12, 0, "flat pulse");
            var gauss = Enumerable.Range(-200, 401).Select(i => Math.Exp(-i * i / 3000.0)).ToArray();
            double peak = KerrLens.EffectivePowerW(gauss, "peak"), w = KerrLens.EffectivePowerW(gauss, "pulseWeighted");
            Assert.Close(1.0, peak, 1e-12, 0, "peak");
            Assert.Close(1 / Math.Sqrt(2), w, 1e-3, 0, "Gaussian pulse: sum P^2 / sum P = peak / sqrt 2");
        }
    }
}
