using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Gain;
using UltrafastEngine.Numerics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Regression tests for what the physics review of 2026-10-30 found in the tracker, resonator, stress and coherence code.</summary>
    [TestArea("Physics review 2026-10-30: dispersed rays in glass, reversed cavity passes, stress depolarisation, mode truncation")]
    public class PhysicsReviewTests
    {
        private const double Lam = 1030e-9, Lpm = 1200;
        private static double Period => 1e-3 / Lpm;
        private static double Beta => Math.Asin(Lam / Period - Math.Sin(Math.PI / 4));
        private static RunSpec Tracked(double w) => Spec(Gauss(Lam, 100e-15, 1e-6), O("inputBeam", O("kind", "gaussian", "w0M", w)));
        private static JsonObject G1() => O("id", "g1", "type", "grating", "aoiDeg", -45.0, "linesPerMm", Lpm, "order", 1);
        private static JsonObject G2() => O("id", "g2", "type", "grating", "aoiDeg", Beta * 180 / Math.PI, "linesPerMm", Lpm, "order", -1);

        [Test(Reference = "paraxial reduced distance: a glass path L acts as L / n for angular dispersion", Tolerance = "1 %")]
        public void DispersedBeamInGlass_ActsAsTheReducedDistance()
        {
            double L = 10e-3, n = 1.45;
            var glass = Sys(G1(), O("id", "g", "type", "bulk", "material", "flat", "lengthM", L), G2());
            glass.Materials.Set("flat", FlatMaterial(0, n));
            var vac = Sys(G1(), O("id", "g", "type", "gap", "material", "Vacuum", "lengthM", L / n), G2());
            Assert.Close(Go(vac, Tracked(2e-3)).FinalSystem.GddS2, Go(glass, Tracked(2e-3)).FinalSystem.GddS2, 0.01, 0, "GDD of the pair");
        }

        [Test(Reference = "Linear-cavity round trip with element matrices reversed on the return (A, B, C, D) -> (D, B, C, A)", Tolerance = "0.3 %")]
        public void LinearCavity_ReturnPass_ReversesAnAnamorphicElement()
        {
            double f = 0.3, L1 = 0.2, L2 = 0.25, a = Math.Cos(Beta) / Math.Cos(Math.PI / 4);
            var sys = Sys(O("id", "c", "type", "resonator", "geometry", "linear", "elements", A(
                O("id", "l", "type", "lens", "focalLengthM", f), O("id", "a", "type", "gap", "material", "Vacuum", "lengthM", L1), G1(),
                O("id", "b", "type", "gap", "material", "Vacuum", "lengthM", L2), O("id", "m", "type", "lens", "focalLengthM", 1e30))));
            double[] Mul(double[] x, double[] y) => new[] { x[0] * y[0] + x[1] * y[2], x[0] * y[1] + x[1] * y[3], x[2] * y[0] + x[3] * y[2], x[2] * y[1] + x[3] * y[3] };
            double[] Gap(double d) => new[] { 1, d, 0, 1.0 };
            double[] lens = { 1, 0, -1 / f, 1.0 }, gr = { a, 0, 0, 1 / a }, back = { 1 / a, 0, 0, a };
            // applied in order: lens, L1, grating, L2, end, L2, grating back, L1
            var m = lens;
            foreach (var step in new[] { Gap(L1), gr, Gap(L2), Gap(L2), back, Gap(L1) }) m = Mul(step, m);
            var v = Go(sys, Tracked(1e-3)).Records[0].Values;
            // the wrong (unreversed) return gives (A + D) / 2 = -0.87 here and the sagittal plane -0.50, against -0.245. The tolerance
            // allows for the grating being in air: lambda in air shifts a^2 by about 5e-4.
            bool xIsTangential = Math.Abs(v["stabilityX"] - (m[0] + m[3]) / 2) < Math.Abs(v["stabilityY"] - (m[0] + m[3]) / 2);
            Assert.Close((m[0] + m[3]) / 2, xIsTangential ? v["stabilityX"] : v["stabilityY"], 0.003, 0, "(A + D) / 2 in the plane of incidence");
        }

        [Test(Reference = "gaussian average: D = x^2 / (1 + x^2) / 4, x = C w^2 / (2 r0^2); filled rod (1 - sin C / C) / 4 (Koechner)", Tolerance = "1e-6")]
        public void StressDepolarization_DependsOnTheBeam()
        {
            double C = 6, r0 = 3e-3, w = 1e-3, x = C * w * w / (2 * r0 * r0);
            Assert.Close(x * x / (1 + x * x) / 4, GainElement.StressDepolarization(C, w, r0), 0.02, 0, "small beam");
            Assert.Close((1 - Math.Sin(C) / C) / 4, GainElement.StressDepolarization(C, 1e3 * r0, r0), 1e-6, 0, "beam much wider than the rod");
        }

        [Test]
        public void FoxLi_WithNoAperture_DoesNotClaimAMode()
        {
            // nothing selects a mode: a mismatched start keeps oscillating, with the loss already zero
            double w = 0.2e-3;
            var t = O("model", "radial", "points", 128, "radiusM", 8e-3, "spectralSamples", 1);
            var sys = Sys(O("id", "cav", "type", "resonator", "transverse", t, "roundTrips", O("max", 30, "tolerance", 1e-9), "elements", A(
                O("id", "a", "type", "gap", "lengthM", 0.3, "material", "Vacuum"), O("id", "l", "type", "lens", "focalLengthM", 0.5), O("id", "b", "type", "gap", "lengthM", 0.3, "material", "Vacuum"))));
            var src = Spec(Gauss(Lam, 1e-12, 1e-6), O("inputBeam", O("kind", "gaussian", "w0M", w)));
            var r = Go(sys, src);
            Assert.Close(0, r.Records[0].Values["converged"], 0, 0, "no convergence is claimed");
        }

        [Test]
        public void Coherence_TooFewModes_Warns_AndKeepsTheEnergy()
        {
            double w0 = 0.5e-3;
            var grid = O("model", "cartesian", "nx", 256, "ny", 1, "halfWidthM", 12e-3, "spectralSamples", 2);
            var src = (JsonObject)JsonValue.Parse(grid.ToJson(false)); src.Set("initialProfile", O("kind", "gaussian", "radiusM", w0));
            var sys = Sys(O("id", "s", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", src));
            var run = Spec(O("shape", "cw", "centerWavelengthM", Lam, "powerW", 1.0), O("coherence", O("m2", 10.0, "modes", 12)));
            var r = Go(sys, run);
            Assert.True(r.Warnings.Any(x => x.Contains("raise \"modes\"")), "warned");
            Assert.Close(1.0, r.Records[0].EnergyOutJ / (1.0 * 1e-9), 1e-6, 0, "the energy is the source's");
        }

        [Test]
        public void ProfileMode_WithoutACarriedProfile_Warns()
        {
            var r = Go(Sys(G1()), Spec(Gauss(Lam, 100e-15, 1e-6), O("inputBeam", O("kind", "gaussian", "w0M", 2e-3), "physics", O("spatioTemporal", "profile"))));
            Assert.True(r.Warnings.Any(x => x.Contains("needs a carried transverse profile")), "warned");
        }

        [Test(Reference = "Snell's law at both faces, exit-angle dispersion by finite difference", Tolerance = "1 %")]
        public void ProfileMode_APrismTiltsTheProfile_ByItsExitAngle()
        {
            var sys = Sys(O("id", "p", "type", "prism", "material", "FusedSilica", "apexDeg", 60.0, "aoiDeg", 50.0, "pathM", 5e-3));
            var cr = Compiler.Compile(sys, Spec(Gauss(Lam, 100e-15, 1e-6), O("inputBeam", O("kind", "gaussian", "w0M", 2e-3), "physics", O("spatioTemporal", "profile"))));
            var surf = cr.Elements[0].Active as Optics.DispersiveSurfaceElement;
            Assert.True(surf != null, "the prism has a dispersive surface");
            double w0 = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / Lam;
            Assert.Close(0, surf.TiltWavenumber(w0), 0, 1e-3 * w0 / PhysicalConstants.SpeedOfLightMPerS, "no tilt at the carrier");
            Assert.Greater(Math.Abs(surf.TiltWavenumber(1.01 * w0)), 1.0, "tilt away from the carrier");
        }

        [Test]
        public void CurvedBeam_ActsOnTheTrackedSpatialChirp_WithTheProfilesSign()
        {
            // a beam dispersed by a grating, far past its Rayleigh range: the curvature term must cancel the tilt from F, not double it
            var near = Go(Sys(G1(), O("id", "g", "type", "gap", "material", "Vacuum", "lengthM", 1e-3)), Tracked(0.2e-3));
            var far = Go(Sys(G1(), O("id", "g", "type", "gap", "material", "Vacuum", "lengthM", 50.0)), Tracked(0.2e-3));
            double Tilt(RunResult r) => r.Records.Last().Values.First(kv => kv.Key.StartsWith("trackedPulseFrontTilt") && kv.Key.EndsWith("SPerM") && kv.Value != 0).Value;
            Assert.Less(Math.Abs(Tilt(far)), 0.2 * Math.Abs(Tilt(near)), "the far-field tilt falls, near " + Tilt(near) + ", far " + Tilt(far));
        }
    }
}
