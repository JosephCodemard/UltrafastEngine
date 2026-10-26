using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Physics;

namespace UltrafastEngine.Numerics
{
    /// <summary>
    /// A compute back-end: where the propagation of a nonlinear segment runs. It sits above <see cref="IFft"/>: a back-end
    /// receives a whole <see cref="IPropagationProblem"/> (linear operator, nonlinear operator, length, stepper settings)
    /// and returns the field at the end, so a GPU back-end can keep the field on the device for every step instead of
    /// copying it for each FFT. The built-in "cpu" back-end runs the managed steppers with SIMD kernels and the managed
    /// FFT. GPU back-ends (Vulkan / OpenCL / CUDA / Metal) are plugins (PluginContext.RegisterBackend); a back-end that
    /// cannot handle a problem returns false from <see cref="Supports"/> and the CPU path is used.
    /// </summary>
    public interface IComputeBackend
    {
        string Name { get; }
        string Description { get; }
        /// <summary>True if this back-end can propagate the problem (e.g. it knows the problem's nonlinear operator).</summary>
        bool Supports(IPropagationProblem problem);
        /// <summary>Propagate over the problem's full length.</summary>
        Complex[] Propagate(IPropagationProblem problem, Complex[] initial, StepperSettings settings, StepStatistics stats, Action<double> progress);
    }

    /// <summary>The managed CPU back-end (interaction-picture Runge-Kutta steppers, SIMD kernels, managed FFT).</summary>
    public sealed class CpuBackend : IComputeBackend
    {
        public string Name => "cpu";
        public string Description => "managed CPU (" + Environment.ProcessorCount + " cores, SIMD " + (ComplexKernels.Accelerated ? "on" : "off") + ", FFT " + Fft.Provider.Name + ")";
        public bool Supports(IPropagationProblem problem) => true;
        public Complex[] Propagate(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress) => Steppers.PropagateCpu(p, a0, st, stats, progress);
    }

    /// <summary>One compute device (a GPU, or the CPU) as a back-end reports it.</summary>
    public sealed class ComputeDeviceInfo
    {
        public string Backend, Name, Vendor, Driver, Notes;
        public int Index, ComputeUnits;
        public long MemoryBytes;
        public bool DoublePrecision;
        /// <summary>False when the device is listed but cannot be used (e.g. no double precision).</summary>
        public bool Usable = true;

        public Serialization.JsonObject ToJson() => new Serialization.JsonObject().Set("backend", Backend).Set("index", Index).Set("name", Name)
            .Set("vendor", Vendor ?? "").Set("driver", Driver ?? "").Set("memoryBytes", (double)MemoryBytes).Set("computeUnits", ComputeUnits)
            .Set("doublePrecision", DoublePrecision).Set("usable", Usable).Set("notes", Notes ?? "");
    }

    /// <summary>A back-end that runs on devices (GPUs): lists them, has a priority for "auto", and can test itself
    /// against the CPU path (`ultrafast devices --selftest`).</summary>
    public interface IDeviceBackend : IComputeBackend
    {
        /// <summary>Higher is preferred by the "auto" back-end (CUDA above OpenCL).</summary>
        int Priority { get; }
        /// <summary>Devices found (empty when the driver / runtime is not installed).</summary>
        IReadOnlyList<ComputeDeviceInfo> Devices { get; }
        /// <summary>Why no device is usable (driver not found, ...), or null.</summary>
        string Unavailable { get; }
        /// <summary>Compares the device with the CPU on standard problems; with <paramref name="benchmark"/> also times
        /// both. Returns {"device", "passed", "checks": [...], "benchmark": [...]}.</summary>
        Serialization.JsonObject SelfTest(int device, bool benchmark);
        /// <summary>Why this back-end would not run <paramref name="problem"/> (null when it would).</summary>
        string WhyNot(IPropagationProblem problem);
    }

    /// <summary>Per-run compute settings a back-end reads (run spec "compute").</summary>
    public sealed class ComputeSettings
    {
        public int Device = -1;
        public int GpuMinPoints;
        /// <summary>Concurrent element propagations per device (0: back-end default).</summary>
        public int DeviceSlots;
        public Serialization.JsonObject Options;
    }

    /// <summary>"auto": each nonlinear element goes to the highest-priority device back-end that supports it (and finds
    /// it large enough to be worth it), else to the CPU.</summary>
    public sealed class AutoBackend : IComputeBackend
    {
        public string Name => "auto";
        public string Description => "auto (" + string.Join(", ", Candidates().Select(b => b.Name).Concat(new[] { "cpu" })) + ")";
        public bool Supports(IPropagationProblem problem) => true;
        internal static IEnumerable<IDeviceBackend> Candidates() => ComputeBackends.Available.OfType<IDeviceBackend>().Where(b => b.Unavailable == null).OrderByDescending(b => b.Priority);
        public Complex[] Propagate(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress)
        {
            var why = new List<string>();
            foreach (var b in Candidates())
            {
                var w = b.WhyNot(p);
                if (w == null) { stats.Notes.Add("compute back-end: " + b.Name); return b.Propagate(p, a0, st, stats, progress); }
                why.Add(b.Name + ": " + w);
            }
            if (why.Count > 0) stats.Notes.Add("auto: ran on the CPU (" + string.Join("; ", why) + ")");
            return Steppers.PropagateCpu(p, a0, st, stats, progress);
        }
    }

    /// <summary>Registered back-ends and the one in use (run spec "compute": {"backend": ...}).</summary>
    public static class ComputeBackends
    {
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, IComputeBackend> All = new Dictionary<string, IComputeBackend>(StringComparer.OrdinalIgnoreCase) { { "cpu", new CpuBackend() }, { "auto", new AutoBackend() } };
        [ThreadStatic] private static IComputeBackend _current;
        [ThreadStatic] private static ComputeSettings _settings;

        /// <summary>The compute settings of the run on the calling thread (never null).</summary>
        public static ComputeSettings Settings { get => _settings ?? new ComputeSettings(); set => _settings = value; }

        /// <summary>Every device of every device back-end, plus the CPU.</summary>
        public static List<ComputeDeviceInfo> AllDevices()
        {
            var l = new List<ComputeDeviceInfo> { new ComputeDeviceInfo { Backend = "cpu", Index = 0, Name = Environment.ProcessorCount + " cores", DoublePrecision = true,
                Notes = "SIMD " + (ComplexKernels.Accelerated ? "on" : "off") + ", FFT " + Fft.Provider.Name } };
            foreach (var b in Available.OfType<IDeviceBackend>()) l.AddRange(b.Devices);
            return l;
        }

        public static void Register(IComputeBackend backend) { lock (Lock) All[backend.Name] = backend; }
        /// <summary>Every registered back-end (plugins are discovered first, so plugin back-ends are listed).</summary>
        public static IEnumerable<IComputeBackend> Available { get { Plugins.PluginManager.EnsureLoaded(); lock (Lock) return All.Values.ToList(); } }
        public static IComputeBackend Get(string name) { Plugins.PluginManager.EnsureLoaded(); lock (Lock) return name != null && All.TryGetValue(name, out var b) ? b : null; }

        private static string _default;

        /// <summary>Back-end of runs whose spec names none: set by the host (C API uf_set_default_backend, Python
        /// ultrafast.set_default_backend); initially the ULTRAFAST_BACKEND environment variable, else "cpu".</summary>
        public static string DefaultName
        {
            get { if (_default == null) { var e = Environment.GetEnvironmentVariable("ULTRAFAST_BACKEND"); _default = string.IsNullOrEmpty(e) ? "cpu" : e.Trim(); } return _default; }
            set => _default = string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>When set, every run uses this back-end whatever its spec says (CLI --backend).</summary>
        public static string OverrideName { get; set; }

        /// <summary>When >= 0, every run uses this device index (CLI --device).</summary>
        public static int OverrideDevice { get; set; } = -1;

        /// <summary>The device index a run with spec value <paramref name="specDevice"/> uses: the override, the spec, else
        /// ULTRAFAST_DEVICE, else -1 (the back-end's choice).</summary>
        public static int ResolveDevice(int specDevice)
        {
            if (OverrideDevice >= 0) return OverrideDevice;
            if (specDevice >= 0) return specDevice;
            return int.TryParse(Environment.GetEnvironmentVariable("ULTRAFAST_DEVICE"), out var d) ? d : -1;
        }

        /// <summary>The back-end name a run with spec value <paramref name="specName"/> uses.</summary>
        public static string Resolve(string specName) => OverrideName ?? specName ?? DefaultName;

        /// <summary>The back-end for the calling thread (default cpu).</summary>
        public static IComputeBackend Current { get => _current ?? Get("cpu"); set => _current = value; }
    }
}
