using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltrafastEngine.Plugins;

namespace UltrafastEngine.Zemax
{
    /// <summary>
    /// The zemax plugin: CLI commands that build system files from OpticStudio samples (zemax-build), recorded
    /// sessions (zemax-replay) or the built-in fake OpticStudio systems (zemax-demo). The OpticStudio extension itself
    /// (UltrafastEngine.Zemax.exe, Windows) ships in the plugin's tools/net48 folder. Docs: plugins/zemax/README.md.
    /// </summary>
    public sealed class ZemaxPlugin : IPlugin
    {
        public string Id => "zemax";
        public string Name => "Zemax OpticStudio";
        public string Version => "2.1.8-beta";
        public string Description => "Exports OpticStudio systems to .ufs files and ships the OpticStudio extension program.";

        public void Register(PluginContext ctx)
        {
            ctx.RegisterCommand(new ZemaxCommand("zemax-build", "ultrafast zemax-build <sample.ufsample.json> <export.json> [-o system.ufs]\n        Rebuild a system file from a raw OpticStudio sample (saved with UltrafastEngine.Zemax --save-sample)."));
            ctx.RegisterCommand(new ZemaxCommand("zemax-replay", "ultrafast zemax-replay <session.json> <export.json> [-o system.ufs]\n        Re-run the sampler against a recorded OpticStudio session (UltrafastEngine.Zemax --record)."));
            ctx.RegisterCommand(new ZemaxCommand("zemax-demo", "ultrafast zemax-demo <treacy|mpc|slab> <export.json> [-o system.ufs]\n        Export one of the built-in fake OpticStudio systems (to try the Zemax workflow without Zemax)."));
        }
    }

    internal sealed class ZemaxCommand : ICliCommand
    {
        public ZemaxCommand(string name, string usage) { Name = name; Usage = usage; }
        public string Name { get; }
        public string Usage { get; }

        public int Run(string[] a, TextWriter output, TextWriter error)
        {
            var p = a.Where((x, i) => !x.StartsWith("-") && (i == 0 || a[i - 1] != "-o")).ToArray();
            if (p.Length < 2) { error.WriteLine("usage: " + Usage.Split('\n')[0]); return 1; }
            var cfg = ExportConfig.Load(p[1]);
            ZemaxSample sample;
            if (Name == "zemax-build") sample = ZemaxSample.Load(p[0]);
            else
            {
                IOpticStudio os = Name == "zemax-replay" ? (IOpticStudio)new ReplayOpticStudio(p[0]) : new FakeOpticStudio(p[0]);
                sample = Sampler.Sample(os, new SamplerOptions
                {
                    WavelengthsUm = cfg.SampleWavelengthsUm(), KeepCurrentWavelengths = string.Equals(cfg.SamplingMode, "zemax", StringComparison.OrdinalIgnoreCase),
                    BatchSize = cfg.BatchSize, GeometryFromOperands = !string.Equals(cfg.Geometry, "batchRayTrace", StringComparison.OrdinalIgnoreCase),
                    CarrierUm = cfg.CarrierUm(), BeamWaist = cfg.BeamWaistM.HasValue ? cfg.BeamWaistM.Value / os.LensUnitToMeters : (double?)null,
                    BeamWaistToSurface1 = cfg.BeamWaistToSurface1M / os.LensUnitToMeters, BeamM2 = cfg.BeamM2, CoatingOperand = cfg.CoatingOperand, Log = output.WriteLine
                });
            }
            var notes = new List<string>();
            var sys = SystemBuilder.Build(sample, cfg, notes);
            foreach (var w in sample.Warnings) output.WriteLine("WARNING: " + w);
            foreach (var n in notes) output.WriteLine("  " + n);
            int oi = Array.IndexOf(a, "-o");
            string outPath = oi >= 0 && oi + 1 < a.Length ? a[oi + 1] : Path.ChangeExtension(p[1], ".ufs");
            sys.Save(outPath);
            output.WriteLine("wrote " + outPath + " (" + sys.Elements.Count + " elements)");
            return 0;
        }
    }
}
