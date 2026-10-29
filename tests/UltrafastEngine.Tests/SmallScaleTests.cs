using System;
using System.Linq;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Small-scale self-focusing in a spatial (cartesian) plate: seeded ripple / noise on a flat-top beam and the
    /// Bespalov-Talanov growth rate as the analytic reference.</summary>
    public class SmallScaleTests
    {
        private const double Lam = 1030e-9, L = 3e-3, Half = 3.5e-3, Tau = 100e-15, N0 = 1.45;

        private static RunResult Plate(double n2, JsonObject profile, int nx = 1024, int ny = 1, double energyJ = 1e-6)
        {
            var mats = O("kerr", O("index", O("kind", "constant", "value", N0), "n2", n2));
            var el = O("id", "k", "type", "bulk", "material", "kerr", "lengthM", L, "beam", O("kind", "gaussian", "w0M", 1e-3),
                "transverse", O("model", "cartesian", "nx", nx, "ny", ny, "halfWidthM", Half, "initialProfile", profile));
            var sys = SystemDescription.FromJson(O("format", "ultrafast-system", "version", 2, "name", "bt", "materials", mats, "elements", new JsonArray(new JsonValue[] { el })));
            var run = O("format", "ultrafast-run", "version", 2, "pulse", O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", Tau, "energyJ", energyJ),
                "physics", O("raman", false, "selfSteepening", false));
            return Simulation.Run(sys, Specs.RunSpec.FromJson(run));
        }

        private static double[] Arr(RunResult r, string name) => r.Records[0].Arrays.Single(a => a.Name == "el_k_" + name).Data;

        /// <summary>Amplitude of the cos(Kx) component of F(x) relative to its mean over the beam core |x| &lt; 0.4 mm (least squares).</summary>
        private static double Modulation(double[] x, double[] f, double period)
        {
            double K = 2 * Math.PI / period; int n = 0; double s1 = 0, sc = 0, ss = 0, scc = 0, sss = 0, scs = 0, sf = 0, sfc = 0, sfs = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (Math.Abs(x[i]) > 0.4e-3) continue;
                double c = Math.Cos(K * x[i]), s = Math.Sin(K * x[i]);
                n++; s1 += 1; sc += c; ss += s; scc += c * c; sss += s * s; scs += c * s; sf += f[i]; sfc += f[i] * c; sfs += f[i] * s;
            }
            // normal equations of f = a + b cos + c sin
            double[,] m = { { s1, sc, ss }, { sc, scc, scs }, { ss, scs, sss } }; double[] rhs = { sf, sfc, sfs };
            var sol = Solve3(m, rhs);
            return Math.Sqrt(sol[1] * sol[1] + sol[2] * sol[2]) / sol[0];
        }

        private static double[] Solve3(double[,] a, double[] b)
        {
            int n = 3; var m = (double[,])a.Clone(); var r = (double[])b.Clone();
            for (int i = 0; i < n; i++)
            {
                int p = i; for (int k = i + 1; k < n; k++) if (Math.Abs(m[k, i]) > Math.Abs(m[p, i])) p = k;
                for (int j = 0; j < n; j++) { double t = m[i, j]; m[i, j] = m[p, j]; m[p, j] = t; }
                { double t = r[i]; r[i] = r[p]; r[p] = t; }
                for (int k = i + 1; k < n; k++) { double f = m[k, i] / m[i, i]; for (int j = i; j < n; j++) m[k, j] -= f * m[i, j]; r[k] -= f * r[i]; }
            }
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--) { double s = r[i]; for (int j = i + 1; j < n; j++) s -= m[i, j] * x[j]; x[i] = s / m[i, i]; }
            return x;
        }

        /// <summary>Bespalov-Talanov: a plane wave of intensity I with an amplitude ripple eps cos(Kx) is linearised as
        /// u' = q v, v' = (2 Delta - q) u, q = K^2 / 2k, Delta = k0 n2 I: u(z) = eps cosh(g z), g^2 = q (2 Delta - q) (cos for q &gt; 2 Delta).
        /// The fluence (time-integral over the Gaussian pulse, Delta ~ f(t)) then carries the ripple 2 eps <cosh(g L)>_f.</summary>
        private static double PredictedModulation(double eps, double period, double delta0)
        {
            double k = N0 * 2 * Math.PI / Lam, K = 2 * Math.PI / period, q = K * K / (2 * k);
            double num = 0, den = 0;
            for (int i = -2000; i <= 2000; i++)
            {
                double t = i * 0.002 * Tau, f = Math.Exp(-4 * Math.Log(2) * Math.Pow(t / Tau, 2));
                double g2 = q * (2 * delta0 * f - q), gain = g2 >= 0 ? Math.Cosh(Math.Sqrt(g2) * L) : Math.Cos(Math.Sqrt(-g2) * L);
                num += f * gain; den += f;
            }
            return 2 * eps * num / den;
        }

        private static (double F0, double N2For) Calibrate(JsonObject profile, double delta0)
        {
            var lin = Plate(0, profile);
            var x = Arr(lin, "x_m"); var f = Arr(lin, "fluence_in_x");
            double F0 = Enumerable.Range(0, x.Length).Where(i => Math.Abs(x[i]) < 0.4e-3).Average(i => f[i]);
            double i0 = F0 / (Tau * Math.Sqrt(Math.PI / (4 * Math.Log(2))));      // peak intensity of the flat core
            return (F0, delta0 / (2 * Math.PI / Lam * i0));
        }

        [Test]
        public void SmallScale_RippleGrowth_MatchesBespalovTalanov()
        {
            double k = N0 * 2 * Math.PI / Lam, delta0 = 500.0, eps = 0.01;
            // the most unstable ripple (q = Delta) and a stable one (q = 3 Delta)
            foreach (double qOverDelta in new[] { 1.0, 3.0 })
            {
                double K = Math.Sqrt(2 * k * qOverDelta * delta0);
                int periods = (int)Math.Round(2 * Half * K / (2 * Math.PI)); double period = 2 * Half / periods;
                var prof = O("kind", "flatTop", "ripple", O("periodM", period, "relativeAmplitude", eps));
                var (_, n2) = Calibrate(prof, delta0);
                var r = Plate(n2, prof);
                double measured = Modulation(Arr(r, "x_m"), Arr(r, "fluence_out_x"), period);
                double expected = PredictedModulation(eps, period, delta0);
                Assert.Close(Math.Abs(expected), measured, 0.06, 0, "fluence ripple after the plate, q / Delta = " + qOverDelta);
                // the measured gain is the diagnostic the engine reports
                Assert.Greater(r.Records[0].Values["smallScaleGain"], 0, "reported");
            }
        }

        [Test]
        public void SmallScale_WithoutKerr_TheRippleDiffractsAsTheTalbotEffect()
        {
            // Delta = 0: u = eps cos(q L): the same formula, no nonlinearity
            double k = N0 * 2 * Math.PI / Lam, eps = 0.01, K = Math.Sqrt(2 * k * 500.0);
            int periods = (int)Math.Round(2 * Half * K / (2 * Math.PI)); double period = 2 * Half / periods;
            var r = Plate(0, O("kind", "flatTop", "ripple", O("periodM", period, "relativeAmplitude", eps)));
            double q = Math.Pow(2 * Math.PI / period, 2) / (2 * k);
            Assert.Close(2 * eps * Math.Abs(Math.Cos(q * L)), Modulation(Arr(r, "x_m"), Arr(r, "fluence_out_x"), period), 0.08, 2e-4, "diffraction only: 2 eps |cos(q L)|");
        }

        [Test]
        public void SmallScale_SeededNoise_IsReproducible_AndScalesWithItsAmplitude()
        {
            double Contrast(double a, int seed)
            {
                var r = Plate(0, O("kind", "flatTop", "noise", O("relativeAmplitudeRms", a, "correlationLengthM", 100e-6, "seed", seed)), nx: 128, ny: 128, energyJ: 1e-6);
                return r.Records[0].Values["fluenceContrastIn"];
            }
            double c1 = Contrast(0.01, 5), c2 = Contrast(0.02, 5);
            Assert.Close(c1, Contrast(0.01, 5), 1e-12, 0, "same seed, same field");
            Assert.True(Math.Abs(c1 - Contrast(0.01, 6)) > 1e-5, "another seed, another field");
            Assert.Close(2.0, c2 / c1, 0.02, 0, "twice the amplitude, twice the contrast");
            Assert.Close(2 * 0.01, c1, 0.5, 0, "fluence contrast ~ 2 x the relative amplitude rms (few correlation cells in the core: loose)");
        }

        [Test]
        public void SmallScale_ReportsPeakPowerOverCritical()
        {
            // P_cr = 3.77 lambda^2 / (8 pi n0 n2)
            var r = Plate(3e-20, O("kind", "flatTop"));
            var p = PulseMetrics.TemporalPower(r.Input).Max();
            Assert.Close(p / (3.77 * Lam * Lam / (8 * Math.PI * N0 * 3e-20)), r.Records[0].Values["peakPowerOverCritical"], 0.02, 0, "P / P_cr");
        }
    }
}
