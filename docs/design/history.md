# Development history

This page records how the engine was built, phase by phase, up to 2.1.8-beta (for what comes next see the
[roadmap](../roadmap.md)). UltrafastEngine 2.0 was developed in phases. Each phase ends with tests that pin the new physics to analytic
results or another code. Status as of **2.1.8-beta** (Stage 1 complete).

Order from here (decided 2026-10-16): **Stage 1** finishes the core engine and its documentation (released as **2.1.8-beta**, the stable core);
**Stage 2** is the fitting plugin with `transform` elements; **Stage 3** is the Zemax plugin. The live-data plugin and
ML/PINN plugins follow after the stable release. Fitting, Zemax and live data are *plugins*: installed separately,
loaded through public interfaces, never required by the core.

## Phase 0 -- the split and the contract  (done)
- [x] Zemax-free C# core (net10.0 + net48), zero dependencies; own JSON, expression compiler, FFT, NPZ writer
- [x] System file (`.ufs`) and run spec formats, reader/writer, validation
- [x] Model registry with data / expression / callback forms; overrides (run > system > data > material > engine) and provenance report
- [x] Material library (Sellmeier/Ciddor/gases, absolute index), table/expression/callback indices
- [x] v2 L0 physics ported into compile -> execute (group-delay linear operators, RK4IP, grid planner, diagnostics)
- [x] C API (`ultrafast.h`, [UnmanagedCallersOnly], NativeAOT-ready), Python package (ctypes / hostfxr), CLI
- [x] Zemax binding rebuilt on an `IOpticStudio` abstraction: sampler, system builder, fake OpticStudio, record/replay, stubs
- [x] Test runner (C#), pytest suite, docs site, examples

## Phase 1 -- the physics you see in the lab  (done in this release)
- [x] Fresnel s/p, thin-film transfer matrices (stacks, quarter-wave notation), ideal/AR/GDD/tabulated coatings, grating efficiency
- [x] Losses with exact per-mechanism budget: Fresnel, coatings, clipping (Gaussian on apertures, vignetting), surface scatter (TIS), absorption
- [x] Jones polarisation with 3D s/p basis transport from traced ray vectors; waveplates, polarisers, rotators
- [x] LIDT margins and peak fluence/intensity per surface; critical-power warning
- [x] Adaptive ERK4(3)-IP stepper (FSAL) besides fixed RK4IP
- [x] Full vector chi(3) coupling (XPM 2/3 + coherent term)
- [x] User time-domain responses n(t, I) with co-moving and lab time; replace/add modes
- [x] Carrier-resolved real-field propagation (automatic where a model asks for it)
- [x] Instruments: energy meter, spectrometer, scope/photodiode, AC/IAC, FROG (SHG/PG), d-scan, contrast, polarimeter, pulse summary, ideal compressor
- [x] pyNLO cross-check
- [x] Zemax export: AOI (RAID), normals (RENA/B/C), coating rules (+ optional operand read-out), semi-diameters, beam radii, environment

## Phase 2 -- active media and spatial physics  (plan: docs/design/phase2-active-media.md)
- [x] **2.1** foundations: per-pulse grids with exact carrier change, internal multi-field propagation, auxiliary beam ports (parameters / previous run / callback), by-products, signed energy budget, user elements
- [x] **2.1** spatial: radial (Hankel) and cartesian transverse models with diffraction per element, profiles carried between elements, lenses and curved mirrors, beam diagnostics (M2, true peak fluence)
- [x] **2.1** χ(2) / OPCPA: `crystal` element (SHG/SFG/DFG/OPA, non-collinear, type 0/I/II, QPM), crystal library, phase-matching calculator, superfluorescence seeding, pyNLO cross-check
- [x] **2.2** gain: Yb:YAG, Ti:sapphire, Nd:YAG and user media, saturation (spectral Frantz-Nodvik, time-ordered), pump models and rate equations, pulse-train steady state, thermal lens, multipass and regenerative amplifiers, spatial gain; position-dependent material properties
- [x] **2.3** solid-state oscillators (parametric, Haus and cavity models; Kerr-lens and SESAM mode-locking; soliton, multi-pulse, cw-breakthrough and QML diagnostics); broadband χ(2) in the carrier-resolved mode, ASE, thermal lens to OpticStudio (Gradient 1)

## Phase 2.5 -- connecting to the lab  (done; fitting moves to a plugin in Stage 1.1 and is rebuilt in Stage 2)
- [x] Instrument sampling: near field / far field (focal plane of a lens), pinhole, iris, fibre (NA check), slit, point, off-axis; on carried profiles or a Gaussian beam model
- [x] Measured data on any instrument: CSV / NPZ / JSON with units from column names, background, normalisation, per-point or relative σ; curves, 2-D traces (FROG, d-scan, cameras) and scalar readings; χ², rms error, plots
- [x] Calibration fit: Levenberg-Marquardt over paths into the system / run files, frozen grid, covariance, correlation and identifiability warnings; predictions with 1σ for every instrument and element, measured or not

## Stage 1 -- stable core (done: 2.1.8-beta)
Guiding use case: reproducing high-dynamic-range contrast measurements of a Yb amplifier -> telescope -> MPC ->
compressor chain (energy-dependent pedestal, asymmetric post-pedestal), with every candidate mechanism switchable.

### 1.1 Architecture  (done: 2.0.0-alpha.2)
- [x] Plugin system: `IPlugin` + manifest, discovery (`plugins/` folders, `ULTRAFAST_PLUGIN_PATH`), `ultrafast plugins list|install|remove`, Python discovery; plugins register elements, instruments, models, solvers, run modes and CLI commands
- [x] Zemax binding moved to `plugins/zemax`; current fitting/comparison code moved to `plugins/fitting` (kept working until Stage 2)
- [x] Option registry: every element / instrument / model declares its parameters (type, unit, default, description) once
- [x] JSON Schema v2 for `.ufs` and run specs generated from the registry (`$schema` for editor autocompletion); `ultrafast schema`, `ultrafast validate` against it
- [x] Schema-versioned migrations (v1 -> v2) in the loader and `ultrafast migrate`
- [x] Unique instrument names (auto-suffix + warning); many identical instruments anywhere
- [x] One `camera` instrument: imaging `near` / `far` / `relay` (lenses and gaps) + sensor model (pixels, QE(λ), exposure, gain, bit depth, read/dark noise, saturation, hot pixels, background); `nearFieldCamera` / `farFieldCamera` migrate

### 1.2 Optical components (non-ray-traced models of what each component does to the beam)  (done: 2.1.8-beta)
- [x] Gaussian beam tracking (astigmatic q-parameters x/y) through every element in 1-D: lenses, curved mirrors and gaps update the beam of later elements automatically (principal axes x/y only; thermal lenses of gain media not yet in the tracking)
- [x] Mirrors: off-axis astigmatism of a curved mirror, Zernike aberrations and surface figure on spatial fields, GDD-ripple coating models
- [x] `ndFilter` (OD or OD(λ), absorptive / reflective, substrate dispersion and nonlinearity, ghosts)
- [x] Thick (multi-order / zero-order) waveplates with material dispersion; thin-film polarisers at AOI; Faraday rotator (built-in TGG data approximate)
- [x] Ghost reflections of plates and windows (post-pulses, and their nonlinear pre-pulse partners)

### 1.3 Contrast and polarisation  (done: 2.1.8-beta)
- [x] Third-order cross-correlator instrument (SHG-gated 3ω correlation, 10^10-10^12 dynamic range, resolution, noise floor, scan grid, polarisation selection)
- [x] Input pulse pedestal models: ASE (incoherent), spectral phase noise, pre/post pulses
- [x] Vector (Jones) propagation on spatial grids (NER resolved across the beam)

### 1.4 Gases  (done: 2.1.8-beta)
- [x] Rotational Raman of molecular gases (N2, O2, air, CO2, H2), isotropic + anisotropic parts (acts on NER); molecular constants from the literature, dephasing times approximate
- [x] refractiveindex.info database reader (all formulas + tabulated n,k), bundled subset, import tool, p/T scaling

### 1.5 L3 propagation  (done: 2.1.8-beta)
- [x] UPPE on radial / cartesian grids (full k_z dispersion, non-paraxial, space-time focusing)
- [x] Ionisation (PPT / ADK, Keldysh, multiphoton) and plasma (Drude, avalanche, recombination, absorption)

### 1.6 Speed and studies  (done: 2.1.8-beta)
- [x] SIMD complex kernels (Vector128: SSE2 / NEON on .NET 10); mixed-radix and Bluestein FFT
- [x] Compute back-end interface above `IFft` (whole segment on the device); GPU back-ends are left to plugins (none shipped yet)
- [x] Parameter scans (grid, random, LHS, Sobol), optimisation, sensitivities (local, Sobol); prefix caching; parallel batches (pipelining of elements across points deferred)

### 1.7 Pulse retrieval  (done: 2.1.8-beta)
- [x] FROG (PCGPA, ePIE) and d-scan retrieval behind `IRetrieval`; retrieved fields usable as run inputs

### 1.8 Consolidation  (done: 2.1.8-beta)
- [x] docs/reference, docs/tutorials, docs/examples (runnable, CLI + Python, a few C++), docs/assets (realistic synthetic data from a hidden "true" system with an answer key)
- [x] Every option, function, extension point and override documented; agent-readable docs (`llms.txt`, `AGENTS.md`)
- [x] Python 3.10+: full type hints (inline, `py.typed`; no separate stubs needed), typed dicts from the schema (`ultrafast.types`), generated API reference

## Stage 2 -- fitting plugin with `transform` elements
- [ ] `transform` element: a parameterised operator on the complex field -- spectral amplitude / phase, temporal modulation, Jones matrix per frequency, energy, spatial phase (Zernike), pointing, general (low-rank) matrices, user / ML operators -- so spectrum, pulse, contrast and beam stay mutually consistent
- [ ] Constraints: passivity, causality (Kramers-Kronig), smoothness; identifiability report
- [ ] Replaceable estimators (LM, regularised GN, CMA-ES, ensemble Kalman, MCMC; user plugins); instrument-response corrections
- [ ] Results in three layers: simulated -> inferred -> believed, with provenance and an influence map

## Stage 3 -- Zemax plugin (OpticStudio depth)
- [ ] Zernike aberrations exported from OpticStudio applied to spatial fields (`IAberrationModel`)
- [ ] Paraxial ABCD matrices between elements from paraxial-ray operands (exact transverse transport)
- [ ] Prism pairs and Martinez stretchers with lens aberrations as analytic elements
- [ ] Output basis vectors in results (lab-frame polarisation after folded paths)

## After 2.0 (plugins and breadth)
- [ ] Live-data plugin (streamed recordings, a digital twin that re-fits as data arrive)
- [ ] ML / PINN estimator and surrogate plugins
- [ ] Branches (splitters, parallel paths, coherent recombination)
- [ ] Wheels for Windows/macOS/Linux on PyPI, NuGet package for the core
- [ ] OpticStudio User Extension (menu-integrated) in addition to the Interactive Extension

Ideas and priorities are tracked in issues; contributions welcome (CONTRIBUTING.md).
