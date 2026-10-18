using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Physics
{
    /// <summary>
    /// Amplitude coefficients of a surface for s and p polarisation, POWER-normalised: |r|^2 = R and |t|^2 = T
    /// (T includes the Re(eta_s)/Re(eta_0) factor), so they act directly on fields in sqrt(W) units.
    /// Convention: physics time dependence exp(-i w t); r_s = r_p at normal incidence (so a mirror does not
    /// flip the transported x/y labels); a positive dphi/dw is a delay.
    /// </summary>
    public struct SurfaceCoefficients
    {
        public Complex Rs, Rp, Ts, Tp;
    }

    /// <summary>A surface treatment (bare Fresnel, multilayer stack, ideal/tabulated coating, grating efficiency).</summary>
    public interface ICoating
    {
        string Description { get; }
        LidtSpec Lidt { get; }
        /// <summary>n0: incident medium (complex, n + ik), ns: exit/substrate medium, theta0: angle of incidence in n0.</summary>
        SurfaceCoefficients Evaluate(double omega, double theta0, Complex n0, Complex ns);
    }

    // ================================================================================== thin-film TMM

    /// <summary>A thin-film layer: material (complex index via n and attenuation) and physical thickness.</summary>
    public sealed class FilmLayer
    {
        public Material Material { get; set; }
        public double ThicknessM { get; set; }
    }

    /// <summary>
    /// Characteristic-matrix (Abeles) method for an isotropic multilayer, physics convention exp(-i w t):
    /// M_j = [[cos d, -i sin d / eta], [-i eta sin d, cos d]], d = (w/c) N d cos(theta_j),
    /// eta_s = N cos(theta), eta_p = N / cos(theta), r = (eta0 B - C)/(eta0 B + C), [B; C] = prod(M_j) [1; eta_sub].
    /// With zero layers this is the Fresnel interface.
    /// </summary>
    public static class ThinFilm
    {
        public static Complex ComplexIndex(Material m, double omega)
        {
            double n = m.IndexAt(omega);
            double k = 0;
            if (m.Attenuation != null)
            {
                var a = new double[1];
                m.Attenuation.Evaluate(new[] { omega }, a);
                k = a[0] * PhysicalConstants.SpeedOfLightMPerS / (2 * omega);
            }
            return new Complex(n, k);
        }

        /// <summary>cos(theta) in a medium of index N given the invariant N0 sin(theta0); branch with Im(N cos) &gt;= 0
        /// (fields decay into the medium for exp(+ikz)).</summary>
        public static Complex CosTheta(Complex n, Complex n0SinTheta0)
        {
            var s = n0SinTheta0 / n;
            var c = Complex.Sqrt(1 - s * s);
            var nc = n * c;
            if (nc.Imaginary < 0 || (Math.Abs(nc.Imaginary) < 1e-15 && nc.Real < 0)) c = -c;
            return c;
        }

        public static SurfaceCoefficients Evaluate(double omega, double theta0, Complex n0, Complex ns, IReadOnlyList<FilmLayer> layers)
        {
            var inv = n0 * Math.Sin(theta0);
            var c0 = CosTheta(n0, inv);
            var cs = CosTheta(ns, inv);
            double k0 = omega / PhysicalConstants.SpeedOfLightMPerS;
            var res = new SurfaceCoefficients();
            for (int pol = 0; pol < 2; pol++)
            {
                Complex eta0 = pol == 0 ? n0 * c0 : n0 / c0;
                Complex etaS = pol == 0 ? ns * cs : ns / cs;
                Complex m11 = 1, m12 = 0, m21 = 0, m22 = 1;
                if (layers != null)
                    foreach (var L in layers)
                    {
                        var nj = ComplexIndex(L.Material, omega);
                        var cj = CosTheta(nj, inv);
                        var eta = pol == 0 ? nj * cj : nj / cj;
                        var d = k0 * nj * L.ThicknessM * cj;
                        var cd = Complex.Cos(d); var sd = Complex.Sin(d);
                        Complex a11 = cd, a12 = -Complex.ImaginaryOne * sd / eta, a21 = -Complex.ImaginaryOne * eta * sd, a22 = cd;
                        var t11 = m11 * a11 + m12 * a21; var t12 = m11 * a12 + m12 * a22;
                        var t21 = m21 * a11 + m22 * a21; var t22 = m21 * a12 + m22 * a22;
                        m11 = t11; m12 = t12; m21 = t21; m22 = t22;
                    }
                var B = m11 + m12 * etaS;
                var C = m21 + m22 * etaS;
                var den = eta0 * B + C;
                var r = (eta0 * B - C) / den;
                // power-normalised transmission amplitude: |t|^2 = 4 Re(eta0) Re(etaS) / |eta0 B + C|^2
                double norm = Math.Sqrt(Math.Max(0, 4 * eta0.Real * etaS.Real));
                var tphase = 2 * eta0 / den;
                var t = tphase.Magnitude > 0 ? Complex.FromPolarCoordinates(norm / den.Magnitude, tphase.Phase) : Complex.Zero;
                if (pol == 0) { res.Rs = r; res.Ts = t; } else { res.Rp = r; res.Tp = t; }
            }
            return res;
        }
    }

    // ================================================================================== coatings

    /// <summary>Bare interface (Fresnel) or a physical multilayer.</summary>
    public sealed class StackCoating : ICoating
    {
        public IReadOnlyList<FilmLayer> Layers { get; }
        public LidtSpec Lidt { get; set; }
        private readonly string _d;
        public StackCoating(IReadOnlyList<FilmLayer> layers, string description) { Layers = layers ?? new List<FilmLayer>(); _d = description; }
        public string Description => _d ?? (Layers.Count == 0 ? "bare (Fresnel)" : Layers.Count + "-layer stack");
        public SurfaceCoefficients Evaluate(double omega, double theta0, Complex n0, Complex ns) => ThinFilm.Evaluate(omega, theta0, n0, ns, Layers);
    }

    /// <summary>Non-physical coatings defined directly by R/T and phase (ideal, residual AR, GDD mirror, tables,
    /// grating efficiencies). Rs/Rp, Ts/Tp and phases are functions of omega only (AOI already baked in, e.g.
    /// data exported by Zemax for that surface).</summary>
    public sealed class SpecCoating : ICoating
    {
        public Func<double, (double Rs, double Rp, double Ts, double Tp, double PhiRs, double PhiRp, double PhiTs, double PhiTp)> F { get; }
        public LidtSpec Lidt { get; set; }
        private readonly string _d;
        public SpecCoating(string description, Func<double, (double, double, double, double, double, double, double, double)> f) { _d = description; F = f; }
        public string Description => _d;
        public SurfaceCoefficients Evaluate(double omega, double theta0, Complex n0, Complex ns)
        {
            var v = F(omega);
            return new SurfaceCoefficients
            {
                Rs = Complex.FromPolarCoordinates(Math.Sqrt(Math.Max(0, v.Rs)), v.PhiRs),
                Rp = Complex.FromPolarCoordinates(Math.Sqrt(Math.Max(0, v.Rp)), v.PhiRp),
                Ts = Complex.FromPolarCoordinates(Math.Sqrt(Math.Max(0, v.Ts)), v.PhiTs),
                Tp = Complex.FromPolarCoordinates(Math.Sqrt(Math.Max(0, v.Tp)), v.PhiTp)
            };
        }
    }

    /// <summary>Builds coatings from JSON (see docs/reference/coatings.md).</summary>
    public static class CoatingFactory
    {
        /// <summary>kinds: none | stack | quarterWave | ideal | ar | gdd | table | efficiency.</summary>
        public static ICoating Create(JsonObject o, Func<string, Material> materials, double defaultOmega0, string origin)
        {
            if (o == null) return new StackCoating(null, null);
            string kind = o.GetString("kind", "stack");
            var lidt = LidtSpec.FromJson(o.GetObject("lidt"));
            double c = PhysicalConstants.SpeedOfLightMPerS;
            ICoating result;
            switch (kind.ToLowerInvariant())
            {
                case "none":
                case "bare":
                    result = new StackCoating(null, "bare (Fresnel)");
                    break;
                case "stack":
                {
                    var layers = new List<FilmLayer>();
                    var arr = o.GetArray("layers") ?? new JsonArray();
                    double designL = o.GetDouble("designWavelengthM", 2 * Math.PI * c / defaultOmega0);
                    foreach (var lv in arr)
                    {
                        var lo = lv.AsObject();
                        var m = materials(lo.GetRequiredString("material", origin + " layer"));
                        double d = lo.Has("thicknessM") ? lo.GetDouble("thicknessM", 0) : lo.GetDouble("qwot", 1.0) * designL / (4 * m.IndexAt(2 * Math.PI * c / designL));
                        layers.Add(new FilmLayer { Material = m, ThicknessM = d });
                    }
                    result = new StackCoating(layers, o.GetString("name") ?? (layers.Count + "-layer stack"));
                    break;
                }
                case "quarterwave":
                {
                    string design = o.GetRequiredString("design", origin);
                    double designL = o.GetDouble("designWavelengthM", 2 * Math.PI * c / defaultOmega0);
                    var symbols = new Dictionary<char, Material>();
                    foreach (var key in new[] { "H", "L", "M" })
                        if (o.Has(key)) symbols[key[0]] = materials(o.GetString(key));
                    if (!symbols.ContainsKey('H')) symbols['H'] = materials("Ta2O5");
                    if (!symbols.ContainsKey('L')) symbols['L'] = materials("SiO2_film");
                    var layers = ExpandDesign(design, symbols, designL, origin);
                    result = new StackCoating(layers, o.GetString("name") ?? ("quarter-wave " + design + " @ " + (designL * 1e9).ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " nm"));
                    break;
                }
                case "ideal":
                case "ar":
                {
                    double R = o.GetDouble("R", kind == "ar" ? 0.0025 : 1.0);
                    double Rs = o.GetDouble("Rs", R), Rp = o.GetDouble("Rp", R);
                    double Ts = o.GetDouble("Ts", o.GetDouble("T", 1 - Rs)), Tp = o.GetDouble("Tp", o.GetDouble("T", 1 - Rp));
                    double ph = o.GetDouble("phaseRad", 0);
                    result = new SpecCoating(kind == "ar" ? "AR coating R = " + R.ToString("G3", System.Globalization.CultureInfo.InvariantCulture) : "ideal coating R = " + R.ToString("G6", System.Globalization.CultureInfo.InvariantCulture),
                        w => (Rs, Rp, Ts, Tp, ph, ph, 0, 0));
                    break;
                }
                case "gdd":
                {
                    double R = o.GetDouble("R", 0.999);
                    double gdd = o.GetDouble("gddS2", 0), tod = o.GetDouble("todS3", 0), fod = o.GetDouble("fodS4", 0);
                    double wr = o.Has("referenceWavelengthM") ? 2 * Math.PI * c / o.GetDouble("referenceWavelengthM", 0) : defaultOmega0;
                    result = new SpecCoating(string.Format(System.Globalization.CultureInfo.InvariantCulture, "GDD coating {0:G4} fs^2, R = {1:G5}", gdd * 1e30, R), w =>
                    {
                        double d = w - wr;
                        double phi = gdd * d * d / 2 + tod * d * d * d / 6 + fod * d * d * d * d / 24;
                        return (R, R, 1 - R, 1 - R, phi, phi, 0, 0);
                    });
                    break;
                }
                case "gddripple":
                case "chirped":
                {
                    // dispersive mirror coating: base GDD/TOD plus a sinusoidal GDD ripple (and optional reflectance ripple)
                    double R = o.GetDouble("R", 0.998);
                    double gdd = o.GetDouble("gddS2", 0) + o.GetDouble("gddFs2", 0) * 1e-30, tod = o.GetDouble("todS3", 0) + o.GetDouble("todFs3", 0) * 1e-45;
                    double amp = o.GetDouble("rippleGddS2", 0) + o.GetDouble("rippleGddFs2", 0) * 1e-30;
                    double wr = o.Has("referenceWavelengthM") ? 2 * Math.PI * c / o.GetDouble("referenceWavelengthM", 0) : defaultOmega0;
                    double period = o.GetDouble("ripplePeriodRadPerS", double.NaN);
                    if (double.IsNaN(period) && o.Has("ripplePeriodNm")) { double lam = 2 * Math.PI * c / wr; period = 2 * Math.PI * c * o.GetDouble("ripplePeriodNm", 0) * 1e-9 / (lam * lam); }
                    if (double.IsNaN(period) && o.Has("ripplePeriodThz")) period = 2 * Math.PI * o.GetDouble("ripplePeriodThz", 0) * 1e12;
                    if (amp != 0 && !(period > 0)) throw new JsonException(origin + ": gddRipple coating needs ripplePeriodNm, ripplePeriodThz or ripplePeriodRadPerS.");
                    double ph0 = o.GetDouble("ripplePhaseRad", 0), rRip = o.GetDouble("reflectanceRipple", 0);
                    double k = period > 0 ? 2 * Math.PI / period : 0;
                    result = new SpecCoating(string.Format(System.Globalization.CultureInfo.InvariantCulture, "dispersive coating {0:G4} fs^2, ripple {1:G3} fs^2, R = {2:G5}", gdd * 1e30, amp * 1e30, R), w =>
                    {
                        double d = w - wr;
                        double phi = gdd * d * d / 2 + tod * d * d * d / 6 + (k > 0 ? -amp / (k * k) * Math.Sin(k * d + ph0) : 0);
                        double Rw = Math.Max(0, Math.Min(1, R * (1 + rRip * Math.Sin(k * d + ph0))));
                        return (Rw, Rw, 1 - Rw, 1 - Rw, phi, phi, 0, 0);
                    });
                    break;
                }
                case "table":
                {
                    var wl = WavelengthsM(o, origin);
                    int n = wl.Length;
                    var w = wl.Select(l => 2 * Math.PI * c / l).ToArray();
                    double[] Get(string a, string b, double[] fallback) => o.GetDoubleArray(a) ?? (b != null ? o.GetDoubleArray(b) : null) ?? fallback;
                    var R = Get("R", null, null);
                    var Rs = Get("Rs", "R", null); var Rp = Get("Rp", "R", null);
                    var T = Get("T", null, null);
                    var Ts = Get("Ts", "T", Rs?.Select(x => 1 - x).ToArray()); var Tp = Get("Tp", "T", Rp?.Select(x => 1 - x).ToArray());
                    if (Rs == null && Ts == null) throw new JsonException(origin + ": coating table needs R/Rs/Rp and/or T/Ts/Tp.");
                    Rs = Rs ?? new double[n]; Rp = Rp ?? new double[n];
                    var prs = PhaseArray(o, "phaseRs", "phaseR", w); var prp = PhaseArray(o, "phaseRp", "phaseR", w);
                    var pts = PhaseArray(o, "phaseTs", "phaseT", w); var ptp = PhaseArray(o, "phaseTp", "phaseT", w);
                    var order = Enumerable.Range(0, n).OrderBy(i => w[i]).ToArray();
                    double[] S(double[] a) => order.Select(i => a[i]).ToArray();
                    var ws = S(w);
                    var I = new[] { S(Rs), S(Rp), S(Ts), S(Tp), S(prs), S(prp), S(pts), S(ptp) };
                    Func<double[], double, double> interp = (y, x) =>
                    {
                        if (ws.Length == 1) return y[0];
                        if (x <= ws[0]) return y[0];
                        if (x >= ws[ws.Length - 1]) return y[y.Length - 1];
                        int k = Array.BinarySearch(ws, x); if (k >= 0) return y[k]; k = ~k;
                        double f = (x - ws[k - 1]) / (ws[k] - ws[k - 1]); return y[k - 1] + f * (y[k] - y[k - 1]);
                    };
                    // phases: cubic spline (smooth GD), clamped linear continuation outside
                    var phSpl = I.Skip(4).Select(y => ws.Length >= 4 ? new NaturalCubicSpline(ws, y) : null).ToArray();
                    Func<int, double, double> ph = (k, x) => phSpl[k] != null ? phSpl[k].EvaluateLinearExtrapolate(x) : interp(I[4 + k], x);
                    result = new SpecCoating(o.GetString("name") ?? ("tabulated coating (" + n + " wavelengths)"),
                        x => (interp(I[0], x), interp(I[1], x), interp(I[2], x), interp(I[3], x), ph(0, x), ph(1, x), ph(2, x), ph(3, x)));
                    break;
                }
                case "efficiency":
                {
                    // grating / special surface: power efficiency for s and p, used in both reflect and diffract modes
                    double es = o.GetDouble("s", o.GetDouble("efficiency", 1.0)), ep = o.GetDouble("p", o.GetDouble("efficiency", 1.0));
                    result = new SpecCoating(string.Format(System.Globalization.CultureInfo.InvariantCulture, "efficiency s {0:G4} / p {1:G4}", es, ep), w => (es, ep, es, ep, 0, 0, 0, 0));
                    break;
                }
                default:
                    throw new JsonException(origin + ": unknown coating kind \"" + kind + "\" (none, stack, quarterWave, ideal, ar, gdd, gddRipple, table, efficiency).");
            }
            if (result is StackCoating sc) sc.Lidt = lidt;
            if (result is SpecCoating spc) spc.Lidt = lidt;
            return result;
        }

        private static double[] WavelengthsM(JsonObject o, string origin)
        {
            if (o.Has("wavelengthsM")) return o.GetDoubleArray("wavelengthsM");
            if (o.Has("wavelengthsNm")) return o.GetDoubleArray("wavelengthsNm").Select(x => x * 1e-9).ToArray();
            if (o.Has("wavelengthsUm")) return o.GetDoubleArray("wavelengthsUm").Select(x => x * 1e-6).ToArray();
            throw new JsonException(origin + ": table needs wavelengthsM.");
        }

        /// <summary>Phase from a phase array, or by integrating a group-delay array, or from a GDD array (double integral).</summary>
        private static double[] PhaseArray(JsonObject o, string key, string fallbackKey, double[] w)
        {
            var p = o.GetDoubleArray(key) ?? o.GetDoubleArray(fallbackKey);
            if (p != null) return p;
            string gdKey = key.Replace("phase", "groupDelay");
            var gd = o.GetDoubleArray(gdKey + "S") ?? o.GetDoubleArray(fallbackKey.Replace("phase", "groupDelay") + "S");
            if (gd == null) return new double[w.Length];
            var ord = Enumerable.Range(0, w.Length).OrderBy(i => w[i]).ToArray();
            var phi = new double[w.Length];
            for (int k = 1; k < ord.Length; k++)
                phi[ord[k]] = phi[ord[k - 1]] + 0.5 * (gd[ord[k]] + gd[ord[k - 1]]) * (w[ord[k]] - w[ord[k - 1]]);
            return phi;
        }

        /// <summary>Expand "(HL)^8 H 2L" style designs: H/L/M are quarter-wave optical thicknesses at the design
        /// wavelength; a numeric prefix multiplies the layer; (...)^n repeats.</summary>
        public static List<FilmLayer> ExpandDesign(string design, Dictionary<char, Material> symbols, double designWavelengthM, string origin)
        {
            string expanded = design.Replace(" ", "");
            var rx = new Regex(@"\(([^()]*)\)\^(\d+)");
            int guard = 0;
            while (rx.IsMatch(expanded) && guard++ < 50)
                expanded = rx.Replace(expanded, m => string.Concat(Enumerable.Repeat(m.Groups[1].Value, int.Parse(m.Groups[2].Value))));
            if (expanded.Contains("(") || expanded.Contains(")")) throw new JsonException(origin + ": could not parse coating design \"" + design + "\".");
            var layers = new List<FilmLayer>();
            double designOmega = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / designWavelengthM;
            var tok = new Regex(@"(\d*\.?\d*)([A-Za-z])");
            foreach (Match m in tok.Matches(expanded))
            {
                double mult = string.IsNullOrEmpty(m.Groups[1].Value) ? 1.0 : double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                char s = char.ToUpperInvariant(m.Groups[2].Value[0]);
                if (!symbols.TryGetValue(s, out var mat)) throw new JsonException(origin + ": coating design uses '" + s + "' but no material is defined for it.");
                double n = mat.IndexAt(designOmega);
                layers.Add(new FilmLayer { Material = mat, ThicknessM = mult * designWavelengthM / (4 * n) });
            }
            return layers;
        }
    }

    // ================================================================================== interface term

    /// <summary>
    /// A refracting, reflecting or diffracting surface: Fresnel or coating coefficients for s and p, rotated
    /// into the transported x/y basis: J = B^T diag(a_s, a_p) B, where B maps (Ex, Ey) to (Es, Ep).
    /// </summary>
    public sealed class InterfaceTerm : JonesTerm
    {
        public Material Before { get; } public Material After { get; } public ICoating Coating { get; }
        public string Mode { get; }  // "refract" | "reflect" | "diffract"
        public Func<double, double> Aoi { get; }
        public double B11 { get; } public double B12 { get; } public double B21 { get; } public double B22 { get; }
        private readonly string _label;

        public InterfaceTerm(string label, Material before, Material after, ICoating coating, string mode, Func<double, double> aoi, double[] basis)
        {
            _label = label; Before = before; After = after; Coating = coating ?? new StackCoating(null, null); Mode = (mode ?? "refract").ToLowerInvariant(); Aoi = aoi;
            B11 = basis[0]; B12 = basis[1]; B21 = basis[2]; B22 = basis[3];
            Mechanism = coating == null || (coating is StackCoating st && st.Layers.Count == 0) ? "fresnel" : (Mode == "diffract" ? "grating" : "coating");
        }

        public override string Description => _label + ": " + Mode + " " + Before.Name + " -> " + After.Name + ", " + Coating.Description;

        public SurfaceCoefficients Coefficients(double omega)
        {
            var n0 = ThinFilm.ComplexIndex(Before, omega);
            var ns = ThinFilm.ComplexIndex(After, omega);
            return Coating.Evaluate(omega, Aoi(omega), n0, ns);
        }

        protected override void Jones(double[] omega, Complex[] xx, Complex[] xy, Complex[] yx, Complex[] yy)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                if (omega[i] <= 0) { xx[i] = xy[i] = yx[i] = yy[i] = Complex.Zero; continue; }
                var c = Coefficients(omega[i]);
                Complex a, b;
                if (Mode == "refract") { a = c.Ts; b = c.Tp; }
                else { a = c.Rs; b = c.Rp; }
                xx[i] = B11 * B11 * a + B21 * B21 * b;
                xy[i] = B11 * B12 * a + B21 * B22 * b;
                yx[i] = xy[i];
                yy[i] = B12 * B12 * a + B22 * B22 * b;
            }
        }
    }

    // ================================================================================== basis transport

    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(double s, Vec3 a) => new Vec3(s * a.X, s * a.Y, s * a.Z);
        public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public Vec3 Cross(Vec3 b) => new Vec3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Norm => Math.Sqrt(Dot(this));
        public Vec3 Normalized() { double n = Norm; return n > 0 ? (1.0 / n) * this : this; }
        public static Vec3 From(double[] a) => new Vec3(a[0], a[1], a[2]);
        public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:F4},{1:F4},{2:F4})", X, Y, Z);
    }

    /// <summary>
    /// Transports the local polarisation basis (u, v) along a folded 3D ray path. At each surface (incoming
    /// direction kIn, outgoing kOut, surface normal n) it returns the 2x2 matrix B that maps (Eu, Ev) to (Es, Ep)
    /// and then rotates (u, v) with the rigid rotation taking (s, p_in, k_in) to (s, p_out, k_out). With this
    /// transport the Jones matrix of the surface is exactly B^T diag(a_s, a_p) B, and "x" at the output means the
    /// transported image of the input x axis (what a polarimeter after a periscope would call x).
    /// </summary>
    public sealed class BasisTracker
    {
        public Vec3 U { get; private set; } = new Vec3(1, 0, 0);
        public Vec3 V { get; private set; } = new Vec3(0, 1, 0);
        public Vec3 K { get; private set; } = new Vec3(0, 0, 1);

        private void AlignTo(Vec3 k)
        {
            k = k.Normalized();
            if ((k - K).Norm < 1e-12) return;
            // re-orthogonalise u against the new direction (handles coordinate breaks / small mismatches)
            var u = U - U.Dot(k) * k;
            if (u.Norm < 1e-9) u = V.Cross(k);
            u = u.Normalized();
            U = u; V = k.Cross(u); K = k;
        }

        /// <summary>Returns B as [b11, b12, b21, b22].</summary>
        public double[] Surface(Vec3 kIn, Vec3 kOut, Vec3 normal)
        {
            AlignTo(kIn);
            var s = kIn.Cross(normal);
            if (s.Norm < 1e-9) s = U; // normal incidence: any s; keep the basis
            s = s.Normalized();
            var pIn = K.Cross(s);
            var b = new[] { U.Dot(s), V.Dot(s), U.Dot(pIn), V.Dot(pIn) };
            var ko = kOut.Normalized();
            var pOut = ko.Cross(s);
            Vec3 Rot(Vec3 a) => a.Dot(s) * s + a.Dot(pIn) * pOut + a.Dot(K) * ko;
            var u2 = Rot(U).Normalized();
            U = u2; K = ko; V = K.Cross(U);
            return b;
        }

        /// <summary>2D (no ray vectors): plane of incidence given by the angle psi of the s direction from local x.
        /// psi = 90 deg (default) = folding in the x-z plane.</summary>
        public static double[] FromPsi(double psi) => new[] { Math.Cos(psi), Math.Sin(psi), -Math.Sin(psi), Math.Cos(psi) };
    }
}
