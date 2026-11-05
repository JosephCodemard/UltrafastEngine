// OpenBugRegressionTests.cs -- regressions for the open-bug sweep: tilted plates without a beam, beams of another colour at
// splitters and leak mirrors, probe packet names, the reported GDD's reference frequency, the critical power constant.
// Each test states the analytic or published answer that exposed the bug.

using System;
using System.Linq;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Regressions from the open-bug sweep")]
    public class OpenBugRegressionTests
    {
        private const double C = 299792458.0;

        private static double NFs(double w) => UltrafastEngine.Materials.MaterialLibrary.BuiltIn("FusedSilica").IndexAt(w);

        private static double Gdd(Func<double, double> phi, double w0)
        {
            double h = w0 * 1e-3;
            return (phi(w0 + h) - 2 * phi(w0) + phi(w0 - h)) / (h * h);
        }

        [Test(Reference = "plane-wave phase of a tilted plate: k_z d with k_z = (w/c) sqrt(n^2 - sin^2 theta), plus the air path to the plane normal to the beam", Tolerance = "1e-4")]
        public void TiltedPlate_WithoutBeam_HasTheRefractedColoursGdd_AndMatchesTheTrackedBeam()
        {
            const double d = 5e-3, lam = 1030e-9;
            foreach (double aoiDeg in new[] { 0.01, 40.0, 70.0 })
            {
                double th = aoiDeg * Math.PI / 180, s = Math.Sin(th);
                double Run(bool beam)
                {
                    var extra = O("physics", O("nonlinear", false));
                    if (beam) extra.Set("inputBeam", O("kind", "gaussian", "w0M", 1e-3));
                    var spec = Spec(Gauss(lam, 100e-15, 1e-9), extra);
                    var sys = Sys(O("id", "p", "type", "plate", "material", "FusedSilica", "medium", "Vacuum", "thicknessM", d, "aoiDeg", aoiDeg));
                    return Go(sys, spec).FinalSystem.GddS2;
                }
                var r0 = Go(Sys(O("id", "p", "type", "plate", "material", "FusedSilica", "medium", "Vacuum", "thicknessM", d, "aoiDeg", aoiDeg)),
                            Spec(Gauss(lam, 100e-15, 1e-9), O("physics", O("nonlinear", false))));
                double w0 = r0.Input.Grid.OmegaCenter;
                double st0 = s / NFs(w0), tan0 = st0 / Math.Sqrt(1 - st0 * st0);
                Func<double, double> phi = w => w / C * d * (Math.Sqrt(NFs(w) * NFs(w) - s * s) + s * tan0);
                double expected = Gdd(phi, w0);
                Assert.Close(expected, r0.FinalSystem.GddS2, 1e-4, 0, "untracked GDD at " + aoiDeg + " deg");
                Assert.Close(expected, Run(true), 1e-4, 0, "tracked GDD at " + aoiDeg + " deg");
            }
        }
    }
}
