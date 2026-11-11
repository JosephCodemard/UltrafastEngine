// DispersiveResponses.cs -- the frequency-resolved part of time-domain responses (the Drude term of a plasma).
//
// A time-domain response writes Delta n(t) at the carrier; the propagators then multiply the product Delta n(t) A(t) by i k0 in the
// frequency domain. A response that implements IDispersiveResponse has a part, Delta n_d(t), whose index change depends on the frequency
// of the light it acts on. Here that part is applied as
//     dA/dz (omega) += i (omega / c) R(omega) FFT[ Delta n_d(t) A(t) ](omega),     R = Delta n_d(omega) / Delta n_d(omega_ref),
// for omega > 0 (no negative frequencies), the first-order expansion in the density with the density varying slowly against the optical cycle.
//
// Classes (internal): DispersiveTerm (a response with its Delta n_d(t) arrays), DispersiveResponses
//   Evaluate(ctx, responses, uRe, uIm, lockEach) -> terms or null   evaluates all responses; ordinary ones add into uRe / uIm
//   AddRate(terms, a, omegaAbs, rate, offset, firstBin, wCut)       adds the dispersive rate of the field a (time domain) to rate[offset + j]
//   AddToTime(terms, a, k0, n)                                      adds i k0 Delta n_d a into a time-domain array (for the step-size estimate)
using System.Collections.Generic;
using System.Numerics;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Physics
{
    internal sealed class DispersiveTerm
    {
        public IDispersiveResponse Response;
        public double[] Re, Im;
        public double OmegaRef;
    }

    internal static class DispersiveResponses
    {
        public static List<DispersiveTerm> Evaluate(ResponseContext ctx, IList<ITimeDomainResponse> responses, double[] uRe, double[] uIm, bool lockEach)
        {
            int n = ctx.N; var re = new double[n]; var im = new double[n];
            List<DispersiveTerm> terms = null;
            foreach (var resp in responses)
            {
                System.Array.Clear(re, 0, n); System.Array.Clear(im, 0, n);
                if (resp is IDispersiveResponse d && d.FrequencyResolved)
                {
                    var t = new DispersiveTerm { Response = d, Re = new double[n], Im = new double[n], OmegaRef = ctx.Omega0 };
                    if (lockEach) lock (resp) d.EvaluateSplit(ctx, re, im, t.Re, t.Im); else d.EvaluateSplit(ctx, re, im, t.Re, t.Im);
                    (terms ?? (terms = new List<DispersiveTerm>())).Add(t);
                }
                else if (lockEach) lock (resp) resp.Evaluate(ctx, re, im);
                else resp.Evaluate(ctx, re, im);
                for (int i = 0; i < n; i++) { uRe[i] += re[i]; uIm[i] += im[i]; }
            }
            return terms;
        }

        public static void AddRate(List<DispersiveTerm> terms, Complex[] a, double[] omegaAbs, Complex[] rate, int offset, int firstBin, double wCut)
        {
            int n = a.Length; double c = PhysicalConstants.SpeedOfLightMPerS;
            var prod = new Complex[n];
            foreach (var t in terms)
            {
                for (int i = 0; i < n; i++) prod[i] = new Complex(t.Re[i], t.Im[i]) * a[i];
                var f = SpectralTransform.TimeToFreq(prod);
                for (int j = firstBin; j < n; j++)
                {
                    double w = omegaAbs[j];
                    if (!(w > 0) || w > wCut) continue;
                    rate[offset + j] += Complex.ImaginaryOne * (w / c) * t.Response.Spectral(w, t.OmegaRef) * f[j];
                }
            }
        }

        public static void AddToTime(List<DispersiveTerm> terms, Complex[] a, double k0, Complex[] target)
        {
            foreach (var t in terms)
                for (int i = 0; i < a.Length; i++) target[i] += Complex.ImaginaryOne * k0 * new Complex(t.Re[i], t.Im[i]) * a[i];
        }
    }
}
