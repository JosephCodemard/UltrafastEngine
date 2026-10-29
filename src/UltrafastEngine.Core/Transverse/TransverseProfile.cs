using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Transverse
{
    /// <summary>
    /// Per-element transverse model:
    /// <code>"transverse": {"model": "radial", "points": 64, "radiusM": 3e-3, "transport": "auto",
    ///                       "initialProfile": {"kind": "superGaussian", "order": 4}}</code>
    /// model: "none" (1-D, default), "radial" (axisymmetric, Hankel) or "cartesian" (x-y grid, FFT).
    /// transport: how the incoming beam's transverse profile is used -- "auto" (exact if it was propagated
    /// physically since the last spatial element, otherwise rescaled to this element's beam size), "exact",
    /// "rescale", or "beam" (ignore any carried profile and start from the beam model).
    /// </summary>
    public sealed class TransverseSpec
    {
        public string Model { get; set; } = "none";
        public int Points { get; set; } = 64;
        public int Nx { get; set; } = 64;
        public int Ny { get; set; } = 64;
        public double RadiusM { get; set; } = double.NaN;
        public double HalfWidthXM { get; set; } = double.NaN;
        public double HalfWidthYM { get; set; } = double.NaN;
        /// <summary>Grid extent as a multiple of the largest expected beam radius (when no explicit extent is given).</summary>
        public double ExtentFactor { get; set; } = 3.5;
        public string Transport { get; set; } = "auto";
        public JsonObject InitialProfile { get; set; }
        /// <summary>Non-paraxial k_z = sqrt(k^2 - k_perp^2) (default) or paraxial k - k_perp^2 / 2k.</summary>
        public bool Paraxial { get; set; }
        /// <summary>"auto" (vector when the beam carries a y component), "scalar" or "vector" (Jones fields on the grid).</summary>
        public string Polarization { get; set; } = "auto";
        /// <summary>"envelope" (paraxial nonlinear coupling) or "uppe" (unidirectional pulse propagation equation: the
        /// nonlinear drive of each plane-wave component scales with n omega / (c k_z)).</summary>
        public string Equation { get; set; } = "envelope";

        public bool IsSpatial => Model == "radial" || Model == "cartesian";

        public static TransverseSpec FromJson(JsonValue v)
        {
            var s = new TransverseSpec();
            if (v == null || v.IsNull) return s;
            if (v.Kind == JsonKind.String) { s.Model = v.AsString().ToLowerInvariant(); return s; }
            var o = v.AsObject();
            s.Model = o.GetString("model", "radial").ToLowerInvariant();
            if (s.Model != "none" && s.Model != "radial" && s.Model != "cartesian")
                throw new JsonException("transverse.model must be \"none\", \"radial\" or \"cartesian\" (got \"" + s.Model + "\").");
            s.Points = o.GetInt("points", 64);
            s.Nx = o.GetInt("nx", o.GetInt("points", 64));
            s.Ny = o.GetInt("ny", s.Nx);
            s.RadiusM = o.GetDouble("radiusM", double.NaN);
            s.HalfWidthXM = o.GetDouble("halfWidthXM", o.GetDouble("halfWidthM", double.NaN));
            s.HalfWidthYM = o.GetDouble("halfWidthYM", s.HalfWidthXM);
            s.ExtentFactor = o.GetDouble("extentFactor", 3.5);
            s.Transport = o.GetString("transport", "auto").ToLowerInvariant();
            s.InitialProfile = o.GetObject("initialProfile");
            s.Paraxial = o.GetBool("paraxial", false);
            s.Polarization = o.GetString("polarization", "auto").ToLowerInvariant();
            if (s.Polarization != "auto" && s.Polarization != "scalar" && s.Polarization != "vector") throw new JsonException("transverse.polarization must be \"auto\", \"scalar\" or \"vector\".");
            s.Equation = o.GetString("equation", "envelope").ToLowerInvariant();
            if (s.Equation != "envelope" && s.Equation != "uppe") throw new JsonException("transverse.equation must be \"envelope\" or \"uppe\".");
            return s;
        }

        public ITransverseBasis CreateBasis(double beamRadiusM)
        {
            double r = double.IsNaN(RadiusM) ? ExtentFactor * beamRadiusM : RadiusM;
            if (Model == "radial") return new RadialBasis(Points, r);
            double hx = double.IsNaN(HalfWidthXM) ? ExtentFactor * beamRadiusM : HalfWidthXM;
            double hy = double.IsNaN(HalfWidthYM) ? hx : HalfWidthYM;
            return new CartesianBasis(Nx, Ny, hx, hy);
        }
    }

    /// <summary>
    /// The transverse shape the main beam carries between spatial elements: P(r, omega) = E(r, omega) / A(omega),
    /// normalised so that sum_m w_m |P_m|^2 = 1 at every stored frequency (A is the collapsed 1-D envelope in sqrt(W)).
    /// Stored at a reduced set of absolute frequencies and interpolated in between.
    /// </summary>
    public sealed class TransverseProfile
    {
        public ITransverseBasis Basis { get; }
        public double[] Omega { get; }
        /// <summary>[m * Omega.Length + f]</summary>
        public Complex[] Data { get; }
        /// <summary>Shape of the y polarisation, same layout, when it differs from x (vector propagation); null = same as x.</summary>
        public Complex[] DataY { get; set; }
        /// <summary>True while the profile has only been transported physically (spatial elements, lenses,
        /// curved mirrors, zero-length surfaces) since it was created.</summary>
        public bool Exact { get; set; } = true;
        public string Origin { get; set; }
        /// <summary>Pulse energy carried by each frequency sample's spectral band (relative weights for beam metrics);
        /// null = equal weights.</summary>
        public double[] SampleWeights { get; set; }

        /// <summary>Index of the sample carrying the most energy (the reference for single-frequency metrics).</summary>
        public int MainSample => SampleWeights == null ? Omega.Length / 2 : Array.IndexOf(SampleWeights, SampleWeights.Max());

        public TransverseProfile(ITransverseBasis basis, double[] omega, Complex[] data, string origin)
        {
            Basis = basis; Omega = omega; Data = data; Origin = origin;
        }

        public TransverseProfile Clone() => new TransverseProfile(Basis, (double[])Omega.Clone(), (Complex[])Data.Clone(), Origin) { Exact = Exact, SampleWeights = (double[])SampleWeights?.Clone(), DataY = (Complex[])DataY?.Clone() };

        /// <summary>Profile at each absolute frequency (linear interpolation; held constant outside), layout [m * n + i].</summary>
        public Complex[] At(double[] omegaAbs) => At(omegaAbs, false);

        /// <summary>Profile of the x (or, with y = true, the y) polarisation at each absolute frequency.</summary>
        public Complex[] At(double[] omegaAbs, bool y)
        {
            var Data = y && DataY != null ? DataY : this.Data;
            int n = omegaAbs.Length, M = Basis.M, nf = Omega.Length;
            var r = new Complex[M * n];
            for (int i = 0; i < n; i++)
            {
                double w = omegaAbs[i];
                int f0; double t;
                if (nf == 1 || w <= Omega[0]) { f0 = 0; t = 0; }
                else if (w >= Omega[nf - 1]) { f0 = nf - 1; t = 0; }
                else
                {
                    int lo = 0, hi = nf - 1;
                    while (hi - lo > 1) { int mid = (lo + hi) / 2; if (Omega[mid] <= w) lo = mid; else hi = mid; }
                    f0 = lo; t = (w - Omega[lo]) / (Omega[hi] - Omega[lo]);
                }
                double norm = 0;
                for (int m = 0; m < M; m++)
                {
                    var a = Data[m * nf + f0];
                    var v = t == 0 ? a : a * (1 - t) + Data[m * nf + f0 + 1] * t;
                    r[m * n + i] = v;
                    norm += Basis.Weights[m] * (v.Real * v.Real + v.Imaginary * v.Imaginary);
                }
                // the samples have unit norm (sum w |P|^2 = 1); keep it between samples so E = A P carries |A|^2 exactly
                if (t != 0 && norm > 0) { double sc = 1 / Math.Sqrt(norm); for (int m = 0; m < M; m++) r[m * n + i] *= sc; }
            }
            return r;
        }

        /// <summary>Spectrally-weighted second-moment (1/e^2-equivalent) beam radius (m); for cartesian (wx, wy).</summary>
        public (double Wx, double Wy) SecondMomentRadius()
        {
            int M = Basis.M, nf = Omega.Length;
            double sx = 0, sy = 0, s0 = 0;
            for (int m = 0; m < M; m++)
            {
                double p = 0; for (int f = 0; f < nf; f++) p += (SampleWeights?[f] ?? 1.0) * (Data[m * nf + f].Real * Data[m * nf + f].Real + Data[m * nf + f].Imaginary * Data[m * nf + f].Imaginary);
                p *= Basis.Weights[m];
                s0 += p;
                if (Basis.Kind == "radial") sx += p * Basis.Radius[m] * Basis.Radius[m];
                else { sx += p * Basis.X[m] * Basis.X[m]; sy += p * Basis.Y[m] * Basis.Y[m]; }
            }
            if (s0 <= 0) return (double.NaN, double.NaN);
            if (Basis.Kind == "radial") { double w = Math.Sqrt(2 * sx / s0); return (w, w); }
            return (2 * Math.Sqrt(sx / s0), 2 * Math.Sqrt(sy / s0));
        }

        public string Describe()
        {
            var (wx, wy) = SecondMomentRadius();
            return string.Format(CultureInfo.InvariantCulture, "{0}, w = {1:G4}{2} mm, from {3}{4}", Basis.Kind, wx * 1e3, Basis.Kind == "radial" ? "" : " x " + (wy * 1e3).ToString("G4", CultureInfo.InvariantCulture), Origin, Exact ? "" : " (rescaled)");
        }
    }

    /// <summary>Building, collapsing, rescaling and measuring transverse fields.</summary>
    public static class TransverseOps
    {
        private const double C = PhysicalConstants.SpeedOfLightMPerS;

        /// <summary>Beam model at z: radius (x, y) and wavefront radius of curvature (m; +inf = flat; positive = diverging).</summary>
        public static (double Wx, double Wy, double R) BeamAt(IBeamModel b, double z)
        {
            double wx = b.RadiusX(z), wy = b.RadiusY(z);
            double R;
            if (b is GaussianBeam g)
            {
                double dz = z - 0.5 * (g.ZwX + g.ZwY), zr = 0.5 * (g.ZrX + g.ZrY);
                R = Math.Abs(dz) < 1e-15 ? double.PositiveInfinity : dz * (1 + (zr / dz) * (zr / dz));
            }
            else
            {
                // geometric estimate from the expansion rate: 1/R = (dw/dz) / w
                double h = 1e-4;
                double w0 = 0.5 * (wx + wy), w1 = 0.5 * (b.RadiusX(z + h) + b.RadiusY(z + h)), wm = 0.5 * (b.RadiusX(Math.Max(0, z - h)) + b.RadiusY(Math.Max(0, z - h)));
                double dw = (w1 - wm) / (z - h < 0 ? h + z : 2 * h);
                R = Math.Abs(dw) < 1e-12 ? double.PositiveInfinity : w0 / dw;
            }
            return (wx, wy, R);
        }

        /// <summary>
        /// A normalised profile on the basis from a shape spec: {"kind": "gaussian"} (default), "superGaussian"
        /// ("order"), "flatTop" (super-Gaussian of order 10), with 1/e^2 radii wx, wy and wavefront curvature R
        /// (phase +k r^2 / 2R, k = n omega / c). Sampled at the given absolute frequencies.
        /// </summary>
        public static TransverseProfile FromShape(ITransverseBasis basis, JsonObject shape, double wx, double wy, double R, Func<double, double> indexAt, double[] omegas, string origin)
        {
            string kind = (shape?.GetString("kind", "gaussian") ?? "gaussian").ToLowerInvariant();
            int order = kind == "flattop" ? 10 : kind == "supergaussian" ? Math.Max(1, shape.GetInt("order", 4)) : 1;
            double dx = shape?.GetDouble("decenterXM", 0) ?? 0, dy = shape?.GetDouble("decenterYM", 0) ?? 0;
            double tilt = shape?.GetDouble("tiltXRad", 0) ?? 0;
            int M = basis.M, nf = omegas.Length;
            var amp = new double[M];
            for (int m = 0; m < M; m++)
            {
                double x = basis.X[m] - dx, y = basis.Y[m] - dy;
                double q = basis.Kind == "radial" ? Math.Pow(basis.Radius[m] / wx, 2) : x * x / (wx * wx) + y * y / (wy * wy);
                amp[m] = Math.Exp(-Math.Pow(q, order));
            }
            var seedPhase = SeedNoise(basis, shape, amp, wx, origin);
            double norm = 0; for (int m = 0; m < M; m++) norm += basis.Weights[m] * amp[m] * amp[m];
            if (!(norm > 0)) throw new InvalidOperationException("transverse profile has no energy on the grid (beam larger than the grid or zero radius)");
            norm = 1 / Math.Sqrt(norm);
            var data = new Complex[M * nf];
            for (int f = 0; f < nf; f++)
            {
                double k = indexAt(omegas[f]) * omegas[f] / C;
                for (int m = 0; m < M; m++)
                {
                    double r2 = basis.Radius[m] * basis.Radius[m];
                    double ph = double.IsInfinity(R) ? 0 : k * r2 / (2 * R);
                    if (tilt != 0) ph += k * Math.Sin(tilt) * basis.X[m];
                    if (seedPhase != null) ph += seedPhase[m];
                    data[m * nf + f] = Complex.FromPolarCoordinates(amp[m] * norm, ph);
                }
            }
            return new TransverseProfile(basis, omegas, data, origin);
        }

        /// <summary>
        /// Seeds of small-scale structure on the initial field (the starting point of small-scale self-focusing, Bespalov-Talanov):
        /// <c>ripple</c> {periodM, relativeAmplitude, angleDeg, phaseRad}: amplitude x (1 + a cos(2 pi (x cos t + y sin t) / period + phase));
        /// <c>noise</c> {relativeAmplitudeRms, phaseRmsRad, correlationLengthM, seed, components}: a smooth random amplitude and phase,
        /// N(x, y) = sqrt(2/K) sum cos(kx x + ky y + phi_k) with Gaussian-distributed wavevectors of rms 1/correlationLength, unit variance.
        /// Multiplies <paramref name="amp"/> in place; returns the phase (rad) to add, or null. Cartesian grids only.
        /// </summary>
        private static double[] SeedNoise(ITransverseBasis basis, JsonObject shape, double[] amp, double w, string origin)
        {
            var rip = shape?.GetObject("ripple"); var noi = shape?.GetObject("noise");
            if (rip == null && noi == null) return null;
            if (basis.Kind == "radial") throw new InvalidOperationException("element " + origin + ": initialProfile ripple / noise need a cartesian transverse grid (\"model\": \"cartesian\").");
            int M = basis.M;
            if (rip != null)
            {
                double period = rip.GetDouble("periodM", double.NaN), a = rip.GetDouble("relativeAmplitude", 0), ph0 = rip.GetDouble("phaseRad", 0), ang = rip.GetDouble("angleDeg", 0) * Math.PI / 180;
                if (!(period > 0)) throw new InvalidOperationException("element " + origin + ": initialProfile.ripple.periodM is needed.");
                for (int m = 0; m < M; m++) amp[m] *= 1 + a * Math.Cos(2 * Math.PI * (basis.X[m] * Math.Cos(ang) + basis.Y[m] * Math.Sin(ang)) / period + ph0);
            }
            if (noi == null) return null;
            double aRms = noi.GetDouble("relativeAmplitudeRms", 0), pRms = noi.GetDouble("phaseRmsRad", 0);
            double corr = noi.GetDouble("correlationLengthM", double.NaN);
            if (!(corr > 0)) throw new InvalidOperationException("element " + origin + ": initialProfile.noise.correlationLengthM is needed.");
            int K = Math.Max(16, noi.GetInt("components", 256));
            var rnd = new Random(noi.GetInt("seed", 1));
            Func<double, double[]> field = rms =>
            {
                var f = new double[M]; if (!(rms > 0)) return f;
                for (int k = 0; k < K; k++)
                {
                    double u1 = 1 - rnd.NextDouble(), u2 = rnd.NextDouble(), u3 = 1 - rnd.NextDouble(), u4 = rnd.NextDouble();
                    double kx = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2) / corr, ky = Math.Sqrt(-2 * Math.Log(u3)) * Math.Cos(2 * Math.PI * u4) / corr, p = 2 * Math.PI * rnd.NextDouble();
                    for (int m = 0; m < M; m++) f[m] += Math.Cos(kx * basis.X[m] + ky * basis.Y[m] + p);
                }
                double s = rms * Math.Sqrt(2.0 / K); for (int m = 0; m < M; m++) f[m] *= s;
                return f;
            };
            var an = field(aRms); var pn = field(pRms);
            for (int m = 0; m < M; m++) amp[m] *= Math.Max(0, 1 + an[m]);
            return pRms > 0 ? pn : null;
        }

        /// <summary>Frequencies at which to store a profile: up to <paramref name="count"/> bins spread over the
        /// spectral support of <paramref name="spectrum"/> (relative level 1e-6).</summary>
        public static int[] SampleBins(double[] spectrum, int count = 48)
        {
            int n = spectrum.Length; double mx = spectrum.Max();
            int lo = 0, hi = n - 1;
            while (lo < n - 1 && spectrum[lo] < 1e-6 * mx) lo++;
            while (hi > 0 && spectrum[hi] < 1e-6 * mx) hi--;
            if (hi <= lo) return new[] { Array.IndexOf(spectrum, mx) };
            int k = Math.Min(count, hi - lo + 1);
            var bins = new SortedSet<int>();
            for (int j = 0; j < k; j++) bins.Add(lo + (int)Math.Round((double)j * (hi - lo) / Math.Max(1, k - 1)));
            return bins.ToArray();
        }

        /// <summary>Full field E (layout [m * n + i], sqrt(W/m^2)) from a 1-D spectrum and a profile.</summary>
        public static Complex[] Expand(Complex[] a, double[] omegaAbs, TransverseProfile p)
        {
            var e = p.At(omegaAbs);
            int n = a.Length, M = p.Basis.M;
            for (int m = 0; m < M; m++) { int o = m * n; for (int i = 0; i < n; i++) e[o + i] *= a[i]; }
            return e;
        }

        /// <summary>Full y field from the 1-D y spectrum and the profile (its y shape, or the x shape when it has none).</summary>
        public static Complex[] ExpandY(Complex[] a, double[] omegaAbs, TransverseProfile p)
        {
            var e = p.At(omegaAbs, true);
            int n = a.Length, M = p.Basis.M;
            for (int m = 0; m < M; m++) { int o = m * n; for (int i = 0; i < n; i++) e[o + i] *= a[i]; }
            return e;
        }

        /// <summary>Collapse x and y fields: 1-D spectra (each exact in energy) and a profile with separate x and y shapes.</summary>
        public static (Complex[] Ax, Complex[] Ay) CollapseVector(Complex[] ex, Complex[] ey, ITransverseBasis basis, double[] omegaAbs, string origin, out TransverseProfile profile)
        {
            int M = basis.M, n = omegaAbs.Length;
            var ax = Collapse(ex, basis, omegaAbs, origin, out var px);
            var ay = new Complex[n]; var pwT = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0; Complex c = Complex.Zero;
                for (int m = 0; m < M; m++) { var v = ey[m * n + i]; double w = basis.Weights[m]; s += w * (v.Real * v.Real + v.Imaginary * v.Imaginary); c += w * v; }
                ay[i] = Complex.FromPolarCoordinates(Math.Sqrt(s), c.Magnitude > 0 ? c.Phase : 0);
                pwT[i] = s + ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary;
            }
            // re-sample both shapes at bins chosen from the total spectrum
            var bins = SampleBins(pwT);
            var om = bins.Select(b => omegaAbs[b]).ToArray(); int nf = bins.Length;
            var dx = new Complex[M * nf]; var dy = new Complex[M * nf];
            for (int f = 0; f < nf; f++)
            {
                int i = bins[f];
                for (int m = 0; m < M; m++)
                {
                    if (ax[i].Magnitude > 0) dx[m * nf + f] = ex[m * n + i] / ax[i];
                    if (ay[i].Magnitude > 0) dy[m * nf + f] = ey[m * n + i] / ay[i];
                }
            }
            var sw = new double[nf];
            for (int i = 0; i < n; i++)
            {
                if (pwT[i] <= 0) continue;
                int best = 0; double d = double.PositiveInfinity;
                for (int f = 0; f < nf; f++) { double dd = Math.Abs(om[f] - omegaAbs[i]); if (dd < d) { d = dd; best = f; } }
                sw[best] += pwT[i];
            }
            // a component that is zero at a sample takes the other component's shape there (keeps the profile normalised)
            for (int f = 0; f < nf; f++)
            {
                bool zx = true, zy = true;
                for (int m = 0; m < M && (zx || zy); m++) { if (dx[m * nf + f] != Complex.Zero) zx = false; if (dy[m * nf + f] != Complex.Zero) zy = false; }
                if (zx && !zy) for (int m = 0; m < M; m++) dx[m * nf + f] = dy[m * nf + f];
                if (zy && !zx) for (int m = 0; m < M; m++) dy[m * nf + f] = dx[m * nf + f];
            }
            profile = new TransverseProfile(basis, om, dx, origin) { SampleWeights = sw, DataY = dy };
            return (ax, ay);
        }

        /// <summary>
        /// Collapse a full field to the 1-D envelope A (exact integrated spectrum |A|^2 = sum_m w_m |E_m|^2, phase =
        /// area-weighted coherent phase) and the profile E / A sampled at a reduced set of frequencies.
        /// </summary>
        public static Complex[] Collapse(Complex[] e, ITransverseBasis basis, double[] omegaAbs, string origin, out TransverseProfile profile)
        {
            int M = basis.M, n = omegaAbs.Length;
            var a = new Complex[n]; var pw = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0; Complex c = Complex.Zero;
                for (int m = 0; m < M; m++)
                {
                    var v = e[m * n + i]; double w = basis.Weights[m];
                    s += w * (v.Real * v.Real + v.Imaginary * v.Imaginary);
                    c += w * v;
                }
                pw[i] = s;
                double ph = c.Magnitude > 0 ? c.Phase : 0;
                a[i] = Complex.FromPolarCoordinates(Math.Sqrt(s), ph);
            }
            var bins = SampleBins(pw);
            var om = bins.Select(b => omegaAbs[b]).ToArray();
            int nf = bins.Length;
            var data = new Complex[M * nf];
            for (int f = 0; f < nf; f++)
            {
                int i = bins[f];
                if (a[i].Magnitude <= 0) continue;
                for (int m = 0; m < M; m++) data[m * nf + f] = e[m * n + i] / a[i];
            }
            // energy of each sample's band (nearest sample) -> weights for spectrally averaged beam metrics
            var sw = new double[nf];
            for (int i = 0; i < n; i++)
            {
                if (pw[i] <= 0) continue;
                int best = 0; double d = double.PositiveInfinity;
                for (int f = 0; f < nf; f++) { double dd = Math.Abs(om[f] - omegaAbs[i]); if (dd < d) { d = dd; best = f; } }
                sw[best] += pw[i];
            }
            profile = new TransverseProfile(basis, om, data, origin) { SampleWeights = sw };
            return a;
        }

        /// <summary>Weighted quadratic-phase coefficient a (phase ~ a r^2) of each stored frequency of a profile.</summary>
        public static double[] Curvature(TransverseProfile p)
        {
            var B = p.Basis; int M = B.M, nf = p.Omega.Length;
            var res = new double[nf];
            // order points by radius, unwrap along r
            var order = Enumerable.Range(0, M).OrderBy(m => B.Radius[m]).ToArray();
            for (int f = 0; f < nf; f++)
            {
                var ph = new double[M]; double prev = 0, acc = 0; bool first = true;
                foreach (int m in order)
                {
                    double v = p.Data[m * nf + f].Phase;
                    if (first) { acc = v; first = false; }
                    else { double d = v - prev; d -= 2 * Math.PI * Math.Round(d / (2 * Math.PI)); acc += d; }
                    prev = v; ph[m] = acc;
                }
                // weighted least squares ph = b + a r^2
                double sw = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
                for (int m = 0; m < M; m++)
                {
                    double w = B.Weights[m] * p.Data[m * nf + f].Magnitude * p.Data[m * nf + f].Magnitude, x = B.Radius[m] * B.Radius[m];
                    sw += w; sx += w * x; sy += w * ph[m]; sxx += w * x * x; sxy += w * x * ph[m];
                }
                double det = sw * sxx - sx * sx;
                res[f] = Math.Abs(det) > 0 ? (sw * sxy - sx * sy) / det : 0;
            }
            return res;
        }

        /// <summary>True if both bases sample the same points.</summary>
        private static bool SameSampling(ITransverseBasis a, ITransverseBasis b)
        {
            if (a.Kind != b.Kind || a.M != b.M) return false;
            for (int m = 0; m < a.M; m++)
            {
                double tol = 1e-12 * (Math.Abs(a.Extent) + 1e-30);
                if (Math.Abs(a.Radius[m] - b.Radius[m]) > tol) return false;
                if (a.X != null && b.X != null && (Math.Abs(a.X[m] - b.X[m]) > tol || Math.Abs(a.Y[m] - b.Y[m]) > tol)) return false;
            }
            return true;
        }

        /// <summary>
        /// The profile resampled onto <paramref name="target"/> with its shape scaled to new radii (wx, wy), its
        /// quadratic phase replaced by the curvature R of the new beam (k = n omega / c) -- the "rescaled" transport.
        /// Pass double.NaN radii to keep the size and R = NaN to keep the phase (pure resampling).
        /// </summary>
        public static TransverseProfile Rescale(TransverseProfile p, ITransverseBasis target, double wx, double wy, double R, Func<double, double> indexAt, string origin)
        {
            if (p.DataY != null)
            {
                var px = p.Clone(); px.DataY = null;
                var py = new TransverseProfile(p.Basis, (double[])p.Omega.Clone(), (Complex[])p.DataY.Clone(), p.Origin) { Exact = p.Exact, SampleWeights = p.SampleWeights };
                var rx = Rescale(px, target, wx, wy, R, indexAt, origin);
                rx.DataY = Rescale(py, target, wx, wy, R, indexAt, origin).Data;
                return rx;
            }
            // identical sampling (e.g. consecutive elements with the same transverse spec): no interpolation
            if (double.IsNaN(wx) && double.IsNaN(wy) && double.IsNaN(R) && SameSampling(p.Basis, target))
                return new TransverseProfile(target, p.Omega, (Complex[])p.Data.Clone(), origin) { Exact = p.Exact, SampleWeights = p.SampleWeights };
            var (ox, oy) = p.SecondMomentRadius();
            double sx = double.IsNaN(wx) ? 1 : wx / ox, sy = double.IsNaN(wy) ? sx : wy / oy;
            if (p.Basis.Kind == "radial") sy = sx;
            // the quadratic phase is removed before interpolating (it can change by radians per grid step) and
            // re-applied exactly on the target grid: the new beam's curvature if R is given, else the old one scaled
            var curv = Curvature(p);
            int nf = p.Omega.Length, Mt = target.M, Ms = p.Basis.M;
            var flat = p.Clone();
            for (int f = 0; f < nf; f++)
                for (int m = 0; m < Ms; m++) flat.Data[m * nf + f] *= Complex.FromPolarCoordinates(1, -curv[f] * p.Basis.Radius[m] * p.Basis.Radius[m]);
            var data = new Complex[Mt * nf];
            for (int f = 0; f < nf; f++)
            {
                double k = indexAt(p.Omega[f]) * p.Omega[f] / C;
                for (int m = 0; m < Mt; m++)
                {
                    double x = target.X[m] / sx, y = target.Y[m] / sy;
                    var v = Sample(flat, f, x, y) / Math.Sqrt(sx * sy);
                    double r2 = target.Radius[m] * target.Radius[m];
                    double ph = double.IsNaN(R) ? curv[f] * (x * x + y * y) : (double.IsInfinity(R) ? 0 : k * r2 / (2 * R));
                    data[m * nf + f] = v * Complex.FromPolarCoordinates(1, ph);
                }
                // renormalise
                double s = 0; for (int m = 0; m < Mt; m++) s += target.Weights[m] * (data[m * nf + f].Real * data[m * nf + f].Real + data[m * nf + f].Imaginary * data[m * nf + f].Imaginary);
                if (s > 0) { double c = 1 / Math.Sqrt(s); for (int m = 0; m < Mt; m++) data[m * nf + f] *= c; }
            }
            return new TransverseProfile(target, (double[])p.Omega.Clone(), data, origin) { Exact = false, SampleWeights = p.SampleWeights };
        }

        /// <summary>Profile value at (x, y) for stored frequency f (linear interpolation in r for radial, bilinear for cartesian; 0 outside).</summary>
        private static Complex Sample(TransverseProfile p, int f, double x, double y)
        {
            var B = p.Basis; int nf = p.Omega.Length;
            if (B is RadialBasis rb)
            {
                double r = Math.Sqrt(x * x + y * y);
                var R = rb.Radius;
                if (r <= R[0]) return p.Data[0 * nf + f];
                if (r >= R[rb.M - 1]) return Complex.Zero;
                int lo = 0, hi = rb.M - 1;
                while (hi - lo > 1) { int mid = (lo + hi) / 2; if (R[mid] <= r) lo = mid; else hi = mid; }
                double t = (r - R[lo]) / (R[hi] - R[lo]);
                return p.Data[lo * nf + f] * (1 - t) + p.Data[hi * nf + f] * t;
            }
            var cb = (CartesianBasis)B;
            double fx = x / cb.Dx + cb.Nx / 2, fy = cb.Ny > 1 ? y / cb.Dy + cb.Ny / 2 : 0;
            int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
            if (ix < 0 || ix >= cb.Nx - 1 || (cb.Ny > 1 && (iy < 0 || iy >= cb.Ny - 1))) return Complex.Zero;
            double tx = fx - ix, ty = fy - iy;
            Complex V(int a, int b) => p.Data[(a * cb.Ny + b) * nf + f];
            if (cb.Ny == 1) return V(ix, 0) * (1 - tx) + V(ix + 1, 0) * tx;
            return V(ix, iy) * (1 - tx) * (1 - ty) + V(ix + 1, iy) * tx * (1 - ty) + V(ix, iy + 1) * (1 - tx) * ty + V(ix + 1, iy + 1) * tx * ty;
        }

        /// <summary>Thin lens on a profile: phase -k(omega) r^2 / (2 f(omega)).</summary>
        public static TransverseProfile ApplyLens(TransverseProfile p, Func<double, double> focalLengthAt, Func<double, double> indexAt)
        {
            var q = p.Clone(); var B = q.Basis; int nf = q.Omega.Length;
            for (int f = 0; f < nf; f++)
            {
                double w = q.Omega[f], k = indexAt(w) * w / C, fl = focalLengthAt(w);
                if (double.IsInfinity(fl) || double.IsNaN(fl)) continue;
                for (int m = 0; m < B.M; m++)
                {
                    var ph = Complex.FromPolarCoordinates(1, -k * B.Radius[m] * B.Radius[m] / (2 * fl));
                    q.Data[m * nf + f] *= ph;
                    if (q.DataY != null) q.DataY[m * nf + f] *= ph;
                }
            }
            return q;
        }

        /// <summary>
        /// Beam quality factor at one stored frequency. The fitted quadratic phase is removed first (M^2 is invariant
        /// under a thin lens, and the residual field is smooth), &lt;k^2&gt; is taken spectrally from the basis transform
        /// (exact) and the residual position-momentum cross moment by finite differences:
        /// radial M^2 = sqrt(&lt;r^2&gt;&lt;k^2&gt; - &lt;r k_r&gt;^2); cartesian (x) M^2 = 2 sqrt(&lt;x^2&gt;&lt;kx^2&gt; - &lt;x kx&gt;^2).
        /// </summary>
        public static double BeamQuality(TransverseProfile p, int f = -1)
        {
            int nf = p.Omega.Length; var B = p.Basis; int M = B.M;
            if (f < 0) f = p.MainSample;
            double a = Curvature(p)[f];
            var v = new Complex[M];
            for (int m = 0; m < M; m++) v[m] = p.Data[m * nf + f] * Complex.FromPolarCoordinates(1, -a * B.Radius[m] * B.Radius[m]);
            var g = (Complex[])v.Clone();
            B.ToK(g, 1);
            double sk = 0, sk0 = 0;
            bool radial = B is RadialBasis;
            for (int m = 0; m < M; m++) { double q = g[m].Real * g[m].Real + g[m].Imaginary * g[m].Imaginary; sk0 += q; sk += q * (radial ? B.K2[m] : B.Kx[m] * B.Kx[m]); }
            if (sk0 <= 0) return double.NaN;
            double k2 = sk / sk0;
            double s0 = 0, x2 = 0, xk = 0;
            if (radial)
            {
                var order = Enumerable.Range(0, M).OrderBy(m => B.Radius[m]).ToArray();
                for (int j = 0; j < M; j++)
                {
                    int m = order[j];
                    int ia = order[Math.Max(0, j - 1)], ib = order[Math.Min(M - 1, j + 1)];
                    double dr = B.Radius[ib] - B.Radius[ia];
                    Complex d = j == 0 || dr <= 0 ? Complex.Zero : (v[ib] - v[ia]) / dr;
                    double i2 = v[m].Real * v[m].Real + v[m].Imaginary * v[m].Imaginary, w = B.Weights[m], r = B.Radius[m];
                    s0 += w * i2; x2 += w * r * r * i2; xk += w * r * (Complex.Conjugate(v[m]) * d).Imaginary;
                }
                x2 /= s0; xk /= s0;
                return Math.Sqrt(Math.Max(0, x2 * k2 - xk * xk));
            }
            var cb = (CartesianBasis)B;
            for (int ix = 0; ix < cb.Nx; ix++)
                for (int iy = 0; iy < cb.Ny; iy++)
                {
                    int m = ix * cb.Ny + iy;
                    int ia = Math.Max(0, ix - 1), ib = Math.Min(cb.Nx - 1, ix + 1);
                    var d = (v[ib * cb.Ny + iy] - v[ia * cb.Ny + iy]) / (cb.Dx * (ib - ia));
                    double i2 = v[m].Real * v[m].Real + v[m].Imaginary * v[m].Imaginary, x = cb.X[m];
                    s0 += i2; x2 += x * x * i2; xk += x * (Complex.Conjugate(v[m]) * d).Imaginary;
                }
            x2 /= s0; xk /= s0;
            return 2 * Math.Sqrt(Math.Max(0, x2 * k2 - xk * xk));
        }

        /// <summary>Fluence (J/m^2) at each spatial point of a full spectral field (layout [m * n + i]).</summary>
        public static double[] Fluence(Complex[] e, int n, FrequencyGrid g, ITransverseBasis basis)
        {
            int M = basis.M; var f = new double[M];
            for (int m = 0; m < M; m++)
            {
                double s = 0; int o = m * n;
                for (int i = 0; i < n; i++) s += e[o + i].Real * e[o + i].Real + e[o + i].Imaginary * e[o + i].Imaginary;
                f[m] = s / n * g.DTime;
            }
            return f;
        }
    }
}
