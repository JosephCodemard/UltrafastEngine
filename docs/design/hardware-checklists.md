# Hardware checklists

Internal page ([folder index](README.md)). Exact steps for checks that need hardware the development machines may not have. The results of
each run go to [history.md](history.md) (the first GPU run: Stage 10, 2026-11-16). Zemax has its own checklist in
`plugins/zemax/HARDWARE-CHECKLIST.md` (never run: no OpticStudio).

# GPU (CUDA / OpenCL on an NVIDIA card)

A consumer card (RTX 3060 Ti) runs FP64 at about 1/64 of its FP32 rate, so **parity (same answer as the CPU) is the goal there, not speed**;
speed-ups need an A100-class card. Kernels 1-4, study ensembles and mixed precision were built and checked on 2026-11-16; re-run this list after any
change to `plugins/gpu` and on any new device.

## 1. Build and self-test (about 5 minutes)

```bat
dotnet build UltrafastEngine.sln -c Release
dotnet src\UltrafastEngine.Cli\bin\Release\net10.0\ultrafast.dll devices --selftest
dotnet tests\UltrafastEngine.Tests\bin\Release\net10.0\UltrafastEngine.Tests.dll GpuTests
dotnet src\UltrafastEngine.Cli\bin\Release\net10.0\ultrafast.dll devices --benchmark
```

Paste back **the full output** of the four commands, the output of `nvidia-smi` (device name, driver version, CUDA version) and
`ultrafast devices --json`. Expected: `selftest` passes on the CUDA device (relative L2 difference to the CPU at most 1e-9); the OpenCL device
too if the NVIDIA OpenCL runtime is present; `GpuTests` all PASS (none skipped: a skip means no double-precision device was found).

## 2. Kernels and their CPU references (all four built 2026-11-16; the list stays as the parity map)

Each kernel needs a CPU-parity test in `GpuTests` (relative L2 of the output field at most 1e-9) for **both** CUDA and OpenCL. Start at
`plugins/gpu/src/Kernels.cs` (source of the kernels), `EnvelopeStepper.cs` (1-D), `SpatialStepper.cs`, `GpuBackend.cs` (what a device
supports).

| # | kernel | CPU reference | notes |
|---|---|---|---|
| 1 | gas rotational Raman (today any multipass-cell air gap stays on the CPU) | `Physics/Nonlinear.cs` rotational response | the delayed response is a convolution: use the FFT of the response on the device |
| 2 | modulator and tile source | `Instruments`, `Optics` modulator | S2 parity test exists; extend |
| 3 | expression responses compiled to kernels | `Models` expression responses | restrict to the arithmetic subset; fall back to CPU otherwise |
| 4 | chi(2) | `Crystals/` | |
| later | carrier-resolved propagation, spatial gain | | |

Batched study points as an ensemble dimension in one launch (N <= 4096 1-D problems are launch-latency bound: 200 points in one launch is the
good case). Design: `Studies.Run` evaluates points through `Eval`; batch them in `Batch()` and hand the batch to the back-end when
`compute.backend` is a GPU one.

## 3. Mixed precision (FP32), only after parity

The FP32 study on the Mac (`docs/reference/speed.md`) found the operator in single precision good to 1e-9 in energy and spectrum, but
the contrast floor is filled below about -85 dB. If a mixed-precision mode is built: off by default, refused when a contrast / pedestal instrument asks
for more than about 70 dB, and its parity test compares energy (1e-6) and spectrum L1 (1e-4), not the field to 1e-9. Also check single-precision FFTs
(not covered by the Mac study) on the SPM plate, the 50-transit multipass cell, a soliton and SHG.

## 4. What to paste back, and what to do when a parity test fails

* The full text output, device names, driver version.
* For a failing parity test: the test name, the relative L2 error, the problem size, the kernel name and the backend (CUDA / OpenCL). Bisect by switching one operator term off (`raman`,
  `selfSteepening`) to find the kernel.
* Never loosen a tolerance to make a test pass; a disagreement is a finding.

## 5. Not testable on the Mac

| item | why |
|---|---|
| CUDA kernels of any kind | no NVIDIA device; the Mac has OpenCL only with a double-precision-less Apple GPU |
| OpenCL parity (`GpuTests`) | skipped without a double-precision device; PoCL (`brew install pocl`) could provide a CPU OpenCL device with FP64 here but is not installed and not verified on arm64 |
| .NET Framework 4.8 runs | needs Mono or Windows |
| NativeAOT-library Python test | needs the AOT toolchain |
