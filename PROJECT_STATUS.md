# Validation status -- 2.1.8-beta

What is verified, where, and what still needs checking on other machines. Capabilities and known limitations are
described in [docs/capabilities.md](docs/capabilities.md); what comes next is in [ROADMAP.md](ROADMAP.md) (T0 / T1 first).

## Current status (latest run per platform)

| platform | C# tests (`--slow`) | other checks | date |
|---|---|---|---|
| macOS arm64, .NET 10 | **378 pass, 0 fail, 8 skipped** (7 `GpuTests`: no FP64 GPU device; the opt-in HAPI test), net48 builds | Python 110 pass, 1 skipped, on the hosted library, with pyNLO and LightPipes; `build/example_check.py`: all examples and tutorials ok (07 and the C++ example manual); mkdocs --strict passes | 2026-10-31 |
| Linux x64, .NET 10 and Mono (net48) | 225 / 225 (release baseline; the tests added since have not run on Linux -- the new CI will) | Python 61 / 61 with pyNLO; all 31 CLI examples strict; release packages | 2.1.8-beta release |
| Windows (net48 / OpticStudio) | not run | -- | -- |

The sections below are the records of each verification, newest first after the baseline.

## Baseline: release verification of 2.1.8-beta (Linux x64, .NET 10, Mono 6, Python 3.13)

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

## Verified for the review of T0 to T2 and branches (macOS arm64, .NET 10, Python 3.12, 2026-10-31)

| | result |
|---|---|
| C# (`--slow`) | 378 pass, 0 fail, 8 skipped |
| Python | 110 pass, 1 skipped; pyNLO runs on Python 3.12 with jsonpickle, so its cross-checks are available |
| CLI examples vs the engine of d6fb4e9 | 40 outputs of 29 examples: 27 identical. The other 13: numerical noise (centroids near 1e-21 m, edge fractions from 1e-20 to 1e-12), M² of beams with hard edges (below 0.4 %; 25 % for the square iris, where it now holds between planes), and example 08's radial run, 3.4e-4 in GDD of the output. Energy, duration and peak power unchanged within 1e-4 |
| CLI examples vs f22b59f, after the fix for fed crystal ports | 50 outputs of 35 examples: all identical (before the last change, which keeps the exact nominal carrier) |
| Phase matching | sinc² against angle; efficiency as L² then L; SH bandwidth 0.886 / (GVM L); Manley-Rowe through SHG and THG; `shg` and `sfg` are collinear only |
| Not compared | Python examples (they use the current library), and 35 and 38 (version 3 files) |
| Node cache | 81-point delay scan: 58.2 s to 9.6 s with 0.3 m of nonlinear glass upstream, identical rows |
| Not run | net48 tests on Mono, CI, Windows, an independent physics review |

## Verified for diffraction, thermal-lens tracking and spatio-temporal couplings (macOS arm64, .NET 10, 2026-10-29)

| | result |
|---|---|
| `dotnet build UltrafastEngine.sln -c Release` (net10.0 and net48) | 0 errors (the expected ZOS-API warning) |
| C# tests, .NET 10, incl. slow (`--slow`) | **287 pass, 0 fail, 8 skipped** (270 before + 8 `DiffractionTests` + 9 `BeamTrackingTests`; the 8 skips are the 7 `GpuTests` and the opt-in HAPI test, as before) |
| Python tests, NativeAOT engine (no plugins, no pyNLO), `ULTRAFAST_SLOW` unset | 61 pass incl. the new `test_diffraction.py` (3); 4 failures + 1 error need the fitting plugin (`transform`, fits) or reflection that the AOT build trims (`uf.study`): environment, not code -- the hosted engine (needs `clr_loader`) was not available |
| Examples 08, 11, 15, 16, 17, 18, 21, 23, 29 and the tutorial files, strict mode, old vs new engine | every `summary.json` number equal to 10⁻⁴ except round-off-level values (10⁻²⁰ m centroids, sub-fs³ TOD residuals), the `beamProfile` peak fluence of 08 radial (−0.27 %, not quoted), the new tracked-coupling keys of 23 (GDD 10⁻¹³ fs²) and the step statistics of linear spatial elements; no README number changed |
| Example 30 (new, 4 runs, strict) and `compare.py` / `iris.py` | run; all 16 patterns within 1.3 % of the peak of the Fresnel integral (round beams < 0.6 %) |
| Generated files | regenerated; `SchemaTests` pass |
| Not run here | `mkdocs build --strict` (mkdocs not installed; a link / anchor check of the Markdown found no new problems), `mypy` (not installed), net48 tests under Mono, pyNLO cross-checks (propagation physics of 1-D elements unchanged), the hosted Python engine and plugin tests |

## Verified for the delayed-light / HITRAN work (macOS arm64, .NET 10, Python 3.12, 2026-10-29)

| | result |
|---|---|
| `dotnet build UltrafastEngine.sln -c Release` (net10.0 and net48) | 0 errors |
| C# tests, .NET 10, incl. slow (`-- --slow`), after the follow-ups (2-D seeds, tail, surface scatter, detector presets, TIPS) | **270 pass, 0 fail, 8 skipped** (the 7 `GpuTests` and the opt-in `AgainstHapi_*` test, which passes -- 1 of 1 -- with `ULTRAFAST_HITRAN_VALIDATE_DIR` set; 15 more tests: 4 `SmallScaleTests`, 7 `DelayedLightTests`, 4 `HitranTests`). Before the follow-ups: **255 pass, 0 fail, 7 skipped** (the 225 tests of before -- 218 run, 7 skipped -- plus the 24 `DelayedLightTests` and 13 `HitranTests`; the 7 skips are the `GpuTests`: this Mac has no double-precision CUDA / OpenCL device, so the GPU = CPU parity tests did not run here; no GPU kernel or nonlinear operator was changed) |
| Python tests (hosted engine), no pyNLO, `ULTRAFAST_SLOW` unset | 63 pass, 9 skipped (8 in `test_delayed_light.py`, 1 new in `test_packaging.py`) |
| CLI examples in strict mode, incl. the new 28 (delayed light) and 29 (2-D small-scale self-focusing, 57 s) | 28 run; 07, 09 and the two dependent runs of 26 need their own inputs (Zemax export, earlier results) and were not run standalone |
| HITRAN against HAPI (real H2O, CO2, O2 lines, 250 / 296 / 350 K, `plugins/hitran/tools/make_hapi_reference.py`) | integrated absorption within 0.7 %, worst point on a line within 3.9 % (same wing cutoff); compared with the reference implementation, not with a measured spectrum |
| Generated files | regenerated (`/regen` steps); `SchemaTests` pass |
| Not run here | the net48 tests under Mono, the pyNLO cross-checks, `mypy` (not installed), a NativeAOT publish with the hitran plugin compiled in, the live HITRAN API download (the downloader is tested with a fake API) |

## Not verifiable here -- please check on your machines

1. ~~**Apple silicon (arm64)**~~ -- verified 2026-10-29: the full C# suite passes on macOS arm64 (NEON `Vector128` path).
2. **OpticStudio**: the binding compiled against the real ZOS-API (2025 R1) earlier but has not talked to a running OpticStudio. First
   test: `UltrafastEngine.Zemax.exe info`, then `export` on the stretcher and the MPC (docs/plugins/zemax.md).
3. **NativeAOT library** on Windows (`dotnet publish src/UltrafastEngine.Native -r win-x64`): needs nuget.org runtime packs.
   Built and used on Linux x64 (C++ example, native wheel) and on macOS arm64 (`python/tools/build_engine.py --aot`, Python tests,
   2026-10-29).
4. **Visual Studio 2026**: the solution targets .NET 10 + .NET Framework 4.8 and should open as is.
5. **GPU plugin on real GPUs**: no GPU here. The OpenCL path was verified on PoCL (a CPU OpenCL runtime); the CUDA
   path (driver API, NVRTC, cuFFT, cuBLAS) shares the kernels and steppers but has not run. On an NVIDIA machine run
   `ultrafast devices --selftest --benchmark` (Windows and Linux), and on an AMD / Intel GPU with OpenCL; send the
   `--json` output if a check fails. The speed-ups in docs/reference/speed.md are expectations until measured.
6. ~~**Accelerate FFT on macOS**~~ -- verified 2026-10-29: `ultrafast devices` on an M3 reports `FFT accelerate (vDSP)` (it passed its
   self-check against the managed FFT) and the suite ran with it.
7. **Release packages on macOS and Windows**: `./build.sh --all --platform osx-arm64` on the Mac and
   `build.bat --all --platform win-x64 --zos-api-dir ...` on Windows (NativeAOT parts must be built on their own OS).

## Known limitations (summary; full list in docs/capabilities.md)
- Beam tracking: simple astigmatism (principal axes x / y); the tracked beam stays Gaussian (thermal lens as the best-fit
  parabola, spatio-temporal coupling dispersion to fourth order); a multipass amplifier puts its thermal lens on it only on request, a regenerative amplifier not at all.
- Branched systems: beams crossing in a crystal stay collinear in the model; the new branching code has had no independent physics review. A `resonator` is a nested sequence; partial coherence is one run per mode.
  Angular dispersion on profiles needs `physics.spatioTemporal: "profile"` and a cartesian grid. Stress birefringence is the beam average for a uniformly heated cubic rod.
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
