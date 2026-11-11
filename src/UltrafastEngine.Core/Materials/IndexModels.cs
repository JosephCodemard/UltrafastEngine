using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Materials
{
    /// <summary>Helpers shared by index models.</summary>
    internal static class Wl
    {
        public const double C = PhysicalConstants.SpeedOfLightMPerS;
        public static double OmegaToLambdaM(double w) => w > 0 ? 2.0 * Math.PI * C / w : double.NaN;
        public static double OmegaToLambdaUm(double w) => OmegaToLambdaM(w) * 1e6;
    }

    /// <summary>Constant index (e.g. vacuum = 1, or a quick "n = 1.45" estimate).</summary>
    public sealed class ConstantIndex : IRefractiveIndex
    {
        public double Value { get; }
        public ConstantIndex(double n) { Value = n; }
        public string Description => "constant n = " + Value.ToString("G8", System.Globalization.CultureInfo.InvariantCulture);
        public void Evaluate(double[] omega, double[] nOut) { for (int i = 0; i < omega.Length; i++) nOut[i] = Value; }
    }

    /// <summary>
    /// Sellmeier: n^2 = A + sum_i B_i lambda^2 / (lambda^2 - C_i), lambda in micrometres (C_i in um^2),
    /// the form used by Schott, Malitson, refractiveindex.info "formula 2". JSON:
    /// <c>{"kind":"sellmeier","B":[...],"C":[...],"A":1}</c>. A defaults to 1.
    /// </summary>
    public sealed class SellmeierIndex : IRefractiveIndex
    {
        public double A { get; }
        public double[] B { get; }
        public double[] C { get; }
        public string Name { get; }
        public SellmeierIndex(double[] b, double[] c, double a = 1.0, string name = null)
        {
            if (b == null || c == null || b.Length != c.Length) throw new ArgumentException("Sellmeier B and C must have equal length.");
            A = a; B = b; C = c; Name = name;
        }
        public string Description => "sellmeier" + (Name != null ? " (" + Name + ")" : "") + " B=[" + string.Join(",", B.Select(x => x.ToString("G6", System.Globalization.CultureInfo.InvariantCulture))) + "]";
        public void Evaluate(double[] omega, double[] nOut)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double l2 = Wl.OmegaToLambdaUm(omega[i]); l2 *= l2;
                double n2 = A;
                for (int k = 0; k < B.Length; k++) n2 += B[k] * l2 / (l2 - C[k]);
                nOut[i] = n2 > 0 ? Math.Sqrt(n2) : double.NaN;
            }
        }
    }

    /// <summary>Sellmeier with an infrared correction term (common for crystals):
    /// n^2 = A + sum B_k lambda^2 / (lambda^2 - C_k) - D lambda^2 (lambda in um).</summary>
    public sealed class SellmeierIrIndex : IRefractiveIndex
    {
        public double A { get; } public double[] B { get; } public double[] C { get; } public double D { get; }
        public string Name { get; }
        public SellmeierIrIndex(double a, double[] b, double[] c, double d, string name = null) { A = a; B = b; C = c; D = d; Name = name; }
        public string Description => "sellmeier + IR term" + (Name != null ? " (" + Name + ")" : "");
        public void Evaluate(double[] omega, double[] nOut)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double l2 = Wl.OmegaToLambdaUm(omega[i]); l2 *= l2;
                double n2 = A - D * l2;
                for (int k = 0; k < B.Length; k++) n2 += B[k] * l2 / (l2 - C[k]);
                nOut[i] = n2 > 0 ? Math.Sqrt(n2) : double.NaN;
            }
        }
    }

    /// <summary>Cauchy: n = A + B/lambda^2 + C/lambda^4 + ... (lambda in um). JSON: {"kind":"cauchy","coefficients":[A,B,C,...]}</summary>
    public sealed class CauchyIndex : IRefractiveIndex
    {
        public double[] Coefficients { get; }
        public CauchyIndex(double[] coefficients) { Coefficients = coefficients; }
        public string Description => "cauchy";
        public void Evaluate(double[] omega, double[] nOut)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double l2 = Wl.OmegaToLambdaUm(omega[i]); l2 *= l2;
                double n = 0, p = 1;
                foreach (var c in Coefficients) { n += c / p; p *= l2; }
                nOut[i] = n;
            }
        }
    }

    /// <summary>
    /// Tabulated n(lambda), cubic-spline interpolated in omega; outside the table it continues with constant
    /// slope dn/domega (so group index stays finite) -- flagged in the provenance report. This is the form the
    /// Zemax binding exports (OpticStudio's own index at each sample wavelength). JSON:
    /// <c>{"kind":"table","wavelengthsM":[...],"n":[...]}</c> (or "wavelengthsUm"/"wavelengthsNm").
    /// </summary>
    public sealed class TableIndex : IRefractiveIndex
    {
        private readonly double[] _w, _n;
        private readonly NaturalCubicSpline _spline;
        private readonly LinearInterpolator _lin;
        public TableIndex(double[] wavelengthsM, double[] n)
        {
            if (wavelengthsM.Length != n.Length || n.Length == 0) throw new ArgumentException("Index table: wavelength and n arrays must have equal, non-zero length.");
            var pts = wavelengthsM.Select((l, i) => (w: 2 * Math.PI * Wl.C / l, n: n[i])).OrderBy(p => p.w).ToArray();
            _w = pts.Select(p => p.w).ToArray();
            _n = pts.Select(p => p.n).ToArray();
            if (_w.Length >= 4) _spline = new NaturalCubicSpline(_w, _n);
            else if (_w.Length >= 2) _lin = new LinearInterpolator(_w, _n);
        }
        public double MinOmega => _w[0];
        public double MaxOmega => _w[_w.Length - 1];
        public string Description => "table (" + _w.Length + " points, " + (Wl.OmegaToLambdaM(MaxOmega) * 1e9).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "-" + (Wl.OmegaToLambdaM(MinOmega) * 1e9).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " nm)";
        public void Evaluate(double[] omega, double[] nOut)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double w = omega[i];
                if (_spline != null) nOut[i] = _spline.EvaluateLinearExtrapolate(w);
                else if (_lin != null)
                {
                    double slope = (_n[_n.Length - 1] - _n[0]) / (_w[_w.Length - 1] - _w[0]);
                    nOut[i] = w < _w[0] ? _n[0] + slope * (w - _w[0]) : w > _w[_w.Length - 1] ? _n[_n.Length - 1] + slope * (w - _w[_w.Length - 1]) : _lin.Evaluate(w);
                }
                else nOut[i] = _n[0];
            }
        }
    }

    /// <summary>n from an expression in omega, lambda (m), lambda_um, lambda_nm, nu (Hz). JSON:
    /// <c>{"kind":"expression","expr":"sqrt(1 + 0.6961663*lambda_um^2/(lambda_um^2-0.0684043^2))","params":{}}</c></summary>
    public sealed class ExpressionIndex : IRefractiveIndex
    {
        public static readonly string[] Vars = { "omega", "lambda", "lambda_um", "lambda_nm", "nu" };
        private readonly CompiledExpression _e;
        public ExpressionIndex(string expr, IReadOnlyDictionary<string, double> p) { _e = CompiledExpression.Compile(expr, Vars, p); }
        public string Description => "expression \"" + _e.Text + "\"";
        public void Evaluate(double[] omega, double[] nOut)
        {
            int n = omega.Length;
            var l = new double[n]; var lu = new double[n]; var ln = new double[n]; var nu = new double[n];
            for (int i = 0; i < n; i++) { l[i] = Wl.OmegaToLambdaM(omega[i]); lu[i] = l[i] * 1e6; ln[i] = l[i] * 1e9; nu[i] = omega[i] / (2 * Math.PI); }
            var r = _e.Evaluate(new Dictionary<string, double[]> { { "omega", omega }, { "lambda", l }, { "lambda_um", lu }, { "lambda_nm", ln }, { "nu", nu } }, n);
            Array.Copy(r, nOut, n);
        }
    }

    /// <summary>Index from a registered <see cref="IndexCallback"/> (C#, Python or C).</summary>
    public sealed class CallbackIndex : IRefractiveIndex
    {
        private readonly IndexCallback _cb; private readonly string _name;
        public CallbackIndex(string name, IndexCallback cb) { _name = name; _cb = cb ?? throw new ArgumentNullException(nameof(cb)); }
        public string Description => "callback @" + _name;
        public void Evaluate(double[] omega, double[] nOut) => _cb(omega, nOut);
    }

    /// <summary>n_abs = n_rel * n_air: converts a catalogue index quoted relative to air (Schott/Zemax convention)
    /// to the absolute (vacuum-referenced) index the engine propagates with.</summary>
    public sealed class AirReferencedIndex : IRefractiveIndex
    {
        private readonly IRefractiveIndex _rel, _air;
        public AirReferencedIndex(IRefractiveIndex relativeToAir, IRefractiveIndex air) { _rel = relativeToAir; _air = air; }
        public string Description => _rel.Description + " x n_air";
        public void Evaluate(double[] omega, double[] nOut)
        {
            var a = new double[omega.Length];
            _rel.Evaluate(omega, nOut);
            _air.Evaluate(omega, a);
            for (int i = 0; i < omega.Length; i++) nOut[i] *= a[i];
        }
    }

    /// <summary>
    /// Refractive index of air, Ciddor (Appl. Opt. 35, 1566, 1996) -- the standard used by metrology and by
    /// the NIST "Engineering Metrology Toolbox". Valid 300 nm - 1.7 um (extrapolates smoothly beyond).
    /// JSON: <c>{"kind":"ciddor","temperatureC":20,"pressurePa":101325,"humidity":0.0,"co2ppm":450}</c>.
    /// </summary>
    public sealed class CiddorAirIndex : IRefractiveIndex
    {
        /// <summary>The constants of Ciddor's formulas, from the catalogue entry Air (quantities standardAirDispersion, co2Dependence, waterVaporDispersion, thermodynamics, saturationVapourPressure,
        /// enhancementFactor, compressibility, referenceStates), read once per loaded catalogue.</summary>
        private sealed class Constants
        {
            public double K0, K1, K2, K3, Co2Slope, Co2Ref, MaMolar, MaSlope, MaRef, Cf, R, Mw, Pstd, Tstd, Pw, Tw;
            public double[] W, Svp, Enh, Zc;
            public double T0C, P0Pa, Rh0, Co20Ppm;
        }

        private static Constants K => Catalogue.Cached("ciddor.constants", () =>
        {
            var air = Catalogue.Get("Air", "gas");
            var sd = air.Q("standardAirDispersion"); var co = air.Q("co2Dependence"); var wv = air.Q("waterVaporDispersion"); var th = air.Q("thermodynamics"); var rs = air.Q("referenceStates"); var ix = air.Q("index");
            return new Constants
            {
                K0 = sd.Number("k0PerUm2"), K1 = sd.Number("k1PerUm2"), K2 = sd.Number("k2PerUm2"), K3 = sd.Number("k3PerUm2"),
                Co2Slope = co.Number("co2SlopeFractionPerPpm"), Co2Ref = co.Number("co2ReferencePpm"), MaMolar = co.Number("airMolarMassGPerMol"), MaSlope = co.Number("airMolarMassSlopeGPerMolPerPpm"), MaRef = co.Number("airMolarMassReferencePpm"),
                W = wv.Array("polynomialCoefficients"), Cf = wv.Number("correctionFactor"), R = th.Number("gasConstantJPerMolK"), Mw = th.Number("waterMolarMassKgPerMol"),
                Svp = air.Q("saturationVapourPressure").Array("coefficients"), Enh = air.Q("enhancementFactor").Array("coefficients"), Zc = air.Q("compressibility").Array("coefficients"),
                Pstd = rs.Number("standardAirPressurePa"), Tstd = rs.Number("standardAirTemperatureK"), Pw = rs.Number("waterVaporPressurePa"), Tw = rs.Number("waterVaporTemperatureK"),
                T0C = ix.Number("temperatureC"), P0Pa = ix.Number("pressurePa"), Rh0 = ix.Number("relativeHumidityPercent"), Co20Ppm = ix.Number("co2Ppm")
            };
        });

        public double TemperatureC { get; }
        public double PressurePa { get; }
        public double RelativeHumidity { get; }
        public double Co2Ppm { get; }

        private static double Svp(Constants k, double T) => Math.Exp(k.Svp[0] * T * T + k.Svp[1] * T + k.Svp[2] + k.Svp[3] / T);
        private static double Enhancement(Constants k, double p, double t) => k.Enh[0] + k.Enh[1] * p + k.Enh[2] * t * t;

        /// <summary>Mole fraction of water vapour: enhancement factor x RH x saturation vapour pressure / pressure (CIPM-2007 / Ciddor).</summary>
        public double WaterVaporMoleFraction
        {
            get
            {
                var k = K; double T = TemperatureC + 273.15, p = PressurePa, t = TemperatureC;
                return Enhancement(k, p, t) * RelativeHumidity * Svp(k, T) / p;
            }
        }

        /// <summary>Air at the given state; parameters not given (NaN) take the standard environment of the catalogue entry Air (20 C, 101325 Pa, dry, 450 ppm CO2).</summary>
        public CiddorAirIndex(double temperatureC = double.NaN, double pressurePa = double.NaN, double relativeHumidity = double.NaN, double co2Ppm = double.NaN)
        {
            var k = K;
            TemperatureC = double.IsNaN(temperatureC) ? k.T0C : temperatureC; PressurePa = double.IsNaN(pressurePa) ? k.P0Pa : pressurePa;
            RelativeHumidity = double.IsNaN(relativeHumidity) ? k.Rh0 : relativeHumidity; Co2Ppm = double.IsNaN(co2Ppm) ? k.Co20Ppm : co2Ppm;
        }

        public string Description => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "ciddor air ({0:G4} C, {1:G6} Pa, RH {2:G3}, CO2 {3:G4} ppm)", TemperatureC, PressurePa, RelativeHumidity, Co2Ppm);

        private static double Z(Constants k, double p, double T, double xw)
        {
            double a0 = k.Zc[0], a1 = k.Zc[1], a2 = k.Zc[2], b0 = k.Zc[3], b1 = k.Zc[4], c0 = k.Zc[5], c1 = k.Zc[6], d = k.Zc[7], e = k.Zc[8];
            double t = T - 273.15;
            return 1 - (p / T) * (a0 + a1 * t + a2 * t * t + (b0 + b1 * t) * xw + (c0 + c1 * t) * xw * xw) + (p / T) * (p / T) * (d + e * xw * xw);
        }

        public void Evaluate(double[] omega, double[] nOut)
        {
            var kc = K;
            double k0 = kc.K0, k1 = kc.K1, k2 = kc.K2, k3 = kc.K3;
            double w0 = kc.W[0], w1 = kc.W[1], w2 = kc.W[2], w3 = kc.W[3], cf = kc.Cf;
            double R = kc.R, Mw = kc.Mw;
            double T = TemperatureC + 273.15, p = PressurePa, t = TemperatureC;
            double Ma = 1e-3 * (kc.MaMolar + kc.MaSlope * (Co2Ppm - kc.MaRef));
            double svp = Svp(kc, T);
            double f = Enhancement(kc, p, t);
            double xw = f * RelativeHumidity * svp / p;
            double Za = Z(kc, kc.Pstd, kc.Tstd, 0), Zw = Z(kc, kc.Pw, kc.Tw, 1), Zm = Z(kc, p, T, xw);
            double rhoAxs = kc.Pstd * Ma / (Za * R * kc.Tstd);
            double rhoWs = kc.Pw * Mw / (Zw * R * kc.Tw);
            double rhoA = p * Ma * (1 - xw) / (Zm * R * T);
            double rhoW = p * Mw * xw / (Zm * R * T);
            for (int i = 0; i < omega.Length; i++)
            {
                double s2 = 1.0 / Wl.OmegaToLambdaUm(omega[i]); s2 *= s2;
                double nas = 1e-8 * (k1 / (k0 - s2) + k3 / (k2 - s2));
                double naxs = nas * (1 + kc.Co2Slope * (Co2Ppm - kc.Co2Ref));
                double nws = 1e-8 * cf * (w0 + w1 * s2 + w2 * s2 * s2 + w3 * s2 * s2 * s2);
                nOut[i] = 1 + (rhoA / rhoAxs) * naxs + (rhoW / rhoWs) * nws;
            }
        }
    }

    /// <summary>
    /// Gas index, Borzsonyi et al., Appl. Opt. 47, 4856 (2008):
    /// n^2 - 1 = (p/p0)(T0/T) [B1 l^2/(l^2 - C1) + B2 l^2/(l^2 - C2)], l in um, p0 = 1 bar, T0 = 273 K.
    /// Fitted 400-1000 nm. JSON: <c>{"kind":"gas","species":"Ar","pressureBar":1,"temperatureK":293}</c>.
    /// </summary>
    public sealed class GasIndex : IRefractiveIndex
    {
        /// <summary>The four Borzsonyi coefficients of each gas (n^2 - 1 = f (B1 l^2 / (l^2 - C1) + B2 l^2 / (l^2 - C2)), l in um), from the catalogue.</summary>
        public static Dictionary<string, double[]> Coefficients => Catalogue.Cached("gasindex.coefficients", () =>
        {
            var d = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Catalogue.Entries("gas"))
            {
                var q = e.TryQ("index");
                if (q == null) continue;
                if (q.Text("model") == "borzsonyi") d[e.Id] = q.Array("coefficients");
                else if (q.Has("borzsonyiCoefficients")) d[e.Id] = q.Array("borzsonyiCoefficients");
            }
            return d;
        });

        public string Species { get; }
        public double PressureBar { get; }
        public double TemperatureK { get; }
        private readonly double[] _k;

        public GasIndex(string species, double pressureBar, double temperatureK = 293.15)
        {
            if (!Coefficients.TryGetValue(species, out _k)) throw new ArgumentException("Unknown gas species '" + species + "' (known: " + string.Join(", ", Coefficients.Keys) + ").");
            Species = species; PressureBar = pressureBar; TemperatureK = temperatureK;
        }

        public double DensityFactor => PressureBar * 273.0 / TemperatureK;
        public string Description => string.Format(System.Globalization.CultureInfo.InvariantCulture, "gas {0} ({1:G4} bar, {2:G4} K, Borzsonyi 2008)", Species, PressureBar, TemperatureK);

        public void Evaluate(double[] omega, double[] nOut)
        {
            double f = DensityFactor;
            for (int i = 0; i < omega.Length; i++)
            {
                double l2 = Wl.OmegaToLambdaUm(omega[i]); l2 *= l2;
                double s = _k[0] * l2 / (l2 - _k[1]) + _k[2] * l2 / (l2 - _k[3]);
                double n2m1 = f * s;
                nOut[i] = n2m1 > -1 ? Math.Sqrt(1 + n2m1) : double.NaN;
            }
        }
    }

    // ------------------------------------------------------------------------------ attenuation models

    /// <summary>Constant attenuation alpha (1/m). JSON: a number, or {"kind":"constant","value":...}.</summary>
    public sealed class ConstantAttenuation : IAttenuation
    {
        public double Alpha { get; }
        public ConstantAttenuation(double alphaPerM) { Alpha = alphaPerM; }
        public string Description => "constant alpha = " + Alpha.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " /m";
        public void Evaluate(double[] omega, double[] a) { for (int i = 0; i < omega.Length; i++) a[i] = Alpha; }
    }

    /// <summary>Tabulated alpha(lambda) or internal transmittance over a thickness (Schott-style):
    /// {"kind":"table","wavelengthsM":[...],"alphaPerM":[...]} or {..., "internalTransmission":[...], "thicknessM":0.01}.</summary>
    public sealed class TableAttenuation : IAttenuation
    {
        private readonly LinearInterpolator _i; private readonly double[] _w, _a;
        public TableAttenuation(double[] wavelengthsM, double[] alphaPerM)
        {
            var pts = wavelengthsM.Select((l, k) => (w: 2 * Math.PI * Wl.C / l, a: alphaPerM[k])).OrderBy(p => p.w).ToArray();
            _w = pts.Select(p => p.w).ToArray(); _a = pts.Select(p => p.a).ToArray();
            if (_w.Length >= 2) _i = new LinearInterpolator(_w, _a);
        }
        public string Description => "table attenuation (" + _w.Length + " points)";
        public void Evaluate(double[] omega, double[] a)
        {
            for (int k = 0; k < omega.Length; k++)
                a[k] = _i == null ? _a[0] : omega[k] <= _w[0] ? _a[0] : omega[k] >= _w[_w.Length - 1] ? _a[_a.Length - 1] : _i.Evaluate(omega[k]);
        }
    }

    /// <summary>alpha from an expression (same variables as <see cref="ExpressionIndex"/>), or from a callback.</summary>
    public sealed class FunctionAttenuation : IAttenuation
    {
        private readonly IRefractiveIndex _f; // reuses the index evaluators: the value is alpha instead of n
        public FunctionAttenuation(IRefractiveIndex f) { _f = f; }
        public string Description => _f.Description + " (alpha, 1/m)";
        public void Evaluate(double[] omega, double[] a) => _f.Evaluate(omega, a);
    }
}
