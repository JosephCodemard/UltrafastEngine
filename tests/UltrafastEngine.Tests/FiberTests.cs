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

        /// <summary>Soliton self-frequency shift of an N = 1, T0 = 300 fs soliton (D = 15 ps/(nm km), gamma = 1.3 /(W km),
        /// Blow-Wood Raman, no self-steepening) on the auto-sized grid, whose time step (23.5 fs) is two Raman periods:
        /// the rate must be Gordon's perturbation integral with the model's response,
        /// dOmega/dz = -(gamma fR / 2 P0 T0) int |P~(W)|^2 W Im H(W) dW / 2 pi, |P~|^2 = (pi P0 T0^2 W / sinh(pi W T0 / 2))^2
        /// (~ -8 |beta2| T_R / 15 T0^4). With the kernel sampled directly on that grid it was 43 % too fast;
        /// sweeps T0 and the three silica models.</summary>
        [Test(Reference = "Gordon, Opt. Lett. 11, 662 (1986): SSFS rate (perturbation integral over the Raman gain)", Tolerance = "2 %")]
        public void SolitonSelfFrequencyShift_OnACoarseGrid_MatchesGordon()
        {
            double lam = 1550e-9, b2 = -15e-6 * lam * lam / (2 * Math.PI * C), gamma = 1.3e-3, t0 = 300e-15, len = 45;
            double p0 = Math.Abs(b2) / (gamma * t0 * t0);
            var r = Go(Fib(O("id", "f", "type", "fiber", "lengthM", len, "betas", A(b2), "gamma", gamma, "raman", "silica")),
                Spec(O("shape", "sech2", "centerWavelengthM", lam, "durationFwhmS", 2 * Math.Log(1 + Math.Sqrt(2)) * t0, "peakPowerW", p0),
                     O("physics", O("selfSteepening", false))));
            Assert.Greater(r.Output.Grid.DTime, 15e-15, "the auto grid is coarse against the 12 fs Raman oscillation");
            double rate = (SpectralCentroid(r.Output) - SpectralCentroid(r.Input)) / len;
            // Gordon's integral with the Blow-Wood response (unit integral), fR = 0.18
            var h = new DampedOscillatorResponse(12.2e-15, 32e-15);
            int nt = 40000; double dth = 0.1e-15;
            var th = Enumerable.Range(0, nt).Select(i => i * dth).ToArray();
            var hv = h.Evaluate(th); double norm = hv.Sum() * dth;
            double fR = 0.18, sum = 0; int nw = 800; double wMax = 40 / t0, dw = wMax / nw;
            for (int k = 1; k <= nw; k++)
            {
                double w = k * dw, imH = 0;
                for (int i = 0; i < nt; i++) imH += hv[i] * Math.Sin(w * th[i]);
                imH *= dth / norm;
                double pw = Math.PI * p0 * t0 * t0 * w / Math.Sinh(Math.PI * w * t0 / 2);
                sum += pw * pw * w * imH * dw;
            }
            double gordon = -gamma * fR / (2 * p0 * t0) * 2 * sum / (2 * Math.PI);
            Assert.Close(gordon, rate, 0.02, 0, "SSFS rate on the auto grid");
        }

        /// <summary>The kernel every path builds from a delayed response (EnvelopeProblem.ResponseKernel; since WP fix-2 also the
        /// carrier-resolved path, which sampled h(t) directly) has the response's own spectrum on a coarse grid. Damped
        /// oscillator h = A exp(-t/tau2) sin(t/tau1), A = (tau1^2 + tau2^2)/(tau1 tau2^2) (Blow and Wood: 12.2 / 32 fs):
        /// H(w) = A (1/tau1) / ((1/tau2 - i w)^2 + 1/tau1^2), H(0) = 1. At dt = 10 fs (a field grid for an 8 um carrier) the
        /// directly sampled kernel sums to 0.935 and, normalised, its spectrum is 77 % high at 40 THz (aliasing); the band-limited
        /// kernel matches H within 0.5 % up to 0.8 of the Nyquist frequency, in magnitude and as a complex number (normalised to
        /// its value at w = 0). The engine's convention is E ~ exp(-i w t) (docs/concepts.md), so a spectrum is int f(t) exp(+i w t) dt
        /// and a causal response has Im H > 0 at positive offsets near the Raman peak: a time-reversed (anti-causal) or
        /// sign-flipped kernel gives conj(H) or -H and fails the complex check.</summary>
        [Test(Reference = "Blow & Wood, IEEE JQE 25, 2665 (1989): damped-oscillator Raman response, analytic H(w)", Tolerance = "0.5 %")]
        public void RamanKernel_OnACoarseGrid_HasTheResponseSpectrum()
        {
            double t1 = 12.2e-15, t2 = 32e-15, dt = 10e-15;
            var grid = UltrafastEngine.Pulses.FrequencyGrid.Create(1024, dt, W(1030e-9));
            var resp = new DampedOscillatorResponse(t1, t2);
            var h = UltrafastEngine.Physics.EnvelopeProblem.ResponseKernel(resp, grid);
            Assert.Close(1.0, h.Sum() * dt, 1e-9, 0, "unit sum on the grid");
            var hf = UltrafastEngine.Numerics.SpectralTransform.TimeToFreq(h.Select(x => new System.Numerics.Complex(x, 0)).ToArray());
            int n = grid.N, i0 = Array.IndexOf(grid.OmegaOffset, grid.OmegaOffset.OrderBy(Math.Abs).First());
            double a = (t1 * t1 + t2 * t2) / (t1 * t2 * t2), nyq = Math.PI / dt;
            for (int i = 0; i < n; i++)
            {
                double w = grid.OmegaOffset[i];
                if (Math.Abs(w) > 0.8 * nyq || i % 8 != 0) continue;
                var den = new System.Numerics.Complex(1 / t2, -w) * new System.Numerics.Complex(1 / t2, -w) + 1 / (t1 * t1);
                var hAnalytic = a / t1 / den; var hNum = hf[i] / hf[i0];
                string at = (w / (2 * Math.PI) * 1e-12).ToString("F1") + " THz";
                Assert.Close(hAnalytic.Magnitude, hNum.Magnitude, 0.005, 0, "|H| at " + at);
                Assert.True((hNum - hAnalytic).Magnitude <= 0.005 * hAnalytic.Magnitude, "complex H (causal phase) at " + at + ": " + hNum + " against " + hAnalytic);
            }
            // the Raman gain side: near the 13 THz peak Im H > 0 on the positive side, < 0 on the negative side (causal response)
            int ip = Array.IndexOf(grid.OmegaOffset, grid.OmegaOffset.OrderBy(x => Math.Abs(x - 1 / t1)).First());
            int im = Array.IndexOf(grid.OmegaOffset, grid.OmegaOffset.OrderBy(x => Math.Abs(x + 1 / t1)).First());
            Assert.Greater((hf[ip] / hf[i0]).Imaginary, 0.5, "Im H at +13 THz (Raman peak, causal)");
            Assert.Less((hf[im] / hf[i0]).Imaginary, -0.5, "Im H at -13 THz");
            // the same kernel reversed in time (anti-causal) has conj(H): the complex check rejects it
            var rev = new System.Numerics.Complex[n];
            for (int i = 1; i < n; i++) rev[i] = h[n - i];
            rev[0] = h[0];
            var rf = UltrafastEngine.Numerics.SpectralTransform.TimeToFreq(rev);
            Assert.Less((rf[ip] / rf[i0]).Imaginary, 0, "a time-reversed kernel has the opposite Im H (the check above would fail)");
        }

        /// <summary>The Raman kernel of the carrier-resolved solver, end to end: a 2 ps Gaussian at 20 um on a 512-point, 16 ps
        /// envelope grid (field grid dt = 7.8 fs, coarse against the 12 fs Raman oscillation) through 1 mm of a dispersionless medium
        /// (n = 1.4) with n2 and the silica Raman response (fR = 0.18), constant area. For a pulse much longer than the response the
        /// delayed part acts as instantaneous, so the nonlinear phase at the peak is the full k0 n2 I0 L ((h * I)(t_peak) = I0 to
        /// O((T_R / T)^2)). Measured as arg(A(t_peak)) against a 1e6 times weaker run, in the carrier-resolved and the envelope
        /// solver (observed within 2e-4). Checked to fail: with fR dropped from the field path 0.82 of it, with the directly
        /// sampled, unnormalised kernel (sum 0.961) 0.7 % low. The envelope grid must not reach zero frequency: with 4096 points
        /// over the same 16 ps the carrier-resolved SPM phase came out 33 % high (docs/design/limitations.md).</summary>
        [Test(Reference = "SPM phase k0 n2 I0 L with a delayed share fR of unit-integral response (long-pulse limit)", Tolerance = "0.1 %")]
        public void RamanOn_LongPulse_CarrierResolvedAndEnvelopeGiveTheFullKerrPhase()
        {
            double lam = 20e-6, T = 2e-12, L = 1e-3, n2 = 2.6e-20, ae = 1e-8, phiTarget = 0.1;
            double k0 = W(lam) / C, i0 = phiTarget / (k0 * n2 * L), p0 = i0 * ae, e = p0 * T * Math.Sqrt(Math.PI / (4 * Math.Log(2)));
            var mat = FlatMaterial(n2, 1.4); mat.Set("raman", O("kind", "silica"));
            var so = O("format", "ultrafast-system", "name", "raman-field", "materials", O("flat", mat));
            so.Set("elements", A(O("id", "x", "type", "bulk", "material", "flat", "lengthM", L, "beam", O("kind", "constant", "areaM2", ae))));
            var sys = SystemDescription.FromJson(so);
            foreach (var mode in new[] { "carrier", "envelope" })
            {
                RunResult Run(double energy) => Go(sys, Spec(Gauss(lam, T, energy), O("physics", O("fieldMode", mode, "raman", true, "selfSteepening", false), "grid", O("n", 512, "windowS", 16e-12))));
                var hi = Run(e); var lo = Run(e * 1e-6);
                var ah = hi.Output.TemporalAx();
                var al = lo.Output.TemporalAx();
                int pk = 0; for (int i = 1; i < al.Length; i++) if (al[i].Magnitude > al[pk].Magnitude) pk = i;
                double phi = (ah[pk] / al[pk]).Phase;
                Assert.Close(k0 * n2 * i0 * L, phi, 0.001, 0, mode + ": nonlinear phase at the peak (Kerr + Raman)");
            }
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
