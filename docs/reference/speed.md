# Speed and compute back-ends

How the engine spends its time: the CPU FFT provider, the element-wise kernels, the compute back-ends that integrate a
nonlinear element (the CPU, or a GPU through the [gpu plugin](../plugins/gpu.md)), and what studies and device
ensembles gain over a loop. Every timing on this page names the machine, the date and the script or test that made it.

## Keys

```jsonc
"compute": {
  "backend": "auto",      // cpu (default) | auto | cuda | opencl | cpu-fp32
  "threads": 8,           // CPU worker threads, 0 = all cores
  "fft": "auto",          // CPU FFT provider: auto | accelerate | fftw | managed (process-wide)
  "device": -1,           // GPU index from `ultrafast devices` (-1: first suitable)
  "gpuMinPoints": 0,      // smallest problem (complex samples per state) sent to a GPU; 0 = the back-end's default
  "deviceSlots": 4,       // GPU: element propagations running on one device at the same time
  "precision": "double",  // double | mixed (GPU back-ends: the nonlinear operator in single precision)
  "memoryLimitBytes": 0   // refuse a run whose memory estimate is above this (docs/reference/memory.md)
}
```

* `gpuMinPoints` 0 means 8192 samples (1-D) and 32768 (spatial) with `backend: "auto"`, and **no minimum** with an
  explicit `cuda` or `opencl`; a positive value applies to both.
* The process default of `backend` (for run specs without it) is `cpu`; change it with the `ULTRAFAST_BACKEND`
  environment variable, `ultrafast run --backend auto`, `uf.set_default_backend("auto")` or `uf_set_default_backend("auto")`.
* The FFT provider is chosen once per process: `compute.fft`, the `ULTRAFAST_FFT` environment variable or
  `uf.set_default_backend(fft=...)`.
* Study key `ensemble` (default on with a GPU back-end): the points of a batch run as one device ensemble
  ([Parameter studies](studies.md)).

Defaults and types: [options](options.md#compute).

```bash
ultrafast devices                  # back-ends, devices, CPU FFT in use
ultrafast devices --selftest       # every usable device against the CPU on standard problems
ultrafast devices --benchmark      # ... and timings: seconds per run, speed-up
ultrafast run system.ufs run.json --backend auto --device 0
```

## Model

### What runs where
<span id="compute-back-ends"></span>

**FFT (CPU).** One process-wide provider:

| provider | where | notes |
|---|---|---|
| `accelerate` | macOS (Intel and Apple silicon), .NET 10 build | Apple's vDSP, part of the OS; lengths f·2^k (f = 1, 3, 5, 15), other lengths go to the managed FFT |
| `fftw` | Windows, Linux, macOS when installed, .NET 10 build | FFTW 3 (`apt install libfftw3-3`, `brew install fftw`, `libfftw3-3.dll` on the PATH); any length. Intel MKL works through its FFTW interface: `ULTRAFAST_FFTW_LIBRARY=mkl_rt` |
| `managed` | everywhere; the only provider of the .NET Framework 4.8 build | radix-4 Stockham for powers of two (no bit reversal, exact twiddle table), mixed radix (factors up to 13) and Bluestein for other lengths |
| `auto` (default) | | Accelerate on macOS, else FFTW when found, else managed |

A native provider is used only after it reproduced the managed FFT on test vectors (relative error below 1e-12);
otherwise the run falls back to `managed` and says why (on .NET Framework 4.8: "native FFT providers need .NET 10").
Results do not depend on the provider beyond rounding (1e-12 relative). `ultrafast devices` shows the provider in use.

* **Element-wise kernels** (operator products, axpy, exponentials) use 128-bit SIMD in the .NET 10 build: SSE2 on x64,
  NEON on ARM64 (Apple silicon). The .NET Framework 4.8 build runs the scalar code. `ComplexKernels.Accelerated` reports
  it; `ComplexKernels.ForceScalar = true` switches it off.
* **The 1-D nonlinear operator** (Kerr, Raman, self-steepening, vector coupling) works in place on reused arrays: no
  allocation per evaluation.
* **Nonlinear elements** run on the selected compute back-end.

| back-end | what it does |
|---|---|
| `cpu` | the interaction-picture Runge-Kutta steppers on the CPU ([Steppers](nonlinear.md#steppers)) |
| `auto` | each nonlinear element goes to the highest-priority device back-end that supports it and finds it large enough (CUDA before OpenCL), else to the CPU; the element's notes say which, and why not. `auto` never picks an OpenCL device that is a CPU (PoCL, Intel's CPU runtime) unless `ULTRAFAST_OPENCL_ALLOW_CPU=1` |
| `cuda`, `opencl` | the [gpu plugin](../plugins/gpu.md): NVIDIA GPUs (CUDA), any OpenCL device with double precision. Elements the device cannot run stay on the CPU with a note |
| `cpu-fp32` | a research back-end: the CPU steppers with the nonlinear operator's input and result, and the linear rate, rounded to single precision ([below](#fp32-study-mixed-precision)) |

An unknown back-end name falls back to the CPU with a warning. Device discovery: on Windows and Linux the CUDA driver and
the OpenCL ICD loader; on macOS the `opencl` back-end loads Apple's `OpenCL.framework` and every vendor library named by an
`.icd` file (for example Homebrew PoCL), and lists all their devices. Apple GPUs have no double precision and are listed
as not usable; a PoCL CPU device has double precision and is usable with an explicit `opencl` (it is not faster than the
CPU back-end; it exists to check the device kernels on a Mac). Setting up: [GPU](../plugins/gpu.md#setting-up).

A back-end (`IComputeBackend`) receives a whole nonlinear segment (linear operator, nonlinear operator, length and stepper
settings) and returns the field at its end, so a GPU back-end keeps the field on the device for all steps. Write your own
as a plugin:

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
operator, Raman kernels, coefficients, transverse transform); they return null when the element uses a response only the
CPU implements.

### What gains from a GPU

The device runs the whole adaptive integration: every stage, FFT and the step control stay on the GPU, and the host reads
back a few hundred numbers per step. The adaptive stepper (embedded ERK4(3)-IP with first-same-as-last) evaluates the
nonlinear operator four times per accepted step (five stages, the last reused as the next step's first); each evaluation
is 2 to 4 batched FFTs plus 3 to 5 element-wise kernels, so a step costs about 50 to 150 kernel launches.

| problem | samples per state | expect |
|---|---|---|
| 1-D, N ≤ 4096 | ≤ 8192 | CPU faster: launch latency (a few µs per kernel) dominates; `auto` keeps these on the CPU |
| 1-D, N = 2^14 ... 2^17 (long fibres, wide windows), vector + Raman | 16k-260k | GPU gains grow with N: the CPU cost grows as N log N per FFT, the GPU stays near its launch floor |
| spatial radial (64-160 points × 1024-8192 samples) | 10^5-10^6 | GPU: the Hankel transform is one dense matrix product over all frequencies (cuBLAS DGEMM on CUDA) and the per-point FFTs are one batched transform |
| spatial cartesian (64² ... 256² × 512-4096) | 10^6-10^8 | GPU: the largest gains, every transform is a batched FFT; device memory sets the limit (`ultrafast devices` shows it) |
| parameter studies, fits, coherence trials | | each parallel run gets its own CUDA stream / OpenCL queue, so `"parallel": n` runs overlap on one device (`deviceSlots`) |

The speed-up depends on the GPU's double-precision throughput: data-centre GPUs (A100, H100, ...) run double precision at
1/2 of single precision, consumer GPUs (GeForce) at 1/32 to 1/64. The engine computes in double precision unless
`precision` is `mixed` (results agree with the CPU to rounding), so on a GeForce card the gain comes mainly from memory
bandwidth. `ultrafast devices --benchmark` times the CPU and each usable device on growing 1-D and spatial problems and
reports the speed-up; it times only devices that pass the self-test (on a Mac: none, or a PoCL CPU device if installed).

### Parameter studies against a loop

Parameter studies freeze the grid, cache the unchanged prefix of the system, skip instruments no output reads and run
points in parallel batches ([Parameter studies](studies.md)). With a GPU back-end the points of a batch run as one device
ensemble ([below](#device-ensembles-for-studies)); `"parallel"` then only matters for elements that cannot join it.

`tools/bench` (`dotnet run -c Release --project tools/bench -- scan-mpc loop-mpc scan-length loop-length scan-gdd loop-gdd`),
run 2026-11-26 on an Apple M3 (8 cores, 16 GB), macOS, .NET 10, Accelerate FFT, default threads. "Loop" is the same points
as independent `Simulation.Run` calls; "study" is `Studies.Run`. The bench also prints the bytes allocated.

| case | points | loop | study | gain | allocated (loop / study) |
|---|---|---|---|---|---|
| `mpc-argon`, length of the last gap | 20 | 6.4 s | 1.2 s | ×5.3 (outputs equal to 1e-11 relative) | 2.5 GB / 1.9 GB |
| `spm-plate`, plate length | 200 | 0.94 s | 0.65 s | ×1.4 | 2.3 GB / 0.79 GB |
| `spm-plate`, input GDD (acts on the pulse: no prefix) | 200 | 0.88 s | 0.45 s | ×2.0 | 2.3 GB / 0.85 GB |

The gain grows with the cost of the elements before the changed one and of the instruments. When a single run takes a few
ms, the 200 runs already finish in less than a second. On the same machine and date one 1-D `spm-plate` run (N = 4096)
allocates 15 MB and one radial `spatial-kerr` run 1.0 GB (bench cases `spm-plate`, `spatial-kerr`).

### Device ensembles for studies

With a GPU back-end the points of a study batch run as one ensemble: every 1-D element of every point is integrated in the
same kernel launches (each point with its own steps), so the launch latency is paid once per step for all points instead
of once per point. Whole study wall time (compile, nominal run, all points) of a 200-point scan of a Kerr + Raman plate
(the self-test problem), measured in November 2026 on Windows with an RTX 3060 Ti (FP64 at 1/64 of FP32; driver 610.88,
CUDA 13.3), .NET 10 and a 20-core CPU; no script in the repository repeats these timings
(`GpuTests.StudyPoints_AsOneDeviceEnsemble_MatchTheCpuStudy` checks the results):

| case | CPU, `parallel` 4 | CPU, `parallel` 20 | GPU, a stream per point (`ensemble: 0`, `parallel` 4) | GPU ensemble, CUDA / OpenCL |
|---|---|---|---|---|
| scalar, N = 4096 | 4.4 s | 1.7 s | 13.4 s | 2.2 s / 1.9 s |
| vector, N = 4096 | 5.9 s | 2.4 s | 8.0 s | 2.3 s / 2.5 s |
| scalar, N = 16384 | 18.1 s | 11.3 s | 13.8 s | 7.2 s / 9.2 s |

The ensemble is 3.5 to 6 times faster than separate streams. On this consumer card it matches a fully loaded 20-core CPU
at N = 4096 and is 1.6 times faster at N = 16384. A card with full-rate FP64 (A100, H100) was not measured.

### FP32 study (mixed precision)

The `cpu-fp32` back-end rounds the input and the result of every nonlinear-operator evaluation, and the linear rate, to
single precision; FFTs and the stepper arithmetic stay double, so it is a *lower bound* of the error of a real
single-precision kernel. Acceptance: energy 1e-6, spectrum L1 1e-4, contrast floor unchanged. `tools/bench` case `fp32`,
run 2026-11-26 on the Apple M3 above:

| case | energy | spectrum L1 | max abs(dI) / peak | floor (10th percentile of I / peak) |
|---|---|---|---|---|
| `spm-plate` | 3e-10 | 7e-10 | 3e-9 | 7e-33 |
| `mpc-argon` | 6e-10 | 1e-9 | 1e-8 | 7e-10 |
| `spatial-kerr` | 1e-10 | 4e-10 | 2e-9 | 4e-17 |

The operator in FP32 meets the energy and spectrum criteria by three to four orders of magnitude. The **floor criterion
fails** wherever the intensity floor is below about 1e-8 of the peak (the SPM plate and spatial cases): the rounding noise
(about −85 dB) fills it. So mixed precision is acceptable for energy, spectrum and pulse shape, and is **refused when a
contrast or pedestal instrument asks for more than about 70 dB of dynamic range**.

### Mixed precision on the GPU

`"compute": {"precision": "mixed"}` on a GPU back-end runs the nonlinear operator of 1-D envelope and χ(2) elements,
**including its FFTs**, in single precision; the state, the linear step and the Runge-Kutta combinations stay double.
It is off by default. A run whose instruments ask for more than 70 dB of dynamic range (`contrast` and `crossCorrelator`
at their defaults, any `dynamicRangeDb` or detector above 70 dB) is refused with an error; spatial elements and elements
with orientational (gas) or user-response kernels run in double with a note; the CPU back-end ignores the key with a
warning; studies do not form device ensembles in mixed precision.

Against the double-precision CPU run, on the RTX 3060 Ti above (CUDA and OpenCL agree;
`GpuTests.MixedPrecision_SpmPlateMpcSolitonAndShg_MeetTheFp32Criteria` asserts energy 1e-6 and spectrum L1 1e-4 on any
device):

| case | energy | spectrum L1 | max abs(dI) / peak |
|---|---|---|---|
| `spm-plate` | 4e-10 to 2e-9 | 2e-8 | 4e-8 |
| `mpc-argon` (20 passes) | 6e-9 to 8e-9 | 1e-6 | 7e-5 |
| N = 3 soliton with a dispersive wave (1 mm, β2 = −10 ps²/km) | 2e-11 to 2e-10 | 2e-9 | 4e-9 |
| BBO type-I SHG (1 mm, 200 fs, 20 µJ) | 5e-8 to 8e-8 | 2.5e-7 | 7e-7 to 8e-7 |

Single-precision FFTs make the error grow with the number of steps: the 20-pass cell reaches 7e-5 of the peak intensity,
three orders above the operator-only emulation. Speed on that card: 1.3 to 1.5 times faster than the double-precision
device run, with 1 to 8 % more steps (the rounding noise enters the error estimate); no script in the repository repeats
the timings. The double-precision path uses cuFFT, the mixed path the built-in single-precision FFT.

## Validation

* FFT: the Stockham FFT against radix-2 and the DFT for every power of two
  (`ComputeTests.Stockham_MatchesRadix2AndTheDft_ForEveryPowerOfTwo`); the managed provider always works and a native one
  passes the 1e-12 self-check or says why (`ComputeTests.FftProviders_ManagedAlwaysWorks_NativeOnesPassTheSelfCheckOrSayWhy`);
  `compute.fft` selects the provider and the results agree (`ComputeTests.ComputeFftOption_SelectsTheProvider_AndResultsAgree`);
  `auto` without devices runs on the CPU (`ComputeTests.AutoBackend_WithoutDevices_RunsOnTheCpu`).
* Device back-ends against the CPU: `ultrafast devices --selftest` and `GpuTests` (study ensembles, compiled responses,
  mixed precision; they run where a usable device exists, on a Mac with PoCL).
* Studies against loops: the bench cases print the outputs' digest; the study and the loop agree to 1e-11 relative on
  the multipass cell (table above).

## Benchmarks

Wall times of complete runs, with the hardware and date of each measurement. They show the order of magnitude, not a
promise: they depend on the machine, the load and the problem. `ultrafast devices --benchmark` times your own devices.

**Apple silicon CPU** (Apple M3, 8 cores, 16 GB, macOS, .NET 10, Accelerate FFT; measured 2026-11-18; no script in the
repository builds this case). A scalar fibre of 0.5 m with Kerr, self-steepening and Raman (200 fs, 5 nJ); median of 5
runs, the first run excluded:

| grid N | one run |
|---|---|
| 1024 | 42 ms |
| 4096 | 109 ms |
| 16384 | 0.48 s |
| 65536 | 2.3 s |

A 200-point scan of the fibre length over the same fibre (whole study, compile and nominal run included):

| grid N | `parallel` 1 | `parallel` 4 | `parallel` 8 |
|---|---|---|---|
| 4096 | 19.8 s | 7.2 s | 5.3 s |
| 16384 | 96 s | 36 s | 30 s |

**Spatial, large grid.** A 1920 × 1080 single-frequency panel on a 2048 × 1280 grid (modulator, 0.2 m gap, beam
profile): 2.2 s, and 260 MB held after the run with `keepProfiles: "used"`, 491 MB with `all` (Apple M3, 2026-11-26;
the slow test `SpatialStreamingTests.FullPanel_1920x1080_SingleFrequency_RunsWithinTheSpikeBudget` prints these
figures).

**GPU** (RTX 3060 Ti): [Device ensembles for studies](#device-ensembles-for-studies) and
[Mixed precision on the GPU](#mixed-precision-on-the-gpu). Not measured: a data-centre GPU (full-rate FP64).

## Limits

* Apple GPUs have no double precision, so on a Mac the GPU back-ends do not use the GPU; the CPU with Accelerate is the
  fast path.
* The .NET Framework 4.8 build has only the managed FFT and no SIMD kernels.
* Two speed-ups were measured and not built. **A cache for the compiled system:** a trial on a 128 × 128 grid costs
  10 ms including the compile; revisit if a loop over a much larger system shows the compile taking more than 30 % of a
  trial. **A fully streaming executor** (never holding all transverse frequency slices): the peak memory of a large pulse
  run is set by the .NET garbage collector, not the propagator (2.7 to 3.4 GB at 1920 × 1080 with 16 stored
  frequencies, whatever the block size); revisit if a 4096 × 4096 pulse run is needed, and measure it first. Both
  measurements are recorded in the development history (`docs/design/history.md`, internal); no test repeats them.
* There is no linear fast path (one transfer function, no integration) and no CPU ensemble dimension: a linear element
  already costs one FFT pair, and the CPU has no launch latency to hide.
* Practical advice: let the grid planner size the grid; `"stepper": {"tolerance": 1e-5}` is usually enough (that is what
  [`fidelity: standard`](fidelity.md) sets); install FFTW on Windows and Linux; `"compute": {"backend": "auto"}` is safe to
  leave on, since without a GPU or for small elements it runs on the CPU.
