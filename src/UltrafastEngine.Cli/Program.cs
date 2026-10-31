using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Instruments;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Cli
{
    public static class Program
    {
        private const string Usage = @"UltrafastEngine 2.0 command line

  ultrafast run <system.ufs> <run.json> [-o <dir>] [--quiet] [--threads <n>] [--backend cpu|auto|cuda|opencl] [--device <i>]
        Propagate the pulse through the system; writes summary.json, result.npz, provenance.json and CSVs.
        --backend / --device override the run spec's compute settings (GPU back-ends: the gpu plugin, docs/plugins/gpu.md).
  ultrafast validate <system.ufs> [run.json]
        Check a system file (and optionally a run spec) without propagating.
  ultrafast describe <system.ufs>
        List the elements and what the engine will do with each (terms, nonlinear segments, provenance).
  ultrafast info
        Built-in model kinds, materials, crystals, element types and instruments (JSON).
  ultrafast crystal <name> --process shg|sfg|opa|dfg [--type I|II|0] [--fundamental <m>] [--pump <m>] [--signal <m>]
                    [--idler <m>] [--a <m> --b <m>] [--plane XY|XZ|YZ] [--theta <deg>] [--phi <deg>] [--nc <deg>|magic]
                    [--qpm] [--period <m>] [--length <m>] [--temperature <C>] [--pump-intensity <W/m^2>] [--json]
  ultrafast crystal --request <request.json>
        Phase-matching calculator: angles (or QPM period), deff, walk-off, acceptance bandwidths, GVM, gain spectrum.
  ultrafast template <run.json>
        Write a commented starter run spec.
  ultrafast plugins [list|install <folder|zip>|remove <id>|paths]
        Installed plugins (fitting, zemax, ...), where they are searched, and installing / removing them.
  ultrafast schema [system|run] [-o file.schema.json]
        JSON Schema of system files / run specs (for editor autocompletion; see docs/reference/schema.md).
  ultrafast reference [-o options.md]
        Markdown reference of every option (generated from the engine; docs/reference/options.md).
  ultrafast migrate <file.ufs|run.json> [-o out]
        Upgrade an older file to the current format version.
  ultrafast devices [--selftest] [--benchmark] [--backend <name>] [--device <i>] [--json]
        Compute back-ends and devices (CPU, CUDA, OpenCL), the CPU FFT in use; --selftest compares each device with
        the CPU on standard problems, --benchmark also times both (docs/reference/speed.md).
  ultrafast study <study.json> [-o <dir>] [--parallel <n>] [--backend <name>]
        Parameter scan (grid, random, LHS, Sobol), optimisation or sensitivity study (docs/reference/studies.md).
  ultrafast retrieve <pcgpa|epie|dscan|gerchbergSaxton> <trace.npz|trace.json> [--kind shg-frog|pg-frog|dscan] [--fundamental spectrum.csv]
                     [--material FusedSilica] [--instrument name] [--iterations n] [-o <dir>]
        Pulse retrieval from a FROG or d-scan trace (or a results folder's result.npz: --instrument picks the trace);
        writes retrieved_spectrum.csv (usable as a run-spec pulse).
  ultrafast materials list | show <name|page> | import <page.yml> [--name <name>]
        Built-in materials, gases and bundled refractiveindex.info pages; add a page to the user library.
Docs: docs/index.md   Examples: docs/examples/";

        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help" || args[0] == "help") { Console.WriteLine(FullUsage()); return args.Length == 0 ? 1 : 0; }
            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "run": ApplyBackend(args); return Run(args.Skip(1).ToArray());
                    case "validate": return Validate(args.Skip(1).ToArray());
                    case "describe": return Describe(args.Skip(1).ToArray());
                    case "info": Console.WriteLine(Info().ToJson(true)); return 0;
                    case "crystal": return CrystalCommand(args.Skip(1).ToArray());
                    case "template": File.WriteAllText(args.Length > 1 ? args[1] : "run.json", Template); Console.WriteLine("wrote " + (args.Length > 1 ? args[1] : "run.json")); return 0;
                    case "--version": case "version": Console.WriteLine(EngineInfo.Version); return 0;
                    case "plugins": return PluginsCommand(args.Skip(1).ToArray());
                    case "schema": return SchemaCommand(args.Skip(1).ToArray());
                    case "migrate": return MigrateCommand(args.Skip(1).ToArray());
                    case "study": ApplyBackend(args); return StudyCommand(args.Skip(1).ToArray());
                    case "devices": return DevicesCommand(args.Skip(1).ToArray());
                    case "retrieve": return RetrieveCommand(args.Skip(1).ToArray());
                    case "materials": return MaterialsCommand(args.Skip(1).ToArray());
                    case "reference":
                    {
                        string md = Schema.ReferenceGenerator.Markdown(), o = Opt(args, "-o");
                        if (o == null) Console.WriteLine(md); else { File.WriteAllText(o, md); Console.WriteLine("wrote " + o); }
                        return 0;
                    }
                    default:
                        if (Plugins.PluginManager.CliCommands.TryGetValue(args[0], out var pcmd)) return pcmd.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
                        Console.Error.WriteLine("unknown command '" + args[0] + "'" + (args[0].StartsWith("zemax", StringComparison.OrdinalIgnoreCase) ? " (it is provided by the zemax plugin, which is not installed: see ultrafast plugins list)" : "") + "\n\n" + FullUsage()); return 1;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is ModelException || ex is FileNotFoundException || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                if (Environment.GetEnvironmentVariable("ULTRAFAST_DEBUG") == "1") Console.Error.WriteLine(ex.ToString());
                return 2;
            }
        }

        private static string Opt(string[] a, string name, string fallback = null)
        {
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;
        }

        private static string[] Positional(string[] a)
        {
            var r = new System.Collections.Generic.List<string>();
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == "-o" || a[i] == "--out" || a[i] == "--threads" || a[i] == "--backend" || a[i] == "--device") { i++; continue; }
                if (a[i].StartsWith("--")) continue;
                r.Add(a[i]);
            }
            return r.ToArray();
        }

        /// <summary>--backend / --device: override every run's "compute" back-end (and device index).</summary>
        private static void ApplyBackend(string[] a)
        {
            var b = Opt(a, "--backend");
            if (b != null) ComputeBackends.OverrideName = b;
            var d = Opt(a, "--device");
            if (d != null) ComputeBackends.OverrideDevice = int.Parse(d);
        }

        private static int DevicesCommand(string[] a)
        {
            int dev = Opt(a, "--device") != null ? int.Parse(Opt(a, "--device")) : -1;
            bool bench = a.Contains("--benchmark"), self = a.Contains("--selftest") || bench;
            var r = Commands.Devices(self, bench, Opt(a, "--backend"), dev);
            if (a.Contains("--json")) { Console.WriteLine(r.ToJson(true)); return 0; }
            Console.WriteLine("default back-end: " + r.GetString("defaultBackend", "cpu") + "    CPU FFT: " + r.GetString("fft", ""));
            Console.WriteLine("back-ends:");
            foreach (JsonObject b in r.GetArray("backends"))
                Console.WriteLine(string.Format("  {0,-8} {1}", b.GetString("name", ""), b.GetBool("available", true) ? b.GetString("description", "") : "unavailable: " + b.GetString("unavailable", "")));
            Console.WriteLine("devices:");
            foreach (JsonObject d in r.GetArray("devices"))
            {
                double mem = d.GetDouble("memoryBytes", 0);
                Console.WriteLine(string.Format("  {0,-7}#{1} {2}{3}{4}{5}", d.GetString("backend", ""), d.GetInt("index", 0), d.GetString("name", ""),
                    mem > 0 ? string.Format(", {0:F1} GB", mem / 1073741824.0) : "", d.GetBool("usable", true) ? "" : " [not usable]",
                    string.IsNullOrEmpty(d.GetString("notes", "")) ? "" : "  (" + d.GetString("notes", "") + ")"));
            }
            bool ok = true;
            if (r.Has("selftest"))
                foreach (JsonObject t in r.GetArray("selftest"))
                {
                    bool p = t.GetBool("passed", false), skip = t.GetBool("skipped", false); ok &= p || skip;
                    Console.WriteLine("self-test " + t.GetString("backend", "") + (t.Has("device") ? " #" + t.GetInt("device", 0) + " " + t.GetString("name", "") : "") + ": " + (skip ? "skipped" : p ? "PASSED" : "FAILED") + (t.Has("error") ? " (" + t.GetString("error", "") + ")" : ""));
                    if (t.Has("checks"))
                        foreach (JsonObject c in t.GetArray("checks"))
                            Console.WriteLine(string.Format("    {0,-10} {1,-6} rel. error {2:E1}  {3:F3} s{4}", c.GetString("check", ""), c.GetBool("passed", false) ? "ok" : "FAIL",
                                c.GetDouble("relativeError", double.NaN), c.GetDouble("seconds", 0), c.Has("note") ? "  " + c.GetString("note", "") : ""));
                    if (t.Has("benchmark"))
                    {
                        Console.WriteLine("    benchmark (seconds per run):");
                        foreach (JsonObject bm in t.GetArray("benchmark"))
                            Console.WriteLine(bm.Has("error") ? "      " + bm.GetString("problem", "") + ": " + bm.GetString("error", "") :
                                string.Format("      {0,-9} n = {1,-6}{2,-12} cpu {3,8:F3}  device {4,8:F3}  speed-up {5,6:F2}x", bm.GetString("problem", ""), bm.GetInt("n", 0),
                                bm.Has("transversePoints") ? " x " + bm.GetInt("transversePoints", 0) : "", bm.GetDouble("cpuSeconds", 0), bm.GetDouble("deviceSeconds", 0), bm.GetDouble("speedup", 0)));
                    }
                }
            return ok ? 0 : 3;
        }

        private static int Run(string[] a)
        {
            var p = Positional(a);
            if (p.Length < 2) { Console.Error.WriteLine("usage: ultrafast run <system.ufs> <run.json> [-o <dir>]"); return 1; }
            string outDir = Opt(a, "-o") ?? Opt(a, "--out") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p[1])), "results", Path.GetFileNameWithoutExtension(p[1]));
            if (Opt(a, "--threads") != null) ParallelismSettings.MaxDegreeOfParallelism = int.Parse(Opt(a, "--threads"));
            bool quiet = a.Contains("--quiet");
            bool showProgress = !quiet && !Console.IsOutputRedirected;
            var sys = SystemDescription.Load(p[0]);
            var run = RunSpec.Load(p[1]);
            var compiled = Compiler.Compile(sys, run);
            if (!quiet)
            {
                Console.WriteLine("system: " + sys.Name + " (" + sys.Elements.Count + " elements, " + compiled.Elements.Count(e => e.Nonlinear != null) + " nonlinear)");
                foreach (var n in compiled.Notes) Console.WriteLine("  " + n);
                foreach (var w in compiled.Warnings) Console.WriteLine("  WARNING: " + w);
            }
            int lastLen = 0;
            ProgressCallback prog = !showProgress ? (ProgressCallback)null : (id, i, n, f) =>
            {
                string line = string.Format("  [{0,3}/{1}] {2,-24} {3,5:P0}", i + 1, n, id.Length > 24 ? id.Substring(0, 24) : id, f);
                Console.Write("\r" + line.PadRight(lastLen));
                lastLen = line.Length;
            };
            RunResult res;
            var mode = run.Raw == null ? null : Plugins.PluginManager.Modes.FirstOrDefault(m => run.Raw.Has(m.Key));
            if (mode != null)
            {
                if (!quiet) Console.WriteLine(mode.Key + ": " + mode.Description + " ...");
                ProgressCallback fp = !showProgress ? (ProgressCallback)null : (id, i, n, f) => Console.Write("\r  run " + i);
                res = Simulation.Run(sys, run, null, fp);
                if (showProgress) Console.WriteLine();
            }
            else if (run.Raw != null && run.Raw.Has("coherence")) res = PartialCoherence.Run(sys, run, compiled.Registry, prog, default);
            else res = Executor.Execute(compiled, prog);
            if (showProgress) Console.WriteLine();
            res.WriteAll(outDir);
            var s = res.SummaryJson();
            var o = s.GetObject("output"); var t = s.GetObject("totals");
            Console.WriteLine(string.Format("done in {0:F2} s -> {1}", res.ElapsedS, outDir));
            Console.WriteLine(string.Format("  energy {0:G4} J (T = {1:P2}), FWHM {2:G4} fs (TL {3:G4} fs), peak {4:G4} W, B = {5:G3} rad",
                o.GetDouble("energyJ", double.NaN), t.GetDouble("energyTransmission", double.NaN), o.GetDouble("fwhmS", double.NaN) * 1e15,
                o.GetDouble("transformLimitFwhmS", double.NaN) * 1e15, o.GetDouble("peakPowerW", double.NaN), t.GetDouble("bIntegralRad", 0)));
            if (!quiet && !res.Extensions.ContainsKey("fitting.fit")) Measured.Measurements.Report(res, Console.Out);
            if (!quiet) foreach (var rep in Plugins.PluginManager.RunReporters) rep.Report(res, Console.Out);
            foreach (var w in res.Warnings.Except(compiled.Warnings)) Console.WriteLine("  WARNING: " + w);
            return 0;
        }

        private static string FullUsage()
        {
            var cmds = Plugins.PluginManager.CliCommands;
            if (cmds.Count == 0) return Usage;
            var extra = string.Join("\n", cmds.Values.OrderBy(c => c.Name).Select(c => "  " + c.Usage));
            return Usage.Replace("\nDocs: docs/index.md", "\nPlugin commands:\n" + extra + "\n\nDocs: docs/index.md");
        }

        private static int PluginsCommand(string[] a)
        {
            string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "list";
            switch (sub)
            {
                case "list":
                    var ps = Plugins.PluginManager.Plugins;
                    if (ps.Count == 0) Console.WriteLine("no plugins installed (search paths: ultrafast plugins paths)");
                    foreach (var p in ps)
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-10} {1,-16} {2}{3}\n           {4}", p.Id, p.Version, p.Loaded ? "" : "NOT LOADED: ", p.Loaded ? p.Description : p.Error, p.Source));
                    return 0;
                case "paths":
                    foreach (var d in Plugins.PluginManager.SearchPaths()) Console.WriteLine(d + (Directory.Exists(d) ? "" : "   (does not exist)"));
                    return 0;
                case "install":
                    if (a.Length < 2) { Console.Error.WriteLine("usage: ultrafast plugins install <plugin folder | plugin.zip>"); return 1; }
                    Console.WriteLine("installed to " + Plugins.PluginManager.Install(a[1]));
                    return 0;
                case "remove":
                    if (a.Length < 2) { Console.Error.WriteLine("usage: ultrafast plugins remove <id>"); return 1; }
                    Console.WriteLine(Plugins.PluginManager.Remove(a[1]) ? "removed " + a[1] : "'" + a[1] + "' is not installed in " + Plugins.PluginManager.UserPluginFolder());
                    return 0;
                default:
                    Console.Error.WriteLine("usage: ultrafast plugins [list|paths|install <folder|zip>|remove <id>]"); return 1;
            }
        }

        /// <summary>Positional arguments when every "--x" option takes a value.</summary>
        private static string[] Args(string[] a)
        {
            var r = new System.Collections.Generic.List<string>();
            for (int i = 0; i < a.Length; i++) { if (a[i].StartsWith("-")) { i++; continue; } r.Add(a[i]); }
            return r.ToArray();
        }

        private static int StudyCommand(string[] a)
        {
            var p = Args(a);
            if (p.Length < 1) { Console.Error.WriteLine("usage: ultrafast study <study.json> [-o dir] [--parallel n]"); return 1; }
            var req = JsonValue.ParseFile(p[0]).AsObject();
            req.Set("command", "study");
            req.Set("output", Opt(a, "-o") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p[0])), Path.GetFileNameWithoutExtension(p[0]) + "-results"));
            if (Opt(a, "--parallel") != null) req.Set("parallel", int.Parse(Opt(a, "--parallel")));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = Commands.Execute(req, Path.GetDirectoryName(Path.GetFullPath(p[0])));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} evaluations in {1:F1} s -> {2}", res.GetInt("evaluations", 0), sw.Elapsed.TotalSeconds, req.GetString("output")));
            foreach (var n in res.GetArray("notes") ?? new JsonArray()) Console.WriteLine("  " + n.AsString());
            var an = res.GetObject("analysis");
            if (an != null && an.Count > 0) Console.WriteLine(Brief(an).ToJson(true));
            return 0;
        }

        /// <summary>The analysis without long arrays (they are in the results folder).</summary>
        private static JsonObject Brief(JsonObject o)
        {
            var r = new JsonObject();
            foreach (var kv in o)
            {
                if (kv.Value.Kind == JsonKind.Array && kv.Value.AsArray().Count > 12) r.Set(kv.Key, "[" + kv.Value.AsArray().Count + " values]");
                else if (kv.Value.Kind == JsonKind.Object) r.Set(kv.Key, Brief(kv.Value.AsObject()));
                else r.Set(kv.Key, kv.Value);
            }
            return r;
        }

        private static int RetrieveCommand(string[] a)
        {
            var p = Args(a);
            if (p.Length < 2) { Console.Error.WriteLine("usage: ultrafast retrieve <pcgpa|epie|dscan|gerchbergSaxton> <trace.npz|trace.json> [--kind ...] [--fundamental spectrum.csv] [-o dir]"); return 1; }
            var req = new JsonObject().Set("command", "retrieve").Set("method", p[0]).Set("trace", Path.GetFullPath(p[1]));
            if (Opt(a, "--kind") != null) req.Set("kind", Opt(a, "--kind"));
            if (Opt(a, "--fundamental") != null) req.Set("fundamental", Path.GetFullPath(Opt(a, "--fundamental")));
            if (Opt(a, "--material") != null) req.Set("material", Opt(a, "--material"));
            if (Opt(a, "--instrument") != null) req.Set("instrument", Opt(a, "--instrument"));
            var opt = new JsonObject(); if (Opt(a, "--iterations") != null) opt.Set("maxIterations", int.Parse(Opt(a, "--iterations"))); req.Set("options", opt);
            req.Set("output", Path.GetFullPath(Opt(a, "-o") ?? "retrieval"));
            var res = Commands.Execute(req);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}: error {1:G3} after {2} iterations, FWHM {3:G4} fs -> {4}", res.GetString("method"), res.GetDouble("error", double.NaN), res.GetInt("iterations", 0), res.GetDouble("fwhmS", double.NaN) * 1e15, req.GetString("output")));
            foreach (var n in res.GetArray("notes") ?? new JsonArray()) Console.WriteLine("  " + n.AsString());
            return 0;
        }

        private static int MaterialsCommand(string[] a)
        {
            var p = Args(a);
            string sub = p.Length > 0 ? p[0] : "list";
            JsonObject res;
            switch (sub)
            {
                case "list": res = Commands.MaterialsList(); break;
                case "show": if (p.Length < 2) { Console.Error.WriteLine("usage: ultrafast materials show <name|page>"); return 1; } res = Commands.MaterialsShow(p[1], Directory.GetCurrentDirectory()); break;
                case "import":
                    if (p.Length < 2) { Console.Error.WriteLine("usage: ultrafast materials import <page.yml> [--name N]"); return 1; }
                    var req = new JsonObject().Set("file", Path.GetFullPath(p[1])); if (Opt(a, "--name") != null) req.Set("name", Opt(a, "--name"));
                    res = Commands.MaterialsImport(req, null); break;
                default: Console.Error.WriteLine("usage: ultrafast materials list | show <name> | import <page.yml>"); return 1;
            }
            Console.WriteLine(res.ToJson(true));
            return 0;
        }

        private static int SchemaCommand(string[] a)
        {
            string which = Positional(a).FirstOrDefault() ?? "system";
            var schema = which.StartsWith("r", StringComparison.OrdinalIgnoreCase) ? Schema.SchemaGenerator.RunSchema() : Schema.SchemaGenerator.SystemSchema();
            string text = schema.ToJson(true);
            string o = Opt(a, "-o");
            if (o == null) Console.WriteLine(text); else { File.WriteAllText(o, text); Console.WriteLine("wrote " + o); }
            return 0;
        }

        private static int MigrateCommand(string[] a)
        {
            var p = Positional(a);
            if (p.Length < 1) { Console.Error.WriteLine("usage: ultrafast migrate <file.ufs | run.json> [-o out]"); return 1; }
            var root = JsonValue.ParseFile(p[0]).AsObject();
            var notes = new System.Collections.Generic.List<string>();
            bool isRun = root.GetString("format", "") == "ultrafast-run" || root.Has("pulse");
            var migrated = isRun ? Schema.Migrations.MigrateRun(root, notes) : Schema.Migrations.MigrateSystem(root, notes);
            foreach (var n in notes) Console.WriteLine("  " + n);
            string o = Opt(a, "-o") ?? p[0];
            File.WriteAllText(o, migrated.ToJson(true));
            Console.WriteLine((notes.Count == 0 ? "already current; " : "") + "wrote " + o);
            return 0;
        }

        private static int Validate(string[] a)
        {
            var p = Positional(a);
            if (p.Length < 1) { Console.Error.WriteLine("usage: ultrafast validate <system.ufs> [run.json] [--graph [--mermaid]]"); return 1; }
            var sys = SystemDescription.Load(p[0]);
            var rep = Simulation.Validate(sys, p.Length > 1 ? RunSpec.Load(p[1]) : null);
            foreach (var e in rep.Errors) Console.WriteLine("ERROR: " + e);
            foreach (var w in rep.Warnings) Console.WriteLine("WARNING: " + w);
            foreach (var n in rep.Notes) Console.WriteLine("note: " + n);
            if (rep.Ok && a.Contains("--graph"))
            {
                var graph = SystemGraph.Build(sys);
                Console.Write(a.Contains("--mermaid") ? graph.ToMermaid() : graph.ToText());
            }
            Console.WriteLine(rep.Ok ? "OK: " + rep.ElementCount + " elements (" + rep.NonlinearElements + " nonlinear)" : "INVALID");
            return rep.Ok ? 0 : 3;
        }

        private static int Describe(string[] a)
        {
            var p = Positional(a);
            var sys = SystemDescription.Load(p[0]);
            double lam = sys.WavelengthsM != null && sys.WavelengthsM.Length > 0 ? sys.WavelengthsM.OrderBy(x => x).ElementAt(sys.WavelengthsM.Length / 2) : 1.03e-6;
            var run = p.Length > 1 ? RunSpec.Load(p[1]) : RunSpec.FromJson(new JsonObject().Set("pulse", new JsonObject().Set("centerWavelengthM", lam).Set("durationFwhmS", 100e-15).Set("energyJ", 1e-9)));
            var c = Compiler.Compile(sys, run);
            foreach (var e in c.Elements)
            {
                Console.WriteLine(string.Format("{0,-16} {1,-14} L = {2,10:G5} m{3}", e.Id, e.Type, e.LengthM, e.Enabled ? "" : "  (disabled)"));
                foreach (var t in e.Pre) Console.WriteLine("    [" + t.Mechanism + "] " + t.Description);
                if (e.Nonlinear != null)
                {
                    Console.WriteLine("    [nonlinear] n2 = " + e.Nonlinear.Medium.N2.ToString("G3") + " m^2/W, beam: " + e.Nonlinear.Beam.Description + (e.Nonlinear.Medium.ExtraResponses.Count > 0 ? ", + " + string.Join(", ", e.Nonlinear.Medium.ExtraResponses.Select(r => r.Description)) : ""));
                    foreach (var t in e.Nonlinear.Background) Console.WriteLine("      background [" + t.Mechanism + "] " + t.Description);
                }
                foreach (var t in e.Post) Console.WriteLine("    [" + t.Mechanism + "] " + t.Description);
                foreach (var n in e.Notes) Console.WriteLine("    note: " + n);
            }
            foreach (var w in c.Warnings) Console.WriteLine("WARNING: " + w);
            return 0;
        }

        public static JsonObject Info()
        {
            var reg = ModelRegistry.CreateDefault();
            return new JsonObject().Set("version", EngineInfo.Version).Set("models", reg.Describe())
                .Set("elementTypes", JsonValue.FromArray(Compiler.ElementTypes.Concat(Plugins.PluginManager.ElementTypes)))
                .Set("plugins", JsonValue.FromArray(Plugins.PluginManager.Plugins.Where(p => p.Loaded).Select(p => p.Id + " " + p.Version)))
                .Set("materials", JsonValue.FromArray(MaterialLibrary.BuiltInNames))
                .Set("instruments", JsonValue.FromArray(InstrumentFactory.Known))
                .Set("crystals", JsonValue.FromArray(Crystals.CrystalLibrary.Names));
        }

        private static int CrystalCommand(string[] a)
        {
            JsonObject req;
            string reqFile = Opt(a, "--request");
            if (reqFile != null) req = JsonValue.ParseFile(reqFile).AsObject();
            else
            {
                var pos = a.Where((x, i) => !x.StartsWith("--") && (i == 0 || !a[i - 1].StartsWith("--") || a[i - 1] == "--json" || a[i - 1] == "--qpm")).ToList();
                if (pos.Count == 0) { Console.Error.WriteLine("usage: ultrafast crystal <name> --process shg --fundamental 1030e-9 [...]  (built-in: " + string.Join(", ", Crystals.CrystalLibrary.Names) + ")"); return 1; }
                req = new JsonObject().Set("crystal", pos[0]).Set("process", Opt(a, "--process", "shg")).Set("type", Opt(a, "--type", a.Contains("--qpm") || a.Contains("--period") ? "0" : "I"));
                var wl = new JsonObject();
                foreach (var k in new[] { "fundamental", "pump", "signal", "idler", "a", "b" }) { var v = Opt(a, "--" + k); if (v != null) wl.Set(k, double.Parse(v, CultureInfo.InvariantCulture)); }
                req.Set("wavelengthsM", wl);
                void Num(string opt, string key) { var v = Opt(a, opt); if (v != null) req.Set(key, double.Parse(v, CultureInfo.InvariantCulture)); }
                Num("--theta", "thetaDeg"); Num("--phi", "phiDeg"); Num("--length", "lengthM"); Num("--temperature", "temperatureC"); Num("--pump-intensity", "pumpIntensityWPerM2");
                var plane = Opt(a, "--plane"); if (plane != null) req.Set("plane", plane);
                var nc = Opt(a, "--nc"); if (nc != null) req.Set("noncollinearDeg", nc.Equals("magic", StringComparison.OrdinalIgnoreCase) ? (JsonValue)"magic" : double.Parse(nc, CultureInfo.InvariantCulture));
                var per = Opt(a, "--period");
                if (per != null) req.Set("qpm", new JsonObject().Set("periodM", double.Parse(per, CultureInfo.InvariantCulture)));
                else if (a.Contains("--qpm")) req.Set("qpm", true);
            }
            var res = Crystals.PhaseMatchCalculator.Solve(req);
            if (a.Contains("--json")) { Console.WriteLine(res.ToJson(true)); return res.GetBool("phaseMatched", false) ? 0 : 3; }
            Console.WriteLine(res.GetString("crystal") + ", " + res.GetString("process"));
            if (!res.GetBool("phaseMatched", false)) { Console.WriteLine("  " + res.GetString("message")); return 3; }
            var b = res.GetObject("best");
            var ci = CultureInfo.InvariantCulture;
            Console.WriteLine(string.Format(ci, "  theta = {0:F3} deg, phi = {1:F3} deg ({2}), deff = {3:F3} pm/V, roles {4}", b.GetDouble("thetaDeg", 0), b.GetDouble("phiDeg", 0), b.GetString("plane"), b.GetDouble("deffPmPerV", 0), string.Join("/", b["roles"].AsStringArray())));
            if (b.Has("noncollinearDeg")) Console.WriteLine(string.Format(ci, "  non-collinear angle {0:F3} deg (pump at theta = {1:F3} deg; idler at {2:F3} deg from the signal)", b.GetDouble("noncollinearDeg", 0), b.GetDouble("pumpThetaDeg", 0), b.GetDouble("idlerAngleToSignalDeg", 0)));
            if (b.Has("qpmPeriodM")) Console.WriteLine(string.Format(ci, "  QPM period {0:F4} um (first order; deff includes 2/pi)", b.GetDouble("qpmPeriodM", 0) * 1e6));
            foreach (var w in b.GetArray("waves"))
            {
                var wo = w.AsObject();
                Console.WriteLine(string.Format(ci, "  {0,-18} {1,9:F2} nm  {2,-4} n = {3:F5}  n_g = {4:F5}  walk-off {5:F2} mrad", wo.GetString("name"), wo.GetDouble("wavelengthM", 0) * 1e9, wo.GetString("polarization"), wo.GetDouble("n", 0), wo.GetDouble("groupIndex", 0), wo.GetDouble("walkOffMrad", 0)));
            }
            var acc = b.GetObject("acceptance");
            Console.WriteLine("  acceptance (FWHM, L = " + (acc.GetDouble("lengthM", 0) * 1e3).ToString("G4", ci) + " mm): " + string.Join(", ", acc.Where(kv => kv.Key != "lengthM" && kv.Key != "dDeltaK_dOmegaSignal").Select(kv => kv.Key + " " + (kv.Value.IsNull ? "inf" : kv.Value.AsDouble().ToString("G4", ci)))));
            Console.WriteLine("  GVM: " + string.Join(", ", b.GetObject("gvm").Select(kv => kv.Key.Replace(" fs/mm", "") + " " + kv.Value.AsDouble().ToString("F1", ci) + " fs/mm")));
            if (res.GetArray("solutions").Count > 1) Console.WriteLine("  (" + res.GetArray("solutions").Count + " solutions; --json lists all)");
            return 0;
        }

        private const string Template = @"{
  // UltrafastEngine run spec (comments and trailing commas are allowed). Full reference: docs/reference/run-spec.md
  ""format"": ""ultrafast-run"",
  ""name"": ""my-run"",
  ""pulse"": {
    ""shape"": ""gaussian"",            // gaussian | sech2 | superGaussian | spectrum
    ""centerWavelengthM"": 1.03e-6,
    ""durationFwhmS"": 200e-15,        // transform-limited intensity FWHM
    ""energyJ"": 10e-6,                // or averagePowerW + repRateHz, or peakPowerW
    ""repRateHz"": 100e3,
    ""polarization"": ""x"",
    ""chirp"": { ""gddS2"": 0 }
  },
  ""grid"": { ""auto"": true, ""maxN"": 4194304 },
  ""physics"": { ""nonlinear"": true, ""selfSteepening"": true, ""raman"": true, ""fieldMode"": ""auto"", ""polarization"": ""jones"" },
  ""stepper"": { ""method"": ""adaptive"", ""tolerance"": 1e-6 },
  ""probes"": ""all"",
  ""instruments"": [
    { ""kind"": ""spectrometer"", ""at"": ""end"", ""resolutionM"": 0.1e-9 },
    { ""kind"": ""autocorrelator"", ""at"": ""end"" },
    { ""kind"": ""compressor"", ""at"": ""end"" }
  ],
  ""overrides"": [
    // { ""target"": ""material:FusedSilica"", ""property"": ""n2"", ""value"": 2.2e-20 }
  ],
  ""outputs"": { ""npz"": true, ""csv"": true, ""json"": true }
}
";
    }
}
