using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Retrieval
{
    /// <summary>A measured (or simulated) trace to retrieve a pulse from (docs/reference/retrieval.md).</summary>
    public sealed class TraceData
    {
        /// <summary>"shg-frog", "pg-frog" or "dscan".</summary>
        public string Kind { get; set; } = "shg-frog";
        /// <summary>Row axis: delays (s, FROG) or glass insertions (m) / added GDD (s^2) (d-scan).</summary>
        public double[] Rows { get; set; }
        /// <summary>Column axis: absolute angular frequency of the signal (rad/s).</summary>
        public double[] Omega { get; set; }
        /// <summary>Trace, row-major [row, column].</summary>
        public double[] Trace { get; set; }
        /// <summary>d-scan: the fundamental spectrum (absolute omega, intensity per omega) and the wedge material ("gdd" for a GDD scan).</summary>
        public double[] FundamentalOmega { get; set; }
        public double[] FundamentalIntensity { get; set; }
        public string Material { get; set; } = "FusedSilica";
        /// <summary>Carrier of the fundamental (rad/s); default from the trace (FROG: half the signal centroid).</summary>
        public double Omega0 { get; set; } = double.NaN;
        public int RowCount => Rows.Length;
        public int ColumnCount => Omega.Length;

        /// <summary>Trace from an instrument of a result (frog or dscan), e.g. to retrieve a simulated measurement.</summary>
        public static TraceData FromInstrument(Results.RunResult r, string instrument)
        {
            var o = r.Instruments.FirstOrDefault(i => string.Equals(i.Name, instrument, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("no instrument '" + instrument + "' in the result");
            double[] A(string suffix) => o.Arrays.FirstOrDefault(a => a.Name.EndsWith("_" + suffix))?.Data;
            if (o.Kind == "frog")
                return new TraceData { Kind = "shg-frog", Rows = A("delay_s"), Omega = A("omega_rad_s"), Trace = A("trace") };
            if (o.Kind == "dscan")
            {
                var wl = A("shg_wavelength_m");
                var t = new TraceData { Kind = "dscan", Rows = A("insertion_m") ?? A("gdd_s2"), Omega = wl.Select(l => 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / l).ToArray(), Trace = A("trace") };
                if (A("insertion_m") == null) t.Material = "gdd";
                var spec = o.Values;
                return t;
            }
            throw new ArgumentException("instrument '" + instrument + "' is a " + o.Kind + " (frog or dscan needed)");
        }

        /// <summary>Load from an NPZ or JSON file with keys delay_s / insertion_m / gdd_s2, omega_rad_s or wavelength_m (columns) and trace
        /// (2-D); for d-scan also fundamental_wavelength_m + fundamental_intensity (or a CSV via options).</summary>
        public static TraceData Load(string path, string kind, string instrument = null)
        {
            var arrays = new Dictionary<string, (double[] Data, int[] Shape)>(StringComparer.OrdinalIgnoreCase);
            if (path.EndsWith(".npz", StringComparison.OrdinalIgnoreCase))
                foreach (var a in NpzWriter.Read(path)) arrays[a.Name] = (a.Data, a.Shape);
            else
            {
                var o = JsonValue.ParseFile(path).AsObject();
                foreach (var kv in o) if (kv.Value.Kind == JsonKind.Array)
                    {
                        var arr = kv.Value.AsArray();
                        if (arr.Count > 0 && arr[0].Kind == JsonKind.Array) { var rows = arr.Select(x => x.AsDoubleArray()).ToArray(); arrays[kv.Key] = (rows.SelectMany(x => x).ToArray(), new[] { rows.Length, rows[0].Length }); }
                        else arrays[kv.Key] = (kv.Value.AsDoubleArray(), new[] { arr.Count });
                    }
            }
            // a results file holds many instruments: find the trace (optionally "prefix_trace" for an instrument name)
            // and take the axes with the same prefix, so delay_s / omega_rad_s of another instrument are not mixed in
            string traceKey = instrument != null
                ? arrays.Keys.FirstOrDefault(x => x.Equals(instrument + "_trace", StringComparison.OrdinalIgnoreCase))
                    ?? throw new FormatException(path + ": no array \"" + instrument + "_trace\" (instruments with traces: " + string.Join(", ", arrays.Keys.Where(x => x.EndsWith("_trace", StringComparison.OrdinalIgnoreCase)).Select(x => x.Substring(0, x.Length - 6))) + ")")
                : arrays.Keys.FirstOrDefault(x => x.Equals("trace", StringComparison.OrdinalIgnoreCase)) ?? arrays.Keys.FirstOrDefault(x => x.EndsWith("_trace", StringComparison.OrdinalIgnoreCase) && arrays[x].Shape.Length == 2);
            string prefix = traceKey != null && traceKey.Length > 5 ? traceKey.Substring(0, traceKey.Length - 5) : "";
            double[] Get(params string[] names)
            {
                foreach (var nm in names)
                {
                    var k = arrays.Keys.FirstOrDefault(x => x.Equals(prefix + nm, StringComparison.OrdinalIgnoreCase))
                         ?? (prefix == "" ? arrays.Keys.FirstOrDefault(x => x.Equals(nm, StringComparison.OrdinalIgnoreCase) || x.EndsWith("_" + nm, StringComparison.OrdinalIgnoreCase)) : null);
                    if (k != null) return arrays[k].Data;
                }
                return null;
            }
            var t = new TraceData { Kind = kind ?? "shg-frog" };
            t.Rows = Get("delay_s", "insertion_m", "gdd_s2") ?? throw new FormatException(path + ": no row axis (delay_s, insertion_m or gdd_s2)");
            if (Get("insertion_m") == null && Get("gdd_s2") != null) t.Material = "gdd";
            var om = Get("omega_rad_s");
            var wl = Get("wavelength_m", "shg_wavelength_m");
            t.Omega = om ?? wl?.Select(l => 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / l).ToArray() ?? throw new FormatException(path + ": no column axis (omega_rad_s or wavelength_m)");
            t.Trace = (traceKey != null ? arrays[traceKey].Data : null) ?? throw new FormatException(path + ": no trace");
            if (t.Trace.Length != t.Rows.Length * t.Omega.Length)
                throw new FormatException(string.Format(CultureInfo.InvariantCulture, "{0}: trace has {1} values, but the axes give {2} rows x {3} columns", path, t.Trace.Length, t.Rows.Length, t.Omega.Length));
            var fw = Get("fundamental_wavelength_m"); var fi = Get("fundamental_intensity");
            if (fw != null && fi != null) { t.FundamentalOmega = fw.Select(l => 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / l).ToArray(); t.FundamentalIntensity = fi.Select((v, i) => v * fw[i] * fw[i]).ToArray(); }
            return t;
        }

        /// <summary>Bilinear value at (row coordinate r, absolute omega w); 0 outside.</summary>
        public double At(double r, double w)
        {
            int nr = Rows.Length, nc = Omega.Length;
            var (i0, fr) = Locate(Rows, r); var (j0, fc) = Locate(Omega, w);
            if (i0 < 0 || j0 < 0) return 0;
            double V(int i, int j) => Trace[Math.Min(i, nr - 1) * nc + Math.Min(j, nc - 1)];
            return V(i0, j0) * (1 - fr) * (1 - fc) + V(i0 + 1, j0) * fr * (1 - fc) + V(i0, j0 + 1) * (1 - fr) * fc + V(i0 + 1, j0 + 1) * fr * fc;
        }

        private static (int, double) Locate(double[] x, double v)
        {
            bool asc = x[x.Length - 1] >= x[0];
            int n = x.Length;
            double lo = asc ? x[0] : x[n - 1], hi = asc ? x[n - 1] : x[0];
            if (v < lo || v > hi) return (-1, 0);
            for (int i = 0; i < n - 1; i++)
            {
                double a = x[i], b = x[i + 1];
                if ((v >= a && v <= b) || (v <= a && v >= b)) return (i, b == a ? 0 : (v - a) / (b - a));
            }
            return (n - 1, 0);
        }
    }

    /// <summary>The retrieved pulse.</summary>
    public sealed class RetrievalResult
    {
        public string Method { get; set; }
        /// <summary>Time axis (s) and complex field envelope (sqrt(W), arbitrary scale) about Omega0.</summary>
        public double[] Time { get; set; }
        public Complex[] Field { get; set; }
        public double Omega0 { get; set; }
        /// <summary>Absolute omega (rad/s) and the complex spectrum (engine convention).</summary>
        public double[] Omega { get; set; }
        public Complex[] Spectrum { get; set; }
        /// <summary>FROG error G (rms difference of the normalised traces) or d-scan rms residual.</summary>
        public double Error { get; set; }
        public int Iterations { get; set; }
        public double[] ErrorHistory { get; set; }
        /// <summary>Retrieved trace on the measured axes (row-major).</summary>
        public double[] RetrievedTrace { get; set; }
        public List<string> Notes { get; } = new List<string>();

        public double[] TemporalIntensity => Field.Select(z => z.Real * z.Real + z.Imaginary * z.Imaginary).ToArray();
        public double FwhmS => Results.PulseMetrics.Fwhm(Time, TemporalIntensity);

        /// <summary>The spectrum restricted to its support as a run-spec pulse ({"shape": "spectrum", ...}) at the given energy.</summary>
        public JsonObject ToPulseJson(double energyJ)
        {
            var I = Spectrum.Select(z => z.Real * z.Real + z.Imaginary * z.Imaginary).ToArray();
            double mx = I.Max();
            var idx = Enumerable.Range(0, I.Length).Where(i => I[i] > 1e-6 * mx && Omega[i] > 0).ToArray();
            var ph = PhaseUnwrap.Unwrap(Spectrum.Select(z => z.Phase).ToArray());
            double c = PhysicalConstants.SpeedOfLightMPerS;
            var wl = idx.Select(i => 2 * Math.PI * c / Omega[i]).ToArray();
            var inten = idx.Select(i => I[i] / (wl[Array.IndexOf(idx, i)] * wl[Array.IndexOf(idx, i)])).ToArray();   // per wavelength
            return new JsonObject().Set("shape", "spectrum").Set("energyJ", energyJ).Set("centerWavelengthM", 2 * Math.PI * c / Omega0)
                .Set("spectrum", new JsonObject().Set("wavelengthsM", JsonValue.FromArray(wl)).Set("intensity", JsonValue.FromArray(inten)).Set("phaseRad", JsonValue.FromArray(idx.Select(i => ph[i]).ToArray())));
        }

        /// <summary>CSV: wavelength (nm), spectral intensity per wavelength, phase (rad) -- loadable as a run-spec pulse spectrum.</summary>
        public void WriteSpectrumCsv(string path)
        {
            var p = ToPulseJson(1).GetObject("spectrum");
            var wl = p.GetDoubleArray("wavelengthsM"); var I = p.GetDoubleArray("intensity"); var ph = p.GetDoubleArray("phaseRad");
            using (var w = new StreamWriter(path))
            {
                w.WriteLine("wavelength_nm,intensity,phase_rad");
                foreach (var i in Enumerable.Range(0, wl.Length).OrderBy(i => wl[i]))
                    w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:G10},{1:G8},{2:G8}", wl[i] * 1e9, I[i], ph[i]));
            }
        }

        public JsonObject ToJson() => new JsonObject().Set("method", Method).Set("error", Error).Set("iterations", Iterations)
            .Set("fwhmS", FwhmS).Set("centerWavelengthM", 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / Omega0)
            .Set("notes", JsonValue.FromArray(Notes));
    }

    /// <summary>A pulse-retrieval algorithm. Register new ones with <see cref="Retrievals.Register"/> (or a plugin).</summary>
    public interface IRetrieval
    {
        string Name { get; }
        string Description { get; }
        /// <summary>Trace kinds it accepts ("shg-frog", "pg-frog", "dscan").</summary>
        string[] TraceKinds { get; }
        RetrievalResult Retrieve(TraceData data, JsonObject options);
    }

    public static class Retrievals
    {
        private static readonly Dictionary<string, IRetrieval> All = new Dictionary<string, IRetrieval>(StringComparer.OrdinalIgnoreCase)
        {
            { "pcgpa", new PcgpaRetrieval() }, { "epie", new EpieRetrieval() }, { "dscan", new DScanRetrieval() },
        };
        public static void Register(IRetrieval r) { lock (All) All[r.Name] = r; }
        public static IEnumerable<IRetrieval> Available { get { lock (All) return All.Values.ToList(); } }
        public static IRetrieval Get(string name) { lock (All) return All.TryGetValue(name, out var r) ? r : throw new ArgumentException("unknown retrieval \"" + name + "\" (available: " + string.Join(", ", All.Keys) + ")"); }
        public static RetrievalResult Run(string method, TraceData data, JsonObject options = null) => Get(method).Retrieve(data, options ?? new JsonObject());
    }

    // ============================================================================================ FROG

    /// <summary>Shared FROG machinery: the N x N grid, resampling of the measured trace, the forward model (identical to
    /// the engine's frog instrument), errors.</summary>
    public abstract class FrogRetrievalBase : IRetrieval
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public string[] TraceKinds => new[] { "shg-frog", "pg-frog" };

        protected int N; protected double Dt; protected FrequencyGrid G; protected double[] Imeas; protected bool Pg; protected double Wc;

        protected void Setup(TraceData d, JsonObject o)
        {
            Pg = d.Kind == "pg-frog";
            N = o.GetInt("points", 0);
            if (N <= 0) { N = 1; while (N < Math.Max(d.RowCount, 64)) N <<= 1; N = Math.Min(N, 512); }
            // delay step from the measured delays (FROG grids: dt dw = 2 pi / N)
            var rows = d.Rows; double span = Math.Abs(rows[rows.Length - 1] - rows[0]);
            Dt = o.GetDouble("dtS", span / Math.Max(1, rows.Length - 1) * Math.Max(1.0, (double)rows.Length / N));
            // signal centre: the trace's frequency centroid
            double sw = 0, s = 0;
            for (int i = 0; i < d.RowCount; i++) for (int j = 0; j < d.ColumnCount; j++) { double v = d.Trace[i * d.ColumnCount + j]; s += v * d.Omega[j]; sw += v; }
            Wc = s / sw;
            double w0 = double.IsNaN(d.Omega0) ? (Pg ? Wc : Wc / 2) : d.Omega0;
            G = FrequencyGrid.Create(N, Dt, w0);
            Imeas = new double[N * N];
            for (int r = 0; r < N; r++)
                for (int k = 0; k < N; k++)
                    Imeas[r * N + k] = Math.Max(0, d.At(G.Time[r], Wc + G.OmegaOffset[k]));
            double mx = Imeas.Max(); if (mx > 0) for (int i = 0; i < Imeas.Length; i++) Imeas[i] /= mx;
        }

        /// <summary>Signal field E_sig(t_i, tau_r) = E(t_i) g(t_i - tau_r) (circular shifts on the N-point grid).</summary>
        protected Complex[] Signal(Complex[] e, int r)
        {
            int s = r - N / 2;
            var row = new Complex[N];
            for (int i = 0; i < N; i++) { var gte = e[((i - s) % N + N) % N]; row[i] = e[i] * (Pg ? (Complex)(gte.Magnitude * gte.Magnitude) : gte); }
            return row;
        }

        protected double[] Forward(Complex[] e)
        {
            var t = new double[N * N];
            for (int r = 0; r < N; r++)
            {
                var f = SpectralTransform.TimeToFreq(Signal(e, r));
                for (int k = 0; k < N; k++) t[r * N + k] = f[k].Real * f[k].Real + f[k].Imaginary * f[k].Imaginary;
            }
            return t;
        }

        /// <summary>FROG error G = sqrt(mean (Imeas - mu Iret)^2) with the best scale mu.</summary>
        protected double Error(double[] iret)
        {
            double num = 0, den = 0;
            for (int i = 0; i < iret.Length; i++) { num += Imeas[i] * iret[i]; den += iret[i] * iret[i]; }
            double mu = den > 0 ? num / den : 0, s = 0;
            for (int i = 0; i < iret.Length; i++) { double d = Imeas[i] - mu * iret[i]; s += d * d; }
            return Math.Sqrt(s / iret.Length);
        }

        /// <summary>Initial guess: a Gaussian of the delay-marginal width with a small random phase (seeded).</summary>
        protected Complex[] Guess(JsonObject o)
        {
            var rnd = new Random(o.GetInt("seed", 1));
            var dm = new double[N];
            for (int r = 0; r < N; r++) for (int k = 0; k < N; k++) dm[r] += Imeas[r * N + k];
            double fw = Results.PulseMetrics.Fwhm(G.Time, dm);
            if (double.IsNaN(fw) || fw <= 0) fw = 10 * Dt;
            double tau = fw / Math.Sqrt(2) / 1.665;
            return G.Time.Select(t => Complex.FromPolarCoordinates(Math.Exp(-t * t / (2 * tau * tau)), 0.1 * (rnd.NextDouble() - 0.5))).ToArray();
        }

        protected RetrievalResult Finish(Complex[] e, double err, int it, List<double> hist, TraceData d)
        {
            // centre the pulse (FROG has no absolute time) and fix the global phase
            var p = e.Select(z => z.Magnitude * z.Magnitude).ToArray();
            int ip = Array.IndexOf(p, p.Max()), sh = N / 2 - ip;
            var c = new Complex[N];
            for (int i = 0; i < N; i++) c[((i + sh) % N + N) % N] = e[i];
            var ph = c[N / 2].Magnitude > 0 ? Complex.FromPolarCoordinates(1, -c[N / 2].Phase) : Complex.One;
            for (int i = 0; i < N; i++) c[i] *= ph;
            var spec = SpectralTransform.TimeToFreq(c);
            var res = new RetrievalResult
            {
                Method = Name, Time = (double[])G.Time.Clone(), Field = c, Omega0 = G.OmegaCenter, Omega = (double[])G.OmegaAbsolute.Clone(), Spectrum = spec,
                Error = err, Iterations = it, ErrorHistory = hist.ToArray()
            };
            // retrieved trace on the measured axes
            var tr = Forward(c); double mx = tr.Max();
            var out2 = new double[d.RowCount * d.ColumnCount];
            for (int r = 0; r < d.RowCount; r++)
                for (int k = 0; k < d.ColumnCount; k++)
                {
                    double fr = (d.Rows[r] - G.Time[0]) / Dt, fk = (d.Omega[k] - Wc - G.OmegaOffset[0]) / G.DOmega;
                    int ir = (int)Math.Floor(fr), ik = (int)Math.Floor(fk);
                    if (ir < 0 || ik < 0 || ir >= N - 1 || ik >= N - 1) continue;
                    double ur = fr - ir, uk = fk - ik;
                    out2[r * d.ColumnCount + k] = (tr[ir * N + ik] * (1 - ur) * (1 - uk) + tr[(ir + 1) * N + ik] * ur * (1 - uk) + tr[ir * N + ik + 1] * (1 - ur) * uk + tr[(ir + 1) * N + ik + 1] * ur * uk) / mx;
                }
            res.RetrievedTrace = out2;
            if (!Pg) res.Notes.Add("SHG-FROG cannot tell E(t) from E*(-t): the sign of the spectral phase (the direction of the chirp) is ambiguous");
            res.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0} x {0} grid, dt = {1:G4} fs, FROG error G = {2:G3}", N, Dt * 1e15, err));
            return res;
        }

        public abstract RetrievalResult Retrieve(TraceData data, JsonObject options);
    }

    /// <summary>
    /// Principal-component generalised projections (Kane, IEEE JQE 35, 421 (1999)): the signal is projected onto the
    /// measured magnitudes, turned back into the outer product O = E g^T, and E (and the gate) are updated by one power
    /// iteration E ← O O† E. Fast and robust for SHG- and PG-FROG.
    /// </summary>
    public sealed class PcgpaRetrieval : FrogRetrievalBase
    {
        public override string Name => "pcgpa";
        public override string Description => "FROG: principal-component generalised projections (SHG, PG)";

        public override RetrievalResult Retrieve(TraceData d, JsonObject o)
        {
            Setup(d, o);
            int maxIt = o.GetInt("maxIterations", 300); double tol = o.GetDouble("tolerance", 1e-5);
            var e = Guess(o);
            var best = (Complex[])e.Clone(); double bestErr = double.PositiveInfinity; var hist = new List<double>();
            var O = new Complex[N * N];
            int it;
            for (it = 0; it < maxIt; it++)
            {
                for (int r = 0; r < N; r++)
                {
                    var f = SpectralTransform.TimeToFreq(Signal(e, r));
                    for (int k = 0; k < N; k++)
                    {
                        double m = Math.Sqrt(Imeas[r * N + k]), a = f[k].Magnitude;
                        f[k] = a > 0 ? f[k] * (m / a) : m;
                    }
                    var s = SpectralTransform.FreqToTime(f);
                    int sh = r - N / 2;
                    for (int i = 0; i < N; i++) O[i * N + ((i - sh) % N + N) % N] = s[i];
                }
                // power iteration: E <- O O^H E  (gate: for SHG the same field; PG: |E|^2 gate follows E)
                var t = new Complex[N];
                for (int j = 0; j < N; j++) { Complex acc = 0; for (int i = 0; i < N; i++) acc += Complex.Conjugate(O[i * N + j]) * e[i]; t[j] = acc; }
                var en = new Complex[N];
                for (int i = 0; i < N; i++) { Complex acc = 0; for (int j = 0; j < N; j++) acc += O[i * N + j] * t[j]; en[i] = acc; }
                double nrm = Math.Sqrt(en.Sum(z => z.Real * z.Real + z.Imaginary * z.Imaginary));
                if (nrm == 0) break;
                for (int i = 0; i < N; i++) en[i] /= nrm;
                e = en;
                double err = Error(Forward(e));
                hist.Add(err);
                if (err < bestErr) { bestErr = err; best = (Complex[])e.Clone(); }
                if (err < tol) { it++; break; }
            }
            return Finish(best, bestErr, it, hist, d);
        }
    }

    /// <summary>
    /// Ptychographic retrieval (ePIE adapted to FROG; Sidorenko et al., Optica 3, 1320 (2016)): delays are visited in
    /// random order, each exit field is projected onto its measured spectrum and the object (the pulse) is updated with
    /// the gate as probe. Converges well on noisy traces.
    /// </summary>
    public sealed class EpieRetrieval : FrogRetrievalBase
    {
        public override string Name => "epie";
        public override string Description => "FROG: extended ptychographic iterative engine (SHG, PG)";

        public override RetrievalResult Retrieve(TraceData d, JsonObject o)
        {
            Setup(d, o);
            int maxIt = o.GetInt("maxIterations", 300); double tol = o.GetDouble("tolerance", 1e-5), alpha = o.GetDouble("stepSize", 0.5);
            var rnd = new Random(o.GetInt("seed", 1));
            var e = Guess(o);
            var best = (Complex[])e.Clone(); double bestErr = double.PositiveInfinity; var hist = new List<double>();
            var order = Enumerable.Range(0, N).ToArray();
            int it;
            for (it = 0; it < maxIt; it++)
            {
                for (int a = N - 1; a > 0; a--) { int b = rnd.Next(a + 1); var tmp = order[a]; order[a] = order[b]; order[b] = tmp; }
                foreach (int r in order)
                {
                    int s = r - N / 2;
                    var gate = new Complex[N];
                    for (int i = 0; i < N; i++) { var g = e[((i - s) % N + N) % N]; gate[i] = Pg ? (Complex)(g.Magnitude * g.Magnitude) : g; }
                    var psi = new Complex[N]; for (int i = 0; i < N; i++) psi[i] = e[i] * gate[i];
                    var f = SpectralTransform.TimeToFreq(psi);
                    for (int k = 0; k < N; k++) { double m = Math.Sqrt(Imeas[r * N + k]), am = f[k].Magnitude; f[k] = am > 0 ? f[k] * (m / am) : m; }
                    var psi2 = SpectralTransform.FreqToTime(f);
                    double gmax = gate.Max(z => z.Magnitude * z.Magnitude), emax = e.Max(z => z.Magnitude * z.Magnitude);
                    if (gmax <= 0) continue;
                    var en = (Complex[])e.Clone();
                    for (int i = 0; i < N; i++)
                    {
                        var dlt = psi2[i] - psi[i];
                        en[i] += alpha * Complex.Conjugate(gate[i]) / gmax * dlt;                       // object update
                        if (!Pg) en[((i - s) % N + N) % N] += alpha * Complex.Conjugate(e[i]) / emax * dlt;  // the gate is the pulse itself (SHG)
                    }
                    e = en;
                }
                double nrm = Math.Sqrt(e.Sum(z => z.Real * z.Real + z.Imaginary * z.Imaginary));
                for (int i = 0; i < N; i++) e[i] /= nrm;
                double err = Error(Forward(e));
                hist.Add(err);
                if (err < bestErr) { bestErr = err; best = (Complex[])e.Clone(); }
                if (err < tol) { it++; break; }
            }
            return Finish(best, bestErr, it, hist, d);
        }
    }

    // ============================================================================================ d-scan

    /// <summary>
    /// d-scan retrieval: the measured fundamental spectrum is kept; the spectral phase (a smooth curve through
    /// "nodes" points across the spectrum, plus optional Taylor terms) is fitted by Levenberg-Marquardt so that the
    /// simulated SHG-vs-insertion trace (the engine's dscan forward model) matches the measured one, with a per-column
    /// calibration (spectral response of the SHG detection) eliminated analytically.
    /// </summary>
    public sealed class DScanRetrieval : IRetrieval
    {
        public string Name => "dscan";
        public string Description => "d-scan: spectral phase by least squares on the SHG trace (fundamental spectrum known)";
        public string[] TraceKinds => new[] { "dscan" };

        public RetrievalResult Retrieve(TraceData d, JsonObject o)
        {
            if (d.FundamentalOmega == null) throw new ArgumentException("d-scan retrieval needs the fundamental spectrum (fundamental_wavelength_m + fundamental_intensity)");
            int nodes = o.GetInt("nodes", 16), maxIt = o.GetInt("maxIterations", 60);
            // grid: holds the fundamental spectrum and its SHG
            double c = PhysicalConstants.SpeedOfLightMPerS;
            var fw = d.FundamentalOmega; var fi = d.FundamentalIntensity;
            double sw = 0, s = 0; for (int i = 0; i < fw.Length; i++) { s += fi[i] * fw[i]; sw += fi[i]; }
            double w0 = double.IsNaN(d.Omega0) ? s / sw : d.Omega0;
            double span = (fw.Max() - fw.Min());
            int n = o.GetInt("points", 2048);
            double dw = 4 * span / n;
            double dt = 2 * Math.PI / (n * dw);
            var g = FrequencyGrid.Create(n, dt, w0);
            var ord = Enumerable.Range(0, fw.Length).OrderBy(i => fw[i]).ToArray();
            var li = new LinearInterpolator(ord.Select(i => fw[i]).ToArray(), ord.Select(i => Math.Max(0, fi[i])).ToArray());
            double wlo = fw.Min(), whi = fw.Max();
            var amp = g.OmegaAbsolute.Select(w => w < wlo || w > whi ? 0 : Math.Sqrt(Math.Max(0, li.Evaluate(w)))).ToArray();
            // phase per unit insertion (carrier phase and group delay removed) or per unit GDD
            var unit = new double[n];
            bool gdd = d.Material == "gdd";
            if (!gdd)
            {
                var m = MaterialLibrary.BuiltIn(d.Material);
                var nIdx = new double[n]; m.Index.Evaluate(g.OmegaAbsolute, nIdx);
                double ng0 = m.GroupIndexAt(w0), n0 = m.IndexAt(w0);
                for (int i = 0; i < n; i++) { double w = g.OmegaAbsolute[i]; unit[i] = double.IsNaN(nIdx[i]) ? 0 : (nIdx[i] * w - n0 * w0 - ng0 * (w - w0)) / c; }
            }
            else for (int i = 0; i < n; i++) unit[i] = 0.5 * g.OmegaOffset[i] * g.OmegaOffset[i];
            // node positions over the spectral support (relative omega, phase in rad)
            var nodeW = Enumerable.Range(0, nodes).Select(k => wlo + (whi - wlo) * k / (nodes - 1.0)).ToArray();
            double[] PhaseOf(double[] p)
            {
                var spl = new NaturalCubicSpline(nodeW, p);
                return g.OmegaAbsolute.Select(w => w < wlo || w > whi ? 0 : spl.Evaluate(w)).ToArray();
            }
            int R = d.RowCount, C = d.ColumnCount;
            var meas = (double[])d.Trace.Clone(); double mm = meas.Max(); for (int i = 0; i < meas.Length; i++) meas[i] /= mm;
            double[] Model(double[] p)
            {
                var ph = PhaseOf(p);
                var t = new double[R * C];
                for (int r = 0; r < R; r++)
                {
                    var a = new Complex[n];
                    for (int i = 0; i < n; i++) a[i] = Complex.FromPolarCoordinates(amp[i], ph[i] + unit[i] * d.Rows[r]);
                    var at = SpectralTransform.FreqToTime(a);
                    for (int i = 0; i < n; i++) at[i] *= at[i];
                    var sf = SpectralTransform.TimeToFreq(at);
                    for (int k = 0; k < C; k++)
                    {
                        double x = (d.Omega[k] - 2 * w0) / g.DOmega + n / 2; int i0 = (int)Math.Floor(x); double u = x - i0;
                        if (i0 < 0 || i0 >= n - 1) continue;
                        double v0 = sf[i0].Real * sf[i0].Real + sf[i0].Imaginary * sf[i0].Imaginary, v1 = sf[i0 + 1].Real * sf[i0 + 1].Real + sf[i0 + 1].Imaginary * sf[i0 + 1].Imaginary;
                        t[r * C + k] = v0 * (1 - u) + v1 * u;
                    }
                }
                // per-column calibration mu_k (least squares), so the spectral response of the detection drops out
                for (int k = 0; k < C; k++)
                {
                    double num = 0, den = 0;
                    for (int r = 0; r < R; r++) { num += meas[r * C + k] * t[r * C + k]; den += t[r * C + k] * t[r * C + k]; }
                    double mu = den > 0 ? num / den : 0;
                    for (int r = 0; r < R; r++) t[r * C + k] *= mu;
                }
                return t;
            }
            double Cost(double[] p) { var m = Model(p); double e = 0; for (int i = 0; i < m.Length; i++) { double dd = m[i] - meas[i]; e += dd * dd; } return e; }
            // start: best quadratic phase (coarse scan of GDD), expressed on the nodes
            double bestQ = 0, bestC = double.PositiveInfinity;
            double rowSpan = Math.Abs(d.Rows[R - 1] - d.Rows[0]), gddUnit = 0;
            {
                double h = 1e-3 * (whi - wlo);
                int ic = n / 2; gddUnit = unit[ic + 1] + unit[ic - 1] - 2 * unit[ic]; gddUnit /= g.DOmega * g.DOmega;   // d2(unit)/dw2 at the carrier
            }
            for (int q = -20; q <= 20; q++)
            {
                double gd = q / 20.0 * rowSpan * gddUnit;
                var p = nodeW.Select(w => 0.5 * gd * (w - w0) * (w - w0)).ToArray();
                double cst = Cost(p);
                if (cst < bestC) { bestC = cst; bestQ = gd; }
            }
            var par = nodeW.Select(w => 0.5 * bestQ * (w - w0) * (w - w0)).ToArray();
            // Levenberg-Marquardt with a finite-difference Jacobian
            double lambda = 1e-2; var hist = new List<double>();
            var res0 = Residual(Model(par), meas);
            int it;
            for (it = 0; it < maxIt; it++)
            {
                int P = par.Length, M = res0.Length;
                var J = new double[M, P];
                for (int j = 0; j < P; j++)
                {
                    var pp = (double[])par.Clone(); pp[j] += 1e-3;
                    var rj = Residual(Model(pp), meas);
                    for (int i = 0; i < M; i++) J[i, j] = (rj[i] - res0[i]) / 1e-3;
                }
                var JtJ = new double[P, P]; var Jtr = new double[P];
                for (int a = 0; a < P; a++)
                {
                    for (int i = 0; i < M; i++) Jtr[a] += J[i, a] * res0[i];
                    for (int b = a; b < P; b++) { double sum = 0; for (int i = 0; i < M; i++) sum += J[i, a] * J[i, b]; JtJ[a, b] = JtJ[b, a] = sum; }
                }
                double c0 = res0.Sum(x => x * x);
                bool improved = false;
                for (int tries = 0; tries < 8 && !improved; tries++)
                {
                    var A = (double[,])JtJ.Clone();
                    for (int a = 0; a < P; a++) A[a, a] *= 1 + lambda;
                    var step = SolveSym(A, Jtr.Select(x => -x).ToArray());
                    if (step == null) { lambda *= 10; continue; }
                    var pn = par.Select((v, j) => v + step[j]).ToArray();
                    var rn = Residual(Model(pn), meas);
                    double cn = rn.Sum(x => x * x);
                    if (cn < c0) { par = pn; res0 = rn; lambda = Math.Max(1e-6, lambda / 3); improved = true; hist.Add(Math.Sqrt(cn / M)); }
                    else lambda *= 10;
                }
                if (!improved || (hist.Count > 2 && Math.Abs(hist[hist.Count - 2] - hist[hist.Count - 1]) < 1e-6 * hist[hist.Count - 1])) { it++; break; }
            }
            var phase = PhaseOf(par);
            var spec = new Complex[n];
            for (int i = 0; i < n; i++) spec[i] = Complex.FromPolarCoordinates(amp[i], phase[i]);
            var field = SpectralTransform.FreqToTime(spec);
            var r0 = new RetrievalResult
            {
                Method = Name, Time = (double[])g.Time.Clone(), Field = field, Omega0 = w0, Omega = (double[])g.OmegaAbsolute.Clone(), Spectrum = spec,
                Error = Math.Sqrt(res0.Sum(x => x * x) / res0.Length), Iterations = it, ErrorHistory = hist.ToArray(), RetrievedTrace = Model(par)
            };
            r0.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0} phase nodes over {1:G4}-{2:G4} nm; rms residual {3:G3}", nodes, 2 * Math.PI * c / whi * 1e9, 2 * Math.PI * c / wlo * 1e9, r0.Error));
            return r0;
        }

        private static double[] Residual(double[] model, double[] meas) { var r = new double[model.Length]; for (int i = 0; i < r.Length; i++) r[i] = model[i] - meas[i]; return r; }

        private static double[] SolveSym(double[,] A, double[] b)
        {
            int n = b.Length; var M = (double[,])A.Clone(); var x = (double[])b.Clone();
            for (int k = 0; k < n; k++)
            {
                int piv = k; for (int i = k + 1; i < n; i++) if (Math.Abs(M[i, k]) > Math.Abs(M[piv, k])) piv = i;
                if (Math.Abs(M[piv, k]) < 1e-300) return null;
                if (piv != k) { for (int j = 0; j < n; j++) { var t = M[k, j]; M[k, j] = M[piv, j]; M[piv, j] = t; } var tb = x[k]; x[k] = x[piv]; x[piv] = tb; }
                for (int i = k + 1; i < n; i++) { double f = M[i, k] / M[k, k]; for (int j = k; j < n; j++) M[i, j] -= f * M[k, j]; x[i] -= f * x[k]; }
            }
            for (int i = n - 1; i >= 0; i--) { double s = x[i]; for (int j = i + 1; j < n; j++) s -= M[i, j] * x[j]; x[i] = s / M[i, i]; }
            return x;
        }
    }
}
