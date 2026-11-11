# Plan: the next iteration, toward the first release

Written 2026-11-10 after S5. A **smaller** iteration than the AO / twin work: closing physics and data gaps, investigating the open findings, a release system with
selectable components and an overhaul of the documentation. Sizes are focused agent-days (S ≈ 1, M ≈ 2-3, L ≈ 4-6). What the previous iteration built and left open:
[ao-twin-2026-11.md](ao-twin-2026-11.md). Longer-term design that is not scheduled: [backlog.md](backlog.md). The rules of `CLAUDE.md` apply (a physics change needs a published
or analytic reference in a test; a disagreement is a finding, never tuned away; tags and releases only when the owner asks).

## 1. Answers to the open questions

* **Repository size:** not a concern; media stays in the repository and is the optional `media` component of a release (see WP8). Work stays on `dev/2026-11` for now; no merge to `main` until the owner asks.
* **`UF_WITH_AO`: what it is.** The Python package and the CLI run the *hosted* engine, which finds plugins as DLLs at run time, so `ao` and `twin` work there. The C API library
  (`UltrafastEngine.Native`, NativeAOT, used by C / C++ and by the optional native Python wheel) cannot load DLLs at run time, so each first-party plugin is **compiled into it**
  behind a build constant: `UF_WITH_GPU` and `UF_WITH_HITRAN` in `src/UltrafastEngine.Native/UltrafastEngine.Native.csproj` and a `#if` in `Exports.cs`. `ao` was never added, so a C++ program
  linking the native library gets "unknown element type deformableMirror". `UF_WITH_AO` is the same two edits for `ao` (the `twin` plugin is a Python module plus a CLI command and needs nothing). It is a
  15-line change, included as WP0; the packager gets `UF_WITH_AO=false` for a release without the plugin, as for the others.
* **Grid-planner hook for shaper side pulses: yes** (WP0). **Unknown-element message naming `ao`: yes** (WP0).

## 2. Work packages

Order: WP0 first (small, unblocks), then WP1-WP4 (physics, each on its own branch; files overlap little), WP5 and WP6 (investigations, run in parallel with them), WP7 the findings, WP8 and WP9 last,
because they describe what the others deliver. Every package ends with tests, docs, the changelog and `/verify`.

### WP0. Housekeeping that was promised (S)

1. `UF_WITH_AO` in the Native project and `Exports.cs`; the packager's plugin switch; a test that a native-library build lists the four `ao` elements (skipped where NativeAOT is not built).
2. **Planner hook.** `GridPlanner` reads from the `spectralShaper` and `aopdf` terms the time extent they create: side pulses at `2π / pixel bandwidth` and the stretch from a programmed GDD
   (`GDD x bandwidth`); it widens the window, or when the window is fixed it **warns** that output will wrap. Test: the Weiner example of `temporal-shaping` runs without setting `grid.windowS`;
   a shaper with a 1 ns programmed GDD on a 10 ps fixed window warns.
3. The unknown-element message (`Compiler.cs:649`) names the plugin for `deformableMirror`, `slm`, `spectralShaper`, `aopdf` as it already does for the other first-party types.
4. *(Done 2026-11-11: see [notes/wp0.md](notes/wp0.md); Bolduc and double phase through the SLM and the lobe fractions are in `python/tests/test_ao_plugin.py`, the Littrow check in `AoDeviceTests`.)* Mark the open edges of S3 that have no test: grating-derived dispersion of `spectralShaper` against a Littrow reference; end-to-end Bolduc and double phase through an SLM with a varying field; tiled-array
   lobe fractions beyond `0 < f < 1`. Write the tests (S) or state in the docs why not.

### WP1. Spatial modelling: thermal lenses in beam tracking (M-L; priority 1)

*Done 2026-11-11 (session B): see [notes/session-b.md](notes/session-b.md).*

*Today* ([capabilities](../capabilities.md)): the tracked beam is Gaussian; a thermal lens is applied as the **thin parabola** that fits it best over the beam; a multipass amplifier puts it on the tracked
beam only when given the path between passes; a regenerative amplifier does not; the radial spatial model has the full distributed thermal lens (see `Wavefronts/spatial-gain-thermal-lens`).

*Plan.* (a) Treat the pumped region as a **distributed lens** in the ABCD tracker: a graded-index duct of length L with `n(r) = n0 - ½ n0 g² r²` with `g²` from the heat load (dn/dT, the thermal conductivity and the absorbed pump), i.e. the
exact ABCD matrix of a quadratic-index medium instead of a thin lens at one end (this changes the beam radius inside long rods and the focal position for thick media). (b) Apply it per pass in
multipass and per round trip in regenerative amplifiers (today missing). (c) Report the effective focal length and the thin-lens equivalent in the element's `values`. (d) Keep the aberrated part
(spherical, from the non-parabolic heat profile) on the spatial model; say so in the docs.

*Tests.* The duct matrix against the analytic `cos(gL)`, `sin(gL)/g` form and its thin-lens limit `gL → 0` (1e-12); the engine's tracked beam radius after the `spatial-gain-thermal-lens` rod (60 W: f_th 0.54 m,
471 µm after the rod, 319 µm after 0.4 m, from the radial model) reproduced by the tracker to a stated tolerance (measure the spread first); a regenerative amplifier with N round trips equals N thin
duct passes. *Open question:* is end-face bulging worth including (backlog)? Not in this package.

### WP2. Gain modelling: ASE and stress birefringence (L; priority 2)

*Today:* ASE is one-dimensional, in the signal polarisation, and covers only the simulated time window; stress birefringence (`thermalLens.stress`) is the beam-averaged depolarised fraction, not the
four-lobed pattern.

*Plan, in this order.* (a) **ASE over the inversion lifetime**: a steady-state forward and backward ASE model in the gain rate equations (spectrally resolved, both polarisations) that depletes the inversion
between pulses, feeds the contrast pedestal and is shared with fibre gain (backlog C3); reference: the closed-form two-level steady state (Saleh et al., IEEE PTL 2, 714 (1990)) and photon balance. (b) **Two
polarisations** for ASE. (c) **Stress birefringence pattern**: the radial / azimuthal index change of a uniformly heated YAG rod (Koechner, *Solid-State Laser Engineering*, thermal-effects chapter;
Cousins, IEEE JQE 28, 1992; read the formulas from the sources, do not write them from memory) as a map applied through the spatial model, so the depolarisation of a linearly polarised beam is the four-lobed
pattern; the beam-averaged value must equal today's `stress` result (consistency test). *Spike first (S):* does the spatial path carry a vector field (both polarisations) today? If not, the pattern is applied as a
Jones map on a scalar pair and the limits are stated. *Not in this package:* 3-D (transverse-resolved) ASE; say so in the docs.

### WP3. Material models and a catalogue (L; priority 3)

*Goal:* every material parameter lives in **catalogue files with provenance**, none hard-coded in C#.

* **Catalogue.** A `materials/` data folder (one JSON file per material, gas or Raman model) with, per value: the number, its unit in the key name, `source` (paper, page or table), `status`
  (`verified` | `approximate` | `unverified`), an uncertainty (the literature spread, e.g. a factor 2-3 for CS2 n2) and the temperature / pressure / wavelength it applies to. A schema, a loader that
  replaces the tables in `MaterialLibrary.cs`, `Gases.cs`, `RamanModels.cs`, and an `ultrafast reference` page **generated** from the catalogue (`reference/materials-catalogue.md`, like `options.md`).
* **Runs say what they used.** The run notes list every `unverified` / `approximate` value a run touched (and `summary.json` carries it), so a quantitative result is never silently built on one.
* **Verify the known list** against the original papers (the owner supplies PDFs; licensed PDFs and their figures never enter the repository, only the extracted numbers with citations): CS2 n2 (3.0e-18 m²/W), the
  90 / 10 electronic-orientational split and the 1.6 / 0.2 ps constants; BaF2 n2 (2.0e-20 m²/W) and σ = -1.2; **rotational Raman dephasing times** (`T2At1Bar` of N2, O2, CO2, H2, air); the **TGG refractive index**
  (Sellmeier source); other n2 values flagged in `docs/reference/materials.md` and `nonlinear.md`. Re-run `liu-2013`, `shimizu-1967` and `kaur-2024` after the numbers change (HANDOFF).
* **Tests.** Every built-in material round-trips through the catalogue (the old table and the new files agree to 1e-15 before any value is edited, so the move is a pure refactor); a lint test fails when
  a catalogue entry has no source or status; user catalogues in `~/.ultrafast/materials` override with a notice.

### WP4. Plasma in envelope mode (M; priority 4)

*Done 2026-11-11 (session B): see [notes/session-b.md](notes/session-b.md); open: PPT / ADK against carrier-resolved, avalanche vs inverse-bremsstrahlung double count.*

*Today:* the envelope model evaluates the Drude term at the **carrier frequency**; carrier-resolved mode is exact. *Plan:* apply the plasma's linear response as a spectral operator per step: the Drude
index `n_p(ω) = sqrt(1 - ω_p²/(ω(ω + iν)))` expanded to first order in the density, frequency-resolved (the 1/ω² dependence across the pulse bandwidth, including the blueshift of the leading and trailing parts), with the
density from the existing ionisation rate. *Tests:* envelope with the new term against the carrier-resolved model on a 10 fs, 2 µm pulse (the blueshift and the energy loss to ionisation agree to a stated
tolerance, measured first); the old behaviour recovered for a narrow spectrum (1e-9); energy booked (`plasma`, `ionisation`) closes. Document the new limit (no negative frequencies, already stated).

### WP5. Leftovers from the spikes (S; priority 5, mostly decisions)

*Done 2026-11-11 (session A): see [notes/session-a.md](notes/session-a.md).*

| item | recommendation |
|---|---|
| compile cache | **closed (owner, 2026-11-10).** A 128 x 128 trial costs 10 ms including the compile (spikes S4); revisit only if a loop with a much larger system shows the compile above 30 % of the trial. Write the trigger into `speed.md`. |
| full streaming executor | **closed (owner, 2026-11-10).** The peak is the .NET GC, not the propagator (2.7-3.4 GB for any block size at 1920 x 1080 x 16). Revisit if a 4096 x 4096 pulse run is wanted; measure it first (spike S1 only did 1920 x 1080). |
| tiles as a Branched system | **replaced by a test (owner agreed):** N sources through a `Branched` system recombined at one point (the on-axis value only) against the tile source's on-axis fluence. A Branched system cannot give an array's far field, which is why the spike was not run. |

### WP6. Small fixes found on the way (S each)

*`ny = 1` done 2026-11-11 (session A, [notes/session-a.md](notes/session-a.md)); the cavity figure stays in WP9.*

* **`ny = 1` grids** (HANDOFF / S1): a grid with `ny = 1` did not reproduce a measured input while `ny = 4` works. Not investigated. Reproduce in a minimal test (a `measured` profile through one gap), find the cause
  (the y pitch from `halfWidthYM` and `ny`, or the sampling of the measured profile), then either fix it or make the validator refuse `ny < 2` with a clear message. A bug-hunter task.
* **Cavity figure.** `Branched/cavity-and-coherence` has no figure (a cw / resonator run has no pulse to draw). Draw the Fox-Li iteration (the mode converging round trip by round trip as an animation and the radius
  against the ABCD eigenmode), and the Gaussian-Schell beam's coherent modes and width against `w(z)`. Done inside WP9.

### WP7. The open findings (S-M each; investigate, then write the cause in the README)

*Done 2026-11-11 (session A, [notes/session-a.md](notes/session-a.md)): SPGD gain, cw against pulse. Open: Raman shift rate, SLM against Fourier (with the new first-order efficiency finding in [notes/wp0.md](notes/wp0.md)), tiled-array SPGD reference.*

| finding | how to find the cause | done when |
|---|---|---|
| Raman shift rate 16-36 % above Gordon (`Fibres/raman-soliton-shift`) | (1) the same fibre in pyNLO (already a test dependency); (2) Gordon's rate with the model's **exact** Raman gain: `dΩ/dz ∝ ∫ Ω Im χ_R(Ω) sech²-spectrum dΩ` integrated numerically, not the first-moment `T_R`; (3) a fixed-width-soliton variant | the engine agrees with (1) and (2) to a few percent, or the difference is a stated engine finding |
| SLM engine against Fourier prediction 0.98-0.99 (`slm-beam-shaping`) | compare the engine with an exact angular-spectrum propagation of the same sampled field in NumPy on the same grid; switch off, one at a time, the 6 x 6 sub-pixel averaging (`fillFactor`, cell averaging), the absorbing edge and the lens model | the example's reference becomes the exact one, with a residual stated as a bound |
| tiled-array SPGD ends at 1.02 of the in-phase reference | compute each tile's complex on-axis field from single-tile runs on the 128 grid: is the zero-piston sum really the maximum? (tile areas on the grid differ, the on-axis pixel may not be the axis) | explained, or the reference is replaced by the true maximum |
| SPGD gain sensitivity (1.5 → 0.64, 3 → 0.95, 5 → 0.80) | the gain is in units of 1 / metric: normalise the metric by its starting value, add a documented default and a gain sweep to the docs; optionally gain sequences (Spall) | a default that reaches ≥ 0.9 across a stated range of metric scales |
| cw against pulse: 3e-6, not 1e-9 | build the cw line **at the requested carrier**: shift the lattice so a bin sits on it (as pulses have been since 2.2.0, `snapCarrier`); every cw result moves by about 1e-6, so re-baseline the cw reference tests at their stated tolerances and list them in the changelog | cw and pulse patterns agree to 1e-9 in the S2 test; no test tolerance loosened |

### WP8. Release system: the full repository, plus a selective installer and uninstaller (M)

*Decision (owner, 2026-11-10):* a release is the **full repository** (source archive, plus the built per-platform packages as assets), and a **custom installer / uninstaller** lets the user take only
part of it, e.g. `--include python tests examples.Branched`. *Today* `build.sh` / `build/package.py` build one folder per platform with a maintainer's choice of components
(`--cli --python --cpp --csharp --zemax --plugins --examples --docs`); there is no release workflow yet.

* **Is there a better way using GitHub?** GitHub releases cannot serve part of an asset, and a source archive is all-or-nothing, so selection needs either small assets or a partial clone. The plan uses both,
  because they suit different users: (1) **built components** as separate release assets (below), downloaded individually; (2) **source selection** with git's own partial clone,
  `git clone --filter=blob:none --sparse <repo> --branch <tag>` then `git sparse-checkout set <paths>`, which the installer calls when `git` exists (it fetches only the folders asked for, and the tag pins the
  version). Not available: per-file download from a release; GitHub's "download ZIP" of a folder.
* **Components** (the names `--include` and `--exclude` accept; dotted names select a part): `cli` (per platform), `python` (wheel; `python-native` per platform), `cpp`, `csharp`, `plugins` or `plugins.<id>`,
  `schemas`, `docs`, `examples` or `examples.<Group>` or `examples.<Group>.<name>` (so `examples.Branched` is that group; shared helper files such as `_figures.py` and `Computing/_optics.py` travel with it),
  `media` (hero figures and animations, off by default), `tests` (sources and data, no binaries) and `component-tests`, `papers`, `source` (everything). Each is a release asset with its own archive,
  size and SHA-256 listed in a `manifest.json`; dependencies are in the manifest (`examples` needs `python`; `python` needs the engine library for the platform; `plugins.twin` needs `plugins.fitting`).
* **`install.py`** (in the repository and attached to every release; standard library only):
  `python install.py --include python tests examples.Branched [--exclude media] [--version X | --latest] [--platform auto] [--dest DIR] [--source release|git]`,
  `--list` (components, sizes, what is installed), `--dry-run`, and `python install.py --uninstall [--include ...]` to remove part or all. It downloads only the chosen assets, verifies the hashes, installs wheels
  with `pip install --find-links` (no PyPI, owner rule), and writes `<dest>/.ultrafast-install.json` (what was installed from which version, with every file it wrote), so uninstall removes exactly those files and
  nothing else, and a second `--include` adds to an existing install (also an upgrade path). Never installs or removes the ZOS-API.
* **Packager:** `build/package.py` takes the same component names (`--components`), writes the per-component assets and the manifest; the existing flags stay as aliases.
* **`.github/workflows/release.yml`** on a `v*` tag: builds every component on its OS, smoke-tests it (CLI example 01 strict, the wheel in a fresh venv runs the quickstart, the C example, `ultrafast devices`, and
  `install.py` into a temporary folder with a selection, then `--uninstall` leaves it empty), fails if any archive contains `ZOSAPI*` (plus a test that plants one), and attaches the assets and the manifest.
  **No tag is created without the owner's word.**
* **Tests:** component resolution (dotted names, dependencies, excludes); install and uninstall against a local fake release folder (a `file://` manifest) with a changed file left alone on uninstall (warn, do not delete);
  hash mismatch refuses; the sparse-checkout path in a temporary repository.

### WP9. Documentation overhaul: from a story to a short scientific presentation (L, in waves)

*Decision:* the README of every example and the landing pages follow one structure instead of "the story":

1. **Question** (one sentence: what is being tested or shown, and why a scientist or engineer cares).
2. **Setup:** a system diagram (the engine's own schematic) and a **parameter table** (SI units in the keys, as in the files); the exact command.
3. **What is checked:** the reference (analytic, published or a second code) and the tolerance, stated before the result.
4. **Results:** a hero figure first, then a heat map or an animation (**always**; every example gets at least one), the table of numbers, the quoted output.
5. **Extend it:** three or four concrete changes (which keys to turn, what to expect, which other example to look at) and the Python or `.ufs` fragment.
6. **Limits and provenance:** what the model leaves out, open findings, unverified inputs (from the catalogue, WP3).

*Mechanics.* `docs/examples/_template.md` and a lint (`build/example_lint.py`: required sections, a hero, an animation or heat map, a quoted-output block, `# optional:` on figure scripts, no wall time in quoted output)
run in CI beside `example_check.py`. A one-file `example.json` per example (title, group, question, references, figures) feeds the gallery and the per-group index so those pages stop being hand-edited.
*Waves:* (1) the 12 examples written in S5 (rewrite in the new structure, fix the "story" tone); (2) `Wavefronts`, `LaserSystems` (several results per chain: wavefront, contrast, FROG trace, beam maps), `Nonlinear`;
(3) `Diffraction`, `Branched` (with the cavity figure of WP6), `Diagnostics`, `Components`, `GettingStarted`, `DigitalTwin/transforms`; (4) the landing pages (`docs/index`, `examples/README`, `concepts`), the README hero
(assembled from the gallery), and the plugin pages' "what is checked" sections. Reference pages stay reference pages. Missing AO / twin examples from the previous iteration (component gallery, complex-field shaping,
spline against network) are written directly in the new structure.

## 3. Suggested sessions

| session | packages | executor / notes |
|---|---|---|
| A | WP0, WP6 (`ny = 1`), WP5 decisions, WP7 (cw carrier; SPGD gain) | one agent, small tasks; `bug-hunter` for `ny = 1` |
| B | WP1 and WP4 | physics: Opus-class judgement for the ABCD duct and the Drude operator; `physics-reviewer` at the end |
| C | WP3 | mostly plumbing and reading papers; owner supplies the PDFs |
| D | WP2 | spike first, then ASE, then the pattern |
| E | WP7 (Raman, SLM, tiled array) | investigations; may run beside B-D |
| F | WP8 (packager, `install.py`, workflow) | packaging and a workflow; Sonnet-class |
| G-H | WP9 waves 1-4 | one example group per sitting; `example_lint` first |

## 4. Definition of done for the iteration

Each package: tests with a published or analytic reference; the full C# and Python suites; generated files current (`/regen`); `mkdocs build --strict` 0 warnings; `example_check.py` and `example_lint.py` 0 problems;
changelog, `docs/testing.md`, `PROJECT_STATUS.md` counts; CI green on Linux, Windows and net48 before a merge to `main`. The first release is cut **only when the owner asks**; the checklist is `docs/releasing.md` plus WP8.

## 5. Risks

| risk | handling |
|---|---|
| moving the cw line (WP7) shifts many reference results by about 1e-6 | list the affected tests, re-baseline each at its stated tolerance, never loosen a physics tolerance; separate commit |
| the catalogue refactor changes a number by accident (WP3) | the round-trip test (old table = new files to 1e-15) lands first, values change in later commits |
| a vector spatial path may not exist (WP2) | a one-day spike before any design; state the limits |
| verifying constants needs the owner's PDFs | list what is needed up front (WP3); unverified values stay flagged and are reported in run notes |
| the documentation overhaul is large (WP9) | waves; the lint and `example.json` make it mechanical; start with the examples that are most shown |
