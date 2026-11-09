# Roadmap: adaptive optics, temporal shaping, conditioned transforms and the digital twin

Written 2026-11-09 on the branch `dev/2026-11`. The reasoning and the physics are in
[plan-ao-digital-twin-2026-11.md](plan-ao-digital-twin-2026-11.md); this page is the **ordered list of work packages**
that separate Claude sessions can pick up. Sizes: S ≈ 1 focused agent-day, M ≈ 2-3, L ≈ 4-6, XL ≥ 7.
**Nothing here is implemented yet.**

## 1. Goals, in one paragraph

Add (1) an **`ao` plugin** with a deformable mirror, an SLM (up to a full 1920 × 1080 panel) and a coherent tiled-aperture
amplifier array, with wavefront sensing, Zernike modal and zonal correction, hologram design and sensorless optimisation;
(2) **temporal pulse shaping** (a pixelated spectral shaper and an AOPDF model) in the same plugin; (3) **conditioned
transforms** in the fitting plugin (shared parameters over many operating conditions, then physics-constrained networks);
(4) a **`twin` plugin** on top of fitting for digital twins, with an optional MCP server for AI agents. The core engine gets
only the physics that is not specific to one device. Every package ends in examples that **look good**: users judge the
engine in the first minute.

## 2. How the sessions work together

* **One branch per stream**, cut from `dev/2026-11` after each merge wave: `dev/2026-11-core`, `-ao-*`, `-fitting`, `-twin`,
  `-visuals`, `-examples-*`. Use `git worktree` (or `isolation: worktree`) so builds do not clobber each other.
* **File ownership prevents conflicts.** Only the *core* stream edits `src/UltrafastEngine.Core/**`. Plugin streams edit only
  their own `plugins/<name>/**`, `docs/plugins/<name>.md`, their example folders and their tests. Shared files
  (`mkdocs.yml`, `llms.txt`, `CHANGELOG.md`, `docs/testing.md`, `PROJECT_STATUS.md`, `docs/capabilities.md`,
  `ROADMAP.md`) are **edited by the lead at merge time**; a stream puts its lines in `docs/design/notes/<stream>.md`
  instead.
* **Generated files** (schemas, `options.md`, `types.py`, `python-api.md`, the validation matrix) are regenerated **once per
  merge wave by the lead** (`/regen`), never by a stream, as in the November consolidation.
* **Every package** follows the project definition of done: builds for net10.0 and net48, new behaviour has a test with an
  analytic or published reference, docs for humans and for agents, example READMEs checked with `/example-check`, strict
  options, `mkdocs build --strict` with 0 warnings. A package is not finished until a `physics-reviewer` pass on its diff is
  clean (physics packages).
* **Cost control** (you moved `paper-replicator` to `haiku` for usage): use `test-runner` (haiku) for long builds and test
  runs; use `sonnet` for examples, docs and plumbing; reserve the strongest model for core physics, the chromatic SLM model and
  the algorithms. Give each session its package text from this page and nothing else, to keep context small.
* **Honesty rules** carry over: a disagreement with a reference is a finding, never tuned away; unverified numbers are marked.

## 3. The example quality bar (applies to every example package)

Users will look for a few seconds, so each example must give a good result **and** a good picture.

| rule | detail |
|---|---|
| a hero figure | one figure that tells the story at a glance (before / after, target / result), 16:9 or 3:2, consistent theme from `ultrafast.plotting` (light and dark), units on every axis, the analytic or target value drawn on the same axes, ≤ 300 kB PNG (the existing figure budget) |
| an animation for every iterative algorithm | GS, SPGD, modal search, phase locking, closed-loop correction, twin fits: the thing converging, frame by frame, with the metric curve beside it. 6-15 s, 15-20 fps, ≤ 2.5 MB each |
| no hidden dependencies | animations are written as GIF with Pillow (no ffmpeg on the development Mac); MP4/WebM is optional when `ffmpeg` is found, and smaller |
| numbers in the README match a fresh run | `/example-check`; numbers below 1e-10 are printed as bounds, never as exact round-off |
| runs fast | each example finishes in under 2 minutes on a laptop CPU; the full-size SLM example in under 5 and says so |
| runnable and self-checking | `ULTRAFAST_STRICT_OPTIONS=1`; ends by asserting the key number against the reference, so a regression breaks the example check |
| a showcase | a docs "Gallery" page and a README hero animation, assembled from the examples by one script |

The animation helper, the style and the gallery are package **V1**, built before the examples.

## 4. Work packages in order

The columns are: **id**, what, depends on, tests (reference values), deliverable, size. "Gate" rows are decisions that
need the owner or a measurement.

### Phase 0: make CI green, measure, build the visual tools

| id | package | depends | acceptance | size |
|---|---|---|---|---|
| P0.1 | **CI fixes.** Example 32 prints a bound for round-off (done in the working tree). The FROG ePIE retrieval test: cause from a probe run on Linux CI, then an algorithm fix or a test that checks what the algorithm guarantees; tolerance never loosened to hide a failure | none | CI green on `main`; reasons recorded in the changelog and `docs/reference/retrieval.md` | S |
| P0.2 | **Spikes** (one note, `docs/design/notes/spikes.md`, with numbers): **S1 scale**: a cartesian profile of 1920 × 1080 and 4096 × 4096, one frequency, time and memory of a `mask file`, a propagation and a camera, CPU and (if present) GPU. **S2 sparse spectral sampling**: 800 nm / 30 fs pulse through a phase mask, K = 4, 8, 16, 32 frequencies against a dense run; the error in focal peak and pulse duration. **S3 chromatic SLM**: a blazed grating as a `mask file` and as a thin `bulk` with `indexProfile`; does a pulse disperse as a grating of the same period? **S4 loop speed**: time per trial of a 128² spatial run; SPGD / CMA-ES on 15 Zernike coefficients through the existing fitting plugin. **S5 tiled array**: 7 tiles as a Branched system versus a coherent sum on a cartesian grid; far-field combining efficiency. **S6 multi-condition**: read `plugins/fitting/src/Fitting.cs` and try two datasets in one fit. **S7 animation**: GIF size and time for 100 frames of 800 × 450 | none | each spike has a number and a decision; gate G0 below | M |
| V1 | **Visual toolkit.** `ultrafast.plotting` gains `animate(frames, ...)` (GIF via Pillow, MP4 if `ffmpeg`), a shared style, a "before / after" and "target / result" panel helper, a convergence-panel helper, and an `assets budget` check that fails when a figure exceeds 300 kB or a GIF 2.5 MB. A `build/gallery.py` assembles the hero images into `docs/gallery.md` | none | unit tests for the helpers; a demo animation rendered in CI; budget check in `example_check.py` | M |
| **G0** | **Gate.** From S1-S3: choose the core design (streaming over frequency, sparse sampling, which chromatic rules) and from S6 the size of F1. Owner reviews the spike note | P0.2 | decisions written into the plan page | - |

### Phase 1: core primitives (one session, serial: it edits the compiler and the schema)

| id | package | depends | tests (reference) | size |
|---|---|---|---|---|
| C1 | **`modulator` element.** A cartesian cell grid (count, pitch, offset, rotation) with an array of complex transmission, OPD or phase; `fillFactor`, `crosstalkKernel`, `quantisationLevels`, `phaseWrapRad`; `chromatic`: `opd`, `phaseAtDesign`, `index`; sub-pixel averaging | G0 | a pure tilt moves the focus by f·θ; defocus gives the thin-lens shift; N-level blazed grating first-order efficiency [sin(π/N)/(π/N)]²; replica orders at λ/pitch; a ramp disperses a pulse like a grating | L |
| C2 | **In-memory maps.** Arrays in the Python and C APIs and the run spec without files; a `map` callback `f(state) → array`; a compile cache so a loop of N trials compiles once | C1 | N trials equal N file runs to 1e-12; compile once, run many (timing test) | M |
| C3 | **Streaming and sparse spectral sampling** for linear spatial chains: one frequency at a time with only the requested outputs kept; K sampled frequencies interpolated, with a reported error estimate | C1, G0 | streamed result equals the all-frequency result (1e-12); the sparse error estimate bounds the true error found in S2 | L |
| C4 | **Coherent tile source** (`initialProfile` kind `tiles`): N tiles, positions, amplitude, phase, tip-tilt, delay, per-tile waist and shape, fill factor | C1 | N equal in-phase tiles: peak N² of one tile; phase error σ costs exp(−σ²); grating-lobe angle λ/pitch | M |
| C5 | **cw / narrow-band fast path** for spatial runs and zonal sensor outputs (slope maps, wavefront map as a stable array) | C1 | cw result equals the pulse result at the carrier (1e-9) | S |
| C6 | **GPU parity** of C1/C3/C4 on the spatial back-end (CPU parity 1e-9; skipped without a device) | C1-C4 | `GpuTests` rows | M |
| C7 | **Core docs and merge wave 1**: reference pages, `capabilities.md`, `/regen`, validation matrix, changelog | C1-C6 | `mkdocs --strict` 0 warnings; five generated files current | S |

### Phase 2: the `ao` plugin devices (three sessions in parallel after C1/C2; each owns its own files)

| id | package | depends | tests (reference) | size |
|---|---|---|---|---|
| AO0 | **Scaffold**: `plugins/ao` (plugin.json, `requires: ["fitting"]` if it uses the estimators, Python module `ultrafast_ao`, docs page `docs/plugins/ao.md`), device-preset loader (generic, parametric) | C1 | plugin loads; element listed by `ultrafast plugins` | S |
| AO1 | **`deformableMirror`**: layouts (square, hex, ring, explicit), count, pitch, aperture; Gaussian or measured influence function with coupling; stroke, quantisation, hysteresis; segmented and bimorph kinds; command-to-wavefront | AO0 | influence at a neighbour = coupling; surface-to-wavefront factor 2 cos(aoi); Zernike fit residual falls with N | M |
| AO2 | **`slm`**: any panel size up to 1920 × 1080+, pitch, fill factor, grey levels, voltage-to-retardance curve, LC thickness and Δn(λ), fringing kernel, zero-order leakage; input as phase, Δn map or grey levels | AO0, C3 | blazed-grating efficiency vs N levels; pixelation orders; a pulse through a wrapped ramp; the full 1920 × 1080 example within the S1 budget | L |
| AO3 | **`tiledArray`**: square, hex and ring layouts; per-channel amplitude, piston, tip-tilt, delay, dispersion; combining efficiency, far-field Strehl, grating-lobe fraction; optional nested per-channel system as a second step | AO0, C4 | N² peak; exp(−σ²); fill-factor side-lobe energy; delay mismatch cost for a 30 fs pulse (overlap of the envelopes) | M |

### Phase 3: sensing, control and algorithms (after AO1; two sessions)

| id | package | depends | tests (reference) | size |
|---|---|---|---|---|
| AO4 | **Wavefront sensing**: `shackHartmann` model (lenslet pitch, focal length, centroiding, photon and read noise, dynamic range); Zernike **modal** and **zonal** (Southwell) reconstructors | C5 | spot shift = f·∂W/∂x; noiseless Zernike recovery exact; noise propagation matches theory | M |
| AO5 | **Calibration and control**: `ao calibrate` (interaction matrix by poking), `ao correct` (Tikhonov / truncated SVD, reports residual rms, Strehl, saturated actuators, null space), `ao loop` (integrator with gain and delay); modal correction by Zernike | AO1, AO4 | a Zernike aberration within the DM's range corrected to the fitting-error floor; Strehl ≥ Maréchal prediction; stability margin of the integrator | M |
| AO6 | **Holograms**: Gerchberg-Saxton family (GS, weighted GS, MRAF), complex-to-phase-only encoding (double-phase, Bolduc-type), quantisation-aware refinement, output as grey levels or voltages | AO2 | GS error monotone (Fienup 1982); recover a known phase; uniformity of WGS spots; encoding error vs method | M |
| AO7 | **Sensorless optimisers**: SPGD, hill climbing, modal (N + 1 measurements per mode), phase locking for the tile array; user-chosen metric (peak, Strehl, encircled energy, SHG signal); transmission matrix by phase stepping | AO1, AO3 | focus enhancement (π/4)(N − 1) + 1 for N phase-only modes (Vellekoop and Mosk 2007); convergence to the one-shot result within tolerance | M |

### Phase 4: temporal shaping (one session, parallel to Phase 3)

| id | package | depends | tests (reference) | size |
|---|---|---|---|---|
| TS1 | **`spectralShaper`** (1-D): pixels → wavelength through grating dispersion and focal length, Gaussian resolution kernel from the spot, fill factor, gaps, grey levels, wrapping; acts on the spectrum directly | AO0 | a programmed linear phase delays the pulse; pixel-gap pre/post pulses at 1/(pixel bandwidth) (Weiner); resolution kernel limits the time window to ~ 1/(spot bandwidth) | M |
| TS2 | **`aopdf`** element (programmable spectral phase and amplitude, finite window, efficiency, ripple) and **closed-loop temporal shaping** with FROG / d-scan as the metric (evolutionary and MIIPS-style phase scans) | TS1, AO7 | gain-narrowed amplifier output restored; converge to a target pulse within tolerance | M |
| TS3 | *(later)* full 4f line with the spatial model and space-time coupling | TS1, C3 | spatial chirp from a misaligned 4f line against Kostenbauder | L |

### Phase 5: fitting-plugin stream (parallel to Phases 1-4; touches only `plugins/fitting`)

| id | package | depends | tests (reference) | size |
|---|---|---|---|---|
| F1 | **Multi-condition fit**: `conditions` list (settings overrides + measured data per condition), shared and per-condition parameters, held-out conditions, per-condition residuals in the report | S6 | two synthetic conditions recover known shared parameters (1e-3); a held-out condition is predicted within the noise | M |
| F2 | **Parametric conditioning**: each transform parameter as a function of the condition vector (constant, linear, power law, polynomial) with physical priors | F1 | a nonlinear phase linear in energy recovers the slope from spectra at five energies | M |
| F3 | **Smooth bases in (ω, θ)**: tensor-product splines / Chebyshev for spectral phase and log-amplitude, penalties in both directions, existing passivity, causality and Kramers-Kronig constraints | F1 | a known smooth surface recovered; penalties stop over-fitting noise | M |
| F4 | **Network transforms** (`kind: "network"`): a small multilayer perceptron g(ω, θ; w) with constraints built into the output layer, trained by the existing estimators. Described in the docs as "physics-constrained network transforms" | F2, F3 | recovers a hidden smooth map; constraints hold exactly; compared with F3 on the same data | M |
| F5 | **Gate: gradients.** Chain-rule gradients for a transform followed only by linear elements (backward propagation); decide whether an adjoint of the split-step solver is worth an XL package | F4 | a short research note with the cost and gain measured | S |

### Phase 6: the `twin` plugin (after F1/F2; mostly serial)

| id | package | depends | deliverable | size |
|---|---|---|---|---|
| T1 | **Twin project** layout, recording catalogue by condition, fit and validation splits, provenance, one-command rebuild | F1 | `ultrafast twin init / fit / validate` | M |
| T2 | **Validation report** with held-out conditions and credible bands; flags a twin that only reproduces its training data | F1, F2 | HTML report | M |
| T3 | **Placement planner** and **experiment design**: where transforms are identifiable; which condition to measure next | T1 | planner output on the synthetic lab | M |
| T4 | **What-if explorer**: scans and optimisation on the fitted twin with prediction bands | T1, T2 | `ultrafast twin whatif` | M |
| T5 | **Drift tracking** (ensemble Kalman) | T1 | tracks a slowly moving parameter in a simulated week | L |
| T6 | **MCP server** (stdio, `pip install "ultrafast[mcp]"`): about eight coarse tools (`describe`, `validate`, `run`, `compare_to_measured`, `scan`, `fit`, `explain_warning`, `plot`); workspace-scoped; user expressions and callbacks refused by default; time and memory budget per call | T1-T4 | tool tests with a scripted client; refusal tests for code execution | M |
| T7 | **Design-goal file** and checker, plus a benchmark of agent design tasks with known answers | T6 | pass / fail per goal | M |

### Phase 7: examples with plots and animations (many parallel sessions; each owns one folder)

Numbering follows `docs/examples/` (next free: 43). Each row is one session; every row has a hero figure and, where the
algorithm iterates, an animation.

| id | example | shows | hero figure | animation | depends |
|---|---|---|---|---|---|
| E1 | **AO components gallery** | DM (influence map, a poke), SLM (grey levels → phase → far field, wrapping, 8-bit steps), tiled array (near and far field) | 3 × 3 panel: device map, wavefront, focus | a poke sweep across the DM | AO1-AO3 |
| E2 | **DM correction of a Zernike aberration** | one-shot calibrate and correct; modal versus zonal; Strehl before / after; actuator use | before / after focus with Strehl | the Zernike modes being corrected one by one | AO5 |
| E3 | **Sensorless correction** | SPGD and modal search from a metric only, compared with one-shot | metric versus iteration, three methods | the focal spot sharpening, metric curve beside it | AO7 |
| E4 | **Aberrated high-power beam + DM + nonlinear chain** | local intensity and B-integral with and without correction | peak-fluence maps and B-integral maps | aberration strength sweep | AO5, existing nonlinear elements |
| E5 | **SLM beam shaping** | smiley face, square, ring, vortex donut in the focal plane; full 1920 × 1080 panel with 8-bit quantisation | target / hologram / result / error for four shapes | the GS iteration morphing noise into the picture | AO2, AO6 |
| E6 | **Complex shaping with a phase-only device** | amplitude and phase target at once; encoding methods compared | error versus method | switching between methods | AO6 |
| E7 | **Coherent tiled-aperture combining** | 7 and 61 tiles, random phases then locked; delay mismatch for a 30 fs pulse; channel power imbalance | far-field pattern before / after, combining efficiency versus noise | SPGD locking: far field and efficiency curve together | AO3, AO7 |
| E8 | **Temporal shaping** | flat-top, double pulse and pulse train from a programmed phase; pixelation side-pulses; gain-narrowed amplifier restored by an AOPDF | pulse in time for each shape; spectrum and phase beside it | closed-loop shaping converging to a target pulse | TS1, TS2 |
| E9 | **Transmission matrix through a scatterer** | focusing by phase conjugation | enhancement versus N with the formula | speckle collapsing into a focus | AO7 |
| E10 | **Fitting across conditions** | spectra at five pulse energies share one transform; held-out energy predicted | data and fit overlay, parameter versus energy | the fit converging, all spectra at once | F2 |
| E11 | **Build the digital twin** (synthetic lab with hidden aberrations) | place transforms, fit five conditions, recover the hidden values, predict a sixth with a band | hidden versus recovered, prediction with credible band | parameters converging; the twin's FROG trace approaching the measured one | T1, T2, F3 |
| E12 | **Explore the twin without beam time** | scan compressor, energy and pressure on the fitted model | predicted FROG, spectrum, contrast maps | a parameter sweep through the compressor | T4 |
| E13 | **Network transform versus spline transform** | when a network helps and when it only fits noise; identifiability map | side-by-side fit and error | training curve | F4 |
| E14 | **Agent-driven design** | an agent designs a compressor for a goal file through MCP; engine warnings visible | goal checklist and the design's result | the agent's runs as a sequence of pulses | T6, T7 |
| E15 | **Wavefront twin** | fit a DM / SLM map from a measured wavefront and a focal spot | recovered versus true map | fit progress | AO5, T2 |

### Phase 8: integration and finishing (the lead)

| id | package | content | size |
|---|---|---|---|
| Z1 | **Merge waves** | after each phase: merge streams into `dev/2026-11`, `/regen` once, full tests, `physics-reviewer` on physics diffs | M per wave |
| Z2 | **Docs**: reference pages (`modulator`, `ao` plugin, temporal shaping, conditioned transforms, `twin`, MCP), `capabilities.md`, `docs/testing.md` rows, validation matrix, `PROJECT_STATUS.md` counts, `CHANGELOG.md`, `llms.txt`, `mkdocs.yml`; an "Adaptive optics and digital twin" guide for agents in `docs/agents.md` | M |
| Z3 | **Showcase**: README hero animation, docs Gallery page, a short "what is new" page | S |
| Z4 | **Full verification**: `/verify` (builds, all tests, examples in strict mode, docs), CI green on Linux, macOS, Windows | M |
| Z5 | **Merge to `main`** after `/verify` and green CI. A tag or release only on your request | S |

## 5. Order and parallelism at a glance

```
Wave 0   P0.1 CI fixes ── P0.2 spikes ── V1 visual toolkit ── G0 (owner gate)
Wave 1   core C1..C7 (1 session) ‖ fitting F1..F3 (1 session) ‖ V1 gallery
Wave 2   AO0, AO1, AO2, AO3 (3 sessions) ‖ F4/F5 ‖ twin T1, T2
Wave 3   AO4..AO7 (2 sessions) ‖ TS1, TS2 ‖ twin T3, T4
Wave 4   examples E1..E15 (up to 6 sessions, one folder each) ‖ T5, T6, T7
Wave 5   Z1..Z5 verification, docs, showcase, merge to main
```

The critical path is **P0.2 → G0 → C1 → C3 → AO2 → AO6 → E5** (the full-size SLM). The fitting stream does not wait for
the core, which is why it starts in Wave 1.

## 6. Risks and how the order handles them

| risk | handled by |
|---|---|
| a full-size SLM does not fit a laptop | spikes S1-S2 before any design; streaming and sparse sampling in C3; examples carry a time and memory budget |
| the chromatic SLM model is wrong | S3 first; test against the blazed-grating reference before the SLM element exists |
| multi-condition fitting is more work than expected | S6 reads the code before F1 is sized; F2 (parametric) ships before F4 (network) |
| networks cannot be trained at scale (no gradients) | F4 limited to small nets with the existing estimators; F5 is an explicit gate, not a promise |
| animations bloat the repository | budget check in V1 (≤ 2.5 MB each); decision below if the total grows |
| MCP executes untrusted code | refused by default; workspace-scoped; read-only with respect to the lab |
| parallel sessions collide | file ownership (section 2); the lead regenerates shared files |

## 7. Open points for the owner

1. **Repository size for animations.** Committing about 15 GIFs of ≤ 2.5 MB is about 40 MB. Alternatives: attach them to a
   release, or generate them in the docs workflow. Recommendation: commit hero animations (a handful), generate the rest at
   docs build time.
2. **Gate G0** needs your review of the spike numbers (scale, sparse sampling, chromatic SLM).
3. **Version.** This is a feature release (2.3.0 by the project's convention) once merged; tags and releases only on your
   word.
