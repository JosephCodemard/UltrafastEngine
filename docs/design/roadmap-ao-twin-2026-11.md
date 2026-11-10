# Roadmap: adaptive optics, temporal shaping, conditioned transforms, digital twin, examples

Written 2026-11-09, revised the same day, on the branch `dev/2026-11`. The reasoning and the physics are in
[plan-ao-digital-twin-2026-11.md](plan-ao-digital-twin-2026-11.md). This page is what you paste into Claude Code: **five
session briefs** (S1-S5), each self-contained, with the model and advisor to use, the files to read and own, the order of
work, the tests and the acceptance criteria. Sizes: S ≈ 1 focused agent-day, M ≈ 2-3, L ≈ 4-6, XL ≥ 7.

## 1. The session map

| session | what | depends on | executor / advisor | size |
|---|---|---|---|---|
| **S0** (done, in the planning session) | CI fixes, logo, licence, README, this plan | - | - | done |
| **S1 Foundation** (**done 2026-11-09**) | Python package structure; plotting consolidation, animation, Zernike plot; examples regrouped into folders and an `ultrafast-examples` package; run-all figure scripts; optical-computing examples | S0 | Sonnet 5.5 / Opus 5.5 (1 call) | L |
| **S2 Core and spikes** | measurements that decide the design (scale, sparse spectral sampling, chromatic SLM, repetition), then the core primitives: `modulator`, in-memory maps, streaming and sparse spectral sampling, coherent tiles, fast path | S1 | Opus 5.5 / none | XL |
| **S3 AO plugin** | `ao` plugin: deformable mirror, SLM, tiled array, sensing, calibration and control, holograms, sensorless optimisers, temporal shaping | S2 | Sonnet 5.5 / Opus 5.5 (2 calls) | XL |
| **S4 Fitting and twin** | conditioned transforms in the fitting plugin; `twin` plugin; MCP server; goal files | S1 (not S2/S3: runs in parallel with them) | Sonnet 5.5 / Opus 5.5 (3 calls) | L-XL |
| **S5 Examples, docs, verification** | the AO and twin examples with plots and animations, docs, status, the full verification and the merge to `main` | S2, S3, S4 | Sonnet 5.5 / Opus 5.5 (1 call) | L |
| *S6, later, optional* | repetition and switching: Pockels cell, pulse picker, pulse-train mode (see section 6) | S2 | Opus 5.5 / none | L |

**Order.** S1 first, because it restructures the Python package and moves the examples; any example written before that would
be written twice. After S1, **S2 and S4 can run in parallel** (they own different files). S3 waits for S2. S5 is last.

```
S0 ── S1 ──┬── S2 ── S3 ──┐
           └── S4 ────────┴── S5 ── (S6)
```

## 2. Which model and which advisor

**Available to you: Sonnet 5.5 (`claude-sonnet-5-5`) and Opus 5.5 (`claude-opus-5-5`); no Fable, and a limited token budget.**
Opus 5.5 is the strongest model you have, so **nothing can advise it**: where a session needs the strongest model it runs *on*
Opus with no advisor, and the safety net is the reference-value tests and the owner's gate. Everywhere else the cheaper
combination is a **Sonnet executor with an Opus advisor**, called a few times at the decisions that matter.

*Reasoning, not measurement*: the split follows how much physics judgement each session needs against how much plumbing it
contains. Check `/usage` after S1 and move a session up or down if the numbers surprise you.

| session | executor | advisor | effort | why |
|---|---|---|---|---|
| S1 | **Sonnet 5.5** | **Opus 5.5**, one call | medium | a wide, mostly mechanical refactor and move; the risk is broken imports and links, not physics. One advisor call on the old → new name table and the example regrouping, before any file moves |
| S2 | **Opus 5.5** | none | medium | the chromatic SLM model and the streaming / sparse-sampling error control are where a wrong physics decision is expensive, so this is the one session that runs on the strongest model. Safety net: the reference tests, gate G0 (the owner reads the spike note), and one `physics-reviewer` pass |
| S3 | **Sonnet 5.5** | **Opus 5.5**, two calls | medium (medium/high if needed for the SLM pulse model) | S2 has settled the physics design; S3 is device models and algorithms with reference values. Advisor (1) before the SLM chromatic model and (2) before "done". If Sonnet cannot make a reference test pass in two attempts, switch the session to Opus for that step only |
| S4 | **Sonnet 5.5** | **Opus 5.5**, three calls | medium/high | plumbing, statistics and a server. Advisor at the multi-condition fit design (F1), the MCP security design (T6) and before "done" |
| S5 | **Sonnet 5.5** | **Opus 5.5**, one call | medium | examples, docs and verification; one call before the merge to `main`. `test-runner` (haiku) runs the long jobs |
| S6 | **Opus 5.5** | none | medium | switch physics (Pockels cell, regen timing) |

* **An advisor must be at least as capable as the executor.** Opus 5.5 advising Sonnet 5.5 is valid. If your Claude Code lets you
  choose the advisor model, choose Opus 5.5; otherwise leave the default and say so in your first message (I could not check how
  your setup selects it). Where the table says *none*, do not use an advisor.
* **Advisor calls are the expensive part.** Make them where the table says, no more: once after orientation and before the first
  edit (where listed), when a result disagrees with a reference value and two attempts did not fix it, and once before declaring
  done with the work already committed.
* **Use `test-runner` (already `haiku`) for builds and test runs.** Do not start `paper-replicator`, `bug-hunter` or
  `physics-reviewer` agents unless a brief says so; each starts cold and runs on the session's model. A `physics-reviewer` pass
  is required once, at the end of S2 and of S3.
* **Keep context small.** Give a session only its brief, read only the files its brief lists, and follow the token-economy
  rules in `CLAUDE.md` (never read generated files).

## 3. Rules that apply to every session

* **Branches and worktrees.** Cut a branch from `dev/2026-11` per session: `dev/2026-11-s1-foundation`, `-s2-core`, `-s3-ao`,
  `-s4-twin`, `-s5-examples`. Work in a git worktree if two sessions run at once. Merge into `dev/2026-11` when the
  acceptance criteria hold and CI is green; merge to `main` only after S5, `/verify` and green CI (owner's rule).
* **File ownership prevents conflicts.** Only S2 edits `src/UltrafastEngine.Core/**`. S3 and S4 edit their own
  `plugins/<name>/**`, `docs/plugins/<name>.md`, their tests and their example folders. Shared files
  (`mkdocs.yml`, `llms.txt`, `CHANGELOG.md`, `docs/testing.md`, `PROJECT_STATUS.md`, `docs/capabilities.md`, `ROADMAP.md`,
  `docs/roadmap.md`) are edited by **the session that is merging**, last; until then a session writes its lines into
  `docs/design/notes/<session>.md`. S1 is allowed to touch `mkdocs.yml` and `llms.txt` (it moves pages).
* **Generated files** (`schemas/*.json`, `python/src/ultrafast/types.py`, `docs/reference/options.md`, `python-api.md`,
  `validation-matrix.md`) are regenerated with `/regen` (and `--matrix`) once per merge; never edited by hand.
* **Definition of done** (from `CLAUDE.md`): builds for net10.0 and net48; new behaviour has a test with an analytic or
  published reference (physics); full C# suite and Python tests pass after `build_engine.py`; generated files current; docs
  (reference page, changelog, `docs/testing.md` row, `PROJECT_STATUS.md` counts); `mkdocs build --strict` with 0 warnings;
  examples run with `ULTRAFAST_STRICT_OPTIONS=1`.
* **Honesty rules.** A disagreement with a reference is a finding, never tuned away; a test tolerance is not loosened to hide a
  failure (the ePIE case of 2026-11-09 is the model: measure the spread first, then test what the algorithm guarantees);
  numbers that are only round-off are printed as bounds; unverified constants are marked.
* **Python conventions** (all sessions): the package keeps full autocompletion (builders return the generated TypedDicts);
  lowercase module names, CapWords classes; `ultrafast.build` keeps working; renamed things keep working through a shim with a
  `DeprecationWarning`.
* **CI** runs on Linux (x64 and arm64) and Windows. macOS is checked by hand. Tests must not depend on a display, a GPU, ffmpeg
  or the network.
* **Handoff.** Each session ends by committing, then writing `docs/design/notes/<session>.md` (what is done, what is not, the
  numbers measured, decisions taken, anything the next session must know), and listing its changelog lines there.

## 4. Specifications shared by several sessions

### 4.1 Python package structure (S1)

Goal: someone reading `import ultrafast` can find things by what they *are*. Lowercase module names (PEP 8) with CapWords
classes: `ultrafast.system.Sequential`, `ultrafast.components.BeamSplitter`. If you prefer the capitalised style
(`Ultrafast.System`), say so before S1 starts; it is unusual for Python and would break `import ultrafast.system`
conventions of every tool, so I recommend lowercase.

```
ultrafast/
  __init__.py        run, load, open, version; the commonly used names re-exported
  build.py           (kept) dict builders with TypedDict autocompletion; the low-level truth, unchanged API
  system/            Sequential, Branched (ports, links, loops), load / save .ufs, validation, diagram
  components/        classes or factories grouped by kind, each returning the typed dict:
                     optics (Mirror, Lens, Grating, GratingPair, Prism, Plate, Aperture, Mask, Wavefront),
                     splitters (BeamSplitter, PolarizingSplitter, Dichroic, LeakyMirror, Delay),
                     polarisation (Waveplate, Polarizer, Rotator, Faraday),
                     crystals, fibres, gain (Gain, Amplifier, Oscillator), media (Bulk, Gap)
  sources/           Pulse, Beam, measured spectra, pedestals
  instruments/       one class or factory per instrument (Spectrometer, Frog, DScan, Camera, WavefrontSensor, ...)
  algorithms/        pure-numpy / engine-driving algorithms usable on their own:
                     retrieval (FROG, d-scan, Gerchberg-Saxton), optimisation (scans, Nelder-Mead, CMA-ES wrappers),
                     zernike (fit, evaluate, Noll tables), later holography and control (the `ao` plugin adds to it)
  results/           result objects, metrics (duration, bandwidth, Strehl), saved-result loading
  plotting/          split of the 1371-line plotting.py: style, temporal, spectral, spatial, zernike, instruments,
                     diagrams, animate  (the old function names stay importable)
  materials.py, crystal.py, cells.py, laser.py, measured.py, plugins.py   (kept; thin re-exports where they move)
```

S1 starts by reading `build.py`, `core.py`, `plotting.py`, `plot.py`, `diagram.py`, `laser.py`, `__init__.py` and the Python
tests and writing the exact old-name → new-name table into `docs/design/notes/s1-foundation.md` **before moving anything**.
Rules: nothing public disappears (shims with `DeprecationWarning`); the TypedDict autocompletion test (existing) stays green;
a new test imports every old public name; `docs/reference/python.md` and `python-api.md` (generated) are updated.

### 4.2 Plotting, Zernike decomposition and animation (S1)

* **Consolidate** `plot.py`, `plotting.py`, `diagram.py` and the fitting plugin's `figures.py` into the `ultrafast.plotting`
  package with one theme (light and dark), shared helpers (SI-prefix axes, dB, before/after and target/result panels,
  convergence panels) and the old names as aliases.
* **Zernike decomposition plot** (`plotting.zernike`): input = a wavefront map (array with axes), a `wavefrontSensor` result,
  or a complex field. Panels: the wavefront map; the fitted reconstruction; the residual; a bar chart of the Noll
  coefficients (waves rms) coloured by radial order and labelled for the named modes (piston, tilt, defocus, astigmatism, coma,
  trefoil, spherical); the rms and the Strehl estimate (Maréchal) in the title. Options: `max_noll`, piston/tilt removed or not,
  one or several wavefronts side by side (for before/after correction), an animated variant that steps through a list of
  wavefronts (used by the DM correction example).
* **Core?** *Checked:* the core already has `Zernike.Fit` and `Zernike.Z` (used by the `wavefrontSensor` instrument), so the
  plot does **not** need new core work. Implement a small numpy `algorithms.zernike` (`noll_to_nm`, `evaluate`, `fit`) and
  **cross-check it against the core's coefficients** for the same map in a test (1e-6), plus orthonormality (rms 1 over the
  disc) as the analytic test. Do not duplicate more than that.
* **Animation helper** (`plotting.animate`): takes a frame function and a frame list, writes an **animated GIF with Pillow**
  (no ffmpeg on the owner's Mac) and, when `ffmpeg` is on the path, an MP4 as well; options for fps, size, a loop pause on the
  last frame, a progress bar. A budget check (`build/example_check.py`): a PNG over 300 kB or a GIF over 2.5 MB is flagged.
  Tests: a 5-frame animation writes a valid GIF with the right frame count, size and loop flag; no display needed (Agg).

### 4.3 Examples: grouped folders, an installable package, run-all scripts (S1)

Today `docs/examples/` has 43 numbered folders. Regroup them (a **proposal**; S1 confirms each placement after reading its
README):

| group | folders that move there (old number) |
|---|---|
| `GettingStarted/` | 00 quickstart, 01 spm-plate, 04 custom-index-python, 12 user-element, 18 custom-nonlinear-medium |
| `LaserSystems/` | 02 stretcher-compressor, 13 yb-multipass-cpa, 14 ti-sapphire-regen, 19 ti-sapphire-klm-haus, 20 yb-laser-system, 38 four-grating-compressor, 09 nopcpa-bbo, 42 noncollinear-opa, 22 contrast-chain |
| `Nonlinear/` | 03 mpc-argon, 06 carrier-resolved, 11 spatial-kerr, 24 filament, 25 supercontinuum, 27 ner-measured-input, 29 small-scale-self-focusing, 08 shg-lbo, 10 ppln-dfg |
| `Diffraction/` | 30 iris-diffraction, 35 tilt-fringes, 36 structured-beams |
| `Wavefronts/` | 16 position-dependent-index, 15 spatial-gain-thermal-lens (the AO examples join this group in S5) |
| `Branched/` | 31 cavity-and-coherence, 32 interferometers, 37 f2f-cep, 41 pump-probe |
| `Diagnostics/` | 17 instrument-tour, 33 modelled-autocorrelator, 34 modelled-instruments, 39 third-order-cross-correlator, 40 noncollinear-correlators, 21 measured-data, 28 delayed-light |
| `Components/` | 05 polarization-coatings, 23 components |
| `DigitalTwin/` | 26 transforms (the twin examples join this group in S5) |
| `Interop/` | 07 zemax-workflow, `cpp/` |
| `Computing/` | new (4.4) |
| `AdaptiveOptics/` | new, created empty-with-README in S1, filled in S5 |

* **Constraints to preserve** (from `docs/CLAUDE.md`): example 09 reads example 08's results (both move; the path in 09 changes to
  the new location), 26's `run-new-input.json` / `run-backward.json` use `results-fit/fitted-system.ufs`, tutorial specs use
  `plate.ufs` and `cpa.ufs`. Do the move in **one `git mv` pass**, then fix: `mkdocs.yml` nav, every markdown link, the
  `docs/CLAUDE.md` notes, `llms.txt`, `build/example_check.py` paths, the tutorials, `docs/testing.md`, CI paths, and every
  "example NN" reference in the docs (keep a table `docs/examples/README.md` with old number → new path). `mkdocs --strict` and
  `example_check.py` are the proof.
* **Run-all scripts** (the owner runs them locally; CI keeps `example_check.py` for the numbers): in every example folder a
  `make_figures.py` that runs the example, writes its hero figure(s) and, for iterative algorithms, its animation(s) next to
  it; the README references those files. A group runner `docs/examples/<Group>/make_all.py` and a top-level
  `docs/examples/make_all.py` with `--group`, `--only <name>`, `--no-animations`, `--jobs N`, `--report report.json`, which
  also checks the figure budget and prints a table (example, time, files written, problems).
* **Installable package.** *Fact:* a pip extra installs dependencies, not repository folders. So `ultrafast[examples]` pulls in a
  separate package `ultrafast-examples` (the grouped folders as package data, plus the `ultrafast-examples` command:
  `list`, `copy <group|name> <dest>`, `run <name>`). It is built locally (`build/package.py` gains `--examples-wheel`) and
  resolved with `--find-links` like the plugin extras (no PyPI). The command copies examples to a writable folder, because
  installed packages should not be written into.

### 4.4 Optical-computing examples (S1; buildable today, no new core needed)

`Computing/` shows what an optical system does in parallel and compares the engine's run time with the theoretical
processing time. Built from existing elements (`lens`, `mask` of kind `file`, cartesian grids, cameras):

| example | what | theory to compare |
|---|---|---|
| `4f-correlator` | a 4f system with a matched filter in the Fourier plane: find a pattern in a scene in one pass | correlation theorem (cross-correlation by FFT); the peak position and height against `numpy` |
| `optical-convolution` | the same 4f layout as a convolution engine (blur, edge detection, Gaussian kernel) | convolution theorem; error versus `scipy`-free numpy reference |
| `matrix-vector` | a lens-based vector-matrix multiplier (cylindrical lenses and a mask, the classic Goodman layout) | y = Ax, error versus `A @ x`, with the number of operations the optics did |
| `parallelism-and-time` | scale the problem (N × N pixels) and plot **three times**: the engine's wall time, the digital FFT time of the same operation, and the *theoretical optical* time (propagation length / c plus detector integration time) | digital cost O(N² log N), optical cost independent of N once the system is built; a table and one plot |

Each computing example prints the number of effective multiply-accumulate operations the optics performed (e.g. N² for a
convolution kernel of N² taps ⇒ N⁴ operations done in one light transit), the engine's measured wall time, and the optical
transit time, and states plainly that the engine *simulates* the optics on a digital machine, so its wall time is not the
physical time: the comparison is about scaling. A one-page README per example, one hero figure, and one animation (the scene,
the filter, and the output forming).

### 4.5 The example quality bar (all sessions that write examples)

| rule | detail |
|---|---|
| a hero figure | one figure that tells the story at a glance (before / after, target / result), consistent theme from `ultrafast.plotting` (light and dark), units on every axis, the analytic or target value on the same axes, ≤ 300 kB PNG |
| an animation for every iterative algorithm | GS, SPGD, modal search, phase locking, closed-loop correction, twin fits: the thing converging, with the metric curve beside it. 6-15 s, 15-20 fps, ≤ 2.5 MB |
| no hidden dependencies | GIF via Pillow; MP4 optional when `ffmpeg` exists |
| numbers match a fresh run | `/example-check`; round-off is printed as a bound |
| fast | under 2 minutes on a laptop CPU; the full-size SLM example under 5 minutes and says so |
| self-checking | ends by asserting the key number against its reference, so a regression breaks the check |
| showcase | `docs/gallery.md` and the README hero animation are assembled from the examples by `build/gallery.py` |

## 5. The session briefs

Each brief can be pasted as the first message of a fresh Claude Code session (after `/model` and, if available, the advisor
choice from section 2). Start every session in a new worktree on its own branch. Each brief ends with a **handoff**.

---

### S1 Foundation (Sonnet 5.5, advisor Opus 5.5)

**Goal.** Make the Python package and the examples easy to navigate and pleasant to look at, before anything new is added to
them: package structure, one plotting module with animation and a Zernike plot, grouped examples with an installable package
and run-all scripts, and the optical-computing examples.

**Branch.** `dev/2026-11-s1-foundation` from `dev/2026-11`.

**Read first, nothing else.** `CLAUDE.md`, `python/CLAUDE.md`, `docs/CLAUDE.md`, section 3 and 4.1-4.5 of this page,
`python/src/ultrafast/{__init__,build,core,plotting,plot,diagram,laser}.py` (line ranges for the big ones; never `types.py`),
`docs/examples/README.md`, `build/example_check.py`, `docs/reference/python.md`.

**Files you own.** `python/**` (not the generated `types.py` / `python-api.md`), `docs/examples/**`, `docs/tutorials/**` (path
fixes only), `build/example_check.py`, `build/package.py` (the examples wheel), `build/gallery.py`, `mkdocs.yml` and
`llms.txt` (moves only). You do **not** touch `src/**` or `plugins/**` (except `plugins/fitting/python/.../figures.py` to fold
it into the plotting package, keeping its names).

**Order of work.**
1. **The one advisor call (Opus 5.5), after reading**, before any edit: the old-name → new-name table (4.1) and the example regrouping table (4.3).
2. Python structure (4.1): create the packages, move code, add shims, add the import-every-old-name test. Keep
   `ultrafast.build` as it is.
3. Plotting (4.2): split and consolidate, theme, helpers, `animate`, `algorithms.zernike`, the Zernike decomposition plot and
   its tests (cross-check against the core's `wavefrontSensor` coefficients).
4. Regroup the examples in one `git mv` pass, fix every link and path (4.3), `mkdocs --strict`, `example_check.py` (all).
5. Per-example `make_figures.py`, group and top-level `make_all.py`, the budget check, `build/gallery.py`.
6. The examples package and the `ultrafast-examples` command (`build/package.py --examples-wheel`); `pip install` it into a
   fresh venv and run `ultrafast-examples list`.
7. The `Computing/` examples (4.4) with their figures and animations; add them to the gallery.
8. Docs: `docs/reference/python.md` structure section, `docs/examples/README.md` (group index and old → new table),
   changelog lines in the notes file.
9. Commit; no second advisor call (the budget is one); the tests and `mkdocs --strict` are the check.

**Tests.** Import of every old public name; TypedDict autocompletion test still green; Zernike orthonormality and core
cross-check; GIF writer; figure-budget check; `ultrafast-examples copy` into a temp folder; the full Python suite after
`build_engine.py`; `example_check.py` 0 problems; `mkdocs --strict` 0 warnings.

**Acceptance.** `python3 -c "from ultrafast.system import Sequential, Branched; from ultrafast.components import BeamSplitter"`
works; old imports work with a `DeprecationWarning`; `docs/examples/make_all.py --only Computing` produces every figure and
animation within budget; the example check passes with the new paths; the examples wheel installs and lists the groups.

**Handoff.** `docs/design/notes/s1-foundation.md`: the old → new table, the example move table, anything left, the commands to
regenerate figures, the animation budget numbers.

**Progress (2026-11-09, branch `dev/2026-11-s1-foundation`; details in `docs/design/notes/s1-foundation.md`).**

| step | state |
|---|---|
| 1 advisor call (Opus 5.5) | done; its corrections are applied (public-surface snapshot first, no module named like a figure function, shim warns, fold decision written down) |
| 2 Python structure | **done**: `system`, `components` (optics, splitters, polarisation, crystals, fibres, `amplifiers`, media), `sources`, `instruments` (18 real factories), `algorithms`, `results`; CapWords names are aliases of the `build` functions, so typed returns are unchanged; builders kept in `build` only are listed in a test |
| 3 plotting | **done**: `plotting.py` split into a package (`style, data, time_domain, spectral, run_figures, spatial, instrument_panels, diagrams, zernike, animate`); every 2.2.0 public and underscore name still imports (`python/tests/data/public_surface_2_2_0.json`); `ultrafast.diagram` is a shim with a `DeprecationWarning`; matplotlib and Pillow stay lazy |
| Zernike and animation | **done**: `algorithms.zernike` (orthonormal to 1e-12 by polar quadrature; agrees with the engine's `wavefrontSensor` coefficients to 1e-6), the decomposition figure (one or several wavefronts, complex-field input, animated variant), GIF writer with a size-budget check |
| 4 regroup examples | **done**: one `git mv` pass into 10 groups (+ `Computing`, `AdaptiveOptics`); 138 files re-linked; old numbers kept in the `docs/examples/README.md` table; `example_check.py` takes a name, group or old number |
| 5 `make_figures.py`, `make_all.py`, budget check, `build/gallery.py` | **done**: a `make_figures.py` in every example (42), `make_all.py` per group and at the top (`--group --only --no-animations --jobs --report`); the figure budget is checked; `docs/gallery.md` is generated |
| 6 examples wheel and `ultrafast-examples` | **done**: `build/package.py --examples-wheel` (3.8 MB), `list` / `copy` / `run`, extras `animate` and `examples`; tested from a fresh venv |
| 7 optical-computing examples | **done**: `4f-correlator`, `optical-convolution`, `matrix-vector`, `parallelism-and-time` with hero figures and animations (all within budget) |
| 8 docs | **done**: package structure and Zernike / animation sections in `docs/reference/python.md`, regenerated `python-api.md`, `docs/examples/README.md`, architecture code map, releasing page, notes and changelog lines in `docs/design/notes/s1-foundation.md` |
| outside the brief (owner's requests) | **done**: brand CSS for the docs sidebar and links, long-logo footer on the plugin READMEs, `CONTRIBUTING.md`, `ROADMAP.md` and `tests/components/README.md`; Google Search Console verification tag in `overrides/main.html`; the `.gitignore` fix that let CI see `ultrafast.results` |

Findings that change later sessions:

* `lens` has no cylindrical or separate x/y focal length, so the Goodman vector-matrix multiplier of 4.4 cannot be built from
  existing elements. The `matrix-vector` example will use a lens as an exact DFT matrix and a 4f system for circulant matrices;
  a cylindrical lens is a note for S2.
* `plotting/zernike.py` and the fitting plugin's `figures.py`: the plugin figure stays in the plugin (it reads the fitting
  result format and is registered only when the plugin loads); it uses the stable names re-exported by `ultrafast.plotting`.
  This is the "fold" of 4.2, with the reason written down.
* Full verification of the finished S1 is recorded in `docs/design/notes/s1-foundation.md` (sections 9 and 10). **S1 was merged to `main` on 2026-11-09 (`a875aec`), before S5, at the owner's request;** S2 starts from `dev/2026-11` in a new session (what it should know first: section 11 of the notes).
* **Open for the owner:** repository size (section 8, point 1): only the four Computing animations (39-329 kB each) are committed so far; the other 38 examples draw a generic hero into a git-ignored `figures/` folder.

---

### S2 Core and spikes (Opus 5.5, no advisor)

**Goal.** Measure first, then build the few core primitives that the AO plugin, the tile array and the shapers need:
`modulator`, in-memory maps, streaming and sparse spectral sampling, a coherent tile source, a fast spatial path. Core physics
only: no device catalogue.

**Branch.** `dev/2026-11-s2-core`.

**Read first.** `CLAUDE.md`, `AGENTS.md`, `docs/concepts.md`, `docs/reference/transverse.md` (masks, profiles),
`docs/reference/spatial-properties.md`, `docs/reference/speed.md`, `docs/reference/components.md#aberrations`,
`src/UltrafastEngine.Core/Elements/MaskElement.cs` and `Transverse/*.cs` (line ranges), the plan page (B1-B4, B8),
the skills `engine-physics` and `physics-change`.

**Files you own.** `src/UltrafastEngine.Core/**`, `src/UltrafastEngine.Native/**`, `tests/UltrafastEngine.Tests/**`,
`docs/reference/*` for the new elements, `docs/design/notes/spikes.md`. The generated files are regenerated once, at the end.

**Order of work.**
1. **Spikes**, written up with numbers in `docs/design/notes/spikes.md`:
   * **S1-scale:** cartesian profile 1920 × 1080 and 4096 × 4096, one frequency: time and memory of a `mask file`, one
     propagation, one camera; CPU, GPU if present.
   * **S2-sparse:** 800 nm / 30 fs through a phase mask: K = 4, 8, 16, 32 frequencies against a dense run; the error in focal peak
     and pulse duration.
   * **S3-chromatic:** a blazed grating as a `mask file` and as a thin `bulk` + `indexProfile`: does a pulse disperse like a
     grating of the same period?
   * **S4-loop:** time per trial of a 128² spatial run; compile-once-run-many potential.
   * **S5-tiles:** 7 tiles as a Branched system against a coherent sum on a cartesian grid.
   * **S6-multi-condition:** read `plugins/fitting/src/Fitting.cs` and report whether two datasets can be fitted jointly (input
     for S4, not built here).
   * **S7-repetition:** read how the amplifier handles a regenerative switch and round trips, and how `repRateHz` / steady state
     is used (the input for the Pockels comment in section 6).
2. **Decide** (no advisor): with the spike numbers, choose streaming, sparse sampling and the chromatic rules, and write the
   reasoning and the alternatives rejected into the notes file, because nobody reviews this choice before the owner does. **Gate G0: stop and show the owner the spike note if any spike contradicts the plan** (a full
   1920 × 1080 SLM at pulse bandwidth is the biggest risk).
3. **C1 `modulator`:** cell grid (count, pitch, offset, rotation), array of complex transmission / OPD / phase;
   `fillFactor`, `crosstalkKernel`, `quantisationLevels`, `phaseWrapRad`; `chromatic`: `opd`, `phaseAtDesign`, `index`;
   sub-pixel averaging (reuse the `mask` edge-averaging code).
4. **C2 in-memory maps:** arrays through the Python and C APIs and the run spec without files; a `map` callback; a compile
   cache so a loop compiles once.
5. **C3 streaming and sparse spectral sampling** for linear spatial chains, with a reported error estimate.
6. **C4 coherent tile source** (`initialProfile` kind `tiles`).
7. **C5 fast path:** cw / narrow-band spatial runs; zonal sensor outputs (slope maps, wavefront map as a stable array).
8. **C6 GPU parity** of C1/C3/C4 on the spatial back-end (skipped without a device).
9. `physics-reviewer` on the diff, fix findings; `/regen`; merge wave: docs, changelog.

**Tests (reference values).** Pure tilt moves the focus by f·θ; defocus gives the thin-lens shift; N-level blazed grating first
order [sin(π/N)/(π/N)]²; replica orders at λ/pitch; a ramp disperses a pulse like a grating (S3); streamed result equals the
all-frequency result (1e-12); the sparse error estimate bounds the true error found in S2; N in-phase tiles give N² peak,
phase error σ costs exp(−σ²), grating-lobe angle λ/pitch; cw equals the pulse at the carrier (1e-9); N trials equal N file runs
(1e-12).

**Acceptance.** All of the above; full C# suite and Python tests green; net10.0 and net48 build; `mkdocs --strict`; the spike
note exists with numbers; a 1920 × 1080 single-frequency propagation runs within the budget the spike recorded.

**Handoff.** `docs/design/notes/s2-core.md` with the API of each primitive as shipped (keys, units, defaults), the spike
numbers, and what the `ao` plugin may rely on.

---

### S3 AO plugin (Sonnet 5.5, advisor Opus 5.5)

**Goal.** The `ao` plugin: deformable mirror, SLM, coherent tiled-aperture array; wavefront sensing with Zernike modal and
zonal reconstruction; calibration and control; holograms; sensorless optimisers and phase locking; temporal shaping
(`spectralShaper`, `aopdf`). All devices are **parametric**: no vendor data (owner decision 4), up to a full 1920 × 1080 SLM.

**Branch.** `dev/2026-11-s3-ao` (after S2 is merged into `dev/2026-11`).

**Read first.** `CLAUDE.md`, `plugins/CLAUDE.md`, `plugins/fitting/README.md` (as the model plugin), `docs/reference/plugins.md`,
the plan page B1-B9, `docs/design/notes/s2-core.md`, `docs/design/notes/spikes.md`, `docs/reference/retrieval.md`
(Gerchberg-Saxton section).

**Files you own.** `plugins/ao/**` (new), `docs/plugins/ao.md`, `tests/UltrafastEngine.Tests/Ao*Tests.cs`,
`python/src/ultrafast/algorithms/` additions for holography and control (the Python side of the plugin).

**Order of work** (package ids from the first version of this plan in brackets).
1. **Scaffold** (AO0): `plugin.json` (`requires: ["fitting"]` if the estimators are reused), the Python module, the docs page.
2. **Devices:** `deformableMirror` (AO1), `slm` (AO2), `tiledArray` (AO3), each compiling to the core `modulator` or tile source.
   **Advisor call 1 (Opus 5.5)** before the SLM: the chromatic model (settled in S2) and the pulse test.
3. **Sensing and control:** `shackHartmann` model with Zernike modal and zonal (Southwell) reconstructors (AO4); `ao calibrate`,
   `ao correct`, `ao loop` (AO5).
4. **Holograms** (AO6): GS, weighted GS, MRAF, complex-to-phase-only encoding (double-phase, Bolduc-type),
   quantisation-aware refinement; **sensorless optimisers** (AO7): SPGD, hill climbing, modal search, phase locking, and
   transmission matrix by phase stepping.
5. **Temporal shaping** (TS1, TS2): `spectralShaper` (pixels → wavelength, resolution kernel, fill factor, gaps, wrapping),
   `aopdf` (spectral phase and amplitude, finite window, efficiency, ripple), closed-loop shaping with FROG or d-scan as the
   metric. (TS3, the full 4f line with spatial chirp, is later.)
6. `physics-reviewer` on the diff; **advisor call 2 (Opus 5.5)** before done; docs, changelog lines in the notes file.

**Tests (reference values).** DM influence at a neighbour equals the coupling; surface-to-wavefront factor 2 cos(aoi); Zernike
fit residual falls with the actuator count; Strehl of the corrected beam against Maréchal exp(−σ²); Shack-Hartmann spot shift =
f ∂W/∂x and noiseless Zernike recovery exact; SLM blazed-grating efficiency vs N levels; pixel replica orders at λ/pitch; a pulse
through a wrapped phase ramp; GS error monotone (Fienup 1982) and a known phase recovered; WGS spot uniformity; tile array N²
peak, exp(−σ²), delay-mismatch cost for a 30 fs pulse; focus enhancement (π/4)(N − 1) + 1 (Vellekoop and Mosk, Opt. Lett. 32,
2309 (2007)); programmed spectral phase delays the pulse; pixel-gap pre/post pulses at 1/(pixel bandwidth) (Weiner); the full
1920 × 1080 SLM example within the S1-scale budget.

**Acceptance.** Every reference test passes; the plugin loads and lists its elements; docs page with a parameter table per
element (keys, units, defaults); no vendor numbers anywhere (presets are labelled generic).

**Handoff.** `docs/design/notes/s3-ao.md`: the shipped API, the numbers, what the AO examples (S5) may call.

---

### S4 Fitting and twin (Sonnet 5.5, advisor Opus 5.5)

**Goal.** (a) Conditioned transforms in the **fitting** plugin (F1-F5): one set of shared parameters fitted to many operating
conditions, parametric and smooth-basis dependence on the condition vector, physics-constrained network transforms.
(b) A **`twin`** plugin on top (T1-T7): project layout, validation with held-out conditions, placement planner, experiment
design, what-if explorer, drift tracking, the MCP server and the goal file.

**Branch.** `dev/2026-11-s4-twin`. Runs **in parallel with S2/S3**; it edits only `plugins/fitting`, `plugins/twin` and their docs.

**Read first.** `CLAUDE.md`, `plugins/CLAUDE.md`, `docs/plugins/fitting.md`, `plugins/fitting/src/Fitting.cs` and
`Estimators.cs` (line ranges), `docs/examples/DigitalTwin/transforms/README.md` (the old example 26), the skills
`mirror-lab-setup` and `optimise-setup`, the plan page (C1-C6), `docs/agents.md`.

**Files you own.** `plugins/fitting/**`, `plugins/twin/**` (new), `docs/plugins/fitting.md`, `docs/plugins/twin.md`,
`python/src/ultrafast_twin` or the plugin's Python module, `tests/UltrafastEngine.Tests/Fitting*Tests.cs`. **Not** `src/**`:
if the core needs a hook, write it in the notes file and let S2/S5 add it.

**Order of work.**
1. **F1 multi-condition fit** (a `conditions` list; shared and per-condition parameters; held-out conditions; per-condition
   residuals). **Advisor call 1 (Opus 5.5)** on the design after reading the code and the S6 spike result.
2. **F2 parametric conditioning** (constant, linear, power law, polynomial; physical priors), **F3 smooth bases in (ω, θ)**
   (tensor-product splines or Chebyshev; penalties in both directions; the existing passivity, causality and
   Kramers-Kronig constraints).
3. **F4 network transforms** (`kind: "network"`, a small MLP with constraints built into the output layer; trained by the
   existing estimators; described in the docs as *physics-constrained network transforms*, not as a PINN); write the constraints
   down in the notes file. **F5** is a short note on gradients through linear elements: measured, not promised.
4. **`twin` plugin:** T1 project layout and `ultrafast twin init / fit / validate`; T2 validation report with credible bands;
   T3 placement planner and experiment design; T4 what-if explorer; T5 drift tracking (ensemble Kalman).
5. **T6 MCP server** (stdio, optional extra `ultrafast[mcp]`): about eight coarse tools (`describe`, `validate`, `run`,
   `compare_to_measured`, `scan`, `fit`, `explain_warning`, `plot`), workspace-scoped, **user expressions and callbacks refused
   by default**, a time and memory budget per call, read-only with respect to any lab hardware. **Advisor call 2** on the security
   design before it is written. **T7** goal file and checker, and a small benchmark of design tasks with known answers.
6. Docs, changelog lines in the notes file, **advisor call 3** before done (the MCP surface is a security
   boundary).

**Tests (reference values).** Two synthetic conditions recover known shared parameters (1e-3); a held-out condition is
predicted within the noise; a nonlinear phase linear in energy recovers its slope from spectra at five energies; a known smooth
surface is recovered and the penalties stop noise fitting; the network recovers a hidden smooth map with its constraints
holding exactly; MCP: tool schema tests with a scripted client, and refusal tests (an expression, a Python callback, a path
outside the workspace, a call over its time budget).

**Acceptance.** `ultrafast twin init` on the synthetic lab runs end to end; the validation report flags a twin that only fits
its training data; the MCP server passes its refusal tests; plugin docs complete.

**Handoff.** `docs/design/notes/s4-twin.md`.

---

### S5 Examples, docs, verification (Sonnet 5.5, advisor Opus 5.5)

**Goal.** Fill `AdaptiveOptics/` and `DigitalTwin/` with examples that impress (hero figures and animations), finish the
documentation and verification, and merge to `main`.

**Branch.** `dev/2026-11-s5-examples`.

**Read first.** the notes files of S1-S4, section 4.5 of this page, `build/example_check.py`, `docs/examples/README.md`,
the plan page (B5, C4).

**Files you own.** `docs/examples/AdaptiveOptics/**`, `docs/examples/DigitalTwin/**`, `docs/examples/Wavefronts/**` (additions),
the docs reference pages for the new elements, and (as the merging session) the shared files in section 3.

**Order of work.**
1. **AdaptiveOptics:** E1 components gallery; E2 DM correction of a Zernike aberration (with the Zernike plot and the
   correction animation); E3 sensorless correction (SPGD, modal search: spot sharpening animation); E4 aberrated high-power beam
   with a DM and a nonlinear chain (B-integral maps with and without correction); E5 SLM beam shaping (smiley face, square,
   ring, vortex donut; a **full 1920 × 1080 panel with 8-bit quantisation**; GS iteration animation); E6 complex shaping with a
   phase-only device; E7 coherent tiled-aperture combining (7 and 61 tiles, SPGD locking animation, delay mismatch for a 30 fs
   pulse); E8 temporal shaping (flat-top, double pulse, pulse train, pixel-gap side pulses, a gain-narrowed amplifier restored by
   an AOPDF, closed-loop shaping animation); E9 transmission matrix through a scatterer.
2. **DigitalTwin:** E10 fitting across conditions (spectra at five energies, one shared transform, held-out energy); E11 build the
   digital twin (synthetic lab with hidden aberrations; place transforms, fit five conditions, recover the hidden values,
   predict a sixth with a band); E12 explore the twin without beam time; E13 network versus spline transform; E14 agent-driven
   design through the MCP server; E15 the wavefront twin.
3. `make_figures.py` and `make_all.py` entries, gallery, README hero animation (`build/gallery.py`).
4. **Merge wave** (as the merging session): `CHANGELOG.md`, `docs/testing.md` rows, `PROJECT_STATUS.md` counts,
   `docs/capabilities.md`, `ROADMAP.md`, `docs/roadmap.md`, `llms.txt`, `mkdocs.yml`; `/regen`; an "adaptive optics and digital
   twin" section in `docs/agents.md`; the README badges (test counts).
5. `/verify` (builds, all tests, examples in strict mode, docs), CI green on Linux and Windows. **The one advisor call (Opus 5.5)** before the
   merge. Merge `dev/2026-11` to `main` only then. A tag or release **only when the owner asks**.

**Acceptance.** Every example runs under `ULTRAFAST_STRICT_OPTIONS=1`, asserts its key number, produces its figures and
animations within budget with `make_all.py`; `example_check.py` 0 problems; docs 0 warnings; CI green.

**Handoff.** `docs/design/notes/s5-release.md`: counts, what was verified, what was not (GPU, OpticStudio, macOS by hand).

**Aditional Note from the user**: Currently it seems that many of our examples are old, and are fairly unintuitive and a quick glance does not help. I think we need to really go through each and every example and tell a little story with some animations / nice plots - that is to say: for example with Wavefronts/ we want to see a 2d image of out inputs/output wavefronts! Or with LaserSystem/ show a range of results (ie wavefronts, contrast, FROG trace etc.). We also should add examples/Fibres to show our fibre capabilities.

## 6. Repetition and Pockels cells (a comment for the plan; later session S6)

*What the engine does today (checked in the docs).* Most of the engine is **single shot**: one pulse through a system. The gain
and amplifier elements are the exception: with a `repRateHz` (on the pulse, or on the element, which then acts as an **ideal
pulse picker**) they iterate the pump-amplify cycle to a **steady state** with a fast energy model, then propagate the last pulse
with the full model; period doubling is detected and warned about (`docs/reference/gain.md`, "Steady state at the repetition
rate"). A regenerative amplifier is a `geometry: "regenerative"` element with a number of round trips. The `grid.tail` second
grid carries delayed light such as ghosts and pre-pulses. Jones-calculus polarisation elements (waveplates, polarisers,
Faraday rotators) exist.

*What I did not find (not checked beyond the docs; the S2 spike S7 reads the code to confirm).* A **switch model**: nothing in
the docs describes a Pockels cell, a pulse picker with a finite contrast, a switching window or its rise time, or the leakage
of a regenerative amplifier's cavity switch into neighbouring pulses. Time between pulses (ns to µs) is handled only as the
repetition period of the gain model, not as a resolved axis.

*Why it matters.* A Pockels cell is the device that decides which pulse goes where: pulse pickers (rep-rate division),
regenerative-amplifier switch-in and switch-out, cavity dumping, and contrast cleaning (a plasma-mirror-free alternative in some
chains). Its imperfections are exactly the contrast problems the engine already cares about: **finite extinction ratio** (the
unselected pulses leak at, say, 1e-3 to 1e-2 of the selected one), **finite rise and fall time** (the selected pulse sees a
partial retardance if the switch edge is slow; neighbouring pre- and post-pulses see a rising edge), **voltage-dependent
retardance** (an electro-optic crystal acts as a waveplate with retardance π V / Vπ, plus thermal and piezoelectric ringing),
and in the regen the **round-trip number** setting the energy and the pre-pulse level. Repetition also matters beyond Pockels
cells: thermal lensing and its steady state depend on the average power; a DM or SLM responds at kHz or tens of Hz while
the laser runs at kHz to MHz, so an AO loop acts on averages, not on single shots; and tiled-array phase noise is a rate
question.

*Design sketch (S6, not part of S1-S5).*
1. **`pockelsCell` element** (a voltage-controlled waveplate pair with a polariser, as a model rather than a vendor catalogue):
   crystal (KD*P, BBO, RTP, LN) with Vπ, aperture, length, extinction ratio, an applied **voltage waveform** V(t) (rise time,
   flat top, fall, a delay relative to the pulse), and the polarisation transform built from the existing Jones elements. On a
   single pulse it applies the retardance at the pulse time, which the engine can already do with a `waveplate` of that
   retardance; the new part is the time dependence across pulses.
2. **A pulse-train layer**: N pulses at the repetition period t_k, each passing the cell with transmission T(V(t_k)); the
   per-pulse full propagation stays as it is, and a macro-time table records the energy, pre- and post-pulse leakage per pulse.
   This is the same idea as the gain steady-state iteration (a fast model across pulses, the full model for the one that
   matters), extended with a switch.
3. **A regenerative-amplifier switch**: switch-in and switch-out timing in terms of round trips with the cell's edge times,
   so that the pre-pulse and the cavity-dump contrast come out of the model instead of being set by hand.
4. **A pulse-picker contrast example** (leakage into the contrast instrument) and a regen example with switching.

*Open questions for the owner (S6, not blocking S1-S5).* Which switches matter first (pulse picker, regen, cavity dump)?
Is the contrast of the switch (extinction, edge time) a quantity you measure and want to fit with the twin?

## 7. Risks and how the order handles them

| risk | handled by |
|---|---|
| a full-size SLM does not fit a laptop | spikes S1-scale and S2-sparse before any design; streaming and sparse sampling in S2; examples carry a time and memory budget |
| the chromatic SLM model is wrong | spike S3 first; the blazed-grating-as-a-grating reference test before the SLM element exists; S2 runs on Opus 5.5 and records its reasoning for the owner; the S3 advisor re-reads it |
| the Python restructure breaks users | shims with `DeprecationWarning`, an import-every-old-name test, the autocompletion test |
| moving the examples breaks docs and CI | one `git mv` pass, an old → new table, `mkdocs --strict` and `example_check.py` as proof |
| multi-condition fitting is bigger than expected | spike S6-multi-condition reads the code first; F2 (parametric) ships before F4 (network) |
| networks cannot be trained at scale (no gradients) | F4 limited to small nets with the existing estimators; F5 is a note, not a promise |
| animations bloat the repository | budget check (≤ 2.5 MB each); decision below |
| MCP executes untrusted code | refused by default; workspace-scoped; read-only with respect to the lab; advisor review of the design |
| parallel sessions collide | file ownership (section 3); the merging session edits the shared files |
| token cost | model split in section 2, at most three advisor calls per session, `test-runner` on haiku, briefs that list what to read |

## 8. Open points for the owner

1. **Repository size for animations.** About 15 GIFs of up to 2.5 MB is about 40 MB. Alternatives: attach them to a release, or generate them in the docs workflow. Recommendation: commit the hero animations (a handful), generate the rest at docs build. -- GENERATE ALL IMAGES BEFORE BUILD AND COMMIT
1. **Gate G0** (inside S2) needs your review of the spike numbers.
2. **Python naming.** Lowercase modules (`ultrafast.system`) are recommended over the capitalised form; say so if you want the other. -- THAT IS FINE
3. **Version.** The merge is a feature release (2.3.0 by the project's convention); tags and releases only on your word.
