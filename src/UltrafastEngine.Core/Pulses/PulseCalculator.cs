// Pulse calculator: time-bandwidth product (TBP), transform limit, stretch factor and chirp estimate.
//
// A stand-alone calculator in the style of Crystals.PhaseMatchCalculator: JSON request in, JSON result out, so the CLI
// (`ultrafast pulse`), the C API (`uf_pulse_calc`) and Python (`ultrafast.calc`) share one implementation.
//
// Main class:  PulseCalculator
//   Solve(JsonObject request) -> JsonObject          request key "op": "shapes" | "limit" | "analyze" | "measure"
//   TbpOf(shape)                                      exact intensity-FWHM time-bandwidth product of a shape
// Conventions: durations and bandwidths are intensity FWHM; the bandwidth is in frequency (Hz) or wavelength (m, with
// centerWavelengthM); the conversion is dnu = c dlambda / lambda^2 (first order in dlambda / lambda).
using System;
using System.Collections.Generic;
using System.Globalization;
using UltrafastEngine.Numerics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Pulses
{
    /// <summary>
    /// Time-bandwidth product calculator.
    /// <code>
    /// { "op": "shapes" }                                                          the shapes and their TBP
    /// { "op": "limit",   "shape": "sech2", "durationFwhmS": 100e-15 }             also bandwidthFwhmHz | bandwidthFwhmM + centerWavelengthM
    /// { "op": "analyze", "shape": "gaussian", "durationFwhmS": 300e-15, "bandwidthFwhmM": 40e-9, "centerWavelengthM": 1030e-9 }
    /// { "op": "measure", "timeS": [...], "intensity": [...], "wavelengthM": [...] | "frequencyHz": [...], "spectrum": [...] }
    /// </code>
    /// "analyze" and "measure" return the product, the ratio to the transform limit, the stretch factor (duration over the
    /// limit) and, for a Gaussian, the group-delay dispersion (GDD) that would compress the pulse.
    /// </summary>
    public static class PulseCalculator
    {
        private const double C = PhysicalConstants.SpeedOfLightMPerS;

        /// <summary>Shapes with a closed-form product (intensity FWHM in time times intensity FWHM in frequency).</summary>
        public static readonly string[] Shapes = { "gaussian", "sech2", "rectangular" };

        /// <summary>Exact TBP: Gaussian 2 ln2 / pi; sech^2 (2 ln(1+sqrt2))^2 / pi^2; rectangular pulse (sinc^2 spectrum) 0.8859.</summary>
        public static double TbpOf(string shape)
        {
            switch ((shape ?? "gaussian").ToLowerInvariant())
            {
                case "gaussian": case "gauss": return 2 * Math.Log(2) / Math.PI;
                case "sech2": case "sech": case "sech^2": { double a = 2 * Math.Log(1 + Math.Sqrt(2)); return a * a / (Math.PI * Math.PI); }
                // sinc^2(x) = 1/2 at x = 1.39156 (sin x / x = 1/sqrt2); FWHM in frequency = 2 x / (pi T)
                case "rectangular": case "square": case "rect": return 2 * 1.3915574 / Math.PI;
                default: throw new JsonException("unknown pulse shape '" + shape + "' (use " + string.Join(", ", Shapes) + ")");
            }
        }

        public static JsonObject Solve(JsonObject req)
        {
            string op = req.GetString("op", "analyze").ToLowerInvariant();
            switch (op)
            {
                case "shapes": return ShapesTable();
                case "limit": return Limit(req);
                case "analyze": return Analyze(req);
                case "measure": return Measure(req);
                default: throw new JsonException("unknown pulse calculator op '" + op + "' (shapes, limit, analyze, measure)");
            }
        }

        private static JsonObject ShapesTable()
        {
            var arr = new JsonArray();
            foreach (var s in Shapes) arr.Add(new JsonObject().Set("shape", s).Set("timeBandwidthProduct", TbpOf(s)));
            return new JsonObject().Set("shapes", arr);
        }

        private static double DnuFromDlambda(double dlam, double lam0) => C * dlam / (lam0 * lam0);
        private static double DlambdaFromDnu(double dnu, double lam0) => lam0 * lam0 * dnu / C;

        private static JsonObject Limit(JsonObject req)
        {
            string shape = req.GetString("shape", "gaussian");
            double tbp = TbpOf(shape), lam0 = req.GetDouble("centerWavelengthM", double.NaN);
            double tau = req.GetDouble("durationFwhmS", double.NaN), dnu = req.GetDouble("bandwidthFwhmHz", double.NaN);
            double dlam = req.GetDouble("bandwidthFwhmM", double.NaN);
            if (double.IsNaN(dnu) && !double.IsNaN(dlam))
            {
                if (double.IsNaN(lam0)) throw new JsonException("\"centerWavelengthM\" is required with \"bandwidthFwhmM\"");
                dnu = DnuFromDlambda(dlam, lam0);
            }
            if (double.IsNaN(tau) == double.IsNaN(dnu)) throw new JsonException("give \"durationFwhmS\" or a bandwidth (\"bandwidthFwhmHz\", or \"bandwidthFwhmM\" with \"centerWavelengthM\"), not both");
            if (double.IsNaN(tau)) tau = tbp / dnu; else dnu = tbp / tau;
            var o = new JsonObject().Set("shape", shape).Set("timeBandwidthProduct", tbp).Set("durationFwhmS", tau).Set("bandwidthFwhmHz", dnu);
            if (!double.IsNaN(lam0)) o.Set("centerWavelengthM", lam0).Set("bandwidthFwhmM", DlambdaFromDnu(dnu, lam0));
            return o;
        }

        private static JsonObject Analyze(JsonObject req)
        {
            string shape = req.GetString("shape", "gaussian");
            double tau = req.GetDouble("durationFwhmS", double.NaN), lam0 = req.GetDouble("centerWavelengthM", double.NaN);
            double dnu = req.GetDouble("bandwidthFwhmHz", double.NaN), dlam = req.GetDouble("bandwidthFwhmM", double.NaN);
            if (double.IsNaN(dnu) && !double.IsNaN(dlam))
            {
                if (double.IsNaN(lam0)) throw new JsonException("\"centerWavelengthM\" is required with \"bandwidthFwhmM\"");
                dnu = DnuFromDlambda(dlam, lam0);
            }
            if (double.IsNaN(tau) || double.IsNaN(dnu)) throw new JsonException("\"analyze\" needs \"durationFwhmS\" and a bandwidth (\"bandwidthFwhmHz\", or \"bandwidthFwhmM\" with \"centerWavelengthM\")");
            return Report(shape, tau, dnu, lam0, double.NaN);
        }

        private static JsonObject Report(string shape, double tau, double dnu, double lam0, double tbpRms)
        {
            double limit = TbpOf(shape), tbp = tau * dnu, tau0 = limit / dnu, stretch = tau / tau0;
            var o = new JsonObject().Set("shape", shape).Set("durationFwhmS", tau).Set("bandwidthFwhmHz", dnu)
                .Set("timeBandwidthProduct", tbp).Set("transformLimitProduct", limit).Set("ratioToLimit", tbp / limit)
                .Set("transformLimitedDurationS", tau0).Set("stretchFactor", stretch);
            if (!double.IsNaN(lam0)) o.Set("centerWavelengthM", lam0).Set("bandwidthFwhmM", DlambdaFromDnu(dnu, lam0));
            if (!double.IsNaN(tbpRms)) o.Set("timeBandwidthProductRms", tbpRms).Set("rmsLimitProduct", 1 / (4 * Math.PI));
            // Linearly chirped Gaussian: tau = tau0 sqrt(1 + (4 ln2 GDD / tau0^2)^2)  =>  |GDD| = tau0^2 / (4 ln2) sqrt(stretch^2 - 1)
            if (shape.ToLowerInvariant().StartsWith("gauss", StringComparison.Ordinal))
                o.Set("gddMagnitudeFs2", stretch > 1 + 1e-9 ? tau0 * tau0 / (4 * Math.Log(2)) * Math.Sqrt(stretch * stretch - 1) * 1e30 : 0.0);
            o.Set("note", stretch < 0.98 ? "duration is shorter than the transform limit of this shape: the shape, the widths or the units are probably wrong"
                : stretch < 1.05 ? "close to transform-limited" : "chirped or structured: more bandwidth than the duration needs");
            return o;
        }

        private static JsonObject Measure(JsonObject req)
        {
            double[] t = req.GetDoubleArray("timeS"), it = req.GetDoubleArray("intensity"), sp = req.GetDoubleArray("spectrum");
            if (t == null || it == null || sp == null || t.Length != it.Length || t.Length < 5) throw new JsonException("\"measure\" needs equal-length \"timeS\" and \"intensity\" (>= 5 points) and a \"spectrum\" with \"wavelengthM\" or \"frequencyHz\"");
            double[] nu = req.GetDoubleArray("frequencyHz"), lam = req.GetDoubleArray("wavelengthM");
            if ((nu == null) == (lam == null)) throw new JsonException("give \"frequencyHz\" or \"wavelengthM\" for the spectrum, not both");
            double[] x = nu ?? lam;
            if (x.Length != sp.Length) throw new JsonException("\"spectrum\" and its axis must have the same length");
            // spectral intensity per unit frequency on an increasing, uniform frequency grid
            int n = x.Length;
            var f = new double[n]; var s = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (nu != null) { f[i] = nu[i]; s[i] = sp[i]; }
                else { f[i] = C / lam[i]; s[i] = sp[i] * lam[i] * lam[i] / C; }  // S(nu) = S(lambda) |dlambda/dnu|
            }
            if (f[0] > f[n - 1]) { Array.Reverse(f); Array.Reverse(s); }
            var fu = new double[n]; var su = new double[n];
            for (int i = 0, k = 0; i < n; i++)
            {
                fu[i] = f[0] + (f[n - 1] - f[0]) * i / (n - 1);
                while (k < n - 2 && f[k + 1] < fu[i]) k++;
                double w = f[k + 1] > f[k] ? (fu[i] - f[k]) / (f[k + 1] - f[k]) : 0;
                su[i] = s[k] + w * (s[k + 1] - s[k]);
            }
            double tau = PulseMetrics.Fwhm(t, it), dnu = PulseMetrics.Fwhm(fu, su);
            if (double.IsNaN(tau) || double.IsNaN(dnu)) throw new JsonException("cannot find the FWHM: the pulse or the spectrum does not fall below half its peak inside the data");
            var (_, st) = PulseMetrics.Moments(t, it);
            var (fm, sf) = PulseMetrics.Moments(fu, su);
            double lam0 = C / fm;
            var o = Report(req.GetString("shape", "gaussian"), tau, dnu, lam0, st * sf);
            return o;
        }
    }
}
