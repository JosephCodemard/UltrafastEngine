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

    /// <summary>Registered back-ends and the one in use (run spec "compute": {"backend": ...}).</summary>
    public static class ComputeBackends
    {
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, IComputeBackend> All = new Dictionary<string, IComputeBackend>(StringComparer.OrdinalIgnoreCase) { { "cpu", new CpuBackend() } };
        [ThreadStatic] private static IComputeBackend _current;

        public static void Register(IComputeBackend backend) { lock (Lock) All[backend.Name] = backend; }
        public static IEnumerable<IComputeBackend> Available { get { lock (Lock) return All.Values.ToList(); } }
        public static IComputeBackend Get(string name) { lock (Lock) return name != null && All.TryGetValue(name, out var b) ? b : null; }

        /// <summary>The back-end for the calling thread (default cpu).</summary>
        public static IComputeBackend Current { get => _current ?? Get("cpu"); set => _current = value; }
    }
}
