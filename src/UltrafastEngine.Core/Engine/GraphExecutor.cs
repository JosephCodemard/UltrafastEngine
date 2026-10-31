// GraphExecutor.cs -- runs a Branched system: the scheduler above RunElement.
//
//   partial class Executor
//     void RunGraph(ExecutionState st)     every stage in order; fills res.Output, res.At["id#port"], res.Graph
//   class GraphCache                       what elements gave, by a key of everything upstream, for studies
//     int Hits; bool Store
//
// Each element's input ports hold queues of packets. An element takes what is queued, merged where the packets are coherent
// and fit a window, runs once per packet through RunElement (the same code a Sequential system uses), and delivers each output
// to the port it is linked to. A loop repeats its stage: packets sent back to an earlier element wait in that element's queue
// until the next pass, so no edge has to be cut by hand.
//
// Design: docs/design/branching.md, sections 5 and 7.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Models;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Engine
{
    /// <summary>The outputs of elements, shared by the points of a study. The nominal run stores; the others only read,
    /// so what depends on a scanned parameter is not kept. Results are the same with and without it.</summary>
    public sealed class GraphCache
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedNode> Nodes = new System.Collections.Concurrent.ConcurrentDictionary<string, CachedNode>();
        public bool Store;
        public int Hits;
    }

    /// <summary>What one element gave.</summary>
    public sealed class CachedNode
    {
        public ElementRecord Record;
        public PulseField Pulse;
        public List<(string Port, PulseField Pulse, PathLedger Path)> Outputs;
    }

    public static partial class Executor
    {
        private const int MaxPackets = 2000;

        /// <summary>The key of each element outside loops: a hash of its JSON, what feeds it, and the run. Same key, same output.</summary>
        private static Dictionary<string, string> NodeKeys(CompiledRun cr)
        {
            var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sysJson = cr.System.ToJson(); sysJson.Remove("elements");
            var runJson = (JsonObject)JsonValue.Parse(cr.Run.Raw.ToJson(false));
            foreach (var k in new[] { "instruments", "outputs", "name" }) runJson.Remove(k);
            string global = sysJson.ToJson(false) + runJson.ToJson(false);
            foreach (var stage in cr.Graph.Stages)
            {
                if (stage.Loop != null) continue;
                string id = stage.Ids[0];
                var sources = cr.Graph.Edges.Where(e => string.Equals(e.To, id, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.ToPort, StringComparer.Ordinal).ToList();
                if (sources.Any(e => !keys.ContainsKey(e.From))) continue;
                var el = cr.System.Elements.First(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                string text = global + el.Json.ToJson(false) + string.Join("|", sources.Select(e => e.ToPort + "<" + e.FromPort + keys[e.From]));
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    keys[id] = Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)));
            }
            return keys;
        }

        /// <summary>State of one Branched run.</summary>
        private sealed class GraphRun
        {
            public readonly Dictionary<string, List<BeamPacket>> Queues = new Dictionary<string, List<BeamPacket>>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, ElementRecord> Records = new Dictionary<string, ElementRecord>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, CompiledElement> Elements = new Dictionary<string, CompiledElement>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, int> Index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, List<BeamPacket>> Outputs = new Dictionary<string, List<BeamPacket>>(StringComparer.OrdinalIgnoreCase);
            /// <summary>What left the current loop on this pass, so a steady-state loop can keep the last pass only.</summary>
            public List<(List<BeamPacket> queue, BeamPacket packet)> LeftLoop;
            public HashSet<string> LoopMembers;
            public Dictionary<string, string> Keys;
            /// <summary>What the running element delivered, to store in the cache.</summary>
            public List<(string Port, PulseField Pulse, PathLedger Path)> Delivered;
            public bool Cw => Merge.Cw;
            public MergeContext Merge;
            public double InputEnergyJ;
            public List<BeamPacket> Queue(string id, string port)
            {
                string key = id + "#" + port;
                if (!Queues.TryGetValue(key, out var q)) Queues[key] = q = new List<BeamPacket>();
                return q;
            }
        }

        private static void RunGraph(ExecutionState st)
        {
            var cr = st.Cr; var res = st.Res; var graph = cr.Graph;
            var run = new GraphRun { Merge = new MergeContext { Cw = cr.Run?.Pulse?.IsCw == true, Warnings = res.Warnings } };
            for (int i = 0; i < cr.Elements.Count; i++) { run.Elements[cr.Elements[i].Id] = cr.Elements[i]; run.Index[cr.Elements[i].Id] = i; }

            var firstEl = cr.System.Elements[0];
            run.Queue(firstEl.Id, Ports.Inputs(firstEl)[0]).Add(new BeamPacket { Pulse = cr.Input, Path = new PathLedger { Beam = cr.BeamAtInput, Tail = st.Tail } });
            st.Tail = null;
            run.InputEnergyJ = cr.Input.TotalEnergyJ();
            foreach (var el in cr.Elements)
                foreach (var kv in el.VirtualInputs)
                {
                    var p = kv.Value.Spec.Build(cr.Grid, 0, res.Notes);
                    run.InputEnergyJ += p.TotalEnergyJ();
                    string source = kv.Value.CoherentWith == null ? el.Id + "#" + kv.Key : kv.Value.CoherentWith;
                    run.Queue(el.Id, kv.Key).Add(new BeamPacket { Pulse = p, Path = new PathLedger { SourceId = source, PhaseRad = kv.Value.PhaseRad } });
                }

            if (cr.GraphCache != null) run.Keys = NodeKeys(cr);
            foreach (var stage in graph.Stages)
            {
                st.Cancel.ThrowIfCancellationRequested();
                if (stage.Loop != null) RunLoop(st, run, stage.Loop);
                else RunNode(st, run, stage.Ids[0]);
            }

            var end = Packets.Merge(run.Outputs.TryGetValue(SystemGraph.End, out var e) ? e : new List<BeamPacket>(), run.Merge);
            var main = Packets.Strongest(end);
            res.Output = main?.Pulse ?? cr.Input.WithSpectrum(new Complex[cr.Input.Grid.N], new Complex[cr.Input.Grid.N]);
            res.At["end"] = res.Output;
            var lastRecord = res.Records.LastOrDefault();
            var extra = end.Where(p => !ReferenceEquals(p, main)).ToList();
            if (extra.Count > 0)
            {
                res.Notes.Add(string.Format(CultureInfo.InvariantCulture, "\"end\" holds {0} packets that do not fit one time window: the output field is the strongest, and graph.energyOutJ counts them all.", end.Count));
                if (lastRecord != null) AddLoss(lastRecord, "other packets at end", extra.Sum(p => p.EnergyJ));
            }
            var pathOut = main?.Path ?? new PathLedger();
            res.FinalArrivalS = pathOut.ArrivalS; res.FinalSystem = pathOut.System;

            res.TailLedger = main?.Path.Tail;
            if (main?.Path.Tail != null) res.TailAt["end"] = main.Path.Tail.TailField(res.Output.RepRateHz);
            var ports = new JsonArray();
            foreach (var kv in run.Outputs.Where(x => x.Key != SystemGraph.End).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var merged = Packets.Merge(kv.Value, run.Merge);
                double energy = merged.Sum(p => p.EnergyJ);
                string id = kv.Key.Substring(0, kv.Key.IndexOf('#')), port = kv.Key.Substring(kv.Key.IndexOf('#') + 1);
                if (run.Records.TryGetValue(id, out var rec)) AddLoss(rec, "virtual output " + port, energy);
                var strongest = Packets.Strongest(merged);
                if (strongest != null) res.At[kv.Key] = strongest.Pulse;
                ports.Add(new JsonObject().Set("port", id + ":" + port).Set("energyJ", energy).Set("packets", merged.Count));
            }
            if (run.Merge.MismatchJ > 0 && lastRecord != null) AddLoss(lastRecord, "mode mismatch", run.Merge.MismatchJ);
            // light at `end` in other packets is booked on the record, so the field's budget closes, but it did leave the system
            double others = extra.Sum(p => p.EnergyJ), outJ = res.Output.TotalEnergyJ() + others, lost = res.Records.Sum(r => r.LossJ.Values.Sum()) - others;
            res.Graph = graph.ToJson()
                .Set("virtualOutputEnergies", ports)
                .Set("energyInJ", run.InputEnergyJ).Set("energyOutJ", outJ).Set("energyBookedJ", lost).Set("packetsAtEnd", end.Count)
                .Set("energyClosureJ", run.InputEnergyJ - outJ - lost)
                .Set("pathPhaseRad", pathOut.PhaseRad).Set("phaseKnown", pathOut.PhaseKnown);
        }

        private static void RunNode(ExecutionState st, GraphRun run, string id)
        {
            var cache = st.Cr.GraphCache;
            string key = null;
            if (cache == null || run.Keys == null || !run.Keys.TryGetValue(id, out key)) { RunNodeOnce(st, run, id); return; }
            if (cache.Nodes.TryGetValue(key, out var hit))
            {
                System.Threading.Interlocked.Increment(ref cache.Hits);
                foreach (var q in run.Queues.Where(q => q.Key.StartsWith(id + "#", StringComparison.OrdinalIgnoreCase))) q.Value.Clear();
                var rec = hit.Record.Copy();
                st.Res.Records.Add(rec); run.Records[id] = rec;
                st.Res.At[id] = hit.Pulse;
                foreach (var (port, pulse, path) in hit.Outputs) Deliver(run, st, id, port, pulse, path.Clone());
                return;
            }
            run.Delivered = new List<(string, PulseField, PathLedger)>();
            RunNodeOnce(st, run, id);
            if (cache.Store && run.Records.TryGetValue(id, out var made))
                cache.Nodes[key] = new CachedNode { Record = made.Copy(), Pulse = st.Res.At.TryGetValue(id, out var p) ? p : null, Outputs = run.Delivered };
            run.Delivered = null;
        }

        private static void RunNodeOnce(ExecutionState st, GraphRun run, string id)
        {
            var el = run.Elements[id];
            var sys = st.Cr.System.Elements.First(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            string[] inputs = Ports.Inputs(sys), outputs = Ports.Outputs(sys);
            // beams a crystal takes from its ports meet the main beam inside it, so they run together with it
            var fed = (el.Active as Crystals.CrystalElement)?.Aux.Where(a => a.FromPort).ToList() ?? new List<AuxBeamSpec>();
            var fedPackets = new Dictionary<AuxBeamSpec, BeamPacket>();
            double fedLeftOver = 0;
            foreach (var a in fed)
            {
                var waiting = Packets.Merge(run.Queue(id, a.Role), run.Merge);
                var strongest = Packets.Strongest(waiting);
                run.Queue(id, a.Role).Clear();
                if (strongest == null) throw new ModelException("Element '" + id + "': no light reached port '" + a.Role + "'.");
                fedPackets[a] = strongest;
                fedLeftOver += waiting.Where(q => !ReferenceEquals(q, strongest)).Sum(q => q.EnergyJ);
            }
            foreach (var port in inputs)
            {
                if (fed.Any(a => a.Role == port)) continue;
                var queue = run.Queue(id, port);
                if (queue.Count == 0) continue;
                var packets = Packets.Merge(queue, run.Merge);
                queue.Clear();
                if (fed.Count > 0 && packets.Count > 1)
                {
                    // the fed beams overlap one main packet: running each would use their energy twice
                    var main = Packets.Strongest(packets);
                    fedLeftOver += packets.Where(q => !ReferenceEquals(q, main)).Sum(q => q.EnergyJ);
                    packets = new List<BeamPacket> { main };
                }
                foreach (var packet in packets)
                {
                    if (!el.Enabled) { Deliver(run, st, id, outputs[0], packet.Pulse, packet.Path); continue; }
                    var path = packet.Path.Clone();
                    st.InputPort = port;
                    foreach (var kv in fedPackets)
                    {
                        kv.Key.Supplied = kv.Value.Pulse;
                        kv.Key.DelayS = kv.Value.Path.FrameDelayS - packet.Path.FrameDelayS;
                        kv.Key.SuppliedPhaseRad = kv.Value.Path.PhaseRad - packet.Path.PhaseRad;
                    }
                    st.Tail = path.Tail;
                    var outPulse = RunElement(st, run.Index[id], el, packet.Pulse, path);
                    path.Tail = st.Tail; st.Tail = null;
                    // the crystal books an aux beam's energy as a gain; here the budget already counts it at its source
                    foreach (var kv in fedPackets) AddLoss(st.Res.Records[st.Res.Records.Count - 1], "port " + kv.Key.Role + " input", kv.Value.EnergyJ);
                    if (fedLeftOver > 0)
                    {
                        AddLoss(st.Res.Records[st.Res.Records.Count - 1], "packets not overlapping in crystal", fedLeftOver);
                        st.Res.Warnings.Add("Element '" + id + "': separate packets reached the crystal; only the strongest on each port interact, and the rest are booked.");
                    }
                    string keep = (el.Active as IMultiPortElement)?.ContinuingPort ?? outputs[0];
                    var outs = st.LastOutputs;
                    MergeRecord(st.Res, run, id);
                    var original = path.Tail?.Clone();
                    if (el.Active is BeamSplitterElement kept && path.Tail != null) ScaleTail(path.Tail, kept, port, keep);
                    Deliver(run, st, id, keep, outPulse, path);
                    path = path.Clone(); path.Tail = original;
                    if (outs != null)
                        foreach (var kv in outs)
                        {
                            if (!outputs.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) continue;
                            var side = path.Clone();
                            if (el.Active is BeamSplitterElement bs && side.Tail != null) ScaleTail(side.Tail, bs, port, kv.Key);
                            if (el.Active is Crystals.CrystalElement)
                            {
                                // a crystal books its by-products itself: an unlinked one stays booked, a linked one continues
                                if (st.Cr.Graph.EdgeFrom(id, kv.Key) == null) continue;
                                AddLoss(run.Records[id], "port " + kv.Key + " output", -kv.Value.TotalEnergyJ());
                            }
                            Deliver(run, st, id, kv.Key, kv.Value, side);
                        }
                }
            }
        }

        private static void ScaleTail(TailLedger tail, BeamSplitterElement bs, string inPort, string outPort)
        {
            tail.MultiplyPolarized(w => bs.Coefficient(inPort, outPort, w));
        }

        /// <summary>An element that ran more than once has one record: its energies and losses are the sums.</summary>
        private static void MergeRecord(RunResult res, GraphRun run, string id)
        {
            var rec = res.Records[res.Records.Count - 1];
            if (!run.Records.TryGetValue(id, out var first)) { run.Records[id] = rec; return; }
            res.Records.RemoveAt(res.Records.Count - 1);
            first.EnergyInJ += rec.EnergyInJ; first.EnergyOutJ += rec.EnergyOutJ;
            foreach (var kv in rec.LossJ) AddLoss(first, kv.Key, kv.Value);
            first.Values["runs"] = (first.Values.TryGetValue("runs", out double runs) ? runs : 1) + 1;
        }

        private static void Deliver(GraphRun run, ExecutionState st, string id, string port, PulseField pulse, PathLedger path)
        {
            var packet = new BeamPacket { Pulse = pulse, Path = path };
            run.Delivered?.Add((port, pulse, path.Clone()));
            st.Res.At[id + "#" + port] = pulse;
            var edge = st.Cr.Graph.EdgeFrom(id, port);
            List<BeamPacket> target;
            bool leaves = true;
            if (edge == null || edge.ToEnd)
            {
                string key = edge == null ? id + "#" + port : SystemGraph.End;
                if (!run.Outputs.TryGetValue(key, out target)) run.Outputs[key] = target = new List<BeamPacket>();
            }
            else
            {
                target = run.Queue(edge.To, edge.ToPort);
                leaves = run.LoopMembers == null || !run.LoopMembers.Contains(edge.To);
            }
            target.Add(packet);
            if (run.LeftLoop != null && leaves) run.LeftLoop.Add((target, packet));
            if (target.Count > MaxPackets) throw new ModelException("More than " + MaxPackets + " packets are waiting at '" + id + "': too many separate pulses. Lower loop.maxRoundTrips or raise loop.tolerance.");
        }

        private static void RunLoop(ExecutionState st, GraphRun run, GraphLoop loop)
        {
            var res = st.Res;
            run.LoopMembers = new HashSet<string>(loop.Members, StringComparer.OrdinalIgnoreCase);
            var inLoop = run.Queues.Where(q => run.LoopMembers.Contains(q.Key.Substring(0, q.Key.IndexOf('#')))).SelectMany(q => q.Value);
            double entering = inLoop.Sum(p => p.EnergyJ);
            bool steady = loop.Kind == "steadyState", settled = false;
            int pass = 0;
            (Complex[] X, Complex[] Y)? previous = null;
            double discarded = 0, previousEnergy = 0, lastEnergy = 0;
            for (; pass < loop.MaxRoundTrips && !settled; pass++)
            {
                st.Cancel.ThrowIfCancellationRequested();
                if (steady && run.LeftLoop != null)
                    foreach (var (queue, packet) in run.LeftLoop) { discarded += packet.EnergyJ; queue.Remove(packet); }
                run.LeftLoop = new List<(List<BeamPacket>, BeamPacket)>();
                foreach (var id in loop.Members) RunNode(st, run, id);
                var circulating = run.Queues.Where(q => run.LoopMembers.Contains(q.Key.Substring(0, q.Key.IndexOf('#')))).SelectMany(q => q.Value).ToList();
                double energy = circulating.Sum(p => p.EnergyJ);
                lastEnergy = energy;
                if (energy <= 0) settled = true;
                else if (steady)
                {
                    // a passive loop keeps its shape from the first pass while it decays: the energy has to repeat too
                    var strongest = Packets.Strongest(circulating).Pulse;
                    var field = (strongest.Ax, strongest.Ay);
                    if (previous != null && Mismatch(previous.Value.X, previous.Value.Y, field.Ax, field.Ay) < loop.Tolerance
                        && Math.Abs(energy - previousEnergy) <= loop.Tolerance * energy) settled = true;
                    previous = field; previousEnergy = energy;
                }
                else if (energy < loop.Tolerance * entering) settled = true;
            }
            double left = 0;
            foreach (var q in run.Queues.Where(q => run.LoopMembers.Contains(q.Key.Substring(0, q.Key.IndexOf('#'))))) { left += q.Value.Sum(p => p.EnergyJ); q.Value.Clear(); }
            var entry = run.Records.TryGetValue(loop.Entry, out var r) ? r : null;
            if (entry != null)
            {
                if (left > 0) AddLoss(entry, "left in loop", left);
                if (discarded > 0) AddLoss(entry, "earlier passes", discarded);
                entry.Values["roundTrips"] = pass; entry.Values["loopConverged"] = settled ? 1 : 0;
            }
            if (!settled)
                res.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "The loop entered at '{0}' did not settle in {1} round trips: raise loop.maxRoundTrips or loop.tolerance.{2}", loop.Entry, pass,
                    steady && lastEnergy < 0.5 * entering ? " The light is decaying: a loop with no gain has no steady state after one input, so use \"ringDown\"." : ""));
            run.LoopMembers = null; run.LeftLoop = null;
        }

        /// <summary>1 - |overlap|^2 of two scalar fields.</summary>
        internal static double Mismatch(Complex[] a, Complex[] b) { var zero = new Complex[a.Length]; return Mismatch(a, zero, b, zero); }

        /// <summary>1 - |overlap|^2 of two Jones fields: zero when they have the same shape, whatever their size and phase.</summary>
        internal static double Mismatch(Complex[] ax, Complex[] ay, Complex[] bx, Complex[] by)
        {
            Complex overlap = Complex.Zero; double na = 0, nb = 0;
            for (int i = 0; i < ax.Length; i++)
            {
                overlap += Complex.Conjugate(ax[i]) * bx[i] + Complex.Conjugate(ay[i]) * by[i];
                na += ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary + ay[i].Real * ay[i].Real + ay[i].Imaginary * ay[i].Imaginary;
                nb += bx[i].Real * bx[i].Real + bx[i].Imaginary * bx[i].Imaginary + by[i].Real * by[i].Real + by[i].Imaginary * by[i].Imaginary;
            }
            return na > 0 && nb > 0 ? 1 - (overlap.Real * overlap.Real + overlap.Imaginary * overlap.Imaginary) / (na * nb) : 1;
        }
    }
}
