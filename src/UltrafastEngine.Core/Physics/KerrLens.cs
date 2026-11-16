// KerrLens.cs -- self-focusing of the Gaussian beam of a 1-D element (docs/reference/nonlinear.md#kerr-lens-in-a-1-d-element).
//
// The 1-D pulse model carries a beam that is a fixed function of z. With "kerrLens" the executor builds that beam from the pulse instead:
// each step of the medium is a thin Kerr lens of the pulse's power (the thin-slice formula of CellModeSolver.KerrLensPowerPerM), and
// the second-moment radius of each axis follows the Ermakov equation
//     w'' = Lambda^2 / w^3 - K w,    K = c n2 I0 / (n w_axis^2),  I0 = 2 P / (pi wx wy),    Lambda = M^2 lambda / (pi n)
// (w is the radius, z the length in the medium of index n: a ray in the medium bends by (1/n) d(dn)/dx)
// (c = 1 "secondMoment", c = 4 "paraxial"). For a round beam this has the exact solution w^2 = w0^2 [1 + (1 - P / P_c)(z / zR)^2] with
// P_c = lambda^2 / (2 pi c n n2) (the self-trapping power of the model; Marburger's P_cr is 6 % lower for c = 1).
//
// Main class: KerrLens (static). Integrate(GaussianBeam, n2, indexN, powerW, lengthM, c, ...) returns the beam along z as a SampledBeam.
// Limits: one power for the whole element (the power at its entry), a Gaussian base beam, no feedback of the lens on the power.
using System;

namespace UltrafastEngine.Physics
{
    public static class KerrLens
    {
        /// <summary>The factor c of the thin-lens formula: 1 for "secondMoment", 4 for "paraxial".</summary>
        public static double LensFactor(string lensModel) => lensModel == "paraxial" ? 4.0 : 1.0;

        /// <summary>Radii along an element of length <paramref name="lengthM"/> of a Gaussian beam that sees the Kerr lens of the constant power
        /// <paramref name="powerW"/> in a medium of nonlinear index <paramref name="n2"/> (m^2/W) and linear index <paramref name="indexN"/>. <paramref name="collapsedAtM"/> is the z where
        /// the model collapses (radius below 1e-3 of the entry radius) or NaN; the radius is then held at that floor.</summary>
        public static SampledBeam Integrate(GaussianBeam b, double n2, double indexN, double powerW, double lengthM, double lensFactor, out double collapsedAtM, int intervals = 800)
        {
            collapsedAtM = double.NaN;
            double lamX = b.W0X * b.W0X / b.ZrX, lamY = b.W0Y * b.W0Y / b.ZrY;   // Lambda = w0^2 / zR
            double wx = b.RadiusX(0), wy = b.RadiusY(0);
            double vx = b.W0X * b.W0X * (0 - b.ZwX) / (b.ZrX * b.ZrX * wx), vy = b.W0Y * b.W0Y * (0 - b.ZwY) / (b.ZrY * b.ZrY * wy);   // dw/dz of the free beam
            double floorX = 1e-3 * wx, floorY = 1e-3 * wy;
            var z = new double[intervals + 1]; var rx = new double[intervals + 1]; var ry = new double[intervals + 1];
            z[0] = 0; rx[0] = wx; ry[0] = wy;
            double dzOut = lengthM / intervals;
            // sub-steps: resolve the free-beam length scale and the lens
            int sub = Math.Max(1, (int)Math.Ceiling(dzOut / (0.02 * Math.Min(b.ZrX, b.ZrY))));
            sub = Math.Min(sub, 200);
            double h = dzOut / sub;
            void Acc(double x, double y, out double ax, out double ay)
            {
                double i0 = 2 * powerW / (Math.PI * x * y);
                ax = lamX * lamX / (x * x * x) - lensFactor * n2 * i0 / (indexN * x);
                ay = lamY * lamY / (y * y * y) - lensFactor * n2 * i0 / (indexN * y);
            }
            bool dead = false;
            for (int k = 1; k <= intervals; k++)
            {
                for (int s = 0; s < sub && !dead; s++)
                {
                    Acc(wx, wy, out double a1x, out double a1y);
                    double x2 = wx + 0.5 * h * vx, y2 = wy + 0.5 * h * vy, vx2 = vx + 0.5 * h * a1x, vy2 = vy + 0.5 * h * a1y;
                    Acc(x2, y2, out double a2x, out double a2y);
                    double x3 = wx + 0.5 * h * vx2, y3 = wy + 0.5 * h * vy2, vx3 = vx + 0.5 * h * a2x, vy3 = vy + 0.5 * h * a2y;
                    Acc(x3, y3, out double a3x, out double a3y);
                    double x4 = wx + h * vx3, y4 = wy + h * vy3, vx4 = vx + h * a3x, vy4 = vy + h * a3y;
                    Acc(x4, y4, out double a4x, out double a4y);
                    wx += h / 6 * (vx + 2 * vx2 + 2 * vx3 + vx4); wy += h / 6 * (vy + 2 * vy2 + 2 * vy3 + vy4);
                    vx += h / 6 * (a1x + 2 * a2x + 2 * a3x + a4x); vy += h / 6 * (a1y + 2 * a2y + 2 * a3y + a4y);
                    if (!(wx > floorX) || !(wy > floorY))
                    {
                        dead = true; collapsedAtM = z[k - 1] + (s + 1) * h;
                        wx = Math.Max(wx, floorX); wy = Math.Max(wy, floorY);
                        if (double.IsNaN(wx)) wx = floorX; if (double.IsNaN(wy)) wy = floorY;
                    }
                }
                z[k] = k * dzOut; rx[k] = wx; ry[k] = wy;
            }
            return SampledBeam.FromArrays(z, rx, ry);
        }

        /// <summary>The power that sets the lens: the peak power, or the pulse-weighted power sum(P^2) / sum(P) (the lens power of each time slice
        /// is proportional to its P, and the slice carries energy proportional to P).</summary>
        public static double EffectivePowerW(double[] temporalPowerW, string mode)
        {
            if (mode != "pulseWeighted") { double m = 0; foreach (var p in temporalPowerW) if (p > m) m = p; return m; }
            double s1 = 0, s2 = 0; foreach (var p in temporalPowerW) { if (p > 0) { s1 += p; s2 += p * p; } }
            return s1 > 0 ? s2 / s1 : 0;
        }
    }
}
