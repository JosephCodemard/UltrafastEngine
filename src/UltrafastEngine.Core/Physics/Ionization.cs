using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Physics
{
    /// <summary>A photo-ionisation rate: probability per neutral per second at a cycle-averaged intensity (W/m^2), or —
    /// in carrier-resolved mode — at an instantaneous field (V/m).</summary>
    public interface IIonizationRate
    {
        string Description { get; }
        /// <summary>Cycle-averaged rate (1/s per neutral) at intensity I (W/m^2) in a medium of index n0.</summary>
        double Rate(double intensityWPerM2);
        /// <summary>Quasi-static rate at an instantaneous field |E| (V/m); NaN if the model has no static limit.</summary>
        double StaticRate(double fieldVPerM);
        /// <summary>Energy taken from the field per ionisation event (J): the ionisation potential / band gap.</summary>
        double EnergyJ { get; }
    }

    /// <summary>Special functions used by the ionisation models.</summary>
    public static class IonizationMath
    {
        /// <summary>Dawson's integral D(x) = exp(-x^2) ∫0^x exp(t^2) dt (Rybicki's method, Numerical Recipes 6.10).</summary>
        public static double Dawson(double x)
        {
            const double H = 0.4, A1 = 2.0 / 3.0, A2 = 0.4, A3 = 2.0 / 7.0;
            const int NMAX = 6;
            double xx = Math.Abs(x);
            if (xx < 0.2) { double x2 = x * x; return x * (1 - A1 * x2 * (1 - A2 * x2 * (1 - A3 * x2))); }
            var c = new double[NMAX];
            for (int i = 0; i < NMAX; i++) { double t = (2 * i + 1) * H; c[i] = Math.Exp(-t * t); }
            int n0 = 2 * (int)(0.5 * xx / H + 0.5);
            double xp = xx - n0 * H, e1 = Math.Exp(2 * xp * H), e2 = e1 * e1, d1 = n0 + 1, d2 = d1 - 2, sum = 0;
            for (int i = 0; i < NMAX; i++, d1 += 2, d2 -= 2, e1 *= e2) sum += c[i] * (e1 / d1 + 1 / (d2 * e1));
            return 0.5641895835 * Math.Sign(x) * Math.Exp(-xp * xp) * sum;
        }

        /// <summary>Complete elliptic integrals K(m), E(m) with parameter m = k^2 (arithmetic-geometric mean).</summary>
        public static (double K, double E) Elliptic(double m)
        {
            if (m >= 1) return (double.PositiveInfinity, 1.0);
            double a = 1, b = Math.Sqrt(1 - m), c = Math.Sqrt(m), sum = 0.5 * c * c, pow = 0.5;
            for (int i = 0; i < 60 && Math.Abs(c) > 1e-16; i++)
            {
                double an = 0.5 * (a + b), bn = Math.Sqrt(a * b);
                c = 0.5 * (a - b); pow *= 2; sum += pow * c * c;
                a = an; b = bn;
            }
            double K = Math.PI / (2 * a);
            return (K, K * (1 - sum));
        }

        public static double LogGamma(double x)
        {
            // Lanczos approximation
            double[] g = { 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
            if (x < 0.5) return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1 - x);
            x -= 1; double a = 0.99999999999980993; double t = x + 7.5;
            for (int i = 0; i < 8; i++) a += g[i] / (x + i + 1);
            return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
        }
    }

    internal static class Au
    {
        public const double Field = 5.14220674763e11;      // V/m
        public const double Energy = 4.3597447222071e-18;  // J (Hartree)
        public const double Time = 2.4188843265857e-17;    // s
        public const double Ev = 1.602176634e-19;
        public const double Eps0 = 8.8541878128e-12, Me = 9.1093837015e-31, Hbar = 1.054571817e-34, E = 1.602176634e-19;
        public static double FieldFromIntensity(double i, double n0) => Math.Sqrt(2 * Math.Max(i, 0) / (n0 * PhysicalConstants.SpeedOfLightMPerS * Eps0));
    }

    /// <summary>Multiphoton ionisation W = sigma_K I^K (cross section sigma_K in (m^2/W)^K / s).</summary>
    public sealed class MpiRate : IIonizationRate
    {
        public double SigmaK { get; } public int K { get; } public double EnergyJ { get; }
        public MpiRate(double sigmaK, int k, double ionizationEv) { SigmaK = sigmaK; K = k; EnergyJ = ionizationEv * Au.Ev; }
        public string Description => string.Format(CultureInfo.InvariantCulture, "MPI sigma_{0} = {1:G3}", K, SigmaK);
        public double Rate(double i) => SigmaK * Math.Pow(Math.Max(i, 0), K);
        public double StaticRate(double f) => double.NaN;
    }

    /// <summary>
    /// PPT (Perelomov-Popov-Terent'ev) cycle-averaged rate for an atom (or molecule with an effective charge) in a
    /// linearly polarised field, with the Ammosov-Delone-Krainov coefficient C_{n*l*} (m = 0):
    /// W = sqrt(6/π) C² f(l) Ip (2κ³/F)^{2n*-3/2} (1+γ²)^{3/4} A_0(ω, γ) exp(-(2κ³/3F) g(γ)), atomic units, κ = sqrt(2 Ip),
    /// n* = Z/κ, γ = ωκ/F the Keldysh parameter. Covers multiphoton (γ ≫ 1, W ∝ I^K) and tunnel (γ ≪ 1, → ADK) regimes.
    /// The Coulomb factor carries Popov's correction (1 + 2γ/e)^{-2n*}, so the multiphoton limit scales as I^K.
    /// The ADK rate (cycle-averaged and quasi-static) is the γ → 0 limit (<see cref="AdkRate"/>).
    /// </summary>
    public sealed class PptRate : IIonizationRate
    {
        public double IpAu { get; } public double Z { get; } public int L { get; } public double OmegaAu { get; } public double N0 { get; }
        public double EnergyJ => IpAu * Au.Energy;
        private readonly double _kappa, _ns, _c2;
        public PptRate(double ionizationEv, double omega0, double z = 1, int l = 1, double n0 = 1)
        {
            IpAu = ionizationEv * Au.Ev / Au.Energy; Z = z; L = l; OmegaAu = omega0 * Au.Time; N0 = n0;
            _kappa = Math.Sqrt(2 * IpAu); _ns = Z / _kappa;
            double ls = _ns - 1;
            _c2 = Math.Exp(2 * _ns * Math.Log(2) - Math.Log(_ns) - IonizationMath.LogGamma(_ns + ls + 1) - IonizationMath.LogGamma(_ns - ls));
        }
        public string Description => string.Format(CultureInfo.InvariantCulture, "PPT (Ip = {0:G4} eV, Z = {1:G3}, l = {2})", IpAu * Au.Energy / Au.Ev, Z, L);

        public double Rate(double intensity)
        {
            double F = Au.FieldFromIntensity(intensity, N0) / Au.Field;
            if (F <= 0) return 0;
            double k3 = _kappa * _kappa * _kappa, w = OmegaAu, g = w * _kappa / F;
            double asg = Math.Log(g + Math.Sqrt(1 + g * g)), sq = Math.Sqrt(1 + g * g);
            double gg = 3 / (2 * g) * ((1 + 1 / (2 * g * g)) * asg - sq / (2 * g));
            double alpha = 2 * (asg - g / sq), beta = 2 * g / sq, nu = IpAu / w * (1 + 1 / (2 * g * g));
            double sum = 0;
            int k0 = (int)Math.Ceiling(nu);
            for (int k = k0; k < k0 + 20000; k++)
            {
                double d = k - nu, term = Math.Exp(-alpha * d) * IonizationMath.Dawson(Math.Sqrt(beta * d));
                sum += term;
                if (k > k0 + 10 && term < 1e-10 * sum) break;
            }
            double A0 = 4 / Math.Sqrt(3 * Math.PI) * g * g / (1 + g * g) * sum;
            double logW = 0.5 * Math.Log(6 / Math.PI) + Math.Log(_c2 * (2 * L + 1) * IpAu) + (2 * _ns - 1.5) * Math.Log(2 * k3 / F) + 0.75 * Math.Log(1 + g * g) + Math.Log(Math.Max(A0, 1e-300)) - 2 * k3 / (3 * F) * gg
                - 2 * _ns * Math.Log(1 + 2 * g / Math.E);   // Popov's Coulomb correction for gamma >~ 1 (Phys. Usp. 47, 855 (2004)): restores W ~ I^K
            return Math.Exp(logW) / Au.Time;
        }

        /// <summary>Quasi-static (instantaneous) ADK tunnel rate at field |E|.</summary>
        public double StaticRate(double field) => AdkRate.Static(field, IpAu, _ns, _c2, L);
    }

    /// <summary>ADK tunnel ionisation: cycle-averaged W = C² f(l) Ip sqrt(3F/(π κ³)) (2κ³/F)^{2n*-1} exp(-2κ³/(3F)) (a.u.),
    /// and the quasi-static rate (without the cycle-averaging factor) for carrier-resolved propagation.</summary>
    public sealed class AdkRate : IIonizationRate
    {
        private readonly double _ip, _ns, _c2, _n0; private readonly int _l;
        public double EnergyJ => _ip * Au.Energy;
        public AdkRate(double ionizationEv, double z = 1, int l = 1, double n0 = 1)
        {
            _ip = ionizationEv * Au.Ev / Au.Energy; _l = l; _n0 = n0;
            double kappa = Math.Sqrt(2 * _ip); _ns = z / kappa;
            double ls = _ns - 1;
            _c2 = Math.Exp(2 * _ns * Math.Log(2) - Math.Log(_ns) - IonizationMath.LogGamma(_ns + ls + 1) - IonizationMath.LogGamma(_ns - ls));
        }
        public string Description => string.Format(CultureInfo.InvariantCulture, "ADK (Ip = {0:G4} eV)", _ip * Au.Energy / Au.Ev);

        internal static double Static(double field, double ip, double ns, double c2, int l)
        {
            double F = Math.Abs(field) / Au.Field;
            if (F <= 0) return 0;
            double k3 = Math.Pow(2 * ip, 1.5);
            return Math.Exp(Math.Log(c2 * (2 * l + 1) * ip) + (2 * ns - 1) * Math.Log(2 * k3 / F) - 2 * k3 / (3 * F)) / Au.Time;
        }
        public double StaticRate(double field) => Static(field, _ip, _ns, _c2, _l);
        public double Rate(double intensity)
        {
            double E = Au.FieldFromIntensity(intensity, _n0), F = E / Au.Field;
            if (F <= 0) return 0;
            double k3 = Math.Pow(2 * _ip, 1.5);
            return Math.Sqrt(3 * F / (Math.PI * k3)) * StaticRate(E);
        }
    }

    /// <summary>
    /// Keldysh photo-ionisation of a dielectric (band gap Ug, reduced mass m*), cycle averaged (Keldysh 1965; form of
    /// Couairon &amp; Mysyrowicz, Phys. Rep. 441, 47 (2007)), per unit volume, returned per valence electron:
    /// W = (2ω/9π) (m*ω / ħ√Γ)^{3/2} Q(γ, x) exp(-π⟨x+1⟩ (K(Γ) - E(Γ)) / E(Ξ)) / ρ_n, Γ = γ²/(1+γ²), Ξ = 1/(1+γ²),
    /// γ = ω sqrt(m* Ug) / (e E), x = (2/π)(Ug/ħω) E(Ξ)/√Γ.
    /// </summary>
    public sealed class KeldyshRate : IIonizationRate
    {
        public double BandGapJ { get; } public double Mass { get; } public double Omega { get; } public double NeutralDensity { get; } public double N0 { get; }
        public double EnergyJ => BandGapJ;
        public KeldyshRate(double bandGapEv, double effectiveMass, double omega0, double neutralDensityM3, double n0)
        { BandGapJ = bandGapEv * Au.Ev; Mass = effectiveMass * Au.Me; Omega = omega0; NeutralDensity = neutralDensityM3; N0 = n0; }
        public string Description => string.Format(CultureInfo.InvariantCulture, "Keldysh (Ug = {0:G4} eV, m* = {1:G3} me)", BandGapJ / Au.Ev, Mass / Au.Me);
        public double StaticRate(double f) => double.NaN;

        public double Rate(double intensity)
        {
            double E = Au.FieldFromIntensity(intensity, N0);
            if (E <= 0) return 0;
            double w = Omega, g = w * Math.Sqrt(Mass * BandGapJ) / (Au.E * E);
            double G = g * g / (1 + g * g), X = 1 / (1 + g * g);
            var (kG, eG) = IonizationMath.Elliptic(G);
            var (kX, eX) = IonizationMath.Elliptic(X);
            double x = 2 / Math.PI * BandGapJ / (Au.Hbar * w) * eX / Math.Sqrt(G);
            double nx = Math.Floor(x + 1);
            double dec = Math.PI * (kG - eG) / eX;
            double sum = 0;
            for (int n = 0; n < 20000; n++)
            {
                double t = Math.Exp(-n * dec) * IonizationMath.Dawson(Math.Sqrt(Math.PI * Math.PI * (2 * nx - 2 * x + n) / (4 * kX * eX)));
                sum += t;
                if (n > 10 && t < 1e-12 * sum) break;
            }
            double Q = Math.Sqrt(Math.PI / (2 * kX)) * sum;
            double logW = Math.Log(2 * w / (9 * Math.PI)) + 1.5 * Math.Log(w * Mass / (Au.Hbar * Math.Sqrt(G))) + Math.Log(Math.Max(Q, 1e-300)) - nx * dec;
            return Math.Exp(logW) / NeutralDensity;
        }
    }

    /// <summary>A response that reports values after an element (peak electron density, ...).</summary>
    public interface IReportingResponse
    {
        void Report(IDictionary<string, double> values);
    }

    /// <summary>
    /// Photo-ionisation and plasma (docs/reference/ionization.md), a time-domain response evaluated along the co-moving
    /// time at every point (1-D: the on-axis intensity; spatial grids: each point):
    /// <code>dρ/dt = Σ_s W_s(I) (ρ_s - ρ_s,ionised) + (σ I / U) ρ - ρ/τ_rec - β ρ²</code>
    /// Drude: Δn = -(ρ / 2 n0 ρ_c) (1 - i/(ω0 τ_c)) / (1 + 1/(ω0 τ_c)²), ρ_c = ε0 m_e ω0² / e² (the imaginary part is
    /// inverse-bremsstrahlung absorption); the energy of each ionisation is taken from the field
    /// (Im Δn += U W ρ_n / (2 k0 I), multiphoton absorption). σ = k0 ω0 τ_c / (n0 ρ_c (1 + ω0²τ_c²)) is the avalanche
    /// cross section. Several species (air: N2 and O2) ionise independently. In carrier-resolved mode the quasi-static
    /// (ADK) rate at the instantaneous field is used where the model has one.
    /// </summary>
    public sealed class PlasmaResponse : ITimeDomainResponse, IReportingResponse
    {
        public sealed class Channel { public string Name; public IIonizationRate Rate; public double DensityM3; }
        public List<Channel> Channels { get; } = new List<Channel>();
        public double CollisionTimeS { get; set; } = 350e-15;
        public double RecombinationTimeS { get; set; } = double.PositiveInfinity;
        public double RecombinationBetaM3PerS { get; set; }
        public bool Avalanche { get; set; } = true;
        public bool Absorption { get; set; } = true;
        public double N0 { get; set; } = 1.0;
        private double _peakRho;
        private readonly object _lock = new object();

        public string Description => "plasma: " + string.Join(", ", Channels.Select(c => c.Name + " " + c.Rate.Description + string.Format(CultureInfo.InvariantCulture, " ({0:G3} /m^3)", c.DensityM3))) +
            string.Format(CultureInfo.InvariantCulture, ", tau_c = {0:G3} fs{1}{2}", CollisionTimeS * 1e15, Avalanche ? ", avalanche" : "", double.IsInfinity(RecombinationTimeS) ? "" : ", tau_rec = " + (RecombinationTimeS * 1e15).ToString("G3", CultureInfo.InvariantCulture) + " fs");
        public bool NeedsCarrierResolution => false;
        public double PeakElectronDensityM3 { get { lock (_lock) return _peakRho; } }

        public void Report(IDictionary<string, double> values)
        {
            values["peakElectronDensityM3"] = PeakElectronDensityM3;
            double nt = Channels.Sum(c => c.DensityM3);
            if (nt > 0) values["peakIonizationFraction"] = PeakElectronDensityM3 / nt;
        }

        public void Evaluate(ResponseContext ctx, double[] dnRe, double[] dnIm)
        {
            int n = ctx.N; double w0 = ctx.Omega0, c = PhysicalConstants.SpeedOfLightMPerS;
            double n0 = double.IsNaN(ctx.N0) ? N0 : ctx.N0;
            double rhoC = Au.Eps0 * Au.Me * w0 * w0 / (Au.E * Au.E);
            double wt = w0 * CollisionTimeS, k0 = w0 / c;
            double drude = 1 / (2 * n0 * rhoC) / (1 + 1 / (wt * wt));
            double sigma = k0 * w0 * CollisionTimeS / (n0 * rhoC * (1 + wt * wt));
            int K = Channels.Count;
            var ionised = new double[K];
            double rho = 0, dt = n > 1 ? ctx.T[1] - ctx.T[0] : 0, peak = 0;
            double uMean = Channels.Count > 0 ? Channels.Average(ch => ch.Rate.EnergyJ) : 1;
            for (int i = 0; i < n; i++)
            {
                double I = ctx.Intensity[i];
                double gen = 0, loss = 0;
                for (int k = 0; k < K; k++)
                {
                    var ch = Channels[k];
                    // carrier-resolved: quasi-static rate at the instantaneous field |E| = sqrt(I_inst / (n0 c eps0))
                    double W = ctx.CarrierResolved && ctx.InstantIntensity != null && !double.IsNaN(ch.Rate.StaticRate(1)) ? ch.Rate.StaticRate(Math.Sqrt(Math.Max(0, ctx.InstantIntensity[i]) / (n0 * c * Au.Eps0))) : ch.Rate.Rate(I);
                    double avail = Math.Max(0, ch.DensityM3 - ionised[k]);
                    double g = W * avail;
                    gen += g; loss += g * ch.Rate.EnergyJ;
                    ionised[k] = Math.Min(ch.DensityM3, ionised[k] + g * dt);
                }
                double aval = Avalanche ? sigma * I / uMean * rho : 0;
                // index change at this instant (the plasma made before)
                dnRe[i] += -drude * rho;
                if (Absorption)
                {
                    dnIm[i] += drude * rho / wt;
                    if (I > 0) dnIm[i] += (loss + aval * uMean) / I / (2 * k0);
                }
                double drho = gen + aval - (double.IsInfinity(RecombinationTimeS) ? 0 : rho / RecombinationTimeS) - RecombinationBetaM3PerS * rho * rho;
                rho = Math.Max(0, rho + drho * dt);
                if (rho > peak) peak = rho;
            }
            lock (_lock) if (peak > _peakRho) _peakRho = peak;
        }

        /// <summary>Plasma response for a material: gases from GasData (air = N2 + O2), dielectrics from their
        /// SolidIonizationData; spec keys override (model, densities, times, avalanche).</summary>
        public static PlasmaResponse For(Material m, double omega0, Serialization.JsonObject spec, out string note)
        {
            spec = spec ?? new Serialization.JsonObject();
            string model = spec.GetString("model", m.IsGas ? "ppt" : "keldysh").ToLowerInvariant();
            double n0 = m.IndexAt(omega0);
            var r = new PlasmaResponse { N0 = n0, Avalanche = spec.GetBool("avalanche", !m.IsGas || spec.Has("collisionTimeS")), Absorption = spec.GetBool("absorption", true) };
            note = null;
            if (m.IsGas)
            {
                double p = double.IsNaN(m.GasPressureBar) ? 1.0 : m.GasPressureBar, T = double.IsNaN(m.GasTemperatureK) ? 293.15 : m.GasTemperatureK;
                double ntot = spec.GetDouble("neutralDensityM3", p * 1e5 / (1.380649e-23 * T));
                var mix = string.Equals(m.GasSpecies, "air", StringComparison.OrdinalIgnoreCase) || (m.GasSpecies == null && m.Name.StartsWith("Air", StringComparison.OrdinalIgnoreCase))
                    ? GasData.AirComposition.Where(x => x.Species != "Ar").ToArray() : new[] { (m.GasSpecies ?? spec.GetString("species", "Ar"), 1.0) };
                foreach (var (sp, f) in mix)
                {
                    var g = GasData.Get(sp) ?? throw new Serialization.JsonException("ionization: no data for gas '" + sp + "' (give ionizationEv)");
                    double ip = spec.GetDouble("ionizationEv", g.IonizationEv);
                    IIonizationRate rate = model == "adk" ? new AdkRate(ip, spec.GetDouble("zEff", g.ZEff), g.L, n0)
                        : model == "mpi" ? (IIonizationRate)new MpiRate(spec.GetDouble("sigmaK", 0), spec.GetInt("photons", (int)Math.Ceiling(ip * Au.Ev / (Au.Hbar * omega0))), ip)
                        : model == "ppt" ? new PptRate(ip, omega0, spec.GetDouble("zEff", g.ZEff), g.L, n0)
                        : throw new Serialization.JsonException("ionization model for gases: ppt, adk or mpi");
                    r.Channels.Add(new Channel { Name = sp, Rate = rate, DensityM3 = ntot * f });
                }
                r.CollisionTimeS = spec.GetDouble("collisionTimeS", 350e-15 / p);
                r.RecombinationBetaM3PerS = spec.GetDouble("recombinationBetaM3PerS", 0);
                r.RecombinationTimeS = spec.GetDouble("recombinationTimeS", double.PositiveInfinity);
            }
            else
            {
                var d = m.Ionization ?? SolidIonizationData.For(m.Name);
                if (d == null && !spec.Has("bandGapEv")) throw new Serialization.JsonException("ionization: no band-gap data for material '" + m.Name + "' (give bandGapEv, effectiveMass, neutralDensityM3)");
                d = d ?? new SolidIonizationData();
                double ug = spec.GetDouble("bandGapEv", d.BandGapEv), ms = spec.GetDouble("effectiveMass", d.EffectiveMass), rn = spec.GetDouble("neutralDensityM3", d.NeutralDensityM3);
                IIonizationRate rate = model == "mpi" ? new MpiRate(spec.GetDouble("sigmaK", 0), spec.GetInt("photons", (int)Math.Ceiling(ug * Au.Ev / (Au.Hbar * omega0))), ug)
                    : model == "keldysh" ? (IIonizationRate)new KeldyshRate(ug, ms, omega0, rn, n0)
                    : throw new Serialization.JsonException("ionization model for solids: keldysh or mpi");
                r.Channels.Add(new Channel { Name = m.Name, Rate = rate, DensityM3 = rn });
                r.CollisionTimeS = spec.GetDouble("collisionTimeS", d.CollisionTimeS);
                r.RecombinationTimeS = spec.GetDouble("recombinationTimeS", d.RecombinationTimeS);
            }
            return r;
        }
    }
}
