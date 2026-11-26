using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Spatial (transverse) models")]
    public class SpatialTests
    {
        private const double Lam = 1030e-9;

        [Test(Reference = "∫ Gaussian = π w²/2; transform round trip", Tolerance = "1e-06")]
        public void RadialBasis_QuadratureAndTransformAreExact()
        {
            var b = new RadialBasis(64, 3e-3);
            double w = 0.8e-3, s = 0;
            for (int m = 0; m < b.M; m++) s += b.Weights[m] * Math.Exp(-2 * b.Radius[m] * b.Radius[m] / (w * w));
            Assert.Close(Math.PI * w * w / 2, s, 1e-6, 0, "integral of a Gaussian");
            var f = new Complex[b.M];
            for (int m = 0; m < b.M; m++) f[m] = new Complex(Math.Exp(-b.Radius[m] * b.Radius[m] / (w * w)), 0.3 * b.Radius[m] / w);
            var g = (Complex[])f.Clone();
            b.ToK(g, 1); b.ToSpace(g, 1);
            double err = f.Zip(g, (x, y) => (x - y).Magnitude).Max();
            Assert.True(err < 1e-12, "Hankel transform round trip error " + err);
        }

        private static JsonObject GaussBeam(double w0, double zWaist) => O("kind", "gaussian", "w0M", w0, "zWaistM", zWaist, "wavelengthM", Lam);

        private static double BeamRadiusAfter(string model, double lengthM, double w0, int points = 64)
        {
            var tv = model == "radial" ? O("model", "radial", "points", points, "radiusM", 6 * w0 * Math.Sqrt(1 + Math.Pow(lengthM / (Math.PI * w0 * w0 / Lam), 2)) / 1.0)
                                       : O("model", "cartesian", "nx", points, "ny", points, "halfWidthM", 5 * w0 * Math.Sqrt(1 + Math.Pow(lengthM / (Math.PI * w0 * w0 / Lam), 2)));
            var sys = Sys(O("id", "free", "type", "gap", "material", "Vacuum", "lengthM", lengthM, "beam", GaussBeam(w0, 0), "transverse", tv));
            var r = Go(sys, Spec(Gauss(Lam, 200e-15, 1e-6)));
            Assert.Close(r.Input.TotalEnergyJ(), r.Output.TotalEnergyJ(), 1e-9, 0, model + " energy");
            Assert.Close(1.0, r.Records[0].BeamQualityM2, 0.02, 0, model + " M^2 of a Gaussian");
            return r.Records[0].BeamRadiusXM;
        }

        [Test(Reference = "Gaussian beam w(z) = w0 √(1 + (z/z_R)²)", Tolerance = "0.5 %")]
        public void Diffraction_GaussianBeamRadiusMatchesAnalytic_Radial()
        {
            double w0 = 100e-6, zr = Math.PI * w0 * w0 / Lam;
            double w = BeamRadiusAfter("radial", 2 * zr, w0);
            Assert.Close(w0 * Math.Sqrt(5), w, 0.005, 0, "w(2 zR)");
        }

        [Test(Reference = "Gaussian beam w(z) = w0 √(1 + (z/z_R)²)", Tolerance = "1 %")]
        public void Diffraction_GaussianBeamRadiusMatchesAnalytic_Cartesian()
        {
            double w0 = 100e-6, zr = Math.PI * w0 * w0 / Lam;
            double w = BeamRadiusAfter("cartesian", 1.5 * zr, w0, 64);
            Assert.Close(w0 * Math.Sqrt(1 + 2.25), w, 0.01, 0, "w(1.5 zR)");
        }

        [Test(Reference = "Gaussian optics: waist λf/(πw)", Tolerance = "2 %")]
        public void LensAndGap_FocusAGaussianBeamAsGaussianOpticsPredicts()
        {
            // collimated w = 1 mm -> f = 0.5 m lens -> 0.5 m: waist w_f = lambda f / (pi w)
            double w = 1e-3, f = 0.5;
            var tvIn = O("model", "radial", "points", 64, "radiusM", 3.5e-3);
            var tvFocus = O("model", "radial", "points", 224, "radiusM", 3.5e-3); // ~20 um spacing for a 164 um focus
            var sys = Sys(
                O("id", "start", "type", "gap", "material", "Vacuum", "lengthM", 0.0, "beam", GaussBeam(w, 0), "transverse", tvIn),
                O("id", "lens", "type", "lens", "focalLengthM", f, "medium", "Vacuum"),
                O("id", "toFocus", "type", "gap", "material", "Vacuum", "lengthM", f, "transverse", tvFocus));
            var r = Go(sys, Spec(Gauss(Lam, 300e-15, 1e-6)));
            double wf = Lam * f / (Math.PI * w);
            Assert.Close(wf, r.Records[2].BeamRadiusXM, 0.02, 0, "focal spot radius");
            Assert.True(r.Records[2].Notes.Any(n => n.Contains("carried exactly")), "profile transported exactly");
        }

        [Test(Category = "physics")]
        public void CarriedProfile_ThroughFreeSpace_ConservesEnergyAfterSpectralBroadening()
        {
            // a self-focused, spectrally broadened beam: the profile is stored at sampled frequencies and must carry the
            // exact energy through the following (linear) spatial elements
            var tv = O("model", "radial", "points", 48, "radiusM", 0.4e-3);
            var sys = Sys(
                O("id", "plate", "type", "bulk", "material", "FusedSilica", "lengthM", 5e-3, "beam", GaussBeam(100e-6, 0), "transverse", tv),
                O("id", "gap1", "type", "gap", "material", "Vacuum", "lengthM", 5e-3, "transverse", tv),
                O("id", "gap2", "type", "gap", "material", "Vacuum", "lengthM", 5e-3, "transverse", O("model", "radial", "points", 64, "radiusM", 0.6e-3)));
            var r = Go(sys, Spec(Gauss(Lam, 200e-15, 3e-6)));
            Assert.Greater(RmsSpectralWidth(r.Output) / RmsSpectralWidth(r.Input), 1.03, "spectrum broadened");
            Assert.Close(r.Records[0].EnergyOutJ, r.Output.TotalEnergyJ(), 1e-9, 0, "free space conserves energy");
        }

        [Test(Reference = "thin Kerr lens: k0 n2 I0 L exp(−2r²/w²)", Tolerance = "0.2 %")]
        public void ThinKerrLens_PhaseProfileMatchesAnalytic()
        {
            // L << zR: phase(r, t=0) - phase_linear(r, t=0) = k0 n2 I0 L exp(-2 r^2/w^2); the linear reference removes
            // the (small, r-independent) Gouy phase L/zR, which is of the same order as the Kerr phase here
            double w = 1e-3, L = 1e-3, n2 = 1e-19, peak = 1e7;
            var grid = FrequencyGrid.Create(256, 10e-15, U.W(Lam));
            var basis = new RadialBasis(48, 3.5e-3);
            int n = grid.N;
            double i0 = 2 * peak / (Math.PI * w * w);
            double sig = 300e-15 / (2 * Math.Sqrt(2 * Math.Log(2)));
            Complex[] Run(double n2v)
            {
                var med = new NonlinearMedium { N2 = n2v, EnableSelfSteepening = false, EnableRaman = false };
                var prob = new SpatialEnvelopeProblem(grid, basis, med, L, new double[n], Enumerable.Repeat(1.0, n).ToArray(), _ => 1.0, false, 0, 0, "t");
                var E = new Complex[basis.M * n];
                for (int m = 0; m < basis.M; m++)
                {
                    var row = new Complex[n];
                    for (int i = 0; i < n; i++) row[i] = Math.Sqrt(i0) * Math.Exp(-basis.Radius[m] * basis.Radius[m] / (w * w)) * Math.Exp(-grid.Time[i] * grid.Time[i] / (4 * sig * sig));
                    Array.Copy(SpectralTransform.TimeToFreq(row), 0, E, m * n, n);
                }
                basis.ToK(E, n);
                var o = n2v == 0 ? E : Steppers.Propagate(prob, E, new StepperSettings { Tolerance = 1e-9 }, new StepStatistics());
                if (n2v == 0)
                {
                    // linear: one exact step of the diffraction operator
                    var lin = new SpatialEnvelopeProblem(grid, basis, null, L, new double[n], Enumerable.Repeat(1.0, n).ToArray(), _ => 1.0, false, 0, 0, "t");
                    o = Steppers.Propagate(lin, E, new StepperSettings(), new StepStatistics());
                }
                basis.ToSpace(o, n);
                return o;
            }
            var nl = Run(n2); var li = Run(0);
            double k0 = U.W(Lam) / C;
            for (int m = 0; m < basis.M; m += 6)
            {
                if (basis.Radius[m] > 1.5 * w) break;
                var a = SpectralTransform.FreqToTime(nl.Skip(m * n).Take(n).ToArray());
                var b = SpectralTransform.FreqToTime(li.Skip(m * n).Take(n).ToArray());
                double expected = k0 * n2 * i0 * L * Math.Exp(-2 * basis.Radius[m] * basis.Radius[m] / (w * w));
                Assert.Close(expected, (a[n / 2] / b[n / 2]).Phase, 0.002, 1e-6, "Kerr phase at r = " + basis.Radius[m]);
            }
        }

        // ------------------------------------------------------------------ Kerr lens of fused silica, through the compiler
        // The set-up of docs/examples/Nonlinear/spatial-kerr: a collimated w0 = 100 um Gaussian beam (waist at the entrance) and a
        // 300 fs Gaussian pulse at 1030 nm in FusedSilica with Raman and self-steepening on, on the auto-sized grid (dt = 25 fs,
        // two periods of the 12 fs Raman oscillation). Before 8dfff5d the Raman kernel sampled on that grid summed to 0.585
        // instead of 1, so the Kerr index was 0.82 + 0.18 x 0.585 = 0.925 of n2: B, the Kerr lens and the change of beam size were
        // all 7.5 % weak (WP fix-2). These tests pin them against Gaussian-beam theory with the catalogue n2.

        private const double SilicaW0 = 100e-6;

        private static UltrafastEngine.Results.RunResult SilicaKerrPlate(double plateM, double energyJ, int gaps)
        {
            var els = new System.Collections.Generic.List<JsonObject>
            {
                O("id", "plate", "type", "bulk", "material", "FusedSilica", "lengthM", plateM, "beam", O("kind", "gaussian", "w0M", SilicaW0),
                  "transverse", O("model", "radial", "points", 96, "radiusM", 0.45e-3))
            };
            for (int i = 1; i <= gaps; i++)
                els.Add(O("id", "free" + i, "type", "gap", "material", "Vacuum", "lengthM", 10e-3, "transverse", O("model", "radial", "points", 224, "radiusM", 1.0e-3)));
            return Go(Sys(els.ToArray()), Spec(O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 300e-15, "energyJ", energyJ, "polarization", "x"),
                O("physics", O("selfSteepening", true, "raman", true))));
        }

        private static (double N0, double N2, double P0, double ZrM) SilicaBeam(double energyJ)
        {
            var mat = MaterialLibrary.BuiltIn("FusedSilica");
            var nv = new double[1]; mat.Index.Evaluate(new[] { U.W(Lam) }, nv);
            double p0 = energyJ / (300e-15 * Math.Sqrt(Math.PI / (4 * Math.Log(2))));   // Gaussian pulse peak power
            return (nv[0], mat.N2, p0, Math.PI * nv[0] * SilicaW0 * SilicaW0 / Lam);
        }

        /// <summary>The virial (second-moment) law of the paraxial cubic NLS (Vlasov, Petrishchev and Talanov 1971): for a
        /// collimated Gaussian beam w^2(z) = w0^2 [1 + (1 - P/P_G)(z/zR)^2] exactly, with P_G = lambda^2 / (2 pi n0 n2) (the
        /// moment collapse threshold of a Gaussian, 1.06 x Marburger's). Each time slice obeys it (dispersion length >> 10 mm), so the
        /// energy-weighted radius uses the energy-weighted power sum(P^2)/sum(P) = P0/sqrt 2. The Kerr part of w^2 after 10 mm,
        /// w_lin^2 - w_Kerr^2 = w0^2 (P0/sqrt2/P_G)(L/zR)^2, is compared (1 nJ run as the linear reference). With n2 7.5 % weak it is
        /// 7.5 % low. The reported radius is 1.0 % above the law: it is computed from the profile stored at 48 sampled frequencies
        /// (TransverseProfile.Collapse), whose shapes differ across the SPM-broadened spectrum; the solver itself follows the law to
        /// 1e-5 (<see cref="SpatialKerr_Solver_SecondMomentFollowsTheVirialLaw"/>).</summary>
        [Test(Reference = "Vlasov, Petrishchev & Talanov, Radiophys. Quantum Electron. 14, 1062 (1971): w^2 = w0^2 [1 + (1 - P/P_G)(z/zR)^2]", Tolerance = "2 % (reported radius from the sampled profile)")]
        public void SpatialKerr_SilicaPlate_BeamSizeFollowsTheVirialLaw()
        {
            double L = 10e-3;
            var (n0, n2, p0, zr) = SilicaBeam(4e-6);
            double pG = Lam * Lam / (2 * Math.PI * n0 * n2), pE = p0 / Math.Sqrt(2);
            double wl = SilicaKerrPlate(L, 1e-9, 0).Records[0].BeamRadiusXM, wk = SilicaKerrPlate(L, 4e-6, 0).Records[0].BeamRadiusXM;
            Assert.Close(SilicaW0 * Math.Sqrt(1 + L * L / (zr * zr)), wl, 0.002, 0, "linear radius (Gaussian optics)");
            double expected = SilicaW0 * SilicaW0 * pE / pG * L * L / (zr * zr);
            Assert.Close(expected, wl * wl - wk * wk, 0.02, 0, "Kerr reduction of w^2 at the exit");
        }

        /// <summary>The same law on the radial solver alone (SpatialEnvelopeProblem, constant index 1.45, Kerr only): the
        /// second moment over every (r, omega) sample of the output field. 300 fs at 1030 nm, P0 = 12.5 MW (2.8 P_G), w0 = 100 um,
        /// 10 mm; paraxial and exact k_z, dt = 25 and 5 fs. The law holds per time slice (no dispersion, instantaneous response),
        /// so it is exact up to the step tolerance and the space-time focusing of the omega-dependent diffraction (both below 1e-5).</summary>
        [Test(Reference = "Vlasov, Petrishchev & Talanov (1971) virial law with P_G = lambda^2 / (2 pi n0 n2)", Tolerance = "1e-4")]
        public void SpatialKerr_Solver_SecondMomentFollowsTheVirialLaw()
        {
            const double n0 = 1.45, n2 = 2.6e-20, w = 100e-6, L = 10e-3, p0 = 12.525e6, tau = 300e-15;
            foreach (var (dt, paraxial) in new[] { (25e-15, true), (25e-15, false), (5e-15, true) })
            {
                var grid = FrequencyGrid.Create(512, dt, U.W(Lam));
                var basis = new RadialBasis(96, 0.45e-3);
                int n = grid.N;
                double sig = tau / (2 * Math.Sqrt(2 * Math.Log(2))), i0 = 2 * p0 / (Math.PI * w * w);
                double W2(double n2v)
                {
                    var med = new NonlinearMedium { N2 = n2v, EnableSelfSteepening = false, EnableRaman = false };
                    var E = new Complex[basis.M * n];
                    for (int m = 0; m < basis.M; m++)
                    {
                        var row = new Complex[n];
                        for (int i = 0; i < n; i++) row[i] = Math.Sqrt(i0) * Math.Exp(-basis.Radius[m] * basis.Radius[m] / (w * w)) * Math.Exp(-grid.Time[i] * grid.Time[i] / (4 * sig * sig));
                        Array.Copy(SpectralTransform.TimeToFreq(row), 0, E, m * n, n);
                    }
                    basis.ToK(E, n);
                    var prob = new SpatialEnvelopeProblem(grid, basis, n2v == 0 ? null : med, L, new double[n], Enumerable.Repeat(1.0, n).ToArray(), _ => n0, paraxial, 0, 0, "t");
                    var o = Steppers.Propagate(prob, E, new StepperSettings { Tolerance = 1e-8 }, new StepStatistics());
                    basis.ToSpace(o, n);
                    double s0 = 0, s2 = 0;
                    for (int m = 0; m < basis.M; m++)
                        for (int i = 0; i < n; i++)
                        {
                            var v = o[m * n + i]; double p = basis.Weights[m] * (v.Real * v.Real + v.Imaginary * v.Imaginary);
                            s0 += p; s2 += p * basis.Radius[m] * basis.Radius[m];
                        }
                    return 2 * s2 / s0;
                }
                double lam0 = 2 * Math.PI * C / grid.OmegaCenter, zr = Math.PI * n0 * w * w / lam0, pG = lam0 * lam0 / (2 * Math.PI * n0 * n2);
                double expected = w * w * p0 / Math.Sqrt(2) / pG * L * L / (zr * zr);
                Assert.Close(expected, W2(0) - W2(n2), 1e-4, 0, $"Kerr reduction of w^2 (dt {dt:G2} s, paraxial {paraxial})");
            }
        }

        /// <summary>Thin plate (2 mm, L/zR = 0.045), 1 uJ (P0 = 0.7 P_G), then 2 x 10 mm of vacuum. (1) The reported B-integral is
        /// the on-axis peak k0 n2 I0 zR atan(L/zR), I0 = 2 P0 / (pi w0^2) (diffraction lowers it by 0.07 %, self-focusing raises it by
        /// less). (2) The Kerr lens, as seen by the beam's second moment: a phase B exp(-2r^2/w^2) changes d(w^2)/dz by -2B/k0 in
        /// vacuum (the virial law above gives the same exactly), so the moment focal length is f = 2 w0^2 / (-Delta d(w^2)/dz) =
        /// k0 w0^2 / B_E = pi w0^4 / (sqrt2 n2 P0 L) with the energy-weighted B_E = B/sqrt2 (1.36 m here; the on-axis, paraxial focal
        /// length of the same lens is w^2 / (4 n2 I0 L), 4 sqrt2 times shorter). The slope is taken from w_Kerr^2 - w_lin^2 at 0, 10
        /// and 20 mm after the plate (exactly quadratic in free space).</summary>
        [Test(Reference = "Gaussian-beam Kerr lens: B = k0 n2 I0 zR atan(L/zR); moment focal length f = pi w^4 / (sqrt2 n2 P0 L)", Tolerance = "0.3 % (B), 0.5 % (f)")]
        public void SpatialKerr_ThinSilicaPlate_OnAxisBAndKerrLensFocalLengthMatchGaussianBeamTheory()
        {
            double L = 2e-3, d = 10e-3, eJ = 1e-6;
            var (n0, n2, p0, zr) = SilicaBeam(eJ);
            double k0 = U.W(Lam) / C, i0 = 2 * p0 / (Math.PI * SilicaW0 * SilicaW0);
            var k = SilicaKerrPlate(L, eJ, 2); var l = SilicaKerrPlate(L, 1e-9, 2);
            double bExpected = k0 * n2 * i0 * zr * Math.Atan(L / zr);
            double[] dw2 = Enumerable.Range(0, 3).Select(i => Math.Pow(k.Records[i].BeamRadiusXM, 2) - Math.Pow(l.Records[i].BeamRadiusXM, 2)).ToArray();
            double slope = (4 * dw2[1] - dw2[2] - 3 * dw2[0]) / (2 * d);
            double f = 2 * SilicaW0 * SilicaW0 / -slope, fExpected = Math.PI * Math.Pow(SilicaW0, 4) / (Math.Sqrt(2) * n2 * p0 * L);
            Assert.Close(bExpected, k.Records[0].BIntegralRad, 0.003, 0, "on-axis B-integral");
            Assert.Close(fExpected, f, 0.005, 0, "moment focal length of the Kerr lens");
        }
    }
}
