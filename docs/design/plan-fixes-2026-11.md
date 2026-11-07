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
