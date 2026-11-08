# Plan: fix the engine bugs and gaps found in the November 2026 consolidation

Findings, repros and numbers: `docs/design/progress-2026-11-consolidate.md` (removed when this plan is done).
Four implementers work in separate git worktrees on disjoint code; the lead merges, regenerates the generated files, re-runs the
component tests and paper replications, and writes each paper's "how well the science matched" section.

| stream | items | main code | tests / checks to flip |
|---|---|---|---|
| 1 grid and solver | B1 2 µm / 10 fs SPM, B2 angular-dispersion carrier snap, B3 astigmatic round aperture, B4 binaryPhase, B9 stepper on phase-mismatched crystals, B10 delayed-response memory in the grid planner, B13 fibre selfSteepening, B14 D(λ) interpolation | `Engine/Compiler*`, `GridPlanner`, `Optics/` gratings, prisms, apertures, masks, `Physics/Nonlinear`, `Crystals/` | `media.py`, `optics_misc.py` KNOWN marks |
| 2 instruments and retrieval | B5 energy-meter band edges, B6 IAC peak, B7 cross-correlator `resolutionS`, B12 retrieve kind, G5 SD-FROG / XFROG / multi-trace retrieval, G6 gate-arm element | `Instruments/`, `Retrieval/` | `instruments_more.py` |
| 3 materials and crystals | B8 KDP cut deff, B11 anisotropicFraction on expression responses and > 1, G1 cubic χ(3) tensor + BaF2 (XPW), G2 CS2 material | `Crystals/`, `Materials/`, `Physics/Nonlinear` | `crystals_chi2.py`, new tests |
| 4 beams and geometry | G3 Kerr-matched cell mode helper, G4 single-shot time-to-space cross-correlator | `Beams/`, `Instruments/`, `Systems/` | `spatial.py`, new tests |
| lead | B15 docs generator links, re-verify `externalAngleDeg`, merge, `/regen`, paper re-runs, docs | | |

Rules for implementers: a test with an analytic or published reference per fix (physics accuracy first), header comments on new
files, reference docs for new options, regenerate nothing (the lead does), no edits to CHANGELOG / testing.md / PROJECT_STATUS (report
the lines instead), no push, commit in the worktree branch.

Deferred (recorded in the roadmap): air scatter and stripe filter of a single-shot correlator, crystal-face reflection spikes,
a multi-order Fabry-Perot of finite beams.

## Status (2026-11-08)
Four implementers run in git worktrees under `.claude/worktrees/` (branches `worktree-agent-<id>`):
stream 1 `a29b2025f815cde80`, stream 2 `a3949310445871089`, stream 3 `a210869c846351b77`, stream 4 `a4524c7a8bd6287c1`.
They were stopped twice by the usage limit and resumed with SendMessage; their work is saved in their worktrees. To continue after
another interruption: `git worktree list`, `git log` in each, SendMessage "continue" to the agent id.
Lead checks: `externalAngleDeg` works (declared on `interaction`, tested in NoncollinearOpaTests; the earlier "unknown parameter" was a
stale `bin/native`). Open: the phase-matching calculator rejects `noncollinearDeg` for `sfg` although the schema allows it.
After the streams report: merge the four branches into `dev/2026-11-consolidate`, `/regen`, fix the generated "More:" links (B15),
re-run `tests/components/run_all.py`, `build/paper_check.py` (set ULTRAFAST_LIBRARY first), add "how well the science matched" to each
paper README, then delete `progress-2026-11-consolidate.md` and update capabilities, roadmap, PROJECT_STATUS, CHANGELOG, testing.md.

## Collected after the streams (changelog lines, paper follow-ups, testing.md rows)

### Stream 2 (merged): instruments and retrieval
* CHANGELOG Fixed: `energyMeter` and camera `bandM` weight edge bins by the fraction inside the band; interferometric autocorrelator delay
  axis symmetric with d = 0 and analytic background (peak:background 8 for all carriers; note when `points` under-samples fringes);
  `crossCorrelator` with `resolutionS` reports trace, floor and `contrastAt_*_db` relative to the resolution-limited peak; `retrieve` no
  longer silently assumes SHG-FROG (kind from `kind`, from the data, or the instrument's `frogType`; asymmetric trace without kind = error).
* CHANGELOG Added: `frog` `type: "sd"`; `xfrog` (alias `sfgFrog`) with a known `reference`; retrieval kinds `sd-frog`, `xfrog` for
  `pcgpa` and `epie`; multi-trace `multiFrog` (double FROG) and Python `uf.retrieve_multi`; `crossCorrelator` `gateArm`.
* testing.md rows: InstrumentFixesTests (band edges vs Gaussian CDF, IAC 8 for five pulses, cross-correlator resolution consistency, gate-arm
  ghost levels R^(2k)); FrogKindsTests (PG / SD centroid slopes, Kane & Trebino App. C; XFROG slope and sum frequency; retrieval with sign;
  kind inference); MultiTraceRetrievalTests (double FROG, six starts, Kida et al. 2014).
* Paper follow-ups: ma-2018: delete `repro_1.py`, make the two `res1ps_*` checks hard, use `gateArm` instead of the Branched workaround.
  kida-2014: use `xfrog` and `retrieve_multi` instead of numpy; drop the "no XFROG / multi-trace" caveat. kane-1993: SD gate + XFROG now exist.
* Not done: CLI `retrieve` stays single-trace; XFROG with a measured reference from a file; a probe-arm-only element option.

### Stream 3 (merged): materials and crystals
* CHANGELOG Fixed: KDP type-I d_eff with an explicit cut changed erratically with φ (ordinary-mode eigenvector came from rounding noise);
  `anisotropicFraction` honoured by `expression` and `callback` delayed responses, range 0..1.5 (1.5 = purely orientational), explicit key
  on `linAgrawal` replaces its own split. Behaviour change: values > 1.5 or < 0 are errors.
* CHANGELOG Added: built-in `CS2` (Kedenburg index; n2 3.0e-18 m²/W with 90 % delayed; `orientational` delayed-response kind) and `BaF2`
  (Li 1980 Sellmeier; n2 2.0e-20; σ = −1.2); cubic Kerr anisotropy (`kerrAnisotropy`, `kerrCutAxis`, `kerrAzimuthDeg`) with XPW.
* UNVERIFIED literature values: CS2 total n2, 90/10 split, 1.6 / 0.2 ps times; BaF2 n2 and σ (citation from memory). Flagged in docs.
* XPW law from the tensor: [001] cut ∝ sin²(4β) (zero at 0 and 45°, max at 22.5°); [011] cut ∝ sinβ cosβ (sin²β/2 − cos²β), zero at 54.7°.
  The Kaur paper's 64.5° angle convention is not tied to this yet.
* Not done: GPU cubic term; spatial and carrier-resolved cubic term (throws); net48 tests, Python tests, mkdocs and examples not run in the
  worktree.
* testing.md: MaterialsCrystalsTests (14 tests: KDP d_eff, CS2, BaF2, cubic χ(3)/XPW); NerTests +3.
* Paper follow-ups: liu-2013 and shimizu-1967 use built-in `CS2`; kaur-2024 can model XPW with `BaF2` + `kerrCutAxis`/`kerrAzimuthDeg`;
  maker-1962 re-check KDP d_eff vs φ.

### Stream 4 (merged): cell mode and single-shot correlator
* CHANGELOG Added: `cellMode` command and `ultrafast.cells.cell_mode` (Kerr-matched Gaussian eigenmode of a two-mirror multi-pass cell,
  Herriott or quasi-concentric, gas fill or plates; empty-cell Kogelnik-Li mode, re-entrant spacing, Gouy phase, B-integral; second-moment
  Kerr lens agrees with the radial model's rms radius to 0.3 %); instrument `singleShotCorrelator` (window and resolution from the crossing
  geometry, Ma et al. 2018 eqs. 1-2; trace on position and delay axes; area conservation).
* testing.md: CellModeTests (7), SingleShotCorrelatorTests (8, 1 slow ~150 s), python/tests/test_cells.py (2).
* Not simulated by `singleShotCorrelator`: beam propagation in the crystal (efficiency, depletion, envelope across the overlap, walk-off), air
  scatter, stripe filter, crystal-face reflections, fibre array. The spatial `sfg` crystal on a cartesian grid does reproduce the mapping.
* Not done: cellMode is a command / Python function only (no element option), round beams only, peak power only.
* Paper follow-ups: schulte-2016 and kaur-2024 should use `cell_mode` for the launch beam instead of trial; ma-2018 compare `singleShotCorrelator`.

### Stream 1 (merged): grid and solver
* **User-visible change:** the input pulse is built at the REQUESTED carrier (sub-bin offset on the lattice), not the carrier snapped onto the
  lattice; Kerr, self-steepening, angular-dispersion reports and carrier wavelength follow it (`grid.ReportOmega`). Window frame still moves
  with the lattice-centre group delay: a dispersive pulse drifts by GDD × offset × L relative to the window (73 fs for a 1 ps soliton at
  515 nm over 3 L_D); pulse intact. Moving the frame to the pulse carrier is a larger change, not done. Coarse default grid (12 points per
  FWHM): a 10 fs soliton with off-lattice carrier is 0.3 % off; exact with `snapCarrier: false`.
* CHANGELOG Changed: input pulse built at the requested carrier; grid-dependent fibre SPM phase error (0.1-0.4 %) gone. Fixed:
  `trackedAngularDispersion` of gratings and prisms; round aperture on an elliptical beam (exact integral); strongly phase-mismatched thick
  χ(2) crystal (≤ 0.5 rad mismatch phase per step); grid planner includes the memory of a delayed response, short fixed windows reported;
  element-level `selfSteepening` on fibres and bulk; fibre D(λ)/β2 tables use a cubic spline. Added: warning and `spectrum at non-positive
  frequency` loss booking when nonlinear broadening reaches ω ≤ 0. Docs: binaryPhase edge loss is a sampling effect (formula in transverse.md).
* testing.md: GridSolverTests (9 tests), BeamSourceTests +1.
* **B1 profile distortion at 2 µm / 10 fs is NOT fixed, only reported:** the SPM spectrum of a 1.5-cycle pulse at B = 3 reaches ω ≤ 0 (analytic
  signal has no negative frequencies). Three media.py checks stay KNOWN. Fix would need a carrier-resolved model.
* Not covered: delayed-response memory only for Raman (not generic `timeResponse` or gas revivals); decentre of round aperture still ignored; GPU
  untested; carrier offset in the carrier-resolved model only covered by existing tests.
* Paper follow-up: maker-1962 `repro_2.py` and shimizu-1967 `repro_1.py` now behave as expected: convert to documented regression checks or delete.

## Status: all four streams merged into dev/2026-11-consolidate (2026-11-08)
* C# suite 467 + schema fix green; component tests 1688 / 1691 (3 KNOWN = B1 2 µm / 10 fs); paper_check 11 of 12 green.
* **Regression to diagnose:** shimizu-1967 hard check "engine (CS2 dispersion off) = lowest-order model" went from 6e-4 to 5.8e-3 (tol 5e-3)
  after the merge (requested-carrier pulse or longer auto window?). Kida soft checks 2 -> 4.
* Next: four paper agents re-run the papers with the new features and add "How well the science matched" to every README;
  then delete `progress-2026-11-consolidate.md` and this plan's findings are folded into capabilities / roadmap / CHANGELOG / testing.md / PROJECT_STATUS.
