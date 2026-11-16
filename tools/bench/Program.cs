// Developer benchmark for the speed work (docs/reference/speed.md): wall time of reference runs and of a 200-point study,
// plus a digest of each output (energy, rms width, a hash of the spectrum) so a refactor can be checked against a golden file.
// Usage: bench [--golden FILE | --check FILE] [case ...]   cases: spm-plate mpc-argon spatial-kerr scan-gdd scan-length
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Bench
{
    internal static class Program
    {
        static string Root()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "UltrafastEngine.sln"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("repository root not found");
        }

        static (SystemDescription, RunSpec) Load(string folder, string ufs, string run = "run.json")
        {
            var dir = Path.Combine(Root(), "docs", "examples", folder);
            return (SystemDescription.Load(Path.Combine(dir, ufs)), RunSpec.Load(Path.Combine(dir, run)));
        }

        static string Digest(UltrafastEngine.Results.RunResult r)
        {
            var f = r.Output;
            double e = f.TotalEnergyJ();
            double h = 0; int n = f.Ax.Length;
            for (int i = 0; i < n; i++) h += f.Ax[i].Magnitude * (1 + (i % 97) * 1e-3);
            return string.Format(CultureInfo.InvariantCulture, "E={0:R} sum={1:R}", e, h);
        }

        static void Main(string[] args)
        {
            string golden = null, check = null; var cases = new List<string>();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--golden") golden = args[++i]; else if (args[i] == "--check") check = args[++i]; else cases.Add(args[i]);
            if (cases.Count == 0) cases.AddRange(new[] { "spm-plate", "mpc-argon", "spatial-kerr", "scan-gdd", "scan-length" });
            var lines = new List<string>();
            foreach (var c in cases)
            {
                long a0 = GC.GetTotalAllocatedBytes(true); var sw = Stopwatch.StartNew(); string digest;
                switch (c)
                {
                    case "spm-plate": { var (s, r) = Load("GettingStarted/spm-plate", "plate.ufs"); digest = Digest(Simulation.Run(s, r)); break; }
                    case "mpc-argon": { var (s, r) = Load("Nonlinear/mpc-argon", "mpc.ufs"); digest = Digest(Simulation.Run(s, r)); break; }
                    case "spatial-kerr": { var (s, r) = Load("Nonlinear/spatial-kerr", "kerr.ufs"); digest = Digest(Simulation.Run(s, r)); break; }
                    case "scan-gdd": digest = Scan("GettingStarted/spm-plate", "plate.ufs", "pulse/chirp/gddFs2", -2000, 2000, 200); break;
                    case "fidelity": digest = FidelityTable(); break;
                    case "fp32": digest = Fp32Study(); break;
                    case "scan-mpc": digest = Scan("Nonlinear/mpc-argon", "mpc.ufs", "element:from_cell/lengthM", 0.05, 0.2, 20); break;
                    case "loop-mpc": digest = LoopLength("Nonlinear/mpc-argon", "mpc.ufs", "from_cell", 0.05, 0.2, 20); break;
                    case "loop-gdd": digest = Loop(-2000, 2000, 200); break;
                    case "loop-length": digest = LoopLength("GettingStarted/spm-plate", "plate.ufs", "plate", 0.002, 0.012, 200); break;
                    case "scan-length": digest = Scan("GettingStarted/spm-plate", "plate.ufs", "element:plate/lengthM", 0.002, 0.012, 200); break;
                    default: Console.WriteLine("unknown case " + c); continue;
                }
                var line = c + " | " + digest;
                lines.Add(line);
                Console.WriteLine("{0,-12} {1,8:F2} s  {2,8:F0} MB allocated  {3}", c, sw.Elapsed.TotalSeconds, (GC.GetTotalAllocatedBytes(true) - a0) / 1e6, digest);
            }
            if (golden != null) File.WriteAllLines(golden, lines);
            if (check != null)
            {
                var g = File.ReadAllLines(check).ToDictionary(l => l.Split('|')[0].Trim(), l => l);
                foreach (var l in lines) Console.WriteLine((g.TryGetValue(l.Split('|')[0].Trim(), out var o) && o == l ? "same     " : "DIFFERENT ") + l.Split('|')[0]);
            }
        }

        // the same 200 points as scan-gdd / scan-length, as 200 independent runs (what a plain loop in a script does)
        static string Loop(double lo, double hi, int n)
        {
            var (s, r0) = Load("GettingStarted/spm-plate", "plate.ufs"); double sum = 0;
            for (int i = 0; i < n; i++)
            {
                var rj = (JsonObject)JsonValue.Parse(r0.Raw.ToJson(false));
                var pj = rj.GetObject("pulse"); var ch = pj.GetObject("chirp") ?? new JsonObject(); ch.Set("gddFs2", lo + (hi - lo) * i / (n - 1)); pj.Set("chirp", ch);
                sum += UltrafastEngine.Results.PulseMetrics.Summarize(Simulation.Run(s, RunSpec.FromJson(rj, r0.BaseDirectory)).Output).FwhmS;
            }
            return string.Format(CultureInfo.InvariantCulture, "points={0} sumFwhm={1:R}", n, sum);
        }

        static string LoopLength(string folder, string ufs, string id, double lo, double hi, int n)
        {
            var (s0, r0) = Load(folder, ufs); double sum = 0;
            for (int i = 0; i < n; i++)
            {
                var sj = (JsonObject)JsonValue.Parse(s0.ToJson().ToJson(false));
                foreach (var e in sj.GetArray("elements")) if (e.AsObject().GetString("id") == id) e.AsObject().Set("lengthM", lo + (hi - lo) * i / (n - 1));
                sum += UltrafastEngine.Results.PulseMetrics.Summarize(Simulation.Run(SystemDescription.FromJson(sj, s0.BaseDirectory), r0).Output).FwhmS;
            }
            return string.Format(CultureInfo.InvariantCulture, "points={0} sumFwhm={1:R}", n, sum);
        }

        // quick and standard against full on the reference cases (the table of docs/reference/fidelity.md)
        static string FidelityTable()
        {
            var rows = new List<string>();
            foreach (var (name, folder, ufs) in new[] { ("spm-plate", "GettingStarted/spm-plate", "plate.ufs"), ("mpc-argon", "Nonlinear/mpc-argon", "mpc.ufs"), ("spatial-kerr", "Nonlinear/spatial-kerr", "kerr.ufs") })
            {
                var (s, r) = Load(folder, ufs);
                UltrafastEngine.Results.PulseSummary m0 = null; double t0 = 0;
                foreach (var level in new[] { "full", "standard", "quick" })
                {
                    var rj = (JsonObject)JsonValue.Parse(r.Raw.ToJson(false)); rj.Set("fidelity", level); rj.Set("instruments", new JsonArray()); rj.Set("physics", new JsonObject()); rj.Set("stepper", new JsonObject());   // explicit keys stay as written, so the table starts from defaults
                    var spec = RunSpec.FromJson(rj, r.BaseDirectory);
                    Simulation.Run(s, spec);                       // warm-up
                    var sw = Stopwatch.StartNew(); var res = Simulation.Run(s, RunSpec.FromJson(rj, r.BaseDirectory)); double t = sw.Elapsed.TotalSeconds;
                    var m = UltrafastEngine.Results.PulseMetrics.Summarize(res.Output);
                    if (level == "full") { m0 = m; t0 = t; }
                    rows.Add(string.Format(CultureInfo.InvariantCulture, "{0,-12} {1,-9} {2,6:F2} s (x{3,4:F1})  FWHM {4,8:F3} fs ({5,6:+0.00;-0.00;0.00} %)  spectral FWHM {6,7:F3} nm ({7,6:+0.00;-0.00;0.00} %)  energy {8:E1}  warnings: {9}", name, level, t, t0 / t, m.FwhmS * 1e15, 100 * (m.FwhmS / m0.FwhmS - 1), m.SpectralFwhmM * 1e9, 100 * (m.SpectralFwhmM / m0.SpectralFwhmM - 1), Math.Abs(m.EnergyJ / m0.EnergyJ - 1), res.Warnings.Count(w => w.Contains("unreliable"))));
                }
            }
            return string.Join("\n               ", rows);
        }

        // FP32 study: each reference case with the double-precision and the single-precision-operator back-end
        static string Fp32Study()
        {
            var rows = new List<string>();
            foreach (var (name, folder, ufs) in new[] { ("spm-plate", "GettingStarted/spm-plate", "plate.ufs"), ("mpc-argon", "Nonlinear/mpc-argon", "mpc.ufs"), ("spatial-kerr", "Nonlinear/spatial-kerr", "kerr.ufs") })
            {
                var (s, r) = Load(folder, ufs);
                var rj = (JsonObject)JsonValue.Parse(r.Raw.ToJson(false)); rj.Set("instruments", new JsonArray());
                var r64 = Simulation.Run(s, RunSpec.FromJson(rj, r.BaseDirectory));
                rj.Set("compute", new JsonObject().Set("backend", "cpu-fp32"));
                var r32 = Simulation.Run(s, RunSpec.FromJson(rj, r.BaseDirectory));
                if (!r32.Records.Any(x => x.Steps != null && x.Steps.Notes.Any(t => t.Contains("cpu-fp32")))) throw new Exception("the cpu-fp32 back-end was not used in " + name);
                var a = r64.Output; var b = r32.Output; int n = a.Ax.Length;
                double e64 = a.TotalEnergyJ(), e32 = b.TotalEnergyJ();
                var fa = (System.Numerics.Complex[])a.Ax.Clone(); var fb = (System.Numerics.Complex[])b.Ax.Clone();
                UltrafastEngine.Numerics.ComplexFft.TransformInPlace(fa, -1, false); UltrafastEngine.Numerics.ComplexFft.TransformInPlace(fb, -1, false);
                double sa = 0, sd = 0, ia = 0, id = 0, pk = 0, maxd = 0;
                for (int i = 0; i < n; i++) { double p = fa[i].Magnitude * fa[i].Magnitude, q = fb[i].Magnitude * fb[i].Magnitude; sa += p; sd += Math.Abs(p - q); }
                var floor = new List<double>();
                for (int i = 0; i < n; i++) { double p = a.Ax[i].Magnitude * a.Ax[i].Magnitude, q = b.Ax[i].Magnitude * b.Ax[i].Magnitude; pk = Math.Max(pk, p); maxd = Math.Max(maxd, Math.Abs(p - q)); floor.Add(p); }
                floor.Sort(); double fl = floor[n / 10] / pk;
                rows.Add(string.Format(CultureInfo.InvariantCulture, "{0}: energy rel {1:E2}, spectrum L1 {2:E2}, max |dI|/peak {3:E2} (10th-percentile floor {4:E2})", name, Math.Abs(e32 - e64) / e64, sd / sa, maxd / pk, fl));
            }
            return string.Join("\n               ", rows);
        }

        static string Scan(string folder, string ufs, string path, double lo, double hi, int points)
        {
            var (s, r) = Load(folder, ufs);
            var spec = StudySpec.FromJson((JsonObject)JsonValue.Parse("{\"mode\":\"scan\",\"parameters\":[{\"path\":\"" + path + "\",\"min\":" + lo.ToString(CultureInfo.InvariantCulture) + ",\"max\":" + hi.ToString(CultureInfo.InvariantCulture) + ",\"points\":" + points + "}],\"outputs\":[\"pulse:end/fwhmS\"]}"));
            var res = Studies.Run(s, r, spec);
            return string.Format(CultureInfo.InvariantCulture, "points={0} sumFwhm={1:R} err={2}", res.Evaluations, res.Outputs.Sum(o => o[0]), res.Errors.Count(e => e != null));
        }
    }
}
