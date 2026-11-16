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

Measured on one 2-core x64 container (the supercontinuum example, N = 8192, 3109 adaptive steps): 23.4 s before
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
[Parameter studies](studies.md). With a GPU back-end the points of a batch run as one device ensemble ([below](#device-ensembles-for-studies)); `"parallel"` then only matters for elements that cannot join it.

## Parameter studies against a loop

`tools/bench` (`dotnet run -c Release --project tools/bench -- scan-mpc loop-mpc`), Apple M-series, .NET 10, Accelerate FFT. "Loop" is the same
points as independent `Simulation.Run` calls; "study" is `Studies.Run` (frozen grid, instruments skipped when unused, prefix cache).

| case | points | loop | study | gain |
|---|---|---|---|---|
| `mpc-argon`, length of the last gap | 20 | 6.5 s | 1.1 s | x5.7 (outputs equal to 1e-11 relative) |
| `spm-plate`, plate length | 200 | 0.78 s | 0.44 s | x1.8 |
| `spm-plate`, input GDD (acts on the pulse: no prefix) | 200 | 0.8 to 1.1 s | 0.7 s | x1.2 to 1.5 |

The gain grows with the cost of the elements before the changed one and of the instruments. When a single run takes 3 ms, nothing is gained
by any machinery: the 200 runs already finish in less than a second. A separate *linear fast path* (one transfer function, no integration)
and an *ensemble dimension on the CPU* were therefore **not built**: a linear element already costs one FFT pair, and
the CPU has no launch latency to hide. On the GPU the ensemble was built (WP-C(B)): [Device ensembles for studies](#device-ensembles-for-studies).

Allocation: the 200-point study allocates 0.8 GB (the loop 2.2 GB); a single 1-D run on 4096 points allocates 11 MB, a radial spatial run 1 GB
(`spatial-kerr`). Pooling the spatial buffers is the remaining CPU item; it is not needed for any measured case to be slow.

## Device ensembles for studies

With a GPU back-end the points of a study batch run as one ensemble ([Parameter studies](studies.md)): every 1-D element of every point is
integrated in the same kernel launches (each point with its own steps), so the launch latency is paid once per step for all points instead of
once per point. RTX 3060 Ti (FP64 at 1/64 of FP32), .NET 10, 20-core CPU, 200-point scan of a Kerr + Raman plate (the self-test problem), whole
study wall time (compile, nominal run, all points):

| case | CPU, `parallel` 4 | CPU, `parallel` 20 | GPU, a stream per point (`ensemble: 0`, `parallel` 4) | GPU ensemble, CUDA / OpenCL |
|---|---|---|---|---|
| scalar, N = 4096 | 4.4 s | 1.7 s | 13.4 s | 2.2 s / 1.9 s |
| vector, N = 4096 | 5.9 s | 2.4 s | 8.0 s | 2.3 s / 2.5 s |
| scalar, N = 16384 | 18.1 s | 11.3 s | 13.8 s | 7.2 s / 9.2 s |

The ensemble is 3.5 to 6 times faster than separate streams. On this consumer card it matches a fully loaded 20-core CPU at N = 4096 and is 1.6
times faster at N = 16384; a card with full-rate FP64 (A100, H100) should gain much more, but that was not measured.

## FP32 study (mixed precision)

The `cpu-fp32` back-end (study only: `"compute": {"backend": "cpu-fp32"}`) rounds the input and the result of every nonlinear-operator
evaluation, and the linear rate, to single precision; FFTs and the stepper arithmetic stay double, so it is a *lower bound* of the error of
a real single-precision kernel. Acceptance: energy 1e-6, spectrum L1 1e-4, contrast floor unchanged.

| case | energy | spectrum L1 | max abs(dI) / peak | floor (10th percentile of I / peak) |
|---|---|---|---|---|
| `spm-plate` | 3e-10 | 7e-10 | 3e-9 | 7e-33 |
| `mpc-argon` | 6e-10 | 1e-9 | 1e-8 | 7e-10 |
| `spatial-kerr` | 1e-10 | 4e-10 | 2e-9 | 4e-17 |

Result: the operator in FP32 meets energy and spectrum criteria by three to four orders of magnitude. The **floor criterion fails** wherever the
intensity floor is below about 1e-8 of the peak (the SPM plate and spatial cases): the rounding noise (about -85 dB) fills it. So mixed
precision (consumer GPUs) is acceptable for energy, spectrum and pulse shape, and must be **refused when a contrast or pedestal instrument asks
for more than about 70 dB of dynamic range**. Not yet checked: single-precision FFTs (the accumulated error over thousands of steps is expected
to be about 1e-6 to 1e-5), the soliton and SHG cases; both need the GPU session ([GPU checklist](../design/gpu-windows-checklist.md)).

## Practical advice

* Let the grid planner size the grid; a 4096-point 1-D run with a few nonlinear elements takes milliseconds to a
  second. Spatial models multiply the cost by the number of transverse points (radial 64-160, cartesian 64² ... 256²).
* For many runs (scans, fits) prefer studies over a Python loop: they reuse the grid and the prefix, skip instruments no output reads, and
  can store points in a cache folder. To explore cheaply, add [`"fidelity": "quick"`](fidelity.md) and confirm the best points in full.
* `"stepper": {"tolerance": 1e-5}` is usually enough; the default 1e-6 is conservative.
* Install FFTW on Windows and Linux (on macOS Accelerate is already there): it is the cheapest speed-up.
* `"compute": {"backend": "auto"}` is safe to leave on: without a GPU, or for small elements, it runs on the CPU.

## Measured limits, and when to revisit them

Two speed-ups were considered and not built (owner decision, 2026-11-10). Each has the measurement that decided it and the condition that reopens it.

* **A cache for the compiled system.** In a loop of many trials (sensorless optimisation, a fit) a trial on a 128 x 128 grid costs 10 ms *including* the compile, so a cache would save very little. **Revisit if** a loop over a much larger system shows the compile taking more than 30 % of the time of a trial.
* **A fully streaming executor** (never holding all transverse frequency slices in memory). The peak memory of a large pulse run is set by the .NET garbage collector, not by the propagator: 2.7 to 3.4 GB at 1920 x 1080 with 16 stored frequencies, whatever the block size. **Revisit if** a 4096 x 4096 pulse run is needed, and measure it first: only the 1920 x 1080 case has been measured. A cw run at 4096 x 4096 needs 3.4 GB and 20 s today.

Both are recorded with their numbers in [the S2 spikes](../design/history.md).
