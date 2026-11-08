// SingleShotCorrelator.cs -- the single-shot (time-to-space) cross-correlator: instrument "singleShotCorrelator".
//
// What it does: two beams cross at an angle in a nonlinear crystal, the pulse under test (PT) and a clean sampling pulse (by default its
// own second harmonic, or the pulse itself for an autocorrelator). The sum-frequency (or second-harmonic) signal at position x across
// the overlap is generated where the two pulse envelopes meet, i.e. it measures the correlation S(tau) = integral I_PT(t) I_S(t - tau) dt
// at the delay tau(x) = tau0 + x * (n_T sin a + n_S sin b) / c, so a camera or a position axis reads a delay trace in one shot
// (Ma et al., High Power Laser Sci. Eng. 6, e61 (2018), eqs. (1) and (2)). The instrument reports
//   - the delay window T = W * slope, slope = (n_T sin a + n_S sin b) / c, and the geometric resolution dtau = L / c |n_T cos a - n_S cos b|
//     (a, b: the angles of the two inputs to the signal direction, fixed by transverse momentum conservation of the phase indices n; with
//     "delayIndex": "group" (default) the group index of the crystal enters the resolution through dk_z/d omega, see SingleShotGeometry.Solve),
//   - the trace on the position axis x_m and on the delay axis delay_s, as a camera of "points" pixels of width W / points would record it.
// Model: the correlation is that of the temporal intensities at the instrument's location (a flat-top overlap of width W, uniform
// across the beam), convolved with a rectangle of width dtau (each position collects light from all depths of the crystal, whose
// delay spans dtau) and averaged over the pixel. The two beams are NOT propagated through the crystal on a spatial grid: the
// conversion efficiency, depletion, walk-off, the beam profile across the overlap, air scattering of the signal, the stripe filter and
// the reflections between the crystal faces are not simulated. Area conservation: the integral of the raw signal over delay equals
// (integral I_PT dt) (integral I_S dt) when the delay window contains the whole correlation ("windowCoverage" = 1).
//
// Main classes and signatures:
//   SingleShotGeometry.Solve(nT, nS, ngT, ngS, omegaT, omegaS, crossingInsideRad)   (a, b, slope, resolution per length)
//   SingleShotCorrelator : IInstrument                                                          (Measure)
using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Crystals;
using UltrafastEngine.Numerics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Instruments
{
    /// <summary>Geometry of two beams crossing inside a crystal and a signal leaving where their transverse momenta cancel.</summary>
    public struct SingleShotGeometry
    {
        public double AngleTRad, AngleSRad;            // a, b: angles of the PT and sampling beams to the signal direction
        public double SlopeSPerM;                      // d tau / d x: (n_T sin a + n_S sin b) / c with the delay indices
        public double ResolutionSPerM;                 // d tau / d z across the crystal: |n_T cos a - n_S cos b| / c

        /// <param name="nT">Phase index at the PT frequency.</param><param name="nS">Phase index at the sampling frequency.</param>
        /// <param name="ngT">Group index at the PT frequency (set equal to nT for the form of Ma et al.'s eq. (2)).</param>
        /// <param name="ngS">The same for the sampling pulse.</param>
        /// <param name="crossingInsideRad">Full angle between the two beams inside the crystal.</param>
        /// <remarks>
        /// The beams enter from air at fixed angles through a face perpendicular to the signal. Each frequency keeps its tangential wave
        /// number k_x = omega sin(phi) / c, so an envelope arrives at x at t = x dk_x/d omega = x n sin(a) / c: the PHASE index enters the
        /// slope (eq. (1) exactly). Along the depth z the arrival time is z dk_z/d omega with k_z = sqrt(n^2 omega^2 / c^2 - k_x^2), which gives
        /// (n_g - n sin^2 a) / (c cos a); for n_g = n this is n cos a / c, the form of eq. (2). The difference between the two beams is the
        /// resolution.
        /// </remarks>
        public static SingleShotGeometry Solve(double nT, double nS, double ngT, double ngS, double omegaT, double omegaS, double crossingInsideRad)
        {
            // k_T + k_S = k_signal: the transverse parts cancel about the signal axis, k_T sin a = k_S sin b, a + b = crossing angle
            double kT = nT * omegaT, kS = nS * omegaS;       // c cancels
            double a = Math.Atan2(kS * Math.Sin(crossingInsideRad), kT + kS * Math.Cos(crossingInsideRad));
            double b = crossingInsideRad - a;
            double c = PhysicalConstants.SpeedOfLightMPerS;
            double dzT = (ngT - nT * Math.Sin(a) * Math.Sin(a)) / Math.Cos(a), dzS = (ngS - nS * Math.Sin(b) * Math.Sin(b)) / Math.Cos(b);
            return new SingleShotGeometry
            {
                AngleTRad = a, AngleSRad = b,
                SlopeSPerM = (nT * Math.Sin(a) + nS * Math.Sin(b)) / c,
                ResolutionSPerM = Math.Abs(dzT - dzS) / c
            };
        }
    }

    public sealed class SingleShotCorrelator : IInstrument
    {
        public string Kind => "singleShotCorrelator";

        private static double Index(Models.IRefractiveIndex n, double omega) { var o = new double[1]; n.Evaluate(new[] { omega }, o); return o[0]; }
        private static double GroupIndex(Models.IRefractiveIndex n, double omega)
        {
            double h = omega * 1e-4; var w = new[] { omega - 2 * h, omega - h, omega, omega + h, omega + 2 * h }; var v = new double[5];
            n.Evaluate(w, v);
            return v[2] + omega * (v[0] - 8 * v[1] + 8 * v[3] - v[4]) / (12 * h);
        }

        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var p = ctx.Pulse; var s = ctx.Spec; var g = p.Grid; int n = g.N;
            double dt = g.DTime;
            string sampKind = s.GetString("samplingPulse", "secondHarmonic");
            if (sampKind != "secondHarmonic" && sampKind != "fundamental") throw new InvalidOperationException("singleShotCorrelator: samplingPulse is \"secondHarmonic\" or \"fundamental\".");
            double omegaT = g.OmegaCenter, omegaS = sampKind == "secondHarmonic" ? 2 * omegaT : omegaT;

            // ---- indices
            double nT, nS, ngT, ngS; string how;
            bool phaseDelay = s.GetString("delayIndex", "group") == "phase";
            if (s.Has("indexPt") || s.Has("indexSampling"))
            {
                nT = s.GetDouble("indexPt", s.GetDouble("indexSampling", 1)); nS = s.GetDouble("indexSampling", nT);
                ngT = s.GetDouble("groupIndexPt", nT); ngS = s.GetDouble("groupIndexSampling", nS); how = "given indices";
            }
            else
            {
                var cr = CrystalLibrary.Get(s.GetString("crystal", "BBO"));
                nT = Index(cr.Nx, omegaT); nS = Index(cr.Nx, omegaS); ngT = GroupIndex(cr.Nx, omegaT); ngS = GroupIndex(cr.Nx, omegaS);
                how = cr.Name + " (ordinary / x index)";
            }
            if (phaseDelay) { ngT = nT; ngS = nS; }

            // ---- crossing angle
            double crossIn;
            if (s.Has("externalCrossingAngleDeg"))
            {
                // the face is perpendicular to the signal: omega_T sin(phi_T) = omega_S sin(phi_S) (tangential continuity of the sum frequency)
                // with phi_T + phi_S the external angle; the beams then refract with Snell's law at that face
                double ext = s.GetDouble("externalCrossingAngleDeg", 0) * Math.PI / 180, rr = omegaS / omegaT;
                double phiT = Math.Atan2(rr * Math.Sin(ext), 1 + rr * Math.Cos(ext)), phiS = ext - phiT;
                crossIn = Math.Asin(Math.Sin(phiT) / nT) + Math.Asin(Math.Sin(phiS) / nS);
            }
            else if (s.Has("crossingAngleDeg")) crossIn = s.GetDouble("crossingAngleDeg", 0) * Math.PI / 180;
            else throw new InvalidOperationException("singleShotCorrelator needs crossingAngleDeg (inside the crystal) or externalCrossingAngleDeg.");
            if (!(crossIn > 0) || crossIn >= Math.PI) throw new InvalidOperationException("singleShotCorrelator: the crossing angle must be between 0 and 180 degrees.");
            var geo = SingleShotGeometry.Solve(nT, nS, ngT, ngS, omegaT, omegaS, crossIn);
            double W = s.GetDouble("beamWidthM", double.NaN);
            if (!(W > 0)) throw new InvalidOperationException("singleShotCorrelator needs beamWidthM (the width of the beam overlap, perpendicular to the signal).");
            double L = s.GetDouble("crystalLengthM", 0);
            int points = s.GetInt("points", 256);
            double tau0 = s.GetDouble("delayOffsetS", 0);
            double window = W * geo.SlopeSPerM, resGeo = L * geo.ResolutionSPerM;
            double pixel = W / points, pixDelay = pixel * geo.SlopeSPerM;

            // ---- temporal intensities of the pulse under test and of the sampling pulse
            var ax = p.TemporalAx(); var ay = p.TemporalAy();
            var gx = ctx.GatePulse != null ? ctx.GatePulse.TemporalAx() : ax; var gy = ctx.GatePulse != null ? ctx.GatePulse.TemporalAy() : ay;
            double[] I = new double[n];
            for (int i = 0; i < n; i++) I[i] = ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary + ay[i].Real * ay[i].Real + ay[i].Imaginary * ay[i].Imaginary;
            double ex = gx.Sum(z => z.Magnitude * z.Magnitude), ey = gy.Sum(z => z.Magnitude * z.Magnitude);
            var e = ex >= ey ? gx : gy;
            if (Math.Min(ex, ey) > 1e-3 * Math.Max(ex, ey)) o.Notes.Add("pulse is not linearly polarised: the sampling pulse is made from the dominant component, the signal from the total intensity");
            var G = new double[n];
            for (int i = 0; i < n; i++) { var z = sampKind == "secondHarmonic" ? e[i] * e[i] : e[i]; G[i] = z.Real * z.Real + z.Imaginary * z.Imaginary; }

            // ---- S(tau) = sum_t I(t) G(t - tau) dt by zero-padded FFT; c[k] with k >= 0 the lag k dt, negative lags wrap to the end
            int m = 2 * n;
            var fi = new Complex[m]; var fg = new Complex[m];
            for (int i = 0; i < n; i++) { fi[i] = I[i]; fg[i] = G[i]; }
            var FI = ComplexFft.Forward(fi); var FG = ComplexFft.Forward(fg);
            for (int k = 0; k < m; k++) FI[k] *= Complex.Conjugate(FG[k]);
            var c = ComplexFft.Inverse(FI);
            // lags -n+1 .. n-1 (those beyond are zero)
            int nl = 2 * n - 1; var lag = new double[nl]; var S = new double[nl];
            for (int j = 0; j < nl; j++) { int k = j - (n - 1); lag[j] = k * dt; S[j] = c[k >= 0 ? k : m + k].Real * dt; }
            double areaI = I.Sum() * dt, areaG = G.Sum() * dt;

            // ---- a rectangle of width dtau (depth of the crystal), then the pixel
            double[] S1 = BoxAverage(lag, S, resGeo, dt);
            var cum1 = Cumulative(S1, dt);
            var xs = new double[points]; var taus = new double[points]; var raw = new double[points];
            for (int k = 0; k < points; k++)
            {
                xs[k] = -W / 2 + (k + 0.5) * pixel; taus[k] = tau0 + xs[k] * geo.SlopeSPerM;
                raw[k] = BoxAt(lag, S1, cum1, taus[k], pixDelay, dt);
            }
            double smax = raw.Max();
            if (!(smax > 0)) throw new InvalidOperationException("singleShotCorrelator: no signal inside the delay window (the pulse does not overlap the sampling pulse at any position).");
            double areaMeasured = raw.Sum() * pixDelay, areaExpected = areaI * areaG;
            double floor = s.Has("noiseFloor") ? s.GetDouble("noiseFloor", 1e-6) : Math.Pow(10, -s.GetDouble("dynamicRangeDb", 60) / 10);
            bool noise = s.GetBool("noise", true); var rnd = new Random(s.GetInt("seed", 1));
            var meas = new double[points]; var ideal = new double[points];
            for (int k = 0; k < points; k++)
            {
                ideal[k] = raw[k] / smax;
                double u1 = 1 - rnd.NextDouble(), u2 = rnd.NextDouble();
                meas[k] = ideal[k] + (noise ? floor * Math.Abs(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2)) : floor);
            }
            double mm = meas.Max();
            o.Add("x_m", xs, "m", "position across the overlap (positive: later delay)");
            o.Add("delay_s", taus, "s", "delay at each position, tau0 + x * slope (positive: PT feature after the main pulse)");
            o.Add("signal", meas.Select(v => v / mm).ToArray(), "", "normalised signal (with the camera floor)");
            o.Add("signal_db", meas.Select(v => 10 * Math.Log10(Math.Max(v / mm, 1e-300))).ToArray(), "dB", "normalised signal");
            o.Add("signal_ideal", ideal, "", "signal without noise or floor");
            o.Add("signal_raw", raw, "W^2 s", "S(tau) = integral I_PT(t) I_S(t - tau) dt before normalisation (W^2 s for powers in W)");
            Tools.Put(o, "indexPt", nT); Tools.Put(o, "indexSampling", nS); Tools.Put(o, "groupIndexPt", ngT); Tools.Put(o, "groupIndexSampling", ngS);
            Tools.Put(o, "angleToSignalPtDeg", geo.AngleTRad * 180 / Math.PI); Tools.Put(o, "angleToSignalSamplingDeg", geo.AngleSRad * 180 / Math.PI);
            Tools.Put(o, "crossingAngleInsideDeg", crossIn * 180 / Math.PI);
            Tools.Put(o, "delayPerPositionSPerM", geo.SlopeSPerM);
            Tools.Put(o, "windowS", window);
            Tools.Put(o, "resolutionGeometricS", resGeo);
            Tools.Put(o, "resolutionPixelS", pixDelay);
            Tools.Put(o, "resolutionS", Math.Sqrt(resGeo * resGeo + pixDelay * pixDelay));
            Tools.Put(o, "areaMeasuredWSquaredS", areaMeasured); Tools.Put(o, "areaExpectedWSquaredS", areaExpected);
            Tools.Put(o, "windowCoverage", areaMeasured / areaExpected);
            Tools.Put(o, "dynamicRangeDb", -10 * Math.Log10(floor));
            Tools.Put(o, "fwhmS", PulseMetrics.Fwhm(taus, ideal));
            // the strongest feature outside +-guardS of the main peak
            int jp = Array.IndexOf(ideal, 1.0);
            double guard = s.GetDouble("windowAroundPeakS", 5 * PulseMetrics.Fwhm(taus, ideal));
            double pre = 0, post = 0;
            for (int k = 0; k < points; k++)
            {
                if (taus[k] < taus[jp] - guard && ideal[k] > pre) pre = ideal[k];
                if (taus[k] > taus[jp] + guard && ideal[k] > post) post = ideal[k];
            }
            Tools.Put(o, "prePulseContrastDb", 10 * Math.Log10(Math.Max(pre, floor))); Tools.Put(o, "postPulseContrastDb", 10 * Math.Log10(Math.Max(post, floor)));
            o.Notes.Add("single-shot geometry: indices from " + how + (phaseDelay ? "; resolution from phase indices (the form of the paper's eq. (2))" : "; resolution with the crystal's group index")
                + "; window " + (window * 1e12).ToString("G4", CultureInfo.InvariantCulture) + " ps for W = " + (W * 1e3).ToString("G3", CultureInfo.InvariantCulture)
                + " mm, geometric resolution " + (resGeo * 1e12).ToString("G3", CultureInfo.InvariantCulture) + " ps, pixel " + (pixDelay * 1e12).ToString("G3", CultureInfo.InvariantCulture)
                + " ps; the beams are not propagated through the crystal (flat-top overlap, no efficiency, depletion, scattering or face reflections)");
            if (areaMeasured < 0.98 * areaExpected) o.Notes.Add("the delay window cuts the correlation: " + ((1 - areaMeasured / areaExpected) * 100).ToString("G3", CultureInfo.InvariantCulture) + " % of its area lies outside it");
            if (resGeo > 0 && resGeo < dt) o.Notes.Add("the geometric resolution is below the simulation time step");
        }

        /// <summary>Running mean of y over a rectangle of width w (centred): (C(t + w/2) - C(t - w/2)) / w with C the integral of y; w below the step: y itself.</summary>
        private static double[] BoxAverage(double[] x, double[] y, double w, double dx)
        {
            if (w <= dx) return (double[])y.Clone();
            var r = new double[y.Length];
            var cum = Cumulative(y, dx);
            for (int i = 0; i < y.Length; i++) r[i] = (CumAt(x, cum, x[i] + w / 2) - CumAt(x, cum, x[i] - w / 2)) / w;
            return r;
        }

        private static double BoxAt(double[] x, double[] y, double[] cum, double t, double w, double dx)
        {
            if (w <= dx) return Tools.Interp(x, y, t) * (t < x[0] || t > x[x.Length - 1] ? 0 : 1);
            return (CumAt(x, cum, t + w / 2) - CumAt(x, cum, t - w / 2)) / w;
        }

        private static double[] Cumulative(double[] y, double dx)
        {
            var c = new double[y.Length];
            for (int i = 1; i < y.Length; i++) c[i] = c[i - 1] + 0.5 * (y[i] + y[i - 1]) * dx;
            return c;
        }

        private static double CumAt(double[] x, double[] c, double t)
        {
            if (t <= x[0]) return 0; if (t >= x[x.Length - 1]) return c[c.Length - 1];
            double f = (t - x[0]) / (x[1] - x[0]); int i = (int)f; return c[i] + (f - i) * (c[i + 1] - c[i]);
        }
    }
}
