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

## Stage 12 -- WP-1b: memory safety  (2026-11-17, `dev/2026-11`, Windows + RTX 3060 Ti)

Trigger: a user's fit of a 46-pass multipass cell ran out of memory after more than 2 h. Cause, read from the code: the fitting residual cache kept
up to 65 full `RunResult`s (every element's pulse and profile), the Jacobian n more, and nothing estimated or limited memory.

* **Fitting** (`Fitting.cs`): the cache holds `(residuals, cost)` with the old eviction rule (so the same evaluations happen); `Track` keeps the latest and best
  `Eval` in full and slims every other one (`Eval.Slim`: `At`, `TailAt`, snapshots, `Output`, `Input`, `Compiled` dropped; instruments and records stay, `Predict`
  reads them); Jacobian runs are slimmed at once; evaluations use `keepProfiles: used` unless the spec sets it. Golden values were recorded from the old code
  first: 28 evaluations, 6 iterations, chi^2 476.58415279612495, all parameters, sigmas and predictions bit-identical afterwards. The retention test counts full
  results alive after a GC at every evaluation (`Fitter.EvaluationObserver`): 28 with the old code, fewer than 5 now.
* **Core**: `Engine/MemoryEstimate.cs` (formulas in `docs/reference/memory.md`), compile note, `compute.memoryLimitBytes` (a `ModelException` at compile time; `validate` shows it as
  an error), 80 % free-memory warning (`GCMemoryInfo` on .NET, `GlobalMemoryStatusEx` / `/proc/meminfo` on net48, silent when unknown), `CompiledRun.Memory`,
  `ValidationReport.Memory`, the `memory` command. Study points run with `used` profiles (the study outputs read no profile; the nominal run keeps the user's setting).
  Finding while testing: vacuum gaps share one pulse array between elements, so a retained-memory test needs dispersive elements.
* **GPU**: `GpuContext.FreeMemoryBytes` (CUDA `cuMemGetInfo_v2`; others total minus tracked buffers), `GpuMemory` pure decisions (`Fits` 0.8 x free, `MaxConcurrent`),
  slots lowered for large problems, ensembles halved with a note, LRU (32) program cache with `cuModuleUnload` / `clReleaseProgram`, `ReleaseContexts`.
* **Python**: `uf.estimate_memory`, `MemoryWarning`, pre-flight in `scan` and in `run` of a fit, `close()` / `with` on engine handles.
* Not done: `keepProfiles used` for branched systems, instrument 2-D outputs in the estimate (roadmap §2); mypy is not installed in the Windows venv (the regen step was skipped).

## Stage 13 -- WP-2: calculators, plotting and schematics  (2026-11-17, `dev/2026-11-wp2` merged into `dev/2026-11`, Windows)

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

## Stage 14 -- WP-2b and WP-3: MCP server, release groundwork  (2026-11-17 / 18, `dev/2026-11-wp2b` merged into `dev/2026-11`, Windows + RTX 3060 Ti)

Worked in a git worktree (`../UltrafastEngine-wp2b`) because the owner's overnight simulations used the main checkout's `python/src/ultrafast/bin`; only light tests were run.

* **WP-2b, the MCP server (`ultrafast-mcp`, `python/src/ultrafast/mcp/`, decision E8).** The current MCP revision (2026-07-28) is *stateless* (`server/discover`, protocol version and capabilities in
  every request's `_meta`, `resultType`, error -32022); most clients still speak the handshake revisions (`initialize`, 2025-11-25 and earlier). The server serves both: `initialize` selects the legacy
  behaviour, a request with the modern `_meta` the new one. Layout: `server.py` (JSON-RPC, dispatch), `policy.py` (every refusal, no engine), `tools.py` (six definitions, `prepare`), `worker.py` +
  `_worker_main.py` + `ops.py` (a persistent child with a private empty `ULTRAFAST_HOME`, killed on timeout, size and memory pre-flight), `explain.py`, `resources.py`. Design points worth keeping: refusals are
  tool results with `isError` (the agent can correct the call), not protocol errors; `fidelity` for a run *file* is applied through a temporary copy next to the original (relative data paths keep working);
  `fit` / `study` / `backward` sections are refused in `run` (a fit has no time budget yet; it is the next tool); the sandbox is policy plus a separate process, not an OS jail (documented). A junction
  (Windows) counts as a link: tested with `mklink /J` because symlinks need a privilege. `ultrafast twin serve <folder>` starts it; its docstring, `plugin.json`, README and `docs/plugins/twin.md` were wrong
  (said "not implemented") and are fixed. Schemas are shipped in the wheel by `build/package.py` (`ultrafast/schemas`).
* **WP-3, release groundwork (nothing uploaded, no tag).** Version **1.0.0** in every place (`build/check_version.py` lists them: pyproject, `__init__`, `Directory.Build.props`, CITATION, the engine constant, six
  plugin.json and C# constants); the 2.x numbers were internal and never released. `release.yml`: `version` job (tag = files; `vX.Y.ZrcN` builds `X.Y.ZrcN` wheels through `UF_WHEEL_VERSION` in
  `package.py`), `light-tests`, `twine check --strict` and the ZOS-API gate on the extracted wheels, `testpypi` / `pypi` jobs with `id-token: write` behind the environments of the same names (E4), no sdist (E5),
  Linux native wheels left out of the upload (built on the runner's glibc, not in a manylinux container). **PyPI name: `ultrafast-engine`.** The owner preferred `ultrafast`; on 2026-11-17 it had no project on PyPI
  but is taken on TestPyPI by another author, which would make the rc rehearsal impossible (E13). Install docs (`docs/install.md`, `python/README.md`) and the owner's steps (`docs/releasing.md`).
* **WP9 slice 1:** `docs/examples/_template.md` (excluded from the site), `example.json` in the 12 S5 examples, `build/example_lint.py` (structure and metadata, never runs an example; in CI and in `release.yml`);
  `temporal-shaping` got the missing Layout label. `component-gallery`, `dependence-models` and `transforms` do not follow the structure yet (wave 2).
* **Public roadmap** in three blocks (`docs/roadmap.md`); `ROADMAP.md` is generated from it (`build/sync_roadmap.py`, checked in CI and by `test_wp3.py`). The old page had two diverging copies.
* **Review before the merge found four things, all fixed:** file arguments were not checked (only inline objects), so a `run.json` with `"@cb"` or an outside path passed (the document check now parses the file and resolves relative paths from the file's folder); `plot` offered `spectrogram`, which returns data (now `spectrogram_3d`); the `light-tests` job would have needed .NET and PyYAML (narrowed to `test_wp3`, `test_mcp`, the ZOS-API tests); and `twine check` had not run. **`twine check` still has not run here** (no package index reachable from this machine); the wheel was built (`UF_WHEEL_VERSION=1.0.0rc1`, 1 MB), its contents (mcp package, schemas, `ultrafast-mcp` entry point, metadata) were inspected and the installed copy answered `initialize`, `tools/list` and `resources/read`. `twine check --strict` runs in the `publish` job before any upload.
* **Tests run:** `test_mcp.py` (52), `test_wp3.py` (22), `test_release.py`, `test_packaging.py`, `test_examples_package.py`, `test_structure.py`, `test_setup_and_calc.py`: 124 passed, 1 skipped before the review fixes, 74 in `test_mcp` + `test_wp3` after them; `mkdocs build --strict`.
* **Not run (owner's overnight simulations were using the machine):** the full C# suite (`--slow`), the rest of the Python suite, the pyNLO and LightPipes cross-checks, the examples (`/example-check`),
  net48, any NativeAOT build (the solution did build: 0 errors), the release workflow itself (it has never run on GitHub), `PROJECT_STATUS.md` counts (not updated). The engine in the worktree was rebuilt (hosted, plugins fitting and twin only).
* **Not done:** the LaTeX report (not committed by design); MCP tools `compare_to_measured` and `fit`, prompts, agent pack, evals (roadmap section 4); the owner's PyPI steps; the macOS NativeAOT wheel.

## Stage 15 -- CI repair, ionisation in χ(2) crystals, MCP and agents completed  (2026-11-18, `dev/2026-11`, macOS arm64)

* **CI failures of the Stage 14 merge, all fixed:** `FittingMemoryTests.Fit_ResultIsBitIdentical...` compared to the last bit and failed on Linux and arm (…612467 against …612495); it now compares to 1e-9 relative and says why.
  `ValidationMatrixTests` and the schemas were stale (regenerated). The `media.py` ionisation component script lost the plasma to the negligible-ionisation skip (it now sets `skipNegligible: false`).
  Python: `test_version_and_capabilities` still expected 2.x; the MCP policy took `C:\Windows\x.npz` for a relative path on POSIX (drive and UNC forms are refused off Windows; on Windows pathlib decides).
* **Ionisation in χ(2) crystals** (`ionization` on `crystal`, element-level, off by default; decision Ph5): `PlasmaResponse.History` (shared with the bulk path) and `ForSolid`; `Chi2Problem.Plasmas`, one rate per field at its own carrier and own
  intensity, densities added, Drude index and loss on each field, 1-D and spatial; band gaps for BBO, LBO, KDP, KTP, LiNbO₃ in the catalogue as `unverified` (from the UV edge, recalled, not checked against a source). A first version built the rate at the highest carrier; review showed
  that gives a 2-photon rate for 800 nm light in BBO (really 5), so it was replaced. Tests `Chi2IonizationTests` (7): density against the multiphoton integral, energy taken, Drude phase, d = 0 Keldysh crystal equals the bulk plasma to 0.2 %, photon order, SHG budget, radial against 1-D.
* **MCP and agents:** tools `compare_to_measured` and `fit` (own time budget, memory pre-flight, run against the real engine), prompts, `ultrafast-agent install --target claude|codex|cursor` with five shipped skills, `tests/agent-evals/` (five prompts with criteria and replayed reference trajectories; no live agent has been scored).
* **Gallery:** every `make_figures.py` re-run (60 examples, 0 problems) and `docs/gallery.md` rebuilt; new hero figure for `Branched/cavity-and-coherence`. WP9 waves 2-4 are not done.
* **Release:** `release.yml` unchanged (tag `v*`, environments `testpypi` / `pypi`, project `ultrafast-engine`); the owner still creates the pending publishers and tags.
* **Not done / open:** see `roadmap-internal.md` sections 1, 4, 5; CI on GitHub for this stage has not run yet; net48 on Mono not available on this machine.

## Stage 16 -- Release completion: unit-in-key renames, decentred aperture, new examples, reference pages, benchmarks  (2026-11-18, `dev/2026-11`, macOS arm64, Apple M3)

* **Unit-in-key renames by load-time migration** (`Schema/Migrations.cs`, `Renames` table; one deprecation note per old key): fibre `betas` -> `betasSkPerM`, Faraday rotator `fieldT` -> `fieldTesla`, Raman models `tau1` / `tau2` / `taub` -> `tau1S` / `tau2S` / `taubS`, `dnDT` -> `dnDTPerK`. Readers also fall back to the old names (models built by hand in C#). Old files, tests and examples keep working. Test `SchemaTests.UnitInKeyRenames_*`.
* **Engine items closed:** `outputs.keepProfiles: "used"` now also works in branched systems (the links of the graph decide; `SpatialStreamingTests.KeepProfilesUsed_InABranchedMachZehnder_*`, result unchanged). The circular aperture now handles a decentred beam (angular trapezoid over a closed-form radial integral; `GridSolverTests.CircularAperture_OnADecentredBeam_*` against the noncentral chi-square mixture and 2-D quadrature). A cw source off the frequency lattice already sat on the requested frequency: only the test was missing (`PhysicsReviewTests.CwSource_OffTheLattice_*`). The CLI banner no longer says "2.0".
* **Examples:** `Branched/system-schematics` (a Mach-Zehnder and a ring-down etalon in every plot style) and `GettingStarted/calculators` (pulse, beam, nonlinear and crystal calculators, with the B-integral checked against an engine run: ratio 1.00 with the catalogue n2 of 2.6e-20 m^2/W). Hero images for `Interop/zemax-workflow`, `Interop/cpp` and `GettingStarted/user-element`. All 64 `make_figures.py` re-run, 0 problems.
* **Docs:** `reference/cli.md` and `reference/mcp.md` new; schema page lists the renamed keys; README and `docs/index.md` front-page table has four rows; `CONTRIBUTING.md` rule 7 (figures use the plotting backend); `speed.md` has a Benchmarks section (Apple M3 CPU measured today, RTX 3060 Ti figures from earlier stages).
* **Finding:** the first calculator-versus-engine B-integral differed by 18 %: the script used n2 = 2.2e-20, the catalogue has 2.6e-20 for fused silica. Not an engine bug.
* **CI of the first PR failed on one cause:** the schematic figures were written to `figures/`, which `.gitignore` excludes (`docs/examples/**/figures/`), so the README linked files that were not in the repository (docs build, example lint, one Python test). They are now in `styles/`. A scan of every tracked markdown file for image links to untracked files finds none left (only `_template.md`, a template).
* **Agents completed in the same stage:** the `design-system` skill and `ultrafast agent` in the C# CLI (embedded resources from `python/src/ultrafast/agents/pack`; `test_the_cli_agent_install_writes_the_same_files_as_the_python_command`). Left for v1.1.0 from this area: live scoring of the evals, MCP tools for `study` / `backward` / the twin, one source for the skills.
* **Second PR round (same stage):** the last CI failure was Windows only: Python `write_text` wrote CRLF where the C# command wrote LF (the installer now writes bytes). The owner decided the first release is an **alpha** (decision E17): tags `v1.0.0aN` go to PyPI as pre-releases, files keep `1.0.0`. MCP completed: tools `study`, `backward`, `twin_fit`, `twin_validate`, `twin_predict` (policy: study limits, a twin project is read through the same checks as any system), `tests/agent-evals/run_live.py` (live scoring with `claude -p`), two more evals, `build/sync_skills.py`. The first live run of the mandatory eval **failed** (Sonnet scanned in `full`): the default fidelity is `full`; the rule is now in the server instructions and the three skills; afterwards Sonnet (2 of 3 runs) and Haiku passed.
* **Moved to v1.1.0** (design-heavy, no release blocker, each listed in `roadmap-internal.md`): glass in beam splitters, delayed-response window planning, non-collinear OPA overlap test, study schema, carrier frame, one-cycle pulses, Kerr-lensed beam carried on, spatial XPW, cross-correlator, `multiFrog`, birth phase, frequency-dependent A_eff, GPU Chi2 plasma / gain with ASE / instrument read-outs, multi-GPU and Apple Metal FP32, full conversion of every example README to the template, coverage and mypy in CI.
* **Not done:** tags and PyPI uploads (owner); net48 on Mono not available here.

## Stage 17 -- GPU pulse-averaged B (E1) and the main-lobe FWHM  (2026-11-24, `mpc-session6`, Windows, RTX 3060 Ti)

* **E1, GPU pulse- and beam-averaged B were 0.** `StepStatistics.BIntegralPulseAveragedRad` had no initialiser (0); the executor estimates the
  average only when it is NaN, so on CUDA / OpenCL (whose steppers never touched it) every element and `totals` reported 0. Fixed twice over: the
  default is NaN (the CPU steppers set 0 when they start), and the 1-D device stepper computes the exact average (`uf_pmean`: Σ P, Σ P Δn
  beside `uf_peak`, one read-back) so CPU and GPU agree to 1e-8 on plates and nonlinear air gaps. The shape-based estimate that remains for the
  ensemble stepper is about 8 % high in air gaps, whose rotational Raman response is delayed (160 fs pulses).
* **Main-lobe width:** `mainLobeFwhmS`, `mainLobeEnergyFraction` (bounds: first local minimum or 5 % of peak outside the half-maximum crossings;
  erf(√ln 20) = 0.9856 for a Gaussian), `satellitePeakCount` (= `peakCount` − 1) in every pulse summary, `pulseSummary` and `compressor`;
  the plots use it. Prompted by multipass-cell soliton compression, where a satellite above 50 % made `fwhmS` jump from ~10 fs to > 100 fs.

## Stage 18 -- WP A2: findings of the A1 physics review  (2026-11-25, `dev/wp-a2`)

`PulseMetrics.MainLobe` no longer returns NaN for a lobe that touches the window edge: the width runs to the edge sample (a lower bound) and
`PulseSummary.MainLobeAtWindowEdge` (not serialised) makes the multi-peak warning say "at least ... enlarge the window"; the warning also
handles an undefined `fwhmS`. Test `MainLobe_TouchingTheWindowEdge_IsFiniteAndFlagged`. The device B test tolerance went from 1e-8 to 1e-5
(an adaptive step sequence changed by one step moves the sums at 1e-6). `docs/plugins/gpu.md` says the ensemble path estimates the pulse-averaged
B (8-15 % off with Raman); `limitations.md` notes that the main-lobe energy window stops at the first local minimum.

## Stage 19 -- WP B1: the style guide  (2026-11-25, `dev/wp-b1`, macOS arm64)

* `docs/style.md` codifies the best of the existing pages (`concepts.md`, `reference/nonlinear.md`, tutorial 1, the Raman-shift example)
  rather than a new voice. The word list comes from counting docs prose with code removed: British -ise forms already win every pair
  (polarisation 91 : 2, fibre 136 : 1, centre 79 : 0 once HTML `align="center"` is discounted); "focused", "program" and "meter" (instrument)
  stay. Engine messages are ASCII today (0 of 427 contain a non-ASCII character), so messages and comments stay ASCII while docs prose uses
  Unicode symbols. Spaced percent ("3 %", 690 : 1), `1e-9` exponents and ` -- ` dashes are the existing majority and were kept. B2 lints
  against the two tables; C2 and D apply the templates.

## Stage 20 -- WP Q1: mypy as a CI gate  (2026-11-25, `dev/wp-q1`)

`[tool.mypy]` in `python/pyproject.toml` checks `build.py`, the generated `types.py` and `calc/` (other modules are followed silently, not yet gated; `follow_imports = silent`, `check_untyped_defs`, `warn_unused_ignores`). The code was already clean apart from three stale `# type: ignore` comments in `build.py`, now removed; `types.py` needed no generator change. A `mypy` step runs in the CI `python` job and `tests/test_typecheck.py` (skipped without mypy) checks both that the tree is clean and that a seeded return-type error fails. Widening the scope to `core.py`, `plotting/` and `results/` (about 60 errors today) is a later step.

## Stage 21 -- WP Z1: the Zemax plugin's own ZOS-API connection core  (2026-11-25, `dev/wp-z1`, macOS arm64)

The connection of the Windows binding was rewritten as our own code (E19; ZOSPy read online as a guide only). The sequence (NetHelper
initialisation, connect in interactive / user-extension / standalone mode, licence, version >= 20.1, primary system, file load) and its
messages live in `UltrafastEngine.Zemax.Sampling/ZosConnectionCore.cs` behind two small interfaces, so `ZemaxConnectionTests` checks them on any
OS with a fake ZOS-API; `ZosConnection.cs` implements the interfaces over ZOS-API, with the initialisation kept out of methods that name ZOSAPI
types (the old `Connect` initialised and used ZOSAPI in one method, which can fail to load `ZOSAPI.dll`). Only a standalone instance is closed,
also after a failed check and on Ctrl+C. `ZosOpticStudio.cs` was split into system access and analyses. Built against the extended stubs
with Mono reference assemblies on macOS; nothing ran on OpticStudio: `HARDWARE-CHECKLIST.md` step 3b lists what the owner must check.

## Stage 22 -- WP P1: glass in beam splitters  (2026-11-25, `dev/wp-p1`, macOS arm64)

* `beamSplitter` and `polarizingSplitter` take `material`, `transmittedThicknessM` and `reflectedThicknessM` (closing the "beam splitters
  have no glass" gap of Stage 11). The glass terms (`BulkTerm`, plus `AttenuationTerm` when the material absorbs) sit on
  `BeamSplitterElement.TransmittedGlass` / `ReflectedGlass` and run after the 2x2 split through `Executor.ApplyTerm`, the same arithmetic as a
  plate. Sequential systems put the continuing port's frame into `ctx.Own`; Branched systems add each output's glass to its own path
  ledger in `GraphExecutor.AddGlass` (the side ports start from the unglassed ledger: an early version let them inherit the kept port's
  glass, which the Mach-Zehnder test caught once the window held the 9.7 ps arm delay). A fed beam on port b in a Sequential system is
  put in the main beam's frame (`Reframe`). Tail ledgers go through the glass with the splitter coefficient. Splitter + glass equals
  splitter + ideal-AR plate per arm to 1e-12; no glass runs no new code. Sequential tails cross the continuing port's glass too
  (`Executor.RunElement`; found by the physics review). Open, not P1: a fed beam on another carrier (1035 nm against 1030 nm) gives a
  different spectrum in a Sequential and a Branched system even without glass; the fed-beam glass test therefore uses one carrier.

## Stage 23 -- WP G0: PoCL reachable on macOS  (2026-11-25, `dev/wp-g0`, macOS arm64, Apple M3)

The `opencl` back-end loaded only the first library it found, which on a Mac is Apple's `OpenCL.framework` (Apple GPU, no FP64), so
`GpuTests` skipped. `OpenClApi.LoadAll` now loads the framework and, directly, every vendor library named by an `.icd` file
(`/opt/homebrew/etc/OpenCL/vendors`, `OCL_ICD_VENDORS`, ...; an ICD loader only when none loads), each device keeps its own API table,
and platforms seen twice are skipped. `ULTRAFAST_OPENCL_LIBRARY` accepts a list. `ultrafast devices` shows the Apple GPU as single
precision only (not usable) and Homebrew PoCL 7.2 as `Portable Computing Language cpu` with FP64. The tests already ask for
`backend: "opencl"` and the first usable device, so no switch was needed: `GpuTests` 20 passed, 0 failed, 0 skipped on PoCL (no kernel
failures), `devices --selftest` passes. `auto` still never picks a CPU OpenCL device. No `ocl-icd` install was needed.

## Stage 24 -- WP D1: Python next to the CLI in the example READMEs  (2026-11-26, `dev/wp-d1`, macOS arm64)

* **Tabs:** `pymdownx.superfences` + `pymdownx.tabbed` (`alternate_style`) in `mkdocs.yml`, with `pymdownx.highlight: use_pygments: false`
  so code blocks keep `<code class="language-x">` for the readthedocs theme's highlight.js (every page's HTML compared before / after: same
  blocks, only a `highlight` class and a trailing newline differ). The theme has no tab CSS: it is in `docs/css/brand.css`. CI and
  `docs.yml` now install `pymdown-extensions`.
* **Convention** (`docs/style.md` "Run it", `_template.md`): a `**Run it**` paragraph between Layout and What it shows, tabs `=== "CLI"`
  and `=== "Python"`; the Python tab is a complete program with the builders (typed dicts) and `uf.System.load` of the example's `.ufs`,
  printing the CLI's line in the CLI's format (.NET `G4` = Python `.4G`). Marker: an HTML comment line `<!-- snippet -->` (or
  `<!-- snippet slow -->`) before the fence -- chosen over a fence attribute because Python-Markdown passes the comment through invisibly
  and no extension is needed; `build/readme_snippets.py` parses it and rejects a marker without its block.
* **Checks:** `python/tests/test_readme_snippets.py` runs each snippet in a temporary copy of its folder at quick fidelity (added as text
  to every run-spec JSON of the copy, and to dict specs by patching `ultrafast.core.Run.__init__` in a prelude) with
  `ULTRAFAST_STRICT_OPTIONS=1`; `build/example_check.py` also runs the snippets (full fidelity) so the Python tab's quoted output is
  checked; `build/example_lint.py` requires the tabs and a snippet when `example.json` has the new optional `"python": true` (chosen over
  a `wave` threshold: explicit per example, independent of the conversion wave).
* **Converted:** `GettingStarted/quickstart` (new `two-plates.ufs` + `run.json` equal to the builders, so it has a CLI tab) and
  `GettingStarted/spm-plate` (`script` = `run.json`; hand check of B: 0.669 rad from the waist intensity against 0.667 from the run).
  Both tabs print identical numbers to the CLI; `example_check` reproduces every quoted line. The per-example D2 instructions are in the
  D2 row of `roadmap-internal.md`.

## Working notes that still help

* **Bug pattern** seen four times: a documented element key read only through run-spec overrides and never from the element's own JSON (`n2`, `raman`, `fR`, `selfSteepening`). When adding an element key, read it with the `Num/Bool/Str` helpers (element JSON **and** override), and test that the element key equals the same value set on the material.
* **Honesty rules that worked:** a paper disagreement is a finding, never tuned away; a KNOWN mark in a component check must have a repro; unverifiable numbers are UNVERIFIED and soft; a test tolerance is never loosened to hide a failure.
* **Parallel agents:** implementers in separate git worktrees (`isolation: worktree`), each on a distinct part of the code; the lead merges and regenerates the generated files once. A subagent started inside a worktree session cannot run commands in the main checkout (and vice versa).
* **Python environment:** export `ULTRAFAST_LIBRARY` and `PYTHONPATH` in the same shell command as the Python call, or a stale `bin/native` library is used. Rebuild with `python/tools/build_engine.py` after any C# change, never while another job uses the library (other Python processes can lock `python/src/ultrafast/bin` on Windows: retry after they finish).
* **Windows machine (owner's):** `python3` is the Microsoft Store stub, use `python` from `../.venv`; HTTPS certificate checks fail for git and pip because the antivirus inspects TLS: the repository has `http.sslBackend schannel` (local config), and pip takes `--cert` with a bundle of the Windows roots exported from `Cert:\LocalMachine\Root`.
* **macOS machine:** no Mono (net48 runs only in CI); `pdftoppm` is not installed, so PDFs are read with `pypdf` or PDFKit; the sandbox denies the `.venv` interpreter for subagents (plain `python3` works with `PYNLO_SRC`).
* **Licensed PDFs** in `docs/papers/*.pdf` are git-ignored; several were scans with garbled OCR (Michelson-Morley, Perot-Fabry, Treacy, Shimizu): unreadable numbers are UNVERIFIED in each `paper.json`.

## Stage 25 -- WP B2: the style linter  (2026-11-25, `dev/wp-b2`, macOS arm64)

* `build/style_lint.py` parses the banned-filler, British-spelling and term tables of `docs/style.md` (nothing is duplicated in code), strips
  code, inline code, HTML, link targets and URLs, and reports `file:line: rule`. Entries with a qualifier it cannot judge ("(as a noun)",
  "(the unit)", a whole cell containing one) are skipped; `just (as in "just run")` is matched as `just run`. The -ize forms are derived from
  the -ise forms of their row. The `units` rule checks numeric values under unit-less keys in JSON blocks with a short exception list.
* Baseline `build/style_lint_baseline.txt`: 59 hits (31 term, 17 filler, 11 spelling, 0 units after the exceptions), keyed by path, rule, match and a
  hash of the line, so moving a line keeps it allowed and editing it does not. CI runs the linter in `docs-and-examples`; `--update-baseline`
  shrinks the file once the prose is fixed (C2 and D do that). `docs/style.md` and `docs/design/notes/` are not scanned.

## Stage 26 -- WP C2f: reference pages calculators, pulse calculator, plotting, MCP; concepts and capabilities  (2026-11-25, `dev/wp-c2f`)

Rewrote `calculators.md`, `pulse-calculator.md`, `plotting.md` and `mcp.md` to the reference template (Keys, Model, Validation, Limits) and corrected `concepts.md` and `capabilities.md`. Each of the 40 rows of `notes/reference-audit-f.md` was re-checked against the source and closed with a resolution column (40 fixed, none kept, none found wrong); seven code-side findings are listed in the audit's section "Code findings for WP C4" (no code edited). Plotting history text (work-package names, "before" images) was removed. `mkdocs build --strict` gives 0 warnings.

## Stage 27 -- WP C2e: reference pages of group e  (2026-11-25, `dev/wp-c2e`)

Rewrote `instruments.md`, `retrieval.md`, `measured-data.md`, `results.md` and `studies.md` to the B1 reference template (Keys, Model or behaviour, Validation with test names, Limits) and closed all rows of `reference-audit-e.md` (column "resolution"). Each audit row was re-checked against the source: the page claim on the main lobe at the window edge was stale (A2 made it a finite lower bound, not null), the audit's own row on it was wrong, as were its rows on the study `element:ID*` wildcard (only the prefix-cache lookup uses it) and on imaging instruments not ignoring `sampling` (the executor skips it and adds a note). The 18-row instrument key table became a table of reported values with links to `options.md`; the `240 fs to 26 fs` floor example was replaced by what `MeasuredSpectrumTests` asserts. Anchors other pages and `options.md` link to were kept. `mkdocs build --strict`: 0 warnings.

## Stage 28 -- WP AG1: user agent pack split from the development rules  (2026-11-26, `dev/wp-ag1`, macOS arm64)

* The pack (`python/src/ultrafast/agents/pack/`) gained `AGENTS.md` (how to drive the engine: files, discovery commands, running, results, habits, MCP) and `llms.txt` (index with documentation-site URLs only). One source each: the Python wheel ships them (`package-data`) and the CLI embeds the same files, so `ultrafast agent install` and `ultrafast-agent install` stay byte-identical. The repository `AGENTS.md` / `CLAUDE.md` / `llms.txt` are untouched and are not copies.
* Install layout (decision): claude `.claude/ultrafast/AGENTS.md`; cursor an `alwaysApply` rule `ultrafast-guide.mdc`; codex the guide as the first part of the marked block; all `.ultrafast/llms.txt`. Claude Code does not read a nested AGENTS.md, so the docs tell the user to import it from `CLAUDE.md`; the installer never edits the user's files.
* `test_agents.py`: `REPO_TERMS` (BuiltIn, CHANGELOG, /regen, worktree, Experiments, ZOSAPI, dotnet build, ...) is searched in every pack file and every installed file; the installed pack must hold the guide. `sync_skills.py --check` fails when the two files are missing.

## Stage 29 -- WP C2a: reference pages of group a  (2026-11-26, `dev/wp-c2a`, macOS arm64)

* Rewrote `system-file`, `run-spec`, `schema`, `cli`, `c-api`, `python`, `plugins` and `extending` to the reference template (Keys, Validation, Limits) and added a resolution column to every row of the group-a audit: 125 rows: 83 fixed on the page (of them 3 also with a code finding), 4 code findings only, 38 kept (page already right, or the audit was wrong). Each finding was re-checked against the source and the built CLI.
* The audit was wrong twice: `crystal` does declare `chi3` and `n2` (the long `ionization` line in `BuiltInElements.cs:268` hides them; strict validation accepts both), and `ultrafast.System` and `ultrafast.system.System` are one class. Its exit-code row was incomplete: `ultrafast validate` returns 3 for an invalid file.
* Code findings (nine, C4-1 to C4-9, in the audit file; no code was changed): the registry disagrees with the compiler on `system.version`, `bulk.dispersionSource` and `crystal.seedNoise`; `topology` is undeclared and top-level keys are unchecked; `DocAt` links point at three pages that do not exist (`elements.md`, `solvers.md`, `spatial-models.md`); `substrates` is missing from the known-key set.


## Stage 30 -- WP AG3: transcript adapters for Codex and Cursor in the agent-eval runner  (2026-11-26, `dev/wp-ag3`)

`tests/agent-evals/run_live.py` now has an adapter layer: `parse_claude` (the old reader), `parse_codex` (`codex exec --json`) and `parse_cursor` (`cursor-agent -p --output-format stream-json`) return the same trace, final answer and files written; `--agent claude|codex|cursor` runs the agent, `--transcript FILE` scores a saved transcript without running anything (format detected). The vendors document the event names and the message types but not the MCP tool-call item (Codex) or the MCP tool-call object (Cursor); those fields follow the open-source event definitions and observed output, are marked as such in the docstrings and the evals section of `docs/reference/mcp.md`, and are the place to fix if a CLI version differs. Owner decision kept: no model was called; `run_codex` and `run_cursor` are untested until the owner runs them. Twelve hand-made replays (two evals, pass and fail variants, three formats) and `python/tests/test_agent_eval_adapters.py` check identical traces and verdicts in all formats.

## Stage 31 -- WP D2-Wavefronts: Wavefronts examples to the template with a CLI and a Python tab  (2026-11-26, `dev/wp-d2-wavefronts`, macOS arm64)

* Both examples of the group (`position-dependent-index`, `spatial-gain-thermal-lens`) now have the template README (`**Run it**` with `=== "CLI"` and `=== "Python"`, snippet marker), `example.json` (`wave` 2, `python` true) and an updated row in `docs/examples/README.md`; the old "16 --" / "15 --" prefixes left the titles. No engine code changed.
* The Python tab of the rod example runs both pump powers in one loop (the 5 W run is an `overrides` entry built with `b.override` and `b.pump`). All quoted numbers reproduce; the only change is the absorbed pump at 5 W (4.05 W printed as 4.1 W).
* Checks: `example_lint`, `example_check` (0 not reproduced), the snippet tests, `style_lint` and `mkdocs build --strict` (0 warnings) are green.

## Stage 32 -- WP AG2: twin_design, twin_track and twin_init on the MCP server  (2026-11-26, `dev/wp-ag2`, macOS arm64)

* Three tools in `python/src/ultrafast/mcp/tools.py` (schemas and policy), `ops.py` (worker) and the server: `twin_design` calls
  `ultrafast_twin.design` (`energy_candidates`, `placement_candidates`, `rank_candidates`), `twin_track` calls `DriftTracker`, `twin_init` calls
  `ultrafast_twin.init`; no physics was reimplemented. Reports go to the project's `reports/design.json` / `track.json`, as with the CLI.
* Decisions: (1) the server lists **16** tools, not the 15 of the roadmap row: `twin_init` is a tool of its own (no existing tool could carry it), and the row's count did not include it.
  (2) Limits: 12 candidates and 240 engine runs (candidates x (parameters + 1), checked in the worker where the parameter count is known) per `twin_design`; 12 recordings and
  192 runs (members x recordings), members 4 to 32 per `twin_track`; placement candidates are counted from the project's system in the policy. (3) `twin_design` and
  `twin_track` take `timeBudgetS` like `fit`; the server now caps any `timeBudgetS` by its budget in one place (`_LONG_OPS`). (4) A `set` value is a number, or an element id
  for `instrument:NAME/at` only. (5) Recording paths are relative to the project folder (like `twin.json`'s `data`), not to the workspace. (6) The tracker keeps no state
  between calls: one call takes every recording since the fit. (7) `twin_init` refuses an existing `twin.json` without `force`, a file, and `.ultrafast-mcp/`.
* Tests (`test_mcp.py`, 70 -> 112 with the parametrised cases): 37 refusals, the budget test for both tools (killed, next call works, capped by the server budget), and the
  synthetic lab end to end through the real server (1.5 s). The agent-pack skills do not list MCP tools, so they were not changed. mypy's gate does not cover `mcp/`
  (three old errors in `server.py` `_meta` handling stay outside it).

## Stage 33 -- WP D2-LaserSystems: LaserSystems examples to the template with a CLI and a Python tab  (2026-11-26, `dev/wp-d2-lasersystems`, macOS arm64)

* All ten examples of the group have the template README (`**Run it**` with `=== "CLI"` and `=== "Python"`, snippet marker), `example.json` (`wave` 2, `python` true) and the interface column "CLI + Python" in `docs/examples/README.md`; the old numbers left the titles. No engine code changed.
* Several results per chain: every quoted number was kept and re-checked (oscillator, amplifier, compressor lines of `yb-laser-system`; the two runs of `shg-lbo`; run and study of `contrast-chain`). Numbers that moved: stretched pulse of `stretcher-compressor` 233.6 ps (was 232.6), GDD of `four-grating-compressor` -1547449 fs2 (was -1547462; now equal to Treacy), compressed pulse of `nopcpa-bbo` 24.0 fs (was 23.8), stretched pulse of `yb-laser-system` 131 ps (was 130), floor of the correlator in `contrast-chain` -56.5 dB (was -58; the figure line moved, `contrast-chain.png` redrawn). The Yb:YAG amplifier of `yb-multipass-cpa` gives 1.46 mJ before and 1.04 mJ after the compressor; the README table quotes the former and the CLI line the latter, both now stated.
* `nopcpa-bbo` reads the results of `shg-lbo`: its Python tab first runs `../shg-lbo` and writes `../shg-lbo/results` (a file outside the example's own folder, the one exception to the "no files outside the folder" rule). `python/tests/test_readme_snippets.py` therefore copies the folders listed in `build/examples_cli.py` `DEPENDS` next to the example.
* Checks: `example_lint` (all 28 folders), `example_check LaserSystems` (0 not reproduced), the ten snippet tests, `style_lint` and `mkdocs build --strict` are green. `example_lint` on a group folder reports "no example.json" (also for Wavefronts); lint with no argument or with an example folder.


## Stage 34 -- WP C2b: reference pages of group b (nonlinear, ionisation, delayed light, gases, fibres, gain, oscillators)  (2026-11-26, `dev/wp-c2b`)

* Rewrote the seven pages to the reference template (Keys with links to `options.md`, Model, Validation with test names and asserted tolerances, Limits) and added a resolution column to all 40 rows of the group-b audit: 40 fixed on the page (6 of them also with a code finding), none kept as wrong-audit; one parenthesis of history kept in `nonlinear.md` (the GPU averaged B-integral before 2026-11-24), as `docs/style.md` allows. Every anchor that other pages or the schema link to exists (`gain.md#the-amplifier-element` is new; `nonlinear.md#cubic-anisotropy` kept by an HTML anchor; `materials.md` now links `#cubic-anisotropy-xpw`).
* The audit was partly wrong once (I4): the solid valence densities are not all 2.1e28 m^-3 and the generic solid entry uses m* = 0.635, not 0.5. Re-verified: `absorption: false` does drop the ionisation loss (I1, the audit was right).
* Physics review (`physics-reviewer`): no physics errors; six precision fixes applied (echo-delay table with the Malitson n_g = 1.4627, also in the `engine-physics` skill; `linAgrawal` symbols and default b = (fb + fc)/(fa + fb + fc); fibre fR fallback; loss < 1; ripple clamp; remaining neutrals in the ionisation loss).
* Code findings C4-b1 to C4-b7 (no code changed): `stepper` links `solvers.md`; `oscillator` element keys declared where the code ignores them; `pumpDurationS` declared in `pump` but read on the element; gain-element `n2` undeclared; pump `order` / `kind` defaults; a gas avalanche switched on by `collisionTimeS`; a test reference string with n_g 1.4635.

## Stage 35 -- WP D2-Nonlinear: Nonlinear examples to the template with a CLI and a Python tab  (2026-11-26, `dev/wp-d2-nonlinear`, macOS arm64)

* All eight examples of the group have the template README (`**Run it**` with the two tabs, snippet marker), `example.json` (`wave` 2, `python` true) and the interface column "CLI + Python"; the old numbers left the titles. No engine code changed.
* Numbers that moved (old prose against a fresh run): `mpc-argon` spectral FWHM 61.0 nm (61.9); `spatial-kerr` every Kerr-run number (beam radii 97.4 / 97.9 / 113.1 / 138.2 / 168.8 um, wavefront R -70 mm (-81), far-field half-angle 3.80 mrad (3.70), space-time 234 / 327 fs (242 / 325), B 1.55 rad (1.4), transform limit 199 fs (207), M2 1.08 (1.07), the sampling table) while the linear run still equals Gaussian optics; `ner-measured-input` input transform limit 249 fs (240); `ppln-dfg` pump depletion 68 % (about 70); `supercontinuum` coherence 0.98 (0.97) and 0.24 (about 0.2) at 150 fs, -40 dB span 492 to 1307 nm (about 450 to 1300), 91.05 % energy. Nothing failed to reproduce; the old `spatial-kerr` values are from an older engine and were not investigated.
* Prose-only numbers were moved into the Python tabs (`filament`: carrier-only plasma variant with centroid 813 nm and 7.5 % loss; `supercontinuum`: the 150 fs coherence and the Raman soliton delay 3.34 ps; `ppln-dfg`: acceptance widths). `uf.retrieve("pcgpa", "results/result.npz", output="retrieved")` reproduces the CLI retrieval line; `uf.coherence(system, spec, trials=6, parallel=6)` reproduces `ultrafast study coherence.json` exactly. `ppln-dfg` prints `T = 2,120.22 %` with `:,.2f`.
* `ner-measured-input`, `mpc-argon` and `ppln-dfg` keep their helper scripts (`ner.py`, `make_mpc.py`, `compare_pynlo.py`); the README shows their output in a separate block or as an optional CLI line.
* Checks: `example_lint` (all folders), `example_check Nonlinear`, the eight snippet tests, `style_lint` and `mkdocs build --strict` are green.


## Stage XX -- WP fix-2: the spatial-kerr numbers, and the Kerr lens pinned against theory  (2026-11-26, `dev/wp-fix-2`, macOS arm64)

* **Cause.** The `spatial-kerr` README numbers (B 1.40 rad, TL 207 fs, wavefront R -81 mm, far field 3.70 mrad, exit radius 97.8 um) date from the initial commit 2f77295 and were never re-run. Bisected on the example (temporary worktree; 2f77295 needed the then-untracked `Results/*.cs`): 2f77295 gives the old numbers, 8dfff5d ("Added GPU support", 2026-10-26) and every later commit the new ones. 8dfff5d introduced `EnvelopeProblem.RamanKernels`, which scales the sampled Raman kernel to unit sum on the grid, in the 1-D and the transverse paths. Before, the Blow-Wood kernel sampled at the example's dt = 25 fs summed to 0.585, so the Kerr index was n2 (0.82 + 0.18 x 0.585) = 0.925 n2; the current engine with n2 x 0.925 reproduces every old number (B 1.404, TL 207.4 fs, R -81.6 mm, 3.70 mrad, 97.8 um). 3d9df78 (band-limited kernel) moved them by < 1e-4 more.
* **Which is right: the new numbers.** The second moment of a Gaussian beam in a Kerr medium follows w^2 = w0^2 [1 + (1 - P/P_G)(z/zR)^2], P_G = lambda^2 / (2 pi n0 n2), exactly (virial law), slice by slice for a pulse (energy-weighted P0/sqrt2): 97.47 um predicted, 97.4 now (97.86 with 0.925 n2). The moment curvature after the plate, -131 mm predicted, is -130 mm from the free-space radii. A 300 fs pulse sees the Raman share as instantaneous, so Raman off gives the same numbers, as it should. The wavefront sensor's -70 mm is the curvature over the centre of the beam and has no closed form.
* **Tests.** `SpatialTests.SpatialKerr_Solver_SecondMomentFollowsTheVirialLaw` (radial solver, 1e-4; observed 1e-5), `SpatialKerr_SilicaPlate_BeamSizeFollowsTheVirialLaw` (through the compiler with Raman on the 25 fs grid, 2 %; observed 1.0 %, from the profile stored at 48 sampled frequencies), `SpatialKerr_ThinSilicaPlate_OnAxisBAndKerrLensFocalLengthMatchGaussianBeamTheory` (B = k0 n2 I0 zR atan(L/zR), 0.3 %; moment focal length pi w^4 / (sqrt2 n2 P0 L), 0.5 %). With the pre-8dfff5d kernel they fail by 7 %.
* **Sibling path.** The carrier-resolved `FieldProblem` still sampled h(t) directly (normalised but not band-limited); it now uses `EnvelopeProblem.ResponseKernel`, like the envelope paths (`FiberTests.RamanKernel_OnACoarseGrid_HasTheResponseSpectrum`: |H(w)| within 0.5 % at dt = 10 fs, 77 % off when sampled directly). Only field grids with dt > 0.5 fs (long carriers) change.
* **The other two moved numbers are not engine changes.** `mpc-argon` 61.0 nm is `output.spectralFwhmM` (frequency FWHM converted at the centroid), 61.9 nm the `spectrometer` instrument (per unit wavelength); both are reported at every commit since 2f77295 and the README now names both. `ner-measured-input` gives an input transform limit of 248.6 fs at every commit since the example was added (dae0e9e); the old "240 fs" was never a run value.
* Known limit, recorded: the per-element `beam.radiusXM` after a strongly SPM-broadened spatial element comes from the 48-frequency sampled profile (`TransverseProfile.Collapse`), 1 % off the exact second moment in the Kerr part of w^2 (0.05 % of w) for spatial-kerr.
* Checks: solution build (net10.0 + net48) 0 errors; C# `--slow` 709 pass, 0 fail, 1 skipped after `/regen`; Python 525 pass, 12 skipped (with pyNLO); `mkdocs build --strict` 0 warnings; spatial-kerr re-run with `ULTRAFAST_STRICT_OPTIONS=1` gives the README numbers. The `physics-reviewer` subagent could not be started from this agent session (no agent tool); the formulas (P_G from the Hamiltonian, the moment focal length, the 4 sqrt2 ratio) were checked by hand and against the solver; a reviewer pass on the diff is still open.
* **Physics review (run by the coordinator on the diff).** No physics errors; two test gaps, now closed: `FiberTests.RamanOn_LongPulse_CarrierResolvedAndEnvelopeGiveTheFullKerrPhase` (2 ps at 20 um, field dt 7.8 fs, Raman on: nonlinear phase = k0 n2 I0 L in both solvers within 0.1 %; fails with fR dropped (0.82) or the raw unnormalised kernel (-0.7 %)), and a complex / causal-phase check in `RamanKernel_OnACoarseGrid_HasTheResponseSpectrum` (a time-reversed or sign-flipped kernel fails). Recorded in `limitations.md`: Raman on the cycle-averaged I_env drops the 2 omega0 term (~3 % of the delayed part at 8 um in silica); `SampledResponse` falls back to direct sampling above 2^22 points; and, found while writing the test, carrier-resolved SPM is 33 % high when the envelope grid reaches non-positive frequency (only a note today).
