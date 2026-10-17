using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Physics
{
    // ===================================================================================== time responses

    /// <summary>
    /// Delta n(t) from expressions. Variables: t (co-moving, s), t_lab (s), t_arrival (s), z (m, within the
    /// element), L (element length, m), I, Ix, Iy (cycle-averaged intensity, W/m^2), I_inst (instantaneous
    /// intensity; = I in envelope mode), E (real field, sqrt(W)/m; 0 in envelope mode), omega0, lambda0, Aeff,
    /// plus any "params". JSON:
    /// <code>{"kind":"expression", "expr":"n2x*I + dn0*sin(2*pi*t_lab/T)", "exprIm":"0",
    ///  "params":{"n2x":1e-20,"dn0":1e-5,"T":2.7e-15}, "carrierResolved": true}</code>
    /// </summary>
    public sealed class ExpressionTimeResponse : ITimeDomainResponse
    {
        public static readonly string[] Vars = { "t", "t_lab", "t_arrival", "z", "L", "I", "Ix", "Iy", "I_inst", "E", "omega0", "lambda0", "Aeff", "x", "y", "r", "theta", "spatial", "F" };
        private readonly CompiledExpression _re, _im;
        public bool NeedsCarrierResolution { get; }
        public ExpressionTimeResponse(ModelSpec s)
        {
            var p = s.Params();
            string re = s.Json.GetString("expr") ?? s.Json.GetString("exprRe") ?? "0";
            _re = CompiledExpression.Compile(re, Vars, p);
            string im = s.Json.GetString("exprIm");
            if (!string.IsNullOrEmpty(im)) _im = CompiledExpression.Compile(im, Vars, p);
            NeedsCarrierResolution = s.Json.GetBool("carrierResolved", false);
        }
        public string Description => "expression dn = \"" + _re.Text + "\"" + (_im != null ? " + i(\"" + _im.Text + "\")" : "") + (NeedsCarrierResolution ? " [carrier-resolved]" : "");

        /// <summary>Running fluence F(t) = integral of I dt' up to t (J/m^2): for slow, accumulating responses.</summary>
        private static double[] Fluence(ResponseContext c)
        {
            int n = c.N; var f = new double[n]; var I = c.Intensity; double dt = n > 1 ? c.T[1] - c.T[0] : 0, s = 0;
            for (int i = 0; i < n; i++) { s += (i > 0 ? 0.5 * (I[i] + I[i - 1]) : 0) * dt; f[i] = s; }
            return f;
        }

        public void Evaluate(ResponseContext c, double[] dnRe, double[] dnIm)
        {
            int n = c.N;
            var b = new Dictionary<string, double[]>
            {
                { "t", c.T }, { "t_lab", c.TLab }, { "t_arrival", new[] { c.TArrival } }, { "z", new[] { c.Z } }, { "L", new[] { c.ElementLength } },
                { "I", c.Intensity }, { "Ix", c.IntensityX ?? c.Intensity }, { "Iy", c.IntensityY ?? new double[n] },
                { "I_inst", c.InstantIntensity ?? c.Intensity }, { "E", c.Field ?? new double[1] },
                { "omega0", new[] { c.Omega0 } }, { "lambda0", new[] { 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0 } },
                { "Aeff", new[] { c.EffectiveAreaM2 } },
                { "x", new[] { c.X } }, { "y", new[] { c.Y } }, { "r", new[] { Math.Sqrt(c.X * c.X + c.Y * c.Y) } },
                { "theta", new[] { Math.Atan2(c.Y, c.X) } }, { "spatial", new[] { c.Spatial ? 1.0 : 0.0 } },
                { "F", Fluence(c) }
            };
            var r = _re.Evaluate(b, n);
            Array.Copy(r, dnRe, n);
            if (_im != null) { var q = _im.Evaluate(b, n); Array.Copy(q, dnIm, n); }
        }
    }

    /// <summary>Delta n(t) from a registered callback (C#, Python, C/C++).</summary>
    public sealed class CallbackTimeResponse : ITimeDomainResponse
    {
        private readonly TimeResponseCallback _cb; private readonly string _name;
        public bool NeedsCarrierResolution { get; }
        public CallbackTimeResponse(string name, TimeResponseCallback cb, bool carrierResolved) { _name = name; _cb = cb; NeedsCarrierResolution = carrierResolved; }
        public string Description => "callback @" + _name + (NeedsCarrierResolution ? " [carrier-resolved]" : "");
        public void Evaluate(ResponseContext c, double[] dnRe, double[] dnIm) => _cb(c, dnRe, dnIm);
    }

    /// <summary>Two-photon absorption, dI/dz = -beta I^2 (beta in m/W): Delta n_im = beta I c / (2 omega0).</summary>
    public sealed class TwoPhotonAbsorptionResponse : ITimeDomainResponse
    {
        public double Beta { get; }
        public TwoPhotonAbsorptionResponse(double beta) { Beta = beta; }
        public bool NeedsCarrierResolution => false;
        public string Description => "two-photon absorption beta = " + Beta.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " m/W";
        public void Evaluate(ResponseContext c, double[] dnRe, double[] dnIm)
        {
            double k = Beta * PhysicalConstants.SpeedOfLightMPerS / (2 * c.Omega0);
            for (int i = 0; i < c.N; i++) dnIm[i] = k * c.Intensity[i];
        }
    }

    /// <summary>
    /// Broadband chi(2) in the carrier-resolved mode: the second-order polarisation P = 2 eps0 d_eff E^2 written as
    /// an index change Delta n = d_eff E / n0 (E the real field in V/m, E = e / sqrt(n0 c eps0 A_eff)). SHG, SFG, DFG
    /// and optical rectification of any bandwidth come out of one field; phase matching follows from the material
    /// dispersion. "periodM" flips the sign of d_eff every half period (quasi-phase matching, z from the element start).
    /// Scalar (one polarisation): no birefringent phase matching -- use QPM or a dispersion you supply.
    /// </summary>
    public sealed class Chi2Response : ITimeDomainResponse
    {
        public double Deff { get; }
        public double PeriodM { get; }
        public double N0Override { get; }
        public Chi2Response(double deff, double period, double n0) { Deff = deff; PeriodM = period; N0Override = n0; }
        public bool NeedsCarrierResolution => true;
        public string Description => "chi(2): d_eff = " + (Deff * 1e12).ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " pm/V" + (PeriodM > 0 ? ", QPM period " + (PeriodM * 1e6).ToString("G5", System.Globalization.CultureInfo.InvariantCulture) + " um" : "");
        public void Evaluate(ResponseContext c, double[] dnRe, double[] dnIm)
        {
            if (!c.CarrierResolved || c.Field == null) return;   // averages to zero on an envelope
            double n0 = N0Override > 0 ? N0Override : (c.N0 > 0 ? c.N0 : 1.0);
            double sign = PeriodM > 0 ? (Math.Sin(2 * Math.PI * c.Z / PeriodM) >= 0 ? 1 : -1) : 1;
            double k = sign * Deff / n0 / Math.Sqrt(n0 * PhysicalConstants.SpeedOfLightMPerS * 8.8541878128e-12 * c.EffectiveAreaM2);
            for (int i = 0; i < c.N; i++) dnRe[i] = k * c.Field[i];
        }
    }

    /// <summary>An additional instantaneous Kerr term n2 I (e.g. a second species, or a quick n2 tweak).</summary>
    public sealed class ExtraKerrResponse : ITimeDomainResponse
    {
        public double N2 { get; }
        public ExtraKerrResponse(double n2) { N2 = n2; }
        public bool NeedsCarrierResolution => false;
        public string Description => "additional Kerr n2 = " + N2.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " m^2/W";
        public void Evaluate(ResponseContext c, double[] dnRe, double[] dnIm) { for (int i = 0; i < c.N; i++) dnRe[i] = N2 * c.Intensity[i]; }
    }

    // ===================================================================================== filters

    internal static class OmegaVars
    {
        public static Dictionary<string, double[]> Bind(double[] omega, double omega0)
        {
            int n = omega.Length;
            var l = new double[n]; var lu = new double[n]; var ln = new double[n]; var dw = new double[n];
            for (int i = 0; i < n; i++)
            {
                l[i] = omega[i] > 0 ? 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / omega[i] : double.NaN;
                lu[i] = l[i] * 1e6; ln[i] = l[i] * 1e9; dw[i] = omega[i] - omega0;
            }
            return new Dictionary<string, double[]> { { "omega", omega }, { "lambda", l }, { "lambda_um", lu }, { "lambda_nm", ln }, { "domega", dw }, { "omega0", new[] { omega0 } } };
        }
    }

    /// <summary>Tabulated filter: wavelengths + power transmission (+ optional phase or group delay).
    /// {"kind":"table","wavelengthsNm":[...],"transmission":[...],"phaseRad":[...]} (or "groupDelayS").
    /// Transmission is linearly interpolated and 0 outside the table unless "outside":"hold".</summary>
    public sealed class TableFilter : ISpectralFilter
    {
        private double[] _w, _t, _phi, _gd; private bool _hold;
        public string Description { get; private set; }
        public static TableFilter FromSpec(ModelSpec s)
        {
            var wl = BuiltInModels.WavelengthsM(s);
            var t = s.Json.GetDoubleArray("transmission") ?? Enumerable.Repeat(1.0, wl.Length).ToArray();
            var ph = s.Json.GetDoubleArray("phaseRad");
            var gd = s.Json.GetDoubleArray("groupDelayS");
            var order = Enumerable.Range(0, wl.Length).OrderBy(i => -wl[i]).ToArray(); // ascending omega
            var f = new TableFilter
            {
                _w = order.Select(i => 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / wl[i]).ToArray(),
                _t = order.Select(i => t[i]).ToArray(),
                _phi = ph == null ? null : order.Select(i => ph[i]).ToArray(),
                _gd = gd == null ? null : order.Select(i => gd[i]).ToArray(),
                _hold = string.Equals(s.Json.GetString("outside", "zero"), "hold", StringComparison.OrdinalIgnoreCase),
                Description = "table filter (" + wl.Length + " points)"
            };
            return f;
        }
        public void Evaluate(double[] omega, double[] tOut, double[] phOut)
        {
            var lt = new LinearInterpolator(_w, _t);
            for (int i = 0; i < omega.Length; i++)
            {
                double w = omega[i];
                bool outside = w < _w[0] || w > _w[_w.Length - 1];
                tOut[i] = outside ? (_hold ? (w < _w[0] ? _t[0] : _t[_t.Length - 1]) : 0.0) : Math.Max(0, Math.Min(1, lt.Evaluate(w)));
            }
            if (_phi != null)
            {
                IInterpolator ip = _w.Length >= 4 ? (IInterpolator)new NaturalCubicSpline(_w, _phi) : new LinearInterpolator(_w, _phi);
                for (int i = 0; i < omega.Length; i++) phOut[i] = ip is NaturalCubicSpline sp ? sp.EvaluateLinearExtrapolate(omega[i]) : ip.Evaluate(omega[i]);
            }
            else if (_gd != null)
            {
                // integrate group delay (uniform omega grid assumed, as everywhere in the engine)
                var gsp = _w.Length >= 4 ? new NaturalCubicSpline(_w, _gd) : null;
                var gl = new LinearInterpolator(_w, _gd);
                for (int i = 0; i < omega.Length; i++) phOut[i] = 0;
                for (int i = 1; i < omega.Length; i++)
                {
                    double a = gsp != null ? gsp.EvaluateLinearExtrapolate(omega[i - 1]) : gl.Evaluate(omega[i - 1]);
                    double b = gsp != null ? gsp.EvaluateLinearExtrapolate(omega[i]) : gl.Evaluate(omega[i]);
                    phOut[i] = phOut[i - 1] + 0.5 * (a + b) * (omega[i] - omega[i - 1]);
                }
            }
        }
    }

    /// <summary>Filter from expressions "transmission" (power, clamped to [0,1]) and "phase" (rad). Variables:
    /// omega, lambda, lambda_um, lambda_nm, domega (= omega - omega0), omega0, plus "params".</summary>
    public sealed class ExpressionFilter : ISpectralFilter
    {
        private readonly CompiledExpression _t, _p; private readonly double _w0;
        public static readonly string[] Vars = { "omega", "lambda", "lambda_um", "lambda_nm", "domega", "omega0" };
        public ExpressionFilter(ModelSpec s)
        {
            var p = s.Params();
            _t = CompiledExpression.Compile(s.Json.GetString("transmission") ?? s.Json.GetString("expr") ?? "1", Vars, p);
            var ph = s.Json.GetString("phase");
            if (!string.IsNullOrEmpty(ph)) _p = CompiledExpression.Compile(ph, Vars, p);
            _w0 = s.GetDouble("omega0", double.NaN);
            if (double.IsNaN(_w0) && s.Json.Has("centerWavelengthM")) _w0 = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / s.GetRequired("centerWavelengthM");
        }
        /// <summary>Set by the compiler to the run's carrier when the spec did not give one.</summary>
        public double Omega0 { get; set; } = double.NaN;
        public string Description => "expression filter T=\"" + _t.Text + "\"" + (_p != null ? ", phi=\"" + _p.Text + "\"" : "");
        public void Evaluate(double[] omega, double[] tOut, double[] phOut)
        {
            double w0 = !double.IsNaN(_w0) ? _w0 : (!double.IsNaN(Omega0) ? Omega0 : omega[omega.Length / 2]);
            var b = OmegaVars.Bind(omega, w0);
            var t = _t.Evaluate(b, omega.Length);
            for (int i = 0; i < omega.Length; i++) tOut[i] = double.IsNaN(t[i]) ? 0 : Math.Max(0, Math.Min(1, t[i]));
            if (_p != null) { var p = _p.Evaluate(b, omega.Length); Array.Copy(p, phOut, omega.Length); }
        }
    }

    /// <summary>(Super-)Gaussian band-pass: {"kind":"gaussian","centerWavelengthM":..,"fwhmM":..,"peak":1}
    /// (FWHM of the power transmission, in wavelength; super-Gaussian uses "order").</summary>
    public sealed class ShapeFilter : ISpectralFilter
    {
        private readonly double _wc, _fwhmW, _peak; private readonly int _order;
        public ShapeFilter(ModelSpec s, int superGaussianOrder)
        {
            double lc = s.GetRequired("centerWavelengthM");
            double fwhmL = s.GetRequired("fwhmM");
            double c = PhysicalConstants.SpeedOfLightMPerS;
            _wc = 2 * Math.PI * c / lc;
            _fwhmW = 2 * Math.PI * c * fwhmL / (lc * lc);
            _peak = s.GetDouble("peak", 1.0);
            _order = Math.Max(1, superGaussianOrder);
        }
        public string Description => (_order == 1 ? "gaussian" : "super-gaussian(" + _order + ")") + " filter";
        public void Evaluate(double[] omega, double[] tOut, double[] phOut)
        {
            double s = _fwhmW / 2.0;
            for (int i = 0; i < omega.Length; i++)
            {
                double x = (omega[i] - _wc) / s;
                tOut[i] = _peak * Math.Exp(-Math.Log(2) * Math.Pow(x * x, _order));
            }
        }
    }

    public sealed class ConstantFilter : ISpectralFilter
    {
        private readonly double _t;
        public ConstantFilter(double t) { _t = Math.Max(0, Math.Min(1, t)); }
        public string Description => "constant transmission " + _t.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
        public void Evaluate(double[] omega, double[] tOut, double[] phOut) { for (int i = 0; i < omega.Length; i++) tOut[i] = _t; }
    }

    public sealed class CallbackFilter : ISpectralFilter
    {
        private readonly FilterCallback _cb; private readonly string _name;
        public CallbackFilter(string name, FilterCallback cb) { _name = name; _cb = cb; }
        public string Description => "callback @" + _name;
        public void Evaluate(double[] omega, double[] tOut, double[] phOut) { for (int i = 0; i < omega.Length; i++) tOut[i] = 1; _cb(omega, tOut, phOut); }
    }

    // ===================================================================================== beams

    /// <summary>
    /// Embedded-Gaussian beam (M^2) through a uniform medium:
    /// w(z) = w0 sqrt(1 + ((z - zw)/zR)^2), zR = pi w0^2 n / (M^2 lambda).
    /// JSON: {"kind":"gaussian","w0M":1e-4,"zWaistM":0.05,"m2":1.0,"wavelengthM":1.03e-6,"n":1.0}
    /// (optionally separate x/y: "w0xM","w0yM","zWaistXM","zWaistYM","m2x","m2y").
    /// zWaistM is measured from the element start.
    /// </summary>
    public sealed class GaussianBeam : IBeamModel
    {
        public double W0X, W0Y, ZwX, ZwY, ZrX, ZrY;
        public static GaussianBeam FromSpec(ModelSpec s)
        {
            var j = s.Json;
            double lam = j.GetDouble("wavelengthM", double.NaN);
            double n = j.GetDouble("n", 1.0);
            double w0 = j.GetDouble("w0M", double.NaN);
            double w0x = j.GetDouble("w0xM", w0), w0y = j.GetDouble("w0yM", w0);
            double zw = j.GetDouble("zWaistM", 0.0);
            double m2 = j.GetDouble("m2", 1.0);
            var b = new GaussianBeam { W0X = w0x, W0Y = w0y, ZwX = j.GetDouble("zWaistXM", zw), ZwY = j.GetDouble("zWaistYM", zw) };
            if (double.IsNaN(w0x) || double.IsNaN(w0y)) throw new ModelException(s.Origin + ": gaussian beam needs w0M (or w0xM and w0yM).");
            if (j.Has("zRayleighM") || j.Has("zRayleighXM"))
            {
                b.ZrX = j.GetDouble("zRayleighXM", j.GetDouble("zRayleighM", double.NaN));
                b.ZrY = j.GetDouble("zRayleighYM", j.GetDouble("zRayleighM", double.NaN));
            }
            else
            {
                if (double.IsNaN(lam)) throw new ModelException(s.Origin + ": gaussian beam needs wavelengthM (or zRayleighM).");
                b.ZrX = Math.PI * w0x * w0x * n / (j.GetDouble("m2x", m2) * lam);
                b.ZrY = Math.PI * w0y * w0y * n / (j.GetDouble("m2y", m2) * lam);
            }
            return b;
        }
        public string Description => string.Format(System.Globalization.CultureInfo.InvariantCulture, "gaussian w0 = {0:G4}/{1:G4} um, zR = {2:G4}/{3:G4} mm", W0X * 1e6, W0Y * 1e6, ZrX * 1e3, ZrY * 1e3);
        public double RadiusX(double z) => W0X * Math.Sqrt(1 + Math.Pow((z - ZwX) / ZrX, 2));
        public double RadiusY(double z) => W0Y * Math.Sqrt(1 + Math.Pow((z - ZwY) / ZrY, 2));
        public double EffectiveArea(double z) => Math.PI * RadiusX(z) * RadiusY(z) / 2.0;
    }

    /// <summary>Beam radii sampled along z (e.g. from Zemax GBPS at many points):
    /// {"kind":"sampled","zM":[...],"radiusXM":[...],"radiusYM":[...]} (radiusM for round beams).</summary>
    public sealed class SampledBeam : IBeamModel
    {
        private double[] _z, _wx, _wy;
        public static SampledBeam FromSpec(ModelSpec s)
        {
            var j = s.Json;
            var z = j.GetDoubleArray("zM") ?? throw new ModelException(s.Origin + ": sampled beam needs zM.");
            var wx = j.GetDoubleArray("radiusXM") ?? j.GetDoubleArray("radiusM") ?? throw new ModelException(s.Origin + ": sampled beam needs radiusM or radiusXM/radiusYM.");
            var wy = j.GetDoubleArray("radiusYM") ?? wx;
            if (z.Length != wx.Length || z.Length != wy.Length) throw new ModelException(s.Origin + ": sampled beam arrays must have equal length.");
            return new SampledBeam { _z = z, _wx = wx, _wy = wy };
        }
        public static SampledBeam FromArrays(double[] z, double[] wx, double[] wy) => new SampledBeam { _z = z, _wx = wx, _wy = wy ?? wx };
        public string Description => "sampled beam (" + _z.Length + " points)";
        private static double Interp(double[] x, double[] y, double v)
        {
            if (x.Length == 1) return y[0];
            if (v <= x[0]) return y[0];
            if (v >= x[x.Length - 1]) return y[y.Length - 1];
            int i = Array.BinarySearch(x, v); if (i >= 0) return y[i];
            i = ~i; double f = (v - x[i - 1]) / (x[i] - x[i - 1]);
            return y[i - 1] + f * (y[i] - y[i - 1]);
        }
        public double RadiusX(double z) => Interp(_z, _wx, z);
        public double RadiusY(double z) => Interp(_z, _wy, z);
        public double EffectiveArea(double z) => Math.PI * RadiusX(z) * RadiusY(z) / 2.0;
    }

    /// <summary>Constant beam: {"kind":"constant","radiusM":..} or {"areaM2":..} (effective area directly).</summary>
    public sealed class ConstantBeam : IBeamModel
    {
        public double Wx, Wy, Area;
        public static ConstantBeam FromSpec(ModelSpec s)
        {
            var j = s.Json;
            if (j.Has("areaM2")) { double a = j.GetDouble("areaM2", 0); double w = Math.Sqrt(2 * a / Math.PI); return new ConstantBeam { Wx = w, Wy = w, Area = a }; }
            double wx = j.GetDouble("radiusXM", j.GetDouble("radiusM", double.NaN)), wy = j.GetDouble("radiusYM", j.GetDouble("radiusM", wx));
            if (double.IsNaN(wx)) throw new ModelException(s.Origin + ": constant beam needs radiusM or areaM2.");
            return new ConstantBeam { Wx = wx, Wy = wy, Area = Math.PI * wx * wy / 2 };
        }
        public string Description => "constant beam, Aeff = " + Area.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " m^2";
        public double RadiusX(double z) => Wx;
        public double RadiusY(double z) => Wy;
        public double EffectiveArea(double z) => Area;
    }

    /// <summary>Beam radii from expressions in z: {"kind":"expression","radius":"w0*sqrt(1+((z-0.05)/zr)^2)","params":{...}}
    /// (or "radiusX" / "radiusY").</summary>
    public sealed class ExpressionBeam : IBeamModel
    {
        private readonly CompiledExpression _x, _y;
        public ExpressionBeam(ModelSpec s)
        {
            var p = s.Params();
            string rx = s.Json.GetString("radiusX") ?? s.Json.GetString("radius") ?? s.Json.GetString("expr") ?? throw new ModelException(s.Origin + ": expression beam needs \"radius\".");
            string ry = s.Json.GetString("radiusY") ?? rx;
            _x = CompiledExpression.Compile(rx, new[] { "z" }, p);
            _y = CompiledExpression.Compile(ry, new[] { "z" }, p);
        }
        public string Description => "expression beam w(z) = \"" + _x.Text + "\"";
        public double RadiusX(double z) => _x.EvaluateScalar(new Dictionary<string, double> { { "z", z } });
        public double RadiusY(double z) => _y.EvaluateScalar(new Dictionary<string, double> { { "z", z } });
        public double EffectiveArea(double z) => Math.PI * RadiusX(z) * RadiusY(z) / 2.0;
    }

    public sealed class CallbackBeam : IBeamModel
    {
        private readonly BeamCallback _cb; private readonly string _name;
        public CallbackBeam(string name, BeamCallback cb) { _name = name; _cb = cb; }
        public string Description => "callback @" + _name;
        private (double, double) Eval(double z) { var x = new double[1]; var y = new double[1]; _cb(new[] { z }, x, y); return (x[0], y[0]); }
        public double RadiusX(double z) => Eval(z).Item1;
        public double RadiusY(double z) => Eval(z).Item2;
        public double EffectiveArea(double z) { var (x, y) = Eval(z); return Math.PI * x * y / 2.0; }
    }

    // ===================================================================================== apertures

    /// <summary>Circular hard aperture of radius a on a centred Gaussian beam: T = 1 - exp(-2 a^2 / (wx wy))
    /// (exact for round beams; geometric-mean approximation for elliptical ones; decentre via the Marcum-Q
    /// approximation is ignored at L0 and reported).</summary>
    public sealed class CircularAperture : IAperture
    {
        public double Radius { get; }
        public CircularAperture(double radius) { Radius = radius; }
        public string Description => "circular aperture r = " + (Radius * 1e3).ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " mm";
        public double PowerTransmission(double wx, double wy, double dx, double dy)
        {
            if (Radius <= 0 || double.IsInfinity(Radius)) return Radius <= 0 ? 0 : 1;
            double w2 = wx * wy;
            return 1 - Math.Exp(-2 * Radius * Radius / w2);
        }
    }

    /// <summary>Rectangular aperture (half widths ax, ay) on a Gaussian beam: T = erf(sqrt2 ax/wx) erf(sqrt2 ay/wy) (decentre-aware).</summary>
    public sealed class RectangularAperture : IAperture
    {
        public double Ax { get; } public double Ay { get; }
        public RectangularAperture(double ax, double ay) { Ax = ax; Ay = ay; }
        public string Description => "rectangular aperture " + (2e3 * Ax).ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " x " + (2e3 * Ay).ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " mm";
        public double PowerTransmission(double wx, double wy, double dx, double dy)
        {
            double s2 = Math.Sqrt(2);
            double tx = 0.5 * (SpecialFunctions.Erf(s2 * (Ax - dx) / wx) + SpecialFunctions.Erf(s2 * (Ax + dx) / wx));
            double ty = 0.5 * (SpecialFunctions.Erf(s2 * (Ay - dy) / wy) + SpecialFunctions.Erf(s2 * (Ay + dy) / wy));
            return tx * ty;
        }
    }

    public static class SpecialFunctions
    {
        /// <summary>erf, Abramowitz-Stegun 7.1.26 refined (|error| &lt; 1.2e-7).</summary>
        public static double Erf(double x)
        {
            double t = 1.0 / (1.0 + 0.5 * Math.Abs(x));
            double y = 1 - t * Math.Exp(-x * x - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0 ? y : -y;
        }
    }
}
