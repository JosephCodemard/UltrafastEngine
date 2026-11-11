using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Gain;
using UltrafastEngine.Materials;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Session D (WP2): ASE over the inversion lifetime (AseModel) and the four-lobed stress-birefringence map.</summary>
    [TestArea("Inter-pulse ASE (both directions, spectrally resolved) and the stress-birefringence pattern")]
    public class AseStressTests
    {
        const double H = GainMedium.H, C = GainMedium.C;
        private static JsonObject Grid(int n, double window) => O("grid", O("auto", false, "n", n, "windowS", window));

        /// <summary>A medium with flat cross sections (m^2) so the closed forms apply; 1-D crystal of the given mode area.</summary>
        private static GainCrystal FlatCrystal(double se, double sa, double nt, double lengthM, int nz, double modeRadiusM, out GainMedium m)
        {
            m = new GainMedium { Name = "flat", LifetimeS = 1e-3, DopingDensityM3 = nt, EmissionBand = _ => se, AbsorptionBand = _ => sa };
            return new GainCrystal(m, lengthM, nz, new PumpSpec(), modeRadiusM, null, null);
        }

        private static AseModel Model(GainCrystal gc, double[] nu, double dnu, int pol = 2, double n = 1.82)
            => new AseModel(gc, new AseSpec { Enabled = true, Polarizations = pol }, n, 2 * Math.PI * nu[0], nu, dnu);

        [Test(Category = "gain", Reference = "single-mode ASE per polarisation P = n_sp h nu dnu (G - 1), n_sp = sigma_e N2 / (sigma_e N2 - sigma_a N1) (Desurvire, Erbium-doped fiber amplifiers, 1994; Siegman, Lasers, ch. 6)", Tolerance = "1e-12 (uniform inversion: the slice step is exact)")]
        public void Ase_UniformInversion_IsNspHnuDnuGMinusOne_BothEnds_BothPolarisations()
        {
            double se = 2.0e-24, sa = 0.2e-24, nt = 1.4e26, L = 5e-3, w = 0.4e-3, nu = C / 1030e-9, dnu = 1e12, beta = 0.3;
            var gc = FlatCrystal(se, sa, nt, L, 24, w, out _);
            gc.SetInversionFraction(beta);
            double A = gc.CellArea[0], n2 = beta * nt, gam = se * n2 - sa * (nt - n2), G = Math.Exp(gam * L), nsp = se * n2 / gam;
            foreach (int pol in new[] { 1, 2 })
            {
                var o = Model(gc, new[] { nu }, dnu, pol).ModeOutput(gc.N2, gc.Nt, 0, A);
                double expected = pol * nsp * H * nu * dnu * (G - 1);
                Assert.Close(expected, o.ForwardW, 1e-12, 0, "forward, " + pol + " polarisation(s)");
                Assert.Close(expected, o.BackwardW, 1e-12, 0, "backward equals forward for a uniform inversion");
            }
        }

        [Test(Category = "gain", Reference = "dI/dz = gamma(z) I + S(z) integrated with a fine RK4 for a linear inversion profile", Tolerance = "1e-6 (the model holds N2 constant in each of 400 slices, the reference is continuous: O(1/nz^2))")]
        public void Ase_NonUniformInversion_MatchesADirectIntegration_AndForwardDiffersFromBackward()
        {
            double se = 2.0e-24, sa = 0.2e-24, nt = 1.4e26, L = 5e-3, w = 0.4e-3, nu = C / 1030e-9, dnu = 1e12;
            int nz = 400;
            var gc = FlatCrystal(se, sa, nt, L, nz, w, out _);
            for (int j = 0; j < nz; j++) gc.N2[j] = nt * (0.1 + 0.5 * (j + 0.5) / nz);       // beta rises from 0.1 to 0.6 along z
            double A = gc.CellArea[0];
            var o = Model(gc, new[] { nu }, dnu).ModeOutput(gc.N2, gc.Nt, 0, A);
            double N2At(double z) { double b = 0.1 + 0.5 * z / L; return nt * b; }
            double Rhs(double z, double I, double dir) { double n2 = N2At(z); return dir * ((se * n2 - sa * (nt - n2)) * I) + 2 * H * nu * se * n2 / A; }
            double Integrate(int dir)
            {
                int ns = 20000; double dz = L / ns, I = 0;
                for (int s = 0; s < ns; s++)
                {
                    double z = dir > 0 ? s * dz : L - s * dz, h = dir * 0 + dz;
                    // march along the propagation direction: parameter t from the entrance
                    double zz(double t) => dir > 0 ? z + t : z - t;
                    double k1 = Rhs(zz(0), I, 1), k2 = Rhs(zz(h / 2), I + h / 2 * k1, 1), k3 = Rhs(zz(h / 2), I + h / 2 * k2, 1), k4 = Rhs(zz(h), I + h * k3, 1);
                    I += h / 6 * (k1 + 2 * k2 + 2 * k3 + k4);
                }
                return I;
            }
            // the gain gamma is the same for both directions; Rhs with dir = 1 marches along each direction's own path
            double fwd = Integrate(1) * A * dnu, bwd = Integrate(-1) * A * dnu;
            Assert.Close(fwd, o.ForwardW, 1e-6, 0, "forward");
            Assert.Close(bwd, o.BackwardW, 1e-6, 0, "backward");
            Assert.Greater(Math.Abs(o.ForwardW - o.BackwardW) / o.ForwardW, 0.01, "the two ends differ when the inversion is not uniform");
        }

        [Test(Category = "gain", Reference = "Fuchtbauer-Ladenburg: 1 / tau_rad = 8 pi n^2 integral sigma_e(nu) nu^2 / c^2 dnu (Aull and Jenssen, IEEE JQE 18, 925 (1982)); the source into 4 pi, both polarisations, is N2 <h nu> / tau_rad", Tolerance = "1e-9 (quadrature of the same integrals)")]
        public void Ase_SpontaneousSource_IntoFourPi_IsTheRadiativeDecay()
        {
            // a Gaussian emission band, 2 nm wide at 1030 nm
            double nu0 = C / 1030e-9, sigNu = 0.4e12, peak = 2e-24, n = 1.82, nt = 1e26;
            var m = new GainMedium { Name = "g", LifetimeS = 1e-3, DopingDensityM3 = nt, EmissionBand = o => peak * Math.Exp(-0.5 * Math.Pow((o / (2 * Math.PI) - nu0) / sigNu, 2)), AbsorptionBand = _ => 0 };
            var gc = new GainCrystal(m, 1e-3, 4, new PumpSpec(), 1e-4, null, null);
            int K = 4000; double lo = nu0 - 8 * sigNu, dnu = 16 * sigNu / K;
            var nus = Enumerable.Range(0, K).Select(i => lo + (i + 0.5) * dnu).ToArray();
            var ase = new AseModel(gc, new AseSpec { Enabled = true, Polarizations = 2 }, n, 2 * Math.PI * nu0, nus, dnu);
            double n2 = 0.1 * nt, power = 0, invTau = 0, hnuWeighted = 0;
            for (int i = 0; i < K; i++)
            {
                double se = ase.SigmaE[i];
                power += 2 * H * nus[i] * se * n2 * ase.Etendue(nus[i], 4 * Math.PI) * dnu;      // the source term of the model, W / m^3
                double w = 8 * Math.PI * n * n * se * nus[i] * nus[i] / (C * C) * dnu;
                invTau += w; hnuWeighted += w * H * nus[i];
            }
            Assert.Close(n2 * hnuWeighted, power, 1e-9, 0, "source power = N2 sum h nu / tau_rad(nu)");
            Assert.True(invTau > 0, "radiative rate");
        }

        [Test(Category = "gain", Reference = "homogeneously saturated amplifier, dP/dz = g0 P / (1 + P / P_sat): ln(G / G0) + (G - 1) P_in / P_sat = 0 (integrated law; Siegman, Lasers, ch. 7; Saleh and Teich, Fundamentals of Photonics, ch. 15)", Tolerance = "1e-4")]
        public void Ase_SliceStep_IntegratesTheSaturatedAmplifierLaw()
        {
            double g0 = 200, L = 0.02, Pin = 0.3, Psat = 1.0;        // g0 L = 4: G0 = e^4
            double G0 = Math.Exp(g0 * L), lo = 1, hi = G0;
            for (int i = 0; i < 200; i++) { double G = 0.5 * (lo + hi); if (Math.Log(G / G0) + (G - 1) * Pin / Psat > 0) hi = G; else lo = G; }
            double Gref = 0.5 * (lo + hi);
            int ns = 4000; double dz = L / ns, P = Pin;
            for (int s = 0; s < ns; s++)
            {
                double g1 = g0 / (1 + P / Psat), Pm = AseModel.Slice(P, g1, 0, dz / 2, out _), g2 = g0 / (1 + Pm / Psat);
                P = AseModel.Slice(P, g2, 0, dz, out _);
            }
            Assert.Close(Gref, P / Pin, 1e-4, 0, "saturated gain");
            // and the photon balance of one slice: stimulated = out - in - source dz
            double o = AseModel.Slice(0.2, 30, 5, 1e-3, out double stim);
            Assert.Close(o - 0.2 - 5e-3, stim, 1e-15, 1e-15, "stimulated part of a slice");
        }

        [Test(Category = "gain", Reference = "photon balance in the cw steady state: net absorbed pump photons = N2 V / tau + stimulated ASE photons", Tolerance = "0.5 %")]
        public void Ase_CwSteadyState_ClosesThePhotonBalance_AndDepletesTheInversion()
        {
            // an end-pumped Yb:YAG rod; the signal mode (1-D cell) lies well inside the flat-top pump, so the inversion is uniform over the pump area
            double a = 0.4e-3, L = 8e-3, wmode = 0.05 * a;
            var med = GainMedium.Get("Yb:YAG");
            PumpSpec Pump() => new PumpSpec { Kind = "cw", PowerW = 250, WavelengthM = 940e-9, RadiusM = a, Profile = "tophat", Passes = 2 };
            GainCrystal Run(bool ase, out double absorbedW)
            {
                var gc = new GainCrystal(med, L, 24, Pump(), wmode, null, null);
                if (ase) gc.AseDepletion = new AseModel(gc, new AseSpec { Enabled = true, SolidAngleSr = 1.0 }, 1.82, 2 * Math.PI * C / 1030e-9);   // 1 sr: the escape cone of YAG, the upper bound
                gc.PumpCw(12 * med.LifetimeS, 600, false);
                double tt = 0.05 * med.LifetimeS; gc.PumpCw(tt, 20, true);
                absorbedW = gc.AbsorbedPumpJ.Sum() / tt;
                return gc;
            }
            var g0 = Run(false, out double abs0);
            var g1 = Run(true, out double abs1);
            double hvp = H * C / 940e-9, V = Math.PI * a * a * g1.Dz, areaRatio = Math.PI * a * a / g1.CellArea[0];
            double nPhotons = abs1 / hvp, decay = g1.N2.Sum() * V / med.LifetimeS;
            var rates = g1.AseDepletion.Rates(g1.N2, g1.Nt);
            double stim = g1.AseDepletion.LastStimulatedPhotonsPerS * areaRatio;     // the column of the mode stands for the whole pump area
            Assert.Greater(stim / nPhotons, 0.02, "ASE matters in this case (fraction of the absorbed pump photons)");
            double decay0 = g0.N2.Sum() * V / med.LifetimeS;
            Assert.Close(abs0 / hvp, decay0, 5e-3, 0, "photon balance without ASE");
            Assert.Close(nPhotons, decay + stim, 5e-3, 0, "photon balance with ASE");
            Assert.Less(g1.N2.Sum(), 0.97 * g0.N2.Sum(), "ASE lowers the stored inversion");
            Assert.Greater(abs1, abs0, "less bleaching of the pump with a lower inversion");
        }

        [Test(Category = "gain", Reference = "limits: no solid angle gives no depletion; the depletion grows with the solid angle", Tolerance = "1e-6")]
        public void Ase_Depletion_VanishesWithTheSolidAngle_AndGrowsWithIt()
        {
            double a = 0.4e-3, L = 8e-3; var med = GainMedium.Get("Yb:YAG");
            double Stored(double sr)
            {
                var pump = new PumpSpec { Kind = "cw", PowerW = 250, WavelengthM = 940e-9, RadiusM = a, Profile = "tophat", Passes = 2 };
                var gc = new GainCrystal(med, L, 16, pump, a * Math.Sqrt(2), null, null);
                if (sr >= 0) gc.AseDepletion = new AseModel(gc, new AseSpec { Enabled = true, SolidAngleSr = sr }, 1.82, 2 * Math.PI * C / 1030e-9);
                gc.PumpCw(12 * med.LifetimeS, 400, false);
                return gc.N2.Sum();
            }
            double none = Stored(-1), tiny = Stored(1e-12), small = Stored(0.01), large = Stored(0.3);
            Assert.Close(none, tiny, 1e-6, 0, "vanishing solid angle");
            Assert.Less(small, none, "depletes"); Assert.Less(large, small, "more with a larger angle");
        }

        [Test(Category = "gain", Reference = "the element reports the ASE leaving the signal mode and the depletion; the energy budget of the pulse still closes", Tolerance = "1e-6")]
        public void AseModel_OnAnAmplifier_ReportsThePedestal_AndTheBudgetCloses()
        {
            RunResult Amp(bool ase) => Go(Sys(O("id", "amp", "type", "gain", "medium", "Yb:YAG", "lengthM", 0.008, "slices", 16, "modeRadiusM", 0.4e-3 * Math.Sqrt(2), "spm", false,
                            "pump", O("kind", "cw", "powerW", 250, "wavelengthM", 940e-9, "radiusM", 0.4e-3, "profile", "tophat", "passes", 2), "thermalLens", false,
                            "repRateHz", 1e5, "aseModel", ase)), Spec(Gauss(1030e-9, 1e-12, 1e-8), Grid(1024, 40e-12)));
            var r0 = Amp(false); var r1 = Amp(true);
            var v = r1.Records[0].Values;
            Assert.Greater(v["aseForwardPowerW"], 0, "forward ASE in the mode"); Assert.Greater(v["aseBackwardPowerW"], 0, "backward ASE");
            Assert.Greater(v["aseDepletionFraction"], 0, "depletion"); Assert.Less(v["aseDepletionFraction"], 1, "less than the absorbed pump");
            Assert.Greater(v["asePedestalEnergyFraction"], 0, "pedestal strength");
            Assert.Less(r1.Records[0].Values["inversionFractionBefore"], r0.Records[0].Values["inversionFractionBefore"], "ASE lowers the inversion the pulse sees");
            Assert.Less(r1.Output.TotalEnergyJ(), r0.Output.TotalEnergyJ(), "and the extracted energy");
            Assert.False(r0.Records[0].Values.ContainsKey("aseForwardPowerW"), "off by default");
            Assert.Close(r1.Input.TotalEnergyJ() - r1.Output.TotalEnergyJ(), r1.Records[0].LossJ.Values.Sum(), 1e-6, 1e-21, "budget closes");
        }

        [Test(Category = "gain", Reference = "Koechner, Solid-State Laser Engineering, ch. 7: the beam average of the four-lobed pattern, D = (1 - <cos delta>) / 4, delta = C r^2 / r0^2", Tolerance = "1e-6 (quadrature on the grid; the Gaussian is well inside the grid)")]
        public void StressPattern_BeamAverage_EqualsTheLumpedDepolarisation_AndHasFourLobes()
        {
            double w = 0.8e-3, r0 = 3e-3, Cst = 3.0; int N = 256; double half = 4.5e-3;
            var basis = new CartesianBasis(N, N, half, half); int M = basis.M;
            var ex = new Complex[M]; var ey = new Complex[M];
            for (int m = 0; m < M; m++) ex[m] = Math.Exp(-(basis.X[m] * basis.X[m] + basis.Y[m] * basis.Y[m]) / (w * w));
            double e0 = ex.Sum(z => z.Real * z.Real);
            GainElement.ApplyStressPattern(ex, ey, 1, basis, Cst, r0);
            double exE = ex.Sum(z => z.Real * z.Real + z.Imaginary * z.Imaginary), eyE = ey.Sum(z => z.Real * z.Real + z.Imaginary * z.Imaginary);
            Assert.Close(e0, exE + eyE, 1e-13, 0, "unitary: energy kept");
            Assert.Close(GainElement.StressDepolarization(Cst, w, r0), eyE / (exE + eyE), 1e-6, 0, "beam average");
            // lobes: no y on the axes, the maximum on the diagonals, the sign alternates from quadrant to quadrant
            double OnAxis = 0, Diag = 0; int iq1 = -1, iq2 = -1;
            for (int m = 0; m < M; m++)
            {
                double x = basis.X[m], y = basis.Y[m];
                if (Math.Abs(y) < 1e-9 && Math.Abs(x) > 0 && Math.Abs(x) < 1.5e-3) OnAxis = Math.Max(OnAxis, ey[m].Magnitude);
                if (Math.Abs(x - y) < 1e-9 && x > 0.5e-3 && x < 1.5e-3) Diag = Math.Max(Diag, ey[m].Magnitude);
                if (iq1 < 0 && x > 0.5e-3 && y > 0.5e-3 && Math.Abs(x - y) < basis.Dx * 0.6) iq1 = m;
                if (iq2 < 0 && x < -0.5e-3 && y > 0.5e-3 && Math.Abs(x + y) < basis.Dx * 0.6) iq2 = m;
            }
            Assert.Less(OnAxis, 1e-9 * Diag, "no depolarised light on the x axis (E along a principal axis)");
            Assert.Greater(Diag, 1e-3, "four lobes on the diagonals");
            Assert.Less((ey[iq1] * Complex.Conjugate(ey[iq2])).Real, 0, "adjacent lobes have opposite sign");
        }

        [Test(Category = "gain", Reference = "the pattern on a cartesian grid carries the same beam-averaged loss as the lumped depolarisation reported by the element", Tolerance = "5 % at the exit radius (the gain is not uniform over the beam)")]
        public void StressPattern_InAGainElement_AgreesWithTheLumpedValue_AndTheProfileCarriesY()
        {
            RunResult Run(string model) => Go(Sys(O("id", "g", "type", "gain", "medium", "Nd:YAG", "lengthM", 0.02, "slices", 6, "beam", O("kind", "gaussian", "w0M", 0.5e-3), "spm", false,
                            "pump", O("kind", "cw", "powerW", 400, "wavelengthM", 808e-9, "radiusM", 1.5e-3, "profile", "tophat"),
                            "thermalLens", O("rodRadiusM", 2.5e-3, "heatFraction", 0.3, "stress", true),
                            "transverse", model == "cartesian" ? O("model", "cartesian", "nx", 64, "ny", 64, "halfWidthM", 2.5e-3) : O("model", "radial", "points", 48, "radiusM", 2.5e-3))),
                            Spec(Gauss(1064e-9, 1e-10, 1e-9)));
            var r = Run("cartesian"); var v = r.Records[0].Values;
            Assert.True(v.ContainsKey("depolarizationFractionPattern"), "pattern applied");
            // the lumped value uses the beam radius at the middle of the rod, the pattern the beam at the exit (the thermal lens has changed it):
            // D scales as w^4 for a small beam, so compare with the lumped formula at the radius of the output beam
            var rec = r.Records[0]; double wOut = rec.BeamRadiusXM;
            double dOut = GainElement.StressDepolarization(v["stressRetardationRad"], wOut, 2.5e-3);
            Assert.Close(dOut, v["depolarizationFractionPattern"], 0.05, 0, "pattern average against the lumped formula at the exit radius");
            Assert.Close(v["depolarizationFraction"], v["depolarizationFractionPattern"], 0.35, 0, "and the mid-rod lumped value is the same order");
            double ey = r.Output.Ay.Sum(z => z.Real * z.Real + z.Imaginary * z.Imaginary), ex = r.Output.Ax.Sum(z => z.Real * z.Real + z.Imaginary * z.Imaginary);
            Assert.Close(v["depolarizationFractionPattern"], ey / (ex + ey), 1e-6, 0, "energy in y");
            Assert.True(r.Output.Profile != null && r.Output.Profile.DataY != null, "the carried profile has its own y shape");
            var rad = Run("radial"); // no 2-D basis: the lumped rotation, as before
            Assert.False(rad.Records[0].Values.ContainsKey("depolarizationFractionPattern"), "radial grid: lumped");
            Assert.Close(rad.Records[0].Values["depolarizationFraction"], v["depolarizationFraction"], 0.02, 0, "same lumped number (the radius at mid-rod differs slightly between the two bases)");
        }
    }
}
