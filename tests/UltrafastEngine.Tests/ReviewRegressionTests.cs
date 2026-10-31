// ReviewRegressionTests.cs -- bugs found by the T0-T2 review, each with the known answer that exposed it.
// Beam quality and second moments of tilted, decentred and higher-order beams; the budget of Branched systems with separate
// packets; steady-state loops. The cross-checks that found them are python/tests/test_lightpipes.py and test_pynlo_branched.py.

using System;
using System.Linq;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Regressions from the review: M^2, second moments, packets at ports, steady-state loops")]
    public class ReviewRegressionTests
    {
        private const double Lam = 1030e-9, W = 1e-3;

        private static (double M2X, double M2Y, double Wx, double Wy) Beam(JsonObject profile, int n = 256, double z = 0.5)
        {
            JsonObject T(JsonObject p) { var t = O("model", "cartesian", "nx", n, "ny", n, "halfWidthM", 8e-3, "spectralSamples", 2); if (p != null) t.Set("initialProfile", p); return t; }
            var sys = Sys(O("id", "s", "type", "gap", "material", "Vacuum", "lengthM", 0.0, "transverse", T(profile)), O("id", "e", "type", "gap", "material", "Vacuum", "lengthM", z, "transverse", T(null)));
            var r = Spec(Gauss(Lam, 1e-12, 1e-3), O("instruments", A(O("kind", "beamProfile", "name", "p", "at", "end"))));
            var v = Go(sys, r).Instruments.First(i => i.Name == "p").Values;
            return (v["m2"].AsDouble(), v["m2Y"].AsDouble(), v["radiusXM"].AsDouble(), v["radiusYM"].AsDouble());
        }

        private static double Gaussian(double z) => W * Math.Sqrt(1 + Math.Pow(z * Lam / (Math.PI * W * W), 2));

        [Test(Reference = "Siegman, Proc. SPIE 1224, 2 (1990): M^2 from central moments", Tolerance = "1e-6")]
        public void TiltAndDecentre_DoNotDegradeBeamQualityOrSize()
        {
            foreach (var p in new[] { O("kind", "gaussian", "radiusM", W, "tiltXRad", 1e-3), O("kind", "gaussian", "radiusM", W, "decenterXM", 1e-3),
                                      O("kind", "gaussian", "radiusM", W, "decenterXM", 1e-3, "tiltXRad", 1e-3, "tiltYRad", -5e-4) })
            {
                var b = Beam(p);
                Assert.Close(1, b.M2X, 1e-6, 0, "M2x"); Assert.Close(1, b.M2Y, 1e-6, 0, "M2y");
                Assert.Close(Gaussian(0.5), b.Wx, 1e-5, 0, "wx");
            }
        }

        [Test(Reference = "Hermite-Gauss M^2 = 2m + 1", Tolerance = "1e-6")]
        public void HermiteGauss_HasItsAnalyticBeamQualityOnEachAxis()
        {
            var b = Beam(O("kind", "hermiteGauss", "radiusM", W, "modeX", 3, "modeY", 1));
            Assert.Close(7, b.M2X, 1e-6, 0, "x"); Assert.Close(3, b.M2Y, 1e-6, 0, "y");
        }

        [Test(Reference = "Laguerre-Gauss M^2 = 2p + |l| + 1", Tolerance = "1e-6")]
        public void Vortex_HasItsAnalyticBeamQuality()
        {
            foreach (var (p, l) in new[] { (0, 1), (1, 2), (2, 1) })
                Assert.Close(2 * p + l + 1, Beam(O("kind", "laguerreGauss", "radiusM", W, "modeP", p, "modeL", l)).M2X, 1e-6, 0, "LG");
        }

        [Test(Reference = "Gaussian beam: curvature does not change M^2", Tolerance = "1e-6")]
        public void CurvedBeam_KeepsUnitBeamQuality()
        {
            Assert.Close(1, Beam(O("kind", "gaussian", "radiusM", W, "radiusOfCurvatureM", 2.0)).M2X, 1e-6, 0, "M2");
        }

        private static SystemDescription Branched(params JsonObject[] elements) { var s = Sys(elements); s.Topology = SystemGraph.Branched; return s; }
        private static JsonObject Bs(string id, double r, JsonValue next) => O("id", id, "type", "beamSplitter", "reflectivity", r, "next", next);
        private static JsonObject Delay(string id, double s, JsonValue next) => O("id", id, "type", "delay", "delayS", s, "next", next);
        private static double Graph(UltrafastEngine.Results.RunResult r, string key) => r.Graph.GetDouble(key, double.NaN);

        [Test(Reference = "energy conservation: 1/4 + 1/4 leaves by c", Tolerance = "1e-12")]
        public void PacketsTooFarApartForOneWindow_AllCountAsOutput()
        {
            var sys = Branched(Bs("a", 0.5, O("c", "x", "d", "y")), Delay("x", 0, "m:1"), Delay("y", 200e-12, "m:2"), Bs("m", 0.5, O("c", "end", "d", "virtual")));
            var r = Go(sys, Spec(Gauss(Lam, 100e-15, 1e-6), O("grid", O("n", 4096, "windowS", 10e-12))));
            Assert.Close(0.5e-6, Graph(r, "energyOutJ"), 1e-12, 0, "out");
            Assert.Close(2, Graph(r, "packetsAtEnd"), 0, 0, "packets");
            Assert.Close(0, Graph(r, "energyClosureJ"), 0, 1e-20, "closure");
        }

        [Test(Reference = "energy conservation at a crystal port", Tolerance = "1e-6 of the input")]
        public void SeparatePacketsAtACrystalPort_AreBooked()
        {
            var x = O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 20e-6, "cut", "auto", "interaction", O("process", "sfg", "type", "I"), "beam", O("kind", "gaussian", "w0M", 100e-6),
                      "output", "sum", "next", "end", "aux", A(O("role", "b", "fromPort", true, "beam", O("kind", "gaussian", "radiusM", 100e-6))));
            var sys = Branched(Bs("bs", 0.5, O("c", "f", "d", "m")), Delay("f", 0, "x"), Bs("m", 0.5, O("c", "d1", "d", "d2")), Delay("d1", 0, "c:1"), Delay("d2", 300e-12, "c:2"), Bs("c", 0.5, O("c", "x:b", "d", "virtual")), x);
            var r = Go(sys, Spec(Gauss(Lam, 100e-15, 1e-9), O("grid", O("n", 4096, "windowS", 20e-12))));
            Assert.Close(0, Graph(r, "energyClosureJ"), 0, 1e-6 * 1e-9, "closure");
        }

        [Test(Reference = "a passive loop decays, so it has no steady state", Tolerance = "exact")]
        public void SteadyStateLoop_DoesNotClaimConvergenceWhileTheLightDecays()
        {
            var entry = Bs("m1", 0.8, O("c", "f", "d", "end")).Set("loop", O("kind", "steadyState", "maxRoundTrips", 40, "tolerance", 1e-9));
            var sys = Branched(entry, O("id", "f", "type", "gap", "material", "Vacuum", "lengthM", 0.01, "next", "m1:2"));
            var r = Go(sys, Spec(O("shape", "cw", "centerWavelengthM", Lam, "powerW", 1.0)));
            Assert.Close(0, r.Records[0].Values["loopConverged"], 0, 0, "converged");
            Assert.True(r.Warnings.Any(w => w.Contains("ringDown")), "says what to use");
        }

        [Test(Reference = "all-pass ring: |r + t^2 q / (1 + r q)|^2 = 1", Tolerance = "1e-6")]
        public void RingDownRing_IsAllPassAtEveryLength()
        {
            foreach (var length in new[] { 0.01, 0.010000125 })
            {
                var entry = Bs("m1", 0.8, O("c", "f", "d", "end")).Set("loop", O("kind", "ringDown", "maxRoundTrips", 3000, "tolerance", 1e-16));
                var r = Go(Branched(entry, O("id", "f", "type", "gap", "material", "Vacuum", "lengthM", length, "next", "m1:2")), Spec(O("shape", "cw", "centerWavelengthM", Lam, "powerW", 1.0)));
                Assert.Close(1, Graph(r, "energyOutJ") / Graph(r, "energyInJ"), 1e-6, 0, "ring");
            }
        }
    }
}
