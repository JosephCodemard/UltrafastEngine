using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Gain;
using UltrafastEngine.Materials;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Resonators, partial coherence, thermal stress and the amplifier's thermal lens on the tracked beam (T2).</summary>
    [TestArea("Resonators, partial coherence, thermal stress birefringence, amplifier thermal lens")]
    public class CavityAndCoherenceTests
    {
        private const double Lam = 1030e-9;
        private static ElementRecord Rec(RunResult r, string id) => r.Records.First(x => x.Id == id);
        private static RunSpec Pulse(JsonObject extra = null) => Spec(Gauss(Lam, 1e-12, 1e-6), extra);

        [Test(Reference = "ABCD eigenmode of a periodic lens guide: w0^2 = (lambda / pi) sqrt(L (4 f - L)) / 2 at the waist between lenses (Kogelnik & Li, Appl. Opt. 5, 1550 (1966))", Tolerance = "1e-3")]
        public void Resonator_LensGuide_HasTheAbcdEigenmode_AndPutsItOnTheTrackedBeam()
        {
            double f = 0.5, L = 0.6;
            // ring: half gap, lens, half gap: the reference plane is the waist
            var sys = Sys(O("id", "cav", "type", "resonator", "elements", A(O("id", "a", "type", "gap", "lengthM", L / 2, "material", "Vacuum"), O("id", "l", "type", "lens", "focalLengthM", f, "medium", "Vacuum"), O("id", "b", "type", "gap", "lengthM", L / 2, "material", "Vacuum"))),
                          O("id", "after", "type", "gap", "lengthM", 0.0, "material", "Vacuum"));
            var r = Go(sys, Pulse(O("inputBeam", O("kind", "gaussian", "w0M", 1e-3))));
            var v = Rec(r, "cav").Values;
            Assert.Close(1 - L / (2 * f), v["stabilityX"], 1e-9, 0, "(A + D) / 2");
            double w02 = Lam / Math.PI * Math.Sqrt(L * (4 * f - L)) / 2;
            Assert.Close(Math.Sqrt(w02), v["modeRadiusXM"], 1e-3, 0, "waist radius");
            Assert.Close(Math.Sqrt(w02), Rec(r, "after").Values["trackedBeamRadiusXM"], 1e-3, 0, "tracked beam takes the eigenmode");
            Assert.Close(2.99792458e8 / L, v["freeSpectralRangeHz"], 1e-9, 0, "FSR c / L");
        }

        [Test(Reference = "stability |(A + D) / 2| < 1: L > 4 f is unstable")]
        public void Resonator_Unstable_WarnsAndHasNoMode()
        {
            var sys = Sys(O("id", "cav", "type", "resonator", "elements", A(O("id", "a", "type", "gap", "lengthM", 3.0, "material", "Vacuum"), O("id", "l", "type", "lens", "focalLengthM", 0.5, "medium", "Vacuum"))));
            var r = Go(sys, Pulse());
            Assert.True(r.Warnings.Any(w => w.Contains("unstable")), "warning");
            Assert.Close(0, Rec(r, "cav").Values["stable"], 0, 0, "flag");
        }

        [Test(Reference = "two-mirror cavity: (A + D) / 2 = 2 g1 g2 - 1, g = 1 - L / R; w^2 at a mirror of a symmetric cavity = (lambda L / pi) sqrt(1 / (1 - g^2))", Tolerance = "1e-3")]
        public void Resonator_LinearTwoMirrorCavity_MatchesTheMirrorFormulas()
        {
            double R = 1.5, L = 1.0, g = 1 - L / R;
            // each end mirror acts once per round trip, as a lens of f = R / 2; the reference plane is at the first mirror, after its bounce
            var sys = Sys(O("id", "cav", "type", "resonator", "geometry", "linear", "elements", A(O("id", "m1", "type", "lens", "focalLengthM", R / 2, "medium", "Vacuum"), O("id", "g", "type", "gap", "lengthM", L, "material", "Vacuum"), O("id", "m2", "type", "lens", "focalLengthM", R / 2, "medium", "Vacuum"))));
            var v = Rec(Go(sys, Pulse()), "cav").Values;
            Assert.Close(2 * g * g - 1, v["stabilityX"], 1e-9, 0, "(A + D) / 2");
            Assert.Close(Math.Sqrt(Lam * L / Math.PI * Math.Sqrt(1 / (1 - g * g))), v["modeRadiusXM"], 1e-3, 0, "radius on the mirror");
            Assert.Close(2 * L, v["roundTripOpticalLengthM"], 1e-9, 0, "round trip 2 L");
        }

        [Test(Reference = "cross-model: Fox-Li wave iteration vs the ABCD eigenmode", Tolerance = "3 %")]
        public void Resonator_FoxLi_ConvergesToTheGaussianEigenmode()
        {
            double f = 0.5, L = 0.6, w = Math.Sqrt(Lam / Math.PI * Math.Sqrt(L * (4 * f - L)) / 2);
            var t = O("model", "radial", "points", 128, "radiusM", 6 * w, "spectralSamples", 1);
            var sys = Sys(O("id", "cav", "type", "resonator", "transverse", t, "roundTrips", O("max", 600, "tolerance", 1e-7), "elements", A(O("id", "a", "type", "gap", "lengthM", L / 2, "material", "Vacuum"), O("id", "l", "type", "lens", "focalLengthM", f, "medium", "Vacuum"), O("id", "ap", "type", "aperture", "radiusM", 2.5 * w), O("id", "b", "type", "gap", "lengthM", L / 2, "material", "Vacuum"))));
            var r = Go(sys, Pulse());
            Assert.Close(1, Rec(r, "cav").Values["converged"], 0, 0, "converged");
            Assert.Close(w, Rec(r, "cav").Values["foxLiRadiusXM"], 0.03, 0, "mode radius");
            Assert.Less(Rec(r, "cav").Values["roundTripLoss"], 1e-3, "a large aperture loses little");
        }

        private static double PlaneMirrorLoss(double fresnel)
        {
            double L = 1.0, a = Math.Sqrt(fresnel * Lam * L);
            var t = O("model", "radial", "points", 512, "radiusM", 8 * a, "spectralSamples", 1, "absorbingBoundary", true);
            var sys = Sys(O("id", "cav", "type", "resonator", "transverse", t, "roundTrips", O("max", 1500, "tolerance", 1e-8), "elements", A(O("id", "ap", "type", "aperture", "radiusM", a), O("id", "g", "type", "gap", "lengthM", L, "material", "Vacuum"))));
            var r = Go(sys, Pulse());
            Assert.Close(1, Rec(r, "cav").Values["converged"], 0, 0, "converged at N = " + fresnel);
            return Rec(r, "cav").Values["roundTripLoss"];
        }

        [Test(Slow = true, Reference = "Fox & Li (1961), Vainshtein: loss per transit of plane circular mirrors falls as N^-3/2 at large N; loss near 20 % at N = 1", Tolerance = "25 %")]
        public void Resonator_FoxLi_PlaneMirrors_LossFallsAsNToTheMinusThreeHalves()
        {
            double l1 = PlaneMirrorLoss(1), l10 = PlaneMirrorLoss(10), l20 = PlaneMirrorLoss(20);
            Assert.Less(l10, l1, "loss falls with N");
            Assert.Greater(l1, 0.12, "N = 1"); Assert.Less(l1, 0.28, "N = 1");
            Assert.Close(Math.Pow(2, 1.5), l10 / l20, 0.25, 0, "N^-3/2 between N = 10 and 20");
        }

        [Test(Reference = "Gaussian-Schell beam: w(z) = w0 sqrt(1 + (M^2 z / z_R)^2) (Starikov & Wolf 1982; Siegman M^2 law)", Tolerance = "2 %")]
        public void PartialCoherence_GaussianSchellBeam_SpreadsWithItsM2()
        {
            double w0 = 0.5e-3, m2 = 2.5, zr = Math.PI * w0 * w0 / Lam, z = zr;
            var t = O("model", "cartesian", "nx", 512, "ny", 1, "halfWidthM", 8e-3, "spectralSamples", 2);
            var src = (JsonObject)JsonValue.Parse(t.ToJson(false)); src.Set("initialProfile", O("kind", "gaussian", "radiusM", w0));
            var sys = Sys(O("id", "s", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", src), O("id", "z", "type", "gap", "lengthM", z, "material", "Vacuum", "transverse", t));
            var r = Go(sys, Pulse(O("coherence", O("m2", m2, "modes", 40), "instruments", A(O("kind", "beamProfile", "name", "bp", "at", "z")))));
            Assert.Close(w0, Rec(r, "s").BeamRadiusXM, 0.02, 0, "waist radius");
            Assert.Close(w0 * Math.Sqrt(1 + m2 * m2), Rec(r, "z").BeamRadiusXM, 0.02, 0, "w(z_R)");
            Assert.Close(w0 * Math.Sqrt(1 + m2 * m2), r.Instruments.First(i => i.Name == "bp").Values.GetDouble("radiusXM", 0), 0.02, 0, "instrument");
            var c = r.Instruments.First(i => i.Name == "coherence");
            Assert.Greater(c.Values.GetDouble("capturedWeight", 0), 0.999, "the series is captured");
            Assert.Close(1e-6, Rec(r, "z").EnergyOutJ, 0.002, 0, "energies add");
        }

        [Test]
        public void PartialCoherence_M2OfOne_IsTheCoherentBeam_AndNeedsACartesianGrid()
        {
            double w0 = 0.5e-3;
            var t = O("model", "cartesian", "nx", 128, "ny", 1, "halfWidthM", 4e-3, "spectralSamples", 2, "initialProfile", O("kind", "gaussian", "radiusM", w0));
            var r = Go(Sys(O("id", "s", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", t)), Pulse(O("coherence", O("m2", 1.0))));
            Assert.Close(w0, Rec(r, "s").BeamRadiusXM, 0.01, 0, "radius");
            var rad = O("model", "radial", "points", 64, "radiusM", 4e-3, "initialProfile", O("kind", "gaussian", "radiusM", w0));
            Assert.Throws<JsonException>(() => Go(Sys(O("id", "s", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", rad)), Pulse(O("coherence", O("m2", 2.0)))), "radial refused");
        }

        [Test(Reference = "Koechner, Solid-State Laser Engineering, ch. 7: D = (1 - sin C / C) / 4, C = n0^3 alpha C_B P_h / (lambda K)", Tolerance = "2 %")]
        public void ThermalStress_DepolarisedFraction_MatchesKoechner_AndTendsToAQuarter()
        {
            double? last = null;
            foreach (double power in new[] { 5.0, 40.0, 400.0 })
            {
                var sys = Sys(O("id", "g", "type", "gain", "medium", "Nd:YAG", "lengthM", 0.02, "slices", 6, "modeRadiusM", 60e-3, "spm", false,
                                "pump", O("kind", "cw", "powerW", power, "wavelengthM", 808e-9, "radiusM", 1.5e-3, "profile", "tophat"),
                                "thermalLens", O("rodRadiusM", 2e-3, "heatFraction", 0.3, "stress", true)));
                var r = Go(sys, Spec(Gauss(1064e-9, 1e-10, 1e-9)));
                var v = Rec(r, "g").Values; var m = GainMedium.Get("Nd:YAG");
                double n0 = MaterialLibrary.BuiltIn("YAG").IndexAt(W(1064e-9));
                double C = n0 * n0 * n0 * m.ThermalExpansionPerK * (m.StressCr - m.StressCphi) * v["heatW"] / (1064e-9 * m.ThermalConductivity);
                double D = (1 - Math.Sin(C) / C) / 4;
                // the mode is far wider than the rod here, which is the filled rod of Koechner's formula
                Assert.Close(D, v["depolarizationFraction"], 0.02, 0, "fraction at " + power + " W");
                double ey = r.Output.Ay.Sum(z => z.Real * z.Real + z.Imaginary * z.Imaginary), ex = r.Output.Ax.Sum(z => z.Real * z.Real + z.Imaginary * z.Imaginary);
                Assert.Close(v["depolarizationFraction"], ey / (ex + ey), 1e-6, 0, "energy moved to y");
                Assert.Less(v["stressFocalLengthRadialM"], v["stressFocalLengthTangentialM"], "bifocal: C_r > C_phi gives the shorter radial lens");
                Assert.Less(v["depolarizationFraction"], 0.3, "bounded");
                last = v["depolarizationFraction"];
            }
            Assert.Close(0.25, last.Value, 0.2, 0, "tends to 1/4");
            var ti = Sys(O("id", "g", "type", "gain", "medium", "Ti:sapphire", "lengthM", 0.01, "slices", 4, "modeRadiusM", 1e-3, "pump", O("kind", "cw", "powerW", 10, "wavelengthM", 532e-9, "radiusM", 1e-3, "profile", "tophat"), "thermalLens", O("rodRadiusM", 2e-3, "stress", true)));
            Assert.True(Go(ti, Spec(Gauss(800e-9, 1e-10, 1e-9))).Warnings.Any(w => w.Contains("stress coefficients")), "uniaxial host: no coefficients");
        }

        [Test(Reference = "N thin lenses in contact: 1 / f_total = N / f; Gaussian lens law (Self 1983)", Tolerance = "0.3 %")]
        public void AmplifierThermalLens_OnRequest_ActsOncePerPass()
        {
            double w0 = 1e-3, f = 1.5, zr = Math.PI * w0 * w0 / Lam, ft = f / 3, L = 1e-4;
            double d = ft / (1 + Math.Pow(ft / zr, 2)), w1 = w0 / Math.Sqrt(1 + Math.Pow(zr / ft, 2));
            JsonObject Amp(bool track) => O("id", "a", "type", "amplifier", "geometry", "multipass", "passes", 3, "medium", "Yb:YAG", "lengthM", L, "inversion", O("fraction", 0.0), "spm", false,
                                            "thermalLens", track ? O("focalLengthM", f, "track", true) : O("focalLengthM", f));
            var with = Go(Sys(Amp(true), O("id", "gap", "type", "gap", "lengthM", d, "material", "Vacuum")), Spec(Gauss(Lam, 2e-12, 1e-9), O("inputBeam", O("kind", "gaussian", "w0M", w0))));
            Assert.Close(w1, Rec(with, "gap").Values["trackedBeamRadiusXM"], 0.003, 0, "three lenses in contact");
            var without = Go(Sys(Amp(false), O("id", "gap", "type", "gap", "lengthM", d, "material", "Vacuum")), Spec(Gauss(Lam, 2e-12, 1e-9), O("inputBeam", O("kind", "gaussian", "w0M", w0))));
            Assert.Greater(Rec(without, "gap").Values["trackedBeamRadiusXM"], 0.9 * w0, "off unless asked: the path between passes is not known");
        }
    }
}
