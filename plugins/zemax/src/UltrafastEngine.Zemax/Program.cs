using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Zemax
{
    /// <summary>
    /// UltrafastEngine.Zemax.exe -- the OpticStudio binding.
    /// </summary>
    internal static class Program
    {
        private const string Usage = @"UltrafastEngine.Zemax -- OpticStudio binding (ZOS-API)

  UltrafastEngine.Zemax export <export.json> [-o system.ufs] [connection] [--save-sample s.json] [--record session.json]
        Sample the system loaded in OpticStudio and write a system file.
  UltrafastEngine.Zemax run <export.json> <run.json> [-o <dir>] [connection]
        Export, then propagate in-process (writes <dir>/system.ufs and the results).
  UltrafastEngine.Zemax info [connection]
        Show the connected system (surfaces, materials, coatings, wavelengths).
  UltrafastEngine.Zemax thermal-lens <results dir> --element <id> --surface <n> [connection] [--dry-run]
        Write the thermal lens of a gain element into OpticStudio: surface n becomes a Gradient 1 surface,
        n(r) = n0 + Nr2 r^2 (z-averaged; set the surface thickness to the crystal length).
  UltrafastEngine.Zemax selftest
        Export the built-in fake systems (no OpticStudio needed) and run them.

  connection:  (default) attach to OpticStudio in Interactive Extension mode
               (in OpticStudio: Programming > Interactive Extension, then run this program)
               --standalone --file <lens.zos>   start a private OpticStudio and load a file
               --opticstudio <install dir>      if the installation is not found automatically

The system file can then be run anywhere (Windows/macOS/Linux):  ultrafast run system.ufs run.json -o out
Docs: docs/plugins/zemax.md";

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help") { Console.WriteLine(Usage); return args.Length == 0 ? 1 : 0; }
#if ZOSAPI_STUBS
            if (args[0] != "selftest") { Console.Error.WriteLine("This build of UltrafastEngine.Zemax was compiled against ZOS-API STUBS (OpticStudio was not found at build time).\nRebuild with -p:ZosApiDir=<OpticStudio install folder>. Only 'selftest' works."); return 3; }
#endif
            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "export": return Export(args, runAfter: false);
                    case "run": return Export(args, runAfter: true);
                    case "info": return Info(args);
                    case "thermal-lens": return ThermalLens(args);
                    case "selftest": return SelfTest();
                    default: Console.Error.WriteLine("unknown command '" + args[0] + "'\n\n" + Usage); return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                if (args.Contains("--verbose")) Console.Error.WriteLine(ex);
                return 2;
            }
        }

        private static string Opt(string[] a, string name) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        private static string[] Positional(string[] a)
        {
            var valued = new HashSet<string> { "-o", "--out", "--file", "--opticstudio", "--save-sample", "--record" };
            var r = new List<string>();
            for (int i = 1; i < a.Length; i++)
            {
                if (valued.Contains(a[i])) { i++; continue; }
                if (a[i].StartsWith("--")) continue;
                r.Add(a[i]);
            }
            return r.ToArray();
        }

        private static ZosConnection Connect(string[] a)
        {
            bool standalone = a.Contains("--standalone") || Opt(a, "--file") != null;
            var c = ZosConnection.Connect(standalone ? ConnectionMode.Standalone : ConnectionMode.Extension, Opt(a, "--opticstudio"));
            if (Opt(a, "--file") != null) c.Load(Path.GetFullPath(Opt(a, "--file")));
            Console.WriteLine("Connected to OpticStudio (" + (standalone ? "standalone" : "Interactive Extension") + "): " + (string.IsNullOrEmpty(c.System.SystemFile) ? "(untitled)" : c.System.SystemFile));
            return c;
        }

        internal static (SystemDescription System, ZemaxSample Sample) ExportFrom(IOpticStudio os, ExportConfig cfg, Action<string> log)
        {
            var opt = new SamplerOptions
            {
                WavelengthsUm = cfg.SampleWavelengthsUm(),
                KeepCurrentWavelengths = string.Equals(cfg.SamplingMode, "zemax", StringComparison.OrdinalIgnoreCase),
                BatchSize = cfg.BatchSize,
                GeometryFromOperands = !string.Equals(cfg.Geometry, "batchRayTrace", StringComparison.OrdinalIgnoreCase),
                CarrierUm = cfg.CarrierUm(),
                BeamWaist = cfg.BeamWaistM.HasValue ? cfg.BeamWaistM.Value / os.LensUnitToMeters : (double?)null,
                BeamWaistToSurface1 = cfg.BeamWaistToSurface1M / os.LensUnitToMeters,
                BeamM2 = cfg.BeamM2,
                CoatingOperand = cfg.CoatingOperand,
                Log = log
            };
            if (opt.WavelengthsUm != null)
                log(string.Format(CultureInfo.InvariantCulture, "Sampling {0} wavelengths {1:F1}-{2:F1} nm (carrier {3:F2} nm)", opt.WavelengthsUm.Count, opt.WavelengthsUm.Min() * 1e3, opt.WavelengthsUm.Max() * 1e3, opt.CarrierUm * 1e3));
            var sample = Sampler.Sample(os, opt);
            var notes = new List<string>();
            var sys = SystemBuilder.Build(sample, cfg, notes);
            foreach (var w in sample.Warnings) log("  WARNING: " + w);
            foreach (var n in notes) log("  " + n);
            log("System: " + sys.Elements.Count + " elements (" + sys.Elements.Count(e => e.Type == "bulk") + " nonlinear passes, " + sys.Elements.Count(e => e.Type == "interface") + " surfaces)");
            return (sys, sample);
        }

        private static int Export(string[] a, bool runAfter)
        {
            var p = Positional(a);
            if (p.Length < (runAfter ? 2 : 1)) { Console.Error.WriteLine(Usage); return 1; }
            var cfg = ExportConfig.Load(p[0]);
            string outDir = runAfter ? (Opt(a, "-o") ?? Opt(a, "--out") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p[1])), "results")) : null;
            string ufs = runAfter ? Path.Combine(outDir, "system.ufs") : (Opt(a, "-o") ?? Opt(a, "--out") ?? Path.ChangeExtension(p[0], ".ufs"));
            using (var conn = Connect(a))
            {
                IOpticStudio os = new ZosOpticStudio(conn.System);
                RecordingOpticStudio rec = null;
                if (Opt(a, "--record") != null) os = rec = new RecordingOpticStudio(os);
                var (sys, sample) = ExportFrom(os, cfg, Console.WriteLine);
                if (rec != null) { rec.Save(Opt(a, "--record")); Console.WriteLine("Session recorded: " + Opt(a, "--record")); }
                if (Opt(a, "--save-sample") != null) { sample.Save(Opt(a, "--save-sample")); Console.WriteLine("Raw sample saved: " + Opt(a, "--save-sample")); }
                sys.Save(ufs);
                Console.WriteLine("Wrote " + Path.GetFullPath(ufs));
                if (!runAfter) return 0;
                var run = RunSpec.Load(p[1]);
                var compiled = Compiler.Compile(sys, run);
                foreach (var n in compiled.Notes) Console.WriteLine("  " + n);
                foreach (var w in compiled.Warnings) Console.WriteLine("  WARNING: " + w);
                var res = Executor.Execute(compiled, (id, i, n, f) => Console.Write(string.Format("\r  [{0}/{1}] {2,-30} {3,5:P0}", i + 1, n, id, f)));
                Console.WriteLine();
                res.WriteAll(outDir);
                Console.WriteLine("Results: " + Path.GetFullPath(outDir));
                return 0;
            }
        }

        private static int ThermalLens(string[] a)
        {
            var p = Positional(a);
            string id = Opt(a, "--element"), sf = Opt(a, "--surface");
            if (p.Length < 1 || id == null || sf == null) { Console.Error.WriteLine(Usage); return 1; }
            string sum = Directory.Exists(p[0]) ? Path.Combine(p[0], "summary.json") : p[0];
            var summary = Serialization.JsonValue.Parse(File.ReadAllText(sum)).AsObject();
            int surf = int.Parse(sf, CultureInfo.InvariantCulture);
            using (var conn = Connect(a))
            {
                var os = new ZosOpticStudio(conn.System);
                var row = os.GetSurface(surf);
                var plan = ThermalLensExport.Plan(summary, id, os.LensUnitToMeters, row.Thickness);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "surface {0} ({1}, thickness {2:G5}): Gradient 1 with n0 = {3:F6}, Nr2 = {4:G5} per lens unit^2 (f_th = {5:G4} m)", surf, row.Material, row.Thickness, plan.N0, plan.Nr2PerLensUnit2, plan.FocalLengthM));
                if (a.Contains("--dry-run")) return 0;
                os.SetGradient1(surf, plan.N0, plan.Nr2PerLensUnit2, plan.DeltaT);
                Console.WriteLine("done (not saved: save the lens file in OpticStudio if you want to keep it)");
                return 0;
            }
        }

        private static int Info(string[] a)
        {
            using (var conn = Connect(a))
            {
                var os = new ZosOpticStudio(conn.System);
                Console.WriteLine("Lens units: " + os.LensUnitToMeters + " m; environment " + os.TemperatureC + " C, " + os.PressureAtm + " atm");
                var w = os.GetWavelengths(out int prim);
                Console.WriteLine("Wavelengths: " + string.Join(", ", w.Select((x, i) => (i + 1 == prim ? "*" : "") + x.Um.ToString("F4"))));
                Console.WriteLine("  #  type                 material      coating       semi-dia   thickness  comment");
                for (int s = 0; s < os.NumberOfSurfaces; s++)
                {
                    var r = os.GetSurface(s);
                    Console.WriteLine(string.Format("{0,3}  {1,-20} {2,-13} {3,-13} {4,9:G5} {5,11:G6}  {6}", s, r.TypeName, r.Material, r.Coating, r.SemiDiameter, r.Thickness, r.Comment));
                }
                return 0;
            }
        }

        private static int SelfTest()
        {
            foreach (var kind in new[] { "treacy", "mpc", "slab" })
            {
                Console.WriteLine("== fake " + kind);
                var cfg = ExportConfig.FromJson(Serialization.JsonValue.Parse(kind == "mpc"
                    ? @"{""sampling"":{""minWavelengthM"":0.99e-6,""maxWavelengthM"":1.07e-6,""count"":16,""carrierWavelengthM"":1.03e-6},""beam"":{""waistRadiusM"":0.2e-3},""media"":[{""matchMaterial"":""ARGON"",""material"":""Ar1bar"",""nonlinear"":true,""definition"":{""gas"":{""species"":""Ar"",""pressureBar"":1.0}}}],""coatings"":[{""matchComment"":""MPC_MIRROR"",""coating"":{""kind"":""gdd"",""R"":0.999,""gddS2"":-150e-30}}]}"
                    : @"{""sampling"":{""minWavelengthM"":0.98e-6,""maxWavelengthM"":1.08e-6,""count"":24,""carrierWavelengthM"":1.03e-6},""beam"":{""waistRadiusM"":2e-3}}").AsObject());
                var (sys, _) = ExportFrom(new FakeOpticStudio(kind), cfg, s => Console.WriteLine(s));
                var run = RunSpec.FromJson(Serialization.JsonValue.Parse(@"{""pulse"":{""shape"":""gaussian"",""centerWavelengthM"":1.03e-6,""durationFwhmS"":200e-15,""energyJ"":2e-4}}").AsObject());
                var r = Simulation.Run(sys, run);
                Console.WriteLine(string.Format("  output FWHM {0:G4} ps, system GDD {1:G5} fs^2, T = {2:P2}", r.SummaryJson().GetObject("output").GetDouble("fwhmS", 0) * 1e12, r.FinalSystem.GddS2 * 1e30, r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ()));
            }
            return 0;
        }
    }
}
