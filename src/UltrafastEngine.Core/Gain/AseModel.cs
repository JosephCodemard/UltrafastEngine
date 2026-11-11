// AseModel.cs -- amplified spontaneous emission over the inversion lifetime (quasi-static, both directions, spectrally resolved).
//
// What it does. Between pulses (milliseconds) the upper-state density N2(z, cell) is steady on the time scale of the ASE
// transit (picoseconds), so the ASE is solved as a boundary-value problem in z at frozen N2 and the result depletes N2 in the rate
// equations of GainCrystal (PumpCw, Decay). Per transverse cell c (1-D: one cell) and per frequency nu_i of the emission band the
// intensity I (W / m^2 / Hz) in a bundle of etendue q = n^2 Omega / lambda0^2 (1 / m^2) grows as
//     dI+- / dz = +- gamma I + p h nu sigma_e N2 q,   gamma = sigma_e N2 - sigma_a (N_t - N2)
// (p = 1 or 2 polarisations). Forward I+ starts at 0 at z = 0, backward I- at z = L. For q = 1 / A_mode and p = 2 this is the
// textbook single-mode ASE of an amplifier, P = n_sp h nu dnu (G - 1) per polarisation (Desurvire 1994; Siegman 1986, ch. 6); for
// Omega = 4 pi the source integrates to N2 h nu / tau_rad by the Fuchtbauer-Ladenburg relation (Aull and Jenssen 1982), which fixes the factors.
//
// Classes:
//   AseSpec   -- the user's settings (solid angle, polarisations, band, points).
//   AseModel  -- the solver: Rates(...) gives the extra decay rate of every (slice, cell) for GainCrystal; ModeOutput(...) gives the
//                ASE leaving the signal mode (for the contrast pedestal); Slice(...) is the exact slice step (public for tests).
// Key signatures:
//   public double[] Rates(double[] n2, double[] nt)                     -> W[k] (1/s) = stimulated photons lost per ion per second
//   public AseOutput ModeOutput(double[] n2, double[] nt, int cell)    -> forward / backward power spectra in the signal mode
//   public static double Slice(double iIn, double gamma, double source, double dz, out double stim)  -> I after one slice, exact for constant gamma
// Not modelled: transverse spreading of the rays between cells (each cell is an independent column), reflections at the faces
// (so no parasitic lasing), pump-light fluorescence trapping. See docs/reference/gain.md#ase.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Gain
{
    /// <summary>Settings of the inter-pulse ASE model (<c>aseModel</c> on a gain or amplifier element).</summary>
    public sealed class AseSpec
    {
        public bool Enabled;
        /// <summary>Effective solid angle (sr, inside the medium) of the rays that depopulate the inversion; NaN = min(pi r_pump^2 / L^2, escape cone).</summary>
        public double SolidAngleSr = double.NaN;
        public int Polarizations = 2;
        public int Points = 40;
        public double BandLowerM = double.NaN, BandUpperM = double.NaN;

        public static AseSpec FromJson(JsonValue v)
        {
            var s = new AseSpec();
            if (v == null || v.IsNull) return s;
            if (v.Kind == JsonKind.Bool) { s.Enabled = v.AsBool(); return s; }
            var o = v.AsObject();
            s.Enabled = o.GetBool("enabled", true);
            s.SolidAngleSr = o.GetDouble("solidAngleSr", double.NaN);
            s.Polarizations = o.GetInt("polarizations", 2);
            s.Points = o.GetInt("points", 40);
            s.BandLowerM = o.GetDouble("bandLowerM", double.NaN); s.BandUpperM = o.GetDouble("bandUpperM", double.NaN);
            if (s.Polarizations != 1 && s.Polarizations != 2) throw new ArgumentException("aseModel.polarizations must be 1 or 2");
            if (s.Points < 4) throw new ArgumentException("aseModel.points must be at least 4");
            return s;
        }
    }

    /// <summary>ASE leaving the signal mode at the two end faces, per frequency (W / Hz), and its integral.</summary>
    public sealed class AseOutput
    {
        public double[] NuHz, WavelengthM, ForwardWPerHz, BackwardWPerHz;
        public double ForwardW, BackwardW;
    }

    public sealed class AseModel
    {
        const double H = GainMedium.H, C = GainMedium.C;
        private readonly GainCrystal _gc;
        public readonly AseSpec Spec;
        public double[] Nu { get; }                 // Hz
        public double DNu { get; }                  // Hz
        public double[] SigmaE { get; }
        public double[] SigmaA { get; }
        public double HostIndex { get; }
        public double SolidAngleSr { get; }
        /// <summary>Last evaluation: ASE power added by stimulated emission summed over the crystal (W) and the photon rate (1/s).</summary>
        public double LastStimulatedPowerW { get; private set; }
        public double LastStimulatedPhotonsPerS { get; private set; }

        /// <param name="nuOverride">Tests: use these frequencies (Hz) with spacing <paramref name="dnuOverride"/> instead of the band of the medium.</param>
        public AseModel(GainCrystal gc, AseSpec spec, double hostIndex, double signalOmega, double[] nuOverride = null, double dnuOverride = double.NaN)
        {
            _gc = gc; Spec = spec; HostIndex = hostIndex;
            var m = gc.Medium;
            if (nuOverride != null) { Nu = nuOverride; DNu = dnuOverride; }
            else
            {
                double ls = 2 * Math.PI * C / signalOmega, lo = double.IsNaN(spec.BandLowerM) ? 0.75 * ls : spec.BandLowerM, hi = double.IsNaN(spec.BandUpperM) ? 1.35 * ls : spec.BandUpperM;
                // the band where the emission cross section matters: sigma_e above 1e-3 of its maximum
                int ns = 1201; var lam = new double[ns]; var se = new double[ns]; double mx = 0;
                for (int i = 0; i < ns; i++) { lam[i] = lo + (hi - lo) * i / (ns - 1); se[i] = m.SigmaE(GainMedium.Omega(lam[i])); mx = Math.Max(mx, se[i]); }
                int a = 0, b = ns - 1;
                if (double.IsNaN(spec.BandLowerM)) while (a < b && se[a] < 1e-3 * mx) a++;
                if (double.IsNaN(spec.BandUpperM)) while (b > a && se[b] < 1e-3 * mx) b--;
                double nuLo = C / lam[b], nuHi = C / lam[a];
                if (!(nuHi > nuLo)) { nuLo = C / hi; nuHi = C / lo; }
                int K = spec.Points; DNu = (nuHi - nuLo) / K;
                Nu = Enumerable.Range(0, K).Select(i => nuLo + (i + 0.5) * DNu).ToArray();
            }
            SigmaE = Nu.Select(v => m.SigmaE(2 * Math.PI * v)).ToArray();
            SigmaA = Nu.Select(v => m.SigmaA(2 * Math.PI * v)).ToArray();
            if (!double.IsNaN(spec.SolidAngleSr)) SolidAngleSr = spec.SolidAngleSr;
            else
            {
                double rp = gc.Pump.RadiusM > 0 ? gc.Pump.RadiusM : Math.Sqrt(gc.CellArea.Sum() / Math.PI);
                double geo = Math.PI * rp * rp / (gc.LengthM * gc.LengthM), esc = 2 * Math.PI * (1 - Math.Sqrt(1 - 1 / (hostIndex * hostIndex)));
                SolidAngleSr = Math.Min(geo, esc);
            }
        }

        /// <summary>Etendue factor q = n^2 Omega / lambda0^2 (1 / m^2) at frequency nu for the bundle of solid angle omega (sr inside the medium).</summary>
        public double Etendue(double nu, double omegaSr) { double l0 = C / nu; return HostIndex * HostIndex * omegaSr / (l0 * l0); }

        /// <summary>
        /// One slice of length dz at constant gain gamma (1/m) and source S (W / m^3 / Hz, per m of path): I_out = I_in e^{gamma dz} + S (e^{gamma dz} - 1) / gamma.
        /// stim = I_out - I_in - S dz, the intensity added by stimulated emission (net of absorption): the photon balance of the slice.
        /// </summary>
        public static double Slice(double iIn, double gamma, double source, double dz, out double stim)
        {
            double x = gamma * dz, e = Math.Exp(x);
            double iOut = iIn * e + source * dz * (Math.Abs(x) < 1e-8 ? 1 + 0.5 * x : (e - 1) / x);
            stim = iOut - iIn - source * dz;
            return iOut;
        }

        /// <summary>Integrate one column (cell) at frozen N2: I+ and I- at the entrance of every slice j are not needed, only the stimulated photons per slice.
        /// Returns the stimulated photons per slice (1/s/m^2 of the column, J-free: W / m^2 divided by h nu, summed over nu) and the end intensities.</summary>
        private void Column(double[] n2, double[] nt, int cell, double omegaSr, double areaForPower,
                            double[] stimPhotons, double[] stimPower, double[] fwdEnd, double[] bwdEnd)
        {
            int Nz = _gc.Nz, Cells = _gc.Cells; double dz = _gc.Dz; int K = Nu.Length; double p = Spec.Polarizations;
            if (stimPhotons != null) Array.Clear(stimPhotons, 0, Nz);
            if (stimPower != null) Array.Clear(stimPower, 0, Nz);
            for (int i = 0; i < K; i++)
            {
                double hv = H * Nu[i], q = Etendue(Nu[i], omegaSr), se = SigmaE[i], sa = SigmaA[i];
                for (int dir = 0; dir < 2; dir++)
                {
                    double I = 0;
                    for (int jj = 0; jj < Nz; jj++)
                    {
                        int j = dir == 0 ? jj : Nz - 1 - jj, k = j * Cells + cell;
                        double N2 = n2[k], N1 = nt[k] - N2;
                        double gamma = se * N2 - sa * N1, src = p * hv * se * N2 * q;
                        I = Slice(I, gamma, src, dz, out double stim);
                        // stim is the W / m^2 / Hz added over dz: photons per volume per second = stim / (h nu dz)
                        if (stimPhotons != null) stimPhotons[j] += DNu * stim / (hv * dz);
                        if (stimPower != null) stimPower[j] += DNu * stim * areaForPower;
                    }
                    if (dir == 0 && fwdEnd != null) fwdEnd[i] = I * areaForPower; else if (dir == 1 && bwdEnd != null) bwdEnd[i] = I * areaForPower;
                }
            }
        }

        /// <summary>
        /// Extra decay rate W[k] (1/s) of the upper state of every (slice, cell) from the stimulated ASE photons at the given N2 (the
        /// spontaneous part is already in 1 / tau): W = (stimulated photons per volume per second) / N2. Updates LastStimulated*.
        /// </summary>
        public double[] Rates(double[] n2, double[] nt)
        {
            int Nz = _gc.Nz, Cells = _gc.Cells; var W = new double[n2.Length];
            double totP = 0, totPh = 0; object lk = new object();
            Parallel.For(0, Cells, () => (0.0, 0.0), (c, _, acc) =>
            {
                var ph = new double[Nz]; var pw = new double[Nz];
                Column(n2, nt, c, SolidAngleSr, _gc.CellArea[c], ph, pw, null, null);
                double a = acc.Item1, b = acc.Item2;
                for (int j = 0; j < Nz; j++)
                {
                    int k = j * Cells + c;
                    W[k] = n2[k] > 0 ? ph[j] / n2[k] : 0;
                    a += pw[j]; b += ph[j] * _gc.CellArea[c] * _gc.Dz;
                }
                return (a, b);
            }, acc => { lock (lk) { totP += acc.Item1; totPh += acc.Item2; } });
            LastStimulatedPowerW = totP; LastStimulatedPhotonsPerS = totPh;
            return W;
        }

        /// <summary>ASE leaving the signal mode (one spatial mode of area <paramref name="modeArea"/>, the model's polarisations) through each end face, from the column of <paramref name="cell"/>.</summary>
        public AseOutput ModeOutput(double[] n2, double[] nt, int cell, double modeArea)
        {
            int K = Nu.Length; var f = new double[K]; var b = new double[K];
            // etendue of exactly one mode: n^2 Omega / lambda0^2 = 1 / A, i.e. Omega = lambda0^2 / (n^2 A); the power is intensity x A
            var sav = Spec.Polarizations;
            var o = new AseOutput { NuHz = Nu, WavelengthM = Nu.Select(v => C / v).ToArray(), ForwardWPerHz = f, BackwardWPerHz = b };
            int Nz = _gc.Nz, Cells = _gc.Cells; double dz = _gc.Dz, p = Spec.Polarizations;
            for (int i = 0; i < K; i++)
            {
                double hv = H * Nu[i], se = SigmaE[i], sa = SigmaA[i], q = 1 / modeArea;
                for (int dir = 0; dir < 2; dir++)
                {
                    double I = 0;
                    for (int jj = 0; jj < Nz; jj++)
                    {
                        int j = dir == 0 ? jj : Nz - 1 - jj, k = j * Cells + cell;
                        double N2 = n2[k], N1 = nt[k] - N2;
                        I = Slice(I, se * N2 - sa * N1, p * hv * se * N2 * q, dz, out _);
                    }
                    if (dir == 0) f[i] = I * modeArea; else b[i] = I * modeArea;
                }
            }
            o.ForwardW = f.Sum() * DNu; o.BackwardW = b.Sum() * DNu;
            return o;
        }
    }
}
