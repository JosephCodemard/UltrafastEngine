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
                double p, q;
                if (Math.Abs(B) > 1e-300) { p = B; q = l - A; if (Math.Abs(p) + Math.Abs(q) < 1e-300) { p = l - Cc; q = B; } }
                else { if (Math.Abs(l - A) < Math.Abs(l - Cc)) { p = 1; q = 0; } else { p = 0; q = 1; } }
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
        public static readonly string[] Names = { "BBO", "LBO", "KTP", "KDP", "LiNbO3", "MgO:LiNbO3", "BiBO", "AgGaSe2" };

        private static IRefractiveIndex F2(string reference, double lo, double hi, params double[] c) => new RiiFormulaIndex(2, c, lo, hi, reference);
        private static IRefractiveIndex F4(string reference, double lo, double hi, params double[] c) => new RiiFormulaIndex(4, c, lo, hi, reference);
        private static Dictionary<string, double> Dpm(params (string, double)[] v) => v.ToDictionary(x => x.Item1, x => x.Item2);

        public static CrystalModel Get(string name, double temperatureC = double.NaN)
        {
            string key = (name ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            const string tam = "Tamosauskas et al., Opt. Mater. Express 8, 1410 (2018)";
            const string kato = "Kato & Takaoka, Appl. Opt. 41, 5040 (2002)";
            const string kato94 = "Kato, IEEE J. Quantum Electron. 30, 2950 (1994)";
            const string zern = "Zernike, JOSA 54, 1215 (1964)";
            const string zel = "Zelmon, Small & Jundt, JOSA B 14, 3319 (1997)";
            const string ume = "Umemura, Miyata & Kato, Opt. Mater. 30, 532 (2007)";
            const string katoAgs = "Kato & Shirahata, Jpn. J. Appl. Phys. 35, 4645 (1996) (refractiveindex.info: Kato)";
            double T = double.IsNaN(temperatureC) ? 20 : temperatureC;
            CrystalModel c;
            switch (key)
            {
                case "bbo": case "b-bab2o4": case "bab2o4": case "beta-bbo":
                    c = new CrystalModel
                    {
                        Name = "BBO", OpticalClass = "uniaxial", PointGroup = "3m",
                        Nx = F2(tam, 0.188, 5.2, 0, 0.90291, 0.003926, 0.83155, 0.018786, 0.76536, 60.01),
                        Nz = F2(tam, 0.188, 5.2, 0, 1.151075, 0.007142, 0.21803, 0.02259, 0.656, 263),
                        D = CrystalModel.Tensor("3m", Dpm(("d22", 2.2), ("d31", 0.04))), N2 = 2.8e-20,
                        ValidMinUm = 0.188, ValidMaxUm = 5.2,
                        References = "indices: " + tam + "; d22 = 2.2, d31 = 0.04 pm/V (Eckardt et al., IEEE JQE 26, 922 (1990)); n2: DeSalvo et al., IEEE JQE 32, 1324 (1996)"
                    };
                    break;
                case "lbo": case "lib3o5":
                    c = new CrystalModel
                    {
                        Name = "LBO", OpticalClass = "biaxial", PointGroup = "mm2 (polar axis = Y)",
                        // Kato (1994): n^2 = A + B/(l^2 - C) - D l^2 (+ E l^4 - F l^6), l in um. Reproduces the handbook
                        // XY-plane type-I SHG angles (phi = 11.6 deg at 1064 nm, 13.8 deg at 1030 nm, 31.7 deg at 800 nm);
                        // the Chen/Hanson-Dick fit on refractiveindex.info gives 11.8 / 14.6 / 34.3 deg.
                        Nx = F4(kato94, 0.19, 2.6, 2.4542, 0.01125, 0, 0.01135, 1, 0, 0, 0, 1, -0.01388, 2),
                        Ny = F4(kato94, 0.19, 2.6, 2.5390, 0.01277, 0, 0.01189, 1, 0, 0, 0, 1, -0.01849, 2, 4.3025e-5, 4, -2.9131e-5, 6),
                        Nz = F4(kato94, 0.19, 2.6, 2.5865, 0.01310, 0, 0.01223, 1, 0, 0, 0, 1, -0.01862, 2, 4.5778e-5, 4, -3.2526e-5, 6),
                        D = CrystalModel.Tensor("mm2:y", Dpm(("d31", -0.67), ("d32", 0.85), ("d33", 0.04))), N2 = 1.9e-20,
                        ValidMinUm = 0.19, ValidMaxUm = 2.6,
                        References = "indices: " + kato94 + " (room temperature); d31 = -0.67, d32 = 0.85, d33 = 0.04 pm/V (Dmitriev, Gurzadyan & Nikogosyan, Handbook of Nonlinear Optical Crystals); n2 ~ 1.9e-20 m^2/W (approximate)"
                    };
                    c.Notes.Add("LBO indices are room-temperature (no thermal model): non-critical phase matching by temperature is not modelled");
                    break;
                case "ktp": case "ktiopo4": case "ppktp":
                    c = new CrystalModel
                    {
                        Name = key == "ppktp" ? "PPKTP" : "KTP", OpticalClass = "biaxial", PointGroup = "mm2",
                        Nx = F4(kato, 0.43, 3.54, 3.29100, 0.04140, 0, 0.03978, 1, 9.35522, 0, 31.45571, 1),
                        Ny = F4(kato, 0.43, 3.54, 3.45018, 0.04341, 0, 0.04597, 1, 16.98825, 0, 39.43799, 1),
                        Nz = F4(kato, 0.43, 3.54, 4.59423, 0.06206, 0, 0.04763, 1, 110.80672, 0, 86.12171, 1),
                        D = CrystalModel.Tensor("mm2", Dpm(("d31", 1.95), ("d32", 3.9), ("d33", 15.3))), N2 = 2.2e-19,
                        ValidMinUm = 0.43, ValidMaxUm = 3.54,
                        References = "indices: " + kato + " (20 C); d31 = 1.95, d32 = 3.9, d33 = 15.3 pm/V at 1064 nm (Vanherzeele & Bierlein, Opt. Lett. 17, 982 (1992)); n2: DeSalvo et al. (1996), E||z"
                    };
                    break;
                case "kdp": case "kh2po4":
                    c = new CrystalModel
                    {
                        Name = "KDP", OpticalClass = "uniaxial", PointGroup = "-42m",
                        Nx = F4(zern, 0.2138, 1.529, 2.259276, 13.00522, 2, 400, 1, 0.01008956, 0, 0.0129426, 1),
                        Nz = F4(zern, 0.2138, 1.529, 2.132668, 3.2279924, 2, 400, 1, 0.008637494, 0, 0.0122810, 1),
                        D = CrystalModel.Tensor("-42m", Dpm(("d36", 0.39))), N2 = 2.0e-20,
                        ValidMinUm = 0.2138, ValidMaxUm = 1.529,
                        References = "indices: " + zern + "; d36 = 0.39 pm/V (Eckardt et al. 1990); n2: Adair et al. (refractiveindex.info)"
                    };
                    break;
                case "linbo3": case "ln": case "cln": case "ppln":
                    c = new CrystalModel
                    {
                        Name = key == "ppln" ? "PPLN (congruent)" : "LiNbO3 (congruent)", OpticalClass = "uniaxial", PointGroup = "3m", TemperatureDependent = true, TemperatureC = T,
                        Nx = F2(zel, 0.4, 5.0, 0, 2.6734, 0.01764, 1.2290, 0.05914, 12.614, 474.60),
                        Nz = new LithiumNiobateIndex(new[] { 5.35583, 0.100473, 0.20692, 100.0, 11.34927, 1.5334e-2 }, new[] { 4.629e-7, 3.862e-8, -0.89e-8, 2.657e-5 }, T, "Jundt, Opt. Lett. 22, 1553 (1997), congruent LiNbO3 e-ray"),
                        D = CrystalModel.Tensor("3m", Dpm(("d22", 2.1), ("d31", 4.6), ("d33", 25.0))), N2 = 9.3e-20,
                        ValidMinUm = 0.4, ValidMaxUm = 5.0,
                        References = "e: Jundt (1997) (temperature dependent); o: " + zel + " (21 C); d33 = 25, d31 = 4.6, d22 = 2.1 pm/V (Shoji et al., JOSA B 14, 2268 (1997)); n2: DeSalvo et al. (1996)"
                    };
                    break;
                case "mgo:linbo3": case "mgln": case "mgo:ln": case "mgo:ppln": case "ppmgln": case "mgo:cln":
                    c = new CrystalModel
                    {
                        Name = "MgO:LiNbO3 (5 mol%)", OpticalClass = "uniaxial", PointGroup = "3m", TemperatureDependent = true, TemperatureC = T,
                        Nx = F2(zel, 0.4, 5.0, 0, 2.6734, 0.01764, 1.2290, 0.05914, 12.614, 474.60),
                        Nz = new LithiumNiobateIndex(new[] { 5.756, 0.0983, 0.2020, 189.32, 12.52, 1.32e-2 }, new[] { 2.860e-6, 4.700e-8, 6.113e-8, 1.516e-4 }, T, "Gayer et al., Appl. Phys. B 91, 343 (2008), 5% MgO:CLN e-ray"),
                        D = CrystalModel.Tensor("3m", Dpm(("d22", 2.1), ("d31", 4.4), ("d33", 25.0))), N2 = 9.3e-20,
                        ValidMinUm = 0.5, ValidMaxUm = 4.0,
                        References = "e: Gayer et al. (2008) (temperature dependent; the room-temperature coefficients are verified, the temperature coefficient b4 = 1.516e-4 could not be cross-checked -- pyNLO uses 1.526e-5); o: congruent LiNbO3, " + zel + "; d33 = 25 pm/V"
                    };
                    c.Notes.Add("MgO:LiNbO3: the o-ray index is that of congruent LiNbO3 (only e-ray data for the doped crystal are built in); type-0 (eee) QPM is unaffected");
                    break;
                case "bibo": case "bib3o6":
                    c = new CrystalModel
                    {
                        Name = "BiBO", OpticalClass = "biaxial", PointGroup = null,
                        Nx = F4(ume, 0.48, 3.1, 3.07403, 0.03231, 0, 0.03163, 1, 0, 0, 0, 1, -0.013376, 2),
                        Ny = F4(ume, 0.48, 3.1, 3.16940, 0.03717, 0, 0.03483, 1, 0, 0, 0, 1, -0.01827, 2),
                        Nz = F4(ume, 0.48, 3.1, 3.6545, 0.05112, 0, 0.03713, 1, 0, 0, 0, 1, -0.02261, 2),
                        D = null, N2 = 7.65e-20, ValidMinUm = 0.48, ValidMaxUm = 3.1,
                        References = "indices (n_alpha, n_beta, n_gamma as x, y, z): " + ume + "; n2: Kabacinski et al. (refractiveindex.info); d tensor not built in (monoclinic): give deffPmPerV"
                    };
                    break;
                case "aggase2":
                    c = new CrystalModel
                    {
                        Name = "AgGaSe2", OpticalClass = "uniaxial", PointGroup = "-42m",
                        Nx = F4(katoAgs, 0.81, 18, 11.62264, 0.43221, 0, 0.15597, 1, 18868.52, 0, 3953.34, 1),
                        Nz = F4(katoAgs, 0.81, 18, 11.33168, 0.45951, 0, 0.21284, 1, 17816.81, 0, 3828.78, 1),
                        D = CrystalModel.Tensor("-42m", Dpm(("d36", 33))), N2 = 3.5e-18, ValidMinUm = 0.81, ValidMaxUm = 18,
                        References = "indices: " + katoAgs + "; d36 = 33 pm/V; n2 approximate"
                    };
                    break;
                default:
                    throw new ModelException("unknown crystal \"" + name + "\". Built-in crystals: " + string.Join(", ", Names) + ". Define your own with \"crystal\": {\"class\": \"uniaxial\", \"no\": ..., \"ne\": ..., \"d\": {...}}.");
            }
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
            if (o.Has("no")) { c.Nx = Ix("no"); c.Ny = c.Nx; }
            if (o.Has("ne")) c.Nz = Ix("ne");
            if (o.Has("nx")) c.Nx = Ix("nx");
            if (o.Has("ny")) c.Ny = Ix("ny");
            if (o.Has("nz")) c.Nz = Ix("nz");
            if (c.Ny == null) c.Ny = c.Nx;
            if (c.Nx == null || c.Nz == null) throw new ModelException(origin + ": a user crystal needs \"no\" and \"ne\" (uniaxial) or \"nx\", \"ny\", \"nz\" (biaxial) index models.");
            if (o.Has("d"))
            {
                var dObj = o.GetObject("d");
                var map = new Dictionary<string, double>();
                foreach (var kv in dObj) map[kv.Key.ToLowerInvariant()] = kv.Value.AsDouble();
                c.PointGroup = o.GetString("pointGroup", "general");
                c.D = CrystalModel.Tensor(c.PointGroup, map);
            }
            if (o.Has("deffPmPerV")) c.DeffOverrideMPerV = o.GetDouble("deffPmPerV", 0) * 1e-12;
            if (o.Has("n2")) c.N2 = o.GetDouble("n2", 0);
            if (o.Has("absorption")) c.Absorption = reg.Create<IAttenuation>("attenuation", ModelSpec.FromJson(o["absorption"], ModelSource.SystemData, origin + ".absorption"));
            if (o.Has("references")) c.References = o.GetString("references");
            return c;
        }
    }
}
