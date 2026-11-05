// LinearComponentTests.cs -- the component sheet (ComponentSheet.cs) for the linear family and the splitters, plus the
// regressions the verification scripts found: leakMirror GDD missing from the reported dispersion, a grating pair without
// a propagating order removing the beam silently, a fed beam of another colour built on the run's grid, and the exact
// resampler aliasing carrier shifts larger than half the source band.
// Main tests: LinearSheet_EveryElement_PassesAtEveryCarrierAndBandwidth, LeakMirror_ReportsTheGddOfThePortThatContinues,
// GratingPair_WithoutAPropagatingOrder_IsAnError, FedBeam_OfAnotherColour_KeepsItsCarrier, GridResampler_ShiftBeyondTheBand_IsExact.
using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests.Components
{
    [TestArea("Component sheet: linear elements and splitters at 515 nm / 1030 nm / 2 um, 1 ps / 10 fs, Sequential = Branched")]
    public class LinearComponentTests
    {
        private static readonly double[] FsB = { 0.6961663, 0.4079426, 0.8974794 };
        private static readonly double[] FsC = { 0.0684043 * 0.0684043, 0.1162414 * 0.1162414, 9.896161 * 9.896161 };

        private static List<SheetCase> Cases() => new List<SheetCase>
        {
            new SheetCase("bulk FusedSilica 5 mm", l => O("id", "x", "type", "bulk", "material", "FusedSilica", "lengthM", 5e-3),
                (l, w) => ComponentSheet.MediumGdd(x => ComponentSheet.GlassIndex(FsB, FsC, x), 5e-3, w)) { GddRelTol = 1e-5 },
            new SheetCase("gap Air 0.5 m", l => O("id", "x", "type", "gap", "lengthM", 0.5),
                (l, w) => ComponentSheet.MediumGdd(x => { var n = new double[1]; new Materials.CiddorAirIndex().Evaluate(new[] { 2 * Math.PI * C / x }, n); return n[0]; }, 0.5, w)) { GddRelTol = 1e-4 },
            // Taylor models about the requested carrier, read at the grid centre: GDD + TOD (w_c - w)
            new SheetCase("dispersion 500 fs^2, 1000 fs^3", l => O("id", "x", "type", "dispersion", "gddFs2", 500.0, "todFs3", 1000.0),
                (l, w) => 500e-30 + 1000e-45 * (w - W(l))),
            new SheetCase("chirpedMirror 3 x -50 fs^2, R 0.99", l => O("id", "x", "type", "chirpedMirror", "gddFs2", -50.0, "bounces", 3, "reflectance", 0.99),
                (l, w) => -150e-30, Math.Pow(0.99, 3)),
            new SheetCase("leakMirror out (gddFs2 -200)", l => O("id", "x", "type", "leakMirror", "transmission", 0.05, "gddFs2", -200.0, "leakGddFs2", 300.0),
                (l, w) => -200e-30, 0.95, "out", "leak"),
            new SheetCase("leakMirror leak (leakGddFs2 300)", l => O("id", "x", "type", "leakMirror", "transmission", 0.05, "gddFs2", -200.0, "leakGddFs2", 300.0),
                (l, w) => 300e-30, 0.05, "leak", "out"),
            new SheetCase("beamSplitter R 0.3, port d", l => O("id", "x", "type", "beamSplitter", "reflectivity", 0.3), (l, w) => 0, 0.3, "d", "c"),
            new SheetCase("beamSplitter R 0.3, port c", l => O("id", "x", "type", "beamSplitter", "reflectivity", 0.3), (l, w) => 0, 0.7, "c", "d"),
            // groove densities with a first order at each carrier
            new SheetCase("gratingPair G 0.1 m, AOI 50 deg, 2 passes", l => O("id", "x", "type", "gratingPair", "linesPerMm", Lpm(l), "aoiDeg", 50.0, "separationM", 0.1),
                (l, w) => ComponentSheet.TreacyGdd(2 * Math.PI * C / w, Lpm(l) * 1e3, 50 * Math.PI / 180, 0.1, -1, 2)) { GddRelTol = 1e-5 },
        };

        // low enough that a 10 fs spectrum has no evanescent part (1500 l/mm at 1030 nm cuts at 1.18 um and clips 2.6 %, booked)
        private static double Lpm(double lam) => lam < 700e-9 ? 1800.0 : lam < 1.5e-6 ? 1000.0 : 300.0;

        [Test(Reference = "Sellmeier (Malitson 1965) x Ciddor (1996); Treacy, IEEE JQE 5, 454 (1969); Taylor phase; Stokes relations", Tolerance = "GDD 1e-6 (glass 1e-5, air 1e-4), energy 1e-9, Branched = Sequential 1e-12")]
        public void LinearSheet_EveryElement_PassesAtEveryCarrierAndBandwidth()
        {
            // the analytic GDD is evaluated at the grid centre, which the engine snaps to the frequency step; the field must not
            // change its |A(w)|^2 (no silent narrowing at 10 fs), and a one-element Branched system must give the same field
            var fails = Cases().SelectMany(ComponentSheet.Run).ToList();
            Assert.True(fails.Count == 0, string.Join("\n", fails));
        }

        [Test(Reference = "leakMirror gddFs2 / leakGddFs2 (the phase applied to each port)", Tolerance = "1e-9")]
        public void LeakMirror_ReportsTheGddOfThePortThatContinues()
        {
            // before: the phase was applied to the field but the summary's own / system GDD stayed 0 on both ports
            foreach (var (port, other, gdd) in new[] { ("out", "leak", -200e-30), ("leak", "out", 300e-30) })
            {
                var e = O("id", "m", "type", "leakMirror", "transmission", 0.05, "gddFs2", -200.0, "leakGddFs2", 300.0, "output", port);
                var seq = Go(Sys(e), Spec(Gauss(1030e-9, 30e-15, 1e-9)));
                Assert.Close(gdd, seq.FinalSystem.GddS2, 1e-9, 0, "Sequential " + port);
                var b = O("id", "m", "type", "leakMirror", "transmission", 0.05, "gddFs2", -200.0, "leakGddFs2", 300.0, "next", O(port, "end", other, "virtual"));
                var s = Sys(b); s.Topology = SystemGraph.Branched;
                Assert.Close(gdd, Go(s, Spec(Gauss(1030e-9, 30e-15, 1e-9))).FinalSystem.GddS2, 1e-9, 0, "Branched " + port);
            }
        }

        [Test]
        public void GratingPair_WithoutAPropagatingOrder_IsAnError()
        {
            // 1500 l/mm at 2 um: |m| lambda / d = 3, no first order. Before: the whole spectrum was removed, energy null, no warning
            string msg = null;
            try { Go(Sys(O("id", "gp", "type", "gratingPair", "linesPerMm", 1500.0, "aoiDeg", 50.0, "separationM", 0.1)), Spec(Gauss(2e-6, 100e-15, 1e-9))); }
            catch (Exception ex) { msg = ex.Message; }
            Assert.True(msg != null && msg.Contains("does not propagate"), msg ?? "no error");
        }

        [Test(Reference = "Linear superposition: a lossless splitter sends (1 - R) a + R b of each colour to port c; different colours do not interfere", Tolerance = "1e-6")]
        public void FedBeam_OfAnotherColour_KeepsItsCarrier()
        {
            // 1 nJ at 1030 nm into a, 2 nJ at 515 nm (coherentWith input) into b of an R = 0.3 splitter.
            // Before: the 515 nm beam was built on the 1030 nm grid and interfered as if it were 1030 nm (port c: 3.9 pJ).
            var bs = O("id", "bs", "type", "beamSplitter", "reflectivity", 0.3, "next", O("c", "end", "d", "virtual"),
                "inputs", O("b", O("pulse", Gauss(515e-9, 100e-15, 2e-9), "coherentWith", "input")));
            var s = Sys(bs); s.Topology = SystemGraph.Branched;
            var r = Go(s, Spec(Gauss(1030e-9, 100e-15, 1e-9), O("physics", O("nonlinear", false))));
            var p = r.Output; var I = p.SpectralIntensity(); double total = I.Sum(), green = 0;
            for (int i = 0; i < p.Grid.N; i++) if (p.Grid.OmegaAbsolute[i] > W(750e-9)) green += I[i];
            double e = p.TotalEnergyJ();
            Assert.Close(0.7e-9 + 0.6e-9, e, 1e-9, 0, "port c energy");
            Assert.Close(0.6e-9, e * green / total, 1e-6, 0, "the 515 nm share of port c");
        }

        [Test(Reference = "Band-limited resampling between exactly related grids is exact", Tolerance = "1e-9")]
        public void GridResampler_ShiftBeyondTheBand_IsExact()
        {
            // a 515 nm pulse moved onto a grid with a quarter of the time step centred at 690 nm: the carrier shift (1.0e15 rad/s)
            // exceeds half the source band (3.8e14 rad/s). Before: the shift was applied in time at the source step and aliased.
            var src = FrequencyGrid.Create(512, 8.3e-15, Math.Round(W(515e-9) / (2 * Math.PI / (512 * 8.3e-15))) * (2 * Math.PI / (512 * 8.3e-15)));
            var pulse = PulseSpec.FromJson(Gauss(515e-9, 100e-15, 1e-9), ".").Build(src, false);
            var dst = GridResampler.Derive(src, 0, -2, W(690e-9));
            var q = GridResampler.Resample(pulse, dst, out double lost);
            Assert.Less(lost, 1e-9, "energy outside the destination");
            Assert.Close(SpectralCentroid(pulse), SpectralCentroid(q), 1e-9, 0, "spectral centroid");
        }
    }
}
