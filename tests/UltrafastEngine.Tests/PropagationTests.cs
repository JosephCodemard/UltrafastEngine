using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using UltrafastEngine.Pulses;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Nonlinear envelope propagation")]
    public class PropagationTests
    {
        private const double Lam = 1030e-9;
        private const double N2 = 2.5e-20, Area = 1e-8;
        private static double Gamma => N2 * W(Lam) / (C * Area);

        internal static SystemDescription FlatKerrSystem(double lengthM, double n2 = N2, JsonObject extraMaterial = null, string material = "flat")
        {
            var mats = O("flat", FlatMaterial(n2));
            if (extraMaterial != null) mats.Set("custom", extraMaterial);
            var s = O("format", "ultrafast-system", "name", "flat kerr", "materials", mats);
            s.Set("elements", A(O("id", "nl", "type", "bulk", "material", material, "lengthM", lengthM, "beam", O("kind", "constant", "areaM2", Area))));
            return SystemDescription.FromJson(s);
        }

        private static JsonObject Physics(bool ss = false, bool raman = true, string field = "envelope") =>
            O("physics", O("selfSteepening", ss, "raman", raman, "fieldMode", field));

        [Test(Reference = "SPM: analytic phase γ P L, spectral broadening of a Gaussian (Agrawal)", Tolerance = "1e-06, 1 %")]
        public void Spm_GaussianBroadeningMatchesAnalyticAndBIntegral()
        {
            double L = 0.01, phi = 5.0, p0 = phi / (Gamma * L);
            var run = Spec(O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 100e-15, "peakPowerW", p0), Physics());
            var r = Go(FlatKerrSystem(L), run);
            double ratio = RmsSpectralWidth(r.Output) / RmsSpectralWidth(r.Input);
            double expected = Math.Sqrt(1 + 4.0 / (3 * Math.Sqrt(3)) * phi * phi); // Agrawal, Nonlinear Fiber Optics, eq. 4.1.15 (m = 1)
            Assert.Close(expected, ratio, 0.01, 0, "rms spectral broadening");
            Assert.Close(phi, r.Records[0].BIntegralRad, 0.01, 0, "B-integral");
            Assert.Close(r.Input.TotalEnergyJ(), r.Output.TotalEnergyJ(), 1e-6, 0, "energy");
        }

        internal static JsonObject Beta2Material(double beta2, double n2)
        {
            double w0 = W(Lam), n0 = 1.45;
            return O("index", O("kind", "expression", "expr", "c*(k0 + k1*(omega - w0) + 0.5*b2*(omega - w0)^2)/omega",
                "params", O("k0", n0 * w0 / C, "k1", n0 / C, "b2", beta2, "w0", w0)), "n2", n2);
        }

        [Test(Reference = "fundamental soliton of the NLSE", Tolerance = "1 %")]
        public void Soliton_FundamentalSolitonKeepsItsShape()
        {
            double beta2 = -2e-26, t0 = 50e-15;
            double ld = t0 * t0 / Math.Abs(beta2), z0 = Math.PI / 2 * ld;
            double p0 = Math.Abs(beta2) / (Gamma * t0 * t0);
            var sys = FlatKerrSystem(z0, N2, Beta2Material(beta2, N2), "custom");
            var run = Spec(O("shape", "sech2", "centerWavelengthM", Lam, "durationFwhmS", 1.7627 * t0, "peakPowerW", p0), Physics());
            var r = Go(sys, run);
            var a = PulseMetrics.Summarize(r.Input); var b = PulseMetrics.Summarize(r.Output);
            Assert.Close(a.PeakPowerW, b.PeakPowerW, 0.01, 0, "soliton peak power after one period");
            Assert.Close(a.FwhmS, b.FwhmS, 0.01, 0, "soliton FWHM after one period");
            // the same pulse without the nonlinearity broadens strongly
            var lin = Go(FlatKerrSystem(z0, N2, Beta2Material(beta2, 0), "custom"), run);
            Assert.Greater(PulseMetrics.Summarize(lin.Output).FwhmS, 1.5 * a.FwhmS, "dispersion-only broadening");
        }

        [Test(Slow = true)]
        public void Steppers_AdaptiveAgreesWithFixedRk4Ip()
        {
            double beta2 = -2e-26, t0 = 50e-15, ld = t0 * t0 / Math.Abs(beta2);
            double p0 = 4 * Math.Abs(beta2) / (Gamma * t0 * t0); // N = 2
            var sys = FlatKerrSystem(0.5 * ld, N2, Beta2Material(beta2, N2), "custom");
            var pulse = O("shape", "sech2", "centerWavelengthM", Lam, "durationFwhmS", 1.7627 * t0, "peakPowerW", p0);
            var fixedRun = Spec(pulse, O("physics", O("selfSteepening", true), "stepper", O("method", "rk4ip", "maxPhasePerStepRad", 0.0005), "grid", O("n", 2048, "auto", false, "windowS", 3e-12)));
            var adaptRun = Spec(pulse, O("physics", O("selfSteepening", true), "stepper", O("method", "adaptive", "tolerance", 1e-8), "grid", O("n", 2048, "auto", false, "windowS", 3e-12)));
            var f = Go(sys, fixedRun); var ad = Go(sys, adaptRun);
            double num = 0, den = 0;
            for (int i = 0; i < f.Output.Grid.N; i++) { num += (f.Output.Ax[i] - ad.Output.Ax[i]).Magnitude * (f.Output.Ax[i] - ad.Output.Ax[i]).Magnitude; den += f.Output.Ax[i].Magnitude * f.Output.Ax[i].Magnitude; }
            Assert.Less(Math.Sqrt(num / den), 1e-4, "relative L2 difference fixed vs adaptive");
            Assert.Less(ad.Records[0].Steps.Accepted, f.Records[0].Steps.Accepted, "adaptive needs fewer steps");
        }

        [Test]
        public void SelfSteepening_MovesPeakToTrailingEdgeAndBroadensBlueSide()
        {
            double L = 0.01, p0 = 12.0 / (Gamma * L);
            var pulse = O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 30e-15, "peakPowerW", p0);
            var with = Go(FlatKerrSystem(L), Spec(pulse, Physics(ss: true)));
            var without = Go(FlatKerrSystem(L), Spec(pulse, Physics(ss: false)));
            var g = with.Output.Grid;
            double tPeak(PulseField p) { var pt = PulseMetrics.TemporalPower(p); return g.Time[Array.IndexOf(pt, pt.Max())]; }
            Assert.Greater(tPeak(with.Output), 2e-15, "peak moves to the trailing edge (later time)");
            Assert.Less(Math.Abs(tPeak(without.Output)), 1e-15, "symmetric without self-steepening");
            var s = with.Output.SpectralIntensity(); double mx = s.Max(); double w0 = g.OmegaCenter;
            int lo = Array.FindIndex(s, v => v > 1e-3 * mx), hi = Array.FindLastIndex(s, v => v > 1e-3 * mx);
            Assert.Greater(g.OmegaAbsolute[hi] - w0, w0 - g.OmegaAbsolute[lo], "blue side extends further");
        }

        [Test]
        public void Raman_RedShiftsTheSpectrum()
        {
            // fused silica Raman response, dispersion removed so only the nonlinear response acts
            var mat = O("base", "FusedSilica", "index", O("kind", "constant", "value", 1.45), "n2", N2);
            double L = 0.01, p0 = 15.0 / (Gamma * L);
            var pulse = O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 50e-15, "peakPowerW", p0);
            var sys = FlatKerrSystem(L, N2, mat, "custom");
            var on = Go(sys, Spec(pulse, Physics(raman: true)));
            var off = Go(sys, Spec(pulse, Physics(raman: false)));
            Assert.Less(SpectralCentroid(on.Output), SpectralCentroid(off.Output) - 1e-4 * W(Lam), "Raman lowers the spectral centroid");
            Assert.Close(SpectralCentroid(off.Input), SpectralCentroid(off.Output), 1e-6, 0, "no shift without Raman");
        }

        [Test(Reference = "cross-model: the envelope model", Tolerance = "0.1 %, 2 %")]
        public void FieldMode_AgreesWithEnvelopeForSpm()
        {
            var mat = O("base", "FusedSilica", "n2", N2);
            double L = 5e-3, p0 = 4.0 / (Gamma * L);
            var pulse = O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 60e-15, "peakPowerW", p0);
            var sys = FlatKerrSystem(L, N2, mat, "custom");
            var env = Go(sys, Spec(pulse, Physics(ss: true, raman: false, field: "envelope")));
            var fld = Go(sys, Spec(pulse, Physics(ss: true, raman: false, field: "carrier")));
            Assert.True(fld.Records[0].Steps.Representation.StartsWith("carrier"), "field mode used");
            Assert.Close(RmsSpectralWidth(env.Output), RmsSpectralWidth(fld.Output), 0.02, 0, "rms spectral width");
            Assert.Close(env.Output.TotalEnergyJ(), fld.Output.TotalEnergyJ(), 1e-3, 0, "energy");
            var a = env.Output.SpectralIntensity(); var b = fld.Output.SpectralIntensity();
            double num = 0, den = 0; for (int i = 0; i < a.Length; i++) { num += Math.Abs(a[i] - b[i]); den += a[i]; }
            Assert.Less(num / den, 0.05, "spectra overlap");
        }

        [Test]
        public void TimeResponse_CallbackAndExpressionReproduceBuiltInKerr()
        {
            double L = 0.01, p0 = 3.0 / (Gamma * L);
            var pulse = O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 100e-15, "peakPowerW", p0);
            var grid = O("n", 2048, "windowS", 4e-12, "auto", false); // identical grids for a sample-by-sample comparison
            var phys = Physics(); phys.Set("grid", grid);
            var builtIn = Go(FlatKerrSystem(L), Spec(pulse, phys));

            var reg = ModelRegistry.CreateDefault();
            reg.RegisterCallback("myKerr", (TimeResponseCallback)((ctx, re, im) => { for (int i = 0; i < ctx.N; i++) re[i] = N2 * ctx.Intensity[i]; }));
            var ovCb = O("physics", O("selfSteepening", false), "grid", grid);
            ovCb.Set("overrides", A(O("target", "element:nl", "property", "timeResponse", "mode", "add", "model", O("kind", "callback", "name", "myKerr"))));
            var cb = Go(FlatKerrSystem(L, 0.0), Spec(pulse, ovCb), reg);

            var ovEx = O("physics", O("selfSteepening", false), "grid", grid);
            ovEx.Set("overrides", A(O("target", "element:nl", "property", "timeResponse", "model", O("kind", "expression", "expr", "n2x*I", "params", O("n2x", N2)))));
            var ex = Go(FlatKerrSystem(L, 5e-20), Spec(pulse, ovEx)); // replace-mode: built-in Kerr (5e-20) switched off

            double D(PulseField a, PulseField b) => Enumerable.Range(0, a.Grid.N).Max(i => (a.Ax[i] - b.Ax[i]).Magnitude) / a.Ax.Max(c => c.Magnitude);
            Assert.Less(D(builtIn.Output, cb.Output), 1e-9, "callback == built-in Kerr");
            Assert.Less(D(builtIn.Output, ex.Output), 1e-9, "expression (replace mode) == built-in Kerr");
            Assert.True(ex.Compiled.Provenance.Entries.Any(p => p.Property.Contains("replaces Kerr") && p.Source == ModelSource.RunOverride), "provenance records the replacement");
        }

        [Test(Reference = "phase modulation: Bessel sidebands J_n(m)")]
        public void TimeResponse_LabTimeModulationCreatesBesselSidebands()
        {
            double L = 1e-3, beta = 0.5, k0 = W(Lam) / C, delta = beta / (k0 * L), fm = 3e12;
            var pulse = O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 3e-12, "energyJ", 1e-9);
            foreach (var mode in new[] { "envelope", "carrier" })
            {
                var extra = O("physics", O("fieldMode", mode, "selfSteepening", mode == "carrier"), "grid", O("windowS", 40e-12));
                extra.Set("overrides", A(O("target", "element:nl", "property", "timeResponse", "mode", "add",
                    "model", O("kind", "expression", "expr", "d*sin(2*pi*fm*(t_lab - t_arrival))", "params", O("d", delta, "fm", fm), "carrierResolved", mode == "carrier"))));
                var r = Go(FlatKerrSystem(L, 0.0), Spec(pulse, extra));
                var g = r.Output.Grid; var s = r.Output.SpectralIntensity();
                double band(double centre) { double acc = 0; for (int i = 0; i < g.N; i++) if (Math.Abs(g.OmegaAbsolute[i] - centre) < 0.4 * 2 * Math.PI * fm) acc += s[i]; return acc; }
                double ratio = band(g.OmegaCenter + 2 * Math.PI * fm) / band(g.OmegaCenter);
                double j0 = 0.93847, j1 = 0.24227;
                Assert.Close(j1 * j1 / (j0 * j0), ratio, mode == "envelope" ? 0.01 : 0.04, 0, mode + ": first sideband / carrier");
            }
        }

        [Test(Reference = "linear chirp τ = τ0 + GDD Δω", Tolerance = "1e-09, 0.1 %, 1 %, 2 %")]
        public void Stretcher_SampledGroupDelayStretchesAndReportsArrival()
        {
            double gdd = 5e-24, tod = 0, tau0 = 1e-9;
            var wl = Enumerable.Range(0, 25).Select(i => 1000e-9 + i * 2.5e-9).ToArray();
            double w0 = W(Lam);
            var gd = wl.Select(l => tau0 + gdd * (W(l) - w0) + tod * Math.Pow(W(l) - w0, 2) / 2).ToArray();
            var s = O("format", "ultrafast-system", "name", "stretcher", "sampling", O("wavelengthsM", wl));
            s.Set("elements", A(O("id", "str", "type", "dispersion", "groupDelayS", gd)));
            var sys = SystemDescription.FromJson(s);
            var r = Go(sys, Spec(Gauss(Lam, 200e-15, 1e-9)));
            double t0 = 200e-15, expected = t0 * Math.Sqrt(1 + Math.Pow(4 * Math.Log(2) * gdd / (t0 * t0), 2));
            var outS = PulseMetrics.Summarize(r.Output);
            Assert.Close(expected, outS.FwhmS, 0.01, 0, "stretched FWHM");
            Assert.Close(tau0 + gdd * (r.Compiled.Grid.OmegaCenter - w0), r.FinalArrivalS, 1e-9, 0, "arrival time (at the snapped carrier)");
            Assert.Close(gdd, r.FinalSystem.GddS2, 1e-3, 0, "system GDD");
            Assert.Close(gdd, outS.PulseGddS2, 0.02, 0, "pulse GDD fit");
            Assert.Less(outS.EdgeEnergyFraction, 1e-6, "auto-sized window holds the pulse");
        }

        [Test(Reference = "GDD and reflectance add per bounce", Tolerance = "1e-09, 0.1 %")]
        public void ChirpedMirrorCoating_GddAccumulatesPerBounce()
        {
            var els = Enumerable.Range(0, 4).Select(i => O("id", "m" + i, "type", "mirror", "coating", O("kind", "gdd", "R", 0.995, "gddS2", -100e-30))).ToArray();
            var r = Go(Sys(els), Spec(Gauss(800e-9, 30e-15, 1e-9)));
            Assert.Close(-400e-30, r.FinalSystem.GddS2, 1e-3, 0, "4 bounces x -100 fs^2");
            Assert.Close(Math.Pow(0.995, 4), r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ(), 1e-9, 0, "reflectance^4");
        }

        [Test(Reference = "Beer-Lambert law", Tolerance = "1e-09")]
        public void MaterialAttenuation_BeerLambert()
        {
            var s = O("format", "ultrafast-system", "materials", O("lossy", O("index", 1.5, "attenuation", 10.0)));
            s.Set("elements", A(O("id", "b", "type", "bulk", "material", "lossy", "lengthM", 0.05)));
            var r = Go(SystemDescription.FromJson(s), Spec(Gauss(Lam, 100e-15, 1e-9)));
            Assert.Close(Math.Exp(-0.5), r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ(), 1e-9, 0, "exp(-alpha L)");
            Assert.Close(r.Input.TotalEnergyJ() * (1 - Math.Exp(-0.5)), r.Records[0].LossJ["absorption"], 1e-9, 0, "attributed to absorption");
        }
    }
}
