# UltrafastEngine

**Nonlinear ultrafast pulse propagation through complete optical systems** — stretchers and compressors,
amplifiers, multipass cells, fibres, crystals, plates, gas cells, mirrors and coatings — simulated end to end and
measured the way your lab instruments would measure it.

`2.1.8-beta` · C# core with **no dependencies** (.NET 10 and .NET Framework 4.8) · command line · C API ·
Python package · plugins · MIT licence

```bash
dotnet build UltrafastEngine.sln -c Release
dotnet run --project src/UltrafastEngine.Cli -c Release -- run docs/examples/01-spm-plate/plate.ufs docs/examples/01-spm-plate/run.json -o results
```

```python
import ultrafast as uf
from ultrafast import build as b

system = b.system("plate", [b.bulk("plate", "FusedSilica", 5e-3, beam=b.gaussian_beam(100e-6, 2.5e-3))])
spec = b.run_spec(b.pulse(1.03e-6, 200e-15, energy=5e-6), instruments=[b.instrument("spectrometer"), b.instrument("compressor")])
result = uf.run(system, spec)
print(result.summary["output"]["fwhmS"], result.instrument("compressor")["compressedFwhmS"])
```

## What it does

You describe an optical system in a **system file** (`.ufs`, JSON) and a run in a **run spec** (input pulse, physics
options, instruments). The engine sizes its own time–frequency grid, propagates the pulse element by element, and
writes a self-describing results folder: scalar summaries (`summary.json`), arrays (`result.npz`, CSV), per-element
records, a loss budget, and the provenance of every model value. Instruments report what a real device would show,
with its resolution, sampling and detector.

| Area | What is modelled |
|---|---|
| **Linear optics** | dispersion to all orders from Sellmeier / tabulated / [refractiveindex.info](https://refractiveindex.info) materials or ray-traced group delay; grating pairs, prisms (angular dispersion and pulse-front tilt of the tracked beam; prism-pair and Martinez-stretcher dispersion from ray-traced group delay), chirped mirrors with GDD ripple; absolute (vacuum-referenced) indices and air |
| **Surfaces and losses** | Fresnel, thin-film coatings (transfer matrix), clipping, scatter, absorption, ND filters, thin-film polarisers — with an exact per-mechanism loss budget, peak fluence and LIDT margins |
| **Polarisation** | Jones calculus with s/p basis transport; waveplates (thick, zero-order, compound, with birefringent dispersion), polarisers, Faraday rotators |
| **Kerr nonlinearity** | self- and cross-phase modulation with the full isotropic χ(3) tensor, Raman (Blow–Wood, Lin–Agrawal, Hollenbeck–Cantrell), self-steepening, user time-domain responses n(t, I) |
| **Fibres** | single-mode fibres and waveguides from Taylor coefficients or a dispersion curve, γ or γ(z), loss and gain, soliton and dispersive-wave diagnostics, quantum noise and supercontinuum coherence |
| **Gases** | rotational Raman of N₂, O₂, CO₂, H₂ and air; pressure and temperature scaling; photo-ionisation (PPT, ADK, Keldysh, MPI) and plasma |
| **χ(2) crystals** | SHG, SFG, DFG, OPA / OPCPA, birefringent and quasi-phase-matched (including chirped gratings), non-collinear (OPA, DFG, and SFG with crossing beams and recorded output angles); phase-matching calculator |
| **Active media** | Yb, Ti:sapphire, Nd and user gain media with rate equations, pumping, thermal lens, multipass and regenerative amplifiers; mode-locked oscillators (Kerr-lens, SESAM) |
| **Beams and space** | Gaussian beam tracking (astigmatic) through lenses and off-axis mirrors; radial and cartesian full-field models with diffraction, self-focusing, Zernike aberrations, vector fields and a UPPE coupling |
| **Contrast** | input pedestals (pre/post-pulses, ASE, phase noise, a measured contrast trace), ghost reflections of plates, crystals and rods, a third-order cross-correlator with 100+ dB dynamic range |
| **Instruments** | energy meter, spectrometer, photodiode/scope, intensity and interferometric autocorrelator, FROG, d-scan, cross-correlator, polarimeter, cameras, wavefront sensor, ideal compressor, and virtual probes (Wigner, field, space–time) — each with resolution, filters and a detector model |
| **Analysis** | parameter scans (grid, random, Latin hypercube, Sobol), optimisation, local and global sensitivities, FROG / d-scan pulse retrieval |

## From the design to your lab

A model that only knows the design predicts the design. The **fitting plugin** (shipped with the engine) closes the
gap with your measurements:

* **`transform` elements**: physical changes to the beam that you insert where they happen. They cover spectral
  phase (residual dispersion), spectral amplitude (gain narrowing, filters, optionally causal via Kramers–Kronig),
  energy, polarisation errors, contrast degradation (replicas, phase noise, ASE pedestals) and wavefront errors.
  Any of their numbers can be marked `"fit": true`.
* **Measured data** on any instrument (part of the core): CSV, NPZ or JSON, curves, traces and readings, compared on
  the recording's axis and range at the recording instrument's resolution, linearly or in dB
  ([Measured data](docs/reference/measured-data.md)). Measured input spectra are cleaned (sorted, duplicates merged,
  background subtracted).
* **Fitting** with Levenberg–Marquardt, CMA-ES or MCMC (or your own estimator), with priors, smoothness and
  instrument-response parameters. Results come in three layers (simulated → inferred → believed), with an influence
  map showing which measurement determines which number and the provenance of every data file. The output includes
  `fitted-system.ufs`, which runs the fitted line on **new inputs**.
* **Backward propagation**: give it the field measured at the output and it finds the field at every plane upstream,
  including through nonlinear elements.

```bash
ultrafast run line.ufs run-fit.json -o results-fit                              # fit the transforms to the recordings
ultrafast run results-fit/fitted-system.ufs new-seed.json -o results-new         # reuse them on a new input
```

See [Fitting plugin](docs/plugins/fitting.md) and [example 26](docs/examples/26-transforms/README.md).

## Validation

The physics is validated against analytic results and against [pyNLO](https://github.com/pyNLO/PyNLO): fibre
propagation with Raman and gain agrees to better than 0.5 % (temporal and spectral profiles), difference-frequency
generation in PPLN to better than 1 % in the converted energies. Beyond the unit tests:

* [Published results reproduced](docs/papers/README.md): post-compression quality, the soliton self-frequency shift, focused
  second-harmonic generation, small-scale self-focusing, nonlinear ellipse rotation in a multi-pass cell, and a lab beamline
  fitted to recordings, each with plots and an account of what agrees and what does not.
* [Component verification](docs/component-tests.md): every component run alone against its analytic answer, with plots.

See [Capabilities and limitations](docs/capabilities.md).

## Built to be changed

* **Every ingredient is a model.** Refractive index, n², Raman response, beam size, coatings, filters, gain media,
  time-domain responses — each can be given as data, an expression or a function (C#, Python, C), per material,
  per element or globally, without editing the system file (overrides in the run spec). A provenance report records
  where every value came from.
* **Plugins.** Element types, instruments, models, run modes, transform operators, estimators, retrieval
  algorithms, compute back-ends and CLI commands can be added as plugins that the engine discovers at start-up. Four
  plugins live in `plugins/`: **fitting** (transforms, fitting, backward propagation), **zemax** (export a sequential
  OpticStudio system to a system file), **gpu** (CUDA and OpenCL back-ends) and **hitran** (gas lines in air). A plugin is a folder:
  `ultrafast plugins install <folder or zip>`, `ultrafast plugins list`. See [Plugins](docs/plugins/index.md) and
  [Installing plugins](docs/plugins/installing.md).
* **Schemas everywhere.** Every option is declared once with its type, unit and default. From that declaration come
  the JSON Schemas (editor completion for `.ufs` and run specs), unknown-key warnings with suggestions, the
  [option reference](docs/reference/options.md), and Python TypedDicts for autocompletion and type checking.
* **One engine, every interface.** The same core runs from the CLI, from Python (in-process), from C/C++ through a
  C API (NativeAOT library), and from any .NET language.

* **Beams that split and meet.** A system is `Sequential` or `Branched`: splitters, interferometers, loops and crystals fed by arms, with the carrier phase of each path, so interference needs no separate model. Instruments such as an autocorrelator or FROG can be built from elements ([Branched systems](docs/reference/branches.md)).

## Documentation

| | |
|---|---|
| [Getting started](docs/getting-started.md) | build on Windows, macOS (including Apple silicon) and Linux; Python setup |
| [Tutorials](docs/tutorials/index.md) | six step-by-step tutorials: first run, diagnostics, a CPA chain, measured data, contrast, diffraction |
| [Examples](docs/examples/README.md) | 43 worked systems, from an SPM plate to a supercontinuum, regenerative amplifier, OPCPA, interferometers and instruments built from elements, each marked CLI, Python or both |
| [Reference](docs/reference/system-file.md) | system file, run spec, every element and instrument, models, results |
| [Capabilities and limitations](docs/capabilities.md) | what is modelled, how it is validated, and what is not modelled |
| [Plugins](docs/plugins/index.md) | the fitting, zemax, gpu and hitran plugins, [installing plugins](docs/plugins/installing.md), [writing your own](docs/reference/plugins.md) |
| [For AI agents](docs/agents.md) | how to drive and extend the engine programmatically ([`AGENTS.md`](AGENTS.md), [`llms.txt`](llms.txt)) |
| [Papers](docs/papers/README.md) | published results reproduced with the engine, with plots and commentary |
| [Claude Code](docs/claude-code.md) | the Claude Code setup: skills and agents to replicate papers, mirror a lab setup, optimise and debug a setup, plus the developer commands |
| [Releasing](docs/releasing.md) | `build.sh` / `build.bat`: compiled packages (CLI, Python wheel, C API, C# libraries, OpticStudio extension) for each platform, with only the plugins you choose |

The documentation is a [MkDocs](https://www.mkdocs.org) site: `pip install mkdocs && mkdocs serve`.

## Repository layout

```
src/UltrafastEngine.Core     the engine (C#, net10.0 + net48, no NuGet dependencies)
src/UltrafastEngine.Cli      the `ultrafast` command
src/UltrafastEngine.Native   C API (include/ultrafast.h)
python/                      the `ultrafast` Python package
plugins/                     fitting, zemax, gpu, hitran
schemas/                     JSON Schemas of system files and run specs
docs/                        documentation, tutorials, examples, paper replications (docs/papers), synthetic test data
tests/components/                every component against its analytic answer, with plots
tests/                       C# test suite (no external framework)
build/, build.sh, build.bat  MSBuild helpers and the release packager (docs/releasing.md)
CLAUDE.md, .claude/          Claude Code setup (docs/claude-code.md)
```

## Status

UltrafastEngine is in beta: the file formats are versioned (older files are migrated on load), the core is
feature-complete for its scope, and the test suite pins the physics to analytic results and reference codes. What
is planned next (coating and crystal design plugins, more paper replications, waveguides and fibre gain, deeper OpticStudio integration) is on the [roadmap](docs/roadmap.md). Changes
are listed in the [changelog](CHANGELOG.md).

Contributions are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md). Licensed under the [MIT licence](LICENSE).

## About this project and AI assistance

UltrafastEngine was written with substantial help from AI: the code, tests and documentation were developed with
**Anthropic's Claude Opus 5.5** (Medium and High reasoning effort), working under the direction and review of the
author. The physics is checked against analytic results and an independent code (pyNLO) by the test suite, but as
with any simulation tool, validate the results that matter to you against your own measurements before relying on
them.
