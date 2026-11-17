# WP-C(B): GPU coverage and the Windows / NVIDIA check

**Status (2026-11-16): done** on the owner's Windows machine (RTX 3060 Ti, driver 610.88, CUDA 13.3), CUDA and OpenCL: kernels 1-4 (rotational Raman 1-D and spatial; modulator / tile parity; expression, two-photon and extra-Kerr responses compiled to kernels; 1-D χ(2)), study points as one device ensemble, mixed precision for 1-D envelope and χ(2). Results and the measured numbers: `gpu-windows-checklist.md` (section 0), `docs/reference/speed.md`. Open: spatial χ(2) and spatial mixed precision on the device, a single-precision cuFFT plan, carrier-resolved propagation and spatial gain, PoCL in CI.

Session 4. Code is written on the Mac and verified on the owner's Windows + NVIDIA machine (a 3060 Ti runs FP64 at about 1/64 rate: only CUDA **parity** matters there; speed-ups need an A100-class card or an accepted CPU baseline).

## Kernels (backlog B3), in order

1. Gas rotational Raman (today any MPC air gap stays on the CPU).
2. Modulator and tile source.
3. Expression responses compiled to kernels.
4. χ(2).
Then, if time: carrier-resolved propagation, spatial gain.
CUDA and OpenCL both; each with a CPU-parity test in `GpuTests` at 1e-9 (relative L2 of the output field). Batched study points (from WP-C(A)) as the ensemble dimension in one launch: 1-D problems with N ≤ 4096 are launch-latency bound, 200 points in one launch is the good case.
Mixed precision (FP32 steps where WP-C(A)'s study allows) is an option for consumer cards, off by default.

## Test here without a GPU

Try `brew install pocl` (CPU OpenCL with FP64) to run `GpuTests` locally; not yet checked on arm64 macOS. If it works, add PoCL to CI (backlog decision 1).

## `docs/design/gpu-windows-checklist.md` (written in this session)

Exact steps: build (`dotnet build -c Release`), `ultrafast devices --selftest`, `GpuTests`, `--benchmark`, expected tolerances, what to paste back (the full output; device names; driver version), a table of kernels that could not be tested on the Mac, and what to do if a parity test fails (which log, which kernel). The first run is done by the owner; the next session reads the pasted results and fixes.
