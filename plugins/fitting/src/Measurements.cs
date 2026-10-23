using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UltrafastEngine.Instruments;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Plugins.Fitting
{
    /// <summary>
    /// Recorded data for an instrument: columns from a CSV file (header row with column names; '#' comments;
    /// comma, semicolon, tab or whitespace separated) or arrays from a NumPy .npz file. Units come from the column
    /// name suffix (wavelength_nm, delay_fs, time_ps, energy_uj, gdd_fs2, ...) or from "xUnit"/"yUnit".
    /// </summary>
    public sealed class MeasuredData
    {
        public string Path;
        public Dictionary<string, double[]> Columns = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int[]> Shapes = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        public List<string> Order = new List<string>();

        public static MeasuredData Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("measured data file not found: " + path);
            var d = new MeasuredData { Path = path };
            if (path.EndsWith(".npz", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var a in NpzWriter.Read(path)) { d.Columns[a.Name] = a.Data; d.Shapes[a.Name] = a.Shape; d.Order.Add(a.Name); }
                return d;
            }
            var lines = File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            if (lines.Count == 0) throw new FormatException(path + ": no data");
            char[] seps = lines[0].Contains(',') ? new[] { ',' } : lines[0].Contains(';') ? new[] { ';' } : lines[0].Contains('\t') ? new[] { '\t' } : new[] { ' ' };
            var first = lines[0].Split(seps, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().Trim('"')).ToArray();
            bool header = first.Any(x => !double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
            var names = header ? first : Enumerable.Range(0, first.Length).Select(i => "col" + i).ToArray();
            var cols = names.Select(_ => new List<double>()).ToArray();
            foreach (var l in lines.Skip(header ? 1 : 0))
            {
                var parts = l.Split(seps, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < names.Length; i++)
                    cols[i].Add(i < parts.Length && double.TryParse(parts[i].Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN);
            }
            for (int i = 0; i < names.Length; i++) { d.Columns[names[i]] = cols[i].ToArray(); d.Shapes[names[i]] = new[] { cols[i].Count }; d.Order.Add(names[i]); }
            return d;
        }

        /// <summary>The first column whose name starts with one of the stems (e.g. "wavelength" matches wavelength_nm), or null.</summary>
        public string Find(params string[] stems)
        {
            foreach (var s in stems)
                foreach (var n in Order)
                    if (string.Equals(n, s, StringComparison.OrdinalIgnoreCase) || n.StartsWith(s + "_", StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }

        /// <summary>SI multiplier from a column-name suffix (or an explicit unit string).</summary>
        public static double Unit(string nameOrUnit)
        {
            if (string.IsNullOrEmpty(nameOrUnit)) return 1;
            string u = nameOrUnit.Contains("_") ? nameOrUnit.Substring(nameOrUnit.LastIndexOf('_') + 1) : nameOrUnit;
            switch (u.ToLowerInvariant())
            {
                case "nm": return 1e-9; case "um": case "µm": return 1e-6; case "mm": return 1e-3; case "cm": return 1e-2;
                case "fs": return 1e-15; case "ps": return 1e-12; case "ns": return 1e-9; case "us": return 1e-6; case "ms": return 1e-3;
                case "fs2": return 1e-30; case "ps2": return 1e-24; case "fs3": return 1e-45;
                case "uj": return 1e-6; case "mj": return 1e-3; case "nj": return 1e-9; case "pj": return 1e-12;
                case "mw": return 1e-3; case "uw": return 1e-6; case "kw": return 1e3;
                case "thz": return 1e12;
                default: return 1;
            }
        }
    }

    /// <summary>A measured instrument compared with its simulation.</summary>
    public sealed class Comparison
    {
        public string Instrument, Kind, Location, Mode, File, Normalization, RowAxis, ColAxis, XAxis;
        /// <summary>Resolved path of the data file (for provenance hashes), or null.</summary>
        public string FullPath;
        public double[] Residuals = new double[0];      // (simulated - measured) / sigma
        public double Chi2, RmsError = double.NaN;      // RmsError: rms(sim - meas) / peak(meas) on the compared curve or trace
        public int Points;
        public List<(string Name, double Measured, double Simulated, double Sigma)> Values = new List<(string, double, double, double)>();
        public List<string> Notes = new List<string>();

        public JsonObject ToJson()
        {
            var o = new JsonObject().Set("instrument", Instrument).Set("kind", Kind).Set("at", Location).Set("mode", Mode).Set("points", Points)
                .Set("chi2", Chi2).Set("reducedChi2", Points > 0 ? Chi2 / Points : double.NaN);
            if (!double.IsNaN(RmsError)) o.Set("rmsError", RmsError);
            if (File != null) o.Set("file", File);
            if (Normalization != null) o.Set("normalization", Normalization);
            if (XAxis != null) o.Set("xAxis", XAxis);
            if (RowAxis != null) o.Set("rowAxis", RowAxis).Set("colAxis", ColAxis);
            if (Values.Count > 0)
            {
                var vo = new JsonObject();
                foreach (var v in Values) vo.Set(v.Name, new JsonObject().Set("measured", v.Measured).Set("simulated", v.Simulated).Set("sigma", v.Sigma).Set("deviation", v.Simulated - v.Measured));
                o.Set("values", vo);
            }
            if (Notes.Count > 0) o.Set("notes", JsonValue.FromArray(Notes));
            return o;
        }
    }

    /// <summary>
    /// Compares an instrument's simulated reading with recorded data ("measured" in the instrument spec,
    /// docs/plugins/fitting.md). Curves (spectrometer, autocorrelator, scope, beam profile, delay scan, phase-matching
    /// curve) and traces (FROG, d-scan, cameras) are interpolated onto the measured axes, normalised (peak, area,
    /// a free scale, or absolute) and compared point by point; scalar values are compared directly.
    /// </summary>
    public static class Measurements
    {
        private sealed class Layout { public string X, Y, Rows, Cols, Trace; public string[] MX, MY, MRows, MCols, MTrace; }

        private static Layout For(string kind, InstrumentOutput o)
        {
            bool has(string s) => o.Arrays.Any(a => a.Name.EndsWith("_" + s));
            switch (kind)
            {
                case "spectrometer": return new Layout { X = "wavelength_m", Y = "spectrum", MX = new[] { "wavelength", "lambda", "wl", "x" }, MY = new[] { "spectrum", "intensity", "counts", "signal", "y" } };
                case "crossCorrelator": case "thirdOrderCorrelator": return new Layout { X = "delay_s", Y = "signal", MX = new[] { "delay", "tau", "x" }, MY = new[] { "signal", "intensity", "y" } };
                case "autocorrelator": return new Layout { X = "delay_s", Y = "signal", MX = new[] { "delay", "tau", "x" }, MY = new[] { "signal", "ac", "intensity", "y" } };
                case "scope": return new Layout { X = "time_s", Y = "signal_w", MX = new[] { "time", "t", "x" }, MY = new[] { "signal", "power", "voltage", "y" } };
                case "beamProfile": return has("radius_m") ? new Layout { X = "radius_m", Y = "fluence_j_m2", MX = new[] { "radius", "r", "x" }, MY = new[] { "fluence", "intensity", "signal", "y" } }
                                                         : new Layout { X = "x_m", Y = "fluence_x_j_m2", MX = new[] { "x", "position" }, MY = new[] { "fluence", "intensity", "signal", "y" } };
                case "delayScan": return new Layout { X = "delay_s", Y = "output_energy_j", MX = new[] { "delay", "x" }, MY = new[] { "energy", "signal", "y" } };
                case "phaseMatchingCurve":
                {
                    var xa = o.Arrays.Select(a => a.Name).FirstOrDefault(n => n.EndsWith("_detuning_deg") || n.EndsWith("_temperature_c") || n.EndsWith("_period_m") || n.EndsWith("_wavelength_m"));
                    string xs = xa == null ? "wavelength_m" : xa.Substring(RunResultName(o).Length + 1);
                    return new Layout { X = xs, Y = "efficiency_norm", MX = new[] { "detuning", "temperature", "period", "wavelength", "x" }, MY = new[] { "efficiency", "signal", "y" } };
                }
                case "frog": return new Layout { Rows = "delay_s", Cols = "wavelength_m", Trace = "trace", MRows = new[] { "delay", "tau" }, MCols = new[] { "wavelength", "lambda" }, MTrace = new[] { "trace", "frog" } };
                case "dscan": return new Layout { Rows = has("insertion_m") ? "insertion_m" : "gdd_s2", Cols = "shg_wavelength_m", Trace = "trace", MRows = has("insertion_m") ? new[] { "insertion", "glass", "z" } : new[] { "gdd" }, MCols = new[] { "wavelength", "shg_wavelength", "lambda" }, MTrace = new[] { "trace", "dscan" } };
                case "nearFieldCamera": case "camera": case "farFieldCamera":
                    return new Layout { Rows = "x_m", Cols = "y_m", Trace = "fluence_j_m2", MRows = new[] { "x" }, MCols = new[] { "y" }, MTrace = new[] { "image", "fluence", "counts" } };
                default: return null;
            }
        }

        private static string RunResultName(InstrumentOutput o) => Results.RunResult.Sanitize(o.Name);

        private static double[] Arr(InstrumentOutput o, string suffix)
        {
            var a = o.Arrays.FirstOrDefault(x => x.Name == RunResultName(o) + "_" + suffix);
            return a?.Data;
        }

        /// <summary>The recording's stated resolution ("resolutionM" / "resolutionS" in measured) against the simulated
        /// instrument's: a mismatch biases every fit, so it is reported.</summary>
        private static void CheckResolution(InstrumentOutput o, JsonObject s, Comparison c)
        {
            string key = s.Has("resolutionM") ? "resolutionM" : s.Has("resolutionS") ? "resolutionS" : null;
            if (key == null) return;
            double rec = s.GetDouble(key, 0);
            var st = o.Settings ?? new JsonObject();
            string[] candidates = key == "resolutionM" ? new[] { "resolutionM", "spectralResolutionM" } : new[] { "resolutionS", "riseTimeS" };
            double sim = double.NaN; string simKey = null;
            foreach (var k in candidates) if (st.TryGet(k, out var v) && v.Kind == JsonKind.Number) { sim = v.AsDouble(); simKey = k; break; }
            var ax = st.GetObject("axis");
            if (ax != null && ax.Has("resolution")) { double a = ax.GetDouble("resolution", 0); sim = double.IsNaN(sim) ? a : Math.Sqrt(sim * sim + a * a); simKey = (simKey ?? "") + "+axis.resolution"; }
            if (double.IsNaN(sim)) sim = 0;
            bool mismatch = rec > 0 ? Math.Abs(sim - rec) > 0.1 * rec : sim > 0;
            var ci = CultureInfo.InvariantCulture;
            if (mismatch)
                c.Notes.Add(string.Format(ci, "the recording's resolution is {0:G3} but the simulated instrument uses {1:G3} ({2}): set the instrument's {3} to the recording's (or fit it: instrument:{4}/{3})", rec, sim, simKey ?? "ideal", candidates[0], o.Name));
            else c.Notes.Add(string.Format(ci, "resolution {0:G3} matches the recording", rec));
        }

        public static Comparison Compare(InstrumentOutput o, JsonValue spec, string baseDir)
        {
            var c = new Comparison { Instrument = o.Name, Kind = o.Kind, Location = o.Location };
            JsonObject s = spec.Kind == JsonKind.String ? new JsonObject().Set("file", spec.AsString()) : spec.AsObject();
            string kind = o.Kind;
            double relSigma = s.GetDouble("sigma", 0.02);
            string norm = s.GetString("normalize", "peak").ToLowerInvariant();
            c.Normalization = norm;
            var res = new List<double>();
            string file = s.GetString("file");
            var vals = s.GetObject("values");
            if (file != null && file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                // a JSON file of scalar readings: {"energyJ": 1.2e-6, "fwhmS": [35e-15, 2e-15]}
                string jp = System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(baseDir ?? ".", file);
                var fv = JsonValue.ParseFile(jp).AsObject();
                c.FullPath = jp;
                if (vals == null) vals = fv; else foreach (var kv in fv) if (!vals.Has(kv.Key)) vals.Set(kv.Key, kv.Value);
                c.File = file; file = null;
            }
            if (file != null)
            {
                string path = System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(baseDir ?? ".", file);
                c.File = file; c.FullPath = path;
                var md = MeasuredData.Load(path);
                var L = For(kind, o);
                var sel = s.GetObject("columns") ?? new JsonObject();
                if (s.Has("simulated")) { var so = s.GetObject("simulated"); L = new Layout { X = so.GetString("x"), Y = so.GetString("y"), Rows = so.GetString("rows"), Cols = so.GetString("cols"), Trace = so.GetString("trace"), MX = new[] { "x" }, MY = new[] { "y" }, MRows = new[] { "rows" }, MCols = new[] { "cols" }, MTrace = new[] { "trace" } }; }
                if (L == null) throw new JsonException("instrument '" + o.Name + "' (" + kind + "): no curve to compare a file with -- give \"measured\": {\"values\": {...}} or \"simulated\": {\"x\": ..., \"y\": ...}");
                if (L.Trace == null) CompareCurve(o, c, md, L, sel, s, norm, relSigma, res);
                else CompareTrace(o, c, md, L, sel, s, norm, relSigma, res);
            }
            if (vals != null)
                foreach (var kv in vals)
                {
                    double mv, sg;
                    if (kv.Value.Kind == JsonKind.Array) { var a = kv.Value.AsDoubleArray(); mv = a[0]; sg = a.Length > 1 ? a[1] : 0.02 * Math.Abs(mv); }
                    else { mv = kv.Value.AsDouble(); sg = s.GetDouble("valueSigma", 0.02) * Math.Abs(mv); }
                    if (!(sg > 0)) sg = 1e-30 + 0.02 * Math.Abs(mv);
                    double sv = o.Values.GetDouble(kv.Key, double.NaN);
                    if (double.IsNaN(sv)) { c.Notes.Add("value \"" + kv.Key + "\" is not reported by this instrument (known: " + string.Join(", ", o.Values.Select(x => x.Key)) + ")"); continue; }
                    c.Values.Add((kv.Key, mv, sv, sg));
                    res.Add((sv - mv) / sg * Math.Sqrt(s.GetDouble("valueWeight", 1.0)));
                    o.Values.Set("measured." + kv.Key, mv);
                }
            CheckResolution(o, s, c);
            double w = Math.Sqrt(Math.Max(0, s.GetDouble("weight", 1.0)));
            c.Residuals = res.Select(r => r * w).ToArray();
            c.Points = c.Residuals.Length;
            c.Chi2 = c.Residuals.Sum(r => r * r);
            c.Mode = file == null || (c.File != null && c.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ? "values" : (For(kind, o)?.Trace != null && !s.Has("simulated") ? "trace" : "curve");
            if (!double.IsNaN(c.RmsError)) o.Values.Set("measuredRmsError", c.RmsError);
            o.Values.Set("measuredChi2", c.Chi2);
            foreach (var n in c.Notes) o.Notes.Add("measured: " + n);
            return c;
        }

        private static void CompareCurve(InstrumentOutput o, Comparison c, MeasuredData md, Layout L, JsonObject sel, JsonObject s, string norm, double relSigma, List<double> res)
        {
            string xn = sel.GetString("x") ?? md.Find(L.MX) ?? md.Order[0];
            string yn = sel.GetString("y") ?? md.Find(L.MY) ?? md.Order.FirstOrDefault(n => n != xn);
            if (yn == null) throw new FormatException(md.Path + ": needs two columns (x and y)");
            double xu = s.Has("xUnit") ? MeasuredData.Unit(s.GetString("xUnit")) : MeasuredData.Unit(xn);
            if (!s.Has("xUnit") && MeasuredData.Unit(xn) == 1 && L.X.EndsWith("_m") && md.Columns[xn].Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max() > 1e-3 && L.X.Contains("wavelength"))
            { xu = 1e-9; c.Notes.Add("wavelength column without unit: taken as nm"); }
            var mx = md.Columns[xn].Select(v => v * xu).ToArray();
            var my = (double[])md.Columns[yn].Clone();
            bool yDb = string.Equals(s.GetString("yUnit", ""), "dB", StringComparison.OrdinalIgnoreCase) || yn.EndsWith("_db", StringComparison.OrdinalIgnoreCase);
            double yu = s.Has("yUnit") && !yDb ? MeasuredData.Unit(s.GetString("yUnit")) : 1;
            if (yDb) { for (int i = 0; i < my.Length; i++) my[i] = Math.Pow(10, my[i] / 10); c.Notes.Add("data given in dB: converted to linear"); }
            else for (int i = 0; i < my.Length; i++) my[i] *= yu;
            string sn = sel.GetString("sigma") ?? md.Find("sigma", "err", "error", "std");
            double[] msig = sn != null ? md.Columns[sn].Select(v => v * yu).ToArray() : null;
            // order, range, background
            var idx = Enumerable.Range(0, mx.Length).Where(i => !double.IsNaN(mx[i]) && !double.IsNaN(my[i])).OrderBy(i => mx[i]).ToArray();
            var rng = s.GetDoubleArray("rangeX");
            if (rng != null) idx = idx.Where(i => mx[i] >= Math.Min(rng[0], rng[1]) && mx[i] <= Math.Max(rng[0], rng[1])).ToArray();   // rangeX in SI
            mx = idx.Select(i => mx[i]).ToArray(); my = idx.Select(i => my[i]).ToArray(); if (msig != null) msig = idx.Select(i => msig[i]).ToArray();
            if (mx.Length < 3) throw new FormatException(md.Path + ": fewer than 3 usable points");
            Background(my, s, c);
            c.XAxis = L.X;
            var sx = Arr(o, L.X); var sy = Arr(o, L.Y);
            if (sx == null || sy == null) throw new InvalidOperationException("instrument '" + o.Name + "' has no arrays '" + L.X + "' / '" + L.Y + "' to compare with");
            var so = Enumerable.Range(0, sx.Length).OrderBy(i => sx[i]).ToArray();
            var sxs = so.Select(i => sx[i]).ToArray(); var sys = so.Select(i => sy[i]).ToArray();
            var sim = mx.Select(x => Interp0(sxs, sys, x)).ToArray();
            double scale = Normalise(sim, my, mx, norm, c, msig);
            double peak = my.Max(Math.Abs);
            double se = 0;
            string sc = s.GetString("scale", "linear").ToLowerInvariant();
            if (sc == "log" || sc == "db")
            {
                // high-dynamic-range data (contrast, cross-correlators, spectral wings): compare in dB, so a -90 dB
                // pedestal weighs as much as the peak; values below the floor are clipped to it on both sides
                var pos = my.Where(v => v > 0).ToArray();
                double floorDb = s.GetDouble("floorDb", pos.Length > 0 ? 10 * Math.Log10(pos.Min() / Math.Max(1e-300, peak)) : -100);
                double floor = peak * Math.Pow(10, floorDb / 10), sdb = s.GetDouble("sigmaDb", 1.0), sedb = 0;
                for (int i = 0; i < mx.Length; i++)
                {
                    double d = 10 * Math.Log10(Math.Max(sim[i], floor) / Math.Max(my[i], floor));
                    res.Add(d / sdb); sedb += d * d;
                    se += (sim[i] - my[i]) * (sim[i] - my[i]);
                }
                o.Values.Set("measuredRmsErrorDb", Math.Sqrt(sedb / mx.Length));
                c.Notes.Add(string.Format(CultureInfo.InvariantCulture, "compared in dB (sigma {0:G3} dB, floor {1:G3} dB)", sdb, floorDb));
            }
            else
                for (int i = 0; i < mx.Length; i++)
                {
                    double sig = msig != null ? msig[i] : relSigma * peak;
                    if (!(sig > 0)) sig = relSigma * Math.Max(1e-300, peak);
                    res.Add((sim[i] - my[i]) / sig);
                    se += (sim[i] - my[i]) * (sim[i] - my[i]);
                }
            c.RmsError = Math.Sqrt(se / mx.Length) / Math.Max(1e-300, peak);
            o.Add("measured_x", mx, "", "measured axis (SI)");
            o.Add("measured_y", my, "", "measured data (" + norm + ")");
            o.Add("simulated_on_measured", sim, "", "simulated reading on the measured axis (" + norm + ")");
            if (msig != null) o.Add("measured_sigma", msig, "", "measurement uncertainty");
            if (!double.IsNaN(scale)) o.Values.Set("measuredScale", scale);
        }

        private static void CompareTrace(InstrumentOutput o, Comparison c, MeasuredData md, Layout L, JsonObject sel, JsonObject s, string norm, double relSigma, List<double> res)
        {
            string rn = sel.GetString("rows") ?? md.Find(L.MRows), cn = sel.GetString("cols") ?? md.Find(L.MCols), tn = sel.GetString("trace") ?? md.Find(L.MTrace);
            if (rn == null || cn == null || tn == null) throw new FormatException(md.Path + ": a 2-D trace needs arrays for the rows (" + string.Join("/", L.MRows) + "), the columns (" + string.Join("/", L.MCols) + ") and the trace (" + string.Join("/", L.MTrace) + ") -- or give \"columns\": {\"rows\", \"cols\", \"trace\"}");
            double ru = s.Has("rowUnit") ? MeasuredData.Unit(s.GetString("rowUnit")) : MeasuredData.Unit(rn), cu = s.Has("colUnit") ? MeasuredData.Unit(s.GetString("colUnit")) : MeasuredData.Unit(cn);
            if (L.Cols.Contains("wavelength") && cu == 1 && md.Columns[cn].Max() > 1e-3) { cu = 1e-9; c.Notes.Add("wavelength axis without unit: taken as nm"); }
            var mr = md.Columns[rn].Select(v => v * ru).ToArray(); var mc = md.Columns[cn].Select(v => v * cu).ToArray();
            var mt = md.Columns[tn];
            if (mt.Length != mr.Length * mc.Length) throw new FormatException(md.Path + ": trace has " + mt.Length + " values, rows x cols = " + mr.Length + " x " + mc.Length);
            var tr = (double[])mt.Clone();
            Background(tr, s, c, mc.Length);
            c.RowAxis = L.Rows; c.ColAxis = L.Cols;
            var sr = Arr(o, L.Rows); var sc = Arr(o, L.Cols); var st = Arr(o, L.Trace);
            if (sr == null || sc == null || st == null) throw new InvalidOperationException("instrument '" + o.Name + "' has no trace to compare with");
            int nr = sr.Length, nc = sc.Length;
            var sim = new double[mr.Length * mc.Length];
            for (int i = 0; i < mr.Length; i++)
                for (int j = 0; j < mc.Length; j++)
                    sim[i * mc.Length + j] = Bilinear(sr, sc, st, mr[i], mc[j]);
            double scale = Normalise(sim, tr, null, norm, c, null);
            double peak = tr.Max(Math.Abs), se = 0;
            for (int k = 0; k < tr.Length; k++) { res.Add((sim[k] - tr[k]) / (relSigma * Math.Max(1e-300, peak))); se += (sim[k] - tr[k]) * (sim[k] - tr[k]); }
            c.RmsError = Math.Sqrt(se / tr.Length) / Math.Max(1e-300, peak);   // the FROG "G" error for peak-normalised traces
            o.Add("measured_rows", mr, "", "measured trace rows (SI)");
            o.Add("measured_cols", mc, "", "measured trace columns (SI)");
            o.Add("measured_trace", tr, "", "measured trace [rows, cols] (" + norm + ")", mr.Length, mc.Length);
            o.Add("simulated_on_measured", sim, "", "simulated trace on the measured axes", mr.Length, mc.Length);
            if (!double.IsNaN(scale)) o.Values.Set("measuredScale", scale);
        }

        /// <summary>Subtracts a background: a number, or "auto" = the median of the data at the edges of the measured
        /// axis (the first and last 5 % of the points, or of the columns of a trace), where a recording is normally
        /// dark. Unlike a low percentile, the median of a noisy dark level is not biased by the noise.</summary>
        private static void Background(double[] y, JsonObject s, Comparison c, int cols = 0)
        {
            if (!s.Has("background")) return;
            var b = s["background"];
            double v;
            if (b.Kind == JsonKind.String && b.AsString().Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                var edge = new List<double>();
                if (cols > 0)
                {
                    int rows = y.Length / cols, k = Math.Max(2, cols / 20);
                    for (int r = 0; r < rows; r++) for (int j = 0; j < k; j++) { edge.Add(y[r * cols + j]); edge.Add(y[r * cols + cols - 1 - j]); }
                }
                else
                {
                    int k = Math.Max(3, y.Length / 20);
                    for (int j = 0; j < k && j < y.Length; j++) { edge.Add(y[j]); edge.Add(y[y.Length - 1 - j]); }
                }
                var sorted = edge.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToArray();
                v = sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
                c.Notes.Add(string.Format(CultureInfo.InvariantCulture, "background {0:G3} subtracted (median at the edges of the axis)", v));
            }
            else v = b.AsDouble();
            for (int i = 0; i < y.Length; i++) y[i] -= v;
        }

        /// <summary>Normalise in place: peak (simulation to 1, data scaled onto it by least squares), maximum (both divided by their maximum), area (both to unit integral), scale (simulated x best-fit factor), none.</summary>
        private static double Normalise(double[] sim, double[] meas, double[] x, string norm, Comparison c, double[] sig)
        {
            switch (norm)
            {
                case "none": case "absolute": return double.NaN;
                case "scale":
                {
                    double num = 0, den = 0;
                    for (int i = 0; i < sim.Length; i++) { double w = sig != null && sig[i] > 0 ? 1 / (sig[i] * sig[i]) : 1; num += w * sim[i] * meas[i]; den += w * sim[i] * sim[i]; }
                    double k = den > 0 ? num / den : 1;
                    for (int i = 0; i < sim.Length; i++) sim[i] *= k;
                    return k;
                }
                case "area":
                {
                    double a = Area(x, sim), b = Area(x, meas);
                    if (a > 0) for (int i = 0; i < sim.Length; i++) sim[i] /= a;
                    if (b > 0) for (int i = 0; i < meas.Length; i++) meas[i] /= b;
                    return double.NaN;
                }
                case "maximum":
                {
                    double a = sim.Max(), b = meas.Max();
                    if (a > 0) for (int i = 0; i < sim.Length; i++) sim[i] /= a;
                    if (b > 0) for (int i = 0; i < meas.Length; i++) meas[i] /= b;
                    if (!(a > 0)) c.Notes.Add("the simulated reading is zero on the measured axis");
                    return double.NaN;
                }
                default:
                {
                    // "peak": the simulation is normalised to its maximum, and the data are scaled onto it by least
                    // squares -- unlike dividing by the measured maximum, this is not biased by noise on the peak
                    double a = sim.Max();
                    if (!(a > 0)) { c.Notes.Add("the simulated reading is zero on the measured axis"); double m0 = meas.Max(); if (m0 > 0) for (int i = 0; i < meas.Length; i++) meas[i] /= m0; return double.NaN; }
                    for (int i = 0; i < sim.Length; i++) sim[i] /= a;
                    double num = 0, den = 0;
                    for (int i = 0; i < sim.Length; i++) { double w = sig != null && sig[i] > 0 ? 1 / (sig[i] * sig[i]) : 1; num += w * sim[i] * meas[i]; den += w * sim[i] * sim[i]; }
                    double b = num > 0 && den > 0 ? num / den : meas.Max();   // meas ~ b * sim (unbiased by noise on meas)
                    if (b > 0) for (int i = 0; i < meas.Length; i++) meas[i] /= b;
                    return double.NaN;
                }
            }
        }

        private static double Area(double[] x, double[] y)
        {
            if (x == null) return y.Sum();
            double s = 0; for (int i = 1; i < x.Length; i++) s += 0.5 * (y[i] + y[i - 1]) * (x[i] - x[i - 1]); return s;
        }

        internal static double Interp0(double[] x, double[] y, double v)
        {
            int n = x.Length; if (n == 0 || v < x[0] || v > x[n - 1]) return 0;
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (x[m] <= v) lo = m; else hi = m; }
            double t = x[hi] == x[lo] ? 0 : (v - x[lo]) / (x[hi] - x[lo]);
            return y[lo] * (1 - t) + y[hi] * t;
        }

        /// <summary>Bilinear interpolation of t[rows, cols] (axes in any monotonic order); zero outside.</summary>
        private static double Bilinear(double[] r, double[] cc, double[] t, double x, double y)
        {
            int nr = r.Length, nc = cc.Length;
            (int, double) Loc(double[] a, double v)
            {
                bool asc = a[a.Length - 1] >= a[0];
                double lo = asc ? a[0] : a[a.Length - 1], hi = asc ? a[a.Length - 1] : a[0];
                if (v < lo || v > hi || double.IsNaN(v)) return (-1, 0);
                int i0 = 0;
                for (int i = 0; i < a.Length - 1; i++) { double a0 = a[i], a1 = a[i + 1]; if ((v >= Math.Min(a0, a1)) && (v <= Math.Max(a0, a1))) { i0 = i; break; } }
                double d = a[i0 + 1] - a[i0];
                return (i0, d == 0 ? 0 : (v - a[i0]) / d);
            }
            var (i, u) = Loc(r, x); var (j, w) = Loc(cc, y);
            if (i < 0 || j < 0) return 0;
            double v00 = t[i * nc + j], v10 = t[(i + 1) * nc + j], v01 = t[i * nc + j + 1], v11 = t[(i + 1) * nc + j + 1];
            return v00 * (1 - u) * (1 - w) + v10 * u * (1 - w) + v01 * (1 - u) * w + v11 * u * w;
        }
    }
}
