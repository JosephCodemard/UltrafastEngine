using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;

namespace UltrafastEngine.Optics
{
    /// <summary>
    /// A coated mirror on a substrate of finite thickness: the reflection of the front coating plus the echoes of the light that
    /// leaks through it, reflects from the back face and returns. Exact spectral transfer per polarisation (s, p):
    /// <code>
    /// H(w) = r + sum_{k=1..K} o_k E_k,   E_1 = t t' r_b a e^{i psi},   E_k = E_1 g^(k-1),   g = r' r_b a e^{i psi}
    /// psi(w) = 2 n(w) w d cos(theta_t) / c,   a = exp(-alpha d / cos(theta_t))
    /// </code>
    /// r, t: front coating, air to substrate; r', t': substrate to air through the coating (from the reciprocity and
    /// unitarity of the lossless coating: t' = t, r' = -r* t / t*); r_b: back face, substrate to air (bare Fresnel,
    /// a back coating, or a flat reflectance); o_k: beam overlap of the k-th echo (wedge, lateral shift). The delay of an echo
    /// is the group delay of psi, 2 n_g d cos(theta_t) / c, a consequence of n(w) -- it is not put in by hand.
    /// </summary>
    public sealed class MirrorSubstrateTerm : JonesTerm
    {
        private readonly string _label;
        public Material Before { get; } public Material Substrate { get; }
        public ICoating Front { get; } public ICoating Back { get; }
        public Func<double, double> Aoi { get; }
        public double ThicknessM { get; }
        private readonly double[] _overlap;
        private readonly double _b11, _b12, _b21, _b22;
        public int Orders => _overlap.Length;

        public MirrorSubstrateTerm(string label, Material before, Material substrate, ICoating front, ICoating back, Func<double, double> aoi, double thicknessM, double[] overlapPerOrder, double[] basis)
        {
            _label = label; Before = before; Substrate = substrate; Front = front ?? new StackCoating(null, null); Back = back ?? new StackCoating(null, "bare (Fresnel)");
            Aoi = aoi; ThicknessM = thicknessM; _overlap = overlapPerOrder;
            _b11 = basis[0]; _b12 = basis[1]; _b21 = basis[2]; _b22 = basis[3];
            Mechanism = "coating";
        }

        public override string Description => string.Format(CultureInfo.InvariantCulture, "{0}: mirror on {1}, {2:G5} mm substrate, {3} echo order{4}, front {5}, back {6}",
            _label, Substrate.Name, ThicknessM * 1e3, Orders, Orders == 1 ? "" : "s", Front.Description, Back.Description);

        /// <summary>The s and p reflection at w without (echoes = 0) or with the first <paramref name="orders"/> echoes.</summary>
        public (Complex S, Complex P) Reflection(double w, int orders)
        {
            var n0 = ThinFilm.ComplexIndex(Before, w); var ns = ThinFilm.ComplexIndex(Substrate, w);
            double th0 = Aoi(w);
            var c = Front.Evaluate(w, th0, n0, ns);
            if (orders <= 0) return (c.Rs, c.Rp);
            var sint = n0 * Math.Sin(th0) / ns;
            var cost = Complex.Sqrt(1.0 - sint * sint);
            double thT = Math.Asin(Math.Min(1.0, Math.Abs(sint.Real)));
            var b = Back.Evaluate(w, thT, ns, n0);
            double a = 1.0;
            var att = Substrate.Attenuation;
            if (att != null) { var al = new double[1]; att.Evaluate(new[] { w }, al); a = Math.Exp(-al[0] * ThicknessM / Math.Max(cost.Real, 1e-3)); }
            var ph = Complex.Exp(Complex.ImaginaryOne * 2.0 * w * ns * ThicknessM * cost / PhysicalConstants.SpeedOfLightMPerS);
            return (Total(c.Rs, c.Ts, b.Rs, a * ph, orders), Total(c.Rp, c.Tp, b.Rp, a * ph, orders));
        }

        private Complex Total(Complex r, Complex t, Complex rb, Complex roundTrip, int orders)
        {
            Complex rPrime = t.Magnitude > 1e-300 ? -Complex.Conjugate(r) * t / Complex.Conjugate(t) : r;
            var e = t * t * rb * roundTrip;                 // first echo
            var g = rPrime * rb * roundTrip;                // each further internal round trip
            var h = r;
            for (int k = 1; k <= orders; k++) { h += _overlap[k - 1] * e; e *= g; }
            return h;
        }

        protected override Complex RefValue(double w)
        {
            var (s, p) = Reflection(w, 0);
            var bs = Basis(s, p, out var xy);
            return Reference(bs.xx, xy, xy, bs.yy);
        }

        private (Complex xx, Complex yy) Basis(Complex a, Complex b, out Complex xy)
        {
            xy = _b11 * _b12 * a + _b21 * _b22 * b;
            return (_b11 * _b11 * a + _b21 * _b21 * b, _b12 * _b12 * a + _b22 * _b22 * b);
        }

        protected override void Jones(double[] omega, Complex[] xx, Complex[] xy, Complex[] yx, Complex[] yy)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                if (omega[i] <= 0) { xx[i] = xy[i] = yx[i] = yy[i] = Complex.Zero; continue; }
                var (s, p) = Reflection(omega[i], Orders);
                var d = Basis(s, p, out var cross);
                xx[i] = d.xx; yy[i] = d.yy; xy[i] = cross; yx[i] = cross;
            }
        }
    }

    /// <summary>Substrates of the mirror catalogue ("substrate": "FS-12.7mm"): name -> material and thickness (bare back face).</summary>
    public static class SubstrateCatalog
    {
        public static readonly (string Name, string Material, double ThicknessM)[] BuiltIn =
        {
            ("FS-3mm", "FusedSilica", 3.0e-3), ("FS-6.35mm", "FusedSilica", 6.35e-3), ("FS-9.5mm", "FusedSilica", 9.5e-3),
            ("FS-12.7mm", "FusedSilica", 12.7e-3), ("FS-25.4mm", "FusedSilica", 25.4e-3),
            ("BK7-6.35mm", "N-BK7", 6.35e-3), ("BK7-12.7mm", "N-BK7", 12.7e-3),
        };
    }
}
