using System;
using System.Globalization;
using UltrafastEngine.Models;

namespace UltrafastEngine.Materials
{
    /// <summary>
    /// Lin &amp; Agrawal's Raman response of fused silica (Opt. Lett. 31, 3086 (2006)): the Blow-Wood damped oscillator
    /// plus a "boson peak" term, h(t) = (fa + fc) ha(t) + fb hb(t), ha the damped oscillator (tau1 = 12.2 fs,
    /// tau2 = 32 fs), hb(t) = (2 tau_b - t) / tau_b^2 exp(-t / tau_b), tau_b = 96 fs, fa = 0.75, fb = 0.21, fc = 0.04;
    /// fR = 0.245. Both parts have unit integral, so h does too. This is pyNLO's "full" Raman model.
    /// </summary>
    public sealed class LinAgrawalRamanResponse : IVectorDelayedResponse
    {
        /// <summary>The anisotropic part h_b = fc ha + fb hb (Lin &amp; Agrawal: the isotropic part is fa ha).</summary>
        public double[] EvaluateAnisotropic(double[] t)
        {
            double na = (_t1 * _t1 + _t2 * _t2) / (_t1 * _t2 * _t2);
            var h = new double[t.Length];
            for (int i = 0; i < t.Length; i++)
            {
                double x = t[i];
                if (x < 0) continue;
                h[i] = _fc * na * Math.Exp(-x / _t2) * Math.Sin(x / _t1) + _fb * (2 * _tb - x) / (_tb * _tb) * Math.Exp(-x / _tb);
            }
            return h;
        }
        public double AnisotropicFraction => (_fb + _fc) / (_fa + _fb + _fc);
        public const double DefaultFraction = 0.245;
        private readonly double _t1, _t2, _tb, _fa, _fb, _fc;
        public LinAgrawalRamanResponse(double tau1 = 12.2e-15, double tau2 = 32e-15, double taub = 96e-15, double fa = 0.75, double fb = 0.21, double fc = 0.04)
        { _t1 = tau1; _t2 = tau2; _tb = taub; _fa = fa; _fb = fb; _fc = fc; }
        public string Description => string.Format(CultureInfo.InvariantCulture, "Lin-Agrawal silica Raman (damped oscillator {0:G3}/{1:G3} fs + boson peak {2:G3} fs; fa {3}, fb {4}, fc {5})", _t1 * 1e15, _t2 * 1e15, _tb * 1e15, _fa, _fb, _fc);
        public double[] Evaluate(double[] t)
        {
            double na = (_t1 * _t1 + _t2 * _t2) / (_t1 * _t2 * _t2);
            var h = new double[t.Length];
            for (int i = 0; i < t.Length; i++)
            {
                double x = t[i];
                if (x < 0) continue;
                double ha = na * Math.Exp(-x / _t2) * Math.Sin(x / _t1);
                double hb = (2 * _tb - x) / (_tb * _tb) * Math.Exp(-x / _tb);
                h[i] = (_fa + _fc) * ha + _fb * hb;
            }
            return h;
        }
    }

    /// <summary>
    /// Hollenbeck &amp; Cantrell's intermediate-broadening model of the Raman response of fused silica (J. Opt. Soc.
    /// Am. B 19, 2886 (2002)): 13 vibrational modes, each a damped oscillator with Lorentzian and Gaussian broadening,
    /// h(t) = sum_i A_i exp(-gamma_i t - Gamma_i^2 t^2 / 4) sin(omega_i t), normalised to unit integral (its Raman
    /// gain, Im H, peaks at 13.2 THz as measured). It
    /// reproduces the measured Raman gain spectrum (peak at 13.2 THz, shoulder at 15 THz, tail to 40 THz) better than the
    /// single damped oscillator. fR = 0.18 (as with the damped oscillator).
    /// </summary>
    public sealed class HollenbeckCantrellRamanResponse : IDelayedResponse
    {
        public const double DefaultFraction = 0.18;
        // component positions (cm^-1), peak intensities, Gaussian FWHM (cm^-1), Lorentzian FWHM (cm^-1): Table 1 of the paper
        private static readonly double[] Pos = { 56.25, 100.00, 231.25, 362.50, 463.00, 497.00, 611.50, 691.67, 793.67, 835.50, 930.00, 1080.00, 1215.00 };
        private static readonly double[] Amp = { 1.00, 11.40, 36.67, 67.67, 74.00, 4.50, 6.80, 4.60, 4.20, 4.50, 2.70, 3.10, 3.00 };
        private static readonly double[] Gfw = { 52.10, 110.42, 175.00, 162.50, 135.33, 24.50, 41.50, 155.00, 59.50, 64.30, 150.00, 91.00, 160.00 };
        private static readonly double[] Lfw = { 17.37, 38.81, 58.33, 54.17, 45.11, 8.17, 13.83, 51.67, 19.83, 21.43, 50.00, 30.33, 53.33 };
        private static double _norm = double.NaN;

        public string Description => "Hollenbeck-Cantrell silica Raman (13-mode intermediate-broadening model)";

        private static double Raw(double t)
        {
            if (t < 0) return 0;
            const double cCm = 2.99792458e10; // cm/s
            double s = 0;
            for (int i = 0; i < Pos.Length; i++)
            {
                double wv = 2 * Math.PI * cCm * Pos[i];
                double gamma = Math.PI * cCm * Lfw[i], Gamma = Math.PI * cCm * Gfw[i];
                s += Amp[i] * Math.Exp(-gamma * t) * Math.Exp(-Gamma * Gamma * t * t / 4) * Math.Sin(wv * t);
            }
            return s;
        }

        private static double Norm()
        {
            if (!double.IsNaN(_norm)) return _norm;
            // unit integral, evaluated on a fine grid (the response has decayed after ~2 ps)
            const int n = 400000; const double tMax = 4e-12; double dt = tMax / n, s = 0;
            for (int i = 0; i <= n; i++) s += (i == 0 || i == n ? 0.5 : 1.0) * Raw(i * dt);
            _norm = 1.0 / (s * dt);
            return _norm;
        }

        public double[] Evaluate(double[] t)
        {
            double k = Norm();
            var h = new double[t.Length];
            for (int i = 0; i < t.Length; i++) h[i] = k * Raw(t[i]);
            return h;
        }
    }
}
