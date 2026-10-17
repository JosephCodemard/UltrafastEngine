using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Materials
{
    /// <summary>
    /// Reader for the refractiveindex.info database format (YAML pages: DATA blocks of type "formula 1" .. "formula 9",
    /// "tabulated n", "tabulated k", "tabulated nk", and SPECS such as temperature / pressure). The database is public
    /// domain (CC0); a subset is bundled with the engine (<see cref="Bundled"/>), any page or a full local clone can be
    /// read from disk (ULTRAFAST_RII_PATH points at the clone's "data" folder). Wavelengths are in micrometres in the files.
    /// Gases (formula 6 or a gas SPECS block) are scaled with density p T_ref / (p_ref T); solids take an optional
    /// thermo-optic coefficient dn/dT. Outside the stated wavelength range formulas are evaluated as they are (with a
    /// note in the description) and tables hold their end values.
    /// </summary>
    public sealed class RefractiveIndexInfoPage
    {
        public string Name { get; set; }
        public string References { get; set; }
        public string Comments { get; set; }
        public List<(string Type, double[] Range, double[] Coefficients, double[][] Rows)> Data { get; } = new List<(string, double[], double[], double[][])>();
        public Dictionary<string, string> Specs { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Parse the YAML subset used by the database.</summary>
        public static RefractiveIndexInfoPage Parse(string yaml, string name = null)
        {
            var page = new RefractiveIndexInfoPage { Name = name };
            var lines = yaml.Replace("\r", "").Split('\n');
            string section = null;
            string type = null; double[] range = null, coef = null; List<double[]> rows = null;
            bool inBlock = false; int blockIndent = -1;
            void Flush()
            {
                if (type != null) page.Data.Add((type, range, coef, rows?.ToArray()));
                type = null; range = null; coef = null; rows = null;
            }
            foreach (var raw in lines)
            {
                if (raw.TrimStart().StartsWith("#")) continue;
                int indent = raw.Length - raw.TrimStart().Length;
                string line = raw.Trim();
                if (inBlock)
                {
                    if (line.Length > 0 && indent >= blockIndent)
                    {
                        var v = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                        rows.Add(v);
                        continue;
                    }
                    if (line.Length == 0) continue;
                    inBlock = false;
                }
                if (line.Length == 0) continue;
                if (indent == 0)
                {
                    Flush();
                    int c = line.IndexOf(':');
                    string key = c > 0 ? line.Substring(0, c).Trim() : line;
                    string val = c > 0 ? Unquote(line.Substring(c + 1).Trim()) : "";
                    section = key.ToUpperInvariant();
                    if (section == "REFERENCES") page.References = val;
                    else if (section == "COMMENTS") page.Comments = val;
                    continue;
                }
                if (section == "DATA")
                {
                    if (line.StartsWith("- ")) { Flush(); line = line.Substring(2).Trim(); }
                    int c = line.IndexOf(':');
                    if (c < 0) continue;
                    string key = line.Substring(0, c).Trim().ToLowerInvariant(), val = Unquote(line.Substring(c + 1).Trim());
                    switch (key)
                    {
                        case "type": type = val.ToLowerInvariant(); break;
                        case "wavelength_range": case "range": range = Nums(val); break;
                        case "coefficients": coef = Nums(val); break;
                        case "data":
                            rows = new List<double[]>();
                            if (val.StartsWith("|")) { inBlock = true; blockIndent = indent + 1; }
                            else if (val.Length > 0) rows.Add(Nums(val));
                            break;
                    }
                }
                else if (section == "SPECS")
                {
                    int c = line.IndexOf(':');
                    if (c > 0) page.Specs[line.Substring(0, c).Trim()] = Unquote(line.Substring(c + 1).Trim());
                }
            }
            Flush();
            if (page.Data.Count == 0) throw new FormatException("refractiveindex.info page" + (name != null ? " '" + name + "'" : "") + " has no DATA block.");
            return page;
        }

        private static string Unquote(string s) => s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0] ? s.Substring(1, s.Length - 2) : s;
        private static double[] Nums(string s) => s.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();

        /// <summary>A number from SPECS ("20.0 °C", "101325 Pa", "1 atm"), converted to K or Pa; NaN if absent.</summary>
        public double Spec(string key)
        {
            if (!Specs.TryGetValue(key, out var v)) return double.NaN;
            var parts = v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) return double.NaN;
            string unit = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "";
            if (unit.Contains("c") && unit.Contains("°") || unit == "c" || unit == "degc") return x + 273.15;
            if (unit == "k") return x;
            if (unit == "pa") return x;
            if (unit == "kpa") return x * 1e3;
            if (unit == "atm") return x * 101325;
            if (unit == "bar") return x * 1e5;
            if (unit == "torr" || unit == "mmhg") return x * 133.322;
            return x;
        }

        public bool IsGas => Data.Any(d => d.Type == "formula 6") || Specs.ContainsKey("pressure");

        // ------------------------------------------------------------------------------------------ index

        /// <summary>n(λ) from the formula / table blocks (λ in um).</summary>
        public double IndexAt(double lambdaUm)
        {
            foreach (var (t, rng, c, rows) in Data)
            {
                switch (t)
                {
                    case "tabulated n": case "tabulated nk": return Table(rows, 1, lambdaUm);
                    case "tabulated k": continue;
                }
                if (t.StartsWith("formula")) return Formula(int.Parse(t.Substring(8), CultureInfo.InvariantCulture), c, lambdaUm);
            }
            throw new FormatException("refractiveindex.info page '" + Name + "' has no n data (only k).");
        }

        /// <summary>Extinction coefficient k(λ), 0 if the page has none.</summary>
        public double KAt(double lambdaUm)
        {
            foreach (var (t, _, _, rows) in Data)
            {
                if (t == "tabulated k") return Table(rows, 1, lambdaUm);
                if (t == "tabulated nk") return Table(rows, 2, lambdaUm);
            }
            return 0;
        }

        public bool HasK => Data.Any(d => d.Type == "tabulated k" || d.Type == "tabulated nk");

        public double[] Range
        {
            get
            {
                foreach (var (t, rng, _, rows) in Data)
                {
                    if (rng != null && rng.Length >= 2) return rng;
                    if (rows != null && rows.Length > 0) return new[] { rows.Min(r => r[0]), rows.Max(r => r[0]) };
                }
                return null;
            }
        }

        private static double Table(double[][] rows, int col, double x)
        {
            if (rows == null || rows.Length == 0) return double.NaN;
            if (x <= rows[0][0]) return rows[0][col];
            if (x >= rows[rows.Length - 1][0]) return rows[rows.Length - 1][col];
            int lo = 0, hi = rows.Length - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (rows[m][0] <= x) lo = m; else hi = m; }
            double f = (x - rows[lo][0]) / (rows[hi][0] - rows[lo][0]);
            return rows[lo][col] + f * (rows[hi][col] - rows[lo][col]);
        }

        /// <summary>The nine refractiveindex.info dispersion formulas (λ in um), returning n.</summary>
        public static double Formula(int kind, double[] c, double l)
        {
            double C(int i) => i < c.Length ? c[i] : 0;
            double l2 = l * l, s;
            switch (kind)
            {
                case 1: // Sellmeier: n^2 - 1 = C1 + sum C_i l^2 / (l^2 - C_{i+1}^2)
                    s = 1 + C(0);
                    for (int i = 1; i + 1 < c.Length; i += 2) s += C(i) * l2 / (l2 - C(i + 1) * C(i + 1));
                    return Math.Sqrt(s);
                case 2: // Sellmeier-2: n^2 - 1 = C1 + sum C_i l^2 / (l^2 - C_{i+1})
                    s = 1 + C(0);
                    for (int i = 1; i + 1 < c.Length; i += 2) s += C(i) * l2 / (l2 - C(i + 1));
                    return Math.Sqrt(s);
                case 3: // polynomial: n^2 = C1 + sum C_i l^C_{i+1}
                    s = C(0);
                    for (int i = 1; i + 1 < c.Length; i += 2) s += C(i) * Math.Pow(l, C(i + 1));
                    return Math.Sqrt(s);
                case 4: // RefractiveIndex.INFO
                    s = C(0);
                    if (c.Length > 1) s += C(1) * Math.Pow(l, C(2)) / (l2 - Math.Pow(C(3), C(4)));
                    if (c.Length > 5) s += C(5) * Math.Pow(l, C(6)) / (l2 - Math.Pow(C(7), C(8)));
                    for (int i = 9; i + 1 < c.Length; i += 2) s += C(i) * Math.Pow(l, C(i + 1));
                    return Math.Sqrt(s);
                case 5: // Cauchy: n = C1 + sum C_i l^C_{i+1}
                    s = C(0);
                    for (int i = 1; i + 1 < c.Length; i += 2) s += C(i) * Math.Pow(l, C(i + 1));
                    return s;
                case 6: // gases: n - 1 = C1 + sum C_i / (C_{i+1} - l^-2)
                    s = C(0);
                    for (int i = 1; i + 1 < c.Length; i += 2) s += C(i) / (C(i + 1) - 1 / l2);
                    return 1 + s;
                case 7: // Herzberger
                {
                    double L = 1 / (l2 - 0.028);
                    return C(0) + C(1) * L + C(2) * L * L + C(3) * l2 + C(4) * l2 * l2 + C(5) * l2 * l2 * l2;
                }
                case 8: // Retro: (n^2 - 1)/(n^2 + 2) = C1 + C2 l^2/(l^2 - C3) + C4 l^2
                {
                    double r = C(0) + C(1) * l2 / (l2 - C(2)) + C(3) * l2;
                    return Math.Sqrt((1 + 2 * r) / (1 - r));
                }
                case 9: // Exotic: n^2 = C1 + C2/(l^2 - C3) + C4 (l - C5)/((l - C5)^2 + C6)
                    s = C(0) + C(1) / (l2 - C(2)) + C(3) * (l - C(4)) / ((l - C(4)) * (l - C(4)) + C(5));
                    return Math.Sqrt(s);
                default: throw new FormatException("unknown refractiveindex.info formula " + kind);
            }
        }

        // ------------------------------------------------------------------------------------------ bundled subset / files

        private const string Prefix = "UltrafastEngine.Data.refractiveindex.";

        /// <summary>Names of the bundled pages ("main/SiO2/Malitson", ...).</summary>
        public static IEnumerable<string> Bundled
        {
            get
            {
                var asm = typeof(RefractiveIndexInfoPage).Assembly;
                foreach (var r in asm.GetManifestResourceNames().Where(r => r.StartsWith(Prefix) && r.EndsWith(".yml")).OrderBy(r => r))
                    yield return r.Substring(Prefix.Length, r.Length - Prefix.Length - 4).Replace('\\', '/');
            }
        }

        /// <summary>
        /// A page by name or path: an existing file path; "shelf/book/page" in the bundled subset; the same under
        /// ULTRAFAST_RII_PATH (a local clone's data folder: data/shelf/book/page.yml or the older data-nk layout).
        /// </summary>
        public static RefractiveIndexInfoPage Load(string nameOrPath, string baseDirectory = null)
        {
            string p = nameOrPath;
            if (!Path.IsPathRooted(p) && baseDirectory != null && File.Exists(Path.Combine(baseDirectory, p))) p = Path.Combine(baseDirectory, p);
            if (File.Exists(p)) return Parse(File.ReadAllText(p), Path.GetFileNameWithoutExtension(p));
            string key = nameOrPath.Trim().Trim('/').Replace('\\', '/');
            if (key.EndsWith(".yml")) key = key.Substring(0, key.Length - 4);
            var asm = typeof(RefractiveIndexInfoPage).Assembly;
            string res = Prefix + key + ".yml";
            var match = asm.GetManifestResourceNames().FirstOrDefault(r => string.Equals(r.Replace('\\', '/'), res, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                using (var s = asm.GetManifestResourceStream(match))
                using (var rd = new StreamReader(s))
                    return Parse(rd.ReadToEnd(), key);
            var userPage = Path.Combine(Commands.UserMaterialsFolder(), key + ".yml");
            if (File.Exists(userPage)) return Parse(File.ReadAllText(userPage), key);
            string root = Environment.GetEnvironmentVariable("ULTRAFAST_RII_PATH");
            if (!string.IsNullOrEmpty(root))
                foreach (var cand in new[] { Path.Combine(root, key + ".yml"), Path.Combine(root, "data", key + ".yml"), Path.Combine(root, "data-nk", key + ".yml") })
                    if (File.Exists(cand)) return Parse(File.ReadAllText(cand), key);
            throw new FileNotFoundException("refractiveindex.info page '" + nameOrPath + "' not found (a file path, a bundled page: " + string.Join(", ", Bundled) +
                ", or a page of the clone in ULTRAFAST_RII_PATH).");
        }
    }

    /// <summary>
    /// A refractiveindex.info page as an index model: {"kind": "refractiveindex", "page": "main/SiO2/Malitson"} (or "file"),
    /// optional "pressureBar" / "temperatureK" (gases: density scaling from the page's SPECS reference, default 293.15 K,
    /// 101325 Pa), "dnDT" (1/K, solids: n + dnDT (T - T_ref)), "relativeToAir" (the page's n are relative to air).
    /// </summary>
    public sealed class RefractiveIndexInfoIndex : IRefractiveIndex, IAttenuation
    {
        public RefractiveIndexInfoPage Page { get; }
        private readonly double _scale = 1, _dn;
        private readonly bool _gas;
        private readonly IRefractiveIndex _air;
        private readonly string _d;

        public RefractiveIndexInfoIndex(RefractiveIndexInfoPage page, double pressureBar = double.NaN, double temperatureK = double.NaN, double dnDT = 0, bool relativeToAir = false)
        {
            Page = page;
            _gas = page.IsGas;
            double tRef = page.Spec("temperature"); if (double.IsNaN(tRef)) tRef = 293.15;
            double pRef = page.Spec("pressure"); if (double.IsNaN(pRef)) pRef = 101325;
            var notes = new List<string>();
            if (_gas && (!double.IsNaN(pressureBar) || !double.IsNaN(temperatureK)))
            {
                double p = double.IsNaN(pressureBar) ? pRef : pressureBar * 1e5, t = double.IsNaN(temperatureK) ? tRef : temperatureK;
                _scale = p / pRef * tRef / t;
                notes.Add(string.Format(CultureInfo.InvariantCulture, "density x{0:G4} ({1:G4} bar, {2:G4} K)", _scale, p * 1e-5, t));
            }
            else if (!_gas && !double.IsNaN(temperatureK) && dnDT != 0) { _dn = dnDT * (temperatureK - tRef); notes.Add(string.Format(CultureInfo.InvariantCulture, "dn = {0:G3} at {1:G4} K", _dn, temperatureK)); }
            if (relativeToAir) { _air = new CiddorAirIndex(); notes.Add("relative to air"); }
            var r = page.Range;
            _d = "refractiveindex.info " + (page.Name ?? "page") + (r != null ? string.Format(CultureInfo.InvariantCulture, " ({0:G4}-{1:G4} um)", r[0], r[1]) : "") + (notes.Count > 0 ? ", " + string.Join(", ", notes) : "");
        }

        public string Description => _d;

        public void Evaluate(double[] omega, double[] nOut)
        {
            double[] na = null;
            if (_air != null) { na = new double[omega.Length]; _air.Evaluate(omega, na); }
            for (int i = 0; i < omega.Length; i++)
            {
                if (omega[i] <= 0) { nOut[i] = double.NaN; continue; }
                double l = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / omega[i] * 1e6;
                double n = Page.IndexAt(l);
                n = 1 + (n - 1) * _scale + _dn;
                if (na != null) n *= na[i];
                nOut[i] = n;
            }
        }

        /// <summary>alpha = 4 pi k / lambda (1/m), from the page's k data.</summary>
        void IAttenuation.Evaluate(double[] omega, double[] alpha)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double l = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / omega[i];
                alpha[i] = 4 * Math.PI * Page.KAt(l * 1e6) * _scale / l;
            }
        }
    }
}
