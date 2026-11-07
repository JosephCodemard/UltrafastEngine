// Multi-trace FROG retrieval ("double FROG"): several traces that share unknown pulses are retrieved together.
//
// Example (Kida et al., Opt. Express 22, 3004 (2014)): the SHG-FROG of a reference pulse R, the SHG-FROG of a test pulse S, and the
// XFROG (sum-frequency) trace of S gated by R. Each pulse alone is ambiguous in time direction (E(t) and E*(-t) give the same SHG-FROG);
// the XFROG trace ties the two directions together, and the SHG traces of both pulses remove the remaining twin.
//
// Main types
//   MultiTraceTerm      one trace: TraceData + the name of the pulse it measures (Test) and, for an XFROG trace, the pulse that gates it (Gate)
//   MultiTraceResult    one RetrievalResult per named pulse, the FROG error of every trace, iterations, notes
//   MultiTraceRetrieval.Run(terms, options)
// Options: points (grid, default next power of two >= the longest delay axis, at most 512), dtS, maxIterations (600), tolerance (1e-5),
//          seed (1), carriersRadS ({name: rad/s} for a pulse whose carrier no SHG / PG / SD trace fixes).
//
// Algorithm: all pulses live on one N-point time grid (step dt), each with its own carrier. In every sweep each trace in turn projects the
// current signal onto its measured magnitudes and updates the pulses it involves: SHG / PG / SD by the principal-component step of PCGPA
// (Kane 1999), XFROG by two least-squares steps (test pulse with the gate fixed, then gate with the test pulse fixed). Time origin: a common
// shift of all pulses is free (the traces fix their relative delays), so the first pulse is centred.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Retrieval
{
    /// <summary>One trace of a multi-trace retrieval.</summary>
    public sealed class MultiTraceTerm
    {
        public TraceData Data { get; set; }
        /// <summary>Name of the unknown pulse measured (the gated pulse for an XFROG trace).</summary>
        public string Test { get; set; }
        /// <summary>XFROG: name of the unknown gate pulse; null or empty: the gate is the known reference stored in <see cref="Data"/>.</summary>
        public string Gate { get; set; }
    }

    public sealed class MultiTraceResult
    {
        public Dictionary<string, RetrievalResult> Pulses { get; } = new Dictionary<string, RetrievalResult>();
        /// <summary>FROG error G of each trace, in the order given.</summary>
        public double[] TraceErrors { get; set; }
        public int Iterations { get; set; }
        /// <summary>Largest trace error after each sweep.</summary>
        public double[] ErrorHistory { get; set; }
        public List<string> Notes { get; } = new List<string>();
    }

    public static class MultiTraceRetrieval
    {
        private sealed class Term
        {
            public MultiTraceTerm Spec; public string Gate; public double Wc; public double[] Imeas;
            public Complex[] Ref;   // known XFROG gate on the retrieval grid
        }

        public static MultiTraceResult Run(IList<MultiTraceTerm> specs, JsonObject options)
        {
            options = options ?? new JsonObject();
            if (specs == null || specs.Count < 1) throw new ArgumentException("multi-trace retrieval needs at least one trace");
            var terms = new List<Term>();
            foreach (var sp in specs)
            {
                if (string.IsNullOrEmpty(sp.Test)) throw new ArgumentException("every trace needs the name of the pulse it measures (\"pulse\")");
                terms.Add(new Term { Spec = sp, Gate = FrogRetrievalBase.GateOf(sp.Data.Kind) });
                if (terms[terms.Count - 1].Gate == "x" && string.IsNullOrEmpty(sp.Gate) && sp.Data.ReferenceField == null)
                    throw new ArgumentException("an XFROG trace needs either the name of its (unknown) gate pulse (\"gate\") or a known reference");
            }
            // grid
            int N = options.GetInt("points", 0);
            int maxRows = terms.Max(t => t.Spec.Data.RowCount);
            if (N <= 0) { N = 1; while (N < Math.Max(maxRows, 64)) N <<= 1; N = Math.Min(N, 512); }
            var r0 = terms[0].Spec.Data.Rows;
            double Dt = options.GetDouble("dtS", Math.Abs(r0[r0.Length - 1] - r0[0]) / Math.Max(1, r0.Length - 1) * Math.Max(1.0, (double)r0.Length / N));
            var time = new double[N]; var dOff = new double[N]; double dW = 2 * Math.PI / (N * Dt);
            for (int i = 0; i < N; i++) { time[i] = (i - N / 2) * Dt; dOff[i] = (i - N / 2) * dW; }
            // signal centres and pulse carriers
            foreach (var t in terms)
            {
                var d = t.Spec.Data; double s = 0, sw = 0;
                for (int i = 0; i < d.RowCount; i++) for (int j = 0; j < d.ColumnCount; j++) { double v = d.Trace[i * d.ColumnCount + j]; s += v * d.Omega[j]; sw += v; }
                t.Wc = s / sw;
            }
            var carrier = new Dictionary<string, double>();
            var given = options.GetObject("carriersRadS");
            if (given != null) foreach (var kv in given) carrier[kv.Key] = kv.Value.AsDouble();
            for (int pass = 0; pass < terms.Count + 1; pass++)
                foreach (var t in terms)
                {
                    string test = t.Spec.Test, gate = t.Spec.Gate;
                    if (t.Gate == "x")
                    {
                        if (string.IsNullOrEmpty(gate)) { double wr = t.Spec.Data.ReferenceOmega0; if (!carrier.ContainsKey(test)) carrier[test] = t.Wc - wr; }
                        else if (carrier.ContainsKey(gate) && !carrier.ContainsKey(test)) carrier[test] = t.Wc - carrier[gate];
                        else if (carrier.ContainsKey(test) && !carrier.ContainsKey(gate)) carrier[gate] = t.Wc - carrier[test];
                    }
                    else if (!carrier.ContainsKey(test)) carrier[test] = FrogRetrievalBase.PulseCarrier(t.Gate, t.Wc, 0);
                }
            var names = new List<string>();
            foreach (var t in terms) { if (!names.Contains(t.Spec.Test)) names.Add(t.Spec.Test); if (t.Gate == "x" && !string.IsNullOrEmpty(t.Spec.Gate) && !names.Contains(t.Spec.Gate)) names.Add(t.Spec.Gate); }
            foreach (var nm in names)
                if (!carrier.ContainsKey(nm)) throw new ArgumentException("the carrier of pulse \"" + nm + "\" is not fixed by any SHG / PG / SD trace: give options.carriersRadS");
            // measured traces on the retrieval grid
            foreach (var t in terms)
            {
                var d = t.Spec.Data;
                t.Imeas = new double[N * N];
                for (int r = 0; r < N; r++) for (int k = 0; k < N; k++) t.Imeas[r * N + k] = Math.Max(0, d.At(time[r], t.Wc + dOff[k]));
                double mx = t.Imeas.Max(); if (mx > 0) for (int i = 0; i < t.Imeas.Length; i++) t.Imeas[i] /= mx;
                if (t.Gate == "x" && string.IsNullOrEmpty(t.Spec.Gate)) t.Ref = ResampleRef(d, time);
            }
            // initial guesses: Gaussians of the width of a trace's delay marginal, with a small seeded random phase
            var rnd = new Random(options.GetInt("seed", 1));
            var field = new Dictionary<string, Complex[]>();
            foreach (var nm in names)
            {
                var t = terms.First(x => x.Spec.Test == nm || x.Spec.Gate == nm);
                var dm = new double[N]; for (int r = 0; r < N; r++) for (int k = 0; k < N; k++) dm[r] += t.Imeas[r * N + k];
                double fw = Results.PulseMetrics.Fwhm(time, dm); if (double.IsNaN(fw) || fw <= 0) fw = 10 * Dt;
                double tau = fw / Math.Sqrt(2) / 1.665;
                var e = time.Select(x => Complex.FromPolarCoordinates(Math.Exp(-x * x / (2 * tau * tau)), 0.1 * (rnd.NextDouble() - 0.5))).ToArray();
                field[nm] = Normalise(e);
            }
            int maxIt = options.GetInt("maxIterations", 600); double tol = options.GetDouble("tolerance", 1e-5);
            var hist = new List<double>(); double bestMax = double.PositiveInfinity;
            var best = field.ToDictionary(kv => kv.Key, kv => (Complex[])kv.Value.Clone());
            double[] bestErr = null; int it;
            for (it = 0; it < maxIt; it++)
            {
                foreach (var t in terms)
                {
                    string a = t.Spec.Test, b = t.Spec.Gate;
                    if (t.Gate == "x")
                    {
                        var gate = t.Ref ?? field[b];
                        field[a] = Normalise(LsTest(t, field[a], gate, N));
                        if (t.Ref == null) field[b] = Normalise(LsGate(t, field[a], field[b], N));
                    }
                    else field[a] = Normalise(Pcgpa(t, field[a], N));
                }
                var errs = terms.Select(t => TraceError(t, field, N)).ToArray();
                double mxErr = errs.Max(); hist.Add(mxErr);
                if (mxErr < bestMax) { bestMax = mxErr; bestErr = errs; foreach (var nm in names) best[nm] = (Complex[])field[nm].Clone(); }
                if (mxErr < tol) { it++; break; }
            }
            // a common shift (the first pulse centred) and per-pulse global phase
            var first = best[names[0]];
            var pw = first.Select(z => z.Magnitude * z.Magnitude).ToArray();
            int sh = N / 2 - Array.IndexOf(pw, pw.Max());
            var result = new MultiTraceResult { TraceErrors = bestErr, Iterations = it, ErrorHistory = hist.ToArray() };
            foreach (var nm in names)
            {
                var c = new Complex[N];
                for (int i = 0; i < N; i++) c[((i + sh) % N + N) % N] = best[nm][i];
                var p2 = c.Select(z => z.Magnitude * z.Magnitude).ToArray(); int ic = Array.IndexOf(p2, p2.Max());
                var ph = c[ic].Magnitude > 0 ? Complex.FromPolarCoordinates(1, -c[ic].Phase) : Complex.One;
                for (int i = 0; i < N; i++) c[i] *= ph;
                var grid = FrequencyGrid.Create(N, Dt, carrier[nm]);
                var rr = new RetrievalResult
                {
                    Method = "multiFrog", Time = (double[])time.Clone(), Field = c, Omega0 = carrier[nm], Omega = (double[])grid.OmegaAbsolute.Clone(),
                    Spectrum = SpectralTransform.TimeToFreq(c), Error = bestErr.Max(), Iterations = it, ErrorHistory = hist.ToArray()
                };
                rr.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0} x {0} grid, dt = {1:G4} fs, largest trace error G = {2:G3}", N, Dt * 1e15, bestErr.Max()));
                result.Pulses[nm] = rr;
            }
            result.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0} traces, {1} pulse(s), {2} sweeps; trace errors G: {3}", terms.Count, names.Count, it,
                string.Join(", ", bestErr.Select(x => x.ToString("G3", CultureInfo.InvariantCulture)))));
            if (!terms.Any(t => t.Gate == "x")) result.Notes.Add("no XFROG trace links the pulses: each SHG-based pulse keeps its time-direction ambiguity");
            return result;
        }

        private static Complex[] Normalise(Complex[] e)
        {
            double n = Math.Sqrt(e.Sum(z => z.Real * z.Real + z.Imaginary * z.Imaginary));
            return n > 0 ? e.Select(z => z / n).ToArray() : e;
        }

        private static Complex[] ResampleRef(TraceData d, double[] time)
        {
            var rt = d.ReferenceTime; var rf = d.ReferenceField; var r = new Complex[time.Length];
            for (int i = 0; i < time.Length; i++)
            {
                double t = time[i]; if (t < rt[0] || t > rt[rt.Length - 1]) continue;
                double f = (t - rt[0]) / (rt[rt.Length - 1] - rt[0]) * (rt.Length - 1); int k = Math.Min(rt.Length - 2, (int)Math.Floor(f)); double u = f - k;
                r[i] = rf[k] * (1 - u) + rf[k + 1] * u;
            }
            return r;
        }

        /// <summary>E_sig(t_i, tau_r) for the trace's gate: test pulse a, gate pulse g (for XFROG; otherwise g is ignored and a gates itself).</summary>
        private static Complex[] Signal(string gate, Complex[] a, Complex[] g, int r, int N)
        {
            int s = r - N / 2; var row = new Complex[N];
            for (int i = 0; i < N; i++)
            {
                int j = ((i - s) % N + N) % N;
                switch (gate)
                {
                    case "pg": row[i] = a[i] * (Complex)(a[j].Magnitude * a[j].Magnitude); break;
                    case "sd": row[i] = a[i] * a[i] * Complex.Conjugate(a[j]); break;
                    case "x": row[i] = a[i] * g[j]; break;
                    default: row[i] = a[i] * a[j]; break;
                }
            }
            return row;
        }

        /// <summary>Project the signal of every delay onto the measured magnitudes; O[i, j] = projected signal (time i, delayed time j = i - shift).</summary>
        private static Complex[] Project(Term t, Complex[] a, Complex[] g, int N)
        {
            var O = new Complex[N * N];
            for (int r = 0; r < N; r++)
            {
                var f = SpectralTransform.TimeToFreq(Signal(t.Gate, a, g, r, N));
                for (int k = 0; k < N; k++) { double m = Math.Sqrt(t.Imeas[r * N + k]), am = f[k].Magnitude; f[k] = am > 0 ? f[k] * (m / am) : m; }
                var s = SpectralTransform.FreqToTime(f); int sh = r - N / 2;
                for (int i = 0; i < N; i++) O[i * N + ((i - sh) % N + N) % N] = s[i];
            }
            return O;
        }

        private static Complex[] Pcgpa(Term t, Complex[] e, int N)
        {
            var O = Project(t, e, e, N);
            var en = new Complex[N];
            if (t.Gate == "sd")
            {
                var u = new Complex[N];
                for (int i = 0; i < N; i++) { Complex acc = 0; for (int j = 0; j < N; j++) acc += O[i * N + j] * e[j]; u[i] = acc; }
                for (int j = 0; j < N; j++) { Complex acc = 0; for (int i = 0; i < N; i++) acc += Complex.Conjugate(O[i * N + j]) * u[i]; en[j] = acc; }
                return en;
            }
            var tt = new Complex[N];
            for (int j = 0; j < N; j++) { Complex acc = 0; for (int i = 0; i < N; i++) acc += Complex.Conjugate(O[i * N + j]) * e[i]; tt[j] = acc; }
            for (int i = 0; i < N; i++) { Complex acc = 0; for (int j = 0; j < N; j++) acc += O[i * N + j] * tt[j]; en[i] = acc; }
            return en;
        }

        /// <summary>Least squares for the test pulse with the gate fixed: a_i = sum_j O[i, j] conj(g_j) / sum |g|^2.</summary>
        private static Complex[] LsTest(Term t, Complex[] a, Complex[] g, int N)
        {
            var O = Project(t, a, g, N); double g2 = g.Sum(z => z.Real * z.Real + z.Imaginary * z.Imaginary);
            var en = new Complex[N];
            for (int i = 0; i < N; i++) { Complex acc = 0; for (int j = 0; j < N; j++) acc += O[i * N + j] * Complex.Conjugate(g[j]); en[i] = acc / Math.Max(g2, 1e-300); }
            return en;
        }

        /// <summary>Least squares for the gate with the test pulse fixed: g_j = sum_i conj(a_i) O[i, j] / sum |a|^2.</summary>
        private static Complex[] LsGate(Term t, Complex[] a, Complex[] g, int N)
        {
            var O = Project(t, a, g, N); double a2 = a.Sum(z => z.Real * z.Real + z.Imaginary * z.Imaginary);
            var en = new Complex[N];
            for (int j = 0; j < N; j++) { Complex acc = 0; for (int i = 0; i < N; i++) acc += Complex.Conjugate(a[i]) * O[i * N + j]; en[j] = acc / Math.Max(a2, 1e-300); }
            return en;
        }

        private static double TraceError(Term t, Dictionary<string, Complex[]> field, int N)
        {
            var a = field[t.Spec.Test]; var g = t.Gate == "x" ? (t.Ref ?? field[t.Spec.Gate]) : a;
            var iret = new double[N * N];
            for (int r = 0; r < N; r++)
            {
                var f = SpectralTransform.TimeToFreq(Signal(t.Gate, a, g, r, N));
                for (int k = 0; k < N; k++) iret[r * N + k] = f[k].Real * f[k].Real + f[k].Imaginary * f[k].Imaginary;
            }
            double num = 0, den = 0;
            for (int i = 0; i < iret.Length; i++) { num += t.Imeas[i] * iret[i]; den += iret[i] * iret[i]; }
            double mu = den > 0 ? num / den : 0, s = 0;
            for (int i = 0; i < iret.Length; i++) { double dd = t.Imeas[i] - mu * iret[i]; s += dd * dd; }
            return Math.Sqrt(s / iret.Length);
        }
    }
}
