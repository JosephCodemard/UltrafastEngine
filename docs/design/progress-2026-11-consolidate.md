# Progress: consolidation of tests, papers and docs (branch `dev/2026-11-consolidate`)

Started 2026-11-07. Owner rules: no push to GitHub (local commits only); PDFs never committed (`docs/papers/*.pdf` is gitignored);
agents must not edit C# or rebuild the engine while others run; engine bugs are reported with a minimal repro, not tuned away.

## Owner decisions
* Replicate all 12 PDFs, including Michelson-Morley 1887 and Michelson 1892.
* Old papers (Escoto, Gordon, Boyd-Kleinman, Bespalov-Talanov, lab-mpc-ner) are deleted (superseded). Kept: `docs/papers/README.md`
  (to rewrite as the index of the new set), `CLAUDE.md`, `paperlib.py`.
* Component Python scripts move to `tests/components/`; the C# sheet stays in `tests/UltrafastEngine.Tests/Components/`.
* Owner will download extra papers I recommended (Maker 1964, Dudley 2006, Nisoli 1997, Ross 1997, SPIDER, d-scan).

## Done
* `verification/` -> `tests/components/` (flat), `docs/verification.md` -> `docs/component-tests.md`; links, CI job (`components`),
  .gitignore updated; `run_all.py --no-plots` = 378/378.
* Old paper folders removed; stale code-comment references removed (FiberTests, SystemTests, Instruments.cs, Nonlinear.cs).
* Docs audit against code: capabilities, concepts, roadmap (+ROADMAP.md), index, README, agents, architecture, plugins, llms.txt.

## In progress (background agents; check the folders for output)
* Paper replications, each in `docs/papers/<author>-<year>-<topic>/` (paper.json, run.py, checks.json, figures/, README.md):
  FROG pair; Michelson 1887 + 1892 + Treacy 1969; Shimizu 1967 + CS2 NER cleaner; multi-pass cell pair.
* Component families in `tests/components/`: `optics_misc.py`, `media.py`, `crystals_chi2.py`, `spatial.py`, `instruments_more.py`
  (repros of engine bugs in `tests/components/repro/`).

## To do
1. Not started papers: Bjorkholm-type "dispersion and focusing in harmonic production" (BBO/SHG), "Powerful fs pulse generation by
   chirped parametric amplification in BBO" (OPCPA), "Single-shot cross-correlator for contrast".
2. Register new families in `FAMILIES` in `tests/components/run_all.py`; run `run_all.py --no-plots`; add them to
   `docs/component-tests.md`, `docs/testing.md`, `PROJECT_STATUS.md` (counts), CHANGELOG (Unreleased); add C# sheet entries where cheap.
3. Rewrite `docs/papers/README.md` (index, ours vs paper, what does not reproduce); run `.venv/bin/python build/paper_check.py`
   (currently exits 1 when no folders exist); `mkdocs build --strict` must be 0 warnings.
4. Collect agent reports; list every engine disagreement for the owner (they want these flagged, not fixed silently).
5. Known findings not yet fixed: generated options.md "More:" links point to nonexistent `reference/elements.md` (C# in
   `BuiltInElements.cs`, then `/regen`); `docs/CLAUDE.md` claims broken anchors fail the build (no `validation:` in mkdocs.yml).
6. Run `/verify` before the final commit.

## Environment
`export ULTRAFAST_LIBRARY=$PWD/python/src/ultrafast/bin/hosted/UltrafastEngine.Native.dll PYTHONPATH=python/src ULTRAFAST_STRICT_OPTIONS=1 MPLBACKEND=Agg`
and use `.venv/bin/python`. Rebuild the engine only with no agents running: `.venv/bin/python python/tools/build_engine.py`.

## Update: `media.py` and `crystals_chi2.py` done (agent report)
* `media.py`: 231 checks (228 ok, 3 KNOWN, 0 FAIL); `crystals_chi2.py`: 174 checks, all ok. Not yet in `FAMILIES` of `run_all.py`.
* **Engine findings for the owner**
  1. KNOWN bug: pure SPM in a dispersion-free fibre at 2 µm, 10 fs, auto grid changes |A(t)|² by 6 % of peak at B = 3 and loses
     energy (2e-5); at 1030 nm the change is 5e-4. The carrier-centred grid reaches ω ≤ 0 at 2 µm. Repro:
     `tests/components/repro/fiber_spm_2um_10fs.py`.
  2. Element-level `selfSteepening: false` on a fibre did not switch it off in one test; only `physics.selfSteepening` did (not chased).
  3. At 10 fs the SPM phase deviates 4-6 mrad from γPL at 515 / 1030 nm, erratic with grid size (auto-grid sampling suspected).
  4. A coarse D(λ) table (53 points) gives up to 0.8 % error in β2 near D = 0.
  5. `own.gddS2` of a D(λ) fibre at 2 µm is 0.4 % from β2·L at `reportOmega0`.
* Not tested: rotational revival (C# `GasTests`), bulk Raman gain, walk-off in a real run, depleted OPA, type-II SHG efficiency.

## Update: `optics_misc.py` done (agent report)
* 489 checks, 0 failures, 15 KNOWN (3 engine bugs), ~56 s. Not yet in `FAMILIES`. Note `_common.py` default `--out` may still say
  `verification/report` in one place: check it.
* **Engine findings for the owner**
  1. `trackedAngularDispersion…` of grating (7 checks) and prism (3) is off by exactly (λ_req/λ_snap)² − 1 (up to 1.2e-4 on the
     default grid, 4e-4 on a coarse one): angle/magnification use the requested carrier, the dθ/dω → dθ/dλ conversion the snapped
     grid ω0. GDD is unaffected. Repro `tests/components/repro/grating_snapped_carrier.py`.
  2. `CircularAperture.PowerTransmission` (`Physics/ModelImplementations.cs`) uses 1 − exp(−2a²/(wx·wy)), exact only for a round
     beam: 0.8816 vs 0.7897 by quadrature for w = 0.6 × 1.2 mm, a = 0.8 mm. Repro `round_aperture_astigmatic.py`.
  3. `binaryPhase` zone plate transmission 0.977 / 0.991 / 0.994 at 512 / 1024 / 2048 points, not 1 (cell averaging at π edges; may
     be a docs fix). Repro `binary_phase_zoneplate.py`.
* Smaller: round aperture ignores `decenterXM` on a 1-D beam (docs say rectangular only); Gaussian filter "FWHM in wavelength" is
  ~1 % wide at 200 nm (first-order λ→ω); thick-lens ABCD ignores `thicknessM`.
* Not tested: ghost wedge/overlap options, mask kinds other than the zone plate, TGG B-integral/dispersion, delay carrier phase.
