# Validation status -- 2.1.8-beta

What is verified, where, and what still needs checking on other machines. Capabilities and known limitations are
described in [docs/capabilities.md](docs/capabilities.md).

## Verified in the development environment (Linux x64, .NET 10, Mono 6, Python 3.13)

| | result |
|---|---|
| `dotnet build UltrafastEngine.sln -c Release` | builds; one expected warning when no ZOS-API is present (the Zemax binding then builds against stubs) |
| C# tests, .NET 10, incl. slow (`-- --slow`) | **225/225 pass** (incl. the NER, measured-data, compute and GPU tests; the GPU tests ran on an OpenCL CPU device, PoCL) |
| C# tests, .NET Framework 4.8 build under Mono, incl. slow | **225/225 pass** (GPU tests on PoCL) |
| Python tests (hosted engine), `ULTRAFAST_SLOW=1`, with pyNLO | **61/61 pass**, including the pyNLO cross-checks (fibre, Raman models, gain, DFG: scalar results unchanged by the vector Raman), the measured-data tests and the transform / fit / backward tests |
| Every CLI example (31 run specs, incl. the new 27) with `ULTRAFAST_STRICT_OPTIONS=1` | run (09 after 08, and 26's new-input and backward runs after its fit, as the READMEs say) |
| Python examples (quickstart, 03, 04, 12, 16, 18, 27 `ner.py`) | run |
| Release packages (`./build.sh --all --platform host portable`) | built and smoke-tested on Linux x64: self-contained CLI ran example 01 with both plugins, the hosted and native wheels installed and ran in a fresh venv, the C API example built with CMake and ran; no ZOS-API file in any package. Windows / macOS packages and RID cross-publishing (needs nuget.org) not verified here |
| `ultrafast study` (examples 22, 25 incl. coherence), `ultrafast retrieve` (example 24, `docs/assets`), `ultrafast materials` | run; results quoted in the READMEs |
| `mkdocs build --strict` | 0 warnings, no broken links or anchors |
| Generated files (schemas, options.md, `ultrafast.types`, python-api.md) | current (checked by tests) |
| `mypy` on `ultrafast.build` + `ultrafast.types` and `ultrafast_fitting.build` | no issues; a misspelt element key is reported |
| C header | `gcc -fsyntax-only` passes |
| GPU plugin, OpenCL path | `ultrafast devices --selftest` on PoCL: FFT, 1-D scalar / vector, radial, cartesian all match the CPU to ~1e-15; the same through the NativeAOT library (`uf_devices_json`) and the C++ example `devices.cpp`; Python `uf.devices()` / runs on the device |

## Not verifiable here -- please check on your machines

1. **Apple silicon (arm64)**: the SIMD kernels use `Vector128` (NEON on ARM64) on .NET 10 and fall back to scalar
   code elsewhere; tests compare both paths, but they ran on x64 here. Run the C# tests once on the Mac.
2. **OpticStudio**: the binding compiled against the real ZOS-API (2025 R1) earlier but has not talked to a running OpticStudio. First
   test: `UltrafastEngine.Zemax.exe info`, then `export` on the stretcher and the MPC (docs/plugins/zemax.md).
3. **NativeAOT library** on macOS / Windows (`dotnet publish src/UltrafastEngine.Native -r osx-arm64` / `win-x64`): needs
   nuget.org runtime packs. On Linux x64 it was built (through `build.sh --cpp`), loaded by the C++ example and by
   the native Python wheel.
4. **Visual Studio 2026**: the solution targets .NET 10 + .NET Framework 4.8 and should open as is.
5. **GPU plugin on real GPUs**: no GPU here. The OpenCL path was verified on PoCL (a CPU OpenCL runtime); the CUDA
   path (driver API, NVRTC, cuFFT, cuBLAS) shares the kernels and steppers but has not run. On an NVIDIA machine run
   `ultrafast devices --selftest --benchmark` (Windows and Linux), and on an AMD / Intel GPU with OpenCL; send the
   `--json` output if a check fails. The speed-ups in docs/reference/speed.md are expectations until measured.
6. **Accelerate FFT on macOS**: written against vDSP's documented API, not run here (Linux only); the provider is
   used only after it passes its self-check against the managed FFT, so a fault shows up as a note and a fall-back.
   `ultrafast devices` on the Mac shows the provider in use.
7. **Release packages on macOS and Windows**: `./build.sh --all --platform osx-arm64` on the Mac and
   `build.bat --all --platform win-x64 --zos-api-dir ...` on Windows (NativeAOT parts must be built on their own OS).

## Known limitations (summary; full list in docs/capabilities.md)
- Beam tracking: simple astigmatism (principal axes x / y); thermal lenses of gain media are not tracked.
- Spatial models are per element; aberrations on 1-D beams are reported (Strehl), applied only on spatial grids.
- Crystal surfaces are not recognised by the Zemax binding (add `crystal` elements to the exported file).
- BiBO has no built-in d tensor; the MgO:LN temperature coefficient b4 is unverified. TGG index is approximate.
- Rotational Raman: molecular constants from the literature, dephasing times approximate; electronic n2 values
  spread by 10-30 % in the literature.
- Envelope-mode plasma uses the carrier frequency for the Drude term (exact in carrier-resolved mode).
- GPU (gpu plugin): 1-D and spatial envelope propagation only; user responses, rotational Raman, position-dependent
  properties, carrier-resolved, χ(2) and gain stay on the CPU. Studies do not pipeline elements across points.
- Gain: ASE only in 1-D, no thermal stress birefringence; library cross sections good to ~10-20 %.
- Oscillators relax the gain dynamics; first-principles spatial KLM is slow (experimental).
- Fibres: single mode, frequency-independent effective area, unsaturated gain.
- Measured data and fits are static (record, then compare / fit).
