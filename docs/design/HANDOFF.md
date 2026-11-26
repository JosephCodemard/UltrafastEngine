# HANDOFF: coordinator session, 1.0.0 work packages

Internal page ([folder index](README.md)). The state at the end of the coordinator session of 2026-11-25/26, for the **next coordinator
("master") session**. Read with [coordinator.md](coordinator.md) (the procedure, the agent brief, the scripts in `build/wp/`) and section 0 of
[roadmap-internal.md](roadmap-internal.md) (every WP, its model, dependencies, status and the lanes). Update this page at the end of every
coordinator session; WP-level state lives in each worktree's own `HANDOFF.md`.

## Start the next session

* Open Claude Code in `UltrafastEngine/` (branch `dev/2026-11`, pushed, clean). Model for the coordinator: **Sonnet** (see "Models and
  usage" below), then paste:

  > You are the coordinator of the UltrafastEngine 1.0.0 work packages. The owner is away: never ask a question and never wait for an
  > answer; decide, record, continue. Read `docs/design/HANDOFF.md`, then `docs/design/coordinator.md` and section 0 of
  > `docs/design/roadmap-internal.md`. Run the loop of coordinator.md with **at most three WP agents at once**, each on the model its row
  > names (Haiku for `test-runner`), starting with "Next" below. Keep your context small: never read agent transcripts, full diffs or
  > generated files. When the queue is done or the day ends, do "End of the session" in coordinator.md and update this HANDOFF.md.

## Done in the session of 2026-11-25/26 (all merged into `dev/2026-11` and pushed)

| WP | result |
|---|---|
| A1, A2 | `mpc-session6` merged (GPU pulse- and beam-averaged B fix, main-lobe metrics); its review findings fixed (main lobe at the window edge, GPU test tolerance, docs) |
| B1, B2 | `docs/style.md` (voice, British spelling, terms, units, templates) and `build/style_lint.py` in CI with a baseline |
| C1a-f, C2a-f, C3 | every hand-written reference page audited against the code (`docs/design/notes/reference-audit-*.md`, about 400 rows) and rewritten to the reference template; consistency pass (504 links, one value per number, `llms.txt`, skills and the user pack). 41 code-side findings are listed for C4 |
| D1, D2 (Wavefronts, LaserSystems, Nonlinear, Diffraction, Branched) | CLI and Python tabs in example READMEs, `<!-- snippet -->` blocks run by `python/tests/test_readme_snippets.py`, 30 examples converted; numbers re-checked against fresh runs (changes listed below) |
| Q1 | mypy gate on `build.py`, `types.py`, `calc/` in CI |
| AG1, AG2, AG3 | user agent pack separate from the development rules; MCP tools `twin_design`, `twin_track`, `twin_init` (16 tools); Codex / Cursor transcript adapters (offline; no live scoring) |
| G0 | Apple OpenCL and PoCL found together on macOS: all 20 `GpuTests` run on PoCL (FP64) on the Mac; the Apple GPU is listed as single precision only |
| P1, P2 | glass in `beamSplitter` / `polarizingSplitter` (per port, 1e-12 against splitter plus plates); delayed-response window planning (a 10 ps revival was cut by the old plan) |
| fix-2 | the spatial-kerr number change explained (commit `8dfff5d`, unit-sum Raman kernel, right); analytic Kerr-lens and Raman-kernel tests |
| Z1, Z2 | own ZOS-API connection core (net48; ZOSPy read as a guide only); Zernike Standard coefficients from OpticStudio into a `wavefront` element (fake path tested only) |
| tooling | `build/wp/` (start, merge, regen, cleanup, roadmap dedupe), `docs/design/coordinator.md`, decisions E18-E20 |

Last full checkpoint (before C2b-d, C3, D2 Diffraction / Branched, Z2): build 0 errors; C# 697 passed, 0 failed (20 GPU tests on PoCL);
Python 518 passed with pyNLO; mkdocs strict, style, example and skill-sync checks, mypy all green. Run a fresh checkpoint first (Next, 1).

## In progress at the end of the session

Only **X1**: worktree `../uf-wp-x1` with a committed `HANDOFF.md`, not started; merge `dev/2026-11` into it first. fix-2 and P2 were
finished and merged at the end of the session (fix-2: spatial-kerr drift explained by the unit-sum Raman kernel of `8dfff5d`, analytic
Kerr-lens tests, the carrier-resolved path on the same Raman kernel; P2: narrow Raman revivals and `memoryS` held by the grid planner).

## Next (in this order; at most three at once; models as in the rows)

1. `test-runner` (Haiku) checkpoint on `dev/2026-11`.
2. **C4** (Sonnet): the 41 code-side findings of the audits (each file's "Code findings for WP C4"); includes the registry default of `system.version` (2 against 3), undeclared `topology`, `seedNoise` default, biaxial crystal class inference, oscillator keys read at the wrong level, `detuneDeg` in the XY plane, the memory estimate missing the state of nonlinear spatial elements (10x too low).
3. **fix-1** (Opus): a fed beam on another carrier gives different spectra in Sequential and Branched systems. **fix-3** (Opus):
   carrier-resolved SPM 33 % high when the envelope grid reaches zero frequency (decide with P11: fix or refuse).
4. **D2** groups left (Sonnet, one at a time): Diagnostics, Components, GettingStarted (rest), DigitalTwin, Fibres, Computing, Interop, AdaptiveOptics; then D6 (landing pages; PROJECT_STATUS says 60 example folders, there are 64), D3, D4, D5.
5. **X1** (Opus), then X2; **G1** then G2 (single precision on OpenCL, Apple GPU), G4, G6, G8; **P3-P7**, P12 (Sonnet), P8-P11, P13 (Opus); **W3**, W1, W2; **S1**, S2; **Z3-Z6** (Sonnet); Q2, Q3, Q4 (Sonnet).
6. V1 last: full `/verify`, CI green, pull request `dev/2026-11` to `main` (open, do not merge).

## Findings the owner should know

* **Numbers in example READMEs changed** to match fresh runs. Small (under 1 %) except: `Nonlinear/spatial-kerr` (B 1.4 to 1.55 rad,
  wavefront R -81 to -70 mm); cause found by fix-2: commit `8dfff5d` (2026-10-26) scaled the sampled Raman kernel to unit sum, which is
  right (the old kernel summed to 0.585 on that grid, so the Kerr index was 7.5 % low); the new values follow the exact second-moment law
  of a Gaussian beam in a Kerr medium. `LaserSystems/contrast-chain` correlator floor -58 to -56.5 dB (fresh run). `ner-measured-input`
  240 fs was never a run result (248.6 fs at every commit).
* **Zemax:** Z1 and Z2 are tested only against the fake OpticStudio. `HARDWARE-CHECKLIST.md` steps 3b and 4b settle the real ZOS-API
  member names, the ZERN operand layout, ENPD / EXPD and the OPD sign (`opdSign` defaults to -1 without a confirming source).
* **Memory estimate** leaves out the state of nonlinear spatial elements (can be 10x low): now stated in `memory.md`; fix in C4.
* **CI "Docs and example check"** failed on the D2 merges for two lines (fixed in `41aa926`): `build/example_check.py` split numbers
  with thousands separators, and `Nonlinear/supercontinuum`'s peak power differs by 0.2 % between Linux (CI) and macOS. The examples are
  checked on macOS by the WP agents but on Linux in CI: chaotic cases (large B) should not quote four digits. Also for C4: the CLI line of
  `LaserSystems/yb-laser-system` prints `T = 19,847,939,989.03 %` (output over the seed energy of an oscillator-seeded chain): print a
  gain or omit T when the input is a seed.
* Known limitation recorded: Raman on the cycle-averaged intensity in the carrier-resolved path drops the 2ω0 term (about 3 % of the
  delayed part for an 8 µm carrier in silica).

## For the Windows PC (RTX 3060 Ti, CUDA; OpticStudio if installed)

Run on `dev/2026-11` after pulling (`python` from `../.venv`, see `docs/design/history.md` working notes):

1. `dotnet build UltrafastEngine.sln -c Release`, then `dotnet tests\UltrafastEngine.Tests\bin\Release\net10.0\UltrafastEngine.Tests.dll GpuTests`
   (CUDA and OpenCL): checks the B-integral kernel of A1 on real devices (tolerance 1e-5), and that G0's multi-library OpenCL loading
   did not break Windows (`ultrafast devices`, `ultrafast devices --selftest`).
2. The mixed-precision tests and table (`GpuTests.MixedPrecision_*`, `docs/reference/speed.md`), and the benchmarks C2d removed for lack
   of provenance: `tools/bench` and the slow `FullPanel_1920x1080` test, recording machine, date and command in `speed.md`.
3. net48 under the real .NET Framework (CI uses Mono): `tests\UltrafastEngine.Tests\bin\Release\net48\UltrafastEngine.Tests.exe --slow`.
4. The Python suite on Windows (line ends, paths): `python python\tools\build_engine.py`, then `python -m pytest python\tests`.
5. OpticStudio: `plugins/zemax/HARDWARE-CHECKLIST.md` steps 3b (connection core) and 4b (Zernike read, OPD sign).
6. Later, when built: G3 (single-precision cuFFT plan) and G10 (CUDA parity and speed for G1-G8).

## Models and usage

The 5-hour limit was hit four times with up to twelve agents, most of them Opus. What changes:

* **Coordinator on Sonnet.** Its work is mechanical (briefs, merges, checks). When a decision needs depth (a physics disagreement, a
  design choice), it asks a short read-only Opus agent (`physics-reviewer` or `Plan`) instead of running on Opus itself. Claude Code's
  `opusplan` model setting (Opus while planning, Sonnet while executing) is an alternative for the coordinator.
* **WP agents: Sonnet by default, Opus only for the rows that say so** (device kernels and precision, the hard physics items: G1, G4, G5,
  G7, G9, P8-P11, P13, W1, W3, X1, X2, S1, fix-1). Every ⚛ WP ends with an Opus `physics-reviewer` pass (short, read-only).
* **Haiku** for `test-runner` checkpoints (pass `model: "haiku"` in the Agent call) and other runs that only report counts.
* At most three WP agents at once; a fresh agent per WP (from `HANDOFF.md`) rather than resuming a long one; agents commit WIP with a
  `HANDOFF.md` at milestones so a usage stop loses nothing.

## Process lessons

* Every merge conflicts in the validation matrix (new tests): `merge.sh` takes the WP side and asks for `regen.sh`; run it and commit.
* Union resolution of the roadmap kept both versions of a changed row; `build/wp/dedupe_roadmap.py` now keeps the done one.
* Reference pages rewritten in parallel with other WPs conflict: `merge.sh` undoes such a merge and the WP agent merges `dev/2026-11`
  into its branch (worked every time).
* The tool permission classifier refused a forced removal of worktrees with work in them; `cleanup.sh` removes only merged, clean ones.
* An agent with a background job of its own may send its final report several times; ignore repeats.
