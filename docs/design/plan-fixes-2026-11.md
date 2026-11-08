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
