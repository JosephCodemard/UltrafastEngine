using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Plugins.Gpu
{
    /// <summary>
    /// The OpenCL 1.2 C API, loaded at run time from the ICD loader (OpenCL.dll, libOpenCL.so.1, or the macOS
    /// framework). Only the calls the back-end needs. 64-bit processes only (the x86 Windows API uses stdcall).
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

        /// <summary>Loads the OpenCL ICD loader (ULTRAFAST_OPENCL_LIBRARY overrides the search), or null with the reason.</summary>
        public static OpenClApi TryLoad(out string why)
        {
            why = null;
            if (IntPtr.Size != 8) { why = "the GPU back-ends need a 64-bit process"; return null; }
            var names = new List<string> { Environment.GetEnvironmentVariable("ULTRAFAST_OPENCL_LIBRARY") };
            if (NativeLib.IsWindows) names.AddRange(new[] { "OpenCL.dll" });
            else if (NativeLib.IsMac) names.AddRange(new[] { "/System/Library/Frameworks/OpenCL.framework/OpenCL", "libOpenCL.dylib" });
            else names.AddRange(new[] { "libOpenCL.so.1", "libOpenCL.so" });
            var lib = NativeLib.TryLoad(names, out var tried);
            if (lib == null) { why = "OpenCL runtime not found (tried " + tried + "); install your GPU driver or an OpenCL ICD loader"; return null; }
            try { return new OpenClApi(lib); }
            catch (Exception ex) { why = ex.Message; return null; }
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
            var src = Encoding.ASCII.GetBytes(Kernels.Source);
            fixed (byte* s = src)
            {
                byte* sp = s; nuint len = (nuint)src.Length;
                Program = api.CreateProgramWithSource(Ctx, 1, &sp, &len, &err);
                OpenClApi.Check(err, "clCreateProgramWithSource");
            }
            var opts = Encoding.ASCII.GetBytes("-D UF_OPENCL=1\0");
            int berr;
            fixed (byte* o = opts) berr = api.BuildProgram(Program, 1, &d, o, IntPtr.Zero, IntPtr.Zero);
            if (berr != 0)
            {
                var log = new byte[65536]; nuint ret = 0;
                fixed (byte* l = log) api.GetProgramBuildInfo(Program, d, OpenClApi.PROGRAM_BUILD_LOG, (nuint)log.Length, l, &ret);
                throw new GpuException("OpenCL kernel build failed on " + Info.Name + " (error " + berr + "):\n" + Encoding.ASCII.GetString(log, 0, (int)Math.Max(0, (long)ret - 1)), berr);
            }
            ulong maxWg = api.DeviceSize(d, OpenClApi.DEVICE_MAX_WORK_GROUP_SIZE);
            int gs = 256; while (gs > 1 && (ulong)gs > maxWg) gs >>= 1;
            GroupSize = gs;
            MaxGroups = Math.Max(16, Math.Min(4096, 8 * Math.Max(1, Info.ComputeUnits) * (dev.IsCpu ? 1 : 16)));
        }

        public override GpuBuffer Alloc(long bytes)
        {
            int err;
            var m = Api.CreateBuffer(Ctx, 1 /* READ_WRITE */, (nuint)Math.Max(16, bytes), null, &err);
            OpenClApi.Check(err, "clCreateBuffer (" + bytes + " bytes)");
            return new ClBuffer(Api, m, bytes);
        }

        public override GpuStream CreateStream() => new ClStream(this);

        public override void Dispose()
        {
            if (Program != IntPtr.Zero) Api.ReleaseProgram(Program);
            if (Ctx != IntPtr.Zero) Api.ReleaseContext(Ctx);
        }

        internal sealed class ClBuffer : GpuBuffer
        {
            private readonly OpenClApi _api;
            public IntPtr Mem;
            public ClBuffer(OpenClApi api, IntPtr m, long bytes) { _api = api; Mem = m; Bytes = bytes; }
            public override void Dispose() { if (Mem != IntPtr.Zero) { _api.ReleaseMemObject(Mem); Mem = IntPtr.Zero; } }
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
                fixed (byte* p = b) k = _c.Api.CreateKernel(_c.Program, p, &err);
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
        public static List<OpenClDeviceRef> List(OpenClApi api, out string why)
        {
            why = null;
            var l = new List<OpenClDeviceRef>();
            uint np = 0;
            int e = api.GetPlatformIDs(0, null, &np);
            if (e != 0 || np == 0) { why = "no OpenCL platform installed (clGetPlatformIDs: " + (e != 0 ? "error " + e : "0 platforms") + ")"; return l; }
            var plats = new IntPtr[np];
            fixed (IntPtr* pp = plats) api.GetPlatformIDs(np, pp, null);
            foreach (var p in plats)
            {
                uint nd = 0;
                if (api.GetDeviceIDs(p, OpenClApi.DEVICE_TYPE_ALL, 0, null, &nd) != 0 || nd == 0) continue;
                var devs = new IntPtr[nd];
                fixed (IntPtr* dp = devs) api.GetDeviceIDs(p, OpenClApi.DEVICE_TYPE_ALL, nd, dp, null);
                string pname = api.PlatformString(p, OpenClApi.PLATFORM_NAME);
                foreach (var d in devs)
                {
                    ulong type = api.DeviceULong(d, OpenClApi.DEVICE_TYPE);
                    bool fp64 = api.DeviceULong(d, OpenClApi.DEVICE_DOUBLE_FP_CONFIG) != 0;
                    bool cpu = (type & OpenClApi.DEVICE_TYPE_CPU) != 0 && (type & (OpenClApi.DEVICE_TYPE_GPU | OpenClApi.DEVICE_TYPE_ACCELERATOR)) == 0;
                    var info = new ComputeDeviceInfo
                    {
                        Backend = "opencl", Index = l.Count, Name = api.DeviceString(d, OpenClApi.DEVICE_NAME), Vendor = api.DeviceString(d, OpenClApi.DEVICE_VENDOR),
                        Driver = pname + " " + api.DeviceString(d, OpenClApi.DRIVER_VERSION), ComputeUnits = (int)api.DeviceUInt(d, OpenClApi.DEVICE_MAX_COMPUTE_UNITS),
                        MemoryBytes = (long)api.DeviceULong(d, OpenClApi.DEVICE_GLOBAL_MEM_SIZE), DoublePrecision = fp64, Usable = fp64,
                        Notes = (cpu ? "CPU device (used only with backend \"opencl\", never by \"auto\")" : "GPU") + (fp64 ? "" : "; no double precision: not usable")
                    };
                    l.Add(new OpenClDeviceRef { Platform = p, Device = d, Info = info, IsCpu = cpu });
                }
            }
            if (l.Count == 0) why = "no OpenCL device found";
            return l;
        }
    }
}
