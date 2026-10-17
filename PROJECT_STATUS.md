# Project status -- 2.1.8-beta

Stage 1 (the stable core) is complete: phases 0-2.5 and Stage 1.1-1.8 (ROADMAP.md). Next: Stage 2, the fitting
plugin with `transform` elements. What was verified where:

## Verified in the development environment (Linux x64, .NET 10, Mono 6, Python 3.13)

| | result |
|---|---|
| `dotnet build UltrafastEngine.sln -c Release` (Zemax binding against the real ZOS-API assemblies in `plugins/zemax/src/UltrafastEngine.Zemax/lib`) | builds, 0 warnings |
| C# tests, .NET 10, incl. slow (`-- --slow`) | **149/149 pass** |
| C# tests, .NET Framework 4.8 build under Mono, incl. slow | **149/149 pass** |
| Python tests (hosted engine), `ULTRAFAST_SLOW=1` | **38 pass**, 4 skipped (pyNLO cross-checks: pyNLO not installed in this environment; they passed in 2.0.0-alpha.2) |
| Every CLI example (26 run specs) with `ULTRAFAST_STRICT_OPTIONS=1` | run (09 after 08, as its README says) |
| Python examples (quickstart, 03, 04, 12, 16, 18) | run |
| `ultrafast study` (example 22), `ultrafast retrieve` (example 24, `docs/assets`), `ultrafast materials` | run; results quoted in the READMEs |
| `mkdocs build --strict` | 0 warnings, no broken links or anchors |
| Generated files (schemas, options.md, `ultrafast.types`, python-api.md) | current (checked by tests) |
| `mypy` on `ultrafast.build` + `ultrafast.types` | no issues; a misspelt element key is reported |
| C header | `gcc -fsyntax-only` passes |

## Not verifiable here -- please check on your machines

1. **Apple silicon (arm64)**: the SIMD kernels use `Vector128` (NEON on ARM64) on .NET 10 and fall back to scalar
   code elsewhere; tests compare both paths, but they ran on x64 here. Run the C# tests once on the Mac.
2. **OpticStudio**: the binding compiles against the real ZOS-API but has not talked to a running OpticStudio. First
   test: `UltrafastEngine.Zemax.exe info`, then `export` on the stretcher and the MPC (docs/plugins/zemax.md).
3. **NativeAOT library** (`dotnet publish src/UltrafastEngine.Native -r osx-arm64` / `win-x64`): needs nuget.org runtime
   packs; the same exports were exercised through the hosted route.
4. **Visual Studio 2026**: the solution targets .NET 10 + .NET Framework 4.8 and should open as is.

## Known limitations
- Beam tracking: simple astigmatism (principal axes x / y); thermal lenses of gain media are not yet tracked.
- Spatial models are per element; aberrations on 1-D beams are reported (Strehl), applied only on spatial grids.
- Crystal surfaces are not yet recognised by the Zemax binding (add `crystal` elements to the exported file).
- BiBO has no built-in d tensor; the MgO:LN temperature coefficient b4 is unverified. TGG index is approximate.
- Rotational Raman: molecular constants from the literature, dephasing times approximate; electronic n2 values
  spread by 10-30 % in the literature.
- Envelope-mode plasma uses the carrier frequency for the Drude term (exact in carrier-resolved mode).
- Compute back-ends: only the CPU back-end ships; GPU back-ends are plugin work. Studies do not pipeline elements
  across points.
- Gain: ASE only in 1-D, no thermal stress birefringence; library cross sections good to ~10-20 %.
- Oscillators relax the gain dynamics; first-principles spatial KLM is slow (experimental).
- Measured data and fits are static (record, then compare / fit); Stage 2 rebuilds fitting around `transform` elements.
