using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using UltrafastEngine.Instruments;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Engine
{
    /// <summary>
    /// Partial coherence by coherent modes (docs/reference/transverse.md#partial-coherence): a Gaussian-Schell beam of quality M^2 is
    /// an incoherent sum of Hermite-Gauss modes with weights t^n, t = (M^2 - 1) / (M^2 + 1) (Starikov &amp; Wolf, J. Opt. Soc. Am. 72, 923
    /// (1982)), of mode radius w0 / sqrt(M^2). This is a run mode around the executor: each mode is one complete run (with its share of the
    /// energy) and the fluences and second moments are added. Nothing in the executor or the elements knows about it.
    /// </summary>
    public static class PartialCoherence
    {
        public static RunResult Run(SystemDescription system, RunSpec run, ModelRegistry registry, ProgressCallback progress, CancellationToken cancel)
        {
            var spec = run.Raw.GetObject("coherence") ?? throw new JsonException("coherence must be an object.");
            string model = spec.GetString("model", "gaussianSchell");
            if (!string.Equals(model, "gaussianSchell", StringComparison.OrdinalIgnoreCase)) throw new JsonException("coherence.model must be \"gaussianSchell\".");
            double m2 = spec.GetDouble("m2", double.NaN);
            if (!(m2 >= 1)) throw new JsonException("coherence.m2 must be >= 1.");
            string id = spec.GetString("element", null);
            var el = id != null ? system.Find(id) : system.Elements.FirstOrDefault(x => x.Json.GetObject("transverse") != null);
            var tv = el?.Json.GetObject("transverse") ?? throw new JsonException("coherence: no element with a \"transverse\" object to start the modes from (coherence.element).");
            if (!string.Equals(tv.GetString("model", ""), "cartesian", StringComparison.OrdinalIgnoreCase)) throw new JsonException("coherence: the modes need a cartesian grid at element '" + el.Id + "'.");
            var ini = tv.GetObject("initialProfile");
            double w0 = ini?.GetDouble("radiusM", double.NaN) ?? double.NaN;
            if (double.IsNaN(w0)) w0 = el.Json.GetObject("beam")?.GetDouble("w0M", double.NaN) ?? double.NaN;
            if (!(w0 > 0)) throw new JsonException("coherence: the beam radius comes from initialProfile.radiusM (or the element's beam w0M).");
            int dims = spec.GetInt("dimensions", tv.GetInt("ny", tv.GetInt("nx", 64)) == 1 ? 1 : 2);
            int modes = spec.GetInt("modes", 12);
            double t = (m2 - 1) / (m2 + 1);
            var list = new List<(int Mx, int My, double Weight)>();
            for (int a = 0; a < modes; a++)
                for (int b = 0; b < (dims == 2 ? modes - a : 1); b++) list.Add((a, b, Math.Pow(t, a + b)));
            double full = dims == 2 ? 1 / ((1 - t) * (1 - t)) : 1 / (1 - t), sum = list.Sum(x => x.Weight);
            var saved = (JsonObject)JsonValue.Parse(tv.ToJson(false)).AsObject();
            double e0 = run.Pulse.EnergyJ, p0 = run.Pulse.PeakPowerW, a0 = run.Pulse.AveragePowerW;
            RunResult first = null;
            var results = new List<(RunResult R, double P)>();
            try
            {
                foreach (var (mx, my, w) in list)
                {
                    double share = w / sum;
                    if (share < 1e-4) continue;
                    var prof = new JsonObject().Set("kind", "hermiteGauss").Set("modeX", mx).Set("modeY", my).Set("radiusM", w0 / Math.Sqrt(m2));
                    tv.Set("initialProfile", prof);
                    run.Pulse.EnergyJ = e0 * share; run.Pulse.PeakPowerW = p0 * share; run.Pulse.AveragePowerW = a0 * share;
                    var r = Executor.Execute(Compiler.Compile(system, run, registry), progress, cancel);
                    results.Add((r, share)); first = first ?? r;
                }
            }
            finally
            {
                run.Pulse.EnergyJ = e0; run.Pulse.PeakPowerW = p0; run.Pulse.AveragePowerW = a0;
                tv.Set("initialProfile", saved.GetObject("initialProfile") ?? new JsonObject());
                if (!saved.Has("initialProfile")) tv.Remove("initialProfile");
            }
            Combine(first, results);
            double kept = results.Sum(x => x.P);
            var o = new InstrumentOutput { Name = "coherence", Kind = "coherence", Location = el.Id };
            o.Values.Set("m2", m2).Set("modes", results.Count).Set("t", t).Set("capturedWeight", sum / full).Set("virtual", true);
            o.Notes.Add(string.Format(CultureInfo.InvariantCulture, "Gaussian-Schell beam of M^2 = {0:G4}: {1} coherent modes, {2:P2} of the weight of the full series, each one complete run; intensities add.", m2, results.Count, sum / full));
            first.Instruments.Add(o);
            if (results.Any(x => x.R.Records.Any(q => q.BIntegralRad > 0.01))) first.Warnings.Add("coherence: an element is nonlinear; each mode was propagated alone, so the coupling between modes is not included.");
            first.Notes.AddRange(o.Notes);
            return first;
        }

        /// <summary>Add the modes' results into the first: energies, second moments and fluence arrays of the beamProfile instruments.</summary>
        private static void Combine(RunResult into, List<(RunResult R, double P)> res)
        {
            for (int i = 0; i < into.Records.Count; i++)
            {
                var recs = res.Select(x => x.R.Records[i]).ToList(); var rec = into.Records[i];
                rec.EnergyInJ = recs.Sum(x => x.EnergyInJ); rec.EnergyOutJ = recs.Sum(x => x.EnergyOutJ);
                double W(Func<ElementRecord, double> f) => Math.Sqrt(res.Sum(x => x.P * Math.Pow(f(x.R.Records[i]), 2)));
                if (!double.IsNaN(rec.BeamRadiusXM)) { rec.BeamRadiusXM = W(x => x.BeamRadiusXM); rec.BeamRadiusYM = W(x => x.BeamRadiusYM); }
            }
            for (int i = 0; i < into.Instruments.Count; i++)
            {
                var o = into.Instruments[i];
                if (o.Kind != "beamProfile") continue;
                foreach (var a in o.Arrays.Where(a => a.Name.Contains("fluence")))
                {
                    var data = new double[a.Data.Length];
                    foreach (var (r, _) in res) { var d = r.Instruments[i].Arrays.First(x => x.Name == a.Name).Data; for (int k = 0; k < data.Length; k++) data[k] += d[k]; }
                    Array.Copy(data, a.Data, data.Length);
                }
                foreach (var key in new[] { "radiusXM", "radiusYM" })
                    if (o.Values.Has(key)) o.Values.Set(key, Math.Sqrt(res.Sum(x => x.P * Math.Pow(x.R.Instruments[i].Values.GetDouble(key, 0), 2))));
            }
        }
    }
}
