using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>The core memory estimate (Engine/MemoryEstimate.cs): counts against hand calculations for 1-D, radial and cartesian runs,
    /// compute.memoryLimitBytes, the free-memory warning, and a real run's retained managed memory against the estimate. docs/reference/memory.md.</summary>
    [TestArea("Memory safety: estimate and limit")]
    public class MemoryEstimateTests
    {
        private const double Lam = 1030e-9;

        private static JsonObject Gap(string id, JsonObject transverse = null, string material = "Vacuum")
        {
            var o = O("id", id, "type", "gap", "lengthM", 0.01, "material", material);
            if (transverse != null) o.Set("transverse", transverse);
            return o;
        }

        private static CompiledRun Compile(UltrafastEngine.Systems.SystemDescription sys, JsonObject extra = null, int n = 4096)
        {
            var o = O("grid", O("auto", false, "n", n, "windowS", 20e-12));
            if (extra != null) foreach (var kv in extra) o.Set(kv.Key, kv.Value);
            return Compiler.Compile(sys, Spec(Gauss(Lam, 100e-15, 1e-6), o));
        }

        [Test(Reference = "hand count: (elements + input) x 2 polarisations x N x 16 B; 24 N-sample solver arrays", Tolerance = "exact")]
        public void Estimate_OneDimensional_MatchesTheHandCount()
        {
            var cr = Compile(Sys(Gap("a"), Gap("b"), Gap("c")));
            var m = cr.Memory;
            Assert.Equal(4L * 2 * 4096 * 16, m.PulseBytes, "pulses: input + 3 elements");
            Assert.Equal(0L, m.ProfileBytes, "no transverse model, no profiles");
            Assert.Equal(24L * 4096 * 16, m.WorkspaceBytes, "1-D workspace");
            Assert.Equal(m.PulseBytes + m.WorkspaceBytes, m.TotalBytes, "total");
            Assert.True(cr.Notes.Any(x => x.StartsWith("Estimated memory of one run")), "the estimate is a compile note");
        }

        [Test(Reference = "hand count: profiles kept = spatial elements + 1 in flight (all) or instruments + 1 + 1 (used); M x spectralSamples x 16 B each", Tolerance = "exact")]
        public void Estimate_Radial_MatchesTheHandCount_AllAndUsed()
        {
            var tv = O("model", "radial", "points", 40, "spectralSamples", 10, "initialProfile", O("kind", "gaussian", "radiusM", 1e-3));
            var sys = Sys(Gap("a", tv), Gap("b", O("model", "radial", "points", 40, "spectralSamples", 10)), Gap("c", O("model", "radial", "points", 40, "spectralSamples", 10)), Gap("d", O("model", "radial", "points", 40, "spectralSamples", 10)));
            var all = Compile(sys).Memory;
            Assert.Equal(5L * 40 * 10 * 16, all.ProfileBytes, "keepProfiles all: 4 spatial elements + the pulse in flight");
            var inst = O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "end")), "outputs", O("keepProfiles", "used"));
            var used = Compile(sys, inst).Memory;
            Assert.Equal(3L * 40 * 10 * 16, used.ProfileBytes, "keepProfiles used: 1 instrument + the element before it, plus the pulse in flight");
            Assert.Equal(Math.Max(24L * 4096 * 16, 8L * 40 * 10 * 16), all.WorkspaceBytes, "workspace: the larger of the 1-D arrays and 8 spatial arrays");
        }

        [Test(Reference = "hand count: nx x ny points, vector fields doubled, frequency block limits the workspace", Tolerance = "exact")]
        public void Estimate_CartesianVector_MatchesTheHandCount()
        {
            var tv = O("model", "cartesian", "nx", 32, "ny", 16, "halfWidthM", 2e-3, "spectralSamples", 12, "frequencyBlock", 3, "polarization", "vector");
            var m = Compile(Sys(Gap("a", tv), Gap("b", tv))).Memory;
            Assert.Equal(3L * 32 * 16 * 12 * 16 * 2, m.ProfileBytes, "2 spatial elements + 1 in flight, two polarisations");
            Assert.Equal(Math.Max(24L * 4096 * 16, 8L * 32 * 16 * 3 * 16 * 2), m.WorkspaceBytes, "workspace of 3 frequencies at a time (or the 1-D arrays, if larger)");
            var big = O("model", "cartesian", "nx", 128, "ny", 128, "halfWidthM", 2e-3, "spectralSamples", 12, "frequencyBlock", 3, "polarization", "vector");
            Assert.Equal(8L * 128 * 128 * 3 * 16 * 2, Compile(Sys(Gap("a", big))).Memory.WorkspaceBytes, "workspace of 3 frequencies at a time");
            big.Set("frequencyBlock", 0);
            Assert.Equal(8L * 128 * 128 * 12 * 16 * 2, Compile(Sys(Gap("a", big))).Memory.WorkspaceBytes, "all 12 frequencies at once");
        }

        [Test(Reference = "compute.memoryLimitBytes refuses the run before it starts, with advice", Tolerance = "exact")]
        public void Limit_BelowTheEstimate_FailsAtCompile_AboveItPasses()
        {
            var sys = Sys(Gap("a"), Gap("b"));
            string msg = null;
            try { Compile(sys, O("compute", O("memoryLimitBytes", 1e5))); } catch (ModelException e) { msg = e.Message; }
            Assert.True(msg != null && msg.Contains("compute.memoryLimitBytes") && msg.Contains("To reduce it"), "message names the option and gives advice: " + msg);
            var ok = Compile(sys, O("compute", O("memoryLimitBytes", 1e9)));
            Assert.True(ok.Memory.TotalBytes < 1e9, "under the limit");
            var validate = Simulation.Validate(sys, Spec(Gauss(Lam, 100e-15, 1e-6), O("grid", O("auto", false, "n", 4096, "windowS", 20e-12), "compute", O("memoryLimitBytes", 1e5))));
            Assert.True(!validate.Ok && validate.Errors.Any(e => e.Contains("memory limit")), "validate reports it as an error");
        }

        [Test(Reference = "a warning above 80 % of the free memory, none below", Tolerance = "exact")]
        public void FreeMemory_Above80Percent_Warns_NotBelow()
        {
            var sys = Sys(Gap("a"), Gap("b"));
            long need = Compile(sys).Memory.TotalBytes;
            try
            {
                MemoryEstimate.AvailableBytesOverride = (long)(need / 0.85);   // the estimate is 85 % of the free memory
                Assert.True(Compile(sys).Warnings.Any(w => w.StartsWith("Memory:") && w.Contains("free memory")), "warns at 85 %");
                MemoryEstimate.AvailableBytesOverride = (long)(need / 0.75);
                Assert.True(!Compile(sys).Warnings.Any(w => w.StartsWith("Memory:")), "silent at 75 %");
                MemoryEstimate.AvailableBytesOverride = -1;   // unknown: no warning
                Assert.True(!Compile(sys).Warnings.Any(w => w.StartsWith("Memory:")), "silent when the free memory is unknown");
            }
            finally { MemoryEstimate.AvailableBytesOverride = null; }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static (long Retained, long Estimate) RetainedByARun(bool spatial)
        {
            var tv = O("model", "radial", "points", 64, "spectralSamples", 16, "initialProfile", O("kind", "gaussian", "radiusM", 1e-3));
            var els = Enumerable.Range(0, 12).Select(i => Gap("g" + i, spatial ? (i == 0 ? tv : O("model", "radial", "points", 64, "spectralSamples", 16)) : null, "FusedSilica")).ToArray();   // dispersive: every element makes a new pulse
            var sys = Sys(els);
            var run = Spec(Gauss(Lam, 100e-15, 1e-6), O("grid", O("auto", false, "n", 1 << 15, "windowS", 20e-12)));
            for (int k = 0; k < 3; k++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            long before = GC.GetTotalMemory(true);
            var result = Go(sys, run);
            long after = GC.GetTotalMemory(true);
            long est = result.Compiled.Memory.TotalBytes;
            GC.KeepAlive(result);
            return (after - before, est);
        }

        [Test(Reference = "the estimate is a planning figure: the managed memory a finished run retains lies between a tenth of it and all of it", Tolerance = "0.1 x estimate < retained <= estimate")]
        public void RealRun_RetainedManagedMemory_IsBelowTheEstimate_AndNotFarBelow()
        {
            foreach (var spatial in new[] { false, true })
            {
                var (retained, est) = RetainedByARun(spatial);
                string what = (spatial ? "radial" : "1-D") + ": retained " + retained + " B, estimate " + est;
                Assert.Less((double)retained, (double)est, what);
                Assert.Greater((double)retained, est / 10.0, what + " (the estimate is not inflated)");
            }
        }

        [Test(Reference = "study points run with outputs.keepProfiles \"used\": outputs identical to the run with every profile kept", Tolerance = "exact")]
        public void Study_PointsWithUsedProfiles_GiveTheSameOutputsAsAllProfiles()
        {
            var tv = O("model", "radial", "points", 32, "spectralSamples", 8, "initialProfile", O("kind", "gaussian", "radiusM", 1e-3));
            var sys = Sys(Gap("a", tv, "FusedSilica"), Gap("b", O("model", "radial", "points", 32, "spectralSamples", 8), "FusedSilica"));
            double[] last = null;
            foreach (var keep in new string[] { null, "all" })
            {
                var extra = O("grid", O("auto", false, "n", 2048, "windowS", 10e-12));
                if (keep != null) extra.Set("outputs", O("keepProfiles", keep));
                var run = Spec(Gauss(Lam, 100e-15, 1e-6), extra);
                var st = StudySpec.FromJson(O("mode", "scan", "parameters", A(O("path", "pulse/energyJ", "values", new[] { 1e-6, 2e-6, 4e-6 })), "outputs", A("pulse:end/energyJ", "pulse:end/fwhmS")));
                var res = Studies.Run(sys, run, st);
                var flat = res.Outputs.SelectMany(o => o).ToArray();
                if (last != null) for (int k = 0; k < flat.Length; k++) Assert.True(flat[k] == last[k], "output " + k + " differs: " + flat[k] + " vs " + last[k]);
                last = flat;
            }
        }
    }
}
