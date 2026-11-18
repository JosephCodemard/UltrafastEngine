using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
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
            if (!(g < 1e6)) return 0;   // a field this weak (the far wing of a pulse): G rounds to 1, K(1) is infinite, and the rate is below 1e-100 of any usable one
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
    public sealed class PlasmaResponse : IDispersiveResponse, IReportingResponse
    {
        public sealed class Channel { public string Name; public IIonizationRate Rate; public double DensityM3; }
        public List<Channel> Channels { get; } = new List<Channel>();
        public double CollisionTimeS { get; set; } = 350e-15;
        public double RecombinationTimeS { get; set; } = double.PositiveInfinity;
        public double RecombinationBetaM3PerS { get; set; }
        public bool Avalanche { get; set; } = true;
        public bool Absorption { get; set; } = true;
        public double N0 { get; set; } = 1.0;
        /// <summary>Free-electron density present before the pulse (a prepared plasma, a discharge), 1/m^3.</summary>
        public double InitialDensityM3 { get; set; }
        /// <summary>True (default): the Drude index is applied at every frequency of the pulse, Δn(ω) ∝ 1/(ω (ω + iν)) (<see cref="Spectral"/>);
        /// false: at the carrier frequency only.</summary>
        public bool FrequencyResolved { get; set; } = true;
        /// <summary>True (default, key <c>ionization.skipNegligible</c>): the executor leaves the plasma term out of an element when
        /// <see cref="NegligibleAt"/> bounds its effect below 1e-6 rad of phase and 1e-9 of the energy.</summary>
        public bool SkipNegligible { get; set; } = true;
        /// <summary>Intensity and duration margin of <see cref="NegligibleAt"/>: the bound is evaluated at this many times the entry peak
        /// intensity, over this many times the pulse's effective duration (the pulse may compress or focus inside the element).</summary>
        public const double NegligibleMargin = 4;
        private double _peakRho;
        private readonly object _lock = new object();

        public string Description => "plasma" + (FrequencyResolved ? " (Drude index at every frequency)" : " (Drude index at the carrier)") + (InitialDensityM3 > 0 ? string.Format(CultureInfo.InvariantCulture, ", prepared " + "{0:G3} /m^3", InitialDensityM3) : "") + ": " + string.Join(", ", Channels.Select(c => c.Name + " " + c.Rate.Description + string.Format(CultureInfo.InvariantCulture, " ({0:G3} /m^3)", c.DensityM3))) +
            string.Format(CultureInfo.InvariantCulture, ", tau_c = {0:G3} fs{1}{2}", CollisionTimeS * 1e15, Avalanche ? ", avalanche" : "", double.IsInfinity(RecombinationTimeS) ? "" : ", tau_rec = " + (RecombinationTimeS * 1e15).ToString("G3", CultureInfo.InvariantCulture) + " fs");
        public bool NeedsCarrierResolution => false;
        public double PeakElectronDensityM3 { get { lock (_lock) return _peakRho; } }

        public void Report(IDictionary<string, double> values)
        {
            values["peakElectronDensityM3"] = PeakElectronDensityM3;
            double nt = Channels.Sum(c => c.DensityM3);
            if (nt > 0) values["peakIonizationFraction"] = PeakElectronDensityM3 / nt;
        }

        /// <summary>Everything at the carrier frequency (the Drude term included).</summary>
        public void Evaluate(ResponseContext ctx, double[] dnRe, double[] dnIm) => Run(ctx, dnRe, dnIm, null, null);

        /// <summary>The Drude term (index change and inverse-bremsstrahlung absorption) goes to the dispersive arrays, the energy taken by
        /// ionisation and avalanche (a loss at the carrier, not a Drude term) stays in dnIm.</summary>
        public void EvaluateSplit(ResponseContext ctx, double[] dnRe, double[] dnIm, double[] dispRe, double[] dispIm) => Run(ctx, dnRe, dnIm, dispRe, dispIm);

        /// <summary>R(ω) = ω_ref (ω_ref + iν) / (ω (ω + iν)), ν = 1/τ_c: the Drude index Δn = -ω_p² / (2 n0 ω (ω + iν)) relative to its value at ω_ref (Couairon &amp; Mysyrowicz, Phys. Rep. 441, 47 (2007), eq. 6 and 15).</summary>
        public Complex Spectral(double omegaAbs, double omegaRef)
        {
            if (!(omegaAbs > 0)) return Complex.Zero;
            double nu = 1 / CollisionTimeS;
            return omegaRef * new Complex(omegaRef, nu) / (omegaAbs * new Complex(omegaAbs, nu));
        }

        /// <summary>The electron density and the energy taken by ionisation along the time axis of <paramref name="ctx"/>: rho[i] is the density made before
        /// sample i (what acts on the field there), lossWPerM3[i] the ionisation power density sum_s W_s U_s (ρ_s - ρ_s,ionised) at sample i. The peak density
        /// is recorded for <see cref="Report"/>. Shared by <see cref="Run"/> and the chi(2) crystals.</summary>
        public void History(ResponseContext ctx, double[] rho, double[] lossWPerM3)
        {
            int n = ctx.N; double w0 = ctx.Omega0, c = PhysicalConstants.SpeedOfLightMPerS;
            double n0 = double.IsNaN(ctx.N0) ? N0 : ctx.N0;
            double rhoC = Au.Eps0 * Au.Me * w0 * w0 / (Au.E * Au.E);
            double sigma = (w0 / c) * w0 * CollisionTimeS / (n0 * rhoC * (1 + w0 * CollisionTimeS * w0 * CollisionTimeS));
            int K = Channels.Count;
            var ionised = new double[K];
            double r = InitialDensityM3, dt = n > 1 ? ctx.T[1] - ctx.T[0] : 0, peak = r;
            double uMean = K > 0 ? Channels.Average(ch => ch.Rate.EnergyJ) : 1;
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
                double aval = Avalanche ? sigma * I / uMean * r : 0;
                rho[i] = r; lossWPerM3[i] = loss;
                double drho = gen + aval - (double.IsInfinity(RecombinationTimeS) ? 0 : r / RecombinationTimeS) - RecombinationBetaM3PerS * r * r;
                r = Math.Max(0, r + drho * dt);
                if (r > peak) peak = r;
            }
            lock (_lock) if (peak > _peakRho) _peakRho = peak;
        }

        private void Run(ResponseContext ctx, double[] dnRe, double[] dnIm, double[] dispRe, double[] dispIm)
        {
            int n = ctx.N; double w0 = ctx.Omega0, c = PhysicalConstants.SpeedOfLightMPerS;
            double n0 = double.IsNaN(ctx.N0) ? N0 : ctx.N0;
            double rhoC = Au.Eps0 * Au.Me * w0 * w0 / (Au.E * Au.E);
            double wt = w0 * CollisionTimeS, k0 = w0 / c;
            double drude = 1 / (2 * n0 * rhoC) / (1 + 1 / (wt * wt));
            var rho = new double[n]; var loss = new double[n];
            History(ctx, rho, loss);
            var dRe = dispRe ?? dnRe; var dIm = dispIm ?? dnIm;
            for (int i = 0; i < n; i++)
            {
                double I = ctx.Intensity[i];
                // index change at this instant (the plasma made before)
                dRe[i] += -drude * rho[i];
                if (Absorption)
                {
                    dIm[i] += drude * rho[i] / wt;
                    // The avalanche takes its energy from the inverse-bremsstrahlung absorption already booked in the Drude
                    // imaginary part: aval * uMean / (I 2 k0) = sigma rho / (2 k0) = drude rho / wt. Adding it again would count it twice.
                    if (I > 0) dnIm[i] += loss[i] / I / (2 * k0);
                }
            }
        }

        /// <summary>The Drude index change Δn = dRe + i dIm of a density ρ at carrier ω and background index n: dRe = -ρ / (2 n ρ_c (1 + 1/(ωτ)²)), dIm = -dRe / (ωτ)
        /// (inverse bremsstrahlung, when <see cref="Absorption"/>). The ionisation loss is separate (<see cref="History"/>).</summary>
        public (double Re, double Im) Drude(double rho, double omega, double n)
        {
            double rhoC = Au.Eps0 * Au.Me * omega * omega / (Au.E * Au.E), wt = omega * CollisionTimeS;
            double d = 1 / (2 * n * rhoC) / (1 + 1 / (wt * wt));
            return (-d * rho, Absorption ? d * rho / wt : 0);
        }

        /// <summary>The plasma term for a device kernel (<see cref="PlasmaDeviceSpec"/>), at carrier omega0 on a grid with the given absolute
        /// frequencies (centred order). Envelope mode only (the cycle-averaged rate).</summary>
        private static readonly Dictionary<string, double[]> TableCache = new Dictionary<string, double[]>();

        public PlasmaDeviceSpec DeviceForm(double omega0, double[] omegaAbsolute)
        {
            double c = PhysicalConstants.SpeedOfLightMPerS, rhoC = Au.Eps0 * Au.Me * omega0 * omega0 / (Au.E * Au.E), wt = omega0 * CollisionTimeS, k0 = omega0 / c;
            var s = new PlasmaDeviceSpec
            {
                Channels = Channels.Count, DensityM3 = Channels.Select(ch => ch.DensityM3).ToArray(), EnergyJ = Channels.Select(ch => ch.Rate.EnergyJ).ToArray(),
                Drude = 1 / (2 * N0 * rhoC) / (1 + 1 / (wt * wt)), OmegaTauC = wt, K0 = k0, Sigma = k0 * omega0 * CollisionTimeS / (N0 * rhoC * (1 + wt * wt)),
                UMean = Channels.Count > 0 ? Channels.Average(ch => ch.Rate.EnergyJ) : 1,
                RecombinationRatePerS = double.IsInfinity(RecombinationTimeS) ? 0 : 1 / RecombinationTimeS, RecombinationBetaM3PerS = RecombinationBetaM3PerS,
                InitialDensityM3 = InitialDensityM3, Avalanche = Avalanche, Absorption = Absorption, FrequencyResolved = FrequencyResolved,
                ReportPeakDensity = rho => { lock (_lock) if (rho > _peakRho) _peakRho = rho; }
            };
            int nt = PlasmaDeviceSpec.TableN;
            s.LnRate = new double[nt * Channels.Count];
            for (int k = 0; k < Channels.Count; k++)
            {
                var rate = Channels[k].Rate;
                // one table per rate model (the passes of a cell share it): key = model, description and the exact rate at four intensities
                // (a fingerprint of every parameter, carrier and index included)
                string key = rate.GetType().FullName + "|" + rate.Description + "|" + string.Join("|", new[] { 1e15, 1e16, 1e17, 1e18 }.Select(x => rate.Rate(x).ToString("R", CultureInfo.InvariantCulture)));
                double[] tab;
                lock (TableCache)
                    if (!TableCache.TryGetValue(key, out tab))
                    {
                        tab = new double[nt];
                        for (int i = 0; i < nt; i++)
                        {
                            double w = rate.Rate(Math.Exp(PlasmaDeviceSpec.LnIMin + i * s.DLnI));
                            tab[i] = w > 1e-300 && !double.IsNaN(w) ? Math.Log(w) : -690.8;   // ln 1e-300
                        }
                        if (TableCache.Count > 64) TableCache.Clear();
                        TableCache[key] = tab;
                    }
                Array.Copy(tab, 0, s.LnRate, k * nt, nt);
            }
            if (FrequencyResolved)
            {
                s.SpectralRate = new Complex[omegaAbsolute.Length];
                for (int j = 0; j < omegaAbsolute.Length; j++)
                {
                    double w = omegaAbsolute[j];
                    if (w > 0) s.SpectralRate[j] = Complex.ImaginaryOne * (w / c) * Spectral(w, omega0);
                }
            }
            return s;
        }

        /// <summary>
        /// Conservative upper bound of what this plasma term can do in an element, from the entry pulse: peak intensity I_pk, effective
        /// duration T = E / P_pk. Every rate is taken at M I_pk (M = <see cref="NegligibleMargin"/>) over M T, with avalanche growth
        /// exp(sigma M^2 I_pk T / U) (the bounds hold for rates that grow at least linearly with I, as MPI, PPT and Keldysh do). The density
        /// bound rho gives the largest Drude phase k0 |dn| L (dn = -rho / (2 n0 rho_c) with the collision factor) and the largest absorbed
        /// fraction (rho U plus inverse bremsstrahlung) L A / E. Negligible when the phase is below 1e-6 rad and the fraction below 1e-9: the
        /// field then changes by less than the stepper's tolerance. A prepared plasma (electronDensityM3 &gt; 0) is never negligible.
        /// </summary>
        public bool NegligibleAt(double peakIntensityWPerM2, double energyJ, double peakPowerW, double lengthM, double areaM2, double omega0, out double rhoBoundM3, out double phaseRad, out double lossFraction)
        {
            double c = PhysicalConstants.SpeedOfLightMPerS, M = NegligibleMargin;
            double rhoC = Au.Eps0 * Au.Me * omega0 * omega0 / (Au.E * Au.E), wt = omega0 * CollisionTimeS, k0 = omega0 / c;
            double drude = 1 / (2 * N0 * rhoC) / (1 + 1 / (wt * wt));
            double T = peakPowerW > 0 ? energyJ / peakPowerW : 0, I = M * peakIntensityWPerM2;
            double rho = 0, uMean = Channels.Count > 0 ? Channels.Average(ch => ch.Rate.EnergyJ) : 1, absorbedPerVolume = 0;
            foreach (var ch in Channels)
            {
                double frac = Math.Min(1, Math.Max(0, ch.Rate.Rate(I)) * M * T);
                if (double.IsNaN(frac)) frac = 1;
                rho += frac * ch.DensityM3;
                absorbedPerVolume += frac * ch.DensityM3 * ch.Rate.EnergyJ;
            }
            if (Avalanche)
            {
                double sigma = k0 * omega0 * CollisionTimeS / (N0 * rhoC * (1 + wt * wt));
                double g = sigma * I * M * T / uMean;
                rho *= g < 700 ? Math.Exp(g) : double.PositiveInfinity;
            }
            rhoBoundM3 = rho + InitialDensityM3;
            phaseRad = k0 * drude * rhoBoundM3 * Math.Max(1, 1 / wt) * lengthM;
            // inverse bremsstrahlung: absorbed power 2 k0 Im(dn) I per volume over the time M T
            double ib = Absorption ? 2 * k0 * drude * rhoBoundM3 / wt * I * M * T : 0;
            lossFraction = energyJ > 0 ? (absorbedPerVolume + ib) * lengthM * areaM2 / energyJ : double.PositiveInfinity;
            return InitialDensityM3 <= 0 && phaseRad < 1e-6 && lossFraction < 1e-9;
        }

        /// <summary>Plasma response for a material: gases from GasData (air = N2 + O2), dielectrics from their
        /// SolidIonizationData; spec keys override (model, densities, times, avalanche).</summary>
        public static PlasmaResponse For(Material m, double omega0, Serialization.JsonObject spec, out string note)
        {
            spec = spec ?? new Serialization.JsonObject();
            string model = spec.GetString("model", m.IsGas ? "ppt" : "keldysh").ToLowerInvariant();
            double n0 = m.IndexAt(omega0);
            var r = new PlasmaResponse { N0 = n0, Avalanche = spec.GetBool("avalanche", !m.IsGas || spec.Has("collisionTimeS")), Absorption = spec.GetBool("absorption", true),
                InitialDensityM3 = spec.GetDouble("electronDensityM3", 0), FrequencyResolved = spec.GetBool("frequencyResolved", true), SkipNegligible = spec.GetBool("skipNegligible", true) };
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
            else r.ConfigureSolid(m.Name, m.Ionization ?? SolidIonizationData.For(m.Name), omega0, n0, spec, model);
            return r;
        }

        /// <summary>The dielectric branch of <see cref="For"/> for a named solid (a glass or a χ(2) crystal) with its <see cref="SolidIonizationData"/>
        /// (null: the run must give bandGapEv, effectiveMass and neutralDensityM3); spec keys override the data.</summary>
        public static PlasmaResponse ForSolid(string name, SolidIonizationData d, double omega0, double n0, Serialization.JsonObject spec)
        {
            spec = spec ?? new Serialization.JsonObject();
            string model = spec.GetString("model", "keldysh").ToLowerInvariant();
            var r = new PlasmaResponse { N0 = n0, Avalanche = spec.GetBool("avalanche", true), Absorption = spec.GetBool("absorption", true),
                InitialDensityM3 = spec.GetDouble("electronDensityM3", 0), FrequencyResolved = spec.GetBool("frequencyResolved", true), SkipNegligible = spec.GetBool("skipNegligible", true) };
            r.ConfigureSolid(name, d, omega0, n0, spec, model);
            return r;
        }

        private void ConfigureSolid(string name, SolidIonizationData d, double omega0, double n0, Serialization.JsonObject spec, string model)
        {
            if (d == null && !spec.Has("bandGapEv")) throw new Serialization.JsonException("ionization: no band-gap data for material '" + name + "' (give bandGapEv, effectiveMass, neutralDensityM3)");
            d = d ?? new SolidIonizationData();
            double ug = spec.GetDouble("bandGapEv", d.BandGapEv), ms = spec.GetDouble("effectiveMass", d.EffectiveMass), rn = spec.GetDouble("neutralDensityM3", d.NeutralDensityM3);
            IIonizationRate rate = model == "mpi" ? new MpiRate(spec.GetDouble("sigmaK", 0), spec.GetInt("photons", (int)Math.Ceiling(ug * Au.Ev / (Au.Hbar * omega0))), ug)
                : model == "keldysh" ? (IIonizationRate)new KeldyshRate(ug, ms, omega0, rn, n0)
                : throw new Serialization.JsonException("ionization model for solids: keldysh or mpi");
            Channels.Add(new Channel { Name = name, Rate = rate, DensityM3 = rn });
            CollisionTimeS = spec.GetDouble("collisionTimeS", d.CollisionTimeS);
            RecombinationTimeS = spec.GetDouble("recombinationTimeS", d.RecombinationTimeS);
        }
    }
}
