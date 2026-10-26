using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Plugins.Gpu
{
    /// <summary>
    /// CUDA through the driver API (libcuda / nvcuda.dll, installed with the NVIDIA driver), kernels compiled at run
    /// time with NVRTC (CUDA toolkit or the nvidia-cuda-nvrtc pip wheel), and cuFFT / cuBLAS when they are found
    /// (optional: the built-in FFT and matrix kernels are used otherwise). Libraries are searched in
    /// ULTRAFAST_CUDA_LIB_DIR, CUDA_PATH / CUDA_HOME, the usual install folders and the loader path.
    /// </summary>
    public sealed unsafe class CudaApi
    {
        public readonly NativeLib Driver, Nvrtc, Cufft, Cublas;
        public readonly delegate* unmanaged[Cdecl]<uint, int> Init;
        public readonly delegate* unmanaged[Cdecl]<int*, int> DriverGetVersion;
        public readonly delegate* unmanaged[Cdecl]<int*, int> DeviceGetCount;
        public readonly delegate* unmanaged[Cdecl]<int*, int, int> DeviceGet;
        public readonly delegate* unmanaged[Cdecl]<byte*, int, int, int> DeviceGetName;
        public readonly delegate* unmanaged[Cdecl]<nuint*, int, int> DeviceTotalMem;
        public readonly delegate* unmanaged[Cdecl]<int*, int, int, int> DeviceGetAttribute;
        public readonly delegate* unmanaged[Cdecl]<IntPtr*, int, int> PrimaryCtxRetain;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, int> CtxSetCurrent;
        public readonly delegate* unmanaged[Cdecl]<IntPtr*, void*, int> ModuleLoadData;
        public readonly delegate* unmanaged[Cdecl]<IntPtr*, IntPtr, byte*, int> ModuleGetFunction;
        public readonly delegate* unmanaged[Cdecl]<ulong*, nuint, int> MemAlloc;
        public readonly delegate* unmanaged[Cdecl]<ulong, int> MemFree;
        public readonly delegate* unmanaged[Cdecl]<ulong, void*, nuint, IntPtr, int> MemcpyHtoDAsync;
        public readonly delegate* unmanaged[Cdecl]<void*, ulong, nuint, IntPtr, int> MemcpyDtoHAsync;
        public readonly delegate* unmanaged[Cdecl]<ulong, ulong, nuint, IntPtr, int> MemcpyDtoDAsync;
        public readonly delegate* unmanaged[Cdecl]<IntPtr*, uint, int> StreamCreate;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, int> StreamSynchronize, StreamDestroy;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, uint, uint, uint, uint, uint, uint, uint, IntPtr, void**, void**, int> LaunchKernel;
        public readonly delegate* unmanaged[Cdecl]<int, byte**, int> GetErrorString;
        // NVRTC
        public readonly delegate* unmanaged[Cdecl]<IntPtr*, byte*, byte*, int, byte**, byte**, int> NvrtcCreateProgram;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, int, byte**, int> NvrtcCompileProgram;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, nuint*, int> NvrtcGetPTXSize, NvrtcGetProgramLogSize;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, byte*, int> NvrtcGetPTX, NvrtcGetProgramLog;
        public readonly delegate* unmanaged[Cdecl]<IntPtr*, int> NvrtcDestroyProgram;
        public readonly delegate* unmanaged[Cdecl]<int*, int*, int> NvrtcVersion;
        // cuFFT (optional)
        public readonly delegate* unmanaged[Cdecl]<int*, int, int, int, int> CufftPlan1d;
        public readonly delegate* unmanaged[Cdecl]<int, IntPtr, int> CufftSetStream;
        public readonly delegate* unmanaged[Cdecl]<int, ulong, ulong, int, int> CufftExecZ2Z;
        public readonly delegate* unmanaged[Cdecl]<int, int> CufftDestroy;
        // cuBLAS (optional)
        public readonly delegate* unmanaged[Cdecl]<IntPtr*, int> CublasCreate;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int> CublasSetStream;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int, int, double*, ulong, int, ulong, int, double*, ulong, int, int> CublasDgemm;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, int> CublasDestroy;

        public const int ATTR_MULTIPROCESSOR_COUNT = 16, ATTR_CC_MAJOR = 75, ATTR_CC_MINOR = 76;

        private CudaApi(NativeLib drv, NativeLib nvrtc, NativeLib cufft, NativeLib cublas)
        {
            Driver = drv; Nvrtc = nvrtc; Cufft = cufft; Cublas = cublas;
            Init = (delegate* unmanaged[Cdecl]<uint, int>)drv.Get("cuInit");
            DriverGetVersion = (delegate* unmanaged[Cdecl]<int*, int>)drv.Get("cuDriverGetVersion");
            DeviceGetCount = (delegate* unmanaged[Cdecl]<int*, int>)drv.Get("cuDeviceGetCount");
            DeviceGet = (delegate* unmanaged[Cdecl]<int*, int, int>)drv.Get("cuDeviceGet");
            DeviceGetName = (delegate* unmanaged[Cdecl]<byte*, int, int, int>)drv.Get("cuDeviceGetName");
            DeviceTotalMem = (delegate* unmanaged[Cdecl]<nuint*, int, int>)drv.Get("cuDeviceTotalMem_v2");
            DeviceGetAttribute = (delegate* unmanaged[Cdecl]<int*, int, int, int>)drv.Get("cuDeviceGetAttribute");
            PrimaryCtxRetain = (delegate* unmanaged[Cdecl]<IntPtr*, int, int>)drv.Get("cuDevicePrimaryCtxRetain");
            CtxSetCurrent = (delegate* unmanaged[Cdecl]<IntPtr, int>)drv.Get("cuCtxSetCurrent");
            ModuleLoadData = (delegate* unmanaged[Cdecl]<IntPtr*, void*, int>)drv.Get("cuModuleLoadData");
            ModuleGetFunction = (delegate* unmanaged[Cdecl]<IntPtr*, IntPtr, byte*, int>)drv.Get("cuModuleGetFunction");
            MemAlloc = (delegate* unmanaged[Cdecl]<ulong*, nuint, int>)drv.Get("cuMemAlloc_v2");
            MemFree = (delegate* unmanaged[Cdecl]<ulong, int>)drv.Get("cuMemFree_v2");
            MemcpyHtoDAsync = (delegate* unmanaged[Cdecl]<ulong, void*, nuint, IntPtr, int>)drv.Get("cuMemcpyHtoDAsync_v2");
            MemcpyDtoHAsync = (delegate* unmanaged[Cdecl]<void*, ulong, nuint, IntPtr, int>)drv.Get("cuMemcpyDtoHAsync_v2");
            MemcpyDtoDAsync = (delegate* unmanaged[Cdecl]<ulong, ulong, nuint, IntPtr, int>)drv.Get("cuMemcpyDtoDAsync_v2");
            StreamCreate = (delegate* unmanaged[Cdecl]<IntPtr*, uint, int>)drv.Get("cuStreamCreate");
            StreamSynchronize = (delegate* unmanaged[Cdecl]<IntPtr, int>)drv.Get("cuStreamSynchronize");
            StreamDestroy = (delegate* unmanaged[Cdecl]<IntPtr, int>)drv.Get("cuStreamDestroy_v2");
            LaunchKernel = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint, uint, uint, uint, uint, uint, IntPtr, void**, void**, int>)drv.Get("cuLaunchKernel");
            var ges = drv.Find("cuGetErrorString"); if (ges != IntPtr.Zero) GetErrorString = (delegate* unmanaged[Cdecl]<int, byte**, int>)ges;
            if (nvrtc != null)
            {
                NvrtcCreateProgram = (delegate* unmanaged[Cdecl]<IntPtr*, byte*, byte*, int, byte**, byte**, int>)nvrtc.Get("nvrtcCreateProgram");
                NvrtcCompileProgram = (delegate* unmanaged[Cdecl]<IntPtr, int, byte**, int>)nvrtc.Get("nvrtcCompileProgram");
                NvrtcGetPTXSize = (delegate* unmanaged[Cdecl]<IntPtr, nuint*, int>)nvrtc.Get("nvrtcGetPTXSize");
                NvrtcGetPTX = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int>)nvrtc.Get("nvrtcGetPTX");
                NvrtcGetProgramLogSize = (delegate* unmanaged[Cdecl]<IntPtr, nuint*, int>)nvrtc.Get("nvrtcGetProgramLogSize");
                NvrtcGetProgramLog = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int>)nvrtc.Get("nvrtcGetProgramLog");
                NvrtcDestroyProgram = (delegate* unmanaged[Cdecl]<IntPtr*, int>)nvrtc.Get("nvrtcDestroyProgram");
                NvrtcVersion = (delegate* unmanaged[Cdecl]<int*, int*, int>)nvrtc.Get("nvrtcVersion");
            }
            if (cufft != null)
            {
                CufftPlan1d = (delegate* unmanaged[Cdecl]<int*, int, int, int, int>)cufft.Get("cufftPlan1d");
                CufftSetStream = (delegate* unmanaged[Cdecl]<int, IntPtr, int>)cufft.Get("cufftSetStream");
                CufftExecZ2Z = (delegate* unmanaged[Cdecl]<int, ulong, ulong, int, int>)cufft.Get("cufftExecZ2Z");
                CufftDestroy = (delegate* unmanaged[Cdecl]<int, int>)cufft.Get("cufftDestroy");
            }
            if (cublas != null)
            {
                CublasCreate = (delegate* unmanaged[Cdecl]<IntPtr*, int>)cublas.Get("cublasCreate_v2");
                CublasSetStream = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)cublas.Get("cublasSetStream_v2");
                CublasDgemm = (delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int, int, double*, ulong, int, ulong, int, double*, ulong, int, int>)cublas.Get("cublasDgemm_v2");
                CublasDestroy = (delegate* unmanaged[Cdecl]<IntPtr, int>)cublas.Get("cublasDestroy_v2");
            }
        }

        /// <summary>Folders searched for the toolkit libraries.</summary>
        public static List<string> SearchDirs()
        {
            var d = new List<string>();
            var own = Environment.GetEnvironmentVariable("ULTRAFAST_CUDA_LIB_DIR");
            if (!string.IsNullOrEmpty(own)) d.AddRange(own.Split(System.IO.Path.PathSeparator));
            if (NativeLib.IsWindows)
            {
                d.AddRange(NativeLib.EnvDirs(new[] { "CUDA_PATH" }, new[] { "bin\\x64", "bin" }));
                var pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? "C:\\Program Files";
                var root = System.IO.Path.Combine(pf, "NVIDIA GPU Computing Toolkit", "CUDA");
                try { if (Directory.Exists(root)) foreach (var v in Directory.GetDirectories(root).OrderByDescending(x => x, StringComparer.Ordinal)) { d.Add(System.IO.Path.Combine(v, "bin", "x64")); d.Add(System.IO.Path.Combine(v, "bin")); } } catch { }
            }
            else
            {
                d.AddRange(NativeLib.EnvDirs(new[] { "CUDA_HOME", "CUDA_PATH" }, new[] { "lib64", "lib", "targets/x86_64-linux/lib", "targets/sbsa-linux/lib" }));
                d.AddRange(new[] { "/usr/local/cuda/lib64", "/usr/local/cuda/targets/x86_64-linux/lib", "/usr/local/cuda/targets/sbsa-linux/lib", "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib64", "/opt/cuda/lib64" });
                try { foreach (var v in Directory.GetDirectories("/usr/local", "cuda-*").OrderByDescending(x => x, StringComparer.Ordinal)) d.Add(System.IO.Path.Combine(v, "lib64")); } catch { }
            }
            return d.Where(Directory.Exists).Distinct().ToList();
        }

        private static IEnumerable<string> Candidates(string winPattern, string linuxPattern, string[] linuxNames, List<string> dirs)
        {
            if (NativeLib.IsWindows)
                foreach (var f in NativeLib.Glob(dirs, winPattern)) { var n = System.IO.Path.GetFileName(f).ToLowerInvariant(); if (!n.Contains("builtins") && !n.Contains(".alt.")) yield return f; }
            else
            {
                foreach (var f in NativeLib.Glob(dirs, linuxPattern)) if (!f.EndsWith(".a")) yield return f;
                foreach (var n in linuxNames) yield return n;
            }
        }

        public static CudaApi TryLoad(out string why, out List<string> notes)
        {
            why = null; notes = new List<string>();
            if (IntPtr.Size != 8) { why = "the GPU back-ends need a 64-bit process"; return null; }
            if (NativeLib.IsMac) { why = "CUDA is not available on macOS (the CPU uses Apple Accelerate; see docs/plugins/gpu.md)"; return null; }
            var drvNames = NativeLib.IsWindows ? new[] { "nvcuda.dll" } : new[] { "libcuda.so.1", "libcuda.so", "/usr/lib/wsl/lib/libcuda.so.1" };
            var drv = NativeLib.TryLoad(drvNames, out var t1);
            if (drv == null) { why = "NVIDIA driver not found (tried " + t1 + ")"; return null; }
            var dirs = SearchDirs();
            var nvrtc = NativeLib.TryLoad(Candidates("nvrtc64_*.dll", "libnvrtc.so*", new[] { "libnvrtc.so.13", "libnvrtc.so.12", "libnvrtc.so.11.2", "libnvrtc.so" }, dirs), out var t2);
            if (nvrtc == null)
            {
                why = "NVRTC (the CUDA run-time compiler) not found: install the CUDA toolkit (or pip install nvidia-cuda-nvrtc-cu12) and/or set ULTRAFAST_CUDA_LIB_DIR to the folder holding " +
                      (NativeLib.IsWindows ? "nvrtc64_*.dll" : "libnvrtc.so") + "; the \"opencl\" back-end works with the driver alone";
                return null;
            }
            NativeLib cufft = null, cublas = null;
            if (Environment.GetEnvironmentVariable("ULTRAFAST_CUDA_NO_VENDOR") != "1")
            {
                cufft = NativeLib.TryLoad(Candidates("cufft64_*.dll", "libcufft.so*", new[] { "libcufft.so.12", "libcufft.so.11", "libcufft.so.10", "libcufft.so" }, dirs), out _);
                cublas = NativeLib.TryLoad(Candidates("cublas64_*.dll", "libcublas.so*", new[] { "libcublas.so.13", "libcublas.so.12", "libcublas.so.11", "libcublas.so" }, dirs), out _);
                if (cufft != null && cufft.Find("cufftExecZ2Z") == IntPtr.Zero) cufft = null;
                if (cublas != null && cublas.Find("cublasDgemm_v2") == IntPtr.Zero) cublas = null;
            }
            notes.Add("NVRTC " + System.IO.Path.GetFileName(nvrtc.Path));
            notes.Add(cufft != null ? "cuFFT " + System.IO.Path.GetFileName(cufft.Path) : "cuFFT not found: built-in FFT");
            notes.Add(cublas != null ? "cuBLAS " + System.IO.Path.GetFileName(cublas.Path) : "cuBLAS not found: built-in matrix kernel");
            try
            {
                var api = new CudaApi(drv, nvrtc, cufft, cublas);
                int e = api.Init(0);
                if (e != 0) { why = "cuInit failed: " + api.Error(e); return null; }
                return api;
            }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        public string Error(int code)
        {
            if (GetErrorString != null) { byte* s = null; if (GetErrorString(code, &s) == 0 && s != null) return code + " (" + Marshal(s) + ")"; }
            return code.ToString();
        }

        internal static string Marshal(byte* s)
        {
            int n = 0; while (s[n] != 0) n++;
            return Encoding.ASCII.GetString(s, n);
        }

        public void Check(int err, string call)
        {
            if (err != 0) throw new GpuException(call + " failed (CUDA error " + Error(err) + ")", err);
        }

        public string DeviceName(int dev)
        {
            var b = new byte[256];
            fixed (byte* p = b) if (DeviceGetName(p, b.Length, dev) != 0) return "?";
            int n = Array.IndexOf(b, (byte)0); return Encoding.ASCII.GetString(b, 0, n < 0 ? b.Length : n);
        }

        public int Attribute(int dev, int attr) { int v = 0; DeviceGetAttribute(&v, attr, dev); return v; }
    }

    /// <summary>A CUDA device: primary context, the kernels compiled once by NVRTC, cuFFT plans and a cuBLAS handle per stream.</summary>
    public sealed unsafe class CudaContext : GpuContext
    {
        internal readonly CudaApi Api;
        internal readonly IntPtr Ctx, Module;
        internal readonly int Dev;
        private readonly Dictionary<string, IntPtr> _functions = new Dictionary<string, IntPtr>();
        [ThreadStatic] private static IntPtr _current;

        public CudaContext(CudaApi api, int ordinal, ComputeDeviceInfo info)
        {
            Api = api; Info = info;
            int dev; api.Check(api.DeviceGet(&dev, ordinal), "cuDeviceGet"); Dev = dev;
            IntPtr ctx; api.Check(api.PrimaryCtxRetain(&ctx, dev), "cuDevicePrimaryCtxRetain"); Ctx = ctx;
            MakeCurrent();
            var ptx = Compile(api, api.Attribute(dev, CudaApi.ATTR_CC_MAJOR) * 10 + api.Attribute(dev, CudaApi.ATTR_CC_MINOR));
            IntPtr mod;
            fixed (byte* p = ptx)
            {
                int e = api.ModuleLoadData(&mod, p);
                if (e == 222) throw new GpuException("the NVIDIA driver is older than NVRTC (CUDA_ERROR_UNSUPPORTED_PTX_VERSION): update the driver, or point ULTRAFAST_CUDA_LIB_DIR at an older toolkit", e);
                api.Check(e, "cuModuleLoadData");
            }
            Module = mod;
            GroupSize = 256;
            MaxGroups = Math.Max(64, 32 * Math.Max(1, info.ComputeUnits));
            if (api.CufftPlan1d != null) VendorFftName = "cuFFT";
            if (api.CublasDgemm != null) VendorBlasName = "cuBLAS";
        }

        /// <summary>PTX for compute capability <paramref name="cc"/> (falls back to older virtual architectures when
        /// NVRTC does not know the device's).</summary>
        private static byte[] Compile(CudaApi api, int cc)
        {
            var src = Encoding.ASCII.GetBytes(Kernels.Source + "\0");
            var name = Encoding.ASCII.GetBytes("ultrafast_kernels.cu\0");
            string lastLog = "";
            var archs = new List<int> { cc };
            foreach (var a in new[] { 90, 89, 86, 80, 75, 70, 61, 60, 52 }) if (a < cc && !archs.Contains(a)) archs.Add(a);
            var ptrs = stackalloc byte*[3];
            foreach (var arch in archs)
            {
                IntPtr prog;
                fixed (byte* s = src) fixed (byte* nm = name) api.Check(api.NvrtcCreateProgram(&prog, s, nm, 0, null, null), "nvrtcCreateProgram");
                var opts = new[] { "--gpu-architecture=compute_" + arch, "-DUF_CUDA=1", "--fmad=false" }.Select(o => Encoding.ASCII.GetBytes(o + "\0")).ToArray();
                var handles = opts.Select(o => System.Runtime.InteropServices.GCHandle.Alloc(o, System.Runtime.InteropServices.GCHandleType.Pinned)).ToArray();
                try
                {
                    for (int i = 0; i < opts.Length; i++) ptrs[i] = (byte*)handles[i].AddrOfPinnedObject();
                    int e = api.NvrtcCompileProgram(prog, opts.Length, ptrs);
                    if (e == 0)
                    {
                        nuint size; api.Check(api.NvrtcGetPTXSize(prog, &size), "nvrtcGetPTXSize");
                        var ptx = new byte[(int)size];
                        fixed (byte* p = ptx) api.Check(api.NvrtcGetPTX(prog, p), "nvrtcGetPTX");
                        api.NvrtcDestroyProgram(&prog);
                        return ptx;
                    }
                    nuint ls; api.NvrtcGetProgramLogSize(prog, &ls);
                    var log = new byte[Math.Max(1, (int)ls)];
                    fixed (byte* l = log) api.NvrtcGetProgramLog(prog, l);
                    lastLog = Encoding.ASCII.GetString(log).TrimEnd('\0');
                    api.NvrtcDestroyProgram(&prog);
                    if (!lastLog.Contains("compute_")) break;   // a real compile error, not an unknown architecture
                }
                finally { foreach (var h in handles) h.Free(); }
            }
            throw new GpuException("NVRTC could not compile the kernels:\n" + lastLog);
        }

        internal void MakeCurrent()
        {
            if (_current == Ctx) return;
            Api.Check(Api.CtxSetCurrent(Ctx), "cuCtxSetCurrent");
            _current = Ctx;
        }

        internal IntPtr Function(string name)
        {
            lock (_functions)
            {
                if (_functions.TryGetValue(name, out var f)) return f;
                var b = Encoding.ASCII.GetBytes(name + "\0");
                fixed (byte* p = b) Api.Check(Api.ModuleGetFunction(&f, Module, p), "cuModuleGetFunction " + name);
                _functions[name] = f;
                return f;
            }
        }

        public override GpuBuffer Alloc(long bytes)
        {
            MakeCurrent();
            ulong p; Api.Check(Api.MemAlloc(&p, (nuint)Math.Max(16, bytes)), "cuMemAlloc (" + bytes + " bytes)");
            return new CuBuffer(this, p, bytes);
        }

        public override GpuStream CreateStream() => new CuStream(this);
        public override void Dispose() { }   // the primary context lives as long as the process

        internal sealed class CuBuffer : GpuBuffer
        {
            private readonly CudaContext _c;
            public ulong Ptr;
            public CuBuffer(CudaContext c, ulong p, long bytes) { _c = c; Ptr = p; Bytes = bytes; }
            public override void Dispose() { if (Ptr != 0) { _c.MakeCurrent(); _c.Api.MemFree(Ptr); Ptr = 0; } }
        }

        internal sealed class CuStream : GpuStream
        {
            private readonly CudaContext _c;
            internal readonly IntPtr S;
            private IntPtr _blas;

            public CuStream(CudaContext c)
            {
                _c = c; Context = c;
                c.MakeCurrent();
                IntPtr s; c.Api.Check(c.Api.StreamCreate(&s, 1 /* non-blocking */), "cuStreamCreate"); S = s;
            }

            public override void Launch(string kernel, int groups, params KArg[] args)
            {
                _c.MakeCurrent();
                var f = _c.Function(kernel);
                int n = args.Length;
                ulong* vals = stackalloc ulong[n];
                void** ptrs = stackalloc void*[n];
                for (int i = 0; i < n; i++)
                {
                    var a = args[i];
                    if (a.Kind == 0) vals[i] = ((CuBuffer)a.Buffer).Ptr;
                    else if (a.Kind == 1) { vals[i] = 0; *(int*)&vals[i] = a.I; }
                    else *(double*)&vals[i] = a.D;
                    ptrs[i] = &vals[i];
                }
                _c.Api.Check(_c.Api.LaunchKernel(f, (uint)groups, 1, 1, (uint)_c.GroupSize, 1, 1, 0, S, ptrs, null), "cuLaunchKernel " + kernel);
            }

            public override void Write(GpuBuffer dst, long off, void* src, long bytes)
            {
                if (bytes <= 0) return;
                _c.MakeCurrent();
                _c.Api.Check(_c.Api.MemcpyHtoDAsync(((CuBuffer)dst).Ptr + (ulong)off, src, (nuint)bytes, S), "cuMemcpyHtoDAsync");
                _c.Api.Check(_c.Api.StreamSynchronize(S), "cuStreamSynchronize");   // the host array is only pinned for this call
            }

            public override void Read(GpuBuffer src, long off, void* dst, long bytes)
            {
                if (bytes <= 0) return;
                _c.MakeCurrent();
                _c.Api.Check(_c.Api.MemcpyDtoHAsync(dst, ((CuBuffer)src).Ptr + (ulong)off, (nuint)bytes, S), "cuMemcpyDtoHAsync");
                _c.Api.Check(_c.Api.StreamSynchronize(S), "cuStreamSynchronize");
            }

            public override void Copy(GpuBuffer src, long so, GpuBuffer dst, long dOff, long bytes)
            {
                if (bytes <= 0) return;
                _c.MakeCurrent();
                _c.Api.Check(_c.Api.MemcpyDtoDAsync(((CuBuffer)dst).Ptr + (ulong)dOff, ((CuBuffer)src).Ptr + (ulong)so, (nuint)bytes, S), "cuMemcpyDtoDAsync");
            }

            public override void Finish() { _c.MakeCurrent(); _c.Api.Check(_c.Api.StreamSynchronize(S), "cuStreamSynchronize"); }

            public override IVendorFft VendorFft(int n, int batch)
            {
                if (_c.Api.CufftPlan1d == null) return null;
                _c.MakeCurrent();
                int plan;
                int e = _c.Api.CufftPlan1d(&plan, n, 0x69 /* CUFFT_Z2Z */, batch);
                if (e != 0) return null;
                _c.Api.CufftSetStream(plan, S);
                return new CuFft(_c, plan);
            }

            public override bool VendorDgemmRowMajor(GpuBuffer a, GpuBuffer b, GpuBuffer c, int m, int n, int k, long bOff, long cOff)
            {
                if (_c.Api.CublasDgemm == null) return false;
                _c.MakeCurrent();
                if (_blas == IntPtr.Zero)
                {
                    IntPtr h; if (_c.Api.CublasCreate(&h) != 0) return false;
                    _c.Api.CublasSetStream(h, S); _blas = h;
                }
                // row-major C (m x n) = A (m x k) B (k x n)  <=>  column-major C^T (n x m) = B^T (n x k) A^T (k x m)
                double one = 1, zero = 0;
                int e = _c.Api.CublasDgemm(_blas, 0, 0, n, m, k, &one, ((CuBuffer)b).Ptr + (ulong)(8 * bOff), n, ((CuBuffer)a).Ptr, k, &zero, ((CuBuffer)c).Ptr + (ulong)(8 * cOff), n);
                return e == 0;
            }

            public override void Dispose()
            {
                _c.MakeCurrent();
                if (_blas != IntPtr.Zero) _c.Api.CublasDestroy(_blas);
                _c.Api.StreamDestroy(S);
            }
        }

        private sealed class CuFft : IVendorFft
        {
            private readonly CudaContext _c; private readonly int _plan;
            public CuFft(CudaContext c, int plan) { _c = c; _plan = plan; }
            public void Execute(GpuBuffer data, int exponentSign)
            {
                _c.MakeCurrent();
                var p = ((CuBuffer)data).Ptr;
                // cuFFT: CUFFT_FORWARD (-1) = exp(-i ...), CUFFT_INVERSE (+1) = exp(+i ...), both unnormalised
                _c.Api.Check(_c.Api.CufftExecZ2Z(_plan, p, p, exponentSign < 0 ? -1 : 1), "cufftExecZ2Z");
            }
            public void Dispose() { _c.MakeCurrent(); _c.Api.CufftDestroy(_plan); }
        }
    }
}
