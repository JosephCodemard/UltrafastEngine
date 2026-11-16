namespace UltrafastEngine.Plugins.Gpu
{
    /// <summary>
    /// The device kernels: one source compiled at run time as OpenCL C (-D UF_OPENCL) or CUDA C++ (NVRTC, -D UF_CUDA).
    /// Every kernel loops over its items with a grid stride, so any launch size is valid; reductions write one partial
    /// result per work group (the host finishes them). Complex numbers are double pairs (re, im). All arrays are in
    /// natural FFT order (index 0 = zero frequency / the first time sample after the swap of halves): the host swaps
    /// halves when it uploads and downloads, so the engine's centred spectral transforms become plain FFTs here.
    /// </summary>
    public static class Kernels
    {
        /// <summary>The whole kernel source (header + body), compiled once per device context.</summary>
        public const string Source = Header + Body;

        /// <summary>Macros and complex helpers shared by every program (the built-in kernels and the per-problem kernels
        /// generated for user responses, <see cref="UserResponseKernel"/>).</summary>
        public const string Header = @"
#ifdef UF_OPENCL
#pragma OPENCL EXTENSION cl_khr_fp64 : enable
#define KERNEL __kernel
#define G __global
#define LOC __local
#define GID ((int)get_global_id(0))
#define GSZ ((int)get_global_size(0))
#define LID ((int)get_local_id(0))
#define LSZ ((int)get_local_size(0))
#define GRP ((int)get_group_id(0))
#define NGRP ((int)get_num_groups(0))
#define SYNC() barrier(CLK_LOCAL_MEM_FENCE)
#define DEVFN inline
typedef double2 cplx;
#define C2(a, b) ((double2)((a), (b)))
#else
#define KERNEL extern ""C"" __global__
#define G
#define LOC __shared__
#define GID ((int)(blockIdx.x * blockDim.x + threadIdx.x))
#define GSZ ((int)(gridDim.x * blockDim.x))
#define LID ((int)threadIdx.x)
#define LSZ ((int)blockDim.x)
#define GRP ((int)blockIdx.x)
#define NGRP ((int)gridDim.x)
#define SYNC() __syncthreads()
#define DEVFN __device__ inline
struct alignas(16) uf_cplx { double x, y; };
typedef uf_cplx cplx;
DEVFN cplx uf_c2(double a, double b) { cplx r; r.x = a; r.y = b; return r; }
#define C2(a, b) uf_c2((a), (b))
#endif

DEVFN cplx cmul(cplx a, cplx b) { return C2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x); }
DEVFN cplx cadd(cplx a, cplx b) { return C2(a.x + b.x, a.y + b.y); }
DEVFN cplx csub(cplx a, cplx b) { return C2(a.x - b.x, a.y - b.y); }
DEVFN cplx csc(cplx a, double s) { return C2(a.x * s, a.y * s); }
DEVFN cplx cconj(cplx a) { return C2(a.x, -a.y); }
DEVFN cplx cimul(cplx a, double s) { return C2(-a.y * s, a.x * s); }   /* i s a */
DEVFN double cabs2(cplx a) { return a.x * a.x + a.y * a.y; }
";

        /// <summary>The built-in kernels.</summary>
        public const string Body = @"

/* ---------------------------------------------------------------- element-wise */

KERNEL void uf_exp(G const cplx* L, double h, G cplx* dst, int len)
{
    for (int i = GID; i < len; i += GSZ) {
        double re = L[i].x * h, im = L[i].y * h;
        double m = re < -700.0 ? 0.0 : exp(re);
        dst[i] = C2(m * cos(im), m * sin(im));
    }
}

KERNEL void uf_mul(G const cplx* a, G const cplx* b, G cplx* dst, int len)
{
    for (int i = GID; i < len; i += GSZ) dst[i] = cmul(a[i], b[i]);
}

/* dst = y + s x */
KERNEL void uf_axpy(G const cplx* y, double s, G const cplx* x, G cplx* dst, int len)
{
    for (int i = GID; i < len; i += GSZ) dst[i] = C2(y[i].x + s * x[i].x, y[i].y + s * x[i].y);
}

/* dst = e (y + s x) */
KERNEL void uf_axpy_mul(G const cplx* e, G const cplx* y, double s, G const cplx* x, G cplx* dst, int len)
{
    for (int i = GID; i < len; i += GSZ) dst[i] = cmul(e[i], C2(y[i].x + s * x[i].x, y[i].y + s * x[i].y));
}

KERNEL void uf_scale(G cplx* a, double s, int len)
{
    for (int i = GID; i < len; i += GSZ) a[i] = csc(a[i], s);
}

/* a[i] *= f[i mod n] (real factor per frequency, shared by all components) */
KERNEL void uf_scale_real(G cplx* a, G const double* f, int n, int len)
{
    for (int i = GID; i < len; i += GSZ) a[i] = csc(a[i], f[i % n]);
}

/* beta = half (aI + h/6 (k1 + 2 k2 + 2 k3)) */
KERNEL void uf_rk_beta(G const cplx* hf, G const cplx* aI, G const cplx* k1, G const cplx* k2, G const cplx* k3, double h, G cplx* beta, int len)
{
    double c = h / 6.0;
    for (int i = GID; i < len; i += GSZ) {
        cplx s = C2(aI[i].x + c * (k1[i].x + 2.0 * k2[i].x + 2.0 * k3[i].x), aI[i].y + c * (k1[i].y + 2.0 * k2[i].y + 2.0 * k3[i].y));
        beta[i] = cmul(hf[i], s);
    }
}

/* the embedded error of ERK4(3)-IP: per group sum |a4 - a3|^2 -> part[o + g], |a4|^2 -> part[o + NGRP + g],
   with a3 = beta + h/30 (2 k4 + 3 k5) */
KERNEL void uf_rk_err(G const cplx* beta, G const cplx* a4, G const cplx* k4, G const cplx* k5, double h, int len, G double* part, int o)
{
    LOC double sa[256];
    LOC double sb[256];
    double na = 0.0, nb = 0.0, c = h / 30.0;
    for (int i = GID; i < len; i += GSZ) {
        double dx = a4[i].x - (beta[i].x + c * (2.0 * k4[i].x + 3.0 * k5[i].x));
        double dy = a4[i].y - (beta[i].y + c * (2.0 * k4[i].y + 3.0 * k5[i].y));
        na += dx * dx + dy * dy;
        nb += cabs2(a4[i]);
    }
    sa[LID] = na; sb[LID] = nb;
    SYNC();
    for (int s = LSZ / 2; s > 0; s >>= 1) { if (LID < s) { sa[LID] += sa[LID + s]; sb[LID] += sb[LID + s]; } SYNC(); }
    if (LID == 0) { part[o + GRP] = sa[0]; part[o + NGRP + GRP] = sb[0]; }
}

/* RK4IP final combination: a = half (aI + h/6 (k1 + 2 k2 + 2 k3)) + h/6 k4 */
KERNEL void uf_rk_final(G const cplx* beta, G const cplx* k4, double h, G cplx* a, int len)
{
    double c = h / 6.0;
    for (int i = GID; i < len; i += GSZ) a[i] = C2(beta[i].x + c * k4[i].x, beta[i].y + c * k4[i].y);
}

/* ---------------------------------------------------------------- FFT (radix-4 Stockham, as StockhamFft.cs) */

/* Element q of transform b sits at (b / inner) * os + (b % inner) + q * st: contiguous rows (inner 1, os n, st 1)
   or a strided axis of a multi-dimensional array (inner > 1). bfast: neighbouring work items take neighbouring
   transforms (coalesced for strided axes) instead of neighbouring elements. */
DEVFN int faddr(int b, int q, int inner, int os, int st) { return (b / inner) * os + (b % inner) + q * st; }

/* one radix-4 pass over batch transforms of length n: sub-transforms of length len, stride s; twiddle W^k = tw[k]
   (k < n); sg = +1 for the exp(-i ...) transform, -1 for exp(+i ...); scale multiplies the output (1/N on the last
   pass) */
KERNEL void uf_fft4(G const cplx* x, G cplx* y, G const cplx* tw, int n, int len, int s, int step, double sg, int batch, double scale,
                    int inner, int os, int st, int bfast)
{
    int quarter = n >> 2, m = len >> 2, total = quarter * batch;
    for (int g = GID; g < total; g += GSZ) {
        int b, j;
        if (bfast) { b = g % batch; j = g / batch; } else { b = g / quarter; j = g - b * quarter; }
        int p = j / s, q = j - p * s;
        int k1 = p * step;
        cplx w1 = tw[k1], w2 = tw[2 * k1], w3 = tw[3 * k1];
        w1.y *= sg; w2.y *= sg; w3.y *= sg;
        cplx a = x[faddr(b, q + s * p, inner, os, st)], bb = x[faddr(b, q + s * (p + m), inner, os, st)];
        cplx c = x[faddr(b, q + s * (p + 2 * m), inner, os, st)], d = x[faddr(b, q + s * (p + 3 * m), inner, os, st)];
        cplx apc = cadd(a, c), amc = csub(a, c), bpd = cadd(bb, d), bmd = csub(bb, d);
        cplx jb = C2(sg * bmd.y, -sg * bmd.x);
        int o0 = q + s * 4 * p;
        y[faddr(b, o0, inner, os, st)] = csc(cadd(apc, bpd), scale);
        y[faddr(b, o0 + s, inner, os, st)] = csc(cmul(w1, cadd(amc, jb)), scale);
        y[faddr(b, o0 + 2 * s, inner, os, st)] = csc(cmul(w2, csub(apc, bpd)), scale);
        y[faddr(b, o0 + 3 * s, inner, os, st)] = csc(cmul(w3, csub(amc, jb)), scale);
    }
}

/* final radix-2 pass (log2 n odd): s = n/2 */
KERNEL void uf_fft2(G const cplx* x, G cplx* y, int n, int batch, double scale, int inner, int os, int st, int bfast)
{
    int hn = n >> 1, total = hn * batch;
    for (int g = GID; g < total; g += GSZ) {
        int b, q;
        if (bfast) { b = g % batch; q = g / batch; } else { b = g / hn; q = g - b * hn; }
        int i0 = faddr(b, q, inner, os, st), i1 = faddr(b, q + hn, inner, os, st);
        cplx p0 = x[i0], p1 = x[i1];
        y[i0] = csc(cadd(p0, p1), scale);
        y[i1] = csc(csub(p0, p1), scale);
    }
}

/* copy the elements of a strided batch (for the odd-pass case of strided transforms) */
KERNEL void uf_fft_copy(G const cplx* x, G cplx* y, int n, int batch, int inner, int os, int st)
{
    int total = n * batch;
    for (int g = GID; g < total; g += GSZ) { int b = g % batch, q = g / batch; int i = faddr(b, q, inner, os, st); y[i] = x[i]; }
}

/* multiply element q of each transform by (-1)^q: the swap of halves of an even-length transform */
KERNEL void uf_alt_sign(G cplx* x, int n, int batch, int inner, int os, int st)
{
    int total = (n >> 1) * batch;
    for (int g = GID; g < total; g += GSZ) { int b = g % batch, q = 2 * (g / batch) + 1; int i = faddr(b, q, inner, os, st); x[i] = C2(-x[i].x, -x[i].y); }
}

/* ---------------------------------------------------------------- 1-D envelope nonlinearity (EnvelopeProblem) */

/* Raman inputs as complex signals (imaginary part 0), each n long: C0 = I, and for the vector anisotropic model
   C1 = I, C2 = Ix, C3 = Iy, C4 = Re(Ax Ay*) (all / Aeff). T holds Ax (and Ay at offset n) in time. */
KERNEL void uf_env_raman_in(G const cplx* T, int n, int scalar, int nsig, double invA, G cplx* C)
{
    for (int i = GID; i < n; i += GSZ) {
        cplx ax = T[i];
        cplx ay = scalar ? C2(0.0, 0.0) : T[n + i];
        double ix = cabs2(ax) * invA, iy = cabs2(ay) * invA;
        C[i] = C2(ix + iy, 0.0);
        if (nsig > 1) {
            C[n + i] = C2(ix + iy, 0.0);
            C[2 * n + i] = C2(ix, 0.0);
            C[3 * n + i] = C2(iy, 0.0);
            C[4 * n + i] = C2((ax.x * ay.x + ax.y * ay.y) * invA, 0.0);
        }
    }
}

/* multiply spectrum 0 by the Raman kernel K and spectra 1.. by the anisotropic kernel Kb */
KERNEL void uf_env_raman_mul(G cplx* C, G const cplx* K, G const cplx* Kb, int n, int nsig)
{
    int total = n * nsig;
    for (int g = GID; g < total; g += GSZ) {
        int s = g / n, i = g - s * n;
        C[g] = cmul(C[g], s == 0 ? K[i] : Kb[i]);
    }
}

/* Running fluence of the total intensity per row (the response variable F, ExpressionTimeResponse.Fluence):
   F(c) = sum_{k=1..c} (I(k) + I(k-1)) dt / 2 over the CENTRED time order; I = (|Ax|^2 + |Ay|^2) / A (1-D: A = Aeff;
   spatial: 1). Stored at the natural index of sample c. One work group per row: each item sums a contiguous chunk, item 0
   turns the chunk sums into offsets, then each item writes its chunk. */
KERNEL void uf_fluence(G const cplx* T, int n, int M, int block, int scalar, double A, double dt, G double* F)
{
    LOC double sm[256];
    int h = n >> 1;
    int chunk = (n + LSZ - 1) / LSZ;
    for (int r = GRP; r < M; r += NGRP) {
        int c0 = LID * chunk, c1 = c0 + chunk; if (c1 > n) c1 = n;
        double s = 0.0, prev = 0.0;
        for (int c = c0; c < c1; c++) {
            int d = c + h; if (d >= n) d -= n;
            double I = (cabs2(T[r * n + d]) + (scalar ? 0.0 : cabs2(T[block + r * n + d]))) / A;
            if (c > 0) {
                if (c == c0) { int e = c - 1 + h; if (e >= n) e -= n; prev = (cabs2(T[r * n + e]) + (scalar ? 0.0 : cabs2(T[block + r * n + e]))) / A; }
                s += 0.5 * (I + prev) * dt;
            }
            prev = I;
        }
        sm[LID] = s;
        SYNC();
        if (LID == 0) { double acc = 0.0; for (int k = 0; k < LSZ; k++) { double v = sm[k]; sm[k] = acc; acc += v; } }
        SYNC();
        double acc = sm[LID];
        for (int c = c0; c < c1; c++) {
            int d = c + h; if (d >= n) d -= n;
            double I = (cabs2(T[r * n + d]) + (scalar ? 0.0 : cabs2(T[block + r * n + d]))) / A;
            if (c > 0) {
                if (c == c0) { int e = c - 1 + h; if (e >= n) e -= n; prev = (cabs2(T[r * n + e]) + (scalar ? 0.0 : cabs2(T[block + r * n + e]))) / A; }
                acc += 0.5 * (I + prev) * dt;
            }
            prev = I;
            F[r * n + d] = acc;
        }
        SYNC();
    }
}

/* Orientational (gas rotational Raman) inputs, IAnisotropicResponse.Convolve: a zero-padded linear convolution in the
   engine's CENTRED time order, so centred sample c reads the natural sample (c + n/2) mod n. Rows r < M of n samples
   (row r of component y at block + r n; 1-D: M = 1, block = n). Padded signals of length m, signal s of row r at
   (s M + r) m: scalar Q0 = Ix; vector Q0 = 3/2 Ix - 1/2 I, Q1 = 3/2 Iy - 1/2 I, Q2 = 3/2 Re(Ax Ay*) (all x invA,
   1 / Aeff in 1-D); zero for c >= n. */
KERNEL void uf_rot_in(G const cplx* T, int n, int M, int block, int scalar, double invA, int m, G cplx* Q)
{
    int h = n >> 1, total = M * m;
    for (int g = GID; g < total; g += GSZ) {
        int r = g / m, c = g - r * m;
        if (c >= n) {
            Q[g] = C2(0.0, 0.0);
            if (!scalar) { Q[total + g] = C2(0.0, 0.0); Q[2 * total + g] = C2(0.0, 0.0); }
            continue;
        }
        int d = c + h; if (d >= n) d -= n;
        cplx ax = T[r * n + d];
        double ix = cabs2(ax) * invA;
        if (scalar) { Q[g] = C2(ix, 0.0); continue; }
        cplx ay = T[block + r * n + d];
        double iy = cabs2(ay) * invA, it = ix + iy;
        Q[g] = C2(1.5 * ix - 0.5 * it, 0.0);
        Q[total + g] = C2(1.5 * iy - 0.5 * it, 0.0);
        Q[2 * total + g] = C2(1.5 * (ax.x * ay.x + ax.y * ay.y) * invA, 0.0);
    }
}

/* every one of the nsig padded spectra (length m) times the kernel spectrum K */
KERNEL void uf_rot_mul(G cplx* Q, G const cplx* K, int m, int nsig)
{
    int total = m * nsig;
    for (int g = GID; g < total; g += GSZ) Q[g] = cmul(Q[g], K[g % m]);
}

/* Kerr + Raman rate in time (EnvelopeProblem.NonlinearFast): R = N(A) (x at 0, y at n); P = |Ax|^2 + |Ay|^2,
   D = |Im(Ax* Nx + Ay* Ny)| / (k0 P) for the B-integral; per-group max of P -> part[po + g].
   raman: 0 none, 1 scalar (C0), 2 vector (C0..C4); C already convolved (multiply by dt).
   rot: 0 none, 1 scalar, 2 vector -- the orientational index changes (EnvelopeProblem.Nonlinear), Q already convolved
   (centred order, padded length m): dnx += Q0, dny += Q1, coupling dnxy = Q2. */
KERNEL void uf_env_kerr(G const cplx* T, G const cplx* C, int n, int scalar, int raman, double invA, double k0, double n2,
                        double fR, double dt, double cc, G cplx* R, G double* P, G double* D, G double* part, int po,
                        G const cplx* Q, int rot, int m, G const cplx* U, int user)
{
    LOC double sm[256];
    double pm = 0.0;
    int h = n >> 1;
    for (int i = GID; i < n; i += GSZ) {
        cplx ax = T[i];
        cplx ay = scalar ? C2(0.0, 0.0) : T[n + i];
        double ix = cabs2(ax) * invA, iy = cabs2(ay) * invA;
        double rx = 0.0, ry = 0.0, rxy = 0.0;
        if (raman == 1) { rx = fR * C[i].x * dt; ry = rx; }
        else if (raman == 2) {
            double iso = (C[i].x - C[n + i].x) * dt;
            rx = fR * (iso + C[2 * n + i].x * dt);
            ry = fR * (iso + C[3 * n + i].x * dt);
            rxy = C[4 * n + i].x * dt;
        }
        double dnx = n2 * ((1.0 - fR) * (ix + 2.0 / 3.0 * iy) + rx);
        double dny = n2 * ((1.0 - fR) * (iy + 2.0 / 3.0 * ix) + ry);
        double qxy = 0.0;
        if (rot != 0) {
            int c = i + h; if (c >= n) c -= n;
            dnx += Q[c].x;
            if (rot == 2) { dny += Q[m + c].x; qxy = Q[2 * m + c].x; }
        }
        /* extra time-domain responses (user expressions, ...): complex dn = U, the same for x and y */
        double ui = 0.0;
        if (user) { cplx u = U[i]; dnx += u.x; dny += u.x; ui = u.y; }
        cplx nx = user ? cmul(C2(-k0 * ui, k0 * dnx), ax) : cimul(ax, k0 * dnx);
        cplx ny = C2(0.0, 0.0);
        if (!scalar) {
            ny = user ? cmul(C2(-k0 * ui, k0 * dny), ay) : cimul(ay, k0 * dny);
            if (cc != 0.0) {
                nx = cadd(nx, cimul(cmul(cmul(ay, ay), cconj(ax)), k0 * cc));
                ny = cadd(ny, cimul(cmul(cmul(ax, ax), cconj(ay)), k0 * cc));
            }
            if (rot == 2) {
                nx = cadd(nx, cimul(ay, k0 * qxy));
                ny = cadd(ny, cimul(ax, k0 * qxy));
            }
            if (raman == 2) {
                double d = n2 * fR * rxy;
                nx = cadd(nx, cimul(ay, k0 * d));
                ny = cadd(ny, cimul(ax, k0 * d));
            }
            R[n + i] = ny;
        }
        R[i] = nx;
        double p = cabs2(ax) + cabs2(ay);
        cplx s = cmul(cconj(ax), nx);
        if (!scalar) s = cadd(s, cmul(cconj(ay), ny));
        P[i] = p;
        D[i] = p > 0.0 ? fabs(s.y / (k0 * p)) : 0.0;
        pm = fmax(pm, p);
    }
    sm[LID] = pm;
    SYNC();
    for (int s2 = LSZ / 2; s2 > 0; s2 >>= 1) { if (LID < s2) sm[LID] = fmax(sm[LID], sm[LID + s2]); SYNC(); }
    if (LID == 0) part[po + GRP] = sm[0];
}

/* peak of D over samples with P >= 1e-6 max P (PeakIndexChange); pmax from part[po .. po + npg); result per group
   -> part[ko + g] */
KERNEL void uf_peak(G const double* P, G const double* D, int n, G double* part, int po, int npg, int ko)
{
    LOC double sm[256];
    double pmax = 0.0;
    for (int g = 0; g < npg; g++) pmax = fmax(pmax, part[po + g]);
    double thr = 1e-6 * pmax, best = 0.0;
    if (pmax > 0.0) for (int i = GID; i < n; i += GSZ) if (P[i] >= thr) best = fmax(best, D[i]);
    sm[LID] = best;
    SYNC();
    for (int s = LSZ / 2; s > 0; s >>= 1) { if (LID < s) sm[LID] = fmax(sm[LID], sm[LID + s]); SYNC(); }
    if (LID == 0) part[ko + GRP] = sm[0];
}

/* ---------------------------------------------------------------- spatial (SpatialEnvelopeProblem) */
/* layout: component c, transverse point m, sample i at c * block + m * n + i (block = M n) */

/* a[idx] *= scale[row mod M] (or / scale when inv), row = idx / n */
KERNEL void uf_rowscale(G cplx* a, G const double* scale, int n, int M, int len, int inv)
{
    for (int i = GID; i < len; i += GSZ) { double s = scale[(i / n) % M]; a[i] = csc(a[i], inv ? 1.0 / s : s); }
}

/* real row-major product C = A B, A: rows x K, B: K x ncols (doubles), offsets in doubles */
KERNEL void uf_matmul(G const double* A, G const double* B, G double* C, int rows, int K, int ncols, int bOff, int cOff)
{
    int total = rows * ncols;
    for (int g = GID; g < total; g += GSZ) {
        int a = g / ncols, j = g - a * ncols;
        double acc = 0.0;
        for (int b = 0; b < K; b++) acc += A[a * K + b] * B[bOff + b * ncols + j];
        C[cOff + g] = acc;
    }
}

/* a[i] *= f[i mod flen] */
KERNEL void uf_scale_real_mod(G cplx* a, G const double* f, int flen, int len)
{
    for (int i = GID; i < len; i += GSZ) a[i] = csc(a[i], f[i % flen]);
}

KERNEL void uf_sp_raman_in(G const cplx* T, int block, int vector, int nsig, G cplx* C)
{
    for (int i = GID; i < block; i += GSZ) {
        cplx ax = T[i];
        cplx ay = vector ? T[block + i] : C2(0.0, 0.0);
        double ix = cabs2(ax), iy = cabs2(ay);
        C[i] = C2(ix + iy, 0.0);
        if (nsig > 1) {
            C[block + i] = C2(ix + iy, 0.0);
            C[2 * block + i] = C2(ix, 0.0);
            C[3 * block + i] = C2(iy, 0.0);
            C[4 * block + i] = C2(ax.x * ay.x + ax.y * ay.y, 0.0);
        }
    }
}

KERNEL void uf_sp_raman_mul(G cplx* C, G const cplx* K, G const cplx* Kb, int n, int block, int nsig)
{
    int total = block * nsig;
    for (int g = GID; g < total; g += GSZ) { int s = g / block, i = g % n; C[g] = cmul(C[g], s == 0 ? K[i] : Kb[i]); }
}

/* Kerr + Raman rate per (point, time) as SpatialEnvelopeProblem.Nonlinear (no position-dependent properties):
   R = N(E); P = |Ex|^2 + |Ey|^2, D = |Im(Ex* Nx + Ey* Ny)| / (k0 P).
   rot: 0 none, 1 scalar, 2 vector orientational index changes from Q (uf_rot_in layout, already convolved; rows of n). */
KERNEL void uf_sp_kerr(G const cplx* T, G const cplx* C, int block, int vector, int raman, double k0, double n2, double fR, double dt,
                       double cc, G cplx* R, G double* P, G double* D, G const cplx* Q, int rot, int m, int n, int M,
                       G const cplx* U, int user)
{
    int h = n >> 1;
    for (int i = GID; i < block; i += GSZ) {
        cplx ax = T[i];
        cplx ay = vector ? T[block + i] : C2(0.0, 0.0);
        double ix = cabs2(ax), iy = cabs2(ay);
        double rx = 0.0, ry = 0.0, rxy = 0.0;
        if (raman == 1) { rx = fR * C[i].x * dt; ry = rx; }
        else if (raman == 2) {
            double iso = (C[i].x - C[block + i].x) * dt;
            rx = fR * (iso + C[2 * block + i].x * dt);
            ry = fR * (iso + C[3 * block + i].x * dt);
            rxy = C[4 * block + i].x * dt;
        }
        double dnx = n2 * ((1.0 - fR) * (ix + (vector ? 2.0 / 3.0 * iy : 0.0)) + rx);
        double dny = vector ? n2 * ((1.0 - fR) * (iy + 2.0 / 3.0 * ix) + ry) : 0.0;
        double qxy = 0.0;
        if (rot != 0) {
            int r = i / n, t = i - r * n, c = t + h; if (c >= n) c -= n;
            int q = r * m + c, total = M * m;
            dnx += Q[q].x;
            if (rot == 2) { dny += Q[total + q].x; qxy = Q[2 * total + q].x; }
        }
        double ui = 0.0;
        if (user) { cplx u = U[i]; dnx += u.x; dny += u.x; ui = u.y; }
        cplx nx = user ? cmul(C2(-k0 * ui, k0 * dnx), ax) : cimul(ax, k0 * dnx);
        cplx ny = C2(0.0, 0.0);
        if (vector) {
            ny = user ? cmul(C2(-k0 * ui, k0 * dny), ay) : cimul(ay, k0 * dny);
            if (cc != 0.0) {
                nx = cadd(nx, cimul(cmul(cmul(ay, ay), cconj(ax)), k0 * cc));
                ny = cadd(ny, cimul(cmul(cmul(ax, ax), cconj(ay)), k0 * cc));
            }
            if (rot == 2) {
                nx = cadd(nx, cimul(ay, k0 * qxy));
                ny = cadd(ny, cimul(ax, k0 * qxy));
            }
            if (raman == 2) {
                double d = n2 * fR * rxy;
                nx = cadd(nx, cimul(ay, k0 * d));
                ny = cadd(ny, cimul(ax, k0 * d));
            }
            R[block + i] = ny;
        }
        R[i] = nx;
        double p = ix + iy;
        cplx sx = cmul(cconj(ax), nx);
        if (vector) sx = cadd(sx, cmul(cconj(ay), ny));
        P[i] = p;
        D[i] = p > 0.0 ? fabs(sx.y / (k0 * p)) : 0.0;
    }
}

/* per transverse point m: max of D over samples with P >= 1e-6 x (max P of that point) -> out[m] */
KERNEL void uf_rowpeak(G const double* P, G const double* D, int n, int M, G double* out)
{
    LOC double sm[256];
    for (int m = GRP; m < M; m += NGRP) {
        double pm = 0.0;
        for (int i = LID; i < n; i += LSZ) pm = fmax(pm, P[m * n + i]);
        sm[LID] = pm;
        SYNC();
        for (int s = LSZ / 2; s > 0; s >>= 1) { if (LID < s) sm[LID] = fmax(sm[LID], sm[LID + s]); SYNC(); }
        double pmax = sm[0];
        SYNC();
        double best = 0.0, thr = 1e-6 * pmax;
        if (pmax > 0.0) for (int i = LID; i < n; i += LSZ) if (P[m * n + i] >= thr) best = fmax(best, D[m * n + i]);
        sm[LID] = best;
        SYNC();
        for (int s = LSZ / 2; s > 0; s >>= 1) { if (LID < s) sm[LID] = fmax(sm[LID], sm[LID + s]); SYNC(); }
        if (LID == 0) out[m] = sm[0];
        SYNC();
    }
}
";
    }
}
