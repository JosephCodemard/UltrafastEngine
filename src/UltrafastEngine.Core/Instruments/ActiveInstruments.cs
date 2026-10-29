using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Crystals;
using UltrafastEngine.Engine;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Instruments
{
    internal static class ActiveTools
    {
        public const double Hbar = 1.054571817e-34;

        public static CompiledElement Element(InstrumentContext ctx, out ElementRecord rec)
        {
            string id = ctx.Location.Split('#')[0];
            rec = ctx.Run.Records.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            return ctx.Run.Compiled.Elements.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static CrystalElement Crystal(InstrumentContext ctx, out ElementRecord rec)
        {
            var el = Element(ctx, out rec);
            return el?.Active as CrystalElement ?? throw new InvalidOperationException("\"at\" must be the id of a crystal element (got \"" + ctx.Location + "\")");
        }

        /// <summary>The pulse entering element <paramref name="id"/> (the previous enabled element's output, or the input).</summary>
        public static PulseField InputOf(RunResult res, string id)
        {
            int k = res.Records.FindIndex(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            if (k <= 0) return res.Input;
            return res.At[res.Records[k - 1].Id];
        }
    }

    /// <summary>
    /// "conversion" at a crystal: energy and photon number of every field leaving it (main beam and by-products),
    /// the aux energy that went in, and the conversion efficiency.
    /// </summary>
    public sealed class ConversionMeter : IInstrument
    {
        public string Kind => "conversion";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            ActiveTools.Element(ctx, out var rec);
            if (rec == null) throw new InvalidOperationException("conversion: \"at\" must be an element id");
            var p = ctx.Run.At[rec.Id];
            double ein = rec.EnergyInJ;
            double auxIn = -rec.LossJ.Where(kv => kv.Key.StartsWith("from ")).Sum(kv => kv.Value);
            Tools.Put(o, "inputEnergyJ", ein);
            Tools.Put(o, "auxEnergyInJ", auxIn);
            Tools.Put(o, "mainEnergyOutJ", rec.EnergyOutJ);
            Tools.Put(o, "efficiencyVsInput", rec.EnergyOutJ / Math.Max(1e-300, ein));
            Tools.Put(o, "efficiencyVsTotalIn", rec.EnergyOutJ / Math.Max(1e-300, ein + auxIn));
            Tools.Put(o, "mainPhotons", rec.EnergyOutJ / (ActiveTools.Hbar * p.Grid.OmegaCenter));
            foreach (var kv in rec.Byproducts)
            {
                if (!ctx.Run.At.TryGetValue(rec.Id + "#" + kv.Key, out var bp)) continue;
                Tools.Put(o, kv.Key + "EnergyJ", kv.Value.EnergyJ);
                Tools.Put(o, kv.Key + "Photons", kv.Value.EnergyJ / (ActiveTools.Hbar * bp.Grid.OmegaCenter));
                Tools.Put(o, kv.Key + "CentroidWavelengthM", kv.Value.CentroidWavelengthM);
            }
        }
    }

    /// <summary>
    /// "phaseMatchingCurve" at a crystal: the low-conversion response sinc^2(Delta k L / 2) of the crystal's own
    /// geometry vs one parameter -- "scan": "angle" (deg around the cut, "rangeDeg"), "wavelength" (of the main beam,
    /// "rangeM"), "temperature" (LiNbO3, "rangeK") or "period" (QPM, "rangeM").
    /// </summary>
    public sealed class PhaseMatchingCurve : IInstrument
    {
        public string Kind => "phaseMatchingCurve";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var cr = ActiveTools.Crystal(ctx, out var rec);
            string scan = ctx.Spec.GetString("scan", "angle").ToLowerInvariant();
            int pts = ctx.Spec.GetInt("points", 201);
            double wMain = ActiveTools.InputOf(ctx.Run, cr.Id).Grid.OmegaCenter;
            double wAux = cr.Aux.Count > 0 ? cr.Aux[0].CenterOmega() : double.NaN;
            var x = new double[pts]; var s2 = new double[pts]; var dks = new double[pts];
            double range = ctx.Spec.GetDouble(scan == "angle" ? "rangeDeg" : scan == "temperature" ? "rangeK" : "rangeM", scan == "angle" ? 1.0 : scan == "temperature" ? 20 : scan == "period" ? 0.2e-6 : 20e-9);
            var keepCrystal = cr.Crystal; double keepTheta = cr.ThetaRad, keepPhi = cr.PhiRad;
            for (int k = 0; k < pts; k++)
            {
                double d = -range / 2 + range * k / (pts - 1);
                double w = wMain; double kg = cr.Qpm ? Math.Sign(1.0) * 2 * Math.PI / cr.QpmPeriodM : 0;
                switch (scan)
                {
                    case "angle": if (cr.Plane == "XY") cr.PhiRad = keepPhi + d * Math.PI / 180; else cr.ThetaRad = keepTheta + d * Math.PI / 180; x[k] = d; break;
                    case "wavelength": { double lam = 2 * Math.PI * Tools.C / wMain + d; w = 2 * Math.PI * Tools.C / lam; x[k] = lam; break; }
                    case "temperature":
                        if (!keepCrystal.TemperatureDependent) throw new InvalidOperationException("phaseMatchingCurve: " + keepCrystal.Name + " has no temperature-dependent indices");
                        cr.Crystal = CrystalLibrary.Get(keepCrystal.Name.StartsWith("MgO") ? "MgO:LiNbO3" : "LiNbO3", keepCrystal.TemperatureC + d); x[k] = keepCrystal.TemperatureC + d; break;
                    case "period": if (!cr.Qpm) throw new InvalidOperationException("phaseMatchingCurve: scan \"period\" needs a QPM crystal"); kg = 2 * Math.PI / (cr.QpmPeriodM + d); x[k] = cr.QpmPeriodM + d; break;
                    default: throw new InvalidOperationException("phaseMatchingCurve: scan must be angle, wavelength, temperature or period");
                }
                double w1, w2; int refW;
                if (cr.Process == "shg") { w1 = w2 = w; refW = 1; }
                else if (cr.Process == "sfg") { if (w <= wAux) { w1 = w; w2 = wAux; refW = 1; } else { w1 = wAux; w2 = w; refW = 2; } }
                else
                {
                    double wp = cr.MainRole == "pump" ? w : cr.Aux.First(a => a.Role == "pump").CenterOmega();
                    double ws = cr.MainRole == "pump" ? cr.Aux.First().CenterOmega() : w;
                    double wi = wp - ws; if (ws <= wi) { w1 = ws; w2 = wi; refW = 1; } else { w1 = wi; w2 = ws; refW = 2; }
                }
                var tw = cr.Geometry(refW);
                var g = tw.At(w1, w2);
                double dk = g.DeltaK - (cr.Qpm ? Math.Sign(g.DeltaK) * kg : 0);
                if (cr.Qpm && scan != "period") dk = g.DeltaK - Math.Sign(g.DeltaK) * 2 * Math.PI / cr.QpmPeriodM;
                double a = dk * cr.LengthM / 2;
                dks[k] = dk; s2[k] = Math.Abs(a) < 1e-12 ? 1 : Math.Pow(Math.Sin(a) / a, 2);
            }
            cr.Crystal = keepCrystal; cr.ThetaRad = keepTheta; cr.PhiRad = keepPhi;
            string unit = scan == "angle" ? "deg" : scan == "temperature" ? "C" : "m";
            o.Add(scan == "angle" ? "detuning_deg" : scan == "temperature" ? "temperature_c" : scan == "period" ? "period_m" : "wavelength_m", x, unit, "scan variable");
            o.Add("efficiency_norm", s2, "norm.", "low-conversion efficiency sinc^2(dk L/2)");
            o.Add("delta_k_per_m", dks, "1/m", "phase mismatch");
            int best = Array.IndexOf(s2, s2.Max());
            Tools.Put(o, "peakAt", x[best]);
            double half = 0.5; int lo = best, hi = best;
            while (lo > 0 && s2[lo] > half) lo--; while (hi < pts - 1 && s2[hi] > half) hi++;
            Tools.Put(o, "fwhm", x[hi] - x[lo]);
        }
    }

    /// <summary>
    /// "delayScan" at a crystal: re-runs the crystal for aux delays "fromS" .. "toS" ("points") on the pulse that
    /// entered it, and reports the output energy (and conversion) vs delay -- the OPCPA timing curve.
    /// </summary>
    public sealed class DelayScan : IInstrument
    {
        public string Kind => "delayScan";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var cr = ActiveTools.Crystal(ctx, out var rec);
            if (cr.Aux.Count == 0) throw new InvalidOperationException("delayScan: the crystal has no auxiliary beam");
            var pin = ActiveTools.InputOf(ctx.Run, cr.Id);
            double from = ctx.Spec.GetDouble("fromS", -1e-12), to = ctx.Spec.GetDouble("toS", 1e-12);
            int pts = Math.Max(2, ctx.Spec.GetInt("points", 21));
            var d = new double[pts]; var e = new double[pts];
            var el = ctx.Run.Compiled.Elements.First(x => ReferenceEquals(x.Active, cr));
            for (int k = 0; k < pts; k++)
            {
                d[k] = from + (to - from) * k / (pts - 1);
                var r = new ElementRecord { Id = cr.Id, EnergyInJ = pin.TotalEnergyJ() };
                var actx = new ActiveContext { Run = ctx.Run.Compiled, Element = el, Record = r, Result = ctx.Run, ArrivalS = 0 };
                var outp = cr.Run(actx, pin, d[k], out _);
                e[k] = outp.TotalEnergyJ();
            }
            o.Add("delay_s", d, "s", "aux delay relative to the main beam");
            o.Add("output_energy_j", e, "J", "energy of the output field");
            int b = Array.IndexOf(e, e.Max());
            Tools.Put(o, "optimalDelayS", d[b]);
            Tools.Put(o, "maxOutputEnergyJ", e[b]);
        }
    }

    /// <summary>
    /// "beamProfile": fluence across the beam where a transverse profile is carried (after a spatial element):
    /// radial fluence (radial) or x / y cuts (cartesian), second-moment radius, M^2, true peak fluence.
    /// </summary>
    public sealed class BeamProfileInstrument : IInstrument
    {
        public string Kind => "beamProfile";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var p = ctx.Pulse;
            if (p.Profile == null) throw new InvalidOperationException("beamProfile: no transverse profile at \"" + ctx.Location + "\" (place it after an element with a radial/cartesian transverse model)");
            var prof = p.Profile; var B = prof.Basis;
            // fluence from the stored frequency samples, each carrying the energy of its spectral band (as the cameras do)
            var flu = TransverseOps.SampleFluence(prof, p);
            var (wx, wy) = prof.SecondMomentRadius();
            Tools.Put(o, "radiusXM", wx); Tools.Put(o, "radiusYM", wy);
            Tools.Put(o, "m2", TransverseOps.BeamQuality(prof));
            Tools.Put(o, "peakFluenceJPerM2", flu.Max());
            Tools.Put(o, "energyJ", p.TotalEnergyJ());
            if (B is RadialBasis)
            {
                o.Add("radius_m", (double[])B.Radius.Clone(), "m", "radius");
                o.Add("fluence_j_m2", flu, "J/m^2", "fluence");
            }
            else
            {
                var cb = (CartesianBasis)B;
                var xs = new double[cb.Nx]; var fx = new double[cb.Nx];
                for (int ix = 0; ix < cb.Nx; ix++) { int m = ix * cb.Ny + cb.Ny / 2; xs[ix] = cb.X[m]; fx[ix] = flu[m]; }
                o.Add("x_m", xs, "m", "x (cut through the axis)");
                o.Add("fluence_x_j_m2", fx, "J/m^2", "fluence along x");
                if (cb.Ny > 1)
                {
                    var ys = new double[cb.Ny]; var fy = new double[cb.Ny];
                    for (int iy = 0; iy < cb.Ny; iy++) { int m = (cb.Nx / 2) * cb.Ny + iy; ys[iy] = cb.Y[m]; fy[iy] = flu[m]; }
                    o.Add("y_m", ys, "m", "y (cut through the axis)");
                    o.Add("fluence_y_j_m2", fy, "J/m^2", "fluence along y");
                    o.Add("fluence_map_j_m2", flu, "J/m^2", "fluence map [x][y]", cb.Nx, cb.Ny);
                }
            }
        }
    }
}
