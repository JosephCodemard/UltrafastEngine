using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Engine
{
    /// <summary>
    /// The <c>fiber</c> element (docs/reference/fibers.md): a single-mode fibre or waveguide described the way fibre
    /// codes describe it -- Taylor coefficients beta_k (or a dispersion curve D(lambda), or a material), the nonlinear
    /// coefficient gamma (constant or gamma(z)), loss in dB/km, and gain (flat in dB/m, or a gain spectrum scaled to
    /// reach an output energy) -- compiled into a nonlinear bulk element with an effective area. Raman (Blow-Wood,
    /// Lin-Agrawal, Hollenbeck-Cantrell), self-steepening and the adaptive interaction-picture solver apply as for any
    /// medium. At run time the element reports the soliton number, dispersion and nonlinear lengths, the fission length
    /// and the dispersive-wave resonance for the pulse that actually enters it.
    /// </summary>
    public static partial class Compiler
    {
        private static void CompileFiber(Ctx c, SystemElement e, CompiledElement ce)
        {
            double L = Num(c, e, "lengthM", double.NaN);
            if (double.IsNaN(L) || L < 0) throw new JsonException("fiber needs a non-negative \"lengthM\".");
            double w0 = c.Omega0;
            double wRef = e.Json.Has("referenceWavelengthM") ? 2 * Math.PI * Cl / Num(c, e, "referenceWavelengthM", 0) : w0;
            double nEff = Num(c, e, "effectiveIndex", 1.45);

            // ---- dispersion: Taylor coefficients, a D(lambda) / beta2(lambda) curve, or a material
            IRefractiveIndex index; string dispDesc; Func<double, double> beta2At;
            double[] betas = null;
            if (e.Json.Has("betas") || e.Json.Has("betasPsKm"))
            {
                bool ps = !e.Json.Has("betas");
                var raw = e.Json.GetDoubleArray(ps ? "betasPsKm" : "betas");
                betas = new double[raw.Length];
                for (int k = 0; k < raw.Length; k++) betas[k] = ps ? raw[k] * Math.Pow(1e-12, k + 2) / 1e3 : raw[k];   // ps^n/km -> s^n/m
                // an override of "betas" replaces the array (fits and studies vary single coefficients through it)
                var bo = Find(c, e, "betas"); if (bo != null) betas = bo.RawValue.AsDoubleArray();
                index = new TaylorFiberIndex(wRef, nEff, betas);
                var bb = betas;
                beta2At = w => { double d = w - wRef, s = 0, f = 1; for (int k = 0; k < bb.Length; k++) { s += bb[k] * Math.Pow(d, k) / f; f *= k + 1; } return s; };
                dispDesc = "Taylor beta_2..beta_" + (betas.Length + 1) + " at " + (2 * Math.PI * Cl / wRef * 1e9).ToString("G6", CultureInfo.InvariantCulture) + " nm";
            }
            else if (e.Json.Has("dispersion"))
            {
                var d = e.Json.GetObject("dispersion") ?? throw new JsonException("fiber \"dispersion\": an object {wavelengthsM, dPsNmKm | beta2S2PerM}");
                var wl = d.GetDoubleArray("wavelengthsM") ?? d.GetDoubleArray("wavelengthsNm")?.Select(x => x * 1e-9).ToArray() ?? throw new JsonException("fiber dispersion needs wavelengthsM");
                double[] b2;
                if (d.Has("dPsNmKm")) { var D = d.GetDoubleArray("dPsNmKm"); b2 = wl.Select((l, i) => -l * l * D[i] * 1e-6 / (2 * Math.PI * Cl)).ToArray(); }   // ps/(nm km) = 1e-6 s/m^2
                else b2 = d.GetDoubleArray("beta2S2PerM") ?? throw new JsonException("fiber dispersion needs dPsNmKm or beta2S2PerM");
                var fi = new TabulatedFiberIndex(w0, nEff, wl, b2);
                index = fi; beta2At = fi.Beta2At;
                dispDesc = "dispersion curve (" + wl.Length + " points, " + (wl.Min() * 1e9).ToString("G4", CultureInfo.InvariantCulture) + "-" + (wl.Max() * 1e9).ToString("G4", CultureInfo.InvariantCulture) + " nm)";
            }
            else if (e.Json.Has("material"))
            {
                var m0 = GetMaterial(c, Str(c, e, "material", null));
                index = m0.Index;
                beta2At = w => { double h = w * 1e-3; var o = new[] { w - h, w, w + h }; var n = new double[3]; m0.Index.Evaluate(o, n); return (n[2] * o[2] - 2 * n[1] * o[1] + n[0] * o[0]) / (Cl * h * h); };
                dispDesc = "material " + m0.Name;
            }
            else throw new JsonException("fiber needs its dispersion: \"betas\" (s^n/m from beta2), \"betasPsKm\" (ps^n/km), \"dispersion\" {wavelengthsM, dPsNmKm} or \"material\".");

            // ---- nonlinearity: gamma (number or expression in z) or n2 + effective area
            double aeff = e.Json.Has("effectiveAreaM2") ? Num(c, e, "effectiveAreaM2", 1e-11)
                : e.Json.Has("modeFieldDiameterM") ? Math.PI * Math.Pow(Num(c, e, "modeFieldDiameterM", 0) / 2, 2) : double.NaN;
            double n2; double gamma0; string gammaExpr = null;
            if (e.Json.Has("gamma"))
            {
                var gv = Find(c, e, "gamma")?.RawValue ?? e.Json["gamma"];
                if (gv.Kind == JsonKind.String)
                {
                    gammaExpr = gv.AsString();
                    gamma0 = CompiledExpression.Compile(gammaExpr, new[] { "z" }, null).EvaluateScalar(new Dictionary<string, double> { { "z", 0.0 } });
                }
                else gamma0 = gv.AsDouble();
                if (double.IsNaN(aeff)) aeff = 1e-11;   // only gamma matters: the area is a bookkeeping choice
                n2 = gamma0 * Cl * aeff / w0;
            }
            else
            {
                n2 = Num(c, e, "n2", 2.6e-20);
                if (double.IsNaN(aeff)) throw new JsonException("fiber needs \"gamma\" (1/(W m)), or \"n2\" with \"effectiveAreaM2\" / \"modeFieldDiameterM\".");
                gamma0 = n2 * w0 / (Cl * aeff);
            }

            // ---- loss and gain (power coefficients, 1/m; negative = gain)
            Func<double, double> loss = PerWavelengthOrNumber(e.Json, "lossDbPerKm", "loss", "dbPerKm", 0);
            double flatGain = Num(c, e, "gainDbPerM", 0);
            JsonObject gainSpec = e.Json.GetObject("gain");
            Func<double, double> gainShape = null;
            if (gainSpec != null)
            {
                var gw = gainSpec.GetDoubleArray("wavelengthsM") ?? gainSpec.GetDoubleArray("wavelengthsNm")?.Select(x => x * 1e-9).ToArray() ?? throw new JsonException("fiber gain needs wavelengthsM");
                var gd = gainSpec.GetDoubleArray("dbPerM") ?? gainSpec.GetDoubleArray("relative") ?? throw new JsonException("fiber gain needs dbPerM (or a relative shape)");
                var ow = gw.Select(l => 2 * Math.PI * Cl / l).ToArray(); var ord = Enumerable.Range(0, ow.Length).OrderBy(i => ow[i]).ToArray();
                var xs = ord.Select(i => ow[i]).ToArray(); var ys = ord.Select(i => gd[i]).ToArray();
                gainShape = w => w < xs[0] || w > xs[xs.Length - 1] ? 0 : Interpolation.Linear(xs, ys, w);
            }
            var att = new FiberLossGain(loss, flatGain, gainShape);

            // ---- the material and the inner bulk element
            var mat = new Material { Name = "fiber:" + e.Id, Index = index, N2 = n2, Attenuation = att.HasAny ? att : null };
            string ramanKind = "silica";
            if (e.Json.TryGet("raman", out var rj) && rj.Kind == JsonKind.String) ramanKind = rj.AsString();
            if (e.Json.TryGet("raman", out rj) && rj.Kind == JsonKind.Bool && !rj.AsBool()) ramanKind = "none";
            if (rj != null && rj.Kind == JsonKind.Object)
            {
                var spec = ModelSpec.FromJson(rj, ModelSource.SystemData, "element " + e.Id + ".raman");
                mat.Raman = c.Reg.Create<IDelayedResponse>("delayedResponse", spec);
                double fr = MaterialLibrary.DefaultRamanFraction(spec.Kind);
                mat.RamanFraction = mat.Raman == null ? 0 : spec.Json.GetDouble("fR", Num(c, e, "ramanFraction", double.IsNaN(fr) ? 0.18 : fr));
            }
            else
            {
                var spec = ModelSpec.FromJson(new JsonObject().Set("kind", ramanKind), ModelSource.SystemData, "element " + e.Id + ".raman");
                mat.Raman = c.Reg.Create<IDelayedResponse>("delayedResponse", spec);
                double fr = MaterialLibrary.DefaultRamanFraction(ramanKind);
                mat.RamanFraction = mat.Raman == null ? 0 : Num(c, e, "ramanFraction", double.IsNaN(fr) ? 0.18 : fr);
            }
            mat.Provenance["index"] = (dispDesc, ModelSource.SystemData, "element " + e.Id);
            c.Out.Materials[mat.Name] = mat;

            var j = new JsonObject().Set("id", e.Id).Set("type", "bulk").Set("material", mat.Name).Set("lengthM", L);
            foreach (var k in new[] { "nonlinear", "selfSteepening", "timeResponse", "replaceKerr", "transverse", "ionization" }) if (e.Json.Has(k)) j.Set(k, e.Json[k]);
            if (gammaExpr != null)
                j.Set("beam", new JsonObject().Set("kind", "expression").Set("radius", "sqrt(2*a0*g0/(pi*(" + gammaExpr + ")))")
                    .Set("params", new JsonObject().Set("a0", aeff).Set("g0", gamma0)));
            else j.Set("beam", new JsonObject().Set("kind", "constant").Set("areaM2", aeff));
            var saved = c.Track;
            CompileBulk(c, new SystemElement { Id = e.Id, Type = "bulk", Label = e.Label, Json = j, Surfaces = e.Surfaces }, ce, false);
            ce.LengthM = L;
            ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "fiber: {0}; gamma {1:G4} /(W m){2}; A_eff {3:G3} um^2; {4}", dispDesc, gamma0,
                gammaExpr != null ? " at z = 0 (gamma(z) = " + gammaExpr + ")" : "", aeff * 1e12, att.Description));
            // the beam leaves the fibre as its mode: restart beam tracking at a waist of the mode radius at the end face
            if (saved != null)
            {
                double wMode = Math.Sqrt(aeff / Math.PI), lam = 2 * Math.PI * Cl / w0;
                var q = new Complex(0, Math.PI * wMode * wMode / lam);
                c.Track = new Optics.BeamState(q, q, lam);
                ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "beam tracking restarts at the fibre end: mode radius {0:G3} um", wMode * 1e6));
            }

            // ---- run-time: scale a gain spectrum to the requested output, and report the fibre-optics numbers
            var gainGoal = gainSpec != null && (gainSpec.Has("outputEnergyJ") || gainSpec.Has("outputPowerW")) ? gainSpec : null;
            var b2fun = beta2At;
            ce.Prepare = (pulse, rec) =>
            {
                if (gainGoal != null) att.ScaleToGoal(pulse, L, gainGoal, rec.Notes);
                FiberMetrics(pulse, gamma0, b2fun, betas, wRef, L, rec);
            };
        }

        private static Func<double, double> PerWavelengthOrNumber(JsonObject j, string numberKey, string tableKey, string valueKey, double fallback)
        {
            if (j.Has(numberKey)) { double v = j.GetDouble(numberKey, fallback) * Math.Log(10) / 10 / 1000; return _ => v; }   // dB/km -> 1/m
            var t = j.GetObject(tableKey);
            if (t == null) return _ => fallback;
            var wl = t.GetDoubleArray("wavelengthsM") ?? t.GetDoubleArray("wavelengthsNm")?.Select(x => x * 1e-9).ToArray() ?? throw new JsonException("fiber " + tableKey + " needs wavelengthsM");
            var v2 = t.GetDoubleArray(valueKey) ?? throw new JsonException("fiber " + tableKey + " needs " + valueKey);
            var w = wl.Select(l => 2 * Math.PI * Cl / l).ToArray(); var o = Enumerable.Range(0, w.Length).OrderBy(i => w[i]).ToArray();
            var xs = o.Select(i => w[i]).ToArray(); var ys = o.Select(i => v2[i] * Math.Log(10) / 10 / 1000).ToArray();
            return x => x <= xs[0] ? ys[0] : x >= xs[xs.Length - 1] ? ys[ys.Length - 1] : Interpolation.Linear(xs, ys, x);
        }

        /// <summary>Soliton number, dispersion / nonlinear / fission lengths and the dispersive-wave resonance for the
        /// pulse entering the fibre (sech^2 convention T0 = FWHM / 1.763).</summary>
        private static void FiberMetrics(PulseField pulse, double gamma, Func<double, double> beta2At, double[] betas, double wRef, double L, ElementRecord rec)
        {
            var s = PulseMetrics.Summarize(pulse);
            double w0 = pulse.Grid.OmegaCenter, b2 = beta2At(w0), p0 = s.PeakPowerW, t0 = s.FwhmS / 1.763;
            if (double.IsNaN(b2) || !(t0 > 0) || !(p0 > 0)) return;
            double ld = t0 * t0 / Math.Abs(b2), lnl = 1 / (gamma * p0), N = Math.Sqrt(ld / lnl);
            rec.Values["beta2S2PerM"] = b2; rec.Values["dispersionLengthM"] = ld; rec.Values["nonlinearLengthM"] = lnl; rec.Values["solitonNumber"] = N;
            if (b2 < 0 && N > 1)
            {
                rec.Values["fissionLengthM"] = ld / N;
                // dispersive-wave phase matching: sum_{k>=2} beta_k dw^k / k! = gamma P0 / 2 (soliton peak power ~ P0 for N ~ 1..)
                if (betas != null && betas.Length >= 2)
                {
                    double off = w0 - wRef;
                    Func<double, double> D = dw =>
                    {
                        // Taylor expansion about the pulse carrier, from the coefficients about the reference
                        double sum = 0;
                        for (int k = 2; k <= betas.Length + 1; k++)
                        {
                            double bk = 0, f = 1; // beta_k at w0 = sum_j beta_{k+j}(ref) off^j / j!
                            for (int jj = 0; k + jj - 2 < betas.Length; jj++) { bk += betas[k + jj - 2] * Math.Pow(off, jj) / f; f *= jj + 1; }
                            double kf = 1; for (int q = 2; q <= k; q++) kf *= q;
                            sum += bk * Math.Pow(dw, k) / kf;
                        }
                        return sum - gamma * p0 / 2;
                    };
                    double best = double.NaN;
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        double prev = D(0), step = 0.002 * w0;
                        for (int i = 1; i <= 400; i++)
                        {
                            double x = sgn * i * step, v = D(x);
                            if (Math.Sign(v) != Math.Sign(prev))
                            {
                                double a = sgn * (i - 1) * step, bnd = x;
                                for (int it = 0; it < 60; it++) { double m = 0.5 * (a + bnd); if (Math.Sign(D(m)) == Math.Sign(D(a))) a = m; else bnd = m; }
                                double root = 0.5 * (a + bnd);
                                if (double.IsNaN(best) || Math.Abs(root) < Math.Abs(best)) best = root;
                                break;
                            }
                            prev = v;
                        }
                    }
                    if (!double.IsNaN(best)) rec.Values["dispersiveWaveWavelengthM"] = 2 * Math.PI * Cl / (w0 + best);
                }
            }
            rec.Notes.Add(string.Format(CultureInfo.InvariantCulture, "fiber: N = {0:G3}, L_D = {1:G3} m, L_NL = {2:G3} m{3}{4}", N, ld, lnl,
                rec.Values.ContainsKey("fissionLengthM") ? string.Format(CultureInfo.InvariantCulture, ", fission ~ {0:G3} m", rec.Values["fissionLengthM"]) : "",
                rec.Values.ContainsKey("dispersiveWaveWavelengthM") ? string.Format(CultureInfo.InvariantCulture, ", dispersive wave ~ {0:G5} nm", rec.Values["dispersiveWaveWavelengthM"] * 1e9) : ""));
        }
    }

    /// <summary>n(omega) of a fibre mode from Taylor coefficients about a reference frequency: beta(w) = n_eff w_r / c +
    /// n_eff (w - w_r) / c + sum_k beta_k (w - w_r)^k / k!, n = c beta / w (the constant and linear parts only set the
    /// carrier phase and group delay, which the engine removes).</summary>
    public sealed class TaylorFiberIndex : IRefractiveIndex
    {
        private readonly double _wr, _n; private readonly double[] _b;
        public TaylorFiberIndex(double wRef, double nEff, double[] betas) { _wr = wRef; _n = nEff; _b = betas; }
        public string Description => "fiber mode, Taylor series (" + _b.Length + " coefficients from beta_2)";
        public void Evaluate(double[] omega, double[] nOut)
        {
            double c = PhysicalConstants.SpeedOfLightMPerS;
            for (int i = 0; i < omega.Length; i++)
            {
                double d = omega[i] - _wr, beta = _n * _wr / c + _n * d / c, f = 2, p = d * d;
                for (int k = 0; k < _b.Length; k++) { beta += _b[k] * p / f; p *= d; f *= k + 3; }
                nOut[i] = omega[i] > 0 ? c * beta / omega[i] : double.NaN;
            }
        }
    }

    /// <summary>n(omega) of a fibre mode from a tabulated beta2(omega): beta is integrated twice from the carrier (cubic
    /// interpolation of beta2, constant beyond the data).</summary>
    public sealed class TabulatedFiberIndex : IRefractiveIndex
    {
        private readonly double _w0, _n; private readonly double[] _w, _b2, _gw, _beta;
        public TabulatedFiberIndex(double w0, double nEff, double[] wavelengthsM, double[] beta2)
        {
            _w0 = w0; _n = nEff;
            double c = PhysicalConstants.SpeedOfLightMPerS;
            var w = wavelengthsM.Select(l => 2 * Math.PI * c / l).ToArray();
            var o = Enumerable.Range(0, w.Length).OrderBy(i => w[i]).ToArray();
            _w = o.Select(i => w[i]).ToArray(); _b2 = o.Select(i => beta2[i]).ToArray();
            // integrate on a fine grid spanning the data and +-60 % around the carrier, with the carrier on a node
            double lo = Math.Min(_w[0], 0.4 * w0), hi = Math.Max(_w[_w.Length - 1], 1.6 * w0);
            int n = 20001; double dw = (hi - lo) / (n - 1);
            int i0 = (int)Math.Ceiling((w0 - lo) / dw); lo = w0 - i0 * dw; n = Math.Max(n, i0 + 2);
            _gw = new double[n]; _beta = new double[n];
            var b1 = new double[n];
            for (int i = 0; i < n; i++) _gw[i] = lo + i * dw;
            for (int i = i0 + 1; i < n; i++) b1[i] = b1[i - 1] + 0.5 * (Beta2At(_gw[i - 1]) + Beta2At(_gw[i])) * dw;
            for (int i = i0 - 1; i >= 0; i--) b1[i] = b1[i + 1] - 0.5 * (Beta2At(_gw[i + 1]) + Beta2At(_gw[i])) * dw;
            for (int i = i0 + 1; i < n; i++) _beta[i] = _beta[i - 1] + 0.5 * (b1[i - 1] + b1[i]) * dw;
            for (int i = i0 - 1; i >= 0; i--) _beta[i] = _beta[i + 1] - 0.5 * (b1[i + 1] + b1[i]) * dw;
        }
        public double Beta2At(double w) => w <= _w[0] ? _b2[0] : w >= _w[_w.Length - 1] ? _b2[_b2.Length - 1] : Interpolation.Linear(_w, _b2, w);
        public string Description => "fiber mode, tabulated dispersion (" + _w.Length + " points)";
        public void Evaluate(double[] omega, double[] nOut)
        {
            double c = PhysicalConstants.SpeedOfLightMPerS;
            for (int i = 0; i < omega.Length; i++)
            {
                double w = omega[i];
                if (w <= 0) { nOut[i] = double.NaN; continue; }
                double b = w < _gw[0] || w > _gw[_gw.Length - 1] ? double.NaN : Interpolation.Linear(_gw, _beta, w);
                nOut[i] = double.IsNaN(b) ? double.NaN : c * (_n * _w0 / c + _n * (w - _w0) / c + b) / w;
            }
        }
    }

    /// <summary>Power loss (1/m) minus gain: flat gain in dB/m, and a gain spectrum (dB/m shape) that can be scaled at
    /// run time so the output reaches a requested energy or average power.</summary>
    public sealed class FiberLossGain : IAttenuation
    {
        private readonly Func<double, double> _loss, _shape; private readonly double _flat;
        private double _scale = 1;
        public FiberLossGain(Func<double, double> lossPerM, double flatGainDbPerM, Func<double, double> gainShapeDbPerM)
        { _loss = lossPerM; _flat = flatGainDbPerM * Math.Log(10) / 10; _shape = gainShapeDbPerM; }
        public bool HasAny => _flat != 0 || _shape != null || _loss(1e15) != 0 || _loss(2e15) != 0;
        public string Description => (_flat != 0 ? "gain " + (_flat * 10 / Math.Log(10)).ToString("G4", CultureInfo.InvariantCulture) + " dB/m, " : "") +
            (_shape != null ? "gain spectrum x" + _scale.ToString("G4", CultureInfo.InvariantCulture) + ", " : "") + "loss " + (_loss(1.8e15) * 10 / Math.Log(10) * 1000).ToString("G4", CultureInfo.InvariantCulture) + " dB/km";
        public void Evaluate(double[] omega, double[] alphaOut)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double g = _flat + (_shape != null ? _scale * _shape(omega[i]) * Math.Log(10) / 10 : 0);
                alphaOut[i] = _loss(omega[i]) - g;
            }
        }

        /// <summary>Scale the gain spectrum so the linear output (input spectrum x exp(g L)) has the requested energy
        /// (as pyNLO's propagate-to-gain-goal estimate).</summary>
        public void ScaleToGoal(PulseField p, double L, JsonObject goal, List<string> notes)
        {
            if (_shape == null) return;
            double target = goal.Has("outputEnergyJ") ? goal.GetDouble("outputEnergyJ", 0) : goal.GetDouble("outputPowerW", 0) / Math.Max(p.RepRateHz, 1e-300);
            if (!(target > 0)) { notes.Add("fiber gain: outputPowerW needs the pulse's repRateHz"); return; }
            var g = p.Grid; var S = p.SpectralIntensity(); double norm = g.DTime / g.N;
            Func<double, double> E = k =>
            {
                double e = 0;
                for (int i = 0; i < g.N; i++)
                {
                    double gg = _flat + k * _shape(g.OmegaAbsolute[i]) * Math.Log(10) / 10, al = _loss(g.OmegaAbsolute[i]) - gg;
                    e += S[i] * Math.Exp(-al * L);
                }
                return e * norm;
            };
            double lo = -50, hi = 50;
            if (E(hi) < target || E(lo) > target) { notes.Add("fiber gain: the requested output cannot be reached with this gain spectrum"); return; }
            for (int it = 0; it < 200; it++) { double m = 0.5 * (lo + hi); if (E(m) < target) lo = m; else hi = m; }
            _scale = 0.5 * (lo + hi);
            notes.Add(string.Format(CultureInfo.InvariantCulture, "fiber gain spectrum scaled x{0:G4} for {1:G4} J out (linear estimate from the input spectrum)", _scale, target));
        }
    }
}
