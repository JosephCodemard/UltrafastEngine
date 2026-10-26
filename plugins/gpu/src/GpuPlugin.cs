using System;
using UltrafastEngine.Schema;

namespace UltrafastEngine.Plugins.Gpu
{
    /// <summary>
    /// The GPU plugin (plugins/gpu): compute back-ends "cuda" and "opencl". Select one with
    /// <c>"compute": {"backend": "cuda"}</c>, or <c>"auto"</c> to let each nonlinear element go to the fastest device that
    /// supports it (else the CPU). Supported on the device: 1-D envelope propagation (scalar / vector Kerr, Raman,
    /// self-steepening) and radial / cartesian spatial propagation (the same physics, envelope or UPPE coupling).
    /// Everything else runs on the CPU, with a note. Docs: docs/plugins/gpu.md.
    /// </summary>
    public sealed class GpuPlugin : IPlugin
    {
        public const string PluginVersion = "2.1.8-beta";
        public string Id => "gpu";
        public string Name => "GPU: CUDA and OpenCL compute back-ends";
        public string Version => PluginVersion;
        public string Description => "Runs nonlinear propagation (1-D envelope and radial / cartesian spatial) on NVIDIA GPUs through CUDA and on any double-precision OpenCL device.";

        /// <summary>The two back-ends (one instance each per process).</summary>
        public static readonly CudaBackend Cuda = new CudaBackend();
        public static readonly OpenClBackend OpenCl = new OpenClBackend();

        public void Register(PluginContext ctx)
        {
            ctx.RegisterBackend(Cuda);
            ctx.RegisterBackend(OpenCl);
        }

        /// <summary>Registers the plugin in-process (hosts that cannot load plugin folders, e.g. the NativeAOT library).</summary>
        public static void RegisterStatic()
        {
            try { PluginManager.Register(new GpuPlugin(), "built-in"); } catch { }
        }
    }
}
