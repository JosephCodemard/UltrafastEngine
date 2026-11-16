using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Plugins.Gpu
{
    /// <summary>
    /// Shared logic of the "cuda" and "opencl" back-ends: device discovery (lazy, once), one context per device
    /// (kernels compiled on first use), one stream per (device, thread) so concurrent runs overlap, the decision which
    /// problems go to the device, and a fall-back to the CPU with a note if the device fails.
    /// </summary>
    public abstract class GpuBackend : IEnsembleBackend
    {
        public abstract string Name { get; }
        public abstract int Priority { get; }
        public string Description => Name + " (" + (Unavailable ?? string.Join("; ", Devices.Where(d => d.Usable).Select(d => "#" + d.Index + " " + d.Name))) + ")";

        /// <summary>Default smallest problem the "auto" back-end sends to this device (complex samples per state).</summary>
        public const int DefaultMinPoints1D = 8192, DefaultMinPointsSpatial = 32768;

        private readonly object _lock = new object();
        private bool _discovered;
        private string _unavailable;
        private List<ComputeDeviceInfo> _devices = new List<ComputeDeviceInfo>();
        private readonly Dictionary<int, GpuContext> _contexts = new Dictionary<int, GpuContext>();
        private readonly Dictionary<int, string> _failed = new Dictionary<int, string>();
        [ThreadStatic] private static Dictionary<GpuContext, GpuStream> _streams;

        /// <summary>Find the runtime and the devices; return why nothing is usable, or null.</summary>
        protected abstract string Discover(List<ComputeDeviceInfo> devices, List<string> notes);
        /// <summary>Create the context of device <paramref name="index"/> (compiles the kernels).</summary>
        protected abstract GpuContext CreateContext(int index);
        /// <summary>True when "auto" may pick this device (OpenCL CPU devices are excluded).</summary>
        protected virtual bool AutoEligible(int index) => true;
        /// <summary>True when transforms of any even length run on the device (a vendor FFT such as cuFFT was found).</summary>
        protected virtual bool HasVendorFft => false;

        public List<string> RuntimeNotes { get; } = new List<string>();

        private void EnsureDiscovered()
        {
            if (_discovered) return;
            lock (_lock)
            {
                if (_discovered) return;
                var d = new List<ComputeDeviceInfo>();
                try { _unavailable = Discover(d, RuntimeNotes); }
                catch (Exception ex) { _unavailable = Name + " discovery failed: " + ex.Message; }
                _devices = d;
                if (_unavailable == null && !d.Any(x => x.Usable)) _unavailable = "no usable " + Name + " device (double precision is required)";
                _discovered = true;
            }
        }

        public IReadOnlyList<ComputeDeviceInfo> Devices { get { EnsureDiscovered(); return _devices; } }
        public string Unavailable { get { EnsureDiscovered(); return _unavailable; } }

        /// <summary>The device a run uses: "compute.device" if set (must be usable), else the first usable (for "auto":
        /// the first eligible).</summary>
        protected int SelectDevice(bool auto)
        {
            if (Unavailable != null) return -1;
            int want = ComputeBackends.Settings.Device;
            if (want >= 0) return want < _devices.Count && _devices[want].Usable && !_failed.ContainsKey(want) ? want : -1;
            foreach (var d in _devices) if (d.Usable && !_failed.ContainsKey(d.Index) && (!auto || AutoEligible(d.Index))) return d.Index;
            return -1;
        }

        public GpuContext Context(int index)
        {
            lock (_lock)
            {
                if (_contexts.TryGetValue(index, out var c)) return c;
                if (_failed.TryGetValue(index, out var why)) throw new GpuException(why);
                try { c = CreateContext(index); }
                catch (Exception ex) { _failed[index] = Name + " device #" + index + ": " + ex.Message; throw new GpuException(_failed[index]); }
                _contexts[index] = c;
                return c;
            }
        }

        /// <summary>The calling thread's stream on a context (created on first use).</summary>
        public static GpuStream StreamFor(GpuContext c)
        {
            var d = _streams ?? (_streams = new Dictionary<GpuContext, GpuStream>());
            if (!d.TryGetValue(c, out var s)) { s = c.CreateStream(); d[c] = s; }
            return s;
        }

        private static bool IsAuto => ComputeBackends.Current is AutoBackend;

        private static int MinPoints(bool spatial)
        {
            int set = ComputeBackends.Settings.GpuMinPoints;
            if (set > 0) return set;
            if (!IsAuto) return 0;
            return spatial ? DefaultMinPointsSpatial : DefaultMinPoints1D;
        }

        /// <summary>Why this back-end would not run <paramref name="p"/> (null when it would).</summary>
        public string WhyNot(IPropagationProblem p)
        {
            if (Unavailable != null) return Unavailable;
            int dev = SelectDevice(IsAuto);
            if (dev < 0) return "no usable device selected";
            var info = _devices[dev];
            bool vendorFft = HasVendorFft;
            if (p is EnvelopeProblem ep)
            {
                var spec = ep.DeviceSpec();
                if (spec == null) return "the element uses " + (ep.DeviceUnsupportedReason ?? "a feature only the CPU implements");
                if (spec.N % 2 != 0) return "odd grid size";
                if (!vendorFft && !GpuFft.BuiltInSupports(spec.N)) return "grid size " + spec.N + " is not a power of two (built-in GPU FFT)";
                int pts = spec.N * (spec.Scalar ? 1 : 2);
                if (pts < MinPoints(false)) return "small problem (" + pts + " samples < gpuMinPoints " + MinPoints(false) + ")";
                if (info.MemoryBytes > 0 && EnvelopeStepper.MemoryNeeded(spec) > 0.8 * info.MemoryBytes) return "not enough device memory";
                return null;
            }
            if (p is SpatialEnvelopeProblem sp)
            {
                var spec = sp.DeviceSpec();
                if (spec == null) return "the spatial element uses a feature only the CPU implements (" + sp.DeviceUnsupportedReason + ")";
                if (!vendorFft && !GpuFft.BuiltInSupports(spec.N)) return "grid size " + spec.N + " is not a power of two (built-in GPU FFT)";
                if (spec.Cartesian && !vendorFft && (!GpuFft.BuiltInSupports(spec.Nx) || (spec.Ny > 1 && !GpuFft.BuiltInSupports(spec.Ny)))) return "transverse sizes must be powers of two (built-in GPU FFT)";
                long pts = (long)spec.N * spec.M * (spec.Vector ? 2 : 1);
                if (pts < MinPoints(true)) return "small problem (" + pts + " samples < gpuMinPoints " + MinPoints(true) + ")";
                if (info.MemoryBytes > 0 && SpatialStepper.MemoryNeeded(spec) > 0.8 * info.MemoryBytes) return "not enough device memory";
                return null;
            }
            if (p is Crystals.Chi2Problem cp)
            {
                var spec = cp.DeviceSpec();
                if (spec == null) return "the crystal uses " + cp.DeviceUnsupportedReason;
                if (!vendorFft && !GpuFft.BuiltInSupports(spec.N)) return "grid size " + spec.N + " is not a power of two (built-in GPU FFT)";
                int pts = spec.N * spec.K;
                if (pts < MinPoints(false)) return "small problem (" + pts + " samples < gpuMinPoints " + MinPoints(false) + ")";
                if (info.MemoryBytes > 0 && Chi2Stepper.MemoryNeeded(spec) > 0.8 * info.MemoryBytes) return "not enough device memory";
                return null;
            }
            return "this kind of element (" + p.GetType().Name + ") runs on the CPU";
        }

        public bool Supports(IPropagationProblem p) => WhyNot(p) == null;

        /// <summary>Default concurrent element propagations per device ("compute": {"deviceSlots"}).</summary>
        public const int DefaultDeviceSlots = 4;

        /// <summary>Counts the propagations running on one device (Phase D: concurrent runs share the device through
        /// separate streams; the gate keeps memory and queue depth bounded).</summary>
        private sealed class SlotGate
        {
            private int _busy;
            public int Busy { get { lock (this) return _busy; } }
            public bool Enter(int limit, bool wait)
            {
                lock (this)
                {
                    while (_busy >= limit) { if (!wait) return false; System.Threading.Monitor.Wait(this); }
                    _busy++;
                    return true;
                }
            }
            public void Exit() { lock (this) { _busy--; System.Threading.Monitor.PulseAll(this); } }
        }
        private readonly Dictionary<int, SlotGate> _gates = new Dictionary<int, SlotGate>();
        private SlotGate Gate(int dev) { lock (_gates) { if (!_gates.TryGetValue(dev, out var g)) _gates[dev] = g = new SlotGate(); return g; } }

        /// <summary>Propagations running on device <paramref name="dev"/> now.</summary>
        public int RunningOn(int dev) => Gate(dev).Busy;

        public Complex[] Propagate(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress)
        {
            // a study batch on a device: wait for the other points and run them as one ensemble (Numerics/Ensemble.cs)
            var gate = EnsembleGate.Current;
            if (gate != null)
            {
                var key = EnsembleKey(p, st);
                if (key != null) return gate.Submit(new EnsembleJob { Problem = p, Initial = a0, Settings = st, Stats = stats, Progress = progress, Key = key }, this);
            }
            return PropagateOne(p, a0, st, stats, progress);
        }

        /// <summary>Default largest ensemble (members per launch); the study key "ensemble" sets it (EnsembleGate.MaxMembers).</summary>
        public const int DefaultEnsembleMax = 256;

        public string EnsembleKey(IPropagationProblem p, StepperSettings st)
        {
            if (!(p is EnvelopeProblem ep) || WhyNot(p) != null) return null;
            if (string.Equals(st.Method, "rk4ip", StringComparison.OrdinalIgnoreCase) || string.Equals(st.Method, "fixed", StringComparison.OrdinalIgnoreCase)) return null;
            var s = ep.DeviceSpec();
            if (s == null || s.Responses != null) return null;
            bool raman = s.RamanKernelFreq != null && s.RamanFraction > 0 && s.N2 != 0;
            var inv = CultureInfo.InvariantCulture;
            return string.Join("|", Name, SelectDevice(IsAuto).ToString(inv), s.N.ToString(inv), s.Scalar ? "s" : "v", raman ? "R" + Hash(s.RamanKernelFreq) + Hash(s.RamanAnisoFreq) : "-",
                s.RotationalKernelFreq != null ? "Q" + Hash(s.RotationalKernelFreq) : "-", s.SelfSteepening ? "S" + Hash(s.OmegaOffset) + s.OmegaCenter.ToString("R", inv) : "-",
                s.DTime.ToString("R", inv), st.Tolerance.ToString("R", inv), st.MaxNonlinearPhasePerStepRad.ToString("R", inv), st.MaxStepM.ToString("R", inv),
                st.MinStepM.ToString("R", inv), st.MaxSteps.ToString(inv), st.MaxAreaChangePerStep.ToString("R", inv));
        }

        private static string Hash(Complex[] a) { if (a == null) return "0"; ulong h = 14695981039346656037UL; foreach (var c in a) { h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(c.Real)) * 1099511628211UL; h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(c.Imaginary)) * 1099511628211UL; } return h.ToString("x16"); }
        private static string Hash(double[] a) { if (a == null) return "0"; ulong h = 14695981039346656037UL; foreach (var c in a) h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(c)) * 1099511628211UL; return h.ToString("x16"); }

        public void PropagateEnsemble(IList<EnsembleJob> jobs)
        {
            int max = EnsembleGate.Current?.MaxMembers > 0 ? EnsembleGate.Current.MaxMembers : DefaultEnsembleMax;
            if (max < 1) max = 1;
            for (int start = 0; start < jobs.Count; start += max)
            {
                var part = new List<EnsembleJob>();
                for (int i = start; i < Math.Min(jobs.Count, start + max); i++) part.Add(jobs[i]);
                RunEnsemble(part);
            }
        }

        private void RunEnsemble(List<EnsembleJob> jobs)
        {
            void Alone(EnsembleJob j, string note) { if (note != null) j.Stats.Notes.Add(note); j.Result = PropagateOne(j.Problem, j.Initial, j.Settings, j.Stats, j.Progress); }
            if (jobs.Count == 1) { Alone(jobs[0], null); return; }
            int dev = SelectDevice(IsAuto);
            var specs = jobs.Select(j => ((EnvelopeProblem)j.Problem).DeviceSpec()).ToList();
            var info = _devices[dev];
            if (info.MemoryBytes > 0 && EnsembleStepper.MemoryNeeded(specs[0], jobs.Count) > 0.8 * info.MemoryBytes && jobs.Count > 1)
            {   // split in halves until it fits
                int h = jobs.Count / 2;
                RunEnsemble(jobs.GetRange(0, h)); RunEnsemble(jobs.GetRange(h, jobs.Count - h));
                return;
            }
            int slots = ComputeBackends.Settings.DeviceSlots > 0 ? ComputeBackends.Settings.DeviceSlots : DefaultDeviceSlots;
            var gate = Gate(dev);
            gate.Enter(slots, true);
            try
            {
                var ctx = Context(dev);
                var s = StreamFor(ctx);
                string note = "device: " + Name + " #" + dev + " " + ctx.Info.Name + (ctx.VendorFftName != null ? " (" + ctx.VendorFftName + ")" : " (built-in FFT)") + "; ensemble of " + jobs.Count + " study points in one launch";
                foreach (var j in jobs) j.Stats.Notes.Add(note);
                using (var stp = new EnsembleStepper(s, specs))
                {
                    var outs = stp.Propagate(jobs.Select(j => j.Initial).ToList(), jobs.Select(j => j.Settings).ToList(), jobs.Select(j => j.Stats).ToList());
                    for (int i = 0; i < jobs.Count; i++) { jobs[i].Result = outs[i]; jobs[i].Progress?.Invoke(1.0); }
                }
            }
            catch (GpuException ex)
            {
                foreach (var j in jobs)
                {
                    j.Stats.Notes.Add(Name + " ensemble failed (" + ex.Message.Split('\n')[0] + "); ran on the CPU");
                    j.Stats.Accepted = 0; j.Stats.Rejected = 0; j.Stats.BIntegralRad = 0; j.Stats.MinStepM = double.PositiveInfinity; j.Stats.MaxStepM = 0;
                    j.Result = Steppers.PropagateCpu(j.Problem, j.Initial, j.Settings, j.Stats, j.Progress);
                }
            }
            finally { gate.Exit(); }
        }

        private Complex[] PropagateOne(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress)
        {
            var why = WhyNot(p);
            if (why != null) { stats.Notes.Add(Name + ": " + why + "; ran on the CPU"); return Steppers.PropagateCpu(p, a0, st, stats, progress); }
            int dev = SelectDevice(IsAuto);
            int slots = ComputeBackends.Settings.DeviceSlots > 0 ? ComputeBackends.Settings.DeviceSlots : DefaultDeviceSlots;
            var gate = Gate(dev);
            if (!gate.Enter(slots, !IsAuto)) { stats.Notes.Add(Name + ": all " + slots + " device slots busy; ran on the CPU"); return Steppers.PropagateCpu(p, a0, st, stats, progress); }
            try
            {
                var ctx = Context(dev);
                var s = StreamFor(ctx);
                stats.Notes.Add("device: " + Name + " #" + dev + " " + ctx.Info.Name + (ctx.VendorFftName != null ? " (" + ctx.VendorFftName + ")" : " (built-in FFT)"));
                if (p is EnvelopeProblem ep)
                    using (var stp = new EnvelopeStepper(s, ep.DeviceSpec())) return stp.Propagate(a0, st, stats, progress);
                if (p is Crystals.Chi2Problem cp)
                    using (var stp = new Chi2Stepper(s, cp.DeviceSpec())) return stp.Propagate(a0, st, stats, progress);
                var sp = (SpatialEnvelopeProblem)p;
                using (var stp = new SpatialStepper(s, sp.DeviceSpec())) return stp.Propagate(a0, st, stats, progress);
            }
            catch (GpuException ex)
            {
                stats.Notes.Add(Name + " failed (" + ex.Message.Split('\n')[0] + "); ran on the CPU");
                stats.Accepted = 0; stats.Rejected = 0; stats.BIntegralRad = 0; stats.MinStepM = double.PositiveInfinity; stats.MaxStepM = 0;
                return Steppers.PropagateCpu(p, a0, st, stats, progress);
            }
            finally { gate.Exit(); }
        }

        public JsonObject SelfTest(int device, bool benchmark) => GpuSelfTest.Run(this, device, benchmark);
    }

    /// <summary>"cuda": NVIDIA GPUs through the driver API + NVRTC (+ cuFFT / cuBLAS).</summary>
    public sealed class CudaBackend : GpuBackend
    {
        private CudaApi _api;
        public override string Name => "cuda";
        public override int Priority => 20;

        protected override string Discover(List<ComputeDeviceInfo> devices, List<string> notes)
        {
            _api = CudaApi.TryLoad(out var why, out var n);
            notes.AddRange(n);
            if (_api == null) return why;
            unsafe
            {
                int count = 0, ver = 0;
                _api.DriverGetVersion(&ver);
                int e = _api.DeviceGetCount(&count);
                if (e != 0) return "cuDeviceGetCount failed: " + _api.Error(e);
                for (int i = 0; i < count; i++)
                {
                    int dev; _api.DeviceGet(&dev, i);
                    nuint mem = 0; _api.DeviceTotalMem(&mem, dev);
                    int major = _api.Attribute(dev, CudaApi.ATTR_CC_MAJOR), minor = _api.Attribute(dev, CudaApi.ATTR_CC_MINOR);
                    devices.Add(new ComputeDeviceInfo
                    {
                        Backend = "cuda", Index = i, Name = _api.DeviceName(dev), Vendor = "NVIDIA", Driver = "CUDA driver " + ver / 1000 + "." + ver % 1000 / 10,
                        ComputeUnits = _api.Attribute(dev, CudaApi.ATTR_MULTIPROCESSOR_COUNT), MemoryBytes = (long)mem, DoublePrecision = true, Usable = major >= 5,
                        Notes = "compute capability " + major + "." + minor + "; " + string.Join(", ", notes) + (major >= 5 ? "" : "; compute capability below 5.0 is not supported")
                    });
                }
                if (count == 0) return "the NVIDIA driver reports no CUDA device";
            }
            return null;
        }

        protected override GpuContext CreateContext(int index) => new CudaContext(_api, index, Devices[index]);
        protected override bool HasVendorFft => _api != null && _api.Cufft != null;
    }

    /// <summary>"opencl": any OpenCL 1.2 device with double precision (NVIDIA, AMD, Intel GPUs; CPU runtimes such as
    /// PoCL for testing).</summary>
    public sealed class OpenClBackend : GpuBackend
    {
        private OpenClApi _api;
        private List<OpenClDeviceRef> _refs = new List<OpenClDeviceRef>();
        public override string Name => "opencl";
        public override int Priority => 10;

        protected override string Discover(List<ComputeDeviceInfo> devices, List<string> notes)
        {
            _api = OpenClApi.TryLoad(out var why);
            if (_api == null) return why;
            notes.Add("OpenCL " + System.IO.Path.GetFileName(_api.Lib.Path));
            _refs = OpenClDevices.List(_api, out why);
            devices.AddRange(_refs.Select(r => r.Info));
            return _refs.Count == 0 ? why : null;
        }

        protected override bool AutoEligible(int index) => !_refs[index].IsCpu || Environment.GetEnvironmentVariable("ULTRAFAST_OPENCL_ALLOW_CPU") == "1";
        protected override GpuContext CreateContext(int index) => new OpenClContext(_api, _refs[index]);
    }
}
