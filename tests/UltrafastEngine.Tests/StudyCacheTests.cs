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
    
        /// <summary>A fake ensemble back-end: records the batch sizes and returns each job's initial field times its index.</summary>
        private sealed class FakeEnsemble : Numerics.IEnsembleBackend
        {
            public readonly System.Collections.Concurrent.ConcurrentBag<int> Sizes = new System.Collections.Concurrent.ConcurrentBag<int>();
            public string Name => "fake"; public string Description => "fake"; public int Priority => 0;
            public System.Collections.Generic.IReadOnlyList<Numerics.ComputeDeviceInfo> Devices => new Numerics.ComputeDeviceInfo[0];
            public string Unavailable => null;
            public JsonObject SelfTest(int device, bool benchmark) => new JsonObject();
            public string WhyNot(Physics.IPropagationProblem p) => null;
            public bool Supports(Physics.IPropagationProblem p) => true;
            public System.Numerics.Complex[] Propagate(Physics.IPropagationProblem p, System.Numerics.Complex[] a0, Physics.StepperSettings st, Physics.StepStatistics stats, Action<double> progress) => a0;
            public string EnsembleKey(Physics.IPropagationProblem p, Physics.StepperSettings st) => "k";
            public void PropagateEnsemble(System.Collections.Generic.IList<Numerics.EnsembleJob> jobs)
            {
                Sizes.Add(jobs.Count);
                foreach (var j in jobs) j.Result = new[] { j.Initial[0] * 2 };
            }
        }

        [Test(Category = "studies")]
        public void EnsembleGate_BatchesWaitingThreads_AndNeverWaitsForFinishedOnes()
        {
            // the gate behind device ensembles (Numerics/Ensemble.cs): 8 threads with 0..4 jobs each and random delays; every job
            // must come back, no thread may wait for one that has finished, and jobs that arrive together share a batch
            var fake = new FakeEnsemble();
            var gate = new Numerics.EnsembleGate();
            var results = new System.Collections.Concurrent.ConcurrentDictionary<int, double>();
            var threads = new System.Threading.Thread[8];
            for (int t = 0; t < threads.Length; t++) gate.Enter();
            for (int t = 0; t < threads.Length; t++)
            {
                int tt = t;
                threads[t] = new System.Threading.Thread(() =>
                {
                    var rnd = new Random(tt);
                    try
                    {
                        for (int k = 0; k < tt % 5; k++)
                        {
                            System.Threading.Thread.Sleep(rnd.Next(0, 20));
                            int id = tt * 10 + k;
                            var r = gate.Submit(new Numerics.EnsembleJob { Initial = new System.Numerics.Complex[] { id } }, fake);
                            results[id] = r[0].Real;
                        }
                    }
                    finally { gate.Leave(); }
                });
                threads[t].Start();
            }
            foreach (var th in threads) Assert.True(th.Join(20000), "no deadlock");
            int expected = Enumerable.Range(0, 8).Sum(t => t % 5);
            Assert.Equal(expected, results.Count, "every job came back");
            foreach (var kv in results) Assert.Close(2.0 * kv.Key, kv.Value, 0, 0, "result of job " + kv.Key);
            Assert.Equal(expected, fake.Sizes.Sum(), "members counted once");
            Assert.Equal(expected, gate.Members, "gate counters");
            Assert.True(gate.Largest > 1, "simultaneous jobs shared a batch (largest " + gate.Largest + ")");
        }
    }
}
