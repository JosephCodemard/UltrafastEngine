using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;

namespace UltrafastEngine.Engine
{
    /// <summary>Per-element diagnostics row (one line of elements.csv).</summary>
    public sealed class ElementRecord
    {
        /// <summary>A copy whose energies and losses can change without touching this one.</summary>
        public ElementRecord Copy()
        {
            var c = (ElementRecord)MemberwiseClone();
            c.LossJ = new Dictionary<string, double>(LossJ, StringComparer.OrdinalIgnoreCase);
            c.Values = new Dictionary<string, double>(Values);
            return c;
        }

        public int Index; public string Id, Type, Label;
        public double LengthM, CumulativeLengthM;
        public double EnergyInJ, EnergyOutJ;
        public Dictionary<string, double> LossJ = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public PulseSummary Out;
        /// <summary>Lab-frame arrival time of the carrier group delay after this element (s).</summary>
        public double ArrivalS;
        public DispersionPoint Own, System;
        public double BIntegralRad, CumulativeBIntegralRad;
        public StepStatistics Steps;
        public double PeakIntensityWPerM2 = double.NaN, PeakFluenceJPerM2 = double.NaN, LidtFluenceJPerM2 = double.NaN, LidtMargin = double.NaN;
        public double OutsideDataFraction = double.NaN;
        /// <summary>Peak power entering a nonlinear element divided by the critical power for self-focusing,
        /// P_cr = 3.77 lambda^2 / (8 pi n0 n2). Spatial effects are not modelled at L0: values above ~0.3 deserve care.</summary>
        public double CriticalPowerRatio = double.NaN;
        public bool Probe;
        public List<string> Notes = new List<string>();
        public List<string> Terms = new List<string>();
        /// <summary>Fields produced by the element but not followed (e.g. idler, residual pump), with their summaries.</summary>
        public Dictionary<string, PulseSummary> Byproducts = new Dictionary<string, PulseSummary>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Auxiliary beams an active element took in: field name -> description (source, beam, delay).</summary>
        public Dictionary<string, string> Aux = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Element-specific arrays (gain spectrum, inversion vs z, energy per pass ...), exported as
        /// "el_&lt;id&gt;_&lt;name&gt;" in result.npz.</summary>
        public List<Serialization.NdArray> Arrays = new List<Serialization.NdArray>();

        public void AddArray(string name, double[] data, string unit, string description, params int[] shape)
            => Arrays.Add(new Serialization.NdArray("el_" + Results.RunResult.Sanitize(Id) + "_" + name, data, shape) { Unit = unit, Description = "element " + Id + ": " + description });
        /// <summary>Spatial elements: second-moment beam radius and M^2 at the exit, true peak fluence.</summary>
        public double BeamRadiusXM = double.NaN, BeamRadiusYM = double.NaN, BeamQualityM2 = double.NaN;
        /// <summary>Centre wavelength of the beam leaving the element (changes after SHG, idler output...).</summary>
        public double CarrierWavelengthM = double.NaN;
        public string Representation;
        public Dictionary<string, double> Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Compact snapshot of the pulse at a probe (cropped to its support and decimated).</summary>
    public sealed class ProbeSnapshot
    {
        public string Id;
        public double[] TimeS, PowerW, OmegaRadPerS, WavelengthM, SpectrumPerOmega, PhaseRad;
    }

    public delegate void ProgressCallback(string elementId, int elementIndex, int elementCount, double fraction);

    /// <summary>
    /// Executes a <see cref="CompiledRun"/>: walks the elements, applies linear terms (with per-mechanism energy
    /// accounting), integrates nonlinear segments, records diagnostics and probe snapshots, then runs the
    /// instruments.
    /// </summary>
    public static partial class Executor
    {
        public static RunResult Execute(CompiledRun cr, ProgressCallback progress = null, CancellationToken cancel = default)
        {
            // compute back-end and threads for this run
            var prevBackend = ComputeBackends.Current; var prevFft = Fft.Provider; int prevThreads = ParallelismSettings.MaxDegreeOfParallelism;
            if (!string.IsNullOrEmpty(cr.Run.ComputeFft)) { var fn = FftProviders.Select(cr.Run.ComputeFft); if (fn != null) cr.Warnings.Add(fn); }
            var bname = ComputeBackends.Resolve(cr.Run.ComputeBackend);
            var b = ComputeBackends.Get(bname);
            if (b == null) { cr.Warnings.Add("compute back-end \"" + bname + "\" is not available (installed: " + string.Join(", ", ComputeBackends.Available.Select(x => x.Name)) + "); using cpu."); b = ComputeBackends.Get("cpu"); }
            try
            {
                ComputeBackends.Current = b;
                ComputeBackends.Settings = new ComputeSettings { Device = ComputeBackends.ResolveDevice(cr.Run.ComputeDevice), GpuMinPoints = cr.Run.GpuMinPoints, DeviceSlots = cr.Run.DeviceSlots, Options = cr.Run.ComputeOptions };
                if (cr.Run.Threads > 0) ParallelismSettings.MaxDegreeOfParallelism = cr.Run.Threads;
                var r = Execute(cr, progress, cancel, true);
                if (!(b is CpuBackend)) r.Notes.Add("compute back-end: " + b.Description);
                return r;
            }
            finally { ComputeBackends.Current = prevBackend; Fft.Provider = prevFft; ComputeBackends.Settings = null; ParallelismSettings.MaxDegreeOfParallelism = prevThreads; }
        }

        /// <summary>
        /// Run a list of compiled elements on a pulse (no instruments): used by composite elements such as the
        /// oscillator cavity, once per round trip. The records are returned in the result.
        /// </summary>
        public static RunResult RunElements(CompiledRun parent, IEnumerable<CompiledElement> elements, PulseField input, CancellationToken cancel = default)
        {
            var sub = new CompiledRun { System = parent.System, Run = parent.Run, Registry = parent.Registry, Grid = input.Grid, Input = input, GridPlan = parent.GridPlan, RequestedOmega0 = parent.RequestedOmega0 };
            sub.Elements.AddRange(elements);
            return Execute(sub, null, cancel, false);
        }

        /// <summary>
        /// Execute from element <paramref name="startIndex"/> on, reusing the state of <paramref name="baseResult"/> before it
        /// (prefix caching in parameter studies). Valid when the elements before startIndex compile identically and the grid
        /// is the same (studies freeze the grid).
        /// </summary>
        public static RunResult ExecuteFrom(CompiledRun cr, RunResult baseResult, int startIndex, CancellationToken cancel = default)
            => Execute(cr, null, cancel, true, baseResult, startIndex);

        private static RunResult Execute(CompiledRun cr, ProgressCallback progress, CancellationToken cancel, bool measure, RunResult resumeBase = null, int resumeIndex = 0)
        {
            var sw = Stopwatch.StartNew();
            var axes = new Dictionary<FrequencyGrid, OmegaAxis>();
            OmegaAxis AxisOf(FrequencyGrid g) { if (!axes.TryGetValue(g, out var ax)) axes[g] = ax = OmegaAxis.FromGrid(g); return ax; }
            var res = new RunResult { Compiled = cr, Input = cr.Input };
            res.Notes.AddRange(cr.Notes); res.Warnings.AddRange(cr.Warnings);
            int snapPoints = cr.Run.Outputs.MaxPoints > 0 ? cr.Run.Outputs.MaxPoints : 2048;
            var pulse = cr.Input;
            res.At["input"] = pulse;
            TailLedger tail = null;
            var tailKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "end" };
            if (measure && cr.Run.Grid.TailEnabled)
            {
                if (resumeBase != null) res.Notes.Add("grid.tail is not used in prefix-cached study runs");
                else
                {
                    foreach (var ispec in cr.Run.Instruments) tailKeys.Add(ispec.GetString("at", "end"));
                    foreach (var pe in cr.Elements.Where(x => x.Probe)) tailKeys.Add(pe.Id);
                    double mainW = cr.Grid.N * cr.Grid.DTime;
                    double sat = Compiler.SatelliteRoom(cr.Elements.Where(x => x.Enabled), cr.Run.Grid.SatelliteLevel);
                    double wTail = double.IsNaN(cr.Run.Grid.TailWindowS) ? Math.Max(8 * mainW, 2.5 * (2 * sat + mainW)) : cr.Run.Grid.TailWindowS;
                    if (wTail < 2 * mainW) { res.Warnings.Add("grid.tail.windowS is shorter than twice the main window; using twice the main window."); wTail = 2 * mainW; }
                    tail = new TailLedger(cr.Grid, wTail, cr.Run.Grid.MaxN);
                    res.Notes.Add(string.Format(CultureInfo.InvariantCulture, "Delayed-light ledger: tail grid N = {0} ({1}x the main grid), window {2:G4} ps, dt {3:G4} fs; linear elements act on main + tail exactly, nonlinear elements on the main field (the tail's own nonlinear propagation and main x tail interference are neglected).",
                        tail.Long.N, tail.Factor, tail.WindowS * 1e12, tail.Long.DTime * 1e15));
                    if (tail.WindowS < wTail * 0.999) res.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "grid.maxN limits the tail window to {0:G4} ps (wanted {1:G4} ps).", tail.WindowS * 1e12, wTail * 1e12));
                }
            }
            var path = new PathLedger();
            int count = cr.Elements.Count;
            int first = 0;
            if (resumeBase != null && resumeIndex > 0 && cr.Graph?.IsBranched != true && resumeBase.Output.Grid.N == cr.Grid.N && Math.Abs(resumeBase.Output.Grid.DTime - cr.Grid.DTime) < 1e-12 * cr.Grid.DTime)
            {
                first = Math.Min(resumeIndex, count);
                foreach (var rr in resumeBase.Records.Where(x => x.Index < first)) { res.Records.Add(rr); res.At[rr.Id] = resumeBase.At[rr.Id]; path.ArrivalS = rr.ArrivalS; path.CumulativeLengthM = rr.CumulativeLengthM; path.CumulativeBIntegralRad = rr.CumulativeBIntegralRad; path.System = rr.System; pulse = resumeBase.At[rr.Id]; }
                foreach (var sn in resumeBase.Snapshots.Where(x => res.At.ContainsKey(x.Id) && x.Id != "end")) res.Snapshots.Add(sn);
                res.Notes.Add("prefix cache: elements before #" + first + " reused from the nominal run");
            }
            var st = new ExecutionState { Cr = cr, Res = res, Tail = tail, TailKeys = tailKeys, SnapPoints = snapPoints, AxisOf = AxisOf, Progress = progress, Cancel = cancel, Count = count };
            bool branched = cr.Graph?.IsBranched == true;
            if (branched) RunGraph(st);
            for (int ei = first; ei < count && !branched; ei++)
            {
                cancel.ThrowIfCancellationRequested();
                var el = cr.Elements[ei];
                if (!el.Enabled) continue;
                pulse = RunElement(st, ei, el, pulse, path);
            }
            if (!branched) tail = st.Tail; else tail = null;
            if (!branched) { res.Output = pulse; res.At["end"] = pulse; }
            if (tail != null && ReferenceEquals(pulse.Grid, tail.Main))
            {
                res.TailLedger = tail;
                res.TailAt["end"] = tail.TailField(pulse.RepRateHz);
                double eTail = tail.TailEnergyJ, eMain = pulse.TotalEnergyJ();
                if (eTail > cr.Run.Grid.TailWarnFraction * (eTail + eMain))
                    res.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "The delayed-light tail holds {0:P2} of the output energy (more than {1:P1}): the neglected nonlinear propagation of the tail may matter.", eTail / (eTail + eMain), cr.Run.Grid.TailWarnFraction));
                double edge = tail.TailEdgeEnergyFraction();
                if (edge > 1e-3 && eTail > 1e-9 * eMain)
                    res.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "{0:P2} of the tail energy is near the edge of the tail window ({1:G4} ps): the tail wraps around; raise grid.tail.windowS.", edge, tail.WindowS * 1e12));
            }
            if (!branched) { res.FinalArrivalS = path.ArrivalS; res.FinalSystem = path.System; }

            if (measure) MeasureInstruments(cr, res, cancel);
            res.ElapsedS = sw.Elapsed.TotalSeconds;
            return res;
        }

        /// <summary>What every element of a run shares while it executes.</summary>
        private sealed class ExecutionState
        {
            public CompiledRun Cr; public RunResult Res; public TailLedger Tail; public HashSet<string> TailKeys;
            public int SnapPoints, Count; public Func<FrequencyGrid, OmegaAxis> AxisOf;
            public ProgressCallback Progress; public CancellationToken Cancel;
            /// <summary>Branched systems: the input port of this run of an element, and the by-products it gave.</summary>
            public string InputPort; public Dictionary<string, PulseField> LastOutputs;
        }

        /// <summary>Run one element on one pulse, record it, and advance <paramref name="path"/>. A sequential system calls this
        /// for each element in turn.</summary>
        private static PulseField RunElement(ExecutionState st, int ei, CompiledElement el, PulseField pulse, PathLedger path)
        {
            var cr = st.Cr; var res = st.Res; var tail = st.Tail; var tailKeys = st.TailKeys; var AxisOf = st.AxisOf;
            var progress = st.Progress; var cancel = st.Cancel; int snapPoints = st.SnapPoints, count = st.Count;
            st.LastOutputs = null;
            double arrival = path.ArrivalS, cumLen = path.CumulativeLengthM, cumB = path.CumulativeBIntegralRad;
            var system = path.System;
            progress?.Invoke(el.Id, ei, count, 0.0);
            var rec = new ElementRecord { Index = ei, Id = el.Id, Type = el.Type, Label = el.Label, LengthM = el.LengthM, Probe = el.Probe, EnergyInJ = pulse.TotalEnergyJ() };
            rec.Notes.AddRange(el.Notes);
            var own = new DispersionPoint();
            double omegaIn = pulse.Grid.OmegaCenter;

            // outside-data check (sampled terms)
            foreach (var t in el.AllLinear.OfType<SampledGroupDelayTerm>())
            {
                double f = OutsideFraction(pulse, t.MinOmega, t.MaxOmega);
                rec.OutsideDataFraction = double.IsNaN(rec.OutsideDataFraction) ? f : Math.Max(rec.OutsideDataFraction, f);
            }

            el.Prepare?.Invoke(pulse, rec);
            pulse = ApplyLinear(tail, pulse, el.Pre, AxisOf(pulse.Grid), rec, ref own, AxisOf, cr.Run.Grid.TailExactSteps);
            Action<double> prog = progress == null && !cancel.CanBeCanceled ? (Action<double>)null : f => { progress?.Invoke(el.Id, ei, count, f); cancel.ThrowIfCancellationRequested(); };
            if (el.Active != null)
            {
                int eiCopy = ei;
                var actx = new ActiveContext
                {
                    Run = cr, Element = el, Record = rec, Result = res, Index = ei, Path = path, InputPort = st.InputPort, InGraph = cr.Graph?.IsBranched == true, ArrivalS = arrival, Progress = prog, Cancel = cancel,
                    DownstreamGrid = p => PlanDownstreamGrid(cr, p, eiCopy + 1, rec.Notes)
                };
                pulse = el.Active.Execute(actx, pulse);
                st.LastOutputs = actx.Byproducts;
                if (!(el.Active is IReportsCarrierPhase) || actx.PhaseLost) path.PhaseKnown = false;
                path.PhaseRad *= actx.PathPhaseFactor;
                if (tail != null) rec.Notes.Add("delayed-light tail: this active element acts on the main field only; the tail passes through its Pre/Post linear terms");
                own += actx.Own;
                rec.Terms.Add(el.Active.Description);
                foreach (var kv in actx.Byproducts)
                {
                    rec.Byproducts[kv.Key] = PulseMetrics.Summarize(kv.Value);
                    string key = el.Id + "#" + kv.Key;
                    res.At[key] = kv.Value;
                    res.Snapshots.Add(Snapshot(key, kv.Value, snapPoints));
                }
                if (pulse.Profile != null && pulse.Profile.Origin == el.Id)
                {
                    var (bx, by) = pulse.Profile.SecondMomentRadius();
                    rec.BeamRadiusXM = bx; rec.BeamRadiusYM = by;
                    try { rec.BeamQualityM2 = Transverse.TransverseOps.BeamQuality(pulse.Profile); } catch { }
                }
            }
            else if (el.Nonlinear != null)
            {
                var seg = el.Nonlinear;
                var grid = pulse.Grid;
                double eBefore = pulse.TotalEnergyJ();
                if (!seg.Medium.ReplaceKerr && seg.Medium.N2 > 0)
                {
                    double lam = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / grid.OmegaCenter, n0 = seg.Material.IndexAt(grid.OmegaCenter);
                    double pcr = 3.77 * lam * lam / (8 * Math.PI * n0 * seg.Medium.N2);
                    rec.CriticalPowerRatio = PulseMetrics.TemporalPower(pulse).Max() / pcr;
                    if (rec.CriticalPowerRatio > 0.3 && !res.Warnings.Any(w => w.Contains("critical power")))
                        res.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Element '{0}': peak power is {1:G3} x the critical power for self-focusing; spatial effects (Kerr lensing, filamentation) are not modelled in 1-D -- use \"transverse\": {{\"model\": \"radial\"}} on this element.", el.Id, rec.CriticalPowerRatio));
                }
                var stats = new StepStatistics();
                var pulseBefore = pulse;
                double tauEl = seg.Background.Sum(b => b.GroupDelayAt(grid.OmegaCenter));
                pulse = seg.CarrierResolved
                    ? PropagateField(cr, pulse, seg, arrival, tauEl, el.Id, stats, rec, prog)
                    : PropagateEnvelope(cr, pulse, seg, AxisOf(grid), arrival, tauEl, el.Id, stats, prog);
                foreach (var b in seg.Background) { var d = b.DispersionAt(grid.OmegaCenter); own += d; rec.Terms.Add(b.Description); }
                tail?.PropagateTail(seg.Background, (m, dE) => AddLoss(rec, m, dE));
                if (tail != null && cr.Run.Grid.TailCrossPhase && !seg.CarrierResolved && !seg.Medium.ReplaceKerr && seg.Medium.N2 > 0 && ReferenceEquals(pulseBefore.Grid, pulse.Grid))
                {
                    double inv = 0; int ns = 100;
                    for (int k = 0; k < ns; k++) inv += seg.LengthM / ns / seg.Beam.EffectiveArea(seg.LengthM * (k + 0.5) / ns);
                    tail.CrossPhase(pulseBefore, pulse, grid.OmegaCenter / PhysicalConstants.SpeedOfLightMPerS * seg.Medium.N2 * inv);
                }
                double eAfter = pulse.TotalEnergyJ();
                // energy change inside a nonlinear segment: absorption if the medium absorbs (linear attenuation or
                // an imaginary index response); otherwise Raman (energy given to the medium) plus solver error
                bool absorbs = seg.Background.Any(b => b.Mechanism == "absorption") || seg.Medium.ExtraResponses.Count > 0;
                AddLoss(rec, absorbs ? "absorption" : (seg.Medium.EffectiveRamanFraction > 0 ? "raman+numerical" : "numerical"), eBefore - eAfter);
                rec.Steps = stats; rec.BIntegralRad = stats.BIntegralRad;
                foreach (var rr in seg.Medium.ExtraResponses.OfType<IReportingResponse>()) rr.Report(rec.Values);
                double minA = double.PositiveInfinity;
                for (int k = 0; k <= 200; k++) minA = Math.Min(minA, seg.Beam.EffectiveArea(seg.LengthM * k / 200.0));
                rec.PeakIntensityWPerM2 = Math.Max(Results.PulseMetrics.TemporalPower(pulse).Max(), 0) / minA;
                foreach (var n in stats.Notes) rec.Notes.Add(n);
            }
            pulse = ApplyLinear(tail, pulse, el.Post, AxisOf(pulse.Grid), rec, ref own, AxisOf, cr.Run.Grid.TailExactSteps);
            if (tail != null && !ReferenceEquals(pulse.Grid, tail.Main))
            {
                res.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Element '{0}' changed the grid or carrier: the delayed-light tail ({1:G3} J) is dropped from here on.", el.Id, tail.TailEnergyJ));
                tail = null; st.Tail = null;
            }

            // a carried transverse profile goes stale through elements that propagate without knowing about it
            if (pulse.Profile != null && pulse.Profile.Exact && !(el.Active?.TransverseAware ?? false) && el.LengthM > 0)
            {
                var q = pulse.WithSpectrum(pulse.Ax, pulse.Ay);
                q.Profile = pulse.Profile.Clone(); q.Profile.Exact = false;
                pulse = q;
                rec.Notes.Add("1-D element with length: the transverse profile carried from '" + q.Profile.Origin + "' will be rescaled (not propagated) at the next spatial element");
            }

            bool carrierChanged = Math.Abs(pulse.Grid.OmegaCenter - omegaIn) > 1e-3 * omegaIn;
            arrival += own.GroupDelayS;
            path.Add(own);
            if (carrierChanged)
            {
                system = new DispersionPoint();
                rec.Notes.Add(string.Format(CultureInfo.InvariantCulture, "carrier changed {0:F2} -> {1:F2} nm: system dispersion is accumulated afresh from here (new field)",
                    2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / omegaIn * 1e9, 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / pulse.Grid.OmegaCenter * 1e9));
            }
            else system += own;
            cumLen += el.LengthM;
            cumB += rec.BIntegralRad;
            rec.Own = own; rec.System = system; rec.ArrivalS = arrival; rec.CumulativeLengthM = cumLen; rec.CumulativeBIntegralRad = cumB;
            rec.EnergyOutJ = pulse.TotalEnergyJ();
            rec.Out = PulseMetrics.Summarize(pulse);
            rec.CarrierWavelengthM = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / pulse.Grid.OmegaCenter;
            if (el.BeamOut != null)
            {
                // Gaussian beam tracking: 1/e^2 radii and wavefront curvature at the exit
                rec.Values["trackedBeamRadiusXM"] = el.BeamOut.RadiusX; rec.Values["trackedBeamRadiusYM"] = el.BeamOut.RadiusY;
                rec.Values["trackedCurvatureXM"] = el.BeamOut.CurvatureX; rec.Values["trackedCurvatureYM"] = el.BeamOut.CurvatureY;
                if (double.IsNaN(rec.BeamRadiusXM)) { rec.BeamRadiusXM = el.BeamOut.RadiusX; rec.BeamRadiusYM = el.BeamOut.RadiusY; }
                Optics.Couplings.Report(el.BeamOut, pulse, rec.Values);
            }
            if (el.Surface != null && !double.IsNaN(el.Surface.BeamRadiusXM))
            {
                double wx = el.Surface.BeamRadiusXM, wy = double.IsNaN(el.Surface.BeamRadiusYM) ? wx : el.Surface.BeamRadiusYM;
                double eSurf = Math.Max(rec.EnergyInJ, rec.EnergyOutJ);
                rec.PeakFluenceJPerM2 = 2 * eSurf / (Math.PI * wx * wy);
                rec.PeakIntensityWPerM2 = 2 * Math.Max(rec.Out.PeakPowerW, 0) / (Math.PI * wx * wy);
            }
            if (el.Surface?.Lidt != null && !double.IsNaN(rec.PeakFluenceJPerM2))
            {
                rec.LidtFluenceJPerM2 = el.Surface.Lidt.FluenceAt(double.IsNaN(rec.Out.FwhmS) ? 1e-12 : rec.Out.FwhmS);
                rec.LidtMargin = rec.LidtFluenceJPerM2 / rec.PeakFluenceJPerM2;
                if (rec.LidtMargin < 1) res.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Element '{0}': peak fluence {1:G3} J/cm^2 exceeds the scaled LIDT {2:G3} J/cm^2.", el.Id, rec.PeakFluenceJPerM2 * 1e-4, rec.LidtFluenceJPerM2 * 1e-4));
            }
            if (rec.Out.EdgeEnergyFraction > 1e-3 && cr.Run?.Pulse?.IsCw != true)
            {
                string w = string.Format(CultureInfo.InvariantCulture, "After element '{0}' {1:P2} of the energy is near the time-window edge (wrap-around likely).", el.Id, rec.Out.EdgeEnergyFraction);
                if (!res.Warnings.Any(x => x.Contains("time-window edge"))) res.Warnings.Add(w);
            }
            if (rec.OutsideDataFraction > 1e-3)
                rec.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0:P2} of the spectral energy lies outside the sampled data range (extrapolated dispersion)", rec.OutsideDataFraction));
            res.Records.Add(rec);
            if (el.Probe) res.Snapshots.Add(Snapshot(el.Id, pulse, snapPoints));
            res.At[el.Id] = pulse;
            if (tail != null && tailKeys.Contains(el.Id)) res.TailAt[el.Id] = tail.TailField(pulse.RepRateHz);
            progress?.Invoke(el.Id, ei, count, 1.0);
            if (el.BeamOut != null)
            {
                path.Beam = el.BeamOut;
                // diffraction of a carried profile holds the Gouy phase already; a tracked beam has it only here
                if (pulse.Profile == null && el.BeamIn != null) path.PhaseRad += el.BeamOut.GouyRad - el.BeamIn.GouyRad;
            }
            path.ArrivalS = arrival; path.CumulativeLengthM = cumLen; path.CumulativeBIntegralRad = cumB; path.System = system;
            return pulse;
        }

        /// <summary>
        /// Run the run spec's instruments on the pulses recorded in <paramref name="res"/> (<c>res.At</c>: "input", element
        /// ids, "end"), apply each instrument's sampling, filter and detector, and let plugins post-process the readings.
        /// Used after a forward run and by run modes that produce the pulses differently (e.g. backward propagation).
        /// </summary>
        private static readonly HashSet<string> TailLinearInstruments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "energyMeter", "powerMeter", "spectrometer", "scope", "photodiode", "contrast", "polarimeter", "crossCorrelator" };

        public static void MeasureInstruments(CompiledRun cr, RunResult res, CancellationToken cancel = default)
        {
            // instruments (names are unique: a repeated name gets _2, _3, ...)
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ispec0 in cr.Run.Instruments)
            {
                cancel.ThrowIfCancellationRequested();
                var ispec = ispec0;
                string at = ispec.GetString("at", "end").Replace(':', '#');
                try { ispec = Instruments.InstrumentDevice.ResolveDetector(ispec, cr.Run); }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { res.Warnings.Add("Instrument " + ispec.GetString("kind", "?") + " at " + at + ": " + ex.Message); continue; }
                if (!res.At.TryGetValue(at, out var p)) { res.Warnings.Add("Instrument at \"" + at + "\": no pulse recorded there."); continue; }
                try
                {
                    var inst = Instruments.InstrumentFactory.Create(ispec);
                    var ctx = new Instruments.InstrumentContext { Pulse = p, Location = at, Run = res, Spec = ispec, Registry = cr.Registry };
                    string baseName = ispec.GetString("name", inst.Kind + (at == "end" ? "" : "_" + RunResult.Sanitize(at)));
                    string name = baseName;
                    for (int k = 2; !usedNames.Add(RunResult.Sanitize(name)); k++) name = baseName + "_" + k;
                    if (name != baseName)
                    {
                        if (ispec.Has("name")) res.Warnings.Add("Instrument name \"" + baseName + "\" is used more than once: this one is reported as \"" + name + "\".");
                        else res.Notes.Add("Instrument " + inst.Kind + " at " + at + " is reported as \"" + name + "\" (another unnamed " + inst.Kind + " is there; give each a \"name\").");
                    }
                    var outp = new Instruments.InstrumentOutput { Name = name, Kind = inst.Kind, Location = at };
                    if (res.TailLedger != null && res.TailAt.TryGetValue(at, out var tailField) && ReferenceEquals(p.Grid, res.TailLedger.Main))
                    {
                        if (TailLinearInstruments.Contains(inst.Kind))
                        {
                            ctx.Pulse = res.TailLedger.Composite(p, tailField);
                            if (inst.Kind == "crossCorrelator") ctx.GatePulse = res.TailLedger.EmbedMain(p);
                            outp.Notes.Add(inst.Kind == "crossCorrelator"
                                ? "delayed-light tail included in the probe channel; the SHG gate is made from the main field only"
                                : "delayed-light tail included (main + tail on the long grid)");
                        }
                        else outp.Notes.Add("nonlinear instrument: the delayed-light tail is not included (main field only)");
                    }
                    var sampling = ispec.GetObject("sampling");
                    if (sampling != null)
                    {
                        if (Instruments.Sampling.IsImaging(inst.Kind)) outp.Notes.Add("\"sampling\" is ignored by imaging instruments (they have their own plane: propagateM, focalLengthM)");
                        else ctx.Pulse = Instruments.Sampling.Apply(ctx, sampling, outp);
                    }
                    ctx.Pulse = Instruments.InstrumentDevice.ApplyFilter(ctx, outp);
                    if (ctx.GatePulse != null && !ReferenceEquals(ctx.GatePulse.Grid, ctx.Pulse.Grid)) { ctx.GatePulse = null; outp.Notes.Add("sampling changed the grid: the gate is made from the composite field"); }
                    inst.Measure(ctx, outp);
                    Instruments.InstrumentDevice.ApplyAxis(ispec, outp);
                    Instruments.InstrumentDevice.ApplyDetector(ispec, outp);
                    outp.Settings = Instruments.InstrumentDevice.Settings(ispec, inst.Kind);
                    res.Instruments.Add(outp);
                    // recorded data: compare the reading with the measurement (on the measured axis, its range and resolution)
                    if (ispec.TryGet("measured", out var measured) && measured != null && !measured.IsNull)
                    {
                        try { res.Comparisons.Add(Measured.Measurements.Compare(outp, measured, cr.Run?.BaseDirectory)); }
                        catch (Exception ex) when (!(ex is OperationCanceledException)) { res.Warnings.Add("Instrument " + outp.Name + ": measured data not compared: " + ex.Message); }
                    }
                    // plugins post-process the reading (e.g. compare it with recorded data)
                    var hooks = Plugins.PluginManager.Hooks;
                    foreach (var h in hooks)
                    {
                        if (!h.Keys.Any(k => ispec.Has(k))) continue;
                        try { h.AfterMeasure(new Plugins.InstrumentHookContext { Output = outp, Spec = ispec, Result = res, Compiled = cr }); }
                        catch (Exception ex) when (!(ex is OperationCanceledException)) { res.Warnings.Add("Instrument " + outp.Name + ": " + string.Join("/", h.Keys) + " not processed: " + ex.Message); }
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    res.Warnings.Add("Instrument " + ispec.GetString("kind", "?") + " at " + at + " failed: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// A grid for the elements from <paramref name="fromIndex"/> on, sized from the actual pulse (its spectrum,
        /// the remaining group-delay spread and nonlinearity) and related to the pulse's grid by powers of two, so
        /// <see cref="GridResampler"/> moves the pulse onto it exactly.
        /// </summary>
        public static FrequencyGrid PlanDownstreamGrid(CompiledRun cr, PulseField p, int fromIndex, List<string> notes)
        {
            var run = cr.Run; var g = p.Grid;
            var rest = cr.Elements.Skip(fromIndex).Where(x => x.Enabled).ToList();
            var tau = rest.Select(x => (Func<double, double>)x.GroupDelayAt).ToList();
            bool anyNl = rest.Any(x => x.Nonlinear != null || x.Active != null);
            double pk = PulseMetrics.TemporalPower(p).Max(), k0 = g.OmegaCenter / PhysicalConstants.SpeedOfLightMPerS, bEst = 0;
            foreach (var x in rest.Where(x => x.Nonlinear != null && !x.Nonlinear.Medium.ReplaceKerr))
            {
                double inv = 0; int ns = 50;
                for (int k = 0; k < ns; k++) inv += x.Nonlinear.LengthM / ns / x.Nonlinear.Beam.EffectiveArea(x.Nonlinear.LengthM * (k + 0.5) / ns);
                bEst += k0 * x.Nonlinear.Medium.N2 * pk * inv;
            }
            double margin = double.IsNaN(run.Grid.SpectralMargin) ? (anyNl ? Math.Max(2.0, 1.0 + bEst) : 1.3) : run.Grid.SpectralMargin;
            double window = g.N * g.DTime;
            double fwhmTl = PulseMetrics.Fwhm(g.Time, PulseMetrics.TransformLimitedPower(p));
            var plan = GridPlanner.Plan(p, tau, margin, anyNl ? run.Grid.MinSpanFraction : 0.0, window, g.N, run.Grid.MaxN, true, false,
                double.IsNaN(fwhmTl) ? double.PositiveInfinity : fwhmTl / run.Grid.MinPointsPerFwhm, Compiler.SatelliteRoom(rest, run.Grid.SatelliteLevel));
            double reqW = Math.Max(plan.RequiredWindowS, 1e-15), reqDt = plan.RequiredDtS;
            int a = (int)Math.Ceiling(Math.Log(reqW / window, 2) - 1e-9);
            int b = (int)Math.Floor(Math.Log(reqDt / g.DTime, 2) + 1e-9);
            // keep N within [64, maxN]
            for (int guard = 0; guard < 64; guard++)
            {
                double n = window * Math.Pow(2, a) / (g.DTime * Math.Pow(2, b));
                if (n > run.Grid.MaxN) { b++; continue; }
                if (n < 64) { a++; continue; }
                break;
            }
            if (a == 0 && b == 0) return g;
            var ng = GridResampler.Derive(g, a, b, g.OmegaCenter);
            notes?.Add(string.Format(CultureInfo.InvariantCulture, "grid re-planned for the rest of the system: N = {0}, window {1:G4} ps, dt {2:G4} fs (carrier {3:F2} nm)",
                ng.N, ng.N * ng.DTime * 1e12, ng.DTime * 1e15, 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / ng.OmegaCenter * 1e9));
            return ng;
        }

        /// <summary>Apply an element's Pre or Post terms: one by one to the main field, or, with a delayed-light ledger,
        /// exactly to main + tail (light moved between the two is booked as "delayed light (tail)" so the loss budget closes).</summary>
        private static PulseField ApplyLinear(TailLedger tail, PulseField p, List<LinearTerm> terms, OmegaAxis axis, ElementRecord rec, ref DispersionPoint own, Func<FrequencyGrid, OmegaAxis> axisOf, bool exactSteps)
        {
            if (terms.Count == 0) return p;
            if (tail == null || !ReferenceEquals(p.Grid, tail.Main) || p.Profile?.DataY != null)
            {
                if (tail != null && ReferenceEquals(p.Grid, tail.Main)) tail.PropagateTail(terms, (m, dE) => AddLoss(rec, m, dE));
                foreach (var t in terms) p = ApplyTerm(p, t, axisOf(p.Grid), rec, ref own);
                return p;
            }
            if (!exactSteps && terms.All(t => t.IsSmooth))
            {
                // smooth terms (dispersion, flat loss, bare Fresnel ...): the impulse response is short, so main and tail can be treated
                // separately -- the main field on the main grid, the tail on the long grid -- without the three long FFTs of the exact step
                tail.PropagateTail(terms, (m, dE) => AddLoss(rec, m, dE));
                foreach (var t in terms) p = ApplyTerm(p, t, axisOf(p.Grid), rec, ref own);
                tail.SeparateSteps++;
                return p;
            }
            tail.ExactSteps++;
            double e0 = p.TotalEnergyJ(), booked = 0;
            var q = tail.Apply(p, terms, (m, dE) => { AddLoss(rec, m, dE); booked += dE; });
            AddLoss(rec, "delayed light (tail)", (e0 - q.TotalEnergyJ()) - booked);
            foreach (var t in terms)
            {
                var d = t.DispersionAt(p.Grid.OmegaCenter);
                if (!double.IsNaN(d.GroupDelayS)) own += d;
                rec.Terms.Add(t.Description);
            }
            return q;
        }

        internal static void AddLoss(ElementRecord rec, string mech, double dE)
        {
            if (Math.Abs(dE) < 1e-30 || Math.Abs(dE) < 1e-12 * Math.Abs(rec.EnergyInJ)) return; // round-off
            rec.LossJ.TryGetValue(mech, out double v);
            rec.LossJ[mech] = v + dE;
        }

        internal static PulseField ApplyTerm(PulseField p, LinearTerm t, OmegaAxis axis, ElementRecord rec, ref DispersionPoint own)
        {
            var grid = p.Grid;
            double e0 = p.TotalEnergyJ();
            var h = t.Evaluate(axis);
            PulseField q;
            if (p.Profile?.DataY != null && !h.IsDiagonal)
            {
                // the x and y shapes differ: a polarisation-mixing term mixes the shapes too -> apply it to the full field
                int n = grid.N, M = p.Profile.Basis.M;
                var ex = Transverse.TransverseOps.Expand(p.Ax, grid.OmegaAbsolute, p.Profile);
                var ey = Transverse.TransverseOps.ExpandY(p.Ay, grid.OmegaAbsolute, p.Profile);
                for (int i = 0; i < n; i++)
                {
                    h.Get(i, out var xx, out var xy, out var yx, out var yy);
                    for (int m = 0; m < M; m++) { int k = m * n + i; var a = ex[k]; var b = ey[k]; ex[k] = xx * a + xy * b; ey[k] = yx * a + yy * b; }
                }
                var (bx, by) = Transverse.TransverseOps.CollapseVector(ex, ey, p.Profile.Basis, grid.OmegaAbsolute, p.Profile.Origin, out var prof);
                prof.Exact = p.Profile.Exact;
                q = new PulseField(grid, bx, by, p.RepRateHz) { Profile = prof };
            }
            else
            {
                h.Apply(p.Ax, p.Ay, out var bx, out var by);
                q = p.WithSpectrum(bx, by);
            }
            AddLoss(rec, t.Mechanism, e0 - q.TotalEnergyJ());
            var d = t.DispersionAt(grid.OmegaCenter);
            if (!double.IsNaN(d.GroupDelayS)) own += d;
            rec.Terms.Add(t.Description);
            return q;
        }

        internal static void Combine(IEnumerable<LinearTerm> terms, OmegaAxis axis, out double[] phX, out double[] ampX, out double[] phY, out double[] ampY)
        {
            int n = axis.N;
            phX = new double[n]; ampX = Enumerable.Repeat(1.0, n).ToArray(); phY = null; ampY = null;
            foreach (var t in terms)
            {
                if (!t.TryPhaseAmplitude(axis, out var px, out var ax, out var py, out var ay))
                    throw new InvalidOperationException("Term '" + t.Description + "' mixes polarisations and cannot be part of a nonlinear segment's background.");
                if (py != null && phY == null) { phY = (double[])phX.Clone(); ampY = (double[])ampX.Clone(); }
                for (int i = 0; i < n; i++)
                {
                    if (phY != null) { phY[i] += (py ?? px)[i]; ampY[i] *= (ay ?? ax)[i]; }
                    phX[i] += px[i]; ampX[i] *= ax[i];
                }
            }
        }

        private static PulseField PropagateEnvelope(CompiledRun cr, PulseField p, NonlinearSegment seg, OmegaAxis axis, double arrival, double tauEl, string id, StepStatistics stats, Action<double> prog)
        {
            var g = p.Grid; int n = g.N;
            Combine(seg.Background, axis, out var phX, out var ampX, out var phY, out var ampY);
            bool scalar = phY == null && p.Ay.All(c => c == Complex.Zero);
            var prob = new EnvelopeProblem(g, seg.Medium, seg.Beam, seg.LengthM, phX, ampX, phY, ampY, scalar, arrival, tauEl, id);
            var state = new Complex[prob.Length];
            Array.Copy(p.Ax, 0, state, 0, n);
            if (!scalar) Array.Copy(p.Ay, 0, state, n, n);
            stats.Representation = scalar ? "envelope (x only)" : "envelope (x, y)";
            var outS = Steppers.Propagate(prob, state, cr.Run.Stepper, stats, prog);
            var ax = new Complex[n]; var ay = new Complex[n];
            Array.Copy(outS, 0, ax, 0, n);
            if (!scalar) Array.Copy(outS, n, ay, 0, n);
            return p.WithSpectrum(ax, ay);
        }

        private static PulseField PropagateField(CompiledRun cr, PulseField p, NonlinearSegment seg, double arrival, double tauEl, string id, StepStatistics stats, ElementRecord rec, Action<double> prog)
        {
            var g = p.Grid; int n = g.N;
            double harmonic = cr.Run.Physics.FieldHarmonic;
            // chi(2): the drive is dealiased above 2/3 of the field band -> make room for the second harmonic
            if (seg.Medium.ExtraResponses.Any(r => r is Chi2Response)) harmonic = Math.Max(harmonic, 4.5);
            var map = new FieldGridMapping(g, harmonic);
            if (map.DroppedBins > 0) rec.Notes.Add(map.DroppedBins + " envelope bins at non-positive absolute frequency were dropped for carrier-resolved propagation");
            var pos = map.PositiveAxis();
            Combine(seg.Background, pos, out var phX, out var ampX, out _, out _);
            var prob = new FieldProblem(map, seg.Medium, seg.Beam, seg.LengthM, phX, ampX, arrival, tauEl, id) { N0 = seg.Material.IndexAt(g.OmegaCenter) };
            var e0 = map.ToField(p.Ax);
            stats.Representation = "carrier-resolved field (" + map.Field.N + " points, x" + map.Oversample + ")";
            var e1 = Steppers.Propagate(prob, e0, cr.Run.Stepper, stats, prog);
            // energy that left the envelope band (harmonics)
            // the state carries the positive frequencies (the negative half is not propagated): energy = DC + 2 x positive half
            double fieldE = 0; int half = e1.Length / 2;
            for (int j = half; j < e1.Length; j++) fieldE += (j == half ? 1 : 2) * (e1[j].Real * e1[j].Real + e1[j].Imaginary * e1[j].Imaginary);
            fieldE = fieldE / map.Field.N * map.Field.DTime;
            var ax = map.ToEnvelope(e1);
            // y component: linear only
            Complex[] ay = p.Ay;
            if (p.Ay.Any(c => c != Complex.Zero))
            {
                var axisE = OmegaAxis.FromGrid(g);
                Combine(seg.Background, axisE, out var px, out var amx, out var py, out var amy);
                ay = new Complex[n];
                for (int i = 0; i < n; i++) ay[i] = p.Ay[i] * Complex.FromPolarCoordinates((amy ?? amx)[i], (py ?? px)[i]);
            }
            var q = p.WithSpectrum(ax, ay);
            double inBand = EnergyNormalization.ComputeEnergyJFromFreqDomain(ax, new Complex[n], g);
            double outOfBand = fieldE - inBand;
            if (outOfBand > 1e-12 * fieldE) { AddLoss(rec, "harmonics (out of band)", outOfBand); rec.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0:G3} J generated outside the envelope band (harmonics) and not tracked further", outOfBand)); }
            return q;
        }

        public static double OutsideFraction(PulseField p, double lo, double hi)
        {
            var s = p.SpectralIntensity(); double tot = 0, o = 0;
            for (int i = 0; i < s.Length; i++) { tot += s[i]; double w = p.Grid.OmegaAbsolute[i]; if (w < lo || w > hi) o += s[i]; }
            return tot > 0 ? o / tot : 0;
        }

        internal static ProbeSnapshot Snapshot(string id, PulseField p, int maxPoints)
        {
            var g = p.Grid;
            var pt = PulseMetrics.TemporalPower(p);
            var sp = p.SpectralIntensity();
            var (tl, th) = PulseMetrics.SupportRange(pt, 1e-6);
            var (sl, sh) = PulseMetrics.SupportRange(sp, 1e-6);
            var s = new ProbeSnapshot { Id = id };
            s.TimeS = Crop(g.Time, tl, th, maxPoints); s.PowerW = CropMax(pt, tl, th, maxPoints);
            s.OmegaRadPerS = Crop(g.OmegaAbsolute, sl, sh, maxPoints); s.SpectrumPerOmega = CropMax(sp, sl, sh, maxPoints);
            s.WavelengthM = s.OmegaRadPerS.Select(w => 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / w).ToArray();
            var ph = p.SpectralPhaseUnwrappedX();
            s.PhaseRad = Crop(ph, sl, sh, maxPoints);
            return s;
        }

        /// <summary>Take [lo, hi], then decimate to at most max points by uniform picking.</summary>
        internal static double[] Crop(double[] a, int lo, int hi, int max)
        {
            int len = hi - lo + 1;
            if (max <= 0 || len <= max) { var r = new double[len]; Array.Copy(a, lo, r, 0, len); return r; }
            var o = new double[max];
            for (int i = 0; i < max; i++) o[i] = a[lo + (int)((long)i * (len - 1) / (max - 1))];
            return o;
        }

        /// <summary>As <see cref="Crop"/> but each output point is the max over its bin (keeps narrow peaks visible).</summary>
        internal static double[] CropMax(double[] a, int lo, int hi, int max)
        {
            int len = hi - lo + 1;
            if (max <= 0 || len <= max) { var r = new double[len]; Array.Copy(a, lo, r, 0, len); return r; }
            var o = new double[max];
            for (int i = 0; i < max; i++)
            {
                int a0 = lo + (int)((long)i * (len - 1) / (max - 1)), a1 = lo + (int)((long)(i + 1) * (len - 1) / (max - 1));
                double m = a[a0]; for (int k = a0; k <= Math.Min(a1, hi); k++) m = Math.Max(m, a[k]);
                o[i] = m;
            }
            return o;
        }
    }
}
