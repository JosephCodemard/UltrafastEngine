# Development history

Internal page ([folder index](README.md)). This page records how the engine was built, phase by phase and session by session, with the
findings and numbers of each (open work: [roadmap-internal](roadmap-internal.md); public summary: [roadmap](../roadmap.md)). The intro below
was written at 2.1.8-beta. UltrafastEngine 2.0 was developed in phases. Each phase ends with tests that pin the new physics to analytic
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

## Phase 2 -- active media and spatial physics
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

## Stage 4 -- engine depth before branches  (2026-10-29 to 30)

Done in this order, each stage building on the one before: review of tests with no external reference (T0); absorbing boundary, Hankel cache and cw source (T1.1); Gerchberg-Saxton and near fields (T1.4); ray-traced dispersion, `prism`, `spaceTime`, angular dispersion on profiles (T1.3); thermal lens, stress birefringence, partial coherence, resonators (T2). New tracker state stayed inside `BeamState`, so each edge of a graph could later own a clone, and partial coherence stayed a run mode around the executor, so branches do not depend on it.

- [x] Diffraction validated against the Fresnel integral; absorbing boundary (linear and nonlinear elements); profile-only linear path; cheaper Hankel matrix
- [x] Beam tracking: thermal lenses, Kostenbauder couplings, ray bundle for dispersion to fourth order, `prism`, angular dispersion on carried profiles
- [x] Sources and masks: Hermite-Gauss, Laguerre-Gauss, Bessel-Gauss, Airy, measured near fields (NPZ, PNG), cw; axicon, zone plate, vortex, slits, maps; Gerchberg-Saxton
- [x] Cavities and coherence: `resonator` (eigenmode, Fox-Li), Gaussian-Schell partial coherence, stress birefringence, amplifier thermal lens
- [x] Trust: `build/example_check.py`, Linux CI, generated validation matrix

## Stage 5 -- branched systems  (from 2026-10-30)

- [x] Ports on all element types, `PathLedger`, `SystemGraph` with verification, Sequential and Branched forms
- [x] Interference in 1-D, packets, loops, crystal ports, `beamSplitter`, `delay`, virtual inputs
- [x] An independent physics review of the T1 and T2 work, with its findings fixed
- [x] Space, the splitter family, Gouy and crystal phases, tail per path, node cache, array studies, modelled instruments

## Stage 6 -- review of T0 to T2 and branches  (2026-10-31)

Every spatial model and the branched interferometers were compared with LightPipes (an independent Fresnel code, development use only), and branched arms with pyNLO, which turned out to run on Python 3.12 once `jsonpickle` is installed.

- [x] Found and fixed: M² and second-moment radius of tilted, decentred and higher-order beams; `steadyState` loops claiming convergence while decaying; `graph.energyOutJ` hiding separate packets; unbooked packets at crystal ports; the second arm's tail lost when carried profiles merged
- [x] Tests: `test_lightpipes.py`, `test_pynlo_branched.py`, `ReviewRegressionTests`
- [x] Examples 32 to 34 moved to the Python API; 35 to 38 added; tutorial files moved to `docs/tutorials/files/`

## Stage 7 -- adaptive optics, temporal shaping and the digital twin  (2026-11-09 to 10; five sessions S1-S5, merged to `main`)

Owner decisions: transverse shaping first, temporal shaping too; "Zernike" means modes; the fibre array is a coherent tiled-aperture array; every device is parametric (no vendor models);
up to a full 1920 x 1080 SLM; the twin is its own plugin on top of fitting; CI on Linux (x64, arm64) and Windows, macOS by hand; Apache-2.0.

- [x] **S1 foundation:** Python package split by what things are (shims keep every 2.2.0 name), `ultrafast.plotting` with one theme and `animate`, Zernike decomposition, examples regrouped into folders with `make_all.py` / `make_figures.py`, gallery generator
- [x] **S2 core:** `modulator` element (cells, fill factor, gaps, quantisation, cross-talk, chromatic rules), in-memory maps, `transverse.frequencyBlock`, `outputs.keepProfiles`, coherent tile source, slope maps from `wavefrontSensor`; scale spikes (a 1920 x 1080 panel runs; the memory peak is the .NET GC, 2.7-3.4 GB)
- [x] **S3 `ao` plugin:** `deformableMirror`, `slm`, `spectralShaper`, `aopdf`; `ultrafast_ao` (DM bench, tiled array, combining metrics); holography, control and sensorless algorithms; a physics review (15 findings, 9 fixed)
- [x] **S4 fitting and twin:** fitting across conditions (held-out conditions, `dependence` kinds up to network, surfaces), the `twin` plugin (project, validation, planner, what-if, drift tracker, goals, benchmark)
- [x] **S5 examples and docs:** 7 AdaptiveOptics, 2 DigitalTwin, 3 Fibres examples; twin docs

## Stage 8 -- closing gaps toward the first release  (2026-11-11; sessions A-F on `dev/2026-11`)

- [x] **WP0 (A):** `UF_WITH_AO` in the native library, grid-planner hook for shaper side pulses (widens the window or warns), unknown-element message names `ao`, tests for Littrow dispersion, Bolduc and double phase through an SLM, tiled-array lobe fractions
- [x] **WP1 (B) thermal lenses in beam tracking:** distributed (quadratic-index) lens in the ABCD tracker, applied per pass in multipass and per round trip in regenerative amplifiers, effective focal length reported
- [x] **WP4 (B) plasma in envelope mode:** frequency-resolved Drude operator (`ionization.frequencyResolved`, default true), `electronDensityM3` for a prepared plasma; envelope against carrier-resolved within 1.2 % (blue shift) for MPI. The frequency-resolved shift is up to 5 % below the single-carrier formula for a 30 fs pulse at 1030 nm (the component check states both)
- [x] **WP2 (D) gain:** `aseModel` (steady-state forward / backward ASE per transverse cell and frequency, depletes the inversion between pulses; checked against n_sp h nu dnu (G-1), Frantz-Nodvik, saturated-amplifier law, cw photon balance); four-lobed stress-birefringence Jones map on cartesian grids (beam average equals the lumped value to 1e-6)
- [x] **WP3 (C) material catalogue:** all material, gas, Raman, gain and chi(2) data in embedded JSON with source, status and uncertainty; runs list every `approximate` / `unverified` value used (`summary.json` `unverifiedInputs`); `physics.strictMaterials`; TGG index refitted to the Schlarb-Sugg table (old fit up to 0.0106 off); 43 of 183 quantities `verified`; the YVO4 vendor sets stay (5e-4 / 9e-4 from Shi et al. 2001, recorded)
- [x] **WP5 (A) decisions:** no compile cache (a 128 x 128 trial costs 10 ms including compile), no full streaming executor (revisit for 4096 x 4096), tiles checked against a Branched balanced tree in a test; triggers in `docs/reference/speed.md`
- [x] **WP6/WP7 (A, E) findings:** `ny = 1` grids; cw line built at the requested carrier (cw against pulse agree to 1e-9); SPGD gain normalised by the running maximum (default reaches >= 0.9 over metric scales 1e-3 to 1e3; Spall sequences not built); Raman soliton shift rate explained (Gordon's first-moment approximation; engine within 2 % of the exact-gain rate and 0.3 % of pyNLO); SLM against Fourier explained (panel half a pixel off the grid; new warning); tiled-array reference explained (under-sampled lens, lock now at f = 1 m)
- [x] **WP8 (F) release system:** components and manifest, `install.py` (release and git sources, install, add, upgrade, uninstall), packager `--components`, ZOS-API gate, `release.yml`, 23 tests (see [releasing](../releasing.md))
- [x] **Polish:** README picture grid and contact badge, logo palette and banner on every docs page, CI timeouts and apt retries, plasma component check split into carrier and frequency-resolved rows
- [x] **Follow-up (PR #1):** avalanche / inverse-bremsstrahlung double count fixed; catalogue defaults for user gain media and solids without data; `strictMaterialsAllow`; partial-override notes; Keldysh far-wing NaN. Spall gain sequences judged unnecessary (the running-maximum normalisation covers the range 1e-3 to 1e3)

## Stage 9 -- the road to 1.0.0: core fixes and leftovers  (2026-11-16; sessions WP-D and WP-E on `dev/2026-11`)

The plan for the first public release had seven work packages (WP-A agents / MCP, WP-B docs, WP-C(A) speed on the Mac, WP-C(B) GPU on Windows, WP-D core fixes, WP-E leftovers and Zemax, WP-F release); its owner decisions are in [decisions.md](decisions.md).

**WP-D (core fixes).**
* `n2Scale` in a 1-D element scaled only the Kerr n2; the rotational Raman of gases (air gaps of a multipass cell) kept its full strength, while the spatial model already scaled it.
  Now every third-order response of the element is scaled (Kerr, delayed Raman, XPW, rotational Raman); user `responses` are not, and the element note says so.
* Found while testing: the element's own `n2` key was ignored (only run-spec overrides were read). Fixed, with a regression test.
* **Kerr lens in the 1-D beam model** (`kerrLens: off | peak | pulseWeighted`, `kerrLensModel`): the Gaussian beam of a bulk element self-focuses with thin Kerr lenses of the pulse's power
  (second-moment radius, Ermakov equation). Reference: the exact solution w² = w0²[1 + (1 − P/P_c)(z/zR)²]; the model's P_c is 1.06 × Marburger's. Limits: one power per element (entry), the
  beam leaving the element is not carried on, off by default. Tests: `KerrLensTests`, `GasTests.N2Scale_In1D_ScalesTheRotationalRamanToo`.
* **D3 sweep** (a bug-hunter agent in its own worktree): an element's own `raman` / `fR` were read only through overrides (fixed; the same pattern as `n2`); `thinFilmPolarizer.bandPosition` was read but not declared; crystal elements dropped ~1e-6 of the energy at the output resampling without booking it (now the mechanism `grid boundary`); a list of keys with a unit missing from their name went to the roadmap (unit-in-key renames). Slowest tests: 31 s (`SingleShotCorrelatorTests.SpatialSfgOfTwoCrossedBeams...`) and 20 s (`BeamSourceTests.BinaryPhaseZonePlate...`); the whole suite takes about 3.5 minutes.

**WP-E (leftovers and Zemax).**
* Python: `result.unverified_inputs` (typed) and `result.ase_pedestal()` (one stage's inter-pulse ASE as the next stage's `pulse.pedestal`).
* A lens or curved mirror whose phase changes by more than π per grid cell at the beam edge warns that it is under-sampled.
* Fuchtbauer-Ladenburg check of the gain catalogue: Yb:YAG's emission fit reproduces its lifetime to 6.7 %; Nd:YAG's is one 1064 nm line that carries about 8 % of 1/τ (documented in the catalogue notes; the missing lines need the Stark-level spectrum).
* The SPGD examples use the default scale-free step.
* New examples: `AdaptiveOptics/component-gallery` (the four `ao` elements against analytic answers; the deformable-mirror sensor reading is 8 % below 2 × poke for a cause not traced, noted in its README) and `DigitalTwin/dependence-models` (proportional, quadratic, Chebyshev and constrained-network energy laws on a saturating lab: only the Chebyshev series passes inside and outside the training range; the network is sensitive to its size and start). Fixed on the way: the twin plugin crashed predicting from a fit whose reported width was `null`. `UF_WITH_AO` in the native library was already done in Stage 7 (the WP-E spec was stale).
* **Zemax** is documented as experimental and never verified against OpticStudio: out of the core narrative, one status box on its plugin page, `plugins/zemax/HARDWARE-CHECKLIST.md` for the first run, `TODO(zemax-hw)` markers on the unverified ZOS-API assumptions, and an unreadable lens unit is now an error instead of a silent default to millimetres.
* State after both: C# 614 pass / 0 fail / 9 skipped; Python 265 pass / 2 skipped (after D3); strict examples 0 problems; `mkdocs --strict` clean. net48 under Mono was not run (Mono is not installed on the owner's Mac).

## Stage 10 -- speed and GPU  (2026-11-16; sessions WP-C(A) on the Mac and WP-C(B) on Windows, `dev/2026-11`)

**WP-C(A), speed on the Mac** (owner approved the conservative fidelity bundles).
* **`fidelity`** run key (`full` / `standard` / `quick`, CLI `--fidelity`): `Specs/Fidelity.cs`, applied in `RunSpec.FromJson` to unset keys only; `fidelityReport` in `summary.json`; unreliability warnings from cheap indicators (B, P/P_cr, ...). Page `docs/reference/fidelity.md`. Tests `FidelityTests`.
* **Studies:** grid union over the range ends, instruments skipped when no output reads one, point cache (`"cache": folder`) with resume, Python `uf.scan` (lazy, shaped). A study is 5.7x faster than a loop on the multipass cell. Tests `StudyCacheTests`.
* **FP32 study:** `cpu-fp32` back-end (`Numerics/Fp32Emulation.cs`). The operator in single precision is good to 1e-9 (energy, spectrum) but fills the contrast floor below about -85 dB, so mixed precision is refused above about 70 dB of dynamic range.
* `tools/bench` (developer benchmark, not in the solution) and `tools/bench/golden-osx-arm64.txt` (outputs of the five baseline cases before any change).
* Measured and **deliberately not built** (reasons and revisit conditions in `docs/reference/speed.md`): linear fast path, CPU ensemble batching, pooled buffers / SIMD Hankel. No measured case is slow (20 ms to 0.5 s).
* C# 622 pass / 0 fail / 9 skipped (GPU and HAPI gates); Python 266 pass / 2 skipped; 39 example specs strict; `mkdocs --strict` clean.

**WP-C(B), GPU on Windows + NVIDIA** (RTX 3060 Ti, driver 610.88, CUDA 13.3; NVRTC 12, cuFFT 11, cuBLAS 12; OpenCL from the NVIDIA driver; run by Claude Code on the owner's machine).
* Self-test on CUDA and OpenCL: relative errors 2e-16 to 8e-15. Benchmark (double precision; CPU = 20 cores): scalar N = 8192 0.75x (CUDA) / 1.06x (OpenCL), N = 65536 4.6x, vector 32768 4.9x / 6.9x, radial 1024 x 64 7.8x / 8.3x, cartesian 512 x 1024 6.8x / 6.3x.
* **Kernel 1**, gas rotational Raman on the device, 1-D and spatial (`IPaddedKernelResponse`: the CPU's zero-padded causal convolution); MPC air gaps no longer stay on the CPU (unless they ionise: fixed in Stage 11).
* **Kernel 2**, modulator / tile source: parity extended (realistic panel, vector pulse), no code change needed.
* **Kernel 3**, time responses compiled to kernels: `expression` (all functions and variables incl. `F`), `twoPhotonAbsorption`, `kerr` (`CompiledExpression.ToC`, `Physics/DeviceResponses.cs`, `UserResponseKernel.cs`). Callbacks stay on the CPU.
* **Kernel 4**, χ(2) 1-D coupled waves (`Chi2Stepper.cs`); the device stepper lands on QPM walls like the CPU.
* **Study ensembles** (`Numerics/Ensemble.cs`, `EnsembleStepper.cs`, study key `ensemble`): 3.5-6x faster than a stream per point; level with a 20-core CPU at N = 4096, 1.6x faster at 16384 on this FP64-limited card.
* **Mixed precision** (`compute.precision: mixed`): 1-D and χ(2) operators with FP32 FFTs; energy 1e-6 / spectrum 1e-4 met on SPM plate, 20-pass MPC, soliton, SHG (worst: energy 8e-8, spectrum 1e-6); 1.3-1.5x faster; refused above 70 dB of instrument dynamic range.
* Findings: the GPU parity tests used to pass when a device run had failed and fallen back to the CPU (fixed); domain-resolved QPM at the default tolerance is only ~7e-4 accurate in field because of sliver steps at walls (CPU too; fixed in Stage 11); fixed steps ignore QPM walls (now a warning).
* C# `--slow` 642 tests (16 `GpuTests`): 641 pass, 1 skip (HAPI); Python 267 pass / 1 skip; 41 example specs strict; `mkdocs --strict` clean.

## Stage 11 -- WP-1: design docs, a user's multipass-cell report, physics questions  (2026-11-17, `dev/2026-11`, Windows + RTX 3060 Ti)

The session started from a discovery pass over every planning document and split the remaining work into three packages (WP-1 engine and docs,
WP-2 Python surface, WP-3 docs and release) plus WP-1b (memory safety, moved to its own session); see [roadmap-internal](roadmap-internal.md) §0.

* **Design docs:** twelve files and `HANDOFF.md` merged into the seven current ones of this folder; the folder left the documentation site.
* **A user's 46-pass cell report**, each item reproduced or answered:
  validity is now machine-readable (`summary.json` `validity`, `physics.strictValidity`); the "zero frequency" warning on pass 1 was a false positive
  (any energy deficit, solver error included, was reported; the check now needs the spectrum at omega <= 0); warnings name every pass, not the first;
  B has pulse- and beam-averaged values beside the on-axis peak; the FWHM of a multi-peaked pulse is flagged (`peakCount`, `fwhmEnergyFraction`);
  ionisation that cannot matter is skipped (a conservative bound: 4 x the entry peak intensity over 4 x the effective duration, phase < 1e-6 rad,
  energy < 1e-9) and ionisation that matters runs on the GPU (rate table + one-work-item time scan + spectral Drude FFT; MPI exact to 1e-16, PPT /
  Keldysh 4e-6 in the ionised density because the table is cubic across channel-closing kinks; an adaptive run may then take one step more, 5e-5 in
  the field); the radial model through 46 `sameAs` passes equals the passes written out (`SameAsTests`). Not done: carrying the Kerr-lensed 1-D beam to
  later elements (roadmap §2).
* **Physics questions answered from the code** (recorded in `limitations.md`): free-electron plasma in solids (yes: Keldysh, avalanche, Drude; not in
  chi(2) crystals); circular light on an oblique mirror (elliptical with a real surface: new test against Fresnel at 20-70 deg; an ideal mirror keeps it
  circular); self-steepening (on by default, tau_shock = 1/omega0); thick optics (plates, thick waveplates, TFP substrates, Faraday crystals, lenses with
  `thicknessM`, and now polarisers with `material` + `thicknessM`; beam splitters have no glass). Fixed on the way: `ionization` and `kerrLens` were
  dropped on plates (the "key read only through overrides" pattern in another form).
* **QPM sliver steps:** steps ending within 10 % of a wall land on it; tolerances 1e-7 apart now agree to 3e-8 (was 3e-5). The default tolerance is
  still 4e-4 from converged over ~100 walls: the remaining error is ordinary global error, not slivers.
* Verified: C# `--slow` 655 pass / 0 fail / 1 skip (HAPI; 17 `GpuTests` on CUDA and OpenCL); Python 268 pass / 1 skip (pyNLO, LightPipes);
  `mkdocs --strict` clean; generated files current; examples strict: two READMEs differ from a Windows run in the last quoted digit
  (`AdaptiveOptics/tiled-array` 0.878 vs 0.879, `Diffraction/iris-diffraction` 0.45 % vs 0.46 %), with 0 numbers changed against the previous
  commit's engine: platform rounding of numbers recorded on macOS, not a change (READMEs left as they are).
* Moved to WP-2: Lorentzian / asymmetric-exponential TBP shapes, PM calculator `noncollinearDeg` for `sfg`. Moved to the roadmap: a study schema,
  glass in beam splitters, exact PPT / Keldysh rates on the device.

## Stage 12 -- WP-2: calculators, plotting and schematics  (2026-11-17, `dev/2026-11-wp2` merged into `dev/2026-11`, Windows)

Built in a git worktree (`../UltrafastEngine-wp2`) while another session worked on `dev/2026-11`; two commits (part A, part B). MCP (part C of the
brief) was split off as WP-2b.

* **Calculators (A):** `ultrafast.calc` became a package (`pulse`, `crystal`, `cell`, and the new pure-Python `beam` and `nonlinear`; decision E7: they work without the engine, tested by
  making the engine unavailable in a subprocess). The old module names stay as re-exports (`ultrafast.crystal`, `ultrafast.cells` keep every 2.2.0 name: `test_structure`
  snapshots them). Keywords carry their unit; the old ones warn (`calc/_compat.py`). Engine errors are `ValueError`. TBP shapes `lorentzian` (field 1/(1+(t/tau)^2), 0.1420) and
  `asymmetric-exponential` (one-sided exponential, 0.1103), checked against the table and a numerical Fourier transform. The phase-match calculator already accepted
  `noncollinearDeg` for `sfg` (the roadmap item was done in WP-1 or earlier): tested, no change. Marburger's `z_sf` uses `z_R = pi n0 w0^2 / lambda` (checked against published
  statements of the formula), `P_cr = 3.77 lambda^2 / (8 pi n0 n2)` as the engine's `criticalPower` check.
* **Plotting (B):** `use_style` (4 styles, call and context manager, `ULTRAFAST_PLOT_STYLE`, `--style`, `--column`); palettes validated with the dataviz validator (the `ultrafast`
  light palette has three colours below 3:1 on the light surface: labels are needed, documented). Paper figures wider than 9 in keep their size (a dashboard shrunk to 7 in
  overlaps its text) and are saved under 4800 px across. Schematics: `plotting/graph_layout.py` (pure layout, golden tests) and a new `draw_graph`; `SystemGraph.ToMermaid`
  with types, virtual ports, loops. `spectrogram_3d` / `spectrogram` / `animate_evolution` (a probe stores the spectrum and the spectral phase, not a time-domain phase:
  its envelope is rebuilt on a frequency grid). `docs/reference/plotting.md` with before / after images from `python/tools/make_plotting_docs.py`.
* **Verified:** Python 304 passed, 12 skipped; C# filters `PulseCalculator`, `Branching`, `Crystal`, `PhaseMatch` pass; solution build 0 errors; `mkdocs --strict` 0 warnings.
  **Not run before the merge:** the full C# suite (`dotnet ... UltrafastEngine.Tests.dll`, ~5 min) and net48 were skipped for time on 2026-11-17. Run only: `PulseCalculator`, `Branching`, `Crystal`, `PhaseMatch` filters (all pass), the solution build (0 errors) and the whole Python suite (304 passed, 12 skipped, `mkdocs --strict` 0 warnings). If the full suite fails later, suspect first the C# changes of WP-2: `PulseCalculator.cs` (two new TBP shapes in `Shapes`, which the CLI and the `shapes` op list), `SystemGraph.cs` (`Types`, `ToMermaid`).
* **Not done:** `mypy` on the new modules (not installed in the venv); `PROJECT_STATUS.md` counts not updated (test counts changed: +about 50 Python, +3 C#);
  the paper `run.py` scripts still use the old keyword names (they work, with a `DeprecationWarning`).

## Working notes that still help

* **Bug pattern** seen four times: a documented element key read only through run-spec overrides and never from the element's own JSON (`n2`, `raman`, `fR`, `selfSteepening`). When adding an element key, read it with the `Num/Bool/Str` helpers (element JSON **and** override), and test that the element key equals the same value set on the material.
* **Honesty rules that worked:** a paper disagreement is a finding, never tuned away; a KNOWN mark in a component check must have a repro; unverifiable numbers are UNVERIFIED and soft; a test tolerance is never loosened to hide a failure.
* **Parallel agents:** implementers in separate git worktrees (`isolation: worktree`), each on a distinct part of the code; the lead merges and regenerates the generated files once. A subagent started inside a worktree session cannot run commands in the main checkout (and vice versa).
* **Python environment:** export `ULTRAFAST_LIBRARY` and `PYTHONPATH` in the same shell command as the Python call, or a stale `bin/native` library is used. Rebuild with `python/tools/build_engine.py` after any C# change, never while another job uses the library (other Python processes can lock `python/src/ultrafast/bin` on Windows: retry after they finish).
* **Windows machine (owner's):** `python3` is the Microsoft Store stub, use `python` from `../.venv`; HTTPS certificate checks fail for git and pip because the antivirus inspects TLS: the repository has `http.sslBackend schannel` (local config), and pip takes `--cert` with a bundle of the Windows roots exported from `Cert:\LocalMachine\Root`.
* **macOS machine:** no Mono (net48 runs only in CI); `pdftoppm` is not installed, so PDFs are read with `pypdf` or PDFKit; the sandbox denies the `.venv` interpreter for subagents (plain `python3` works with `PYNLO_SRC`).
* **Licensed PDFs** in `docs/papers/*.pdf` are git-ignored; several were scans with garbled OCR (Michelson-Morley, Perot-Fabry, Treacy, Shimizu): unreadable numbers are UNVERIFIED in each `paper.json`.
