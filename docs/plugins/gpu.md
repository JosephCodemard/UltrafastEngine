# GPU (CUDA and OpenCL)

The `gpu` plugin runs nonlinear propagation on a graphics card. It adds two compute back-ends:

| back-end | devices | needs |
|---|---|---|
| `cuda` | NVIDIA GPUs (compute capability 5.0 or newer) | the NVIDIA driver and NVRTC (the CUDA run-time compiler, from the CUDA toolkit or `pip install nvidia-cuda-nvrtc-cu12`); cuFFT and cuBLAS are used when found |
| `opencl` | any OpenCL 1.2 device with double precision: NVIDIA, AMD and Intel GPUs | the GPU driver (it installs the OpenCL runtime) |

Nothing is linked at build time: the plugin loads on every machine and reports what it found. Without a GPU the
back-ends say why they are unavailable and every run stays on the CPU. Results on the device agree with the CPU to
rounding (the same schemes and arithmetic; ~1e-15 relative in the self-test).

## Use it

```jsonc
// run spec
"compute": { "backend": "auto" }       // GPU for elements it supports and that are large enough, CPU otherwise
"compute": { "backend": "cuda", "device": 0 }
```

```bash
ultrafast devices                      # what was found
ultrafast devices --selftest           # each device against the CPU: FFT, 1-D scalar / vector, radial, cartesian
ultrafast devices --benchmark          # also times CPU and device on growing problems
ultrafast run system.ufs run.json --backend auto
ULTRAFAST_BACKEND=auto python my_script.py      # default back-end for every run
```

```python
import ultrafast as uf
from ultrafast import build as b

print(uf.devices()["devices"])                         # [{"backend": "cpu", ...}, {"backend": "cuda", "name": ..., "memoryBytes": ...}]
print(uf.devices(selftest=True)["selftest"])
spec = b.run_spec(pulse, compute=b.compute("auto"))    # per run
uf.set_default_backend("auto")                         # or for every run of the process
rows = uf.benchmark()                                  # [{"problem", "n", "cpuSeconds", "deviceSeconds", "speedup", ...}]
```

```c
char* json;  uf_devices_json("{\"selftest\": true}", &json);  /* ... */  uf_free(json);
uf_set_default_backend("auto");
```

The C library and the native Python wheel have the plugin compiled in (they cannot load plugin folders); the CLI,
the hosted Python wheel and C# load it from `plugins/gpu`. Example program: `docs/examples/cpp/devices.cpp`.

Each nonlinear element's notes say where it ran (`device: cuda #0 NVIDIA RTX A5000 (cuFFT)`) or why it stayed on the
CPU (`cuda: small problem (4096 samples < gpuMinPoints 8192); ran on the CPU`).

## What runs on the device

| element | on the device |
|---|---|
| bulk, fibre, gap with 1-D envelope propagation: Kerr (scalar or vector, with the coherent coupling), Raman (scalar, or vector with the anisotropic part), self-steepening, z-dependent beam area | yes |
| spatial (radial Hankel or cartesian) envelope propagation with the same physics, envelope or UPPE coupling, vector fields | yes |
| user time responses (`timeResponse` models, callbacks), rotational Raman of gases, position-dependent properties | CPU |
| carrier-resolved field propagation, χ(2) crystals, amplifiers, ionisation | CPU |

The built-in GPU FFT needs power-of-two lengths (the grid planner always picks them); with cuFFT any even length
works for the time axis. Cartesian transverse grids must be powers of two on the device.

`"auto"` sends an element to the device only when it has at least `gpuMinPoints` complex samples per state (default
8192 for 1-D, 32768 for spatial): below that the CPU is faster, because each GPU step costs about 50-150 kernel
launches of a few µs each. An explicit `"cuda"` / `"opencl"` uses the device for every supported element unless
`gpuMinPoints` is set. `"auto"` never picks an OpenCL device that is a CPU (PoCL, Intel's CPU runtime) unless
`ULTRAFAST_OPENCL_ALLOW_CPU=1`. What speed-up to expect: [Speed](../reference/speed.md#what-gains-from-a-gpu).

## Setting up

### Windows (NVIDIA)

1. Install the NVIDIA driver (it provides `nvcuda.dll` and `OpenCL.dll`). The `opencl` back-end works now.
2. For `cuda`: install the CUDA toolkit (12.x or 13.x; only NVRTC is needed, cuFFT / cuBLAS are used when
   present). The plugin searches `CUDA_PATH\bin` and `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\*\bin`.
   Without the toolkit: `pip install nvidia-cuda-nvrtc-cu12 nvidia-cufft-cu12` and set
   `ULTRAFAST_CUDA_LIB_DIR` to the wheel's `nvidia\cuda_nvrtc\bin` (and `nvidia\cufft\bin`, separated by `;`).
3. `ultrafast devices --selftest`.

### Linux (NVIDIA)

1. Install the NVIDIA driver (`libcuda.so.1`; `libnvidia-opencl` and an ICD loader, `ocl-icd-libopencl1`, for
   OpenCL).
2. For `cuda`: the CUDA toolkit (`/usr/local/cuda/lib64` is searched, and `CUDA_HOME`) or
   `pip install nvidia-cuda-nvrtc-cu12 nvidia-cufft-cu12 nvidia-cublas-cu12` with `ULTRAFAST_CUDA_LIB_DIR` pointing
   at the wheels' `lib` folders (separated by `:`). WSL2 works (`/usr/lib/wsl/lib/libcuda.so.1`).
3. `ultrafast devices --selftest`.

### Linux / Windows (AMD, Intel)

Install the GPU driver with OpenCL (AMD: ROCm or the Adrenalin driver; Intel: the compute runtime). Check that
the device reports double precision in `ultrafast devices` (most AMD GPUs do; many Intel integrated GPUs do
not, and are listed as not usable).

### macOS

Apple GPUs have no double-precision arithmetic and CUDA does not exist on macOS, so the GPU back-ends are not
used there. The CPU uses Apple's Accelerate FFT automatically (`"fft": "auto"`), which is the fast path on a Mac.

### Environment variables

| variable | effect |
|---|---|
| `ULTRAFAST_BACKEND` | default back-end of runs without `compute.backend` (`auto`, `cuda`, ...) |
| `ULTRAFAST_DEVICE` | default device index |
| `ULTRAFAST_CUDA_LIB_DIR` | folders searched first for NVRTC, cuFFT, cuBLAS (`;` / `:` separated) |
| `ULTRAFAST_CUDA_NO_VENDOR=1` | ignore cuFFT and cuBLAS (use the built-in kernels; for comparisons) |
| `ULTRAFAST_OPENCL_LIBRARY` | the OpenCL ICD loader to load |
| `ULTRAFAST_OPENCL_ALLOW_CPU=1` | let `auto` use OpenCL CPU devices |

### Troubleshooting

| message | fix |
|---|---|
| `NVIDIA driver not found` | install / update the driver; on Linux check `ldconfig -p \| grep libcuda` |
| `NVRTC ... not found` | install the CUDA toolkit or the NVRTC wheel and set `ULTRAFAST_CUDA_LIB_DIR`; meanwhile use `opencl` |
| `CUDA_ERROR_UNSUPPORTED_PTX_VERSION` | the driver is older than the toolkit's NVRTC: update the driver |
| `no usable ... device (double precision is required)` | the GPU has no fp64 (Apple, many Intel iGPUs) |
| `not enough device memory` | smaller grid / fewer transverse points, or `deviceSlots: 1` for parallel studies |
| `cuda failed (...); ran on the CPU` | the run finished on the CPU; send the message with `ultrafast devices --selftest --json` in a bug report |

## Concurrent runs (studies, fits)

Every thread that runs a simulation gets its own CUDA stream / OpenCL command queue, so parallel study points, fit
evaluations and coherence trials overlap on one device. `"compute": {"deviceSlots": 4}` caps the element
propagations running on a device at once (memory, queue depth); when all slots are busy, `auto` runs the element on
the CPU (CPU and GPU then work together) and an explicit back-end waits.

## How it works (for developers and AI agents)

Source: `plugins/gpu/src/`.

| file | role |
|---|---|
| `NativeLib.cs` | run-time loading of vendor libraries (NativeLibrary on .NET 10, LoadLibrary / dlopen on .NET Framework / Mono) |
| `GpuDevice.cs` | device abstraction: `GpuContext` (one device, compiled kernels), `GpuStream` (one per thread), buffers, `GpuFft` (vendor or built-in Stockham, contiguous or strided layouts) |
| `Kernels.cs` | **one** kernel source, compiled as OpenCL C or CUDA C++ (NVRTC) through a few macros: element-wise RK operations, reductions, radix-4 Stockham FFT passes, the 1-D and spatial nonlinear operators, the Hankel matrix product |
| `OpenClDevice.cs`, `CudaDevice.cs` | the two runtimes (function pointers to the C APIs; cuFFT / cuBLAS optional) |
| `EnvelopeStepper.cs` | `DeviceStepper`: ERK4(3)-IP and RK4IP exactly as `Steppers.AdaptiveErk43Ip` / `FixedRk4Ip`, and the 1-D operator (mirrors `EnvelopeProblem.NonlinearFast`) |
| `SpatialStepper.cs` | the spatial operator (mirrors `SpatialEnvelopeProblem.Nonlinear`) |
| `GpuBackend.cs` | `cuda` / `opencl` back-ends: discovery, device choice, `WhyNot(problem)`, slots, CPU fall-back on device errors |
| `GpuSelfTest.cs` | standard problems, self-test and benchmark |

Conventions: on the device every spectrum is in natural FFT order (the host swaps halves on upload and download), so
the engine's centred spectral transforms become plain FFTs; the time→frequency transform uses exponent +1,
unnormalised, frequency→time exponent -1 with 1/N, as `SpectralTransform`. A new physics term must be added to the
CPU operator, to `DeviceSpec()` (or make it return null), to the kernel and to `GpuTests`.

Tests: `tests/UltrafastEngine.Tests/GpuTests.cs` compares every path with the CPU (1e-9 required). Machines without
a GPU can run them on the CPU through PoCL:

```bash
pip install pocl-binary-distribution
mkdir -p ~/.icd && find / -name "libpocl*.so" 2>/dev/null | head -1 > ~/.icd/pocl.icd
OCL_ICD_VENDORS=~/.icd dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll GpuTests
```
