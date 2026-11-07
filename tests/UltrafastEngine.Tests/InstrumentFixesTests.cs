// Tests of instrument fixes (Nov 2026 consolidation): energy-meter band edges, interferometric-autocorrelator peak,
// cross-correlator resolution consistency and the arm that sees delayed light. Each against an analytic value.
using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Instrument fixes: band edges, IAC peak, cross-correlator resolution and arms")]
    public class InstrumentFixesTests
    {
        private const double C = 299792458.0;

        private static SystemDescription SysOf(JsonObject materials, params JsonObject[] elements)
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "t");
            if (materials != null) o.Set("materials", materials);
            o.Set("elements", new JsonArray(elements));
            return SystemDescription.FromJson(o);
        }

        private static RunResult Go(SystemDescription s, JsonObject pulse, JsonObject grid, params JsonObject[] instruments)
        {
            var o = O("format", "ultrafast-run", "version", 2);
            o.Set("pulse", pulse);
            if (grid != null) o.Set("grid", grid);
            o.Set("instruments", new JsonArray(instruments.Cast<JsonValue>().ToArray()));
            return Simulation.Run(s, Specs.RunSpec.FromJson(o));
        }

        private static SystemDescription Empty() => SysOf(null, O("id", "d", "type", "dispersion", "gddFs2", 0.0));
        private static double[] Arr(RunResult r, string name, string what) => r.Instruments.Single(i => i.Name == name).Arrays.Single(a => a.Name.EndsWith("_" + what)).Data;
        private static double Cdf(double x) => 0.5 * (1 + Erf(x / Math.Sqrt(2)));
        private static double Erf(double x)
        {   // Simpson integration of the Gaussian: accurate to 1e-12
            int n = 4000; double h = x / n, s = 0;
            for (int i = 0; i <= n; i++) { double w = (i == 0 || i == n) ? 1 : (i % 2 == 1 ? 4 : 2); s += w * Math.Exp(-(i * h) * (i * h)); }
            return 2 / Math.Sqrt(Math.PI) * s * h / 3;
        }

        private static double BandFraction(double lam, double tau, double h)
        {
            double w0 = 2 * Math.PI * C / lam;
            double sig = (4 * Math.Log(2) / tau) / (2 * Math.Sqrt(2 * Math.Log(2)));
            return Cdf((2 * Math.PI * C / (lam - h) - w0) / sig) - Cdf((2 * Math.PI * C / (lam + h) - w0) / sig);
        }

        /// <summary>A transform-limited Gaussian pulse has a Gaussian spectrum, so the energy in a wavelength band is a difference of
        /// normal CDFs. The meter weights each edge bin by the fraction of it inside the band.</summary>
        [Test(Reference = "Gaussian spectrum: band fraction = CDF(w_hi) - CDF(w_lo)", Tolerance = "0.5 %")]
        public void EnergyMeter_BandEdges_AreWeightedByTheFractionOfTheBin()
        {
            double lam = 1030e-9, tau = 100e-15, h = 2e-9;
            double want = BandFraction(lam, tau, h);
            foreach (var grid in new JsonObject[] { null, O("auto", false, "n", 1 << 14, "windowS", 40e-12) })
            {
                var r = Go(Empty(), Gauss(lam, tau, 1e-6), grid, O("kind", "energyMeter", "name", "em", "rangeM", new[] { lam - h, lam + h }));
                double got = r.Instruments.Single().Values.GetDouble("energyJ", double.NaN) / 1e-6;
                Assert.Close(want, got, 0.005, 0, "band fraction");
            }
        }

        /// <summary>The interferometric autocorrelation of any pulse peaks at 8 times its background (Diels and Rudolph, Ultrashort Laser
        /// Pulse Phenomena, 2nd ed., ch. 8). The delay axis must contain d = 0 and sample the fringes.</summary>
        [Test(Reference = "Diels and Rudolph: IAC peak to background 8:1", Tolerance = "0.2 %")]
        public void InterferometricAutocorrelator_PeakToBackground_IsEight_ForEveryCarrierAndBandwidth()
        {
            foreach (var (lam, tau) in new[] { (1030e-9, 100e-15), (1030e-9, 30e-15), (2e-6, 100e-15), (515e-9, 30e-15), (1030e-9, 37e-15) })
            {
                var r = Go(Empty(), Gauss(lam, tau, 1e-9), null, O("kind", "autocorrelator", "type", "interferometric", "name", "iac"));
                double pb = r.Instruments.Single().Values.GetDouble("peakToBackground", double.NaN);
                Assert.Close(8.0, pb, 0.002, 0, "peak to background at " + lam * 1e9 + " nm, " + tau * 1e15 + " fs");
                var d = Arr(r, "iac", "delay_s");
                Assert.True(d.Length % 2 == 1 && Math.Abs(d[d.Length / 2]) < 1e-21, "the delay axis contains zero");
            }
        }

        private static double Db(double v) => 10 * Math.Log10(v);

        /// <summary>The finite delay resolution smears the main peak and the pre-pulse alike, so the pre-pulse still reads its relative
        /// intensity (-60 dB for 1e-6) and the floor stays at dynamicRangeDb below the resolution-limited peak, in the trace and in the report.</summary>
        [Test(Reference = "linear smoothing: relative height of a distant pre-pulse is unchanged", Tolerance = "0.3 dB")]
        public void CrossCorrelator_WithResolution_TraceFloorAndReportAreConsistent()
        {
            var pu = Gauss(800e-9, 50e-15, 1e-6);
            pu.Set("pedestal", O("kind", "prePulse", "delayS", -10e-12, "relativeIntensity", 1e-6));
            foreach (double res in new[] { 0.0, 1e-12 })
            {
                var xc = O("kind", "crossCorrelator", "name", "xc", "rangeS", new[] { -45e-12, 10e-12 }, "stepS", 0.5e-12, "fineStepS", 50e-15, "fineRangeS", 50e-12,
                    "dynamicRangeDb", 105.0, "noise", false, "probeDelaysS", new[] { -10e-12 });
                if (res > 0) xc.Set("resolutionS", res);
                var r = Go(Empty(), pu, O("windowS", 120e-12), xc);
                var d = Arr(r, "xc", "delay_s"); var s = Arr(r, "xc", "signal");
                double mx = s.Max();
                double Level(double t) { double m = 0; for (int i = 0; i < d.Length; i++) if (Math.Abs(d[i] - t) < 0.4e-12) m = Math.Max(m, s[i] / mx); return Db(m); }
                Assert.Close(-60.0, Level(-10e-12), 0, 0.3, "trace pre-pulse, resolution " + res);
                Assert.Close(-105.0, Level(-20e-12), 0, 0.3, "trace floor, resolution " + res);
                Assert.Close(-60.0, r.Instruments.Single().Values.GetDouble("contrastAt_m10ps_db", double.NaN), 0, 0.3, "reported contrast, resolution " + res);
            }
        }

        private static SystemDescription GhostPlate(double r)
            => SysOf(O("ar", O("kind", "ar")), O("id", "w", "type", "plate", "material", "FusedSilica", "thicknessM", 5e-3, "coating", O("kind", "ar", "R", r), "ghosts", true));

        private static double GhostLevel(string arm, double sign, double R)
        {
            double ng = Materials.MaterialLibrary.BuiltIn("FusedSilica").GroupIndexAt(2 * Math.PI * C / 1030e-9), T = 2 * ng * 5e-3 / C;
            var xc = O("kind", "crossCorrelator", "name", "xc", "rangeS", new[] { sign * T - 3e-12, sign * T + 3e-12 }, "stepS", 10e-15, "noise", false, "noiseFloor", 1e-16);
            if (arm != null) xc.Set("delayedLightArm", arm);
            var r = Go(GhostPlate(R), Gauss(1030e-9, 100e-15, 1e-6), O("tail", true), xc);
            return Arr(r, "xc", "signal_ideal").Max();
        }

        /// <summary>A plate's first ghost replica has intensity R^2 (R per face). In the sampling arm the correlator sees it at +T with that
        /// level; in the gate arm the SHG gate |E^2|^2 squares it (R^4) and it appears at -T; with both arms it shows at +T (R^2) and -T (R^4).</summary>
        [Test(Reference = "Airy: first ghost intensity R^2; gate = |E^2|^2 gives R^4", Tolerance = "5 %")]
        public void CrossCorrelator_DelayedLightArm_PutsTheGhostInTheSamplingOrTheGateArm()
        {
            double R = 0.01;
            Assert.Close(R * R, GhostLevel(null, +1, R), 0.05, 0, "default = sampling arm, +T");
            Assert.Close(R * R, GhostLevel("sampling", +1, R), 0.05, 0, "sampling arm, +T");
            Assert.Less(GhostLevel("sampling", -1, R), 1e-12, "sampling arm: nothing at -T");
            Assert.Close(Math.Pow(R, 4), GhostLevel("gate", -1, R), 0.05, 0, "gate arm, -T");
            Assert.Less(GhostLevel("gate", +1, R), 1e-12, "gate arm: nothing at +T");
            Assert.Close(R * R, GhostLevel("both", +1, R), 0.05, 0, "both, +T");
            Assert.Close(Math.Pow(R, 4), GhostLevel("both", -1, R), 0.05, 0, "both, -T");
        }
    }
}
