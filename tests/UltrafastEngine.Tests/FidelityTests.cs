using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>The run-level fidelity switch (docs/reference/fidelity.md): bundles fill only unset switches, are reported, full is unchanged, quick stays close to full at low B and warns at high B.</summary>
    [TestArea("Fidelity switch")]
    public class FidelityTests
    {
        private static SystemDescription Plate(double lengthM = 5e-3)
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "plate");
            o.Set("elements", A(O("id", "p", "type", "bulk", "material", "FusedSilica", "lengthM", lengthM, "beam", O("kind", "constant", "areaM2", 1e-8))));
            return SystemDescription.FromJson(o);
        }

        private static RunSpec Run(string fidelity, double peakW, JsonObject physics = null, JsonArray instruments = null)
        {
            var r = O("format", "ultrafast-run", "version", 2, "name", "r");
            if (fidelity != null) r.Set("fidelity", fidelity);
            if (physics != null) r.Set("physics", physics);
            if (instruments != null) r.Set("instruments", instruments);
            r.Set("pulse", O("shape", "gaussian", "centerWavelengthM", 1030e-9, "durationFwhmS", 200e-15, "peakPowerW", peakW));
            return RunSpec.FromJson(r);
        }

        [Test]
        public void Fidelity_FullIsTheDefaultAndChangesNothing()
        {
            var a = Simulation.Run(Plate(), Run(null, 2e6));
            var b = Simulation.Run(Plate(), Run("full", 2e6));
            Assert.Equal(a.Output.TotalEnergyJ(), b.Output.TotalEnergyJ(), "same energy");
            for (int i = 0; i < a.Output.Ax.Length; i++) Assert.True(a.Output.Ax[i] == b.Output.Ax[i], "bit-identical field at " + i);
            Assert.True(!b.SummaryJson().Has("fidelityReport"), "no report at full fidelity");
        }

        [Test]
        public void Fidelity_QuickFillsOnlyUnsetSwitchesAndReportsThem()
        {
            var r = Run("quick", 2e6, O("raman", true));
            Assert.True(r.Physics.Raman, "raman written by the run spec stays true");
            Assert.True(!r.Physics.SelfSteepening, "self-steepening was unset: quick turns it off");
            Assert.Equal("scalar", r.Physics.Polarization, "polarisation scalar");
            Assert.Equal(Fidelity.QuickTolerance, r.Stepper.Tolerance, "looser tolerance");
            Assert.True(r.Fidelity.Kept.Any(k => k.StartsWith("physics.raman")), "the kept switch is listed");
            Assert.True(r.Fidelity.Changes.Any(k => k.StartsWith("physics.selfSteepening")), "the change is listed");
            var s = Run("standard", 2e6);
            Assert.True(s.Physics.Raman && s.Physics.SelfSteepening && s.Physics.Polarization == "jones", "standard keeps the physics");
            Assert.Equal(Fidelity.StandardTolerance, s.Stepper.Tolerance, "standard tolerance");
            Assert.Throws<JsonException>(() => Run("fast", 1e6), "unknown level");
        }

        [Test]
        public void Fidelity_QuickStaysCloseToFullAtLowB()
        {
            // B = gamma P L is about 0.15 rad here, so Raman, self-steepening and the looser step change the output very little.
            var full = Simulation.Run(Plate(), Run("full", 2e6));
            var quick = Simulation.Run(Plate(), Run("quick", 2e6));
            var sf = Results.PulseMetrics.Summarize(full.Output); var sq = Results.PulseMetrics.Summarize(quick.Output);
            Assert.Close(sf.SpectralFwhmM, sq.SpectralFwhmM, 0.01, 0, "spectral width, quick vs full");
            // measured 6.5e-6: the delayed response (Raman) moves a little energy to the medium; quick keeps the 1e-4 solver tolerance
            Assert.Close(sf.EnergyJ, sq.EnergyJ, 1e-4, 0, "energy");
            Assert.True(quick.Warnings.All(w => !w.Contains("unreliable")), "no unreliability warning at low B: " + string.Join(" | ", quick.Warnings));
            Assert.True(quick.SummaryJson().Has("fidelityReport"), "report in summary.json");
        }

        [Test]
        public void Fidelity_QuickWarnsWhenTheDroppedPhysicsMatters()
        {
            var q = Simulation.Run(Plate(), Run("quick", 2e8));   // B about 15 rad
            Assert.True(q.Warnings.Any(w => w.Contains("fidelity quick") && w.Contains("unreliable")), "warning: " + string.Join(" | ", q.Warnings));
            var rep = q.SummaryJson().GetObject("fidelityReport");
            Assert.True(rep.GetObject("indicators").GetDouble("bIntegralRad", 0) > 3, "B reported");
        }

        [Test]
        public void Fidelity_QuickSkipsFrogUnlessKept()
        {
            var ins = A(O("kind", "frog"), O("kind", "frog", "name", "kept", "keepInQuick", true), O("kind", "pulseSummary"));
            var r = Run("quick", 2e6, null, ins);
            Assert.Equal(2, r.Instruments.Count, "one frog skipped");
            Assert.True(r.Fidelity.Changes.Any(c => c.Contains("frog") && c.Contains("skipped")), "reported");
        }

        [Test]
        public void Fidelity_QuickReplacesASpatialModelByTheBeamModel()
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "spatial");
            o.Set("elements", A(
                O("id", "p", "type", "bulk", "material", "FusedSilica", "lengthM", 2e-3, "beam", O("kind", "gaussian", "w0M", 100e-6), "transverse", O("model", "radial", "points", 48, "radiusM", 0.45e-3))));
            var sys = SystemDescription.FromJson(o);
            var full = Simulation.Run(sys, Run("full", 1e5));
            var quick = Simulation.Run(sys, Run("quick", 1e5));
            Assert.True(full.Compiled.Elements[0].Active is Transverse.SpatialBulkElement, "full: spatial model");
            Assert.True(!(quick.Compiled.Elements[0].Active is Transverse.SpatialBulkElement), "quick: 1-D beam model");
            Assert.True(quick.Notes.Any(n => n.Contains("fidelity quick uses the 1-D beam model")), "reported in the notes");
            Assert.Close(Results.PulseMetrics.Summarize(full.Output).EnergyJ, Results.PulseMetrics.Summarize(quick.Output).EnergyJ, 1e-3, 0, "linear-regime energy");
        }
    }
}
