using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;

namespace UltrafastEngine.Engine
{
    /// <summary>
    /// Sizes the FFT grid from the pulse AND the system: the window must hold the input pulse spread by the
    /// system's group-delay variation across the spectrum (a stretcher turns 200 fs into nanoseconds), and the
    /// time step must resolve the spectrum (with a margin for nonlinear broadening). Ported from v2 with the
    /// group delay supplied as a function.
    /// </summary>
    public static class GridPlanner
    {
        public sealed class GridPlan
        {
            public double WindowS;
            public int N;
            public double RequiredWindowS;
            public double RequiredDtS;
            public bool Changed;
            public double DelaySpreadMinS, DelaySpreadMaxS;
            public List<string> Notes = new List<string>();
            public List<string> Warnings = new List<string>();
        }

        /// <summary>-40 dB spectral extent of the pulse (absolute omega).</summary>
        public static (double Lo, double Hi) SpectralExtent(PulseField pulse, double relLevel = 1e-4)
        {
            var g = pulse.Grid;
            var spec = pulse.SpectralIntensity();
            double thr = spec.Max() * relLevel;
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = 0; i < spec.Length; i++) if (spec[i] >= thr) { lo = Math.Min(lo, g.OmegaAbsolute[i]); hi = Math.Max(hi, g.OmegaAbsolute[i]); }
            return (lo, hi);
        }

        public static GridPlan Plan(PulseField pulse, IReadOnlyList<Func<double, double>> elementGroupDelays, double spectralMargin, double minSpanFraction, double configuredWindowS, int configuredN, int maxN, bool autoSize, bool keepConfiguredDt = true, double maxDtS = double.PositiveInfinity, double satelliteDelayS = 0, double programmedGddS2 = 0)
        {
            var g = pulse.Grid;
            var plan = new GridPlan { WindowS = configuredWindowS, N = configuredN };
            var (wLo, wHi) = SpectralExtent(pulse);

            // the pulse must fit the window after EVERY element, not just at the end (a stretcher followed by a
            // compressor has ~zero total spread but a huge one in between): track the spread of each partial sum
            double tMin = 0, tMax = 0;
            const int K = 400;
            var cum = new double[K + 1];
            double cum0 = 0;
            foreach (var gd in elementGroupDelays)
            {
                double g0 = gd(g.OmegaCenter); if (double.IsNaN(g0) || double.IsInfinity(g0)) g0 = 0;
                cum0 += g0;
                for (int k = 0; k <= K; k++)
                {
                    double w = wLo + (wHi - wLo) * k / K;
                    double v = gd(w);
                    if (!double.IsNaN(v) && !double.IsInfinity(v)) cum[k] += v; else cum[k] += g0;
                    double t = cum[k] - cum0;
                    tMin = Math.Min(tMin, t); tMax = Math.Max(tMax, t);
                }
            }
            plan.DelaySpreadMinS = tMin; plan.DelaySpreadMaxS = tMax;

            var p = Results.PulseMetrics.TemporalPower(pulse);
            double pk = p.Max();
            int a = 0, b = g.N - 1;
            while (a < g.N && p[a] < 1e-6 * pk) a++;
            while (b > 0 && p[b] < 1e-6 * pk) b--;
            double inputExtent = Math.Max(g.DTime, (b - a) * g.DTime);
            // satellite pulses (ghost replicas, pre-pulse partners) must fit too: room on both sides
            // a programmed spectral phase that carries no declared group delay (shaper, AOPDF) still stretches the pulse by GDD x bandwidth
            double stretchS = Math.Abs(programmedGddS2) * (wHi - wLo);
            if (stretchS > 0) plan.Notes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Programmed GDD ({0:G4} fs^2) over the {1:G4} rad/s input bandwidth stretches the pulse by up to {2:G4} ps.", Math.Abs(programmedGddS2) * 1e30, wHi - wLo, stretchS * 1e12));
            double halfNeeded = Math.Max(Math.Abs(tMin), Math.Abs(tMax)) + 0.5 * inputExtent + Math.Abs(satelliteDelayS) + 0.5 * stretchS;
            plan.RequiredWindowS = 2.0 * 1.25 * halfNeeded;

            double devMax = Math.Max(g.OmegaCenter - wLo, wHi - g.OmegaCenter) * Math.Max(1.0, spectralMargin);
            devMax = Math.Max(devMax, minSpanFraction * g.OmegaCenter);  // nonlinear runs: leave room for new frequencies
            devMax = Math.Min(devMax, 0.95 * g.OmegaCenter); // never reach zero frequency in the envelope grid
            plan.RequiredDtS = Math.Min(Math.PI / (1.1 * devMax), maxDtS);

            double currentDt = configuredWindowS / configuredN;
            plan.Notes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Largest group-delay spread of the input spectrum (-40 dB) anywhere in the system: {0:G4} .. {1:G4} ps relative to the carrier.", tMin * 1e12, tMax * 1e12));
            plan.Notes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Required window ~{0:G4} ps (configured {1:G4} ps); required dt <= {2:G4} fs (configured {3:G4} fs).",
                plan.RequiredWindowS * 1e12, configuredWindowS * 1e12, plan.RequiredDtS * 1e15, currentDt * 1e15));

            bool windowOk = configuredWindowS >= plan.RequiredWindowS, dtOk = currentDt <= plan.RequiredDtS;
            bool wasteful = !keepConfiguredDt && currentDt < 0.5 * plan.RequiredDtS;
            if (windowOk && dtOk && !wasteful) { plan.Notes.Add("Configured grid is adequate."); return plan; }
            if (!autoSize)
            {
                plan.Warnings.Add("Grid looks too small (" + (!windowOk ? "time window" : "") + (!windowOk && !dtOk ? " and " : "") + (!dtOk ? "time step" : "") +
                    ") and grid.auto is false -- expect wrap-around/aliasing; check edge_energy_fraction in the results.");
                return plan;
            }
            if (double.IsNaN(plan.RequiredWindowS) || double.IsInfinity(plan.RequiredWindowS) || double.IsNaN(plan.RequiredDtS) || plan.RequiredDtS <= 0)
            {
                plan.Warnings.Add("Could not size the grid (non-finite dispersion data) -- keeping the configured grid.");
                return plan;
            }
            double window = Math.Max(configuredWindowS, plan.RequiredWindowS);
            double dt = keepConfiguredDt ? Math.Min(currentDt, plan.RequiredDtS) : plan.RequiredDtS;
            int n = NextPow2((long)Math.Ceiling(window / dt));
            if (n > maxN)
            {
                n = maxN;
                double dtSpectrumOnly = Math.PI / (1.1 * Math.Max(g.OmegaCenter - wLo, wHi - g.OmegaCenter));
                dt = window / n;
                if (dt > dtSpectrumOnly)
                {
                    dt = dtSpectrumOnly; window = dt * n;
                    plan.Warnings.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "grid.maxN = {0} cannot hold the required window at the required resolution; window limited to {1:G4} ps -- the dispersed pulse will wrap around. Raise grid.maxN.", maxN, window * 1e12));
                }
                else
                    plan.Notes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "grid.maxN = {0} reached -- spectral margin reduced to what the input spectrum needs.", maxN));
            }
            plan.WindowS = n * dt; plan.N = n; plan.Changed = true;
            plan.Notes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Grid auto-sized: N = {0}, window = {1:G4} ps, dt = {2:G4} fs.", n, plan.WindowS * 1e12, dt * 1e15));
            return plan;
        }

        public static int NextPow2(long v) { long p = 1; while (p < v) p <<= 1; return (int)Math.Min(p, 1L << 30); }

        /// <summary>Grid with the carrier moved to the nearest multiple of the frequency step.</summary>
        /// <summary>
        /// The memory of a delayed response h(t) (integral 1, h = 0 for t &lt; 0): the time after which less than <paramref name="tailFraction"/> of
        /// its weight remains. A nonlinear index change driven by a pulse lasts this long after the pulse; on a periodic time grid it wraps
        /// around to the leading edge unless the window holds pulse + memory (CS2, tau = 18 ps: 9.2 tau = 166 ps for 1e-4). Found by
        /// sampling h on a geometric time axis from 1 fs to 10 us.
        /// </summary>
        public static double ResponseMemoryS(Models.IDelayedResponse r, double tailFraction = 1e-4)
        {
            const int N = 1200; double t0 = 1e-15, ratio = Math.Pow(1e10, 1.0 / N);
            var t = new double[N + 1]; for (int i = 0; i <= N; i++) t[i] = t0 * Math.Pow(ratio, i);
            double[] h;
            try { h = r.Evaluate(t); } catch { return 0; }
            var cum = new double[N + 1]; double total = 0;
            for (int i = 1; i <= N; i++) { total += 0.5 * (Math.Abs(h[i]) + Math.Abs(h[i - 1])) * (t[i] - t[i - 1]); cum[i] = total; }
            if (!(total > 0) || double.IsNaN(total) || double.IsInfinity(total)) return 0;
            for (int i = N; i > 0; i--) if (total - cum[i - 1] > tailFraction * total) return t[i];
            return 0;
        }

        public static FrequencyGrid Create(int n, double windowS, double omega0, bool snap, out double snappedOmega0)
        {
            double dt = windowS / n;
            double dw = 2 * Math.PI / (n * dt);
            snappedOmega0 = snap ? Math.Round(omega0 / dw) * dw : omega0;
            return FrequencyGrid.Create(n, dt, snappedOmega0);
        }
    }
}
