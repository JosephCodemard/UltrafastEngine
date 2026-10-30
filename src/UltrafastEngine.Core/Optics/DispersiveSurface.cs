using System;
using System.Globalization;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Optics
{
    /// <summary>
    /// Angular dispersion on a carried cartesian profile (physics.spatioTemporal = "profile"): every stored frequency is tilted by
    /// its exact deviation from the carrier, exp(i q(omega) x) with q = k_out sin(theta_out(omega) - theta_out(omega0)), and the
    /// tangential axis is magnified by cos(theta_out) / cos(theta_in). The field then holds the coupling itself: spatial elements
    /// downstream spread the colours and two gratings recombine them, with no help from the tracker's phase.
    /// </summary>
    public sealed class DispersiveSurfaceElement : IActiveElement
    {
        public Func<double, double> TiltWavenumber;
        public double Magnification = 1;
        public bool TangentialIsX = true;

        public string Kind => "dispersiveSurface";
        public string Description => "angular dispersion on the carried profile";
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => true;

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            if (p.Profile == null) return p;
            if (!(p.Profile.Basis is CartesianBasis b))
            {
                ctx.Result.Warnings.Add("Element '" + ctx.Element.Id + "': angular dispersion on a profile needs a cartesian grid; the radial profile passes unchanged.");
                return p;
            }
            var prof = p.Profile.Clone(); int nf = prof.Omega.Length, M = b.M;
            int nAxis = TangentialIsX ? b.Nx : b.Ny, stride = TangentialIsX ? b.Ny : 1;
            if (nAxis < 2) { ctx.Result.Warnings.Add("Element '" + ctx.Element.Id + "': the grid has no points along the plane of incidence."); return p; }
            double dAxis = TangentialIsX ? b.Dx : b.Dy;
            var coord = TangentialIsX ? b.X : b.Y;
            var old = (Complex[])prof.Data.Clone();
            for (int f = 0; f < nf; f++)
            {
                double q = TiltWavenumber(prof.Omega[f]), s = 0;
                for (int m = 0; m < M; m++)
                {
                    // the value at x / a, on the same line of the grid
                    double u = coord[m] / Magnification / dAxis + nAxis / 2;
                    int iu = (int)Math.Floor(u); double t = u - iu;
                    int here = TangentialIsX ? m / b.Ny : m % b.Ny;
                    Complex v = Complex.Zero;
                    if (iu >= 0 && iu < nAxis - 1)
                    {
                        int m0 = m + (iu - here) * stride;
                        v = old[m0 * nf + f] * (1 - t) + old[(m0 + stride) * nf + f] * t;
                    }
                    v *= Complex.FromPolarCoordinates(1, q * coord[m]);
                    prof.Data[m * nf + f] = v;
                    s += b.Weights[m] * (v.Real * v.Real + v.Imaginary * v.Imaginary);
                }
                if (s > 0) { double norm = 1 / Math.Sqrt(s); for (int m = 0; m < M; m++) prof.Data[m * nf + f] *= norm; }
            }
            // stored frequencies must be close enough for the interpolation between them across the beam
            double w = 2 * (TangentialIsX ? prof.SecondMomentRadius().Wx : prof.SecondMomentRadius().Wy), step = 0;
            for (int f = 1; f < nf; f++) step = Math.Max(step, Math.Abs(TiltWavenumber(prof.Omega[f]) - TiltWavenumber(prof.Omega[f - 1])) * w);
            ctx.Record.Values["profileTiltStepRad"] = step;
            if (step > Math.PI / 2) ctx.Result.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Element '{0}': the tilt changes by {1:G3} rad across the beam between stored frequencies: raise transverse.spectralSamples.", ctx.Element.Id, step));
            var q2 = p.WithSpectrum(p.Ax, p.Ay); q2.Profile = prof;
            return q2;
        }
    }
}
