// SingleShotCorrelatorTests.cs -- the single-shot time-to-space cross-correlator (Instruments/SingleShotCorrelator.cs).
// References: Ma et al., High Power Laser Sci. Eng. 6, e61 (2018), eqs. (1) and (2); the geometry of the symmetric equal-index case;
// area conservation of the correlation; consistency with the delay-scanning crossCorrelator.
using System;
using System.Linq;
using UltrafastEngine.Crystals;
using UltrafastEngine.Instruments;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Single-shot time-to-space cross-correlator (window, resolution, position to delay)")]
    public class SingleShotCorrelatorTests
    {
        private const double C = 299792458.0, Lam = 1053e-9;

        private static RunResult Run(JsonObject pulse, JsonObject inst, double windowS = 120e-12) =>
            Go(Sys(), Spec(pulse, O("grid", O("windowS", windowS), "instruments", A(inst))));
        private static InstrumentOutput Inst(RunResult r, string name) => r.Instruments.First(i => i.Name == name);
        private static double[] Arr(InstrumentOutput o, string suffix) => o.Arrays.First(a => a.Name == o.Name + "_" + suffix).Data;
        private static JsonObject Ss(params object[] kv)
        {
            var o = O("kind", "singleShotCorrelator", "name", "ss", "noise", false, "dynamicRangeDb", 120.0);
            for (int i = 0; i < kv.Length; i += 2) o.Set((string)kv[i], kv[i + 1] is double d ? (JsonValue)d : kv[i + 1] is int n ? (JsonValue)n : (JsonValue)(string)kv[i + 1]);
            return o;
        }
        private static double Idx(CrystalModel c, double w) { var o = new double[1]; c.Nx.Evaluate(new[] { w }, o); return o[0]; }

        [Test(Reference = "Ma et al. 2018 eq. (1) for two beams of equal index n crossing at the full angle g: T = 2 W n sin(g/2) / c; eq. (2): dtau = 0", Tolerance = "1e-9")]
        public void SymmetricEqualIndexGeometry_GivesTwoNSinHalfAngleOverC_AndNoSmearing()
        {
            double n = 1.5, W = 0.01, g = 40;
            var r = Run(Gauss(Lam, 100e-15, 1e-6), Ss("samplingPulse", "fundamental", "indexPt", n, "indexSampling", n, "crossingAngleDeg", g, "beamWidthM", W, "crystalLengthM", 2e-3, "points", 64), 100e-12);
            var s = Inst(r, "ss"); var v = s.Values;
            double slope = 2 * n * Math.Sin(g / 2 * Math.PI / 180) / C;
            Assert.Close(W * slope, v.GetDouble("windowS", 0), 1e-9, 0, "window");
            Assert.Close(0, v.GetDouble("resolutionGeometricS", 1), 0, 1e-18, "resolution of equal-angle equal-index beams");
            Assert.Close(g / 2, v.GetDouble("angleToSignalPtDeg", 0), 1e-9, 0, "a = g / 2");
            var x = Arr(s, "x_m"); var tau = Arr(s, "delay_s");
            Assert.Close(x[40] * slope, tau[40], 1e-12, 0, "tau = x slope");
        }

        [Test(Reference = "Ma et al. 2018 eqs. (1), (2) with the angles a, b of the inputs to the signal from k_T + k_S = k_sig (vector sum written out independently in the test), BBO 1053 nm + 526.5 nm at 40 deg: about 1 ps resolution for 1-2 mm (p. 2)", Tolerance = "1e-5 (carrier snapped to the grid); 0.5-1.1 ps")]
        public void BboAt40Degrees_FollowsEquations1And2_AndTheQuotedResolution()
        {
            var bbo = CrystalLibrary.Get("BBO");
            double wT = 2 * Math.PI * C / Lam, wS = 2 * wT;
            double nT = Idx(bbo, wT), nS = Idx(bbo, wS), g = 40 * Math.PI / 180, W = 0.015, L = 1.5e-3;
            // independent geometry: k_S along z, k_T at the angle g to it; the signal along k_T + k_S
            double kT = nT * wT, kS = nS * wS;
            double sx = kT * Math.Sin(g), sz = kS + kT * Math.Cos(g), ks = Math.Sqrt(sx * sx + sz * sz);
            double a = Math.Acos((kT * Math.Cos(g) * sz + kT * Math.Sin(g) * sx) / (kT * ks)), b = Math.Acos(sz / ks);
            double window = W / C * (nT * Math.Sin(a) + nS * Math.Sin(b)), dtau = L / C * Math.Abs(nT * Math.Cos(a) - nS * Math.Cos(b));
            var r = Run(Gauss(Lam, 100e-15, 1e-6), Ss("crystal", "BBO", "delayIndex", "phase", "crossingAngleDeg", 40.0, "beamWidthM", W, "crystalLengthM", L), 100e-12);
            var v = Inst(r, "ss").Values;
            // the grid snaps the carrier to its frequency lattice (relative shift ~1e-7), which moves the indices by ~1e-7
            Assert.Close(window, v.GetDouble("windowS", 0), 1e-5, 0, "eq. (1)");
            Assert.Close(dtau, v.GetDouble("resolutionGeometricS", 0), 1e-5, 0, "eq. (2)");
            Assert.Close(a * 180 / Math.PI, v.GetDouble("angleToSignalPtDeg", 0), 1e-5, 0, "angle a");
            Assert.Greater(dtau * 1e12, 0.5, "resolution above 0.5 ps"); Assert.Less(dtau * 1e12, 1.1, "about 1 ps for 1-2 mm of BBO");
            // the group-index delays (default) differ from the phase-index ones by a few per cent, in the direction of n_g > n
            var rg = Run(Gauss(Lam, 100e-15, 1e-6), Ss("crystal", "BBO", "crossingAngleDeg", 40.0, "beamWidthM", W, "crystalLengthM", L), 100e-12);
            double ratio = Inst(rg, "ss").Values.GetDouble("windowS", 0) / window;
            Assert.Greater(ratio, 1.0, "group index above phase index in BBO"); Assert.Less(ratio, 1.1, "a few per cent");
        }

        [Test(Reference = "time-to-space mapping: a pre-pulse of relative intensity e at -T before the main pulse appears at x = -T / slope with height e (third-order correlation, Ma et al. 2018 Fig. 1)", Tolerance = "1 pixel; 2 %")]
        public void PrePulse_AppearsAtThePositionOfItsDelay_WithItsRelativeIntensity()
        {
            var pu = Gauss(Lam, 50e-15, 1e-6);
            pu.Set("pedestal", A(O("kind", "prePulse", "delayS", -10e-12, "relativeIntensity", 1e-3)));
            var r = Run(pu, Ss("crystal", "BBO", "crossingAngleDeg", 30.0, "beamWidthM", 0.01, "crystalLengthM", 1.5e-3, "points", 512));
            var s = Inst(r, "ss"); var x = Arr(s, "x_m"); var ideal = Arr(s, "signal_ideal");
            double slope = s.Values.GetDouble("delayPerPositionSPerM", 0), pixel = 0.01 / 512;
            Assert.Greater(s.Values.GetDouble("windowS", 0), 25e-12, "the window holds -10 ps");
            // the 0.5 ps rectangle of the crystal depth makes a flat top: locate each peak by its centroid
            double Centroid(double centre, double half, out double height)
            {
                double sw = 0, sx = 0; height = 0;
                for (int k = 0; k < x.Length; k++) if (Math.Abs(x[k] - centre) < half) { sw += ideal[k]; sx += ideal[k] * x[k]; height = Math.Max(height, ideal[k]); }
                return sx / sw;
            }
            Assert.Close(0, Centroid(0, 1.5e-3, out double hMain), 0, pixel, "the main peak sits at the centre (zero delay offset)");
            Assert.Close(-10e-12 / slope, Centroid(-10e-12 / slope, 1.5e-3, out double hPre), 0, pixel, "pre-pulse position (one pixel)");
            Assert.Close(1e-3, hPre / hMain, 0.02, 0, "relative intensity of the pre-pulse");
        }

        [Test(Reference = "a delta-like correlation read through a crystal of length L shows the rectangle of width dtau = L |n_T cos a - n_S cos b| / c of eq. (2)", Tolerance = "3 %")]
        public void ShortPulse_TraceWidthIsTheGeometricResolution()
        {
            var r = Run(Gauss(Lam, 30e-15, 1e-6), Ss("crystal", "BBO", "delayIndex", "phase", "crossingAngleDeg", 40.0, "beamWidthM", 0.02, "crystalLengthM", 3e-3, "points", 2048));
            var s = Inst(r, "ss");
            double dtau = s.Values.GetDouble("resolutionGeometricS", 0);
            Assert.Greater(dtau, 1.5e-12, "resolution well above the pulse");
            Assert.Close(dtau, s.Values.GetDouble("fwhmS", 0), 0.03, 0, "FWHM of the trace (rectangle of width dtau, 30 fs correlation, pixel 15 fs)");
        }

        [Test(Reference = "the correlation integral conserves area: sum S dtau = (integral I_PT dt)(integral I_S dt); here the sampling pulse is the pulse itself, so E^2", Tolerance = "1e-3")]
        public void SignalArea_EqualsTheProductOfTheTwoPulseEnergies_WhenTheWindowHoldsTheCorrelation()
        {
            double E = 3e-6;
            var r = Run(Gauss(Lam, 80e-15, E), Ss("samplingPulse", "fundamental", "crystal", "BBO", "crossingAngleDeg", 20.0, "beamWidthM", 0.02, "crystalLengthM", 2e-3, "points", 1024));
            var s = Inst(r, "ss"); var tau = Arr(s, "delay_s"); var raw = Arr(s, "signal_raw");
            double area = raw.Sum() * (tau[1] - tau[0]);
            Assert.Close(E * E, area, 1e-3, 0, "sum over delay of the raw signal = E^2");
            Assert.Close(1, s.Values.GetDouble("windowCoverage", 0), 1e-3, 0, "coverage");
            // a window that cuts the correlation loses the area outside it
            var cut = Run(Gauss(Lam, 80e-15, E), Ss("samplingPulse", "fundamental", "crystal", "BBO", "crossingAngleDeg", 20.0, "beamWidthM", 1e-6, "crystalLengthM", 0.0, "points", 64));
            Assert.Less(Inst(cut, "ss").Values.GetDouble("windowCoverage", 1), 0.2, "window shorter than the pulse");
        }

        [Test(Reference = "with no crystal smearing and fine pixels the single-shot trace is the delay trace of the crossCorrelator (same third-order correlation)", Tolerance = "1 %")]
        public void WithoutSmearing_TheTraceIsTheCrossCorrelatorsTrace()
        {
            var pu = Gauss(Lam, 120e-15, 1e-6);
            var r = Go(Sys(), Spec(pu, O("grid", O("windowS", 40e-12), "instruments", A(
                Ss("crystal", "BBO", "crossingAngleDeg", 30.0, "beamWidthM", 0.02, "points", 8000, "dynamicRangeDb", 200.0),
                O("kind", "crossCorrelator", "name", "cc", "noise", false, "rangeS", new[] { -1e-12, 1e-12 }, "stepS", 2e-15, "dynamicRangeDb", 200.0)))));
            Assert.Close(Inst(r, "cc").Values.GetDouble("fwhmS", 0), Inst(r, "ss").Values.GetDouble("fwhmS", 0), 0.01, 0, "FWHM of the correlation");
        }
    }
}
