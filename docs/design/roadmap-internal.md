# Internal roadmap: open engineering work

Internal page ([folder index](README.md)). Open items, with size (focused agent-days: S ≈ 1, M ≈ 2-3, L ≈ 4-6, XL ≥ 7), area, acceptance
criteria and what blocks them. The public summary is [docs/roadmap.md](../roadmap.md). Designs not scheduled are in
[future-plugins.md](future-plugins.md); limitations that are not planned work are in [limitations.md](limitations.md).
Classification: **core** (core public roadmap), **plugin** (separate plugin), **internal** (stays here: engineering with no user-visible promise).

**Status for 1.0.0 (2026-11-25):** the owner has pulled most of sections 2 to 7 and the public roadmap's "Next" items 1, 2, 4, 5, 7 and the
"Later" items waveguides and gain, XPW, Zemax depth and the single-shot cross-correlator into the push towards 1.0.0 (section 0). Decision
E16 (no open items in 1.0.0) is superseded by E20; the owner's release steps (section 1) are unchanged. Sections 2 to 7 stay as the
item catalogue; section 0 is the order of work.

## 0. Work packages for 1.0.0 (plan of 2026-11-25)

Each work package (WP) is sized for **one fresh Claude instance** (Opus or Sonnet as given) with a small context. The instance reads only
`CLAUDE.md`, this section's row for its WP and the files named in it, and finishes the WP completely (code, tests, docs) before it stops.

### 0.1 How to run a WP

* **Branch.** Start from the current `dev/2026-11`: `git worktree add ../uf-wp-<id> -b dev/wp-<id> dev/2026-11`. One WP, one branch. The
  coordinator (the owner or the coordinating session) merges finished WPs into `dev/2026-11`; `main` gets `dev/2026-11` after `/verify` and
  green CI. No tags, no releases, no PyPI (owner only).
* **Brief** (paste into the new instance, with the WP id):
  > Work package `<id>` of `docs/design/roadmap-internal.md` section 0. Read `CLAUDE.md` and that row, then only the files it names and what
  > you find by grep. Work in the worktree `../uf-wp-<id>` on branch `dev/wp-<id>`. Do not read generated files. Finish with: tests for the
  > new behaviour (analytic or published reference for physics), the test filter green, `/regen` if a declaration changed, the docs named in
  > the row, a line in `CHANGELOG.md` (top section), a paragraph in `docs/design/history.md`, the WP's status here set to `done <date>`, and
  > one commit. Report in under 200 words: what changed, test counts, anything left open.
* **Done for every WP** = the project's definition of done (`CLAUDE.md`) for what it touched: builds for net10.0 and net48, its tests and the
  default C# suite pass, Python tests if Python changed, `mkdocs build --strict` with 0 warnings if docs changed.
* **Physics WPs** (marked ⚛) follow `/physics-change` and end with a `physics-reviewer` pass on their diff; its findings are fixed or listed
  in the report.
* **Hot files.** Many WPs touch `Schema/BuiltIn*.cs`, `Engine/Compiler.cs`, `Physics/Nonlinear.cs` or `plugins/gpu/src/Kernels.cs`. Run at
  most **one WP at a time per lane** (0.3); merge a lane's WP before starting its next. Generated files that conflict at a merge are not
  resolved by hand: take either side and re-run `/regen`.
* **Models.** Opus: physics, GPU kernels, architecture, anything with a design choice. Sonnet: documentation, examples, CI, mechanical
  conversions, audits against a checklist.
* **Hard rules** (repeat them in every brief that touches the area): never touch the ZOS-API DLLs; never read `../Experiments/` (only its
  `data/` may be copied into `docs/papers/`); no NuGet packages; guard newer APIs with `#if NET`; SI units with the unit in the key.

### 0.2 Work packages

Size: S ≈ 1, M ≈ 2-3, L ≈ 4-6 agent-days. "Deps" lists the WPs that must be merged first.

#### A. Merge of `mpc-session6`

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| A1 | — | S | — | merge `origin/mpc-session6` (GPU pulse- and beam-averaged B fix E1; `mainLobeFwhmS`, `mainLobeEnergyFraction`, `satellitePeakCount`) into `dev/2026-11` | builds; suites green; generated files current | done 2026-11-25 (`5e851b5`): build 0 errors; C# 661 passed / 29 skipped; Python 443 passed; generated files current; mkdocs strict 0 warnings; physics review: no physics bugs, four minor findings → A2 |
| A2 | Sonnet | S | A1 | the four findings of the physics review of A1: (1) `docs/plugins/gpu.md` says the ensemble path (`EnsembleStepper`, `GpuBackend.cs:282`) *estimates* the pulse-averaged B from the entry / exit shapes (8-15 % off with Raman); (2) `GpuTests.BIntegral_PulseAndBeamAveraged_OnTheDevice_MatchTheCpu` tolerance from 1e-8 to one that survives a changed adaptive step sequence (state why in the test); (3) `PulseMetrics.MainLobe` (`PulseMetrics.cs:84-110`) handles a lobe that touches or wraps the window edge (finite width or an explicit "at the window edge" note, never "NaN fs" in the warning at `Executor.cs:190`), with a test; (4) `limitations.md`: the main-lobe energy window stops at the first local minimum (noise ripple can cut it short) | test for the edge case; filter `ValidityTests` green | done 2026-11-25 |

#### B. Writing style

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| B1 | Opus | S | — | `docs/style.md` (in `mkdocs.yml` nav and `llms.txt`), the one rule book for docs, CLI / engine messages, docstrings and comments: voice (plain, precise, present tense, say what the engine does), British spelling with a word list (polarisation, fibre, optimise, ...), the term list (one name per concept: B-integral, χ(2), carrier-resolved, ...), units and numbers (SI, unit in the key, `1e-9` vs 10⁻⁹, significant figures), warnings and errors (what happened, the element, the fix), and three page templates: **reference page** (what it is, keys → link to `options.md`, equations with conventions, validation with test names, limits), **example** (the existing `_template.md`), **tutorial**. Fold in the rules now scattered over `docs/CLAUDE.md`, `AGENTS.md` and the skills, and point those files at `style.md` | page builds strictly; the three templates exist; `docs/CLAUDE.md` links it | open |
| B2 | Sonnet | S | B1 | `build/style_lint.py`: US spellings from the B1 word list, banned filler words, a unit-less numeric key in a JSON block; baseline allowlist for today's hits; a step in the CI `docs-and-examples` job; a test in `python/tests` | CI step green on the baseline; a seeded violation fails | open |

#### C. Reference consolidation (the owner: "lots of it is plain wrong")

C1 audits are read-only and may all run at once (they need no B1). Each writes `docs/design/notes/reference-audit-<group>.md`: a table
*claim (page:line) | what the code does | evidence (file:line or test) | severity (wrong / stale / unclear / missing)*. Truth is the source:
`Schema/BuiltIn*.cs` for keys and defaults, the compiler and physics files for behaviour, the tests for validated numbers. No edits to the pages.

| WP | model | size | deps | pages (`docs/reference/`) | status |
|---|---|---|---|---|---|
| C1a | Sonnet | S | — | `system-file`, `run-spec`, `schema`, `cli`, `c-api`, `python`, `plugins`, `extending` | open |
| C1b | Sonnet | S | — | `nonlinear`, `ionization`, `delayed-light`, `gases`, `fibers`, `gain`, `oscillators` | open |
| C1c | Sonnet | S | — | `crystals`, `materials`, `coatings`, `components`, `active-elements`, `modulator`, `spatial-properties` | open |
| C1d | Sonnet | S | — | `transverse`, `branches`, `models`, `fidelity`, `memory`, `speed` | open |
| C1e | Sonnet | S | — | `instruments`, `retrieval`, `measured-data`, `results`, `studies` | open |
| C1f | Sonnet | S | — | `calculators`, `pulse-calculator`, `plotting`, `mcp`; plus `docs/concepts.md` and `docs/capabilities.md` | open |

C2 rewrites one group per WP to the B1 reference template and fixes every row of its audit (each row ends *fixed* or *kept, because ...*).
Hand-copied key tables are replaced by links to the generated `options.md`; numbers come with the test that checks them.

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| C2a | Sonnet | M | C1a, B1 | group a pages | every audit row closed; `mkdocs build --strict` 0 warnings | open |
| C2b ⚛ | Opus | M | C1b, B1 | group b pages | as C2a, and a `physics-reviewer` pass on the pages | open |
| C2c ⚛ | Opus | M | C1c, B1 | group c pages | as C2b | open |
| C2d ⚛ | Opus | M | C1d, B1 | group d pages | as C2b | open |
| C2e | Sonnet | M | C1e, B1 | group e pages | as C2a | open |
| C2f | Sonnet | S | C1f, B1 | group f pages, `concepts.md`, `capabilities.md` | as C2a | open |
| C3 | Sonnet | S | all C2 | consistency pass: cross-links, `llms.txt` descriptions, `docs/agents.md` and the skills' quick references agree with the new pages; no two pages state the same number differently | `mkdocs build --strict`; a grep list of the main numbers shows one value each | open |

Pages written by later WPs (E, P, W, X, S, Z) follow the B1 template from the start.

#### D. Examples (public roadmap item 5; the owner: Python next to the CLI in every README)

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| D1 | Opus | M | B1 | scaffolding: `pymdownx.tabbed` in `mkdocs.yml`; the template's "Run it" section with **CLI** and **Python** tabs (the Python tab uses the `ultrafast` builders, so autocompletion works); `python/tests/test_readme_snippets.py` collects the marked Python blocks of every README and runs them at quick fidelity with `ULTRAFAST_STRICT_OPTIONS=1` (slow ones under `ULTRAFAST_SLOW=1`); `build/example_lint.py` requires the Python tab for converted examples; `GettingStarted/quickstart` and `GettingStarted/spm-plate` converted as the pattern | the two examples render both tabs; the snippet test runs them; lint green | open |
| D2-* | Sonnet | S-M each | D1 | one WP per group, in this order: `Wavefronts`, `LaserSystems`, `Nonlinear`, `Diffraction`, `Branched`, `Diagnostics`, `Components`, `GettingStarted` (rest), `DigitalTwin` (with `transforms`, `dependence-models`), `Fibres`, `Computing`, `Interop`, `AdaptiveOptics` (with `component-gallery`). Each example of the group: README to the template, `example.json`, the Python tab, numbers re-checked with `/example-check` | group passes `build/example_lint.py` and the snippet test; quoted numbers match a fresh run | open |
| D3 | Opus | S | — | engine output of the Fox-Li radius per round trip; fourth panel of `Branched/cavity-and-coherence` | the last value equals today's final radius; the curve converges to the ABCD eigenmode | open |
| D4 | Opus | M | D1 | complex-field shaping example with a phase-only SLM (double-phase / Bolduc; the algorithms exist, the end-to-end path is untested) | shaped field against the target: overlap stated and tested | open |
| D5 | Opus | S | — | trace the deformable-mirror wavefront-sensor reading 8 % below 2 × poke in `AdaptiveOptics/component-gallery` (bug-hunter style: minimal failing test, cause, fix or documented reason) | cause stated; test | open |
| D6 | Sonnet | S | all D2, C3 | landing pages: `docs/index.md`, `docs/examples/README.md`, `docs/concepts.md` links, the plugin pages' "what is checked" sections | `mkdocs build --strict`; every example listed once | open |

#### Q. Quality gates in CI (public roadmap item 7)

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| Q1 | Sonnet | S | — | mypy (installed in `../.venv`, 2.4.1) on `python/src/ultrafast/build.py`, `types.py` and `calc/`; a `mypy.ini` / `pyproject` section; fix the errors (no blanket `ignore`); a CI step in the `python` job; `python/CLAUDE.md` updated | CI step green; a seeded type error fails | open |
| Q2 | Sonnet | S | — | coverage on a Linux CI job: `dotnet-coverage` (a dotnet tool, not a package reference) for the C# suite and `pytest-cov` for Python; reports as artefacts and a summary line; no threshold at first | job green; numbers in `docs/testing.md` | open |
| Q3 | Sonnet | S | G0 | PoCL in CI: install `pocl` on the Linux runner so `GpuTests` run on a CPU OpenCL device with FP64 | `GpuTests` run (not skip) in CI | open |
| Q4 | Sonnet | S | — | net48 under Mono on the development Mac (`brew install mono` documented in `docs/testing.md` and the `/verify` skill; CI already runs net48 under Mono on Linux) | `/verify` runs net48 locally when Mono is present | open |

#### AG. Agents (public roadmap item 1; owner: no live scoring runs in these WPs)

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| AG1 | Sonnet | S | — | split the user pack from the development rules: the shipped pack (`python/src/ultrafast/agents/pack/`, `build/sync_skills.py`) gets a user `AGENTS.md` and `llms.txt` with nothing of the repository's internals; the repository's `AGENTS.md` stays for developers | a test greps the shipped files for repository terms (`BuiltIn`, `CHANGELOG`, `/regen`, ...) and finds none | open |
| AG2 | Opus | M | — | MCP tools `twin_design` (the next measurement to take) and `twin_track` (drift), `ultrafast twin init` through the server (`python/src/ultrafast/mcp/tools.py`, `policy.py`); each with its refusals and a budget test; `docs/reference/mcp.md` | MCP tests green (handshake lists 15 tools) | open |
| AG3 | Sonnet | S | — | transcript adapters in `tests/agent-evals/run_live.py` for Codex and Cursor sessions (read their documented transcript formats), the same scorers; tested on hand-made replay transcripts; the owner runs the live scores (Sonnet, Haiku, a third family) later | scorers give the same verdict on a replay in each format | open |

#### E. FP32 devices and GPU speed (public roadmap item 2; decision E18)

PoCL 7.2 (Homebrew) is installed on the development Mac: a CPU OpenCL device **with FP64**, so double-precision kernels are checked for parity
here; Apple's `OpenCL.framework` (Apple GPU, FP32 only) checks the single-precision path. Speed is measured later on the RTX 3060 Ti (G10).

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| G0 | Opus | S | — | make the PoCL device reachable on macOS (`OpenClDevice.cs:69-76` tries `OpenCL.framework` first; use `ULTRAFAST_OPENCL_LIBRARY` or an ICD loader such as Homebrew `ocl-icd`, and/or list both); `ultrafast devices` shows the Apple GPU (single precision only) and PoCL (FP64); `GpuTests` run on PoCL; document in `docs/plugins/gpu.md` and `docs/testing.md` | `GpuTests` pass on PoCL on the Mac (list any that fail as new WPs) | open |
| G1 ⚛ | Opus | L | G0, A2 | `compute.precision: "single"`: the state, the linear step and the nonlinear operator in FP32 with compensated (Kahan / pairwise) sums for energy and B; kernels built from one source with a `real` type; every single (and mixed) run warns with the expected error from the FP32 study; runs needing more than 70 dB of dynamic range refused as for `mixed`; `docs/reference/speed.md` table for the Apple GPU | energy 1e-6, spectrum L1 1e-4 against double on the `speed.md` cases; warning tested; refusal tested | open |
| G2 | Opus | S | G1 | FP32-only OpenCL devices usable (`OpenClDevice.cs:298-305`: `Usable` when FP64 or single precision is asked); `auto` never downgrades silently: it picks an FP32-only device only with `precision: "single"`, and says so; integrated GPUs and Apple silicon named in `docs/plugins/gpu.md` and `docs/install.md` | test with a fake FP32-only device; Apple GPU runs a single-precision case | open |
| G3 | Opus | S | — | single-precision cuFFT plan for `mixed` (today the mixed path uses the built-in FFT); compile here, test on the 3060 Ti in G10 | builds; CPU fallback unchanged | open (needs CUDA to check) |
| G4 ⚛ | Opus | M | G0 | plasma term of `Chi2Problem` on the device: per-field multiphoton rate table, density integral, Drude index and loss in the coupled-wave kernel; 1-D first, then spatial | device equals the CPU to 1e-9 on PoCL for the five `Chi2IonizationTests` cases | open |
| G5 ⚛ | Opus | L | G0, W3 | rate-equation gain with spectrally resolved ASE on the device (inversion and saturation update) | a regenerative-amplifier scan equals the CPU to 1e-9 on PoCL | open |
| G6 | Opus | M | G0 | instrument read-outs on the device: FROG, d-scan, cross-correlator traces as batched 2-D transforms | traces equal the CPU to 1e-9 on PoCL | open |
| G7 ⚛ | Opus | L | G4, G1 | spatial χ(2) and spatial mixed / single precision on the device | parity 1e-9 (double) on PoCL; energy 1e-6 (reduced precision) on the Apple GPU | open |
| G8 | Opus | M | G0 | multi-GPU ensembles: one study's points split over devices | two devices (for example two PoCL contexts) give the one-device result bit for bit | open |
| G9 | Opus | L | G2 | native Metal back-end, FP32 (no NuGet: Objective-C runtime through P/Invoke, kernels in Metal Shading Language generated from the OpenCL source), so the engine keeps working if Apple removes OpenCL | Metal equals the OpenCL single-precision result to FP32 rounding on the G1 cases | open |
| G10 | Opus | M | G1-G8 | owner's session on the RTX 3060 Ti: CUDA and OpenCL parity, the cuFFT plan, speed tables in `speed.md` | all `GpuTests` pass on CUDA; tables updated | open (owner's machine) |

#### P. Engine accuracy and elements (public roadmap item 4) — one lane, in this order

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| P1 ⚛ | Opus | S | — | glass in `beamSplitter` and `polarizingSplitter` (per-port glass on the transmitted and reflected paths) | equals the splitter plus a `plate` in each arm to 1e-12 | open |
| P2 ⚛ | Opus | S | — | delayed-response window planning (`Engine/GridPlanner.cs`) for generic `timeResponse` and gas rotational revivals | the planned window covers the response memory; test with a 10 ps revival | open |
| P3 | Opus | S | — | non-collinear OPA crossed-beam overlap test | gain loss against the analytic Gaussian-overlap average | open |
| P4 ⚛ | Opus | S | — | built-in metal mirrors Ag, Au, Al: n,k **fetched** from a published table (Rakić et al., Appl. Opt. 37, 5271 (1998), or Johnson & Christy 1972; source and URL in the catalogue entry), never typed from memory | 45° silver: ellipticity of circular light against the Fresnel value; catalogue status `verified` | open |
| P5 | Opus | M | — | `schemas/study.schema.json` from a `ComponentSpec("study")` (keys now only in `docs/reference/studies.md`), generated and checked like the other schemas; Python `study` / `scan` typed from it | regen test covers it; a wrong key in a study is an error under strict options | open |
| P6 | Opus | S | — | retrieval: multi-trace from the CLI; XFROG with a measured reference file | CLI test per mode | open |
| P7 ⚛ | Opus | M | — | field snapshots along z inside an element: `outputs.zSnapshots: N`; memory in the memory estimate | the snapshot at z = L equals the element output to 1e-12 | open |
| P8 ⚛ | Opus | M | — | the Kerr-lensed beam carried to later elements (run-time beam override after a `kerrLens` element) | Herriott cell with a Kerr plate per pass against `cellMode` to 1 %; P = 0 bit-identical | open |
| P9 ⚛ | Opus | M | — | carrier frame: the window frame follows the pulse carrier | Schulte result independent of `snapCarrier` to 1e-3; 10 fs soliton exact on the default grid | open |
| P10 ⚛ | Opus | M | — | frequency-dependent effective area and a user-set shock time (Lægsgaard form; `future-plugins.md` C1) | constant table equals today to 1e-12; published dispersive-wave case | open |
| P11 ⚛ | Opus | L | P9 | carrier-resolved path for pulses near one optical cycle (SPM reaching zero frequency) | the three KNOWN checks of `media.py` pass | open |
| P12 | Opus | M | — | `multiFrog` two-trace case ending in the time-reversed twin (`docs/papers/kida-2014-double-frog/repro_2.py`) | fixed (initial guess / step order) or documented as a limitation with the cause | open |
| P13 ⚛ | Opus | M | — | birth phase of tunnelling electrons in the drift energy | against Corkum, PRL 71, 1994 (1993) | open |

#### W. Waveguides and gain

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| W1 ⚛ | Opus | M | P7 | hollow-capillary gas waveguide: check first what the hollow-core gas fibre model already does; add the Marcatili-Schmeltzer EH₁₁ mode (loss and dispersion, higher modes optional) | loss and β₂ against Marcatili & Schmeltzer (1964) to 1e-6; a published hollow-fibre compression case | open |
| W2 ⚛ | Opus | M | W1 | tapers: core radius and pressure along z (gradient fill) | a uniform taper equals W1; a linear pressure gradient against the analytic effective pressure | open |
| W3 ⚛ | Opus | L | — | saturated rate-equation gain in fibres with spectrally resolved ASE (Giles-Desurvire model, forward and backward ASE) | small-signal gain and saturated output against a published Yb-fibre amplifier; energy closes to 1e-6 | open |

#### X. Cross-polarised-wave generation (the 1-D version exists)

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| X1 ⚛ | Opus | M | — | XPW in the spatial model | the 1-D result recovered for a plane wave to 1e-9; Gaussian-beam efficiency against the published reduction | open |
| X2 ⚛ | Opus | M | X1 | XPW in the carrier-resolved model | equals the envelope result for a long pulse to 1e-6 | open |
| X3 | Opus | M | X1, G0 | XPW on the GPU (1-D and spatial) | parity 1e-9 on PoCL | open |

#### S. Single-shot cross-correlator as a component

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| S1 ⚛ | Opus | M | — | design note, then the component: crossed beams in the crystal propagated with the χ(2) stepper (today `Instruments/CrossCorrelator.cs` forms the trace analytically) | the trace equals today's instrument in the thin-crystal limit to 1e-6 | open |
| S2 ⚛ | Opus | M | S1 | air scatter, stripe filter, face reflections (ghost pre-pulses), fibre-array detection | each effect against its analytic value (ghost delay 2nL/c and amplitude R², scatter floor) | open |

#### Z. Zemax plugin depth (decision E19; needs the owner's OpticStudio to check)

Built against `tools/ZosApiStubs` and the fake OpticStudio; each WP adds steps to `HARDWARE-CHECKLIST.md` that the owner runs. ZOSPy is read
as a guide only, never shipped or required; closely ported logic carries attribution. The ZOS-API DLLs are never touched.

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| Z1 | Opus | M | — | our own ZOS-API connection core (net48): standalone application, extension and interactive modes, `ZOSAPI_NetHelper` initialisation, licence and version checks, clean shutdown; `ZosOpticStudio.cs` split into connection, system access and analyses | builds against the stubs; fake-path tests unchanged; checklist steps written | open |
| Z2 | Opus | M | Z1 | Zernike aberrations (Zernike standard coefficients) → engine wavefront element | fake-path test against known coefficients | open |
| Z3 | Opus | S | Z1 | paraxial ABCD matrices from operands | fake-path test against a thick lens | open |
| Z4 | Opus | M | Z1 | prism pairs and Martinez stretchers → dispersive engine elements (GDD and TOD from the ray-traced optical path against wavelength) | against the analytic prism-pair and grating-stretcher GDD | open |
| Z5 | Opus | S | Z1 | crystal surfaces (orientation and material mapped to engine crystals) | fake-path test | open |
| Z6 | Opus | S | Z1 | menu-integrated OpticStudio extension (Programming tab) | builds; checklist step | open |
| Z7 | — | — | Z1-Z6 | the owner runs `HARDWARE-CHECKLIST.md` on OpticStudio | checklist ticked; fixes as new WPs | open (owner) |

#### V. Close-out

| WP | model | size | deps | scope | acceptance | status |
|---|---|---|---|---|---|---|
| V1 | Sonnet | S | all above | `/verify` on `dev/2026-11`; `/regen`; `CHANGELOG.md`, `PROJECT_STATUS.md` counts, `docs/testing.md`; `ROADMAP.md` = `docs/roadmap.md` moved to "done"; sections 2-7 of this file pruned; merge to `main` after green CI | `/verify` green; CI green | open |

### 0.3 Lanes and order

A lane is a sequence that shares hot files; lanes run in parallel (at most four code lanes at once).

| lane | sequence | shares |
|---|---|---|
| docs | B1 → B2; C1a-f (any time, all at once) → C2a-f → C3; D1 → D2-* (one group at a time) → D6 | docs only |
| tooling | Q1, Q2, Q4, AG1, AG3 (any order, in parallel), AG2 | CI, Python |
| GPU | G0 → (Q3) → G1 → G2 → G9; G0 → G4 → G7; G0 → G6; G0 → G8; G3 any time; X3 after X1; G5 after W3; G10 last | `plugins/gpu/src/Kernels.cs` |
| engine | P1 → P2 → P3 → P4 → P5 → P6 → P7 → P8 → P9 → P10 → P11 → P12 → P13; D3, D5 between them | `BuiltIn*.cs`, `Compiler.cs` |
| physics-large | W3; X1 → X2; S1 → S2; W1 (after P7) → W2 | `Physics/`, `Instruments/` |
| zemax | Z1 → Z2 … Z6 → Z7 | `plugins/zemax/` |
| close | A2 first; V1 last | — |

Suggested first wave (all independent): A2, B1, C1a-f, G0, P1, Q1, AG1, Z1.

## 0b. Completed work packages (2026-11-17 to 2026-11-18)

| WP | contents | status |
|---|---|---|
| WP-1 | design docs consolidated into this folder; engine items from a user's 46-pass MPC report (structured validity and strict switch, zero-frequency false positive, warnings naming every element, pulse- and beam-averaged B, FWHM peak count, radial model through `sameAs` passes); ionisation: negligible-ionisation pre-check and the plasma term on the GPU; QPM sliver steps; dropped `ionization` / `kerrLens` keys on plates; mirror ellipticity test; glass on polarisers; lens thickness note; `uf.scan(fidelity=)` with a run file | done 2026-11-17 ([history](history.md), Stage 11) |
| WP-1b | memory safety (Phase 3 of the 2026-11-17 plan), a session of its own: fitting residual cache (keep scalars, latest and best results), `keepProfiles: used` in fit and study evaluations, a core memory estimate with an optional limit (`compute.memoryLimitBytes`), GPU free memory and concurrent slots, bounded kernel caches, Python `uf.estimate_memory` and pre-flight, `docs/reference/memory.md` | done 2026-11-17 ([history](history.md), Stage 12) |
| WP-2 | Python surface: `ultrafast.calc` (pulse, crystal, cell, beam / mode matching, nonlinear estimates; Lorentzian and asymmetric-exponential TBP shapes; the phase-matching calculator accepting `noncollinearDeg` for `sfg`; no `ultrafast-lite`, decision E7); plotting styles `ultrafast` / `paper` / `science` (decision V1), schematics for branched and sequential systems (loops, routing, glyphs, parameters, deterministic), wavelength-time-intensity 3-D plot, evolution animation | done 2026-11-17 ([history](history.md), Stage 12); the MCP slice moved to WP-2b |
| WP-2b | MCP first slice, a session of its own (decision E8): `ultrafast-mcp` in `python/src/ultrafast/mcp/` (`server.py` hand-written JSON-RPC 2.0 over stdio, `worker.py` persistent sandboxed subprocess killed and respawned on timeout, `policy.py` refusals); tools `describe`, `validate`, `run` (with fidelity; returns the fidelity report, warnings and `validity`), `scan`, `explain_warning`, `plot`; resources (schemas, fidelity table, examples index); `ultrafast_twin serve` starts it (fix its docstring and `plugin.json`); tests (handshake, 6 tools, each refusal, a call over budget killed and the next one works); `docs/agents.md` MCP section with Claude Code / Codex config. Still on the roadmap after it: `compare_to_measured`, `fit`, prompts, agent pack, evals | done 2026-11-18 ([history](history.md), Stage 14): first six tools, resources, worker and policy as planned; the server speaks both protocol eras (2026-07-28 and the `initialize` revisions); `compare_to_measured`, `fit`, prompts, the agent pack and the evals were added on 2026-11-18 ([history](history.md), Stage 15; what is left is in section 4) |
| WP-3 | WP9 slice 1 (`_template.md`, `example.json`, `build/example_lint.py` in CI, wave 1: the 12 S5 examples); release: version 1.0.0 everywhere, CHANGELOG folded into 1.0.0, `release.yml` with version check, tests, `twine check`, TestPyPI / PyPI trusted publishing behind protected environments (decision E4, no sdist E5), pip install docs; public roadmap in three blocks (`docs/roadmap.md` = `ROADMAP.md`); final docs; LaTeX report (not committed) | groundwork done 2026-11-18 ([history](history.md), Stage 14): version 1.0.0, `release.yml` (version gate, `twine check`, TestPyPI / PyPI behind environments), install docs, owner checklist, WP9 slice 1, public roadmap, `build/example_lint.py`; **open:** the owner's PyPI steps ([releasing](../releasing.md#publishing-to-pypi-maintainers-decisions-e4-e5)), the LaTeX report |

## 1. Release to 1.0.0 (needs the owner)

| item | class | size | acceptance | blocked by |
|---|---|---|---|---|
| First run of `release.yml` on a `v1.0.0rc1` tag; fix the fallout (Linux / Windows NativeAOT jobs, wheel in a fresh venv) | internal | M | all jobs green; TestPyPI install in a fresh venv on Linux, Windows, macOS runs the quickstart, a strict example, `ultrafast devices`, the `ultrafast-mcp` handshake | the owner creates the tag, the PyPI / TestPyPI pending publishers and the two GitHub environments ([releasing](../releasing.md#publishing-to-pypi-maintainers-decisions-e4-e5)) |
| Real PyPI upload of 1.0.0 | internal | S | `pip install "ultrafast-engine[hosted]"` works on the three OS | the owner's explicit go-ahead (name `ultrafast-engine` checked free on 2026-11-17, decision E13) |
| GitHub Pages: Settings → Pages → Source: GitHub Actions | internal | S | site deployed from `main` | the owner |
| macOS NativeAOT wheel | internal | S | built on the owner's Mac and attached to the release | no macOS runner for this repository |

## 2. Engine (core)

| item | class | size | acceptance / notes |
|---|---|---|---|
| Kerr-lensed beam carried to later elements (today `kerrLens` acts inside one element; the beam leaving it is not carried on) | core | M | run-time beam override after a `kerrLens` element; Herriott cell with a Kerr plate per pass against `cellMode` (same lens per pass) to 1 %; P = 0 bit-identical. Physics change: `/physics-change` |
| Field snapshots along z inside an element (for evolution maps and videos within one fibre or crystal) | core | M | `outputs.zSnapshots: N` stores N fields per element; memory accounted by the memory estimate; snapshot at z = L equals the element output to 1e-12 |
| Carrier frame: move the window frame to the pulse carrier (a dispersive pulse off the lattice drifts by GDD × offset × L); `snapCarrier` still changes the Schulte cell by 1.4 % in bandwidth | core | M | Schulte result independent of `snapCarrier` to 1e-3; 10 fs soliton exact on the default grid |
| Pulses near one cycle (SPM reaching zero frequency): a carrier-resolved path | core | L | the three KNOWN `media.py` checks pass |
| Crystal band gaps from a published optical-gap measurement (BBO, LBO, KDP, KTP, LiNbO₃ are estimated from the UV edge, `unverified`); the plasma term of `Chi2Problem` on the GPU | core | S | the owner supplies the sources; catalogue status `verified`; device parity 1e-9 |
| Built-in metal mirrors (Ag, Au, Al n,k) | core | S | data read from a published table (Rakić et al., Appl. Opt. 37, 5271 (1998), or Johnson & Christy 1972), not from memory; 45° silver: ellipticity of circular light against the Fresnel value |
| Frequency-dependent effective area and a user-set shock time (see [future-plugins](future-plugins.md) C1) | core | M | Lægsgaard form; constant table equals today to 1e-12 |
| `multiFrog` two-trace case ends in the time-reversed twin in most starts | core | M | initial guess / step order fixed or documented as a limitation (`docs/papers/kida-2014-double-frog/repro_2.py`) |
| A schema for study specs (`schemas/study.schema.json`; the keys `mode`, `sampling`, `parameters`, `outputs`, `cache`, `freezeGrid`, `prefixCache`, `ensemble`, ... are documented in `docs/reference/studies.md` only), generated and checked like the other schemas | internal | M | schema generated from a `ComponentSpec("study")`; the Python `study` / `scan` arguments typed from it |
| Glass in beam splitters (`beamSplitter`, `polarizingSplitter`: the cube's glass on the transmitted and reflected paths) | core | S | per-port glass; today: a `plate` in the arm |
| GPU plasma term in spatial elements and carrier-resolved propagation; exact (series) PPT / Keldysh rates on the device instead of the table | gpu | M | parity 1e-12 for PPT |
| Delayed-response window planning for generic `timeResponse` and gas rotational revivals | core | S | planner window covers the response memory; test with a 10 ps revival |
| Non-collinear OPA crossed-beam overlap test | internal | S | gain loss against the analytic Gaussian-overlap average |
| `cellMode`: an element option or run mode, astigmatic injection, pulse (not only peak-power) Kerr lens, stability check | core | M | |
| Birth phase of tunnelling electrons (drift energy assumes a uniform birth phase) | core | M | needs a reference calculation (Corkum 1993) |
| Single-shot cross-correlator: crystal propagation, air scatter, stripe filter, face reflections, fibre array | core | L | |
| XPW / cubic Kerr in the spatial and carrier-resolved models and on the GPU | core | L | 1-D result recovered for a plane wave |
| Retrieval: multi-trace from the CLI; XFROG with a measured reference file | core | S | |

## 3. GPU (plugin `gpu`)

| item | size | acceptance |
|---|---|---|
| Spatial χ(2) and spatial mixed precision on the device | L | parity 1e-9 (double) / energy 1e-6 (mixed) on CUDA and OpenCL |
| Single-precision cuFFT plan for mixed precision (the mixed path uses the built-in FFT) | S | mixed-precision tolerances kept; speed measured |
| Carrier-resolved propagation and spatial gain on the device | L | parity 1e-9 |
| **Plasma term of `Chi2Problem` on the device** (ionisation in χ(2) crystals, Stage 15): the per-field multiphoton rate table, the density integral and the Drude index and loss in the coupled-wave kernel (1-D first, then spatial) | M | device equals the CPU to 1e-9 for the five `Chi2IonizationTests` cases; speed measured against the CPU on a 4096-point SHG with 8 photon-order rates |
| **Rate-equation gain on the device** (amplifier chains and ASE: the inversion and the gain-saturation update, with the spectrally resolved ASE) | L | a regenerative amplifier scan equals the CPU to 1e-9; the speed-up is measured on an ensemble of pump energies |
| **Instrument read-outs on the device** (FROG / d-scan / cross-correlator traces: batched 2-D transforms; today the CPU forms them from the pulse the device returns) | M | trace equals the CPU to 1e-9; retrieval and fits that call them run faster |
| **Multi-GPU ensembles** (one study point set split over devices) and **Apple Metal FP32** for quick-fidelity scans (decision P3: only after the FP32 study; the study says energy and spectrum pass, contrast does not) | M / L | two devices give the one-device result bit for bit; Metal results meet the mixed-precision tolerances |
| Ensembles with user-response kernels and fixed steps | M | ensemble equals stream-per-point to 1e-12 |
| PoCL in CI (a CPU OpenCL device with FP64) so `GpuTests` run there | S | `GpuTests` run in CI on Linux |
| AMD / Intel OpenCL and data-centre NVIDIA cards | — | needs the hardware |

## 4. Agents and MCP (extensions, v1.1.0)

Done by 1.0.0-alpha: 13 MCP tools (`study`, `backward` and the twin tools included), prompts, the agent pack (six skills, `ultrafast-agent` and `ultrafast agent`), seven evals with replays and a live scorer (`tests/agent-evals/run_live.py`), one source for the skills (`build/sync_skills.py`).

| item | size | acceptance |
|---|---|---|
| Live scores of all seven evals (only the mandatory one has been run live: Sonnet and Haiku) with a third agent family (Codex, Cursor) | M | table in `docs/reference/mcp.md`; every mandatory eval passes for three agents |
| An adapter in `run_live.py` for agents other than `claude -p` (a transcript reader for Codex and Cursor sessions) | S | the same scorers on their transcripts |
| MCP: twin tools `twin_design` (next measurement) and `twin_track` (drift), `ultrafast twin` init through the server | S | each with its refusals and a budget test |
| Split the user pack from the development `.claude/` folder (`AGENTS.md` and `llms.txt` written for users; development rules in a dev-only file) | S | a user's `AGENTS.md` mentions nothing of the repository's internals |

## 5. Documentation (WP9 remainder)

| item | size | notes |
|---|---|---|
| WP9 wave 1 done (12 S5 examples, `example.json`, lint). Waves 2-4 (plus `component-gallery`, `dependence-models`, `transforms`, which do not follow the template yet): `Wavefronts`, `LaserSystems` (several results per chain), `Nonlinear`; then `Diffraction`, `Branched` (with the cavity figure), `Diagnostics`, `Components`, `GettingStarted`, `DigitalTwin/transforms`; then the landing pages (`docs/index`, `examples/README`, `concepts`) and the plugin pages' "what is checked" sections | L | each converted example gets an `example.json`, so `build/example_lint.py` checks it |
| Fox-Li *convergence history* for `Branched/cavity-and-coherence` (the hero figure shows the final Fox-Li radius against the ABCD eigenmode and the Gaussian-Schell beam against w(z); the engine does not output the radius per round trip) | S | an output array of the radius per round trip; a fourth panel |
| Complex-field shaping example with a phase-only SLM (double-phase / Bolduc; the algorithms exist, the end-to-end SLM path is untested) | M | |
| Deformable-mirror wavefront-sensor reading 8 % below 2 × poke in `AdaptiveOptics/component-gallery` (cause not traced) | S | |

## 6. Data that needs the owner's papers

| item | notes |
|---|---|
| CS2 n2 (3.0e-18 m²/W), 90 / 10 electronic-orientational split, 1.6 / 0.2 ps; BaF2 n2 (2.0e-20 m²/W) and σ = -1.2; rotational Raman `T2At1Bar` of N2, O2, CO2, H2; other n2 values | then re-run `liu-2013`, `shimizu-1967`, `kaur-2024` |
| LBO (Kato 1994), Jundt / Gayer LiNbO3 e-ray forms | |
| Nd:YAG Stark lines (emission fit is one 1064 nm line, about 8 % of 1/τ) | |
| Stress: slice-by-slice retardation, radial / tangential lenses on the field, vector gain for a y input (Koechner, read from the source) | package of its own |

## 7. Process

| item | notes |
|---|---|
| net48 test run under Mono on the development machines (CI runs it on Linux) | |
| Code coverage on a Linux CI job (`dotnet-coverage` does not attach on macOS arm64) | |
| Linting / typing in CI: `mypy` on `build.py` and `types.py` (documented in `python/CLAUDE.md`, not run in CI) | |
