using System;
using System.Collections.Generic;
using System.Numerics;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Plugins.Gpu
{
    /// <summary>An error reported by a GPU runtime (CUDA, NVRTC, cuFFT, OpenCL), with the call that failed.</summary>
    public sealed class GpuException : Exception
    {
        public int Code { get; }
        public GpuException(string message, int code = 0) : base(message) { Code = code; }
    }

    /// <summary>Device memory. Sizes in bytes; complex numbers are two doubles (System.Numerics.Complex layout).</summary>
    public abstract class GpuBuffer : IDisposable
    {
        public long Bytes { get; protected set; }
        public abstract void Dispose();
    }

    /// <summary>A kernel argument: a buffer, an int or a double (no boxing).</summary>
    public readonly struct KArg
    {
        public readonly byte Kind;     // 0 buffer, 1 int, 2 double
        public readonly GpuBuffer Buffer;
        public readonly int I;
        public readonly double D;
        private KArg(byte k, GpuBuffer b, int i, double d) { Kind = k; Buffer = b; I = i; D = d; }
        public static implicit operator KArg(GpuBuffer b) => new KArg(0, b, 0, 0);
        public static implicit operator KArg(int i) => new KArg(1, null, i, 0);
        public static implicit operator KArg(double d) => new KArg(2, null, 0, d);
    }

    /// <summary>
    /// One device with its compiled kernels (thread-safe). Work is issued through <see cref="GpuStream"/>s: one per
    /// run, so concurrent runs (studies, fits) overlap on the device.
    /// </summary>
    public abstract class GpuContext : IDisposable
    {
        public ComputeDeviceInfo Info { get; protected set; }
        /// <summary>Work-group (CUDA block) size of every launch: a power of two, at most 256.</summary>
        public int GroupSize { get; protected set; } = 256;
        /// <summary>Upper limit on groups per launch (kernels loop over the remaining items).</summary>
        public int MaxGroups { get; protected set; } = 1024;
        /// <summary>Name of the vendor FFT in use (cuFFT), or null for the built-in Stockham kernels.</summary>
        public string VendorFftName { get; protected set; }
        /// <summary>Name of the vendor BLAS in use (cuBLAS), or null for the built-in matrix kernel.</summary>
        public string VendorBlasName { get; protected set; }
        public abstract GpuBuffer Alloc(long bytes);
        public abstract GpuStream CreateStream();
        public abstract void Dispose();

        /// <summary>Groups for a grid-stride launch over <paramref name="items"/> items.</summary>
        public int Groups(long items) => (int)Math.Max(1, Math.Min(MaxGroups, (items + GroupSize - 1) / GroupSize));

        private long _allocated;
        internal void Track(long delta) => System.Threading.Interlocked.Add(ref _allocated, delta);
        /// <summary>Device memory held by this context's live buffers (bytes).</summary>
        public long AllocatedBytes => System.Threading.Interlocked.Read(ref _allocated);
        /// <summary>Free device memory now (bytes), -1 when unknown. CUDA asks the driver (cuMemGetInfo, which also counts other processes);
        /// the default (OpenCL has no such query) is the total minus this context's live buffers.</summary>
        public virtual long FreeMemoryBytes() => Info.MemoryBytes > 0 ? Math.Max(0, Info.MemoryBytes - AllocatedBytes) : -1;

        /// <summary>Most extra programs (user-response kernels) kept compiled per context; the least recently used is released beyond it.</summary>
        public const int MaxPrograms = 32;
        private readonly List<string> _programs = new List<string>();   // least recently used first
        /// <summary>Extra programs currently compiled on this context.</summary>
        public int ProgramCount { get { lock (_programs) return _programs.Count; } }

        /// <summary>Compile an extra program (<see cref="Kernels.Header"/> + <paramref name="body"/>) that defines the kernel
        /// <paramref name="kernelName"/>, once per context; afterwards <see cref="GpuStream.Launch"/> finds it by name.</summary>
        public void EnsureProgram(string kernelName, string body)
        {
            lock (_programs)
            {
                int at = _programs.IndexOf(kernelName);
                if (at >= 0) { _programs.RemoveAt(at); _programs.Add(kernelName); return; }
                BuildProgram(kernelName, Kernels.Header + body);
                _programs.Add(kernelName);
                while (_programs.Count > MaxPrograms) { string old = _programs[0]; _programs.RemoveAt(0); DropProgram(old); }
            }
        }

        /// <summary>Release the compiled program of <paramref name="kernelName"/> (the next <see cref="EnsureProgram"/> compiles it again).</summary>
        protected abstract void DropProgram(string kernelName);

        /// <summary>Compile <paramref name="source"/> and register <paramref name="kernelName"/> (throws <see cref="GpuException"/> with the log).</summary>
        protected abstract void BuildProgram(string kernelName, string source);
    }

    /// <summary>An in-order queue of device work (an OpenCL command queue or a CUDA stream). Not thread-safe: one per
    /// thread / run.</summary>
    public abstract class GpuStream : IDisposable
    {
        public GpuContext Context { get; protected set; }
        /// <summary>Launch a kernel of the shared source with <paramref name="groups"/> x GroupSize work items.</summary>
        public abstract void Launch(string kernel, int groups, params KArg[] args);
        public abstract unsafe void Write(GpuBuffer dst, long dstOffsetBytes, void* src, long bytes);
        /// <summary>Blocking read (waits for all queued work).</summary>
        public abstract unsafe void Read(GpuBuffer src, long srcOffsetBytes, void* dst, long bytes);
        public abstract void Copy(GpuBuffer src, long srcOffsetBytes, GpuBuffer dst, long dstOffsetBytes, long bytes);
        public abstract void Finish();
        public abstract void Dispose();
        /// <summary>A vendor FFT plan for <paramref name="batch"/> contiguous transforms of length n, or null.</summary>
        public virtual IVendorFft VendorFft(int n, int batch) => null;
        /// <summary>Vendor real matrix product C = A B (row-major, A m x k, B k x n) when available.</summary>
        public virtual bool VendorDgemmRowMajor(GpuBuffer a, GpuBuffer b, GpuBuffer c, int m, int n, int k, long bOffsetDoubles, long cOffsetDoubles) => false;

        public unsafe void Write(GpuBuffer dst, Complex[] src, long dstOffsetElements = 0, int count = -1)
        {
            if (count < 0) count = src.Length;
            fixed (Complex* p = src) Write(dst, dstOffsetElements * 16, p, (long)count * 16);
        }
        public unsafe void Write(GpuBuffer dst, double[] src, long dstOffsetElements = 0, int count = -1)
        {
            if (count < 0) count = src.Length;
            fixed (double* p = src) Write(dst, dstOffsetElements * 8, p, (long)count * 8);
        }
        public unsafe void Read(GpuBuffer src, Complex[] dst, long srcOffsetElements = 0, int count = -1)
        {
            if (count < 0) count = dst.Length;
            fixed (Complex* p = dst) Read(src, srcOffsetElements * 16, p, (long)count * 16);
        }
        public unsafe void Read(GpuBuffer src, double[] dst, long srcOffsetElements = 0, int count = -1)
        {
            if (count < 0) count = dst.Length;
            fixed (double* p = dst) Read(src, srcOffsetElements * 8, p, (long)count * 8);
        }
    }

    /// <summary>A vendor FFT plan (cuFFT): in place, unnormalised, exponent sign -1 or +1.</summary>
    public interface IVendorFft : IDisposable
    {
        void Execute(GpuBuffer data, int exponentSign);
    }

    /// <summary>
    /// Batched complex FFT on a stream, in place, the engine's sign convention (exponent -1 or +1, optional 1/N).
    /// Layout: element q of transform b at (b / inner) * os + (b % inner) + q * stride (complex elements) --
    /// contiguous rows by default, or one axis of a multi-dimensional array. Contiguous rows use the vendor FFT when
    /// the context has one (cuFFT); otherwise the built-in radix-4 Stockham kernels run (power-of-two lengths; the
    /// same algorithm and twiddle table as the CPU <see cref="StockhamFft"/>).
    /// </summary>
    public sealed class GpuFft : IDisposable
    {
        private readonly GpuStream _s;
        private readonly IVendorFft _vendor;
        private readonly GpuBuffer _tw, _scratch;
        private readonly int _inner, _os, _st, _bfast;
        public int N { get; }
        public int Batch { get; }
        public bool Vendor => _vendor != null;

        public GpuFft(GpuStream s, int n, int batch, bool allowVendor = true) : this(s, n, batch, 1, n, 1, allowVendor, (long)n * batch) { }

        /// <param name="span">Complex elements the layout covers (scratch size).</param>
        public GpuFft(GpuStream s, int n, int batch, int inner, int os, int stride, bool allowVendor, long span)
        {
            _s = s; N = n; Batch = batch; _inner = inner; _os = os; _st = stride; _bfast = inner > 1 ? 1 : 0;
            bool contiguous = inner == 1 && os == n && stride == 1;
            if (allowVendor && contiguous) _vendor = s.VendorFft(n, batch);
            if (_vendor != null) return;
            if (!BuiltInSupports(n)) throw new GpuException("the built-in GPU FFT needs a power-of-two length (got " + n + ")");
            var tw = new double[2 * n];
            for (int k = 0; k < n; k++) { double a = -2.0 * Math.PI * k / n; tw[2 * k] = Math.Cos(a); tw[2 * k + 1] = Math.Sin(a); }
            _tw = s.Context.Alloc(16L * n);
            s.Write(_tw, tw);
            _scratch = s.Context.Alloc(16L * span);
        }

        public static bool BuiltInSupports(int n) => n >= 2 && (n & (n - 1)) == 0;

        public void Transform(GpuBuffer data, int exponentSign, bool normalize)
        {
            var ctx = _s.Context;
            long total = (long)N * Batch;
            if (_vendor != null)
            {
                _vendor.Execute(data, exponentSign);
                if (normalize) _s.Launch("uf_scale", ctx.Groups(total), data, 1.0 / N, (int)total);
                return;
            }
            double sg = exponentSign > 0 ? -1.0 : 1.0;
            GpuBuffer src = data, dst = _scratch;
            int len = N, st = 1;
            int passes = 0; for (int l = N; l >= 4; l >>= 2) passes++;
            bool radix2 = (N >> (2 * passes)) == 2;
            int totalPasses = passes + (radix2 ? 1 : 0), done = 0;
            while (len >= 4)
            {
                done++;
                double scale = normalize && done == totalPasses ? 1.0 / N : 1.0;
                _s.Launch("uf_fft4", ctx.Groups(total / 4), src, dst, _tw, N, len, st, N / len, sg, Batch, scale, _inner, _os, _st, _bfast);
                var t = src; src = dst; dst = t;
                len >>= 2; st <<= 2;
            }
            if (len == 2)
            {
                _s.Launch("uf_fft2", ctx.Groups(total / 2), src, dst, N, Batch, normalize ? 1.0 / N : 1.0, _inner, _os, _st, _bfast);
                var t = src; src = dst; dst = t;
            }
            if (!ReferenceEquals(src, data))
            {
                if (_inner == 1 && _os == N && _st == 1) _s.Copy(src, 0, data, 0, 16 * total);
                else _s.Launch("uf_fft_copy", ctx.Groups(total), src, data, N, Batch, _inner, _os, _st);
            }
        }

        /// <summary>Multiply element q of every transform by (-1)^q (a swap of halves on the other side of the transform).</summary>
        public void AlternateSign(GpuBuffer data) => _s.Launch("uf_alt_sign", _s.Context.Groups((long)N / 2 * Batch), data, N, Batch, _inner, _os, _st);

        public void Dispose() { _vendor?.Dispose(); _tw?.Dispose(); _scratch?.Dispose(); }
    }

    /// <summary>
    /// Batched single-precision complex FFT over contiguous rows (compute.precision = mixed): the built-in radix-4 Stockham kernels
    /// in float (power-of-two lengths), the same sign and normalisation convention as <see cref="GpuFft"/>. Twiddles are computed in
    /// double and rounded once. Elements are two floats (8 bytes).
    /// </summary>
    public sealed class GpuFftF : IDisposable
    {
        private readonly GpuStream _s;
        private readonly GpuBuffer _tw, _scratch;
        public int N { get; }
        public int Batch { get; }

        public GpuFftF(GpuStream s, int n, int batch)
        {
            _s = s; N = n; Batch = batch;
            if (!GpuFft.BuiltInSupports(n)) throw new GpuException("mixed precision needs a power-of-two grid (got " + n + ")");
            var tw = new float[2 * n];
            for (int k = 0; k < n; k++) { double a = -2.0 * Math.PI * k / n; tw[2 * k] = (float)Math.Cos(a); tw[2 * k + 1] = (float)Math.Sin(a); }
            _tw = s.Context.Alloc(8L * n);
            unsafe { fixed (float* p = tw) s.Write(_tw, 0, p, 8L * n); }
            _scratch = s.Context.Alloc(8L * n * batch);
        }

        public void Transform(GpuBuffer data, int exponentSign, bool normalize)
        {
            var ctx = _s.Context;
            long total = (long)N * Batch;
            double sg = exponentSign > 0 ? -1.0 : 1.0;
            GpuBuffer src = data, dst = _scratch;
            int len = N, st = 1, passes = 0;
            for (int l = N; l >= 4; l >>= 2) passes++;
            bool radix2 = (N >> (2 * passes)) == 2;
            int totalPasses = passes + (radix2 ? 1 : 0), done = 0;
            while (len >= 4)
            {
                done++;
                double scale = normalize && done == totalPasses ? 1.0 / N : 1.0;
                _s.Launch("uf_fft4f", ctx.Groups(total / 4), src, dst, _tw, N, len, st, N / len, sg, Batch, scale);
                var t = src; src = dst; dst = t;
                len >>= 2; st <<= 2;
            }
            if (len == 2)
            {
                _s.Launch("uf_fft2f", ctx.Groups(total / 2), src, dst, N, Batch, normalize ? 1.0 / N : 1.0);
                var t = src; src = dst; dst = t;
            }
            if (!ReferenceEquals(src, data)) _s.Copy(src, 0, data, 0, 8 * total);
        }

        public void Dispose() { _tw?.Dispose(); _scratch?.Dispose(); }
    }
}
