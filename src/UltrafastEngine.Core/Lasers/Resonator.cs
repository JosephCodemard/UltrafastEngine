using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Optics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Lasers
{
    /// <summary>
    /// A passive resonator around a nested element sequence (docs/reference/oscillators.md#resonators): the round-trip ABCD matrix from
    /// the tracker gives the stability and the eigenmode, which the tracked beam takes on; with a "transverse" grid the Fox-Li iteration
    /// (Bell Syst. Tech. J. 40, 453 (1961)) finds the lowest mode with its diffraction loss. A ring runs the elements once; a linear
    /// cavity runs them forward and back (end elements once per round trip). A sequence, not a graph: when branches arrive this can
    /// become a cycle in the graph with the same keys.
    /// </summary>
    public sealed class ResonatorElement : IActiveElement
    {
        public List<CompiledElement> Elements = new List<CompiledElement>();
        public List<CompiledElement> RoundTrip = new List<CompiledElement>();
        public TransverseSpec Grid;
        public int MaxRoundTrips = 400; public double Tolerance = 1e-6;
        public double StabilityX, StabilityY, RadiusXM = double.NaN, RadiusYM = double.NaN, RoundTripOpticalM, RoundTripGroupDelayS;
        public Complex Qx, Qy; public bool Stable;

        public string Kind => "resonator";
        public string Description => "resonator of " + Elements.Count + " elements";
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => Grid != null;

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            var r = ctx.Record;
            r.Values["stabilityX"] = StabilityX; r.Values["stabilityY"] = StabilityY;
            r.Values["stable"] = Stable ? 1 : 0;
            r.Values["roundTripOpticalLengthM"] = RoundTripOpticalM;
            if (RoundTripGroupDelayS > 0) r.Values["freeSpectralRangeHz"] = 1 / RoundTripGroupDelayS;
            if (Stable) { r.Values["modeRadiusXM"] = RadiusXM; r.Values["modeRadiusYM"] = RadiusYM; r.Values["modeCurvatureXPerM"] = (1 / Qx).Real; }
            if (Grid == null) return p;
            double w = Stable ? Math.Sqrt(RadiusXM * RadiusYM) : (Grid.Model == "radial" ? Grid.RadiusM : Grid.HalfWidthXM) / 3;
            var basis = Grid.CreateBasis(w);
            var g = p.Grid; var bins = TransverseOps.SampleBins(p.SpectralIntensity(), 1);
            var om = bins.Select(b => g.OmegaAbsolute[b]).ToArray();
            var cur = new PulseField(g, p.Ax, p.Ay, p.RepRateHz) { Profile = TransverseOps.FromShape(basis, null, w, w, double.PositiveInfinity, _ => 1.0, om, ctx.Element.Id) };
            double e0 = cur.TotalEnergyJ(), loss = double.NaN; int it = 0; bool conv = false;
            for (it = 1; it <= MaxRoundTrips; it++)
            {
                ctx.Cancel.ThrowIfCancellationRequested();
                var prev = cur.Profile;
                var sub = Executor.RunElements(ctx.Run, RoundTrip, cur, ctx.Cancel);
                double t = sub.Output.TotalEnergyJ() / cur.TotalEnergyJ();
                // back to the starting energy: the mode, not the decaying pulse, is what iterates
                double sc = Math.Sqrt(cur.TotalEnergyJ() / sub.Output.TotalEnergyJ());
                var next = new PulseField(g, sub.Output.Ax.Select(z => z * sc).ToArray(), sub.Output.Ay.Select(z => z * sc).ToArray(), p.RepRateHz) { Profile = sub.Output.Profile };
                double change = Math.Abs(1 - t - loss);
                loss = 1 - t; cur = next;
                if (it > 8 && change < Tolerance && Executor.Mismatch(prev.Data, next.Profile.Data) < Tolerance) { conv = true; break; }
            }
            r.Values["foxLiRoundTrips"] = Math.Min(it, MaxRoundTrips);
            r.Values["roundTripLoss"] = loss;
            r.Values["converged"] = conv ? 1 : 0;
            if (!conv) ctx.Result.Warnings.Add("Element '" + ctx.Element.Id + "': the Fox-Li iteration did not converge in " + MaxRoundTrips + " round trips.");
            var (wx, wy) = cur.Profile.SecondMomentRadius();
            r.BeamRadiusXM = wx; r.BeamRadiusYM = wy; r.Values["foxLiRadiusXM"] = wx;
            ctx.Note(string.Format(CultureInfo.InvariantCulture, "Fox-Li: round-trip loss {0:P3} after {1} round trips, mode radius {2:G4} mm", loss, r.Values["foxLiRoundTrips"], wx * 1e3));
            ctx.Energy("numerical", 0);
            var q = p.WithSpectrum(p.Ax, p.Ay); q.Profile = cur.Profile;
            return q;
        }
    }

    public static class ResonatorCompiler
    {
        public static void Compile(CompilerServices c, SystemElement e, CompiledElement ce)
        {
            var j = e.Json; string origin = "resonator '" + e.Id + "'";
            var arr = j.GetArray("elements");
            if (arr == null || arr.Count == 0) throw new JsonException(origin + " needs \"elements\".");
            string geo = j.GetString("geometry", "ring").ToLowerInvariant();
            if (geo != "ring" && geo != "linear") throw new JsonException(origin + ": geometry must be \"ring\" or \"linear\".");
            var el = new ResonatorElement();
            var gv = j["transverse"];
            if (gv != null && !gv.IsNull) { el.Grid = TransverseSpec.FromJson(gv); if (!el.Grid.IsSpatial) el.Grid = null; }
            var rto = j.GetObject("roundTrips");
            if (rto != null) { el.MaxRoundTrips = rto.GetInt("max", el.MaxRoundTrips); el.Tolerance = rto.GetDouble("tolerance", el.Tolerance); }
            // the nested elements are compiled with a probe beam: the ray matrix of each comes from the tracker
            double lam = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0;
            var saved = c.Tracked;
            c.SetTracked(new BeamState(new Complex(0, 1), new Complex(0, 1), lam));
            var seg = new List<(double[] X, double[] Y)>();
            foreach (var v in arr)
            {
                var o = v.AsObject();
                string type = o.GetString("type", "");
                if (string.Equals(type, "oscillator", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "resonator", StringComparison.OrdinalIgnoreCase)) throw new JsonException(origin + ": cavities cannot be nested.");
                if (el.Grid != null && (type == "gap" || type == "bulk") && !o.Has("transverse")) o.Set("transverse", gv);
                var inner = c.CompileNestedOnBeam(o, e.Id);
                el.Elements.Add(inner);
                seg.Add((BeamState.Segment(inner.BeamIn.MatrixX, inner.BeamOut.MatrixX), BeamState.Segment(inner.BeamIn.MatrixY, inner.BeamOut.MatrixY)));
            }
            c.SetTracked(saved);
            var order = Enumerable.Range(0, el.Elements.Count).ToList();
            if (geo == "linear") order.AddRange(Enumerable.Range(1, Math.Max(0, el.Elements.Count - 2)).Select(k => el.Elements.Count - 1 - k));
            el.RoundTrip = order.Select(k => el.Elements[k]).ToList();
            var mx = new[] { 1.0, 0, 0, 1 }; var my = new[] { 1.0, 0, 0, 1 };
            for (int i = 0; i < order.Count; i++)
            {
                // on the way back an element acts with its matrix reversed: (A, B, C, D) becomes (D, B, C, A)
                bool back = i >= el.Elements.Count;
                double[] Pass(double[] m) => back ? new[] { m[3], m[1], m[2], m[0] } : m;
                mx = BeamState.Multiply(Pass(seg[order[i]].X), mx); my = BeamState.Multiply(Pass(seg[order[i]].Y), my);
            }
            el.RoundTripOpticalM = order.Sum(k => el.Elements[k].LengthM * (el.Elements[k].TrackingIndex > 0 ? el.Elements[k].TrackingIndex : 1.0));
            // the group delay where the material gives one; an air gap's is relative to air, so its optical path counts
            el.RoundTripGroupDelayS = order.Sum(k => Math.Max(el.Elements[k].GroupDelayAt(2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / lam),
                el.Elements[k].LengthM * (el.Elements[k].TrackingIndex > 0 ? el.Elements[k].TrackingIndex : 1.0) / PhysicalConstants.SpeedOfLightMPerS));
            el.StabilityX = (mx[0] + mx[3]) / 2; el.StabilityY = (my[0] + my[3]) / 2;
            el.Stable = Math.Abs(el.StabilityX) < 1 && Math.Abs(el.StabilityY) < 1 && mx[1] != 0 && my[1] != 0;
            if (el.Stable)
            {
                el.Qx = Eigen(mx); el.Qy = Eigen(my);
                var beam = new BeamState(el.Qx, el.Qy, lam);
                el.RadiusXM = beam.RadiusX; el.RadiusYM = beam.RadiusY;
                if (saved != null) c.SetTracked(beam);
                ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "resonator: stable, (A + D) / 2 = {0:G4} / {1:G4}, mode radius {2:G4} x {3:G4} mm at the reference plane", el.StabilityX, el.StabilityY, el.RadiusXM * 1e3, el.RadiusYM * 1e3));
            }
            else c.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "{0} is unstable: (A + D) / 2 = {1:G4} / {2:G4}; it has no Gaussian eigenmode.", origin, el.StabilityX, el.StabilityY));
            ce.Active = el;
            c.Provenance.Record("element:" + e.Id, "resonator", el.Description + ", " + geo, ModelSource.SystemData, "system file");
        }

        /// <summary>The self-consistent q of a stable round trip: 1/q = (D - A) / 2B - i sqrt(1 - m^2) / |B|, m = (A + D) / 2.</summary>
        private static Complex Eigen(double[] m)
        {
            double h = (m[0] + m[3]) / 2;
            return 1 / new Complex((m[3] - m[0]) / (2 * m[1]), -Math.Sqrt(1 - h * h) / Math.Abs(m[1]));
        }
    }
}
