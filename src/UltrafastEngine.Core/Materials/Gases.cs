using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;

namespace UltrafastEngine.Materials
{
    /// <summary>
    /// Data of the built-in gases (docs/reference/gases.md). Values are literature values good to ~10-30 %
    /// (electronic n2, polarisability anisotropy, dephasing times); every one can be overridden in a material
    /// definition. References: rotational constants (NIST / Herzberg); anisotropies Bridge &amp; Buckingham, Proc. R. Soc.
    /// A 295, 334 (1966); electronic n2 Wahlstrand, Cheng &amp; Milchberg, PRA 85, 043820 (2012); ionisation potentials NIST.
    /// </summary>
    public sealed class GasData
    {
        public string Species;
        /// <summary>Ionisation potential (eV).</summary>
        public double IonizationEv;
        /// <summary>Orbital angular momentum l of the ionised electron (PPT/ADK), and an effective charge (molecules).</summary>
        public int L; public double ZEff = 1.0;
        /// <summary>Electronic (instantaneous) n2 at 1 bar, 273 K (m^2/W).</summary>
        public double N2Electronic;
        /// <summary>Rigid-rotor data of a linear molecule: B, D (1/cm), polarisability anisotropy (volume, Angstrom^3),
        /// nuclear-spin weights of even / odd J, and the rotational coherence (dephasing) time at 1 bar, 293 K (s).</summary>
        public double RotB, RotD, AnisotropyA3, GEven, GOdd, T2At1Bar;
        public bool Molecular => RotB > 0;

        public static readonly Dictionary<string, GasData> Table = new Dictionary<string, GasData>(StringComparer.OrdinalIgnoreCase)
        {
            { "He", new GasData { Species = "He", IonizationEv = 24.587, L = 0, N2Electronic = 3.5e-25 } },
            { "Ne", new GasData { Species = "Ne", IonizationEv = 21.565, L = 1, N2Electronic = 7.5e-25 } },
            { "Ar", new GasData { Species = "Ar", IonizationEv = 15.760, L = 1, N2Electronic = 1.0e-23 } },
            { "Kr", new GasData { Species = "Kr", IonizationEv = 13.999, L = 1, N2Electronic = 2.8e-23 } },
            { "Xe", new GasData { Species = "Xe", IonizationEv = 12.130, L = 1, N2Electronic = 8.0e-23 } },
            { "N2", new GasData { Species = "N2", IonizationEv = 15.581, L = 0, ZEff = 0.9, N2Electronic = 8.0e-24, RotB = 1.98957, RotD = 5.76e-6, AnisotropyA3 = 0.70, GEven = 6, GOdd = 3, T2At1Bar = 100e-12 } },
            { "O2", new GasData { Species = "O2", IonizationEv = 12.070, L = 1, ZEff = 0.53, N2Electronic = 1.03e-23, RotB = 1.43768, RotD = 4.85e-6, AnisotropyA3 = 1.14, GEven = 0, GOdd = 1, T2At1Bar = 80e-12 } },
            { "CO2", new GasData { Species = "CO2", IonizationEv = 13.777, L = 1, ZEff = 1.0, N2Electronic = 1.4e-23, RotB = 0.39021, RotD = 1.33e-7, AnisotropyA3 = 2.10, GEven = 1, GOdd = 0, T2At1Bar = 50e-12 } },
            { "H2", new GasData { Species = "H2", IonizationEv = 15.426, L = 0, ZEff = 1.0, N2Electronic = 6.9e-24, RotB = 59.322, RotD = 0.0471, AnisotropyA3 = 0.30, GEven = 1, GOdd = 3, T2At1Bar = 300e-12 } },
        };

        /// <summary>Mole fractions of dry air used for its nonlinear and ionisation responses.</summary>
        public static readonly (string Species, double Fraction)[] AirComposition = { ("N2", 0.7808), ("O2", 0.2095), ("Ar", 0.0093) };

        public static GasData Get(string species) => species != null && Table.TryGetValue(species, out var g) ? g : null;
    }

    /// <summary>
    /// Rotational Raman (molecular orientation) response of a linear molecule, from the rigid-rotor density matrix in
    /// first order (Kubo): for a pump polarised along e,
    /// <code>Δ⟨cos²θ⟩(t) = ∫ χ(t - t') F(t') dt',  F = Δα I / (2 n0 c ε0),
    /// χ(τ) = (4 / 15ħ) Σ_J (J+1)(J+2)/(2J+3) (p_J - p_{J+2}) sin(ω_J τ) exp(-τ / T2),  ω_J = (E_{J+2} - E_J)/ħ</code>
    /// with p_J the thermal population of one (J, M) state (nuclear-spin weights included), E_J = hc (B J(J+1) - D J²(J+1)²).
    /// The index change along the pump is Δn_∥ = N Δα Δ⟨cos²θ⟩ / (2 n0 ε0), so
    /// <code>R(τ) = N Δα² χ(τ) / (4 n0² c ε0²)</code> and perpendicular light sees -1/2 of it. Its time integral is
    /// the adiabatic orientational n2 (for an isolated molecule 3/4 of the thermal-equilibrium value, approached at room
    /// temperature: ∫χ → 1 / (15 kT)). Mixtures add their kernels.
    /// </summary>
    public sealed class RotationalRamanResponse : IAnisotropicResponse
    {
        private const double Hbar = 1.054571817e-34, Kb = 1.380649e-23, Eps0 = 8.8541878128e-12, Hc = 6.62607015e-34 * 2.99792458e10; // J cm
        private readonly List<(double Omega, double Weight, double Gamma)> _lines = new List<(double, double, double)>();  // R(t) = sum W sin(w t) exp(-g t)
        private readonly string _d;
        private readonly object _lock = new object();
        private int _cacheN; private double _cacheDt; private Complex[] _cacheK;

        public string Description => _d;
        public double AdiabaticN2 => _lines.Sum(l => l.Weight * l.Omega / (l.Omega * l.Omega + l.Gamma * l.Gamma));
        public IReadOnlyList<(double Omega, double Weight, double Gamma)> Lines => _lines;

        private RotationalRamanResponse(string d) { _d = d; }

        /// <summary>Response of a gas (or mixture: species with mole fractions) at pressure / temperature.</summary>
        public static RotationalRamanResponse For(IEnumerable<(string Species, double Fraction)> mixture, double pressureBar, double temperatureK, double n0 = 1.0, double t2Override = double.NaN)
        {
            var parts = new List<string>();
            var r = new RotationalRamanResponse(null);
            double ntot = pressureBar * 1e5 / (Kb * temperatureK);
            foreach (var (sp, frac) in mixture)
            {
                var g = GasData.Get(sp);
                if (g == null || !g.Molecular || frac <= 0) continue;
                double N = ntot * frac;
                double da = 4 * Math.PI * Eps0 * g.AnisotropyA3 * 1e-30;
                double pref = N * da * da / (4 * n0 * n0 * 2.99792458e8 * Eps0 * Eps0) * 4 / (15 * Hbar);
                double t2 = double.IsNaN(t2Override) ? g.T2At1Bar / Math.Max(pressureBar, 1e-6) * Math.Sqrt(temperatureK / 293.15) : t2Override;
                double gam = 1 / t2;
                // populations
                Func<int, double> E = J => Hc * (g.RotB * J * (J + 1) - g.RotD * J * J * (J + 1) * (J + 1));
                Func<int, double> gw = J => J % 2 == 0 ? g.GEven : g.GOdd;
                int jmax = 0; double z = 0;
                for (int J = 0; J < 400; J++)
                {
                    double b = gw(J) * Math.Exp(-E(J) / (Kb * temperatureK));
                    z += (2 * J + 1) * b;
                    if (J > 5 && E(J) / (Kb * temperatureK) > 30) { jmax = J; break; }
                    jmax = J;
                }
                for (int J = 0; J + 2 <= jmax; J++)
                {
                    double pj = gw(J) * Math.Exp(-E(J) / (Kb * temperatureK)) / z, pj2 = gw(J + 2) * Math.Exp(-E(J + 2) / (Kb * temperatureK)) / z;
                    double w = (pj - pj2) * (J + 1.0) * (J + 2.0) / (2 * J + 3.0);
                    if (w == 0) continue;
                    double om = (E(J + 2) - E(J)) / Hbar;
                    r._lines.Add((om, pref * w, gam));
                }
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} x{1:G3} (B = {2:G5} /cm, d_alpha = {3:G3} A^3, T2 = {4:G3} ps)", sp, frac, g.RotB, g.AnisotropyA3, t2 * 1e12));
            }
            if (r._lines.Count == 0) return null;
            var res = new RotationalRamanResponse("rotational Raman: " + string.Join(", ", parts) + string.Format(CultureInfo.InvariantCulture, " at {0:G4} bar, {1:G4} K", pressureBar, temperatureK));
            res._lines.AddRange(r._lines);
            return res;
        }

        /// <summary>R(t) (m^2/W/s).</summary>
        public double KernelAt(double t) => t < 0 ? 0 : _lines.Sum(l => l.Weight * Math.Sin(l.Omega * t) * Math.Exp(-l.Gamma * t));

        /// <summary>Exact integral of R over [a, b] (bin-integrated kernel: no aliasing of fast lines on coarse grids).</summary>
        private double Integral(double a, double b)
        {
            double s = 0;
            foreach (var (w, W, g) in _lines)
            {
                // ∫ e^{-g t} sin(w t) dt = -e^{-g t} (g sin(w t) + w cos(w t)) / (g^2 + w^2)
                double d = g * g + w * w;
                double Fa = -Math.Exp(-g * a) * (g * Math.Sin(w * a) + w * Math.Cos(w * a)) / d;
                double Fb = -Math.Exp(-g * b) * (g * Math.Sin(w * b) + w * Math.Cos(w * b)) / d;
                s += W * (Fb - Fa);
            }
            return s;
        }

        public double[] Convolve(double[] x, double dt)
        {
            int n = x.Length, m = 1; while (m < 2 * n) m <<= 1;
            Complex[] K;
            lock (_lock)
            {
                if (_cacheK == null || _cacheN != n || _cacheDt != dt)
                {
                    var k = new Complex[m];
                    // sample j holds the response to an impulse in bin 0 (trapezoid-consistent: half bin at t = 0)
                    k[0] = Integral(0, 0.5 * dt);
                    for (int j = 1; j < n; j++) k[j] = Integral((j - 0.5) * dt, (j + 0.5) * dt);
                    ComplexFft.TransformInPlace(k, -1, false);
                    _cacheK = k; _cacheN = n; _cacheDt = dt;
                }
                K = _cacheK;
            }
            var a = new Complex[m];
            for (int i = 0; i < n; i++) a[i] = x[i];
            ComplexFft.TransformInPlace(a, -1, false);
            for (int i = 0; i < m; i++) a[i] *= K[i];
            ComplexFft.TransformInPlace(a, +1, true);
            var y = new double[n];
            for (int i = 0; i < n; i++) y[i] = a[i].Real;
            return y;
        }
    }

    /// <summary>
    /// Refractive index of the gases without a Borzsonyi fit (dispersion formulas in n - 1 = Σ B_i / (C_i - σ²), σ in
    /// 1/um, at reference conditions), scaled with density p T_ref / (p_ref T).
    /// O2: Zhang et al., Appl. Opt. 47, 3143 (2008), 293.15 K; CO2: Bideau-Mehu et al., Opt. Commun. 9, 432 (1973),
    /// 273.15 K; H2: Peck &amp; Huang, JOSA 67, 1550 (1977), 273.15 K; all at 101325 Pa.
    /// </summary>
    public sealed class GasFormulaIndex : Models.IRefractiveIndex
    {
        private static readonly Dictionary<string, (double C0, double[] B, double[] C, double Tref)> Forms = new Dictionary<string, (double, double[], double[], double)>(StringComparer.OrdinalIgnoreCase)
        {
            { "O2", (1.181494e-4, new[] { 9.708931e-3 }, new[] { 75.4 }, 293.15) },
            { "CO2", (0, new[] { 6.99100e-2, 1.44720e-3, 6.42941e-5, 5.21306e-5, 1.46847e-6 }, new[] { 166.175, 79.609, 56.3064, 46.0196, 0.0584738 }, 273.15) },
            { "H2", (0, new[] { 0.0148956, 0.0049037 }, new[] { 180.7, 92.0 }, 273.15) },
        };
        public static bool Knows(string species) => Forms.ContainsKey(species);
        public static IEnumerable<string> Species => Forms.Keys;

        public string Name { get; }
        private readonly (double C0, double[] B, double[] C, double Tref) _f;
        private readonly double _scale;
        public double DensityFactor { get; }

        public GasFormulaIndex(string species, double pressureBar, double temperatureK)
        {
            if (!Forms.TryGetValue(species, out _f)) throw new ArgumentException("no dispersion formula for gas '" + species + "'");
            Name = species;
            _scale = pressureBar / 1.01325 * _f.Tref / temperatureK;
            DensityFactor = pressureBar * 273.0 / temperatureK;
        }

        public string Description => "gas " + Name + " (dispersion formula, density-scaled)";

        public void Evaluate(double[] omega, double[] nOut)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double l = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / omega[i] * 1e6, s2 = 1 / (l * l);
                double v = _f.C0;
                for (int k = 0; k < _f.B.Length; k++) v += _f.B[k] / (_f.C[k] - s2);
                nOut[i] = 1 + _scale * v;
            }
        }
    }
}
