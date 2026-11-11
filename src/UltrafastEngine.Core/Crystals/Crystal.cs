using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Crystals
{
    /// <summary>
    /// Dispersion formulas in the refractiveindex.info convention (lambda in micrometres):
    /// formula 2: n^2 - 1 = C1 + sum_i C(2i) lambda^2 / (lambda^2 - C(2i+1));
    /// formula 4: n^2 = C1 + C2 l^C3/(l^2 - C4^C5) + C6 l^C7/(l^2 - C8^C9) + C10 l^C11 + C12 l^C13 + ...
    /// </summary>
    public sealed class RiiFormulaIndex : IRefractiveIndex
    {
        private readonly int _formula; private readonly double[] _c; private readonly string _ref;
        public double MinUm { get; } public double MaxUm { get; }
        public RiiFormulaIndex(int formula, double[] coefficients, double minUm, double maxUm, string reference)
        {
            if (formula != 2 && formula != 4) throw new ArgumentException("formula 2 or 4");
            _formula = formula; _c = coefficients; MinUm = minUm; MaxUm = maxUm; _ref = reference;
        }
        public string Description => "refractiveindex.info formula " + _formula + " (" + _ref + "; " + MinUm.ToString("G3", CultureInfo.InvariantCulture) + "-" + MaxUm.ToString("G3", CultureInfo.InvariantCulture) + " um)";
        public double At(double lUm)
        {
            double l2 = lUm * lUm, n2;
            if (_formula == 2)
            {
                n2 = 1 + _c[0];
                for (int i = 1; i + 1 < _c.Length; i += 2) n2 += _c[i] * l2 / (l2 - _c[i + 1]);
            }
            else
            {
                double C(int k) => k <= _c.Length ? _c[k - 1] : 0.0;
                n2 = C(1);
                if (_c.Length >= 5) n2 += C(2) * Math.Pow(lUm, C(3)) / (l2 - Math.Pow(C(4), C(5)));
                if (_c.Length >= 9) n2 += C(6) * Math.Pow(lUm, C(7)) / (l2 - Math.Pow(C(8), C(9)));
                for (int k = 10; k + 1 <= _c.Length; k += 2) n2 += C(k) * Math.Pow(lUm, C(k + 1));
            }
            return n2 > 0 ? Math.Sqrt(n2) : double.NaN;
        }
        public void Evaluate(double[] omega, double[] nOut)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double w = omega[i];
                nOut[i] = w > 0 ? At(2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / w * 1e6) : double.NaN;
            }
        }
    }

    /// <summary>Temperature-dependent extraordinary index of (MgO-doped) congruent LiNbO3 in the Jundt / Gayer form:
    /// n_e^2 = a1 + b1 f + (a2 + b2 f)/(l^2 - (a3 + b3 f)^2) + (a4 + b4 f)/(l^2 - a5^2) - (a6 + b5 f) l^2,
    /// f = (T - 24.5)(T + 570.82), T in degC, l in um.</summary>
    public sealed class LithiumNiobateIndex : IRefractiveIndex
    {
        private readonly double[] _a, _b; private readonly double _t; private readonly string _ref;
        public LithiumNiobateIndex(double[] a, double[] b, double temperatureC, string reference) { _a = a; _b = b; _t = temperatureC; _ref = reference; }
        public string Description => _ref + string.Format(CultureInfo.InvariantCulture, ", T = {0:G4} C", _t);
        public void Evaluate(double[] omega, double[] nOut)
        {
            double f = (_t - 24.5) * (_t + 570.82);
            double b5 = _b.Length > 4 ? _b[4] : 0;
            for (int i = 0; i < omega.Length; i++)
            {
                double w = omega[i];
                if (w <= 0) { nOut[i] = double.NaN; continue; }
                double l = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / w * 1e6, l2 = l * l;
                double a3 = _a[2] + _b[2] * f;
                double n2 = _a[0] + _b[0] * f + (_a[1] + _b[1] * f) / (l2 - a3 * a3) + (_a[3] + _b[3] * f) / (l2 - _a[4] * _a[4]) - (_a[5] + b5 * f) * l2;
                nOut[i] = n2 > 0 ? Math.Sqrt(n2) : double.NaN;
            }
        }
    }

    /// <summary>A propagation eigenmode of an anisotropic crystal: index, D-field and E-field unit vectors, walk-off.</summary>
    public struct CrystalMode
    {
        public double N;
        public Vec3 D, E;
        /// <summary>Angle between E and D (= angle between the Poynting vector and k), rad.</summary>
        public double WalkOffRad;
    }

    /// <summary>
    /// A chi(2) crystal: principal refractive indices in the dielectric frame (nx &lt;= ny &lt;= nz for biaxial; nx = ny = no,
    /// nz = ne for uniaxial), the second-order tensor d_il (contracted, m/V) in the same frame, and n2. Directions
    /// are given by polar angles (theta from z, phi from x in the x-y plane). The two eigenmodes of a direction are
    /// called "slow" (larger index) and "fast"; for uniaxial crystals "o" and "e" map onto them by the sign of the
    /// birefringence.
    /// </summary>
    public sealed class CrystalModel
    {
        public string Name { get; set; }
        /// <summary>"uniaxial" or "biaxial".</summary>
        public string OpticalClass { get; set; }
        public IRefractiveIndex Nx { get; set; }
        public IRefractiveIndex Ny { get; set; }
        public IRefractiveIndex Nz { get; set; }
        /// <summary>d_il (3 x 6, contracted Voigt notation), m/V; null if unknown (then DeffOverride is required).</summary>
        public double[,] D { get; set; }
        public string PointGroup { get; set; }
        public double DeffOverrideMPerV { get; set; } = double.NaN;
        public double N2 { get; set; }
        public IAttenuation Absorption { get; set; }
        public double TemperatureC { get; set; } = 20;
        public double ValidMinUm { get; set; } = double.NaN;
        public double ValidMaxUm { get; set; } = double.NaN;
        public string References { get; set; }
        public List<string> Notes { get; } = new List<string>();
        public bool TemperatureDependent { get; set; }
        /// <summary>The catalogue quantities this crystal takes its numbers from ("indexX", "indexZ", "d", "n2", ...); a user override removes the quantity.</summary>
        public Dictionary<string, CatalogueQuantity> Used { get; } = new Dictionary<string, CatalogueQuantity>(StringComparer.OrdinalIgnoreCase);

        public bool IsUniaxial => OpticalClass == "uniaxial";
        /// <summary>Uniaxial: true if ne &lt; no (BBO, KDP, LiNbO3); then o = slow, e = fast.</summary>
        public bool NegativeUniaxial(double omega) => IsUniaxial && Idx(Nz, omega) < Idx(Nx, omega);

        internal static double Idx(IRefractiveIndex n, double w) { var o = new double[1]; n.Evaluate(new[] { w }, o); return o[0]; }

        public static Vec3 Direction(double thetaRad, double phiRad) => new Vec3(Math.Sin(thetaRad) * Math.Cos(phiRad), Math.Sin(thetaRad) * Math.Sin(phiRad), Math.Cos(thetaRad));

        /// <summary>The two eigenmodes along unit direction s at omega: (slow, fast).</summary>
        public (CrystalMode Slow, CrystalMode Fast) Modes(double omega, Vec3 s)
        {
            double nx = Idx(Nx, omega), ny = Idx(Ny, omega), nz = Idx(Nz, omega);
            double ex = 1 / (nx * nx), ey = 1 / (ny * ny), ez = 1 / (nz * nz); // impermeability
            s = s.Normalized();
            Vec3 a = Math.Abs(s.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
            var u = s.Cross(a).Normalized(); var v = s.Cross(u).Normalized();
            double Eta(Vec3 p, Vec3 q) => p.X * q.X * ex + p.Y * q.Y * ey + p.Z * q.Z * ez;
            double A = Eta(u, u), B = Eta(u, v), Cc = Eta(v, v);
            double mean = 0.5 * (A + Cc), dif = Math.Sqrt(0.25 * (A - Cc) * (A - Cc) + B * B);
            double lSlow = mean - dif, lFast = mean + dif; // smaller impermeability = larger index
            Vec3 Vec(double l)
            {
                // (B, l - A) and (l - Cc, B) are both eigenvectors of [[A, B], [B, Cc]] for the eigenvalue l; take the one with the
                // larger norm. With B at rounding level (uniaxial crystal: the o axis is exactly u) the first is pure noise
                // (B ~ 1e-17, l - A ~ 1e-17) and its direction was random, so d_eff changed erratically with phi.
                double p1 = B, q1 = l - A, p2 = l - Cc, q2 = B, p, q;
                if (p1 * p1 + q1 * q1 >= p2 * p2 + q2 * q2) { p = p1; q = q1; } else { p = p2; q = q2; }
                if (p * p + q * q < 1e-300) { p = 1; q = 0; } // isotropic: any direction
                if (Math.Abs(p) >= Math.Abs(q) ? p < 0 : q < 0) { p = -p; q = -q; }   // one sign convention for both branches (d_eff changes sign only where it should)
                return (p * u + q * v).Normalized();
            }
            var dS = Vec(lSlow);
            var dF = s.Cross(dS).Normalized(); // orthogonal partner, keeps the pair exactly orthogonal at degeneracy
            if (Math.Abs(dif) < 1e-15) dF = s.Cross(dS).Normalized();
            CrystalMode Mode(double l, Vec3 d)
            {
                var e = new Vec3(d.X * ex, d.Y * ey, d.Z * ez).Normalized();
                double c = Math.Max(-1, Math.Min(1, e.Dot(d)));
                return new CrystalMode { N = 1 / Math.Sqrt(l), D = d, E = e, WalkOffRad = Math.Acos(Math.Abs(c)) };
            }
            return (Mode(lSlow, dS), Mode(lFast, dF));
        }

        /// <summary>Mode for a role "slow"/"fast" (or "o"/"e" for uniaxial crystals).</summary>
        public CrystalMode Mode(double omega, Vec3 s, string role)
        {
            var (sl, fa) = Modes(omega, s);
            return IsSlow(role, omega) ? sl : fa;
        }

        public bool IsSlow(string role, double omega)
        {
            switch ((role ?? "").ToLowerInvariant())
            {
                case "slow": case "s": return true;
                case "fast": case "f": return false;
                case "o": case "ordinary":
                    if (!IsUniaxial) throw new ModelException(Name + " is biaxial: use polarisation roles \"slow\"/\"fast\" (or type I/II) instead of o/e");
                    return NegativeUniaxial(1e15 * 2);
                case "e": case "extraordinary":
                    if (!IsUniaxial) throw new ModelException(Name + " is biaxial: use polarisation roles \"slow\"/\"fast\" (or type I/II) instead of o/e");
                    return !NegativeUniaxial(1e15 * 2);
                default: throw new ModelException("unknown polarisation role \"" + role + "\" (slow, fast, o, e)");
            }
        }

        private static readonly int[,] Voigt = { { 0, 5, 4 }, { 5, 1, 3 }, { 4, 3, 2 } };

        /// <summary>d_eff = e3 . d : e1 e2 (m/V) for E-field unit vectors of the three waves (e3 = highest frequency).</summary>
        public double Deff(Vec3 e1, Vec3 e2, Vec3 e3)
        {
            if (!double.IsNaN(DeffOverrideMPerV)) return DeffOverrideMPerV;
            if (D == null) throw new ModelException("crystal " + Name + " has no d tensor in the library: give \"deffPmPerV\" on the element (or a \"d\" tensor in a user crystal)");
            double[] a = { e1.X, e1.Y, e1.Z }, b = { e2.X, e2.Y, e2.Z }, c = { e3.X, e3.Y, e3.Z };
            double s = 0;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    for (int k = 0; k < 3; k++)
                        s += c[i] * D[i, Voigt[j, k]] * a[j] * b[k];
            return s;
        }

        public string Describe()
        {
            var ci = CultureInfo.InvariantCulture;
            return Name + " (" + OpticalClass + (PointGroup != null ? ", " + PointGroup : "") + (TemperatureDependent ? string.Format(ci, ", T = {0:G4} C", TemperatureC) : "") + ")";
        }

        /// <summary>Contracted d tensor for common point groups from the independent coefficients (pm/V), Kleinman symmetry.</summary>
        public static double[,] Tensor(string pointGroup, Dictionary<string, double> dPm)
        {
            double G(string k) => dPm.TryGetValue(k, out var v) ? v * 1e-12 : 0.0;
            var d = new double[3, 6];
            switch (pointGroup)
            {
                case "3m": // BBO, LiNbO3: d22, d31(=d15), d33
                    d[0, 4] = G("d31"); d[0, 5] = -G("d22");
                    d[1, 0] = -G("d22"); d[1, 1] = G("d22"); d[1, 3] = G("d31");
                    d[2, 0] = G("d31"); d[2, 1] = G("d31"); d[2, 2] = G("d33");
                    break;
                case "mm2": // KTP: polar axis = dielectric z; d31(=d15), d32(=d24), d33
                    d[0, 4] = G("d31"); d[1, 3] = G("d32");
                    d[2, 0] = G("d31"); d[2, 1] = G("d32"); d[2, 2] = G("d33");
                    break;
                case "mm2:y":
                    // LBO: crystallographic (a, b, c) = dielectric (X, Z, Y); the polar axis c is the dielectric Y axis.
                    // Coefficients are quoted in the crystallographic frame (d31, d32, d33) and mapped:
                    // d_YXX = d31, d_YZZ = d32, d_YYY = d33, d_XXY = d31, d_ZZY = d32 (Kleinman). Then the XY-plane
                    // type-I deff is d32 cos(phi), as in the handbooks.
                    d[1, 0] = G("d31"); d[1, 2] = G("d32"); d[1, 1] = G("d33");
                    d[0, 5] = G("d31"); d[2, 3] = G("d32");
                    break;
                case "-42m": // KDP, AgGaSe2: d36 (= d14 = d25)
                    d[0, 3] = G("d36"); d[1, 4] = G("d36"); d[2, 5] = G("d36");
                    break;
                case "general":
                    foreach (var kv in dPm)
                    {
                        if (kv.Key.Length != 3 || kv.Key[0] != 'd') continue;
                        int i = kv.Key[1] - '1', l = kv.Key[2] - '1';
                        if (i >= 0 && i < 3 && l >= 0 && l < 6) d[i, l] = kv.Value * 1e-12;
                    }
                    break;
                default: throw new ModelException("unknown point group \"" + pointGroup + "\" (3m, mm2, mm2:y, -42m, or \"general\" with explicit d_il)");
            }
            return d;
        }
    }

    /// <summary>Built-in chi(2) crystals and user-defined ones from JSON.</summary>
    public static class CrystalLibrary
    {
        /// <summary>The built-in crystals, in catalogue order (Data/materials/crystals).</summary>
        public static string[] Names => Catalogue.Entries("crystal").Select(e => e.Id).ToArray();

        private static Dictionary<string, double> Dpm(JsonObject o) => o.ToDictionary(kv => kv.Key, kv => kv.Value.AsDouble());

        /// <summary>The index model of an "index*" quantity of a crystal (refractiveindex.info formula 2 or 4, or the temperature-dependent LiNbO3 form).</summary>
        private static IRefractiveIndex Index(CatalogueQuantity q, double temperatureC)
        {
            var vr = q.Array("validRangeUm"); string cite = q.Text("citation");
            switch (q.Text("model"))
            {
                case "riiFormula2": return new RiiFormulaIndex(2, q.Array("coefficients"), vr[0], vr[1], cite);
                case "riiFormula4": return new RiiFormulaIndex(4, q.Array("coefficients"), vr[0], vr[1], cite);
                case "jundt": return new LithiumNiobateIndex(q.Array("a"), q.Array("b"), temperatureC, cite);
                default: throw new ModelException("catalogue " + q.Path + ": unknown crystal index model \"" + q.Text("model") + "\".");
            }
        }

        /// <summary>A built-in chi(2) crystal from the catalogue: principal indices, d tensor, n2, validity range, each with its source and status.</summary>
        public static CrystalModel Get(string name, double temperatureC = double.NaN)
        {
            string key = (name ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            var e = Catalogue.Find(key, "crystal") ?? throw new ModelException("unknown crystal \"" + name + "\". Built-in crystals: " + string.Join(", ", Names) + ". Define your own with \"crystal\": {\"class\": \"uniaxial\", \"no\": ..., \"ne\": ..., \"d\": {...}}.");
            double T = double.IsNaN(temperatureC) ? 20 : temperatureC;
            bool tdep = e.Raw.GetBool("temperatureDependent", false);
            string display = e.Raw.GetObject("aliasDisplayNames")?.GetString(key) ?? e.Raw.GetString("displayName", e.Id);
            var c = new CrystalModel { Name = display, OpticalClass = e.Raw.GetString("opticalClass"), TemperatureDependent = tdep, References = e.Raw.GetString("references") };
            if (tdep) c.TemperatureC = T;
            c.Nx = Index(e.Q("indexX"), T); c.Nz = Index(e.Q("indexZ"), T);
            if (e.Has("indexY")) c.Ny = Index(e.Q("indexY"), T);
            var d = e.TryQ("d");
            if (d != null) { c.PointGroup = d.Text("pointGroup"); c.D = CrystalModel.Tensor(d.Text("tensor"), Dpm(d.Json.GetObject("dPmPerV"))); }
            c.N2 = e.Q("n2").Number("valueM2PerW");
            var v = e.Q("validity").Array("validRangeUm"); c.ValidMinUm = v[0]; c.ValidMaxUm = v[1];
            if (!string.IsNullOrEmpty(e.Notes)) c.Notes.Add(e.Notes);
            foreach (var q in e.Quantities.Values) c.Used[q.Name] = q;
            if (c.Ny == null) c.Ny = c.Nx;
            if (!c.TemperatureDependent && !double.IsNaN(temperatureC) && Math.Abs(temperatureC - 20) > 5)
                c.Notes.Add(c.Name + ": the built-in indices are not temperature dependent; temperatureC = " + temperatureC.ToString("G4", CultureInfo.InvariantCulture) + " is ignored");
            return c;
        }

        /// <summary>
        /// A user-defined crystal:
        /// <code>{ "name": "MyXtal", "class": "uniaxial", "no": {index model}, "ne": {index model},
        ///   "pointGroup": "3m", "d": {"d22": 2.2, "d31": 0.04}, "n2": 3e-20 }
        /// biaxial: "nx", "ny", "nz"; or "deffPmPerV" instead of a tensor; or "base": "BBO" + any of the above to modify a built-in.</code>
        /// </summary>
        public static CrystalModel FromJson(JsonObject o, ModelRegistry reg, double temperatureC, string origin)
        {
            CrystalModel c = o.Has("base") ? Get(o.GetString("base"), temperatureC) : new CrystalModel { Name = o.GetString("name", "user crystal"), OpticalClass = "uniaxial" };
            if (o.Has("name")) c.Name = o.GetString("name");
            if (o.Has("class")) c.OpticalClass = o.GetString("class").ToLowerInvariant();
            IRefractiveIndex Ix(string key) => reg.Create<IRefractiveIndex>("index", ModelSpec.FromJson(o[key], ModelSource.SystemData, origin + "." + key));
            if (o.Has("no")) { c.Nx = Ix("no"); c.Ny = c.Nx; c.Used.Remove("indexX"); c.Used.Remove("indexY"); }
            if (o.Has("ne")) { c.Nz = Ix("ne"); c.Used.Remove("indexZ"); }
            if (o.Has("nx")) { c.Nx = Ix("nx"); c.Used.Remove("indexX"); }
            if (o.Has("ny")) { c.Ny = Ix("ny"); c.Used.Remove("indexY"); }
            if (o.Has("nz")) { c.Nz = Ix("nz"); c.Used.Remove("indexZ"); }
            if (c.Ny == null) c.Ny = c.Nx;
            if (c.Nx == null || c.Nz == null) throw new ModelException(origin + ": a user crystal needs \"no\" and \"ne\" (uniaxial) or \"nx\", \"ny\", \"nz\" (biaxial) index models.");
            if (o.Has("d"))
            {
                var dObj = o.GetObject("d");
                var map = new Dictionary<string, double>();
                foreach (var kv in dObj) map[kv.Key.ToLowerInvariant()] = kv.Value.AsDouble();
                c.PointGroup = o.GetString("pointGroup", "general");
                c.D = CrystalModel.Tensor(c.PointGroup, map); c.Used.Remove("d");
            }
            if (o.Has("deffPmPerV")) { c.DeffOverrideMPerV = o.GetDouble("deffPmPerV", 0) * 1e-12; c.Used.Remove("d"); }
            if (o.Has("n2")) { c.N2 = o.GetDouble("n2", 0); c.Used.Remove("n2"); }
            if (o.Has("absorption")) c.Absorption = reg.Create<IAttenuation>("attenuation", ModelSpec.FromJson(o["absorption"], ModelSource.SystemData, origin + ".absorption"));
            if (o.Has("references")) c.References = o.GetString("references");
            return c;
        }
    }
}
