using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Materials;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Optics
{
    /// <summary>
    /// What a lens, a curved (possibly off-axis) mirror or an aberrated surface does to a carried transverse profile:
    /// a quadratic phase with focal lengths f_x, f_y (astigmatism of an off-axis mirror: f_t = R cos θ / 2,
    /// f_s = R / (2 cos θ)) and a wavefront error W(x, y) (Zernike terms, random figure). A radial grid can only hold
    /// axisymmetric changes: astigmatism is averaged (harmonic mean of the focal lengths) and non-axisymmetric Zernike
    /// terms are dropped, each with a note. On a 1-D beam nothing changes; the element reports the Maréchal Strehl
    /// estimate of its aberrations for the tracked (or given) Gaussian beam.
    /// </summary>
    public sealed class SurfaceOpticsElement : IActiveElement
    {
        private readonly double _fx, _fy, _w0; private readonly Material _lensMat, _medium;
        public Aberration Aberration { get; }
        /// <summary>Beam radius used for the 1-D Strehl estimate (NaN: none).</summary>
        public double BeamRadiusForStrehlM { get; set; } = double.NaN;
        private readonly string _what;

        public SurfaceOpticsElement(string what, double fx, double fy, Material lensMaterial, Material medium, double referenceOmega, Aberration aberration)
        {
            _what = what; _fx = fx; _fy = fy; _lensMat = lensMaterial; _medium = medium; _w0 = referenceOmega; Aberration = aberration;
        }

        public string Kind => "surfaceOptics";
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => true;
        public bool HasPower => !double.IsNaN(_fx) && !double.IsInfinity(_fx) || !double.IsNaN(_fy) && !double.IsInfinity(_fy);

        public string Description
        {
            get
            {
                string s = _what;
                if (HasPower) s += Math.Abs(_fx - _fy) < 1e-12 * Math.Abs(_fx) ? string.Format(CultureInfo.InvariantCulture, ", f = {0:G5} m", _fx) : string.Format(CultureInfo.InvariantCulture, ", f_x = {0:G5} m, f_y = {1:G5} m", _fx, _fy);
                if (_lensMat != null) s += " (" + _lensMat.Name + ", chromatic)";
                if (Aberration != null && Aberration.Any) s += "; " + Aberration.Describe();
                return s;
            }
        }

        private double Chromatic(double f, double w)
        {
            if (_lensMat == null || double.IsNaN(f) || double.IsInfinity(f)) return f;
            double n0 = _lensMat.IndexAt(_w0), n = _lensMat.IndexAt(w), m0 = _medium?.IndexAt(_w0) ?? 1, m = _medium?.IndexAt(w) ?? 1;
            return f * (n0 - m0) / (n - m);
        }

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            if (p.Profile == null)
            {
                if (Aberration != null && Aberration.Any)
                {
                    double lam = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / p.Grid.OmegaCenter;
                    double w = BeamRadiusForStrehlM;
                    if (double.IsNaN(w) && ctx.Element.BeamIn != null) w = Math.Sqrt(ctx.Element.BeamIn.RadiusX * ctx.Element.BeamIn.RadiusY);
                    if (!double.IsNaN(w) && w > 0)
                    {
                        var (rms, strehl) = Aberration.StrehlForGaussian(w, lam);
                        ctx.Record.Values["wavefrontRmsM"] = rms; ctx.Record.Values["strehlEstimate"] = strehl;
                        ctx.Note(string.Format(CultureInfo.InvariantCulture, "aberrations on a 1-D beam: wavefront rms {0:G3} nm (piston/tilt removed) over w = {1:G4} mm, Marechal Strehl ~ {2:G4}; the pulse itself is unchanged (use a transverse model to see the far field)", rms * 1e9, w * 1e3, strehl));
                    }
                    else ctx.Note("aberrations on a 1-D beam have no effect (no beam radius for a Strehl estimate; add inputBeam for beam tracking or a transverse model)");
                }
                else if (HasPower) ctx.Note("lens/mirror power: no transverse profile carried (1-D beam) -- it acts on the tracked Gaussian beam and on spatial elements downstream");
                return p;
            }
            var q = p.WithSpectrum(p.Ax, p.Ay);
            var prof = p.Profile.Clone();
            var B = prof.Basis; int nf = prof.Omega.Length;
            bool radial = B is RadialBasis;
            if (HasPower)
            {
                double fxr = _fx, fyr = double.IsNaN(_fy) ? _fx : _fy;
                if (radial && Math.Abs(fxr - fyr) > 1e-9 * Math.Abs(fxr))
                {
                    double fm = 2 / (1 / fxr + 1 / fyr);
                    ctx.Note(string.Format(CultureInfo.InvariantCulture, "astigmatism (f_x = {0:G5} m, f_y = {1:G5} m) cannot be held by a radial grid: applied the mean power f = {2:G5} m (use a cartesian transverse model)", fxr, fyr, fm));
                    fxr = fyr = fm;
                }
                for (int f = 0; f < nf; f++)
                {
                    double w = prof.Omega[f], k = (_medium?.IndexAt(w) ?? 1.0) * w / PhysicalConstants.SpeedOfLightMPerS;
                    double fx = Chromatic(fxr, w), fy = Chromatic(fyr, w);
                    double cx = double.IsInfinity(fx) || double.IsNaN(fx) ? 0 : 1 / fx, cy = double.IsInfinity(fy) || double.IsNaN(fy) ? 0 : 1 / fy;
                    for (int m = 0; m < B.M; m++)
                    {
                        double ph = radial ? -k * B.Radius[m] * B.Radius[m] * cx / 2 : -k * (B.X[m] * B.X[m] * cx + B.Y[m] * B.Y[m] * cy) / 2;
                        prof.Data[m * nf + f] *= Complex.FromPolarCoordinates(1, ph);
                        if (prof.DataY != null) prof.DataY[m * nf + f] *= Complex.FromPolarCoordinates(1, ph);
                    }
                }
            }
            if (Aberration != null && Aberration.Any)
            {
                var opd = new double[B.M];
                bool dropped = radial && Aberration.Terms.Any(t => t.Waves != 0 && Zernike.Noll(t.J).M != 0);
                for (int m = 0; m < B.M; m++) opd[m] = Aberration.Opd(B.X[m], B.Y[m], radial);
                if (Aberration.RandomRmsM > 0)
                {
                    if (B is CartesianBasis cb) { var rf = Aberration.RandomFigure(cb); for (int m = 0; m < B.M; m++) opd[m] += rf[m]; }
                    else ctx.Note("random surface figure needs a cartesian transverse model; ignored on the radial grid");
                }
                if (dropped) ctx.Note("non-axisymmetric Zernike terms are ignored on a radial grid (use a cartesian transverse model)");
                for (int f = 0; f < nf; f++)
                {
                    double k = prof.Omega[f] / PhysicalConstants.SpeedOfLightMPerS;
                    for (int m = 0; m < B.M; m++)
                    {
                        var ph = Complex.FromPolarCoordinates(1, k * opd[m]);
                        prof.Data[m * nf + f] *= ph;
                        if (prof.DataY != null) prof.DataY[m * nf + f] *= ph;
                    }
                }
                // weighted rms over the beam (piston / tilt not removed), for the record
                double s0 = 0, s1 = 0, s2 = 0; int fm2 = prof.MainSample;
                for (int m = 0; m < B.M; m++) { double i2 = B.Weights[m] * Math.Pow(prof.Data[m * nf + fm2].Magnitude, 2); s0 += i2; s1 += i2 * opd[m]; s2 += i2 * opd[m] * opd[m]; }
                if (s0 > 0) ctx.Record.Values["wavefrontRmsM"] = Math.Sqrt(Math.Max(0, s2 / s0 - (s1 / s0) * (s1 / s0)));
            }
            q.Profile = prof;
            return q;
        }
    }
}
