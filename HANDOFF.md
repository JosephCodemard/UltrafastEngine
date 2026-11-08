# Handoff: the November 2026 consolidation (released as 2.2.0)

Written 2026-11-08 at the end of a long session. It says what was done, what was fixed, what is still open and what to
do next. Read it together with [PROJECT_STATUS.md](PROJECT_STATUS.md) (what is verified),
[docs/roadmap.md](docs/roadmap.md) (what is planned) and [docs/capabilities.md](docs/capabilities.md) (what is and is not
modelled). The work is committed on `main` (last commit `30f6e69` plus the commit that adds this file). **Nothing has been
pushed to GitHub and no tag or release was made.**

## What the session set out to do

1. Consolidate the component tests into `tests/components/` and cover every element the engine supports.
2. Replicate twelve new papers (PDFs supplied by the owner) as a test of the engine and a showcase of it.
3. Bring the documentation up to date with the engine.
4. Then: fix every engine bug and implement every gap that 1 and 2 turned up, re-run the papers, verify, merge to `main`,
   add a GitHub Pages workflow, rewrite the README and release as a non-beta version.

## What changed

### Tests
* `verification/` became `tests/components/` (flat), `docs/verification.md` became `docs/component-tests.md`.
* Ten families, **1691 checks** (1688 pass, 3 marked KNOWN): linear, splitters_masks, polarisation, crystals, instruments
  (existing) and optics_misc, media, crystals_chi2, spatial, instruments_more (new). `python3 tests/components/run_all.py`
  (`--no-plots` for CI). The C# component sheet in `tests/UltrafastEngine.Tests/Components/` stays where it is.
* New C# test files: `GridSolverTests`, `InstrumentFixesTests`, `FrogKindsTests`, `MultiTraceRetrievalTests`,
  `MaterialsCrystalsTests`, `CellModeTests`, `SingleShotCorrelatorTests`; extra tests in `NerTests` and `BeamSourceTests`.
  Final counts: 468 C# (`--slow`), 127 Python.

### Papers (`docs/papers/`, index in `docs/papers/README.md`)
Old papers were removed (superseded). Twelve were replicated. Each folder has `paper.json`, `run.py`, `checks.json`,
`figures/` and a `README.md` that ends with **How well the science matched** (per quantity: verdict and most likely reason).
`python3 build/paper_check.py` runs them all (about 8 min, 0 hard failures).

| folder | headline |
|---|---|
| kane-1993-frog | SD / PG / SHG traces; chirp-rate slopes 4/3 and 2/3 reproduced; engine SD gate and retrieval |
| kida-2014-double-frog | double FROG removes the time ambiguity (16 of 16 starts); two-trace case does not (open) |
| michelson-1887-ether-null | the engine's Michelson readout; the null is built in, so the check is circular by construction |
| perot-1899-multiple-beam-interference | Airy coefficient, coincidence periods. **The PDF supplied as "Michelson 1892" is Perot & Fabry 1899** |
| treacy-1969-grating-pulse-compression | engine matches the paper's equations; four printed numbers disagree with the paper's own formulas |
| shimizu-1967-cs2-broadening | qualitative (the paper gives no n2, intensity or τ) |
| liu-2013-ner-cs2-pulse-cleaner | Eq. (1) transmissivity and contrast gain reproduced; cleaned width 158 fs vs about 60 fs |
| schulte-2016-mpc-fused-silica | bandwidth 13.6 / 14.2 / 17.2 nm vs 13.4 / 13.5 / 16.1 nm (the PDF is Schulte, not "Weitenberg 2017") |
| kaur-2024-mpc-ner-xpw | NER ceilings reproduced; pressure and B of the optimum and XPW efficiency depend on an unstated cell |
| ma-2018-single-shot-cross-correlator | window and resolution equal eqs. 1-2; crystal physics and artefacts not simulated |
| maker-1962-dispersion-focusing-harmonics | Maker fringes, KDP d_eff vs φ (the PDF is Maker et al. 1962, not Bjorkholm) |
| dubietis-1992-chirped-pulse-parametric-amplification | 67 fs limit, 24x compression; fibre width, grating distance and final 70 fs do not |

The PDFs are in `docs/papers/*.pdf` and are **git-ignored**: licensed papers and their figures never go into the repository.
Several were scans with garbled OCR (Michelson-Morley, Perot-Fabry, Treacy, Shimizu): numbers that could not be read are
marked UNVERIFIED in each `paper.json` and are soft checks.

### Engine bugs fixed
| | bug | fix |
|---|---|---|
| B1 | erratic SPM phase (grid dependent) at 10 fs; fibre γ converted to n2 at the requested ω but solved at the snapped ω0 | the pulse and Kerr terms use the requested carrier (`grid.reportOmega0`) |
| B2 | grating / prism angular dispersion off by (λ_req/λ_snap)² − 1 | reports use the requested carrier |
| B3 | `CircularAperture` transmission wrong for elliptical beams (0.8816 vs 0.7897) | exact integral |
| B4 | `binaryPhase` zone plate transmits less than 1 | not a bug: a sampling effect; documented with its formula |
| B5 | energy meter / camera `rangeM`, `bandM` edges snapped to whole bins | edge bins weighted by the fraction inside |
| B6 | interferometric autocorrelator peak:background 7.84, not 8 | delay axis includes d = 0, analytic background |
| B7 | `crossCorrelator` with `resolutionS`: trace, floor and `contrastAt_*` on different normalisations | all relative to the resolution-limited peak |
| B8 | KDP type-I d_eff with an explicit `cut` erratic in φ | eigenvector chosen from rounding noise; now the larger branch |
| B9 | strongly phase-mismatched thick crystal 150-960x off the sinc² law, no warning | at most 0.5 rad of mismatch phase per step |
| B10 | grid planner ignored the memory of a delayed response | included in the planned window; short fixed windows reported |
| B11 | `anisotropicFraction` ignored by `expression` / `callback` responses, values above 1 rejected | honoured for every kind, range 0..1.5 |
| B12 | `retrieve` silently assumed SHG-FROG | kind from the data, `kind`, or the instrument's `frogType`; otherwise an error |
| B13 | element-level `selfSteepening: false` ignored on fibres and bulk | honoured |
| B14 | coarse D(λ) tables 0.8 % off in β2 | cubic spline |
| B15 | generated "More:" labels named a nonexistent page | labels show the real target |

### Engine features added
Self-diffraction FROG (`frog` `type: "sd"`), `xfrog` instrument (alias `sfgFrog`) with a known `reference`, retrieval kinds
`sd-frog` / `xfrog`, multi-trace retrieval `multiFrog` and Python `uf.retrieve_multi`; `crossCorrelator` `gateArm`;
built-in `CS2` and `BaF2` materials, the `orientational` delayed response, cubic Kerr anisotropy (`kerrAnisotropy`,
`kerrCutAxis`, `kerrAzimuthDeg`) with cross-polarised-wave generation; `cellMode` (Kerr-matched mode of a multi-pass cell,
`ultrafast.cells.cell_mode`); `singleShotCorrelator` instrument; a warning and a loss booking when SPM reaches zero frequency.

### Behaviour changes users will notice (also in the changelog)
* The input pulse is built at the **requested carrier**, not the lattice-snapped one. Numbers moved by at most a few
  percent (carrier-snap level); example READMEs and tutorial text were updated.
* `retrieve` needs a kind for a bare polarisation-gate or self-diffraction trace.
* `anisotropicFraction` outside 0..1.5 is an error.

### Documentation and infrastructure
`docs/capabilities.md`, `concepts.md`, `roadmap.md` (+ `ROADMAP.md`), `testing.md`, `component-tests.md`, the index,
README, plugin pages and architecture notes were checked against the code and corrected (stale claims removed: beam
splitters, `resonator`, stress birefringence, non-collinear crystals). The README was rewritten with badges. New
`.github/workflows/docs.yml` builds the docs with `--strict` on pull requests and deploys to GitHub Pages on `main`.
A stray `cpa_results/` folder written to the repo root by `docs/tutorials/files/cpa.py` is fixed (it now writes next to the
script). Version is **2.2.0**, no longer beta.

## What is still open

### Engine
* **Pulses of about one cycle** (10 fs at 2 µm, B = 3): SPM that reaches zero frequency is cut. The engine warns and books the
  loss as `spectrum at non-positive frequency`; a carrier-resolved path is the real fix. Three `media.py` checks stay KNOWN.
* **`snapCarrier` in a nonlinear cell:** it still changes the Schulte cell result by 1.4 % in bandwidth and 0.4 % in B although the
  pulse is built at the requested carrier. Find the linear or nonlinear term that still uses the lattice carrier. Related: the window
  frame follows the lattice-centre group delay, so a dispersive pulse off the lattice drifts by GDD x offset x L.
* **`multiFrog` with two traces** (reference SHG plus XFROG) ends in the time-reversed twin in most starts; with a third trace all
  starts find the truth. Check the initial guess and step order (`docs/papers/kida-2014-double-frog/repro_2.py`).
* **Continuous-wave sources** still sit on the nearest frequency bin (relative error 2.8e-7); `snapCarrier: false` does not change it.
* **Cubic Kerr:** 1-D envelope only (spatial and carrier-resolved models throw; GPU not supported).
* **`singleShotCorrelator`:** no beam propagation in the crystal (efficiency, depletion, envelope, walk-off), no air scatter, stripe
  filter, crystal-face reflection spikes or fibre array.
* **`cellMode`:** round beams, peak power only, a command and a Python function (not an element); a plate touching a mirror is
  treated as immersing it; it does not check that a matched mode survives a perturbation.
* **Delayed-response window planning** covers Raman and orientational responses, not generic `timeResponse` or gas rotational revivals.
* Round-aperture decentre is ignored; the phase-matching calculator rejects `noncollinearDeg` for `sfg` although the element accepts it.
* A non-collinear OPA at a 2° crossing loses about half its gain at 64 / 150 µm radii: plausible, but there is no test against an
  analytic Gaussian-overlap average.
* Also see the roadmap items that were already open: frequency-dependent effective area, fibre tapers, hollow capillary (Marcatili),
  ensemble Kalman / Bayesian estimators, release workflow, pip extras, GPU coverage, Zemax depth.

### Unverified numbers shipped in the engine
CS2 total n2 (3.0e-18 m²/W), the 90 / 10 electronic-orientational split and the 1.6 / 0.2 ps time constants; BaF2 n2 (2.0e-20 m²/W)
and σ = −1.2 (citation from memory). All are flagged in the docs. The literature spread is a factor 2-3. Replace them with checked
values before quantitative work.

### Process and environment
* **Not run:** the net48 tests under Mono (Mono is not installed on the owner's Mac); the new `docs.yml` workflow itself; GPU tests.
* **Coverage:** no coverage number exists. `dotnet-coverage` does not attach on macOS arm64 ("Profiler was not initialized"). Run it
  on a Linux CI job (and send it to Codecov or a similar service) if a coverage badge is wanted. The test-count badges in the README
  are static and must be edited when the counts change.
* **GitHub Pages** needs one setting: Settings → Pages → Source: "GitHub Actions". Then push `main`.
* Local branches `dev/2026-11-consolidate` (merged), `dev/2026-11` and `branching` still exist and can be deleted when you are ready.
  The four agent worktrees were merged and removed.

## Suggested next steps (in order)
1. Push `main`, enable Pages, watch CI go green (Mono net48, macOS, Windows, the docs workflow), fix whatever differs from the local run.
2. Verify the unverified constants (CS2, BaF2) against the original papers; then re-run `liu-2013`, `shimizu-1967` and `kaur-2024`.
3. Close the `snapCarrier` / frame-drift item (it touches the multi-pass-cell papers, the most important use case).
4. Decide the `multiFrog` two-trace question (algorithm detail or a real defect).
5. Carrier-resolved handling of one-cycle pulses.
6. Coverage in CI, then the release workflow (`v*` tag builds packages and wheels; ZOS-API assemblies are never packaged).
7. Design plugins (coating design, crystal design) as in the roadmap.

## Papers worth replicating next
Chosen for physics the current set does not cover (PDFs to be supplied by the owner):
* **Polarisation / Kerr:** Maker, Terhune & Savage, PRL 12, 507 (1964) (intensity-dependent index and ellipse rotation: tests the
  Kerr tensor and CS2 directly).
* **Nonlinear fibre and gas:** Dudley, Genty & Coen, Rev. Mod. Phys. 78, 1135 (2006) (supercontinuum); Nisoli et al., Opt. Lett. 22, 522
  (1997) (hollow-fibre compression below 5 fs; needs the Marcatili model).
* **Diffraction and beams:** Durnin, PRL 58, 1499 (1987) (Bessel beams / axicon); a classic Fresnel edge or circular-aperture paper.
* **OPCPA:** Ross et al., Opt. Commun. 144, 125 (1997) (design of an OPCPA chain); a modern broadband non-collinear OPA paper.
* **Diagnostics:** Iaconis & Walmsley, Opt. Lett. 23, 792 (1998) (SPIDER); Miranda et al., Opt. Express 20, 18732 (2012) (d-scan);
  Michelson's own 1892 paper (the PDF supplied under that name was Perot & Fabry).
* **Lasers:** a Ti:sapphire Kerr-lens mode-locked oscillator and a Yb regenerative-amplifier paper (to exercise `oscillator` and
  regenerative `amplifier`, which have no replication yet).
DOIs for the first four were given from memory: check them before downloading.

## Working notes for the next person (or agent)
* **Python environment:** use the repo `.venv/bin/python`. Export `ULTRAFAST_LIBRARY` and `PYTHONPATH` in the **same** shell command
  as the Python call, or a stale `bin/native` library is used (this made two agents believe `externalAngleDeg` and `noncollinearDeg`
  were unsupported). Rebuild with `python3 python/tools/build_engine.py` after any C# change and never while another job uses the
  library. In zsh, a command stored in a variable is not word-split: store only the path.
* **PDFs:** `pdftoppm` is not installed, so the Read tool cannot render PDFs. Text layers were read with `pypdf` or macOS PDFKit
  (PyMuPDF worked too); page images were rendered for equations and figures. Keep extracted files out of the repository.
* **Parallel agents:** implementers worked in separate git worktrees (`isolation: worktree`), each editing a distinct part of the
  code, and the lead merged the branches and regenerated the five generated files once. This avoided clobbered builds. Agents were
  stopped twice by the usage limit; their work was saved in the worktrees and resumed with `SendMessage`.
* **Honesty rules that worked:** a paper disagreement is a finding, never tuned away; a KNOWN mark in a component check must have a
  repro; unverifiable numbers are UNVERIFIED and soft; a test tolerance is never loosened to hide a failure (the one 2 % to 3 % change
  in `python/tests/test_callbacks.py` is explained in its comment: the value was 1.99 % off before the changes too).
* **Generated files** (`schemas/*.json`, `python/src/ultrafast/types.py`, `docs/reference/options.md`, `python-api.md`,
  `validation-matrix.md`) are regenerated with `/regen` and checked by tests; do not edit them by hand.
* `docs/design/plan-fixes-2026-11.md` records the fix plan with each stream's notes and changelog lines; it is complete.
