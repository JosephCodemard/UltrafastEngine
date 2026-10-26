# Speed and compute back-ends

## What runs where

**FFT (CPU)**: one process-wide provider, chosen once (`"compute": {"fft": ...}`, the `ULTRAFAST_FFT` environment
variable, or `uf.set_default_backend(fft=...)`):

| provider | where | notes |
|---|---|---|
| `accelerate` | macOS (Intel and Apple silicon) | Apple's vDSP, part of the OS; lengths f·2^k (f = 1, 3, 5, 15), others go to the managed FFT |
| `fftw` | Windows, Linux, macOS when installed | FFTW 3 (`apt install libfftw3-3`, `brew install fftw`, `libfftw3-3.dll` on the PATH); any length. Intel MKL works through its FFTW interface: `ULTRAFAST_FFTW_LIBRARY=mkl_rt` |
| `managed` | everywhere, .NET Framework 4.8 too | radix-4 Stockham for powers of two (no bit reversal, exact twiddle table), mixed-radix (factors up to 13) and Bluestein for other lengths |
| `auto` (default) | | Accelerate on macOS, else FFTW when found, else managed |

A native provider is used only after it reproduced the managed FFT on test vectors (relative error below
1e-12); otherwise the run falls back to `managed` and says why. Results do not depend on the provider beyond
rounding (1e-12 relative). `ultrafast devices` shows the provider in use.

* **Element-wise kernels** (operator products, axpy, exponentials) use 128-bit SIMD on .NET 10: SSE2 on x64, NEON on
  ARM64 (Apple silicon). .NET Framework 4.8 runs the scalar code. `ComplexKernels.Accelerated` reports it;
  `ComplexKernels.ForceScalar = true` switches it off.
* **The 1-D nonlinear operator** (Kerr, Raman, self-steepening, vector coupling) works in place on reused arrays:
  no allocation per evaluation.
* **Nonlinear steps** run on the selected compute back-end: the CPU, or a GPU through the
  [gpu plugin](../plugins/gpu.md).

Measured on one 2-core x64 container (the 25-supercontinuum example, N = 8192, 3109 adaptive steps): 23.4 s before
these changes, 8.8 s with the managed Stockham FFT and the allocation-free operator, 5.7 s with FFTW. The output is
the same to 1e-12.

## Compute back-ends

```jsonc
"compute": {
  "backend": "auto",      // cpu (default) | auto | cuda | opencl
  "threads": 8,           // CPU worker threads, 0 = all cores
  "fft": "auto",          // CPU FFT provider (above)
  "device": -1,           // GPU index from `ultrafast devices` (-1: first suitable)
  "gpuMinPoints": 0,      // auto: smallest problem sent to a GPU (0: 8192 samples 1-D, 32768 spatial)
  "deviceSlots": 4        // GPU: element propagations running on one device at the same time
}
```

| back-end | what it does |
|---|---|
| `cpu` | the interaction-picture Runge-Kutta steppers on the CPU ([Solvers](nonlinear.md)) |
| `auto` | each nonlinear element goes to the highest-priority device back-end that supports it and finds it large enough (CUDA before OpenCL), else to the CPU; the element's notes say which, and why not |
| `cuda`, `opencl` | the [gpu plugin](../plugins/gpu.md): NVIDIA GPUs (CUDA), any double-precision OpenCL device. Elements the device cannot run stay on the CPU with a note |

The process default (for run specs without `compute.backend`) is `cpu`; change it with the `ULTRAFAST_BACKEND`
environment variable, `ultrafast run --backend auto`, `uf.set_default_backend("auto")` or
`uf_set_default_backend("auto")`.

```bash
ultrafast devices                  # back-ends, devices, CPU FFT in use
ultrafast devices --selftest       # every device against the CPU on standard problems
ultrafast devices --benchmark      # ... and timings: seconds per run, speed-up
ultrafast run system.ufs run.json --backend auto --device 0
```

A back-end (`IComputeBackend`) receives a whole nonlinear segment -- linear operator, nonlinear operator, length and
stepper settings -- and returns the field at its end, so a GPU back-end keeps the field on the device for all steps.
Write your own as a plugin:

```csharp
public sealed class MyBackend : IComputeBackend
{
    public string Name => "mybackend";
    public string Description => "...";
    public bool Supports(IPropagationProblem p) => p is EnvelopeProblem ep && ep.DeviceSpec() != null;
    public Complex[] Propagate(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress) { ... }
}
// in the plugin: ctx.RegisterBackend(new MyBackend());   IDeviceBackend adds devices, priority and a self-test
```

`EnvelopeProblem.DeviceSpec()` and `SpatialEnvelopeProblem.DeviceSpec()` export everything a device needs (linear
operator, Raman kernels, coefficients, transverse transform); they return null when the element uses a response only
the CPU implements. An unknown back-end name falls back to the CPU with a warning.

## What gains from a GPU

The device runs the whole adaptive integration: every stage, FFT and the step control stay on the GPU, and the host
reads back a few hundred numbers per step. Per step that is 5 evaluations of the nonlinear operator, each 2 to 4
batched FFTs plus 3 to 5 element-wise kernels, so each step costs about 50 to 150 kernel launches.

| problem | samples per state | expect |
|---|---|---|
| 1-D, N ≤ 4096 | ≤ 8192 | CPU faster: launch latency (a few µs per kernel) dominates; `auto` keeps these on the CPU |
| 1-D, N = 2^14 ... 2^17 (long fibres, wide windows), vector + Raman | 16k-260k | GPU gains grow with N: the CPU cost grows as N log N per FFT, the GPU stays near its launch floor |
| spatial radial (64-160 points × 1024-8192 samples) | 10^5-10^6 | GPU: the Hankel transform is one dense matrix product over all frequencies (cuBLAS DGEMM on CUDA) and the per-point FFTs are one batched transform |
| spatial cartesian (64² ... 256² × 512-4096) | 10^6-10^8 | GPU: the largest gains -- every transform is a batched FFT; device memory sets the limit (`ultrafast devices` shows it) |
| parameter studies, fits, coherence trials | | each parallel run gets its own CUDA stream / OpenCL queue, so `"parallel": n` runs overlap on one device (`deviceSlots`) |

The speed-up depends on the GPU's double-precision throughput: data-centre GPUs (A100, H100, ...) run double
precision at 1/2 of single precision, consumer GPUs (GeForce) at 1/32 to 1/64. The engine computes in double
precision throughout (results agree with the CPU to rounding), so on a GeForce card the gain comes mainly from
memory bandwidth. Measure on your machine: `ultrafast devices --benchmark` times the CPU and each device on growing
1-D and spatial problems and reports the speed-up. Apple GPUs have no double precision; on macOS the CPU uses
Accelerate instead.

## Studies

Parameter studies freeze the grid, cache the unchanged prefix of the system and run points in parallel batches:
[Parameter studies](studies.md). With a GPU back-end, set `"parallel"` to 2-4 so the device always has work queued.

## Practical advice

* Let the grid planner size the grid; a 4096-point 1-D run with a few nonlinear elements takes milliseconds to a
  second. Spatial models multiply the cost by the number of transverse points (radial 64-160, cartesian 64² ... 256²).
* For many runs (scans, fits) prefer studies over a Python loop: they reuse the grid and the prefix.
* `"stepper": {"tolerance": 1e-5}` is usually enough; the default 1e-6 is conservative.
* Install FFTW on Windows and Linux (on macOS Accelerate is already there): it is the cheapest speed-up.
* `"compute": {"backend": "auto"}` is safe to leave on: without a GPU, or for small elements, it runs on the CPU.
