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
                double w0 = W(lam);   // dispersion is reported at the requested carrier
                double st0 = s / NFs(w0), tan0 = st0 / Math.Sqrt(1 - st0 * st0);
                Func<double, double> phi = w => w / C * d * (Math.Sqrt(NFs(w) * NFs(w) - s * s) + s * tan0);
                double expected = Gdd(phi, w0);
                Assert.Close(expected, r0.FinalSystem.GddS2, 1e-4, 0, "untracked GDD at " + aoiDeg + " deg");
                Assert.Close(expected, Run(true), 1e-4, 0, "tracked GDD at " + aoiDeg + " deg");
            }
        }

        private static double Tc(UltrafastEngine.Pulses.PulseField p) => TemporalCentroid(p);

        [Test(Reference = "linear superposition: the splitter passes each colour at its own frequency", Tolerance = "1 %")]
        public void SequentialSplitter_FedBeamOfAnotherColour_KeepsItsOwnCarrier()
        {
            // 1000 nm fed into a 1030 nm run: both colours leave by port c, half of each (R = 0.5), so the spectral centroid is the mean frequency
            double l1 = 1030e-9, l2 = 1000e-9;
            var s = Sys(O("id", "bs", "type", "beamSplitter", "reflectivity", 0.5, "inputs", O("b", O("pulse", Gauss(l2, 400e-15, 1e-9), "delayS", 0.0, "coherentWith", "input"))));
            var r = Go(s, Spec(Gauss(l1, 400e-15, 1e-9)));
            Assert.Close(0.5 * (W(l1) + W(l2)), SpectralCentroid(r.Output), 2e-4, 0, "centroid is the mean of the two carriers");
            Assert.Close(1e-9, r.Output.TotalEnergyJ(), 1e-3, 0, "half of each 1 nJ beam");
        }

        [Test]
        public void SequentialSplitter_FedBeamOutsideTheGrid_IsReportedAndBooked_NotRenormalised()
        {
            double l1 = 1030e-9;
            var s = Sys(O("id", "bs", "type", "beamSplitter", "reflectivity", 0.5, "inputs", O("b", O("pulse", Gauss(l1 / 2, 100e-15, 1e-9), "delayS", 0.0, "coherentWith", "input"))));
            var r = Go(s, Spec(Gauss(l1, 100e-15, 1e-9)));
            Assert.Close(0.5e-9, r.Output.TotalEnergyJ(), 1e-3, 0, "only the main beam's half is on the grid");
            Assert.True(r.Warnings.Any(w => w.Contains("outside the run's frequency grid")), "a warning names the lost energy");
            Assert.True(r.Records[0].LossJ.ContainsKey("fed beam outside the run's grid"), "booked as a loss");
        }

        [Test]
        public void MeasuredSpectrum_OutsideTheGridBand_IsAnError_NotRenormalisedAtTheEdge()
        {
            var wl = Enumerable.Range(0, 200).Select(i => 1000e-9 + i * 0.3e-9).ToArray();
            var I = wl.Select(l => Math.Exp(-Math.Pow((l - 1030e-9) / 10e-9, 2))).ToArray();
            var run = UltrafastEngine.Specs.RunSpec.FromJson(O("format", "ultrafast-run", "pulse", O("spectrum", O("wavelengthsM", wl, "intensity", I), "energyJ", 1e-6)));
            var onBand = UltrafastEngine.Pulses.FrequencyGrid.Create(1024, 5e-15, W(1030e-9));
            var notes = new System.Collections.Generic.List<string>();
            run.Pulse.Build(onBand, notes);
            Assert.Equal(0, notes.Count, "a spectrum inside the band raises nothing");
            var offBand = UltrafastEngine.Pulses.FrequencyGrid.Create(1024, 5e-15, W(515e-9));
            string msg = "";
            try { run.Pulse.Build(offBand, notes); } catch (InvalidOperationException ex) { msg = ex.Message; }
            Assert.True(msg.Contains("frequency band"), "a spectrum outside the band is an error: " + msg);
        }

        [Test(Reference = "a leakMirror's GDD is a quadratic phase about the beam's own carrier: no group-delay offset for another colour", Tolerance = "0.2 ps")]
        public void LeakMirror_GddIsAboutTheFedBeamsOwnCarrier()
        {
            // a negligible main pulse and a 1000 nm fed beam on a 1030 nm run: the reflected port's temporal centroid stays at zero
            var s = Sys(O("id", "m", "type", "leakMirror", "transmission", 0.02, "gddFs2", 20000.0, "inputs", O("b", O("pulse", Gauss(1000e-9, 100e-15, 1e-9), "delayS", 0.0, "coherentWith", "input"))));
            var r = Go(s, Spec(Gauss(1030e-9, 100e-15, 1e-15)));
            Assert.True(Math.Abs(Tc(r.Output)) < 0.2e-12 + 0, "centroid " + Tc(r.Output));
        }

        [Test]
        public void Probe_SeeingTwoPackets_GivesEachItsOwnNamedArrays()
        {
            var sys = Sys(O("id", "a", "type", "beamSplitter", "reflectivity", 0.5, "next", O("c", "x", "d", "y")), O("id", "x", "type", "delay", "delayS", 0.0, "next", "m:1"),
                O("id", "y", "type", "delay", "delayS", 200e-12, "next", "m:2"),
                O("id", "m", "type", "beamSplitter", "reflectivity", 0.5, "next", O("c", "pr", "d", "virtual")), O("id", "pr", "type", "probe", "next", "end"));
            sys.Topology = UltrafastEngine.Systems.SystemGraph.Branched;
            var r = Go(sys, Spec(Gauss(1030e-9, 100e-15, 1e-6), O("grid", O("n", 4096, "windowS", 10e-12), "probes", A("pr"))));
            var names = r.Arrays().Select(a => a.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count(), "array names are unique");
            Assert.True(names.Contains("probe_pr_packet1_spectrum") && names.Contains("probe_pr_packet2_spectrum"), "one set per packet: " + string.Join(",", names.Where(n => n.StartsWith("probe_"))));
            Assert.True(!names.Contains("probe_pr_spectrum"), "no unsuffixed duplicate");
            Assert.True(r.GetArray("probe_pr_packet2_spectrum") != null, "lookup by name finds the second packet");
            Assert.True(r.SummaryJson()["probes"].AsArray().Select(v => v.AsString()).Contains("pr_packet2"), "summary.probes lists the names");
        }

        [Test(Reference = "a Taylor dispersion element has its GDD at its reference wavelength: 1000 fs^2 and 2000 fs^3 about the requested carrier", Tolerance = "1e-9")]
        public void ReportedGdd_IsAtTheRequestedCarrier_NotTheSnappedGridCentre()
        {
            var r = Go(Sys(O("id", "d", "type", "dispersion", "gddFs2", 1000.0, "todFs3", 2000.0)), Spec(Gauss(1030e-9, 30e-15, 1e-9)));
            Assert.True(Math.Abs(r.Input.Grid.OmegaCenter - W(1030e-9)) > 1e9, "the grid centre is snapped away from the requested carrier");
            Assert.Close(1000e-30, r.FinalSystem.GddS2, 1e-9, 0, "system GDD");
            Assert.Close(2000e-45, r.FinalSystem.TodS3, 1e-9, 0, "system TOD");
            Assert.Close(1000e-30, r.Records[0].Own.GddS2, 1e-9, 0, "own GDD");
        }
    }
}
