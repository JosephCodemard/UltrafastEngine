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

        /// <summary>A long pulse needs more delay points than the "points" cap: the axis still contains d = 0 (so the peak is 8) and a note says
        /// that the fringes are under-sampled.</summary>
        [Test(Reference = "Diels and Rudolph: IAC peak to background 8:1", Tolerance = "0.2 %")]
        public void InterferometricAutocorrelator_LongPulse_KeepsZeroDelay_AndSaysTheFringesAreUndersampled()
        {
            var r = Go(Empty(), Gauss(1030e-9, 1e-12, 1e-9), null, O("kind", "autocorrelator", "type", "interferometric", "name", "iac"));
            Assert.Close(8.0, r.Instruments.Single().Values.GetDouble("peakToBackground", double.NaN), 0.002, 0, "peak to background, 1 ps");
            Assert.True(r.Instruments.Single().Notes.Any(n => n.Contains("under-sampled")), "note");
            var d = Arr(r, "iac", "delay_s");
            Assert.True(d.Length % 2 == 1 && Math.Abs(d[d.Length / 2]) < 1e-21, "zero delay is on the axis");
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

        private const double PlateD = 1.0e-3, GateLam = 1030e-9;

        private static (double[] Delay, double[] Signal) GateArmRun(JsonObject gateArm, double center, double half)
        {
            var xc = O("kind", "crossCorrelator", "name", "xc", "rangeS", new[] { center - half, center + half }, "stepS", 10e-15, "noise", false, "noiseFloor", 1e-16);
            if (gateArm != null) xc.Set("gateArm", gateArm);
            var r = Go(Empty(), Gauss(GateLam, 200e-15, 1e-6), O("auto", false, "n", 1 << 14, "windowS", 100e-12), xc);
            return (Arr(r, "xc", "delay_s"), Arr(r, "xc", "signal_ideal"));
        }

        private static JsonObject PlateArm() => O("elements", A(O("id", "plate", "type", "plate", "material", "FusedSilica", "thicknessM", PlateD, "ghosts", O("orders", 3, "overlap", 1.0))));

        /// <summary>A plate with ghosts in the gate arm only (the second-harmonic gate, as in the single-shot cross-correlator of Ma et al.,
        /// HPLSE 2018, Fig. 4c): the k-th ghost replica of the gate has intensity R^(2k) (R the Fresnel reflectance of an uncoated face at the
        /// second harmonic, evaluated at lambda0/2) at k times 2 n_g d / c, with n_g at the second harmonic. The probe channel is untouched.</summary>
        [Test(Reference = "Fresnel: ghost k of a plate has intensity R^(2k); spacing 2 n_g(lambda/2) d / c", Tolerance = "3 %, spacing 0.1 %")]
        public void CrossCorrelator_GateArmPlate_GivesGhostLevelsRToThe2k_AtTheSecondHarmonicSpacing()
        {
            var fs = Materials.MaterialLibrary.BuiltIn("FusedSilica");
            double w2 = 2 * 2 * Math.PI * C / GateLam;
            double n2 = fs.IndexAt(w2), ng2 = fs.GroupIndexAt(w2), R = Math.Pow((n2 - 1) / (n2 + 1), 2), T = 2 * ng2 * PlateD / C;
            for (int k = 1; k <= 3; k++)
            {
                var (d, s) = GateArmRun(PlateArm(), -k * T, 1.5e-12);
                int ip = Array.IndexOf(s, s.Max());
                Assert.Close(Math.Pow(R, 2 * k), s[ip], 0.03, 0, "ghost " + k + " level");
                Assert.Close(-k * T, d[ip], 0, 0.1e-3 * T * k + 10e-15, "ghost " + k + " position");
            }
            // nothing at +T: the probe channel has no ghost
            var (dp, sp) = GateArmRun(PlateArm(), +T, 1.5e-12);
            Assert.Less(sp.Max(), 1e-12, "no replica at +T");
            // the main peak is unchanged apart from the plate's own transmission (relative trace)
            var (d0, s0) = GateArmRun(null, 0, 1e-12);
            Assert.Close(1.0, s0.Max(), 1e-9, 0, "without a gate arm the peak is the normalisation");
        }
    }
}
