using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using UltrafastEngine.Engine;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Measured;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Plugins.Fitting
{
    /// <summary>
    /// Backward propagation ("backward" in the run spec, docs/plugins/fitting.md#backward-propagation): the run's
    /// pulse is the field measured at a plane downstream (default: the system output); the engine undoes the elements
    /// in reverse order to find the field at every plane upstream, up to the input. Linear terms are inverted with
    /// Tikhonov regularisation, nonlinear segments are integrated with z reversed (exact for Kerr, Raman and
    /// self-steepening up to the step tolerance), transform elements use their inverses, and active elements
    /// (amplifiers, crystals, spatial models) are linearised from a forward reference run, passed through, or refused.
    /// </summary>
    public static class BackwardPropagation
    {
        /// <summary>Report of a backward run (summary.json → backward; reconstructed-pulse.json).</summary>
        public sealed class Report
        {
            public string From, To, ActiveMode;
            public double Regularization;
            public List<(string Element, string Method, double Clipped)> Steps = new List<(string, string, double)>();
            public JsonObject ReconstructedPulse;
            public List<string> Notes = new List<string>();

            public JsonObject ToJson() => new JsonObject().Set("from", From).Set("to", To).Set("regularization", Regularization).Set("active", ActiveMode)
                .Set("steps", new JsonArray(Steps.Select(s => (JsonValue)new JsonObject().Set("element", s.Element).Set("method", s.Method).Set("regularizedEnergyFraction", s.Clipped))))
                .Set("notes", JsonValue.FromArray(Notes));
        }

        public static RunResult Run(SystemDescription system, RunSpec run, ModelRegistry registry, ProgressCallback progress = null, CancellationToken cancel = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var bv = run.Raw["backward"];
            var bo = bv.Kind == JsonKind.Object ? bv.AsObject() : new JsonObject();
            var rep = new Report { From = bo.GetString("from", "end"), To = bo.GetString("to", "input"), Regularization = bo.GetDouble("regularization", 1e-3), ActiveMode = bo.GetString("active", "error").ToLowerInvariant() };
            if (rep.ActiveMode != "error" && rep.ActiveMode != "linearized" && rep.ActiveMode != "identity") throw new JsonException("backward.active must be \"error\", \"linearized\" or \"identity\"");
            var cr = Compiler.Compile(system, run, registry);
            var els = cr.Elements;
            int fromIdx = rep.From == "end" ? els.Count - 1 : els.FindIndex(e => string.Equals(e.Id, rep.From, StringComparison.OrdinalIgnoreCase));
            int toIdx = rep.To == "input" ? -1 : els.FindIndex(e => string.Equals(e.Id, rep.To, StringComparison.OrdinalIgnoreCase));
            if (fromIdx < 0 && rep.From != "end") throw new JsonException("backward.from: no element '" + rep.From + "'");
            if (toIdx < -1 || (toIdx == -1 && rep.To != "input")) throw new JsonException("backward.to: no element '" + rep.To + "'");
            if (toIdx >= fromIdx && els.Count > 0) throw new JsonException("backward: 'to' (" + rep.To + ") must be upstream of 'from' (" + rep.From + ")");
            // forward reference run for linearised active elements
            RunResult reference = null;
            bool needRef = Enumerable.Range(toIdx + 1, Math.Max(0, fromIdx - toIdx)).Any(i => els[i].Enabled && els[i].Active != null && !(els[i].Active is TransformActive));
            if (needRef && rep.ActiveMode == "error")
            {
                var bad = Enumerable.Range(toIdx + 1, fromIdx - toIdx).Where(i => els[i].Enabled && els[i].Active != null && !(els[i].Active is TransformActive)).Select(i => els[i].Id + " (" + els[i].Active.Kind + ")");
                throw new JsonException("backward: element(s) " + string.Join(", ", bad) + " cannot be inverted exactly -- set backward.active to \"linearized\" (with a referencePulse: the gain / conversion of a forward run is undone as a linear filter) or \"identity\" (passed through)");
            }
            if (needRef && rep.ActiveMode == "linearized")
            {
                var rp = bo.GetObject("referencePulse") ?? throw new JsonException("backward.active = \"linearized\" needs backward.referencePulse: the input pulse of a forward run that sets the operating point of the amplifiers / crystals");
                var rj = (JsonObject)JsonValue.Parse(run.Raw.ToJson(false));
                rj.Remove("backward"); rj.Set("pulse", rp); rj.Set("instruments", new JsonArray()); rj.Set("probes", "all");
                reference = Executor.Execute(Compiler.Compile(system, RunSpec.FromJson(rj, run.BaseDirectory), registry), null, cancel);
                rep.Notes.Add("forward reference run for linearised active elements: input " + rp.ToJson(false));
            }
            // ------------------------------------------------------------ walk backwards
            var res = new RunResult { Compiled = cr };
            res.Notes.AddRange(cr.Notes); res.Warnings.AddRange(cr.Warnings);
            var pulse = cr.Input;
            string fromKey = rep.From == "end" ? "end" : els[fromIdx].Id;
            res.At[fromKey] = pulse;
            if (fromIdx >= 0) res.At[els[fromIdx].Id] = pulse;
            res.At["end"] = pulse;
            res.Output = pulse;
            var records = new List<ElementRecord>();
            int total = fromIdx - toIdx;
            for (int ei = fromIdx; ei > toIdx; ei--)
            {
                cancel.ThrowIfCancellationRequested();
                var el = els[ei];
                progress?.Invoke(el.Id, fromIdx - ei, total, 0);
                var rec = new ElementRecord { Index = ei, Id = el.Id, Type = el.Type, Label = el.Label, LengthM = el.LengthM, EnergyOutJ = pulse.TotalEnergyJ() };
                rec.Out = PulseMetrics.Summarize(pulse);
                if (!el.Enabled) { records.Add(rec); continue; }
                double reg = el.Source?.Json?.GetDouble("regularization", rep.Regularization) ?? rep.Regularization;
                var methods = new List<string>(); double clipped = 0;
                foreach (var t in Enumerable.Reverse(el.Post)) { pulse = LinearInverse.Apply(pulse, t, reg, out var cf); clipped = Math.Max(clipped, cf); methods.Add(t is TransformTerm ? "linear operators of the transform inverted" : "inverse " + t.Mechanism); }
                if (el.Active is TransformActive ta) { var notes = new List<string>(); pulse = ta.Invert(pulse, reg, notes); rec.Notes.AddRange(notes); methods.Add("field operators of the transform undone"); }
                else if (el.Active != null)
                {
                    if (rep.ActiveMode == "identity") { methods.Add("passed through (identity)"); rec.Notes.Add("active element " + el.Active.Kind + " passed through unchanged (backward.active = identity)"); }
                    else
                    {
                        var (pin, pout) = RefFields(reference, els, ei);
                        if (pin == null || pout == null) throw new InvalidOperationException("backward: the reference run has no fields around '" + el.Id + "'");
                        pulse = Linearized(pulse, pin, pout, reg, out var cf); clipped = Math.Max(clipped, cf);
                        methods.Add("linearised from the reference run (gain / conversion undone as a filter)");
                    }
                }
                else if (el.Nonlinear != null)
                {
                    pulse = ReverseSegment(cr, pulse, el.Nonlinear, el.Id, rec);
                    methods.Add("nonlinear segment, z reversed");
                }
                foreach (var t in Enumerable.Reverse(el.Pre)) { pulse = LinearInverse.Apply(pulse, t, reg, out var cf); clipped = Math.Max(clipped, cf); methods.Add(t is TransformTerm ? "linear operators of the transform inverted" : "inverse " + t.Mechanism); }
                rec.EnergyInJ = pulse.TotalEnergyJ();
                if (clipped > 1e-3) rec.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0:P2} of the spectral energy lies where the transmission is below ~{1:G2} of its peak: the field there is limited by the regularisation (not recoverable)", clipped, reg));
                rec.Notes.Add("backward: " + string.Join(", ", methods.Distinct()));
                rep.Steps.Add((el.Id, string.Join(", ", methods.Distinct()), clipped));
                records.Add(rec);
                string key = ei - 1 >= 0 ? els[ei - 1].Id : "input";
                res.At[key] = pulse;
                progress?.Invoke(el.Id, fromIdx - ei, total, 1);
            }
            if (toIdx == -1) res.At["input"] = pulse;
            res.Input = pulse;
            // records in forward order; each record's "out" is the field after the element (as in a forward run)
            records.Reverse();
            foreach (var r in records) res.Records.Add(r);
            res.Notes.Add(string.Format(CultureInfo.InvariantCulture, "backward propagation from {0} to {1} through {2} element(s), regularisation {3:G2}", rep.From, rep.To, total, rep.Regularization));
            rep.ReconstructedPulse = PulseJson(pulse, rep.To);
            res.Extensions[BackwardKey] = rep;
            Executor.MeasureInstruments(cr, res, cancel);
            res.ElapsedS = sw.Elapsed.TotalSeconds;
            return res;
        }

        internal const string BackwardKey = "fitting.backward";
        public static Report Backward(this RunResult r) => r.Extensions.TryGetValue(BackwardKey, out var o) ? (Report)o : null;

        private static (PulseField In, PulseField Out) RefFields(RunResult reference, List<CompiledElement> els, int ei)
        {
            if (reference == null) return (null, null);
            PulseField pin = null;
            for (int k = ei - 1; k >= -1 && pin == null; k--) { string key = k >= 0 ? els[k].Id : "input"; if (reference.At.TryGetValue(key, out var p)) pin = p; }
            reference.At.TryGetValue(els[ei].Id, out var pout);
            return (pin, pout);
        }

        /// <summary>Undo an active element as the linear filter H = A_out / A_in it was in the reference run
        /// (per polarisation; regularised), after moving the pulse onto the reference grids.</summary>
        private static PulseField Linearized(PulseField p, PulseField refIn, PulseField refOut, double reg, out double clipped)
        {
            if (p.Grid.N != refOut.Grid.N || Math.Abs(p.Grid.DTime - refOut.Grid.DTime) > 1e-9 * refOut.Grid.DTime || Math.Abs(p.Grid.OmegaCenter - refOut.Grid.OmegaCenter) > 1e-9 * refOut.Grid.OmegaCenter)
                p = GridResampler.Resample(p, refOut.Grid, out _);
            var ri = refIn.Grid.N == refOut.Grid.N && Math.Abs(refIn.Grid.OmegaCenter - refOut.Grid.OmegaCenter) < 1e-9 * refOut.Grid.OmegaCenter ? refIn : GridResampler.Resample(refIn, refOut.Grid, out _);
            int n = refOut.Grid.N;
            var hx = new Complex[n]; var hy = new Complex[n];
            double mx = ri.Ax.Max(z => z.Magnitude), my = ri.Ay.Max(z => z.Magnitude);
            for (int i = 0; i < n; i++)
            {
                hx[i] = ri.Ax[i].Magnitude > 1e-6 * mx ? refOut.Ax[i] / ri.Ax[i] : Complex.Zero;
                hy[i] = my > 0 && ri.Ay[i].Magnitude > 1e-6 * my ? refOut.Ay[i] / ri.Ay[i] : hx[i];
            }
            var q = LinearInverse.Apply(p, JonesSpectrum.Diagonal(hx, hy), reg, out clipped);
            // back onto the input grid of the reference run
            if (refIn.Grid.N != n || Math.Abs(refIn.Grid.OmegaCenter - refOut.Grid.OmegaCenter) > 1e-9 * refOut.Grid.OmegaCenter)
                q = GridResampler.Resample(q, refIn.Grid, out _);
            return q;
        }

        /// <summary>Integrate a nonlinear segment from its end back to its start: dA/ds = -L A - N(A, L - s).</summary>
        private static PulseField ReverseSegment(CompiledRun cr, PulseField p, NonlinearSegment seg, string id, ElementRecord rec)
        {
            var g = p.Grid; int n = g.N;
            var axis = OmegaAxis.FromGrid(g);
            var phX = new double[n]; var ampX = Enumerable.Repeat(1.0, n).ToArray(); double[] phY = null, ampY = null;
            foreach (var t in seg.Background)
            {
                if (!t.TryPhaseAmplitude(axis, out var px, out var ax, out var py, out var ay)) throw new InvalidOperationException("backward: a polarisation-mixing term inside '" + id + "' cannot be reversed");
                if (py != null && phY == null) { phY = (double[])phX.Clone(); ampY = (double[])ampX.Clone(); }
                for (int i = 0; i < n; i++)
                {
                    if (phY != null) { phY[i] += (py ?? px)[i]; ampY[i] *= (ay ?? ax)[i]; }
                    phX[i] += px[i]; ampX[i] *= ax[i];
                }
            }
            bool scalar = phY == null && p.Ay.All(c => c == Complex.Zero);
            double tauEl = seg.Background.Sum(b => b.GroupDelayAt(g.OmegaCenter));
            var inner = new EnvelopeProblem(g, seg.Medium, seg.Beam, seg.LengthM, phX, ampX, phY, ampY, scalar, 0, tauEl, id);
            var prob = new ReversedProblem(inner, 1e-3);
            var state = new Complex[prob.Length];
            Array.Copy(p.Ax, 0, state, 0, n);
            if (!scalar) Array.Copy(p.Ay, 0, state, n, n);
            var stats = new StepStatistics();
            var outS = Steppers.Propagate(prob, state, cr.Run.Stepper, stats, null);
            var bx = new Complex[n]; var by = new Complex[n];
            Array.Copy(outS, 0, bx, 0, n);
            if (!scalar) Array.Copy(outS, n, by, 0, n); else by = (Complex[])p.Ay.Clone();
            rec.Steps = stats; rec.BIntegralRad = stats.BIntegralRad;
            if (seg.CarrierResolved) rec.Notes.Add("this segment runs carrier-resolved going forward; it is reversed in the envelope picture (accurate unless the spectrum spans an octave)");
            if (seg.Medium.ExtraResponses.Count > 0) rec.Notes.Add("extra responses (ionisation, chi(2), user) are dissipative or coupled: reversing them is approximate");
            return p.WithSpectrum(bx, by);
        }

        /// <summary>The same propagation problem run from z = L to z = 0. Absorption becomes gain when reversed: the
        /// gain per segment is capped at 1 / floor so bins absorbed almost completely are not amplified without bound.</summary>
        private sealed class ReversedProblem : IPropagationProblem
        {
            private readonly IPropagationProblem _p;
            public ReversedProblem(IPropagationProblem p, double floor)
            {
                _p = p;
                double cap = -Math.Log(floor) / p.LengthM;
                LinearRate = p.LinearRate.Select(l => new Complex(Math.Min(-l.Real, cap), -l.Imaginary)).ToArray();
            }
            public int Length => _p.Length;
            public double LengthM => _p.LengthM;
            public Complex[] LinearRate { get; }
            public double AreaAt(double z) => _p.AreaAt(LengthM - z);
            public double Nonlinear(Complex[] state, double z, Complex[] rateOut)
            {
                double b = _p.Nonlinear(state, LengthM - z, rateOut);
                for (int i = 0; i < rateOut.Length; i++) rateOut[i] = -rateOut[i];
                return b;
            }
        }

        /// <summary>The reconstructed field as a pulse object for a run spec (shape: spectrum, with phase and energy).</summary>
        public static JsonObject PulseJson(PulseField p, string plane)
        {
            var g = p.Grid; var s = p.SpectralIntensity();
            double mx = s.Max();
            var idx = Enumerable.Range(0, g.N).Where(i => s[i] > 1e-7 * mx && g.OmegaAbsolute[i] > 0).ToList();
            if (idx.Count == 0) idx = Enumerable.Range(0, g.N).Where(i => g.OmegaAbsolute[i] > 0).ToList();
            int lo = idx.First(), hi = idx.Last();
            var range = Enumerable.Range(lo, hi - lo + 1).ToList();
            if (range.Count > 4096) { int step = (range.Count + 4095) / 4096; range = range.Where((_, k) => k % step == 0).ToList(); }
            var ph = p.SpectralPhaseUnwrappedX();
            // remove the linear part (a delay) about the spectral centroid so the pulse is centred in a new window
            double sw = range.Sum(i => s[i]), wc = range.Sum(i => s[i] * g.OmegaAbsolute[i]) / Math.Max(sw, 1e-300);
            double sxx = 0, sxy = 0, sx = 0, sy = 0, sw2 = 0;
            foreach (var i in range) { double w = s[i], x = g.OmegaAbsolute[i] - wc; sw2 += w; sx += w * x; sy += w * ph[i]; sxx += w * x * x; sxy += w * x * ph[i]; }
            double slope = (sw2 * sxy - sx * sy) / Math.Max(1e-300, sw2 * sxx - sx * sx), icpt = (sy - slope * sx) / Math.Max(sw2, 1e-300);
            var lam = new List<double>(); var inten = new List<double>(); var phase = new List<double>();
            foreach (var i in Enumerable.Reverse(range))    // ascending wavelength
            {
                double w = g.OmegaAbsolute[i], l = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / w;
                lam.Add(l); inten.Add(s[i] / (l * l) / mx); phase.Add(ph[i] - icpt - slope * (w - wc));
            }
            double im = inten.Max(); for (int k = 0; k < inten.Count; k++) inten[k] /= im;
            var o = new JsonObject().Set("shape", "spectrum").Set("energyJ", p.TotalEnergyJ())
                .Set("spectrum", new JsonObject().Set("wavelengthsM", JsonValue.FromArray(lam.ToArray())).Set("intensity", JsonValue.FromArray(inten.ToArray())).Set("phaseRad", JsonValue.FromArray(phase.ToArray())))
                .Set("_note", "field reconstructed at '" + plane + "' by backward propagation (UltrafastEngine fitting plugin); the polarisation is reduced to x");
            if (p.RepRateHz > 0) o.Set("repRateHz", p.RepRateHz);
            return o;
        }
    }
}
