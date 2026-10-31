using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Transverse
{
    /// <summary>Bessel functions of the first kind (orders 0 and 1) and zeros of J0.</summary>
    public static class Bessel
    {
        // Rational/asymptotic approximations (Numerical Recipes, 3rd ed., sect. 6.5), |error| ~ 1e-8 absolute.
        // The Hankel matrix built from them is re-orthogonalised afterwards, so energy is conserved exactly.
        public static double J0(double x)
        {
            double ax = Math.Abs(x);
            if (ax < 8.0)
            {
                double y = x * x;
                double a1 = 57568490574.0 + y * (-13362590354.0 + y * (651619640.7 + y * (-11214424.18 + y * (77392.33017 + y * (-184.9052456)))));
                double a2 = 57568490411.0 + y * (1029532985.0 + y * (9494680.718 + y * (59272.64853 + y * (267.8532712 + y * 1.0))));
                return a1 / a2;
            }
            else
            {
                double z = 8.0 / ax, y = z * z, xx = ax - 0.785398164;
                double a1 = 1.0 + y * (-0.1098628627e-2 + y * (0.2734510407e-4 + y * (-0.2073370639e-5 + y * 0.2093887211e-6)));
                double a2 = -0.1562499995e-1 + y * (0.1430488765e-3 + y * (-0.6911147651e-5 + y * (0.7621095161e-6 - y * 0.934935152e-7)));
                return Math.Sqrt(0.636619772 / ax) * (Math.Cos(xx) * a1 - z * Math.Sin(xx) * a2);
            }
        }

        public static double J1(double x)
        {
            double ax = Math.Abs(x);
            if (ax < 8.0)
            {
                double y = x * x;
                double a1 = x * (72362614232.0 + y * (-7895059235.0 + y * (242396853.1 + y * (-2972611.439 + y * (15704.48260 + y * (-30.16036606))))));
                double a2 = 144725228442.0 + y * (2300535178.0 + y * (18583304.74 + y * (99447.43394 + y * (376.9991397 + y * 1.0))));
                return a1 / a2;
            }
            else
            {
                double z = 8.0 / ax, y = z * z, xx = ax - 2.356194491;
                double a1 = 1.0 + y * (0.183105e-2 + y * (-0.3516396496e-4 + y * (0.2457520174e-5 + y * (-0.240337019e-6))));
                double a2 = 0.04687499995 + y * (-0.2002690873e-3 + y * (0.8449199096e-5 + y * (-0.88228987e-6 + y * 0.105787412e-6)));
                double ans = Math.Sqrt(0.636619772 / ax) * (Math.Cos(xx) * a1 - z * Math.Sin(xx) * a2);
                return x < 0 ? -ans : ans;
            }
        }

        /// <summary>First <paramref name="count"/> positive zeros of J0 (McMahon's expansion refined by Newton).</summary>
        public static double[] ZerosJ0(int count)
        {
            var z = new double[count];
            for (int k = 1; k <= count; k++)
            {
                double b = (k - 0.25) * Math.PI;
                double x = b + 1.0 / (8 * b) - 124.0 / (3 * Math.Pow(8 * b, 3)) + 120928.0 / (15 * Math.Pow(8 * b, 5));
                for (int it = 0; it < 30; it++)
                {
                    double f = J0(x), d = -J1(x);
                    double dx = f / d;
                    x -= dx;
                    if (Math.Abs(dx) < 1e-14 * x) break;
                }
                z[k - 1] = x;
            }
            return z;
        }
    }

    /// <summary>
    /// A transverse sampling of the beam. Data for M spatial points and n frequency (or time) samples is stored as
    /// one array, index <c>m * n + i</c> (spatial point m, sample i). The transform to transverse-wavevector space
    /// is linear and invertible; <see cref="K2"/> holds k_perp^2 of each wavevector mode, so diffraction is a diagonal
    /// operator there. <see cref="Weights"/> are area elements: power = sum_m Weights[m] |E_m|^2 with E in sqrt(W/m^2).
    /// </summary>
    public interface ITransverseBasis
    {
        string Kind { get; }
        int M { get; }
        /// <summary>Area element of each spatial point (m^2).</summary>
        double[] Weights { get; }
        /// <summary>k_perp^2 (1/m^2) of each wavevector mode.</summary>
        double[] K2 { get; }
        /// <summary>Distance from the axis of each spatial point (m).</summary>
        double[] Radius { get; }
        /// <summary>x / y of each spatial point (m); for radial bases X = r, Y = 0.</summary>
        double[] X { get; }
        double[] Y { get; }
        /// <summary>x-component of the wavevector of each mode (cartesian; zero for radial).</summary>
        double[] Kx { get; }
        double Extent { get; }
        /// <summary>In-place transform space -> wavevector for each of the n samples.</summary>
        void ToK(Complex[] data, int n);
        /// <summary>In-place transform wavevector -> space.</summary>
        void ToSpace(Complex[] data, int n);
        string Describe();
    }

    /// <summary>
    /// Axisymmetric beams: quasi-discrete Hankel transform of order 0 (Guizar-Sicairos &amp; Gutierrez-Vega,
    /// J. Opt. Soc. Am. A 21, 53 (2004)). r_m = j_m R / S, k_m = j_m / R (j_m zeros of J0, S = j_{M+1}).
    /// The transform matrix is symmetric; it is made exactly orthogonal (Newton-Schulz), so linear propagation
    /// conserves energy to round-off.
    /// </summary>
    public sealed class RadialBasis : ITransverseBasis
    {
        public string Kind => "radial";
        public int M { get; }
        public double Extent { get; }
        public double[] Weights { get; }
        public double[] K2 { get; }
        public double[] Radius { get; }
        public double[] X => Radius;
        public double[] Y { get; }
        public double[] Kx { get; }
        private readonly double[] _t;      // M x M orthogonal matrix, row-major
        private readonly double[] _scale;  // R / |J1(j_m)|

        /// <summary>The M x M orthogonal transform matrix (row-major): ToK = T (scale x data), ToSpace = (T data) / scale.
        /// Exposed for device back-ends.</summary>
        public double[] Matrix => _t;
        /// <summary>Per-point scale R / |J1(j_m)| used by ToK / ToSpace.</summary>
        public double[] Scale => _scale;

        public RadialBasis(int points, double radiusM)
        {
            if (points < 4) throw new ArgumentException("radial basis needs at least 4 points");
            if (!(radiusM > 0)) throw new ArgumentException("radial basis needs a positive radius");
            M = points; Extent = radiusM;
            var j = Bessel.ZerosJ0(points + 1);
            double S = j[points];
            Radius = new double[M]; K2 = new double[M]; Weights = new double[M]; _scale = new double[M]; Y = new double[M]; Kx = new double[M];
            var j1 = new double[M];
            for (int m = 0; m < M; m++)
            {
                j1[m] = Math.Abs(Bessel.J1(j[m]));
                Radius[m] = j[m] * radiusM / S;
                double k = j[m] / radiusM;
                K2[m] = k * k;
                Weights[m] = 4 * Math.PI * radiusM * radiusM / (S * S * j1[m] * j1[m]);
                _scale[m] = radiusM / j1[m];
            }
            _t = Matrices.GetOrAdd(M, _ =>
            {
                var t = new double[M * M];
                Parallel.For(0, M, a =>
                {
                    for (int b = 0; b < M; b++)
                        t[a * M + b] = 2.0 * Bessel.J0(j[a] * j[b] / S) / (j1[a] * j1[b] * S);
                });
                return Orthogonalize(t, M);
            });
        }

        // The orthogonal matrix depends only on the number of points (r_m and k_m scale with R, the matrix does not):
        // built once per size and shared (2048 points: ~2 s, once per process).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, double[]> Matrices = new System.Collections.Concurrent.ConcurrentDictionary<int, double[]>();

        /// <summary>Newton-Schulz iteration X &lt;- X (3 I - X X) / 2 for the symmetric, near-orthogonal X (X stays
        /// symmetric, so X^T X = X X). Row-major products with contiguous inner loops.</summary>
        private static double[] Orthogonalize(double[] x, int n)
        {
            var xx = new double[n * n]; var y = new double[n * n];
            for (int it = 0; it < 8; it++)
            {
                Multiply(x, x, xx, n);
                double err = 0;
                for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) err = Math.Max(err, Math.Abs(xx[a * n + b] - (a == b ? 1 : 0)));
                if (err < 1e-14) break;
                Multiply(x, xx, y, n);                                            // y = X (X X)
                for (int k = 0; k < y.Length; k++) y[k] = 1.5 * x[k] - 0.5 * y[k]; // X (3 I - X X) / 2
                Array.Copy(y, x, x.Length);
            }
            return x;
        }

        /// <summary>c = a b for n x n row-major matrices whose product is symmetric (powers of one symmetric matrix): only the upper
        /// triangle is computed and then mirrored (rows in parallel, i-k-j order, four rows of a per pass over b).</summary>
        private static void Multiply(double[] a, double[] b, double[] c, int n)
        {
            int blocks = (n + 3) / 4;
            Parallel.For(0, blocks, blk =>
            {
                int r0 = blk * 4, rows = Math.Min(4, n - r0);
                Array.Clear(c, r0 * n, rows * n);
                for (int k = 0; k < n; k++)
                {
                    int ob = k * n;
                    for (int r = 0; r < rows; r++)
                    {
                        double s = a[(r0 + r) * n + k];
                        if (s == 0) continue;
                        int row = r0 + r, oc = row * n;
                        for (int j = row; j < n; j++) c[oc + j] += s * b[ob + j];
                    }
                }
            });
            Parallel.For(0, n, r => { for (int j = 0; j < r; j++) c[r * n + j] = c[j * n + r]; });
        }

        private void Apply(Complex[] data, int n, bool toK)
        {
            int M_ = M;
            var src = new Complex[data.Length];
            // scale (space side) then multiply by T
            if (toK) { for (int m = 0; m < M_; m++) { double s = _scale[m]; int o = m * n; for (int i = 0; i < n; i++) src[o + i] = data[o + i] * s; } }
            else Array.Copy(data, src, data.Length);
            Parallel.For(0, M_, a =>
            {
                int oa = a * n;
                var acc = new Complex[n];
                for (int b = 0; b < M_; b++)
                {
                    double tab = _t[a * M_ + b];
                    if (tab == 0) continue;
                    int ob = b * n;
                    for (int i = 0; i < n; i++) acc[i] += tab * src[ob + i];
                }
                if (!toK) { double s = 1.0 / _scale[a]; for (int i = 0; i < n; i++) acc[i] *= s; }
                Array.Copy(acc, 0, data, oa, n);
            });
        }

        public void ToK(Complex[] data, int n) => Apply(data, n, true);
        public void ToSpace(Complex[] data, int n) => Apply(data, n, false);
        public string Describe() => string.Format(System.Globalization.CultureInfo.InvariantCulture, "radial (Hankel), {0} points to r = {1:G4} mm", M, Extent * 1e3);
    }

    /// <summary>Cartesian beams on an Nx x Ny grid (2-D FFT). Point index m = ix * Ny + iy, x centred on the axis.</summary>
    public sealed class CartesianBasis : ITransverseBasis
    {
        public string Kind => "cartesian";
        public int M { get; }
        public int Nx { get; }
        public int Ny { get; }
        public double Dx { get; }
        public double Dy { get; }
        public double Extent { get; }
        public double[] Weights { get; }
        public double[] K2 { get; }
        public double[] Radius { get; }
        public double[] X { get; }
        public double[] Y { get; }
        public double[] Kx { get; }
        public double[] Ky { get; }

        public CartesianBasis(int nx, int ny, double halfWidthXM, double halfWidthYM)
        {
            if (nx % 2 != 0 || (ny > 1 && ny % 2 != 0) || nx < 4 || ny < 1) throw new ArgumentException("cartesian basis needs even point counts (>= 4; any even size works -- mixed-radix FFT -- powers of two are fastest)");
            Nx = nx; Ny = ny; M = nx * ny;
            Dx = 2 * halfWidthXM / nx; Dy = ny > 1 ? 2 * halfWidthYM / ny : 1.0;
            Extent = Math.Max(halfWidthXM, halfWidthYM);
            Weights = Enumerable.Repeat(Dx * Dy, M).ToArray();
            X = new double[M]; Y = new double[M]; Radius = new double[M]; K2 = new double[M]; Kx = new double[M]; Ky = new double[M];
            for (int ix = 0; ix < nx; ix++)
                for (int iy = 0; iy < ny; iy++)
                {
                    int m = ix * ny + iy;
                    X[m] = (ix - nx / 2) * Dx; Y[m] = ny > 1 ? (iy - ny / 2) * Dy : 0;
                    Radius[m] = Math.Sqrt(X[m] * X[m] + Y[m] * Y[m]);
                    // wavevector modes in standard FFT order (index 0 = DC), matching ToK's output
                    double kx = 2 * Math.PI * (ix < nx / 2 ? ix : ix - nx) / (nx * Dx);
                    double ky = ny > 1 ? 2 * Math.PI * (iy < ny / 2 ? iy : iy - ny) / (ny * Dy) : 0;
                    Kx[m] = kx; Ky[m] = ky;
                    K2[m] = kx * kx + ky * ky;
                }
        }

        /// <summary>2-D FFT of every sample i. Space data are centred (index N/2 = axis); k data are in standard FFT
        /// order. -1: space -> k (unnormalised); +1: k -> space (1/N). In place, buffers reused per thread.</summary>
        private void Fft2(Complex[] data, int n, int sign)
        {
            int nx = Nx, ny = Ny;
            System.Threading.Tasks.Parallel.For(0, n, () => (new Complex[nx], new Complex[Math.Max(1, ny)]), (i, st, buf) =>
            {
                var (lx, ly) = buf;
                if (ny > 1)
                    for (int ix = 0; ix < nx; ix++)
                    {
                        int b0 = ix * ny;
                        if (sign < 0) for (int iy = 0; iy < ny; iy++) ly[iy] = data[(b0 + ((iy + ny / 2) % ny)) * n + i];
                        else for (int iy = 0; iy < ny; iy++) ly[iy] = data[(b0 + iy) * n + i];
                        Fft.Provider.TransformInPlace(ly, sign < 0 ? -1 : +1, sign > 0);
                        if (sign < 0) for (int iy = 0; iy < ny; iy++) data[(b0 + iy) * n + i] = ly[iy];
                        else for (int iy = 0; iy < ny; iy++) data[(b0 + ((iy + ny / 2) % ny)) * n + i] = ly[iy];
                    }
                for (int iy = 0; iy < ny; iy++)
                {
                    if (sign < 0) for (int ix = 0; ix < nx; ix++) lx[ix] = data[(((ix + nx / 2) % nx) * ny + iy) * n + i];
                    else for (int ix = 0; ix < nx; ix++) lx[ix] = data[(ix * ny + iy) * n + i];
                    Fft.Provider.TransformInPlace(lx, sign < 0 ? -1 : +1, sign > 0);
                    if (sign < 0) for (int ix = 0; ix < nx; ix++) data[(ix * ny + iy) * n + i] = lx[ix];
                    else for (int ix = 0; ix < nx; ix++) data[(((ix + nx / 2) % nx) * ny + iy) * n + i] = lx[ix];
                }
                return buf;
            }, _ => { });
        }

        public void ToK(Complex[] data, int n) => Fft2(data, n, -1);
        public void ToSpace(Complex[] data, int n) => Fft2(data, n, +1);
        public string Describe() => string.Format(System.Globalization.CultureInfo.InvariantCulture, "cartesian {0} x {1}, {2:G4} x {3:G4} mm", Nx, Ny, Nx * Dx * 1e3, Ny * Dy * 1e3);
    }
}
