using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Instruments
{
    /// <summary>
    /// Where an instrument looks at the beam (docs/instruments.md, "Near field, far field and apertures"):
    /// <code>
    /// "sampling": { "plane": "near" | "far",          // far = the focal plane of a lens of focalLengthM
    ///               "focalLengthM": 0.2, "propagateM": 0.0,
    ///               "aperture": { "kind": "integrated" | "pinhole" | "fibre" | "iris" | "slit" | "point",
    ///                             "radiusM": 25e-6, "widthM": 50e-6, "centerXM": 0, "centerYM": 0, "na": 0.22 } }
    /// </code>
    /// The instrument then measures the light that passes the aperture: the local spectrum E(x, y, w) integrated
    /// over the aperture (power) with the aperture-averaged spectral phase (for temporal instruments). Uses the
    /// carried transverse profile, or an ideal Gaussian from the beam model in 1-D (the camera rules).
    /// </summary>
    public static class Sampling
    {
        private static readonly HashSet<string> Imaging = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "nearFieldCamera", "camera", "farFieldCamera", "wavefrontSensor", "beamProfile", "spaceTime" };

        /// <summary>True if the instrument kind images the beam itself (sampling does not apply).</summary>
        public static bool IsImaging(string kind) => Imaging.Contains(kind);

        public static PulseField Apply(InstrumentContext ctx, JsonObject s, InstrumentOutput o)
        {
            var p = ctx.Pulse;
            string plane = s.GetString("plane", "near").ToLowerInvariant();
            if (plane != "near" && plane != "far") throw new JsonException("sampling.plane must be \"near\" or \"far\"");
            double f = s.GetDouble("focalLengthM", 0.2);
            var ap = s.GetObject("aperture") ?? new JsonObject();
            string kind = ap.GetString("kind", s.Has("aperture") ? "pinhole" : "integrated").ToLowerInvariant();
            if (kind == "fiber") kind = "fibre";
            var ci = CultureInfo.InvariantCulture;
            string where = plane == "far" ? string.Format(ci, "far field (f = {0:G4} m)", f) : "near field" + (s.GetDouble("propagateM", 0) != 0 ? string.Format(ci, " {0:G4} m downstream", s.GetDouble("propagateM", 0)) : "");
            if (kind == "integrated")
            {
                o.Values.Set("sampling", where + ", whole beam");
                if (plane == "far" || s.GetDouble("propagateM", 0) != 0) o.Notes.Add("sampling the whole beam: near field, far field and any propagation give the same integrated pulse");
                return p;
            }
            // the field: the camera rules (carried profile, else an ideal Gaussian from a beam model)
            var sub = new InstrumentContext { Pulse = p, Location = ctx.Location, Run = ctx.Run, Registry = ctx.Registry, Spec = new JsonObject() };
            if (s.Has("propagateM")) sub.Spec.Set("propagateM", s.GetDouble("propagateM", 0));
            if (ctx.Spec.Has("beam")) sub.Spec.Set("beam", ctx.Spec["beam"]);
            var notes = new List<string>();
            var cf = CameraField.Acquire(sub, notes);
            foreach (var n in notes) o.Notes.Add(n.Replace("camera", "sampling").Replace("is imaged", "is sampled"));
            double cx = ap.GetDouble("centerXM", 0), cy = ap.GetDouble("centerYM", 0);
            double R = ap.GetDouble("radiusM", kind == "fibre" ? 25e-6 : 50e-6), W = ap.GetDouble("widthM", 2 * R);
            // quadrature points inside the aperture
            var pts = new List<(double X, double Y, double A)>();
            if (kind == "point") pts.Add((cx, cy, 1.0));
            else if (kind == "slit")
            {
                // full height: span the beam (+-4 beam radii on the plane)
                double ext = Extent(cf, plane, f);
                int nx = 9, ny = 61;
                for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++)
                    pts.Add((cx + W * ((i + 0.5) / nx - 0.5), -ext + 2 * ext * (j + 0.5) / ny, W / nx * 2 * ext / ny));
            }
            else
            {
                int nr = 8, nt = 16;
                for (int i = 0; i < nr; i++)
                {
                    double r0 = R * i / nr, r1 = R * (i + 1) / nr, rm = 0.5 * (r0 + r1), da = Math.PI * (r1 * r1 - r0 * r0) / nt;
                    for (int j = 0; j < nt; j++) { double t = 2 * Math.PI * (j + 0.5) / nt; pts.Add((cx + rm * Math.Cos(t), cy + rm * Math.Sin(t), da)); }
                }
            }
            int nf = cf.NF, M = cf.Basis.M;
            var T = new double[nf]; var ph = new double[nf];
            for (int q = 0; q < nf; q++)
            {
                double k = cf.Omega[q] / Tools.C, pw = 0; Complex co = Complex.Zero;
                foreach (var (x, y, a) in pts)
                {
                    Complex u = plane == "near" ? CameraField.SampleAt(cf.Basis, m => cf.E[m * nf + q], x, y) : FarField(cf, q, k, f, x, y);
                    pw += a * (u.Real * u.Real + u.Imaginary * u.Imaginary); co += a * u;
                }
                T[q] = kind == "point" ? pw : Math.Min(1, pw);   // point: power density (1/m^2) -> the spectrum shape only
                ph[q] = co.Phase;
            }
            if (kind == "point") { double mx = T.Max(); if (mx > 0) for (int q = 0; q < nf; q++) T[q] /= mx; }
            // unwrap the phase over the samples
            for (int q = 1; q < nf; q++) { while (ph[q] - ph[q - 1] > Math.PI) ph[q] -= 2 * Math.PI; while (ph[q] - ph[q - 1] < -Math.PI) ph[q] += 2 * Math.PI; }
            if (kind == "fibre")
            {
                double na = ap.GetDouble("na", 0.22);
                double conv = plane == "far" ? cf.Basis.Extent / 4 / f : 0;   // rough beam convergence half-angle at a focus
                if (conv > na) o.Notes.Add(string.Format(ci, "the beam converges at ~{0:G2} rad onto the fibre, more than its NA {1:G2}: coupling would be lower than computed", conv, na));
            }
            var g = p.Grid; var ax = new Complex[g.N]; var ay = new Complex[g.N];
            double e0 = p.TotalEnergyJ();
            for (int i = 0; i < g.N; i++)
            {
                double w = g.OmegaAbsolute[i], t, phi;
                if (nf == 1 || w <= cf.Omega[0]) { t = T[0]; phi = ph[0]; }
                else if (w >= cf.Omega[nf - 1]) { t = T[nf - 1]; phi = ph[nf - 1]; }
                else { int j = 1; while (cf.Omega[j] < w) j++; double u = (w - cf.Omega[j - 1]) / (cf.Omega[j] - cf.Omega[j - 1]); t = T[j - 1] * (1 - u) + T[j] * u; phi = ph[j - 1] * (1 - u) + ph[j] * u; }
                var fac = Complex.FromPolarCoordinates(Math.Sqrt(Math.Max(0, t)), phi);
                ax[i] = p.Ax[i] * fac; ay[i] = p.Ay[i] * fac;
            }
            var res = new PulseField(g, ax, ay, p.RepRateHz);
            double frac = res.TotalEnergyJ() / Math.Max(1e-300, e0);
            string apText = kind == "point" ? string.Format(ci, "point at ({0:G3}, {1:G3}) mm (spectral shape only)", cx * 1e3, cy * 1e3)
                : kind == "slit" ? string.Format(ci, "slit {0:G3} um wide at x = {1:G3} mm", W * 1e6, cx * 1e3)
                : string.Format(ci, "{0} r = {1:G3} um at ({2:G3}, {3:G3}) mm", kind, R * 1e6, cx * 1e3, cy * 1e3);
            o.Values.Set("sampling", where + ", " + apText);
            if (kind != "point") o.Values.Set("samplingTransmission", frac);
            if (cf.Synthetic) o.Notes.Add("1-D beam: the sampled spectrum differs from the integrated one only through the wavelength dependence of the (Gaussian) far-field spot");
            return res;
        }

        private static double Extent(CameraField cf, string plane, double f)
        {
            double w = cf.Basis.Extent / 4;          // ~ beam radius on the grid
            if (plane == "near") return 4 * w;
            double k = cf.Omega[cf.NF / 2] / Tools.C;
            return 4 * f / (k * w / 2);             // ~4 far-field radii
        }

        /// <summary>Far-field (focal plane) amplitude per unit area: u(x) = (k / 2 pi f) E~(k x / f), so integral |u|^2 dA = 1.</summary>
        private static Complex FarField(CameraField cf, int q, double k, double f, double x, double y)
        {
            var b = cf.Basis; int nf = cf.NF, M = b.M; Complex acc = Complex.Zero;
            double kx = k * x / f, ky = k * y / f;
            if (b is RadialBasis)
            {
                double kr = Math.Sqrt(kx * kx + ky * ky);
                for (int m = 0; m < M; m++) acc += b.Weights[m] * cf.E[m * nf + q] * Bessel.J0(kr * b.Radius[m]);
            }
            else
                for (int m = 0; m < M; m++) acc += b.Weights[m] * cf.E[m * nf + q] * Complex.FromPolarCoordinates(1, -(kx * b.X[m] + ky * b.Y[m]));
            return acc * (k / (2 * Math.PI * f));
        }
    }
}
