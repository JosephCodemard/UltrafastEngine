# UltrafastEngine

**Nonlinear ultrafast pulse propagation through complete optical systems** — stretchers and compressors,
amplifiers, multipass cells, fibres, crystals, plates, gas cells, mirrors and coatings.

UltrafastEngine answers *what does my pulse look like after this system?* — spectrum, temporal profile, chirp,
energy and losses, B-integral, contrast, fluence against damage thresholds, polarisation, beam size — and shows it the
way laboratory instruments would: spectrometer, autocorrelator, FROG, d-scan, photodiode, cross-correlator,
polarimeter, camera, each with its own resolution and detector.

```
 OpticStudio ──(zemax plugin)──┐
                               v
 hand-written / Python ──> system file (.ufs) ──┐
                           run spec (.json)  ───┴──> UltrafastEngine.Core ──> results: summary.json, result.npz, CSV,
                                                     (C#, no dependencies)      instruments, loss budget, provenance
                                                       ^      ^      ^
                                                CLI   Python  C API (C/C++)  .NET
```

## In short

* **One engine, every interface.** A C# library with no dependencies (.NET 10 and .NET Framework 4.8), used from the
  command line, from Python in-process, from C/C++ through a C API, and from any .NET language. System files run
  unchanged on Windows, macOS and Linux.
* **Physics of the whole chain.** Exact linear optics (materials, coatings, gratings, polarisation, losses), Kerr and
  Raman nonlinearity, fibres, gases with rotational Raman and ionisation, χ(2) crystals, gain media and oscillators,
  spatial models up to UPPE — see [Capabilities and limitations](capabilities.md).
* **Everything is a replaceable model.** Indices, n², Raman responses, beam sizes, coatings, filters, gain media and
  time-domain responses can be data, expressions or your own functions, set per material, per element or globally
  from the run spec ([Models and overrides](reference/models.md)).
* **Instruments, not just fields.** Every result can be read through a modelled device, with its resolution,
  sampling and detector.
* **From the design to your lab** (fitting plugin): `transform` elements fitted to your recordings make the model
  reproduce the real system and carry that to new inputs; a measured output field can be propagated back to the input.
* **Plugins.** Element types, instruments, models, run modes, retrieval algorithms, compute back-ends and commands
  can be added without touching the core ([Plugin system](reference/plugins.md)).
* **Self-describing options.** Every key has a declared type, unit and default: JSON Schemas give editor completion,
  typos are reported with suggestions, Python gets TypedDicts ([Option reference](reference/options.md)).

## Where to go next

| I want to... | Read |
|---|---|
| install and build | [Getting started](getting-started.md) |
| learn step by step | [Tutorials](tutorials/index.md) |
| see what is modelled and what is not | [Capabilities and limitations](capabilities.md) |
| understand the model and conventions | [Concepts](concepts.md), [Model levels](model-levels.md) |
| describe a system / a run | [System file](reference/system-file.md), [Run spec](reference/run-spec.md) |
| change physics without code | [Models and overrides](reference/models.md) |
| fibres and supercontinuum | [Fibres](reference/fibers.md) |
| frequency conversion, OPA / OPCPA | [Crystals](reference/crystals.md) |
| amplifiers and lasers | [Gain media](reference/gain.md), [Oscillators](reference/oscillators.md) |
| diffraction, self-focusing, filaments | [Spatial models](reference/transverse.md), [Ionisation](reference/ionization.md) |
| model my instruments, compare with measurements | [Instruments](reference/instruments.md), [Fitting plugin](plugins/fitting.md) |
| fit the model to my lab, reuse it on new inputs, propagate backward | [Fitting plugin](plugins/fitting.md), [example 26](examples/26-transforms/README.md) |
| install or check plugins | [Installing plugins](plugins/installing.md) |
| scan, optimise, retrieve a pulse | [Studies](reference/studies.md), [Pulse retrieval](reference/retrieval.md) |
| plug in my own function or element | [Extending](reference/extending.md), [Python](reference/python.md), [C API](reference/c-api.md) |
| export from OpticStudio | [Zemax plugin](plugins/zemax.md) |
| check the validation | [Testing and validation](testing.md) |
| drive it from an AI agent | [For AI agents](agents.md) |
