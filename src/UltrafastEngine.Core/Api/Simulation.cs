using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UltrafastEngine.Engine;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Specs;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine
{
    /// <summary>
    /// The one-call entry point:
    /// <code>
    /// var result = Simulation.Run(SystemDescription.Load("stretcher.ufs"), RunSpec.Load("run.json"));
    /// result.WriteAll("out");
    /// </code>
    /// For custom physics, pass a <see cref="ModelRegistry"/> with your callbacks/factories registered.
    /// </summary>
    public static class Simulation
    {
        public static RunResult Run(SystemDescription system, RunSpec run, ModelRegistry registry = null, ProgressCallback progress = null, CancellationToken cancel = default)
        {
            registry = registry ?? ModelRegistry.CreateDefault();
            // a plugin run mode selected by a top-level key of the run spec (e.g. the fitting plugin's "fit")
            if (run.Raw != null)
            {
                foreach (var mode in Plugins.PluginManager.Modes)
                    if (run.Raw.Has(mode.Key)) return mode.Run(system, run, registry, progress, cancel);
                if (run.Raw.Has("fit")) throw new JsonException("the run spec has \"fit\", which needs the fitting plugin (not installed: see `ultrafast plugins list` and docs/reference/plugins.md).");
            }
            var compiled = Compiler.Compile(system, run, registry);
            return Executor.Execute(compiled, progress, cancel);
        }

        public static RunResult RunFiles(string systemPath, string runPath, string outputDirectory = null, ModelRegistry registry = null, ProgressCallback progress = null)
        {
            var r = Run(SystemDescription.Load(systemPath), RunSpec.Load(runPath), registry, progress);
            if (outputDirectory != null) r.WriteAll(outputDirectory);
            return r;
        }

        /// <summary>Structural checks plus a trial compilation with a probe pulse (no propagation).</summary>
        public static ValidationReport Validate(SystemDescription system, RunSpec run = null, ModelRegistry registry = null)
        {
            var rep = new ValidationReport();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in system.Elements)
            {
                if (!ids.Add(e.Id)) rep.Errors.Add("duplicate element id '" + e.Id + "'");
                if (!Compiler.ElementTypes.Contains(e.Type, StringComparer.OrdinalIgnoreCase) && !Plugins.PluginManager.TryGetElementCompiler(e.Type, out _) && !string.Equals(e.Type, "surface", StringComparison.OrdinalIgnoreCase) && !string.Equals(e.Type, "medium", StringComparison.OrdinalIgnoreCase) && !string.Equals(e.Type, "marker", StringComparison.OrdinalIgnoreCase))
                    rep.Errors.Add("element '" + e.Id + "': unknown type '" + e.Type + "'");
            }
            if (system.WavelengthsM != null && system.WavelengthsM.Any(l => !(l > 0))) rep.Errors.Add("sampling wavelengths must be positive");
            if (rep.Errors.Count > 0) return rep;
            try
            {
                if (run == null)
                {
                    double lam = system.WavelengthsM != null && system.WavelengthsM.Length > 0 ? system.WavelengthsM.OrderBy(x => x).ElementAt(system.WavelengthsM.Length / 2) : 1.03e-6;
                    run = RunSpec.FromJson(new JsonObject().Set("pulse", new JsonObject().Set("shape", "gaussian").Set("centerWavelengthM", lam).Set("durationFwhmS", 100e-15).Set("energyJ", 1e-9))
                        .Set("grid", new JsonObject().Set("maxN", 1 << 16)));
                }
                var c = Compiler.Compile(system, run, registry ?? ModelRegistry.CreateDefault());
                rep.Warnings.AddRange(c.Warnings);
                rep.Notes.AddRange(c.Notes);
                rep.ElementCount = c.Elements.Count;
                rep.NonlinearElements = c.Elements.Count(x => x.Nonlinear != null);
                rep.Provenance = c.Provenance.ToJson();
            }
            catch (Exception ex) when (ex is JsonException || ex is ModelException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            {
                rep.Errors.Add(ex.Message);
            }
            return rep;
        }
    }

    public sealed class ValidationReport
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Notes { get; } = new List<string>();
        public int ElementCount { get; set; }
        public int NonlinearElements { get; set; }
        public JsonArray Provenance { get; set; }
        public bool Ok => Errors.Count == 0;

        public JsonObject ToJson() => new JsonObject().Set("ok", Ok).Set("errors", JsonValue.FromArray(Errors)).Set("warnings", JsonValue.FromArray(Warnings))
            .Set("notes", JsonValue.FromArray(Notes)).Set("elements", ElementCount).Set("nonlinearElements", NonlinearElements).Set("provenance", Provenance ?? new JsonArray());
    }
}
