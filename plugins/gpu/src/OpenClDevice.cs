using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Plugins.Gpu
{
    /// <summary>
    /// The OpenCL 1.2 C API, loaded at run time from the ICD loader (OpenCL.dll, libOpenCL.so.1) or, on macOS, from
    /// Apple's framework and from each vendor library named by an .icd file (Homebrew PoCL), all enumerated
    /// (<see cref="LoadAll"/>). Only the calls the back-end needs. 64-bit processes only (the x86 Windows API uses stdcall).
    /// </summary>
    public sealed unsafe class OpenClApi
    {
        public readonly NativeLib Lib;
        public readonly delegate* unmanaged[Cdecl]<uint, IntPtr*, uint*, int> GetPlatformIDs;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, uint, nuint, void*, nuint*, int> GetPlatformInfo;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, IntPtr*, uint*, int> GetDeviceIDs;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, uint, nuint, void*, nuint*, int> GetDeviceInfo;
        public readonly delegate* unmanaged[Cdecl]<IntPtr*, uint, IntPtr*, IntPtr, IntPtr, int*, IntPtr> CreateContext;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ulong, int*, IntPtr> CreateCommandQueue;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, ulong, nuint, void*, int*, IntPtr> CreateBuffer;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, uint, byte**, nuint*, int*, IntPtr> CreateProgramWithSource;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, uint, IntPtr*, byte*, IntPtr, IntPtr, int> BuildProgram;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, nuint, void*, nuint*, int> GetProgramBuildInfo;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, byte*, int*, IntPtr> CreateKernel;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, uint, nuint, void*, int> SetKernelArg;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, nuint*, nuint*, nuint*, uint, IntPtr*, IntPtr*, int> EnqueueNDRangeKernel;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, nuint, nuint, void*, uint, IntPtr*, IntPtr*, int> EnqueueWriteBuffer;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, nuint, nuint, void*, uint, IntPtr*, IntPtr*, int> EnqueueReadBuffer;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, nuint, nuint, nuint, uint, IntPtr*, IntPtr*, int> EnqueueCopyBuffer;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, int> Finish;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, int> ReleaseMemObject, ReleaseKernel, ReleaseProgram, ReleaseCommandQueue, ReleaseContext;

        public const uint PLATFORM_VERSION = 0x0901, PLATFORM_NAME = 0x0902, PLATFORM_VENDOR = 0x0903;
        public const uint DEVICE_TYPE = 0x1000, DEVICE_MAX_COMPUTE_UNITS = 0x1002, DEVICE_MAX_WORK_GROUP_SIZE = 0x1004,
            DEVICE_MAX_MEM_ALLOC_SIZE = 0x1010, DEVICE_GLOBAL_MEM_SIZE = 0x101F, DEVICE_NAME = 0x102B, DEVICE_VENDOR = 0x102C,
            DRIVER_VERSION = 0x102D, DEVICE_VERSION = 0x102F, DEVICE_DOUBLE_FP_CONFIG = 0x1032;
        public const ulong DEVICE_TYPE_CPU = 2, DEVICE_TYPE_GPU = 4, DEVICE_TYPE_ACCELERATOR = 8, DEVICE_TYPE_ALL = 0xFFFFFFFF;
        public const uint PROGRAM_BUILD_LOG = 0x1183;

        private OpenClApi(NativeLib lib)
        {
            Lib = lib;
            GetPlatformIDs = (delegate* unmanaged[Cdecl]<uint, IntPtr*, uint*, int>)lib.Get("clGetPlatformIDs");
            GetPlatformInfo = (delegate* unmanaged[Cdecl]<IntPtr, uint, nuint, void*, nuint*, int>)lib.Get("clGetPlatformInfo");
            GetDeviceIDs = (delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, IntPtr*, uint*, int>)lib.Get("clGetDeviceIDs");
            GetDeviceInfo = (delegate* unmanaged[Cdecl]<IntPtr, uint, nuint, void*, nuint*, int>)lib.Get("clGetDeviceInfo");
            CreateContext = (delegate* unmanaged[Cdecl]<IntPtr*, uint, IntPtr*, IntPtr, IntPtr, int*, IntPtr>)lib.Get("clCreateContext");
            CreateCommandQueue = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ulong, int*, IntPtr>)lib.Get("clCreateCommandQueue");
            CreateBuffer = (delegate* unmanaged[Cdecl]<IntPtr, ulong, nuint, void*, int*, IntPtr>)lib.Get("clCreateBuffer");
            CreateProgramWithSource = (delegate* unmanaged[Cdecl]<IntPtr, uint, byte**, nuint*, int*, IntPtr>)lib.Get("clCreateProgramWithSource");
            BuildProgram = (delegate* unmanaged[Cdecl]<IntPtr, uint, IntPtr*, byte*, IntPtr, IntPtr, int>)lib.Get("clBuildProgram");
            GetProgramBuildInfo = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, nuint, void*, nuint*, int>)lib.Get("clGetProgramBuildInfo");
            CreateKernel = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int*, IntPtr>)lib.Get("clCreateKernel");
            SetKernelArg = (delegate* unmanaged[Cdecl]<IntPtr, uint, nuint, void*, int>)lib.Get("clSetKernelArg");
            EnqueueNDRangeKernel = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, nuint*, nuint*, nuint*, uint, IntPtr*, IntPtr*, int>)lib.Get("clEnqueueNDRangeKernel");
            EnqueueWriteBuffer = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, nuint, nuint, void*, uint, IntPtr*, IntPtr*, int>)lib.Get("clEnqueueWriteBuffer");
            EnqueueReadBuffer = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, nuint, nuint, void*, uint, IntPtr*, IntPtr*, int>)lib.Get("clEnqueueReadBuffer");
            EnqueueCopyBuffer = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, nuint, nuint, nuint, uint, IntPtr*, IntPtr*, int>)lib.Get("clEnqueueCopyBuffer");
            Finish = (delegate* unmanaged[Cdecl]<IntPtr, int>)lib.Get("clFinish");
            ReleaseMemObject = (delegate* unmanaged[Cdecl]<IntPtr, int>)lib.Get("clReleaseMemObject");
            ReleaseKernel = (delegate* unmanaged[Cdecl]<IntPtr, int>)lib.Get("clReleaseKernel");
            ReleaseProgram = (delegate* unmanaged[Cdecl]<IntPtr, int>)lib.Get("clReleaseProgram");
            ReleaseCommandQueue = (delegate* unmanaged[Cdecl]<IntPtr, int>)lib.Get("clReleaseCommandQueue");
            ReleaseContext = (delegate* unmanaged[Cdecl]<IntPtr, int>)lib.Get("clReleaseContext");
        }

        /// <summary>Loads the OpenCL ICD loader (ULTRAFAST_OPENCL_LIBRARY overrides the search), or null with the reason.
        /// The first library of <see cref="LoadAll"/>.</summary>
        public static OpenClApi TryLoad(out string why)
        {
            var all = LoadAll(out why);
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>
        /// Every OpenCL library to enumerate, in order, or an empty list with the reason.
        /// <list type="bullet">
        /// <item>ULTRAFAST_OPENCL_LIBRARY: one or more libraries (separated by the path separator, ':' or ';'); when any of
        /// them loads, only those are used.</item>
        /// <item>Windows / Linux: the first ICD loader that loads (OpenCL.dll, libOpenCL.so.1); it lists every installed vendor.</item>
        /// <item>macOS: Apple's OpenCL.framework (the Apple GPU, no FP64) <b>and</b> every vendor library named by an
        /// <c>.icd</c> file in OCL_ICD_VENDORS, /opt/homebrew/etc/OpenCL/vendors, /usr/local/etc/OpenCL/vendors or
        /// /etc/OpenCL/vendors (for example Homebrew PoCL, a CPU device with FP64), loaded directly; an ICD loader
        /// (libOpenCL.dylib) is tried only when no .icd file names a library that loads.</item>
        /// </list>
        /// </summary>
        public static List<OpenClApi> LoadAll(out string why)
        {
            why = null;
            var apis = new List<OpenClApi>();
            if (IntPtr.Size != 8) { why = "the GPU back-ends need a 64-bit process"; return apis; }
            var tried = new List<string>();
            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool Add(string name)
            {
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) return false;
                var lib = NativeLib.TryLoad(new[] { name }, out var t);
                tried.Add(t);
                if (lib == null) return false;
                try { apis.Add(new OpenClApi(lib)); return true; }
                catch (Exception ex) { errors.Add(name + ": " + ex.Message); return false; }
            }

            var env = Environment.GetEnvironmentVariable("ULTRAFAST_OPENCL_LIBRARY");
            if (!string.IsNullOrEmpty(env))
            {
                foreach (var e in env.Split(new[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)) Add(e.Trim());
                if (apis.Count > 0) return apis;
            }
            if (NativeLib.IsWindows) Add("OpenCL.dll");
            else if (NativeLib.IsMac)
            {
                Add("/System/Library/Frameworks/OpenCL.framework/OpenCL");
                bool vendor = false;
                foreach (var v in IcdVendorLibraries()) vendor |= Add(v);
                if (!vendor)
                    foreach (var l in new[] { "/opt/homebrew/opt/opencl-icd-loader/lib/libOpenCL.1.dylib", "/opt/homebrew/opt/ocl-icd/lib/libOpenCL.1.dylib",
                                              "/usr/local/opt/opencl-icd-loader/lib/libOpenCL.1.dylib", "/usr/local/opt/ocl-icd/lib/libOpenCL.1.dylib", "libOpenCL.dylib" })
                        if (Add(l)) break;
            }
            else { if (!Add("libOpenCL.so.1")) Add("libOpenCL.so"); }
            if (apis.Count == 0)
                why = "OpenCL runtime not found (tried " + string.Join(", ", tried) + ")" + (errors.Count > 0 ? " (" + string.Join("; ", errors) + ")" : "")
                    + "; install your GPU driver or an OpenCL ICD loader";
            return apis;
        }

        /// <summary>The vendor libraries named by the OpenCL <c>.icd</c> files (macOS search folders; OCL_ICD_VENDORS first).</summary>
        public static IEnumerable<string> IcdVendorLibraries()
        {
            var dirs = new List<string>();
            var env = Environment.GetEnvironmentVariable("OCL_ICD_VENDORS");
            if (!string.IsNullOrEmpty(env)) dirs.Add(env);
            dirs.AddRange(new[] { "/opt/homebrew/etc/OpenCL/vendors", "/usr/local/etc/OpenCL/vendors", "/etc/OpenCL/vendors" });
            return IcdVendorLibraries(dirs);
        }

        /// <summary>The library named by each <c>.icd</c> file in <paramref name="dirs"/> (first non-empty, non-comment line).</summary>
        public static IEnumerable<string> IcdVendorLibraries(IEnumerable<string> dirs)
        {
            foreach (var f in NativeLib.Glob(dirs, "*.icd"))
            {
                string line = null;
                try { line = System.IO.File.ReadAllLines(f).Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0 && !x.StartsWith("#")); } catch { }
                if (!string.IsNullOrEmpty(line)) yield return line;
            }
        }

        public static void Check(int err, string call)
        {
            if (err != 0) throw new GpuException(call + " failed (OpenCL error " + err + ")", err);
        }

        public string PlatformString(IntPtr p, uint param)
        {
            var buf = new byte[1024]; nuint ret = 0;
            fixed (byte* b = buf) if (GetPlatformInfo(p, param, (nuint)buf.Length, b, &ret) != 0) return "";
            return Encoding.ASCII.GetString(buf, 0, (int)Math.Max(0, (long)ret - 1)).Trim('\0', ' ');
        }

        public string DeviceString(IntPtr d, uint param)
        {
            var buf = new byte[1024]; nuint ret = 0;
            fixed (byte* b = buf) if (GetDeviceInfo(d, param, (nuint)buf.Length, b, &ret) != 0) return "";
            return Encoding.ASCII.GetString(buf, 0, (int)Math.Max(0, (long)ret - 1)).Trim('\0', ' ');
        }

        public ulong DeviceULong(IntPtr d, uint param) { ulong v = 0; GetDeviceInfo(d, param, 8, &v, null); return v; }
        public uint DeviceUInt(IntPtr d, uint param) { uint v = 0; GetDeviceInfo(d, param, 4, &v, null); return v; }
        public ulong DeviceSize(IntPtr d, uint param) { nuint v = 0; GetDeviceInfo(d, param, (nuint)IntPtr.Size, &v, null); return v; }
    }

    /// <summary>An OpenCL device as the back-end lists it.</summary>
    public sealed class OpenClDeviceRef
    {
        public OpenClApi Api;
        public IntPtr Platform, Device;
        public ComputeDeviceInfo Info;
        public bool IsCpu;
    }

    /// <summary>OpenCL context + program on one device.</summary>
    public sealed unsafe class OpenClContext : GpuContext
    {
        internal readonly OpenClApi Api;
        internal readonly IntPtr Ctx, Program, Device;

        public OpenClContext(OpenClApi api, OpenClDeviceRef dev)
        {
            Api = api; Device = dev.Device; Info = dev.Info;
            int err;
            IntPtr d = dev.Device;
            var props = stackalloc IntPtr[3];
            props[0] = (IntPtr)0x1084; props[1] = dev.Platform; props[2] = IntPtr.Zero;   // CL_CONTEXT_PLATFORM
            Ctx = api.CreateContext(props, 1, &d, IntPtr.Zero, IntPtr.Zero, &err);
            OpenClApi.Check(err, "clCreateContext");
            Program = Build(Kernels.Source);
            ulong maxWg = api.DeviceSize(d, OpenClApi.DEVICE_MAX_WORK_GROUP_SIZE);
            int gs = 256; while (gs > 1 && (ulong)gs > maxWg) gs >>= 1;
            GroupSize = gs;
            MaxGroups = Math.Max(16, Math.Min(4096, 8 * Math.Max(1, Info.ComputeUnits) * (dev.IsCpu ? 1 : 16)));
        }

        private readonly Dictionary<string, IntPtr> _extra = new Dictionary<string, IntPtr>();

        /// <summary>The program that defines kernel <paramref name="name"/>.</summary>
        internal IntPtr ProgramOf(string name) { lock (_extra) return _extra.TryGetValue(name, out var p) ? p : Program; }

        protected override void BuildProgram(string kernelName, string source)
        {
            var p = Build(source);
            lock (_extra) _extra[kernelName] = p;
        }

        private IntPtr Build(string source)
        {
            int err;
            IntPtr d = Device, prog;
            var src = Encoding.ASCII.GetBytes(source);
            fixed (byte* s = src)
            {
                byte* sp = s; nuint len = (nuint)src.Length;
                prog = Api.CreateProgramWithSource(Ctx, 1, &sp, &len, &err);
                OpenClApi.Check(err, "clCreateProgramWithSource");
            }
            var opts = Encoding.ASCII.GetBytes("-D UF_OPENCL=1\0");
            int berr;
            fixed (byte* o = opts) berr = Api.BuildProgram(prog, 1, &d, o, IntPtr.Zero, IntPtr.Zero);
            if (berr != 0)
            {
                var log = new byte[65536]; nuint ret = 0;
                fixed (byte* l = log) Api.GetProgramBuildInfo(prog, d, OpenClApi.PROGRAM_BUILD_LOG, (nuint)log.Length, l, &ret);
                Api.ReleaseProgram(prog);
                throw new GpuException("OpenCL kernel build failed on " + Info.Name + " (error " + berr + "):\n" + Encoding.ASCII.GetString(log, 0, (int)Math.Max(0, (long)ret - 1)), berr);
            }
            return prog;
        }

        public override GpuBuffer Alloc(long bytes)
        {
            int err;
            var m = Api.CreateBuffer(Ctx, 1 /* READ_WRITE */, (nuint)Math.Max(16, bytes), null, &err);
            OpenClApi.Check(err, "clCreateBuffer (" + bytes + " bytes)");
            Track(bytes);
            return new ClBuffer(this, Api, m, bytes);
        }

        protected override void DropProgram(string kernelName)
        {
            lock (_extra) { if (_extra.TryGetValue(kernelName, out var p)) { _extra.Remove(kernelName); Api.ReleaseProgram(p); } }   // kernels made from it keep it alive until released
        }

        public override GpuStream CreateStream() => new ClStream(this);

        public override void Dispose()
        {
            lock (_extra) { foreach (var p in _extra.Values) Api.ReleaseProgram(p); _extra.Clear(); }
            if (Program != IntPtr.Zero) Api.ReleaseProgram(Program);
            if (Ctx != IntPtr.Zero) Api.ReleaseContext(Ctx);
        }

        internal sealed class ClBuffer : GpuBuffer
        {
            private readonly OpenClApi _api;
            public IntPtr Mem;
            private readonly GpuContext _ctx;
            public ClBuffer(GpuContext ctx, OpenClApi api, IntPtr m, long bytes) { _ctx = ctx; _api = api; Mem = m; Bytes = bytes; }
            public override void Dispose() { if (Mem != IntPtr.Zero) { _api.ReleaseMemObject(Mem); Mem = IntPtr.Zero; _ctx.Track(-Bytes); } }
        }

        internal sealed class ClStream : GpuStream
        {
            private readonly OpenClContext _c;
            private readonly IntPtr _q;
            private readonly Dictionary<string, IntPtr> _kernels = new Dictionary<string, IntPtr>();

            public ClStream(OpenClContext c)
            {
                _c = c; Context = c;
                int err;
                _q = c.Api.CreateCommandQueue(c.Ctx, c.Device, 0, &err);
                OpenClApi.Check(err, "clCreateCommandQueue");
            }

            private IntPtr Kernel(string name)
            {
                if (_kernels.TryGetValue(name, out var k)) return k;
                var b = Encoding.ASCII.GetBytes(name + "\0");
                int err;
                fixed (byte* p = b) k = _c.Api.CreateKernel(_c.ProgramOf(name), p, &err);
                OpenClApi.Check(err, "clCreateKernel " + name);
                _kernels[name] = k;
                return k;
            }

            public override void Launch(string kernel, int groups, params KArg[] args)
            {
                var k = Kernel(kernel);
                var api = _c.Api;
                for (int i = 0; i < args.Length; i++)
                {
                    var a = args[i];
                    int err;
                    if (a.Kind == 0) { var m = ((ClBuffer)a.Buffer).Mem; err = api.SetKernelArg(k, (uint)i, (nuint)IntPtr.Size, &m); }
                    else if (a.Kind == 1) { int v = a.I; err = api.SetKernelArg(k, (uint)i, 4, &v); }
                    else { double v = a.D; err = api.SetKernelArg(k, (uint)i, 8, &v); }
                    OpenClApi.Check(err, "clSetKernelArg " + kernel + " #" + i);
                }
                nuint global = (nuint)((long)groups * _c.GroupSize), local = (nuint)_c.GroupSize;
                OpenClApi.Check(api.EnqueueNDRangeKernel(_q, k, 1, null, &global, &local, 0, null, null), "clEnqueueNDRangeKernel " + kernel);
            }

            public override void Write(GpuBuffer dst, long off, void* src, long bytes)
            {
                if (bytes <= 0) return;
                OpenClApi.Check(_c.Api.EnqueueWriteBuffer(_q, ((ClBuffer)dst).Mem, 1, (nuint)off, (nuint)bytes, src, 0, null, null), "clEnqueueWriteBuffer");
            }

            public override void Read(GpuBuffer src, long off, void* dst, long bytes)
            {
                if (bytes <= 0) return;
                OpenClApi.Check(_c.Api.EnqueueReadBuffer(_q, ((ClBuffer)src).Mem, 1, (nuint)off, (nuint)bytes, dst, 0, null, null), "clEnqueueReadBuffer");
            }

            public override void Copy(GpuBuffer src, long so, GpuBuffer dst, long dOff, long bytes)
            {
                if (bytes <= 0) return;
                OpenClApi.Check(_c.Api.EnqueueCopyBuffer(_q, ((ClBuffer)src).Mem, ((ClBuffer)dst).Mem, (nuint)so, (nuint)dOff, (nuint)bytes, 0, null, null), "clEnqueueCopyBuffer");
            }

            public override void Finish() => OpenClApi.Check(_c.Api.Finish(_q), "clFinish");

            public override void Dispose()
            {
                foreach (var k in _kernels.Values) _c.Api.ReleaseKernel(k);
                _kernels.Clear();
                if (_q != IntPtr.Zero) _c.Api.ReleaseCommandQueue(_q);
            }
        }
    }

    /// <summary>Enumerates OpenCL devices (all platforms).</summary>
    public static unsafe class OpenClDevices
    {
        public static List<OpenClDeviceRef> List(OpenClApi api, out string why) => List(new[] { api }, out why);

        /// <summary>The devices of every library in order; a platform already listed through an earlier library (same
        /// platform name, vendor and version) is skipped.</summary>
        public static List<OpenClDeviceRef> List(IEnumerable<OpenClApi> apis, out string why)
        {
            why = null;
            var l = new List<OpenClDeviceRef>();
            var platforms = new HashSet<string>(StringComparer.Ordinal);
            var empty = new List<string>();
            foreach (var api in apis) ListOne(api, l, platforms, empty);
            if (l.Count == 0) why = empty.Count > 0 ? "no OpenCL platform installed (" + string.Join("; ", empty) + ")" : "no OpenCL device found";
            return l;
        }

        private static void ListOne(OpenClApi api, List<OpenClDeviceRef> l, HashSet<string> platforms, List<string> empty)
        {
            string lib = System.IO.Path.GetFileName(api.Lib.Path);
            uint np = 0;
            int e = api.GetPlatformIDs(0, null, &np);
            if (e != 0 || np == 0) { empty.Add(lib + " clGetPlatformIDs: " + (e != 0 ? "error " + e : "0 platforms")); return; }
            var plats = new IntPtr[np];
            fixed (IntPtr* pp = plats) api.GetPlatformIDs(np, pp, null);
            foreach (var p in plats)
            {
                uint nd = 0;
                if (api.GetDeviceIDs(p, OpenClApi.DEVICE_TYPE_ALL, 0, null, &nd) != 0 || nd == 0) continue;
                var devs = new IntPtr[nd];
                fixed (IntPtr* dp = devs) api.GetDeviceIDs(p, OpenClApi.DEVICE_TYPE_ALL, nd, dp, null);
                string pname = api.PlatformString(p, OpenClApi.PLATFORM_NAME);
                if (!platforms.Add(pname + "|" + api.PlatformString(p, OpenClApi.PLATFORM_VENDOR) + "|" + api.PlatformString(p, OpenClApi.PLATFORM_VERSION))) continue;
                foreach (var d in devs)
                {
                    ulong type = api.DeviceULong(d, OpenClApi.DEVICE_TYPE);
                    bool fp64 = api.DeviceULong(d, OpenClApi.DEVICE_DOUBLE_FP_CONFIG) != 0;
                    bool cpu = (type & OpenClApi.DEVICE_TYPE_CPU) != 0 && (type & (OpenClApi.DEVICE_TYPE_GPU | OpenClApi.DEVICE_TYPE_ACCELERATOR)) == 0;
                    string name = api.DeviceString(d, OpenClApi.DEVICE_NAME);
                    if (cpu && pname.Length > 0 && name.IndexOf(pname, StringComparison.OrdinalIgnoreCase) < 0) name = pname + " " + name;   // PoCL calls its device "cpu"
                    var info = new ComputeDeviceInfo
                    {
                        Backend = "opencl", Index = l.Count, Name = name, Vendor = api.DeviceString(d, OpenClApi.DEVICE_VENDOR),
                        Driver = pname + " " + api.DeviceString(d, OpenClApi.DRIVER_VERSION) + " (" + lib + ")", ComputeUnits = (int)api.DeviceUInt(d, OpenClApi.DEVICE_MAX_COMPUTE_UNITS),
                        MemoryBytes = (long)api.DeviceULong(d, OpenClApi.DEVICE_GLOBAL_MEM_SIZE), DoublePrecision = fp64, Usable = fp64,
                        Notes = (cpu ? "CPU device (used only with backend \"opencl\", never by \"auto\")" : "GPU")
                            + (fp64 ? "; double precision" : "; single precision only (no FP64): not usable, the back-ends need double precision")
                    };
                    l.Add(new OpenClDeviceRef { Api = api, Platform = p, Device = d, Info = info, IsCpu = cpu });
                }
            }
        }
    }
}
