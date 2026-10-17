using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Models;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Instruments
{
    /// <summary>
    /// The beam as cameras see it: the transverse field E(m, Omega_f) = P(m, Omega_f) at the profile's frequency
    /// samples, each carrying the pulse energy of its spectral band (weights w_f, optional band-pass), optionally
    /// propagated by a distance (refocused) in free space.
    /// </summary>
    internal sealed class CameraField
    {
        public ITransverseBasis Basis;
        public double[] Omega;    // nf frequency samples (rad/s)
        public double[] W;        // energy of each sample's spectral band (J)
        public Complex[] E;       // [m * nf + f], unit-norm per sample (sum_m Weights |E|^2 = 1)
        public bool Synthetic;    // built from a beam model (no carried transverse profile)
        public string Source;
        public int NF => Omega.Length;
        public double Energy => W.Sum();

        public static CameraField Acquire(InstrumentContext ctx, List<string> notes)
        {
            var p = ctx.Pulse; var spec = ctx.Spec;
            var prof = p.Profile;
            var cf = new CameraField();
            if (prof == null)
            {
                prof = Synthesize(ctx, out string src);
                cf.Synthetic = true; cf.Source = src;
                notes.Add("no transverse profile at \"" + ctx.Location + "\" (1-D elements): an ideal Gaussian from " + src + " is imaged; use a transverse model upstream to see real beam shapes");
            }
            else cf.Source = "transverse profile from '" + prof.Origin + "'";
            cf.Basis = prof.Basis; cf.Omega = (double[])prof.Omega.Clone();
            int nf = cf.NF, M = cf.Basis.M;
            // spectral weights per sample (nearest sample), with an optional band-pass filter
            var g = p.Grid; double[] band = spec.GetDoubleArray("bandM");
            double wLo = 0, wHi = double.PositiveInfinity;
            if (band != null && band.Length == 2) { wLo = 2 * Math.PI * Tools.C / Math.Max(band[0], band[1]); wHi = 2 * Math.PI * Tools.C / Math.Min(band[0], band[1]); }
            cf.W = new double[nf];
            for (int i = 0; i < g.N; i++)
            {
                double w = g.OmegaAbsolute[i]; if (w < wLo || w > wHi) continue;
                double s = (p.Ax[i].Real * p.Ax[i].Real + p.Ax[i].Imaginary * p.Ax[i].Imaginary + p.Ay[i].Real * p.Ay[i].Real + p.Ay[i].Imaginary * p.Ay[i].Imaginary) * g.DTime / g.N;
                if (s == 0) continue;
                int f = Nearest(cf.Omega, w);
                cf.W[f] += s;
            }
            if (band != null) notes.Add(string.Format(CultureInfo.InvariantCulture, "band-pass {0:G4}-{1:G4} nm: {2:P1} of the energy", Math.Min(band[0], band[1]) * 1e9, Math.Max(band[0], band[1]) * 1e9, cf.Energy / Math.Max(1e-300, p.TotalEnergyJ())));
            // detector quantum efficiency (any spectral-filter model: constant, table, gaussian, expression, callback)
            if (spec.Has("qe"))
            {
                var reg = ctx.Registry ?? ctx.Run?.Compiled?.Registry ?? ModelRegistry.CreateDefault();
                var qe = reg.Create<ISpectralFilter>("filter", ModelSpec.FromJson(spec["qe"], ModelSource.RunOverride, "camera qe"));
                var T = new double[nf]; var ph = new double[nf];
                qe.Evaluate(cf.Omega, T, ph);
                double before = cf.Energy;
                for (int f = 0; f < nf; f++) cf.W[f] *= Math.Max(0, T[f]);
                notes.Add(string.Format(CultureInfo.InvariantCulture, "quantum efficiency {0}: {1:P1} of the energy is detected", qe.Description, cf.Energy / Math.Max(1e-300, before)));
            }
            cf.E = (Complex[])prof.Data.Clone();
            double dz = spec.GetDouble("propagateM", 0);
            if (dz != 0) { cf.Propagate(dz); notes.Add(string.Format(CultureInfo.InvariantCulture, "field propagated {0:G4} m in free space before imaging", dz)); }
            var relay = spec.GetArray("relay");
            if (relay != null)
            {
                foreach (var stepV in relay)
                {
                    var step = stepV.AsObject();
                    if (step.Has("gapM")) cf.Propagate(step.GetDouble("gapM", 0));
                    else if (step.Has("focalLengthM")) cf.ApplyLens(step.GetDouble("focalLengthM", double.PositiveInfinity));
                    else throw new JsonException("camera relay steps are {\"gapM\": d} or {\"focalLengthM\": f}");
                }
                notes.Add("imaged through a relay of " + relay.Count + " steps (thin lenses and free-space gaps, exact diffraction on the field's grid)");
                double edge = cf.EdgeFraction();
                if (edge > 1e-3) notes.Add(string.Format(CultureInfo.InvariantCulture, "{0:P2} of the energy reaches the edge of the field's grid after the relay: the image is not reliable (use magnification for large image scales)", edge));
            }
            return cf;
        }

        private static int Nearest(double[] a, double x)
        {
            int b = 0; double d = double.PositiveInfinity;
            for (int i = 0; i < a.Length; i++) { double e = Math.Abs(a[i] - x); if (e < d) { d = e; b = i; } }
            return b;
        }

        /// <summary>An ideal Gaussian from the instrument's "beam", the element's beam model or the run's input beam.</summary>
        private static TransverseProfile Synthesize(InstrumentContext ctx, out string source)
        {
            IBeamModel beam = null; double z = 0; source = null;
            var reg = ctx.Registry ?? ctx.Run.Compiled.Registry;
            if (ctx.Spec.Has("beam"))
            {
                var bj = (JsonObject)JsonValue.Parse(ctx.Spec["beam"].ToJson(false));
                if (bj.GetString("kind", "") == "gaussian" && !bj.Has("wavelengthM") && !bj.Has("zRayleighM")) bj.Set("wavelengthM", 2 * Math.PI * Tools.C / ctx.Pulse.Grid.OmegaCenter);
                beam = reg.Create<IBeamModel>("beam", ModelSpec.FromJson(bj, ModelSource.RunOverride, "instrument beam"));
                source = "the instrument's \"beam\"";
            }
            else
            {
                string id = ctx.Location;
                var els = ctx.Run.Compiled.Elements;
                int k = id == "end" ? els.Count - 1 : els.FindIndex(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
                // the tracked Gaussian beam at this location (physics.beamTracking)
                var tracked = id == "input" ? ctx.Run.Compiled.BeamAtInput : (k >= 0 ? els[k].BeamOut : null);
                if (tracked != null) { beam = tracked.ToModel(1.0); z = 0; source = "the tracked Gaussian beam (" + tracked.Describe() + ")"; }
                for (int j = k; j >= 0 && beam == null; j--)
                {
                    var e = els[j];
                    if (e.Nonlinear?.Beam != null) { beam = e.Nonlinear.Beam; z = e.Nonlinear.LengthM; source = "the beam model of '" + e.Id + "' at its end"; }
                    else if (e.Active is Crystals.CrystalElement ce && ce.Beam != null) { beam = ce.Beam; z = ce.LengthM; source = "the beam model of '" + e.Id + "' at its end"; }
                    else if (e.Source?.Json != null && e.Source.Json.Has("beam") && e.Source.Json["beam"].Kind == JsonKind.Object)
                    {
                        var bj = (JsonObject)JsonValue.Parse(e.Source.Json["beam"].ToJson(false));
                        if (bj.GetString("kind", "") == "gaussian" && !bj.Has("wavelengthM") && !bj.Has("zRayleighM")) bj.Set("wavelengthM", 2 * Math.PI * Tools.C / ctx.Pulse.Grid.OmegaCenter);
                        beam = reg.Create<IBeamModel>("beam", ModelSpec.FromJson(bj, ModelSource.SystemData, "element " + e.Id + ".beam"));
                        z = e.LengthM; source = "the beam model of '" + e.Id + "' at its end";
                    }
                }
                if (beam == null)
                {
                    var ib = ctx.Run.Compiled.Run.InputBeam ?? ctx.Run.Compiled.System.InputBeam;
                    if (ib != null) { beam = reg.Create<IBeamModel>("beam", ModelSpec.FromJson(ib, ModelSource.RunOverride, "inputBeam")); source = "the input beam"; }
                }
            }
            if (beam == null) throw new InvalidOperationException("camera at \"" + ctx.Location + "\": no transverse profile and no beam model -- give the instrument a \"beam\" (e.g. {\"kind\": \"gaussian\", \"w0M\": 1e-3}) or use a transverse model upstream");
            var (wx, wy, R) = TransverseOps.BeamAt(beam, z);
            double wmax = Math.Max(wx, wy);
            ITransverseBasis basis = Math.Abs(wx - wy) < 1e-9 * wmax ? new RadialBasis(96, 4 * wmax) : (ITransverseBasis)new CartesianBasis(128, 128, 4 * wx, 4 * wy);
            var p = ctx.Pulse;
            var bins = TransverseOps.SampleBins(p.SpectralIntensity());
            var om = bins.Select(b => p.Grid.OmegaAbsolute[b]).ToArray();
            return TransverseOps.FromShape(basis, null, wx, wy, R, w => 1.0, om, "camera");
        }

        public void Propagate(double dz)
        {
            int nf = NF, M = Basis.M;
            Basis.ToK(E, nf);
            for (int m = 0; m < M; m++)
                for (int f = 0; f < nf; f++)
                {
                    double k = Omega[f] / Tools.C, kz2 = k * k - Basis.K2[m];
                    E[m * nf + f] *= kz2 > 0 ? Complex.FromPolarCoordinates(1, (Math.Sqrt(kz2) - k) * dz) : Complex.Zero;
                }
            Basis.ToSpace(E, nf);
        }

        /// <summary>Thin lens: multiplies every frequency's field by exp(-i k r^2 / 2f).</summary>
        public void ApplyLens(double f)
        {
            if (double.IsInfinity(f) || f == 0) return;
            int nf = NF, M = Basis.M;
            for (int m = 0; m < M; m++)
            {
                double r2 = Basis.X[m] * Basis.X[m] + Basis.Y[m] * Basis.Y[m];
                if (Basis is RadialBasis) r2 = Basis.Radius[m] * Basis.Radius[m];
                for (int q = 0; q < nf; q++) E[m * nf + q] *= Complex.FromPolarCoordinates(1, -Omega[q] / Tools.C * r2 / (2 * f));
            }
        }

        /// <summary>Fraction of the fluence in the outer 10 % of the grid (radius or half-width).</summary>
        public double EdgeFraction()
        {
            var F = Fluence(); double tot = 0, edge = 0, ext = Basis.Extent;
            for (int m = 0; m < Basis.M; m++)
            {
                double w = Basis.Weights[m] * F[m]; tot += w;
                double rr = Basis is RadialBasis ? Basis.Radius[m] : Math.Max(Math.Abs(Basis.X[m]), Math.Abs(Basis.Y[m]));
                if (rr > 0.9 * ext) edge += w;
            }
            return tot > 0 ? edge / tot : 0;
        }

        /// <summary>Fluence (J/m^2) at each basis point.</summary>
        public double[] Fluence()
        {
            int nf = NF, M = Basis.M; var F = new double[M];
            for (int m = 0; m < M; m++) { double s = 0; for (int f = 0; f < nf; f++) { var v = E[m * nf + f]; s += W[f] * (v.Real * v.Real + v.Imaginary * v.Imaginary); } F[m] = s; }
            return F;
        }

        /// <summary>1/e^2-equivalent radii from second moments of a fluence on the basis.</summary>
        public static (double Wx, double Wy, double Cx, double Cy) Moments(ITransverseBasis b, double[] F)
        {
            double s = 0, sx = 0, sy = 0, sxx = 0, syy = 0, srr = 0;
            for (int m = 0; m < b.M; m++)
            {
                double w = b.Weights[m] * F[m]; s += w;
                sx += w * b.X[m]; sy += w * b.Y[m]; sxx += w * b.X[m] * b.X[m]; syy += w * b.Y[m] * b.Y[m]; srr += w * b.Radius[m] * b.Radius[m];
            }
            if (s <= 0) return (double.NaN, double.NaN, 0, 0);
            if (b.Kind == "radial") { double wr = Math.Sqrt(2 * srr / s); return (wr, wr, 0, 0); }
            double cx = sx / s, cy = sy / s;
            return (2 * Math.Sqrt(Math.Max(0, sxx / s - cx * cx)), 2 * Math.Sqrt(Math.Max(0, syy / s - cy * cy)), cx, cy);
        }

        /// <summary>Sample a value defined on the basis points at (x, y) (radial: linear in r; cartesian: bilinear).</summary>
        public static Complex SampleAt(ITransverseBasis b, Func<int, Complex> val, double x, double y)
        {
            if (b is RadialBasis rb)
            {
                double r = Math.Sqrt(x * x + y * y);
                var R = rb.Radius; int M = rb.M;
                if (r <= R[0]) return val(0);
                if (r >= R[M - 1]) return r > rb.Extent ? Complex.Zero : val(M - 1) * ((rb.Extent - r) / (rb.Extent - R[M - 1]));
                int lo = 0, hi = M - 1;
                while (hi - lo > 1) { int mid = (lo + hi) / 2; if (R[mid] <= r) lo = mid; else hi = mid; }
                double t = (r - R[lo]) / (R[hi] - R[lo]);
                return val(lo) * (1 - t) + val(hi) * t;
            }
            var cb = (CartesianBasis)b;
            double fx = x / cb.Dx + cb.Nx / 2, fy = cb.Ny > 1 ? y / cb.Dy + cb.Ny / 2 : 0;
            int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
            if (ix < 0 || ix >= cb.Nx - 1 || (cb.Ny > 1 && (iy < 0 || iy >= cb.Ny - 1))) return Complex.Zero;
            double u = fx - ix;
            if (cb.Ny == 1) return val(ix) * (1 - u) + val(ix + 1) * u;
            double v = fy - iy;
            Complex V(int a, int c) => val(a * cb.Ny + c);
            return V(ix, iy) * (1 - u) * (1 - v) + V(ix + 1, iy) * u * (1 - v) + V(ix, iy + 1) * (1 - u) * v + V(ix + 1, iy + 1) * u * v;
        }
    }

    /// <summary>A pixelated sensor: geometry, auto field of view, quantisation and noise, image metrics.</summary>
    internal static class Sensor
    {
        public static (int Nx, int Ny) Pixels(JsonObject spec, int fallback)
        {
            if (spec.TryGet("pixels", out var pv))
            {
                if (pv.Kind == JsonKind.Array) { var a = pv.AsDoubleArray(); return ((int)a[0], (int)a[a.Length > 1 ? 1 : 0]); }
                int n = (int)pv.AsDouble(); return (n, n);
            }
            return (fallback, fallback);
        }

        /// <summary>Pixel-centre coordinates (image plane / magnification = object plane).</summary>
        public static double[] Axis(int n, double pitch, double centre)
        {
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = centre + (i - 0.5 * (n - 1)) * pitch;
            return a;
        }

        /// <summary>Quantise to "bitDepth" bits against "fullScaleJPerM2" (auto: 1.1 x peak) with "readNoiseCounts" rms.</summary>
        public static double[] Digitise(double[] img, JsonObject spec, InstrumentOutput o, int nx, int ny)
        {
            int bits = spec.GetInt("bitDepth", 0);
            if (bits <= 0) return img;
            double peak = img.Max();
            double full = spec.GetDouble("fullScaleJPerM2", 1.1 * peak);
            double noise = spec.GetDouble("readNoiseCounts", 0);
            double background = spec.GetDouble("backgroundCounts", 0);
            double hot = spec.GetDouble("hotPixelFraction", 0);
            int maxc = (1 << Math.Min(bits, 24)) - 1;
            var rnd = new Random(spec.GetInt("noiseSeed", 1));
            var r = new double[img.Length]; var counts = new double[img.Length]; int sat = 0;
            for (int i = 0; i < img.Length; i++)
            {
                double c = img[i] / full * maxc + background;
                if (noise > 0) { double u1 = 1 - rnd.NextDouble(), u2 = rnd.NextDouble(); c += noise * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }
                if (hot > 0 && rnd.NextDouble() < hot) c = maxc;
                if (c >= maxc) { c = maxc; sat++; }
                if (c < 0) c = 0;
                counts[i] = Math.Round(c);
                // the fluence image keeps the physical scale: the background offset is removed again, noise and quantisation stay
                r[i] = (counts[i] - background) * full / maxc;
            }
            o.Add("counts", counts, "counts", "raw sensor counts [x][y] (background, noise, quantisation, hot pixels)", nx, ny);
            Tools.Put(o, "saturatedPixelFraction", (double)sat / img.Length);
            Tools.Put(o, "fullScaleJPerM2", full);
            Tools.Put(o, "bitDepth", bits);
            return r;
        }

        /// <summary>Centroid, D4sigma widths (reported as 1/e^2-equivalent radii), peak and energy of an image [x][y].</summary>
        public static void Metrics(double[] img, double[] xs, double[] ys, InstrumentOutput o, string prefix = "")
        {
            int nx = xs.Length, ny = ys.Length; double dA = Math.Abs((xs.Length > 1 ? xs[1] - xs[0] : 1) * (ys.Length > 1 ? ys[1] - ys[0] : 1));
            double s = 0, sx = 0, sy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) { double v = img[i * ny + j]; s += v; sx += v * xs[i]; sy += v * ys[j]; sxx += v * xs[i] * xs[i]; syy += v * ys[j] * ys[j]; }
            double cx = s > 0 ? sx / s : 0, cy = s > 0 ? sy / s : 0;
            double wx = s > 0 ? 2 * Math.Sqrt(Math.Max(0, sxx / s - cx * cx)) : double.NaN, wy = s > 0 ? 2 * Math.Sqrt(Math.Max(0, syy / s - cy * cy)) : double.NaN;
            Tools.Put(o, prefix + "energyJ", s * dA);
            Tools.Put(o, prefix + "peakFluenceJPerM2", img.Max());
            Tools.Put(o, prefix + "centroidXM", cx); Tools.Put(o, prefix + "centroidYM", cy);
            Tools.Put(o, prefix + "radiusXM", wx); Tools.Put(o, prefix + "radiusYM", wy);
            Tools.Put(o, prefix + "ellipticity", Math.Min(wx, wy) / Math.Max(wx, wy));
        }
    }

    /// <summary>
    /// "nearFieldCamera": the fluence image a camera records at the instrument's location (or "propagateM" further
    /// on, e.g. to look at a focus), through an imaging system of "magnification" onto a sensor of "pixels"
    /// (n or [nx, ny]) with "pixelSizeM" (auto: fits ~5 beam radii), optional "bandM" band-pass filter, "bitDepth",
    /// "fullScaleJPerM2" and "readNoiseCounts". Reports centroid, 1/e^2 radii (D4sigma / 2), ellipticity, peak.
    /// </summary>
    internal sealed class NearFieldCamera : IInstrument
    {
        public string Kind => "nearFieldCamera";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var cf = CameraField.Acquire(ctx, o.Notes);
            var F = cf.Fluence();
            var (wx, wy, cx0, cy0) = CameraField.Moments(cf.Basis, F);
            var (nx, ny) = Sensor.Pixels(ctx.Spec, 256);
            double mag = ctx.Spec.GetDouble("magnification", 1.0);
            double pitchObj = ctx.Spec.Has("pixelSizeM") ? ctx.Spec.GetDouble("pixelSizeM", 0) / mag : 2 * 2.6 * Math.Max(wx, wy) / Math.Max(nx, ny);
            var xs = Sensor.Axis(nx, pitchObj, ctx.Spec.GetDouble("centerXM", 0));
            var ys = Sensor.Axis(ny, pitchObj, ctx.Spec.GetDouble("centerYM", 0));
            var img = new double[nx * ny];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    img[i * ny + j] = Math.Max(0, CameraField.SampleAt(cf.Basis, m => F[m], xs[i], ys[j]).Real);
            img = Sensor.Digitise(img, ctx.Spec, o, nx, ny);
            o.Add("x_m", xs, "m", "pixel x (object plane)");
            o.Add("y_m", ys, "m", "pixel y (object plane)");
            o.Add("fluence_j_m2", img, "J/m^2", "fluence image [x][y]", nx, ny);
            Sensor.Metrics(img, xs, ys, o);
            Tools.Put(o, "pixelSizeObjectM", pitchObj);
            Tools.Put(o, "m2", cf.Synthetic ? double.NaN : SafeM2(ctx));
            if (img[0] + img[nx * ny - 1] > 0 && Edge(img, nx, ny) > 1e-3) o.Notes.Add("the beam is clipped by the sensor: increase pixels or pixelSizeM");
            o.Values.Set("source", cf.Source);
        }

        private static double SafeM2(InstrumentContext ctx) { try { return TransverseOps.BeamQuality(ctx.Pulse.Profile); } catch { return double.NaN; } }

        internal static double Edge(double[] img, int nx, int ny)
        {
            double e = 0, s = img.Sum();
            for (int i = 0; i < nx; i++) { e += img[i * ny] + img[i * ny + ny - 1]; }
            for (int j = 0; j < ny; j++) { e += img[j] + img[(nx - 1) * ny + j]; }
            return s > 0 ? e / s : 0;
        }
    }

    /// <summary>
    /// "farFieldCamera": the fluence in the focal plane of an ideal lens of "focalLengthM" (default 1 m) -- the
    /// angular spectrum of the beam, each frequency scaled by lambda f. Same sensor options as the near-field
    /// camera. Reports spot radii, divergence (radius / f), the energy within the 1/e^2 radius and a Strehl ratio
    /// (peak / peak of the same near-field amplitude with a flat wavefront: a measure of wavefront quality).
    /// </summary>
    internal sealed class FarFieldCamera : IInstrument
    {
        public string Kind => "farFieldCamera";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var cf = CameraField.Acquire(ctx, o.Notes);
            double f = ctx.Spec.GetDouble("focalLengthM", 1.0);
            var b = cf.Basis; int nf = cf.NF, M = b.M;
            var flat = new Complex[cf.E.Length];
            for (int i = 0; i < flat.Length; i++) flat[i] = cf.E[i].Magnitude;
            var Ek = Spectrum(cf.E, b, nf);
            // auto field of view from the second moment of k
            double kbar = 0, k2 = 0, wsum = 0;
            for (int q = 0; q < nf; q++) { double kq = cf.Omega[q] / Tools.C; kbar += cf.W[q] * kq; wsum += cf.W[q]; }
            kbar /= Math.Max(1e-300, wsum);
            double norm = 0;
            for (int m = 0; m < M; m++) for (int q = 0; q < nf; q++) { double v = cf.W[q] * Ek[m * nf + q]; k2 += v * b.K2[m]; norm += v; }
            double rhoW = f * Math.Sqrt(2 * k2 / Math.Max(1e-300, norm)) / kbar; // 1/e^2-equivalent spot radius
            var (nx, ny) = Sensor.Pixels(ctx.Spec, 256);
            double mag = ctx.Spec.GetDouble("magnification", 1.0);
            double pitch = ctx.Spec.Has("pixelSizeM") ? ctx.Spec.GetDouble("pixelSizeM", 0) / mag : 2 * 3.0 * rhoW / Math.Max(nx, ny);
            var xs = Sensor.Axis(nx, pitch, ctx.Spec.GetDouble("centerXM", 0));
            var ys = Sensor.Axis(ny, pitch, ctx.Spec.GetDouble("centerYM", 0));
            var img = Image(cf.E, cf, f, xs, ys); var ideal = Image(flat, cf, f, xs, ys);
            double strehl = img.Max() / Math.Max(1e-300, ideal.Max());
            img = Sensor.Digitise(img, ctx.Spec, o, nx, ny);
            o.Add("x_m", xs, "m", "focal-plane x");
            o.Add("y_m", ys, "m", "focal-plane y");
            o.Add("fluence_j_m2", img, "J/m^2", "focal-plane fluence [x][y]", nx, ny);
            Sensor.Metrics(img, xs, ys, o);
            double rx = o.Values.GetDouble("radiusXM", double.NaN), ry = o.Values.GetDouble("radiusYM", double.NaN);
            Tools.Put(o, "focalLengthM", f);
            Tools.Put(o, "divergenceHalfAngleXRad", rx / f); Tools.Put(o, "divergenceHalfAngleYRad", ry / f);
            Tools.Put(o, "strehlRatio", strehl);
            // energy inside the 1/e^2 ellipse (86.5 % for a Gaussian)
            double inside = 0, tot = 0; double cx = o.Values.GetDouble("centroidXM", 0), cy = o.Values.GetDouble("centroidYM", 0);
            for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) { double v = img[i * ny + j]; tot += v; double u = (xs[i] - cx) / rx, w = (ys[j] - cy) / ry; if (u * u + w * w <= 1) inside += v; }
            Tools.Put(o, "energyInside1e2Fraction", tot > 0 ? inside / tot : double.NaN);
            if (NearFieldCamera.Edge(img, nx, ny) > 1e-3) o.Notes.Add("the focal spot is clipped by the sensor: increase pixels or pixelSizeM");
            o.Values.Set("source", cf.Source);
        }

        /// <summary>|E(k)|^2 for each mode and sample, normalised to unit power per sample.</summary>
        private static double[] Spectrum(Complex[] e, ITransverseBasis b, int nf)
        {
            var d = (Complex[])e.Clone();
            b.ToK(d, nf);
            int M = b.M; var r = new double[M * nf];
            var wk = KWeights(b);
            // the QDHT's k-side samples carry a factor 1/|J1(j_m)| (Guizar-Sicairos): undo it for intensities
            var corr = new double[M];
            for (int m = 0; m < M; m++) { if (b is RadialBasis rb) { double jm = Math.Sqrt(b.K2[m]) * rb.Extent, j1 = Bessel.J1(jm); corr[m] = j1 * j1; } else corr[m] = 1; }
            for (int q = 0; q < nf; q++)
            {
                double s = 0;
                for (int m = 0; m < M; m++) { var v = d[m * nf + q]; double p = corr[m] * (v.Real * v.Real + v.Imaginary * v.Imaginary); r[m * nf + q] = p; s += wk[m] * p; }
                if (s > 0) for (int m = 0; m < M; m++) r[m * nf + q] /= s;   // now sum_m wk |E~|^2 = 1 (per k-area)
            }
            return r;
        }

        /// <summary>k-space area elements of the modes (radial: r -> k scaling of the space weights; cartesian: uniform).</summary>
        private static double[] KWeights(ITransverseBasis b)
        {
            var w = new double[b.M];
            if (b is RadialBasis rb)
            {
                for (int m = 0; m < b.M; m++) { double k = Math.Sqrt(b.K2[m]); double s = k / rb.Radius[m]; w[m] = b.Weights[m] * s * s; }
            }
            else { var cb = (CartesianBasis)b; double dk = (2 * Math.PI / (cb.Nx * cb.Dx)) * (cb.Ny > 1 ? 2 * Math.PI / (cb.Ny * cb.Dy) : 1); for (int m = 0; m < b.M; m++) w[m] = dk; }
            return w;
        }

        /// <summary>Focal-plane fluence by direct Fourier (cartesian) / Hankel (radial) integration of each sample's
        /// field at the pixels' spatial frequencies k x / f: exact quadrature, independent of the grid's k sampling.
        /// I(x) = w_f |E~(k x / f)|^2 (k / f)^2 / (2 pi)^2 with E~(k) = integral E exp(-i k.r) dA.</summary>
        private static double[] Image(Complex[] e, CameraField cf, double f, double[] xs, double[] ys)
        {
            var b = cf.Basis; int nf = cf.NF, nx = xs.Length, ny = ys.Length, M = b.M;
            var img = new double[nx * ny];
            if (b is RadialBasis)
            {
                double rmax = 0; foreach (var x in xs) foreach (var y in ys) rmax = Math.Max(rmax, Math.Sqrt(x * x + y * y));
                int L = 800; var rho = Enumerable.Range(0, L).Select(l => rmax * l / (L - 1)).ToArray();
                var prof = new double[L];
                for (int q = 0; q < nf; q++)
                {
                    if (cf.W[q] <= 0) continue;
                    double k = cf.Omega[q] / Tools.C, scale = cf.W[q] * (k / f) * (k / f) / (4 * Math.PI * Math.PI);
                    System.Threading.Tasks.Parallel.For(0, L, l =>
                    {
                        double kr = k * rho[l] / f; Complex acc = Complex.Zero;
                        for (int m = 0; m < M; m++) acc += b.Weights[m] * e[m * nf + q] * Bessel.J0(kr * b.Radius[m]);
                        prof[l] += scale * (acc.Real * acc.Real + acc.Imaginary * acc.Imaginary);
                    });
                }
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                    {
                        double r = Math.Sqrt(xs[i] * xs[i] + ys[j] * ys[j]), u = r / rmax * (L - 1); int l = Math.Min(L - 2, (int)u); double t = u - l;
                        img[i * ny + j] = prof[l] * (1 - t) + prof[l + 1] * t;
                    }
                return img;
            }
            var cb = (CartesianBasis)b; int Nx = cb.Nx, Ny = cb.Ny;
            var xg = Enumerable.Range(0, Nx).Select(ix => cb.X[ix * Ny]).ToArray();
            var yg = Enumerable.Range(0, Ny).Select(iy => cb.Y[iy]).ToArray();
            for (int q = 0; q < nf; q++)
            {
                if (cf.W[q] <= 0) continue;
                double k = cf.Omega[q] / Tools.C, scale = cf.W[q] * (k / f) * (k / f) / (4 * Math.PI * Math.PI) * cb.Dx * cb.Dx * (Ny > 1 ? cb.Dy * cb.Dy : 1);
                // T[ix][j] = sum_iy E(ix, iy) exp(-i ky_j y_iy), then result[i][j] = sum_ix exp(-i kx_i x_ix) T[ix][j]
                var T = new Complex[Nx * ny];
                System.Threading.Tasks.Parallel.For(0, Nx, ix =>
                {
                    for (int j = 0; j < ny; j++)
                    {
                        double ky = k * ys[j] / f; Complex acc = Complex.Zero;
                        for (int iy = 0; iy < Ny; iy++) acc += e[(ix * Ny + iy) * nf + q] * Complex.FromPolarCoordinates(1, -ky * yg[iy]);
                        T[ix * ny + j] = acc;
                    }
                });
                System.Threading.Tasks.Parallel.For(0, nx, i =>
                {
                    double kx = k * xs[i] / f;
                    var ph = new Complex[Nx]; for (int ix = 0; ix < Nx; ix++) ph[ix] = Complex.FromPolarCoordinates(1, -kx * xg[ix]);
                    for (int j = 0; j < ny; j++)
                    {
                        Complex acc = Complex.Zero;
                        for (int ix = 0; ix < Nx; ix++) acc += ph[ix] * T[ix * ny + j];
                        img[i * ny + j] += scale * (acc.Real * acc.Real + acc.Imaginary * acc.Imaginary);
                    }
                });
            }
            return img;
        }

        private static double InterpSorted(double[] x, int[] order, Func<int, double> y, double v)
        {
            int n = order.Length;
            if (v <= x[order[0]]) return y(order[0]);
            if (v >= x[order[n - 1]]) return 0;
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (x[order[mid]] <= v) lo = mid; else hi = mid; }
            double t = (v - x[order[lo]]) / (x[order[hi]] - x[order[lo]]);
            return y(order[lo]) * (1 - t) + y(order[hi]) * t;
        }

        private static double InterpK(CartesianBasis cb, Func<int, double> y, double kx, double ky)
        {
            double dkx = 2 * Math.PI / (cb.Nx * cb.Dx), dky = cb.Ny > 1 ? 2 * Math.PI / (cb.Ny * cb.Dy) : 1;
            double fx = kx / dkx, fy = cb.Ny > 1 ? ky / dky : 0;
            if (Math.Abs(fx) >= cb.Nx / 2 - 1 || Math.Abs(fy) >= Math.Max(1, cb.Ny / 2 - 1)) return 0;
            int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
            double u = fx - ix, w = fy - iy;
            int W(int i, int n) => ((i % n) + n) % n;
            double V(int a, int c) => y(W(a, cb.Nx) * cb.Ny + (cb.Ny > 1 ? W(c, cb.Ny) : 0));
            if (cb.Ny == 1) return V(ix, 0) * (1 - u) + V(ix + 1, 0) * u;
            return V(ix, iy) * (1 - u) * (1 - w) + V(ix + 1, iy) * u * (1 - w) + V(ix, iy + 1) * (1 - u) * w + V(ix + 1, iy + 1) * u * w;
        }
    }

    /// <summary>
    /// "wavefrontSensor" (Shack-Hartmann-like): the wavefront (optical path difference, in waves) across the beam at
    /// one wavelength ("wavelengthM", default the spectral centroid), sampled on "pixels" lenslets (default 64), where
    /// the fluence exceeds "threshold" (1 %) of its peak. Zernike coefficients (Noll ordering, rms-normalised, in
    /// waves) are fitted over a circle of "apertureRadiusM" (default the 1/e^2 radius); piston and tilt are removed from
    /// the map. The defocus term gives the radius of curvature (R &gt; 0: diverging).
    /// </summary>
    /// <summary>
    /// "camera": one camera for every imaging plane (docs/reference/instruments.md#camera). <c>imaging</c> chooses what
    /// the sensor sees -- <c>near</c> (the beam at the instrument's location, optionally <c>propagateM</c> further and
    /// through a <c>relay</c> of thin lenses and gaps), <c>far</c> (the focal plane of a lens of <c>focalLengthM</c>) --
    /// and <c>magnification</c>; <c>sensor</c> describes the detector: <c>pixels</c>, <c>pixelSizeM</c>, <c>qe</c>
    /// (quantum efficiency, a spectral-filter model), <c>bitDepth</c>, <c>fullScaleJPerM2</c>, <c>readNoiseCounts</c>,
    /// <c>backgroundCounts</c>, <c>hotPixelFraction</c>, <c>noiseSeed</c>. Sensor keys may also be given at the top
    /// level (as in format version 1). The old kinds nearFieldCamera / farFieldCamera are this camera with
    /// plane near / far.
    /// </summary>
    public sealed class Camera : IInstrument
    {
        private readonly string _defaultPlane;
        public Camera(string defaultPlane = "near") { _defaultPlane = defaultPlane; }
        public string Kind => "camera";

        private static readonly string[] ImagingKeys = { "propagateM", "focalLengthM", "magnification", "relay" };
        private static readonly string[] SensorKeys = { "pixels", "pixelSizeM", "qe", "bitDepth", "fullScaleJPerM2", "readNoiseCounts", "backgroundCounts", "hotPixelFraction", "noiseSeed", "centerXM", "centerYM", "bandM" };

        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var spec = ctx.Spec;
            var imaging = spec.GetObject("imaging") ?? new JsonObject();
            var sensor = spec.GetObject("sensor") ?? new JsonObject();
            string plane = (imaging.GetString("plane") ?? spec.GetString("plane") ?? _defaultPlane).ToLowerInvariant();
            if (plane == "relay") plane = "near";
            if (plane != "near" && plane != "far") throw new JsonException("camera imaging.plane must be \"near\", \"far\" or \"relay\"");
            // one flat spec for the imaging implementations: top level, then imaging / sensor objects (they win)
            var flat = new JsonObject();
            foreach (var kv in spec) if (kv.Key != "imaging" && kv.Key != "sensor") flat.Set(kv.Key, kv.Value);
            foreach (var k in ImagingKeys) if (imaging.TryGet(k, out var v)) flat.Set(k, v);
            foreach (var kv in sensor) flat.Set(kv.Key, kv.Value);
            if (plane == "far" && !flat.Has("focalLengthM")) flat.Set("focalLengthM", 1.0);
            var sub = new InstrumentContext { Pulse = ctx.Pulse, Location = ctx.Location, Run = ctx.Run, Spec = flat, Registry = ctx.Registry };
            if (plane == "far") new FarFieldCamera().Measure(sub, o); else new NearFieldCamera().Measure(sub, o);
            o.Values.Set("imagingPlane", plane);
        }
    }

    public sealed class WavefrontSensor : IInstrument
    {
        public string Kind => "wavefrontSensor";
        public void Measure(InstrumentContext ctx, InstrumentOutput o)
        {
            var cf = CameraField.Acquire(ctx, o.Notes);
            var b = cf.Basis; int nf = cf.NF;
            double wsum = cf.W.Sum(), wc = 0; for (int q = 0; q < nf; q++) wc += cf.W[q] * cf.Omega[q]; wc /= Math.Max(1e-300, wsum);
            double wTarget = ctx.Spec.Has("wavelengthM") ? 2 * Math.PI * Tools.C / ctx.Spec.GetDouble("wavelengthM", 1e-6) : wc;
            int fq = 0; double best = double.PositiveInfinity;
            for (int q = 0; q < nf; q++) if (cf.W[q] > 1e-6 * wsum || nf == 1) { double d = Math.Abs(cf.Omega[q] - wTarget); if (d < best) { best = d; fq = q; } }
            double lam = 2 * Math.PI * Tools.C / cf.Omega[fq];
            var F = cf.Fluence();
            var (wx, wy, cx0, cy0) = CameraField.Moments(b, F);
            double ap = ctx.Spec.GetDouble("apertureRadiusM", Math.Max(wx, wy));
            int n = Sensor.Pixels(ctx.Spec, 64).Nx;
            double half = ctx.Spec.GetDouble("halfWidthM", 1.25 * ap);
            var xs = Sensor.Axis(n, 2 * half / n, 0); var ys = Sensor.Axis(n, 2 * half / n, 0);
            var field = new Complex[n * n]; double peak = 0;
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) { var v = CameraField.SampleAt(b, m => cf.E[m * nf + fq], xs[i], ys[j]); field[i * n + j] = v; peak = Math.Max(peak, v.Magnitude * v.Magnitude); }
            double thr = ctx.Spec.GetDouble("threshold", 0.01) * peak;
            var phase = Unwrap(field, n, thr);
            var wave = phase.Select(p => double.IsNaN(p) ? double.NaN : p / (2 * Math.PI)).ToArray();
            int J = Math.Max(3, Math.Min(36, ctx.Spec.GetInt("zernikeTerms", 15)));
            var coef = Zernike.Fit(wave, xs, ys, ap, J);
            // remove piston and tilt from the map
            var map = new double[n * n];
            double pv0 = double.PositiveInfinity, pv1 = double.NegativeInfinity, s2 = 0, s1 = 0; int cnt = 0;
            double rs2 = 0, rs1 = 0;
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
            {
                int k = i * n + j; double r = Math.Sqrt(xs[i] * xs[i] + ys[j] * ys[j]);
                if (double.IsNaN(wave[k])) { map[k] = double.NaN; continue; }
                double rho = r / ap, th = Math.Atan2(ys[j], xs[i]);
                double v = wave[k]; for (int jj = 1; jj <= Math.Min(3, J); jj++) v -= coef[jj - 1] * Zernike.Z(jj, rho, th);
                map[k] = v;
                if (rho <= 1)
                {
                    pv0 = Math.Min(pv0, v); pv1 = Math.Max(pv1, v); s1 += v; s2 += v * v; cnt++;
                    double vr = v - (J >= 4 ? coef[3] * Zernike.Z(4, rho, th) : 0); rs1 += vr; rs2 += vr * vr;
                }
            }
            o.Add("x_m", xs, "m", "lenslet x");
            o.Add("y_m", ys, "m", "lenslet y");
            o.Add("wavefront_waves", map, "waves", "wavefront (piston and tilt removed), NaN outside the threshold [x][y]", n, n);
            o.Add("zernike_index", Enumerable.Range(1, J).Select(x => (double)x).ToArray(), "", "Noll index");
            o.Add("zernike_waves", coef, "waves", "Zernike coefficients (rms-normalised, Noll)");
            Tools.Put(o, "wavelengthM", lam);
            Tools.Put(o, "apertureRadiusM", ap);
            double rms = cnt > 0 ? Math.Sqrt(Math.Max(0, s2 / cnt - (s1 / cnt) * (s1 / cnt))) : double.NaN;
            Tools.Put(o, "rmsWaves", rms);
            Tools.Put(o, "pvWaves", cnt > 0 ? pv1 - pv0 : double.NaN);
            Tools.Put(o, "rmsWithoutDefocusWaves", cnt > 0 ? Math.Sqrt(Math.Max(0, rs2 / cnt - (rs1 / cnt) * (rs1 / cnt))) : double.NaN);
            if (J >= 4)
            {
                Tools.Put(o, "defocusWaves", coef[3]);
                // OPD = lambda a4 sqrt3 (2 rho^2 - 1) = r^2 / (2R)  ->  R = a^2 / (4 sqrt3 lambda a4)
                Tools.Put(o, "radiusOfCurvatureM", Math.Abs(coef[3]) > 1e-12 ? ap * ap / (4 * Math.Sqrt(3) * lam * coef[3]) : double.PositiveInfinity);
            }
            if (J >= 6) { Tools.Put(o, "astigmatism0Waves", coef[5]); Tools.Put(o, "astigmatism45Waves", coef[4]); }
            if (J >= 8) { Tools.Put(o, "comaXWaves", coef[7]); Tools.Put(o, "comaYWaves", coef[6]); }
            if (J >= 11) Tools.Put(o, "sphericalWaves", coef[10]);
            o.Values.Set("source", cf.Source);
        }

        /// <summary>Phase map unwrapped from the centre: the centre row outward, then every column outward.</summary>
        internal static double[] Unwrap(Complex[] f, int n, double thr)
        {
            var ph = new double[n * n];
            for (int k = 0; k < ph.Length; k++) ph[k] = f[k].Magnitude * f[k].Magnitude >= thr ? f[k].Phase : double.NaN;
            int c = n / 2;
            double Wrap(double d) => d - 2 * Math.PI * Math.Round(d / (2 * Math.PI));
            var res = new double[n * n]; for (int k = 0; k < res.Length; k++) res[k] = double.NaN;
            res[c * n + c] = ph[c * n + c];
            for (int dir = -1; dir <= 1; dir += 2)
                for (int i = c + dir; i >= 0 && i < n; i += dir)
                {
                    double prev = res[(i - dir) * n + c], cur = ph[i * n + c];
                    res[i * n + c] = double.IsNaN(cur) || double.IsNaN(prev) ? double.NaN : prev + Wrap(cur - prev);
                }
            for (int i = 0; i < n; i++)
                for (int dir = -1; dir <= 1; dir += 2)
                    for (int j = c + dir; j >= 0 && j < n; j += dir)
                    {
                        double prev = res[i * n + j - dir], cur = ph[i * n + j];
                        res[i * n + j] = double.IsNaN(cur) || double.IsNaN(prev) ? double.NaN : prev + Wrap(cur - prev);
                    }
            return res;
        }
    }

    /// <summary>Zernike polynomials (Noll ordering, rms-normalised over the unit disk) and least-squares fitting.</summary>
    public static class Zernike
    {
        public static (int N, int M) NollToNm(int j)
        {
            int n = 0; while ((n + 1) * (n + 2) / 2 < j) n++;
            int k = j - n * (n + 1) / 2 - 1;
            int m = n % 2 == 0 ? 2 * ((k + 1) / 2) : 2 * (k / 2) + 1;
            return (n, m);
        }

        public static double Z(int j, double rho, double theta)
        {
            var (n, m) = NollToNm(j);
            double R = 0;
            for (int s = 0; s <= (n - m) / 2; s++)
                R += (s % 2 == 0 ? 1 : -1) * Fact(n - s) / (Fact(s) * Fact((n + m) / 2 - s) * Fact((n - m) / 2 - s)) * Math.Pow(rho, n - 2 * s);
            if (m == 0) return Math.Sqrt(n + 1) * R;
            return Math.Sqrt(2 * (n + 1)) * R * (j % 2 == 0 ? Math.Cos(m * theta) : Math.Sin(m * theta));
        }

        private static double Fact(int k) { double r = 1; for (int i = 2; i <= k; i++) r *= i; return r; }

        /// <summary>Fit map [x][y] (NaN = no data) over the disk of radius a: returns J coefficients (j = 1..J).</summary>
        public static double[] Fit(double[] map, double[] xs, double[] ys, double a, int J)
        {
            var A = new double[J, J]; var bb = new double[J]; var z = new double[J];
            int ny = ys.Length;
            for (int i = 0; i < xs.Length; i++)
                for (int j = 0; j < ny; j++)
                {
                    double v = map[i * ny + j]; if (double.IsNaN(v)) continue;
                    double rho = Math.Sqrt(xs[i] * xs[i] + ys[j] * ys[j]) / a; if (rho > 1) continue;
                    double th = Math.Atan2(ys[j], xs[i]);
                    for (int p = 0; p < J; p++) z[p] = Z(p + 1, rho, th);
                    for (int p = 0; p < J; p++) { bb[p] += z[p] * v; for (int q = 0; q < J; q++) A[p, q] += z[p] * z[q]; }
                }
            return Solve(A, bb, J);
        }

        private static double[] Solve(double[,] A, double[] b, int n)
        {
            var a = (double[,])A.Clone(); var x = (double[])b.Clone();
            for (int p = 0; p < n; p++) a[p, p] += 1e-12 * (1 + a[p, p]);
            for (int c = 0; c < n; c++)
            {
                int piv = c; for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
                if (Math.Abs(a[piv, c]) < 1e-300) continue;
                if (piv != c) { for (int k = 0; k < n; k++) { var t = a[c, k]; a[c, k] = a[piv, k]; a[piv, k] = t; } var tt = x[c]; x[c] = x[piv]; x[piv] = tt; }
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue; double f = a[r, c] / a[c, c]; if (f == 0) continue;
                    for (int k = c; k < n; k++) a[r, k] -= f * a[c, k]; x[r] -= f * x[c];
                }
            }
            for (int c = 0; c < n; c++) x[c] = Math.Abs(a[c, c]) > 1e-300 ? x[c] / a[c, c] : 0;
            return x;
        }
    }
}
