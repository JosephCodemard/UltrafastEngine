using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltrafastEngine.Engine;
using UltrafastEngine.Instruments;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Results
{
    /// <summary>
    /// Everything a run produced: input/output pulses, per-element records, probe snapshots, instrument
    /// outputs, provenance, notes and warnings. <see cref="SummaryJson"/> is the machine-readable summary;
    /// <see cref="Arrays"/> lists every numerical array; <see cref="WriteAll"/> writes summary.json,
    /// result.npz, provenance.json and CSV files.
    /// </summary>
    public sealed class RunResult
    {
        public CompiledRun Compiled { get; set; }
        public PulseField Input { get; set; }
        public PulseField Output { get; set; }
        public List<ElementRecord> Records { get; } = new List<ElementRecord>();
        public List<ProbeSnapshot> Snapshots { get; } = new List<ProbeSnapshot>();
        /// <summary>Full pulses kept at "input", "end", and every element id (references, not copies).</summary>
        public Dictionary<string, PulseField> At { get; } = new Dictionary<string, PulseField>(StringComparer.OrdinalIgnoreCase);
        public List<InstrumentOutput> Instruments { get; } = new List<InstrumentOutput>();
        /// <summary>Instruments compared with recorded data ("measured" in the instrument spec): residuals, chi^2,
        /// rms error, scalar readings. docs/reference/measured-data.md.</summary>
        public List<Measured.Comparison> Comparisons { get; } = new List<Measured.Comparison>();
        /// <summary>Data that plugins attach to the result (e.g. the fitting plugin's comparisons and fit report),
        /// by key. Plugins add their summary sections and files through <see cref="Plugins.IResultExtension"/>.</summary>
        public Dictionary<string, object> Extensions { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public List<string> Notes { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public double ElapsedS { get; set; }
        public double FinalArrivalS { get; set; }
        public DispersionPoint FinalSystem { get; set; }

        private static double R(double v) => double.IsNaN(v) || double.IsInfinity(v) ? double.NaN : v;

        private static JsonObject GridJson(FrequencyGrid g) => new JsonObject().Set("n", g.N).Set("dtS", g.DTime).Set("windowS", g.N * g.DTime).Set("omega0", g.OmegaCenter)
            .Set("centerWavelengthM", 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / g.OmegaCenter).Set("domega", g.DOmega);

        private static JsonObject SummaryOf(PulseSummary s) => new JsonObject()
            .Set("energyJ", R(s.EnergyJ)).Set("peakPowerW", R(s.PeakPowerW)).Set("fwhmS", R(s.FwhmS)).Set("rmsWidthS", R(s.RmsWidthS))
            .Set("transformLimitFwhmS", R(s.TransformLimitFwhmS)).Set("temporalStrehl", R(s.TemporalStrehl))
            .Set("spectralFwhmM", R(s.SpectralFwhmM)).Set("spectralRmsWidthM", R(s.SpectralRmsWidthM)).Set("centroidWavelengthM", R(s.CentroidWavelengthM)).Set("peakWavelengthM", R(s.PeakWavelengthM))
            .Set("timeBandwidthProduct", R(s.TimeBandwidthProduct)).Set("pulseGddS2", R(s.PulseGddS2)).Set("pulseTodS3", R(s.PulseTodS3))
            .Set("centroidTimeS", R(s.CentroidTimeS)).Set("edgeEnergyFraction", R(s.EdgeEnergyFraction))
            .Set("stokes", JsonValue.FromArray(new[] { R(s.S1), R(s.S2), R(s.S3) })).Set("degreeOfPolarization", R(s.PolarizationDegree));

        public JsonObject SummaryJson()
        {
            var cr = Compiled;
            var g = cr.Grid;
            var inS = PulseMetrics.Summarize(Input);
            var outS = PulseMetrics.Summarize(Output);
            var o = new JsonObject()
                .Set("format", "ultrafast-result").Set("version", 1)
                .Set("engine", new JsonObject().Set("name", "UltrafastEngine").Set("version", EngineInfo.Version).Set("fft", Fft.Provider.Name))
                .Set("system", cr.System.Name).Set("run", cr.Run.Name)
                .Set("elapsedS", ElapsedS)
                .Set("grid", GridJson(g))
                .Set("outputGrid", GridJson(Output.Grid))
                .Set("input", SummaryOf(inS))
                .Set("output", SummaryOf(outS))
                .Set("totals", new JsonObject()
                    .Set("energyTransmission", inS.EnergyJ > 0 ? outS.EnergyJ / inS.EnergyJ : double.NaN)
                    .Set("arrivalTimeS", FinalArrivalS)
                    .Set("systemGroupDelayS", FinalSystem.GroupDelayS).Set("systemGddS2", FinalSystem.GddS2).Set("systemTodS3", FinalSystem.TodS3)
                    .Set("bIntegralRad", Records.Sum(r => r.BIntegralRad))
                    .Set("lengthM", Records.Sum(r => r.LengthM)));
            // loss budget by mechanism
            var mech = new JsonObject();
            foreach (var m in Records.SelectMany(r => r.LossJ.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            {
                double j = Records.Sum(r => r.LossJ.TryGetValue(m, out var v) ? v : 0);
                mech.Set(m, new JsonObject().Set("energyJ", j).Set("fractionOfInput", inS.EnergyJ > 0 ? j / inS.EnergyJ : double.NaN));
            }
            o.Set("lossBudget", mech);
            o.Set("energyBudget", mech); // same table; negative entries are energy GAINED by the main beam (parametric gain, amplification)
            var els = new JsonArray();
            foreach (var r in Records)
            {
                var lj = new JsonObject(); foreach (var kv in r.LossJ) lj.Set(kv.Key, kv.Value);
                var eo = new JsonObject()
                    .Set("index", r.Index).Set("id", r.Id).Set("type", r.Type).Set("label", r.Label)
                    .Set("lengthM", r.LengthM).Set("cumulativeLengthM", r.CumulativeLengthM)
                    .Set("energyInJ", r.EnergyInJ).Set("energyOutJ", r.EnergyOutJ).Set("lossJ", lj)
                    .Set("arrivalTimeS", r.ArrivalS)
                    .Set("own", new JsonObject().Set("groupDelayS", R(r.Own.GroupDelayS)).Set("gddS2", R(r.Own.GddS2)).Set("todS3", R(r.Own.TodS3)))
                    .Set("system", new JsonObject().Set("groupDelayS", R(r.System.GroupDelayS)).Set("gddS2", R(r.System.GddS2)).Set("todS3", R(r.System.TodS3)))
                    .Set("bIntegralRad", r.BIntegralRad).Set("cumulativeBIntegralRad", r.CumulativeBIntegralRad)
                    .Set("peakIntensityWPerM2", R(r.PeakIntensityWPerM2)).Set("peakFluenceJPerM2", R(r.PeakFluenceJPerM2))
                    .Set("lidtFluenceJPerM2", R(r.LidtFluenceJPerM2)).Set("lidtMargin", R(r.LidtMargin))
                    .Set("outsideDataFraction", R(r.OutsideDataFraction)).Set("criticalPowerRatio", R(r.CriticalPowerRatio))
                    .Set("pulse", SummaryOf(r.Out))
                    .Set("terms", JsonValue.FromArray(r.Terms));
                if (r.Steps != null)
                    eo.Set("solver", new JsonObject().Set("method", r.Steps.Method).Set("representation", r.Steps.Representation).Set("accepted", r.Steps.Accepted).Set("rejected", r.Steps.Rejected)
                        .Set("minStepM", R(r.Steps.MinStepM)).Set("maxStepM", R(r.Steps.MaxStepM)));
                if (!double.IsNaN(r.CarrierWavelengthM)) eo.Set("carrierWavelengthM", r.CarrierWavelengthM);
                if (!double.IsNaN(r.BeamRadiusXM)) eo.Set("beam", new JsonObject().Set("radiusXM", R(r.BeamRadiusXM)).Set("radiusYM", R(r.BeamRadiusYM)).Set("m2", R(r.BeamQualityM2)));
                if (r.Representation != null) eo.Set("representation", r.Representation);
                if (r.Values.Count > 0) { var vo = new JsonObject(); foreach (var kv in r.Values) vo.Set(kv.Key, R(kv.Value)); eo.Set("values", vo); }
                if (r.Byproducts.Count > 0)
                {
                    var bo = new JsonObject();
                    foreach (var kv in r.Byproducts) bo.Set(kv.Key, SummaryOf(kv.Value));
                    eo.Set("byproducts", bo);
                }
                if (r.Arrays.Count > 0) eo.Set("arrays", JsonValue.FromArray(r.Arrays.Select(a => a.Name)));
                if (r.Aux.Count > 0)
                {
                    var ao = new JsonObject();
                    foreach (var kv in r.Aux) ao.Set(kv.Key, kv.Value);
                    eo.Set("aux", ao);
                }
                if (r.Notes.Count > 0) eo.Set("notes", JsonValue.FromArray(r.Notes));
                els.Add(eo);
            }
            o.Set("elements", els);
            var ins = new JsonArray();
            foreach (var i in Instruments) ins.Add(i.ToJson());
            o.Set("instruments", ins);
            if (Comparisons.Count > 0)
            {
                o.Set("comparisons", new JsonArray(Comparisons.Select(c => (JsonValue)c.ToJson())));
                o.Set("measuredChi2", Comparisons.Sum(c => c.Chi2));
            }
            foreach (var ext in Plugins.PluginManager.Extensions) ext.ContributeSummary(this, o);
            o.Set("probes", JsonValue.FromArray(Snapshots.Select(s => s.Id)));
            o.Set("arrays", new JsonArray(Arrays().Select(a => (JsonValue)new JsonObject().Set("name", a.Name).Set("shape", JsonValue.FromArray(a.Shape.Select(x => (double)x))).Set("unit", a.Unit).Set("description", a.Description))));
            o.Set("notes", JsonValue.FromArray(Notes));
            o.Set("warnings", JsonValue.FromArray(Warnings));
            return o;
        }

        private static NdArray A(string name, double[] d, string unit, string desc) => new NdArray(name, d) { Unit = unit, Description = desc };

        private List<NdArray> _arrays;

        /// <summary>All numerical outputs (built once, cached).</summary>
        public List<NdArray> Arrays()
        {
            if (_arrays != null) return _arrays;
            var list = new List<NdArray>();
            int max = Compiled.Run.Outputs.MaxPoints;
            void PulseArrays(string prefix, PulseField p)
            {
                var g = p.Grid;
                var pt = PulseMetrics.TemporalPower(p);
                var sp = p.SpectralIntensity();
                var (tl, th) = max > 0 ? PulseMetrics.SupportRange(pt, 1e-9) : (0, g.N - 1);
                var (sl, sh) = max > 0 ? PulseMetrics.SupportRange(sp, 1e-9) : (0, g.N - 1);
                list.Add(A(prefix + "_time_s", Executor.Crop(g.Time, tl, th, max), "s", "co-moving time"));
                list.Add(A(prefix + "_power_w", Executor.CropMax(pt, tl, th, max), "W", "temporal power |Ax|^2+|Ay|^2"));
                var ax = p.TemporalAx(); var ay = p.TemporalAy();
                list.Add(A(prefix + "_power_x_w", Executor.Crop(ax.Select(c => c.Real * c.Real + c.Imaginary * c.Imaginary).ToArray(), tl, th, max), "W", "x-polarised power"));
                list.Add(A(prefix + "_power_y_w", Executor.Crop(ay.Select(c => c.Real * c.Real + c.Imaginary * c.Imaginary).ToArray(), tl, th, max), "W", "y-polarised power"));
                list.Add(A(prefix + "_omega_rad_s", Executor.Crop(g.OmegaAbsolute, sl, sh, max), "rad/s", "absolute angular frequency"));
                var wl = g.WavelengthAxisM();
                list.Add(A(prefix + "_wavelength_m", Executor.Crop(wl, sl, sh, max), "m", "wavelength of each spectral sample"));
                list.Add(A(prefix + "_spectrum", Executor.CropMax(sp, sl, sh, max), "arb.", "spectral power density per unit omega |Ax(w)|^2+|Ay(w)|^2"));
                list.Add(A(prefix + "_phase_x_rad", Executor.Crop(p.SpectralPhaseUnwrappedX(), sl, sh, max), "rad", "unwrapped spectral phase, x"));
                if (max <= 0)
                {
                    list.Add(A(prefix + "_ax_re", p.Ax.Select(c => c.Real).ToArray(), "sqrt(W)", "spectral envelope Ax, real part (grid order)"));
                    list.Add(A(prefix + "_ax_im", p.Ax.Select(c => c.Imaginary).ToArray(), "sqrt(W)", "spectral envelope Ax, imaginary part"));
                    list.Add(A(prefix + "_ay_re", p.Ay.Select(c => c.Real).ToArray(), "sqrt(W)", "spectral envelope Ay, real part"));
                    list.Add(A(prefix + "_ay_im", p.Ay.Select(c => c.Imaginary).ToArray(), "sqrt(W)", "spectral envelope Ay, imaginary part"));
                }
            }
            PulseArrays("input", Input);
            PulseArrays("output", Output);
            // element table
            var cols = ElementColumns();
            var tab = new double[Records.Count * cols.Length];
            for (int i = 0; i < Records.Count; i++) { var row = ElementRow(Records[i]); Array.Copy(row, 0, tab, i * cols.Length, cols.Length); }
            list.Add(new NdArray("elements", tab, Records.Count, cols.Length) { Unit = "mixed", Description = "per-element table; columns: " + string.Join(",", cols) });
            foreach (var s in Snapshots)
            {
                string pf = "probe_" + Sanitize(s.Id);
                list.Add(A(pf + "_time_s", s.TimeS, "s", "probe " + s.Id));
                list.Add(A(pf + "_power_w", s.PowerW, "W", "probe " + s.Id));
                list.Add(A(pf + "_wavelength_m", s.WavelengthM, "m", "probe " + s.Id));
                list.Add(A(pf + "_spectrum", s.SpectrumPerOmega, "arb.", "probe " + s.Id));
                list.Add(A(pf + "_phase_x_rad", s.PhaseRad, "rad", "probe " + s.Id));
            }
            foreach (var i in Instruments) foreach (var a in i.Arrays) list.Add(a);
            foreach (var r in Records) foreach (var a in r.Arrays) list.Add(a);
            _arrays = list;
            return list;
        }

        public static string Sanitize(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());

        public static string[] ElementColumns() => new[]
        {
            "index", "cumulative_length_m", "energy_in_j", "energy_out_j", "transmission", "arrival_time_s",
            "own_gd_s", "own_gdd_s2", "own_tod_s3", "system_gd_s", "system_gdd_s2", "system_tod_s3",
            "peak_power_w", "fwhm_s", "rms_width_s", "tl_fwhm_s", "temporal_strehl", "spectral_fwhm_m", "centroid_wavelength_m",
            "pulse_gdd_s2", "b_integral_rad", "cumulative_b_rad", "peak_intensity_w_m2", "peak_fluence_j_m2", "lidt_margin",
            "edge_energy_fraction", "outside_data_fraction", "critical_power_ratio", "s1", "s2", "s3"
        };

        private static double[] ElementRow(ElementRecord r) => new[]
        {
            r.Index, r.CumulativeLengthM, r.EnergyInJ, r.EnergyOutJ, r.EnergyInJ > 0 ? r.EnergyOutJ / r.EnergyInJ : double.NaN, r.ArrivalS,
            r.Own.GroupDelayS, r.Own.GddS2, r.Own.TodS3, r.System.GroupDelayS, r.System.GddS2, r.System.TodS3,
            r.Out.PeakPowerW, r.Out.FwhmS, r.Out.RmsWidthS, r.Out.TransformLimitFwhmS, r.Out.TemporalStrehl, r.Out.SpectralFwhmM, r.Out.CentroidWavelengthM,
            r.Out.PulseGddS2, r.BIntegralRad, r.CumulativeBIntegralRad, r.PeakIntensityWPerM2, r.PeakFluenceJPerM2, r.LidtMargin,
            r.Out.EdgeEnergyFraction, r.OutsideDataFraction, r.CriticalPowerRatio, r.Out.S1, r.Out.S2, r.Out.S3
        };

        public NdArray GetArray(string name) => Arrays().FirstOrDefault(a => a.Name == name);

        // ------------------------------------------------------------------ writers
        public void WriteAll(string directory)
        {
            Directory.CreateDirectory(directory);
            var o = Compiled.Run.Outputs;
            if (o.Json)
            {
                File.WriteAllText(Path.Combine(directory, "summary.json"), SummaryJson().ToJson(true));
                File.WriteAllText(Path.Combine(directory, "provenance.json"), Compiled.Provenance.ToJson().ToJson(true));
            }
            if (o.Npz) NpzWriter.Write(Path.Combine(directory, "result.npz"), Arrays());
            foreach (var ext in Plugins.PluginManager.Extensions) ext.Write(this, directory);
            if (o.Csv) WriteCsv(directory);
        }

        public void WriteCsv(string directory)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            var cols = ElementColumns();
            sb.Append("id,type,label,").Append(string.Join(",", cols)).Append(",losses\n");
            foreach (var r in Records)
            {
                sb.Append(Csv(r.Id)).Append(',').Append(Csv(r.Type)).Append(',').Append(Csv(r.Label)).Append(',');
                sb.Append(string.Join(",", ElementRow(r).Select(v => double.IsNaN(v) ? "" : v.ToString("G9", ci))));
                sb.Append(',').Append(Csv(string.Join("; ", r.LossJ.Select(kv => kv.Key + "=" + kv.Value.ToString("G4", ci)))));
                sb.Append('\n');
            }
            File.WriteAllText(Path.Combine(directory, "elements.csv"), sb.ToString());
            if (ReferenceEquals(Input.Grid, Output.Grid))
            {
                WriteTraceCsv(Path.Combine(directory, "spectrum.csv"), "wavelength_m", new[] { "input_spectrum", "output_spectrum" },
                    Input.Grid.WavelengthAxisM(), Input.SpectralIntensity(), Output.SpectralIntensity(), Input.SpectralPhaseUnwrappedX(), Output.SpectralPhaseUnwrappedX());
                WriteTraceCsv(Path.Combine(directory, "temporal.csv"), "time_s", new[] { "input_power_w", "output_power_w" },
                    Input.Grid.Time, PulseMetrics.TemporalPower(Input), PulseMetrics.TemporalPower(Output), null, null);
            }
            else
            {
                // input and output live on different grids (carrier change): one file each
                WriteTraceCsv(Path.Combine(directory, "spectrum_input.csv"), "wavelength_m", new[] { "input_spectrum" },
                    Input.Grid.WavelengthAxisM(), Input.SpectralIntensity(), null, Input.SpectralPhaseUnwrappedX(), null);
                WriteTraceCsv(Path.Combine(directory, "spectrum.csv"), "wavelength_m", new[] { "output_spectrum" },
                    Output.Grid.WavelengthAxisM(), Output.SpectralIntensity(), null, Output.SpectralPhaseUnwrappedX(), null);
                WriteTraceCsv(Path.Combine(directory, "temporal_input.csv"), "time_s", new[] { "input_power_w" },
                    Input.Grid.Time, PulseMetrics.TemporalPower(Input), null, null, null);
                WriteTraceCsv(Path.Combine(directory, "temporal.csv"), "time_s", new[] { "output_power_w" },
                    Output.Grid.Time, PulseMetrics.TemporalPower(Output), null, null, null);
            }
            foreach (var i in Instruments) i.WriteCsv(directory);
        }

        private void WriteTraceCsv(string path, string xName, string[] names, double[] x, double[] a, double[] b, double[] pa, double[] pb)
        {
            var ci = CultureInfo.InvariantCulture;
            int max = Compiled.Run.Outputs.MaxPoints;
            double amax = Math.Max(1e-300, a.Max()), bmax = b == null ? 1 : Math.Max(1e-300, b.Max());
            var both = new double[a.Length]; for (int i = 0; i < a.Length; i++) both[i] = a[i] / amax + (b == null ? 0 : b[i] / bmax);
            var (lo, hi) = PulseMetrics.SupportRange(both, 1e-9);
            var idx = Enumerable.Range(lo, hi - lo + 1).ToArray();
            if (max > 0 && idx.Length > max) idx = Enumerable.Range(0, max).Select(i => lo + (int)((long)i * (hi - lo) / (max - 1))).ToArray();
            var sb = new StringBuilder();
            sb.Append(xName).Append(',').Append(names[0]);
            if (b != null) sb.Append(',').Append(names[1]);
            if (pa != null) sb.Append(b != null ? ",input_phase_x_rad,output_phase_x_rad" : "," + names[0].Replace("_spectrum", "") + "_phase_x_rad");
            sb.Append('\n');
            foreach (int i in idx)
            {
                if (double.IsNaN(x[i])) continue;
                sb.Append(x[i].ToString("G10", ci)).Append(',').Append(a[i].ToString("G6", ci));
                if (b != null) sb.Append(',').Append(b[i].ToString("G6", ci));
                if (pa != null) { sb.Append(',').Append(pa[i].ToString("G8", ci)); if (pb != null) sb.Append(',').Append(pb[i].ToString("G8", ci)); }
                sb.Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
        }

        internal static string Csv(string s) => s == null ? "" : (s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s);
    }

    public static class EngineInfo
    {
        public const string Version = "2.1.8-beta";
    }
}
