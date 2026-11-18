using System;
using System.Linq;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>A thin lens whose phase the transverse grid cannot represent raises a warning.</summary>
    [TestArea("Lens sampling warning")]
    public class LensSamplingTests
    {
        private const double Lam = 1030e-9, W0 = 0.3e-3;

        private static string[] Warnings(double focalLengthM, int points)
        {
            var so = O("format", "ultrafast-system", "name", "lens");
            so.Set("elements", A(
                O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum",
                  "transverse", O("model", "radial", "points", points, "radiusM", 1.2e-3, "absorbingBoundary", true, "initialProfile", O("kind", "gaussian", "radiusM", W0))),
                O("id", "lens", "type", "lens", "focalLengthM", focalLengthM)));
            var r = Go(SystemDescription.FromJson(so), Spec(Gauss(Lam, 300e-15, 1e-6)));
            return r.Warnings.ToArray();
        }

        [Test(Reference = "Nyquist: the lens phase k r dr / f per cell at the beam edge (1.5 w) must stay below pi", Tolerance = "exact threshold")]
        public void Lens_WithAPhaseStepAbovePiAtTheBeamEdge_WarnsAndAGentleOneDoesNot()
        {
            // step = k * 1.5 w * dr / f; dr = 1.2 mm / 64 = 19 um, k = 6.1e6 / m, 1.5 w = 0.45 mm  ->  5.2 rad at f = 10 mm, 0.10 rad at f = 0.5 m
            Assert.True(Warnings(0.01, 64).Any(w => w.Contains("under-sampled") && w.Contains("'lens'")), "f = 10 mm on 64 points is under-sampled");
            Assert.False(Warnings(0.5, 64).Any(w => w.Contains("under-sampled")), "f = 0.5 m is well sampled");
            Assert.False(Warnings(0.01, 1024).Any(w => w.Contains("under-sampled")), "the same lens on 1024 points (0.35 rad per cell) is fine");
        }
    }
}
