// Double FROG (Kida et al., Opt. Express 22, 3004 (2014)): several traces that share unknown pulses, retrieved together.
using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Results;
using UltrafastEngine.Retrieval;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>The SHG-FROG of a reference pulse, the SHG-FROG of a test pulse and the XFROG trace of the test pulse gated by the reference,
    /// retrieved together.</summary>
    [TestArea("Multi-trace FROG retrieval (double FROG)")]
    public class MultiTraceRetrievalTests
    {
        private const double RefLam = 800e-9, RefFwhm = 30e-15, RefGdd = 195, TestLam = 1200e-9, TestFwhm = 40e-15, TestGdd = 580;

        private static RunResult Measure(double lam, double fwhm, double gddFs2, JsonObject instrument)
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "r");
            o.Set("elements", A(O("id", "x", "type", "probe")));
            var run = O("format", "ultrafast-run", "version", 2);
            run.Set("pulse", O("shape", "gaussian", "centerWavelengthM", lam, "durationFwhmS", fwhm, "energyJ", 1e-9, "chirp", O("gddFs2", gddFs2)));
            run.Set("instruments", A(instrument));
            return Simulation.Run(SystemDescription.FromJson(o), Specs.RunSpec.FromJson(run));
        }

        private static double GddOf(RetrievalResult r)
        {
            var I = r.Spectrum.Select(z => z.Magnitude * z.Magnitude).ToArray(); double mx = I.Max();
            var ph = UltrafastEngine.Numerics.PhaseUnwrap.Unwrap(r.Spectrum.Select(z => z.Phase).ToArray());
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, t0 = 0, t1 = 0, t2 = 0;
            for (int i = 0; i < I.Length; i++)
            {
                if (I[i] < 0.05 * mx) continue;
                double w = I[i], x = r.Omega[i] - r.Omega0, y = ph[i];
                s0 += w; s1 += w * x; s2 += w * x * x; s3 += w * x * x * x; s4 += w * x * x * x * x; t0 += w * y; t1 += w * x * y; t2 += w * x * x * y;
            }
            double D(double a, double b, double c, double d, double e, double f, double g, double h, double k) => a * (e * k - f * h) - b * (d * k - f * g) + c * (d * h - e * g);
            return 2 * D(s0, s1, t0, s1, s2, t1, s2, s3, t2) / D(s0, s1, s2, s1, s2, s3, s2, s3, s4);
        }

        /// <summary>Three traces, one unknown pulse each plus the link: every start (six seeds) must give the right time direction of both pulses,
        /// which SHG-FROG alone cannot (E(t) and E*(-t) have the same trace). Truth: GDD +195 and +580 fs^2.</summary>
        [Test(Reference = "Kida et al. 2014: double FROG removes the SHG-FROG time-direction ambiguity of both pulses", Tolerance = "6 % GDD, 5 % duration, sign exact")]
        public void DoubleFrog_SixStarts_AllGiveTheRightTimeDirectionForBothPulses()
        {
            var refPulse = O("shape", "gaussian", "centerWavelengthM", RefLam, "durationFwhmS", RefFwhm, "chirp", O("gddFs2", RefGdd));
            var rr = Measure(RefLam, RefFwhm, RefGdd, O("kind", "frog", "type", "shg", "name", "fr", "points", 128));
            var rt = Measure(TestLam, TestFwhm, TestGdd, O("kind", "frog", "type", "shg", "name", "ft", "points", 128));
            var rx = Measure(TestLam, TestFwhm, TestGdd, O("kind", "xfrog", "name", "fx", "points", 128, "reference", refPulse));
            double fwRef = PulseMetrics.Summarize(rr.Output).FwhmS, fwTest = PulseMetrics.Summarize(rt.Output).FwhmS;
            var terms = new List<MultiTraceTerm>
            {
                new MultiTraceTerm { Data = TraceData.FromInstrument(rr, "fr"), Test = "ref" },
                new MultiTraceTerm { Data = TraceData.FromInstrument(rt, "ft"), Test = "test" },
                new MultiTraceTerm { Data = TraceData.FromInstrument(rx, "fx"), Test = "test", Gate = "ref" },
            };
            for (int seed = 1; seed <= 6; seed++)
            {
                var res = MultiTraceRetrieval.Run(terms, O("maxIterations", 600, "seed", seed));
                Assert.Less(res.TraceErrors.Max(), 1.5e-2, "seed " + seed + ": trace errors " + string.Join(",", res.TraceErrors));
                Assert.Close(RefGdd, GddOf(res.Pulses["ref"]) * 1e30, 0.06, 0, "seed " + seed + ": reference GDD with its sign");
                Assert.Close(TestGdd, GddOf(res.Pulses["test"]) * 1e30, 0.06, 0, "seed " + seed + ": test GDD with its sign");
                Assert.Close(fwRef, res.Pulses["ref"].FwhmS, 0.05, 0, "seed " + seed + ": reference duration");
                Assert.Close(fwTest, res.Pulses["test"].FwhmS, 0.05, 0, "seed " + seed + ": test duration");
            }
        }
    }
}
