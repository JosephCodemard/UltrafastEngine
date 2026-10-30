using System;
using System.Linq;
using UltrafastEngine.Results;
using UltrafastEngine.Retrieval;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Stage 1.7: pulse retrieval from simulated FROG and d-scan traces.</summary>
    [TestArea("Pulse retrieval (FROG, d-scan)")]
    public class RetrievalTests
    {
        private const double Lam = 800e-9;

        private static RunResult Measure(JsonObject instrument, double gddFs2, double todFs3)
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "r");
            o.Set("elements", A(O("id", "x", "type", "probe")));
            var run = O("format", "ultrafast-run", "version", 2);
            run.Set("pulse", O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 25e-15, "energyJ", 1e-9, "chirp", O("gddFs2", gddFs2, "todFs3", todFs3)));
            run.Set("instruments", A(instrument));
            return Simulation.Run(SystemDescription.FromJson(o), Specs.RunSpec.FromJson(run));
        }

        private static double Gdd(RetrievalResult r)
        {
            // GDD at the carrier from the retrieved spectral phase (weighted quadratic fit over the spectrum)
            var I = r.Spectrum.Select(z => z.Magnitude * z.Magnitude).ToArray(); double mx = I.Max();
            var ph = UltrafastEngine.Numerics.PhaseUnwrap.Unwrap(r.Spectrum.Select(z => z.Phase).ToArray());
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, t0 = 0, t1 = 0, t2 = 0;
            for (int i = 0; i < I.Length; i++)
            {
                if (I[i] < 0.05 * mx) continue;
                double w = I[i], x = r.Omega[i] - r.Omega0, y = ph[i];
                s0 += w; s1 += w * x; s2 += w * x * x; s3 += w * x * x * x; s4 += w * x * x * x * x; t0 += w * y; t1 += w * x * y; t2 += w * x * x * y;
            }
            // solve [s0 s1 s2; s1 s2 s3; s2 s3 s4] [a b c] = [t0 t1 t2]; GDD = 2c
            double D(double a, double b, double c, double d, double e, double f, double g, double h, double k) => a * (e * k - f * h) - b * (d * k - f * g) + c * (d * h - e * g);
            double det = D(s0, s1, s2, s1, s2, s3, s2, s3, s4);
            double cc = D(s0, s1, t0, s1, s2, t1, s2, s3, t2) / det;
            return 2 * cc;
        }

        [Test(Reference = "known chirped pulse", Tolerance = "3 %, 5 %")]
        public void Frog_PcgpaAndEpieRecoverAChirpedPulse()
        {
            double gdd = 300;
            var r = Measure(O("kind", "frog", "name", "frog", "points", 128), gdd, 0);
            var trace = TraceData.FromInstrument(r, "frog");
            double trueFwhm = PulseMetrics.Summarize(r.Output).FwhmS;
            foreach (var m in new[] { "pcgpa", "epie" })
            {
                var res = Retrievals.Run(m, trace, O("maxIterations", m == "epie" ? 150 : 300));
                Assert.Less(res.Error, 5e-3, m + ": FROG error");
                Assert.Close(trueFwhm, res.FwhmS, 0.03, 0, m + ": pulse duration");
                Assert.Close(gdd, Math.Abs(Gdd(res)) * 1e30, 0.05, 0, m + ": |GDD| (sign is ambiguous in SHG-FROG)");
            }
        }

        [Test(Reference = "known spectral phase", Tolerance = "5 %, 6 %")]
        public void DScan_RecoversPhaseAndFeedsARun()
        {
            double gdd = -40, tod = 400;
            var r = Measure(O("kind", "dscan", "name", "ds", "material", "FusedSilica", "insertionM", new[] { -3e-3, 3e-3 }, "steps", 41), gdd, tod);
            var t = TraceData.FromInstrument(r, "ds");
            t.FundamentalOmega = r.Input.Grid.OmegaAbsolute; t.FundamentalIntensity = r.Input.SpectralIntensity();
            var res = Retrievals.Run("dscan", t, O("nodes", 12));
            Assert.Less(res.Error, 0.01, "d-scan residual");
            Assert.Close(gdd, Gdd(res) * 1e30, 0, 8, "GDD (d-scan resolves its sign)");
            double trueFwhm = PulseMetrics.Summarize(r.Output).FwhmS;
            Assert.Close(trueFwhm, res.FwhmS, 0.05, 0, "pulse duration");
            // the retrieved field as the input of a new run
            var run = O("format", "ultrafast-run", "version", 2); run.Set("pulse", res.ToPulseJson(1e-9));
            var o = O("format", "ultrafast-system", "version", 2, "name", "again"); o.Set("elements", A(O("id", "x", "type", "probe")));
            var again = Simulation.Run(SystemDescription.FromJson(o), Specs.RunSpec.FromJson(run));
            Assert.Close(trueFwhm, PulseMetrics.Summarize(again.Output).FwhmS, 0.06, 0, "retrieved pulse reused as a run input");
        }
    }
}
