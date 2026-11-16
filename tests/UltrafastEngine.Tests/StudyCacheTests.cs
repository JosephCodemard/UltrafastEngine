using System;
using System.IO;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Parameter studies: the frozen grid serves every point (grid union), and the point cache resumes a study.</summary>
    [TestArea("Study grid union and point cache")]
    public class StudyCacheTests
    {
        private static (SystemDescription, RunSpec) Plate()
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "plate");
            o.Set("elements", A(O("id", "p", "type", "bulk", "material", "FusedSilica", "lengthM", 0.02, "beam", O("kind", "constant", "areaM2", 1e-8))));
            var r = O("format", "ultrafast-run", "version", 2, "name", "r", "physics", O("raman", false));
            r.Set("pulse", O("shape", "gaussian", "centerWavelengthM", 1030e-9, "durationFwhmS", 200e-15, "peakPowerW", 1e6));
            return (SystemDescription.FromJson(o), RunSpec.FromJson(r));
        }

        [Test]
        public void Study_FrozenGridServesTheWholeRange()
        {
            var (s, r) = Plate();
            // the pulse changes from 60 fs to 2 ps: the nominal (mid-range) plan alone would be too coarse or too short for the ends
            var spec = StudySpec.FromJson(O("mode", "scan", "parameters", A(O("path", "pulse/durationFwhmS", "values", new[] { 60e-15, 1e-12, 2e-12 })), "outputs", A("pulse:end/energyJ", "pulse:end/fwhmS")));
            var res = Studies.Run(s, r, spec);
            Assert.True(res.Notes.Any(n => n.StartsWith("grid union")), "union reported: " + string.Join(" | ", res.Notes));
            for (int i = 0; i < res.Parameters.Count; i++)
            {
                var rj = (JsonObject)JsonValue.Parse(r.Raw.ToJson(false));
                rj.GetObject("pulse").Set("durationFwhmS", res.Parameters[i][0]);
                var alone = Simulation.Run(s, RunSpec.FromJson(rj));
                var m = Results.PulseMetrics.Summarize(alone.Output);
                Assert.Close(m.FwhmS, res.Outputs[i][1], 0.01, 0, "width of point " + i + " on the frozen grid equals the independent run");
                Assert.Close(m.EnergyJ, res.Outputs[i][0], 1e-6, 0, "energy of point " + i);
            }
        }

        [Test]
        public void Study_PointCacheResumesAndInvalidates()
        {
            var (s, r) = Plate();
            string dir = Path.Combine(Path.GetTempPath(), "uf-study-cache-" + Guid.NewGuid().ToString("N"));
            try
            {
                JsonObject Spec(double lo, string cache) => O("mode", "scan", "cache", cache, "parameters", A(O("path", "element:p/lengthM", "min", lo, "max", 0.03, "points", 4)), "outputs", A("record:p/BIntegralRad"));
                var first = Studies.Run(s, r, StudySpec.FromJson(Spec(0.01, dir)));
                Assert.True(first.Notes.Any(n => n.Contains("0 of 4 points read")), "empty cache: " + string.Join(" | ", first.Notes));
                var second = Studies.Run(s, r, StudySpec.FromJson(Spec(0.01, dir)));
                Assert.True(second.Notes.Any(n => n.Contains("4 of 4 points read")), "all from the cache: " + string.Join(" | ", second.Notes));
                for (int i = 0; i < 4; i++) Assert.True(first.Outputs[i][0] == second.Outputs[i][0], "cached value is bit-identical");
                // a different range shares the end points (0.03) and the nominal-independent points only when the frozen grid is the same;
                // a changed system must not hit the cache at all
                var o2 = (JsonObject)JsonValue.Parse(s.ToJson().ToJson(false));
                o2.GetArray("elements")[0].AsObject().Set("lengthM", 0.021);
                var third = Studies.Run(SystemDescription.FromJson(o2), r, StudySpec.FromJson(Spec(0.01, dir)));
                Assert.True(third.Notes.Any(n => n.Contains("0 of 4 points read")), "a changed system does not use old points: " + string.Join(" | ", third.Notes));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
