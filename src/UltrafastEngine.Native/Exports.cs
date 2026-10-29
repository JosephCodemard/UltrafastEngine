using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UltrafastEngine.Engine;
using UltrafastEngine.Instruments;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Native
{
    /// <summary>Layout-compatible with uf_response_ctx in include/ultrafast.h.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct UfResponseCtx
    {
        public int N;
        public double* T, TLab;
        public double TArrival, Z, Length;
        public double* Intensity, IntensityX, IntensityY, InstantIntensity, Field;
        public double Omega0, Area;
        public int CarrierResolved;
        public byte* ElementId;
        public double X, Y;
        public int Spatial;
    }

    /// <summary>Layout-compatible with uf_element_ctx in include/ultrafast.h.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct UfElementCtx
    {
        public int N;
        public double* Omega, T;
        public double Omega0, Dt;
        public double* AxRe, AxIm, AyRe, AyIm;
        public double Arrival;
        public byte* ElementId, ParamsJson;
    }

    internal sealed class CallbackException : Exception
    {
        public CallbackException(string m) : base(m) { }
    }

    internal sealed unsafe class NativeRun
    {
        public SystemDescription System;
        public RunSpec Spec;
        public ModelRegistry Registry;
        public delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int, double, int> Progress;
        public IntPtr ProgressUser;
    }

    /// <summary>
    /// The C API. Each method is an [UnmanagedCallersOnly] export: with NativeAOT these become plain C symbols
    /// of ultrafast.dll / libultrafast.so; in a hosted build they are retrieved as function pointers through
    /// hostfxr (this is what the Python package does in development mode). See include/ultrafast.h.
    /// </summary>
    /// <summary>Registers the compiled-in plugins when the library loads.</summary>
    internal static class NativeInit
    {
#pragma warning disable CA2255 // a library-load hook is exactly what this is
        [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init()
        {
#if UF_WITH_GPU
            UltrafastEngine.Plugins.Gpu.GpuPlugin.RegisterStatic();
#endif
#if UF_WITH_HITRAN
            UltrafastEngine.Plugins.Hitran.HitranPlugin.RegisterStatic();
#endif
        }
    }

    public static unsafe class Exports
    {
        public const int OK = 0, E_ARGUMENT = -1, E_PARSE = -2, E_MODEL = -3, E_RUN = -4, E_NOT_FOUND = -5, E_CALLBACK = -6, E_CANCELLED = -7, E_BUFFER = -8, E_INTERNAL = -99;

        [ThreadStatic] private static IntPtr _lastError;
        private static IntPtr _version;

        // ------------------------------------------------------------------ helpers
        private static void SetError(string msg)
        {
            if (_lastError != IntPtr.Zero) Marshal.FreeCoTaskMem(_lastError);
            _lastError = msg == null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(msg);
        }

        private static int Fail(Exception ex)
        {
            SetError(ex.Message);
            switch (ex)
            {
                case CallbackException _: return E_CALLBACK;
                case OperationCanceledException _: return E_CANCELLED;
                case JsonException _: case FormatException _: return E_PARSE;
                case ModelException _: return E_MODEL;
                case FileNotFoundException _: case DirectoryNotFoundException _: case KeyNotFoundException _: return E_NOT_FOUND;
                case ArgumentException _: return E_ARGUMENT;
                case System.Reflection.TargetInvocationException tie when tie.InnerException != null: return Fail(tie.InnerException);
                default: return E_RUN;
            }
        }

        private static string Str(byte* p) => p == null ? null : Marshal.PtrToStringUTF8((IntPtr)p);
        private static byte* OutStr(string s) => (byte*)Marshal.StringToCoTaskMemUTF8(s ?? "");
        private static IntPtr NewHandle(object o) => GCHandle.ToIntPtr(GCHandle.Alloc(o));
        private static T Get<T>(IntPtr h) where T : class
        {
            if (h == IntPtr.Zero) throw new ArgumentException("null handle");
            var o = GCHandle.FromIntPtr(h).Target as T;
            if (o == null) throw new ArgumentException("handle is not a " + typeof(T).Name);
            return o;
        }

        private static int Ok() { SetError(null); return OK; }

        // ------------------------------------------------------------------ library
        [UnmanagedCallersOnly(EntryPoint = "uf_version", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static byte* Version()
        {
            if (_version == IntPtr.Zero) _version = Marshal.StringToCoTaskMemUTF8(EngineInfo.Version);
            return (byte*)_version;
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_last_error", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static byte* LastError() => (byte*)_lastError;

        [UnmanagedCallersOnly(EntryPoint = "uf_free", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static void Free(IntPtr p) { if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p); }

        [UnmanagedCallersOnly(EntryPoint = "uf_release", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static void Release(IntPtr h) { if (h != IntPtr.Zero) GCHandle.FromIntPtr(h).Free(); }

        [UnmanagedCallersOnly(EntryPoint = "uf_capabilities", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int Capabilities(byte** json)
        {
            try
            {
                if (json == null) throw new ArgumentException("json_out is null");
                var reg = ModelRegistry.CreateDefault();
                var o = new JsonObject().Set("version", EngineInfo.Version).Set("models", reg.Describe())
                    .Set("elementTypes", JsonValue.FromArray(Compiler.ElementTypes))
                    .Set("materials", JsonValue.FromArray(MaterialLibrary.BuiltInNames))
                    .Set("instruments", JsonValue.FromArray(InstrumentFactory.Known))
                    .Set("crystals", JsonValue.FromArray(Crystals.CrystalLibrary.Names))
                    .Set("fft", Numerics.Fft.Provider.Name);
                *json = OutStr(o.ToJson(true));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        /// <summary>JSON command (studies, retrieval, materials): {"command": "study" | "retrieve" | "materials.list" | ...}.</summary>
        [UnmanagedCallersOnly(EntryPoint = "uf_command", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int Command(byte* request, byte** json)
        {
            try
            {
                if (json == null) throw new ArgumentException("result_json_out is null");
                var req = JsonValue.Parse(Str(request) ?? throw new ArgumentException("request_json is null")).AsObject();
                *json = OutStr(UltrafastEngine.Commands.Execute(req, req.GetString("baseDirectory")).ToJson(false));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        /// <summary>Compute back-ends and devices as JSON; options_json (may be null) as the "devices" command:
        /// {"selftest": bool, "benchmark": bool, "backend": name, "device": index}.</summary>
        [UnmanagedCallersOnly(EntryPoint = "uf_devices_json", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int DevicesJson(byte* options, byte** json)
        {
            try
            {
                if (json == null) throw new ArgumentException("json_out is null");
                var o = options == null ? new JsonObject() : JsonValue.Parse(Str(options)).AsObject();
                *json = OutStr(UltrafastEngine.Commands.Devices(o.GetBool("selftest", false), o.GetBool("benchmark", false), o.GetString("backend", null), o.GetInt("device", -1)).ToJson(true));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        /// <summary>Back-end for runs whose spec names none ("cpu", "auto", "cuda", "opencl"; null or "" restores
        /// ULTRAFAST_BACKEND / cpu). Unknown names fail with E_ARGUMENT.</summary>
        [UnmanagedCallersOnly(EntryPoint = "uf_set_default_backend", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SetDefaultBackend(byte* name)
        {
            try
            {
                var n = Str(name);
                if (!string.IsNullOrEmpty(n) && Numerics.ComputeBackends.Get(n) == null)
                    throw new ArgumentException("unknown compute back-end \"" + n + "\" (installed: " + string.Join(", ", Numerics.ComputeBackends.Available.Select(b => b.Name)) + ")");
                Numerics.ComputeBackends.DefaultName = n;
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_crystal_phase_match", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int CrystalPhaseMatch(byte* request, byte** json)
        {
            try
            {
                if (json == null) throw new ArgumentException("result_json_out is null");
                var req = JsonValue.Parse(Str(request) ?? throw new ArgumentException("request_json is null")).AsObject();
                *json = OutStr(Crystals.PhaseMatchCalculator.Solve(req).ToJson(true));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ------------------------------------------------------------------ systems
        [UnmanagedCallersOnly(EntryPoint = "uf_system_load_file", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SystemLoadFile(byte* path, IntPtr* outH)
        {
            try
            {
                if (outH == null) throw new ArgumentException("out is null");
                *outH = NewHandle(SystemDescription.Load(Str(path) ?? throw new ArgumentException("path is null")));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_system_load_json", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SystemLoadJson(byte* json, IntPtr* outH)
        {
            try
            {
                if (outH == null) throw new ArgumentException("out is null");
                *outH = NewHandle(SystemDescription.Parse(Str(json) ?? throw new ArgumentException("json is null"), Directory.GetCurrentDirectory()));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_system_to_json", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SystemToJson(IntPtr h, byte** json)
        {
            try { *json = OutStr(Get<SystemDescription>(h).ToJson().ToJson(true)); return Ok(); }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_system_validate", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SystemValidate(IntPtr h, byte** json)
        {
            try { *json = OutStr(Simulation.Validate(Get<SystemDescription>(h)).ToJson().ToJson(true)); return Ok(); }
            catch (Exception ex) { return Fail(ex); }
        }

        // ------------------------------------------------------------------ runs
        [UnmanagedCallersOnly(EntryPoint = "uf_run_create", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int RunCreate(IntPtr sys, byte* runJson, IntPtr* outH)
        {
            try
            {
                var s = Get<SystemDescription>(sys);
                var spec = RunSpec.Parse(Str(runJson) ?? throw new ArgumentException("run_json is null"), s.BaseDirectory ?? Directory.GetCurrentDirectory());
                *outH = NewHandle(new NativeRun { System = s, Spec = spec, Registry = ModelRegistry.CreateDefault() });
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_run_create_file", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int RunCreateFile(IntPtr sys, byte* path, IntPtr* outH)
        {
            try
            {
                var s = Get<SystemDescription>(sys);
                *outH = NewHandle(new NativeRun { System = s, Spec = RunSpec.Load(Str(path)), Registry = ModelRegistry.CreateDefault() });
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        private static void Check(int rc, string what) { if (rc != 0) throw new CallbackException("callback '" + what + "' returned " + rc); }

        [UnmanagedCallersOnly(EntryPoint = "uf_run_set_time_response_callback", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SetTimeResponse(IntPtr run, byte* name, IntPtr fn, IntPtr user, int carrierResolved)
        {
            try
            {
                var r = Get<NativeRun>(run); string n = Str(name);
                if (fn == IntPtr.Zero || string.IsNullOrEmpty(n)) throw new ArgumentException("name and fn are required");
                var f = (delegate* unmanaged[Cdecl]<IntPtr, UfResponseCtx*, double*, double*, int>)fn;
                r.Registry.RegisterTimeResponse(n, (ctx, re, im) =>
                {
                    var id = Encoding.UTF8.GetBytes((ctx.ElementId ?? "") + "\0");
                    var zeros = ctx.IntensityY == null ? new double[ctx.N] : null;
                    fixed (double* t = ctx.T, tl = ctx.TLab, I = ctx.Intensity, ix = ctx.IntensityX ?? ctx.Intensity, iy = ctx.IntensityY ?? zeros,
                           ii = ctx.InstantIntensity ?? ctx.Intensity, fld = ctx.Field, pre = re, pim = im)
                    fixed (byte* pid = id)
                    {
                        var c = new UfResponseCtx
                        {
                            N = ctx.N, T = t, TLab = tl, TArrival = ctx.TArrival, Z = ctx.Z, Length = ctx.ElementLength,
                            Intensity = I, IntensityX = ix, IntensityY = iy, InstantIntensity = ii, Field = fld,
                            Omega0 = ctx.Omega0, Area = ctx.EffectiveAreaM2, CarrierResolved = ctx.CarrierResolved ? 1 : 0, ElementId = pid,
                            X = ctx.X, Y = ctx.Y, Spatial = ctx.Spatial ? 1 : 0
                        };
                        Check(f(user, &c, pre, pim), n);
                    }
                }, carrierResolved != 0);
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_run_set_index_callback", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SetIndex(IntPtr run, byte* name, IntPtr fn, IntPtr user)
        {
            try
            {
                var r = Get<NativeRun>(run); string n = Str(name);
                var f = (delegate* unmanaged[Cdecl]<IntPtr, int, double*, double*, int>)fn;
                r.Registry.RegisterCallback(n, (IndexCallback)((w, o) => { fixed (double* pw = w, po = o) Check(f(user, w.Length, pw, po), n); }));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_run_set_filter_callback", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SetFilter(IntPtr run, byte* name, IntPtr fn, IntPtr user)
        {
            try
            {
                var r = Get<NativeRun>(run); string n = Str(name);
                var f = (delegate* unmanaged[Cdecl]<IntPtr, int, double*, double*, double*, int>)fn;
                r.Registry.RegisterCallback(n, (FilterCallback)((w, t, p) => { fixed (double* pw = w, pt = t, pp = p) Check(f(user, w.Length, pw, pt, pp), n); }));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_run_set_beam_callback", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SetBeam(IntPtr run, byte* name, IntPtr fn, IntPtr user)
        {
            try
            {
                var r = Get<NativeRun>(run); string n = Str(name);
                var f = (delegate* unmanaged[Cdecl]<IntPtr, int, double*, double*, double*, int>)fn;
                r.Registry.RegisterCallback(n, (BeamCallback)((z, x, y) => { fixed (double* pz = z, px = x, py = y) Check(f(user, z.Length, pz, px, py), n); }));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_run_set_field_callback", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SetField(IntPtr run, byte* name, IntPtr fn, IntPtr user)
        {
            try
            {
                var r = Get<NativeRun>(run); string n = Str(name);
                var f = (delegate* unmanaged[Cdecl]<IntPtr, int, double*, double*, double*, double*, int>)fn;
                r.Registry.RegisterCallback(n, (FieldCallback)((x, y, z, o) => { fixed (double* px = x, py = y, pz = z, po = o) Check(f(user, x.Length, px, py, pz, po), n); }));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_run_set_array_callback", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SetArray(IntPtr run, byte* name, IntPtr fn, IntPtr user)
        {
            try
            {
                var r = Get<NativeRun>(run); string n = Str(name);
                var f = (delegate* unmanaged[Cdecl]<IntPtr, int, double*, double*, int>)fn;
                // usable both where an ArrayCallback and where an IndexCallback (alpha(omega)) is expected
                r.Registry.RegisterCallback(n, (ArrayCallback)((x, y) => { fixed (double* px = x, py = y) Check(f(user, x.Length, px, py), n); }));
                r.Registry.RegisterCallback(n + "#index", (IndexCallback)((x, y) => { fixed (double* px = x, py = y) Check(f(user, x.Length, px, py), n); }));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_run_set_element_callback", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SetElement(IntPtr run, byte* name, IntPtr fn, IntPtr user)
        {
            try
            {
                var r = Get<NativeRun>(run); string n = Str(name);
                if (fn == IntPtr.Zero || string.IsNullOrEmpty(n)) throw new ArgumentException("name and fn are required");
                var f = (delegate* unmanaged[Cdecl]<IntPtr, UfElementCtx*, int>)fn;
                r.Registry.RegisterCallback(n, (ElementCallback)(ctx =>
                {
                    var id = Encoding.UTF8.GetBytes((ctx.ElementId ?? "") + "\0");
                    var pj = Encoding.UTF8.GetBytes((ctx.ParamsJson ?? "{}") + "\0");
                    fixed (double* w = ctx.Omega, t = ctx.T, xr = ctx.AxRe, xi = ctx.AxIm, yr = ctx.AyRe, yi = ctx.AyIm)
                    fixed (byte* pid = id, ppj = pj)
                    {
                        var c = new UfElementCtx { N = ctx.N, Omega = w, T = t, Omega0 = ctx.Omega0, Dt = ctx.DtS, AxRe = xr, AxIm = xi, AyRe = yr, AyIm = yi, Arrival = ctx.ArrivalS, ElementId = pid, ParamsJson = ppj };
                        Check(f(user, &c), n);
                    }
                }));
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_run_set_progress_callback", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SetProgress(IntPtr run, IntPtr fn, IntPtr user)
        {
            try
            {
                var r = Get<NativeRun>(run);
                r.Progress = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int, double, int>)fn;
                r.ProgressUser = user;
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_run_execute", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int RunExecute(IntPtr run, IntPtr* outRes)
        {
            try
            {
                var r = Get<NativeRun>(run);
                if (outRes == null) throw new ArgumentException("out is null");
                var cts = new CancellationTokenSource();
                ProgressCallback prog = null;
                if (r.Progress != null)
                {
                    var pf = r.Progress; var pu = r.ProgressUser;
                    prog = (id, i, n, f) =>
                    {
                        var b = Encoding.UTF8.GetBytes((id ?? "") + "\0");
                        fixed (byte* pb = b) if (pf(pu, pb, i, n, f) != 0) cts.Cancel();
                    };
                }
                var res = Simulation.Run(r.System, r.Spec, r.Registry, prog, cts.Token);
                *outRes = NewHandle(res);
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        // ------------------------------------------------------------------ results
        [UnmanagedCallersOnly(EntryPoint = "uf_result_summary_json", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int ResultSummary(IntPtr h, byte** json)
        {
            try { *json = OutStr(Get<RunResult>(h).SummaryJson().ToJson(true)); return Ok(); }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_result_provenance_json", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int ResultProvenance(IntPtr h, byte** json)
        {
            try { *json = OutStr(Get<RunResult>(h).Compiled.Provenance.ToJson().ToJson(true)); return Ok(); }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_result_array_names", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int ResultArrayNames(IntPtr h, byte** json)
        {
            try { *json = OutStr(JsonValue.FromArray(Get<RunResult>(h).Arrays().Select(a => a.Name)).ToJson(false)); return Ok(); }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_result_array_info", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int ResultArrayInfo(IntPtr h, byte* name, int* ndim, long* shape4, long* length)
        {
            try
            {
                var a = Get<RunResult>(h).GetArray(Str(name)) ?? throw new KeyNotFoundException("no array named '" + Str(name) + "'");
                if (ndim != null) *ndim = a.Shape.Length;
                if (shape4 != null) for (int i = 0; i < Math.Min(4, a.Shape.Length); i++) shape4[i] = a.Shape[i];
                if (length != null) *length = a.Data.Length;
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_result_array_copy", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int ResultArrayCopy(IntPtr h, byte* name, double* dest, long capacity)
        {
            try
            {
                var a = Get<RunResult>(h).GetArray(Str(name)) ?? throw new KeyNotFoundException("no array named '" + Str(name) + "'");
                if (dest == null) throw new ArgumentException("dest is null");
                if (capacity < a.Data.Length) { SetError("buffer holds " + capacity + " values, array has " + a.Data.Length); return E_BUFFER; }
                Marshal.Copy(a.Data, 0, (IntPtr)dest, a.Data.Length);
                return Ok();
            }
            catch (Exception ex) { return Fail(ex); }
        }

        [UnmanagedCallersOnly(EntryPoint = "uf_result_write", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int ResultWrite(IntPtr h, byte* dir)
        {
            try { Get<RunResult>(h).WriteAll(Str(dir) ?? throw new ArgumentException("directory is null")); return Ok(); }
            catch (Exception ex) { return Fail(ex); }
        }
    }
}
