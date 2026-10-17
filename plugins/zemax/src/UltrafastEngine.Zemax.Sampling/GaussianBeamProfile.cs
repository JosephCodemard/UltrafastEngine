using System;
using System.Collections.Generic;

namespace UltrafastEngine.Zemax
{
    /// <summary>
    /// Paraxial beam radius w(z) along ONE gap (surface s-1 -> s), built from Zemax's
    /// GBPS (beam radius at each end) and GBPW (waist radius of the beam in the space
    /// following surface s-1). ZOS-API-free so it can be unit tested.
    ///
    /// Why this matters for the MPC: SPM strength goes as 1/area, and in a multipass cell
    /// the beam is large on the mirrors and focuses to a small waist mid-pass. The
    /// previous code took the area at ONE LDE surface (the span's "middle" index --
    /// arbitrary with coordinate breaks, often a mirror) for an entire nonlinear span,
    /// so B-integral could be off by orders of magnitude per pass, and a whole gas-filled
    /// cell could be lumped into one element. This model gives the true w(z) so the
    /// solver can integrate gamma(z) along each pass.
    ///
    /// Model: w(z)^2 = w0^2 (1 + ((z - zw)/zR)^2). With w0 known (GBPW) and both end radii
    /// known, zR and the waist position zw follow up to a discrete ambiguity (waist inside
    /// vs. outside the gap), resolved by picking the candidate whose zR is closest to the
    /// physical pi w0^2 n / (M^2 lambda). If GBPW is unusable or no candidate is consistent
    /// (> 25 % off), it falls back to the physical zR with the waist position fitted to the
    /// end radius, and finally to linear interpolation of w between the ends. Which model
    /// was used is reported (Method) so the run log shows it.
    /// </summary>
    public sealed class GaussianBeamProfile
    {
        public double LengthM { get; private set; }
        public string Method { get; private set; }
        public double WaistRadiusM { get; private set; }
        public double WaistPositionM { get; private set; }
        public double RayleighRangeM { get; private set; }
        private readonly double _wStart, _wEnd;
        private bool _linear;

        private GaussianBeamProfile(double lengthM, double wStart, double wEnd) { LengthM = lengthM; _wStart = wStart; _wEnd = wEnd; }

        public double RadiusAt(double zM)
        {
            if (_linear || LengthM <= 0) return LengthM <= 0 ? _wStart : _wStart + (_wEnd - _wStart) * Math.Max(0, Math.Min(1, zM / LengthM));
            double u = (zM - WaistPositionM) / RayleighRangeM;
            return WaistRadiusM * Math.Sqrt(1.0 + u * u);
        }

        /// <summary>Effective area pi w^2 / 2 (peak intensity of a Gaussian = 2P/(pi w^2)).</summary>
        public double AreaAt(double zM) { double w = RadiusAt(zM); return Math.PI * w * w / 2.0; }

        public static GaussianBeamProfile Build(double lengthM, double wStartM, double wEndM, double waistM,
            double index, double m2, double lambdaM)
        {
            var p = new GaussianBeamProfile(Math.Abs(lengthM), wStartM, wEndM);
            double L = p.LengthM;
            if (L <= 0 || wStartM <= 0 || wEndM <= 0) { p._linear = true; p.Method = "point"; return p; }

            double zrPhys = waistM > 0 ? Math.PI * waistM * waistM * Math.Abs(index) / (Math.Max(1.0, m2) * lambdaM) : double.NaN;
            double minEnd = Math.Min(wStartM, wEndM);

            if (waistM > 0 && waistM <= minEnd * 1.0005)
            {
                double w0 = Math.Min(waistM, minEnd);
                double a = Math.Sqrt(Math.Max(0.0, wStartM * wStartM / (w0 * w0) - 1.0));
                double b = Math.Sqrt(Math.Max(0.0, wEndM * wEndM / (w0 * w0) - 1.0));
                var candidates = new List<(double zr, double zw, string what)>();
                if (a + b > 1e-9) candidates.Add((L / (a + b), a * L / (a + b), "waist inside gap"));
                if (Math.Abs(a - b) > 1e-9)
                {
                    double zr = L / Math.Abs(a - b);
                    candidates.Add((zr, a > b ? a * zr : -a * zr, a > b ? "waist after gap" : "waist before gap"));
                }
                if (candidates.Count == 0)
                {
                    // Both ends at the waist radius: collimated across the whole gap.
                    p.WaistRadiusM = w0; p.WaistPositionM = 0.5 * L; p.RayleighRangeM = double.IsNaN(zrPhys) ? 1e30 : Math.Max(zrPhys, 1e3 * L);
                    p.Method = "collimated (GBPW)"; return p;
                }
                (double zr, double zw, string what) best = candidates[0];
                double bestErr = double.PositiveInfinity;
                foreach (var c in candidates)
                {
                    double err = double.IsNaN(zrPhys) ? 0.0 : Math.Abs(Math.Log(c.zr / zrPhys));
                    if (err < bestErr) { bestErr = err; best = c; }
                }
                if (double.IsNaN(zrPhys) || bestErr < Math.Log(1.25))
                {
                    p.WaistRadiusM = w0; p.RayleighRangeM = best.zr; p.WaistPositionM = best.zw;
                    p.Method = "GBPS+GBPW (" + best.what + ")"; return p;
                }
            }

            // Fallback 1: physical zR with the measured waist, waist position fitted to w_start
            // and the sign chosen to reproduce w_end.
            if (waistM > 0 && !double.IsNaN(zrPhys) && waistM <= minEnd * 1.0005)
            {
                double w0 = Math.Min(waistM, minEnd);
                double a = Math.Sqrt(Math.Max(0.0, wStartM * wStartM / (w0 * w0) - 1.0)) * zrPhys;
                double bestErr = double.PositiveInfinity, bestZw = 0;
                foreach (double zw in new[] { a, -a })
                {
                    double u = (L - zw) / zrPhys;
                    double wEndModel = w0 * Math.Sqrt(1 + u * u);
                    double err = Math.Abs(wEndModel - wEndM) / wEndM;
                    if (err < bestErr) { bestErr = err; bestZw = zw; }
                }
                if (bestErr < 0.1)
                {
                    p.WaistRadiusM = w0; p.RayleighRangeM = zrPhys; p.WaistPositionM = bestZw;
                    p.Method = "GBPW + physical zR"; return p;
                }
            }

            p._linear = true;
            p.Method = "linear w between GBPS ends (GBPW inconsistent/unavailable)";
            return p;
        }
    }
}
