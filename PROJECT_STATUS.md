# Validation status -- 2.1.8-beta

What is verified, where, and what still needs checking on other machines. Capabilities and known limitations are
described in [docs/capabilities.md](docs/capabilities.md).

## Verified in the development environment (Linux x64, .NET 10, Mono 6, Python 3.13)

| | result |
|---|---|
| `dotnet build UltrafastEngine.sln -c Release` | builds; one expected warning when no ZOS-API is present (the Zemax binding then builds against stubs) |
| C# tests, .NET 10, incl. slow (`-- --slow`) | **213/213 pass** (incl. the NER and measured-data tests) |
| C# tests, .NET Framework 4.8 build under Mono, incl. slow | **213/213 pass** |
| Python tests (hosted engine), `ULTRAFAST_SLOW=1`, with pyNLO | **56/56 pass**, including the pyNLO cross-checks (fibre, Raman models, gain, DFG: scalar results unchanged by the vector Raman), the measured-data tests and the transform / fit / backward tests |
| Every CLI example (31 run specs, incl. the new 27) with `ULTRAFAST_STRICT_OPTIONS=1` | run (09 after 08, and 26's new-input and backward runs after its fit, as the READMEs say) |
| Python examples (quickstart, 03, 04, 12, 16, 18, 27 `ner.py`) | run |
| `ultrafast study` (examples 22, 25 incl. coherence), `ultrafast retrieve` (example 24, `docs/assets`), `ultrafast materials` | run; results quoted in the READMEs |
| `mkdocs build --strict` | 0 warnings, no broken links or anchors |
| Generated files (schemas, options.md, `ultrafast.types`, python-api.md) | current (checked by tests) |
| `mypy` on `ultrafast.build` + `ultrafast.types` and `ultrafast_fitting.build` | no issues; a misspelt element key is reported |
| C header | `gcc -fsyntax-only` passes |

## Not verifiable here -- please check on your machines

1. **Apple silicon (arm64)**: the SIMD kernels use `Vector128` (NEON on ARM64) on .NET 10 and fall back to scalar
   code elsewhere; tests compare both paths, but they ran on x64 here. Run the C# tests once on the Mac.
2. **OpticStudio**: the binding compiled against the real ZOS-API (2025 R1) earlier but has not talked to a running OpticStudio. First
   test: `UltrafastEngine.Zemax.exe info`, then `export` on the stretcher and the MPC (docs/plugins/zemax.md).
3. **NativeAOT library** (`dotnet publish src/UltrafastEngine.Native -r osx-arm64` / `win-x64`): needs nuget.org runtime
   packs; the same exports were exercised through the hosted route.
4. **Visual Studio 2026**: the solution targets .NET 10 + .NET Framework 4.8 and should open as is.

## Known limitations (summary; full list in docs/capabilities.md)
- Beam tracking: simple astigmatism (principal axes x / y); thermal lenses of gain media are not tracked.
- Spatial models are per element; aberrations on 1-D beams are reported (Strehl), applied only on spatial grids.
- Crystal surfaces are not recognised by the Zemax binding (add `crystal` elements to the exported file).
- BiBO has no built-in d tensor; the MgO:LN temperature coefficient b4 is unverified. TGG index is approximate.
- Rotational Raman: molecular constants from the literature, dephasing times approximate; electronic n2 values
  spread by 10-30 % in the literature.
- Envelope-mode plasma uses the carrier frequency for the Drude term (exact in carrier-resolved mode).
- Compute back-ends: only the CPU back-end ships; GPU back-ends are plugin work. Studies do not pipeline elements
  across points.
- Gain: ASE only in 1-D, no thermal stress birefringence; library cross sections good to ~10-20 %.
- Oscillators relax the gain dynamics; first-principles spatial KLM is slow (experimental).
- Fibres: single mode, frequency-independent effective area, unsaturated gain.
- Measured data and fits are static (record, then compare / fit).
