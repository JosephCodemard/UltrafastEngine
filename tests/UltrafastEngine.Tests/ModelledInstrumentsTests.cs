using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Numerics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Instruments built from elements, against their analytic results and the ideal instruments; the study node cache and array outputs.</summary>
    [TestArea("Modelled instruments: IAC, FTS, FROG from elements; study node cache and array outputs")]
    public class ModelledInstrumentsTests
    {
        private const double Lam = 1030e-9, C = PhysicalConstants.SpeedOfLightMPerS, Tau = 60e-15;

        private static SystemDescription Branched(params JsonObject[] e) { var s = Sys(e); s.Topology = SystemGraph.Branched; return s; }
        private static JsonObject El(string id, string type, JsonValue next, params object[] kv) => O(new object[] { "id", id, "type", type, "next", next }.Concat(kv).ToArray());
        private static JsonObject Bbo(string process, JsonValue next, params object[] kv) => O(new object[] { "id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 10e-6, "cut", "auto",
            "beam", O("kind", "gaussian", "w0M", 200e-6), "interaction", O("process", process, "type", "I"), "output", process == "shg" ? "sh" : "sum", "next", next }.Concat(kv).ToArray());
        private static JsonObject Crossed(string process) => Bbo("sfg", "end", "aux", A(O("role", "b", "fromPort", true, "beam", O("kind", "gaussian", "radiusM", 200e-6))));

        private static SystemDescription Interferometer(double delayS, JsonObject after, string outNext) => Branched(
            El("bs1", "beamSplitter", O("c", "fixed", "d", "stage")),
            El("fixed", "delay", "bs2:1", "delayS", 0.0), El("stage", "delay", "bs2:2", "delayS", delayS),
            El("bs2", "beamSplitter", O("c", "virtual", "d", outNext)), after);

        [Test(Reference = "interferometric autocorrelation: peak to background 8:1", Tolerance = "1 %")]
        public void Iac_FromElements_HasTheEightToOneRatio()
        {
            double Signal(double d) => Go(Interferometer(d, Bbo("shg", "end"), "x"), Spec(Gauss(Lam, Tau, 1e-9), O("grid", O("n", 1 << 13, "windowS", 8e-12)))).Output.TotalEnergyJ();
            Assert.Close(8, Signal(0) / Signal(10 * Tau), 0.01, 0, "peak / background");
        }

        [Test(Reference = "Wiener-Khinchin: the interferogram's transform is the spectrum", Tolerance = "0.2 % centroid, 2 % width")]
        public void Fts_FromElements_RecoversTheSpectrum()
        {
            double dt = 0.5e-15; int n = 1024;
            var run = Spec(Gauss(Lam, Tau, 1e-9), O("grid", O("n", 1 << 11, "windowS", 2e-12)));
            var signal = new double[n];
            var sys = Enumerable.Range(0, n).ToArray();
            System.Threading.Tasks.Parallel.ForEach(sys, k => signal[k] = Go(Interferometer((k - n / 2) * dt, El("det", "delay", "end", "delayS", 0.0), "det"), run).Output.TotalEnergyJ());
            double mean = signal.Average();
            // a slow DFT over the band of the pulse
            double w0 = 2 * Math.PI * C / Lam, dw = 2 * Math.PI / (n * dt), num = 0, den = 0, num2 = 0;
            var freqs = Enumerable.Range(-120, 241).Select(m => w0 + m * 0.05 * dw).ToArray();
            var spec = freqs.Select(w => { double re = 0, im = 0; for (int k = 0; k < n; k++) { double t = (k - n / 2) * dt; re += (signal[k] - mean) * Math.Cos(w * t); im += (signal[k] - mean) * Math.Sin(w * t); } return Math.Sqrt(re * re + im * im); }).ToArray();   // the transform of the interferogram is S(omega) itself
            for (int i = 0; i < freqs.Length; i++) { num += freqs[i] * spec[i]; den += spec[i]; }
            double centroid = num / den;
            for (int i = 0; i < freqs.Length; i++) num2 += Math.Pow(freqs[i] - centroid, 2) * spec[i];
            double rms = Math.Sqrt(num2 / den);
            var input = Go(Sys(O("id", "p", "type", "probe")), run).Output;
            Assert.Close(SpectralCentroid(input), centroid, 0.002, 0, "centroid");
            Assert.Close(RmsSpectralWidth(input), rms, 0.02, 0, "rms width");
        }

        private static SystemDescription Frog(double delay) => Branched(
            El("bs", "beamSplitter", O("c", "fixed", "d", "stage")),
            El("fixed", "delay", "x", "delayS", 0.0), El("stage", "delay", "x:b", "delayS", delay), Crossed("sfg"));

        [Test(Reference = "delay marginal of an SHG FROG trace: the autocorrelation, FWHM sqrt 2 tau", Tolerance = "2 %")]
        public void Frog_FromElements_HasTheAutocorrelationMarginal()
        {
            var spec = StudySpec.FromJson(O("parameters", A(O("path", "element:stage/delayS", "values", JsonValue.FromArray(Enumerable.Range(-20, 41).Select(k => k * 0.15 * Tau).ToArray()), "nominal", 0.0)),
                "outputs", A("energy:end"), "arrayOutputs", A("spec/spectrum")));
            var res = Studies.Run(Frog(0), Spec(Gauss(Lam, Tau, 1e-9), O("instruments", A(O("kind", "spectrometer", "name", "spec", "normalize", "none")))), spec);
            var trace = res.Arrays.First().Value;
            Assert.Equal(41, trace.Count, "one row per delay");
            double[] delays = res.Parameters.Select(p => p[0]).ToArray(), marginal = trace.Select(r => r.Sum()).ToArray();
            double half = marginal.Max() / 2;
            int i = Array.FindIndex(marginal, v => v >= half);
            double left = delays[i - 1] + (half - marginal[i - 1]) / (marginal[i] - marginal[i - 1]) * (delays[i] - delays[i - 1]);
            Assert.Close(Math.Sqrt(2) * Tau, -2 * left, 0.02, 0, "FWHM of the marginal");
        }

        [Test]
        public void NodeCache_RerunsOnlyWhatFollowsTheChange_WithTheSameResult()
        {
            // an expensive element upstream of the arms: glass with SPM
            var sys = Branched(
                O("id", "glass", "type", "bulk", "material", "FusedSilica", "lengthM", 5e-3, "beam", O("kind", "gaussian", "w0M", 100e-6), "next", "bs"),
                El("bs", "beamSplitter", O("c", "fixed", "d", "stage")),
                El("fixed", "delay", "x", "delayS", 0.0), El("stage", "delay", "x:b", "delayS", 0.0), Crossed("sfg"));
            var run = Spec(Gauss(Lam, Tau, 1e-6));
            JsonObject St(bool cache) => O("parameters", A(O("path", "element:stage/delayS", "values", A(-100e-15, -50e-15, 0.0, 50e-15, 100e-15), "nominal", 0.0)), "outputs", A("energy:end"), "prefixCache", cache);
            var withCache = Studies.Run(sys, run, StudySpec.FromJson(St(true)));
            var without = Studies.Run(sys, run, StudySpec.FromJson(St(false)));
            for (int i = 0; i < 5; i++) Assert.Close(without.Outputs[i][0], withCache.Outputs[i][0], 1e-12, 0, "point " + i);
            // glass, bs and fixed at each of the 5 points, and stage and x at the point equal to the nominal
            Assert.True(withCache.Notes.Any(n => n.Contains("17 element runs reused")), "the cache was used: " + string.Join("; ", withCache.Notes));
        }
    }
}
