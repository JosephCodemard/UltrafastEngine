// FROG geometries beyond SHG: polarisation gate, self-diffraction (E^2 E*), XFROG with a known reference, kind inference for retrieval.
// Analytic references: the first moment of a chirped Gaussian's FROG trace (Kane and Trebino 1993, App. C), and retrieval of a known chirped pulse.
using System;
using System.Linq;
using UltrafastEngine.Results;
using UltrafastEngine.Retrieval;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("FROG geometries: PG, SD, XFROG, kind inference")]
    public class FrogKindsTests
    {
        private const double Lam = 800e-9, Fwhm = 25e-15;

        private static RunResult Measure(JsonObject instrument, double gddFs2, double lam = Lam, double fwhm = Fwhm)
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "r");
            o.Set("elements", A(O("id", "x", "type", "probe")));
            var run = O("format", "ultrafast-run", "version", 2);
            run.Set("pulse", O("shape", "gaussian", "centerWavelengthM", lam, "durationFwhmS", fwhm, "energyJ", 1e-9, "chirp", O("gddFs2", gddFs2)));
            run.Set("instruments", A(instrument));
            return Simulation.Run(SystemDescription.FromJson(o), Specs.RunSpec.FromJson(run));
        }

        private static double Gdd(RetrievalResult r)
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
            double det = D(s0, s1, s2, s1, s2, s3, s2, s3, s4);
            return 2 * D(s0, s1, t0, s1, s2, t1, s2, s3, t2) / det;
        }

        /// <summary>Chirp rate b = d(omega)/dt and intensity 1/e half-width T of a Gaussian of transform-limited FWHM fwhm with GDD phi2:
        /// |E|^2 = exp(-t^2/T^2), omega(t) = omega0 + b t, with T0 = fwhm / (2 sqrt(ln 2)): T^2 = T0^2 (1 + (phi2/T0^2)^2), b = phi2 / (T0^4 + phi2^2).</summary>
        private static (double B, double T) Chirp(double fwhm, double phi2)
        {
            double t0 = fwhm / (2 * Math.Sqrt(Math.Log(2)));
            return (phi2 / (Math.Pow(t0, 4) + phi2 * phi2), t0 * Math.Sqrt(1 + Math.Pow(phi2 / (t0 * t0), 2)));
        }

        /// <summary>Slope d(centroid omega)/d(delay) of a trace: the intensity-weighted centre frequency of each delay row, fitted over the rows
        /// that carry at least a quarter of the peak row's energy.</summary>
        private static double CentroidSlope(RunResult r, string name)
        {
            double[] A(string s) => r.Instruments.Single(i => i.Name == name).Arrays.Single(a => a.Name == name + "_" + s).Data;
            var d = A("delay_s"); var w = A("omega_rad_s"); var tr = A("trace");
            int nr = d.Length, nc = w.Length;
            var rowE = new double[nr]; var cen = new double[nr];
            for (int i = 0; i < nr; i++) { double e = 0, c = 0; for (int k = 0; k < nc; k++) { e += tr[i * nc + k]; c += tr[i * nc + k] * w[k]; } rowE[i] = e; cen[i] = e > 0 ? c / e : 0; }
            double emax = rowE.Max(), sx = 0, sy = 0, sxx = 0, sxy = 0, n = 0;
            for (int i = 0; i < nr; i++) if (rowE[i] > 0.25 * emax) { sx += d[i]; sy += cen[i]; sxx += d[i] * d[i]; sxy += d[i] * cen[i]; n++; }
            return (n * sxy - sx * sy) / (n * sxx - sx * sx);
        }

        /// <summary>For a linearly chirped Gaussian the spectral first moment of the signal at delay tau is omega0 + b (intensity-weighted mean
        /// of t) + (terms from the gate's phase). PG: E(t)|E(t-tau)|^2 has mean t = 2 tau/3, slope 2b/3. SD: E^2(t) E*(t-tau) has mean t = tau/3
        /// and the gate adds b tau, slope 4b/3 (Kane and Trebino, Opt. Lett. 18, 823 (1993), App. C). Signs follow the engine's conventions
        /// (E = Re{A exp(-i w0 t)}, +GDD: red first).</summary>
        [Test(Reference = "Kane and Trebino 1993, App. C: PG slope 2b/3, SD slope 4b/3", Tolerance = "2 %")]
        public void Frog_PgAndSd_TraceCentroid_TiltsWithTheChirpRate()
        {
            double gdd = 300e-30;
            var (b, _) = Chirp(Fwhm, gdd);
            var rpg = Measure(O("kind", "frog", "type", "pg", "name", "f", "points", 256), 300);
            var rsd = Measure(O("kind", "frog", "type", "sd", "name", "f", "points", 256), 300);
            Assert.Close(2.0 / 3.0 * b, CentroidSlope(rpg, "f"), 0.02, 0, "PG slope");
            Assert.Close(4.0 / 3.0 * b, CentroidSlope(rsd, "f"), 0.02, 0, "SD slope");
            var rneg = Measure(O("kind", "frog", "type", "sd", "name", "f", "points", 256), -300);
            Assert.Close(-4.0 / 3.0 * b, CentroidSlope(rneg, "f"), 0.02, 0, "SD slope flips with the chirp sign");
        }

        /// <summary>XFROG with an unchirped Gaussian reference of intensity half-width T_R gates E(t) R(t-tau) at the sum frequency: the mean t
        /// of the signal is tau T^2 / (T^2 + T_R^2), so the centroid tilts with b T^2/(T^2 + T_R^2). The column axis is at omega + omega_ref.</summary>
        [Test(Reference = "first moment of a Gaussian product: slope b T^2 / (T^2 + T_R^2)", Tolerance = "2 %")]
        public void Xfrog_TraceCentroid_TiltsWithTheChirpRate_AtTheSumFrequency()
        {
            double gdd = 300e-30, refFwhm = 20e-15, lamR = 1200e-9;
            var (b, T) = Chirp(Fwhm, gdd);
            double tr = refFwhm / (2 * Math.Sqrt(Math.Log(2)));
            var r = Measure(O("kind", "xfrog", "name", "x", "points", 256, "reference", O("shape", "gaussian", "centerWavelengthM", lamR, "durationFwhmS", refFwhm)), 300);
            Assert.Close(b * T * T / (T * T + tr * tr), CentroidSlope(r, "x"), 0.02, 0, "XFROG slope");
            double[] A(string s) => r.Instruments.Single().Arrays.Single(a => a.Name == "x_" + s).Data;
            var w = A("omega_rad_s"); var tra = A("trace"); int nc = w.Length;
            double c = 0, e = 0; for (int i = 0; i < tra.Length; i++) { c += tra[i] * w[i % nc]; e += tra[i]; }
            double wSum = 2 * Math.PI * 299792458.0 * (1 / Lam + 1 / lamR);
            Assert.Close(wSum, c / e, 5e-4, 0, "signal at the sum frequency");
        }

        /// <summary>Retrieval of a known chirped pulse from SD and PG traces: no direction ambiguity, so the sign of the GDD must come out right.
        /// PCGPA is deterministic and accurate (duration within 1.4 % over 20 seeds on Linux and macOS). ePIE visits the delays in random order and
        /// stops at a trace error of about 1e-3 with a duration that scatters by about 2 % rms, with rare tails: over 20 seeds on macOS the largest
        /// miss was 7 % and on Linux CI seed 1 of the SD trace at GDD -300 fs^2 gave 17 % (trace error 1.4e-3, GDD right to 1 %). A 3 % duration limit
        /// on one seed was therefore never a property of ePIE, and the result changed with the FFT rounding of the platform. For ePIE the test checks
        /// what it does guarantee: the trace error and the GDD (with its sign) on every run, no gross failure on any run, and the median duration of
        /// three seeds.</summary>
        [Test(Reference = "known chirped pulse; PCGPA 3 % duration, 6 % GDD; ePIE median of 3 seeds 5 % duration, 8 % GDD each", Tolerance = "see summary")]
        public void Frog_PcgpaAndEpie_RecoverAChirpedPulse_FromSdAndPgTraces_WithTheSign()
        {
            foreach (var type in new[] { "sd", "pg" })
                foreach (double gdd in new[] { 300.0, -300.0 })
                {
                    var r = Measure(O("kind", "frog", "type", type, "name", "frog", "points", 128), gdd);
                    var trace = TraceData.FromInstrument(r, "frog");
                    Assert.Equal(type + "-frog", trace.Kind);
                    double trueFwhm = PulseMetrics.Summarize(r.Output).FwhmS;

                    string what = type + " pcgpa gdd " + gdd;
                    var pc = Retrievals.Run("pcgpa", trace, O("maxIterations", 400));
                    Assert.Less(pc.Error, 8e-3, what + ": FROG error");
                    Assert.Close(trueFwhm, pc.FwhmS, 0.03, 0, what + ": duration");
                    Assert.Close(gdd, Gdd(pc) * 1e30, 0.06, 0, what + ": GDD with its sign");

                    var durations = new double[3];
                    for (int k = 0; k < 3; k++)
                    {
                        what = type + " epie seed " + (k + 1) + " gdd " + gdd;
                        var ep = Retrievals.Run("epie", trace, O("maxIterations", 300, "seed", k + 1));
                        Assert.Less(ep.Error, 3e-3, what + ": FROG error");
                        Assert.Close(trueFwhm, ep.FwhmS, 0.25, 0, what + ": duration (no gross failure)");
                        Assert.Close(gdd, Gdd(ep) * 1e30, 0.08, 0, what + ": GDD with its sign");
                        durations[k] = ep.FwhmS;
                    }
                    Array.Sort(durations);
                    Assert.Close(trueFwhm, durations[1], 0.05, 0, type + " epie gdd " + gdd + ": median duration of 3 seeds");
                }
        }

        /// <summary>XFROG with the known reference fixes the time origin and the direction: GDD with sign, from either algorithm.</summary>
        [Test(Reference = "known chirped pulse, known reference", Tolerance = "3 % duration, 6 % GDD, sign exact")]
        public void Xfrog_PcgpaAndEpie_RecoverAChirpedPulse_WithTheSign()
        {
            var reference = O("shape", "gaussian", "centerWavelengthM", 1200e-9, "durationFwhmS", 20e-15);
            foreach (double gdd in new[] { 300.0, -300.0 })
            {
                var r = Measure(O("kind", "xfrog", "name", "x", "points", 128, "reference", reference), gdd);
                var trace = TraceData.FromInstrument(r, "x");
                Assert.Equal("xfrog", trace.Kind);
                double trueFwhm = PulseMetrics.Summarize(r.Output).FwhmS;
                foreach (var m in new[] { "pcgpa", "epie" })
                {
                    var res = Retrievals.Run(m, trace, O("maxIterations", 300, "stepSize", 1.0));   // known gate: a full object step
                    string what = "xfrog " + m + " gdd " + gdd;
                    Assert.Less(res.Error, 8e-3, what + ": FROG error");
                    Assert.Close(trueFwhm, res.FwhmS, 0.03, 0, what + ": duration");
                    Assert.Close(gdd, Gdd(res) * 1e30, 0.06, 0, what + ": GDD with its sign");
                }
            }
        }

        private static JsonObject TraceDict(RunResult r, string name, string kind)
        {
            double[] A(string s) => r.Instruments.Single(i => i.Name == name).Arrays.Single(a => a.Name == name + "_" + s).Data;
            var o = O("delay_s", A("delay_s"), "omega_rad_s", A("omega_rad_s"), "trace", A("trace"));
            if (kind != null) o.Set("kind", kind);
            return o;
        }

        private static bool Throws(Action a, string contains)
        {
            try { a(); return false; } catch (Exception ex) { return ex.Message.Contains(contains); }
        }

        /// <summary>A polarisation-gate trace given without a kind must not quietly come back as an SHG retrieval: the kind is inferred from the
        /// instrument output, read from the data when it names one, and otherwise an asymmetric trace is refused (an SHG trace is symmetric in
        /// the delay).</summary>
        [Test(Reference = "SHG-FROG traces are symmetric in the delay; PG traces of a chirped pulse are not", Tolerance = "exact")]
        public void Retrieve_WithoutAKind_DoesNotAssumeShg_ForAPolarisationGateTrace()
        {
            var r = Measure(O("kind", "frog", "type", "pg", "name", "frog", "points", 128), 300);
            Assert.Equal("pg-frog", TraceData.FromInstrument(r, "frog").Kind);
            Assert.True(Throws(() => Commands.Retrieve(O("method", "pcgpa", "trace", TraceDict(r, "frog", null)), "."), "not symmetric"), "refused without a kind");
            var ok = Commands.Retrieve(O("method", "pcgpa", "trace", TraceDict(r, "frog", "pg-frog"), "options", O("maxIterations", 200)), ".");
            Assert.Less(ok.GetDouble("error", 1), 8e-3, "kind named in the data");
            var ok2 = Commands.Retrieve(O("method", "pcgpa", "kind", "pg-frog", "trace", TraceDict(r, "frog", null), "options", O("maxIterations", 200)), ".");
            Assert.Less(ok2.GetDouble("error", 1), 8e-3, "kind named in the request");
            // an SHG trace without a kind is fine, and says that SHG was assumed
            var rs = Measure(O("kind", "frog", "type", "shg", "name", "frog", "points", 128), 300);
            var shg = Commands.Retrieve(O("method", "pcgpa", "trace", TraceDict(rs, "frog", null), "options", O("maxIterations", 100)), ".");
            Assert.True(shg["notes"].AsArray().Any(n => n.AsString().Contains("assumed shg-frog")), "note: SHG assumed");
            // naming SHG for a PG trace warns
            var wrong = Commands.Retrieve(O("method", "pcgpa", "kind", "shg-frog", "trace", TraceDict(r, "frog", null), "options", O("maxIterations", 50)), ".");
            Assert.True(wrong["notes"].AsArray().Any(n => n.AsString().Contains("not symmetric")), "warning for an asymmetric trace named SHG");
        }
    }
}
