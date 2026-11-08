// CellModeTests.cs -- the Kerr-matched mode of a two-mirror multi-pass cell (Lasers/CellMode.cs, command "cellMode").
// References: the Kogelnik-Li eigenmode of the empty cell, the Herriott re-entrance condition, the first-order shift of the
// round-trip stability by a thin Kerr lens, and the engine's own radial Kerr propagation for the lens definition.
using System;
using System.Linq;
using UltrafastEngine.Lasers;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Multi-pass cell modes (Kerr-matched Herriott and quasi-concentric cells)")]
    public class CellModeTests
    {
        private static JsonObject Cell(double lam, double r, double d, params object[] kv)
        {
            var o = O("command", "cellMode", "wavelengthM", lam, "mirrorRadiusM", r, "separationM", d);
            for (int i = 0; i < kv.Length; i += 2)
            {
                var v = kv[i + 1];
                o.Set((string)kv[i], v is JsonValue jv ? jv : v is double dd ? (JsonValue)dd : v is int ii ? (JsonValue)ii : (JsonValue)(string)v);
            }
            return o;
        }
        private static JsonObject Mode(JsonObject r, string key = "emptyCell") => r.GetObject(key);

        [Test(Reference = "Kogelnik & Li, Appl. Opt. 5, 1550 (1966): w0^2 = (lambda d / pi) sqrt(g1 g2 (1 - g1 g2)) / |g1 + g2 - 2 g1 g2|, w1^2 = (lambda d / pi) sqrt(g2 / (g1 (1 - g1 g2)))", Tolerance = "1e-9")]
        public void EmptyCell_MatchesKogelnikLiForSymmetricAndAsymmetricCells()
        {
            double lam = 1030e-9;
            foreach (var (r1, r2, d) in new[] { (0.35, 0.35, 0.54), (1.5, 1.5, 2.4), (1.5, 1.5, 2.9985), (0.5, 0.8, 0.4), (1.0, 1.0, 1.9999) })
            {
                var o = Cell(lam, r1, d);
                o.Set("mirrorRadiiM", JsonValue.FromArray(new[] { r1, r2 })); o.Remove("mirrorRadiusM");
                var res = Commands.Execute(o);
                double g1 = 1 - d / r1, g2 = 1 - d / r2;
                double w0 = Math.Sqrt(lam * d / Math.PI * Math.Sqrt(g1 * g2 * (1 - g1 * g2)) / Math.Abs(g1 + g2 - 2 * g1 * g2));
                double wm1 = Math.Sqrt(lam * d / Math.PI * Math.Sqrt(g2 / (g1 * (1 - g1 * g2)))), wm2 = Math.Sqrt(lam * d / Math.PI * Math.Sqrt(g1 / (g2 * (1 - g1 * g2))));
                var m = Mode(res);
                string tag = " (R = " + r1 + ", " + r2 + ", d = " + d + ")";
                Assert.Close(w0, m.GetDouble("waistRadiusM", 0), 1e-9, 0, "waist" + tag);
                Assert.Close(wm1, m.GetDouble("radiusAtMirror1M", 0), 1e-9, 0, "mirror 1 spot" + tag);
                Assert.Close(wm2, m.GetDouble("radiusAtMirror2M", 0), 1e-9, 0, "mirror 2 spot" + tag);
                Assert.Close(w0, CellModeSolver.EmptyCellWaistM(lam, r1, r2, d), 1e-12, 0, "EmptyCellWaistM" + tag);
                // the launch plane is mirror 1: the waist is d * g2 (1 - g1) / (g1 + g2 - 2 g1 g2) ahead of it
                Assert.Close(d * g2 * (1 - g1) / (g1 + g2 - 2 * g1 * g2), m.GetDouble("waistDistanceFromLaunchM", 0), 1e-9, 1e-12, "waist position" + tag);
            }
        }

        [Test(Reference = "Schulte et al., Opt. Lett. 41, 4511 (2016): empty-cell mode of 540 mm / R = 350 mm: waist 220 um, 459 um on the mirrors (as read in docs/papers/schulte-2016-mpc-fused-silica)", Tolerance = "0.3 %")]
        public void EmptyCell_OfTheSchulteCell_HasThePublishedSpotSizes()
        {
            var res = Commands.Execute(Cell(1030e-9, 0.35, 0.54));
            Assert.Close(220e-6, Mode(res).GetDouble("waistRadiusM", 0), 0.003, 0, "waist");
            Assert.Close(459e-6, Mode(res).GetDouble("radiusAtMirror1M", 0), 0.003, 0, "mirror spot");
        }

        [Test(Reference = "Herriott, Kogelnik & Kompfner, Appl. Opt. 3, 523 (1964): the pattern closes after N round trips when N theta = 2 pi k, cos(theta / 2) = g", Tolerance = "1e-9")]
        public void HerriottSpacing_ClosesThePatternAfterTheChosenRoundTrips()
        {
            foreach (var (n, k) in new[] { (8, 3), (12, 5), (18, 7), (30, 11), (18, 12), (12, 7), (30, 19) })
            {
                var o = Cell(800e-9, 1.5, 3.0);
                o.Remove("separationM"); o.Set("reentrant", O("roundTrips", n, "order", k)); o.Set("passes", 2 * n);
                var res = Commands.Execute(o);
                // round-trip Gouy phase theta = 2 arccos(g) (symmetric cell, g = 1 - d / R, theta > pi when g < 0, e.g. (18, 12): g = -0.5): N theta = 2 pi k
                Assert.Close(2 * Math.PI * k, n * res.GetDouble("roundTripGouyPhaseRad", 0), 1e-9, 0, "N theta for N = " + n + ", k = " + k);
                Assert.Close(CellModeSolver.HerriottSpacingM(1.5, n, k), res.GetDouble("separationM", 0), 1e-12, 0, "spacing");
            }
        }

        [Test(Reference = "first order in a thin Kerr lens: cos(theta') - cos(theta) = -(Phi_total / 2) z_R sin(theta), theta = 2 arccos(g) the round-trip Gouy phase of the symmetric cell, Phi = 2 n2 dz P / (pi w0^4) per pass", Tolerance = "0.3 %")]
        public void WeakKerrPlateAtTheWaist_ShiftsTheRoundTripStabilityAsTheThinLensFormula()
        {
            // a plate at the waist of a symmetric cell is passed twice per round trip; for g > 0 (theta < pi) the focusing lens
            // raises the Gouy phase, for g < 0 (quasi-concentric side, theta > pi) the sign of B flips and it lowers cos(theta)'s sensitivity: both follow the formula
            double lam = 1030e-9, n2 = 2.6e-20, t = 1e-6, P = 1e3;   // t << z_R, P << critical: first order holds to ~ 1e-4
            foreach (var (r, d) in new[] { (1.0, 0.7), (0.35, 0.54), (1.5, 2.9) })
            {
                var o = Cell(lam, r, d, "peakPowerW", P, "slices", 1);
                o.Set("plates", A(O("thicknessM", t, "index", 1.0, "n2M2PerW", n2)));
                var res = Commands.Execute(o);
                Assert.True(res.GetBool("matched", false), "matched");
                double g = 1 - d / r, w0 = CellModeSolver.EmptyCellWaistM(lam, r, r, d), zR = Math.PI * w0 * w0 / lam;
                double theta = 2 * Math.Acos(g);
                double phiTotal = 2 * CellModeSolver.KerrLensPowerPerM(n2, t, P, w0);
                double expected = -phiTotal / 2 * zR * Math.Sin(theta);
                double got = res.GetDouble("stability", 0) - res.GetDouble("emptyCellStability", 0);
                Assert.Close(expected, got, 0.003, 0, "change of (A + D) / 2 for R = " + r + ", d = " + d + " (g = " + g.ToString("G3") + ")");
            }
        }

        [Test(Reference = "self-consistency of the round trip: the matched q reproduces itself with the Kerr lenses it creates (residual < 1e-9); a vanishing power gives the empty-cell mode", Tolerance = "1e-9")]
        public void MatchedMode_ReproducesItselfAndTendsToTheEmptyCellModeAtLowPower()
        {
            double lam = 800e-9;
            var low = Commands.Execute(Cell(lam, 1.5, 2.9, "peakPowerW", 1.0, "gas", O("species", "Ar", "pressureBar", 0.4)));
            Assert.Close(Mode(low, "emptyCell").GetDouble("waistRadiusM", 0), Mode(low, "mode").GetDouble("waistRadiusM", 0), 1e-6, 0, "1 W: the empty-cell mode");
            // 36 m of argon at 0.4 bar, P about 0.4 of the gas critical power: a visible shift
            var hi = Commands.Execute(Cell(lam, 1.5, 2.9, "peakPowerW", 2.0e9, "gas", O("species", "Ar", "pressureBar", 0.4)));
            Assert.True(hi.GetBool("matched", false), "matched at high power: " + hi.GetString("message", ""));
            Assert.Less(hi.GetDouble("residual", 1), 1e-9, "residual of the fixed point");
            Assert.Greater(Math.Abs(hi.GetDouble("shiftOfWaistRadius", 0)), 0.01, "the Kerr lens moves the matched mode");
            Assert.Less(hi.GetDouble("overlapEmptyModeWithMatchedMode", 1), 0.9999, "the empty-cell mode no longer matches");
            Assert.Greater(hi.GetDouble("bIntegralPerRoundTripRad", 0), 0.1, "B per round trip");
        }

        [Test(Reference = "the matched mode ceases to exist when the Kerr lens pushes the round trip out of its stability range; the helper says so instead of returning a mode", Tolerance = "-")]
        public void WhenNoStableMatchedModeExists_TheHelperReportsIt()
        {
            var res = Commands.Execute(Cell(800e-9, 1.5, 3.0 - 1e-4, "peakPowerW", 5.0e11, "gas", O("species", "Ar", "pressureBar", 3.0)));
            Assert.True(!res.GetBool("matched", true), "no matched mode at an absurd power");
            Assert.True(res.GetString("message", "").Length > 0, "a message");
            var unstable = Commands.Execute(Cell(800e-9, 1.5, 4.0));   // d > 2 R: g = -4/3, g1 g2 > 1
            Assert.True(!unstable.GetBool("emptyCellStable", true) && !unstable.GetBool("matched", true), "an unstable empty cell has no mode");
        }

        [Test(Category = "nonlinear", Reference = "Vlasov, Petrishchev & Talanov (1971) moment law: the thin-lens power 1/f = n2 I0 dz / w^2 (I0 = 2P / pi w^2) reproduces the rms-radius evolution of the engine's radial Kerr propagation (first order in P / P_cr); the on-axis parabolic lens (4x) does not", Tolerance = "2 %")]
        public void KerrLensOfTheHelper_ReproducesTheRadialModelsRmsRadius()
        {
            // a Gaussian beam with its waist at the entrance of a 30 mm Kerr slab (n = 1, dispersionless), then 60 mm of vacuum; the
            // time-integrated fluence radius changes by ABCD with one thin Kerr lens per slice. The fluence of a Gaussian pulse weights
            // each time slice by its power, so to first order the effective power is P_peak / sqrt(2) (mean of P^2 over mean of P).
            double lam = 1030e-9, w0 = 100e-6, n2 = 1e-19, L = 0.03, z2 = 0.06, tau = 100e-15, E = 2.4e-8;
            int slices = 60;
            double Run(double n2v, out double peak)
            {
                var so = O("format", "ultrafast-system", "name", "kerr", "materials", O("flat", FlatMaterial(n2v, 1.0)));
                JsonObject Grid() => O("model", "radial", "points", 128, "radiusM", 1.2e-3, "absorbingBoundary", true);
                so.Set("elements", A(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", Grid().Set("initialProfile", O("kind", "gaussian", "radiusM", w0))),
                                     O("id", "slab", "type", "bulk", "material", "flat", "lengthM", L, "transverse", Grid(), "beam", O("kind", "gaussian", "w0M", w0)),
                                     O("id", "out", "type", "gap", "lengthM", z2, "material", "Vacuum", "transverse", Grid())));
                var r = Go(UltrafastEngine.Systems.SystemDescription.FromJson(so), Spec(Gauss(lam, tau, E)));
                peak = UltrafastEngine.Results.PulseMetrics.TemporalPower(r.Input).Max();
                return r.Records.First(x => x.Id == "out").BeamRadiusXM;
            }
            double wLin = Run(0, out double pk), wNl = Run(n2, out _);
            double engine = wNl * wNl / (wLin * wLin) - 1;
            double Abcd(string model)
            {
                // 1/q of the linear beam, thin Kerr lenses at the slice centres (linear radii), then the final radius
                double zr = Math.PI * w0 * w0 / lam, dz = L / slices, peff = pk / Math.Sqrt(2);
                var q = new System.Numerics.Complex(0, zr);
                double z = 0;
                for (int j = 0; j < slices; j++)
                {
                    q += dz / 2; double w = Math.Sqrt(lam / Math.PI * (q.Imaginary * q.Imaginary + q.Real * q.Real) / q.Imaginary);
                    q = 1 / (1 / q - CellModeSolver.KerrLensPowerPerM(n2, dz, peff, w, model)); q += dz / 2; z += dz;
                }
                q += z2;
                double wf2 = lam / Math.PI * (q.Imaginary * q.Imaginary + q.Real * q.Real) / q.Imaginary;
                var ql = new System.Numerics.Complex(0, zr) + L + z2;
                double wl2 = lam / Math.PI * (ql.Imaginary * ql.Imaginary + ql.Real * ql.Real) / ql.Imaginary;
                return wf2 / wl2 - 1;
            }
            double second = Abcd("secondMoment"), parax = Abcd("paraxial");
            Assert.Less(engine, 0, "self-focusing narrows the beam");
            Assert.Close(second, engine, 0.02, 0, "second-moment lens vs the radial model");
            Assert.Greater(Math.Abs(parax - engine) / Math.Abs(engine), 1.5, "the on-axis lens (4x) is far from the radial model's rms radius");
        }
    }
}
