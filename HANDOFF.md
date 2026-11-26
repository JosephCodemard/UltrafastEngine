# HANDOFF -- WP Z2

Work package **Z2** of `docs/design/roadmap-internal.md` section 0. Worktree `/Users/josephcodemard/Documents/Projects/UltrafastEngine/uf-wp-z2`, branch `dev/wp-z2` (from `dev/2026-11`).
Written 2026-11-26 by the coordinator after the agents stopped at a usage limit.

## Status

Code done and building (WIP commit): `IOpticStudio.ReadZernikeStandard` + `ZernikeStandardData`; real impl in `ZosAnalyses.cs`
(temporary ZERN rows, ENPD/EXPD; stubs gained ZERN/ENPD/EXPD); fake (`FakeOpticStudio.ZernikeNollWaves`), record/replay; sample JSON
`zernike`; export block `zernike` (`ZernikeExport`, `ExportConfig.ToSamplerOptions` replaces two duplicate option builders);
`Sampler.ReadZernike` at the carrier; `SystemBuilder.BuildWavefront` (conversion comment there; opdSign default -1, piston dropped,
radiusM = ENPD/2).

## Next steps

1. (done) Tests in `ZemaxTests`: 3 Zernike_* tests pass.
2. Docs: `docs/plugins/zemax.md` section "Zernike aberrations", HARDWARE-CHECKLIST step 4b + assumptions, example export.json, docs/testing.md rows.
3. CHANGELOG, history, roadmap row, delete this file, final commit.

## Notes from the original brief

Zernike aberrations from OpticStudio into an engine wavefront element. **Never touch the ZOS-API DLLs** (`plugins/zemax/src/UltrafastEngine.Zemax/lib/`);
ZOSPy (MIT) may be read online as a guide only (how it runs Zernike Standard Coefficients), never shipped or required; attribute closely
ported logic (decision E19). Read `plugins/zemax/README.md`, `HARDWARE-CHECKLIST.md`, `docs/plugins/zemax.md`, the Sampling project
(`IOpticStudio.cs`, `Sampler.cs`, `SystemBuilder.cs`, `FakeOpticStudio.cs`, `ZemaxSample.cs`, `ZosConnectionCore.cs`) and the Windows
project (`ZosConnection.cs`, `ZosSystemAccess.cs`, `ZosAnalyses.cs`). IOpticStudio gains a Zernike read (field, wavelength,
normalisation radius); real implementation in `ZosAnalyses.cs` (extend our compile-only stubs); fake implementation; sample format carries
the coefficients; `SystemBuilder` emits the engine wavefront element with the conversion from Zernike Standard (Noll order, waves) to the
engine's convention (find it in `Optics/`), stated in a comment and the docs. Test on the fake path: pure defocus and astigmatism give the
analytic phase at pupil points to 1e-12. Add the owner's OpticStudio steps to `HARDWARE-CHECKLIST.md`. Filter `Zemax`. Model: Opus.

## How to work (all WPs)

* Read `CLAUDE.md`, section 0.1 of `docs/design/roadmap-internal.md` and this WP's row, then only the files named here and what grep finds.
  Do not read generated files (`schemas/*.json`, `python/src/ultrafast/types.py`, `docs/reference/options.md`, `python-api.md`,
  `validation-matrix.md`, `materials-catalogue.md`); grep them for anchors only. Never read `../Experiments/`.
* Work only in this worktree. Python with numpy / mkdocs: `/Users/josephcodemard/Documents/Projects/UltrafastEngine/UltrafastEngine/.venv/bin/python` (system `python3` lacks them); mypy:
  `/Users/josephcodemard/Documents/Projects/UltrafastEngine/.venv/bin/python -m mypy`. For Python tests run
  `<venv python> python/tools/build_engine.py` first and export `ULTRAFAST_LIBRARY` / `PYTHONPATH` in the same shell command if needed.
* Build `dotnet build UltrafastEngine.sln -c Release` (0 errors, net10.0 and net48). Tests:
  `dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll <filter>` piped through `grep -E "FAIL|passed" | grep -v "^   at"`.
  Regenerate generated files with `build/wp/regen.sh` when a declaration, Python builder or test attribute changed.
* Docs follow `docs/style.md`; `<venv python> -m mkdocs build --strict` 0 warnings; `<venv python> build/style_lint.py` green.
* The owner is away: never ask questions; decide, and record the decision in the report and the history paragraph.
* **If you stop before finishing**, update this HANDOFF.md (what is done, what is next, anything learnt) and commit everything as
  `WIP <id>: ...`. **When finished**: CHANGELOG.md line (top section); a paragraph in `docs/design/history.md` headed
  `## Stage XX -- WP <id>: ...` (literally XX); the WP's status in `roadmap-internal.md` set to `done <date>`; then **delete HANDOFF.md** in
  the final commit (`build/wp/merge.sh` refuses a branch that still has it); commit message ends with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Do not push or merge (the coordinator does). Report in under 200 words.
