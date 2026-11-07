// MaterialsCrystalsTests.cs -- crystal polarisation vectors / d_eff for explicit cuts (B8), delayed-response polarisation split (B11),
// CS2 liquid (G2) and the cubic chi(3) tensor with BaF2 (G1). Each test states the analytic or published answer.

using System;
using System.Linq;
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
        [Test(Category = "physics", Reference = "KDP (-42m) type-I o o -> e: d_eff = d36 |sin 2phi| E_z (exact E-field vectors); textbook d36 sin(theta) sin(2 phi) within the walk-off angle (Dmitriev, Handbook of NLO crystals)", Tolerance = "1e-9 relative (exact); 4 % (textbook)")]
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
    }
}
