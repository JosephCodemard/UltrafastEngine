using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Plugins.Hitran
{
    /// <summary>The lines of one absorbing species and its state in the air.</summary>
    public sealed class GasSpecies
    {
        public string Name;
        public List<HitranLine> Lines;
        /// <summary>Mole fraction of the species in the air (the line strengths already contain the isotopologue abundances).</summary>
        public double MoleFraction;
        /// <summary>Q(T)/Q(Tref) = (T/Tref)^exponent: 1.5 for non-linear rotors (H2O), 1 for linear molecules (CO2, O2).
        /// An approximation (the vibrational part is neglected): good to about 1 % within +-30 K of 296 K.</summary>
        public double PartitionExponent;
        /// <summary>Partition sums Q(T) per local isotopologue number (TIPS, written by the downloader), or null: then the power law above is used.</summary>
        public Dictionary<int, PartitionTable> Partition;

        /// <summary>Q(296 K) / Q(T) for an isotopologue: from the table when it has one, else (296/T)^exponent.</summary>
        public double QRatio(int isotopologue, double temperatureK)
        {
            if (Partition != null && Partition.TryGetValue(isotopologue, out var t)) return t.At(296.0) / t.At(temperatureK);
            return Math.Pow(296.0 / temperatureK, PartitionExponent);
        }
    }

    /// <summary>Q(T) of one isotopologue on a temperature grid (linear interpolation; outside the grid an error: extrapolating a partition sum is not safe).</summary>
    public sealed class PartitionTable
    {
        public readonly double[] T, Q;
        public PartitionTable(double[] t, double[] q)
        {
            if (t.Length < 2 || t.Length != q.Length) throw new JsonException("a partition table needs at least two (T, Q) rows.");
            var o = Enumerable.Range(0, t.Length).OrderBy(i => t[i]).ToArray();
            T = o.Select(i => t[i]).ToArray(); Q = o.Select(i => q[i]).ToArray();
        }
        public double At(double temperatureK)
        {
            if (temperatureK < T[0] || temperatureK > T[T.Length - 1]) throw new JsonException("partition table covers " + T[0] + " .. " + T[T.Length - 1] + " K; " + temperatureK + " K is outside.");
            int lo = 0, hi = T.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (T[mid] <= temperatureK) lo = mid; else hi = mid; }
            double f = (temperatureK - T[lo]) / (T[hi] - T[lo]);
            return Q[lo] + f * (Q[hi] - Q[lo]);
        }

        /// <summary>CSV with the header <c>iso,T,Q</c> (local isotopologue number, K, partition sum): all isotopologues of a molecule.</summary>
        public static Dictionary<int, PartitionTable> Read(string path)
        {
            if (!File.Exists(path)) throw new JsonException("partition file not found: " + path);
            var rows = new Dictionary<int, List<(double T, double Q)>>();
            bool header = true;
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                var f = line.Split(',');
                if (header) { header = false; if (!(f.Length >= 3 && f[0].Trim().ToLowerInvariant() == "iso")) throw new JsonException(path + ": the first row must be the header iso,T,Q."); continue; }
                int iso = int.Parse(f[0].Trim(), CultureInfo.InvariantCulture);
                if (!rows.TryGetValue(iso, out var l)) rows[iso] = l = new List<(double, double)>();
                l.Add((double.Parse(f[1], CultureInfo.InvariantCulture), double.Parse(f[2], CultureInfo.InvariantCulture)));
            }
            return rows.ToDictionary(kv => kv.Key, kv => new PartitionTable(kv.Value.Select(r => r.T).ToArray(), kv.Value.Select(r => r.Q).ToArray()));
        }
    }

    /// <summary>
    /// Line-by-line gas absorption and dispersion: Voigt lines (Doppler + pressure broadening) from HITRAN data, giving the
    /// complex refractive index of the gas. One complex line shape F = w(z)/(gamma_D sqrt(pi)), z = (nu - nu0 + i gamma_L)/gamma_D,
    /// gives both parts, so n(omega) and alpha(omega) are Kramers-Kronig consistent by construction:
    /// <code>
    /// alpha = n_d S Re F                       (intensity, 1/cm)
    /// Dn    = -n_d S Im F / (4 pi nu)          (nu in cm-1)    kappa = n_d S Re F / (4 pi nu)
    /// </code>
    /// (exp(-i omega t) convention: n > 1 below a resonance). Line strength and width are scaled to the temperature
    /// (Boltzmann factor, stimulated emission, n_air), the centre shifted by delta_air p, lines cut off at +-cutoff. No
    /// continuum absorption. The model is the air: n = Ciddor + Dn (or 1 + Dn with baseIndex "vacuum"), alpha from the lines.
    /// </summary>
    public sealed class HitranAirIndex : IRefractiveIndex, IAttenuation, ISmoothIndex
    {
        private const double KB = 1.380649e-23, CLight = 299792458.0, Amu = 1.66053906660e-27, C2 = 1.4387769, TRef = 296.0;

        private struct Prepared { public double Center, GammaL, GammaD, Amp; }

        private readonly IRefractiveIndex _base;
        private readonly List<Prepared> _lines = new List<Prepared>();
        private readonly string _desc;
        private readonly Dictionary<(int, double, double), (double[] Dn, double[] Alpha)> _cache = new Dictionary<(int, double, double), (double[], double[])>();
        private readonly Queue<(int, double, double)> _order = new Queue<(int, double, double)>();
        private readonly object _lock = new object();

        public double TemperatureK { get; }
        public double PressurePa { get; }
        public double CutoffCm1 { get; }
        public int LineCount => _lines.Count;
        public string Description => _desc;

        /// <summary>The index without the lines (Ciddor, or 1): what the frame of a propagation step is referenced to.</summary>
        public IRefractiveIndex Smooth => _base ?? new VacuumIndex();

        private sealed class VacuumIndex : IRefractiveIndex
        {
            public string Description => "vacuum";
            public void Evaluate(double[] omega, double[] nOut) { for (int i = 0; i < omega.Length; i++) nOut[i] = 1.0; }
        }

        public HitranAirIndex(IRefractiveIndex baseIndex, double temperatureK, double pressurePa, IEnumerable<GasSpecies> species, double cutoffCm1 = 25.0, double lineScale = 1.0)
        {
            _base = baseIndex; TemperatureK = temperatureK; PressurePa = pressurePa; CutoffCm1 = cutoffCm1;
            double pAtm = pressurePa / 101325.0;
            double nTotalPerCm3 = pressurePa / (KB * temperatureK) * 1e-6;
            var names = new List<string>();
            foreach (var sp in species)
            {
                double pSelf = sp.MoleFraction * pAtm, nd = sp.MoleFraction * nTotalPerCm3;
                int count = 0;
                foreach (var l in sp.Lines)
                {
                    double nu = l.NuCm1;
                    double boltz = Math.Exp(-C2 * l.ElowerCm1 * (1.0 / temperatureK - 1.0 / TRef));
                    double stim = (1.0 - Math.Exp(-C2 * nu / temperatureK)) / (1.0 - Math.Exp(-C2 * nu / TRef));
                    double s = l.S * sp.QRatio(l.Isotopologue, temperatureK) * boltz * stim * lineScale;
                    double gL = Math.Pow(TRef / temperatureK, l.NAir) * (l.GammaAir * (pAtm - pSelf) + l.GammaSelf * pSelf);
                    double center = nu + l.DeltaAir * pAtm;
                    double gD = center * Math.Sqrt(2.0 * KB * temperatureK * Math.Log(2.0) / (Mass(sp.Name, l) * Amu * CLight * CLight));
                    if (!(s > 0) || !(gL > 0) || !(gD > 0)) continue;
                    _lines.Add(new Prepared { Center = center, GammaL = gL, GammaD = gD, Amp = nd * s });
                    count++;
                }
                names.Add(sp.Name + " " + count + " lines, x = " + sp.MoleFraction.ToString("G3", CultureInfo.InvariantCulture) + (sp.Partition != null ? ", TIPS partition sums" : ", Q ~ T^" + sp.PartitionExponent.ToString("G2", CultureInfo.InvariantCulture)));
            }
            _lines.Sort((a, b) => a.Center.CompareTo(b.Center));
            _desc = string.Format(CultureInfo.InvariantCulture, "hitran air ({0:G4} K, {1:G6} Pa; {2}; Voigt, cutoff {3:G3} cm-1)", temperatureK, pressurePa, string.Join(", ", names), cutoffCm1);
        }

        /// <summary>Doppler half width at half maximum (cm-1) of a line of wavenumber nu (cm-1) for molar mass M (g/mol) at T (K).</summary>
        public static double DopplerHwhmCm1(double nuCm1, double temperatureK, double molarMassGPerMol)
            => nuCm1 * Math.Sqrt(2.0 * KB * temperatureK * Math.Log(2.0) / (molarMassGPerMol * 1e-3 / 6.02214076e23 * CLight * CLight));

        // atomic masses (u)
        private const double H = 1.00782503, D = 2.01410178, C12 = 12.0, C13 = 13.00335484, O16 = 15.99491462, O17 = 16.9991317, O18 = 17.99915961;

        /// <summary>Mass (u) of the isotopologue (HITRAN local isotopologue numbering for H2O, CO2 and O2); the main isotopologue's mass otherwise.</summary>
        public static double Mass(string species, HitranLine l)
        {
            switch (species.ToUpperInvariant())
            {
                case "H2O":
                    switch (l.Isotopologue) { case 2: return 2 * H + O18; case 3: return 2 * H + O17; case 4: return H + D + O16; case 5: return H + D + O18; case 6: return H + D + O17; case 7: return 2 * D + O16; default: return 2 * H + O16; }
                case "CO2":
                    switch (l.Isotopologue)
                    {
                        case 2: return C13 + 2 * O16; case 3: return C12 + O16 + O18; case 4: return C12 + O16 + O17; case 5: return C13 + O16 + O18;
                        case 6: return C13 + O16 + O17; case 7: return C12 + 2 * O18; case 8: return C12 + O17 + O18; case 9: return C12 + 2 * O17;
                        case 10: return C13 + 2 * O18; case 11: return C13 + O17 + O18; default: return C12 + 2 * O16;
                    }
                case "O2":
                    switch (l.Isotopologue) { case 2: return O16 + O18; case 3: return O16 + O17; default: return 2 * O16; }
                default: throw new JsonException("hitran: species " + species + " is not supported (H2O, CO2, O2).");
            }
        }

        /// <summary>Refractivity change Dn and intensity absorption coefficient (1/m) of the lines on an angular-frequency axis.</summary>
        public (double[] Dn, double[] AlphaPerM) Compute(double[] omega)
        {
            int n = omega.Length;
            if (n == 0) return (new double[0], new double[0]);
            var key = (n, omega[0], omega[n - 1]);
            lock (_lock) { if (_cache.TryGetValue(key, out var hit)) return hit; }
            var dn = new double[n]; var alpha = new double[n];
            bool ascending = omega[n - 1] > omega[0];
            var nu = new double[n];
            double k = 1.0 / (2.0 * Math.PI * CLight * 100.0);
            for (int i = 0; i < n; i++) nu[i] = omega[i] * k;
            foreach (var l in _lines)
            {
                int i0, i1;
                if (ascending) { i0 = LowerBound(nu, l.Center - CutoffCm1); i1 = LowerBound(nu, l.Center + CutoffCm1); }
                else { i0 = 0; i1 = n; }
                double invGd = 1.0 / l.GammaD, pref = l.Amp * invGd / Math.Sqrt(Math.PI);
                for (int i = i0; i < i1; i++)
                {
                    double d = nu[i] - l.Center;
                    if (Math.Abs(d) > CutoffCm1 || nu[i] <= 0) continue;
                    var q = pref * Faddeeva.W(new Complex(d * invGd, l.GammaL * invGd));      // n_d S F, 1/cm
                    alpha[i] += q.Real;
                    dn[i] += -q.Imaginary / (4.0 * Math.PI * nu[i]);
                }
            }
            for (int i = 0; i < n; i++) alpha[i] *= 100.0;
            var res = (dn, alpha);
            lock (_lock)
            {
                _cache[key] = res; _order.Enqueue(key);
                while (_order.Count > 6) _cache.Remove(_order.Dequeue());
            }
            return res;
        }

        private static int LowerBound(double[] a, double v)
        {
            int lo = 0, hi = a.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (a[mid] < v) lo = mid + 1; else hi = mid; }
            return lo;
        }

        void IRefractiveIndex.Evaluate(double[] omega, double[] nOut)
        {
            var dn = Compute(omega).Dn;
            if (_base != null) _base.Evaluate(omega, nOut); else for (int i = 0; i < omega.Length; i++) nOut[i] = 1.0;
            for (int i = 0; i < omega.Length; i++) nOut[i] += dn[i];
        }

        void IAttenuation.Evaluate(double[] omega, double[] alphaPerMOut)
        {
            var a = Compute(omega).AlphaPerM;
            Array.Copy(a, alphaPerMOut, omega.Length);
        }

        // ------------------------------------------------------------------------------------------------ from a model spec

        private static readonly Dictionary<string, int> MoleculeId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "H2O", 1 }, { "CO2", 2 }, { "O2", 7 } };

        /// <summary>
        /// <c>{"kind": "hitran", "temperatureC": 20, "pressurePa": 101325, "humidity": 0.5, "co2ppm": 420,
        /// "species": {"H2O": "H2O.par", "CO2": {"file": "CO2.csv", "moleFraction": 4.2e-4}, "O2": true},
        /// "rangeCm1": [6500, 14000], "cutoffCm1": 25, "baseIndex": "ciddor" | "vacuum", "lineScale": 1}</c>.
        /// A species given as true (or without "file") reads &lt;name&gt;.par / &lt;name&gt;.csv from <c>dataDir</c>, the environment
        /// variable ULTRAFAST_HITRAN_DIR or ~/.ultrafast/hitran.
        /// </summary>
        public static HitranAirIndex FromSpec(ModelSpec s)
        {
            var j = s.Json;
            double tC = s.GetDouble("temperatureC", 20), p = s.GetDouble("pressurePa", 101325), rh = s.GetDouble("humidity", 0), co2 = s.GetDouble("co2ppm", 450);
            var ciddor = new CiddorAirIndex(tC, p, rh, co2);
            double xw = ciddor.WaterVaporMoleFraction;
            var range = j.GetDoubleArray("rangeCm1") ?? new[] { 6500.0, 14000.0 };
            if (range.Length != 2 || !(range[1] > range[0])) throw new JsonException(s.Origin + ": rangeCm1 is [min, max] in cm-1.");
            var sp = j.GetObject("species") ?? new JsonObject().Set("H2O", true);
            var list = new List<GasSpecies>();
            foreach (var kv in sp)
            {
                string name = kv.Key;
                if (!MoleculeId.TryGetValue(name, out int mol)) throw new JsonException(s.Origin + ": species " + name + " is not supported (H2O, CO2, O2).");
                string file = null, partFile = null; double x = double.NaN, qexp = double.NaN;
                if (kv.Value.Kind == JsonKind.String) file = kv.Value.AsString();
                else if (kv.Value.Kind == JsonKind.Object)
                {
                    var o = kv.Value.AsObject();
                    file = o.GetString("file"); x = o.GetDouble("moleFraction", double.NaN); qexp = o.GetDouble("partitionExponent", double.NaN); partFile = o.GetString("partitionFile");
                }
                else if (kv.Value.Kind == JsonKind.Bool && !kv.Value.AsBool()) continue;
                string path = Locate(file, name, j.GetString("dataDir"), s.Origin);
                var lines = HitranFile.Read(path, range[0], range[1]).Where(l => l.Molecule == 0 || l.Molecule == mol).ToList();
                if (lines.Count == 0) throw new JsonException(s.Origin + ": " + path + " has no " + name + " lines between " + range[0] + " and " + range[1] + " cm-1.");
                if (double.IsNaN(x)) x = name.ToUpperInvariant() == "H2O" ? xw : name.ToUpperInvariant() == "CO2" ? co2 * 1e-6 : 0.2095 * (1 - xw);
                // partition sums: an explicit partitionFile, or <name>.partition.csv next to the line file (written by the downloader)
                Dictionary<int, PartitionTable> part = null;
                if (partFile == null) { var guess = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", name.ToUpperInvariant() + ".partition.csv"); if (File.Exists(guess) && double.IsNaN(qexp)) partFile = guess; }
                if (partFile != null) part = PartitionTable.Read(Locate(partFile, name, j.GetString("dataDir"), s.Origin));
                if (double.IsNaN(qexp)) qexp = name.ToUpperInvariant() == "H2O" ? 1.5 : 1.0;
                list.Add(new GasSpecies { Name = name.ToUpperInvariant(), Lines = lines, MoleFraction = x, PartitionExponent = qexp, Partition = part });
            }
            var baseIdx = string.Equals(j.GetString("baseIndex", "ciddor"), "vacuum", StringComparison.OrdinalIgnoreCase) ? null : (IRefractiveIndex)ciddor;
            return new HitranAirIndex(baseIdx, tC + 273.15, p, list, j.GetDouble("cutoffCm1", 25.0), j.GetDouble("lineScale", 1.0));
        }

        private static string Locate(string file, string species, string dataDir, string origin)
        {
            var dirs = new List<string>();
            if (!string.IsNullOrEmpty(dataDir)) dirs.Add(dataDir);
            var env = Environment.GetEnvironmentVariable("ULTRAFAST_HITRAN_DIR"); if (!string.IsNullOrEmpty(env)) dirs.Add(env);
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ultrafast", "hitran"));
            var names = file != null ? new[] { file } : new[] { species + ".par", species + ".csv" };
            foreach (var n in names)
            {
                if (File.Exists(n)) return n;
                foreach (var d in dirs) { var f = Path.Combine(d, n); if (File.Exists(f)) return f; }
            }
            throw new JsonException(origin + ": no HITRAN line file for " + species + " (looked for " + string.Join(", ", names) + " in the working directory and " + string.Join(", ", dirs) +
                "). Download the data first: python -m ultrafast_hitran download H2O (docs/plugins/hitran.md).");
        }
    }
}
