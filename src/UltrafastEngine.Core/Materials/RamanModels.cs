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
        /// <summary>f_R of the model (catalogue entry "linAgrawal").</summary>
        public static double DefaultFraction => CatalogueModels.RamanFractionOf(Catalogue.Get("linAgrawal", "raman"));
        private readonly double _t1, _t2, _tb, _fa, _fb, _fc;
        /// <summary>Parameters not given (NaN) come from the catalogue entry "linAgrawal" (tau1S, tau2S, taubS, fa, fb, fc).</summary>
        public LinAgrawalRamanResponse(double tau1 = double.NaN, double tau2 = double.NaN, double taub = double.NaN, double fa = double.NaN, double fb = double.NaN, double fc = double.NaN)
        {
            var q = Catalogue.Get("linAgrawal", "raman").Q("response");
            _t1 = double.IsNaN(tau1) ? q.Number("tau1S") : tau1; _t2 = double.IsNaN(tau2) ? q.Number("tau2S") : tau2; _tb = double.IsNaN(taub) ? q.Number("taubS") : taub;
            _fa = double.IsNaN(fa) ? q.Number("fa") : fa; _fb = double.IsNaN(fb) ? q.Number("fb") : fb; _fc = double.IsNaN(fc) ? q.Number("fc") : fc;
        }
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
        /// <summary>f_R of the model (catalogue entry "hollenbeck").</summary>
        public static double DefaultFraction => CatalogueModels.RamanFractionOf(Catalogue.Get("hollenbeck", "raman"));

        // component positions (cm^-1), peak intensities, Gaussian FWHM (cm^-1), Lorentzian FWHM (cm^-1): Table 1 of the paper, from the
        // catalogue entry "hollenbeck" (with the unit-integral normalisation, computed once per loaded catalogue)
        private sealed class Table { public double[] Pos, Amp, Gfw, Lfw; public double Norm = double.NaN; }
        private static Table T => Catalogue.Cached("hollenbeck.table", () =>
        {
            var q = Catalogue.Get("hollenbeck", "raman").Q("response");
            return new Table { Pos = q.Array("modePositionsPerCm"), Amp = q.Array("peakIntensities"), Gfw = q.Array("gaussianFwhmPerCm"), Lfw = q.Array("lorentzianFwhmPerCm") };
        });

        public string Description => "Hollenbeck-Cantrell silica Raman (13-mode intermediate-broadening model)";

        private static double Raw(double t) => Raw(T, t);
        private static double Raw(Table tb, double t)
        {
            if (t < 0) return 0;
            double[] Pos = tb.Pos, Amp = tb.Amp, Gfw = tb.Gfw, Lfw = tb.Lfw;
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
            var tb = T;
            if (!double.IsNaN(tb.Norm)) return tb.Norm;
            // unit integral, evaluated on a fine grid (the response has decayed after ~2 ps)
            const int n = 400000; const double tMax = 4e-12; double dt = tMax / n, s = 0;
            for (int i = 0; i <= n; i++) s += (i == 0 || i == n ? 0.5 : 1.0) * Raw(tb, i * dt);
            tb.Norm = 1.0 / (s * dt);
            return tb.Norm;
        }

        public double[] Evaluate(double[] t)
        {
            double k = Norm();
            var tb = T;
            var h = new double[t.Length];
            for (int i = 0; i < t.Length; i++) h[i] = k * Raw(tb, t[i]);
            return h;
        }
    }
}
