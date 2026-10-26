# gpu plugin

Runs nonlinear propagation on graphics cards. Back-ends:

* **`cuda`**: NVIDIA GPUs through the driver API; kernels compiled at run time by NVRTC; cuFFT and cuBLAS when found.
* **`opencl`**: any OpenCL 1.2 device with double precision (NVIDIA, AMD, Intel GPUs).

Select with `"compute": {"backend": "auto"}` (GPU for elements it supports and that are large enough, CPU otherwise),
`"cuda"` or `"opencl"`. Supported on the device: 1-D envelope propagation (scalar / vector Kerr, scalar / vector
Raman, self-steepening, z-dependent beam area) and radial / cartesian spatial propagation with the same physics
(envelope or UPPE coupling). Everything else runs on the CPU with a note. Results match the CPU to rounding.

```bash
ultrafast devices --selftest        # what was found, and a comparison with the CPU
ultrafast devices --benchmark       # timings and speed-ups on this machine
```

Python: `uf.devices()`, `uf.benchmark()`, `uf.set_default_backend("auto")`, `b.compute("auto")`.
C: `uf_devices_json`, `uf_set_default_backend` (compiled into the native library).

Nothing is linked at build time and there are no NuGet packages: vendor libraries are loaded when first needed, so
the plugin loads on every machine (without a GPU the back-ends report why they are unavailable). macOS: Apple GPUs
have no double precision; the CPU uses Accelerate instead.

Docs: [docs/plugins/gpu.md](../../docs/plugins/gpu.md) (setup per OS, troubleshooting, internals),
[docs/reference/speed.md](../../docs/reference/speed.md) (what gains from a GPU).
