using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>The fiber element, the silica Raman models, quantum noise and the coherence study, each against an
    /// analytic result (pyNLO cross-checks are in python/tests/test_pynlo.py).</summary>
    [TestArea("Fibres: solitons, dispersion, loss and gain, Raman, noise and coherence")]
    public class FiberTests
    {
        private const double Lam = 1550e-9;

        private static SystemDescription Fib(params JsonObject[] els)
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "f");
            o.Set("elements", new JsonArray(els));
            return SystemDescription.FromJson(o);
        }

        private static PulseSummary Out(RunResult r) => PulseMetrics.Summarize(r.Output);

        private static JsonObject Sech(double fwhm, double energy) => O("shape", "sech2", "centerWavelengthM", Lam, "durationFwhmS", fwhm, "energyJ", energy);

        private static RunResult Run(SystemDescription s, JsonObject pulse, JsonObject extra = null)
        {
            var e = extra ?? new JsonObject();
            if (!e.Has("stepper")) e.Set("stepper", O("tolerance", 1e-8));
            return Go(s, Spec(pulse, e));
        }

        [Test(Reference = "fundamental soliton sech shape (NLSE)", Tolerance = "1 %")]
        public void FundamentalSoliton_PropagatesUnchanged_AndMetricsAreRight()
        {
            double b2 = -20e-27, gamma = 2e-3, fw = 200e-15, t0 = fw / 1.763;
            double p0 = Math.Abs(b2) / (gamma * t0 * t0), energy = 2 * p0 * t0;   // N = 1; sech^2 energy = 2 P0 T0
            double ld = t0 * t0 / Math.Abs(b2);
            var s = Fib(O("id", "smf", "type", "fiber", "lengthM", 5 * ld, "betas", A(b2), "gamma", gamma, "raman", "none"));
            var r = Run(s, Sech(fw, energy), O("physics", O("selfSteepening", false)));
            var rec = r.Records[0];
            Assert.Close(1.0, rec.Values["solitonNumber"], 0.01, 0, "N = 1 (from the sampled input pulse)");
            Assert.Close(ld, rec.Values["dispersionLengthM"], 0.01, 0, "L_D = T0^2/|beta2| (T0 from the sampled FWHM)");
            Assert.Close(fw, Out(r).FwhmS, 0.01, 0, "duration kept over 5 L_D");
            Assert.Close(p0, Out(r).PeakPowerW, 0.01, 0, "peak power kept");
        }

        [Test(Reference = "D(λ) = −(2πc/λ²) β2", Tolerance = "1 %")]
        public void DispersionCurve_EqualsTaylorCoefficients()
        {
            double b2 = -20e-27, b3 = 0.1e-39, w0 = W(Lam);
            // D(lambda) from beta2(w) = b2 + b3 (w - w0)
            var wl = Enumerable.Range(0, 41).Select(i => 1350e-9 + i * 10e-9).ToArray();
            var D = wl.Select(l => { double w = W(l), bb = b2 + b3 * (w - w0); return -2 * Math.PI * C * bb / (l * l) / 1e-6; }).ToArray();
            var a = Run(Fib(O("id", "f", "type", "fiber", "lengthM", 0.5, "betas", A(b2, b3), "gamma", 2e-3)), Sech(100e-15, 50e-12));
            var b = Run(Fib(O("id", "f", "type", "fiber", "lengthM", 0.5, "dispersion", O("wavelengthsM", new JsonArray(wl.Select(x => (JsonValue)x).ToArray()), "dPsNmKm", new JsonArray(D.Select(x => (JsonValue)x).ToArray())), "gamma", 2e-3)), Sech(100e-15, 50e-12));
            Assert.Close(Out(a).FwhmS, Out(b).FwhmS, 0.01, 0, "same duration");
            Assert.Close(Out(a).SpectralRmsWidthM, Out(b).SpectralRmsWidthM, 0.01, 0, "same spectral width");
            Assert.Close(b2, b.Records[0].Values["beta2S2PerM"], 0.01, 0, "beta2 from the curve");
        }

        [Test(Reference = "exponential loss and gain in dB", Tolerance = "0.01 %, 0.1 %")]
        public void LossGainAndGainGoal()
        {
            var lin = O("nonlinear", false);
            JsonObject F(params object[] kv) { var o = O("id", "f", "type", "fiber", "betasPsKm", A(-20.0), "gamma", 1e-3); for (int i = 0; i < kv.Length; i += 2) o.Set((string)kv[i], kv[i + 1] is JsonValue jv ? jv : (JsonValue)Convert.ToDouble(kv[i + 1])); foreach (var x in lin) o.Set(x.Key, x.Value); return o; }
            double e0 = 1e-9;
            var loss = Run(Fib(F("lengthM", 1000.0, "lossDbPerKm", 3.0)), Sech(1e-12, e0));
            Assert.Close(Math.Pow(10, -0.3) * e0, Out(loss).EnergyJ, 1e-4, 0, "3 dB/km over 1 km");
            var gain = Run(Fib(F("lengthM", 2.0, "gainDbPerM", 5.0)), Sech(1e-12, e0));
            Assert.Close(10 * e0, Out(gain).EnergyJ, 1e-4, 0, "5 dB/m over 2 m");
            var gs = O("wavelengthsM", A(1500e-9, 1550e-9, 1600e-9), "dbPerM", A(0.5, 1.0, 0.5), "outputEnergyJ", 20e-9);
            var goal = Run(Fib(F("lengthM", 3.0, "gain", gs)), Sech(1e-12, e0));
            Assert.Close(20e-9, Out(goal).EnergyJ, 1e-3, 0, "gain spectrum scaled to the requested output");
        }

        [Test(Reference = "B = ∫ γ(z) P dz", Tolerance = "1 %")]
        public void TaperedGamma_BIntegralFollowsTheIntegral()
        {
            double g0 = 1e-3, L = 2.0, e = 1e-9, fw = 10e-12;
            var s = Fib(O("id", "f", "type", "fiber", "lengthM", L, "betas", A(0.0), "gamma", "0.001*(1 + z)", "raman", "none"));
            var r = Run(s, O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", fw, "energyJ", e), O("physics", O("selfSteepening", false)));
            double p0 = PulseMetrics.Summarize(r.Input).PeakPowerW;
            Assert.Close(g0 * p0 * (L + L * L / 2), r.Records[0].BIntegralRad, 0.01, 0, "B = P0 int gamma(z) dz");
        }

        [Test(Reference = "Hollenbeck & Cantrell, JOSA B 19, 2886 (2002): gain peak 13.2 THz", Tolerance = "1 %, 3 %")]
        public void RamanModels_NormalisedAndHollenbeckPeaksAt13THz()
        {
            int n = 1 << 16; double dt = 0.5e-15;
            var t = Enumerable.Range(0, n).Select(i => (i - n / 2) * dt).ToArray();
            foreach (var m in new IDelayedResponse[] { new LinAgrawalRamanResponse(), new HollenbeckCantrellRamanResponse(), new DampedOscillatorResponse(12.2e-15, 32e-15) })
            {
                var h = m.Evaluate(t);
                Assert.Close(1.0, h.Sum() * dt, 0.01, 0, m.Description + ": unit integral");
                Assert.True(h.Take(n / 2).All(v => v == 0), m.Description + ": causal");
            }
            // Raman gain ~ Im H(f): Hollenbeck-Cantrell peaks at 13.2 THz (440 cm^-1)
            var hh = new HollenbeckCantrellRamanResponse().Evaluate(t);
            double bestF = 0, best = 0;
            for (double f = 5e12; f < 25e12; f += 0.05e12)
            {
                double im = 0; for (int i = n / 2; i < n; i++) im += hh[i] * Math.Sin(2 * Math.PI * f * t[i]) * dt;
                if (im > best) { best = im; bestF = f; }
            }
            Assert.Close(13.2e12, bestF, 0.03, 0, "Hollenbeck-Cantrell Raman gain peak");
            Assert.Close(0.245, MaterialLibrary.DefaultRamanFraction("linAgrawal"), 0, 0, "Lin-Agrawal fR");
        }

        [Test(Reference = "dispersive-wave phase matching (Akhmediev & Karlsson 1995)", Tolerance = "0.3 %, 1 %")]
        public void DispersiveWave_ResonanceMatchesPhaseMatching()
        {
            double b2 = -10e-27, b3 = 0.08e-39, gamma = 0.1, fw = 50e-15, t0 = fw / 1.763;
            double p0 = 9 * Math.Abs(b2) / (gamma * t0 * t0);   // N = 3
            var s = Fib(O("id", "pcf", "type", "fiber", "lengthM", 1e-3, "betas", A(b2, b3), "gamma", gamma));
            var r = Run(s, Sech(fw, 2 * p0 * t0));
            var v = r.Records[0].Values;
            Assert.Close(3.0, v["solitonNumber"], 0.01, 0, "N = 3");
            p0 = PulseMetrics.Summarize(r.Input).PeakPowerW;   // the sampled peak power the element used
            // b2 dw^2/2 + b3 dw^3/6 = gamma P0 / 2
            double lo = 0, hi = 10 * Math.Abs(3 * b2 / b3);
            Func<double, double> f = dw => b2 * dw * dw / 2 + b3 * dw * dw * dw / 6 - gamma * p0 / 2;
            lo = -3 * b2 / b3 * 0.5; // beyond the zero of the dispersion part's minimum
            for (int it = 0; it < 200; it++) { double m = 0.5 * (lo + hi); if (Math.Sign(f(m)) == Math.Sign(f(lo))) lo = m; else hi = m; }
            double lamDw = 2 * Math.PI * C / (W(Lam) + 0.5 * (lo + hi));
            Assert.Close(lamDw, v["dispersiveWaveWavelengthM"], 3e-3, 0, "dispersive-wave wavelength (carrier snapped to the grid)");
            Assert.Less(v["dispersiveWaveWavelengthM"], Lam, "blue of the soliton for beta3 > 0");
        }

        [Test(Reference = "one photon per mode: N ħω of energy", Tolerance = "5 %")]
        public void QuantumNoise_OnePhotonPerMode_AndCoherentSoliton()
        {
            // one photon per mode adds N hbar w of energy in total
            var s = Fib(O("id", "f", "type", "fiber", "lengthM", 1e-6, "betas", A(-20e-27), "gamma", 1e-3, "nonlinear", false));
            var pulse = Sech(100e-15, 1e-12); pulse.Set("noise", O("kind", "onePhotonPerMode", "seed", 3));
            var r = Run(s, pulse);
            var g = r.Input.Grid;
            double expected = g.OmegaAbsolute.Where(w => w > 0).Sum(w => 1.054571817e-34 * w);
            Assert.Close(1e-12 + expected, r.Input.TotalEnergyJ(), 0.05, 1e-15, "energy of the noise ~ sum of hbar w (random phases: +- a few %)");
            // a low-N soliton stays coherent: |g12| ~ 1 over the spectrum
            var sc = Fib(O("id", "f", "type", "fiber", "lengthM", 0.05, "betas", A(-20e-27), "gamma", 2e-3));
            var c1 = Studies.Run(sc, Spec(Sech(300e-15, 100e-12), O("stepper", O("tolerance", 1e-5))), StudySpec.FromJson(O("mode", "coherence", "coherence", O("trials", 4), "parallel", 4)));
            var co = c1.Analysis.GetObject("coherence");
            Assert.Greater(co.GetDouble("meanCoherence", 0), 0.99, "low-N soliton: coherent");
            Assert.True(co.GetDoubleArray("g12").Length == co.GetDoubleArray("wavelength_m").Length, "g12(lambda) exported");
        }

        [Test(Slow = true, Reference = "Dudley, Genty & Coen, Rev. Mod. Phys. 78, 1135 (2006): coherence of MI supercontinuum")]
        public void Coherence_LostInLongPulseSupercontinuum()
        {
            // a 1 ps pulse at N ~ 30 broadens through modulation instability seeded by the quantum noise: shot-to-shot
            // fluctuations destroy the spectral coherence (Dudley et al., Rev. Mod. Phys. 78, 1135 (2006))
            var sc = Fib(O("id", "f", "type", "fiber", "lengthM", 0.4, "betas", A(-20e-27, 0.1e-39), "gamma", 20e-3));
            var c = Studies.Run(sc, Spec(Sech(1e-12, 3e-9), O("grid", O("n", 8192), "stepper", O("tolerance", 1e-5))),
                StudySpec.FromJson(O("mode", "coherence", "coherence", O("trials", 6), "parallel", 6)));
            double g = c.Analysis.GetObject("coherence").GetDouble("meanCoherence", 0);
            Assert.Less(g, 0.8, "coherence lost");
        }
    }
}
