# UltrafastEngine 2.0

**Nonlinear ultrafast pulse propagation through open-air optical systems** -- stretchers, compressors,
multipass cells, plates, gas cells, mirrors and coatings, and (Phase 2) nonlinear crystals for frequency
conversion and OPCPA, with spatially resolved beams -- with an optional OpticStudio (Zemax) front end.

UltrafastEngine answers "what does my pulse look like after this system?" -- spectrum, temporal profile,
chirp, energy and losses, B-integral, fluence against damage thresholds, polarisation -- and shows it the way
your lab instruments would (spectrometer, autocorrelator, FROG, d-scan, photodiode, contrast meter,
polarimeter...).

```
 OpticStudio (.zos) --Zemax binding--+
                                     v
 hand-written / Python-built --> system file (.ufs) --+--> UltrafastEngine.Core --> results (JSON, NPZ, CSV)
                                     run spec (.json) -+     (C#, no dependencies)       instruments, provenance
                                                             ^         ^        ^
                                              C API (C/C++)  Python    CLI      C# (.NET / .NET Framework)
```

## What makes it different

* **Zemax-free physics core.** One C# library with zero dependencies, usable from C#, C/C++ (C API, NativeAOT
  library), Python (`pip install ultrafast-engine`) and the command line. The OpticStudio binding is a separate
  program that *exports* a system file; the same file runs on Windows, macOS and Linux.
* **Everything overridable.** Every physical ingredient -- refractive index, n2, Raman response, time-domain
  index response n(t, I), beam size, coatings, apertures, filters -- is a model you can replace with data, an
  expression or your own function (Python, C#, C), per material, per element or globally. A provenance report
  records where every value came from.
* **Exact linear optics.** Dispersion from ray-traced group delay (correct through gratings), Sellmeier or
  tabulated materials with absolute (vacuum-referenced) indices, thin-film coatings by transfer matrices,
  Fresnel losses, aperture clipping, surface scatter, Jones-matrix polarisation with s/p basis tracking.
* **Fast, adaptive nonlinear propagation.** Interaction-picture Runge-Kutta (RK4IP and the adaptive
  ERK4(3)-IP), Kerr + Raman + self-steepening with full vector coupling, user time responses, and a
  carrier-resolved real-field mode for physics on the optical-cycle scale. Cross-checked against pyNLO to <0.1 %.
* **Active elements (Phase 2).** χ(2) crystals (SHG, SFG, OPA/OPCPA, DFG; birefringent and QPM, non-collinear
  magic angle) with exact dispersion of every field, aux beams from pulse specs or previous runs, by-products,
  and a phase-matching calculator. Radial and cartesian spatial models for self-focusing, spatial depletion,
  walk-off and M². User elements in Python, C# or C. Gain media and oscillators follow in Phases 2.2 / 2.3.
* **Honest diagnostics.** Exact loss budget per mechanism, energy-conservation and window-edge checks, critical
  power and LIDT warnings, and per-element tables -- not just an output spectrum.
* **Connected to the lab.** Instruments can look through a fibre, pinhole or slit in the near or far field, compare
  themselves with your recorded data (CSV / NPZ), and a calibration fit finds uncertain parameters with error
  bars and predicts every other instrument from them.

## Where to go next

| I want to... | Read |
|---|---|
| install / build (Visual Studio, macOS, Linux, Python) | [Getting started](getting-started.md) |
| learn step by step | [Tutorials](tutorials/index.md) |
| understand the model and conventions | [Concepts](concepts.md), [Model levels](model-levels.md) |
| describe a system / a run | [System file](reference/system-file.md), [Run spec](reference/run-spec.md) |
| change physics without code | [Models & overrides](reference/models.md) |
| frequency conversion, OPA/OPCPA, phase matching | [Crystals](reference/crystals.md), [Active elements](reference/active-elements.md) |
| diffraction, self-focusing, beam quality | [Spatial models](reference/transverse.md) |
| plug in my own function | [Extending](reference/extending.md), [Python](reference/python.md), [C API](reference/c-api.md) |
| export from OpticStudio | [Zemax plugin](plugins/zemax.md) |
| read the results | [Results](reference/results.md), [Instruments](reference/instruments.md) |
| compare with my measurements, calibrate the model | [Sampling](reference/instruments.md#sampling-where-the-instrument-looks), [Fitting plugin](plugins/fitting.md), [Tutorial 4](tutorials/04-measured-data.md) |
| know what is validated | [Testing & validation](testing.md) |
| see what comes next | [Roadmap](roadmap.md) |
