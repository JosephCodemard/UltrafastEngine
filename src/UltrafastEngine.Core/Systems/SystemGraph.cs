// SystemGraph.cs -- the connected form of a system: which port feeds which, in what order, and what is wrong with it.
//
// Every system compiles to one of these. A Sequential file gives a chain. A Branched file ("topology": "branched") states
// every link with `next`. Nothing here knows any physics, so a system can be checked without running it.
//
//   class GraphEdge    From, FromPort -> To, ToPort                      To is an element id, or End
//   class GraphLoop    Entry, Members, Kind, MaxRoundTrips, Tolerance    one strongly connected component
//   class GraphStage   Ids, Loop                                         one element, or all the elements of a loop
//   class SystemGraph
//     static SystemGraph Build(SystemDescription system)                 parse, connect, verify; throws JsonException on errors
//     string Topology; List<GraphStage> Stages; List<GraphEdge> Edges; List<string> Warnings; List<string> VirtualOutputs
//     GraphEdge EdgeFrom(string id, string port)                         null if the port is a virtual output
//     string ToText(); string ToMermaid(); JsonObject ToJson()
//   static class Ports
//     string[] Inputs(SystemElement e), Outputs(SystemElement e)         declared in Schema/BuiltIn*.cs (default: in, out)
//
// Design: docs/design/branching.md, sections 2 and 3. Reference: docs/reference/branches.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UltrafastEngine.Schema;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Systems
{
    /// <summary>A link from an output port to an input port.</summary>
    public sealed class GraphEdge
    {
        public string From, FromPort, To, ToPort;
        public bool ToEnd => To == SystemGraph.End;
    }

    /// <summary>A cycle of the graph: the elements light can circulate through, and how to run them.</summary>
    public sealed class GraphLoop
    {
        public string Entry, Kind = "ringDown";
        public List<string> Members = new List<string>();
        public int MaxRoundTrips = 1000;
        public double Tolerance = 1e-6;
    }

    /// <summary>One step of the execution order: one element, or all the elements of a loop.</summary>
    public sealed class GraphStage
    {
        public List<string> Ids = new List<string>();
        public GraphLoop Loop;
    }

    /// <summary>Beam ports of an element type, from the schema.</summary>
    public static class Ports
    {
        private static readonly string[] In = { "in" }, Out = { "out" };
        public static string[] Inputs(SystemElement e)
        {
            var declared = OptionRegistry.Get("element", e.Type)?.InputPorts ?? In;
            var aux = e.Json.GetArray("aux");
            if (aux == null) return declared;
            return declared.Concat(aux.Select(a => a.AsObject()).Where(a => a.GetBool("fromPort", false)).Select(a => a.GetString("role", "pump").ToLowerInvariant())).ToArray();
        }

        public static string[] Outputs(SystemElement e)
        {
            if (!string.Equals(e.Type, "crystal", StringComparison.OrdinalIgnoreCase)) return OptionRegistry.Get("element", e.Type)?.OutputPorts ?? Out;
            // the wave named by "output" is `out`; the other waves are ports of their own
            string process = e.Json.GetObject("interaction")?.GetString("process", "opa")?.ToLowerInvariant();
            var waves = process == "shg" ? new[] { "fundamental", "sh" } : process == "sfg" ? new[] { "a", "b", "sum" } : new[] { "signal", "idler", "pump" };
            string main = e.Json.GetString("output", process == "shg" ? "sh" : process == "sfg" ? "sum" : "signal");
            return new[] { "out" }.Concat(waves.Where(w => !Same2(w, main))).ToArray();
        }

        private static bool Same2(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The connected form of a system (see the file header).</summary>
    public sealed class SystemGraph
    {
        public const string End = "end", Virtual = "virtual", Sequential = "sequential", Branched = "branched";

        public string Topology = Sequential;
        public List<GraphStage> Stages { get; } = new List<GraphStage>();
        public List<GraphEdge> Edges { get; } = new List<GraphEdge>();
        public List<string> Warnings { get; } = new List<string>();
        /// <summary>Output ports with no target, as "id#port".</summary>
        public List<string> VirtualOutputs { get; } = new List<string>();
        /// <summary>Input ports with no source, as "id#port".</summary>
        public List<string> VirtualInputs { get; } = new List<string>();
        public string FirstId;
        public bool IsBranched => Topology == Branched;

        public GraphEdge EdgeFrom(string id, string port) =>
            Edges.FirstOrDefault(x => Same(x.From, id) && Same(x.FromPort, port));

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>Connect and verify a system. Errors throw <see cref="JsonException"/>, all at once; alerts go to <see cref="Warnings"/>.</summary>
        public static SystemGraph Build(SystemDescription system)
        {
            var g = new SystemGraph { Topology = system.Topology ?? Sequential };
            if (g.Topology != Sequential && g.Topology != Branched)
                throw new JsonException("topology must be \"sequential\" or \"branched\", not \"" + g.Topology + "\".");
            if (system.Elements.Count == 0)
            {
                if (g.IsBranched) throw new JsonException("A branched system needs elements.");
                return g;
            }
            g.FirstId = system.Elements[0].Id;
            if (g.IsBranched) g.BuildBranched(system);
            else g.BuildChain(system);
            return g;
        }

        private void BuildChain(SystemDescription system)
        {
            foreach (var e in system.Elements)
            {
                foreach (var key in new[] { "next", "loop" })
                    if (e.Json.Has(key)) throw new JsonException("Element '" + e.Id + "' has \"" + key + "\": this needs \"topology\": \"branched\".");
                Stages.Add(new GraphStage { Ids = { e.Id } });
            }
        }

        private void BuildBranched(SystemDescription system)
        {
            var errors = new List<string>();
            var byId = new Dictionary<string, SystemElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in system.Elements)
            {
                if (byId.ContainsKey(e.Id)) errors.Add("element id '" + e.Id + "' is used twice");
                else byId[e.Id] = e;
            }
            if (errors.Count == 0)
            {
                foreach (var e in system.Elements) ReadLinks(e, byId, errors);
                CheckInputs(system, byId, errors);
                if (errors.Count == 0) Order(system, byId, errors);
            }
            if (errors.Count > 0) throw new JsonException("The branched system has errors:\n  " + string.Join("\n  ", errors));
        }

        private void ReadLinks(SystemElement e, Dictionary<string, SystemElement> byId, List<string> errors)
        {
            var outputs = Ports.Outputs(e);
            var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Link(string port, JsonValue target)
            {
                if (!ParseTarget(target, byId, out string to, out string toPort, out string problem)) { errors.Add("element '" + e.Id + "': " + problem); return; }
                linked.Add(port);
                if (to == Virtual) VirtualOutputs.Add(e.Id + "#" + port);
                else Edges.Add(new GraphEdge { From = e.Id, FromPort = port, To = to, ToPort = toPort });
            }
            if (e.Json.TryGet("next", out var next))
            {
                if (next.Kind == JsonKind.Object && !next.AsObject().Has("id"))
                {
                    foreach (var kv in next.AsObject())
                    {
                        string port = ResolvePort(kv.Key, outputs);
                        if (port == null) errors.Add("element '" + e.Id + "' has no output port '" + kv.Key + "' (ports: " + string.Join(", ", outputs) + ")");
                        else Link(port, kv.Value);
                    }
                }
                else Link(outputs[0], next);
            }
            foreach (var port in outputs.Where(p => !linked.Contains(p)))
            {
                VirtualOutputs.Add(e.Id + "#" + port);
                Warnings.Add("Unused output port: element '" + e.Id + "' port '" + port + "' goes nowhere. A virtual output was added; its light is recorded and booked. Give it a \"next\", or \"virtual\" to silence this.");
            }
        }

        /// <summary>A port by name or by number (from 1, in declared order); null if there is none.</summary>
        private static string ResolvePort(string text, string[] ports)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return n >= 1 && n <= ports.Length ? ports[n - 1] : null;
            return ports.FirstOrDefault(p => Same(p, text));
        }

        private static bool ParseTarget(JsonValue v, Dictionary<string, SystemElement> byId, out string to, out string toPort, out string problem)
        {
            to = toPort = problem = null;
            string id, port = null;
            if (v.Kind == JsonKind.String)
            {
                string s = v.AsString();
                int c = s.IndexOf(':');
                id = c < 0 ? s : s.Substring(0, c);
                if (c >= 0) port = s.Substring(c + 1);
            }
            else if (v.Kind == JsonKind.Object && v.AsObject().Has("id"))
            {
                var o = v.AsObject();
                id = o.GetString("id");
                if (o.TryGet("port", out var pv)) port = pv.Kind == JsonKind.String ? pv.AsString() : pv.AsDouble().ToString("G", CultureInfo.InvariantCulture);
            }
            else { problem = "a \"next\" target is a string such as \"bs1:1\", or {\"id\": \"bs1\", \"port\": 1}"; return false; }
            if (Same(id, End)) { to = End; return true; }
            if (Same(id, Virtual)) { to = Virtual; return true; }
            if (!byId.TryGetValue(id, out var target)) { problem = "\"next\" names '" + id + "', which is not an element id"; return false; }
            var inputs = Ports.Inputs(target);
            to = target.Id;
            toPort = port == null ? inputs[0] : ResolvePort(port, inputs);
            if (toPort == null) { problem = "\"next\" names input port '" + port + "' of '" + id + "', which has ports " + string.Join(", ", inputs); return false; }
            return true;
        }

        private void CheckInputs(SystemDescription system, Dictionary<string, SystemElement> byId, List<string> errors)
        {
            var first = system.Elements[0];
            foreach (var e in system.Elements)
            {
                var inputs = Ports.Inputs(e);
                var declared = e.Json.GetObject("inputs") ?? new JsonObject();
                foreach (var kv in declared)
                    if (ResolvePort(kv.Key, inputs) == null) errors.Add("element '" + e.Id + "' has no input port '" + kv.Key + "' (ports: " + string.Join(", ", inputs) + ")");
                foreach (var port in inputs)
                {
                    var sources = Edges.Where(x => Same(x.To, e.Id) && Same(x.ToPort, port)).ToList();
                    bool isRunInput = ReferenceEquals(e, first) && port == inputs[0];
                    JsonObject virt = VirtualSpec(declared, inputs, port);
                    int count = sources.Count + (isRunInput ? 1 : 0) + (virt != null && !virt.GetBool("virtual", false) ? 1 : 0);
                    string names = string.Join(", ", sources.Select(x => x.From + ":" + x.FromPort).Concat(isRunInput ? new[] { "the run's input" } : new string[0]).Concat(virt != null && !virt.GetBool("virtual", false) ? new[] { "a virtual beam" } : new string[0]));
                    if (count > 1) errors.Add("input port '" + port + "' of element '" + e.Id + "' has " + count + " sources (" + names + "): a port takes one. Beams meet at an element with two input ports.");
                    if (count == 0)
                    {
                        VirtualInputs.Add(e.Id + "#" + port);
                        if (virt == null) Warnings.Add("Unused input port: element '" + e.Id + "' port '" + port + "' has no source. A virtual input was added: no light enters there. Declare \"inputs\": {\"" + port + "\": {\"virtual\": true}} to silence this.");
                    }
                }
            }
            if (Edges.Count(x => x.ToEnd) > 1) errors.Add("\"end\" has " + Edges.Count(x => x.ToEnd) + " sources (" + string.Join(", ", Edges.Where(x => x.ToEnd).Select(x => x.From + ":" + x.FromPort)) + "): the system has one output; use \"virtual\" for the others");
            if (!Edges.Any(x => x.ToEnd)) Warnings.Add("No element leads to \"end\": the system has no main output, and \"end\" is empty.");
        }

        private static JsonObject VirtualSpec(JsonObject declared, string[] inputs, string port)
        {
            foreach (var kv in declared)
                if (Same(ResolvePort(kv.Key, inputs), port)) return kv.Value.AsObject();
            return null;
        }

        private void Order(SystemDescription system, Dictionary<string, SystemElement> byId, List<string> errors)
        {
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < system.Elements.Count; i++) index[system.Elements[i].Id] = i;
            int n = system.Elements.Count;
            var adj = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
            foreach (var x in Edges.Where(x => !x.ToEnd)) adj[index[x.From]].Add(index[x.To]);

            // reachability from the run's input and from virtual beams
            var seen = new bool[n];
            var stack = new Stack<int>();
            void Root(int i) { if (!seen[i]) { seen[i] = true; stack.Push(i); } }
            Root(0);
            for (int i = 0; i < n; i++)
            {
                var d = system.Elements[i].Json.GetObject("inputs");
                if (d != null && d.Any(kv => !kv.Value.AsObject().GetBool("virtual", false))) Root(i);
            }
            while (stack.Count > 0) foreach (int j in adj[stack.Pop()]) Root(j);
            for (int i = 0; i < n; i++)
                if (!seen[i]) errors.Add("element '" + system.Elements[i].Id + "' is unreachable: no light gets there");
            if (errors.Count > 0) return;

            int[] comp = StronglyConnected(adj, out int nComp);
            var members = Enumerable.Range(0, nComp).Select(_ => new List<int>()).ToArray();
            for (int i = 0; i < n; i++) members[comp[i]].Add(i);
            var indeg = new int[nComp];
            var compAdj = Enumerable.Range(0, nComp).Select(_ => new HashSet<int>()).ToArray();
            for (int i = 0; i < n; i++)
                foreach (int j in adj[i])
                    if (comp[i] != comp[j] && compAdj[comp[i]].Add(comp[j])) indeg[comp[j]]++;
            var ready = new SortedSet<(int first, int c)>();
            for (int c = 0; c < nComp; c++) if (indeg[c] == 0) ready.Add((members[c].Min(), c));
            while (ready.Count > 0)
            {
                var head = ready.Min; ready.Remove(head);
                int c = head.c;
                bool cyclic = members[c].Count > 1 || adj[members[c][0]].Contains(members[c][0]);
                Stages.Add(cyclic ? LoopStage(system, members[c], adj, comp, errors) : new GraphStage { Ids = { system.Elements[members[c][0]].Id } });
                foreach (int d in compAdj[c]) if (--indeg[d] == 0) ready.Add((members[d].Min(), d));
            }
            foreach (var e in system.Elements.Where(x => x.Json.Has("loop")))
                if (!Stages.Any(s => s.Loop != null && s.Loop.Members.Contains(e.Id, StringComparer.OrdinalIgnoreCase)))
                    errors.Add("element '" + e.Id + "' has \"loop\" but is on no loop");
        }

        private GraphStage LoopStage(SystemDescription system, List<int> members, List<int>[] adj, int[] comp, List<string> errors)
        {
            string names = string.Join(", ", members.Select(i => system.Elements[i].Id));
            var withLoop = members.Where(i => system.Elements[i].Json.Has("loop")).ToList();
            if (withLoop.Count != 1)
            {
                errors.Add("the loop through " + names + (withLoop.Count == 0 ? " has no settings: give the element where light enters it a \"loop\" object" : " has more than one \"loop\" object"));
                return new GraphStage();
            }
            var entry = system.Elements[withLoop[0]];
            var o = entry.Json.GetObject("loop");
            var loop = new GraphLoop { Entry = entry.Id, Kind = o.GetString("kind", "ringDown"), MaxRoundTrips = o.GetInt("maxRoundTrips", 1000), Tolerance = o.GetDouble("tolerance", 1e-6) };
            if (loop.Kind != "ringDown" && loop.Kind != "steadyState") errors.Add("loop kind must be \"ringDown\" or \"steadyState\", not \"" + loop.Kind + "\"");
            bool exit = members.Any(i => adj[i].Any(j => comp[j] != comp[i]))
                || Edges.Any(x => x.ToEnd && members.Any(i => Same(system.Elements[i].Id, x.From)))
                || VirtualOutputs.Any(p => members.Any(i => p.StartsWith(system.Elements[i].Id + "#", StringComparison.OrdinalIgnoreCase)));
            if (!exit) errors.Add("the loop through " + names + " has no way out: light could never leave it");
            // walk from the entry, so a pass meets the elements in the order the light does
            var seen = new HashSet<int> { withLoop[0] };
            var queue = new Queue<int>(); queue.Enqueue(withLoop[0]);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                loop.Members.Add(system.Elements[i].Id);
                foreach (int j in adj[i]) if (comp[j] == comp[i] && seen.Add(j)) queue.Enqueue(j);
            }
            return new GraphStage { Ids = new List<string>(loop.Members), Loop = loop };
        }

        /// <summary>Tarjan's algorithm, iterative so a long chain cannot overflow the stack.</summary>
        private static int[] StronglyConnected(List<int>[] adj, out int count)
        {
            int n = adj.Length, counter = 0, comps = 0;
            int[] low = new int[n], num = new int[n], comp = new int[n], pos = new int[n];
            for (int i = 0; i < n; i++) { num[i] = -1; comp[i] = -1; }
            var stack = new Stack<int>(); var onStack = new bool[n]; var call = new Stack<int>();
            for (int s = 0; s < n; s++)
            {
                if (num[s] >= 0) continue;
                call.Push(s);
                while (call.Count > 0)
                {
                    int v = call.Peek();
                    if (num[v] < 0) { num[v] = low[v] = counter++; stack.Push(v); onStack[v] = true; }
                    if (pos[v] < adj[v].Count)
                    {
                        int w = adj[v][pos[v]++];
                        if (num[w] < 0) call.Push(w);
                        else if (onStack[w]) low[v] = Math.Min(low[v], num[w]);
                        continue;
                    }
                    call.Pop();
                    if (call.Count > 0) low[call.Peek()] = Math.Min(low[call.Peek()], low[v]);
                    if (low[v] == num[v])
                    {
                        int w;
                        do { w = stack.Pop(); onStack[w] = false; comp[w] = comps; } while (w != v);
                        comps++;
                    }
                }
            }
            count = comps;
            return comp;
        }

        /// <summary>The graph as lines of text, for `ultrafast validate --graph`.</summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("topology: " + Topology);
            foreach (var st in Stages)
                sb.AppendLine(st.Loop != null ? "loop (" + st.Loop.Kind + ", enters at " + st.Loop.Entry + "): " + string.Join(" -> ", st.Ids) : "element: " + st.Ids[0]);
            foreach (var e in Edges) sb.AppendLine("link: " + e.From + ":" + e.FromPort + " -> " + (e.ToEnd ? End : e.To + ":" + e.ToPort));
            foreach (var p in VirtualInputs) sb.AppendLine("virtual input: " + p.Replace('#', ':'));
            foreach (var p in VirtualOutputs) sb.AppendLine("virtual output: " + p.Replace('#', ':'));
            return sb.ToString();
        }

        /// <summary>The graph as a Mermaid flowchart, for documents.</summary>
        public string ToMermaid()
        {
            var sb = new StringBuilder("flowchart LR\n");
            if (FirstId != null) sb.AppendLine("  input([input]) --> " + Node(FirstId));
            foreach (var e in Edges) sb.AppendLine("  " + Node(e.From) + " -- " + e.FromPort + " --> " + (e.ToEnd ? "finish([end])" : Node(e.To)));
            return sb.ToString();
        }

        private static string Node(string id) => "n_" + new string(id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        private static JsonArray Strings(IEnumerable<string> items) => new JsonArray(items.Select(x => (JsonValue)x));

        public JsonObject ToJson()
        {
            var edges = new JsonArray();
            foreach (var e in Edges) edges.Add(new JsonObject().Set("from", e.From + ":" + e.FromPort).Set("to", e.ToEnd ? End : e.To + ":" + e.ToPort));
            var loops = new JsonArray();
            foreach (var l in Stages.Where(s => s.Loop != null).Select(s => s.Loop))
                loops.Add(new JsonObject().Set("entry", l.Entry).Set("kind", l.Kind).Set("members", Strings(l.Members)));
            return new JsonObject().Set("topology", Topology).Set("links", edges).Set("loops", loops)
                .Set("virtualInputs", Strings(VirtualInputs)).Set("virtualOutputs", Strings(VirtualOutputs));
        }
    }
}
