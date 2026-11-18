// Fidelity switch (docs/reference/fidelity.md): a run-level key "fidelity": "full" | "standard" | "quick" that turns a named bundle of
// cost-saving switches on. A switch the run spec writes itself is never touched (the bundle only fills what is unset), and
// every change is recorded and reported, so a quick result is never silently different from a full one.
// Main classes: FidelityPlan (parsed level, the changes made, which spatial / instrument shortcuts apply), Fidelity.Apply (at
// parse time: RunSpec.FromJson), Fidelity.Assess (after a run: an unreliability warning from B, self-steepening and critical power).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Specs
{
    /// <summary>The fidelity of a run and what it changed.</summary>
    public sealed class FidelityPlan
    {
        public const string Full = "full", Standard = "standard", Quick = "quick";
        public string Level = Full;
        /// <summary>One line per switch the bundle changed (its name, old and new value).</summary>
        public List<string> Changes { get; } = new List<string>();
        /// <summary>Switches the bundle would change but the run spec fixed itself.</summary>
        public List<string> Kept { get; } = new List<string>();
        /// <summary>quick: a spatial (transverse) model is replaced by the 1-D beam model where the element has a beam to take over.</summary>
        public bool CollapseTransverse;
        /// <summary>Elements whose spatial model was actually replaced by the 1-D model (filled by the compiler).</summary>
        public HashSet<string> CollapsedElements { get; } = new HashSet<string>();
        /// <summary>Instrument kinds skipped at this level (unless the instrument says "keepInQuick": true).</summary>
        public HashSet<string> SkippedInstrumentKinds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool IsFull => Level == Full;

        public JsonObject ToJson() => new JsonObject().Set("level", Level)
            .Set("changes", JsonValue.FromArray(Changes)).Set("keptAsWritten", JsonValue.FromArray(Kept));
    }

    public static class Fidelity
    {
        /// <summary>Looser local tolerance of the adaptive solver for quick / standard (the default is 1e-6).</summary>
        public const double QuickTolerance = 1e-4, StandardTolerance = 1e-5;
        /// <summary>Instrument kinds that run an iterative retrieval or a 2-D scan: skipped in quick unless asked for with keepInQuick.</summary>
        public static readonly string[] ExpensiveInstruments = { "frog", "xfrog", "sfgFrog", "dscan" };

        public static FidelityPlan Parse(JsonObject run)
        {
            var plan = new FidelityPlan();
            string level = run == null ? FidelityPlan.Full : run.GetString("fidelity", FidelityPlan.Full).ToLowerInvariant();
            if (level != FidelityPlan.Full && level != FidelityPlan.Standard && level != FidelityPlan.Quick)
                throw new JsonException("fidelity must be \"full\", \"standard\" or \"quick\" (got \"" + level + "\").");
            plan.Level = level;
            return plan;
        }

        /// <summary>Applies the bundle to the parsed run spec: fills the switches the run spec leaves unset.</summary>
        public static void Apply(RunSpec r, JsonObject raw)
        {
            var plan = Parse(raw);
            r.Fidelity = plan;
            if (plan.IsFull) return;
            var phys = raw.GetObject("physics"); var step = raw.GetObject("stepper");
            bool quick = plan.Level == FidelityPlan.Quick;
            void Bool(string key, bool value, Action<bool> set, bool current)
            {
                if (phys != null && phys.Has(key)) { if (current != value) plan.Kept.Add("physics." + key + " = " + Fmt(current)); return; }
                if (current == value) return;
                set(value); plan.Changes.Add("physics." + key + ": " + Fmt(current) + " -> " + Fmt(value));
            }
            if (quick)
            {
                Bool("raman", false, v => r.Physics.Raman = v, r.Physics.Raman);
                Bool("selfSteepening", false, v => r.Physics.SelfSteepening = v, r.Physics.SelfSteepening);
                if (phys != null && phys.Has("polarization")) { if (!r.Physics.Polarization.Equals("scalar", StringComparison.OrdinalIgnoreCase)) plan.Kept.Add("physics.polarization = " + r.Physics.Polarization); }
                else if (!r.Physics.Polarization.Equals("scalar", StringComparison.OrdinalIgnoreCase))
                { plan.Changes.Add("physics.polarization: " + r.Physics.Polarization + " -> scalar"); r.Physics.Polarization = "scalar"; }
                plan.CollapseTransverse = true;
                plan.Changes.Add("spatial (transverse) models -> the 1-D beam model, on elements that have a beam to take over");
            }
            double tol = quick ? QuickTolerance : StandardTolerance;
            if (step != null && step.Has("tolerance")) { if (r.Stepper.Tolerance < tol) plan.Kept.Add("stepper.tolerance = " + r.Stepper.Tolerance.ToString("G3", CultureInfo.InvariantCulture)); }
            else if (r.Stepper.Tolerance < tol)
            { plan.Changes.Add("stepper.tolerance: " + r.Stepper.Tolerance.ToString("G3", CultureInfo.InvariantCulture) + " -> " + tol.ToString("G3", CultureInfo.InvariantCulture)); r.Stepper.Tolerance = tol; }
            if (quick)
            {
                foreach (var k in ExpensiveInstruments) plan.SkippedInstrumentKinds.Add(k);
                var skipped = r.Instruments.Where(i => plan.SkippedInstrumentKinds.Contains(i.GetString("kind", "")) && !i.GetBool("keepInQuick", false)).ToList();
                foreach (var i in skipped)
                {
                    r.Instruments.Remove(i);
                    plan.Changes.Add("instrument " + i.GetString("kind", "?") + (i.Has("name") ? " \"" + i.GetString("name", "") + "\"" : "") + " skipped (add \"keepInQuick\": true to run it)");
                }
            }
        }

        private static string Fmt(bool b) => b ? "true" : "false";

        /// <summary>
        /// After a run below full fidelity: dimensionless indicators of how much the dropped physics could matter, as warnings.
        /// B = total B-integral; s = B / (omega0 T0), the size of the self-steepening shock term (T0 = FWHM / 1.665);
        /// P / P_cr of every nonlinear element (spatial effects that the 1-D model leaves out).
        /// </summary>
        public static void Assess(RunResult res)
        {
            var cr = res.Compiled; var plan = cr?.Run?.Fidelity;
            if (plan == null || plan.IsFull) return;
            string tag = "fidelity " + plan.Level + ": ";
            double b = res.Records.Sum(x => x.BIntegralRad);
            var rep = new List<string>();
            bool quick = plan.Level == FidelityPlan.Quick;
            if (quick && b > 3) rep.Add(string.Format(CultureInfo.InvariantCulture, "B = {0:G3} rad: strongly nonlinear, so every dropped term (Raman, self-steepening, polarisation coupling) can change the result; confirm in full", b));
            double dur = res.Input != null ? PulseMetrics.Summarize(res.Input).FwhmS : double.NaN;
            double lam = cr.Run.Pulse != null ? cr.Run.Pulse.CenterWavelengthM : double.NaN;
            if (quick && plan.Changes.Any(x => x.StartsWith("physics.selfSteepening")) && b > 0 && !double.IsNaN(dur) && !double.IsNaN(lam) && dur > 0 && lam > 0)
            {
                double w0 = 2 * Math.PI * 299792458.0 / lam, s = b / (w0 * dur / 1.665);
                if (s > 0.01) rep.Add(string.Format(CultureInfo.InvariantCulture, "self-steepening was dropped and B / (omega0 T0) = {0:G2} (above 0.01): the pulse shock and spectral asymmetry are missing", s));
            }
            if (quick && b > 1 && !double.IsNaN(dur) && dur < 2e-12 && plan.Changes.Any(x => x.StartsWith("physics.raman")))
                rep.Add(string.Format(CultureInfo.InvariantCulture, "the delayed (Raman) response was dropped at B = {0:G3} rad and a {1:G3} fs pulse: it can shift the spectrum and, in gases, cause noise-driven pulse cleaning (NER)", b, dur * 1e15));
            double pcr = res.Records.Where(x => !double.IsNaN(x.CriticalPowerRatio)).Select(x => x.CriticalPowerRatio).DefaultIfEmpty(0).Max();
            if (pcr > 0.3 && plan.CollapsedElements.Count > 0) rep.Add(string.Format(CultureInfo.InvariantCulture, "peak power reaches {0:G3} x the critical power for self-focusing and the spatial model of {1} was collapsed to 1-D", pcr, string.Join(", ", plan.CollapsedElements)));
            if (plan.Changes.Any(x => x.StartsWith("physics.polarization")) && cr.Run.Pulse != null && IsMixedPolarization(cr.Run.Pulse))
                rep.Add("the input is not linearly polarised along x or y and the polarisation was made scalar: coupling between the components is lost");
            foreach (var m in rep) res.Warnings.Add(tag + "result may be unreliable: " + m + ".");
            res.FidelityReport = plan.ToJson().Set("indicators", new JsonObject().Set("bIntegralRad", b).Set("peakPowerOverCritical", pcr).Set("collapsedToOneD", JsonValue.FromArray(plan.CollapsedElements))).Set("unreliableBecause", JsonValue.FromArray(rep));
            res.Notes.Add(tag + (plan.Changes.Count == 0 ? "no switch changed (the run spec already set them)" : string.Join("; ", plan.Changes)));
        }

        private static bool IsMixedPolarization(Pulses.PulseSpec p)
        {
            var j = p.Polarization.Normalized();
            double ax = j.Ex.Magnitude, ay = j.Ey.Magnitude;
            return Math.Min(ax, ay) > 1e-3 * Math.Max(ax, ay);
        }
    }
}
