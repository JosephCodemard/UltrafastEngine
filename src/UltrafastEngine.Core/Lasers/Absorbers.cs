using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Lasers
{
    /// <summary>
    /// A saturable absorber acting on the main beam in the time domain (docs/oscillators.md):
    /// <list type="bullet">
    /// <item>"slow" (SESAM, default): power loss q(t) with dq/dt = -(q - q0)/tau_A - q P(t)/E_sat,A (recovery time,
    /// saturation fluence x spot area), plus a non-saturable loss;</item>
    /// <item>"fast": q = q0 / (1 + P / P_sat) (e.g. an ideal fast absorber, NPE);</item>
    /// <item>"kerrLens": q = max(0, q0 - kappa P) -- the lumped self-amplitude modulation of Kerr-lens mode-locking.</item>
    /// </list>
    /// Field transmission exp(-(q(t) + q_ns)/2). Works as a reflector (SESAM) or a transmissive element.
    /// </summary>
    public sealed class SaturableAbsorber
    {
        public string Kind = "slow";
        public double ModulationDepth = 0.02, NonsaturableLoss, RecoveryTimeS = 1e-12;
        public double SaturationEnergyJ = double.NaN, SaturationFluence = double.NaN, AreaM2 = double.NaN, SaturationPowerW = double.NaN, KappaPerW = double.NaN;

        public static SaturableAbsorber FromJson(JsonObject o, Func<string, double, double> num, IBeamModel beam, string origin)
        {
            var a = new SaturableAbsorber { Kind = o.GetString("kind", o.GetString("absorber", "slow")).ToLowerInvariant() };
            if (a.Kind == "sesam") a.Kind = "slow";
            if (a.Kind == "kerrlens" || a.Kind == "klm") a.Kind = "kerrlens";
            if (a.Kind != "slow" && a.Kind != "fast" && a.Kind != "kerrlens") throw new JsonException(origin + ": absorber kind must be slow (sesam), fast or kerrLens");
            a.ModulationDepth = num("modulationDepth", 0.02);
            a.NonsaturableLoss = num("nonsaturableLoss", 0);
            a.RecoveryTimeS = num("recoveryTimeS", 1e-12);
            a.SaturationEnergyJ = num("saturationEnergyJ", double.NaN);
            a.SaturationFluence = num("saturationFluenceJPerM2", double.NaN);
            a.SaturationPowerW = num("saturationPowerW", double.NaN);
            a.KappaPerW = num("kappaPerW", double.NaN);
            double area = num("areaM2", double.NaN), w = num("spotRadiusM", double.NaN);
            if (double.IsNaN(area) && w > 0) area = Math.PI * w * w / 2;
            if (double.IsNaN(area) && beam != null) area = beam.EffectiveArea(0);
            a.AreaM2 = area;
            double isat = num("saturationIntensityWPerM2", double.NaN);
            if (double.IsNaN(a.SaturationPowerW) && isat > 0 && area > 0) a.SaturationPowerW = isat * area;
            if (a.Kind == "slow" && double.IsNaN(a.SaturationEnergyJ))
            {
                if (a.SaturationFluence > 0 && area > 0) a.SaturationEnergyJ = a.SaturationFluence * area;
                else throw new JsonException(origin + ": a slow absorber needs saturationEnergyJ, or saturationFluenceJPerM2 with spotRadiusM / areaM2 / beam");
            }
            if (a.Kind == "fast" && !(a.SaturationPowerW > 0)) throw new JsonException(origin + ": a fast absorber needs saturationPowerW (or saturationIntensityWPerM2 with a spot size)");
            if (a.Kind == "kerrlens" && !(a.KappaPerW > 0)) throw new JsonException(origin + ": a kerrLens absorber needs kappaPerW (loss reduction per W of power)");
            if (!(a.ModulationDepth >= 0 && a.ModulationDepth < 1)) throw new JsonException(origin + ": modulationDepth must be in [0, 1)");
            return a;
        }

        public string Describe()
        {
            var ci = CultureInfo.InvariantCulture;
            switch (Kind)
            {
                case "fast": return string.Format(ci, "fast saturable absorber, q0 = {0:P3}, P_sat = {1:G4} W", ModulationDepth, SaturationPowerW);
                case "kerrlens": return string.Format(ci, "Kerr-lens (fast) loss modulation, q0 = {0:P3}, kappa = {1:G4} /W", ModulationDepth, KappaPerW);
                default: return string.Format(ci, "SESAM: dR = {0:P3}, ns loss {1:P3}, E_sat = {2:G4} J, tau = {3:G4} ps", ModulationDepth, NonsaturableLoss, SaturationEnergyJ, RecoveryTimeS * 1e12);
            }
        }

        /// <summary>Apply to the time-domain fields in place; returns the loss q(t) actually applied (power, without the non-saturable part).</summary>
        public double[] Apply(Complex[] tx, Complex[] ty, double dt)
        {
            int n = tx.Length; var q = new double[n];
            double qq = ModulationDepth;
            double decay = Kind == "slow" ? Math.Exp(-dt / Math.Max(1e-30, RecoveryTimeS)) : 0;
            for (int i = 0; i < n; i++)
            {
                double P = tx[i].Real * tx[i].Real + tx[i].Imaginary * tx[i].Imaginary + (ty != null ? ty[i].Real * ty[i].Real + ty[i].Imaginary * ty[i].Imaginary : 0);
                switch (Kind)
                {
                    case "fast": qq = ModulationDepth / (1 + P / SaturationPowerW); break;
                    case "kerrlens": qq = Math.Max(0, ModulationDepth - KappaPerW * P); break;
                    default:
                        // recovery, then saturation by this sample's energy (exact for each sub-step), loss at the midpoint
                        qq = ModulationDepth + (qq - ModulationDepth) * decay;
                        double s = Math.Exp(-0.5 * P * dt / SaturationEnergyJ);
                        qq *= s; q[i] = qq; qq *= s;
                        double f0 = Math.Exp(-0.5 * (q[i] + NonsaturableLoss));
                        tx[i] *= f0; if (ty != null) ty[i] *= f0;
                        continue;
                }
                q[i] = qq;
                double f = Math.Exp(-0.5 * (qq + NonsaturableLoss));
                tx[i] *= f; if (ty != null) ty[i] *= f;
            }
            return q;
        }

        /// <summary>Apply to a pulse (spectral fields); returns the new pulse.</summary>
        public PulseField Apply(PulseField p, out double[] q)
        {
            var tx = p.TemporalAx(); var ty = p.Ay.Any(z => z != Complex.Zero) ? p.TemporalAy() : null;
            q = Apply(tx, ty, p.Grid.DTime);
            return p.WithSpectrum(SpectralTransform.TimeToFreq(tx), ty != null ? SpectralTransform.TimeToFreq(ty) : p.Ay);
        }

        /// <summary>Energy saturation parameter E / E_sat (slow absorbers; NaN otherwise).</summary>
        public double Saturation(double energyJ) => Kind == "slow" ? energyJ / SaturationEnergyJ : double.NaN;
    }

    /// <summary>The `saturableAbsorber` / `sesam` element.</summary>
    public sealed class SaturableAbsorberElement : IActiveElement
    {
        public SaturableAbsorber A;
        public string Kind => "saturableAbsorber";
        public string Description => A.Describe();
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => true;   // a thin element: the carried profile stays valid

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            double e0 = p.TotalEnergyJ();
            var q = A.Apply(p, out var qt);
            double e1 = q.TotalEnergyJ();
            ctx.Energy("saturable absorption", e0 - e1);
            ctx.Record.Values["lowSignalLoss"] = A.ModulationDepth + A.NonsaturableLoss;
            ctx.Record.Values["pulseLoss"] = 1 - e1 / Math.Max(1e-300, e0);
            if (A.Kind == "slow") ctx.Record.Values["saturationParameter"] = A.Saturation(e0);
            // q(t) over the pulse support
            var pw = Results.PulseMetrics.TemporalPower(p); double pk = pw.Max();
            int lo = 0, hi = pw.Length - 1; while (lo < hi && pw[lo] < 1e-4 * pk) lo++; while (hi > lo && pw[hi] < 1e-4 * pk) hi--;
            int pad = Math.Max(4, (hi - lo) / 2); lo = Math.Max(0, lo - pad); hi = Math.Min(pw.Length - 1, hi + pad);
            int step = Math.Max(1, (hi - lo + 1) / 1024);
            var idx = Enumerable.Range(0, (hi - lo) / step + 1).Select(k => lo + k * step).Where(i => i <= hi).ToArray();
            ctx.Record.AddArray("timeS", idx.Select(i => p.Grid.Time[i]).ToArray(), "s", "time axis of the absorber loss");
            ctx.Record.AddArray("loss", idx.Select(i => qt[i]).ToArray(), "", "saturable power loss q(t) seen by the pulse");
            return q;
        }

        public static void Compile(CompilerServices c, SystemElement e, CompiledElement ce)
        {
            var j = e.Json; IBeamModel beam = null;
            if (j.Has("beam"))
            {
                var bs = ModelSpec.FromJson(j["beam"], ModelSource.SystemData, "element " + e.Id + ".beam");
                if (bs.Kind == "gaussian" && !bs.Json.Has("wavelengthM") && !bs.Json.Has("zRayleighM")) bs.Json.Set("wavelengthM", 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0);
                beam = c.Registry.Create<IBeamModel>("beam", bs);
            }
            var a = SaturableAbsorber.FromJson(j, (k, f) => c.Num(e, k, f), beam, "element " + e.Id);
            ce.Active = new SaturableAbsorberElement { A = a };
            c.Provenance.Record("element:" + e.Id, "saturableAbsorber", a.Describe(), ModelSource.SystemData, "system file");
        }
    }

    /// <summary>
    /// An aperture that acts on a carried transverse profile (hard circular / rectangular, or soft Gaussian of
    /// order n): the profile is masked at every stored frequency, the spectrum scaled by the frequency-resolved
    /// transmission. Without a profile it falls back to the 1-D clip loss on the beam model.
    /// </summary>
    public sealed class ApertureElement : IActiveElement
    {
        public double RadiusM = double.NaN, HalfX = double.NaN, HalfY = double.NaN, SoftRadiusM = double.NaN, CenterX, CenterY;
        public int SoftOrder = 1;
        public List<LinearTerm> Fallback = new List<LinearTerm>();
        public string Kind => "aperture";
        public string Description
        {
            get
            {
                var ci = CultureInfo.InvariantCulture;
                if (SoftRadiusM > 0) return string.Format(ci, "soft aperture (Gaussian{0}), a = {1:G4} mm", SoftOrder > 1 ? " order " + SoftOrder : "", SoftRadiusM * 1e3);
                if (RadiusM > 0) return string.Format(ci, "circular aperture r = {0:G4} mm", RadiusM * 1e3);
                return string.Format(ci, "rectangular aperture {0:G4} x {1:G4} mm", 2 * HalfX * 1e3, 2 * HalfY * 1e3);
            }
        }
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => true;

        public double Mask(double x, double y)
        {
            x -= CenterX; y -= CenterY;
            if (SoftRadiusM > 0) { double u = (x * x + y * y) / (SoftRadiusM * SoftRadiusM); return Math.Exp(-Math.Pow(u, SoftOrder)); }   // amplitude: intensity exp(-2 (r/a)^2n)
            if (RadiusM > 0) return x * x + y * y <= RadiusM * RadiusM ? 1 : 0;
            if (HalfX > 0) return Math.Abs(x) <= HalfX && Math.Abs(y) <= HalfY ? 1 : 0;
            return 1;
        }

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            if (p.Profile == null)
            {
                var own = new DispersionPoint();
                foreach (var t in Fallback) p = Executor.ApplyTerm(p, t, OmegaAxis.FromGrid(p.Grid), ctx.Record, ref own);
                if (Fallback.Count == 0) ctx.Note("aperture: no transverse profile and no beam size -- nothing clipped");
                return p;
            }
            var prof = p.Profile.Clone(); var B = prof.Basis; int nf = prof.Omega.Length, M = B.M;
            var mask = Enumerable.Range(0, M).Select(m => Mask(B.X[m], B.Y[m])).ToArray();
            var T = new double[nf];
            for (int f = 0; f < nf; f++)
            {
                double s = 0;
                for (int m = 0; m < M; m++) { var v = prof.Data[m * nf + f] * mask[m]; prof.Data[m * nf + f] = v; s += B.Weights[m] * (v.Real * v.Real + v.Imaginary * v.Imaginary); }
                T[f] = s;
                double norm = s > 0 ? 1 / Math.Sqrt(s) : 0;
                for (int m = 0; m < M; m++) prof.Data[m * nf + f] *= norm;
            }
            var g = p.Grid; var ax = (Complex[])p.Ax.Clone(); var ay = (Complex[])p.Ay.Clone();
            for (int i = 0; i < g.N; i++)
            {
                double w = g.OmegaAbsolute[i], t;
                if (nf == 1 || w <= prof.Omega[0]) t = T[0];
                else if (w >= prof.Omega[nf - 1]) t = T[nf - 1];
                else { int k = 1; while (prof.Omega[k] < w) k++; double u = (w - prof.Omega[k - 1]) / (prof.Omega[k] - prof.Omega[k - 1]); t = T[k - 1] * (1 - u) + T[k] * u; }
                double a = Math.Sqrt(Math.Max(0, t)); ax[i] *= a; ay[i] *= a;
            }
            double e0 = p.TotalEnergyJ();
            var q = p.WithSpectrum(ax, ay); q.Profile = prof;
            ctx.Energy("clip", e0 - q.TotalEnergyJ());
            ctx.Record.Values["transmission"] = q.TotalEnergyJ() / Math.Max(1e-300, e0);
            return q;
        }
    }
}
