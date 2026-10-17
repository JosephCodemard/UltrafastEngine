using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Instruments
{
    // Virtual instruments read quantities no real device measures directly (the complete complex field, its
    // absolute phase, distributions in time and frequency at once, the field at every point of the beam in time).
    // They are marked "virtual": true in their values so that results are never mistaken for lab measurements.

    /// <summary>
    /// "wigner" (virtual): the Wigner distribution W(t, omega) of the exact field -- the time-frequency picture of
    /// chirp, sub-pulses and spectral interference, with the exact marginals |A(t)|^2 and |A(omega)|^2.
    /// "points" (default 256) sets the time samples; "gateFwhmS" &gt; 0 turns it into a spectrogram with a Gaussian
    /// gate (always positive, lower resolution). Negative W values are physical (interference terms).
    /// </summary>
    public sealed class WignerInstrument : IInstrument
    {
        public string Kind => "wigner";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            int pts = Math.Max(32, Math.Min(1024, ctx.Spec.GetInt("points", 256)));
            // a grid resolving the whole pulse (chirped pulses need many samples); W is evaluated only on <= pts
            // time rows and then reduced to <= 2 pts frequency columns over the spectral support
            var cp = Tools.Compact(ctx.Pulse, ctx.Spec.GetInt("maxN", 65536), out var note); if (note != null) o.Notes.Add(note);
            var g = cp.Grid; int n = g.N, n2 = 2 * n;
            bool hasY = cp.Ay.Any(z => z != Complex.Zero);
            // 2x oversampled time envelope (zero-padded spectrum): the discrete kernel uses A(t +- k dt / 2)
            var ex = new Complex[n2]; var ey = new Complex[n2];
            for (int i = 0; i < n; i++) { ex[i + n / 2] = cp.Ax[i]; ey[i + n / 2] = cp.Ay[i]; }
            SpectralTransform.FreqToTimeInPlace(ex); SpectralTransform.FreqToTimeInPlace(ey);
            double dt2 = g.DTime / 2, gate = ctx.Spec.GetDouble("gateFwhmS", 0);
            var pw = PulseMetrics.TemporalPower(cp);
            var (r0, r1) = PulseMetrics.SupportRange(pw, 1e-4, 2);
            int rs = Math.Max(1, (r1 - r0 + pts) / pts);
            var rows = new List<int>(); for (int i = r0; i <= r1; i += rs) rows.Add(i);
            int R = rows.Count;
            var Wfull = new double[R * n2];
            System.Threading.Tasks.Parallel.For(0, R, ri =>
            {
                int c = 2 * rows[ri]; var kern = new Complex[n2];
                for (int k = -n2 / 2; k < n2 / 2; k++)
                {
                    if (gate > 0)
                    {
                        int idx = c + k; if (idx < 0 || idx >= n2) continue;
                        double tau = k * dt2; kern[k + n2 / 2] = ex[idx] * Math.Exp(-2 * Math.Log(2) * tau * tau / (gate * gate));
                        continue;
                    }
                    int a2 = c + k, b2 = c - k;
                    if (a2 < 0 || a2 >= n2 || b2 < 0 || b2 >= n2) continue;
                    var v = ex[a2] * Complex.Conjugate(ex[b2]);
                    if (hasY) v += ey[a2] * Complex.Conjugate(ey[b2]);
                    kern[k + n2 / 2] = v;
                }
                SpectralTransform.TimeToFreqInPlace(kern);   // sum_k kern(k) exp(+i Omega k dt)
                for (int l = 0; l < n2; l++) Wfull[ri * n2 + l] = gate > 0 ? kern[l].Magnitude * kern[l].Magnitude : kern[l].Real;
            });
            double dOm = 2 * Math.PI / (n2 * g.DTime);   // tau = 2 k dt2 = k dt
            var colW = new double[n2];
            for (int ri = 0; ri < R; ri++) for (int l = 0; l < n2; l++) colW[l] += Math.Abs(Wfull[ri * n2 + l]);
            var (c0, c1) = PulseMetrics.SupportRange(colW, 1e-4, 2);
            int cs = Math.Max(1, (c1 - c0 + 2 * pts) / (2 * pts));
            var cols = new List<int>(); for (int l = c0; l <= c1; l += cs) cols.Add(l);
            var W = new double[R * cols.Count];
            for (int ri = 0; ri < R; ri++)
                for (int c = 0; c < cols.Count; c++)
                {
                    double acc = 0; int cnt = 0;
                    for (int l = cols[c]; l < Math.Min(n2, cols[c] + cs); l++) { acc += Wfull[ri * n2 + l]; cnt++; }
                    W[ri * cols.Count + c] = acc / Math.Max(1, cnt);
                }
            var om = cols.Select(l => g.OmegaCenter + (l + 0.5 * (cs - 1) - n2 / 2) * dOm).ToArray();
            var t = rows.Select(i => g.Time[i]).ToArray();
            double mx = W.Max(x => Math.Abs(x));
            if (mx > 0) for (int i = 0; i < W.Length; i++) W[i] /= mx;
            o.Add("time_s", t, "s", "time");
            o.Add("omega_rad_s", om, "rad/s", "angular frequency");
            o.Add("wavelength_m", om.Select(w => w > 0 ? 2 * Math.PI * Tools.C / w : double.NaN).ToArray(), "m", "wavelength");
            o.Add(gate > 0 ? "spectrogram" : "distribution", W, "norm.", (gate > 0 ? "spectrogram" : "Wigner distribution") + " [time][frequency], normalised to max |W|", R, cols.Count);
            o.Values.Set("virtual", true);
            Tools.Put(o, "gateFwhmS", gate);
            if (gate <= 0) Tools.Put(o, "negativeFraction", -W.Where(x => x < 0).Sum() / Math.Max(1e-300, W.Where(x => x > 0).Sum()));
        }
    }

    /// <summary>
    /// "fieldProbe" (virtual): the complete field -- temporal phase and instantaneous frequency, spectral phase,
    /// group delay and GDD vs wavelength, the carrier-envelope phase, and (optionally, "realField": true) the real
    /// electric field E(t) = Re[A(t) exp(-i w0 t)] (sqrt(W) units) resolved on the optical cycle.
    /// </summary>
    public sealed class FieldProbe : IInstrument
    {
        public string Kind => "fieldProbe";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var p = ctx.Pulse;
            var cp = Tools.Compact(p, ctx.Spec.GetInt("maxN", 8192), out var note); if (note != null) o.Notes.Add(note);
            var g = cp.Grid; int n = g.N;
            var a = cp.TemporalAx();
            var pw = PulseMetrics.TemporalPower(cp);
            double pk = pw.Max(), lev = ctx.Spec.GetDouble("threshold", 1e-3);
            // temporal phase (unwrapped from the peak) and instantaneous frequency w0 - dphi/dt
            int ip = Array.IndexOf(pw, pk);
            var ph = new double[n]; for (int i = 0; i < n; i++) ph[i] = double.NaN;
            ph[ip] = a[ip].Phase;
            for (int i = ip + 1; i < n && pw[i] >= lev * pk; i++) ph[i] = ph[i - 1] + Wrap(a[i].Phase - a[i - 1].Phase);
            for (int i = ip - 1; i >= 0 && pw[i] >= lev * pk; i--) ph[i] = ph[i + 1] + Wrap(a[i].Phase - a[i + 1].Phase);
            var inst = new double[n];
            for (int i = 0; i < n; i++)
            {
                double l = i > 0 ? ph[i - 1] : double.NaN, r = i < n - 1 ? ph[i + 1] : double.NaN;
                inst[i] = double.IsNaN(l) || double.IsNaN(r) ? double.NaN : g.OmegaCenter - (r - l) / (2 * g.DTime);
            }
            double ph0 = ph[ip];
            o.Add("time_s", (double[])g.Time.Clone(), "s", "time");
            o.Add("power_w", pw, "W", "power");
            o.Add("temporal_phase_rad", ph.Select(x => double.IsNaN(x) ? x : x - ph0).ToArray(), "rad", "temporal phase of A(t) relative to the peak (NaN below threshold)");
            o.Add("instantaneous_wavelength_m", inst.Select(w => double.IsNaN(w) || w <= 0 ? double.NaN : 2 * Math.PI * Tools.C / w).ToArray(), "m", "instantaneous wavelength 2 pi c / (w0 - dphi/dt)");
            // spectral phase, group delay and GDD
            var S = cp.SpectralIntensity(); double sp = S.Max();
            var sph = new double[n]; for (int i = 0; i < n; i++) sph[i] = double.NaN;
            int iq = Array.IndexOf(S, sp);
            sph[iq] = cp.Ax[iq].Phase;
            for (int i = iq + 1; i < n && S[i] >= lev * sp; i++) sph[i] = sph[i - 1] + Wrap(cp.Ax[i].Phase - cp.Ax[i - 1].Phase);
            for (int i = iq - 1; i >= 0 && S[i] >= lev * sp; i--) sph[i] = sph[i + 1] + Wrap(cp.Ax[i].Phase - cp.Ax[i + 1].Phase);
            var gd = new double[n]; var gdd = new double[n];
            double dw = g.DOmega;
            for (int i = 0; i < n; i++)
            {
                bool ok = i > 0 && i < n - 1 && !double.IsNaN(sph[i - 1]) && !double.IsNaN(sph[i + 1]);
                gd[i] = ok ? (sph[i + 1] - sph[i - 1]) / (2 * dw) : double.NaN;
                gdd[i] = ok ? (sph[i + 1] - 2 * sph[i] + sph[i - 1]) / (dw * dw) : double.NaN;
            }
            var (tc, _) = PulseMetrics.Moments(g.Time, pw);
            o.Add("wavelength_m", g.OmegaAbsolute.Select(w => w > 0 ? 2 * Math.PI * Tools.C / w : double.NaN).ToArray(), "m", "wavelength");
            o.Add("spectrum", S, "a.u.", "spectral intensity");
            o.Add("group_delay_s", gd.Select(x => double.IsNaN(x) ? x : x - tc).ToArray(), "s", "group delay dphi/dw relative to the energy centroid (NaN below threshold)");
            o.Add("gdd_s2", gdd, "s^2", "group-delay dispersion d2phi/dw2 (NaN below threshold)");
            // CEP: carrier phase at the envelope peak, E = Re[A(t) exp(-i w0 t)] on the run's co-moving time axis
            {
                var af = p.TemporalAx(); var pf = PulseMetrics.TemporalPower(p); int k = Array.IndexOf(pf, pf.Max());
                double cep = Wrap(af[k].Phase - p.Grid.OmegaCenter * p.Grid.Time[k]);
                Tools.Put(o, "cepRad", cep);
                Tools.Put(o, "peakTimeS", p.Grid.Time[k]);
            }
            // linear chirp: slope of the instantaneous frequency over the FWHM
            var sel = Enumerable.Range(0, n).Where(i => pw[i] >= 0.5 * pk && !double.IsNaN(inst[i])).ToArray();
            if (sel.Length > 3)
            {
                double mt = sel.Average(i => g.Time[i]), mw = sel.Average(i => inst[i]);
                double sxy = sel.Sum(i => (g.Time[i] - mt) * (inst[i] - mw)), sxx = sel.Sum(i => (g.Time[i] - mt) * (g.Time[i] - mt));
                Tools.Put(o, "chirpRateRadPerS2", sxx > 0 ? sxy / sxx : double.NaN);
            }
            if (ctx.Spec.GetBool("realField", false))
            {
                // resample to >= 8 points per optical cycle by zero-padding the spectrum around the carrier
                double need = 2 * Math.PI / g.OmegaCenter / 8;
                int up = 1; while (g.DTime / up > need) up *= 2;
                long total = (long)n * up;
                if (total > (1 << 21)) o.Notes.Add("realField skipped: " + total + " samples would be needed to resolve the carrier");
                else
                {
                    int N = (int)total; var big = new Complex[N];
                    for (int i = 0; i < n; i++) big[i + (N - n) / 2] = cp.Ax[i] * up;
                    SpectralTransform.FreqToTimeInPlace(big);
                    double dt = g.DTime / up; var t = new double[N]; var e = new double[N];
                    for (int i = 0; i < N; i++) { t[i] = (i - N / 2) * dt; e[i] = (big[i] * Complex.FromPolarCoordinates(1, -g.OmegaCenter * t[i])).Real; }
                    o.Add("real_field_time_s", t, "s", "time of the real-field samples");
                    o.Add("real_field_sqrtw", e, "sqrt(W)", "real field Re[A(t) exp(-i w0 t)] (multiply by sqrt(2 / (c eps0 n A_eff)) for V/m)");
                }
            }
            o.Values.Set("virtual", true);
        }

        private static double Wrap(double d) => d - 2 * Math.PI * Math.Round(d / (2 * Math.PI));
    }

    /// <summary>
    /// "spaceTime" (virtual): the intensity at every point across the beam vs time, I(r, t), and the spectrum vs
    /// position, S(r, lambda), after an element with a transverse model -- spatio-temporal coupling made visible:
    /// pulse-front delay/curvature, spatial chirp, pulses that compress on axis but not in the wings. Radial models
    /// give r; cartesian models a cut along x through the axis ("axis": "y" for the other cut).
    /// </summary>
    public sealed class SpaceTime : IInstrument
    {
        public string Kind => "spaceTime";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var p = ctx.Pulse;
            var prof = p.Profile ?? throw new InvalidOperationException("spaceTime: no transverse profile at \"" + ctx.Location + "\" (place it after an element with a radial/cartesian transverse model)");
            var cp = Tools.Compact(p, ctx.Spec.GetInt("points", 512), out var note); if (note != null) o.Notes.Add(note);
            var g = cp.Grid; int n = g.N;
            var B = prof.Basis;
            // the positions on the cut
            List<int> idx;
            if (B is CartesianBasis cb)
            {
                bool alongY = ctx.Spec.GetString("axis", "x") == "y" && cb.Ny > 1;
                idx = alongY ? Enumerable.Range(0, cb.Ny).Select(iy => (cb.Nx / 2) * cb.Ny + iy).ToList()
                             : Enumerable.Range(0, cb.Nx).Select(ix => ix * cb.Ny + (cb.Ny > 1 ? cb.Ny / 2 : 0)).ToList();
            }
            else idx = Enumerable.Range(0, B.M).ToList();
            var (wx, wy) = prof.SecondMomentRadius();
            double rmax = ctx.Spec.GetDouble("maxRadiusM", 2.5 * Math.Max(wx, wy));
            idx = idx.Where(m => Math.Abs(Coord(B, m, ctx)) <= rmax).ToList();
            int R = idx.Count;
            var P = prof.At(g.OmegaAbsolute);        // [m * n + i]
            var It = new double[R * n]; var Sw = new double[R * n];
            var pos = idx.Select(m => Coord(B, m, ctx)).ToArray();
            System.Threading.Tasks.Parallel.For(0, R, r =>
            {
                int m = idx[r]; var e = new Complex[n];
                for (int i = 0; i < n; i++) { e[i] = cp.Ax[i] * P[m * n + i]; double s = e[i].Real * e[i].Real + e[i].Imaginary * e[i].Imaginary; Sw[r * n + i] = s * g.DTime / n; }
                SpectralTransform.FreqToTimeInPlace(e);
                for (int i = 0; i < n; i++) It[r * n + i] = e[i].Real * e[i].Real + e[i].Imaginary * e[i].Imaginary; // W/m^2
            });
            var tc = new double[R]; var tw = new double[R]; var lc = new double[R]; var fw = new double[R];
            for (int r = 0; r < R; r++)
            {
                var row = new double[n]; Array.Copy(It, r * n, row, 0, n);
                var (m1, rms) = PulseMetrics.Moments(g.Time, row); tc[r] = m1; tw[r] = rms;
                fw[r] = PulseMetrics.Fwhm(g.Time, row);
                var srow = new double[n]; Array.Copy(Sw, r * n, srow, 0, n);
                var (wm, _) = PulseMetrics.Moments(g.OmegaAbsolute, srow); lc[r] = wm > 0 ? 2 * Math.PI * Tools.C / wm : double.NaN;
            }
            o.Add("position_m", pos, "m", B.Kind == "radial" ? "radius" : "position along the cut");
            o.Add("time_s", (double[])g.Time.Clone(), "s", "time");
            o.Add("wavelength_m", g.OmegaAbsolute.Select(w => w > 0 ? 2 * Math.PI * Tools.C / w : double.NaN).ToArray(), "m", "wavelength");
            o.Add("intensity_w_m2", It, "W/m^2", "intensity [position][time]", R, n);
            o.Add("spectral_fluence", Sw, "J/m^2 per bin", "spectral fluence [position][frequency bin]", R, n);
            o.Add("arrival_time_s", tc, "s", "energy-centroid arrival time vs position (pulse front)");
            o.Add("fwhm_s", fw, "s", "local pulse duration (FWHM) vs position");
            o.Add("centroid_wavelength_m", lc, "m", "local spectral centroid vs position (spatial chirp)");
            int i0 = Enumerable.Range(0, R).OrderBy(r => Math.Abs(pos[r])).First();
            double w = Math.Max(wx, wy);
            int iw = Enumerable.Range(0, R).OrderBy(r => Math.Abs(Math.Abs(pos[r]) - w)).First();
            Tools.Put(o, "pulseFrontDelayS", tc[iw] - tc[i0]);
            Tools.Put(o, "spatialChirpM", lc[iw] - lc[i0]);
            Tools.Put(o, "onAxisFwhmS", fw[i0]);
            Tools.Put(o, "fwhmAtRadiusS", fw[iw]);
            Tools.Put(o, "beamIntegratedFwhmS", PulseMetrics.Fwhm(g.Time, PulseMetrics.TemporalPower(cp)));
            Tools.Put(o, "peakIntensityWPerM2", It.Max());
            Tools.Put(o, "referenceRadiusM", w);
            o.Values.Set("virtual", true);
        }

        private static double Coord(ITransverseBasis b, int m, InstrumentContext ctx)
        {
            if (b is RadialBasis) return b.Radius[m];
            return ctx.Spec.GetString("axis", "x") == "y" ? b.Y[m] : b.X[m];
        }
    }
}
