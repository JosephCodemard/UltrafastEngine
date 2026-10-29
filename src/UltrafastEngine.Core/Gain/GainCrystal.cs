using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Gain
{
    /// <summary>
    /// The pump: "cw" (diode, power on continuously), "pulsed" (e.g. a Q-switched green laser for Ti:sapphire:
    /// energy deposited before the seed arrives, "delayS" earlier) or "none" (inversion given directly).
    /// "profile": "tophat" (default), "gaussian" or "superGaussian" (+ "order") with "radiusM" (1/e^2 for Gaussian);
    /// "passes": 2 reflects the unabsorbed pump back through the crystal.
    /// </summary>
    public sealed class PumpSpec
    {
        public string Kind = "none";
        public double PowerW, EnergyJ, WavelengthM = double.NaN, RadiusM = double.NaN, DelayS;
        public string Profile = "tophat";
        public int Order = 4, Passes = 1;

        public static PumpSpec FromJson(JsonObject o, string origin)
        {
            if (o == null) return new PumpSpec();
            var p = new PumpSpec
            {
                Kind = o.GetString("kind", o.Has("energyJ") ? "pulsed" : "cw").ToLowerInvariant(),
                PowerW = o.GetDouble("powerW", 0), EnergyJ = o.GetDouble("energyJ", 0),
                WavelengthM = o.GetDouble("wavelengthM", double.NaN), RadiusM = o.GetDouble("radiusM", double.NaN),
                DelayS = o.GetDouble("delayS", 0), Profile = o.GetString("profile", "tophat").ToLowerInvariant(),
                Order = o.GetInt("order", 4), Passes = Math.Max(1, Math.Min(2, o.GetInt("passes", 1)))
            };
            if (p.Kind != "cw" && p.Kind != "pulsed" && p.Kind != "none") throw new JsonException(origin + ": pump kind must be cw, pulsed or none");
            if (p.Kind != "none")
            {
                if (double.IsNaN(p.WavelengthM)) throw new JsonException(origin + ": pump needs wavelengthM");
                if (!(p.RadiusM > 0)) throw new JsonException(origin + ": pump needs radiusM");
                if (p.Kind == "cw" && !(p.PowerW > 0)) throw new JsonException(origin + ": cw pump needs powerW");
                if (p.Kind == "pulsed" && !(p.EnergyJ > 0)) throw new JsonException(origin + ": pulsed pump needs energyJ");
            }
            return p;
        }

        /// <summary>Pump intensity per watt (or fluence per joule) at radius r (1/m^2): the profile normalised to unit power.</summary>
        public double Shape(double r)
        {
            double w = RadiusM;
            switch (Profile)
            {
                case "gaussian": return 2 / (Math.PI * w * w) * Math.Exp(-2 * r * r / (w * w));
                case "supergaussian":
                {
                    // exp(-2 (r/w)^(2 n)); norm: integral = pi w^2 Gamma(1 + 1/n) / 2^(1/n)
                    double n = Math.Max(1, Order), norm = Math.PI * w * w * Gamma(1 + 1 / n) / Math.Pow(2, 1 / n);
                    return Math.Exp(-2 * Math.Pow(r / w, 2 * n)) / norm;
                }
                default: return r <= w ? 1 / (Math.PI * w * w) : 0;
            }
        }

        public string Describe()
        {
            if (Kind == "none") return "no pump (inversion given)";
            var ci = CultureInfo.InvariantCulture;
            return string.Format(ci, "{0} pump {1} at {2:G5} nm, {3} w = {4:G4} mm{5}", Kind, Kind == "cw" ? (PowerW.ToString("G4", ci) + " W") : (EnergyJ * 1e3).ToString("G4", ci) + " mJ",
                WavelengthM * 1e9, Profile, RadiusM * 1e3, Passes == 2 ? ", double pass" : "");
        }

        internal static double Gamma(double x)
        {
            // Lanczos approximation (x > 0)
            double[] g = { 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
            if (x < 0.5) return Math.PI / (Math.Sin(Math.PI * x) * Gamma(1 - x));
            x -= 1; double a = 0.99999999999980993; double t = x + 7.5;
            for (int i = 0; i < 8; i++) a += g[i] / (x + i + 1);
            return Math.Sqrt(2 * Math.PI) * Math.Pow(t, x + 0.5) * Math.Exp(-t) * a;
        }
    }

    /// <summary>
    /// A pumped gain crystal: the upper-state density N2 on z slices (and transverse cells: one cell of the signal's
    /// mode area in 1-D, the points of a radial/cartesian basis in spatial models), its pumping between pulses (rate
    /// equations with pump absorption, bleaching and stimulated emission at the pump wavelength), heat load and
    /// thermal lens, and the amplification of a pulse with time-ordered saturation.
    /// </summary>
    public sealed class GainCrystal
    {
        const double Hbar = 1.054571817e-34, H = GainMedium.H, C = GainMedium.C;
        public GainMedium Medium { get; }
        public double LengthM { get; }
        public int Nz { get; }
        public double Dz => LengthM / Nz;
        public PumpSpec Pump { get; }
        public ITransverseBasis Basis { get; }            // null in 1-D
        public int Cells { get; }
        public double[] CellArea { get; }                  // m^2
        public double[] CellX { get; }
        public double[] CellY { get; }
        public double[] Nt { get; }                        // dopant density per cell [j * Cells + c]
        public double[] N2 { get; set; }                   // upper-state density [j * Cells + c]
        private readonly double[] _pumpShape;              // per cell (1/m^2): pump intensity per W / fluence per J
        public double[] AbsorbedPumpJ { get; }             // per slice, accumulated by the last pumping call (all cells, x the cell's share)
        public double PumpOmega { get; }
        private readonly double _sap, _sep;                // pump absorption / emission cross sections
        public ISpatialField DopingProfile { get; }
        /// <summary>Non-null: amplified spontaneous emission is seeded (1-D passes): per slice and frequency bin, hbar w sigma_e N2 dz of noise into the signal mode.</summary>
        public Random Ase { get; set; }

        /// <summary>1-D: one cell representing a Gaussian signal mode of 1/e^2 radius <paramref name="modeRadiusM"/>.</summary>
        public GainCrystal(GainMedium m, double lengthM, int nz, PumpSpec pump, double modeRadiusM, ITransverseBasis basis, ISpatialField doping)
        {
            Medium = m; LengthM = lengthM; Nz = Math.Max(1, nz); Pump = pump; Basis = basis; DopingProfile = doping;
            if (basis == null)
            {
                Cells = 1; CellArea = new[] { Math.PI * modeRadiusM * modeRadiusM / 2 }; CellX = new[] { 0.0 }; CellY = new[] { 0.0 };
            }
            else { Cells = basis.M; CellArea = (double[])basis.Weights.Clone(); CellX = (double[])basis.X.Clone(); CellY = (double[])basis.Y.Clone(); }
            Nt = new double[Nz * Cells]; N2 = new double[Nz * Cells]; AbsorbedPumpJ = new double[Nz];
            for (int j = 0; j < Nz; j++)
            {
                var sc = doping != null ? doping.AtPoints(CellX, CellY, (j + 0.5) * Dz) : null;
                for (int c = 0; c < Cells; c++) Nt[j * Cells + c] = m.DopingDensityM3 * (sc != null ? Math.Max(0, sc[c]) : 1.0);
            }
            _pumpShape = new double[Cells];
            if (pump.Kind != "none")
            {
                PumpOmega = GainMedium.Omega(pump.WavelengthM);
                _sap = m.SigmaA(PumpOmega); _sep = m.SigmaE(PumpOmega);
                if (basis == null)
                {
                    // signal-weighted pump intensity over the Gaussian mode: integral shape(r) I_s(r) dA / integral I_s dA
                    double s = 0, sw = 0, rmax = 4 * Math.Max(modeRadiusM, pump.RadiusM); int nr = 4000;
                    for (int i = 0; i < nr; i++) { double r = (i + 0.5) * rmax / nr, wgt = Math.Exp(-2 * r * r / (modeRadiusM * modeRadiusM)) * r; s += pump.Shape(r) * wgt; sw += wgt; }
                    _pumpShape[0] = s / sw;
                }
                else for (int c = 0; c < Cells; c++) _pumpShape[c] = pump.Shape(Math.Sqrt(CellX[c] * CellX[c] + CellY[c] * CellY[c]));
            }
        }

        public GainCrystal CloneState() { var g = (GainCrystal)MemberwiseClone(); g.N2 = (double[])N2.Clone(); return g; }
        public double[] SaveN2() => (double[])N2.Clone();
        public void RestoreN2(double[] s) => Array.Copy(s, N2, N2.Length);

        /// <summary>Set a uniform inversion fraction beta = N2 / N_t.</summary>
        public void SetInversionFraction(double beta) { for (int k = 0; k < N2.Length; k++) N2[k] = beta * Nt[k]; }

        // ------------------------------------------------------------------------------------------ pumping

        /// <summary>Pump-absorption coefficient (1/m) of a cell with the current N2.</summary>
        private double PumpAlpha(int k) => _sap * (Nt[k] - N2[k]) - _sep * N2[k];

        /// <summary>
        /// CW pumping for <paramref name="durationS"/> (rate equations, exact exponential update per step with the pump
        /// flux of that step; forward and optional return pass). Accumulates the absorbed pump energy per slice.
        /// </summary>
        public void PumpCw(double durationS, int steps = 60, bool tally = true)
        {
            if (Pump.Kind != "cw" || durationS <= 0) { Decay(durationS); return; }
            if (tally) Array.Clear(AbsorbedPumpJ, 0, Nz);
            double hv = H * PumpOmega / (2 * Math.PI), tau = Medium.LifetimeS, dt = durationS / steps;
            var phiF = new double[Nz * Cells]; var phiB = new double[Nz * Cells];
            for (int s = 0; s < steps; s++)
            {
                // forward sweep: intensity at each slice centre
                for (int c = 0; c < Cells; c++)
                {
                    double I = Pump.PowerW * _pumpShape[c];
                    for (int j = 0; j < Nz; j++)
                    {
                        int k = j * Cells + c; double a = PumpAlpha(k), t = Math.Exp(-a * Dz);
                        phiF[k] = I * (1 - t) / Math.Max(1e-300, a * Dz) / hv;   // slice-averaged photon flux
                        if (tally) AbsorbedPumpJ[j] += I * (1 - t) * CellArea[c] * dt;
                        I *= t;
                    }
                    if (Pump.Passes == 2)
                        for (int j = Nz - 1; j >= 0; j--)
                        {
                            int k = j * Cells + c; double a = PumpAlpha(k), t = Math.Exp(-a * Dz);
                            phiB[k] = I * (1 - t) / Math.Max(1e-300, a * Dz) / hv;
                            if (tally) AbsorbedPumpJ[j] += I * (1 - t) * CellArea[c] * dt;
                            I *= t;
                        }
                }
                for (int k = 0; k < N2.Length; k++)
                {
                    double phi = phiF[k] + phiB[k];
                    double A = _sap * Nt[k] * phi, B = (_sap + _sep) * phi + 1 / tau;
                    double ninf = A / B;
                    N2[k] = ninf + (N2[k] - ninf) * Math.Exp(-B * dt);
                }
            }
            if (Basis == null && tally)
            {
                // 1-D: the slice tally above is for the mode cell (intensity x mode area); scale to the whole pump beam
                double scale = 1 / (_pumpShape[0] * CellArea[0]);
                for (int j = 0; j < Nz; j++) AbsorbedPumpJ[j] *= scale;
            }
        }

        /// <summary>A pump pulse deposited at once (bleaching resolved in 40 sub-steps; forward and optional return pass).</summary>
        public void PumpPulse(bool tally = true)
        {
            if (Pump.Kind != "pulsed") return;
            if (tally) Array.Clear(AbsorbedPumpJ, 0, Nz);
            double hv = H * PumpOmega / (2 * Math.PI); int S = 40;
            for (int s = 0; s < S; s++)
                for (int c = 0; c < Cells; c++)
                {
                    double F = Pump.EnergyJ * _pumpShape[c] / S;  // J/m^2
                    for (int pass = 0; pass < Pump.Passes; pass++)
                        for (int jj = 0; jj < Nz; jj++)
                        {
                            int j = pass == 0 ? jj : Nz - 1 - jj, k = j * Cells + c;
                            double a = PumpAlpha(k), dF = F * (1 - Math.Exp(-a * Dz));
                            N2[k] = Math.Max(0, Math.Min(Nt[k], N2[k] + dF / (hv * Dz)));
                            if (tally) AbsorbedPumpJ[j] += dF * (Basis == null ? 1 / _pumpShape[0] : CellArea[c]);
                            F -= dF;
                        }
                }
        }

        /// <summary>
        /// Relax the inversion towards the cw steady state with a continuous signal photon flux
        /// <paramref name="phiSignal"/> (photons / m^2 / s, cross sections <paramref name="sigmaE"/>, <paramref name="sigmaA"/>)
        /// on top of the pump (attenuated slice by slice with the current populations):
        /// N2_ss = (s_ap Phi_p + s_as Phi_s) N_t / ((s_ap + s_ep) Phi_p + (s_es + s_as) Phi_s + 1 / tau); N2 += a (N2_ss - N2).
        /// </summary>
        public void RelaxTowardsCw(double phiSignal, double sigmaE, double sigmaA, double a)
        {
            double tau = Medium.LifetimeS, hv = Pump.Kind == "cw" ? H * PumpOmega / (2 * Math.PI) : 1;
            var phiP = new double[N2.Length];
            if (Pump.Kind == "cw")
                for (int c = 0; c < Cells; c++)
                {
                    double I = Pump.PowerW * _pumpShape[c];
                    for (int j = 0; j < Nz; j++) { int k = j * Cells + c; double al = PumpAlpha(k), t = Math.Exp(-al * Dz); phiP[k] += I * (1 - t) / Math.Max(1e-300, al * Dz) / hv; I *= t; }
                    if (Pump.Passes == 2)
                        for (int j = Nz - 1; j >= 0; j--) { int k = j * Cells + c; double al = PumpAlpha(k), t = Math.Exp(-al * Dz); phiP[k] += I * (1 - t) / Math.Max(1e-300, al * Dz) / hv; I *= t; }
                }
            for (int k = 0; k < N2.Length; k++)
            {
                double num = (_sap * phiP[k] + sigmaA * phiSignal) * Nt[k];
                double den = (_sap + _sep) * phiP[k] + (sigmaE + sigmaA) * phiSignal + 1 / tau;
                double ss = num / den;
                N2[k] += a * (ss - N2[k]);
            }
        }

        public void Decay(double t) { if (t <= 0) return; double f = Math.Exp(-t / Medium.LifetimeS); for (int k = 0; k < N2.Length; k++) N2[k] *= f; }

        // ------------------------------------------------------------------------------------------ diagnostics

        /// <summary>Single-pass small-signal gain exp(integral g dz) at each omega for cell c.</summary>
        public double[] SmallSignalGain(double[] omega, int c = -1)
        {
            if (c < 0) c = AxisCell;
            var r = new double[omega.Length];
            for (int i = 0; i < omega.Length; i++)
            {
                double se = Medium.SigmaE(omega[i]), sa = Medium.SigmaA(omega[i]), s = 0;
                for (int j = 0; j < Nz; j++) { int k = j * Cells + c; s += (se * N2[k] - sa * (Nt[k] - N2[k])) * Dz; }
                r[i] = Math.Exp(s);
            }
            return r;
        }

        /// <summary>Energy stored above transparency at omega (J): sum (N2 - N_tr) h nu V, N_tr = N_t sigma_a / (sigma_a + sigma_e).</summary>
        public double ExtractableEnergy(double omega)
        {
            double se = Medium.SigmaE(omega), sa = Medium.SigmaA(omega), hv = H * omega / (2 * Math.PI), e = 0;
            for (int j = 0; j < Nz; j++) for (int c = 0; c < Cells; c++) { int k = j * Cells + c; e += Math.Max(0, N2[k] - Nt[k] * sa / (sa + se)) * hv * CellArea[c] * Dz; }
            return e;
        }

        /// <summary>Upper-state fraction on the axis, averaged over z.</summary>
        public double MeanInversionFraction() { int a = AxisCell; double s = 0, t = 0; for (int j = 0; j < Nz; j++) { s += N2[j * Cells + a]; t += Nt[j * Cells + a]; } return s / Math.Max(1e-300, t); }

        /// <summary>Upper-state fraction of every transverse cell, averaged over z.</summary>
        public double[] InversionVsCell() { var r = new double[Cells]; for (int c = 0; c < Cells; c++) { double s = 0, t = 0; for (int j = 0; j < Nz; j++) { s += N2[j * Cells + c]; t += Nt[j * Cells + c]; } r[c] = s / Math.Max(1e-300, t); } return r; }

        /// <summary>Index of the transverse cell closest to the axis (0 in 1-D).</summary>
        public int AxisCell { get { int b = 0; double r = double.MaxValue; for (int c = 0; c < Cells; c++) { double q = CellX[c] * CellX[c] + CellY[c] * CellY[c]; if (q < r) { r = q; b = c; } } return b; } }

        public double[] InversionVsZ(int c = -1) { if (c < 0) c = AxisCell; return InversionVsZAt(c); }
        private double[] InversionVsZAt(int c) => Enumerable.Range(0, Nz).Select(j => N2[j * Cells + c] / Math.Max(1e-300, Nt[j * Cells + c])).ToArray();

        // ------------------------------------------------------------------------------------------ thermal lens

        /// <summary>
        /// Steady-state temperature rise Delta T(r, z) (K) from the heat load = heatFraction x absorbed pump power
        /// (per slice, distributed over the pump profile), in a rod of radius <paramref name="rodRadiusM"/> held at
        /// the sink temperature (radial conduction only). Returns the index change as a field Delta n(r, z) = dn/dT
        /// Delta T and the thermal focal length (thin-lens equivalent of the parabolic part near the axis); with
        /// <paramref name="sliceInvF"/> (length Nz) also each slice's share 1/f_j (sum = 1/f_th). With
        /// <paramref name="modeRadiusM"/> the slice shares are the lens a Gaussian beam of that 1/e^2 radius sees: the
        /// parabola fitted with the beam's intensity as weight (least rms wavefront error over the beam), which is weaker
        /// than the on-axis lens when the beam fills the pumped region.
        /// </summary>
        public (ISpatialField Field, double FocalLengthM, double PeakDeltaTK, double HeatW) ThermalLens(double absorbedPowerScale, double heatFraction, double rodRadiusM, double[] sliceInvF = null, double modeRadiusM = double.NaN)
        {
            int nr = 400; double R = rodRadiusM, dr = R / nr;
            var rs = Enumerable.Range(0, nr + 1).Select(i => i * dr).ToArray();
            var vals = new double[Nz * (nr + 1)];
            double invF = 0, heat = 0, peak = 0;
            double wp = Pump.RadiusM;
            // normalised pump shape on the fine radial grid (integral = 1)
            var shape = rs.Select(r => Pump.Shape(r)).ToArray();
            for (int j = 0; j < Nz; j++)
            {
                double Pj = AbsorbedPumpJ[j] * absorbedPowerScale * heatFraction;   // W of heat in this slice
                heat += Pj;
                double qScale = Pj / Dz;                                            // W/m of heat, distributed by the shape (1/m^2)
                // dT/dr = -(1 / (K r)) integral_0^r q r' dr'
                var T = new double[nr + 1]; double cum = 0; var dTdr = new double[nr + 1];
                for (int i = 1; i <= nr; i++)
                {
                    double rm = 0.5 * (rs[i] + rs[i - 1]);
                    cum += qScale * Pump.Shape(rm) * rm * dr;
                    dTdr[i] = -cum / (Medium.ThermalConductivity * rs[i]);
                }
                T[nr] = 0;
                for (int i = nr - 1; i >= 0; i--) T[i] = T[i + 1] - 0.5 * (dTdr[i] + dTdr[i + 1]) * dr;
                for (int i = 0; i <= nr; i++) vals[j * (nr + 1) + i] = Medium.DnDT * T[i];
                peak = Math.Max(peak, T[0]);
                // parabolic fit Delta n = a + c r^2 near the axis (r < 0.3 wp)
                double s0 = 0, s1 = 0, s2 = 0, t0 = 0, t1 = 0; int cnt = 0;
                for (int i = 0; i <= nr && rs[i] <= 0.3 * wp; i++) { double u = rs[i] * rs[i], v = vals[j * (nr + 1) + i]; s0 += 1; s1 += u; s2 += u * u; t0 += v; t1 += u * v; cnt++; }
                double cfit = cnt > 2 ? (s0 * t1 - s1 * t0) / (s0 * s2 - s1 * s1) : 0;
                invF += -2 * cfit * Dz;
                if (sliceInvF != null)
                {
                    if (modeRadiusM > 0)
                    {
                        // intensity-weighted fit over the beam: weight exp(-2 r^2 / w^2) r dr
                        s0 = s1 = s2 = t0 = t1 = 0;
                        for (int i = 0; i <= nr; i++)
                        {
                            double u = rs[i] * rs[i], v = vals[j * (nr + 1) + i], wgt = Math.Exp(-2 * u / (modeRadiusM * modeRadiusM)) * rs[i];
                            s0 += wgt; s1 += wgt * u; s2 += wgt * u * u; t0 += wgt * v; t1 += wgt * u * v;
                        }
                        double cb = s0 * s2 - s1 * s1 > 0 ? (s0 * t1 - s1 * t0) / (s0 * s2 - s1 * s1) : 0;
                        sliceInvF[j] = -2 * cb * Dz;
                    }
                    else sliceInvF[j] = -2 * cfit * Dz;
                }
            }
            var zs = Enumerable.Range(0, Nz).Select(j => (j + 0.5) * Dz).ToArray();
            var field = new RadialZTableField(rs, zs, vals, "thermal lens (dn/dT x Delta T, " + Medium.Name + ")");
            return (field, invF != 0 ? 1 / invF : double.PositiveInfinity, peak, heat);
        }

        // ------------------------------------------------------------------------------------------ amplification

        /// <summary>Per-bin cross sections and the frame-referenced host wavenumber (1/m) for a grid.</summary>
        public sealed class GridData
        {
            public double[] SigmaE, SigmaA, KRel, OmegaAbs; public double K0; public FrequencyGrid Grid;
        }

        public GridData Prepare(FrequencyGrid g, Material host)
        {
            int n = g.N; var d = new GridData { Grid = g, SigmaE = new double[n], SigmaA = new double[n], KRel = new double[n], OmegaAbs = g.OmegaAbsolute };
            double w0 = g.OmegaCenter, dw = 1e-4 * w0;
            double k(double w) => host.IndexAt(w) * w / C;
            double k0 = k(w0), k1 = (k(w0 + dw) - k(w0 - dw)) / (2 * dw);
            d.K0 = k0;
            for (int i = 0; i < n; i++)
            {
                double w = g.OmegaAbsolute[i];
                d.SigmaE[i] = Medium.SigmaE(w); d.SigmaA[i] = Medium.SigmaA(w);
                double kk = w > 0 ? k(w) : double.NaN;
                d.KRel[i] = double.IsNaN(kk) ? 0 : kk - k0 - k1 * (w - w0);
            }
            return d;
        }

        /// <summary>Statistics of one pass.</summary>
        public sealed class PassStats { public double BIntegralRad, PhotonsGained, EnergyGainedJ, AseSeedJ; }

        /// <summary>
        /// Arrival-ordered groups of frequency bins for time-ordered saturation: the bins are sorted by their group
        /// delay (derivative of the spectral phase) and cut into <paramref name="K"/> groups of equal energy. For a
        /// chirped pulse, time and frequency map onto each other, so processing the groups one after the other
        /// lets the leading colours deplete the inversion before the trailing ones arrive (the spectral reshaping of
        /// chirped-pulse amplification). K = 1: all bins see the same inversion (Frantz-Nodvik per slice).
        /// </summary>
        public static int[][] ArrivalGroups(Complex[] ax, Complex[] ay, FrequencyGrid g, int K)
        {
            int n = g.N;
            if (K <= 1) return null;
            var e = new double[n]; double mx = 0;
            for (int i = 0; i < n; i++) { e[i] = ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary + (ay != null ? ay[i].Real * ay[i].Real + ay[i].Imaginary * ay[i].Imaginary : 0); mx = Math.Max(mx, e[i]); }
            if (mx <= 0) return null;
            // phase of the dominant component, unwrapped over the support
            var ph = new double[n];
            for (int i = 0; i < n; i++) { var z = ay != null && e[i] > 0 && (ay[i].Magnitude > ax[i].Magnitude) ? ay[i] : ax[i]; ph[i] = Math.Atan2(z.Imaginary, z.Real); }
            ph = PhaseUnwrap.Unwrap(ph);
            var tau = new double[n]; double thr = 1e-8 * mx;
            for (int i = 0; i < n; i++)
            {
                if (e[i] < thr || i == 0 || i == n - 1) { tau[i] = double.PositiveInfinity; continue; }
                tau[i] = (ph[i + 1] - ph[i - 1]) / (2 * g.DOmega);
            }
            var order = Enumerable.Range(0, n).OrderBy(i => tau[i]).ToArray();
            double tot = 0; foreach (int i in order) if (!double.IsInfinity(tau[i])) tot += e[i];
            var groups = new List<int>[K]; for (int k = 0; k < K; k++) groups[k] = new List<int>();
            double cum = 0;
            foreach (int i in order)
            {
                int k = double.IsInfinity(tau[i]) ? K - 1 : Math.Min(K - 1, (int)(cum / tot * K));
                groups[k].Add(i);
                if (!double.IsInfinity(tau[i])) cum += e[i];
            }
            return groups.Where(x => x.Count > 0).Select(x => x.ToArray()).ToArray();
        }

        /// <summary>
        /// One pass of a 1-D pulse (spectra ax, ay on <paramref name="d"/>'s grid) through the crystal: per slice a
        /// half step of host dispersion, gain with time-ordered saturation (see <see cref="ArrivalGroups"/>),
        /// SPM (n2 of the host on the mode area, applied in as many sub-steps as the B-integral needs) and the other
        /// half step. N2 is depleted photon by photon (exact energy bookkeeping).
        /// </summary>
        public PassStats Pass1D(Complex[] ax, Complex[] ay, GridData d, double n2, int segments, bool reverse)
        {
            var g = d.Grid; int n = g.N; double dz = Dz, A = CellArea[0];
            var st = new PassStats();
            bool hasY = ay != null && ay.Any(z => z != Complex.Zero);
            var groups = ArrivalGroups(ax, hasY ? ay : null, g, segments);
            var half = new Complex[n]; for (int i = 0; i < n; i++) half[i] = Complex.FromPolarCoordinates(1, d.KRel[i] * dz / 2);
            // SPM sub-steps from the B-integral estimate of the pass (peak power at the entrance, unsaturated gain)
            int spmEvery = 0; double kerr = (g.OmegaCenter / C) * n2;
            if (n2 != 0)
            {
                double pk = PeakPower(ax, hasY ? ay : null);
                double G = SmallSignalGain(new[] { g.OmegaCenter })[0];
                double Best = kerr * pk / A * LengthM * Math.Max(1, G);
                int steps = Best < 1e-4 ? 0 : Math.Max(1, Math.Min(Nz, (int)Math.Ceiling(Best / 0.05)));
                spmEvery = steps == 0 ? 0 : Math.Max(1, Nz / steps);
            }
            int since = 0;
            for (int jj = 0; jj < Nz; jj++)
            {
                int j = reverse ? Nz - 1 - jj : jj;
                for (int i = 0; i < n; i++) { ax[i] *= half[i]; if (hasY) ay[i] *= half[i]; }
                st.PhotonsGained += GainStep(ax, hasY ? ay : null, g, d, j, 0, A, dz, groups, out double eg); st.EnergyGainedJ += eg;
                if (Ase != null) st.AseSeedJ += AddAse(ax, g, d, j, dz);
                since++;
                if (spmEvery > 0 && (since == spmEvery || jj == Nz - 1))
                {
                    double L = since * dz; since = 0;
                    var tx = SpectralTransform.FreqToTime(ax); var ty = hasY ? SpectralTransform.FreqToTime(ay) : null;
                    double pk = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double P = tx[i].Real * tx[i].Real + tx[i].Imaginary * tx[i].Imaginary + (hasY ? ty[i].Real * ty[i].Real + ty[i].Imaginary * ty[i].Imaginary : 0);
                        double ph = kerr * P / A * L; pk = Math.Max(pk, ph);
                        var r = Complex.FromPolarCoordinates(1, ph); tx[i] *= r; if (hasY) ty[i] *= r;
                    }
                    st.BIntegralRad += pk;
                    Array.Copy(SpectralTransform.TimeToFreq(tx), ax, n);
                    if (hasY) Array.Copy(SpectralTransform.TimeToFreq(ty), ay, n);
                }
                for (int i = 0; i < n; i++) { ax[i] *= half[i]; if (hasY) ay[i] *= half[i]; }
            }
            if (n2 != 0 && spmEvery == 0) st.BIntegralRad += kerr * PeakPower(ax, hasY ? ay : null) / A * LengthM;   // negligible: reported only
            return st;
        }

        /// <summary>Spontaneous emission into the signal mode (one spatial mode, the signal polarisation): returns its energy.</summary>
        private double AddAse(Complex[] ax, FrequencyGrid g, GridData d, int j, double dz)
        {
            int n = g.N; double n2 = N2[j * Cells], tot = 0;
            for (int i = 0; i < n; i++)
            {
                double w = d.OmegaAbs[i], se = d.SigmaE[i];
                if (w <= 0 || se <= 0) continue;
                double e = Hbar * w * se * n2 * dz;            // mean energy added to this bin (J)
                double u1 = Math.Max(1e-300, Ase.NextDouble()), u2 = Ase.NextDouble();
                double amp = Math.Sqrt(-Math.Log(u1) * e * n / g.DTime);
                var z = Complex.FromPolarCoordinates(amp, 2 * Math.PI * u2);
                double before = ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary;
                ax[i] += z;
                tot += (ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary - before) * g.DTime / n;
            }
            return tot;
        }

        private static double PeakPower(Complex[] ax, Complex[] ay)
        {
            var tx = SpectralTransform.FreqToTime(ax); var ty = ay != null ? SpectralTransform.FreqToTime(ay) : null; double pk = 0;
            for (int i = 0; i < tx.Length; i++) pk = Math.Max(pk, tx[i].Real * tx[i].Real + tx[i].Imaginary * tx[i].Imaginary + (ty != null ? ty[i].Real * ty[i].Real + ty[i].Imaginary * ty[i].Imaginary : 0));
            return pk;
        }

        /// <summary>
        /// Gain on slice j, cell c for a field given by its spectrum (in place). The groups of bins (arrival order;
        /// null = all at once) are amplified one after the other, each depleting N2 before the next. Within a group
        /// the small-signal exponents are scaled so that the energy gain obeys Frantz-Nodvik for the slice.
        /// Returns the photons gained; <paramref name="energy"/> the energy gained (J, or J/m^2 for spatial fields).
        /// </summary>
        internal double GainStep(Complex[] sx, Complex[] sy, FrequencyGrid g, GridData d, int j, int c, double area, double dz, int[][] groups, out double energy)
        {
            int n = g.N, k = j * Cells + c; double ntot = Nt[k], norm = g.DTime / n;
            energy = 0; double photons = 0;
            int G = groups?.Length ?? 1;
            for (int s = 0; s < G; s++)
            {
                int[] idx = groups?[s];
                int cnt = idx?.Length ?? n;
                double n2 = N2[k], E0 = 0, Ess = 0, sig = 0, hv = 0;
                for (int q = 0; q < cnt; q++)
                {
                    int i = idx != null ? idx[q] : q;
                    double se = d.SigmaE[i], sa = d.SigmaA[i], w = d.OmegaAbs[i];
                    if ((se == 0 && sa == 0) || w <= 0) continue;
                    double e0 = (sx[i].Real * sx[i].Real + sx[i].Imaginary * sx[i].Imaginary + (sy != null ? sy[i].Real * sy[i].Real + sy[i].Imaginary * sy[i].Imaginary : 0)) * norm;
                    if (e0 == 0) continue;
                    double gz = (se * n2 - sa * (ntot - n2)) * dz;
                    E0 += e0; Ess += e0 * Math.Exp(gz); sig += e0 * (se + sa); hv += e0 * Hbar * w;
                }
                double scale = E0 > 0 ? FrantzNodvikScale(E0 / area, hv / sig, Ess / E0) : 1;
                double gained = 0, eg = 0;
                for (int q = 0; q < cnt; q++)
                {
                    int i = idx != null ? idx[q] : q;
                    double se = d.SigmaE[i], sa = d.SigmaA[i], w = d.OmegaAbs[i];
                    if ((se == 0 && sa == 0) || w <= 0) continue;
                    double gz = (se * n2 - sa * (ntot - n2)) * dz;
                    double f = Math.Exp(0.5 * gz * scale);
                    double e0 = (sx[i].Real * sx[i].Real + sx[i].Imaginary * sx[i].Imaginary + (sy != null ? sy[i].Real * sy[i].Real + sy[i].Imaginary * sy[i].Imaginary : 0)) * norm;
                    sx[i] *= f; if (sy != null) sy[i] *= f;
                    gained += e0 * (f * f - 1) / (Hbar * w); eg += e0 * (f * f - 1);
                }
                // one ion per stimulated photon (net of signal reabsorption)
                N2[k] = Math.Max(0, Math.Min(ntot, n2 - gained / (area * dz)));
                photons += gained; energy += eg;
            }
            return photons;
        }

        /// <summary>
        /// Factor s on the small-signal exponents of a slice so that its energy gain matches Frantz-Nodvik:
        /// G = ln(1 + G0 (e^x - 1)) / x with x = F / F_sat; s = ln G / ln G0.
        /// </summary>
        public static double FrantzNodvikScale(double fluence, double fsat, double g0)
        {
            if (!(g0 > 0) || Math.Abs(Math.Log(g0)) < 1e-12 || !(fsat > 0)) return 1;
            double x = fluence / fsat;
            if (x < 1e-9) return 1;
            double lnArg = x < 700 ? Math.Log(1 + g0 * (Math.Exp(x) - 1)) : x + Math.Log(g0);
            if (!(lnArg > 0)) return 1;
            double lnG = Math.Log(lnArg / x);
            double s = lnG / Math.Log(g0);
            return Math.Max(0, Math.Min(1, s));
        }

        // ------------------------------------------------------------------------------------------ fast (energy) model

        /// <summary>Coarse spectral bins for the pulse-train iteration: energy-weighted frequency and cross sections.</summary>
        public sealed class Bins { public double[] Omega, SigmaE, SigmaA, Energy; }

        /// <summary>Group a spectrum (energy per grid bin, J) into at most <paramref name="nb"/> bins over its support.</summary>
        public Bins MakeBins(double[] omegaAbs, double[] energy, int nb = 96)
        {
            double mx = energy.Max(); int lo = 0, hi = energy.Length - 1;
            while (lo < hi && energy[lo] < 1e-7 * mx) lo++; while (hi > lo && energy[hi] < 1e-7 * mx) hi--;
            int cnt = Math.Max(1, Math.Min(nb, hi - lo + 1)); double width = (hi - lo + 1) / (double)cnt;
            var b = new Bins { Omega = new double[cnt], SigmaE = new double[cnt], SigmaA = new double[cnt], Energy = new double[cnt] };
            var ws = new double[cnt];
            for (int i = lo; i <= hi; i++) { int q = Math.Min(cnt - 1, (int)((i - lo) / width)); b.Energy[q] += energy[i]; ws[q] += energy[i] * omegaAbs[i]; }
            for (int q = 0; q < cnt; q++)
            {
                double w = b.Energy[q] > 0 ? ws[q] / b.Energy[q] : omegaAbs[lo + (int)((q + 0.5) * width)];
                b.Omega[q] = w; b.SigmaE[q] = Medium.SigmaE(w); b.SigmaA[q] = Medium.SigmaA(w);
            }
            return b;
        }

        /// <summary>
        /// One pass of a pulse given only by its energy per bin (J, or J/m^2 with area 1) through cell c: per slice
        /// the Frantz-Nodvik-scaled spectral gain and the matching depletion. No phase, no time ordering -- used to
        /// iterate the pulse train to steady state cheaply; the last pulse is then run with the full model.
        /// Returns the energy gained.
        /// </summary>
        public double FastPass(double[] e, Bins b, int c, double area, bool reverse)
        {
            int nb = e.Length; double gained = 0; var gx = new double[nb];
            for (int jj = 0; jj < Nz; jj++)
            {
                int j = reverse ? Nz - 1 - jj : jj, k = j * Cells + c; double n2 = N2[k], nt = Nt[k];
                double E0 = 0, Ess = 0, sig = 0, hv = 0;
                for (int q = 0; q < nb; q++)
                {
                    gx[q] = (b.SigmaE[q] * n2 - b.SigmaA[q] * (nt - n2)) * Dz;
                    E0 += e[q]; Ess += e[q] * Math.Exp(gx[q]); sig += e[q] * (b.SigmaE[q] + b.SigmaA[q]); hv += e[q] * Hbar * b.Omega[q];
                }
                if (E0 <= 0 || sig <= 0) continue;
                double s = FrantzNodvikScale(E0 / area, hv / sig, Ess / E0), photons = 0;
                for (int q = 0; q < nb; q++) { double de = e[q] * (Math.Exp(gx[q] * s) - 1); e[q] += de; gained += de; photons += de / (Hbar * b.Omega[q]); }
                N2[k] = Math.Max(0, Math.Min(nt, n2 - photons / (area * Dz)));
            }
            return gained;
        }

        /// <summary>
        /// One pass of a spatial field E (k-space layout [m * n + i] on <see cref="Basis"/>) through the crystal:
        /// per slice a half step of the linear operator (host dispersion + diffraction), gain per transverse cell,
        /// SPM, position-dependent Delta n / absorption (thermal lens, user profiles), and the other half step.
        /// </summary>
        public PassStats PassSpatial(Complex[] Ek, GridData d, Complex[] halfStepK, double n2, SpatialProperties props, int segments, bool reverse)
        {
            var g = d.Grid; int n = g.N, M = Basis.M; double dz = Dz;
            var st = new PassStats(); object lk = new object();
            var omc = g.OmegaAbsolute.Select(w => w / C).ToArray();
            int[][] groups = null;
            for (int jj = 0; jj < Nz; jj++)
            {
                int j = reverse ? Nz - 1 - jj : jj; double z = (j + 0.5) * dz;
                for (int q = 0; q < Ek.Length; q++) Ek[q] *= halfStepK[q];
                Basis.ToSpace(Ek, n);
                if (jj == 0 && segments > 1)
                {
                    // arrival order from the most energetic transverse point
                    int best = 0; double be = -1;
                    for (int m = 0; m < M; m++) { double e = 0; for (int i = 0; i < n; i++) e += Ek[m * n + i].Real * Ek[m * n + i].Real + Ek[m * n + i].Imaginary * Ek[m * n + i].Imaginary; e *= CellArea[m]; if (e > be) { be = e; best = m; } }
                    var row0 = new Complex[n]; Array.Copy(Ek, best * n, row0, 0, n);
                    groups = ArrivalGroups(row0, null, g, segments);
                }
                double[] dn = props?.IndexChange != null ? props.IndexChange.AtPoints(Basis.X, Basis.Y, z) : null;
                double[] al = props?.Absorption != null ? props.Absorption.AtPoints(Basis.X, Basis.Y, z) : null;
                double[] n2s = props?.N2Scale != null ? props.N2Scale.AtPoints(Basis.X, Basis.Y, z) : null;
                double maxB = 0, photons = 0, egain = 0;
                Parallel.For(0, M, () => (0.0, 0.0, 0.0), (m, _, acc) =>
                {
                    var row = new Complex[n]; Array.Copy(Ek, m * n, row, 0, n);
                    if (dn != null || al != null)
                        for (int i = 0; i < n; i++) row[i] *= Complex.Exp(new Complex(-(al != null ? 0.5 * al[m] : 0) * dz, (dn != null ? omc[i] * dn[m] : 0) * dz));
                    // spatial fields are sqrt(W/m^2): photons and energy per m^2 of this point
                    double ph = GainStep(row, null, g, d, j, m, 1.0, dz, groups, out double eg) * CellArea[m]; eg *= CellArea[m];
                    double pk = 0;
                    if (n2 != 0)
                    {
                        SpectralTransform.FreqToTimeInPlace(row);
                        double sc = n2s != null ? n2s[m] : 1;
                        for (int i = 0; i < n; i++) { double I = row[i].Real * row[i].Real + row[i].Imaginary * row[i].Imaginary, phi = (g.OmegaCenter / C) * sc * n2 * I * dz; row[i] *= Complex.FromPolarCoordinates(1, phi); pk = Math.Max(pk, phi); }
                        SpectralTransform.TimeToFreqInPlace(row);
                    }
                    Array.Copy(row, 0, Ek, m * n, n);
                    return (Math.Max(acc.Item1, pk), acc.Item2 + ph, acc.Item3 + eg);
                }, acc => { lock (lk) { maxB = Math.Max(maxB, acc.Item1); photons += acc.Item2; egain += acc.Item3; } });
                st.BIntegralRad += maxB; st.PhotonsGained += photons; st.EnergyGainedJ += egain;
                Basis.ToK(Ek, n);
                for (int q = 0; q < Ek.Length; q++) Ek[q] *= halfStepK[q];
            }
            return st;
        }
    }

    /// <summary>Delta n(r, z) on a fine radial grid (thermal lens), linear interpolation, zero beyond the rod.</summary>
    public sealed class RadialZTableField : ISpatialField
    {
        private readonly double[] _r, _z, _v; private readonly string _d;
        public RadialZTableField(double[] r, double[] z, double[] v, string description) { _r = r; _z = z; _v = v; _d = description; }
        public string Description => _d;
        public bool DependsOnZ => _z.Length > 1;
        public double PeakAt(int j) => _v[j * _r.Length];
        public void Evaluate(double[] x, double[] y, double[] z, double[] o)
        {
            int nr = _r.Length, nz = _z.Length; double dr = _r[1] - _r[0];
            for (int i = 0; i < o.Length; i++)
            {
                double r = Math.Sqrt(x[i] * x[i] + y[i] * y[i]);
                double fz = nz == 1 ? 0 : Math.Max(0, Math.Min(nz - 1, (z[i] - _z[0]) / (_z[1] - _z[0])));
                int jz = Math.Min(nz - 2 < 0 ? 0 : nz - 2, (int)fz); double tz = nz == 1 ? 0 : fz - jz;
                double fr = r / dr; if (fr >= nr - 1) { o[i] = _v[jz * nr + nr - 1]; continue; }
                int ir = (int)fr; double tr = fr - ir;
                double a = _v[jz * nr + ir] * (1 - tr) + _v[jz * nr + ir + 1] * tr;
                if (nz > 1) { double b = _v[(jz + 1) * nr + ir] * (1 - tr) + _v[(jz + 1) * nr + ir + 1] * tr; a = a * (1 - tz) + b * tz; }
                o[i] = a;
            }
        }
    }
}
